using Keysharp.Builtins;
#if WINDOWS
using Keysharp.Internals.Window.Windows;
using static Keysharp.Internals.Input.Mouse.MouseUtils;

namespace Keysharp.Internals.Os.Windows
{
	/// <summary>
	/// Concrete implementation of ControlManager for the Windows platfrom.
	/// </summary>
	internal class ControlManager : ControlManagerBase
	{
		internal override long ControlAddItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.CB_ADDSTRING;
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.LB_ADDSTRING;
				else
					return (long)Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List", DefaultErrorLong);

				long res;

				if (OwnListItems(item.Handle) is IList items)
					res = items.Add(str);
				else
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, 0, str, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
						return (long)Errors.TargetErrorOccurred($"Could not add {str} to combo or list box", title, text, excludeTitle, excludeText, DefaultErrorLong);

					res = result.ToInt64();

					if (res == WindowsAPI.CB_ERR || res == WindowsAPI.CB_ERRSPACE)
						return (long)Errors.ErrorOccurred("Failed", DefaultErrorLong);
				}

				WindowInfoBase.DoControlDelay();
				return res + 1L;
			}

			return 0L;
		}

		/// <summary>
		/// The items of the script's own ComboBox or ListBox, which are changed there rather than by message so that
		/// the control's Items stay in step with its native list; null for any other control.
		/// </summary>
		private static IList OwnListItems(nint handle) => Control.FromHandle(handle) switch
		{
			ComboBox cb => cb.Items,
			ListBox lb => lb.Items,
			_ => null
		};

		internal override void ControlChooseIndex(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0, x_msg = 0, y_msg = 0;
				n--;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.CB_SETCURSEL;
					x_msg = WindowsAPI.CBN_SELCHANGE;
					y_msg = WindowsAPI.CBN_SELENDOK;
				}
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
				{
					msg = (WindowsAPI.GetWindowLongPtr(item.Handle, WindowsAPI.GWL_STYLE).ToInt64() & (WindowsAPI.LBS_EXTENDEDSEL | WindowsAPI.LBS_MULTIPLESEL)) != 0
						  ? WindowsAPI.LB_SETSEL
						  : (uint)WindowsAPI.LB_SETCURSEL;
					x_msg = WindowsAPI.LBN_SELCHANGE;
					y_msg = WindowsAPI.LBN_DBLCLK;
				}
				else if (item.ClassName.Contains("Tab", StringComparison.OrdinalIgnoreCase))
				{
					if (!WindowsAPI.ControlSetTab(item.Handle, n))
					{
						_ = Errors.TargetErrorOccurred($"Could not set tab index to {n}", title, text, excludeTitle, excludeText);
						return;
					}
				}
				else
				{
					_ = Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo, List or Tab");
					return;
				}

				nint result;

				if (msg == WindowsAPI.LB_SETSEL)//Multi-select, so use the cumulative method.
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, n != -1 ? 1 : 0, new nint(n), SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out result) == 0)
					{
						_ = Errors.TargetErrorOccurred($"Could not set list box selection index to {n}", title, text, excludeTitle, excludeText);
						return;
					}
				}
				else//ComboBox or single-select ListBox.
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, n, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out result) == 0)
					{
						_ = Errors.TargetErrorOccurred($"Could not set combo or single selection list box index to {n}", title, text, excludeTitle, excludeText);
						return;
					}
				}

				if (result.ToInt64() == WindowsAPI.CB_ERR && n != -1)//CB_ERR == LB_ERR
				{
					_ = Errors.ErrorOccurred("Failed");
					return;
				}

				NotifyParent(item.Handle, x_msg, y_msg);
				WindowInfoBase.DoControlDelay();
			}
		}

		internal override long ControlChooseString(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0, x_msg = 0, y_msg = 0;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.CB_SELECTSTRING;
					x_msg = WindowsAPI.CBN_SELCHANGE;
					y_msg = WindowsAPI.CBN_SELENDOK;
				}
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
				{
					msg = (WindowsAPI.GetWindowLongPtr(item.Handle, WindowsAPI.GWL_STYLE).ToInt64() & (WindowsAPI.LBS_EXTENDEDSEL | WindowsAPI.LBS_MULTIPLESEL)) != 0
						  ? WindowsAPI.LB_FINDSTRING
						  : (uint)WindowsAPI.LB_SELECTSTRING;
					x_msg = WindowsAPI.LBN_SELCHANGE;
					y_msg = WindowsAPI.LBN_DBLCLK;
				}
				else
					return (long)Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List", DefaultErrorLong);

				nint item_index;

				if (msg == WindowsAPI.LB_FINDSTRING)//Multi-select ListBox (LB_SELECTSTRING is not supported by these).
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, -1, str, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out item_index) == 0)
						return (long)Errors.TargetErrorOccurred($"Could not set list box selection index to {str}", title, text, excludeTitle, excludeText, DefaultErrorLong);

					if (item_index.ToInt64() == WindowsAPI.LB_ERR)
						return (long)Errors.TargetErrorOccurred($"Erroneous item index when setting list box selection index to {str}", title, text, excludeTitle, excludeText, DefaultErrorLong);

					if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LB_SETSEL, 1, item_index, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
						return (long)Errors.TargetErrorOccurred($"Could not set list box selection index to {item_index.ToInt64()}", title, text, excludeTitle, excludeText, DefaultErrorLong);

					if (result.ToInt64() == WindowsAPI.LB_ERR)
						return (long)Errors.TargetErrorOccurred($"Erroneous item index when setting list box selection index to {item_index.ToInt64()}", title, text, excludeTitle, excludeText, DefaultErrorLong);
				}
				else//ComboBox or single-select ListBox.
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, -1, str, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out item_index) == 0)
						return (long)Errors.TargetErrorOccurred($"Could not set combo box selection index to {str}", title, text, excludeTitle, excludeText, DefaultErrorLong);

					if (item_index.ToInt64() == WindowsAPI.CB_ERR) // CB_ERR == LB_ERR
						return (long)Errors.TargetErrorOccurred($"Erroneous item index when setting combo box selection index to {str}", title, text, excludeTitle, excludeText, DefaultErrorLong);
				}

				NotifyParent(item.Handle, x_msg, y_msg);
				WindowInfoBase.DoControlDelay();
				return item_index.ToInt64() + 1;
			}

			return 0L;
		}

		internal override void ControlClick(object ctrlorpos, object title, object text, string whichButton, int clickCount, string options, object excludeTitle, object excludeText)
		{
			var winx = int.MinValue;
			var winy = int.MinValue;
			var ctrlx = int.MinValue;
			var ctrly = int.MinValue;
			var vk = HookThread.ConvertMouseButton(whichButton);
			var posoverride = options.Contains("pos", StringComparison.OrdinalIgnoreCase);
			bool d = false, u = false, na = false;

			foreach (Range r in options.AsSpan().SplitAny(Spaces))
			{
				var opt = options.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (opt.Equals("d", StringComparison.OrdinalIgnoreCase))
						d = true;
					else if (opt.Equals("u", StringComparison.OrdinalIgnoreCase))
						u = true;
					else if (opt.Equals("na", StringComparison.OrdinalIgnoreCase))
						na = true;
					else if (Options.TryParse(opt, "x", ref ctrlx)) { }
					else if (Options.TryParse(opt, "y", ref ctrly)) { }
				}
			}

			if (d)
				u = false;

			if (u)
				d = false;

			if (ctrlorpos is string s && s.StartsWith("x", StringComparison.OrdinalIgnoreCase) && s.Contains(' ') && s.Contains('y', StringComparison.OrdinalIgnoreCase))
			{
				foreach (Range r in s.AsSpan().SplitAny(Spaces))
				{
					var opt = s.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						if (Options.TryParse(opt, "x", ref winx)) { }
						else if (Options.TryParse(opt, "y", ref winy)) { }
					}
				}
			}

			// The target window is kept apart from the control, as AHK's DetermineTargetControl gives them, because it
			// is the window that gets activated. A handle names both.
			WindowInfoBase target;
			nint control = 0;

			if (!posoverride)
			{
				if (!WindowSearch.TrySearchControl(ctrlorpos, title, text, excludeTitle, excludeText, out var item, out target))
					return;

				control = item?.Handle ?? 0;
			}
			else
			{
				if (WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true) is not WindowInfoBase win)
					return;

				target = win;
			}

			// Read as "xN yN" only once no control matched, so a control whose class or text looks like that comes first.
			if (control == 0)
			{
				if (winx == int.MinValue || winy == int.MinValue)
				{
					_ = Errors.TargetErrorOccurred($"Could not get control {ctrlorpos}", title, text, excludeTitle, excludeText);
					return;
				}

				var origin = target.ClientToScreen();
				var pah = new PointAndHwnd(new POINT(origin.X + winx, origin.Y + winy)) { ignoreDisabled = true };
				Platform.Window.ChildFindPoint(target.Handle, pah);
				control = pah.hwndFound != 0 ? pah.hwndFound : target.Handle;
				// The point overrides any X and Y option, converted to the client area of whichever window gets the click.
				var point = pah.pt;
				_ = WindowsAPI.ScreenToClient(control, ref point);
				ctrlx = point.X;
				ctrly = point.Y;
			}

			if (clickCount < 1)
			{
				// Zero does nothing, so a count read from a variable can turn the click off.
				if (clickCount < 0)
					_ = Errors.InvalidParameterErrorOccurred(5, "ControlClick", clickCount);

				return;
			}

			if (ctrlx == int.MinValue || ctrly == int.MinValue)
			{
				if (!WindowsAPI.GetWindowRect(control, out var temprect))
				{
					_ = Errors.TargetErrorOccurred($"Could not get control rect {ctrlorpos}", title, text, excludeTitle, excludeText);
					return;
				}

				if (ctrlx == int.MinValue)
					ctrlx = (temprect.Right - temprect.Left) / 2;

				if (ctrly == int.MinValue)
					ctrly = (temprect.Bottom - temprect.Top) / 2;
			}

			var click = new POINT(ctrlx, ctrly);
			uint msg_down = 0, msg_up = 0;
			uint wparam = 0, wparam_up = 0;
			var vk_is_wheel = vk == VirtualKeys.VK_WHEEL_UP || vk == VirtualKeys.VK_WHEEL_DOWN;
			var vk_is_hwheel = vk == VirtualKeys.VK_WHEEL_LEFT || vk == VirtualKeys.VK_WHEEL_RIGHT;

			if (vk_is_wheel)
			{
				_ = WindowsAPI.ClientToScreen(control, ref click); // Wheel messages use screen coordinates.
				wparam = (uint)(clickCount * ((vk == VirtualKeys.VK_WHEEL_UP) ? WHEEL_DELTA : -WHEEL_DELTA)) << 16;  // High order word contains the delta.
				msg_down = WindowsAPI.WM_MOUSEWHEEL;
			}
			else if (vk_is_hwheel)  // Lexikos: Support horizontal scrolling in Windows Vista and later.
			{
				wparam = (uint)(clickCount * ((vk == VirtualKeys.VK_WHEEL_LEFT) ? -WHEEL_DELTA : WHEEL_DELTA)) << 16;
				msg_down = WindowsAPI.WM_MOUSEHWHEEL;
			}
			else
			{
				switch (vk)
				{
					case VirtualKeys.VK_LBUTTON: msg_down = WindowsAPI.WM_LBUTTONDOWN; msg_up = WindowsAPI.WM_LBUTTONUP; wparam = WindowsAPI.MK_LBUTTON; break;

					case VirtualKeys.VK_RBUTTON: msg_down = WindowsAPI.WM_RBUTTONDOWN; msg_up = WindowsAPI.WM_RBUTTONUP; wparam = WindowsAPI.MK_RBUTTON; break;

					case VirtualKeys.VK_MBUTTON: msg_down = WindowsAPI.WM_MBUTTONDOWN; msg_up = WindowsAPI.WM_MBUTTONUP; wparam = WindowsAPI.MK_MBUTTON; break;

					case VirtualKeys.VK_XBUTTON1: msg_down = WindowsAPI.WM_XBUTTONDOWN; msg_up = WindowsAPI.WM_XBUTTONUP; wparam_up = VirtualKeys.VK_XBUTTON1 << 16; wparam = WindowsAPI.MK_XBUTTON1 | wparam_up; break;

					case VirtualKeys.VK_XBUTTON2: msg_down = WindowsAPI.WM_XBUTTONDOWN; msg_up = WindowsAPI.WM_XBUTTONUP; wparam_up = XBUTTON2 << 16; wparam = WindowsAPI.MK_XBUTTON2 | wparam_up; break;
				}
			}

			var lparam = KeyboardUtils.MakeLong((short)click.X, (short)click.Y);
			(bool, uint) thinfo = (false, 0);

			if (!na)
			{
				thinfo = WindowsAPI.AttachThreadInput(target.Handle, true);
			}

			if (vk_is_wheel || vk_is_hwheel) // v1.0.48: Lexikos: Support horizontal scrolling in Windows Vista and later.
			{
				_ = WindowsAPI.PostMessage(control, msg_down, wparam, lparam);
				WindowInfoBase.DoControlDelay();
			}
			else
			{
				for (var i = 0; i < clickCount; ++i)
				{
					if (!u) // It's either down-only or up-and-down so always to the down-event.
					{
						_ = WindowsAPI.PostMessage(control, msg_down, wparam, lparam);
						WindowInfoBase.DoControlDelay();
					}

					if (!d) // It's either up-only or up-and-down so always to the up-event.
					{
						_ = WindowsAPI.PostMessage(control, msg_up, wparam_up, lparam);
						WindowInfoBase.DoControlDelay();
					}
				}
			}

			if (!na)
				WindowsAPI.DetachThreadInput(thinfo.Item1, thinfo.Item2);
		}

		internal override void ControlDeleteItem(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg;
				n--;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.CB_DELETESTRING;
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.LB_DELETESTRING;
				else
				{
					_ = Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List");
					return;
				}

				bool deleted;

				if (OwnListItems(item.Handle) is IList items)
				{
					deleted = n >= 0 && n < items.Count;

					if (deleted)
						items.RemoveAt(n);
				}
				else
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, msg, n, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
					{
						_ = Errors.TargetErrorOccurred($"Could not delete combo or list box index {n}", title, text, excludeTitle, excludeText);
						return;
					}

					deleted = result.ToInt64() != WindowsAPI.CB_ERR; // CB_ERR == LB_ERR
				}

				if (!deleted)
				{
					_ = Errors.TargetErrorOccurred($"Erroneous item index when deleting combo or list box selection index to {n}", title, text, excludeTitle, excludeText);
					return;
				}

				WindowInfoBase.DoControlDelay();
			}
		}

		internal override long ControlFindItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.CB_FINDSTRINGEXACT;
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.LB_FINDSTRINGEXACT;
				else
					return (long)Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List", DefaultErrorLong);

				if (WindowsAPI.SendMessageTimeout(item.Handle, msg, -1, str, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var index) == 0 || index.ToInt64() == WindowsAPI.CB_ERR) // CB_ERR == LB_ERR
					return (long)Errors.TargetErrorOccurred($"Could not search for combo or list box item string {str}", title, text, excludeTitle, excludeText, DefaultErrorLong);

				// No A_ControlDelay: AHK's ControlFindItem is a query and does not call DoControlDelay.
				return index.ToInt64() + 1;
			}

			return 0L;
		}

		internal override void ControlFocus(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				var thinfo = WindowsAPI.AttachThreadInput(item.Handle, true);
				_ = WindowsAPI.SetFocus(item.Handle);
				WindowInfoBase.DoControlDelay();
				WindowsAPI.DetachThreadInput(thinfo.Item1, thinfo.Item2);
			}
		}

		internal override long ControlGetChecked(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is CheckBox cb)
					return cb.CheckState == CheckState.Checked ? 1L : 0L;

				if (!WindowsAPI.TryGetChecked(item.Handle, out var isChecked))
					return (long)Errors.OSErrorOccurred("", $"Could not get the check state of the control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", 0L);

				return isChecked ? 1L : 0L;
			}

			return 0L;
		}

		internal override string ControlGetChoice(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0, x_msg = 0, y_msg = 0;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.CB_GETCURSEL;
					x_msg = WindowsAPI.CB_GETLBTEXTLEN;
					y_msg = WindowsAPI.CB_GETLBTEXT;
				}
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.LB_GETCURSEL;
					x_msg = WindowsAPI.LB_GETTEXTLEN;
					y_msg = WindowsAPI.LB_GETTEXT;
				}
				else
					return (string)Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List", DefaultErrorString);

				if (WindowsAPI.SendMessageTimeout(item.Handle, msg, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var index) == 0
						|| index.ToInt64() == WindowsAPI.CB_ERR  // CB_ERR == LB_ERR.  There is no selection (or very rarely, some other type of problem).
						|| WindowsAPI.SendMessageTimeout(item.Handle, x_msg, index, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var length) == 0
						|| length.ToInt64() == WindowsAPI.CB_ERR)  // CB_ERR == LB_ERR
					return (string)Errors.ErrorOccurred($"Could not get selected item string for combo or list box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorString);

				var buffer = new char[length.ToInt32() + 1];//The text is written with its terminator.

				//The text can turn out shorter than the length given for it, but never longer.
				if (WindowsAPI.SendMessageTimeout(item.Handle, y_msg, index, buffer, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var actual) == 0
						|| actual < 0 || actual > length)
					return (string)Errors.ErrorOccurred($"Could not get selected item string for combo or list box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorString);

				return new string(buffer, 0, actual.ToInt32());
			}

			return DefaultObject;
		}

		internal override long ControlGetExStyle(object ctrl, object title, object text, object excludeTitle, object excludeText) => WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item ? item.ExStyle : 0L;

		internal override long ControlGetFocus(object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true) is WindowInfoBase item)
			{
				var h = WindowsAPI.GetWindowThreadProcessId(item.Handle, out var pid);
				var info = GUITHREADINFO.Default;//Must be initialized this way because the size field must be populated.

				// Failure is most likely because the target thread has no input queue; i.e. target_window
				// is a console window and the process which owns it has no input queue.  Controls cannot
				// exist without an input queue, so a return value of 0 is appropriate in that case.
				// A value of 0 is already ambiguous (window is not focused, or window itself is focused),
				// and is most likely preferable to a thrown exception, so returning 0 in the unlikely event
				// of some other failure seems acceptable.  There might be a possibility of a race condition
				// between determining target_window and the window being destroyed, but checking for that
				// doesn't seem useful since the window could be destroyed or deactivated after we return.
				if (!WindowsAPI.GetGUIThreadInfo(h, ref info))
					return 0L;

				//Use IsChild() to ensure the focused control actually belongs to this window.
				//Otherwise, a HWND will be returned if any window in the same thread has focus,
				//including the target window itself (typically when it has no controls).
				if (!WindowsAPI.IsChild(item.Handle, info.hwndFocus))
					return 0L;//As documented, if "none of the target window's controls has focus, the return value is 0".

				return info.hwndFocus.ToInt64();
			}

			return 0L;
		}

		internal override long ControlGetIndex(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.CB_GETCURSEL;
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.LB_GETCURSEL;
				else if (item.ClassName.Contains("Tab", StringComparison.OrdinalIgnoreCase))
					msg = WindowsAPI.TCM_GETCURSEL;
				else
					return (long)Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo, List or Tab", DefaultErrorLong);


				if (WindowsAPI.SendMessageTimeout(item.Handle, msg, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var index) == 0)
					return (long)Errors.TargetErrorOccurred($"Could not get selected item index for combo box, list box or tab control", title, text, excludeTitle, excludeText, DefaultErrorLong);

				return index.ToInt64() + 1L;
			}

			return 0L;
		}

		internal override object ControlGetItems(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				uint msg = 0, x_msg = 0, y_msg = 0;

				if (item.ClassName.Contains("Combo", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.CB_GETCOUNT;
					x_msg = WindowsAPI.CB_GETLBTEXTLEN;
					y_msg = WindowsAPI.CB_GETLBTEXT;
				}
				else if (item.ClassName.Contains("List", StringComparison.OrdinalIgnoreCase))
				{
					msg = WindowsAPI.LB_GETCOUNT;
					x_msg = WindowsAPI.LB_GETTEXTLEN;
					y_msg = WindowsAPI.LB_GETTEXT;
				}
				else
					return Errors.TargetErrorOccurred($"Class name {item.ClassName} did not contain Combo or List");

				if (WindowsAPI.SendMessageTimeout(item.Handle, msg, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 5000, out var count) == 0
						|| count.ToInt64() == WindowsAPI.LB_ERR)
					return Errors.ErrorOccurred($"Could not get the item count of the combo or list box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

				var cnt = count.ToInt32();
				nint length = 0;

				//LB_GETTEXT and CB_GETLBTEXT take no buffer size, so one buffer is sized for the longest item.
				for (var i = 0; i < cnt; i++)
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, x_msg, i, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 5000, out var itemLength) == 0
							|| itemLength.ToInt64() == WindowsAPI.LB_ERR)
						return Errors.ErrorOccurred($"Could not get an item length of the combo or list box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

					if (itemLength > length)
						length = itemLength;
				}

				var buffer = new char[length.ToInt32() + 1];
				var listBoxContent = new List<object>(cnt);

				for (var i = 0; i < cnt; i++)
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, y_msg, i, buffer, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 5000, out var itemLength) == 0
							|| itemLength < 0 || itemLength > length)
						return Errors.ErrorOccurred($"Could not get an item of the combo or list box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

					listBoxContent.Add(new string(buffer, 0, itemLength.ToInt32()));
				}

				return new Keysharp.Builtins.Array(listBoxContent);
			}

			return new Keysharp.Builtins.Array();
		}

		internal override void ControlGetPos(ref object outX,
											 ref object outY,
											 ref object outWidth,
											 ref object outHeight,
											 object ctrl = null,
											 object title = null,
											 object text = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			if (WindowSearch.TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, out var target, true) && item != null)
			{
				var coordParent = GetControlCoordinateParent(item, target);

				if (WindowsAPI.GetWindowRect(item.Handle, out var rect))
				{
					// MapWindowPoints returns 0 also for a parent whose client area is at the screen's origin.
					_ = WindowsAPI.MapWindowPoints(0, coordParent, ref rect, 2);
					outX = (long)rect.Left;
					outY = (long)rect.Top;
					outWidth = (long)(rect.Right - rect.Left);
					outHeight = (long)(rect.Bottom - rect.Top);
					return;
				}
			}

			outX = 0L;
			outY = 0L;
			outWidth = 0L;
			outHeight = 0L;
		}

		private static nint GetControlCoordinateParent(WindowInfoBase item, WindowInfoBase target)
		{
			var parent = WindowSearch.GetControlReferenceWindow(item, target);
			// A script-owned form puts its menu outside the content panel used for GUI client coordinates.
			return Control.FromHandle(parent.Handle) is KeysharpForm form ? form.ContentContainer.Handle : parent.Handle;
		}

		internal override void ControlMove(int x, int y, int width, int height, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (!WindowSearch.TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, out var target, true) || item == null)
				return;

			if (!WindowsAPI.GetWindowRect(item.Handle, out var rect))
				return;

			var coordParent = GetControlCoordinateParent(item, target);
			_ = WindowsAPI.MapWindowPoints(0, coordParent, ref rect, 2);
			var point = new RECT { Left = x == int.MinValue ? rect.Left : x, Top = y == int.MinValue ? rect.Top : y };
			// GetParent returns an owner for top-level windows, whose MoveWindow coordinates are screen-relative.
			var immediateParent = (item.Style & WindowsAPI.WS_CHILD) != 0 ? WindowsAPI.GetParent(item.Handle) : 0;

			if (immediateParent != coordParent)
				_ = WindowsAPI.MapWindowPoints(coordParent, immediateParent, ref point, 1);

			_ = WindowsAPI.MoveWindow(item.Handle, point.Left, point.Top,
				width == int.MinValue ? rect.Right - rect.Left : width,
				height == int.MinValue ? rect.Bottom - rect.Top : height, true);
			WindowInfoBase.DoControlDelay();
		}

		internal override long ControlGetStyle(object ctrl, object title, object text, object excludeTitle, object excludeText) => WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item ? item.Style : 0;

		internal override string ControlGetText(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			return WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item
				   ? Control.FromHandle(item.Handle) is Control ctrl2 ? ctrl2.Text : WindowsAPI.GetWindowTextTimeout(item.Handle, 5000)
				   : "";
		}

		internal override void ControlHideDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		DropdownHelper(false, ctrl, title, text, excludeTitle, excludeText);

		internal override void ControlSend(string str, object ctrl, object title, object text, object excludeTitle, object excludeText) => ControlSendHelper(str, ctrl, title, text, excludeTitle, excludeText, SendRawModes.NotRaw);

		internal void ControlSendHelper(string str, object ctrl, object title, object text, object excludeTitle, object excludeText, SendRawModes mode)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				Script.TheScript.HookThread.kbdMsSender.SendKeys(str, mode, SendModes.Event, item.Handle);
			}
		}

		internal override void ControlSendText(string str, object ctrl, object title, object text, object excludeTitle, object excludeText) => ControlSendHelper(str, ctrl, title, text, excludeTitle, excludeText, SendRawModes.RawText);

		internal override void ControlSetChecked(object val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				var onoff = Conversions.ConvertOnOffToggle(val);
				var ctrl2 = Control.FromHandle(item.Handle);

				if (ctrl2 is CheckBox cb)
					cb.CheckState = (onoff == ToggleValueType.Toggle ? cb.CheckState != CheckState.Checked : onoff == ToggleValueType.On) ? CheckState.Checked : CheckState.Unchecked;
				else if (ctrl2 is RadioButton rb)
					rb.Checked = onoff == ToggleValueType.Toggle ? !rb.Checked : onoff == ToggleValueType.On;
				else
				{
					//A toggle clicks without asking, as in AHK.
					if (onoff is ToggleValueType.On or ToggleValueType.Off)
					{
						if (!WindowsAPI.TryGetChecked(item.Handle, out var isChecked))
						{
							_ = Errors.OSErrorOccurred("", $"Could not get the check state of the control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");
							return;
						}

						if (isChecked == (onoff == ToggleValueType.On))
							return;
					}

					var thinfo = WindowsAPI.AttachThreadInput(item.Handle, false);//Pass false because the SetActiveWindow() call below is more specific.
					_ = WindowsAPI.SetActiveWindow(WindowsAPI.GetNonChildParent(item.Handle));

					if (!WindowsAPI.GetWindowRect(item.Handle, out var rect))
						rect.Bottom = rect.Left = rect.Right = rect.Top = 0;

					var lparam = KeyboardUtils.MakeLong((short)((rect.Right - rect.Left) / 2), (short)((rect.Bottom - rect.Top) / 2));
					_ = WindowsAPI.PostMessage(item.Handle, WindowsAPI.WM_LBUTTONDOWN, WindowsAPI.MK_LBUTTON, lparam);
					_ = WindowsAPI.PostMessage(item.Handle, WindowsAPI.WM_LBUTTONUP, 0, lparam);
					WindowInfoBase.DoControlDelay();
					WindowsAPI.DetachThreadInput(thinfo.Item1, thinfo.Item2);
				}
			}
		}

		internal override void ControlSetEnabled(object val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				var onoff = Conversions.ConvertOnOffToggle(val);

				if (Control.FromHandle(item.Handle) is Control ctrl2)
					ctrl2.Enabled = onoff == ToggleValueType.Toggle ? !ctrl2.Enabled : onoff == ToggleValueType.On;
				else if (!WindowsAPI.EnableWindow(item.Handle, onoff == ToggleValueType.Toggle ? !WindowsAPI.IsWindowEnabled(item.Handle) : onoff == ToggleValueType.On))
					_ =  Errors.ErrorOccurred($"Could not enable control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

				WindowInfoBase.DoControlDelay();
			}
		}

		internal override void ControlShowDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		DropdownHelper(true, ctrl, title, text, excludeTitle, excludeText);

		internal override long EditGetCurrentCol(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				_ = WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_GETSEL, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result);
				var val = result & 0xFFFF;

				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_LINEFROMCHAR, val, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000u, out var result2) == 0)
					return (long)Errors.ErrorOccurred($"Could not get line form character position for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				if (result2 == 0)//Since we're on line zero, the column number is simply start+1.
					return val + 1;

				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_LINEINDEX, result2, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var line_start) == 0)
					return (long)Errors.ErrorOccurred($"Could not get line line index from character position for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				return val - line_start.ToInt64() + 1L;
			}

			return 0L;
		}

		internal override long EditGetCurrentLine(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_LINEFROMCHAR, -1, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
					return (long)Errors.ErrorOccurred($"Could not get current line index for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				return result.ToInt64() + 1L;
			}

			return 0L;
		}

		internal override string EditGetLine(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (n < 1)
					return (string)Errors.InvalidParameterErrorOccurred(1, "EditGetLine", n, DefaultErrorString);

				//EM_GETLINE reads the buffer's size from its first character, which caps it at 32767.
				const int bufferSize = 32767;
				var buffer = ArrayPool<char>.Shared.Rent(bufferSize);

				try
				{
					n--;
					buffer[0] = (char)bufferSize;

					if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_GETLINE, n, buffer, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
						return (string)Errors.ErrorOccurred($"Could not get line for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorString);

					if (result == 0)//The line is empty or does not exist.
					{
						if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_GETLINECOUNT, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var linecount) == 0)
							return (string)Errors.ErrorOccurred($"Could not get line count for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorString);

						if (n + 1 > linecount.ToInt32())
							return (string)Errors.ValueErrorOccurred($"Requested line of {n + 1} is greater than the number of lines ({linecount.ToInt32()}) in the text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", null, DefaultErrorString);
					}

					return new string(buffer, 0, result.ToInt32());
				}
				finally
				{
					ArrayPool<char>.Shared.Return(buffer);
				}
			}

			return DefaultObject;
		}

		internal override long EditGetLineCount(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{

				//Don't try to cast to TextBox control here because it handles lines differently: wordwrapping doesn't count as a new line, whereas it does with EM_GETLINECOUNT;
				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_GETLINECOUNT, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
					return (long)Errors.ErrorOccurred($"Could not get line count for text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				return result.ToInt64();
			}

			return 0L;
		}

		internal override string EditGetSelectedText(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is TextBoxBase ctrl2)
					return ctrl2.SelectedText;
				var start = 0u;
				var end = 0u;
				_ = WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_GETSEL, ref start, ref end, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result);

				if (start > end)
					return (string)Errors.ErrorOccurred($"Start position of {start} was > end position of {end} when querying selected text of an edit control.", DefaultErrorString);

				if (start == end)
					return DefaultObject;

				var s = WindowsAPI.GetWindowTextTimeout(item.Handle, 2000);
				return s[(int)start..(int)end];
			}

			return DefaultObject;
		}

		internal override void EditPaste(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.EM_REPLACESEL, 1, str, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
					_ = Errors.ErrorOccurred($"Could not paste into text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

				WindowInfoBase.DoControlDelay();
			}
		}

		internal override object ListViewGetContent(string options, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			object ret = null;

			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				var focused = false;
				var count = false;
				var sel = false;
				var countcol = false;
				var col = int.MinValue;

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						if (opt.Equals("focused", StringComparison.OrdinalIgnoreCase)) { focused = true; }
						else if (opt.Equals("count", StringComparison.OrdinalIgnoreCase)) { count = true; }
						else if (opt.Equals("selected", StringComparison.OrdinalIgnoreCase)) { sel = true; }
						else if (opt.Equals("col", StringComparison.OrdinalIgnoreCase)) { countcol = true; }
						else if (Options.TryParse(opt, "col", ref col)) { col--; }
					}
				}

				if (Control.FromHandle(item.Handle) is ListView lv)
				{
					if (count && sel)
						ret = (long)lv.SelectedItems.Count;
					else if (count && focused)
						ret = lv.FocusedItem is ListViewItem lvi ? lvi.Index + 1L : (object)0L;
					else if (count && countcol)
						ret = (long)lv.Columns.Count;
					else if (count)
						ret = (long)lv.Items.Count;
					else
					{
						var sb = new StringBuilder(1024);
						var items = new List<ListViewItem>();

						if (focused)
						{
							if (lv.FocusedItem is ListViewItem lvi)
								items.Add(lvi);
						}
						else if (sel)
							items.AddRange(lv.SelectedItems.Cast<ListViewItem>());
						else
							items.AddRange(lv.Items.Cast<ListViewItem>());

						if (col >= 0)
						{
							if (col >= lv.Columns.Count)
								return Errors.ValueErrorOccurred($"Column {col + 1} is greater than list view column count of {lv.Columns.Count} in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

							items.ForEach(templvi => sb.AppendLine(templvi.SubItems[col].Text));
						}
						else
							items.ForEach(templvi => sb.AppendLine(string.Join('\t', templvi.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text))));

						ret = sb.ToString();
					}
				}
				else
				{
					if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETITEMCOUNT, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var row_count) == 0)
						return Errors.TargetErrorOccurred($"Could not get row count for list view", title, text, excludeTitle, excludeText);

					var col_count = new nint(-1);  // Fix for v1.0.37.01: Use -1 to indicate "undetermined col count".

					if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETHEADER, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var header_control) != 0
							&& header_control.ToInt64() != 0) // Relies on short-circuit boolean order.
						_ = WindowsAPI.SendMessageTimeout(header_control, WindowsAPI.HDM_GETITEMCOUNT, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out col_count);

					var rowct = row_count.ToInt64();
					var colct = col_count.ToInt64();

					if (count)
					{
						if (focused) // Listed first so that it takes precedence over include_selected_only.
						{
							if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETNEXTITEM, -1, WindowsAPI.LVNI_FOCUSED, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
								return Errors.TargetErrorOccurred($"Could not get next item for list view", title, text, excludeTitle, excludeText);

							ret = result.ToInt64() + 1L;
						}
						else if (sel)
						{
							if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETSELECTEDCOUNT, 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
								return Errors.TargetErrorOccurred($"Could not get selected item count for list view", title, text, excludeTitle, excludeText);

							ret = result.ToInt64();
						}
						else if (countcol) // "Count Col" returns the number of columns.
							ret = colct;
						else // Total row count.
							ret = rowct;

						return ret;
					}

					if (rowct < 1 || colct == 0) // But don't return when col_count == -1 (i.e. always make the attempt when col count is undetermined).
						return DefaultObject;  // No text in the control, so indicate success.

					// One block in the remote process holds the LVITEM and, after it, the buffer its text is written to.
					var lvItemSize = Marshal.SizeOf<LVITEM>();
					var remotelvi = WindowsAPI.AllocInterProcMem((uint)(lvItemSize + WindowsAPI.LV_REMOTE_BUF_SIZE * sizeof(char)), item.Handle, ProcessAccessTypes.PROCESS_QUERY_INFORMATION, out var prochandle);

					if (remotelvi == 0)
						return Errors.TargetErrorOccurred($"Could not allocate inter process memory for list view", title, text, excludeTitle, excludeText);

					var lvItem = new LVITEM
					{
						mask = 0x0001,//LVIF_TEXT
						cchTextMax = WindowsAPI.LV_REMOTE_BUF_SIZE - 1,
						pszText = remotelvi + lvItemSize
					};
					long i, total_length;
					nint next = 0;
					var is_selective = focused || sel;
					var single_col_mode = col > -1 || colct == -1;// Get only one column in these cases.
					var localText = new byte[WindowsAPI.LV_REMOTE_BUF_SIZE * sizeof(char)];
					var sb = new StringBuilder(1024);

					try
					{
						for (i = 0, next = new nint(-1), total_length = 0; i < rowct; ++i) // For each row:
						{
							if (is_selective)
							{
								// Fix for v1.0.37.01: Prevent an infinite loop that might occur if the target control no longer
								// exists (perhaps having been closed in the middle of the operation) or is permanently hung.
								// If GetLastError() were to return zero after the below, it would mean the function timed out.
								// However, rather than checking and retrying, it seems better to abort the operation because:
								// 1) Timeout should be quite rare.
								// 2) Reduces code size.
								// 3) Having a retry really should be accompanied by SLEEP_WITHOUT_INTERRUPTION because all this
								//    time our thread would not pumping messages (and worse, if the keyboard/mouse hooks are installed,
								//    mouse/key lag would occur).
								if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETNEXTITEM, next.ToInt32(), focused ? WindowsAPI.LVNI_FOCUSED : WindowsAPI.LVNI_SELECTED,
																  SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out next) == 0
										|| next.ToInt32() == -1) // No next item.  Relies on short-circuit boolean order.
									break; // End of estimation phase (if estimate is too small, the text retrieval below will truncate it).
							}
							else
								next = new nint(i);

							for (lvItem.iSubItem = (col > -1) ? col : 0 // iSubItem is which field to fetch. If it's zero, the item vs. subitem will be fetched.
												   ; colct == -1 || lvItem.iSubItem < colct // If column count is undetermined (-1), always make the attempt.
									; ++lvItem.iSubItem) // For each column:
							{
								if (WindowsAPI.WriteProcessMemory(prochandle, remotelvi, ref lvItem, lvItemSize, out _)
										&& WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETITEMTEXT, next, remotelvi, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var itemlen) != 0)
									total_length += itemlen.ToInt64();

								//else timed out or failed, don't include the length in the estimate.  Instead, the
								// text-fetching routine below will ensure the text doesn't overflow the var capacity.
								if (single_col_mode)
									break;
							}
						}

						// Add to total_length enough room for one linefeed per row, and one tab after each column
						// except the last (formula verified correct, though it's inflated by 1 for safety). "i" contains the
						// actual number of rows that will be transcribed, which might be less than rowct if is_selective==true.
						total_length += i * (single_col_mode ? 1 : colct);
						var capacity = total_length; // LRESULT avoids signed vs. unsigned compiler warnings.

						if (capacity > 0) // For maintainability, avoid going negative.
							--capacity; // Adjust to exclude the zero terminator, which simplifies things below.

						// RETRIEVE THE TEXT FROM THE REMOTE LISTVIEW
						// Start total_length at zero in case actual size is greater than estimate, in which case only a partial set of text along with its '\t' and '\n' chars will be written.
						for (i = 0, next = new nint(-1), total_length = 0; i < rowct; ++i) // For each row:
						{
							if (is_selective)
							{
								// Fix for v1.0.37.01: Prevent an infinite loop (for details, see comments in the estimation phase above).
								if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETNEXTITEM, next.ToInt32(), focused ? WindowsAPI.LVNI_FOCUSED : WindowsAPI.LVNI_SELECTED
																  , SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out next) == 0
										|| next.ToInt32() == -1) // No next item.
									break; // See comment above for why unconditional break vs. continue.
							}
							else // Retrieve every row, so the "next" row becomes the "i" index.
								next = new nint(i);

							// Insert a linefeed before each row except the first:
							if (i != 0 && total_length < capacity) // If we're at capacity, it will exit the loops when the next field is read.
							{
								_ = sb.AppendLine();
								++total_length;
							}

							// iSubItem is which field to fetch. If it's zero, the item vs. subitem will be fetched:
							for (lvItem.iSubItem = (col > -1) ? col : 0
												   ; colct == -1 || lvItem.iSubItem < colct // If column count is undetermined (-1), always make the attempt.
									; ++lvItem.iSubItem) // For each column:
							{
								// Insert a tab before each column except the first and except when in single-column mode:
								if (!single_col_mode && lvItem.iSubItem != 0 && total_length < capacity)  // If we're at capacity, it will exit the loops when the next field is read.
								{
									_ = sb.Append('\t');
									++total_length;
								}

								if (!WindowsAPI.WriteProcessMemory(prochandle, remotelvi, ref lvItem, lvItemSize, out _)
										|| WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.LVM_GETITEMTEXT, next, remotelvi, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var templen) == 0)
									continue; // Timed out or failed. It seems more useful to continue getting text rather than aborting the operation.

								// Never more than the buffer holds, whatever the control reports.
								var length = (uint)Math.Clamp(templen.ToInt64(), 0, WindowsAPI.LV_REMOTE_BUF_SIZE - 1);

								// Otherwise, the message was successfully sent.
								if (length > 0)
								{
									if (total_length + length > capacity)
										goto break_both; // "goto" for simplicity and code size reduction.

									// Otherwise:
									// READ THE TEXT FROM THE REMOTE PROCESS
									// Although MSDN has the following comment about LVM_GETITEM, it is not present for
									// LVM_GETITEMTEXT. Therefore, to improve performance (by avoiding a second call to
									// ReadProcessMemory) and to reduce code size, we'll take them at their word until
									// proven otherwise.  Here is the MSDN comment about LVM_GETITEM: "Applications
									// should not assume that the text will necessarily be placed in the specified
									// buffer. The control may instead change the pszText member of the structure
									// to point to the new text, rather than place it in the buffer."
									if (WindowsAPI.ReadProcessMemory(prochandle, lvItem.pszText, localText, length * sizeof(char), out var bytesread))
									{
										_ = sb.Append(MemoryMarshal.Cast<byte, char>(new ReadOnlySpan<byte>(localText, 0, (int)bytesread)));
										total_length += length; // Recalculate length in case its different than the estimate (for any reason).
									}

									//else it failed; but even so, continue on to put in a tab (if called for).
								}

								//else length is zero; but even so, continue on to put in a tab (if called for).
								if (single_col_mode)
									break;
							}
						}

						break_both:
						ret = sb.ToString();
					}
					finally
					{
						_ = WindowsAPI.VirtualFreeEx(prochandle, remotelvi, 0, VirtualAllocExTypes.MEM_RELEASE);
						_ = WindowsAPI.CloseHandle(prochandle);
					}
				}

				return ret;
			}

			return DefaultObject;
		}

		internal override void MenuSelect(object title, object text, object menu, object sub1, object sub2, object sub3, object sub4, object sub5, object sub6, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (!menu.CoerceString(out var menuStr))
					return;

				var sysMenu = menuStr == "0&";

				if (!sysMenu && Control.FromHandle(win.Handle) is Form form)//Winforms needs special treatment because it doesn't use an actual native main menu.
				{
					if (form.MainMenuStrip is MenuStrip strip)
					{
						if (!TryGetMenuItem(strip, out var item, menu, sub1, sub2, sub3, sub4, sub5, sub6))
							return;

						if (item != null)
							item.PerformClick();
						else
							_ = Errors.ValueErrorOccurred($"Could not find menu.", $"{title}, {text}, {menu}, {sub1}, {sub2}, {sub3}, {sub4}, {sub5}, {sub6}, {excludeTitle}, {excludeText}");
					}
				}
				else if (win != null && win.Handle != 0)
				{
					if (!TryGetNativeMenuItemId(win.Handle, out var menuId, menu, sub1, sub2, sub3, sub4, sub5, sub6))
						return;

					if (menuId != 0xFFFFFFFF)
					{
						_ = WindowsAPI.PostMessage(win.Handle, (uint)(sysMenu ? WindowsAPI.WM_SYSCOMMAND : WindowsAPI.WM_COMMAND), menuId, 0U);
						// No delay: AHK's MenuSelect ends with PostMessage and does not call DoWinDelay/DoControlDelay.
					}
					else
						_ = Errors.ValueErrorOccurred($"Could not find menu.", $"{title}, {text}, {menu}, {sub1}, {sub2}, {sub3}, {sub4}, {sub5}, {sub6}, {excludeTitle}, {excludeText}");
				}
			}
		}

		private static bool TryGetNativeMenuItemId(nint handle, out uint menuid, params object[] items)
		{
			menuid = 0xFFFFFFFF;

			if (handle == 0)
				return true;

			var i1 = 0;
			nint menu;

			if (!items[0].CoerceString(out var first))
				return false;

			if (first == "0&")
			{
				menu = WindowsAPI.GetSystemMenu(handle, false);
				i1 = 1;
			}
			else
				menu = WindowsAPI.GetMenu(handle);

			if (menu == 0 || WindowsAPI.GetMenuItemCount(menu) == 0)
				return true;

			var buf = new StringBuilder(1024);

			for (; i1 < items.Length; i1++)
			{
				if (!items[i1].CoerceString(out var item))
					return false;

				if (item.Length == 0)
					continue;

				var pos = -1;

				//A path which goes on past an item without a submenu names nothing, as one with an unmatched element does.
				if (menu != 0)
				{
					if (item.EndsWith('&') && int.TryParse(item.Trim('&'), out var n) && n > 0)
					{
						if (n <= WindowsAPI.GetMenuItemCount(menu))
							pos = n - 1;
					}
					else
					{
						var itemct = WindowsAPI.GetMenuItemCount(menu);

						for (var i = 0; i < itemct; i++)
						{
							if (WindowsAPI.GetMenuString(menu, (uint)i, buf, buf.Capacity - 1, WindowsAPI.MF_BYPOSITION) > 0
									&& ControlManagerBase.MenuMatchHelper(buf.ToString(), item))
							{
								pos = i;
								break;
							}
						}
					}
				}

				if (pos < 0)
				{
					menuid = 0xFFFFFFFF;
					break;
				}

				menuid = WindowsAPI.GetMenuItemID(menu, pos);
				menu = WindowsAPI.GetSubMenu(menu, pos);
			}

			return true;
		}

		internal void NotifyParent(nint handle, uint x_msg, uint y_msg)
		{
			var immediate_parent = WindowsAPI.GetParent(handle);

			if (immediate_parent == 0)
			{
				_ = Errors.TargetErrorOccurred("Parent is null");
				return;
			}

			WindowsAPI.SetLastError(0);//Must be done to differentiate between success and failure when control has ID 0.
			var control_id = WindowsAPI.GetDlgCtrlID(handle);

			if (control_id == 0 && WindowsAPI.GetLastError() != 0)
			{
				_ = Errors.OSErrorOccurred("", $"Last error was not zero");
				return;
			}

			// Both conditions must be checked (see above).

			// Proceed even if control_id == 0, since some applications are known to
			// utilize the notification in that case (e.g. Notepad's Save As dialog).
			if (WindowsAPI.SendMessageTimeout(immediate_parent, WindowsAPI.WM_COMMAND, (nint)KeyboardUtils.MakeLong((short)control_id, (short)x_msg)
											  , handle, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result) == 0)
			{
				_ = Errors.TargetErrorOccurred($"Could not send WM_COMMAND message of {x_msg} to {immediate_parent.ToInt64()}");
				return;
			}

			if (WindowsAPI.SendMessageTimeout(immediate_parent, WindowsAPI.WM_COMMAND, (nint)KeyboardUtils.MakeLong((short)control_id, (short)y_msg)
											  , handle, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var result2) == 0)
			{
				_ = Errors.TargetErrorOccurred($"Could not send WM_COMMAND message of {y_msg} to {immediate_parent.ToInt64()}");
				return;
			}
		}

		internal override void PostMessage(uint msg, nint wparam, nint lparam, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			var item = ctrl != null
					   ? WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText)
					   : WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true);

			if (item is WindowInfo)
			{
				if (!WindowsAPI.PostMessage(item.Handle, (uint)msg, new nint(wparam), new nint(lparam)))
					_ = Errors.ErrorOccurred($"Could not post message with values msg: {msg}, lparam: {lparam}, wparam: {wparam} to control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");
			}
		}

		internal override long SendMessage(uint msg, object wparam, object lparam, object ctrl, object title, object text, object excludeTitle, object excludeText, int timeout)
		{
			long ret;
			Reflections.TryGetPtrProperty(wparam, out var waddr);
			var wptr = new nint(waddr);
			var item = ctrl != null
					   ? WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText)
					   : WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true);

			if (item == null)
				return 0L;

			var thehandle = item.Handle;

			if (lparam is string s)
			{
				var sendresult = 0L;
				nint result = 0;

				if (msg == WindowsAPI.WM_COPYDATA)
				{
					var sarr = Encoding.Unicode.GetBytes(s);
					var len = sarr.Length;
					WindowsAPI.COPYDATASTRUCT cds;
					cds.dwData = 1;
					cds.lpData = s;
					cds.cbData = len + 1;
					sendresult = WindowsAPI.SendMessageTimeout(thehandle, msg, wptr, ref cds, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, (uint)timeout, out result);
				}
				else
					sendresult = WindowsAPI.SendMessageTimeout(thehandle, msg, wptr, s, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, (uint)timeout, out result);

				if (sendresult == 0)
					return (long)Errors.OSErrorOccurred("", $"Could not send message with values msg: {msg}, lparam: {lparam}, wparam: {wparam} to control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				ret = result.ToInt64();
			}
			else
			{
				Reflections.TryGetPtrProperty(lparam, out var laddr);
				var lptr = new nint(laddr);

				if (WindowsAPI.SendMessageTimeout(thehandle, (uint)msg, wptr, lptr, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, (uint)timeout, out var result) == 0)
					return (long)Errors.OSErrorOccurred("", $"Could not send message with values msg: {msg}, lparam: {lparam}, wparam: {wparam} to control in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", DefaultErrorLong);

				ret = result.ToInt64();
			}

			return ret;
		}

		private static void DropdownHelper(bool val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (WindowsAPI.SendMessageTimeout(item.Handle, WindowsAPI.CB_SHOWDROPDOWN, val ? 1 : 0, 0, SendMessageTimeoutFlags.SMTO_ABORTIFHUNG, 2000, out var _) == 0)
					_ = Errors.ErrorOccurred($"Could not hide combo box drop down for in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}");

				WindowInfoBase.DoControlDelay();
			}
		}
	}
}

#endif
