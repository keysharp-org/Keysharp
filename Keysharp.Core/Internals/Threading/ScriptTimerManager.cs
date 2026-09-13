using Keysharp.Builtins;
using Keysharp.Internals.Scripting;
using Keysharp.Runtime;

namespace Keysharp.Internals.Threading
{
	internal sealed class ScriptTimerState : CallbackRegistration
	{
		internal ScriptTimerState(object callback, ScriptEventScheduler ownerScheduler)
			: base(callback, ownerScheduler, true)
		{
		}

		internal long PeriodMs { get; set; }
		internal long NextDueTick { get; set; }
		internal bool RunOnce { get; set; }
		internal bool Queued { get; set; }
		internal bool WakeRequested { get; set; }
		internal int RunningCount { get; set; }
	}

	internal sealed class ScriptTimerKeyComparer : IEqualityComparer<(object Callback, ScriptEventScheduler OwnerScheduler)>
	{
		internal static readonly ScriptTimerKeyComparer Instance = new();

		private ScriptTimerKeyComparer()
		{
		}

		public bool Equals((object Callback, ScriptEventScheduler OwnerScheduler) x, (object Callback, ScriptEventScheduler OwnerScheduler) y)
			=> Functions.SameCallback(x.Callback, y.Callback) && ReferenceEquals(x.OwnerScheduler, y.OwnerScheduler);

		public int GetHashCode((object Callback, ScriptEventScheduler OwnerScheduler) obj)
		{
			unchecked
			{
				return (Functions.CallbackHash(obj.Callback) * 397)
					^ (obj.OwnerScheduler != null ? RuntimeHelpers.GetHashCode(obj.OwnerScheduler) : 0);
			}
		}
	}

	internal sealed class ScriptTimerManager : IDisposable
	{
		private readonly object gate = new();
		private readonly Dictionary<(object Callback, ScriptEventScheduler OwnerScheduler), ScriptTimerState> timers = new(ScriptTimerKeyComparer.Instance);
		private readonly AutoResetEvent wakeEvent = new(false);
		private Thread timerThread;
		private bool disposed;

		internal int Count
		{
			get
			{
				lock (gate)
					return timers.Count;
			}
		}

		internal bool IsEmpty
		{
			get
			{
				lock (gate)
					return timers.Count == 0;
			}
		}

		/// <summary>Whether any timer is enabled, which is what keeps a script running, as AHK counts only enabled
		/// timers. A run-once timer is disabled as its thread starts, so it no longer counts at that thread's end.</summary>
		internal bool AnyEnabled
		{
			get
			{
				lock (gate)
				{
					foreach (var timer in timers.Values)
						if (timer.IsActive)
							return true;

					return false;
				}
			}
		}

		internal ScriptTimerState[] GetSnapshot()
		{
			lock (gate)
			{
				if (timers.Count == 0)
					return [];

				return [.. timers.Values];
			}
		}

		internal ScriptTimerState Find(object callback, ScriptEventScheduler ownerScheduler)
		{
			if (callback == null)
				return null;

			lock (gate)
			{
				return timers.TryGetValue((callback, ownerScheduler), out var timer) ? timer : null;
			}
		}

		internal ScriptTimerState Upsert(object callback, ScriptEventScheduler ownerScheduler, long periodMs, bool runOnce, long priority)
		{
			ArgumentNullException.ThrowIfNull(callback);

			ScriptTimerState timer;

			lock (gate)
			{
				ThrowIfDisposed();

				var key = (callback, ownerScheduler);

				if (!timers.TryGetValue(key, out timer))
				{
					timer = new ScriptTimerState(callback, ownerScheduler);
					timers.Add(key, timer);
				}

				//AHK stores a period of 1 as 0 so the timer may run within the same tick instead of waiting for the next
				//one (script.cpp SetTimer), which is what makes SetTimer(fn, -1) followed by Sleep(-1) fire. SetTimer
				//has already turned a period of 0 into "disable", so 1 is the only value needing this.
				timer.PeriodMs = periodMs == 1 ? 0 : periodMs;
				timer.NextDueTick = Environment.TickCount64 + timer.PeriodMs;
				timer.WakeRequested = false;
				timer.Priority = priority;
				timer.RunOnce = runOnce;
				timer.SetActive(true);
				EnsureThreadStarted();
			}

			Wake();
			ownerScheduler?.WakeForTimerCheck();
			return timer;
		}

		internal void ResetTimer(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.NextDueTick = Environment.TickCount64 + timer.PeriodMs;
				timer.WakeRequested = false;
				timer.SetActive(true);
			}

			Wake();
			timer.OwnerScheduler?.WakeForTimerCheck();
		}

