using static Keysharp.Core.WindowHelper;
using static Keysharp.Core.WindowSearch;

namespace Keysharp.Core
{
	public static partial class KeysharpEnhancements
	{

		public static object WinMaximizeAll()
		{
			DoDelayedAction(WindowManager.MaximizeAll);
			return DefaultObject;
		}

		public static long WinGetAlwaysOnTop(object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null) => (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win && win.AlwaysOnTop) ? 1L : 0L;
	}

	public static class WindowX
	{
		public static object DetectHiddenText(object mode)
		{
			var oldVal = A_DetectHiddenText;
			A_DetectHiddenText = mode;
			return oldVal;
		}

		public static object DetectHiddenWindows(object mode)
		{
			var oldVal = A_DetectHiddenWindows;
			A_DetectHiddenWindows = mode;
			return oldVal;
		}

		public static long GroupActivate(object groupName, object mode = null)
		{
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var script = Script.TheScript;

			if (script.WindowProvider.Manager.Groups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return 0L;

				var windows = SearchWindows($"ahk_group {name}");

				if (windows.Count != 0 && windows.Count == group.activated.Count)
					group.activated.Clear();

				if (windows.Count == 1 && windows[0].Handle.ToInt64() == WindowManager.GetForegroundWindowHandle().ToInt64())
					return 0L;

				if (!m.Equals(Keyword_R, StringComparison.OrdinalIgnoreCase) && !windows.Any(w => w.Active))
					windows.Reverse();

				foreach (var win in windows)
				{
					var h = win.Handle.ToInt64();

					if (!group.activated.Contains(h))
					{
						win.Active = true;
						group.activated.Push(h);
						group.lastWasDeactivate = false;
						return h;
					}
				}
			}

			return 0L;
		}

		public static object GroupAdd(object groupName,
									  object winTitle = null,
									  object winText = null,
									  object excludeTitle = null,
									  object excludeText = null)
		{
			var name = groupName.As();
			var windowGroups = TheScript.WindowProvider.Manager.Groups;

			if (string.IsNullOrEmpty(name))
				return DefaultErrorObject;

			if (!windowGroups.ContainsKey(name))
				windowGroups.Add(name, new WindowGroup());

			if (name != "AllWindows")
			{
				var group = windowGroups[name];
				group.sc.Add(SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText));
				group.activated.Clear();
				group.deactivated.Clear();
			}

			return DefaultObject;
		}

		public static object GroupClose(object groupName, object mode = null)
		{
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var mgr = Script.TheScript.WindowProvider.Manager;
			var windowGroups = mgr.Groups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return DefaultErrorObject;

				var stack = group.lastWasDeactivate ? group.deactivated : group.activated;
				var windows = SearchWindows($"ahk_group {name}");

				switch (m.ToLowerInvariant())
				{
					case Keyword_A:
						while (stack.Count != 0)
							_ = WindowManager.CreateWindow(new nint(stack.Pop())).Close();

						_ = windowGroups.Remove(name);
						break;

					case Keyword_R:
						if (stack.Count > 0)
							_ = WindowManager.CreateWindow(new nint(stack.Pop())).Close();

						if (stack.Count > 0 && !windows.Any(w => w.Active))
							_ = WindowManager.CreateWindow(new nint(stack.Peek())).Active = true;

						break;

					case "":
						if (stack.Count > 0)
							_ = WindowManager.CreateWindow(new nint(stack.Pop())).Close();

						if (stack.Count > 0)
							WindowManager.CreateWindow(new nint(stack.ToArray()[stack.Count - 1])).Active = true;

						break;
				}
			}

