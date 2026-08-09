using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		internal ToolStripMenuItem openMenuItem;
		internal ToolStripMenuItem suspendMenuItem;
		internal NotifyIcon Tray;
		internal Keysharp.Builtins.Menu trayMenu;

		internal bool EnsureTrayMenu()
		{
			if (Tray != null && trayMenu != null)
				return true;

			if (NoTrayIcon || IsUiInitializationBlocked || IsHeadless)
				return false;

			Script.InvokeOnUIThread(() =>
			{
				if (Tray == null || trayMenu == null)
					CreateTrayMenu();
			});

			return Tray != null && trayMenu != null;
		}

		public void CreateTrayMenu()
		{
			if (NoTrayIcon || IsUiInitializationBlocked || IsHeadless)
				return;

			NotifyIcon trayIcon;

			try
			{
				trayIcon = Tray = new NotifyIcon { ContextMenuStrip = new ContextMenuStrip(), Text = A_ScriptName.Substring(0, Math.Min(A_ScriptName.Length, 64)) };//System tray icon tooltips have a limit of 64 characters.
			}
			catch (DllNotFoundException ex)
			{
				// Some Linux CI images lack AppIndicator runtime dependencies. Fallback to no tray.
				NoTrayIcon = true;
				Script.WriteUncaughtErrorToStdErr("Tray initialization skipped: " + ex.Message);
				return;
			}
			catch (TypeInitializationException ex)
			{
				NoTrayIcon = true;
				Script.WriteUncaughtErrorToStdErr("Tray initialization skipped: " + ex.Message);
				return;
			}
			catch (InvalidOperationException ex)
			{
				NoTrayIcon = true;
				Script.WriteUncaughtErrorToStdErr("Tray initialization skipped: " + ex.Message);
				return;
			}

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
				Script.InvokeOnUIThread(() =>
				{
					var script = Script.TheScript;
					var suspended = script.flowData.suspended = !script.flowData.suspended;
					script.HotstringManager.SuspendAll(suspended);//Must do this prior to ManifestAllHotkeysHotstringsHooks() to avoid incorrect removal of hook.
					_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(); //Update the state of all hotkeys based on the complex interdependencies hotkeys have with each another.

					script.suspendMenuItem?.Checked = suspended;
					script.mainWindow?.SuspendHotkeysToolStripMenuItem.Checked = suspended;
					script.Tray?.Icon = suspended ? script.suspendedIcon : script.normalIcon;
				});
			}

		/// <summary>
		/// Whether a tray mouse event came from the primary (left) button.
		/// Only the left button activates the default menu item: the right button just displays the
		/// tray menu (which the underlying icon does on its own) and the middle button does nothing.
		/// This matters because the platform raises MouseClick/MouseDoubleClick for every button,
		/// so without this test a right-click would pop the menu *and* run the default item.
		/// </summary>
		private static bool IsPrimaryClick(MouseEventArgs e) =>
#if WINDOWS
			e.Button == Forms.MouseButtons.Left;
#else
			(e.Buttons & Forms.MouseButtons.Primary) != 0;
#endif

		private static void TrayIcon_MouseClick(object sender, MouseEventArgs e)
		{
			if (IsPrimaryClick(e) && sender is NotifyIcon ni && ni.Tag is Keysharp.Builtins.Menu mnu)
				if (mnu.ClickCount == 1)
					if (mnu.defaultItem is ToolStripItem tsi)
						mnu.Tsmi_Click(tsi, new EventArgs());
		}

		private static void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			if (IsPrimaryClick(e) && sender is NotifyIcon ni && ni.Tag is Keysharp.Builtins.Menu mnu)
				if (mnu.ClickCount > 1)
					if (mnu.defaultItem is ToolStripItem tsi)
						mnu.Tsmi_Click(tsi, new EventArgs());
		}
	}
}
