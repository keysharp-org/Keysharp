#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Singleton chooser for <see cref="IWaylandBackend"/>. Probes the running
	/// compositor on first access and caches the result. Override probing with
	/// <c>KEYSHARP_WAYLAND_BACKEND=auto|kwin|sway|hyprland|cosmic|gnome|cinnamon|none</c>.
	/// </summary>
	internal static class WaylandBackend
	{
		private static readonly object sync = new();
		private static IWaylandBackend current;
		private static bool probed;

		internal static IWaylandBackend Current
		{
			get
			{
				lock (sync)
				{
					if (probed)
						return current;

					probed = true;
					current = Probe();
					return current;
				}
			}
		}

		/// <summary>Test/process-host lifecycle reset. Call only when no platform operation is in flight.</summary>
		internal static void Reset()
		{
			IWaylandBackend previous;

			lock (sync)
			{
				previous = current;
				current = null;
				probed = false;
			}

			(previous as IDisposable)?.Dispose();
			WaylandLayerShellClient.Reset();
			CosmicImageCapture.Reset();
			WaylandForeignToplevels.Reset();
			WaylandScreenCapture.Reset();
			WaylandVirtualPointerClient.Reset();
		}

		private static IWaylandBackend Probe()
		{
			// These backends drive the *Wayland* compositor's privileged introspection (cursor/window
			// geometry, z-order, input injection, overlays). In an X11/Xorg session the always-present X
			// server owns all of that, so a Wayland backend must never engage — not even when
			// XDG_CURRENT_DESKTOP names a compositor (GNOME and KDE both ship an Xorg session whose
			// XDG_CURRENT_DESKTOP is still "GNOME"/"KDE", which is exactly how a probe-by-desktop picks the
			// wrong path). Gating here, at the single source of every backend, means no current or future
			// caller can pick one up in a non-Wayland session by forgetting its own IsWaylandSession check.
			//
			// This is deliberately above the KEYSHARP_WAYLAND_BACKEND override: that override selects AMONG
			// Wayland compositors within a Wayland session; it is not an escape hatch for running a Wayland
			// path under X11 (every consumer already short-circuits on !IsWaylandSession, so a forced backend
			// would be dead code there anyway). An XWayland *client* in a Wayland session is unaffected:
			// XDG_SESSION_TYPE or WAYLAND_DISPLAY still identifies the session as Wayland.
			if (!Platform.Desktop.IsWaylandSession)
				return null;

			var forced = Environment.GetEnvironmentVariable("KEYSHARP_WAYLAND_BACKEND")?.Trim().ToLowerInvariant();

			if (!string.IsNullOrEmpty(forced))
			{
				return forced switch
				{
					"none" => null,
					"kwin" => new KWinBackend(),
					"sway" => new SwayBackend(),
					"hyprland" => new HyprlandBackend(),
					"cosmic" => new CosmicBackend(),
					"gnome" => new GnomeBackend(),
					"cinnamon" => new CinnamonBackend(),
					_ => AutoProbe()
				};
			}

			return AutoProbe();
		}

		private static IWaylandBackend AutoProbe()
		{
			// Order matters: prefer the compositor whose IPC is richest where multiple
			// detections might collide (e.g. a session set up to talk to both KWin and
			// COSMIC for transition reasons).
			if (KWinBackend.IsAvailable())
				return new KWinBackend();

			if (SwayBackend.IsAvailable())
				return new SwayBackend();

			if (HyprlandBackend.IsAvailable())
				return new HyprlandBackend();

			if (CosmicBackend.IsAvailable())
				return new CosmicBackend();

			if (GnomeBackend.IsAvailable())
				return new GnomeBackend();

			if (CinnamonBackend.IsAvailable())
				return new CinnamonBackend();

			return null;
		}

		private static bool DesktopMatches(string token)
		{
			var current = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");

			if (string.IsNullOrEmpty(current))
				return false;

			foreach (var part in current.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				if (part.Equals(token, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		private static bool EnvironmentContains(string variable, string token)
		{
			var value = Environment.GetEnvironmentVariable(variable);
			return !string.IsNullOrEmpty(value) && value.Contains(token, StringComparison.OrdinalIgnoreCase);
		}

		internal sealed class KWinBackend : IWaylandBackend
		{
			private readonly object handleLock = new();
			private readonly Dictionary<string, nint> handlesById = new(StringComparer.Ordinal);
			private readonly Dictionary<nint, string> idsByHandle = new();

			public string Name => "KWin";

			// KWin FakeInput evdev button codes (Linux BTN_* constants).
			private const uint BtnLeft    = 0x110;
			private const uint BtnRight   = 0x111;
			private const uint BtnMiddle  = 0x112;
			private const uint BtnBack    = 0x116;
			private const uint BtnForward = 0x115;

			internal static bool IsAvailable()
				=> DesktopMatches("KDE")
				   || EnvironmentContains("DESKTOP_SESSION", "kde")
				   || EnvironmentContains("DESKTOP_SESSION", "plasma")
				   || EnvironmentContains("XDG_SESSION_DESKTOP", "kde")
				   || EnvironmentContains("XDG_SESSION_DESKTOP", "plasma")
				   || string.Equals(Environment.GetEnvironmentVariable("KDE_FULL_SESSION"), "true", StringComparison.OrdinalIgnoreCase)
				   || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KDE_SESSION_VERSION"));

			public bool SupportsMouse => true;

			public bool TryGetCursorPos(out int x, out int y)
				=> KWinDBusBridge.QueryCursorPosition(out x, out y);

			// KWin's public idle-time integration supplies timeout notifications but no exact current-idle
			// query, and the KWin scripting API exposes no idle duration. Do not manufacture a zero via a script.
			public bool TryGetIdleTime(out long milliseconds)
			{
				milliseconds = 0;
				return false;
			}

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> KWinFakeInputBridge.TryMoveAbsolute(x, y);

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> KWinFakeInputBridge.TryMoveRelative(dx, dy);

			public bool TrySendMouseButton(uint button, bool pressed)
			{
				var evdev = button switch
				{
					1 => BtnLeft,
					2 => BtnMiddle,
					3 => BtnRight,
					8 => BtnBack,
					9 => BtnForward,
					_ => 0u
				};
				return evdev != 0 && KWinFakeInputBridge.TryButton(evdev, pressed);
			}

			public bool TrySendMouseScroll(int delta, bool vertical)
			{
				// KWin FakeInput axis: 0 = vertical, 1 = horizontal.
				// KWin convention: positive delta = scroll down/right.
				// AHK/Keysharp convention: positive delta = scroll up/right.
				// Negate the vertical axis; horizontal direction is the same.
				var axis      = vertical ? 0u : 1u;
				var kwinDelta = vertical ? -(double)delta / 120.0 : (double)delta / 120.0;
				return KWinFakeInputBridge.TryAxis(axis, kwinDelta);
			}

			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				windows = [];

				var json = RunJsonOperation("listWindows", new { includeHidden });
				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok") || !root.TryGetProperty("windows", out var arr) || arr.ValueKind != JsonValueKind.Array)
						return false;

					var parsed = new List<WaylandWindowInfo>();

					foreach (var item in arr.EnumerateArray())
						if (TryParseWindow(item, out var info))
							parsed.Add(info);

					windows = parsed;
					return true;
				}
				catch
				{
					return false;
				}
			}

			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				window = null;
				var json = RunJsonOperation("activeWindow");
				return TryParseSingleWindow(json, out window);
			}

			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				window = null;

				if (!TryGetCompositorId(handle, out var id))
					return false;

				var json = RunJsonOperation("getWindow", new { id }, "get_window");
				return TryParseSingleWindow(json, out window);
			}

			public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			{
				window = null;
				// Identify our passive layer-shell OSDs without excluding another process's potentially
				// interactive special window.  The KWin script uses this only for at-point hit testing.
				var json = RunJsonOperation("windowAt", new { x, y, ownPid = Environment.ProcessId }, "window_at");
				return TryParseSingleWindow(json, out window);
			}

			public bool TryGetWorkArea(out Rectangle area)
			{
				area = Rectangle.Empty;

				var json = RunJsonOperation("workArea", null, "workarea");
				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok") || !root.TryGetProperty("area", out var a) || a.ValueKind != JsonValueKind.Object)
						return false;

					var rect = new Rectangle(JsonInt(a, "x"), JsonInt(a, "y"), JsonInt(a, "width"), JsonInt(a, "height"));
					if (rect.Width <= 0 || rect.Height <= 0)
						return false;

					area = rect;
					return true;
				}
				catch
				{
					return false;
				}
			}

			public bool TryActivateWindow(nint handle)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("activate", new { id });
			}

			public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
				=> RunCommandOperation("reserveWindow",
					new { pid = Environment.ProcessId, cookie = cookie.ToString(), x, y, ttlMs }, "reserve_window");

			public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
			{
				handle = 0;
				compositorId = "";
				var json = RunJsonOperation("getReservedWindow",
					new { pid = Environment.ProcessId, cookie = cookie.ToString() }, "get_reserved");

				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok") || !JsonString(root, "id", out var id) || id.IsNullOrEmpty())
						return false;

					compositorId = id;
					handle = GetOrCreateHandle(id);
					return true;
				}
				catch
				{
					return false;
				}
			}

			public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("moveResize", new
				{
					id,
					setPosition,
					setSize,
					x = bounds.X,
					y = bounds.Y,
					width = bounds.Width,
					height = bounds.Height
				}, "move_resize");
			}

			public bool TrySetNoBorder(nint handle, bool noBorder)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				// KWin's per-window noBorder removes the server-side decoration (titlebar) without putting the
				// window into GTK client-side-decoration mode — so, unlike the empty-titlebar CSD trick, it does
				// not make GTK manage (and clobber) the surface input region. That is what lets a click-through
				// overlay be both borderless and input-transparent.
				return RunCommandOperation("setNoBorder", new { id, noBorder }, "no_border");
			}

			public bool TrySetSkipTaskbar(nint handle, bool skip)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("setSkipTaskbar", new { id, skip }, "skip_taskbar");
			}

			public bool TrySetWindowState(nint handle, FormWindowState state)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				var stateName = state switch
				{
					FormWindowState.Minimized => "minimized",
					FormWindowState.Maximized => "maximized",
					_ => "normal"
				};

				return RunCommandOperation("setWindowState", new { id, state = stateName }, "state");
			}

			public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("setAlwaysOnTop", new { id, onTop }, "keepabove");
			}

			public bool TrySetZOrder(nint handle, ZOrder z)
			{
				if (z != ZOrder.Top || !TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("raise", new { id });
			}

			public bool TrySetTransparency(nint handle, object alpha)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				var opacity = AlphaToOpacity(alpha);

				return RunCommandOperation("setOpacity", new { id, opacity }, "opacity");
			}

			public bool SupportsTransparency => true;

			public bool TryCloseWindow(nint handle)
			{
				if (!TryGetCompositorId(handle, out var id))
					return false;

				return RunCommandOperation("close", new { id });
			}

			public bool SupportsWindowEvents => true;

			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			{
				if (sink == null)
					return null;

				var script = KWinDBusBridge.WindowHelpersJs + "\n" + EventScriptBody;

				void OnEvent(string type, string json)
				{
					var kind = MapEventKind(type);

					if (kind == null || json.IsNullOrEmpty())
						return;

					try
					{
						using var doc = JsonDocument.Parse(json);

						if (TryParseWindow(doc.RootElement, out var info) && info.Handle != 0)
							sink(new WaylandWindowEvent(kind.Value, info.Handle) { Bounds = info.FrameGeometry });
					}
					catch
					{
					}
				}

				return RecoveringSubscription.Create(
					onError =>
					{
						var token = Guid.NewGuid().ToString("N");
						try { return KWinDBusBridge.StartEventScriptAsync(token, script, OnEvent).GetAwaiter().GetResult(); }
						catch (Exception ex) { onError(ex); return null; }
					},
					() => new WaylandPollingEventSource(this, sink),
					() => KWinDBusBridge.HasOwner,
					KWinDBusBridge.SubscribeAvailability);
			}

			private static WaylandWindowEventKind? MapEventKind(string type) => type switch
			{
				"create"   => WaylandWindowEventKind.Created,
				"close"    => WaylandWindowEventKind.Closed,
				"active"   => WaylandWindowEventKind.Activated,
				"title"    => WaylandWindowEventKind.TitleChanged,
				"minimize" => WaylandWindowEventKind.Minimized,
				"restore"  => WaylandWindowEventKind.Restored,
				"move"     => WaylandWindowEventKind.MoveResized,
				_          => null
			};

			// Persistent KWin script body (prefixed with KWinDBusBridge.WindowHelpersJs — shared with the operation
			// script so windowId() cannot drift): connects workspace + per-window signals and pushes events via the
			// harness-provided emit(type, value). Existing windows get their per-window signals hooked without
			// re-emitting "create" (they already exist). KWin 6 signal names are tried first, then the KWin 5
			// client* equivalents.
			private const string EventScriptBody = """
function connectFirst(names, handler) {
  for (var i = 0; i < names.length; ++i) {
    try {
      var sig = workspace[names[i]];
      if (sig && typeof sig.connect === "function") { sig.connect(handler); return true; }
    } catch (e) {}
  }
  return false;
}
function tryConnect(obj, name, handler) {
  try {
    var sig = obj[name];
    if (sig && typeof sig.connect === "function") { sig.connect(handler); return true; }
  } catch (e) {}
  return false;
}
function trackWindow(w) {
  if (!w) return;
  tryConnect(w, "captionChanged", function() { var info = windowInfo(w, true); if (info) emit("title", info); });
  tryConnect(w, "minimizedChanged", function() {
    var info = windowInfo(w, true);
    if (info) emit(safeBool(w.minimized) ? "minimize" : "restore", info);
  });
  if (!tryConnect(w, "frameGeometryChanged", function() { var info = windowInfo(w, true); if (info) emit("move", info); }))
    tryConnect(w, "geometryChanged", function() { var info = windowInfo(w, true); if (info) emit("move", info); });
}
function onAdded(w) {
  var info = windowInfo(w);
  if (!info) return;
  trackWindow(w);
  emit("create", info);
}
function onRemoved(w) {
  if (!w) return;
  var id = windowId(w);
  if (id) emit("close", { id: id });
}
function onActivated(w) {
  if (!w) return;
  var info = windowInfo(w, true);
  if (info) emit("active", info);
}
connectFirst(["windowAdded", "clientAdded"], onAdded);
connectFirst(["windowRemoved", "clientRemoved"], onRemoved);
connectFirst(["windowActivated", "clientActivated"], onActivated);
var __order = windowListCompat();
for (var __i = 0; __i < __order.length; ++__i) {
  if (windowInfo(__order[__i])) trackWindow(__order[__i]);
}
""";

			private string RunJsonOperation(string operation, object args = null, string requestPrefix = null)
			{
				try
				{
					return KWinDBusBridge.RunJsonOperation(operation, args, requestPrefix ?? operation);
				}
				catch
				{
					return null;
				}
			}

			private bool RunCommandOperation(string operation, object args = null, string requestPrefix = null)
			{
				try
				{
					return KWinDBusBridge.RunCommandOperation(operation, args, requestPrefix ?? operation);
				}
				catch
				{
					return false;
				}
			}

			private bool TryParseSingleWindow(string json, out WaylandWindowInfo window)
			{
				window = null;

				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok")
						|| !root.TryGetProperty("window", out var item)
						|| item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
						return false;

					return TryParseWindow(item, out window);
				}
				catch
				{
					return false;
				}
			}

			internal bool TryParseWindow(JsonElement item, out WaylandWindowInfo info)
			{
				info = null;

				if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty())
					return false;

				info = new WaylandWindowInfo(
					handle: GetOrCreateHandle(id),
					compositorId: id,
					title: JsonString(item, "title"),
					appId: JsonString(item, "appId"),
					pid: JsonLong(item, "pid"),
					frameGeometry: JsonRectangle(item, "frame"),
					clientGeometry: JsonRectangle(item, "client"),
					surfaceGeometry: JsonRectangle(item, "buffer"),
					active: JsonBool(item, "active"),
					minimized: JsonBool(item, "minimized"),
					maximized: JsonBool(item, "maximized"),
					visible: JsonBool(item, "visible"),
					alwaysOnTop: JsonBool(item, "alwaysOnTop"),
					decorated: !item.TryGetProperty("decorated", out _) || JsonBool(item, "decorated"),
					transparency: item.TryGetProperty("transparency", out _) ? JsonLong(item, "transparency") : -1L);
				return true;
			}

			private static double AlphaToOpacity(object alpha)
			{
				if (alpha is string s && s.Equals("off", StringComparison.OrdinalIgnoreCase))
					return 1.0;

				return Math.Clamp((int)alpha.Al(), 0, 255) / 255.0;
			}

			private nint GetOrCreateHandle(string id)
			{
				lock (handleLock)
				{
					if (handlesById.TryGetValue(id, out var handle))
						return handle;

					handle = DeriveHandle(id);
					handlesById[id] = handle;
					idsByHandle[handle] = id;
					return handle;
				}
			}

			// Derive a stable 64-bit handle from the compositor's window id (a UUID string for
			// KWin). Deterministic so the same window keeps the same handle across separate
			// Keysharp runs in the same Plasma session — `ahk_id <handle>` round-trips between
			// scripts. Bit 62 is set so the result is positive and lives above the 32-bit X11
			// XID range, eliminating collisions with the X11 fallback path. Non-UUID ids fall
			// back to a string hash (fallback-id windows still get a stable handle).
			private static nint DeriveHandle(string id)
			{
				long folded;

				if (Guid.TryParse(id, out var guid))
				{
					var bytes = guid.ToByteArray();
					folded = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
				}
				else
				{
					// FNV-1a on the id bytes — only used for non-UUID fallback ids.
					folded = unchecked((long)0xcbf29ce484222325UL);

					foreach (var ch in id)
						folded = unchecked((folded ^ ch) * 0x100000001b3L);
				}

				return new nint((folded & 0x3FFF_FFFF_FFFF_FFFFL) | 0x4000_0000_0000_0000L);
			}

			private bool TryGetCompositorId(nint handle, out string id)
			{
				lock (handleLock)
					return idsByHandle.TryGetValue(handle, out id);
			}

			public bool IsKnown(nint handle) => TryGetCompositorId(handle, out _);

			/// <summary>
			/// The KWin internalId UUID for a window handle, in the canonical <c>{…}</c> form that
			/// org.kde.KWin.ScreenShot2.CaptureWindow expects (matching QUuid::toString()). False for
			/// fallback (non-UUID "windowId:") ids or unknown handles — those have no capturable internalId.
			/// </summary>
			internal bool TryGetWindowUuid(nint handle, out string uuid)
			{
				uuid = null;

				if (TryGetCompositorId(handle, out var id) && Guid.TryParse(id, out var guid))
				{
					uuid = guid.ToString("B");
					return true;
				}

				return false;
			}
		}

		/// <summary>
		/// Wayland backend for GNOME Shell. Communicates with the bundled
		/// keysharp@keysharp.io GNOME Shell extension via D-Bus. The extension
		/// runs inside the compositor process and therefore has access to all
		/// Mutter window state that is normally inaccessible to foreign clients.
		/// </summary>
		internal sealed class GnomeBackend : IWaylandBackend
		{
			// Bit 61 set marks a handle as originating from this backend.
			// Bit 62 is already used by KWinBackend; X11 XIDs are 32-bit; so
			// bit 61 gives a collision-free range for GNOME stable_sequences.
			private const long GnomeBit = unchecked((long)0x2000_0000_0000_0000L);

			public string Name => "GNOME";

			public bool SupportsMouse => true;

			internal static bool IsAvailable()
				=> DesktopMatches("GNOME")
				   || DesktopMatches("ubuntu")
				   || EnvironmentContains("DESKTOP_SESSION", "gnome")
				   || EnvironmentContains("XDG_SESSION_DESKTOP", "gnome")
				   || EnvironmentContains("XDG_CURRENT_DESKTOP", "gnome");

			public bool TryGetCursorPos(out int x, out int y)
				=> GnomeShellBridge.QueryCursorPosition(out x, out y);

			public bool TryGetIdleTime(out long milliseconds)
				=> GnomeShellBridge.QueryIdleTime(out milliseconds);

			public bool TryGetWorkArea(out Rectangle area)
				=> GnomeShellBridge.QueryWorkArea(out area);

			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				windows = [];
				var json = GnomeShellBridge.QueryWindowList(includeHidden);

				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok") || !root.TryGetProperty("windows", out var arr) || arr.ValueKind != JsonValueKind.Array)
						return false;

					var parsed = new List<WaylandWindowInfo>();

					foreach (var item in arr.EnumerateArray())
						if (TryParseWindow(item, out var info))
							parsed.Add(info);

					windows = parsed;
					return true;
				}
				catch
				{
					return false;
				}
			}

			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				window = null;
				var json = GnomeShellBridge.QueryActiveWindow();
				return TryParseSingleWindow(json, out window);
			}

			public bool IsKnown(nint handle) => TryHandleToSeq(handle, out _);

			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				window = null;

				if (!TryHandleToSeq(handle, out var seq))
					return false;

				// Fetch the full list and find by handle — the extension has no
				// single-window-by-id method, but the list is cheap.
				if (!TryListWindows(true, out var all))
					return false;

				window = all.FirstOrDefault(w => w.Handle == handle);
				return window != null;
			}

			public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			{
				window = null;

				// Walk the window list top-to-bottom (list is bottom-to-top order) and return the first
				// window whose frame contains the point. Skip windows parked on other workspaces — the
				// actor list spans all of them.
				if (!TryListWindows(false, out var all))
					return false;

				for (var i = all.Count - 1; i >= 0; i--)
				{
					var w = all[i];

					if (w.OnCurrentWorkspace && w.FrameGeometry.Contains(x, y))
					{
						window = w;
						return true;
					}
				}

				return false;
			}

			public bool TryActivateWindow(nint handle)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return GnomeShellBridge.SendFocusWindow(seq);
			}

			public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return GnomeShellBridge.SendMoveResize(
					seq,
					setPosition ? bounds.X : int.MinValue,
					setPosition ? bounds.Y : int.MinValue,
					setSize && bounds.Width > 0 ? bounds.Width : 0,
					setSize && bounds.Height > 0 ? bounds.Height : 0);
			}

			public bool TrySetWindowState(nint handle, FormWindowState state)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return GnomeShellBridge.SendSetWindowState(seq, WaylandWindowStateProtocol.ToShellExtensionState(state));
			}

			public bool TrySetNoBorder(nint handle, bool noBorder)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				// noBorder == hide titlebar == decorated false. (On GNOME this is a no-op for our own
				// overlays — Mutter doesn't add server-side decorations, so decorated=false already yields
				// no titlebar; the extension only acts on XWayland windows.)
				return GnomeShellBridge.SendSetWindowDecorated(seq, !noBorder);
			}

			public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return GnomeShellBridge.SendSetWindowAbove(seq, onTop);
			}

			public bool TryCloseWindow(nint handle)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return GnomeShellBridge.SendCloseWindow(seq);
			}

			public bool TryKillWindow(nint handle)
				=> TryHandleToSeq(handle, out var seq) && GnomeShellBridge.SendKillWindow(seq);

			public bool TrySetZOrder(nint handle, ZOrder z)
				=> TryHandleToSeq(handle, out var seq)
				   && (z == ZOrder.Top
					   ? GnomeShellBridge.SendRaiseWindow(seq)
					   : z == ZOrder.Bottom && GnomeShellBridge.SendLowerWindow(seq));

			public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
				=> GnomeShellBridge.SendReserveWindow(cookie, x, y, ttlMs);

			public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
			{
				compositorId = GnomeShellBridge.SendGetReservedWindow(cookie);
				handle = compositorId.Length > 0 && ulong.TryParse(compositorId, out var seq) ? SeqToHandle(seq) : 0;
				return handle != 0;
			}

			public bool TrySetTransparency(nint handle, object alpha)
				=> TryHandleToSeq(handle, out var seq) && GnomeShellBridge.SendSetOpacity(seq, alpha);

			public bool SupportsTransparency => true;

				// The Keysharp extension owns the compositor-drawn overlay surface, so its D-Bus service ownership
				// is the capability gate. A stale/broken extension that owns the name but errors on the actual
				// overlay call is handled reactively by TryShowImageOverlay's tri-state result (a definitive
				// Failed falls back to Eto), not by a separate up-front probe.
				public bool SupportsImageOverlay => GnomeShellBridge.ExtensionServiceHasOwner();

				// GNOME was selected from the desktop/session itself. Always let the real Show RPC decide; a transient
				// NameHasOwner miss must not permanently bind a newly-created overlay to the Eto fallback.
				public bool CanAttemptImageOverlay => true;

				public OverlayShowResult TryShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
					=> GnomeShellBridge.SendShowImageOverlay(id, x, y, width, height, pngBytes);

				public bool TryMoveImageOverlay(uint id, int x, int y, int width, int height)
					=> GnomeShellBridge.SendMoveImageOverlay(id, x, y, width, height);

				public bool TryHideImageOverlay(uint id)
					=> GnomeShellBridge.SendHideImageOverlay(id);

				public bool SupportsWindowEvents => true;

			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			{
				if (sink == null)
					return null;

				void OnEvent(string type, string json)
				{
					var kind = MapEventKind(type);

					if (kind == null || json.IsNullOrEmpty())
						return;

					try
					{
						using var doc = JsonDocument.Parse(json);

						if (TryParseWindow(doc.RootElement, out var info) && info.Handle != 0)
							sink(new WaylandWindowEvent(kind.Value, info.Handle) { Bounds = info.FrameGeometry });
					}
					catch
					{
					}
				}

				// GNOME has no independent query channel when the extension is absent, so remain quiescent and
				// promote when its watched D-Bus owner returns instead of polling that same unavailable service.
				return RecoveringSubscription.Create(
					onError => GnomeShellBridge.WatchWindowEvent(OnEvent, onError),
					null,
					GnomeShellBridge.ExtensionServiceHasOwner,
					GnomeShellBridge.SubscribeExtensionAvailability);
			}

			private static WaylandWindowEventKind? MapEventKind(string type) => type switch
			{
				"create"   => WaylandWindowEventKind.Created,
				"close"    => WaylandWindowEventKind.Closed,
				"active"   => WaylandWindowEventKind.Activated,
				"title"    => WaylandWindowEventKind.TitleChanged,
				"minimize" => WaylandWindowEventKind.Minimized,
				"restore"  => WaylandWindowEventKind.Restored,
				"move"     => WaylandWindowEventKind.MoveResized,
				_          => null
			};

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> GnomeShellBridge.SendMouseMoveAbsolute(x, y);

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> GnomeShellBridge.SendMouseMoveRelative(dx, dy);

			public bool TrySendMouseButton(uint button, bool pressed)
				=> GnomeShellBridge.SendMouseButton(button, pressed);

			public bool TrySendMouseScroll(int delta, bool vertical)
				=> GnomeShellBridge.SendMouseScroll(delta, vertical);

				// Clipboard runs only through the extension (Mutter exposes no data-control protocol). The recovering
				// clipboard router uses this real protocol probe (not mere name ownership) to promote/demote at runtime.
				// Raw MIME <-> bytes; higher layers map formats onto it.
				public bool SupportsClipboard => GnomeShellBridge.SupportsClipboard();

				public string[] GetClipboardMimetypes()
					=> GnomeShellBridge.GetClipboardMimetypes();

				public byte[] GetClipboardContent(string mimetype)
					=> GnomeShellBridge.GetClipboardContent(mimetype);

				public bool SetClipboardContent(string mimetype, byte[] bytes)
					=> GnomeShellBridge.SetClipboardContent(mimetype, bytes);

				public string GetClipboardText()
					=> GnomeShellBridge.GetClipboardText();

				public bool SetClipboardText(string text)
					=> GnomeShellBridge.SetClipboardText(text);

				public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler, Action<Exception> onError = null)
					=> handler == null ? null : GnomeShellBridge.WatchClipboardChanged(handler, onError);

				public IDisposable SubscribeClipboardAvailability(Action handler)
					=> GnomeShellBridge.SubscribeExtensionAvailability(handler);

			// ---- helpers ------------------------------------------------

			private static bool TryParseWindow(JsonElement item, out WaylandWindowInfo info)
			{
				info = null;

				if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty())
					return false;

				if (!ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
					return false;

				info = new WaylandWindowInfo(
					handle: SeqToHandle(seq),
					compositorId: id,
					title: JsonString(item, "title"),
					appId: JsonString(item, "appId"),
					pid: JsonLong(item, "pid"),
					frameGeometry: JsonRectangle(item, "frame"),
					clientGeometry: JsonRectangle(item, "client"),
					surfaceGeometry: JsonRectangle(item, "buffer"),
					active: JsonBool(item, "active"),
					minimized: JsonBool(item, "minimized"),
					maximized: JsonBool(item, "maximized"),
					visible: JsonBool(item, "visible"),
					alwaysOnTop: JsonBool(item, "alwaysOnTop"),
					decorated: !item.TryGetProperty("decorated", out _) || JsonBool(item, "decorated"),
					transparency: item.TryGetProperty("transparency", out _) ? JsonLong(item, "transparency") : -1L,
					onCurrentWorkspace: !item.TryGetProperty("onCurrentWorkspace", out _) || JsonBool(item, "onCurrentWorkspace"));
				return true;
			}

			private static bool TryParseSingleWindow(string json, out WaylandWindowInfo window)
			{
				window = null;

				if (json.IsNullOrEmpty())
					return false;

				try
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;

					if (!JsonBool(root, "ok")
						|| !root.TryGetProperty("window", out var item)
						|| item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
						return false;

					return TryParseWindow(item, out window);
				}
				catch
				{
					return false;
				}
			}

			// Encode a stable_sequence as an nint handle. Bit 61 marks it
			// as a GNOME handle, keeping it above the 32-bit X11 XID range
			// and separate from KWin's bit-62 handles.
			private static nint SeqToHandle(ulong seq)
				=> new nint((long)((seq & 0xFFFF_FFFF) | (ulong)GnomeBit));

			private static bool TryHandleToSeq(nint handle, out ulong seq)
			{
				var h = handle.ToInt64();

				if ((h & unchecked((long)0x6000_0000_0000_0000L)) == GnomeBit)
				{
					seq = (ulong)(h & 0xFFFF_FFFF);
					return true;
				}

				seq = 0;
				return false;
			}

			/// <summary>
			/// The bare compositor stable_sequence for a window handle, stripping the GnomeBit marker that
			/// <see cref="SeqToHandle"/> applies. The extension matches windows by raw stable_sequence, so
			/// the marker must be removed before capture. False for non-GNOME handles. Mirrors
			/// <see cref="KWinBackend.TryGetWindowUuid"/>.
			/// </summary>
			internal bool TryGetWindowSeq(nint handle, out ulong seq) => TryHandleToSeq(handle, out seq);
		}

		internal sealed class SwayBackend : IWaylandBackend
		{
			public string Name => "sway";

			internal static bool IsAvailable() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK"));

			public bool TryGetCursorPos(out int x, out int y)
			{
				x = 0;
				y = 0;
				// TODO: sway's IPC over $SWAYSOCK doesn't expose cursor pos directly; we'd
				// need to combine GET_TREE (for output rects) with the focused container's
				// rect, or rely on a future protocol addition. May not be implementable
				// without sway changes.
				return false;
			}

			// Sway is selected purely by $SWAYSOCK, which says nothing about whether this build
			// actually advertises zwlr_virtual_pointer_manager_v1 -- SupportsMouse reflects the real,
			// probed capability so WaylandMouseInjection.Backend() can fall through to inputd gracefully.
			public bool SupportsMouse => WaylandVirtualPointerClient.Current != null;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> WaylandVirtualPointerClient.Current?.TryMoveAbsolute(x, y) == true;

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> WaylandVirtualPointerClient.Current?.TryMoveRelative(dx, dy) == true;

			public bool TrySendMouseButton(uint button, bool pressed)
			{
				var evdev = WaylandVirtualPointerClient.MapX11ButtonToEvdev(button);
				return evdev != 0 && WaylandVirtualPointerClient.Current?.TryButton(evdev, pressed) == true;
			}

			public bool TrySendMouseScroll(int delta, bool vertical)
				=> WaylandVirtualPointerClient.Current?.TryScroll(delta, vertical) == true;
		}

		internal sealed class HyprlandBackend : IWaylandBackend
		{
			public string Name => "Hyprland";

			internal static bool IsAvailable() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

			public bool TryGetCursorPos(out int x, out int y)
			{
				x = 0;
				y = 0;

				var sig = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE");

				if (string.IsNullOrEmpty(sig))
					return false;

				// Hyprland's command socket reads a single request, writes the reply, then closes.
				// Newer builds place it under $XDG_RUNTIME_DIR/hypr/<sig>/; older ones used /tmp/hypr/<sig>/.
				var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

				if (!string.IsNullOrEmpty(runtimeDir)
						&& TryQueryCursorPos(Path.Combine(runtimeDir, "hypr", sig, ".socket.sock"), out x, out y))
					return true;

				return TryQueryCursorPos(Path.Combine("/tmp", "hypr", sig, ".socket.sock"), out x, out y);
			}

			// Connects to the given Hyprland command socket, sends "cursorpos", and parses the
			// "X, Y" reply. Returns false (with x/y zeroed) on any failure so the caller can fall back.
			private static bool TryQueryCursorPos(string socketPath, out int x, out int y)
			{
				x = 0;
				y = 0;

				if (!File.Exists(socketPath))
					return false;

				try
				{
					using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
					{
						ReceiveTimeout = 250,
						SendTimeout = 250
					};
					socket.Connect(new UnixDomainSocketEndPoint(socketPath));
					_ = socket.Send(Encoding.ASCII.GetBytes("cursorpos"));

					var sb = new StringBuilder();
					var buffer = new byte[256];
					int read;

					while ((read = socket.Receive(buffer)) > 0)
						_ = sb.Append(Encoding.ASCII.GetString(buffer, 0, read));

					var response = sb.ToString();
					var comma = response.IndexOf(',');

					if (comma >= 0
							&& int.TryParse(response.AsSpan(0, comma).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
							&& int.TryParse(response.AsSpan(comma + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
						return true;
				}
				catch
				{
					// Socket gone, permission denied, malformed reply, etc. — fall through to failure.
				}

				x = 0;
				y = 0;
				return false;
			}

			// Hyprland is selected purely by $HYPRLAND_INSTANCE_SIGNATURE, which says nothing about
			// whether this build actually advertises zwlr_virtual_pointer_manager_v1 -- SupportsMouse
			// reflects the real, probed capability so the caller can fall through to inputd gracefully.
			public bool SupportsMouse => WaylandVirtualPointerClient.Current != null;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> WaylandVirtualPointerClient.Current?.TryMoveAbsolute(x, y) == true;

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> WaylandVirtualPointerClient.Current?.TryMoveRelative(dx, dy) == true;

			public bool TrySendMouseButton(uint button, bool pressed)
			{
				var evdev = WaylandVirtualPointerClient.MapX11ButtonToEvdev(button);
				return evdev != 0 && WaylandVirtualPointerClient.Current?.TryButton(evdev, pressed) == true;
			}

			public bool TrySendMouseScroll(int delta, bool vertical)
				=> WaylandVirtualPointerClient.Current?.TryScroll(delta, vertical) == true;
		}

		internal sealed class CosmicBackend : IWaylandBackend
		{
			public string Name => "COSMIC";

			internal static bool IsAvailable()
				=> DesktopMatches("COSMIC")
					|| EnvironmentContains("DESKTOP_SESSION", "cosmic")
					|| EnvironmentContains("XDG_SESSION_DESKTOP", "cosmic");

			public bool SupportsWindowEvents => WaylandForeignToplevels.Current?.CanListCosmic == true;

			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
				=> sink != null && WaylandForeignToplevels.Current?.CanListCosmic == true
					? new WaylandPollingEventSource(this, sink)
					: null;

			public bool IsKnown(nint handle) => WaylandForeignToplevels.KnowsCosmicCurrent(handle);

			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				var client = WaylandForeignToplevels.Current;
				windows = client?.EnumerateCosmic(includeHidden) ?? [];
				return client?.CanListCosmic == true;
			}

			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				var client = WaylandForeignToplevels.Current;

				if (client != null)
					return client.TryGetCosmicActive(out window);

				window = null;
				return false;
			}

			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				var client = WaylandForeignToplevels.Current;

				if (client != null)
					return client.TryGetCosmic(handle, out window);

				window = null;
				return false;
			}

			public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			{
				var client = WaylandForeignToplevels.Current;

				if (client != null)
					return client.TryGetCosmicAt(x, y, out window);

				window = null;
				return false;
			}

			public bool TryGetCursorPos(out int x, out int y)
			{
				x = 0;
				y = 0;
				// COSMIC toplevel-info supplies window geometry, but neither it nor a standard Wayland protocol
				// exposes the global cursor position.
				return false;
			}

			// COSMIC is selected purely by $XDG_CURRENT_DESKTOP, which says nothing about whether
			// this build actually advertises zwlr_virtual_pointer_manager_v1 -- SupportsMouse reflects
			// the real, probed capability so the caller can fall through to inputd gracefully.
			public bool SupportsMouse => WaylandVirtualPointerClient.Current != null;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> WaylandVirtualPointerClient.Current?.TryMoveAbsolute(x, y) == true;

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> WaylandVirtualPointerClient.Current?.TryMoveRelative(dx, dy) == true;

			public bool TrySendMouseButton(uint button, bool pressed)
			{
				var evdev = WaylandVirtualPointerClient.MapX11ButtonToEvdev(button);
				return evdev != 0 && WaylandVirtualPointerClient.Current?.TryButton(evdev, pressed) == true;
			}

			public bool TrySendMouseScroll(int delta, bool vertical)
				=> WaylandVirtualPointerClient.Current?.TryScroll(delta, vertical) == true;
		}

		// Shared by the nested backends: every compositor bridge returns JSON with the same shape, and these
		// readers are deliberately lenient (a bool may arrive as true, 1, or "true") because the extensions and
		// IPC sockets each pick their own encoding.
		private static bool JsonBool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && JsonBool(value);

		private static bool JsonBool(JsonElement value)
			=> value.ValueKind == JsonValueKind.True
			   || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) && i != 0)
			   || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b) && b);

		private static string JsonString(JsonElement element, string property)
			=> JsonString(element, property, out var value) ? value : string.Empty;

		private static bool JsonString(JsonElement element, string property, out string result)
		{
			result = string.Empty;

			if (!element.TryGetProperty(property, out var value))
				return false;

			result = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
			return true;
		}

		private static long JsonLong(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var value))
				return 0L;

			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var l))
				return l;

			return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out l) ? l : 0L;
		}

		private static Rectangle JsonRectangle(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var rect) || rect.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;

			return new Rectangle(JsonInt(rect, "x"), JsonInt(rect, "y"), JsonInt(rect, "width"), JsonInt(rect, "height"));
		}

		private static int JsonInt(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var value))
				return 0;

			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i))
				return i;

			return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
		}
	}
}
#endif
