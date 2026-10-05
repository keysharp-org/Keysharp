#if LINUX
using System.Threading;
using Keysharp.Builtins;
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Input.Linux
{
	internal sealed class KeysharpInputManager : IDisposable
	{
		internal LinuxPermissions Permissions { get; set; }

		internal static KeysharpInputManager Current => Script.TheScript.LinuxServices.Input;

		private readonly Script owner;

		internal KeysharpInputManager(Script owner = null)
		{
			this.owner = owner;
			queries = new(this, "query");
			sends = new(this, "synthesis");
		}

		private readonly Lock gate = new();
		private readonly Lock authorizationGate = new();
		private readonly Lock blockRequests = new();
		private bool clientsStopped;
		private long lifecycleVersion;
		private readonly RetryGate connectionRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));

		// Hook callbacks use their callback connection; other calls share these. A Send waits for every hook,
		// so it has its own, leaving queries free for the #HotIf criteria a hook may wait on meanwhile.
		private readonly SharedConnection queries, sends;
		private readonly Lazy<BlockingCollection<Action>> mainThreadSends = new(StartSendThread);
		// Volatile lets reachability checks avoid blocking behind a prompt.
		private volatile KeysharpInputClient client;
		internal KeysharpInputClient AuthorizationLease => client;
		internal bool WaitForSynthesisState() => client?.WaitForSynthesisState() == true;
		private KeysharpInputClient blockClient;
		private Script blockOwner;
		private KeysharpInputClient.BlockInputMask appliedBlockMask;

		internal uint SendInputViaSynthesisChannel(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags = KeysharpInputClient.SynthFlags.None)
		{
			try
			{
				return SendInputOnce(inputs, flags);
			}
			// The cached grant was revoked: ask for it again, as a check before each Send would,
			// and send once more if it is back. A hook callback cannot wait on a prompt.
			catch (NativeClientException ex)
				when (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked
					&& !Keysharp.Internals.Input.Hooks.HookThread.InHookCallback)
			{
				RefreshLeaseState();

				if (!Permissions.EnsureInputControl(operation: "input synthesis").IsGranted)
					throw;

				return SendInputOnce(inputs, flags);
			}
		}

		private uint SendInputOnce(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags)
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
			{
				EnsureSynthesisCapabilityNoPrompt(hookClient, inputs, "hook");
				return hookClient.SendInput(inputs, flags,
					Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookEventId);
			}

			// This process's own hook sees the input before the Send returns, and may need the main thread
			// meanwhile, as a #HotIf criterion reading a control does. The hook thread sends only inside its
			// callbacks, above, since a Send from it would wait on its own hook.
			var script = owner;

			if ((flags & KeysharpInputClient.SynthFlags.BypassHook) == 0
				&& script is { IsOnMainThread: true } && script.HookThread?.HasEitherHook() == true)
				return SendServicingMainThread(inputs, flags);
			else
				return SendOnSynthesisChannel(inputs, flags);
		}

		private uint SendOnSynthesisChannel(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags)
		{
			uint modifiers = 0;
			if (!sends.TryUse(sc =>
			{
				EnsureSynthesisCapabilityNoPrompt(sc, inputs, "synthesis");
				modifiers = sc.SendInput(inputs, flags);
				return true;
			}))
				throw new InvalidOperationException("keysharp-input synthesis channel is unavailable.");
			return modifiers;
		}

		/// <summary>
		/// Makes a Send on the Send thread, started by the first such Send, while the main thread waits the way
		/// Windows' keybd_event does, serving what a hook may wait on without starting new threads.
		/// </summary>
		private uint SendServicingMainThread(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags)
		{
			using var done = new ManualResetEventSlim();
			uint modifiers = 0;
			Exception error = null;
			mainThreadSends.Value.Add(() =>
			{
				try
				{
					modifiers = SendOnSynthesisChannel(inputs, flags);
				}
				catch (Exception ex)
				{
					error = ex;
				}

				done.Set();
			});

			while (!done.Wait(1))
				Keysharp.Internals.Flow.SleepWithoutInterruption(-1);

			if (error != null)
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
			return modifiers;
		}

		// A dedicated thread, because a busy thread pool can be slow to add one.
		private static BlockingCollection<Action> StartSendThread()
		{
			var queue = new BlockingCollection<Action>();
			new Thread(() =>
			{
				foreach (var send in queue.GetConsumingEnumerable())
					send();
			})
			{ IsBackground = true, Name = "keysharp-input Send" }.Start();
			return queue;
		}

		private static void EnsureSynthesisCapabilityNoPrompt(KeysharpInputClient connectedClient,
			IReadOnlyList<KeysharpInputClient.Input> inputs, string channel)
		{
			var synthesis = KeysharpInputClient.RequiredSynthesisOperations(inputs);

			if (synthesis != KeysharpInputClient.Operations.None
				&& !EnsureQueryCapabilityNoPrompt(connectedClient, synthesis))
				throw new InvalidOperationException(
					$"keysharp-input {channel} channel does not hold the InputControl grant required for synthesis.");
		}

		internal bool TryGetModifierState(
			out uint logicalModifiersLR,
			out uint physicalModifiersLR,
			out bool capsLock,
			out bool numLock,
			out bool scrollLock)
		{
			logicalModifiersLR = 0;
			physicalModifiersLR = 0;
			capsLock = false;
			numLock = false;
			scrollLock = false;

			if (!TryReadKeyboardSnapshot(out var state, out physicalModifiersLR))
				return false;

			logicalModifiersLR = state.ModifiersLR;
			capsLock = state.CapsLock;
			numLock = state.NumLock;
			scrollLock = state.ScrollLock;
			return true;
		}

		private bool TryReadKeyboardSnapshot(out KeysharpInputClient.KeyStateSnapshot state,
			out uint physicalModifiers, bool requireMonitoring = false)
		{
			state = default;
			physicalModifiers = 0;
			var connected = client;
			if (connected?.IsConnected == true)
				return ReadKeyboardSnapshot(connected, out state, out physicalModifiers, requireMonitoring);
			if (Keysharp.Internals.Input.Hooks.HookThread.InHookCallback) return false;
			lock (authorizationGate)
			{
				if (!TryEnsureConnected("keyboard state", out _, out _)) return false;
				connected = client;
				return ReadKeyboardSnapshot(connected, out state, out physicalModifiers, requireMonitoring);
			}
		}

		private bool ReadKeyboardSnapshot(KeysharpInputClient connected, out KeysharpInputClient.KeyStateSnapshot state,
			out uint physicalModifiers, bool requireMonitoring)
		{
			state = default;
			physicalModifiers = 0;
			return connected != null && (!requireMonitoring || (connected.GrantedScopes & LinuxPermissionScope.InputMonitoring) != 0)
				&& connected.TryGetKeyStateSnapshot(out state, out physicalModifiers) && IsCurrentLease(connected);
		}

		/// <summary>
		/// Queries the compositor-independent idle counter without requesting a grant.
		/// </summary>
		internal bool TryGetIdleTime(out long milliseconds)
		{
			milliseconds = 0;

			ulong captured = 0;

			try
			{
				if (!TryUseQueryClient(qc => qc.TryGetIdleTime(out captured)))
					return false;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"keysharp-input: idle time query failed: {ex.Message}");
				return false;
			}

			milliseconds = captured > long.MaxValue ? long.MaxValue : (long)captured;
			return true;
		}

		/// <summary>Queries logical and physical keyboard state. This full bitmap is input-monitoring data.</summary>
		internal bool TryGetKeyState(out uint modifiersLR, out bool capsLock, out bool numLock, out bool scrollLock,
			out KeysharpInputClient.KeyStateBitmap logicalKeys, out KeysharpInputClient.KeyStateBitmap physicalKeys, uint deviceID = 0)
		{
			modifiersLR = 0;
			capsLock = false;
			numLock = false;
			scrollLock = false;
			logicalKeys = default;
			physicalKeys = default;

			// Full key state is monitoring data. Use an existing grant and never prompt here.
			KeysharpInputClient.KeyStateSnapshot state;
			if (deviceID == 0)
			{
				if (!TryReadKeyboardSnapshot(out state, out _, requireMonitoring: true))
					return false;
			}
			else if (!TryQuery(KeysharpInputClient.Operations.QueryKeyState,
				qc => qc.TryQueryKeyState(deviceID, out var value) ? (true, value) : (false, default),
				out state, "device key state query")) return false;

			modifiersLR = state.ModifiersLR;
			capsLock = state.CapsLock;
			numLock = state.NumLock;
			scrollLock = state.ScrollLock;
			logicalKeys = state.LogicalKeys;
			physicalKeys = state.PhysicalKeys;
			return true;
		}

		/// <summary>Enumerates connected gamepads. Gamepad access is ungated, so this needs no grant
		/// and never prompts.</summary>
		internal bool TryListGamepads(out List<KeysharpInputClient.GamepadInfo> gamepads,
			out ulong generation)
		{
			gamepads = null;
			ulong captured = 0;
			List<KeysharpInputClient.GamepadInfo> captureList = null;

			try
			{
				if (!TryUseQueryClient(qc =>
				{
					captureList = qc.ListGamepads(out captured);
					return true;
				}))
				{
					generation = 0;
					return false;
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"keysharp-input: gamepad list failed: {ex.Message}");
				generation = 0;
				return false;
			}

			gamepads = captureList;
			generation = captured;
			return true;
		}

		/// <summary>Reads one gamepad's buttons and axes, as taken at <paramref name="generation"/>.</summary>
		internal bool TryGetGamepadState(uint deviceId, ulong generation,
			out KeysharpInputClient.GamepadState state)
		{
			var captured = default(KeysharpInputClient.GamepadState);
			state = default;

			try
			{
				if (!TryUseQueryClient(qc => qc.TryGetGamepadState(deviceId, generation, out captured)))
					return false;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"keysharp-input: gamepad state query failed: {ex.Message}");
				return false;
			}

			state = captured;
			return true;
		}

		internal bool TryGetPointerPosition(
			out int x,
			out int y,
			out int xMin,
			out int xMax,
			out int yMin,
			out int yMax)
		{
			x = y = xMin = xMax = yMin = yMax = 0;

			if (!TryQuery(
					KeysharpInputClient.Operations.QueryPointerPosition,
					qc => qc.TryGetPointerPosition(out var position) ? (true, position) : (false, default),
					out KeysharpInputClient.PointerPosition position))
				return false;

			x = position.X;
			y = position.Y;
			xMin = position.XMin;
			xMax = position.XMax;
			yMin = position.YMin;
			yMax = position.YMax;
			return true;
		}

		/// <summary>Live logical state of one mouse button (Wayland path for GetKeyState(button)).</summary>
		internal bool TryGetButtonStateLogical(uint vk, out bool down)
			=> TryQueryButtonState(vk, physical: false, out down);

		/// <summary>Live physical state of one mouse button.</summary>
		internal bool TryGetButtonStatePhysical(uint vk, out bool down)
			=> TryQueryButtonState(vk, physical: true, out down);

		private bool TryQueryButtonState(uint vk, bool physical, out bool down)
		{
			down = false;

			var bit = vk switch
			{
				0x01u => 1u << 0, // VK_LBUTTON
				0x02u => 1u << 1, // VK_RBUTTON
				0x04u => 1u << 2, // VK_MBUTTON
				0x05u => 1u << 3, // VK_XBUTTON1
				0x06u => 1u << 4, // VK_XBUTTON2
				_ => 0u
			};

			if (bit == 0)
				return false;

			if (!TryQuery(
					KeysharpInputClient.Operations.QueryPointerButtons,
					qc => qc.TryGetPointerButtons(out var buttons) ? (true, buttons) : (false, default),
					out KeysharpInputClient.PointerButtons buttons))
				return false;

			var buttonsMask = physical ? buttons.PhysicalButtons : buttons.LogicalButtons;
			down = (buttonsMask & bit) != 0;
			return true;
		}

		private bool TryQuery<T>(
			KeysharpInputClient.Operations required,
			Func<KeysharpInputClient, (bool Success, T Value)> query,
			out T value,
			string failureContext = null)
		{
			value = default;
			var captured = default(T);
			var permissionDenied = false;

			try
			{
				var success = TryUseQueryClient(qc =>
				{
					if (!EnsureQueryCapabilityNoPrompt(qc, required))
					{
						permissionDenied = true;
						return false;
					}

					var result = query(qc);

					if (!result.Success)
						return false;

					captured = result.Value;
					return true;
				});

				if (permissionDenied)
					RefreshLeaseState();

				if (!success)
					return false;

				value = captured;
				return true;
			}
			catch (NativeClientException ex)
				when (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked)
			{
				RefreshLeaseState();

				if (failureContext != null)
					Diagnostics.Debug.WriteLine($"keysharp-input: {failureContext} failed: {ex.Message}");

				return false;
			}
			catch (Exception ex) when (ex is not DeviceKeyStateUnsupportedException)
			{
				if (failureContext != null)
					Diagnostics.Debug.WriteLine($"keysharp-input: {failureContext} failed: {ex.Message}");

				return false;
			}
		}

		private void RefreshLeaseState()
		{
			lock (authorizationGate)
			{
				var lease = client;
				try { lease?.RefreshLeaseState(); }
				catch (Exception exception) when (IsTransportException(exception))
				{
					Diagnostics.Debug.WriteLine($"keysharp-input lease ended: {exception.Message}");
					HandleConnectionLost(lease);
				}
			}
		}

		private bool TryUseQueryClient(Func<KeysharpInputClient, bool> action)
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient == null)
				return queries.TryUse(action);

			try
			{
				return action(hookClient);
			}
			catch (Exception ex) when (IsTransportException(ex))
			{
				Diagnostics.Debug.WriteLine($"keysharp-input hook channel lost: {ex.Message}");
				return false;
			}
		}

		internal bool TrySetBlockInput(Script owner,
			KeysharpInputClient.BlockInputMask mask, out string message)
		{
			ArgumentNullException.ThrowIfNull(owner);

			if (mask != KeysharpInputClient.BlockInputMask.None)
			{
				var permission = EnsureOperations(KeysharpInputClient.Operations.BlockInput,
					"block input", checkOnly: Script.IsHeadless);

				if (!permission.IsGranted)
				{
					message = permission.Message;
					return false;
				}
			}

			lock (blockRequests)
			{
				KeysharpInputClient request, lease;
				lock (gate)
				{
					if (!ReferenceEquals(this.owner, owner) || clientsStopped)
					{
						message = "The script owning this BlockInput request has already stopped.";
						return false;
					}
					if (mask == appliedBlockMask && (mask == KeysharpInputClient.BlockInputMask.None || blockClient != null))
					{
						blockOwner = mask == KeysharpInputClient.BlockInputMask.None ? null : owner;
						message = string.Empty;
						return true;
					}
					lease = client;
					request = mask == KeysharpInputClient.BlockInputMask.None ? StopBlockClientLocked() : blockClient;
					if (mask == KeysharpInputClient.BlockInputMask.None) ClearManagedBlockStateLocked();
				}
				if (mask == KeysharpInputClient.BlockInputMask.None)
				{
					request?.Dispose();
					message = string.Empty;
					return true;
				}
				var refreshLease = false;
				try
				{
					request ??= KeysharpInputClient.Connect(KeysharpInputClient.Operations.BlockInput,
						role: KeysharpInputClient.ConnectionRole.Rpc, lease: lease);
					lock (gate)
					{
						if (!ReferenceEquals(this.owner, owner) || clientsStopped || !ReferenceEquals(client, lease))
							throw new ObjectDisposedException("keysharp-input session");
						blockClient = request;
						blockOwner = owner;
					}
					var granted = request.SetBlockInput(mask);
					lock (gate)
					{
						if (!ReferenceEquals(blockClient, request) || !ReferenceEquals(this.owner, owner) || !ReferenceEquals(client, lease))
							throw new ObjectDisposedException("keysharp-input session");
						if (granted != mask)
							throw new InvalidDataException($"keysharp-input granted {granted}, but {mask} was requested.");
						appliedBlockMask = granted;
					}
					message = string.Empty;
					return true;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					message = ex.Message;
				}
				catch (NativeClientException ex)
				{
					if (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked)
						refreshLease = true;
					message = ex.Message;
				}
				lock (gate)
					if (ReferenceEquals(blockClient, request))
					{
						_ = StopBlockClientLocked();
						ClearManagedBlockStateLocked();
					}
				request?.Dispose();
				if (refreshLease) RefreshLeaseState();
				return false;
			}
		}

		private KeysharpInputClient StopBlockClientLocked()
		{
			var retired = blockClient;
			blockClient = null;
			appliedBlockMask = KeysharpInputClient.BlockInputMask.None;
			return retired;
		}

		private void LeaseStateChanged(KeysharpInputClient source)
		{
			if (!ReferenceEquals(client, source) || source.IsConnected
				&& (source.GrantedScopes & LinuxPermissionScope.InputControl) != 0) return;
			KeysharpInputClient retired = null;
			lock (gate)
				if (ReferenceEquals(client, source))
				{
					retired = StopBlockClientLocked();
					ClearManagedBlockStateLocked();
				}
			retired?.Dispose();
		}

		private void ClearManagedBlockStateLocked()
		{
			if (blockOwner != null)
				blockOwner.KeyboardData.blockInput = false;

			blockOwner = null;
			appliedBlockMask = KeysharpInputClient.BlockInputMask.None;
		}

		/// <summary>
		/// Allocation-, lock-, and IPC-free check for an already granted operation.
		/// A reconnect race only sends one call through the locked request path.
		/// </summary>
		internal bool HasInputOperation(KeysharpInputClient.Operations required)
		{
			var c = client;
			return c != null && c.HasOperations(required);
		}

		internal PermissionResult EnsureOperations(KeysharpInputClient.Operations required,
			string operation = null, bool forcePrompt = false, bool checkOnly = false)
			=> Permissions.RequestInputOperations(required,
				operation ?? "input automation", !checkOnly && !Script.IsHeadless, forcePrompt);

		internal KeysharpInputClient GetAuthorizationLease(string operation, bool rearm, out PermissionResult failure)
		{
			lock (authorizationGate)
			{
				if (rearm) connectionRetries.Rearm();
				if (!TryEnsureConnected(operation, out var status, out var message))
				{
					failure = new(status, message);
					return null;
				}
				failure = default;
				return client;
			}
		}

		private bool IsCurrentLease(KeysharpInputClient lease)
		{
			lock (gate) return !clientsStopped && lease is { IsConnected: true } && ReferenceEquals(client, lease);
		}

		private bool TryEnsureConnected(string operation, out PermissionStatus status, out string message)
		{
			var lease = client;
			if (lease != null)
			{
				if (IsCurrentLease(lease))
				{
					status = PermissionStatus.Granted;
					message = string.Empty;
					return true;
				}

				// Discard a closed cached connection before attempting a fresh handshake.
				HandleConnectionLost(lease);
			}

			return TryConnect(operation, out status, out message);
		}

		private bool TryConnect(string operation, out PermissionStatus status, out string message)
		{
			long version;
			lock (gate)
			{
				if (clientsStopped)
				{
					status = PermissionStatus.Unsupported;
					message = "The keysharp-input session has stopped.";
					return false;
				}
				version = lifecycleVersion;
			}
			using var attempt = connectionRetries.TryBegin();

			if (attempt == null)
			{
				status = PermissionStatus.Unsupported;
				message = $"keysharp-input is unavailable and its {connectionRetries.FailureCount}-attempt reconnect burst " +
					$"has stopped for this run. An explicit permission request or a detected connection loss will rearm it.";
				return false;
			}

			KeysharpInputClient candidate = null;
			try
			{
				candidate = KeysharpInputClient.Connect(
					requestTimeoutMs: KeysharpInputClient.AuthorizationTimeoutMs,
					role: KeysharpInputClient.ConnectionRole.Lease);
				candidate.SetLeaseStateHandler(LeaseStateChanged);
				lock (gate)
				{
					if (clientsStopped || lifecycleVersion != version)
						throw new ObjectDisposedException("keysharp-input session");
					client = candidate;
				}
				attempt.Succeed();
				queries.Rearm();
				sends.Rearm();
				status = PermissionStatus.Granted;
				message = string.Empty;

				return true;
			}
			catch (Exception ex) when (IsConnectException(ex))
			{
				if (candidate != null) DisposeClient(candidate);
				candidate?.Dispose();
				attempt.Fail(ex);
				status = PermissionStatus.Unsupported;
				message = $"keysharp-input is not installed or not available at '{KeysharpInputClient.DefaultSocketPath}'. " +
					$"Install keysharp-input 1.x (client ABI 1.0, libkeysharp-input.so.1) to use '{operation ?? "input automation"}'. Details: {ex.Message}";
				return false;
			}
		}

		private static bool IsConnectException(Exception ex)
			=> ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
				or ObjectDisposedException or InvalidDataException
				|| ex is NativeClientException native
					&& native.Status is NativeClientStatus.Unavailable
						or NativeClientStatus.Unsupported
						or NativeClientStatus.Timeout or NativeClientStatus.Internal;

		internal static bool IsTransportException(Exception ex)
			=> IsConnectException(ex) || ex is EndOfStreamException
				|| ex is NativeClientException { Status: NativeClientStatus.Cancelled };

		// Queries may use a persistent grant but never prompt.
		private static bool EnsureQueryCapabilityNoPrompt(KeysharpInputClient qc, KeysharpInputClient.Operations required)
		{
			return qc.IsConnected && qc.HasOperations(required);
		}

		private void DisposeClient(KeysharpInputClient expected)
		{
			KeysharpInputClient retired, block;
			lock (gate)
			{
				if (!ReferenceEquals(client, expected)) return;
				retired = client;
				client = null;
				lifecycleVersion++;
				block = StopBlockClientLocked();
				ClearManagedBlockStateLocked();
			}
			RetireClients(retired, block);
		}

		private void RetireClients(KeysharpInputClient lease, KeysharpInputClient block)
		{
			try { lease?.Dispose(); } catch { }
			try { block?.Dispose(); } catch { }
			queries.Dispose(lease);
			sends.Dispose(lease);
		}

		internal void HandleConnectionLost(KeysharpInputClient expected)
		{
			DisposeClient(expected);
			connectionRetries.Rearm();
		}

		public void Dispose()
		{
			KeysharpInputClient retired, block;
			lock (gate)
			{
				if (clientsStopped) return;
				clientsStopped = true;
				lifecycleVersion++;
				retired = client;
				client = null;
				block = StopBlockClientLocked();
				ClearManagedBlockStateLocked();
			}
			RetireClients(retired, block);
			if (mainThreadSends.IsValueCreated) mainThreadSends.Value.CompleteAdding();
		}

		/// <summary>A connection threads share one request at a time, reconnecting in bursts after a loss.</summary>
		private sealed class SharedConnection(KeysharpInputManager service, string channel)
		{
			private readonly Lock gate = new();
			private readonly RetryGate retries = new(maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(100), maximumRetryDelay: TimeSpan.FromSeconds(1));
			private KeysharpInputClient current;
			private long version;

			internal bool TryUse(Func<KeysharpInputClient, bool> action)
				=> TryUse(action, static (client, use) => use(client));

			// The state form lets a hot caller pass what it uses without a closure.
			internal bool TryUse<TState>(TState state, Func<KeysharpInputClient, TState, bool> action)
			{
				var lease = service.client;
				if (!service.IsCurrentLease(lease))
				{
					lock (service.authorizationGate)
					{
						if (!service.TryEnsureConnected($"{channel} channel", out _, out _)) return false;
						lease = service.client;
					}
				}
				if (!service.IsCurrentLease(lease)) return false;
				KeysharpInputClient connection, retired = null;
				long revision;
				lock (gate)
				{
					revision = version;
					connection = current;
					if (connection is not { IsConnected: true } || connection.LeaseId != lease.LeaseId)
					{
						retired = connection;
						connection = current = null;
					}
				}
				retired?.Dispose();
				if (connection == null)
				{
					using var attempt = retries.TryBegin();
					if (attempt == null) return false;
					try
					{
						connection = KeysharpInputClient.Connect(role: KeysharpInputClient.ConnectionRole.Rpc, lease: lease);
						lock (gate)
						{
							if (version != revision || !service.IsCurrentLease(lease))
								throw new ObjectDisposedException("keysharp-input session");
							current = connection;
						}
						attempt.Succeed();
					}
					catch (Exception ex) when (IsConnectException(ex))
					{
						connection?.Dispose();
						attempt.Fail(ex);
						return false;
					}
				}
				try
				{
					var result = action(connection, state);
					return service.IsCurrentLease(lease) && result;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-input {channel} channel lost: {ex.Message}");
					lock (gate)
						if (ReferenceEquals(current, connection)) { current = null; version++; }
					connection.Dispose();
					retries.Rearm();
					return false;
				}
			}

			internal void Rearm() => retries.Rearm();

			internal void Dispose(KeysharpInputClient lease)
			{
				if (lease == null) return;
				KeysharpInputClient retired;
				lock (gate)
				{
					if (current != null && current.LeaseId != lease.LeaseId) return;
					version++;
					retired = current;
					current = null;
				}
				try { retired?.Dispose(); } catch { }
				retries.Rearm();
			}
		}

	}
}
#endif
