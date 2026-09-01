using System.Text;
using Keysharp.Internals.Input.Keyboard;

namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>
		/// VK ⇄ char / scancode translation and layout handle. These helpers are platform-specific but
		/// compile-time (no runtime session fork), with Linux mapping consulting the active xkb / input service
		/// layout via KeyCodes.
		/// </summary>
		internal static class Keys
		{
#if WINDOWS
			public static nint GetKeyboardLayout(uint? idThread = null) => Os.Windows.WindowsAPI.GetKeyboardLayout(idThread ?? Platform.Window.GetFocusedControlThread());

			public static string GetKeyboardLayoutName()
			{
				var layout = GetKeyboardLayout();
				var klid = GetKeyboardLayoutKlid(layout);

				if (klid == "")
					klid = unchecked((uint)layout.ToInt64()).ToString("X8", CultureInfo.InvariantCulture);

				var layoutText = GetKeyboardLayoutText(klid);
				return layoutText == "" ? klid : $"{klid}:{layoutText}";
			}

			public static nint ResolveKeyboardLayout(string layout)
			{
				if (string.IsNullOrWhiteSpace(layout))
					return GetKeyboardLayout();

				var klid = layout.Split(':', 2)[0].Trim();

				if (klid.Length == 8 && uint.TryParse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
				{
					var hkl = Os.Windows.WindowsAPI.LoadKeyboardLayout(klid, 0x00000080); // KLF_NOTELLSHELL

					if (hkl != 0)
						return hkl;
				}

				return GetKeyboardLayout();
			}

			private static string GetKeyboardLayoutKlid(nint layout)
			{
				// ActivateKeyboardLayout returns the previous HKL truncated to int; reinterpret the bits as
				// uint and zero-extend so IME HKLs (high bit set, e.g. 0xE0010411) reconstruct correctly and
				// the finally-block restore below targets the real handle.
				var oldLayout = new nint(unchecked((uint)Os.Windows.WindowsAPI.ActivateKeyboardLayout(layout, 0)));

				try
				{
					var chars = new char[16];

					if (!Os.Windows.WindowsAPI.GetKeyboardLayoutName(chars))
						return "";

					var len = System.Array.IndexOf(chars, '\0');
					return len <= 0 ? new string(chars).TrimEnd('\0') : new string(chars, 0, len);
				}
				finally
				{
					if (oldLayout != 0 && oldLayout != layout)
						_ = Os.Windows.WindowsAPI.ActivateKeyboardLayout(oldLayout, 0);
				}
			}

			private static string GetKeyboardLayoutText(string klid)
			{
				try
				{
					using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{klid}");
					return key?.GetValue("Layout Text") as string ?? "";
				}
				catch
				{
					return "";
				}
			}

			public static int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl)
				=> Os.Windows.WindowsAPI.ToUnicodeEx(wVirtKey, wScanCode, lpKeyState, pwszBuff, pwszBuff.Length, wFlags, dwhkl);

			public static uint MapVirtualKeyToChar(uint wVirtKey, nint hkl)
				=> Os.Windows.WindowsAPI.MapVirtualKeyEx(wVirtKey, Os.Windows.WindowsAPI.MAPVK_VK_TO_CHAR, hkl);
#else
			// Return the current xkb_keymap pointer as a stand-in for HKL.
			public static nint GetKeyboardLayout(uint? idThread = null) => KeyCodes.GetCurrentKeymapHandle();

			public static string GetKeyboardLayoutName() => KeyCodes.GetCurrentKeymapName();

			public static nint ResolveKeyboardLayout(string layout) => KeyCodes.ResolveKeyboardLayout(layout);

			private static void DeriveModifierState(byte[] lpKeyState, out bool shift, out bool caps, out bool altGr)
			{
				shift =
					(lpKeyState.Length > VirtualKeys.VK_SHIFT && (lpKeyState[VirtualKeys.VK_SHIFT] & 0x80) != 0) ||
					(lpKeyState.Length > VirtualKeys.VK_LSHIFT && (lpKeyState[VirtualKeys.VK_LSHIFT] & 0x80) != 0) ||
					(lpKeyState.Length > VirtualKeys.VK_RSHIFT && (lpKeyState[VirtualKeys.VK_RSHIFT] & 0x80) != 0);

				caps = lpKeyState.Length > VirtualKeys.VK_CAPITAL && (lpKeyState[VirtualKeys.VK_CAPITAL] & 0x01) != 0;

				altGr =
					(lpKeyState.Length > VirtualKeys.VK_RMENU && (lpKeyState[VirtualKeys.VK_RMENU] & 0x80) != 0) ||
					(lpKeyState.Length > VirtualKeys.VK_RCONTROL && (lpKeyState[VirtualKeys.VK_RCONTROL] & 0x80) != 0
					 && lpKeyState.Length > VirtualKeys.VK_LMENU && (lpKeyState[VirtualKeys.VK_LMENU] & 0x80) != 0);
			}

			/// <summary>
			/// Dead-key-aware counterpart to <see cref="ToUnicode"/>, used by the input-collection hook so
			/// that dead keys compose on Linux/macOS the way they already do on Windows. Routes through the
			/// active layout's stateful translator and falls back to the stateless <see cref="ToUnicode"/>.
			/// Return convention matches Windows ToUnicode: &gt;0 chars, 0 no text, &lt;0 dead key.
			/// </summary>
			public static int ToUnicodeWithDeadKeys(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl)
			{
				if (pwszBuff == null || pwszBuff.Length == 0)
					return 0;

				DeriveModifierState(lpKeyState, out var shift, out var caps, out var altGr);
				var n = KeyCodes.TranslateKeyWithDeadKeys(wVirtKey, shift, altGr, caps, pwszBuff.AsSpan());

				if (n != KeyCodes.TranslateNotHandled)
					return n;

				return ToUnicode(wVirtKey, wScanCode, lpKeyState, pwszBuff, wFlags, dwhkl);
			}

			public static int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl)
			{
				// Best-effort VK->char mapping for Linux; limited to US-style keys. Similar to Windows
				// ToUnicodeEx but without dead-key handling.
				if (pwszBuff == null || pwszBuff.Length == 0)
					return 0;

				DeriveModifierState(lpKeyState, out var shift, out var caps, out var altGr);

				// Prefer the active OS keyboard layout (handles non-US layouts and non-ASCII chars such as "ä");
				// fall back to the hardcoded US table below if the platform's char mapper can't translate this key.
				if (KeyCodes.TryMapKeystrokeToRune(wVirtKey, shift, altGr, out var mappedRune))
				{
					if (caps && Rune.IsLetter(mappedRune))
					{
						var upper = Rune.ToUpperInvariant(mappedRune);
						var lower = Rune.ToLowerInvariant(mappedRune);
						mappedRune = mappedRune == upper ? lower : upper;
					}

					var written = mappedRune.EncodeToUtf16(pwszBuff);
					return written;
				}

				char ch = '\0';

				if (wVirtKey is >= (uint)'A' and <= (uint)'Z')
				{
					bool upper = shift ^ caps;
					ch = (char)(upper ? wVirtKey : (wVirtKey + 32)); // make lowercase by adding 32
				}
				else if (wVirtKey is >= (uint)'0' and <= (uint)'9')
				{
					if (!shift)
					{
						ch = (char)wVirtKey;
					}
					else
					{
						ch = wVirtKey switch
						{
							'1' => '!',
							'2' => '@',
							'3' => '#',
							'4' => '$',
							'5' => '%',
							'6' => '^',
							'7' => '&',
							'8' => '*',
							'9' => '(',
							'0' => ')',
							_ => '\0'
						};
					}
				}
				else
				{
					ch = wVirtKey switch
					{
						VirtualKeys.VK_SPACE => ' ',
						VirtualKeys.VK_OEM_MINUS => shift ? '_' : '-',
						VirtualKeys.VK_OEM_PLUS => shift ? '+' : '=',
						VirtualKeys.VK_OEM_1 => shift ? ':' : ';',
						VirtualKeys.VK_OEM_2 => shift ? '?' : '/',
						VirtualKeys.VK_OEM_3 => shift ? '~' : '`',
						VirtualKeys.VK_OEM_4 => shift ? '{' : '[',
						VirtualKeys.VK_OEM_5 => shift ? '|' : '\\',
						VirtualKeys.VK_OEM_6 => shift ? '}' : ']',
						VirtualKeys.VK_OEM_7 => shift ? '"' : '\'',
						VirtualKeys.VK_OEM_COMMA => shift ? '<' : ',',
						VirtualKeys.VK_OEM_PERIOD => shift ? '>' : '.',
						_ => '\0'
					};
				}

				if (ch == '\0')
					return 0;

				pwszBuff[0] = ch;
				return 1;
			}

			public static uint MapVirtualKeyToChar(uint wVirtKey, nint hkl)
			{
				char[] ch = new char[1];
				int result = ToUnicode(wVirtKey, 0, new byte[256], ch, 0, hkl);

				if (result > 0)
					return ch[0];

				return 0;
			}
#endif
		}
	}
}
