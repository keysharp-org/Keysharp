using Keysharp.Builtins;

namespace Keysharp.Internals
{

#if WINDOWS
	internal sealed class WindowsWindow : WindowBase
	{
		private const int Unchanged = WindowInfoBase.Unchanged;

		public override WindowInfoBase CreateWindow(nint id) => new WindowInfo(id);

		public override WindowInfoBase ActiveWindow()
		{
			var fh = GetForegroundHandle();
			return fh == 0 ? new WindowInfo(0) : new WindowInfo(fh);
		}

		// --- granular live reads ---
		public override string GetTitle(nint h) => IsSpecified(h) ? WindowsAPI.GetWindowText(h) : string.Empty;
		public override string GetClassName(nint h) => IsSpecified(h) ? WindowsAPI.GetClassName(h) : string.Empty;

		public override long GetPid(nint h)
		{
			_ = WindowsAPI.GetWindowThreadProcessId(h, out var pid);
			return pid;
		}

		public override Rectangle GetBounds(nint h) => GetWindowBounds(h);

		private static Rectangle GetWindowBounds(nint h)
		{
			if (!IsSpecified(h) || !WindowsAPI.GetWindowRect(h, out var rect))
				return Rectangle.Empty;
			return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
		}

		public override Rectangle GetClientBounds(nint h)
		{
			if (!IsSpecified(h) || !WindowsAPI.GetClientRect(h, out var rect))
				return Rectangle.Empty;

			// GetClientRect is client-relative. Report screen-relative bounds to match the other platforms.
			var pt = ClientToScreen(h);
			return new Rectangle(pt.X, pt.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
		}

		public override long GetStyle(nint h)
			=> IsSpecified(h) ? WindowsAPI.GetWindowLongPtr(h, WindowsAPI.GWL_STYLE).ToInt64() : 0;

		public override long GetExStyle(nint h)
			=> IsSpecified(h) ? WindowsAPI.GetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE).ToInt64() : 0;

		public override bool GetActive(nint h) => IsSpecified(h) && GetForegroundHandle() == h;
		public override bool GetVisible(nint h) => IsSpecified(h) && (TryGetParent(h, out var parent) && parent != 0 ? WindowsAPI.IsWindowVisible(h) : (WindowsAPI.IsWindowVisible(h) && !WindowsAPI.IsWindowCloaked(h)));
		public override bool GetEnabled(nint h) => IsSpecified(h) && WindowsAPI.IsWindowEnabled(h);
		public override bool GetHung(nint h) => h != 0 && WindowsAPI.IsHungAppWindow(h);
		public override bool GetExists(nint h) => IsSpecified(h) && WindowsAPI.IsWindow(h);
		public override FormWindowState GetWindowState(nint h) => !IsSpecified(h) ? FormWindowState.Normal : WindowsAPI.IsIconic(h) ? FormWindowState.Minimized : (WindowsAPI.IsZoomed(h) ? FormWindowState.Maximized : FormWindowState.Normal);
		public override bool GetAlwaysOnTop(nint h) => IsSpecified(h) && (GetExStyle(h) & WindowsAPI.WS_EX_TOPMOST) != 0;

		public override object GetTransparency(nint h)
		{
			if (WindowsAPI.GetLayeredWindowAttributes(h, out _, out var alpha, out var flags))
				if ((flags & WindowsAPI.LWA_ALPHA) == WindowsAPI.LWA_ALPHA)
					return (long)alpha;

			return -1L;
		}

		public override object GetTransparentColor(nint h)
		{
			if (WindowsAPI.GetLayeredWindowAttributes(h, out var key, out _, out var flags))
				if ((flags & WindowsAPI.LWA_COLORKEY) == WindowsAPI.LWA_COLORKEY)
					return key;

			return int.MinValue;
		}

		public override POINT ClientToScreen(nint h)
		{
			var pt = new POINT();
			_ = WindowsAPI.ClientToScreen(h, ref pt);
			return pt;
		}

		public override bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
			text = GetText(h, detectHidden, ThreadAccessors.A_TitleMatchModeSpeed);
			return true;
		}

