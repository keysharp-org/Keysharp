using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// Engine-side state for a single <c>Clipboard.OnChange</c> subscription. A clipboard change has no filtering
	/// criteria and no per-event state, so it adds nothing to <see cref="EventSubscriptionBase"/>; the
	/// script-facing <c>Ks.ClipboardHook</c> wraps one of these.
	/// </summary>
	internal sealed class ClipboardEventRegistration(KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler,
		ClipboardEventManager manager)
		: EventSubscriptionBase(callback, count, ownerScheduler)
	{
		internal readonly ClipboardEventManager manager = manager;

		internal override void Unregister() => manager.Unregister(this);
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
		: EventManagerBase<ClipboardEventRegistration, ClipboardEventManager.NativeSource, ClipboardEventManager.Payload>(script)
	{
		/// <summary>What a change carries: 0 = the clipboard is now empty, 1 = text or files, 2 = anything else.</summary>
		internal readonly record struct Payload(long DataType);

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

		protected override void SyncNativeLocked() => EnsureBackend()?.Apply(registrations.Count > 0);

		/// <summary>A_EventInfo holds the same data type the callback receives, so a handler that ignores its
		/// parameter can still branch on it.</summary>
		protected override void ApplyThreadState(ThreadVariables tv, ClipboardEventRegistration reg, in Payload payload)
			=> tv.eventInfo = payload.DataType;

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
				Fire(reg, dataType);
		}

		private void Fire(ClipboardEventRegistration reg, long dataType)
		{
			// A paused hook stays registered — and keeps the native monitor installed — but doesn't fire or consume
			// its remaining-count budget.
			if (reg.Suppressed || !reg.TryConsumeFire())
				return;

			var scheduler = DispatchTarget(reg);

			if (scheduler == null)
				return;

			// Callback shape (locked): (hook, type), matching the AHK-shaped OnClipboardChange type codes.
			object[] args = [reg.scriptObject, dataType];
			var payload = new Payload(dataType);
			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunCallback(scheduler, reg, args, payload));

			if (reg.IsExhausted)
				Unregister(reg);
		}
	}
}
