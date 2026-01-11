#if WINDOWS
using static Keysharp.Core.Common.Keyboard.VirtualKeys;
using Keysharp.Core.Common.Mouse;

//#define DPI
namespace Keysharp.Core.Windows
{
	/// <summary>
	/// Concrete implementation of WindowItem for the Windows platfrom.
	/// </summary>
	internal class WindowItem : WindowItemBase, IWin32Window
	{
		private int lastChildCount = 64;

		internal override bool Active
		{
			get
			{
				if (IsSpecified && WindowManager.ActiveWindow is WindowItemBase item)
				{
					//KeysharpEnhancements.OutputDebugLine($"item.Handle: {item.Handle.ToInt64()}, item.Title: {item.Title}, Handle: {Handle.ToInt64()}, Title: {Title}");
					//Keysharp.Core.File.FileAppend($"item.Handle: {item.Handle.ToInt64()}, item.Title: {item.Title}, Handle: {Handle.ToInt64()}, Title: {Title}\n", "out.txt");
					if (item.Handle.ToInt64() == Handle.ToInt64())
						return true;
				}

				return false;
			}
			set
			{
				if (IsSpecified)
				{
					if (WindowManager.ActiveWindow.Handle.ToInt64() != Handle.ToInt64())
					{
						if (IsIconic)
							_ = WindowsAPI.ShowWindow(Handle, WindowsAPI.SW_RESTORE);
						else
							_ = SetForegroundWindowEx(this);

						DoWinDelay();
					}
				}
			}
		}

		internal override bool AlwaysOnTop
		{
			get => IsSpecified&& (ExStyle & WindowsAPI.WS_EX_TOPMOST) != 0;

			set
			{
				if (!IsSpecified)
					return;

				var type = new nint(value ? WindowsAPI.HWND_TOPMOST : WindowsAPI.HWND_NOTOPMOST);
				_ = WindowsAPI.SetWindowPos(Handle, type, 0, 0, 0, 0, WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE);
				DoWinDelay();
			}
		}

		internal override bool Bottom
		{
			set
			{
				if (!IsSpecified)
					return;

				var type = new nint(value ? WindowsAPI.HWND_BOTTOM : WindowsAPI.HWND_TOP);
				_ = WindowsAPI.SetWindowPos(Handle, type, 0, 0, 0, 0, WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE);
			}
		}

		internal override HashSet<WindowItemBase> ChildWindows
		{
			get
			{
				var children = new HashSet<WindowItemBase>(lastChildCount);

				if (IsSpecified)
				{
					//var detectHiddenText = ThreadAccessors.A_DetectHiddenText;
					_ = WindowsAPI.EnumChildWindows(Handle, (nint hwnd, int lParam) =>
					{
						//if (detectHiddenText || WindowsAPI.IsWindowVisible(hwnd))
						_ = children.Add(WindowManager.CreateWindow(hwnd));
						return true;
					}, 0);
				}

				//The EnumChildWindows() call above will not include any controls if the window has not been shown yet.
				//So we must also use this method to ensure we get everything.
				if (Control.FromHandle(Handle) is Form form)
				{
					form.Invoke(() =>
					{
						foreach (var ctrl in form.GetAllControlsRecursive<Control>())
							_ = children.Add(WindowManager.CreateWindow(ctrl.Handle));//HashSet takes care of avoiding dupes.
					});
				}

				lastChildCount = children.Count;
				return children;
			}
		}

		internal override string ClassName => IsSpecified ? WindowsAPI.GetClassName(Handle) : string.Empty;

		internal override Rectangle ClientLocation
		{
			get
			{
				if (!IsSpecified || !WindowsAPI.GetClientRect(Handle, out var rect))
					return Rectangle.Empty;

#if DPI
				var scale = 1.0 / Accessors.A_ScaledScreenDPI;
				return new Rectangle((int)(scale * rect.Left), (int)(scale * rect.Top), (int)(scale * (rect.Right - rect.Left)), (int)(scale * (rect.Bottom - rect.Top)));
#else
				return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
#endif
			}
		}

