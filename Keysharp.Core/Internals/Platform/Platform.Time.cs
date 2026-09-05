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
				=> Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetIdleTime(out milliseconds);
#endif
		}
	}
}
