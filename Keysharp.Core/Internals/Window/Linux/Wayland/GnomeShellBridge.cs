#if LINUX
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Gnome = Keysharp.Internals.DBus.Generated.Gnome;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	// The GnomeShell1 and IdleMonitor proxies are generated from Internals/Linux/DBus/Interfaces/GnomeShell.xml.

	/// <summary>
	/// Static bridge to the Keysharp GNOME Shell extension D-Bus service.
	/// Lazily connects on first use and caches the session-bus connection
	/// and proxy for subsequent calls. All public methods are thread-safe.
	/// </summary>
	internal static class GnomeShellBridge
	{
		private const string ServiceName  = "io.github.keysharp.GnomeShell";
		private const string ObjectPath   = "/io/github/keysharp/GnomeShell";
		private const string IdleMonitorServiceName = "org.gnome.Mutter.IdleMonitor";
		private const string IdleMonitorObjectPath = "/org/gnome/Mutter/IdleMonitor/Core";
		private const int    TimeoutMs    = 2000;
		private const int    ImageOverlayTimeoutMs = 10_000;
		private const int    ExtensionMissingCacheMs = 5000;
		private const int    ExtensionPresentCacheMs = 1000;

		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<Gnome.GnomeShell1> extension;
		private static RetryGate highlightOwnerRegistration;
		private static Gnome.IdleMonitor idleMonitorProxy;
		private static DbusSession idleMonitorSession;
		private static long clipboardSupportCacheUntil;
		private static bool clipboardSupportCached;
		private static readonly object clipboardSupportSync = new();

		// Overlay-ownership plumbing (mirrors CinnamonBackend): our stable process key, the unique name of
		// our D-Bus connection, and the sticky "already registered under this connection" latch + retry gate.
		private static readonly string HighlightOwnerKey = WaylandOverlayOwner.Key;
		private static string connectionLocalName = "";
		private static string registeredHighlightOwnerBusName = "";

		static GnomeShellBridge()
			=> Initialize();

		private static void Initialize()
		{
			sessions = new RecoverableService<DbusSession>(ConnectSessionBus,
				initialRetryDelay: TimeSpan.FromMilliseconds(500),
				maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension = new WatchedDbusService<Gnome.GnomeShell1>(sessions, ServiceName, new ObjectPath(ObjectPath), TimeoutMs,
				(c, d, p) => new Gnome.GnomeShell1(c, d, p));
			highlightOwnerRegistration = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(500), maximumRetryDelay: TimeSpan.FromSeconds(5));
			extension.AvailabilityChanged += ExtensionAvailabilityChanged;
		}

		// ---- public query/command surface used by GnomeBackend ----------

		internal static bool QueryCursorPosition(out int x, out int y)
			=> DesktopClient.QueryCursorPosition("gnome", out x, out y);

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
					idleMonitorProxy = new Gnome.IdleMonitor(lease.Value.Connection,
						IdleMonitorServiceName, new ObjectPath(IdleMonitorObjectPath));
				}

				var task = idleMonitorProxy.GetIdletimeAsync();

				if (!task.WaitWithoutInterruption(TimeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("GNOME idle monitor", "GetIdletime", $"timed out after {TimeoutMs} ms");
					return false;
				}

				var value = task.GetAwaiter().GetResult();
				milliseconds = value > long.MaxValue ? long.MaxValue : (long)value;
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("GNOME idle monitor", "GetIdletime", WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		internal static bool QueryWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea("gnome", out area);

		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList("gnome", includeHidden);

		internal static string QueryActiveWindow()
			=> DesktopClient.QueryActiveWindow("gnome");

		internal static bool SendFocusWindow(ulong handle)
			=> DesktopClient.FocusWindow("gnome", handle);

		internal static bool SendRaiseWindow(ulong handle)
			=> DesktopClient.RaiseWindow("gnome", handle);

		internal static bool SendLowerWindow(ulong handle)
			=> DesktopClient.LowerWindow("gnome", handle);

		// Ask the shell to place the NEXT window this process creates, before it is first painted. Fails closed
		// against an extension that predates the method, leaving the caller on the correlate-then-move path.
		internal static bool SendReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> DesktopClient.ReserveWindow("gnome", cookie, x, y, ttlMs);

		internal static string SendGetReservedWindow(ulong cookie)
			=> DesktopClient.GetReservedWindow("gnome", cookie);

		internal static bool SendMoveResize(ulong handle, int x, int y, int width, int height)
			=> DesktopClient.MoveResizeWindow("gnome", handle, x, y, width, height);

		// Move/resize by X11 window id (X11 sessions). GNOME Shell disables Eval, so this relies on the
		// extension method; a window is unreachable (returns false → caller falls back to XMoveWindow) until
		// an extension carrying MoveResizeWindowByXid is installed and the shell reloaded.
		internal static bool SendMoveResizeByXid(ulong xid, int x, int y, int width, int height)
			=> DesktopClient.MoveResizeWindowByXid("gnome", xid, x, y, width, height);

		internal static bool SendSetWindowState(ulong handle, int state)
			=> DesktopClient.SetWindowState("gnome", handle, state);

		internal static bool SendSetWindowAbove(ulong handle, bool above)
			=> DesktopClient.SetWindowAbove("gnome", handle, above);

		internal static bool SendSetWindowDecorated(ulong handle, bool decorated)
			=> DesktopClient.SetWindowDecorated("gnome", handle, decorated);

		internal static bool SendSetOpacity(ulong handle, object value)
		{
			// "off" clears transparency (fully opaque); otherwise coerce to a 0-255 alpha. Mirrors CinnamonBackend.
			var alpha = value is string s && s.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255
				: Math.Clamp((int)value.Al(), 0, 255);

			return DesktopClient.SetWindowOpacity("gnome", handle, alpha);
		}

		internal static bool SendCloseWindow(ulong handle)
			=> DesktopClient.CloseWindow("gnome", handle);

		internal static bool SendKillWindow(ulong handle)
			=> DesktopClient.KillWindow("gnome", handle);

		internal static OverlayShowResult SendShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
			=> pngBytes is { Length: > 0 }
			   ? RunShow(p => p.ShowImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height, pngBytes),
						 ImageOverlayTimeoutMs)
			   : OverlayShowResult.Failed;

		internal static bool SendMoveImageOverlay(uint id, int x, int y, int width, int height)
			=> Run(p => p.MoveImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height));

		internal static bool SendHideImageOverlay(uint id)
			=> Run(p => p.HideImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName));

		internal static IDisposable WatchActiveWindowChanged(Action<string> handler)
			=> handler == null ? null : DesktopClient.WatchWindowEvents("gnome",
				(type, json) =>
				{
					if (type == "active-state")
						handler(json);
				});

		internal static IDisposable WatchWindowEvent(Action<string, string> handler, Action<Exception> onError = null)
			=> DesktopClient.WatchWindowEvents("gnome", handler, onError);

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

				clipboardSupportCached = DesktopClient.ProbeProvider("gnome");
				clipboardSupportCacheUntil = now + (clipboardSupportCached
					? ExtensionPresentCacheMs : ExtensionMissingCacheMs);
				return clipboardSupportCached;
			}
		}

		// Clipboard access runs only through the extension (Mutter exposes no data-control protocol, so a
		// background app otherwise can't read/write/monitor the clipboard). Content is raw MIME-type <-> bytes
		// so every format round-trips. Getters return null when the extension is absent/failed (vs an empty
		// array/"" for a legitimately empty clipboard).
		internal static string[] GetClipboardMimetypes()
			=> DesktopClient.GetClipboardMimetypes("gnome");

		internal static byte[] GetClipboardContent(string mimetype)
			=> DesktopClient.GetClipboardContent("gnome", mimetype);

		internal static bool SetClipboardContent(string mimetype, byte[] bytes)
			=> Run(p => p.SetClipboardContentAsync(mimetype, bytes ?? System.Array.Empty<byte>()));

		internal static string GetClipboardText()
			=> DesktopClient.GetClipboardText("gnome");

		internal static bool SetClipboardText(string text)
			=> Run(p => p.SetClipboardTextAsync(text ?? string.Empty));

		internal static IDisposable WatchClipboardChanged(Action<string, string[]> handler, Action<Exception> onError = null)
			=> DesktopClient.WatchClipboardChanges("gnome", handler, onError);

		// ---- connection management ---------------------------------------

		private static T Run<T>(Func<Gnome.GnomeShell1, Task<T>> call, int timeoutMs = TimeoutMs,
			[System.Runtime.CompilerServices.CallerMemberName] string operation = null)
			=> TryRun(call, out T result, timeoutMs, operation) ? result : default;

		private static bool TryRun<T>(Func<Gnome.GnomeShell1, Task<T>> call, out T result, int timeoutMs = TimeoutMs,
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
					WaylandBridgeDiagnostics.Failure("GNOME Shell", operation, "extension service is unavailable");
					return false;
				}

				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("GNOME Shell", operation, $"timed out after {timeoutMs} ms");
					return false;
				}

				result = task.GetAwaiter().GetResult();
				return true;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("GNOME Shell", operation, WaylandBridgeDiagnostics.Describe(ex));
				return false;
			}
		}

		// Show an image overlay, classifying the outcome so the caller can tell an ambiguous timeout (commit to
		// the compositor) from a definitive failure (fall back to Eto). A plain Run collapses both to `false`,
		// which is exactly what caused the duplicated-overlay bug: a slow first upload timed out client-side yet
		// still created the shell actor, and the false return triggered a second, Eto, surface.
		private static OverlayShowResult RunShow(Func<Gnome.GnomeShell1, Task<bool>> call, int timeoutMs)
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
					WaylandBridgeDiagnostics.Failure("GNOME Shell", "ShowImageOverlay", "extension service is unavailable");
					return OverlayShowResult.Failed;
				}

				if (!task.WaitWithoutInterruption(timeoutMs))
				{
					WaylandBridgeDiagnostics.Failure("GNOME Shell", "ShowImageOverlay",
						$"timed out after {timeoutMs} ms; the compositor result is ambiguous");
					return OverlayShowResult.TimedOut;
				}

				if (task.GetAwaiter().GetResult())
					return OverlayShowResult.Shown;

				WaylandBridgeDiagnostics.Failure("GNOME Shell", "ShowImageOverlay", "extension returned false");
				return OverlayShowResult.Failed;
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("GNOME Shell", "ShowImageOverlay", WaylandBridgeDiagnostics.Describe(ex));
				return OverlayShowResult.Failed;
			}
		}

		private static DbusSession ConnectSessionBus()
			=> DbusSession.Connect(DBusBus.Session, TimeoutMs, "GNOME Shell", (session, reason) =>
			{
				if (reason != null)
					WaylandBridgeDiagnostics.Failure("GNOME Shell", "session bus disconnected",
												   WaylandBridgeDiagnostics.Describe(reason));

				sessions.Invalidate(session, reason);
			});

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

		internal static bool ExtensionServiceHasOwner() => extension.HasOwner;

		internal static IDisposable SubscribeExtensionAvailability(Action handler)
		{
			if (handler == null)
				return null;

			extension.AvailabilityChanged += handler;
			return new CallbackDisposable(() => extension.AvailabilityChanged -= handler);
		}

		internal static void Reset()
		{
			extension.Dispose();
			sessions.Dispose();
			idleMonitorProxy = null;
			idleMonitorSession = null;
			connectionLocalName = registeredHighlightOwnerBusName = "";
			Initialize();
		}

		// Announce this process to the shell extension as an overlay owner so it can attribute our overlays
		// and reap them when we die. Guarded so the real D-Bus call fires at most once per connection name,
		// with a short backoff on miss (extension still loading / absent). Mirrors CinnamonBackend.
		private static void RegisterHighlightOwner(Gnome.GnomeShell1 p)
		{
			if (p == null || string.IsNullOrEmpty(connectionLocalName) || registeredHighlightOwnerBusName == connectionLocalName)
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
					WaylandBridgeDiagnostics.Failure("GNOME Shell", "RegisterHighlightOwner", $"timed out after {TimeoutMs} ms");
					return;
				}

				if (task.GetAwaiter().GetResult())
				{
					registeredHighlightOwnerBusName = connectionLocalName;
					attempt.Succeed();
				}
				else
				{
					WaylandBridgeDiagnostics.Failure("GNOME Shell", "RegisterHighlightOwner", "extension returned false");
				}
			}
			catch (Exception ex)
			{
				WaylandBridgeDiagnostics.Failure("GNOME Shell", "RegisterHighlightOwner", WaylandBridgeDiagnostics.Describe(ex));
				attempt.Fail(ex);
			}
		}
	}
}
#endif