		public override void ChildFindPoint(nint h, PointAndHwnd pah)
		{
			var rect = new RECT();
			_ = WindowsAPI.EnumChildWindows(h, (nint hwnd, int lParam) =>
			{
				if (!WindowsAPI.IsWindowVisible(hwnd)
					|| (pah.ignoreDisabled && !WindowsAPI.IsWindowEnabled(hwnd)))
					return true;

				if (!WindowsAPI.GetWindowRect(hwnd, out rect))
					return true;

				if (pah.pt.X >= rect.Left && pah.pt.X < rect.Right && pah.pt.Y >= rect.Top && pah.pt.Y < rect.Bottom)
				{
					var centerx = rect.Left + ((double)(rect.Right - rect.Left) / 2);
					var centery = rect.Top + ((double)(rect.Bottom - rect.Top) / 2);
					var distance = Math.Sqrt(Math.Pow(pah.pt.X - centerx, 2.0) + Math.Pow(pah.pt.Y - centery, 2.0));
					var updateIt = pah.hwndFound == 0;

					if (!updateIt)
					{
						if (rect.Left >= pah.rectFound.Left && rect.Right <= pah.rectFound.Right
							&& rect.Top >= pah.rectFound.Top && rect.Bottom <= pah.rectFound.Bottom)
							updateIt = true;
						else if (distance < pah.distanceFound &&
								 (pah.rectFound.Left < rect.Left || pah.rectFound.Right > rect.Right
								  || pah.rectFound.Top < rect.Top || pah.rectFound.Bottom > rect.Bottom))
							updateIt = true;
					}

					if (updateIt)
					{
						pah.hwndFound = hwnd;
						pah.rectFound = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
						pah.distanceFound = distance;
					}
				}

				return true;
			}, 0);
		}

		public override bool TryClientToScreen(nint h, ref Point pt)
		{
			var o = ClientToScreen(h);
			pt = new Point(pt.X + o.X, pt.Y + o.Y);
			return true;
		}

		public override bool TryGetParent(nint h, out nint parent)
		{
			parent = WindowsAPI.GetAncestor(h, gaFlags.GA_PARENT);
			return parent != 0;
		}

		public override bool TryGetTopLevel(nint h, out nint top)
		{
			top = WindowsAPI.GetNonChildParent(h);
			return top != 0;
		}

		public override bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
			var set = new HashSet<nint>(64);

			if (IsSpecified(h))
			{
				_ = WindowsAPI.EnumChildWindows(h, (nint hwnd, int lParam) =>
				{
					_ = set.Add(hwnd);
					return true;
				}, 0);
			}

			// EnumChildWindows can miss controls for never-shown WinForms windows.
			if (Control.FromHandle(h) is Form form)
			{
				form.Invoke(() =>
				{
					foreach (var ctrl in form.GetAllControlsRecursive<Control>())
						_ = set.Add(ctrl.Handle);
				});
			}

