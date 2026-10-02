#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.ListView"/>. The grid holds the selection, and the rows' cells are rebound
		/// after a change. A selection the script changes is passed to the ItemSelect bookkeeping, which records it
		/// without raising an event.
		/// </summary>
		public partial class ListView
		{
			private partial long InsertRow(int index, in ListViewRowOptions options, string[] texts)
			{
				var lv = Lv;
				var row = (index < 0
						   ? lv.AddRow(texts, options.check == true, options.colstart)
						   : lv.InsertRow(index, texts, options.check == true, options.colstart)) - 1;

				if (ApplyRowState(row, options))
					Lv_SelectedRowsChanged(lv, EventArgs.Empty);

				return row + 1L;
			}

			private partial void ModifyRows(int start, int end, in ListViewRowOptions options, string[] texts)
			{
				var lv = Lv;
				var needsRefresh = false;
				var selectionChanged = false;

				for (var row = start; row < end; row++)
				{
					var item = lv.Items[row];

					for (int i = 0, j = options.colstart; i < texts.Length && j < lv.Columns.Count; i++, j++)
					{
						if (texts[i] is string text)
						{
							KeysharpListView.SetCellText(item, j, text);
							needsRefresh = true;
						}
					}

					if (options.check is bool check)
					{
						item.Checked = check;
						needsRefresh = true;
					}

					selectionChanged |= ApplyRowState(row, options);

					if (options.vis)
						((Eto.Forms.GridView)lv).ScrollToRow(row);
				}

				if (needsRefresh)
					lv.RefreshDataStore();

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
					if (focus)
					{
						lv.FocusedItem = lv.Items[row];
						listViewFocusedRow = row;
					}
					else if (listViewFocusedRow == row)
					{
						lv.FocusedItem = null;
						listViewFocusedRow = -1;
					}
				}

				return options.select.HasValue || options.focus.HasValue;
			}

			private partial void DeleteRows(int index)
			{
				var lv = Lv;

				if (index < 0)
					lv.Items.Clear();
				else
					lv.Items.RemoveAt(index);

				lv.RefreshDataStore();
				lv.SelectedItems.Clear();
				lv.SelectedIndices.Clear();
				lv.FocusedItem = null;
				listViewSelectedRows = [];
				listViewFocusedRow = -1;
			}

			private partial int NextRow(int start, bool focused)
			{
				var lv = Lv;

				if (focused)
				{
					var row = lv.FocusedItem is { } item ? lv.Items.IndexOf(item) : -1;
					return row > start ? row : -1;
				}

				var grid = (Eto.Forms.GridView)lv;
				var next = -1;

				foreach (var row in grid.SelectedRows)
					if (row > start && (next < 0 || row < next))
						next = row;

				return next < 0 && grid.SelectedRow > start ? grid.SelectedRow : next;
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
				var oldCount = lv.Columns.Count;
				lv.Columns.RemoveAt(index);
				var newCount = lv.Columns.Count;

				foreach (var item in lv.Items)
				{
					var values = new List<string>(oldCount);

					for (var i = 0; i < oldCount; i++)
						values.Add(KeysharpListView.GetCellText(item, i));

					values.RemoveAt(index);

					for (var i = 0; i < newCount; i++)
						KeysharpListView.SetCellText(item, i, values[i]);
				}

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
