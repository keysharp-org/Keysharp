using Keysharp.Builtins;
#if LINUX
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Keysharp.Internals.Input.Hooks.Linux;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.Mouse;
using Keysharp.Internals.Input.Unix;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Linux
{
	/// <summary>
	/// Keyboard/mouse sender that routes input through keysharp-input.
	/// </summary>
	internal sealed class LinuxKeyboardMouseSender : KeyboardMouseSender
	{
		internal LinuxKeyboardMouseSender(Script script) : base(script) { }

		private const int MaxInputBatchSize = KeysharpInputClient.MaxInputsPerRequest;
		private const int MaxMouseMoveChunk = 1000;

		private readonly List<QueuedInputEvent> eventQueue = new(MaxInitialEventsSI);
		private static readonly (uint Flag, uint Button, bool Down)[] CompositorButtonRoutes =
		[
			((uint)MOUSEEVENTF.LEFTDOWN, 1u, true),
			((uint)MOUSEEVENTF.LEFTUP, 1u, false),
			((uint)MOUSEEVENTF.RIGHTDOWN, 3u, true),
			((uint)MOUSEEVENTF.RIGHTUP, 3u, false),
			((uint)MOUSEEVENTF.MIDDLEDOWN, 2u, true),
			((uint)MOUSEEVENTF.MIDDLEUP, 2u, false),
		];

		private readonly struct QueuedInputEvent
		{
			internal readonly KeysharpInputClient.Input Input;
			internal readonly int DelayMs;
			internal readonly bool IsDelay;

			private QueuedInputEvent(KeysharpInputClient.Input input, int delayMs, bool isDelay)
			{
				Input = input;
				DelayMs = delayMs;
				IsDelay = isDelay;
			}

			internal static QueuedInputEvent FromInput(KeysharpInputClient.Input input) => new(input, 0, false);
			internal static QueuedInputEvent Delay(int delayMs) => new(default, delayMs, true);
		}

		internal override bool MouseButtonsSwapped => false;

		protected internal override bool TrySendPlatformRawText(ReadOnlySpan<char> text, ref int keyIndex, uint modifiersLR)
		{
			if (keyIndex < 0 || keyIndex >= text.Length)
				return false;

			var localEvents = sendMode == SendModes.Event ? new List<KeysharpInputClient.Input>() : null;
			var doKeyDelay = KeyDelayWouldSleepOrQueue();
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);

			for (var i = keyIndex; i < text.Length;)
			{
				if (!QueueRawTextItem(localEvents, text, ref i, modifiersLR, extraInfo))
					continue;

				if (doKeyDelay)
				{
					FlushRawTextEvents(localEvents);
					DoKeyDelay();
				}
			}

			if (localEvents != null && localEvents.Count != 0)
				SendInputBatches(localEvents);

			keyIndex = text.Length - 1;
			return true;
		}

		internal override void InitEventArray(int maxEvents, uint modifiersLR)
		{
			eventQueue.Clear();
			base.maxEvents = maxEvents;
			eventModifiersLR = modifiersLR;
			sendInputCursorPos.X = CoordUnspecified;
			sendInputCursorPos.Y = CoordUnspecified;
			abortArraySend = false;
		}

		internal override void CleanupEventArray(long finalKeyDelay)
		{
			eventQueue.Clear();

			// Do this before any final corrective delay/keystrokes so they are emitted immediately.
			sendMode = SendModes.Event;
			DoKeyDelay(finalKeyDelay);
		}

		internal override int SiEventCount() => eventQueue.Count;
		internal override int PbEventCount() => 0;

		// No journal-playback hook via uinput/input service; SendPlay is sent as SendEvent (see WarnIfPlayUnsupported).
		protected override bool SupportsPlayMode => false;

		internal override void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo, bool autoRepeat = false)
		{
			if (vk == 0 && sc == 0 && eventFlags == 0)
			{
				eventQueue.Add(QueuedInputEvent.Delay((int)extraInfo));
				return;
			}

			var flags = (KeysharpInputClient.KeyEventFlags)eventFlags;
			var isKeyUp = (flags & KeysharpInputClient.KeyEventFlags.KeyUp) != 0;
			var isUnicode = (flags & KeysharpInputClient.KeyEventFlags.Unicode) != 0;

			if (isKeyUp)
				eventModifiersLR &= ~keyAsModifiersLR;
			else
				eventModifiersLR |= keyAsModifiersLR;

			if (!isUnicode)
				flags = NormalizeKeyFlags(vk, ref sc, flags);

			QueueInput(KeysharpInputClient.Input.Key(
				vk: (ushort)vk,
				scan: (ushort)sc,
				flags: flags,
				extraInfo: (ulong)extraInfo));
		}

		internal override void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y)
		{
			QueueInput(CreateMouseInput(eventFlags, data, x, y, (ulong)KeyIgnoreLevel(ThreadAccessors.A_SendLevel)));
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			if (eventQueue.Count == 0)
				return;

			// SendInput bypasses hooks, then reconciles logical modifier state.
			try
			{
				var batch = new List<KeysharpInputClient.Input>(Math.Min(eventQueue.Count, MaxInputBatchSize));

				for (var i = 0; i < eventQueue.Count; i++)
				{
					var ev = eventQueue[i];

					if (!ev.IsDelay)
					{
						batch.Add(ev.Input);
						continue;
					}

					SendInputBatches(batch, KeysharpInputClient.SynthFlags.BypassHook);
					batch.Clear();

					if (i == eventQueue.Count - 1)
						finalKeyDelay = ev.DelayMs;
					else if (ev.DelayMs > 0)
						Keysharp.Internals.Flow.SleepWithoutInterruption(ev.DelayMs);
				}

				SendInputBatches(batch, KeysharpInputClient.SynthFlags.BypassHook);
			}
			finally
			{
				eventQueue.Clear();
			}

			ReconcileLogicalModifiersFromOs();
		}

		/// <summary>Reconciles logical modifiers after bypass-hook SendInput.</summary>
		private void ReconcileLogicalModifiersFromOs()
		{
			var ht = script.HookThread;
			var sender = ht.kbdMsSender;

			if (sender == null)
				return;

			if (KeysharpInputManager.TryGetModifierState(out var logicalMods, out _, out _, out _, out _))
			{
				sender.modifiersLRLogical = logicalMods;
				sender.modifiersLRLogicalNonIgnored = logicalMods;
			}
			else
			{
				var modsCurrent = sender.GetModifierLRState(true);
				sender.modifiersLRLogical = sender.modifiersLRLogicalNonIgnored = modsCurrent;
			}
		}

		/// <summary>Uses the input service-backed live toggle state after sending a lock key.</summary>
		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			var startingState = script.HookThread.IsKeyToggledOn(vk) ? ToggleValueType.On : ToggleValueType.Off;

			if (toggleValue != ToggleValueType.On && toggleValue != ToggleValueType.Off)
				return startingState;

			if (startingState == toggleValue)
				return startingState;

			// Release if held (prevents the toggle being swallowed by the OS).
			if (script.HookThread.IsKeyDownLogical(vk))
				SendKeyEvent(KeyEventTypes.KeyUp, vk);

			SendKeyEvent(KeyEventTypes.KeyDownAndUp, vk);

			System.Threading.Thread.Sleep(5);

			if (vk == VK_CAPITAL && toggleValue == ToggleValueType.Off && script.HookThread.IsKeyToggledOn(vk))
			{
				// Some keyboard layouts only toggle CapsLock off via Shift.
				SendKeyEvent(KeyEventTypes.KeyDownAndUp, VK_SHIFT);
			}

			return startingState;
		}

		/// <summary>Immediate single-event send, equivalent to keybd_event() on Windows.</summary>
		internal override void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint flags, long extraInfo, bool autoRepeat = false)
		{
			var keyFlags = NormalizeKeyFlags(vk, ref sc, (KeysharpInputClient.KeyEventFlags)flags);

			if (eventType == KeyEventTypes.KeyDownAndUp)
			{
				SendInputBatches(
				[
					KeysharpInputClient.Input.Key((ushort)vk, (ushort)sc, keyFlags, extraInfo: (ulong)extraInfo),
					KeysharpInputClient.Input.Key((ushort)vk, (ushort)sc,
						keyFlags | KeysharpInputClient.KeyEventFlags.KeyUp, extraInfo: (ulong)extraInfo),
				]);
				return;
			}

			if (eventType == KeyEventTypes.KeyUp)
				keyFlags |= KeysharpInputClient.KeyEventFlags.KeyUp;

			SendInputBatches(
			[
				KeysharpInputClient.Input.Key((ushort)vk, (ushort)sc, keyFlags, extraInfo: (ulong)extraInfo),
			]);
		}

		internal override void SendUnicodeChar(char ch, uint modifiers)
		{
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);

			if (!Rune.TryCreate(ch, out var rune)
				|| !rune.IsAscii
				|| !KeyCodes.TryMapRuneToKeystroke(rune, targetKeybdLayoutRef?.Value, out var vk, out var needShift, out var needAltGr)
				|| vk == 0)
			{
				SendDaemonUnicodeChar(ch, modifiers, extraInfo);
				return;
			}

			uint transientModifiers = 0;

			if (needShift)
				transientModifiers |= MOD_LSHIFT;

			if (needAltGr)
				transientModifiers |= MOD_RALT;

			var targetModifiers = modifiers | transientModifiers;
			SetModifierLRState(targetModifiers, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, extraInfo);
			SendKeyEvent(KeyEventTypes.KeyDownAndUp, vk, 0, 0, false, extraInfo);
			SetModifierLRState(modifiers, sendMode != SendModes.Event ? eventModifiersLR : targetModifiers, 0, false, true, extraInfo);
		}

		internal override void SendUnicodePair(char high, char low, uint modifiers)
		{
			// Keep the surrogate pair in one native synthesis batch.
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			Span<char> units = stackalloc char[2] { high, low };
			SendDaemonUnicodeUnits(units, modifiers, extraInfo);
		}

		private void SendDaemonUnicodeChar(char ch, uint modifiers, long extraInfo)
		{
			Span<char> units = stackalloc char[1] { ch };
			SendDaemonUnicodeUnits(units, modifiers, extraInfo);
		}

		// Emit one or more UTF-16 code units (a single char or a surrogate pair) as a
		// single synthesis batch, wrapped by exactly one modifier clear/restore.
		private void SendDaemonUnicodeUnits(ReadOnlySpan<char> units, uint modifiers, long extraInfo)
		{
			// Clear all logical/synthetic modifiers to a clean baseline first. The
			// daemon fires its own self-contained Ctrl+Shift+U trigger and
			// unconditionally releases Ctrl/Shift at the end; without clearing, a
			// modifier the caller holds (e.g. Send "^{U+2603}c") is stranded and our
			// logical state desyncs. The trailing restore re-presses `modifiers`.
			SetModifierLRState(0u, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, extraInfo);

			var inputs = new KeysharpInputClient.Input[units.Length * 2];

			for (var i = 0; i < units.Length; i++)
			{
				inputs[i * 2] = KeysharpInputClient.Input.Key(0, units[i],
					KeysharpInputClient.KeyEventFlags.Unicode, extraInfo: (ulong)extraInfo);
				inputs[i * 2 + 1] = KeysharpInputClient.Input.Key(0, units[i],
					KeysharpInputClient.KeyEventFlags.Unicode | KeysharpInputClient.KeyEventFlags.KeyUp,
					extraInfo: (ulong)extraInfo);
			}

			if (sendMode != SendModes.Event)
			{
				foreach (var input in inputs)
					QueueInput(input);
			}
			else
				SendInputBatches(inputs);

			SetModifierLRState(modifiers, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, extraInfo);
		}

		private bool QueueRawTextItem(List<KeysharpInputClient.Input> events, ReadOnlySpan<char> text, ref int index, uint modifiersLR, long extraInfo)
		{
			var ch = text[index];

			if (ch == '\r')
			{
				index += index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
				QueueKey(events, KeyEventTypes.KeyDownAndUp, VK_RETURN, extraInfo);
				return true;
			}

			index++;

			if (ch == '\n')
			{
				QueueKey(events, KeyEventTypes.KeyDownAndUp, VK_RETURN, extraInfo);
				return true;
			}

			if (ch == '\b')
			{
				QueueKey(events, KeyEventTypes.KeyDownAndUp, VK_BACK, extraInfo);
				return true;
			}

			if (ch == '\t')
			{
				QueueKey(events, KeyEventTypes.KeyDownAndUp, VK_TAB, extraInfo);
				return true;
			}

			if (Rune.DecodeFromUtf16(text[(index - 1)..], out var rune, out var charsConsumed) != OperationStatus.Done)
				return false;

			index += charsConsumed - 1;
			QueueTextRune(events, rune, modifiersLR, extraInfo);
			return true;
		}

		private static void FlushRawTextEvents(List<KeysharpInputClient.Input> events)
		{
			if (events == null || events.Count == 0)
				return;

			SendInputBatches(events);
			events.Clear();
		}

		private void QueueTextRune(List<KeysharpInputClient.Input> events, Rune rune, uint modifiersLR, long extraInfo)
		{
			if (rune.IsAscii
				&& KeyCodes.TryMapRuneToKeystroke(rune, targetKeybdLayoutRef?.Value, out var vk, out var needShift, out var needAltGr)
				&& vk != 0)
			{
				uint transientModifiers = 0;

				if (needShift)
					transientModifiers |= MOD_LSHIFT;

				if (needAltGr)
					transientModifiers |= MOD_RALT;

				QueueModifierTransition(events, transientModifiers & ~modifiersLR, true, extraInfo);
				QueueKey(events, KeyEventTypes.KeyDownAndUp, vk, extraInfo);
				QueueModifierTransition(events, transientModifiers & ~modifiersLR, false, extraInfo);
				return;
			}

			Span<char> chars = stackalloc char[2];
			var length = rune.EncodeToUtf16(chars);

			for (var i = 0; i < length; i++)
				QueueUnicodeUnit(events, chars[i], extraInfo);
		}

		private void QueueUnicodeUnit(List<KeysharpInputClient.Input> events, char ch, long extraInfo)
		{
			AddOrQueue(events, KeysharpInputClient.Input.Key(
				0,
				ch,
				KeysharpInputClient.KeyEventFlags.Unicode,
				extraInfo: (ulong)extraInfo));
			AddOrQueue(events, KeysharpInputClient.Input.Key(
				0,
				ch,
				KeysharpInputClient.KeyEventFlags.Unicode | KeysharpInputClient.KeyEventFlags.KeyUp,
				extraInfo: (ulong)extraInfo));
		}

		private void QueueModifierTransition(List<KeysharpInputClient.Input> events, uint modifiers, bool down, long extraInfo)
		{
			if ((modifiers & MOD_LSHIFT) != 0)
				QueueKey(events, down ? KeyEventTypes.KeyDown : KeyEventTypes.KeyUp, VK_LSHIFT, extraInfo);

			if ((modifiers & MOD_RALT) != 0)
				QueueKey(events, down ? KeyEventTypes.KeyDown : KeyEventTypes.KeyUp, VK_RMENU, extraInfo);
		}

		private void QueueKey(List<KeysharpInputClient.Input> events, KeyEventTypes eventType, uint vk, long extraInfo)
		{
			var downFlags = (KeysharpInputClient.KeyEventFlags)0;
			var upFlags = KeysharpInputClient.KeyEventFlags.KeyUp;

			if (eventType != KeyEventTypes.KeyUp)
				AddOrQueue(events, KeysharpInputClient.Input.Key((ushort)vk, 0, downFlags, extraInfo: (ulong)extraInfo));

			if (eventType != KeyEventTypes.KeyDown)
				AddOrQueue(events, KeysharpInputClient.Input.Key((ushort)vk, 0, upFlags, extraInfo: (ulong)extraInfo));
		}

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			// Prefer compositor injection for button/scroll events when available.
			if (sendMode == SendModes.Event
				&& (eventFlags & (uint)MOUSEEVENTF.MOVE) == 0)
			{
				var compositorMouse = WaylandMouseInjection.Backend();

				if (compositorMouse != null && TryRouteToCompositor(compositorMouse, eventFlags, data))
					return;
			}

			if (sendMode != SendModes.Event)
			{
				PutMouseEventIntoArray(eventFlags, data, x, y);
				return;
			}

			SendInputBatches(
			[
				CreateMouseInput(eventFlags, data, x, y, (ulong)KeyIgnoreLevel(ThreadAccessors.A_SendLevel)),
			]);
		}

		internal override void MouseMove(ref int x, ref int y, ref uint eventFlags, long speed, bool moveOffset)
		{
			if (x == CoordUnspecified || y == CoordUnspecified)
				return;

			if (moveOffset)
			{
				if (sendMode == SendModes.Event)
				{
					var compositorMouse = WaylandMouseInjection.Backend();

					if (compositorMouse == null
						|| !compositorMouse.TrySendMouseMoveRelative(x, y))
						SendRelativeMouseMove(x, y,
							(ulong)KeyIgnoreLevel(ThreadAccessors.A_SendLevel));
				}
				else
					QueueRelativeMouseMove(x, y);

				DoMouseDelay();
				eventFlags = 0;
				x = CoordUnspecified;
				y = CoordUnspecified;
				return;
			}

			var targetX = x;
			var targetY = y;
			CoordToScreen(ref targetX, ref targetY, CoordMode.Mouse);

			// Fall back to uinput when the compositor rejects absolute injection.
			if (sendMode == SendModes.Event)
			{
				var compositorMouse = WaylandMouseInjection.Backend();

				if (compositorMouse != null)
				{
					if (speed < 0)
						speed = 0;
					else if (speed > MaxMouseSpeed)
						speed = MaxMouseSpeed;

					var injected = false;

					if (speed == 0 || !GetCursorPos(out var current))
					{
						injected = compositorMouse.TrySendMouseMoveAbsolute(targetX, targetY);

						if (injected)
							DoMouseDelay();
					}
					else
					{
						var steps = Math.Max(1, (int)speed);
						var previousX = current.X;
						var previousY = current.Y;
						injected = true;

						for (var step = 1; step <= steps; step++)
						{
							var nextX = current.X + ((targetX - current.X) * step / steps);
							var nextY = current.Y + ((targetY - current.Y) * step / steps);

							// Only an attempted step can determine whether injection works.
							if (nextX != previousX || nextY != previousY)
							{
								if (!compositorMouse.TrySendMouseMoveAbsolute(nextX, nextY))
								{
									injected = false;
									break;
								}
							}

							previousX = nextX;
							previousY = nextY;
							DoMouseDelay();
						}

					}

					if (injected)
					{
						eventFlags = (uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE;
						x = targetX;
						y = targetY;
						return;
					}
				}
			}

			// uinput fallback maps [0,65535] across the whole virtual desktop.
			var vb = Keysharp.Builtins.Monitor.GetVirtualScreenBounds();
			var absTargetX = MouseCoordToAbs(targetX - (int)vb.Left, (int)vb.Width);
			var absTargetY = MouseCoordToAbs(targetY - (int)vb.Top, (int)vb.Height);

			if (sendMode == SendModes.Input)
			{
				sendInputCursorPos.X = targetX;
				sendInputCursorPos.Y = targetY;
				PutMouseEventIntoArray(
					(uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE,
					0,
					absTargetX,
					absTargetY);
				DoMouseDelay();
				eventFlags = (uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE;
				x = absTargetX;
				y = absTargetY;
				return;
			}

			if (speed < 0)
				speed = 0;
			else if (speed > MaxMouseSpeed)
				speed = MaxMouseSpeed;

			if (speed == 0 || !GetCursorPos(out var cur))
			{
				MouseEvent(
					(uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE,
					0,
					absTargetX,
					absTargetY);
				DoMouseDelay();
			}
			else
			{
				var steps = Math.Max(1, (int)speed);
				var previousX = cur.X;
				var previousY = cur.Y;

				for (var step = 1; step <= steps; step++)
				{
					var nextX = cur.X + ((targetX - cur.X) * step / steps);
					var nextY = cur.Y + ((targetY - cur.Y) * step / steps);
					var stepDx = nextX - previousX;
					var stepDy = nextY - previousY;

					if (stepDx != 0 || stepDy != 0)
					{
						MouseEvent(
							(uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE,
							0,
							MouseCoordToAbs(nextX - (int)vb.Left, (int)vb.Width),
							MouseCoordToAbs(nextY - (int)vb.Top, (int)vb.Height));
					}

					previousX = nextX;
					previousY = nextY;
					DoMouseDelay();
				}
			}

			eventFlags = (uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE;
			x = absTargetX;
			y = absTargetY;
		}


		private static bool TryRouteToCompositor(
			Keysharp.Internals.Window.Linux.Wayland.IWaylandBackend compositorMouse,
			uint flags, uint data)
		{
			foreach (var (flag, button, down) in CompositorButtonRoutes)
				if ((flags & flag) != 0)
					return compositorMouse.TrySendMouseButton(button, down);

			if ((flags & (uint)MOUSEEVENTF.XDOWN) != 0)
				return compositorMouse.TrySendMouseButton((data & 0x0001u) != 0 ? 8u : 9u, true);
			if ((flags & (uint)MOUSEEVENTF.XUP) != 0)
				return compositorMouse.TrySendMouseButton((data & 0x0001u) != 0 ? 8u : 9u, false);
			if ((flags & (uint)MOUSEEVENTF.WHEEL) != 0)
				return compositorMouse.TrySendMouseScroll(unchecked((short)(ushort)(data & 0xFFFF)), true);
			if ((flags & (uint)MOUSEEVENTF.HWHEEL) != 0)
				return compositorMouse.TrySendMouseScroll(unchecked((short)(ushort)(data & 0xFFFF)), false);
			return false;
		}

		private static void SendRelativeMouseMove(int dx, int dy, ulong extraInfo)
		{
			if (dx == 0 && dy == 0)
				return;

			KeysharpInputManager.SendInputViaSynthesisChannel(
			[
				KeysharpInputClient.Input.MouseEvent(
					dx,
					dy,
					0,
					KeysharpInputClient.MouseEventFlags.Move,
					extraInfo: extraInfo)
			]);
		}

		private void QueueRelativeMouseMove(int dx, int dy)
		{
			if (dx == 0 && dy == 0)
				return;

			PutMouseEventIntoArray((uint)MOUSEEVENTF.MOVE, 0, dx, dy);
		}


		internal override int MouseCoordToAbs(int coord, int widthOrHeight)
			=> widthOrHeight <= 0 ? 0 : ((65536 * coord) / widthOrHeight) + (coord < 0 ? -1 : 1);

		internal override ResultType LayoutHasAltGrDirect(nint layout) => ResultType.ConditionFalse;

		internal override void AttachTargetWindowThread(
			ref bool threadsAreAttached, ref uint keybdLayoutThread,
			ref WindowInfoBase tempitem, nint targetWindow) { }

		internal override void DetachTargetWindowThread(uint mainThread, uint targetThread) { }

		protected internal override void LongOperationUpdate() { }
		protected internal override void LongOperationUpdateForSendKeys() { }

		protected override void RegisterHook() { }

		private static KeysharpInputClient.KeyEventFlags NormalizeKeyFlags(
			uint vk,
			ref uint sc,
			KeysharpInputClient.KeyEventFlags flags)
		{
			if ((sc & 0x100) != 0)
				flags |= KeysharpInputClient.KeyEventFlags.ExtendedKey;

			if (vk == 0 && (sc & 0xFF) != 0)
				flags |= KeysharpInputClient.KeyEventFlags.ScanCode;

			sc &= 0xFF;
			return flags;
		}

		private static KeysharpInputClient.Input CreateMouseInput(
			uint eventFlags,
			uint data,
			int x,
			int y,
			ulong extraInfo)
			=> KeysharpInputClient.Input.MouseEvent(
				x == CoordUnspecified ? 0 : x,
				y == CoordUnspecified ? 0 : y,
				data,
				(KeysharpInputClient.MouseEventFlags)eventFlags,
				extraInfo: extraInfo);

		private void QueueInput(KeysharpInputClient.Input input)
			=> eventQueue.Add(QueuedInputEvent.FromInput(input));

		private void AddOrQueue(List<KeysharpInputClient.Input> events, KeysharpInputClient.Input input)
		{
			if (events != null)
				events.Add(input);
			else
				QueueInput(input);
		}

		private static void SendInputBatches(IReadOnlyList<KeysharpInputClient.Input> inputs, KeysharpInputClient.SynthFlags flags = KeysharpInputClient.SynthFlags.None)
		{
			if (inputs.Count == 0)
				return;

			if (inputs.Count <= MaxInputBatchSize)
			{
				KeysharpInputManager.SendInputViaSynthesisChannel(inputs, flags);
				return;
			}

			var offset = 0;

			while (offset < inputs.Count)
			{
				var count = Math.Min(MaxInputBatchSize, inputs.Count - offset);

				// Never end a batch on an unpaired UTF-16 high surrogate: its low half
				// would land in the next batch, where the daemon's per-batch
				// pending-high-surrogate reset drops the pair. Back off to exclude the
				// whole dangling group. (Only matters when more data follows.)
				if (offset + count < inputs.Count)
					count = TrimTrailingHighSurrogate(inputs, offset, count);

				var batch = new KeysharpInputClient.Input[count];

				for (var i = 0; i < count; i++)
					batch[i] = inputs[offset + i];

				KeysharpInputManager.SendInputViaSynthesisChannel(batch, flags);
				offset += count;
			}
		}

		// If [offset, offset+count) ends with a unicode keydown carrying an unpaired
		// high surrogate, return a shorter count that excludes that keydown (and its
		// keyup) so the surrogate pair stays within one batch. Mirrors the daemon's
		// pending_high_surrogate tracking.
		private static int TrimTrailingHighSurrogate(IReadOnlyList<KeysharpInputClient.Input> inputs, int offset, int count)
		{
			var pendingHighIdx = -1;

			for (var i = offset; i < offset + count; i++)
			{
				var input = inputs[i];

				if (input.Type != KeysharpInputClient.InputType.Keyboard)
					continue;

				var kflags = input.Keyboard.Flags;

				if ((kflags & KeysharpInputClient.KeyEventFlags.Unicode) == 0
					|| (kflags & KeysharpInputClient.KeyEventFlags.KeyUp) != 0)
					continue;

				var unit = input.Keyboard.Scan;
				pendingHighIdx = unit >= 0xD800 && unit <= 0xDBFF ? i : -1;
			}

			if (pendingHighIdx < 0)
				return count;

			var trimmed = pendingHighIdx - offset;
			return trimmed > 0 ? trimmed : count;
		}
	}
}
#endif
