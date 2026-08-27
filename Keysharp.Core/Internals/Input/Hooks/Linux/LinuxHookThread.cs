#if LINUX
using Keysharp.Builtins;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Input.Hooks.Unix;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Hooks.Linux
{
	// Linux hook extension points backed by keysharp-inputd.
	internal sealed class LinuxHookThread : UnixHookThread
	{
		internal LinuxHookThread(Script script, string mutexName) : base(script, mutexName) { }

		private sealed record CallbackContext(
			KeysharpInputdClient Client,
			ulong EventId);

		// A hook-originated Send belongs to this native callback thread. ThreadStatic
		// deliberately keeps queued hotkey work and bounded #HotIf tasks off this
		// HookStream; only this thread may synchronously read and re-enter it.
		[ThreadStatic]
		private static CallbackContext callbackContext;

		private KeysharpInputdClient inputdHookClient;
		private CancellationTokenSource inputdHookCancel;
		private Task inputdHookTask;
		private HookType inputdHookKinds;
		private bool usingInputdHooks;
		internal static bool IsInHookCallback => callbackContext != null;
		internal static KeysharpInputdClient CurrentHookClient
			=> callbackContext?.Client;
		internal static ulong CurrentHookEventId
			=> callbackContext?.EventId ?? 0u;
		// Avoid grabbing evdev before X/XWayland can receive inputd's replay device.
		private const int InputdGrabDisplayWaitMs = 5000;
		private const int InputdGrabDisplayWaitPollMs = 100;

		// Crash-loop protection for inputd hook recovery.
		private int inputdRecoveryInFlight;
		private long lastInputdRecoveryTicks;
		private int inputdRecoveryAttempts;
		private const long InputdRecoveryWindowMs = 5000;
		private const int MaxInputdRecoveryAttempts = 3;

		// Wayland cursor queries are IPC, so throttle ClipCursor correction.
		private long lastClipQueryTicks;
		private const int ClipCorrectionDelayMs = 8;
		private static readonly long ClipQueryMinIntervalTicks = System.Diagnostics.Stopwatch.Frequency * ClipCorrectionDelayMs / 1000;
		private int clipCorrectionRequest;
		private int clipCorrectionWorkerActive;

		protected override KeyboardMouseSender CreateKbdMsSender()
			=> new InputdKeyboardMouseSender(script);

		// Fast path for the 8 modifier VKs while the inputd hook is active: every
		// key event, physical (evdev) and synthetic (uinput, re-injected through
		// the hook), flows through UpdateKeybdState on this same process, so
		// kbdMsSender.modifiersLRLogical is always the complete, authoritative
		// modifier state -- a same-thread field read, no IPC required. Without
		// this override, the base implementation round-trips to keysharp-inputd
		// over the query socket for every call, and GetModifierLRState(true)
		// (KeyboardMouseSender.cs) calls this up to 8 times (once per modifier
		// VK) on the mainline hotkey-firing path for every hotkey-eligible
		// keystroke -- turning a zero-cost check into up to 8 blocking
		// round-trips per keystroke, serialized against any other concurrent
		// query (e.g. MouseGetPos) via the shared query-client lock.
		// Non-modifier VKs and mouse VKs fall through to the base implementation
		// unchanged.
		internal override bool IsKeyDownLogical(uint vk)
		{
			if (usingInputdHooks)
			{
				var modMask = ModifierLRMaskFromVK(vk);

				if (modMask != 0)
					return (kbdMsSender.modifiersLRLogical & modMask) != 0;
			}

			return base.IsKeyDownLogical(vk);
		}

		protected override void StopPlatformHookCore(bool dispose)
		{
			StopInputdHookCore();
			base.StopPlatformHookCore(dispose);
		}

		protected override string PlatformHookDisabledMessage => "Linux hook disabled via KEYSHARP_DISABLE_HOOK=1.";

		protected override void OnPlatformHookStartFailed(string message)
			=> WarnIfWaylandHookUnavailable();

		private static bool warnedWaylandHookUnavailable;

		// Wayland has no in-process global input capture fallback. Surface the missing helper once to stderr
		// so hotkeys/hotstrings/InputHook do not silently stop working outside the debug pane.
		private static void WarnIfWaylandHookUnavailable()
		{
			if (warnedWaylandHookUnavailable || !Platform.Desktop.IsWaylandSession)
				return;

			warnedWaylandHookUnavailable = true;
			Script.WriteUncaughtErrorToStdErr(
				"Keysharp: global keyboard/mouse hooks require the keysharp-inputd helper on Wayland. " +
				"Install and enable it (re-run the installer, or " +
				"'keysharp-inputd --install-input-access') to enable global input capture on Wayland.");
		}

		private static bool IsInputdInjected(uint flags, uint injectedFlag)
			=> (flags & injectedFlag) != 0;

		private bool ProcessInputdKeyboardHook(KeysharpInputdClient.KeyboardHookEvent ev)
		{
			if (!keyboardEnabled)
				return false;

			var vk = ev.VkCode;
			var sc = ev.ScanCode <= SC_MAX ? ev.ScanCode : 0u;
			var keyUp = (ev.Flags & 0x80u) != 0 || ev.Message == 0x0101u || ev.Message == 0x0105u;
			var isInjected = IsInputdInjected(ev.Flags, 0x10u);
			Keysharp.Internals.InputdKeyboard.UpdateIndicatorSnapshotFromHookFlags(ev.Flags);

			// KeyPhysIgnore tracks as physical state but is still ignored by hotkeys.
			if (ev.ExtraInfo == (ulong)KeyboardMouseSender.KeyPhysIgnore)
				isInjected = false;

			switch (vk)
			{
				case VK_SHIFT:
					vk = sc == SC_RSHIFT ? VK_RSHIFT : VK_LSHIFT;
					break;
				case VK_CONTROL:
					vk = sc == SC_RCONTROL ? VK_RCONTROL : VK_LCONTROL;
					break;
				case VK_MENU:
					vk = sc == SC_RALT ? VK_RMENU : VK_LMENU;
					break;
			}

			// Windows reports numpad navigation VKs when NumLock and Shift agree.
			var numLockOn = Keysharp.Internals.InputdKeyboard.HookFlagsNumLockOn(ev.Flags);
			var shiftDown = (kbdMsSender.modifiersLRLogical & (MOD_LSHIFT | MOD_RSHIFT)) != 0;

			vk = KeyCodes.ApplyNumpadState(vk, numLockOn, shiftDown);

			if (vk == 0)
				return false;

			lastHookEventWasKeyboard = true;
			lastKeyboardEventVk = vk;

			if (!isInjected)
				script.timeLastInputPhysical = DateTime.UtcNow;

			var args = new KeyboardHookEventArgs(
				keyUp ? EventType.KeyReleased : EventType.KeyPressed,
				vk,
				sc,
				isInjected ? EventMask.SimulatedEvent : EventMask.None,
				ev.TimeMs);
			var extraInfo = ev.ExtraInfo;

			if (extraInfo == (ulong)KeyboardMouseSender.KeyBlockThis)
				return true;

			var result = LowLevelCommon(args, vk, sc, ev.ScanCode, keyUp, extraInfo,
				isInjected ? HOOK_EVENT_INJECTED : 0, ev.DeviceId);
			ApplyKeyStateAfterKeyboardDecision(vk, keyUp, isInjected, result);
			return result != 0;
		}

		// Pure Wayland and normal X11-ready sessions return immediately.
		private static void WaitForDisplayServerBeforeGrab()
		{
			if (IsWaylandSession || IsX11Available)
				return;

			var deadline = Environment.TickCount64 + InputdGrabDisplayWaitMs;

			while (Environment.TickCount64 < deadline && !IsX11Available)
				Thread.Sleep(InputdGrabDisplayWaitPollMs);
		}

		protected override bool StartPlatformHookCore(bool wantKeyboard, bool wantMouse, out string message)
		{
			message = string.Empty;

			if (!wantKeyboard && !wantMouse)
				return false;

			var wantedHooks = HookType.None;

			if (wantKeyboard)
				wantedHooks |= HookType.Keyboard;

			if (wantMouse)
				wantedHooks |= HookType.Mouse;

			var hookRunning = inputdHookClient != null
				&& inputdHookTask != null
				&& !inputdHookTask.IsCompleted;

			if (hookRunning && inputdHookKinds == wantedHooks)
			{
				usingInputdHooks = true;
				return true;
			}

			WaitForDisplayServerBeforeGrab();
			StopInputdHookCore();

			var required = KeysharpInputdClient.Capabilities.None;

			if (wantKeyboard)
				required |= KeysharpInputdClient.Capabilities.HookKeyboard;

			if (wantMouse)
				required |= KeysharpInputdClient.Capabilities.HookMouse;

			var permissionRequest = KeysharpInputdManager.ExpandInputPermissionRequest(required);
			var permission = KeysharpInputdManager.EnsureCapabilities(permissionRequest, "install keyboard/mouse hooks");

			if (!permission.IsGranted)
			{
				message = $"keysharp-inputd hook unavailable; global hooks disabled. {permission.Message}";
				return false;
			}

			try
			{
				inputdHookClient = KeysharpInputdClient.Connect(
					permissionRequest,
					role: KeysharpInputdClient.ConnectionRole.HookStream);
				inputdHookClient.SetHookQuarantineHandler(HandleHookQuarantined);
				inputdHookClient.SetNestedHookEventHandler(ProcessNestedHookEvent);

				if (wantMouse)
					_ = inputdHookClient.SubscribeHook(KeysharpInputdClient.HookType.MouseLowLevel);

				if (wantKeyboard)
					_ = inputdHookClient.SubscribeHook(KeysharpInputdClient.HookType.KeyboardLowLevel);

				inputdHookCancel = new CancellationTokenSource();
				var hookToken = inputdHookCancel.Token;
				var hookClient = inputdHookClient;
				inputdHookTask = Task.Run(() => InputdHookLoop(hookClient, hookToken));

				inputdHookKinds = wantedHooks;
				usingInputdHooks = true;
				return true;
			}
			catch (Exception ex)
			{
				StopInputdHookCore();
				message = $"keysharp-inputd hook unavailable; global hooks disabled. {ex.Message}";
				return false;
			}
		}

		private void HandleHookQuarantined(KeysharpInputdClient.HookQuarantine quarantine)
		{
			Diagnostics.Debug.WriteLine(
				$"keysharp-inputd quarantined {quarantine.HookType} hook at event {quarantine.EventId}; " +
				$"strike {quarantine.StrikeCount}; inputd will retry it after {quarantine.RetryAfterMs} ms.");
		}

		private void StopInputdHookCore()
		{
			usingInputdHooks = false;
			try { inputdHookCancel?.Cancel(); } catch { }
			try { inputdHookClient?.Dispose(); } catch { }
			try { if (inputdHookTask != null && !inputdHookTask.IsCompleted) inputdHookTask.Wait(50); } catch { }
			inputdHookCancel = null;
			inputdHookClient = null;
			inputdHookTask = null;
			inputdHookKinds = HookType.None;
		}

		private sealed class HookReaderLiveness
		{
			private const long StallGraceMs = 10_000;
			private long lastProgressTicks = Environment.TickCount64;
			private volatile bool waitingForEvent = true;

			internal void MarkWaiting()
			{
				Volatile.Write(ref lastProgressTicks, Environment.TickCount64);
				waitingForEvent = true;
			}

			internal void MarkProgress()
			{
				waitingForEvent = false;
				Volatile.Write(ref lastProgressTicks, Environment.TickCount64);
			}

			internal bool IsAlive()
				=> waitingForEvent
					|| Environment.TickCount64 - Volatile.Read(ref lastProgressTicks) < StallGraceMs;
		}

		private void InputdHookLoop(KeysharpInputdClient client, CancellationToken token)
		{
			var liveness = new HookReaderLiveness();
			client.SetLeaseLivenessProbe(liveness.IsAlive);

			try
			{
				InputdHookLoopCore(client, token, liveness);
			}
			finally
			{
				client.SetLeaseLivenessProbe(static () => false);
			}
		}

		private void InputdHookLoopCore(KeysharpInputdClient client, CancellationToken token, HookReaderLiveness liveness)
		{
			while (!token.IsCancellationRequested)
			{
				KeysharpInputdClient.HookEvent hookEvent;
				liveness.MarkWaiting();

				try
				{
					hookEvent = client.ReadHookEvent();
				}
				catch (ObjectDisposedException)
				{
					return;
				}
				catch (Exception ex)
				{
					if (!token.IsCancellationRequested)
					{
						Diagnostics.Debug.WriteLine($"keysharp-inputd hook reader stopped: {ex.Message}");
						HandleInputdHookReaderLoss(ex.Message);
					}

					return;
				}

				liveness.MarkProgress();

				try
				{
					ProcessAndDecideHookEvent(client, hookEvent);
				}
				catch (KeysharpInputdClient.RequestFailedException ex)
					when (KeysharpInputdClient.IsStaleHookDecisionFailure(ex))
				{
					Diagnostics.Debug.WriteLine($"keysharp-inputd hook decision for event {hookEvent.EventId} arrived after its deadline; continuing hooks.");
				}
				catch (Exception ex)
				{
					if (!token.IsCancellationRequested)
					{
						Diagnostics.Debug.WriteLine($"keysharp-inputd hook decision failed: {ex.Message}");
						HandleInputdHookReaderLoss(ex.Message);
					}

					return;
				}
			}
		}

		private void ProcessNestedHookEvent(
			KeysharpInputdClient client,
			KeysharpInputdClient.HookEvent hookEvent)
		{
			try
			{
				ProcessAndDecideHookEvent(client, hookEvent);
			}
			catch (KeysharpInputdClient.RequestFailedException ex)
				when (KeysharpInputdClient.IsStaleHookDecisionFailure(ex))
			{
				Diagnostics.Debug.WriteLine($"keysharp-inputd nested hook decision for event {hookEvent.EventId} arrived after its deadline.");
			}
		}

		private void ProcessAndDecideHookEvent(
			KeysharpInputdClient client,
			in KeysharpInputdClient.HookEvent hookEvent)
		{
			var previousContext = callbackContext;
			var block = false;
			Exception callbackError = null;

			callbackContext = new(client, hookEvent.EventId);
			using var hotIfBudget = BeginHotIfCallback(HotIfCallbackBudgetMilliseconds);

			try
			{
				try
				{
					block = hookEvent.HookType switch
					{
						KeysharpInputdClient.HookType.KeyboardLowLevel => ProcessInputdKeyboardHook(hookEvent.Keyboard),
						KeysharpInputdClient.HookType.MouseLowLevel => ProcessInputdMouseHook(hookEvent.Mouse),
						_ => false
					};
				}
				catch (Exception ex)
				{
					callbackError = ex;
					Diagnostics.Debug.WriteLine($"keysharp-inputd hook event processing failed: {ex}");
				}

				SendInputdHookDecision(client, hookEvent, block);
			}
			finally
			{
				callbackContext = previousContext;
			}

			if (!block
				&& CursorClipActive
				&& hookEvent.HookType == KeysharpInputdClient.HookType.MouseLowLevel
				&& hookEvent.Mouse.Message == 0x0200u
				&& (hookEvent.Mouse.Flags & 0x01u) == 0)
				RequestCursorClipCorrection();

			if (callbackError != null)
				throw new InvalidOperationException("hook event processing failed", callbackError);
		}

		// A hook decision is pure suppress-or-pass. Direct sends on this owner have
		// already unwound recursively; worker-thread sends are independently queued.
		private static void SendInputdHookDecision(
			KeysharpInputdClient client,
			in KeysharpInputdClient.HookEvent hookEvent,
			bool block)
		{
			client.SendHookDecision(
				hookEvent.EventId,
				block ? KeysharpInputdClient.HookDecision.Block : KeysharpInputdClient.HookDecision.Pass);
		}

		private void HandleInputdHookReaderLoss(string reason)
		{
			if (Interlocked.Exchange(ref inputdRecoveryInFlight, 1) == 1)
				return;

			_ = Task.Run(() =>
			{
				try
				{
					RecoverInputdHooks(reason);
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"keysharp-inputd hook recovery failed: {ex}");
				}
				finally
				{
					Volatile.Write(ref inputdRecoveryInFlight, 0);
				}
			});
		}

		private void RecoverInputdHooks(string reason)
		{
			HookType want;
			long recoveryGeneration;

			lock (hookStateLock)
			{
				recoveryGeneration = hookStateGeneration;
				want = HookType.None;

				if (keyboardEnabled)
					want |= HookType.Keyboard;
				if (mouseEnabled)
					want |= HookType.Mouse;

				keyboardEnabled = false;
				mouseEnabled = false;
				StopInputdHookCore();
			}

			if (want == HookType.None)
				return;

			var now = Environment.TickCount64;

			if (now - lastInputdRecoveryTicks < InputdRecoveryWindowMs)
			{
				if (++inputdRecoveryAttempts >= MaxInputdRecoveryAttempts)
				{
					if (CursorClipActive)
						ClearCursorClip();

					// Give up: mirror ChangePlatformHookState's disable path. keyboardEnabled/mouseEnabled were
					// already cleared above, but kbdHook/mouseHook stayed non-zero and SyncHookMutexes was never
					// called. Without this, HasKbdHook()/HasMouseHook() (and thus A_KeybdHookInstalled/
					// A_MouseHookInstalled) keep reporting installed, and the cross-process named
					// 'Keysharp Keybd'/'Keysharp Mouse' mutexes stay held -- making OTHER Keysharp scripts wrongly
					// think a system hook exists and push their Send onto the SendInput fallback.
					var giveUpMessage = $"keysharp-inputd hooks lost repeatedly; global hooks disabled: {reason}";

					lock (hookStateLock)
					{
						kbdHook = 0;
						mouseHook = 0;
						// Record why we gave up so GetHookActivationFailureReason()/A_*HookInstalled reflect the
						// disabled state, rather than leaving a stale message from the last activation attempt (matches
						// how the normal disable path sets lastHookActivationFailure).
						lastHookActivationFailure = giveUpMessage;
					}

					SyncHookMutexes(changeIsTemporary: false);
					Diagnostics.Debug.WriteLine(giveUpMessage);
					return;
				}
			}
			else
			{
				inputdRecoveryAttempts = 1;
			}

			lastInputdRecoveryTicks = now;
			Diagnostics.Debug.WriteLine($"keysharp-inputd hook reader lost ({reason}); re-establishing hooks.");
			ChangePlatformHookState(want, changeIsTemporary: false, expectedGeneration: recoveryGeneration);

			if (CursorClipActive && !usingInputdHooks)
			{
				ClearCursorClip();
				Diagnostics.Debug.WriteLine("ClipCursor released because the keysharp-inputd mouse hook was lost.");
			}
		}

		private bool TryGetCursorPosThrottled(out POINT p)
		{
			if (IsWaylandSession)
			{
				var now = Stopwatch.GetTimestamp();

				if (now - lastClipQueryTicks < ClipQueryMinIntervalTicks)
				{
					p = default;
					return false;
				}

				lastClipQueryTicks = now;
			}

			return GetCursorPos(out p);
		}

		private void RequestCursorClipCorrection()
		{
			Interlocked.Increment(ref clipCorrectionRequest);

			if (Interlocked.CompareExchange(ref clipCorrectionWorkerActive, 1, 0) != 0)
				return;

			_ = Task.Run(RunCursorClipCorrectionAsync);
		}

		private async Task RunCursorClipCorrectionAsync()
		{
			var handledRequest = Volatile.Read(ref clipCorrectionRequest);

			try
			{
				while (CursorClipActive)
				{
					handledRequest = Volatile.Read(ref clipCorrectionRequest);
					await Task.Delay(ClipCorrectionDelayMs).ConfigureAwait(false);

					if (GetCursorPos(out var p))
					{
						int x = p.X, y = p.Y;

						if (ClampToCursorClip(ref x, ref y))
							_ = Platform.Mouse.TryMoveAbsolute(x, y);
					}

					if (handledRequest == Volatile.Read(ref clipCorrectionRequest))
						break;
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"ClipCursor correction failed: {ex.Message}");
			}

			Volatile.Write(ref clipCorrectionWorkerActive, 0);

			if (CursorClipActive
				&& handledRequest != Volatile.Read(ref clipCorrectionRequest)
				&& Interlocked.CompareExchange(ref clipCorrectionWorkerActive, 1, 0) == 0)
				_ = Task.Run(RunCursorClipCorrectionAsync);
		}

		private bool ProcessInputdMouseHook(KeysharpInputdClient.MouseHookEvent ev)
		{
			if (!mouseEnabled)
				return false;

			var isInjected = IsInputdInjected(ev.Flags, 0x01u);
			lastHookEventWasKeyboard = false;

			if (!isInjected)
			{
				script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;
			}

			switch (ev.Message)
			{
				case 0x0200u:
				{
					var isAbsolute = (ev.MouseData & (uint)MOUSEEVENTF.ABSOLUTE) != 0;
					var moveBlocked = !isInjected && script.KeyboardData.blockMouseMove;

					if (script.input != null
						&& !CollectMouseMove(ev.DeltaX, ev.DeltaY, ev.ExtraInfo, isInjected,
							ev.TimeMs is > 0 and <= long.MaxValue ? (long)ev.TimeMs : Environment.TickCount64,
							deviceId: ev.DeviceId, isAbsolute: isAbsolute))
						moveBlocked = true;

					if (!isInjected && CursorClipActive && TryGetCursorPosThrottled(out var clipPos))
					{
						int cx = clipPos.X, cy = clipPos.Y;

						if (ClampToCursorClip(ref cx, ref cy))
						{
							_ = Platform.Mouse.TryMoveAbsolute(cx, cy);
							return true;
						}
					}

					return moveBlocked;
				}
				case 0x020Au:
					return ProcessInputdMouseWheelHook(ev, vertical: true, isInjected);
				case 0x020Eu:
					return ProcessInputdMouseWheelHook(ev, vertical: false, isInjected);
			}

			// The daemon's evdev button events carry no cursor position (it stamps x=y=0). We deliberately do NOT
			// query the compositor from this hot hook-reader thread to synthesize one: on a relative mouse the daemon
			// has no absolute position to give, so the only source would be a GetCursorPos round-trip on every click,
			// inside the daemon's hook-decision deadline. Instead the click is reported WITHOUT a position (A_EventInfo
			// omits X/Y via HasPosition=false below), and any #HotIf predicate or callback that needs the location
			// resolves it itself on the script thread (e.g. MouseGetPos). The one exception is an active cursor clip,
			// which must read and clamp the live position anyway -- that value is real, so we pass it through.
			POINT clickPos = default;
			var haveClickPos = !isInjected && CursorClipActive && GetCursorPos(out clickPos);

			if (haveClickPos)
			{
				int bx = clickPos.X, by = clickPos.Y;

				if (ClampToCursorClip(ref bx, ref by))
				{
					_ = Platform.Mouse.TryMoveAbsolute(bx, by);
					clickPos = new POINT(bx, by);
				}
			}

			var (vk, keyUp) = ev.Message switch
			{
				0x0201u => (VK_LBUTTON, false),
				0x0202u => (VK_LBUTTON, true),
				0x0204u => (VK_RBUTTON, false),
				0x0205u => (VK_RBUTTON, true),
				0x0207u => (VK_MBUTTON, false),
				0x0208u => (VK_MBUTTON, true),
				0x020Bu => ((ev.MouseData >> 16) == MouseUtils.XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2, false),
				0x020Cu => ((ev.MouseData >> 16) == MouseUtils.XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2, true),
				_ => (0u, true)
			};

			if (vk == 0)
				return false;

			var args = new MouseHookEventArgs(
				keyUp ? EventType.MouseReleased : EventType.MousePressed,
				KeyCodes.VkToMouseButton(vk),
				// Only a cursor-clip read gives a real position here; a plain click has none (see above). When there
				// is none, report the CoordUnspecified (INT_MIN) sentinel -- InputHook OnMouseDown/OnMouseUp x/y
				// params can't be "unset", so a distinctive sentinel keeps a real (0,0) click from being confused
				// with a missing position (A_EventInfo omits X/Y outright via HasPosition below).
				haveClickPos ? clickPos.X : KeyboardMouseSender.CoordUnspecified,
				haveClickPos ? clickPos.Y : KeyboardMouseSender.CoordUnspecified,
				isInjected ? EventMask.SimulatedEvent : EventMask.None,
				ev.TimeMs)
			{
				HasPosition = haveClickPos
			};
			var result = LowLevelCommon(args, vk, 0, 0, keyUp, ev.ExtraInfo,
				isInjected ? HOOK_EVENT_INJECTED : 0, ev.DeviceId);
			return result != 0;
		}

		private bool ProcessInputdMouseWheelHook(KeysharpInputdClient.MouseHookEvent ev, bool vertical, bool isInjected)
		{
			var delta = unchecked((short)(ev.MouseData >> 16));
			var vk = vertical
				? (delta < 0 ? VK_WHEEL_DOWN : VK_WHEEL_UP)
				: (delta < 0 ? VK_WHEEL_LEFT : VK_WHEEL_RIGHT);
			var sc = (uint)delta;
			var args = new MouseWheelHookEventArgs(
				delta,
				vertical ? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Horizontal,
				// Wheel events carry no cursor position either: sentinel for the OnMouseWheel/OnMouse* x/y params,
				// and HasPosition=false so A_EventInfo omits X/Y (rather than reporting a misleading (0,0)).
				KeyboardMouseSender.CoordUnspecified,
				KeyboardMouseSender.CoordUnspecified,
				isInjected ? EventMask.SimulatedEvent : EventMask.None,
				ev.TimeMs)
			{
				HasPosition = false
			};
			var result = LowLevelCommon(args, vk, sc, sc, keyUp: false, ev.ExtraInfo,
				isInjected ? HOOK_EVENT_INJECTED : 0, deviceId: ev.DeviceId);
			return result != 0;
		}

		protected override bool CanClipCursor(out string reason)
		{
			if (!usingInputdHooks)
			{
				reason = "the keysharp-inputd mouse hook is not active";
				return false;
			}

			if (!Platform.Mouse.SupportsCursorQueryAndMove)
			{
				reason = "the cursor cannot be both queried and moved in this session";
				return false;
			}

			reason = "";
			return true;
		}
	}
}
#endif
