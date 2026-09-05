#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Singleton chooser for <see cref="IWaylandBackend"/>. Probes the running
	/// compositor on first access and caches the result. Override probing with
	/// <c>KEYSHARP_WAYLAND_BACKEND=auto|kwin|sway|hyprland|cosmic|gnome|cinnamon|generic|none</c>.
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
					"kwin" => new KWinBrokerBackend(),
					"sway" => new SwayBackend(),
					"hyprland" => new HyprlandBackend(),
					"cosmic" => new CosmicBackend(),
					"gnome" => new GnomeBackend(),
					"cinnamon" => new CinnamonBackend(),
					"generic" => new GenericWaylandBackend(),
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
			if (KWinBrokerBackend.IsAvailable())
				return new KWinBrokerBackend();

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

			if (GenericWaylandBackend.IsAvailable())
				return new GenericWaylandBackend();

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

		/// <summary>
		/// Wayland backend for GNOME Shell. Desktop automation uses keysharp-desktop,
		/// whose bundled Shell extension can inspect and control Mutter window state.
		/// The direct D-Bus bridge is retained only for process-owned overlays.
		/// </summary>
		internal sealed class GnomeBackend : IWaylandBackend
		{
			private const string BackendName = "gnome";
			// Bit 61 set marks a handle as originating from this backend.
			// Bit 62 is already used by KWinBrokerBackend; X11 XIDs are 32-bit; so
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
				=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);

			public bool TryGetWorkArea(out Rectangle area)
				=> DesktopClient.QueryWorkArea(BackendName, out area);

			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				windows = [];
				var json = DesktopClient.QueryWindowList(BackendName, includeHidden);

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
				var json = DesktopClient.QueryActiveWindow(BackendName);
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

				return DesktopClient.FocusWindow(BackendName, seq);
			}

			public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return DesktopClient.MoveResizeWindow(BackendName,
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

				return DesktopClient.SetWindowState(BackendName, seq,
					WaylandWindowStateProtocol.ToShellExtensionState(state));
			}

			public bool TrySetNoBorder(nint handle, bool noBorder)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				// noBorder == hide titlebar == decorated false. (On GNOME this is a no-op for our own
				// overlays — Mutter doesn't add server-side decorations, so decorated=false already yields
				// no titlebar; the extension only acts on XWayland windows.)
				return DesktopClient.SetWindowDecorated(BackendName, seq, !noBorder);
			}

			public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return DesktopClient.SetWindowAbove(BackendName, seq, onTop);
			}

			public bool TryCloseWindow(nint handle)
			{
				if (!TryHandleToSeq(handle, out var seq))
					return false;

				return DesktopClient.CloseWindow(BackendName, seq);
			}

			public bool TryKillWindow(nint handle)
				=> TryHandleToSeq(handle, out var seq) && DesktopClient.KillWindow(BackendName, seq);

			public bool TrySetZOrder(nint handle, ZOrder z)
				=> TryHandleToSeq(handle, out var seq)
				   && (z == ZOrder.Top
					   ? DesktopClient.RaiseWindow(BackendName, seq)
					   : z == ZOrder.Bottom && DesktopClient.LowerWindow(BackendName, seq));

			public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
				=> DesktopClient.ReserveWindow(BackendName, cookie, x, y, ttlMs);

			public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
			{
				compositorId = DesktopClient.GetReservedWindow(BackendName, cookie);
				handle = compositorId.Length > 0 && ulong.TryParse(compositorId, out var seq) ? SeqToHandle(seq) : 0;
				return handle != 0;
			}

			public bool TrySetTransparency(nint handle, object alpha)
			{
				var opacity = alpha is string value && value.Equals("off", StringComparison.OrdinalIgnoreCase)
					? 255
					: Math.Clamp((int)alpha.Al(), 0, 255);

				return TryHandleToSeq(handle, out var seq)
					&& DesktopClient.SetWindowOpacity(BackendName, seq, opacity);
			}

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

				return RecoveringSubscription.Create(
					onError => DesktopClient.WatchWindowEvents(BackendName, OnEvent, onError),
					() => new WaylandPollingEventSource(this, sink),
					() => DesktopClient.ProbeProvider(BackendName),
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
				=> DesktopClient.SendMouseMoveAbsolute(BackendName, x, y);

			public bool TrySendMouseMoveRelative(int dx, int dy)
				=> DesktopClient.SendMouseMoveRelative(BackendName, dx, dy);

			public bool TrySendMouseButton(uint button, bool pressed)
				=> DesktopClient.SendMouseButton(BackendName, button, pressed);

			public bool TrySendMouseScroll(int delta, bool vertical)
				=> DesktopClient.SendMouseScroll(BackendName, delta, vertical);

				// Clipboard runs only through the extension (Mutter exposes no data-control protocol). The recovering
				// clipboard router uses this real protocol probe (not mere name ownership) to promote/demote at runtime.
				// Raw MIME <-> bytes; higher layers map formats onto it.
				public bool SupportsClipboard => DesktopClient.ProviderSupportsClipboard(BackendName);

				public string[] GetClipboardMimetypes()
					=> DesktopClient.GetClipboardMimetypes(BackendName);

				public byte[] GetClipboardContent(string mimetype)
					=> DesktopClient.GetClipboardContent(BackendName, mimetype);

				public bool SetClipboardContent(string mimetype, byte[] bytes)
					=> DesktopClient.SetClipboardContent(BackendName, mimetype, bytes);

				public string GetClipboardText()
					=> DesktopClient.GetClipboardText(BackendName);

				public bool SetClipboardText(string text)
					=> DesktopClient.SetClipboardText(BackendName, text);

				public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler, Action<Exception> onError = null)
					=> handler == null ? null : DesktopClient.WatchClipboardChanges(BackendName, handler, onError);

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
			/// <see cref="KWinBrokerBackend.TryGetWindowUuid"/>.
			/// </summary>
			internal bool TryGetWindowSeq(nint handle, out ulong seq) => TryHandleToSeq(handle, out seq);
		}

		internal sealed class SwayBackend : IWaylandBackend
		{
			private readonly GenericWaylandBackend desktop = new();

			public string Name => "sway";

			internal static bool IsAvailable() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK"));

			public bool TryGetCursorPos(out int x, out int y)
				=> desktop.TryGetCursorPos(out x, out y);

			public bool SupportsWindowEvents => desktop?.SupportsWindowEvents == true;
			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
				=> desktop?.SubscribeWindowEvents(sink);
			public bool IsKnown(nint handle) => desktop?.IsKnown(handle) == true;
			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				if (desktop != null)
					return desktop.TryListWindows(includeHidden, out windows);
				windows = [];
				return false;
			}
			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				if (desktop != null)
					return desktop.TryGetActiveWindow(out window);
				window = null;
				return false;
			}
			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				if (desktop != null)
					return desktop.TryGetWindow(handle, out window);
				window = null;
				return false;
			}
			public bool TryActivateWindow(nint handle) => desktop?.TryActivateWindow(handle) == true;
			public bool TrySetWindowState(nint handle, FormWindowState state)
				=> desktop?.TrySetWindowState(handle, state) == true;
			public bool TryCloseWindow(nint handle) => desktop?.TryCloseWindow(handle) == true;

			public bool SupportsMouse => desktop.SupportsMouse;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> desktop.TrySendMouseMoveAbsolute(x, y);
		}

		internal sealed class HyprlandBackend : IWaylandBackend
		{
			private readonly GenericWaylandBackend desktop = new();

			public string Name => "Hyprland";

			internal static bool IsAvailable() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

			public bool TryGetCursorPos(out int x, out int y)
				=> desktop.TryGetCursorPos(out x, out y);

			public bool SupportsWindowEvents => desktop?.SupportsWindowEvents == true;
			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
				=> desktop?.SubscribeWindowEvents(sink);
			public bool IsKnown(nint handle) => desktop?.IsKnown(handle) == true;
			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				if (desktop != null)
					return desktop.TryListWindows(includeHidden, out windows);
				windows = [];
				return false;
			}
			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				if (desktop != null)
					return desktop.TryGetActiveWindow(out window);
				window = null;
				return false;
			}
			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				if (desktop != null)
					return desktop.TryGetWindow(handle, out window);
				window = null;
				return false;
			}
			public bool TryActivateWindow(nint handle) => desktop?.TryActivateWindow(handle) == true;
			public bool TrySetWindowState(nint handle, FormWindowState state)
				=> desktop?.TrySetWindowState(handle, state) == true;
			public bool TryCloseWindow(nint handle) => desktop?.TryCloseWindow(handle) == true;

			public bool SupportsMouse => desktop.SupportsMouse;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> desktop.TrySendMouseMoveAbsolute(x, y);
		}

		internal sealed class CosmicBackend : IWaylandBackend
		{
			private readonly GenericWaylandBackend desktop = new();

			public string Name => "COSMIC";

			internal static bool IsAvailable()
				=> DesktopMatches("COSMIC")
					|| EnvironmentContains("DESKTOP_SESSION", "cosmic")
					|| EnvironmentContains("XDG_SESSION_DESKTOP", "cosmic");

			public bool SupportsWindowEvents => desktop.SupportsWindowEvents;

			public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
				=> desktop.SubscribeWindowEvents(sink);

			public bool IsKnown(nint handle) => desktop.IsKnown(handle);

			public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			{
				return desktop.TryListWindows(includeHidden, out windows);
			}

			public bool TryGetActiveWindow(out WaylandWindowInfo window)
			{
				return desktop.TryGetActiveWindow(out window);
			}

			public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			{
				return desktop.TryGetWindow(handle, out window);
			}

			public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			{
				if (desktop.TryListWindows(true, out var windows))
				{
					window = windows.FirstOrDefault(candidate => candidate.Active
						&& candidate.Visible && candidate.FrameGeometry.Contains(x, y));
					return window != null;
				}

				window = null;
				return false;
			}

			public bool TryActivateWindow(nint handle) => desktop.TryActivateWindow(handle);
			public bool TrySetWindowState(nint handle, FormWindowState state)
				=> desktop.TrySetWindowState(handle, state);
			public bool TryCloseWindow(nint handle) => desktop.TryCloseWindow(handle);

			public bool TryGetCursorPos(out int x, out int y)
				=> desktop.TryGetCursorPos(out x, out y);

			public bool SupportsMouse => desktop.SupportsMouse;

			public bool TrySendMouseMoveAbsolute(int x, int y)
				=> desktop.TrySendMouseMoveAbsolute(x, y);
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