		internal override bool Enabled
		{
			get => IsSpecified&& WindowsAPI.IsWindowEnabled(Handle);

			set
			{
				if (!IsSpecified)
					return;

				_ = WindowsAPI.EnableWindow(Handle, value);
				DoWinDelay();
			}
		}

		internal override bool Exists => IsSpecified&& WindowsAPI.IsWindow(Handle);

		internal override long ExStyle
		{
			get => IsSpecified ? WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE).ToInt64() : 0;

			set
			{
				if (IsSpecified)
					_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(value));
			}
		}

		internal override bool IsHung => Handle == 0 ? false : WindowsAPI.IsHungAppWindow(Handle);

		internal override Rectangle Location
		{
			get
			{
				if (!IsSpecified || !WindowsAPI.GetWindowRect(Handle, out var rect))
					return Rectangle.Empty;

#if DPI
				var scale = 1.0 / Accessors.A_ScaledScreenDPI;
				return new Rectangle((int)(scale * rect.Left), (int)(scale * rect.Top), (int)(scale * (rect.Right - rect.Left)), (int)(scale * (rect.Bottom - rect.Top)));
#else
				return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
#endif
			}
			set
			{
				if (!IsSpecified || !WindowsAPI.GetWindowRect(Handle, out var rect))
					return;

				if (value.X != int.MinValue)
					rect.Left = value.X;

				if (value.Y != int.MinValue)
					rect.Top = value.Y;

				_ = WindowsAPI.MoveWindow(Handle, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, true);
			}
		}

		internal override WindowItemBase NonChildParentWindow => WindowManager.CreateWindow(WindowsAPI.GetNonChildParent(Handle));

		internal override WindowItemBase ParentWindow => WindowManager.CreateWindow(WindowsAPI.GetAncestor(Handle, gaFlags.GA_PARENT));

		internal override string Path
		{
			get
			{
				if (!processPath.IsNullOrEmpty())
					return processPath;

				_ = WindowsAPI.GetWindowThreadProcessId(Handle, out var pid);

				if (pid == 0)
					return DefaultErrorString;

				if (Processes.GetProcessName(pid, out processPath) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);

				return processPath;
			}
		}

		internal override long PID
		{
			get
			{
				_ = WindowsAPI.GetWindowThreadProcessId(Handle, out var n);
				return n;
			}
		}

		internal override string ProcessName
		{
			get
			{
				if (!processName.IsNullOrEmpty())
					return processName;

				_ = WindowsAPI.GetWindowThreadProcessId(Handle, out var pid);

				if (pid == 0)
					return DefaultErrorString;

				if (Processes.GetProcessName(pid, out processName) == 0)
					return (string)Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()), "", DefaultErrorString);

				return processName;
			}
		}

		internal override Size Size
		{
			get
			{
				var scale = 1.0 / A_ScaledScreenDPI;
				return !IsSpecified || !WindowsAPI.GetWindowRect(Handle, out var rect)
					   ? Size.Empty
#if DPI
					   : new Size((int)(scale * (rect.Right - rect.Left)), (int)(scale * (rect.Bottom - rect.Top)));
#else
					   : new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
#endif
			}
			set
			{
				if (!IsSpecified || !WindowsAPI.GetWindowRect(Handle, out var rect))
					return;

				_ = WindowsAPI.MoveWindow(Handle, rect.Left, rect.Top, value.Width, value.Height, true);
			}
		}

		internal override long Style
		{
			get => IsSpecified ? WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE).ToInt64() : 0;

			set
			{
				if (IsSpecified)
					_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_STYLE, new nint(value));
			}
		}

		internal override List<string> Text
		{
			get
			{
				if (!IsSpecified)
					return [];

				var items = new List<string>(64);
				var tv = Script.TheScript.Threads.CurrentThread.configData;
				_ = WindowsAPI.EnumChildWindows(Handle, (nint hwnd, int lParam) =>
				{
					if (tv.detectHiddenText || WindowsAPI.IsWindowVisible(hwnd))
					{
						var text = tv.titleMatchModeSpeed ? WindowsAPI.GetWindowText(hwnd) : WindowsAPI.GetWindowTextTimeout(hwnd, 5000);//AHK used 5000.
						items.Add(text);
					}

					return true;
				}, 0);
				return items;
			}
		}

		internal override string Title
		{
			get => IsSpecified ? WindowsAPI.GetWindowText(Handle) : string.Empty;

			set
			{
				if (IsSpecified)
					_ = WindowsAPI.SetWindowText(Handle, value ?? string.Empty);
			}
		}

		internal override object Transparency
		{
			get
			{
				if (WindowsAPI.GetLayeredWindowAttributes(Handle, out var key, out var alpha, out var flags))
					if ((flags & WindowsAPI.LWA_ALPHA) == WindowsAPI.LWA_ALPHA)
						return (long)alpha;

				return -1L;
			}
			set
			{
				var exstyle = WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE).ToInt64();

				if (value is string s)
				{
					if (s.ToLower() == "off")
						_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(exstyle | ~WindowsAPI.WS_EX_LAYERED));
				}
				else
				{
					var alpha = Math.Clamp((int)value.Al(), 0, 255);

					if (WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(exstyle | WindowsAPI.WS_EX_LAYERED)) == 0 ||
							!WindowsAPI.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, WindowsAPI.LWA_ALPHA))
						_ = Errors.OSErrorOccurred("", $"Could not assign transparency with alpha value of {alpha}.");
				}
			}
		}

		internal override object TransparentColor
		{
			get
			{
				if (WindowsAPI.GetLayeredWindowAttributes(Handle, out var key, out var alpha, out var flags))
					if ((flags & WindowsAPI.LWA_COLORKEY) == WindowsAPI.LWA_COLORKEY)
						return key;

				return int.MinValue;
			}
			set
			{
				var splits = value.As().Split(SpaceTab, StringSplitOptions.RemoveEmptyEntries);
				var colorstr = splits[0];
				var exstyle = WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE);

				if (colorstr.ToLower() == "off")
				{
					if (WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(exstyle.ToInt64() & ~WindowsAPI.WS_EX_LAYERED)) == 0)
						_ = Errors.OSErrorOccurred("", $"Could not turn transparency off.");
				}
				else
				{
					var val = 0L;
					var flags = WindowsAPI.LWA_COLORKEY;

					if (Conversions.TryParseColor(colorstr, out var color))
					{
						if (splits.Length > 1)
						{
							val = splits[1].Al();
							flags |= WindowsAPI.LWA_ALPHA;
						}

						if (WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(exstyle.ToInt64() | WindowsAPI.WS_EX_LAYERED)) != 0)//At one point this only worked with xor ^ and not or |, but it seems to be working with | now.
						{
							color = Color.FromArgb(color.A, color.B, color.G, color.R);//Flip RGB to BGR.

							if (!WindowsAPI.SetLayeredWindowAttributes(Handle, (uint)color.ToArgb() & 0x00FFFFFF, (byte)val, (uint)flags))//Make top byte of color zero.
								_ = Errors.OSErrorOccurred("", $"Could not assign transparency color {color} with alpha value of {val}.");
						}
						else
							_ = Errors.OSErrorOccurred("", $"Could not assign transparency color {color} with alpha value of {val}.");
					}
				}
			}
		}

		internal override bool Visible
		{
			get => IsSpecified && WindowsAPI.IsWindowVisible(Handle);
			set => _ = value ? Show() : Hide();
		}

		internal override FormWindowState WindowState
		{
			get
			{
				return !IsSpecified
					   ? FormWindowState.Normal
					   : WindowsAPI.IsZoomed(Handle) ? FormWindowState.Maximized : (WindowsAPI.IsIconic(Handle) ? FormWindowState.Minimized : FormWindowState.Normal);
			}
			set
			{
				if (!IsSpecified)
					return;

				var cmd = WindowsAPI.SW_NORMAL;

				switch (value)
				{
					case FormWindowState.Maximized: cmd = WindowsAPI.SW_MAXIMIZE; break;

					case FormWindowState.Minimized: cmd = WindowsAPI.SW_MINIMIZE; break;
				}

				_ = WindowsAPI.ShowWindow(Handle, cmd);
			}
		}

		internal WindowItem(nint handle) : base(handle)
		{
		}

		/// <summary>
		/// Gotten from AHK. Unsure how or why this works, but know that it does work.
		/// </summary>
		/// <param name="targetWindow"></param>
		/// <returns></returns>
		internal static nint SetForegroundWindowEx(WindowItemBase win, bool backgroundActivation = false)
		{
			if (win == null)
				return 0;

			var targetWindow = win.Handle;
			var script = Script.TheScript;
			var mainid = script.ProcessesData.MainThreadID;
			var targetThread = WindowsAPI.GetWindowThreadProcessId(targetWindow, out var procid);

			if (targetThread != mainid && win.IsHung)//Calls to IsWindowHung should probably be avoided if the window belongs to our thread.
				return 0;

			var origForegroundWnd = WindowsAPI.GetForegroundWindow();
			var sender = script.HookThread.kbdMsSender;

			//Restore the window *before* checking if it is already active.
			if (win.IsIconic && !backgroundActivation)
				// This might never return if targetWindow is a hung window.  But it seems better
				// to do it this way than to use the PostMessage() method, which might not work
				// reliably with apps that don't handle such messages in a standard way.
				// A minimized window must be restored or else SetForegroundWindow() always(?)
				// won't work on it.  UPDATE: ShowWindowAsync() would prevent a hang, but
				// probably shouldn't use it because we rely on the fact that the message
				// has been acted on prior to trying to activate the window (and all Async()
				// does is post a message to its queue):
				_ = WindowsAPI.ShowWindow(targetWindow, WindowsAPI.SW_RESTORE);

			if (targetWindow == origForegroundWnd)//It's already the active window.
				return targetWindow;

			nint newForegroundWnd = 0;

			//Try a simple approach first.
			if (!script.WinActivateForce)
			{
				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);

				if (newForegroundWnd != 0)
					return newForegroundWnd;
			}

			// Otherwise continue with the more drastic methods below.
			bool is_attached_my_to_fore = false, isAttachedForeToTarget = false;
			uint foreThread = 0;

			if (origForegroundWnd != 0) // Might be NULL from above.
			{
				var foregroundwin = WindowManager.CreateWindow(origForegroundWnd);
				// Based on MSDN docs, these calls should always succeed due to the other
				// checks done above (e.g. that none of the HWND's are NULL):
				foreThread = WindowsAPI.GetWindowThreadProcessId(origForegroundWnd, out var id);

				// MY: Normally, it's suggested that you only need to attach the thread of the
				// foreground window to our thread.  However, I've confirmed that doing all three
				// attaches below makes the attempt much more likely to succeed.  In fact, it
				// almost always succeeds whereas the one-attach method hardly ever succeeds the first
				// time (resulting in a flashing taskbar button due to having to invoke a second attempt)
				// when one window is quickly activated after another was just activated.
				// AutoIt3: Attach all our input threads, will cause SetForeground to work under 98/Me.
				// MSDN docs: The AttachThreadInput function fails if either of the specified threads
				// does not have a message queue (My: ok here, since any window's thread MUST have a
				// message queue).  [It] also fails if a journal record hook is installed.  ... Note
				// that key state, which can be ascertained by calls to the GetKeyState or
				// GetKeyboardState function, is reset after a call to AttachThreadInput.  You cannot
				// attach a thread to a thread in another desktop.  A thread cannot attach to itself.
				// Therefore, idAttachTo cannot equal idAttach.  Update: It appears that of the three,
				// this first call does not offer any additional benefit, at least on XP, so not
				// using it for now:
				if (foreThread != 0 && mainid != foreThread && !foregroundwin.IsHung)
					is_attached_my_to_fore = WindowsAPI.AttachThreadInput(mainid, foreThread, true);

				if (foreThread != 0 && targetThread != 0 && foreThread != targetThread)//IsWindowHung(targetWindow) was called earlier.
					isAttachedForeToTarget = WindowsAPI.AttachThreadInput(foreThread, targetThread, true);
			}

			// The log showed that it never seemed to need more than two tries.  But there's
			// not much harm in trying a few extra times.  The number of tries needed might
			// vary depending on how fast the CPU is:
			var activateforce = script.WinActivateForce ? 1 : 0;

			for (var i = 0; i < 5; ++i)
			{
				if (i == activateforce && !sender.triedKeyUp) // At least one attempt failed this time, and Alt-up hasn't been tried since the process started.
				{
					sender.triedKeyUp = true;
					// Lexikos: Recent testing on Windows 10.0.19555 indicated that sending Alt-up was just as effective
					// as sending double-Alt (the second Alt was probably just to counter the first one), but it should
					// have lower risk of side-effects since there's no key-down.  One observable side-effect is that
					// if the user happens to be holding Alt (and it wasn't suppressed due to being part of a hotkey),
					// the window menu might light up for an instant before the window loses focus.  However, this also
					// means that if the system is set to hide the menu mnemonics (underlines), that actually happens
					// whereas they would normally be left visible because the window didn't catch Alt-up.
					// The Alt-up seems to be effective at allowing ALL subsequent SetForegroundWindow() calls to succeed
					// even without AttachThreadInput(), and even if Alt-up is just sent at program startup, so it's only
					// done once per process.  In other words, this should stop subsequent calls from causing taskbar
					// buttons to flash.  This doesn't seem to be necessary if AttachThreadInput() is used, so it's not
					// done for #WinActivateForce unless there's been one failed attempt.
					// KEY_BLOCK_THIS is used so that if this or any other scripts (running on v1.1.27+) have a hook
					// installed, the Alt-up will be suppressed to further reduce the risk of side-effects.  Testing
					// showed that the suppressed event worked just as well (in theory, because the system's handling
					// of it isn't and can't be suppressed).
					sender.SendKeyEvent(KeyEventTypes.KeyUp, VirtualKeys.VK_MENU, 0, 0, false, KeyboardMouseSender.KeyBlockThis);//Porting these will be tough.
				}

				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);

				if (newForegroundWnd != 0)
					break;
			}

			// I decided to avoid the quick minimize + restore method of activation.  It's
			// not that much more effective (if at all), and there are some significant
			// disadvantages:
			// - This call will often hang our thread if aTargetWindow is a hung window: ShowWindow(aTargetWindow, SW_MINIMIZE)
			// - Using SW_FORCEMINIMIZE instead of SW_MINIMIZE has at least one (and probably more)
			// side effect: When the window is restored, at least via SW_RESTORE, it is no longer
			// maximized even if it was before the minimize.  So don't use it.
			if (newForegroundWnd == 0) // Not successful yet.
			{
				// Some apps may be intentionally blocking us by having called the API function
				// LockSetForegroundWindow(), for which MSDN says "The system automatically enables
				// calls to SetForegroundWindow if the user presses the ALT key or takes some action
				// that causes the system itself to change the foreground window (for example,
				// clicking a background window)."  Also, it's probably best to avoid doing
				// the 2-alts method except as a last resort, because I think it may mess up
				// the state of menus the user had displayed.  And of course if the foreground
				// app has special handling for alt-key events, it might get confused.
				// My original note: "The 2-alts case seems to mess up on rare occasions,
				// perhaps due to menu weirdness triggered by the alt key."
				// AutoIt3: OK, this is not funny - bring out the extreme measures (usually for 2000/XP).
				// Simulate two single ALT keystrokes.  UPDATE: This hardly ever succeeds.  Usually when
				// it fails, the foreground window is NULL (none).  I'm going to try an Win-tab instead,
				// which selects a task bar button.  This seems less invasive than doing an alt-tab
				// because not only doesn't it activate some other window first, it also doesn't appear
				// to change the Z-order, which is good because we don't want the alt-tab order
				// that the user sees to be affected by this.  UPDATE: Win-tab isn't doing it, so try
				// Alt-tab.  Alt-tab doesn't do it either.  The window itself (metapad.exe is the only
				// culprit window I've found so far) seems to resist being brought to the foreground,
				// but later, after the hotkey is released, it can be.  So perhaps this is being
				// caused by the fact that the user has keys held down (logically or physically?)
				// Releasing those keys with a key-up event might help, so try that sometime:
				sender.SendKeyEvent(KeyEventTypes.KeyDownAndUp, VirtualKeys.VK_MENU);
				sender.SendKeyEvent(KeyEventTypes.KeyDownAndUp, VirtualKeys.VK_MENU);
				// Also replacing "2-alts" with "alt-tab" below, for now:
				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);
			}

			// Very important to detach any threads whose inputs were attached above,
			// prior to returning, otherwise the next attempt to attach thread inputs
			// for these particular windows may result in a hung thread or other
			// undesirable effect:
			if (is_attached_my_to_fore)
				_ = WindowsAPI.AttachThreadInput(mainid, foreThread, false);

			if (isAttachedForeToTarget)
				_ = WindowsAPI.AttachThreadInput(foreThread, targetThread, false);

			// Finally.  This one works, solving the problem of the MessageBox window
			// having the input focus and being the foreground window, but not actually
			// being visible (even though IsVisible() and IsIconic() say it is)!  It may
			// help with other conditions under which this function would otherwise fail.
			// Here's the way the repeat the failure to test how the absence of this line
			// affects things, at least on my XP SP1 system:
			// y::MsgBox, test
			// #e::(some hotkey that activates Windows Explorer)
			// Now: Activate explorer with the hotkey, then invoke the MsgBox.  It will
			// usually be activated but invisible.  Also: Whenever this invisible problem
			// is about to occur, with or without this fix, it appears that the OS's z-order
			// is a bit messed up, because when you dismiss the MessageBox, an unexpected
			// window (probably the one two levels down) becomes active rather than the
			// window that's only 1 level down in the z-order:
			if (newForegroundWnd != 0 && !backgroundActivation) // success.
			{
				// Even though this is already done for the IE 5.5 "hack" above, must at
				// a minimum do it here: The above one may be optional, not sure (safest
				// to leave it unless someone can test with IE 5.5).
				// Note: I suspect the two lines below achieve the same thing.  They may
				// even be functionally identical.  UPDATE: This may no longer be needed
				// now that the first BringWindowToTop(), above, has been disabled due to
				// its causing more trouble than it's worth.  But seems safer to leave
				// this one enabled in case it does resolve IE 5.5 related issues and
				// possible other issues:
				_ = WindowsAPI.BringWindowToTop(targetWindow);
			}

			return newForegroundWnd;//Return this rather than targetWindow because it's more appropriate.
		}

		internal override void ChildFindPoint(PointAndHwnd pah)
		{
			var rect = new RECT();
			_ = WindowsAPI.EnumChildWindows(Handle, (nint hwnd, int lParam) =>
			{
				if (!WindowsAPI.IsWindowVisible(hwnd) // Omit hidden controls, like Window Spy does.
						|| (pah.ignoreDisabled && !WindowsAPI.IsWindowEnabled(hwnd))) // For ControlClick, also omit disabled controls, since testing shows that the OS doesn't post mouse messages to them.
					return true;

				if (!WindowsAPI.GetWindowRect(hwnd, out rect))
					return true;

				// The given point must be inside aWnd's bounds.  Then, if there is no hwnd found yet or if aWnd
				// is entirely contained within the previously found hwnd, update to a "better" found window like
				// Window Spy.  This overcomes the limitations of WindowFromPoint() and ChildWindowFromPoint().
				// The pixel at (left, top) lies inside the control, whereas MSDN says "the pixel at (right, bottom)
				// lies immediately outside the rectangle" -- so use < instead of <= below:
				if (pah.pt.X >= rect.Left && pah.pt.X < rect.Right && pah.pt.Y >= rect.Top && pah.pt.Y < rect.Bottom)
				{
					// If the window's center is closer to the given point, break the tie and have it take
					// precedence.  This solves the problem where a particular control from a set of overlapping
					// controls is chosen arbitrarily (based on Z-order) rather than based on something the
					// user would find more intuitive (the control whose center is closest to the mouse):
					var centerx = rect.Left + ((double)(rect.Right - rect.Left) / 2);
					var centery = rect.Top + ((double)(rect.Bottom - rect.Top) / 2);
					var distance = Math.Sqrt(Math.Pow(pah.pt.X - centerx, 2.0) + Math.Pow(pah.pt.Y - centery, 2.0));
					var updateIt = pah.hwndFound == 0;

					if (!updateIt)
					{
						// If the new window's rect is entirely contained within the old found-window's rect, update
						// even if the distance is greater.  Conversely, if the new window's rect entirely encloses
						// the old window's rect, do not update even if the distance is less:
						if (rect.Left >= pah.rectFound.Left && rect.Right <= pah.rectFound.Right
								&& rect.Top >= pah.rectFound.Top && rect.Bottom <= pah.rectFound.Bottom)
							updateIt = true; // New is entirely enclosed by old: update to the New.
						else if (distance < pah.distanceFound &&
								 (pah.rectFound.Left < rect.Left || pah.rectFound.Right > rect.Right
								  || pah.rectFound.Top < rect.Top || pah.rectFound.Bottom > rect.Bottom))
							updateIt = true; // New doesn't entirely enclose old and new's center is closer to the point.
					}

					if (updateIt)
					{
						pah.hwndFound = hwnd;
						pah.rectFound = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top); // And at least one caller uses this returned rect.
						pah.distanceFound = distance;
					}
				}

				return true;
			}, 0);
		}

		/// <summary>
		/// Left-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal override void Click(Point? location = null)
		{
			SendMouseEvent((uint)MOUSEEVENTF.LEFTDOWN, location);
			SendMouseEvent((uint)MOUSEEVENTF.LEFTUP, location);
		}

		/// <summary>
		/// Right-Clicks on this window/control
		/// </summary>
		/// <param name="location"></param>
		internal override void ClickRight(Point? location = null)
		{
			SendMouseEvent((uint)MOUSEEVENTF.RIGHTDOWN, location);
			SendMouseEvent((uint)MOUSEEVENTF.RIGHTUP, location);
		}

		internal override POINT ClientToScreen()
		{
			var pt = new POINT();
			_ = WindowsAPI.ClientToScreen(Handle, ref pt);
#if DPI
			var scale = 1.0 / Accessors.A_ScaledScreenDPI;
			pt.X = (int)(scale * pt.X);
			pt.Y = (int)(scale * pt.Y);
#endif
			return pt;
		}

		internal override bool Close() => IsSpecified&& WindowsAPI.PostMessage(Handle, WindowsAPI.WM_CLOSE, 0, 0);

		internal uint GetMenuItemId(params object[] items)
		{
			if (!IsSpecified)
				return 0;

			var i1 = 0;
			var menuid = 0xFFFFFFFF;
			nint menu = 0;

			if (items[0].As() == "0&")
			{
				menu = WindowsAPI.GetSystemMenu(Handle, false);
				i1 = 1;
			}
			else
				menu = WindowsAPI.GetMenu(Handle);

			if (menu == 0 || WindowsAPI.GetMenuItemCount(menu) == 0)
				return 0xFFFFFFFF;

			for (; i1 < items.Length; i1++)
			{
				var item = items[i1].As();

				if (item == null || item.Length == 0)
					continue;

				if (item.EndsWith('&') && int.TryParse(item.Trim('&'), out var n) && n > 0)
				{
					n--;
					menuid = WindowsAPI.GetMenuItemID(menu, n);
					menu = WindowsAPI.GetSubMenu(menu, n);

					if (menu == 0)
						break;// return 0xFFFFFFFF;
				}
				else
				{
					var itemct = WindowsAPI.GetMenuItemCount(menu);

					for (var i = 0; i < itemct; i++)
					{
						var buf = new StringBuilder(256);

						if (WindowsAPI.GetMenuString(menu, (uint)i, buf, buf.Capacity - 1, WindowsAPI.MF_BYPOSITION) > 0)
						{
							var matchfound = ControlManagerBase.MenuMatchHelper(buf.ToString(), item);

							if (matchfound)
							{
								menuid = WindowsAPI.GetMenuItemID(menu, i);
								menu = WindowsAPI.GetSubMenu(menu, i);
								break;
							}
						}
					}
				}
			}

			return menuid;
		}

		internal override bool Hide() => IsSpecified&& WindowsAPI.ShowWindow(Handle, WindowsAPI.SW_HIDE);

		internal override bool Kill()
		{
			_ = Close();
			var i = 0;

			while (Exists && i++ < 5)
				Thread.Sleep(0);

			if (!Exists)
				return true;

			var pid = (uint)PID;
			var prc = pid != 0 ? WindowsAPI.OpenProcess(ProcessAccessTypes.PROCESS_ALL_ACCESS, false, pid) : 0;

			if (prc != 0)
			{
				_ = WindowsAPI.TerminateProcess(prc, 0);
				_ = WindowsAPI.CloseHandle(prc);
			}

			return !Exists;
		}

		//internal override WindowItemBase RealChildWindowFromPoint(Point location)
		//{
		//  WindowItemBase child = null;

		//  if (IsSpecified)
		//      child = new WindowItem(WindowsAPI.RealChildWindowFromPoint(Handle, location));

		//  return child;
		//}

		internal override bool Redraw() => IsSpecified&& WindowsAPI.InvalidateRect(Handle, 0, true);

		internal void SendMouseEvent(uint mouseevent, Point? location = null)
		{
			var click = new Point();

			if (location.HasValue)
			{
				click = location.Value;
			}
			else
			{
				// if not specified find middlepoint of this window/control
				var size = Size;
				click.X = size.Width / 2;
				click.Y = size.Height / 2;
			}

			var lparam = new nint(Conversions.MakeInt(click.X, click.Y));
			_ = WindowsAPI.PostMessage(Handle, mouseevent, new nint(1), lparam);
		}

		internal override bool Show()
		{
			var b = IsSpecified && WindowsAPI.ShowWindow(Handle, WindowsAPI.SW_SHOWDEFAULT);
			DoWinDelay();
			return b;
		}

		/// <summary>
		/// Returns null if targetWindow or its owned-window couldn't be brought to the foreground.
		/// Otherwise, on success, it returns either targetWindow or an HWND owned by targetWindow.
		/// Gotten from AHK.
		/// </summary>
		/// <param name="targetWindow"></param>
		/// <param name="foreWindow"></param>
		/// <returns></returns>
		private static nint AttemptSetForeground(nint targetWindow, nint foreWindow)
		{
			_ = WindowsAPI.SetForegroundWindow(targetWindow);
			//Need to be able to set interrupt disable here which prevents both timers and hotkeys from firing while this sleep is happening.//TODO
			_ = Flow.Sleep(10); //The MsgSleep() function in AHK is massive. Unsure how to duplicate here, so just use regular thread sleep.
			var newForeWindow = WindowsAPI.GetForegroundWindow();

			if (newForeWindow == targetWindow)
				return targetWindow;

			// The window we're trying to get to the foreground is the owner of the new foreground window.
			// This is considered to be a success because a window that owns other windows can never be
			// made the foreground window, at least if the windows it owns are visible.
			return newForeWindow != foreWindow && targetWindow == WindowsAPI.GetWindow(newForeWindow, WindowsAPI.GW_OWNER)
				   ? newForeWindow
				   : 0;
		}
	}
}

#endif