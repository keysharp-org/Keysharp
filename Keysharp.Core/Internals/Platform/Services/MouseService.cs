#if LINUX
using Wl = Keysharp.Internals.Window.Linux.Wayland;
#endif
namespace Keysharp.Internals
{
#if LINUX
	/// <summary>
	/// Resolves the one Linux <see cref="IMouse"/> for this session — the single place the X11-vs-Wayland decision
	/// is made (mirrors <see cref="LinuxScreens"/>). After this, cursor query/injection is plain virtual dispatch
	/// with no per-call session check.
	/// </summary>
	internal static class LinuxMice
	{
		internal static IMouse Resolve()
			=> IsWaylandSession
				? new WaylandMouse()
				: new X11Mouse();
	}

	/// <summary>
	/// Shared Linux base: cursor-shape get/set, plus the absolute-pointer→pixel scaling used by the input service
	/// position fallback. (Mouse-event injection is NOT here — it lives in the keyboard/mouse senders, unified
	/// with keyboard input; this service only covers cursor state.)
	/// </summary>
	internal abstract class LinuxMouseBase : IMouse
	{
		public string GetCursorShape() => GetCursor();

		public void SetCursorShape(string ahkName) => SetCursor(ahkName);

		public abstract bool TryGetCursorPos(out int x, out int y);
		public abstract bool SupportsCursorQueryAndMove { get; }
		public abstract bool TryMoveAbsolute(int x, int y);

		// Button state requires the input service monitoring grant.
		public bool TryGetButtonStateLogical(uint vk, out bool down)
			=> KeysharpInputManager.TryGetButtonStateLogical(vk, out down);

		public bool TryGetButtonStatePhysical(uint vk, out bool down)
			=> KeysharpInputManager.TryGetButtonStatePhysical(vk, out down);

		// Maps a daemon absolute-pointer axis (normalised to [min,max] across the whole virtual desktop) to a screen
		// pixel. origin is the virtual desktop's Left/Top and size its Width/Height: the desktop can start at a
		// negative origin (a monitor left of / above the primary), and a plain 0 origin plus the primary-monitor size
		// would clamp a second-monitor cursor onto the primary.
		protected static bool TryScalePointerAxis(int value, int min, int max, int origin, int size, out int scaled)
		{
			scaled = 0;

			if (max <= min || size <= 0)
				return false;

			var clamped = Math.Clamp(value, min, max);
			scaled = origin + (int)Math.Round((double)(clamped - min) * (size - 1) / (max - min));
			return true;
		}
	}

	/// <summary>
	/// X11 pointer queries and movement are served by keysharp-desktop.
	/// </summary>
	internal sealed class X11Mouse : LinuxMouseBase
	{
		private static Wl.DesktopBackend Broker => Wl.DesktopBackend.X11;

		public override bool TryGetCursorPos(out int x, out int y)
			=> Broker.TryGetCursorPos(out x, out y);
		public override bool SupportsCursorQueryAndMove
			=> Broker.SupportsMouse;
		public override bool TryMoveAbsolute(int x, int y)
			=> Broker.TrySendMouseMoveAbsolute(x, y);
	}

	/// <summary>
	/// Wayland: the core protocol forbids foreign clients from querying or moving the global cursor, so everything
	/// goes through the current compositor backend. When no backend is available, cursor position still falls
	/// back to the input service pointer report, while injection remains unavailable.
	/// </summary>
	internal sealed class WaylandMouse : LinuxMouseBase
	{
		private static Wl.IWaylandBackend Backend => Wl.WaylandBackend.Current;

		public override bool TryGetCursorPos(out int x, out int y)
		{
			if (Backend?.TryGetCursorPos(out x, out y) == true)
				return true;

			// No compositor cursor query (or no backend): for an absolute-positioning device, derive it from input service's
			// last report (normalised across the virtual desktop) scaled onto the virtual-desktop bounds. A_ScreenWidth/
			// Height are the PRIMARY monitor size and assume a 0 origin, which clamps a second-monitor cursor onto the
			// primary; the virtual-desktop bounds carry the true size and (possibly negative) origin.
			var vb = Keysharp.Builtins.Monitor.GetVirtualScreenBounds();

			if (KeysharpInputManager.TryGetPointerPosition(
					out var rawX, out var rawY, out var minX, out var maxX, out var minY, out var maxY)
				&& TryScalePointerAxis(rawX, minX, maxX, (int)vb.Left, (int)vb.Width, out x)
				&& TryScalePointerAxis(rawY, minY, maxY, (int)vb.Top, (int)vb.Height, out y))
				return true;

			x = 0;
			y = 0;
			return false;
		}

