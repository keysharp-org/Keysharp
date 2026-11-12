#if LINUX
namespace Keysharp.Internals.Input.Linux
{
	/// <summary>
	/// Joysticks and gamepads on Linux, read through keysharp-input. That needs neither membership in the "input"
	/// group nor a permission grant: gamepads carry no text, so the service leaves them ungated.
	/// A joystick's number is its position among the gamepads the service reports, which it orders by
	/// device node so that a script's joystick number keeps meaning the same device.
	///
	/// When the service is unavailable or the joystick is gone, a read fails rather than throws, so a script
	/// querying a joystick on a machine without one sees the blank values it would on Windows.
	/// </summary>
	internal static class LinuxJoystick
	{
		// linux/input-event-codes.h. BTN_SOUTH is BTN_GAMEPAD, the button the kernel's gamepad layout requires.
		private const ushort BTN_SOUTH = 0x130, BTN_EAST = 0x131, BTN_NORTH = 0x133, BTN_WEST = 0x134,
			BTN_TL = 0x136, BTN_TR = 0x137, BTN_SELECT = 0x13a, BTN_START = 0x13b, BTN_THUMBL = 0x13d, BTN_THUMBR = 0x13e;

		// A, B, X, Y, the bumpers, Back, Start and the stick clicks: buttons 1 to 10 of an Xbox controller on Windows.
		private static readonly ushort[] xboxButtonOrder =
			[BTN_SOUTH, BTN_EAST, BTN_NORTH, BTN_WEST, BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_THUMBL, BTN_THUMBR];

		/// <summary>One listed gamepad, with its axes and buttons arranged as a joystick's.</summary>
		internal sealed class Device
		{
			internal readonly uint DeviceId;
			internal readonly JoyCaps Caps;
			private readonly JoystickLayout layout;
			private readonly int hatX, hatY;
			// The state bit each joystick button reads, or null when the joystick's buttons are in the state's order.
			private readonly int[] buttonBits;

			internal Device(in KeysharpInputClient.GamepadInfo info)
			{
				DeviceId = info.DeviceId;
				var axes = Array.ConvertAll(info.Axes, axis => new JoyAxisRange(axis.Code, axis.Minimum, axis.Maximum));
				// The kernel's gamepad layout (Documentation/input/gamepad.rst), which its drivers give Xbox,
				// PlayStation and Switch controllers alike. Any other device keeps the axes and button order it declares.
				var gamepad = Array.IndexOf(info.ButtonCodes, BTN_SOUTH) >= 0
					&& Array.Exists(axes, axis => axis.Code == JoystickLayout.AbsRx)
					&& Array.Exists(axes, axis => axis.Code == JoystickLayout.AbsRy);
				layout = new JoystickLayout(axes, gamepad);
				hatX = layout.IndexOf(JoystickLayout.AbsHat0X);
				hatY = layout.IndexOf(JoystickLayout.AbsHat0Y);

				if (gamepad)
					buttonBits = OrderAsXbox(info.ButtonCodes);

				// A hat missing one of its axes cannot give a direction, so it is not a POV.
				Caps = new(info.Name, info.ButtonCount, layout.Axes, hatX >= 0 && hatY >= 0);
			}

			// The kernel's order puts the guide button before the stick clicks, which Windows numbers 9 and 10.
			private static int[] OrderAsXbox(ushort[] codes)
			{
				var bits = new int[codes.Length];
				var count = 0;

				foreach (var code in xboxButtonOrder)
					if (Array.IndexOf(codes, code) is >= 0 and var bit)
						bits[count++] = bit;

				for (var bit = 0; bit < codes.Length; bit++)
					if (Array.IndexOf(xboxButtonOrder, codes[bit]) < 0)
						bits[count++] = bit;

				return bits;
			}

			internal JoyReading Read(in KeysharpInputClient.GamepadState state)
			{
				var reading = new JoyReading { Buttons = state.Buttons, Pov = -1 };

				if (buttonBits != null)
				{
					reading.Buttons = 0;

					for (var i = Math.Min(buttonBits.Length, JoystickData.MaxJoyButtons) - 1; i >= 0; i--)
						if (buttonBits[i] < 32 && ((state.Buttons >> buttonBits[i]) & 1) != 0)
							reading.Buttons |= 1u << i;
				}

				var values = state.AxisValues;
				layout.Read(values, ref reading);

				if (Caps.HasPov && hatX < values.Length && hatY < values.Length)
					reading.Pov = JoystickLayout.PovFromHat(values[hatX], values[hatY]);

				return reading;
			}
		}

		// Enumerating gamepads costs a round-trip, and GetKeyState("Joy…") can be polled in a tight
		// loop. Cache the listing briefly so a poll loop reuses it, while still picking up a newly
		// plugged controller within the TTL.
		private static readonly Lock deviceCacheGate = new();
		private static Device[] cachedDevices;
		private static ulong cachedGeneration;
		private static long cacheStampTicks;
		private const long CacheTtlTicks = TimeSpan.TicksPerSecond; // re-list at most ~once per second

		private static bool TryGetDevice(uint index, out Device device, out ulong generation)
		{
			lock (deviceCacheGate)
			{
				var now = DateTime.UtcNow.Ticks;

				if (cachedDevices == null || now - cacheStampTicks > CacheTtlTicks)
					RefreshLocked(now);

				generation = cachedGeneration;
				device = index < cachedDevices.Length ? cachedDevices[index] : null;
				return device != null;
			}
		}

		private static void RefreshLocked(long now)
		{
			cachedDevices = KeysharpInputManager.Current.TryListGamepads(out var gamepads, out var generation)
				? gamepads.ConvertAll(static gamepad => new Device(gamepad)).ToArray()
				: [];
			cachedGeneration = generation;
			cacheStampTicks = now;
		}

		internal static bool TryGetCaps(uint index, out JoyCaps caps)
		{
			var found = TryGetDevice(index, out var device, out _);
			caps = found ? device.Caps : default;
			return found;
		}

		/// <summary>
		/// Reads the joystick's live state. A device set that changed since the cached listing was
		/// taken is re-listed once, so a hotplug costs a retry rather than a wrong answer.
		/// </summary>
		internal static bool TryGetReading(uint index, out JoyReading reading)
		{
			reading = default;

			if (!TryGetDevice(index, out var device, out var generation))
				return false;

			if (!KeysharpInputManager.Current.TryGetGamepadState(device.DeviceId, generation, out var state))
			{
				lock (deviceCacheGate)
				{
					// Another poll may already have re-listed; only the first one pays for it.
					if (cachedGeneration == generation)
						RefreshLocked(DateTime.UtcNow.Ticks);
				}

				if (!TryGetDevice(index, out device, out generation)
						|| !KeysharpInputManager.Current.TryGetGamepadState(device.DeviceId, generation, out state))
					return false;
			}

			reading = device.Read(state);
			return true;
		}
	}
}
#endif
