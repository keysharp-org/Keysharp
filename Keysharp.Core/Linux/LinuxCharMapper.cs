// Replace ONLY the LinuxCharMapper in your TextMapping file with this version.
// Key change: no P/Invoke to xkb_keysym_from_utf32. We compute keysyms ourselves.
// Rule used (X11 keysym encoding):
//   - ASCII (U+0020..U+007E) -> keysym == codepoint
//   - Latin-1 (U+00A0..U+00FF) -> keysym == codepoint
//   - Otherwise -> keysym == 0x01000000 | codepoint
// This avoids the missing symbol error while remaining compatible with libxkbcommon.

#if LINUX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using static Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Linux
{
	// WinForms Keys enum because Eto Keys enum is different and 
	// the current logic depends heavily on the WinForms one
	public enum Keys {
		None		= 0x00000000,
		LButton		= 0x00000001,
		RButton		= 0x00000002,
		Cancel		= 0x00000003,
		MButton		= 0x00000004,
		XButton1	= 0x00000005,
		XButton2	= 0x00000006,
		Back		= 0x00000008,
		Tab			= 0x00000009,
		LineFeed	= 0x0000000A,
		Clear		= 0x0000000C,
		Return		= 0x0000000D,
		Enter		= 0x0000000D,
		ShiftKey	= 0x00000010,
		ControlKey	= 0x00000011,
		Menu		= 0x00000012,
		Pause		= 0x00000013,
		CapsLock	= 0x00000014,
		Capital		= 0x00000014,
		KanaMode	= 0x00000015,
		HanguelMode	= 0x00000015,
		HangulMode	= 0x00000015,
		JunjaMode	= 0x00000017,
		FinalMode	= 0x00000018,
		KanjiMode	= 0x00000019,
		HanjaMode	= 0x00000019,
		Escape		= 0x0000001B,
		IMEConvert	= 0x0000001C,
		IMENonconvert	= 0x0000001D,
		IMEAceept	= 0x0000001E,
		IMEModeChange	= 0x0000001F,
		Space		= 0x00000020,
		PageUp		= 0x00000021,
		Prior		= 0x00000021,
		PageDown	= 0x00000022,
		Next		= 0x00000022,
		End			= 0x00000023,
		Home		= 0x00000024,
		Left		= 0x00000025,
		Up			= 0x00000026,
		Right		= 0x00000027,
		Down		= 0x00000028,
		Select		= 0x00000029,
		Print		= 0x0000002A,
		Execute		= 0x0000002B,
		PrintScreen	= 0x0000002C,
		Snapshot	= 0x0000002C,
		Insert		= 0x0000002D,
		Delete		= 0x0000002E,
		Help		= 0x0000002F,
		D0		= 0x00000030,
		D1		= 0x00000031,
		D2		= 0x00000032,
		D3		= 0x00000033,
		D4		= 0x00000034,
		D5		= 0x00000035,
		D6		= 0x00000036,
		D7		= 0x00000037,
		D8		= 0x00000038,
		D9		= 0x00000039,
		A		= 0x00000041,
		B		= 0x00000042,
		C		= 0x00000043,
		D		= 0x00000044,
		E		= 0x00000045,
		F		= 0x00000046,
		G		= 0x00000047,
		H		= 0x00000048,
		I		= 0x00000049,
		J		= 0x0000004A,
		K		= 0x0000004B,
		L		= 0x0000004C,
		M		= 0x0000004D,
		N		= 0x0000004E,
		O		= 0x0000004F,
		P		= 0x00000050,
		Q		= 0x00000051,
		R		= 0x00000052,
		S		= 0x00000053,
		T		= 0x00000054,
		U		= 0x00000055,
		V		= 0x00000056,
		W		= 0x00000057,
		X		= 0x00000058,
		Y		= 0x00000059,
		Z		= 0x0000005A,
		LWin		= 0x0000005B,
		RWin		= 0x0000005C,
		Apps		= 0x0000005D,
		NumPad0		= 0x00000060,
		NumPad1		= 0x00000061,
		NumPad2		= 0x00000062,
		NumPad3		= 0x00000063,
		NumPad4		= 0x00000064,
		NumPad5		= 0x00000065,
		NumPad6		= 0x00000066,
		NumPad7		= 0x00000067,
		NumPad8		= 0x00000068,
		NumPad9		= 0x00000069,
		Multiply	= 0x0000006A,
		Add			= 0x0000006B,
		Separator	= 0x0000006C,
		Subtract	= 0x0000006D,
		Decimal		= 0x0000006E,
		Divide		= 0x0000006F,
		F1		= 0x00000070,
		F2		= 0x00000071,
		F3		= 0x00000072,
		F4		= 0x00000073,
		F5		= 0x00000074,
		F6		= 0x00000075,
		F7		= 0x00000076,
		F8		= 0x00000077,
		F9		= 0x00000078,
		F10		= 0x00000079,
		F11		= 0x0000007A,
		F12		= 0x0000007B,
		F13		= 0x0000007C,
		F14		= 0x0000007D,
		F15		= 0x0000007E,
		F16		= 0x0000007F,
		F17		= 0x00000080,
		F18		= 0x00000081,
		F19		= 0x00000082,
		F20		= 0x00000083,
		F21		= 0x00000084,
		F22		= 0x00000085,
		F23		= 0x00000086,
		F24		= 0x00000087,
		NumLock		= 0x00000090,
		Scroll		= 0x00000091,
		LShiftKey	= 0x000000A0,
		RShiftKey	= 0x000000A1,
		LControlKey	= 0x000000A2,
		RControlKey	= 0x000000A3,
		LMenu		= 0x000000A4,
		RMenu		= 0x000000A5,
		BrowserBack	= 0x000000A6,
		BrowserForward	= 0x000000A7,
		BrowserRefresh	= 0x000000A8,
		BrowserStop	= 0x000000A9,
		BrowserSearch	= 0x000000AA,
		BrowserFavorites= 0x000000AB,
		BrowserHome	= 0x000000AC,
		VolumeMute	= 0x000000AD,
		VolumeDown	= 0x000000AE,
		VolumeUp	= 0x000000AF,
		MediaNextTrack	= 0x000000B0,
		MediaPreviousTrack= 0x000000B1,
		MediaStop	= 0x000000B2,
		MediaPlayPause	= 0x000000B3,
		LaunchMail	= 0x000000B4,
		SelectMedia	= 0x000000B5,
		LaunchApplication1= 0x000000B6,
		LaunchApplication2= 0x000000B7,
		OemSemicolon	= 0x000000BA,
		Oemplus		= 0x000000BB,
		Oemcomma	= 0x000000BC,
		OemMinus	= 0x000000BD,
		OemPeriod	= 0x000000BE,
		OemQuestion	= 0x000000BF,
		Oemtilde	= 0x000000C0,
		OemOpenBrackets	= 0x000000DB,
		OemPipe		= 0x000000DC,
		OemCloseBrackets= 0x000000DD,
		OemQuotes	= 0x000000DE,
		Oem8		= 0x000000DF,
		OemBackslash	= 0x000000E2,
		ProcessKey	= 0x000000E5,
		Attn		= 0x000000F6,
		Crsel		= 0x000000F7,
		Exsel		= 0x000000F8,
		EraseEof	= 0x000000F9,
		Play		= 0x000000FA,
		Zoom		= 0x000000FB,
		NoName		= 0x000000FC,
		Pa1		= 0x000000FD,
		OemClear	= 0x000000FE,
		KeyCode		= 0x0000FFFF,
		Shift		= 0x00010000,
		Control		= 0x00020000,
		Alt		= 0x00040000,
		Modifiers	= unchecked((int)0xFFFF0000),
		IMEAccept	= 0x0000001E,
		Oem1		= 0x000000BA,
		Oem102		= 0x000000E2,
		Oem2		= 0x000000BF,
		Oem3		= 0x000000C0,
		Oem4		= 0x000000DB,
		Oem5		= 0x000000DC,
		Oem6		= 0x000000DD,
		Oem7		= 0x000000DE,
		Packet		= 0x000000E7,
		Sleep		= 0x0000005F
	}
	partial class LinuxKeyboardMouseSender
	{
		internal static class LinuxCharMapper
		{
			private static readonly object _initLock = new();
			private static bool _initTried;
			private static bool _ready;

			// Optional layout preferences settable via ConfigureLayout(...)
			private static string? _prefRules, _prefModel, _prefLayout, _prefVariant, _prefOptions;

			// libxkbcommon handles
			private static IntPtr _ctx    = IntPtr.Zero;   // xkb_context*
			private static IntPtr _keymap = IntPtr.Zero;   // xkb_keymap*

			// Optional X11 (disabled by default)
			private static bool   _tryX11 = false;
			private static bool   _xThreadsInited = false;
			private static IntPtr _xDisplay = IntPtr.Zero;

			// Ranges & mods
			private static int  _minKeycode, _maxKeycode;
			private static uint _shiftIndex = uint.MaxValue;
			private static uint _mod5Index  = uint.MaxValue; // often AltGr
			private static bool _hasModsForLevel;

			// Cache: keysym -> (vk, needShift, needAltGr)
			private static readonly Dictionary<uint, (uint vk, bool s, bool g)> _cache = new(256);

			private static readonly Dictionary<string, uint> _xkbName2Vk = new(StringComparer.Ordinal)
			{
				["TLDE"] = VK_OEM_3,
				["AE01"] = (uint)'1', ["AE02"] = (uint)'2', ["AE03"] = (uint)'3', ["AE04"] = (uint)'4',
				["AE05"] = (uint)'5', ["AE06"] = (uint)'6', ["AE07"] = (uint)'7', ["AE08"] = (uint)'8',
				["AE09"] = (uint)'9', ["AE10"] = (uint)'0', ["AE11"] = VK_OEM_MINUS, ["AE12"] = VK_OEM_PLUS,

				["AD01"] = (uint)'Q', ["AD02"] = (uint)'W', ["AD03"] = (uint)'E', ["AD04"] = (uint)'R',
				["AD05"] = (uint)'T', ["AD06"] = (uint)'Y', ["AD07"] = (uint)'U', ["AD08"] = (uint)'I',
				["AD09"] = (uint)'O', ["AD10"] = (uint)'P', ["AD11"] = VK_OEM_4,  ["AD12"] = VK_OEM_6,

				["AC01"] = (uint)'A', ["AC02"] = (uint)'S', ["AC03"] = (uint)'D', ["AC04"] = (uint)'F',
				["AC05"] = (uint)'G', ["AC06"] = (uint)'H', ["AC07"] = (uint)'J', ["AC08"] = (uint)'K',
				["AC09"] = (uint)'L', ["AC10"] = VK_OEM_1,  ["AC11"] = VK_OEM_7,  ["BKSL"] = VK_OEM_5,

				["AB01"] = (uint)'Z', ["AB02"] = (uint)'X', ["AB03"] = (uint)'C', ["AB04"] = (uint)'V',
				["AB05"] = (uint)'B', ["AB06"] = (uint)'N', ["AB07"] = (uint)'M', ["AB08"] = VK_OEM_COMMA,
				["AB09"] = VK_OEM_PERIOD, ["AB10"] = VK_OEM_2,

				["SPCE"] = VK_SPACE,
			};

			public static bool TryMapRuneToKeystroke(Rune rune, out uint vk, out bool needShift, out bool needAltGr)
			{
				vk = 0; needShift = false; needAltGr = false;

				EnsureInitialized();
				if (!_ready)
				{
					// Fallback to US fast map if xkbcommon couldn't init
					return TextFastMapUS.TryMapCharToVkShift_US(rune, out vk, out needShift);
				}

				// Convert Rune to X11 keysym value without calling into libxkbcommon:
				// ASCII and Latin-1 (0xA0..0xFF) are direct; others use 0x01000000 | codepoint.
				uint ks = KeysymFromRune(rune);
				if (ks == 0)
					return false;

				// Cache?
				if (_cache.TryGetValue(ks, out var m))
				{
					(vk, needShift, needAltGr) = m;
					return true;
				}

				// Scan all keys & levels on layout 0
				for (uint key = (uint)_minKeycode; key <= (uint)_maxKeycode; key++)
				{
					var name = xkb_keymap_key_get_name_safe(_keymap, key);
					if (string.IsNullOrEmpty(name))
						continue;

					if (!_xkbName2Vk.TryGetValue(name!, out var candidateVk))
						continue;

					uint layout = 0;
					int levels = xkb_keymap_num_levels_for_key(_keymap, key, layout);
					if (levels <= 0) continue;

					for (uint level = 0; level < (uint)levels; level++)
					{
						if (!LevelHasKeysym(_keymap, key, layout, level, ks))
							continue;

						(bool s, bool g) = ResolveModsForLevel(key, layout, level);
						_cache[ks] = (candidateVk, s, g);
						vk = candidateVk; needShift = s; needAltGr = g;
						return true;
					}
				}

				return false;
			}

			/// <summary>
			/// Optional: call early to set rules/model/layout/variant/options (no X11 needed).
			/// </summary>
			public static void ConfigureLayout(string? rules = null, string? model = null, string? layout = null, string? variant = null, string? options = null)
			{
				lock (_initLock)
				{
					_prefRules   = rules;
					_prefModel   = model;
					_prefLayout  = layout;
					_prefVariant = variant;
					_prefOptions = options;

					if (_keymap != IntPtr.Zero) { xkb_keymap_unref(_keymap); _keymap = IntPtr.Zero; }
					if (_ctx    != IntPtr.Zero) { xkb_context_unref(_ctx);   _ctx    = IntPtr.Zero; }
					if (_xDisplay != IntPtr.Zero) { XCloseDisplay(_xDisplay); _xDisplay = IntPtr.Zero; }
					_cache.Clear();
					_ready = _initTried = false;
				}
			}

			/// <summary>
			/// Expose the current xkb_keymap pointer (or IntPtr.Zero if unavailable).
			/// Useful for callers that need a layout token similar to Windows' HKL.
			/// </summary>
			internal static nint GetCurrentKeymapHandle()
			{
				EnsureInitialized();
				return _keymap;
			}

			private static uint KeysymFromRune(Rune r)
			{
				uint cp = (uint)r.Value;

				// ASCII printable & common controls we might type:
				if (cp <= 0x7F)
					return cp;

				// Latin-1 range has direct keysyms (not 0x01000000-prefixed)
				if (cp >= 0x00A0 && cp <= 0x00FF)
					return cp;

				// Generic Unicode keysym encoding
				if (cp >= 0x0100 && cp <= 0x10FFFF)
					return 0x01000000u | cp;

				return 0;
			}

			private static void EnsureInitialized()
			{
				if (_ready || _initTried) return;

				lock (_initLock)
				{
					if (_ready || _initTried) return;
					_initTried = true;

					try
					{
						_tryX11 = string.Equals(Environment.GetEnvironmentVariable("KEYSHARP_XKB_USE_X11"), "1", StringComparison.Ordinal);

						_ctx = xkb_context_new(0);
						if (_ctx == IntPtr.Zero)
							return;

						if (BuildKeymapFromNames())
						{
							FinalizeKeymapCommon();
							_ready = true;
							return;
						}

						// Optional X11 fallback (OFF by default)
						if (_tryX11)
						{
							if (!_xThreadsInited)
							{
								try { XInitThreads(); _xThreadsInited = true; } catch { }
							}
							if (SetupKeymapFromX11())
							{
								FinalizeKeymapCommon();
								_ready = true;
								return;
							}
						}
					}
					catch
					{
						_ready = false;
					}
				}
			}

			private static bool BuildKeymapFromNames()
			{
				string? rules   = _prefRules   ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_RULES");
				string? model   = _prefModel   ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_MODEL");
				string? layout  = _prefLayout  ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT");
				string? variant = _prefVariant ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_VARIANT");
				string? options = _prefOptions ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_OPTIONS");

				var names = new xkb_rule_names();
				var pins = new List<IntPtr>();

				try
				{
					if (!string.IsNullOrEmpty(rules))   names.rules   = AllocUtf8(rules!, pins);
					if (!string.IsNullOrEmpty(model))   names.model   = AllocUtf8(model!, pins);
					if (!string.IsNullOrEmpty(layout))  names.layout  = AllocUtf8(layout!, pins);
					if (!string.IsNullOrEmpty(variant)) names.variant = AllocUtf8(variant!, pins);
					if (!string.IsNullOrEmpty(options)) names.options = AllocUtf8(options!, pins);

					_keymap = xkb_keymap_new_from_names(_ctx, ref names, 0);
					return _keymap != IntPtr.Zero;
				}
				finally
				{
					foreach (var p in pins) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
				}
			}

			private static bool SetupKeymapFromX11()
			{
				try
				{
					// Best-effort; wrapped to avoid the glibc assert crash. Only used if KEYSHARP_XKB_USE_X11=1
					_ = xkb_x11_setup_xkb_extension(_xDisplay, 1, 0, 0, out _, out _, out _, out _);

					int deviceId = xkb_x11_get_core_keyboard_device_id(_xDisplay);
					if (deviceId <= 0)
						return false;

					_keymap = xkb_x11_keymap_new_from_device(_ctx, _xDisplay, deviceId, 0);
					return _keymap != IntPtr.Zero;
				}
				catch
				{
					return false;
				}
			}

			private static void FinalizeKeymapCommon()
			{
				_minKeycode = xkb_keymap_min_keycode(_keymap);
				_maxKeycode = xkb_keymap_max_keycode(_keymap);

				_shiftIndex = xkb_keymap_mod_get_index(_keymap, "Shift");

				_mod5Index = xkb_keymap_mod_get_index(_keymap, "Mod5");
				if (_mod5Index == uint.MaxValue)
				{
					_mod5Index = xkb_keymap_mod_get_index(_keymap, "AltGr");
					if (_mod5Index == uint.MaxValue)
						_mod5Index = xkb_keymap_mod_get_index(_keymap, "ISO_Level3_Shift");
				}

				try
				{
					xkb_keymap_key_get_mods_for_level(_keymap, 0, 0, 0, IntPtr.Zero, UIntPtr.Zero);
					_hasModsForLevel = true;
				}
				catch (EntryPointNotFoundException)
				{
					_hasModsForLevel = false;
				}
			}

			private static (bool needShift, bool needAltGr) ResolveModsForLevel(uint key, uint layout, uint level)
			{
				if (_hasModsForLevel)
				{
					Span<ulong> buf = stackalloc ulong[4];
					int got;
					unsafe
					{
						fixed (ulong* p = buf)
							got = xkb_keymap_key_get_mods_for_level(_keymap, key, layout, level, (IntPtr)p, (UIntPtr)buf.Length);
					}
					if (got > 0)
					{
						ulong mask = buf[0];
						bool s = (_shiftIndex != uint.MaxValue) && ((mask & (1UL << (int)_shiftIndex)) != 0);
						bool g = (_mod5Index  != uint.MaxValue) && ((mask & (1UL << (int)_mod5Index))  != 0);
						return (s, g);
					}
				}

				// Heuristic when the function isn't available
				return level switch
				{
					0 => (false, false),
					1 => (true,  false),
					2 => (false, true ),
					3 => (true,  true ),
					_ => (false, false)
				};
			}

			private static bool LevelHasKeysym(IntPtr keymap, uint key, uint layout, uint level, uint target)
			{
				IntPtr symsPtr;
				int count = xkb_keymap_key_get_syms_by_level(keymap, key, layout, level, out symsPtr);
				if (count <= 0 || symsPtr == IntPtr.Zero)
					return false;

				unsafe
				{
					var syms = (uint*)symsPtr;
					for (int i = 0; i < count; i++)
						if (syms[i] == target)
							return true;
				}
				return false;
			}

			private static unsafe string? xkb_keymap_key_get_name_safe(IntPtr keymap, uint key)
			{
				var ptr = xkb_keymap_key_get_name(keymap, key);
				return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
			}

			private static IntPtr AllocUtf8(string s, List<IntPtr> pins)
			{
				var bytes = Encoding.UTF8.GetBytes(s + "\0");
				var mem = Marshal.AllocHGlobal(bytes.Length);
				Marshal.Copy(bytes, 0, mem, bytes.Length);
				pins.Add(mem);
				return mem;
			}

			// ------------ P/Invoke (no xkb_keysym_from_utf32 here) ------------
			private const string LibXkbCommon    = "libxkbcommon.so.0";
			private const string LibXkbCommonX11 = "libxkbcommon-x11.so.0";
			private const string LibX11          = "libX11.so.6";

			[StructLayout(LayoutKind.Sequential)]
			private struct xkb_rule_names
			{
				public IntPtr rules, model, layout, variant, options;
			}

			[DllImport(LibXkbCommon)] private static extern IntPtr xkb_context_new(uint flags);
			[DllImport(LibXkbCommon)] private static extern void   xkb_context_unref(IntPtr ctx);

			[DllImport(LibXkbCommon)] private static extern IntPtr xkb_keymap_new_from_names(IntPtr ctx, ref xkb_rule_names names, uint flags);
			[DllImport(LibXkbCommon)] private static extern void   xkb_keymap_unref(IntPtr keymap);
			[DllImport(LibXkbCommon)] private static extern int    xkb_keymap_min_keycode(IntPtr keymap);
			[DllImport(LibXkbCommon)] private static extern int    xkb_keymap_max_keycode(IntPtr keymap);
			[DllImport(LibXkbCommon)] private static extern uint   xkb_keymap_mod_get_index(IntPtr keymap, string name);
			[DllImport(LibXkbCommon)] private static extern int    xkb_keymap_num_levels_for_key(IntPtr keymap, uint key, uint layout);
			[DllImport(LibXkbCommon)] private static extern int    xkb_keymap_key_get_syms_by_level(IntPtr keymap, uint key, uint layout, uint level, out IntPtr syms_out);
			[DllImport(LibXkbCommon, EntryPoint = "xkb_keymap_key_get_mods_for_level")]
			private static extern int xkb_keymap_key_get_mods_for_level(IntPtr keymap, uint key, uint layout, uint level, IntPtr masks_out, UIntPtr masks_out_size);
			[DllImport(LibXkbCommon)] private static extern IntPtr xkb_keymap_key_get_name(IntPtr keymap, uint key);

			// Optional X11 path
			[DllImport(LibX11)] private static extern int    XInitThreads();
			[DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
			[DllImport(LibX11)] private static extern int    XCloseDisplay(IntPtr display);

			[DllImport(LibXkbCommonX11)] private static extern int    xkb_x11_setup_xkb_extension(IntPtr dpy, int major, int minor, int flags, out int major_out, out int minor_out, out int base_event_out, out int base_error_out);
			[DllImport(LibXkbCommonX11)] private static extern int    xkb_x11_get_core_keyboard_device_id(IntPtr dpy);
			[DllImport(LibXkbCommonX11)] private static extern IntPtr xkb_x11_keymap_new_from_device(IntPtr ctx, IntPtr dpy, int device_id, uint flags);

			// Cleanup
			static LinuxCharMapper()
			{
				AppDomain.CurrentDomain.ProcessExit += (_, __) =>
				{
					if (_keymap   != IntPtr.Zero) { xkb_keymap_unref(_keymap); _keymap = IntPtr.Zero; }
					if (_ctx      != IntPtr.Zero) { xkb_context_unref(_ctx);   _ctx    = IntPtr.Zero; }
					if (_xDisplay != IntPtr.Zero) { XCloseDisplay(_xDisplay);  _xDisplay = IntPtr.Zero; }
				};
			}
		}

		private static class TextFastMapUS
		{
			public static bool TryMapCharToVkShift_US(Rune rune, out uint vk, out bool needShift)
			{
				vk = 0; needShift = false;
				if (!rune.IsAscii) return false;
				char ch = (char)rune.Value;

				switch (ch)
				{
					case ' ': vk = VK_SPACE; return true;
					case '\t': vk = VK_TAB; return true;
					case '\r':
					case '\n': vk = VK_RETURN; return true;
					case '\b': vk = VK_BACK; return true;
				}
				if (ch is >= 'a' and <= 'z') { vk = (uint)char.ToUpperInvariant(ch); return true; }
				if (ch is >= 'A' and <= 'Z') { vk = (uint)ch; needShift = true; return true; }
				if (ch is >= '0' and <= '9') { vk = (uint)ch; return true; }

				switch (ch)
				{
					case '!': vk=(uint)'1'; needShift=true; return true;
					case '@': vk=(uint)'2'; needShift=true; return true;
					case '#': vk=(uint)'3'; needShift=true; return true;
					case '$': vk=(uint)'4'; needShift=true; return true;
					case '%': vk=(uint)'5'; needShift=true; return true;
					case '^': vk=(uint)'6'; needShift=true; return true;
					case '&': vk=(uint)'7'; needShift=true; return true;
					case '*': vk=(uint)'8'; needShift=true; return true;
					case '(': vk=(uint)'9'; needShift=true; return true;
					case ')': vk=(uint)'0'; needShift=true; return true;

					case '-': vk=VK_OEM_MINUS; return true;
					case '_': vk=VK_OEM_MINUS; needShift=true; return true;

					case '=': vk=VK_OEM_PLUS; return true;
					case '+': vk=VK_OEM_PLUS; needShift=true; return true;

					case '[': vk=VK_OEM_4; return true;
					case '{': vk=VK_OEM_4; needShift=true; return true;

					case ']': vk=VK_OEM_6; return true;
					case '}': vk=VK_OEM_6; needShift=true; return true;

					case '\\': vk=VK_OEM_5; return true;
					case '|':  vk=VK_OEM_5; needShift=true; return true;

					case ';': vk=VK_OEM_1; return true;
					case ':': vk=VK_OEM_1; needShift=true; return true;

					case '\'': vk=VK_OEM_7; return true;
					case '"':  vk=VK_OEM_7; needShift=true; return true;

					case ',': vk=VK_OEM_COMMA; return true;
					case '<': vk=VK_OEM_COMMA; needShift=true; return true;

					case '.': vk=VK_OEM_PERIOD; return true;
					case '>': vk=VK_OEM_PERIOD; needShift=true; return true;

					case '/': vk=VK_OEM_2; return true;
					case '?': vk=VK_OEM_2; needShift=true; return true;

					case '`': vk=VK_OEM_3; return true;
					case '~': vk=VK_OEM_3; needShift=true; return true;
				}
				return false;
			}
		}
	}
}
#endif
