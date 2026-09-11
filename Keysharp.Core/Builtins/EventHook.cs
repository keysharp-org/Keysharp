namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The base of every event subscription a script holds: <c>InputHook</c>, <c>Ks.WinEvent</c>,
		/// <c>Ks.MonitorHook</c>, <c>Ks.ClipboardHook</c>, <c>Ks.Audio.DeviceHook</c> and
		/// <c>Ks.Clr.EventSubscription</c>. A hook is always in one of three states, readable from two members:
		/// <list type="bullet">
		/// <item>Active — <c>InProgress</c> is true; the callback fires when the event happens.</item>
		/// <item>Idle — <c>InProgress</c> is false and <c>EndReason</c> is <c>""</c>; <c>Start()</c> runs it.</item>
		/// <item>Ended — <c>EndReason</c> says why: <c>"Stopped"</c>, <c>"Exit"</c>, <c>"Failed"</c>, or an
		/// <c>InputHook</c>'s own reasons.</item>
		/// </list>
		/// <para>
		/// <c>Stop()</c> is final for a hook a factory returned, because nothing is left to describe its source once
		/// it is released; an <c>InputHook</c> carries its own description, so its <c>Start()</c> begins again.
		/// Pause is cheap and reversible and keeps the native source installed; Stop is what releases it.
		/// </para>
		/// <para>
		/// <c>Stop()</c> and <c>Pause()</c> take effect when called: a callback that was queued but has not started
		/// is discarded, while the one already running always finishes. A hook is rooted by the manager that owns
		/// it, so dropping the handle never stops it — only <c>Stop()</c> or the owning thread's teardown does.
		/// </para>
		/// </summary>
		public class EventHook : KeysharpObject
		{
			internal Keysharp.Internals.Events.EventSubscriptionBase sub;

			internal EventHook() : base() { }

			// InputHook is constructed by scripts; nothing else here is.
			private protected EventHook(params object[] args) : base(args) { }

			/// <summary>True while the callback fires when the event happens.</summary>
			public virtual bool InProgress => sub is { IsActive: true } && !sub.Suppressed;

			/// <summary><c>""</c> while the hook can still fire, otherwise why it ended.</summary>
			public virtual string EndReason => sub?.EndReason ?? "";

			/// <summary>Makes this hook fire again. Resumes a paused hook and does nothing otherwise, so it needs no
			/// state test in front of it.</summary>
			public virtual object Start()
			{
				if (sub is { IsActive: true } s)
					s.paused = false;

				return DefaultObject;
			}

			/// <summary>Ends the hook and releases its native source. Idempotent; the first reason recorded wins. A
			/// family that has an end callback runs it here.</summary>
			public virtual object Stop()
			{
				if (sub is { IsActive: true } s)
				{
					s.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonStopped);
					s.Unregister();
				}

				return DefaultObject;
			}

			/// <summary>Stops firing and stays resumable, changing nothing else. <c>Start()</c> resumes.</summary>
			public virtual object Pause()
			{
				if (sub is { IsActive: true } s)
					s.paused = true;

				return DefaultObject;
			}
		}
	}
}
