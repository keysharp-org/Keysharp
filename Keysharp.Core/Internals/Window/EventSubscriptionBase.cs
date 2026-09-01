using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Window
{
	/// <summary>
	/// The bookkeeping every script event subscription needs regardless of what it subscribes to: the callback, the
	/// scheduler that owns it, its persistence registration, the script-facing wrapper passed back as the callback's
	/// first argument, a pause flag, and an atomic remaining-fire budget.
	/// <para>
	/// Shared by <see cref="WinEventRegistration"/> and <see cref="MonitorEventRegistration"/> so the budget rule —
	/// which decides when a counted subscription may still fire and when it auto-stops — has exactly one
	/// implementation. Subclasses add only what is specific to their event source.
	/// </para>
	/// </summary>
	internal abstract class EventSubscriptionBase
	{
		internal readonly KeysharpFunc callback;
		internal readonly ScriptEventScheduler ownerScheduler;
		internal readonly CallbackRegistration registration;
		internal object scriptObject;                         // the Ks.WinEvent / Ks.MonitorHook wrapper (callback arg 1)
		internal volatile bool paused;                        // a paused hook stays registered but doesn't fire
		internal volatile bool active;

		private long remaining;                               // -1 => unlimited

		protected EventSubscriptionBase(KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler)
		{
			this.callback = callback;
			this.ownerScheduler = ownerScheduler;
			if (count == 0 || count < -1) 
			{
				Errors.ValueErrorOccurred("Count must be -1 (unlimited) or a positive number.", 0);
				return;
			}
			remaining = count;
			active = true;
			registration = new CallbackRegistration(callback, ownerScheduler, true);
		}

		internal long Remaining => Interlocked.Read(ref remaining);

		/// <summary>
		/// Atomically decides whether this subscription may fire once more, consuming one unit of the
		/// remaining-fire budget. Returns false once the budget is exhausted or the subscription is stopped.
		/// </summary>
		internal bool TryConsumeFire()
		{
			if (!active)
				return false;

			while (true)
			{
				var cur = Interlocked.Read(ref remaining);

				if (cur == 0)
					return false;

				if (cur < 0)
					return true;                              // unlimited

				if (Interlocked.CompareExchange(ref remaining, cur - 1, cur) == cur)
					return true;
			}
		}

		/// <summary>True once the fire budget has just been exhausted (so the manager can auto-stop).</summary>
		internal bool IsExhausted => Interlocked.Read(ref remaining) == 0;
	}
}
