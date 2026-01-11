using static Keysharp.Core.WindowSearch;

namespace Keysharp.Core
{
	internal static class WindowHelper
	{
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
				object hwnd = null;

				try
				{
					hwnd = Script.GetPropertyValue(ctrl, "Hwnd");
				}
				catch { }

				nint ptr = 0;

				if (hwnd is long ll)
					ptr = new nint(ll);
				else
				{
					_ = Errors.ValueErrorOccurred($"Invalid hWnd property type {hwnd.GetType().Name}");
					return (false, 0);
				}

				return (true, ptr);
			}

			return (false, 0);
		}

		internal static void DoDelayedAction(Action act)
		{
			act();
			WindowItemBase.DoWinDelay();
		}

		internal static T DoDelayedFunc<T>(Func<T> func)
		{
			var val = func();
			WindowItemBase.DoWinDelay();
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
				if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
				{
					var rect = client ? win.ClientLocation : win.Location;

					if (client)
					{
						var pt = win.ClientToScreen();
						outX = (long)(rect.Left + pt.X);
						outY = (long)(rect.Top + pt.Y);
					}
					else
					{
						outX = (long)rect.Left;
						outY = (long)rect.Top;
					}

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
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				var val = value;

				if (ex)
				{
					/*  if (val is int i)
					    win.ExStyle = i;
					    else if (val is uint ui)
					    win.ExStyle = ui;
					    else*/ if (val is long l)
						win.ExStyle = l;
					else if (val is double d)
						win.ExStyle = (long)d;
					else if (val is string s)
					{
						long temp = 0;

						if (Options.TryParse(s, "+", ref temp)) { win.ExStyle |= temp; }
						else if (Options.TryParse(s, "-", ref temp)) { win.ExStyle &= ~temp; }
						else if (Options.TryParse(s, "^", ref temp)) { win.ExStyle ^= temp; }
						else win.ExStyle = val.ParseLong(true).Value;
					}
				}
				else
				{
					/*  if (val is int i)
					    win.Style = i;
					    else if (val is uint ui)
					    win.Style = ui;
					    else*/ if (val is long l)
						win.Style = l;
					else if (val is double d)
						win.Style = (long)d;
					else if (val is string s)
					{
						long temp = 0;

						if (Options.TryParse(s, "+", ref temp)) { win.Style |= temp; }
						else if (Options.TryParse(s, "-", ref temp)) { win.Style &= ~temp; }
						else if (Options.TryParse(s, "^", ref temp)) { win.Style ^= temp; }
						else win.Style = val.ParseLong(true).Value;
					}
				}

				WindowItemBase.DoWinDelay();
			}
		}

		internal static void WinSetToggleX(Action<WindowItemBase, bool> set, Func<WindowItemBase, bool> get,
										   object value,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			var val = value.Ai();

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				if (val == 0)
					set(win, false);
				else if (val == 1)
					set(win, true);
				else if (val == -1)
					set(win, !get(win));

				WindowItemBase.DoWinDelay();
			}
		}
	}
}