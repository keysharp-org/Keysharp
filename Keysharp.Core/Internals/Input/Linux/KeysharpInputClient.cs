#if LINUX
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Input.Linux
{
	/// <summary>Typed client for <c>libkeysharp-input.so.0</c>.</summary>
	internal sealed unsafe class KeysharpInputClient : IDisposable
	{
		internal const string SocketEnvironmentVariable = "KEYSHARP_INPUT_SOCKET";
		internal const string DefaultSocketPathValue = "/run/keysharp-input/keysharp-input.sock";
		internal const int MaxInputsPerRequest = 1024;
		internal const int KeyStateBitmapBytes = 96;
		internal const int DefaultRequestTimeoutMs = 5000;
		internal const int AuthorizationTimeoutMs = 125_000;
		private const int HookPollTimeoutMs = 500;
		private const int NestedHookLimit = 16;
		private const LinuxPermissionScope ManagedScopes =
			LinuxPermissionScope.InputMonitoring | LinuxPermissionScope.InputControl;
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);
		private static readonly Native.NestedHookHandler NestedHookThunk = DispatchNestedHook;
		private static readonly uint NativeServiceInfoStructSize =
			checked((uint)sizeof(NativeServiceInfo));

		internal enum ConnectionRole : uint
		{
			Rpc = 0,
			CallbackStream = 2,
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
			All = (1UL << 14) - 1,
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
		internal readonly record struct KeyStateSnapshot(uint ModifiersLR, bool CapsLock,
			bool NumLock, bool ScrollLock, byte[] LogicalKeys, byte[] PhysicalKeys);
		internal readonly record struct ModifierStateSnapshot(uint LogicalModifiersLR,
			uint PhysicalModifiersLR, bool CapsLock, bool NumLock, bool ScrollLock);
		internal readonly record struct PointerButtons(uint LogicalButtons, uint PhysicalButtons);

		private readonly Lock nativeLock = new();
		private readonly ConnectionRole connectionRole;
		private readonly nint[] nestedReplies = new nint[NestedHookLimit];
		private readonly ulong[] nestedEventIds = new ulong[NestedHookLimit];
		private readonly nint[] nestedReplacementBuffers = new nint[NestedHookLimit];
		private nint connection;
		private NativeHookEvent currentHookEvent;
		private ulong currentHookEventId;
		private int nestedDepth;
		private volatile bool disposePending;
		private GCHandle callbackHandle;
		private Action<KeysharpInputClient, HookEvent> nestedHookEventHandler;
		private Action<HookQuarantine> hookQuarantineHandler;
		private volatile Func<bool> leaseLivenessProbe;

		private KeysharpInputClient(nint connection, ConnectionRole role,
			LinuxPermissionScope grantedScopes, Operations availableOperations)
		{
			this.connection = connection;
			connectionRole = role;
			GrantedScopes = grantedScopes;
			AvailableOperations = availableOperations;
		}

		internal LinuxPermissionScope GrantedScopes { get; private set; }
		internal Operations AvailableOperations { get; }
		internal bool IsConnected => !disposePending && Volatile.Read(ref connection) != 0;

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
			ConnectionRole role = ConnectionRole.Rpc)
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
						|| info.ClientAbiMajor != 0 || info.ClientAbiMinor < 1)
						throw new InvalidDataException(
							$"Unsupported keysharp-input client ABI {info.ClientAbiMajor}.{info.ClientAbiMinor}.");
					if ((info.GrantedScopes & ~(uint)ManagedScopes) != 0)
						throw new InvalidDataException("keysharp-input returned unknown capability bits.");

					var client = new KeysharpInputClient(connection, role,
						(LinuxPermissionScope)info.GrantedScopes,
						(Operations)info.AvailableOperations & Operations.All);
					connection = 0;

					if ((client.AvailableOperations & requested) != requested)
					{
						client.Dispose();
						throw new NativeClientException("keysharp-input", "connect",
							NativeClientStatus.Unsupported, 0, 0,
							"keysharp-input does not provide the requested operations.");
					}
					if (!client.HasOperations(requested))
					{
						client.Dispose();
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
			lock (nativeLock)
			{
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
		}

		internal void SetHookQuarantineHandler(Action<HookQuarantine> handler)
			=> hookQuarantineHandler = handler;
		internal void SetLeaseLivenessProbe(Func<bool> probe) => leaseLivenessProbe = probe;
		internal void InvalidateScopes(LinuxPermissionScope scopes)
			=> GrantedScopes &= ~(scopes & ManagedScopes);

		internal bool HasOperations(Operations operations)
		{
			var requiredScopes = RequiredScopes(operations);
			return (AvailableOperations & operations) == operations
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
			if ((mask & ~(BlockInputMask.Keyboard | BlockInputMask.Mouse)) != 0)
				throw new ArgumentOutOfRangeException(nameof(mask));
			if (mask != BlockInputMask.None)
				RequireOperations(Operations.BlockInput);

			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_error_init(out var error);
				var status = (NativeClientStatus)Native.ksi_set_block_input(connection,
					(uint)mask, out var effective, ref error);
				ThrowIfFailed(status, "set block input", error);
				return (BlockInputMask)effective;
			}
		}

		internal Operations SubscribeHook(HookType hookType)
		{
			var operation = hookType switch
			{
				HookType.KeyboardLowLevel => Operations.HookKeyboard,
				HookType.MouseLowLevel => Operations.HookMouse,
				_ => throw new ArgumentOutOfRangeException(nameof(hookType)),
			};
			RequireOperations(operation);

			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_error_init(out var error);
				var status = (NativeClientStatus)Native.ksi_hook_subscribe(connection,
					(uint)hookType, out var activeOperations, ref error);
				ThrowIfFailed(status, "subscribe hook", error);
				return (Operations)activeOperations;
			}
		}

		internal void SendInput(IReadOnlyList<Input> inputs,
			SynthFlags flags = SynthFlags.None, ulong parentHookEventId = 0)
		{
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
				return;

			var required = Operations.None;
			NativeInput[] rented = null;
			Span<NativeInput> nativeInputs = inputs.Count <= 64
				? stackalloc NativeInput[inputs.Count]
				: (rented = ArrayPool<NativeInput>.Shared.Rent(inputs.Count)).AsSpan(0, inputs.Count);

			try
			{
				for (var index = 0; index < inputs.Count; index++)
				{
					required |= inputs[index].Type switch
					{
						InputType.Keyboard => Operations.SynthesizeKeyboard,
						InputType.Mouse => Operations.SynthesizeMouse,
						_ => throw new NotSupportedException($"Input type {inputs[index].Type} is not supported."),
					};
					nativeInputs[index] = ToNative(inputs[index]);
				}
				RequireOperations(required);

				fixed (NativeInput* pointer = nativeInputs)
				lock (nativeLock)
				{
					ThrowIfDisposed();
					Native.ksi_error_init(out var error);
					var status = (NativeClientStatus)Native.ksi_synthesize(connection,
						pointer, (uint)inputs.Count, (uint)flags, ref error);
					ThrowIfFailed(status, "synthesize input", error);
				}
			}
			finally
			{
				if (rented != null)
					ArrayPool<NativeInput>.Shared.Return(rented);
			}
		}

		internal KeyStateSnapshot QueryKeyState()
		{
			RequireOperations(Operations.QueryKeyState);
			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_key_state_init(out var state);
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_get_key_state(
					connection, ref state, ref error), "query key state", error);
				var logical = new byte[KeyStateBitmapBytes];
				var physical = new byte[KeyStateBitmapBytes];
				new ReadOnlySpan<byte>(state.LogicalKeys, KeyStateBitmapBytes).CopyTo(logical);
				new ReadOnlySpan<byte>(state.PhysicalKeys, KeyStateBitmapBytes).CopyTo(physical);
				return new(state.ModifiersLR, state.CapsLock != 0, state.NumLock != 0,
					state.ScrollLock != 0, logical, physical);
			}
		}

		internal ModifierStateSnapshot QueryModifierState()
		{
			RequireOperations(Operations.QueryModifiers);
			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_modifier_state_init(out var state);
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_get_modifier_state(
					connection, ref state, ref error), "query modifiers", error);
				return new(state.LogicalModifiersLR, state.PhysicalModifiersLR,
					state.CapsLock != 0, state.NumLock != 0, state.ScrollLock != 0);
			}
		}

		internal bool TryGetPointerPosition(out PointerPosition position)
		{
			RequireOperations(Operations.QueryPointerPosition);
			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_pointer_position_init(out var native);
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_get_pointer_position(
					connection, ref native, ref error), "query pointer position", error);
				position = new(native.X, native.Y, native.XMin, native.XMax,
					native.YMin, native.YMax);
				return native.Valid != 0;
			}
		}

		internal bool TryGetPointerButtons(out PointerButtons buttons)
		{
			RequireOperations(Operations.QueryPointerButtons);
			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_pointer_buttons_init(out var native);
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_get_pointer_buttons(
					connection, ref native, ref error), "query pointer buttons", error);
				buttons = new(native.LogicalButtons, native.PhysicalButtons);
				return native.Valid != 0;
			}
		}

		internal bool TryGetIdleTime(out ulong milliseconds)
		{
			RequireOperations(Operations.QueryIdleTime);
			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_idle_time_init(out var native);
				Native.ksi_error_init(out var error);
				ThrowIfFailed((NativeClientStatus)Native.ksi_get_idle_time(
					connection, ref native, ref error), "query idle time", error);
				milliseconds = native.IdleTimeMs;
				return native.Valid != 0;
			}
		}

		internal HookEvent ReadHookEvent()
		{
			if (connectionRole != ConnectionRole.CallbackStream)
				throw new InvalidOperationException("Hook events require a callback-stream connection.");

			for (;;)
			{
				NativeHookMessage message;
				NativeError error;
				NativeClientStatus status;
				lock (nativeLock)
				{
					ThrowIfDisposed();
					Native.ksi_hook_message_init(out message);
					Native.ksi_error_init(out error);
					status = (NativeClientStatus)Native.ksi_hook_next(connection,
						HookPollTimeoutMs, ref message, ref error);
				}

				if (status == NativeClientStatus.Timeout)
				{
					if (leaseLivenessProbe?.Invoke() == false)
						throw new IOException("keysharp-input hook consumer stopped responding.");
					continue;
				}
				ThrowIfFailed(status, "read hook event", error);

				switch (message.Kind)
				{
					case 1:
						currentHookEvent = message.Data.Event;
						currentHookEventId = currentHookEvent.RequestId;
						return ToManaged(currentHookEvent);
					case 2:
						var quarantine = message.Data.Quarantined;
						hookQuarantineHandler?.Invoke(new((HookType)quarantine.HookType,
							quarantine.Reason, quarantine.EventId, quarantine.Generation,
							quarantine.StrikeCount, quarantine.RetryAfterMs));
						break;
					case 3:
						InvalidateScopes((LinuxPermissionScope)message.Data.RevokedScopes);
						break;
					default:
						throw new InvalidDataException("keysharp-input returned an unknown hook message.");
				}
			}
		}

		internal void SendHookDecision(ulong eventId, HookDecision decision,
			IReadOnlyList<Input> replacementInputs = null)
		{
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
				lock (nativeLock)
				{
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

		internal bool TryRequestScopes(LinuxPermissionScope requestedScopes,
			out int status, bool checkOnly = false)
		{
			if (requestedScopes == LinuxPermissionScope.None
				|| (requestedScopes & ~ManagedScopes) != 0)
				throw new ArgumentOutOfRangeException(nameof(requestedScopes));

			lock (nativeLock)
			{
				ThrowIfDisposed();
				Native.ksi_error_init(out var error);
				var nativeStatus = (NativeClientStatus)Native.ksi_authorize(connection,
					(uint)(checkOnly ? AuthorizationMode.Check : AuthorizationMode.Request),
					(uint)requestedScopes, out var granted, ref error);
				status = (int)nativeStatus;
				GrantedScopes = (LinuxPermissionScope)(Native.ksi_connection_granted_scopes(connection)
					& (uint)ManagedScopes);
				if (nativeStatus == NativeClientStatus.Ok)
				{
					GrantedScopes |= (LinuxPermissionScope)granted & ManagedScopes;
					return (GrantedScopes & requestedScopes) == requestedScopes;
				}
				if (nativeStatus is NativeClientStatus.Denied or NativeClientStatus.Unsupported
					or NativeClientStatus.Cancelled or NativeClientStatus.Revoked)
					return false;
				ThrowIfFailed(nativeStatus, "authorize", error);
				return false;
			}
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
			if (status != NativeClientStatus.Ok)
			{
				if (connection != 0)
					GrantedScopes = (LinuxPermissionScope)(
						Native.ksi_connection_granted_scopes(connection) & (uint)ManagedScopes);
				throw new NativeClientException("keysharp-input", operation, status,
					error.Detail, error.SystemError, error.GetMessage());
			}
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
			lock (nativeLock)
			{
				if (nestedDepth != 0)
				{
					// A callback still has native stack frames using this connection and its reply buffers.
					if (!disposePending)
					{
						disposePending = true;
						ThreadPool.QueueUserWorkItem(static client => client.Dispose(), this, preferLocal: false);
					}
					return;
				}
				var handle = Interlocked.Exchange(ref connection, 0);
				if (handle == 0)
					return;
				Native.ksi_disconnect(handle);
				for (var depth = 0; depth < nestedReplacementBuffers.Length; depth++)
				{
					NativeMemory.Free((void*)nestedReplacementBuffers[depth]);
					nestedReplacementBuffers[depth] = 0;
				}
				if (callbackHandle.IsAllocated)
					callbackHandle.Free();
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
			internal nint SocketPath;
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
		private struct NativeModifierState
		{
			internal uint StructSize;
			internal uint LogicalModifiersLR;
			internal uint PhysicalModifiersLR;
			internal byte CapsLock;
			internal byte NumLock;
			internal byte ScrollLock;
			private byte reserved0;
			private fixed ulong reserved[2];
		}

		private static class Native
		{
			private const string Library = "libkeysharp-input.so.0";

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			internal delegate uint NestedHookHandler(nint connection,
				NativeHookEvent* hookEvent, NativeHookReply* reply, nint context,
				NativeError* error);

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
			internal static extern void ksi_modifier_state_init(out NativeModifierState state);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_connect(ref NativeConnectOptions options,
				out nint connection, ref NativeServiceInfo info, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern void ksi_disconnect(nint connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_authorize(nint connection, uint mode,
				uint scopes, out uint grantedScopes, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_connection_granted_scopes(nint connection);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_set_nested_hook_handler(nint connection,
				NestedHookHandler handler, nint context, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_subscribe(nint connection, uint hookType,
				out ulong activeOperations, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_next(nint connection, uint timeoutMs,
				ref NativeHookMessage message, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_hook_reply_event(nint connection,
				NativeHookEvent* hookEvent, ref NativeHookReply reply, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_synthesize(nint connection,
				NativeInput* inputs, uint count, uint flags, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_set_block_input(nint connection, uint mask,
				out uint effectiveMask, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_pointer_position(nint connection,
				ref NativePointerPosition position, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_key_state(nint connection,
				ref NativeKeyState state, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_pointer_buttons(nint connection,
				ref NativePointerButtons buttons, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_idle_time(nint connection,
				ref NativeIdleTime idleTime, ref NativeError error);
			[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
			internal static extern uint ksi_get_modifier_state(nint connection,
				ref NativeModifierState state, ref NativeError error);
		}
	}
}
#endif
