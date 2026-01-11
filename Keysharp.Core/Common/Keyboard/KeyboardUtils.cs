using static Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Common.Keyboard
{
	internal class KeyboardUtilsData
	{
		internal List<int> mouseList = [];
		internal List<int> keyboardList = [];
		internal List<int> kbMouseList = [];
	}

	internal class KeyboardUtils
	{
		internal const uint MOD_ALT = 0x0001;
		internal const uint MOD_CONTROL = 0x0002;
		internal const uint MOD_LALT = 0x04;
		internal const uint MOD_LCONTROL = 0x01;
		internal const uint MOD_LSHIFT = 0x10;
		internal const uint MOD_LWIN = 0x40;
		internal const uint MOD_RALT = 0x08;
		internal const uint MOD_RCONTROL = 0x02;
		internal const uint MOD_RSHIFT = 0x20;
		internal const uint MOD_RWIN = 0x80;
		internal const uint MOD_SHIFT = 0x0004;
		internal const uint MOD_WIN = 0x0008;
		internal const uint MODLR_COUNT = 8;
		internal const uint MODLR_MAX = 0xFF;
		internal const uint MODLR_LMASK = MOD_LCONTROL | MOD_LALT | MOD_LSHIFT | MOD_LWIN;
		internal const uint MODLR_RMASK = MOD_RCONTROL | MOD_RALT | MOD_RSHIFT | MOD_RWIN;
		internal const uint MODLR_MASK = MODLR_LMASK | MODLR_RMASK;

		internal const int KEYEVENTF_EXTENDEDKEY = 1;
		internal const int KEYEVENTF_KEYUP = 2;
		internal const int KEYEVENTF_UNICODE = 4;
		internal const int KEYEVENTF_SCANCODE = 8;

		internal static string[] SEND_MODES = ["Event", "Input", "Play", "InputThenPlay"]; // Must match the SendModes enum.

		internal static uint MakeLong(short lowPart, short highPart) => ((ushort)lowPart) | (uint)(highPart << 16);


		internal KeyboardUtils()
		{
#if LINUX
			var inputStr = "xinput".Bash();
			var kud = Script.TheScript.KeyboardUtilsData;
			var mouseList = kud.mouseList;
			var keyboardList = kud.keyboardList;
			var kbMouseList = kud.kbMouseList;

			mouseList.Clear();
			keyboardList.Clear();
			kbMouseList.Clear();

			foreach (Range r in inputStr.AsSpan().SplitAny(CrLf))
			{
				var split = inputStr.AsSpan(r).Trim();

				//KeysharpEnhancements.OutputDebugLine($"Examining split xinput string: {split}");
				if (split.Length > 0 && !split.Contains("XTEST", StringComparison.OrdinalIgnoreCase))
				{
					var mouse = split.Contains("slave  pointer", StringComparison.OrdinalIgnoreCase);//The double spaces are intentional.
					var kb = split.Contains("slave  keyboard", StringComparison.OrdinalIgnoreCase);

					if (mouse || kb)//The two spaces are intentional.
					{
						foreach (Range r2 in split.SplitAny(SpaceTab))
						{
							var lineSplit = split[r2].Trim();
							//KeysharpEnhancements.OutputDebugLine($"Examining {(mouse ? "mouse" : "kb")} line split: {lineSplit}");

							if (lineSplit.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
							{
								if (int.TryParse(lineSplit.Slice(3), out var id))
								{
									if (mouse)
										mouseList.Add(id);
									else if (kb)
										keyboardList.Add(id);

									break;
								}
							}
						}
					}
				}
			}

			kbMouseList.AddRange(keyboardList);
			kbMouseList.AddRange(mouseList);
#endif
		}

		internal static void AdjustKeyState(byte[] keyState, uint modifiersLR)//Unsure if this should be in the base. Can it be cross platform?
		// Caller has ensured that aKeyState is a 256-BYTE array of key states, in the same format used
		// by GetKeyboardState() and ToAsciiEx().
		{
			var sd = KeyboardMouseSender.StateDown;
			keyState[VK_LSHIFT] = (byte)((modifiersLR & MOD_LSHIFT) != 0 ? sd : 0);
			keyState[VK_RSHIFT] = (byte)((modifiersLR & MOD_RSHIFT) != 0 ? sd : 0);
			keyState[VK_LCONTROL] = (byte)((modifiersLR & MOD_LCONTROL) != 0 ? sd : 0);
			keyState[VK_RCONTROL] = (byte)((modifiersLR & MOD_RCONTROL) != 0 ? sd : 0);
			keyState[VK_LMENU] = (byte)((modifiersLR & MOD_LALT) != 0 ? sd : 0);
			keyState[VK_RMENU] = (byte)((modifiersLR & MOD_RALT) != 0 ? sd : 0);
			keyState[VK_LWIN] = (byte)((modifiersLR & MOD_LWIN) != 0 ? sd : 0);
			keyState[VK_RWIN] = (byte)((modifiersLR & MOD_RWIN) != 0 ? sd : 0);
			// Update the state of neutral keys only after the above, in case both keys of the pair were wrongly down:
			keyState[VK_SHIFT] = (byte)((keyState[VK_LSHIFT] != 0 || keyState[VK_RSHIFT] != 0) ? sd : 0);
			keyState[VK_CONTROL] = (byte)((keyState[VK_LCONTROL] != 0 || keyState[VK_RCONTROL] != 0) ? sd : 0);
			keyState[VK_MENU] = (byte)((keyState[VK_LMENU] != 0 || keyState[VK_RMENU] != 0) ? sd : 0);
		}

		/// <summary>
		/// Convert the input param to a modifiersLR value and return it.
		/// </summary>
		/// <param name="modifiers"></param>
		/// <returns></returns>
		internal static uint ConvertModifiers(uint modifiers)
		{
			var modifiersLR = 0u;

			if ((modifiers & MOD_WIN) != 0) modifiersLR |= MOD_LWIN | MOD_RWIN;

			if ((modifiers & MOD_ALT) != 0) modifiersLR |= MOD_LALT | MOD_RALT;

			if ((modifiers & MOD_CONTROL) != 0) modifiersLR |= MOD_LCONTROL | MOD_RCONTROL;

			if ((modifiers & MOD_SHIFT) != 0) modifiersLR |= MOD_LSHIFT | MOD_RSHIFT;

			return modifiersLR;
		}

		internal static uint ConvertModifiersLR(uint modifiersLR)
		{
			var modifiers = 0u;

			if ((modifiersLR & (MOD_LWIN | MOD_RWIN)) != 0) modifiers |= MOD_WIN;

			if ((modifiersLR & (MOD_LALT | MOD_RALT)) != 0) modifiers |= MOD_ALT;

			if ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) != 0) modifiers |= MOD_SHIFT;

			if ((modifiersLR & (MOD_LCONTROL | MOD_RCONTROL)) != 0) modifiers |= MOD_CONTROL;

			return modifiers;
		}

		internal static bool IsModifierVk(uint vk) => vk switch
		{
			VK_LSHIFT or VK_RSHIFT or VK_SHIFT or
			VK_LCONTROL or VK_RCONTROL or VK_CONTROL or
			VK_LMENU or VK_RMENU or VK_MENU or
			VK_LWIN or VK_RWIN => true,
			_ => false
		};
	}

	[Flags]
	internal enum KeyModifiers
	{
		None = 0,
		Alt = 1,
		Control = 2,
		Shift = 4,

		// Either WINDOWS key was held down. These keys are labeled with the Windows logo.
		// Keyboard shortcuts that involve the WINDOWS key are reserved for use by the
		// operating system.
		Windows = 8
	}

	// FAIL = 0 to remind that FAIL should have the value zero instead of something arbitrary
	// because some callers may simply evaluate the return result as true or false
	// (and false is a failure):
	public enum ResultType
	{
		Fail = 0, Ok, Warn = Ok, CriticalError// Some things might rely on OK==1 (i.e. boolean "true")
		, ConditionTrue, ConditionFalse
		, LoopBreak, LoopContinue
		, EarlyReturn, EarlyExit// EARLY_EXIT needs to be distinct from FAIL for ExitApp() and AutoExecSection().
		, FailOrOk// For LineError/RuntimeError, error is continuable.
	}

	public enum SendModes
	{
		Event,// Event must be zero.
		Input,
		Play,
		InputThenPlay,
		Invalid
	}

	public enum SendRawModes
	{
		NotRaw,
		Raw,
		RawText
	}



	internal enum Actions
	{
		// Seems best to make ACT_INVALID zero so that it will be the ZeroMemory() default within
		// any POD structures that contain an action_type field:
		ACT_INVALID = 0  // These should both be zero for initialization and function-return-value purposes.

		, ACT_ASSIGNEXPR
		, ACT_EXPRESSION

		// Keep ACT_BLOCK_BEGIN as the first "control flow" action, for range checks with ACT_FIRST_CONTROL_FLOW:
		, ACT_BLOCK_BEGIN, ACT_BLOCK_END

		, ACT_STATIC
		, ACT_HOTKEY_IF // Must be before ACT_FIRST_COMMAND.
		, ACT_EXIT // Used both with BIF_PerformAction and AddLine(), but excluded from the "named" range below so the function is preferred.
		, ACT_IF, ACT_FIRST_NAMED_ACTION = ACT_IF
		, ACT_ELSE
		, ACT_LOOP, ACT_LOOP_FILE, ACT_LOOP_REG, ACT_LOOP_READ, ACT_LOOP_PARSE
		, ACT_FOR, ACT_WHILE, ACT_UNTIL // Keep LOOP, FOR, WHILE and UNTIL together and in this order for range checks in various places.
		, ACT_BREAK, ACT_CONTINUE // Keep ACT_FOR..ACT_CONTINUE together for ACT_EXPANDS_ITS_OWN_ARGS.
		, ACT_GOTO
		, ACT_FIRST_JUMP = ACT_BREAK, ACT_LAST_JUMP = ACT_GOTO // Actions which accept a label name.
		, ACT_RETURN
		, ACT_TRY, ACT_CATCH, ACT_FINALLY, ACT_THROW // Keep TRY, CATCH and FINALLY together and in this order for range checks.
		, ACT_SWITCH, ACT_CASE // Keep ACT_TRY..ACT_CASE together for ACT_EXPANDS_ITS_OWN_ARGS.
		, ACT_LAST_NAMED_ACTION = ACT_CASE

		// ================================================================================
		// All others are not included in g_act, and are only used for misc. purposes:
		// ================================================================================
		, ACT_MOUSEMOVE, ACT_MOUSECLICK, ACT_MOUSECLICKDRAG
	};

	internal enum HookType
	{
		None = 0,
		Keyboard = 1,
		Mouse = 2,
		Fail = 0xFF
	}

	internal enum KeyDownTypes
	{
		Temp = 0,
		Persistent,
		Remap
	}

	internal enum KeyEventTypes
	{
		KeyDown,
		KeyUp,
		KeyDownAndUp
	}

	internal enum KeyStateTypes//For use with GetKeyJoyState(), etc.
	{
		Logical,
		Physical,
		Toggle
	}

	internal enum MenuType
	{
		None = 0,//None must be zero.
		Popup,
		Bar
	}

	internal enum MessageMode
	{ WaitForMessages, ReturnAfterMessages, ReturnAfterMessagesSpecialFilter };

	// In above, SM_INPUT falls back to SM_EVENT when the SendInput mode would be defeated by the presence
	// of a keyboard/mouse hooks in another script (it does this because SendEvent is superior to a
	// crippled/interruptible SendInput due to SendEvent being able to dynamically adjust to changing
	// conditions [such as the user releasing a modifier key during the Send]).  By contrast,
	// SM_INPUT_FALLBACK_TO_PLAY falls back to the SendPlay mode.  SendInput has this extra fallback behavior
	// because it's likely to become the most popular sending method.
	// These are used for things that can be turned on, off, or left at a
	// neutral default value that is neither on nor off.  INVALID must
	// be zero:
	internal enum ToggleValueType
	{
		Invalid = 0,
		On,
		Off,
		AlwaysOn,
		AlwaysOff,
		Toggle,
		Neutral,
		Send,
		Mouse,
		SendAndMouse,
		Default,
		MouseMove,
		MouseMoveOff
	}

	internal static class ScanCodes
	{
		internal const uint NumpadEnter = 0x11C;//These will likely need to be different on linux.//TODO
		internal const uint Insert = 0x152;
		internal const uint Delete = 0x153;
		internal const uint Home = 0x147;
		internal const uint End = 0x14F;
		internal const uint Up = 0x148;
		internal const uint Down = 0x150;
		internal const uint Left = 0x14B;
		internal const uint Right = 0x14D;
		internal const uint PgUp = 0x149;
		internal const uint PgDn = 0x151;

		// These are the same scan codes as their counterpart except the extended flag is 0 rather than
		// 1 (0xE0 uncompressed):
		internal const uint Enter = 0x1C;

		// In addition, the below dual-state numpad keys share the same scan code (but different vk's)
		// regardless of the state of numlock:
		internal const uint NumpadDel = 0x53;
		internal const uint NumpadIns = 0x52;
		internal const uint NumpadEnd = 0x4F;
		internal const uint NumpadHome = 0x47;
		internal const uint NumpadClear = 0x4C;
		internal const uint NumpadUp = 0x48;
		internal const uint NumpadDn = 0x50;
		internal const uint NumpadLeft = 0x4B;
		internal const uint NumpadRight = 0x4D;
		internal const uint NumpadPgUp = 0x49;
		internal const uint NumpadPgDn = 0x51;
		internal const uint NumpadDot = NumpadDel;
		internal const uint Numpad0 = NumpadIns;
		internal const uint Numpad1 = NumpadEnd;
		internal const uint Numpad2 = NumpadDn;
		internal const uint Numpad3 = NumpadPgDn;
		internal const uint Numpad4 = NumpadLeft;
		internal const uint Numpad5 = NumpadClear;
		internal const uint Numpad6 = NumpadRight;
		internal const uint Numpad7 = NumpadHome;
		internal const uint Numpad8 = NumpadUp;
		internal const uint Numpad9 = NumpadPgUp;
		internal const uint NumpadLock = 0x145;
		internal const uint NumpadDiv = 0x135;
		internal const uint NumpadMult = 0x037;
		internal const uint NumpadSub = 0x04A;
		internal const uint NumpadAdd = 0x04E;
		internal const uint Pause = 0x045;
		internal const uint LControl = 0x01D;
		internal const uint RControl = 0x11D;
		internal const uint LShift = 0x02A;
		internal const uint RShift = 0x136; // Must be extended, at least on WinXP, or there will be problems, e.g. SetModifierLRState().
		internal const uint LAlt = 0x038;
		internal const uint RAlt = 0x138;
		internal const uint LWin = 0x15B;
		internal const uint RWin = 0x15C;
		internal const uint AppsKey = 0x15D;
		internal const uint PrintScreen = 0x137;

		//
		// The system injects events with these scan codes:
		//  - For Shift-up prior to a Numpad keydown or keyup if Numlock is on and Shift is down,
		//    e.g. to translate Shift+Numpad1 to unshifted-NumpadEnd.
		//  - For Shift-down prior to a non-Numpad keydown if a Numpad key is still held down
		//    after the above, e.g. for Shift+Numpad1+Esc.
		//  - For LCtrl generated by AltGr.
		// Note that the system uses the normal scan codes (0x2A or 0x36) for Shift-down following
		// the Numpad keyup if no other keys were pushed.  Our hook filters out the second byte to
		// simplify the code, so these values can only be found in KBDLLHOOKSTRUCT::scanCode.
		// Find "fake shift-key events" for older and more detailed comments.
		// Note that 0x0200 corresponds to SCANCODE_SIMULATED in kbd.h (DDK).
		internal const uint FakeFlag = 0x200;
		internal const uint FakeLShift = 0x22A;
		internal const uint FakeRShift = 0x236; // This is the actual scancode received by the hook, excluding the 0x100 we add for "extended" keys.

		// Testing with the KbdEdit Demo preview mode indicates that AltGr will send this SC
		// even if the VK assigned to 0x1D is changed.  It is a combination of SCANCODE_CTRL
		// and SCANCODE_SIMULATED, which are defined in kbd.h (Windows DDK).
		internal const uint FakeLControl = 0x21D;
	}

	/// <summary>
	/// We cannot use the System.Windows.Forms.Keys enum because it does not contain various keys
	/// such as mouse wheel.
	/// </summary>
	internal static class VirtualKeys
	{
		internal const uint VK_LBUTTON = 0x01;//These will likely need to be different on linux.//TODO
		internal const uint VK_RBUTTON = 0x02;
		internal const uint VK_CANCEL = 0x03;
		internal const uint VK_MBUTTON = 0x04;//NOT contiguous with L & RBUTTON
		internal const uint VK_XBUTTON1 = 0x05;//NOT contiguous with L & RBUTTON
		internal const uint VK_XBUTTON2 = 0x06;//NOT contiguous with L & RBUTTON
		internal const uint VK_LSHIFT = 0xA0;
		internal const uint VK_RSHIFT = 0xA1;
		internal const uint VK_LCONTROL = 0xA2;
		internal const uint VK_RCONTROL = 0xA3;
		internal const uint VK_LMENU = 0xA4;
		internal const uint VK_RMENU = 0xA5;
		internal const uint VK_BROWSER_BACK = 0xA6;
		internal const uint VK_BROWSER_FORWARD = 0xA7;
		internal const uint VK_BROWSER_REFRESH = 0xA8;
		internal const uint VK_BROWSER_STOP = 0xA9;
		internal const uint VK_BROWSER_SEARCH = 0xAA;
		internal const uint VK_BROWSER_FAVORITES = 0xAB;
		internal const uint VK_BROWSER_HOME = 0xAC;
		internal const uint VK_VOLUME_MUTE = 0xAD;
		internal const uint VK_VOLUME_DOWN = 0xAE;
		internal const uint VK_VOLUME_UP = 0xAF;
		internal const uint VK_MEDIA_NEXT_TRACK = 0xB0;
		internal const uint VK_MEDIA_PREV_TRACK = 0xB1;
		internal const uint VK_MEDIA_STOP = 0xB2;
		internal const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
		internal const uint VK_LAUNCH_MAIL = 0xB4;
		internal const uint VK_LAUNCH_MEDIA_SELECT = 0xB5;
		internal const uint VK_LAUNCH_APP1 = 0xB6;
		internal const uint VK_LAUNCH_APP2 = 0xB7;
		internal const uint VK_OEM_1 = 0xBA;
		internal const uint VK_OEM_PLUS = 0xBB;
		internal const uint VK_OEM_COMMA = 0xBC;
		internal const uint VK_OEM_MINUS = 0xBD;
		internal const uint VK_OEM_PERIOD = 0xBE;
		internal const uint VK_OEM_2 = 0xBF;
		internal const uint VK_OEM_3 = 0xC0;
		internal const uint VK_OEM_4 = 0xDB;
		internal const uint VK_OEM_5 = 0xDC;
		internal const uint VK_OEM_6 = 0xDD;
		internal const uint VK_OEM_7 = 0xDE;
		internal const uint VK_OEM_8 = 0xDF;
		internal const uint VK_BACK = 0x08;
		internal const uint VK_TAB = 0x09;
		internal const uint VK_CLEAR = 0x0C;
		internal const uint VK_RETURN = 0x0D;
		internal const uint VK_SHIFT = 0x10;
		internal const uint VK_CONTROL = 0x11;
		internal const uint VK_MENU = 0x12;
		internal const uint VK_PAUSE = 0x13;
		internal const uint VK_CAPITAL = 0x14;
		internal const uint VK_ESCAPE = 0x1B;
		internal const uint VK_CONVERT = 0x1C;
		internal const uint VK_NONCONVERT = 0x1D;
		internal const uint VK_ACCEPT = 0x1E;
		internal const uint VK_MODECHANGE = 0x1F;
		internal const uint VK_SPACE = 0x20;
		internal const uint VK_PRIOR = 0x21;
		internal const uint VK_NEXT = 0x22;
		internal const uint VK_END = 0x23;
		internal const uint VK_HOME = 0x24;
		internal const uint VK_LEFT = 0x25;
		internal const uint VK_UP = 0x26;
		internal const uint VK_RIGHT = 0x27;
		internal const uint VK_DOWN = 0x28;
		internal const uint VK_SELECT = 0x29;
		internal const uint VK_PRINT = 0x2A;
		internal const uint VK_EXECUTE = 0x2B;
		internal const uint VK_SNAPSHOT = 0x2C;
		internal const uint VK_INSERT = 0x2D;
		internal const uint VK_DELETE = 0x2E;
		internal const uint VK_HELP = 0x2F;
		internal const uint VK_LWIN = 0x5B;
		internal const uint VK_RWIN = 0x5C;
		internal const uint VK_APPS = 0x5D;
		internal const uint VK_SLEEP = 0x5F;
		internal const uint VK_NUMPAD0 = 0x60;
		internal const uint VK_NUMPAD1 = 0x61;
		internal const uint VK_NUMPAD2 = 0x62;
		internal const uint VK_NUMPAD3 = 0x63;
		internal const uint VK_NUMPAD4 = 0x64;
		internal const uint VK_NUMPAD5 = 0x65;
		internal const uint VK_NUMPAD6 = 0x66;
		internal const uint VK_NUMPAD7 = 0x67;
		internal const uint VK_NUMPAD8 = 0x68;
		internal const uint VK_NUMPAD9 = 0x69;
		internal const uint VK_MULTIPLY = 0x6A;
		internal const uint VK_ADD = 0x6B;
		internal const uint VK_SEPARATOR = 0x6C;
		internal const uint VK_SUBTRACT = 0x6D;
		internal const uint VK_DECIMAL = 0x6E;
		internal const uint VK_DIVIDE = 0x6F;
		internal const uint VK_F1 = 0x70;
		internal const uint VK_F2 = 0x71;
		internal const uint VK_F3 = 0x72;
		internal const uint VK_F4 = 0x73;
		internal const uint VK_F5 = 0x74;
		internal const uint VK_F6 = 0x75;
		internal const uint VK_F7 = 0x76;
		internal const uint VK_F8 = 0x77;
		internal const uint VK_F9 = 0x78;
		internal const uint VK_F10 = 0x79;
		internal const uint VK_F11 = 0x7A;
		internal const uint VK_F12 = 0x7B;
		internal const uint VK_F13 = 0x7C;
		internal const uint VK_F14 = 0x7D;
		internal const uint VK_F15 = 0x7E;
		internal const uint VK_F16 = 0x7F;
		internal const uint VK_F17 = 0x80;
		internal const uint VK_F18 = 0x81;
		internal const uint VK_F19 = 0x82;
		internal const uint VK_F20 = 0x83;
		internal const uint VK_F21 = 0x84;
		internal const uint VK_F22 = 0x85;
		internal const uint VK_F23 = 0x86;
		internal const uint VK_F24 = 0x87;
		internal const uint VK_NUMLOCK = 0x90;
		internal const uint VK_SCROLL = 0x91;
		internal const uint VK_PACKET = 0xE7;
		// Create some "fake" virtual keys to simplify sections of the code.
		// According to winuser.h, the following ranges (among others)
		// are considered "unassigned" rather than "reserved", so should be
		// fairly safe to use for the foreseeable future.  0xFF should probably
		// be avoided since it's sometimes used as a failure indictor by API
		// calls.  And 0x00 should definitely be avoided because it is used
		// to indicate failure by many functions that deal with virtual keys.
		// 0x88 - 0x8F : unassigned
		// 0x97 - 0x9F : unassigned (this range seems less likely to be used)
		internal const uint VK_NEW_MOUSE_FIRST = 0x9A;
		internal const uint VK_WHEEL_LEFT = 0x9C;
		internal const uint VK_WHEEL_RIGHT = 0x9D;
		internal const uint VK_WHEEL_DOWN = 0x9E;
		internal const uint VK_WHEEL_UP = 0x9F;
		internal const uint VK_NEW_MOUSE_LAST = 0x9F;
		internal const uint VKMAX = 256;
	}
}