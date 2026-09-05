namespace Keysharp.Internals
{
	/// <summary>Keeps a subscription available through fallback and bounded-backoff retries.</summary>
	internal sealed class RecoveringSubscription : IDisposable
	{
		private const int MaximumAttempts = 3;
		private readonly object sync = new();
		private readonly Func<Action<Exception>, IDisposable> subscribePreferred;
		private readonly Func<IDisposable> subscribeFallback;
		private readonly Func<bool> preferredAvailable;
		private readonly Action<bool> stateChanged;
		private readonly bool keepFallbackWarm;
		private readonly int retryIntervalMs;
		private readonly System.Threading.Timer timer;
		private IDisposable preferred, fallback, availability;
		private object attempt;
		private int failures;
		private bool creatingFallback, disposed;

		internal RecoveringSubscription(Func<Action<Exception>, IDisposable> subscribePreferred,
			Func<IDisposable> subscribeFallback, Func<bool> preferredAvailable,
			Func<Action, IDisposable> subscribeAvailability, Action<bool> stateChanged = null,
			bool keepFallbackWarm = false, int retryIntervalMs = 1000)
		{
			this.subscribePreferred = subscribePreferred;
			this.subscribeFallback = subscribeFallback;
			this.preferredAvailable = preferredAvailable;
			this.stateChanged = stateChanged;
			this.keepFallbackWarm = keepFallbackWarm;
			this.retryIntervalMs = Math.Max(1, retryIntervalMs);
			timer = new System.Threading.Timer(_ => TryAttachPreferred(), null, Timeout.Infinite, Timeout.Infinite);
			availability = subscribeAvailability?.Invoke(() => PreferredFailed(null));
		}

		internal bool IsPreferred { get { lock (sync) return preferred != null; } }

		internal static RecoveringSubscription Create(Func<Action<Exception>, IDisposable> preferred,
			Func<IDisposable> fallback, Func<bool> available, Func<Action, IDisposable> availability)
		{
			var subscription = new RecoveringSubscription(preferred, fallback, available, availability);
			subscription.Start();
			return subscription;
		}

		internal void Start()
		{
			if (keepFallbackWarm)
				EnsureFallback();

			if (!TryAttachPreferred())
				EnsureFallback();
		}

		internal bool TryAttachPreferred()
		{
			object candidate;
			lock (sync)
			{
				if (disposed || attempt != null)
					return preferred != null;

				attempt = candidate = new object();
			}

			IDisposable subscription = null, retireFallback = null;
			try
			{
				if (preferredAvailable?.Invoke() != false)
					subscription = subscribePreferred?.Invoke(_ => PreferredFailed(candidate));
			}
			catch { }

			lock (sync)
			{
				// One identity covers setup and the live stream. Failure or an owner change clears it,
				// so a result arriving after either cannot revive the retired subscription.
				if (!disposed && ReferenceEquals(attempt, candidate))
				{
					preferred = subscription;
					subscription = null;

					if (preferred == null)
					{
						attempt = null;
						ScheduleRetryLocked();
					}
					else
					{
						failures = 0;
						timer.Change(Timeout.Infinite, Timeout.Infinite);
						if (!keepFallbackWarm) { retireFallback = fallback; fallback = null; }
					}
				}
			}

			subscription?.Dispose();
			retireFallback?.Dispose();
			var attached = IsPreferred;
			if (attached)
				stateChanged?.Invoke(true);
			return attached;
		}

		private void PreferredFailed(object failed)
		{
			IDisposable retire;
			lock (sync)
			{
				if (disposed || (failed != null && !ReferenceEquals(attempt, failed)))
					return;

				retire = preferred;
				preferred = null;
				attempt = null;
				if (failed == null)
					failures = 0;
				else
					ScheduleRetryLocked();
			}

			retire?.Dispose();
			stateChanged?.Invoke(false);
			EnsureFallback();
			if (failed == null)
				TryAttachPreferred();
		}

		private void ScheduleRetryLocked()
		{
			failures = Math.Min(MaximumAttempts, failures + 1);

			if (failures < MaximumAttempts)
				timer.Change((int)Math.Min(int.MaxValue, (long)retryIntervalMs << failures), Timeout.Infinite);
		}

		private void EnsureFallback()
		{
			lock (sync)
			{
				if (disposed || fallback != null || creatingFallback || (!keepFallbackWarm && preferred != null))
					return;
				creatingFallback = true;
			}

			IDisposable candidate;
			try { candidate = subscribeFallback?.Invoke(); }
			catch { candidate = null; }

			lock (sync)
			{
				creatingFallback = false;
				if (!disposed && (keepFallbackWarm || preferred == null))
				{
					fallback = candidate;
					candidate = null;
				}
			}
			candidate?.Dispose();
		}

		public void Dispose()
		{
			IDisposable p, f, a;
			lock (sync)
			{
				if (disposed) return;
				disposed = true;
				attempt = null;
				p = preferred; f = fallback; a = availability;
				preferred = fallback = availability = null;
			}
			timer.Dispose();
			a?.Dispose(); p?.Dispose(); f?.Dispose();
		}
	}
}
