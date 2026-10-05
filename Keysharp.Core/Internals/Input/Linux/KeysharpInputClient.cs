#if LINUX
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Os;

namespace Keysharp.Internals.Input.Linux
{
	/// <summary>Typed client for <c>libkeysharp-input.so.1</c>.</summary>
	internal sealed unsafe class KeysharpInputClient : IDisposable
	{
		internal const string SocketEnvironmentVariable = "KEYSHARP_INPUT_SOCKET";
		internal const string DefaultSocketPathValue = "/run/keysharp-input/keysharp-input.sock";
		internal const int MaxInputsPerRequest = 1024;
		internal const int KeyStateBitmapBytes = 96;
		internal const int DeviceNameCapacity = 256;
		internal const int DeviceAxisCapacity = 64;
		internal const int DeviceButtonCapacity = 128;
		internal const int DefaultRequestTimeoutMs = 5000;
		internal const int AuthorizationTimeoutMs = 125_000;
		private const int NestedHookLimit = 16;
		private const LinuxPermissionScope ManagedScopes =
			LinuxPermissionScope.InputMonitoring | LinuxPermissionScope.InputControl;
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);
		private static readonly Native.NestedHookHandler NestedHookThunk = DispatchNestedHook;
		private static readonly Native.DeviceVisitor GamepadVisitorThunk = CollectGamepad;
		private static readonly uint NativeServiceInfoStructSize =
			checked((uint)sizeof(NativeServiceInfo));

		internal enum ConnectionRole : uint
		{
			Rpc = 0,
			CallbackStream = 2,
			Lease = 3,
		}

		private enum AuthorizationMode : uint
		{
			Check = 0,
			Request = 1,
		}

		[Flags]
		internal enum Operations : ulong
		{
			None = 0,
			HookKeyboard = 1UL << 0,
			HookMouse = 1UL << 1,
			SynthesizeKeyboard = 1UL << 2,
			SynthesizeMouse = 1UL << 3,
			BlockInput = 1UL << 4,
			QueryIndicators = 1UL << 5,
			QueryPointerPosition = 1UL << 6,
			QueryKeyState = 1UL << 7,
			QueryPointerButtons = 1UL << 8,
			QueryIdleTime = 1UL << 9,
			QueryModifiers = 1UL << 10,
			ObserveKeyboard = 1UL << 11,
			ObserveMouse = 1UL << 12,
			QueryDevices = 1UL << 13,
			QueryGamepads = 1UL << 14,
			All = (1UL << 15) - 1,
		}

		internal enum HookType : uint
		{
			KeyboardLowLevel = 13,
			MouseLowLevel = 14,
		}

		internal enum HookDecision : uint
		{
			Pass = 0,
			Block = 1,
			Modify = 2,
		}

		internal enum MessageKind : uint
		{
			KeyDown = 0x0100,
			KeyUp = 0x0101,
			SystemKeyDown = 0x0104,
			SystemKeyUp = 0x0105,
			MouseMove = 0x0200,
			LeftButtonDown = 0x0201,
			LeftButtonUp = 0x0202,
			RightButtonDown = 0x0204,
			RightButtonUp = 0x0205,
			MiddleButtonDown = 0x0207,
			MiddleButtonUp = 0x0208,
			MouseWheel = 0x020A,
			XButtonDown = 0x020B,
			XButtonUp = 0x020C,
			MouseHorizontalWheel = 0x020E,
		}

		[Flags]
		internal enum HookFlags : uint
		{
			MouseInjected = 0x01,
			KeyboardInjected = 0x10,
			KeyboardUp = 0x80,
		}

		[Flags]
		internal enum BlockInputMask : uint
		{
			None = 0,
			Keyboard = 1,
			Mouse = 2,
		}

		internal enum InputType : uint
		{
			Mouse = 0,
			Keyboard = 1,
		}

		[Flags]
		internal enum KeyEventFlags : uint
		{
			ExtendedKey = 0x0001,
			KeyUp = 0x0002,
			Unicode = 0x0004,
			ScanCode = 0x0008,
		}

		[Flags]
		internal enum MouseEventFlags : uint
		{
			Move = 0x0001,
			LeftDown = 0x0002,
			LeftUp = 0x0004,
			RightDown = 0x0008,
			RightUp = 0x0010,
			MiddleDown = 0x0020,
			MiddleUp = 0x0040,
			XDown = 0x0080,
			XUp = 0x0100,
			Wheel = 0x0800,
			HWheel = 0x1000,
			MoveNoCoalesce = 0x2000,
			VirtualDesk = 0x4000,
			Absolute = 0x8000,
		}

		[Flags]
		internal enum SynthFlags : uint
		{
			None = 0,
			BypassHook = 1,
		}

		internal readonly record struct KeyboardInput(ushort Vk, ushort Scan,
			KeyEventFlags Flags, uint Time = 0, ulong ExtraInfo = 0);
		internal readonly record struct MouseInput(int Dx, int Dy, uint MouseData,
			MouseEventFlags Flags, uint Time = 0, ulong ExtraInfo = 0);

		internal readonly record struct Input(InputType Type, KeyboardInput Keyboard, MouseInput Mouse)
		{
			internal static Input Key(ushort vk, ushort scan = 0,
				KeyEventFlags flags = 0, uint time = 0, ulong extraInfo = 0)
				=> new(InputType.Keyboard, new(vk, scan, flags, time, extraInfo), default);

			internal static Input MouseEvent(int dx, int dy, uint mouseData,
				MouseEventFlags flags, uint time = 0, ulong extraInfo = 0)
				=> new(InputType.Mouse, default, new(dx, dy, mouseData, flags, time, extraInfo));
		}

		internal readonly record struct KeyboardHookEvent(uint Message, uint VkCode,
			uint ScanCode, uint Flags, ulong TimeMs, ulong ExtraInfo, uint DeviceId);
		internal readonly record struct MouseHookEvent(uint Message, int X, int Y,
			uint MouseData, uint Flags, ulong TimeMs, ulong ExtraInfo, uint DeviceId,
			int DeltaX, int DeltaY);
		internal readonly record struct HookEvent(ulong EventId, HookType HookType,
			KeyboardHookEvent Keyboard, MouseHookEvent Mouse);
		internal readonly record struct HookQuarantine(HookType HookType, uint Reason,
			ulong EventId, uint Generation, uint StrikeCount, uint RetryAfterMs);
		internal readonly record struct PointerPosition(int X, int Y, int XMin,
			int XMax, int YMin, int YMax);
		[System.Runtime.CompilerServices.InlineArray(KeyStateBitmapBytes)]
		internal struct KeyStateBitmap
		{
			private byte element;
		}