		public override bool SupportsCursorQueryAndMove
			=> Backend is { SupportsMouse: true } backend && backend.TryGetCursorPos(out _, out _);

		public override bool TryMoveAbsolute(int x, int y) => Backend?.TrySendMouseMoveAbsolute(x, y) == true;

	}
#elif WINDOWS
	internal sealed class WindowsMouse : IMouse
	{
		public bool TryGetCursorPos(out int x, out int y)
		{
			var pos = Cursor.Position;
			x = Convert.ToInt32(pos.X);
			y = Convert.ToInt32(pos.Y);
			return true;
		}

		public string GetCursorShape() => GetCursor();

		public void SetCursorShape(string ahkName) => SetCursor(ahkName);

		public bool SupportsCursorQueryAndMove => true;
		public bool TryMoveAbsolute(int x, int y)
			=> Keysharp.Internals.Os.Windows.WindowsAPI.SetCursorPos(x, y);

		public bool TryGetButtonStateLogical(uint vk, out bool down)
			=> TryQueryWin32ButtonState(vk, out down);

		public bool TryGetButtonStatePhysical(uint vk, out bool down)
			=> TryQueryWin32ButtonState(vk, out down);

		private static bool TryQueryWin32ButtonState(uint vk, out bool down)
		{
			down = (Keysharp.Internals.Os.Windows.WindowsAPI.GetAsyncKeyState((int)vk) & 0x8000) != 0;
			return true;
		}
	}
#elif OSX
	internal sealed class MacMouse : IMouse
	{
		public bool TryGetCursorPos(out int x, out int y)
		{
			var pos = Forms.Mouse.Position;
			x = Convert.ToInt32(pos.X);
			y = Convert.ToInt32(pos.Y);
			return true;
		}

		public string GetCursorShape() => GetCursor();

		public void SetCursorShape(string ahkName) => SetCursor(ahkName);

		public bool SupportsCursorQueryAndMove => true;
		public bool TryMoveAbsolute(int x, int y)
		{
			var warpResult = CGWarpMouseCursorPosition(new Keysharp.Internals.Input.MacOS.MacNativeInput.CGPoint(x, y));
			var associateResult = CGAssociateMouseAndMouseCursorPosition(1);
			return warpResult == 0 && associateResult == 0;
		}

		[System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static extern int CGWarpMouseCursorPosition(Keysharp.Internals.Input.MacOS.MacNativeInput.CGPoint newCursorPosition);

		[System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

		[System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
		private static extern bool CGEventSourceButtonState(int sourceState, uint button);

		public bool TryGetButtonStateLogical(uint vk, out bool down)
			=> TryQueryButtonState(vk, Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateCombinedSessionState, out down);

		public bool TryGetButtonStatePhysical(uint vk, out bool down)
			=> TryQueryButtonState(vk, Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateHIDSystemState, out down);

		// Live mouse-button state via CoreGraphics (no hook/tap needed).
		// CGMouseButton: Left=0, Right=1, Center=2; extra buttons 3/4 map to XButton1/2.
		private static bool TryQueryButtonState(uint vk, uint sourceState, out bool down)
		{
			down = false;
			uint button;

			switch (vk)
			{
				case 0x01: button = 0; break; // VK_LBUTTON
				case 0x02: button = 1; break; // VK_RBUTTON
				case 0x04: button = 2; break; // VK_MBUTTON
				case 0x05: button = 3; break; // VK_XBUTTON1
				case 0x06: button = 4; break; // VK_XBUTTON2
				default: return false;
			}

			try
			{
				down = CGEventSourceButtonState((int)sourceState, button);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
#endif
}
