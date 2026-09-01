namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>Milliseconds since the last user (or synthetic) input, backing the A_TimeIdle family.
		/// Compile-time per-OS.</summary>
		internal static class Time
		{
#if WINDOWS
			public static bool TryGetIdleTime(out long milliseconds)
			{
				var lii = Os.Windows.LASTINPUTINFO.Default;

				if (Os.Windows.WindowsAPI.GetLastInputInfo(ref lii))
				{
					milliseconds = Environment.TickCount - lii.dwTime;
					return true;
				}

				milliseconds = 0L;
				return false;
			}
#elif OSX
			public static bool TryGetIdleTime(out long milliseconds) => Keysharp.Internals.Input.MacOS.MacNativeInput.TryGetIdleTime(out milliseconds);
#else
			public static bool TryGetIdleTime(out long milliseconds)
			{
				// xprintidle is an X11 utility. On native Wayland it either cannot answer or reports only XWayland
				// activity, so ask the active compositor backend instead. Keep the plain xprintidle invocation on
				// X11 so a missing/broken installation produces the normal Bash diagnostic rather than being hidden.
				if (!Desktop.IsWaylandSession)
				{
					if ("xprintidle".Bash(out var output) == 0)
					{
						milliseconds = output.Al();
						return true;
					}

					milliseconds = 0L;
					return false;
				}

				if (Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Current?.TryGetIdleTime(out milliseconds) == true)
					return true;

				return Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetIdleTime(out milliseconds);
			}
#endif
		}
	}
}
