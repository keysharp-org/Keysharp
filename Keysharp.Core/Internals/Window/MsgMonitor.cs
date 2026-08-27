using Keysharp.Builtins;
namespace Keysharp.Internals.Window
{
	internal sealed class MsgMonitorRegistration(KeysharpFunc callback, int maxInstances, ScriptEventScheduler ownerScheduler)
		: CallbackRegistration(callback, ownerScheduler, true)
	{
		internal int InstanceCount;
		internal int MaxInstances { get; } = maxInstances;
	}

	internal class MsgMonitor
	{
		private readonly Lock gate = new();
		private readonly CallbackRegistry<MsgMonitorRegistration> registrations = new();
		internal bool isPrefiltered = false;

		internal bool IsEmpty
		{
			get
			{
				lock (gate)
				{
					return registrations.IsEmpty;
				}
			}
		}

		internal MsgMonitorRegistration[] GetRegistrationsSnapshot()
		{
			lock (gate)
				return registrations.GetSnapshot();
		}

		internal void ModifyRegistration(KeysharpFunc funcObj, long addRemove, ScriptEventScheduler ownerScheduler)
		{
			lock (gate)
				registrations.ModifyEventHandlers(funcObj, addRemove, (callback, value) => new MsgMonitorRegistration(
					callback,
					Math.Clamp((int)Math.Abs(value), 1, Script.maxThreadsLimit),
					ownerScheduler));
		}

		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			lock (gate)
				return registrations.RemoveOwned(scheduler);
		}
	}
}
