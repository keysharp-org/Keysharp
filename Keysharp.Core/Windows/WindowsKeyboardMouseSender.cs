#if WINDOWS
using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using static Keysharp.Core.Windows.WindowsAPI;
using Keysharp.Core.Common.Mouse;

namespace Keysharp.Core.Windows
{
	[StructLayout(LayoutKind.Explicit)]
	internal struct PlaybackEvent
	{
		[FieldOffset(0)]
		internal uint messagetype;

		[FieldOffset(1)]
		internal ScVk scvk;

		[FieldOffset(1)]
		internal Pt pt;

		[FieldOffset(1)]
		internal uint time_to_wait; // This member is present only when message==0; otherwise, a struct is present.
	}

	/// <summary>
	/// Concrete implementation of KeyboardMouseSender for the Windows platfrom.
	/// </summary>
	internal class WindowsKeyboardMouseSender : KeyboardMouseSender
	{
		internal int currentEvent;
		internal List<PlaybackEvent> eventPb = new (MaxInitialEventsPB);
		internal List<INPUT> eventSi = new (MaxInitialEventsSI);
		internal bool firstCallForThisEvent;
		// sizeof(INPUT) == 28 as of 2006. Since Send is called so often, and since most Sends are short, reducing the load on the stack is also a deciding factor for these.
		// sizeof(PlaybackEvent) == 8, so more events are justified before resorting to malloc().
		internal uint hooksToRemoveDuringSendInput;

		internal long workaroundHitTest;

		private readonly StringBuilder buf = new (4);

		// Below uses a pseudo-random value.  It's best that this be constant so that if multiple instances
		// of the app are running, they will all ignore each other's keyboard & mouse events.  Also, a value
		// close to UINT_MAX might be a little better since it's might be less likely to be used as a pointer
		// value by any apps that send keybd events whose ExtraInfo is really a pointer value.
		//public const uint KEY_IGNORE = 0xFFC3D44F;
		//public const uint KEY_PHYS_IGNORE = (KEY_IGNORE - 1);  // Same as above but marked as physical for other instances of the hook.
		//public const uint KEY_IGNORE_ALL_EXCEPT_MODIFIER = (KEY_IGNORE - 2);  // Non-physical and ignored only if it's not a modifier.
		//public const uint KEY_BLOCK_THIS = (KEY_IGNORE + 1);
		private readonly int[] ctrls = [/*(int)Keys.ShiftKey, */(int)Keys.LShiftKey, (int)Keys.RShiftKey, /*(int)Keys.ControlKey,*/(int)Keys.LControlKey, (int)Keys.RControlKey, (int)Keys.Menu];

		//private static readonly byte[] state = new byte[VKMAX];
		//private readonly nint hookId = 0;
		private bool thisEventHasBeenLogged, thisEventIsScreenCoord;
		//private bool dead;
		//private List<uint> deadKeys;
		//private bool ignore;
		//private nint kbd = GetKeyboardLayout(0);
		//private WindowsAPI.LowLevelKeyboardProc proc;

		private DateTime thisEventTime;

		internal WindowsKeyboardMouseSender()
		{
		}

		internal override bool MouseButtonsSwapped => GetSystemMetrics(SystemMetric.SM_SWAPBUTTON) != 0;

		/// <summary>
		/// Loads and reads the keyboard layout DLL to determine if it has AltGr.
		/// Activates the layout as a side-effect, but reverts it if !aSideEffectsOK.
		/// This is fast enough that there's no need to cache these values on startup.
		/// </summary>
		/// <param name="layout"></param>
		/// <returns></returns>
		internal override ResultType LayoutHasAltGrDirect(nint layout)
		{
			const int KLLF_ALTGR = 0x0001;
			var result = ResultType.Fail;
			var hmod = LoadKeyboardLayoutModule(layout);

			if (hmod != 0)
			{
				var kbdLayerDescriptor = GetProcAddress(hmod, "KbdLayerDescriptor");

				if (kbdLayerDescriptor != 0)
				{
					var func = (KbdTables)Marshal.GetDelegateForFunctionPointer(kbdLayerDescriptor, typeof(KbdTables));
					var kl = func();
					var flags = kl.fLocaleFlags;
					result = (flags & KLLF_ALTGR) != 0 ? ResultType.ConditionTrue : ResultType.ConditionFalse;
				}

				_ = FreeLibrary(hmod);
			}

			return result;
		}

