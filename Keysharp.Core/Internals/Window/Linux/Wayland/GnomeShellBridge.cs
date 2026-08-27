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
		private static RetryGate clipboardProbes;

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
			clipboardProbes = new RetryGate(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));
			extension.AvailabilityChanged += ExtensionAvailabilityChanged;
		}

		// ---- public query/command surface used by GnomeBackend ----------

		internal static bool QueryCursorPosition(out int x, out int y)
		{
			x = 0;
			y = 0;

			if (!TryRun(p => p.GetCursorPositionAsync(), out (int X, int Y) result))
				return false;

			(x, y) = result;
			return true;
		}

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
		{
			area = Rectangle.Empty;

			if (!TryRun(p => p.GetWorkAreaAsync(), out (int X, int Y, int Width, int Height) result))
				return false;

			if (result.Width <= 0 || result.Height <= 0)
				return false;

			area = new Rectangle(result.X, result.Y, result.Width, result.Height);
			return true;
		}

		internal static string QueryWindowList(bool includeHidden)
			=> Run(p => p.GetWindowListAsync(includeHidden));

		internal static string QueryActiveWindow()
			=> Run(p => p.GetActiveWindowAsync());

		internal static bool SendFocusWindow(ulong handle)
			=> Run(p => p.FocusWindowAsync(handle));

		internal static bool SendRaiseWindow(ulong handle)
			=> Run(p => p.RaiseWindowAsync(handle));

		internal static bool SendLowerWindow(ulong handle)
			=> Run(p => p.LowerWindowAsync(handle));

		// Ask the shell to place the NEXT window this process creates, before it is first painted. Fails closed
		// against an extension that predates the method, leaving the caller on the correlate-then-move path.
		internal static bool SendReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> Run(p => p.ReserveWindowAsync(Environment.ProcessId, cookie, x, y, ttlMs));

		internal static string SendGetReservedWindow(ulong cookie)
			=> Run(p => p.GetReservedWindowAsync(Environment.ProcessId, cookie)) ?? "";

		internal static bool SendMoveResize(ulong handle, int x, int y, int width, int height)
			=> Run(p => p.MoveResizeWindowAsync(handle, x, y, width, height));

		// Move/resize by X11 window id (X11 sessions). GNOME Shell disables Eval, so this relies on the
		// extension method; a window is unreachable (returns false → caller falls back to XMoveWindow) until
		// an extension carrying MoveResizeWindowByXid is installed and the shell reloaded.
		internal static bool SendMoveResizeByXid(ulong xid, int x, int y, int width, int height)
			=> Run(p => p.MoveResizeWindowByXidAsync(xid, x, y, width, height));

		internal static bool SendSetWindowState(ulong handle, int state)
			=> Run(p => p.SetWindowStateAsync(handle, state));

		internal static bool SendSetWindowAbove(ulong handle, bool above)
			=> Run(p => p.SetWindowAboveAsync(handle, above));

		internal static bool SendSetWindowDecorated(ulong handle, bool decorated)
			=> Run(p => p.SetWindowDecoratedAsync(handle, decorated));

		internal static bool SendSetOpacity(ulong handle, object value)
		{
			// "off" clears transparency (fully opaque); otherwise coerce to a 0-255 alpha. Mirrors CinnamonBackend.
			var alpha = value is string s && s.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255
				: Math.Clamp((int)value.Al(), 0, 255);

			return Run(p => p.SetWindowOpacityAsync(handle, alpha));
		}

		internal static bool SendCloseWindow(ulong handle)
			=> Run(p => p.CloseWindowAsync(handle));

		internal static bool SendKillWindow(ulong handle)
			=> Run(p => p.KillWindowAsync(handle));

		internal static OverlayShowResult SendShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
			=> pngBytes is { Length: > 0 }
			   ? RunShow(p => p.ShowImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height, pngBytes),
						 ImageOverlayTimeoutMs)
			   : OverlayShowResult.Failed;

		internal static OverlayShowResult SendShowImageOverlayShm(uint id, int x, int y, int width, int height,
			string shmPath, int pixelWidth, int pixelHeight, int stride)
			=> !shmPath.IsNullOrEmpty()
			   ? RunShow(p => p.ShowImageOverlayShmAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height,
														 shmPath, pixelWidth, pixelHeight, stride),
						 ImageOverlayTimeoutMs)
			   : OverlayShowResult.Failed;

		internal static bool SendMoveImageOverlay(uint id, int x, int y, int width, int height)
			=> Run(p => p.MoveImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height));

		internal static bool SendHideImageOverlay(uint id)
			=> Run(p => p.HideImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName));

		internal static bool SendMouseMoveAbsolute(int x, int y)
			=> Run(p => p.SendMouseMoveAbsoluteAsync(x, y));

		internal static bool SendMouseMoveRelative(int dx, int dy)
			=> Run(p => p.SendMouseMoveRelativeAsync(dx, dy));

		internal static bool SendMouseButton(uint button, bool pressed)
			=> Run(p => p.SendMouseButtonAsync(button, pressed));

		internal static bool SendMouseScroll(int delta, bool vertical)
			=> Run(p => p.SendMouseScrollAsync(delta, vertical));

		internal static byte[] CaptureArea(int x, int y, int w, int h)
		{
			const int captureTimeoutMs = 10_000;

			if (!TryRun(p => p.CaptureAreaAsync(x, y, w, h), out byte[] bytes, captureTimeoutMs))
				return null;

			return bytes is { Length: > 0 } ? bytes : null;
		}

		internal static IDisposable WatchActiveWindowChanged(Action<string> handler)
		{
			return TryRun(p => p.WatchActiveWindowChangedAsync(handler, emitOnCapturedContext: false).AsTask(), out IDisposable subscription)
				? subscription : null;
		}

		internal static IDisposable WatchWindowEvent(Action<string, string> handler, Action<Exception> onError = null)
		{
			return TryRun(p => p.WatchWindowEventAsync(DBusSignals.Adapt<(string, string)>(e => handler(e.Item1, e.Item2), onError),
												   DBusSignals.FlagsFor(onError), emitOnCapturedContext: false).AsTask(), out IDisposable subscription)
				? subscription : null;
		}

		// Whether the extension actually answers clipboard calls. The overlay path can gate on cheap name
		// ownership because it reacts to a per-call tri-state (a definitive failure falls back to Eto), but the
		// the recovering clipboard router uses this as its current liveness signal, so a stale/incompatible extension
		// that owns the D-Bus name yet no longer speaks the clipboard protocol must not be treated as usable. Verify
		// with a cheap, side-effect-free
		// GetClipboardMimetypes round-trip (null = absent/failed, any array = answered) and cache the result.
		internal static bool SupportsClipboard()
		{
			var now = Environment.TickCount64;

			if (!ExtensionServiceHasOwner())
				return false;

			if (now < clipboardSupportCacheUntil)
				return clipboardSupportCached;

			using var attempt = clipboardProbes.TryBegin();

			if (attempt == null)
				return false;

			var ok = Run(p => p.GetClipboardMimetypesAsync()) != null;
			clipboardSupportCached = ok;
			clipboardSupportCacheUntil = now + (ok ? ExtensionPresentCacheMs : ExtensionMissingCacheMs);

			if (ok)
				attempt.Succeed();

			return ok;
		}

		// Clipboard access runs only through the extension (Mutter exposes no data-control protocol, so a
		// background app otherwise can't read/write/monitor the clipboard). Content is raw MIME-type <-> bytes
		// so every format round-trips. Getters return null when the extension is absent/failed (vs an empty
		// array/"" for a legitimately empty clipboard).
		internal static string[] GetClipboardMimetypes()
			=> Run(p => p.GetClipboardMimetypesAsync());

		internal static byte[] GetClipboardContent(string mimetype)
			=> Run(p => p.GetClipboardContentAsync(mimetype));

		internal static bool SetClipboardContent(string mimetype, byte[] bytes)
			=> Run(p => p.SetClipboardContentAsync(mimetype, bytes ?? System.Array.Empty<byte>()));

		internal static string GetClipboardText()
			=> Run(p => p.GetClipboardTextAsync());

		internal static bool SetClipboardText(string text)
			=> Run(p => p.SetClipboardTextAsync(text ?? string.Empty));

		internal static IDisposable WatchClipboardChanged(Action<string, string[]> handler, Action<Exception> onError = null)
		{
			return TryRun(p => p.WatchClipboardChangedAsync(DBusSignals.Adapt<(string, string[])>(e => handler(e.Item1, e.Item2), onError),
													   DBusSignals.FlagsFor(onError), emitOnCapturedContext: false).AsTask(), out IDisposable subscription)
				? subscription : null;
		}

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
			clipboardSupportCached = false;
			clipboardSupportCacheUntil = 0;
			clipboardProbes.Rearm();
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