		internal readonly record struct KeyStateSnapshot(uint ModifiersLR, bool CapsLock,
			bool NumLock, bool ScrollLock, KeyStateBitmap LogicalKeys, KeyStateBitmap PhysicalKeys);
		internal readonly record struct PointerButtons(uint LogicalButtons, uint PhysicalButtons);
		internal readonly record struct GamepadAxis(uint Code, int Minimum, int Maximum);
		internal readonly record struct GamepadInfo(uint DeviceId, string Name,
			int ButtonCount, GamepadAxis[] Axes);
		/// <summary>Live gamepad reading. Buttons holds the first 32 buttons in the device's
		/// button order, which is as many as a script can address.</summary>
		internal readonly record struct GamepadState(uint DeviceId, ulong Generation,
			int ButtonCount, uint Buttons, int[] AxisValues);

		internal sealed class KeyboardMirror
		{
			private readonly object gate = new();
			private KeyStateSnapshot? snapshot;
			private ulong sequence, acknowledgedSequence;
			private uint physicalModifiers, acknowledgedModifiers;
			private bool awaitingSnapshot = true;

			internal bool Publish(ulong next, KeyStateSnapshot state, uint physical, bool initial)
			{
				lock (gate)
				{
					if (!initial && awaitingSnapshot) return true;
					if (!initial && next != sequence + 1)
					{
						snapshot = null;
						awaitingSnapshot = true;
						Monitor.PulseAll(gate);
						return false;
					}
					snapshot = state;
					sequence = next;
					physicalModifiers = physical;
					awaitingSnapshot = false;
					Monitor.PulseAll(gate);
					return true;
				}
			}

			internal void Acknowledge(ulong atSequence, uint logical)
			{
				lock (gate)
					if (atSequence > acknowledgedSequence)
					{
						acknowledgedSequence = atSequence;
						acknowledgedModifiers = logical;
					}
			}

			internal bool TryRead(out KeyStateSnapshot state, out uint physical)
			{
				lock (gate)
				{
					state = snapshot.GetValueOrDefault();
					if (acknowledgedSequence > sequence)
						state = state with { ModifiersLR = acknowledgedModifiers };
					physical = physicalModifiers;
					return snapshot.HasValue;
				}
			}

			internal void Invalidate()
			{
				lock (gate) { snapshot = null; awaitingSnapshot = true; Monitor.PulseAll(gate); }
			}

			internal bool WaitForAcknowledgement(int timeoutMs)
			{
				var deadline = Environment.TickCount64 + timeoutMs;
				lock (gate)
				{
					while (snapshot.HasValue && sequence < acknowledgedSequence)
					{
						var remaining = deadline - Environment.TickCount64;
						if (remaining <= 0 || !Monitor.Wait(gate, (int)remaining)) return false;
					}
					return snapshot.HasValue;
				}
			}
		}

		private readonly ILinuxConnectionDispatcher owner;
		private readonly KeysharpInputClient lease;
		private readonly KeyboardMirror keyboardState = new();
		private Action<KeysharpInputClient, HookEvent> hookEventHandler;
		private Action<Exception> streamErrorHandler;
		private Action<KeysharpInputClient> leaseStateHandler;
		private readonly ConnectionRole connectionRole;
		private readonly nint[] nestedReplies = new nint[NestedHookLimit];
		private readonly ulong[] nestedEventIds = new ulong[NestedHookLimit];
		private readonly nint[] nestedReplacementBuffers = new nint[NestedHookLimit];
		private nint connection;
		private int grantedScopes;
		private NativeHookEvent currentHookEvent;
		private ulong currentHookEventId;
		private int nestedDepth;
		private volatile bool disposePending;
		private GCHandle callbackHandle;
		private Action<KeysharpInputClient, HookEvent> nestedHookEventHandler;
		private Action<HookQuarantine> hookQuarantineHandler;

		private KeysharpInputClient(nint connection, ConnectionRole role,
			LinuxPermissionScope grantedScopes, Operations availableOperations,
			ILinuxConnectionDispatcher owner, KeysharpInputClient lease, ulong leaseId)
		{
			this.connection = connection;
			this.owner = owner;
			this.lease = lease;
			LeaseId = leaseId;
			connectionRole = role;
			GrantedScopes = grantedScopes;
			AvailableOperations = availableOperations;
		}

		internal LinuxPermissionScope GrantedScopes
		{
			get => disposePending ? LinuxPermissionScope.None
				: lease?.GrantedScopes ?? (LinuxPermissionScope)(uint)Volatile.Read(ref grantedScopes);
			private set => Volatile.Write(ref grantedScopes, unchecked((int)(uint)value));
		}
		internal Operations AvailableOperations { get; }
		internal ulong LeaseId { get; }
		internal bool ReaderRunning => owner.IsRunning;
		internal bool IsConnected => !disposePending && owner.IsRunning && Volatile.Read(ref connection) != 0
			&& (lease == null || lease.IsConnected);

		internal static string DefaultSocketPath
		{
			get
			{
				var configured = Environment.GetEnvironmentVariable(SocketEnvironmentVariable);
				return string.IsNullOrWhiteSpace(configured) ? DefaultSocketPathValue : configured;
			}
		}

		internal static KeysharpInputClient Connect(Operations requested = Operations.None,
			string socketPath = null, int requestTimeoutMs = DefaultRequestTimeoutMs,
			ConnectionRole role = ConnectionRole.Lease, KeysharpInputClient lease = null)
		{
			KeysharpInputClient client = null;
			ILinuxConnectionDispatcher owner = role == ConnectionRole.Rpc
				? new LinuxRpcDispatcher(() => client?.Cleanup())
				: new LinuxConnectionOwner($"keysharp-input {role}",
				() => client == null || client.connection == 0
					|| role == ConnectionRole.CallbackStream && client.hookEventHandler == null
					? -1 : Native.ksi_connection_fd(client.connection),
				() => client?.Drain() ?? false, () => client?.Cleanup());
			owner.Start();
			try
			{
				return owner.Invoke(() =>
				{
					client = ConnectCore(owner, requested, socketPath, requestTimeoutMs, role, lease);
					if (role == ConnectionRole.Lease) client.SubscribeKeyState();
					return client;
				});
			}
			catch { owner.Dispose(); throw; }
		}