		/// <summary>
		/// AHK says this works for all layouts except one Ukrainian and one North Korean. See line 3940 of keyboard_mouse.cpp
		/// Their solution for those two was far more complex than we'd like to implement here.
		/// So we acknowledge those two layouts won't support AltGr correctly.
		/// Gotten from: https://stackoverflow.com/questions/54588823/detect-if-the-keyboard-layout-has-altgr-on-it-under-windows
		/// </summary>
		/// <param name="layout">The keyboard layout to examine</param>
		/// <returns>True if the layout has AltGr, else false.</returns>
		//private bool LayoutHasAltGr(nint layout)//Unsure if this is usable on linux, where the registry method used below obviously doesn't exist.
		//{
		//  var hasAltGr = false;
		//  for (byte i = 32; i <= 255; ++i)
		//  {
		//      var scancode = WindowsAPI.VkKeyScanEx(i, layout);
		//      if (scancode != -1 && (scancode & 0x600) == 0x600)//Ctrl + Alt means AltGr.
		//      {
		//          hasAltGr = true;
		//          break;
		//      }
		//  }
		//  return hasAltGr;
		//}
		/// <summary>
		/// Loads a keyboard layout DLL and returns its handle.
		/// Activates the layout as a side-effect, but reverts it if !aSideEffectsOK.
		/// </summary>
		/// <param name="layout"></param>
		/// <returns></returns>
		internal static nint LoadKeyboardLayoutModule(nint layout)
		{
			nint hmod = 0;
#if WINDOWS
			// Unfortunately activating the layout seems to be the only way to retrieve it's name.
			// This may have side-effects in general (such as the language selector flickering),
			// but shouldn't have any in our case since we're only changing layouts for our thread,
			// and only if some other window is active (because if our window was active, layout
			// is already the current layout).
			var oldLayout = ActivateKeyboardLayout(layout, 0);

			if (oldLayout != 0)
			{
				var chars = new char[16];

				if (GetKeyboardLayoutName(chars))
				{
					using (var key = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Control\\Keyboard Layouts\\{new string(chars, 0, System.Array.IndexOf(chars, '\0'))}"))
					{
						if (key != null)
						{
							var o = key.GetValue("Layout File");

							if (o is string s)
								hmod = WindowsAPI.LoadLibrary(s);
						}
					}
				}

				if (layout.ToInt32() != oldLayout)
					_ = ActivateKeyboardLayout(new nint(oldLayout), 0); // Nothing we can do if it fails.
			}

#endif
			return hmod;
		}

		internal override void CleanupEventArray(long finalKeyDelay)
		{
			if (sendMode == SendModes.Input)
			{
				if (maxEvents > MaxInitialEventsSI)
					eventSi.Clear();
			}
			else if (sendMode == SendModes.Play)
			{
				if (maxEvents > MaxInitialEventsPB)
					eventPb.Clear();
			}

			// The following must be done only after functions called above are done using it.  But it must also be done
			// prior to our caller toggling capslock back on , to avoid the capslock keystroke from going into the array.
			sendMode = SendModes.Event;
			DoKeyDelay(finalKeyDelay); // Do this only after resetting sSendMode above.  Should be okay for mouse events too.
		}

		internal override nint GetFocusedKeybdLayout(nint window)
		{
			var script = Script.TheScript;

			if (window == 0)
				window = WindowManager.GetForegroundWindowHandle();

			nint tempzero = 0;
			return PlatformManager.GetKeyboardLayout(WindowManager.GetFocusedCtrlThread(ref tempzero, window));
		}

		//internal ResultType ExpandEventArray()
		//{
		//  if (abortArraySend) // A prior call failed (might be impossible).  Avoid malloc() in this case.
		//      return ResultType.Fail;
		internal override void InitEventArray(int maxEvents, uint modifiersLR)
		{
			eventSi.Clear();
			eventPb.Clear();
			base.maxEvents = maxEvents;
			eventModifiersLR = modifiersLR;
			sendInputCursorPos.X = CoordUnspecified;
			sendInputCursorPos.Y = CoordUnspecified;
			hooksToRemoveDuringSendInput = 0;
			abortArraySend = false; // If KeyEvent() ever sets it to true, that allows us to send nothing at all rather than a partial send.
			firstCallForThisEvent = true;
			// The above isn't a local static inside PlaybackProc because PlaybackProc might get aborted in the
			// middle of a NEXT/SKIP pair by user pressing Ctrl-Esc, etc, which would make it unreliable.
		}

		internal override bool MouseClickPreLRButton(KeyEventTypes eventType, uint vk) {
			// v1.0.43 The first line below means: We're not in SendInput/Play mode or we are but this
			// will be the first event inside the array.  The latter case also implies that no initial
			// mouse-move was done above (otherwise there would already be a MouseMove event in the array,
			// and thus the click here wouldn't be the first item).  It doesn't seem necessary to support
			// the MouseMove case above because the workaround generally isn't needed in such situations
			// (see detailed comments below).  Furthermore, if the MouseMove were supported in array-mode,
			// it would require that GetCursorPos() below be conditionally replaced with something like
			// the following (since when in array-mode, the cursor hasn't actually moved *yet*):
			//      CoordToScreen(aX_orig, aY_orig, COORD_MODE_MOUSE);  // Moving mouse relative to the active window.
			// Known limitation: the work-around described below isn't as complete for SendPlay as it is
			// for the other modes: because dragging the title bar of one of this thread's windows with a
			// remap such as F1::LButton doesn't work if that remap uses SendPlay internally (the window
			// gets stuck to the mouse cursor).
			if ((sendMode == SendModes.Event || TotalEventCount() == 0) // See above.
					&& (eventType == KeyEventTypes.KeyDown || (eventType == KeyEventTypes.KeyUp && (workaroundVK != 0)))) // i.e. this is a down-only event or up-only event.
			{
				// v1.0.40.01: The following section corrects misbehavior caused by a thread sending
				// simulated mouse clicks to one of its own windows.  A script consisting only of the
				// following two lines can reproduce this issue:
				// F1::LButton
				// F2::RButton
				// The problems came about from the following sequence of events:
				// 1) Script simulates a left-click-down in the title bar's close, minimize, or maximize button.
				// 2) WM_NCLBUTTONDOWN is sent to the window's window proc, which then passes it on to
				//    DefWindowProc or DefDlgProc, which then apparently enters a loop in which no messages
				//    (or a very limited subset) are pumped.
				// 3) Thus, if the user presses a hotkey while the thread is in this state, that hotkey is
				//    queued/buffered until DefWindowProc/DefDlgProc exits its loop.
				// 4) But the buffered hotkey is the very thing that's supposed to exit the loop via sending a
				//    simulated left-click-up event.
				// 5) Thus, a deadlock occurs.
				// 6) A similar situation arises when a right-click-down is sent to the title bar or sys-menu-icon.
				//
				// The following workaround operates by suppressing qualified click-down events until the
				// corresponding click-up occurs, at which time the click-up is transformed into a down+up if the
				// click-up is still in the same cursor position as the down. It seems preferable to fix this here
				// rather than changing each window proc. to always respond to click-down rather vs. click-up
				// because that would make all of the script's windows behave in a non-standard way, possibly
				// producing side-effects and defeating other programs' attempts to interact with them.
				// (Thanks to Shimanov for this solution.)
				//
				// Remaining known limitations:
				// 1) Title bar buttons are not visibly in a pressed down state when a simulated click-down is sent
				//    to them.
				// 2) A window that should not be activated, such as AlwaysOnTop+Disabled, is activated anyway
				//    by SetForegroundWindowEx().  Not yet fixed due to its rarity and minimal consequences.
				// 3) A related problem for which no solution has been discovered (and perhaps it's too obscure
				//    an issue to justify any added code size): If a remapping such as "F1::LButton" is in effect,
				//    pressing and releasing F1 while the cursor is over a script window's title bar will cause the
				//    window to move slightly the next time the mouse is moved.
				// 4) Clicking one of the script's window's title bar with a key/button that has been remapped to
				//    become the left mouse button sometimes causes the button to get stuck down from the window's
				//    point of view.  The reasons are related to those in #1 above.  In both #1 and #2, the workaround
				//    is not at fault because it's not in effect then.  Instead, the issue is that DefWindowProc enters
				//    a non-msg-pumping loop while it waits for the user to drag-move the window.  If instead the user
				//    releases the button without dragging, the loop exits on its own after a 500ms delay or so.
				// 5) Obscure behavior caused by keyboard's auto-repeat feature: Use a key that's been remapped to
				//    become the left mouse button to click and hold the minimize button of one of the script's windows.
				//    Drag to the left.  The window starts moving.  This is caused by the fact that the down-click is
				//    suppressed, thus the remap's hotkey subroutine thinks the mouse button is down, thus its
				//    auto-repeat suppression doesn't work and it sends another click.
				_ = PlatformManager.GetCursorPos(out var point); // Assuming success seems harmless.
				// Despite what MSDN says, WindowFromPoint() appears to fetch a non-NULL value even when the
				// mouse is hovering over a disabled control (at least on XP).
				nint childUnderCursor, parentUnderCursor;

				if ((childUnderCursor = WindowsAPI.WindowFromPoint(point)) != 0
						&& (parentUnderCursor = WindowsAPI.GetNonChildParent(childUnderCursor)) != 0 // WM_NCHITTEST below probably requires parent vs. child.
						&& GetWindowThreadProcessId(parentUnderCursor, out _) == Script.TheScript.ProcessesData.MainThreadID) // It's one of our thread's windows.
				{
					var hitTest = SendMessage(parentUnderCursor, WM_NCHITTEST, 0, MakeLong((short)point.X, (short)point.Y));

					if (vk == VK_LBUTTON && (hitTest == HTCLOSE || hitTest == HTMAXBUTTON // Title bar buttons: Close, Maximize.
												|| hitTest == HTMINBUTTON || hitTest == HTHELP) // Title bar buttons: Minimize, Help.
							|| vk == VK_RBUTTON && (hitTest == HTCAPTION || hitTest == HTSYSMENU))
					{
						if (eventType == KeyEventTypes.KeyDown)
						{
							// Ignore this event and substitute for it: Activate the window when one
							// of its title bar buttons is down-clicked.
							workaroundVK = vk;
							workaroundHitTest = hitTest;
							_ = WindowItem.SetForegroundWindowEx(WindowManager.CreateWindow(parentUnderCursor)); // Try to reproduce customary behavior.
							// For simplicity, aRepeatCount>1 is ignored and DoMouseDelay() is not done.
							return true;
						}
						else // KEYUP
						{
							if (workaroundHitTest == hitTest) // To weed out cases where user clicked down on a button then released somewhere other than the button.
								eventType = KeyEventTypes.KeyDownAndUp; // Translate this click-up into down+up to make up for the fact that the down was previously suppressed.

							//else let the click-up occur in case it does something or user wants it.
						}
					}
				} // Work-around for sending mouse clicks to one of our thread's own windows.
			}
			return false;
		}

		/// <summary>
		/// Having this part outsourced to a function helps remember to use KEY_IGNORE so that our own mouse
		/// events won't be falsely detected as hotkeys by the hooks (if they are installed).
		/// </summary>
		/// <param name="eventFlags"></param>
		/// <param name="data"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		internal override void MouseEvent(uint eventFlags, uint data, int x = CoordUnspecified, int y = CoordUnspecified)
		{
			if (sendMode != SendModes.Event)
				PutMouseEventIntoArray(eventFlags, data, x, y);
			else
			{
				//KeysharpEnhancements.OutputDebugLine($"Sending mouse_event() with sendlevel {ThreadAccessors.A_SendLevel}");
				mouse_event(eventFlags
							, x == CoordUnspecified ? 0 : x // v1.0.43.01: Must be zero if no change in position is desired
							, y == CoordUnspecified ? 0 : y // (fixes compatibility with certain apps/games).
							, data, new nint(KeyIgnoreLevel(ThreadAccessors.A_SendLevel)));
			}
		}

		internal override int PbEventCount() => eventPb.Count;

		internal nint PlaybackHandler(int code, nint wParam, ref EventMsg lParam)
		// Journal playback hook.
		{
			var script = Script.TheScript;
			var ht = script.HookThread;
			//var lParam = (EventMsg)Marshal.PtrToStructure(lp, typeof(EventMsg));

			switch (code)
			{
				case HC_GETNEXT:
				{
					if (firstCallForThisEvent)
					{
						// Gather the delay(s) for this event, if any, and calculate the time the keystroke should be sent.
						// NOTE: It must be done this way because testing shows that simply returning the desired delay
						// for the first call of each event is not reliable, at least not for the first few events (they
						// tend to get sent much more quickly than specified).  More details:
						// MSDN says, "When the system ...calls the hook procedure [after the first time] with code set to
						// HC_GETNEXT to retrieve the same message... the return value... should be zero."
						// Apparently the above is overly cautious wording with the intent to warn people not to write code
						// that gets stuck in infinite playback due to never returning 0, because returning non-zero on
						// calls after the first works fine as long as 0 is eventually returned.  Furthermore, I've seen
						// other professional code examples that uses this "countdown" approach, so it seems valid.
						firstCallForThisEvent = false;
						thisEventHasBeenLogged = false;
						thisEventIsScreenCoord = false;

						for (thisEventTime = DateTime.UtcNow;
								eventPb[currentEvent].messagetype == 0;// HC_SKIP has ensured there is a non-delay event, so no need to check sCurrentEvent < sEventCount.
								thisEventTime.AddMilliseconds(eventPb[currentEvent++].time_to_wait)) ; // Overflow is okay.
					}

					// Above has ensured that sThisEventTime is valid regardless of whether this is the first call
					// for this event.  It has also incremented sCurrentEvent, if needed, for use below.
					// Copy the current mouse/keyboard event to the EVENTMSG structure (lParam).
					// MSDN says that HC_GETNEXT can be received multiple times consecutively, in which case the
					// same event should be copied into the structure each time.
					var sourceEvent = eventPb[currentEvent];
					var ev = lParam;  // For convenience, maintainability, and possibly performance.
					// Currently, the following isn't documented entirely accurately at MSDN, but other sources confirm
					// the below are the proper values to store.  In addition, the extended flag as set below has been
					// confirmed to work properly by monitoring the resulting WM_KEYDOWN message in a main message loop.
					//
					// Strip off extra bits early for maintainability.  It must be stripped off the source event itself
					// because if HC_GETNEXT is called again for this same event, don't want to apply the offset again.
					bool hasCoordOffset;

					if (hasCoordOffset = ((sourceEvent.messagetype & MSG_OFFSET_MOUSE_MOVE) != 0))
						sourceEvent.messagetype &= ~MSG_OFFSET_MOUSE_MOVE;

					ev.message = sourceEvent.messagetype;
					// The following members are not set because testing confirms that they're ignored:
					// event.hwnd: ignored even if assigned the HWND of an existing window or control.
					// event.time: Apparently ignored in favor of this playback proc's return value.  Furthermore,
					// testing shows that the posted keystroke message (e.g. WM_KEYDOWN) has the correct timestamp
					// even when event.time is left as a random time, which shows that the member is completely
					// ignored during playback, at least on XP.
					bool isKeyboardNotMouse;

					if (isKeyboardNotMouse = (sourceEvent.messagetype >= WM_KEYFIRST && sourceEvent.messagetype <= WM_KEYLAST)) // Keyboard event.
					{
						ev.paramL = (uint)(sourceEvent.scvk.sc << 8) | sourceEvent.scvk.vk;
						ev.paramH = (uint)(sourceEvent.scvk.sc & 0xFF); // 0xFF omits the extended-key-bit, if present.

						if ((sourceEvent.scvk.sc & 0x100) != 0) // It's an extended key.
							ev.paramH |= 0x8000; // So mark it that way using EVENTMSG's convention.

						// Notes about inability of playback to simulate LWin and RWin in a way that performs their native function:
						// For the following reasons, it seems best not to send LWin/RWin via keybd_event inside the playback hook:
						// 1) Complexities such as having to check for an array that consists entirely of LWin/RWin events,
						//    in which case the playback hook mustn't be activated because it requires that we send
						//    at least one event through it.  Another complexity is that all keys modified by Win would
						//    have to be flagged in the array as needing to be sent via keybd_event.
						// 2) It might preserve some flexibility to be able to send LWin/RWin events directly to a window,
						//    similar to ControlSend (perhaps for shells other than Explorer, who might allow apps to make
						//    use of LWin/RWin internally). The window should receive LWIN/RWIN as WM_KEYDOWN messages when
						//    sent via playback.  Note: unlike the neutral SHIFT/ALT/CTRL keys, which are detectible via the
						//    target thread's call to GetKeyState(), LWin and RWin aren't detectible that way.
						// 3) Code size and complexity.
						//
						// Related: LWin and RWin are released and pressed down during playback for simplicity and also
						// on the off-chance the target window takes note of the incoming WM_KEYDOWN on VK_LWIN/RWIN and
						// changes state until the up-event is received (however, the target thread's call of GetKeyState
						// can't see a any effect for hook-sent LWin/RWin).
						//
						// Related: If LWin or RWin is logically down at start of SendPlay, SendPlay's events won't be
						// able to release it from the POV of the target thread's calls to GetKeyState().  That might mess
						// things up for apps that check the logical state of the Win keys.  But due to rarity: in those
						// cases, a workaround would be to do an explicit old-style Send {Blind} (as the first line of the
						// hotkey) to release the modifier logically prior to SendPlay commands.
						//
						// Related: Although some apps might not like receiving SendPlay's LWin/RWin if shell==Explorer
						// (since there may be no normal way for such keystrokes to arrive as WM_KEYDOWN events) maybe it's
						// best not to omit/ignore LWin/RWin if it is possible in other shells, or adds flexibility.
						// After all, sending {LWin/RWin} via hook should be rare, especially if it has no effect (except
						// for cases where a Win hotkey releases LWin as part of SendPlay, but even that can be worked
						// around via an explicit Send {Blind}{LWin up} beforehand).
					}
					else // MOUSE EVENT.
					{
						// Unlike keybd_event() and SendInput(), explicit coordinates must be specified for each mouse event.
						// The builder of this array must ensure that coordinates are valid or set to COORD_UNSPECIFIED_SHORT.
						if (sourceEvent.pt.x == CoordUnspecifiedShort || hasCoordOffset)
						{
							// For simplicity with calls such as CoordToScreen(), the one who set up this array has ensured
							// that both X and Y are either COORD_UNSPECIFIED_SHORT or not so (i.e. not a combination).
							// Since the user nor anything else can move the cursor during our playback, GetCursorPos()
							// should accurately reflect the position set by any previous mouse-move done by this playback.
							// This seems likely to be true even for DirectInput games, though hasn't been tested yet.
							if (PlatformManager.GetCursorPos(out var cursor))
							{
								ev.paramL = (uint)cursor.X;
								ev.paramH = (uint)cursor.Y;
							}

							if (hasCoordOffset) // The specified coordinates are offsets to be applied to the cursor's current position.
							{
								ev.paramL += (uint)(sourceEvent.pt.x);
								ev.paramH += (uint)(sourceEvent.pt.y);
								// Update source array in case HC_GETNEXT is called again for this same event, in which case
								// don't want to apply the offset again (the has-offset flag has already been removed from the
								// source event higher above).
								sourceEvent.pt.x = (short)ev.paramL;
								sourceEvent.pt.y = (short)ev.paramH;
								thisEventIsScreenCoord = true; // Mark the above as absolute vs. relative in case HC_GETNEXT is called again for this event.
							}
						}
						else
						{
							ev.paramL = (uint)sourceEvent.pt.x;
							ev.paramH = (uint)sourceEvent.pt.y;

							if (!thisEventIsScreenCoord) // Coordinates are relative to the window that is active now (during realtime playback).
							{
								var tx = (int)ev.paramL;
								var ty = (int)ev.paramH;
								CoordToScreen(ref tx, ref ty, CoordMode.Mouse);   // Playback uses screen coords.
								ev.paramL = (uint)tx;
								ev.paramH = (uint)ty;
							}
						}
					}

					var timeUntilEvent = (thisEventTime - DateTime.UtcNow).TotalMilliseconds; // Cast to int to avoid loss of negatives from DWORD subtraction.

					if (timeUntilEvent > 0)
						return new nint((int)timeUntilEvent);

					// Otherwise, the event is scheduled to occur immediately (or is overdue).  In case HC_GETNEXT can be
					// called multiple times even when we previously returned 0, ensure the event is logged only once.
					if (!thisEventHasBeenLogged && isKeyboardNotMouse) // Mouse events aren't currently logged for consistency with other send methods.
					{
						// The event is logged here rather than higher above so that its timestamp is accurate.
						// It's also so that events aren't logged if the user cancel's the operation in the middle
						// (by pressing Ctrl-Alt-Del or Ctrl-Esc).
						if (ht.keyHistory is KeyHistory kh)
							kh.UpdateKeyEventHistory(sourceEvent.messagetype == WM_KEYUP || sourceEvent.messagetype == WM_SYSKEYUP, sourceEvent.scvk.vk, sourceEvent.scvk.sc);

						thisEventHasBeenLogged = true;
					}

					return 0; // No CallNextHookEx(). See comments further below.
				} // case HC_GETNEXT.

				case HC_SKIP: // Advance to the next mouse/keyboard event, if any.
					// Advance to the next item, which is either a delay or an event (preps for next HC_GETNEXT).
					++currentEvent;
					// Although caller knows it has to do the tail-end delay (if any) since there's no way to
					// do a trailing delay at the end of playback, it may have put a delay at the end of the
					// array anyway for code simplicity.  For that reason and maintainability:
					// Skip over any delays that are present to discover if there is a next event.
					int u;

					for (u = currentEvent; u < eventPb.Count && eventPb[u].messagetype == 0; ++u) ;

					if (u == eventPb.Count) // No more events.
					{
						// MSDN implies in the following statement that it's acceptable (and perhaps preferable in
						// the case of a playback hook) for the hook to unhook itself: "The hook procedure can be in the
						// state of being called by another thread even after UnhookWindowsHookEx returns."
						script.HookThread.Unhook(script.playbackHook);
						script.playbackHook = 0; // Signal the installer of the hook that it's gone now.
						// The following is an obsolete method from pre-v1.0.44.  Do not reinstate it without adding handling
						// to MainWindowProc() to do "g_PlaybackHook = NULL" upon receipt of WM_CANCELJOURNAL.
						// PostMessage(g_hWnd, WM_CANCELJOURNAL, 0, 0); // v1.0.44: Post it to g_hWnd vs. NULL because it's a little safer (SEE COMMENTS in MsgSleep's WM_CANCELJOURNAL for why it's almost completely safe with NULL).
						// Above: By using WM_CANCELJOURNAL instead of a custom message, the creator of this hook can easily
						// use a message filter to watch for both a system-generated removal of the hook (via the user
						// pressing Ctrl-Esc. or Ctrl-Alt-Del) or one we generate here (though it's currently not implemented
						// that way because it would prevent journal playback to one of our thread's own windows).
					}
					else
						firstCallForThisEvent = true; // Reset to prepare for next HC_GETNEXT.

					return 0; // MSDN: The return value is used only if the hook code is HC_GETNEXT; otherwise, it is ignored.

				default:
					// Covers the following cases:
					//case HC_NOREMOVE: // MSDN: An application has called the PeekMessage function with wRemoveMsg set to PM_NOREMOVE, indicating that the message is not removed from the message queue after PeekMessage processing.
					//case HC_SYSMODALON:  // MSDN: A system-modal dialog box is being displayed. Until the dialog box is destroyed, the hook procedure must stop playing back messages.
					//case HC_SYSMODALOFF: // MSDN: A system-modal dialog box has been destroyed. The hook procedure must resume playing back the messages.
					//case(...aCode < 0...): MSDN docs specify that the hook should return in this case.
					//
					// MS gives some sample code at http://support.microsoft.com/default.aspx?scid=KB;EN-US;124835
					// about the proper values to return to avoid hangs on NT (it seems likely that this implementation
					// is compliant enough if you read between the lines).  Their sample code indicates that
					// "return CallNextHook()"  should be done for basically everything except HC_SKIP/HC_GETNEXT, so
					// as of 1.0.43.08, that is what is done here.
					// Testing shows that when a so-called system modal dialog is displayed (even if it isn't the
					// active window) playback stops automatically, probably because the system doesn't call the hook
					// during such times (only a "MsgBox 4096" has been tested so far).
					//
					// The first parameter uses g_PlaybackHook rather than NULL because MSDN says it's merely
					// "currently ignored", but in the older "Win32 hooks" article, it says that the behavior
					// may change in the future.
					return CallNextHookEx(script.playbackHook, code, wParam, ref lParam);
					// Except for the cases above, CallNextHookEx() is not called for performance and also because from
					// what I can tell from the MSDN docs and other examples, it is neither required nor desirable to do so
					// during playback's SKIP/GETNEXT.
					// MSDN: The return value is used only if the hook code is HC_GETNEXT; otherwise, it is ignored.
			} // switch().

			// Execution should never reach since all cases do their own custom return above.
		}

		/// <summary>
		/// This function is designed to be called from only one thread (the main thread) since it's not thread-safe.
		/// Playback hook only supports sending neutral modifiers.  Caller must ensure that any left/right modifiers
		/// such as VK_RCONTROL are translated into neutral (e.g. VK_CONTROL).
		/// </summary>
		/// <param name="keyAsModifiersLR"></param>
		/// <param name="vk"></param>
		/// <param name="sc"></param>
		/// <param name="eventFlags"></param>
		/// <param name="extraInfo"></param>
		internal override void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo)
		{
			var key_up = (eventFlags & KEYEVENTF_KEYUP) != 0;

			// To make the SendPlay method identical in output to the other keystroke methods, have it generate
			// a leading down/up LControl event immediately prior to each RAlt event (with no key-delay).
			// This avoids having to add special handling to places like SetModifierLRState() to do AltGr things
			// differently when sending via playback vs. other methods.  The event order recorded by the journal
			// record hook is a little different than what the low-level keyboard hook sees, but I don't think
			// the order should matter in this case:
			//   sc  vk key  msg
			//   138 12 Alt  syskeydown (right vs. left scan code)
			//   01d 11 Ctrl keydown (left scan code) <-- In keyboard hook, normally this precedes Alt, not follows it. Seems inconsequential (testing confirms).
			//   01d 11 Ctrl keyup  (left scan code)
			//   138 12 Alt  syskeyup (right vs. left scan code)
			// Check for VK_MENU not VK_RMENU because caller should have translated it to neutral:
			if (vk == VK_MENU && sc == ScanCodes.RAlt && targetLayoutHasAltGr == ResultType.ConditionTrue && sendMode == SendModes.Play)
				// Must pass VK_CONTROL rather than VK_LCONTROL because playback hook requires neutral modifiers.
				PutKeybdEventIntoArray(MOD_LCONTROL, VK_CONTROL, ScanCodes.LControl, eventFlags, extraInfo); // Recursive call to self.

			// Above must be done prior to the capacity check below because above might add a new array item.

			// Keep track of the predicted modifier state for use in other places:
			if (key_up)
				eventModifiersLR &= ~keyAsModifiersLR;
			else
				eventModifiersLR |= keyAsModifiersLR;

			if (sendMode == SendModes.Input)
			{
				var thisEvent = new INPUT();
				thisEvent.type = INPUT_KEYBOARD;
				thisEvent.i.k.wVk = (ushort)vk;
				thisEvent.i.k.wScan = (ushort)((eventFlags & KEYEVENTF_UNICODE) != 0 ? sc : sc & 0xFF);
				thisEvent.i.k.dwFlags = eventFlags;
				thisEvent.i.k.dwExtraInfo = (ulong)extraInfo; // Although our hook won't be installed (or won't detect, in the case of playback), that of other scripts might be, so set this for them.
				thisEvent.i.k.time = 0; // Let the system provide its own timestamp, which might be more accurate for individual events if this will be a very long SendInput.
				eventSi.Add(thisEvent);
				hooksToRemoveDuringSendInput |= HookKeyboard; // Presence of keyboard hook defeats uninterruptibility of keystrokes.
			}
			else // Playback hook.
			{
				var thisEvent = new PlaybackEvent();

				if (vk == 0 && sc == 0)//Caller is signaling that aExtraInfo contains a delay/sleep event.
				{
					// Although delays at the tail end of the playback array can't be implemented by the playback
					// itself, caller wants them put in too.
					thisEvent.messagetype = 0; // Message number zero flags it as a delay rather than an actual event.
					thisEvent.time_to_wait = (uint)extraInfo;
				}
				else // A normal (non-delay) event for playback.
				{
					// By monitoring incoming events in a message/event loop, the following key combinations were
					// confirmed to be WM_SYSKEYDOWN vs. WM_KEYDOWN (up events weren't tested, so are assumed to
					// be the same as down-events):
					// Alt+Win
					// Alt+Shift
					// Alt+Capslock/Numlock/Scrolllock
					// Alt+AppsKey
					// Alt+F2/Delete/Home/End/Arrow/BS
					// Alt+Space/Enter
					// Alt+Numpad (tested all digits & most other keys, with/without Numlock ON)
					// F10 (by itself) / Win+F10 / Alt+F10 / Shift+F10 (but not Ctrl+F10)
					// By contrast, the following are not SYS: Alt+Ctrl, Alt+Esc, Alt+Tab (the latter two
					// are never received by msg/event loop probably because the system intercepts them).
					// So the rule appears to be: It's a normal (non-sys) key if Alt isn't down and the key
					// isn't F10, or if Ctrl is down. Though a press of the Alt key itself is a syskey unless Ctrl is down.
					// Update: The release of ALT is WM_KEYUP vs. WM_SYSKEYUP when it modified at least one key while it was down.
					if ((eventModifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) != 0 // Control is down...
							|| (eventModifiersLR & (MOD_LALT | MOD_RALT)) == 0   // ... or: Alt isn't down and this key isn't Alt or F10...
							&& vk != VK_F10 && ((keyAsModifiersLR & (MOD_LALT | MOD_RALT)) == 0)
							|| (((eventModifiersLR & (MOD_LALT | MOD_RALT)) != 0) && key_up))//... or this is the release of Alt (for simplicity, assume that Alt modified something while it was down).
						thisEvent.messagetype = (uint)(key_up ? WM_KEYUP : WM_KEYDOWN);
					else
						thisEvent.messagetype = (uint)(key_up ? WM_SYSKEYUP : WM_SYSKEYDOWN);

					thisEvent.scvk.vk = (byte)vk;
					thisEvent.scvk.sc = (ushort)sc; // Don't omit the extended-key-bit because it is used later on.
				}

				eventPb.Add(thisEvent);
			}
		}

