namespace Keysharp.Internals.Window
{
	/// <summary>
	/// The kinds of window events a backend can deliver. These are platform-neutral; each
	/// <see cref="IWindowEventBackend"/> maps them onto the native signals of its OS/compositor.
	/// </summary>
	internal enum WindowEventType
	{
		/// <summary>A window became the active/foreground window.</summary>
		Active,
		/// <summary>A new top-level window was created.</summary>
		Create,
		/// <summary>A top-level window was destroyed (or hidden/cloaked for a DetectHiddenWindows-off consumer).</summary>
		Close,
		/// <summary>A window moved or resized.</summary>
		Move,
		/// <summary>A window became visible (mapped/shown).</summary>
		Show,
		/// <summary>A window was minimized/iconified.</summary>
		Minimize,
		/// <summary>A window was restored from the minimized state.</summary>
		Restore,
		/// <summary>A window's title changed.</summary>
		TitleChange,
		/// <summary>The text caret (insertion point) moved inside a window. The handle is the caret owner's top-level
		/// window, and the caret's screen rectangle rides on <see cref="WindowEventRaw.Bounds"/>. Unlike every other
		/// event the backend must supply that rectangle — only it can observe the caret at event time — so a
		/// <see cref="WindowEventRaw"/> of this type without bounds is dropped by <see cref="WinEventManager"/>.</summary>
		CaretMove,
		/// <summary>A window matching the criteria entered the matching set (appeared via create/show/title-change).
		/// Derived by <see cref="WinEventManager"/> from the lower-level events; not a distinct native hook.</summary>
		Exist,
		/// <summary>A window matching the criteria left the matching set (destroyed/hidden/cloaked, or a title change
		/// stopped it matching). Honors DetectHiddenWindows. Derived by <see cref="WinEventManager"/>; not a native hook.</summary>
		NotExist
	}

	/// <summary>
	/// Bit flags mirroring <see cref="WindowEventType"/>, used to tell a backend which categories of native
	/// hook are currently needed (so unused/expensive hooks are never installed).
	/// </summary>
	[Flags]
	internal enum WindowEventMask
	{
		None        = 0,
		Active      = 1 << 0,
		Create      = 1 << 1,
		Close       = 1 << 2,
		Move        = 1 << 3,
		Show        = 1 << 4,
		Minimize    = 1 << 5,
		Restore     = 1 << 6,
		TitleChange = 1 << 7,
		CaretMove   = 1 << 8
	}

	internal static class WindowEventTypeExtensions
	{
		internal static WindowEventMask ToMask(this WindowEventType type) => type switch
		{
			WindowEventType.Active      => WindowEventMask.Active,
			WindowEventType.Create      => WindowEventMask.Create,
			WindowEventType.Close       => WindowEventMask.Close,
			WindowEventType.Move        => WindowEventMask.Move,
			WindowEventType.Show        => WindowEventMask.Show,
			WindowEventType.Minimize    => WindowEventMask.Minimize,
			WindowEventType.Restore     => WindowEventMask.Restore,
			WindowEventType.TitleChange => WindowEventMask.TitleChange,
			WindowEventType.CaretMove   => WindowEventMask.CaretMove,
			// Exist/NotExist are derived (membership transitions), not backed by a dedicated native hook — the
			// backend mask they require is computed from the underlying events in WinEventManager.SyncBackendMask.
			_ => WindowEventMask.None
		};
	}

	/// <summary>
	/// A normalized window event produced by an <see cref="IWindowEventBackend"/>. Carries the handle, type, and
	/// timestamp, plus — for Move events — the window's screen bounds when the backend can supply them without an
	/// extra round-trip (Wayland compositors already have them). <see cref="Bounds"/> is null otherwise, and the
	/// consumer queries the window's geometry on demand (cheap on X11/Windows).
	/// </summary>
	internal readonly record struct WindowEventRaw(WindowEventType Type, nint Hwnd, long TimeMs)
	{
		/// <summary>Screen rectangle carried by the event: the window's bounds for
		/// <see cref="WindowEventType.Move"/> (optional — the consumer queries it when null), and the caret's
		/// rectangle for <see cref="WindowEventType.CaretMove"/> (mandatory — the caret is only observable at event
		/// time, from the backend, so a caret event without it is dropped rather than re-queried).</summary>
		internal Rectangle? Bounds { get; init; }

		/// <summary>
		/// For a <see cref="WindowEventType.Close"/> event, true when the backend has already confirmed the window is
		/// gone (a true destruction, not a possible hide/cloak), so the consumer must not re-verify existence. The
		/// macOS backend sets this because its Close is sourced from an <c>AXUIElementDestroyed</c> notification, which
		/// is authoritative — and the window server's <c>CGWindowList</c> can still list the window briefly afterwards,
		/// so a re-check would race it. Backends whose Close can also mean "hidden" (e.g. Windows cloaking) leave it
		/// false, and the consumer re-checks existence to honor DetectHiddenWindows.
		/// </summary>
		internal bool DestroyConfirmed { get; init; }
	}

	/// <summary>
	/// Platform abstraction for the native window-event source. Implementations install/uninstall the minimal
	/// native hooks required for the requested event categories and raise normalized <see cref="WindowEventRaw"/>
	/// values through <see cref="Sink"/>. This is the general abstraction; the per-platform
	/// <c>WindowEventBackend</c> implements it and is driven directly by <see cref="WinEventManager"/>.
	/// <para>
	/// Threading: <see cref="Sink"/> may be invoked on any thread, and never calls script code itself — it only
	/// produces <see cref="WindowEventRaw"/> values for the consumer to marshal.
	/// </para>
	/// </summary>
	internal interface IWindowEventBackend : IDisposable
	{
		/// <summary>Whether active-window events avoid a polling window-list scan.</summary>
		bool SupportsEfficientActiveTracking => true;

		/// <summary>Receives each native event. Set by the consumer before the first <see cref="Start"/> call.</summary>
		Action<WindowEventRaw> Sink { get; set; }

		/// <summary>Ensure native hooks for every category in <paramref name="mask"/> are installed (idempotent).</summary>
		void Start(WindowEventMask mask);

		/// <summary>Uninstall native hooks for the categories in <paramref name="mask"/> (idempotent).</summary>
		void Stop(WindowEventMask mask);
	}
}
