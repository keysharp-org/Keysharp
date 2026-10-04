#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.ListView"/>. The grid shows the rows as they are inserted and removed and holds
		/// the selection. A selection or focus the script changes is passed to the ItemSelect bookkeeping, which records
		/// it without raising an event.
		/// </summary>
		public partial class ListView
		{
			private partial long InsertRow(int index, in ListViewRowOptions options, string[] texts)
			{
				var row = Lv.InsertRow(index, texts, options.check == true, options.colstart);

				if (ApplyRowState(row, options))
					Lv_SelectedRowsChanged(Lv, EventArgs.Empty);

				return row + 1L;
			}

			private partial void ModifyRows(int start, int end, in ListViewRowOptions options, string[] texts)
			{
				var lv = Lv;
				var changed = false;
				var selectionChanged = false;

				for (var row = start; row < end; row++)
				{
					var item = lv.Items[row];

					for (int i = 0, j = options.colstart; i < texts.Length && j < lv.Columns.Count; i++, j++)
					{
						if (texts[i] is string text)
						{
							KeysharpListView.SetCellText(item, j, text);
							changed = true;
						}
					}

					if (options.check is bool check)
					{
						item.Checked = check;
						changed = true;
					}

					selectionChanged |= ApplyRowState(row, options);

					if (options.vis)
						((Eto.Forms.GridView)lv).ScrollToRow(row);
				}

				if (changed)
					lv.ReloadData(Enumerable.Range(start, end - start));

				if (selectionChanged)
					Lv_SelectedRowsChanged(lv, EventArgs.Empty);
			}

			//Applies the selection and focus the options name to a row; true when they name either.
			private bool ApplyRowState(int row, in ListViewRowOptions options)
			{
				var lv = Lv;
				var grid = (Eto.Forms.GridView)lv;

				if (options.select is bool select)
				{
					if (select)
					{
						if (lv.MultiSelect)
							grid.SelectRow(row);
						else
							grid.SelectedRow = row;
					}
					else if (lv.MultiSelect)
						grid.UnselectRow(row);
					else if (grid.SelectedRow == row)
						grid.SelectedRow = -1;
				}

				if (options.focus is bool focus)
				{
					var item = lv.Items[row];

					if (focus)
						lv.FocusedItem = item;
					else if (lv.FocusedItem == item)
						lv.FocusedItem = null;
				}

				return options.select.HasValue || options.focus.HasValue;
			}

			private partial void DeleteRows(int index)
			{
				var lv = Lv;

				//A selected row the script deletes is gone rather than deselected, so the user hears of nothing.
				eventHandlerActive = false;

				try
				{
					if (index < 0)
					{
						lv.Items.Clear();
						lv.FocusedItem = null;
					}
					else
					{
						if (lv.Items[index] == lv.FocusedItem)
							lv.FocusedItem = null;

						lv.Items.RemoveAt(index);
					}
				}
				finally
				{
					eventHandlerActive = true;
				}
			}

			private partial int NextRow(int start, bool focused)
			{
				if (focused)
				{
					var row = Lv.FocusedRow;
					return row > start ? row : -1;
				}

				var next = -1;

				foreach (var row in ((Eto.Forms.GridView)Lv).SelectedRows)
					if (row > start && (next < 0 || row < next))
						next = row;

				return next;
			}

			private partial int InsertColumn(int index, string title, string[][] rows)
			{
				var lv = Lv;
				var header = new ColumnHeader { Text = title };

				if (index < lv.Columns.Count)
					lv.Columns.Insert(index, header);
				else
				{
					index = lv.Columns.Count;
					lv.Columns.Add(header);
				}

				for (var r = 0; r < rows.Length; r++)
					for (var c = 0; c < rows[r].Length; c++)
						KeysharpListView.SetCellText(lv.Items[r], c, rows[r][c]);

				lv.SyncColumns();
				return index;
			}

			private partial void DeleteColumn(int index)
			{
				var lv = Lv;
				lv.Columns.RemoveAt(index);

				foreach (var item in lv.Items)
					if (index < item.SubItems.Count)
						item.SubItems.RemoveAt(index);

				lv.SyncColumns();
			}

			private partial void SetColumnTitle(int index, string title)
			{
				Lv.Columns[index].Text = title;
				Lv.SyncColumns();
			}

			private partial void SetColumnWidth(int index, int width) => Lv.SetColumnWidth(index, width);

			private partial void AutoSizeColumn(int index, bool header)
			{
				if (index < 0)
					Lv.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
				else
					Lv.AutoResizeColumn(index, header);
			}

			private partial void SetColumnAlignment(int index, GuiOptions.HorizontalAlignment alignment) =>
				Lv.SetColumnAlignment(index, alignment switch
				{
					GuiOptions.HorizontalAlignment.Center => Eto.Forms.TextAlignment.Center,
					GuiOptions.HorizontalAlignment.Right => Eto.Forms.TextAlignment.Right,
					_ => Eto.Forms.TextAlignment.Left
				});

			//Eto's grid header shows text only.
			private partial void SetColumnImage(int index, int? image, bool? right)
			{
			}

			private partial void SortRows(int column, Comparison<string> compare) => Lv.SortRows(column, compare);

			//Eto's grid has one image list for every icon type.
			private partial long ReplaceImageList(ImageList il, long type)
			{
				var old = ImageLists.IL_GetId(Lv.ImageList);
				Lv.ImageList = il;
				return old;
			}
		}
	}
}
#endif