		/// <summary>
		/// This function is designed to be called from only one thread (the main thread) since it's not thread-safe.
		/// If the array-type is journal playback, caller should include MOUSEEVENTF_ABSOLUTE in eventFlags if the
		/// the mouse coordinates aX and aY are relative to the screen rather than the active window.
		/// </summary>
		/// <param name="eventFlags"></param>
		/// <param name="data"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		internal override void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y)
		{
			if (sendMode == SendModes.Input)
			{
				var thisEvent = new INPUT();
				var sendLevel = ThreadAccessors.A_SendLevel;
				thisEvent.type = INPUT_MOUSE;
				thisEvent.i.m.dx = (x == CoordUnspecified) ? 0 : x; // v1.0.43.01: Must be zero if no change in position is
				thisEvent.i.m.dy = (y == CoordUnspecified) ? 0 : y; // desired (fixes compatibility with certain apps/games).
				thisEvent.i.m.dwFlags = eventFlags;
				thisEvent.i.m.mouseData = data;
				thisEvent.i.m.dwExtraInfo = (ulong)KeyIgnoreLevel(sendLevel); // Although our hook won't be installed (or won't detect, in the case of playback), that of other scripts might be, so set this for them.
				thisEvent.i.m.time = 0; // Let the system provide its own timestamp, which might be more accurate for individual events if this will be a very long SendInput.
				eventSi.Add(thisEvent);
				hooksToRemoveDuringSendInput |= HookMouse; // Presence of mouse hook defeats uninterruptibility of mouse clicks/moves.
			}
			else // Playback hook.
			{
				// Note: Delay events (sleeps), which are supported in playback mode but not SendInput, are always inserted
				// via PutKeybdEventIntoArray() rather than this function.
				var thisEvent = new PlaybackEvent();

				// Determine the type of event specified by caller, but also omit MOUSEEVENTF_MOVE so that the
				// follow variations can be differentiated:
				// 1) MOUSEEVENTF_MOVE by itself.
				// 2) MOUSEEVENTF_MOVE with a click event or wheel turn (in this case MOUSEEVENTF_MOVE is permitted but
				//    not required, since all mouse events in playback mode must have explicit coordinates at the
				//    time they're played back).
				// 3) A click event or wheel turn by itself (same remark as above).
				// Bits are isolated in what should be a future-proof way (also omits MSG_OFFSET_MOUSE_MOVE bit).
				switch (eventFlags & (0x1FFF & ~(int)MOUSEEVENTF.MOVE)) // v1.0.48: 0x1FFF vs. 0xFFF to support MOUSEEVENTF_HWHEEL.
				{
					case 0: thisEvent.messagetype = WM_MOUSEMOVE; break; // It's a movement without a click.

					// In cases other than the above, it's a click or wheel turn with optional WM_MOUSEMOVE too.
					case (uint)MOUSEEVENTF.LEFTDOWN: thisEvent.messagetype = WM_LBUTTONDOWN; break;

					case (uint)MOUSEEVENTF.LEFTUP: thisEvent.messagetype = WM_LBUTTONUP; break;

					case (uint)MOUSEEVENTF.RIGHTDOWN: thisEvent.messagetype = WM_RBUTTONDOWN; break;

					case (uint)MOUSEEVENTF.RIGHTUP: thisEvent.messagetype = WM_RBUTTONUP; break;

					case (uint)MOUSEEVENTF.MIDDLEDOWN: thisEvent.messagetype = WM_MBUTTONDOWN; break;

					case (uint)MOUSEEVENTF.MIDDLEUP: thisEvent.messagetype = WM_MBUTTONUP; break;

					case (uint)MOUSEEVENTF.XDOWN: thisEvent.messagetype = WM_XBUTTONDOWN; break;

					case (uint)MOUSEEVENTF.XUP: thisEvent.messagetype = WM_XBUTTONUP; break;

					case (uint)MOUSEEVENTF.WHEEL: thisEvent.messagetype = WM_MOUSEWHEEL; break;

					case (uint)MOUSEEVENTF.HWHEEL: thisEvent.messagetype = WM_MOUSEHWHEEL; break; // v1.0.48
						// WHEEL: No info comes into journal-record about which direction the wheel was turned (nor by how many
						// notches).  In addition, it appears impossible to specify such info when playing back the event.
						// Therefore, playback usually produces downward wheel movement (but upward in some apps like
						// Visual Studio).
				}

				// COORD_UNSPECIFIED_SHORT is used so that the very first event can be a click with unspecified
				// coordinates: it seems best to have the cursor's position fetched during playback rather than
				// here because if done here, there might be time for the cursor to move physically before
				// playback begins (especially if our thread is preempted while building the array).
				thisEvent.pt.x = (x == CoordUnspecified) ? CoordUnspecifiedShort : (short)x;
				thisEvent.pt.y = (y == CoordUnspecified) ? CoordUnspecifiedShort : (short)y;

				if ((eventFlags & MsgOffsetMouseMove) != 0) // Caller wants this event marked as a movement relative to cursor's current position.
					thisEvent.messagetype |= MsgOffsetMouseMove;

				eventPb.Add(thisEvent);
			}
		}

