using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

#if LINUX
namespace Keysharp.Internals.Input.Linux
{
	internal sealed class KeysharpInputdClient : IDisposable
	{
		internal const string SocketEnvironmentVariable = "KEYSHARP_INPUTD_SOCKET";
		internal const string DefaultSocketPathValue = "/run/keysharp-inputd/keysharp-inputd.sock";

		private const uint ProtocolMajor = 1;
		private const uint ProtocolMinor = 1;
		private const int HeaderSize = 24;
		private const int MaxMessageSize = 65536;
		private const int InputSize = 40;
		internal const int MaxInputsPerRequest = 1024;
		internal const int KeyStateBitmapBytes = 96;
		private const uint ClientHelloFlagForcePrompt = 0x00000001;
		// Bounds request round-trips so a hung daemon cannot block a script thread.
		internal const int DefaultRequestTimeoutMs = 5000;
		// CLIENT_HELLO may wait on the daemon's interactive trust prompt.
		private const int HelloResponseTimeoutMs = 75000;
		private const int LeaseHeartbeatPeriodMs = 5000;

		internal enum ConnectionRole : uint
		{
			GeneralRpc = 0,
			HookStream = 1,
		}

		[Flags]
		internal enum Capabilities : uint
		{
			None = 0,
			HookKeyboard = 0x00000001,
			HookMouse = 0x00000002,
			SynthKeyboard = 0x00000004,
			SynthMouse = 0x00000008,
			BlockInput = 0x00000010,
			// Brokered in the combined inputd trust prompt for helper reuse.
			ScreenCapture = 0x00000020,
			AccessibilityAutomation = 0x00000040,
		}

		internal enum MessageType : uint
		{
			ClientHello = 1,
			Heartbeat = 3,
			SubscribeHook = 10,
			UnsubscribeHook = 11,
			HookEvent = 12,
			HookDecision = 13,
			HookQuarantined = 14,
			SynthesizeInput = 20,
			SynthesisResult = 21,
			EmergencyPassthrough = 30,
			SetBlockInput = 31,
			GetIndicatorState    = 40,
			IndicatorStateResult = 41,
			GetPointerPosition   = 42,
			PointerPositionResult = 43,
			GetKeyState          = 44,
			KeyStateResult       = 45,
			GetPointerButtons    = 46,
			PointerButtonsResult = 47,
			IdleTime             = 48,
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
			// Suppress-and-replace. The hook path no longer emits this — a hook now
			// returns pure Block/Pass and any inline send goes out as a separate
			// synthesis (Windows-style). Kept for protocol completeness.
			Modify = 2,
		}

		internal enum StatusDetail : uint
		{
			None = 0,
			PayloadTooSmall = 1,
			InputCountLimit = 2,
			PayloadSizeMismatch = 3,
			ResourceExhausted = 12,
			RecursionLimit = 32,
			ExpandedInputLimit = 33,
			Cancelled = 125,
			PermissionDenied = 403,
			CallbackTimeout = 408,
		}

		internal enum HookDecisionDetail : uint
		{
			PayloadTooSmall = 1,
			StaleOrWrongResponder = 2,
			InvalidDecision = 4,
			InputCountLimit = 5,
			PayloadSizeMismatch = 6,
			EmptyModify = 7,
		}

		[Flags]
		internal enum BlockInputMask : uint
		{
			None = 0,
			Keyboard = 0x00000001,
			Mouse = 0x00000002,
		}

		internal enum InputType : uint
		{
			Mouse = 0,
			Keyboard = 1,
			Hardware = 2,
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

		internal readonly record struct KeyboardInput(ushort Vk, ushort Scan, KeyEventFlags Flags, uint Time = 0, ulong ExtraInfo = 0);
		internal readonly record struct MouseInput(int Dx, int Dy, uint MouseData, MouseEventFlags Flags, uint Time = 0, ulong ExtraInfo = 0);

