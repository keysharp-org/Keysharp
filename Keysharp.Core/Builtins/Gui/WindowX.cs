using static Keysharp.Builtins.WindowHelper;
using static Keysharp.Builtins.WindowSearch;

namespace Keysharp.Builtins
{
	public partial class Ks
	{

		public static object WinMaximizeAll()
		{
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var window in WindowQuery.AllWindows)
					if (!Platform.Window.TrySetState(window.Handle, FormWindowState.Maximized))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMaximizeAll));

			return DefaultObject;
		}

		/// <summary>
		/// Returns the handle of the window located at the given screen coordinates.
		/// </summary>
		/// <param name="x">The X screen coordinate.</param>
		/// <param name="y">The Y screen coordinate.</param>
		/// <returns>The window handle at the specified point, or 0 if none is found.</returns>
		public static long WinFromPoint(object x = null, object y = null)
		{
			EnsureWindowMonitoringPermission("WinFromPoint");

			if (x == null || y == null)
			{
				GetCursorPos(out var point);
				x ??= point.X; y ??= point.Y;
			}
			return WindowQuery.WindowFromPoint(new POINT(x.Ai(), y.Ai()))?.Handle ?? 0L;
		}
	}

	public static class WindowX
	{
		public static object DetectHiddenText(object setting)
		{
			var oldVal = A_DetectHiddenText;
			A_DetectHiddenText = setting;
			return oldVal;
		}

		public static object DetectHiddenWindows(object setting)
		{
			var oldVal = A_DetectHiddenWindows;
			A_DetectHiddenWindows = setting;
			return oldVal;
		}

		public static long GroupActivate(object groupName, object mode = null)
		{
			EnsureWindowControlPermission("GroupActivate");
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var script = Script.TheScript;

			if (script.WindowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return 0L;

				var windows = SearchWindows($"ahk_group {name}");

				if (windows.Count != 0 && windows.Count == group.activated.Count)
					group.activated.Clear();

				if (windows.Count == 1 && windows[0].Handle.ToInt64() == WindowQuery.GetForegroundWindowHandle().ToInt64())
					return 0L;

				if (!m.Equals(Keyword_R, StringComparison.OrdinalIgnoreCase) && !windows.Any(w => w.Active))
					windows.Reverse();

				foreach (var win in windows)
				{
					var h = win.Handle.ToInt64();

					if (!group.activated.Contains(h))
					{
						Platform.Window.TryActivate(win.Handle);
						group.activated.Push(h);
						group.lastWasDeactivate = false;
						WindowInfoBase.DoWinDelay();
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
			var windowGroups = TheScript.WindowGroups;

			if (string.IsNullOrEmpty(name))
				return Errors.ValueErrorOccurred("Group name must not be empty.");

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
			EnsureWindowControlPermission("GroupClose");
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var windowGroups = Script.TheScript.WindowGroups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return DefaultObject;

				var stack = group.lastWasDeactivate ? group.deactivated : group.activated;
				var windows = SearchWindows($"ahk_group {name}");

				switch (m)
				{
					case var x when x.Equals(Keyword_A, StringComparison.OrdinalIgnoreCase):
						while (stack.Count != 0)
							_ = Platform.Window.TryClose(new nint(stack.Pop()));

						_ = windowGroups.Remove(name);
						break;

					case var x when x.Equals(Keyword_R, StringComparison.OrdinalIgnoreCase):
						if (stack.Count > 0)
							_ = Platform.Window.TryClose(new nint(stack.Pop()));

						if (stack.Count > 0 && !windows.Any(w => w.Active))
						{
							_ = Platform.Window.TryActivate(new nint(stack.Peek()));
							WindowInfoBase.DoWinDelay();
						}

						break;

					case "":
						if (stack.Count > 0)
							_ = Platform.Window.TryClose(new nint(stack.Pop()));

						if (stack.Count > 0)
						{
							_ = Platform.Window.TryActivate(new nint(stack.ToArray()[stack.Count - 1]));
							WindowInfoBase.DoWinDelay();
						}

						break;
				}
			}

			return DefaultObject;
		}

		public static object GroupDeactivate(object groupName, object mode = null)
		{
			EnsureWindowControlPermission("GroupDeactivate");
			var name = groupName.As().ToLowerInvariant();
			var m = mode.As();
			var script = Script.TheScript;
			var windowGroups = script.WindowGroups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0)
					return DefaultObject;

				var windows = SearchWindows($"ahk_group {name}");
				var allwindows = WindowQuery.FilterForGroups(WindowQuery.AllWindows.Where(w => !windows.Any(ww => ww.Handle.ToInt64() == w.Handle.ToInt64()))).ToList();

				if (allwindows.Count != 0 && windows.Count == group.deactivated.Count)
					group.deactivated.Clear();

				if (allwindows.Count == 1 && allwindows[0].Handle.ToInt64() == WindowQuery.GetForegroundWindowHandle().ToInt64())
					return DefaultObject;

				if (!m.Equals(Keyword_R, StringComparison.OrdinalIgnoreCase) && windows.Any(w => w.Active))
					allwindows.Reverse();

				foreach (var win in allwindows)
				{
					var h = win.Handle.ToInt64();

					if (!group.deactivated.Contains(h))
					{
						Platform.Window.TryActivate(win.Handle);
						group.deactivated.Push(h);
						group.lastWasDeactivate = true;
						WindowInfoBase.DoWinDelay();
						return DefaultObject;
					}
				}
			}

			return DefaultObject;
		}

		public static object ListViewGetContent(object options = null,
												object controlID = null,
												object winTitle = null,
												object winText = null,
												object excludeTitle = null,
												object excludeText = null) => Platform.Control.ListViewGetContent(
														options.As(),
														controlID,
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
			EnsureWindowControlPermission("MenuSelect");
			Platform.Control.MenuSelect(
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

		public static object PostMessage(object msgNumber,
										 object wParam = null,
										 object lParam = null,
										 object controlID = null,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureWindowControlPermission("PostMessage");
			Platform.Control.PostMessage(
				msgNumber.Aui(),
				wParam.Ai(),
				lParam.Ai(),
				controlID,
				winTitle,
				winText.As(),
				excludeTitle.As(),
				excludeText.As());
			return DefaultObject;
		}

		public static long SendMessage(object msgNumber,
									   object wParam = null,
									   object lParam = null,
									   object controlID = null,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null,
									   object timeout = null)
		{
			EnsureWindowControlPermission("SendMessage");
			return Platform.Control.SendMessage(
										   msgNumber.Aui(),
										   wParam,
										   lParam,
										   controlID,
										   winTitle,
										   winText.As(),
										   excludeTitle.As(),
										   excludeText.As(),
										   timeout.Ai(5000));
		}

		public static object SetControlDelay(object delay)
		{
			var oldVal = A_ControlDelay = delay;
			A_ControlDelay = delay;
			return oldVal;
		}

		internal static object SetProcessDPIAware()
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
		/// <param name="matchMode">String or integers 1, 2, 3, or string RegEx to set TitleMatchMode, else strings fast/slow to set TitleMatchModeSpeed.</param>
		public static object SetTitleMatchMode(object matchMode)
		{
			object oldVal = null;
			var val = matchMode.As();

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
		/// <param name="partNumber">Which part number of the bar to retrieve. Default 1, which is usually the part that contains the text of interest.</param>
		/// <param name="winTitle">The title or partial title of the target window (the matching behavior is determined by SetTitleMatchMode).<br/>
		/// If this and the other 3 window parameters are blank or omitted, the Last Found Window will be used.<br/>
		/// If this is the letter A and the other 3 window parameters are blank or omitted, the active window will be used.<br/>
		/// To use a window class, specify ahk_class ExactClassName (shown by Window Spy).<br/>
		/// To use a process identifier (PID), specify ahk_pid %VarContainingPID%. To use a window group, specify ahk_group GroupName.<br/>
		/// To use a window's unique ID number, specify ahk_id %VarContainingID%.<br/>
		/// The search can be narrowed by specifying multiple criteria. For example: My File.txt ahk_class Notepad
		/// </param>
		/// <param name="winText">If present, this parameter must be a substring from a single text element of the target window (as revealed by the included Window Spy utility).<br/>
		/// Hidden text elements are detected if DetectHiddenText is ON.
		/// </param>
		/// <param name="excludeTitle">Windows whose titles include this value will not be considered.</param>
		/// <param name="excludeText">Windows whose text include this value will not be considered.</param>
		/// <returns>The retrieved text</returns>
		public static string StatusBarGetText(object partNumber = null,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			var part = Math.Max(0, partNumber.Ai(1) - 1);
			var text = winText.As();
			var title = excludeTitle.As();
			var exclude = excludeText.As();
#if WINDOWS
			// Standard Win32 common-control status bar (class "msctls_statusbar32", first instance).
			WindowInfoBase ctrl;

			if ((ctrl = SearchControl("msctls_statusbar321", winTitle, text, title, exclude, false)) != null)
			{
				var sb = Platform.StatusBar.CreateStatusBar(ctrl.Handle);

				if (part < sb.Captions.Length)
					return sb.Captions[part];

				return DefaultObject;
			}

			// Keysharp / WinForms StatusStrip fallback (same process).
			if ((ctrl = SearchControl("WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1", winTitle, text, title, exclude, false)) != null
				&& Control.FromHandle(ctrl.Handle) is StatusStrip ss)
			{
				if (part < ss.Items.Count)
					return ss.Items[part].Text;

				return DefaultObject;
			}

			_ = Errors.TargetErrorOccurred("Window does not contain a standard status bar.", winTitle, text, title, exclude);
			return DefaultObject;
#else
			// Reading a status bar from another process on Linux/macOS would require AT-SPI
			// (or similar accessibility) support, which Keysharp does not currently implement.
			_ = Errors.TargetErrorOccurred("StatusBarGetText is not supported on this platform.", winTitle, text, title, exclude);
			return DefaultObject;
#endif
		}

		/// <summary>
		/// Waits until a window's status bar contains the specified string.
		/// </summary>
		/// <param name="barText">
		/// <para>The text or partial text for the which the command will wait to appear. Default is blank (empty), which means to wait for the status bar to become blank. The text is case sensitive and the matching behavior is determined by SetTitleMatchMode, similar to WinTitle below.</para>
		/// <para>To instead wait for the bar's text to change, either use StatusBarGetText in a loop, or use the RegEx example at the bottom of this page.</para>
		/// </param>
		/// <param name="timeout">The number of seconds (can contain a decimal point) to wait before timing out, in which case Accessors.A_ErrorLevel will be set to 1. Default is blank, which means wait indefinitely. Specifying 0 is the same as specifying 0.5.</param>
		/// <param name="partNumber">Which part number of the bar to retrieve. Default 1, which is usually the part that contains the text of interest.</param>
		/// <param name="winTitle">The title or partial title of the target window (the matching behavior is determined by SetTitleMatchMode). If this and the other 3 window parameters are blank or omitted, the Last Found Window will be used. If this is the letter A and the other 3 window parameters are blank or omitted, the active window will be used. To use a window class, specify ahk_class ExactClassName (shown by Window Spy). To use a process identifier (PID), specify ahk_pid %VarContainingPID%. To use a window group, specify ahk_group GroupName. To use a window's unique ID number, specify ahk_id %VarContainingID%. The search can be narrowed by specifying multiple criteria. For example: My File.txt ahk_class Notepad</param>
		/// <param name="winText">If present, this parameter must be a substring from a single text element of the target window (as revealed by the included Window Spy utility). Hidden text elements are detected if DetectHiddenText is ON.</param>
		/// <param name="interval">How often the status bar should be checked while the command is waiting (in milliseconds), which can be an expression. Default is 50.</param>
		/// <param name="excludeTitle">Windows whose titles include this value will not be considered.</param>
		/// <param name="excludeText">Windows whose text include this value will not be considered.</param>
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

			WindowInfoBase.DoWinDelay();
			return matchfound ? 1 : 0;
		}

		public static object WinActivate(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureWindowControlPermission("WinActivate");
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (!Platform.Window.TryActivate(win.Handle))
					return WindowOperationUnsupported(nameof(WinActivate));
			}

			WindowInfoBase.DoWinDelay();
			return DefaultObject;
		}

		public static object WinActivateBottom(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureWindowControlPermission("WinActivateBottom");
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true, true) is WindowInfoBase win)
			{
				if (!Platform.Window.TryActivate(win.Handle))
					return WindowOperationUnsupported(nameof(WinActivateBottom));
			}

			WindowInfoBase.DoWinDelay();
			return DefaultObject;
		}

		/// <summary>
		/// Returns the Unique ID (HWND) of the active window if it matches the specified criteria.
		/// </summary>
		/// <param name="winTitle"></param>
		/// <param name="winText"></param>
		/// <param name="excludeTitle"></param>
		/// <param name="excludeText"></param>
		/// <returns></returns>
		public static long WinActive(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			var window = SearchActiveWindow(criteria, true);
			return window != null ? window.Handle.ToInt64() : 0L;
		}

		/// <summary>
		/// Closes the specified window.
		/// </summary>
		/// <param name="winTitle"></param>
		/// <param name="winText"></param>
		/// <param name="secondsToWait"></param>
		/// <param name="excludeTitle"></param>
		/// <param name="excludeText"></param>
		public static object WinClose(object winTitle = null,
									  object winText = null,
									  object secondsToWait = null,
									  object excludeTitle = null,
									  object excludeText = null)
		{
			EnsureWindowControlPermission("WinClose");
			var seconds = secondsToWait.Ad(double.MinValue);
			var script = Script.TheScript;
			var (windows, crit) = WindowQuery.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (crit != null && string.IsNullOrEmpty(crit.Group) && windows.Count == 0 && !script.IsTearingDown)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText);

			var unsupported = false;

			foreach (var win in windows)
			{
				if (!Platform.Window.TryClose(win.Handle))
				{
					unsupported = true;
					continue;
				}

				if (seconds != double.MinValue)
					_ = win.WaitClose(seconds == 0 ? 0.5 : seconds);
			}

			WindowInfoBase.DoWinDelay();
			if (unsupported)
				return WindowOperationUnsupported(nameof(WinClose));

			return DefaultObject;
		}

		/// <summary>
		/// Returns the Unique ID (HWND) of the first matching window (0 if none) as a hexadecimal integer.
		/// </summary>
		/// <param name="winTitle"></param>
		/// <param name="winText"></param>
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
										 object excludeText = null) => SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.ClassName : "";

		public static object WinGetClientPos([ByRef] object outX = null,
											 [ByRef] object outY = null,
											 [ByRef] object outWidth = null,
											 [ByRef] object outHeight = null,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
            object valX = null, valY = null, valWidth = null, valHeight = null;
			WinPosHelper(true, ref valX, ref valY, ref valWidth, ref valHeight, winTitle, winText, excludeTitle, excludeText);
            if (outX != null) Refs.SetValue(outX, valX);
			if (outY != null) Refs.SetValue(outY, valY);
			if (outWidth != null) Refs.SetValue(outWidth, valWidth);
			if (outHeight != null) Refs.SetValue(outHeight, valHeight);
			return DefaultObject;
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
		WinGetControlsHelper(false, winTitle, winText, excludeTitle, excludeText) ?? DefaultObject;

		public static long WinGetCount(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		SearchWindows(winTitle, winText, excludeTitle, excludeText).Count;

		public static long WinGetExStyle(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.ExStyle : 0L;

		public static object WinGetID(object winTitle = null,
									  object winText = null,
									  object excludeTitle = null,
									  object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.Handle.ToInt64() : 0L;

		public static long WinGetIDLast(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinGetIDLast");
			var script = Script.TheScript;
			var (windows, criteria) = WindowQuery.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (windows != null && windows.Count > 0)
			{
				return windows[^1].Handle.ToInt64();
			}
			else if (!script.IsTearingDown)
				return (long)Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText, DefaultErrorLong);

			return 0L;
		}

		public static Array WinGetList(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinGetList");
			return new Array((
					  winTitle.IsNullOrEmpty()
					  && winText.IsNullOrEmpty()
					  && excludeTitle.IsNullOrEmpty()
					  && excludeText.IsNullOrEmpty()
					  ? WindowQuery.AllWindows
					  : SearchWindows(winTitle, winText, excludeTitle, excludeText)).Select(item => item.Handle.ToInt64()).ToList());
		}

		public static long WinGetMinMax(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			var val = 0L;

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
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
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.PID : 0L;

		public static object WinGetPos([ByRef] object outX = null,
									   [ByRef] object outY = null,
									   [ByRef] object outWidth = null,
									   [ByRef] object outHeight = null,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
            object valX = null, valY = null, valWidth = null, valHeight = null;
			WinPosHelper(false, ref valX, ref valY, ref valWidth, ref valHeight, winTitle, winText, excludeTitle, excludeText);
            if (outX != null) Refs.SetValue(outX, valX);
			if (outY != null) Refs.SetValue(outY, valY);
			if (outWidth != null) Refs.SetValue(outWidth, valWidth);
			if (outHeight != null) Refs.SetValue(outHeight, valHeight);
            return null;
		}

		public static string WinGetProcessName(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.ProcessName : "";

		public static string WinGetProcessPath(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.Path : "";

		public static long WinGetStyle(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.Style : 0L;

		public static string WinGetText(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null) =>
		string.Join(Keyword_Linefeed, SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.Text : [""]);

		public static string WinGetTitle(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? win.Title : "";

		public static string WinGetTransColor(object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
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
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				var color = win.Transparency.Al();
				return color != -1 ? color : "";
			}

			return DefaultObject;
		}

		public static long WinGetAlwaysOnTop(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null) => (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win && win.AlwaysOnTop) ? 1L : 0L;

		public static long WinGetEnabled(object winTitle = null,
							 object winText = null,
							 object excludeTitle = null,
							 object excludeText = null) => (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win && win.Enabled) ? 1L : 0L;

		public static object WinHide(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			var unsupported = false;
			DoDelayedAction(() =>
			{
				var matches = SearchWindows(winTitle, winText, excludeTitle, excludeText);

				foreach (var win in matches)
					if (!Platform.Window.TryHide(win.Handle))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinHide));

			return DefaultObject;
		}

		public static object WinKill(object winTitle = null,
									 object winText = null,
									 object secondsToWait = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			EnsureWindowControlPermission("WinKill");
			var seconds = secondsToWait.Ad(double.MinValue);
			var script = Script.TheScript;
			var (windows, crit) = WindowQuery.FindWindowGroup(winTitle, winText, excludeTitle, excludeText);

			if (crit != null && string.IsNullOrEmpty(crit.Group) && windows.Count == 0 && !script.IsTearingDown)
				return Errors.TargetErrorOccurred(winTitle, winText, excludeTitle, excludeText, DefaultErrorLong);

			foreach (var win in windows)
			{
				// Result deliberately ignored (AHK semantics): TerminateProcess is asynchronous, so a
				// successful kill can still report the window as existing for a few milliseconds.
				_ = Platform.Window.TryKill(win.Handle);

				if (seconds != double.MinValue)
					_ = win.WaitClose(seconds == 0 ? 0.5 : seconds);
			}

			WindowInfoBase.DoWinDelay();
			return DefaultObject;
		}

		public static object WinMaximize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var win in SearchWindows(winTitle, winText, excludeTitle, excludeText))
					if (!Platform.Window.TrySetState(win.Handle, FormWindowState.Maximized))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMaximize));

			return DefaultObject;
		}

		public static object WinMinimize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var win in SearchWindows(winTitle, winText, excludeTitle, excludeText))
					if (!Platform.Window.TrySetState(win.Handle, FormWindowState.Minimized))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMinimize));

			return DefaultObject;
		}

		public static object WinMinimizeAll()
		{
#if WINDOWS
			// The shell's native Minimize All: skips windows the shell exempts and lets
			// WinMinimizeAllUndo restore each window's prior (e.g. maximized) state.
			DoDelayedAction(() => _ = WindowsAPI.PostMessage(WindowsAPI.FindWindow("Shell_TrayWnd", null), WindowsAPI.WM_COMMAND, new nint(419), 0));
			return DefaultObject;
#else
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var window in WindowQuery.AllWindows)
					if (!Platform.Window.TrySetState(window.Handle, FormWindowState.Minimized))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMinimizeAll));

			return DefaultObject;
