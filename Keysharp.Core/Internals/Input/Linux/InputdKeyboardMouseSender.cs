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
	/// Keyboard/mouse sender that routes input through keysharp-inputd.
	/// </summary>
	internal sealed class InputdKeyboardMouseSender : KeyboardMouseSender
	{
		// KEYEVENTF_* constants equal KSI_KEYEVENTF_* by design.
		private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
		private const uint KEYEVENTF_KEYUP = 0x0002;
		private const uint KEYEVENTF_UNICODE = 0x0004;
		private const uint KEYEVENTF_SCANCODE = 0x0008;
		private const int MaxInputdBatchSize = KeysharpInputdClient.MaxInputsPerRequest;
		private const int MaxMouseMoveChunk = 1000;

		private readonly List<InputdQueuedEvent> eventQueue = new(MaxInitialEventsSI);
		private static readonly (uint Flag, uint Button, bool Down)[] GnomeButtonRoutes =
		[
			((uint)MOUSEEVENTF.LEFTDOWN, 1u, true),
			((uint)MOUSEEVENTF.LEFTUP, 1u, false),
			((uint)MOUSEEVENTF.RIGHTDOWN, 3u, true),
			((uint)MOUSEEVENTF.RIGHTUP, 3u, false),
			((uint)MOUSEEVENTF.MIDDLEDOWN, 2u, true),
			((uint)MOUSEEVENTF.MIDDLEUP, 2u, false),
		];

		private readonly struct InputdQueuedEvent
		{
			internal readonly KeysharpInputdClient.Input Input;
			internal readonly int DelayMs;
			internal readonly bool IsDelay;

			private InputdQueuedEvent(KeysharpInputdClient.Input input, int delayMs, bool isDelay)
			{
				Input = input;
				DelayMs = delayMs;
				IsDelay = isDelay;
			}

			internal static InputdQueuedEvent FromInput(KeysharpInputdClient.Input input) => new(input, 0, false);
			internal static InputdQueuedEvent Delay(int delayMs) => new(default, delayMs, true);
		}

		internal override bool MouseButtonsSwapped => false;

		protected internal override bool TrySendPlatformRawText(ReadOnlySpan<char> text, ref int keyIndex, uint modifiersLR)
		{
			if (keyIndex < 0 || keyIndex >= text.Length)
				return false;

			var localEvents = sendMode == SendModes.Event ? new List<KeysharpInputdClient.Input>() : null;
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

		// No journal-playback hook via uinput/inputd; SendPlay is sent as SendEvent (see WarnIfPlayUnsupported).
		protected override bool SupportsPlayMode => false;

		internal override void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo, bool autoRepeat = false)
		{
			if (vk == 0 && sc == 0 && eventFlags == 0)
			{
				eventQueue.Add(InputdQueuedEvent.Delay((int)extraInfo));
				return;
			}

			var flags = (KeysharpInputdClient.KeyEventFlags)eventFlags;
			var isKeyUp = (eventFlags & KEYEVENTF_KEYUP) != 0;
			var isUnicode = (eventFlags & KEYEVENTF_UNICODE) != 0;

			if (isKeyUp)
				eventModifiersLR &= ~keyAsModifiersLR;
			else
				eventModifiersLR |= keyAsModifiersLR;

			if (!isUnicode)
				flags = NormalizeKeyFlags(vk, ref sc, flags);

			QueueInput(KeysharpInputdClient.Input.Key(
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
				var batch = new List<KeysharpInputdClient.Input>(Math.Min(eventQueue.Count, MaxInputdBatchSize));

				for (var i = 0; i < eventQueue.Count; i++)
				{
					var ev = eventQueue[i];

					if (!ev.IsDelay)
					{
						batch.Add(ev.Input);
						continue;
					}

					SendInputBatches(batch, KeysharpInputdClient.SynthFlags.BypassHook);
					batch.Clear();

					if (i == eventQueue.Count - 1)
						finalKeyDelay = ev.DelayMs;
					else if (ev.DelayMs > 0)
						Keysharp.Internals.Flow.SleepWithoutInterruption(ev.DelayMs);
				}

				SendInputBatches(batch, KeysharpInputdClient.SynthFlags.BypassHook);
			}
			finally
			{
				eventQueue.Clear();
			}

			ReconcileLogicalModifiersFromOs();
		}

		/// <summary>Reconciles logical modifiers after bypass-hook SendInput.</summary>
		private static void ReconcileLogicalModifiersFromOs()
		{
			var ht = Script.TheScript.HookThread;
			var sender = ht.kbdMsSender;

			if (sender == null)
				return;

			if (KeysharpInputdManager.TryGetKeyState(out var logicalMods, out _, out _, out _))
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

		/// <summary>Uses the inputd-backed live toggle state after sending a lock key.</summary>
		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			var script = Script.TheScript;

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
			var keyFlags = NormalizeKeyFlags(vk, ref sc, (KeysharpInputdClient.KeyEventFlags)flags);

			if (eventType == KeyEventTypes.KeyDownAndUp)
			{
				SendInputBatches(
				[
					KeysharpInputdClient.Input.Key((ushort)vk, (ushort)sc, keyFlags, extraInfo: (ulong)extraInfo),
					KeysharpInputdClient.Input.Key((ushort)vk, (ushort)sc, keyFlags | (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_KEYUP, extraInfo: (ulong)extraInfo),
				]);
				return;
			}

			if (eventType == KeyEventTypes.KeyUp)
				keyFlags |= (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_KEYUP;

			SendInputBatches(
			[
				KeysharpInputdClient.Input.Key((ushort)vk, (ushort)sc, keyFlags, extraInfo: (ulong)extraInfo),
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
			// A surrogate half is never ASCII, so both units always take the daemon
			// unicode path. Emit them in ONE batch so the pair reconstructs (the
			// daemon resets its pending high surrogate at every batch boundary).
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

			var inputs = new KeysharpInputdClient.Input[units.Length * 2];

			for (var i = 0; i < units.Length; i++)
			{
				inputs[i * 2] = KeysharpInputdClient.Input.Key(0, units[i], (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_UNICODE, extraInfo: (ulong)extraInfo);
				inputs[i * 2 + 1] = KeysharpInputdClient.Input.Key(0, units[i], (KeysharpInputdClient.KeyEventFlags)(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP), extraInfo: (ulong)extraInfo);
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

		private bool QueueRawTextItem(List<KeysharpInputdClient.Input> events, ReadOnlySpan<char> text, ref int index, uint modifiersLR, long extraInfo)
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

		private static void FlushRawTextEvents(List<KeysharpInputdClient.Input> events)
		{
			if (events == null || events.Count == 0)
				return;

			SendInputBatches(events);
			events.Clear();
		}

		private void QueueTextRune(List<KeysharpInputdClient.Input> events, Rune rune, uint modifiersLR, long extraInfo)
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

		private void QueueUnicodeUnit(List<KeysharpInputdClient.Input> events, char ch, long extraInfo)
		{
			AddOrQueue(events, KeysharpInputdClient.Input.Key(
				0,
				ch,
				(KeysharpInputdClient.KeyEventFlags)KEYEVENTF_UNICODE,
				extraInfo: (ulong)extraInfo));
			AddOrQueue(events, KeysharpInputdClient.Input.Key(
				0,
				ch,
				(KeysharpInputdClient.KeyEventFlags)(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
				extraInfo: (ulong)extraInfo));
		}

		private void QueueModifierTransition(List<KeysharpInputdClient.Input> events, uint modifiers, bool down, long extraInfo)
		{
			if ((modifiers & MOD_LSHIFT) != 0)
				QueueKey(events, down ? KeyEventTypes.KeyDown : KeyEventTypes.KeyUp, VK_LSHIFT, extraInfo);

			if ((modifiers & MOD_RALT) != 0)
				QueueKey(events, down ? KeyEventTypes.KeyDown : KeyEventTypes.KeyUp, VK_RMENU, extraInfo);
		}

		private void QueueKey(List<KeysharpInputdClient.Input> events, KeyEventTypes eventType, uint vk, long extraInfo)
		{
			var downFlags = (KeysharpInputdClient.KeyEventFlags)0;
			var upFlags = (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_KEYUP;

			if (eventType != KeyEventTypes.KeyUp)
				AddOrQueue(events, KeysharpInputdClient.Input.Key((ushort)vk, 0, downFlags, extraInfo: (ulong)extraInfo));

			if (eventType != KeyEventTypes.KeyDown)
				AddOrQueue(events, KeysharpInputdClient.Input.Key((ushort)vk, 0, upFlags, extraInfo: (ulong)extraInfo));
		}

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			// Prefer compositor injection for button/scroll events when available.
			if ((eventFlags & (uint)MOUSEEVENTF.MOVE) == 0)
			{
				var gnomeMouse = WaylandMouseInjection.Backend();

				if (gnomeMouse != null && TryRouteToGnome(gnomeMouse, eventFlags, data))
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
					SendRelativeMouseMove(x, y, (ulong)KeyIgnoreLevel(ThreadAccessors.A_SendLevel));
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

			// Prefer compositor injection for absolute moves; uinput absolute mapping is the fallback. A backend
			// exists whenever the compositor is one Keysharp can drive, but the interface behind it may still be
			// missing (KWin dropped its FakeInput D-Bus object), so the fallback is chosen on the injection
			// FAILING rather than on the backend's absence - otherwise the move is silently dropped.
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

						for (var step = 1; step <= steps; step++)
						{
							var nextX = current.X + ((targetX - current.X) * step / steps);
							var nextY = current.Y + ((targetY - current.Y) * step / steps);

							//A step that lands on the previous pixel is not injected, so it must not decide the
							//outcome: only a step that was actually attempted can say whether injection works.
							if (nextX != previousX || nextY != previousY)
							{
								if (!compositorMouse.TrySendMouseMoveAbsolute(nextX, nextY))
									break;

								injected = true;
							}

							previousX = nextX;
							previousY = nextY;
							DoMouseDelay();
						}

						//Nothing moved because every step matched the cursor's current position: already there.
						if (previousX == targetX && previousY == targetY)
							injected = true;
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


		private static bool TryRouteToGnome(
			Keysharp.Internals.Window.Linux.Wayland.IWaylandBackend gnomeMouse,
			uint flags, uint data)
		{
			foreach (var (flag, button, down) in GnomeButtonRoutes)
				if ((flags & flag) != 0)
					return gnomeMouse.TrySendMouseButton(button, down);

			if ((flags & (uint)MOUSEEVENTF.XDOWN) != 0)
				return gnomeMouse.TrySendMouseButton((data & 0x0001u) != 0 ? 8u : 9u, true);
			if ((flags & (uint)MOUSEEVENTF.XUP) != 0)
				return gnomeMouse.TrySendMouseButton((data & 0x0001u) != 0 ? 8u : 9u, false);
			if ((flags & (uint)MOUSEEVENTF.WHEEL) != 0)
				return gnomeMouse.TrySendMouseScroll(unchecked((short)(ushort)(data & 0xFFFF)), true);
			if ((flags & (uint)MOUSEEVENTF.HWHEEL) != 0)
				return gnomeMouse.TrySendMouseScroll(unchecked((short)(ushort)(data & 0xFFFF)), false);
			return false;
		}

		private static void SendRelativeMouseMove(int dx, int dy, ulong extraInfo)
		{
			if (dx == 0 && dy == 0)
				return;

			var stepCount = Math.Max(Math.Abs(dx), Math.Abs(dy));
			var events = new List<KeysharpInputdClient.Input>(Math.Min(stepCount, MaxMouseMoveChunk));
			var previousX = 0;
			var previousY = 0;

			for (var step = 1; step <= stepCount; step++)
			{
				var nextX = dx * step / stepCount;
				var nextY = dy * step / stepCount;
				var stepDx = nextX - previousX;
				var stepDy = nextY - previousY;

				if (stepDx != 0 || stepDy != 0)
				{
					events.Add(KeysharpInputdClient.Input.MouseEvent(
						stepDx,
						stepDy,
						0,
						KeysharpInputdClient.MouseEventFlags.Move,
						extraInfo: extraInfo));
				}

				if (events.Count == MaxMouseMoveChunk)
				{
					KeysharpInputdManager.SendInputViaSynthesisChannel(events);
					events.Clear();
				}

				previousX = nextX;
				previousY = nextY;
			}

			if (events.Count != 0)
				KeysharpInputdManager.SendInputViaSynthesisChannel(events);
		}

		private void QueueRelativeMouseMove(int dx, int dy)
		{
			if (dx == 0 && dy == 0)
				return;

			var stepCount = Math.Max(Math.Abs(dx), Math.Abs(dy));
			var previousX = 0;
			var previousY = 0;

			for (var step = 1; step <= stepCount; step++)
			{
				var nextX = dx * step / stepCount;
				var nextY = dy * step / stepCount;
				var stepDx = nextX - previousX;
				var stepDy = nextY - previousY;

				if (stepDx != 0 || stepDy != 0)
					PutMouseEventIntoArray((uint)MOUSEEVENTF.MOVE, 0, stepDx, stepDy);

				previousX = nextX;
				previousY = nextY;
			}
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

		private static KeysharpInputdClient.KeyEventFlags NormalizeKeyFlags(
			uint vk,
			ref uint sc,
			KeysharpInputdClient.KeyEventFlags flags)
		{
			if ((sc & 0x100) != 0)
				flags |= (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_EXTENDEDKEY;

			if (vk == 0 && (sc & 0xFF) != 0)
				flags |= (KeysharpInputdClient.KeyEventFlags)KEYEVENTF_SCANCODE;

			sc &= 0xFF;
			return flags;
		}

		private static KeysharpInputdClient.Input CreateMouseInput(
			uint eventFlags,
			uint data,
			int x,
			int y,
			ulong extraInfo)
			=> KeysharpInputdClient.Input.MouseEvent(
				x == CoordUnspecified ? 0 : x,
				y == CoordUnspecified ? 0 : y,
				data,
				(KeysharpInputdClient.MouseEventFlags)eventFlags,
				extraInfo: extraInfo);

		private void QueueInput(KeysharpInputdClient.Input input)
			=> eventQueue.Add(InputdQueuedEvent.FromInput(input));

		private void AddOrQueue(List<KeysharpInputdClient.Input> events, KeysharpInputdClient.Input input)
		{
			if (events != null)
				events.Add(input);
			else
				QueueInput(input);
		}

		private static void SendInputBatches(IReadOnlyList<KeysharpInputdClient.Input> inputs, KeysharpInputdClient.SynthFlags flags = KeysharpInputdClient.SynthFlags.None)
		{
			if (inputs.Count == 0)
				return;

			if (inputs.Count <= MaxInputdBatchSize)
			{
				KeysharpInputdManager.SendInputViaSynthesisChannel(inputs, flags);
				return;
			}

			var offset = 0;

			while (offset < inputs.Count)
			{
				var count = Math.Min(MaxInputdBatchSize, inputs.Count - offset);

				// Never end a batch on an unpaired UTF-16 high surrogate: its low half
				// would land in the next batch, where the daemon's per-batch
				// pending-high-surrogate reset drops the pair. Back off to exclude the
				// whole dangling group. (Only matters when more data follows.)
				if (offset + count < inputs.Count)
					count = TrimTrailingHighSurrogate(inputs, offset, count);

				var batch = new KeysharpInputdClient.Input[count];

				for (var i = 0; i < count; i++)
					batch[i] = inputs[offset + i];

				KeysharpInputdManager.SendInputViaSynthesisChannel(batch, flags);
				offset += count;
			}
		}

		// If [offset, offset+count) ends with a unicode keydown carrying an unpaired
		// high surrogate, return a shorter count that excludes that keydown (and its
		// keyup) so the surrogate pair stays within one batch. Mirrors the daemon's
		// pending_high_surrogate tracking.
		private static int TrimTrailingHighSurrogate(IReadOnlyList<KeysharpInputdClient.Input> inputs, int offset, int count)
		{
			var pendingHighIdx = -1;

			for (var i = offset; i < offset + count; i++)
			{
				var input = inputs[i];

				if (input.Type != KeysharpInputdClient.InputType.Keyboard)
					continue;

				var kflags = input.Keyboard.Flags;

				if ((kflags & KeysharpInputdClient.KeyEventFlags.Unicode) == 0
					|| (kflags & KeysharpInputdClient.KeyEventFlags.KeyUp) != 0)
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