			children = set.ToList();
			return true;
		}

		// --- control ---
		public override bool TrySetAlwaysOnTop(nint h, bool onTop)
		{
			if (IsSpecified(h))
			{
				var type = new nint(onTop ? WindowsAPI.HWND_TOPMOST : WindowsAPI.HWND_NOTOPMOST);
				_ = WindowsAPI.SetWindowPos(h, type, 0, 0, 0, 0, WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE);
			}

			return true;
		}

		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			SetBounds(h, bounds);
			return true;
		}

		public override bool TrySetState(nint h, FormWindowState state)
		{
			if (IsSpecified(h))
			{
				var cmd = WindowsAPI.SW_NORMAL;

				switch (state)
				{
					case FormWindowState.Maximized: cmd = WindowsAPI.SW_MAXIMIZE; break;
					case FormWindowState.Minimized: cmd = WindowsAPI.SW_MINIMIZE; break;
				}

				_ = WindowsAPI.ShowWindow(h, cmd);
			}

			return true;
		}

		public override bool TryRestore(nint h)
		{
			// SW_NORMAL also unmaximizes a minimized window; SW_RESTORE preserves its prior state.
			if (IsSpecified(h) && !GetHung(h))
				_ = WindowsAPI.ShowWindow(h, WindowsAPI.SW_RESTORE);

			return true;
		}

		public override bool TryUnminimize(nint h)
			=> !WindowsAPI.IsIconic(h) || TryRestore(h);

		public override bool TrySetStyle(nint h, long style)
		{
			ApplyWindowLongAndRefresh(h, WindowsAPI.GWL_STYLE, style);
			return true;
		}

		public override bool TrySetExStyle(nint h, long exStyle)
		{
			ApplyWindowLongAndRefresh(h, WindowsAPI.GWL_EXSTYLE, exStyle);
			return true;
		}

		public override bool TrySetTransparency(nint h, object alpha)
		{
			SetTransparency(h, alpha);
			return true;
		}

		public override bool TryActivate(nint h)
		{
			if (IsSpecified(h))
			{
				if (GetWindowState(h) == FormWindowState.Minimized)
					_ = WindowsAPI.ShowWindow(h, WindowsAPI.SW_RESTORE);

				if (GetForegroundHandle() != h)
					_ = SetForegroundWindowEx(h);
			}

			return true;
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (IsSpecified(h))
			{
				var type = new nint(z == ZOrder.Bottom ? WindowsAPI.HWND_BOTTOM : WindowsAPI.HWND_TOP);
				_ = WindowsAPI.SetWindowPos(h, type, 0, 0, 0, 0, WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOACTIVATE);
			}

			return true;
		}

		public override bool TryClose(nint h) => IsSpecified(h) && WindowsAPI.PostMessage(h, WindowsAPI.WM_CLOSE, 0, 0);

		public override bool TryKill(nint h)
		{
			_ = TryClose(h);

			// Give a responsive app time to process WM_CLOSE and shut down gracefully (saving prompts, etc.)
			// before escalating to a hard TerminateProcess. AHK waits ~500ms; poll with a real delay so we
			// don't force-kill a healthy window that simply hasn't pumped its message queue yet. Use the
			// pump-aware sleep (like Mac's TryKill and the rest of this file) so WinKill on our own pump thread
			// keeps timers/hotkeys running AND lets the script's own window actually dispatch the WM_CLOSE it
			// was posted — a plain Thread.Sleep would starve that dispatch and hard-kill our own process.
			for (var waited = 0; GetExists(h) && waited < 500; waited += 10)
				Flow.SleepWithoutInterruption(10);

			if (!GetExists(h))
				return true;

			var pid = (uint)GetPid(h);
			var prc = pid != 0 ? WindowsAPI.OpenProcess(ProcessAccessTypes.PROCESS_ALL_ACCESS, false, pid) : 0;

			if (prc != 0)
			{
				_ = WindowsAPI.TerminateProcess(prc, 0);
				_ = WindowsAPI.CloseHandle(prc);
			}

			return !GetExists(h);
		}

		// ShowWindow's return value is the window's PREVIOUS visibility, not success — never propagate it.
		public override bool TryHide(nint h)
		{
			if (!IsSpecified(h))
				return false;

			_ = WindowsAPI.ShowWindow(h, WindowsAPI.SW_HIDE);
			return true;
		}

		public override bool TryShow(nint h)
		{
			if (!IsSpecified(h))
				return false;

			_ = WindowsAPI.ShowWindow(h, WindowsAPI.SW_SHOWDEFAULT);
			return true;
		}
		public override bool TryRedraw(nint h) => IsSpecified(h) && WindowsAPI.InvalidateRect(h, 0, true);

		public override bool TryClick(nint h, Point at, uint button, int count)
		{
			for (var i = 0; i < count; i++)
			{
				switch (button)
				{
					case 2:  // right
						SendMouseEvent(h, WindowsAPI.WM_RBUTTONDOWN, WindowsAPI.MK_RBUTTON, at);
						SendMouseEvent(h, WindowsAPI.WM_RBUTTONUP, 0, at);
						break;

					case 3:  // middle
						SendMouseEvent(h, WindowsAPI.WM_MBUTTONDOWN, WindowsAPI.MK_MBUTTON, at);
						SendMouseEvent(h, WindowsAPI.WM_MBUTTONUP, 0, at);
						break;

					default:  // left
						SendMouseEvent(h, WindowsAPI.WM_LBUTTONDOWN, WindowsAPI.MK_LBUTTON, at);
						SendMouseEvent(h, WindowsAPI.WM_LBUTTONUP, 0, at);
						break;
				}
			}

			return true;
		}

		public override bool TrySetTitle(nint h, string title)
		{
			if (IsSpecified(h))
				_ = WindowsAPI.SetWindowText(h, title ?? string.Empty);

			return true;
		}

		public override bool TrySetVisible(nint h, bool visible)
		{
			_ = visible ? TryShow(h) : TryHide(h);
			return true;
		}

		public override bool TrySetEnabled(nint h, bool enabled)
		{
			if (IsSpecified(h))
				_ = WindowsAPI.EnableWindow(h, enabled);

			return true;
		}

		public override bool TrySetTransparentColor(nint h, object color)
		{
			SetTransparentColor(h, color);
			return true;
		}

		// Native by-class (+ exact title) lookup: one Win32 FindWindow instead of an EnumWindows scan. Returning
		// true with handle 0 tells WindowQuery there is no such window at all, so it skips the full enumeration.
		public override bool TryFindWindow(string className, string title, out nint handle)
		{
			var hwnd = WindowsAPI.FindWindow(string.IsNullOrEmpty(className) ? null : className,
											string.IsNullOrEmpty(title) ? null : title);
			handle = hwnd;
			return true;
		}

		public override nint GetForegroundHandle() => WindowsAPI.GetForegroundWindow();

		public override bool IsWindow(nint h) => WindowsAPI.IsWindow(h) || h == WindowsAPI.HWND_BROADCAST;

		public override uint GetFocusedControlThread(nint window = 0)
		{
			var aWindow = window;
			var threadId = 0u;

			if (aWindow == 0)
			{
				var info = GUITHREADINFO.Default;

				if (WindowsAPI.GetGUIThreadInfo(0, ref info))
				{
					var hwnd = info.hwndFocus != 0 ? info.hwndFocus : info.hwndActive;

					if (hwnd != 0)
						return WindowsAPI.GetWindowThreadProcessId(hwnd, out _);
				}
				aWindow = WindowsAPI.GetForegroundWindow();
			}

			if (aWindow != 0)
			{
				threadId = WindowsAPI.GetWindowThreadProcessId(aWindow, out var _);
				var info = GUITHREADINFO.Default;

				if (WindowsAPI.GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != 0)
				{
					threadId = WindowsAPI.GetWindowThreadProcessId(info.hwndFocus, out var _);
				}
			}

			return threadId;
		}

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
		{
			var list = new List<WindowInfoBase>();
			_ = WindowsAPI.EnumWindows(delegate (nint hwnd, int lParam)
			{
				if (includeHidden)
					list.Add(new WindowInfo(hwnd));
				else if (WindowsAPI.IsWindowVisible(hwnd) && !WindowsAPI.IsWindowCloaked(hwnd))
					list.Add(new WindowInfo(hwnd, visible: true));

				return true;
			}, 0);
			return list;
		}

		public override bool TryGetAt(int x, int y, out nint child)
		{
			var ctrl = WindowsAPI.WindowFromPoint(new POINT(x, y));
			child = ctrl;
			return ctrl != 0;
		}

		public override bool IncludeInGroups(nint h)
		{
			var exstyle = GetExStyle(h);
			return GetEnabled(h)
				   && (exstyle & WindowsAPI.WS_EX_TOPMOST) == 0
				   && (exstyle & WindowsAPI.WS_EX_NOACTIVATE) == 0
				   && (exstyle & (WindowsAPI.WS_EX_TOOLWINDOW | WindowsAPI.WS_EX_APPWINDOW)) != WindowsAPI.WS_EX_TOOLWINDOW
				   && WindowsAPI.IsWindowVisible(h) && !WindowsAPI.IsWindowCloaked(h)
				   && WindowsAPI.GetLastActivePopup(h) != h
				   && WindowsAPI.GetWindow(h, WindowsAPI.GW_OWNER) == 0
				   && WindowsAPI.GetShellWindow() != h;
		}

		private static bool IsSpecified(nint h) => h != 0;

		private static void ApplyWindowLongAndRefresh(nint h, int index, long value)
		{
			if (!IsSpecified(h))
				return;

			_ = WindowsAPI.SetWindowLongPtr(h, index, new nint(value));
			_ = WindowsAPI.SetWindowPos(h, 0, 0, 0, 0, 0,
				(uint)(WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE | WindowsAPI.SWP_NOZORDER | WindowsAPI.SWP_FRAMECHANGED | WindowsAPI.SWP_NOACTIVATE));
			_ = WindowsAPI.InvalidateRect(h, 0, true);
		}

		private static List<string> GetText(nint h, bool detectHiddenText, bool fast)
		{
			if (!IsSpecified(h))
				return [];

			var items = new List<string>(64);
			_ = WindowsAPI.EnumChildWindows(h, (nint hwnd, int lParam) =>
			{
				if (detectHiddenText || WindowsAPI.IsWindowVisible(hwnd))
				{
					var text = fast ? WindowsAPI.GetWindowText(hwnd) : WindowsAPI.GetWindowTextTimeout(hwnd, 5000);
					items.Add(text);
				}

				return true;
			}, 0);
			return items;
		}

		private static void SetBounds(nint h, Rectangle value)
		{
			if (!IsSpecified(h))
				return;

			var setPos  = value.X != Unchanged || value.Y != Unchanged;
			var setSize = value.Width != Unchanged || value.Height != Unchanged;

			if (!setPos && !setSize)
				return;

			int curX = 0, curY = 0, curW = 0, curH = 0;

			if ((setPos && (value.X == Unchanged || value.Y == Unchanged))
				|| (setSize && (value.Width == Unchanged || value.Height == Unchanged)))
			{
				if (!WindowsAPI.GetWindowRect(h, out var rect))
					return;

				curX = rect.Left;
				curY = rect.Top;
				curW = rect.Right - rect.Left;
				curH = rect.Bottom - rect.Top;
			}

			var x = value.X == Unchanged ? curX : value.X;
			var y = value.Y == Unchanged ? curY : value.Y;
			var w = value.Width == Unchanged ? curW : value.Width;
			var height = value.Height == Unchanged ? curH : value.Height;
			var flags = (uint)(WindowsAPI.SWP_NOZORDER | WindowsAPI.SWP_NOACTIVATE
							   | (setPos ? 0 : WindowsAPI.SWP_NOMOVE)
							   | (setSize ? 0 : WindowsAPI.SWP_NOSIZE));

			if (!WindowsAPI.SetWindowPos(h, 0, x, y, w, height, flags))
				_ = Errors.OSErrorOccurred(new Win32Exception(Marshal.GetLastWin32Error()));
		}

		private static void SetTransparency(nint h, object value)
		{
			var exstyle = WindowsAPI.GetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE).ToInt64();

			if (value is string s)
			{
				if (s.ToLower() == "off")
					_ = WindowsAPI.SetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE, new nint(exstyle & ~WindowsAPI.WS_EX_LAYERED));
			}
			else
			{
				var alpha = Math.Clamp((int)value.Al(), 0, 255);

				if (WindowsAPI.SetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE, new nint(exstyle | WindowsAPI.WS_EX_LAYERED)) == 0 ||
					!WindowsAPI.SetLayeredWindowAttributes(h, 0, (byte)alpha, WindowsAPI.LWA_ALPHA))
					_ = Errors.OSErrorOccurred("", $"Could not assign transparency with alpha value of {alpha}.");
			}
		}

		private static void SetTransparentColor(nint h, object value)
		{
			var splits = value.As().Split(SpaceTab, StringSplitOptions.RemoveEmptyEntries);
			var colorstr = splits[0];
			var exstyle = WindowsAPI.GetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE);

			if (colorstr.ToLower() == "off")
			{
				if (WindowsAPI.SetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE, new nint(exstyle.ToInt64() & ~WindowsAPI.WS_EX_LAYERED)) == 0)
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

					if (WindowsAPI.SetWindowLongPtr(h, WindowsAPI.GWL_EXSTYLE, new nint(exstyle.ToInt64() | WindowsAPI.WS_EX_LAYERED)) != 0)
					{
						color = Color.FromArgb(color.A, color.B, color.G, color.R);

						if (!WindowsAPI.SetLayeredWindowAttributes(h, (uint)color.ToArgb() & 0x00FFFFFF, (byte)val, (uint)flags))
							_ = Errors.OSErrorOccurred("", $"Could not assign transparency color {color} with alpha value of {val}.");
					}
					else
						_ = Errors.OSErrorOccurred("", $"Could not assign transparency color {color} with alpha value of {val}.");
				}
			}
		}

		// message is a WM_*BUTTONDOWN/UP window message (NOT a MOUSEEVENTF flag — those numerically collide with
		// WM_DESTROY/WM_CLOSE and would destroy/close the target). keyFlags carries the MK_* button state in wParam
		// and the click position goes in lParam, as the mouse window messages expect.
		private static void SendMouseEvent(nint h, int message, int keyFlags, Point? location = null)
		{
			var size = GetWindowBounds(h).Size;
			var click = location ?? new Point(size.Width / 2, size.Height / 2);
			var lparam = new nint(Conversions.MakeInt(click.X, click.Y));
			_ = WindowsAPI.PostMessage(h, (uint)message, new nint(keyFlags), lparam);
		}

		private static nint SetForegroundWindowEx(nint targetWindow, bool backgroundActivation = false)
		{
			if (targetWindow == 0)
				return 0;

			var script = Script.TheScript;
			var mainid = script.NativeMainThreadID;
			var targetThread = WindowsAPI.GetWindowThreadProcessId(targetWindow, out var _);

			if (targetThread != mainid && WindowsAPI.IsHungAppWindow(targetWindow))
				return 0;

			var origForegroundWnd = WindowsAPI.GetForegroundWindow();
			var sender = script.HookThread.kbdMsSender;

			if (WindowsAPI.IsIconic(targetWindow) && !backgroundActivation)
				_ = WindowsAPI.ShowWindow(targetWindow, WindowsAPI.SW_RESTORE);

			if (targetWindow == origForegroundWnd)
				return targetWindow;

			nint newForegroundWnd = 0;

			if (!script.WinActivateForce)
			{
				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);

				if (newForegroundWnd != 0)
					return newForegroundWnd;
			}

			bool isAttachedMyToFore = false, isAttachedForeToTarget = false;
			uint foreThread = 0;

			if (origForegroundWnd != 0)
			{
				foreThread = WindowsAPI.GetWindowThreadProcessId(origForegroundWnd, out var _);

				if (foreThread != 0 && mainid != foreThread && !WindowsAPI.IsHungAppWindow(origForegroundWnd))
					isAttachedMyToFore = WindowsAPI.AttachThreadInput(mainid, foreThread, true);

				if (foreThread != 0 && targetThread != 0 && foreThread != targetThread)
					isAttachedForeToTarget = WindowsAPI.AttachThreadInput(foreThread, targetThread, true);
			}

			var activateforce = script.WinActivateForce ? 1 : 0;

			for (var i = 0; i < 5; ++i)
			{
				if (i == activateforce && !sender.triedKeyUp)
				{
					sender.triedKeyUp = true;
					sender.SendKeyEvent(KeyEventTypes.KeyUp, VirtualKeys.VK_MENU, 0, 0, false, KeyboardMouseSender.KeyBlockThis);
				}

				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);

				if (newForegroundWnd != 0)
					break;
			}

			if (newForegroundWnd == 0)
			{
				sender.SendKeyEvent(KeyEventTypes.KeyDownAndUp, VirtualKeys.VK_MENU);
				sender.SendKeyEvent(KeyEventTypes.KeyDownAndUp, VirtualKeys.VK_MENU);
				newForegroundWnd = AttemptSetForeground(targetWindow, origForegroundWnd);
			}

			if (isAttachedMyToFore)
				_ = WindowsAPI.AttachThreadInput(mainid, foreThread, false);

			if (isAttachedForeToTarget)
				_ = WindowsAPI.AttachThreadInput(foreThread, targetThread, false);

			if (newForegroundWnd != 0 && !backgroundActivation)
				_ = WindowsAPI.BringWindowToTop(targetWindow);

			return newForegroundWnd;
		}

		private static nint AttemptSetForeground(nint targetWindow, nint foreWindow)
		{
			_ = WindowsAPI.SetForegroundWindow(targetWindow);
			Flow.SleepWithoutInterruption(10);
			var newForeWindow = WindowsAPI.GetForegroundWindow();

			if (newForeWindow == targetWindow)
				return targetWindow;

			return newForeWindow != foreWindow && targetWindow == WindowsAPI.GetWindow(newForeWindow, WindowsAPI.GW_OWNER)
				   ? newForeWindow
				   : 0;
		}
	}
#endif
}
