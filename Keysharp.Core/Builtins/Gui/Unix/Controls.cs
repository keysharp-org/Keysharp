#if !WINDOWS
using System.Diagnostics;
using WinForms = System.Windows.Forms;
using CallbackRegistry = Keysharp.Internals.Scripting.CallbackRegistry;

namespace Keysharp.Builtins
{
	public enum SortOrder
	{
		None,
		Ascending,
		Descending
	}

	public enum View
	{
		Details,
		LargeIcon,
		SmallIcon,
		List,
		Tile
	}

	public enum ColumnHeaderStyle
	{
		None,
		Nonclickable,
		Clickable
	}

	public enum ColumnHeaderAutoResizeStyle
	{
		None,
		HeaderSize,
		ColumnContent
	}

	public enum TabAlignment
	{
		Top,
		Bottom,
		Left,
		Right
	}

	public enum TabAppearance
	{
		Normal,
		FlatButtons
	}

	public enum CharacterCasing
	{
		Normal,
		Upper,
		Lower
	}

	/// <summary>
	/// The Uppercase and Lowercase options of an Edit or RichEdit, which convert only the text an edit inserted, whether
	/// typed, pasted or set, as the Windows edit control does. The toolkit reports a typed character from inside its own
	/// edit, where the text cannot be changed again, so the inserted text is converted on the UI loop's next pass.
	/// </summary>
	internal sealed class TextNormalizer
	{
		private readonly TextControl control;
		private readonly Action normalize;
		private CharacterCasing casing;
		private bool pending, rewriting;
		private string normalizedText = "";

		internal TextNormalizer(TextControl control)
		{
			this.control = control;
			normalize = Normalize;
		}

		internal CharacterCasing Casing
		{
			get => casing;
			set
			{
				casing = value;
				normalizedText = control.Text ?? "";
			}
		}

		/// <summary>Called for every text change, including the one this makes itself.</summary>
		internal void TextChanged()
		{
			if (rewriting || pending || casing == CharacterCasing.Normal)
				return;

			pending = true;
			Application.Instance.AsyncInvoke(normalize);
		}

		private void Normalize()
		{
			pending = false;

			if (control.IsDisposed || casing == CharacterCasing.Normal)
				return;

			var current = control.Text ?? "";
			var previous = normalizedText;
			var prefix = current.AsSpan().CommonPrefixLength(previous);

			//GTK replacement boundaries must encompass both units of a surrogate pair.
			if (prefix > 0 && prefix < current.Length && char.IsHighSurrogate(current[prefix - 1]) && char.IsLowSurrogate(current[prefix]))
				prefix--;

			var suffix = 0;
			var maxSuffix = Math.Min(current.Length, previous.Length) - prefix;

			while (suffix < maxSuffix && current[^(suffix + 1)] == previous[^(suffix + 1)])
				suffix++;

			if (suffix > 0 && suffix < current.Length && char.IsHighSurrogate(current[^(suffix + 1)]) && char.IsLowSurrogate(current[^suffix]))
				suffix--;

			var length = current.Length - prefix - suffix;

			if (length > 0)
			{
				var inserted = current.AsSpan(prefix, length);
				var adjusted = casing == CharacterCasing.Upper ? inserted.ToString().ToUpperInvariant() : inserted.ToString().ToLowerInvariant();

				if (!inserted.SequenceEqual(adjusted))
				{
					var caret = control.CaretIndex;
					rewriting = true;

					try
					{
						control.ReplaceText(prefix, length, adjusted);
					}
					finally
					{
						rewriting = false;
					}

					control.CaretIndex = caret;
					current = control.Text ?? "";
				}
			}

			normalizedText = current;
		}
	}

	/// <summary>
	/// The Number option of an Edit or RichEdit, which as ES_NUMBER refuses a typed character that is not a digit. Text a
	/// script sets or the user pastes is taken as it is, as in AutoHotkey.
	/// </summary>
	internal static class NumberOption
	{
		private static readonly EventHandler<TextInputEventArgs> refuseNonDigits = (_, e) =>
		{
			foreach (var ch in e.Text ?? "")
			{
				if (!char.IsDigit(ch))
				{
					e.Cancel = true;
					return;
				}
			}
		};

		//Subscribed only while the option is on, since handling TextInput routes the control's keys through Eto's own
		//input context.
		internal static void Apply(TextControl control, ref bool numeric, bool value)
		{
			if (value == numeric)
				return;

			numeric = value;

			if (value)
				control.TextInput += refuseNonDigits;
			else
				control.TextInput -= refuseNonDigits;
		}
	}

	public enum SizeGripStyle
	{
		Auto,
		Show,
		Hide
	}

	public enum PictureBoxSizeMode
	{
		Normal,
		StretchImage,
		AutoSize,
		CenterImage,
		Zoom
	}

	public enum CheckState
	{
		Unchecked,
		Checked,
		Indeterminate
	}

	public enum ContentAlignment
	{
		TopLeft,
		TopCenter,
		TopRight,
		MiddleLeft,
		MiddleCenter,
		MiddleRight,
		BottomLeft,
		BottomCenter,
		BottomRight
	}

	public enum LeftRightAlignment
	{
		Left,
		Right
	}

	public enum FormBorderStyle
	{
		None,
		FixedSingle,
		FixedDialog,
		Sizable,
		SizableToolWindow,
		FixedToolWindow
	}

	public enum FormStartPosition
	{
		Manual,
		CenterScreen,
		WindowsDefaultLocation,
		WindowsDefaultBounds,
		CenterParent
	}

	public enum SelectionMode
	{
		One,
		MultiExtended
	}

	public enum ComboBoxStyle
	{
		DropDown,
		Simple,
		DropDownList
	}

	public enum TickStyle
	{
		None,
		TopLeft,
		BottomRight,
		Both
	}

	public enum ProgressBarStyle
	{
		Blocks,
		Continuous,
		Marquee
	}

	//The style numbers the shared Gui code passes to each constructor are Win32 styles, which these controls have no use for.

	public class KeysharpButton : Button
	{
		public bool AutoSize { get; set; }

		public KeysharpButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpCheckBox : CheckBox
	{
		//Projected from Checked rather than stored alongside it: a stored copy cannot see the user ticking
		//the box, so it only ever reports the last assignment.
		public CheckState CheckState
		{
			get => Checked == null ? CheckState.Indeterminate : Checked.Value ? CheckState.Checked : CheckState.Unchecked;
			set
			{
				if (value == CheckState.Indeterminate)
					ThreeState = true;

				Checked = value == CheckState.Indeterminate ? null : value == CheckState.Checked;
			}
		}

		public KeysharpCheckBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	/// <summary>
	/// The items of a ListBox, DropDownList or ComboBox. With Sort, each item goes after the items that compare equal
	/// or lower, ignoring case, as a Windows list with LBS_SORT or CBS_SORT places it.
	/// </summary>
	public sealed class ListControlItems : ObservableCollection<object>
	{
		private bool sorted;

		internal bool Sorted
		{
			get => sorted;
			set
			{
				if (value == sorted)
					return;

				sorted = value;

				if (sorted && Count > 1)
				{
					var items = this.ToArray();
					ClearItems();

					foreach (var item in items)
						Add(item);
				}
			}
		}

		protected override void InsertItem(int index, object item) => base.InsertItem(sorted ? IndexFor(item) : index, item);

		/// <summary>The index an added item takes: the end, or with Sort after the items that compare equal or lower.</summary>
		internal int IndexFor(object item)
		{
			if (!sorted)
				return Count;

			var text = item?.ToString();
			int low = 0, high = Count;

			while (low < high)
			{
				var mid = (low + high) >>> 1;

				if (string.Compare(this[mid]?.ToString(), text, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase) <= 0)
					low = mid + 1;
				else
					high = mid;
			}

			return low;
		}
	}

	public class KeysharpComboBox : ComboBox
	{
		private ComboBoxStyle dropDownStyle = ComboBoxStyle.DropDown;

		/// <summary>A DropDownList is a combo box whose text cannot be edited. Eto has no always-open list, so Simple shows as DropDown.</summary>
		public ComboBoxStyle DropDownStyle
		{
			get => dropDownStyle;
			set
			{
				dropDownStyle = value;
				ReadOnly = value == ComboBoxStyle.DropDownList;
			}
		}

		public new ListControlItems Items { get; } = new ();

		public bool Sorted
		{
			get => Items.Sorted;
			set => Items.Sorted = value;
		}

		public KeysharpComboBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			ItemTextBinding = Binding.Delegate<object, string>(item => item?.ToString());
			DataStore = Items;
		}
	}

