using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// The bookkeeping every script event subscription needs regardless of what it subscribes to. It <em>is</em>
	/// the <see cref="CallbackRegistration"/> the scheduler sweeps — which already owns the callback, the owning
	/// scheduler, the liveness bit and the persistence root — and adds only what a pausable subscription needs on
	/// top: the script-facing wrapper passed back as the callback's first argument, a pause flag, and the reason it
	/// ended.
	/// <para>
	/// Shared by every <see cref="EventManagerBase{TRegistration, TBackend, TPayload}"/> family, so the state rules
	/// have exactly one implementation. Subclasses add only what is specific to their event source.
	/// </para>
	/// </summary>
	internal abstract class EventSubscriptionBase : CallbackRegistration
	{
		/// <summary>The script called <c>Stop()</c>.</summary>
		internal const string EndReasonStopped = "Stopped";
		/// <summary>The hook's owning thread tore down, the engine disposed, or registration was refused.</summary>
		internal const string EndReasonExit = "Exit";
		/// <summary>The native source could not be installed, so the hook could never fire.</summary>
		internal const string EndReasonFailed = "Failed";

		internal object scriptObject;                         // the Ks.EventHook wrapper (callback arg 1)
		internal volatile bool paused;                        // a paused hook stays registered but doesn't fire

		private string endReason;                             // null while the hook can still fire

		protected EventSubscriptionBase(KeysharpFunc callback, ScriptEventScheduler ownerScheduler)
			: base(callback, ownerScheduler, true)
		{
		}

		/// <summary>Whether dispatch is currently suppressed. A suppressed subscription stays registered and keeps
		/// whatever state its source tracks current, but its queued callbacks are discarded when they would run.</summary>
		internal bool Suppressed => paused;

		/// <summary><c>""</c> while the subscription can still fire, otherwise why it ended.</summary>
		internal string EndReason => Volatile.Read(ref endReason) ?? "";

		/// <summary>
		/// Records why this subscription ended. The first reason wins, so a later teardown cannot overwrite a
		/// <c>Stop()</c>. Every caller writes this BEFORE clearing liveness: the only torn read that order allows
		/// is a dead hook briefly reporting Active, where the reverse would let it read Idle and invite a
		/// <c>Start()</c> that can never succeed.
		/// </summary>
		internal void End(string reason) => _ = Interlocked.CompareExchange(ref endReason, reason, null);

		/// <summary>Cancels this subscription with the manager that owns it, so a hook can stop itself without
		/// knowing which manager that is.</summary>
		internal abstract void Unregister();
	}
}
