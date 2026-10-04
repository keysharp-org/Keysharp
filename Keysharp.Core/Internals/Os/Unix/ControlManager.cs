using Keysharp.Builtins;
#if !WINDOWS
using Keysharp.Internals.Os.Windows;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using static Keysharp.Builtins.KeysharpListView;

namespace Keysharp.Internals.Os.Unix
{
	/// <summary>
	/// Concrete implementation of ControlManager for the linux platfrom.
	/// </summary>
	internal class ControlManager : ControlManagerBase
	{
		private static int FindDataStoreIndex(IEnumerable dataStore, string value)
		{
			if (dataStore == null || string.IsNullOrEmpty(value))
				return -1;

			var index = 0;
			foreach (var item in dataStore)
			{
				if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
					return index;
				index++;
			}

			return -1;
		}

		internal override long ControlAddItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var res = 0L;
				var ctrl2 = item.Control;
				if (ctrl2 is KeysharpComboBox cb)
				{
					res = cb.Items.Count;
					cb.Items.Add(str);
				}
				else if (ctrl2 is KeysharpListBox lb)
				{
					res = lb.Items.Count;
					lb.Items.Add(str);
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}

				return res + 1L;
			}

			return 0L;
		}

		internal override void ControlChooseIndex(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;
				n--;

				if (ctrl2 is ComboBox cb)
				{
					if (n >= 0)
						cb.SelectedIndex = n;
					else
						cb.SelectedIndex = -1;
				}
				else if (ctrl2 is ListBox lb)
				{
					if (n >= 0)
					{
						lb.SelectedIndex = n;

						if (lb.GetGuiControl() is Gui.Control gc)
							gc._control_DoubleClick(lb, new EventArgs());
					}
					else
						lb.SelectedIndex = -1;
				}
				else if (ctrl2 is TabControl tc)
				{
					tc.SelectedIndex = n;
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}
		}

		internal override long ControlChooseString(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			var index = 0L;

			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;

				if (ctrl2 is ComboBox cb)
				{
					index = cb.FindString(str);
					cb.SelectedIndex = (int)index;
				}
				else if (ctrl2 is ListBox lb)
				{
					index = lb.FindString(str);
					lb.SelectedIndex = (int)index;

					if (index >= 0)
					{
						if (lb.GetGuiControl() is Gui.Control gc)
							gc._control_DoubleClick(lb, new EventArgs());
					}
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return index;
		}

		internal override void ControlClick(object ctrlorpos, object title, object text, string whichButton, int clickCount, string options, object excludeTitle, object excludeText)
		{
			var winx = int.MinValue;
			var winy = int.MinValue;
			var ctrlx = int.MinValue;
			var ctrly = int.MinValue;
			var vk = HookThread.ConvertMouseButton(whichButton);
			var posoverride = options?.Contains("pos", StringComparison.OrdinalIgnoreCase) ?? false;
			bool d = false, u = false;
			var posAppliedToChild = false;

			if (!string.IsNullOrEmpty(options))
			{
				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

					if (opt.Length > 0)
					{
						if (opt.Equals("d", StringComparison.OrdinalIgnoreCase))
							d = true;
						else if (opt.Equals("u", StringComparison.OrdinalIgnoreCase))
							u = true;
						else if (Options.TryParse(opt, "x", ref ctrlx)) { }
						else if (Options.TryParse(opt, "y", ref ctrly)) { }
					}
				}
			}

			if (d) u = false;
			if (u) d = false;

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

			WindowInfoBase item = null;
			WindowInfoBase target = null;
			var getctrlbycoords = false;

			if (ctrlorpos.IsNullOrEmpty())
			{
				item = target = WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true);
			}
			else if (!posoverride)
			{
				if (!WindowSearch.TrySearchControl(ctrlorpos, title, text, excludeTitle, excludeText, out item, out target))
					return;

				if (item == null)
				{
					if (winx != int.MinValue && winy != int.MinValue)
						getctrlbycoords = true;
					else
						_ = Errors.TargetErrorOccurred($"Could not get control {ctrlorpos}", title, text, excludeTitle, excludeText);
				}
			}
			else
			{
				if (winx != int.MinValue && winy != int.MinValue)
					getctrlbycoords = true;
			}

			if (getctrlbycoords)
			{
				item = target ?? WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true);
				if (item != null)
				{
					var pt = new POINT(winx, winy);
					item.ClientToScreen(ref pt);
					var pah = new PointAndHwnd(pt);
					Platform.Window.ChildFindPoint(item.Handle, pah);
					if (pah.hwndFound != 0)
					{
						item = WindowQuery.CreateWindow(pah.hwndFound);
						if (ctrlx == int.MinValue || ctrly == int.MinValue)
						{
							ctrlx = pt.X - pah.rectFound.Left;
							ctrly = pt.Y - pah.rectFound.Top;
						}
						posAppliedToChild = true;
					}
				}
			}

			if (item == null || clickCount < 1)
				return;

			var size = item.Size;
			var clickX = ctrlx != int.MinValue ? ctrlx : size.Width / 2;
			var clickY = ctrly != int.MinValue ? ctrly : size.Height / 2;

			if (!posAppliedToChild && winx != int.MinValue && winy != int.MinValue)
			{
				clickX = winx;
				clickY = winy;
			}

			var clickPoint = new Point(clickX, clickY);
			var vkIsWheel = MouseUtils.IsWheelVK(vk);

			if (item is ControlInfo controlInfo && vk == VK_LBUTTON && !vkIsWheel && !d && !u && controlInfo.TryInvokeDefaultClick(clickPoint, clickCount))
			{
				WindowInfoBase.DoControlDelay();
				return;
			}

