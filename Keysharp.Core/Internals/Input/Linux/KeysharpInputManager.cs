#if LINUX
using System.Threading;
using Keysharp.Builtins;
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Input.Linux
{
	internal static class KeysharpInputManager
	{
		private static readonly Lock gate = new();
		private static readonly Lock authorizationGate = new();
		private static readonly Lock queryGate = new();
		private static readonly HashSet<Script> owners = new();
		private static readonly RetryGate connectionRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static readonly RetryGate queryRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(100), maximumRetryDelay: TimeSpan.FromSeconds(1));

		// Hook callbacks use their callback connection; other calls share queryClient.
		// Volatile lets reachability checks avoid blocking behind a prompt.
		private static volatile KeysharpInputClient client;
		private static KeysharpInputClient queryClient;
		private static KeysharpInputClient blockClient;
		private static Timer blockHeartbeat;
		private static Script blockOwner;
		private static KeysharpInputClient.BlockInputMask appliedBlockMask;
		// Avoid repeated prompts after a denial until an explicit re-request.
		private static LinuxPermissionScope declinedScopes;

		internal static void SendInputViaSynthesisChannel(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags = KeysharpInputClient.SynthFlags.None)
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
			{
				EnsureSynthesisCapabilityNoPrompt(hookClient, inputs, "hook");
				hookClient.SendInput(inputs, flags,
					Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookEventId);
				return;
			}

			if (!TryUseQueryClient(qc =>
			{
				EnsureSynthesisCapabilityNoPrompt(qc, inputs, "query");
				qc.SendInput(inputs, flags);
				return true;
			}))
				throw new InvalidOperationException("keysharp-input query channel is unavailable for synthesis.");
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

		internal static bool TryGetModifierState(
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

			if (!TryQuery(
					KeysharpInputClient.Operations.QueryModifiers,
					qc => (true, qc.QueryModifierState()),
					out KeysharpInputClient.ModifierStateSnapshot state,
					"modifier state query"))
				return false;

			logicalModifiersLR = state.LogicalModifiersLR;
			physicalModifiersLR = state.PhysicalModifiersLR;
			capsLock = state.CapsLock;
			numLock = state.NumLock;
			scrollLock = state.ScrollLock;
			return true;
		}

		/// <summary>
		/// Queries the compositor-independent idle counter without requesting a grant.
		/// </summary>
		internal static bool TryGetIdleTime(out long milliseconds)
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
		internal static bool TryGetKeyState(out uint modifiersLR, out bool capsLock, out bool numLock, out bool scrollLock, out byte[] logicalKeys, out byte[] physicalKeys)
		{
			modifiersLR = 0;
			capsLock = false;
			numLock = false;
			scrollLock = false;
			logicalKeys = [];
			physicalKeys = [];

			// Full key state is monitoring data. Use an existing grant and never prompt here.
			if (!TryQuery(
					KeysharpInputClient.Operations.QueryKeyState,
					qc => (true, qc.QueryKeyState()),
					out KeysharpInputClient.KeyStateSnapshot state,
					"key state query"))
				return false;

			modifiersLR = state.ModifiersLR;
			capsLock = state.CapsLock;
			numLock = state.NumLock;
			scrollLock = state.ScrollLock;
			logicalKeys = state.LogicalKeys ?? [];
			physicalKeys = state.PhysicalKeys ?? [];
			return true;
		}

		internal static bool TryGetPointerPosition(
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
		internal static bool TryGetButtonStateLogical(uint vk, out bool down)
			=> TryQueryButtonState(vk, physical: false, out down);

		/// <summary>Live physical state of one mouse button.</summary>
		internal static bool TryGetButtonStatePhysical(uint vk, out bool down)
			=> TryQueryButtonState(vk, physical: true, out down);

		private static bool TryQueryButtonState(uint vk, bool physical, out bool down)
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

		private static bool TryQuery<T>(
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
					InvalidateScopesAfterQueryDenial(required);

				if (!success)
					return false;

				value = captured;
				return true;
			}
			catch (NativeClientException ex)
				when (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked)
			{
				InvalidateScopesAfterQueryDenial(required);

				if (failureContext != null)
					Diagnostics.Debug.WriteLine($"keysharp-input: {failureContext} failed: {ex.Message}");

				return false;
			}
			catch (Exception ex)
			{
				if (failureContext != null)
					Diagnostics.Debug.WriteLine($"keysharp-input: {failureContext} failed: {ex.Message}");

				return false;
			}
		}

		private static void InvalidateScopesAfterQueryDenial(KeysharpInputClient.Operations required)
		{
			lock (authorizationGate)
				client?.InvalidateScopes(KeysharpInputClient.RequiredScopes(required));
		}

		private static KeysharpInputClient GetOrCreateQueryClient()
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
				return hookClient;

			lock (queryGate)
			{
				if (queryClient != null && queryClient.IsConnected)
					return queryClient;

				if (queryClient != null)
				{
					try { queryClient.Dispose(); } catch { }
					queryClient = null;
					queryRetries.Rearm();
				}

				using var attempt = queryRetries.TryBegin();

				if (attempt == null)
					return null;

				try
				{
					queryClient = KeysharpInputClient.Connect();
					attempt.Succeed();
				}
				catch (Exception ex) when (IsConnectException(ex))
				{
					attempt.Fail(ex);
				}

				return queryClient;
			}
		}

		private static bool TryUseQueryClient(Func<KeysharpInputClient, bool> action)
		{
			var qc = GetOrCreateQueryClient();

			if (qc == null)
				return false;

			if (ReferenceEquals(qc,
				Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient))
			{
				try
				{
					return action(qc);
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-input hook channel lost: {ex.Message}");
					return false;
				}
			}

			lock (queryGate)
			{
				if (!ReferenceEquals(qc, queryClient))
					return false;

				try
				{
					return action(qc);
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-input query channel lost: {ex.Message}");
					try { queryClient?.Dispose(); } catch { }
					queryClient = null;
					queryRetries.Rearm();
					return false;
				}
			}
		}

		internal static bool TrySetBlockInput(Script owner,
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

			var invalidateControlGrant = false;
			var success = false;

			lock (gate)
			{
				if (!owners.Contains(owner))
				{
					message = "The script owning this BlockInput request has already stopped.";
					return false;
				}

				if (mask == appliedBlockMask
					&& (mask == KeysharpInputClient.BlockInputMask.None
						|| blockClient != null))
				{
					blockOwner = mask == KeysharpInputClient.BlockInputMask.None ? null : owner;
					if (mask != KeysharpInputClient.BlockInputMask.None)
						StartBlockHeartbeatLocked();
					message = string.Empty;
					return true;
				}

				if (mask == KeysharpInputClient.BlockInputMask.None)
				{
					StopBlockClientLocked();
					blockOwner = null;
					message = string.Empty;
					return true;
				}

				try
				{
					blockOwner = owner;
					blockClient ??= KeysharpInputClient.Connect(
						KeysharpInputClient.Operations.BlockInput);
					var granted = blockClient.SetBlockInput(mask);

					if (granted != mask)
					{
						Diagnostics.Debug.WriteLine($"BlockInput: daemon granted {granted}, requested {mask}.");
						StopBlockClientLocked();
						ClearManagedBlockStateLocked();
						message = $"keysharp-input granted {granted}, but {mask} was requested.";
						return false;
					}

					appliedBlockMask = granted;
					StartBlockHeartbeatLocked();
					message = string.Empty;
					success = true;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					StopBlockClientLocked();
					ClearManagedBlockStateLocked();
					message = ex.Message;
				}
				catch (NativeClientException ex)
				{
					if (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked)
						invalidateControlGrant = true;
					StopBlockClientLocked();
					ClearManagedBlockStateLocked();
					message = ex.Message;
				}
			}

			if (invalidateControlGrant)
				lock (authorizationGate)
					client?.InvalidateScopes(LinuxPermissionScope.InputControl);

			return success;
		}

		private static void StopBlockClientLocked()
		{
			blockHeartbeat?.Dispose();
			blockHeartbeat = null;
			blockClient?.Dispose();
			blockClient = null;
			appliedBlockMask = KeysharpInputClient.BlockInputMask.None;
		}

		private static void StartBlockHeartbeatLocked()
		{
			// Nonzero blocking has a 15-second daemon lease; renew well before it expires.
			blockHeartbeat ??= new Timer(static _ =>
			{
				lock (gate)
				{
					if (blockClient == null)
						return;

					try
					{
						blockClient.Ping();
					}
					catch (Exception ex)
					{
						StopBlockClientLocked();
						ClearManagedBlockStateLocked();
						Diagnostics.Debug.WriteLine($"BlockInput lease renewal failed: {ex.Message}");
					}
				}
			}, null, 5000, 5000);
		}

		private static void ClearManagedBlockStateLocked()
		{
			if (blockOwner != null)
				blockOwner.KeyboardData.blockInput = false;

			blockOwner = null;
			appliedBlockMask = KeysharpInputClient.BlockInputMask.None;
		}

		/// <summary>
		/// Reports the current persistent grant without prompting.
		/// </summary>
		internal static PermissionResult PeekInputPermission(LinuxPermissionScope required)
			=> EnsurePermissionScope(required, "input permission status query", checkOnly: true);

		private const string DeclinedForRunMessage =
			"Access to keysharp-input was declined for this run. Re-run the app or request the permission explicitly to try again.";

		/// <summary>
		/// Allocation-, lock-, and IPC-free check for an already granted operation.
		/// A reconnect race only sends one call through the locked request path.
		/// </summary>
		internal static bool HasInputOperation(KeysharpInputClient.Operations required)
		{
			var c = client;
			return c != null && c.HasOperations(required);
		}

		internal static PermissionResult EnsurePermissionScope(LinuxPermissionScope required,
			string operation = null, bool forcePrompt = false, bool checkOnly = false)
		{
			if (required == LinuxPermissionScope.None
				|| (required & ~(LinuxPermissionScope.InputMonitoring | LinuxPermissionScope.InputControl)) != 0)
				throw new ArgumentOutOfRangeException(nameof(required));

			return EnsureAuthorization(required, KeysharpInputClient.Operations.None,
				operation ?? "input permission", forcePrompt, checkOnly);
		}

		internal static PermissionResult EnsureOperations(KeysharpInputClient.Operations required,
			string operation = null, bool forcePrompt = false, bool checkOnly = false)
			=> EnsureAuthorization(KeysharpInputClient.RequiredScopes(required), required,
				operation ?? "input automation", forcePrompt, checkOnly);

		private static PermissionResult EnsureAuthorization(LinuxPermissionScope required,
			KeysharpInputClient.Operations operations, string operation, bool forcePrompt, bool checkOnly)
		{
			checkOnly |= Script.IsHeadless;
			lock (authorizationGate)
			{
				if (forcePrompt)
				{
					declinedScopes &= ~required;
					connectionRetries.Rearm();
				}

				if (!TryEnsureConnected(operation, out var connectStatus, out var connectMessage))
					return new PermissionResult(connectStatus, connectMessage);

				bool Request(bool noninteractive, out int status)
					=> operations != KeysharpInputClient.Operations.None || required == LinuxPermissionScope.None
						? client.TryRequestOperations(operations, out status, noninteractive)
						: client.TryRequestScopes(required, out status, noninteractive);

				try
				{
					// A noninteractive refresh observes external revocation before using a cached grant.
					if (Request(true, out var status))
					{
						declinedScopes &= ~required;
						return new PermissionResult(PermissionStatus.Granted);
					}

					if (!checkOnly && status != (int)NativeClientStatus.Unsupported)
					{
						if (!forcePrompt && required != LinuxPermissionScope.None
							&& (declinedScopes & required) == required)
							return new PermissionResult(PermissionStatus.Denied, DeclinedForRunMessage);

						if (Request(false, out status))
						{
							declinedScopes &= ~required;
							return new PermissionResult(PermissionStatus.Granted);
						}
						declinedScopes |= required;
					}

					return new PermissionResult(status == (int)NativeClientStatus.Unsupported
						? PermissionStatus.Unsupported : PermissionStatus.Denied,
						$"keysharp-input could not authorize '{operation}'. Required scopes: {required}; granted scopes: {client.GrantedScopes}.");
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					HandleConnectionLost();
					return new PermissionResult(PermissionStatus.Unsupported,
						$"keysharp-input connection lost while preparing '{operation}': {ex.Message}");
				}
			}
		}

		private static bool TryEnsureConnected(string operation, out PermissionStatus status, out string message)
		{
			if (client != null)
			{
				if (client.IsConnected)
				{
					status = PermissionStatus.Granted;
					message = string.Empty;
					return true;
				}

				// Discard a closed cached connection before attempting a fresh handshake.
				HandleConnectionLost();
			}

			return TryConnect(operation, out status, out message);
		}

		private static bool TryConnect(string operation, out PermissionStatus status, out string message)
		{
			using var attempt = connectionRetries.TryBegin();

			if (attempt == null)
			{
				status = PermissionStatus.Unsupported;
				message = $"keysharp-input is unavailable and its {connectionRetries.FailureCount}-attempt reconnect burst " +
					$"has stopped for this run. An explicit permission request or a detected connection loss will rearm it.";
				return false;
			}

			try
			{
				client = KeysharpInputClient.Connect(
					requestTimeoutMs: KeysharpInputClient.AuthorizationTimeoutMs);
				attempt.Succeed();
				queryRetries.Rearm();
				status = PermissionStatus.Granted;
				message = string.Empty;

				return true;
			}
			catch (Exception ex) when (IsConnectException(ex))
			{
				DisposeClient();
				attempt.Fail(ex);
				status = PermissionStatus.Unsupported;
				message = $"keysharp-input is not installed or not available at '{KeysharpInputClient.DefaultSocketPath}'. " +
					$"Install the keysharp-input helper to use '{operation ?? "input automation"}'. Details: {ex.Message}";
				return false;
			}
		}

		private static bool IsConnectException(Exception ex)
			=> ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
				or ObjectDisposedException or InvalidDataException
				|| ex is NativeClientException native
					&& native.Status is NativeClientStatus.Unavailable
						or NativeClientStatus.Timeout or NativeClientStatus.Internal;

		private static bool IsTransportException(Exception ex)
			=> IsConnectException(ex) || ex is EndOfStreamException
				|| ex is NativeClientException { Status: NativeClientStatus.Cancelled };

		// Queries may use a persistent grant but never prompt.
		private static bool EnsureQueryCapabilityNoPrompt(KeysharpInputClient qc, KeysharpInputClient.Operations required)
		{
			if (qc.HasOperations(required))
				return true;

			return qc.TryRequestOperations(required, out _, checkOnly: true);
		}

		private static void DisposeClient()
		{
			try { client?.Dispose(); } catch { }
			client = null;
			DisposeQueryClient();
		}

		private static void HandleConnectionLost()
		{
			DisposeClient();
			connectionRetries.Rearm();
			queryRetries.Rearm();
		}

		internal static void RegisterOwner(Script owner)
		{
			ArgumentNullException.ThrowIfNull(owner);

			lock (gate)
				_ = owners.Add(owner);
		}

		internal static void DisconnectClients(Script owner)
		{
			ArgumentNullException.ThrowIfNull(owner);
			var possiblyLastOwner = false;

			lock (gate)
			{
				_ = owners.Remove(owner);
				possiblyLastOwner = owners.Count == 0;

				if (possiblyLastOwner || ReferenceEquals(blockOwner, owner))
				{
					StopBlockClientLocked();
					ClearManagedBlockStateLocked();
				}
			}

			if (!possiblyLastOwner)
				return;

			// Authorization owns the main client. Recheck owner state after acquiring
			// that lock so a concurrently starting script does not lose its connection.
			lock (authorizationGate)
			lock (gate)
			{
				if (owners.Count != 0)
					return;

				DisposeClient();
				declinedScopes = LinuxPermissionScope.None;
				connectionRetries.Rearm();
				queryRetries.Rearm();
			}
		}

		private static void DisposeQueryClient()
		{
			lock (queryGate)
			{
				try { queryClient?.Dispose(); } catch { }
				queryClient = null;
				queryRetries.Rearm();
			}
		}

	}
}
#endif