		internal readonly record struct Input(InputType Type, KeyboardInput Keyboard, MouseInput Mouse)
		{
			internal static Input Key(ushort vk, ushort scan = 0, KeyEventFlags flags = 0, uint time = 0, ulong extraInfo = 0)
				=> new(InputType.Keyboard, new KeyboardInput(vk, scan, flags, time, extraInfo), default);

			internal static Input MouseEvent(int dx, int dy, uint mouseData, MouseEventFlags flags, uint time = 0, ulong extraInfo = 0)
				=> new(InputType.Mouse, default, new MouseInput(dx, dy, mouseData, flags, time, extraInfo));
		}

		internal readonly record struct KeyboardHookEvent(
			uint Message,
			uint VkCode,
			uint ScanCode,
			uint Flags,
			ulong TimeMs,
			ulong ExtraInfo,
			uint DeviceId);

		internal readonly record struct MouseHookEvent(
			uint Message,
			int X,
			int Y,
			uint MouseData,
			uint Flags,
			ulong TimeMs,
			ulong ExtraInfo,
			uint DeviceId,
			int DeltaX,
			int DeltaY);

		internal readonly record struct HookEvent(
			ulong EventId,
			HookType HookType,
			KeyboardHookEvent Keyboard,
			MouseHookEvent Mouse);

		internal readonly record struct HookQuarantine(
			HookType HookType,
			uint Reason,
			ulong EventId,
			uint Generation,
			uint StrikeCount,
			uint RetryAfterMs);

		internal readonly record struct PointerPosition(
			int X,
			int Y,
			int XMin,
			int XMax,
			int YMin,
			int YMax);

		internal readonly record struct KeyStateSnapshot(
			uint ModifiersLR,
			bool CapsLock,
			bool NumLock,
			bool ScrollLock,
			byte[] LogicalKeys,
			byte[] PhysicalKeys);

		private readonly Socket socket;
		private readonly ConnectionRole connectionRole;
		private readonly Lock sendLock = new();
		// One thread reads a HookStream. The monitor is reentrant, so synchronous
		// nested callbacks can issue their own request without admitting another reader.
		private readonly object responseGate = new();
		// Response demultiplexing: which (type, correlation) pairs someone is still
		// waiting for, and frames already read on their behalf by another waiter.
		// Both are held under dispatchLock, never under responseGate -- a hook pump
		// parks itself inside responseGate for as long as the daemon stays idle.
		private readonly Lock dispatchLock = new();
		private readonly List<(MessageType Type, ulong CorrelationId)> awaitedResponses = [];
		private readonly List<Frame> deferredResponses = [];
		private Action<KeysharpInputdClient, HookEvent> nestedHookEventHandler;
		private Action<HookQuarantine> hookQuarantineHandler;
		private Timer leaseHeartbeatTimer;
		// Null renews unconditionally; false stops renewing the daemon grab lease.
		private volatile Func<bool> leaseLivenessProbe;
		private long nextCorrelationId;
		private bool disposed;
		private readonly int requestTimeoutMs;

		private KeysharpInputdClient(Socket socket, int requestTimeoutMs, ConnectionRole role)
		{
			this.socket = socket;
			this.requestTimeoutMs = requestTimeoutMs;
			connectionRole = role;

			if (requestTimeoutMs > 0)
			{
				socket.ReceiveTimeout = requestTimeoutMs;
				socket.SendTimeout = requestTimeoutMs;
			}
		}

		/// <summary>
		/// Installs the synchronous callback pump used by a HookStream while it waits
		/// for a hook-originated synthesis result.
		/// </summary>
		internal void SetNestedHookEventHandler(Action<KeysharpInputdClient, HookEvent> handler)
			=> Volatile.Write(ref nestedHookEventHandler, handler);

		internal void SetHookQuarantineHandler(Action<HookQuarantine> handler)
			=> hookQuarantineHandler = handler;

		internal sealed class RequestFailedException : IOException
		{
			internal MessageType RequestType { get; }
			internal int Status { get; }
			internal uint Detail { get; }

			internal RequestFailedException(MessageType requestType, int status, uint detail)
				: base($"keysharp-inputd request {requestType} failed with status {status}, detail {detail}.")
			{
				RequestType = requestType;
				Status = status;
				Detail = detail;
			}
		}

