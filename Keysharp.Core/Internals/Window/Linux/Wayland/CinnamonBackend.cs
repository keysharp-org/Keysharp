#if LINUX
using Keysharp.Builtins;
using System.Globalization;
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Cin = Keysharp.Internals.DBus.Generated.Cinnamon;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	// The CinnamonShell1 overlay proxy is generated from
	// Internals/Linux/DBus/Interfaces/Cinnamon.xml.

	/// <summary>
	/// Static bridge to the Keysharp Cinnamon extension's image-overlay service.
	/// </summary>
	internal static class CinnamonShellBridge
	{
		private const string ExtensionServiceName = "io.github.keysharp.CinnamonShell";
		private const string ExtensionObjectPath  = "/io/github/keysharp/CinnamonShell";
		private const int    TimeoutMs   = 2000;
		// The first image-overlay upload is cold and large (full-resolution PNG); give it a generous deadline so
		// it isn't classified TimedOut before the shell finishes decoding + uploading it. Mirrors GnomeShellBridge.
		private const int    ImageOverlayTimeoutMs = 10_000;
		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<Cin.CinnamonShell1> extension;
		private static RetryGate highlightOwnerRegistration;
		private static string connectionLocalName = "";
		private static string registeredHighlightOwnerBusName = "";
		private static readonly string HighlightOwnerKey = WaylandOverlayOwner.Key;

		static CinnamonShellBridge()
			=> Initialize();

		private static void Initialize()
		{
			sessions = new RecoverableService<DbusSession>(ConnectSessionBus,
				initialRetryDelay: TimeSpan.FromMilliseconds(500),
				maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension = new WatchedDbusService<Cin.CinnamonShell1>(sessions, ExtensionServiceName,
				new ObjectPath(ExtensionObjectPath), TimeoutMs, (c, d, p) => new Cin.CinnamonShell1(c, d, p));
			highlightOwnerRegistration = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(500), maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension.AvailabilityChanged += ExtensionAvailabilityChanged;
		}

		internal static OverlayShowResult SendShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
			=> pngBytes is { Length: > 0 }
			   ? RunShow(p => p.ShowImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height, pngBytes),
						 ImageOverlayTimeoutMs)
			   : OverlayShowResult.Failed;

		internal static bool SendMoveImageOverlay(uint id, int x, int y, int width, int height)
			=> RunExtensionBool(p => p.MoveImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height));

		internal static bool SendHideImageOverlay(uint id)
			=> RunExtensionBool(p => p.HideImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName));

		private static T RunExtension<T>(Func<Cin.CinnamonShell1, Task<T>> call,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
			=> TryRunExtension(call, out T result, TimeoutMs, operation) ? result : default;

		private static bool TryRunExtension<T>(Func<Cin.CinnamonShell1, Task<T>> call, out T result,
			int timeoutMs = TimeoutMs,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
		{
			result = default;

			try
			{
				if (!extension.TryUse((p, session) =>
				{
					connectionLocalName = session.LocalName;
					RegisterHighlightOwner(p);
					return call(p);
				}, out Task<T> task))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, "extension service is unavailable");
					return false;
				}

				// Pump the message queue while waiting on the extension's reply instead of freezing it — these
				// calls run from hotkey actions / timers on the main thread, and a slow (cold-channel) reply must
				// not stall hotkey processing and the UI.
				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, $"timed out after {timeoutMs} ms");
					return false;
				}

				result = task.GetAwaiter().GetResult();
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", operation, WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		private static bool RunExtensionBool(Func<Cin.CinnamonShell1, Task<bool>> call,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
			=> TryRunExtension(call, out bool result, TimeoutMs, operation) && result;

		// Show an image overlay, classifying the outcome (see OverlayShowResult) so the caller can distinguish an
		// ambiguous timeout — the shell most likely still created the actor, so commit to the compositor — from a
		// definitive failure that may safely fall back to Eto. A plain RunExtensionBool collapses both to false,
		// which is what let a slow first upload spawn a duplicate Eto overlay.
		private static OverlayShowResult RunShow(Func<Cin.CinnamonShell1, Task<bool>> call, int timeoutMs)
		{
			try
			{
				if (!extension.TryUse((p, session) =>
				{
					connectionLocalName = session.LocalName;
					RegisterHighlightOwner(p);
					return call(p);
				}, out Task<bool> task))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", "extension service is unavailable");
					return OverlayShowResult.Failed;
				}

				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay",
						$"timed out after {timeoutMs} ms; the compositor result is ambiguous");
					return OverlayShowResult.TimedOut;
				}

				if (task.GetAwaiter().GetResult())
					return OverlayShowResult.Shown;

				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", "extension returned false");
				return OverlayShowResult.Failed;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "ShowImageOverlay", WaylandBridgeDiagnostics.Describe(ex));
				return OverlayShowResult.Failed;
			}
		}


		private static void RegisterHighlightOwner(Cin.CinnamonShell1 p)
		{
			if (p == null || connectionLocalName.IsNullOrEmpty() || registeredHighlightOwnerBusName == connectionLocalName)
				return;

			using var attempt = highlightOwnerRegistration.TryBegin();

			if (attempt == null)
				return;

			try
			{
				var task = Task.Run(() => p.RegisterHighlightOwnerAsync(HighlightOwnerKey, connectionLocalName));

				// Pump the message loop while waiting (never a plain .Wait) — this runs from hotkey/timer
				// actions and must not stall the pump on a cold D-Bus channel.
				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", $"timed out after {TimeoutMs} ms");
					return;
				}

				if (task.GetAwaiter().GetResult())
				{
					registeredHighlightOwnerBusName = connectionLocalName;
					attempt.Succeed();
				}
				else
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", "extension returned false");
				}
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon Shell", "RegisterHighlightOwner", WaylandBridgeDiagnostics.Describe(ex));
				attempt.Fail(ex);
			}
		}

		internal static bool ExtensionServiceHasOwner() => extension.HasOwner;

		internal static IDisposable SubscribeExtensionAvailability(Action handler)
		{
			if (handler == null)
				return null;

			extension.AvailabilityChanged += handler;
			return new CallbackDisposable(() => extension.AvailabilityChanged -= handler);
		}

		private static DbusSession ConnectSessionBus()
			=> DbusSession.Connect(DBusBus.Session, TimeoutMs, "Cinnamon Shell",
								   (session, reason) => sessions.Invalidate(session, reason));

		private static void ExtensionAvailabilityChanged()
		{
			registeredHighlightOwnerBusName = "";
			highlightOwnerRegistration.Rearm();
		}

		internal static void Reset()
		{
			extension.Dispose();
			sessions.Dispose();
			connectionLocalName = registeredHighlightOwnerBusName = "";
			Initialize();
		}

	}

	/// <summary>
	/// Wayland window-management backend for the Cinnamon desktop (Muffin compositor).
	/// Desktop automation uses keysharp-desktop; the direct shell bridge is retained only
	/// for process-owned overlays.
	/// </summary>
	internal sealed class CinnamonBackend : IWaylandBackend
	{
		private const string BackendName = "cinnamon";
		// Bit 60 marks a handle as Cinnamon's; keeps it above the 32-bit X11 XID range and
		// distinct from GNOME (bit 61) and KWin (bit 62).
		private const long CinnamonBit = unchecked((long)0x1000_0000_0000_0000L);

		public string Name => "Cinnamon";

		public bool SupportsMouse => true;

		internal static bool IsAvailable()
			=> EnvContains("XDG_CURRENT_DESKTOP", "cinnamon")
			   || EnvContains("DESKTOP_SESSION", "cinnamon")
			   || EnvContains("XDG_SESSION_DESKTOP", "cinnamon");

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

					if (TryParseEventWindow(doc.RootElement, out var info) && info.Handle != 0)
					{
						var bounds = info.FrameGeometry.Width > 0 && info.FrameGeometry.Height > 0 ? info.FrameGeometry : (Rectangle?)null;
						sink(new WaylandWindowEvent(kind.Value, info.Handle) { Bounds = bounds });
					}
				}
				catch
				{
				}
			}

			return RecoveringSubscription.Create(
				onError => DesktopClient.WatchWindowEvents(BackendName, OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				() => DesktopClient.ProbeProvider(BackendName),
				CinnamonShellBridge.SubscribeExtensionAvailability);
		}

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
			=> TryParseSingleWindow(DesktopClient.QueryActiveWindow(BackendName), out window);

		public bool IsKnown(nint handle) => TryHandleToSeq(handle, out _);

		/// <summary>The raw stable_sequence for a Cinnamon-tagged handle (the id the extension keys on).</summary>
		internal bool TryGetWindowSeq(nint handle, out ulong seq) => TryHandleToSeq(handle, out seq);

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			window = null;

			if (!TryHandleToSeq(handle, out _) || !TryListWindows(true, out var all))
				return false;

			window = all.FirstOrDefault(w => w.Handle == handle);
			return window != null;
		}

		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
		{
			window = null;

			if (!TryListWindows(false, out var all))
				return false;

			// List is bottom-to-top; walk top-down so the topmost window at the point wins. Skip windows
			// parked on other workspaces — the actor list spans all of them.
			for (var i = all.Count - 1; i >= 0; i--)
			{
				if (all[i].OnCurrentWorkspace && all[i].FrameGeometry.Contains(x, y))
				{
					window = all[i];
					return true;
				}
			}

			return false;
		}

		public bool TryActivateWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && DesktopClient.FocusWindow(BackendName, seq);

		public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> DesktopClient.ReserveWindow(BackendName, cookie, x, y, ttlMs);

		public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			compositorId = DesktopClient.GetReservedWindow(BackendName, cookie);
			handle = compositorId.Length > 0 && ulong.TryParse(compositorId, out var seq) ? SeqToHandle(seq) : 0;
			return handle != 0;
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryHandleToSeq(handle, out var seq)
			   && DesktopClient.MoveResizeWindow(
				   BackendName, seq,
				   setPosition ? bounds.X : int.MinValue,
				   setPosition ? bounds.Y : int.MinValue,
				   setSize && bounds.Width > 0 ? bounds.Width : 0,
				   setSize && bounds.Height > 0 ? bounds.Height : 0);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryHandleToSeq(handle, out var seq)
			   && DesktopClient.SetWindowState(BackendName, seq,
				   WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryHandleToSeq(handle, out var seq) && DesktopClient.SetWindowAbove(BackendName, seq, onTop);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryHandleToSeq(handle, out var seq) && DesktopClient.SetWindowDecorated(BackendName, seq, !noBorder);

		public bool TrySetTransparency(nint handle, object alpha)
		{
			var opacity = alpha is string value && value.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255
				: Math.Clamp((int)alpha.Al(), 0, 255);

			return TryHandleToSeq(handle, out var seq)
				&& DesktopClient.SetWindowOpacity(BackendName, seq, opacity);
		}

		public bool SupportsTransparency => true;

		public bool TrySetZOrder(nint handle, ZOrder z)
			=> TryHandleToSeq(handle, out var seq)
			   && (z == ZOrder.Top
				   ? DesktopClient.RaiseWindow(BackendName, seq)
				   : z == ZOrder.Bottom && DesktopClient.LowerWindow(BackendName, seq));

		public bool TryCloseWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && DesktopClient.CloseWindow(BackendName, seq);

		public bool TryKillWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && DesktopClient.KillWindow(BackendName, seq);

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> DesktopClient.SendMouseMoveAbsolute(BackendName, x, y);

		public bool TrySendMouseMoveRelative(int dx, int dy)
			=> DesktopClient.SendMouseMoveRelative(BackendName, dx, dy);

		public bool TrySendMouseButton(uint button, bool pressed)
			=> DesktopClient.SendMouseButton(BackendName, button, pressed);

		public bool TrySendMouseScroll(int delta, bool vertical)
			=> DesktopClient.SendMouseScroll(BackendName, delta, vertical);

			// The Keysharp extension owns the compositor-drawn overlay + clipboard surface, so its D-Bus
			// service ownership is the single capability gate for both. A stale/broken extension that owns
			// the name but errors on the actual overlay call is handled reactively by TryShowImageOverlay's
			// tri-state result (a definitive Failed falls back to Eto), not by a separate up-front probe.
			public bool SupportsImageOverlay => CinnamonShellBridge.ExtensionServiceHasOwner();

			// Cinnamon was selected from the desktop/session itself, so attempt the authoritative Show RPC even if
			// the cached service-owner hint momentarily misses during startup.
			public bool CanAttemptImageOverlay => true;

			public OverlayShowResult TryShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
				=> CinnamonShellBridge.SendShowImageOverlay(id, x, y, width, height, pngBytes);

			public bool TryMoveImageOverlay(uint id, int x, int y, int width, int height)
				=> CinnamonShellBridge.SendMoveImageOverlay(id, x, y, width, height);

			public bool TryHideImageOverlay(uint id)
				=> CinnamonShellBridge.SendHideImageOverlay(id);

			// Clipboard runs only through the extension (Muffin exposes no data-control protocol). Because the
			// the recovering clipboard router can promote/demote at runtime, this remains a real liveness probe (not
			// mere name ownership). Raw MIME <-> bytes; higher layers map formats onto it.
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
				=> CinnamonShellBridge.SubscribeExtensionAvailability(handler);

		// ---- helpers ------------------------------------------------

		private static bool EnvContains(string variable, string token)
		{
			var value = Environment.GetEnvironmentVariable(variable);
			return !string.IsNullOrEmpty(value) && value.Contains(token, StringComparison.OrdinalIgnoreCase);
		}

		private static bool TryParseWindow(JsonElement item, out WaylandWindowInfo info)
		{
			info = null;

			if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty()
				|| !ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
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

		private static bool TryParseEventWindow(JsonElement item, out WaylandWindowInfo info)
		{
			if (TryParseWindow(item, out info))
				return true;

			info = null;

			if (!JsonString(item, "id", out var id) || id.IsNullOrEmpty()
				|| !ulong.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
				return false;

			info = new WaylandWindowInfo(handle: SeqToHandle(seq), compositorId: id);
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

		private static nint SeqToHandle(ulong seq)
			=> new((long)((seq & 0xFFFF_FFFF) | (ulong)CinnamonBit));

		private static bool TryHandleToSeq(nint handle, out ulong seq)
		{
			var h = handle.ToInt64();

			if ((h & unchecked((long)0x7000_0000_0000_0000L)) == CinnamonBit)
			{
				seq = (ulong)(h & 0xFFFF_FFFF);
				return true;
			}

			seq = 0;
			return false;
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

		private static bool JsonBool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

		private static long JsonLong(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

		private static string JsonString(JsonElement element, string property)
			=> JsonString(element, property, out var value) ? value : string.Empty;

		private static bool JsonString(JsonElement element, string property, out string value)
		{
			if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
			{
				value = prop.GetString() ?? string.Empty;
				return true;
			}

			value = string.Empty;
			return false;
		}

		private static Rectangle JsonRectangle(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var rect) || rect.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;

			return new Rectangle(
				RectInt(rect, "x"),
				RectInt(rect, "y"),
				RectInt(rect, "width"),
				RectInt(rect, "height"));
		}

		private static int RectInt(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
	}
}
#endif