#if LINUX
			if (!Platform.Desktop.IsX11Available)
				return;

			uint button;
			if (vk == VK_LBUTTON) button = 1;
			else if (vk == VK_RBUTTON) button = 3;
			else if (vk == VK_MBUTTON) button = 2;
			else if (vk == VK_XBUTTON1) button = 8;
			else if (vk == VK_XBUTTON2) button = 9;
			else if (vk == VK_WHEEL_UP) button = 4;
			else if (vk == VK_WHEEL_DOWN) button = 5;
			else if (vk == VK_WHEEL_LEFT) button = 6;
			else if (vk == VK_WHEEL_RIGHT) button = 7;
			else return;

#elif OSX
			if (d || u || vkIsWheel)
			{
				_ = Errors.ValueErrorOccurred("macOS native ControlClick supports Left, Right, Middle, X1 and X2 clicks; D/U and wheel input are unsupported.");
				return;
			}

			uint button;
			if (vk == VK_LBUTTON) button = 1;
			else if (vk == VK_RBUTTON) button = 2;
			else if (vk == VK_MBUTTON) button = 3;
			else if (vk == VK_XBUTTON1) button = 4;
			else if (vk == VK_XBUTTON2) button = 5;
			else
			{
				_ = Errors.ValueErrorOccurred($"Invalid macOS ControlClick button '{whichButton}'. Expected Left, Right, Middle, X1 or X2.");
				return;
			}

			if (Platform.Window.TryClick(item.Handle, clickPoint, button, clickCount))
				WindowInfoBase.DoControlDelay();
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif

#if LINUX
			for (var i = 0; i < clickCount; i++)
			{
				if (vkIsWheel || !u)
				{
					if (!SendX11MouseEvent(item.Handle, button, clickPoint, true)) return;
					WindowInfoBase.DoControlDelay();
				}

				if (vkIsWheel || !d)
				{
					if (!SendX11MouseEvent(item.Handle, button, clickPoint, false)) return;
					WindowInfoBase.DoControlDelay();
				}
			}
#endif
		}

#if LINUX
		private static bool SendX11MouseEvent(nint handle, uint button, Point location, bool down)
			=> Keysharp.Internals.Window.Linux.Wayland.DesktopBackend.X11
				.TrySendWindowButton(handle, location, button, down);
