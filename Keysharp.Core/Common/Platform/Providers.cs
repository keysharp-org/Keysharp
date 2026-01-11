namespace Keysharp.Core.Common.Platform
{
	internal class ControlProvider
	{
		internal ControlManagerBase Manager { get; } = new ControlManager();
	}

	internal static class DriveProvider
	{
		/// <summary>
		/// Creates a Drive for the current platform
		/// </summary>
		/// <param name="drive"></param>
		/// <returns></returns>
		internal static DriveBase CreateDrive(DriveInfo drive)
		{
#if WINDOWS
			return new Windows.Drive(drive);
#elif LINUX
			return new Linux.Drive(drive);
#else
			return null;
#endif
		}
	}

	internal class PlatformProvider
	{
		internal PlatformManagerBase Manager { get; } =
#if WINDOWS
			new PlatformManager()
#elif LINUX
			new Linux.PlatformManager()
#else
			null
#endif
		;
	}

	internal static class StatusBarProvider
	{
		/// <summary>
		/// Creates a StatusBar for the current platform
		/// </summary>
		/// <param name="drive"></param>
		/// <returns></returns>
		internal static StatusBarBase CreateStatusBar(nint hwnd)
		{
			return new StatusBar(hwnd);
		}
	}

	internal class WindowProvider
	{
		internal WindowManagerBase Manager { get; } = new WindowManager();
	}
}