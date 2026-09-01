namespace Keysharp.Internals
{
	// The capability-service contracts the Platform host exposes. Following IWaylandBackend's proven idiom,
	// methods are Try*/return-bool so a partially-capable backend degrades gracefully rather than throwing.

	internal enum ZOrder { Top, Bottom }

	/// <summary>The currently-live keyboard/mouse injection transport. Kept as a typed value (not a bool)
	/// so additional transports/state can be added without churning consumers.</summary>
	internal enum InputTransport { None, Service, Mac, Windows }

	/// <summary>Foreign window READ surface (by handle). The Linux impl internally composes X11 + the active
	/// Wayland backend; routing re-resolves the backend from the handle on each call.</summary>
	internal interface IWindowQuery
	{
		/// <summary>Build the neutral, read-only window for a bare id, as the per-backend <c>WindowInfoBase</c>
		/// subtype: a <c>MacWindowInfo</c> (seeded from the kCGWindow batch) or <c>WaylandWindowInfo</c> (the
		/// compositor payload) where the backend batches, else a lazy <c>WindowInfo</c> (X11/Windows) that fills
		/// per property live on first access.</summary>
		Keysharp.Internals.Window.WindowInfoBase CreateWindow(nint id);

		/// <summary>The active window as a (seeded-where-cheap) WindowInfo; an unspecified item (handle 0) when
		/// there is none.</summary>
		Keysharp.Internals.Window.WindowInfoBase ActiveWindow();

		/// <summary>All top-level windows, top-of-z-order first — seeded where the platform enumerates in one
		/// batch (Wayland/macOS), empty/lazy otherwise (X11/Windows).</summary>
		IReadOnlyList<Keysharp.Internals.Window.WindowInfoBase> Enumerate(bool includeHidden);

		bool TryGetAt(int x, int y, out nint child);

		/// <summary>At-point query returning the built item; null when nothing is there. Backends whose
		/// at-point lookup already parses the full window payload (Wayland) return it directly instead of
		/// making the caller re-fetch the same window by handle.</summary>
		Keysharp.Internals.Window.WindowInfoBase WindowAt(int x, int y);

		/// <summary>Direct, non-enumerating lookup of the first top-level window by class name (and, in
		/// title-match-mode 3, exact title) — the platform's native FindWindow. Returns true when the backend
		/// has such a fast path (Windows): <paramref name="handle"/> is the match, or value 0 when the backend
		/// can definitively say no such window exists (so the caller skips the O(N) enumeration entirely).
		/// Returns false where there is no native fast path (X11/Wayland/macOS), and the caller enumerates.</summary>
		bool TryFindWindow(string className, string title, out nint handle);

		bool IsWindow(nint h);
		nint GetForegroundHandle();
		bool TryGetParent(nint h, out nint parent);
		bool TryGetTopLevel(nint h, out nint top);
		bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children);
		bool TryClientToScreen(nint h, ref Point pt);
		uint GetFocusedControlThread(nint window = 0);

		/// <summary>Whether the window belongs in alt-tab/group enumerations (Windows filters out topmost/
		/// tool/owned/shell windows); true everywhere else.</summary>
		bool IncludeInGroups(nint h);

		/// <summary>The window's text lines (the title plus, optionally, hidden child text). Not seeded into the
		/// item's cache because it is a per-call recursive tree-walk, not a cheap batched field — it stays lazy.</summary>
		bool TryGetText(nint h, bool detectHidden, out List<string> text);

		/// <summary>Deepest child window at <see cref="Keysharp.Internals.Window.PointAndHwnd.pt"/> (hit-test used
		/// by ControlFromPoint); mutates <paramref name="pah"/> in place, mirroring the legacy item method.</summary>
		void ChildFindPoint(nint h, Keysharp.Internals.Window.PointAndHwnd pah);

		// Granular single-property reads. These exist so the neutral WindowInfo can read a foreign window's
		// VOLATILE state LIVE per access (an X11/foreign window's Active/Visible/Bounds can change between two
		// reads of the same item — WinWaitActive loops on one held item), exactly as the legacy per-OS items
		// did, instead of paying a full batched snapshot per single-property access. (A Wayland-backend window
		// is served from the item's cached snapshot and never reaches these.)
		string GetTitle(nint h);
		string GetClassName(nint h);
		long GetPid(nint h);
		Rectangle GetBounds(nint h);
		Rectangle GetClientBounds(nint h);
		long GetStyle(nint h);
		long GetExStyle(nint h);
		bool GetActive(nint h);
		bool GetVisible(nint h);
		bool GetEnabled(nint h);
		bool GetHung(nint h);
		bool GetExists(nint h);
		FormWindowState GetWindowState(nint h);
		bool GetAlwaysOnTop(nint h);
		object GetTransparency(nint h);
		object GetTransparentColor(nint h);
		Keysharp.Internals.Window.POINT ClientToScreen(nint h);
	}

	/// <summary>Foreign window MUTATE surface (by handle).</summary>
	/// <remarks>
	/// <para><c>Try*</c> methods on this surface use <c>false</c> as a capability result: the operation is not
	/// implemented, is not exposed by the current platform/compositor, or no backend-specific fallback exists.</para>
	/// <para>If an implementation actually attempts a supported native operation and that operation fails, it should
	/// preserve the existing native/runtime error behavior instead of converting the failure to <c>false</c>. The
	/// user-facing window built-ins translate unsupported <c>false</c> results into <c>OSError</c>; internal
	/// best-effort callers may ignore them.</para>
	/// </remarks>
	internal interface IWindowControl
	{
		bool TrySetAlwaysOnTop(nint h, bool onTop);
		bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize);
		bool TrySetState(nint h, FormWindowState state);
		bool TrySetStyle(nint h, long style);
		bool TrySetExStyle(nint h, long exStyle);
		bool TrySetTransparency(nint h, object alpha);
		bool TryActivate(nint h);
		bool TrySetZOrder(nint h, ZOrder z);
		bool TryClose(nint h);
		bool TryKill(nint h);
		bool TryHide(nint h);
		bool TryShow(nint h);
		bool TryRedraw(nint h);
		bool TryClick(nint h, Point at, uint button, int count);
		bool TrySetTitle(nint h, string title);
		bool TrySetVisible(nint h, bool visible);
		bool TrySetEnabled(nint h, bool enabled);
		bool TrySetTransparentColor(nint h, object color);
	}

	/// <summary>The single resolved foreign-window service, exposing both the read (<see cref="IWindowQuery"/>)
	/// and mutate (<see cref="IWindowControl"/>) surfaces — <c>Platform.Window</c>.</summary>
	internal interface IWindow : IWindowQuery, IWindowControl { }

	/// <summary>Cursor STATE: position (MouseGetPos), shape (A_Cursor), and the clip-warp positioning the input service
	/// cursor-clip enforcement needs. NOT mouse-event injection — synthesized mouse input (MouseMove/Click/Send)
	/// is produced by the input/sender subsystem, unified with keyboard, not here.</summary>
	internal interface IMouse
	{
		/// <summary>Queries only capabilities already granted; this service-layer method must never prompt.</summary>
		bool TryGetCursorPos(out int x, out int y);
		string GetCursorShape();
		void SetCursorShape(string ahkName);

		/// <summary>Whether this service can BOTH query the cursor position AND move it — the exact capability
		/// the input service cursor-clip enforcement needs. Resolves the X11-vs-Wayland question once so callers
		/// (e.g. the hook thread's <c>CanClipCursor</c>) don't re-derive the session shape.</summary>
		bool SupportsCursorQueryAndMove { get; }

		/// <summary>Move the cursor to an absolute screen position (the clip-correction counterpart to
		/// <see cref="TryGetCursorPos"/>): X11 <c>XWarpPointer</c> / the Wayland compositor backend.</summary>
		bool TryMoveAbsolute(int x, int y);

		/// <summary>Live LOGICAL state of a mouse button (VK_LBUTTON/RBUTTON/MBUTTON/XBUTTON1/2), matching the
		/// current OS/session button state used by <c>GetKeyState(button)</c> without the physical option.</summary>
		bool TryGetButtonStateLogical(uint vk, out bool down);

		/// <summary>Live PHYSICAL state of a mouse button (VK_LBUTTON/RBUTTON/MBUTTON/XBUTTON1/2), for
		/// GetKeyState(.., "P") when no mouse hook is tracking it — the cross-platform analogue of Win32
		/// GetAsyncKeyState (X11 <c>XQueryPointer</c> mask, macOS <c>CGEventSourceButtonState</c>, Wayland via
		/// the input service daemon's evdev read). Returns false if this platform cannot answer, so the caller falls
		/// back to the hook-tracked state.</summary>
		bool TryGetButtonStatePhysical(uint vk, out bool down);
	}

	/// <summary>Monitors, work area, screen/window capture.</summary>
	internal interface IScreen
	{
		/// <summary>
		/// A fresh snapshot of every display in native screen space. Callers must not cache it across monitor
		/// hotplug, topology, primary-display, or scale changes.
		/// </summary>
		IReadOnlyList<DisplayInfo> GetDisplays();

		/// <summary>
		/// The expensive per-display metadata for one display out of a <see cref="GetDisplays"/> snapshot — EDID
		/// identity, model, refresh rate, physical size, orientation, connection kind. Separate from
		/// <see cref="GetDisplays"/> on purpose: topology enumeration is on the path of every <c>MonitorGet</c>,
		/// <c>A_ScreenWidth</c>, overlay placement and capture, so it must not start paying for registry/sysfs/
		/// DisplayConfig round-trips that only the monitor-metadata API needs.
		/// <para>Never returns null: a backend that can answer nothing returns <see cref="DisplayDetails.Empty"/>,
		/// whose empty/zero fields the caller reports as unset.</para>
		/// </summary>
		DisplayDetails GetDisplayDetails(DisplayInfo display);

		/// <summary>Captures one rectangle expressed in native screen coordinates as one linearly-mapped bitmap.
		/// A backend may compose or resample display captures internally, but the final bitmap must cover the
		/// complete requested rectangle with one uniform pixel-to-screen transform.</summary>
		bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp);
		bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale);
		bool RequiresAuthorization { get; }

		/// <summary>Gate a capture against the compositor's authorization (keysharp-desktop
		/// handshake); returns NotApplicable where capture needs no separate grant. Resolved per-compositor, so
		/// no <c>is …Backend</c> test at the call site.</summary>
		Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt);
	}

	/// <summary>
	/// Monitor DEVICE control — brightness and raw DDC/CI VCP features. Deliberately its own service rather than
	/// more of <see cref="IScreen"/>: this is hardware control, not topology, and (unlike IScreen) it has no
	/// per-compositor variation on Linux at all — DDC/CI over i2c and logind's backlight interface are kernel and
	/// session facilities that work identically under X11 and every Wayland compositor. Folding it into the
	/// per-compositor IScreen hierarchy would duplicate one implementation across five subclasses.
	/// <para>Follows the <see cref="IWindowControl"/> convention: <c>Try*</c> returning false means "this platform
	/// or this monitor cannot do it", which the script layer turns into an OSError naming the limitation. A native
	/// call that is supported but fails should surface its own error rather than degrade to false.</para>
	/// </summary>
	internal interface IMonitorControl
	{
		/// <summary>Current brightness as a percentage (0-100).</summary>
		bool TryGetBrightness(DisplayInfo display, DisplayDetails details, out int percent);

		/// <summary>Sets brightness as a percentage (0-100); the implementation clamps.</summary>
		bool TrySetBrightness(DisplayInfo display, DisplayDetails details, int percent);

		/// <summary>Reads one DDC/CI VCP feature, returning its current and maximum value.</summary>
		bool TryGetVcp(DisplayInfo display, DisplayDetails details, byte code, out int current, out int max);

		/// <summary>Writes one DDC/CI VCP feature.</summary>
		bool TrySetVcp(DisplayInfo display, DisplayDetails details, byte code, int value);

		/// <summary>A short, actionable reason the operations above are unavailable for this display — used verbatim
		/// in the OSError so the user learns what to install/permit instead of just "not supported".</summary>
		string UnsupportedReason(DisplayInfo display, DisplayDetails details);
	}

	/// <summary>Click-through image overlays — the single cross-platform overlay primitive. The user-facing
	/// Overlay builtin, and Highlight and ToolTip (on Linux/macOS), all render through this. The backing
	/// (Eto / wlr layer-shell / compositor extension) is decided lazily-with-retry on the first Show, per overlay.</summary>
	internal interface IOverlay
	{
		/// <summary>Returns the pixel canvas the renderer prefers for one native screen rectangle. This is a
		/// render-target query, not monitor geometry: Windows normally returns the native size, while Cocoa and
		/// Wayland may return a denser backing size.</summary>
		PixelSize GetCanvasSize(ScreenRect bounds);

		/// <summary>Allocates a drawing surface of the kind this platform presents most cheaply — on Windows one
		/// whose pixels the compositor can read directly, so a frame never has to be copied to be shown. The
		/// caller owns the returned surface and disposes it; it is not tied to an overlay id and outlives any one
		/// backing instance. Null if <paramref name="pixels"/> is empty or the allocation failed.</summary>
		OverlaySurface CreateOverlaySurface(PixelSize pixels);

		/// <summary>
		/// Create or update a click-through image overlay from <paramref name="surface"/>. Synchronous; the
		/// surface remains the caller's live drawing surface and is neither retained nor disposed here.
		/// Non-positive bounds use its raster size as native screen units. The surface carries its own damage —
		/// the region changed since it was last presented — which is a hint; presenting more is always correct.
		/// <paramref name="clickThrough"/> true (the usual) makes the surface transparent to mouse input; false makes
		/// it receive mouse input so an interactive overlay can be built (where the backing supports it).
		/// </summary>
		bool TryPresentImageOverlay(uint id, OverlaySurface surface, ScreenRect bounds, byte opacity,
									bool clickThrough);

		/// <summary>Registers (or, with null, removes) the receiver for pointer events on one overlay's surface.
		/// The sink is stored by id — it survives backing recreation across Hide/Show — and is raised on the UI
		/// thread with surface-local coordinates. Only backings with a client-side input window raise it, and only
		/// while the overlay is not click-through.</summary>
		void SetImageOverlayPointerSink(uint id, Action<OverlayPointerEvent> sink);

		/// <summary>Move/resize an existing image overlay, reusing its last pixels (repositions in place where the
		/// backing can, else re-applies the stored image at the new geometry).</summary>
		bool TryMoveImageOverlay(uint id, ScreenRect bounds);

		/// <summary>Hide and free one image overlay. Idempotent. Confirm-gated: returns false if the withdraw
		/// could not be confirmed, so the caller keeps the id and retries rather than orphaning the surface.</summary>
		bool TryHideImageOverlay(uint id);

		/// <summary>Unconditionally dispose and forget one image overlay's backing, WITHOUT the confirm-gating of
		/// <see cref="TryHideImageOverlay"/>. Used by Destroy to force-reap a backing whose withdraw could not be
		/// confirmed, so it is never left mapped with no owner left to retry. Idempotent; a no-op for an unknown id.</summary>
		void DisposeImageOverlay(uint id);

		/// <summary>Hide and free every image overlay owned by <paramref name="owner"/>, or every process overlay
		/// when no owner is supplied.</summary>
		bool TryHideAllImageOverlays(Script owner = null);

		/// <summary>Native handle for a toolkit-backed overlay where one exists (Eto/WinForms window or wlr layer
		/// surface), otherwise zero (a compositor-drawn overlay has no client-side window).</summary>
		nint GetImageOverlayHandle(uint id);
	}

	/// <summary>
	/// The canonical, cross-platform clipboard content kinds. Each backend maps a kind to the format names its own
	/// platform actually uses (<see cref="IClipboard.KindFormats"/>) — the names are never normalized, so
	/// <c>Clipboard.Formats</c> keeps reporting what the platform calls things and the kinds carry the portability.
	/// </summary>
	internal enum ClipboardKind
	{
		Text,
		Image,
		Files,
		Html,
		Rtf
	}

	/// <summary>
	/// One item of a multi-format clipboard write. Either a canonical <see cref="Kind"/> — which the backend encodes
	/// into its own platform representation, because only the backend knows what that is — or a platform-native
	/// <see cref="Format"/> name whose <see cref="Value"/> bytes are written verbatim.
	/// </summary>
	internal readonly struct ClipboardEntry
	{
		/// <summary>The canonical kind, or <c>null</c> when this entry names a native format.</summary>
		internal ClipboardKind? Kind { get; }

		/// <summary>The platform-native format name, or <c>null</c> when this entry is a canonical kind.</summary>
		internal string Format { get; }

		/// <summary>The payload: <c>string</c> (Text/Html/Rtf), <c>string[]</c> (Files), <see cref="Bitmap"/>
		/// (Image), or <c>byte[]</c> (a native format).</summary>
		internal object Value { get; }

		private ClipboardEntry(ClipboardKind? kind, string format, object value)
		{
			Kind = kind;
			Format = format;
			Value = value;
		}

		internal static ClipboardEntry Of(ClipboardKind kind, object value) => new (kind, null, value);

		internal static ClipboardEntry Raw(string format, byte[] bytes) => new (null, format, bytes);
	}

	/// <summary>The process clipboard, resolved once like <see cref="IScreen"/>/<see cref="IOverlay"/>. The backend
	/// is chosen at host construction — Windows raw-Win32, a Wayland shell-extension backend (Cinnamon/Muffin, and
	/// any future compositor whose <c>IWaylandBackend</c> exposes clipboard access), or the shared Eto path — so the
	/// clipboard seams (A_Clipboard, ClipboardAll, the Clipboard class, OnClipboardChange) are plain calls with no
	/// per-call <c>is …Backend</c> / <c>if (Cinnamon)</c> test at the call site.
	/// <para>Every implementation reached through <see cref="Platform.Clipboard"/> is wrapped by
	/// <c>UiThreadClipboard</c>, so implementations may assume they run on the UI thread and must never marshal
	/// again themselves.</para></summary>
	internal interface IClipboard
	{
		/// <summary>The clipboard's text (line endings normalized to <c>\n</c>), or "" when there is none.</summary>
		string GetText();

		/// <summary>Set the clipboard to plain text; "" or <c>null</c> clears the clipboard.</summary>
		void SetText(string text);

		/// <summary>Whether the clipboard holds no data in any format.</summary>
		bool IsEmpty { get; }

		/// <summary>The OnClipboardChange event code for the current content: 0 = empty, 1 = text, 2 = other/binary.
		/// "Text" means text or files, matching AHK's <c>CF_NATIVETEXT || CF_HDROP</c> test.</summary>
		int ChangeType();

		/// <summary>A private copy of the clipboard's image (the caller owns and disposes it), or <c>null</c> when
		/// the clipboard holds no image.</summary>
		Bitmap GetImage();

		/// <summary>Put an image on the clipboard.</summary>
		void SetImage(Bitmap image);

		/// <summary>Every format the clipboard currently advertises, under the names the platform itself uses
		/// ("HTML Format", "FileDrop" on Windows; "text/html", "text/uri-list" elsewhere). Empty when the clipboard
		/// is empty — this is the authority <see cref="IsEmpty"/> and <see cref="ChangeType"/> are derived from.</summary>
		string[] GetFormats();

		/// <summary>Whether one platform-native format is present. Separate from <see cref="GetFormats"/> because
		/// some backends can answer a single probe far more cheaply than a full enumeration.</summary>
		bool Has(string format);

		/// <summary>The platform-native format names this backend recognizes for a canonical kind, most preferred
		/// first. Never empty; the names need not currently be present on the clipboard.</summary>
		string[] KindFormats(ClipboardKind kind);

		/// <summary>Whether the clipboard currently holds a canonical kind — one format enumeration, not one probe
		/// per candidate name.</summary>
		bool HasKind(ClipboardKind kind);

		/// <summary>One format's raw bytes exactly as the platform stores them, or <c>null</c> when absent.</summary>
		byte[] GetData(string format);

		/// <summary>A text-ish kind (Text/Html/Rtf) decoded to script-level text, WITHOUT the platform's envelope —
		/// Windows wraps HTML in a CF_HTML header, everyone else stores bare markup. "" when absent. The mirror of
		/// the encoding <see cref="SetAll"/> does on the way in, so the envelope is a backend concern at both ends
		/// rather than an <c>if (Windows)</c> at the call site.</summary>
		string GetKindText(ClipboardKind kind);

		/// <summary>The clipboard's file list as local paths, empty when it holds none.</summary>
		string[] GetFiles();

		/// <summary>Publish every entry in ONE clipboard transaction, so the formats coexist instead of overwriting
		/// each other. A null/empty list clears the clipboard.</summary>
		void SetAll(IReadOnlyList<ClipboardEntry> entries);

		/// <summary>Serialize every clipboard format into an opaque blob (the <c>ClipboardAll()</c> save).</summary>
		byte[] CaptureAll();

		/// <summary>Restore a blob produced by <see cref="CaptureAll"/> (the <c>A_Clipboard := ClipboardAll</c> restore).</summary>
		void RestoreAll(Keysharp.Builtins.ClipboardAll clip);

		/// <summary>Subscribe to clipboard-change notifications; <paramref name="onChanged"/> may be invoked on any
		/// thread (the caller marshals to the UI thread). Returns an <see cref="IDisposable"/> to unsubscribe, or
		/// <c>null</c> when this backend has no watchable change source (the caller then simply does not monitor).</summary>
		IDisposable Subscribe(Action onChanged);
	}

	/// <summary>Window lifecycle/state events. Owns the per-platform window-event backend selection and
	/// exposes the chosen one.</summary>
	internal interface IWindowEvents
	{
		Keysharp.Internals.Window.IWindowEventBackend CreateBackend(Script owner);
	}

	/// <summary>Display-configuration change notifications, behind <c>Ks.Monitor.OnChange</c>. Its own service
	/// rather than another property on <see cref="IWindowEvents"/>: the native sources have nothing in common with
	/// the window-event ones (SystemEvents / GDK monitor signals / CoreGraphics reconfiguration), and on Linux this
	/// one needs no per-compositor selection at all.</summary>
	internal interface IMonitorEvents
	{
		Keysharp.Internals.Window.IMonitorEventBackend CreateBackend(Script owner);
	}

	/// <summary>Session control (logout/shutdown/reboot), DE-keyed on Linux. <paramref name="flags"/> follows
	/// the AHK Shutdown codes (1=shutdown, 2=reboot, 4=force, 8=power-down, 0=logoff).</summary>
	internal interface ISession
	{
		bool ExitProgram(uint flags, uint reason);
	}

	/// <summary>Global hotkey registration + hotkey-message posting. MUST execute on the grab-owning hook
	/// thread (X11 XGrabKey is bound to the [ThreadStatic] connection that established it).</summary>
	internal interface IHotkeys
	{
		bool Register(nint hWnd, uint id, KeyModifiers modifiers, uint vk);
		bool Unregister(nint hWnd, uint id);
		bool PostHotkeyMessage(nint hWnd, uint wParam, uint lParam);
	}

	/// <summary>Platform-neutral view of the keyboard/mouse injection transport.</summary>
	internal interface IInput
	{
		/// <summary>The live injection transport. Fixed on macOS/Windows.</summary>
		InputTransport ActiveTransport { get; }
	}

	/// <summary>Live OS/session keyboard state query surface.</summary>
	internal interface IKeyboard
	{
		/// <summary>Current logical modifier state, represented with Keysharp MOD_* left/right bits.</summary>
		bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null);

		/// <summary>Current authoritative physical/device modifier state, represented with Keysharp MOD_*
		/// left/right bits. Returns false when the platform cannot distinguish physical from logical state, or
		/// when only some keys could be probed; mods is then partial rather than meaningful, so callers must
		/// discard it and fall back rather than treat the missing bits as "up".</summary>
		bool TryGetModifierLRStatePhysical(out uint mods);

		/// <summary>Current OS/session logical down/up state for the given VK. On Windows this intentionally
		/// models Win32 GetAsyncKeyState-style current state, not GetKeyState's thread-message-queue state.</summary>
		bool TryGetKeyStateLogical(uint vk, out bool isDown);

		/// <summary>Current authoritative physical/device down/up state for the given VK.
		/// Returns false when the platform cannot distinguish physical from logical state.</summary>
		bool TryGetKeyStatePhysical(uint vk, out bool isDown);

		/// <summary>Current lock/toggle states when the platform exposes them.</summary>
		bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn);
	}
}