#endif

		internal override void ControlDeleteItem(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;
				n--;

				if (ctrl2 is KeysharpComboBox cb)
				{
					cb.Items.RemoveAt(n);
					cb.SelectedIndex = -1;//On linux, if the selected item is deleted, it will throw an exception the next time the dropdown is clicked if SelectedIndex is not set to -1.
				}
				else if (ctrl2 is KeysharpListBox lb)
				{
					lb.Items.RemoveAt(n);
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}
		}

		internal override long ControlFindItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;

				if (ctrl2 is ComboBox cb)
					return FindDataStoreIndex(cb.DataStore, str) + 1L;
				else if (ctrl2 is ListBox lb)
					return FindDataStoreIndex(lb.DataStore, str) + 1L;
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return 0L;
		}

		internal override void ControlFocus(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				if (item.Control is Control ctrl2)
					ctrl2.Focus();
				else
#if LINUX
					_ = Keysharp.Internals.Window.Linux.Wayland.DesktopBackend.X11
						.TryFocusChildWindow(item.Handle);
#else
					item.Focus();
#endif
			}
		}

		internal override long ControlGetChecked(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;

				if (ctrl2 is CheckBox cb)
#if WINDOWS
					return cb.Checked ? 1L : 0L;
#else
					return cb.Checked == null ? -1L : cb.Checked.Value ? 1L : 0L;
#endif
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return 0L;
		}

		internal override string ControlGetChoice(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;
				if (ctrl2 is ListControl lc)
					return lc.SelectedValue?.ToString() ?? "";
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return DefaultObject;
		}

		internal override long ControlGetExStyle(object ctrl, object title, object text, object excludeTitle, object excludeText) => 1;

		internal override long ControlGetFocus(object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is Form form)
				{
					if (form.ActiveControl != null)
						return form.ActiveControl.Handle.ToInt64();
				}
			}

			return 0L;
		}

		internal override long ControlGetIndex(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			long index = -1;

			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;

				if (ctrl2 is ComboBox cb)
					index = cb.SelectedIndex;
				else if (ctrl2 is ListBox lb)
					index = lb.SelectedIndex;
				else if (ctrl2 is TabControl tc)
					index = tc.SelectedIndex;
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return index + 1L;
		}

		internal override object ControlGetItems(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;

				if (ctrl2 is KeysharpComboBox cb)
					return new Keysharp.Builtins.Array(cb.Items.Cast<object>().Select(item => (object)item.ToString()));
				else if (ctrl2 is KeysharpListBox lb)
					return new Keysharp.Builtins.Array(lb.Items.Cast<object>().Select(item => (object)item.ToString()));
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return new Keysharp.Builtins.Array();
		}

		internal override void ControlGetPos(ref object outX, ref object outY, ref object outWidth, ref object outHeight, object ctrl = null, object title = null, object text = null, object excludeTitle = null, object excludeText = null)
		{
			if (WindowSearch.TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, out var target, true)
				&& item != null && Control.FromHandle(item.Handle) is Control control)
			{
				var bounds = control.GetScreenBounds(true);
				var origin = WindowSearch.GetControlReferenceWindow(item, target).ClientToScreen();
				outX = (long)(bounds.X - origin.X);
				outY = (long)(bounds.Y - origin.Y);
				outWidth = control.Width;
				outHeight = control.Height;
				return;
			}

			outX = 0L;
			outY = 0L;
			outWidth = 0L;
			outHeight = 0L;
		}

		internal override void ControlMove(int x, int y, int width, int height, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (!WindowSearch.TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, out var target, true) || item == null)
				return;

			if (Control.FromHandle(item.Handle) is Control control)
			{
				var origin = WindowSearch.GetControlReferenceWindow(item, target).ClientToScreen();
				var parentOrigin = control.Parent?.ScreenOrigin(true) ?? PointF.Empty;
				var location = control is Forms.Window window ? window.Location : control.GetLocation();
				control.SetLocation(new Point(x == int.MinValue ? location.X : x + origin.X - Convert.ToInt32(parentOrigin.X),
					y == int.MinValue ? location.Y : y + origin.Y - Convert.ToInt32(parentOrigin.Y)));
				control.Size = new Size(width == int.MinValue ? control.Size.Width : width, height == int.MinValue ? control.Size.Height : height);
			}
			else
				_ = Platform.Window.TryMoveResize(item.Handle, new Rectangle(x, y, width, height),
					x != int.MinValue || y != int.MinValue, width != int.MinValue || height != int.MinValue);

			WindowInfoBase.DoControlDelay();
		}

		internal override long ControlGetStyle(object ctrl, object title, object text, object excludeTitle, object excludeText) => 1;

		internal override string ControlGetText(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			var val = "";

			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var ctrl2 = item.Control;
				val = ctrl2 != null ? ctrl2.Text : item.Title;
			}

			return val;
		}

		internal override void ControlHideDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		DropdownHelper(false, ctrl, title, text, excludeTitle, excludeText);

		internal override void ControlSend(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			ControlSendHelper(str, ctrl, title, text, excludeTitle, excludeText, SendRawModes.NotRaw);
		}

		internal override void ControlSendText(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			ControlSendHelper(str, ctrl, title, text, excludeTitle, excludeText, SendRawModes.RawText);
		}

		internal override void ControlSetChecked(object val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var onoff = Conversions.ConvertOnOffToggle(val);
				var ctrl2 = item.Control;

				if (ctrl2 is CheckBox cb)
					cb.Checked = onoff == ToggleValueType.Toggle ? !cb.Checked : onoff == ToggleValueType.On;
				else if (ctrl2 is RadioButton rb)
					rb.Checked = onoff == ToggleValueType.Toggle ? !rb.Checked : onoff == ToggleValueType.On;
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}
		}

		internal override void ControlSetEnabled(object val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var onoff = Conversions.ConvertOnOffToggle(val);

				if (item.Control is Control ctrl2)
					ctrl2.Enabled = onoff == ToggleValueType.Toggle ? !ctrl2.Enabled : onoff == ToggleValueType.On;
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}
		}

		private static void ControlSendHelper(string str, object ctrl, object title, object text, object excludeTitle, object excludeText, SendRawModes mode)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
				Script.TheScript.HookThread.kbdMsSender.SendKeys(str, mode, SendModes.Event, item.Handle);
		}

		internal override void ControlShowDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		DropdownHelper(true, ctrl, title, text, excludeTitle, excludeText);

		//Lines are counted at the line breaks in the text, so a long line the control wraps is still one line, unlike in
		//a Windows edit control.
		internal override long EditGetCurrentCol(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is not TextControl edit)
				return 0L;

			var before = BeforeSelection(edit);
			return before.Length - before.LastIndexOf('\n');
		}

		internal override long EditGetCurrentLine(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
			GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is TextControl edit ? BeforeSelection(edit).Count('\n') + 1L : 0L;

		internal override string EditGetLine(int n, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is not TextControl edit)
				return DefaultObject;

			if (n < 1)
				return (string)Errors.InvalidParameterErrorOccurred(1, "EditGetLine", n, DefaultErrorString);

			var rest = (edit.Text ?? "").AsSpan();

			for (var i = 1; i < n; i++)
			{
				var lineBreak = rest.IndexOf('\n');

				if (lineBreak < 0)
					return (string)Errors.ValueErrorOccurred($"Requested line of {n} is greater than the number of lines ({i}) in the text box in window with criteria: title: {title}, text: {text}, exclude title: {excludeTitle}, exclude text: {excludeText}", null, DefaultErrorString);

				rest = rest[(lineBreak + 1)..];
			}

			var end = rest.IndexOf('\n');
			return (end < 0 ? rest : rest[..end]).TrimEnd('\r').ToString();
		}

		internal override long EditGetLineCount(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
			GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is TextControl edit ? (edit.Text ?? "").AsSpan().Count('\n') + 1L : 0L;

		internal override string EditGetSelectedText(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is not TextControl edit)
				return DefaultObject;

			var selection = edit.Selection;
			return selection.Length() > 0 ? (edit.Text ?? "").Substring(selection.Start, selection.Length()) : "";
		}

		internal override void EditPaste(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (GetEtoTextControl(ctrl, title, text, excludeTitle, excludeText) is not TextControl edit)
				return;

			//As EM_REPLACESEL: the text replaces the selection and the caret follows it.
			var selection = edit.Selection;
			edit.ReplaceText(selection.Start, selection.Length(), str);
			edit.CaretIndex = selection.Start + str.Length;
			WindowInfoBase.DoControlDelay();
		}

		/// <summary>The Edit or RichEdit control the criteria name, or null after raising the error when they name none.</summary>
		private static TextControl GetEtoTextControl(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is not ControlInfo item)
				return null;

			if (item.Control is TextBox or TextArea or PasswordBox)
				return (TextControl)item.Control;

			_ = Errors.TargetErrorOccurred("The control is not an Edit", title, text, excludeTitle, excludeText);
			return null;
		}

		private static ReadOnlySpan<char> BeforeSelection(TextControl edit)
		{
			var all = edit.Text ?? "";
			return all.AsSpan(0, Math.Min(edit.Selection.Start, all.Length));
		}

		internal override object ListViewGetContent(string options, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			object ret = null;

			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				var focused = false;
				var count = false;
				var sel = false;
				var countcol = false;
				var col = int.MinValue;
				var opts = Options.ParseOptions(options);

				foreach (var opt in opts)
				{
					if (string.Compare(opt, "focused", true) == 0) { focused = true; }
					else if (string.Compare(opt, "count", true) == 0) { count = true; }
					else if (string.Compare(opt, "selected", true) == 0) { sel = true; }
					else if (string.Compare(opt, "col", true) == 0) { countcol = true; }
					else if (Options.TryParse(opt, "col", ref col)) { col--; }
				}

				if (item.Control is KeysharpListView lv)
				{
					//As in AutoHotkey, Focused takes precedence over Selected.
					if (count && focused)
						ret = lv.FocusedRow + 1L;
					else if (count && sel)
						ret = (long)lv.SelectedIndices.Count;
					else if (count && countcol)
						ret = (long)lv.Columns.Count;
					else if (count)
						ret = (long)lv.Items.Count;
					else
					{
						if (col != int.MinValue && (col < 0 || col >= lv.Columns.Count))
							return Errors.ValueErrorOccurred($"Column {col + 1} is outside the list view column count of {lv.Columns.Count}.");

						IEnumerable<int> rows = focused ? (lv.FocusedRow is var focusedRow and >= 0 ? [focusedRow] : []) : sel ? lv.SelectedIndices : Enumerable.Range(0, lv.Items.Count);
						var sb = new StringBuilder(1024);
						var firstRow = true;

						//A linefeed between rows, none after the last, and a tab between the cells of a row.
						foreach (var row in rows)
						{
							if (!firstRow)
								_ = sb.Append('\n');

							firstRow = false;
							var cells = lv.Items[row];

							if (col >= 0)
								_ = sb.Append(GetCellText(cells, col));
							else
								for (var c = 0; c < lv.Columns.Count; c++)
									_ = (c > 0 ? sb.Append('\t') : sb).Append(GetCellText(cells, c));
						}

						ret = sb.ToString();
					}
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}

			return ret;
		}

		internal override void MenuSelect(object title, object text, object menu, object sub1, object sub2, object sub3, object sub4, object sub5, object sub6, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchWindow(title, text, excludeTitle, excludeText, true) is WindowInfoBase win)
			{
				if (Control.FromHandle(win.Handle) is Form form)
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
			}
		}

		internal override void PostMessage(uint msg, nint wparam, nint lparam, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			_ = TryDispatchWindowMessage(msg, wparam, lparam, ctrl, title, text, excludeTitle, excludeText, true);
		}

		internal override long SendMessage(uint msg, object wparam, object lparam, object ctrl, object title, object text, object excludeTitle, object excludeText, int timeout)
		{
			if (!wparam.CoerceInt(out var wp) || !lparam.CoerceInt(out var lp))
				return 0L;

			return TryDispatchWindowMessage(msg, wp, lp, ctrl, title, text, excludeTitle, excludeText, false) ? 1L : 0L;
		}

		private static bool TryDispatchWindowMessage(uint msg, nint wparam, nint lparam, object ctrl, object title, object text, object excludeTitle, object excludeText, bool post)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText, false) is not ControlInfo item)
				return false;

			var action = CreateWindowMessageAction(item.Control, msg, wparam, lparam);

			if (action == null)
				return false;

			if (post)
