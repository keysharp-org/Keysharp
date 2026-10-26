using Keysharp.Builtins;
using Keysharp.Components.Input;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.ScanCodes;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using static Keysharp.Internals.Os.Windows.WindowsAPI;
using DeviceInfo = Keysharp.Internals.InputDeviceInfo;
#if WINDOWS
using NativeHookThread = Keysharp.Internals.Input.Hooks.Windows.WindowsHookThread;
#elif LINUX
using NativeHookThread = Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread;
#else
using NativeHookThread = Keysharp.Internals.Input.Hooks.MacOS.MacHookThread;
#endif

namespace Keysharp.Internals.Input.Hooks
{
	internal class ProviderHookThread(Script owner, string mutexName, IHookProvider replacement)
		: NativeHookThread(owner, mutexName), IHookHost, IDisposable
	{
		private readonly ProviderLease<IHookProvider> capture = new(replacement, "hook provider");
		private IHookProvider provider => capture.Provider;
		private readonly object controlGate = new();
		private readonly Dictionary<(long Id, bool Mouse), DeviceState> devices = [];
		private readonly Dictionary<(long Id, bool Mouse), string> identities = [];
		private object source = new();
		private Frame current, reusable;
		private int panicKeys, lost, generation, resetPending;
#if WINDOWS
		private bool altGrControl;
		private int forwardingHolds;
#endif
		private string failure = "";

		internal override bool UsesHookProvider => !capture.IsRetired;
		protected override bool CaptureBelowOsKeyState => current != null;
#if WINDOWS
		protected virtual void AddRemoveNativeHooks(HookType kinds, bool temporary) => base.AddRemoveHooks(kinds, temporary);
		protected virtual void QueueOnHookThread(Action action) => PostHookCallback(action);
#else
		private bool IsOnHookThread => false;
		internal override object Invoke(Func<object> action) { lock (hookStateLock) return action(); }
		protected virtual void QueueOnHookThread(Action action) { lock (hookStateLock) action(); }
		protected virtual void AddRemoveNativeHooks(HookType kinds, bool temporary)
		{
			if (!UsesHookProvider) { base.AddRemoveHooks(kinds, temporary); return; }
#if OSX
			if ((kinds & HookType.Keyboard) != 0 && !HasKbdHook() && !Script.IsUiInitializationBlocked)
				KeyCodes.PrepareForInputHook(script);
#endif
#if LINUX
			TrackForegroundWindow((kinds & HookType.Keyboard) != 0);
#endif
		}
		protected override void EnsureCursorClipPermissions()
			=> _ = script.Permissions.EnsureCapabilities(inputMonitoring: !UsesHookProvider, inputControl: true, operation: "ClipCursor");
		internal override void RefreshPlatformKeyGrabs()
		{
			if (!UsesHookProvider) base.RefreshPlatformKeyGrabs();
		}
#endif
		internal override string GetHookActivationFailureReason() => UsesHookProvider ? failure : base.GetHookActivationFailureReason();
		internal int DispatchTimeoutMilliseconds { get; set; } = HotIfCallbackBudgetMilliseconds + 500;

		internal override void AddRemoveHooks(HookType kinds, bool changeIsTemporary = false)
		{
			if (changeIsTemporary || !UsesHookProvider)
			{
				AddRemoveNativeHooks(kinds, changeIsTemporary);
#if WINDOWS
				if (Volatile.Read(ref lost) != 0)
				{
					activeHooks = GetNativeHooks();
					SyncHookMutexes(false);
				}
#endif
				return;
			}

			Start();
			_ = Invoke(() =>
			{
				lock (controlGate)
				{
					if (!TryEnter()) return null;
					try
					{
						var previous = GetActiveHooks();
						if (kinds == previous && Volatile.Read(ref lost) == 0) return null;
						var wasLost = Volatile.Read(ref lost);
						var previousGeneration = generation;
#if LINUX
						// Opening the desktop keymap subscription can wait for a snapshot, outside an event's deadline.
						if ((kinds & HookType.Keyboard) != 0) _ = script.LinuxServices.KeyboardState.Get();
#endif
						AddRemoveNativeHooks(kinds, false);
#if WINDOWS
						if (GetNativeHooks() != kinds)
						{
							RestoreNativeHooks(previous);
							return null;
						}
#endif
						var changed = previous ^ kinds;
						Volatile.Write(ref lost, 0);
						activeHooks = kinds;
						generation++;
						if (provider.SetHooks((kinds & HookType.Keyboard) != 0, (kinds & HookType.Mouse) != 0, this, out failure))
						{
							ForgetDevices(changed);
#if !WINDOWS
							if ((kinds & ~previous) != HookType.None) ResetHook(false, kinds & ~previous, true);
#endif
							panicKeys = 0;
						}
						else
						{
							if (Volatile.Read(ref lost) == 0)
							{
								Volatile.Write(ref lost, wasLost);
								activeHooks = previous;
								generation = previousGeneration;
								RestoreNativeHooks(previous);
							}
							ReportError($"Input capture could not be installed: {failure}");
						}
						SyncHookMutexes(false);
					}
					catch (Exception ex) { CaptureLost(ex.Message); }
					finally { Exit(); }
				}
				return null;
			});
		}

		private void RestoreNativeHooks(HookType kinds)
		{
			AddRemoveNativeHooks(kinds, false);
#if WINDOWS
			if (GetNativeHooks() != kinds)
			{
				activeHooks = GetNativeHooks();
				SyncHookMutexes(false);
				CaptureLost("The native input observers could not be restored.");
			}
#endif
		}

#if WINDOWS
		protected override nint KeybdHookCallback(int code, nint message, ref KBDLLHOOKSTRUCT input)
		{
			if (code == HC_ACTION && UsesHookProvider && Volatile.Read(ref lost) == 0 && HasKbdHook()
				&& !IsKeybdEventArtificial(input.flags, input.dwExtraInfo))
				return CallNextHookEx(kbdHook, code, message, ref input);
			var previous = current;
			current = null;
			try { return base.KeybdHookCallback(code, message, ref input); }
			finally { current = previous; }
		}

		protected override nint MouseHookCallback(int code, nint message, ref MSDLLHOOKSTRUCT input)
		{
			if (code == HC_ACTION && UsesHookProvider && Volatile.Read(ref lost) == 0 && HasMouseHook()
				&& !IsMouseEventArtificial(input.flags, input.dwExtraInfo))
			{
				if (message == WM_MOUSEMOVE && CursorClipActive)
				{
					int x = input.pt.X, y = input.pt.Y;
					if (ClampToCursorClip(ref x, ref y)) { _ = Platform.Mouse.TryMoveAbsolute(x, y); return 1; }
				}
				return CallNextHookEx(mouseHook, code, message, ref input);
			}
			var previous = current;
			current = null;
			try { return base.MouseHookCallback(code, message, ref input); }
			finally { current = previous; }
		}

#endif
		bool IHookHost.KeyboardEvent(uint message, uint vk, uint scan, uint flags, uint time, ulong extraInfo, long deviceId)
		{
			if (!TryEnter()) return false;
			try
			{
#if WINDOWS
				var panicVk = vk != 0 ? vk : scan switch { 0x0E => VK_BACK, 0x01 => VK_ESCAPE, 0x1C => VK_RETURN, _ => 0 };
				var physical = (flags & (LLKHF_INJECTED | LLKHF_EXTENDED)) == 0;
#else
				if (vk == 0)
#if OSX
					_ = KeyCodes.TryMapMacCodeToVk(scan, out vk);
#else
					vk = KeyCodes.MapScToVk(scan);
#endif
				var panicVk = vk;
				var physical = (flags & LLKHF_INJECTED) == 0;
#endif
				physical &= !IsSenderProviderOutput(extraInfo);
				var key = !physical ? 0 : panicVk switch
				{ VK_BACK => 1, VK_ESCAPE => 2, VK_RETURN => 4, _ => 0 };
				if (message is WM_KEYUP or WM_SYSKEYUP) panicKeys &= ~key;
				else if (key != 0 && (panicKeys |= key) == 7)
				{
					CaptureLost("Backspace, Escape and Enter were held together.");
					return false;
				}
				if (!Admit(HookType.Keyboard)) return false;
				var frame = RentFrame(message, vk, scan, flags, time, extraInfo, deviceId);
#if WINDOWS
				frame.TrackOnly = kbdHook == 0;
#endif
				return Dispatch(frame);
			}
			finally { Exit(); }
		}

		bool IHookHost.MouseEvent(uint message, int x, int y, uint data, uint flags, uint time, ulong extraInfo, long deviceId, int dx, int dy)
		{
			if (!TryEnter()) return false;
			try
			{
				if (!Admit(HookType.Mouse)) return false;
				if (message == WM_MOUSEMOVE && (flags & LLMHF_INJECTED) == 0 && !IsSenderProviderOutput(extraInfo)
					&& script.input == null && !script.KeyboardData.blockMouseMove
					&& !script.KeyboardData.blockInput && !CursorClipActive)
				{
#if OSX
					if (x != int.MinValue && y != int.MinValue) mouseStream.ObserveMove(x, y, mouseStream.SenderRevision);
					else mouseStream.InvalidatePosition();
#endif
					script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;
					return false;
				}
				var frame = RentFrame(message, 0, 0, flags, time, extraInfo, deviceId);
				frame.Mouse = true;
#if WINDOWS
				frame.TrackOnly = mouseHook == 0;
#endif
				(frame.X, frame.Y, frame.Data, frame.DeltaX, frame.DeltaY) = (x, y, data, dx, dy);
				return Dispatch(frame);
			}
			finally { Exit(); }
		}

		private bool Admit(HookType kind) => Volatile.Read(ref lost) == 0 && !script.IsDisposed && !script.hasExited
			&& (GetActiveHooks() & kind) != 0;

		private Frame RentFrame(uint message, uint vk, uint scan, uint flags, uint time, ulong extraInfo, long deviceId)
		{
			var frame = reusable;
			if (frame == null || !frame.Finished) reusable = frame = new Frame(this);
			(frame.Message, frame.Vk, frame.Scan, frame.Flags, frame.Time, frame.ExtraInfo, frame.DeviceId) =
				(message, vk, scan, flags, time == 0 ? unchecked((uint)Environment.TickCount) : time, extraInfo, deviceId);
			frame.Source = source;
			frame.Mouse = frame.TrackOnly = frame.Finished = frame.Cancelled = frame.Committed = frame.Forwarded = frame.Forwarding = frame.Suppressed = false;
			return frame;
		}

		private bool Dispatch(Frame frame)
		{
			frame.Deadline = Stopwatch.GetTimestamp() + (long)DispatchTimeoutMilliseconds * Stopwatch.Frequency / 1000;
			if (IsOnHookThread) Run(frame);
			else
			{
				try { QueueOnHookThread(frame.RunCallback); }
				catch (Exception ex) { CaptureLost(ex.Message); return false; }
				lock (frame)
				{
					while (!frame.Finished)
					{
						var remaining = (frame.Deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
						if (remaining > 0) { System.Threading.Monitor.Wait(frame, (int)Math.Min(remaining + 1, int.MaxValue)); continue; }
						// A callback already launched must keep its suppression decision. Later work cannot post or forward.
						frame.Cancelled = true;
						while (frame.Forwarding) System.Threading.Monitor.Wait(frame);
						break;
					}
				}
				if (frame.Cancelled) CaptureLost("The script did not decide an input event in time.");
			}
			return frame.Cancelled ? frame.Committed && !frame.Forwarded : frame.Suppressed;
		}

		private void Run(Frame frame)
		{
			var previous = current;
			current = frame;
			try
			{
				var kind = frame.Mouse ? HookType.Mouse : HookType.Keyboard;
				lock (frame)
					if ((frame.Cancelled |= Stopwatch.GetTimestamp() >= frame.Deadline) || !UsesHookProvider || Volatile.Read(ref lost) != 0 || !ReferenceEquals(frame.Source, source)
						|| (GetActiveHooks() & kind) == 0) return;
#if WINDOWS
				frame.TrackOnly |= frame.Mouse ? mouseHook == 0 : kbdHook == 0;
#endif
				ResetPendingState();
#if OSX
				var observeModifiers = !frame.Mouse && !HasKeysharpInjectedExtraInfo(frame.ExtraInfo) && ModifierLRMaskFromVK(frame.Vk) != 0;
				var senderRevision = observeModifiers ? keyboardState.SenderRevisions.Modifiers : -1;
#endif
				var budget = (frame.Deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency - 100;
				using var scope = BeginHookCallback((int)Math.Clamp(budget, 0, HotIfCallbackBudgetMilliseconds));
				frame.Suppressed = frame.Mouse ? ProcessMouse(frame) : ProcessKeyboard(frame);
#if OSX
				if (!frame.Mouse && !HasKeysharpInjectedExtraInfo(frame.ExtraInfo))
				{
					if (observeModifiers) keyboardState.ObserveModifiers(kbdMsSender.modifiersLRLogical, senderRevision);
					if (frame.Vk == VK_CAPITAL && Keysharp.Internals.Input.MacOS.MacCapsLockState.TryGet(out var capsOn))
						keyboardState.ObserveCapsLock(capsOn);
				}
#endif
				if (!frame.Suppressed) ForwardCurrent();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Input capture processing failed: {ex}");
				CaptureLost(ex.Message);
			}
			finally
			{
				current = previous;
				lock (frame)
				{
					frame.Cancelled |= Stopwatch.GetTimestamp() >= frame.Deadline;
					frame.Suppressed &= !frame.Forwarded;
					frame.Finished = true;
					System.Threading.Monitor.PulseAll(frame);
				}
			}
		}

		protected internal override nint CallNextHook(HookEventArgs e)
		{
			if (current == null) return base.CallNextHook(e);
			ForwardCurrent();
			return 0;
		}

		private void ForwardCurrent()
		{
			var frame = current;
			if (frame == null) return;
#if WINDOWS
			if (forwardingHolds != 0) return;
#endif
			lock (frame)
			{
				if ((frame.Cancelled |= Stopwatch.GetTimestamp() >= frame.Deadline) || frame.Forwarded || !UsesHookProvider) return;
				frame.Forwarding = true;
			}
			try { provider.CallNextHook(); }
			finally
			{
				lock (frame)
				{
					frame.Forwarded = true;
					frame.Forwarding = false;
					System.Threading.Monitor.PulseAll(frame);
				}
			}
		}

		internal override bool PostMessage(KeysharpMsg message)
		{
			if (!InHookCallback || message.message == (uint)UserMessages.AHK_INPUT_END || current is not { } frame)
				return base.PostMessage(message);
			lock (frame)
			{
				if (frame.Cancelled |= Stopwatch.GetTimestamp() >= frame.Deadline) return false;
				var posted = base.PostMessage(message);
				frame.Committed |= posted && (message.message == (uint)UserMessages.AHK_HOOK_HOTKEY
					|| message.message == (uint)UserMessages.AHK_HOTSTRING);
				return posted;
			}
		}

		private bool ProcessKeyboard(Frame f)
		{
			if (f.Message is not (WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP) || f.Vk > VK_MAX) return false;
			if (f.Scan > SC_MAX && !(f.Vk == VK_PACKET && (f.Flags & LLKHF_INJECTED) != 0 && f.Scan <= char.MaxValue)) return false;
			var up = f.Message is WM_KEYUP or WM_SYSKEYUP;
			var injected = (f.Flags & LLKHF_INJECTED) != 0 || IsSenderProviderOutput(f.ExtraInfo);
#if WINDOWS
			var extended = (f.Flags & LLKHF_EXTENDED) != 0 || (f.Scan & 0x100) != 0;
			var scan = f.Scan & 0xFF;
			var vk = f.Vk;
			if (vk == 0)
			{
				var layout = GetKeyboardLayout(WindowQuery.GetFocusedCtrlThread(GetForegroundWindow()));
				vk = MapVirtualKeyEx(scan | (extended ? 0xE000u : 0), MAPVK_VSC_TO_VK_EX, layout);
				if (!extended && ((vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9) || vk == VK_DECIMAL))
					vk = KeypadState(vk);
				else if (!extended && KeypadOf(vk) is var keypad and not 0)
					vk = KeypadState(keypad);
			}
			if (vk == 0 || vk > VK_MAX) return false;
			if (vk is VK_RSHIFT or VK_NUMLOCK || vk == VK_SHIFT && scan == (RShift & 0xFF)) extended = true;
			var flags = extended ? f.Flags | LLKHF_EXTENDED : f.Flags & ~LLKHF_EXTENDED;
			var sc = scan | (extended ? 0x100u : 0);
			vk = vk switch { VK_SHIFT => sc == RShift ? VK_RSHIFT : VK_LSHIFT,
				VK_CONTROL => sc == RControl ? VK_RCONTROL : VK_LCONTROL, VK_MENU => sc == RAlt ? VK_RMENU : VK_LMENU, _ => vk };
#else
			var sc = f.Scan;
			var vk = f.Vk;
			if (vk is VK_SHIFT or VK_CONTROL or VK_MENU)
			{
#if OSX
				_ = KeyCodes.TryMapMacCodeToVk(sc, out var mapped);
#else
				var mapped = KeyCodes.MapScToVk(sc);
#endif
				vk = vk switch { VK_SHIFT => mapped == VK_RSHIFT ? VK_RSHIFT : VK_LSHIFT,
					VK_CONTROL => mapped == VK_RCONTROL ? VK_RCONTROL : VK_LCONTROL,
					_ => mapped == VK_RMENU ? VK_RMENU : VK_LMENU };
			}
			if (vk == 0 || vk > VK_MAX) return false;
			var scan = sc;
#endif
			if (!injected) RecordDevice(f, vk, sc, up);
			if ((!injected && script.KeyboardData.blockInput) || f.TrackOnly)
			{
				if (!injected) script.timeLastInputPhysical = script.timeLastInputKeyboard = DateTime.UtcNow;
				if (kvk[vk].asModifiersLR != 0) UpdateKeybdState(scan, f.ExtraInfo, injected, vk, sc, up, script.KeyboardData.blockInput);
				else if (!injected) physicalKeyState[vk] = (byte)(up ? 0 : StateDown);
				return !injected && script.KeyboardData.blockInput;
			}
#if WINDOWS
			if (vk == VK_RMENU && (up ? altGrControl : !altGrControl && (kbdMsSender.modifiersLRPhysical & MOD_LCONTROL) == 0
				&& kbdMsSender.LayoutHasAltGr(GetKeyboardLayout(WindowQuery.GetFocusedCtrlThread(GetForegroundWindow()))) == ResultType.ConditionTrue))
			{
				altGrControl = !up;
				var control = new KBDLLHOOKSTRUCT { vkCode = VK_LCONTROL, scanCode = FakeLControl, flags = flags & ~LLKHF_EXTENDED,
					time = f.Time, dwExtraInfo = f.ExtraInfo };
				forwardingHolds++;
				try { _ = LowLevelKeybdHandler(HC_ACTION, (nint)f.Message, ref control, f.DeviceId == 0 ? null : f.DeviceId); }
				finally { forwardingHolds--; }
			}
			var input = new KBDLLHOOKSTRUCT { vkCode = vk, scanCode = scan, flags = flags, time = f.Time, dwExtraInfo = f.ExtraInfo };
			return LowLevelKeybdHandler(HC_ACTION, (nint)f.Message, ref input, f.DeviceId == 0 ? null : f.DeviceId) != 0;
#else
			var args = new KeyboardHookEventArgs(up ? EventType.KeyReleased : EventType.KeyPressed, vk, sc,
				injected ? EventMask.SimulatedEvent : EventMask.None, f.Time);
			return LowLevelCommon(args, vk, sc, sc, up, f.ExtraInfo, injected ? HOOK_EVENT_INJECTED : 0,
				f.DeviceId == 0 ? null : f.DeviceId) != 0;
#endif
		}

#if WINDOWS
		private uint KeypadState(uint vk)
			=> IsKeyToggledOn(VK_NUMLOCK) != ((kbdMsSender.modifiersLRLogical & (MOD_LSHIFT | MOD_RSHIFT)) != 0)
				? vk : vk switch { VK_NUMPAD0 => VK_INSERT, VK_NUMPAD1 => VK_END, VK_NUMPAD2 => VK_DOWN,
					VK_NUMPAD3 => VK_NEXT, VK_NUMPAD4 => VK_LEFT, VK_NUMPAD5 => VK_CLEAR, VK_NUMPAD6 => VK_RIGHT,
					VK_NUMPAD7 => VK_HOME, VK_NUMPAD8 => VK_UP, VK_NUMPAD9 => VK_PRIOR, _ => VK_DELETE };

		private static uint KeypadOf(uint vk) => vk switch { VK_INSERT => VK_NUMPAD0, VK_END => VK_NUMPAD1, VK_DOWN => VK_NUMPAD2,
			VK_NEXT => VK_NUMPAD3, VK_LEFT => VK_NUMPAD4, VK_CLEAR => VK_NUMPAD5, VK_RIGHT => VK_NUMPAD6,
			VK_HOME => VK_NUMPAD7, VK_UP => VK_NUMPAD8, VK_PRIOR => VK_NUMPAD9, VK_DELETE => VK_DECIMAL, _ => 0 };
#endif

		private bool ProcessMouse(Frame f)
		{
			var vk = MouseButton(f.Message, f.Data, out var up);
			var injected = (f.Flags & LLMHF_INJECTED) != 0 || IsSenderProviderOutput(f.ExtraInfo);
#if OSX
			var mouseRevision = mouseStream.SenderRevision;
#endif
			if (!injected && vk != 0 && !MouseUtils.IsWheelVK(vk)) RecordDevice(f, vk, 0, up);
			if ((!injected && script.KeyboardData.blockInput) || f.TrackOnly)
			{
				if (!injected) script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;
				if (!injected && vk != 0 && !MouseUtils.IsWheelVK(vk)) physicalKeyState[vk] = (byte)(up ? 0 : StateDown);
				return !injected && script.KeyboardData.blockInput;
			}
			var position = f.X != int.MinValue && f.Y != int.MinValue;
			if (f.Message == WM_MOUSEMOVE)
			{
				if (!injected) script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;
				if (!injected && script.KeyboardData.blockMouseMove) return true;
				if (!injected && CursorClipActive && position)
				{
					int x = f.X, y = f.Y;
					if (ClampToCursorClip(ref x, ref y)) { _ = Platform.Mouse.TryMoveAbsolute(x, y); return true; }
				}
				var suppressedMove = script.input != null && !CollectMouseMove(f.DeltaX, f.DeltaY, f.ExtraInfo, injected, f.Time,
					position ? new POINT(f.X, f.Y) : null, f.DeviceId == 0 ? null : f.DeviceId, (f.Data & 0x8000) != 0);
#if OSX
				if (!suppressedMove && position && !HasKeysharpInjectedExtraInfo(f.ExtraInfo))
					mouseStream.ObserveMove(f.X, f.Y, mouseRevision);
#endif
				return suppressedMove;
			}
			if (vk == 0) return false;
#if WINDOWS
			// Callback coordinates may be queried, while A_EventInfo only exposes a position captured with the event.
			if (!position && !Platform.Mouse.TryGetCursorPos(out f.X, out f.Y))
			{
				if (!MouseUtils.IsWheelVK(vk)) physicalKeyState[vk] = (byte)(up ? 0 : StateDown);
				return false;
			}
			var input = new MSDLLHOOKSTRUCT { pt = new POINT(f.X, f.Y), mouseData = unchecked((int)f.Data),
				flags = f.Flags, time = unchecked((int)f.Time), dwExtraInfo = f.ExtraInfo };
			return LowLevelMouseHandler(HC_ACTION, (nint)f.Message, ref input, f.DeviceId == 0 ? null : f.DeviceId, position) != 0;
#else
			var mask = injected ? EventMask.SimulatedEvent : EventMask.None;
			var delta = (uint)(short)(f.Data >> 16);
			HookEventArgs args = MouseUtils.IsWheelVK(vk)
				? new MouseWheelHookEventArgs((short)delta, f.Message == WM_MOUSEWHEEL ? MouseWheelScrollDirection.Vertical
					: MouseWheelScrollDirection.Horizontal, f.X, f.Y, mask, f.Time) { HasPosition = position }
				: new MouseHookEventArgs(up ? EventType.MouseReleased : EventType.MousePressed,
					KeyCodes.VkToMouseButton(vk), f.X, f.Y, mask, f.Time) { HasPosition = position };
			var suppressed = LowLevelCommon(args, vk, MouseUtils.IsWheelVK(vk) ? delta : 0,
				MouseUtils.IsWheelVK(vk) ? delta : 0, up, f.ExtraInfo, injected ? HOOK_EVENT_INJECTED : 0,
				f.DeviceId == 0 ? null : f.DeviceId) != 0;
#if OSX
			if (!suppressed && !MouseUtils.IsWheelVK(vk) && !HasKeysharpInjectedExtraInfo(f.ExtraInfo))
				mouseStream.ObserveButton(KeyCodes.VkToMouseButton(vk), !up, f.X, f.Y, 1, mouseRevision, position);
#endif
			return suppressed;
#endif
		}

		internal override uint GetModifierLRStateLogical()
		{
#if !WINDOWS
			if (UsesHookProvider && HasKbdHook()) return kbdMsSender.modifiersLRLogical;
#endif
			var state = base.GetModifierLRStateLogical();
			// A driver transition reaches OS state only after forwarding; native repair must not erase it while pending.
			return current is { Forwarded: false } ? state | kbdMsSender.modifiersLRLogical : state;
		}

		internal override void ClearPhysicalModifiers(uint modifiers)
		{
			// Captured physical holds survive synthetic changes to OS logical state.
			if (!UsesHookProvider || Volatile.Read(ref lost) != 0) base.ClearPhysicalModifiers(modifiers);
		}

#if OSX
		internal override bool IsKeyDownLogical(uint vk)
			=> UsesHookProvider && HasKbdHook() && ModifierLRMaskFromVK(vk) is var mask and not 0
				? (kbdMsSender.modifiersLRLogical & mask) != 0 : base.IsKeyDownLogical(vk);
#endif

		private static uint MouseButton(uint message, uint data, out bool up)
		{
			up = message is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP or WM_XBUTTONUP;
			return message switch { WM_LBUTTONDOWN or WM_LBUTTONUP => VK_LBUTTON,
				WM_RBUTTONDOWN or WM_RBUTTONUP => VK_RBUTTON, WM_MBUTTONDOWN or WM_MBUTTONUP => VK_MBUTTON,
				WM_XBUTTONDOWN or WM_XBUTTONUP => data >> 16 == 1 ? VK_XBUTTON1 : VK_XBUTTON2,
				WM_MOUSEWHEEL => (short)(data >> 16) > 0 ? VK_WHEEL_UP : VK_WHEEL_DOWN,
				WM_MOUSEHWHEEL => (short)(data >> 16) > 0 ? VK_WHEEL_RIGHT : VK_WHEEL_LEFT, _ => 0 };
		}

		public void CaptureLost(string message)
		{
			int selectedGeneration;
			lock (controlGate)
			{
				if (!UsesHookProvider || Interlocked.Exchange(ref lost, 1) != 0) return;
				selectedGeneration = generation;
				failure = message;
#if WINDOWS
				activeHooks = GetNativeHooks();
#else
				activeHooks = HookType.None;
#endif
				ClearDeviceState();
			}
			try { QueueOnHookThread(() =>
			{
				if (!TryEnter()) return;
				try
				{
					lock (controlGate)
						if (generation == selectedGeneration && Volatile.Read(ref lost) != 0)
						{
							_ = provider.SetHooks(false, false, this, out _);
							ResetPendingState();
#if LINUX
							TrackForegroundWindow(false);
#endif
							SyncHookMutexes(false);
						}
				}
				catch (Exception ex) { Diagnostics.Debug.WriteLine($"Input capture cleanup failed: {ex}"); }
				finally { Exit(); }
			}); }
			catch (Exception ex) { Diagnostics.Debug.WriteLine($"Input capture cleanup could not be queued: {ex}"); }
			ReportError($"Input capture ended: {message}");
		}

		private void ReportError(string message)
		{
			if (!script.IsDisposed && !script.hasExited)
				_ = script.MainEventScheduler.EnqueueThreadLaunch(0, false, false,
					() => _ = Errors.OSErrorOccurredWithMessage(message), useTryCatch: true);
		}

		public void ResetState()
		{
			if (!UsesHookProvider) return;
			ClearDeviceState();
			try { QueueOnHookThread(ResetPendingState); }
			catch (ObjectDisposedException) { }
		}

		private void ResetPendingState()
		{
			if (Interlocked.Exchange(ref resetPending, 0) == 0) return;
#if OSX
			mouseStream.ResetObservedButtons();
			mouseStream.ForgetHardwareMoves();
#endif
#if WINDOWS
			altGrControl = false;
#endif
			panicKeys = 0;
			if (kvk?.Length > 0) ResetHook(allModifiersUp: !OperatingSystem.IsWindows(), resetKVKandKSC: true);
#if OSX
			keyboardState.ObserveModifiers(kbdMsSender.modifiersLRLogical);
#endif
		}

		public void Dispose()
		{
			// Removal holds a call of its own, so the provider outlives the retirement that removes its hooks.
			if (!TryEnter()) return;
			try
			{
				lock (controlGate)
				{
					if (capture.IsRetired) return;
					_ = capture.Retire();
#if WINDOWS
					activeHooks = GetNativeHooks();
#else
					activeHooks = HookType.None;
#endif
					ClearDeviceState();
					try { _ = provider.SetHooks(false, false, this, out _); }
					catch (Exception ex) { Diagnostics.Debug.WriteLine($"Input capture removal failed: {ex}"); }
				}
				SyncHookMutexes(false);
				if (!script.IsDisposed && IsHookThreadRunning())
					try { QueueOnHookThread(ResetPendingState); }
					catch (ObjectDisposedException) { }
			}
			finally { Exit(); }
		}

		protected internal override void Stop()
		{
			Dispose();
			base.Stop();
		}

		private bool TryEnter() => capture.TryEnter();
		private void Exit()
		{
#if WINDOWS
			_ = capture.Exit();
#else
			if (!capture.Exit()) return;
			// The last call out of a removed provider hands capture back to the native hooks.
			_ = Invoke(() =>
			{
				var kinds = script.HotkeyData.whichHookNeeded | script.HotkeyData.whichHookAlways;
				try
				{
					if (!script.IsDisposed && kinds != HookType.None) AddRemoveNativeHooks(kinds, false);
					else SyncHookMutexes(false);
				}
				catch (Exception ex) { ReportError($"Native input capture could not be restored: {ex.Message}"); }
				return null;
			});
#endif
		}

		private void ClearDeviceState()
		{
			lock (devices) { devices.Clear(); identities.Clear(); source = new(); }
			Volatile.Write(ref resetPending, 1);
		}

		private void ForgetDevices(HookType kinds)
		{
#if OSX
			if ((kinds & HookType.Mouse) != 0) { mouseStream.ResetObservedButtons(); mouseStream.ForgetHardwareMoves(); }
#endif
			lock (devices)
				foreach (var key in devices.Keys.ToArray())
					if ((kinds & (key.Mouse ? HookType.Mouse : HookType.Keyboard)) != 0) devices.Remove(key);
		}

		private void RecordDevice(Frame frame, uint vk, uint sc, bool up)
		{
			if (frame.DeviceId == 0) return;
			var key = (frame.DeviceId, frame.Mouse);
			string observed = null;
			var available = true;
			Keysharp.Components.Input.InputDeviceInfo info = null;
			if (provider is IInputDeviceProvider descriptions)
			{
				lock (devices) identities.TryGetValue(key, out observed);
				try { available = descriptions.TryGetDeviceInfo(frame.DeviceId, frame.Mouse ? "Mouse" : "Keyboard", out info); }
				catch (Exception ex) when (ex is not KeysharpException and not Keysharp.Builtins.Flow.UserRequestedExitException)
				{
					available = false;
					Diagnostics.Debug.WriteLine($"Input device identity is unavailable: {ex.Message}");
				}
			}
			lock (devices)
			{
				if (!ReferenceEquals(frame.Source, source)) return;
				if (!available) { ForgetDevice(key, observed); return; }
				// A reused slot must forget its previous attachment before scan-repeat detection.
				if (info != null && !ObserveIdentity(key, info.HardwareId, observed)) return;
				if (!devices.TryGetValue(key, out var state)) devices[key] = state = new();
				if (sc != 0)
				{
					var held = state.Scans[sc];
					if (held != 0) { if (!up) return; vk = held; state.Scans[sc] = 0; }
					else if (!up) state.Scans[sc] = (byte)vk;
					else return;
					if (up) { if (state.Keys[vk] != 0) state.Keys[vk]--; }
					else state.Keys[vk]++;
				}
				else state.Keys[vk] = (ushort)(up ? 0 : 1);
			}
		}

		internal override DeviceInfo[] GetInputDevices()
		{
			if (!UsesHookProvider) return base.GetInputDevices();
			if (!TryEnter()) return [];
			var lease = source;
			try
			{
				if (provider is not IInputDeviceProvider descriptions) return base.GetInputDevices();
				Dictionary<(long Id, bool Mouse), string> observed;
				lock (devices) observed = new(identities);
				var result = descriptions.GetDevices().Select(info => ConvertDevice(info, lease)).ToArray();
				lock (devices)
				{
					if (!ReferenceEquals(lease, source)) return result;
					var present = new HashSet<(long, bool)>();
					foreach (var device in result)
					{
						var key = (device.Id, device.Kind == "Mouse");
						present.Add(key);
						observed.TryGetValue(key, out var previous);
						_ = ObserveIdentity(key, device.HardwareId, previous);
					}
					foreach (var key in identities.Keys.ToArray())
						if (!present.Contains(key) && observed.TryGetValue(key, out var previous)) ForgetDevice(key, previous);
				}
				return result;
			}
			finally { Exit(); }
		}

		private bool ObserveIdentity((long Id, bool Mouse) key, string hardwareId, string observed)
		{
			identities.TryGetValue(key, out var previous);
			if (previous == hardwareId) return true;
			// An older metadata result cannot replace an identity observed while its query ran.
			if (previous != null && previous != observed) return false;
			if (previous != null) devices.Remove(key);
			identities[key] = hardwareId;
			return true;
		}
		private void ForgetDevice((long Id, bool Mouse) key, string observed)
		{
			identities.TryGetValue(key, out var previous);
			if (previous != observed) return;
			identities.Remove(key);
			devices.Remove(key);
		}
		private static DeviceInfo ConvertDevice(Keysharp.Components.Input.InputDeviceInfo info, object lease)
			=> new(info.Id, info.Kind, info.Name, info.HasVendorId ? info.VendorId : null,
				info.HasProductId ? info.ProductId : null, info.HardwareId, info.Manufacturer ?? "", info.Connection ?? "",
				info.Location ?? "", info.IsInternal) { Source = lease };

		internal override bool TryGetDeviceKeyState(DeviceInfo device, uint vk, out bool down)
		{
			if (!ReferenceEquals(device.Source, source)) return base.TryGetDeviceKeyState(device, vk, out down);
			down = false;
			if (Volatile.Read(ref lost) != 0 || !TryEnter()) return false;
			try
			{
				var key = (device.Id, device.Kind == "Mouse");
				if (provider is IInputDeviceProvider descriptions)
				{
					string observed;
					lock (devices) identities.TryGetValue(key, out observed);
					var found = descriptions.TryGetDeviceInfo(device.Id, device.Kind, out var info);
					lock (devices)
					{
						if (!ReferenceEquals(device.Source, source)) return false;
						if (!found) { ForgetDevice(key, observed); return false; }
						if (!ObserveIdentity(key, info.HardwareId, observed)) return false;
					}
					if (info.HardwareId != device.HardwareId) return false;
				}
				lock (devices)
				{
					if (!ReferenceEquals(device.Source, source) || !devices.TryGetValue(key, out var state)) return false;
					down = vk switch { VK_SHIFT => state.Keys[VK_LSHIFT] != 0 || state.Keys[VK_RSHIFT] != 0,
						VK_CONTROL => state.Keys[VK_LCONTROL] != 0 || state.Keys[VK_RCONTROL] != 0,
						VK_MENU => state.Keys[VK_LMENU] != 0 || state.Keys[VK_RMENU] != 0, _ => state.Keys[vk] != 0 };
					return true;
				}
			}
			catch (Exception ex) when (ex is not KeysharpException and not Keysharp.Builtins.Flow.UserRequestedExitException)
			{
				Diagnostics.Debug.WriteLine($"Input device state is unavailable: {ex.Message}");
				return false;
			}
			finally { Exit(); }
		}

		protected override object EventDeviceSource => current?.Source ?? base.EventDeviceSource;

		internal override DeviceInfo ResolveInputDevice(long id, bool mouse, object lease)
		{
			if (!ReferenceEquals(lease, source)) return base.ResolveInputDevice(id, mouse, lease);
			var descriptor = new DeviceInfo(id, mouse ? "Mouse" : "Keyboard") { Source = lease };
			if (TryEnter())
			{
				try
				{
					var key = (id, mouse);
					string observed;
					lock (devices) identities.TryGetValue(key, out observed);
					if (provider is IInputDeviceProvider descriptions && descriptions.TryGetDeviceInfo(id, descriptor.Kind, out var info))
					{
						descriptor = ConvertDevice(info, lease);
						lock (devices)
							if (ReferenceEquals(lease, source)) _ = ObserveIdentity(key, info.HardwareId, observed);
					}
				}
				catch (Exception ex) { Diagnostics.Debug.WriteLine($"Input device description failed: {ex.Message}"); }
				finally { Exit(); }
			}
			return descriptor;
		}

		private sealed class DeviceState
		{
			internal readonly ushort[] Keys = new ushort[VK_ARRAY_COUNT];
			internal readonly byte[] Scans = new byte[SC_ARRAY_COUNT];
		}
		private sealed class Frame
		{
			internal readonly Action RunCallback;
			internal Frame(ProviderHookThread owner) => RunCallback = () => owner.Run(this);
			internal uint Message, Vk, Scan, Flags, Time;
			internal ulong ExtraInfo;
			internal long DeviceId;
			internal object Source;
			internal int X, Y, DeltaX, DeltaY;
			internal uint Data;
			internal long Deadline;
			internal bool Mouse, TrackOnly, Finished, Cancelled, Committed, Forwarded, Forwarding, Suppressed;
		}

	}
}