		private static KeysharpInputClient ConnectCore(ILinuxConnectionDispatcher owner, Operations requested,
			string socketPath, int requestTimeoutMs, ConnectionRole role, KeysharpInputClient lease)
		{
			if ((requested & ~Operations.All) != 0)
				throw new ArgumentOutOfRangeException(nameof(requested));

			Native.ksi_connect_options_init(out var options);
			Native.ksi_service_info_init(out var info);
			Native.ksi_error_init(out var error);
			options.Role = (uint)role;
			options.AuthorizationMode = (uint)AuthorizationMode.Check;
			options.RequestedScopes = (uint)RequiredScopes(requested);
			options.TimeoutMs = checked((uint)requestTimeoutMs);
			options.LeaseId = lease?.LeaseId ?? 0;
			var socketPathMemory = socketPath == null ? 0 : Marshal.StringToCoTaskMemUTF8(socketPath);
			options.SocketPath = socketPathMemory;

			try
			{
				var status = (NativeClientStatus)Native.ksi_connect(ref options,
					out var connection, ref info, ref error);
				ThrowConnectIfFailed(status, error);

				try
				{
					if (info.StructSize != NativeServiceInfoStructSize
						|| info.ClientAbiMajor != 1)
						throw new InvalidDataException(
							$"Keysharp requires keysharp-input client ABI 1.0 (libkeysharp-input.so.1); found {info.ClientAbiMajor}.{info.ClientAbiMinor}.");
					if ((info.GrantedScopes & ~(uint)ManagedScopes) != 0)
						throw new InvalidDataException("keysharp-input returned unknown capability bits.");

					var client = new KeysharpInputClient(connection, role,
						(LinuxPermissionScope)info.GrantedScopes,
						(Operations)info.AvailableOperations & Operations.All, owner, lease, info.LeaseId);
					connection = 0;

					if ((client.AvailableOperations & requested) != requested)
					{
						client.Cleanup();
						throw new NativeClientException("keysharp-input", "connect",
							NativeClientStatus.Unsupported, 0, 0,
							"keysharp-input does not provide the requested operations.");
					}
					if (!client.HasOperations(requested))
					{
						client.Cleanup();
						throw new NativeClientException("keysharp-input", "connect",
							NativeClientStatus.Denied, 0, 0,
							"keysharp-input has not granted the requested permission.");
					}

					return client;
				}
				finally
				{
					if (connection != 0)
						Native.ksi_disconnect(connection);
				}
			}
			finally
			{
				if (socketPathMemory != 0)
					Marshal.FreeCoTaskMem(socketPathMemory);
			}
		}

		internal void SetNestedHookEventHandler(Action<KeysharpInputClient, HookEvent> handler)
		{
			if (!owner.IsOwnerThread) { owner.Invoke(() => { SetNestedHookEventHandler(handler); return true; }); return; }
			ThrowIfDisposed();
			nestedHookEventHandler = handler;
			var allocated = false;
			if (handler != null && !callbackHandle.IsAllocated)
			{
				callbackHandle = GCHandle.Alloc(this);
				allocated = true;
			}

			try
			{
				Native.ksi_error_init(out var error);
				var status = (NativeClientStatus)Native.ksi_set_nested_hook_handler(
					connection, handler == null ? null : NestedHookThunk,
					handler == null ? 0 : GCHandle.ToIntPtr(callbackHandle), ref error);
				ThrowIfFailed(status, "configure nested hook callback", error);
			}
			catch
			{
				if (allocated)
					callbackHandle.Free();
				throw;
			}

			if (handler == null && callbackHandle.IsAllocated)
				callbackHandle.Free();
		}

		internal void SetHookQuarantineHandler(Action<HookQuarantine> handler)
			=> hookQuarantineHandler = handler;
		internal void RefreshLeaseState()
		{
			if (lease != null) { lease.RefreshLeaseState(); return; }
			owner.Invoke(() => { while (Drain()) { } return true; });
		}

		internal bool HasOperations(Operations operations)
		{
			var requiredScopes = RequiredScopes(operations);
			return IsConnected && (AvailableOperations & operations) == operations
				&& (GrantedScopes & requiredScopes) == requiredScopes;
		}

		internal static LinuxPermissionScope RequiredScopes(Operations operations)
		{
			var scopes = LinuxPermissionScope.None;
			if ((operations & (Operations.HookKeyboard | Operations.HookMouse
				| Operations.QueryKeyState | Operations.QueryPointerButtons
				| Operations.ObserveKeyboard | Operations.ObserveMouse | Operations.QueryDevices)) != 0)
				scopes |= LinuxPermissionScope.InputMonitoring;
			if ((operations & (Operations.SynthesizeKeyboard | Operations.SynthesizeMouse
				| Operations.BlockInput)) != 0)
				scopes |= LinuxPermissionScope.InputControl;
			return scopes;
		}

		internal BlockInputMask SetBlockInput(BlockInputMask mask)
		{
			if (!owner.IsOwnerThread) return owner.Invoke(() => SetBlockInput(mask));
			if ((mask & ~(BlockInputMask.Keyboard | BlockInputMask.Mouse)) != 0)
				throw new ArgumentOutOfRangeException(nameof(mask));
			if (mask != BlockInputMask.None)
				RequireOperations(Operations.BlockInput);

			ThrowIfDisposed();
			Native.ksi_error_init(out var error);
			var status = (NativeClientStatus)Native.ksi_set_block_input(connection,
				(uint)mask, out var effective, ref error);
			ThrowIfFailed(status, "set block input", error);
			return (BlockInputMask)effective;
		}

		internal Operations SubscribeHook(HookType hookType)
		{
			if (!owner.IsOwnerThread) return owner.Invoke(() => SubscribeHook(hookType));
			var operation = hookType switch
			{
				HookType.KeyboardLowLevel => Operations.HookKeyboard,
				HookType.MouseLowLevel => Operations.HookMouse,
				_ => throw new ArgumentOutOfRangeException(nameof(hookType)),
			};
			RequireOperations(operation);

			ThrowIfDisposed();
			Native.ksi_error_init(out var error);
			var status = (NativeClientStatus)Native.ksi_hook_subscribe(connection,
				(uint)hookType, out var activeOperations, ref error);
			ThrowIfFailed(status, "subscribe hook", error);
			return (Operations)activeOperations;
		}

		internal Operations UnsubscribeHook(HookType hookType)
		{
			if (!owner.IsOwnerThread) return owner.Invoke(() => UnsubscribeHook(hookType));
			if (hookType is not (HookType.KeyboardLowLevel or HookType.MouseLowLevel))
				throw new ArgumentOutOfRangeException(nameof(hookType));

			ThrowIfDisposed();
			Native.ksi_error_init(out var error);
			var status = (NativeClientStatus)Native.ksi_hook_unsubscribe(connection,
				(uint)hookType, out var activeOperations, ref error);
			ThrowIfFailed(status, "unsubscribe hook", error);
			return (Operations)activeOperations;
		}

