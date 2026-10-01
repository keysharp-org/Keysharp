using Keysharp.Builtins;

namespace Keysharp.Internals.Window
{
	/// <summary>
	/// Coordinate translation between the active coord mode (Screen / Window / Client) and screen coordinates.
	/// Platform-neutral — it operates on the active window's geometry, not any OS API — so it lives in the window
	/// layer next to <see cref="WindowQuery"/> rather than under the Platform host (it dispatches nothing per-OS).
	/// </summary>
	internal static class WindowCoords
	{
		/// <summary>
		/// aX/aY are interpreted according to the current coord mode and converted to screen coordinates
		/// based on the active window's upper-left corner (or its client area).
		/// </summary>
		public static void CoordToScreen(ref int aX, ref int aY, CoordMode modeType)
		{
			var coordMode = ThreadAccessors.GetCoordMode(modeType);

			if (coordMode == CoordModeType.Screen)
				return;

			var activeWindow = WindowQuery.ActiveWindow;

			if (activeWindow.Handle != 0 && !activeWindow.IsIconic)
			{
				if (coordMode == CoordModeType.Window)
				{
					var loc = activeWindow.Location;
					aX += loc.X;
					aY += loc.Y;
				}
				else
				{
					var pt = activeWindow.ClientToScreen();
					aX += pt.X;
					aY += pt.Y;
				}
			}
		}
	}
}
