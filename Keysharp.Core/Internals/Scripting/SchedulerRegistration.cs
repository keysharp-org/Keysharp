using Keysharp.Builtins;
using Keysharp.Internals.Invoke;

namespace Keysharp.Internals.Scripting
{
	internal class SchedulerRegistration
	{
		private int persistenceHeld;                          // 1 while this registration holds its owner's root
		// False for a registration that must not keep its owning thread alive: an event hook whose AHK counterpart does
		// not keep a script running.
		private readonly bool holdsRoot;

		internal SchedulerRegistration(ScriptEventScheduler ownerScheduler = null, bool active = false, bool holdsRoot = true)
		{
			this.holdsRoot = holdsRoot;
			Set(ownerScheduler, active);
		}

		internal ScriptEventScheduler OwnerScheduler { get; private set; }
		// Volatile: the window-event intake reads liveness without taking the manager gate.
		private volatile bool isActive;

		internal bool IsActive => isActive;

		/// <summary>
		/// The thread priority this registration launches its callback at (default 0). Timers set it from SetTimer's
		/// priority argument; menu items from Menu.Add's "Pn" option. Most registrations leave it 0.
		/// </summary>
		internal long Priority { get; set; }

		internal void SetActive(bool active)
			=> Set(OwnerScheduler, active);

		internal void Set(ScriptEventScheduler ownerScheduler, bool active)
		{
			if (ReferenceEquals(OwnerScheduler, ownerScheduler) && IsActive == active)
				return;

			UpdatePersistence(false);
			OwnerScheduler = ownerScheduler;
			isActive = active;
			UpdatePersistence(active && ownerScheduler != null && holdsRoot);
		}

		internal void Clear()
		{
			UpdatePersistence(false);
			OwnerScheduler = null;
			isActive = false;
		}

		// Exchanged, so a Stop() and a teardown clearing the same registration at once release its root only once.
		private void UpdatePersistence(bool shouldHold)
		{
			var owner = OwnerScheduler;                       // read first: a concurrent Clear nulls it
			var held = shouldHold ? 1 : 0;

			if (Interlocked.Exchange(ref persistenceHeld, held) != held)
				owner?.AdjustPersistenceRoot(shouldHold ? 1 : -1);
		}
	}

	/// <summary>A scheduler registration with the script's callback: the object the script gave, a function or any
	/// other callable object, as AHK keeps it (<see cref="Functions.ToCallback"/>).</summary>
	internal class CallbackRegistration : SchedulerRegistration
	{
		private object callback;

		internal CallbackRegistration(object callback = null, ScriptEventScheduler ownerScheduler = null, bool active = false,
				bool holdsRoot = true)
			: base(ownerScheduler, active, holdsRoot)
		{
			this.callback = callback;
		}

		internal static CallbackRegistration CreateGlobal(object callback, long _)
			=> new(callback, null, true);

		internal object Callback => callback;

		internal void Set(object callback, ScriptEventScheduler ownerScheduler, bool active)
		{
			this.callback = callback;
			base.Set(ownerScheduler, active);
		}
	}
}
