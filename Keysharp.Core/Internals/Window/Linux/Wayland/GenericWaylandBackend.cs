#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// The keysharp-desktop backend for Wayland compositors with no extension of their
	/// own: sway, Hyprland, COSMIC, niri, river and the rest.
	///
	/// This is a different position from every other backend here, and the difference
	/// is worth understanding before using it. GNOME and Cinnamon run code INSIDE the
	/// compositor and sit on the privileged side of every Wayland restriction. This one
	/// is an ordinary client on the outside, so it can do only what a compositor has
	/// chosen to expose to one: read the clipboard, and list what windows exist.
	///
	/// Everything else is not unimplemented, it is impossible. No Wayland protocol lets
	/// one client restack another client's window, set its geometry or opacity, keep it
	/// above, change its decoration, learn a pid to signal it, or correlate a toplevel
	/// to the process about to create it. Nothing reports which window has focus either,
	/// which is why there is no active-window query here. Those will not arrive with
	/// more work; they need protocols that do not exist.
	/// </summary>
	internal static class GenericWaylandBackend
	{
		private const string BackendName = "generic";

		/// <summary>
		/// Whether the broker will serve this session, asked rather than inferred. What
		/// a Wayland compositor implements is not knowable from its name -- two of the
		/// same kind differ -- so the daemon probes what is actually advertised and
		/// registers that, and asking is the only answer that cannot be wrong.
		/// </summary>
		internal static bool IsAvailable()
			=> DesktopClient.ProbeProvider(BackendName);

		/// <summary>
		/// The window list, which reports less than the other backends do. It carries a
		/// title, a class taken from the app id, and an opaque identifier, and OMITS
		/// geometry, pid and window state rather than reporting zeros for them:
		/// ext-foreign-toplevel-list does not carry those, and a zero would read as a
		/// fact. The identifier is a string chosen by the compositor with nothing to
		/// derive from it; it is simply the handle to pass back.
		///
		/// Null when the broker cannot serve it, as with every other backend.
		/// </summary>
		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList(BackendName, includeHidden);

		/// <summary>
		/// Reads only, and not through a portal: ext-data-control asks for no consent
		/// dialog and shows none, because a compositor that offers the protocol has
		/// already decided a client which can bind it may read the selection.
		///
		/// Writing is absent for the same reason as on X11 -- owning a selection means
		/// staying alive to serve it, and the worker that answers exits with its one
		/// operation, so the content would vanish behind the caller.
		/// </summary>
		internal static string[] ClipboardMimetypes()
			=> DesktopClient.GetClipboardMimetypes(BackendName);

		internal static byte[] ClipboardContent(string mimetype)
			=> DesktopClient.GetClipboardContent(BackendName, mimetype);

		internal static string ClipboardText()
			=> DesktopClient.GetClipboardText(BackendName);
	}
}
#endif