		internal static bool IsStaleHookDecisionFailure(Exception exception)
			=> exception is RequestFailedException
			{
				RequestType: MessageType.HookDecision,
				Detail: (uint)HookDecisionDetail.StaleOrWrongResponder
			};

		internal Capabilities GrantedCapabilities { get; private set; }

		/// <summary>
		/// Cheap, non-blocking liveness probe -- a single Poll() syscall, no
		/// actual request. False once the socket is disposed or the daemon has
		/// already closed/reset the connection (e.g. it crashed or restarted).
		/// A live, idle connection is never reported readable, so this cannot
		/// false-negative on a healthy connection; it CAN still false-positive
		/// right up until the daemon actually dies (this is a snapshot, not a
		/// guarantee), so callers must still handle a transport exception on
		/// the next real request either way -- this only catches the common
		/// case of a cached connection that is already known-dead before that
		/// request is even attempted, letting the caller reconnect
		/// transparently instead of surfacing an avoidable exception.
		/// </summary>
		internal bool IsConnected
		{
			get
			{
				if (disposed)
					return false;

				try
				{
					return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
				}
				catch (ObjectDisposedException)
				{
					return false;
				}
				catch (SocketException)
				{
					return false;
				}
			}
		}

		internal static string DefaultSocketPath
		{
			get
			{
				var configured = Environment.GetEnvironmentVariable(SocketEnvironmentVariable);

				if (!string.IsNullOrWhiteSpace(configured))
					return configured;

				return DefaultSocketPathValue;
			}
		}

		internal static KeysharpInputdClient Connect(
			Capabilities requested = Capabilities.None,
			string socketPath = null,
			int requestTimeoutMs = DefaultRequestTimeoutMs,
			ConnectionRole role = ConnectionRole.GeneralRpc)
		{
			var endpoint = new UnixDomainSocketEndPoint(socketPath ?? DefaultSocketPath);
			var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

			try
			{
				if (requestTimeoutMs > 0)
				{
					using var cancellation = new CancellationTokenSource(requestTimeoutMs);

					try
					{
						socket.ConnectAsync(endpoint, cancellation.Token).AsTask().GetAwaiter().GetResult();
					}
					catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
					{
						throw new SocketException((int)SocketError.TimedOut);
					}
				}
				else
					socket.Connect(endpoint);

				var client = new KeysharpInputdClient(socket, requestTimeoutMs, role);
				client.RequestCapabilities(requested);
				return client;
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		internal void SendHeartbeat()
		{
			SendStatusRequest(MessageType.Heartbeat);
		}

		internal BlockInputMask SetBlockInput(BlockInputMask mask)
		{
			Span<byte> payload = stackalloc byte[8];
			BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)mask);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], 0);

			var applied = (BlockInputMask)SendStatusRequest(MessageType.SetBlockInput, payload);

			if (applied != BlockInputMask.None)
				EnsureLeaseHeartbeat();

			return applied;
		}

