#if LINUX
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// Windows VK ⇄ Linux evdev (input service) scan code tables. Fixed mappings used by the
	/// input service backend. Part of the unified <see cref="KeyCodes"/> facade.
	/// <para>
	/// This is a second copy of the daemon's table in <c>keysharp-input/src/platform/vk_evdev.c</c>, and
	/// the two must agree: the daemon stamps the VK on every hook event it delivers, while this table answers
	/// the client-side questions (key names, VK→code synthesis, key-state bitmaps) — so a divergence would make
	/// one key report one VK when pressed and a different one when queried. They cannot be shared because they
	/// live in different processes and languages; both sides are covered by tests
	/// (<c>LinuxEvdevMappingsCoverPortableAndAlternateKeyCodes</c> here, and the VK/evdev case in
	/// <c>keysharp-input/tests/daemon_unit_tests.c</c>), so add every new mapping to both.
	/// </para>
	/// </summary>
	internal static partial class KeyCodes
	{
		internal static uint VkToEvdev(uint vk, bool returnSecondary = false)
		{
			// Only VK_RETURN has two AHK key names: the main Enter (KEY_ENTER = 28) and NumpadEnter
			// (KEY_KPENTER = 96). Evdev has other synonymous physical codes, such as KEY_MAIL and
			// KEY_EMAIL, but those represent one AHK key name and are recognised by EvdevToVk.
			// Mirroring the Windows mapper, a secondary request for a VK that has none returns 0 — callers use
			// that both as the "this VK maps to two scan codes" test and to fall back to the primary code.
			if (returnSecondary)
				return vk == VK_RETURN ? 96u : 0u;

			return vk switch
			{
				>= (uint)'A' and <= (uint)'Z' => vk switch
				{
					(uint)'A' => 30u, (uint)'B' => 48u, (uint)'C' => 46u, (uint)'D' => 32u,
					(uint)'E' => 18u, (uint)'F' => 33u, (uint)'G' => 34u, (uint)'H' => 35u,
					(uint)'I' => 23u, (uint)'J' => 36u, (uint)'K' => 37u, (uint)'L' => 38u,
					(uint)'M' => 50u, (uint)'N' => 49u, (uint)'O' => 24u, (uint)'P' => 25u,
					(uint)'Q' => 16u, (uint)'R' => 19u, (uint)'S' => 31u, (uint)'T' => 20u,
					(uint)'U' => 22u, (uint)'V' => 47u, (uint)'W' => 17u, (uint)'X' => 45u,
					(uint)'Y' => 21u, (uint)'Z' => 44u,
					_ => 0u
				},
				>= (uint)'0' and <= (uint)'9' => vk switch
				{
					(uint)'0' => 11u, (uint)'1' => 2u, (uint)'2' => 3u, (uint)'3' => 4u,
					(uint)'4' => 5u, (uint)'5' => 6u, (uint)'6' => 7u, (uint)'7' => 8u,
					(uint)'8' => 9u, (uint)'9' => 10u,
					_ => 0u
				},
				VK_BACK => 14u,
				VK_TAB => 15u,
				VK_CLEAR => 0x163u,
				VK_RETURN => 28u,
				VK_ESCAPE => 1u,
				VK_SPACE => 57u,
				VK_PRIOR => 104u,
				VK_NEXT => 109u,
				VK_END => 107u,
				VK_HOME => 102u,
				VK_LEFT => 105u,
				VK_UP => 103u,
				VK_RIGHT => 106u,
				VK_DOWN => 108u,
				VK_INSERT => 110u,
				VK_DELETE => 111u,
				VK_SELECT => 0x161u,
				VK_PRINT => 210u,
				VK_EXECUTE => 134u,
				VK_SNAPSHOT => 99u,
				VK_HELP => 138u,
				VK_PAUSE => 119u,
				VK_CAPITAL => 58u,
				VK_NUMLOCK => 69u,
				VK_SCROLL => 70u,
				VK_CONVERT => 92u,
				VK_NONCONVERT => 94u,
				VK_KANA => 93u,
				VK_HANJA => 123u,
				VK_MODECHANGE => 0x175u,
				VK_LCONTROL or VK_CONTROL => 29u,
				VK_RCONTROL => 97u,
				VK_LSHIFT or VK_SHIFT => 42u,
				VK_RSHIFT => 54u,
				VK_LMENU or VK_MENU => 56u,
				VK_RMENU => 100u,
				VK_LWIN => 125u,
				VK_RWIN => 126u,
				VK_APPS => 127u,
				VK_SLEEP => 142u,
				>= VK_F1 and <= VK_F24 => vk switch
				{
					VK_F1 => 59u, VK_F2 => 60u, VK_F3 => 61u, VK_F4 => 62u,
					VK_F5 => 63u, VK_F6 => 64u, VK_F7 => 65u, VK_F8 => 66u,
					VK_F9 => 67u, VK_F10 => 68u, VK_F11 => 87u, VK_F12 => 88u,
					VK_F13 => 183u, VK_F14 => 184u, VK_F15 => 185u, VK_F16 => 186u,
					VK_F17 => 187u, VK_F18 => 188u, VK_F19 => 189u, VK_F20 => 190u,
					VK_F21 => 191u, VK_F22 => 192u, VK_F23 => 193u, VK_F24 => 194u,
					_ => 0u
				},
				VK_NUMPAD0 => 82u,
				VK_NUMPAD1 => 79u,
				VK_NUMPAD2 => 80u,
				VK_NUMPAD3 => 81u,
				VK_NUMPAD4 => 75u,
				VK_NUMPAD5 => 76u,
				VK_NUMPAD6 => 77u,
				VK_NUMPAD7 => 71u,
				VK_NUMPAD8 => 72u,
				VK_NUMPAD9 => 73u,
				VK_MULTIPLY => 55u,
				VK_ADD => 78u,
				VK_SEPARATOR => 121u,
				VK_SUBTRACT => 74u,
				VK_DECIMAL => 83u,
				VK_DIVIDE => 98u,
				VK_OEM_1 => 39u,
				VK_OEM_PLUS => 13u,
				VK_OEM_COMMA => 51u,
				VK_OEM_MINUS => 12u,
				VK_OEM_PERIOD => 52u,
				VK_OEM_2 => 53u,
				VK_OEM_3 => 41u,
				VK_OEM_4 => 26u,
				VK_OEM_5 => 43u,
				VK_OEM_6 => 27u,
				VK_OEM_7 => 40u,
				VK_OEM_102 => 86u,
				// Volume / media / browser / application keys (input-event-codes.h KEY_*)
				VK_VOLUME_MUTE => 113u,         // KEY_MUTE
				VK_VOLUME_DOWN => 114u,         // KEY_VOLUMEDOWN
				VK_VOLUME_UP => 115u,           // KEY_VOLUMEUP
				VK_MEDIA_PLAY_PAUSE => 164u,    // KEY_PLAYPAUSE
				VK_MEDIA_NEXT_TRACK => 163u,    // KEY_NEXTSONG
				VK_MEDIA_PREV_TRACK => 165u,    // KEY_PREVIOUSSONG
				VK_MEDIA_STOP => 166u,          // KEY_STOPCD
				VK_BROWSER_STOP => 128u,        // KEY_STOP
				VK_BROWSER_BACK => 158u,        // KEY_BACK
				VK_BROWSER_FORWARD => 159u,     // KEY_FORWARD
				VK_BROWSER_REFRESH => 173u,     // KEY_REFRESH
				VK_BROWSER_SEARCH => 217u,      // KEY_SEARCH
				VK_BROWSER_FAVORITES => 156u,   // KEY_BOOKMARKS
				VK_BROWSER_HOME => 172u,        // KEY_HOMEPAGE
				VK_LAUNCH_MAIL => 155u,         // KEY_MAIL
				VK_LAUNCH_MEDIA_SELECT => 226u, // KEY_MEDIA
				VK_LAUNCH_APP1 => 148u,         // KEY_PROG1
				VK_LAUNCH_APP2 => 149u,         // KEY_PROG2
				VK_CANCEL => 223u,              // KEY_CANCEL
				_ => 0u
			};
		}

		internal static uint EvdevToVk(uint sc)
		{
			return sc switch
			{
				30u => (uint)'A', 48u => (uint)'B', 46u => (uint)'C', 32u => (uint)'D',
				18u => (uint)'E', 33u => (uint)'F', 34u => (uint)'G', 35u => (uint)'H',
				23u => (uint)'I', 36u => (uint)'J', 37u => (uint)'K', 38u => (uint)'L',
				50u => (uint)'M', 49u => (uint)'N', 24u => (uint)'O', 25u => (uint)'P',
				16u => (uint)'Q', 19u => (uint)'R', 31u => (uint)'S', 20u => (uint)'T',
				22u => (uint)'U', 47u => (uint)'V', 17u => (uint)'W', 45u => (uint)'X',
				21u => (uint)'Y', 44u => (uint)'Z',
				11u => (uint)'0', 2u => (uint)'1', 3u => (uint)'2', 4u => (uint)'3',
				5u => (uint)'4', 6u => (uint)'5', 7u => (uint)'6', 8u => (uint)'7',
				9u => (uint)'8', 10u => (uint)'9',
				14u => VK_BACK,
				15u => VK_TAB,
				0x163u => VK_CLEAR,
				28u or 96u => VK_RETURN,
				1u => VK_ESCAPE,
				57u => VK_SPACE,
				104u => VK_PRIOR,
				109u => VK_NEXT,
				107u => VK_END,
				102u => VK_HOME,
				105u => VK_LEFT,
				103u => VK_UP,
				106u => VK_RIGHT,
				108u => VK_DOWN,
				110u => VK_INSERT,
				111u => VK_DELETE,
				0x161u => VK_SELECT,
				210u => VK_PRINT,
				134u => VK_EXECUTE,
				99u => VK_SNAPSHOT,
				138u => VK_HELP,
				119u => VK_PAUSE,
				58u => VK_CAPITAL,
				69u => VK_NUMLOCK,
				70u => VK_SCROLL,
				92u => VK_CONVERT,
				94u => VK_NONCONVERT,
				90u or 91u or 93u or 122u => VK_KANA,
				123u => VK_HANJA,
				0x175u => VK_MODECHANGE,
				29u => VK_LCONTROL,
				97u => VK_RCONTROL,
				42u => VK_LSHIFT,
				54u => VK_RSHIFT,
				56u => VK_LMENU,
				100u => VK_RMENU,
				125u => VK_LWIN,
				126u => VK_RWIN,
				127u or 139u => VK_APPS,
				142u => VK_SLEEP,
				59u => VK_F1, 60u => VK_F2, 61u => VK_F3, 62u => VK_F4,
				63u => VK_F5, 64u => VK_F6, 65u => VK_F7, 66u => VK_F8,
				67u => VK_F9, 68u => VK_F10, 87u => VK_F11, 88u => VK_F12,
				183u => VK_F13, 184u => VK_F14, 185u => VK_F15, 186u => VK_F16,
				187u => VK_F17, 188u => VK_F18, 189u => VK_F19, 190u => VK_F20,
				191u => VK_F21, 192u => VK_F22, 193u => VK_F23, 194u => VK_F24,
				82u => VK_NUMPAD0,
				79u => VK_NUMPAD1,
				80u => VK_NUMPAD2,
				81u => VK_NUMPAD3,
				75u => VK_NUMPAD4,
				76u => VK_NUMPAD5,
				77u => VK_NUMPAD6,
				71u => VK_NUMPAD7,
				72u => VK_NUMPAD8,
				73u => VK_NUMPAD9,
				55u => VK_MULTIPLY,
				78u => VK_ADD,
				95u or 121u => VK_SEPARATOR,
				74u => VK_SUBTRACT,
				83u => VK_DECIMAL,
				98u => VK_DIVIDE,
				39u => VK_OEM_1,
				13u or 117u => VK_OEM_PLUS,
				51u => VK_OEM_COMMA,
				12u => VK_OEM_MINUS,
				52u => VK_OEM_PERIOD,
				53u => VK_OEM_2,
				41u => VK_OEM_3,
				26u => VK_OEM_4,
				43u or 124u => VK_OEM_5,
				27u => VK_OEM_6,
				40u => VK_OEM_7,
				86u or 89u => VK_OEM_102,
				// Volume / media / browser / application keys (input-event-codes.h KEY_*)
				113u => VK_VOLUME_MUTE,
				114u => VK_VOLUME_DOWN,
				115u => VK_VOLUME_UP,
				164u or 200u or 201u or 207u => VK_MEDIA_PLAY_PAUSE,
				163u => VK_MEDIA_NEXT_TRACK,
				165u => VK_MEDIA_PREV_TRACK,
				166u => VK_MEDIA_STOP,
				128u => VK_BROWSER_STOP,
				158u => VK_BROWSER_BACK,
				159u => VK_BROWSER_FORWARD,
				173u => VK_BROWSER_REFRESH,
				217u => VK_BROWSER_SEARCH,
				156u or 364u => VK_BROWSER_FAVORITES,
				150u or 172u => VK_BROWSER_HOME,
				155u or 215u => VK_LAUNCH_MAIL,
				226u => VK_LAUNCH_MEDIA_SELECT,
				148u => VK_LAUNCH_APP1,
				149u => VK_LAUNCH_APP2,
				223u => VK_CANCEL,
				_ => 0u
			};
		}
	}
}
#endif
