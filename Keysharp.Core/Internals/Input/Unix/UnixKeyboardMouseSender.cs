#if !WINDOWS

using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Keysharp.Builtins;
#if LINUX
using Keysharp.Internals.Input.Linux;
#endif
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using Keysharp.Internals.Input.Mouse;
using Keysharp.Internals.Input.Hooks;

namespace Keysharp.Internals.Input.Unix
{
	/// <summary>
	/// Shared non-Windows keyboard/mouse sender implementation.
	/// </summary>
	internal partial class UnixKeyboardMouseSender : KeyboardMouseSender
	{
		internal PlatformEventSimulator sim => backend.sim;
		protected PlatformEventSimulator Sim => backend.sim;
		protected readonly PlatformKeySimulationBackend backend;

		// Prefer Right-Alt as AltGr on Linux (prevents menu activation better than neutral Alt).
		private const uint VK_ALTGR = VK_RMENU;

		protected sealed class InputArrayState
		{
			internal readonly List<ArrayEvent> Events;
			internal int Count;
			internal readonly uint InitialModifiers;
			internal readonly uint PrevEventModifiers;
			internal readonly SendModes Mode;
			internal readonly POINT PrevCursorPosition;

			internal InputArrayState(List<ArrayEvent> events, uint initialModifiers, uint prevEventModifiers,
				SendModes mode, POINT prevCursorPosition)
			{
				Events = events;
				InitialModifiers = initialModifiers;
				PrevEventModifiers = prevEventModifiers;
				Mode = mode;
				PrevCursorPosition = prevCursorPosition;
			}
		}

		private readonly Lock inputGate = new();
		private readonly Stack<InputArrayState> inputStack = new();

		// Holds a UTF-16 high surrogate between the two SendUnicodeChar calls of an astral
		// scalar (e.g. emoji) while sending in Event mode; see SendUnicodeChar.
		private char pendingHighSurrogate;

		internal UnixKeyboardMouseSender(Script script) : base(script)
		{
			backend = new PlatformKeySimulationBackend(script);
		}

		protected void EnsureInputSendPermission(string operation)
			=> _ = script.Permissions.EnsureInputControl(operation: operation);

		internal override bool MouseButtonsSwapped
		{
			get
			{
#if LINUX
				if (!Platform.Desktop.IsX11Available)
					return false;

				var mapping = DesktopKeyboardState.X11.Get()?.PointerMapping;
				return mapping is { Length: >= 3 } && mapping[0] != 1;
#else
				return false;
#endif
			}
		}

		#region Input array recording

		protected enum ArrayEventType : byte
		{
			KeyDown,
			KeyUp,
			DelayMs,
			Text,

			MouseMoveAbs,
			MouseMoveRel,
			MousePress,
			MouseRelease,
			MouseWheelV,
			MouseWheelH
		}

