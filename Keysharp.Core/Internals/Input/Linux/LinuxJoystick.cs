using Keysharp.Builtins;
#if LINUX
// Disambiguate from the enclosing Keysharp.Internals.Input.Joystick namespace.
using JoystickCore = Keysharp.Internals.Input.Joystick.Joystick;

namespace Keysharp.Internals.Input.Linux
{
	/// <summary>
	/// Joystick and gamepad access on Linux, mirroring the winmm joyGetPosEx model used on Windows.
	/// Reading the devices goes through keysharp-input, so it needs neither membership in the "input"
	/// group nor a permission grant: gamepads carry no text, so the service leaves them ungated.
	/// Joysticks are addressed by a zero-based index into the gamepads it reports, which it orders by
	/// device node so that a script's joystick number keeps meaning the same device.
	///
	/// All access is best-effort: when the service is unavailable or the joystick is gone, the public
	/// helpers return "no joystick" results rather than throwing, so scripts that query joysticks on a
	/// machine without one (the common case) behave gracefully instead of crashing.
	///
	/// The axis mapping from Windows' fixed X/Y/Z/R/U/V set onto evdev axes is necessarily a
	/// heuristic, because evdev exposes device-specific axis codes. The chosen mapping matches the
	/// most common convention used by SDL/Wine for the legacy joystick API.
	/// </summary>
	internal static class LinuxJoystick
	{
		// evdev absolute axis codes (linux/input-event-codes.h).
		private const uint ABS_X = 0x00;
		private const uint ABS_Y = 0x01;
		private const uint ABS_Z = 0x02;
		private const uint ABS_RX = 0x03;
		private const uint ABS_RY = 0x04;
		private const uint ABS_RZ = 0x05;
		private const uint ABS_HAT0X = 0x10;
		private const uint ABS_HAT0Y = 0x11;

		// Windows axis -> evdev axis. R is "rudder or 4th axis"; the rest follow the common
		// SDL/Wine convention for the legacy joystick API.
		private static uint AbsForJoyControl(JoyControls joy) => joy switch
		{
			JoyControls.Xpos => ABS_X,
			JoyControls.Ypos => ABS_Y,
			JoyControls.Zpos => ABS_Z,
			JoyControls.Rpos => ABS_RZ,
			JoyControls.Upos => ABS_RX,
			JoyControls.Vpos => ABS_RY,
			_ => uint.MaxValue
		};

		// Enumerating gamepads costs a round-trip, and GetKeyState("Joy…") can be polled in a tight
		// loop. Cache the listing briefly so a poll loop reuses it, while still picking up a newly
		// plugged controller within the TTL.
		private static readonly Lock deviceCacheGate = new();
		private static List<KeysharpInputClient.GamepadInfo> cachedDevices;
		private static ulong cachedGeneration;
		private static long cacheStampTicks;
		private const long CacheTtlTicks = TimeSpan.TicksPerSecond; // re-list at most ~once per second

		private static List<KeysharpInputClient.GamepadInfo> GetDevices(out ulong generation)
		{
			lock (deviceCacheGate)
			{
				var now = DateTime.UtcNow.Ticks;

				if (cachedDevices == null || now - cacheStampTicks > CacheTtlTicks)
					RefreshLocked(now);

				generation = cachedGeneration;
				return cachedDevices;
			}
		}

		private static void RefreshLocked(long now)
		{
			if (!KeysharpInputManager.TryListGamepads(out var gamepads, out var generation))
				gamepads = [];

			cachedDevices = gamepads;
			cachedGeneration = generation;
			cacheStampTicks = now;
		}

		/// <summary>
		/// Returns the joystick at the given zero-based index, or false if it does not exist.
		/// </summary>
		private static bool TryGetDevice(uint index, out KeysharpInputClient.GamepadInfo device,
			out ulong generation)
		{
			var devices = GetDevices(out generation);

			if (index >= devices.Count)
			{
				device = default;
				return false;
			}

			device = devices[(int)index];
			return true;
		}

		/// <summary>
		/// Reads the joystick's live state. A device set that changed since the cached listing was
		/// taken is re-listed once, so a hotplug costs a retry rather than a wrong answer.
		/// </summary>
		private static bool TryGetState(uint index, out KeysharpInputClient.GamepadInfo device,
			out KeysharpInputClient.GamepadState state)
		{
			state = default;

			if (!TryGetDevice(index, out device, out var generation))
				return false;

			if (KeysharpInputManager.TryGetGamepadState(device.DeviceId, generation, out state))
				return true;

			lock (deviceCacheGate)
			{
				// Another poll may already have re-listed; only the first one pays for it.
				if (cachedGeneration == generation)
					RefreshLocked(DateTime.UtcNow.Ticks);
			}

			return TryGetDevice(index, out device, out generation)
				&& KeysharpInputManager.TryGetGamepadState(device.DeviceId, generation, out state);
		}

		/// <summary>
		/// Returns the position of the given axis in the device's axis list, which is also its position
		/// in a state reading, or -1 when the device has no such axis.
		/// </summary>
		private static int IndexOfAxis(in KeysharpInputClient.GamepadInfo device, uint code)
		{
			var axes = device.Axes;

			for (var i = 0; i < axes.Length; i++)
				if (axes[i].Code == code)
					return i;

			return -1;
		}

