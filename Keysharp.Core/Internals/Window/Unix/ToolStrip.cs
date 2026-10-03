using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Window.Unix
{
	public class ToolStripItem
	{
		private ToolStrip parent;
		private bool checkedValue;
		private string text = "";
		private string name = "";
		private bool visible = true;
		private bool enabled = true;
		private object image;

		internal MenuItem EtoItem { get; set; }

		public string Text
		{
			get => text;
			set
			{
				text = value ?? "";
				if (EtoItem != null)
					EtoItem.Text = PresentedText;
			}
		}

		internal virtual string PresentedText => text;

		//Whether this item must be backed by an Eto CheckMenuItem rather than a ButtonMenuItem. The base item
		//only ever builds a ButtonMenuItem, so overrides must stay in step with their own BuildEtoItem.
		internal virtual bool NeedsCheckItem => false;

		public string Name
		{
			get => name;
			set => name = value ?? "";
		}

		public bool Visible
		{
			get => visible;
			set
			{
				visible = value;
				if (EtoItem != null)
					EtoItem.Visible = value;
			}
		}

		// Shared menu code uses WinForms Available so closed menus can still toggle items.
		// Unix Visible already stores that state independently of the parent.
		public bool Available
		{
			get => Visible;
			set => Visible = value;
		}

		public bool Enabled
		{
			get => enabled;
			set
			{
				enabled = value;
				if (EtoItem != null)
					EtoItem.Enabled = value;
			}
		}

		public bool Checked
		{
			get => checkedValue;
			set
			{
				if (checkedValue == value)
					return;

				checkedValue = value;
				// Only a CheckMenuItem can show a state indicator and only a ButtonMenuItem can show an image, so
				// the parent has to rebuild its list whenever the required type changes. Toggling an item that is
				// already of the right type — every radio item, and every uncheck of a plain one — does not.
				var isCheckItem = EtoItem is CheckMenuItem;

				if (EtoItem != null && isCheckItem != NeedsCheckItem)
					parent?.SyncEtoItems();
				else if (isCheckItem)
					((CheckMenuItem)EtoItem).Checked = value;

				CheckedChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public Color BackColor
		{
			get => Colors.Black;
			set => _ = value;
		}

		public Color ForeColor
		{
			get => Colors.Black;
			set => _ = value;
		}

		public object Tag { get; set; }

		public Eto.Drawing.Font Font { get; set; }

		public object Image
		{
			get => image;
			set
			{
				image = value;
				if (EtoItem is ButtonMenuItem button)
					button.Image = value as Eto.Drawing.Image;
			}
		}

		public ToolStrip Owner
		{
			get => parent;
			internal set => parent = value;
		}

		public event EventHandler Click;
		public event EventHandler CheckedChanged;

		internal void SetParent(ToolStrip owner) => parent = owner;

		public ToolStrip GetCurrentParent() => parent;

		public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

		internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

		internal virtual MenuItem BuildEtoItem()
		{
			if (EtoItem == null)
				EtoItem = new ButtonMenuItem();

			EtoItem.Text = PresentedText;
			EtoItem.Enabled = Enabled;
			EtoItem.Visible = Visible;

			if (EtoItem is ButtonMenuItem button && image is Eto.Drawing.Image etoImage)
				button.Image = etoImage;

			EtoItem.Click -= EtoItem_Click;
			EtoItem.Click += EtoItem_Click;
			return EtoItem;
		}

		internal virtual void ResetEtoItemRecursive()
		{
			EtoItem = null;
		}

		private void EtoItem_Click(object sender, EventArgs e) => RaiseClick();
	}

	public class ToolStripSeparator : ToolStripItem
	{
		internal override MenuItem BuildEtoItem()
		{
			EtoItem = new SeparatorMenuItem();
			return EtoItem;
		}
	}

	public class ToolStripItemCollection : Collection<ToolStripItem>
	{
		private ToolStrip owner;
		private ToolStripMenuItem ownerMenuItem;

		internal ToolStripItemCollection(ToolStrip owner)
		{
			this.owner = owner;
		}

		internal ToolStripItemCollection(ToolStripMenuItem ownerMenuItem)
		{
			this.ownerMenuItem = ownerMenuItem;
		}

		internal void SetOwner(ToolStrip newOwner)
		{
			owner = newOwner;
			ownerMenuItem = null;
		}

		public ToolStripMenuItem Add(string text)
		{
			var item = new ToolStripMenuItem(text);
			Add(item);
			return item;
		}

		public new ToolStripItem Add(ToolStripItem item)
		{
			AddItem(item);
			return item;
		}

		public void AddRange(ToolStripItem[] items)
		{
			foreach (var item in items)
				AddItem(item);
		}

		public ToolStripItem[] Find(string key, bool searchAllChildren)
		{
			if (string.IsNullOrEmpty(key))
				return [];

			var matches = new List<ToolStripItem>();
			FindRecursive(matches, this, key, searchAllChildren);
			return [.. matches];
		}

		private static void FindRecursive(List<ToolStripItem> matches, IEnumerable<ToolStripItem> items, string key, bool searchAllChildren)
		{
			foreach (var item in items)
			{
				if (string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase))
					matches.Add(item);

				if (searchAllChildren && item is ToolStripMenuItem menuItem)
					FindRecursive(matches, menuItem.DropDownItems, key, true);
			}
		}

		private void AddItem(ToolStripItem item)
		{
			if (item == null)
				return;

			if (owner != null)
				item.SetParent(owner);
			else if (ownerMenuItem != null)
				item.SetParent(ownerMenuItem.Owner);

			Items.Add(item);
			owner?.SyncEtoItems();
			ownerMenuItem?.SyncSubItems();
		}

		protected override void InsertItem(int index, ToolStripItem item)
		{
			if (item != null)
			{
				if (owner != null)
					item.SetParent(owner);
				else if (ownerMenuItem != null)
					item.SetParent(ownerMenuItem.Owner);
			}

			base.InsertItem(index, item);
			owner?.SyncEtoItems();
			ownerMenuItem?.SyncSubItems();
		}

		protected override void SetItem(int index, ToolStripItem item)
		{
			if (item != null)
			{
				if (owner != null)
					item.SetParent(owner);
				else if (ownerMenuItem != null)
					item.SetParent(ownerMenuItem.Owner);
			}

			base.SetItem(index, item);
			owner?.SyncEtoItems();
			ownerMenuItem?.SyncSubItems();
		}

		protected override void RemoveItem(int index)
		{
			base.RemoveItem(index);
			owner?.SyncEtoItems();
			ownerMenuItem?.SyncSubItems();
		}

		protected override void ClearItems()
		{
			base.ClearItems();
			owner?.SyncEtoItems();
			ownerMenuItem?.SyncSubItems();
		}
	}

	public class ToolStrip
	{
		private static long nextSyntheticHandle = 1;
		private readonly nint syntheticHandle;
		public ToolStripItemCollection Items { get; }
		public string Name { get; set; } = "";
		public DockStyle Dock { get; set; } = DockStyle.None;
		public nint Handle
		{
			get
			{
				var handle = ContextMenu.Handle;
				return handle != 0 ? handle : syntheticHandle;
			}
		}

		public Color BackColor
		{
			get => Colors.Black;
			set => _ = value;
		}

		public Color ForeColor
		{
			get => Colors.Black;
			set => _ = value;
		}

		internal ContextMenu ContextMenu { get; } = new ContextMenu();

		public ToolStrip()
		{
			syntheticHandle = new nint(Interlocked.Increment(ref nextSyntheticHandle));
			Items = new ToolStripItemCollection(this);
		}

		public IEnumerable<ToolStripItem> GetItems() => Items;

		internal virtual void SyncEtoItems()
		{
			ContextMenu.Items.Clear();
			foreach (var item in Items)
				ContextMenu.Items.Add(item.BuildEtoItem());

			UnixMenuPresentation.Apply(ContextMenu, Items);
		}

		public virtual void Refresh()
		{
			SyncEtoItems();
		}
	}

	public class ToolStripDropDownMenu : ToolStrip
	{
		private readonly ToolStripMenuItem ownerItem;

		public ToolStripDropDownMenu(ToolStripMenuItem ownerItem = null)
		{
			this.ownerItem = ownerItem;
		}

		internal override void SyncEtoItems()
		{
			if (ownerItem != null)
				ownerItem.SyncSubItems();
			else
				base.SyncEtoItems();
		}

		public virtual void Show(Eto.Drawing.Point point, Control parent = null)
		{
			SyncEtoItems();
			ContextMenu.Show(parent, point);
		}
	}

	public class ContextMenuStrip : ToolStripDropDownMenu
	{
		internal ContextMenu EtoMenu => ContextMenu;

		public event EventHandler<EventArgs> Closed
		{
			add => ContextMenu.Closed += value;
			remove => ContextMenu.Closed -= value;
		}
	}

	public class ToolStripMenuItem : ToolStripItem
	{
		private readonly ToolStripDropDownMenu dropDownMenu;

		public ToolStripItemCollection DropDownItems => dropDownMenu.Items;
		public ToolStripDropDownMenu DropDown => dropDownMenu;
		public Keys ShortcutKeys { get; set; }
		public object TextAlign { get; set; }

		private Keysharp.Builtins.Menu.MenuItemPresentation Presentation =>
			Tag as Keysharp.Builtins.Menu.MenuItemPresentation;

		internal override string PresentedText
		{
			get
			{
				var text = base.PresentedText;
#if OSX
				// NSMenu does not expose a radio-style state image independently of radio-group behavior.
				// Prefixing the bullet preserves AHK's presentation without changing click/check semantics.
				if (Checked && Presentation?.Radio == true)
					text = "\u25cf " + text;

				if (Presentation?.Rtl == true)
					text = "\u2067" + text + "\u2069";
#endif
				return text;
			}
		}

		// A submenu is always a plain button item: it hosts the child menu and never shows a state indicator.
		// Beyond that, macOS draws the radio bullet into the text (see PresentedText) so a radio item stays a
		// button item, whereas GTK draws it with CheckMenuItem.DrawAsRadio and needs a check item even unchecked.
#if OSX
		internal override bool NeedsCheckItem => DropDownItems.Count == 0 && Checked && Presentation?.Radio != true;
#else
		internal override bool NeedsCheckItem => DropDownItems.Count == 0 && (Checked || Presentation?.Radio == true);
#endif

		public ToolStripMenuItem()
		{
			dropDownMenu = new ToolStripDropDownMenu(this);
		}

		public ToolStripMenuItem(string text) : this()
		{
			Text = text;
			Name = text;
		}

		internal override MenuItem BuildEtoItem()
		{
			if (EtoItem == null || (EtoItem is CheckMenuItem) != NeedsCheckItem)
				EtoItem = NeedsCheckItem ? new CheckMenuItem() : new ButtonMenuItem();

			EtoItem.Text = PresentedText;
			EtoItem.Enabled = Enabled;
			EtoItem.Visible = Visible;

			if (EtoItem is ButtonMenuItem button && Image is Eto.Drawing.Image etoImage)
				button.Image = etoImage;

			if (EtoItem is CheckMenuItem checkItem)
				checkItem.Checked = Checked;

			EtoItem.Click -= EtoItem_Click;
			EtoItem.Click += EtoItem_Click;

			SyncSubItems();
			UnixMenuPresentation.Apply(EtoItem, Presentation);
			return EtoItem;
		}

		internal override void ResetEtoItemRecursive()
		{
			foreach (var item in DropDownItems)
				item.ResetEtoItemRecursive();

			EtoItem = null;
		}

		internal void SyncSubItems()
		{
			if (EtoItem is not ButtonMenuItem button)
				return;

			button.Items.Clear();
			foreach (var item in DropDownItems)
			{
				// Force a fresh Eto item to avoid GTK "parent already set" warnings.
				item.ResetEtoItemRecursive();
				button.Items.Add(item.BuildEtoItem());
			}

			UnixMenuPresentation.Apply(button, DropDownItems);
		}

		private void EtoItem_Click(object sender, EventArgs e)
		{
			// Selecting an AHK menu item does not implicitly toggle its check/radio state.
			if (sender is CheckMenuItem checkItem && checkItem.Checked != Checked)
				checkItem.Checked = Checked;

			RaiseClick();
		}
	}

	internal static class UnixMenuPresentation
	{
		internal static void Apply(MenuItem item, Keysharp.Builtins.Menu.MenuItemPresentation presentation)
		{
#if LINUX
			if (item?.ControlObject is Gtk.MenuItem nativeItem)
			{
				var rtl = presentation?.Rtl == true;
				// GTK3's RightJustified property is deprecated without a native replacement; it is still the
				// platform API which implements AHK's right-aligned menu-bar item.
#pragma warning disable CS0612
				nativeItem.RightJustified = presentation?.Right == true;
#pragma warning restore CS0612
				nativeItem.Direction = rtl ? Gtk.TextDirection.Rtl : Gtk.TextDirection.Ltr;

				// The item's own direction only decides which side the state indicator sits on: a GtkMenuItem does
				// not propagate it to the label Eto adds as its child, which is what actually lays out the text.
				// A GtkLabel mirrors its alignment for an RTL direction, so this is what right-aligns the item.
				// Setting an explicit alignment instead would not work: GTK mirrors that too, cancelling it out.
				if (nativeItem.Child is Gtk.Label label)
					label.Direction = nativeItem.Direction;

				if (nativeItem is Gtk.CheckMenuItem checkItem)
					checkItem.DrawAsRadio = presentation?.Radio == true;
			}
#endif
		}

		internal static void Apply(ContextMenu menu, IReadOnlyList<ToolStripItem> items)
		{
#if LINUX
			if (menu?.ControlObject is Gtk.Menu nativeMenu)
				Apply(nativeMenu, items);
#endif
		}

		internal static void Apply(ButtonMenuItem parent, IReadOnlyList<ToolStripItem> items)
		{
#if LINUX
			if (parent?.ControlObject is Gtk.MenuItem nativeParent && nativeParent.Submenu is Gtk.Menu nativeMenu)
				Apply(nativeMenu, items);
#endif
		}

#if LINUX
		internal const string BarBreakStyleClass = "keysharp-menu-barbreak";
		private const uint ApplicationStylePriority = 600;//GTK_STYLE_PROVIDER_PRIORITY_APPLICATION, which gtk-sharp does not expose.
		private static Gtk.CssProvider barBreakProvider;

		//A GtkMenu gives every one of its columns the width of the widest item in the whole menu, so a column
		//holding nothing but a divider would be as wide as a column of text. A left border on the items of the
		//column the divider belongs to is the only placement that costs no width.
		private static void EnsureBarBreakStyle()
		{
			if (barBreakProvider != null || Gdk.Screen.Default is not Gdk.Screen screen)
				return;

			barBreakProvider = new Gtk.CssProvider();
			_ = barBreakProvider.LoadFromData($"menuitem.{BarBreakStyleClass} {{ border-left: 1px solid alpha(currentColor, 0.35); }}");
			Gtk.StyleContext.AddProviderForScreen(screen, barBreakProvider, ApplicationStylePriority);
		}

		private static void Apply(Gtk.Menu menu, IReadOnlyList<ToolStripItem> items)
		{
			var columns = new List<(List<Gtk.Widget> Widgets, bool Bar)>();
			var current = new List<Gtk.Widget>();
			columns.Add((current, false));

			foreach (var item in items)
			{
				if (item.EtoItem?.ControlObject is not Gtk.Widget widget)
					continue;

				var presentation = item.Tag as Keysharp.Builtins.Menu.MenuItemPresentation;
				Apply(item.EtoItem, presentation);
				widget.StyleContext.RemoveClass(BarBreakStyleClass);

				if (current.Count > 0 && presentation is { StartsColumn: true })
				{
					current = [];
					columns.Add((current, presentation.BarBreak));
				}

				current.Add(widget);
			}

			if (columns.Count == 1)
				return;

			if (columns.Any(static column => column.Bar))
				EnsureBarBreakStyle();

			for (var columnIndex = 0; columnIndex < columns.Count; ++columnIndex)
			{
				var (widgets, bar) = columns[columnIndex];

				for (var row = 0; row < widgets.Count; ++row)
				{
					//Attaching a widget the menu already owns only moves it, so nothing has to be removed first.
					menu.Attach(widgets[row], (uint)columnIndex, (uint)columnIndex + 1, (uint)row, (uint)row + 1);

					if (bar)
						widgets[row].StyleContext.AddClass(BarBreakStyleClass);
				}
			}
		}
#endif
	}
}
#endif
