#if LINUX
using Tmds.DBus.Protocol;

namespace Keysharp.Internals.DBus
{
	/// <summary>
	/// Adapts the generated Notification-based signal watchers to the (handler, onError) shape the compositor
	/// backends use.
	/// </summary>
	internal static class DBusSignals
	{
		/// <summary>
		/// Wraps a value handler and an optional error handler. Nothing may escape: an exception leaving a signal
		/// handler tears down the connection's whole dispatch loop, silently killing every other subscription and
		/// call on it.
		/// </summary>
		internal static Action<Notification<T>> Adapt<T>(Action<T> handler, Action<Exception> onError = null)
			=> n =>
		{
			try
			{
				if (n.HasValue)
					handler(n.Value);
				else if (n.Exception is Exception ex)
					onError?.Invoke(ex);
			}
			catch
			{
			}
		};

		/// <summary>Error notifications are only delivered when asked for, so the flags follow the handler.</summary>
		internal static ObserverFlags FlagsFor(Action<Exception> onError)
			=> onError != null ? ObserverFlags.EmitAll : ObserverFlags.None;
	}
}
#endif
