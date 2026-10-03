namespace Keysharp.Internals
{
	/// <summary>
	/// Serializes attempts with backoff. A bounded burst waits for <see cref="Rearm"/> unless ongoing retries
	/// are requested; <see cref="Suspend"/> represents a known-stable absence.
	/// </summary>
	internal sealed class RetryGate
	{
		internal sealed class Attempt : IDisposable
		{
			private RetryGate owner;
			private readonly long version;
			private bool succeeded;

			internal Attempt(RetryGate owner, long version)
			{
				this.owner = owner;
				this.version = version;
			}

			internal void Succeed() => succeeded = true;

			internal void Fail(Exception exception = null) => succeeded = false;

			public void Dispose()
			{
				var gate = Interlocked.Exchange(ref owner, null);
				gate?.Complete(version, succeeded);
			}
		}

		private readonly object sync = new();
		private readonly TimeProvider timeProvider;
		private readonly int maximumAttempts;
		private readonly TimeSpan initialRetryDelay;
		private readonly TimeSpan maximumRetryDelay;
		private readonly bool retryIndefinitely;
		private long version;
		private long lastFailureTimestamp;
		private int failures;
		private bool attempting;
		private bool suspended;

		internal RetryGate(TimeProvider timeProvider = null, int maximumAttempts = 3,
			TimeSpan? initialRetryDelay = null, TimeSpan? maximumRetryDelay = null, bool retryIndefinitely = false)
		{
			this.timeProvider = timeProvider ?? TimeProvider.System;
			this.maximumAttempts = Math.Max(1, maximumAttempts);
			this.initialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(250);
			this.maximumRetryDelay = maximumRetryDelay ?? TimeSpan.FromSeconds(5);
			this.retryIndefinitely = retryIndefinitely;
		}

		internal int FailureCount
		{
			get { lock (sync) return failures; }
		}

		internal Attempt TryBegin()
		{
			lock (sync)
			{
				if (attempting || suspended || (!retryIndefinitely && failures >= maximumAttempts) || RetryDelayRemaining())
					return null;

				attempting = true;
				return new Attempt(this, version);
			}
		}

		internal void Rearm()
		{
			lock (sync)
			{
				version++;
				attempting = false;
				failures = 0;
				lastFailureTimestamp = 0;
				suspended = false;
			}
		}

		internal void Suspend()
		{
			lock (sync)
			{
				version++;
				attempting = false;
				suspended = true;
			}
		}

		internal void Invalidate()
		{
			lock (sync)
			{
				version++;
				attempting = false;
				RecordFailure();
			}
		}

		private bool RetryDelayRemaining()
		{
			if (failures == 0)
				return false;

			var shift = Math.Min(20, failures - 1);
			var delayMs = retryIndefinitely && failures >= maximumAttempts ? maximumRetryDelay.TotalMilliseconds
				: Math.Min(maximumRetryDelay.TotalMilliseconds, initialRetryDelay.TotalMilliseconds * (1L << shift));
			return timeProvider.GetElapsedTime(lastFailureTimestamp, timeProvider.GetTimestamp())
				< TimeSpan.FromMilliseconds(delayMs);
		}

		private void Complete(long attemptVersion, bool succeeded)
		{
			lock (sync)
			{
				if (attemptVersion != version || !attempting)
					return;

				attempting = false;

				if (succeeded)
				{
					failures = 0;
					lastFailureTimestamp = 0;
				}
				else
				{
					RecordFailure();
				}
			}
		}

		private void RecordFailure()
		{
			failures = Math.Min(maximumAttempts, failures + 1);
			lastFailureTimestamp = timeProvider.GetTimestamp();
		}
	}
}
