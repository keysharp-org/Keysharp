using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Window.Unix
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

		private bool systemMenuLoaded;

		// Called once the menu bar has been assigned to a window, after Eto's one-time CreateSystemMenu
		// (via MenuBar.OnPreLoad) has merged the standard App/Edit/Window menus. After that, OnPreLoad
		// won't run again, so SyncEtoMenuBar has to re-merge them itself.
		internal void MarkSystemMenuLoaded() => systemMenuLoaded = true;

		internal void SyncEtoMenuBar()
		{
			toolStrip.SyncNativeItems(EtoMenuBar.Items);

			// The sync drops the system menus Eto merged in (the macOS editing shortcuts), so they are merged again;
			// before the first load MenuBar.OnPreLoad merges them, and merging here too would double them.
			if (systemMenuLoaded && EtoMenuBar.Handler is Eto.Forms.MenuBar.IHandler handler)
				handler.CreateSystemMenu();
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