		/// <summary>
		/// aFinalKeyDelay (which the caller should have initialized to -1 prior to calling) may be changed here
		/// to the desired/final delay.  Caller must not act upon it until changing sTypeOfHookToBuild to something
		/// that will allow DoKeyDelay() to do a real delay.
		/// </summary>
		/// <param name=""></param>
		/// <param name=""></param>
		/// <param name="modsDuringSend"></param>
		internal override void SendEventArray(ref long finalKeyDelay, uint modsDuringSend)
		{
			var script = Script.TheScript;
			var ht = script.HookThread;

			if (sendMode == SendModes.Input)
			{
				//if (eventSi.Count == 0)
				//return;
				// Remove hook(s) temporarily because the presence of low-level (LL) keybd hook completely disables
				// the uninterruptibility of SendInput's keystrokes (but the mouse hook doesn't affect them).
				// The converse is also true.  This was tested via:
				//  #space::
				//  SendInput {Click 400, 400, 100}
				//  MsgBox
				//  ExitApp
				// ... and also with BurnK6 running, a CPU maxing utility.  The mouse clicks were sent directly to the
				// BurnK6 window, and were pretty slow, and also I could physically move the mouse cursor a little
				// between each of sendinput's mouse clicks.  But removing the mouse-hook during SendInputs solves all that.
				// Rather than removing both hooks unconditionally, it's better to
				// remove only those that actually have corresponding events in the array.  This avoids temporarily
				// losing visibility of physical key states (especially when the keyboard hook is removed).
				HookType activeHooks;

				if ((activeHooks = ht.GetActiveHooks()) != HookType.None)
					ht.AddRemoveHooks((HookType)((int)activeHooks & ~hooksToRemoveDuringSendInput), true);

				_ = SendInput((uint)eventSi.Count, eventSi.ToArray(), Marshal.SizeOf(typeof(INPUT))); // Must call dynamically-resolved version for Win95/NT compatibility.

				// The return value is ignored because it never seems to be anything other than sEventCount, even if
				// the Send seems to partially fail (e.g. due to hitting 5000 event maximum).
				// Typical speed of SendInput: 10ms or less for short sends (under 100 events).
				// Typically 30ms for 500 events; and typically no more than 200ms for 5000 events (which is
				// the apparent max).
				// Testing shows that when SendInput returns to its caller, all of its key states are in effect
				// even if the target window hasn't yet had time to receive them all.  For example, the
				// following reports that LShift is down:
				//   SendInput {a 4900}{LShift down}
				//   MsgBox % GetKeyState("LShift")
				// Furthermore, if the user manages to physically press or release a key during the call to
				// SendInput, testing shows that such events are in effect immediately when SendInput returns
				// to its caller, perhaps because SendInput clear out any backlog of physical keystrokes prior to
				// returning, or perhaps because the part of the OS that updates key states is a very high priority.
				if (activeHooks != HookType.None)
				{
					if (((int)activeHooks & hooksToRemoveDuringSendInput & HookKeyboard) != 0) // Keyboard hook was actually removed during SendInput.
					{
						// The above call to SendInput() has not only sent its own events, it has also emptied
						// the buffer of any events generated outside but during the SendInput.  Since such
						// events are almost always physical events rather than simulated ones, it seems to do
						// more good than harm on average to consider any such changes to be physical.
						// The g_PhysicalKeyState array is also updated by GetModifierLRState(true), but only
						// for the modifier keys, not for all keys on the keyboard.  Even if adjust all keys
						// is possible, it seems overly complex and it might impact performance more than it's
						// worth given the rarity of the user changing physical key states during a SendInput
						// and then wanting to explicitly retrieve that state via GetKeyState(Key, "P").
						var modsCurrent = GetModifierLRState(true); // This also serves to correct the hook's logical modifiers, since hook was absent during the SendInput.
						var modsChangedPhysicallyDuringSend = modsDuringSend ^ modsCurrent;
						modifiersLRPhysical &= ~(modsChangedPhysicallyDuringSend & modsDuringSend); // Remove those that changed from down to up.
						modifiersLRPhysical |= modsChangedPhysicallyDuringSend & modsCurrent; // Add those that changed from up to down.
						modifiersLRLogical = modifiersLRLogicalNonIgnored = modsCurrent; // Necessary for hotkeys to be recognized correctly if modifiers were sent.
						ht.hsHwnd = GetForegroundWindow(); // An item done by ResetHook() that seems worthwhile here.
						// Most other things done by ResetHook() seem like they would do more harm than good to reset here
						// because of the the time the hook is typically missing is very short, usually under 30ms.
					}

					ht.AddRemoveHooks(activeHooks, true); // Restore the hooks that were active before the SendInput.
				}

				return;
			}

			// Since above didn't return, sSendMode == SM_PLAY.
			// It seems best not to call IsWindowHung() here because:
			// 1) It might improve script reliability to allow playback to a hung window because even though
			//    the entire system would appear frozen, if the window becomes unhung, the keystrokes would
			//    eventually get sent to it as intended (and the script may be designed to rely on this).
			//    Furthermore, the user can press Ctrl-Alt-Del or Ctrl-Esc to unfreeze the system.
			// 2) It might hurt performance.
			//
			// During journal playback, it appears that LL hook receives events in realtime; its just that
			// keystrokes the hook passes through (or generates itself) don't actually hit the active window
			// until after the playback is done.  Preliminary testing shows that the hook's disguise of Alt/Win
			// still function properly for Win/Alt hotkeys that use the playback method.
			currentEvent = 0; // Reset for use by the hook below.  Should be done BEFORE the hook is installed in the next line.
			/*  JOURNAL_RECORD_MODE
			            // To record and analyze events via the above:
			            // - Uncomment the line that defines this in the header file.
			            // - Put breakpoint after the hook removes itself (a few lines below).  Don't try to put breakpoint in RECORD hook
			            //   itself because it tends to freeze keyboard input (must press Ctrl-Alt-Del or Ctrl-Esc to unfreeze).
			            // - Have the script send a keystroke (best to use non-character keystroke such as SendPlay {Shift}).
			            // - It is now recording, so press the desired keys.
			            // - Press Ctrl+Break, Ctrl-Esc, or Ctrl-Alt-Del to stop recording (which should then hit breakpoint below).
			            // - Study contents of the sEventPB array, which contains the keystrokes just recorded.
			            eventCount = 0; // Used by RecordProc().

			            if ((script.TheScript.playbackHook = SetWindowsHookEx(WH_JOURNALRECORD, RecordProc, WindowsAPI.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0)) == 0)
			                return;

			*/
			_ = ht.Invoke(() => Script.TheScript.playbackHook = SetWindowsHookEx(WH_JOURNALPLAYBACK, PlaybackHandler, Marshal.GetHINSTANCE(typeof(Script).Module), 0));

			if (script.playbackHook == 0)
				return;

			// During playback, have the keybd hook (if it's installed) block presses of the Windows key.
			// This is done because the Windows key is about the only key (other than Ctrl-Esc or Ctrl-Alt-Del)
			// that takes effect immediately rather than getting buffered/postponed until after the playback.
			// It should be okay to set this after the playback hook is installed because playback shouldn't
			// actually begin until we have our thread do its first MsgSleep later below.
			ht.blockWinKeys = true;

			// Otherwise, hook is installed, so:
			// Wait for the hook to remove itself because the script should not be allowed to continue
			// until the Send finishes.
			// GetMessage(single_msg_filter) can't be used because then our thread couldn't playback
			// keystrokes to one of its own windows.  In addition, testing shows that it wouldn't
			// measurably improve performance anyway.
			// Note: User can't activate tray icon with mouse (because mouse is blocked), so there's
			// no need to call our main event loop merely to have the tray menu responsive.
			// Sleeping for 0 performs at least 15% worse than INTERVAL_UNSPECIFIED. I think this is
			// because the journal playback hook can operate only when this thread is in a message-pumping
			// state, and message pumping is far more efficient with GetMessage than PeekMessage.
			// Also note that both registered and hook hotkeys are noticed/caught during journal playback
			// (confirmed through testing).  However, they are kept buffered until the Send finishes
			// because ACT_SEND and such are designed to be uninterruptible by other script threads;
			// also, it would be undesirable in almost any conceivable case.
			//
			// Use a loop rather than a single call to MsgSleep(WAIT_FOR_MESSAGES) because
			// WAIT_FOR_MESSAGES is designed only for use by WinMain().  The loop doesn't measurably
			// affect performance because there used to be the following here in place of the loop,
			// and it didn't perform any better:
			// GetMessage(&msg, NULL, WM_CANCELJOURNAL, WM_CANCELJOURNAL);
			while (script.playbackHook != 0)
				Flow.SleepWithoutInterruption(Flow.intervalUnspecified); // For maintainability, macro is used rather than optimizing/splitting the code it contains.

			ht.blockWinKeys = false;

			// Either the hook unhooked itself or the OS did due to Ctrl-Esc or Ctrl-Alt-Del.
			// MSDN: When an application sees a [system-generated] WM_CANCELJOURNAL message, it can assume
			// two things: the user has intentionally cancelled the journal record or playback mode,
			// and the system has already unhooked any journal record or playback hook procedures.
			if (eventPb.Count > 0 && eventPb[eventPb.Count - 1].messagetype == 0) // Playback hook can't do the final delay, so we do it here.
				finalKeyDelay = (int)eventPb[eventPb.Count - 1].time_to_wait;// Don't do delay right here because the delay would be put into the array instead.

			// GetModifierLRState(true) is not done because keystrokes generated by the playback hook
			// aren't really keystrokes in the sense that they affect global key state or modifier state.
			// They affect only the keystate retrieved when the target thread calls GetKeyState()
			// (GetAsyncKeyState can never see such changes, even if called from the target thread).
			// Furthermore, the hook (if present) continues to operate during journal playback, so it
			// will keep its own modifiers up-to-date if any physical or simulate keystrokes happen to
			// come in during playback (such keystrokes arrive in the hook in real time, but they don't
			// actually hit the active window until the playback finishes).
		}