		protected readonly struct ArrayEvent
		{
			internal readonly ArrayEventType Type;

			// Key
			internal readonly uint Vk;
			internal readonly uint ModifiersLR;
			internal readonly bool AutoRepeat;

			// Delay
			internal readonly int DelayMs;

			// Text
			internal readonly string Text;

			// Mouse (x/y may be CoordUnspecified)
			internal readonly int X;
			internal readonly int Y;
			internal readonly MouseButton Button;
			internal readonly short WheelDelta;

			private ArrayEvent(ArrayEventType type, uint vk, uint modifiersLR, int delayMs, string text,
							int x, int y, MouseButton button, short wheelDelta, bool autoRepeat = false)
			{
				Type = type;
				Vk = vk;
				ModifiersLR = modifiersLR;
				AutoRepeat = autoRepeat;
				DelayMs = delayMs;
				Text = text;
				X = x;
				Y = y;
				Button = button;
				WheelDelta = wheelDelta;
			}

			internal static ArrayEvent Key(ArrayEventType type, uint vk, uint modifiersLR, bool autoRepeat)
				=> new(type, vk, modifiersLR, 0, null, 0, 0, MouseButton.NoButton, 0, autoRepeat);

			internal static ArrayEvent Delay(int ms)
				=> new(ArrayEventType.DelayMs, 0, 0, ms, null, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent TextEvent(string text)
				=> new(ArrayEventType.Text, 0, 0, 0, text, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveAbs(int x, int y)
				=> new(ArrayEventType.MouseMoveAbs, 0, 0, 0, null, x, y, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveRel(int dx, int dy)
				=> new(ArrayEventType.MouseMoveRel, 0, 0, 0, null, dx, dy, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseButtonEvent(ArrayEventType type, MouseButton button, int x, int y)
				=> new(type, 0, 0, 0, null, x, y, button, 0);

			internal static ArrayEvent MouseWheelV(short delta)
				=> new(ArrayEventType.MouseWheelV, 0, 0, 0, null, 0, 0, MouseButton.NoButton, delta);

			internal static ArrayEvent MouseWheelH(short delta)
				=> new(ArrayEventType.MouseWheelH, 0, 0, 0, null, 0, 0, MouseButton.NoButton, delta);
		}

		protected void AddArrayEvent(in ArrayEvent ev)
		{
			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return;

				var st = inputStack.Peek();
				st.Events.Add(ev);
				st.Count++;
			}
		}

		#endregion

		internal override void InitEventArray(int maxEvents, uint modifiersLR)
		{
			// Allocate before changing any sender state so an allocation failure cannot leave a
			// half-open frame or corrupt the prediction belonging to an outer send.
			var cap = maxEvents > 0 ? Math.Min(maxEvents, 2048) : 512;
			var events = new List<ArrayEvent>(cap);

			lock (inputGate)
			{
				// Reserve and construct the complete frame before changing the modifier prediction.
				// After EnsureCapacity succeeds, Push is a non-allocating assignment.
				inputStack.EnsureCapacity(inputStack.Count + 1);
				var prev = eventModifiersLR;
				var state = new InputArrayState(events, modifiersLR, prev, sendMode, sendInputCursorPos);
				eventModifiersLR = modifiersLR;
				inputStack.Push(state);
			}
			sendInputCursorPos.X = CoordUnspecified;
			sendInputCursorPos.Y = CoordUnspecified;
		}

		internal override void CleanupEventArray(long finalKeyDelay)
		{
			sendMode = PopEventArray();
			DoKeyDelay(finalKeyDelay);
		}

		internal override void AbortEventArray()
		{
			sendMode = PopEventArray();
		}

		private SendModes PopEventArray()
		{
			lock (inputGate)
			{
				if (inputStack.Count != 0)
				{
					var st = inputStack.Pop();
					eventModifiersLR = st.PrevEventModifiers;
					sendInputCursorPos = st.PrevCursorPosition;
				}

				return inputStack.Count == 0 ? SendModes.Event : inputStack.Peek().Mode;
			}
		}

		internal override int SiEventCount()
		{
			lock (inputGate)
				return inputStack.Count > 0 ? inputStack.Peek().Count : 0;
		}

		internal override void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y)
		{
			// Linux MouseClick encodes type in the high word; handle that before the legacy MOUSEEVENTF path.
			if ((eventFlags & 0xFFFF0000) != 0)
			{
				var type = (KeyEventTypes)(eventFlags >> 16);

				if (type == KeyEventTypes.KeyDown || type == KeyEventTypes.KeyUp || type == KeyEventTypes.KeyDownAndUp)
				{
					var button = KeyCodes.VkToMouseButton(eventFlags & 0xFFFF);

					if (button != MouseButton.NoButton)
					{
						if (type != KeyEventTypes.KeyUp)
							AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MousePress, button, x, y));

						if (type != KeyEventTypes.KeyDown)
							AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MouseRelease, button, x, y));

						return;
					}
				}
			}

			var actionFlags = eventFlags & (0x1FFFu & ~(uint)MOUSEEVENTF.MOVE);
			var relativeMove = (eventFlags & MsgOffsetMouseMove) != 0;

			if (actionFlags == 0)
			{
				// movement-only
				if (relativeMove)
					AddArrayEvent(ArrayEvent.MouseMoveRel(x, y));
				else
					AddArrayEvent(ArrayEvent.MouseMoveAbs(x, y)); // x/y may be CoordUnspecified
				return;
			}

