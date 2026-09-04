#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// The brokered KWin backend: a KDE Wayland session, served by a KWin script the
	/// broker reaches over a socket the session daemon hands it at registration.
	///
	/// This is distinct from <see cref="KWinDBusBridge"/>, which drives KWin directly.
	/// The brokered route exists so the same consent, revocation and audit apply on KDE
	/// as everywhere else; a caller that cannot reach the broker still has the direct
	/// path.
	///
	/// Two things about it are worth knowing before use. Captures do NOT go through the
	/// script -- they run in the broker's forked worker -- so they keep working when the
	/// script is wedged, busy or being restarted. And everything that does go through
	/// the script runs on the compositor's main thread, where concurrency is exactly one:
	/// the broker's two lanes buy ordering, so a cheap query is never behind a queue of
	/// enumerations, but never simultaneity.
	/// </summary>
	internal static class KWinBrokerBackend
	{
		private const string BackendName = "kwin";

		/// <summary>
		/// Whether the broker will serve this session. A probe rather than a look at
		/// the environment: the script may be absent, wedged or a version behind, and
		/// the service is the only thing that knows.
		/// </summary>
		internal static bool IsAvailable()
			=> DesktopClient.ProbeProvider(BackendName);

		internal static bool QueryCursorPosition(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);

		internal static bool QueryWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea(BackendName, out area);

		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList(BackendName, includeHidden);

		internal static string QueryActiveWindow()
			=> DesktopClient.QueryActiveWindow(BackendName);

		internal static bool FocusWindow(ulong handle)
			=> DesktopClient.FocusWindow(BackendName, handle);

		internal static bool RaiseWindow(ulong handle)
			=> DesktopClient.RaiseWindow(BackendName, handle);

		/// <summary>
		/// Deliberately absent: KWin exposes raise and no lower, and the script reports
		/// the operation unsupported rather than approximating it by sending the window
		/// behind every other one, which is a different thing and a worse surprise. The
		/// broker does not advertise it either.
		/// </summary>
		internal static bool LowerWindow(ulong handle) => false;

		internal static bool CloseWindow(ulong handle)
			=> DesktopClient.CloseWindow(BackendName, handle);

		internal static bool MoveResizeWindow(ulong handle, int x, int y,
											 int width, int height)
			=> DesktopClient.MoveResizeWindow(BackendName, handle, x, y, width, height);

		/// <summary>0 restores, 1 minimizes, 2 maximizes.</summary>
		internal static bool SetWindowState(ulong handle, int state)
			=> DesktopClient.SetWindowState(BackendName, handle, state);

		/// <summary>
		/// 0 to 255. The script converts to the 0..1 KWin uses and rounds on the way
		/// back, so a fully opaque window reports 255 rather than 254.
		/// </summary>
		internal static bool SetWindowOpacity(ulong handle, int opacity)
			=> DesktopClient.SetWindowOpacity(BackendName, handle, opacity);

		internal static bool SetWindowAbove(ulong handle, bool above)
			=> DesktopClient.SetWindowAbove(BackendName, handle, above);

		internal static bool SetWindowDecorated(ulong handle, bool decorated)
			=> DesktopClient.SetWindowDecorated(BackendName, handle, decorated);
	}
}
#endif
