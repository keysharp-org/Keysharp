namespace Keysharp.Internals.Input
{
	/// <summary>Admits calls to one provider and releases it after the last call admitted before retirement.</summary>
	internal class ProviderLease<T>(T provider, string kind) where T : class
	{
		// The sign bit retires admission; the remaining bits count calls still using the provider.
		private int calls;

		internal T Provider { get; private set; } = provider;

		internal bool IsRetired => Volatile.Read(ref calls) < 0;

		internal bool TryEnter()
		{
			var state = Volatile.Read(ref calls);

			while (state >= 0 && state < int.MaxValue)
			{
				var observed = Interlocked.CompareExchange(ref calls, state + 1, state);
				if (observed == state) return true;
				state = observed;
			}

			return false;
		}

		/// <summary>Ends an admitted call, returning true when that released the provider.</summary>
		internal bool Exit() => Interlocked.Decrement(ref calls) == int.MinValue && Release();

		/// <summary>Stops admission, returning true when no call was active and the provider was released.</summary>
		// The last call releases the provider, so retirement never waits for a blocked driver call.
		internal bool Retire() => Interlocked.Or(ref calls, int.MinValue) == 0 && Release();

		private bool Release()
		{
			var released = Provider;
			Provider = null;
			try { (released as IDisposable)?.Dispose(); }
			catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"The {kind} teardown failed: {ex}"); }
			return true;
		}
	}

	/// <summary>Holds the one registered provider of a kind, which a script can replace after removing it.</summary>
	internal sealed class ProviderSlot<T>(Script script, string kind) : IDisposable where T : class
	{
		// A permanent marker prevents registration from racing past shutdown.
		private static readonly Registration retired = new(null, null);
		private readonly Script script = script;
		private readonly string kind = kind;
		private Registration registration;
		private volatile bool wasRegistered;

		/// <summary>Whether a provider has ever been registered, which stays true after its removal because output
		/// it already submitted can still be on its way.</summary>
		internal bool WasRegistered => wasRegistered;

		internal bool IsRetired => Volatile.Read(ref registration) == retired;

		internal IDisposable Register(T provider)
		{
			ArgumentNullException.ThrowIfNull(provider);
			ObjectDisposedException.ThrowIf(script.IsDisposed, script);
			var current = new Registration(this, provider);
			// Set before the registration is published, so no admitted call can submit output that is not yet recognized.
			wasRegistered = true;
			var previous = Interlocked.CompareExchange(ref registration, current, null);

			if (previous != null)
			{
				ObjectDisposedException.ThrowIf(previous == retired, script);
				throw new InvalidOperationException($"This script already has a {kind}.");
			}
			return current;
		}

		/// <summary>Admits one call, or returns null when no provider is registered; end it with <see cref="ProviderLease{T}.Exit"/>.</summary>
		internal ProviderLease<T> Enter()
		{
			while (true)
			{
				var current = Volatile.Read(ref registration);
				if (current == null || current == retired) return null;
				// A retired lease has already left the slot, so reading it again finds its successor.
				if (current.TryEnter()) return current;
			}
		}

		public void Dispose()
		{
			var current = Interlocked.Exchange(ref registration, retired);
			if (current != retired) current?.Dispose();
		}

		private sealed class Registration(ProviderSlot<T> owner, T provider) : ProviderLease<T>(provider, owner?.kind), IDisposable
		{
			public void Dispose()
			{
				Interlocked.CompareExchange(ref owner.registration, null, this);
				_ = Retire();
			}
		}
	}
}