		internal uint SendInput(IReadOnlyList<Input> inputs,
			SynthFlags flags = SynthFlags.None, ulong parentHookEventId = 0)
		{
			if (!owner.IsOwnerThread) return owner.Invoke(() => SendInput(inputs, flags, parentHookEventId));
			ArgumentNullException.ThrowIfNull(inputs);
			if (inputs.Count > MaxInputsPerRequest)
				throw new ArgumentOutOfRangeException(nameof(inputs));
			if ((flags & ~SynthFlags.BypassHook) != 0)
				throw new ArgumentOutOfRangeException(nameof(flags));
			if ((connectionRole == ConnectionRole.CallbackStream) != (parentHookEventId != 0))
				throw new InvalidOperationException(
					"Hook-channel synthesis requires the current hook event.");
			if (connectionRole == ConnectionRole.CallbackStream
				&& parentHookEventId != currentHookEventId
				&& (nestedDepth == 0 || parentHookEventId != nestedEventIds[nestedDepth - 1]))
				throw new InvalidOperationException("Synthesis does not match the current hook event.");
			if (inputs.Count == 0)
				return 0;

			var required = RequiredSynthesisOperations(inputs);
			NativeInput[] rented = null;
			Span<NativeInput> nativeInputs = inputs.Count <= 64
				? stackalloc NativeInput[inputs.Count]
				: (rented = ArrayPool<NativeInput>.Shared.Rent(inputs.Count)).AsSpan(0, inputs.Count);

			try
			{
				for (var index = 0; index < inputs.Count; index++)
				{
					nativeInputs[index] = ToNative(inputs[index]);
				}
				RequireOperations(required);

				fixed (NativeInput* pointer = nativeInputs)
				{
					ThrowIfDisposed();
					Native.ksi_error_init(out var error);
					var status = (NativeClientStatus)Native.ksi_synthesize(connection,
						pointer, (uint)inputs.Count, (uint)flags, out var modifiers, ref error);
					ThrowIfFailed(status, "synthesize input", error);
					(lease ?? this).keyboardState.Acknowledge(Native.ksi_connection_sequence(connection), modifiers);
					return modifiers;
				}
			}
			finally
			{
				if (rented != null)
					ArrayPool<NativeInput>.Shared.Return(rented);
			}
		}

		internal static Operations RequiredSynthesisOperations(IReadOnlyList<Input> inputs)
		{
			ArgumentNullException.ThrowIfNull(inputs);
			var required = Operations.None;

			for (var index = 0; index < inputs.Count; index++)
				required |= inputs[index].Type switch
				{
					InputType.Keyboard => Operations.SynthesizeKeyboard,
					InputType.Mouse => Operations.SynthesizeMouse,
					_ => throw new NotSupportedException($"Input type {inputs[index].Type} is not supported."),
				};

			return required;
		}

