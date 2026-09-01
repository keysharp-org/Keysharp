#if LINUX
namespace Keysharp.Internals.Input.Linux
{
	/// <summary>
	/// Resolves the active Wayland compositor's mouse-injection backend for Linux keyboard/mouse senders.
	/// This bypasses <c>Platform.Mouse</c> because that path may perform an X11 warp instead of allowing the
	/// caller to fall back to keysharp-input.
	/// </summary>
	internal static class WaylandMouseInjection
	{
		internal static Keysharp.Internals.Window.Linux.Wayland.IWaylandBackend Backend()
		{
			if (!Platform.Desktop.IsWaylandSession)
				return null;

			var b = Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Current;
			return b?.SupportsMouse == true ? b : null;
		}
	}
}
#endif
