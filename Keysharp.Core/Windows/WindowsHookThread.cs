#if WINDOWS
using System;
using System.Runtime.CompilerServices;
using static Keysharp.Core.Common.Keyboard.KeyboardMouseSender;
using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Mouse.MouseUtils;
using static Keysharp.Core.Common.Keyboard.ScanCodes;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using static Keysharp.Core.Windows.WindowsAPI;

namespace Keysharp.Core.Windows
{
	internal enum EventType
	{
		HookEnabled,
		HookDisabled,
		KeyTyped,
		KeyPressed,
		KeyReleased,
		MouseClicked,
		MousePressed,
		MouseReleased,
		MouseMoved,
		MouseDragged,
		MouseWheel,
		MousePressedIgnoreCoordinates,
		MouseReleasedIgnoreCoordinates,
		MouseMovedRelativeToCursor
	}

	[Flags]
	internal enum EventMask
	{
		None = 0
	}

	internal enum MouseButton
	{
		Button1 = 1,
		Button2,
		Button3,
		Button4,
		Button5
	}

	internal enum MouseWheelScrollType
	{
		Unit,
		Block
	}

	internal enum MouseWheelScrollDirection
	{
		Vertical,
		Horizontal
	}

	internal enum KeyCode : uint
	{
		Undefined = 0
	}

	internal struct KeyboardEventData
	{
		internal KeyCode KeyCode;
		internal int RawCode;
		internal ushort RawKeyChar;
		internal char KeyChar => (char)RawKeyChar;
	}

	internal struct MouseEventData
	{
		internal MouseButton Button;
		internal int Clicks;
		internal int X;
		internal int Y;
	}

	internal struct MouseWheelEventData
	{
		internal MouseWheelScrollType Type;
		internal int Rotation;
		internal int Delta;
		internal MouseWheelScrollDirection Direction;
		internal int X;
		internal int Y;
	}

	internal class HookEventArgs : EventArgs
	{
		internal HookEventArgs(EventType type) => Type = type;
		internal EventType Type { get; }
		internal DateTime EventTime => DateTime.UtcNow;
		internal nint Hook { get; init; }
		internal int Code { get; init; }
		internal nint WParam { get; init; }
		internal bool IsEventSimulated { get; init; }
		internal bool SuppressEvent { get; set; }
		internal uint Flags { get; init; }
		internal nint StructPtr { get; init; }
		internal bool HasKeyboard { get; init; }
		internal bool HasMouse => !HasKeyboard;
	}

	internal class KeyboardHookEventArgs : HookEventArgs
	{
		internal KeyboardHookEventArgs(nint ptr, uint vk, uint sc, uint flags, bool simulated, nint hook, int code, nint wParam)
			: base(flags == WM_KEYUP || flags == WM_SYSKEYUP ? EventType.KeyReleased : EventType.KeyPressed)
		{
			StructPtr = ptr;
			Hook = hook;
			Code = code;
			WParam = wParam;
			HasKeyboard = true;
			Flags = flags;
			IsEventSimulated = simulated;
			Data = new KeyboardEventData()
			{
				KeyCode = (KeyCode)vk,
				RawCode = (int)sc,
				RawKeyChar = 0
			};
		}

		internal KeyboardEventData Data { get; }
	}

	internal class MouseHookEventArgs : HookEventArgs
	{
		internal MouseHookEventArgs(EventType type, nint ptr, uint flags, bool simulated, int x, int y, nint hook, int code, nint wParam)
			: base(type)
		{
			StructPtr = ptr;
			Hook = hook;
			Code = code;
			WParam = wParam;
			HasKeyboard = false;
			Flags = flags;
			IsEventSimulated = simulated;
			Data = new MouseEventData()
			{
				Button = MouseButton.Button1,
				Clicks = 0,
				X = x,
				Y = y
			};
		}

		internal MouseEventData Data { get; init; }
	}

	internal class MouseWheelHookEventArgs : HookEventArgs
	{
		internal MouseWheelHookEventArgs(nint ptr, int delta, uint flags, bool simulated, int x, int y, nint hook, int code, nint wParam)
			: base(EventType.MouseWheel)
		{
			StructPtr = ptr;
			Hook = hook;
			Code = code;
			WParam = wParam;
			HasKeyboard = false;
			Flags = flags;
			IsEventSimulated = simulated;
			Data = new MouseWheelEventData()
			{
				Type = MouseWheelScrollType.Unit,
				Rotation = delta,
				Delta = (int)WHEEL_DELTA,
				Direction = delta < 0 ? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Vertical,
				X = x,
				Y = y
			};
		}

		internal new MouseWheelEventData Data { get; init; }
	}

	/// <summary>
	/// Concrete implementation of HookThread for the Windows platfrom.
	/// Once we figure out how to wire up a hook on linux, we need to go through every method here and move any that are not
	/// windows-specific into the base class to reduce duplication.
	/// Of course leave any windows-specific methods here.
	/// </summary>
	internal class WindowsHookThread : HookThread
	{
		private readonly LowLevelKeyboardProc kbdHandlerDel;
		private readonly LowLevelMouseProc mouseHandlerDel;
		private StaThreadWithMessageQueue thread;
		private bool uwpAppFocused;
		private nint uwpHwndChecked = 0;
		internal WindowsHookThread()
		{
			kbdMsSender = new WindowsKeyboardMouseSender();
			kbdHandlerDel = new LowLevelKeyboardProc(LowLevelKeybdHandler);
			mouseHandlerDel = new LowLevelMouseProc(LowLevelMouseHandler);
		}

		public override void SimulateKeyPress(uint key)
		{
			var kbdStruct = new KBDLLHOOKSTRUCT()
			{
				vkCode = key,
				scanCode = MapVkToSc(key),
				flags = 0,
				time = (uint)DateTime.UtcNow.Ticks,
				dwExtraInfo = 0
			};
			_ = LowLevelKeybdHandler(0, new nint(256), ref kbdStruct);
		}