			return DefaultObject;
		}

		public static object GroupDeactivate(object groupName, object mode = null)
		{
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var script = Script.TheScript;
			var windowGroups = script.WindowProvider.Manager.Groups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return DefaultErrorObject;

				var windows = SearchWindows($"ahk_group {name}");
				var allwindows = WindowManager.FilterForGroups(WindowManager.AllWindows.Where(w => !windows.Any(ww => ww.Handle.ToInt64() == w.Handle.ToInt64()))).ToList();

				if (allwindows.Count != 0 && windows.Count == group.deactivated.Count)
					group.deactivated.Clear();

				if (allwindows.Count == 1 && allwindows[0].Handle.ToInt64() == WindowManager.GetForegroundWindowHandle().ToInt64())
					return DefaultObject;

				if (!m.Equals(Keyword_R, StringComparison.OrdinalIgnoreCase) && windows.Any(w => w.Active))
					allwindows.Reverse();

				foreach (var win in allwindows)
				{
					var h = win.Handle.ToInt64();

					if (!group.deactivated.Contains(h))
					{
						win.Active = true;
						group.deactivated.Push(h);
						group.lastWasDeactivate = true;
						return DefaultObject;
					}
				}
			}

			return DefaultObject;
		}

		public static object ListViewGetContent(object options = null,
												object control = null,
												object winTitle = null,
												object winText = null,
												object excludeTitle = null,
												object excludeText = null) => Script.TheScript.ControlProvider.Manager.ListViewGetContent(
														options.As(),
														control,
														winTitle,
														winText,
														excludeTitle,
														excludeText);

		public static object MenuSelect(object winTitle,
										object winText,
										object menu,
										object subMenu1 = null,
										object subMenu2 = null,
										object subMenu3 = null,
										object subMenu4 = null,
										object subMenu5 = null,
										object subMenu6 = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			Script.TheScript.ControlProvider.Manager.MenuSelect(
				winTitle,
				winText,
				menu,
				subMenu1,
				subMenu2,
				subMenu3,
				subMenu4,
				subMenu5,
				subMenu6,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object PostMessage(object msg,
										 object wparam = null,
										 object lparam = null,
										 object control = null,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			Script.TheScript.ControlProvider.Manager.PostMessage(
				msg.Aui(),
				wparam.Ai(),
				lparam.Ai(),
				control,
				winTitle,
				winText.As(),
				excludeTitle.As(),
				excludeText.As());
			return DefaultObject;
		}

		public static long SendMessage(object msg,
									   object wparam = null,
									   object lparam = null,
									   object control = null,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null,
									   object timeout = null) => Script.TheScript.ControlProvider.Manager.SendMessage(
										   msg.Aui(),
										   wparam,
										   lparam,
										   control,
										   winTitle,
										   winText.As(),
										   excludeTitle.As(),
										   excludeText.As(),
										   timeout.Ai(5000));

		public static object SetControlDelay(object obj)
		{
			var oldVal = A_ControlDelay = obj;
			A_ControlDelay = obj;
			return oldVal;
		}

		public static object SetProcessDPIAware()
		{
#if LINUX//Don't have Gtk working on Windows yet, but just in case we ever get it working.//TODO
			Environment.SetEnvironmentVariable("MONO_VISUAL_STYLES", "gtkplus");//This used to need to come first, but I'm not sure what it does now. It seems to have no effect.
			//Update: This seems to be needed to get GTK styles on Linux with Mono, but causes some tearing issues with Keyview. Need to investigate more.//TODO.
#endif
#if WINDOWS
			Application.EnableVisualStyles();

			if (!Script.dpimodeset)
			{
				Script.dpimodeset = true;

				try
				{
					Application.SetCompatibleTextRenderingDefault(false);
				}
				catch { } // Fails if a window already exists, like when running from Keyview
			}

			_ = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			//_ = Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
			return DefaultObject;
		}

		/// <summary>
		/// Sets the matching behavior of the WinTitle parameter in commands such as WinWait.
		/// This function's behavior is somewhat bizarre in that it changes which global variable gets set
		/// based on the value of the parameter passed in.
		/// </summary>
		/// <param name="obj">String or integers 1, 2, 3, or string RegEx to set TitleMatchMode, else strings fast/slow to set TitleMatchModeSpeed.</param>
		public static object SetTitleMatchMode(object matchModeSpeed)
		{
			object oldVal = null;
			var val = matchModeSpeed.As();

			if (string.Compare(val, "fast", true) == 0 || string.Compare(val, "slow", true) == 0)
			{
				oldVal = A_TitleMatchModeSpeed;
				A_TitleMatchModeSpeed = val;
			}
			else
			{
				oldVal = A_TitleMatchMode;
				A_TitleMatchMode = val;
			}

			return oldVal;
		}

		public static object SetWinDelay(object delay)
		{
			var oldVal = A_WinDelay;
			A_WinDelay = delay;
			return oldVal;
		}

		/// <summary>
		/// Retrieves the text from a standard status bar control.
		/// </summary>
		/// <param name="Part">Which part number of the bar to retrieve. Default 1, which is usually the part that contains the text of interest.</param>
		/// <param name="WinTitle">The title or partial title of the target window (the matching behavior is determined by SetTitleMatchMode).<br/>
		/// If this and the other 3 window parameters are blank or omitted, the Last Found Window will be used.<br/>
		/// If this is the letter A and the other 3 window parameters are blank or omitted, the active window will be used.<br/>
		/// To use a window class, specify ahk_class ExactClassName (shown by Window Spy).<br/>
		/// To use a process identifier (PID), specify ahk_pid %VarContainingPID%. To use a window group, specify ahk_group GroupName.<br/>
		/// To use a window's unique ID number, specify ahk_id %VarContainingID%.<br/>
		/// The search can be narrowed by specifying multiple criteria. For example: My File.txt ahk_class Notepad
		/// </param>
		/// <param name="WinText">If present, this parameter must be a substring from a single text element of the target window (as revealed by the included Window Spy utility).<br/>
		/// Hidden text elements are detected if DetectHiddenText is ON.
		/// </param>
		/// <param name="ExcludeTitle">Windows whose titles include this value will not be considered.</param>
		/// <param name="ExcludeText">Windows whose text include this value will not be considered.</param>
		/// <returns>The retrieved text</returns>
		public static string StatusBarGetText(object partNumber = null,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			WindowItemBase ctrl;
			var part = partNumber.Ai(1);
			var text = winText.As();
			var title = excludeTitle.As();
			var exclude = excludeText.As();
#if WINDOWS

			//These class names will be something else on Linux. Need a cross platform way to do this.//TODO
			if ((ctrl = SearchControl("msctls_statusbar321", winTitle, text, title, exclude, false)) != null)
			{
				var sb = StatusBarProvider.CreateStatusBar(ctrl.Handle);
				part = Math.Max(0, part - 1);

				if (part < sb.Captions.Length)
				{
					WindowItemBase.DoWinDelay();
					return sb.Captions[part];
				}
			}
			else if ((ctrl = SearchControl("WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1", winTitle, text, title, exclude, true)) != null)
			{
				if (Control.FromHandle(ctrl.Handle) is StatusStrip ss)
				{
					part = Math.Max(0, part - 1);

					if (part < ss.Items.Count)
					{
						WindowItemBase.DoWinDelay();
						return ss.Items[part].Text;
					}
				}
			}

#elif LINUX

			if ((ctrl = SearchControl("", winTitle, text, title, exclude, false)) != null)//Unsure of the exact control name on linux.//TODO
			{
				var sb = StatusBarProvider.CreateStatusBar(ctrl.Handle);
				part = Math.Max(0, part - 1);

				if (part < sb.Captions.Length)
				{
					WindowItemBase.DoWinDelay();
					return sb.Captions[part];
				}
			}

#endif
			WindowItemBase.DoWinDelay();
			return DefaultObject;
		}

		/// <summary>
		/// Waits until a window's status bar contains the specified string.
		/// </summary>
		/// <param name="BarText">
		/// <para>The text or partial text for the which the command will wait to appear. Default is blank (empty), which means to wait for the status bar to become blank. The text is case sensitive and the matching behavior is determined by SetTitleMatchMode, similar to WinTitle below.</para>
		/// <para>To instead wait for the bar's text to change, either use StatusBarGetText in a loop, or use the RegEx example at the bottom of this page.</para>
		/// </param>
		/// <param name="Seconds">The number of seconds (can contain a decimal point) to wait before timing out, in which case Accessors.A_ErrorLevel will be set to 1. Default is blank, which means wait indefinitely. Specifying 0 is the same as specifying 0.5.</param>
		/// <param name="Part">Which part number of the bar to retrieve. Default 1, which is usually the part that contains the text of interest.</param>
		/// <param name="WinTitle">The title or partial title of the target window (the matching behavior is determined by SetTitleMatchMode). If this and the other 3 window parameters are blank or omitted, the Last Found Window will be used. If this is the letter A and the other 3 window parameters are blank or omitted, the active window will be used. To use a window class, specify ahk_class ExactClassName (shown by Window Spy). To use a process identifier (PID), specify ahk_pid %VarContainingPID%. To use a window group, specify ahk_group GroupName. To use a window's unique ID number, specify ahk_id %VarContainingID%. The search can be narrowed by specifying multiple criteria. For example: My File.txt ahk_class Notepad</param>
		/// <param name="WinText">If present, this parameter must be a substring from a single text element of the target window (as revealed by the included Window Spy utility). Hidden text elements are detected if DetectHiddenText is ON.</param>
		/// <param name="Interval">How often the status bar should be checked while the command is waiting (in milliseconds), which can be an expression. Default is 50.</param>
		/// <param name="ExcludeTitle">Windows whose titles include this value will not be considered.</param>
		/// <param name="ExcludeText">Windows whose text include this value will not be considered.</param>
		public static long StatusBarWait(object barText = null,
										 object timeout = null,
										 object partNumber = null,
										 object winTitle = null,
										 object winText = null,
										 object interval = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			var bartext = barText.As();
			var seconds = timeout.Ai();
			var intvl = interval.Ai();
			var start = DateTime.UtcNow;
			var matchfound = false;

			if (intvl == 0)
				intvl = 50;

			do
			{
				var sbtext = StatusBarGetText(partNumber, winTitle, winText, excludeTitle, excludeText);

				if (sbtext == bartext)
				{
					matchfound = true;
					break;
				}

				if (seconds != 0 && (DateTime.UtcNow - start).TotalSeconds >= seconds)
					break;

				_ = Flow.Sleep(interval);
			} while (true);

			WindowItemBase.DoWinDelay();
			return matchfound ? 1 : 0;
		}

		public static object WinActivate(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
				win.Active = true;

			return DefaultObject;
		}

		public static object WinActivateBottom(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true, true) is WindowItemBase win)
				win.Active = true;

			return DefaultObject;
		}

		/// <summary>
		/// Returns the Unique ID (HWND) of the active window if it matches the specified criteria.
		/// </summary>
		/// <param name="title"></param>
		/// <param name="text"></param>
		/// <param name="excludeTitle"></param>
		/// <param name="excludeText"></param>
		/// <returns></returns>
		public static long WinActive(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			var window = WindowManager.ActiveWindow;
			return (window != null && window.Equals(criteria)) ? window.Handle.ToInt64() : 0L;
		}

		/// <summary>
		/// Closes the specified window.
		/// </summary>
		/// <param name="title"></param>
		/// <param name="text"></param>
		/// <param name="excludeTitle"></param>
		/// <param name="excludeText"></param>
		public static object WinClose(object winTitle = null,
									  object winText = null,
									  object secondsToWait = null,
									  object excludeTitle = null,
									  object excludeText = null)
		{
			var seconds = secondsToWait.Ad(double.MinValue);
			var script = Script.TheScript;
			var (windows, crit) = WindowManager.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (crit == null && string.IsNullOrEmpty(crit.Group) && windows.Count == 0 && !script.IsMainWindowClosing)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);

			foreach (var win in windows)
			{
				_ = win.Close();

				if (seconds != double.MinValue)
					_ = win.WaitClose(seconds == 0 ? 0.5 : seconds);
			}

			WindowItemBase.DoWinDelay();
			return DefaultObject;
		}

		/// <summary>
		/// Returns the Unique ID (HWND) of the first matching window (0 if none) as a hexadecimal integer.
		/// </summary>
		/// <param name="title"></param>
		/// <param name="text"></param>
		/// <param name="excludeTitle"></param>
		/// <param name="excludeText"></param>
		/// <returns></returns>
		public static long WinExist(object winTitle = null,
									object winText = null,
									object excludeTitle = null,
									object excludeText = null)
		{
			var win = SearchWindow(winTitle, winText, excludeTitle, excludeText, false);
			return win != null ? win.Handle.ToInt64() : 0;
		}

		public static string WinGetClass(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) => SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.ClassName : "";

		public static object WinGetClientPos([ByRef][Optional()][DefaultParameterValue(null)] object outX,
											 [ByRef][Optional()][DefaultParameterValue(null)] object outY,
											 [ByRef][Optional()][DefaultParameterValue(null)] object outWidth,
											 [ByRef][Optional()][DefaultParameterValue(null)] object outHeight,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
            outX ??= VarRef.Empty; outY ??= VarRef.Empty; outWidth ??= VarRef.Empty; outHeight ??= VarRef.Empty;
            object valX = Script.GetPropertyValue(outX, "__Value"), valY = Script.GetPropertyValue(outY, "__Value"), valWidth = Script.GetPropertyValue(outWidth, "__Value"), valHeight = Script.GetPropertyValue(outHeight, "__Value");
            WinPosHelper(true, ref valX, ref valY, ref valWidth, ref valHeight, winTitle, winText, excludeTitle, excludeText);
            Script.SetPropertyValue(outX, "__Value", valX); Script.SetPropertyValue(outY, "__Value", valY); Script.SetPropertyValue(outWidth, "__Value", valWidth); Script.SetPropertyValue(outHeight, "__Value", valHeight);
			return null;
		}

		public static object WinGetControls(object winTitle = null,
											object winText = null,
											object excludeTitle = null,
											object excludeText = null) =>
		WinGetControlsHelper(true, winTitle, winText, excludeTitle, excludeText);

		public static object WinGetControlsHwnd(object winTitle = null,
												object winText = null,
												object excludeTitle = null,
												object excludeText = null) =>
		WinGetControlsHelper(false, winTitle, winText, excludeTitle, excludeText);

		public static long WinGetCount(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		SearchWindows(winTitle, winText, excludeTitle, excludeText).Count;

		public static long WinGetExStyle(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.ExStyle : 0L;

		public static object WinGetID(object winTitle = null,
									  object winText = null,
									  object excludeTitle = null,
									  object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.Handle.ToInt64() : 0L;

		public static long WinGetIDLast(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			var script = Script.TheScript;
			var (windows, criteria) = WindowManager.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (windows != null && windows.Count > 0)
			{
				return windows[^1].Handle.ToInt64();
			}
			else if (!script.IsMainWindowClosing)
				return (long)Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText, DefaultErrorLong);

			return 0L;
		}

		public static Array WinGetList(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		new Array((
					  winTitle.IsNullOrEmpty()
					  && winText.IsNullOrEmpty()
					  && excludeTitle.IsNullOrEmpty()
					  && excludeText.IsNullOrEmpty()
					  ? WindowManager.AllWindows
					  : SearchWindows(winTitle, winText, excludeTitle, excludeText)).Select(item => item.Handle.ToInt64()).ToList());

		public static long WinGetMinMax(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			var val = 0L;

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				var state = win.WindowState;

				if (state == FormWindowState.Normal)
					val = 0L;
				else if (state == FormWindowState.Minimized)
					val = -1L;
				else
					val = 1L;
			}

			return val;
		}

		public static object WinGetPID(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.PID : 0L;

		public static object WinGetPos([Optional()][DefaultParameterValue(null)] object outX,
									   [Optional()][DefaultParameterValue(null)] object outY,
									   [Optional()][DefaultParameterValue(null)] object outWidth,
									   [Optional()][DefaultParameterValue(null)] object outHeight,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
            outX ??= VarRef.Empty; outY ??= VarRef.Empty; outWidth ??= VarRef.Empty; outHeight ??= VarRef.Empty;
            object valX = Script.GetPropertyValue(outX, "__Value"), valY = Script.GetPropertyValue(outY, "__Value"), valWidth = Script.GetPropertyValue(outWidth, "__Value"), valHeight = Script.GetPropertyValue(outHeight, "__Value");

            WinPosHelper(false, ref valX, ref valY, ref valWidth, ref valHeight, winTitle, winText, excludeTitle, excludeText);
            Script.SetPropertyValue(outX, "__Value", valX); Script.SetPropertyValue(outY, "__Value", valY); Script.SetPropertyValue(outWidth, "__Value", valWidth); Script.SetPropertyValue(outHeight, "__Value", valHeight);
            return null;
		}

		public static string WinGetProcessName(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.ProcessName : "";

		public static string WinGetProcessPath(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.Path : "";

		public static long WinGetStyle(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.Style : 0L;

		public static string WinGetText(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null) =>
		string.Join(Keyword_Linefeed, SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.Text : [""]);

		public static string WinGetTitle(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win ? win.Title : "";

		public static string WinGetTransColor(object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				var color = (int)win.TransparentColor.Al();
				var tempbgr = Color.FromArgb(color);
#if WINDOWS
				color = Color.FromArgb(tempbgr.A, tempbgr.B, tempbgr.G, tempbgr.R).ToArgb();
#else
				color = Color.FromArgb(tempbgr.Ab, tempbgr.Bb, tempbgr.Gb, tempbgr.Rb).ToArgb();
#endif
				return color != int.MinValue ? $"0x{color:X6}" : "";
			}

			return DefaultObject;
		}

		public static object WinGetTransparent(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				var color = win.Transparency.Al();
				return color != -1 ? color : "";
			}

			return DefaultObject;
		}

		public static object WinHide(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			DoDelayedAction(() => SearchWindows(winTitle, winText, excludeTitle, excludeText).ForEach(win => win.Hide()));
			return DefaultObject;
		}

		public static object WinKill(object winTitle = null,
									 object winText = null,
									 object secondsToWait = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var seconds = secondsToWait.Ad(double.MinValue);
			var script = Script.TheScript;
			var (windows, crit) = WindowManager.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (crit == null && string.IsNullOrEmpty(crit.Group) && windows.Count == 0 && !script.IsMainWindowClosing)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText, DefaultErrorLong);

			foreach (var win in windows)
			{
				_ = win.Kill();

				if (seconds != double.MinValue)
					_ = win.WaitClose(seconds == 0 ? 0.5 : seconds);
			}

			WindowItemBase.DoWinDelay();
			return DefaultObject;
		}

		public static object WinMaximize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			DoDelayedAction(() => SearchWindows(winTitle, winText, excludeTitle, excludeText).ForEach(win => win.WindowState = FormWindowState.Maximized));
			return DefaultObject;
		}

		public static object WinMinimize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			DoDelayedAction(() => SearchWindows(winTitle, winText, excludeTitle, excludeText).ForEach(win => win.WindowState = FormWindowState.Minimized));
			return DefaultObject;
		}

		public static object WinMinimizeAll()
		{
			DoDelayedAction(WindowManager.MinimizeAll);
			return DefaultObject;
		}

		public static object WinMinimizeAllUndo(params object[] obj)
		{
			DoDelayedAction(WindowManager.MinimizeAllUndo);
			return DefaultObject;
		}

		public static object WinMove(object x = null,
									 object y = null,
									 object width = null,
									 object height = null,
									 object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var _x = x.Ai(int.MinValue);
			var _y = y.Ai(int.MinValue);
			var w = width.Ai(int.MinValue);
			var h = height.Ai(int.MinValue);

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				if (_x != int.MinValue || _y != int.MinValue)
				{
					var loc = win.Location;

					if (_x != int.MinValue)
						loc.X = _x;

					if (_y != int.MinValue)
						loc.Y = _y;

					win.Location = loc;
				}

				if (w != int.MinValue || h != int.MinValue)
				{
					var size = win.Size;

					if (w != int.MinValue)
						size.Width = w;

					if (h != int.MinValue)
						size.Height = h;
					
					win.Size = size;
				}
				WindowItemBase.DoWinDelay();
			}

			return DefaultObject;
		}

		public static object WinMoveBottom(object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			DoDelayedAction(() => { if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win) win.Bottom = true; });
			return DefaultObject;
		}

		public static object WinMoveTop(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			DoDelayedAction(() => { if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win) win.Bottom = false; });
			return DefaultObject;
		}

		public static object WinRedraw(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
			DoDelayedAction(() => { if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win) _ = win.Redraw(); });
			return DefaultObject;
		}

		public static object WinRestore(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			DoDelayedAction(() => SearchWindows(winTitle, winText, excludeTitle, excludeText).ForEach(win => win.WindowState = FormWindowState.Normal));
			return DefaultObject;
		}

		public static object WinSetAlwaysOnTop(object newSetting,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			WinSetToggleX((win, b) => win.AlwaysOnTop = b, win => win.AlwaysOnTop, newSetting, winTitle, winText, excludeTitle, excludeText);
			return DefaultObject;
		}

		public static object WinSetEnabled(object newSetting,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			WinSetToggleX((win, b) => win.Enabled = b, win => win.Enabled, newSetting, winTitle, winText, excludeTitle, excludeText);
			return DefaultObject;
		}

		public static object WinSetExStyle(object value,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			WinSetStyleHelper(true, value, winTitle, winText, excludeTitle, excludeText);
			return DefaultObject;
		}

