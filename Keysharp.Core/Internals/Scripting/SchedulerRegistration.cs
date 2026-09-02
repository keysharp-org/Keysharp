using Keysharp.Builtins;
using Keysharp.Internals.Invoke;

namespace Keysharp.Internals.Scripting
{
	internal class SchedulerRegistration
	{
		private bool persistenceHeld;

		internal SchedulerRegistration(ScriptEventScheduler ownerScheduler = null, bool active = false)
			=> Set(ownerScheduler, active);

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
			UpdatePersistence(active && ownerScheduler != null);
		}

		internal void Clear()
		{
			UpdatePersistence(false);
			OwnerScheduler = null;
			isActive = false;
		}

		private void UpdatePersistence(bool shouldHold)
		{
			if (persistenceHeld == shouldHold)
				return;

			OwnerScheduler?.AdjustPersistenceRoot(shouldHold ? 1 : -1);
			persistenceHeld = shouldHold;
		}
	}

	internal class CallbackRegistration : SchedulerRegistration
	{
		private KeysharpFunc callback;

		internal CallbackRegistration(KeysharpFunc callback = null, ScriptEventScheduler ownerScheduler = null, bool active = false)
			: base(ownerScheduler, active)
		{
			this.callback = callback;
		}

		internal static CallbackRegistration CreateCurrent(KeysharpFunc callback, long _)
			=> new(callback, Script.TheScript?.EventScheduler, true);

		internal static CallbackRegistration CreateGlobal(KeysharpFunc callback, long _)
			=> new(callback, null, true);

		internal KeysharpFunc Callback => callback;

		internal void Set(KeysharpFunc callback, ScriptEventScheduler ownerScheduler, bool active)
		{
			this.callback = callback;
			base.Set(ownerScheduler, active);
		}
	}
}