		internal override void AddRemoveHooks(HookType hooksToBeActive, bool changeIsTemporary = false)
		{
			var hooksActiveOrig = GetActiveHooks();

			if (hooksToBeActive == hooksActiveOrig) // It's already in the right state.
				return;

			if (hooksActiveOrig == HookType.None) // Neither hook is active now but at least one will be or the above would have returned.
			{
				// Assert: sThreadHandle should be NULL at this point.  The only way this isn't true is if
				// a previous call to AddRemoveHooks() timed out while waiting for the hook thread to exit,
				// which seems far too rare to add extra code for.
				// CreateThread() vs. _beginthread():
				// It's not necessary to link to the multi-threading C runtime (which bloats the code by 3.5 KB
				// compressed) as long as the new thread doesn't call any C-library functions that aren't thread-safe
				// (in addition to the C functions that obviously use static data, calls to things like malloc(),
				// new, and other memory management functions probably aren't thread-safe unless the multi-threaded
				// library is used). The memory leak described in MSDN for ExitThread() applies only to the
				// multi-threaded libraries (multiple sources confirm this), so it isn't a concern either.
				// That's true even if the program is linked against the multi-threaded DLLs (MSVCRT.dll) rather
				// than the libraries (e.g. for a minimum-sized SC.bin file), as confirmed by the following quotes:
				// "This applies only to the static-link version of the runtime. For this and other reasons, I
				// *highly* recommend using the DLL runtime, which lets you use CreateThread() without prejudice.
				// Confirmation from MSDN: "Another work around is to link the *executable* to the CRT in a *DLL*
				// instead of the static CRT."
				//
				// The hooks are designed to make minimal use of C-library calls, currently calling only things
				// like memcpy() and strlen(), which are thread safe in the single-threaded library (according to
				// their source code).  However, the hooks may indirectly call other library functions via calls
				// to KeyEvent() and other functions, which has already been reviewed for thread-safety but needs
				// to be kept in mind as changes are made in the future.
				//
				// CreateThread's second parameter is the new thread's initial stack size. The stack will grow
				// automatically if more is needed, so it's kept small here to greatly reduce the amount of
				// memory used by the hook thread.  The XP Task Manager's "VM Size" column (which seems much
				// more accurate than "Mem Usage") indicates that a new thread consumes 28 KB + its stack size.
				if (!changeIsTemporary) // Caller has ensured that thread already exists when aChangeIsTemporary==true.
				{
					Start();

					if (channelThreadID != 0)
					{
					}
					// The above priority level seems optimal because if some other process has high priority,
					// the keyboard and mouse hooks will still take precedence, which avoids the mouse cursor
					// and keystroke lag that would otherwise occur (confirmed through testing).  Due to their
					// return-ASAP nature, the hooks are an ideal candidate for almost-realtime priority because
					// they run only rarely and only for tiny bursts of time.
					// Note that the above must also be done in such a way that it works on NT4, which doesn't support
					// below-normal and above-normal process priorities, nor perhaps other aspects of priority.
					// So what is the actual priority given to the hooks by the OS?  Assuming that the script's
					// process is set to NORMAL_PRIORITY_CLASS (which is the default), the following applies:
					// First of all, a definition: "base priority" is the actual/net priority of the thread.
					// It determines how the OS will schedule a thread relative to all other threads on the system.
					// So in a sense, if you look only at base priority, the thread's process's priority has no
					// bearing on how the thread will get scheduled (except to the extent that it contributes
					// to the calculation of the base priority itself).  Here are some common base priorities
					// along with where the hook priority (15) fits in:
					// 7 = NORMAL_PRIORITY_CLASS process + THREAD_PRIORITY_NORMAL thread.
					// 9 = NORMAL_PRIORITY_CLASS process + THREAD_PRIORITY_HIGHEST thread.
					// 13 = HIGH_PRIORITY_CLASS process + THREAD_PRIORITY_NORMAL thread.
					// 15 = (ANY)_PRIORITY_CLASS process + THREAD_PRIORITY_TIME_CRITICAL thread. <-- Seems like the optimal compromise.
					// 15 = HIGH_PRIORITY_CLASS process + THREAD_PRIORITY_HIGHEST thread.
					// 24 = REALTIME_PRIORITY_CLASS process + THREAD_PRIORITY_NORMAL thread.
					else // Failed to create thread.  Seems to rare to justify the display of an error.
					{
						FreeHookMem(); // If everything's designed right, there should be no hooks now (even if there is, they can't be functional because their thread is nonexistent).
						return;
					}
				}
			}

			//else there is at least one hook already active, which guarantees that the hook thread exists (assuming
			// everything is designed right).
			// Above has ensured that the hook thread now exists, so send it the status-change message.
			// Post the AHK_CHANGE_HOOK_STATE message to the new thread to put the right hooks into effect.
			// If both hooks are to be deactivated, AHK_CHANGE_HOOK_STATE also causes the hook thread to exit.
			// PostThreadMessage() has been observed to fail, such as when a script replaces a previous instance
			// of itself via #SingleInstance.  I think this happens because the new thread hasn't yet had a
			// chance to create its message queue via GetMessage().  So rather than using something like
			// WaitForSingleObject() -- which might not be reliable due to split-second timing of when the
			// queue actually gets created -- just keep retrying until time-out or PostThreadMessage() succeeds.
			//var ksmsg = new KeysharpMsg()
			//{
			//  message = (uint)UserMessages.AHK_CHANGE_HOOK_STATE,
			//  wParam = new nint((uint)hooksToBeActive),
			//  lParam = new nint(changeIsTemporary ? 0 : 1/*Flipped on purpose*/)
			//};
			//
			//for (var i = 0; i < 50 && !channel.Writer.TryWrite(ksmsg); ++i)
			//  System.Threading.Thread.Sleep(10); // Should never execute if thread already existed before this function was called.
			//Pulled from msg queue thread
			var problemActivatingHooks = ChangeHookState(hooksToBeActive, changeIsTemporary);
			//
			//
			//for (var i = 0; i < 50 && !PostThreadMessage(hookThreadID, (uint)UserMessages.AHK_CHANGE_HOOK_STATE, new nint((uint)hooksToBeActive), new nint(changeIsTemporary ? 1 : 0/*Flipped on purpose*/)); ++i)
			//System.Threading.Thread.Sleep(10); // Should never execute if thread already existed before this function was called.
			// Above: Sleep(10) seems better than Sleep(0), which would max the CPU while waiting.
			// MUST USE Sleep vs. MsgSleep, otherwise an infinite recursion of ExitApp is possible.
			// This can be reproduced by running a script consisting only of the line #InstallMouseHook
			// and then exiting via the tray menu.  I tried fixing it in TerminateApp with the following,
			// but it's just not enough.  So rather than spend a long time on it, it's fixed directly here:
			// Because of the below, our callers must NOT assume that an exit will actually take place.
			//static is_running = false;
			//if (is_running)
			//  return OK;
			//is_running = true; // Since we're exiting, there should be no need to set it to false further below.
			// If it times out I think it's realistically impossible that the new thread really exists because
			// if it did, it certainly would have had time to execute GetMessage() in all but extreme/theoretical
			// cases.  Therefore, no thread check/termination attempt is done.  Alternatively, a check for
			// GetExitCodeThread() could be done followed by closing the handle and setting it to NULL, but once
			// again the code size doesn't seem worth it for a situation that is probably impossible.
			//
			// Also, a timeout itself seems too rare (perhaps even impossible) to justify a warning dialog.
			// So do nothing, which retains the current values of g_KeybdHook and g_MouseHook.
			// For safety, serialize the termination of the hook thread so that this function can't be called
			// again by the main thread before the hook thread has had a chance to exit in response to the
			// previous call.  This improves reliability, especially by ensuring a clean exit (if our caller
			// is about to exit the app via exit(), which otherwise might not cleanly close all threads).
			// UPDATE: Also serialize all changes to the hook status so that our caller can rely on the new
			// hook state being in effect immediately.  For example, the Input command installs the keyboard
			// hook and it's more maintainable if we ensure the status is correct prior to returning.
			//DateTime startTime;
			//bool problemActivatingHooks;
			//for (problemActivatingHooks = false, startTime = DateTime.UtcNow; ;) // For our caller, wait for hook thread to update the status of the hooks.
			{
				//Wait till we get confirmation back that the action completed.
				//if (PeekMessage(out msg, 0, (uint)UserMessages.AHK_CHANGE_HOOK_STATE, (uint)UserMessages.AHK_CHANGE_HOOK_STATE, PM_REMOVE))
				//if (ksmsg.completed)
				{
					if (hooksToBeActive != HookType.None) // Wait for the hook thread to activate the specified hooks.
					{
						//if (ksmsg.wParam != 0) // The hook thread indicated failure to activate one or both of the hooks.
						if (problemActivatingHooks) // The hook thread indicated failure to activate one or both of the hooks.
						{
							// This is done so that the MsgBox warning won't be shown until after these loops finish,
							// which seems safer to prevent any parts of the script from running as a result
							// the MsgBox pumping hotkey messages and such, which could result in a script
							// subroutine launching while we're in here:
							//problemActivatingHooks = true;
							if (GetActiveHooks() == HookType.None && !changeIsTemporary) // The failure is such that no hooks are now active, and thus (due to the mode) the hook thread will exit.
							{
								// Convert this loop into the mode that waits for the hook thread to exit.
								// This allows the thread handle to be closed and the memory to be freed.
								hooksToBeActive = 0;
								//continue;
							}

							// It failed but one hook is still active, or the change is temporary.  Either way,
							// we're done waiting.  Fall through to "break" below.
						}

						//else it successfully changed the state.
						// In either case, we're done waiting:
						//break;
						//else no AHK_CHANGE_HOOK_STATE message has arrived yet, so keep waiting until it does or timeout occurs.
					}
					else // The hook thread has been asked to deactivate both hooks.
					{
						if (changeIsTemporary) // The thread will not terminate in this mode, it will just remove its hooks.
						{
							//if (GetActiveHooks() == HookType.None) // The hooks have been deactivated.
							//  break; // Don't call FreeHookMem() because caller doesn't want that when aChangeIsTemporary==true.
						}
						else // Wait for the thread to terminate.
						{
							//GetExitCodeThread(new nint(hookThreadID), out exit_code);
							if (IsReadThreadCompleted()) // The hook thread is now gone.
							{
								channelReadThread = null;
								channelThreadID = 0;
								FreeHookMem();//There should be no hooks now (even if there is, they can't be functional because their thread is nonexistent).
								//break;
							}
						}
					}
				}
				//When stepping through code, this timeout will be exceeded, thus preventing the code above from running sometimes.
				//So do not timeout when debugging.
				//if (!Debugger.IsAttached)
				//{
				//  if ((DateTime.UtcNow - startTime).TotalMilliseconds > 1000)//Original did 500ms, increase to 1s just to be safe.
				//      break;
				//}
				// v1.0.43: The following sleeps for 0 rather than some longer time because:
				// 1) In nearly all cases, this loop should do only one iteration because a Sleep(0) should guarantee
				//    that the hook thread will get a timeslice before our thread gets another.  In fact, it might not
				//    do any iterations if the system preempts the main thread immediately when a message is posted to
				//    a higher priority thread (especially one in its own process).
				// 2) SendKeys()'s SendInput mode relies on fast removal of hook to prevent a 10ms or longer delay before
				//    the keystrokes get sent.  Such a delay would be quite undesirable in cases where response time is
				//    critical, such as in games.
				// Testing shows that removing the Sleep() entirely does not help performance.  The following was measured
				// when the CPU was under heavy load from a cpu-maxing utility:
				//   Loop 10  ; Keybd hook must be installed for this test to be meaningful.
				//      SendInput {Shift}
				//System.Threading.Thread.Sleep(0); // Not MsgSleep (see the "Sleep(10)" above for why).
			}

			// If the above loop timed out without the hook thread exiting (if it was asked to exit), sThreadHandle
			// is left as non-NULL to reflect this condition.

			// In case mutex create/open/close can be a high-overhead operation, do it only when the hook isn't
			// being quickly/temporarily removed then added back again.
			if (!changeIsTemporary)
			{
				if (HasKbdHook() && ((int)hooksActiveOrig & (int)HookType.Keyboard) == 0) // The keyboard hook has been newly added.
				{
					keybdMutex = new Mutex(false, KeybdMutexName, out _); // Create-or-open this mutex and have it be unowned.
				}
				else if (!HasKbdHook() && ((int)hooksActiveOrig & (int)HookType.Keyboard) != 0)  // The keyboard hook has been newly removed.
				{
					keybdMutex?.Close();
					keybdMutex = null;
				}

				if (HasKbdHook() && ((int)hooksActiveOrig & (int)HookType.Mouse) == 0) // The mouse hook has been newly added.
				{
					mouseMutex = new Mutex(false, MouseMutexName); // Create-or-open this mutex and have it be unowned.
				}
				else if (!HasKbdHook() && ((int)hooksActiveOrig & (int)HookType.Mouse) != 0)  // The mouse hook has been newly removed.
				{
					mouseMutex?.Close();
					mouseMutex = null;
				}
			}

			// For maintainability, it seems best to display the MsgBox only at the very end.
			if (problemActivatingHooks)
			{
				var script = Script.TheScript;
				// Prevent hotkeys and other subroutines from running (which could happen via MsgBox's message pump)
				// to avoid the possibility that the script will continue to call this function recursively, resulting
				// in an infinite stack of MsgBoxes. This approach is similar to that used in Hotkey::Perform()
				// for the A_MaxHotkeysPerInterval warning dialog:
				script.FlowData.allowInterruption = false;
				// Below is a generic message to reduce code size.  Failure is rare, but has been known to happen when
				// certain types of games are running).
				_ = MessageBox.Show("Warning: The keyboard and/or mouse hook could not be activated; some parts of the script will not function.");//AHK has its own MsgBox() function which does things differently. Will need to see if we need to do all of that.
				script.FlowData.allowInterruption = true;
			}
		}