#if WINDOWS
		public static object WinSetRegion(object options,
										  object winTitle = null,
										  object winText = null,
										  object excludeTitle = null,
										  object excludeText = null)
		{
			var opts = options.As();

			if (!(SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win))
				return DefaultObject;

			var w = int.MinValue;
			var h = int.MinValue;
			var rw = 30;
			var rh = 30;
			var ellipse = false;
			var wind = false;
			var points = new List<POINT>(16);

			foreach (Range r in opts.AsSpan().SplitAny(SpaceTabSv))
			{
				var tempstr = "";
				var opt = opts.AsSpan(r).Trim();

				if (Options.TryParse(opt, "w", ref w)) { }
				else if (Options.TryParse(opt, "h", ref h)) { }
				else if (opt.Equals("e", StringComparison.OrdinalIgnoreCase)) { ellipse = true; }
				else if (opt.Equals("Wind", StringComparison.OrdinalIgnoreCase)) { wind = true; }
				else if (Options.TryParseString(opt, "r", ref tempstr))
				{
					var splits = tempstr.Split('-', StringSplitOptions.None);
					var vals = Conversions.ParseRange(splits);

					if (vals.Count > 0)
						rw = vals[0];

					if (vals.Count > 1)
						rh = vals[1];
				}
				else if (opt.Contains('-'))
				{
					var splits = opt.ToString().Split('-', StringSplitOptions.None);
					var vals = Conversions.ParseRange(splits);

					if (vals.Count > 1)
						points.Add(new POINT(vals[0], vals[1]));
				}
			}

			nint hrgn = 0;

			if (points.Count == 0)
			{
				if (WindowsAPI.SetWindowRgn(win.Handle, 0, true) == 0)
					return Errors.OSErrorOccurred("", $"Could not reset window region with criteria: title: {winTitle}, text: {winText}, exclude title: {excludeTitle}, exclude text: {excludeText}");

				WindowItemBase.DoWinDelay();
				return DefaultObject;
			}
			else if (w != int.MinValue && h != int.MinValue)
			{
				w += points[0].X;//Make width become the right side of the rect.
				h += points[0].Y;//Make height become the bottom.

				if (ellipse)
					hrgn = WindowsAPI.CreateEllipticRgn(points[0].X, points[0].Y, w, h);
				else if (rw != int.MinValue)
					hrgn = WindowsAPI.CreateRoundRectRgn(points[0].X, points[0].Y, w, h, rw, rh);
				else
					hrgn = WindowsAPI.CreateRectRgn(points[0].X, points[0].Y, w, h);
			}
			else
				hrgn = WindowsAPI.CreatePolygonRgn(points.Select(p => new POINT { X = p.X, Y = p.Y }).ToArray(), points.Count, wind ? WindowsAPI.WINDING : WindowsAPI.ALTERNATE);

			if (hrgn != 0)
			{
				if (WindowsAPI.SetWindowRgn(win.Handle, hrgn, true) == 0)
				{
					_ = WindowsAPI.DeleteObject(hrgn);
					return Errors.OSErrorOccurred("", $"Could not set region for window with criteria: title: {winTitle}, text: {winText}, exclude title: {excludeTitle}, exclude text: {excludeText}");
				}
			}
			else
				return Errors.ValueErrorOccurred($"Could not create region for window with criteria: title: {winTitle}, text: {winText}, exclude title: {excludeTitle}, exclude text: {excludeText}");

			WindowItemBase.DoWinDelay();
			return DefaultObject;
		}
