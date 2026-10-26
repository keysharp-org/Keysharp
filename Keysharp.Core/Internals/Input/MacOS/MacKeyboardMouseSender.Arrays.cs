#if OSX
using Keysharp.Builtins;
using Keysharp.Internals.Input.Hooks.Unix;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;

namespace Keysharp.Internals.Input.MacOS
{
	internal partial class MacKeyboardMouseSender
	{
		protected sealed class InputArrayState
		{
			internal readonly List<ArrayEvent> Events;
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
		private readonly List<ArrayEvent> outerEvents = new();

		// Holds a UTF-16 high surrogate between the two SendUnicodeChar calls of an astral
		// scalar (e.g. emoji) while sending in Event mode; see SendUnicodeChar.
		private char pendingHighSurrogate;

		internal override bool MouseButtonsSwapped => false;

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
			internal readonly uint Sc;
			internal readonly uint ModifiersLR;
			internal readonly bool AutoRepeat;

			// Delay
			internal readonly int DelayMs;

			// Text
			internal readonly char Text;

			// Mouse (x/y may be CoordUnspecified)
			internal readonly int X;
			internal readonly int Y;
			internal readonly MouseButton Button;
			internal readonly short WheelDelta;

			private ArrayEvent(ArrayEventType type, uint vk, uint modifiersLR, int delayMs, char text,
							int x, int y, MouseButton button, short wheelDelta, bool autoRepeat = false, uint sc = 0)
			{
				Type = type;
				Vk = vk;
				Sc = sc;
				ModifiersLR = modifiersLR;
				AutoRepeat = autoRepeat;
				DelayMs = delayMs;
				Text = text;
				X = x;
				Y = y;
				Button = button;
				WheelDelta = wheelDelta;
			}

			internal static ArrayEvent Key(ArrayEventType type, uint vk, uint sc, uint modifiersLR, bool autoRepeat)
				=> new(type, vk, modifiersLR, 0, '\0', 0, 0, MouseButton.NoButton, 0, autoRepeat, sc);

			internal static ArrayEvent Delay(int ms)
				=> new(ArrayEventType.DelayMs, 0, 0, ms, '\0', 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent TextEvent(char text)
				=> new(ArrayEventType.Text, 0, 0, 0, text, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveAbs(int x, int y)
				=> new(ArrayEventType.MouseMoveAbs, 0, 0, 0, '\0', x, y, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveRel(int dx, int dy)
				=> new(ArrayEventType.MouseMoveRel, 0, 0, 0, '\0', dx, dy, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseButtonEvent(ArrayEventType type, MouseButton button, int x, int y)
				=> new(type, 0, 0, 0, '\0', x, y, button, 0);

			internal static ArrayEvent MouseWheelV(short delta)
				=> new(ArrayEventType.MouseWheelV, 0, 0, 0, '\0', 0, 0, MouseButton.NoButton, delta);

			internal static ArrayEvent MouseWheelH(short delta)
				=> new(ArrayEventType.MouseWheelH, 0, 0, 0, '\0', 0, 0, MouseButton.NoButton, delta);
		}

		protected void AddArrayEvent(in ArrayEvent ev)
		{
			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return;

				var st = inputStack.Peek();
				st.Events.Add(ev);
			}
		}

		#endregion

		internal override void InitEventArray(int maxEvents, uint modifiersLR)
		{
			var cap = maxEvents > 0 ? Math.Min(maxEvents, 2048) : 512;
			lock (inputGate)
			{
				var events = inputStack.Count == 0 ? outerEvents : new List<ArrayEvent>(cap);
				events.EnsureCapacity(cap);
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
					st.Events.Clear();
					eventModifiersLR = st.PrevEventModifiers;
					sendInputCursorPos = st.PrevCursorPosition;
				}

				return inputStack.Count == 0 ? SendModes.Event : inputStack.Peek().Mode;
			}
		}

		internal override int SiEventCount()
		{
			lock (inputGate)
				return inputStack.Count > 0 ? inputStack.Peek().Events.Count : 0;
		}

		internal override void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y)
		{
			// MouseClick encodes type in the high word; handle that before the MOUSEEVENTF path.
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

			// Later recorded events use this modifier prediction before native replay starts.
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
					AddArrayEvent(ArrayEvent.TextEvent(ch));
				}
				return;
			}