		internal bool TryQueryKeyState(uint deviceID, out KeyStateSnapshot state)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var success = TryQueryKeyState(deviceID, out var value); return (success, value); });
				state = result.value;
				return result.success;
			}
			RequireOperations(Operations.QueryKeyState);
			if (deviceID == 0)
				return TryGetKeyStateSnapshot(out state, out _);
			state = default;

			ThrowIfDisposed();
			Native.ksi_key_state_init(out var native);
			Native.ksi_error_init(out var error);
			var status = Native.ksi_get_device_key_state(connection, deviceID, ref native, ref error);

			if ((NativeClientStatus)status == NativeClientStatus.NotFound)
				return false;

			ThrowIfFailed((NativeClientStatus)status, "query key state", error);
			KeyStateBitmap logical = default, physical = default;
			new ReadOnlySpan<byte>(native.LogicalKeys, KeyStateBitmapBytes).CopyTo(logical);
			new ReadOnlySpan<byte>(native.PhysicalKeys, KeyStateBitmapBytes).CopyTo(physical);
			state = new(native.ModifiersLR, native.CapsLock != 0, native.NumLock != 0,
				native.ScrollLock != 0, logical, physical);
			return true;
		}

		internal bool TryGetPointerPosition(out PointerPosition position)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var success = TryGetPointerPosition(out var value); return (success, value); });
				position = result.value;
				return result.success;
			}
			RequireOperations(Operations.QueryPointerPosition);
			ThrowIfDisposed();
			Native.ksi_pointer_position_init(out var native);
			Native.ksi_error_init(out var error);
			ThrowIfFailed((NativeClientStatus)Native.ksi_get_pointer_position(
				connection, ref native, ref error), "query pointer position", error);
			position = new(native.X, native.Y, native.XMin, native.XMax,
				native.YMin, native.YMax);
			return native.Valid != 0;
		}

		internal bool TryGetPointerButtons(out PointerButtons buttons)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var success = TryGetPointerButtons(out var value); return (success, value); });
				buttons = result.value;
				return result.success;
			}
			RequireOperations(Operations.QueryPointerButtons);
			ThrowIfDisposed();
			Native.ksi_pointer_buttons_init(out var native);
			Native.ksi_error_init(out var error);
			ThrowIfFailed((NativeClientStatus)Native.ksi_get_pointer_buttons(
				connection, ref native, ref error), "query pointer buttons", error);
			buttons = new(native.LogicalButtons, native.PhysicalButtons);
			return native.Valid != 0;
		}

		internal bool TryGetIdleTime(out ulong milliseconds)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var success = TryGetIdleTime(out var value); return (success, value); });
				milliseconds = result.value;
				return result.success;
			}
			RequireOperations(Operations.QueryIdleTime);
			ThrowIfDisposed();
			Native.ksi_idle_time_init(out var native);
			Native.ksi_error_init(out var error);
			ThrowIfFailed((NativeClientStatus)Native.ksi_get_idle_time(
				connection, ref native, ref error), "query idle time", error);
			milliseconds = native.IdleTimeMs;
			return native.Valid != 0;
		}

		/// <summary>Enumerates connected gamepads, ordered so that a device's position is stable
		/// across restarts. Needs no grant, as with pointer position and idle time.</summary>
		internal List<GamepadInfo> ListGamepads(out ulong generation)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var values = ListGamepads(out var value); return (values, value); });
				generation = result.value;
				return result.values;
			}
			RequireOperations(Operations.QueryGamepads);
			var gamepads = new List<GamepadInfo>();
			generation = 0;

			ThrowIfDisposed();
			Native.ksi_error_init(out var error);
			var handle = GCHandle.Alloc(gamepads);

			try
			{
				ThrowIfFailed((NativeClientStatus)Native.ksi_gamepads_list(connection,
					GamepadVisitorThunk, GCHandle.ToIntPtr(handle), out generation, ref error),
					"list gamepads", error);
			}
			finally
			{
				handle.Free();
			}

			return gamepads;
		}

		/// <summary>Reads one gamepad's buttons and axes. Returns false when the device is gone or
		/// the device set changed since <paramref name="generation"/> was taken.</summary>
		internal bool TryGetGamepadState(uint deviceId, ulong generation, out GamepadState state)
		{
			if (!owner.IsOwnerThread)
			{
				var result = owner.Invoke(() => { var success = TryGetGamepadState(deviceId, generation, out var value); return (success, value); });
				state = result.value;
				return result.success;
			}
			RequireOperations(Operations.QueryGamepads);
			state = default;

			ThrowIfDisposed();
			Native.ksi_gamepad_state_init(out var native);
			Native.ksi_error_init(out var error);
			var status = (NativeClientStatus)Native.ksi_get_gamepad_state(connection,
				deviceId, generation, ref native, ref error);

			if (status is NativeClientStatus.NotFound or NativeClientStatus.Busy)
				return false;

			ThrowIfFailed(status, "query gamepad state", error);
			var axisCount = (int)Math.Min(native.AxisCount, DeviceAxisCapacity);
			var values = axisCount != 0 ? new int[axisCount] : [];
			var axes = (NativeGamepadAxisState*)native.Axes;

			for (var i = 0; i < axisCount; i++)
				values[i] = axes[i].Value;

			uint buttons = 0;

			for (var i = 0; i < 4; i++)
				buttons |= (uint)native.Buttons[i] << (i * 8);

			state = new(native.DeviceId, native.DeviceGeneration,
				(int)Math.Min(native.ButtonCount, DeviceButtonCapacity), buttons, values);
			return true;
		}

		private static bool CollectGamepad(NativeDeviceInfo* device, nint context)
		{
			if (GCHandle.FromIntPtr(context).Target is not List<GamepadInfo> gamepads)
				return false;

			var axisCount = (int)Math.Min(device->AxisCount, DeviceAxisCapacity);
			var axes = axisCount != 0 ? new GamepadAxis[axisCount] : [];
			var native = (NativeDeviceAxisInfo*)device->Axes;

			for (var i = 0; i < axisCount; i++)
				axes[i] = new(native[i].Code, native[i].Minimum, native[i].Maximum);

			gamepads.Add(new(device->DeviceId, device->GetName(),
				(int)Math.Min(device->ButtonCount, DeviceButtonCapacity), axes));
			return true;
		}

		internal void SetHookEventHandler(Action<KeysharpInputClient, HookEvent> handler,
			Action<Exception> onError = null)
			=> owner.Invoke(() => { hookEventHandler = handler; streamErrorHandler = onError; return true; });

		internal void SetLeaseStateHandler(Action<KeysharpInputClient> handler)
			=> owner.Invoke(() => { leaseStateHandler = handler; handler?.Invoke(this); return true; });

		internal void SubscribeKeyState()
			=> owner.Invoke(() =>
			{
				keyboardState.Invalidate();
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_key_state_subscribe(connection, ref error),
					"subscribe keyboard state", error);
				while (Drain()) { }
				return true;
			});

		internal bool TryGetKeyStateSnapshot(out KeyStateSnapshot state, out uint physicalModifiers)
		{
			if (lease != null) return lease.TryGetKeyStateSnapshot(out state, out physicalModifiers);
			return keyboardState.TryRead(out state, out physicalModifiers) && IsConnected;
		}

		internal bool WaitForSynthesisState(int timeoutMs = 250)
			=> lease != null ? lease.WaitForSynthesisState(timeoutMs)
				: IsConnected && keyboardState.WaitForAcknowledgement(timeoutMs);

		private bool Drain()
		{
			if (disposePending || connection == 0) return false;
			try
			{
				if (connectionRole == ConnectionRole.CallbackStream)
				{
					if (hookEventHandler == null || !TryReadHookEvent(out var hook)) return false;
					hookEventHandler(this, hook);
					return true;
				}
				var message = new NativeLeaseMessage { StructSize = (uint)sizeof(NativeLeaseMessage) };
				Native.ksi_error_init(out var error);
				var status = (NativeClientStatus)Native.ksi_lease_next(connection, 0, ref message, ref error);
				if (status == NativeClientStatus.Timeout) return false;
				ThrowIfFailed(status, "read lease state", error);
				if (connectionRole != ConnectionRole.Lease) return true;
				if (message.Kind is 1 or 2)
				{
					GrantedScopes = (LinuxPermissionScope)message.GrantedScopes & ManagedScopes;
					leaseStateHandler?.Invoke(this);
				}
				else if (message.Kind is 3 or 4)
				{
					if (!keyboardState.Publish(message.Sequence, ToManaged(message.State),
						message.PhysicalModifiersLR, message.Kind == 3))
						SubscribeKeyState();
				}
				else throw new InvalidDataException("keysharp-input returned an unknown lease message.");
				return true;
			}
			catch (Exception error)
			{
				if (!disposePending) streamErrorHandler?.Invoke(error);
				throw;
			}
		}

		private static KeyStateSnapshot ToManaged(NativeKeyState native)
		{
			KeyStateBitmap logical = default, physical = default;
			new ReadOnlySpan<byte>(native.LogicalKeys, KeyStateBitmapBytes).CopyTo(logical);
			new ReadOnlySpan<byte>(native.PhysicalKeys, KeyStateBitmapBytes).CopyTo(physical);
			return new(native.ModifiersLR, native.CapsLock != 0, native.NumLock != 0,
				native.ScrollLock != 0, logical, physical);
		}

		private bool TryReadHookEvent(out HookEvent hookEvent)
		{
			if (connectionRole != ConnectionRole.CallbackStream)
				throw new InvalidOperationException("Hook events require a callback-stream connection.");

			for (;;)
			{
				NativeHookMessage message;
				NativeError error;
				NativeClientStatus status;
				ThrowIfDisposed();
				Native.ksi_hook_message_init(out message);
				Native.ksi_error_init(out error);
				status = (NativeClientStatus)Native.ksi_hook_next(connection,
					0, ref message, ref error);

				if (status != NativeClientStatus.Timeout)
					ThrowIfFailed(status, "read hook event", error);

				if (status == NativeClientStatus.Timeout)
				{
					hookEvent = default;
					return false;
				}
				switch (message.Kind)
				{
					case 1:
						currentHookEvent = message.Data.Event;
						currentHookEventId = currentHookEvent.RequestId;
						hookEvent = ToManaged(currentHookEvent);
						return true;
					case 2:
						var quarantine = message.Data.Quarantined;
						hookQuarantineHandler?.Invoke(new((HookType)quarantine.HookType,
							quarantine.Reason, quarantine.EventId, quarantine.Generation,
							quarantine.StrikeCount, quarantine.RetryAfterMs));
						break;
					case 3:
						throw new NativeClientException("keysharp-input", "read hook event", NativeClientStatus.Revoked,
							message.Data.RevokedScopes, 0, "The input hook permission was revoked.");
					default:
						throw new InvalidDataException("keysharp-input returned an unknown hook message.");
				}
			}
		}

		internal void SendHookDecision(ulong eventId, HookDecision decision,
			IReadOnlyList<Input> replacementInputs = null)
		{
			if (!owner.IsOwnerThread) { owner.Invoke(() => { SendHookDecision(eventId, decision, replacementInputs); return true; }); return; }
			if (connectionRole != ConnectionRole.CallbackStream || eventId == 0)
				throw new InvalidOperationException("Hook decisions require an active hook event.");
			var count = replacementInputs?.Count ?? 0;
			if (count > MaxInputsPerRequest
				|| decision is < HookDecision.Pass or > HookDecision.Modify
				|| (decision == HookDecision.Modify) != (count != 0))
				throw new ArgumentOutOfRangeException(nameof(decision));
			if (decision != HookDecision.Pass)
				RequireOperations(Operations.BlockInput);

			if (nestedDepth != 0 && nestedEventIds[nestedDepth - 1] == eventId)
			{
				SetNestedReply(nestedDepth - 1, decision, replacementInputs);
				return;
			}
			if (currentHookEventId != eventId)
				throw new InvalidOperationException("Hook decision does not match the current event.");

			NativeInput* nativeInputs = null;
			try
			{
				if (count != 0)
				{
					nativeInputs = (NativeInput*)NativeMemory.Alloc(
						checked((nuint)count * (nuint)sizeof(NativeInput)));
					for (var index = 0; index < count; index++)
						nativeInputs[index] = ToNative(replacementInputs[index]);
				}
				var reply = NewReply(decision, nativeInputs, count);
				ThrowIfDisposed();
				Native.ksi_error_init(out var error);
				fixed (NativeHookEvent* hookEvent = &currentHookEvent)
				{
					var status = (NativeClientStatus)Native.ksi_hook_reply_event(
						connection, hookEvent, ref reply, ref error);
					ThrowIfFailed(status, "reply to hook event", error);
				}
				currentHookEventId = 0;
			}
			finally
			{
				NativeMemory.Free(nativeInputs);
			}
		}

		internal bool TryRequestOperations(Operations requested, out int status,
			bool checkOnly = false)
			=> TryRequestOperations(requested, requested, out status, checkOnly);

		internal bool TryRequestOperations(Operations requested,
			Operations requiredFromService, out int status, bool checkOnly = false)
		{
			if ((requested & ~Operations.All) != 0 || (requiredFromService & ~requested) != 0)
				throw new ArgumentOutOfRangeException(nameof(requested));
			if ((AvailableOperations & requiredFromService) != requiredFromService)
			{
				status = (int)NativeClientStatus.Unsupported;
				return false;
			}

			var scopes = RequiredScopes(requested);
			if (scopes == LinuxPermissionScope.None)
			{
				status = (int)NativeClientStatus.Ok;
				return true;
			}
			return TryRequestScopes(scopes, out status, checkOnly)
				&& HasOperations(requiredFromService);
		}

		internal PermissionResult Authorize(LinuxPermissionScope scopes, Operations operations, string operation, bool prompt)
		{
			var granted = operations != Operations.None || scopes == LinuxPermissionScope.None
				? TryRequestOperations(operations, out var status, !prompt)
				: TryRequestScopes(scopes, out status, !prompt);
			return granted ? new(PermissionStatus.Granted)
				: new(status == (int)NativeClientStatus.Unsupported ? PermissionStatus.Unsupported : PermissionStatus.Denied,
					$"keysharp-input could not authorize '{operation}'. Required scopes: {scopes}; granted scopes: {GrantedScopes}.");
		}

		internal bool TryRequestScopes(LinuxPermissionScope requestedScopes,
			out int status, bool checkOnly = false)
		{
			if (lease != null) return lease.TryRequestScopes(requestedScopes, out status, checkOnly);
			var result = owner.Invoke(() =>
			{
				var success = AuthorizeScopes(requestedScopes, out var value, checkOnly);
				return (success, value);
			});
			status = result.value;
			return result.success;
		}

		private bool AuthorizeScopes(LinuxPermissionScope requestedScopes, out int status, bool checkOnly)
		{
			if (requestedScopes == LinuxPermissionScope.None
				|| (requestedScopes & ~ManagedScopes) != 0)
				throw new ArgumentOutOfRangeException(nameof(requestedScopes));

			ThrowIfDisposed();
			Native.ksi_error_init(out var error);
			var nativeStatus = (NativeClientStatus)Native.ksi_authorize(connection,
				(uint)(checkOnly ? AuthorizationMode.Check : AuthorizationMode.Request),
				(uint)requestedScopes, out var granted, ref error);
			status = (int)nativeStatus;
			if (nativeStatus is NativeClientStatus.Timeout or NativeClientStatus.Unavailable) ThrowIfFailed(nativeStatus, "authorize", error);
			if (disposePending) { status = (int)NativeClientStatus.Revoked; return false; }
			if (nativeStatus == NativeClientStatus.Ok) GrantedScopes = (LinuxPermissionScope)granted & ManagedScopes;
			while (Drain()) { }
			if (nativeStatus == NativeClientStatus.Ok)
			{
				return (GrantedScopes & requestedScopes) == requestedScopes;
			}
			if (nativeStatus is NativeClientStatus.Denied or NativeClientStatus.Unsupported
				or NativeClientStatus.Cancelled or NativeClientStatus.Revoked)
				return false;
			ThrowIfFailed(nativeStatus, "authorize", error);
			return false;
		}

		private static uint DispatchNestedHook(nint connection,
			NativeHookEvent* hookEvent, NativeHookReply* reply, nint context,
			NativeError* error)
		{
			if (context == 0 || hookEvent == null || reply == null)
				return (uint)NativeClientStatus.InvalidRequest;

			try
			{
				var client = GCHandle.FromIntPtr(context).Target as KeysharpInputClient;
				return client == null
					? (uint)NativeClientStatus.Cancelled
					: (uint)client.HandleNestedHook(hookEvent, reply);
			}
			catch (Exception exception)
			{
				Diagnostics.Debug.WriteLine($"keysharp-input nested hook failed open: {exception}");
				*reply = NewReply(HookDecision.Pass, null, 0);
				return (uint)NativeClientStatus.Ok;
			}
		}

		private NativeClientStatus HandleNestedHook(NativeHookEvent* hookEvent,
			NativeHookReply* reply)
		{
			var depth = nestedDepth;
			if (depth >= NestedHookLimit)
				return NativeClientStatus.ResourceExhausted;

			*reply = NewReply(HookDecision.Pass, null, 0);
			nestedReplies[depth] = (nint)reply;
			nestedEventIds[depth] = hookEvent->RequestId;
			NativeMemory.Free((void*)nestedReplacementBuffers[depth]);
			nestedReplacementBuffers[depth] = 0;
			nestedDepth = depth + 1;

			try
			{
				if (!disposePending) nestedHookEventHandler?.Invoke(this, ToManaged(*hookEvent));
				return NativeClientStatus.Ok;
			}
			finally
			{
				// Native code serializes the reply after this callback returns. Each depth
				// retains its buffer until the next callback at that depth or disconnect.
				nestedReplies[depth] = 0;
				nestedEventIds[depth] = 0;
				nestedDepth = depth;
			}
		}

		private void SetNestedReply(int depth, HookDecision decision,
			IReadOnlyList<Input> replacementInputs)
		{
			var count = replacementInputs?.Count ?? 0;
			NativeMemory.Free((void*)nestedReplacementBuffers[depth]);
			nestedReplacementBuffers[depth] = 0;
			NativeInput* inputs = null;
			if (count != 0)
			{
				inputs = (NativeInput*)NativeMemory.Alloc(
					checked((nuint)count * (nuint)sizeof(NativeInput)));
				for (var index = 0; index < count; index++)
					inputs[index] = ToNative(replacementInputs[index]);
			}
			nestedReplacementBuffers[depth] = (nint)inputs;
			*(NativeHookReply*)nestedReplies[depth] = NewReply(decision, inputs, count);
		}

		private static NativeHookReply NewReply(HookDecision decision,
			NativeInput* inputs, int count)
			=> new()
			{
				StructSize = (uint)sizeof(NativeHookReply),
				Decision = (uint)decision,
				Inputs = inputs,
				InputCount = (uint)count,
			};

		private static HookEvent ToManaged(in NativeHookEvent hookEvent)
		{
			if (hookEvent.HookType == (uint)HookType.KeyboardLowLevel)
			{
				var value = hookEvent.Event.Keyboard;
				return new(hookEvent.RequestId, HookType.KeyboardLowLevel,
					new(value.Message, value.VkCode, value.ScanCode, value.Flags,
						value.TimeMs, value.ExtraInfo, value.DeviceId), default);
			}
			if (hookEvent.HookType == (uint)HookType.MouseLowLevel)
			{
				var value = hookEvent.Event.Mouse;
				return new(hookEvent.RequestId, HookType.MouseLowLevel, default,
					new(value.Message, value.X, value.Y, value.MouseData, value.Flags,
						value.TimeMs, value.ExtraInfo, value.DeviceId,
						value.DeltaX, value.DeltaY));
			}
			throw new InvalidDataException("keysharp-input returned an unknown hook type.");
		}

		private static NativeInput ToNative(in Input input)
		{
			var native = new NativeInput
			{
				StructSize = (uint)sizeof(NativeInput),
				Type = (uint)input.Type,
			};
			if (input.Type == InputType.Keyboard)
				native.Data.Keyboard = new()
				{
					Vk = input.Keyboard.Vk,
					Scan = input.Keyboard.Scan,
					Flags = (uint)input.Keyboard.Flags,
					Time = input.Keyboard.Time,
					ExtraInfo = input.Keyboard.ExtraInfo,
				};
			else if (input.Type == InputType.Mouse)
				native.Data.Mouse = new()
				{
					Dx = input.Mouse.Dx,
					Dy = input.Mouse.Dy,
					MouseData = input.Mouse.MouseData,
					Flags = (uint)input.Mouse.Flags,
					Time = input.Mouse.Time,
					ExtraInfo = input.Mouse.ExtraInfo,
				};
			else
				throw new ArgumentOutOfRangeException(nameof(input));
			return native;
		}

		private void RequireOperations(Operations operations)
		{
			if (!HasOperations(operations))
				throw new InvalidOperationException(
					$"keysharp-input does not grant the required operation: {operations}.");
		}

		private void ThrowIfFailed(NativeClientStatus status,
			string operation, in NativeError error)
		{
			if (status == NativeClientStatus.Ok)
				return;

			if (status == NativeClientStatus.Timeout) Dispose();

			throw new NativeClientException("keysharp-input", operation, status,
				error.Detail, error.SystemError, error.GetMessage());
		}

		private static void ThrowConnectIfFailed(NativeClientStatus status,
			in NativeError error)
		{
			if (status != NativeClientStatus.Ok)
				throw new NativeClientException("keysharp-input", "connect", status,
					error.Detail, error.SystemError, error.GetMessage());
		}

		private void ThrowIfDisposed()
		{
			if (connection == 0 || disposePending)
				throw new ObjectDisposedException(nameof(KeysharpInputClient));
		}

		public void Dispose()
		{
			disposePending = true;
			GrantedScopes = LinuxPermissionScope.None;
			keyboardState.Invalidate();
			owner.Dispose();
		}

		private void Cleanup()
		{
			disposePending = true;
			GrantedScopes = LinuxPermissionScope.None;
			keyboardState.Invalidate();
			var handle = Interlocked.Exchange(ref connection, 0);
			if (handle != 0) Native.ksi_disconnect(handle);
			for (var depth = 0; depth < nestedReplacementBuffers.Length; depth++)
			{
				NativeMemory.Free((void*)nestedReplacementBuffers[depth]);
				nestedReplacementBuffers[depth] = 0;
			}
			if (callbackHandle.IsAllocated) callbackHandle.Free();
			leaseStateHandler?.Invoke(this);
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
			internal nint SocketPath;
			internal uint TimeoutMs;
			internal uint Flags;
			internal ulong LeaseId;
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
			internal ulong LeaseId;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeKeyboardInput
		{
			internal ushort Vk;
			internal ushort Scan;
			internal uint Flags;
			internal uint Time;
			private uint reserved0;
			internal ulong ExtraInfo;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeMouseInput
		{
			internal int Dx;
			internal int Dy;
			internal uint MouseData;
			internal uint Flags;
			internal uint Time;
			private uint reserved0;
			internal ulong ExtraInfo;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Explicit, Size = 48)]
		private struct NativeInputData
		{
			[FieldOffset(0)] internal NativeKeyboardInput Keyboard;
			[FieldOffset(0)] internal NativeMouseInput Mouse;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeInput
		{
			internal uint StructSize;
			internal uint Type;
			internal NativeInputData Data;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeKeyboardHookEvent
		{
			internal uint Message;
			internal uint VkCode;
			internal uint ScanCode;
			internal uint Flags;
			internal ulong TimeMs;
			internal ulong ExtraInfo;
			internal uint DeviceId;
			private uint reserved0;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeMouseHookEvent
		{
			internal uint Message;
			internal int X;
			internal int Y;
			internal uint MouseData;
			internal uint Flags;
			private uint reserved0;
			internal ulong TimeMs;
			internal ulong ExtraInfo;
			internal uint DeviceId;
			internal int DeltaX;
			internal int DeltaY;
			private uint reserved1;
		}

		[StructLayout(LayoutKind.Explicit, Size = 56)]
		private struct NativeHookEventData
		{
			[FieldOffset(0)] internal NativeKeyboardHookEvent Keyboard;
			[FieldOffset(0)] internal NativeMouseHookEvent Mouse;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeHookEvent
		{
			internal uint StructSize;
			internal uint HookType;
			internal ulong RequestId;
			internal NativeHookEventData Event;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeHookReply
		{
			internal uint StructSize;
			internal uint Decision;
			internal NativeInput* Inputs;
			internal uint InputCount;
			private uint reserved0;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeHookQuarantined
		{
			internal uint StructSize;
			internal uint HookType;
			internal uint Reason;
			internal uint Generation;
			internal ulong EventId;
			internal uint StrikeCount;
			internal uint RetryAfterMs;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Explicit, Size = 104)]
		private struct NativeHookMessageData
		{
			[FieldOffset(0)] internal NativeHookEvent Event;
			[FieldOffset(0)] internal NativeHookQuarantined Quarantined;
			[FieldOffset(0)] internal uint RevokedScopes;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeHookMessage
		{
			internal uint StructSize;
			internal uint Kind;
			internal NativeHookMessageData Data;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePointerPosition
		{
			internal uint StructSize;
			internal byte Valid;
			private fixed byte reserved0[3];
			internal int X;
			internal int Y;
			internal int XMin;
			internal int XMax;
			internal int YMin;
			internal int YMax;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeLeaseMessage
		{
			internal uint StructSize, Kind;
			internal ulong Sequence;
			internal uint GrantedScopes, RevokedScopes;
			internal uint PhysicalModifiersLR;
			private uint reserved0;
			internal NativeKeyState State;
			private fixed ulong reserved[4];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeKeyState
		{
			internal uint StructSize;
			internal uint ModifiersLR;
			internal byte CapsLock;
			internal byte NumLock;
			internal byte ScrollLock;
			private byte reserved0;
			internal fixed byte LogicalKeys[KeyStateBitmapBytes];
			internal fixed byte PhysicalKeys[KeyStateBitmapBytes];
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativePointerButtons
		{
			internal uint StructSize;
			internal byte Valid;
			private fixed byte reserved0[3];
			internal uint LogicalButtons;
			internal uint PhysicalButtons;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeIdleTime
		{
			internal uint StructSize;
			internal byte Valid;
			private fixed byte reserved0[3];
			internal ulong IdleTimeMs;
			private fixed ulong reserved[2];
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeDeviceAxisInfo
		{
			internal uint StructSize;
			internal uint Code;
			internal int Minimum;
			internal int Maximum;
			internal int Fuzz;
			internal int Flat;
			internal int Resolution;
			private uint reserved;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeDeviceInfo
		{
			internal uint StructSize;
			internal uint DeviceId;
			internal uint Capabilities;
			internal ushort BusType;
			internal ushort Vendor;
			internal ushort Product;
			internal ushort Version;
			private uint reserved0;
			private fixed byte name[DeviceNameCapacity];
			private fixed byte path[512];
			private fixed byte physical[256];
			private fixed byte unique[128];
			internal uint AxisCount;
			private uint reserved1;
			internal fixed byte Axes[DeviceAxisCapacity * 32];
			internal uint ButtonCount;
			private uint reserved2;
			internal fixed ushort ButtonCodes[DeviceButtonCapacity];
			private fixed ulong reserved[4];

			internal string GetName()
			{
				fixed (byte* pointer = name)
				{
					var length = 0;

					while (length < DeviceNameCapacity && pointer[length] != 0)
						length++;

					try { return StrictUtf8.GetString(pointer, length); }
					catch (DecoderFallbackException) { return string.Empty; }
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeGamepadAxisState
		{
			internal uint StructSize;
			internal uint Code;
			internal int Value;
			private uint reserved;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeGamepadState
		{
			internal uint StructSize;
			internal uint DeviceId;
			internal ulong DeviceGeneration;
			internal uint ButtonCount;
			internal uint AxisCount;
			internal fixed byte Buttons[DeviceButtonCapacity / 8];
			internal fixed byte Axes[DeviceAxisCapacity * 16];
			private fixed ulong reserved[4];
		}

		private static class Native
		{
			private const string Library = "libkeysharp-input.so.1";

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			[return: MarshalAs(UnmanagedType.U1)]
			internal delegate bool DeviceVisitor(NativeDeviceInfo* device, nint context);

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			internal delegate uint NestedHookHandler(nint connection,
				NativeHookEvent* hookEvent, NativeHookReply* reply, nint context,
				NativeError* error);

			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern int ksi_connection_fd(nint connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern ulong ksi_connection_sequence(nint connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_lease_next(nint connection, uint timeoutMs,
				ref NativeLeaseMessage message, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_key_state_subscribe(nint connection, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_connect_options_init(out NativeConnectOptions options);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_service_info_init(out NativeServiceInfo info);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_error_init(out NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_hook_message_init(out NativeHookMessage message);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_pointer_position_init(out NativePointerPosition position);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_key_state_init(out NativeKeyState state);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_pointer_buttons_init(out NativePointerButtons buttons);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_idle_time_init(out NativeIdleTime idleTime);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_gamepad_state_init(out NativeGamepadState state);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_gamepads_list(nint connection, DeviceVisitor visitor,
				nint context, out ulong generation, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_gamepad_state(nint connection, uint deviceId,
				ulong generation, ref NativeGamepadState state, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_connect(ref NativeConnectOptions options,
				out nint connection, ref NativeServiceInfo info, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_disconnect(nint connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_authorize(nint connection, uint mode,
				uint scopes, out uint grantedScopes, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_set_nested_hook_handler(nint connection,
				NestedHookHandler handler, nint context, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_subscribe(nint connection, uint hookType,
				out ulong activeOperations, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_unsubscribe(nint connection, uint hookType,
				out ulong activeOperations, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_next(nint connection, uint timeoutMs,
				ref NativeHookMessage message, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_reply_event(nint connection,
				NativeHookEvent* hookEvent, ref NativeHookReply reply, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_synthesize(nint connection,
				NativeInput* inputs, uint count, uint flags, out uint logicalModifiersLR, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_set_block_input(nint connection, uint mask,
				out uint effectiveMask, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_pointer_position(nint connection,
				ref NativePointerPosition position, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_device_key_state(nint connection, uint deviceID,
				ref NativeKeyState state, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_pointer_buttons(nint connection,
				ref NativePointerButtons buttons, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_idle_time(nint connection,
				ref NativeIdleTime idleTime, ref NativeError error);
		}
	}
}
#endif
