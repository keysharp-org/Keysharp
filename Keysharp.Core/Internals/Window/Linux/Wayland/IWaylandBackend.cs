#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Outcome of a compositor-extension image-overlay Show. The middle state is the important one: a
	/// compositor overlay is drawn by a shell extension over an asynchronous D-Bus call, so a slow (cold, large)
	/// upload can exceed our client-side deadline even though the shell received it and created the actor. We must
	/// not treat that ambiguous timeout as a failure — doing so and falling back to an Eto window leaves two
	/// overlays on screen (the shell's actor plus a mis-positioned Eto twin). Only a definitive rejection/absence
	/// is a <see cref="Failed"/>.</summary>
	internal enum OverlayShowResult
	{
		/// <summary>The shell acknowledged the overlay — it is displayed.</summary>
		Shown,

		/// <summary>No acknowledgement within the deadline. The shell most likely still created the actor (the call
		/// was dispatched), so the caller commits to the compositor and updates the same actor on the next Show.</summary>
		TimedOut,

		/// <summary>The extension is absent, or definitively rejected/errored the call. The caller may fall back to
		/// an Eto window without risking a duplicate, because no compositor actor was created.</summary>
		Failed
	}

	/// <summary>The kind of a <see cref="WaylandWindowEvent"/>. These map 1:1 onto the platform-neutral
	/// <c>WindowEventType</c> the WinEvent manager consumes (Created additionally implies Show).</summary>
	internal enum WaylandWindowEventKind
	{
		Created,
		Closed,
		Activated,
		TitleChanged,
		Minimized,
		Restored,
		MoveResized,
		ActiveStateChanged
	}

	/// <summary>A normalized window event produced by an <see cref="IWaylandBackend"/> event source, carrying the
	/// kind and the backend's stable window handle (the same handle <c>TryGetWindow</c>/<c>WinExist</c> use). For
	/// MoveResized events <see cref="Bounds"/> carries the window's screen geometry (compositors already have it),
	/// so the consumer doesn't have to query it back per move.</summary>
	internal readonly record struct WaylandWindowEvent(WaylandWindowEventKind Kind, nint Handle)
	{
		internal Rectangle? Bounds { get; init; }
	}

	internal static class WaylandWindowStateProtocol
	{
		internal const int Normal = 0;
		internal const int Minimized = 1;
		internal const int Maximized = 2;

		internal static int ToShellExtensionState(FormWindowState state) => state switch
		{
			FormWindowState.Minimized => Minimized,
			FormWindowState.Maximized => Maximized,
			_ => Normal,
		};
	}

	/// <summary>The keysharp-desktop surface for foreign windows and compositor-owned input,
	/// clipboard and overlays. Unsupported operations return false so callers can degrade.</summary>
	internal interface IWaylandBackend
	{
		/// <summary>Stable key used by the native desktop service.</summary>
		string BackendKey { get; }

		/// <summary>Human-readable name for diagnostics.</summary>
		string Name { get; }

		/// <summary>
		/// True when the backend can simulate mouse input via
		/// <see cref="TrySendMouseMoveAbsolute"/>, <see cref="TrySendMouseButton"/>, etc.
		/// Used by <see cref="Keysharp.Internals.Input.Linux.LinuxKeyboardMouseSender"/>
		/// to prefer compositor-native mouse injection over input service on Wayland.
		/// </summary>
		bool SupportsMouse => false;

		/// <summary>
		/// True when this backend can supply window lifecycle/state events through
		/// <see cref="SubscribeWindowEvents"/>, either from a native event channel or by polling its window list.
		/// A native Wayland GDK display cannot be used by the X11 event backend, so false means WinEvent is
		/// unavailable for this compositor.
		/// </summary>
		bool SupportsWindowEvents => false;

		/// <summary>True when window events come from a push channel rather than list polling.</summary>
		bool SupportsPushWindowEvents => false;

		/// <summary>
		/// Subscribe to window events (create/close/activate/title-change/minimize/restore/move). The sink may be
		/// invoked on any thread and must not block. Returns an <see cref="IDisposable"/> that ends the subscription
		/// (idempotent), or null if events are unsupported. Handles match those returned by the Try* query methods,
		/// so a consumer can resolve full window state on demand.
		/// </summary>
		IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink) => null;

		/// <summary>
		/// Best-effort global cursor position in screen coordinates. Returns true on success
		/// with <paramref name="x"/>/<paramref name="y"/> set to a valid pixel coordinate;
		/// false if the backend can't currently answer (compositor offline, IPC failed, etc.).
		/// </summary>
		bool TryGetCursorPos(out int x, out int y);

		/// <summary>Cheap membership test — does this handle belong to this backend (bit-tag or id-map check,
		/// without compositor IPC)? True does not guarantee the window still exists; commands verify that themselves.
		/// Use this to guard routing decisions; use <see cref="TryGetWindow"/> only when the info is consumed.</summary>
		bool IsKnown(nint handle) => false;

		/// <summary>Translate a managed window handle to the identifier used for native capture.</summary>
		bool TryGetNativeWindowId(nint handle, out string id)
		{
			id = null;
			return false;
		}

		bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];
			return false;
		}

		bool TryGetActiveWindow(out WaylandWindowInfo window)
		{
			window = null;
			return false;
		}

		bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			window = null;
			return false;
		}

		bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
		{
			window = null;
			return false;
		}

		/// <summary>
		/// Best-effort usable area (the work area, i.e. the monitor minus panels/docks/struts) of the
		/// primary or active monitor, in screen coordinates. A Wayland client cannot compute this itself —
		/// gdk_monitor_get_workarea returns the full monitor there — so it must come from the compositor.
		/// Returns false if the backend can't answer, in which case the caller falls back to the full
		/// monitor bounds (e.g. by maximizing and letting the compositor size the window).
		/// </summary>
		bool TryGetWorkArea(out Rectangle area)
		{
			area = Rectangle.Empty;
			return false;
		}

		bool TryActivateWindow(nint handle) => false;

		/// <summary>Claim the next top-level this process creates under <paramref name="cookie"/>, so
		/// <see cref="TryGetReservedWindow"/> can name it afterwards, and have the compositor place it at
		/// (<paramref name="x"/>, <paramref name="y"/>) before it is first painted. Pass
		/// <see cref="WindowInfoBase.Unchanged"/> for both to claim without placing. False = unsupported,
		/// and the caller keeps the correlate-then-move path.</summary>
		bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs) => false;

		/// <summary>The compositor window a reservation was applied to, which is an exact answer to "which
		/// window is mine" and needs no matching. False = unsupported, or not consumed (yet).</summary>
		bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			handle = 0;
			compositorId = "";
			return false;
		}

		bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize) => false;

		/// <summary>Remove (true) / restore (false) the server-side window decoration (titlebar) for one of our
		/// own borderless windows, without forcing GTK client-side decorations. False = unsupported.</summary>
		bool TrySetNoBorder(nint handle, bool noBorder) => false;

		bool TrySetWindowState(nint handle, FormWindowState state) => false;

		/// <summary>Keep the window above (true) / clear keep-above (false). False = unsupported.</summary>
		bool TrySetAlwaysOnTop(nint handle, bool onTop) => false;

		/// <summary>Hide one of our own windows from the taskbar/pager/switcher (true), or show it there (false).
		/// GTK's skip-taskbar hint is X11-only, so on Wayland a tool window is otherwise listed like any other.
		/// False = unsupported.</summary>
		bool TrySetSkipTaskbar(nint handle, bool skip) => false;

		/// <summary>Move the window in the compositor stacking order. False = unsupported.</summary>
		bool TrySetZOrder(nint handle, ZOrder z) => false;

		/// <summary>Set whole-window opacity from AHK alpha semantics (0 transparent, 255 opaque, "Off" opaque). False = unsupported.</summary>
		bool TrySetTransparency(nint handle, object alpha) => false;

		/// <summary>True when <see cref="TrySetTransparency"/> is implemented at all. Lets a request against one of
		/// the process's own windows be accepted before the window exists compositor-side without
		/// claiming success on a compositor that could never honour it.</summary>
		bool SupportsTransparency => false;

		bool TryCloseWindow(nint handle) => false;

		bool TryKillWindow(nint handle) => false;

		// ---- Compositor-drawn overlay ------------------------------------
		// On a compositor with no wlr-layer-shell (notably GNOME/Mutter), Keysharp cannot create a
		// click-through layer surface itself, so overlays have to be drawn inside the compositor.
		// Highlight, ToolTip and Overlay all render through this single image-overlay primitive.

		/// <summary>True when the backend can draw a generic PNG-backed click-through overlay inside the compositor.</summary>
		bool SupportsImageOverlay => false;

		/// <summary>True when an actual image-overlay call is worth attempting. Shell-extension backends override
		/// this independently of <see cref="SupportsImageOverlay"/> because a transient/slow D-Bus owner probe is not
		/// authoritative: the real Show call provides the definitive success/failure result and must get a chance to
		/// run. Backends with a purely local capability can use the default.</summary>
		bool CanAttemptImageOverlay => SupportsImageOverlay;

		/// <summary>Create or update a compositor-owned image overlay. PNG bytes are copied by the compositor service.
		/// The distinction between <see cref="OverlayShowResult.TimedOut"/> and <see cref="OverlayShowResult.Failed"/>
		/// matters: a timed-out call most likely still reached the shell and created the actor (so the caller commits
		/// to the compositor rather than duplicating the overlay with an Eto fallback), whereas a definitive failure
		/// means the extension is absent or rejected the call (so the caller may fall back).</summary>
		OverlayShowResult TryShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
			=> OverlayShowResult.Failed;

		/// <summary>Reposition/resize an existing image overlay by id, reusing the pixels already uploaded to the
		/// compositor (no re-encode). False = unsupported or no such overlay; the caller should re-Show instead.</summary>
		bool TryMoveImageOverlay(uint id, int x, int y, int width, int height) => false;

		/// <summary>Remove a compositor-owned image overlay. False = unsupported.</summary>
		bool TryHideImageOverlay(uint id) => false;

		// ---- Clipboard (compositor-mediated, all MIME types) ------------
		// For compositors with no data-control protocol (Cinnamon/Muffin), a background app can't read/write/
		// monitor the clipboard directly; the shell extension does it. Raw MIME <-> bytes so every format
		// (text, image, html, uri-list, ...) round-trips. Default: unsupported.

		/// <summary>True when the backend can read/write/monitor the clipboard via the compositor.</summary>
		bool SupportsClipboard => false;

		/// <summary>MIME types currently on the clipboard, or null if unsupported/failed.</summary>
		string[] GetClipboardMimetypes() => null;

		/// <summary>Bytes of one clipboard MIME type, or null if unsupported/absent.</summary>
		byte[] GetClipboardContent(string mimetype) => null;

		/// <summary>Replace the whole clipboard with one MIME type's bytes. False = unsupported/failed.</summary>
		bool SetClipboardContent(string mimetype, byte[] bytes) => false;

		/// <summary>Current clipboard UTF-8 text (fast path), or null if unsupported/failed.</summary>
		string GetClipboardText() => null;

		/// <summary>Replace the clipboard with UTF-8 text. False = unsupported/failed.</summary>
		bool SetClipboardText(string text) => false;

		/// <summary>Subscribe to clipboard changes; handler gets (utf8 text, available MIME types) and may fire
		/// on any thread. Returns an IDisposable that ends the subscription, or null if unsupported.</summary>
		IDisposable SubscribeClipboardChanges(Action<string, string[]> handler, Action<Exception> onError = null) => null;

		/// <summary>Observe availability changes for the compositor clipboard service. This is a recovery hint, not
		/// a clipboard-content notification.</summary>
		IDisposable SubscribeClipboardAvailability(Action handler) => null;

		// ---- Mouse simulation -------------------------------------------
		// Default implementations return false (backend does not support it).

		bool TrySendMouseMoveAbsolute(int x, int y) => false;

		bool TrySendMouseMoveRelative(int dx, int dy) => false;

		/// <summary>button: 1 = left, 2 = middle, 3 = right (X11 convention).</summary>
		bool TrySendMouseButton(uint button, bool pressed) => false;

		/// <summary>
		/// delta in 120-unit wheel increments (positive = up/right).
		/// vertical: true = vertical scroll axis, false = horizontal.
		/// </summary>
		bool TrySendMouseScroll(int delta, bool vertical) => false;
	}
}
#endif
