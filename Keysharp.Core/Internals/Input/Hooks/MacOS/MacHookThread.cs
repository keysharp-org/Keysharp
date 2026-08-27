using Keysharp.Builtins;
#if OSX
using System.Runtime.InteropServices;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.MacOS;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Hooks.MacOS
{
	// macOS uses a native CGEvent sender/tap while still reusing UnixHookThread's
	// higher-level hotkey, hotstring and InputHook decision pipeline.
	internal sealed class MacHookThread : Keysharp.Internals.Input.Hooks.Unix.UnixHookThread
	{
		internal MacHookThread(Script script, string mutexName) : base(script, mutexName) { }

		private const int HookStartTimeoutMs = 3000;
		private MacNativeEventTap nativeEventTap;
		private readonly MacKeyboardState keyboardState = new();
		private readonly MacMouseEventStream mouseStream = new();
		private volatile bool cursorDisassociated;

		[DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

		protected override void EnsureCursorClipPermissions()
		{
			base.EnsureCursorClipPermissions();
			_ = script.Permissions.EnsureInputInjection(operation: "ClipCursor");
		}

		protected override bool CanClipCursor(out string reason)
		{
			var active = HasMouseHook();
			reason = active ? "" : "the global mouse hook is not active";
			return active;
		}

		protected override void MoveCursorForClip(int x, int y)
		{
			lock (moveSuppressionLock)
			{
				_ = MacNativeInput.CGWarpMouseCursorPosition(new MacNativeInput.CGPoint(x, y));

				if (!cursorDisassociated)
					_ = CGAssociateMouseAndMouseCursorPosition(1);
			}
		}

		// Freeze/unfreeze physical cursor movement for BlockInput's mouse-move block and an InputHook with
		// VisibleMouseMove := false. The session event tap is downstream of the OS
		// cursor mover, so setting SuppressEvent on a move only hides it from applications -- the visible
		// cursor has already moved. Disassociating the cursor from the mouse is what actually stops it;
		// move events keep flowing to the tap (so callbacks still fire and we can re-associate on release).
		// macOS restores the association automatically when the process exits, and DeregisterHooks resets
		// it, so a decoupled cursor can't outlive the script.
		protected override bool OnMoveSuppressionChanged(bool active)
		{
			if (!active && !cursorDisassociated)
				return true;

			var error = CGAssociateMouseAndMouseCursorPosition(active ? 0 : 1);
			if (error == 0)
			{
				cursorDisassociated = active;
				return true;
			}

			Diagnostics.Debug.WriteLine($"macOS cursor {(active ? "disassociation" : "reassociation")} failed with CGError {error}.");
			return false;
		}

		internal override void RefreshPlatformKeyGrabs()
		{
			if (!HasMouseHook())
			{
				SetMoveSuppression(false);
				return;
			}

			var suppress = script.KeyboardData.blockMouseMove || script.KeyboardData.blockInput;

			for (var input = script.input; !suppress && input != null; input = input.prev)
				suppress = input.InProgress() && !input.visibleMouseMove;

			SetMoveSuppression(suppress);
		}

		protected override KeyboardMouseSender CreateKbdMsSender()
			=> new MacKeyboardMouseSender(script, keyboardState, mouseStream);

		// macOS App Switcher uses Cmd+Tab, not Alt+Tab.
		protected override uint AltTabModifierVk => VK_LWIN;
		protected override uint AltTabModifierMask => MOD_LWIN | MOD_RWIN;
		protected override bool IsAltTabModifierVk(uint vk) => vk == VK_LWIN || vk == VK_RWIN;

		protected override uint CurrentAltTabModifierVk
		{
			get
			{
				if ((kbdMsSender.modifiersLRLogical & MOD_LWIN) != 0)
					return VK_LWIN;
				if ((kbdMsSender.modifiersLRLogical & MOD_RWIN) != 0)
					return VK_RWIN;
				return 0;
			}
		}

		// macOS hook SC values are raw kVK codes (resolved via KeyCodes' hardcoded kVK table).
		// Keep these 0 so modifier handling stays VK-based and the SC modifier tables aren't
		// seeded from kVK codes.
		internal override uint SC_LCONTROL => 0;
		internal override uint SC_RCONTROL => 0;
		internal override uint SC_LALT => 0;
		internal override uint SC_RALT => 0;
		internal override uint SC_LSHIFT => 0;
		internal override uint SC_RSHIFT => 0;
		internal override uint SC_LWIN => 0;
		internal override uint SC_RWIN => 0;

		// macOS has no process-local BlockInput equivalent, so the native tap suppresses
		// physical events directly while allowing Keysharp's own injected events through.
		private bool ShouldSuppressForBlockInput(bool isInjected) =>
			!isInjected && script.KeyboardData.blockInput;

		internal bool ProcessNativeKeyboardEvent(uint type, nint cgEvent)
		{
			if (!MacNativeInput.IsKeyboardEvent(type))
				return false;
			var senderRevisions = keyboardState.SenderRevisions;

			var flags = MacNativeInput.CGEventGetFlags(cgEvent);
			var rawExtraInfo = MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGEventSourceUserData);
			var hasMetadata = MacNativeInput.TryDecodeInjectedExtraInfo(rawExtraInfo, out var decodedExtraInfo, out var injectedKind);
			var extraInfo = unchecked((ulong)(hasMetadata ? decodedExtraInfo : rawExtraInfo));
			var origin = MacNativeInput.ClassifyEventOrigin(cgEvent, hasMetadata);
			var isInjected = origin is MacKeyboardState.Origin.KeysharpSynthetic or MacKeyboardState.Origin.ForeignSynthetic;

			// Unicode identity is explicit metadata. Ordinary CGEventCreateKeyboardEvent events also have a
			// layout-derived Unicode string, so inspecting the string itself cannot distinguish text injection.
			if (hasMetadata && (injectedKind & MacNativeInput.InjectedEventKind.UnicodeText) != 0)
			{
				if (!keyboardEnabled)
					return false;

				Span<char> unicodeBuffer = stackalloc char[2];

				if ((injectedKind & MacNativeInput.InjectedEventKind.KeyUp) == 0
					&& MacNativeInput.TryGetKeyboardUnicodeString(cgEvent, unicodeBuffer, out var unicodeLength))
					return ProcessSyntheticUnicodeText(unicodeBuffer[..unicodeLength], flags, extraInfo);

				return false; // The paired text key-up carries no separate AHK input.
			}

			var sc = (uint)MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGKeyboardEventKeycode);
			if (!KeyCodes.TryMapMacCodeToVk(sc, out var vk) || vk == 0)
				return false;

			bool keyUp;
			if (type == MacNativeInput.kCGEventFlagsChanged)
			{
				bool? queriedDown = null;
				var querySource = origin switch
				{
					MacKeyboardState.Origin.PhysicalHid => MacNativeInput.kCGEventSourceStateHIDSystemState,
					MacKeyboardState.Origin.ForeignSynthetic => MacNativeInput.kCGEventSourceStateCombinedSessionState,
					_ => uint.MaxValue
				};
				if (querySource != uint.MaxValue && MacKeyboardState.TryQuery(vk, querySource, out var nativeDown))
					queriedDown = nativeDown;

				keyUp = keyboardState.ApplyFlagsChanged(vk, flags, origin, queriedDown, injectedKind,
					senderRevisions.Modifiers, senderRevisions.CapsLock);

				// A mouse-only tap subscribes to flagsChanged solely to keep exact LR state.
				if (!keyboardEnabled)
					return false;
			}
			else
			{
				if (!keyboardEnabled)
					return false;

				keyUp = type == MacNativeInput.kCGEventKeyUp;
				if (SideSpecificModifierLRMaskFromVK(vk) != 0 || vk == VK_CAPITAL)
					_ = keyboardState.ApplyFlagsChanged(vk, flags, origin, !keyUp,
						injectedKind, senderRevisions.Modifiers, senderRevisions.CapsLock);
			}

			var mask = keyboardState.ToEventMask(flags);
			if (isInjected)
				mask |= EventMask.SimulatedEvent;
			Keysharp.Internals.MacKeyboard.UpdateIndicatorSnapshotFromMask(mask);

			var args = new KeyboardHookEventArgs(keyUp ? EventType.KeyReleased : EventType.KeyPressed, vk, sc, mask)
			{
				IsAutoRepeat = type == MacNativeInput.kCGEventKeyDown
					&& MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGKeyboardEventAutorepeat) != 0
			};
			return ProcessNativeKeyboardEvent(args, vk, sc, keyUp, extraInfo);
		}

		private bool ProcessSyntheticUnicodeText(ReadOnlySpan<char> chars, ulong flags, ulong extraInfo)
		{
			var mask = keyboardState.ToEventMask(flags) | EventMask.SimulatedEvent;
			Keysharp.Internals.MacKeyboard.UpdateIndicatorSnapshotFromMask(mask);
			var suppress = false;

			for (var i = 0; i < chars.Length; i++)
			{
				var args = new KeyboardHookEventArgs(EventType.KeyPressed, VK_PACKET, chars[i], mask);
				_ = ProcessNativeKeyboardEvent(args, VK_PACKET, chars[i], keyUp: false, extraInfo);
				suppress |= args.SuppressEvent;
			}

			return suppress;
		}

		private bool ProcessNativeKeyboardEvent(KeyboardHookEventArgs args, uint vk, uint sc, bool keyUp, ulong eventExtraInfo)
		{
			lastHookEventWasKeyboard = true;
			lastKeyboardEventVk = vk;
			var isInjected = HasKeysharpInjectedExtraInfo(eventExtraInfo) || args.IsEventSimulated;

			if (ShouldSuppressForBlockInput(isInjected))
			{
				UpdateObservedPhysicalKeyState(vk, keyUp, isInjected);
				if (!keyUp && !isInjected)
					OnPhysicalKeyDownSuppressed(vk);
				args.SuppressEvent = true;
				return true;
			}

			if (eventExtraInfo == (ulong)KeyBlockThis)
			{
				args.SuppressEvent = true;
				return true;
			}

			var result = LowLevelCommon(args, vk, sc, sc, keyUp, eventExtraInfo, isInjected ? HOOK_EVENT_INJECTED : 0);
			ApplyKeyStateAfterKeyboardDecision(vk, keyUp, isInjected, result);

			if (result != 0)
			{
				if (!keyUp && !isInjected)
					OnPhysicalKeyDownSuppressed(vk);
				args.SuppressEvent = true;
			}

			return args.SuppressEvent;
		}

		internal bool ProcessNativeMouseEvent(uint type, nint cgEvent)
		{
			if (!MacNativeInput.IsMouseEvent(type))
				return false;
			if (!mouseEnabled)
				return false;

			var streamRevision = mouseStream.SenderRevision;

			var flags = MacNativeInput.CGEventGetFlags(cgEvent);
			var isMoveEvent = type == MacNativeInput.kCGEventMouseMoved
				|| type == MacNativeInput.kCGEventLeftMouseDragged
				|| type == MacNativeInput.kCGEventRightMouseDragged
				|| type == MacNativeInput.kCGEventOtherMouseDragged;
			var rawExtraInfo = MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGEventSourceUserData);
			var hasMetadata = MacNativeInput.TryDecodeInjectedExtraInfo(rawExtraInfo, out var decodedExtraInfo, out _);
			var extraInfo = unchecked((ulong)(hasMetadata ? decodedExtraInfo : rawExtraInfo));
			var mask = keyboardState.ToEventMask(flags);
			var origin = MacNativeInput.ClassifyEventOrigin(cgEvent, hasMetadata);
			var isInjected = origin is MacKeyboardState.Origin.KeysharpSynthetic or MacKeyboardState.Origin.ForeignSynthetic;

			if (isInjected)
				mask |= EventMask.SimulatedEvent;

			var loc = MacNativeInput.CGEventGetLocation(cgEvent);
			var x = (int)Math.Round(loc.X);
			var y = (int)Math.Round(loc.Y);
			var wasCursorDisassociated = cursorDisassociated;
			var wantsMove = isMoveEvent && script.input != null;
			long rawDeltaX = 0, rawDeltaY = 0;

			if (wantsMove)
			{
				rawDeltaX = MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGMouseEventDeltaX);
				rawDeltaY = MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGMouseEventDeltaY);
			}

			if (isMoveEvent)
			{
				lastHookEventWasKeyboard = false;
				var suppressMove = !isInjected
					&& (script.KeyboardData.blockMouseMove || script.KeyboardData.blockInput);

				if (wantsMove)
				{
					if (!CollectMouseMove(rawDeltaX, rawDeltaY, extraInfo, isInjected,
							DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new POINT(x, y)))
						suppressMove = true;
				}

				if (!isInjected)
				{
					script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;

					if (CursorClipActive)
					{
						var cx = x;
						var cy = y;

						if (ClampToCursorClip(ref cx, ref cy))
						{
							mouseStream.InvalidatePosition();
							MoveCursorForClip(cx, cy);
							return true;
						}
					}
				}

				if (suppressMove || wasCursorDisassociated)
					mouseStream.InvalidatePosition();
				else if (origin != MacKeyboardState.Origin.KeysharpSynthetic)
						// The sender records its own destination before asynchronously posting the event;
						// observing that event later could roll a newer prediction backward. Only accepted
						// physical/foreign moves refresh the baseline here. Committing after suppression
						// has been decided prevents a blocked event from becoming the baseline.
						mouseStream.ObserveMove(x, y, streamRevision);

				return suppressMove;
			}

			if (type == MacNativeInput.kCGEventScrollWheel)
			{
				lastHookEventWasKeyboard = false;

				if (!isInjected)
					script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;

				var vertical = ReadScrollDelta(cgEvent, MacNativeInput.kCGScrollWheelEventDeltaAxis1,
					MacNativeInput.kCGScrollWheelEventPointDeltaAxis1, MacNativeInput.kCGScrollWheelEventFixedPtDeltaAxis1);
				var horizontal = ReadScrollDelta(cgEvent, MacNativeInput.kCGScrollWheelEventDeltaAxis2,
					MacNativeInput.kCGScrollWheelEventPointDeltaAxis2, MacNativeInput.kCGScrollWheelEventFixedPtDeltaAxis2);

				if (ShouldSuppressForBlockInput(isInjected))
					return true;

				// A trackpad event can carry both axes. Process both rather than discarding the
				// smaller component; suppressing either logical wheel event suppresses the source.
				var suppress = ProcessAxis(vertical, MouseWheelScrollDirection.Vertical);
				suppress |= ProcessAxis(horizontal, MouseWheelScrollDirection.Horizontal);
				return suppress;

				bool ProcessAxis(double preciseDelta, MouseWheelScrollDirection direction)
				{
					if (preciseDelta == 0)
						return false;

					var delta = Math.Sign(preciseDelta) * Math.Max(1, (int)Math.Round(Math.Abs(preciseDelta)));
					var horizontalAxis = direction == MouseWheelScrollDirection.Horizontal;
					var wheelVk = horizontalAxis
						? (delta < 0 ? VK_WHEEL_LEFT : VK_WHEEL_RIGHT)
						: (delta < 0 ? VK_WHEEL_DOWN : VK_WHEEL_UP);
					var wheelSc = unchecked((uint)delta);
					var args = new MouseWheelHookEventArgs(delta, direction, x, y, mask);
					return LowLevelCommon(args, wheelVk, wheelSc, wheelSc, keyUp: false, extraInfo,
						isInjected ? HOOK_EVENT_INJECTED : 0) != 0;
				}
			}

			var buttonNumber = (uint)MacNativeInput.CGEventGetIntegerValueField(cgEvent, MacNativeInput.kCGMouseEventButtonNumber);
			var button = MacNativeInput.ToMouseButton(type, buttonNumber);

			if (button == MouseButton.NoButton)
				return false;

			var keyUp = type == MacNativeInput.kCGEventLeftMouseUp
				|| type == MacNativeInput.kCGEventRightMouseUp
				|| type == MacNativeInput.kCGEventOtherMouseUp;

			lastHookEventWasKeyboard = false;

			if (!isInjected)
				script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;

			var buttonVk = button switch
			{
				MouseButton.Button1 => VK_LBUTTON,
				MouseButton.Button2 => VK_RBUTTON,
				MouseButton.Button3 => VK_MBUTTON,
				MouseButton.Button4 => VK_XBUTTON1,
				MouseButton.Button5 => VK_XBUTTON2,
				_ => 0u
			};

			if (buttonVk == 0)
				return false;

			if (ShouldSuppressForBlockInput(isInjected))
				return true;

			var clickCount = (int)Math.Clamp(MacNativeInput.CGEventGetIntegerValueField(cgEvent,
				MacNativeInput.kCGMouseEventClickState), 1, int.MaxValue);
			var mouseArgs = new MouseHookEventArgs(keyUp ? EventType.MouseReleased : EventType.MousePressed,
				button, x, y, mask, clicks: clickCount);
			var buttonResult = LowLevelCommon(mouseArgs, buttonVk, 0, 0, keyUp, extraInfo, isInjected ? HOOK_EVENT_INJECTED : 0);
			if (buttonResult == 0 && origin != MacKeyboardState.Origin.KeysharpSynthetic)
				mouseStream.ObserveButton(button, !keyUp, x, y, clickCount, streamRevision);
			return buttonResult != 0;
		}

		internal static double ReadScrollDelta(nint cgEvent, uint lineField, uint pointField, uint fixedField)
		{
			var line = MacNativeInput.CGEventGetIntegerValueField(cgEvent, lineField);
			var fixedLine = MacNativeInput.CGEventGetIntegerValueField(cgEvent, fixedField) / 65536.0;
			var continuous = MacNativeInput.CGEventGetIntegerValueField(cgEvent,
				MacNativeInput.kCGScrollWheelEventIsContinuous) != 0;

			// Windows and inputd expose wheel deltas in WHEEL_DELTA (120) units. Quartz line
			// fields are notch/line units, while the fixed field preserves fractional lines for
			// continuous devices. Select the precise representation without changing the public unit.
			if (continuous && fixedLine != 0)
				return fixedLine * MouseUtils.WHEEL_DELTA;
			if (line != 0)
				return line * MouseUtils.WHEEL_DELTA;
			if (fixedLine != 0)
				return fixedLine * MouseUtils.WHEEL_DELTA;

			var point = MacNativeInput.CGEventGetDoubleValueField(cgEvent, pointField);
			if (point != 0)
				return point;

			return 0;
		}

		internal void OnNativeTapReenabled()
		{
			keyboardState.Resync();
			mouseStream.ResyncButtons();
		}

		internal void OnNativeTapTerminated(MacNativeEventTap failedTap, string reason)
		{
			lock (hookStateLock)
			{
				if (!ReferenceEquals(nativeEventTap, failedTap) || (!keyboardEnabled && !mouseEnabled))
					return;
			}
			DisableHooksAfterTapLoss(reason);
		}

		private void DisableHooksAfterTapLoss(string reason)
		{
			var message = $"macOS event tap was lost repeatedly; global hooks disabled: {reason}";
			lock (hookStateLock)
			{
				keyboardEnabled = false;
				mouseEnabled = false;
				kbdHook = 0;
				mouseHook = 0;
				lastHookActivationFailure = message;
			}

			if (CursorClipActive)
				ClearCursorClip();
			SetMoveSuppression(false);
			mouseStream.ResetObservedButtons();
			SyncHookMutexes(changeIsTemporary: false);
			Diagnostics.Debug.WriteLine(message);
		}

		protected override string PlatformHookDisabledMessage => "macOS hook disabled via KEYSHARP_DISABLE_HOOK=1.";

		protected override void OnPlatformHookStartFailed(string message) => SetMoveSuppression(false);

		protected override bool StartPlatformHookCore(bool wantKeyboard, bool wantMouse, out string message)
		{
			message = string.Empty;

			if (!wantKeyboard && !wantMouse)
				return false;

			if (Script.IsUiInitializationBlocked)
				return true;

			var requestedMask = MacNativeInput.EventMaskFor(wantKeyboard, wantMouse);
			if (nativeEventTap is { IsRunning: true, EventMask: var activeMask } && activeMask == requestedMask)
				return true;

			if (!TryDisposeNativeTap(out var disposalFailure))
			{
				message = $"Global hook failed on macOS: {disposalFailure}";
				return false;
			}

			_ = script.Permissions.EnsureInputMonitoring(operation: "install keyboard/mouse hooks");
			// This is the only synchronous UI/layout preparation point. Once the native tap starts,
			// key mapping is snapshot-only and can never wait on the UI thread.
			KeyCodes.PrepareForInputHook(script);
			keyboardState.Resync();
			mouseStream.ResyncButtons();
			nativeEventTap = new MacNativeEventTap(this, requestedMask);

			if (nativeEventTap.Start(HookStartTimeoutMs))
				return true;

			var startupFailure = nativeEventTap.StartupFailure ?? "event tap startup failed";
			if (!TryDisposeNativeTap(out disposalFailure))
				startupFailure += $"; {disposalFailure}";
			mouseStream.ResetObservedButtons();
			message = $"Global hook failed on macOS: {startupFailure}. Check Accessibility/Input Monitoring permissions.";
			return false;
		}

		private bool TryDisposeNativeTap(out string failure)
		{
			failure = string.Empty;
			if (nativeEventTap == null)
				return true;

			try
			{
				nativeEventTap.Dispose();
				nativeEventTap = null;
				return true;
			}
			catch (TimeoutException ex)
			{
				// Keep the object rooted: its callback delegate and owner must remain alive until
				// the native thread exits, even though hook startup cannot proceed.
				failure = ex.Message;
				return false;
			}
		}

		protected override void StopPlatformHookCore(bool dispose)
		{
			if (!dispose)
				return;

			try
			{
				nativeEventTap?.Dispose();
				nativeEventTap = null;
			}
			finally
			{
				// Always restore cursor association and the base hook state, even if a native
				// tap thread refuses to terminate and its ownership must be retained.
				mouseStream.ResetObservedButtons();
				base.StopPlatformHookCore(dispose);
			}
		}

		protected override void OnPhysicalKeyDownSuppressed(uint vk)
		{
			// The HID driver toggles the CapsLock lock state (and LED) before the
			// event tap sees the key-down, so suppression alone can't prevent the
			// toggle. Undo it so a suppressed CapsLock press (prefix key, remap,
			// AlwaysOn/Off) behaves as if the key was never pressed.
			if (vk == VK_CAPITAL)
			{
				_ = Keysharp.Internals.Input.MacOS.MacCapsLockState.TryToggle();
				if (MacCapsLockState.TryGet(out var capsOn))
					keyboardState.ObserveCapsLock(capsOn);
			}
		}
	}
}
#endif
