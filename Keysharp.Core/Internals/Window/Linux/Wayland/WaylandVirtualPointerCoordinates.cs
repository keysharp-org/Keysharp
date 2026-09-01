#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Pure conversion from a global screen-pixel coordinate to zwlr_virtual_pointer_v1's
	/// motion_absolute wire arguments. The compositor computes position as x/x_extent (a [0,1]
	/// fraction of the whole output-layout bounding box when no output is pinned to the pointer),
	/// so this mirrors LinuxKeyboardMouseSender's virtual-desktop-origin translation for the
	/// uinput fallback (targetX - vb.Left, etc.) rather than a [0,65535] uinput-style normalization.
	/// wlroots silently drops the request if either extent is 0, so extents are clamped to a
	/// minimum of 1.
	/// </summary>
	internal static class WaylandVirtualPointerCoordinates
	{
		internal static (uint X, uint Y, uint XExtent, uint YExtent) ToMotionAbsolute(
			int screenX, int screenY, int virtualLeft, int virtualTop, int virtualWidth, int virtualHeight)
		{
			var xExtent = (uint)Math.Max(1, virtualWidth);
			var yExtent = (uint)Math.Max(1, virtualHeight);
			var x = (uint)Math.Clamp(screenX - virtualLeft, 0, (int)xExtent);
			var y = (uint)Math.Clamp(screenY - virtualTop, 0, (int)yExtent);
			return (x, y, xExtent, yExtent);
		}
	}
}
#endif
