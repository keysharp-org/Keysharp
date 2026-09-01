#if LINUX
using System.Threading;
using Keysharp.Builtins;
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Input.Linux
{
	internal static class KeysharpInputManager
	{
		private static readonly Lock gate = new();
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
		// Avoid repeated prompts after a denial until an explicit re-request.
		private static LinuxPermissionScope declinedScopes;

		internal static void SendInputViaSynthesisChannel(
			IReadOnlyList<KeysharpInputClient.Input> inputs,
			KeysharpInputClient.SynthFlags flags = KeysharpInputClient.SynthFlags.None)
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
			{
				EnsureSynthesisCapabilityNoPrompt(hookClient, "hook");
				hookClient.SendInput(inputs, flags,
					Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookEventId);
				return;
			}

			if (!TryUseQueryClient(qc =>
			{
				EnsureSynthesisCapabilityNoPrompt(qc, "query");
				qc.SendInput(inputs, flags);
				return true;
			}))
				throw new InvalidOperationException("keysharp-input query channel is unavailable for synthesis.");
		}

		private static void EnsureSynthesisCapabilityNoPrompt(KeysharpInputClient connectedClient, string channel)
		{
			var synthesis = KeysharpInputClient.Operations.SynthesizeKeyboard
				| KeysharpInputClient.Operations.SynthesizeMouse;

			if (!HasOperations(connectedClient, synthesis)
				&& !connectedClient.TryRequestOperations(synthesis, out _, checkOnly: true))
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

			if (!TryEnsureCaplessQueryConnection("modifier state query"))
				return false;

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

			lock (gate)
				if (!TryEnsureConnected("query idle time", out _, out _))
					return false;

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

			if (!TryEnsureCaplessQueryConnection("pointer position query"))
				return false;

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
			lock (gate)
				client?.InvalidateScopes(KeysharpInputClient.RequiredScopes(required));
		}

		private static bool TryEnsureCaplessQueryConnection(string operation)
		{
			if (Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient != null)
				return true;

			lock (gate)
				return TryEnsureConnected(operation, out _, out _);
		}

		private static KeysharpInputClient GetOrCreateQueryClient()
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
				return hookClient;

			lock (gate)
			{
				if (client == null)
					return null;

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
				catch (NativeClientException)
				{
					throw;
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
				catch (NativeClientException)
				{
					throw;
				}
			}
		}

		internal static bool TrySetBlockInput(KeysharpInputClient.BlockInputMask mask, out string message)
		{
			lock (gate)
			{
				// Teardown must be able to release blocking without opening a prompt.
				if (mask == KeysharpInputClient.BlockInputMask.None
					&& (client == null || !HasOperations(client, KeysharpInputClient.Operations.BlockInput)))
				{
					message = string.Empty;
					return true;
				}

				if (!TryEnsureConnected("block input", out _, out message))
					return false;

				if (!HasOperations(client, KeysharpInputClient.Operations.BlockInput))
				{
					var want = KeysharpInputClient.Operations.BlockInput;
					var wantedScope = KeysharpInputClient.RequiredScopes(want);

					// Respect the session declined latch so block input does not re-prompt
					// after the user already declined input access this run.
					var checkOnly = Script.IsHeadless;
					if ((!checkOnly && (wantedScope & declinedScopes) == wantedScope)
						|| !client.TryRequestOperations(want, want, out _, checkOnly: checkOnly))
					{
						if (!checkOnly && !HasOperations(client, KeysharpInputClient.Operations.BlockInput))
							declinedScopes |= wantedScope;

						message = $"keysharp-input did not grant InputControl. Granted scopes: {client.GrantedScopes}.";
						return false;
					}
				}

				try
				{
					var granted = client.SetBlockInput(mask);

					if (granted != mask)
						Diagnostics.Debug.WriteLine($"BlockInput: daemon granted {granted}, requested {mask}.");

					message = string.Empty;
					return true;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					HandleConnectionLost();
					message = ex.Message;
					return false;
				}
				catch (NativeClientException ex)
					when (ex.Status is NativeClientStatus.Denied or NativeClientStatus.Revoked)
				{
					client?.InvalidateScopes(LinuxPermissionScope.InputControl);
					message = ex.Message;
					return false;
				}
			}
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

		internal static PermissionResult EnsurePermissionScope(
			LinuxPermissionScope required,
			string operation = null,
			bool forcePrompt = false,
			bool checkOnly = false)
		{
			if (required == LinuxPermissionScope.None
				|| (required & ~(LinuxPermissionScope.InputMonitoring | LinuxPermissionScope.InputControl)) != 0)
				throw new ArgumentOutOfRangeException(nameof(required));

			operation ??= "input permission";
			checkOnly |= Script.IsHeadless;

			lock (gate)
			{
				if (forcePrompt)
				{
					declinedScopes &= ~required;
					connectionRetries.Rearm();
				}

				if (!checkOnly && !forcePrompt && (declinedScopes & required) == required)
					return new PermissionResult(PermissionStatus.Denied, DeclinedForRunMessage);

				if (!TryEnsureConnected(operation, out var connectStatus, out var connectMessage))
					return new PermissionResult(connectStatus, connectMessage);

				if ((client.GrantedScopes & required) == required)
					return new PermissionResult(PermissionStatus.Granted);

				try
				{
					if (client.TryRequestScopes(required, out var requestStatus, checkOnly))
						return new PermissionResult(PermissionStatus.Granted);

					if (!checkOnly)
						declinedScopes |= required;

					var status = (NativeClientStatus)(uint)requestStatus;
					if (status is NativeClientStatus.Unsupported or NativeClientStatus.Unavailable)
						return new PermissionResult(PermissionStatus.Unsupported,
							$"keysharp-input cannot authorize {required} for '{operation}'.");

					return new PermissionResult(PermissionStatus.Denied,
						$"keysharp-input did not grant {required} for '{operation}'" +
						(checkOnly ? " (noninteractive check)." : "."));
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					HandleConnectionLost();
					return new PermissionResult(PermissionStatus.Unsupported,
						$"keysharp-input connection lost while preparing '{operation}': {ex.Message}");
				}
			}
		}

		/// <summary>Requests exactly <paramref name="required"/>.</summary>
		internal static PermissionResult EnsureOperations(
			KeysharpInputClient.Operations required,
			string operation = null,
			bool forcePrompt = false,
			bool checkOnly = false)
		{
			operation ??= "input automation";
			checkOnly |= Script.IsHeadless;

			lock (gate)
			{
				var requiredScopes = KeysharpInputClient.RequiredScopes(required);

				// An explicit request clears the denial latch and retry budget.
				if (forcePrompt)
				{
					declinedScopes &= ~requiredScopes;
					connectionRetries.Rearm();
				}

				// Keep the polling path allocation-free after a denial.
				if (!checkOnly && !forcePrompt && requiredScopes != LinuxPermissionScope.None
					&& !MainClientHasOperation(required)
					&& (requiredScopes & declinedScopes) == requiredScopes)
					return new PermissionResult(PermissionStatus.Denied, DeclinedForRunMessage);

				if (!TryEnsureConnected(operation, out var connectStatus, out var connectMessage))
					return new PermissionResult(connectStatus, connectMessage);

				try
				{
					if ((!checkOnly && !forcePrompt && HasOperations(client, required))
						|| client.TryRequestOperations(required, required, out var requestStatus, checkOnly: checkOnly))
						return new PermissionResult(PermissionStatus.Granted);

					// A noninteractive miss is only a status result. An interactive denial is
					// latched so ordinary operations do not create a prompt storm.
					if (!checkOnly)
						declinedScopes |= requiredScopes;

					if (requestStatus == (int)NativeClientStatus.Unsupported)
						return new PermissionResult(PermissionStatus.Unsupported,
							$"keysharp-input does not provide the operations required for '{operation}': {required}.");

					return new PermissionResult(PermissionStatus.Denied,
						$"keysharp-input did not grant the required permission for '{operation}'" +
						(checkOnly ? " (noninteractive check). " : ". ") +
						$"Required scopes: {requiredScopes}; granted scopes: {client.GrantedScopes}.");
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

				ThreadPool.QueueUserWorkItem(
					_ => { try { GetOrCreateQueryClient(); } catch { } });

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

		private static bool HasOperations(KeysharpInputClient connectedClient, KeysharpInputClient.Operations required)
			=> connectedClient.HasOperations(required);

		private static bool IsConnectException(Exception ex)
			=> ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
				or ObjectDisposedException or InvalidDataException
				|| ex is NativeClientException native
					&& native.Status is NativeClientStatus.Unavailable
						or NativeClientStatus.Timeout or NativeClientStatus.Internal;

		private static bool IsTransportException(Exception ex)
			=> IsConnectException(ex) || ex is EndOfStreamException
				|| ex is NativeClientException { Status: NativeClientStatus.Cancelled };

		private static bool MainClientHasOperation(KeysharpInputClient.Operations operation)
		{
			var c = client;
			return c != null && c.HasOperations(operation);
		}

		// Queries may use a persistent grant but never prompt.
		private static bool EnsureQueryCapabilityNoPrompt(KeysharpInputClient qc, KeysharpInputClient.Operations required)
		{
			if (HasOperations(qc, required))
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

			lock (gate)
			{
				_ = owners.Remove(owner);

				if (owners.Count != 0)
					return;

				DisposeClient();
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
