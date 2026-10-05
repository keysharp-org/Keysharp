using static Keysharp.Builtins.WindowHelper;
using static Keysharp.Builtins.WindowSearch;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
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

			if (!x.CoerceInt(out var xi) || !y.CoerceInt(out var yi))
				return 0L;

			return WindowQuery.WindowFromPoint(new POINT(xi, yi))?.Handle ?? 0L;
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

			if (!groupName.CoerceString(out var name) || !mode.CoerceString(out var m))
				return 0L;

			name = name.ToLowerInvariant();
			var script = Script.TheScript;

			if (script.WindowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0 || !TrySearchWindows($"ahk_group {name}", null, null, null, out var windows))
					return 0L;

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
			if (!groupName.CoerceString(out var name))
				return DefaultObject;

			var windowGroups = TheScript.WindowGroups;

			if (string.IsNullOrEmpty(name))
				return Errors.ValueErrorOccurred("Group name must not be empty.");

			SearchCriteria criteria = null;

			if (name != "AllWindows" && !SearchCriteria.TryFromString(winTitle, winText, excludeTitle, excludeText, out criteria))
				return DefaultObject;

			if (!windowGroups.TryGetValue(name, out var group))
				windowGroups.Add(name, group = new WindowGroup());

			if (criteria != null)
			{
				group.sc.Add(criteria);
				group.activated.Clear();
				group.deactivated.Clear();
			}

			return DefaultObject;
		}

		public static object GroupClose(object groupName, object mode = null)
		{
			EnsureWindowControlPermission("GroupClose");

			if (!groupName.CoerceString(out var name) || !mode.CoerceString(out var m))
				return DefaultObject;

			name = name.ToLowerInvariant();
			var windowGroups = Script.TheScript.WindowGroups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0 || !TrySearchWindows($"ahk_group {name}", null, null, null, out var windows))
					return DefaultObject;

				var stack = group.lastWasDeactivate ? group.deactivated : group.activated;

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

			if (!groupName.CoerceString(out var name) || !mode.CoerceString(out var m))
				return DefaultObject;

			name = name.ToLowerInvariant();
			var script = Script.TheScript;
			var windowGroups = script.WindowGroups;

			if (windowGroups.TryGetValue(name, out var group))
			{
				if (group.sc.Count == 0 || !TrySearchWindows($"ahk_group {name}", null, null, null, out var windows))
					return DefaultObject;

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
												object excludeText = null)
		{
			if (!options.CoerceString(out var opts))
				return DefaultObject;

			return Platform.Control.ListViewGetContent(
					   opts,
					   controlID,
					   winTitle,
					   winText,
					   excludeTitle,
					   excludeText);
		}

		public static object MenuSelect([Optional] object winTitle,
										[Optional] object winText,
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
										 [UserDeclaredName("wParam")] object wParam = null,
										 [UserDeclaredName("lParam")] object lParam = null,
										 object controlID = null,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureWindowControlPermission("PostMessage");

			if (!winText.CoerceString(out var text) || !excludeTitle.CoerceString(out var exTitle) || !excludeText.CoerceString(out var exText))
				return DefaultObject;

			if (!msgNumber.CoerceLong(out var msg) || !wParam.CoerceInt(out var wp) || !lParam.CoerceInt(out var lp))
				return DefaultObject;

			Platform.Control.PostMessage(
				unchecked((uint)msg),
				wp,
				lp,
				controlID,
				winTitle,
				text,
				exTitle,
				exText);
			return DefaultObject;
		}

		public static long SendMessage(object msgNumber,
									   [UserDeclaredName("wParam")] object wParam = null,
									   [UserDeclaredName("lParam")] object lParam = null,
									   object controlID = null,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null,
									   object timeout = null)
		{
			EnsureWindowControlPermission("SendMessage");

			if (!winText.CoerceString(out var text) || !excludeTitle.CoerceString(out var exTitle) || !excludeText.CoerceString(out var exText))
				return 0L;

			if (!msgNumber.CoerceLong(out var msg) || !timeout.CoerceInt(out var timeoutMs, 5000))
				return 0L;

			return Platform.Control.SendMessage(
										   unchecked((uint)msg),
										   wParam,
										   lParam,
										   controlID,
										   winTitle,
										   text,
										   exTitle,
										   exText,
										   timeoutMs);
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

			if (!matchMode.CoerceString(out var val))
				return DefaultObject;

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
			if (!partNumber.CoerceInt(out var partN, 1))
				return DefaultObject;

			var part = Math.Max(0, partN - 1);

			if (!winText.CoerceString(out var text) || !excludeTitle.CoerceString(out var title) || !excludeText.CoerceString(out var exclude))
				return DefaultObject;

#if WINDOWS
			// Standard Win32 common-control status bar (class "msctls_statusbar32", first instance).
			if (!TrySearchControl("msctls_statusbar321", winTitle, text, title, exclude, out var ctrl))
				return DefaultObject;

			if (ctrl != null)
			{
				var sb = Platform.StatusBar.CreateStatusBar(ctrl.Handle);

				if (part < sb.Captions.Length)
					return sb.Captions[part];

				return DefaultObject;
			}

			// Keysharp / WinForms StatusStrip fallback (same process).
			if (!TrySearchControl("WindowsForms10.Window.8.app.0.2b89eaa_r3_ad1", winTitle, text, title, exclude, out ctrl))
				return DefaultObject;

			if (ctrl != null && Control.FromHandle(ctrl.Handle) is StatusStrip ss)
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
		/// <param name="timeout">The number of seconds (can contain a decimal point) to wait before timing out, in which case 0 is returned. Default is blank, which means wait indefinitely. Specifying 0 checks once.</param>
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
			// The window text parameters are converted once, so a continued TypeError does not repeat on every poll.
			if (!barText.CoerceString(out var bartext) || !winText.CoerceString(out var text) || !excludeTitle.CoerceString(out var title) || !excludeText.CoerceString(out var exclude))
				return 0L;

			if (!timeout.CoerceDouble(out var seconds, -1) || !partNumber.CoerceInt(out var part, 1) || !interval.CoerceInt(out var intvl, 50))
				return 0L;

			var matchfound = Keysharp.Internals.Flow.WaitUntil(() => StatusBarGetText((long)part, winTitle, text, title, exclude) == bartext,
								SecondsToMs(seconds), intvl < 1 ? 50 : intvl);
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
			if (!WindowQuery.TryToCriteria(winTitle, winText, excludeTitle, excludeText, out var criteria))
				return 0L;

			return SearchActiveWindow(criteria)?.Handle.ToInt64() ?? 0L;
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
			=> CloseWindows(nameof(WinClose), false, winTitle, winText, secondsToWait, excludeTitle, excludeText);

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
		TrySearchWindows(winTitle, winText, excludeTitle, excludeText, out var windows) ? windows.Count : 0L;

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

			if (!WindowQuery.TryFindWindowGroup(winTitle, winText, excludeTitle, excludeText, out var windows, out _, true))
				return 0L;

			if (windows.Count > 0)
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
			IEnumerable<WindowInfoBase> windows;

			if (winTitle.IsNullOrEmpty()
					&& winText.IsNullOrEmpty()
					&& excludeTitle.IsNullOrEmpty()
					&& excludeText.IsNullOrEmpty())
				windows = WindowQuery.AllWindows;
			else if (TrySearchWindows(winTitle, winText, excludeTitle, excludeText, out var matches))
				windows = matches;
			else
				return null;

			return new Array(windows.Select(item => item.Handle.ToInt64()).ToList());
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
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? ProcessImageOrError(win, true) : "";

		public static string WinGetProcessPath(object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null) =>
		SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? ProcessImageOrError(win, false) : "";

		// A process the window search could not query only fails to match there, but reading its name raises. The search
		// may have read the name already, so the lookup is repeated for its error.
		private static string ProcessImageOrError(WindowInfoBase win, bool nameOnly)
		{
			var image = nameOnly ? win.ProcessName : win.Path;

			if (image.Length != 0)
				return image;

#if WINDOWS
			_ = Processes.GetProcessImage((uint)win.PID, nameOnly);
			return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", "");
#else
			return (string)Errors.OSErrorOccurredWithMessage($"Could not read the executable of process {win.PID}.", "");
#endif
		}

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
				_ = win.TransparentColor.TryCoerceLong(out var colorLong);
				var color = (int)colorLong;
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
				_ = win.Transparency.TryCoerceLong(out var color);
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
			if (!WinAct(nameof(WinHide), winTitle, winText, excludeTitle, excludeText, Platform.Window.TryHide))
			{
#if LINUX
				if (Platform.Desktop.IsWaylandSession)
				{
					if (Keysharp.Internals.Linux.DesktopClient.Current.TryProbeWindowSupport(out _, out var visibility)
						&& !visibility)
						return Errors.UnsupportedErrorOccurred("WinHide is unavailable: the desktop service does not support changing foreign-window visibility.");

					return Errors.OSErrorOccurredWithMessage("WinHide failed for a matching window.");
				}
#endif
				return WindowOperationUnsupported(nameof(WinHide));
			}

			return DefaultObject;
		}

		public static object WinKill(object winTitle = null,
									 object winText = null,
									 object secondsToWait = null,
									 object excludeTitle = null,
									 object excludeText = null)
			=> CloseWindows(nameof(WinKill), true, winTitle, winText, secondsToWait, excludeTitle, excludeText);

		public static object WinMaximize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
			=> WinAct(nameof(WinMaximize), winTitle, winText, excludeTitle, excludeText, h => Platform.Window.TrySetState(h, FormWindowState.Maximized))
			   ? DefaultObject : WindowOperationUnsupported(nameof(WinMaximize));

		public static object WinMinimize(object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
			=> WinAct(nameof(WinMinimize), winTitle, winText, excludeTitle, excludeText, h => Platform.Window.TrySetState(h, FormWindowState.Minimized))
			   ? DefaultObject : WindowOperationUnsupported(nameof(WinMinimize));

		public static object WinMinimizeAll()
		{
#if WINDOWS
			// The shell's native Minimize All skips windows the shell exempts.
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

		public static object WinMinimizeAllUndo()
		{
			var unsupported = false;
			DoDelayedAction(() => unsupported = !Platform.Window.TryRestoreAll(ThreadAccessors.A_DetectHiddenWindows));

			if (unsupported)
				return WindowOperationUnsupported(nameof(WinMinimizeAllUndo));

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
			EnsureWindowControlPermission("WinMove");
			int _x = int.MinValue, _y = int.MinValue, w = int.MinValue, h = int.MinValue;

			if ((x is not null && !x.CoerceInt(out _x))
					|| (y is not null && !y.CoerceInt(out _y))
					|| (width is not null && !width.CoerceInt(out w))
					|| (height is not null && !height.CoerceInt(out h)))
				return DefaultObject;

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
			=> WinAct(nameof(WinRestore), winTitle, winText, excludeTitle, excludeText, Platform.Window.TryRestore)
			   ? DefaultObject : WindowOperationUnsupported(nameof(WinRestore));

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

			if (!options.CoerceString(out var opts))
				return DefaultObject;

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
				if (!newTitle.CoerceString(out var title))
					return DefaultObject;

				if (!Platform.Window.TrySetTitle(win.Handle, title))
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
			if (!n.CoerceString(out var alphaText))
				return DefaultObject;

			// AHK removes transparency for a blank N exactly as for Off, so backends only ever see Off.
			if (alphaText.Length == 0)
				n = "Off";
			else if (!alphaText.Equals("Off", StringComparison.OrdinalIgnoreCase))
			{
				if (!n.TryParseLong(out var alpha) || alpha is < 0 or > 255)
					return Errors.ValueErrorOccurred("N must be from 0 through 255, blank or Off.", n);

				n = alpha;
			}

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
			// WinShow finds hidden windows whatever DetectHiddenWindows says, as AutoHotkey's does.
			var tv = Script.TheScript.Threads.CurrentThread.configData;
			var prev = tv.detectHiddenWindows;
			tv.detectHiddenWindows = true;

			try
			{
				return WinAct(nameof(WinShow), winTitle, winText, excludeTitle, excludeText, Platform.Window.TryShow)
					   ? DefaultObject : WindowOperationUnsupported(nameof(WinShow));
			}
			finally
			{
				tv.detectHiddenWindows = prev;
			}
		}

		public static long WinWait(object winTitle = null,
								   object winText = null,
								   object timeout = null,
								   object excludeTitle = null,
								   object excludeText = null)
			=> WinWaitFor(nameof(WinWait), false, true, winTitle, winText, timeout, excludeTitle, excludeText);

		public static long WinWaitActive(object winTitle = null,
										 object winText = null,
										 object timeout = null,
										 object excludeTitle = null,
										 object excludeText = null)
			=> WinWaitFor(nameof(WinWaitActive), true, true, winTitle, winText, timeout, excludeTitle, excludeText);

		public static long WinWaitClose(object winTitle = null,
										object winText = null,
										object timeout = null,
										object excludeTitle = null,
										object excludeText = null)
			=> WinWaitFor(nameof(WinWaitClose), false, false, winTitle, winText, timeout, excludeTitle, excludeText);

		public static long WinWaitNotActive(object winTitle = null,
											object winText = null,
											object timeout = null,
											object excludeTitle = null,
											object excludeText = null)
			=> WinWaitFor(nameof(WinWaitNotActive), true, false, winTitle, winText, timeout, excludeTitle, excludeText);

		/// <summary>
		/// AutoHotkey's WinWait family: waits until a matching window exists or is active, or with
		/// <paramref name="appear"/> false until none does. An omitted timeout waits indefinitely, 0 checks once and a
		/// negative one raises. A handle that names no window ends the wait at once, met only for the Close and
		/// NotActive forms.
		/// </summary>
		private static long WinWaitFor(string name, bool active, bool appear, object winTitle, object winText, object timeout, object excludeTitle, object excludeText)
		{
			EnsureWindowMonitoringPermission(name);
			var timeoutMs = -1;

			if (timeout != null)
			{
				if (!timeout.CoerceDouble(out var seconds))
					return 0L;

				if ((timeoutMs = SecondsToMs(seconds)) < 0)
					return (long)Errors.InvalidParameterErrorOccurred(3, name, timeout, 0L);
			}

			if (!WindowQuery.TryToCriteria(winTitle, winText, excludeTitle, excludeText, out var criteria))
				return 0L;

			WindowInfoBase found = null;
			Func<bool> completed;

			// A handle is watched itself, respecting DetectHiddenWindows so a Gui that hides rather than closes ends
			// WinWaitClose; a window that no longer exists cannot come back.
			if (criteria?.IsPureID == true)
			{
				var hwnd = criteria.ID;
				completed = () =>
				{
					if (!WindowQuery.IsWindow(hwnd))
						return true;

					var win = WindowQuery.CreateWindow(hwnd);
					var isMatch = active ? WindowQuery.GetForegroundWindowHandle() == hwnd : ThreadAccessors.A_DetectHiddenWindows || win.Visible;

					if (isMatch)
						WindowQuery.LastFound = found = win;

					return isMatch == appear;
				};
			}
			else
				completed = () => ((found = active ? SearchActiveWindow(criteria) : WindowQuery.Exist(criteria)) != null) == appear;

			if (!Keysharp.Internals.Flow.WaitUntil(completed, timeoutMs, Script.SLEEP_INTERVAL))
				return 0L;

			WindowInfoBase.DoWinDelay();
			return appear ? found?.Handle.ToInt64() ?? 0L : 1L;
		}

		/// <summary>
		/// AutoHotkey's WinAct for WinHide, WinShow, WinMinimize, WinMaximize and WinRestore. False when the platform
		/// could not act on a window.
		/// </summary>
		private static bool WinAct(string name, object winTitle, object winText, object excludeTitle, object excludeText, Func<nint, bool> act)
		{
			EnsureWindowControlPermission(name);

			if (WinActTargets(winTitle, winText, excludeTitle, excludeText, out _) is not List<WindowInfoBase> windows)
				return true;

			var acted = true;

			foreach (var win in windows)
				acted &= act(win.Handle);

			WindowInfoBase.DoWinDelay();
			return acted;
		}

		/// <summary>
		/// AutoHotkey's WinAct for WinClose and WinKill, which wait the given seconds for each window to go: 20 ms when
		/// omitted, or none for a group.
		/// </summary>
		private static object CloseWindows(string name, bool kill, object winTitle, object winText, object secondsToWait, object excludeTitle, object excludeText)
		{
			EnsureWindowControlPermission(name);

			if (!secondsToWait.CoerceDouble(out var seconds, double.NaN)
					|| WinActTargets(winTitle, winText, excludeTitle, excludeText, out var group) is not List<WindowInfoBase> windows)
				return DefaultObject;

			var waitMs = double.IsNaN(seconds) ? (group ? 0 : 20) : SecondsToMs(seconds);
			var unsupported = false;

			foreach (var win in windows)
			{
				// TerminateProcess is asynchronous, so a kill that worked can still find the window for a moment.
				if (kill)
					_ = Platform.Window.TryKill(win.Handle);
				else if (!Platform.Window.TryClose(win.Handle))
				{
					unsupported = true;
					continue;
				}

				if (waitMs > 0)
					_ = Keysharp.Internals.Flow.WaitUntil(() => !Platform.Window.GetExists(win.Handle), waitMs, Script.SLEEP_INTERVAL);
			}

			WindowInfoBase.DoWinDelay();
			return unsupported ? WindowOperationUnsupported(name) : DefaultObject;
		}

		/// <summary>
		/// The windows AutoHotkey's WinAct acts on: every member of a group when WinTitle is only ahk_group and the
		/// other parameters are blank, otherwise the one window the parameters name. Null when there is none, having
		/// raised a TargetError, or when a parameter raised an error the script continued.
		/// </summary>
		private static List<WindowInfoBase> WinActTargets(object winTitle, object winText, object excludeTitle, object excludeText, out bool group)
		{
			group = winTitle is string title && title.StartsWith(Keyword_ahk_group, StringComparison.OrdinalIgnoreCase)
					&& winText.IsNullOrEmpty() && excludeTitle.IsNullOrEmpty() && excludeText.IsNullOrEmpty()
					&& Script.TheScript.WindowGroups.ContainsKey(title.AsSpan(Keyword_ahk_group.Length).TrimStart(SpaceTab).ToString());

			if (group)
				return TrySearchWindows(winTitle, null, null, null, out var members) ? members : null;

			return SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is WindowInfoBase win ? [win] : null;
		}

		// AutoHotkey's int(seconds * 1000), clamped where that would overflow.
		private static int SecondsToMs(double seconds) => (int)Math.Clamp(seconds * 1000, int.MinValue, int.MaxValue);

#if LINUX
		[PublicHiddenFromUser]
		public static long zzzLinuxTester(params object[] obj)
		{
			return 1L;
		}
#endif
	}
}
