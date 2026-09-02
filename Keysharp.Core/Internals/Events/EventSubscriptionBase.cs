using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// The bookkeeping every script event subscription needs regardless of what it subscribes to. It <em>is</em>
	/// the <see cref="CallbackRegistration"/> the scheduler sweeps — which already owns the callback, the owning
	/// scheduler, the liveness bit and the persistence root — and adds only what a counted subscription needs on
	/// top: the script-facing wrapper passed back as the callback's first argument, a pause flag, and an atomic
	/// remaining-fire budget.
	/// <para>
	/// Shared by <see cref="WinEventRegistration"/> and <see cref="MonitorEventRegistration"/> so the budget rule —
	/// which decides when a counted subscription may still fire and when it auto-stops — has exactly one
	/// implementation. Subclasses add only what is specific to their event source.
	/// </para>
	/// </summary>
	internal abstract class EventSubscriptionBase : CallbackRegistration
	{
		/// <summary>The error a factory raises for a count no subscription can honor.</summary>
		internal const string CountErrorMessage = "Count must be -1 (unlimited) or a positive number.";

		internal object scriptObject;                         // the Ks.WinEvent / Ks.MonitorHook wrapper (callback arg 1)
		internal volatile bool paused;                        // a paused hook stays registered but doesn't fire

		private long remaining;                               // -1 => unlimited

		protected EventSubscriptionBase(KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler)
			: base(callback, ownerScheduler, true)
			=> remaining = count;

		/// <summary>A count a subscription can honor: -1 (unlimited) or positive. The factories check it before
		/// constructing, so a rejected count cannot leave a half-built subscription behind.</summary>
		internal static bool IsValidCount(long count) => count == -1 || count > 0;

		/// <summary>Whether dispatch is currently suppressed. A suppressed subscription stays registered and keeps
		/// whatever state its source tracks current, but does not fire or consume its budget. Overridden where a
		/// source has a manager-wide switch on top of this one (<c>WinEvent.Paused</c>).</summary>
		internal virtual bool Suppressed => paused;

		/// <summary>Cancels this subscription with the manager that owns it, so a hook can stop itself without
		/// knowing which manager that is.</summary>
		internal abstract void Unregister();

		internal long Remaining => Interlocked.Read(ref remaining);

		/// <summary>
		/// Atomically decides whether this subscription may fire once more, consuming one unit of the
		/// remaining-fire budget. Returns false once the budget is exhausted or the subscription is stopped.
		/// </summary>
		internal bool TryConsumeFire()
		{
			if (!IsActive)
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
