namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The base of the controls holding a list of items: ListBox, DDL, ComboBox and Tab, as in AutoHotkey.
		/// </summary>
		public partial class List
		{
			/// <summary>
			/// Appends items, given as an array or one per parameter.
			/// </summary>
			public object Add(params object[] items)
			{
				if (items.Length > 0 && items[0] is Array arr)
					items = arr.array.ToArray();

				if (!CoerceStrings(items, out var texts))
					return DefaultObject;

				var values = new object[texts.Length];

				for (var i = 0; i < texts.Length; i++)
					values[i] = texts[i] ?? "";

				eventHandlerActive = false;

				try
				{
					//AddRange relieves the caller of having to set -Redraw first.
					switch (Ctrl)
					{
						case KeysharpListBox lb:
							lb.Items.AddRange(values);
							break;

						case KeysharpComboBox cb:
							cb.Items.AddRange(values);
							break;

						case KeysharpTabControl tc:
							AddTabs(tc, texts);
							break;
					}
				}
				finally
				{
					eventHandlerActive = true;
				}

				return DefaultObject;
			}

			/// <summary>
			/// Selects the item at a 1-based position, or the first whose text matches. Unlike ControlChooseIndex, this
			/// raises no Change or DoubleClick event.
			/// </summary>
			public object Choose(object value)
			{
				var s = value as string;
				_ = value.TryCoerceInt(out var i);
				i--;

				if (Ctrl is KeysharpTabControl tc)
				{
					if (!string.IsNullOrEmpty(s))
					{
						if (tc.FindTab(s, false) is TabPage tp)
							tc.SelectTab(tp);
					}
					else if (i >= 0)
						tc.SelectTab(i);
				}
				else if (Ctrl is KeysharpListBox lb)
				{
					if (!string.IsNullOrEmpty(s))
						lb.SelectItem(s);
					else if (i >= 0)
						lb.SetSelected(i, true);
					else
						lb.ClearSelected();
				}
				else if (Ctrl is KeysharpComboBox cb)
				{
					if (!string.IsNullOrEmpty(s))
						cb.SelectItem(s);
					else if (i >= 0)
						cb.SelectedIndex = i;
					else if (cb.DropDownStyle != ComboBoxStyle.DropDownList)
					{
						cb.SelectedIndex = -1;
						cb.ResetText();
					}
				}

				return DefaultObject;
			}

			/// <summary>
			/// Deletes the item at a 1-based position, or every item when the position is omitted. As in AutoHotkey,
			/// 0 is refused rather than read as "all", so a stray 0 cannot empty the list.
			/// </summary>
			public object Delete(object index = null)
			{
				if (!index.CoerceInt(out var position))
					return DefaultObject;

				var all = index is null;

				if (!all && position < 1)
					return Errors.InvalidParameterErrorOccurred(1, "Gui.List.Prototype.Delete", index);

				var i = position - 1;

				switch (Ctrl)
				{
					case KeysharpListBox lb:
						if (all)
							lb.Items.Clear();
						else if (i < lb.Items.Count)
							lb.Items.RemoveAt(i);

						break;

					case KeysharpComboBox cb:
						if (all)
							cb.Items.Clear();
						else if (i < cb.Items.Count)
							cb.Items.RemoveAt(i);

						break;

					case KeysharpTabControl tc:
						if (all)
							tc.TabPages.Clear();
						else if (i < tc.TabPages.Count)
							RemoveTab(tc, i);

						break;
				}

				return DefaultObject;
			}

			//Each toolkit's half, in Windows/List.cs and Unix/List.cs.

			private partial void AddTabs(KeysharpTabControl tc, string[] texts);

			private partial void RemoveTab(KeysharpTabControl tc, int index);
		}

		public partial class Tab
		{
			/// <summary>
			/// Makes later Gui.Add calls place their controls on a tab of this control, chosen by 1-based number or
			/// by name, or after the control when the tab is omitted, 0 or "".
			/// </summary>
			public object UseTab(object value = null, object exactMatch = null)
			{
				if (Ctrl is not KeysharpTabControl tc || Gui is not Keysharp.Builtins.Gui g)
					return DefaultObject;

				TabPage page = null;

				if (value is string s)
				{
					if (s.Length > 0 && (page = tc.FindTab(s, exactMatch.Ab())) == null)
						return Errors.ErrorOccurred($"No tab matching the name \"{s}\" found");
				}
				else if (value is not null)
				{
					if (!value.CoerceInt(out var i))
						return DefaultObject;

					if (i != 0)
					{
						if (i < 1 || i > tc.TabPages.Count)
							return Errors.ErrorOccurred($"Tab index {i} out of bounds [1..{tc.TabPages.Count}]");

						page = tc.TabPages[i - 1];
					}
				}

				if (page != null)
				{
					g.CurrentTab = page;
					g.LastContainer = page;
				}
				else
				{
					tc.AdjustSize(g.DpiScale, requestedSize);
					g.LastContainer = tc.GetLogicalParent();
				}

				return DefaultObject;
			}

			/// <summary>
			/// Sets the image list the tabs take their icons from, a Keysharp extension that <see cref="SetTabIcon"/>
			/// needs. Returns the ID of the list it replaces, or 0.
			/// </summary>
			public long SetImageList(object imageListID)
			{
				if (!imageListID.CoerceLong(out var id) || Ctrl is not KeysharpTabControl tc || ImageLists.IL_Get(id) is not ImageList il)
					return 0L;

				var old = ImageLists.IL_GetId(tc.ImageList);
				tc.ImageList = il;
				return old;
			}

			/// <summary>
			/// Shows an image of the tab control's image list on a tab, a Keysharp extension. Both numbers are 1-based, as
			/// tab and icon numbers are elsewhere, and an image number of 0 or past the list removes the icon.
			/// </summary>
			public object SetTabIcon(object tabIndex, object imageIndex)
			{
				if (!tabIndex.CoerceInt(out var tab) || !imageIndex.CoerceInt(out var image))
					return DefaultObject;

				if (Ctrl is not KeysharpTabControl tc || tab < 1 || tab > tc.TabPages.Count)
					return DefaultObject;

				var inList = tc.ImageList != null && image >= 1 && image <= tc.ImageList.Images.Count;
				SetTabImage(tc, tab - 1, inList ? image - 1 : -1);
				return DefaultObject;
			}

			/// <summary>Shows an image of the tab control's image list on a tab, or none for -1.</summary>
			private partial void SetTabImage(KeysharpTabControl tc, int tab, int image);
		}
	}
}
