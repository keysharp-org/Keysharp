namespace Keysharp.Scripting
{
	public partial class Script
	{
		internal ToolStripMenuItem openMenuItem;
		internal ToolStripMenuItem suspendMenuItem;
		internal NotifyIcon Tray;
		internal Keysharp.Core.Menu trayMenu;

		public void CreateTrayMenu()
		{
			var trayIcon = Tray = new NotifyIcon { ContextMenuStrip = new ContextMenuStrip(), Text = A_ScriptName.Substring(0, Math.Min(A_ScriptName.Length, 64)) };//System tray icon tooltips have a limit of 64 characters.
			Script.TheScript.ProcessesData.mainContext = SynchronizationContext.Current;//This must happen after the icon is created.

			if (NoTrayIcon)
				return;

			trayMenu = new (Tray.ContextMenuStrip);
			trayMenu.AddStandard();
			trayIcon.Tag = trayMenu;
			trayIcon.MouseClick += TrayIcon_MouseClick;
			trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

			if (normalIcon is Icon icon)
			{
				trayIcon.Icon = icon;
				trayIcon.Visible = true;
			}
		}

		internal static void SuspendHotkeys()
		{
			Script.TheScript.mainWindow.CheckedInvoke(() =>
			{
				var script = Script.TheScript;
				var suspended = script.flowData.suspended = !script.flowData.suspended;
				script.HotstringManager.SuspendAll(suspended);//Must do this prior to ManifestAllHotkeysHotstringsHooks() to avoid incorrect removal of hook.
				_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(); //Update the state of all hotkeys based on the complex interdependencies hotkeys have with each another.
				script.suspendMenuItem.Checked = suspended;
				script.mainWindow.SuspendHotkeysToolStripMenuItem.Checked = suspended;
				script.Tray.Icon = suspended ? script.suspendedIcon : script.normalIcon;
			}, false);
		}

		private static void TrayIcon_MouseClick(object sender, MouseEventArgs e)
		{
			if (sender is NotifyIcon ni && ni.Tag is Keysharp.Core.Menu mnu)
				if (mnu.ClickCount == 1)
					if (mnu.defaultItem is ToolStripItem tsi)
						mnu.Tsmi_Click(tsi, new EventArgs());
		}

		private static void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			if (sender is NotifyIcon ni && ni.Tag is Keysharp.Core.Menu mnu)
				if (mnu.ClickCount > 1)
					if (mnu.defaultItem is ToolStripItem tsi)
						mnu.Tsmi_Click(tsi, new EventArgs());
		}
	}
}