#if LINUX
// Eto.Drawing, matching DesktopClient. Both it and System.Drawing are global usings,
// so Rectangle and Bitmap are ambiguous without picking one, and the types here have to
// be the ones DesktopClient returns rather than their same-named neighbours.
using Eto.Drawing;
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

		internal static Bitmap Capture(int x, int y, int width, int height)
			=> DesktopClient.CaptureX11(x, y, width, height);

		internal static Bitmap CaptureWindow(ulong handle, bool includeDecoration)
			=> DesktopClient.CaptureX11Window(handle, includeDecoration);

		/// <summary>
		/// Reads only. The broker does not advertise clipboard writes on X11, and this
		/// deliberately offers no way to ask for one: owning an X selection means staying
		/// alive to answer conversion requests for it, and the worker that would own it
		/// exits with its single operation, so the content would disappear the moment the
		/// call returned. Writing stays on the local path until something outlives the
		/// request to hold the selection.
		/// </summary>
		internal static string[] ClipboardMimetypes()
			=> DesktopClient.GetClipboardMimetypes(BackendName);

		internal static byte[] ClipboardContent(string mimetype)
			=> DesktopClient.GetClipboardContent(BackendName, mimetype);

		internal static string ClipboardText()
			=> DesktopClient.GetClipboardText(BackendName);

		/// <summary>
		/// The control verbs. Almost every one of these is a request to the window
		/// manager rather than an operation on the server, so a true result means the
		/// request was delivered and correctly formed, not that the window has moved:
		/// the manager decides, and on a session with no manager running nothing happens
		/// at all. Raise, lower and kill are the exceptions, being server operations.
		/// </summary>
		internal static bool FocusWindow(ulong handle)
			=> DesktopClient.FocusWindow(BackendName, handle);

		internal static bool RaiseWindow(ulong handle)
			=> DesktopClient.RaiseWindow(BackendName, handle);

		internal static bool LowerWindow(ulong handle)
			=> DesktopClient.LowerWindow(BackendName, handle);

		/// <summary>A request the application may refuse or answer with a dialog.</summary>
		internal static bool CloseWindow(ulong handle)
			=> DesktopClient.CloseWindow(BackendName, handle);

		/// <summary>
		/// Not a request. This severs the owning client's connection to the server, and
		/// every other window that client owns dies with it.
		/// </summary>
		internal static bool KillWindow(ulong handle)
			=> DesktopClient.KillWindow(BackendName, handle);

		internal static bool MoveResizeWindow(ulong handle, int x, int y,
											 int width, int height)
			=> DesktopClient.MoveResizeWindow(BackendName, handle, x, y, width, height);

		/// <summary>0 restores, 1 minimizes, 2 maximizes.</summary>
		internal static bool SetWindowState(ulong handle, int state)
			=> DesktopClient.SetWindowState(BackendName, handle, state);

		/// <summary>0 to 255, the scale every backend of the broker uses.</summary>
		internal static bool SetWindowOpacity(ulong handle, int opacity)
			=> DesktopClient.SetWindowOpacity(BackendName, handle, opacity);

		internal static bool SetWindowAbove(ulong handle, bool above)
			=> DesktopClient.SetWindowAbove(BackendName, handle, above);

		internal static bool SetWindowDecorated(ulong handle, bool decorated)
			=> DesktopClient.SetWindowDecorated(BackendName, handle, decorated);
	}
}
#endif
