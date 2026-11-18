using Keysharp.Builtins;
namespace Keysharp.Internals.Os
{
	internal abstract class ControlManagerBase
	{
		internal static bool TryParseMenuPosition(string selector, out int index)
		{
			if (!string.IsNullOrEmpty(selector) && selector.EndsWith('&')
				&& int.TryParse(selector.AsSpan().Trim('&'), out var position) && position > 0)
			{
				index = position - 1;
				return true;
			}

			index = -1;
			return false;
		}

		/// <summary>
		/// The item a MenuSelect path names, or null in <paramref name="menuItem"/> when there is none. False when
		/// an error was raised and the script continued it, and the caller then returns at once without raising
		/// another.
		/// </summary>
		internal static bool TryGetMenuItem(MenuStrip strip, out ToolStripMenuItem menuItem, params object[] items)
		{
			menuItem = null;
			for (var i = 0; i < items.Length; i++)
			{
				if (!items[i].CoerceString(out var item))
				{
					menuItem = null;
					return false;
				}

				if (i > 0 && item.Length == 0)
					continue;

				var candidates = i == 0 ? strip.Items : menuItem.DropDownItems;
				menuItem = null;
				if (TryParseMenuPosition(item, out var n))
				{
					if (n >= candidates.Count)
					{
						_ = Errors.ValueErrorOccurred($"Index {n + 1} was outside the menu length of {candidates.Count}.", item);
						return false;
					}
					menuItem = candidates[n] as ToolStripMenuItem;
				}
				else
					foreach (ToolStripItem candidate in candidates)
					{
						if (MenuMatchHelper(candidate.Text, item))
						{
							menuItem = candidate as ToolStripMenuItem;
							break;
						}
					}
				if (menuItem == null)
					break;
			}

			return true;
		}

		internal static bool MenuMatchHelper(string menuText, string match)
		{
			var matchFound = menuText.StartsWith(match, StringComparison.CurrentCultureIgnoreCase);

			if (!matchFound && menuText.Contains('&'))
			{
				var tempsb = new StringBuilder(menuText.Length);

				for (var ii = 0; ii < menuText.Length; ii++)//This logic gotten from AHK to remove every other ampersand.
				{
					if (menuText[ii] == '&')
						ii++;

					if (ii < menuText.Length)
						_ = tempsb.Append(menuText[ii]);
					else
						break;
				}

				menuText = tempsb.ToString();
				matchFound = menuText.StartsWith(match, StringComparison.CurrentCultureIgnoreCase);
			}

			return matchFound;
		}

		internal abstract long ControlAddItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlChooseIndex(int n, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long ControlChooseString(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlClick(object ctrlorpos, object title, object text, string whichButton, int clickCount, string options, object excludeTitle, object excludeText);

		internal abstract void ControlDeleteItem(int n, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long ControlFindItem(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlFocus(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long ControlGetChecked(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract string ControlGetChoice(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal virtual string ControlGetClassNN(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.TrySearchControl(ctrl, title, text, excludeTitle, excludeText, out var item, out var target, true) && item != null)
			{
				var parent = WindowSearch.GetControlReferenceWindow(item, target);
				return Platform.Window.TryEnumerateChildren(parent.Handle, out var controls)
					? WindowQuery.ClassNNs(controls).FirstOrDefault(c => c.Control == item.Handle).ClassNN ?? item.ClassName
					: item.ClassName;
			}

			return DefaultObject;
		}

		internal virtual long ControlGetEnabled(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
				return item.Enabled ? 1L : 0L;

			return 0L;
		}

		internal abstract long ControlGetExStyle(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long ControlGetFocus(object title, object text, object excludeTitle, object excludeText);

		internal virtual long ControlGetHwnd(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
				return item.Handle.ToInt64();
			else
				return 0L;
		}

		internal abstract long ControlGetIndex(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract object ControlGetItems(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlGetPos(ref object outX, ref object outY, ref object outWidth, ref object outHeight, object ctrl = null, object title = null, object text = null, object excludeTitle = null, object excludeText = null);

		internal abstract long ControlGetStyle(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract string ControlGetText(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal virtual long ControlGetVisible(object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is Control ctrl2)
					return ctrl2.Visible ? 1L : 0L;
				else
					return item.Visible ? 1L : 0L;
			}

			return 0L;
		}

		internal virtual void ControlHide(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		ShowHideHelper(false, ctrl, title, text, excludeTitle, excludeText);

		internal abstract void ControlHideDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlMove(int x, int y, int width, int height, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlSend(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlSendText(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlSetChecked(object val, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void ControlSetEnabled(object val, object ctrl, object title, object text, object excludeTitle, object excludeText);

		//internal abstract void ControlSetText(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);
		internal virtual void ControlSetText(string str, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is Control ctrl2)//No matter what I've tried, sending WM_SETTEXT will not work with buttons, however this method works flawlessly.
					ctrl2.Text = str;
				else
					_ = Platform.Window.TrySetTitle(item.Handle, str);//Just in case... it seems to work on text boxes.

				WindowInfoBase.DoControlDelay();
			}
		}

		internal virtual void ControlShow(object ctrl, object title, object text, object excludeTitle, object excludeText) =>
		ShowHideHelper(true, ctrl, title, text, excludeTitle, excludeText);

		internal abstract void ControlShowDropDown(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long EditGetCurrentCol(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long EditGetCurrentLine(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract string EditGetLine(int n, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long EditGetLineCount(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract string EditGetSelectedText(object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void EditPaste(string str, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract object ListViewGetContent(string options, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract void MenuSelect(object title, object text, object menu, object sub1, object sub2, object sub3, object sub4, object sub5, object sub6, object excludeTitle, object excludeText);

		internal abstract void PostMessage(uint msg, nint wparam, nint lparam, object ctrl, object title, object text, object excludeTitle, object excludeText);

		internal abstract long SendMessage(uint msg, object wparam, object lparam, object ctrl, object title, object text, object excludeTitle, object excludeText, int timeout);

		/// <summary>Accepts an integer or an object's integer Ptr without truncating floats.</summary>
		internal static bool CoerceMessageParam(object value, out nint param)
		{
			if (value is string { Length: 0 })
				value = 0L;

			if (value is IPointable pointable)
				value = pointable.Ptr;
			else if (value is Any)
			{
				var pointer = Script.GetPropertyValueOrNull(value, "Ptr");

				if (pointer == null)
				{
					param = 0;
					_ = Errors.MissingPropertyErrorOccurred(value, "Ptr");
					return false;
				}

				if (pointer is not (long or bool))
				{
					param = 0;
					_ = Errors.ExpectedTypeErrorOccurred("Integer", pointer);
					return false;
				}

				value = pointer;
			}

			var ok = value.CoerceLong(out var number, 0L, false);
			param = (nint)number;
			return ok;
		}

		private static void ShowHideHelper(bool val, object ctrl, object title, object text, object excludeTitle, object excludeText)
		{
			if (WindowSearch.SearchControl(ctrl, title, text, excludeTitle, excludeText) is WindowInfoBase item)
			{
				if (Control.FromHandle(item.Handle) is Control ctrl2)
					ctrl2.Visible = val;
				else
					_ = val ? Platform.Window.TryShow(item.Handle) : Platform.Window.TryHide(item.Handle);

				WindowInfoBase.DoControlDelay();
			}
		}
	}
}
