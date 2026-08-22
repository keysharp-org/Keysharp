using Keysharp.Builtins;
using Keysharp.Internals.Scripting;
using Keysharp.Runtime;

namespace Keysharp.Internals.Threading
{
	internal sealed class ScriptTimerState : CallbackRegistration
	{
		internal ScriptTimerState(KeysharpFunc callback, ScriptEventScheduler ownerScheduler)
			: base(callback, ownerScheduler, true)
		{
		}

		internal long PeriodMs { get; set; }
		internal long NextDueTick { get; set; }
		internal bool RunOnce { get; set; }
		internal bool Enabled { get; set; }
		internal bool Queued { get; set; }
		internal int RunningCount { get; set; }
		internal bool DeletePending { get; set; }
	}

	internal sealed class ScriptTimerKeyComparer : IEqualityComparer<(KeysharpFunc Callback, ScriptEventScheduler OwnerScheduler)>
	{
		internal static readonly ScriptTimerKeyComparer Instance = new();

		private ScriptTimerKeyComparer()
		{
		}

		public bool Equals((KeysharpFunc Callback, ScriptEventScheduler OwnerScheduler) x, (KeysharpFunc Callback, ScriptEventScheduler OwnerScheduler) y)
			=> Equals(x.Callback, y.Callback) && ReferenceEquals(x.OwnerScheduler, y.OwnerScheduler);

		public int GetHashCode((KeysharpFunc Callback, ScriptEventScheduler OwnerScheduler) obj)
		{
			unchecked
			{
				return ((obj.Callback?.GetHashCode() ?? 0) * 397)
					^ (obj.OwnerScheduler != null ? RuntimeHelpers.GetHashCode(obj.OwnerScheduler) : 0);
			}
		}
	}

	internal sealed class ScriptTimerManager : IDisposable
	{
		// Backoff before re-checking a run-once timer that was deferred because its priority was below the current
		// thread's (it has no period of its own). Small so it fires promptly once the higher-priority thread ends.
		private const long PriorityBlockedRetryMs = 15;
		private readonly object gate = new();
		private readonly Dictionary<(KeysharpFunc Callback, ScriptEventScheduler OwnerScheduler), ScriptTimerState> timers = new(ScriptTimerKeyComparer.Instance);
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

		internal ScriptTimerManager()
		{
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

		internal ScriptTimerState Find(KeysharpFunc callback, ScriptEventScheduler ownerScheduler)
		{
			if (callback == null)
				return null;

			lock (gate)
			{
				return timers.TryGetValue((callback, ownerScheduler), out var timer) ? timer : null;
			}
		}

		internal ScriptTimerState Upsert(KeysharpFunc callback, ScriptEventScheduler ownerScheduler, long periodMs, bool runOnce, long priority)
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
				timer.Priority = priority;
				timer.RunOnce = runOnce;
				timer.Enabled = true;
				timer.DeletePending = false;
				timer.SetActive(true);
				EnsureThreadStarted();
			}

			Wake();
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
				timer.Enabled = true;
				timer.DeletePending = false;
				timer.SetActive(true);
			}

			Wake();
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
		}

		internal void DisableOrDelete(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.Enabled = false;
				timer.SetActive(false);

				if (timer.Queued || timer.RunningCount > 0)
				{
					timer.DeletePending = true;
					return;
				}

				_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
				ClearTimerState(timer);
			}

			Wake();
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

				if (timer.RunOnce)
				{
					timer.Enabled = false;
					timer.DeletePending = true;
					timer.SetActive(false);
				}
				else
				{
					timer.NextDueTick = Environment.TickCount64 + timer.PeriodMs;
				}

				timer.RunningCount++;
			}
		}

		// A due timer whose priority is lower than the current thread's cannot run yet (a lower-priority thread may
		// not interrupt a higher-priority one). Re-arm it WITHOUT consuming it — so a run-once timer isn't destroyed
		// (MarkCallbackStarted would set DeletePending) but instead fires once the higher-priority thread ends. A
		// run-once retries after a short backoff (it has no period); a periodic waits its normal period. The forward
		// NextDueTick also keeps the pump from re-serving it in a tight loop.
		internal void DeferPriorityBlocked(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				timer.Queued = false;
				timer.NextDueTick = Environment.TickCount64 + (timer.RunOnce ? PriorityBlockedRetryMs : timer.PeriodMs);
			}

			Wake();
		}

		internal void MarkCallbackFinished(ScriptTimerState timer)
		{
			if (timer == null)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				if (timer.RunningCount > 0)
					timer.RunningCount--;

				if (!timer.Enabled && timer.DeletePending && timer.RunningCount == 0 && !timer.Queued)
				{
					_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
					ClearTimerState(timer);
				}
			}

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

				if (!timer.Enabled && timer.DeletePending && timer.RunningCount == 0)
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

					timer.Enabled = false;
					timer.SetActive(false);

					if (timer.RunningCount > 0)
					{
						timer.DeletePending = true;
						removed = true;
						continue;
					}

					_ = timers.Remove((timer.Callback, timer.OwnerScheduler));
					ClearTimerState(timer);
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

		// This thread is a WAKER: it does not decide which timers fire or enqueue anything. It finds the next due time
		// and, when a timer is due, wakes that timer's owner scheduler via WakeForTimerCheck. The scheduler's pump then
		// runs the due-check itself (EnqueueDueTimers -> EnqueueTimer). Timers that are already Queued or running are
		// skipped here, so once the pump has enqueued a due timer this loop stops waking for it.
		private void Run()
		{
			var dueOwners = new HashSet<ScriptEventScheduler>();

			while (true)
			{
				dueOwners.Clear();
				var waitMs = Timeout.Infinite;
				var anyDue = false;

				lock (gate)
				{
					if (disposed)
						return;

					var now = Environment.TickCount64;
					long nextDueTick = long.MaxValue;

					foreach (var timer in timers.Values)
					{
						if (!timer.Enabled || timer.Queued || timer.RunningCount > 0)
							continue;

						if (now < timer.NextDueTick)
						{
							if (timer.NextDueTick < nextDueTick)
								nextDueTick = timer.NextDueTick;
							continue;
						}

						anyDue = true;

						if (timer.OwnerScheduler != null)
							_ = dueOwners.Add(timer.OwnerScheduler);
					}

					if (anyDue)
						// The wake only triggers the pump; until its EnqueueDueTimers marks these Queued they stay due here,
						// so re-evaluate after a short wait rather than busy-spinning. Once Queued they're skipped above.
						waitMs = 1;
					else if (nextDueTick != long.MaxValue)
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
					if (!timer.Enabled || timer.Queued || timer.RunningCount > 0)
						continue;

					if (!ReferenceEquals(timer.OwnerScheduler, scheduler) || now < timer.NextDueTick)
						continue;

					timer.Queued = true;
					buffer.Add(timer);
				}
			}

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

			timer.Enabled = false;
			timer.Queued = false;
			timer.RunningCount = 0;
			timer.DeletePending = false;
			timer.Set(null, null, false);
		}
	}
}
