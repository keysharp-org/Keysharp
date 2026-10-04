#if LINUX
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
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

	/// <summary>Typed client for <c>libkeysharp-desktop.so.1</c>.</summary>
	internal static unsafe partial class DesktopClient
	{
		private const int RequestTimeoutMs = 30_000;
		private const int AuthorizationTimeoutMs = 125_000;
		private const int ProbeTimeoutMs = 2_000;
		private const int CapabilityCacheMs = 1_000;
		private const int CapabilityRecheckMaximumMs = 60_000;
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

		private static readonly DesktopRpcSession fastSession = new();
		private static readonly DesktopRpcSession slowSession = new();

		private static DesktopRpcSession SessionFor(Operation operation)
			=> (operation & (Operation.CaptureArea | Operation.CaptureWindow | Operation.CaptureDesktop)) != 0
				? slowSession : fastSession;

		private static bool Call(Operation operation, Func<DesktopConnection, CallResult> request)
			=> SessionFor(operation).TryUse(operation, request, out _);

		private static bool Call(Operation operation, Func<DesktopConnection, CallResult> request,
			out NativeClientStatus status, NativeClientStatus? suppressedFailure = null)
			=> SessionFor(operation).TryUse(operation, request, out status, suppressedFailure);

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
			=> Capabilities.TryGet(out _, out _);

		internal static bool TryProbeBackend(out Backend backend)
			=> Capabilities.TryGet(out backend, out _);

		/// <summary>Connects and authorizes the session for <paramref name="scope"/> ahead of its first request.</summary>
		internal static bool OpenSession(LinuxPermissionScope scope)
			=> RequestAuthorization(scope, false).Status == PermissionStatus.Granted
				&& (scope == LinuxPermissionScope.ScreenCapture ? slowSession : fastSession).TryOpen();

		internal static bool ProviderSupportsAbsolutePointer()
			=> ProviderSupports(Operation.MouseMoveAbsolute);

		internal static bool ProviderSupportsWindowList()
			=> ProviderSupports(Operation.WindowList);

		internal static bool ProviderSupportsWindowWatch()
			=> ProviderSupports(Operation.WindowWatch);

		internal static bool ProviderSupportsTransparency()
			=> ProviderSupports(Operation.WindowSetOpacity);

		internal static bool ProviderSupportsWindowKill()
			=> ProviderSupports(Operation.WindowKill);

		internal static bool ProviderSupportsWindowMove()
			=> ProviderSupports(Operation.WindowMoveResize);

		internal static bool TryProbeWindowSupport(out bool capture, out bool visibility)
		{
			capture = visibility = false;

			if (!Capabilities.TryGet(out _, out var operations))
				return false;

			capture = (operations & Operation.CaptureWindow) != 0;
			visibility = (operations & Operation.WindowSetVisible) != 0;
			return true;
		}

		internal static bool ProviderSupportsClipboard()
			=> ProviderSupports(
				Operation.ClipboardMimetypes | Operation.ClipboardContent
				| Operation.ClipboardText | Operation.ClipboardWatch
				| Operation.ClipboardSetContent);

		private static bool ProviderSupports(Operation operations)
			=> Capabilities.Offers(operations);

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

		internal static IDisposable WatchWindowEvents(Action<WaylandWindowEventKind, WaylandWindowInfo> handler,
			Action<Exception> onError = null)
			=> SubscribeState(1, message => DispatchWindowEvents(message.Kind, message.Window, message.PreviousWindow, handler), onError);

		internal static void DispatchWindowEvents(uint kind, WaylandWindowInfo window, WaylandWindowInfo previous,
			Action<WaylandWindowEventKind, WaylandWindowInfo> handler)
		{
			if (window == null || kind < 4 || kind > 11) return;
			WaylandWindowEventKind? primary = kind switch
			{
				4 => WaylandWindowEventKind.Created, 5 => WaylandWindowEventKind.Closed,
				6 => WaylandWindowEventKind.Shown, 7 => WaylandWindowEventKind.Hidden,
				8 => WaylandWindowEventKind.MoveResized, 9 => WaylandWindowEventKind.TitleChanged,
				10 => window.Active ? WaylandWindowEventKind.Activated : WaylandWindowEventKind.ActiveStateChanged,
				_ => null
			};
			if (primary.HasValue) handler(primary.Value, window);
			if (previous == null || kind is 4 or 5) return;
			bool Known(WaylandWindowFields field) => previous.HasKnownField(field) && window.HasKnownField(field);
			// A full delta can carry several changes even though its wire kind names one.
			if (kind is not (6 or 7) && Known(WaylandWindowFields.Visible) && previous.Visible != window.Visible)
				handler(window.Visible ? WaylandWindowEventKind.Shown : WaylandWindowEventKind.Hidden, window);
			if (kind != 10 && Known(WaylandWindowFields.Active) && previous.Active != window.Active)
				handler(window.Active ? WaylandWindowEventKind.Activated : WaylandWindowEventKind.ActiveStateChanged, window);
			if (kind != 9 && Known(WaylandWindowFields.Title) && previous.Title != window.Title)
				handler(WaylandWindowEventKind.TitleChanged, window);
			if (kind != 8 && Known(WaylandWindowFields.Frame) && previous.Bounds != window.Bounds)
				handler(WaylandWindowEventKind.MoveResized, window);
			if (Known(WaylandWindowFields.Minimized) && previous.Minimized != window.Minimized)
				handler(window.Minimized ? WaylandWindowEventKind.Minimized : WaylandWindowEventKind.Restored, window);
		}

		internal static IDisposable WatchClipboardChanges(Action<string, string[]> handler,
			Action<Exception> onError = null)
			=> SubscribeState(8, message =>
			{
				if (message.Data.Length == 0) return;
				using var document = System.Text.Json.JsonDocument.Parse(message.Data);
				var root = document.RootElement;
				handler(DesktopWindowParser.Text(root, "text"), root.TryGetProperty("mimetypes", out var types)
					? types.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : []);
			}, onError);

		internal static IDisposable SubscribeKeyboardState(Action<byte[]> handler, Action<Exception> onError = null)
			=> SubscribeState(2, message => handler(message.Data), onError);

		private static readonly object authorizationSync = new();
		private static object leaseCreation = new();
		private static int authorizationGeneration;
		private static AuthorizationLease authorizationLease;
		private static LinuxPermissionScope declinedScopes;
		private static Script scriptOwner;
		private static bool clientsStopped;

		internal static void RegisterOwner(Script script)
		{
			lock (authorizationSync) { scriptOwner = script; clientsStopped = false; }
		}

		internal static void DisconnectClients(Script script)
		{
			AuthorizationLease retired;
			ulong fastLease, slowLease;
			lock (authorizationSync)
			{
				if (!ReferenceEquals(scriptOwner, script)) return;
				clientsStopped = true;
				authorizationGeneration++;
				leaseCreation = new();
				scriptOwner = null;
				retired = authorizationLease;
				authorizationLease = null;
				declinedScopes = LinuxPermissionScope.None;
				fastLease = fastSession.LeaseId;
				slowLease = slowSession.LeaseId;
				Keysharp.Internals.Input.Linux.DesktopKeyboardState.Current.Reset();
				Capabilities.Forget();
			}
			retired?.Dispose();
			fastSession.Dispose(fastLease);
			slowSession.Dispose(slowLease);
		}

		private static AuthorizationLease GetLease()
		{
			object creating;
			int version;
			lock (authorizationSync)
			{
				if (clientsStopped) throw new ObjectDisposedException("keysharp-desktop session");
				if (authorizationLease?.IsOpen == true) return authorizationLease;
				creating = leaseCreation;
				version = authorizationGeneration;
			}
			lock (creating)
			{
				AuthorizationLease retired;
				lock (authorizationSync)
				{
					if (clientsStopped || version != authorizationGeneration)
						throw new ObjectDisposedException("keysharp-desktop session");
					if (authorizationLease?.IsOpen == true) return authorizationLease;
					retired = authorizationLease;
					authorizationLease = null;
				}
				retired?.Dispose();
				var created = new AuthorizationLease();
				lock (authorizationSync)
				{
					if (!clientsStopped && version == authorizationGeneration)
					{
						authorizationLease = created;
						created.PublishCapabilities();
						return created;
					}
				}
				created.Dispose();
				throw new ObjectDisposedException("keysharp-desktop session");
			}
		}

		internal static PermissionResult RequestAuthorization(LinuxPermissionScope requestedScopes,
			bool prompt, bool forcePrompt = false)
		{
			if (requestedScopes == LinuxPermissionScope.None)
				return new PermissionResult(PermissionStatus.Granted);
			if ((requestedScopes & ~DesktopAuthorizationScopes) != 0)
				return new PermissionResult(PermissionStatus.Unsupported, "Invalid keysharp-desktop permission scope.");

			try
			{
				var lease = GetLease();
				if ((lease.Scopes & requestedScopes) == requestedScopes)
					return new PermissionResult(PermissionStatus.Granted);
				lock (authorizationSync)
				{
					if (forcePrompt) declinedScopes &= ~requestedScopes;
					else if (prompt && (declinedScopes & requestedScopes) != 0)
						return new PermissionResult(PermissionStatus.Denied, "Desktop permission denied.");
				}
				if (!prompt && !Monitor.TryEnter(lease.PromptSync))
					return new PermissionResult(PermissionStatus.Unsupported, AuthorizationPendingMessage);
				if (prompt) Monitor.Enter(lease.PromptSync);
				try
				{
					var result = lease.Authorize(requestedScopes, prompt ? AuthorizationMode.Request : AuthorizationMode.Check);
					lock (authorizationSync)
					{
						if (!ReferenceEquals(authorizationLease, lease) || !lease.IsOpen)
							return new PermissionResult(PermissionStatus.Unsupported, "The desktop lease has stopped.");
						if (result.Status == NativeClientStatus.Denied && prompt) declinedScopes |= requestedScopes;
						else if (result.IsSuccess) declinedScopes &= ~requestedScopes;
					}
					return result.IsSuccess ? new PermissionResult(PermissionStatus.Granted) : PermissionFailure(result);
				}
				finally { Monitor.Exit(lease.PromptSync); }
			}
			catch (Exception exception)
			{
				return new PermissionResult(PermissionStatus.Unsupported,
					$"keysharp-desktop is unavailable; Keysharp requires libkeysharp-desktop client ABI 1.0 (SONAME 1), protocol 3. {exception.Message}");
			}
		}

		private static PermissionResult PermissionFailure(in CallResult result)
		{
			var status = result.Status is NativeClientStatus.Denied
				or NativeClientStatus.Cancelled or NativeClientStatus.Revoked
				? PermissionStatus.Denied : PermissionStatus.Unsupported;
			return new PermissionResult(status, result.Message);
		}

		private static string Invariant(ulong value)
			=> value.ToString(CultureInfo.InvariantCulture);

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
			internal bool ShouldReconnect => Status is NativeClientStatus.Unavailable or NativeClientStatus.Timeout;
			internal bool IsExpectedPollTimeout
				=> Status == NativeClientStatus.Timeout && SystemError == 0;
			internal string Message => NativeClientException.BuildMessage("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic);
			internal Exception Exception => new NativeClientException("keysharp-desktop",
				Operation, Status, Detail, SystemError, Diagnostic);
		}

		// Connections share the service's capabilities. Missing operations are rechecked with backoff;
		// backend probes also refresh known answers so late provider registration is discovered.
		private static class Capabilities
		{
			private sealed record Snapshot(Backend Backend, Operation Operations);

			private static readonly object sync = new();
			private static Snapshot current;
			private static long probeAt;
			private static int recheckMs = CapabilityCacheMs;

			internal static void Learn(Backend backend, Operation operations)
			{
				lock (sync)
				{
					if ((operations & ~(current?.Operations ?? Operation.None)) != 0)
						recheckMs = CapabilityCacheMs;

					probeAt = Math.Max(probeAt, Environment.TickCount64 + CapabilityCacheMs);
					Volatile.Write(ref current, new Snapshot(backend, operations));
				}
			}

			internal static void Forget()
			{
				lock (sync)
					Volatile.Write(ref current, null);
			}

			internal static bool TryGet(out Backend backend, out Operation operations)
			{
				var known = Volatile.Read(ref current);

				if (ClaimProbe(false))
					known = Probe();

				backend = known?.Backend ?? Backend.None;
				operations = known?.Operations ?? Operation.None;
				return known != null;
			}

			internal static bool Offers(Operation needed)
			{
				if (Volatile.Read(ref current) is { } known && (known.Operations & needed) == needed)
					return true;

				return ClaimProbe(true) && Probe() is { } learned && (learned.Operations & needed) == needed;
			}

			// Reserve the next probe time, backing off when an operation is missing.
			private static bool ClaimProbe(bool backOff)
			{
				lock (sync)
				{
					var now = Environment.TickCount64;

					if (now < probeAt)
						return false;

					probeAt = now + (backOff ? recheckMs : CapabilityCacheMs);

					if (backOff)
						recheckMs = Math.Min(recheckMs * 2, CapabilityRecheckMaximumMs);

					return true;
				}
			}

			// Connecting is what learns the answer, through Learn.
			private static Snapshot Probe()
			{
				try
				{
					using var probe = DesktopConnection.Connect(ConnectionRole.Rpc, ProbeTimeoutMs);
				}
				catch
				{
					Forget();
				}

				return Volatile.Read(ref current);
			}
		}

		private sealed class DesktopRpcSession
		{
			private readonly object sync = new();
			private readonly object lifecycle = new();
			private DesktopConnection connection;
			private int generation;
			private ulong leaseId;
			internal ulong LeaseId { get { lock (lifecycle) return leaseId; } }

			internal bool TryUse(Operation operation, Func<DesktopConnection, CallResult> request,
				out NativeClientStatus status, NativeClientStatus? suppressedFailure = null)
			{
				status = NativeClientStatus.Unavailable;
				DesktopConnection current = null;
				try
				{
					var scope = ScopeFor(operation);
					if (scope != LinuxPermissionScope.None && RequestAuthorization(scope, false).Status != PermissionStatus.Granted)
					{
						status = NativeClientStatus.Denied;
						return false;
					}
					var lease = GetLease();
					if (scope == LinuxPermissionScope.WindowControl && lease.HasWindowState && !lease.Windows.WaitReady())
					{
						status = NativeClientStatus.Timeout;
						return false;
					}
					var expectedEpoch = lease.Windows.Epoch;
					ulong sequence;
					CallResult result;
					lock (sync)
					{
						int version;
						lock (lifecycle)
						{
							if (!ReferenceEquals(Volatile.Read(ref authorizationLease), lease) || !lease.IsOpen)
								throw new ObjectDisposedException("keysharp-desktop session");
							if (leaseId != lease.Id) { generation++; leaseId = lease.Id; }
							current = connection;
							version = generation;
						}
						if (current?.IsOpen != true || current.LeaseId != lease.Id)
						{
							Retire(current);
							current = DesktopConnection.Connect(ConnectionRole.Rpc, RequestTimeoutMs, leaseId: lease.Id);
							lock (lifecycle)
							{
								if (version != generation || !lease.IsOpen)
									throw new ObjectDisposedException("keysharp-desktop session");
								connection = current;
								Capabilities.Learn(current.Backend, current.AvailableOperations);
							}
						}
						if ((current.AvailableOperations & operation) != operation && !Capabilities.Offers(operation))
						{
							status = NativeClientStatus.Unsupported;
							return false;
						}
						(result, sequence) = current.Owner.Invoke(() => (request(current), current.Sequence));
						if (result.ShouldReconnect || result.Status == NativeClientStatus.Revoked)
						{
							Retire(current);
							if (result.ShouldReconnect) Capabilities.Forget();
						}
					}
					status = result.Status;
					if (!lease.IsOpen) { status = NativeClientStatus.Revoked; return false; }
					if (!result.IsSuccess)
					{
						if (status != suppressedFailure) DebugLine(result.Message);
						return false;
					}
					if (scope == LinuxPermissionScope.WindowControl && sequence != 0 && lease.HasWindowState
						&& !lease.Windows.WaitUntil(expectedEpoch, sequence))
					{
						lease.Resynchronize(1);
						status = NativeClientStatus.Timeout;
						return false;
					}
					return true;
				}
				catch (Exception exception)
				{
					DebugLine($"keysharp-desktop requires client ABI 1.0 / protocol 3: {exception.Message}");
					Retire(current);
					Capabilities.Forget();
					return false;
				}
			}

			internal bool TryOpen() => TryUse(Operation.None, _ => new CallResult(NativeClientStatus.Ok, 0, 0, "", "open"), out _);

			internal void Dispose(ulong retiredLease)
			{
				DesktopConnection retired;
				lock (lifecycle)
				{
					if (leaseId != retiredLease) return;
					generation++;
					retired = connection;
					connection = null;
					leaseId = 0;
				}
				retired?.Dispose();
			}

			private void Retire(DesktopConnection retired)
			{
				lock (lifecycle)
					if (ReferenceEquals(connection, retired)) connection = null;
				retired?.Dispose();
			}
		}

		private sealed partial class DesktopConnection : IDisposable
		{
			private IntPtr handle;
			internal readonly LinuxConnectionOwner Owner;
			internal Action<StateMessage> StateReceived;
			internal Action<Exception> StreamFailed;
			internal Backend Backend { get; private set; }
			internal Operation AvailableOperations { get; private set; }
			internal ulong LeaseId { get; private set; }
			internal ulong Sequence => Native.ksd_connection_sequence(handle);
			internal bool IsOpen => handle != IntPtr.Zero && Owner.IsRunning;

			private DesktopConnection()
			{
				Owner = new LinuxConnectionOwner("keysharp-desktop connection",
					() => handle == IntPtr.Zero ? -1 : Native.ksd_connection_fd(handle), DrainState,
					() =>
					{
						if (handle != IntPtr.Zero) Native.ksd_disconnect(handle);
						handle = IntPtr.Zero;
						StreamFailed?.Invoke(new IOException("keysharp-desktop connection ended."));
					});
				Owner.Start();
			}

			internal static DesktopConnection Connect(ConnectionRole role, int timeoutMs, ulong leaseId = 0)
			{
				var connection = new DesktopConnection();
				try
				{
					connection.Owner.Invoke(() =>
					{
						Native.ksd_connect_options_init(out var options);
						Native.ksd_service_info_init(out var info);
						var error = new NativeError { StructSize = NativeErrorStructSize };
						options.Role = (uint)role;
						options.AuthorizationMode = (uint)AuthorizationMode.Check;
						options.TimeoutMs = checked((uint)timeoutMs);
						options.LeaseId = leaseId;
						var result = Result((NativeClientStatus)Native.ksd_connect(ref options,
							ref connection.handle, ref info, ref error), in error, "connect");
						if (!result.IsSuccess) throw result.Exception;
						if (info.ClientAbiMajor != 1)
							throw new InvalidDataException($"libkeysharp-desktop ABI {info.ClientAbiMajor}.{info.ClientAbiMinor} is incompatible; Keysharp requires client ABI 1.0 / protocol 3.");
						connection.Backend = Enum.IsDefined((Backend)info.Backend) ? (Backend)info.Backend : Backend.Generic;
						connection.AvailableOperations = (Operation)info.AvailableOperations;
						connection.LeaseId = info.LeaseId;
						return true;
					});
					return connection;
				}
				catch { connection.Dispose(); throw; }
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
				try
				{
					return Owner.Invoke(() =>
					{
						ThrowIfClosed();
						var error = new NativeError { StructSize = NativeErrorStructSize };
						var result = Result((NativeClientStatus)call(handle, ref error), in error, operation);
						if (result.ShouldReconnect) Dispose();
						return result;
					});
				}
				catch (TimeoutException) { Dispose(); throw; }
			}

			private void ThrowIfClosed()
			{
				if (!IsOpen)
					throw new ObjectDisposedException(nameof(DesktopConnection));
			}

			public void Dispose() => Owner.Dispose();
		}

		private delegate uint NativeCall(IntPtr connection, ref NativeError error);
		private delegate uint NativeStringCall(IntPtr connection,
			ref NativeString value, ref NativeError error);

		private static CallResult Result(NativeClientStatus status,
			in NativeError error, string operation)
			=> new(status, error.Detail, error.SystemError,
				error.GetMessage(), operation);

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
			internal ulong LeaseId;
			private fixed ulong reserved[3];
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
			internal ulong LeaseId;
			private fixed ulong reserved[3];
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

		private static partial class Native
		{
			private const string Library = "libkeysharp-desktop.so.1";

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
			internal static extern void ksd_bytes_clear(ref NativeBytes value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_clear(ref NativeString value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_string_list_clear(ref NativeStringList value);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_capture_clear(ref NativeCapture capture);



			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_connect(ref NativeConnectOptions options,
				ref IntPtr connection, ref NativeServiceInfo info, ref NativeError error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksd_disconnect(IntPtr connection);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksd_authorize(IntPtr connection, uint mode,
				uint requestedScopes, out uint grantedScopes, ref NativeError error);


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




		}
	}
}
#endif
