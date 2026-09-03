#if LINUX
using System.Drawing;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// The keysharp-desktop backend for sessions with no Wayland compositor: bare X11,
	/// XFCE, MATE, and KDE or GNOME running an Xorg session without the shell extension
	/// installed. The broker answers these from the X server itself, inside a forked
	/// worker that has dropped to the user, and emits the same payloads the compositor
	/// backends do, so a caller parses one format whichever answered.
	///
	/// This does not replace the local Xlib paths. It is the brokered route, which exists
	/// so the same consent, revocation and audit apply on X11 as on Wayland; a caller
	/// that cannot reach the broker still has the local path.
	/// </summary>
	internal static class X11Backend
	{
		private const string BackendName = "x11";

		/// <summary>
		/// Whether the broker will actually serve this session. Deliberately a probe
		/// rather than a look at the environment: the service decides what it can serve
		/// and may serve less than a session type suggests, so asking it is the only
		/// answer that cannot be wrong. The cursor query carries no permission scope, so
		/// probing raises no prompt.
		/// </summary>
		internal static bool IsAvailable()
			=> QueryCursorPosition(out _, out _);

		internal static bool QueryCursorPosition(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);

		internal static bool QueryWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea(BackendName, out area);

		/// <summary>Null when the broker cannot serve it, as with every other backend.</summary>
		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList(BackendName, includeHidden);

		internal static string QueryActiveWindow()
			=> DesktopClient.QueryActiveWindow(BackendName);
	}
}
#endif
