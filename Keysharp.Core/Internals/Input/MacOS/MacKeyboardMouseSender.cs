#if OSX
using System.Text;
using Keysharp.Builtins;
using Keysharp.Internals.Input.Hooks;
using Keysharp.Internals.Input.Keyboard;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.MacOS
{
	internal sealed class MacKeyboardMouseSender : Keysharp.Internals.Input.Unix.UnixKeyboardMouseSender
	{
		private readonly MacKeyboardState keyboardState;
		private readonly MacMouseEventStream mouseStream;

		internal MacKeyboardMouseSender(Script script, MacKeyboardState keyboardState, MacMouseEventStream mouseStream)
			: base(script)
		{
			this.keyboardState = keyboardState;
			this.mouseStream = mouseStream;
		}

		private uint CurrentPostedModifiers() => keyboardState.GetModifiers(() => GetModifierLRState(true));
		private void RefreshButtonsWithoutHook()
		{
			if (!script.HookThread.HasMouseHook())
				mouseStream.ResyncButtons();
		}

		protected override void OnSendKeysStarting()
		{
			keyboardState.BeginSend(GetModifierLRState(true), QueryCapsLockState());
			RefreshButtonsWithoutHook();
			mouseStream.InvalidatePosition();
		}

		protected override void OnSendKeysFinished() => keyboardState.EndSend();

		private void PostKeyboardWithPredictedState(Keysharp.Internals.Input.Hooks.Unix.UnixHookThread lht, uint vk, bool keyDown,
											long extraInfo, bool autoRepeat = false)
		{
			bool? isNeutral = null;
			var keyAsModifiersLR = lht.KeyToModifiersLR(vk, 0, ref isNeutral);
			_ = CurrentPostedModifiers();
			var capsLock = CurrentCapsLockState();
			keyboardState.PostModifier(keyAsModifiersLR, keyDown, modifiersLR =>
				MacNativeInput.PostKeyboard(vk, keyDown, extraInfo, modifiersLR, autoRepeat, capsLock));
		}

		private bool CurrentCapsLockState() => keyboardState.GetCapsLock(QueryCapsLockState);

		private static bool QueryCapsLockState()
		{
			if (MacCapsLockState.TryGet(out var on))
				return on;

			return (MacNativeInput.CGEventSourceFlagsState(
					MacNativeInput.kCGEventSourceStateHIDSystemState) & MacNativeInput.kCGEventFlagMaskAlphaShift) != 0;
		}

		private void RefreshCapsLockState() => keyboardState.SetCapsLock(QueryCapsLockState());

		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			if (vk != VK_CAPITAL || !MacCapsLockState.TryGet(out var capsOn))
				return base.ToggleKeyState(vk, toggleValue);

			var starting = capsOn ? ToggleValueType.On : ToggleValueType.Off;
			if (toggleValue is not (ToggleValueType.On or ToggleValueType.Off) || starting == toggleValue)
			{
				keyboardState.SetCapsLock(capsOn);
				return starting;
			}

			var desired = toggleValue == ToggleValueType.On;
			if (MacCapsLockState.TrySet(desired))
				keyboardState.SetCapsLock(desired);
			else
				RefreshCapsLockState();

			return starting;
		}

		protected override void DispatchKeybdEvent(Keysharp.Internals.Input.Hooks.Unix.UnixHookThread lht, KeyEventTypes eventType,
											  uint vk, long extraInfo, bool autoRepeat)
		{
			WithSendScope(lht, () =>
			{
				EnsureInputSendPermission("send keyboard input");

				if (vk == VK_CAPITAL && autoRepeat)
					return; // A lock key changes state once per physical press, never on repeat metadata.

				if (vk == VK_CAPITAL && eventType != KeyEventTypes.KeyUp && MacCapsLockState.TryToggle())
				{
					RefreshCapsLockState();
					// IOKit owns the actual lock state; emit the matching CGEvent only as notification
					// for applications and SendLevel-aware Keysharp hooks.
					PostKeyboardWithPredictedState(lht, vk, true, extraInfo);
					if (eventType == KeyEventTypes.KeyDownAndUp)
						PostKeyboardWithPredictedState(lht, vk, false, extraInfo);
					return;
				}

				switch (eventType)
				{
					case KeyEventTypes.KeyDown:
						PostKeyboardWithPredictedState(lht, vk, true, extraInfo, autoRepeat);
						break;
					case KeyEventTypes.KeyUp:
						PostKeyboardWithPredictedState(lht, vk, false, extraInfo);
						break;
					case KeyEventTypes.KeyDownAndUp:
						PostKeyboardWithPredictedState(lht, vk, true, extraInfo, autoRepeat);
						PostKeyboardWithPredictedState(lht, vk, false, extraInfo);
						break;
				}
			});
		}

		protected override bool TrySendPlatformUnicodeText(Keysharp.Internals.Input.Hooks.Unix.UnixHookThread lht, string text, long extraInfo)
		{
			WithSendScope(lht, () =>
			{
				EnsureInputSendPermission("send text input");
				MacNativeInput.PostUnicodeText(text, extraInfo);
			});

			return true;
		}

		protected override bool TrySendPlatformUnicodeChar(Keysharp.Internals.Input.Hooks.Unix.UnixHookThread lht, char ch, long extraInfo, bool hasMappedKeystroke, uint vk, bool needShift, bool needAltGr)
			=> TrySendPlatformUnicodeText(lht, ch.ToString(), extraInfo);

		protected internal override bool TrySendPlatformRawText(ReadOnlySpan<char> text, ref int keyIndex, uint modifiersLR)
		{
			if (sendMode != SendModes.Event || keyIndex < 0 || keyIndex >= text.Length)
				return false;

			var remaining = text[keyIndex..].ToString();
			var lht = script.HookThread as Keysharp.Internals.Input.Hooks.Unix.UnixHookThread;
			if (lht == null)
				return false;

			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			WithSendScope(lht, () =>
			{
				EnsureInputSendPermission("send raw keyboard text");
				if (!KeyDelayWouldSleepOrQueue())
				{
					EmitMacTextWithControls(remaining, extraInfo);
					return;
				}

				// Preserve SendEvent's configured per-character delay when one is active. Each
				// scalar is still one text payload rather than a pair of marshalled UTF-16 units.
				for (var i = 0; i < remaining.Length;)
				{
					var length = i + 1 < remaining.Length
						&& ((remaining[i] == '\r' && remaining[i + 1] == '\n')
							|| (char.IsHighSurrogate(remaining[i]) && char.IsLowSurrogate(remaining[i + 1])))
						? 2 : 1;
					EmitMacTextWithControls(remaining.Substring(i, length), extraInfo);
					DoKeyDelay();
					i += length;
				}
			});

			keyIndex = text.Length - 1;
			return true;
		}

		protected override void DispatchEventArray(Keysharp.Internals.Input.Hooks.Unix.UnixHookThread lht, InputArrayState state, long extraInfo)
			=> WithSendScope(lht, () => ReplayMacEventArray(state, extraInfo));

		private void ReplayMacEventArray(InputArrayState state, long extraInfo)
		{
			var events = state.Events;
			var modifiersLR = state.InitialModifiers;
			var capsLockOn = CurrentCapsLockState();
			var textBatch = new StringBuilder();

			void FlushText()
			{
				if (textBatch.Length == 0)
					return;

				EmitMacTextWithControls(textBatch.ToString(), extraInfo);
				textBatch.Clear();
			}

			foreach (var ev in events)
			{
				if (ev.Type == ArrayEventType.Text)
				{
					textBatch.Append(ev.Text);
					continue;
				}

				FlushText();

				switch (ev.Type)
				{
					case ArrayEventType.DelayMs:
						if (ev.DelayMs > 0)
							Keysharp.Internals.Flow.SleepWithoutInterruption(ev.DelayMs);
						break;
					case ArrayEventType.KeyDown:
						var modifiersAfterDown = modifiersLR | ev.ModifiersLR;
						if (ev.Vk == VK_CAPITAL)
						{
							if (ev.AutoRepeat)
								break;
							if (MacCapsLockState.TryToggle())
							{
								RefreshCapsLockState();
								capsLockOn = CurrentCapsLockState();
							}
						}
						if (MacNativeInput.PostKeyboard(ev.Vk, true, extraInfo, modifiersAfterDown, ev.AutoRepeat, capsLockOn))
							keyboardState.SetModifiers(modifiersLR = modifiersAfterDown);
						break;
					case ArrayEventType.KeyUp:
						var modifiersAfterUp = modifiersLR & ~ev.ModifiersLR;
						if (MacNativeInput.PostKeyboard(ev.Vk, false, extraInfo, modifiersAfterUp, capsLockOn: capsLockOn))
							keyboardState.SetModifiers(modifiersLR = modifiersAfterUp);
						break;
					case ArrayEventType.MouseMoveRel:
						_ = mouseStream.MoveRelative(ev.X, ev.Y, extraInfo);
						break;
					case ArrayEventType.MouseMoveAbs:
					{
						int mx = ev.X, my = ev.Y;
						EnsureCoords(ref mx, ref my);
						mouseStream.MoveAbsolute(mx, my, extraInfo);
						break;
					}
					case ArrayEventType.MousePress:
					case ArrayEventType.MouseRelease:
					{
						mouseStream.Button(ev.Button, ev.Type == ArrayEventType.MousePress, ev.X, ev.Y, extraInfo);
						break;
					}
					case ArrayEventType.MouseWheelV:
						MacNativeInput.PostMouseWheel(ev.WheelDelta, MouseWheelScrollDirection.Vertical, extraInfo);
						break;
					case ArrayEventType.MouseWheelH:
						MacNativeInput.PostMouseWheel(ev.WheelDelta, MouseWheelScrollDirection.Horizontal, extraInfo);
						break;
				}
			}

			FlushText();
			keyboardState.SetModifiers(modifiersLR);
		}

		internal override void SetModifierLRState(uint modifiersLRnew, uint modifiersLRnow, nint targetWindow,
			bool disguiseDownWinAlt, bool disguiseUpWinAlt, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			// Seed the native event stream from the sender's synchronous prediction. Each modifier event posted
			// by base updates this field before the next event is created, independently of WindowServer timing.
			keyboardState.SetModifiers(modifiersLRnow);
			base.SetModifierLRState(modifiersLRnew, modifiersLRnow, targetWindow,
				disguiseDownWinAlt, disguiseUpWinAlt, extraInfo);
		}

		internal override void PerformMouseCommon(Actions actionType, uint vk, int x1, int y1, int x2, int y2,
			long repeatCount, KeyEventTypes eventType, long speed, bool relative)
		{
			RefreshButtonsWithoutHook();
			base.PerformMouseCommon(actionType, vk, x1, y1, x2, y2, repeatCount, eventType, speed, relative);
		}

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			EnsureInputSendPermission("send mouse input");
			if (sendMode != SendModes.Event)
			{
				PutMouseEventIntoArray(eventFlags, data, x, y);
				return;
			}

			var lht = script.HookThread as Keysharp.Internals.Input.Hooks.Unix.UnixHookThread;
			if (lht == null)
				return;

			WithSendScope(lht, () => ReplayMacMouseEvent(eventFlags, data, x, y,
				KeyIgnoreLevel(ThreadAccessors.A_SendLevel)));
		}

		internal override void MouseMove(ref int x, ref int y, ref uint eventFlags, long speed, bool moveOffset)
		{
			EnsureInputSendPermission("move mouse");
			if (x == CoordUnspecified || y == CoordUnspecified)
				return;

			if (sendMode == SendModes.Play)
			{
				PutMouseEventIntoArray((uint)MOUSEEVENTF.MOVE | (moveOffset ? MsgOffsetMouseMove : 0), 0, x, y);
				DoMouseDelay();

				if (moveOffset)
				{
					x = CoordUnspecified;
					y = CoordUnspecified;
				}

				return;
			}

			if (moveOffset)
			{
				if (sendMode == SendModes.Input)
				{
					if (sendInputCursorPos.X == CoordUnspecified)
					{
						if (GetCursorPos(out sendInputCursorPos))
						{
							x += sendInputCursorPos.X;
							y += sendInputCursorPos.Y;
						}
					}
					else
					{
						x += sendInputCursorPos.X;
						y += sendInputCursorPos.Y;
					}
				}
				else if (GetCursorPos(out POINT pos))
				{
					x += pos.X;
					y += pos.Y;
				}
			}
			else
			{
				CoordToScreen(ref x, ref y, CoordMode.Mouse);
			}

			if (sendMode == SendModes.Input)
			{
				sendInputCursorPos.X = x;
				sendInputCursorPos.Y = y;
				AddArrayEvent(ArrayEvent.MouseMoveAbs(x, y));
				DoMouseDelay();
				return;
			}

			if (speed < 0)
				speed = 0;
			else if (speed > MaxMouseSpeed)
				speed = MaxMouseSpeed;

			if (speed == 0 || !GetCursorPos(out POINT cursorPos))
			{
				mouseStream.MoveAbsolute(x, y, KeyIgnoreLevel(ThreadAccessors.A_SendLevel));
				DoMouseDelay();
				return;
			}

			long cx = cursorPos.X;
			long cy = cursorPos.Y;
			const int incrMouseMinSpeed = 32;
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);

			void Step(ref long cur, long dest)
			{
				if (cur == dest)
					return;

				var delta = (dest > cur ? dest - cur : cur - dest) / speed;
				if (delta == 0 || delta < incrMouseMinSpeed)
					delta = incrMouseMinSpeed;

				if (dest > cur)
					cur = Math.Min(dest, cur + delta);
				else
					cur = Math.Max(dest, cur - delta);
			}

			while (cx != x || cy != y)
			{
				Step(ref cx, x);
				Step(ref cy, y);
				mouseStream.MoveAbsolute((int)cx, (int)cy, extraInfo);
				DoMouseDelay();
			}
		}

		private void ReplayMacMouseEvent(uint eventFlags, uint data, int x, int y, long extraInfo)
		{
			if ((eventFlags & 0xFFFF0000) != 0)
			{
				var button = KeyCodes.VkToMouseButton(eventFlags & 0xFFFF);
				var type = (KeyEventTypes)(eventFlags >> 16);
				EmitMacButton(button, type, x, y, extraInfo);
				DoMouseDelay();
				return;
			}

			var actionFlags = eventFlags & (0x1FFFu & ~(uint)MOUSEEVENTF.MOVE);
			var hasMove = (eventFlags & (uint)MOUSEEVENTF.MOVE) != 0;
			var relativeMove = (eventFlags & MsgOffsetMouseMove) != 0;

			if (hasMove)
			{
				if (relativeMove)
					_ = mouseStream.MoveRelative(x, y, extraInfo);
				else
				{
					EnsureCoords(ref x, ref y);
					mouseStream.MoveAbsolute(x, y, extraInfo);
				}
			}

			if (actionFlags is (uint)MOUSEEVENTF.WHEEL or (uint)MOUSEEVENTF.HWHEEL)
			{
				var direction = actionFlags == (uint)MOUSEEVENTF.WHEEL
					? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Horizontal;
				MacNativeInput.PostMouseWheel(unchecked((short)data), direction, extraInfo);
			}
			else
			{
				var xButton = data == MouseUtils.XBUTTON2 ? MouseButton.Button5 : MouseButton.Button4;
				var action = actionFlags switch
				{
					(uint)MOUSEEVENTF.LEFTDOWN => (MouseButton.Button1, KeyEventTypes.KeyDown),
					(uint)MOUSEEVENTF.LEFTUP => (MouseButton.Button1, KeyEventTypes.KeyUp),
					(uint)MOUSEEVENTF.RIGHTDOWN => (MouseButton.Button2, KeyEventTypes.KeyDown),
					(uint)MOUSEEVENTF.RIGHTUP => (MouseButton.Button2, KeyEventTypes.KeyUp),
					(uint)MOUSEEVENTF.MIDDLEDOWN => (MouseButton.Button3, KeyEventTypes.KeyDown),
					(uint)MOUSEEVENTF.MIDDLEUP => (MouseButton.Button3, KeyEventTypes.KeyUp),
					(uint)MOUSEEVENTF.XDOWN => (xButton, KeyEventTypes.KeyDown),
					(uint)MOUSEEVENTF.XUP => (xButton, KeyEventTypes.KeyUp),
					_ => (MouseButton.NoButton, KeyEventTypes.KeyDown)
				};
				EmitMacButton(action.Item1, action.Item2, x, y, extraInfo);
			}

			DoMouseDelay();
		}

		private void EmitMacButton(MouseButton button, KeyEventTypes type, int x, int y, long extraInfo)
		{
			if (button == MouseButton.NoButton)
				return;

			if (type != KeyEventTypes.KeyUp)
				mouseStream.Button(button, true, x, y, extraInfo);
			if (type != KeyEventTypes.KeyDown)
				mouseStream.Button(button, false, x, y, extraInfo);
		}

		private void EmitMacTextWithControls(string text, long extraInfo)
		{
			if (string.IsNullOrEmpty(text))
				return;

			var chunkStart = 0;
			bool lastWasCR = false;
			for (var i = 0; i < text.Length; i++)
			{
				var ch = text[i];
				if (ch == '\n' && lastWasCR)
				{
					chunkStart = i + 1;
					lastWasCR = false;
					continue;
				}

				var control = ch switch { '\r' or '\n' => VK_RETURN, '\t' => VK_TAB, '\b' => VK_BACK, _ => 0u };
				if (control != 0)
				{
					if (i > chunkStart)
						MacNativeInput.PostUnicodeText(text[chunkStart..i], extraInfo);
					var capsLock = CurrentCapsLockState();
					MacNativeInput.PostKeyboard(control, true, extraInfo, 0, capsLockOn: capsLock);
					MacNativeInput.PostKeyboard(control, false, extraInfo, 0, capsLockOn: capsLock);
					chunkStart = i + 1;
				}
				lastWasCR = ch == '\r';
			}

			if (chunkStart < text.Length)
				MacNativeInput.PostUnicodeText(text[chunkStart..], extraInfo);
		}
	}
}
#endif
