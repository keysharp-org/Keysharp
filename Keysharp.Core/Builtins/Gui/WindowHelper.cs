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
			=> Errors.UnsupportedErrorOccurred($"{commandName} is not implemented on {WindowOperationPlatformName()}.");

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

		internal static (bool, nint) CtrlTonint(object ctrl)
		{
			if (ctrl == null)
			{
				return (false, 0);
			}
			else if (ctrl is long l)
			{
				return (true, new nint(l));
			}
			else if (!(ctrl is string))
			{
				var hwnd = Script.GetPropertyValueOrNull(ctrl, "Hwnd");

				if (hwnd == null)
				{
					_ = Errors.PropertyErrorOccurred($"Object did not have an Hwnd property.");
					return (false, 0);
				}

				if (hwnd is long ll)
					return (true, new nint(ll));

				_ = Errors.TypeErrorOccurred(hwnd, typeof(long));
				return (false, 0);
			}

			return (false, 0);
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
#if LINUX
					var wayland = win as Keysharp.Internals.Window.Linux.Wayland.WaylandWindowInfo;

					if (wayland == null && Platform.Desktop.IsWaylandSession
						&& Control.FromHandle(win.Handle) is Form form)
					{
						var size = form.GetSize();
						if (Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.TryGetCompositorHandle(
								form, form.Title, size.Width, size.Height, out var compositorHandle)
							&& Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Current is { } backend)
							_ = backend.TryGetWindow(compositorHandle, out wayland);
					}

					if (wayland != null && !wayland.HasKnownField(client
							? Keysharp.Internals.Window.Linux.Wayland.WaylandWindowFields.Client
							: Keysharp.Internals.Window.Linux.Wayland.WaylandWindowFields.Frame))
					{
						_ = Errors.UnsupportedErrorOccurred($"{(client ? "WinGetClientPos" : "WinGetPos")} is unavailable: the compositor does not expose {(client ? "client" : "frame")} geometry for this window.");
						outX = outY = outWidth = outHeight = 0L;
						return;
					}
#endif
					// Both ClientBounds and Bounds are screen-relative rectangles on every platform (ClientBounds
					// takes its origin from ClientToScreen()), so report them directly.
					var rect = client ? win.ClientBounds : win.Bounds;
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
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				var val = value;

				if (ex)
				{
					var exVal = win.ExStyle;

					if (val is long l)
						exVal = l;
					else if (val is double d)
						exVal = (long)d;
					else if (val is string s)
					{
						long temp = 0;

						if (Options.TryParse(s, "+", ref temp)) { exVal |= temp; }
						else if (Options.TryParse(s, "-", ref temp)) { exVal &= ~temp; }
						else if (Options.TryParse(s, "^", ref temp)) { exVal ^= temp; }
						else exVal = val.Al();
					}

					if (!Platform.Window.TrySetExStyle(win.Handle, exVal))
						_ = WindowOperationUnsupported("WinSetExStyle");
				}
				else
				{
					var stVal = win.Style;

					if (val is long l)
						stVal = l;
					else if (val is double d)
						stVal = (long)d;
					else if (val is string s)
					{
						long temp = 0;

						if (Options.TryParse(s, "+", ref temp)) { stVal |= temp; }
						else if (Options.TryParse(s, "-", ref temp)) { stVal &= ~temp; }
						else if (Options.TryParse(s, "^", ref temp)) { stVal ^= temp; }
						else stVal = val.ParseLong().Value;
					}

					if (!Platform.Window.TrySetStyle(win.Handle, stVal))
						_ = WindowOperationUnsupported("WinSetStyle");
				}
				// No A_WinDelay: AHK's WinSetStyle/WinSetExStyle do not call DoWinDelay.
			}
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