		internal override uint CharToVKAndModifiers(char ch, ref uint? modifiersLR, nint keybdLayout, bool enableAZFallback = false)
		// If non-NULL, pModifiersLR contains the initial set of modifiers provided by the caller, to which
		// we add any extra modifiers required to realize aChar.
		{
			// For v1.0.25.12, it seems best to avoid the many recent problems with linefeed (`n) being sent
			// as Ctrl+Enter by changing it to always send a plain Enter, just like carriage return (`r).
			if (ch == '\n')
				return VK_RETURN;

			// Otherwise:
			var modPlusVk = VkKeyScanEx(ch, keybdLayout); // v1.0.44.03: Benchmark shows that VkKeyScanEx() is the same speed as VkKeyScan() when the layout has been pre-fetched.
			var vk = (uint)(modPlusVk & 0xFF);
			var keyscanModifiers = (char)((modPlusVk >> 8) & 0xFF);

			if (keyscanModifiers == -1 && vk == 0xFF) // No translation could be made.
			{
				if (!(enableAZFallback && Strings.Cisalpha(ch)))
					return 0;

				// v1.1.27.00: Use the A-Z fallback; assume the user means vk41-vk5A, since these letters
				// are commonly used to describe keyboard shortcuts even when these vk codes are actually
				// mapped to other characters.  Our callers should pass false for aEnableAZFallback if
				// they require a strict printable character.keycode mapping, such as for sending text.
				vk = char.ToUpper(ch);
				keyscanModifiers = (char)(Strings.Cisupper(ch) ? 0x01 : 0); // It's debatable whether the user intends this to be Shift+letter; this at least makes `Send ^A` consistent across (most?) layouts.
			}

			if ((keyscanModifiers & 0x38) != 0) // "The Hankaku key is pressed" or either of the "Reserved" state bits (for instance, used by Neo2 keyboard layout).
				return 0;// Callers expect failure in this case so that a fallback method can be used.

			// For v1.0.35, pModifiersLR was changed to modLR vs. mod so that AltGr keys such as backslash and
			// '{' are supported on layouts such as German when sending to apps such as Putty that are fussy about
			// which ALT key is held down to produce the character.  The following section detects AltGr by the
			// assuming that any character that requires both CTRL and ALT (with optional SHIFT) to be held
			// down is in fact an AltGr key (I don't think there are any that aren't AltGr in this case, but
			// confirmation would be nice).

			// The win docs for VkKeyScan() are a bit confusing, referring to flag "bits" when it should really
			// say flag "values".  In addition, it seems that these flag values are incompatible with
			// MOD_ALT, MOD_SHIFT, and MOD_CONTROL, so they must be translated:
			if (modifiersLR != null) // The caller wants this info added to the output param.
			{
				// Best not to reset this value because some callers want to retain what was in it before,
				// merely merging these new values into it:
				//*pModifiers = 0;
				if ((keyscanModifiers & 0x06) == 0x06) // 0x06 means "requires/includes AltGr".
				{
					// v1.0.35: The critical difference below is right vs. left ALT.  Must not include MOD_LCONTROL
					// because simulating the RAlt keystroke on these keyboard layouts will automatically
					// press LControl down.
					modifiersLR |= MOD_RALT;
				}
				else // Do normal/default translation.
				{
					// v1.0.40: If caller-supplied modifiers already include the right-side key, no need to
					// add the left-side key (avoids unnecessary keystrokes).
					if ((keyscanModifiers & 0x02) != 0 && (modifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) == 0)
						modifiersLR |= MOD_LCONTROL; // Must not be done if requires_altgr==true, see above.

					if ((keyscanModifiers & 0x04) != 0 && (modifiersLR & (MOD_LALT | MOD_RALT)) == 0)
						modifiersLR |= MOD_LALT;
				}

				// v1.0.36.06: Done unconditionally because presence of AltGr should not preclude the presence of Shift.
				// v1.0.40: If caller-supplied modifiers already contains MOD_RSHIFT, no need to add LSHIFT (avoids
				// unnecessary keystrokes).
				if ((keyscanModifiers & 0x01) != 0 && ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) == 0))
					modifiersLR |= MOD_LSHIFT;
			}

