namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The handle every event subscription hands back, and the first argument every such callback receives:
		/// <c>Ks.WinEvent</c>, <c>Ks.MonitorHook</c> and <c>Ks.ClipboardHook</c> all are one. Because the surface
		/// lives here once, the three cannot drift apart.
		/// <para>
		/// <c>Status</c> is the one value that separates the three states a hook can be in — <c>"Active"</c>
		/// (registered and firing), <c>"Paused"</c> (registered but suppressed) and <c>"Stopped"</c> (unregistered,
		/// which is permanent). <c>IsActive</c> answers the single question "is it firing?", so a paused hook is
		/// NOT active. A stopped hook reports <c>Paused</c> false and <c>Count</c> 0, and ignores writes to both.
		/// </para>
		/// <para>
		/// The subscription is rooted by the manager that owns it, so it keeps firing until <c>Stop()</c>, count
		/// exhaustion, or teardown of the thread that made it — dropping the handle does not stop it.
		/// </para>
		/// </summary>
		public class EventHook : KeysharpObject
		{
			internal EventSubscriptionBase sub;

			public EventHook(params object[] args) : base(args) { }

			internal EventHook() : base() { }

			/// <summary>This hook's effective state: <c>"Active"</c>, <c>"Paused"</c> or <c>"Stopped"</c>.</summary>
			public string Status => !IsLive ? "Stopped" : sub.Suppressed ? "Paused" : "Active";

			/// <summary>True only while this hook is firing; <see cref="Status"/> separates paused from stopped.</summary>
			public bool IsActive => Status == "Active";

			/// <summary>Remaining number of times the callback will fire (-1 = unlimited, 0 once stopped).</summary>
			public long Count => IsLive ? sub.Remaining : 0L;

			/// <summary>Gets or sets this hook's own pause switch. A stopped hook reports false and ignores writes.</summary>
			// Object-typed rather than bool because a script's `true` arrives as an Integer, and this keeps the
			// property accepting the same range of values it always has.
			public object Paused
			{
				get => IsLive && sub.paused;
				set { if (IsLive) sub.paused = value.Ab(); }
			}

			/// <summary>Pauses (1), unpauses (0) or toggles (-1) this hook. Returns the resulting paused state.</summary>
			public object Pause(object newState = null)
			{
				if (!IsLive)
					return false;

				var s = sub;
				var ns = newState.Al(1L);
				s.paused = ns == -1 ? !s.paused : ns != 0;
				return s.paused;
			}

			/// <summary>Cancels the subscription so the callback no longer fires. Idempotent and permanent.</summary>
			public object Stop()
			{
				var s = sub;

				if (s != null && s.IsActive)
					s.Unregister();

				return DefaultObject;
			}

			public override object __Delete()
			{
				_ = Stop();
				return base.__Delete();
			}

			// Unregistering clears the subscription's liveness and leaves its pause flag intact, so every member
			// above gates on liveness rather than on a null check.
			private bool IsLive => sub is { IsActive: true };
		}
	}
}
