using System;

namespace Keysharp.Components.Input
{
	/// <summary>Supplies the keyboard and mouse transport for Play sending.</summary>
	/// <remarks>Calls carry SendInput's KEYBDINPUT and MOUSEINPUT fields: KEYEVENTF_* and MOUSEEVENTF_* flags.
	/// With MOUSEEVENTF_MOVE, X/Y are signed relative input counts unless MOUSEEVENTF_ABSOLUTE is set, in which case
	/// they are normalized 0..65535 coordinates spanning the virtual desktop with MOUSEEVENTF_VIRTUALDESK.
	/// Ignore X/Y when MOUSEEVENTF_MOVE is absent. Scan codes use set-1 on Windows, with KEYEVENTF_EXTENDEDKEY
	/// in place of the prefix, evdev on Linux and kVK on macOS. The script's hooks recognize its own output by
	/// ExtraInfo, so deliver it with the event; where the transport cannot, a hook provider has to report it.
	/// Submit each call synchronously and throw on failure. A call can arrive while one of the provider's own
	/// capture callbacks is waiting for the script, so it must not wait for that callback to return.</remarks>
	public interface ISenderProvider : IDisposable
	{
		void KeybdEvent(ushort VirtualKey, ushort ScanCode, uint Flags, ulong ExtraInfo);
		void MouseEvent(uint Flags, int X, int Y, uint MouseData, ulong ExtraInfo);
	}
}
