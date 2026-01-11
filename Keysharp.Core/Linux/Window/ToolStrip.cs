#if LINUX
namespace Keysharp.Core.Linux
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
					EtoItem.Text = text;
			}
		}

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
				if (EtoItem is CheckMenuItem checkItem)
					checkItem.Checked = value;

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
				if (EtoItem is ButtonMenuItem button && value is Eto.Drawing.Image etoImage)
					button.Image = etoImage;
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

			EtoItem.Text = Text;
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

		public ToolStripItem Add(ToolStripItem item)
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
	}

	public class ToolStrip
	{
		public ToolStripItemCollection Items { get; }
		public string Name { get; set; } = "";
		public DockStyle Dock { get; set; } = DockStyle.None;
		public nint Handle => ContextMenu.Handle;

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
			Items = new ToolStripItemCollection(this);
		}

		public IEnumerable<ToolStripItem> GetItems() => Items;

		internal virtual void SyncEtoItems()
		{
			ContextMenu.Items.Clear();
			foreach (var item in Items)
				ContextMenu.Items.Add(item.BuildEtoItem());
		}

		public virtual void Refresh()
		{
			SyncEtoItems();
		}
	}

	public class ToolStripDropDownMenu : ToolStrip
	{
		public virtual void Show(Eto.Drawing.Point point, Control parent = null)
		{
			SyncEtoItems();
			ContextMenu.Show(parent, point);
		}
	}

	public class ContextMenuStrip : ToolStripDropDownMenu
	{
		internal ContextMenu EtoMenu => ContextMenu;
	}

	public class ToolStripMenuItem : ToolStripItem
	{
		private readonly ToolStripDropDownMenu dropDownMenu;

		public ToolStripItemCollection DropDownItems => dropDownMenu.Items;
		public ToolStripDropDownMenu DropDown => dropDownMenu;
		public Keys ShortcutKeys { get; set; }
		public object TextAlign { get; set; }

		public ToolStripMenuItem()
		{
			dropDownMenu = new ToolStripDropDownMenu();
		}

		public ToolStripMenuItem(string text) : this()
		{
			Text = text;
			Name = text;
		}

		internal override MenuItem BuildEtoItem()
		{
			if (EtoItem == null || (Checked && EtoItem is not CheckMenuItem))
			{
				if (DropDownItems.Count > 0)
					EtoItem = new ButtonMenuItem();
				else if (Checked)
					EtoItem = new CheckMenuItem();
				else
					EtoItem = new ButtonMenuItem();
			}

			EtoItem.Text = Text;
			EtoItem.Enabled = Enabled;
			EtoItem.Visible = Visible;

			if (EtoItem is ButtonMenuItem button && Image is Eto.Drawing.Image etoImage)
				button.Image = etoImage;

			if (EtoItem is CheckMenuItem checkItem)
			{
				checkItem.CheckedChanged -= CheckItem_CheckedChanged;
				checkItem.CheckedChanged += CheckItem_CheckedChanged;
				checkItem.Checked = Checked;
			}

			EtoItem.Click -= EtoItem_Click;
			EtoItem.Click += EtoItem_Click;

			SyncSubItems();
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
		}

		private void CheckItem_CheckedChanged(object sender, EventArgs e)
		{
			if (sender is CheckMenuItem checkItem)
				Checked = checkItem.Checked;
		}

		private void EtoItem_Click(object sender, EventArgs e) => RaiseClick();
	}
}
#endif
