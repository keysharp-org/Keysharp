#if LINUX
using Keysharp.Builtins;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Input.Hooks.Unix;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Hooks.Linux
{
	// Linux hook extension points backed by keysharp-input.
	internal sealed class LinuxHookThread : UnixHookThread
	{
		internal LinuxHookThread(Script script, string mutexName) : base(script, mutexName) { }

		private readonly record struct CallbackContext(
			KeysharpInputClient Client,
			ulong EventId,
			HookType HookKinds);

		// A hook-originated Send belongs to this native callback thread. ThreadStatic
		// deliberately keeps queued hotkey work and bounded #HotIf tasks off this
		// callback stream; only this thread may synchronously read and re-enter it.
		[ThreadStatic]
		private static CallbackContext callbackContext;

		private KeysharpInputClient inputServiceHookClient;
		private CancellationTokenSource inputServiceHookCancel;
		private Task inputServiceHookTask;
		private Task foregroundTrackingTask = Task.CompletedTask;
		private volatile HookType inputServiceCommittedKinds;
		private readonly object recoveryLock = new();
		private bool recoveryRunning;
		private string pendingRecoveryReason;
		private HookType inputServiceSubscribedKinds;
		private bool usingInputServiceHooks;
		internal static bool IsInHookCallback => callbackContext.Client != null;
		internal static KeysharpInputClient CurrentHookClient
			=> callbackContext.Client;
		internal static ulong CurrentHookEventId
			=> callbackContext.EventId;
		// Avoid grabbing evdev before X/XWayland can receive inputService's replay device.
		private const int InputServiceGrabDisplayWaitMs = 5000;
		private const int InputServiceGrabDisplayWaitPollMs = 100;

		// Crash-loop protection for inputService hook recovery.
		private long lastInputServiceRecoveryTicks;
		private int inputServiceRecoveryAttempts;
		private const long InputServiceRecoveryWindowMs = 5000;
		private const int MaxInputServiceRecoveryAttempts = 3;

		// Wayland cursor queries are IPC, so throttle ClipCursor correction.
		private long lastClipQueryTicks;
		private const int ClipCorrectionDelayMs = 8;
		private static readonly long ClipQueryMinIntervalTicks = System.Diagnostics.Stopwatch.Frequency * ClipCorrectionDelayMs / 1000;
		private int clipCorrectionRequest;
		private int clipCorrectionWorkerActive;

		protected override KeyboardMouseSender CreateKbdMsSender()
			=> new LinuxKeyboardMouseSender(script);

		// Hook events keep modifier state current locally, avoiding IPC on the hotkey path.
		internal override bool IsKeyDownLogical(uint vk)
		{
			if (usingInputServiceHooks)
			{
				var modMask = ModifierLRMaskFromVK(vk);

				if (modMask != 0)
					return (kbdMsSender.modifiersLRLogical & modMask) != 0;
			}

			return base.IsKeyDownLogical(vk);
		}

		protected override void StopPlatformHookCore(bool dispose)
		{
			StopInputServiceHookCore();
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
				"Keysharp: global keyboard/mouse hooks require the keysharp-input helper on Wayland. " +
				"Install and enable it (re-run the installer, or " +
				"'sudo keysharp-input daemon --install-input-access') to enable global input capture on Wayland.");
		}

		private bool ProcessInputServiceKeyboardHook(
			KeysharpInputClient.KeyboardHookEvent ev,
			HookType hookKinds)
		{
			if ((hookKinds & HookType.Keyboard) == 0)
				return false;

			var vk = ev.VkCode;
			var sc = ev.ScanCode <= SC_MAX ? ev.ScanCode : 0u;
			var flags = (KeysharpInputClient.HookFlags)ev.Flags;
			var keyUp = (flags & KeysharpInputClient.HookFlags.KeyboardUp) != 0;
			var isInjected = (flags & KeysharpInputClient.HookFlags.KeyboardInjected) != 0;
			Keysharp.Internals.NativeInputKeyboard.UpdateIndicatorSnapshotFromHookFlags(ev.Flags);

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
			var numLockOn = Keysharp.Internals.NativeInputKeyboard.HookFlagsNumLockOn(ev.Flags);
			var shiftDown = (kbdMsSender.modifiersLRLogical & (MOD_LSHIFT | MOD_RSHIFT)) != 0;

			vk = KeyCodes.ApplyNumpadState(vk, numLockOn, shiftDown);

			if (vk == 0 && sc == 0)
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

			var deadline = Environment.TickCount64 + InputServiceGrabDisplayWaitMs;

			while (Environment.TickCount64 < deadline && !IsX11Available)
				Thread.Sleep(InputServiceGrabDisplayWaitPollMs);
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

			var hookRunning = inputServiceHookClient != null
				&& inputServiceHookTask != null
				&& !inputServiceHookTask.IsCompleted;

			if (hookRunning && inputServiceSubscribedKinds == wantedHooks)
				return true;

			WaitForDisplayServerBeforeGrab();
			StopInputServiceHookCore();

			var required = KeysharpInputClient.Operations.None;

			if (wantKeyboard)
				required |= KeysharpInputClient.Operations.HookKeyboard;

			if (wantMouse)
				required |= KeysharpInputClient.Operations.HookMouse;

			// A callback stream can suppress events, so opening one requires both powers.
			required |= KeysharpInputClient.Operations.BlockInput;
			var permission = KeysharpInputManager.EnsureOperations(required,
				"install keyboard/mouse hooks");

			if (!permission.IsGranted)
			{
				message = $"keysharp-input hook unavailable; global hooks disabled. {permission.Message}";
				return false;
			}

			try
			{
				inputServiceHookClient = KeysharpInputClient.Connect(
					required,
					role: KeysharpInputClient.ConnectionRole.CallbackStream);
				inputServiceHookClient.SetHookQuarantineHandler(HandleHookQuarantined);
				inputServiceHookClient.SetNestedHookEventHandler(ProcessNestedHookEvent);

				if (wantMouse)
					_ = inputServiceHookClient.SubscribeHook(KeysharpInputClient.HookType.MouseLowLevel);

				if (wantKeyboard)
					_ = inputServiceHookClient.SubscribeHook(KeysharpInputClient.HookType.KeyboardLowLevel);

				inputServiceHookCancel = new CancellationTokenSource();
				inputServiceSubscribedKinds = wantedHooks;
				return true;
			}
			catch (Exception ex)
			{
				StopInputServiceHookCore();
				message = $"keysharp-input hook unavailable; global hooks disabled. {ex.Message}";
				return false;
			}
		}

		private void HandleHookQuarantined(KeysharpInputClient.HookQuarantine quarantine)
		{
			Diagnostics.Debug.WriteLine(
				$"keysharp-input quarantined {quarantine.HookType} hook at event {quarantine.EventId}; " +
				$"strike {quarantine.StrikeCount}; the service will retry it after {quarantine.RetryAfterMs} ms.");
		}

		private void StopInputServiceHookCore()
		{
			var cancellation = inputServiceHookCancel;
			inputServiceHookCancel = null;
			usingInputServiceHooks = false;
			inputServiceCommittedKinds = HookType.None;
			inputServiceSubscribedKinds = HookType.None;
			try { cancellation?.Cancel(); } catch { }
			inputServiceHookClient?.SetLeaseLivenessProbe(static () => false);
			try { if (inputServiceHookTask != null && !inputServiceHookTask.IsCompleted) inputServiceHookTask.Wait(750); } catch { }
			try { inputServiceHookClient?.Dispose(); } catch { }
			cancellation?.Dispose();
			inputServiceHookClient = null;
			inputServiceHookTask = null;
		}

		protected override void OnPlatformHookStateCommitted(HookType activeHooks)
		{
			// WinEvent setup can connect or join; keep hook transitions ordered without holding hookStateLock for it.
			foregroundTrackingTask = foregroundTrackingTask.ContinueWith(
				_ => script.WinEventManager.SetForegroundTracking(
					(activeHooks & HookType.Keyboard) != 0),
				CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

			if (activeHooks == HookType.None)
			{
				inputServiceCommittedKinds = HookType.None;
				usingInputServiceHooks = false;
				return;
			}

			if (inputServiceHookTask != null && !inputServiceHookTask.IsCompleted)
			{
				inputServiceCommittedKinds = activeHooks;
				usingInputServiceHooks = true;
				return;
			}

			var hookClient = inputServiceHookClient;
			var hookToken = inputServiceHookCancel.Token;
			usingInputServiceHooks = true;
			inputServiceCommittedKinds = activeHooks;
			inputServiceHookTask = Task.Run(() => InputServiceHookLoop(hookClient, hookToken));
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

		private void InputServiceHookLoop(KeysharpInputClient client, CancellationToken token)
		{
			var liveness = new HookReaderLiveness();
			client.SetLeaseLivenessProbe(liveness.IsAlive);

			try
			{
				InputServiceHookLoopCore(client, token, liveness);
			}
			finally
			{
				client.SetLeaseLivenessProbe(static () => false);
			}
		}

		private void InputServiceHookLoopCore(
			KeysharpInputClient client,
			CancellationToken token,
			HookReaderLiveness liveness)
		{
			while (!token.IsCancellationRequested)
			{
				KeysharpInputClient.HookEvent hookEvent;
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
						Diagnostics.Debug.WriteLine($"keysharp-input hook reader stopped: {ex.Message}");
						HandleInputServiceHookReaderLoss(ex.Message);
					}

					return;
				}

				if (token.IsCancellationRequested)
					return;

				liveness.MarkProgress();

				try
				{
					ProcessAndDecideHookEvent(
						client,
						hookEvent,
						inputServiceCommittedKinds);
				}
				catch (Exception ex)
				{
					if (!token.IsCancellationRequested)
					{
						Diagnostics.Debug.WriteLine($"keysharp-input hook decision failed: {ex.Message}");
						HandleInputServiceHookReaderLoss(ex.Message);
					}

					return;
				}
			}
		}

		private void ProcessNestedHookEvent(
			KeysharpInputClient client,
			KeysharpInputClient.HookEvent hookEvent)
		{
			var hookKinds = ReferenceEquals(callbackContext.Client, client)
				? callbackContext.HookKinds
				: inputServiceCommittedKinds;
			ProcessAndDecideHookEvent(client, hookEvent, hookKinds);
		}

		private void ProcessAndDecideHookEvent(
			KeysharpInputClient client,
			in KeysharpInputClient.HookEvent hookEvent,
			HookType hookKinds)
		{
			var previousContext = callbackContext;
			var block = false;

			callbackContext = new(client, hookEvent.EventId, hookKinds);
			using var hotIfBudget = BeginHotIfCallback(HotIfCallbackBudgetMilliseconds);

			try
			{
				try
				{
					block = hookEvent.HookType switch
					{
						KeysharpInputClient.HookType.KeyboardLowLevel => ProcessInputServiceKeyboardHook(hookEvent.Keyboard, hookKinds),
						KeysharpInputClient.HookType.MouseLowLevel => ProcessInputServiceMouseHook(hookEvent.Mouse, hookKinds),
						_ => false
					};
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"keysharp-input hook event processing failed: {ex}");
				}

				client.SendHookDecision(hookEvent.EventId,
					block ? KeysharpInputClient.HookDecision.Block : KeysharpInputClient.HookDecision.Pass);
			}
			finally
			{
				callbackContext = previousContext;
			}

			if (!block
				&& CursorClipActive
				&& hookEvent.HookType == KeysharpInputClient.HookType.MouseLowLevel
				&& hookEvent.Mouse.Message == (uint)KeysharpInputClient.MessageKind.MouseMove
				&& ((KeysharpInputClient.HookFlags)hookEvent.Mouse.Flags
					& KeysharpInputClient.HookFlags.MouseInjected) == 0)
				RequestCursorClipCorrection();

		}

		private void HandleInputServiceHookReaderLoss(string reason)
		{
			lock (recoveryLock)
			{
				pendingRecoveryReason = reason ?? string.Empty;

				if (recoveryRunning)
					return;

				recoveryRunning = true;
				_ = Task.Run(() =>
				{
					while (true)
					{
						string pending;

						lock (recoveryLock)
						{
							pending = pendingRecoveryReason;
							pendingRecoveryReason = null;

							if (pending == null)
							{
								recoveryRunning = false;
								return;
							}
						}

						try { RecoverInputServiceHooks(pending); }
						catch (Exception ex)
						{
							Diagnostics.Debug.WriteLine($"keysharp-input hook recovery failed: {ex}");
						}
					}
				});
			}
		}

		private void RecoverInputServiceHooks(string reason)
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
				StopInputServiceHookCore();
			}

			if (want == HookType.None)
				return;

			var now = Environment.TickCount64;

			if (now - lastInputServiceRecoveryTicks < InputServiceRecoveryWindowMs)
			{
				if (++inputServiceRecoveryAttempts >= MaxInputServiceRecoveryAttempts)
				{
					if (CursorClipActive)
						ClearCursorClip();

					// Complete hook teardown so status and cross-process mutexes stay accurate.
					var giveUpMessage = $"keysharp-input hooks lost repeatedly; global hooks disabled: {reason}";

					lock (hookStateLock)
					{
						kbdHook = 0;
						mouseHook = 0;
						lastHookActivationFailure = giveUpMessage;
					}

					SyncHookMutexes(changeIsTemporary: false);
					Diagnostics.Debug.WriteLine(giveUpMessage);
					return;
				}
			}
			else
			{
				inputServiceRecoveryAttempts = 1;
			}

			lastInputServiceRecoveryTicks = now;
			Diagnostics.Debug.WriteLine($"keysharp-input hook reader lost ({reason}); re-establishing hooks.");
			ChangePlatformHookState(want, changeIsTemporary: false, expectedGeneration: recoveryGeneration);

			if (CursorClipActive && !usingInputServiceHooks)
			{
				ClearCursorClip();
				Diagnostics.Debug.WriteLine("ClipCursor released because the keysharp-input mouse hook was lost.");
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

		private bool ProcessInputServiceMouseHook(
			KeysharpInputClient.MouseHookEvent ev,
			HookType hookKinds)
		{
			if ((hookKinds & HookType.Mouse) == 0)
				return false;

			var isInjected = ((KeysharpInputClient.HookFlags)ev.Flags
				& KeysharpInputClient.HookFlags.MouseInjected) != 0;
			lastHookEventWasKeyboard = false;

			if (!isInjected)
			{
				script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;
			}

			switch ((KeysharpInputClient.MessageKind)ev.Message)
			{
				case KeysharpInputClient.MessageKind.MouseMove:
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
				case KeysharpInputClient.MessageKind.MouseWheel:
					return ProcessInputServiceMouseWheelHook(ev, vertical: true, isInjected);
				case KeysharpInputClient.MessageKind.MouseHorizontalWheel:
					return ProcessInputServiceMouseWheelHook(ev, vertical: false, isInjected);
			}

			// Evdev button events have no absolute position. Avoid compositor IPC on the
			// hook-decision path unless cursor clipping already requires the position.
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

			var (vk, keyUp) = (KeysharpInputClient.MessageKind)ev.Message switch
			{
				KeysharpInputClient.MessageKind.LeftButtonDown => (VK_LBUTTON, false),
				KeysharpInputClient.MessageKind.LeftButtonUp => (VK_LBUTTON, true),
				KeysharpInputClient.MessageKind.RightButtonDown => (VK_RBUTTON, false),
				KeysharpInputClient.MessageKind.RightButtonUp => (VK_RBUTTON, true),
				KeysharpInputClient.MessageKind.MiddleButtonDown => (VK_MBUTTON, false),
				KeysharpInputClient.MessageKind.MiddleButtonUp => (VK_MBUTTON, true),
				KeysharpInputClient.MessageKind.XButtonDown
					=> ((ev.MouseData >> 16) == MouseUtils.XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2, false),
				KeysharpInputClient.MessageKind.XButtonUp
					=> ((ev.MouseData >> 16) == MouseUtils.XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2, true),
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

		private bool ProcessInputServiceMouseWheelHook(KeysharpInputClient.MouseHookEvent ev, bool vertical, bool isInjected)
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
			if (!usingInputServiceHooks)
			{
				reason = "the keysharp-input mouse hook is not active";
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
