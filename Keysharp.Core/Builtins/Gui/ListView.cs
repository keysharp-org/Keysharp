namespace Keysharp.Builtins
{
	internal enum ListViewColumnType
	{
		Text,
		Integer,
		Float
	}

	internal enum ListViewCaseMode
	{
		Insensitive,
		Sensitive,
		Locale,
		Logical
	}

	/// <summary>
	/// The sorting attributes AutoHotkey keeps for each ListView column, which a click on its header and the Sort
	/// option go by.
	/// </summary>
	internal sealed class ListViewColumn
	{
		internal ListViewColumnType type;
		internal ListViewCaseMode caseMode;
		internal bool preferDescending, sortDisabled, unidirectional;

		/// <summary>
		/// The order of two of the column's cells, as AutoHotkey's LV_GeneralSort and LV_Int32Sort have it: a number
		/// column reads text that is not a number as 0, and the logical order applies to a text column only.
		/// </summary>
		internal int Compare(string x, string y) => type switch
		{
			ListViewColumnType.Integer => (x.ParseLong() ?? 0L).CompareTo(y.ParseLong() ?? 0L),
			ListViewColumnType.Float => (x.ParseDouble() ?? 0.0).CompareTo(y.ParseDouble() ?? 0.0),
			_ => caseMode switch
			{
				ListViewCaseMode.Sensitive => string.CompareOrdinal(x, y),
				ListViewCaseMode.Locale => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
				ListViewCaseMode.Logical => LogicalComparer.Compare(x, y),
				_ => string.Compare(x, y, StringComparison.OrdinalIgnoreCase)
			}
		};
	}

	/// <summary>
	/// What the options of ListView.InsertCol and ModifyCol do to a column beyond its sorting attributes, which
	/// <see cref="TryParse"/> sets as it reads them, in order, as AutoHotkey's LV_InsertModifyCol does.
	/// </summary>
	/// </summary>
	internal struct ListViewColumnChange
	{
		internal const int AutoSize = -1, AutoSizeHeader = -2;

		internal Gui.GuiOptions.HorizontalAlignment? align;
		internal int? size;//A width, AutoSize or AutoSizeHeader: whichever of them comes last in the options.
		internal int? image;//0-based, or -1 for none.
		internal bool? imageRight;
		internal char sortNow;//'A' or 'D' to sort by the column once, else '\0'.

		/// <param name="size">What the column is sized to when the options name no size.</param>
		/// <returns>False when an option was not one, whose ValueError the script continued.</returns>
		internal static bool TryParse(string options, ListViewColumn column, int? size, out ListViewColumnChange change)
		{
			change = new ListViewColumnChange { size = size };

			foreach (Range r in options.AsSpan().SplitAny(Spaces))
			{
				var word = options.AsSpan(r).Trim();
				var adding = true;

				if (word.Length > 0 && word[0] is '-' or '+')
				{
					adding = word[0] == '+';
					word = word.Slice(1);
				}

				if (word.Length == 0)
					continue;

				//The sign is ignored by the type, alignment and sort words, as in AutoHotkey.
				if (word.Equals("Integer", StringComparison.OrdinalIgnoreCase))
				{
					column.type = ListViewColumnType.Integer;
					change.align = Gui.GuiOptions.HorizontalAlignment.Right;
				}
				else if (word.Equals("Float", StringComparison.OrdinalIgnoreCase))
				{
					column.type = ListViewColumnType.Float;
					change.align = Gui.GuiOptions.HorizontalAlignment.Right;
				}
				else if (word.Equals("Text", StringComparison.OrdinalIgnoreCase))
					column.type = ListViewColumnType.Text;
				else if (word.Equals("Right", StringComparison.OrdinalIgnoreCase))
					change.align = adding ? Gui.GuiOptions.HorizontalAlignment.Right : Gui.GuiOptions.HorizontalAlignment.Left;
				else if (word.Equals("Center", StringComparison.OrdinalIgnoreCase))
					change.align = adding ? Gui.GuiOptions.HorizontalAlignment.Center : Gui.GuiOptions.HorizontalAlignment.Left;
				else if (word.Equals("Left", StringComparison.OrdinalIgnoreCase))
					change.align = Gui.GuiOptions.HorizontalAlignment.Left;
				else if (word.Equals("Uni", StringComparison.OrdinalIgnoreCase))
					column.unidirectional = adding;
				else if (word.Equals("Desc", StringComparison.OrdinalIgnoreCase))
					column.preferDescending = adding;
				else if (word.StartsWith("Case", StringComparison.OrdinalIgnoreCase))
					column.caseMode = !adding ? ListViewCaseMode.Insensitive
									  : word.Slice(4).Equals("Locale", StringComparison.OrdinalIgnoreCase) ? ListViewCaseMode.Locale : ListViewCaseMode.Sensitive;
				else if (word.Equals("Logical", StringComparison.OrdinalIgnoreCase))
					column.caseMode = ListViewCaseMode.Logical;
				else if (word.StartsWith("Sort", StringComparison.OrdinalIgnoreCase))
					change.sortNow = word.Slice(4).Equals("Desc", StringComparison.OrdinalIgnoreCase) ? 'D' : 'A';
				else if (word.Equals("NoSort", StringComparison.OrdinalIgnoreCase))
					column.sortDisabled = adding;
				else if (word.StartsWith("Auto", StringComparison.OrdinalIgnoreCase))
					change.size = word.Slice(4).Equals("Hdr", StringComparison.OrdinalIgnoreCase) ? AutoSizeHeader : AutoSize;
				else if (word.StartsWith("Icon", StringComparison.OrdinalIgnoreCase))
				{
					var rest = word.Slice(4);

					if (rest.Equals("Right", StringComparison.OrdinalIgnoreCase))
						change.imageRight = adding;
					else
						change.image = adding && int.TryParse(rest, out var icon) ? icon - 1 : -1;
				}
				else if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
					change.size = (int)width;
				else
				{
					_ = Errors.ValueErrorOccurred("Invalid option.", word.ToString());
					return false;
				}
			}

			return true;
		}
	}

	public partial class Gui
	{
		/// <summary>
		/// The holder for a ListView control. Rows and columns are 1-based, and a row's states change only when the
		/// options name them. The semantics, the columns' sorting attributes among them, are shared; each toolkit's
		/// half, the partial members at the end, is in Windows/ListView.cs and Unix/ListView.cs.
		/// </summary>
		public partial class ListView
		{
			/// <summary>
			/// The row options of Add, Insert and Modify. A state is null when the options do not name it, which Modify
			/// leaves as it is.
			/// </summary>
			internal struct ListViewRowOptions
			{
				internal bool? select, focus, check;
				internal bool vis;
				internal int colstart;//0-based.
				internal int? icon;//1-based.

				/// <returns>False when an option was not one, whose ValueError the script continued.</returns>
				internal static bool TryParse(string options, out ListViewRowOptions o)
				{
					o = new ListViewRowOptions();

					foreach (Range r in options.AsSpan().SplitAny(Spaces))
					{
						var word = options.AsSpan(r).Trim();
						var adding = true;

						if (word.Length > 0 && word[0] is '-' or '+')
						{
							adding = word[0] == '+';
							word = word.Slice(1);
						}

						if (word.Length == 0)
							continue;

						if (word.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
							o.select = ApplySuffixFlag(word.Slice(6), adding);
						else if (word.StartsWith("Focus", StringComparison.OrdinalIgnoreCase))
							o.focus = ApplySuffixFlag(word.Slice(5), adding);
						else if (word.StartsWith("Check", StringComparison.OrdinalIgnoreCase))
							o.check = ApplySuffixFlag(word.Slice(5), adding);
						else if (word.StartsWith("Col", StringComparison.OrdinalIgnoreCase))
						{
							if (adding)
								o.colstart = Math.Max(0, (int)Strings.Atoi(word.Slice(3)) - 1);
						}
						else if (word.StartsWith("Icon", StringComparison.OrdinalIgnoreCase))
						{
							if (adding)
								o.icon = (int)Strings.Atoi(word.Slice(4));
						}
						else if (word.Equals("Vis", StringComparison.OrdinalIgnoreCase))
							o.vis = adding;
						else
						{
							_ = Errors.ValueErrorOccurred("Invalid option.", word.ToString());
							return false;
						}
					}

					return true;
				}
			}

			private readonly List<ListViewColumn> columns = [];
			private int sortedColumn = -1;
			private bool sortedAscending;

			private KeysharpListView Lv => (KeysharpListView)Ctrl;

			/// <summary>Appends a row and returns its number.</summary>
			public long Add(object options = null, params object[] columns) => AddRow(-1, options, columns);

			/// <summary>
			/// Inserts a row before a 1-based row number, appending it when the number is past the last row, and returns
			/// the new row's number.
			/// </summary>
			public long Insert(object rowNumber, object options = null, params object[] columns)
			{
				if (!rowNumber.CoerceInt(out var row))
					return 0L;

				if (row < 1)
					return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.Insert", rowNumber, 0L);

				return AddRow(row - 1, options, columns);
			}

			private long AddRow(int index, object options, object[] columns)
			{
				if (!options.CoerceString(out var opts) || !CoerceStrings(columns, out var texts) || !ListViewRowOptions.TryParse(opts, out var o))
					return 0L;

				//The colours are indexed by row, which an insertion shifts, as does an append the control sorts in.
				if (index >= 0 || Lv.Sorting != SortOrder.None)
					_ = ClearColors();

				eventHandlerActive = false;

				try
				{
					return InsertRow(index, o, texts);
				}
				finally
				{
					eventHandlerActive = true;
				}
			}

			/// <summary>
			/// Sets the given columns' text and the states the options name on a row, or on every row when the number is
			/// 0. An omitted column keeps its text. Returns 1, or 0 when the row does not exist.
			/// </summary>
			public long Modify(object rowNumber, object options = null, params object[] columns)
			{
				if (!rowNumber.CoerceInt(out var row) || !options.CoerceString(out var opts) || !CoerceStrings(columns, out var texts))
					return 0L;

				if (row < 0)
					return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.Modify", rowNumber, 0L);

				var count = Lv.Items.Count;

				if (row > count || !ListViewRowOptions.TryParse(opts, out var o))
					return 0L;

				if (row == 0)
					o.vis = false;

				eventHandlerActive = false;

				try
				{
					ModifyRows(row == 0 ? 0 : row - 1, row == 0 ? count : row, o, texts);
				}
				finally
				{
					eventHandlerActive = true;
				}

				return 1L;
			}

			/// <summary>
			/// Deletes a row, or every row when the number is omitted. As in AutoHotkey, 0 is refused rather than read as
			/// "all". Returns 1, or 0 when the row does not exist.
			/// </summary>
			public long Delete(object rowNumber = null)
			{
				if (!rowNumber.CoerceInt(out var row))
					return 0L;

				if (rowNumber is not null)
				{
					if (row < 1)
						return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.Delete", rowNumber, 0L);

					if (row > Lv.Items.Count)
						return 0L;
				}

				_ = ClearColors();
				DeleteRows(rowNumber is null ? -1 : row - 1);
				return 1L;
			}

			/// <summary>Deletes a column and returns 1. A column that does not exist fails, as in AutoHotkey.</summary>
			public long DeleteCol(object column)
			{
				if (!column.CoerceInt(out var col))
					return 0L;

				if (col < 1 || col > Lv.Columns.Count)
					return (long)Errors.ErrorOccurred("Failed", 0L);

				_ = ClearColors();
				DeleteColumn(col - 1);

				if (col - 1 < columns.Count)
					columns.RemoveAt(col - 1);

				return 1L;
			}

			/// <summary>The number of rows, of selected rows ("Selected") or of columns ("Column").</summary>
			public long GetCount(object mode = null)
			{
				if (!mode.CoerceString(out var m))
					return 0L;

				var lv = Lv;

				if (m.Length == 0)
					return lv.Items.Count;

				if (m[0] is 'S' or 's')
					return lv.SelectedItems.Count;

				if (m.StartsWith("Col", StringComparison.OrdinalIgnoreCase))
					return lv.Columns.Count;

				return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.GetCount", mode, 0L);
			}

			/// <summary>
			/// The number of the first row after a 1-based row number which is selected, or focused ("F") or checked
			/// ("C"), or 0 for none. A number below 1 searches from the top, so a loop passing back each result visits
			/// every such row once.
			/// </summary>
			public long GetNext(object startingRowNumber = null, object rowType = null)
			{
				if (!startingRowNumber.CoerceInt(out var start) || !rowType.CoerceString(out var type))
					return 0L;

				var word = type.AsSpan().TrimStart();
				var kind = word.IsEmpty ? '\0' : char.ToUpperInvariant(word[0]);

				if (kind is not ('\0' or 'F' or 'C'))
					return (long)Errors.InvalidParameterErrorOccurred(2, "Gui.ListView.Prototype.GetNext", rowType, 0L);

				var from = start < 1 ? -1 : start - 1;

				if (kind == 'C')
				{
					var items = Lv.Items;

					for (var i = from + 1; i < items.Count; i++)
						if (items[i].Checked)
							return i + 1L;

					return 0L;
				}

				return NextRow(from, kind == 'F') + 1L;
			}

			/// <summary>The text of a cell, or of a column's header when the row number is 0; one that does not exist fails.</summary>
			public string GetText(object rowNumber, object columnNumber = null)
			{
				if (!rowNumber.CoerceInt(out var row) || !columnNumber.CoerceInt(out var col, 1))
					return DefaultErrorString;

				if (row < 0)
					return (string)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.GetText", rowNumber, "");

				if (col < 1)
					return (string)Errors.InvalidParameterErrorOccurred(2, "Gui.ListView.Prototype.GetText", columnNumber, "");

				var lv = Lv;
				col--;

				//A row or column that is not there fails, as the native control does.
				if (row > lv.Items.Count || ((row == 0 || col > 0) && col >= lv.Columns.Count))
					return (string)Errors.ErrorOccurred("Failed", "");

				return row == 0 ? lv.Columns[col].Text : CellText(row - 1, col);
			}

			/// <summary>
			/// Inserts a column before a 1-based column number, appending it when the number is omitted or past the last
			/// column, and returns the new column's number. Unless the options size it, it is sized to its header.
			/// </summary>
			public long InsertCol(object columnNumber = null, object options = null, object columnTitle = null)
			{
				if (!columnNumber.CoerceInt(out var col, int.MaxValue) || !options.CoerceString(out var opts) || !columnTitle.CoerceString(out var title))
					return 0L;

				if (col < 1)
					return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.InsertCol", columnNumber, 0L);

				var column = new ListViewColumn();

				if (!ListViewColumnChange.TryParse(opts, column, ListViewColumnChange.AutoSizeHeader, out var change))
					return 0L;

				var lv = Lv;
				var count = lv.Columns.Count;
				var index = Math.Min(col - 1, count);
				_ = ClearColors();

				//The cells from the new column on move one along. A new first column keeps the old first column's text,
				//which the native control shows there, so the cell left blank is the second.
				var blank = Math.Max(index, 1);
				var rows = new string[lv.Items.Count][];

				for (var r = 0; r < rows.Length; r++)
				{
					var texts = rows[r] = new string[count + 1];

					for (var c = 0; c <= count; c++)
						texts[c] = c < blank ? CellText(r, c) : c == blank ? "" : CellText(r, c - 1);
				}

				index = InsertColumn(index, title, rows);

				while (columns.Count < index)
					columns.Add(new ListViewColumn());

				columns.Insert(index, column);
				ApplyColumnChange(index, change);
				return index + 1L;
			}

			/// <summary>
			/// Changes a column's options and title. With only the number it sizes that column to its contents, and with
			/// nothing every column. Returns 1, or 0 when the column does not exist.
			/// </summary>
			public long ModifyCol(object columnNumber = null, object options = null, object columnTitle = null)
			{
				if (columnNumber is null)
				{
					if (options is not null || columnTitle is not null)
						return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.ModifyCol", columnNumber, 0L);

					AutoSizeColumn(-1, false);
					return 1L;
				}

				if (!columnNumber.CoerceInt(out var col))
					return 0L;

				var index = col - 1;
				var exists = index >= 0 && index < Lv.Columns.Count;

				//A lone column number only sizes, and one that names no column does nothing, as in AutoHotkey.
				if (options is null && columnTitle is null)
				{
					if (!exists)
						return 0L;

					AutoSizeColumn(index, false);
					return 1L;
				}

				if (col < 1)
					return (long)Errors.InvalidParameterErrorOccurred(1, "Gui.ListView.Prototype.ModifyCol", columnNumber, 0L);

				if (!exists)
					return 0L;

				string title = null;

				if (!options.CoerceString(out var opts) || (columnTitle is not null && !columnTitle.CoerceString(out title))
						|| !ListViewColumnChange.TryParse(opts, Column(index), null, out var change))
					return 0L;

				if (title != null)
					SetColumnTitle(index, title);

				ApplyColumnChange(index, change);
				return 1L;
			}

			/// <summary>
			/// Sets the image list of the large (0), small (1) or state (2) icons, or the one the icon size suits when the
			/// type is omitted. Returns the ID of the list it replaces, or 0.
			/// </summary>
			public long SetImageList(object imageListID, object iconType = null)
			{
				if (!imageListID.CoerceLong(out var id) || !iconType.CoerceLong(out var type, -1))
					return 0L;

				return ImageLists.IL_Get(id) is ImageList il ? ReplaceImageList(il, type) : 0L;
			}

			/// <summary>
			/// Sorts by a clicked header, unless the column has NoSort. The direction reverses on the column the rows are
			/// sorted by, unless it has Uni, and is otherwise the column's preferred one.
			/// </summary>
			internal void SortByHeader(int column) => Sort(column, true, '\0');

			/// <summary>AutoHotkey's LV_Sort: sorts the rows by a column, in the forced direction ('A' or 'D') if any.</summary>
			private void Sort(int index, bool onlyIfEnabled, char direction)
			{
				var column = Column(index);

				if ((column.sortDisabled && onlyIfEnabled) || Lv.Items.Count < 2)
					return;

				var ascending = direction != '\0' ? direction == 'A'
								: index == sortedColumn && !column.unidirectional ? !sortedAscending : !column.preferDescending;
				_ = ClearColors();
				SortRows(index, ascending ? column.Compare : (x, y) => column.Compare(y, x));
				sortedColumn = index;
				sortedAscending = ascending;
			}

			private void ApplyColumnChange(int index, in ListViewColumnChange change)
			{
				if (change.align is GuiOptions.HorizontalAlignment align)
					SetColumnAlignment(index, align);

				if (change.image.HasValue || change.imageRight.HasValue)
					SetColumnImage(index, change.image, change.imageRight);

				if (change.size is int size)
				{
					if (size is ListViewColumnChange.AutoSize or ListViewColumnChange.AutoSizeHeader)
						AutoSizeColumn(index, size == ListViewColumnChange.AutoSizeHeader);
					else
						SetColumnWidth(index, size);
				}

				if (change.sortNow != '\0')
					Sort(index, false, change.sortNow);
			}

			//The sorting attributes of a column, created with the defaults for a column that had none yet, such as one the
			//control was created with.
			private ListViewColumn Column(int index)
			{
				while (columns.Count <= index)
					columns.Add(new ListViewColumn());

				return columns[index];
			}

			private string CellText(int row, int column)
			{
				var cells = Lv.Items[row].SubItems;
				return column < cells.Count ? cells[column].Text : "";
			}

			//Each toolkit's half. Row and column indexes here are 0-based.

			/// <summary>Adds a row at an index, appending it when the index is -1 or past the end, and returns its 1-based number.</summary>
			private partial long InsertRow(int index, in ListViewRowOptions options, string[] texts);

			/// <summary>Sets the non-null texts and the named states on the rows from start up to end.</summary>
			private partial void ModifyRows(int start, int end, in ListViewRowOptions options, string[] texts);

			/// <summary>Deletes a row, or every row when the index is -1.</summary>
			private partial void DeleteRows(int index);

			/// <summary>The index of the first row after start which is selected, or focused, or -1.</summary>
			private partial int NextRow(int start, bool focused);

			/// <summary>
			/// Inserts a column at an index no greater than the column count, gives each row the texts of every column,
			/// and returns the index the column took.
			/// </summary>
			private partial int InsertColumn(int index, string title, string[][] rows);

			private partial void DeleteColumn(int index);

			private partial void SetColumnTitle(int index, string title);

			private partial void SetColumnWidth(int index, int width);

			/// <summary>Sizes a column to its contents, or its header too, or every column to its contents when the index is -1.</summary>
			private partial void AutoSizeColumn(int index, bool header);

			private partial void SetColumnAlignment(int index, GuiOptions.HorizontalAlignment alignment);

			/// <summary>Sets the image the header shows, -1 for none, and whether it shows right of the title.</summary>
			private partial void SetColumnImage(int index, int? image, bool? right);

			/// <summary>Reorders the rows by the column's cells.</summary>
			private partial void SortRows(int column, Comparison<string> compare);

			/// <summary>Replaces the image list of an icon type (see <see cref="SetImageList"/>) and returns the old list's ID.</summary>
			private partial long ReplaceImageList(ImageList il, long type);
		}
	}
}