#endif
		public static object WinSetStyle(object value,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			WinSetStyleHelper(false, value, winTitle, winText, excludeTitle, excludeText);
			return DefaultObject;
		}

		public static object WinSetTitle(object newTitle,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				win.Title = newTitle.As();
				WindowItemBase.DoWinDelay();
			}

			return DefaultObject;
		}

		public static object WinSetTransColor(object color,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				win.TransparentColor = color;
				WindowItemBase.DoWinDelay();
			}

			return DefaultObject;
		}

		public static object WinSetTransparent(object n,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				win.Transparency = n;
				WindowItemBase.DoWinDelay();
			}

			return DefaultObject;
		}

		public static object WinShow(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var tv = Script.TheScript.Threads.CurrentThread.configData;
			var prev = tv.detectHiddenWindows;
			tv.detectHiddenWindows = true;
			SearchWindows(winTitle, winText, excludeTitle, excludeText).ForEach(win => win.Show());
			tv.detectHiddenWindows = prev;
			WindowItemBase.DoWinDelay();
			return DefaultObject;
		}

		public static long WinWait(object winTitle = null,
								   object winText = null,
								   object timeout = null,
								   object excludeTitle = null,
								   object excludeText = null)
		{
			var seconds = timeout.Ad();
			WindowItemBase win;
			var start = DateTime.UtcNow;

			do
			{
				win = SearchWindow(winTitle, winText, excludeTitle, excludeText, false, false, true);

				if (win != null || (seconds != 0 && (DateTime.UtcNow - start).TotalSeconds >= seconds))
					break;

				_ = Flow.Sleep(50);
			} while (win == null);

			if (win != null)
				WindowManager.LastFound = win;

			WindowItemBase.DoWinDelay();
			return win != null ? win.Handle.ToInt64() : 0L;
		}

		public static long WinWaitActive(object winTitle = null,
										 object winText = null,
										 object timeout = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			var b = false;
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			var hwnd = 0L;
			var script = Script.TheScript;

			while (!b && (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds))
			{
				var windows = WindowManager.FindWindowGroup(criteria, true);//Pass true because we must inspect all matching windows to see if any of them are active.

				foreach (var win in windows)
				{
					if (win.Active)
					{
						WindowManager.LastFound = win;
						hwnd = win.Handle.ToInt64();
						b = true;
						break;
					}
				}

				if (!b)
					_ = Flow.Sleep(100);
			}

			WindowItemBase.DoWinDelay();
			return hwnd;
		}

		public static long WinWaitClose(object winTitle = null,
										object winText = null,
										object timeout = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			var b = false;
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;
			var script = Script.TheScript;
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			var windows = WindowManager.FindWindowGroup(criteria, false, true);

			if (windows.Count == 0)
				return 1L;

			foreach (var win in windows)//In the case of WinWaitClose(), this loop won't execute and the function will return 1.
			{
				WindowManager.LastFound = win;

				while (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds)
				{
					if (!win.Exists || (!ThreadAccessors.A_DetectHiddenWindows && !win.Visible))
					{
						b = true;
						break;
					}
					else
						_ = Flow.Sleep(100);
				}
			}

			WindowItemBase.DoWinDelay();
			return b ? 1L : 0L;
		}

		public static long WinWaitNotActive(object winTitle = null,
											object winText = null,
											object timeout = null,
											object excludeTitle = null,
											object excludeText = null)
		{
			var b = false;
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowItemBase win)
			{
				//KeysharpEnhancements.OutputDebugLine($"The window to wait for is: {win.Handle.ToInt64()}, {win.Title}");
				//Keysharp.Core.File.FileAppend($"The window to wait for is: {win.Handle.ToInt64()}, {win.Title}\n", "out.txt");
				WindowManager.LastFound = win;

				while (!b && (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds))
				{
					if (!win.Active)
					{
						b = true;
						break;
					}

					_ = Flow.Sleep(100);
				}
			}

			WindowItemBase.DoWinDelay();
			return b ? 1L : 0L;
		}

#if LINUX
		[PublicHiddenFromUser]
		public static long zzzLinuxTester(params object[] obj)
		{
			return 1L;
		}
#endif
	}
}