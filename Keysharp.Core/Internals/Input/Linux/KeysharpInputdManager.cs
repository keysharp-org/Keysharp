using System.Net.Sockets;
using System.Threading;
using Keysharp.Builtins;

#if LINUX
namespace Keysharp.Internals.Input.Linux
{
	internal static class KeysharpInputdManager
	{
		private static readonly Lock gate = new();
		private static readonly Lock queryGate = new();
		private static readonly RetryGate connectionRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(250), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static readonly RetryGate queryRetries = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(100), maximumRetryDelay: TimeSpan.FromSeconds(1));

		// client owns trust/control state; queryClient handles ordinary synthesis/state RPC.
		// Hook callbacks use their LinuxHookThread HookStream synchronously instead.
		// Volatile lets IsDaemonReachable answer without blocking behind a prompt.
		private static volatile KeysharpInputdClient client;
		private static KeysharpInputdClient queryClient;
		private const KeysharpInputdClient.Capabilities InputdGrantedCapabilities =
			KeysharpInputdClient.Capabilities.HookKeyboard
			| KeysharpInputdClient.Capabilities.HookMouse
			| KeysharpInputdClient.Capabilities.SynthKeyboard
			| KeysharpInputdClient.Capabilities.SynthMouse
			| KeysharpInputdClient.Capabilities.BlockInput;

		// Session "declined" latch (macOS-style): capabilities the user denied/cancelled
		// this run. Once latched, we return Denied WITHOUT issuing a hello/prompt, so a
		// single Deny doesn't turn into a re-prompt storm across the several connections
		// and subsystems that each independently ensure capabilities. Cleared only by an
		// explicit user-driven re-request (forcePrompt). Guarded by `gate`.
		// This latch (plus TrySetBlockInput's block-off early-return) is also what keeps
		// ExitApp/teardown from prompting: teardown only re-requests input caps when hooks
		// were active, in which case they are already granted (no prompt) or already
		// declined (short-circuited here) — so no separate shutdown flag is needed.
		private static KeysharpInputdClient.Capabilities declinedCapabilities;

		internal static void SendInputViaSynthesisChannel(
			IReadOnlyList<KeysharpInputdClient.Input> inputs,
			KeysharpInputdClient.SynthFlags flags = KeysharpInputdClient.SynthFlags.None)
		{
			var hookClient = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookClient;

			if (hookClient != null)
			{
				hookClient.SendInput(inputs, flags,
					Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.CurrentHookEventId);
				return;
			}

			if (!TryUseQueryClient(qc =>
			{
				// Query connection inherits trust already established on the main client.
				if (!HasCapabilities(qc, KeysharpInputdClient.Capabilities.SynthKeyboard | KeysharpInputdClient.Capabilities.SynthMouse))
					qc.TryRequestCapabilities(
						KeysharpInputdClient.Capabilities.SynthKeyboard | KeysharpInputdClient.Capabilities.SynthMouse,
						out _);

				qc.SendInput(inputs, flags);
				return true;
			}))
				throw new InvalidOperationException("keysharp-inputd query channel is unavailable for synthesis.");
		}

		/// <summary>Best-effort panic release; never throws.</summary>
		internal static void EmergencyReleaseInput()
		{
			try
			{
				_ = TryUseQueryClient(qc =>
				{
					qc.EmergencyPassthrough();
					return true;
				});
			}
			catch
			{
			}
		}

		internal static bool TryGetIndicatorState(out bool capsLock, out bool numLock, out bool scrollLock)
		{
			capsLock = false;
			numLock = false;
			scrollLock = false;

			if (!TryQuery(
					KeysharpInputdClient.Capabilities.HookKeyboard,
					qc => (true, qc.GetIndicatorState()),
					out (bool CapsLock, bool NumLock, bool ScrollLock) state,
					"indicator state query"))
				return false;

			capsLock = state.CapsLock;
			numLock = state.NumLock;
			scrollLock = state.ScrollLock;
			return true;
		}