		internal void UpdatePriority(ScriptTimerState timer, long priority)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.Priority = priority;
			}

			timer.OwnerScheduler?.WakeForTimerCheck();
		}

		internal void DisableOrDelete(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.SetActive(false);

				if (!timer.Queued && timer.RunningCount == 0)
				{
					_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
					ClearTimerState(timer);
				}
			}

			Wake();
			timer.OwnerScheduler?.WakeForTimerCheck();
		}

		internal void MarkCallbackStarted(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.Queued = false;
				timer.WakeRequested = false;

				if (timer.RunOnce)
					timer.SetActive(false);
				else
					timer.NextDueTick = Environment.TickCount64 + timer.PeriodMs;

				timer.RunningCount++;
			}

			Wake();
		}

		internal void MarkCallbackFinished(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			bool wake;

			lock (gate)
			{
				if (disposed)
					return;

				if (timer.RunningCount > 0)
					timer.RunningCount--;

				// Preserve a future wait: restarting it at completion can add another coarse timer interval.
				wake = timer.RunningCount == 0 && (!timer.IsActive || Environment.TickCount64 >= timer.NextDueTick);

				if (!timer.IsActive && timer.RunningCount == 0 && !timer.Queued)
				{
					_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
					ClearTimerState(timer);
				}
			}

			if (wake)
				Wake();
		}

		internal void ReleaseQueuedTimer(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.Queued = false;
				timer.WakeRequested = false;

				if (!timer.IsActive && timer.RunningCount == 0)
				{
					_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
					ClearTimerState(timer);
				}
			}

			Wake();
		}

		internal bool RemoveOwned(ScriptEventScheduler ownerScheduler)
		{
			if (ownerScheduler == null)
				return false;

			var removed = false;

			lock (gate)
			{
				if (disposed)
					return false;

				foreach (var timer in timers.Values.ToArray())
				{
					if (!ReferenceEquals(timer.OwnerScheduler, ownerScheduler))
						continue;

					timer.SetActive(false);

					if (timer.RunningCount == 0)
					{
						_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
						ClearTimerState(timer);
					}

					removed = true;
				}
			}

			if (removed)
				Wake();

			return removed;
		}

		internal void Clear()
		{
			lock (gate)
			{
				if (disposed)
					return;

				foreach (var timer in timers.Values)
					ClearTimerState(timer);

				timers.Clear();
			}

			Wake();
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				foreach (var timer in timers.Values)
					ClearTimerState(timer);
				timers.Clear();
			}

			Wake();

			if (timerThread != null && !ReferenceEquals(Thread.CurrentThread, timerThread))
				timerThread.Join();

			wakeEvent.Dispose();
		}

		// Notify the owner once per due callback. Queued callbacks wait for scheduler admission without polling.
		private void Run()
		{
			var dueOwners = new HashSet<ScriptEventScheduler>();

			while (true)
			{
				dueOwners.Clear();
				var waitMs = Timeout.Infinite;

				lock (gate)
				{
					if (disposed)
						return;

					var now = Environment.TickCount64;
					long nextDueTick = long.MaxValue;

					foreach (var timer in timers.Values)
					{
						if (!timer.IsActive || timer.Queued || timer.WakeRequested)
							continue;

						// Plan the next wake from callback start; an overrun waits for callback completion.
						if (timer.RunningCount > 0 && now >= timer.NextDueTick)
							continue;

						if (now < timer.NextDueTick)
						{
							if (timer.NextDueTick < nextDueTick)
								nextDueTick = timer.NextDueTick;
							continue;
						}

						if (timer.OwnerScheduler != null)
						{
							timer.WakeRequested = true;
							_ = dueOwners.Add(timer.OwnerScheduler);
						}
					}

					if (nextDueTick != long.MaxValue)
					{
						var delay = nextDueTick - now;
						waitMs = delay >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)delay);
					}
				}

				foreach (var owner in dueOwners)
					owner.WakeForTimerCheck();

				try
				{
					_ = wakeEvent.WaitOne(waitMs);
				}
				catch (ObjectDisposedException)
				{
					return;
				}
			}
		}

		private void EnsureThreadStarted()
		{
			if (timerThread != null)
				return;

			timerThread = new Thread(Run)
			{
				IsBackground = true,
				Name = "Keysharp Script Timer Manager"
			};
			timerThread.Start();
		}

		// The due-check ("should this timer fire"), run from the owner scheduler's pump (EnqueueDueTimers) on the owner
		// thread. Marks this scheduler's due timers Queued and returns them so the caller enqueues them via EnqueueTimer.
		// Reuses the caller's buffer to avoid per-pump allocation (only the owning thread calls this for a given scheduler).
		internal List<ScriptTimerState> TakeDueTimers(ScriptEventScheduler scheduler, List<ScriptTimerState> buffer)
		{
			buffer.Clear();

			lock (gate)
			{
				if (disposed)
					return buffer;

				var now = Environment.TickCount64;

				foreach (var timer in timers.Values)
				{
					if (!timer.IsActive || timer.Queued || timer.RunningCount > 0)
						continue;

					if (!ReferenceEquals(timer.OwnerScheduler, scheduler) || now < timer.NextDueTick)
						continue;

					timer.Queued = true;
					timer.WakeRequested = false;
					buffer.Add(timer);
				}
			}

			if (buffer.Count != 0)
				Wake();

			return buffer;
		}

		private void Wake()
		{
			try
			{
				_ = wakeEvent.Set();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(ScriptTimerManager));
		}

		private static void ClearTimerState(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			timer.Queued = false;
			timer.WakeRequested = false;
			timer.RunningCount = 0;
			timer.Set(null, null, false);
		}
	}
}
