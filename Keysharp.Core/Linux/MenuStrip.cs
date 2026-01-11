#if !WINDOWS
namespace Keysharp.Core.Linux
{
	public class MenuStrip : Panel
	{
		private readonly MenuStripToolStrip toolStrip;

		internal Eto.Forms.MenuBar EtoMenuBar { get; } = new Eto.Forms.MenuBar();
		public ToolStripItemCollection Items { get; }
		public DockStyle Dock { get; set; } = DockStyle.Top;
		public ToolStrip ToolStrip => toolStrip;

		public MenuStrip()
		{
			toolStrip = new MenuStripToolStrip(this);
			Items = toolStrip.Items;
		}

		internal void SyncEtoMenuBar()
		{
			EtoMenuBar.Items.Clear();
			foreach (var item in Items)
			{
				item.ResetEtoItemRecursive();
				EtoMenuBar.Items.Add(item.BuildEtoItem());
			}
		}

		private sealed class MenuStripToolStrip : ToolStrip
		{
			private readonly MenuStrip owner;

			internal MenuStripToolStrip(MenuStrip owner)
			{
				this.owner = owner;
			}

			internal override void SyncEtoItems()
			{
				owner.SyncEtoMenuBar();
			}
		}
	}
}
#endif
