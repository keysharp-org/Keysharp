using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		internal ToolStripMenuItem openMenuItem;
		internal ToolStripMenuItem suspendMenuItem;
		internal NotifyIcon Tray;
		internal Keysharp.Builtins.Menu trayMenu;

		/// <summary>
		/// The tray tooltip, which is the script's and not the icon's: it outlives #NoTrayIcon and a display-less
		/// session, and an icon created later shows whatever it holds. System tray tooltips cap at 64 characters,
		/// so the script name it defaults to is truncated to fit.
		/// </summary>
		internal string TrayTip
		{
			get => AccessorData.iconTip ??= A_ScriptName.Substring(0, Math.Min(A_ScriptName.Length, 64));

			set
			{
				AccessorData.iconTip = value;

				if (Tray != null)
					Tray.Text = value;
			}
		}

		/// <summary>
		/// Guarantees the tray icon, which needs a tray to sit in. Icon-valued accessors use this; a script
		/// reaching for the menu alone wants <see cref="EnsureTrayMenu"/>.
		/// </summary>
		internal bool EnsureTrayIcon()
		{
			if (Tray != null && trayMenu != null)
				return true;

			if (NoTrayIcon || IsUiInitializationBlocked || IsHeadless)
				return false;

			InvokeOnUIThread(() =>
			{
				if (Tray == null || trayMenu == null)
					CreateTrayMenu();
			});

			return Tray != null && trayMenu != null;
		}

		/// <summary>
		/// Guarantees the tray menu, which is the script's own and outlives any icon: AutoHotkey builds it at
		/// startup, so it stays readable and buildable under #NoTrayIcon and with no tray to show it in.
		/// </summary>
		internal bool EnsureTrayMenu()
		{
			if (trayMenu != null)
				return true;

			if (IsUiInitializationBlocked)
				return false;

			InvokeOnUIThread(() =>
			{
				if (trayMenu == null)
					CreateTrayMenu();
			});

			return trayMenu != null;
		}

		public void CreateTrayMenu()
		{
			if (IsUiInitializationBlocked)
				return;

			// The menu is created ahead of the icon and independently of it, so #NoTrayIcon and a display-less
			// session withhold only the icon. The standard items are appended once: a later call that still
			// wants the icon finds the menu already built.
			if (trayMenu == null)
			{
				try
				{
					Keysharp.Builtins.Menu menu = new();
					menu.AddStandard();
					trayMenu = menu;
				}
				catch (Exception ex)
				{
					// A toolkit which cannot build a menu cannot build a tray either, so stop asking for both.
					NoTrayIcon = true;
					Script.WriteUncaughtErrorToStdErr("Tray menu initialization skipped: " + ex.Message);
					return;
				}
			}

			if (NoTrayIcon || IsHeadless)
				return;

			NotifyIcon trayIcon;

			try
			{
				trayIcon = Tray = new NotifyIcon { ContextMenuStrip = trayMenu.MenuItem, Text = TrayTip };
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

			trayIcon.Tag = trayMenu;
			trayIcon.MouseClick += TrayIcon_MouseClick;
			trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;

			if (trayDefaultIcon is Icon icon)
			{
				trayIcon.Icon = icon;
				trayIcon.Visible = true;
			}
		}

			internal static void SuspendHotkeys()
			{
				var script = Script.TheScript;
				script.InvokeOnUIThread(() =>
				{
					var suspended = script.flowData.suspended = !script.flowData.suspended;
					script.HotstringManager.SuspendAll(suspended);//Must do this prior to ManifestAllHotkeysHotstringsHooks() to avoid incorrect removal of hook.
					_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(script); //Update the state of all hotkeys based on the complex interdependencies hotkeys have with each another.

					script.suspendMenuItem?.Checked = suspended;
					script.mainWindow?.SuspendHotkeysToolStripMenuItem.Checked = suspended;
					if (!(bool)A_IconFrozen)
						script.Tray?.Icon = suspended ? script.suspendedIcon : script.trayDefaultIcon;
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
