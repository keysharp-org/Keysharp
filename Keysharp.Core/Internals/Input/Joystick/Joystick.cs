using Keysharp.Builtins;
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
				var val = (int?)buf.Substring(0, index).ParseLong();
				var joystick_id = val.HasValue && val.Value > 0 ? (uint)(val.Value - 1) : 0u;

				if (joystick_id >= JoystickData.MaxJoysticks)
					return JoyControls.Invalid;

				if (joystickID != null)
					joystickID = joystick_id;  // Use ATOI vs. atoi even though hex isn't supported yet.
			}

			// Everything after the optional leading joystick number is the control name itself.
			var rest = index > 0 ? buf.Substring(index) : buf;

			if (rest.StartsWith("Joy", StringComparison.OrdinalIgnoreCase))
			{
				var sub = rest.Substring(3);
				var val = (int?)sub.ParseLong();

				if (val.HasValue)
				{
					if (val.Value < 1 || val.Value > JoystickData.MaxJoyButtons)
						return JoyControls.Invalid;

					return JoyControls.Button1 + (val.Value - 1);
				}
			}

			if (allowOnlyButtons)
				return JoyControls.Invalid;

			if (rest.StartsWith("JoyX", StringComparison.OrdinalIgnoreCase)) return JoyControls.Xpos;

			if (rest.StartsWith("JoyY", StringComparison.OrdinalIgnoreCase)) return JoyControls.Ypos;

			if (rest.StartsWith("JoyZ", StringComparison.OrdinalIgnoreCase)) return JoyControls.Zpos;

			if (rest.StartsWith("JoyR", StringComparison.OrdinalIgnoreCase)) return JoyControls.Rpos;

			if (rest.StartsWith("JoyU", StringComparison.OrdinalIgnoreCase)) return JoyControls.Upos;

			if (rest.StartsWith("JoyV", StringComparison.OrdinalIgnoreCase)) return JoyControls.Vpos;

			if (rest.StartsWith("JoyPOV", StringComparison.OrdinalIgnoreCase)) return JoyControls.Pov;

			if (rest.StartsWith("JoyName", StringComparison.OrdinalIgnoreCase)) return JoyControls.Name;

			if (rest.StartsWith("JoyButtons", StringComparison.OrdinalIgnoreCase)) return JoyControls.Buttons;

			if (rest.StartsWith("JoyAxes", StringComparison.OrdinalIgnoreCase)) return JoyControls.Axes;

			if (rest.StartsWith("JoyInfo", StringComparison.OrdinalIgnoreCase)) return JoyControls.Info;

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
#if WINDOWS
			// Even if joystick hotkeys aren't currently allowed to fire, poll it anyway so that hotkey
			// messages can be buffered for later.
			var jie = JOYINFOEX.Default;
			var script = Script.TheScript;
			var jd = script.JoystickData;

			for (var i = 0; i < JoystickData.MaxJoysticks; ++i)
			{
				if (!script.HotkeyData.joystickHasHotkeys[i])
					continue;

				// Reset these every time in case joyGetPosEx() ever changes them. Also, most systems have only one joystick,
				// so this code will hardly ever be executed more than once (and sometimes zero times):
				jie.dwFlags = WindowsAPI.JOY_RETURNBUTTONS; // vs. JOY_RETURNALL

				if (WindowsAPI.joyGetPosEx(i, ref jie) != WindowsAPI.JOYERR_NOERROR) // Skip this joystick and try the others.
					continue;

				// The exclusive-or operator determines which buttons have changed state.  After that,
				// the bitwise-and operator determines which of those have gone from up to down (the
				// down-to-up events are currently not significant).
				var buttons_newly_down = (jie.dwButtons ^ jd.buttonsPrev[i]) & jie.dwButtons;
				jd.buttonsPrev[i] = jie.dwButtons;

				if (buttons_newly_down == 0)
					continue;

				// See if any of the joystick hotkeys match this joystick ID and one of the buttons that
				// has just been pressed on it.  If so, queue up (buffer) the hotkey events so that they will
				// be processed when messages are next checked:
				HotkeyDefinition.TriggerJoyHotkeys(i, buttons_newly_down);
			}

#elif LINUX
			LinuxJoystick.PollJoysticks();
#else
			// No joystick backend on this platform, so there is nothing to poll and joystick hotkeys never fire —
			// the same outcome as a supported platform with no joystick connected. Deliberately silent: this is the
			// polling entry point, so anything logged here would repeat at the poll interval.
#endif
		}

		internal static object ScriptGetJoyState(JoyControls joy, uint joystickID)
		// Caller must ensure that aToken.marker is a buffer large enough to handle the longest thing put into
		// it here, which is currently jc.szPname (size=32). Caller has set aToken.symbol to be SYM_STRING.
		// aToken is used for the value being returned by GetKeyState() to the script, while this function's
		// bool return value is used only by KeyWait, so is false for "up" and true for "down".
		// If there was a problem determining the position/state, aToken is made blank and false is returned.
		{
#if WINDOWS

			// Set default in case of early return.
			if (joy == JoyControls.Invalid) // Currently never called this way.
				return false; // And leave aToken set to blank.

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
					return false; // And leave aToken set to blank.

				if (joyIsButton)
					return ((jie.dwButtons >> ((int)joy - (int)JoyControls.Button1)) & 0x01) != 0;
			}

			// Otherwise:
			uint range;
			var str = "";
			var resultDouble = 0.0;  // Not initialized to help catch bugs.

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
					if (jie.dwPOV == WindowsAPI.JOY_POVCENTERED) // Need to explicitly compare against JOY_POVCENTERED because it's a WORD not a DWORD.
						return -1L;
					else
						return (long)jie.dwPOV;

				// No break since above always returns.

				case JoyControls.Name:
					return jc.szPname;

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
			} // switch()

			return resultDouble;// If above didn't return, the result should now be in result_double.
#elif LINUX
			return LinuxJoystick.ScriptGetJoyState(joy, joystickID);
#else
			// No joystick backend on this platform. Report the control as unavailable rather than throwing: this is
			// reachable from ordinary script code (GetKeyState("Joy1"), KeyWait), and false/blank is already the
			// documented contract for "couldn't determine the position/state" — the same answer the Linux backend
			// gives when the requested joystick isn't connected. Throwing here surfaced a raw .NET exception type
			// that isn't part of the script error model and couldn't be handled portably.
			LogUnsupportedOnce();
			return false;
#endif
		}

#if !WINDOWS && !LINUX
		private static int loggedUnsupported;

		/// <summary>Notes once that this platform has no joystick backend. Once, because KeyWait polls
		/// <see cref="ScriptGetJoyState"/> in a loop and would otherwise repeat the message at the sleep interval.</summary>
		private static void LogUnsupportedOnce()
		{
			if (Interlocked.Exchange(ref loggedUnsupported, 1) == 0)
				Diagnostics.Debug.WriteLine("Joystick support is not implemented on this platform; joystick controls report as unavailable.");
		}
#endif
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
		, ButtonMax = 32
	};
}