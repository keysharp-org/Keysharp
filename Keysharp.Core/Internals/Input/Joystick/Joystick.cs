using Keysharp.Builtins;
#if LINUX
using JoystickBackend = Keysharp.Internals.Input.Linux.LinuxJoystick;
#elif OSX
using JoystickBackend = Keysharp.Internals.Input.MacOS.MacJoystick;
#endif

namespace Keysharp.Internals.Input.Joystick
{
	internal class JoystickData
	{
		internal const int MaxJoyButtons = 32;
		internal const int MaxJoysticks = 16;  // The maximum allowed by any Windows operating system.
		internal uint[] buttonsPrev = new uint[MaxJoysticks];
	}

	internal static class Joystick
	{
		/// <summary>
		/// The caller TextToKey() currently relies on the fact that when aAllowOnlyButtons==true, a value
		/// that can fit in a sc_type (USHORT) is returned, which is true since the joystick buttons
		/// are very small numbers (JOYCTRL_1==12).
		/// </summary>
		internal static JoyControls ConvertJoy(string buf, ref uint? joystickID, bool allowOnlyButtons = false)
		{
			if (joystickID != null)
				joystickID = 0;  // Set default output value for the caller.

			if (string.IsNullOrEmpty(buf))
				return JoyControls.Invalid;

			var index = 0;

			for (; index < buf.Length && buf[index] >= '0' && buf[index] <= '9'; ++index) ; // self-contained loop to find the first non-digit.

			if (index > 0) // The string starts with a joystick number, e.g. "2JoyX".
			{
				// As AHK's ATOI, the number wraps to 32 bits before its range is checked.
				if (!long.TryParse(buf.AsSpan(0, index), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
					return JoyControls.Invalid;

				var joystick_id = unchecked((int)number - 1);

				if (joystick_id < 0 || joystick_id >= JoystickData.MaxJoysticks)
					return JoyControls.Invalid;

				if (joystickID != null)
					joystickID = (uint)joystick_id;
			}

			// Everything after the optional leading joystick number is the control name itself.
			var rest = buf.AsSpan(index);

			if (rest.StartsWith("Joy", StringComparison.OrdinalIgnoreCase) && rest.Slice(3).TryParseLong(out var button))
			{
				var offset = unchecked((int)button);

				if (offset < 1 || offset > JoystickData.MaxJoyButtons)
					return JoyControls.Invalid;

				return JoyControls.Button1 + (offset - 1);
			}

			if (allowOnlyButtons)
				return JoyControls.Invalid;

			if (rest.Equals("JoyX", StringComparison.OrdinalIgnoreCase)) return JoyControls.Xpos;

			if (rest.Equals("JoyY", StringComparison.OrdinalIgnoreCase)) return JoyControls.Ypos;

			if (rest.Equals("JoyZ", StringComparison.OrdinalIgnoreCase)) return JoyControls.Zpos;

			if (rest.Equals("JoyR", StringComparison.OrdinalIgnoreCase)) return JoyControls.Rpos;

			if (rest.Equals("JoyU", StringComparison.OrdinalIgnoreCase)) return JoyControls.Upos;

			if (rest.Equals("JoyV", StringComparison.OrdinalIgnoreCase)) return JoyControls.Vpos;

			if (rest.Equals("JoyPOV", StringComparison.OrdinalIgnoreCase)) return JoyControls.Pov;

			if (rest.Equals("JoyName", StringComparison.OrdinalIgnoreCase)) return JoyControls.Name;

			if (rest.Equals("JoyButtons", StringComparison.OrdinalIgnoreCase)) return JoyControls.Buttons;

			if (rest.Equals("JoyAxes", StringComparison.OrdinalIgnoreCase)) return JoyControls.Axes;

			if (rest.Equals("JoyInfo", StringComparison.OrdinalIgnoreCase)) return JoyControls.Info;

			return JoyControls.Invalid;
		}

		// Also the max that Windows supports.
		internal static bool IsJoystickButton(JoyControls joy) => joy >= JoyControls.Button1&& joy <= JoyControls.ButtonMax;

		/// <summary>
		/// It's best to call this function only directly from MsgSleep() or when there is an instance of
		/// MsgSleep() closer on the call stack than the nearest dialog's message pump (e.g. MsgBox).
		/// This is because events posted to the thread indirectly by us here would be discarded or mishandled
		/// by a non-standard (dialog) message pump.
		///
		/// Polling the joysticks this way rather than using joySetCapture() is preferable for several reasons:
		/// 1) I believe joySetCapture() internally polls the joystick anyway, via a system timer, so it probably
		///    doesn't perform much better (if at all) than polling "manually".
		/// 2) joySetCapture() only supports 4 buttons;
		/// 3) joySetCapture() will fail if another app is already capturing the joystick;
		/// 4) Even if the joySetCapture() succeeds, other programs (e.g. older games), would be prevented from
		///    capturing the joystick while the script in question is running.
		/// </summary>
		internal static void PollJoysticks()
		{
			// Even if joystick hotkeys aren't currently allowed to fire, poll it anyway so that hotkey
			// messages can be buffered for later.
			var script = Script.TheScript;
			var jd = script.JoystickData;
#if WINDOWS
			var jie = JOYINFOEX.Default;
#endif

			for (var i = 0; i < JoystickData.MaxJoysticks; ++i)
			{
				if (!script.HotkeyData.joystickHasHotkeys[i])
					continue;

#if WINDOWS
				// Reset these every time in case joyGetPosEx() ever changes them. Also, most systems have only one joystick,
				// so this code will hardly ever be executed more than once (and sometimes zero times):
				jie.dwFlags = WindowsAPI.JOY_RETURNBUTTONS; // vs. JOY_RETURNALL

				if (WindowsAPI.joyGetPosEx(i, ref jie) != WindowsAPI.JOYERR_NOERROR) // Skip this joystick and try the others.
					continue;

				var buttons = jie.dwButtons;
#else

				if (!JoystickBackend.TryGetReading((uint)i, out var reading))
					continue;

				var buttons = reading.Buttons;
#endif
				// The exclusive-or operator determines which buttons have changed state.  After that,
				// the bitwise-and operator determines which of those have gone from up to down (the
				// down-to-up events are currently not significant).
				var buttons_newly_down = (buttons ^ jd.buttonsPrev[i]) & buttons;
				jd.buttonsPrev[i] = buttons;

				if (buttons_newly_down == 0)
					continue;

				// See if any of the joystick hotkeys match this joystick ID and one of the buttons that
				// has just been pressed on it.
				HotkeyDefinition.TriggerJoyHotkeys(i, buttons_newly_down);
			}
		}

		/// <summary>
		/// The value GetKeyState returns for a joystick control: a bool for a button, a percentage for an axis,
		/// centidegrees or -1 for the POV, and blank when the joystick cannot be read. KeyWait takes a button as
		/// down only when this is true.
		/// </summary>
		internal static object ScriptGetJoyState(JoyControls joy, uint joystickID)
		{
			if (joy == JoyControls.Invalid) // Currently never called this way.
				return "";

#if WINDOWS
			var joyIsButton = IsJoystickButton(joy);
			var jc = new JOYCAPS();

			if (!joyIsButton && joy != JoyControls.Pov)
			{
				// Get the joystick's range of motion so that we can report position as a percentage.
				if (WindowsAPI.joyGetDevCaps(new nint(joystickID), ref jc, (uint)Marshal.SizeOf(jc)) != WindowsAPI.JOYERR_NOERROR)
					jc = new JOYCAPS();//Recreate on failure, for use of the zeroes later below.
			}

			// Fetch this struct's info only if needed:
			var jie = JOYINFOEX.Default;

			if (joy != JoyControls.Name && joy != JoyControls.Buttons && joy != JoyControls.Axes && joy != JoyControls.Info)
			{
				jie.dwFlags = WindowsAPI.JOY_RETURNALL;

				if (WindowsAPI.joyGetPosEx((int)joystickID, ref jie) != WindowsAPI.JOYERR_NOERROR)
					return "";

				if (joyIsButton)
					return ((jie.dwButtons >> ((int)joy - (int)JoyControls.Button1)) & 0x01) != 0;
			}

			// Otherwise:
			uint range;
			var str = "";

			switch (joy)
			{
				case JoyControls.Xpos:
					range = (jc.wXmax > jc.wXmin) ? jc.wXmax - jc.wXmin : 0;
					return range != 0 ? 100 * (double)jie.dwXpos / range : jie.dwXpos;

				case JoyControls.Ypos:
					range = (jc.wYmax > jc.wYmin) ? jc.wYmax - jc.wYmin : 0;
					return range != 0 ? 100 * (double)jie.dwYpos / range : jie.dwYpos;

				case JoyControls.Zpos:
					range = (jc.wZmax > jc.wZmin) ? jc.wZmax - jc.wZmin : 0;
					return range != 0 ? 100 * (double)jie.dwZpos / range : jie.dwZpos;

				case JoyControls.Rpos:  // Rudder or 4th axis.
					range = (jc.wRmax > jc.wRmin) ? jc.wRmax - jc.wRmin : 0;
					return range != 0 ? 100 * (double)jie.dwRpos / range : jie.dwRpos;

				case JoyControls.Upos:  // 5th axis.
					range = (jc.wUmax > jc.wUmin) ? jc.wUmax - jc.wUmin : 0;
					return range != 0 ? 100 * (double)jie.dwUpos / range : jie.dwUpos;

				case JoyControls.Vpos:  // 6th axis.
					range = (jc.wVmax > jc.wVmin) ? jc.wVmax - jc.wVmin : 0;
					return range != 0 ? 100 * (double)jie.dwVpos / range : jie.dwVpos;

				case JoyControls.Pov:
					if (jie.dwPOV == WindowsAPI.JOY_POVCENTERED)
						return -1L;
					else
						return (long)jie.dwPOV;

				case JoyControls.Name:
					return jc.szPname ?? "";

				case JoyControls.Buttons:
					return (long)jc.wNumButtons; // wMaxButtons is the *driver's* max supported buttons.

				case JoyControls.Axes:
					return (long)jc.wNumAxes; // wMaxAxes is the *driver's* max supported axes.

				case JoyControls.Info:
					if ((jc.wCaps & WindowsAPI.JOYCAPS_HASZ) != 0)
						str += 'Z';

					if ((jc.wCaps & WindowsAPI.JOYCAPS_HASR) != 0)
						str += 'R';

					if ((jc.wCaps & WindowsAPI.JOYCAPS_HASU) != 0)
						str += 'U';

					if ((jc.wCaps & WindowsAPI.JOYCAPS_HASV) != 0)
						str += 'V';

					if ((jc.wCaps & WindowsAPI.JOYCAPS_HASPOV) != 0)
					{
						str += 'P';

						if ((jc.wCaps & WindowsAPI.JOYCAPS_POV4DIR) != 0)
							str += 'D';

						if ((jc.wCaps & WindowsAPI.JOYCAPS_POVCTS) != 0)
							str += 'C';
					}

					return str;
			}

			return "";
#else

			switch (joy)
			{
				case JoyControls.Name:
				case JoyControls.Buttons:
				case JoyControls.Axes:
				case JoyControls.Info:
					// A joystick which is not there has no name, buttons or axes, as with the zeroed JOYCAPS above.
					_ = JoystickBackend.TryGetCaps(joystickID, out var caps);
					return joy switch
					{
						JoyControls.Name => caps.Name ?? "",
						JoyControls.Buttons => (long)caps.Buttons,
						JoyControls.Axes => (long)caps.AxisCount,
						_ => caps.Info
					};
			}

			if (!JoystickBackend.TryGetReading(joystickID, out var reading))
				return "";

			if (IsJoystickButton(joy))
				return ((reading.Buttons >> ((int)joy - (int)JoyControls.Button1)) & 0x01) != 0;

			if (joy == JoyControls.Pov)
				return reading.Pov;

			return reading[joy];
#endif
		}
	}

	internal enum JoyControls
	{
		Invalid, Xpos, Ypos, Zpos
		, Rpos, Upos, Vpos, Pov
		, Name, Buttons, Axes, Info
		, Button1, Button2, Button3, Button4, Button5, Button6, Button7, Button8  // Buttons.
		, Button9, Button10, Button11, Button12, Button13, Button14, Button15, Button16
		, Button17, Button18, Button19, Button20, Button21, Button22, Button23, Button24
		, Button25, Button26, Button27, Button28, Button29, Button30, Button31, Button32
		, ButtonMax = Button32
	};
}