			// Normal key: record as down/up.
			AddArrayEvent(ArrayEvent.Key(isKeyUp ? ArrayEventType.KeyUp : ArrayEventType.KeyDown, vk, sc, keyAsModifiersLR,
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

			if (st.Events.Count == 0 || (UsesSenderProvider && SendEventArrayToProvider()))
				return;

			var lht = script.HookThread as UnixHookThread;
			if (lht == null)
				return;

			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			DispatchEventArray(lht, st, extraInfo);
		}

		protected override bool TryGetSenderEvents(List<SenderEvent> events)
		{
			List<ArrayEvent> recorded;

			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return true;

				recorded = inputStack.Peek().Events;
			}

			var extraInfo = (ulong)KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			ScreenRect? desktop = null;

			SenderEvent MoveTo(int x, int y, uint flags = 0, uint data = 0)
			{
				var (absoluteX, absoluteY) = ToProviderAbsolute(x, y, desktop ??= Platform.Screen.GetVirtualScreenBounds());
				return new(true, flags | (uint)(MOUSEEVENTF.MOVE | MOUSEEVENTF.ABSOLUTE | MOUSEEVENTF.VIRTUALDESK),
					X: absoluteX, Y: absoluteY, MouseData: data, ExtraInfo: extraInfo);
			}

			foreach (var e in recorded)
			{
				switch (e.Type)
				{
					case ArrayEventType.KeyDown:
					case ArrayEventType.KeyUp:
						// kVK 0 is the A key, so only a failed mapping marks a key without a code.
						var sc = e.Sc;
						if (sc == 0 && !KeyCodes.TryMapVkToMacCode(e.Vk, out sc))
							return false;
						events.Add(new(false, e.Type == ArrayEventType.KeyUp ? (uint)KEYEVENTF_KEYUP : 0u, (ushort)e.Vk, (ushort)sc, ExtraInfo: extraInfo));
						break;

					case ArrayEventType.DelayMs:
						events.Add(new(false, 0, Delay: e.DelayMs));
						break;

					case ArrayEventType.Text:
						return false;

					case ArrayEventType.MouseMoveAbs:
						if (e.X != CoordUnspecified && e.Y != CoordUnspecified)
							events.Add(MoveTo(e.X, e.Y));
						break;

					case ArrayEventType.MouseMoveRel:
						events.Add(new(true, (uint)MOUSEEVENTF.MOVE, X: e.X, Y: e.Y, ExtraInfo: extraInfo));
						break;

					case ArrayEventType.MousePress:
					case ArrayEventType.MouseRelease:
						var (flags, data) = e.Button switch
						{
							MouseButton.Button1 => ((uint)MOUSEEVENTF.LEFTDOWN, 0u),
							MouseButton.Button2 => ((uint)MOUSEEVENTF.RIGHTDOWN, 0u),
							MouseButton.Button3 => ((uint)MOUSEEVENTF.MIDDLEDOWN, 0u),
							MouseButton.Button4 => ((uint)MOUSEEVENTF.XDOWN, MouseUtils.XBUTTON1),
							_ => ((uint)MOUSEEVENTF.XDOWN, MouseUtils.XBUTTON2)
						};
						if (e.Type == ArrayEventType.MouseRelease)
							flags <<= 1; // Each up flag is its down flag shifted left.
						events.Add(e.X != CoordUnspecified && e.Y != CoordUnspecified
							? MoveTo(e.X, e.Y, flags, data)
							: new(true, flags, MouseData: data, ExtraInfo: extraInfo));
						break;

					case ArrayEventType.MouseWheelV:
					case ArrayEventType.MouseWheelH:
						events.Add(new(true, (uint)(e.Type == ArrayEventType.MouseWheelV ? MOUSEEVENTF.WHEEL : MOUSEEVENTF.HWHEEL),
							MouseData: unchecked((uint)e.WheelDelta), ExtraInfo: extraInfo));
						break;
				}
			}

			return true;
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


		internal override void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint eventFlags, long extraInfo, bool autoRepeat = false)
		{
			if (script.HookThread is UnixHookThread lht)
				DispatchKeybdEvent(lht, eventType, vk, extraInfo, autoRepeat);
		}

		internal override void SendUnicodeChar(char ch, uint modifiers)
		{
			if (sendMode == SendModes.Input)
			{
				AddArrayEvent(ArrayEvent.TextEvent(ch));
				return;
			}

			if (char.IsHighSurrogate(ch))
			{
				FlushPendingHighSurrogate();
				pendingHighSurrogate = ch;
				return;
			}

			if (pendingHighSurrogate != '\0')
			{
				var high = pendingHighSurrogate;
				pendingHighSurrogate = '\0';
				if (char.IsLowSurrogate(ch))
				{
					Span<char> pair = stackalloc char[] { high, ch };
					PostUnicodeText(pair);
					return;
				}
				Span<char> unpaired = stackalloc char[] { high };
				PostUnicodeText(unpaired);
			}

			Span<char> text = stackalloc char[] { ch };
			PostUnicodeText(text);
		}

		private void PostUnicodeText(ReadOnlySpan<char> text)
		{
			if (script.HookThread is not UnixHookThread lht)
				return;
			using var scope = lht.EnterSendScope();
			MacNativeInput.PostUnicodeText(text, KeyIgnoreLevel(ThreadAccessors.A_SendLevel));
		}

		private void FlushPendingHighSurrogate()
		{
			if (pendingHighSurrogate == '\0')
				return;
			Span<char> text = stackalloc char[] { pendingHighSurrogate };
			pendingHighSurrogate = '\0';
			PostUnicodeText(text);
		}

		internal override int PbEventCount() => 0;
		protected override bool SupportsPlayMode => false;
		internal override ResultType LayoutHasAltGrDirect(nint layout) => ResultType.ConditionFalse;

		protected internal override void LongOperationUpdate() { }
		protected internal override void LongOperationUpdateForSendKeys() { }

		protected override void RegisterHook() { }

		internal override int MouseCoordToAbs(int coord, int width_or_height) => width_or_height <= 0 ? 0 : ((65536 * coord) / width_or_height) + (coord < 0 ? -1 : 1);

	}
}
#endif