		internal override void AttachTargetWindowThread(ref bool threadsAreAttached, ref uint keybdLayoutThread, ref WindowItemBase tempitem, nint targetWindow)
		{
			var pd = TheScript.ProcessesData;
			tempitem = WindowManager.CreateWindow(targetWindow);
			uint targetThread;

			if ((targetThread = GetWindowThreadProcessId(targetWindow, out _)) != 0 // Assign.
					&& targetThread != pd.MainThreadID && !tempitem.IsHung)
			{
				threadsAreAttached = AttachThreadInput(pd.MainThreadID, targetThread, true);
				keybdLayoutThread = targetThread; // Testing shows that ControlSend benefits from the adapt-to-layout technique too.
			}

			//else no target thread, or it's our thread, or it's hung; so keep keybd_layout_thread at its default.
		}

		internal override void DetachTargetWindowThread(uint mainThread, uint targetThread)
		{
			_ = AttachThreadInput(mainThread, targetThread, false);
		}

		internal override void SendCharToTargetWindow(char ch, nint targetWindow)
		{
			_ = WindowsAPI.PostMessage(targetWindow, WM_CHAR, ch, 0);
		}

		internal override void SendUnicodeChar(char ch, uint modifiers)
		{
			// Set modifier keystate as specified by caller.  Generally this will be 0, since
			// key combinations with Unicode packets either do nothing at all or do the same as
			// without the modifiers.  All modifiers are known to interfere in some applications.
			SetModifierLRState(modifiers, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState(), 0, false, true, KeyIgnore);
			var sendLevel = ThreadAccessors.A_SendLevel;

			if (sendMode == SendModes.Input)
			{
				// Calling SendInput() now would cause characters to appear out of sequence.
				// Instead, put them into the array and allow them to be sent in sequence.
				PutKeybdEventIntoArray(0, 0, ch, KEYEVENTF_UNICODE, KeyIgnoreLevel(sendLevel));
				PutKeybdEventIntoArray(0, 0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, KeyIgnoreLevel(sendLevel));
				return;
			}

			//else caller has ensured sendMode is SM_EVENT. In that mode, events are sent one at a time,
			// so it is safe to immediately call SendInput(). SM_PLAY is not supported; for simplicity,
			// SendASC() is called instead of this function. Although this means Unicode chars probably
			// won't work, it seems better than sending chars out of order. One possible alternative could
			// be to "flush" the event array, but since SendInput and SendEvent are probably much more common,
			// this is left for a future version.
			var uInput = new INPUT[2];
			uInput[0].type = INPUT_KEYBOARD;
			uInput[0].i.k.wVk = 0;
			uInput[0].i.k.wScan = ch;
			uInput[0].i.k.dwFlags = KEYEVENTF_UNICODE;
			uInput[0].i.k.time = 0;
			// L25: Set dwExtraInfo to ensure AutoHotkey ignores the event; otherwise it may trigger a SCxxx hotkey (where xxx is u_code).
			uInput[0].i.k.dwExtraInfo = (ulong)KeyIgnoreLevel(sendLevel);
			uInput[1].type = INPUT_KEYBOARD;
			uInput[1].i.k.wVk = 0;
			uInput[1].i.k.wScan = ch;
			uInput[1].i.k.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
			uInput[1].i.k.time = 0;
			uInput[1].i.k.dwExtraInfo = (ulong)KeyIgnoreLevel(sendLevel);
			_ = SendInput(2, uInput, 40);// sizeof(INPUT));
		}

