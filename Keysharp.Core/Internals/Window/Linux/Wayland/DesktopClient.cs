#if LINUX
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Eto.Drawing;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal enum DesktopCaptureStatus
	{
		Unavailable,
		Failed,
		DeniedOrStopped,
		Captured
	}

	/// <summary>Typed client for <c>libkeysharp-desktop.so.0</c>.</summary>
	internal static unsafe partial class DesktopClient
	{
		private const int RequestTimeoutMs = 30_000;
		private const int AuthorizationTimeoutMs = 125_000;
		private const int ProbeTimeoutMs = 2_000;
		private const int CapabilityCacheMs = 1_000;
		private const int EventPollTimeoutMs = 1_000;
		private const uint NativeErrorStructSize = 304;
		private const string AuthorizationPendingMessage =
			"Desktop authorization is currently being requested.";

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

		internal enum Backend : uint
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
			// Enumeration without properties. Ungated, unlike WindowList.
			WindowHandles = 1UL << 29,
			WindowSetSkipTaskbar = 1UL << 30,
			CaptureDesktop = 1UL << 31,
			WindowQuery = 1UL << 32,
			WindowChildren = 1UL << 33,
			WindowAtPoint = 1UL << 34,
			DisplayList = 1UL << 35,
			KeyboardState = 1UL << 36,
			WindowSetTitle = 1UL << 37,
			WindowSetVisible = 1UL << 38,
			WindowRedraw = 1UL << 39,
			WindowClick = 1UL << 40,
			WindowButton = 1UL << 41,
			WindowFocusChild = 1UL << 42,
		}

		private enum CaptureFormat : ushort
		{
			Png = 1,
			Bgra8Premultiplied = 2,
		}

		private static readonly (Operation Operations, LinuxPermissionScope Scope)[] operationScopes =
		[
			(Operation.CaptureArea | Operation.CaptureWindow | Operation.CaptureDesktop,
				LinuxPermissionScope.ScreenCapture),
			(Operation.WindowList | Operation.WindowActive | Operation.WindowWatch
				| Operation.WindowQuery | Operation.WindowChildren | Operation.WindowAtPoint,
				LinuxPermissionScope.WindowMonitoring),
			(Operation.WindowFocus | Operation.WindowRaise | Operation.WindowLower
				| Operation.WindowClose | Operation.WindowKill | Operation.WindowMoveResize
				| Operation.WindowSetState | Operation.WindowSetOpacity | Operation.WindowSetAbove
				| Operation.WindowSetDecorated | Operation.WindowReserve | Operation.WindowGetReserved
				| Operation.WindowSetSkipTaskbar | Operation.WindowSetTitle | Operation.WindowSetVisible
				| Operation.WindowRedraw | Operation.WindowClick | Operation.WindowButton
				| Operation.WindowFocusChild, LinuxPermissionScope.WindowControl),
			(Operation.ClipboardMimetypes | Operation.ClipboardContent | Operation.ClipboardText
				| Operation.ClipboardWatch, LinuxPermissionScope.ClipboardMonitoring),
			(Operation.MouseMoveAbsolute | Operation.MouseMoveRelative | Operation.MouseButton
				| Operation.MouseScroll, LinuxPermissionScope.InputControl),
			(Operation.CursorPosition | Operation.WorkArea | Operation.ClipboardSetContent
				| Operation.WindowHandles | Operation.DisplayList | Operation.KeyboardState,
				LinuxPermissionScope.None),
		];

		private static readonly Dictionary<LinuxPermissionScope, DesktopRpcSession> sessions = new()
		{
			[LinuxPermissionScope.None] = new(LinuxPermissionScope.None),
			[LinuxPermissionScope.ScreenCapture] = new(LinuxPermissionScope.ScreenCapture),
			[LinuxPermissionScope.WindowMonitoring] = new(LinuxPermissionScope.WindowMonitoring),
			[LinuxPermissionScope.WindowControl] = new(LinuxPermissionScope.WindowControl),
			[LinuxPermissionScope.ClipboardMonitoring] = new(LinuxPermissionScope.ClipboardMonitoring),
			[LinuxPermissionScope.InputControl] = new(LinuxPermissionScope.InputControl),
		};

		private static bool Call(Operation operation, Func<DesktopConnection, CallResult> request)
			=> sessions[ScopeFor(operation)].TryUse(operation, request, out _);

		private static bool Call(Operation operation, Func<DesktopConnection, CallResult> request,
			out NativeClientStatus status)
			=> sessions[ScopeFor(operation)].TryUse(operation, request, out status);

		private static LinuxPermissionScope ScopeFor(Operation operation)
		{
			if (operation == Operation.None)
				return LinuxPermissionScope.None;

			foreach (var entry in operationScopes)
				if ((entry.Operations & operation) == operation)
					return entry.Scope;

			throw new ArgumentOutOfRangeException(nameof(operation), operation,
				"Desktop operation has no permission-scope mapping.");
		}

		internal static Bitmap CaptureWindow(string handle,
			bool includeDecoration)
		{
			if (string.IsNullOrWhiteSpace(handle))
				return null;

			Bitmap bitmap = null;
			return Call(Operation.CaptureWindow,
				connection => connection.CaptureWindow(handle, includeDecoration,
					out bitmap)) ? bitmap : null;
		}

		internal static Bitmap CaptureWindow(ulong handle,
			bool includeDecoration = false)
			=> CaptureWindow(Invariant(handle), includeDecoration);

		internal static DesktopCaptureStatus CaptureWithStatus(int x, int y,
			int width, int height, out Bitmap bitmap)
		{
			bitmap = null;

			if (width <= 0 || height <= 0)
				return DesktopCaptureStatus.Failed;

			Bitmap captured = null;
			var success = Call(Operation.CaptureArea,
				connection => connection.CaptureArea(x, y, checked((uint)width),
					checked((uint)height), out captured), out var status);

			if (success)
			{
				bitmap = captured;
				return DesktopCaptureStatus.Captured;
			}

			captured?.Dispose();
			return CaptureStatus(status);
		}

		internal static DesktopCaptureStatus CaptureDesktopWithStatus(out Bitmap bitmap)
		{
			bitmap = null;
			Bitmap captured = null;
			var success = Call(Operation.CaptureDesktop,
				connection => connection.CaptureDesktop(out captured), out var status);

			if (success)
			{
				bitmap = captured;
				return DesktopCaptureStatus.Captured;
			}

			captured?.Dispose();
			return CaptureStatus(status);
		}

		private static DesktopCaptureStatus CaptureStatus(NativeClientStatus status)
			=> status switch
			{
				NativeClientStatus.Denied or NativeClientStatus.Cancelled
					or NativeClientStatus.Revoked => DesktopCaptureStatus.DeniedOrStopped,
				NativeClientStatus.Unsupported or NativeClientStatus.Unavailable
					=> DesktopCaptureStatus.Unavailable,
				_ => DesktopCaptureStatus.Failed,
			};

		internal static bool AllowsCaptureFallback(DesktopCaptureStatus status)
			=> status is DesktopCaptureStatus.Unavailable or DesktopCaptureStatus.Failed;

		internal static bool ProbeProvider()
			=> sessions[LinuxPermissionScope.None].Supports(Operation.None);

		internal static bool TryProbeBackend(out Backend backend)
			=> sessions[LinuxPermissionScope.None].TryGetBackend(out backend);

		internal static bool ProviderSupportsAbsolutePointer()
			=> ProviderSupports(Operation.MouseMoveAbsolute);

		internal static bool ProviderSupportsWindowList()
			=> ProviderSupports(Operation.WindowList);

		internal static bool ProviderSupportsWindowWatch()
			=> ProviderSupports(Operation.WindowWatch);

		internal static bool ProviderSupportsTransparency()
			=> ProviderSupports(Operation.WindowSetOpacity);

		internal static bool ProviderSupportsClipboard()
			=> ProviderSupports(
				Operation.ClipboardMimetypes | Operation.ClipboardContent
				| Operation.ClipboardText | Operation.ClipboardWatch
				| Operation.ClipboardSetContent);

		private static bool ProviderSupports(Operation operations)
			=> sessions[LinuxPermissionScope.None].Supports(operations);

		internal static bool QueryCursorPosition(out int x, out int y)
		{
			var point = default(Point);
			var result = Call(Operation.CursorPosition,
				connection => connection.CursorPosition(out point));
			x = point.X;
			y = point.Y;
			return result;
		}

		internal static bool QueryWorkArea(out Rectangle area)
		{
			area = Rectangle.Empty;
			var value = Rectangle.Empty;
			var result = Call(Operation.WorkArea,
				connection => connection.WorkArea(out value));

			if (result)
				area = value;

			return result;
		}

		internal static byte[] QueryWindowList(bool includeHidden)
		{
			byte[] value = null;
			return Call(Operation.WindowList,
				connection => connection.WindowList(includeHidden, out value)) ? value : null;
		}

		/// <summary>
		/// Every window's handle and nothing else. Runs on the QUERY session, which
		/// carries no permission scope, so it raises no prompt: a handle says a window
		/// exists, which is not something a consent dialog can meaningfully ask about.
		/// Reading a window's title, class, owner or geometry is QueryWindowList, and
		/// that does need a grant.
		/// </summary>
		internal static byte[] QueryWindowHandles()
		{
			byte[] value = null;
			return Call(Operation.WindowHandles,
				connection => connection.WindowHandles(out value)) ? value : null;
		}

		internal static byte[] QueryActiveWindow()
		{
			byte[] value = null;
			return Call(Operation.WindowActive,
				connection => connection.ActiveWindow(out value)) ? value : null;
		}

		internal static bool FocusWindow(ulong handle)
			=> Call(Operation.WindowFocus, connection => connection.FocusWindow(handle));

		internal static bool RaiseWindow(ulong handle)
			=> Call(Operation.WindowRaise, connection => connection.RaiseWindow(handle));

		internal static bool LowerWindow(ulong handle)
			=> Call(Operation.WindowLower, connection => connection.LowerWindow(handle));

		internal static bool CloseWindow(ulong handle)
			=> Call(Operation.WindowClose, connection => connection.CloseWindow(handle));

		internal static bool KillWindow(ulong handle)
			=> Call(Operation.WindowKill, connection => connection.KillWindow(handle));

		internal static bool MoveResizeWindow(ulong handle,
			int x, int y, int width, int height)
			=> width >= 0 && height >= 0
				&& Call(Operation.WindowMoveResize,
					connection => connection.MoveResize(handle, x, y,
						checked((uint)width), checked((uint)height)));

		internal static bool SetWindowState(ulong handle, int state)
			=> Call(Operation.WindowSetState,
					connection => connection.SetWindowState(handle, (uint)state));

		internal static bool SetWindowOpacity(ulong handle, int opacity)
			=> Call(Operation.WindowSetOpacity,
					connection => connection.SetWindowOpacity(handle, (uint)opacity));

		internal static bool SetWindowAbove(ulong handle, bool above)
			=> Call(Operation.WindowSetAbove,
				connection => connection.SetWindowAbove(handle, above));

		internal static bool SetWindowDecorated(ulong handle, bool decorated)
			=> Call(Operation.WindowSetDecorated,
				connection => connection.SetWindowDecorated(handle, decorated));

		internal static bool SetWindowSkipTaskbar(ulong handle, bool skip)
			=> Call(Operation.WindowSetSkipTaskbar,
				connection => connection.SetWindowSkipTaskbar(handle, skip));

		internal static bool ReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> ttlMs >= 0 && Call(Operation.WindowReserve,
					connection => connection.ReserveWindow(cookie, x, y, checked((uint)ttlMs)));

		internal static string GetReservedWindow(ulong cookie)
		{
			ulong handle = 0;
			return Call(
				Operation.WindowGetReserved,
				connection => connection.GetReservedWindow(cookie, out handle))
				? Invariant(handle) : string.Empty;
		}

		internal static string[] GetClipboardMimetypes()
		{
			string[] value = null;
			return Call(Operation.ClipboardMimetypes,
				connection => connection.ClipboardMimetypes(out value)) ? value : null;
		}

		internal static byte[] GetClipboardContent(string mimetype)
		{
			if (string.IsNullOrEmpty(mimetype))
				return null;

			byte[] value = null;
			return Call(Operation.ClipboardContent,
				connection => connection.ClipboardContent(mimetype, out value)) ? value : null;
		}

		internal static string GetClipboardText()
		{
			string value = null;
			return Call(Operation.ClipboardText,
				connection => connection.ClipboardText(out value)) ? value : null;
		}

		internal static bool SetClipboardContent(string mimetype, byte[] bytes)
		{
			var data = bytes ?? System.Array.Empty<byte>();

			if (string.IsNullOrEmpty(mimetype))
				return false;

			return Call(Operation.ClipboardSetContent,
				connection => connection.SetClipboardContent(mimetype, data));
		}

		internal static bool SetClipboardText(string text)
		{
			var value = text ?? string.Empty;
			return Call(Operation.ClipboardSetContent,
				connection => connection.SetClipboardText(value));
		}

		internal static bool SendMouseMoveAbsolute(int x, int y)
			=> Call(Operation.MouseMoveAbsolute,
				connection => connection.MouseCoordinates(true, x, y));

		internal static bool SendMouseMoveRelative(int dx, int dy)
			=> Call(Operation.MouseMoveRelative,
				connection => connection.MouseCoordinates(false, dx, dy));

		internal static bool SendMouseButton(uint button, bool pressed)
			=> Call(Operation.MouseButton,
				connection => connection.MouseButton(button, pressed));

		internal static bool SendMouseScroll(int delta, bool vertical)
			=> Call(Operation.MouseScroll,
				connection => connection.MouseScroll(delta, vertical));

		internal static IDisposable WatchWindowEvents(
			Action<WaylandWindowEventKind, byte[]> handler, Action<Exception> onError = null)
			=> handler == null ? null : DesktopSubscription.StartWindow(
				handler, onError);

		internal static IDisposable WatchClipboardChanges(
			Action<string, string[]> handler, Action<Exception> onError = null)
			=> handler == null ? null : DesktopSubscription.StartClipboard(
				handler, onError);

		private static readonly object authorizationSync = new();
		private static readonly object promptSync = new();
		private static AuthorizationLease authorizationLease;
		private static LinuxPermissionScope declinedScopes;

		internal static PermissionResult RequestAuthorization(
			LinuxPermissionScope requestedScopes, bool prompt, bool forcePrompt = false)
		{
			if (requestedScopes == LinuxPermissionScope.None
				|| (requestedScopes & ~DesktopAuthorizationScopes) != 0)
				return new PermissionResult(PermissionStatus.Unsupported,
					"Invalid keysharp-desktop permission scope.");

			if (TrySettleAuthorization(requestedScopes, prompt, forcePrompt, out var settled))
				return settled;

			if (!prompt && !Monitor.TryEnter(promptSync))
				return new PermissionResult(PermissionStatus.Unsupported,
					AuthorizationPendingMessage);

			if (!prompt)
			{
				try
				{
					return TrySettleAuthorization(requestedScopes, false, forcePrompt,
						out settled) ? settled : AcquireAuthorization(requestedScopes, false);
				}
				finally
				{
					Monitor.Exit(promptSync);
				}
			}

			// The prompt gate serializes polkit dialogs without holding the lease-state lock.
			lock (promptSync)
			{
				if (TrySettleAuthorization(requestedScopes, prompt, forcePrompt, out settled))
					return settled;

				return AcquireAuthorization(requestedScopes, prompt);
			}
		}

		// Answers from current state alone. Returns false when a native check is needed.
		private static bool TrySettleAuthorization(LinuxPermissionScope requestedScopes,
			bool prompt, bool forcePrompt, out PermissionResult result)
		{
			lock (authorizationSync)
			{
				PruneAuthorizationLeaseLocked();
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

			AuthorizationLease lease = null;
			var created = false;

			try
			{
				lock (authorizationSync)
				{
					PruneAuthorizationLeaseLocked();
					lease = authorizationLease;
				}

				if (lease == null)
				{
					lease = new AuthorizationLease(DesktopConnection.Connect(
						ConnectionRole.AuthorizationLease, AuthorizationTimeoutMs));
					created = true;
				}

				var result = lease.Authorize(missing, prompt
					? AuthorizationMode.Request : AuthorizationMode.Check,
					out var granted);

				if (!result.IsSuccess)
				{
					if (created || result.ShouldReconnect
						|| result.Status == NativeClientStatus.Revoked)
					{
						lease.Dispose();

						lock (authorizationSync)
							if (ReferenceEquals(authorizationLease, lease))
								authorizationLease = null;
					}

					if (prompt && result.Status == NativeClientStatus.Denied)
					{
						lock (authorizationSync)
							declinedScopes |= missing;
					}

					return PermissionFailure(result);
				}

				if (created)
				{
					lock (authorizationSync)
					{
						authorizationLease = lease;
						lease.Start();
					}
				}

				lock (authorizationSync)
					declinedScopes &= ~granted;

				return new PermissionResult(PermissionStatus.Granted);
			}
			catch (Exception exception)
			{
				if (created)
					lease?.Dispose();

				DebugLine($"keysharp-desktop authorization failed: {exception.Message}");
				// A missing library reports through the loader's own multi-line probe list, which says nothing a
				// user can act on; name the component and how to install it instead.
				return new PermissionResult(PermissionStatus.Unsupported,
					exception is DllNotFoundException
					? "keysharp-desktop is not installed. Install it with keysharp-linux-setup.sh to use window, capture and clipboard operations."
					: $"keysharp-desktop is unavailable. {exception.Message}");
			}
		}

		private static LinuxPermissionScope GrantedScopesLocked()
			=> authorizationLease?.Scopes ?? LinuxPermissionScope.None;

		private static void PruneAuthorizationLeaseLocked()
		{
			if (authorizationLease?.IsAlive == false)
				authorizationLease = null;
		}

		private static PermissionResult PermissionFailure(in CallResult result)
		{
			var status = result.Status is NativeClientStatus.Denied
				or NativeClientStatus.Cancelled or NativeClientStatus.Revoked
				? PermissionStatus.Denied : PermissionStatus.Unsupported;
			return new PermissionResult(status, result.Message);
		}

		private static bool IsExpectedAuthorizationWait(PermissionResult result)
			=> result.Status == PermissionStatus.Denied
				|| result.Message == AuthorizationPendingMessage;

		private static bool IsExpectedAuthorizationFailure(in CallResult result)
			=> result.Status is NativeClientStatus.Denied or NativeClientStatus.Cancelled;

		private static string Invariant(ulong value)
			=> value.ToString(CultureInfo.InvariantCulture);

		[System.Diagnostics.Conditional("DEBUG")]
		[System.Diagnostics.Conditional("DEBUG")]
		private static void DebugLine(string message)
			=> Diagnostics.Debug.WriteLine(message);

		internal readonly record struct CallResult(
			NativeClientStatus Status,
			uint Detail,
			int SystemError,
			string Diagnostic,
			string Operation)
		{
			internal bool IsSuccess => Status == NativeClientStatus.Ok;
			internal bool ShouldReconnect => Status == NativeClientStatus.Unavailable
				|| (Status == NativeClientStatus.Timeout && SystemError != 0);
			internal bool IsExpectedPollTimeout
				=> Status == NativeClientStatus.Timeout && SystemError == 0;
			internal bool ShouldContinueEventPolling => IsSuccess || IsExpectedPollTimeout;
			internal string Message => NativeClientException.BuildMessage("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic);
			internal Exception Exception => new NativeClientException("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic);
		}

		private sealed class DesktopRpcSession
		{
			private readonly LinuxPermissionScope requiredScope;
			private readonly object sync = new();
			private DesktopConnection connection;
			private long retryProbeAt;
			private long supportsProbeUntil;
			private Backend supportsProbeBackend;
			private Operation supportsProbeOperations;
			private bool hasSupportsProbe;

			internal DesktopRpcSession(LinuxPermissionScope requiredScope)
			{
				this.requiredScope = requiredScope;
			}

			internal bool TryUse(Operation operation,
				Func<DesktopConnection, CallResult> request,
				out NativeClientStatus status)
			{
				lock (sync)
				{
					status = NativeClientStatus.Unsupported;

					if (!PrepareLocked(operation, out status))
						return false;

					if (ConnectionUsableLocked())
						return InvokeLocked(request, out status);
				}

				var permission = RequestAuthorization(requiredScope, prompt: false);

				if (!permission.IsGranted)
				{
					if (!IsExpectedAuthorizationWait(permission))
						DebugLine($"keysharp-desktop permission failed: {permission.Message}");
					status = permission.Status == PermissionStatus.Denied
						? NativeClientStatus.Denied : NativeClientStatus.Unsupported;
					return false;
				}

				lock (sync)
				{
					if (!PrepareLocked(operation, out status))
						return false;

					var authorization = connection.Authorize(requiredScope,
						AuthorizationMode.Check, out _);

					if (!authorization.IsSuccess)
					{
						status = authorization.Status;
						if (!IsExpectedAuthorizationFailure(authorization))
							DebugLine($"keysharp-desktop permission failed: {authorization.Message}");

						if (authorization.Status == NativeClientStatus.Revoked
							|| authorization.ShouldReconnect)
							ResetLocked();

						return false;
					}

					return InvokeLocked(request, out status);
				}
			}

			internal bool Supports(Operation operations)
			{
				lock (sync)
					return TryProbeLocked(out _, out var available)
						&& (available & operations) == operations;
			}

			internal bool TryGetBackend(out Backend backend)
			{
				lock (sync)
					return TryProbeLocked(out backend, out _);
			}

			private bool TryProbeLocked(out Backend backend, out Operation operations)
			{
				if (connection?.IsOpen == true)
				{
					backend = connection.Backend;
					operations = connection.AvailableOperations;
					return true;
				}

				var now = Environment.TickCount64;

				if (now >= supportsProbeUntil)
					try
					{
						using var probe = DesktopConnection.Connect(ConnectionRole.Rpc, ProbeTimeoutMs);
						hasSupportsProbe = true;
						supportsProbeBackend = probe.Backend;
						supportsProbeOperations = probe.AvailableOperations;
					}
					catch
					{
						hasSupportsProbe = false;
						ResetLocked();
					}
					finally
					{
						supportsProbeUntil = now + CapabilityCacheMs;
					}

				backend = supportsProbeBackend;
				operations = supportsProbeOperations;
				return hasSupportsProbe;
			}

			private bool PrepareLocked(Operation operation,
				out NativeClientStatus status)
			{
				status = NativeClientStatus.Unsupported;

				if (connection?.IsOpen != true)
				{
					ResetLocked();

					try
					{
						connection = DesktopConnection.Connect(ConnectionRole.Rpc,
							RequestTimeoutMs);
						retryProbeAt = Environment.TickCount64 + CapabilityCacheMs;
					}
					catch (NativeClientException exception)
					{
						status = exception.Status;
						DebugLine($"keysharp-desktop connection failed: {exception.Message}");
						return false;
					}
					catch (Exception exception)
					{
						status = NativeClientStatus.Internal;
						DebugLine($"keysharp-desktop connection failed: {exception.Message}");
						return false;
					}
				}

				if ((connection.AvailableOperations & operation) == 0)
				{
					if (Environment.TickCount64 >= retryProbeAt)
					{
						ResetLocked();
						return PrepareLocked(operation, out status);
					}

					return false;
				}

				return true;
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

			private bool InvokeLocked(Func<DesktopConnection, CallResult> request,
				out NativeClientStatus status)
			{
				try
				{
					var result = request(connection);
					status = result.Status;

					if (result.IsSuccess)
						return true;

					DebugLine($"keysharp-desktop operation failed: {result.Message}");

					// A lost reply can follow a completed mutation. Reconnect on the next call without replaying it.
					if (result.Status == NativeClientStatus.Revoked || result.ShouldReconnect)
						ResetLocked();

					return false;
				}
				catch (Exception exception)
				{
					DebugLine($"keysharp-desktop operation failed: {exception.Message}");
					status = NativeClientStatus.Internal;
					ResetLocked();
					return false;
				}
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
			private readonly Channel<AuthorizationRequest> requests = Channel.CreateUnbounded<AuthorizationRequest>();
			private Task reader;
			private uint scopes;
			private int disposed;
			private int readerThreadId;

			internal AuthorizationLease(DesktopConnection connection)
				=> this.connection = connection;

			internal LinuxPermissionScope Scopes
				=> (LinuxPermissionScope)Volatile.Read(ref scopes);

			internal bool IsAlive
				=> Volatile.Read(ref disposed) == 0
					&& Scopes != LinuxPermissionScope.None
					&& reader?.IsCompleted != true;

			internal void Start()
				=> reader = Task.Run(ReadEvents);

			internal CallResult Authorize(LinuxPermissionScope requestedScopes,
				AuthorizationMode mode, out LinuxPermissionScope granted)
			{
				if (Volatile.Read(ref disposed) != 0)
					throw new ObjectDisposedException(nameof(AuthorizationLease));

				if (reader == null)
				{
					var result = connection.Authorize(requestedScopes, mode, out granted);

					if (result.IsSuccess)
						Volatile.Write(ref scopes, (uint)granted);

					return result;
				}

				var request = new AuthorizationRequest(requestedScopes, mode);

				if (!requests.Writer.TryWrite(request))
					throw new ObjectDisposedException(nameof(AuthorizationLease));

				var response = request.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(
					AuthorizationTimeoutMs + EventPollTimeoutMs * 2)).GetAwaiter().GetResult();
				granted = response.Granted;
				return response.Result;
			}

			private void ReadEvents()
			{
				Volatile.Write(ref readerThreadId, Environment.CurrentManagedThreadId);
				Exception failure = null;

				try
				{
					while (Volatile.Read(ref disposed) == 0)
					{
						DrainAuthorizationRequests();

						if (Volatile.Read(ref disposed) != 0)
							break;

						var result = connection.LeaseNext(EventPollTimeoutMs, out var revoked);

						if (result.IsExpectedPollTimeout)
							continue;

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
					failure = exception;
					DebugLine($"keysharp-desktop authorization lease ended: {exception.Message}");
				}
				finally
				{
					Interlocked.Exchange(ref disposed, 1);
					Volatile.Write(ref scopes, 0);

					CloseRequests(failure
						?? new ObjectDisposedException(nameof(AuthorizationLease)));

					connection.Dispose();
				}
			}

			private void DrainAuthorizationRequests()
			{
				while (requests.Reader.TryRead(out var request))
				{
					try
					{
						var result = connection.Authorize(request.Scopes, request.Mode,
							out var granted);

						if (result.IsSuccess)
							Volatile.Write(ref scopes, (uint)granted);

						request.Completion.TrySetResult(new(result, granted));
					}
					catch (Exception exception)
					{
						request.Completion.TrySetException(exception);
					}
				}
			}

			private void CloseRequests(Exception failure)
			{
				requests.Writer.TryComplete();
				while (requests.Reader.TryRead(out var request))
					request.Completion.TrySetException(failure);
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				Volatile.Write(ref scopes, 0);
				CloseRequests(new ObjectDisposedException(nameof(AuthorizationLease)));

				if (reader == null)
				{
					connection.Dispose();
					return;
				}

				if (Volatile.Read(ref readerThreadId) != Environment.CurrentManagedThreadId)
					try { reader.Wait(EventPollTimeoutMs * 2); } catch { }
			}

			private sealed class AuthorizationRequest
			{
				internal AuthorizationRequest(LinuxPermissionScope scopes,
					AuthorizationMode mode)
				{
					Scopes = scopes;
					Mode = mode;
				}

				internal LinuxPermissionScope Scopes { get; }
				internal AuthorizationMode Mode { get; }
				internal TaskCompletionSource<AuthorizationResponse> Completion { get; }
					= new(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			private readonly record struct AuthorizationResponse(
				CallResult Result, LinuxPermissionScope Granted);
		}

		private sealed class DesktopSubscription : IDisposable
		{
			private readonly DesktopConnection connection;
			private readonly Action<WaylandWindowEventKind, byte[]> windowHandler;
			private readonly Action<string, string[]> clipboardHandler;
			private readonly Action<Exception> onError;
			private Task reader;
			private int disposed;
			private int readerThreadId;

			private DesktopSubscription(DesktopConnection connection,
				Action<WaylandWindowEventKind, byte[]> windowHandler,
				Action<string, string[]> clipboardHandler,
				Action<Exception> onError)
			{
				this.connection = connection;
				this.windowHandler = windowHandler;
				this.clipboardHandler = clipboardHandler;
				this.onError = onError;
			}

			internal static IDisposable StartWindow(Action<WaylandWindowEventKind, byte[]> handler,
				Action<Exception> onError)
				=> Start(Operation.WindowWatch, handler, null, onError);

			internal static IDisposable StartClipboard(Action<string, string[]> handler,
				Action<Exception> onError)
				=> Start(Operation.ClipboardWatch, null, handler, onError);

			private static IDisposable Start(Operation operation,
				Action<WaylandWindowEventKind, byte[]> windowHandler,
				Action<string, string[]> clipboardHandler,
				Action<Exception> onError)
			{
				DesktopConnection connection = null;

				try
				{
					connection = DesktopConnection.Connect(ConnectionRole.EventStream,
						RequestTimeoutMs);

					var authorization = connection.Authorize(ScopeFor(operation),
						AuthorizationMode.Check, out _);

					if (!authorization.IsSuccess)
					{
						if (IsExpectedAuthorizationFailure(authorization))
						{
							connection.Dispose();
							return null;
						}

						throw authorization.Exception;
					}

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

							if (result.IsSuccess && WindowEventKind(kind) is { } eventKind)
							{
								try { windowHandler(eventKind, json); }
								catch (Exception exception)
								{
									DebugLine($"keysharp-desktop window event handler failed: {exception.Message}");
								}
							}
						}
						else
						{
							result = connection.NextClipboardEvent(EventPollTimeoutMs,
								out var text, out var mimetypes);

							if (result.IsSuccess)
							{
								try { clipboardHandler(text, mimetypes); }
								catch (Exception exception)
								{
									DebugLine($"keysharp-desktop clipboard event handler failed: {exception.Message}");
								}
							}
						}

						if (result.ShouldContinueEventPolling)
							continue;

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

		private sealed partial class DesktopConnection : IDisposable
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
						return (LinuxPermissionScope)Native.ksd_connection_granted_scopes(handle);
					}
				}
			}

			internal static DesktopConnection Connect(ConnectionRole role, int timeoutMs)
			{
				Native.ksd_connect_options_init(out var options);
				Native.ksd_service_info_init(out var info);
				var error = new NativeError { StructSize = NativeErrorStructSize };
				IntPtr nativeHandle = IntPtr.Zero;

				try
				{
					options.Role = (uint)role;
					options.AuthorizationMode = (uint)AuthorizationMode.Check;
					options.TimeoutMs = checked((uint)timeoutMs);
					var status = (NativeClientStatus)Native.ksd_connect(ref options,
						ref nativeHandle, ref info, ref error);
					var result = Result(status, in error, "connect");

					if (!result.IsSuccess)
						throw result.Exception;

					var reportedBackend = (Backend)info.Backend;
					var backend = Enum.IsDefined(reportedBackend)
						? reportedBackend : Backend.Generic;
					var operations = (Operation)info.AvailableOperations;

					if (info.ClientAbiMajor != 0 || info.ClientAbiMinor < 8)
						throw new InvalidDataException(
							$"libkeysharp-desktop client ABI {info.ClientAbiMajor}.{info.ClientAbiMinor} "
							+ "is incompatible; Keysharp requires ABI 0.8 or later.");

					var connection = new DesktopConnection(nativeHandle, backend, operations);
					nativeHandle = IntPtr.Zero;
					return connection;
				}
				finally
				{
					if (nativeHandle != IntPtr.Zero)
						Native.ksd_disconnect(nativeHandle);
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
				return result;
			}

			internal CallResult LeaseNext(int timeoutMs,
				out LinuxPermissionScope revoked)
			{
				uint nativeRevoked = 0;
				var result = Invoke("wait for authorization revocation",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_lease_next(connection, checked((uint)timeoutMs),
							out nativeRevoked, ref error));
				revoked = (LinuxPermissionScope)nativeRevoked;
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

			internal CallResult CaptureDesktop(out Bitmap bitmap)
			{
				Native.ksd_capture_init(out var capture);
				bitmap = null;

				try
				{
					var result = Invoke("capture desktop",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_capture_desktop(connection, ref capture, ref error));

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

			internal CallResult WindowList(bool includeHidden, out byte[] value)
				=> ReadUtf8("list windows",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_list_json(connection,
							includeHidden ? 1u : 0u, ref result, ref error), out value);

			internal CallResult WindowHandles(out byte[] value)
				=> ReadUtf8("list window handles",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_handles_json(connection, ref result, ref error),
					out value);

			internal CallResult ActiveWindow(out byte[] value)
				=> ReadUtf8("query active window",
					(IntPtr connection, ref NativeString result, ref NativeError error)
						=> Native.ksd_window_active_json(connection, ref result, ref error),
					out value);

			internal CallResult CursorPosition(out Point point)
			{
				Native.ksd_point_init(out var value);
				var result = Invoke("query cursor position",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_cursor_position(connection, ref value, ref error));

				point = result.IsSuccess ? new Point(value.X, value.Y) : default;
				return result;
			}

			internal CallResult WorkArea(out Rectangle rectangle)
			{
				Native.ksd_rectangle_init(out var value);
				var result = Invoke("query work area",
					(IntPtr connection, ref NativeError error)
						=> Native.ksd_work_area(connection, ref value, ref error));

				rectangle = result.IsSuccess
					? new Rectangle(value.X, value.Y, checked((int)value.Width),
						checked((int)value.Height)) : Rectangle.Empty;
				return result;
			}

			internal CallResult FocusWindow(ulong window)
				=> Invoke("focus window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_focus(connection, window, ref error));

			internal CallResult RaiseWindow(ulong window)
				=> Invoke("raise window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_raise(connection, window, ref error));

			internal CallResult LowerWindow(ulong window)
				=> Invoke("lower window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_lower(connection, window, ref error));

			internal CallResult CloseWindow(ulong window)
				=> Invoke("close window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_close(connection, window, ref error));

			internal CallResult KillWindow(ulong window)
				=> Invoke("kill window", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_kill(connection, window, ref error));

			internal CallResult MoveResize(ulong window, int x, int y,
				uint width, uint height)
				=> Invoke("move or resize window",
						(IntPtr connection, ref NativeError error)
							=> Native.ksd_window_move_resize(connection, window,
								x, y, width, height, ref error));

			internal CallResult SetWindowState(ulong window, uint state)
				=> Invoke("set window state", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_state(connection, window, state, ref error));

			internal CallResult SetWindowOpacity(ulong window, uint opacity)
				=> Invoke("set window opacity", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_opacity(connection, window, opacity, ref error));

			internal CallResult SetWindowAbove(ulong window, bool above)
				=> Invoke("set window above", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_above(connection, window, above ? 1u : 0u, ref error));

			internal CallResult SetWindowDecorated(ulong window, bool decorated)
				=> Invoke("set window decoration", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_decorated(connection, window, decorated ? 1u : 0u, ref error));

			internal CallResult SetWindowSkipTaskbar(ulong window, bool skip)
				=> Invoke("set window taskbar visibility", (IntPtr connection, ref NativeError error)
					=> Native.ksd_window_set_skip_taskbar(connection, window, skip ? 1u : 0u, ref error));

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
						value = CopyBytes(in bytes);

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
				out byte[] json)
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
						kind = nativeEvent.Kind;
						json = CopyUtf8(in nativeEvent.WindowJson);
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
						text = CopyString(in nativeEvent.Text);
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
						value = CopyString(in nativeString);

					return result;
				}
				finally
				{
					Native.ksd_string_clear(ref nativeString);
				}
			}

			private CallResult ReadUtf8(string operation, NativeStringCall call,
				out byte[] value)
			{
				Native.ksd_string_init(out var nativeString);
				value = null;

				try
				{
					var result = Invoke(operation,
						(IntPtr connection, ref NativeError error)
							=> call(connection, ref nativeString, ref error));

					if (result.IsSuccess)
						value = CopyUtf8(in nativeString);

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
					var error = new NativeError { StructSize = NativeErrorStructSize };
					var status = (NativeClientStatus)call(handle, ref error);
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

		private static WaylandWindowEventKind? WindowEventKind(ushort kind)
			=> kind switch
			{
				1 => WaylandWindowEventKind.Created,
				2 => WaylandWindowEventKind.Closed,
				3 => WaylandWindowEventKind.Activated,
				4 => WaylandWindowEventKind.TitleChanged,
				5 => WaylandWindowEventKind.Minimized,
				6 => WaylandWindowEventKind.Restored,
				7 => WaylandWindowEventKind.MoveResized,
				8 => WaylandWindowEventKind.ActiveStateChanged,
				_ => null,
			};

		private static byte[] CopyBytes(in NativeBytes bytes)
		{
			if (bytes.Length == 0)
				return [];

			return new ReadOnlySpan<byte>((void*)bytes.Data,
				checked((int)bytes.Length)).ToArray();
		}

		private static string CopyString(in NativeString value)
		{
			return value.Length == 0 ? string.Empty : Encoding.UTF8.GetString(
				new ReadOnlySpan<byte>((void*)value.Data, checked((int)value.Length)));
		}

		private static byte[] CopyUtf8(in NativeString value)
			=> value.Length == 0 ? [] : new ReadOnlySpan<byte>((void*)value.Data,
				checked((int)value.Length)).ToArray();

		private static string[] CopyStringList(in NativeStringList list)
		{
			var values = new string[checked((int)list.Count)];
			var items = (NativeString*)list.Items;

			for (var index = 0; index < values.Length; index++)
				values[index] = CopyString(in items[index]);

			return values;
		}

		private static Bitmap ReadCapture(in NativeCapture capture)
		{
			if ((CaptureFormat)capture.Format == CaptureFormat.Png)
			{
				using var stream = new UnmanagedMemoryStream((byte*)capture.Data.Data,
					checked((long)capture.Data.Length));
				return new Bitmap(stream);
			}

			if ((CaptureFormat)capture.Format != CaptureFormat.Bgra8Premultiplied)
				throw new InvalidDataException(
					"libkeysharp-desktop returned an unknown capture format.");

			var data = new ReadOnlySpan<byte>((void*)capture.Data.Data,
				checked((int)capture.Data.Length));
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

			if (Vector128.IsHardwareAccelerated)
			{
				for (; index + 4 <= width; index += 4)
				{
					var input = source + (index * 4);
					var output = destination + (index * 4);
					var pixels = Vector128.Load(input);
					Vector128.Shuffle(pixels, BgraToRgbaShuffleMask)
						.Store(output);

					if (input[3] != byte.MaxValue || input[7] != byte.MaxValue
						|| input[11] != byte.MaxValue || input[15] != byte.MaxValue)
						for (var pixel = 0; pixel < 4; pixel++)
							UnpremultiplyPixel(output + pixel * 4);
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
				UnpremultiplyPixel(output);
			}
		}

		private static void UnpremultiplyPixel(byte* pixel)
		{
			var alpha = pixel[3];

			if (alpha == byte.MaxValue)
				return;

			if (alpha == 0)
			{
				pixel[0] = pixel[1] = pixel[2] = 0;
				return;
			}

			pixel[0] = Unpremultiply(pixel[0], alpha);
			pixel[1] = Unpremultiply(pixel[1], alpha);
			pixel[2] = Unpremultiply(pixel[2], alpha);
		}

		private static byte Unpremultiply(byte component, byte alpha)
			=> (byte)Math.Min(byte.MaxValue,
				(component * byte.MaxValue + alpha / 2) / alpha);

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

					return Encoding.UTF8.GetString(pointer, length);
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

		private static partial class Native
		{
			private const string Library = "libkeysharp-desktop.so.0";

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
				ref IntPtr connection, ref NativeServiceInfo info, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_disconnect(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_authorize(IntPtr connection, uint mode,
				uint requestedScopes, out uint grantedScopes, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_connection_granted_scopes(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_lease_next(IntPtr connection, uint timeoutMs,
				out uint revokedScopes, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_area(IntPtr connection,
				int x, int y, uint width, uint height, ref NativeCapture capture,
				ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_desktop(IntPtr connection,
				ref NativeCapture capture, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_capture_window(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string windowId,
				uint includeDecoration, ref NativeCapture capture, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_list_json(IntPtr connection,
				uint includeHidden, ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_window_handles_json(IntPtr connection,
				ref NativeString value, ref NativeError error);

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
			internal static extern uint ksd_window_set_skip_taskbar(IntPtr connection,
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

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				ref NativeBytes value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_text(IntPtr connection,
				ref NativeString value, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_clipboard_set_content(IntPtr connection,
				[MarshalAs(UnmanagedType.LPUTF8Str)] string mimetype,
				byte[] data, UIntPtr length, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
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
