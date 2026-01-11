#if WINDOWS
namespace Keysharp.Core.Windows
{
	/// <summary>
	/// Concrete implementation of PlatformManager for the Windows platfrom.
	/// </summary>
	internal class PlatformManager : PlatformManagerBase, IPlatformManager
	{
		public static uint CurrentThreadId() => WindowsAPI.GetCurrentThreadId();

		public static bool DestroyIcon(nint icon) => WindowsAPI.DestroyIcon(icon);

		public static bool ExitProgram(uint flags, uint reason) => WindowsAPI.ExitWindowsEx(flags, reason);

		public static nint GetKeyboardLayout(uint idThread)=> WindowsAPI.GetKeyboardLayout(idThread);

		public static nint LoadLibrary(string path) => WindowsAPI.LoadLibrary(path);

		public static bool PostHotkeyMessage(nint hWnd, uint wParam, uint lParam) => WindowsAPI.PostMessage(hWnd, WindowsAPI.WM_HOTKEY, wParam, lParam);

		public static bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam) => WindowsAPI.PostMessage(hWnd, msg, wParam, lParam);

		public static bool PostMessage(nint hWnd, uint msg, uint wParam, uint lParam) => WindowsAPI.PostMessage(hWnd, msg, wParam, lParam);

		public static bool RegisterHotKey(nint hWnd, uint id, KeyModifiers fsModifiers, uint vk) => WindowsAPI.RegisterHotKey(hWnd, id, fsModifiers, vk);

		public static bool SetDllDirectory(string path) => WindowsAPI.SetDllDirectory(path);

		public static int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl)
			=> WindowsAPI.ToUnicodeEx(wVirtKey, wScanCode, lpKeyState, pwszBuff, pwszBuff.Length, wFlags, dwhkl);

		public static uint MapVirtualKeyToChar(uint wVirtKey, nint hkl) => WindowsAPI.MapVirtualKeyEx(wVirtKey, WindowsAPI.MAPVK_VK_TO_CHAR, hkl);

		public static bool UnregisterHotKey(nint hWnd, uint id) => WindowsAPI.UnregisterHotKey(hWnd, id);

		public static bool GetCursorPos(out POINT lpPoint) => WindowsAPI.GetCursorPos(out lpPoint);
	}
}

#endif