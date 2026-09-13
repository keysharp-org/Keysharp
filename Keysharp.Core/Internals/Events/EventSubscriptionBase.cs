using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>Shared lifecycle state for a script event subscription.</summary>
	internal abstract class EventSubscriptionBase : CallbackRegistration
	{
		/// <summary>The script called <c>Stop()</c>.</summary>
		internal const string EndReasonStopped = "Stopped";
		/// <summary>The hook's owning thread tore down, the engine disposed, or registration was refused.</summary>
		internal const string EndReasonExit = "Exit";
		/// <summary>The native source could not be installed, so the hook could never fire.</summary>
		internal const string EndReasonFailed = "Failed";

		internal object scriptObject;                         // the Ks.EventHook wrapper (callback arg 1)

		private string endReason;                             // null while the hook can still fire

		protected EventSubscriptionBase(object callback, ScriptEventScheduler ownerScheduler, bool keepsScriptRunning)
			: base(callback, ownerScheduler, true, keepsScriptRunning)
		{
		}

		/// <summary><c>""</c> while the subscription can still fire, otherwise why it ended.</summary>
		internal string EndReason => Volatile.Read(ref endReason) ?? "";

		/// <summary>Whether the run can still fire.</summary>
		internal bool IsRunning => IsActive && Volatile.Read(ref endReason) == null;

		/// <summary>Records the first reason this run ended and returns whether this call recorded it.</summary>
		internal bool End(string reason) => Interlocked.CompareExchange(ref endReason, reason, null) == null;

		/// <summary>Cancels this subscription with the manager that owns it, so a hook can stop itself without
		/// knowing which manager that is.</summary>
		internal abstract void Unregister();

		/// <summary>Registers this run with the manager of its family.</summary>
		internal abstract void Register();

		internal static Keysharp.Builtins.Array Handles(EventSubscriptionBase[] runs)
		{
			var handles = new object[runs.Length];

			for (var i = 0; i < handles.Length; i++)
				handles[i] = runs[i].scriptObject;

			return new Keysharp.Builtins.Array(handles);
		}

	}
}
