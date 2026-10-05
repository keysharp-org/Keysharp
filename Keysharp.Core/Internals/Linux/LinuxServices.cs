#if LINUX
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Os;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Internals.Linux
{
	/// <summary>Native service sessions belonging to one script. Construction performs no IPC.</summary>
	internal sealed class LinuxServices : IDisposable
	{
		internal KeysharpInputManager Input { get; }
		internal DesktopClient Desktop { get; } = new();
		internal LinuxPermissions Permissions { get; }
		internal DesktopKeyboardState KeyboardState { get; }
		internal DesktopBackend X11 { get; }

		internal LinuxServices(Script owner = null)
		{
			Input = new(owner);
			Permissions = new(Input, Desktop);
			Input.Permissions = Permissions;
			Desktop.Permissions = Permissions;
			KeyboardState = new(Desktop.SubscribeKeyboardState);
			X11 = new(DesktopBackend.X11BackendKey, "X11 (keysharp-desktop)", nativeHandles: true, desktop: Desktop);
		}

		public void Dispose()
		{
			try { KeyboardState.Reset(); }
			finally
			{
				try { Input.Dispose(); }
				finally { Desktop.Dispose(); }
			}
		}
	}
}
#endif
