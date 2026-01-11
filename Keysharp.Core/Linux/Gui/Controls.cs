#if !WINDOWS
using System.Diagnostics;
using WinForms = System.Windows.Forms;

namespace Keysharp.Core
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
		HeaderSize
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

	internal static class TextCasingHelper
	{
		internal static string ApplyCharacterCasing(CharacterCasing casing, string value)
		{
			return casing switch
			{
				CharacterCasing.Upper => value.ToUpperInvariant(),
				CharacterCasing.Lower => value.ToLowerInvariant(),
				_ => value
			};
		}

		internal static string ApplyCharacterCasingDelta(CharacterCasing casing, string previous, string current)
		{
			if (casing == CharacterCasing.Normal)
				return current;

			previous ??= "";
			current ??= "";

			var prefix = 0;
			var maxPrefix = Math.Min(previous.Length, current.Length);
			while (prefix < maxPrefix && previous[prefix] == current[prefix])
				prefix++;

			var suffix = 0;
			var maxSuffix = Math.Min(previous.Length - prefix, current.Length - prefix);
			while (suffix < maxSuffix
				&& previous[previous.Length - 1 - suffix] == current[current.Length - 1 - suffix])
				suffix++;

			var insertedLength = current.Length - prefix - suffix;
			if (insertedLength <= 0)
				return current;

			var inserted = current.Substring(prefix, insertedLength);
			var adjusted = ApplyCharacterCasing(casing, inserted);

			if (string.Equals(inserted, adjusted, StringComparison.Ordinal))
				return current;

			return string.Concat(current.AsSpan(0, prefix), adjusted, current.AsSpan(current.Length - suffix));
		}

		internal static int GetCaretIndex(object control)
		{
			var prop = control.GetType().GetProperty("CaretIndex");
			return prop?.GetValue(control) is int i ? i : 0;
		}

		internal static void SetCaretIndex(object control, int index)
		{
			var prop = control.GetType().GetProperty("CaretIndex");
			prop?.SetValue(control, index);
		}
	}

	internal sealed class TextNormalizeState
	{
		private bool suppressTextNormalize;
		private bool pendingTextNormalize;
		private string lastCasedText = "";

		internal void Reset(string text)
		{
			lastCasedText = text ?? "";
		}

		internal void HandleTextChanged(
			Func<string> getText,
			Action<string> setText,
			Func<CharacterCasing> getCasing,
			Func<bool> isNumeric,
			Func<int> getCaretIndex,
			Action<int> setCaretIndex,
			Action<Action> schedule)
		{
			if (suppressTextNormalize)
				return;

			var casing = getCasing();
			var numeric = isNumeric();

			if (!numeric && casing == CharacterCasing.Normal)
			{
				lastCasedText = getText() ?? "";
				return;
			}

			if (pendingTextNormalize)
				return;

			pendingTextNormalize = true;

			void Normalize()
			{
				pendingTextNormalize = false;
				var current = getText() ?? "";
				var adjusted = current;

				if (numeric)
				{
					if (current.Any(ch => !char.IsDigit(ch)))
						adjusted = string.Join("", current.Where(ch => char.IsDigit(ch)));
				}
				else
				{
					adjusted = TextCasingHelper.ApplyCharacterCasingDelta(casing, lastCasedText, current);
				}

				if (!string.Equals(current, adjusted, StringComparison.Ordinal))
				{
					suppressTextNormalize = true;
					var caret = getCaretIndex();
					setText(adjusted);
					setCaretIndex(Math.Min(caret, adjusted.Length));
					suppressTextNormalize = false;
				}

				lastCasedText = adjusted;
			}

			if (schedule != null)
				schedule(Normalize);
			else
				Normalize();
		}
	}

	public enum ScrollBars
	{
		None,
		Horizontal,
		Vertical,
		Both
	}

	public enum RichTextBoxScrollBars
	{
		None,
		Horizontal,
		Vertical,
		Both,
		ForcedHorizontal,
		ForcedVertical,
		ForcedBoth
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

	public class KeysharpButton : Button
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		public bool AutoSize { get; set; }

		public KeysharpButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpCheckBox : CheckBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public CheckState CheckState { get; set; } = CheckState.Unchecked;
		public ContentAlignment CheckAlign { get; set; } = ContentAlignment.MiddleLeft;

		public KeysharpCheckBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpComboBox : ComboBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDown;
		public new ObservableCollection<object> Items { get; } = new ObservableCollection<object>();
		public object SelectedItem { get; set; }
		public bool DroppedDown { get; set; }
		public int MaxDropDownItems { get; set; }
		public bool Sorted { get; set; }
		public bool IntegralHeight { get; set; }
		public int DropDownHeight { get; set; }

		public KeysharpComboBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			ItemTextBinding = Binding.Delegate<object, string>(item => item?.ToString());
			DataStore = Items;
		}

		public void ResetText()
		{
			Text = "";
		}
	}

	public class KeysharpDateTimePicker : DateTimePicker
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public KeysharpDateTimePicker(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpCustomControl : Control
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly string className;

		public KeysharpCustomControl(string _className, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			className = _className;
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpTextBox : TextBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly TextNormalizeState normalizeState = new TextNormalizeState();
		private CharacterCasing characterCasing = CharacterCasing.Normal;

		internal bool IsNumeric { get; set; }
		public bool AcceptsTab { get; set; }
		public bool AcceptsReturn { get; set; }
		public bool Multiline { get; set; }
		public bool WordWrap { get; set; }
		public char PasswordChar { get; set; }
		public bool UseSystemPasswordChar { get; set; }
		public CharacterCasing CharacterCasing
		{
			get => characterCasing;
			set
			{
				characterCasing = value;
				normalizeState.Reset(Text ?? "");
			}
		}
		public ScrollBars ScrollBars { get; set; } = ScrollBars.None;

		public KeysharpTextBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			normalizeState.Reset(Text ?? "");
			TextChanged += KeysharpEdit_TextChanged;
		}

		private void KeysharpEdit_TextChanged(object sender, EventArgs e)
		{
			normalizeState.HandleTextChanged(
				() => Text,
				value => Text = value,
				() => CharacterCasing,
				() => IsNumeric,
				() => TextCasingHelper.GetCaretIndex(this),
				index => TextCasingHelper.SetCaretIndex(this, index),
				null);
		}
	}

	public class KeysharpPasswordBox : PasswordBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly TextNormalizeState normalizeState = new TextNormalizeState();
		private bool useSystemPasswordChar;
		private CharacterCasing characterCasing = CharacterCasing.Normal;

		internal bool IsNumeric { get; set; }
		public bool AcceptsTab { get; set; }
		public bool AcceptsReturn { get; set; }
		public bool Multiline { get; set; }
		public bool WordWrap { get; set; }
		public bool UseSystemPasswordChar
		{
			get => useSystemPasswordChar;
			set
			{
				useSystemPasswordChar = value;
				if (value)
					PasswordChar = '\0';
			}
		}
		public CharacterCasing CharacterCasing
		{
			get => characterCasing;
			set
			{
				characterCasing = value;
				normalizeState.Reset(Text ?? "");
			}
		}
		public ScrollBars ScrollBars { get; set; } = ScrollBars.None;

		public KeysharpPasswordBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			normalizeState.Reset(Text ?? "");
			TextChanged += KeysharpEdit_TextChanged;
		}

		private void KeysharpEdit_TextChanged(object sender, EventArgs e)
		{
			normalizeState.HandleTextChanged(
				() => Text,
				value => Text = value,
				() => CharacterCasing,
				() => IsNumeric,
				() => TextCasingHelper.GetCaretIndex(this),
				index => TextCasingHelper.SetCaretIndex(this, index),
				action => Application.Instance.InvokeAsync(action));
		}
	}

	public class KeysharpTextArea : TextArea
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly TextNormalizeState normalizeState = new TextNormalizeState();
		private CharacterCasing characterCasing = CharacterCasing.Normal;

		internal bool IsNumeric { get; set; }
		public bool Multiline { get; set; }
		public int MaxLength = -1;
		public bool WordWrap {
			get => Wrap;
			set => Wrap = value;
		}
		public char PasswordChar { get; set; }
		public bool UseSystemPasswordChar { get; set; }
		public CharacterCasing CharacterCasing
		{
			get => characterCasing;
			set
			{
				characterCasing = value;
				normalizeState.Reset(Text ?? "");
			}
		}
		public ScrollBars ScrollBars { get; set; } = ScrollBars.None;

		public KeysharpTextArea(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			normalizeState.Reset(Text ?? "");
			TextChanged += KeysharpEdit_TextChanged;
		}

		private void KeysharpEdit_TextChanged(object sender, EventArgs e)
		{
			normalizeState.HandleTextChanged(
				() => Text,
				value => Text = value,
				() => CharacterCasing,
				() => IsNumeric,
				() => TextCasingHelper.GetCaretIndex(this),
				index => TextCasingHelper.SetCaretIndex(this, index),
				action => Application.Instance.InvokeAsync(action));
		}
	}

	public class KeysharpGroupBox : GroupBox
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public KeysharpGroupBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpLabel : Forms.Label
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public bool AutoSize { get; set; }

		public KeysharpLabel(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpLinkLabel : KeysharpLabel
	{
		internal bool clickSet = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public KeysharpLinkLabel(string text, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			Text = text;
			MouseDown += KeysharpLinkLabel_MouseDown;
		}

		internal static object OnLinkLabelClicked(object obj0, object obj1)
		{
			if (obj0 is KeysharpLinkLabel ll)
				OpenUrl(ll.Text);
			return null;
		}

		private void KeysharpLinkLabel_MouseDown(object sender, MouseEventArgs e) => OpenUrl(Text);

		private static void OpenUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return;

			if (!url.Contains("://"))
				url = "https://" + url;

			var proc = new Process
			{
				EnableRaisingEvents = false,
				StartInfo = new ProcessStartInfo("xdg-open", url)
				{
					UseShellExecute = true
				}
			};
			proc.Start();
		}
	}

	public class KeysharpListBox : ListBox
	{
		public class TabOffsetCollection : Collection<int>
		{
			public void AddRange(IEnumerable<int> values)
			{
				if (values == null)
					return;
				foreach (var value in values)
					Add(value);
			}
		}

		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public SelectionMode SelectionMode { get; set; } = SelectionMode.One;
		public ObservableCollection<object> Items { get; } = new ObservableCollection<object>();
		public IList<int> SelectedIndices { get; } = new List<int>();
		public IList<object> SelectedItems { get; } = new List<object>();
		public int ItemHeight { get; set; } = 16;
		public bool ScrollAlwaysVisible { get; set; }
		public bool HorizontalScrollbar { get; set; }
		public int HorizontalExtent { get; set; }
		public bool Sorted { get; set; }
		public bool IntegralHeight { get; set; }
		public bool UseCustomTabOffsets { get; set; }
		public TabOffsetCollection CustomTabOffsets { get; } = new TabOffsetCollection();

		public object SelectedItem
		{
			get => SelectedItems.Count > 0 ? SelectedItems[0] : null;
			set
			{
				SelectedItems.Clear();
				SelectedIndices.Clear();
				if (value != null)
				{
					SelectedItems.Add(value);
					var index = Items.IndexOf(value);
					if (index >= 0)
						SelectedIndices.Add(index);
				}
			}
		}

		public KeysharpListBox(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			ItemTextBinding = Binding.Delegate<object, string>(item => item?.ToString());
			DataStore = Items;
		}

		public void SetSelected(int index, bool value)
		{
			if (index < 0 || index >= Items.Count)
				return;

			if (value)
			{
				if (!SelectedIndices.Contains(index))
					SelectedIndices.Add(index);
				var item = Items[index];
				if (!SelectedItems.Contains(item))
					SelectedItems.Add(item);
			}
			else
			{
				_ = SelectedIndices.Remove(index);
				_ = SelectedItems.Remove(Items[index]);
			}
		}

		public void ClearSelected()
		{
			SelectedItems.Clear();
			SelectedIndices.Clear();
			SelectedIndex = -1;
		}
	}

	public class KeysharpListView : GridView
	{
		private enum ListViewSortMode
		{
			Text,
			Integer,
			Float
		}

	internal event Action<int> ColumnClicked;

	public class ListViewItem
	{
		public class ListViewSubItem
		{
			public string Text { get; set; } = "";
		}

		public class ListViewSubItemCollection : Collection<ListViewSubItem>
		{
		}

		public int Index { get; internal set; }
		internal KeysharpListView Owner { get; set; }
		public bool Checked { get; set; }
		public bool Selected { get; set; }
		public bool Focused { get; set; }
		public string Text { get; set; } = "";
		public ListViewSubItemCollection SubItems { get; } = new ListViewSubItemCollection();

		public void BeginEdit()
		{
			Owner?.BeginEditItem(this);
		}
	}

	public class ListViewItemCollection : Collection<ListViewItem>
	{
		protected override void InsertItem(int index, ListViewItem item)
		{
			base.InsertItem(index, item);
			RefreshIndices();
		}

		protected override void SetItem(int index, ListViewItem item)
		{
			base.SetItem(index, item);
			RefreshIndices();
		}

		protected override void RemoveItem(int index)
		{
			base.RemoveItem(index);
			RefreshIndices();
		}

		private void RefreshIndices()
		{
			for (var i = 0; i < Count; i++)
				this[i].Index = i;
		}
	}

	public ListViewItemCollection Items { get; } = new ListViewItemCollection();
	public ListViewItemCollection SelectedItems { get; } = new ListViewItemCollection();
	public IList<int> SelectedIndices { get; } = new List<int>();
	public ListViewItem FocusedItem { get; set; }
		public new WinForms.ColumnHeaderCollection Columns { get; } = new WinForms.ColumnHeaderCollection();
		public bool CheckBoxes
		{
			get => checkBoxes;
			set
			{
				checkBoxes = value;
				UpdateCheckColumn();
				RefreshDataStore();
			}
		}
		public bool GridLines
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
		internal bool AllowF2Edit { get; set; } = true;
		public View View
		{
			get => view;
			set
			{
				if (value != View.Details)
					throw new NotImplementedException("ListView view modes other than Report are not implemented on Linux.");

				view = value;
				ApplyView();
			}
		}
		public SortOrder Sorting
		{
			get => sorting;
			set
			{
				sorting = value;
				if (sorting == SortOrder.Ascending)
					SortByColumn(0, false);
				else if (sorting == SortOrder.Descending)
					SortByColumn(0, true);
			}
		}
		public bool MultiSelect
		{
			get => multiSelect;
			set
			{
				multiSelect = value;
				AllowMultipleSelection = value;
			}
		}
		public bool AllowColumnReorder
		{
			get => allowColumnReorder;
			set
			{
				allowColumnReorder = value;
				Reflections.SafeSetProperty(this, "AllowColumnReordering", value);
			}
		}
		public bool FullRowSelect { get; set; }
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
		internal ImageList ImageList { get; set; }

		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly List<GridColumn> etoColumns = [];
		private readonly List<TextBoxCell> etoTextCells = [];
		private readonly List<ListViewItem> etoItems = [];
		private readonly Dictionary<int, ListViewSortMode> columnSortModes = [];
		private readonly Dictionary<int, bool> columnSortDescending = [];
		private readonly HashSet<int> columnSortDefaultDescending = [];
		private readonly HashSet<int> columnNoSort = [];
		private bool multiSelect;
		private bool checkBoxes;
		private bool gridLines;
		private SortOrder sorting;
		private bool labelEdit;
		private bool allowColumnReorder;
		private ColumnHeaderStyle headerStyle = ColumnHeaderStyle.Clickable;
		private bool autoSortHeader = true;
		private GridColumn checkColumn;
		private CheckBoxCell checkCell;
		private View view = View.Details;

	internal GridColumn CheckColumn => checkColumn;
	internal bool HasCheckBoxes => checkBoxes;

		public KeysharpListView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			DataStore = Items;
			ShowHeader = true;
			SelectedRowsChanged += (_, _) => SyncSelection();
			ColumnHeaderClick += OnColumnHeaderClickInternal;
		}

		internal void SyncColumns()
		{
			base.Columns.Clear();
			etoColumns.Clear();
			etoTextCells.Clear();
			checkColumn = null;
			checkCell = null;

			foreach (var item in Items)
				EnsureSubItems(item, Columns.Count);

			for (var i = 0; i < Columns.Count; i++)
			{
				if (!columnSortModes.ContainsKey(i))
					columnSortModes[i] = ListViewSortMode.Text;

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
			}

			UpdateCheckColumn();
			ApplyLabelEdit();
			ApplyHeaderStyle();
			RefreshDataStore();
			EnsureResizableColumns();
		}

		internal void SetColumnSortMode(int columnIndex, string mode)
		{
			if (columnIndex < 0)
				return;

			if (string.Equals(mode, "Integer", StringComparison.OrdinalIgnoreCase))
				columnSortModes[columnIndex] = ListViewSortMode.Integer;
			else if (string.Equals(mode, "Float", StringComparison.OrdinalIgnoreCase))
				columnSortModes[columnIndex] = ListViewSortMode.Float;
			else
				columnSortModes[columnIndex] = ListViewSortMode.Text;
		}

		internal void SetColumnNoSort(int columnIndex, bool noSort)
		{
			if (columnIndex < 0)
				return;

			if (noSort)
				columnNoSort.Add(columnIndex);
			else
				columnNoSort.Remove(columnIndex);
		}

		internal void SetColumnSortDefaultDescending(int columnIndex, bool desc)
		{
			if (columnIndex < 0)
				return;

			if (desc)
				columnSortDefaultDescending.Add(columnIndex);
			else
				columnSortDefaultDescending.Remove(columnIndex);
		}

		internal void SortByColumn(int columnIndex, bool descending)
		{
			if (columnIndex < 0 || columnIndex >= Columns.Count)
				return;

			var mode = columnSortModes.TryGetValue(columnIndex, out var m) ? m : ListViewSortMode.Text;
			var list = Items.ToList();
			list.Sort((a, b) => CompareItems(a, b, columnIndex, mode));
			if (descending)
				list.Reverse();

			Items.Clear();
			foreach (var item in list)
				Items.Add(item);

			columnSortDescending[columnIndex] = descending;
			RefreshDataStore();
		}

		private static int CompareItems(ListViewItem a, ListViewItem b, int columnIndex, ListViewSortMode mode)
		{
			var left = GetCellText(a, columnIndex);
			var right = GetCellText(b, columnIndex);

			if (mode == ListViewSortMode.Integer)
			{
				if (long.TryParse(left, out var li) && long.TryParse(right, out var ri))
					return li.CompareTo(ri);
			}
			else if (mode == ListViewSortMode.Float)
			{
				if (double.TryParse(left, out var ld) && double.TryParse(right, out var rd))
					return ld.CompareTo(rd);
			}

			return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
		}

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

			ColumnClicked?.Invoke(baseColumnIndex);
			if (columnNoSort.Contains(baseColumnIndex))
				return;

			if (autoSortHeader)
			{
				var desc = columnSortDescending.TryGetValue(baseColumnIndex, out var last)
					? !last
					: columnSortDefaultDescending.Contains(baseColumnIndex);
				columnSortDescending[baseColumnIndex] = desc;
				SortByColumn(baseColumnIndex, desc);
			}
		}

		internal void InsertColumnState(int index)
		{
			if (index < 0)
				index = 0;

			var updatedModes = new Dictionary<int, ListViewSortMode>();
			foreach (var pair in columnSortModes)
				updatedModes[pair.Key >= index ? pair.Key + 1 : pair.Key] = pair.Value;
			columnSortModes.Clear();
			foreach (var pair in updatedModes)
				columnSortModes[pair.Key] = pair.Value;

			var updatedDescending = new Dictionary<int, bool>();
			foreach (var pair in columnSortDescending)
				updatedDescending[pair.Key >= index ? pair.Key + 1 : pair.Key] = pair.Value;
			columnSortDescending.Clear();
			foreach (var pair in updatedDescending)
				columnSortDescending[pair.Key] = pair.Value;

			var updatedDefaultDesc = new HashSet<int>();
			foreach (var value in columnSortDefaultDescending)
				updatedDefaultDesc.Add(value >= index ? value + 1 : value);
			columnSortDefaultDescending.Clear();
			foreach (var value in updatedDefaultDesc)
				columnSortDefaultDescending.Add(value);

			var updatedNoSort = new HashSet<int>();
			foreach (var value in columnNoSort)
				updatedNoSort.Add(value >= index ? value + 1 : value);
			columnNoSort.Clear();
			foreach (var value in updatedNoSort)
				columnNoSort.Add(value);
		}

		internal void RemoveColumnState(int index)
		{
			if (index < 0)
				return;

			columnSortModes.Remove(index);
			columnSortDescending.Remove(index);
			_ = columnSortDefaultDescending.Remove(index);
			_ = columnNoSort.Remove(index);

			var updatedModes = new Dictionary<int, ListViewSortMode>();
			foreach (var pair in columnSortModes)
				updatedModes[pair.Key > index ? pair.Key - 1 : pair.Key] = pair.Value;
			columnSortModes.Clear();
			foreach (var pair in updatedModes)
				columnSortModes[pair.Key] = pair.Value;

			var updatedDescending = new Dictionary<int, bool>();
			foreach (var pair in columnSortDescending)
				updatedDescending[pair.Key > index ? pair.Key - 1 : pair.Key] = pair.Value;
			columnSortDescending.Clear();
			foreach (var pair in updatedDescending)
				columnSortDescending[pair.Key] = pair.Value;

			var updatedDefaultDesc = new HashSet<int>();
			foreach (var value in columnSortDefaultDescending)
				updatedDefaultDesc.Add(value > index ? value - 1 : value);
			columnSortDefaultDescending.Clear();
			foreach (var value in updatedDefaultDesc)
				columnSortDefaultDescending.Add(value);

			var updatedNoSort = new HashSet<int>();
			foreach (var value in columnNoSort)
				updatedNoSort.Add(value > index ? value - 1 : value);
			columnNoSort.Clear();
			foreach (var value in updatedNoSort)
				columnNoSort.Add(value);
		}

	internal int AddRow(IReadOnlyList<string> values, bool isChecked = false, int colStart = 0)
	{
			if (base.Columns.Count == 0 && Columns.Count > 0)
				SyncColumns();

		var item = new ListViewItem();
		item.Owner = this;
		EnsureSubItems(item, Columns.Count);

			for (int i = 0, j = colStart; i < values.Count && j < Columns.Count; i++, j++)
				SetCellText(item, j, values[i]);

			item.Checked = isChecked;
			Items.Add(item);
			if (sorting == SortOrder.Ascending)
				SortByColumn(0, false);
			else if (sorting == SortOrder.Descending)
				SortByColumn(0, true);
			RefreshDataStore();
			return Items.Count;
		}

	internal int InsertRow(int index, IReadOnlyList<string> values, bool isChecked = false, int colStart = 0)
	{
			if (base.Columns.Count == 0 && Columns.Count > 0)
				SyncColumns();

		var item = new ListViewItem();
		item.Owner = this;
		EnsureSubItems(item, Columns.Count);

			for (int i = 0, j = colStart; i < values.Count && j < Columns.Count; i++, j++)
				SetCellText(item, j, values[i]);

			item.Checked = isChecked;
			if (index < 0 || index >= Items.Count)
				Items.Add(item);
			else
				Items.Insert(index, item);

			if (sorting == SortOrder.Ascending)
				SortByColumn(0, false);
			else if (sorting == SortOrder.Descending)
				SortByColumn(0, true);

			RefreshDataStore();
			return index < 0 || index >= Items.Count ? Items.Count : index + 1;
		}

		public void AutoResizeColumns(ColumnHeaderAutoResizeStyle style)
		{
			if (Items.Count == 0)
				return;
				
			if (base.Columns.Count == 0 && Columns.Count > 0)
				SyncColumns();

			foreach (var column in etoColumns)
				column.AutoSize = true;

			RefreshDataStore();

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

	internal void RefreshDataStore()
	{
		etoItems.Clear();
		etoItems.AddRange(Items);
		foreach (var item in etoItems)
			item.Owner = this;
		DataStore = etoItems;
		ReloadData(Enumerable.Range(0, etoItems.Count));
	}

	internal void BeginEditItem(ListViewItem item)
	{
		var row = Items.IndexOf(item);
		if (row < 0)
			return;

		var column = checkColumn != null ? 1 : 0;
		BeginEdit(row, column);
	}

	private void UpdateCheckColumn()
	{
		if (checkBoxes)
		{
			if (checkColumn == null)
			{
				checkCell = new CheckBoxCell
				{
					Binding = new DelegateBinding<ListViewItem, bool?>
					{
						GetValue = item => item.Checked,
						SetValue = (item, value) => item.Checked = value == true
					}
				};
				Reflections.SafeSetProperty(checkCell, "Editable", true);
				checkColumn = new GridColumn
				{
					DataCell = checkCell,
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
			checkCell = null;
		}
	}

		private void ApplyHeaderStyle()
		{
			var isDetails = view == View.Details;
			ShowHeader = isDetails && headerStyle != ColumnHeaderStyle.None;
			var sortable = isDetails && headerStyle == ColumnHeaderStyle.Clickable;
			foreach (var column in etoColumns)
				column.Sortable = sortable;
		}

		private void ApplyView()
		{
			var isDetails = view == View.Details;
			ApplyHeaderStyle();

			if (etoColumns.Count > 0)
			{
				for (var i = 0; i < etoColumns.Count; i++)
				{
					var visible = isDetails || i == 0;
					Reflections.SafeSetProperty(etoColumns[i], "Visible", visible);
					if (!visible)
						etoColumns[i].Width = 0;
				}
			}

			EnsureResizableColumns();
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
		RefreshDataStore();
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
		var label = new Eto.Forms.Label { Text = text ?? "" };
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
		for (var i = 0; i < etoTextCells.Count; i++)
		{
			var editable = labelEdit && i == 0;
			Reflections.SafeSetProperty(etoTextCells[i], "Editable", editable);
			Reflections.SafeSetProperty(etoColumns[i], "Editable", editable);
		}
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

	private void SyncSelection()
	{
		SelectedItems.Clear();
		SelectedIndices.Clear();

		foreach (var item in Items)
		{
			item.Selected = false;
			item.Focused = false;
		}

		foreach (var row in SelectedRows)
		{
			if (row < 0 || row >= Items.Count)
				continue;

			SelectedIndices.Add(row);
			SelectedItems.Add(Items[row]);
			Items[row].Selected = true;
		}

		FocusedItem = SelectedItems.Count > 0 ? SelectedItems[0] : null;
		if (FocusedItem != null)
			FocusedItem.Focused = true;
	}

	private static void EnsureSubItems(ListViewItem item, int count)
	{
		while (item.SubItems.Count < count)
			item.SubItems.Add(new ListViewItem.ListViewSubItem());
	}

	private static string GetCellText(ListViewItem item, int columnIndex)
	{
			if (columnIndex == 0)
				return string.IsNullOrEmpty(item.Text) && item.SubItems.Count > 0 ? item.SubItems[0].Text : item.Text;

			return columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : "";
		}

	private static void SetCellText(ListViewItem item, int columnIndex, string value)
	{
			EnsureSubItems(item, columnIndex + 1);

			if (columnIndex == 0)
				item.Text = value ?? "";

			item.SubItems[columnIndex].Text = value ?? "";
		}
	}

	public class KeysharpMonthCalendar : Forms.Calendar
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public KeysharpMonthCalendar(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpNumericUpDown : NumericStepper
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public bool ThousandsSeparator { get; set; }
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
		public LeftRightAlignment UpDownAlign { get; set; } = LeftRightAlignment.Right;
		public bool Hexadecimal { get; set; }

		public KeysharpNumericUpDown(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpPictureBox : ImageView
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
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
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			Filename = filename;
		}
	}

	public class KeysharpProgressBar : Drawable
	{
		private readonly bool customColors = false;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
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
		public ProgressBarStyle Style
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
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
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
				var filledHeight = (int)Math.Round(innerHeight * percent);
				var fillRect = new Rectangle(rect.Left + inset, rect.Bottom - inset - filledHeight, innerWidth, filledHeight);
				DrawFill(e.Graphics, fillRect, fillColor, Style, true);
			}
			else
			{
				var filledWidth = (int)Math.Round(innerWidth * percent);
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
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public bool AutoSize { get; set; }

		public KeysharpRadioButton(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}

		public KeysharpRadioButton(RadioButton controller, int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
			: base(controller)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}

	public class KeysharpRichEdit : RichTextArea
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		internal bool IsNumeric { get; set; }
		internal bool Multiline {get; set; }
		internal int MaxLength = -1;
		internal CharacterCasing CharacterCasing { get; set; } = CharacterCasing.Normal;
		public RichTextBoxScrollBars ScrollBars { get; set; } = RichTextBoxScrollBars.None;

		public KeysharpRichEdit(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			TextChanged += KeysharpRichEdit_TextChanged;
		}

		private void KeysharpRichEdit_TextChanged(object sender, EventArgs e)
		{
			if (IsNumeric && Text.Any(ch => !char.IsDigit(ch)))
				Text = string.Join("", Text.Where(ch => char.IsDigit(ch)));

			switch (CharacterCasing)
			{
				case CharacterCasing.Upper:
					Text = Text.ToUpperInvariant();
					break;
				case CharacterCasing.Lower:
					Text = Text.ToLowerInvariant();
					break;
			}
		}
	}

	public class KeysharpStatusStrip : Panel
	{
		internal class StatusStripItemCollection : Collection<KeysharpToolStripStatusLabel>
		{
			private readonly KeysharpStatusStrip owner;

			internal StatusStripItemCollection(KeysharpStatusStrip owner)
			{
				this.owner = owner;
			}

			public KeysharpToolStripStatusLabel Add(KeysharpToolStripStatusLabel item)
			{
				base.Add(item);
				owner?.UpdateItems();
				return item;
			}

			protected override void InsertItem(int index, KeysharpToolStripStatusLabel item)
			{
				base.InsertItem(index, item);
				owner?.UpdateItems();
			}

			protected override void SetItem(int index, KeysharpToolStripStatusLabel item)
			{
				base.SetItem(index, item);
				owner?.UpdateItems();
			}

			protected override void RemoveItem(int index)
			{
				base.RemoveItem(index);
				owner?.UpdateItems();
			}

			protected override void ClearItems()
			{
				base.ClearItems();
				owner?.UpdateItems();
			}
		}

		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		internal bool AutoSize { get; set; }
		internal Size ImageScalingSize { get; set; }
		internal DockStyle Dock { get; set; }
		internal bool SizingGrip { get; set; }
		internal StatusStripItemCollection Items { get; }
		private readonly List<Control> partControls = new();

		public KeysharpStatusStrip(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
			Items = new StatusStripItemCollection(this);
			BackgroundColor = Colors.LightGrey;
		}

		internal void UpdateItems()
		{
			partControls.Clear();

			var parts = new List<StackLayoutItem>();

			for (var i = 0; i < Items.Count; i++)
			{
				var item = Items[i];
				var partControl = BuildPartControl(item, i);
				if (item.Width > 0)
					partControl.Width = item.Width;

				partControls.Add(partControl);
				parts.Add(new StackLayoutItem(partControl, item.Width <= 0 || item.Spring));
			}

			var body = new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Spacing = 0,
				Padding = new Padding(0, 0, 0, 0)
			};
			body.Items.AddRange(parts);

			var border = new Panel
			{
				BackgroundColor = Colors.Gray,
				Height = 1
			};

			var layout = new StackLayout
			{
				Orientation = Orientation.Vertical,
				Spacing = 0
			};
			layout.Items.Add(new StackLayoutItem(border, true));
			layout.Items.Add(new StackLayoutItem(body, true));

			Content = layout;
		}

		private Control BuildPartControl(KeysharpToolStripStatusLabel item, int index)
		{
			var back = item.BackColor.A > 0 ? item.BackColor : BackgroundColor;
			Control textLayout = BuildTextLayout(item.Text ?? string.Empty, item.Font ?? this.Font, back);

			if (item.Image != null)
			{
				var imgView = new ImageView { Image = item.Image, BackgroundColor = back };
				textLayout = new StackLayout
				{
					Orientation = Orientation.Horizontal,
					Spacing = 4,
					Padding = new Padding(2, 0),
					Items =
					{
						new StackLayoutItem(imgView, false),
						new StackLayoutItem(textLayout, true)
					}
				};
			}

			var panel = new Panel
			{
				BackgroundColor = back,
				Content = textLayout,
				Padding = new Padding(4, 2)
			};

			return panel;
		}

		private static Control BuildTextLayout(string text, Font font, Color back)
		{
			var segments = text.Split('\t');
			var left = segments.Length > 0 ? segments[0] : string.Empty;
			var center = segments.Length > 1 ? segments[1] : string.Empty;
			var right = segments.Length > 2 ? segments[2] : string.Empty;

			var leftLabel = new Forms.Label { Text = left, Font = font, BackgroundColor = back, TextAlignment = Forms.TextAlignment.Left, VerticalAlignment = Forms.VerticalAlignment.Center };
			var centerLabel = new Forms.Label { Text = center, Font = font, BackgroundColor = back, TextAlignment = Forms.TextAlignment.Center, VerticalAlignment = Forms.VerticalAlignment.Center };
			var rightLabel = new Forms.Label { Text = right, Font = font, BackgroundColor = back, TextAlignment = Forms.TextAlignment.Right, VerticalAlignment = Forms.VerticalAlignment.Center };

			var row = new TableRow(
				new TableCell(leftLabel, true),
				new TableCell(centerLabel, true),
				new TableCell(rightLabel, true));

			return new TableLayout
			{
				Padding = Padding.Empty,
				Spacing = new Size(4, 0),
				Rows = { row }
			};
		}

	}

	public class KeysharpTabControl : TabControl
	{
		internal Color? bgcolor;
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public TabAlignment Alignment { get; set; }
		public TabAppearance Appearance { get; set; }
		public bool Multiline { get; set; }
		internal ImageList ImageList { get; set; }

		public KeysharpTabControl(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
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
				requestedSize.Width == int.MinValue ? (int)Math.Round(tempw) : requestedSize.Width,
				requestedSize.Height == int.MinValue ? (int)Math.Round(temph) : requestedSize.Height);
			
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
		internal readonly List<IFuncObj> doubleClickHandlers = [];

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
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

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
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
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
		public bool IsExpanded { get; private set; }
		public Font NodeFont { get; set; }
		public IntPtr Handle => new IntPtr(id);

		private void EnsureValues()
		{
			if (Values == null || Values.Length == 0)
				Values = new object[1];
		}

		public TreeNode NextNode
		{
			get
			{
				var siblings = GetSiblings();
				if (siblings == null)
					return null;

				for (var i = 0; i < siblings.Count; i++)
				{
					if (siblings[i] == this)
						return i + 1 < siblings.Count ? siblings[i + 1] : null;
				}

				return null;
			}
		}

		public TreeNode PrevNode
		{
			get
			{
				var siblings = GetSiblings();
				if (siblings == null)
					return null;

				for (var i = 0; i < siblings.Count; i++)
				{
					if (siblings[i] == this)
						return i - 1 >= 0 ? siblings[i - 1] : null;
				}

				return null;
			}
		}

		public TreeNode FirstNode => Nodes.Count > 0 ? Nodes[0] : null;

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

		public void Expand()
		{
			if (Nodes == null || Nodes.Count == 0)
				return;

			IsExpanded = true;
			Expanded = true;
		}

		public void Collapse()
		{
			IsExpanded = false;
			Expanded = false;
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
			base.RemoveItem(index);
			if (item != null && etoItems != null)
				_ = etoItems.Remove(item);
		}

		protected override void ClearItems()
		{
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
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;
		private readonly Dictionary<ITreeGridItem, bool> expandStates = [];
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
					SelectedItem = value;
			}
		}
		internal TreeNode TopNode { get; set; }
		internal TreeNodeCollection Nodes { get; }
		internal TreeGridItemCollection RootItems { get; }
		public bool CheckBoxes
		{
			get => checkBoxes;
			set
			{
				checkBoxes = value;
				UpdateCheckColumn();
			}
		}
		public bool ShowLines { get; set; } = true;
		public bool ShowPlusMinus { get; set; } = true;
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
		public bool HideSelection { get; set; }

		private bool checkBoxes;
		private bool labelEdit;
		private ImageList imageList;
		internal GridColumn CheckColumn => checkColumn;
		internal bool HasCheckBoxes => checkBoxes;
		internal int TextColumnIndex => textColumn != null ? Columns.IndexOf(textColumn) : -1;

		public KeysharpTreeView(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
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

		internal void DelayedExpandParent(TreeNode node)
		{
			var parent = node.Parent ?? node;
			if (expandStates.TryGetValue(parent, out var b) && b)
			{
				if (parent.Expandable)
				{
					parent.Expanded = true;
					_ = expandStates.Remove(parent);
				}
			}
		}

		internal void MarkForExpansion(TreeNode node) => expandStates[node] = true;

		internal void RemoveMarkForExpansion(TreeNode node) => _ = expandStates.Remove(node);

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

			SelectedItem = node;
			SelectedNode = node;
			if (ensureVisible)
				node.EnsureVisible();
		}

		public void Sort()
		{
			Nodes.SortByText();
			Application.Instance.AsyncInvoke(new Action(ReloadData));
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
		internal static void SortByText(this TreeNodeCollection nodes)
		{
			var ordered = nodes.OrderBy(node => node.Text, StringComparer.OrdinalIgnoreCase).ToList();
			nodes.Clear();
			foreach (var node in ordered)
			{
				node.Nodes?.SortByText();
				nodes.Add(node);
			}
		}
	}

	public class KeysharpWebBrowser : WebView
	{
		private readonly int addStyle, removeStyle;
		private readonly int addExStyle, removeExStyle;

		public void Navigate (string url) => Url = new Uri(url);

		public KeysharpWebBrowser(int _addStyle = 0, int _addExStyle = 0, int _removeStyle = 0, int _removeExStyle = 0)
		{
			addStyle = _addStyle;
			addExStyle = _addExStyle;
			removeStyle = _removeStyle;
			removeExStyle = _removeExStyle;
		}
	}
}
#endif