			return vk;
		}

		internal override bool EarlyCollectInput(ulong extraInfo, uint rawSC, uint vk, uint sc, bool keyUp, bool isIgnored
										, CollectInputState state, KeyHistoryItem keyHistoryCurr)
		// Returns true if the caller should treat the key as visible (non-suppressed).
		// Always use the parameter aVK rather than event.vkCode because the caller or caller's caller
		// might have adjusted aVK, such as to make it a left/right specific modifier key rather than a
		// neutral one. On the other hand, event.scanCode is the one we need for ToUnicodeEx() calls.
		{
			var script = Script.TheScript;
			state.earlyCollected = true;
			state.used_dead_key_non_destructively = false;
			state.charCount = 0;

			if (keyUp && !CollectKeyUp(extraInfo, vk, sc, true))
				return false;

			// The checks above suppress key-up if key-down was suppressed and the Input is still active.
			// Otherwise, avoid suppressing key-up since it may result in the key getting stuck down.
			// At the very least, this is needed for cases where a user presses a #z hotkey, for example,
			// to initiate an Input.  When the user releases the LWIN/RWIN key during the input, that
			// up-event should not be suppressed otherwise the modifier key would get "stuck down".
			if (keyUp)
				return true;

			var transcribeKey = true;

			// Don't unconditionally transcribe modified keys such as Ctrl-C because calling ToAsciiEx() on
			// some such keys (e.g. Ctrl-LeftArrow or RightArrow if I recall correctly), disrupts the native
			// function of those keys.  That is the reason for the existence of the
			// g_input.TranscribeModifiedKeys option.
			// Fix for v1.0.38: Below now uses kbdMsSender.modifiersLR_logical vs. g_modifiersLR_physical because
			// it's the logical state that determines what will actually be produced on the screen and
			// by ToAsciiEx() below.  This fixes the Input command to properly capture simulated
			// keystrokes even when they were sent via hotkey such #c or a hotstring for which the user
			// might still be holding down a modifier, such as :*:<t>::Test (if '>' requires shift key).
			// It might also fix other issues.
			if ((kbdMsSender.modifiersLRLogical & ~(MOD_LSHIFT | MOD_RSHIFT)) != 0 // At least one non-Shift modifier is down (Shift may also be down).
					&& !((kbdMsSender.modifiersLRLogical & (MOD_LALT | MOD_RALT)) != 0 && (kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) != 0))
			{
				// Since in some keybd layouts, AltGr (Ctrl+Alt) will produce valid characters (such as the @ symbol,
				// which is Ctrl+Alt+Q in the German/IBM layout and Ctrl+Alt+2 in the Spanish layout), an attempt
				// will now be made to transcribe all of the following modifier combinations:
				// - Anything with no modifiers at all.
				// - Anything that uses ONLY the shift key.
				// - Anything with Ctrl+Alt together in it, including Ctrl+Alt+Shift, etc. -- but don't do
				//   "anything containing the Alt key" because that causes weird side-effects with
				//   Alt+LeftArrow/RightArrow and maybe other keys too).
				// Older comment: If any modifiers except SHIFT are physically down, don't transcribe the key since
				// most users wouldn't want that.  An additional benefit of this policy is that registered hotkeys will
				// normally be excluded from the input (except those rare ones that have only SHIFT as a modifier).
				// Note that ToAsciiEx() will translate ^i to a tab character, !i to plain i, and many other modified
				// letters as just the plain letter key, which we don't want.
				for (var input = script.input; ; input = input.prev)
				{
					if (input == null) // No inputs left, and none were found that meet the conditions below.
					{
						transcribeKey = false;
						break;
					}

					// Transcription is done only once for all layers, so do this if any layer requests it:
					if (input.transcribeModifiedKeys && input.InProgress() && input.IsInteresting(extraInfo))
						break;
				}
			}

			// v1.1.28.00: active_window is set to the focused control, if any, so that the hotstring buffer is reset
			// when the focus changes between controls, not just between windows.
			// v1.1.28.01: active_window is left as the active window; the above is not done because it disrupts
			// hotstrings when the first keypress causes a change in focus, such as to enter editing mode in Excel.
			// See Get_active_window_keybd_layout macro definition for related comments.
			var activeWindow = WindowManager.GetForegroundWindowHandle(); // Set default in case there's no focused control.
			nint tempzero = 0;
			var activeWindowKeybdLayout = PlatformManager.GetKeyboardLayout(WindowManager.GetFocusedCtrlThread(ref tempzero, activeWindow));
			state.activeWindow = activeWindow;
			state.keyboardLayout = activeWindowKeybdLayout;

			// SUMMARY OF DEAD KEY ISSUE:
			// Calling ToUnicodeEx() with conventional parameters disrupts the entry of dead keys in two different ways:
			//  1) Passing a dead key buffers it within the keyboard layout's internal state.
			//  2) Passing a live key removes any pending dead key from the keyboard layout's internal state.
			// In either case, the state is then incorrect for the active window's own call to ToUnicodeEx(), so it ends
			// up with something like "e" or "''e" instead of "é".  Originally this was solved by reinserting the pending
			// dead key (first by re-sending the dead key, then later by calling ToUnicodeEx()), but now we use a special
			// combination of parameters to avoid changing the state where possible.

			// Univeral Windows Platform apps apparently have their own handling for dead keys:
			//  - On some OS versions, dead key followed by Esc produces Chr(27), unlike non-UWP apps.
			//  - Pressing a dead key in a UWP app does not leave it in the keyboard layout's buffer,
			//    so to get the correct result here we must translate the dead key again, first.
			//  - Pressing a non-dead key disregards any dead key which was placed into the buffer by
			//    calling ToUnicodeEx, and it is left in the buffer.  To get the correct result for the
			//    next call, the dead key must NOT be left in the buffer.
			//  - Chained dead keys reportedly do not work even without AutoHotkey interfering, but in
			//    case that's fixed (and for simplicity), our translation assumes that it will work.
			// Note that this still applies to some apps on Windows 11 22H2 (such as Feedback Hub) but
			// does not apply to newer apps based on WinUI, such as the Photos app.
			if (uwpHwndChecked != activeWindow)
			{
				uwpHwndChecked = activeWindow;
				char[] className = new char[32];
				int len = GetClassName(activeWindow, className, className.Length);
				uwpAppFocused = string.Compare(new string(className, 0, len), "ApplicationFrameWindow", true) == 0;
			}

			int charCount;
			var ch = new char[8];

			if (vk == VK_PACKET)
			{
				// VK_PACKET corresponds to a SendInput event with the KEYEVENTF_UNICODE flag.
				charCount = 1;// SendInput only supports a single 16-bit character code.
				ch[0] = (char)rawSC; // No translation needed.
			}
			else if (transcribeKey && vk != VK_MENU)
			{
				var keyState = new byte[physicalKeyState.Length];
				bool interfere = pendingDeadKeys.Count > 0 && (pendingDeadKeyInvisible || uwpAppFocused);

				if (interfere)
				{
					// Either an invisible InputHook is in progress or there is a UWP app focused.  In either case, the dead key
					// was only recorded by us and wasn't retained by the keyboard layout's internal state, so we need to "replay"
					// the sequence to set things up for conversion of the new key.
					for (int i = 0; i < pendingDeadKeys.Count; ++i)
					{
						var dead_key = pendingDeadKeys[i];
						AdjustKeyState(keyState, dead_key.modLR);
						keyState[VK_CAPITAL] = (byte)dead_key.caps;
						System.Array.Clear(ch, 0, ch.Length);
						_ = ToUnicode(dead_key.vk, dead_key.sc, keyState, ch, 0, activeWindowKeybdLayout);
					}
				}

				// The documentation for ToAsciiEx is incomplete, but recent documentation for ToUnicodeEx shows the meaning of
				// the flags: 0x1 = Alt+Numpad key combinations are not handled (but the flag doesn't prevent Alt-up itself from
				// being processed), 0x2 = handle key break events (key-up).  We fake key-up to avoid changing the dead key state
				// (it stands to reason that key-down normally causes a change in state, so the corresponding key-up wouldn't).
				// We must avoid passing VK_MENU with KBDBREAK (0x8000) because that disrupts any ongoing Alt+Numpad entry.
				// Note that Windows 10 v1607 supports flag 0x4 to avoid changing the keyboard state, but there seems to be no
				// benefit; in particular, the Alt+Numpad state is still affected.
				// Credit to Ilya Zakharevich for pointing out this method @ https://stackoverflow.com/a/78173420/894589
				var flags = interfere ? 1u : 3u;
				var scanCode = rawSC | (interfere ? 0u : 0x8000u);
				// Provide the correct logical modifier and CapsLock state for any translation below.
				AdjustKeyState(keyState, kbdMsSender.modifiersLRLogical);
				keyState[VK_CAPITAL] = (byte)(IsKeyToggledOn(VK_CAPITAL) ? 1 : 0);
				System.Array.Clear(ch, 0, ch.Length);
				charCount = ToUnicode(vk, scanCode, keyState, ch, flags, activeWindowKeybdLayout);

				if (charCount == 0 && (kbdMsSender.modifiersLRLogical & (MOD_LALT | MOD_RALT)) != 0 && (kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) == 0u && !interfere)
				{
					// Apparently, ToUnicodeEx ignores the Alt in Alt and Alt+Shift combinations only if the key-up bit is not set.
					// For consistency with prior versions (and Win, but not Ctrl/Shift), let the Alt state be ignored under these
					// conditions.  transcribe_key and modifier state checked above imply that the M option was used.
					keyState[VK_MENU] = 0;
					System.Array.Clear(ch, 0, ch.Length);
					charCount = ToUnicode(vk, scanCode, keyState, ch, flags, activeWindowKeybdLayout);
				}

				if (charCount <= 0 && interfere) // A key with no text translation, or possibly a chained dead key (if < 0).
				{
					// Flush the dead key which was buffered either by the ToUnicodeEx call above or the dead key loop further up.
					var ignored = new char[8];

					// Michael S. Kaplan blogged that he would explain in a later post why he used VK_SPACE to clear the buffer,
					// but then changed to using VK_DECIMAL and apparently never explained either choice.  Still, VK_DECIMAL
					// seems like a safe choice for clearing the state; probably any key which produces text will work, but
					// the loop is needed in case of an unconventional layout which makes VK_DECIMAL itself a dead key.
					while (ToUnicode(VK_DECIMAL, 0, keyState, ignored, flags, activeWindowKeybdLayout) == -1) ;
				}

				if (charCount > 0)
				{
					state.used_dead_key_non_destructively = pendingDeadKeys.Count > 0 && !interfere;
					pendingDeadKeys.Clear();
					pendingDeadKeyInvisible = false;
				}
				else if (charCount < 0 && pendingDeadKeys.Count < 3)
				{
					// Record this dead key so that we can reproduce the sequence when needed.
					var deadKey = new DeadKeyRecord()
					{
						vk = vk,
						sc = rawSC,
						modLR = kbdMsSender.modifiersLRLogical,
						caps = keyState[VK_CAPITAL]
					};
					pendingDeadKeys.Add(deadKey);
				}

				if ((kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) == 0) // i.e. must not replace '\r' with '\n' if it is the result of Ctrl+M.
				{
					if (ch.Length > 0)
						if (ch[0] == '\r')  // Translate \r to \n since \n is more typical and useful in Windows.
							ch[0] = '\n';

					if (ch.Length > 1)
						if (ch[1] == '\r')  // But it's never referred to if byte_count < 2
							ch[1] = '\n';
				}
			}
			else
			{
				charCount = 0;
			}

			// If Backspace is pressed after a dead key, ch[0] is the "dead" char and ch[1] is '\b'.
			// Testing shows that this can be handled a number of ways (we only support 1 & 2):
			// 1. Insert ch[0] and then apply backspacing.  This is subtly different from doing nothing
			//    in that if there is a selection, it is deleted.  This appears to be how Edit controls
			//    behave on Windows 11 22H2, 10 22H2 and 7 (in a VM).
			// 2. UWP apps perform backspacing and discard the pending dead key.
			//    (VS2022 does as well, but we don't do anything to support that.)
			// 3. VS2015 performs backspacing and leaves the dead key in the buffer.
			// 4. MarkdownPad 2 prints the dead char as if Space was pressed, and does no backspacing.
			// 5. In 2019, Lexikos noted that Win32 apps performed backspacing and THEN inserted ch[0].
			//    This might have only applied to Windows 10 builds around that time.
			if (vk == VK_BACK && charCount > 0)
			{
				if (uwpAppFocused)
				{
					charCount = 0;
					pendingDeadKeys.Clear();
				}
				else // Assume standard Win32 behavior as described above.
					charCount--;// Remove '\b' to simplify the backspacing and collection stages.
			}

			state.ch = ch;
			state.charCount = charCount;

			if (!CollectInputHook(extraInfo, vk, sc, ch, charCount, true))
				return false; // Suppress.

			return true;//Visible.
		}

		internal override object Invoke(Func<object> f) => thread?.Invoke(() => f());

		internal override bool IsHookThreadRunning() => thread != null && !thread.IsDisposed();

		internal override bool IsHotstringWordChar(char ch)
		// Returns true if aChar would be part of a word if followed by a word char.
		// aChar itself may be a word char or a nonspacing mark which combines with
		// the next character (the first character of a potential hotstring match).
		{
			// IsCharAlphaNumeric is used for simplicity and to preserve old behavior
			// (with the only exception being the one added below), in case it's what
			// users have come to expect.  Note that checking for C1_ALPHA or C3_ALPHA
			// and C1_DIGIT is not equivalent: Michael S. Kaplan wrote that the real
			// conditions are "(C1_ALPHA && ! (C3_HIRAGANA | C3_KATAKANA) || C1_DIGIT)" -- https://web.archive.org/web/20130627015450/http://blogs.msdn.com/b/michkap/archive/2007/06/19/3396819.aspx
			if (IsCharAlphaNumeric(ch))
				return true;

			var char_type = new ushort[1];

			if (GetStringTypeEx(0, CT_CTYPE3, ch.ToString(), 1, char_type))//Ignore locale for unicode by passing 0.
			{
				// Nonspacing marks combine with the following character, so would visually
				// appear to be part of the word.  This should fix detection of words beginning
				// with or containing Arabic nonspacing diacritics, for example.
				if ((char_type[0] & C3_NONSPACING) != 0)
					return true;
			}

			return false;
		}

		internal override bool IsKeyDown(uint vk) => (GetKeyState((int)vk) & 0x8000) != 0;

		internal override bool IsKeyDownAsync(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

		internal override bool IsKeyToggledOn(uint vk) => (GetKeyState((int)vk) & 0x01) != 0;

		internal static bool IsKeybdEventArtificial(uint flags) => (flags & LLKHF_INJECTED) != 0 && (flags & LLKHF_LOWER_IL_INJECTED) == 0;
		internal static bool IsMouseEventArtificial(uint flags) => (flags & LLMHF_INJECTED) != 0 && (flags & LLMHF_LOWER_IL_INJECTED) == 0;

		protected internal override nint CallNextHook(HookEventArgs e) 
			=> CallNextHookEx(e.Hook, e.Code, e.WParam, e.StructPtr);

		protected internal override bool IsMouseMenuVisible()
		{
			var script = Script.TheScript;

			if (script.menuIsVisible != MenuType.None)
				return true;

			var menuHwnd = FindWindow("#32768", null);
			return menuHwnd != 0 && GetWindowThreadProcessId(menuHwnd, out _) == script.ProcessesData.MainThreadID;
		}

		protected internal override void UpdateForegroundWindowData(KeyHistoryItem item, KeyHistory history)
		{
			var hwnd = WindowManager.GetForegroundWindowHandle();

			if (hwnd != 0)
			{
				if (hwnd != history.HistoryHwndPrev)
				{
					var script = Script.TheScript;
					var wnd = Control.FromHandle(hwnd) is Control ctrl ? ctrl : script.mainWindow;

					if (wnd != null)
					{
						wnd.CheckedBeginInvoke(() => item.targetWindow = wnd.Text, false, false);
					}

					// v1.0.44.12: The reason for the above is that clicking a window's close or minimize button
					// (and possibly other types of title bar clicks) causes a delay for the following window, at least
					// when XP Theme (but not classic theme) is in effect:
					//#InstallMouseHook
					//Gui, +AlwaysOnTop
					//Gui, Show, w200 h100
					//return
					// The problem came about from the following sequence of events:
					// 1) User clicks the one of the script's window's title bar's close, minimize, or maximize button.
					// 2) WM_NCLBUTTONDOWN is sent to the window's window proc, which then passes it on to
					//    DefWindowProc or DefDlgProc, which then apparently enters a loop in which no messages
					//    (or a very limited subset) are pumped.
					// 3) If anyone sends a message to that window (such as GetWindowText(), which sends a message
					//    in cases where it doesn't have the title pre-cached), the message will not receive a reply
					//    until after the mouse button is released.
					// 4) But the hook is the very thing that's supposed to release the mouse button, and it can't
					//    until a reply is received.
					// 5) Thus, a deadlock occurs.  So after a short but noticeable delay, the OS sees the hook as
					//    unresponsive and bypasses it, sending the click through normally, which breaks the deadlock.
					// 6) A similar situation might arise when a right-click-down is sent to the title bar or
					//    sys-menu-icon.
					//
					// SOLUTION:
					// Post the message to our main thread to have it do the GetWindowText call.  That way, if
					// the target window is one of the main thread's own window's, there's no chance it can be
					// in an unresponsive state like the deadlock described above.  In addition, do this for ALL
					// windows because its simpler, more maintainable, and especially might solve other hook
					// performance problems if GetWindowText() has other situations where it is slow to return
					// (which seems likely).
					// Although the above solution could create rare situations where there's a lag before window text
					// is updated, that seems unlikely to be common or have significant consequences.  Furthermore,
					// it has the advantage of improving hook performance by avoiding the call to GetWindowText (which
					// incidentally might solve hotkey lag problems that have been observed while the active window
					// is momentarily busy/unresponsive -- but maybe not because the main thread would then be lagged
					// instead of the hook thread, which is effectively the same result from user's POV).
					// Note: It seems best not to post the message to the hook thread because if LButton is down,
					// the hook's main event loop would be sending a message to an unresponsive thread (our main thread),
					// which would create the same deadlock.
					// ALTERNATE SOLUTIONS:
					// - #1: Avoid calling GetWindowText at all when LButton or RButton is in a logically-down state.
					// - Same as #1, but do so only if one of the main thread's target windows is known to be in a tight loop (might be too unreliable to detect all such cases).
					// - Same as #1 but less rigorous and more catch-all, such as by checking if the active window belongs to our thread.
					// - Avoid calling GetWindowText at all upon release of LButton.
					// - Same, but only if the window to have text retrieved belongs to our process.
					// - Same, but only if the mouse is inside the close/minimize/etc. buttons of the active window.
				}
				else // i.e. where possible, avoid the overhead of the call to GetWindowText().
					item.targetWindow = "";
			}
			else
				item.targetWindow = "N/A";// Due to AHK_GETWINDOWTEXT, this could collide with main thread's writing to same string; but in addition to being extremely rare, it would likely be inconsequential.

			history.HistoryHwndPrev = hwnd;  // Updated unconditionally in case hwnd is NULL.
		}

		internal override nint GetAltTabMenuHandle() => FindWindow("#32771", null);

		internal override HookAction CancelAltTabMenu(uint vk, bool keyUp) {
			// v1.0.37.07: Cancel the alt-tab menu upon receipt of Escape so that it behaves like the OS's native Alt-Tab.
			// Even if is_ignored==true, it seems more flexible/useful to cancel the Alt-Tab menu upon receiving
			// an Escape keystroke of any kind.
			// Update: Must not handle Alt-Up here in a way similar to Esc-down in case the hook sent Alt-up to
			// dismiss its own menu. Otherwise, the shift key might get stuck down if Shift-Alt-Tab was in effect.
			// Instead, the release-of-prefix-key section should handle it via its checks of this_key.it_put_shift_down, etc.
			if (vk == VK_ESCAPE && !keyUp)
			{
				// When the alt-tab window is owned by the script (it is owned by csrss.exe unless the script
				// is the process that invoked the alt-tab window), testing shows that the script must be the
				// originator of the Escape keystroke.  Therefore, substitute a simulated keystroke for the
				// user's physical keystroke. It might be necessary to do this even if is_ignored==true because
				// a keystroke from some other script/process might not qualify as a valid means to cancel it.
				// UPDATE for v1.0.39: The escape handler below works only if the hook's thread invoked the
				// alt-tab window, not if the script's thread did via something like "Send {Alt down}{tab down}".
				// This is true even if the process ID is checked instead of the thread ID below.  I think this
				// behavior is due to the window obeying escape only when its own thread sends it.  This
				// is probably done to allow a program to automate the alt-tab menu without interference
				// from Escape keystrokes typed by the user.  Although this could probably be fixed by
				// sending a message to the main thread and having it send the Escape keystroke, it seems
				// best not to do this because:
				// 1) The ability to dismiss a script-invoked alt-tab menu with escape would vary depending on
				//    whether the keyboard hook is installed (i.e. it's inconsistent).
				// 2) It's more flexible to preserve the ability to protect the alt-tab menu from physical
				//    escape keystrokes typed by the user.  The script can simulate an escape key to explicitly
				//    close an alt-tab window it invoked (a simulated escape keystroke can apparently also close
				//    any alt-tab menu, even one invoked by physical keystrokes; but the converse isn't true).
				// 3) Lesser reason: Reduces code size and complexity.
				// UPDATE in 2019: Testing on Windows 7 and 10 indicate this does not apply to the more modern
				// versions of Alt-Tab, but it still applies if the classic Alt-Tab is restored via the registry.
				// However, on these OSes, the user is able to press Esc to dismiss our Alt-Tab.  Other scripts
				// (and presumably other processes) are *NOT* able to dismiss it by simulating Esc.
				nint altTabWindow;

				if ((altTabWindow = GetAltTabMenuHandle()) != 0 // There is an alt-tab window...
						&& GetWindowThreadProcessId(altTabWindow, out _) == PlatformManager.CurrentThreadId()) // ...and it's owned by the hook thread (not the main thread).
				{
					kbdMsSender.SendKeyEvent(KeyEventTypes.KeyDown, VK_ESCAPE);
					// By definition, an Alt key should be logically down if the alt-tab menu is visible (even if it
					// isn't, sending an extra up-event seems harmless).  Releasing that Alt key seems best because:
					// 1) If the prefix key that pushed down the alt key is still physically held down and the user
					//    presses a new (non-alt-tab) suffix key to form a hotkey, it avoids any alt-key disruption
					//    of things such as MouseClick that that subroutine might due.
					// 2) If the user holds down the prefix, presses Escape to dismiss the menu, then presses an
					//    alt-tab suffix, testing shows that the existing alt-tab logic here in the hook will put
					//    alt or shift-alt back down if it needs to.
					kbdMsSender.SendKeyEvent(KeyEventTypes.KeyUp, (kbdMsSender.modifiersLRLogical & MOD_RALT) != 0 ? VK_RMENU : VK_LMENU);
					return HookAction.Suppress;
				}

				// Otherwise, the alt-tab window doesn't exist or (more likely) it's owned by some other process
				// such as crss.exe.  Do nothing extra to avoid interfering with the native function of Escape or
				// any remappings or hotkeys assigned to Escape.  Also, do not set altTabMenuIsVisible to false
				// in any of the cases here because there is logic elsewhere in the hook that does that more
				// reliably; it takes into account things such as whether the Escape keystroke will be suppressed
				// due to being a hotkey).
			}
			return HookAction.Continue;
		}

		internal unsafe nint LowLevelMouseHandler(int code, nint param, ref MSDLLHOOKSTRUCT lParam)
		{
			var script = Script.TheScript;

			// code != HC_ACTION should be evaluated PRIOR to considering the values
			// of wParam and lParam, because those values may be invalid or untrustworthy
			// whenever code < 0.
			if (code != HC_ACTION)
				return CallNextHookEx(mouseHook, code, param, ref lParam);

			// Make all mouse events physical to try to simulate mouse clicks in games that normally ignore
			// artificial input.
			//event.flags &= ~LLMHF_INJECTED;
			var isArtificial = IsMouseEventArtificial(lParam.flags);

			if (!isArtificial) // Physical mouse movement or button action (uses LLMHF vs. LLKHF).
				script.timeLastInputPhysical = script.timeLastInputMouse = DateTime.UtcNow;

			// Above: Don't use event.time, mostly because SendInput can produce invalid timestamps on such events
			// (though in truth, that concern isn't valid because SendInput's input isn't marked as physical).
			// Another concern is the comments at the other update of "g_TimeLastInputPhysical" elsewhere in this file.
			// A final concern is that some drivers might be faulty and might not generate an accurate timestamp.
			var iwParam = param.ToInt32();

			if (iwParam == WM_MOUSEMOVE) // Only after updating for physical input, above, is this checked.
				return (script.KeyboardData.blockMouseMove && !isArtificial) ? new nint(1) : CallNextHookEx(mouseHook, code, param, ref lParam);

			// Above: In v1.0.43.11, a new mode was added to block mouse movement only since it's more flexible than
			// BlockInput (which keybd too, and blocks all mouse buttons too).  However, this mode blocks only
			// physical mouse movement because it seems most flexible (and simplest) to allow all artificial
			// movement, even if that movement came from a source other than an AHK script (such as some other
			// macro program).
			// MSDN: WM_LBUTTONDOWN, WM_LBUTTONUP, WM_MOUSEMOVE, WM_MOUSEWHEEL [, WM_MOUSEHWHEEL], WM_RBUTTONDOWN, or WM_RBUTTONUP.
			// But what about the middle button?  It's undocumented, but it is received.
			// What about doubleclicks (e.g. WM_LBUTTONDBLCLK): I checked: They are NOT received.
			// This is expected because each click in a doubleclick could be separately suppressed by
			// the hook, which would make it become a non-doubleclick.
			var vk = 0u;
			var sc = 0u; // To be overridden if this even is a wheel turn.
			var keyUp = true;  // Set default to safest value.

			var structPtr = (nint)Unsafe.AsPointer(ref lParam);

			switch (iwParam)
			{
				case WM_MOUSEWHEEL:
				case WM_MOUSEHWHEEL:
				{
					// v1.0.48: Lexikos: Support horizontal scrolling in Windows Vista and later.
					// MSDN: "A positive value indicates that the wheel was rotated forward, away from the user;
					// a negative value indicates that the wheel was rotated backward, toward the user. One wheel
					// click is defined as WHEEL_DELTA, which is 120."  Testing shows that on XP at least, the
					// abs(delta) is greater than 120 when the user turns the wheel quickly (also depends on
					// granularity of wheel hardware); i.e. the system combines multiple turns into a single event.
					var wheelDelta = Conversions.HighWord(lParam.mouseData);
					var wheelData = new MouseWheelEventData()
					{
						Type = MouseWheelScrollType.Unit,
						Rotation = wheelDelta,
						Delta = (int)WHEEL_DELTA,
						Direction = iwParam == WM_MOUSEWHEEL ? MouseWheelScrollDirection.Vertical : MouseWheelScrollDirection.Horizontal,
						X = lParam.pt.X,
						Y = lParam.pt.Y
					};

					if (iwParam == WM_MOUSEWHEEL)
						vk = wheelDelta < 0 ? VK_WHEEL_DOWN : VK_WHEEL_UP;
					else
						vk = wheelDelta < 0 ? VK_WHEEL_LEFT : VK_WHEEL_RIGHT;

					sc = (uint)wheelDelta;
					keyUp = false; // Always consider wheel movements to be "key down" events.

					var hookEvent = new MouseWheelHookEventArgs(structPtr, wheelDelta, lParam.flags, isArtificial, lParam.pt.X, lParam.pt.Y, mouseHook, code, param);
					return LowLevelCommon(hookEvent, vk, sc, sc, keyUp, lParam.dwExtraInfo, lParam.flags);
				}
				break;

				case WM_LBUTTONUP:
					vk = VK_LBUTTON; break;

				case WM_RBUTTONUP:
					vk = VK_RBUTTON; break;

				case WM_MBUTTONUP:
					vk = VK_MBUTTON; break;

				case WM_NCXBUTTONUP:  // NC means non-client.
				case WM_XBUTTONUP:
					vk = Conversions.HighWord(lParam.mouseData) == XBUTTON1 ? VK_XBUTTON1 : VK_XBUTTON2; break;

				case WM_LBUTTONDOWN:
					vk = VK_LBUTTON; keyUp = false; break;

				case WM_RBUTTONDOWN:
					vk = VK_RBUTTON; keyUp = false; break;

				case WM_MBUTTONDOWN:
					vk = VK_MBUTTON; keyUp = false; break;

				case WM_NCXBUTTONDOWN:
				case WM_XBUTTONDOWN:
					vk = (Conversions.HighWord(lParam.mouseData) == XBUTTON1) ? VK_XBUTTON1 : VK_XBUTTON2; keyUp = false; break;
			}

			var hookEventDefault = new MouseHookEventArgs(keyUp ? EventType.MouseReleased : EventType.MousePressed, structPtr, lParam.flags, isArtificial, lParam.pt.X, lParam.pt.Y, mouseHook, code, param);
			return LowLevelCommon(hookEventDefault, vk, sc, sc, keyUp, lParam.dwExtraInfo, lParam.flags);
		}

		internal override uint MapScToVk(uint sc)
		{
			// aSC is actually a combination of the last byte of the keyboard make code combined with
			// 0x100 for the extended-key flag.  Although in most cases the flag corresponds to a prefix
			// byte of 0xE0, it seems it's actually set by the KBDEXT flag in the keyboard layout dll
			// (it's hard to find documentation).  A few keys have the KBDEXT flag inverted, which means
			// we can't tell reliably which scan codes really need the 0xE0 prefix, so just handle them
			// as special cases and hope that the flag never varies between layouts.
			// If this approach ever fails for custom layouts, some alternatives are:
			//  - Load the keyboard layout dll manually and check the scan code conversion tables for
			//    the presence of the KBDEXT flag.
			//  - Convert aSC and (aSC ^ 0x100), check the conversion of VK back to SC, and if it
			//    round-trips use that VK instead.
			// However, it seems that neither MSKLC nor KbdEdit provide a means to change the KBDEXT flag.
			// US layout: https://github.com/microsoft/Windows-driver-samples/blob/master/input/layout/kbdus/kbdus.c
			// Keyboard make codes: http://stanislavs.org/helppc/make_codes.html
			// More low-level keyboard details: https://www.win.tue.nl/~aeb/linux/kbd/scancodes-1.html#ss1.5
			switch (sc)
			{
				// RShift doesn't have the 0xE0 prefix but has KBDEXT.  The US layout sample says
				// "Right-hand Shift key must have KBDEXT bit set", so it's probably always set.
				// KbdEdit seems to follow this rule when VK_RSHIFT is assigned to a non-ext key.
				// It's definitely possible to assign RShift a different VK, but 1) it can't be
				// done with MSKLC, and 2) KbdEdit clears the ext flag (so aSC != SC_RSHIFT).
				case RShift:

				// NumLock doesn't have the 0xE0 prefix but has KBDEXT.  Actually pressing the key
				// will produce VK_PAUSE if CTRL is down, but with SC_NUMLOCK rather than SC_PAUSE.
				case NumpadLock:
					// These cases can be handled by adjusting aSC to reflect the fact that these
					// keys don't really have the 0xE0 prefix, and allowing MapVirtualKey() to be
					// called below in case they have been remapped.
					sc &= 0xFF;
					break;

				// Pause actually generates 0xE1,0x1D,0x45, or in other words, E1,LCtrl,NumLock.
				// kbd.h says "We must convert the E1+LCtrl to BREAK, then ignore the Numlock".
				// So 0xE11D maps to and from VK_PAUSE, and 0x45 is "ignored".  However, the hook
				// receives only 0x45, not 0xE11D (which I guess would be truncated to 0x1D/ctrl).
				// The documentation for KbdEdit also indicates the mapping of Pause is "hard-wired":
				// http://www.kbdedit.com/manual/low_level_edit_vk_mappings.html
				case Pause:
					return VK_PAUSE;
			}

			if ((sc & 0x100) != 0) // Our extended-key flag.
			{
				// Since it wasn't handled above, assume the extended-key flag corresponds to the 0xE0
				// prefix byte.  Passing 0xE000 should work on Vista and up, though it appears to be
				// documented only for MapVirtualKeyEx() as at 2019-10-26.  Details can be found in
				// archives of Michael Kaplan's blog (the original blog has been taken down):
				// https://web.archive.org/web/20070219075710/http://blogs.msdn.com/michkap/archive/2006/08/29/729476.aspx
				sc = 0xE000 | (sc & 0xFF);
			}

			return MapVirtualKey(sc, MAPVK_VSC_TO_VK_EX);
		}

		/// <summary>
		/// If caller passes true for aReturnSecondary, the "extended" scan code will be returned for
		/// virtual keys that have two scan codes and two names (if there's only one, callers rely on
		/// zero being returned).  In those cases, the caller may want to know:
		///  a) Whether the hook needs to be used to identify a hotkey defined by name.
		///  b) Whether InputHook should handle the keys by SC in order to differentiate.
		///  c) Whether to retrieve the key's name by SC rather than VK.
		/// In all of those cases, only keys that we've given multiple names matter.
		/// Custom layouts could assign some other VK to multiple SCs, but there would
		/// be no reason (or way) to differentiate them in this context.
		/// </summary>
		/// <param name="vk"></param>
		/// <param name="returnSecondary"></param>
		/// <returns></returns>
		internal override uint MapVkToSc(uint vk, bool returnSecondary = false)
		{
			// Try to minimize the number mappings done manually because MapVirtualKey is a more reliable
			// way to get the mapping if user has non-standard or custom keyboard layout.
			var sc = 0u;

			switch (vk)
			{
				// MapVirtualKey() returns 0xE11D, but we want the code normally received by the
				// hook (sc045).  See sc_to_vk() for more comments.
				case VK_PAUSE: sc = Pause; break;

				// PrintScreen: MapVirtualKey() returns 0x54, which is SysReq (produced by pressing
				// Alt+PrintScreen, but still maps to VK_SNAPSHOT).  Use sc137 for consistency with
				// what the hook reports for the naked keypress (and therefore what a hotkey is
				// likely to need).
				case VK_SNAPSHOT: sc = PrintScreen; break;

				// See comments in sc_to_vk().
				case VK_NUMLOCK: sc = NumpadLock; break;
			}

			if (sc != 0) // Above found a match.
				return returnSecondary ? 0 : sc; // Callers rely on zero being returned for VKs that don't have secondary SCs.

			if ((sc = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX)) == 0u)
				return 0; // Indicate "no mapping".

			if ((sc & 0xE000) != 0u) // Prefix byte E0 or E1 (but E1 should only be possible for Pause/Break, which was already handled above).
				sc = 0x0100 | (sc & 0xFF);

			switch (vk)
			{
				// The following virtual keys have more than one physical key, and thus more than one scan code.
				case VK_RETURN:
				case VK_INSERT:
				case VK_DELETE:
				case VK_PRIOR: // PgUp
				case VK_NEXT:  // PgDn
				case VK_HOME:
				case VK_END:
				case VK_UP:
				case VK_DOWN:
				case VK_LEFT:
				case VK_RIGHT:
					// This is likely to be incorrect for custom layouts where aVK is mapped to two SCs
					// that differ in the low byte.  There seems to be no simple way to fix that;
					// the complex ways would be:
					//  - Build our own conversion table by mapping all SCs to VKs (taking care to detect
					//    changes to the current keyboard layout).  Find the second SC that maps to aVK,
					//    or the first one with the 0xE000 flag.  However, there's no guarantee that it
					//    would correspond to NumpadEnter vs. Enter, or Insert vs. NumpadIns, for example.
					//  - Load the keyboard layout dll manually and search the SC-to-VK conversion tables.
					//    What we actually want is to differentiate Numpad keys from their non-Numpad
					//    counter-parts, and we can do that by checking for the KBDNUMPAD flag.
					// Custom layouts might cause these issues:
					//  - If the scan code of the secondary key is changed, the Hotkey control (and
					//    other sections that don't call this function) may return either "scXXX"
					//    or a name inconsistent with the key's current VK (but if it's a custom
					//    layout + standard keyboard, it should match the key's original function).
					//  - The Hotkey control assumes that the HOTKEYF_EXT flag corresponds to the
					//    secondary key, but either/both/neither could be extended on a custom layout.
					//    If it's both/neither, the control would give no way to distinguish.
					return returnSecondary ? sc | 0x0100 : sc; // Below relies on the fact that these cases return early.

				// See "case SC_RSHIFT:" in sc_to_vk() for comments.
				case VK_RSHIFT:
					sc |= 0x0100;
					break;
			}

			// Since above didn't return, if aReturnSecondary==true, return 0 to indicate "no secondary SC for this VK".
			return returnSecondary ? 0 : sc; // Callers rely on zero being returned for VKs that don't have secondary SCs.
		}

		internal override bool SystemHasAnotherKeybdHook() => SystemHasAnotherHook(ref keybdMutex, KeybdMutexName);

		internal override bool SystemHasAnotherMouseHook() => SystemHasAnotherHook(ref mouseMutex, MouseMutexName);

		internal override void Unhook()
		{
			// PostQuitMessage() might be needed to prevent hang-on-exit.  Once this is done, no message boxes or
			// other dialogs can be displayed.  MSDN: "The exit value returned to the system must be the wParam
			// parameter of the WM_QUIT message."  In our case, PostQuitMessage() should announce the same exit code
			// that we will eventually call exit() with:
			//Original did these, but HookThread.Stop() will take care of it before this is called.
			//WindowsAPI.PostQuitMessage(exitCode);
			AddRemoveHooks(HookType.None); // Remove all hooks. By contrast, registered hotkeys are unregistered below.
			Unhook(Script.TheScript.playbackHook); // Would be unusual for this to be installed during exit, but should be checked for completeness.
			thread?.Dispose();
		}

		internal override void Unhook(nint hook)
		{
			if (hook != 0)
				_ = Invoke(() => _ = UnhookWindowsHookEx(hook));
		}

		protected internal override void DeregisterHooks()
		{
			if (IsReadThreadRunning())
			{
				_ = ChangeHookState(HookType.None, false);
				//var ksmsg = new KeysharpMsg()
				//{
				//  message = (uint)UserMessages.AHK_CHANGE_HOOK_STATE
				//};
				//
				//if (channel.Writer.TryWrite(ksmsg))
				//{
				//  var startTime = DateTime.UtcNow;
				//
				//  while ((DateTime.UtcNow - startTime).TotalMilliseconds < 1000 && !ksmsg.completed)
				//      System.Threading.Thread.Sleep(100); // Should never execute if thread already existed before this function was called.
				//}
			}
		}

		protected internal override void Stop()
		{
			thread?.Dispose();
			base.Stop();
		}

		private bool ChangeHookState(HookType hooksToBeActive, bool changeIsTemporary)//This is going to be a problem if it's ever called to re-add a hook from another thread because only the main gui thread has a message loop.//TODO
		{
			var problem_activating_hooks = false;
			Func<object> func = () =>
			{
				if (((uint)hooksToBeActive & (uint)HookType.Keyboard) != 0) // Activate the keyboard hook (if it isn't already).
				{
					if (kbdHook == 0)
					{
						// v1.0.39: Reset *before* hook is installed to avoid any chance that events can
						// flow into the hook prior to the reset:
						if (!changeIsTemporary) // Sender of msg. is signaling that reset should be done.
							ResetHook(false, HookType.Keyboard, true);

						//Note that in AHK, LowLevelKeybdHandler() is called for every keystroke and runs in its own thread.
						//No matter what we do in C#, it can and must only run on the main window thread.
						//This could potentially be a problem with all of the intricate code that takes special action depending
						//on the thread that a particular function is being called from.
						if ((kbdHook = SetWindowsHookEx(WH_KEYBOARD_LL,
														kbdHandlerDel,//This must be a class member or else it will go out of scope and cause the program to crash unpredictably.
														//GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0)) == 0)
														Marshal.GetHINSTANCE(typeof(Script).Module), 0)) == 0)
							problem_activating_hooks = true;
					}
				}
				else // Caller specified that the keyboard hook is to be deactivated (if it isn't already).
					if (HasKbdHook())
						if (UnhookWindowsHookEx(kbdHook) || GetLastError() == ERROR_INVALID_HOOK_HANDLE)// Check last error in case the OS has already removed the hook.
							kbdHook = 0;

				if (((uint)hooksToBeActive & (uint)HookType.Mouse) != 0) // Activate the mouse hook (if it isn't already).
				{
					if (mouseHook == 0)
					{
						if (!changeIsTemporary) // Sender of msg. is signaling that reset should be done.
							ResetHook(false, HookType.Mouse, true);

						if ((mouseHook = SetWindowsHookEx(WH_MOUSE_LL,
														  mouseHandlerDel,
														  //GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0)) == 0)
														  Marshal.GetHINSTANCE(typeof(Script).Module), 0)) == 0)
							problem_activating_hooks = true;
					}
				}
				else // Caller specified that the mouse hook is to be deactivated (if it isn't already).
					if (mouseHook != 0)
						if (UnhookWindowsHookEx(mouseHook) || GetLastError() == ERROR_INVALID_HOOK_HANDLE)// Check last error in case the OS has already removed the hook.
							mouseHook = 0;

				return DefaultObject;
			};

			//Do this here on the first time through because it's only ever needed if a hook is added.
			if (thread == null && hooksToBeActive > HookType.None)
				thread = new (); //This is needed to ensure that the hooks are run on the main thread.

			//Any modifications to the hooks must be done on the hook thread.
			_ = Invoke(func);
			return problem_activating_hooks;
		}

		private unsafe nint LowLevelKeybdHandler(int code, nint wParam, ref KBDLLHOOKSTRUCT lParam)
		{
			if (code != HC_ACTION)  // MSDN docs specify that both LL keybd & mouse hook should return in this case.
				return CallNextHookEx(kbdHook, code, wParam, ref lParam);

			var wParamVal = wParam.ToInt64();

			// Change the event to be physical if that is indicated in its dwExtraInfo attribute.
			// This is done for cases when the hook is installed multiple times and one instance of
			// it wants to inform the others that this event should be considered physical for the
			// purpose of updating modifier and key states:
			if (lParam.dwExtraInfo == KeyPhysIgnore)
				lParam.flags &= ~LLKHF_INJECTED;
			else if (lParam.dwExtraInfo == KeyBlockThis)
				return new nint(1);

			// Make all keybd events physical to try to fool the system into accepting CTRL-ALT-DELETE.
			// This didn't work, which implies that Ctrl-Alt-Delete is trapped at a lower level than
			// this hook (folks have said that it's trapped in the keyboard driver itself):
			//event.flags &= ~LLKHF_INJECTED;
			// Note: Some scan codes are shared by more than one key (e.g. Numpad7 and NumpadHome).  This is why
			// the keyboard hook must be able to handle hotkeys by either their virtual key or their scan code.
			// i.e. if sc were always used in preference to vk, we wouldn't be able to distinguish between such keys.
			var keyUp = wParamVal == WM_KEYUP || wParamVal == WM_SYSKEYUP;
			var vk = lParam.vkCode;
			var sc = lParam.scanCode;
			var isArtificial = IsKeybdEventArtificial(lParam.flags);

			//if (vk == 'B')
			//{
			//  int xx = 123;
			//}

			//if (vk == 'b')
			//{
			//  int xx = 123;
			//}

			//if (code != 0 && ((lParam.vkCode & VK_LSHIFT) == VK_LSHIFT || (lParam.vkCode & VK_RSHIFT) == VK_RSHIFT))
			//if (wParamVal > 0 && lParam.flags > 0 && lParam.vkCode != 0xA0)// (IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT)))
			//{
			//  Console.WriteLine("shift");
			//}

			if (vk != 0 && sc == 0) // Might happen if another app calls keybd_event with a zero scan code.
				sc = MapVkToSc(vk);

			// MapVirtualKey() does *not* include 0xE0 in HIBYTE if key is extended.  In case it ever
			// does in the future (or if event.scanCode ever does), force sc to be an 8-bit value
			// so that it's guaranteed consistent and to ensure it won't exceed SC_MAX (which might cause
			// array indexes to be out-of-bounds).  The 9th bit is later set to 1 if the key is extended:
			sc &= 0xFF;

			// Change sc to be extended if indicated.  But avoid doing so for VK_RSHIFT, which is
			// apparently considered extended by the API when it shouldn't be.  Update: Well, it looks like
			// VK_RSHIFT really is an extended key, at least on WinXP (and probably be extension on the other
			// NT based OSes as well).  What little info I could find on the 'net about this is contradictory,
			// but it's clear that some things just don't work right if the non-extended scan code is sent.  For
			// example, the shift key will appear to get stuck down in the foreground app if the non-extended
			// scan code is sent with VK_RSHIFT key-up event:
			if ((lParam.flags & LLKHF_EXTENDED) != 0) // && vk != VK_RSHIFT)
				sc |= 0x100;

			// The below must be done prior to any returns that indirectly call UpdateKeybdState() to update
			// modifier state.
			// Update: It seems best to do the below unconditionally, even if the OS is Win2k or WinXP,
			// since it seems like this translation will add value even in those cases:
			// To help ensure consistency with Windows XP and 2k, for which this hook has been primarily
			// designed and tested, translate neutral modifier keys into their left/right specific VKs,
			// since beardboy's testing shows that NT4 receives the neutral keys like Win9x does:
			switch (vk)
			{
				case VK_SHIFT: vk = (sc == RShift) ? VK_RSHIFT : VK_LSHIFT; break;

				case VK_CONTROL: vk = (sc == RControl) ? VK_RCONTROL : VK_LCONTROL; break;

				case VK_MENU: vk = (sc == RAlt) ? VK_RMENU : VK_LMENU; break;
			}

			if (lParam.scanCode == FakeLControl &&  kbdMsSender.altGrExtraInfo != 0 && isArtificial)
			{
				// This LCtrl is a result of sending RAlt, which hasn't been received yet.
				// Override dwExtraInfo, though it will only affect this hook instance.
				lParam.dwExtraInfo = (ulong)kbdMsSender.altGrExtraInfo;
			}

			var structPtr = (nint)Unsafe.AsPointer(ref lParam);
			var hookEvent = new KeyboardHookEventArgs(structPtr, vk, sc, lParam.flags, isArtificial, kbdHook, code, wParam);
			return LowLevelCommon(hookEvent, vk, sc, (uint)lParam.scanCode, keyUp, lParam.dwExtraInfo, lParam.flags);
		}

		//protected internal override void DeregisterMouseHook()
		//{
		//  _ = WindowsAPI.UnhookWindowsHookEx(mouseHook);
		//}
		private bool SystemHasAnotherHook(ref Mutex existingMutex, string name)
		{
			if (existingMutex != null)
				existingMutex.Close(); // But don't set it to NULL because we need its value below as a flag.

			var mutex = new Mutex(false, name, out var _); // Create() vs. Open() has enough access to open the mutex if it exists.
			var last_error = GetLastError();

			// Don't check g_KeybdHook because in the case of aChangeIsTemporary, it might be NULL even though
			// we want a handle to the mutex maintained here.
			if (existingMutex != null) // It was open originally, so update the handle the the newly opened one.
				existingMutex = mutex;
			else if (mutex != null) // Keep it closed because the system tracks how many handles there are, deleting the mutex when zero.
				mutex.Close();  // This facilitates other instances of the program getting the proper last_error value.

			return last_error == ERROR_ALREADY_EXISTS;
		}
	}

	internal enum GuiEventKinds
	{ GUI_EVENTKIND_EVENT = 0, GUI_EVENTKIND_NOTIFY, GUI_EVENTKIND_COMMAND }

	internal enum GuiEventTypes
	{
		GUI_EVENT_NONE  // NONE must be zero for any uses of ZeroMemory(), synonymous with false, etc.
		, GUI_EVENT_DROPFILES, GUI_EVENT_CLOSE, GUI_EVENT_ESCAPE, GUI_EVENT_RESIZE, GUI_EVENT_CONTEXTMENU
		, GUI_EVENT_WINDOW_FIRST = GUI_EVENT_DROPFILES, GUI_EVENT_WINDOW_LAST = GUI_EVENT_CONTEXTMENU
		, GUI_EVENT_CONTROL_FIRST
		, GUI_EVENT_CHANGE = GUI_EVENT_CONTROL_FIRST
		, GUI_EVENT_CLICK, GUI_EVENT_DBLCLK, GUI_EVENT_COLCLK
		, GUI_EVENT_ITEMCHECK, GUI_EVENT_ITEMSELECT, GUI_EVENT_ITEMFOCUS, GUI_EVENT_ITEMEXPAND
		, GUI_EVENT_ITEMEDIT
		, GUI_EVENT_FOCUS, GUI_EVENT_LOSEFOCUS
		, GUI_EVENT_NAMED_COUNT

		// The rest don't have explicit names in GUI_EVENT_NAMES:
		, GUI_EVENT_WM_COMMAND = GUI_EVENT_NAMED_COUNT
	};
}

#endif
