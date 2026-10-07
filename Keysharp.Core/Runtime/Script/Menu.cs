using Keysharp.Builtins;
#if !WINDOWS
using Keysharp.Internals.Os.Windows;
#endif
namespace Keysharp.Runtime
{
	public partial class Script
	{
		internal ToolStripMenuItem openMenuItem;
		internal ToolStripMenuItem suspendMenuItem;
		internal ToolStripMenuItem pauseMenuItem;
		internal NotifyIcon Tray;
#if WINDOWS
		internal TrayMessageWindow trayMessageWindow;
#endif
		internal Keysharp.Builtins.Menu trayMenu;

		/// <summary>
		/// Whether the main thread's current pseudo-thread is paused, as the tray icon and the Pause Script items show it.
		/// As in AHK, they follow the current thread, so a thread launched over a paused one shows unpaused until it ends.
		/// Kept so that a thread launch or end which changes nothing costs only a comparison.
		/// </summary>
		internal bool showsPaused;

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
					trayMenu = new();
					_ = trayMenu.AddStandard();
				}
				catch (Exception ex)
				{
					// A toolkit which cannot build a menu cannot build a tray either, so stop asking for both.
					trayMenu = null;
					NoTrayIcon = true;
					Script.WriteUncaughtErrorToStdErr("Tray menu initialization skipped: " + ex.Message);
					return;
				}
			}

			// Tests share a process and need the menu model, but must not publish icons on the user's desktop.
			if (Tray != null || NoTrayIcon || IsHeadless || IsTestHost)
				return;

			NotifyIcon trayIcon;

			try
			{
				trayIcon = Tray = new NotifyIcon { ContextMenuStrip = trayMenu.MenuItem, Text = TrayTip };
#if WINDOWS
				try
				{
					trayMessageWindow = new TrayMessageWindow(this, trayIcon);
				}
				catch
				{
					Tray = null;
					trayIcon.Dispose();
					throw;
				}
#endif
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
#if WINDOWS
			trayIcon.MouseDown += TrayIcon_MouseDown;
#else
			trayIcon.MouseClick += TrayIcon_MouseClick;
			trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;
#endif

			if (trayDefaultIcon is Icon icon)
			{
				trayIcon.Icon = icon;
				trayIcon.Visible = true;
			}
		}

#if WINDOWS
		internal void TrayIcon_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button == Forms.MouseButtons.Left && sender is NotifyIcon { Tag: Keysharp.Builtins.Menu menu }
				&& (menu.ClickCount == 1 || e.Clicks == 2) && menu.defaultItem is ToolStripItem item)
			{
				if (Keysharp.Builtins.Menu.GetStandardCommand(item) is { } command)
					_ = WindowsAPI.PostMessage(MainWindowHandle, WindowsAPI.WM_COMMAND, (nint)command, 0);
				else
					menu.Tsmi_Click(item, EventArgs.Empty);
			}
		}
