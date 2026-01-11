using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using static Keysharp.Core.Common.Mouse.MouseUtils;
using Keysharp.Core.Common.Mouse;

namespace Keysharp.Core.Common.Keyboard
{
	/// <summary>
	/// Screen coordinates, which can be negative.  SHORT vs. INT is used because the likelihood
	/// have having a virtual display surface wider or taller than 32,767 seems too remote to
	/// justify increasing the struct size, which would impact the stack space and dynamic memory
	/// used by every script every time it uses the playback method to send keystrokes or clicks.
	/// Note: WM_LBUTTONDOWN uses WORDs vs. SHORTs, but they're not really comparable because
	/// journal playback/record both use screen coordinates but WM_LBUTTONDOWN et. al. use client
	/// coordinates.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal struct Pt
	{
		internal short x, y;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct ScVk
	{
		internal ushort sc; // Placed above vk for possibly better member stacking/alignment.
		internal byte vk;
	}

	/// <summary>
	/// Platform independent keyboard Hook base.
	/// This Class is abstract.
	/// </summary>
	internal abstract class KeyboardMouseSender
	{
		//Need to figure out if these should be signed or not. Weird bugs can happen with wraparound comparisons if you get it wrong.//TODO
		internal const int CoordCentered = int.MinValue + 1;

		internal const int CoordModeCaret = 6;
		internal const int CoordModeClient = 0;
		internal const int CoordModeInvalid = -1;
		internal const int CoordModeMask = 3;
		internal const int CoordModeMenu = 8;
		internal const int CoordModeMouse = 2;
		internal const int CoordUnspecified = int.MinValue;
		internal const short CoordUnspecifiedShort = short.MinValue;
		internal const int HookFail = 0xFF;
		internal const int HookKeyboard = 0x01;
		internal const int HookMouse = 0x02;
		internal const long KeyBlockThis = KeyIgnore + 1;

		// Below uses a pseudo-random value.It's best that this be constant so that if multiple instances
		// of the app are running, they will all ignore each other's keyboard & mouse events.  Also, a value
		// close to UINT_MAX might be a little better since it's might be less likely to be used as a pointer
		// value by any apps that send keybd events whose ExtraInfo is really a pointer value.
		internal const long KeyIgnore = 0xFFC3D44F;

		internal const long KeyIgnoreAllExceptModifier = KeyIgnore - 2;
		internal const long KeyIgnoreMax = KeyIgnore;
		internal const long KeyPhysIgnore = KeyIgnore - 1;
		internal const long MaxMouseSpeed = 100L;
		internal const string ModLRString = "<^>^<!>!<+>+<#>#";
		internal const uint MsgOffsetMouseMove = 0x80000000;
		internal const long SendLevelMax = 100L;
		internal const int StateDown = 0x80;
		internal const int StateOn = 0x01;

		// These two are ignored in non-Windows platforms but defined here for cross-platform compatibility.
		internal const int MaxInitialEventsPB = 1500;
		internal const int MaxInitialEventsSI = 500;
		internal static char[] bracechars = "{}".ToCharArray();
		internal static string[] CoordModes = ["Client", "Window", "Screen"];
		internal static char[] llChars = "Ll".ToCharArray();
		internal bool abortArraySend = false;
		internal long altGrExtraInfo = 0L;
		internal SearchValues<char> bracecharsSv = SearchValues.Create(bracechars);
		internal SearchValues<char> llCharsSv = SearchValues.Create(llChars);
		internal int maxEvents = 0;
		internal uint eventModifiersLR;
		internal uint modifiersLRCtrlAltDelMask = 0u;
		internal uint modifiersLRLastPressed = 0u;
		internal DateTime modifiersLRLastPressedTime = DateTime.UtcNow;
		internal uint modifiersLRLogical = 0u;
		internal uint modifiersLRLogicalNonIgnored = 0u;
		internal uint modifiersLRNumpadMask = 0u;
		internal uint modifiersLRPhysical = 0u;
		internal KeyType prefixKey = null;
		internal string sendKeyChars = "^+!#{}";
		internal uint thisHotkeyModifiersLR;
		internal bool triedKeyUp;
		internal bool inBlindMode;
		internal uint modifiersLRPersistent;
		internal uint modifiersLRRemapped;
		protected ArrayPool<byte> keyStatePool = ArrayPool<byte>.Create(256, 100);
		protected SendModes sendMode = SendModes.Event;//Note this is different than the one in Accessors and serves as a temporary.
		private const int retention = 1024;
		private readonly StringBuilder caser = new (32);
		private readonly List<HotkeyDefinition> hotkeys;
		private readonly List<HotstringDefinition> hotstrings;
		private readonly Dictionary<Keys, bool> pressed;

		internal uint prevEventModifierDown;
		internal KeyEventTypes prevEventType;
		internal uint prevVK;

		internal nint targetKeybdLayout;
		// Set by SendKeys() for use by the functions it calls directly and indirectly.
		internal ResultType targetLayoutHasAltGr;
		private readonly List<CachedLayoutType> cachedLayouts = new (10);

		internal uint menuMaskKeySC = ScanCodes.LControl;
		internal uint menuMaskKeyVK = VK_CONTROL;

		//Tracks/predicts cursor position as SendInput array is built.
		internal uint workaroundVK;

		//Tracks cursor position over multiple calls to SendInput
		internal POINT sendInputCursorPos;

		internal class CachedLayoutType
		{
			internal ResultType has_altgr = ResultType.Fail;
			internal nint hkl = 0;
		};

		public KeyboardMouseSender()
		{
			hotkeys = [];
			hotstrings = [];
			pressed = [];

			foreach (int i in Enum.GetValues(typeof(Keys)))
				_ = pressed.TryAdd((Keys)i, false);

			RegisterHook();
		}

		internal abstract bool MouseButtonsSwapped { get; }

		public string ApplyCase(string typedstr, string hotstr)
		{
			var typedlen = typedstr.Length;
			var hotlen = hotstr.Length;
			_ = caser.Clear();

			for (int i = 0, j = 0; i < typedlen && j < hotlen;)
			{
				var ch1 = typedstr[i];
				var ch2 = hotstr[j];

				if (char.ToUpperInvariant(ch1) == char.ToUpperInvariant(ch2))
				{
					_ = caser.Append(ch1);
					i++;
				}
				else
					_ = caser.Append(ch2);

				j++;
			}

			return caser.ToString();
		}

		public bool IsPressed(Keys key)
		{
			if (pressed.ContainsKey(key))
				return pressed[key];
			else
			{
				System.Diagnostics.Debug.Fail("Thre should'nt be any key not in this table...");
				return false;
			}
		}

		internal static bool HotInputLevelAllowsFiring(long inputLevel, ulong aEventExtraInfo, ref char? aKeyHistoryChar)
		{
			if (InputLevelFromInfo((long)aEventExtraInfo) <= inputLevel)
			{
				if (aKeyHistoryChar != null)
					aKeyHistoryChar = 'i'; // Mark as ignored in KeyHistory

				return false;
			}

			return true;
		}

		internal static long InputLevelFromInfo(long aExtraInfo)
		{
			if (aExtraInfo >= KeyIgnoreMin() && aExtraInfo <= KeyIgnoreMax)
				return KeyIgnoreLevel(0) - aExtraInfo;

			return SendLevelMax + 1;
		}

		/// <summary>
		/// KEY_PHYS_IGNORE events must be mostly ignored because currently there is no way for a given
		/// hook instance to detect if it sent the event or some other instance.  Therefore, to treat
		/// such events as true physical events might cause infinite loops or other side-effects in
		/// the instance that generated the event.  More review of this is needed if KEY_PHYS_IGNORE
		/// events ever need to be treated as true physical events by the instances of the hook that
		/// didn't originate them. UPDATE: The foregoing can now be accomplished using SendLevel.
		/// </summary>
		/// <param name="aExtraInfo"></param>
		/// <returns></returns>
		internal static bool IsIgnored(ulong val) => val == KeyIgnore || val == KeyPhysIgnore || val == KeyIgnoreAllExceptModifier;

		// Same as above but marked as physical for other instances of the hook.
		// Non-physical and ignored only if it's not a modifier.
		// Same as KEY_IGNORE_ALL_EXCEPT_MODIFIER, but only ignored by Hotkeys & Hotstrings at InputLevel LEVEL and below.
		// The levels are set up to use negative offsets from KEY_IGNORE_ALL_EXCEPT_MODIFIER so that we can leave
		// the values above unchanged and have KEY_IGNORE_LEVEL(0) == KEY_IGNORE_ALL_EXCEPT_MODIFIER.
		//
		// In general, KEY_IGNORE_LEVEL(g->SendLevel) should be used for any input that's meant to be treated as "real",
		// as opposed to input generated for side effects (e.g., masking modifier keys to prevent default OS responses).
		// A lot of the calls that generate input fall into the latter category, so KEY_IGNORE_ALL_EXCEPT_MODIFIER
		// (aka KEY_IGNORE_LEVEL(0)) still gets used quite often.
		//
		// Note that there are no "level" equivalents for KEY_IGNORE or KEY_PHYS_IGNORE (only KEY_IGNORE_ALL_EXCEPT_MODIFIER).
		// For the KEY_IGNORE_LEVEL use cases, there isn't a need to ignore modifiers or differentiate between physical
		// and non-physical, and leaving them out keeps the code much simpler.
		internal static long KeyIgnoreLevel(long level) => KeyIgnoreAllExceptModifier - level;

		internal static long KeyIgnoreMin() => KeyIgnoreLevel(SendLevelMax);

		internal static bool SendLevelIsValid(long level) => level >= 0 && level <= SendLevelMax;

		internal HotkeyDefinition Add(HotkeyDefinition hotkey)
		{
			hotkeys.Add(hotkey);
			return hotkey;
		}

		internal HotstringDefinition Add(HotstringDefinition hotstring)
		{
			hotstrings.Add(hotstring);//This will not check for duplicates.
			return hotstring;
		}

		internal abstract void CleanupEventArray(long aFinalKeyDelay);

		/// <summary>
		/// For v1.0.25, the following situation is fixed by the code below: If LWin or LAlt
		/// becomes a persistent modifier (e.g. via Send {LWin down}) and the user physically
		/// releases LWin immediately before: 1) the {LWin up} is scheduled; and 2) SendKey()
		/// returns.  Then SendKey() will push the modifier back down so that it is in effect
		/// for other things done by its caller (SendKeys) and also so that if the Send
		/// operation ends, the key will still be down as the user intended (to modify future
		/// keystrokes, physical or simulated).  However, since that down-event is followed
		/// immediately by an up-event, the Start Menu appears for WIN-key or the active
		/// window's menu bar is activated for ALT-key.  SOLUTION: Disguise Win-up and Alt-up
		/// events in these cases.  This workaround has been successfully tested.  It's also
		/// limited is scope so that a script can still explicitly invoke the Start Menu with
		/// "Send {LWin}", or activate the menu bar with "Send {Alt}".
		/// The check of sPrevEventModifierDown allows "Send {LWinDown}{LWinUp}" etc., to
		/// continue to work.
		/// v1.0.40: For maximum flexibility and minimum interference while in blind mode,
		/// don't disguise Win and Alt keystrokes then.
		/// </summary>
		/// <param name="vk"></param>
		internal virtual void DisguiseWinAltIfNeeded(uint vk)
		{
			// Caller has ensured that vk is about to have a key-up event, so if the event immediately
			// prior to this one is a key-down of the same type of modifier key, it's our job here
			// to send the disguising keystrokes now (if appropriate).
			if (prevEventType == KeyEventTypes.KeyDown && prevEventModifierDown != vk && !inBlindMode
					// SendPlay mode can't display Start Menu, so no need for disguise keystrokes (such keystrokes might cause
					// unwanted effects in certain games):
					&& ((vk == VK_LWIN || vk == VK_RWIN) && (prevVK == VK_LWIN || prevVK == VK_RWIN) && sendMode != SendModes.Play
						|| (vk == VK_LMENU || (vk == VK_RMENU && targetLayoutHasAltGr != ResultType.ConditionTrue)) && (prevVK == VK_LMENU || prevVK == VK_RMENU)))
				SendKeyEventMenuMask(KeyEventTypes.KeyDownAndUp); // Disguise it to suppress Start Menu or prevent activation of active window's menu bar.
		}

		/// <summary>
		/// Doesn't need to be thread safe because it should only ever be called from main thread.
		/// </summary>
		/// <param name="delay"></param>
		internal virtual void DoKeyDelay(long delay = long.MinValue)
		{
			if (delay == long.MinValue)
				delay = (sendMode == SendModes.Play) ? ThreadAccessors.A_KeyDelayPlay : ThreadAccessors.A_KeyDelay;

			if (delay < 0) // To support user-specified KeyDelay of -1 (fastest send rate).
				return;

			if (sendMode != SendModes.Event)
			{
				if (sendMode == SendModes.Play && delay > 0) // Zero itself isn't supported by playback hook, so no point in inserting such delays into the array.
					PutKeybdEventIntoArray(0, 0, 0, 0, (uint)delay); // Passing zero for vk and sc signals it that aExtraInfo contains the delay.

				//else for other types of arrays, never insert a delay or do one now.
				return;
			}

			Flow.SleepWithoutInterruption(delay);
		}

		internal virtual void DoMouseDelay()// Helper function for the mouse functions below.
		{
			var mouseDelay = sendMode == SendModes.Play ? ThreadAccessors.A_MouseDelayPlay : ThreadAccessors.A_MouseDelay;

			if (mouseDelay < 0) // To support user-specified KeyDelay of -1 (fastest send rate).
				return;

			if (sendMode != SendModes.Event)
			{
				if (sendMode == SendModes.Play && mouseDelay > 0) // Zero itself isn't supported by playback hook, so no point in inserting such delays into the array.
					PutKeybdEventIntoArray(0, 0, 0, 0, (uint)mouseDelay); // Passing zero for vk and sc signals to it that aExtraInfo contains the delay.

				//else for other types of arrays, never insert a delay or do one now (caller should have already
				// checked that, so it's written this way here only for maintainability).
				return;
			}

			// I believe the varying sleep methods below were put in place to avoid issues when simulating
			// clicks on the script's own windows.  There are extensive comments in MouseClick() and the
			// hook about these issues.  Here are more details from an older comment:
			// Always sleep a certain minimum amount of time between events to improve reliability,
			// but allow the user to specify a higher time if desired.  A true sleep is done if the
			// delay period is small.  This fixes a small issue where if LButton is a hotkey that
			// includes "MouseClick left" somewhere in its subroutine, the script's own main window's
			// title bar buttons for min/max/close would not properly respond to left-clicks.
			if (mouseDelay < 11)
				_ = Flow.Sleep(mouseDelay);
			else
				Flow.SleepWithoutInterruption(mouseDelay);
		}

		internal abstract nint GetFocusedKeybdLayout(nint aWindow);

		//  // SendInput() appears to be limited to 5000 chars (10000 events in array), at least on XP.  This is
		//  // either an undocumented SendInput limit or perhaps it's due to the system setting that determines
		//  // how many messages can get backlogged in each thread's msg queue before some start to get dropped.
		//  // Note that SendInput()'s return value always seems to indicate that all the characters were sent
		//  // even when the ones beyond the limit were clearly never received by the target window.
		//  // In any case, it seems best not to restrict to 5000 here in case the limit can vary for any reason.
		//  // The 5000 limit is documented in the help file.
		//  try
		//  {
		//      eventSi.Add(new INPUT());
		//      eventPb.Add(new PlaybackEvent());
		//      maxEvents = eventSi.Count;
		//      return ResultType.Ok;
		//  }
		//  catch
		//  {
		//      abortArraySend = true;//Usually better to send nothing rather than partial.
		//      return ResultType.Fail;//Leave sEventSI and sMaxEvents in their current valid state, to be freed by CleanupEventArray().
		//  }
		//}
		/// <summary>
		/// Try to report a more reliable state of the modifier keys than GetKeyboardState alone could.
		/// Fix for v1.0.42.01: On Windows 2000/XP or later, GetAsyncKeyState() should be called rather than
		/// GetKeyState().  This is because our callers always want the current state of the modifier keys
		/// rather than their state at the time of the currently-in-process message was posted.  For example,
		/// if the control key wasn't down at the time our thread's current message was posted, but it's logically
		/// down according to the system, we would want to release that control key before sending non-control
		/// keystrokes, even if one of our thread's own windows has keyboard focus (because if it does, the
		/// control-up keystroke should wind up getting processed after our thread realizes control is down).
		/// This applies even when the keyboard/mouse hook call use because keystrokes routed to the hook via
		/// the hook's message pump aren't messages per se, and thus GetKeyState and GetAsyncKeyState probably
		/// return the exact same thing whenever there are no messages in the hook's thread-queue (which is almost
		/// always the case).
		/// </summary>
		/// <param name="explicitlyGet"></param>
		/// <returns></returns>
		internal virtual uint GetModifierLRState(bool explicitlyGet = false)
		{
			var ht = Script.TheScript.HookThread;

			// If the hook is active, rely only on its tracked value rather than calling Get():
			if (ht.HasKbdHook() && !explicitlyGet)
				return modifiersLRLogical;

			// Very old comment:
			// Use GetKeyState() rather than GetKeyboardState() because it's the only way to get
			// accurate key state when a console window is active, it seems.  I've also seen other
			// cases where GetKeyboardState() is incorrect (at least under WinXP) when GetKeyState(),
			// in its place, yields the correct info.  Very strange.
			var modifiersLR = 0u;  // Allows all to default to up/off to simplify the below.

			if (ht.IsKeyDownAsync(VK_LSHIFT)) modifiersLR |= MOD_LSHIFT;

			if (ht.IsKeyDownAsync(VK_RSHIFT)) modifiersLR |= MOD_RSHIFT;

			if (ht.IsKeyDownAsync(VK_LCONTROL)) modifiersLR |= MOD_LCONTROL;

			if (ht.IsKeyDownAsync(VK_RCONTROL)) modifiersLR |= MOD_RCONTROL;

			if (ht.IsKeyDownAsync(VK_LMENU)) modifiersLR |= MOD_LALT;

			if (ht.IsKeyDownAsync(VK_RMENU)) modifiersLR |= MOD_RALT;

			if (ht.IsKeyDownAsync(VK_LWIN)) modifiersLR |= MOD_LWIN;

			if (ht.IsKeyDownAsync(VK_RWIN)) modifiersLR |= MOD_RWIN;

			// Thread-safe: The following section isn't thread-safe because either the hook thread
			// or the main thread can be calling it.  However, given that anything dealing with
			// keystrokes isn't thread-safe in the sense that keystrokes can be coming in simultaneously
			// from multiple sources, it seems acceptable to keep it this way (especially since
			// the consequences of a thread collision seem very mild in this case).
			if (ht.HasKbdHook())
			{
				// Since hook is installed, fix any modifiers that it incorrectly thinks are down.
				// Though rare, this situation does arise during periods when the hook cannot track
				// the user's keystrokes, such as when the OS is doing something with the hardware,
				// e.g. switching to TV-out or changing video resolutions.  There are probably other
				// situations where this happens -- never fully explored and identified -- so it
				// seems best to do this, at least when the caller specified aExplicitlyGet = true.
				// To limit the scope of this workaround, only change the state of the hook modifiers
				// to be "up" for those keys the hook thinks are logically down but which the OS thinks
				// are logically up.  Note that it IS possible for a key to be physically down without
				// being logically down (i.e. during a Send command where the user is physically holding
				// down a modifier, but the Send command needs to put it up temporarily), so do not
				// change the hook's physical state for such keys in that case.
				// UPDATE: The following adjustment is now also relied upon by the SendInput method
				// to correct physical modifier state during periods when the hook was temporarily removed
				// to allow a SendInput to be uninterruptible.
				// UPDATE: The modifier state might also become incorrect due to keyboard events which
				// are missed due to User Interface Privelege Isolation; i.e. because a window belonging
				// to a process with higher integrity level than our own became active while the key was
				// down, so we saw the down event but not the up event.
				var modifiersWronglyDown = modifiersLRLogical & ~modifiersLR;

				// modifiers_wrongly_down can sometimes include modifiers that have only just been pressed
				// but aren't yet reflected by IsKeyDownAsync().  This happens much more often if a keyboard
				// hook is installed AFTER our own.  The following simple script was enough to reproduce this:
				//  ~*RWin::GetKeyState("RWin", "P")
				//  >#/::MsgBox  ; This hotkey sometimes or always failed to fire.
				// The sequence of events was probably something like this:
				//  - OS detects RWin down.
				//  - OS calls other hook.
				//  - Other hook calls ours via CallNextHookEx (meaning its thread is blocked
				//    waiting for the call to return).
				//  - Our hook updates key state, posts AHK_HOOK_HOTKEY and RETURNS IMMEDIATELY
				//    (but the other hook is in another thread, so it doesn't resume immediately).
				//  - Script thread receives AHK_HOOK_HOTKEY and fires hotkey.
				//  - Hotkey calls Send or GetKeyState, triggering the section below, adjusting
				//    g_modifiersLR_logical to match GetAsyncKeyState().
				//  - Other hook's thread wakes up and returns.
				//  - OS updates key state, so then GetAsyncKeyState() reports the correct state
				//    and g_modifiersLR_logical is incorrect.
				//  - RWin+/ doesn't fire the hotkey because the hook thinks RWin isn't down,
				//    even though KeyHistory shows that it should be down.
				// The issue occurred with maybe 50% frequency if the other hook was an AutoHotkey hook,
				// and 100% frequency if the other hook was implemented by a script (which is slower).
				// Only the last pressed modifier is excluded, since any other key-down or key-up being
				// detected would usually mean that the previous call to the hook has finished (although
				// the hook can be called recursively with artificial input).
				if (modifiersLRLastPressed != 0 && ((DateTime.UtcNow - modifiersLRLastPressedTime).TotalMilliseconds < 20))
				{
					if ((modifiersWronglyDown & modifiersLRLastPressed) != 0)
					{
						// It's logically down according to the hook, but not according to IsKeyDownAsync().
						// Trust the hook in this case.
						modifiersWronglyDown &= ~modifiersLRLastPressed;
						// v2.0.12: Report that the modifier is down, consistent with g_modifiersLR_logical.
						// This fixes an issue where Send erroneously releases modifiers in {Blind} mode.
						modifiersLR |= modifiersLRLastPressed;
					}
				}

				if (modifiersWronglyDown != 0)
				{
					// Adjust the physical and logical hook state to release the keys that are wrongly down.
					// If a key is wrongly logically down, it seems best to release it both physically and
					// logically, since the hook's failure to see the up-event probably makes its physical
					// state wrong in most such cases.
					modifiersLRPhysical &= ~modifiersWronglyDown;
					modifiersLRLogical &= ~modifiersWronglyDown;
					modifiersLRLogicalNonIgnored &= ~modifiersWronglyDown;
					// Also adjust physical state so that the GetKeyState command will retrieve the correct values:
					AdjustKeyState(ht.physicalKeyState, modifiersLRPhysical);

					// Also reset pPrefixKey if it is one of the wrongly-down modifiers.
					if (prefixKey != null && (prefixKey.asModifiersLR & modifiersWronglyDown) != 0)
						prefixKey = null;
				}
			}

			return modifiersLR;
			// Only consider a modifier key to be really down if both the hook's tracking of it
			// and GetKeyboardState() agree that it should be down.  The should minimize the impact
			// of the inherent unreliability present in each method (and each method is unreliable in
			// ways different from the other).  I have verified through testing that this eliminates
			// many misfires of hotkeys.  UPDATE: Both methods are fairly reliable now due to starting
			// to send scan codes with keybd_event(), using MapVirtualKey to resolve zero-value scan
			// codes in the keyboardproc(), and using GetKeyState() rather than GetKeyboardState().
			// There are still a few cases when they don't agree, so return the bitwise-and of both
			// if the keyboard hook is active.  Bitwise and is used because generally it's safer
			// to assume a modifier key is up, when in doubt (e.g. to avoid firing unwanted hotkeys):
			//  return g_KeybdHook ? (g_modifiersLR_logical & g_modifiersLR_get) : g_modifiersLR_get;
		}

		internal abstract void InitEventArray(int maxEvents, uint aModifiersLR);

		/// <summary>
		/// Thread-safety: While not thoroughly thread-safe, due to the extreme simplicity of the cache array, even if
		/// a collision occurs it should be inconsequential.
		/// Caller must ensure that layout is a valid layout (special values like 0 aren't supported here).
		/// If aHasAltGr is not at its default of LAYOUT_UNDETERMINED, the specified layout's has_altgr property is
		/// updated to the new value, but only if it is currently undetermined (callers can rely on this).
		/// </summary>
		/// <param name="layout"></param>
		/// <returns></returns>
		internal ResultType LayoutHasAltGr(nint layout)
		{
			// Layouts are cached for performance (to avoid the discovery loop later below).
			for (var i = 0; i < cachedLayouts.Count && cachedLayouts[i].hkl != 0; ++i)
				if (cachedLayouts[i].hkl == layout) // Match Found.
					return cachedLayouts[i].has_altgr;

			// Since above didn't return, this layout isn't cached yet.  So create a new cache entry for it and
			// determine whether this layout has an AltGr key.  An LRU/MRU algorithm (timestamp) isn't used because running out
			// of slots seems too unlikely, and the consequences of running out are merely a slight degradation in performance.
			// The old approach here was to call VkKeyScanEx for each character code and find any that require
			// AltGr.  However, that was unacceptably slow for the wider character range of the Unicode build.
			// It was also unreliable (as noted below), so required additional logic in Send and the hook to
			// compensate.  Instead, read the AltGr value directly from the keyboard layout DLL.
			// This method has been compared to the VkKeyScanEx method and another one using Send and hotkeys,
			// and was found to have 100% accuracy for the 203 standard layouts on Windows 10, whereas the
			// VkKeyScanEx method failed for two layouts:
			//   - N'Ko has AltGr but does not appear to use it for anything.
			//   - Ukrainian has AltGr but only uses it for one character, which is also assigned to a naked
			//     VK (so VkKeyScanEx returns that one).  Likely the key in question is absent from some keyboards.
			var hasaltgr = LayoutHasAltGrDirect(layout);
			cachedLayouts.Add(new CachedLayoutType()
			{
				has_altgr = hasaltgr,
				hkl = layout// This is done here (immediately after has_altgr is set) rather than earlier to minimize the consequences of not being fully thread-safe.
			});
			return hasaltgr;
		}

		internal string ModifiersLRToText(uint aModifiersLR)
		{
			var sb = new StringBuilder(64);

			if ((aModifiersLR & MOD_LWIN) != 0) _ = sb.Append("LWin ");

			if ((aModifiersLR & MOD_RWIN) != 0) _ = sb.Append("RWin ");

			if ((aModifiersLR & MOD_LSHIFT) != 0) _ = sb.Append("LShift ");

			if ((aModifiersLR & MOD_RSHIFT) != 0) _ = sb.Append("RShift ");

			if ((aModifiersLR & MOD_LCONTROL) != 0) _ = sb.Append("LCtrl ");

			if ((aModifiersLR & MOD_RCONTROL) != 0) _ = sb.Append("RCtrl ");

			if ((aModifiersLR & MOD_LALT) != 0) _ = sb.Append("LAlt ");

			if ((aModifiersLR & MOD_RALT) != 0) _ = sb.Append("RAlt ");

			return sb.ToString();
		}

		internal abstract ResultType LayoutHasAltGrDirect(nint layout);

		internal virtual bool MouseClickPreLRButton(KeyEventTypes eventType, uint vk)
		{
			// Default implementation does nothing.
			return false;
		}

		internal virtual void MouseClick(uint vk, int x, int y, long repeatCount, long speed, KeyEventTypes eventType, bool moveOffset)
		{
			// Check if one of the coordinates is missing, which can happen in cases where this was called from
			// a source that didn't already validate it (such as MouseClick, %x%, %BlankVar%).
			// Allow aRepeatCount<1 to simply "do nothing", because it increases flexibility in the case where
			// the number of clicks is a dereferenced script variable that may sometimes (by intent) resolve to
			// zero or negative.  For backward compatibility, a RepeatCount <1 does not move the mouse (unlike
			// the Click command and Send {Click}).
			if ((x == CoordUnspecified && y != CoordUnspecified) || (x != CoordUnspecified && y == CoordUnspecified) || (repeatCount < 1L))
				return;

			var eventFlags = 0u; // Set default.

			if (x != CoordUnspecified && y != CoordUnspecified)//Both coordinates were specified.
			{
				// The movement must be a separate event from the click, otherwise it's completely unreliable with
				// SendInput() and probably keybd_event() too.  SendPlay is unknown, but it seems best for
				// compatibility and peace-of-mind to do it for that too.  For example, some apps may be designed
				// to expect mouse movement prior to a click at a *new* position, which is not unreasonable given
				// that this would be the case 99.999% of the time if the user were moving the mouse physically.
				MouseMove(ref x, ref y, ref eventFlags, speed, moveOffset); // It calls DoMouseDelay() and also converts aX and aY to MOUSEEVENTF_ABSOLUTE coordinates.
				// v1.0.43: eventFlags was added to improve reliability.  Explanation: Since the mouse was just moved to an
				// explicitly specified set of coordinates, use those coordinates with subsequent clicks.  This has been
				// shown to significantly improve reliability in cases where the user is moving the mouse during the
				// MouseClick/Drag commands.
			}

			// Above must be done prior to below because initial mouse-move is supported even for wheel turning.

			// For wheel turning, if the user activated this command via a hotkey, and that hotkey
			// has a modifier such as CTRL, the user is probably still holding down the CTRL key
			// at this point.  Therefore, there's some merit to the fact that we should release
			// those modifier keys prior to turning the mouse wheel (since some apps disable the
			// wheel or give it different behavior when the CTRL key is down -- for example, MSIE
			// changes the font size when you use the wheel while CTRL is down).  However, if that
			// were to be done, there would be no way to ever hold down the CTRL key explicitly
			// (via Send, {CtrlDown}) unless the hook were installed.  The same argument could probably
			// be made for mouse button clicks: modifier keys can often affect their behavior.  But
			// changing this function to adjust modifiers for all types of events would probably break
			// some existing scripts.  Maybe it can be a script option in the future.  In the meantime,
			// it seems best not to adjust the modifiers for any mouse events and just document that
			// behavior in the MouseClick command.
			switch (vk)
			{
				case VK_WHEEL_UP:
					MouseEvent(eventFlags | (uint)MOUSEEVENTF.WHEEL, (uint)(repeatCount * WHEEL_DELTA), x, y);  // It ignores aX and aY when MOUSEEVENTF_MOVE is absent.
					return;

				case VK_WHEEL_DOWN:
					MouseEvent(eventFlags | (uint)MOUSEEVENTF.WHEEL, (uint)(-(repeatCount * WHEEL_DELTA)), x, y);//Unsure if casting a negative int to uint will work, need to test scrolling with mouse events.//TODO
					return;

				// v1.0.48: Lexikos: Support horizontal scrolling in Windows Vista and later.
				case VK_WHEEL_LEFT:
					MouseEvent(eventFlags | (uint)MOUSEEVENTF.HWHEEL, (uint)(-(repeatCount * WHEEL_DELTA)), x, y);
					return;

				case VK_WHEEL_RIGHT:
					MouseEvent(eventFlags | (uint)MOUSEEVENTF.HWHEEL, (uint)(repeatCount * WHEEL_DELTA), x, y);
					return;
			}

			// Since above didn't return:
			// Although not thread-safe, the following static vars seem okay because:
			// 1) This function is currently only called by the main thread.
			// 2) Even if that isn't true, the serialized nature of simulated mouse clicks makes it likely that
			//    the statics will produce the correct behavior anyway.
			// 3) Even if that isn't true, the consequences of incorrect behavior seem minimal in this case.
			uint eventDown = 0, eventUp = 0, eventData = 0; // Set default.

			// MSDN: If [eventFlags] is not MOUSEEVENTF_WHEEL, MOUSEEVENTF_XDOWN, or MOUSEEVENTF_XUP, then [eventData]
			// should be zero.
			// v2.0: Always translate logical buttons into physical ones.  Which physical button it becomes depends
			// on whether the mouse buttons are swapped via the Control Panel.
			if ((vk == VK_LBUTTON || vk == VK_RBUTTON) && sendMode != SendModes.Play && MouseButtonsSwapped)
				vk = (vk == VK_LBUTTON) ? VK_RBUTTON : VK_LBUTTON;

			switch (vk)
			{
				case VK_LBUTTON:
				case VK_RBUTTON:

					if (MouseClickPreLRButton(eventType, vk))
						return;

					// sWorkaroundVK is reset later below.

					// Since above didn't return, the work-around isn't in effect and normal click(s) will be sent:
					if (vk == VK_LBUTTON)
					{
						eventDown = (uint)MOUSEEVENTF.LEFTDOWN;
						eventUp = (uint)MOUSEEVENTF.LEFTUP;
					}
					else // vk == VK_RBUTTON
					{
						eventDown = (uint)MOUSEEVENTF.RIGHTDOWN;
						eventUp = (uint)MOUSEEVENTF.RIGHTUP;
					}

					break;

				case VK_MBUTTON:
					eventDown = (uint)MOUSEEVENTF.MIDDLEDOWN;
					eventUp = (uint)MOUSEEVENTF.MIDDLEUP;
					break;

				case VK_XBUTTON1:
				case VK_XBUTTON2:
					eventDown = (uint)MOUSEEVENTF.XDOWN;
					eventUp = (uint)MOUSEEVENTF.XUP;
					eventData = (vk == VK_XBUTTON1) ? XBUTTON1 : XBUTTON2;
					break;
			} // switch()

			// For simplicity and possibly backward compatibility, LONG_OPERATION_INIT/UPDATE isn't done.
			// In addition, some callers might do it for themselves, at least when aRepeatCount==1.
			for (var i = 0L; i < repeatCount; ++i)
			{
				if (eventType != KeyEventTypes.KeyUp) // It's either KEYDOWN or KEYDOWNANDUP.
				{
					// v1.0.43: Reliability is significantly improved by specifying the coordinates with the event (if
					// caller gave us coordinates).  This is mostly because of SetMouseDelay: In previously versions,
					// the delay between a MouseClick's move and its click allowed time for the user to move the mouse
					// away from the target position before the click was sent.
					MouseEvent(eventFlags | eventDown, eventData, x, y); // It ignores aX and aY when MOUSEEVENTF_MOVE is absent.

					// It seems best to always Sleep a certain minimum time between events
					// because the click-down event may cause the target app to do something which
					// changes the context or nature of the click-up event.  AutoIt3 has also been
					// revised to do this. v1.0.40.02: Avoid doing the Sleep between the down and up
					// events when the workaround is in effect because any MouseDelay greater than 10
					// would cause DoMouseDelay() to pump messages, which would defeat the workaround:
					if (workaroundVK == 0)
						DoMouseDelay(); // Inserts delay for all modes except SendInput, for which it does nothing.
				}

				if (eventType != KeyEventTypes.KeyDown) // It's either KEYUP or KEYDOWNANDUP.
				{
					MouseEvent(eventFlags | eventUp, eventData, x, y); // It ignores aX and aY when MOUSEEVENTF_MOVE is absent.
					// It seems best to always do this one too in case the script line that caused
					// us to be called here is followed immediately by another script line which
					// is either another mouse click or something that relies upon the mouse click
					// having been completed:
					DoMouseDelay(); // Inserts delay for all modes except SendInput, for which it does nothing.
				}
			} // for()

			workaroundVK = 0; // Reset this indicator in all cases except those for which above already returned.
		}

		internal virtual void MouseClickDrag(uint vk, int x1, int y1, int x2, int y2, long speed, bool relative)
		{
			// Check if one of the coordinates is missing, which can happen in cases where this was called from
			// a source that didn't already validate it. Can't call Line::ValidateMouseCoords() because that accepts strings.
			if ((x1 == CoordUnspecified ^ y1 == CoordUnspecified) || (x2 == CoordUnspecified ^ y2 == CoordUnspecified))
				return;

			// I asked Jon, "Have you discovered that insta-drags almost always fail?" and he said
			// "Yeah, it was weird, absolute lack of drag... Don't know if it was my config or what."
			// However, testing reveals "insta-drags" work ok, at least on my system, so leaving them enabled.
			// User can easily increase the speed if there's any problem:
			//if (aSpeed < 2)
			//  aSpeed = 2;
			// v2.0: Always translate logical buttons into physical ones.  Which physical button it becomes depends
			// on whether the mouse buttons are swapped via the Control Panel.  Note that journal playback doesn't
			// need the swap because every aspect of it is "logical".
			if ((vk == VK_LBUTTON || vk == VK_RBUTTON) && sendMode != SendModes.Play && MouseButtonsSwapped)
				vk = (vk == VK_LBUTTON) ? VK_RBUTTON : VK_LBUTTON;//Need to figure out making this cross platform.//TODO

			// MSDN: If [event_flags] is not MOUSEEVENTF_WHEEL, [MOUSEEVENTF_HWHEEL,] MOUSEEVENTF_XDOWN,
			// or MOUSEEVENTF_XUP, then [event_data] should be zero.
			var eventdata = 0u; // Set defaults for some.
			var eventup = 0u;
			var eventdown = 0u;
			var eventflags = 0u;

			switch (vk)
			{
				case VK_LBUTTON:
					eventdown = (uint)MOUSEEVENTF.LEFTDOWN;
					eventup = (uint)MOUSEEVENTF.LEFTUP;
					break;

				case VK_RBUTTON:
					eventdown = (uint)MOUSEEVENTF.RIGHTDOWN;
					eventup = (uint)MOUSEEVENTF.RIGHTUP;
					break;

				case VK_MBUTTON:
					eventdown = (uint)MOUSEEVENTF.MIDDLEDOWN;
					eventup = (uint)MOUSEEVENTF.MIDDLEUP;
					break;

				case VK_XBUTTON1:
				case VK_XBUTTON2:
					eventdown = (uint)MOUSEEVENTF.XDOWN;
					eventup = (uint)MOUSEEVENTF.XUP;
					eventdata = (vk == VK_XBUTTON1) ? XBUTTON1 : XBUTTON2;
					break;
			}

			// If the drag isn't starting at the mouse's current position, move the mouse to the specified position:
			if (x1 != CoordUnspecified && y1 != CoordUnspecified)
			{
				// The movement must be a separate event from the click, otherwise it's completely unreliable with
				// SendInput() and probably keybd_event() too.  SendPlay is unknown, but it seems best for
				// compatibility and peace-of-mind to do it for that too.  For example, some apps may be designed
				// to expect mouse movement prior to a click at a *new* position, which is not unreasonable given
				// that this would be the case 99.999% of the time if the user were moving the mouse physically.
				MouseMove(ref x1, ref y1, ref eventflags, speed, relative); // It calls DoMouseDelay() and also converts aX1 and aY1 to MOUSEEVENTF_ABSOLUTE coordinates.
				// v1.0.43: event_flags was added to improve reliability.  Explanation: Since the mouse was just moved to an
				// explicitly specified set of coordinates, use those coordinates with subsequent clicks.  This has been
				// shown to significantly improve reliability in cases where the user is moving the mouse during the
				// MouseClick/Drag commands.
			}

			MouseEvent(eventflags | eventdown, eventdata, x1, y1); // It ignores aX and aY when MOUSEEVENTF_MOVE is absent.
			DoMouseDelay(); // Inserts delay for all modes except SendInput, for which it does nothing.
			// Now that the mouse button has been pushed down, move the mouse to perform the drag:
			MouseMove(ref x2, ref y2, ref eventflags, speed, relative); // It calls DoMouseDelay() and also converts aX2 and aY2 to MOUSEEVENTF_ABSOLUTE coordinates.
			DoMouseDelay(); // Duplicate, see below.
			// Above is a *duplicate* delay because MouseMove() already did one. But it seems best to keep it because:
			// 1) MouseClickDrag can be a CPU intensive operation for the target window depending on what it does
			//    during the drag (selecting objects, etc.)  Thus, and extra delay might help a lot of things.
			// 2) It would probably break some existing scripts to remove the delay, due to timing issues.
			// 3) Dragging is pretty rarely used, so the added performance of removing the delay wouldn't be
			//    a big benefit.
			MouseEvent(eventflags | eventup, eventdata, x2, y2); // It ignores aX and aY when MOUSEEVENTF_MOVE is absent.
			DoMouseDelay();
			// Above line: It seems best to always do this delay too in case the script line that
			// caused us to be called here is followed immediately by another script line which
			// is either another mouse click or something that relies upon this mouse drag
			// having been completed:
		}

		internal abstract void MouseEvent(uint aEventFlags, uint aData, int aX = CoordUnspecified, int aY = CoordUnspecified);

		/// <summary>
		/// This function also does DoMouseDelay() for the caller.
		/// This function converts x and aY for the caller into MOUSEEVENTF_ABSOLUTE coordinates.
		/// The exception is when the playback mode is in effect, in which case such a conversion would be undesirable
		/// both here and by the caller.
		/// It also puts appropriate bit-flags into eventFlags.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="eventFlags"></param>
		/// <param name="speed"></param>
		/// <param name="moveOffset"></param>
		internal virtual void MouseMove(ref int x, ref int y, ref uint eventFlags, long speed, bool moveOffset)
		{
			// Most callers have already validated this, but some haven't.  Since it doesn't take too long to check,
			// do it here rather than requiring all callers to do (helps maintainability).
			if (x == CoordUnspecified || y == CoordUnspecified)
				return;

			if (sendMode == SendModes.Play) // Journal playback mode.
			{
				// Mouse speed (aSpeed) is ignored for SendInput/Play mode: the mouse always moves instantaneously
				// (though in the case of playback-mode, MouseDelay still applies between each movement and click).
				// Playback-mode ignores mouse speed because the cases where the user would want to move the mouse more
				// slowly (such as a demo) seem too rare to justify the code size and complexity, especially since the
				// incremental-move code would have to be implemented in the hook itself to ensure reliability.  This is
				// because calling GetCursorPos() before playback begins could cause the mouse to wind up at the wrong
				// destination, especially if our thread is preempted while building the array (which would give the user
				// a chance to physically move the mouse before uninterruptibility begins).
				// For creating demonstrations that user slower mouse movement, the older MouseMove command can be used
				// in conjunction with BlockInput. This also applies to SendInput because it's conceivable that mouse
				// speed could be supported there (though it seems useless both visually and to improve reliability
				// because no mouse delays are possible within SendInput).
				//
				// MSG_OFFSET_MOUSE_MOVE is used to have the playback hook apply the offset (rather than doing it here
				// like is done for SendInput mode).  This adds flexibility in cases where a new window becomes active
				// during playback, or the active window changes position -- if that were to happen, the offset would
				// otherwise be wrong while CoordMode is Relative because the changes can only be observed and
				// compensated for during playback.
				PutMouseEventIntoArray((uint)MOUSEEVENTF.MOVE | (moveOffset ? MsgOffsetMouseMove : 0),
									   0, x, y);//The playback hook uses normal vs. MOUSEEVENTF_ABSOLUTE coordinates.
				DoMouseDelay();

				if (moveOffset)
				{
					// Now that we're done using the old values of x and aY above, reset them to CoordUnspecified
					// for the caller so that any subsequent clicks it does will be marked as "at current coordinates".
					x = CoordUnspecified;
					y = CoordUnspecified;
				}

				return; // Other parts below rely on this returning early to avoid converting aX/aY into MOUSEEVENTF_ABSOLUTE.
			}

			// The playback mode returned from above doesn't need these flags added because they're ignored for clicks:
			eventFlags |= (uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE;//Done here for caller, for easier maintenance.
			POINT cursor_pos;

			if (moveOffset)  // We're moving the mouse cursor relative to its current position.
			{
				if (sendMode == SendModes.Input)
				{
					// Since GetCursorPos() can't be called to find out a future cursor position, use the position
					// tracked for SendInput (facilitates MouseClickDrag's R-option as well as Send{Click}'s).
					if (sendInputCursorPos.X == CoordUnspecified) // Initial/starting value hasn't yet been set.
					{
						if (GetCursorPos(out sendInputCursorPos)) // Start it off where the cursor is now.
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
				else
				{
					if (GetCursorPos(out cursor_pos)) // None of this is done for playback mode since that mode already returned higher above.
					{
						x += cursor_pos.X;
						y += cursor_pos.Y;
					}
				}
			}
			else
			{
				// Convert relative coords to screen coords if necessary (depends on CoordMode).
				// None of this is done for playback mode since that mode already returned higher above.
				CoordToScreen(ref x, ref y, CoordMode.Mouse);
			}

			if (sendMode == SendModes.Input) // Track predicted cursor position for use by subsequent events put into the array.
			{
				sendInputCursorPos.X = x; // Always stores normal coords (non-MOUSEEVENTF_ABSOLUTE).
				sendInputCursorPos.Y = y; //
			}

			// Find dimensions of primary monitor.
			var screen_width = (int)A_ScreenWidth;
			var screen_height = (int)A_ScreenHeight;
			x = MouseCoordToAbs(x, screen_width);
			y = MouseCoordToAbs(y, screen_height);
			// x and aY MUST BE SET UNCONDITIONALLY because the output parameters must be updated for caller.
			// The incremental-move section further below also needs it.

			if (speed < 0)  // This can happen during script's runtime due to something like: MouseMove, X, Y, %VarContainingNegative%
				speed = 0;  // 0 is the fastest.
			else if (speed > MaxMouseSpeed)
				speed = MaxMouseSpeed;

			if (speed == 0 || sendMode == SendModes.Input) // Instantaneous move to destination coordinates with no incremental positions in between.
			{
				// See the comments in the playback-mode section at the top of this function for why SM_INPUT ignores aSpeed.
				MouseEvent((int)MOUSEEVENTF.MOVE | (int)MOUSEEVENTF.ABSOLUTE, 0, x, y);
				DoMouseDelay(); // Inserts delay for all modes except SendInput, for which it does nothing.
				return;
			}

			// Since above didn't return, use the incremental mouse move to gradually move the cursor until
			// it arrives at the destination coordinates.
			// Convert the cursor's current position to mouse event coordinates (MOUSEEVENTF_ABSOLUTE).
			if (GetCursorPos(out cursor_pos))
			{
				DoIncrementalMouseMove(
					MouseCoordToAbs(cursor_pos.X, screen_width), //Source/starting coords.
					MouseCoordToAbs(cursor_pos.Y, screen_height),
					x, y, speed);                             //Destination/ending coords.
			}
		}

		internal abstract int PbEventCount();

		internal void PerformMouseCommon(Actions actionType, uint vk, int x1, int y1, int x2, int y2,
										 long repeatCount, KeyEventTypes eventType, long speed, bool relative)
		{
			// The maximum number of events, which in this case would be from a MouseClickDrag.  To be conservative
			// (even though INPUT is a much larger struct than PlaybackEvent and SendInput doesn't use mouse-delays),
			// include room for the maximum number of mouse delays too.
			// Drag consists of at most:
			// 1) Move; 2) Delay; 3) Down; 4) Delay; 5) Move; 6) Delay; 7) Delay (dupe); 8) Up; 9) Delay.
			const int MAX_PERFORM_MOUSE_EVENTS = 10;
			var script = Script.TheScript;
			var ht = script.HookThread;
			sendMode = ThreadAccessors.A_SendMode;

			if (sendMode == SendModes.Input || sendMode == SendModes.InputThenPlay)
			{
				if (ht.SystemHasAnotherMouseHook()) // See similar section in SendKeys() for details.
					sendMode = (sendMode == SendModes.Input) ? SendModes.Event : SendModes.Play;
				else
					sendMode = SendModes.Input; // Resolve early so that other sections don't have to consider SM_INPUT_FALLBACK_TO_PLAY a valid value.
			}

			if (sendMode != SendModes.Event) // We're also responsible for setting sSendMode to SM_EVENT prior to returning.
				InitEventArray(MAX_PERFORM_MOUSE_EVENTS, 0);

			// Turn it on unconditionally even if it was on, since Ctrl-Alt-Del might have disabled it.
			// Turn it back off only if it wasn't ON before we started.
			var blockinputPrev = script.KeyboardData.blockInput;
			var doSelectiveBlockinput = (script.KeyboardData.blockInputMode == ToggleValueType.Mouse
										 || script.KeyboardData.blockInputMode == ToggleValueType.SendAndMouse)
										&& sendMode == SendModes.Event;

			if (doSelectiveBlockinput) // It seems best NOT to use g_BlockMouseMove for this, since often times the user would want keyboard input to be disabled too, until after the mouse event is done.
				_ = Core.Keyboard.ScriptBlockInput(true); // Turn it on unconditionally even if it was on, since Ctrl-Alt-Del might have disabled it.

			switch (actionType)
			{
				case Actions.ACT_MOUSEMOVE:
					var unused = 0u;
					MouseMove(ref x1, ref y1, ref unused, speed, relative); // Does nothing if coords are invalid.
					break;

				case Actions.ACT_MOUSECLICK:
					MouseClick(vk, x1, y1, repeatCount, speed, eventType, relative); // Does nothing if coords are invalid.
					break;

				case Actions.ACT_MOUSECLICKDRAG:
					MouseClickDrag(vk, x1, y1, x2, y2, speed, relative); // Does nothing if coords are invalid.
					break;
			}

			if (sendMode != SendModes.Event)
			{
				var finalKeyDelay = -1L; // Set default.

				if (!abortArraySend && TotalEventCount() > 0)
					SendEventArray(ref finalKeyDelay, 0); // Last parameter is ignored because keybd hook isn't removed for a pure-mouse SendInput.

				CleanupEventArray(finalKeyDelay);
			}

			if (doSelectiveBlockinput && !blockinputPrev)  // Turn it back off only if it was off before we started.
				_ = Core.Keyboard.ScriptBlockInput(false);
		}

		internal void ProcessHotkey(int wParamVal, int lParamVal, HotkeyVariant variant, uint msg)
		{
			var hkId = wParamVal & HotkeyDefinition.HOTKEY_ID_MASK;
			var script = Script.TheScript;
			var shk = script.HotkeyData.shk;

			if (hkId < shk.Count)//Ensure hotkey ID is valid.
			{
				var hk = shk[hkId];
				// Check if criterion allows firing.
				// For maintainability, this is done here rather than a little further down
				// past the g_MaxThreadsTotal and thread-priority checks.  Those checks hardly
				// ever abort a hotkey launch anyway.
				//
				// If message is WM_HOTKEY, it's either:
				// 1) A joystick hotkey from TriggerJoyHotkeys(), in which case the lParam is ignored.
				// 2) A hotkey message sent by the OS, in which case lParam contains currently-unused info set by the OS.
				//
				// An incoming WM_HOTKEY can be subject to #HotIf Win. at this stage under the following conditions:
				// 1) Joystick hotkey, because it relies on us to do the check so that the check is done only
				//    once rather than twice.
				// 2) #HotIf Win. keybd hotkeys that were made non-hook because they have a non-suspended, global variant.
				//
				// If message is AHK_HOOK_HOTKEY:
				// Rather than having the hook pass the qualified variant to us, it seems preferable
				// to search through all the criteria again and rediscover it.  This is because conditions
				// may have changed since the message was posted, and although the hotkey might still be
				// eligible for firing, a different variant might now be called for (e.g. due to a change
				// in the active window).  Since most criteria hotkeys have at most only a few criteria,
				// and since most such criteria are #HotIf WinActive rather than Exist, the performance will
				// typically not be reduced much at all.  Furthermore, trading performance for greater
				// reliability seems worth it in this case.
				//
				// The inefficiency of calling HotCriterionAllowsFiring() twice for each hotkey --
				// once in the hook and again here -- seems justified for the following reasons:
				// - It only happens twice if the hotkey a hook hotkey (multi-variant keyboard hotkeys
				//   that have a global variant are usually non-hook, even on NT/2k/XP).
				// - The hook avoids doing its first check of WinActive/Exist if it sees that the hotkey
				//   has a non-suspended, global variant.  That way, hotkeys that are hook-hotkeys for
				//   reasons other than #HotIf Win. (such as mouse, overriding OS hotkeys, or hotkeys
				//   that are too fancy for RegisterHotkey) will not have to do the check twice.
				// - It provides the ability to set the last-found-window for #HotIf WinActive/Exist
				//   (though it's not needed for the "Not" counterparts).  This HWND could be passed
				//   via the message, but that would require malloc-there and free-here, and might
				//   result in memory leaks if its ever possible for messages to get discarded by the OS.
				// - It allows hotkeys that were eligible for firing at the time the message was
				//   posted but that have since become ineligible to be aborted.  This seems like a
				//   good precaution for most users/situations because such hotkey subroutines will
				//   often assume (for scripting simplicity) that the specified window is active or
				//   exists when the subroutine executes its first line.
				// - Most criterion hotkeys use #HotIf WinActive(), which is a very fast call.  Also, although
				//   WinText and/or "SetTitleMatchMode 'Slow'" slow down window searches, those are rarely
				//   used too.
				//
				char? dummy = null;
				var criterion_found_hwnd = 0L;

				if (!(variant != null || (variant = hk.CriterionAllowsFiring(ref criterion_found_hwnd, (ulong)(msg == (uint)UserMessages.AHK_HOOK_HOTKEY ? KeyIgnoreLevel((uint)Conversions.HighWord(lParamVal)) : 0L), ref dummy)) != null))
					return;

				if (!script.Threads.AnyThreadsAvailable())//First test global thread count.
					return;

				// If this is AHK_HOOK_HOTKEY, criterion was eligible at time message was posted,
				// but not now.  Seems best to abort (see other comments).
				// Due to the key-repeat feature and the fact that most scripts use a value of 1
				// for their #MaxThreadsPerHotkey, this check will often help average performance
				// by avoiding a lot of unnecessary overhead that would otherwise occur:
				if (!variant.AnyThreadsAvailable())//Then test local thread count.
				{
					// The key is buffered in this case to boost the responsiveness of hotkeys
					// that are being held down by the user to activate the keyboard's key-repeat
					// feature.  This way, there will always be one extra event waiting in the queue,
					// which will be fired almost the instant the previous iteration of the subroutine
					// finishes (this above description applies only when MaxThreadsPerHotkey is 1,
					// which it usually is).
					variant.RunAgainAfterFinished(); // Wheel notch count (g->EventInfo below) should be okay because subsequent launches reuse the same thread attributes to do the repeats.
					return;
				}

				var tv = script.Threads.CurrentThread;

				// Now that above has ensured variant is non-NULL:
				if (variant.priority >= tv.priority)//Finally, test priority.
				{
					// Above also works for RunAgainAfterFinished since that feature reuses the same thread attributes set above.
					hk.PerformInNewThreadMadeByCallerAsync(variant, criterion_found_hwnd, lParamVal);
				}
			}
		}

		internal void Remove(HotkeyDefinition hotkey) => _ = hotkeys.Remove(hotkey);

		internal void Remove(HotstringDefinition hotstring) => _ = hotstrings.Remove(hotstring);

		internal abstract void SendEventArray(ref long aFinalKeyDelay, uint aModsDuringSend);

		/// <summary>
		/// Caller has ensured that: 1) vk or sc may be zero, but not both; 2) aRepeatCount > 0.
		/// This function is responsible for first setting the correct state of the modifier keys
		/// (as specified by the caller) before sending the key.  After sending, it should put the
		/// modifier keys  back to the way they were originally (UPDATE: It does this only for Win/Alt
		/// for the reasons described near the end of this function).
		/// </summary>
		/// <param name="vk"></param>
		/// <param name="sc"></param>
		/// <param name="modifiersLR"></param>
		/// <param name="modifiersLRPersistent"></param>
		/// <param name="repeatCount"></param>
		/// <param name="eventType"></param>
		/// <param name="keyAsModifiersLR"></param>
		/// <param name="targetWindow"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="moveOffset"></param>
		internal virtual void SendKey(uint vk, uint sc, uint modifiersLR, uint modifiersLRPersistent
									   , long repeatCount, KeyEventTypes eventType, uint keyAsModifiersLR, nint targetWindow
									   , int x = CoordUnspecified, int y = CoordUnspecified, bool moveOffset = false)
		{
			// Caller is now responsible for verifying this:
			// Avoid changing modifier states and other things if there is nothing to be sent.
			// Otherwise, menu bar might activated due to ALT keystrokes that don't modify any key,
			// the Start Menu might appear due to WIN keystrokes that don't modify anything, etc:
			//if ((!vk && !aSC) || aRepeatCount < 1)
			//  return;
			// I thought maybe it might be best not to release unwanted modifier keys that are already down
			// (perhaps via something like "Send, {altdown}{esc}{altup}"), but that harms the case where
			// modifier keys are down somehow, unintentionally: The send command wouldn't behave as expected.
			// e.g. "Send, abc" while the control key is held down by other means, would send ^a^b^c,
			// possibly dangerous.  So it seems best to default to making sure all modifiers are in the
			// proper down/up position prior to sending any Keybd events.  UPDATE: This has been changed
			// so that only modifiers that were actually used to trigger that hotkey are released during
			// the send.  Other modifiers that are down may be down intentionally, e.g. due to a previous
			// call to Send such as: Send {ShiftDown}.
			// UPDATE: It seems best to save the initial state only once, prior to sending the key-group,
			// because only at the beginning can the original state be determined without having to
			// save and restore it in each loop iteration.
			// UPDATE: Not saving and restoring at all anymore, due to interference (side-effects)
			// caused by the extra keybd events.
			// The combination of modifiersLR and modifiersLRPersistent are the modifier keys that
			// should be down prior to sending the specified vk/aSC. modifiersLR are the modifiers
			// for this particular vk keystroke, but modifiersLRPersistent are the ones that will stay
			// in pressed down even after it's sent.
			var modifiersLRSpecified = (modifiersLR | modifiersLRPersistent);
			var script = Script.TheScript;
			var vkIsMouse = MouseUtils.IsMouseVK(vk); // Caller has ensured that VK is non-zero when it wants a mouse click.
			var tv = script.Threads.CurrentThread.configData;
			var sendLevel = tv.sendLevel;

			for (var i = 0L; i < repeatCount; ++i)
			{
				if (sendMode == SendModes.Event)
					LongOperationUpdateForSendKeys();  // This does not measurably affect the performance of SendPlay/Event.

				// These modifiers above stay in effect for each of these keypresses.
				// Always on the first iteration, and thereafter only if the send won't be essentially
				// instantaneous.  The modifiers are checked before every key is sent because
				// if a high repeat-count was specified, the user may have time to release one or more
				// of the modifier keys that were used to trigger a hotkey.  That physical release
				// will cause a key-up event which will cause the state of the modifiers, as seen
				// by the system, to change.  For example, if user releases control-key during the operation,
				// some of the D's won't be control-D's:
				// ^c::Send,^{d 15}
				// Also: Seems best to do SetModifierLRState() even if Keydelay < 0:
				// Update: If this key is itself a modifier, don't change the state of the other
				// modifier keys just for it, since most of the time that is unnecessary and in
				// some cases, the extra generated keystrokes would cause complications/side-effects.
				if (keyAsModifiersLR == 0)
				{
					// DISGUISE UP: Pass "true" to disguise UP-events on WIN and ALT due to hotkeys such as:
					// !a::Send test
					// !a::Send {LButton}
					// v1.0.40: It seems okay to tell SetModifierLRState to disguise Win/Alt regardless of
					// whether our caller is in blind mode.  This is because our caller already put any extra
					// blind-mode modifiers into modifiersLR_specified, which prevents any actual need to
					// disguise anything (only the release of Win/Alt is ever disguised).
					// DISGUISE DOWN: Pass "false" to avoid disguising DOWN-events on Win and Alt because Win/Alt
					// will be immediately followed by some key for them to "modify".  The exceptions to this are
					// when vk is a mouse button (e.g. sending !{LButton} or #{LButton}).  But both of those are
					// so rare that the flexibility of doing exactly what the script specifies seems better than
					// a possibly unwanted disguising.  Also note that hotkeys such as #LButton automatically use
					// both hooks so that the Start Menu doesn't appear when the Win key is released, so we're
					// not responsible for that type of disguising here.
					SetModifierLRState(modifiersLRSpecified, sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState()
									   , targetWindow, false, true, sendLevel != 0 ? KeyIgnoreLevel(sendLevel) : KeyIgnore); // See keyboard_mouse.h for explanation of KEY_IGNORE.
					// Above: Fixed for v1.1.27 to use KEY_IGNORE except when SendLevel is non-zero (since that
					// would indicate that the script probably wants to trigger a hotkey).  KEY_IGNORE is used
					// (and was prior to v1.1.06.00) to prevent the temporary modifier state changes here from
					// interfering with the use of hotkeys while a Send is in progress.
					// SetModifierLRState() also does DoKeyDelay(g->PressDuration).
				}

				// v1.0.42.04: Mouse clicks are now handled here in the same loop as keystrokes so that the modifiers
				// will be readjusted (above) if the user presses/releases modifier keys during the mouse clicks.
				if (vkIsMouse && targetWindow == 0)
				{
					MouseClick(vk, x, y, 1, tv.defaultMouseSpeed, eventType, moveOffset);
				}
				// Above: Since it's rare to send more than one click, it seems best to simplify and reduce code size
				// by not doing more than one click at a time event when mode is SendInput/Play.
				else
				{
					// Sending mouse clicks via ControlSend is not supported, so in that case fall back to the
					// old method of sending the VK directly (which probably has no effect 99% of the time):
					SendKeyEvent(eventType, vk, sc, targetWindow, true, KeyIgnoreLevel(sendLevel));
				}
			} // for() [aRepeatCount]

			// The final iteration by the above loop does a key or mouse delay (KeyEvent and MouseClick do it internally)
			// prior to us changing the modifiers below.  This is a good thing because otherwise the modifiers would
			// sometimes be released so soon after the keys they modify that the modifiers are not in effect.
			// This can be seen sometimes when/ ctrl-shift-tabbing back through a multi-tabbed dialog:
			// The last ^+{tab} might otherwise not take effect because the CTRL key would be released too quickly.

			// Release any modifiers that were pressed down just for the sake of the above
			// event (i.e. leave any persistent modifiers pressed down).  The caller should
			// already have verified that modifiersLR does not contain any of the modifiers
			// in modifiersLRPersistent.  Also, call GetModifierLRState() again explicitly
			// rather than trying to use a saved value from above, in case the above itself
			// changed the value of the modifiers (i.e. aVk/aSC is a modifier).  Admittedly,
			// that would be pretty strange but it seems the most correct thing to do (another
			// reason is that the user may have pressed or released modifier keys during the
			// final mouse/key delay that was done above).
			if (keyAsModifiersLR == 0) // See prior use of this var for explanation.
			{
				// It seems best not to use KEY_IGNORE_ALL_EXCEPT_MODIFIER in this case, though there's
				// a slight chance that a script or two might be broken by not doing so.  The chance
				// is very slight because the only thing KEY_IGNORE_ALL_EXCEPT_MODIFIER would allow is
				// something like the following example.  Note that the hotkey below must be a hook
				// hotkey (even more rare) because registered hotkeys will still see the logical modifier
				// state and thus fire regardless of whether g_modifiersLR_logical_non_ignored says that
				// they shouldn't:
				// #b::Send, {CtrlDown}{AltDown}
				// $^!a::MsgBox You pressed the A key after pressing the B key.
				// In the above, making ^!a a hook hotkey prevents it from working in conjunction with #b.
				// UPDATE: It seems slightly better to have it be KEY_IGNORE_ALL_EXCEPT_MODIFIER for these reasons:
				// 1) Persistent modifiers are fairly rare.  When they're in effect, it's usually for a reason
				//    and probably a pretty good one and from a user who knows what they're doing.
				// 2) The condition that g_modifiersLR_logical_non_ignored was added to fix occurs only when
				//    the user physically presses a suffix key (or auto-repeats one by holding it down)
				//    during the course of a SendKeys() operation.  Since the persistent modifiers were
				//    (by definition) already in effect prior to the Send, putting them back down for the
				//    purpose of firing hook hotkeys does not seem unreasonable, and may in fact add value.
				// DISGUISE DOWN: When SetModifierLRState() is called below, it should only release keys, not press
				// any down (except if the user's physical keystrokes interfered).  Therefore, passing true or false
				// for the disguise-down-events parameter doesn't matter much (but pass "true" in case the user's
				// keystrokes did interfere in a way that requires a Alt or Win to be pressed back down, because
				// disguising it seems best).
				// DISGUISE UP: When SetModifierLRState() is called below, it is passed "false" for disguise-up
				// to avoid generating unnecessary disguise-keystrokes.  They are not needed because if our keystrokes
				// were modified by either WIN or ALT, the release of the WIN or ALT key will already be disguised due to
				// its having modified something while it was down.  The exceptions to this are when vk is a mouse button
				// (e.g. sending !{LButton} or #{LButton}).  But both of those are so rare that the flexibility of doing
				// exactly what the script specifies seems better than a possibly unwanted disguising.
				// UPDATE for v1.0.42.04: Only release Win and Alt (if appropriate), not Ctrl and Shift, since we know
				// Win/Alt don't have to be disguised but our caller would have trouble tracking that info or making that
				// determination.  This avoids extra keystrokes, while still procrastinating the release of Ctrl/Shift so
				// that those can be left down if the caller's next keystroke happens to need them.
				var stateNow = sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState();
				var winAltToBeReplaced = (stateNow & ~modifiersLRPersistent) // The modifiers to be released...
										 & (MOD_LWIN | MOD_RWIN | MOD_LALT | MOD_RALT); // ... but restrict them to only Win/Alt.

				if (winAltToBeReplaced != 0)
				{
					// Originally used the following for mods new/now: state_now & ~winAltToBeReplaced, state_now
					// When AltGr is to be released, the above formula passes LCtrl+RAlt as the current state and just
					// LCtrl as the new state, which results in LCtrl being pushed back down after it is released via
					// AltGr.  Although our caller releases LCtrl if needed, it usually uses KEY_IGNORE, so if we put
					// LCtrl down here, it would be wrongly stuck down in g_modifiersLR_logical_non_ignored, which
					// causes ^-modified hotkeys to fire when they shouldn't and prevents non-^ hotkeys from firing.
					// By ignoring the current modifier state and only specifying the modifiers we want released,
					// we avoid any chance of sending any unwanted key-down:
					SetModifierLRState(0u, winAltToBeReplaced, targetWindow, true, false); // It also does DoKeyDelay(g->PressDuration).
				}
			}
		}

		// For #MenuMaskKey.
		internal virtual void SendKeyEventMenuMask(KeyEventTypes eventType, long extraInfo = KeyIgnoreAllExceptModifier) => SendKeyEvent(eventType, menuMaskKeyVK, menuMaskKeySC, 0, false, extraInfo);

		internal virtual void AttachTargetWindowThread(ref bool threadsAreAttached, ref uint keybdLayoutThread, ref WindowItemBase tempitem, nint targetWindow)
		{
			// Default implementation does nothing.
		}

		internal virtual void DetachTargetWindowThread(uint mainThread, uint targetThread)
		{
			// Default implementation does nothing.
		}

		internal virtual void SendCharToTargetWindow(char ch, nint targetWindow)
		{
			// Default implementation does nothing.
		}

		/// <summary>
		/// thisHotkeyModifiersLR, if non-zero,
		/// should be the set of modifiers used to trigger the hotkey that called the subroutine
		/// containing the Send that got us here.  If any of those modifiers are still down,
		/// they will be released prior to sending the batch of keys specified in <keys>.
		/// v1.0.43: sendModeOrig was added.
		/// </summary>
		/// <param name="keys"></param>
		/// <param name="sendRaw"></param>
		/// <param name="sendModeOrig"></param>
		/// <param name="targetWindow"></param>
		internal virtual void SendKeys(string keys, SendRawModes sendRaw, SendModes sendModeOrig, nint targetWindow)
		{
			if (keys?.Length == 0)
				return;

			var script = Script.TheScript;
			var origLastPeekTime = script.lastPeekTime;
			var modsExcludedFromBlind = 0u;// For performance and also to reserve future flexibility, recognize {Blind} only when it's the first item in the string.
			var i = 0;
			var sub = keys.AsSpan();
			var ht = script.HookThread;

			if (inBlindMode = ((sendRaw == SendRawModes.NotRaw) && keys.StartsWith("{Blind", StringComparison.OrdinalIgnoreCase))) // Don't allow {Blind} while in raw mode due to slight chance {Blind} is intended to be sent as a literal string.
			{
				// Blind Mode (since this seems too obscure to document, it's mentioned here):  Blind Mode relies
				// on modifiers already down for something like ^c because ^c is saying "manifest a ^c", which will
				// happen if ctrl is already down.  By contrast, Blind does not release shift to produce lowercase
				// letters because avoiding that adds flexibility that couldn't be achieved otherwise.
				// Thus, ^c::Send {Blind}c produces the same result when ^c is substituted for the final c.
				// But Send {Blind}{LControl down} will generate the extra events even if ctrl already down.
				var modMask = MODLR_MASK;
				var keySpan = keys.AsSpan(6);

				for (i = 0; i < keySpan.Length && keySpan[i] != '}'; ++i)
				{
					uint mod = 0;

					switch (keySpan[i])
					{
						case '<': modMask = MODLR_LMASK; continue;

						case '>': modMask = MODLR_RMASK; continue;

						case '^': mod = MOD_LCONTROL | MOD_RCONTROL; break;

						case '+': mod = MOD_LSHIFT | MOD_RSHIFT; break;

						case '!': mod = MOD_LALT | MOD_RALT; break;

						case '#': mod = MOD_LWIN | MOD_RWIN; break;

						case '\0': return; // Just ignore the error.
					}

					modsExcludedFromBlind |= mod & modMask;
					modMask = MODLR_MASK; // Reset for the next modifier.
				}

				sub = keySpan.Slice(i);
			}

			if ((sendRaw == SendRawModes.NotRaw) && sub.StartsWith("{Text}", StringComparison.OrdinalIgnoreCase))
			{
				// Setting this early allows CapsLock and the Win+L workaround to be skipped:
				sendRaw = SendRawModes.RawText;
				sub = sub.Slice(6);
			}

			var tv = script.Threads.CurrentThread.configData;
			var origKeyDelay = tv.keyDelay;
			var origPressDuration = tv.keyDuration;

			if (sendModeOrig == SendModes.Input || sendModeOrig == SendModes.InputThenPlay) // Caller has ensured aTargetWindow==NULL for SendInput and SendPlay modes.
			{
				// Both of these modes fall back to a different mode depending on whether some other script
				// is running with a keyboard/mouse hook active.  Of course, the detection of this isn't foolproof
				// because older versions of AHK may be running and/or other apps with LL keyboard hooks. It's
				// just designed to add a lot of value for typical usage because SendInput is preferred due to it
				// being considerably faster than SendPlay, especially for long replacements when the CPU is under
				// heavy load.
				if (ht.SystemHasAnotherKeybdHook() // This function has been benchmarked to ensure it doesn't yield our timeslice, etc.  200 calls take 0ms according to tick-count, even when CPU is maxed.
						|| ((sendRaw == SendRawModes.NotRaw) && ht.SystemHasAnotherMouseHook() && sub.Contains("{Click", StringComparison.OrdinalIgnoreCase))) // Ordered for short-circuit boolean performance.  v1.0.43.09: Fixed to be strcasestr vs. !strcasestr
				{
					// Need to detect in advance what type of array to build (for performance and code size).  That's why
					// it done this way, and here are the comments about it:
					// strcasestr() above has an unwanted amount of overhead if aKeys is huge, but it seems acceptable
					// because it's called only when system has another mouse hook but *not* another keybd hook (very rare).
					// Also, for performance reasons, {LButton and such are not checked for, which is documented and seems
					// justified because the new {Click} method is expected to become prevalent, especially since this
					// whole section only applies when the new SendInput mode is in effect.
					// Finally, checking aSendRaw isn't foolproof because the string might contain {Raw} prior to {Click,
					// but the complexity and performance of checking for that seems unjustified given the rarity,
					// especially since there are almost never any consequences to reverting to hook mode vs. SendInput.
					if (sendModeOrig == SendModes.InputThenPlay)
						sendModeOrig = SendModes.Play;
					else // aSendModeOrig == SM_INPUT, so fall back to EVENT.
					{
						sendModeOrig = SendModes.Event;
						// v1.0.43.08: When SendInput reverts to SendEvent mode, the majority of users would want
						// a fast sending rate that is more comparable to SendInput's speed that the default KeyDelay
						// of 10ms.  PressDuration may be generally superior to KeyDelay because it does a delay after
						// each changing of modifier state (which tends to improve reliability for certain apps).
						// The following rules seem likely to be the best benefit in terms of speed and reliability:
						// KeyDelay 0+,-1+ --> -1, 0
						// KeyDelay -1, 0+ --> -1, 0
						// KeyDelay -1,-1 --> -1, -1
						tv.keyDuration = (tv.keyDelay < 0L && tv.keyDuration < 0L) ? -1L : 0L;
						tv.keyDelay = -1L; // Above line must be done before this one.
					}
				}
				else // SendInput is available and no other impacting hooks are obviously present on the system, so use SendInput unconditionally.
					sendModeOrig = SendModes.Input; // Resolve early so that other sections don't have to consider SM_INPUT_FALLBACK_TO_PLAY a valid value.
			}

			// Might be better to do this prior to changing capslock state.  UPDATE: In v1.0.44.03, the following section
			// has been moved to the top of the function because:
			// 1) For ControlSend, GetModifierLRState() might be more accurate if the threads are attached beforehand.
			// 2) Determines sTargetKeybdLayout and sTargetLayoutHasAltGr early (for maintainability).
			var threadsAreAttached = false; // Set default.
			uint keybdLayoutThread = 0;     //
			WindowItemBase tempitem = null;
			var pd = script.ProcessesData;

			if (targetWindow != 0) // Caller has ensured this is NULL for SendInput and SendPlay modes.
			{
				AttachTargetWindowThread(ref threadsAreAttached, ref keybdLayoutThread, ref tempitem, targetWindow);
			}
			else
			{
				// v1.0.48.01: On Vista or later, work around the fact that an "L" keystroke (physical or artificial) will
				// lock the computer whenever either Windows key is physically pressed down (artificially releasing the
				// Windows key isn't enough to solve it because Win+L is apparently detected aggressively like
				// Ctrl-Alt-Delete.  Unlike the handling of SM_INPUT in another section, this one here goes into
				// effect for all Sends because waiting for an "L" keystroke to be sent would be too late since the
				// Windows would have already been artificially released by then, so IsKeyDownAsync() wouldn't be
				// able to detect when the user physically releases the key.
				if ((thisHotkeyModifiersLR & (MOD_LWIN | MOD_RWIN)) != 0 // Limit the scope to only those hotkeys that have a Win modifier, since anything outside that scope hasn't been fully analyzed.
						&& (DateTime.UtcNow - script.thisHotkeyStartTime).TotalMilliseconds < 50 // Ensure g_script.mThisHotkeyModifiersLR is up-to-date enough to be reliable.
						&& sendModeOrig != SendModes.Play // SM_PLAY is reported to be incapable of locking the computer.
						&& !inBlindMode // The philosophy of blind-mode is that the script should have full control, so don't do any waiting during blind mode.
						&& sendRaw != SendRawModes.RawText // {Text} mode does not trigger Win+L.
						&& PlatformManager.CurrentThreadId() == pd.MainThreadID // Exclude the hook thread because it isn't allowed to call anything like MsgSleep, nor are any calls from the hook thread within the understood/analyzed scope of this workaround.
				   )
				{
					var waitForWinKeyRelease = false;

					if (sendRaw != SendRawModes.NotRaw)
					{
						waitForWinKeyRelease = sub.IndexOfAny(llChars) != -1; // StrChrAny(aKeys, ("Ll")) != NULL;
					}
					else
					{
						// It seems worthwhile to scan for any "L" characters to avoid waiting for the release
						// of the Windows key when there are no L's.  For performance and code size, the check
						// below isn't comprehensive (e.g. it fails to consider things like {L} and #L).
						// Although RegExMatch() could be used instead of the below, that would use up one of
						// the RegEx cache entries, plus it would probably perform worse.  So scan manually.
						int L_pos, brace_pos = 0;
						var bracesub = sub;

						for (waitForWinKeyRelease = false; (L_pos = bracesub.IndexOfAny(llCharsSv, brace_pos)) != -1;)
						{
							// Encountering a #L seems too rare, and the consequences too mild (or nonexistent), to
							// justify the following commented-out section:
							//if (L_pos > aKeys && L_pos[-1] == '#') // A simple check; it won't detect things like #+L.
							//  brace_pos = L_pos + 1;
							//else
							brace_pos = bracesub.IndexOfAny(bracecharsSv, L_pos + 1);

							if (brace_pos == -1 || bracesub[brace_pos] == '{') // See comment below.
							{
								waitForWinKeyRelease = true;
								break;
							}

							//else it found a '}' without a preceding '{', which means this "L" is inside braces.
							// For simplicity, ignore such L's (probably not a perfect check, but seems worthwhile anyway).
						}
					}

					if (waitForWinKeyRelease)
						while (ht.IsKeyDownAsync(VK_LWIN) || ht.IsKeyDownAsync(VK_RWIN)) // Even if the keyboard hook is installed, it seems best to use IsKeyDownAsync() vs. g_PhysicalKeyState[] because it's more likely to produce consistent behavior.
							Flow.SleepWithoutInterruption(Flow.intervalUnspecified); // Seems best not to allow other threads to launch, for maintainability and because SendKeys() isn't designed to be interruptible.
				}

				// v1.0.44.03: The following change is meaningful only to people who use more than one keyboard layout.
				// It seems that the vast majority of them would want the Send command (as well as other features like
				// Hotstrings and the Input command) to adapt to the keyboard layout of the active window (or target window
				// in the case of ControlSend) rather than sticking with the script's own keyboard layout.  In addition,
				// testing shows that this adapt-to-layout method costs almost nothing in performance, especially since
				// the active window, its thread, and its layout are retrieved only once for each Send rather than once
				// for each keystroke.
				// v1.1.27.01: Use the thread of the focused control, which may differ from the active window.
				nint tempzero = 0;
				keybdLayoutThread = WindowManager.GetFocusedCtrlThread(ref tempzero, 0);
			}

			targetKeybdLayout = PlatformManager.GetKeyboardLayout(keybdLayoutThread); // If keybd_layout_thread==0, this will get our thread's own layout, which seems like the best/safest default.
			targetLayoutHasAltGr = LayoutHasAltGr(targetKeybdLayout);  // Note that WM_INPUTLANGCHANGEREQUEST is not monitored by MsgSleep for the purpose of caching our thread's keyboard layout.  This is because it would be unreliable if another msg pump such as MsgBox is running.  Plus it hardly helps perf. at all, and hurts maintainability.
			// Below is now called with "true" so that the hook's modifier state will be corrected (if necessary)
			// prior to every send.
			var modsCurrent = GetModifierLRState(true); // Current "logical" modifier state.
			// For any modifiers put in the "down" state by {xxx DownR}, keep only those which
			// are still logically down before each Send starts.  Otherwise each Send would reset
			// the modifier to "down" even after the user "releases" it by some other means.
			modifiersLRRemapped &= modsCurrent;
			// Make a best guess of what the physical state of the keys is prior to starting (there's no way
			// to be certain without the keyboard hook). Note: It's possible for a key to be down physically
			// but not logically such as when RControl, for example, is a suffix hotkey and the user is
			// physically holding it down.
			uint modsDownPhysicallyOrig, modsDownPhysicallyAndLogically, modsDownPhysicallyButNotLogicallyOrig;
			var ad = script.AccessorData;

			//if (hookId != 0)
			if (ht.HasKbdHook())
			{
				// Since hook is installed, use its more reliable tracking to determine which
				// modifiers are down.
				modsDownPhysicallyOrig = modifiersLRPhysical;
				modsDownPhysicallyAndLogically = modifiersLRPhysical & modifiersLRLogical;
				modsDownPhysicallyButNotLogicallyOrig = modifiersLRPhysical & ~modifiersLRLogical;
			}
			else // Use best-guess instead.
			{
				// Even if TickCount has wrapped due to system being up more than about 49 days,
				// DWORD subtraction still gives the right answer as long as g_script.mThisHotkeyStartTime
				// itself isn't more than about 49 days ago:
				if ((DateTime.UtcNow - script.thisHotkeyStartTime).TotalMilliseconds < ad.hotkeyModifierTimeout) // Elapsed time < timeout-value
					modsDownPhysicallyOrig = modsCurrent & thisHotkeyModifiersLR; // Bitwise AND is set intersection.
				else
					// Since too much time as passed since the user pressed the hotkey, it seems best,
					// based on the action that will occur below, to assume that no hotkey modifiers
					// are physically down:
					modsDownPhysicallyOrig = 0;

				modsDownPhysicallyAndLogically = modsDownPhysicallyOrig;
				modsDownPhysicallyButNotLogicallyOrig = 0; // There's no way of knowing, so assume none.
			}

			// Any of the external modifiers that are down but NOT due to the hotkey are probably
			// logically down rather than physically (perhaps from a prior command such as
			// `Send "{CtrlDown}"`.  Since there's no way to be sure without the keyboard hook
			// or some driver-level monitoring, it seems best to assume that they are logically
			// vs. physically down.
			// persistent_modifiers_for_this_SendKeys contains the modifiers that we will not
			// attempt to change (e.g. `Send "A"` will not release LWin before sending "A" if
			// this value indicates that LWin is down).
			// This used to be = modifiersLR_current & ~modifiersLR_down_physically_and_logically,
			// but v1.0.13 added g_modifiersLR_persistent to limit it to only mods which the script
			// put into effect.  Old comment explains: [[ To improve the above, we now exclude from
			// the set of persistent modifiers any that weren't made persistent by this script.
			// Such a policy seems likely to do more good than harm as there have been cases where
			// a modifier was detected as persistent just because A_HotkeyModifierTimeout expired
			// while the user was still holding down the key, but then when the user released it,
			// this logic here would think it's still persistent and push it back down again
			// to enforce it as "always-down" during the send operation.  Thus, the key would
			// basically get stuck down even after the send was over. ]]
			// v2.0.13: The original bitmask ~modifiersLR_down_physically_and_logically is removed.
			// It was originally described as "all the down-keys in mods_current except any that are
			// physically down due to the hotkey itself".  For example, ^a::Send,b requires the user
			// to hold Ctrl to activate the hotkey, so he probably doesn't want to send ^b.
			// However, sModifiersLR_persistent achieves the purpose of preventing Ctrl from being
			// sent in such cases, while the original bitmask only serves to prevent {Ctrl Down} from
			// being persistent if the user happens to physically hold Ctrl (for any purpose).
			// For example, when Send "{Shift Down}" and *KEY::Send "a" are used in combination,
			// the result should be "A" regardless of whether KEY = Shift.
			modifiersLRPersistent &= modsCurrent & ~modsDownPhysicallyAndLogically;
			uint persistentModifiersForThisSendKeys;
			var modsReleasedForSelectiveBlind = 0u;

			if (inBlindMode)
			{
				// The following value is usually zero unless the user is currently holding down
				// some modifiers as part of a hotkey. These extra modifiers are the ones that
				// this send operation (along with all its calls to SendKey and similar) should
				// consider to be down for the duration of the Send (unless they go up via an
				// explicit {LWin up}, etc.)
				persistentModifiersForThisSendKeys = modsCurrent;

				if (modsExcludedFromBlind != 0) // Caller specified modifiers to exclude from Blind treatment.
				{
					persistentModifiersForThisSendKeys &= ~modsExcludedFromBlind;
					modsReleasedForSelectiveBlind = modsCurrent ^ persistentModifiersForThisSendKeys;
				}
			}
			else
			{
				persistentModifiersForThisSendKeys = modifiersLRPersistent;
			}

			// Above:
			// Keep sModifiersLR_persistent and persistent_modifiers_for_this_SendKeys in sync with each other from now on.
			// By contrast to persistent_modifiers_for_this_SendKeys, sModifiersLR_persistent is the lifetime modifiers for
			// this script that stay in effect between sends.  For example, "Send {LAlt down}" leaves the alt key down
			// even after the Send ends, by design.
			//
			// It seems best not to change persistent_modifiers_for_this_SendKeys in response to the user making physical
			// modifier changes during the course of the Send.  This is because it seems more often desirable that a constant
			// state of modifiers be kept in effect for the entire Send rather than having the user's release of a hotkey
			// modifier key, which typically occurs at some unpredictable time during the Send, to suddenly alter the nature
			// of the Send in mid-stride.  Another reason is to make the behavior of Send consistent with that of SendInput.
			// The default behavior is to turn the capslock key off prior to sending any keys
			// because otherwise lowercase letters would come through as uppercase and vice versa.
			// Remember that apps like MS Word have an auto-correct feature that might make it
			// wrongly seem that the turning off of Capslock below needs a Sleep(0) to take effect.
			var priorCapslockState = tv.storeCapsLockMode && !inBlindMode && sendRaw != SendRawModes.RawText
									 ? ToggleKeyState(VK_CAPITAL, ToggleValueType.Off)
									 : ToggleValueType.Invalid; // In blind mode, don't do store capslock (helps remapping and also adds flexibility).
			// sendMode must be set only after setting Capslock state above, because the hook method
			// is incapable of changing the on/off state of toggleable keys like Capslock.
			// However, it can change Capslock state as seen in the window to which playback events are being
			// sent; but the behavior seems inconsistent and might vary depending on OS type, so it seems best
			// not to rely on it.
			sendMode = sendModeOrig;

			if (sendMode != SendModes.Event) // Build an array.  We're also responsible for setting sendMode to SM_EVENT prior to returning.
			{
				maxEvents = sendMode == SendModes.Input ? MaxInitialEventsSI : MaxInitialEventsPB;
				InitEventArray(maxEvents, modsCurrent);
			}

			var kbd = script.KeyboardData;
			var blockinputPrev = kbd.blockInput;
			var doSelectiveBlockInput = (kbd.blockInputMode == ToggleValueType.Send || kbd.blockInputMode == ToggleValueType.SendAndMouse)
										&& sendMode == SendModes.Event && targetWindow == 0;

			if (doSelectiveBlockInput)
				_ = Core.Keyboard.ScriptBlockInput(true); // Turn it on unconditionally even if it was on, since Ctrl-Alt-Del might have disabled it.

			var vk = 0u;
			var sc = 0u;
			var keyAsModifiersLR = 0u;
			uint? modsForNextKey = 0u;
			// Above: For v1.0.35, it was changed to modLR vs. mod so that AltGr keys such as backslash and '{'
			// are supported on layouts such as German when sending to apps such as Putty that are fussy about
			// which ALT key is held down to produce the character.
			var thisEventModifierDown = 0u;
			var keyTextLength = 0;
			var keyNameLength = 0;
			int endPos;//, spacePos;
			//char oldChar;
			var keyDownType = KeyDownTypes.Temp;
			var eventType = KeyEventTypes.KeyDown;
			long repeatCount = 0;
			int clickX = 0, clickY = 0;
			var moveOffset = false;
			uint placeholder = 0;
			//var msg = new Msg();//May not be needed.//TOOD
			var keyIndex = 0;

			//for (; *aKeys; ++aKeys, prevEventModifierDown = this_event_modifier_down)
			for (; keyIndex < sub.Length; ++keyIndex, prevEventModifierDown = thisEventModifierDown)
			{
				thisEventModifierDown = 0; // Set default for this iteration, overridden selectively below.

				if (sendMode == SendModes.Event)
					LongOperationUpdateForSendKeys(); // This does not measurably affect the performance of SendPlay/Event.

				var ch = sub[keyIndex];

				if (sendRaw == SendRawModes.NotRaw && sendKeyChars.Contains(ch))//  _tcschr(("^+!#{}"), *aKeys))
				{
					switch (ch)
					{
						case '^':
							if ((persistentModifiersForThisSendKeys & (MOD_LCONTROL | MOD_RCONTROL)) == 0)
								modsForNextKey |= MOD_LCONTROL;

							// else don't add it, because the value of mods_for_next_key may also used to determine
							// which keys to release after the key to which this modifier applies is sent.
							// We don't want persistent modifiers to ever be released because that's how
							// AutoIt2 behaves and it seems like a reasonable standard.
							continue;

						case '+':
							if ((persistentModifiersForThisSendKeys & (MOD_LSHIFT | MOD_RSHIFT)) == 0)
								modsForNextKey |= MOD_LSHIFT;

							continue;

						case '!':
							if ((persistentModifiersForThisSendKeys & (MOD_LALT | MOD_RALT)) == 0)
								modsForNextKey |= MOD_LALT;

							continue;

						case '#':
							if ((persistentModifiersForThisSendKeys & (MOD_LWIN | MOD_RWIN)) == 0)
								modsForNextKey |= MOD_LWIN;

							continue;

						case '}': continue;  // Important that these be ignored.  Be very careful about changing this, see below.

						case '{':
						{
							if ((endPos = sub.IndexOf('}', keyIndex + 1)) == -1) // Ignore it and due to rarity, don't reset mods_for_next_key.
								continue; // This check is relied upon by some things below that assume a '}' is present prior to the terminator.

							keyIndex = sub.FindFirstNotOf(SpaceTab, keyIndex + 1);

							if ((keyTextLength = (endPos - keyIndex)) == 0)
							{
								var lenok = sub.Length > endPos + 1;

								if (lenok && sub[endPos + 1] == '}')
								{
									// The literal string "{}}" has been encountered, which is interpreted as a single "}".
									++endPos;
									keyTextLength = 1;
								}
								else
								{
									var nextWord = ReadOnlySpan<char>.Empty;
									var braceTabIndex = sub.IndexOfAny(SpaceTab);

									if (braceTabIndex != -1)
										nextWord = sub.Slice(braceTabIndex).TrimStart();

									if (nextWord.Length > 0)// v1.0.48: Support "{} down}", "{} downtemp}" and "{} up}".
									{
										if (nextWord.StartsWith("Down", StringComparison.OrdinalIgnoreCase) // "Down" or "DownTemp" (or likely enough).
												|| nextWord.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
										{
											if ((endPos = sub.IndexOf('}', keyIndex + 2)) == -1)//See comments at similar section above.
												continue;

											keyTextLength = endPos - keyIndex; // This result must be non-zero due to the checks above.
										}
										else
											goto bracecaseend;  // The loop's ++aKeys will now skip over the '}', ignoring it.
									}
									else // Empty braces {} were encountered (or all whitespace, but literal whitespace isn't sent).
										goto bracecaseend;  // The loop's ++aKeys will now skip over the '}', ignoring it.
								}
							}

							var subspan = sub.Slice(keyIndex, keyTextLength);

							if (subspan.StartsWith("Click", StringComparison.OrdinalIgnoreCase))
							{
								HookThread.ParseClickOptions(subspan.Slice(5).TrimStart(SpaceTab), ref clickX, ref clickY, ref vk
													 , ref eventType, ref repeatCount, ref moveOffset);

								if (repeatCount < 1) // Allow {Click 100, 100, 0} to do a mouse-move vs. click (but modifiers like ^{Click..} aren't supported in this case.
									MouseMove(ref clickX, ref clickY, ref placeholder, tv.defaultMouseSpeed, moveOffset);
								else // Use SendKey because it supports modifiers (e.g. ^{Click}) SendKey requires repeat_count>=1.
									SendKey(vk, 0, modsForNextKey.Value, persistentModifiersForThisSendKeys
											, repeatCount, eventType, 0, targetWindow, clickX, clickY, moveOffset);

								goto bracecaseend; // This {} item completely handled, so move on to next.
							}
							else if (subspan.StartsWith("Raw", StringComparison.OrdinalIgnoreCase)) // This is used by auto-replace hotstrings too.
							{
								// As documented, there's no way to switch back to non-raw mode afterward since there's no
								// correct way to support special (non-literal) strings such as {Raw Off} while in raw mode.
								sendRaw = SendRawModes.Raw;
								goto bracecaseend; // This {} item completely handled, so move on to next.
							}
							else if (subspan.StartsWith("Text", StringComparison.OrdinalIgnoreCase)) // Added in v1.1.27
							{
								if (subspan.Slice(4).TrimStart(SpaceTab).Length == 0)//Pointing at the closing '}'.
									sendRaw = SendRawModes.RawText;

								//else: ignore this {Text something} to reserve for future use.
								goto bracecaseend; // This {} item completely handled, so move on to next.
							}

							// Since above didn't "goto", this item isn't {Click}.
							eventType = KeyEventTypes.KeyDownAndUp;         // Set defaults.
							repeatCount = 1L;
							keyNameLength = keyTextLength;
							var splitct = 0;
							var firstSplit = ReadOnlySpan<char>.Empty;

							foreach (System.Range r in subspan.SplitAny(SpaceTab))
							{
								var split = subspan[r].Trim();

								if (split.Length > 0)
								{
									if (splitct == 0)
									{
										keyNameLength = split.Length;
										firstSplit = split;
									}
									else
									{
										var nextWord = split;
										subspan = firstSplit;

										if (nextWord.StartsWith("Down", StringComparison.OrdinalIgnoreCase))
										{
											eventType = KeyEventTypes.KeyDown;

											// v1.0.44.05: Added key_down_is_persistent (which is not initialized except here because
											// it's only applicable when event_type==KEYDOWN).  It avoids the following problem:
											// When a key is remapped to become a modifier (such as F1::Control), launching one of
											// the script's own hotkeys via F1 would lead to bad side-effects if that hotkey uses
											// the Send command. This is because the Send command assumes that any modifiers pressed
											// down by the script itself (such as Control) are intended to stay down during all
											// keystrokes generated by that script. To work around this, something like KeyWait F1
											// would otherwise be needed. within any hotkey triggered by the F1 key.
											if (nextWord.StartsWith("DownTemp", StringComparison.OrdinalIgnoreCase)) // "DownTemp" means non-persistent.
												keyDownType = KeyDownTypes.Temp;
											else if (nextWord.Length > 4 && char.ToUpper(nextWord[4]) == 'R') // "DownR" means treated as a physical modifier (R = remap); i.e. not kept down during Send, but restored after Send (unlike Temp).
												keyDownType = KeyDownTypes.Remap;
											else
												keyDownType = KeyDownTypes.Persistent;
										}
										else if (nextWord.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
										{
											eventType = KeyEventTypes.KeyUp;
										}
										else if (!subspan.StartsWith("ASC", StringComparison.OrdinalIgnoreCase))
										{
											if (long.TryParse(nextWord, out var templ))
											{
												repeatCount = templ;//.Value;
											}
											else
											{
												_ = Dialogs.MsgBox($"Invalid character passed to Send(): {nextWord}", null, "16");
												return;
											}
										}
									}

									splitct++;
									// Above: If negative or zero, that is handled further below.
									// There is no complaint for values <1 to support scripts that want to conditionally send
									// zero keystrokes, e.g. Send {a %Count%}
								}
							}

							_ = ht.TextToVKandSC(subspan, ref vk, ref sc, ref modsForNextKey, targetKeybdLayout);

							if (repeatCount < 1L)
								goto bracecaseend; // Gets rid of one level of indentation. Well worth it.

							subspan = sub.Slice(1).TrimStart(SpaceTab);//Consider the entire string, minus the first {, below.

							if (vk != 0 || sc != 0)
							{
								bool? b = null;

								if ((keyAsModifiersLR = ht.KeyToModifiersLR(vk, sc, ref b)) != 0) // Assign
								{
									if (targetWindow == 0)
									{
										if (eventType == KeyEventTypes.KeyDown) // i.e. make {Shift down} have the same effect {ShiftDown}
										{
											thisEventModifierDown = vk;

											if (keyDownType == KeyDownTypes.Persistent) // v1.0.44.05.
												modifiersLRPersistent |= keyAsModifiersLR;
											else if (keyDownType == KeyDownTypes.Remap) // v1.1.27.00
												modifiersLRRemapped |= keyAsModifiersLR;

											persistentModifiersForThisSendKeys |= keyAsModifiersLR; // v1.0.44.06: Added this line to fix the fact that "DownTemp" should keep the key pressed down after the send.
										}
										else if (eventType == KeyEventTypes.KeyUp) // *not* KEYDOWNANDUP, since that would be an intentional activation of the Start Menu or menu bar.
										{
											DisguiseWinAltIfNeeded(vk);
											modifiersLRPersistent &= ~keyAsModifiersLR;
											modifiersLRRemapped &= ~keyAsModifiersLR;
											persistentModifiersForThisSendKeys &= ~keyAsModifiersLR;

											// Fix for v1.0.43: Also remove LControl if this key happens to be AltGr.
											if (vk == VK_RMENU && targetLayoutHasAltGr == ResultType.ConditionTrue) // It is AltGr.
												persistentModifiersForThisSendKeys &= ~MOD_LCONTROL;
										}

										// else must never change sModifiersLR_persistent in response to KEYDOWNANDUP
										// because that would break existing scripts.  This is because that same
										// modifier key may have been pushed down via {ShiftDown} rather than "{Shift Down}".
										// In other words, {Shift} should never undo the effects of a prior {ShiftDown}
										// or {Shift down}.
									}

									//else don't add this event to sModifiersLR_persistent because it will not be
									// manifest via keybd_event.  Instead, it will done via less intrusively
									// (less interference with foreground window) via SetKeyboardState() and
									// PostMessage().  This change is for ControlSend in v1.0.21 and has been
									// documented.
								}

								// Below: sModifiersLR_persistent stays in effect (pressed down) even if the key
								// being sent includes that same modifier.  Surprisingly, this is how AutoIt2
								// behaves also, which is good.  Example: Send, {AltDown}!f  ; this will cause
								// Alt to still be down after the command is over, even though F is modified
								// by Alt.
								SendKey(vk, sc, modsForNextKey.Value, persistentModifiersForThisSendKeys
										, repeatCount, eventType, keyAsModifiersLR, targetWindow);
							}
							else if (keyNameLength == 1) // No vk/sc means a char of length one is sent via special method.
							{
								// v1.0.40: SendKeySpecial sends only keybd_event keystrokes, not ControlSend style
								// keystrokes.
								// v1.0.43.07: Added check of event_type!=KEYUP, which causes something like Send {� up} to
								// do nothing if the curr. keyboard layout lacks such a key.  This is relied upon by remappings
								// such as F1::� (i.e. a destination key that doesn't have a VK, at least in English).
								if (eventType != KeyEventTypes.KeyUp) // In this mode, mods_for_next_key and event_type are ignored due to being unsupported.
								{
									if (targetWindow != 0)
									{
										// Although MSDN says WM_CHAR uses UTF-16, it seems to really do automatic
										// translation between ANSI and UTF-16; we rely on this for correct results:
										for (var ii = 0L; ii < repeatCount; ++ii)
											SendCharToTargetWindow(subspan[0], targetWindow);
									}
									else
										SendKeySpecial(subspan[0], repeatCount, modsForNextKey.Value | persistentModifiersForThisSendKeys);
								}
							}
							// See comment "else must never change sModifiersLR_persistent" above about why
							// !aTargetWindow is used below:
							else if ((vk = ht.TextToSpecial(subspan, ref eventType
															, ref persistentModifiersForThisSendKeys, targetWindow == 0)) != 0) // Assign.
							{
								if (targetWindow == 0)
								{
									if (eventType == KeyEventTypes.KeyDown)
										thisEventModifierDown = vk;
									else // It must be KEYUP because TextToSpecial() never returns KEYDOWNANDUP.
										DisguiseWinAltIfNeeded(vk);
								}

								// Since we're here, repeat_count > 0.
								// v1.0.42.04: A previous call to SendKey() or SendKeySpecial() might have left modifiers
								// in the wrong state (e.g. Send +{F1}{ControlDown}).  Since modifiers can sometimes affect
								// each other, make sure they're in the state intended by the user before beginning:
								SetModifierLRState(persistentModifiersForThisSendKeys
												   , sendMode == SendModes.Event ? eventModifiersLR : GetModifierLRState()
												   , targetWindow, false, false); // It also does DoKeyDelay(g->PressDuration).

								for (var ii = 0L; ii < repeatCount; ++ii)
								{
									// Don't tell it to save & restore modifiers because special keys like this one
									// should have maximum flexibility (i.e. nothing extra should be done so that the
									// user can have more control):
									SendKeyEvent(eventType, vk, 0, targetWindow, true);

									if (sendMode == SendModes.Event)
										LongOperationUpdateForSendKeys();
								}
							}
							else if (keyTextLength > 4 && subspan.StartsWith("ASC ", StringComparison.OrdinalIgnoreCase) && targetWindow == 0) // {ASC nnnnn}
							{
								// Include the trailing space in "ASC " to increase uniqueness (selectivity).
								// Also, sending the ASC sequence to window doesn't work, so don't even try:
								//GetBytes() should really work with spans but for some reason it doesn't.//.NET 9
								SendASC(Encoding.ASCII.GetBytes(subspan.Slice(3).TrimStart().ToString()));
								// Do this only once at the end of the sequence:
								DoKeyDelay(); // It knows not to do the delay for SM_INPUT.
							}
							else if (keyTextLength > 2 && subspan.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
							{
								// L24: Send a unicode value as shown by Character Map.
								var hexstop = subspan.FirstIndexOf(ch => !ch.IsHex(), 2);
								var hexsub = subspan.Slice(2, hexstop == -1 ? subspan.Length - 2 : hexstop - 2);

								if (long.TryParse(hexsub, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out var uCode))
								{
									char wc1, wc2;

									if (uCode >= 0x10000)
									{
										// Supplementary characters are encoded as UTF-16 and split into two messages.
										uCode -= 0x10000;
										wc1 = (char)(0xd800 + ((uCode >> 10) & 0x3ff));
										wc2 = (char)(0xdc00 + (uCode & 0x3ff));
									}
									else
									{
										wc1 = (char)uCode;
										wc2 = (char)0;
									}

									if (targetWindow != 0)
									{
										// Although MSDN says WM_CHAR uses UTF-16, PostMessageA appears to truncate it to 8-bit.
										// This probably means it does automatic translation between ANSI and UTF-16.  Since we
										// specifically want to send a Unicode character value, use PostMessageW:
										SendCharToTargetWindow(wc1, targetWindow);

										if (wc2 != 0)
											SendCharToTargetWindow(wc2, targetWindow);
									}
									else
									{
										// Use SendInput in unicode mode if available, otherwise fall back to SendASC.
										// To know why the following requires sendMode != SM_PLAY, see SendUnicodeChar.
										if (sendMode != SendModes.Play)
										{
											SendUnicodeChar(wc1, modsForNextKey.Value | persistentModifiersForThisSendKeys);

											if (wc2 != 0)
												SendUnicodeChar(wc2, modsForNextKey.Value | persistentModifiersForThisSendKeys);
										}
										else // Note that this method generally won't work with Unicode characters except
										{
											// with specific controls which support it, such as RichEdit (tested on WordPad).
											var asc = new byte[8];
											asc[0] = (byte)'0';
											var str = ((int)uCode).ToString();
											var bytes = Encoding.ASCII.GetBytes(str);
											System.Array.Copy(bytes, 0, asc, 1, Math.Min(asc.Length - 1, bytes.Length));
											SendASC(asc);
										}
									}

									DoKeyDelay();
								}
								else
								{
									_ = Errors.ErrorOccurred($"Could not parse {hexsub} as a hexadecimal number when trying to send a unicode character.");
									return;
								}
							}

							//else do nothing since it isn't recognized as any of the above "else if" cases (see below).
							// If what's between {} is unrecognized, such as {Bogus}, it's safest not to send
							// the contents literally since that's almost certainly not what the user intended.
							// In addition, reset the modifiers, since they were intended to apply only to
							// the key inside {}.  Also, the below is done even if repeat-count is zero.
							bracecaseend: // This label is used to simplify the code without sacrificing performance.
							keyIndex = endPos;
							modsForNextKey = 0;
							continue;
						} // case '{'
					} // switch()
				} // if (!aSendRaw && strchr("^+!#{}", *aKeys))
				else // Encountered a character other than ^+!#{} ... or we're in raw mode.
				{
					if (sendRaw == SendRawModes.RawText)
					{
						// \b needs to produce VK_BACK for auto-replace hotstrings to work (this is more useful anyway).
						// \r and \n need to produce VK_RETURN for decent compatibility.  SendKeySpecial('\n') works for
						// some controls (such as Scintilla) but has no effect in other common applications.
						// \t has more utility if translated to VK_TAB.  SendKeySpecial('\t') has no effect in many
						// common cases, and seems to only work in cases where {tab} would work just as well.
						if (sub[keyIndex] == '\r' && sub[keyIndex + 1] == '\n') // Translate \r but ignore any trailing \n, since \r\n -> {Enter 2} is counter-intuitive.
							keyIndex++;

						vk = sub[keyIndex] switch
					{
							'\n' => VK_RETURN,
							'\b' => VK_BACK,
							'\t' => VK_TAB,
							_ => 0,
					};
				}
				else
				{
					// Best to call this separately, rather than as first arg in SendKey, since it changes the
					// value of modifiers and the updated value is *not* guaranteed to be passed.
					// In other words, SendKey(TextToVK(...), modifiers, ...) would often send the old
					// value for modifiers.
					vk = ht.CharToVKAndModifiers(sub[keyIndex], ref modsForNextKey, targetKeybdLayout
												 , (modsForNextKey | persistentModifiersForThisSendKeys) != 0 && sendRaw == SendRawModes.NotRaw); // v1.1.27.00: Disable the a-z to vk41-vk5A fallback translation when modifiers are present since it would produce the wrong printable characters.
						// CharToVKAndModifiers() takes no measurable time compared to the amount of time SendKey takes.
					}

					if (vk != 0)
						SendKey(vk, 0, modsForNextKey.Value, persistentModifiersForThisSendKeys, 1, KeyEventTypes.KeyDownAndUp, 0, targetWindow);
					else // Try to send it by alternate means.
					{
						// In this mode, mods_for_next_key is ignored due to being unsupported.
						if (targetWindow != 0)
							// Although MSDN says WM_CHAR uses UTF-16, it seems to really do automatic
							// translation between ANSI and UTF-16; we rely on this for correct results:
							SendCharToTargetWindow(sub[keyIndex], targetWindow);
						else
							SendKeySpecial(sub[keyIndex], 1, modsForNextKey.Value | persistentModifiersForThisSendKeys);
					}

					modsForNextKey = 0;  // Safest to reset this regardless of whether a key was sent.
				}
			} // for()

			uint modsToSet;

			if (sendMode != SendModes.Event)
			{
				var finalKeyDelay = -1L;  // Set default.

				if (!abortArraySend && TotalEventCount() > 0) // Check for zero events for performance, but more importantly because playback hook will not operate correctly with zero.
				{
					// Add more events to the array (prior to sending) to support the following:
					// Restore the modifiers to match those the user is physically holding down, but do it as *part*
					// of the single SendInput/Play call.  The reasons it's done here as part of the array are:
					// 1) It avoids the need for A_HotkeyModifierTimeout (and it's superior to it) for both SendInput
					//    and SendPlay.
					// 2) The hook will not be present during the SendInput, nor can it be reinstalled in time to
					//    catch any physical events generated by the user during the Send. Consequently, there is no
					//    known way to reliably detect physical keystate changes.
					// 3) Changes made to modifier state by SendPlay are seen only by the active window's thread.
					//    Thus, it would be inconsistent and possibly incorrect to adjust global modifier state
					//    after (or during) a SendPlay.
					// So rather than resorting to A_HotkeyModifierTimeout, we can restore the modifiers within the
					// protection of SendInput/Play's uninterruptibility, allowing the user's buffered keystrokes
					// (if any) to hit against the correct modifier state when the SendInput/Play completes.
					// For example, if #c:: is a hotkey and the user releases Win during the SendInput/Play, that
					// release would hit after SendInput/Play restores Win to the down position, and thus Win would
					// not be stuck down.  Furthermore, if the user didn't release Win, Win would be in the
					// correct/intended position.
					// This approach has a few weaknesses (but the strengths appear to outweigh them):
					// 1) Hitting SendInput's 5000 char limit would omit the tail-end keystrokes, which would mess up
					//    all the assumptions here.  But hitting that limit should be very rare, especially since it's
					//    documented and thus scripts will avoid it.
					// 2) SendInput's assumed uninterruptibility is false if any other app or script has an LL hook
					//    installed.  This too is documented, so scripts should generally avoid using SendInput when
					//    they know there are other LL hooks in the system.  In any case, there's no known solution
					//    for it, so nothing can be done.
					modsToSet = persistentModifiersForThisSendKeys
								| modifiersLRRemapped // Restore any modifiers which were put in the down state by remappings or {key DownR} prior to this Send.
								| (inBlindMode ? modsReleasedForSelectiveBlind
								   : (modsDownPhysicallyOrig & ~modsDownPhysicallyButNotLogicallyOrig)); // The last item is usually 0.
					// Above: When in blind mode, don't restore physical modifiers.  This is done to allow a hotkey
					// such as the following to release Shift:
					//    +space::SendInput/Play {Blind}{Shift up}
					// Note that SendPlay can make such a change only from the POV of the target window; i.e. it can
					// release shift as seen by the target window, but not by any other thread; so the shift key would
					// still be considered to be down for the purpose of firing hotkeys (it can't change global key state
					// as seen by GetAsyncKeyState).
					// For more explanation of above, see a similar section for the non-array/old Send below.
					SetModifierLRState(modsToSet, eventModifiersLR, 0, true, true); // Disguise in case user released or pressed Win/Alt during the Send (seems best to do it even for SendPlay, though it probably needs only Alt, not Win).
					// mods_to_set is used further below as the set of modifiers that were explicitly put into effect at the tail end of SendInput.
					SendEventArray(ref finalKeyDelay, modsToSet);
				}

				CleanupEventArray(finalKeyDelay);
			}
			else // A non-array send is in effect, so a more elaborate adjustment to logical modifiers is called for.
			{
				// Determine (or use best-guess, if necessary) which modifiers are down physically now as opposed
				// to right before the Send began.
				var modsDownPhysically = 0u; // As compared to modsDownPhysicallyOrig.

				if (ht.HasKbdHook())
					modsDownPhysically = modifiersLRPhysical;
				else // No hook, so consult g_HotkeyModifierTimeout to make the determination.
					// Assume that the same modifiers that were phys+logically down before the Send are still
					// physically down (though not necessarily logically, since the Send may have released them),
					// but do this only if the timeout period didn't expire (or the user specified that it never
					// times out; i.e. elapsed time < timeout-value; DWORD subtraction gives the right answer even if
					// tick-count has wrapped around).
					modsDownPhysically = (ad.hotkeyModifierTimeout < 0 // It never times out or...
										  || (DateTime.UtcNow - script.thisHotkeyStartTime).TotalMilliseconds < ad.hotkeyModifierTimeout) // It didn't time out.
										 ? modsDownPhysicallyOrig : 0;

				// Put any modifiers in sModifiersLR_remapped back into effect, as if they were physically down.
				modsDownPhysically |= modifiersLRRemapped;
				// Restore the state of the modifiers to be those the user is physically holding down right now.
				// Any modifiers that are logically "persistent", as detected upon entrance to this function
				// (e.g. due to something such as a prior "Send, {LWinDown}"), are also pushed down if they're not already.
				// Don't press back down the modifiers that were used to trigger this hotkey if there's
				// any doubt that they're still down, since doing so when they're not physically down
				// would cause them to be stuck down, which might cause unwanted behavior when the unsuspecting
				// user resumes typing.
				// v1.0.42.04: Now that SendKey() is lazy about releasing Ctrl and/or Shift (but not Win/Alt),
				// the section below also releases Ctrl/Shift if appropriate.  See SendKey() for more details.
				modsToSet = persistentModifiersForThisSendKeys; // Set default.

				if (inBlindMode) // This section is not needed for the array-sending modes because they exploit uninterruptibility to perform a more reliable restoration.
				{
					// For selective {Blind!#^+}, restore any modifiers that were automatically released at the
					// start, such as for *^1::Send "{Blind^}2" when Ctrl+Alt+1 is pressed (Ctrl is released).
					// But do this before the below so that if the key was physically down at the start and has
					// since been released, it won't be pushed back down.
					modsToSet |= modsReleasedForSelectiveBlind;
					// At the end of a blind-mode send, modifiers are restored differently than normal. One
					// reason for this is to support the explicit ability for a Send to turn off a hotkey's
					// modifiers even if the user is still physically holding them down.  For example:
					//   #space::Send {LWin up}  ; Fails to release it, by design and for backward compatibility.
					//   #space::Send {Blind}{LWin up}  ; Succeeds, allowing LWin to be logically up even though it's physically down.
					var modsChangedPhysicallyDuringSend = modsDownPhysicallyOrig ^ modsDownPhysically;
					// Fix for v1.0.42.04: To prevent keys from getting stuck down, compensate for any modifiers
					// the user physically pressed or released during the Send (especially those released).
					// Remove any modifiers physically released during the send so that they don't get pushed back down:
					modsToSet &= ~(modsChangedPhysicallyDuringSend & modsDownPhysicallyOrig); // Remove those that changed from down to up.
					// Conversely, add any modifiers newly, physically pressed down during the Send, because in
					// most cases the user would want such modifiers to be logically down after the Send.
					// Obsolete comment from v1.0.40: For maximum flexibility and minimum interference while
					// in blind mode, never restore modifiers to the down position then.
					modsToSet |= modsChangedPhysicallyDuringSend & modsDownPhysically; // Add those that changed from up to down.
				}
				else // Regardless of whether the keyboard hook is present, the following formula applies.
					modsToSet |= modsDownPhysically & ~modsDownPhysicallyButNotLogicallyOrig; // The second item is usually 0.

				// Above takes into account the fact that the user may have pressed and/or released some modifiers
				// during the Send.
				// So it includes all keys that are physically down except those that were down physically but not
				// logically at the *start* of the send operation (since the send operation may have changed the
				// logical state).  In other words, we want to restore the keys to their former logical-down
				// position to match the fact that the user is still holding them down physically.  The
				// previously-down keys we don't do this for are those that were physically but not logically down,
				// such as a naked Control key that's used as a suffix without being a prefix.  More details:
				// mods_down_physically_but_not_logically_orig is used to distinguish between the following two cases,
				// allowing modifiers to be properly restored to the down position when the hook is installed:
				// 1) A naked modifier key used only as suffix: when the user phys. presses it, it isn't
				//    logically down because the hook suppressed it.
				// 2) A modifier that is a prefix, that triggers a hotkey via a suffix, and that hotkey sends
				//    that modifier.  The modifier will go back up after the SEND, so the key will be physically
				//    down but not logically.
				// Use KEY_IGNORE_ALL_EXCEPT_MODIFIER to tell the hook to adjust g_modifiersLR_logical_non_ignored
				// because these keys being put back down match the physical pressing of those same keys by the
				// user, and we want such modifiers to be taken into account for the purpose of deciding whether
				// other hotkeys should fire (or the same one again if auto-repeating):
				// v1.0.42.04: A previous call to SendKey() might have left Shift/Ctrl in the down position
				// because by procrastinating, extraneous keystrokes in examples such as "Send ABCD" are
				// eliminated (previously, such that example released the shift key after sending each key,
				// only to have to press it down again for the next one.  For this reason, some modifiers
				// might get released here in addition to any that need to get pressed down.  That's why
				// SetModifierLRState() is called rather than the old method of pushing keys down only,
				// never releasing them.
				// Put the modifiers in mods_to_set into effect.  Although "true" is passed to disguise up-events,
				// there generally shouldn't be any up-events for Alt or Win because SendKey() would have already
				// released them.  One possible exception to this is when the user physically released Alt or Win
				// during the send (perhaps only during specific sensitive/vulnerable moments).
				// g_modifiersLR_numpad_mask is used to work around an issue where our changes to shift-key state
				// trigger the system's shift-numpad handling (usually in combination with actual user input),
				// which in turn causes the Shift key to stick down.  If non-zero, the Shift key is currently "up"
				// but should be "released" anyway, since the system will inject Shift-down either before the next
				// keyboard event or after the Numpad key is released.  Find "fake shift" for more details.
				SetModifierLRState(modsToSet, GetModifierLRState() | modifiersLRNumpadMask, targetWindow, true, true); // It also does DoKeyDelay(g->PressDuration).
			} // End of non-array Send.

			// For peace of mind and because that's how it was tested originally, the following is done
			// only after adjusting the modifier state above (since that adjustment might be able to
			// affect the global variables used below in a meaningful way).
			if (ht.HasKbdHook())
			{
				// Ensure that g_modifiersLR_logical_non_ignored does not contain any down-modifiers
				// that aren't down in g_modifiersLR_logical.  This is done mostly for peace-of-mind,
				// since there might be ways, via combinations of physical user input and the Send
				// commands own input (overlap and interference) for one to get out of sync with the
				// other.  The below uses ^ to find the differences between the two, then uses & to
				// find which are down in non_ignored that aren't in logical, then inverts those bits
				// in g_modifiersLR_logical_non_ignored, which sets those keys to be in the up position:
				modifiersLRLogicalNonIgnored &= ~((modifiersLRLogical ^ modifiersLRLogicalNonIgnored)
												  & modifiersLRLogicalNonIgnored);
			}

			if (priorCapslockState == ToggleValueType.AlwaysOn) // The current user setting requires us to turn it back on.
				_ = ToggleKeyState(VK_CAPITAL, ToggleValueType.AlwaysOn);

			// Might be better to do this after changing capslock state, since having the threads attached
			// tends to help with updating the global state of keys (perhaps only under Win9x in this case):
			if (threadsAreAttached)
				DetachTargetWindowThread(pd.MainThreadID, keybdLayoutThread);

			if (doSelectiveBlockInput && !blockinputPrev) // Turn it back off only if it was off before we started.
				_ = Core.Keyboard.ScriptBlockInput(false);

			//THIS IS PROBABLY NOT NEEDED, SINCE WE PROCESS HOTKEYS ON A DIFFERENT THREAD ANYWAY, SO THERE SHOULDN'T BE ANY NON-CRITICAL BUFFERING.//TODO
			// The following MsgSleep(-1) solves unwanted buffering of hotkey activations while SendKeys is in progress
			// in a non-Critical thread.  Because SLEEP_WITHOUT_INTERRUPTION is used to perform key delays, any incoming
			// hotkey messages would be left in the queue.  It is not until the next interruptible sleep that hotkey
			// messages may be processed, and potentially discarded due to #MaxThreadsPerHotkey (even #MaxThreadsBuffer
			// should only allow one buffered activation).  But if the hotkey thread just calls Send in a loop and then
			// returns, it never performs an interruptible sleep, so the hotkey messages are processed one by one after
			// each new hotkey thread returns, even though Critical was not used.  Also note SLEEP_WITHOUT_INTERRUPTION
			// causes g_script.mLastScriptRest to be reset, so it's unlikely that a sleep would occur between Send calls.
			// To solve this, call MsgSleep(-1) now (unless no delays were performed, or the thread is uninterruptible):
			if (sendModeOrig == SendModes.Event && script.lastPeekTime != origLastPeekTime && script.Threads.IsInterruptible())
				_ = Flow.Sleep(0); // MsgSleep(-1);//MsgSleep() is going to be extremely hard to implement, so just do regular sleep for now until we get real threads implemented.//TODO

#if WINDOWS
			// v1.0.43.03: Someone reported that when a non-autoreplace hotstring calls us to do its backspacing, the
			// hotstring's subroutine can execute a command that activates another window owned by the script before
			// the original window finished receiving its backspaces.  Although I can't reproduce it, this behavior
			// fits with expectations since our thread won't necessarily have a chance to process the incoming
			// keystrokes before executing the command that comes after SendInput.  If those command(s) activate
			// another of this thread's windows, that window will most likely intercept the keystrokes (assuming
			// that the message pump dispatches buffered keystrokes to whichever window is active at the time the
			// message is processed).
			// This fix does not apply to the SendPlay or SendEvent modes, the former due to the fact that it sleeps
			// a lot while the playback is running, and the latter due to key-delay and because testing has never shown
			// a need for it.
			if (sendModeOrig == SendModes.Input && WindowsAPI.GetWindowThreadProcessId(WindowsAPI.GetForegroundWindow(), out _) == pd.MainThreadID) // GetWindowThreadProcessId() tolerates a NULL hwnd.
				Flow.SleepWithoutInterruption(-1);
#endif

			// v1.0.43.08: Restore the original thread key-delay values in case above temporarily overrode them.
			tv.keyDelay = origKeyDelay;
			tv.keyDuration = origPressDuration;
		}

		internal abstract void PutMouseEventIntoArray(uint eventFlags, uint data, int x, int y);

		internal abstract void PutKeybdEventIntoArray(uint keyAsModifiersLR, uint vk, uint sc, uint eventFlags, long extraInfo);

		internal abstract int SiEventCount();

		internal abstract ToggleValueType ToggleKeyState(uint vk, ToggleValueType toggleValue);

		internal int TotalEventCount() => PbEventCount() + SiEventCount();

		protected internal abstract void LongOperationUpdate();

		protected internal abstract void LongOperationUpdateForSendKeys();

		//protected internal abstract void Send(string keys);

		//protected internal abstract void Send(Keys key);
		/// <summary>
		/// aSC or vk (but not both), can be zero to cause the default to be used.
		/// For keys like NumpadEnter -- that have a unique scancode but a non-unique virtual key --
		/// caller can just specify the sc.  In addition, the scan code should be specified for keys
		/// like NumpadPgUp and PgUp.  In that example, the caller would send the same scan code for
		/// both except that PgUp would be extended.   sc_to_vk() would map both of them to the same
		/// virtual key, which is fine since it's the scan code that matters to apps that can
		/// differentiate between keys with the same vk.
		///
		/// Thread-safe: This function is not fully thread-safe because keystrokes can get interleaved,
		/// but that's always a risk with anything short of SendInput.  In fact,
		/// when the hook ISN'T installed, keystrokes can occur in another thread, causing the key state to
		/// change in the middle of KeyEvent, which is the same effect as not having thread-safe key-states
		/// such as GetKeyboardState in here.  Also, the odds of both our threads being in here simultaneously
		/// is greatly reduced by the fact that the hook thread never passes "true" for aDoKeyDelay, thus
		/// its calls should always be very fast.  Even if a collision does occur, there are thread-safety
		/// things done in here that should reduce the impact to nearly as low as having a dedicated
		/// KeyEvent function solely for calling by the hook thread (which might have other problems of its own).
		/// </summary>
		/// <param name="eventType"></param>
		/// <param name="vk"></param>
		/// <param name="sc"></param>
		/// <param name="targetWindow"></param>
		/// <param name="doKeyDelay"></param>
		/// <param name="extraInfo"></param>
		///
		internal virtual void SendKeyEvent(KeyEventTypes eventType, uint vk, uint sc = 0u, nint targetWindow = default, bool doKeyDelay = false, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			if ((vk | sc) == 0)//If neither VK nor SC was specified, return.
				return;

			if (extraInfo == 0) // Shouldn't be called this way because 0 is considered false in some places below (search on " = aExtraInfo" to find them).
				extraInfo = KeyIgnoreAllExceptModifier; // Seems slightly better to use a standard default rather than something arbitrary like 1.

			// Since calls from the hook thread could come in even while the SendInput array is being constructed,
			// don't let those events get interspersed with the script's explicit use of SendInput.
			var ht = Script.TheScript.HookThread;
			//Note that the threading model in Keysharp is different than AHK, so this doesn't apply.
			//In AHK, the low level keyboard proc runs in its own thread, however in Keysharp it turns on the main window thread.
			//Further, after hours of extreme scrutiny in AHK, there seems to be no code in HookThreadProc() where a keyboard event could be sent here.
			//So just hard code callerIsKeybdHook to false.
			var callerIsKeybdHook = false;// WindowsAPI.GetCurrentThreadId() == Keysharp.Core.Processes.MainThreadID;//Hook runs on the main window thread.
			var putEventIntoArray = sendMode != SendModes.Event && !callerIsKeybdHook;

			if (sendMode == SendModes.Input || callerIsKeybdHook) // First check is necessary but second is just for maintainability.
				doKeyDelay = false;

			// Even if the sc_to_vk() mapping results in a zero-value vk, don't return.
			// I think it may be valid to send keybd_events that have a zero vk.
			// In any case, it's unlikely to hurt anything:
			if (vk == 0)
				vk = ht.MapScToVk(sc);
			else if (sc == 0)
				// In spite of what the MSDN docs say, the scan code parameter *is* used, as evidenced by
				// the fact that the hook receives the proper scan code as sent by the below, rather than
				// zero like it normally would.  Even though the hook would try to use MapVirtualKey() to
				// convert zero-value scan codes, it's much better to send it here also for full compatibility
				// with any apps that may rely on scan code (and such would be the case if the hook isn't
				// active because the user doesn't need it; also for some games maybe).
				sc = ht.MapVkToSc(vk);

			var eventFlags = ((sc >> 8) & 0xFF) != 0 ? KEYEVENTF_EXTENDEDKEY : 0u;

			// v1.0.43: Apparently, the journal playback hook requires neutral modifier keystrokes
			// rather than left/right ones.  Otherwise, the Shift key can't capitalize letters, etc.
			if (sendMode == SendModes.Play)
			{
				switch (vk)
				{
					case VK_LCONTROL:
					case VK_RCONTROL: vk = VK_CONTROL; break; // But leave scan code set to a left/right specific value rather than converting it to "left" unconditionally.

					case VK_LSHIFT:
					case VK_RSHIFT: vk = VK_SHIFT; break;

					case VK_LMENU:
					case VK_RMENU: vk = VK_MENU; break;
				}
			}

			// aTargetWindow is almost always passed in as NULL by our caller, even if the overall command
			// being executed is ControlSend.  This is because of the following reasons:
			// 1) Modifiers need to be logically changed via keybd_event() when using ControlSend against
			//    a cmd-prompt, console, or possibly other types of windows.
			// 2) If a hotkey triggered the ControlSend that got us here and that hotkey is a naked modifier
			//    such as RAlt:: or modified modifier such as ^#LShift, that modifier would otherwise auto-repeat
			//    an possibly interfere with the send operation.  This auto-repeat occurs because unlike a normal
			//    send, there are no calls to keybd_event() (keybd_event() stop the auto-repeat as a side-effect).
			// One exception to this is something like "ControlSend, Edit1, {Control down}", which explicitly
			// calls us with a target window.  This exception is by design and has been bug-fixed and documented
			// in ControlSend for v1.0.21:
			if (targetWindow != 0) // This block shouldn't affect overall thread-safety because hook thread never calls it in this mode.
			{
				SendKeyEventToTargetWindow(eventType, vk, sc, targetWindow, doKeyDelay, extraInfo);
			}
			else // Keystrokes are to be sent with keybd_event() or the event array rather than PostMessage().
			{
				// The following static variables are intentionally NOT thread-safe because their whole purpose
				// is to watch the combined stream of keystrokes from all our   Due to our threads'
				// keystrokes getting interleaved with the user's and those of other threads, this kind of
				// monitoring is never 100% reliable.  All we can do is aim for an astronomically low chance
				// of failure.
				// Users of the below want them updated only for keybd_event() keystrokes (not PostMessage ones):
				prevEventType = eventType;
				prevVK = vk;
				var tempTargetLayoutHasAltGr = (callerIsKeybdHook ? LayoutHasAltGr(GetFocusedKeybdLayout(0)) : targetLayoutHasAltGr) == ResultType.ConditionTrue; // i.e. not CONDITION_FALSE (which is nonzero) or FAIL (zero).
				var hookableAltGr = (vk == VK_RMENU) && tempTargetLayoutHasAltGr && !putEventIntoArray && ht.HasKbdHook();// hookId != 0;
				// Calculated only once for performance (and avoided entirely if not needed):
				bool? b = null;
				var keyAsModifiersLR = putEventIntoArray ? ht.KeyToModifiersLR(vk, sc, ref b) : 0;
				var doKeyHistory = !callerIsKeybdHook // If caller is hook, don't log because it does.
								   && sendMode != SendModes.Play// In playback mode, the journal hook logs so that timestamps are accurate.
								   && (!ht.HasKbdHook() || sendMode == SendModes.Input); // In the remaining cases, log only when the hook isn't installed or won't be at the time of the event.

				if (eventType != KeyEventTypes.KeyUp)  // i.e. always do it for KEYDOWNANDUP
				{
					if (putEventIntoArray)
						PutKeybdEventIntoArray(keyAsModifiersLR, vk, sc, eventFlags, extraInfo);
					else
					{
						// The following global is used to flag as our own the keyboard driver's LCtrl-down keystroke
						// that is triggered by RAlt-down (AltGr).  The hook prevents those keystrokes from triggering
						// hotkeys such as "*Control::" anyway, but this ensures the LCtrl-down is marked as "ignored"
						// and given the correct SendLevel.  It may fix other obscure side-effects and bugs, since the
						// event should be considered script-generated even though indirect.  Note: The problem with
						// having the hook detect AltGr's automatic LControl-down is that the keyboard driver seems
						// to generate the LControl-down *before* notifying the system of the RAlt-down.  That makes
						// it impossible for the hook to automatically adjust its SendLevel based on the RAlt-down.
						if (hookableAltGr)
							altGrExtraInfo = extraInfo;

						SendKeybdEvent(KeyEventTypes.KeyDown, vk, sc, eventFlags, (uint)extraInfo);// naked scan code (the 0xE0 prefix, if any, is omitted)
						altGrExtraInfo = 0; // Unconditional reset.
					}

					if (doKeyHistory && ht.keyHistory is KeyHistory kh)
						kh.UpdateKeyEventHistory(false, vk, sc); // Should be thread-safe since if no hook means only one thread ever sends keystrokes (with possible exception of mouse hook, but that seems too rare).
				}

				// The press-duration delay is done only when this is a down-and-up because otherwise,
				// the normal g->KeyDelay will be in effect.  In other words, it seems undesirable in
				// most cases to do both delays for only "one half" of a keystroke:
				if (doKeyDelay && eventType == KeyEventTypes.KeyDownAndUp) // Hook should never specify a delay, so no need to check if caller is hook.
					DoKeyDelay(sendMode == SendModes.Play ? ThreadAccessors.A_KeyDurationPlay : ThreadAccessors.A_KeyDuration); // DoKeyDelay() is not thread safe but since the hook thread should never pass true for aKeyDelay, it shouldn't be an issue.

				if (eventType != KeyEventTypes.KeyDown)  // i.e. always do it for KEYDOWNANDUP
				{
					eventFlags |= KEYEVENTF_KEYUP;

					if (putEventIntoArray)
						PutKeybdEventIntoArray(keyAsModifiersLR, vk, sc, eventFlags, extraInfo);
					else
					{
						if (hookableAltGr) // See comments in similar section above for details.
							altGrExtraInfo = extraInfo;

						SendKeybdEvent(KeyEventTypes.KeyUp, vk, sc, eventFlags, (uint)extraInfo);
						altGrExtraInfo = 0; // Unconditional reset.
					}

					// The following is done to avoid an extraneous artificial {LCtrl Up} later on,
					// since the keyboard driver should insert one in response to this {RAlt Up}:
					if (targetLayoutHasAltGr == ResultType.ConditionTrue && sc == ScanCodes.RAlt)
						eventModifiersLR &= ~MOD_LCONTROL;

					if (doKeyHistory && ht.keyHistory is KeyHistory kh)
						kh.UpdateKeyEventHistory(true, vk, sc);
				}
			}

			if (doKeyDelay) // SM_PLAY also uses DoKeyDelay(): it stores the delay item in the event array.
				DoKeyDelay(); // Thread-safe because only called by main thread in this mode.  See notes above.
		}

		internal virtual void SendKeyEventToTargetWindow(KeyEventTypes eventType, uint vk, uint sc = 0u, nint targetWindow = default, bool doKeyDelay = false, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			throw new NotImplementedException();
		}

		internal abstract void SendKeybdEvent(KeyEventTypes eventType, uint vk, uint sc, uint eventFlags, long extraInfo);

		internal abstract void SendUnicodeChar(char ch, uint modifiers);

		/// <summary>
		/// Caller must be aware that keystrokes are sent directly (i.e. never to a target window via ControlSend mode).
		/// ascii is a string to support explicit leading zeros because sending 216, for example, is not the same as
		/// sending 0216.  The caller is also responsible for restoring any desired modifier keys to the down position
		/// (this function needs to release some of them if they're down).
		/// </summary>
		/// <param name="ascii"></param>
		internal virtual void SendASC(byte[] ascii)
		{
			// UPDATE: In v1.0.42.04, the left Alt key is always used below because:
			// 1) It might be required on Win95/NT (though testing shows that RALT works okay on Windows 98se).
			// 2) It improves maintainability because if the keyboard layout has AltGr, and the Control portion
			//    of AltGr is released without releasing the RAlt portion, anything that expects LControl to
			//    be down whenever RControl is down would be broken.
			// The following test demonstrates that on previous versions under German layout, the right-Alt key
			// portion of AltGr could be used to manifest Alt+Numpad combinations:
			//   Send {RAlt down}{Asc 67}{RAlt up}  ; Should create a C character even when both the active window an AHK are set to German layout.
			//   KeyHistory  ; Shows that the right-Alt key was successfully used rather than the left.
			// Changing the modifier state via SetModifierLRState() (rather than some more error-prone multi-step method)
			// also ensures that the ALT key is pressed down only after releasing any shift key that needed it above.
			// Otherwise, the OS's switch-keyboard-layout hotkey would be triggered accidentally; e.g. the following
			// in English layout: Send ~~��{^}.
			//
			// Make sure modifier state is correct: ALT pressed down and other modifiers UP
			// because CTRL and SHIFT seem to interfere with this technique if they are down,
			// at least under WinXP (though the Windows key doesn't seem to be a problem).
			// Specify KEY_IGNORE so that this action does not affect the modifiers that the
			// hook uses to determine which hotkey should be triggered for a suffix key that
			// has more than one set of triggering modifiers (for when the user is holding down
			// that suffix to auto-repeat it -- see keyboard_mouse.h for details).
			var modifiersLRNow = sendMode != SendModes.Event ? eventModifiersLR : GetModifierLRState();
			SetModifierLRState((modifiersLRNow | MOD_LALT) & ~(MOD_RALT | MOD_LCONTROL | MOD_RCONTROL | MOD_LSHIFT | MOD_RSHIFT)
							   , modifiersLRNow, 0, false // Pass false because there's no need to disguise the down-event of LALT.
							   , true, KeyIgnore); // Pass true so that any release of RALT is disguised (Win is never released here).
			// Note: It seems best never to press back down any key released above because the
			// act of doing so may do more harm than good (i.e. the keystrokes may caused
			// unexpected side-effects.

			// Known limitation (but obscure): There appears to be some OS limitation that prevents the following
			// AltGr hotkey from working more than once in a row:
			// <^>!i::Send {ASC 97}
			// Key history indicates it's doing what it should, but it doesn't actually work.  You have to press the
			// left-Alt key (not RAlt) once to get the hotkey working again.

			// This is not correct because it is possible to generate unicode characters by typing
			// Alt+256 and beyond:
			// int value = ATOI(aAscii);
			// if (value < 0 || value > 255) return 0; // Sanity check.

			// Known issue: If the hotkey that triggers this Send command is CONTROL-ALT
			// (and maybe either CTRL or ALT separately, as well), the {ASC nnnn} method
			// might not work reliably due to strangeness with that OS feature, at least on
			// WinXP.  I already tried adding delays between the keystrokes and it didn't help.

			// Caller relies upon us to stop upon reaching the first non-digit character:
			for (var index = 0; ascii[index] >= 48 && ascii[index] <= 57; ++index)//48 and 57 are 0 and 9.
			{
				// A comment from AutoIt3: ASCII 0 is 48, NUMPAD0 is 96, add on 48 to the ASCII.
				// Also, don't do WinDelay after each keypress in this case because it would make
				// such keys take up to 3 or 4 times as long to send (AutoIt3 avoids doing the
				// delay also).  Note that strings longer than 4 digits are allowed because
				// some or all OSes support Unicode characters 0 through 65535.
				SendKeyEvent(KeyEventTypes.KeyDownAndUp, (uint)(ascii[index] + 48));
			}

			// Must release the key regardless of whether it was already down, so that the sequence will take effect
			// immediately.  Otherwise, our caller might not release the Alt key (since it might need to stay down for
			// other purposes), in which case Alt+Numpad character would never appear and the caller's subsequent
			// keystrokes might get absorbed by the OS's special state of "waiting for Alt+Numpad sequence to complete".
			// Another reason is that the user may be physically holding down Alt, in which case the caller might never
			// release it.  In that case, we want the Alt+Numpad character to appear immediately rather than waiting for
			// the user to release Alt (in the meantime, the caller will likely press Alt back down to match the physical
			// state).
			SendKeyEvent(KeyEventTypes.KeyUp, VK_MENU);
		}

		/// <summary>
		/// Caller must be aware that keystrokes are sent directly (i.e. never to a target window via ControlSend mode).
		/// It must also be aware that the event type KEYDOWNANDUP is always what's used since there's no way
		/// to support anything else.  Furthermore, there's no way to support "modifiersLR_for_next_key" such as ^�
		/// (assuming � is a character for which SendKeySpecial() is required in the current layout) with ASC mode.
		/// This function uses some of the same code as SendKey() above, so maintain them together.
		/// </summary>
		/// <param name="ch"></param>
		/// <param name="repeatCount"></param>
		/// <param name="modifiersLR"></param>
		internal void SendKeySpecial(char ch, long repeatCount, uint modifiersLR)
		{
			// Caller must verify that aRepeatCount >= 1.
			// Avoid changing modifier states and other things if there is nothing to be sent.
			// Otherwise, menu bar might activated due to ALT keystrokes that don't modify any key,
			// the Start Menu might appear due to WIN keystrokes that don't modify anything, etc:
			//if (aRepeatCount < 1)
			//  return;
			// v1.0.40: This function was heavily simplified because the old method of simulating
			// characters via dead keys apparently never executed under any keyboard layout.  It never
			// got past the following on the layouts I tested (Russian, German, Danish, Spanish):
			//      if (!send1 && !send2) // Can't simulate aChar.
			//          return;
			// This might be partially explained by the fact that the following old code always exceeded
			// the bounds of the array (because aChar was always between 0 and 127), so it was never valid
			// in the first place:
			//      asc_int = cAnsiToAscii[(int)((aChar - 128) & 0xff)] & 0xff;
			// Producing ANSI characters via Alt+Numpad and a leading zero appears standard on most languages
			// and layouts (at least those whose active code page is 1252/Latin 1 US/Western Europe).  However,
			// Russian (code page 1251 Cyrillic) is apparently one exception as shown by the fact that sending
			// all of the characters above Chr(127) while under Russian layout produces Cyrillic characters
			// if the active window's focused control is an Edit control (even if its an ANSI app).
			// I don't know the difference between how such characters are actually displayed as opposed to how
			// they're stored in memory (in notepad at least, there appears to be some kind of on-the-fly
			// translation to Unicode as shown when you try to save such a file).  But for now it doesn't matter
			// because for backward compatibility, it seems best not to change it until some alternative is
			// discovered that's high enough in value to justify breaking existing scripts that run under Russian
			// and other non-code-page-1252 layouts.
			//
			// Production of ANSI characters above 127 has been tested on both Windows XP and 98se (but not the
			// Win98 command prompt).
			var useSendasc = sendMode == SendModes.Play; // See SendUnicodeChar for why it isn't called for SM_PLAY.
			var ascString = new byte[16];
			var wc = '0';

			if (useSendasc)
			{
				// The following range isn't checked because this function appears never to be called for such
				// characters (tested in English and Russian so far), probably because VkKeyScan() finds a way to
				// manifest them via Control+VK combinations:
				//if (aChar > -1 && aChar < 32)
				//  return;
				var index = 0;

				if ((ch & ~127) != 0)    // Try using ANSI.
					ascString[index++] = (byte)'0';  // ANSI mode is achieved via leading zero in the Alt+Numpad keystrokes.

				//else use Alt+Numpad without the leading zero, which allows the characters a-z, A-Z, and quite
				// a few others to be produced in Russian and perhaps other layouts, which was impossible in versions
				// prior to 1.0.40.
				var str = ((int)ch).ToString();
				var bytes = Encoding.ASCII.GetBytes(str);
				System.Array.Copy(bytes, 0, ascString, index, Math.Min(ascString.Length - index, bytes.Length));
			}
			else
			{
				wc = ch;
			}

			for (var i = 0L; i < repeatCount; ++i)
			{
				if (sendMode == SendModes.Event)
					LongOperationUpdateForSendKeys();

				if (useSendasc)
					SendASC(ascString);
				else
					SendUnicodeChar(wc, modifiersLR);

				DoKeyDelay(); // It knows not to do the delay for SM_INPUT.
			}

			// It is not necessary to do SetModifierLRState() to put a caller-specified set of persistent modifier
			// keys back into effect because:
			// 1) Our call to SendASC above (if any) at most would have released some of the modifiers (though never
			//    WIN because it isn't necessary); but never pushed any new modifiers down (it even releases ALT
			//    prior to returning).
			// 2) Our callers, if they need to push ALT back down because we didn't do it, will either disguise it
			//    or avoid doing so because they're about to send a keystroke (just about anything) that ALT will
			//    modify and thus not need to be disguised.
		}

		/// <summary>
		/// aX1 and aY1 are the starting coordinates, and "2" are the destination coordinates.
		/// Caller has ensured that aSpeed is in the range 0 to 100, inclusive.
		/// </summary>
		/// <param name="x1"></param>
		/// <param name="y1"></param>
		/// <param name="x2"></param>
		/// <param name="y2"></param>
		/// <param name="speed"></param>
		internal void DoIncrementalMouseMove(long x1, long y1, long x2, long y2, long speed)
		{
			// AutoIt3: So, it's a more gradual speed that is needed :)
			long delta;
			const int incrMouseMinSpeed = 32;

			while (x1 != x2 || y1 != y2)
			{
				if (x1 < x2)
				{
					delta = (x2 - x1) / speed;

					if (delta == 0 || delta < incrMouseMinSpeed)
						delta = incrMouseMinSpeed;

					if ((x1 + delta) > x2)
						x1 = x2;
					else
						x1 += delta;
				}
				else if (x1 > x2)
				{
					delta = (x1 - x2) / speed;

					if (delta == 0 || delta < incrMouseMinSpeed)
						delta = incrMouseMinSpeed;

					if ((x1 - delta) < x2)
						x1 = x2;
					else
						x1 -= delta;
				}

				if (y1 < y2)
				{
					delta = (y2 - y1) / speed;

					if (delta == 0 || delta < incrMouseMinSpeed)
						delta = incrMouseMinSpeed;

					if ((y1 + delta) > y2)
						y1 = y2;
					else
						y1 += delta;
				}
				else if (y1 > y2)
				{
					delta = (y1 - y2) / speed;

					if (delta == 0 || delta < incrMouseMinSpeed)
						delta = incrMouseMinSpeed;

					if ((y1 - delta) < y2)
						y1 = y2;
					else
						y1 -= delta;
				}

				MouseEvent((uint)MOUSEEVENTF.MOVE | (uint)MOUSEEVENTF.ABSOLUTE, 0, (int)x1, (int)y1);
				DoMouseDelay();
				// Above: A delay is required for backward compatibility and because it's just how the incremental-move
				// feature was originally designed in AutoIt v3.  It may in fact improve reliability in some cases,
				// especially with the mouse_event() method vs. SendInput/Play.
			} // while()
		}

		protected abstract void RegisterHook();

		internal abstract int MouseCoordToAbs(int coord, int width_or_height);

		/// <summary>
		/// This function is designed to be called from only the main thread; it's probably not thread-safe.
		/// Puts modifiers into the specified state, releasing or pressing down keys as needed.
		/// The modifiers are released and pressed down in a very delicate order due to their interactions with
		/// each other and their ability to show the Start Menu, activate the menu bar, or trigger the OS's language
		/// bar hotkeys.  Side-effects like these would occur if a more simple approach were used, such as releasing
		/// all modifiers that are going up prior to pushing down the ones that are going down.
		/// When the target layout has an altgr key, it is tempting to try to simplify things by removing MOD_LCONTROL
		/// from modifiersLRnew whenever modifiersLRnew contains MOD_RALT.  However, this a careful review how that
		/// would impact various places below where sTargetLayoutHasAltGr is checked indicates that it wouldn't help.
		/// Note that by design and as documented for ControlSend, aTargetWindow is not used as the target for the
		/// various calls to KeyEvent() here.  It is only used as a workaround for the GUI window issue described
		/// at the bottom.
		/// </summary>
		/// <param name="modifiersLRnew"></param>
		/// <param name="modifiersLRnow"></param>
		/// <param name="targetWindow"></param>
		/// <param name="disguiseDownWinAlt"></param>
		/// <param name="disguiseUpWinAlt"></param>
		/// <param name="extraInfo"></param>
		internal virtual void SetModifierLRState(uint modifiersLRnew, uint modifiersLRnow, nint targetWindow
										 , bool disguiseDownWinAlt, bool disguiseUpWinAlt, long extraInfo = KeyIgnoreAllExceptModifier)
		{
			if (modifiersLRnow == modifiersLRnew) // They're already in the right state, so avoid doing all the checks.
				return; // Especially avoids the aTargetWindow check at the bottom.

			// Notes about modifier key behavior on Windows XP (these probably apply to NT/2k also, and has also
			// been tested to be true on Win98): The WIN and ALT keys are the problem keys, because if either is
			// released without having modified something (even another modifier), the WIN key will cause the
			// Start Menu to appear, and the ALT key will activate the menu bar of the active window (if it has one).
			// For example, a hook hotkey such as "$#c::Send text" (text must start with a lowercase letter
			// to reproduce the issue, because otherwise WIN would be auto-disguised as a side effect of the SHIFT
			// keystroke) would cause the Start Menu to appear if the disguise method below weren't used.
			//
			// Here are more comments formerly in SetModifierLRStateSpecific(), which has since been eliminated
			// because this function is sufficient:
			// To prevent it from activating the menu bar, the release of the ALT key should be disguised
			// unless a CTRL key is currently down.  This is because CTRL always seems to avoid the
			// activation of the menu bar (unlike SHIFT, which sometimes allows the menu to be activated,
			// though this is hard to reproduce on XP).  Another reason not to use SHIFT is that the OS
			// uses LAlt+Shift as a hotkey to switch languages.  Such a hotkey would be triggered if SHIFT
			// were pressed down to disguise the release of LALT.
			//
			// Alt-down events are also disguised whenever they won't be accompanied by a Ctrl-down.
			// This is necessary whenever our caller does not plan to disguise the key itself.  For example,
			// if "!a::Send Test" is a registered hotkey, two things must be done to avoid complications:
			// 1) Prior to sending the word test, ALT must be released in a way that does not activate the
			//    menu bar.  This is done by sandwiching it between a CTRL-down and a CTRL-up.
			// 2) After the send is complete, SendKeys() will restore the ALT key to the down position if
			//    the user is still physically holding ALT down (this is done to make the logical state of
			//    the key match its physical state, which allows the same hotkey to be fired twice in a row
			//    without the user having to release and press down the ALT key physically).
			// The #2 case above is the one handled below by ctrl_wont_be_down.  It is especially necessary
			// when the user releases the ALT key prior to releasing the hotkey suffix, which would otherwise
			// cause the menu bar (if any) of the active window to be activated.
			//
			// Some of the same comments above for ALT key apply to the WIN key.  More about this issue:
			// Although the disguise of the down-event is usually not needed, it is needed in the rare case
			// where the user releases the WIN or ALT key prior to releasing the hotkey's suffix.
			// Although the hook could be told to disguise the physical release of ALT or WIN in these
			// cases, it's best not to rely on the hook since it is not always installed.
			//
			// Registered WIN and ALT hotkeys that don't use the Send command work okay except ALT hotkeys,
			// which if the user releases ALT prior the hotkey's suffix key, cause the menu bar to be activated.
			// Since it is unusual for users to do this and because it is standard behavior for  ALT hotkeys
			// registered in the OS, fixing it via the hook seems like a low priority, and perhaps isn't worth
			// the added code complexity/size.  But if there is ever a need to do so, the following note applies:
			// If the hook is installed, could tell it to disguise any need-to-be-disguised Alt-up that occurs
			// after receipt of the registered ALT hotkey.  But what if that hotkey uses the send command:
			// there might be interference?  Doesn't seem so, because the hook only disguises non-ignored events.
			// Set up some conditions so that the keystrokes that disguise the release of Win or Alt
			// are only sent when necessary (which helps avoid complications caused by keystroke interaction,
			// while improving performance):
			var script = Script.TheScript;
			var modifiersLRunion = modifiersLRnow | modifiersLRnew; // The set of keys that were or will be down.
			var ctrlNotDown = (modifiersLRnow & (MOD_LCONTROL | MOD_RCONTROL)) == 0; // Neither CTRL key is down now.
			var ctrlWillNotBeDown = (modifiersLRnew & (MOD_LCONTROL | MOD_RCONTROL)) == 0 // Nor will it be.
									&& !(targetLayoutHasAltGr == ResultType.ConditionTrue && ((modifiersLRnew & MOD_RALT) != 0)); // Nor will it be pushed down indirectly due to AltGr.
			var ctrlNorShiftNorAltDown = ctrlNotDown                             // Neither CTRL key is down now.
										 && (modifiersLRnow & (MOD_LSHIFT | MOD_RSHIFT | MOD_LALT | MOD_RALT)) == 0; // Nor is any SHIFT/ALT key.
			var ctrlOrShiftOrAltWillBeDown = !ctrlWillNotBeDown             // CTRL will be down.
											 || (modifiersLRnew & (MOD_LSHIFT | MOD_RSHIFT | MOD_LALT | MOD_RALT)) != 0; // or SHIFT or ALT will be.
			// If the required disguise keys aren't down now but will be, defer the release of Win and/or Alt
			// until after the disguise keys are in place (since in that case, the caller wanted them down
			// as part of the normal operation here):
			var deferWinRelease = ctrlNorShiftNorAltDown && ctrlOrShiftOrAltWillBeDown;
			var deferAltRelease = ctrlNotDown && !ctrlWillNotBeDown;  // i.e. Ctrl not down but it will be.
			var releaseShiftBeforeAltCtrl = deferAltRelease // i.e. Control is moving into the down position or...
											|| (((modifiersLRnow & (MOD_LALT | MOD_RALT)) == 0) && ((modifiersLRnew & (MOD_LALT | MOD_RALT)) != 0)); // ...Alt is moving into the down position.
			// Concerning "release_shift_before_alt_ctrl" above: Its purpose is to prevent unwanted firing of the OS's
			// language bar hotkey.  See the bottom of this function for more explanation.
			// ALT:
			var disguiseAltDown = disguiseDownWinAlt && ctrlNotDown && ctrlWillNotBeDown; // Since this applies to both Left and Right Alt, don't take sTargetLayoutHasAltGr into account here. That is done later below.
			// WIN: The WIN key is successfully disguised under a greater number of conditions than ALT.
			// Since SendPlay can't display Start Menu, there's no need to send the disguise-keystrokes (such
			// keystrokes might cause unwanted effects in certain games):
			var disguiseWinDown = disguiseDownWinAlt && sendMode != SendModes.Play
								  && ctrlNotDown && ctrlWillNotBeDown
								  && (modifiersLRunion & (MOD_LSHIFT | MOD_RSHIFT)) == 0 // And neither SHIFT key is down, nor will it be.
								  && (modifiersLRunion & (MOD_LALT | MOD_RALT)) == 0;    // And neither ALT key is down, nor will it be.
			var releaseLwin = (modifiersLRnow & MOD_LWIN) != 0 && (modifiersLRnew & MOD_LWIN) == 0;
			var releaseRwin = (modifiersLRnow & MOD_RWIN) != 0 && (modifiersLRnew & MOD_RWIN) == 0;
			var releaseLalt = (modifiersLRnow & MOD_LALT) != 0 && (modifiersLRnew & MOD_LALT) == 0;
			var releaseRalt = (modifiersLRnow & MOD_RALT) != 0 && (modifiersLRnew & MOD_RALT) == 0;
			var releaseLshift = (modifiersLRnow & MOD_LSHIFT) != 0 && (modifiersLRnew & MOD_LSHIFT) == 0;
			var releaseRshift = (modifiersLRnow & MOD_RSHIFT) != 0 && (modifiersLRnew & MOD_RSHIFT) == 0;

			// Handle ALT and WIN prior to the other modifiers because the "disguise" methods below are
			// only needed upon release of ALT or WIN.  This is because such releases tend to have a better
			// chance of being "disguised" if SHIFT or CTRL is down at the time of the release.  Thus, the
			// release of SHIFT or CTRL (if called for) is deferred until afterward.
			// ** WIN
			// Must be done before ALT in case it is relying on ALT being down to disguise the release WIN.
			// If ALT is going to be pushed down further below, defer_win_release should be true, which will make sure
			// the WIN key isn't released until after the ALT key is pushed down here at the top.
			// Also, WIN is a little more troublesome than ALT, so it is done first in case the ALT key
			// is down but will be going up, since the ALT key being down might help the WIN key.
			// For example, if you hold down CTRL, then hold down LWIN long enough for it to auto-repeat,
			// then release CTRL before releasing LWIN, the Start Menu would appear, at least on XP.
			// But it does not appear if CTRL is released after LWIN.
			// Also note that the ALT key can disguise the WIN key, but not vice versa.
			if (releaseLwin)
			{
				if (!deferWinRelease)
				{
					// Fixed for v1.0.25: To avoid triggering the system's LAlt+Shift language hotkey, the
					// Control key is now used to suppress LWIN/RWIN (preventing the Start Menu from appearing)
					// rather than the Shift key.  This is definitely needed for ALT, but is done here for
					// WIN also in case ALT is down, which might cause the use of SHIFT as the disguise key
					// to trigger the language switch.
					if (ctrlNorShiftNorAltDown && disguiseUpWinAlt // Nor will they be pushed down later below, otherwise defer_win_release would have been true and we couldn't get to this point.
							&& sendMode != SendModes.Play) // SendPlay can't display Start Menu, so disguise not needed (also, disguise might mess up some games).
						SendKeyEventMenuMask(KeyEventTypes.KeyDownAndUp, extraInfo); // Disguise key release to suppress Start Menu.

					// The above event is safe because if we're here, it means VK_CONTROL will not be
					// pressed down further below.  In other words, we're not defeating the job
					// of this function by sending these disguise keystrokes.
					SendKeyEvent(KeyEventTypes.KeyUp, VK_LWIN, 0, 0, false, extraInfo);
				}

				// else release it only after the normal operation of the function pushes down the disguise keys.
			}
			else if ((modifiersLRnow & MOD_LWIN) == 0 && (modifiersLRnew & MOD_LWIN) != 0) // Press down LWin.
			{
				if (disguiseWinDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyDown, extraInfo); // Ensures that the Start Menu does not appear.

				SendKeyEvent(KeyEventTypes.KeyDown, VK_LWIN, 0, 0, false, extraInfo);

				if (disguiseWinDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyUp, extraInfo); // Ensures that the Start Menu does not appear.
			}

			if (releaseRwin)
			{
				if (!deferWinRelease)
				{
					if (ctrlNorShiftNorAltDown && disguiseUpWinAlt && sendMode != SendModes.Play)
						SendKeyEventMenuMask(KeyEventTypes.KeyDownAndUp, extraInfo); // Disguise key release to suppress Start Menu.

					SendKeyEvent(KeyEventTypes.KeyUp, VK_RWIN, 0, 0, false, extraInfo);
				}

				// else release it only after the normal operation of the function pushes down the disguise keys.
			}
			else if ((modifiersLRnow & MOD_RWIN) == 0 && (modifiersLRnew & MOD_RWIN) != 0) // Press down RWin.
			{
				if (disguiseWinDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyDown, extraInfo); // Ensures that the Start Menu does not appear.

				SendKeyEvent(KeyEventTypes.KeyDown, VK_RWIN, 0, 0, false, extraInfo);

				if (disguiseWinDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyUp, extraInfo); // Ensures that the Start Menu does not appear.
			}

			// ** SHIFT (PART 1 OF 2)
			if (releaseShiftBeforeAltCtrl)
			{
				if (releaseLshift)
					SendKeyEvent(KeyEventTypes.KeyUp, VK_LSHIFT, 0, 0, false, extraInfo);

				if (releaseRshift)
					SendKeyEvent(KeyEventTypes.KeyUp, VK_RSHIFT, 0, 0, false, extraInfo);
			}

			// ** ALT
			if (releaseLalt)
			{
				if (!deferAltRelease)
				{
					if (ctrlNotDown && disguiseUpWinAlt)
						SendKeyEventMenuMask(KeyEventTypes.KeyDownAndUp, extraInfo); // Disguise key release to suppress menu activation.

					SendKeyEvent(KeyEventTypes.KeyUp, VK_LMENU, 0, 0, false, extraInfo);
				}
			}
			else if ((modifiersLRnow & MOD_LALT) == 0 && (modifiersLRnew & MOD_LALT) != 0)
			{
				if (disguiseAltDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyDown, extraInfo); // Ensures that menu bar is not activated.

				SendKeyEvent(KeyEventTypes.KeyDown, VK_LMENU, 0, 0, false, extraInfo);

				if (disguiseAltDown)
					SendKeyEventMenuMask(KeyEventTypes.KeyUp, extraInfo);
			}

			if (releaseRalt)
			{
				if (!deferAltRelease || targetLayoutHasAltGr == ResultType.ConditionTrue) // No need to defer if RAlt==AltGr. But don't change the value of defer_alt_release because LAlt uses it too.
				{
					if (targetLayoutHasAltGr == ResultType.ConditionTrue)
					{
						// Indicate that control is up now, since the release of AltGr will cause that indirectly.
						// Fix for v1.0.43: Unlike the pressing down of AltGr in a later section, which callers want
						// to automatically press down LControl too (by the very nature of AltGr), callers do not want
						// the release of AltGr to release LControl unless they specifically asked for LControl to be
						// released too.  This is because the caller may need LControl down to manifest something
						// like ^c. So don't do: modifiersLRnew &= ~MOD_LCONTROL.
						// Without this fix, a hotkey like <^>!m::Send ^c would send "c" vs. "^c" on the German layout.
						// See similar section below for more details.
						modifiersLRnow &= ~MOD_LCONTROL; ; // To reflect what KeyEvent(KEYUP, VK_RMENU) below will do.
					}
					else // No AltGr, so check if disguise is necessary (AltGr itself never needs disguise).
						if (ctrlNotDown && disguiseUpWinAlt)
							SendKeyEventMenuMask(KeyEventTypes.KeyDownAndUp, extraInfo); // Disguise key release to suppress menu activation.

					SendKeyEvent(KeyEventTypes.KeyUp, VK_RMENU, 0, 0, false, extraInfo);
				}
			}
			else if ((modifiersLRnow & MOD_RALT) == 0 && (modifiersLRnew & MOD_RALT) != 0) // Press down RALT.
			{
				// For the below: There should never be a need to disguise AltGr.  Doing so would likely cause unwanted
				// side-effects. Also, disguise_alt_key does not take sTargetLayoutHasAltGr into account because
				// disguise_alt_key also applies to the left alt key.
				if (disguiseAltDown && targetLayoutHasAltGr != ResultType.ConditionTrue)
				{
					SendKeyEventMenuMask(KeyEventTypes.KeyDown, extraInfo); // Ensures that menu bar is not activated.
					SendKeyEvent(KeyEventTypes.KeyDown, VK_RMENU, 0, 0, false, extraInfo);
					SendKeyEventMenuMask(KeyEventTypes.KeyUp, extraInfo);
				}
				else // No disguise needed.
				{
					// v1.0.43: The following check was added to complement the other .43 fix higher above.
					// It may also fix other things independently of that other fix.
					// The following two lines release LControl before pushing down AltGr because otherwise,
					// the next time RAlt is released (such as by the user), some quirk of the OS or driver
					// prevents it from automatically releasing LControl like it normally does (perhaps
					// because the OS is designed to leave LControl down if it was down before AltGr went down).
					// This would cause LControl to get stuck down for hotkeys in German layout such as:
					//   <^>!a::SendRaw, {
					//   <^>!m::Send ^c
					if (targetLayoutHasAltGr == ResultType.ConditionTrue)
					{
						if ((modifiersLRnow & MOD_LCONTROL) != 0)
							SendKeyEvent(KeyEventTypes.KeyUp, VK_LCONTROL, 0, 0, false, extraInfo);

						if ((modifiersLRnow & MOD_RCONTROL) != 0)
						{
							// Release RCtrl before pressing AltGr, because otherwise the system will not put
							// LCtrl into effect, but it will still inject LCtrl-up when AltGr is released.
							// With LCtrl not in effect and RCtrl being released below, AltGr would instead
							// act as pure RAlt, which would not have the right effect.
							// RCtrl will be put back into effect below if modifiersLRnew & MOD_RCONTROL.
							SendKeyEvent(KeyEventTypes.KeyUp, VK_RCONTROL, 0, 0, false, extraInfo);
							modifiersLRnow &= ~MOD_RCONTROL;
						}
					}

					SendKeyEvent(KeyEventTypes.KeyDown, VK_RMENU, 0, 0, false, extraInfo);

					if (targetLayoutHasAltGr == ResultType.ConditionTrue) // Note that KeyEvent() might have just changed the value of sTargetLayoutHasAltGr.
					{
						// Indicate that control is both down and required down so that the section after this one won't
						// release it.  Without this fix, a hotkey that sends an AltGr char such as "^�:: Send '{Raw}{'"
						// would fail to work under German layout because left-alt would be released after right-alt
						// goes down.
						modifiersLRnow |= MOD_LCONTROL; // To reflect what KeyEvent() did above.
						modifiersLRnew |= MOD_LCONTROL; // All callers want LControl to be down if they wanted AltGr to be down.
					}
				}
			}

			// CONTROL and SHIFT are done only after the above because the above might rely on them
			// being down before for certain early operations.

			// ** CONTROL
			if ((modifiersLRnow & MOD_LCONTROL) != 0 && (modifiersLRnew & MOD_LCONTROL) == 0 // Release LControl.
					// v1.0.41.01: The following line was added to fix the fact that callers do not want LControl
					// released when the new modifier state includes AltGr.  This solves a hotkey such as the following and
					// probably several other circumstances:
					// <^>!a::send \  ; Backslash is solved by this fix; it's manifest via AltGr+Dash on German layout.
					&& !((modifiersLRnew & MOD_RALT) != 0 && targetLayoutHasAltGr == ResultType.ConditionTrue))
				SendKeyEvent(KeyEventTypes.KeyUp, VK_LCONTROL, 0, 0, false, extraInfo);
			else if ((modifiersLRnow & MOD_LCONTROL) == 0 && (modifiersLRnew & MOD_LCONTROL) != 0) // Press down LControl.
				SendKeyEvent(KeyEventTypes.KeyDown, VK_LCONTROL, 0, 0, false, extraInfo);

			if ((modifiersLRnow & MOD_RCONTROL) != 0 && (modifiersLRnew & MOD_RCONTROL) == 0) // Release RControl
				SendKeyEvent(KeyEventTypes.KeyUp, VK_RCONTROL, 0, 0, false, extraInfo);
			else if ((modifiersLRnow & MOD_RCONTROL) == 0 && (modifiersLRnew & MOD_RCONTROL) != 0) // Press down RControl.
				SendKeyEvent(KeyEventTypes.KeyDown, VK_RCONTROL, 0, 0, false, extraInfo);

			// ** SHIFT (PART 2 OF 2)
			// Must follow CTRL and ALT because a release of SHIFT while ALT/CTRL is down-but-soon-to-be-up
			// would switch languages via the OS hotkey.  It's okay if defer_alt_release==true because in that case,
			// CTRL just went down above (by definition of defer_alt_release), which will prevent the language hotkey
			// from firing.
			if (releaseLshift && !releaseShiftBeforeAltCtrl) // Release LShift.
				SendKeyEvent(KeyEventTypes.KeyUp, VK_LSHIFT, 0, 0, false, extraInfo);
			else if ((modifiersLRnow & MOD_LSHIFT) == 0 && (modifiersLRnew & MOD_LSHIFT) != 0) // Press down LShift.
				SendKeyEvent(KeyEventTypes.KeyDown, VK_LSHIFT, 0, 0, false, extraInfo);

			if (releaseRshift && !releaseShiftBeforeAltCtrl) // Release RShift.
				SendKeyEvent(KeyEventTypes.KeyUp, VK_RSHIFT, 0, 0, false, extraInfo);
			else if ((modifiersLRnow & MOD_RSHIFT) == 0 && (modifiersLRnew & MOD_RSHIFT) != 0) // Press down RShift.
				SendKeyEvent(KeyEventTypes.KeyDown, VK_RSHIFT, 0, 0, false, extraInfo);

			// ** KEYS DEFERRED FROM EARLIER
			if (deferWinRelease) // Must be done before ALT because it might rely on ALT being down to disguise release of WIN key.
			{
				if (releaseLwin)
					SendKeyEvent(KeyEventTypes.KeyUp, VK_LWIN, 0, 0, false, extraInfo);

				if (releaseRwin)
					SendKeyEvent(KeyEventTypes.KeyUp, VK_RWIN, 0, 0, false, extraInfo);
			}

			if (deferAltRelease)
			{
				if (releaseLalt)
					SendKeyEvent(KeyEventTypes.KeyUp, VK_LMENU, 0, 0, false, extraInfo);

				if (releaseRalt && targetLayoutHasAltGr != ResultType.ConditionTrue) // If AltGr is present, RAlt would already have been released earlier since defer_alt_release would have been ignored for it.
					SendKeyEvent(KeyEventTypes.KeyUp, VK_RMENU, 0, 0, false, extraInfo);
			}

			// When calling SendKeyEvent(), probably best not to specify a scan code unless
			// absolutely necessary, since some keyboards may have non-standard scan codes
			// which SendKeyEvent() will resolve into the proper vk translations for us.
			// Decided not to Sleep() between keystrokes, even zero, out of concern that this
			// would result in a significant delay (perhaps more than 10ms) while the system
			// is under load.
			// Since the above didn't return early, keybd_event() has been used to change the state
			// of at least one modifier.  As a result, if the caller gave a non-NULL aTargetWindow,
			// it wants us to check if that window belongs to our thread.  If it does, we should do
			// a short msg queue check to prevent an apparent synchronization problem when using
			// ControlSend against the script's own GUI or other windows.  Here is an example of a
			// line whose modifier would not be in effect in time for its keystroke to be modified
			// by it:
			// ControlSend, Edit1, ^{end}, Test Window
			// Update: Another bug-fix for v1.0.21, as was the above: If the keyboard hook is installed,
			// the modifier keystrokes must have a way to get routed through the hook BEFORE the
			// keystrokes get sent via PostMessage().  If not, the correct modifier state will usually
			// not be in effect (or at least not be in sync) for the keys sent via PostMessage() afterward.
			// Notes about the macro below:
			// aTargetWindow!=NULL means ControlSend mode is in effect.
			// The g_KeybdHook check must come first (it should take precedence if both conditions are true).
			// -1 has been verified to be insufficient, at least for the very first letter sent if it is
			// supposed to be capitalized.
			// g_MainThreadID is the only thread of our process that owns any windows.
			var pressDuration = sendMode == SendModes.Play ? ThreadAccessors.A_KeyDurationPlay : ThreadAccessors.A_KeyDuration;

			if (pressDuration > -1) // SM_PLAY does use DoKeyDelay() to store a delay item in the event array.
			{
				// Since modifiers were changed by the above, do a key-delay if the special intra-keystroke
				// delay is in effect.
				// Since there normally isn't a delay between a change in modifiers and the first keystroke,
				// if a PressDuration is in effect, also do it here to improve reliability (I have observed
				// cases where modifiers need to be left alone for a short time in order for the keystrokes
				// that follow to be be modified by the intended set of modifiers).
				DoKeyDelay((int)pressDuration); // It knows not to do the delay for SM_INPUT.
			}
			else // Since no key-delay was done, check if a a delay is needed for any other reason.
			{
				// IMPORTANT UPDATE for v1.0.39: Now that the hooks are in a separate thread from the part
				// of the program that sends keystrokes for the script, you might think synchronization of
				// keystrokes would become problematic or at least change.  However, this is apparently not
				// the case.  MSDN doesn't spell this out, but testing shows that what happens with a low-level
				// hook is that the moment a keystroke comes into a thread (either physical or simulated), the OS
				// immediately calls something similar to SendMessage() from that thread to notify the hook
				// thread that a keystroke has arrived.  However, if the hook thread's priority is lower than
				// some other thread next in line for a timeslice, it might take some time for the hook thread
				// to get a timeslice (that's why the hook thread is given a high priority).
				// The SendMessage() call doesn't return until its timeout expires (as set in the registry for
				// hooks) or the hook thread processes the keystroke (which requires that it call something like
				// GetMessage/PeekMessage followed by a HookProc "return").  This is good news because it serializes
				// keyboard and mouse input to make the presence of the hook transparent to other threads (unless
				// the hook does something to reveal itself, such as suppressing keystrokes). Serialization avoids
				// any chance of synchronization problems such as a program that changes the state of a key then
				// immediately checks the state of that same key via GetAsyncKeyState().  Another way to look at
				// all of this is that in essence, a single-threaded hook program that simulates keystrokes or
				// mouse clicks should behave the same when the hook is moved into a separate thread because from
				// the program's point-of-view, keystrokes & mouse clicks result in a calling the hook almost
				// exactly as if the hook were in the same thread.
				if (targetWindow != 0)
				{
					//if (hookId != 0)
					if (script.HookThread.HasKbdHook())
						Flow.SleepWithoutInterruption(0); // Don't use ternary operator to combine this with next due to "else if".
#if WINDOWS
					else if (WindowsAPI.GetWindowThreadProcessId(targetWindow, out var _) == script.ProcessesData.MainThreadID)
						Flow.SleepWithoutInterruption(-1);
#endif
				}
			}

			// Commented out because a return value is no longer needed by callers (since we do the key-delay here,
			// if appropriate).
			//return modifiersLRnow ^ modifiersLRnew; // Calculate the set of modifiers that changed (currently excludes AltGr's change of LControl's state).
			// NOTES about "release_shift_before_alt_ctrl":
			// If going down on alt or control (but not both, though it might not matter), and shift is to be released:
			//  Release shift first.
			// If going down on shift, and control or alt (but not both) is to be released:
			//  Release ctrl/alt first (this is already the case so nothing needs to be done).
			//
			// Release both shifts before going down on lalt/ralt or lctrl/rctrl (but not necessary if going down on
			// *both* alt+ctrl?
			// Release alt and both controls before going down on lshift/rshift.
			// Rather than the below, do the above (for the reason below).
			// But if do this, don't want to prevent a legit/intentional language switch such as:
			//    Send {LAlt down}{Shift}{LAlt up}.
			// If both Alt and Shift are down, Win or Ctrl (or any other key for that matter) must be pressed before either
			// is released.
			// If both Ctrl and Shift are down, Win or Alt (or any other key) must be pressed before either is released.
			// remind: Despite what the Regional Settings window says, RAlt+Shift (and Shift+RAlt) is also a language hotkey (i.e. not just LAlt), at least when RAlt isn't AltGr!
			// remind: Control being down suppresses language switch only once.  After that, control being down doesn't help if lalt is re-pressed prior to re-pressing shift.
			//
			// Language switch occurs when:
			// alt+shift (upon release of shift)
			// shift+alt (upon release of lalt)
			// ctrl+shift (upon release of shift)
			// shift+ctrl (upon release of ctrl)
			// Because language hotkey only takes effect upon release of Shift, it can be disguised via a Control keystroke if that is ever needed.
			// NOTES: More details about disguising ALT and WIN:
			// Registered Alt hotkeys don't quite work if the Alt key is released prior to the suffix.
			// Key history for Alt-B hotkey released this way, which undesirably activates the menu bar:
			// A4  038      d   0.03    Alt
			// 42  030      d   0.03    B
			// A4  038      u   0.24    Alt
			// 42  030      u   0.19    B
			// Testing shows that the above does not happen for a normal (non-hotkey) alt keystroke such as Alt-8,
			// so the above behavior is probably caused by the fact that B-down is suppressed by the OS's hotkey
			// routine, but not B-up.
			// The above also happens with registered WIN hotkeys, but only if the Send cmd resulted in the WIN
			// modifier being pushed back down afterward to match the fact that the user is still holding it down.
			// This behavior applies to ALT hotkeys also.
			// One solution: if the hook is installed, have it keep track of when the start menu or menu bar
			// *would* be activated.  These tracking vars can be consulted by the Send command, and the hook
			// can also be told when to use them after a registered hotkey has been pressed, so that the Alt-up
			// or Win-up keystroke that belongs to it can be disguised.
			// The following are important ways in which other methods of disguise might not be sufficient:
			// Sequence: shift-down win-down shift-up win-up: invokes Start Menu when WIN is held down long enough
			// to auto-repeat.  Same when Ctrl or Alt is used in lieu of Shift.
			// Sequence: shift-down alt-down alt-up shift-up: invokes menu bar.  However, as long as another key,
			// even Shift, is pressed down *after* alt is pressed down, menu bar is not activated, e.g. alt-down
			// shift-down shift-up alt-up.  In addition, CTRL always prevents ALT from activating the menu bar,
			// even with the following sequences:
			// ctrl-down alt-down alt-up ctrl-up
			// alt-down ctrl-down ctrl-up alt-up
			// (also seems true for all other permutations of Ctrl/Alt)
		}
	}
}