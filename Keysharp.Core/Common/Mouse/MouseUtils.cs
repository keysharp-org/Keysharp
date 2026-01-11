
using static Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Common.Mouse
{
	[Flags]
	public enum MOUSEEVENTF : uint
	{
		MOVE = 0x0001,  // mouse move
		LEFTDOWN = 0x0002,  // left button down
		LEFTUP = 0x0004,  // left button up
		RIGHTDOWN = 0x0008,  // right button down
		RIGHTUP = 0x0010,  // right button up
		MIDDLEDOWN = 0x0020,  // middle button down
		MIDDLEUP = 0x0040,  // middle button up
		XDOWN = 0x0080,  // x button down
		XUP = 0x0100,  // x button down
		WHEEL = 0x0800,  // wheel button rolled
		VIRTUALDESK = 0x4000,  // map to entire virtual desktop
		ABSOLUTE = 0x8000,  // absolute move
		HWHEEL = 0x01000, // hwheel button rolled
		MOVE_NOCOALESCE = 0x2000//do not coalesce mouse moves.
	}

	internal class MouseUtils
	{
		internal const uint WHEEL_DELTA = 120;
		internal const uint XBUTTON1 = 0x0001;
		internal const uint XBUTTON2 = 0x0002;

        internal static bool IsMouseVK(uint vk)
		{
			return vk >= VK_LBUTTON && vk <= VK_XBUTTON2 && vk != VK_CANCEL
				   || vk >= VK_NEW_MOUSE_FIRST && vk <= VK_NEW_MOUSE_LAST;
		}

		internal static bool IsWheelVK(uint vk) => vk >= VK_WHEEL_LEFT && vk <= VK_WHEEL_UP;
	}
}