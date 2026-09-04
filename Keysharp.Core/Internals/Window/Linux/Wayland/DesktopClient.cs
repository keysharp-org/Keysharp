#if LINUX
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;
using Eto.Drawing;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Typed client for <c>libkeysharp-desktop.so.0</c>.</summary>
	internal static unsafe class DesktopClient
	{
		private const int RequestTimeoutMs = 30_000;
		private const int AuthorizationTimeoutMs = 125_000;
		private const int ProbeTimeoutMs = 2_000;
		private const int EventPollTimeoutMs = 1_000;
		private const int MaxTextBytes = 4 * 1024 * 1024;
		private const int MaxCaptureBytes = 256 * 1024 * 1024;
		private const int MaxCaptureDimension = 32_768;
		private const int MaxMimetypeBytes = 1024;
		private const int MaxClipboardWriteBytes = 4_193_272;
		private const int MaxMimetypes = 4096;
		private const int MaxWindowHandleBytes = 128;
		private const string SocketEnvironmentVariable = "KEYSHARP_DESKTOP_SOCKET";
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);
		private static readonly uint NativeBytesStructSize = checked((uint)sizeof(NativeBytes));
		private static readonly uint NativeStringStructSize = checked((uint)sizeof(NativeString));
		private static readonly uint NativeStringListStructSize = checked((uint)sizeof(NativeStringList));
		private static readonly uint NativeCaptureStructSize = checked((uint)sizeof(NativeCapture));
		private static readonly uint NativeWindowEventStructSize = checked((uint)sizeof(NativeWindowEvent));
		private static readonly uint NativeClipboardEventStructSize = checked((uint)sizeof(NativeClipboardEvent));
		private static readonly uint NativeServiceInfoStructSize = checked((uint)sizeof(NativeServiceInfo));
		private static readonly uint NativePointStructSize = checked((uint)sizeof(NativePoint));
		private static readonly uint NativeRectangleStructSize = checked((uint)sizeof(NativeRectangle));

		private const LinuxPermissionScope DesktopAuthorizationScopes =
			LinuxPermissionScope.InputControl |
			LinuxPermissionScope.WindowMonitoring |
			LinuxPermissionScope.WindowControl |
			LinuxPermissionScope.ScreenCapture |
			LinuxPermissionScope.AudioCapture |
			LinuxPermissionScope.CameraCapture |
			LinuxPermissionScope.ClipboardMonitoring;

		private enum ConnectionRole : uint
		{
			Rpc = 0,
			EventStream = 1,
			AuthorizationLease = 3,
		}

		private enum AuthorizationMode : uint
		{
			Check = 0,
			Request = 1,
		}

		private enum Backend : uint
		{
			None = 0,
			Kwin = 1,
			Gnome = 2,
			Cinnamon = 3,
			Generic = 4,
			X11 = 5,
		}

		[Flags]
		private enum Operation : ulong
		{
			None = 0,
			CaptureArea = 1UL << 0,
			CaptureWindow = 1UL << 1,
			WindowList = 1UL << 2,
			WindowActive = 1UL << 3,
			WindowWatch = 1UL << 4,
			WindowFocus = 1UL << 5,
			WindowRaise = 1UL << 6,
			WindowLower = 1UL << 7,
			WindowClose = 1UL << 8,
			WindowKill = 1UL << 9,
			WindowMoveResize = 1UL << 10,
			WindowMoveResizeXid = 1UL << 11,
			WindowSetState = 1UL << 12,
			WindowSetOpacity = 1UL << 13,
			WindowSetAbove = 1UL << 14,
			WindowSetDecorated = 1UL << 15,
			WindowReserve = 1UL << 16,
			WindowGetReserved = 1UL << 17,
			ClipboardMimetypes = 1UL << 18,
			ClipboardContent = 1UL << 19,
			ClipboardText = 1UL << 20,
			ClipboardWatch = 1UL << 21,
			MouseMoveAbsolute = 1UL << 22,
			MouseMoveRelative = 1UL << 23,
			MouseButton = 1UL << 24,
			MouseScroll = 1UL << 25,
			CursorPosition = 1UL << 26,
			WorkArea = 1UL << 27,
			ClipboardSetContent = 1UL << 28,
			All = ((1UL << 28) - 1) | ClipboardSetContent,
		}

		private enum CaptureFormat : ushort
		{
			Png = 1,
			Bgra8Premultiplied = 2,
		}

		private static readonly DesktopRpcSession kwinCapture = new(Backend.Kwin,
			LinuxPermissionScope.ScreenCapture, interactive: true);
		private static readonly DesktopRpcSession gnomeCapture = new(Backend.Gnome,
			LinuxPermissionScope.ScreenCapture, interactive: true);
		private static readonly DesktopRpcSession cinnamonCapture = new(Backend.Cinnamon,
			LinuxPermissionScope.ScreenCapture, interactive: true);
		private static readonly DesktopRpcSession gnomeWindowMonitoring = new(Backend.Gnome,
			LinuxPermissionScope.WindowMonitoring);
		private static readonly DesktopRpcSession gnomeWindowControl = new(Backend.Gnome,
			LinuxPermissionScope.WindowControl);
		private static readonly DesktopRpcSession gnomeClipboardMonitoring = new(Backend.Gnome,
			LinuxPermissionScope.ClipboardMonitoring);
		private static readonly DesktopRpcSession gnomeInputControl = new(Backend.Gnome,
			LinuxPermissionScope.InputControl);
		private static readonly DesktopRpcSession cinnamonWindowMonitoring = new(Backend.Cinnamon,
			LinuxPermissionScope.WindowMonitoring);
		private static readonly DesktopRpcSession cinnamonWindowControl = new(Backend.Cinnamon,
			LinuxPermissionScope.WindowControl);
		private static readonly DesktopRpcSession cinnamonClipboardMonitoring = new(Backend.Cinnamon,
			LinuxPermissionScope.ClipboardMonitoring);
		private static readonly DesktopRpcSession cinnamonInputControl = new(Backend.Cinnamon,
			LinuxPermissionScope.InputControl);
		private static readonly DesktopRpcSession gnomeQueries = new(Backend.Gnome,
			LinuxPermissionScope.None);
		private static readonly DesktopRpcSession cinnamonQueries = new(Backend.Cinnamon,
			LinuxPermissionScope.None);
		// The broker serves these from the X server itself on a session with no
		// Wayland compositor, which is every bare X11 desktop as well as XFCE and
		// MATE. The scopes are the same ones the compositor backends use, because
		// the operations are the same.
		private static readonly DesktopRpcSession x11Queries = new(Backend.X11,
			LinuxPermissionScope.None);
		private static readonly DesktopRpcSession x11WindowMonitoring = new(Backend.X11,
			LinuxPermissionScope.WindowMonitoring);
		private static readonly DesktopRpcSession x11Capture = new(Backend.X11,
			LinuxPermissionScope.ScreenCapture, interactive: true);
		private static readonly DesktopRpcSession x11WindowControl = new(Backend.X11,
			LinuxPermissionScope.WindowControl);
		// Reads only. The broker does not advertise clipboard writes on X11: owning a
		// selection means staying alive to serve it, and the worker that answers exits
		// with its one operation, so the content would vanish behind the caller.
		private static readonly DesktopRpcSession x11ClipboardMonitoring = new(Backend.X11,
			LinuxPermissionScope.ClipboardMonitoring);
		// Every Wayland compositor with no extension of its own: sway, Hyprland,
		// COSMIC, niri, river. The broker answers these as an ordinary client on the
		// OUTSIDE of the compositor, so it serves only what the shared protocols
		// expose -- clipboard reads and a window list, and nothing that changes a
		// window, because no Wayland protocol lets one client do that to another.
		private static readonly DesktopRpcSession genericQueries = new(Backend.Generic,
			LinuxPermissionScope.None);
		private static readonly DesktopRpcSession genericWindowMonitoring = new(Backend.Generic,
			LinuxPermissionScope.WindowMonitoring);
		private static readonly DesktopRpcSession genericClipboardMonitoring = new(Backend.Generic,
			LinuxPermissionScope.ClipboardMonitoring);

		internal static Bitmap Capture(int x, int y, int width, int height)
			=> CaptureArea(kwinCapture, x, y, width, height);

		internal static PermissionResult Authorize(string operation, bool prompt = false)
			=> kwinCapture.Authorize(prompt);

		internal static Bitmap CaptureKWinWindow(string uuid, bool includeDecoration)
			=> CaptureWindow(kwinCapture, uuid, includeDecoration);

		internal static Bitmap CaptureGnome(int x, int y, int width, int height)
			=> CaptureArea(gnomeCapture, x, y, width, height);

		internal static Bitmap CaptureGnomeWindow(ulong handle)
			=> CaptureWindow(gnomeCapture, Invariant(handle), includeDecoration: false);

		internal static PermissionResult AuthorizeGnome(string operation, bool prompt = false)
			=> gnomeCapture.Authorize(prompt);

		internal static Bitmap CaptureCinnamon(int x, int y, int width, int height)
			=> CaptureArea(cinnamonCapture, x, y, width, height);

		internal static Bitmap CaptureCinnamonWindow(ulong handle)
			=> CaptureWindow(cinnamonCapture, Invariant(handle), includeDecoration: false);

		internal static Bitmap CaptureX11(int x, int y, int width, int height)
			=> CaptureArea(x11Capture, x, y, width, height);

		// The handle is an XID here, which the broker checks fits in 32 bits rather than
		// truncating a wider one onto whatever window wears the low half.
		internal static Bitmap CaptureX11Window(ulong handle, bool includeDecoration)
			=> CaptureWindow(x11Capture, Invariant(handle), includeDecoration);

		internal static PermissionResult AuthorizeX11(string operation, bool prompt = false)
			=> x11Capture.Authorize(prompt);

		internal static PermissionResult AuthorizeCinnamon(string operation, bool prompt = false)
			=> cinnamonCapture.Authorize(prompt);

		internal static bool ProbeProvider(string backend)
		{
			try
			{
				using var connection = DesktopConnection.Connect(BackendFromName(backend),
					ConnectionRole.Rpc, ProbeTimeoutMs);
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static bool QueryCursorPosition(string backend, out int x, out int y)
		{
			var point = default(Point);
			var result = QuerySession(backend).TryUse(Operation.CursorPosition,
				connection => connection.CursorPosition(out point));
			x = point.X;
			y = point.Y;
			return result;
		}

		internal static bool QueryWorkArea(string backend, out Rectangle area)
		{
			area = Rectangle.Empty;
			var value = Rectangle.Empty;
			var result = QuerySession(backend).TryUse(Operation.WorkArea,
				connection => connection.WorkArea(out value));

			if (result)
				area = value;

			return result;
		}

		internal static string QueryWindowList(string backend, bool includeHidden)
		{
			string value = null;
			return WindowMonitoringSession(backend).TryUse(Operation.WindowList,
				connection => connection.WindowList(includeHidden, out value)) ? value : null;
		}

		internal static string QueryActiveWindow(string backend)
		{
			string value = null;
			return WindowMonitoringSession(backend).TryUse(Operation.WindowActive,
				connection => connection.ActiveWindow(out value)) ? value : null;
		}

		internal static bool FocusWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowFocus,
				connection => connection.WindowAction(Operation.WindowFocus, handle));

		internal static bool RaiseWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowRaise,
				connection => connection.WindowAction(Operation.WindowRaise, handle));

		internal static bool LowerWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowLower,
				connection => connection.WindowAction(Operation.WindowLower, handle));

		internal static bool CloseWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowClose,
				connection => connection.WindowAction(Operation.WindowClose, handle));

		internal static bool KillWindow(string backend, ulong handle)
			=> WindowControlSession(backend).TryUse(Operation.WindowKill,
				connection => connection.WindowAction(Operation.WindowKill, handle));

		internal static bool MoveResizeWindow(string backend, ulong handle,
			int x, int y, int width, int height)
			=> width >= 0 && height >= 0
				&& (x != int.MinValue || y != int.MinValue || width != 0 || height != 0)
				&& WindowControlSession(backend).TryUse(Operation.WindowMoveResize,
					connection => connection.MoveResize(false, handle, x, y,
						checked((uint)width), checked((uint)height)));

		internal static bool MoveResizeWindowByXid(string backend, ulong handle,
			int x, int y, int width, int height)
			=> width >= 0 && height >= 0
				&& (x != int.MinValue || y != int.MinValue || width != 0 || height != 0)
				&& WindowControlSession(backend).TryUse(Operation.WindowMoveResizeXid,
					connection => connection.MoveResize(true, handle, x, y,
						checked((uint)width), checked((uint)height)));

		internal static bool SetWindowState(string backend, ulong handle, int state)
			=> state is >= 0 and <= 2
				&& WindowControlSession(backend).TryUse(Operation.WindowSetState,
					connection => connection.SetWindowValue(Operation.WindowSetState,
						handle, (uint)state));

		internal static bool SetWindowOpacity(string backend, ulong handle, int opacity)
			=> opacity is >= 0 and <= 255
				&& WindowControlSession(backend).TryUse(Operation.WindowSetOpacity,
					connection => connection.SetWindowValue(Operation.WindowSetOpacity,
						handle, (uint)opacity));

		internal static bool SetWindowAbove(string backend, ulong handle, bool above)
			=> WindowControlSession(backend).TryUse(Operation.WindowSetAbove,
				connection => connection.SetWindowValue(Operation.WindowSetAbove,
					handle, above ? 1u : 0u));

		internal static bool SetWindowDecorated(string backend, ulong handle, bool decorated)
			=> WindowControlSession(backend).TryUse(Operation.WindowSetDecorated,
				connection => connection.SetWindowValue(Operation.WindowSetDecorated,
					handle, decorated ? 1u : 0u));

		internal static bool ReserveWindow(string backend, ulong cookie, int x, int y, int ttlMs)
			=> cookie != 0 && ttlMs > 0
				&& WindowControlSession(backend).TryUse(Operation.WindowReserve,
					connection => connection.ReserveWindow(cookie, x, y, checked((uint)ttlMs)));

		internal static string GetReservedWindow(string backend, ulong cookie)
		{
			ulong handle = 0;
			return cookie != 0 && WindowControlSession(backend).TryUse(
				Operation.WindowGetReserved,
				connection => connection.GetReservedWindow(cookie, out handle))
				? Invariant(handle) : string.Empty;
		}

		internal static string[] GetClipboardMimetypes(string backend)
		{
			string[] value = null;
			return ClipboardMonitoringSession(backend).TryUse(Operation.ClipboardMimetypes,
				connection => connection.ClipboardMimetypes(out value)) ? value : null;
		}

		internal static byte[] GetClipboardContent(string backend, string mimetype)
		{
			if (string.IsNullOrEmpty(mimetype)
				|| StrictUtf8.GetByteCount(mimetype) > MaxMimetypeBytes)
				return null;

			byte[] value = null;
			return ClipboardMonitoringSession(backend).TryUse(Operation.ClipboardContent,
				connection => connection.ClipboardContent(mimetype, out value)) ? value : null;
		}

		internal static string GetClipboardText(string backend)
		{
			string value = null;
			return ClipboardMonitoringSession(backend).TryUse(Operation.ClipboardText,
				connection => connection.ClipboardText(out value)) ? value : null;
		}

		internal static bool SetClipboardContent(string backend, string mimetype, byte[] bytes)
		{
			var data = bytes ?? System.Array.Empty<byte>();

			if (string.IsNullOrEmpty(mimetype)
				|| StrictUtf8.GetByteCount(mimetype) > MaxMimetypeBytes
				|| data.Length > MaxClipboardWriteBytes)
				return false;

			return QuerySession(backend).TryUse(Operation.ClipboardSetContent,
				connection => connection.SetClipboardContent(mimetype, data));
		}

		internal static bool SetClipboardText(string backend, string text)
		{
			var value = text ?? string.Empty;
			int length;

			try { length = StrictUtf8.GetByteCount(value); }
			catch (EncoderFallbackException) { return false; }

			if (length > MaxClipboardWriteBytes)
				return false;

			return QuerySession(backend).TryUse(Operation.ClipboardSetContent,
				connection => connection.SetClipboardText(value));
		}

		internal static bool SendMouseMoveAbsolute(string backend, int x, int y)
			=> InputControlSession(backend).TryUse(Operation.MouseMoveAbsolute,
				connection => connection.MouseCoordinates(true, x, y));

		internal static bool SendMouseMoveRelative(string backend, int dx, int dy)
			=> InputControlSession(backend).TryUse(Operation.MouseMoveRelative,
				connection => connection.MouseCoordinates(false, dx, dy));

		internal static bool SendMouseButton(string backend, uint button, bool pressed)
			=> InputControlSession(backend).TryUse(Operation.MouseButton,
				connection => connection.MouseButton(button, pressed));

		internal static bool SendMouseScroll(string backend, int delta, bool vertical)
			=> InputControlSession(backend).TryUse(Operation.MouseScroll,
				connection => connection.MouseScroll(delta, vertical));

		internal static IDisposable WatchWindowEvents(string backend,
			Action<string, string> handler, Action<Exception> onError = null)
			=> handler == null ? null : DesktopSubscription.StartWindow(
				BackendFromName(backend), handler, onError);

		internal static IDisposable WatchClipboardChanges(string backend,
			Action<string, string[]> handler, Action<Exception> onError = null)
			=> handler == null ? null : DesktopSubscription.StartClipboard(
				BackendFromName(backend), handler, onError);

		private static readonly object authorizationSync = new();
		private static readonly object promptSync = new();
		private static readonly List<AuthorizationLease> authorizationLeases = [];
		private static LinuxPermissionScope declinedScopes;
		private static bool authorizationExitHookInstalled;

		internal static PermissionResult AuthorizeCapture(bool recheck = false)
			=> RequestAuthorization(LinuxPermissionScope.ScreenCapture,
				prompt: true, forcePrompt: recheck);

		internal static PermissionResult PeekCaptureConsent()
			=> RequestAuthorization(LinuxPermissionScope.ScreenCapture, prompt: false);

		internal static PermissionResult RequestAuthorization(
			LinuxPermissionScope requestedScopes, bool prompt, bool forcePrompt = false)
		{
			if (requestedScopes == LinuxPermissionScope.None
				|| (requestedScopes & ~DesktopAuthorizationScopes) != 0)
				return new PermissionResult(PermissionStatus.Unsupported,
					"Invalid keysharp-desktop permission scope.");

			if (TrySettleAuthorization(requestedScopes, prompt, forcePrompt, out var settled))
				return settled;

			// Prompts are serialised against each other, because two polkit dialogs racing for
			// the same user is worse than making the second caller wait. This is deliberately not
			// authorizationSync: that lock guards the lease list, and holding it across a dialog
			// that can sit for AuthorizationTimeoutMs waiting for a human would stall every
			// unrelated consent check in the process, including ones already granted.
			lock (promptSync)
			{
				// Another thread may have obtained the scope, or been declined for it, while this
				// one waited for the prompt gate.
				if (TrySettleAuthorization(requestedScopes, prompt, forcePrompt, out settled))
					return settled;

				return AcquireAuthorization(requestedScopes, prompt);
			}
		}

		// Answers from the lease list alone. Returns false when only a prompt can settle it.
		private static bool TrySettleAuthorization(LinuxPermissionScope requestedScopes,
			bool prompt, bool forcePrompt, out PermissionResult result)
		{
			lock (authorizationSync)
			{
				PruneAuthorizationLeasesLocked();
				var missing = requestedScopes & ~GrantedScopesLocked();

				if (missing == LinuxPermissionScope.None)
				{
					result = new PermissionResult(PermissionStatus.Granted);
					return true;
				}

				if (forcePrompt)
				{
					declinedScopes &= ~missing;
				}
				else if (prompt && (declinedScopes & missing) != 0)
				{
					result = new PermissionResult(PermissionStatus.Denied,
						"Desktop permission denied.");
					return true;
				}
			}

			result = default;
			return false;
		}

		private static PermissionResult AcquireAuthorization(
			LinuxPermissionScope requestedScopes, bool prompt)
		{
			LinuxPermissionScope missing;

			lock (authorizationSync)
				missing = requestedScopes & ~GrantedScopesLocked();

			if (missing == LinuxPermissionScope.None)
				return new PermissionResult(PermissionStatus.Granted);

			DesktopConnection connection = null;

			try
			{
				connection = DesktopConnection.Connect(null,
					ConnectionRole.AuthorizationLease, AuthorizationTimeoutMs);
				var result = connection.Authorize(missing, prompt
					? AuthorizationMode.Request : AuthorizationMode.Check,
					out var granted);

				if (!result.IsSuccess)
				{
					connection.Dispose();

					if (prompt && result.Status == NativeClientStatus.Denied)
					{
						lock (authorizationSync)
							declinedScopes |= missing;
					}

					return PermissionFailure(result);
				}

				var lease = new AuthorizationLease(connection, granted);
				connection = null;

				lock (authorizationSync)
				{
					authorizationLeases.Add(lease);
					declinedScopes &= ~granted;
					InstallAuthorizationExitHookLocked();
					lease.Start();
				}

				return new PermissionResult(PermissionStatus.Granted);
			}
			catch (Exception exception)
			{
				connection?.Dispose();
				DebugLine($"keysharp-desktop authorization failed: {exception.Message}");
				return new PermissionResult(PermissionStatus.Unsupported,
					$"keysharp-desktop is unavailable. {exception.Message}");
			}
		}

		internal static bool EnsureCaptureConsent()
			=> (Script.IsHeadless ? PeekCaptureConsent() : AuthorizeCapture()).IsGranted;

		private static LinuxPermissionScope GrantedScopesLocked()
		{
			var granted = LinuxPermissionScope.None;

			foreach (var lease in authorizationLeases)
				granted |= lease.Scopes;

			return granted;
		}

		private static void PruneAuthorizationLeasesLocked()
		{
			for (var index = authorizationLeases.Count - 1; index >= 0; index--)
			{
				if (authorizationLeases[index].IsAlive)
					continue;

				authorizationLeases[index].Dispose();
				authorizationLeases.RemoveAt(index);
			}
		}

		private static void InstallAuthorizationExitHookLocked()
		{
			if (authorizationExitHookInstalled)
				return;

			authorizationExitHookInstalled = true;
			AppDomain.CurrentDomain.ProcessExit += (_, _) =>
			{
				lock (authorizationSync)
				{
					foreach (var lease in authorizationLeases)
						lease.Dispose();

					authorizationLeases.Clear();
				}
			};
		}

		private static PermissionResult PermissionFailure(in CallResult result)
		{
			var status = result.Status is NativeClientStatus.Denied
				or NativeClientStatus.Cancelled or NativeClientStatus.Revoked
				? PermissionStatus.Denied : PermissionStatus.Unsupported;
			return new PermissionResult(status, result.Message);
		}

		private static Bitmap CaptureArea(DesktopRpcSession session,
			int x, int y, int width, int height)
		{
			if (width <= 0 || height <= 0)
				return null;

			Bitmap bitmap = null;
			return session.TryUse(Operation.CaptureArea,
				connection => connection.CaptureArea(x, y, checked((uint)width),
					checked((uint)height), out bitmap)) ? bitmap : null;
		}

		private static Bitmap CaptureWindow(DesktopRpcSession session,
			string handle, bool includeDecoration)
		{
			if (string.IsNullOrWhiteSpace(handle)
				|| StrictUtf8.GetByteCount(handle) > MaxWindowHandleBytes)
				return null;

			Bitmap bitmap = null;
			return session.TryUse(Operation.CaptureWindow,
				connection => connection.CaptureWindow(handle, includeDecoration,
					out bitmap)) ? bitmap : null;
		}

		private static DesktopRpcSession WindowMonitoringSession(string backend)
			=> BackendFromName(backend) switch
			{
				Backend.Gnome => gnomeWindowMonitoring,
				Backend.Cinnamon => cinnamonWindowMonitoring,
				Backend.X11 => x11WindowMonitoring,
				Backend.Generic => genericWindowMonitoring,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static DesktopRpcSession WindowControlSession(string backend)
			=> BackendFromName(backend) switch
			{
				Backend.Gnome => gnomeWindowControl,
				Backend.Cinnamon => cinnamonWindowControl,
				Backend.X11 => x11WindowControl,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static DesktopRpcSession ClipboardMonitoringSession(string backend)
			=> BackendFromName(backend) switch
			{
				Backend.Gnome => gnomeClipboardMonitoring,
				Backend.Cinnamon => cinnamonClipboardMonitoring,
				Backend.X11 => x11ClipboardMonitoring,
				Backend.Generic => genericClipboardMonitoring,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static DesktopRpcSession InputControlSession(string backend)
			=> BackendFromName(backend) switch
			{
				Backend.Gnome => gnomeInputControl,
				Backend.Cinnamon => cinnamonInputControl,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static DesktopRpcSession QuerySession(string backend)
			=> BackendFromName(backend) switch
			{
				Backend.Gnome => gnomeQueries,
				Backend.Cinnamon => cinnamonQueries,
				Backend.X11 => x11Queries,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static Backend BackendFromName(string backend)
			=> backend switch
			{
				"kwin" => Backend.Kwin,
				"gnome" => Backend.Gnome,
				"cinnamon" => Backend.Cinnamon,
				"x11" => Backend.X11,
				"generic" => Backend.Generic,
				_ => throw new ArgumentOutOfRangeException(nameof(backend)),
			};

		private static string Invariant(ulong value)
			=> value.ToString(CultureInfo.InvariantCulture);

		private static void DebugLine(string message)
			=> Diagnostics.Debug.WriteLine(message);

		private readonly record struct CallResult(
			NativeClientStatus Status,
			uint Detail,
			int SystemError,
			string Diagnostic,
			string Operation)
		{
			internal bool IsSuccess => Status == NativeClientStatus.Ok;
			internal bool ShouldReconnect => Status == NativeClientStatus.Unavailable;
			internal string Message => new NativeClientException("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic).Message;
			internal Exception Exception => new NativeClientException("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic);
		}

		private sealed class DesktopRpcSession
		{
			private readonly Backend backend;
			private readonly LinuxPermissionScope requiredScope;
			private readonly bool interactive;
			private readonly object sync = new();
			private DesktopConnection connection;
			private bool promptDeclined;
			private bool exitHookInstalled;

			internal DesktopRpcSession(Backend backend,
				LinuxPermissionScope requiredScope, bool interactive = false)
			{
				this.backend = backend;
				this.requiredScope = requiredScope;
				this.interactive = interactive;
			}

			internal PermissionResult Authorize(bool prompt)
			{
				lock (sync)
				{
					if (prompt)
						promptDeclined = false;

					if (ConnectionUsableLocked())
						return new PermissionResult(PermissionStatus.Granted);

					ResetLocked();
					return StartLocked(prompt);
				}
			}

			internal bool TryUse(Operation operation,
				Func<DesktopConnection, CallResult> request)
			{
				lock (sync)
				{
					for (var attempt = 0; attempt < 2; attempt++)
					{
						if (!ConnectionUsableLocked())
						{
							ResetLocked();
							var allowPrompt = interactive && !Script.IsHeadless && !promptDeclined;
							var permission = StartLocked(allowPrompt);

							if (!permission.IsGranted)
							{
								DebugLine($"keysharp-desktop {backend} permission failed: {permission.Message}");
								return false;
							}
						}

						if ((connection.AvailableOperations & operation) == 0)
							return false;

						try
						{
							var result = request(connection);

							if (result.IsSuccess)
								return true;

							DebugLine($"keysharp-desktop {backend} operation failed: {result.Message}");

							if (result.Status == NativeClientStatus.Revoked)
								ResetLocked();
							else if (result.ShouldReconnect)
							{
								ResetLocked();
								continue;
							}

							return false;
						}
						catch (Exception exception)
						{
							DebugLine($"keysharp-desktop {backend} operation failed: {exception.Message}");
							ResetLocked();
							return false;
						}
					}

					return false;
				}
			}

			private PermissionResult StartLocked(bool prompt)
			{
				try
				{
					connection = DesktopConnection.Connect(backend,
						ConnectionRole.Rpc, prompt ? AuthorizationTimeoutMs : RequestTimeoutMs);

					if (requiredScope != LinuxPermissionScope.None)
					{
						var result = connection.Authorize(requiredScope, prompt
							? AuthorizationMode.Request : AuthorizationMode.Check,
							out _);

						if (!result.IsSuccess)
						{
							if (prompt && result.Status == NativeClientStatus.Denied)
								promptDeclined = true;

							var permission = PermissionFailure(result);
							ResetLocked();
							return permission;
						}
					}

					InstallExitHookLocked();
					return new PermissionResult(PermissionStatus.Granted);
				}
				catch (Exception exception)
				{
					ResetLocked();
					return new PermissionResult(PermissionStatus.Unsupported,
						exception.Message);
				}
			}

			private bool ConnectionUsableLocked()
			{
				try
				{
					return connection?.IsOpen == true
						&& (requiredScope == LinuxPermissionScope.None
							|| (connection.GrantedScopes & requiredScope) == requiredScope);
				}
				catch
				{
					return false;
				}
			}

			private void InstallExitHookLocked()
			{
				if (exitHookInstalled)
					return;

				exitHookInstalled = true;
				AppDomain.CurrentDomain.ProcessExit += (_, _) =>
				{
					lock (sync)
						ResetLocked();
				};
			}

			private void ResetLocked()
			{
				connection?.Dispose();
				connection = null;
			}
		}

		private sealed class AuthorizationLease : IDisposable
		{
			private readonly DesktopConnection connection;
			private Task reader;
			private uint scopes;
			private int disposed;
			private int readerThreadId;

			internal AuthorizationLease(DesktopConnection connection,
				LinuxPermissionScope scopes)
			{
				this.connection = connection;
				this.scopes = (uint)scopes;
			}

			internal LinuxPermissionScope Scopes
				=> (LinuxPermissionScope)Volatile.Read(ref scopes);

			internal bool IsAlive
				=> Volatile.Read(ref disposed) == 0
					&& Scopes != LinuxPermissionScope.None
					&& reader?.IsCompleted != true;

			internal void Start()
				=> reader = Task.Run(ReadEvents);

			private void ReadEvents()
			{
				Volatile.Write(ref readerThreadId, Environment.CurrentManagedThreadId);

				try
				{
					while (Volatile.Read(ref disposed) == 0)
					{
						var result = connection.LeaseNext(EventPollTimeoutMs, out var revoked);

						if (result.Status == NativeClientStatus.Timeout)
						{
							if (Volatile.Read(ref disposed) == 0)
							{
								var ping = connection.Ping();

								if (!ping.IsSuccess)
									throw ping.Exception;
							}

							continue;
						}

						if (!result.IsSuccess && result.Status != NativeClientStatus.Revoked)
							throw result.Exception;

						if (revoked != LinuxPermissionScope.None)
							Volatile.Write(ref scopes,
								Volatile.Read(ref scopes) & ~(uint)revoked);

						if (Scopes == LinuxPermissionScope.None)
							break;
					}
				}
				catch (Exception exception) when (Volatile.Read(ref disposed) == 0)
				{
					DebugLine($"keysharp-desktop authorization lease ended: {exception.Message}");
				}
				finally
				{
					Interlocked.Exchange(ref disposed, 1);
					Volatile.Write(ref scopes, 0);
					connection.Dispose();
				}
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				Volatile.Write(ref scopes, 0);

				if (reader == null)
				{
					connection.Dispose();
					return;
				}

				if (Volatile.Read(ref readerThreadId) != Environment.CurrentManagedThreadId)
					try { reader.Wait(EventPollTimeoutMs * 2); } catch { }
			}
		}

		private sealed class DesktopSubscription : IDisposable
		{
			private readonly DesktopConnection connection;
			private readonly Action<string, string> windowHandler;
			private readonly Action<string, string[]> clipboardHandler;
			private readonly Action<Exception> onError;
			private Task reader;
			private int disposed;
			private int readerThreadId;

			private DesktopSubscription(DesktopConnection connection,
				Action<string, string> windowHandler,
				Action<string, string[]> clipboardHandler,
				Action<Exception> onError)
			{
				this.connection = connection;
				this.windowHandler = windowHandler;
				this.clipboardHandler = clipboardHandler;
				this.onError = onError;
			}

			internal static IDisposable StartWindow(Backend backend,
				Action<string, string> handler, Action<Exception> onError)
				=> Start(backend, LinuxPermissionScope.WindowMonitoring,
					Operation.WindowWatch, handler, null, onError);

			internal static IDisposable StartClipboard(Backend backend,
				Action<string, string[]> handler, Action<Exception> onError)
				=> Start(backend, LinuxPermissionScope.ClipboardMonitoring,
					Operation.ClipboardWatch, null, handler, onError);

			private static IDisposable Start(Backend backend,
				LinuxPermissionScope scope, Operation operation,
				Action<string, string> windowHandler,
				Action<string, string[]> clipboardHandler,
				Action<Exception> onError)
			{
				DesktopConnection connection = null;

				try
				{
					connection = DesktopConnection.Connect(backend,
						ConnectionRole.EventStream, RequestTimeoutMs);
					var authorization = connection.Authorize(scope,
						AuthorizationMode.Check, out _);

					if (!authorization.IsSuccess)
						throw authorization.Exception;

					if ((connection.AvailableOperations & operation) == 0)
						throw new NotSupportedException(
							"The desktop event operation is unavailable.");

					var subscribe = operation == Operation.WindowWatch
						? connection.SubscribeWindowWatch()
						: connection.SubscribeClipboardWatch();

					if (!subscribe.IsSuccess)
						throw subscribe.Exception;

					var subscription = new DesktopSubscription(connection,
						windowHandler, clipboardHandler, onError);
					connection = null;
					subscription.reader = Task.Run(subscription.ReadEvents);
					return subscription;
				}
				catch (Exception exception)
				{
					connection?.Dispose();
					DebugLine($"keysharp-desktop event setup failed: {exception.Message}");
					try { onError?.Invoke(exception); } catch { }
					return null;
				}
			}

			private void ReadEvents()
			{
				Volatile.Write(ref readerThreadId, Environment.CurrentManagedThreadId);

				try
				{
					while (Volatile.Read(ref disposed) == 0)
					{
						CallResult result;

						if (windowHandler != null)
						{
							result = connection.NextWindowEvent(EventPollTimeoutMs,
								out var kind, out var json);

							if (result.IsSuccess)
							{
								windowHandler(WindowEventName(kind), json);
								continue;
							}
						}
						else
						{
							result = connection.NextClipboardEvent(EventPollTimeoutMs,
								out var text, out var mimetypes);

							if (result.IsSuccess)
							{
								clipboardHandler(text, mimetypes);
								continue;
							}
						}

						if (result.Status == NativeClientStatus.Timeout)
							continue;

						if (result.Status == NativeClientStatus.Revoked)
							break;

						throw result.Exception;
					}
				}
				catch (Exception exception) when (Volatile.Read(ref disposed) == 0)
				{
					DebugLine($"keysharp-desktop event stream failed: {exception.Message}");
					try { onError?.Invoke(exception); } catch { }
				}
				finally
				{
					Interlocked.Exchange(ref disposed, 1);
					connection.Dispose();
				}
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				if (Volatile.Read(ref readerThreadId) != Environment.CurrentManagedThreadId)
					try { reader?.Wait(EventPollTimeoutMs * 2); } catch { }
			}
		}

		private sealed class DesktopConnection : IDisposable
		{
			private readonly object gate = new();
			private IntPtr handle;

			private DesktopConnection(IntPtr handle, Backend backend,
				Operation availableOperations)
			{
				this.handle = handle;
				Backend = backend;
				AvailableOperations = availableOperations;
			}

			internal Backend Backend { get; }
			internal Operation AvailableOperations { get; }

			internal bool IsOpen
			{
				get { lock (gate) return handle != IntPtr.Zero; }
			}

			internal LinuxPermissionScope GrantedScopes
			{
				get
				{
					lock (gate)
					{
						ThrowIfClosed();
						var scopes = (LinuxPermissionScope)Native.ksd_connection_granted_scopes(handle);

						if ((scopes & ~LinuxPermissionScope.All) != 0)
							throw new InvalidDataException(
								"libkeysharp-desktop returned invalid permission scopes.");

						return scopes;
					}
				}
			}

			internal static DesktopConnection Connect(Backend? expectedBackend,
				ConnectionRole role, int timeoutMs)
			{
				Native.ksd_connect_options_init(out var options);
				Native.ksd_service_info_init(out var info);
				Native.ksd_error_init(out var error);
				var socket = Environment.GetEnvironmentVariable(SocketEnvironmentVariable);
				var socketPointer = IntPtr.Zero;
				IntPtr nativeHandle = IntPtr.Zero;

				try
				{
					if (!string.IsNullOrWhiteSpace(socket))
					{
						socketPointer = Marshal.StringToCoTaskMemUTF8(socket);
						options.SocketPath = socketPointer;
					}

					options.Role = (uint)role;
					options.AuthorizationMode = (uint)AuthorizationMode.Check;
					options.TimeoutMs = checked((uint)timeoutMs);
					var status = NormalizeStatus(Native.ksd_connect(ref options,
						out nativeHandle, ref info, ref error));
					var result = Result(status, in error, "connect");

					if (!result.IsSuccess)
						throw result.Exception;

					var backend = (Backend)info.Backend;
					var operations = (Operation)info.AvailableOperations;
					var granted = (LinuxPermissionScope)info.GrantedScopes;

					// A newer service may report a backend value or operation bits this build does not
					// name. Both are delivered verbatim and never inferred as supported; support is read
					// from AvailableOperations, which the service computes. Framing stays strict.
					if (info.StructSize != NativeServiceInfoStructSize
						|| info.ClientAbiMajor != 0 || info.ClientAbiMinor < 1
						|| (granted & ~LinuxPermissionScope.All) != 0)
						throw new InvalidDataException(
							"libkeysharp-desktop returned incompatible service information.");

					if (expectedBackend.HasValue && backend != expectedBackend.Value)
						throw new InvalidOperationException(
							$"keysharp-desktop selected {backend}, not {expectedBackend.Value}.");

					var connection = new DesktopConnection(nativeHandle, backend, operations);
					nativeHandle = IntPtr.Zero;
					return connection;
				}
				finally
				{
					if (nativeHandle != IntPtr.Zero)
						Native.ksd_disconnect(nativeHandle);

					if (socketPointer != IntPtr.Zero)
						Marshal.FreeCoTaskMem(socketPointer);
				}
			}

			internal CallResult Authorize(LinuxPermissionScope scopes,
				AuthorizationMode mode, out LinuxPermissionScope granted)
			{
				uint nativeGranted = 0;
				var result = Invoke("authorize", (IntPtr connection, ref NativeError error)
					=> Native.ksd_authorize(connection, (uint)mode, (uint)scopes,
						out nativeGranted, ref error));
				granted = (LinuxPermissionScope)nativeGranted;

				if (result.IsSuccess && ((granted & ~LinuxPermissionScope.All) != 0
					|| (granted & scopes) != scopes))
					throw new InvalidDataException(
						"libkeysharp-desktop returned invalid authorization scopes.");

				return result;
			}

			internal CallResult Ping()
				=> Invoke("ping", (IntPtr connection, ref NativeError error)
					=> Native.ksd_ping(connection, ref error));

			internal CallResult LeaseNext(int timeoutMs,
				out LinuxPermissionScope revoked)
			{
				uint nativeRevoked = 0;
				var result = Invoke("wait for authorization revocation",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_lease_next(connection, checked((uint)timeoutMs),
							out nativeRevoked, ref error));
				revoked = (LinuxPermissionScope)nativeRevoked;

				if ((revoked & ~LinuxPermissionScope.All) != 0)
					throw new InvalidDataException(
						"libkeysharp-desktop returned invalid revoked scopes.");

				return result;
			}

			internal CallResult CaptureArea(int x, int y, uint width, uint height,
				out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture area",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_area(connection, x, y, width, height,
								ref capture, ref error));

					if (result.IsSuccess)
						bitmap = ReadCapture(in capture);

					return result;
				}
				finally
				{
					Native.ksd_capture_clear(ref capture);
				}
			}

			internal CallResult CaptureWindow(string windowId, bool includeDecoration,
				out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_window(connection, windowId,
								includeDecoration ? 1u : 0u, ref capture, ref error));

					if (result.IsSuccess)
						bitmap = ReadCapture(in capture);

					return result;
				}
				finally
				{
					Native.ksd_capture_clear(ref capture);
				}
			}

			internal CallResult WindowList(bool includeHidden, out string value)
				=> ReadString("list windows",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_list_json(connection,
							includeHidden ? 1u : 0u, ref result, ref error), out value);

			internal CallResult ActiveWindow(out string value)
				=> ReadString("query active window",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_active_json(connection, ref result, ref error),
					out value);

			internal CallResult CursorPosition(out Point point)
			{
				Native.ksd_point_init(out var value);
				var result = Invoke("query cursor position",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_cursor_position(connection, ref value, ref error));

				if (result.IsSuccess && value.StructSize != NativePointStructSize)
					throw new InvalidDataException(
						"libkeysharp-desktop returned an incompatible point.");

				point = result.IsSuccess ? new Point(value.X, value.Y) : default;
				return result;
			}

			internal CallResult WorkArea(out Rectangle rectangle)
			{
				Native.ksd_rectangle_init(out var value);
				var result = Invoke("query work area",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_work_area(connection, ref value, ref error));

				if (result.IsSuccess && (value.StructSize != NativeRectangleStructSize
					|| value.Width == 0 || value.Height == 0
					|| value.Width > int.MaxValue || value.Height > int.MaxValue))
					throw new InvalidDataException(
						"libkeysharp-desktop returned an invalid work area.");

				rectangle = result.IsSuccess
					? new Rectangle(value.X, value.Y, checked((int)value.Width),
						checked((int)value.Height)) : Rectangle.Empty;
				return result;
			}

			internal CallResult WindowAction(Operation operation, ulong window)
				=> operation switch
				{
					Operation.WindowFocus => Invoke("focus window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_focus(connection, window, ref error)),
					Operation.WindowRaise => Invoke("raise window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_raise(connection, window, ref error)),
					Operation.WindowLower => Invoke("lower window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_lower(connection, window, ref error)),
					Operation.WindowClose => Invoke("close window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_close(connection, window, ref error)),
					Operation.WindowKill => Invoke("kill window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_kill(connection, window, ref error)),
					_ => throw new ArgumentOutOfRangeException(nameof(operation)),
				};

			internal CallResult MoveResize(bool xid, ulong window, int x, int y,
				uint width, uint height)
				=> xid
					? Invoke("move or resize X11 window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_move_resize_xid(connection, window,
								x, y, width, height, ref error))
					: Invoke("move or resize window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_move_resize(connection, window,
								x, y, width, height, ref error));

			internal CallResult SetWindowValue(Operation operation, ulong window,
				uint value)
				=> operation switch
				{
					Operation.WindowSetState => Invoke("set window state",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_set_state(connection, window, value, ref error)),
					Operation.WindowSetOpacity => Invoke("set window opacity",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_set_opacity(connection, window, value, ref error)),
					Operation.WindowSetAbove => Invoke("set window above",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_set_above(connection, window, value, ref error)),
					Operation.WindowSetDecorated => Invoke("set window decoration",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_set_decorated(connection, window, value, ref error)),
					_ => throw new ArgumentOutOfRangeException(nameof(operation)),
				};

			internal CallResult ReserveWindow(ulong cookie, int x, int y, uint ttlMs)
				=> Invoke("reserve window",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_window_reserve(connection, cookie, x, y, ttlMs,
							ref error));

			internal CallResult GetReservedWindow(ulong cookie, out ulong window)
			{
				ulong value = 0;
				var result = Invoke("get reserved window",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_window_get_reserved(connection, cookie, out value,
							ref error));
				window = value;
				return result;
			}

			internal CallResult ClipboardMimetypes(out string[] values)
			{
				Native.ksd_string_list_init(out var list);
				values = null;

				try
				{
					var result = Invoke("query clipboard MIME types",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_clipboard_mimetypes(connection, ref list,
								ref error));

					if (result.IsSuccess)
						values = CopyStringList(in list);

					return result;
				}
				finally
				{
					Native.ksd_string_list_clear(ref list);
				}
			}

			internal CallResult ClipboardContent(string mimetype, out byte[] value)
			{
				Native.ksd_bytes_init(out var bytes);
				value = null;

				try
				{
					var result = Invoke("read clipboard content",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_clipboard_content(connection, mimetype,
								ref bytes, ref error));

					if (result.IsSuccess)
						value = CopyBytes(in bytes, MaxTextBytes);

					return result;
				}
				finally
				{
					Native.ksd_bytes_clear(ref bytes);
				}
			}

			internal CallResult SetClipboardContent(string mimetype, byte[] data)
				=> Invoke("write clipboard content",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_clipboard_set_content(connection, mimetype, data,
							(UIntPtr)data.Length, ref error));

			internal CallResult SetClipboardText(string text)
				=> Invoke("write clipboard text",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_clipboard_set_text(connection, text, ref error));

			internal CallResult ClipboardText(out string value)
				=> ReadString("read clipboard text",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_clipboard_text(connection, ref result, ref error),
					out value);

			internal CallResult MouseCoordinates(bool absolute, int x, int y)
				=> absolute
					? Invoke("move pointer",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_mouse_move_absolute(connection, x, y, ref error))
					: Invoke("move pointer relatively",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_mouse_move_relative(connection, x, y, ref error));

			internal CallResult MouseButton(uint button, bool pressed)
				=> Invoke("send pointer button",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_mouse_button(connection, button,
							pressed ? 1u : 0u, ref error));

			internal CallResult MouseScroll(int delta, bool vertical)
				=> Invoke("scroll pointer",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_mouse_scroll(connection, delta,
							vertical ? 1u : 0u, ref error));

			internal CallResult SubscribeWindowWatch()
				=> Invoke("subscribe to window events",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_window_watch_subscribe(connection, ref error));

			internal CallResult SubscribeClipboardWatch()
				=> Invoke("subscribe to clipboard events",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_clipboard_watch_subscribe(connection, ref error));

			internal CallResult NextWindowEvent(int timeoutMs, out ushort kind,
				out string json)
			{
				Native.ksd_window_event_init(out var nativeEvent);
				kind = 0;
				json = null;

				try
				{
					var result = Invoke("wait for window event",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_watch_next(connection,
								checked((uint)timeoutMs), ref nativeEvent, ref error));

					if (result.IsSuccess)
					{
						if (nativeEvent.StructSize != NativeWindowEventStructSize)
							throw new InvalidDataException(
								"libkeysharp-desktop returned an incompatible window event.");

						kind = nativeEvent.Kind;
						json = CopyString(in nativeEvent.WindowJson, MaxTextBytes);
					}

					return result;
				}
				finally
				{
					Native.ksd_window_event_clear(ref nativeEvent);
				}
			}

			internal CallResult NextClipboardEvent(int timeoutMs, out string text,
				out string[] mimetypes)
			{
				Native.ksd_clipboard_event_init(out var nativeEvent);
				text = null;
				mimetypes = null;

				try
				{
					var result = Invoke("wait for clipboard event",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_clipboard_watch_next(connection,
								checked((uint)timeoutMs), ref nativeEvent, ref error));

					if (result.IsSuccess)
					{
						if (nativeEvent.StructSize != NativeClipboardEventStructSize)
							throw new InvalidDataException(
								"libkeysharp-desktop returned an incompatible clipboard event.");

						text = CopyString(in nativeEvent.Text, MaxTextBytes);
						mimetypes = CopyStringList(in nativeEvent.Mimetypes);
					}

					return result;
				}
				finally
				{
					Native.ksd_clipboard_event_clear(ref nativeEvent);
				}
			}

			private CallResult ReadString(string operation, NativeStringCall call,
				out string value)
			{
				Native.ksd_string_init(out var nativeString);
				value = null;

				try
				{
					var result = Invoke(operation,
						(IntPtr connection, ref NativeError error)
							=> call(connection, ref nativeString, ref error));

					if (result.IsSuccess)
						value = CopyString(in nativeString, MaxTextBytes);

					return result;
				}
				finally
				{
					Native.ksd_string_clear(ref nativeString);
				}
			}

			private CallResult Invoke(string operation, NativeCall call)
			{
				lock (gate)
				{
					ThrowIfClosed();
					Native.ksd_error_init(out var error);
					var status = NormalizeStatus(call(handle, ref error));
					return Result(status, in error, operation);
				}
			}

			private void ThrowIfClosed()
			{
				if (handle == IntPtr.Zero)
					throw new ObjectDisposedException(nameof(DesktopConnection));
			}

			public void Dispose()
			{
				lock (gate)
				{
					if (handle == IntPtr.Zero)
						return;

					Native.ksd_disconnect(handle);
					handle = IntPtr.Zero;
				}
			}
		}

		private delegate uint NativeCall(IntPtr connection, ref NativeError error);
		private delegate uint NativeStringCall(IntPtr connection,
			ref NativeString value, ref NativeError error);

		private static CallResult Result(NativeClientStatus status,
			in NativeError error, string operation)
			=> new(status, error.Detail, error.SystemError,
				error.GetMessage(), operation);

		private static NativeClientStatus NormalizeStatus(uint value)
		{
			var status = (NativeClientStatus)value;
			return status is NativeClientStatus.Ok
				or NativeClientStatus.Denied
				or NativeClientStatus.Unsupported
				or NativeClientStatus.InvalidRequest
				or NativeClientStatus.Unavailable
				or NativeClientStatus.Busy
				or NativeClientStatus.NotFound
				or NativeClientStatus.ResourceExhausted
				or NativeClientStatus.Timeout
				or NativeClientStatus.Cancelled
				or NativeClientStatus.Revoked
				or NativeClientStatus.Internal ? status : NativeClientStatus.Internal;
		}

		private static string WindowEventName(ushort kind)
			=> kind switch
			{
				1 => "create",
				2 => "close",
				3 => "active",
				4 => "title",
				5 => "minimize",
				6 => "restore",
				7 => "move",
				8 => "active-state",
				_ => throw new InvalidDataException(
					"libkeysharp-desktop returned an unknown window event kind."),
			};

		private static byte[] CopyBytes(in NativeBytes bytes, int maximumLength)
		{
			if (bytes.StructSize != NativeBytesStructSize
				|| bytes.Length > (nuint)maximumLength || bytes.Length > int.MaxValue
				|| bytes.Length != 0 && bytes.Data == IntPtr.Zero)
				throw new InvalidDataException(
					"libkeysharp-desktop returned an invalid byte buffer.");

			if (bytes.Length == 0)
				return [];

			return new ReadOnlySpan<byte>((void*)bytes.Data,
				checked((int)bytes.Length)).ToArray();
		}

		private static string CopyString(in NativeString value, int maximumLength)
		{
			if (value.StructSize != NativeStringStructSize
				|| value.Length > (nuint)maximumLength || value.Length > int.MaxValue
				|| value.Length != 0 && value.Data == IntPtr.Zero)
				throw new InvalidDataException(
					"libkeysharp-desktop returned an invalid string.");

			return value.Length == 0 ? string.Empty : StrictUtf8.GetString(
				new ReadOnlySpan<byte>((void*)value.Data, checked((int)value.Length)));
		}

		private static string[] CopyStringList(in NativeStringList list)
		{
			if (list.StructSize != NativeStringListStructSize
				|| list.Count > MaxMimetypes || list.Count > int.MaxValue
				|| list.Count != 0 && list.Items == IntPtr.Zero)
				throw new InvalidDataException(
					"libkeysharp-desktop returned an invalid string list.");

			var values = new string[checked((int)list.Count)];
			var items = (NativeString*)list.Items;

			for (var index = 0; index < values.Length; index++)
				values[index] = CopyString(in items[index], MaxMimetypeBytes);

			return values;
		}

		private static Bitmap ReadCapture(in NativeCapture capture)
		{
			if (capture.StructSize != NativeCaptureStructSize
				|| capture.Data.StructSize != NativeBytesStructSize
				|| capture.Width is 0 or > MaxCaptureDimension
				|| capture.Height is 0 or > MaxCaptureDimension
				|| capture.Data.Length is 0 or > MaxCaptureBytes
				|| capture.Data.Length > int.MaxValue || capture.Data.Data == IntPtr.Zero)
				throw new InvalidDataException(
					"libkeysharp-desktop returned an invalid capture.");

			var data = new ReadOnlySpan<byte>((void*)capture.Data.Data,
				checked((int)capture.Data.Length));

			if ((CaptureFormat)capture.Format == CaptureFormat.Png)
			{
				if (capture.Stride != 0)
					throw new InvalidDataException(
						"A PNG desktop capture has a non-zero stride.");

				using var stream = new MemoryStream(data.ToArray(), writable: false);
				return new Bitmap(stream);
			}

			if ((CaptureFormat)capture.Format != CaptureFormat.Bgra8Premultiplied
				|| capture.Stride < (ulong)capture.Width * 4
				|| capture.Data.Length != (nuint)((ulong)capture.Stride * capture.Height))
				throw new InvalidDataException(
					"libkeysharp-desktop returned invalid pixel data.");

			return BuildBitmapFromBgra(data, checked((int)capture.Width),
				checked((int)capture.Height), checked((int)capture.Stride));
		}

		private static readonly Vector128<byte> BgraToRgbaShuffleMask = Vector128.Create(
			(byte)2, 1, 0, 3,
			6, 5, 4, 7,
			10, 9, 8, 11,
			14, 13, 12, 15);

		private static Bitmap BuildBitmapFromBgra(ReadOnlySpan<byte> source,
			int width, int height, int stride)
		{
			var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgba);

			try
			{
				using var destination = bitmap.Lock();

				fixed (byte* sourceBase = source)
				{
					for (var row = 0; row < height; row++)
					{
						var sourceRow = sourceBase + ((long)row * stride);
						var destinationRow = (byte*)destination.Data
							+ ((long)row * destination.ScanWidth);
						ConvertBgraRowToRgba(sourceRow, destinationRow, width);
					}
				}

				return bitmap;
			}
			catch
			{
				bitmap.Dispose();
				throw;
			}
		}

		private static void ConvertBgraRowToRgba(byte* source,
			byte* destination, int width)
		{
			var index = 0;

			if (Ssse3.IsSupported)
			{
				for (; index + 4 <= width; index += 4)
				{
					var pixels = Sse2.LoadVector128(source + (index * 4));
					pixels = Ssse3.Shuffle(pixels, BgraToRgbaShuffleMask);
					Sse2.Store(destination + (index * 4), pixels);
				}
			}

			for (; index < width; index++)
			{
				var input = source + (index * 4);
				var output = destination + (index * 4);
				output[0] = input[2];
				output[1] = input[1];
				output[2] = input[0];
				output[3] = input[3];
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeError
		{
			internal uint StructSize;
			internal uint Detail;
			internal int SystemError;
			private uint reserved0;
			private fixed byte message[256];
			private fixed ulong reserved[4];

			internal string GetMessage()
			{
				fixed (byte* pointer = message)
				{
					var length = 0;

					while (length < 256 && pointer[length] != 0)
						length++;

					try { return StrictUtf8.GetString(pointer, length); }
					catch (DecoderFallbackException) { return string.Empty; }
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeConnectOptions
		{
			internal uint StructSize;
			internal uint Role;
			internal uint AuthorizationMode;
			internal uint RequestedScopes;
			internal IntPtr SocketPath;
			internal uint TimeoutMs;
			internal uint Flags;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeServiceInfo
		{
			internal uint StructSize;
			internal uint ClientAbiMajor;
			internal uint ClientAbiMinor;
			internal uint GrantedScopes;
			internal ulong AvailableOperations;
			internal uint Backend;
			private uint reserved0;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePoint
		{
			internal uint StructSize;
			internal int X;
			internal int Y;
			private uint reserved0;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRectangle
		{
			internal uint StructSize;
			internal int X;
			internal int Y;
			internal uint Width;
			internal uint Height;
			private uint reserved0;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeBytes
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Data;
			internal nuint Length;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeString
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Data;
			internal nuint Length;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeStringList
		{
			internal uint StructSize;
			private uint reserved0;
			internal IntPtr Items;
			internal nuint Count;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeCapture
		{
			internal uint StructSize;
			internal ushort Format;
			private ushort reserved0;
			internal uint Width;
			internal uint Height;
			internal uint Stride;
			internal NativeBytes Data;
			private fixed uint reserved[8];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeWindowEvent
		{
			internal uint StructSize;
			internal ushort Kind;
			private ushort reserved0;
			internal NativeString WindowJson;
			private fixed uint reserved[8];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeClipboardEvent
		{
			internal uint StructSize;
			private uint reserved0;
			internal NativeString Text;
			internal NativeStringList Mimetypes;
			private fixed uint reserved[8];
		}

		private static class Native
		{
			private const string Library = "libkeysharp-desktop.so.0";

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_error_init(out NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_connect_options_init(out NativeConnectOptions options);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_service_info_init(out NativeServiceInfo info);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_point_init(out NativePoint point);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_rectangle_init(out NativeRectangle rectangle);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_bytes_init(out NativeBytes value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_init(out NativeString value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_list_init(out NativeStringList value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_capture_init(out NativeCapture capture);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_window_event_init(out NativeWindowEvent value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_clipboard_event_init(out NativeClipboardEvent value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_bytes_clear(ref NativeBytes value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_clear(ref NativeString value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_list_clear(ref NativeStringList value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_capture_clear(ref NativeCapture capture);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_window_event_clear(ref NativeWindowEvent value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_clipboard_event_clear(ref NativeClipboardEvent value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_connect(ref NativeConnectOptions options,
				out IntPtr connection, ref NativeServiceInfo info, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_disconnect(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_authorize(IntPtr connection, uint mode,
				uint requestedScopes, out uint grantedScopes, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_ping(IntPtr connection, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_connection_granted_scopes(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_lease_next(IntPtr connection, uint timeoutMs,
				out uint revokedScopes, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_area(IntPtr connection,
				int x, int y, uint width, uint height, ref NativeCapture capture,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl,
				CharSet = CharSet.Ansi)]
			internal static extern uint ksd_capture_window(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string windowId,
				uint includeDecoration, ref NativeCapture capture, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_list_json(IntPtr connection,
				uint includeHidden, ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_active_json(IntPtr connection,
				ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_cursor_position(IntPtr connection,
				ref NativePoint point, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_work_area(IntPtr connection,
				ref NativeRectangle rectangle, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_focus(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_raise(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_lower(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_close(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_kill(IntPtr connection, ulong window,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_move_resize(IntPtr connection,
				ulong window, int x, int y, uint width, uint height, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_move_resize_xid(IntPtr connection,
				ulong window, int x, int y, uint width, uint height, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_state(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_opacity(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_above(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_set_decorated(IntPtr connection,
				ulong window, uint value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_reserve(IntPtr connection,
				ulong cookie, int x, int y, uint ttlMs, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_get_reserved(IntPtr connection,
				ulong cookie, out ulong window, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_mimetypes(IntPtr connection,
				ref NativeStringList values, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl,
				CharSet = CharSet.Ansi)]
			internal static extern uint ksd_clipboard_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				ref NativeBytes value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_text(IntPtr connection,
				ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl,
				CharSet = CharSet.Ansi)]
			internal static extern uint ksd_clipboard_set_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				byte[] data, UIntPtr length, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl,
				CharSet = CharSet.Ansi)]
			internal static extern uint ksd_clipboard_set_text(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string text, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_move_absolute(IntPtr connection,
				int x, int y, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_move_relative(IntPtr connection,
				int x, int y, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_button(IntPtr connection,
				uint button, uint pressed, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_mouse_scroll(IntPtr connection,
				int delta, uint vertical, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_watch_subscribe(IntPtr connection,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_watch_next(IntPtr connection,
				uint timeoutMs, ref NativeWindowEvent value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_watch_subscribe(IntPtr connection,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_watch_next(IntPtr connection,
				uint timeoutMs, ref NativeClipboardEvent value, ref NativeError error);
		}
	}
}
#endif