		internal override int SiEventCount() => eventSi.Count;

		/// <summary>
		/// Toggle the given vk into another state.  For performance, it is the caller's responsibility to
		/// ensure that vk is a toggleable key such as capslock, numlock, insert, or scrolllock.
		/// Returns the state the key was in before it was changed.
		/// </summary>
		/// <param name="vk"></param>
		/// <param name="toggleValue"></param>
		/// <returns></returns>
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
			// Fix for v1.0.40: IsKeyToggledOn()'s call to GetKeyState() relies on our thread having
			// processed messages.  Confirmed necessary 100% of the time if our thread owns the active window.
			// v1.0.43: Extended the above fix to include all toggleable keys (not just Capslock) and to apply
			// to both directions (ON and OFF) since it seems likely to be needed for them all.
			bool ourThreadIsForeground;

			if (ourThreadIsForeground = GetWindowThreadProcessId(GetForegroundWindow(), out var _) == script.ProcessesData.MainThreadID) // GetWindowThreadProcessId() tolerates a NULL hwnd.
				Flow.SleepWithoutInterruption(-1);

			if (vk == VK_CAPITAL && toggleValue == ToggleValueType.Off && script.HookThread.IsKeyToggledOn(vk))
			{
				// Fix for v1.0.36.06: Since it's Capslock and it didn't turn off as attempted, it's probably because
				// the OS is configured to turn Capslock off only in response to pressing the SHIFT key (via Ctrl Panel's
				// Regional settings).  So send shift to do it instead:
				SendKeyEvent(KeyEventTypes.KeyDownAndUp, VK_SHIFT);

				if (ourThreadIsForeground) // v1.0.43: Added to try to achieve 100% reliability in all situations.
					Flow.SleepWithoutInterruption(-1); // Check msg queue to put SHIFT's turning off of Capslock into effect from our thread's POV.
			}