		private static bool TryGetAxis(in KeysharpInputClient.GamepadInfo device,
			in KeysharpInputClient.GamepadState state, uint code, out int value,
			out KeysharpInputClient.GamepadAxis axis)
		{
			var index = IndexOfAxis(device, code);
			axis = default;
			value = 0;

			if (index < 0 || index >= state.AxisValues.Length)
				return false;

			axis = device.Axes[index];
			value = state.AxisValues[index];
			return true;
		}

		/// <summary>
		/// Polls every joystick that currently has hotkeys bound and queues the hotkey messages for
		/// any buttons that have newly transitioned to the down state, mirroring the Windows path.
		/// </summary>
		internal static void PollJoysticks()
		{
			var script = Script.TheScript;
			var jd = script.JoystickData;

			for (var i = 0; i < JoystickData.MaxJoysticks; ++i)
			{
				if (!script.HotkeyData.joystickHasHotkeys[i])
					continue;

				if (!TryGetState((uint)i, out _, out var state))
					continue;

				// XOR finds changed buttons; AND with the current state keeps only up->down transitions.
				var buttonsNewlyDown = (state.Buttons ^ jd.buttonsPrev[i]) & state.Buttons;
				jd.buttonsPrev[i] = state.Buttons;

				if (buttonsNewlyDown != 0)
					HotkeyDefinition.TriggerJoyHotkeys(i, buttonsNewlyDown);
			}
		}

		/// <summary>
		/// Returns the requested joystick state, mirroring the Windows ScriptGetJoyState contract:
		/// a bool for buttons, a double percentage for axes, a long for POV, and strings for the
		/// informational queries. Returns false / blank when the joystick or control is unavailable.
		/// </summary>
		internal static object ScriptGetJoyState(JoyControls joy, uint joystickID)
		{
			// The informational queries describe the device rather than its position, so they are
			// answered from the listing without reading live state.
			switch (joy)
			{
				case JoyControls.Name:
					return TryGetDevice(joystickID, out var named, out _) ? named.Name : "";

				case JoyControls.Buttons:
					return TryGetDevice(joystickID, out var counted, out _) ? (long)counted.ButtonCount : false;

				case JoyControls.Axes:
					return TryGetDevice(joystickID, out var measured, out _) ? (long)CountAxes(measured) : false;

				case JoyControls.Info:
					return TryGetDevice(joystickID, out var described, out _) ? GetInfo(described) : "";
			}

			if (!TryGetState(joystickID, out var device, out var state))
				return false;

			if (JoystickCore.IsJoystickButton(joy))
			{
				var bit = (int)joy - (int)JoyControls.Button1;
				return bit >= 0 && bit < state.ButtonCount && ((state.Buttons >> bit) & 0x1) != 0;
			}

			switch (joy)
			{
				case JoyControls.Xpos:
				case JoyControls.Ypos:
				case JoyControls.Zpos:
				case JoyControls.Rpos:
				case JoyControls.Upos:
				case JoyControls.Vpos:
				{
					if (!TryGetAxis(device, state, AbsForJoyControl(joy), out var value, out var axis))
						return 0.0;

					var range = axis.Maximum - axis.Minimum;
					return range != 0 ? 100.0 * (value - axis.Minimum) / range : (double)value;
				}

				case JoyControls.Pov:
					return GetPov(device, state);
			}

			return false;
		}

		private static long GetPov(in KeysharpInputClient.GamepadInfo device,
			in KeysharpInputClient.GamepadState state)
		{
			if (!TryGetAxis(device, state, ABS_HAT0X, out var hx, out _)
					|| !TryGetAxis(device, state, ABS_HAT0Y, out var hy, out _))
				return -1L;

			var x = Math.Sign(hx);
			var y = Math.Sign(hy);

			// Centidegrees, clockwise from North, matching Windows JOYINFOEX.dwPOV. -1 when centered.
			return (x, y) switch
			{
				(0, -1) => 0L,
				(1, -1) => 4500L,
				(1, 0) => 9000L,
				(1, 1) => 13500L,
				(0, 1) => 18000L,
				(-1, 1) => 22500L,
				(-1, 0) => 27000L,
				(-1, -1) => 31500L,
				_ => -1L
			};
		}

		private static int CountAxes(in KeysharpInputClient.GamepadInfo device)
		{
			var count = 0;

			foreach (var joy in new[] { JoyControls.Xpos, JoyControls.Ypos, JoyControls.Zpos, JoyControls.Rpos, JoyControls.Upos, JoyControls.Vpos })
				if (IndexOfAxis(device, AbsForJoyControl(joy)) >= 0)
					count++;

			return count;
		}

		private static string GetInfo(in KeysharpInputClient.GamepadInfo device)
		{
			var str = "";

			if (IndexOfAxis(device, ABS_Z) >= 0) str += 'Z';
			if (IndexOfAxis(device, ABS_RZ) >= 0) str += 'R';
			if (IndexOfAxis(device, ABS_RX) >= 0) str += 'U';
			if (IndexOfAxis(device, ABS_RY) >= 0) str += 'V';

			if (IndexOfAxis(device, ABS_HAT0X) >= 0)
			{
				str += 'P';
				str += 'D'; // evdev hat0 reports discrete 4/8-way directions.
			}

			return str;
		}
	}
}
#endif
