#if LINUX
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using SharpHook;
using SharpHook.Native;
using SharpHook.Data;
using Keysharp.Core;
using static Keysharp.Core.Linux.SharpHookKeyMapper;
using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using Keysharp.Core.Common.Mouse;
using SharpHook.Testing;

namespace Keysharp.Core.Linux
{
	/// <summary>
	/// Concrete implementation of KeyboardMouseSender for the linux platform.
	/// </summary>
	internal partial class LinuxKeyboardMouseSender : Common.Keyboard.KeyboardMouseSender
	{
		[Conditional("DEBUG")]
		private static void DebugLog(string message) => Console.WriteLine(message);

		internal IEventSimulator sim => backend.sim;
		private readonly SharpHookKeySimulationBackend backend = new();

		// Prefer Right-Alt as AltGr on Linux (prevents menu activation better than neutral Alt).
		private const uint VK_ALTGR = VK_RMENU;

		private sealed class InputArrayState
		{
			internal readonly List<ArrayEvent> Events;
			internal int Count;
			internal readonly uint PrevEventModifiers;

			internal InputArrayState(List<ArrayEvent> events, uint prevEventModifiers)
			{
				Events = events;
				PrevEventModifiers = prevEventModifiers;
			}
		}

		private readonly Lock inputGate = new();
		private readonly Stack<InputArrayState> inputStack = new();

		internal LinuxKeyboardMouseSender()
		{
		}

		internal override bool MouseButtonsSwapped
		{
			get
			{
				if (!PlatformManager.IsX11Available)
					return false;

				// X11 supports up to 256 buttons, but 3 is enough for swap detection
				byte[] map = new byte[3];
				int count = Xlib.XGetPointerMapping(XDisplay.Default.Handle, map, map.Length);

				if (count < 3)
					return false;

				// If physical button 1 does not map to logical button 1,
				// the primary button is swapped.
				return map[0] != 1;
			}
		}

		#region Input array recording

		private enum ArrayEventType : byte
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

		private readonly struct ArrayEvent
		{
			internal readonly ArrayEventType Type;

			// Key
			internal readonly uint Vk;

			// Delay
			internal readonly int DelayMs;

			// Text
			internal readonly string? Text;

			// Mouse (x/y may be CoordUnspecified)
			internal readonly int X;
			internal readonly int Y;
			internal readonly MouseButton Button;
			internal readonly short WheelDelta;

			private ArrayEvent(ArrayEventType type, uint vk, int delayMs, string? text,
							int x, int y, MouseButton button, short wheelDelta)
			{
				Type = type;
				Vk = vk;
				DelayMs = delayMs;
				Text = text;
				X = x;
				Y = y;
				Button = button;
				WheelDelta = wheelDelta;
			}

			internal static ArrayEvent Key(ArrayEventType type, uint vk)
				=> new(type, vk, 0, null, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent Delay(int ms)
				=> new(ArrayEventType.DelayMs, 0, ms, null, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent TextEvent(string text)
				=> new(ArrayEventType.Text, 0, 0, text, 0, 0, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveAbs(int x, int y)
				=> new(ArrayEventType.MouseMoveAbs, 0, 0, null, x, y, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseMoveRel(int dx, int dy)
				=> new(ArrayEventType.MouseMoveRel, 0, 0, null, dx, dy, MouseButton.NoButton, 0);

			internal static ArrayEvent MouseButtonEvent(ArrayEventType type, MouseButton button, int x, int y)
				=> new(type, 0, 0, null, x, y, button, 0);

			internal static ArrayEvent MouseWheelV(short delta)
				=> new(ArrayEventType.MouseWheelV, 0, 0, null, 0, 0, MouseButton.NoButton, delta);

			internal static ArrayEvent MouseWheelH(short delta)
				=> new(ArrayEventType.MouseWheelH, 0, 0, null, 0, 0, MouseButton.NoButton, delta);
		}

		private void AddArrayEvent(in ArrayEvent ev)
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
			lock (inputGate)
			{
				// Save previous modifier prediction so nested sends restore correctly.
				var prev = eventModifiersLR;
				eventModifiersLR = modifiersLR;

				var cap = maxEvents > 0 ? Math.Min(maxEvents, 2048) : 512;
				inputStack.Push(new InputArrayState(new List<ArrayEvent>(cap), prev));
			}
		}

		internal override void CleanupEventArray(long finalKeyDelay)
		{
			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return;

				var st = inputStack.Pop();
				eventModifiersLR = st.PrevEventModifiers;
			}
		}

		internal override int SiEventCount()
		{
			lock (inputGate)
				return inputStack.Count > 0 ? inputStack.Peek().Count : 0;
		}

		internal override void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y)
		{
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
		internal override void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo)
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
			var keyCode = SharpHookKeyMapper.VkToKeyCode(vk);
			if (keyCode == KeyCode.VcUndefined)
				return;

			AddArrayEvent(ArrayEvent.Key(isKeyUp ? ArrayEventType.KeyUp : ArrayEventType.KeyDown, vk));
		}

		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			InputArrayState st;

