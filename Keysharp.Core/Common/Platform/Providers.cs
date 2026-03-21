namespace Keysharp.Core.Common.Platform;

internal class ControlProvider
{
	internal ControlManagerBase Manager { get; } =
#if WINDOWS
		new ControlManager()
#elif LINUX
		new Linux.ControlManager()
#else
		null
#endif
	;
}

internal static class DriveProvider
{
	/// <summary>
	/// Creates a Drive for the current platform
	/// </summary>
	/// <param name="drive"></param>
	/// <returns></returns>
	internal static DriveBase CreateDrive(DriveInfo drive) =>
#if WINDOWS
		new Windows.Drive(drive);
#elif LINUX
		return new Linux.Drive(drive);
#else
		return null;
#endif

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
	internal static StatusBarBase CreateStatusBar(nint hwnd) =>
#if WINDOWS
		new Windows.StatusBar(hwnd);
#elif LINUX
		return new Linux.StatusBar(hwnd);
#else
		return null;
#endif

}

internal class WindowProvider
{
	internal WindowManagerBase Manager { get; } =
#if WINDOWS
		new WindowManager()
#elif LINUX
		new Linux.WindowManager()
#else
		null
#endif
	;
}