#endif

		internal void SetSuspended(bool suspended)
		{
			InvokeOnUIThread(() =>
			{
				flowData.suspended = suspended;
				HotstringManager.SuspendAll(suspended); // Update hotstrings before recalculating the hooks they need.
				_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(this);

				suspendMenuItem?.Checked = suspended;
				mainWindow?.SuspendHotkeysToolStripMenuItem.Checked = suspended;
				if (!(bool)A_IconFrozen && !NoTrayIcon && EnsureTrayIcon())
					ApplyTrayIcon();
			});
		}

		/// <summary>
		/// Stops treating <paramref name="item"/> as a standard item once a script gives it a callback of its own, as
		/// AHK does, so that its checkmark and visibility are the script's.
		/// </summary>
		internal void ReleaseStandardItem(ToolStripItem item)
		{
			if (ReferenceEquals(item, openMenuItem))
				openMenuItem = null;
			else if (ReferenceEquals(item, suspendMenuItem))
				suspendMenuItem = null;
			else if (ReferenceEquals(item, pauseMenuItem))
				pauseMenuItem = null;
		}

		internal static void SuspendHotkeys()
		{
			var script = TheScript;
			script.SetSuspended(!script.flowData.suspended);
		}

		/// <summary>
		/// The Pause Script menu item. As in AHK, it toggles the thread that was running when the item was chosen,
		/// which is the one beneath the thread this runs in; with no thread running, that pauses the script itself.
		/// </summary>
		internal static void TogglePause() => ThreadAccessors.A_IsPaused = !ThreadAccessors.A_IsPaused;

		/// <summary>
		/// The main window's Pause Script item, which is chosen outside any pseudo-thread and so launches one to toggle
		/// the thread beneath it.
		/// </summary>
		internal void LaunchTogglePause() => Threads.LaunchThreadInMain(TogglePause, kind: ThreadKind.Event);

		/// <summary>
		/// The tray icon for the pause and suspend states, or null when the script's own icon applies. As AHK's
		/// UpdateTrayIcon, a paused thread takes precedence; there is no separate icon for both.
		/// </summary>
		internal Icon TrayStateIcon => showsPaused ? pausedIcon : flowData.suspended ? suspendedIcon : null;

		/// <summary>
		/// Shows the tray icon AHK's UpdateTrayIcon picks: the TraySetIcon icon while frozen or while nothing is paused or
		/// suspended, else the icon of that state, and the script's own when there is neither. A pause or suspend change
		/// leaves a frozen icon alone by not calling this.
		/// </summary>
		internal void ApplyTrayIcon()
		{
			if (Tray == null)
				return;

			var stateIcon = TrayStateIcon;
			Tray.Icon = customIcon != null && (stateIcon == null || (bool)A_IconFrozen) ? customIcon : stateIcon ?? trayDefaultIcon;
		}

		/// <summary>
		/// Brings the tray icon and the Pause Script checkmarks up to date with the main thread's current pseudo-thread.
		/// Called wherever that can change: a pseudo-thread's launch or end, and a change of a pause flag.
		/// </summary>
		internal void UpdatePauseIndicators()
		{
			// A paused thread which ExitApp unwinds clears its flag after the tray is gone.
			if (IsDisposed || !IsOnMainThread)
				return;

			var paused = Threads.CurrentThread.IsPaused;

			if (paused == showsPaused)
				return;

			showsPaused = paused;
			pauseMenuItem?.Checked = paused;
			mainWindow?.PauseScriptToolStripMenuItem.Checked = paused;

			if (!(bool)A_IconFrozen)
				ApplyTrayIcon();
		}

#if !WINDOWS
		internal bool MainMessageClaimed(int message, nint wParam, nint lParam)
		{
			if (IsDisposed || hasExited)
				return true;

			var msg = new Message { HWnd = MainWindowHandle, Msg = message, WParam = wParam, LParam = lParam };
			return msgFilter?.CallEventHandlers(ref msg) == true || IsDisposed || hasExited;
		}

		private static bool IsPrimaryClick(MouseEventArgs e) =>
			(e.Buttons & Forms.MouseButtons.Primary) != 0;

		internal void TrayIcon_MouseClick(object sender, MouseEventArgs e) => TrayIcon_Activated(sender, e, false);

		internal void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e) => TrayIcon_Activated(sender, e, true);

		private void TrayIcon_Activated(object sender, MouseEventArgs e, bool doubleClick)
		{
			if (!IsPrimaryClick(e) || sender is not NotifyIcon { Tag: Keysharp.Builtins.Menu menu })
				return;

			// Eto exposes activation without native button details or cancellable menu opening.
			if (MainMessageClaimed((int)UserMessages.AHK_NOTIFYICON, (nint)UserMessages.AHK_NOTIFYICON,
				(nint)(doubleClick ? WindowsAPI.WM_LBUTTONDBLCLK : WindowsAPI.WM_LBUTTONUP)))
				return;

			if ((menu.ClickCount == 1 || doubleClick) && menu.defaultItem is ToolStripItem item)
				menu.Tsmi_Click(item, EventArgs.Empty);
		}
#endif
	}
}
