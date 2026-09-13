using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// Engine-side state for a single <c>Clipboard.OnChange</c> subscription. A clipboard change has no filtering
	/// criteria and no per-event state, so it adds nothing to <see cref="EventSubscriptionBase"/>; the
	/// script-facing <c>Ks.ClipboardHook</c> wraps one of these.
	/// </summary>
	internal sealed class ClipboardEventRegistration(object callback, ScriptEventScheduler ownerScheduler,
		ClipboardEventManager manager)
		: EventSubscriptionBase(callback, ownerScheduler, manager.KeepsScriptRunning)
	{
		internal readonly ClipboardEventManager manager = manager;

		internal override void Unregister() => manager.Unregister(this);

		internal override void Register() => manager.Register(this);
	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Clipboard.OnChange</c>.
	/// <para>
	/// Deliberately separate from the <c>OnClipboardChange</c> handler chain. That chain is AHK's, and a handler's
	/// return value there stops every handler after it; a hook sharing the chain could therefore suppress unrelated
	/// handlers with an incidental return value (an <c>Array.Push()</c> result, say). Hook results are discarded,
	/// and each hook runs on its own owner's pseudo-thread rather than inline on whichever thread the notification
	/// arrived on.
	/// </para>
	/// </summary>
	internal sealed class ClipboardEventManager(Script script)
		: EventManagerBase<ClipboardEventRegistration, ClipboardEventManager.NativeSource, ValueTuple>(script, true)
	{
		/// <summary>
		/// The clipboard's native source. Unlike the window and display sources there is nothing per-manager to own:
		/// monitoring is a single switch on the script's main window, shared with the <c>OnClipboardChange</c> chain,
		/// so this only re-applies whether that switch should be on.
		/// </summary>
		internal sealed class NativeSource(Script script) : IDisposable
		{
			internal void Apply(bool wantedByHooks) => script.ApplyClipboardMonitoring(wantedByHooks || script.ClipFunctions.Count > 0);

			public void Dispose() => Apply(false);
		}

		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;

		protected override NativeSource CreateBackend() => new(script);

		protected override void SyncNativeLocked()
		{
			if (EnsureBackend() is { } source)
				source.Apply(registrations.Count > 0);
			else if (registrations.Count > 0)
				FailAllLocked();                              // the monitor cannot be installed, so these can never fire
		}

		/// <summary>Fans one clipboard change out to every hook, on whatever thread the notification arrived on.</summary>
		internal void Dispatch(long dataType)
		{
			if (disposed)
				return;

			ClipboardEventRegistration[] toFire;

			lock (gate)
			{
				if (disposed || registrations.Count == 0)
					return;

				toFire = [.. registrations];
			}

			foreach (var reg in toFire)
				Fire(reg, reg.Callback, [reg.scriptObject, dataType]);
		}
	}
}
