#if LINUX
using Keysharp.Internals.DBus;
using Tmds.DBus.Protocol;
using Gnome = Keysharp.Internals.DBus.Generated.Gnome;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	// The GnomeShell1 proxy is generated from Internals/Linux/DBus/Interfaces/GnomeShell.xml.

	/// <summary>
	/// Static bridge to the Keysharp GNOME Shell extension's image-overlay service.
	/// Lazily connects on first use and caches the session-bus connection
	/// and proxy for subsequent calls. All public methods are thread-safe.
	/// </summary>
	internal static class GnomeShellBridge
	{
		private const string ServiceName  = "io.github.keysharp.GnomeShell";
		private const string ObjectPath   = "/io/github/keysharp/GnomeShell";
		private const int    TimeoutMs    = 2000;
		private const int    ImageOverlayTimeoutMs = 10_000;

		private static RecoverableService<DbusSession> sessions;
		private static WatchedDbusService<Gnome.GnomeShell1> extension;
		private static RetryGate highlightOwnerRegistration;

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

		internal static OverlayShowResult SendShowImageOverlay(uint id, int x, int y, int width, int height, byte[] pngBytes)
			=> pngBytes is { Length: > 0 }
			   ? RunShow(p => p.ShowImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height, pngBytes),
						 ImageOverlayTimeoutMs)
			   : OverlayShowResult.Failed;

		internal static bool SendMoveImageOverlay(uint id, int x, int y, int width, int height)
			=> Run(p => p.MoveImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName, x, y, width, height));

		internal static bool SendHideImageOverlay(uint id)
			=> Run(p => p.HideImageOverlayAsync(id, HighlightOwnerKey, connectionLocalName));

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
