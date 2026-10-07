using static Keysharp.Builtins.WindowSearch;

namespace Keysharp.Builtins
{
	internal static class WindowHelper
	{
		internal static void EnsureWindowMonitoringPermission(string operation)
			=> _ = Script.TheScript.Permissions.EnsureWindowMonitoring(operation: operation);

		// Through the shared Ensure gate, which lets an uninstalled component degrade to the empty result its
		// backend returns -- WinActivate then reports the window it could not find -- and errors only on a refusal.
		internal static void EnsureWindowControlPermission(string operation)
			=> _ = Script.TheScript.Permissions.EnsureCapabilities(
				windowMonitoring: true,
				windowControl: true,
				operation: operation);

		internal static object WindowOperationUnsupported(string commandName)
			=> Errors.UnsupportedErrorOccurred($"{commandName} is unsupported for this target on {WindowOperationPlatformName()}.");

		private static string WindowOperationPlatformName()
		{
#if WINDOWS
			return "Windows";
#elif LINUX
			return Platform.Desktop.IsWaylandSession ? "Linux/Wayland" : "Linux";
#elif OSX
			return "macOS";
#else
			return "this platform";
#endif
		}

		/// <summary>
		/// The handle a Control parameter gives, directly or as an object's Hwnd; <paramref name="parsed"/> is false
		/// for a ClassNN or text. False when reading the Hwnd raised an error the script continued.
		/// </summary>
		internal static bool TryCtrlTonint(object ctrl, out bool parsed, out nint handle)
		{
			parsed = false;
			handle = 0;

			if (ctrl is long l)
			{
				parsed = true;
				handle = new nint(l);
			}
			else if (ctrl != null && ctrl is not string)
			{
				var hwnd = Script.GetPropertyValueOrNull(ctrl, "Hwnd");

				if (hwnd == null)
				{
					_ = Errors.PropertyErrorOccurred($"Object did not have an Hwnd property.");
					return false;
				}

				if (hwnd is not long ll)
				{
					_ = Errors.TypeErrorOccurred(hwnd, typeof(long));
					return false;
				}

				parsed = true;
				handle = new nint(ll);
			}

			return true;
		}

		internal static void DoDelayedAction(Action act)
		{
			EnsureWindowControlPermission("window operation");
			act();
			WindowInfoBase.DoWinDelay();
		}

		// Same as DoDelayedAction but WITHOUT a trailing A_WinDelay, for the window functions AHK does not delay
		// (WinMoveTop, WinMoveBottom, WinRedraw — see win.cpp WinMoveTopBottom/WinRedraw, neither calls DoWinDelay).
		internal static void DoAction(Action act)
		{
			EnsureWindowControlPermission("window operation");
			act();
		}

		internal static T DoDelayedFunc<T>(Func<T> func)
		{
			var val = func();
			WindowInfoBase.DoWinDelay();
			return val;
		}

		internal static void WinPosHelper(bool client,
										  ref object outX,
										  ref object outY,
										  ref object outWidth,
										  ref object outHeight,
										  object winTitle,
										  object winText,
										  object excludeTitle,
										  object excludeText)
		{
			//DoDelayedFunc(() =>
			{
				if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
				{
					if (!win.TryGetBounds(client, out var rect))
					{
						_ = Errors.UnsupportedErrorOccurred($"{(client ? "WinGetClientPos" : "WinGetPos")} is unavailable: the compositor does not expose {(client ? "client" : "frame")} geometry for this window.");
						outX = outY = outWidth = outHeight = 0L;
						return;
					}

					outX = (long)rect.Left;
					outY = (long)rect.Top;
					outWidth  = (long)rect.Width;
					outHeight = (long)rect.Height;
				}
				else
				{
					outX = 0L;
					outY = 0L;
					outWidth = 0L;
					outHeight = 0L;
				}
			}//);
		}

		internal static void WinSetStyleHelper(bool ex,
											   object value,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureWindowControlPermission("window style operation");
			var function = ex ? "WinSetExStyle" : "WinSetStyle";

			// No A_WinDelay: AHK's WinSetStyle/WinSetExStyle do not call DoWinDelay.
			if (StyleChange.TryParse(value, function, out var change)
					&& SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
				SetStyle(win, ex, change, function);
		}

		/// <summary>Applies a style value to a window or a control, as AutoHotkey's one WinSetStyle serves both.</summary>
		internal static void SetStyle(WindowInfoBase win, bool ex, StyleChange change, string function)
		{
			var set = ex ? Platform.Window.TrySetExStyle(win.Handle, change.ApplyTo(win.ExStyle))
					  : Platform.Window.TrySetStyle(win.Handle, change.ApplyTo(win.Style));

			if (!set)
				_ = WindowOperationUnsupported(function);
		}

		/// <summary>
		/// The value of WinSetStyle, ControlSetStyle or their Ex forms, read as AutoHotkey reads it: a number replaces the
		/// style, and +, - or ^ before one adds, removes or toggles its bits.
		/// </summary>
		internal readonly record struct StyleChange(char Operator, long Bits)
		{
			/// <summary>False after raising for a blank value, or for one that does not convert to text.</summary>
			internal static bool TryParse(object value, string function, out StyleChange change)
			{
				change = default;

				if (!value.CoerceString(out var text))
					return false;

				if (text.Length == 0)
				{
					_ = Errors.InvalidParameterErrorOccurred(1, function, value);
					return false;
				}

				var op = text[0] is '+' or '-' or '^' ? text[0] : '\0';
				// A 32-bit style word, as AutoHotkey's ATOU reads it: "+-1" adds 0xFFFFFFFF.
				change = new StyleChange(op, unchecked((uint)Strings.Atoi(text.AsSpan(op == '\0' ? 0 : 1))));
				return true;
			}

			internal long ApplyTo(long current) => Operator switch
			{
				'+' => current | Bits,
				'-' => current & ~Bits,
				'^' => current ^ Bits,
				_ => Bits
			};
		}

		internal static void WinSetToggleX(Func<WindowInfoBase, bool, bool> set, Func<WindowInfoBase, bool> get,
										   object value,
										   string commandName,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureWindowControlPermission("window toggle operation");
			var val = Conversions.ConvertOnOffToggle(value);

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				var supported = true;

				if (val == ToggleValueType.Off)
					supported = set(win, false);
				else if (val == ToggleValueType.On)
					supported = set(win, true);
				else if (val == ToggleValueType.Toggle)
					supported = set(win, !get(win));

				if (!supported)
					_ = WindowOperationUnsupported(commandName);

				// No A_WinDelay: AHK's WinSetAlwaysOnTop/WinSetEnabled do not call DoWinDelay.
			}
		}
	}
}
