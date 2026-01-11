#if LINUX

using VirtualKeys = Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Linux
{
	/// <summary>
	/// Concrete implementation of PlatformManager for the linux platfrom.
	/// </summary>
	internal class PlatformManager : PlatformManagerBase, IPlatformManager
	{
		private static readonly bool isGnome, isKde, isXfce, isMate, isCinnamon, isLxqt, isLxde;

		internal static bool IsGnome => isGnome;
		internal static bool IsKde => isKde;
		internal static bool IsXfce => isXfce;
		internal static bool IsMate => isMate;
		internal static bool IsCinnamon => isCinnamon;
		internal static bool IsLxqt => isLxqt;
		internal static bool IsLxde => isLxde;

		internal static bool IsX11Available => XDisplay.Default.Handle != 0;

		static PlatformManager()
		{
			var session = "echo $DESKTOP_SESSION".Bash().ToLower();

			if (session.Contains("gnome", StringComparison.OrdinalIgnoreCase))
				isGnome = true;
			else if (session.Contains("kde", StringComparison.OrdinalIgnoreCase))
				isKde = true;
			else if (session.Contains("xfce", StringComparison.OrdinalIgnoreCase))
				isXfce = true;
			else if (session.Contains("mate", StringComparison.OrdinalIgnoreCase))
				isMate = true;
			else if (session.Contains("cinnamon", StringComparison.OrdinalIgnoreCase))
				isCinnamon = true;
			else if (session.Contains("lxqt", StringComparison.OrdinalIgnoreCase))
				isLxqt = true;
			else if (session.Contains("lxde", StringComparison.OrdinalIgnoreCase))
				isLxde = true;
			else
				isGnome = true;//Assume Gnome if no other DE was found.
		}

		// Return the current xkb_keymap pointer as a stand-in for HKL.
		public static nint GetKeyboardLayout(uint idThread)
		{
			return LinuxKeyboardMouseSender.LinuxCharMapper.GetCurrentKeymapHandle();
		}

		public static int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl)
		{
			// Best-effort VK→char mapping for Linux; limited to US-style keys.
			// Similar to Windows ToUnicodeEx but without dead-key handling.
			if (pwszBuff == null || pwszBuff.Length == 0)
				return 0;

			bool shift =
				(lpKeyState.Length > VirtualKeys.VK_SHIFT && (lpKeyState[VirtualKeys.VK_SHIFT] & 0x80) != 0) ||
				(lpKeyState.Length > VirtualKeys.VK_LSHIFT && (lpKeyState[VirtualKeys.VK_LSHIFT] & 0x80) != 0) ||
				(lpKeyState.Length > VirtualKeys.VK_RSHIFT && (lpKeyState[VirtualKeys.VK_RSHIFT] & 0x80) != 0);

			bool caps = lpKeyState.Length > VirtualKeys.VK_CAPITAL && (lpKeyState[VirtualKeys.VK_CAPITAL] & 0x01) != 0;

			char ch = '\0';

			// Letters
			if (wVirtKey is >= (uint)'A' and <= (uint)'Z')
			{
				bool upper = shift ^ caps;
				ch = (char)(upper ? wVirtKey : (wVirtKey + 32)); // make lowercase by adding 32
			}
			// Digits and shifted symbols on the number row
			else if (wVirtKey is >= (uint)'0' and <= (uint)'9')
			{
				if (!shift)
				{
					ch = (char)wVirtKey;
				}
				else
				{
					// US keyboard shifted digits
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
				// Common punctuation / OEM keys (US layout)
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
			// Return the character code corresponding to the given virtual key.
			// Similar to Windows MapVirtualKeyEx with MAPVK_VK_TO_CHAR.
			char[] ch = new char[1];
			int result = ToUnicode(wVirtKey, 0, new byte[256], ch, 0, hkl);
			if (result > 0)
				return ch[0];
			else
				return 0;
		}

		public static bool SetDllDirectory(string path)
		{
			if (path == null)
			{
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", Script.TheScript.ldLibraryPath);
				return Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") == Script.TheScript.ldLibraryPath;
			}
			else
			{
				var append = path;
				var orig = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
				var newPath = "";

				if (orig != "")
				{
					append = ":" + append;
					newPath = orig + append;
					//newPath = "\"" + orig + "\"" + append;//Unsure if quotes are needed.
				}
				else
					newPath = append;

				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", newPath);
				return Environment.GetEnvironmentVariable("LD_LIBRARY_PATH").EndsWith(append);
			}
		}

		public static nint LoadLibrary(string path) => NativeLibrary.TryLoad(path, out var module) ? module : 0;

		public static uint CurrentThreadId() => (uint)Xlib.gettid();

		public static bool DestroyIcon(nint icon) => Xlib.GdipDisposeImage(icon) == 0;//Unsure if this works or is even needed on linux.

		public static bool ExitProgram(uint flags, uint reason)
		{
			var cmd = "";
			var force = false;

			//Taken from this article: https://fostips.com/log-out-command-linux-desktops/
			if ((flags & 4) == 4)
			{
				force = true;//Close all programs.
			}

			if (flags == 0)//Logoff.
			{
				if (isGnome)
				{
					if (force)
						cmd = "gnome-session-quit --force";
					else
						cmd = "gnome-session-quit";
				}
				else if (isKde)
				{
					if (force)
						cmd = "qdbus org.kde.ksmserver /KSMServer logout 0 0 2";
					else
						cmd = "qdbus org.kde.ksmserver /KSMServer logout 1 0 3";
				}
				else if (isXfce)
				{
					if (force)
						cmd = "xfce4-session-logout --fast";
					else
						cmd = "xfce4-session-logout";
				}
				else if (isMate)
				{
					if (force)
						cmd = "mate-session-save --logout --force";
					else
						cmd = "mate-session-save --logout";
				}
				else if (isCinnamon)
				{
					if (force)
						cmd = "cinnamon-session-quit --no-prompt";
					else
						cmd = "cinnamon-session-quit";
				}
				else if (IsLxqt)
				{
					if (force)
						KeysharpEnhancements.OutputDebugLine($"LXQT doesn't support forced logouts.");

					cmd = "lxqt-leave";
				}
				else if (IsLxde)
				{
					if (force)
						KeysharpEnhancements.OutputDebugLine($"LXDE doesn't support forced logouts.");

					cmd = "lxde-logout";
				}

				_ = cmd.Bash();
			}
			else if ((flags & 1) == 1)//Halt/shutdown.
			{
				if ((flags & 8) == 8)//Power down.
					_ = "shutdown now".Bash();
				else
					_ = "halt".Bash();
			}
			else if ((flags & 2) == 2)//Reboot.
			{
				if (force)
					_ = "reboot -f".Bash();
				else
					_ = "reboot".Bash();
			}
			else if ((flags & 8) == 8)//Shutdown.
			{
				_ = "shutdown now".Bash();
			}

			return true;
		}

		public static bool UnregisterHotKey(nint hWnd, uint id) => true;

		public static bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam) => true;

		public static bool PostMessage(nint hWnd, uint msg, uint wParam, uint lParam) => true;

		public static bool PostHotkeyMessage(nint hWnd, uint wParam, uint lParam) => true;

		public static bool RegisterHotKey(nint hWnd, uint id, KeyModifiers fsModifiers, uint vk) => true;

		public static bool GetCursorPos(out POINT lpPoint)
		{
			var pos = Forms.Mouse.Position;
			lpPoint = new POINT(pos.X.Ai(), pos.Y.Ai());
			return true;
		}
	}
}
#endif
