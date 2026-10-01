using Keysharp.Builtins;
namespace Keysharp.Internals.Window
{
	internal sealed class MsgMonitorRegistration(object callback, int maxInstances, ScriptEventScheduler ownerScheduler)
		: CallbackRegistration(callback, ownerScheduler, true)
	{
		internal int InstanceCount;
		internal volatile int MaxInstances = maxInstances;
	}

	internal class MsgMonitor
	{
		private readonly CallbackRegistry<MsgMonitorRegistration> registrations = new();

		internal bool IsEmpty => registrations.IsEmpty;

		internal MsgMonitorRegistration[] GetRegistrationsSnapshot() => registrations.GetSnapshot();

		internal void ModifyRegistration(object callback, long maxThreads, ScriptEventScheduler ownerScheduler)
			=> _ = registrations.ModifyEventHandlers(callback, maxThreads, (cb, value) => new MsgMonitorRegistration(
				cb, InstanceLimit(value), ownerScheduler));

		/// <summary>Gives a callback its owner already registered the new MaxThreads, as AHK's OnMessage updates it in
		/// place, or, with none given, leaves it as it is; false when it is not registered.</summary>
		internal bool TryUpdate(object callback, long? maxThreads, ScriptEventScheduler ownerScheduler)
			=> registrations.TryUpdate(callback, ownerScheduler, existing =>
			{
				if (maxThreads is long mt)
					existing.MaxInstances = InstanceLimit(mt);
			});

		private static int InstanceLimit(long maxThreads) => Math.Clamp((int)Math.Abs(maxThreads), 1, Script.maxThreadsLimit);

		internal bool RemoveOwned(ScriptEventScheduler scheduler) => registrations.RemoveOwned(scheduler);
	}
}
