#if WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The WinForms half of <see cref="Gui.ListView"/>. Row states go through the item properties, each one native
		/// message, and only the states the options name are touched.
		/// </summary>
		public partial class ListView
		{
			private partial long InsertRow(int index, in ListViewRowOptions options, string[] texts)
			{
				var lv = Lv;
				var item = new ListViewItem();

				while (item.SubItems.Count < lv.Columns.Count)
					_ = item.SubItems.Add("");

				SetTexts(item, options.colstart, texts);

				//The selection and the check are sent with the insertion itself. WinForms keeps an item's focus only in
				//the native control, so that one follows it, once the control exists.
				if (options.select == true)
					item.Selected = true;

				if (options.check == true)
					item.Checked = true;

				SetIcon(lv, item, options.icon);
				_ = index >= 0 && index < lv.Items.Count ? lv.Items.Insert(index, item) : lv.Items.Add(item);

				if (options.focus == true)
				{
					_ = lv.Handle;
					item.Focused = true;
				}

				return item.Index + 1L;
			}

			private partial void ModifyRows(int start, int end, in ListViewRowOptions options, string[] texts)
			{
				var lv = Lv;

				if (options.focus.HasValue)
					_ = lv.Handle;

				for (var i = start; i < end; i++)
				{
					var item = lv.Items[i];
					SetTexts(item, options.colstart, texts);

					if (options.select is bool select)
						item.Selected = select;

					if (options.focus is bool focus)
						item.Focused = focus;

					if (options.check is bool check)
						item.Checked = check;

					SetIcon(lv, item, options.icon);

					if (options.vis)
						item.EnsureVisible();
				}
			}

			private static void SetTexts(ListViewItem item, int colstart, string[] texts)
			{
				for (int i = 0, j = colstart; i < texts.Length && j < item.SubItems.Count; i++, j++)
					if (texts[i] is string text)
						item.SubItems[j].Text = text;
			}

			//Icon numbers are 1-based, image indexes 0-based.
			private static void SetIcon(System.Windows.Forms.ListView lv, ListViewItem item, int? icon)
			{
				if (icon is int n && n >= 1 && lv.SmallImageList != null && n <= lv.SmallImageList.Images.Count)
					item.ImageIndex = n - 1;
			}

			private partial void DeleteRows(int index)
			{
				if (index < 0)
					Lv.Items.Clear();
				else
					Lv.Items.RemoveAt(index);
			}

			private partial int NextRow(int start, bool focused) =>
				(int)WindowsAPI.SendMessage(Lv.Handle, WindowsAPI.LVM_GETNEXTITEM, start, focused ? WindowsAPI.LVNI_FOCUSED : WindowsAPI.LVNI_SELECTED);

			private partial int InsertColumn(int index, string title, string[][] rows)
			{
				var lv = Lv;
				var header = new ColumnHeader { Text = title };

				if (index >= lv.Columns.Count)
					index = lv.Columns.Add(header);
				else
					lv.Columns.Insert(index, header);

				//The items keep their cells in column order, which the new column does not shift.
				for (var r = 0; r < rows.Length; r++)
				{
					var cells = lv.Items[r].SubItems;

					while (cells.Count < rows[r].Length)
						_ = cells.Add("");

					for (var c = 0; c < rows[r].Length; c++)
						cells[c].Text = rows[r][c];
				}

				return index;
			}

			private partial void DeleteColumn(int index) => Lv.Columns.RemoveAt(index);

			private partial void SetColumnTitle(int index, string title) => Lv.Columns[index].Text = title;

			private partial void SetColumnWidth(int index, int width) => Lv.Columns[index].Width = (int)(width * ((Gui)Gui).DpiScale);

			private partial void AutoSizeColumn(int index, bool header)
			{
				if (index < 0)
					Lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
				else
					Lv.AutoResizeColumn(index, header ? ColumnHeaderAutoResizeStyle.HeaderSize : ColumnHeaderAutoResizeStyle.ColumnContent);
			}

			//The first column stays left-aligned whatever is asked, a limit of the native control.
			private partial void SetColumnAlignment(int index, GuiOptions.HorizontalAlignment alignment) =>
				Lv.Columns[index].TextAlign = alignment switch
				{
					GuiOptions.HorizontalAlignment.Center => HorizontalAlignment.Center,
					GuiOptions.HorizontalAlignment.Right => HorizontalAlignment.Right,
					_ => HorizontalAlignment.Left
				};

			private partial void SetColumnImage(int index, int? image, bool? right)
			{
				var lv = Lv;

				if (image is int i)
					lv.Columns[index].ImageIndex = i < 0 ? -1 : i;

				if (right is bool onRight)
				{
					var colflags = new LV_COLUMN { mask = WindowsAPI.LVCF_FMT };
					_ = WindowsAPI.SendLVColMessage(lv.Handle, WindowsAPI.LVM_GETCOLUMN, (uint)index, ref colflags);
					colflags.mask = WindowsAPI.LVCF_FMT;

					if (onRight)
						colflags.fmt |= WindowsAPI.LVCFMT_BITMAP_ON_RIGHT;
					else
						colflags.fmt &= ~WindowsAPI.LVCFMT_BITMAP_ON_RIGHT;

					_ = WindowsAPI.SendLVColMessage(lv.Handle, WindowsAPI.LVM_SETCOLUMN, (uint)index, ref colflags);
				}
			}

			private partial void SortRows(int column, Comparison<string> compare)
			{
				//Assigning a sorter sorts at once; it is removed again so that it plays no part in the control's own
				//Sort option, which orders the rows it adds by their first column.
				var lv = Lv;
				lv.ListViewItemSorter = new RowComparer(column, compare);
				lv.ListViewItemSorter = null;
			}

			private partial long ReplaceImageList(ImageList il, long type)
			{
				var lv = Lv;

				if (type is < 0 or > 2)
				{
					_ = Env.SysGet(SystemMetric.SM_CXSMICON).TryCoerceLong(out var cxSmIcon);
					type = il.ImageSize.Width > cxSmIcon ? 0 : 1;
				}

				long old;

				switch (type)
				{
					case 0:
						old = ImageLists.IL_GetId(lv.LargeImageList);
						lv.LargeImageList = il;
						break;

					case 1:
						old = ImageLists.IL_GetId(lv.SmallImageList);
						lv.SmallImageList = il;
						break;

					default:
						old = ImageLists.IL_GetId(lv.StateImageList);
						lv.StateImageList = il;
						break;
				}

				return old;
			}

			private sealed class RowComparer(int column, Comparison<string> compare) : IComparer
			{
				public int Compare(object x, object y) => compare(Cell(x), Cell(y));

				private string Cell(object item) => item is ListViewItem lvi && column < lvi.SubItems.Count ? lvi.SubItems[column].Text : "";
			}
		}
	}
}
#endif
