namespace Keysharp.Internals
{
	/// <summary>Owns one replaceable service. Creation backs off and retired values outlive active borrowers.</summary>
	internal sealed class RecoverableService<T> : IDisposable where T : class
	{
		internal sealed class Entry(T value)
		{
			internal readonly T Value = value;
			internal int Borrowers;
			internal bool Retired;
		}

		internal sealed class Lease(RecoverableService<T> owner, Entry entry) : IDisposable
		{
			private RecoverableService<T> owner = owner;
			private Entry entry = entry;
			internal T Value => entry?.Value;

			public void Dispose()
			{
				var service = Interlocked.Exchange(ref owner, null);
				service?.Release(Interlocked.Exchange(ref entry, null));
			}
		}

		private readonly object sync = new();
		private readonly Func<T> factory;
		private readonly Action<T> disposer;
		private readonly RetryGate retry;
		private Entry current;
		private Exception lastError;
		private long version;
		private bool creating, disposed;

		internal RecoverableService(Func<T> factory, Action<T> disposer = null, TimeProvider timeProvider = null,
			int maximumAttempts = 3, TimeSpan? initialRetryDelay = null, TimeSpan? maximumRetryDelay = null)
		{
			this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
			this.disposer = disposer ?? (value => (value as IDisposable)?.Dispose());
			retry = new RetryGate(timeProvider, maximumAttempts, initialRetryDelay, maximumRetryDelay, retryIndefinitely: true);
		}

		internal Exception LastError { get { lock (sync) return lastError; } }
		internal int FailureCount => retry.FailureCount;

		internal Lease TryAcquire()
		{
			long attemptVersion;
			RetryGate.Attempt attempt;
			lock (sync)
			{
				if (disposed || creating) return null;
				if (current != null) return Borrow(current);
				attempt = retry.TryBegin();
				if (attempt == null) return null;
				creating = true;
				attemptVersion = version;
			}

			T created = null;
			Exception error = null;
			try { created = factory(); } catch (Exception ex) { error = ex; }

			T discard = null;
			Lease lease = null;
			lock (sync)
			{
				creating = false;
				if (disposed || version != attemptVersion || current != null) discard = created;
				else if (created == null)
				{
					lastError = error;
					attempt.Fail(error);
				}
				else
				{
					current = new Entry(created);
					lease = Borrow(current);
					attempt.Succeed();
					lastError = null;
				}
				attempt.Dispose();
			}
			Dispose(discard);
			return lease;
		}

		internal void Invalidate(T value, Exception error = null)
		{
			T retire = null;
			lock (sync)
			{
				if (disposed || current == null || !ReferenceEquals(current.Value, value)) return;
				retire = Retire();
				version++;
				retry.Invalidate();
				lastError = error;
			}
			Dispose(retire);
		}

		internal void Rearm()
		{
			lock (sync)
			{
				if (disposed) return;
				version++; retry.Rearm(); lastError = null;
			}
		}

		internal void Suspend()
		{
			T retire;
			lock (sync)
			{
				if (disposed) return;
				version++; retry.Suspend(); retire = Retire();
			}
			Dispose(retire);
		}

		private Lease Borrow(Entry entry) { entry.Borrowers++; return new Lease(this, entry); }

		private T Retire()
		{
			var entry = current;
			current = null;
			if (entry == null) return null;
			entry.Retired = true;
			return entry.Borrowers == 0 ? entry.Value : null;
		}

		private void Release(Entry entry)
		{
			T retire = null;
			lock (sync)
				if (entry != null && --entry.Borrowers == 0 && entry.Retired) retire = entry.Value;
			Dispose(retire);
		}

		private void Dispose(T value) { try { if (value != null) disposer(value); } catch { } }

		public void Dispose()
		{
			T retire;
			lock (sync)
			{
				if (disposed) return;
				disposed = true; version++; retry.Suspend(); retire = Retire();
			}
			Dispose(retire);
		}
	}
}
