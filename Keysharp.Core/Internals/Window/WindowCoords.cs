using Keysharp.Builtins;

namespace Keysharp.Internals.Window
{
	internal static class WindowCoords
	{
		public static bool CoordToScreen(ref int x, ref int y, CoordMode category, WindowInfoBase activeWindow = null)
		{
			var tv = Script.TheScript.Threads.CurrentThread;
			var scope = tv.CurrentCoordWindow;
			var mode = scope != null ? scope.CoordModeFor(category) : tv.configData.CoordModeFor(category);

			if (mode == CoordModeType.Screen)
				return true;

			if (scope == null)
			{
				CoordToScreen(ref x, ref y, mode, activeWindow ?? WindowQuery.ActiveWindow);
				return true;
			}

			try
			{
				if (!TryGetReferenceWindow(scope, mode, out var window))
					return false;

				if (!TryGetCoordBounds(window, mode == CoordModeType.Client, out var bounds))
				{
					if (!Platform.Window.GetExists(scope.Handle) || Platform.Window.GetWindowState(scope.Handle) == FormWindowState.Minimized)
						_ = Errors.TargetErrorOccurred("The coordinate window does not exist or is minimized.", (long)scope.Handle, null);
#if WINDOWS
					else if (Platform.Window is WindowsWindow)
						_ = Errors.OSErrorOccurredWithMessage("Could not retrieve the coordinate window's bounds.");
#endif
					else
						_ = Errors.UnsupportedErrorOccurred("The coordinate window's position is not available on this platform.");
					return false;
				}

				x += bounds.X;
				y += bounds.Y;
				return true;
			}
			finally { GC.KeepAlive(scope); }
		}

		internal static bool TryGetReferenceWindow(Ks.CoordWindow scope, CoordModeType mode, out WindowInfoBase window)
		{
			window = null;

			if (mode == CoordModeType.Screen)
				return true;

			window = scope.IsReferenceWindowAlive ? WindowQuery.CreateWindow(scope.Handle) : null;

			if (window == null || !window.Exists || window.IsIconic)
			{
				_ = Errors.TargetErrorOccurred("The coordinate window does not exist or is minimized.", (long)scope.Handle, null);
				return false;
			}

			return true;
		}

		internal static bool TryGetCoordBounds(WindowInfoBase window, bool client, out Rectangle bounds)
		{
#if WINDOWS
			if (Platform.Window is WindowsWindow native)
				return native.TryGetCoordBounds(window.Handle, client, out bounds);
#endif
			return window.TryGetBounds(client, out bounds);
		}

		// Uses an explicit reference for conversion on the native hook thread.
		internal static void CoordToScreen(ref int x, ref int y, CoordModeType mode, WindowInfoBase window)
		{
			if (mode == CoordModeType.Screen || window.Handle == 0 || window.IsIconic)
				return;

			if (mode == CoordModeType.Window)
			{
				var point = window.Location;
				x += point.X;
				y += point.Y;
			}
			else
			{
				var point = window.ClientToScreen();
				x += point.X;
				y += point.Y;
			}
		}
	}
}
