#if LINUX
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// X11 keysym <-> Windows VK tables. These are fixed layout-independent mappings
	/// interpreted locally against the keymap supplied by keysharp-desktop.
	/// </summary>
	internal static partial class KeyCodes
	{
		internal static List<uint> VkToKeysyms(uint vk)
		{
			var list = new List<uint>(2);

			if (vk is >= (uint)'A' and <= (uint)'Z')
			{
				list.Add(vk);
				list.Add(vk + 32);
				return list;
			}

			if (vk is >= (uint)'0' and <= (uint)'9')
			{
				list.Add(vk);
				return list;
			}

			switch (vk)
			{
				case VK_SPACE: list.Add((uint)' '); return list;
				case VK_OEM_MINUS: list.Add((uint)'-'); list.Add((uint)'_'); return list;
				case VK_OEM_PLUS: list.Add((uint)'='); list.Add((uint)'+'); return list;
				case VK_OEM_1: list.Add((uint)';'); list.Add((uint)':'); return list;
				case VK_OEM_2: list.Add((uint)'/'); list.Add((uint)'?'); return list;
				case VK_OEM_3: list.Add((uint)'`'); list.Add((uint)'~'); return list;
				case VK_OEM_4: list.Add((uint)'['); list.Add((uint)'{'); return list;
				case VK_OEM_5: list.Add((uint)'\\'); list.Add((uint)'|'); return list;
				case VK_OEM_6: list.Add((uint)']'); list.Add((uint)'}'); return list;
				case VK_OEM_7: list.Add((uint)'\''); list.Add((uint)'"'); return list;
				case VK_OEM_COMMA: list.Add((uint)','); list.Add((uint)'<'); return list;
				case VK_OEM_PERIOD: list.Add((uint)'.'); list.Add((uint)'>'); return list;
			}

			switch (vk)
			{
				case VK_LSHIFT: list.Add(0xFFE1); break;
				case VK_RSHIFT: list.Add(0xFFE2); break;
				case VK_LCONTROL: list.Add(0xFFE3); break;
				case VK_RCONTROL: list.Add(0xFFE4); break;
				case VK_LMENU: list.Add(0xFFE9); break;
				case VK_RMENU: list.Add(0xFFEA); break;
				case VK_LWIN: list.Add(0xFFEB); break;
				case VK_RWIN: list.Add(0xFFEC); break;
				case VK_RETURN: list.Add(0xFF0D); break;
				case VK_TAB: list.Add(0xFF09); break;
				case VK_ESCAPE: list.Add(0xFF1B); break;
				case VK_BACK: list.Add(0xFF08); break;
				case VK_DELETE: list.Add(0xFFFF); break;
				case VK_INSERT: list.Add(0xFF63); break;
				case VK_HOME: list.Add(0xFF50); break;
				case VK_END: list.Add(0xFF57); break;
				case VK_PRIOR: list.Add(0xFF55); break;
				case VK_NEXT: list.Add(0xFF56); break;
				case VK_LEFT: list.Add(0xFF51); break;
				case VK_UP: list.Add(0xFF52); break;
				case VK_RIGHT: list.Add(0xFF53); break;
				case VK_DOWN: list.Add(0xFF54); break;
			}

			var keysym = VkToKeysym(vk);

			if (keysym != 0 && keysym <= uint.MaxValue && !list.Contains((uint)keysym))
				list.Add((uint)keysym);

			return list;
		}

		internal static ulong VkToKeysym(uint vk)
		{
			if (vk >= 'A' && vk <= 'Z')
				return vk;

			if (vk >= '0' && vk <= '9')
				return vk;

			return vk switch
			{
				VK_LSHIFT => KeysymFromName("Shift_L"),
				VK_RSHIFT => KeysymFromName("Shift_R"),
				VK_LCONTROL => KeysymFromName("Control_L"),
				VK_RCONTROL => KeysymFromName("Control_R"),
				VK_LMENU => KeysymFromName("Alt_L"),
				VK_RMENU => KeysymFromName("Alt_R"),
				VK_LWIN => KeysymFromName("Super_L"),
				VK_RWIN => KeysymFromName("Super_R"),
				VK_CAPITAL => KeysymFromName("Caps_Lock"),
				VK_NUMLOCK => KeysymFromName("Num_Lock"),
				VK_SCROLL => KeysymFromName("Scroll_Lock"),
				VK_BACK => KeysymFromName("BackSpace"),
				VK_DELETE => KeysymFromName("Delete"),
				VK_INSERT => KeysymFromName("Insert"),
				VK_HOME => KeysymFromName("Home"),
				VK_END => KeysymFromName("End"),
				VK_PRIOR => KeysymFromName("Prior"),
				VK_NEXT => KeysymFromName("Next"),
				VK_SNAPSHOT => KeysymFromName("Print"),
				VK_PAUSE => KeysymFromName("Pause"),
				VK_APPS => KeysymFromName("Menu"),
				VK_RETURN => KeysymFromName("Return"),
				VK_TAB => KeysymFromName("Tab"),
				VK_ESCAPE => KeysymFromName("Escape"),
				VK_SPACE => KeysymFromName("space"),
				VK_LEFT => KeysymFromName("Left"),
				VK_RIGHT => KeysymFromName("Right"),
				VK_UP => KeysymFromName("Up"),
				VK_DOWN => KeysymFromName("Down"),
				VK_OEM_MINUS => KeysymFromName("minus"),
				VK_OEM_PLUS => KeysymFromName("equal"),
				VK_OEM_4 => KeysymFromName("bracketleft"),
				VK_OEM_6 => KeysymFromName("bracketright"),
				VK_OEM_5 => KeysymFromName("backslash"),
				VK_OEM_1 => KeysymFromName("semicolon"),
				VK_OEM_7 => KeysymFromName("apostrophe"),
				VK_OEM_COMMA => KeysymFromName("comma"),
				VK_OEM_PERIOD => KeysymFromName("period"),
				VK_OEM_2 => KeysymFromName("slash"),
				VK_OEM_3 => KeysymFromName("grave"),
				_ when (vk >= VK_F1 && vk <= VK_F24) => KeysymFromName($"F{(int)(vk - VK_F1 + 1)}"),
				_ when (vk >= VK_NUMPAD0 && vk <= VK_NUMPAD9) => KeysymFromName($"KP_{(int)(vk - VK_NUMPAD0)}"),
				VK_DIVIDE => KeysymFromName("KP_Divide"),
				VK_MULTIPLY => KeysymFromName("KP_Multiply"),
				VK_SUBTRACT => KeysymFromName("KP_Subtract"),
				VK_ADD => KeysymFromName("KP_Add"),
				VK_DECIMAL => KeysymFromName("KP_Decimal"),
				VK_SEPARATOR => KeysymFromName("KP_Separator"),
				VK_CLEAR => KeysymFromName("Clear"),
				VK_CANCEL => KeysymFromName("Cancel"),
				VK_HELP => KeysymFromName("Help"),
				VK_SLEEP => KeysymFromName("XF86Sleep"),
				VK_VOLUME_MUTE => KeysymFromName("XF86AudioMute"),
				VK_VOLUME_DOWN => KeysymFromName("XF86AudioLowerVolume"),
				VK_VOLUME_UP => KeysymFromName("XF86AudioRaiseVolume"),
				VK_MEDIA_PLAY_PAUSE => KeysymFromName("XF86AudioPlay"),
				VK_MEDIA_STOP => KeysymFromName("XF86AudioStop"),
				VK_MEDIA_PREV_TRACK => KeysymFromName("XF86AudioPrev"),
				VK_MEDIA_NEXT_TRACK => KeysymFromName("XF86AudioNext"),
				VK_BROWSER_BACK => KeysymFromName("XF86Back"),
				VK_BROWSER_FORWARD => KeysymFromName("XF86Forward"),
				VK_BROWSER_STOP => KeysymFromName("XF86Stop"),
				VK_BROWSER_REFRESH => KeysymFromName("XF86Refresh"),
				VK_BROWSER_SEARCH => KeysymFromName("XF86Search"),
				VK_BROWSER_FAVORITES => KeysymFromName("XF86Favorites"),
				VK_BROWSER_HOME => KeysymFromName("XF86HomePage"),
				VK_LAUNCH_MAIL => KeysymFromName("XF86Mail"),
				VK_LAUNCH_MEDIA_SELECT => KeysymFromName("XF86AudioMedia"),
				VK_LAUNCH_APP1 => KeysymFromName("XF86Launch1"),
				VK_LAUNCH_APP2 => KeysymFromName("XF86Launch2"),
				_ => 0
			};
		}

		internal static uint KeysymToVk(ulong ks)
		{
			if ((ks >= 'A' && ks <= 'Z') || (ks >= 'a' && ks <= 'z'))
				return (uint)char.ToUpperInvariant((char)ks);

			if (ks >= '0' && ks <= '9')
				return (uint)ks;

			if (ks >= 0xFFB0 && ks <= 0xFFB9)
				return (uint)(VK_NUMPAD0 + (ks - 0xFFB0));

			if (ks >= 0xFFBE && ks <= 0xFFD5)
				return (uint)(VK_F1 + (ks - 0xFFBE));

			return ks switch
			{
				0xFFE1 => VK_LSHIFT,
				0xFFE2 => VK_RSHIFT,
				0xFFE3 => VK_LCONTROL,
				0xFFE4 => VK_RCONTROL,
				0xFFE9 => VK_LMENU,
				0xFFEA => VK_RMENU,
				0xFFEB => VK_LWIN,
				0xFFEC => VK_RWIN,
				0xFFE5 => VK_CAPITAL,
				0xFF7F => VK_NUMLOCK,
				0xFF14 => VK_SCROLL,
				0xFF08 => VK_BACK,
				0xFFFF => VK_DELETE,
				0xFF63 => VK_INSERT,
				0xFF50 => VK_HOME,
				0xFF57 => VK_END,
				0xFF55 => VK_PRIOR,
				0xFF56 => VK_NEXT,
				0xFF61 => VK_SNAPSHOT,
				0xFF13 => VK_PAUSE,
				0xFF67 => VK_APPS,
				0xFF0D => VK_RETURN,
				0xFF09 => VK_TAB,
				0xFF1B => VK_ESCAPE,
				0x0020 => VK_SPACE,
				0xFF51 => VK_LEFT,
				0xFF53 => VK_RIGHT,
				0xFF52 => VK_UP,
				0xFF54 => VK_DOWN,
				0x002D => VK_OEM_MINUS,
				0x003D => VK_OEM_PLUS,
				0x005B => VK_OEM_4,
				0x005D => VK_OEM_6,
				0x005C => VK_OEM_5,
				0x003B => VK_OEM_1,
				0x0027 => VK_OEM_7,
				0x002C => VK_OEM_COMMA,
				0x002E => VK_OEM_PERIOD,
				0x002F => VK_OEM_2,
				0x0060 => VK_OEM_3,
				0xFFAF => VK_DIVIDE,
				0xFFAA => VK_MULTIPLY,
				0xFFAD => VK_SUBTRACT,
				0xFFAB => VK_ADD,
				0xFFAE => VK_DECIMAL,
				0xFFAC => VK_SEPARATOR,
				0xFF0B => VK_CLEAR,
				0xFF69 => VK_CANCEL,
				0xFF6A => VK_HELP,
				0x1008FF2F => VK_SLEEP,
				0x1008FF12 => VK_VOLUME_MUTE,
				0x1008FF11 => VK_VOLUME_DOWN,
				0x1008FF13 => VK_VOLUME_UP,
				0x1008FF14 => VK_MEDIA_PLAY_PAUSE,
				0x1008FF15 => VK_MEDIA_STOP,
				0x1008FF16 => VK_MEDIA_PREV_TRACK,
				0x1008FF17 => VK_MEDIA_NEXT_TRACK,
				0x1008FF26 => VK_BROWSER_BACK,
				0x1008FF27 => VK_BROWSER_FORWARD,
				0x1008FF28 => VK_BROWSER_STOP,
				0x1008FF29 => VK_BROWSER_REFRESH,
				0x1008FF1B => VK_BROWSER_SEARCH,
				0x1008FF30 => VK_BROWSER_FAVORITES,
				0x1008FF18 => VK_BROWSER_HOME,
				0x1008FF19 => VK_LAUNCH_MAIL,
				0x1008FF32 => VK_LAUNCH_MEDIA_SELECT,
				0x1008FF41 => VK_LAUNCH_APP1,
				0x1008FF42 => VK_LAUNCH_APP2,
				_ => 0
			};
		}

		[DllImport("libxkbcommon.so.0", EntryPoint = "xkb_keysym_from_name")]
		private static extern uint KeysymFromName([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint flags = 0);

		internal static bool ResolveVkToXKeycode(uint vk, out uint xcode, bool returnSecondary = false)
		{
			xcode = 0;

			if (!Platform.Desktop.IsX11Available || vk == 0)
				return false;

			return TryMapVkToXKeycode(vk, out xcode, returnSecondary);
		}
	}
}
#endif