		/// <summary>
		/// Queries the daemon's compositor-independent idle counter without requesting privileged hook
		/// capabilities. A capless CLIENT_HELLO authenticates the process but never opens a trust prompt.
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
				Diagnostics.Debug.WriteLine($"keysharp-inputd: idle time query failed: {ex.Message}");
				return false;
			}

			milliseconds = captured > long.MaxValue ? long.MaxValue : (long)captured;
			return true;
		}

		/// <summary>Queries inputd for current logical modifier and toggle-key state.</summary>
		internal static bool TryGetKeyState(out uint modifiersLR, out bool capsLock, out bool numLock, out bool scrollLock)
			=> TryGetKeyState(out modifiersLR, out capsLock, out numLock, out scrollLock, out _, out _);

		/// <summary>Queries logical keyboard state plus optional logical-key bitmap.</summary>
		internal static bool TryGetKeyState(out uint modifiersLR, out bool capsLock, out bool numLock, out bool scrollLock, out byte[] logicalKeys)
			=> TryGetKeyState(out modifiersLR, out capsLock, out numLock, out scrollLock, out logicalKeys, out _);

		/// <summary>Queries logical and physical keyboard state.</summary>
		internal static bool TryGetKeyState(out uint modifiersLR, out bool capsLock, out bool numLock, out bool scrollLock, out byte[] logicalKeys, out byte[] physicalKeys)
		{
			modifiersLR = 0;
			capsLock = false;
			numLock = false;
			scrollLock = false;
			logicalKeys = [];
			physicalKeys = [];

			// Deliberately no EnsureCapabilities here: this must never prompt. Reading which keys the user is
			// holding is monitoring data, so it cannot be bundled with synthesis — yet Send needs the current
			// modifier state to decide what to press and release, and asking here would make every Send
			// escalate to a keyboard-hook prompt. TryQuery therefore only inherits a HookKeyboard grant that
			// already exists (an installed hook, or an explicit RequestCapabilities("InputMonitoring")), and
			// otherwise reports failure so the caller falls back to another source. See LinuxKeyboard.
			if (!TryQuery(
					KeysharpInputdClient.Capabilities.HookKeyboard,
					qc => (true, qc.QueryKeyState()),
					out KeysharpInputdClient.KeyStateSnapshot state,
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

			if (!EnsureCapabilities(KeysharpInputdClient.Capabilities.HookMouse, "query pointer position").IsGranted)
				return false;

			if (!TryQuery(
					KeysharpInputdClient.Capabilities.HookMouse,
					qc => qc.TryGetPointerPosition(out var position) ? (true, position) : (false, default),
					out KeysharpInputdClient.PointerPosition position))
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

			if (!EnsureCapabilities(KeysharpInputdClient.Capabilities.HookMouse, "query mouse button state").IsGranted)
				return false;

			if (!TryQuery(
					KeysharpInputdClient.Capabilities.HookMouse,
					qc => qc.TryGetPointerButtons(out var buttons) ? (true, buttons) : (false, default),
					out KeysharpInputdClient.PointerButtons buttons))
				return false;

			var buttonsMask = physical ? buttons.PhysicalButtons : buttons.LogicalButtons;
			down = (buttonsMask & bit) != 0;
			return true;
		}

		private static bool TryQuery<T>(
			KeysharpInputdClient.Capabilities required,
			Func<KeysharpInputdClient, (bool Success, T Value)> query,
			out T value,
			string failureContext = null)
		{
			value = default;
			var captured = default(T);

			try
			{
				var success = TryUseQueryClient(qc =>
				{
					if (!EnsureQueryCapabilityNoPrompt(qc, required))
						return false;

					var result = query(qc);

					if (!result.Success)
						return false;

					captured = result.Value;
					return true;
				});

				if (!success)
					return false;

				value = captured;
				return true;
			}
			catch (Exception ex)
			{
				if (failureContext != null)
					Diagnostics.Debug.WriteLine($"keysharp-inputd: {failureContext} failed: {ex.Message}");

				return false;
			}
		}

		private static KeysharpInputdClient GetOrCreateQueryClient()
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
						queryClient = KeysharpInputdClient.Connect();
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

		private static bool TryUseQueryClient(Func<KeysharpInputdClient, bool> action)
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
				catch (KeysharpInputdClient.RequestFailedException)
				{
					throw;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-inputd hook channel lost: {ex.Message}");
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
				catch (KeysharpInputdClient.RequestFailedException)
				{
					throw;
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-inputd query channel lost: {ex.Message}");
					try { queryClient?.Dispose(); } catch { }
					queryClient = null;
					queryRetries.Rearm();
					return false;
				}
			}
		}

		internal static KeysharpInputdClient Client
		{
			get
			{
				lock (gate)
					return client;
			}
		}

		/// <summary>Ensures the client is connected and has synthesis (Send) capability.</summary>
		internal static PermissionResult EnsureInputInjection(string operation = null)
			=> EnsureCapabilities(
				KeysharpInputdClient.Capabilities.SynthKeyboard | KeysharpInputdClient.Capabilities.SynthMouse,
				operation ?? "keyboard/mouse sending");

		/// <summary>Ensures the client is connected and has hook (monitoring) capability.</summary>
		internal static PermissionResult EnsureInputMonitoring(string operation = null)
			=> EnsureCapabilities(
				ExpandInputPermissionRequest(
					KeysharpInputdClient.Capabilities.HookKeyboard | KeysharpInputdClient.Capabilities.HookMouse),
				operation ?? "keyboard/mouse monitoring");

		internal static bool TrySetBlockInput(KeysharpInputdClient.BlockInputMask mask, out string message)
		{
			lock (gate)
			{
				// Turning blocking OFF never needs the capability — never prompt/deny to
				// UN-block during teardown (ExitApp / per-Send restore) for a capability
				// that was never granted.
				if (mask == KeysharpInputdClient.BlockInputMask.None
					&& (client == null || !HasCapabilities(client, KeysharpInputdClient.Capabilities.BlockInput)))
				{
					message = string.Empty;
					return true;
				}

				if (!TryEnsureConnected("block input", out _, out message))
					return false;

				if (!HasCapabilities(client, KeysharpInputdClient.Capabilities.BlockInput))
				{
					var want = ExpandInputPermissionRequest(KeysharpInputdClient.Capabilities.BlockInput)
						& InputdGrantedCapabilities;

					// Respect the session declined latch so block input does not re-prompt
					// after the user already declined input access this run.
					if ((want & declinedCapabilities) == want
						|| !client.TryRequestCapabilities(want, want, out _))
					{
						if (!HasCapabilities(client, KeysharpInputdClient.Capabilities.BlockInput))
							declinedCapabilities |= want;

						message = $"keysharp-inputd did not grant block-input capability. Granted: {client.GrantedCapabilities}.";
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
			}
		}

		/// <summary>
		/// Non-prompting status peek (mirrors macOS CGPreflight*): reports the current grant
		/// state for the given capability WITHOUT issuing a hello or prompt. Ungranted /
		/// undecided reports Denied. Used by the status-query path (RequestCapabilities with
		/// no args, EnsurePermissions) so those never pop a dialog.
		/// </summary>
		internal static PermissionResult PeekInputCapability(KeysharpInputdClient.Capabilities required)
		{
			lock (gate)
			{
				var cap = required & InputdGrantedCapabilities;

				if (cap == KeysharpInputdClient.Capabilities.None)
					return new PermissionResult(PermissionStatus.NotApplicable);

				if (MainClientHasCapability(cap))
					return new PermissionResult(PermissionStatus.Granted);

				return new PermissionResult(PermissionStatus.Denied,
					"keysharp-inputd has not granted this capability (status peek, no prompt).");
			}
		}

		private const string DeclinedForRunMessage =
			"Access to keysharp-inputd was declined for this run. Re-run the app to be prompted again " +
			"(or grant it persistently with Allow).";

		/// <summary>
		/// Allocation-, lock- and IPC-free "do we already hold this?" test, for callers that run in a loop and
		/// would otherwise pay for <see cref="EnsureCapabilities"/> on every iteration. A false answer only means
		/// "not known to be granted" — the caller still has to go through <see cref="EnsureCapabilities"/> to
		/// actually request it. Reading the client reference without <c>gate</c> is deliberate: the worst a race
		/// with a reconnect can do is send one call down the slow path, which then takes the lock properly.
		/// </summary>
		internal static bool HasInputCapability(KeysharpInputdClient.Capabilities required)
		{
			var c = client;
			return c != null && (c.GrantedCapabilities & required) == required;
		}

		/// <summary>Requests exactly <paramref name="required"/>. Callers that are about to install a hook (rather
		/// than just read state) widen that themselves with <see cref="ExpandInputPermissionRequest"/>, so the one
		/// prompt covers the whole set they will need — see LinuxHookThread and <see cref="TrySetBlockInput"/>.</summary>
		internal static PermissionResult EnsureCapabilities(
			KeysharpInputdClient.Capabilities required,
			string operation = null,
			bool forcePrompt = false)
		{
			operation ??= "input automation";

			lock (gate)
			{
				var requiredFromInputd = required & InputdGrantedCapabilities;

				// An explicit user re-request (forcePrompt) clears the declined latch and the endpoint retry budget.
				if (forcePrompt)
				{
					declinedCapabilities &= ~requiredFromInputd;
					connectionRetries.Rearm();
				}

				// Already declined this run: deny WITHOUT a hello/prompt (unless the caps
				// are actually granted already). This is what stops one Deny from
				// cascading into a re-prompt storm as each subsystem re-asks.
				// The message is a constant rather than interpolated: this branch is the steady state for a
				// polling caller (GetKeyState in a loop) after a decline, so it must not allocate per call.
				// The informative, operation-named message is still produced by the actual refusal below.
				if (!forcePrompt && requiredFromInputd != KeysharpInputdClient.Capabilities.None
					&& !MainClientHasCapability(requiredFromInputd)
					&& (requiredFromInputd & declinedCapabilities) == requiredFromInputd)
					return new PermissionResult(PermissionStatus.Denied, DeclinedForRunMessage);

				if (!TryEnsureConnected(operation, out var connectStatus, out var connectMessage))
					return new PermissionResult(connectStatus, connectMessage);

				try
				{
					if ((!forcePrompt && HasCapabilities(client, requiredFromInputd))
						|| client.TryRequestCapabilities(required, requiredFromInputd, out _, forcePrompt))
						return new PermissionResult(PermissionStatus.Granted);

					// Denied by the daemon (user pressed Deny / dismissed the prompt).
					// Latch it so nothing re-prompts for the rest of this run.
					declinedCapabilities |= requiredFromInputd;
					return new PermissionResult(PermissionStatus.Denied,
						$"keysharp-inputd did not grant the required capabilities for '{operation}'. " +
						$"Required from inputd: {requiredFromInputd}, requested: {required}, granted: {client.GrantedCapabilities}.");
				}
				catch (Exception ex) when (IsTransportException(ex))
				{
					HandleConnectionLost();
					return new PermissionResult(PermissionStatus.Unsupported,
						$"keysharp-inputd connection lost while preparing '{operation}': {ex.Message}");
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

				// Cached client is stale -- the daemon already closed this
				// connection (e.g. it crashed or restarted) since we last used
				// it. Previously this branch was trusted purely because it was
				// non-null, so the FIRST real request after a silent daemon
				// death threw an avoidable transport exception instead of
				// transparently reconnecting here, same as every other
				// caller-visible transport failure already does.
				HandleConnectionLost();
			}

			return TryConnect(operation, out status, out message);
		}

		/// <summary>Probes daemon reachability without requesting capabilities.</summary>
		internal static bool IsDaemonReachable()
		{
			if (!gate.TryEnter(TimeSpan.FromMilliseconds(50)))
				return client != null;

			try
			{
				return TryEnsureConnected("probe keysharp-inputd", out _, out _);
			}
			finally
			{
				gate.Exit();
			}
		}

		private static bool TryConnect(string operation, out PermissionStatus status, out string message)
		{
			using var attempt = connectionRetries.TryBegin();

			if (attempt == null)
			{
				status = PermissionStatus.Unsupported;
				message = $"keysharp-inputd is unavailable and its {connectionRetries.FailureCount}-attempt reconnect burst " +
					$"has stopped for this run. An explicit permission request or a detected connection loss will rearm it.";
				return false;
			}

			try
			{
				client = KeysharpInputdClient.Connect();
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
				message = $"keysharp-inputd is not installed or not available at '{KeysharpInputdClient.DefaultSocketPath}'. " +
					$"Install Keysharp's optional Linux input helper to use '{operation ?? "input automation"}'. Details: {ex.Message}";
				return false;
			}
		}

		private static bool HasCapabilities(KeysharpInputdClient connectedClient, KeysharpInputdClient.Capabilities required)
			=> (connectedClient.GrantedCapabilities & required) == required;

		private static bool IsConnectException(Exception ex)
			=> ex is IOException or SocketException or ObjectDisposedException or InvalidDataException;

		private static bool IsTransportException(Exception ex)
			=> IsConnectException(ex) || ex is EndOfStreamException;

		private static bool MainClientHasCapability(KeysharpInputdClient.Capabilities cap)
		{
			var c = client;
			return c != null && (c.GrantedCapabilities & cap) == cap;
		}

		// Query reads must never prompt; inherit only from an existing main grant.
		private static bool EnsureQueryCapabilityNoPrompt(KeysharpInputdClient qc, KeysharpInputdClient.Capabilities required)
		{
			if (HasCapabilities(qc, required))
				return true;

			return MainClientHasCapability(required)
				&& qc.TryRequestCapabilities(required, out _);
		}

		internal static KeysharpInputdClient.Capabilities ExpandInputPermissionRequest(KeysharpInputdClient.Capabilities requested)
		{
			var hookBits = KeysharpInputdClient.Capabilities.HookKeyboard
				| KeysharpInputdClient.Capabilities.HookMouse;
			var synthBits = KeysharpInputdClient.Capabilities.SynthKeyboard
				| KeysharpInputdClient.Capabilities.SynthMouse;

			// Hooking also needs replay/synthesis, so request both device pairs.
			// Synthesis alone stays synthesis-only; it must not acquire or prompt for
			// physical-device access merely because the process can send input.
			if ((requested & hookBits) != 0)
				requested |= hookBits | synthBits;
			else if ((requested & synthBits) != 0)
				requested |= synthBits;

			return requested;
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

		internal static void DisconnectClients()
		{
			lock (gate)
			{
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