#endif
		}

		public static object WinMinimizeAllUndo(params object[] obj)
		{
#if WINDOWS
			DoDelayedAction(() => _ = WindowsAPI.PostMessage(WindowsAPI.FindWindow("Shell_TrayWnd", null), WindowsAPI.WM_COMMAND, new nint(416), 0));
			return DefaultObject;
#else
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var window in WindowQuery.AllWindows)
					if (!Platform.Window.TrySetState(window.Handle, FormWindowState.Normal))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMinimizeAllUndo));

			return DefaultObject;
#endif
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
			EnsureWindowControlPermission("WinMove");
			var _x = (x is null ? int.MinValue : x.ToInt());
			var _y = (y is null ? int.MinValue : y.ToInt());
			var w = (width is null ? int.MinValue : width.ToInt());
			var h = (height is null ? int.MinValue : height.ToInt());

			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (_x != int.MinValue || _y != int.MinValue || w != int.MinValue || h != int.MinValue)
				{
					//Unspecified args are int.MinValue ("leave unchanged"), so one platform call performs
					//move, resize, or both. Actions go by handle now that WindowInfo is a read-only snapshot.
					var setPos  = _x != int.MinValue || _y != int.MinValue;
					var setSize = w != int.MinValue || h != int.MinValue;

					if (!Platform.Window.TryMoveResize(win.Handle, new Rectangle(_x, _y, w, h), setPos, setSize))
						return WindowOperationUnsupported(nameof(WinMove));

					WindowInfoBase.DoWinDelay();
				}
			}

			return DefaultObject;
		}

		public static object WinMoveBottom(object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			DoAction(() =>
			{
				if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win
					&& !Platform.Window.TrySetZOrder(win.Handle, Keysharp.Internals.ZOrder.Bottom))
					_ = WindowOperationUnsupported(nameof(WinMoveBottom));
			});
			return DefaultObject;
		}

		public static object WinMoveTop(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			DoAction(() =>
			{
				if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win
					&& !Platform.Window.TrySetZOrder(win.Handle, Keysharp.Internals.ZOrder.Top))
					_ = WindowOperationUnsupported(nameof(WinMoveTop));
			});
			return DefaultObject;
		}

		public static object WinRedraw(object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
			DoAction(() =>
			{
				if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win
					&& !Platform.Window.TryRedraw(win.Handle))
					_ = WindowOperationUnsupported(nameof(WinRedraw));
			});
			return DefaultObject;
		}

		public static object WinRestore(object winTitle = null,
										object winText = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			var unsupported = false;
			DoDelayedAction(() =>
			{
				foreach (var win in SearchWindows(winTitle, winText, excludeTitle, excludeText))
					if (!Platform.Window.TrySetState(win.Handle, FormWindowState.Normal))
						unsupported = true;
			});

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinRestore));

			return DefaultObject;
		}

		public static object WinSetAlwaysOnTop(object newSetting,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			WinSetToggleX((win, b) => Platform.Window.TrySetAlwaysOnTop(win.Handle, b), win => win.AlwaysOnTop, newSetting, nameof(WinSetAlwaysOnTop), winTitle, winText, excludeTitle, excludeText);
			return DefaultObject;
		}

		public static object WinSetEnabled(object newSetting,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			WinSetToggleX((win, b) => Platform.Window.TrySetEnabled(win.Handle, b), win => win.Enabled, newSetting, nameof(WinSetEnabled), winTitle, winText, excludeTitle, excludeText);
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
			EnsureWindowControlPermission("WinSetRegion");
			var opts = options.As();

			if (!(SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win))
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

			// AHK's WinSetRegion (and the rest of the WinSet* family) applies NO SetWinDelay — only positional/visibility
			// ops (WinMove/WinActivate/WinShow/WinHide/WinMinimize/Maximize/Restore/WinClose) delay. A per-call A_WinDelay
			// here makes frequent region updates (e.g. following a window on every LOCATIONCHANGE) extremely laggy.
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
			EnsureWindowControlPermission("WinSetTitle");
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (!Platform.Window.TrySetTitle(win.Handle, newTitle.As()))
					return WindowOperationUnsupported(nameof(WinSetTitle));

				// No A_WinDelay: AHK's WinSetTitle does not call DoWinDelay.
			}

			return DefaultObject;
		}

		public static object WinSetTransColor(object color,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			EnsureWindowControlPermission("WinSetTransColor");
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (!Platform.Window.TrySetTransparentColor(win.Handle, color))
					return WindowOperationUnsupported(nameof(WinSetTransColor));

				// No A_WinDelay: AHK's WinSetTransColor does not call DoWinDelay.
			}

			return DefaultObject;
		}

		public static object WinSetTransparent(object n,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureWindowControlPermission("WinSetTransparent");
			if (SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (!Platform.Window.TrySetTransparency(win.Handle, n))
					return WindowOperationUnsupported(nameof(WinSetTransparent));

				// No A_WinDelay: AHK's WinSetTransparent does not call DoWinDelay.
			}

			return DefaultObject;
		}

		public static object WinShow(object winTitle = null,
									 object winText = null,
									 object excludeTitle = null,
									 object excludeText = null)
		{
			EnsureWindowControlPermission("WinShow");
			var tv = Script.TheScript.Threads.CurrentThread.configData;
			var prev = tv.detectHiddenWindows;
			var unsupported = false;
			tv.detectHiddenWindows = true;
			try
			{
				foreach (var win in SearchWindows(winTitle, winText, excludeTitle, excludeText))
					if (!Platform.Window.TryShow(win.Handle))
						unsupported = true;
			}
			finally
			{
				tv.detectHiddenWindows = prev;
			}
			WindowInfoBase.DoWinDelay();
			if (unsupported)
				return WindowOperationUnsupported(nameof(WinShow));

			return DefaultObject;
		}

		public static long WinWait(object winTitle = null,
								   object winText = null,
								   object timeout = null,
								   object excludeTitle = null,
								   object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinWait");
			var seconds = timeout.Ad();
			WindowInfoBase win;
			var start = DateTime.UtcNow;
			var criteria = WindowQuery.ToCriteria(winTitle, winText, excludeTitle, excludeText);

			//As in WinWaitClose: a handle is a search criterion here, not a direct address, so the wait
			//respects DetectHiddenWindows and waits for a hidden window to become visible.
			if (criteria != null)
				criteria.IsPureID = false;

			do
			{
				win = criteria == null ? WindowQuery.LastFound : WindowQuery.FindWindow(criteria, false);

				if (win != null || (seconds != 0 && (DateTime.UtcNow - start).TotalSeconds >= seconds))
					break;

				_ = Flow.Sleep(10);
			} while (win == null);

			if (win != null)
			{
				WindowQuery.LastFound = win;
				WindowInfoBase.DoWinDelay();   // AHK delays only when the wait succeeds, not on timeout.
			}

			return win != null ? win.Handle.ToInt64() : 0L;
		}

		public static long WinWaitActive(object winTitle = null,
										 object winText = null,
										 object timeout = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinWaitActive");
			var b = false;
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			var hwnd = 0L;

			while (!b && (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds))
			{
				WindowInfoBase win = null;

				if (criteria.IsEmpty)
				{
					var lastFound = WindowQuery.LastFound;

					if (lastFound != null && lastFound.IsSpecified && Platform.Window.GetActive(lastFound.Handle))
						win = lastFound;
				}
				else
					win = SearchActiveWindow(criteria);

				if (win != null)
				{
					WindowQuery.LastFound = win;
					hwnd = win.Handle.ToInt64();
					b = true;
				}

				if (!b)
					_ = Flow.Sleep(10);
			}

			if (b)
				WindowInfoBase.DoWinDelay();   // AHK delays only when the wait succeeds, not on timeout.

			return hwnd;
		}

		public static long WinWaitClose(object winTitle = null,
										object winText = null,
										object timeout = null,
										object excludeTitle = null,
										object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinWaitClose");
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);
			long result = 0L;
			//A handle is a search criterion here, not a direct address: AHK makes the wait family respect
			//DetectHiddenWindows so a GUI that hides rather than destroys on close ends the wait.
			criteria.IsPureID = false;

			while (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds)
			{
				var windows = WindowQuery.FindWindowGroup(criteria, false);

				if (windows.Count == 0)
				{
					result = 1L;
					break;
				}

				WindowQuery.LastFound = windows[0];
				_ = Flow.Sleep(10);
			}

			if (result == 1L)
				WindowInfoBase.DoWinDelay();   // AHK delays only when the wait succeeds, not on timeout.

			return result;
		}

		public static long WinWaitNotActive(object winTitle = null,
											object winText = null,
											object timeout = null,
											object excludeTitle = null,
											object excludeText = null)
		{
			EnsureWindowMonitoringPermission("WinWaitNotActive");
			var b = false;
			var seconds = timeout.Ad();
			var start = DateTime.UtcNow;
			var criteria = SearchCriteria.FromString(winTitle, winText, excludeTitle, excludeText);

			if (criteria.IsEmpty)
			{
				if (WindowQuery.LastFound is WindowInfoBase win && win.IsSpecified)
				{
					while (!b && (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds))
					{
						if (WindowQuery.LastFound is not WindowInfoBase cur || !cur.IsSpecified || !Platform.Window.GetActive(cur.Handle))
						{
							b = true;
							break;
						}

						_ = Flow.Sleep(10);
					}
				}
			}
			else
			{
				while (!b && (seconds == 0 || (DateTime.UtcNow - start).TotalSeconds < seconds))
				{
					var win = SearchActiveWindow(criteria);

					if (win == null)
					{
						b = true;
						break;
					}

					WindowQuery.LastFound = win;
					_ = Flow.Sleep(10);
				}
			}

			if (b)
				WindowInfoBase.DoWinDelay();   // AHK delays only when the wait succeeds, not on timeout.

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