			lock (inputGate)
			{
				if (inputStack.Count == 0)
					return;

				st = inputStack.Pop();
				// restore previous modifier prediction now that we’ve detached this send
				//eventModifiersLR = st.PrevEventModifiers;
			}

			if (st.Events.Count == 0)
				return;

			var lht = Script.TheScript.HookThread as LinuxHookThread;
			if (lht == null)
				return;

			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			var ms = DateTime.UtcNow;

			WithSendScope(lht, () =>
			{
				WithSendUngrab(lht, () =>
				{
					List<LinuxHookThread.ReleasedKey>? released = null;
					try
					{
						var vksToRelease = CollectVksWeWillPress(st.Events); // only keys we will press down
						if (vksToRelease.Count != 0)
							released = lht.ForceReleaseKeysForSend(vksToRelease);

						var logicalMods = GetModifierLRState();
						// Send up-strokes for modifiers which aren't pressed down, because some key grabs otherwise
						// block the modifiers from being sent. Otherwise this doesn't generate X11 events anyway.
						var modsToPreRelease = CollectModifierUpsForArray(st.Events, logicalMods, lht);
						if (modsToPreRelease.Count != 0)
						{
							foreach (var vk in modsToPreRelease)
								backend.KeyUp(vk, ms, extraInfo);
						}

						// Text replay helper (no delays in array replay; delays are explicit DelayMs events).
						var textBatch = new StringBuilder();

						void FlushText()
						{
							if (textBatch.Length == 0) return;

							EmitTextInjectedWithControls(textBatch.ToString(), extraInfo);
							textBatch.Clear();
							ms = DateTime.Now;
						}

						void EmitTextInjectedWithControls(string s, long extraInfo)
						{
							if (string.IsNullOrEmpty(s))
								return;

							var chunk = new StringBuilder();
							bool lastWasCR = false;
							var ms = DateTime.Now;

							void FlushChunk()
							{
								if (chunk.Length == 0) return;
								sim.SimulateTextEntry(chunk.ToString());
								chunk.Clear();
								ms = DateTime.Now;
							}

							foreach (var ch in s)
							{
								switch (ch)
								{
									case '\r':
										FlushChunk();
										backend.KeyStroke(VK_RETURN, ms, extraInfo);
										lastWasCR = true;
										break;

									case '\n':
										if (lastWasCR) { lastWasCR = false; break; }
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

						foreach (var ev in st.Events)
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
										Flow.SleepWithoutInterruption(ev.DelayMs);
									ms = DateTime.Now;
									break;

								case ArrayEventType.KeyDown:
									backend.KeyDown(ev.Vk, ms, extraInfo);
									if (KeyboardUtils.IsModifierVk(ev.Vk) && IsX11Available)
									{
										XDisplay.Default.XSync(false);
										Flow.SleepWithoutInterruption(1);
										DebugLog($"[SendArray] ModSync vk={ev.Vk}");
									}
									break;

								case ArrayEventType.KeyUp:
									backend.KeyUp(ev.Vk, ms, extraInfo);
									break;

								case ArrayEventType.MouseMoveRel:
									sim.SimulateMouseMovementRelative((short)ev.X, (short)ev.Y);
									break;

								case ArrayEventType.MouseMoveAbs:
								{
									int mx = ev.X, my = ev.Y;
									EnsureCoords(ref mx, ref my);
									sim.SimulateMouseMovement((short)mx, (short)my);
									break;
								}

								case ArrayEventType.MousePress:
								{
									int mx = ev.X, my = ev.Y;
									EnsureCoords(ref mx, ref my);
									sim.SimulateMousePress((short)mx, (short)my, ev.Button);
									break;
								}

								case ArrayEventType.MouseRelease:
								{
									int mx = ev.X, my = ev.Y;
									EnsureCoords(ref mx, ref my);
									sim.SimulateMouseRelease((short)mx, (short)my, ev.Button);
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
					finally
					{
						if (released != null)
							lht.RestoreNonModifierKeysAfterSend(released);
					}
				});
			});

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

		private static HashSet<uint> CollectVksWeWillPress(List<ArrayEvent> events)
		{
			var set = new HashSet<uint>();

			foreach (var ev in events)
			{
				switch (ev.Type)
				{
					case ArrayEventType.KeyDown:
						// only matters if we will press it down; KeyUp-only doesn’t need a pre-release.
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

		private static HashSet<uint> CollectModifierUpsForArray(List<ArrayEvent> events, uint logicalMods, LinuxHookThread lht)
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

				if ((logicalMods & modMask) == 0)
					set.Add(ev.Vk);
			}

			return set;
		}

		#region Mouse/Key immediate ops (Event mode) — keep behavior, but route Input mode through Put*IntoArray

		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			// If we're building an array, record.
			if (sendMode == SendModes.Input)
			{
				PutMouseEventIntoArray(eventFlags, data, x, y);
				return;
			}

			// Otherwise emit immediately.
			var button = VkToMouseButton(eventFlags & 0xFFFF);
			var type = (KeyEventTypes)(eventFlags >> 16);

			var finalX = x;
			var finalY = y;

			if (finalX == CoordUnspecified || finalY == CoordUnspecified)
			{
				_ = GetCursorPos(out POINT pos);
				finalX = pos.X;
				finalY = pos.Y;
			}

				switch (type)
				{
					case KeyEventTypes.KeyDown:
						sim.SimulateMousePress((short)finalX, (short)finalY, button);
						break;
					case KeyEventTypes.KeyUp:
						sim.SimulateMouseRelease((short)finalX, (short)finalY, button);
						break;
					case KeyEventTypes.KeyDownAndUp:
						sim.SimulateMousePress((short)finalX, (short)finalY, button);
						sim.SimulateMouseRelease((short)finalX, (short)finalY, button);
						break;
				}

			DoMouseDelay();
		}

		internal override void MouseMove(ref int x, ref int y, ref uint eventFlags, long speed, bool moveOffset)
		{
			if (sendMode == SendModes.Input)
			{
				if (moveOffset) AddArrayEvent(ArrayEvent.MouseMoveRel(x, y));
				else           AddArrayEvent(ArrayEvent.MouseMoveAbs(x, y));
				return;
			}


				if (moveOffset)
					sim.SimulateMouseMovementRelative((short)x, (short)y);
				else
					sim.SimulateMouseMovement((short)x, (short)y);

			DoMouseDelay();
		}

		internal override void MouseClick(uint vk, int x, int y, long repeatCount, long speed, KeyEventTypes eventType, bool moveOffset)
		{
			for (var i = 0; i < repeatCount; i++)
			{
				MouseEvent(((uint)eventType << 16) | vk, 0, x, y);
				DoMouseDelay();
			}
		}

		internal override void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint eventFlags, long extraInfo)
		{
			var lht = Script.TheScript.HookThread as LinuxHookThread;
			if (lht == null)
				return;

			WithSendScope(lht, () =>
			{
				lock (lht.hkLock)
				{
					var xcode = lht.TemporarilyUngrabKey(vk);
					List<LinuxHookThread.ReleasedKey> released = null;
					try 
					{
						// Event mode: immediate send via backend (backend registers synthetic right before simulating).
						if ((eventType == KeyEventTypes.KeyDown) || (eventType == KeyEventTypes.KeyDownAndUp))
						{
							released = lht.ForceReleaseKeysForSend(new HashSet<uint>() {vk});
							backend.KeyDown(vk, DateTime.UtcNow, extraInfo);
						}
						if ((eventType == KeyEventTypes.KeyUp) || (eventType == KeyEventTypes.KeyDownAndUp))
						{
							backend.KeyUp(vk, DateTime.UtcNow, extraInfo);
						}
						if (released != null) lht.RestoreNonModifierKeysAfterSend(released);
					} 
					finally
					{
						lht.RegrabKey(xcode);
					}
				}
			});
		}

		internal override void SendUnicodeChar(char ch, uint modifiers)
		{
			var extraInfo = KeyIgnoreLevel(ThreadAccessors.A_SendLevel);
			SetModifierLRState(modifiers, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, extraInfo);

			// In Input mode, record as text so it can be interspersed correctly.
			if (sendMode == SendModes.Input)
			{
				AddArrayEvent(ArrayEvent.TextEvent(ch.ToString()));
				return;
			}

			var lht = Script.TheScript.HookThread as LinuxHookThread;
			if (lht == null)
				return;

			WithSendScope(lht, () =>
			{
				WithSendUngrab(lht, () =>
				{
					// Prefer keystroke mapping when possible.
					if (System.Text.Rune.TryCreate(ch, out var rune) &&
						LinuxCharMapper.TryMapRuneToKeystroke(rune, out var vk, out var needShift, out var needAltGr) &&
						vk != 0)
					{
						var ms = DateTime.UtcNow;
						if (needAltGr) backend.KeyDown(VK_ALTGR, ms, extraInfo);
						if (needShift) backend.KeyDown(VK_SHIFT, ms, extraInfo);

						backend.KeyStroke(vk, ms, extraInfo);

						if (needShift) backend.KeyUp(VK_SHIFT, ms, extraInfo);
						if (needAltGr) backend.KeyUp(VK_ALTGR, ms, extraInfo);
					}
					else
					{
						sim.SimulateTextEntry(ch.ToString());
					}
				});
			});
		}

		private static void WithSendScope(LinuxHookThread lht, Action action)
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

		private static void WithSendUngrab(LinuxHookThread lht, Action action)
		{
			lock (lht.hkLock)
			{
				var snap = lht.BeginSendUngrab();
				try
				{
					action();
				}
				finally
				{
					lht.EndSendUngrab(snap);
				}
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

		#endregion

		internal override int PbEventCount() => 0;

		internal override nint GetFocusedKeybdLayout(nint window) => 0;

		internal override ResultType LayoutHasAltGrDirect(nint layout) => ResultType.ConditionFalse;

		internal override ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue)
		{
			var script = Script.TheScript;
			// Can't use IsKeyDownAsync/GetAsyncKeyState() because it doesn't have this info:
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
				if (script.HookThread.IsKeyDown(vk))
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

		internal override void SetModifierLRState(uint modifiersLRnew, uint modifiersLRnow, nint targetWindow
			, bool disguiseDownWinAlt, bool disguiseUpWinAlt, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			base.SetModifierLRState(modifiersLRnew, modifiersLRnow, targetWindow,
				disguiseDownWinAlt, disguiseUpWinAlt, extraInfo);
		}

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
			private readonly LinuxKeyboardMouseSender self;
			private readonly long keyDelay;
			private readonly long keyDuration;

			internal DirectKeySink(LinuxKeyboardMouseSender self, long keyDelay, long keyDuration)
			{
				this.self = self;
				this.keyDelay = keyDelay;
				this.keyDuration = keyDuration;
			}

			public void Down(uint vk, DateTime ms, long extraInfo)
			{
				self.backend.KeyDown(vk, ms, extraInfo);
				if (keyDuration >= 0) Flow.SleepWithoutInterruption(keyDuration);
			}

			public void Up(uint vk, DateTime ms, long extraInfo)
			{
				self.backend.KeyUp(vk, ms, extraInfo);
				if (keyDelay >= 0) Flow.SleepWithoutInterruption(keyDelay);
			}

			public void Stroke(uint vk, DateTime ms, long extraInfo)
			{
				Down(vk, ms, extraInfo);
				Up(vk, ms, extraInfo);
			}

			public void Flush() { /* no-op */ }
		}

		#endregion

		#region SharpHookKeySimulationBackend

		internal sealed class SharpHookKeySimulationBackend
		{
			internal readonly IEventSimulator sim;

			public SharpHookKeySimulationBackend(IEventSimulator? sim = null)
				=> this.sim = sim ?? new EventSimulator();

			private static void RegisterSynthetic(KeyCode code, bool keyUp, DateTime ms, long extraInfo)
			{
				if (code == KeyCode.VcUndefined)
					return;

				if (Script.TheScript.HookThread is LinuxHookThread lht)
					lht.RegisterSyntheticEvent(code, keyUp, ms, extraInfo);
			}

			public void KeyDown(uint vk, DateTime ms, long extraInfo)
			{
				var code = SharpHookKeyMapper.VkToKeyCode(vk);
				if (code == KeyCode.VcUndefined)
					return;

				RegisterSynthetic(code, false, ms, extraInfo);
				DebugLog($"[SendEmit] KeyDown vk={vk} code={code}");
				sim.SimulateKeyPress(code);
			}

			public void KeyUp(uint vk, DateTime ms, long extraInfo)
			{
				var code = SharpHookKeyMapper.VkToKeyCode(vk);
				if (code == KeyCode.VcUndefined)
					return;

				RegisterSynthetic(code, true, ms, extraInfo);
				DebugLog($"[SendEmit] KeyUp vk={vk} code={code}");
				sim.SimulateKeyRelease(code);
			}

			public void KeyStroke(uint vk, DateTime ms, long extraInfo)
			{
				KeyDown(vk, ms, extraInfo);
				KeyUp(vk, ms, extraInfo);
			}

			public IKeySimulationSequence BeginSequence()
				=> new SharpHookKeySequence(this);
		}

		internal sealed class SharpHookKeySequence : IKeySimulationSequence
		{
			private enum ActionType { Down, Up }

			private readonly SharpHookKeySimulationBackend backend;
			private readonly List<(ActionType Type, uint Vk)> actions = new();
			private bool committed;

			public SharpHookKeySequence(SharpHookKeySimulationBackend backend)
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
					DebugLog("[SendEmit] Seq Commit");
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
