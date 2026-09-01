#if LINUX
using Keysharp.Builtins;
using System.Globalization;
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Cin = Keysharp.Internals.DBus.Generated.Cinnamon;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	// The Cinnamon, CinnamonShell1, IdleMonitor and DBus proxies are generated from
	// Internals/Linux/DBus/Interfaces/Cinnamon.xml and FreedesktopDBus.xml.

	/// <summary>
	/// Static bridge to the Cinnamon shell services. Permission-gated window and clipboard
	/// operations use <c>keysharp-desktop</c>; process-owned overlays and input conveniences
	/// use the extension or Cinnamon's <c>Eval</c> service.
	/// </summary>
	internal static class CinnamonShellBridge
	{
		private const string ServiceName = "org.Cinnamon";
		private const string ObjectPath  = "/org/Cinnamon";
		private const string ExtensionServiceName = "io.github.keysharp.CinnamonShell";
		private const string ExtensionObjectPath  = "/io/github/keysharp/CinnamonShell";
		private const string IdleMonitorServiceName = "org.cinnamon.Muffin.IdleMonitor";
		private const string IdleMonitorObjectPath = "/org/cinnamon/Muffin/IdleMonitor/Core";
		private const int    TimeoutMs   = 2000;
		// The first image-overlay upload is cold and large (full-resolution PNG); give it a generous deadline so
		// it isn't classified TimedOut before the shell finishes decoding + uploading it. Mirrors GnomeShellBridge.
		private const int    ImageOverlayTimeoutMs = 10_000;
		private const int    ExtensionMissingCacheMs = 5000;
		private const int    ExtensionPresentCacheMs = 1000;
		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<Cin.Cinnamon> cinnamonService;
		private static WatchedDbusService<Cin.CinnamonShell1> extension;
		private static RetryGate highlightOwnerRegistration;
		private static Cin.IdleMonitor idleMonitorProxy;
		private static DbusSession idleMonitorSession;
		private static long clipboardSupportCacheUntil;
		private static bool clipboardSupportCached;
		private static readonly object clipboardSupportSync = new();
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
			cinnamonService = new WatchedDbusService<Cin.Cinnamon>(sessions, ServiceName, new ObjectPath(ObjectPath), TimeoutMs,
				(c, d, p) => new Cin.Cinnamon(c, d, p));
			extension = new WatchedDbusService<Cin.CinnamonShell1>(sessions, ExtensionServiceName,
				new ObjectPath(ExtensionObjectPath), TimeoutMs, (c, d, p) => new Cin.CinnamonShell1(c, d, p));
			highlightOwnerRegistration = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(500), maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension.AvailabilityChanged += ExtensionAvailabilityChanged;
		}

		internal static string QueryActiveWindow()
			=> DesktopClient.QueryActiveWindow("cinnamon");

		internal static bool QueryIdleTime(out long milliseconds)
		{
			milliseconds = 0;
			using var lease = sessions.TryAcquire();

			if (lease == null)
				return false;

			try
			{
				if (!ReferenceEquals(idleMonitorSession, lease.Value))
				{
					idleMonitorSession = lease.Value;
					idleMonitorProxy = new Cin.IdleMonitor(lease.Value.Connection,
						IdleMonitorServiceName, new ObjectPath(IdleMonitorObjectPath));
				}

				var task = idleMonitorProxy.GetIdletimeAsync();

				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("Cinnamon idle monitor", "GetIdletime", $"timed out after {TimeoutMs} ms");
					return false;
				}

				var value = task.GetAwaiter().GetResult();
				milliseconds = value > long.MaxValue ? long.MaxValue : (long)value;
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("Cinnamon idle monitor", "GetIdletime", WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList("cinnamon", includeHidden);

		internal static bool QueryCursorPosition(out int x, out int y)
			=> DesktopClient.QueryCursorPosition("cinnamon", out x, out y);

		internal static bool QueryWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea("cinnamon", out area);

		internal static bool SendFocusWindow(ulong seq)
			=> DesktopClient.FocusWindow("cinnamon", seq);

		internal static bool SendRaiseWindow(ulong seq)
			=> DesktopClient.RaiseWindow("cinnamon", seq);

		internal static bool SendLowerWindow(ulong seq)
			=> DesktopClient.LowerWindow("cinnamon", seq);

		// Ask the shell to place the NEXT window this process creates, before it is first painted. There is no
		// Eval fallback: this needs a live window-created hook in the extension, which an Eval snippet cannot
		// register safely. Fails closed against an extension that predates the method, leaving the caller on
		// the normal correlate-then-move path.
		internal static bool SendReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> DesktopClient.ReserveWindow("cinnamon", cookie, x, y, ttlMs);

		// The compositor window a reservation landed on, or "" if it was never consumed.
		internal static string SendGetReservedWindow(ulong cookie)
			=> DesktopClient.GetReservedWindow("cinnamon", cookie);

		internal static bool SendMoveResize(ulong seq, int x, int y, int width, int height)
			=> DesktopClient.MoveResizeWindow("cinnamon", seq, x, y, width, height);

		// Move/resize by X11 window id (X11 sessions). Tries the extension method, then an org.Cinnamon.Eval
		// fallback that finds the window by get_xwindow() — so this works even against an installed extension
		// that predates MoveResizeWindowByXid. user_op = true is what lets it reach off-screen (see the
		// extension comment). Position sentinel int.MinValue = unchanged; size <= 0 = unchanged.
		internal static bool SendMoveResizeByXid(ulong xid, int x, int y, int width, int height)
			=> DesktopClient.MoveResizeWindowByXid("cinnamon", xid, x, y, width, height);

		internal static bool SendSetWindowState(ulong seq, int state)
			=> DesktopClient.SetWindowState("cinnamon", seq, state);

		internal static bool SendCloseWindow(ulong seq)
			=> DesktopClient.CloseWindow("cinnamon", seq);

		internal static bool SendKillWindow(ulong seq)
			=> DesktopClient.KillWindow("cinnamon", seq);

		internal static bool SendSetAlwaysOnTop(ulong seq, bool above)
			=> DesktopClient.SetWindowAbove("cinnamon", seq, above);

		internal static bool SendSetNoBorder(ulong seq, bool noBorder)
			=> DesktopClient.SetWindowDecorated("cinnamon", seq, !noBorder);

		internal static bool SendSetOpacity(ulong seq, object value)
		{
			var alpha = value is string s && s.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255
				: Math.Clamp((int)value.Al(), 0, 255);

			return DesktopClient.SetWindowOpacity("cinnamon", seq, alpha);
		}

		// Probe the capability-free broker handshake, then let the first read perform the grant check.
		internal static bool SupportsClipboard()
		{
			if (!ExtensionServiceHasOwner())
				return false;

			lock (clipboardSupportSync)
			{
				var now = Environment.TickCount64;

				if (now < clipboardSupportCacheUntil)
					return clipboardSupportCached;

				clipboardSupportCached = DesktopClient.ProbeProvider("cinnamon");
				clipboardSupportCacheUntil = now + (clipboardSupportCached
					? ExtensionPresentCacheMs : ExtensionMissingCacheMs);
				return clipboardSupportCached;
			}
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

			// Clipboard access runs only through the bundled extension (Muffin exposes no data-control
			// protocol, so a background app otherwise can't read/write/monitor the clipboard). Content is raw
			// MIME-type <-> bytes so every format round-trips. Getters return null when the extension is
			// absent/failed (vs an empty array/"" for a legitimately empty clipboard).
			internal static string[] GetClipboardMimetypes()
				=> DesktopClient.GetClipboardMimetypes("cinnamon");

			internal static byte[] GetClipboardContent(string mimetype)
				=> DesktopClient.GetClipboardContent("cinnamon", mimetype);

			internal static bool SetClipboardContent(string mimetype, byte[] bytes)
				=> RunExtensionBool(p => p.SetClipboardContentAsync(mimetype, bytes ?? System.Array.Empty<byte>()));

			internal static string GetClipboardText()
				=> DesktopClient.GetClipboardText("cinnamon");

			internal static bool SetClipboardText(string text)
				=> RunExtensionBool(p => p.SetClipboardTextAsync(text ?? string.Empty));

			internal static IDisposable WatchClipboardChanged(Action<string, string[]> handler, Action<Exception> onError = null)
				=> DesktopClient.WatchClipboardChanges("cinnamon", handler, onError);

		internal static IDisposable WatchWindowEvent(Action<string, string> handler, Action<Exception> onError = null)
			=> DesktopClient.WatchWindowEvents("cinnamon", handler, onError);

		private static bool RunOk(string js)
		{
			var json = EvalJson(js);

			if (json.IsNullOrEmpty())
				return false;

			try
			{
				using var doc = JsonDocument.Parse(json);
				return GetBool(doc.RootElement, "ok");
			}
			catch
			{
				return false;
			}
		}

		// Runs JS via Eval and returns the inner JSON string. Cinnamon returns
		// [success, JSON.stringify(returnValue)]; since our snippets already return a JSON
		// string, the D-Bus payload is that string encoded a second time — decode one layer.
		private static string EvalJson(string js)
		{
			var raw = Eval(js);

			if (raw == null)
				return null;

			try
			{
				return JsonSerializer.Deserialize<string>(raw);
			}
			catch
			{
				// Not double-encoded (older Cinnamon); use as-is.
				return raw;
			}
		}

		private static string Eval(string js)
		{
			try
			{
				if (!cinnamonService.TryUse(p => p.EvalAsync(js), out Task<(bool Item1, string Item2)> task))
					return null;

				if (!task.WaitWithoutInterruption(TimeoutMs))
					return null;

				var (ok, result) = task.GetAwaiter().GetResult();
				return ok ? result : null;
			}
			catch
			{
				return null;
			}
		}

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
			lock (clipboardSupportSync)
			{
				clipboardSupportCached = false;
				clipboardSupportCacheUntil = 0;
			}
		}

		internal static void Reset()
		{
			extension.Dispose();
			cinnamonService.Dispose();
			sessions.Dispose();
			idleMonitorProxy = null;
			idleMonitorSession = null;
			connectionLocalName = registeredHighlightOwnerBusName = "";
			Initialize();
		}

		private static bool GetBool(JsonElement e, string name)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

		private static int GetInt(JsonElement e, string name)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
	}

	/// <summary>
	/// Wayland window-management backend for the Cinnamon desktop (Muffin compositor),
	/// driven through <see cref="CinnamonShellBridge"/>. Provides the introspection that
	/// Wayland denies foreign clients (active window, window list/geometry, cursor) so
	/// WinActive/WinExist/WinGetTitle("A")/WinGetPos work on Cinnamon-Wayland sessions.
	/// </summary>
	internal sealed class CinnamonBackend : IWaylandBackend
	{
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
				onError => CinnamonShellBridge.WatchWindowEvent(OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				CinnamonShellBridge.ExtensionServiceHasOwner,
				CinnamonShellBridge.SubscribeExtensionAvailability);
		}

		public bool TryGetCursorPos(out int x, out int y)
			=> CinnamonShellBridge.QueryCursorPosition(out x, out y);

		public bool TryGetIdleTime(out long milliseconds)
			=> CinnamonShellBridge.QueryIdleTime(out milliseconds);

		public bool TryGetWorkArea(out Rectangle area)
			=> CinnamonShellBridge.QueryWorkArea(out area);

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];
			var json = CinnamonShellBridge.QueryWindowList(includeHidden);

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
			=> TryParseSingleWindow(CinnamonShellBridge.QueryActiveWindow(), out window);

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
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendFocusWindow(seq);

		public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> CinnamonShellBridge.SendReserveWindow(cookie, x, y, ttlMs);

		public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			compositorId = CinnamonShellBridge.SendGetReservedWindow(cookie);
			handle = compositorId.Length > 0 && ulong.TryParse(compositorId, out var seq) ? SeqToHandle(seq) : 0;
			return handle != 0;
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryHandleToSeq(handle, out var seq)
			   && CinnamonShellBridge.SendMoveResize(
				   seq,
				   setPosition ? bounds.X : int.MinValue,
				   setPosition ? bounds.Y : int.MinValue,
				   setSize && bounds.Width > 0 ? bounds.Width : 0,
				   setSize && bounds.Height > 0 ? bounds.Height : 0);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryHandleToSeq(handle, out var seq)
			   && CinnamonShellBridge.SendSetWindowState(seq, WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetAlwaysOnTop(seq, onTop);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetNoBorder(seq, noBorder);

		public bool TrySetTransparency(nint handle, object alpha)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendSetOpacity(seq, alpha);

		public bool SupportsTransparency => true;

		public bool TrySetZOrder(nint handle, ZOrder z)
			=> TryHandleToSeq(handle, out var seq)
			   && (z == ZOrder.Top
				   ? CinnamonShellBridge.SendRaiseWindow(seq)
				   : z == ZOrder.Bottom && CinnamonShellBridge.SendLowerWindow(seq));

		public bool TryCloseWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendCloseWindow(seq);

		public bool TryKillWindow(nint handle)
			=> TryHandleToSeq(handle, out var seq) && CinnamonShellBridge.SendKillWindow(seq);

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> DesktopClient.SendMouseMoveAbsolute("cinnamon", x, y);

		public bool TrySendMouseMoveRelative(int dx, int dy)
			=> DesktopClient.SendMouseMoveRelative("cinnamon", dx, dy);

		public bool TrySendMouseButton(uint button, bool pressed)
			=> DesktopClient.SendMouseButton("cinnamon", button, pressed);

		public bool TrySendMouseScroll(int delta, bool vertical)
			=> DesktopClient.SendMouseScroll("cinnamon", delta, vertical);

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
			public bool SupportsClipboard => CinnamonShellBridge.SupportsClipboard();

			public string[] GetClipboardMimetypes()
				=> CinnamonShellBridge.GetClipboardMimetypes();

			public byte[] GetClipboardContent(string mimetype)
				=> CinnamonShellBridge.GetClipboardContent(mimetype);

			public bool SetClipboardContent(string mimetype, byte[] bytes)
				=> CinnamonShellBridge.SetClipboardContent(mimetype, bytes);

			public string GetClipboardText()
				=> CinnamonShellBridge.GetClipboardText();

			public bool SetClipboardText(string text)
				=> CinnamonShellBridge.SetClipboardText(text);

			public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler, Action<Exception> onError = null)
				=> handler == null ? null : CinnamonShellBridge.WatchClipboardChanged(handler, onError);

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