			switch (actionFlags)
			{
				case (uint)MOUSEEVENTF.LEFTDOWN:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MousePress, MouseButton.Button1, x, y));
					return;
				case (uint)MOUSEEVENTF.LEFTUP:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MouseRelease, MouseButton.Button1, x, y));
					return;
				case (uint)MOUSEEVENTF.RIGHTDOWN:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MousePress, MouseButton.Button2, x, y));
					return;
				case (uint)MOUSEEVENTF.RIGHTUP:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MouseRelease, MouseButton.Button2, x, y));
					return;
				case (uint)MOUSEEVENTF.MIDDLEDOWN:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MousePress, MouseButton.Button3, x, y));
					return;
				case (uint)MOUSEEVENTF.MIDDLEUP:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(ArrayEventType.MouseRelease, MouseButton.Button3, x, y));
					return;
				case (uint)MOUSEEVENTF.XDOWN:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(
						ArrayEventType.MousePress,
						data == MouseUtils.XBUTTON2 ? MouseButton.Button5 : MouseButton.Button4, x, y));
					return;
				case (uint)MOUSEEVENTF.XUP:
					AddArrayEvent(ArrayEvent.MouseButtonEvent(
						ArrayEventType.MouseRelease,
						data == MouseUtils.XBUTTON2 ? MouseButton.Button5 : MouseButton.Button4, x, y));
					return;
				case (uint)MOUSEEVENTF.WHEEL:
					AddArrayEvent(ArrayEvent.MouseWheelV(unchecked((short)data)));
					return;
				case (uint)MOUSEEVENTF.HWHEEL:
					AddArrayEvent(ArrayEvent.MouseWheelH(unchecked((short)data)));
					return;
			}
		}
		internal override void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo, bool autoRepeat = false)
		{
			bool isKeyUp = (eventFlags & (uint)KEYEVENTF_KEYUP) != 0;
			bool isUnicode = (eventFlags & (uint)KEYEVENTF_UNICODE) != 0;

			// Delay event (used by some Send implementations): vk==0 and sc==0.
			if (vk == 0 && sc == 0)
			{
				AddArrayEvent(ArrayEvent.Delay((int)extraInfo));
				return;
			}

			// Keep predicted modifier state in sync.
			// Keep track of the predicted modifier state for use in other places:
			if (isKeyUp)
				eventModifiersLR &= ~keyAsModifiersLR;
			else
				eventModifiersLR |= keyAsModifiersLR;

			// Unicode packet: record text on "down"; ignore paired "up".
			if (isUnicode)
			{
				if (!isKeyUp)
				{
					char ch = unchecked((char)sc);
					AddArrayEvent(ArrayEvent.TextEvent(ch.ToString()));
				}
				return;
			}

			// Normal key: record as down/up.
			AddArrayEvent(ArrayEvent.Key(isKeyUp ? ArrayEventType.KeyUp : ArrayEventType.KeyDown, vk, keyAsModifiersLR,
				!isKeyUp && autoRepeat));
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			InputArrayState st;

			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return;

				// Dispatch does not own the frame. Cleanup/abort pops it exactly once, which keeps
				// nested sends and exceptional dispatches from consuming an outer send's state.
				st = inputStack.Peek();
			}

			if (st.Events.Count == 0)
				return;

			var lht = script.HookThread as UnixHookThread;
			if (lht == null)
				return;

			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			DispatchEventArray(lht, st, extraInfo);
		}

		protected void ReplayEventArrayEvents(List<ArrayEvent> events, long extraInfo)
		{
			var ms = DateTime.UtcNow;
			var textBatch = new StringBuilder();

			void FlushText()
			{
				if (textBatch.Length == 0)
					return;

				EmitTextInjectedWithControls(textBatch.ToString(), extraInfo);
				textBatch.Clear();
				ms = DateTime.Now;
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
						ms = DateTime.Now;
						break;

					case ArrayEventType.KeyDown:
						backend.KeyDown(ev.Vk, ms, extraInfo);
						break;

					case ArrayEventType.KeyUp:
						backend.KeyUp(ev.Vk, ms, extraInfo);
						break;

					case ArrayEventType.MouseMoveRel:
						sim.SimulateMouseMovementRelative(ClampShort(ev.X), ClampShort(ev.Y));
						break;

					case ArrayEventType.MouseMoveAbs:
					{
						int mx = ev.X, my = ev.Y;
						EnsureCoords(ref mx, ref my);
						sim.SimulateMouseMovement(ClampShort(mx), ClampShort(my));
						break;
					}

					case ArrayEventType.MousePress:
					{
						int mx = ev.X, my = ev.Y;
						if (mx == CoordUnspecified && my == CoordUnspecified)
						{
							sim.SimulateMousePress(ev.Button);
						}
						else
						{
							EnsureCoords(ref mx, ref my);
							sim.SimulateMousePress(ClampShort(mx), ClampShort(my), ev.Button);
						}
						break;
					}

					case ArrayEventType.MouseRelease:
					{
						int mx = ev.X, my = ev.Y;
						if (mx == CoordUnspecified && my == CoordUnspecified)
						{
							sim.SimulateMouseRelease(ev.Button);
						}
						else
						{
							EnsureCoords(ref mx, ref my);
							sim.SimulateMouseRelease(ClampShort(mx), ClampShort(my), ev.Button);
						}
						break;
					}

					case ArrayEventType.MouseWheelV:
						sim.SimulateMouseWheel(ev.WheelDelta, MouseWheelScrollDirection.Vertical, MouseWheelScrollType.UnitScroll);
						break;

					case ArrayEventType.MouseWheelH:
						sim.SimulateMouseWheel(ev.WheelDelta, MouseWheelScrollDirection.Horizontal, MouseWheelScrollType.UnitScroll);
						break;
				}
			}

			FlushText();
		}

		protected void EmitTextInjectedWithControls(string text, long extraInfo)
		{
			if (string.IsNullOrEmpty(text))
				return;

			var chunk = new StringBuilder();
			bool lastWasCR = false;
			var ms = DateTime.Now;

			void FlushChunk()
			{
				if (chunk.Length == 0)
					return;

				sim.SimulateTextEntry(chunk.ToString());
				chunk.Clear();
				ms = DateTime.Now;
			}

			foreach (var ch in text)
			{
				switch (ch)
				{
					case '\r':
						FlushChunk();
						backend.KeyStroke(VK_RETURN, ms, extraInfo);
						lastWasCR = true;
						break;

					case '\n':
						if (lastWasCR)
						{
							lastWasCR = false;
							break;
						}

						FlushChunk();
						backend.KeyStroke(VK_RETURN, ms, extraInfo);
						break;

					case '\t':
						FlushChunk();
						backend.KeyStroke(VK_TAB, ms, extraInfo);
						lastWasCR = false;
						break;

					case '\b':
						FlushChunk();
						backend.KeyStroke(VK_BACK, ms, extraInfo);
						lastWasCR = false;
						break;

					default:
						chunk.Append(ch);
						lastWasCR = false;
						break;
				}
			}

			FlushChunk();
		}

		protected static void EnsureCoords(ref int x, ref int y)
		{
			if (x != CoordUnspecified && y != CoordUnspecified)
				return;

			if (!GetCursorPos(out POINT pos))
				pos = new POINT(0, 0);

			if (x == CoordUnspecified)
				x = pos.X;

			if (y == CoordUnspecified)
				y = pos.Y;
		}

		protected static HashSet<uint> CollectVksWeWillPress(List<ArrayEvent> events)
		{
			var set = new HashSet<uint>();

			foreach (var ev in events)
			{
				switch (ev.Type)
				{
					case ArrayEventType.KeyDown:
						// only matters if we will press it down; KeyUp-only doesn't need a pre-release.
						if (!KeyboardUtils.IsModifierVk(ev.Vk) && !MouseUtils.IsMouseVK(ev.Vk) && !MouseUtils.IsWheelVK(ev.Vk))
							set.Add(ev.Vk);
						break;

					case ArrayEventType.Text:
						if (ev.Text is null) break;

						// If your Text replay emits VKs for controls, include them here.
						// (If you inject '\n' as text and it works for you, you can remove VK_RETURN.)
						if (ev.Text.IndexOf('\t') >= 0) set.Add(VK_TAB);
						if (ev.Text.IndexOf('\b') >= 0) set.Add(VK_BACK);
						if (ev.Text.IndexOf('\r') >= 0 || ev.Text.IndexOf('\n') >= 0) set.Add(VK_RETURN);
						break;
				}
			}

			return set;
		}

		protected static HashSet<uint> CollectModifierUpsForArray(List<ArrayEvent> events, uint logicalMods, UnixHookThread lht)
		{
			var set = new HashSet<uint>();

			foreach (var ev in events)
			{
				if (ev.Type != ArrayEventType.KeyDown)
					continue;

				if (!KeyboardUtils.IsModifierVk(ev.Vk))
					continue;

				bool? neutral = null;
				var modMask = lht.KeyToModifiersLR(ev.Vk, 0, ref neutral);
				if (modMask == 0)
					continue;

				if ((logicalMods & modMask) != 0)
					set.Add(ev.Vk);
			}

			return set;
		}

		#region Mouse/Key immediate ops (Event mode) — keep behavior, but route Input mode through Put*IntoArray

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			EnsureInputSendPermission("send mouse input");
			if (sendMode != SendModes.Event)
			{
				PutMouseEventIntoArray(eventFlags, data, x, y);
				return;
			}

			var lht = script.HookThread as UnixHookThread;

			if (lht != null)
			{
				WithSendScope(lht, () => ReplayImmediateMouseEvent(eventFlags, data, x, y));
				return;
			}

			ReplayImmediateMouseEvent(eventFlags, data, x, y);
		}

		protected void ReplayImmediateMouseEvent(uint eventFlags, uint data, int x, int y)
		{
			// Legacy Linux usage: high word encodes KeyEventTypes, low word encodes vk.
			if ((eventFlags & 0xFFFF0000) != 0)
			{
				var legacyButton = KeyCodes.VkToMouseButton(eventFlags & 0xFFFF);
				var legacyType = (KeyEventTypes)(eventFlags >> 16);
				EmitButton(legacyButton, legacyType, x, y);
				return;
			}

			var actionFlags = eventFlags & (0x1FFFu & ~(uint)MOUSEEVENTF.MOVE);
			var hasMove = (eventFlags & (uint)MOUSEEVENTF.MOVE) != 0;
			var relativeMove = (eventFlags & MsgOffsetMouseMove) != 0;

			if (hasMove)
				EmitMove(relativeMove, x, y);

			switch (actionFlags)
			{
				case 0:
					break; // movement-only (handled above)

				case (uint)MOUSEEVENTF.LEFTDOWN:
					EmitButton(MouseButton.Button1, KeyEventTypes.KeyDown, x, y);
					break;
				case (uint)MOUSEEVENTF.LEFTUP:
					EmitButton(MouseButton.Button1, KeyEventTypes.KeyUp, x, y);
					break;
				case (uint)MOUSEEVENTF.RIGHTDOWN:
					EmitButton(MouseButton.Button2, KeyEventTypes.KeyDown, x, y);
					break;
				case (uint)MOUSEEVENTF.RIGHTUP:
					EmitButton(MouseButton.Button2, KeyEventTypes.KeyUp, x, y);
					break;
				case (uint)MOUSEEVENTF.MIDDLEDOWN:
					EmitButton(MouseButton.Button3, KeyEventTypes.KeyDown, x, y);
					break;
				case (uint)MOUSEEVENTF.MIDDLEUP:
					EmitButton(MouseButton.Button3, KeyEventTypes.KeyUp, x, y);
					break;
				case (uint)MOUSEEVENTF.XDOWN:
					EmitButton(data == MouseUtils.XBUTTON2 ? MouseButton.Button5 : MouseButton.Button4, KeyEventTypes.KeyDown, x, y);
					break;
				case (uint)MOUSEEVENTF.XUP:
					EmitButton(data == MouseUtils.XBUTTON2 ? MouseButton.Button5 : MouseButton.Button4, KeyEventTypes.KeyUp, x, y);
					break;
				case (uint)MOUSEEVENTF.WHEEL:
					sim.SimulateMouseWheel(unchecked((short)data), MouseWheelScrollDirection.Vertical, MouseWheelScrollType.UnitScroll);
					break;
				case (uint)MOUSEEVENTF.HWHEEL:
					sim.SimulateMouseWheel(unchecked((short)data), MouseWheelScrollDirection.Horizontal, MouseWheelScrollType.UnitScroll);
					break;
			}

			DoMouseDelay();

			void EmitMove(bool rel, int mx, int my)
			{
				if (rel)
				{
					sim.SimulateMouseMovementRelative(ClampShort(mx), ClampShort(my));
				}
				else
				{
					EnsureCoords(ref mx, ref my);
					sim.SimulateMouseMovement(ClampShort(mx), ClampShort(my));
				}
			}

			void EmitButton(MouseButton button, KeyEventTypes type, int mx, int my)
			{
				if (button == MouseButton.NoButton)
					return;

				bool noCoords = mx == CoordUnspecified && my == CoordUnspecified;

				if (!noCoords)
					EnsureCoords(ref mx, ref my);

				switch (type)
				{
					case KeyEventTypes.KeyDown:
						if (noCoords)
							sim.SimulateMousePress(button);
						else
							sim.SimulateMousePress(ClampShort(mx), ClampShort(my), button);
						break;
					case KeyEventTypes.KeyUp:
						if (noCoords)
							sim.SimulateMouseRelease(button);
						else
							sim.SimulateMouseRelease(ClampShort(mx), ClampShort(my), button);
						break;
					case KeyEventTypes.KeyDownAndUp:
						if (noCoords)
						{
							sim.SimulateMousePress(button);
							sim.SimulateMouseRelease(button);
						}
						else
						{
							sim.SimulateMousePress(ClampShort(mx), ClampShort(my), button);
							sim.SimulateMouseRelease(ClampShort(mx), ClampShort(my), button);
						}
						break;
				}
			}

			void EnsureCoords(ref int cx, ref int cy)
			{
				if (cx != CoordUnspecified && cy != CoordUnspecified)
					return;

				if (!GetCursorPos(out POINT pos))
					pos = new POINT(0, 0);

				if (cx == CoordUnspecified) cx = pos.X;
				if (cy == CoordUnspecified) cy = pos.Y;
			}
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

			if (speed == 0)
			{
				sim.SimulateMouseMovement(ClampShort(x), ClampShort(y));
				DoMouseDelay();
				return;
			}

			if (!GetCursorPos(out POINT cursorPos))
			{
				sim.SimulateMouseMovement(ClampShort(x), ClampShort(y));
				DoMouseDelay();
				return;
			}

			long cx = cursorPos.X;
			long cy = cursorPos.Y;
			const int incrMouseMinSpeed = 32;

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
				sim.SimulateMouseMovement(ClampShort(cx), ClampShort(cy));
				DoMouseDelay();
			}
		}

		internal override void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint eventFlags, long extraInfo, bool autoRepeat = false)
		{
			EnsureInputSendPermission("send keyboard input");
			var lht = script.HookThread as UnixHookThread;
			if (lht == null)
				return;

			DispatchKeybdEvent(lht, eventType, vk, extraInfo, autoRepeat);
		}

		internal override void SendUnicodeChar(char ch, uint modifiers)
		{
			EnsureInputSendPermission("send keyboard text");

			// UTF-16 surrogate pairs (astral scalars such as emoji) arrive as two separate calls.
			// In Event mode each unit is injected immediately, but the macOS
			// (CGEventKeyboardSetUnicodeString) and X11 (Unicode-keysym remap) paths cannot
			// reassemble a lone surrogate the way Windows' KEYEVENTF_UNICODE can. So buffer the
			// high surrogate and emit the combined scalar once its low surrogate arrives. (Input
			// mode is unaffected: it records each unit as a TextEvent and they are re-joined into
			// a single string before injection.)
			if (sendMode == SendModes.Event)
			{
				if (char.IsHighSurrogate(ch))
				{
					FlushPendingHighSurrogate(); // emit a prior unpaired high surrogate, if any
					pendingHighSurrogate = ch;
					return;
				}

				if (pendingHighSurrogate != '\0')
				{
					var high = pendingHighSurrogate;
					pendingHighSurrogate = '\0';

					if (char.IsLowSurrogate(ch))
					{
						Span<char> pair = [high, ch];
						SendUnicodeScalarImmediate(new string(pair));
						return;
					}

					// Unpaired high surrogate not followed by a low one: emit it best-effort,
					// then fall through to handle the current char normally.
					SendUnicodeScalarImmediate(high.ToString());
				}
			}

			SendUnicodeCharCore(ch, modifiers);
		}

		// Emits a complete Unicode scalar (1 or 2 UTF-16 units) immediately as text, used for
		// astral characters combined from a surrogate pair. Such characters never map to a
		// single keystroke, so the mapped-keystroke path is intentionally skipped.
		private void SendUnicodeScalarImmediate(string text)
		{
			if (string.IsNullOrEmpty(text))
				return;

			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			var lht = script.HookThread as UnixHookThread;

			if (lht == null)
				return;

			if (TrySendPlatformUnicodeText(lht, text, extraInfo))
				return;

			WithSendScope(lht, () => sim.SimulateTextEntry(text));
		}

		private void FlushPendingHighSurrogate()
		{
			if (pendingHighSurrogate == '\0')
				return;

			var high = pendingHighSurrogate;
			pendingHighSurrogate = '\0';
			SendUnicodeScalarImmediate(high.ToString());
		}

		private void SendUnicodeCharCore(char ch, uint modifiers)
		{
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			uint vk = 0;
			var needShift = false;
			var needAltGr = false;
			var hasMappedKeystroke = System.Text.Rune.TryCreate(ch, out var rune)
				&& KeyCodes.TryMapRuneToKeystroke(rune, targetKeybdLayoutRef?.Value, out vk, out needShift, out needAltGr)
				&& vk != 0;

			if (sendMode == SendModes.Input && hasMappedKeystroke && TryQueuePlatformMappedTextKey(ch, modifiers, extraInfo))
				return;

			if (hasMappedKeystroke)
				SetModifierLRState(modifiers, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, extraInfo);

			// In Input mode, record as text so it can be interspersed correctly.
			if (sendMode == SendModes.Input)
			{
				AddArrayEvent(ArrayEvent.TextEvent(ch.ToString()));
				return;
			}

			var lht = script.HookThread as UnixHookThread;
			if (lht == null)
				return;

			if (TrySendPlatformUnicodeChar(lht, ch, extraInfo, hasMappedKeystroke, vk, needShift, needAltGr))
				return;

			WithSendScope(lht, () =>
			{
				// Prefer keystroke mapping when possible.
				if (hasMappedKeystroke)
					SendMappedUnicodeKeystroke(vk, needShift, needAltGr, extraInfo);
				else
					sim.SimulateTextEntry(ch.ToString());
			});
		}

		protected void SendMappedUnicodeKeystroke(uint vk, bool needShift, bool needAltGr, long extraInfo)
		{
			var ms = DateTime.UtcNow;
			if (needAltGr)
				backend.KeyDown(VK_ALTGR, ms, extraInfo);
			if (needShift)
				backend.KeyDown(VK_SHIFT, ms, extraInfo);

			backend.KeyStroke(vk, ms, extraInfo);

			if (needShift)
				backend.KeyUp(VK_SHIFT, ms, extraInfo);
			if (needAltGr)
				backend.KeyUp(VK_ALTGR, ms, extraInfo);
		}

		protected void SendKeyEventDirect(KeyEventTypes eventType, uint vk, long extraInfo)
		{
			var now = DateTime.UtcNow;

			if (eventType == KeyEventTypes.KeyDown || eventType == KeyEventTypes.KeyDownAndUp)
				backend.KeyDown(vk, now, extraInfo);

			if (eventType == KeyEventTypes.KeyUp || eventType == KeyEventTypes.KeyDownAndUp)
				backend.KeyUp(vk, now, extraInfo);
		}

		// Run an action inside a send scope so the hook thread can classify echoed synthetic input.
		protected static void WithSendScope(UnixHookThread lht, Action action)
		{
			var sendScope = lht.EnterSendScope();
			try
			{
				action();
			}
			finally
			{
				sendScope.Dispose();
			}
		}

		internal void SimulateKeyEvent(uint vk, bool isPress, long extraInfo)
		{
			if (vk == 0)
				return;

			var ms = DateTime.UtcNow;
			if (isPress)
				backend.KeyDown(vk, ms, extraInfo);
			else
				backend.KeyUp(vk, ms, extraInfo);
		}

		// Dispatch the recorded event array to the platform backend.
		protected virtual void DispatchEventArray(UnixHookThread lht, InputArrayState state, long extraInfo)
			=> WithSendScope(lht, () => ReplayEventArrayEvents(state.Events, extraInfo));

		// Dispatch a single keyboard event to the platform backend.
		protected virtual void DispatchKeybdEvent(UnixHookThread lht, KeyEventTypes eventType, uint vk, long extraInfo, bool autoRepeat)
			=> WithSendScope(lht, () => SendKeyEventDirect(eventType, vk, extraInfo));

		protected virtual bool TrySendPlatformUnicodeChar(UnixHookThread lht, char ch, long extraInfo, bool hasMappedKeystroke, uint vk, bool needShift, bool needAltGr) => false;

		// Emits a complete Unicode scalar string via the platform's text path. Base returns false
		// so the caller falls back to per-character replay.
		protected virtual bool TrySendPlatformUnicodeText(UnixHookThread lht, string text, long extraInfo) => false;

		protected virtual bool TryQueuePlatformMappedTextKey(char ch, uint modifiers, long extraInfo) => false;

		#endregion

		internal override int PbEventCount() => 0;

		// No journal-playback hook exists on Unix/X11; SendPlay is sent as SendEvent (see WarnIfPlayUnsupported).
		protected override bool SupportsPlayMode => false;

		internal override ResultType LayoutHasAltGrDirect(nint layout) => ResultType.ConditionFalse;

		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			// Can't use the down-state query because it doesn't have toggle-state info:
			var startingState = script.HookThread.IsKeyToggledOn(vk) ? ToggleValueType.On : ToggleValueType.Off;

			if (toggleValue != ToggleValueType.On && toggleValue != ToggleValueType.Off) // Shouldn't be called this way.
				return startingState;

			if (startingState == toggleValue) // It's already in the desired state, so just return the state.
				return startingState;

			//if (vk == VK_NUMLOCK) // v1.1.22.05: This also applies to CapsLock and ScrollLock.
			{
				// If the key is being held down, sending a KEYDOWNANDUP won't change its toggle
				// state unless the key is "released" first.  This has been confirmed for NumLock,
				// CapsLock and ScrollLock on Windows 2000 (in a VM) and Windows 10.
				// Examples where problems previously occurred:
				//   ~CapsLock & x::Send abc  ; Produced "ABC"
				//   ~CapsLock::Send abc  ; Alternated between "abc" and "ABC", even without {Blind}
				//   ~ScrollLock::SetScrollLockState Off  ; Failed to change state
				// The behavior can still be observed by sending the keystrokes manually:
				//   ~NumLock::Send {NumLock}  ; No effect
				//   ~NumLock::Send {NumLock up}{NumLock}  ; OK
				// OLD COMMENTS:
				// Sending an extra up-event first seems to prevent the problem where the Numlock
				// key's indicator light doesn't change to reflect its true state (and maybe its
				// true state doesn't change either).  This problem tends to happen when the key
				// is pressed while the hook is forcing it to be either ON or OFF (or it suppresses
				// it because it's a hotkey).  Needs more testing on diff. keyboards & OSes:
				if (script.HookThread.IsKeyDownLogical(vk))
					SendKeyEvent(KeyEventTypes.KeyUp, vk);
			}
			// Since it's not already in the desired state, toggle it:
			SendKeyEvent(KeyEventTypes.KeyDownAndUp, vk);

			if (vk == VK_CAPITAL && toggleValue == ToggleValueType.Off && script.HookThread.IsKeyToggledOn(vk))
			{
				// Fix for v1.0.36.06: Since it's Capslock and it didn't turn off as attempted, it's probably because
				// the OS is configured to turn Capslock off only in response to pressing the SHIFT key (via Ctrl Panel's
				// Regional settings).  So send shift to do it instead:
				SendKeyEvent(KeyEventTypes.KeyDownAndUp, VK_SHIFT);
			}

			Thread.Sleep(1);

			return startingState;
		}

		protected internal override void LongOperationUpdate() { }
		protected internal override void LongOperationUpdateForSendKeys() { }

		protected override void RegisterHook() { }

		internal override int MouseCoordToAbs(int coord, int width_or_height) => ((65536 * coord) / width_or_height) + (coord < 0 ? -1 : 1);

		#region SmartTextEmitter + sinks (used by SendEventArray + SendUnicodeChar Event-mode)

		private interface IKeySink
		{
			void Down(uint vk, DateTime ms, long extraInfo);
			void Up(uint vk, DateTime ms, long extraInfo);
			void Stroke(uint vk, DateTime ms, long extraInfo);
			void Flush(); // no-op for direct mode
		}

		private sealed class DirectKeySink : IKeySink
		{
			private readonly UnixKeyboardMouseSender self;
			private readonly long keyDelay;
			private readonly long keyDuration;

			internal DirectKeySink(UnixKeyboardMouseSender self, long keyDelay, long keyDuration)
			{
				this.self = self;
				this.keyDelay = keyDelay;
				this.keyDuration = keyDuration;
			}

			public void Down(uint vk, DateTime ms, long extraInfo)
			{
				self.backend.KeyDown(vk, ms, extraInfo);
				if (keyDuration >= 0)
					Keysharp.Internals.Flow.SleepWithoutInterruption((int)keyDuration);
			}

			public void Up(uint vk, DateTime ms, long extraInfo)
			{
				self.backend.KeyUp(vk, ms, extraInfo);
				if (keyDelay >= 0)
					Keysharp.Internals.Flow.SleepWithoutInterruption((int)keyDelay);
			}

			public void Stroke(uint vk, DateTime ms, long extraInfo)
			{
				Down(vk, ms, extraInfo);
				Up(vk, ms, extraInfo);
			}

			public void Flush() { /* no-op */ }
		}

		#endregion

		#region PlatformKeySimulationBackend

		internal sealed class PlatformEventSimulator
		{
#if OSX
			private static void MissingMacBackend()
				=> throw new InvalidOperationException("A macOS input path reached the generic no-op Unix simulator. Add an explicit MacKeyboardMouseSender implementation for this operation.");

			public void SimulateKeyPress(uint vk) => MissingMacBackend();
			public void SimulateKeyRelease(uint vk) => MissingMacBackend();
			public void SimulateMouseMovementRelative(short x, short y) => MissingMacBackend();
			public void SimulateMouseMovement(short x, short y) => MissingMacBackend();
			public void SimulateMousePress(MouseButton button) => MissingMacBackend();
			public void SimulateMousePress(short x, short y, MouseButton button) => MissingMacBackend();
			public void SimulateMouseRelease(MouseButton button) => MissingMacBackend();
			public void SimulateMouseRelease(short x, short y, MouseButton button) => MissingMacBackend();
			public void SimulateMouseWheel(short delta, MouseWheelScrollDirection direction, MouseWheelScrollType type) => MissingMacBackend();
			public void SimulateTextEntry(string text) => MissingMacBackend();
#else
			public void SimulateKeyPress(uint vk) { }
			public void SimulateKeyRelease(uint vk) { }
			public void SimulateMouseMovementRelative(short x, short y) { }
			public void SimulateMouseMovement(short x, short y) { }
			public void SimulateMousePress(MouseButton button) { }
			public void SimulateMousePress(short x, short y, MouseButton button) { }
			public void SimulateMouseRelease(MouseButton button) { }
			public void SimulateMouseRelease(short x, short y, MouseButton button) { }
			public void SimulateMouseWheel(short delta, MouseWheelScrollDirection direction, MouseWheelScrollType type) { }
			public void SimulateTextEntry(string text) { }
#endif
		}

		internal sealed class PlatformKeySimulationBackend
		{
			private readonly Script owner;
			internal readonly PlatformEventSimulator sim;

			public PlatformKeySimulationBackend(Script owner, PlatformEventSimulator sim = null)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
				this.sim = sim ?? new PlatformEventSimulator();
			}

			public void KeyDown(uint vk, DateTime ms, long extraInfo)
			{
				_ = owner.Permissions.EnsureInputControl(operation: "send keyboard input");
#if OSX
				if (vk == VK_CAPITAL && Keysharp.Internals.Input.MacOS.MacCapsLockState.TryToggle())
					return;
#endif
				sim.SimulateKeyPress(vk);
			}

			public void KeyUp(uint vk, DateTime ms, long extraInfo)
			{
				_ = owner.Permissions.EnsureInputControl(operation: "send keyboard input");
#if OSX
				// The toggle was already applied by the paired KeyDown; a CapsLock
				// key-up has no effect on the lock state, so don't post anything.
				if (vk == VK_CAPITAL && Keysharp.Internals.Input.MacOS.MacCapsLockState.IsAvailable)
					return;
#endif
				sim.SimulateKeyRelease(vk);
			}

			public void KeyStroke(uint vk, DateTime ms, long extraInfo)
			{
				KeyDown(vk, ms, extraInfo);
				KeyUp(vk, ms, extraInfo);
			}

			public IKeySimulationSequence BeginSequence()
				=> new PlatformKeySequence(this);
		}

		internal sealed class PlatformKeySequence : IKeySimulationSequence
		{
			private enum ActionType { Down, Up }

			private readonly PlatformKeySimulationBackend backend;
			private readonly List<(ActionType Type, uint Vk)> actions = new();
			private bool committed;

			public PlatformKeySequence(PlatformKeySimulationBackend backend)
				=> this.backend = backend;

			public void AddKeyDown(uint vk)
				=> actions.Add((ActionType.Down, vk));

			public void AddKeyUp(uint vk)
				=> actions.Add((ActionType.Up, vk));

			public void AddKeyStroke(uint vk)
			{
				AddKeyDown(vk);
				AddKeyUp(vk);
			}

			public void Commit(long extraInfo)
			{
				if (committed) return;
				committed = true;

				var ms = DateTime.UtcNow;

				if (actions.Count > 0)
				{
					foreach (var (type, vk) in actions)
					{
						if (type == ActionType.Down)
							backend.KeyDown(vk, ms, extraInfo);
						else
							backend.KeyUp(vk, ms, extraInfo);
					}
				}

				actions.Clear();
			}

			public void Dispose()
			{
				if (!committed)
					Commit(0);
			}
		}

		#endregion
	}
}

#endif
