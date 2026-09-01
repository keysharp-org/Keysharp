#if !WINDOWS
using System;

namespace Keysharp.Internals.Input.Hooks
{
	[Flags]
	internal enum EventMask : uint
	{
		None = 0,
		LeftShift = 1u << 0,
		LeftCtrl = 1u << 1,
		LeftMeta = 1u << 2,
		LeftAlt = 1u << 3,
		RightShift = 1u << 4,
		RightCtrl = 1u << 5,
		RightMeta = 1u << 6,
		RightAlt = 1u << 7,
		Shift = LeftShift | RightShift,
		Ctrl = LeftCtrl | RightCtrl,
		Meta = LeftMeta | RightMeta,
		Alt = LeftAlt | RightAlt,
		Button1 = 1u << 8,
		Button2 = 1u << 9,
		Button3 = 1u << 10,
		Button4 = 1u << 11,
		Button5 = 1u << 12,
		NumLock = 1u << 13,
		CapsLock = 1u << 14,
		ScrollLock = 1u << 15,
		SimulatedEvent = 1u << 30,
		SuppressEvent = 1u << 31
	}

	internal enum EventType : uint
	{
		KeyPressed,
		KeyReleased,
		KeyTyped,
		MousePressed,
		MouseReleased,
		MouseMoved,
		MouseDragged,
		MouseWheel
	}

	internal enum MouseButton : ushort
	{
		NoButton = 0,
		Button1 = 1,
		Button2 = 2,
		Button3 = 3,
		Button4 = 4,
		Button5 = 5
	}

	internal enum MouseWheelScrollDirection
	{
		Vertical,
		Horizontal
	}

	internal enum MouseWheelScrollType
	{
		UnitScroll
	}

	internal struct KeyboardEventData
	{
		internal const ushort RawUndefinedChar = 0xFFFF;
		internal uint VkCode;
		internal uint KeyCode;
		internal ushort RawCode;
		internal ushort RawKeyChar;
	}

	internal struct MouseEventData
	{
		internal MouseButton Button;
		internal int Clicks;
		internal int X;
		internal int Y;
	}

	internal struct MouseWheelEventData
	{
		internal MouseWheelScrollType Type;
		internal int Rotation;
		internal int Delta;
		internal MouseWheelScrollDirection Direction;
		internal int X;
		internal int Y;
	}

	internal class HookEventArgs : EventArgs
	{
		internal HookEventArgs(EventType type, ulong timestamp = 0, EventMask mask = EventMask.None)
		{
			Type = type;
			Timestamp = timestamp != 0 ? unchecked((long)timestamp) : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			Mask = mask;
			IsEventSimulated = (mask & EventMask.SimulatedEvent) != 0;
		}

		internal EventType Type { get; }
		internal DateTimeOffset EventTime => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);
		internal long Timestamp { get; }
		internal EventMask Mask { get; init; }
		internal bool IsEventSimulated { get; init; }
		internal bool IsAutoRepeat { get; init; }
		internal bool SuppressEvent { get; set; }
		internal bool HasKeyboard { get; init; }
		internal bool HasMouse => !HasKeyboard;
		// Whether this event carries a real cursor position (X/Y). The macOS event tap always supplies one, but
		// the Linux input service daemon does not for button/wheel events (its evdev button reports have no coordinates,
		// and it deliberately does not query the compositor from the hook thread). When false, A_EventInfo omits
		// X/Y so a #HotIf predicate/callback resolves the location itself on the script thread (e.g. MouseGetPos).
		internal bool HasPosition { get; init; } = true;
	}

	internal sealed class KeyboardHookEventArgs : HookEventArgs
	{
		internal KeyboardHookEventArgs(EventType type, uint vk, uint sc, EventMask mask = EventMask.None, ulong timestamp = 0)
			: base(type, timestamp, mask)
		{
			HasKeyboard = true;
			Data = new KeyboardEventData
			{
				VkCode = vk,
				KeyCode = vk,
				RawCode = (ushort)sc,
				RawKeyChar = KeyboardEventData.RawUndefinedChar
			};
		}

		internal KeyboardEventData Data { get; }
	}

	internal sealed class MouseHookEventArgs : HookEventArgs
	{
		internal MouseHookEventArgs(EventType type, MouseButton button, int x, int y, EventMask mask = EventMask.None, ulong timestamp = 0, int clicks = 1)
			: base(type, timestamp, mask)
		{
			Data = new MouseEventData
			{
				Button = button,
				Clicks = Math.Max(1, clicks),
				X = x,
				Y = y
			};
		}

		internal MouseEventData Data { get; }
	}

	internal sealed class MouseWheelHookEventArgs : HookEventArgs
	{
		internal MouseWheelHookEventArgs(int rotation, MouseWheelScrollDirection direction, int x, int y, EventMask mask = EventMask.None, ulong timestamp = 0)
			: base(EventType.MouseWheel, timestamp, mask)
		{
			Data = new MouseWheelEventData
			{
				Type = MouseWheelScrollType.UnitScroll,
				Rotation = rotation,
				Delta = 120,
				Direction = direction,
				X = x,
				Y = y
			};
		}

		internal MouseWheelEventData Data { get; }
	}
}
#endif
