namespace Keysharp.Core.Common.Platform
{
	internal interface IPlatformManager
	{
		/// <summary>
		/// aX and aY are interpreted according to the current coord mode.  If necessary, they are converted to
		/// screen coordinates based on the position of the active window's upper-left corner (or its client area).
		/// </summary>
		/// <param name="aX"></param>
		/// <param name="aY"></param>
		/// <param name="aWhichMode"></param>
		internal static abstract void CoordToScreen(ref int aX, ref int aY, CoordMode modeType);

		/// <summary>
		/// Converts screen coordinates to the current coord mode. Inverse of CoordToScreen.
		/// </summary>
		/// <param name="aX"></param>
		/// <param name="aY"></param>
		/// <param name="modeType"></param>
		internal static abstract void ScreenToCoord(ref int aX, ref int aY, CoordMode modeType);

		internal static abstract uint CurrentThreadId();

		internal static abstract bool DestroyIcon(nint icon);

		internal static abstract bool ExitProgram(uint flags, uint reason);

		internal static abstract nint GetKeyboardLayout(uint idThread);

		internal static abstract nint LoadLibrary(string path);

		internal static abstract bool PostHotkeyMessage(nint hWnd, uint wParam, uint lParam);

		internal static abstract bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

		internal static abstract bool PostMessage(nint hWnd, uint msg, uint wParam, uint lParam);

		internal static abstract bool RegisterHotKey(nint hWnd, uint id, KeyModifiers fsModifiers, uint vk);

		internal static abstract bool SetDllDirectory(string path);

		internal static abstract int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, uint wFlags, nint dwhkl);

		internal static abstract uint MapVirtualKeyToChar(uint wVirtKey, nint hkl);

		internal static abstract bool UnregisterHotKey(nint hWnd, uint id);

		internal static abstract bool GetCursorPos(out POINT lpPoint);
	}

	internal abstract class PlatformManagerBase
	{
		/// <summary>
		/// aX and aY are interpreted according to the current coord mode.  If necessary, they are converted to
		/// screen coordinates based on the position of the active window's upper-left corner (or its client area).
		/// </summary>
		/// <param name="aX"></param>
		/// <param name="aY"></param>
		/// <param name="aWhichMode"></param>
		public static void CoordToScreen(ref int aX, ref int aY, CoordMode modeType)
		{
			var script = Script.TheScript;
			var coordMode = ThreadAccessors.GetCoordMode(modeType);

			if (coordMode == CoordModeType.Screen)
				return;

			var activeWindow = WindowManager.ActiveWindow;

			if (activeWindow.Handle != 0 && !activeWindow.IsIconic)
			{
				if (coordMode == CoordModeType.Window)
				{
					var rect = activeWindow.Location;
					aX += rect.Left;
					aY += rect.Top;
				}
				else // (coord_mode == CoordModeType.Window.Client)
				{
					var pt = activeWindow.ClientToScreen();
					aX += pt.X;
					aY += pt.Y;
				}
			}

			//else no active window per se, so don't convert the coordinates.  Leave them as-is as desired
			// by the caller.  More details:
			// Revert to screen coordinates if the foreground window is minimized.  Although it might be
			// impossible for a visible window to be both foreground and minimized, it seems that hidden
			// windows -- such as the script's own main window when activated for the purpose of showing
			// a popup menu -- can be foreground while simultaneously being minimized.  This fixes an
			// issue where the mouse will move to the upper-left corner of the screen rather than the
			// intended coordinates (v1.0.17).
		}

		/// <summary>
		/// Converts screen coordinates to the current coord mode. Inverse of CoordToScreen.
		/// </summary>
		/// <param name="aX"></param>
		/// <param name="aY"></param>
		/// <param name="modeType"></param>
		public static void ScreenToCoord(ref int aX, ref int aY, CoordMode modeType)
		{
			var coordMode = ThreadAccessors.GetCoordMode(modeType);

			if (coordMode == CoordModeType.Screen)
				return;

			var activeWindow = WindowManager.ActiveWindow;

			if (activeWindow.Handle != 0 && !activeWindow.IsIconic)
			{
				if (coordMode == CoordModeType.Window)
				{
					var rect = activeWindow.Location;
					aX -= rect.Left;
					aY -= rect.Top;
				}
				else // CoordModeType.Window.Client
				{
					var pt = activeWindow.ClientToScreen();
					aX -= pt.X;
					aY -= pt.Y;
				}
			}
		}
	}
}