			return startingState;
		}

		protected internal override void LongOperationUpdate()
		{
			var msg = new Msg();
			var now = DateTime.UtcNow;
			var script = Script.TheScript;

			if ((now - script.lastPeekTime).TotalMilliseconds > ThreadAccessors.A_PeekFrequency)
			{
				if (PeekMessage(out msg, 0, 0, 0, PM_NOREMOVE))
					_ = Flow.Sleep(-1);

				now = DateTime.UtcNow;
				script.lastPeekTime = now;
			}
		}

		/// <summary>
		/// Same as the above except for SendKeys() and related functions (uses SLEEP_WITHOUT_INTERRUPTION vs. MsgSleep).
		/// </summary>
		protected internal override void LongOperationUpdateForSendKeys()
		{
			var msg = new Msg();
			var now = DateTime.UtcNow;
			var script = Script.TheScript;

			if ((now - script.lastPeekTime).TotalMilliseconds > ThreadAccessors.A_PeekFrequency)
			{
				if (PeekMessage(out msg, 0, 0, 0, PM_NOREMOVE))
					Flow.SleepWithoutInterruption(-1);

				now = DateTime.UtcNow;
				script.lastPeekTime = now;
			}
		}

		/*
		    protected internal override void Send(string keys)
		    {
		    if (keys.Length == 0)
		        return;

		    var len = keys.Length * 2;
		    var inputs = new INPUT[len];

		    for (var i = 0; i < keys.Length; i++)
		    {
		        uint flag = WindowsAPI.KEYEVENTF_UNICODE;

		        if ((keys[i] & 0xff00) == 0xe000)
		            flag |= WindowsAPI.KEYEVENTF_EXTENDEDKEY;

		        var down = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		        down.i.k = new KEYBDINPUT { wScan = keys[i], dwFlags = flag };
		        var up = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		        up.i.k = new KEYBDINPUT { wScan = keys[i], dwFlags = flag | WindowsAPI.KEYEVENTF_KEYUP };
		        var x = i * 2;
		        inputs[x] = down;
		        inputs[x + 1] = up;
		    }

		    ignore = true;
		    _ = WindowsAPI.SendInput((uint)len, inputs, Marshal.SizeOf(typeof(INPUT)));
		    ignore = false;
		    }
		*/

		/*
		    protected internal override void Send(Keys key)
		    {
		    //This is supposed to prevent modifer keys currently pressed from applying to the key which is sent, but it doesn't seem to work.
		    key &= ~Keys.Modifiers;

		    if (key == Keys.None)
		        return;

		    uint flag = WindowsAPI.KEYEVENTF_UNICODE;
		    var vk = (ushort)key;

		    if ((vk & 0xff00) == 0xe000)
		        flag |= WindowsAPI.KEYEVENTF_EXTENDEDKEY;

		    var down = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		    down.i.k = new KEYBDINPUT
		    {
		        wVk = vk,//Was
		        //wVk = 0,
		        //wScan = vk,
		        dwFlags = flag
		    };
		    var up = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		    up.i.k = new KEYBDINPUT
		    {
		        wVk = vk,//Was
		        //wVk = 0,
		        //wScan = vk,
		        dwFlags = flag | WindowsAPI.KEYEVENTF_KEYUP
		    };
		    var inputs = new[] { down, up };
		    ignore = true;
		    _ = WindowsAPI.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
		    ignore = false;
		    }
		*/

		internal override void SendKeyEventToTargetWindow(KeyEventTypes eventType, uint vk, uint sc = 0u, nint targetWindow = default, bool doKeyDelay = false, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			var ht = Script.TheScript.HookThread;
			bool? b = null;

			if (ht.KeyToModifiersLR(vk, sc, ref b) != 0)
			{
				// When sending modifier keystrokes directly to a window, use the AutoIt3 SetKeyboardState()
				// technique to improve the reliability of changes to modifier states.  If this is not done,
				// sometimes the state of the SHIFT key (and perhaps other modifiers) will get out-of-sync
				// with what's intended, resulting in uppercase vs. lowercase problems (and that's probably
				// just the tip of the iceberg).  For this to be helpful, our caller must have ensured that
				// our thread is attached to aTargetWindow's (but it seems harmless to do the below even if
				// that wasn't done for any reason).  Doing this here in this function rather than at a
				// higher level probably isn't best in terms of performance (e.g. in the case where more
				// than one modifier is being changed, the multiple calls to Get/SetKeyboardState() could
				// be consolidated into one call), but it is much easier to code and maintain this way
				// since many different functions might call us to change the modifier state:
				var state = keyStatePool.Rent(256);//Original did not clear state here, so presumably GetKeyboardState() overwrites all elements.
				_ = GetKeyboardState(state);

				if (eventType == KeyEventTypes.KeyDown)
					state[vk] |= 0x80;
				else if (eventType == KeyEventTypes.KeyUp)
					state[vk] &= 0x7F;//This is probably better, no error.

				//state[vk] &= ~0x80;//Compiler says this is an error.
				// else KEYDOWNANDUP, in which case it seems best (for now) not to change the state at all.
				// It's rarely if ever called that way anyway.

				// If vk is a left/right specific key, be sure to also update the state of the neutral key:
				switch (vk)
				{
					case VK_LCONTROL:
					case VK_RCONTROL:
						if ((state[VK_LCONTROL] & 0x80) != 0 || (state[VK_RCONTROL] & 0x80) != 0)
							state[VK_CONTROL] |= 0x80;
						else
							state[VK_CONTROL] &= 0x7F;

						break;

					case VK_LSHIFT:
					case VK_RSHIFT:
						if ((state[VK_LSHIFT] & 0x80) != 0 || (state[VK_RSHIFT] & 0x80) != 0)
							state[VK_SHIFT] |= 0x80;
						else
							state[VK_SHIFT] &= 0x7F;

						break;

					case VK_LMENU:
					case VK_RMENU:
						if ((state[VK_LMENU] & 0x80) != 0 || (state[VK_RMENU] & 0x80) != 0)
							state[VK_MENU] |= 0x80;
						else
							state[VK_MENU] &= 0x7F;

						break;
				}

				_ = SetKeyboardState(state);
				keyStatePool.Return(state);
				// Even after doing the above, we still continue on to send the keystrokes
				// themselves to the window, for greater reliability (same as AutoIt3).
			}

			// lowest 16 bits: repeat count: always 1 for up events, probably 1 for down in our case.
			// highest order bits: 11000000 (0xC0) for keyup, usually 00000000 (0x00) for keydown.
			var lParam = (long)(sc << 16);

			if (eventType != KeyEventTypes.KeyUp)  // i.e. always do it for KEYDOWNANDUP
				_ = WindowsAPI.PostMessage(targetWindow, WM_KEYDOWN, vk, (uint)(lParam | 0x00000001));

			// The press-duration delay is done only when this is a down-and-up because otherwise,
			// the normal g->KeyDelay will be in effect.  In other words, it seems undesirable in
			// most cases to do both delays for only "one half" of a keystroke:
			if (doKeyDelay && eventType == KeyEventTypes.KeyDownAndUp)
				DoKeyDelay(ThreadAccessors.A_KeyDuration); // Since aTargetWindow!=NULL, sendMode!=SM_PLAY, so no need for to ever use the SendPlay press-duration.

			if (eventType != KeyEventTypes.KeyDown)
				_ = WindowsAPI.PostMessage(targetWindow, WM_KEYUP, vk, (uint)(lParam | 0xC0000001));
		}