		internal uint SubscribeHook(HookType hookType)
		{
			Span<byte> payload = stackalloc byte[8];
			BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)hookType);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], 0);

			var subscriptions = SendStatusRequest(MessageType.SubscribeHook, payload);

			if (subscriptions != 0)
				EnsureLeaseHeartbeat();

			return subscriptions;
		}

		internal uint UnsubscribeHook(HookType hookType)
		{
			Span<byte> payload = stackalloc byte[8];
			BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)hookType);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], 0);

			return SendStatusRequest(MessageType.UnsubscribeHook, payload);
		}

		[Flags]
		internal enum SynthFlags : uint
		{
			None = 0,
			BypassHook = 0x00000001,  // mirrors KSI_SYNTH_FLAG_BYPASS_HOOK
		}

		internal void SendInput(
			IReadOnlyList<Input> inputs,
			SynthFlags flags = SynthFlags.None,
			ulong parentHookEventId = 0u)
		{
			if (inputs == null)
				throw new ArgumentNullException(nameof(inputs));
			if (inputs.Count > MaxInputsPerRequest)
				throw new ArgumentOutOfRangeException(nameof(inputs));
			if ((connectionRole == ConnectionRole.HookStream) != (parentHookEventId != 0u))
				throw new InvalidOperationException(
					"HookStream synthesis requires its current parent event; ordinary synthesis must use GeneralRpc.");

			var payload = new byte[8 + (inputs.Count * InputSize)];
			BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)inputs.Count);
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), (uint)flags);

			for (var i = 0; i < inputs.Count; i++)
				WriteInput(payload.AsSpan(8 + (i * InputSize), InputSize), inputs[i]);

			// On a HookStream the request/response correlation is also the asserted
			// parent callback. Requests are synchronous, so siblings may reuse it.
			SendStatusRequest(MessageType.SynthesizeInput, MessageType.SynthesisResult,
				payload, parentHookEventId == 0u ? null : parentHookEventId);
		}

		/// <summary>Queries the daemon for current lock-key indicator state.</summary>
		internal (bool CapsLock, bool NumLock, bool ScrollLock) GetIndicatorState()
		{
			var response = SendRequest(MessageType.GetIndicatorState, MessageType.IndicatorStateResult);

			if (response.Payload.Length < 3)
				return (false, false, false);

			return (response.Payload[0] != 0, response.Payload[1] != 0, response.Payload[2] != 0);
		}

		/// <summary>Queries logical/toggle keyboard state plus optional key bitmaps.</summary>
		internal KeyStateSnapshot QueryKeyState()
		{
			var response = SendRequest(MessageType.GetKeyState, MessageType.KeyStateResult);

			if (response.Payload.Length < 8)
				return new(0u, false, false, false, [], []);

			var mods = BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(0));
			var logicalKeys = System.Array.Empty<byte>();
			var physicalKeys = System.Array.Empty<byte>();

			if (response.Payload.Length >= 8 + KeyStateBitmapBytes)
			{
				logicalKeys = new byte[KeyStateBitmapBytes];
				response.Payload.AsSpan(8, KeyStateBitmapBytes).CopyTo(logicalKeys);
			}

			if (response.Payload.Length >= 8 + (KeyStateBitmapBytes * 2))
			{
				physicalKeys = new byte[KeyStateBitmapBytes];
				response.Payload.AsSpan(8 + KeyStateBitmapBytes, KeyStateBitmapBytes).CopyTo(physicalKeys);
			}

			return new(mods, response.Payload[4] != 0, response.Payload[5] != 0, response.Payload[6] != 0, logicalKeys, physicalKeys);
		}

		/// <summary>Queries the last raw evdev ABS_X/ABS_Y pointer sample.</summary>
		internal bool TryGetPointerPosition(out PointerPosition position)
		{
			position = default;

			var response = SendRequest(MessageType.GetPointerPosition, MessageType.PointerPositionResult);

			if (response.Payload.Length != 28 || response.Payload[0] == 0)
				return false;

			position = new PointerPosition(
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(4)),
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(8)),
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(12)),
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(16)),
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(20)),
				BinaryPrimitives.ReadInt32LittleEndian(response.Payload.AsSpan(24)));
			return true;
		}

		internal readonly record struct PointerButtons(uint LogicalButtons, uint PhysicalButtons);

		/// <summary>Queries logical and physical mouse-button masks.</summary>
		internal bool TryGetPointerButtons(out PointerButtons buttons)
		{
			buttons = default;

			var response = SendRequest(MessageType.GetPointerButtons, MessageType.PointerButtonsResult);

			if (response.Payload.Length < 8 || response.Payload[0] == 0)
				return false;

			var compatPhysicalButtons = BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(4));

			if (response.Payload.Length >= 16)
			{
				buttons = new(
					BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(8)),
					BinaryPrimitives.ReadUInt32LittleEndian(response.Payload.AsSpan(12)));
			}
			else
			{
				buttons = new(compatPhysicalButtons, compatPhysicalButtons);
			}

			return true;
		}

		/// <summary>Queries milliseconds since inputd last observed upstream user activity.</summary>
		internal bool TryGetIdleTime(out ulong milliseconds)
		{
			milliseconds = 0;
			var response = SendRequest(MessageType.IdleTime, MessageType.IdleTime);

			if (response.Payload.Length != 16 || response.Payload[0] == 0)
				return false;

			milliseconds = BinaryPrimitives.ReadUInt64LittleEndian(response.Payload.AsSpan(8));
			return true;
		}

		internal HookEvent ReadHookEvent()
		{
			lock (responseGate)
			{
				for (;;)
				{
					var frame = ReadFrame(idleRetry: true);

					if (frame.Type == MessageType.HookEvent)
						return ParseHookEvent(frame);

					if (frame.Type == MessageType.HookQuarantined)
					{
						DispatchHookQuarantine(frame);
						continue;
					}

					// A response belonging to another thread's in-flight request, or the
					// late reply to one that gave up, must not kill the pump.
					DeferOrDropResponse(frame);
				}
			}
		}

		private void DispatchHookQuarantine(Frame frame)
		{
			if (frame.Payload.Length != 32)
				throw new InvalidDataException($"Hook quarantine payload has invalid size {frame.Payload.Length}.");

			hookQuarantineHandler?.Invoke(new HookQuarantine(
				(HookType)BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload),
				BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4)),
				BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload.AsSpan(8)),
				BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(16)),
				BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(20)),
				BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(24))));
		}

		private static HookEvent ParseHookEvent(Frame frame)
		{
			if (frame.Payload.Length < 16)
				throw new InvalidDataException("Hook event payload is too small.");

			var eventId = BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload);
			var hookType = (HookType)BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload[8..]);

			if (hookType == HookType.KeyboardLowLevel)
			{
				if (frame.Payload.Length != 56)
					throw new InvalidDataException($"Keyboard hook event has invalid size {frame.Payload.Length}.");

				var keyboard = ReadKeyboardHookEvent(frame.Payload[16..]);
				return new HookEvent(eventId, hookType, keyboard, default);
			}

			if (hookType == HookType.MouseLowLevel)
			{
				if (frame.Payload.Length != 72)
					throw new InvalidDataException($"Mouse hook event has invalid size {frame.Payload.Length}.");

				var mouse = ReadMouseHookEvent(frame.Payload[16..]);
				return new HookEvent(eventId, hookType, default, mouse);
			}

			throw new InvalidDataException($"Unknown hook type {hookType}.");
		}

		internal void SendHookDecision(ulong eventId, HookDecision decision, IReadOnlyList<Input> replacementInputs = null)
		{
			var inputCount = replacementInputs?.Count ?? 0;

			if (inputCount > MaxInputsPerRequest)
				throw new ArgumentOutOfRangeException(nameof(replacementInputs));

			var payload = new byte[16 + (inputCount * InputSize)];
			BinaryPrimitives.WriteUInt64LittleEndian(payload, eventId);
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)decision);
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), (uint)inputCount);

			for (var i = 0; i < inputCount; i++)
				WriteInput(payload.AsSpan(16 + (i * InputSize), InputSize), replacementInputs[i]);

			SendStatusRequest(MessageType.HookDecision, MessageType.HookDecision, payload, eventId);
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			leaseHeartbeatTimer?.Dispose();
			leaseHeartbeatTimer = null;
			socket.Dispose();
		}

		internal bool TryRequestCapabilities(Capabilities requested, out int status, bool forcePrompt = false)
			=> TryRequestCapabilities(requested, requested, out status, forcePrompt);

		internal bool TryRequestCapabilities(Capabilities requested, Capabilities requiredFromInputd, out int status, bool forcePrompt = false)
		{
			var hello = ExchangeHello(requested, forcePrompt);
			status = hello.Status;
			GrantedCapabilities |= hello.Granted;
			return status == 0 && (GrantedCapabilities & requiredFromInputd) == requiredFromInputd;
		}

		internal void RequestCapabilities(Capabilities requested, bool forcePrompt = false)
		{
			if (!TryRequestCapabilities(requested, out var status, forcePrompt))
				throw new IOException(
					$"keysharp-inputd hello failed with status {status}. Requested: {requested}. Granted: {GrantedCapabilities}.");
		}

		private (int Status, Capabilities Granted) ExchangeHello(Capabilities requested, bool forcePrompt)
		{
			Span<byte> payload = stackalloc byte[32];
			payload.Clear();
			BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)requested);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], forcePrompt ? ClientHelloFlagForcePrompt : 0);
			BinaryPrimitives.WriteUInt32LittleEndian(payload[8..], (uint)connectionRole);

			var correlationId = NextCorrelationId();
			BeginAwait(MessageType.ClientHello, correlationId);
			Frame response;
			var previousReceiveTimeout = socket.ReceiveTimeout;

			if (requestTimeoutMs > 0)
				socket.ReceiveTimeout = HelloResponseTimeoutMs;

			try
			{
				SendFrame(MessageType.ClientHello, correlationId, payload);
				response = ReadResponseFrame(MessageType.ClientHello, correlationId);
			}
			finally
			{
				EndAwait(MessageType.ClientHello, correlationId);

				if (requestTimeoutMs > 0)
					socket.ReceiveTimeout = previousReceiveTimeout;
			}

			if (response.Payload.Length != 24)
				throw new InvalidDataException($"Unexpected hello response size {response.Payload.Length}.");

			var status = BinaryPrimitives.ReadInt32LittleEndian(response.Payload);
			var granted = (Capabilities)BinaryPrimitives.ReadUInt32LittleEndian(response.Payload[4..]);
			return (status, granted);
		}

		private ulong NextCorrelationId() => unchecked((ulong)Interlocked.Increment(ref nextCorrelationId));

		private uint SendStatusRequest(MessageType type)
			=> SendStatusRequest(type, type, ReadOnlySpan<byte>.Empty);

		private uint SendStatusRequest(MessageType type, ReadOnlySpan<byte> payload)
			=> SendStatusRequest(type, type, payload);

		private uint SendStatusRequest(
			MessageType requestType,
			MessageType responseType,
			ReadOnlySpan<byte> payload,
			ulong? correlationId = null)
		{
			var id = correlationId ?? NextCorrelationId();
			BeginAwait(responseType, id);

			try
			{
				SendFrame(requestType, id, payload);
				return EnsureStatus(ReadResponseFrame(responseType, id), responseType, id);
			}
			finally
			{
				EndAwait(responseType, id);
			}
		}

		private Frame SendRequest(MessageType requestType, MessageType responseType)
			=> SendRequest(requestType, responseType, ReadOnlySpan<byte>.Empty);

		private Frame SendRequest(MessageType requestType, MessageType responseType, ReadOnlySpan<byte> payload)
		{
			var correlationId = NextCorrelationId();
			BeginAwait(responseType, correlationId);

			try
			{
				SendFrame(requestType, correlationId, payload);
				return ReadResponseFrame(responseType, correlationId);
			}
			finally
			{
				EndAwait(responseType, correlationId);
			}
		}

		private void SendFrame(MessageType type, ulong correlationId, ReadOnlySpan<byte> payload)
		{
			lock (sendLock)
			{
				ThrowIfDisposed();

				if (payload.Length > MaxMessageSize - HeaderSize)
					throw new ArgumentOutOfRangeException(nameof(payload));

				Span<byte> header = stackalloc byte[HeaderSize];
				BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(HeaderSize + payload.Length));
				BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)ProtocolMajor);
				BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)ProtocolMinor);
				BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)type);
				BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 0);
				BinaryPrimitives.WriteUInt64LittleEndian(header[16..], correlationId);

				WriteAll(header);
				WriteAll(payload);
			}
		}

		/// <summary>Installs a probe controlling whether periodic grab-lease heartbeats renew.</summary>
		internal void SetLeaseLivenessProbe(Func<bool> probe) => leaseLivenessProbe = probe;

		private void EnsureLeaseHeartbeat()
		{
			if (leaseHeartbeatTimer != null)
				return;

			leaseHeartbeatTimer = new Timer(
				_ =>
				{
					try
					{
						var probe = leaseLivenessProbe;

						if (probe != null && !probe())
							return;

						// Correlation zero is a one-way lease renewal.
						SendFrame(MessageType.Heartbeat, 0, ReadOnlySpan<byte>.Empty);
					}
					catch
					{
						// Normal request/hook paths own recovery and disposal.
					}
				},
				null,
				LeaseHeartbeatPeriodMs,
				LeaseHeartbeatPeriodMs);
		}

		private Frame ReadFrame(bool idleRetry = false)
		{
			ThrowIfDisposed();

			Span<byte> header = stackalloc byte[HeaderSize];
			ReadAll(header, idleRetry);

			var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
			var major = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
			var minor = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);

			if (size < HeaderSize || size > MaxMessageSize)
				throw new InvalidDataException($"Invalid inputd frame size {size}.");

			if (major != ProtocolMajor || minor != ProtocolMinor)
				throw new InvalidDataException($"Unsupported inputd protocol version {major}.{minor}.");

			var payload = new byte[size - HeaderSize];
			ReadAll(payload);

			return new Frame(
				(MessageType)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
				BinaryPrimitives.ReadUInt32LittleEndian(header[12..]),
				BinaryPrimitives.ReadUInt64LittleEndian(header[16..]),
				payload);
		}

		private void ReadAll(Span<byte> buffer, bool idleRetry = false)
		{
			var offset = 0;

			while (offset < buffer.Length)
			{
				if (!socket.Poll(ReceiveTimeoutMicroseconds(), SelectMode.SelectRead))
				{
					if (idleRetry && offset == 0)
						continue;

					throw new SocketException((int)SocketError.TimedOut);
				}

				var read = socket.Receive(buffer[offset..], SocketFlags.None);

				if (read == 0)
					throw new EndOfStreamException("keysharp-inputd disconnected.");

				offset += read;
			}
		}

		private int ReceiveTimeoutMicroseconds()
		{
			var timeoutMs = socket.ReceiveTimeout;

			if (timeoutMs <= 0)
				return -1;

			return timeoutMs >= int.MaxValue / 1000 ? int.MaxValue : timeoutMs * 1000;
		}

		private Frame ReadResponseFrame(MessageType expectedType, ulong expectedCorrelationId)
		{
			lock (responseGate)
			{
				for (;;)
				{
					// An outer request's response can already be queued behind a hook
					// event the daemon emitted first (e.g. a mouse event races the
					// keyboard SubscribeHook ack). The nested callback reads it while
					// waiting for its own reply, so park it for the outer waiter.
					if (TryTakeDeferredResponse(expectedType, expectedCorrelationId, out var deferred))
						return deferred;

					var frame = ReadFrame();

					if (frame.Type == expectedType
						&& frame.CorrelationId == expectedCorrelationId)
						return frame;

					if (frame.Type == MessageType.HookEvent)
					{
						var handler = Volatile.Read(ref nestedHookEventHandler);

						if (connectionRole != ConnectionRole.HookStream || handler == null)
							throw new InvalidDataException("Unexpected recursive hook event on a non-hook connection.");

						handler(this, ParseHookEvent(frame));
						continue;
					}

					if (frame.Type == MessageType.HookQuarantined)
					{
						DispatchHookQuarantine(frame);
						continue;
					}

					DeferOrDropResponse(frame);
				}
			}
		}

		/// <summary>
		/// Parks a response for the waiter it belongs to. Anything nobody is waiting
		/// for is a duplicate or the reply to a request that already gave up, so it
		/// is dropped -- the stream stays in sync either way.
		/// </summary>
		private void DeferOrDropResponse(Frame frame)
		{
			lock (dispatchLock)
			{
				if (awaitedResponses.Contains((frame.Type, frame.CorrelationId)))
					deferredResponses.Add(frame);
			}
		}

		private bool TryTakeDeferredResponse(MessageType expectedType, ulong expectedCorrelationId, out Frame frame)
		{
			lock (dispatchLock)
			{
				for (var i = 0; i < deferredResponses.Count; i++)
				{
					if (deferredResponses[i].Type == expectedType
						&& deferredResponses[i].CorrelationId == expectedCorrelationId)
					{
						frame = deferredResponses[i];
						deferredResponses.RemoveAt(i);
						return true;
					}
				}
			}

			frame = default;
			return false;
		}

		/// <summary>
		/// Publishes a request's response key so a concurrent or nested reader parks
		/// that response instead of discarding it. Every call must be paired with
		/// <see cref="EndAwait"/> so an abandoned request leaves nothing behind.
		/// </summary>
		private void BeginAwait(MessageType responseType, ulong correlationId)
		{
			lock (dispatchLock)
				awaitedResponses.Add((responseType, correlationId));
		}

		private void EndAwait(MessageType responseType, ulong correlationId)
		{
			lock (dispatchLock)
			{
				_ = awaitedResponses.Remove((responseType, correlationId));

				// A request that threw before claiming its parked response (or whose
				// nested callback threw) must not leave that frame behind.
				if (!awaitedResponses.Contains((responseType, correlationId)))
					_ = deferredResponses.RemoveAll(f => f.Type == responseType && f.CorrelationId == correlationId);
			}
		}

		private void WriteAll(ReadOnlySpan<byte> buffer)
		{
			var offset = 0;

			while (offset < buffer.Length)
			{
				var written = socket.Send(buffer[offset..], SocketFlags.None);

				if (written == 0)
					throw new IOException("Failed to write to keysharp-inputd.");

				offset += written;
			}
		}

		private static uint EnsureStatus(Frame frame, MessageType expectedType, ulong expectedCorrelationId)
		{
			if (frame.Type != expectedType || frame.CorrelationId != expectedCorrelationId)
				throw new InvalidDataException($"Unexpected status response type={frame.Type} correlation={frame.CorrelationId}.");

			if (frame.Payload.Length != 8)
				throw new InvalidDataException($"Unexpected status payload size {frame.Payload.Length}.");

			var status = BinaryPrimitives.ReadInt32LittleEndian(frame.Payload);
			var detail = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload[4..]);

			if (status != 0)
				throw new RequestFailedException(expectedType, status, detail);

			return detail;
		}

		private static void WriteInput(Span<byte> destination, Input input)
		{
			BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)input.Type);
			BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], 0);

			if (input.Type == InputType.Keyboard)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], input.Keyboard.Vk);
				BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], input.Keyboard.Scan);
				BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], (uint)input.Keyboard.Flags);
				BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], input.Keyboard.Time);
				BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], input.Keyboard.ExtraInfo);
			}
			else if (input.Type == InputType.Mouse)
			{
				BinaryPrimitives.WriteInt32LittleEndian(destination[8..], input.Mouse.Dx);
				BinaryPrimitives.WriteInt32LittleEndian(destination[12..], input.Mouse.Dy);
				BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], input.Mouse.MouseData);
				BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], (uint)input.Mouse.Flags);
				BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], input.Mouse.Time);
				BinaryPrimitives.WriteUInt64LittleEndian(destination[32..], input.Mouse.ExtraInfo);
			}
			else
			{
				throw new NotSupportedException($"Input type {input.Type} is not supported.");
			}
		}

		private static KeyboardHookEvent ReadKeyboardHookEvent(ReadOnlySpan<byte> payload)
			=> new(
				BinaryPrimitives.ReadUInt32LittleEndian(payload),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]),
				BinaryPrimitives.ReadUInt64LittleEndian(payload[16..]),
				BinaryPrimitives.ReadUInt64LittleEndian(payload[24..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]));

		private static MouseHookEvent ReadMouseHookEvent(ReadOnlySpan<byte> payload)
			=> new(
				BinaryPrimitives.ReadUInt32LittleEndian(payload),
				BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
				BinaryPrimitives.ReadInt32LittleEndian(payload[8..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[16..]),
				BinaryPrimitives.ReadUInt64LittleEndian(payload[24..]),
				BinaryPrimitives.ReadUInt64LittleEndian(payload[32..]),
				BinaryPrimitives.ReadUInt32LittleEndian(payload[40..]),
				BinaryPrimitives.ReadInt32LittleEndian(payload[48..]),
				BinaryPrimitives.ReadInt32LittleEndian(payload[52..]));

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(KeysharpInputdClient));
		}

		private readonly record struct Frame(MessageType Type, uint ClientId, ulong CorrelationId, byte[] Payload);
	}
}
#endif