	public class KeysharpDateTimePicker : DateTimePicker
	{
		public KeysharpDateTimePicker(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpCustomControl : Control
	{
		public KeysharpCustomControl(string _className, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpTextBox : TextBox
	{
		private readonly TextNormalizer normalizer;

		private bool numeric;

		internal bool IsNumeric
		{
			get => numeric;
			set => NumberOption.Apply(this, ref numeric, value);
		}

		public CharacterCasing CharacterCasing
		{
			get => normalizer.Casing;
			set => normalizer.Casing = value;
		}

		public KeysharpTextBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0) => normalizer = new (this);

		protected override void OnTextChanged(EventArgs e)
		{
			normalizer.TextChanged();
			base.OnTextChanged(e);
		}
	}

	public class KeysharpPasswordBox : PasswordBox
	{
		private readonly TextNormalizer normalizer;

		private bool numeric;

		internal bool IsNumeric
		{
			get => numeric;
			set => NumberOption.Apply(this, ref numeric, value);
		}

		public CharacterCasing CharacterCasing
		{
			get => normalizer.Casing;
			set => normalizer.Casing = value;
		}

		public KeysharpPasswordBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0) => normalizer = new (this);

		protected override void OnTextChanged(EventArgs e)
		{
			normalizer.TextChanged();
			base.OnTextChanged(e);
		}
	}

	public class KeysharpTextArea : TextArea
	{
		private readonly TextNormalizer normalizer;

		private bool numeric;

		internal bool IsNumeric
		{
			get => numeric;
			set => NumberOption.Apply(this, ref numeric, value);
		}

		public CharacterCasing CharacterCasing
		{
			get => normalizer.Casing;
			set => normalizer.Casing = value;
		}

		public KeysharpTextArea(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0) => normalizer = new (this);

		protected override void OnTextChanged(EventArgs e)
		{
			normalizer.TextChanged();
			base.OnTextChanged(e);
		}
	}

	public class KeysharpGroupBox : GroupBox
	{
		public KeysharpGroupBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpLabel : Forms.Label
	{
		public bool AutoSize { get; set; }

		public KeysharpLabel(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	/// <summary>
	/// A custom-drawn link label. Eto's <see cref="Forms.Label"/> renders plain text with a single colour,
	/// so to match the Windows backend (only the anchor text is coloured and underlined) the control is a
	/// <see cref="Drawable"/> that paints each segment itself: normal text in <see cref="TextColor"/>, link
	/// regions in <see cref="LinkColor"/> with an underline.
	/// </summary>
	public class KeysharpLinkLabel : Drawable
	{
		internal static readonly Color LinkColor = Color.FromArgb(0, 102, 204);

		internal bool clickSet = false;
		internal List<Tuple<int, int, Tuple<string, string>>> links;
		private string text = "";
		private Font font;
		private Color? textColor;
		private (float left, float right)[] linkBounds;//Cached x-extent of each link, parallel to links.
		private (string text, float x, float width, bool link)[] segments = [];//What Paint draws, measured once per Text or Font.
		private float lineHeight;
		private bool cursorOverLink;
		private readonly bool transparent;//True when the native widget is windowless and the form shows through.

		public Color TextColor
		{
			get => textColor ?? SystemColors.ControlText;
			set
			{
				textColor = value;
				Invalidate();
			}
		}

		public string Text
		{
			get => text;
			set
			{
				//Strip the <a href=...> markup so the label shows clean text and clicks open the parsed URL,
				//rather than feeding the whole raw string to xdg-open (which would word-split it).
				var parsed = GuiHelper.ParseLinkLabelText(value ?? "");
				text = parsed.Item1;
				links = parsed.Item2;
				UpdateSize();
				Invalidate();
			}
		}

		public Font Font
		{
			get => font;
			set
			{
				font = value;
				UpdateSize();
				Invalidate();
			}
		}

		public KeysharpLinkLabel(string text, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
#if LINUX
			//Make the underlying GTK EventBox windowless so the form shows through (true transparency, like a
			//Label). Otherwise the drawing area paints its own window black where no text is drawn.
			try
			{
				if (this.ToNative() is Gtk.EventBox eventBox)
				{
					eventBox.VisibleWindow = false;
					transparent = true;
				}
			}
			catch { }
#elif OSX
			//Eto's Mac Drawable is backed by an NSView that only paints a background when one is explicitly set,
			//so it is transparent by default; treat it as such so the form shows through instead of a white fill.
			transparent = true;
#endif
			Paint += KeysharpLinkLabel_Paint;
			MouseMove += KeysharpLinkLabel_MouseMove;
			Text = text;
		}

		//The hand cursor should only appear over the blue link text, so track which region the mouse is over.
		private void KeysharpLinkLabel_MouseMove(object sender, MouseEventArgs e)
		{
			var overLink = LinkIndexAt(e.Location) >= 0;

			if (overLink != cursorOverLink)
			{
				cursorOverLink = overLink;
				Cursor = overLink ? Cursors.Pointer : Cursors.Default;
			}
		}

		private void KeysharpLinkLabel_Paint(object sender, PaintEventArgs e)
		{
			var f = font ?? MainWindow.OurDefaultFont;

			//A windowless GTK widget already shows the form behind it, so only fill when opaque: either the
			//control has an explicit background, or it owns its window (non-Linux) and would render black.
			var bg = BackgroundColor;

			if (bg.A > 0 || !transparent)
			{
				using var backgroundBrush = new SolidBrush(bg.A > 0 ? bg : SystemColors.ControlBackground);
				e.Graphics.FillRectangle(backgroundBrush, new Rectangle(0, 0, Width, Height));
			}

			var textColor = TextColor;

			foreach (var (segment, x, width, link) in segments)
			{
				var color = link ? LinkColor : textColor;
				e.Graphics.DrawText(f, color, x, 0, segment);

				if (link)
					e.Graphics.DrawLine(color, x, lineHeight - 1, x + width, lineHeight - 1);
			}
		}

		private void UpdateSize()
		{
			var f = font ?? MainWindow.OurDefaultFont;
			//A single space gives empty text a sensible line height.
			var size = f.MeasureString(text.Length > 0 ? text : " ");
			Size = new Size((int)Math.Ceiling(size.Width) + 1, (int)Math.Ceiling(size.Height));
			lineHeight = size.Height;
			var list = new List<(string, float, float, bool)>();
			var bounds = links is { Count: > 0 } ? new (float, float)[links.Count] : null;
			var pos = 0;

			for (var i = 0; bounds != null && i < links.Count; i++)
			{
				var start = Math.Min(text.Length, links[i].Item1);
				var stop = Math.Min(text.Length, start + links[i].Item2);
				AddSegment(pos, start, false);
				bounds[i] = (f.MeasureString(text[..start]).Width, f.MeasureString(text[..stop]).Width);
				AddSegment(start, stop, true);
				pos = Math.Max(pos, stop);
			}

			AddSegment(pos, text.Length, false);
			segments = [.. list];
			linkBounds = bounds;

			//Placed by the width of everything before it, as the whole text is laid out.
			void AddSegment(int from, int to, bool link)
			{
				if (to > from)
				{
					var x = from == 0 ? 0f : f.MeasureString(text[..from]).Width;
					list.Add((text[from..to], x, f.MeasureString(text[..to]).Width - x, link));
				}
			}
		}

		/// <summary>
		/// Returns the index of the link region under <paramref name="loc"/>, or -1 when the point is not over
		/// any link. Used for both the click target and the hand cursor, so plain text (and the gaps between
		/// links) is correctly treated as non-clickable.
		/// </summary>
		internal int LinkIndexAt(PointF? loc)
		{
			if (linkBounds == null || loc is not PointF p)
				return -1;

			for (var i = 0; i < linkBounds.Length; i++)
				if (p.X >= linkBounds[i].left && p.X <= linkBounds[i].right)
					return i;

			return -1;
		}

		internal static void OpenUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return;

			if (!url.Contains("://"))
				url = "https://" + url;

			//UseShellExecute lets .NET pick the platform's default opener (open on macOS, xdg-open on Linux);
			//passing the URL as FileName avoids hardcoding a launcher that may not exist on this platform.
			var proc = new Process
			{
				EnableRaisingEvents = false,
				StartInfo = new ProcessStartInfo(url)
				{
					UseShellExecute = true
				}
			};
			proc.Start();
		}
	}

	public class KeysharpListBox : ListBox
	{
		/// <summary>
		/// Multi still makes Value and Text arrays, but Eto's list box selects one item at a time, so they hold at
		/// most one.
		/// </summary>
		public SelectionMode SelectionMode { get; set; } = SelectionMode.One;

		public new ListControlItems Items { get; } = new ();

		public int ItemHeight { get; set; } = 16;

		public bool Sorted
		{
			get => Items.Sorted;
			set => Items.Sorted = value;
		}

		public KeysharpListBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			ItemTextBinding = Binding.Delegate<object, string>(item => item?.ToString());
			DataStore = Items;
		}
	}

	public class KeysharpListView : GridView
	{
		internal event Action<int> ColumnClicked;

		/// <summary>A row: the text of its cells, first column first, and its check.</summary>
		public class ListViewItem
		{
			public class ListViewSubItem
			{
				public string Text { get; set; } = "";
			}

			public bool Checked { get; set; }
			public List<ListViewSubItem> SubItems { get; } = [];
		}

		/// <summary>The rows, which the grid shows as they are inserted and removed.</summary>
		public sealed class ListViewItemCollection : ObservableCollection<ListViewItem>
		{
			//Reorders the rows without a notification of its own, so the caller reloads them once.
			internal void Sort(Comparison<ListViewItem> compare) => ((List<ListViewItem>)Items).Sort(compare);
		}

		public ListViewItemCollection Items { get; } = [];

		/// <summary>The focused row, which the selection does not change unless the user moves it away.</summary>
		internal ListViewItem FocusedItem { get; set; }

		internal int FocusedRow => FocusedItem is { } item ? Items.IndexOf(item) : -1;

		/// <summary>The selected rows, lowest first: what the code shared with the WinForms ListView reads.</summary>
		public IReadOnlyList<int> SelectedIndices => [.. SelectedRows];

		public new WinForms.ColumnHeaderCollection Columns { get; } = new WinForms.ColumnHeaderCollection();
		public bool CheckBoxes
		{
			get => checkBoxes;
			set
			{
				checkBoxes = value;
				UpdateCheckColumn();
			}
		}
		public new bool GridLines
		{
			get => gridLines;
			set
			{
				gridLines = value;
				base.GridLines = value ? Eto.Forms.GridLines.Both : Eto.Forms.GridLines.None;
			}
		}
		public bool LabelEdit
		{
			get => labelEdit;
			set
			{
				labelEdit = value;
				ApplyLabelEdit();
			}
		}
		public View View
		{
			get => View.Details;
			set
			{
				if (value != View.Details)
					throw new NotImplementedException("ListView view modes other than Report are not implemented on Linux.");

				ApplyHeaderStyle();
			}
		}
		public SortOrder Sorting
		{
			get => sorting;
			set
			{
				sorting = value;

				if (sorting != SortOrder.None)
					SortRows(0, sorting == SortOrder.Ascending ? CompareSorted : (x, y) => CompareSorted(y, x));
			}
		}
		public bool MultiSelect
		{
			get => AllowMultipleSelection;
			set => AllowMultipleSelection = value;
		}
		public bool AllowColumnReorder
		{
			get => AllowColumnReordering;
			set => AllowColumnReordering = value;
		}
		public ColumnHeaderStyle HeaderStyle
		{
			get => headerStyle;
			set
			{
				headerStyle = value;
				ApplyHeaderStyle();
			}
		}
		public bool AutoSortHeader
		{
			get => autoSortHeader;
			set => autoSortHeader = value;
		}

		/// <summary>
		/// The text colour of the c option, which the ForeColor accessor sets as it sets other controls' TextColor. Only
		/// the cell formatting can show it.
		/// </summary>
		public Color TextColor
		{
			get => textColor ?? SystemColors.ControlText;
			set
			{
				textColor = value;
				RefreshColors();
			}
		}

		internal ImageList ImageList { get; set; }

		private readonly int addStyle;
		private readonly List<GridColumn> etoColumns = [];
		private readonly List<TextBoxCell> etoTextCells = [];
		private readonly Dictionary<GridColumn, int> columnNumbers = [];
		private bool checkBoxes;
		private bool gridLines;
		private SortOrder sorting;
		private bool labelEdit;
		private ColumnHeaderStyle headerStyle = ColumnHeaderStyle.Clickable;
		private bool autoSortHeader = true;
		private bool formatting;
		private Color? textColor;
		private GridColumn checkColumn;

		internal GridColumn CheckColumn => checkColumn;
		internal bool HasCheckBoxes => checkBoxes;

		public KeysharpListView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			DataStore = Items;
			ShowHeader = true;
			MultiSelect = true;//Unless -Multi, as in AutoHotkey.
			ColumnHeaderClick += OnColumnHeaderClickInternal;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				ImageLists.DestroyWithListView(addStyle, ImageList);

			base.Dispose(disposing);
		}

		/// <summary>Draws a row again with its colours, or every row when the row is -1.</summary>
		internal void RefreshColors(int row = -1)
		{
			//Eto calls the formatting back for every cell it draws, so it is attached only once a colour is set. It cannot
			//be detached again.
			if (!formatting)
			{
				formatting = true;
				CellFormatting += FormatColors;
			}

			if (row >= 0)
				ReloadData(row);
			else
				ReloadAllRows();

			Invalidate();
		}

		private void FormatColors(object sender, GridCellFormatEventArgs e)
		{
			if (this.GetGuiControl() is not Gui.ListView owner)
				return;

			//The checkbox column is outside the script's column numbering and takes the row's colours.
			var colors = owner.GetColors(e.Row + 1, columnNumbers.TryGetValue(e.Column, out var column) ? column : 0);

			if ((colors.Text ?? textColor) is Color text)
				e.ForegroundColor = text;

			if (colors.Back is Color back)
				e.BackgroundColor = back;
		}

		private void ReloadAllRows()
		{
			if (Items.Count > 0)
				ReloadData(Enumerable.Range(0, Items.Count));
		}

		internal void SyncColumns()
		{
			base.Columns.Clear();
			etoColumns.Clear();
			etoTextCells.Clear();
			columnNumbers.Clear();
			checkColumn = null;

			for (var i = 0; i < Columns.Count; i++)
			{
				var columnIndex = i;
				var binding = new DelegateBinding<ListViewItem, string>
				{
					GetValue = item => GetCellText(item, columnIndex),
					SetValue = (item, value) => SetCellText(item, columnIndex, value)
				};
				var cell = new TextBoxCell { Binding = binding };
				var column = new GridColumn
				{
					HeaderText = Columns[i].Text,
					DataCell = cell,
					AutoSize = false,
					Resizable = true
				};
				var colWidth = Columns[i].Width;
				column.Width = colWidth > 0 ? colWidth : 100;
				column.HeaderTextAlignment = MapTextAlignment(Columns[i].TextAlign);
				cell.TextAlignment = column.HeaderTextAlignment;

				base.Columns.Add(column);
				etoColumns.Add(column);
				etoTextCells.Add(cell);
				columnNumbers[column] = i + 1;
			}

			UpdateCheckColumn();
			ApplyLabelEdit();
			ApplyHeaderStyle();
			ReloadAllRows();
			EnsureResizableColumns();
		}

		/// <summary>Reorders the rows by a column's cells, keeping each row's selection.</summary>
		internal void SortRows(int column, Comparison<string> compare)
		{
			if (Items.Count < 2)
				return;

			using (SelectionPreserver)
				Items.Sort((a, b) => compare(GetCellText(a, column), GetCellText(b, column)));

			ReloadAllRows();
		}

		//The order of the control's own Sort option, by the first column.
		private static int CompareSorted(string x, string y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase);

		private void OnColumnHeaderClickInternal(object sender, GridColumnEventArgs e)
		{
			if (e == null)
				return;

			var columnIndex = base.Columns.IndexOf(e.Column);
			HandleHeaderClick(columnIndex);
		}

		internal void HandleHeaderClick(int baseColumnIndex)
		{
			if (headerStyle == ColumnHeaderStyle.None || headerStyle == ColumnHeaderStyle.Nonclickable)
				return;

			if (baseColumnIndex < 0)
				return;

			if (checkColumn != null)
			{
				if (baseColumnIndex == base.Columns.IndexOf(checkColumn))
					return;
				baseColumnIndex -= 1;
			}

			if (baseColumnIndex < 0)
				return;

			//Sorted first, as AutoHotkey does, so the rows are in order by the time the script hears of the click.
			if (autoSortHeader)
				(this.GetGuiControl() as Gui.ListView)?.SortByHeader(baseColumnIndex);

			ColumnClicked?.Invoke(baseColumnIndex);
		}

		/// <summary>
		/// Adds a row at an index, at the end when the index is -1 or past the end, or where the Sort option keeps it in
		/// order, and returns the index it took.
		/// </summary>
		internal int InsertRow(int index, IReadOnlyList<string> values, bool isChecked, int colStart)
		{
			if (base.Columns.Count == 0 && Columns.Count > 0)
				SyncColumns();

			var item = new ListViewItem { Checked = isChecked };

			for (int i = 0, j = colStart; i < values.Count && j < Columns.Count; i++, j++)
				SetCellText(item, j, values[i]);

			if (sorting != SortOrder.None)
				index = SortedIndex(GetCellText(item, 0));
			else if (index < 0 || index > Items.Count)
				index = Items.Count;

			Items.Insert(index, item);
			return index;
		}

		//Where a row goes among rows in the order of the Sort option: after those whose first column sorts no later.
		private int SortedIndex(string text)
		{
			var descending = sorting == SortOrder.Descending;
			int low = 0, high = Items.Count;

			while (low < high)
			{
				var mid = (low + high) >>> 1;
				var order = CompareSorted(GetCellText(Items[mid], 0), text);

				if (descending ? order >= 0 : order <= 0)
					low = mid + 1;
				else
					high = mid;
			}

			return low;
		}

		public void AutoResizeColumns(ColumnHeaderAutoResizeStyle style)
		{
			if (Items.Count == 0)
				return;

			if (base.Columns.Count == 0 && Columns.Count > 0)
				SyncColumns();

			foreach (var column in etoColumns)
				column.AutoSize = true;

			ReloadAllRows();

			foreach (var column in etoColumns)
			{
				column.AutoSize = false;
				if (column.Width <= 0)
					column.Width = 100;
			}
			for (var i = 0; i < etoColumns.Count && i < Columns.Count; i++)
				Columns[i].Width = etoColumns[i].Width;
			EnsureResizableColumns();
		}

		/// <summary>Starts editing a row's first column, if there is such a row.</summary>
		internal void BeginEditRow(int row)
		{
			if (row >= 0 && row < Items.Count)
				BeginEdit(row, checkColumn != null ? 1 : 0);
		}

		private void UpdateCheckColumn()
		{
			if (checkBoxes)
			{
				if (checkColumn == null)
				{
					checkColumn = new GridColumn
					{
						DataCell = new CheckBoxCell
						{
							Binding = new DelegateBinding<ListViewItem, bool?>
							{
								GetValue = item => item.Checked,
								SetValue = (item, value) => item.Checked = value == true
							}
						},
						AutoSize = false,
						Width = 24,
						Resizable = false
					};
					base.Columns.Insert(0, checkColumn);
				}
			}
			else if (checkColumn != null)
			{
				_ = base.Columns.Remove(checkColumn);
				checkColumn = null;
			}
		}

		private void ApplyHeaderStyle()
		{
			ShowHeader = headerStyle != ColumnHeaderStyle.None;
			var sortable = headerStyle == ColumnHeaderStyle.Clickable;
			foreach (var column in etoColumns)
				column.Sortable = sortable;
		}

		internal void SetColumnWidth(int columnIndex, int width)
		{
			if (columnIndex < 0 || columnIndex >= etoColumns.Count)
				return;
			etoColumns[columnIndex].Width = width;
			if (columnIndex < Columns.Count)
				Columns[columnIndex].Width = width;
		}

		internal void SetColumnAlignment(int columnIndex, Eto.Forms.TextAlignment alignment)
		{
			if (columnIndex < 0 || columnIndex >= etoColumns.Count)
				return;

			etoColumns[columnIndex].HeaderTextAlignment = alignment;
			etoTextCells[columnIndex].TextAlignment = alignment;
			if (columnIndex < Columns.Count)
				Columns[columnIndex].TextAlign = MapHorizontalAlignment(alignment);
		}

		internal void AutoResizeColumn(int columnIndex, bool includeHeader)
		{
			if (columnIndex < 0 || columnIndex >= etoColumns.Count)
				return;

			var column = etoColumns[columnIndex];
			column.AutoSize = true;
			ReloadAllRows();
			column.AutoSize = false;
			if (column.Width <= 0)
				column.Width = 100;

			if (includeHeader)
			{
				var headerWidth = MeasureHeaderTextWidth(column.HeaderText);
				if (column.Width < headerWidth)
					column.Width = headerWidth;
			}

			if (columnIndex < Columns.Count)
				Columns[columnIndex].Width = column.Width;
		}

		private static int MeasureHeaderTextWidth(string text)
		{
			using var label = new Eto.Forms.Label { Text = text ?? "" };
			var size = label.PreferredSize;
			return (int)size.Width + 10;
		}

		private void EnsureResizableColumns()
		{
			foreach (var column in etoColumns)
			{
				column.Resizable = true;
				column.AutoSize = false;
				if (column.Width <= 0)
					column.Width = 100;
			}
		}

		private void ApplyLabelEdit()
		{
			for (var i = 0; i < etoColumns.Count; i++)
				etoColumns[i].Editable = labelEdit && i == 0;
		}

		private static Eto.Forms.TextAlignment MapTextAlignment(WinForms.HorizontalAlignment alignment) =>
			alignment switch
			{
				WinForms.HorizontalAlignment.Center => Eto.Forms.TextAlignment.Center,
				WinForms.HorizontalAlignment.Right => Eto.Forms.TextAlignment.Right,
				_ => Eto.Forms.TextAlignment.Left
			};

		private static WinForms.HorizontalAlignment MapHorizontalAlignment(Eto.Forms.TextAlignment alignment) =>
			alignment switch
			{
				Eto.Forms.TextAlignment.Center => WinForms.HorizontalAlignment.Center,
				Eto.Forms.TextAlignment.Right => WinForms.HorizontalAlignment.Right,
				_ => WinForms.HorizontalAlignment.Left
			};

		internal static string GetCellText(ListViewItem item, int columnIndex) =>
			columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : "";

		internal static void SetCellText(ListViewItem item, int columnIndex, string value)
		{
			var cells = item.SubItems;

			while (cells.Count <= columnIndex)
				cells.Add(new ListViewItem.ListViewSubItem());

			cells[columnIndex].Text = value ?? "";
		}
	}

	public class KeysharpMonthCalendar : Forms.Calendar
	{

		public KeysharpMonthCalendar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpNumericUpDown : NumericStepper
	{
		public double Minimum
		{
			get => MinValue;
			set => MinValue = value;
		}
		public double Maximum
		{
			get => MaxValue;
			set => MaxValue = value;
		}

		public KeysharpNumericUpDown(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class KeysharpPictureBox : ImageView
	{
		private bool scaleHeight;
		private bool scaleWidth;
		private PictureBoxSizeMode sizeMode = PictureBoxSizeMode.Normal;

		public string Filename { get; private set; }
		public PictureBoxSizeMode SizeMode
		{
			get => sizeMode;
			set => sizeMode = value;
		}

		public bool ScaleHeight
		{
			get => scaleHeight;
			set
			{
				scaleHeight = value;
				if (scaleHeight)
					scaleWidth = false;
			}
		}

		public bool ScaleWidth
		{
			get => scaleWidth;
			set
			{
				scaleWidth = value;
				if (scaleWidth)
					scaleHeight = false;
			}
		}

		public KeysharpPictureBox(string filename, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			Filename = filename;
		}
	}

	public class KeysharpProgressBar : Drawable
	{
		private readonly bool customColors = false;
		private readonly int addStyle;
		private int minimum;
		private int maximum = 100;
		private int value;
		private Color barColor;
		private bool hasBarColor;
		private ProgressBarStyle style = ProgressBarStyle.Blocks;

		internal int AddStyle => addStyle;
		public int Minimum
		{
			get => minimum;
			set
			{
				minimum = value;
				if (maximum < minimum)
					maximum = minimum;
				Value = this.value;
			}
		}
		public int Maximum
		{
			get => maximum;
			set
			{
				maximum = value;
				if (minimum > maximum)
					minimum = maximum;
				Value = this.value;
			}
		}
		public int Value
		{
			get => value;
			set
			{
				var clamped = Math.Min(Maximum, Math.Max(Minimum, value));
				if (clamped == this.value)
					return;
				this.value = clamped;
				Invalidate();
			}
		}
		public new ProgressBarStyle Style
		{
			get => style;
			set
			{
				if (style == value)
					return;
				style = value;
				Invalidate();
			}
		}
		public Color BarColor
		{
			get => barColor;
			set
			{
				barColor = value;
				hasBarColor = true;
				Invalidate();
			}
		}

		public KeysharpProgressBar(bool _customColors, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			customColors = _customColors;
			Paint += KeysharpProgressBar_Paint;
		}

		private void KeysharpProgressBar_Paint(object sender, PaintEventArgs e)
		{
			var rect = new Rectangle(0, 0, Width, Height);
			if (rect.Width <= 0 || rect.Height <= 0)
				return;

			var background = BackgroundColor;
			if (background.A <= 0)
				background = SystemColors.ControlBackground;

			var fillColor = hasBarColor ? barColor : SystemColors.Highlight;
			using (var backgroundBrush = new SolidBrush(background))
				e.Graphics.FillRectangle(backgroundBrush, rect);

			var range = Maximum - Minimum;
			var percent = range > 0 ? (double)(value - Minimum) / range : 0.0;
			percent = Math.Min(1.0, Math.Max(0.0, percent));

			const int inset = 1;
			var innerWidth = Math.Max(0, rect.Width - inset * 2);
			var innerHeight = Math.Max(0, rect.Height - inset * 2);

			if ((AddStyle & 0x04) == 0x04)
			{
				var filledHeight = Convert.ToInt32(innerHeight * percent);
				var fillRect = new Rectangle(rect.Left + inset, rect.Bottom - inset - filledHeight, innerWidth, filledHeight);
				DrawFill(e.Graphics, fillRect, fillColor, Style, true);
			}
			else
			{
				var filledWidth = Convert.ToInt32(innerWidth * percent);
				var fillRect = new Rectangle(rect.Left + inset, rect.Top + inset, filledWidth, innerHeight);
				DrawFill(e.Graphics, fillRect, fillColor, Style, false);
			}

			using (var borderPen = new Pen(SystemColors.ControlText))
				e.Graphics.DrawRectangle(borderPen, rect);
		}

		private static void DrawFill(Graphics graphics, Rectangle rect, Color color, ProgressBarStyle style, bool vertical)
		{
			if (rect.Width <= 0 || rect.Height <= 0)
				return;

			using var fillBrush = new SolidBrush(color);

			if (style != ProgressBarStyle.Blocks)
			{
				graphics.FillRectangle(fillBrush, rect);
				return;
			}

			const int block = 6;
			const int gap = 2;

			if (vertical)
			{
				for (var y = rect.Bottom - block; y >= rect.Top; y -= block + gap)
				{
					var h = Math.Min(block, y - rect.Top + block);
					var blockRect = new Rectangle(rect.Left, y, rect.Width, h);
					graphics.FillRectangle(fillBrush, blockRect);
				}
			}
			else
			{
				for (var x = rect.Left; x <= rect.Right - block; x += block + gap)
				{
					var w = Math.Min(block, rect.Right - x);
					var blockRect = new Rectangle(x, rect.Top, w, rect.Height);
					graphics.FillRectangle(fillBrush, blockRect);
				}
			}
		}
	}

	public class KeysharpRadioButton : RadioButton
	{

		public bool AutoSize { get; set; }

		public KeysharpRadioButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}

		public KeysharpRadioButton(RadioButton controller, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
			: base(controller)
		{
		}
	}

	public partial class KeysharpRichEdit : RichTextArea
	{
		private readonly TextNormalizer normalizer;

		private bool numeric;

		internal bool IsNumeric
		{
			get => numeric;
			set => NumberOption.Apply(this, ref numeric, value);
		}

		internal CharacterCasing CharacterCasing
		{
			get => normalizer.Casing;
			set => normalizer.Casing = value;
		}

		public KeysharpRichEdit(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			normalizer = new (this);
			HookSelectionEvents();
		}

		protected override void OnTextChanged(EventArgs e)
		{
			//Eto's text buffer reports a formatting change as a text change, which is no edit.
			if (!IsFormatting)
			{
				Modified = true;
				normalizer.TextChanged();
			}

			base.OnTextChanged(e);
		}
	}

	public class KeysharpStatusStrip : Panel
	{
		internal class StatusStripItemCollection : Collection<KeysharpToolStripStatusLabel>
		{
			//Returns the part, where the WinForms collection the shared code also adds to returns its index.
			public new KeysharpToolStripStatusLabel Add(KeysharpToolStripStatusLabel item)
			{
				base.Add(item);
				return item;
			}
		}

		/// <summary>The widgets of a part: its text in three labels, left, centred and right, after its icon if it has one.</summary>
		private sealed class PartView
		{
			internal readonly Forms.Label Left = NewLabel(Forms.TextAlignment.Left);
			internal readonly Forms.Label Center = NewLabel(Forms.TextAlignment.Center);
			internal readonly Forms.Label Right = NewLabel(Forms.TextAlignment.Right);
			internal readonly TableLayout Text;
			internal readonly Panel Panel;
			internal ImageView Icon;
			internal Font Font;
			internal Color Back, Fore;

			internal PartView()
			{
				Text = new TableLayout
				{
					Padding = Padding.Empty,
					Spacing = new Size(4, 0),
					Rows = { new TableRow(new TableCell(Left, true), new TableCell(Center, true), new TableCell(Right, true)) }
				};
				Panel = new Panel { Content = Text, Padding = new Padding(4, 2) };
			}

			private static Forms.Label NewLabel(Forms.TextAlignment alignment) =>
				new() { TextAlignment = alignment, VerticalAlignment = Forms.VerticalAlignment.Center };
		}

		private readonly StackLayout body = new() { Orientation = Orientation.Horizontal, Spacing = 0, Padding = new Padding(0) };
		private readonly List<PartView> parts = [];

		internal StatusStripItemCollection Items { get; } = [];

		public KeysharpStatusStrip(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			Content = new StackLayout
			{
				Orientation = Orientation.Vertical,
				Spacing = 0,
				Items =
				{
					new StackLayoutItem(new Panel { BackgroundColor = Colors.Gray, Height = 1 }, true),
					new StackLayoutItem(body, true)
				}
			};
		}

		/// <summary>
		/// The 1-based part under the pointer, or 0 for none. AHK reports the clicked part as the
		/// Click/DoubleClick callback's Info, and each part is a child control here.
		/// <para>
		/// Read from the pointer rather than from the event's Location: the parts are the widgets the click is
		/// actually delivered to, so the toolkit reports coordinates relative to the PART, which would place
		/// every click inside whichever part sits at that offset from the strip's own left edge.
		/// </para>
		/// </summary>
		internal long PartFromPoint()
		{
			if (!GetCursorPos(out POINT cursor))
				return 0L;

			var x = cursor.X - this.ScreenOrigin().X;

			if (x < 0)
				return 0L;

			//Measured from the item widths that drive the layout, not from the part controls themselves: the
			//trailing spring part is never assigned a width, so the toolkit reports the width of its text
			//rather than the space it actually fills, which puts its left edge far to the right of where it
			//is drawn.
			float edge = 0;

			for (var i = 0; i < Items.Count; i++)
			{
				var width = Items[i].Width;

				if (width <= 0)//A spring part runs to the end of the bar.
					return i + 1L;

				edge += width;

				if (x < edge)
					return i + 1L;
			}

			return Items.Count;
		}

		/// <summary>
		/// Lays the parts out again after parts were added or removed or their widths changed. A part that remains keeps
		/// its widgets, and a new one is built here.
		/// </summary>
		internal void UpdateItems()
		{
			if (parts.Count > Items.Count)
				parts.RemoveRange(Items.Count, parts.Count - Items.Count);

			body.SuspendLayout();
			body.Items.Clear();

			for (var i = 0; i < Items.Count; i++)
			{
				var item = Items[i];

				if (i == parts.Count)
				{
					parts.Add(new PartView());
					UpdatePart(i);
				}

				var panel = parts[i].Panel;
				panel.Width = item.Width > 0 ? item.Width : -1;
				body.Items.Add(new StackLayoutItem(panel, item.Width <= 0 || item.Spring));
			}

			body.ResumeLayout();
		}

		/// <summary>Shows a part's text, icon and colours in its widgets, setting only what changed.</summary>
		internal void UpdatePart(int index)
		{
			var item = Items[index];
			var view = parts[index];
			var back = item.BackColor.A > 0 ? item.BackColor : BackgroundColor;
			var fore = Properties.Get<Color?>("ForeColor") ?? Colors.Transparent;
			var font = item.Font ?? this.Font;
			var text = (item.Text ?? "").AsSpan();
			var tab = text.IndexOf('\t');
			view.Left.Text = (tab < 0 ? text : text[..tab]).ToString();
			text = tab < 0 ? [] : text[(tab + 1)..];
			tab = text.IndexOf('\t');
			view.Center.Text = (tab < 0 ? text : text[..tab]).ToString();
			text = tab < 0 ? [] : text[(tab + 1)..];
			tab = text.IndexOf('\t');
			view.Right.Text = (tab < 0 ? text : text[..tab]).ToString();

			if (!ReferenceEquals(view.Font, font))
			{
				view.Font = font;
				view.Left.Font = view.Center.Font = view.Right.Font = font;
			}

			//Explicit text colours are copied; the default follows the native theme.
			if (view.Fore != fore && fore.A > 0)
				view.Left.TextColor = view.Center.TextColor = view.Right.TextColor = fore;

			view.Fore = fore;

			if (view.Back != back)
			{
				view.Back = back;
				view.Panel.BackgroundColor = view.Left.BackgroundColor = view.Center.BackgroundColor = view.Right.BackgroundColor = back;

				if (view.Icon != null)
					view.Icon.BackgroundColor = back;
			}

			if (item.Image != null)
			{
				if (view.Icon == null)
				{
					//The text moves into a row after the icon, so it leaves the part first.
					view.Panel.Content = null;
					view.Icon = new ImageView { BackgroundColor = back };
					view.Panel.Content = new StackLayout
					{
						Orientation = Orientation.Horizontal,
						Spacing = 4,
						Padding = new Padding(2, 0),
						Items = { new StackLayoutItem(view.Icon, false), new StackLayoutItem(view.Text, true) }
					};
				}

				//Scaled to the bar's height less its padding, as the bitmap's own size can overflow the bar.
				var iconSize = Math.Max(16, Height - 12);
				view.Icon.Image = item.Image;
				view.Icon.Size = new Size(iconSize, iconSize);
			}
		}
	}

	public class KeysharpTabControl : TabControl
	{
		internal Color? bgcolor;

		public TabAlignment Alignment { get; set; }
		public TabAppearance Appearance { get; set; }
		public bool Multiline { get; set; }
		internal ImageList ImageList { get; set; }

		public KeysharpTabControl(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
#if LINUX
			if (this.ToNative() is Gtk.Notebook notebook)
				notebook.Scrollable = true;
#endif
		}

		internal static string DisplayText(string text)
		{
#if OSX
			// Eto's macOS tab handler displays the raw text, while GTK already converts escaped mnemonics.
			return (text ?? "").Replace("&&", "&");
#else
			// GTK treats a single "&" in a tab label as a mnemonic marker and swallows it, so "Edits & Messages"
			// would show as "Edits  Messages". AHK/WinForms instead show the ampersand literally. Normalize every
			// ampersand (single or already-escaped "&&") to an escaped "&&" so the GTK mnemonic pass collapses it
			// back to a single literal "&" - matching Windows and keeping the text matchable by UseTab/FindTab.
			return (text ?? "").Replace("&&", "&").Replace("&", "&&");
#endif
		}

		internal void AdjustSize(double dpiscale, Size requestedSize)
		{
			if (requestedSize.Width != int.MinValue && requestedSize.Height != int.MinValue)
			{
				Width = requestedSize.Width;
				Height = requestedSize.Height;
				return;
			}

			var tempw = 0.0;
			var temph = 0.0;

			foreach (TabPage tp in Pages)
			{
				(Control right, Control bottom) rb = tp.RightBottomMost();

				if (rb.right != null)
					tempw = Math.Max(tempw, this.TabWidth() + rb.right.Right + (tp.Margin.Right + this.Margin.Right));

				if (rb.bottom != null)
					temph = Math.Max(temph, this.TabHeight() + rb.bottom.Bottom + (tp.Margin.Bottom + (this.Margin.Bottom * dpiscale)));
			}

			var newSize = new Size(
				requestedSize.Width == int.MinValue ? Convert.ToInt32(tempw) : requestedSize.Width,
				requestedSize.Height == int.MinValue ? Convert.ToInt32(temph) : requestedSize.Height);

			this.SetSize(newSize);
		}

		internal void SetColor(Color color)
		{
			bgcolor = color;
		}

		public void SelectTab(TabPage tp)
		{
			SelectedPage = tp;
		}

		public void SelectTab(int i)
		{
			SelectedIndex = i;
		}

		public void SelectTab(string text)
		{
			if (string.IsNullOrEmpty(text))
				return;
			foreach (var page in Pages)
			{
				if (page.Text == text)
				{
					SelectedPage = page;
					break;
				}
			}
		}
	}

	public class KeysharpToolStripStatusLabel
	{
		public bool AutoSize { get; set; }
		public bool Spring { get; set; }
		public int Width { get; set; } = -1;
		// 0 sunken, 1 none, 2 raised
		public int Style { get; set; } = 0;
		public string Name { get; set; } = "";
		public string Text { get; set; } = "";
		public Font Font { get; set; }
		public Color BackColor { get; set; }
		public Bitmap Image { get; set; }

		public KeysharpToolStripStatusLabel(string text = "")
		{
			Text = text;
		}
	}

	public class KeysharpTrackBar : Slider
	{
		public bool inverted = false;

		public int Minimum
		{
			get => MinValue;
			set => MinValue = value;
		}
		public int Maximum
		{
			get => MaxValue;
			set => MaxValue = value;
		}
		public TickStyle TickStyle { get; set; } = TickStyle.Both;
		public int SmallChange { get; set; } = 1;
		public int LargeChange { get; set; } = 10;

		public new int Value
		{
			get => inverted ? Maximum - base.Value + Minimum : base.Value;
			set => base.Value = inverted ? Maximum - value + Minimum : value;
		}

		public KeysharpTrackBar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
		}
	}

	public class TrackBar : KeysharpTrackBar
	{
		public TrackBar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
			: base(_addStyle, _addExStyle, _removeStyle, _removeExStyle)
		{
		}
	}

	public class TreeNode : TreeGridItem
	{
		private static long nextId = 1;
		private readonly long id = Interlocked.Increment(ref nextId);
		private string text = "";

		public TreeNode()
		{
			Nodes = new TreeNodeCollection(this);
			_ = Children;
		}
		public TreeNode(string text) : this()
		{
			Text = text;
		}

		public KeysharpTreeView TreeView { get; internal set; }
		public TreeNodeCollection Nodes { get; }
		public string Name { get; set; } = "";
		public string Text
		{
			get => text;
			set
			{
				text = value ?? "";
				EnsureValues();
				Values[0] = text;
			}
		}
		public bool Checked { get; set; }
		public int ImageIndex { get; set; } = -1;
		public int SelectedImageIndex { get; set; } = -1;
		//The tree sets Expanded when the user expands or collapses the item too.
		public bool IsExpanded => Expanded;
		public Font NodeFont { get; set; }
		public IntPtr Handle => new IntPtr(id);

		private void EnsureValues()
		{
			if (Values == null || Values.Length == 0)
				Values = new object[1];
		}

		public TreeNode NextNode => GetSiblings() is { } siblings && siblings.IndexOf(this) is var i and >= 0 && i + 1 < siblings.Count ? siblings[i + 1] : null;

		public TreeNode PrevNode => GetSiblings() is { } siblings && siblings.IndexOf(this) is var i and > 0 ? siblings[i - 1] : null;

		private TreeNodeCollection GetSiblings()
		{
			if (Parent is TreeNode parentNode)
				return parentNode.Nodes;
			if (TreeView != null)
				return TreeView.Nodes;
			return null;
		}

		public Bitmap Image
		{
			get
			{
				if (TreeView?.ImageList == null || TreeView.ImageList.Images.Count == 0)
					return null;

				if (ImageIndex >= 0)
					return ImageIndex < TreeView.ImageList.Images.Count ? TreeView.ImageList.Images[ImageIndex] : null;

				return TreeView.ImageList.Images[0];
			}
		}

		public void EnsureVisible()
		{
			ExpandParents();
			TreeView?.ScrollIntoView(this);
		}

		//The tree shows an expansion the script makes once it reloads.
		public void Expand()
		{
			if (Nodes.Count == 0 || Expanded)
				return;

			Expanded = true;
			TreeView?.InvalidateModel();
		}

		public void Collapse()
		{
			if (!Expanded)
				return;

			Expanded = false;
			TreeView?.InvalidateModel();
		}

		private void ExpandParents()
		{
			var parent = Parent as TreeNode;
			while (parent != null)
			{
				parent.Expand();
				parent = parent.Parent as TreeNode;
			}
		}

		public void Remove()
		{
			if (Parent is TreeNode parentNode)
				_ = parentNode.Nodes.Remove(this);
			else
				_ = TreeView?.Nodes.Remove(this);
		}
	}

	public class TreeNodeCollection : Collection<TreeNode>
	{
		private KeysharpTreeView treeView;
		private readonly TreeNode parent;
		private TreeGridItemCollection etoItems;

		public TreeNodeCollection()
		{
		}

		public TreeNodeCollection(KeysharpTreeView treeView, TreeNode parent = null, TreeGridItemCollection items = null)
		{
			this.treeView = treeView;
			this.parent = parent;
			etoItems = items ?? parent?.Children ?? treeView?.RootItems;
		}

		public TreeNodeCollection(TreeNode parent)
		{
			this.parent = parent;
			treeView = parent?.TreeView;
			etoItems = parent?.Children;
		}

		protected override void InsertItem(int index, TreeNode item)
		{
			base.InsertItem(index, item);
			item.Parent = parent;
			item.TreeView = treeView ?? parent?.TreeView;
			item.TreeView?.RegisterNode(item);

			if (etoItems != null)
			{
				if (index >= 0 && index < etoItems.Count)
					etoItems.Insert(index, item);
				else
					etoItems.Add(item);
			}

			item.Nodes?.SetOwner(item.TreeView);
		}

		protected override void RemoveItem(int index)
		{
			var item = index >= 0 && index < Count ? this[index] : null;
			item?.TreeView?.UnregisterNode(item);
			base.RemoveItem(index);
			if (item != null && etoItems != null)
				_ = etoItems.Remove(item);
		}

		protected override void ClearItems()
		{
			foreach (var node in this)
				node.TreeView?.UnregisterNode(node);

			base.ClearItems();
			etoItems?.Clear();
		}

		internal void SetOwner(KeysharpTreeView owner)
		{
			if (owner == null)
				return;

			treeView = owner;
			if (parent == null)
			{
				etoItems = owner.RootItems;
				etoItems.Clear();
				foreach (var node in this)
					etoItems.Add(node);
			}

			for (var i = 0; i < Count; i++)
			{
				var node = this[i];
				node.TreeView = owner;
				owner.RegisterNode(node);
				node.Nodes?.SetOwner(owner);
			}
		}

		public TreeNode Add(string text)
		{
			var node = new TreeNode(text);
			Add(node);
			return node;
		}

		public TreeNode Insert(int index, string text)
		{
			var node = new TreeNode(text);
			Insert(index, node);
			return node;
		}

		public TreeNode[] Find(string key, bool searchAllChildren)
		{
			var matches = new List<TreeNode>();
			foreach (var node in this)
				FindNode(node, key, searchAllChildren, matches);
			return matches.ToArray();
		}

		private static void FindNode(TreeNode node, string key, bool searchAllChildren, List<TreeNode> matches)
		{
			if (node == null)
				return;
			if (node.Name == key)
				matches.Add(node);
			if (!searchAllChildren)
				return;
			foreach (var child in node.Nodes)
				FindNode(child, key, true, matches);
		}
	}

	public class KeysharpTreeView : TreeGridView
	{
		private readonly HashSet<TreeNode> expandMarks = [];
		private readonly Dictionary<long, TreeNode> nodesById = [];
		private bool reloadSuspended, modelStale;
		private TreeNode selectedNode;
		private GridColumn checkColumn;
		private GridColumn imageColumn;
		private CheckBoxCell checkCell;
		private ImageViewCell imageCell;
		private TextBoxCell textCell;
		private GridColumn textColumn;

		internal ImageList ImageList
		{
			get => imageList;
			set
			{
				imageList = value;
				UpdateImageColumn();
			}
		}
		internal TreeNode SelectedNode
		{
			get => selectedNode;
			set
			{
				selectedNode = value;

				if (!ReferenceEquals(SelectedItem, value))
				{
					EnsureModel();
					SelectedItem = value;
				}
			}
		}
		internal TreeNode TopNode { get; set; }
		internal TreeNodeCollection Nodes { get; }
		internal TreeGridItemCollection RootItems { get; }
		internal int NodeCount => nodesById.Count;
		public bool CheckBoxes
		{
			get => checkBoxes;
			set
			{
				checkBoxes = value;
				UpdateCheckColumn();
			}
		}
		public bool LabelEdit
		{
			get => labelEdit;
			set
			{
				labelEdit = value;
				if (textColumn != null)
					textColumn.Editable = value;
			}
		}

		private bool checkBoxes;
		private bool labelEdit;
		private ImageList imageList;
		internal GridColumn CheckColumn => checkColumn;
		internal bool HasCheckBoxes => checkBoxes;
		internal int TextColumnIndex => textColumn != null ? Columns.IndexOf(textColumn) : -1;

		public KeysharpTreeView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			RootItems = new TreeGridItemCollection();
			Nodes = new TreeNodeCollection(this, null, RootItems);

			textCell = new TextBoxCell { Binding = Binding.Property<TreeNode, string>(node => node.Text) };
			textColumn = new GridColumn { DataCell = textCell, AutoSize = true };
			Columns.Add(textColumn);
			DataStore = RootItems;
			ShowHeader = false;
			SelectedItemChanged += (_, _) => SelectedNode = SelectedItem as TreeNode;
		}

		internal void BeginLabelEdit(TreeNode node)
		{
			if (node == null || !LabelEdit)
				return;

			EnsureModel();
			var row = GetVisibleRowIndex(node);
			if (row < 0)
				return;

			var column = TextColumnIndex;
			if (column < 0)
				column = 0;

			BeginEdit(row, column);
		}

		private int GetVisibleRowIndex(TreeNode node)
		{
			var index = 0;
			return TryFindVisibleRow(Nodes, node, ref index) ? index : -1;
		}

		internal void ScrollIntoView(TreeNode node)
		{
			if (node == null)
				return;

			EnsureModel();
			var rowIndex = GetVisibleRowIndex(node);

			if (rowIndex >= 0)
				Application.Instance.AsyncInvoke(() =>
					ScrollToRow(rowIndex));

		}

		private static bool TryFindVisibleRow(TreeNodeCollection nodes, TreeNode target, ref int index)
		{
			foreach (var node in nodes)
			{
				if (node == target)
					return true;

				index++;
				if (node.Expanded && node.Nodes?.Count > 0)
				{
					if (TryFindVisibleRow(node.Nodes, target, ref index))
						return true;
				}
			}

			return false;
		}

		/// <summary>The node under a point of the control, or null.</summary>
		internal TreeNode NodeAt(PointF location)
		{
			EnsureModel();
			return GetCellAt(location)?.Item as TreeNode;
		}

		internal void DelayedExpandParent(TreeNode node)
		{
			var parent = node.Parent as TreeNode ?? node;

			if (parent.Nodes.Count > 0 && expandMarks.Remove(parent))
				parent.Expand();
		}

		internal void MarkForExpansion(TreeNode node) => expandMarks.Add(node);

		internal void RemoveMarkForExpansion(TreeNode node) => _ = expandMarks.Remove(node);

		internal void ClearMarksForExpansion() => expandMarks.Clear();

		internal TreeNode FindNode(long id) => nodesById.TryGetValue(id, out var node) ? node : null;

		internal void RegisterNode(TreeNode node)
		{
			if (node != null)
				nodesById[node.Handle.ToInt64()] = node;
		}

		internal void UnregisterNode(TreeNode node)
		{
			if (node == null)
				return;

			_ = nodesById.Remove(node.Handle.ToInt64());
			foreach (var child in node.Nodes)
				UnregisterNode(child);
		}

		/// <summary>
		/// Records a change GTK sees only through a reload of the whole model: an item added or removed below the top
		/// level, a sort, or an expansion the script makes. The changes of one turn of the message loop, or of the time
		/// -Redraw holds them, share one reload, and anything that maps a node to a row of the view reloads first.
		/// </summary>
		internal void InvalidateModel()
		{
			if (modelStale)
				return;

			modelStale = true;

			if (!reloadSuspended)
				Application.Instance.AsyncInvoke(() =>
				{
					if (!reloadSuspended && !IsDisposed)
						EnsureModel();
				});
		}

		/// <summary>Reloads the model if a change is waiting for it, so that the view's rows match the nodes.</summary>
		internal void EnsureModel()
		{
			if (modelStale)
			{
				modelStale = false;
				ReloadData();
			}
		}

		/// <summary>
		/// Shows a change to one item's text, check mark, font or icon, which GTK reads from the item only when told. A
		/// reload that is waiting shows it anyway.
		/// </summary>
		internal void ShowItemChange(TreeNode node)
		{
			if (reloadSuspended || modelStale)
				InvalidateModel();
			else
				ReloadItem(node, false);
		}

		internal void SuspendReload() => reloadSuspended = true;

		internal void ResumeReload()
		{
			reloadSuspended = false;
			EnsureModel();
		}

		internal void SelectNode(TreeNode node, bool ensureVisible)
		{
			if (node == null)
				return;

			var parent = node.Parent as TreeNode;
			while (parent != null)
			{
				parent.Expand();
				parent = parent.Parent as TreeNode;
			}

			SelectedNode = node;
			if (ensureVisible)
				node.EnsureVisible();
		}

		private void UpdateCheckColumn()
		{
			if (checkBoxes)
			{
				if (checkColumn == null)
				{
					checkCell = new CheckBoxCell
					{
						Binding = Binding.Property<TreeNode, bool?>(node => node.Checked)
					};
					checkColumn = new GridColumn
					{
						DataCell = checkCell,
						AutoSize = true
					};
					Columns.Insert(0, checkColumn);
				}
			}
			else if (checkColumn != null)
			{
				_ = Columns.Remove(checkColumn);
				checkColumn = null;
				checkCell = null;
			}
		}

		private void UpdateImageColumn()
		{
			if (imageList != null && imageList.Images.Count > 0)
			{
				if (imageColumn == null)
				{
					imageCell = new ImageViewCell
					{
						Binding = (IIndirectBinding<Image>)Binding.Property<TreeNode, Bitmap>(node => node.Image)
					};
					imageColumn = new GridColumn
					{
						DataCell = imageCell,
						AutoSize = true
					};
					var insertAt = checkColumn != null ? 1 : 0;
					Columns.Insert(insertAt, imageColumn);
				}
			}
			else if (imageColumn != null)
			{
				_ = Columns.Remove(imageColumn);
				imageColumn = null;
				imageCell = null;
			}
		}
	}

	public static class TreeNodeCollectionExtensions
	{
		//One level, as the native TreeView sorts, by the case-insensitive order of the user's locale.
		internal static void SortByText(this TreeNodeCollection nodes)
		{
			var ordered = nodes.OrderBy(node => node.Text, StringComparer.CurrentCultureIgnoreCase).ToList();
			nodes.Clear();

			foreach (var node in ordered)
				nodes.Add(node);
		}
	}

	/// <summary>
	/// The backing control for <see cref="Gui.WebView"/> off Windows: WebKitGTK on Linux, WKWebView on macOS,
	/// both through Eto. Eto's own WebView already carries most of what <see cref="IWebViewBackend"/> asks for,
	/// so this mostly translates its events and answers the two members which would otherwise throw.
	/// </summary>
	public class KeysharpWebView : WebView, IWebViewBackend
	{
		private IWebViewEventSink sink;

		public KeysharpWebView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			//Eto leaves this false, which suppresses the page's own context menu. Windows leaves it on, so
			//turn it on here too and let BrowserContextMenuEnabled be the one place the choice is made.
			BrowserContextMenuEnabled = true;
		}

		Uri IWebViewBackend.Url
		{
			get
			{
				try
				{
					return Url;
				}
				catch (Exception)//The GTK handler builds its Uri from a pointer that is null until something loads.
				{
					return null;
				}
			}
			set => Url = value;
		}

		string IWebViewBackend.DocumentTitle => DocumentTitle ?? "";

		bool IWebViewBackend.CanGoBack => CanGoBack;

		bool IWebViewBackend.CanGoForward => CanGoForward;

		bool IWebViewBackend.BrowserContextMenuEnabled
		{
			get => BrowserContextMenuEnabled;
			set => BrowserContextMenuEnabled = value;
		}

		void IWebViewBackend.GoBack() => GoBack();

		void IWebViewBackend.GoForward() => GoForward();

		void IWebViewBackend.Stop() => Stop();

		void IWebViewBackend.Reload() => Reload();

		void IWebViewBackend.ShowPrintDialog() => ShowPrintDialog();

		string IWebViewBackend.ExecuteScript(string script) => ExecuteScript(script) ?? "";

		Task<string> IWebViewBackend.ExecuteScriptAsync(string script) => ExecuteScriptAsync(script);

		void IWebViewBackend.LoadHtml(string html, Uri baseUri) => LoadHtml(html, baseUri);

		void IWebViewBackend.AttachEvent(string e, IWebViewEventSink eventSink)
		{
			sink = eventSink;

			switch (e)
			{
				case "navigated":
					Navigated += (_, args) => sink.Navigated(args.Uri?.ToString() ?? "");
					break;

				case "documentloaded":
					DocumentLoaded += (_, args) => sink.DocumentLoaded(args.Uri?.ToString() ?? "");
					break;

				case "documentloading":
					DocumentLoading += (_, args) => args.Cancel = sink.DocumentLoading(args.Uri?.ToString() ?? "", args.IsMainFrame);
					break;

				case "opennewwindow":
					OpenNewWindow += (_, args) => args.Cancel = sink.OpenNewWindow(args.Uri?.ToString() ?? "", args.NewWindowName ?? "");
					break;

				case "documenttitlechanged":
					DocumentTitleChanged += (_, args) => sink.DocumentTitleChanged(args.Title ?? "");
					break;

				case "messagereceived":
					MessageReceived += (_, args) => sink.MessageReceived(args.Message ?? "");
					break;
			}
		}
	}
}
#endif
