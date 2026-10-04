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

				// macOS shows a radio item's state in its text.
				EtoItem?.Text = PresentedText;
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
				InitEtoItem(new ButtonMenuItem(), EtoItem_Click);

			return EtoItem;
		}

		private protected void InitEtoItem(MenuItem etoItem, EventHandler<EventArgs> click)
		{
			EtoItem = etoItem;
			etoItem.Tag = this;
			etoItem.Text = PresentedText;
			etoItem.Enabled = enabled;
			etoItem.Visible = visible;

			if (etoItem is ButtonMenuItem button && image is Eto.Drawing.Image etoImage)
				button.Image = etoImage;

			etoItem.Click += click;
		}

		private void EtoItem_Click(object sender, EventArgs e) => RaiseClick();
	}

	public class ToolStripSeparator : ToolStripItem
	{
		internal override MenuItem BuildEtoItem() => EtoItem ??= new SeparatorMenuItem { Tag = this };
	}

	public class ToolStripItemCollection : Collection<ToolStripItem>
	{
		private readonly ToolStrip owner;

		internal ToolStripItemCollection(ToolStrip owner)
		{
			this.owner = owner;
		}

		public ToolStripMenuItem Add(string text)
		{
			var item = new ToolStripMenuItem(text);
			Add(item);
			return item;
		}

		public new ToolStripItem Add(ToolStripItem item)
		{
			if (item != null)
				base.Add(item);
			return item;
		}

		public void AddRange(ToolStripItem[] items)
		{
			foreach (var item in items)
				Add(item);
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

		protected override void InsertItem(int index, ToolStripItem item)
		{
			item?.SetParent(owner);
			base.InsertItem(index, item);
			owner.SyncEtoItems();
		}

		protected override void SetItem(int index, ToolStripItem item)
		{
			this[index]?.SetParent(null);
			item?.SetParent(owner);
			base.SetItem(index, item);
			owner.SyncEtoItems();
		}

		protected override void RemoveItem(int index)
		{
			this[index]?.SetParent(null);
			base.RemoveItem(index);
			owner.SyncEtoItems();
		}

		protected override void ClearItems()
		{
			foreach (var item in this)
				item?.SetParent(null);

			base.ClearItems();
			owner.SyncEtoItems();
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

		// Made on first use: a submenu's items live in its item's native submenu, so most strips never need one.
		internal ContextMenu ContextMenu => contextMenu ??= new ContextMenu();
		private ContextMenu contextMenu;

		public ToolStrip()
		{
			syntheticHandle = new nint(Interlocked.Increment(ref nextSyntheticHandle));
			Items = new ToolStripItemCollection(this);
		}

		public IEnumerable<ToolStripItem> GetItems() => Items;

#if LINUX
		// GTK keeps cell positions until an item leaves the menu.
		internal bool laidOutInColumns;
#endif

		internal virtual void SyncEtoItems()
		{
			SyncNativeItems(ContextMenu.Items);
			UnixMenuPresentation.Apply(ContextMenu, this);
		}

		// Keep existing native items so edits do not rebuild the whole menu.
		internal void SyncNativeItems(MenuItemCollection native)
		{
			for (var i = 0; i < Items.Count; i++)
			{
				var etoItem = Items[i].BuildEtoItem();

				while (i < native.Count && native[i] != etoItem && !Holds(native[i]))
					native.RemoveAt(i);

				if (i < native.Count && native[i] == etoItem)
					continue;

				// A native item sits in one menu at a time, so one held elsewhere, or further down this one, moves here.
				(etoItem.Parent as ISubmenu)?.Items.Remove(etoItem);
				native.Insert(i, etoItem);
			}

			while (native.Count > Items.Count)
				native.RemoveAt(native.Count - 1);
		}

		// A native item stops standing for an item once the item leaves this menu or is rebuilt as another type.
		private bool Holds(MenuItem etoItem) => etoItem.Tag is ToolStripItem item && item.Owner == this && item.EtoItem == etoItem;

		public virtual void Refresh()
		{
			SyncEtoItems();
		}

		public void Dispose() => contextMenu?.Dispose();
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
			{
				InitEtoItem(NeedsCheckItem ? new CheckMenuItem() : new ButtonMenuItem(), EtoItem_Click);

				if (EtoItem is CheckMenuItem checkItem)
					checkItem.Checked = Checked;
			}
			else
			{
				// macOS writes the item's options into its text, and an options change reaches the item only here.
				EtoItem.Text = PresentedText;
			}

			SyncSubItems();
			UnixMenuPresentation.Apply(EtoItem, Presentation);
			return EtoItem;
		}

		internal void SyncSubItems()
		{
			if (EtoItem is not ButtonMenuItem button)
				return;

			dropDownMenu.SyncNativeItems(button.Items);
			UnixMenuPresentation.Apply(button, dropDownMenu);
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

		internal static void Apply(ContextMenu menu, ToolStrip strip)
		{
#if LINUX
			if (menu?.ControlObject is Gtk.Menu nativeMenu)
				Apply(nativeMenu, strip);
#endif
		}

		internal static void Apply(ButtonMenuItem parent, ToolStrip strip)
		{
#if LINUX
			if (parent?.ControlObject is Gtk.MenuItem nativeParent && nativeParent.Submenu is Gtk.Menu nativeMenu)
				Apply(nativeMenu, strip);
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

		private static void Apply(Gtk.Menu menu, ToolStrip strip)
		{
			var items = strip.Items;

			// A GtkMenu places an item it was given no cell for in the first free row, so once a menu has had columns
			// every item is given its cell, keeping the order right when items are added or columns removed.
			if (!strip.laidOutInColumns)
			{
				for (var i = 1; i < items.Count && !strip.laidOutInColumns; i++)
					strip.laidOutInColumns = items[i].Tag is Keysharp.Builtins.Menu.MenuItemPresentation { StartsColumn: true };

				if (!strip.laidOutInColumns)
				{
					// An item moved here from a divided column of another menu brings its divider along.
					foreach (var item in items)
						(item.EtoItem?.ControlObject as Gtk.Widget)?.StyleContext.RemoveClass(BarBreakStyleClass);

					return;
				}
			}

			uint column = 0, row = 0;
			var bar = false;

			foreach (var item in items)
			{
				if (item.EtoItem?.ControlObject is not Gtk.Widget widget)
					continue;

				if (row > 0 && item.Tag is Keysharp.Builtins.Menu.MenuItemPresentation { StartsColumn: true } presentation)
				{
					column++;
					row = 0;
					bar = presentation.BarBreak;

					if (bar)
						EnsureBarBreakStyle();
				}

				//Attaching a widget the menu already owns only moves it, so nothing has to be removed first.
				menu.Attach(widget, column, column + 1, row, row + 1);
				row++;

				if (bar)
					widget.StyleContext.AddClass(BarBreakStyleClass);
				else
					widget.StyleContext.RemoveClass(BarBreakStyleClass);
			}
		}
#endif
	}
}
#endif