		internal override void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint flags, long extraInfo)
		{
			var scLowByte = sc & 0xFF;
			var eventFlags = ((sc >> 8) & 0xFF) != 0 ? KEYEVENTF_EXTENDEDKEY : 0u;
			if ((eventType  == KeyEventTypes.KeyDown) || (eventType  == KeyEventTypes.KeyDownAndUp))
				keybd_event((byte)vk, (byte)scLowByte, eventFlags, (uint)extraInfo);
			
			if ((eventType == KeyEventTypes.KeyUp) || (eventType  == KeyEventTypes.KeyDownAndUp))
			{
				eventFlags |= KEYEVENTF_KEYUP;
				keybd_event((byte)vk, (byte)scLowByte, eventFlags, (uint)extraInfo);
			}
		}

		//protected override void Backspace(int length)
		//{
		//  length *= 2;
		//  var inputs = new INPUT[length];
		//
		//  for (var i = 0; i < length; i += 2)
		//  {
		//      var down = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		//      down.i.k = new KEYBDINPUT { wVk = VK_BACK };
		//      var up = new INPUT { type = WindowsAPI.INPUT_KEYBOARD };
		//      up.i.k = new KEYBDINPUT { wVk = VK_BACK, dwFlags = WindowsAPI.KEYEVENTF_KEYUP };
		//      inputs[i] = down;
		//      inputs[i + 1] = up;
		//  }
		//
		//  ignore = true;
		//  _ = WindowsAPI.SendInput((uint)length, inputs, Marshal.SizeOf(typeof(INPUT)));
		//  ignore = false;
		//}

		//protected override void DeregisterHook()
		//{
		//  _ = WindowsAPI.UnhookWindowsHookEx(hookId);
		//}
		protected override void RegisterHook()
		{
		}

		/// <summary>
		/// Convert the specified screen coordinates to mouse event coordinates (MOUSEEVENTF_ABSOLUTE).
		/// MSDN: "In a multimonitor system, [MOUSEEVENTF_ABSOLUTE] coordinates map to the primary monitor."
		/// The above implies that values greater than 65535 or less than 0 are appropriate, but only on
		/// multi-monitor systems.  For simplicity, performance, and backward compatibility, no check for
		/// multi-monitor is done here. Instead, the system's default handling for out-of-bounds coordinates
		/// is used; for example, mouse_event() stops the cursor at the edge of the screen.
		/// UPDATE: In v1.0.43, the following formula was fixed (actually broken, see below) to always yield an
		/// in-range value. The previous formula had a +1 at the end:
		/// aX|Y = ((65535 * aX|Y) / (screen_width|height - 1)) + 1;
		/// The extra +1 would produce 65536 (an out-of-range value for a single-monitor system) if the maximum
		/// X or Y coordinate was input (e.g. x-position 1023 on a 1024x768 screen).  Although this correction
		/// seems inconsequential on single-monitor systems, it may fix certain misbehaviors that have been reported
		/// on multi-monitor systems. Update: according to someone I asked to test it, it didn't fix anything on
		/// multimonitor systems, at least those whose monitors vary in size to each other.  In such cases, he said
		/// that only SendPlay or DllCall("SetCursorPos") make mouse movement work properly.
		/// FIX for v1.0.44: Although there's no explanation yet, the v1.0.43 formula is wrong and the one prior
		/// to it was correct; i.e. unless +1 is present below, a mouse movement to coords near the upper-left corner of
		/// the screen is typically off by one pixel (only the y-coordinate is affected in 1024x768 resolution, but
		/// in other resolutions both might be affected).
		/// v1.0.44.07: The following old formula has been replaced:
		/// (((65535 * coord) / (width_or_height - 1)) + 1)
		/// ... with the new one below.  This is based on numEric's research, which indicates that mouse_event()
		/// uses the following inverse formula internally:
		/// x_or_y_coord = (x_or_y_abs_coord * screen_width_or_height) / 65536
		/// </summary>
		/// <param name="coord"></param>
		/// <param name="width_or_height"></param>
		/// <returns></returns>
		internal override int MouseCoordToAbs(int coord, int width_or_height) => ((65536 * coord) / width_or_height) + (coord < 0 ? -1 : 1);

		/*  JOURNAL_RECORD_MODE
		        internal nint PlaybackProc(int nCode, nint wParam, ref KBDLLHOOKSTRUCT lParam)
		        {
		            switch (aCode)
		            {
		                case HC_ACTION:
		                {
		                    EVENTMSG& event = *(PEVENTMSG) lParam;
		                    PlaybackEvent& dest_event = sEventPB[sEventCount];
		                    dest_event.message = event.message;

		                    if (event.message >= WM_MOUSEFIRST && event.message <= WM_MOUSELAST) // Mouse event, including wheel.
		                    {
		                        if (event.message != WM_MOUSEMOVE)
		                        {
		                            // WHEEL: No info comes in about which direction the wheel was turned (nor by how many notches).
		                            // In addition, it appears impossible to specify such info when playing back the event.
		                            // Therefore, playback usually produces downward wheel movement (but upward in some apps like
		                            // Visual Studio).
		                            dest_event.x = event.paramL;
		                            dest_event.y = event.paramH;
		                            ++sEventCount;
		                        }
		                    }
		                    else // Keyboard event.
		                    {
		                        dest_event.vk = event.paramL & 0x00FF;
		                        dest_event.sc = (event.paramL & 0xFF00) >> 8;

		                        if (event.paramH & 0x8000) // Extended key.
		                            dest_event.sc |= 0x100;

		                        if (dest_event.vk == VK_CANCEL) // Ctrl+Break.
		                        {
		                            UnhookWindowsHookEx(g_PlaybackHook);
		                            g_PlaybackHook = NULL; // Signal the installer of the hook that it's gone now.
		                            // Obsolete method, pre-v1.0.44:
		                            //PostMessage(g_hWnd, WM_CANCELJOURNAL, 0, 0); // v1.0.44: Post it to g_hWnd vs. NULL so that it isn't lost when script is displaying a MsgBox or other dialog.
		                        }

		                        ++sEventCount;
		                    }

		                    break;
		                }

		                    //case HC_SYSMODALON:  // A system-modal dialog box is being displayed. Until the dialog box is destroyed, the hook procedure must stop playing back messages.
		                    //case HC_SYSMODALOFF: // A system-modal dialog box has been destroyed. The hook procedure must resume playing back the messages.
		                    //  break;
		            }

		            // Unlike the playback hook, it seems more correct to call CallNextHookEx() unconditionally so that
		            // any other journal record hooks can also record the event.  But MSDN is quite vague about this.
		            return CallNextHookEx(g_PlaybackHook, aCode, wParam, lParam);
		            // Return value is ignored, except possibly when aCode < 0 (MSDN is unclear).
		        }
		*/
		/*
		    private string MapKey(uint vk, uint sc)
		    {
		    _ = buf.Clear();
		    _ = WindowsAPI.GetKeyboardState(state);

		    foreach (var key in ctrls)
		    {
		        const byte d = 0x80;
		        const byte u = d - 1;
		        //var s = WindowsAPI.GetKeyState(key) >> 8 != 0;
		        var s = WindowsAPI.GetAsyncKeyState(key) >> 8 != 0;
		        state[key] &= s ? d : u;

		        if (s)
		        {
		            if ((Keys)key == Keys.LShiftKey || (Keys)key == Keys.RShiftKey)//For some reason, neither GetKeyboardState() or GetKeyState() properly sets these.
		                state[(int)Keys.ShiftKey] = 0x80;
		            else if ((Keys)key == Keys.LControlKey || (Keys)key == Keys.RControlKey)
		                state[(int)Keys.ControlKey] = 0x80;
		        }
		    }

		    _ = WindowsAPI.ToUnicodeEx(vk, sc, state, buf, buf.Capacity, 0, kbd);
		    return buf.ToString();
		    }
		*/
		/*
		    private void ScanDeadKeys()
		    {
		    //var kbd = WindowsAPI.GetKeyboardLayout(0);
		    _ = buf.Clear();
		    deadKeys = new List<uint>();

		    for (var i = 0u; i < VKMAX; i++)
		    {
		        var result = WindowsAPI.ToUnicodeEx(i, 0, state, buf, buf.Capacity, 0, kbd);

		        if (result == -1)
		            deadKeys.Add(i);
		    }
		    }
		*/

		private delegate KBDTABLES64 KbdTables();

		/*  can also try this from https://stackoverflow.com/questions/318777/c-sharp-how-to-translate-virtual-keycode-to-char/38787314#38787314

		    public string KeyCodeToUnicode(Keys key)
		    {
		    byte[] keyboardState = new byte[255];
		    bool keyboardStateStatus = GetKeyboardState(keyboardState);

		    if (!keyboardStateStatus)
		    {
		        return DefaultObject;
		    }

		    uint virtualKeyCode = (uint)key;
		    uint scanCode = MapVirtualKey(virtualKeyCode, 0);
		    nint inputLocaleIdentifier = GetKeyboardLayout(0);

		    StringBuilder result = new StringBuilder();
		    ToUnicodeEx(virtualKeyCode, scanCode, keyboardState, result, (int)5, (uint)0, inputLocaleIdentifier);

		    return result.ToString();
		    }

		    [DllImport("user32.dll")]
		    static extern bool GetKeyboardState(byte[] lpKeyState);

		    [DllImport("user32.dll")]
		    static extern uint MapVirtualKey(uint uCode, uint uMapType);

		    [DllImport("user32.dll")]
		    static extern nint GetKeyboardLayout(uint idThread);

		    [DllImport("user32.dll")]
		    static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

		*/
	}
}

#endif