#if LINUX
				//AsyncInvoke (GtkSharp's Application.Invoke) runs at a higher priority than GTK's
				//layout finalization, so it can fire before freshly-changed text is laid out (e.g. a
				//scroll-to-end would land one line short). A default-priority idle runs after layout.
				_ = GLib.Idle.Add(() => { action(); return false; });
#else
				Application.Instance.AsyncInvoke(action);
#endif
			else
				InvokeWindowMessageAction(action);

			return true;
		}

		private static Action CreateWindowMessageAction(Control control, uint msg, nint wparam, nint lparam) =>
			msg switch
			{
				(uint)WindowsAPI.WM_VSCROLL => CreateTextAreaScrollAction(control, wparam),
				(uint)WindowsAPI.EM_SCROLLCARET => CreateTextAreaScrollCaretAction(control),
				(uint)WindowsAPI.EM_SETSEL => CreateTextAreaSetSelAction(control, wparam, lparam),
				_ => null
			};

		//EM_SETSEL(start, end): select a character range, which also moves the caret to `end`. The two
		//documented special cases are honoured because scripts rely on them: start == -1 deselects and
		//parks the caret at the current end, and end == -1 selects to the end of the text.
		//Eto's Range<int> is INCLUSIVE (see ScrollTextAreaToOffset), so an exclusive [start, end)
		//becomes Range(start, end - 1), and an empty selection becomes Range(caret, caret - 1).
		private static Action CreateTextAreaSetSelAction(Control control, nint wparam, nint lparam)
		{
			if (control is not TextArea area)
				return null;

			var start = (int)wparam.ToInt64();
			var end = (int)lparam.ToInt64();

			return () =>
			{
				var len = area.Text?.Length ?? 0;

				if (start == -1)//Deselect; caret stays where the selection ended.
				{
					var caret = Math.Clamp(area.CaretIndex, 0, len);
					area.Selection = new Range<int>(caret, caret - 1);
					return;
				}

				var from = Math.Clamp(start, 0, len);
				var to = end == -1 ? len : Math.Clamp(end, 0, len);

				if (to < from)
					(from, to) = (to, from);

				area.Selection = new Range<int>(from, to - 1);
				area.CaretIndex = to;
			};
		}

		private static Action CreateTextAreaScrollAction(Control control, nint wparam)
		{
			if (control is not TextArea area)
				return null;

			var cmd = unchecked((int)(wparam.ToInt64() & 0xFFFF));//WM_VSCROLL packs the scroll command in the low word.

			return cmd switch
			{
				WindowsAPI.SB_TOP => area.ScrollToStart,
				WindowsAPI.SB_BOTTOM => () => ScrollTextAreaToEnd(area),
				_ => null
			};
		}

		private static Action CreateTextAreaScrollCaretAction(Control control) =>
			control is TextArea area ? () => ScrollTextAreaToOffset(area, area.CaretIndex) : null;

		private static void InvokeWindowMessageAction(Action action)
		{
			var app = Application.Instance;

			if (app.IsUIThread)
				action();
			else
				app.Invoke(action);
		}

		private static void ScrollTextAreaToOffset(TextArea area, int offset)
		{
			//Eto's TextArea.ScrollTo scrolls to offset (range.Start + range.Length()), and Range<int>.Length()
			//is inclusive (End - Start + 1). End = Start - 1 targets exactly Start, and maps to NSRange(Start, 0).
			area.ScrollTo(new Range<int>(offset, offset - 1));
		}

		private static void ScrollTextAreaToEnd(TextArea area)
		{
			area.ScrollToEnd();
		}

		private static void DropdownHelper(bool val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is ControlInfo item)
			{
				if (item.Control is ComboBox ctrl2)
				{
					ctrl2.DroppedDown = val;
				}
				else
				{
					//How to do the equivalent of what the Windows derivation does, but on linux?
				}
			}
		}
	}
}

#endif
