#if LINUX
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using Eto.Drawing;
using Keysharp.Internals.Window.Linux.Wayland;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Linux
{
	internal enum DesktopCaptureStatus
	{
		Unavailable,
		Failed,
		DeniedOrStopped,
		Captured
	}

	/// <summary>Typed client for <c>libkeysharp-desktop.so.1</c>.</summary>
	internal sealed unsafe partial class DesktopClient : IDisposable
	{
		private const int RequestTimeoutMs = 30_000;
		private const int AuthorizationTimeoutMs = 125_000;
		private const int ProbeTimeoutMs = 2_000;
		internal LinuxPermissions Permissions { get; set; }

		internal static DesktopClient Current => Script.TheScript.LinuxServices.Desktop;
		private readonly CapabilityCache capabilities;

		internal DesktopClient()
		{
			capabilities = new(this);
			fastSession = new(this);
			slowSession = new(this);
		}

		private const int CapabilityCacheMs = 1_000;
		private const int CapabilityRecheckMaximumMs = 60_000;
		private const uint NativeErrorStructSize = 304;

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

		private readonly DesktopRpcSession fastSession;
		private readonly DesktopRpcSession slowSession;

		private DesktopRpcSession SessionFor(Operation operation)
			=> (operation & (Operation.CaptureArea | Operation.CaptureWindow | Operation.CaptureDesktop)) != 0
				? slowSession : fastSession;

		private bool Call(Operation operation, Func<DesktopConnection, CallResult> request)
			=> SessionFor(operation).TryUse(operation, request, out _);

		private bool Call(Operation operation, Func<DesktopConnection, CallResult> request,
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

		internal Bitmap CaptureWindow(string handle,
			bool includeDecoration)
		{
			if (string.IsNullOrWhiteSpace(handle))
				return null;

			Bitmap bitmap = null;
			return Call(Operation.CaptureWindow,
				connection => connection.CaptureWindow(handle, includeDecoration,
					out bitmap)) ? bitmap : null;
		}

		internal Bitmap CaptureWindow(ulong handle,
			bool includeDecoration = false)
			=> CaptureWindow(Invariant(handle), includeDecoration);

		internal DesktopCaptureStatus CaptureWithStatus(int x, int y,
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

		internal DesktopCaptureStatus CaptureDesktopWithStatus(out Bitmap bitmap)
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

		internal bool ProbeProvider()
			=> capabilities.TryGet(out _, out _);

		internal bool TryProbeBackend(out Backend backend)
			=> capabilities.TryGet(out backend, out _);

		/// <summary>Connects and authorizes the session for <paramref name="scope"/> ahead of its first request.</summary>
		internal bool OpenSession(LinuxPermissionScope scope)
			=> Permissions.RequestDesktop(scope, false).Status == PermissionStatus.Granted
				&& (scope == LinuxPermissionScope.ScreenCapture ? slowSession : fastSession).TryOpen();

		internal bool ProviderSupportsAbsolutePointer()
			=> ProviderSupports(Operation.MouseMoveAbsolute);

		internal bool ProviderSupportsWindowList()
			=> ProviderSupports(Operation.WindowList);

		internal bool ProviderSupportsWindowWatch()
			=> ProviderSupports(Operation.WindowWatch);

		internal bool ProviderSupportsTransparency()
			=> ProviderSupports(Operation.WindowSetOpacity);

		internal bool ProviderSupportsWindowKill()
			=> ProviderSupports(Operation.WindowKill);

		internal bool ProviderSupportsWindowMove()
			=> ProviderSupports(Operation.WindowMoveResize);

		internal bool TryProbeWindowSupport(out bool capture, out bool visibility)
		{
			capture = visibility = false;

			if (!capabilities.TryGet(out _, out var operations))
				return false;

			capture = (operations & Operation.CaptureWindow) != 0;
			visibility = (operations & Operation.WindowSetVisible) != 0;
			return true;
		}

		internal bool ProviderSupportsClipboard()
			=> ProviderSupports(
				Operation.ClipboardMimetypes | Operation.ClipboardContent
				| Operation.ClipboardText | Operation.ClipboardWatch
				| Operation.ClipboardSetContent);

		private bool ProviderSupports(Operation operations)
			=> capabilities.Offers(operations);

		internal bool QueryCursorPosition(out int x, out int y)
		{
			var point = default(Point);
			var result = Call(Operation.CursorPosition,
				connection => connection.CursorPosition(out point));
			x = point.X;
			y = point.Y;
			return result;
		}

		internal bool QueryWorkArea(out Rectangle area)
		{
			area = Rectangle.Empty;
			var value = Rectangle.Empty;
			var result = Call(Operation.WorkArea,
				connection => connection.WorkArea(out value));

			if (result)
				area = value;

			return result;
		}

		internal byte[] QueryWindowList(bool includeHidden)
		{
			byte[] value = null;
			return Call(Operation.WindowList,
				connection => connection.WindowList(includeHidden, out value)) ? value : null;
		}

		internal bool FocusWindow(ulong handle)
			=> Call(Operation.WindowFocus, connection => connection.FocusWindow(handle));

		internal bool RaiseWindow(ulong handle)
			=> Call(Operation.WindowRaise, connection => connection.RaiseWindow(handle));

		internal bool LowerWindow(ulong handle)
			=> Call(Operation.WindowLower, connection => connection.LowerWindow(handle));

		internal bool CloseWindow(ulong handle)
			=> Call(Operation.WindowClose, connection => connection.CloseWindow(handle));

		internal bool KillWindow(ulong handle)
			=> Call(Operation.WindowKill, connection => connection.KillWindow(handle));

		internal bool MoveResizeWindow(ulong handle,
			int x, int y, int width, int height)
			=> width >= 0 && height >= 0
				&& Call(Operation.WindowMoveResize,
					connection => connection.MoveResize(handle, x, y,
						checked((uint)width), checked((uint)height)));

		internal bool SetWindowState(ulong handle, int state)
			=> Call(Operation.WindowSetState,
					connection => connection.SetWindowState(handle, (uint)state));

		internal bool SetWindowOpacity(ulong handle, int opacity)
			=> Call(Operation.WindowSetOpacity,
					connection => connection.SetWindowOpacity(handle, (uint)opacity));

		internal bool SetWindowAbove(ulong handle, bool above)
			=> Call(Operation.WindowSetAbove,
				connection => connection.SetWindowAbove(handle, above));

		internal bool SetWindowDecorated(ulong handle, bool decorated)
			=> Call(Operation.WindowSetDecorated,
				connection => connection.SetWindowDecorated(handle, decorated));

		internal bool SetWindowSkipTaskbar(ulong handle, bool skip)
			=> Call(Operation.WindowSetSkipTaskbar,
				connection => connection.SetWindowSkipTaskbar(handle, skip));

		internal bool ReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> ttlMs >= 0 && Call(Operation.WindowReserve,
					connection => connection.ReserveWindow(cookie, x, y, checked((uint)ttlMs)));

		internal string GetReservedWindow(ulong cookie)
		{
			ulong handle = 0;
			return Call(
				Operation.WindowGetReserved,
				connection => connection.GetReservedWindow(cookie, out handle))
				? Invariant(handle) : string.Empty;
		}

		internal string[] GetClipboardMimetypes()
		{
			string[] value = null;
			return Call(Operation.ClipboardMimetypes,
				connection => connection.ClipboardMimetypes(out value)) ? value : null;
		}

		internal byte[] GetClipboardContent(string mimetype)
		{
			if (string.IsNullOrEmpty(mimetype))
				return null;

			byte[] value = null;
			return Call(Operation.ClipboardContent,
				connection => connection.ClipboardContent(mimetype, out value)) ? value : null;
		}

		internal string GetClipboardText()
		{
			string value = null;
			return Call(Operation.ClipboardText,
				connection => connection.ClipboardText(out value)) ? value : null;
		}

		internal bool SetClipboardContent(string mimetype, byte[] bytes)
		{
			var data = bytes ?? System.Array.Empty<byte>();

			if (string.IsNullOrEmpty(mimetype))
				return false;

			return Call(Operation.ClipboardSetContent,
				connection => connection.SetClipboardContent(mimetype, data));
		}

		internal bool SetClipboardText(string text)
		{
			var value = text ?? string.Empty;
			return Call(Operation.ClipboardSetContent,
				connection => connection.SetClipboardText(value));
		}

		internal bool SendMouseMoveAbsolute(int x, int y)
			=> Call(Operation.MouseMoveAbsolute,
				connection => connection.MouseCoordinates(true, x, y));

		internal bool SendMouseMoveRelative(int dx, int dy)
			=> Call(Operation.MouseMoveRelative,
				connection => connection.MouseCoordinates(false, dx, dy));

		internal bool SendMouseButton(uint button, bool pressed)
			=> Call(Operation.MouseButton,
				connection => connection.MouseButton(button, pressed));

		internal bool SendMouseScroll(int delta, bool vertical)
			=> Call(Operation.MouseScroll,
				connection => connection.MouseScroll(delta, vertical));

		internal IDisposable WatchWindowEvents(Action<WaylandWindowEventKind, WaylandWindowInfo> handler,
			Action<Exception> onError = null)
			=> SubscribeState(1, message => DispatchWindowEvents(message.Kind, message.Window, message.PreviousWindow, handler), onError);

		internal static void DispatchWindowEvents(uint kind, WaylandWindowInfo window, WaylandWindowInfo previous,
			Action<WaylandWindowEventKind, WaylandWindowInfo> handler)
		{
			if (window == null || kind < 4 || kind > 11) return;
			if (kind == 5 && previous != null) window = previous;
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

		internal IDisposable WatchClipboardChanges(Action<string, string[]> handler,
			Action<Exception> onError = null)
			=> SubscribeState(8, message =>
			{
				if (message.Data.Length == 0) return;
				using var document = System.Text.Json.JsonDocument.Parse(message.Data);
				var root = document.RootElement;
				handler(DesktopWindowParser.Text(root, "text"), root.TryGetProperty("mimetypes", out var types)
					? types.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : []);
			}, onError);

		internal IDisposable SubscribeKeyboardState(Action<byte[]> handler, Action<Exception> onError = null)
			=> SubscribeState(2, message => handler(message.Data), onError);

		private readonly object authorizationSync = new();
		private object leaseCreation = new();
		private int authorizationGeneration;
		private AuthorizationLease authorizationLease;
		private bool clientsStopped;

		public void Dispose()
		{
			AuthorizationLease retired;
			ulong fastLease, slowLease;
			lock (authorizationSync)
			{
				clientsStopped = true;
				authorizationGeneration++;
				leaseCreation = new();
				retired = authorizationLease;
				authorizationLease = null;
				fastLease = fastSession.LeaseId;
				slowLease = slowSession.LeaseId;
				capabilities.Forget();
			}
			retired?.Dispose();
			fastSession.Dispose(fastLease);
			slowSession.Dispose(slowLease);
		}

		private AuthorizationLease GetLease()
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
				var created = new AuthorizationLease(this);
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

		internal bool HasGrant(LinuxPermissionScope scopes)
		{
			var lease = Volatile.Read(ref authorizationLease);
			return scopes == LinuxPermissionScope.None || lease?.IsOpen == true && (lease.Scopes & scopes) == scopes;
		}

		internal PermissionResult Authorize(LinuxPermissionScope scopes, bool prompt)
		{
			if (scopes == LinuxPermissionScope.None) return new(PermissionStatus.Granted);
			if ((scopes & ~DesktopAuthorizationScopes) != 0)
				return new(PermissionStatus.Unsupported, "Invalid keysharp-desktop permission scope.");
			try
			{
				var lease = GetLease();
				var result = lease.Authorize(scopes, prompt ? AuthorizationMode.Request : AuthorizationMode.Check);
				lock (authorizationSync)
				{
					if (!ReferenceEquals(authorizationLease, lease) || !lease.IsOpen)
						return new(PermissionStatus.Unsupported, "The desktop lease has stopped.");
				}
				return result.IsSuccess ? new(PermissionStatus.Granted) : PermissionFailure(result);
			}
			catch (Exception exception) when (exception is IOException or TimeoutException or ObjectDisposedException
				or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				return new(PermissionStatus.Unsupported,
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
		private sealed class CapabilityCache(DesktopClient service)
		{
			private sealed record Snapshot(Backend Backend, Operation Operations);

			private readonly object sync = new();
			private Snapshot current;
			private long probeAt;
			private int recheckMs = CapabilityCacheMs;

			internal void Learn(Backend backend, Operation operations)
			{
				lock (sync)
				{
					if ((operations & ~(current?.Operations ?? Operation.None)) != 0)
						recheckMs = CapabilityCacheMs;

					probeAt = Math.Max(probeAt, Environment.TickCount64 + CapabilityCacheMs);
					Volatile.Write(ref current, new Snapshot(backend, operations));
				}
			}

			internal void Forget()
			{
				lock (sync)
					Volatile.Write(ref current, null);
			}

			internal bool TryGet(out Backend backend, out Operation operations)
			{
				var known = Volatile.Read(ref current);

				if (ClaimProbe(false))
					known = Probe();

				backend = known?.Backend ?? Backend.None;
				operations = known?.Operations ?? Operation.None;
				return known != null;
			}

			internal bool Offers(Operation needed)
			{
				if (Volatile.Read(ref current) is { } known && (known.Operations & needed) == needed)
					return true;

				return ClaimProbe(true) && Probe() is { } learned && (learned.Operations & needed) == needed;
			}

			// Reserve the next probe time, backing off when an operation is missing.
			private bool ClaimProbe(bool backOff)
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
			private Snapshot Probe()
			{
				try
				{
					var lease = service.GetLease();
					using var probe = DesktopConnection.Connect(ConnectionRole.Rpc, ProbeTimeoutMs, leaseId: lease.Id);
					Learn(probe.Backend, probe.AvailableOperations);
				}
				catch
				{
					Forget();
				}

				return Volatile.Read(ref current);
			}
		}

		private sealed class DesktopRpcSession(DesktopClient service)
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
					if (scope != LinuxPermissionScope.None && service.Permissions.RequestDesktop(scope, false).Status != PermissionStatus.Granted)
					{
						status = NativeClientStatus.Denied;
						return false;
					}
					var lease = service.GetLease();
					if (scope == LinuxPermissionScope.WindowControl && service.capabilities.Offers(Operation.WindowWatch))
						lease.State.EnsureDomains(1);
					if (scope == LinuxPermissionScope.WindowControl && lease.State.HasWindowState && !lease.State.Windows.WaitReady())
					{
						status = NativeClientStatus.Timeout;
						return false;
					}
					var expectedEpoch = lease.State.Windows.Epoch;
					ulong sequence;
					CallResult result;
					lock (sync)
					{
						int version;
						lock (lifecycle)
						{
							if (!ReferenceEquals(Volatile.Read(ref service.authorizationLease), lease) || !lease.IsOpen)
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
								service.capabilities.Learn(current.Backend, current.AvailableOperations);
							}
						}
						if ((current.AvailableOperations & operation) != operation && !service.capabilities.Offers(operation))
						{
							status = NativeClientStatus.Unsupported;
							return false;
						}
					}
					(result, sequence) = current.Owner.Invoke(() => (request(current), current.Sequence));
					if (result.ShouldReconnect || result.Status == NativeClientStatus.Revoked)
					{
						Retire(current);
						if (result.ShouldReconnect) service.capabilities.Forget();
					}
					status = result.Status;
					if (!lease.IsOpen) { status = NativeClientStatus.Revoked; return false; }
					if (!result.IsSuccess)
					{
						if (status != suppressedFailure) DebugLine(result.Message);
						return false;
					}
					if (scope == LinuxPermissionScope.WindowControl && sequence != 0 && lease.State.HasWindowState
						&& !lease.State.Windows.WaitUntil(expectedEpoch, sequence))
					{
						lease.State.Resynchronize(1);
						status = NativeClientStatus.Timeout;
						return false;
					}
					return true;
				}
				catch (Exception exception)
				{
					DebugLine($"keysharp-desktop requires client ABI 1.0 / protocol 3: {exception.Message}");
					Retire(current);
					service.capabilities.Forget();
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


	}
}
#endif
