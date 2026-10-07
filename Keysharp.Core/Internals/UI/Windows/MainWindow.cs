using Keysharp.Builtins;
namespace Keysharp.Internals.UI.Windows
{
	public partial class MainWindow : KeysharpForm
	{
		private const int WmQueryEndSession = 0x0011;
		public static Font OurDefaultFont = new ("MS Shell Dlg", 8F);
		internal FormWindowState lastWindowState = FormWindowState.Normal;
		private readonly bool clipSuccess;
		private AboutBox about;
		private bool selectingTab;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsClosing { get; private set; }

		internal ToolStripMenuItem PauseScriptToolStripMenuItem => pauseScriptToolStripMenuItem;
		internal ToolStripMenuItem SuspendHotkeysToolStripMenuItem => suspendHotkeysToolStripMenuItem;

		internal MainWindow(Script owner) : base(owner)
		{
			InitializeComponent();
			SetMenuStrip(mainMenu);
			//FormBorderStyle = FormBorderStyle.SizableToolWindow;
			SetStyle(ControlStyles.StandardClick, true);
			SetStyle(ControlStyles.StandardDoubleClick, true);
			SetStyle(ControlStyles.EnableNotifyMessage, true);
			// The main window keeps a single, constant handle for its whole lifetime (ShowInTaskbar is already
			// set to its final value by the designer, so nothing forces a handle recreation), which lots of other
			// logic relies on (e.g. hotkeys). Registering the clipboard listener here against that handle is safe.
			// Cross-platform counterpart lives in the Unix MainWindow, which subscribes to Eto's Clipboard.Changed
			// and raises the same ClipboardUpdate event; this Win32 listener is the Windows-specific implementation.
			clipSuccess = WindowsAPI.AddClipboardFormatListener(Handle);
			//Every tab shows a snapshot of live data (variables, hotkeys, key history, buffered debug output),
			//so it's regenerated whenever the user brings that tab into view, not just when a View menu item
			//is clicked. Without this, switching tabs in a freshly opened window shows empty text boxes.
			tcMain.SelectedIndexChanged += (_, _) =>
			{
				if (!selectingTab)
					RefreshSelectedTab();
			};
			editScriptToolStripMenuItem.Visible = !A_IsCompiled;
		}

		public void AddText(string s, MainFocusedTab tab, bool focus)
		{
			//Use CheckedBeginInvoke() because CheckedInvoke() seems to crash if this is called right as the window is closing.
			//Such as with a hotkey that prints on mouse click, which will cause a print when the X is clicked to close.
			this.CheckedBeginInvoke(() =>
			{
				GetText(tab).AppendText($"{s.ReplaceLineEndings(Environment.NewLine)}");//This should scroll to the bottom, if not, try this:
				if (focus)
					SelectTab(GetTab(tab));
			}, false, false);
		}

		public void ClearText(MainFocusedTab tab) => SetText(string.Empty, tab, false);

		// Writes the Debug tab at once, on the UI thread, so that a replacement and the text appended after it stay in
		// order; SetText defers and AddText does not.
		internal void ShowDebugOutput(string text, bool replace)
		{
			var s = text.ReplaceLineEndings(Environment.NewLine);

			if (replace)
				txtDebug.Text = s;
			else
				txtDebug.AppendText(s);
		}

		public void SetText(string s, MainFocusedTab tab, bool focus)
		{
			_ = this.BeginInvoke(() => //These need to be BeginInvoke(), otherwise they can freeze if called within a COM event.
			{
				GetText(tab).Text = s.ReplaceLineEndings(Environment.NewLine);

				if (focus)
					SelectTab(GetTab(tab));
			});
		}

		internal object ListHotkeys()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SetTextInternal(HotkeyDefinition.GetHotkeyDescriptions(OwnerScript), MainFocusedTab.Hotkeys, txtHotkeys, true);
			});
			return DefaultObject;
		}

		internal object ShowDebug()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SelectTab(tpDebug);

				// Show any OutputDebug text accumulated while the window was hidden or not yet shown; otherwise it
				// only appears once another OutputDebug call comes in.
				OwnerScript.DebugOutput.Refresh();
			});
			return DefaultObject;
		}

		internal object ShowHistory()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SetTextInternal(Builtins.Debug.ListKeyHistory(), MainFocusedTab.History, txtHistory, true);
			});
			return DefaultObject;
		}

		internal object ShowInternalVars(bool showTab)
		{
			// Snapshot the running function's locals on this (script) thread before the async UI hop; the scope lives on
			// this thread's call stack, so the UI thread would otherwise see no executing-function scope.
			var execScope = CallStack.Current.ExecutionScope;
			var execLocals = execScope?.Enumerate().ToList();
			var execName = execScope?.Name;
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SetTextInternal(Builtins.Debug.GetVars(null, execLocals, execName), MainFocusedTab.Vars, txtVars, showTab);
			});
			return DefaultObject;
		}

		/// <summary>
		/// Regenerates a tab's contents from the live data it mirrors.
		/// </summary>
		internal void RefreshTab(MainFocusedTab tab)
		{
			switch (tab)
			{
				case MainFocusedTab.Vars: _ = ShowInternalVars(false); break;

				case MainFocusedTab.Hotkeys: _ = ListHotkeys(); break;

				case MainFocusedTab.History: _ = ShowHistory(); break;

				//Show any OutputDebug text accumulated while the window was hidden or not yet shown.
				default: OwnerScript.DebugOutput.Refresh(); break;
			}
		}

		/// <summary>
		/// Regenerates the contents of the tab the user is currently looking at.
		/// </summary>
		internal void RefreshSelectedTab() => RefreshTab(GetFocusedTab(tcMain.SelectedTab));

		/// <summary>
		/// Brings a tab into view on behalf of the code that is filling it in, rather than on behalf of the
		/// user. Refreshing it again here would immediately overwrite the text being shown -- and for the
		/// Vars tab it would also drop the calling function's locals, which only the script thread can see.
		/// </summary>
		private void SelectTab(TabPage page)
		{
			if (page == null)
				return;

			selectingTab = true;

			try
			{
				tcMain.SelectedTab = page;
			}
			finally
			{
				selectingTab = false;
			}
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);

			//Opening the window (tray menu, WinShow(), restoring it) must show current data, rather than
			//whatever happened to be generated the last time a View menu item was clicked.
			if (Visible && !IsClosing && IsHandleCreated)
				RefreshSelectedTab();
		}

		protected override void WndProc(ref Message m)
		{
			// Script.Dispose closes the form by sending a nested WM_CLOSE after the exit is decided.
			// Let WinForms finish that close; the outer request returns after disposal below.
			if (m.Msg == WindowsAPI.WM_CLOSE && (OwnerScript.hasExited || OwnerScript.IsDisposed))
			{
				base.WndProc(ref m);
				return;
			}

			var systemCommand = m.Msg == WindowsAPI.WM_SYSCOMMAND ? m.WParam.ToInt64() & 0xFFF0 : 0;
			var menuCommand = (MenuCommand)(m.WParam.ToInt64() & 0xFFFF);
			var isMenuCommand = m.Msg == WindowsAPI.WM_COMMAND && m.LParam == 0
				&& (m.WParam.ToInt64() >> 16 & 0xFFFF) <= 1 && Enum.IsDefined(menuCommand);

			// These messages are handled here without reaching KeysharpForm.WndProc. A queued message has
			// already visited the filter; a synchronous one still needs its OnMessage callbacks.
			if ((m.Msg == WindowsAPI.WM_CLOSE || m.Msg == WmQueryEndSession || m.Msg == WindowsAPI.WM_ENDSESSION
					|| systemCommand == WindowsAPI.SC_CLOSE || isMenuCommand || m.Msg == (int)UserMessages.AHK_NOTIFYICON)
					&& OwnerScript.msgFilter?.MessageClaimed(ref m) == true)
				return;

			if ((isMenuCommand || m.Msg == (int)UserMessages.AHK_NOTIFYICON)
				&& (OwnerScript.IsDisposed || OwnerScript.hasExited))
			{
				m.Result = 0;
				return;
			}

			if (isMenuCommand)
			{
				HandleMenuCommand(menuCommand);
				m.Result = 0;
				return;
			}

			switch (m.Msg)
			{
				case (int)UserMessages.AHK_NOTIFYICON:
					OwnerScript.trayMessageWindow?.DispatchNotification(m.LParam);
					m.Result = 0;
					return;

				case WindowsAPI.WM_CLIPBOARDUPDATE:
					if (clipSuccess)
						ClipboardUpdate?.Invoke(null);

					break;

				case WmQueryEndSession:
					// The session can still be cancelled by another application. Let Windows decide before exiting.
					m.Result = 1;
					return;

				case WindowsAPI.WM_ENDSESSION:
					if (m.WParam != 0)
						_ = Keysharp.Internals.Flow.ExitAppInternal(OwnerScript,
							(m.LParam.ToInt64() & WindowsAPI.ENDSESSION_LOGOFF) != 0
								? Keysharp.Builtins.Flow.ExitReasons.Logoff
								: Keysharp.Builtins.Flow.ExitReasons.Shutdown, null, false);

					m.Result = 0;
					return;

				// The user's close command hides the main window, as in AHK, leaving the
				// script running. A direct WM_CLOSE still means a request to exit.
				case WindowsAPI.WM_SYSCOMMAND when systemCommand == WindowsAPI.SC_CLOSE:
					Hide();
					m.Result = 0;
					return;

				// A close that did not come from the window's own chrome: a script's WinClose, a tool ending the
				// task, or a Reload's replacement. As in AHK it exits, for the reason the sender put in wParam.
				case WindowsAPI.WM_CLOSE:
					_ = Keysharp.Internals.Flow.ExitAppInternal(OwnerScript,
							m.WParam == ReloadHandshake.ExitByReload ? Keysharp.Builtins.Flow.ExitReasons.Reload : Keysharp.Builtins.Flow.ExitReasons.Close,
							null, false);

					m.Result = 0;
					return;//A veto leaves it open; an accepted exit already closed it during Script.Dispose.

				// WM_HOTKEY is delivery for OS-registered (RegisterHotKey) hotkeys, which is Windows-only. On Linux/macOS
				// there is no equivalent OS facility; hotkeys are instead delivered through the keyboard hook (HookThread),
				// so no cross-platform mechanism is needed here.
				case WindowsAPI.WM_HOTKEY:
					_ = OwnerScript.HookThread.PostMessage(new KeysharpMsg()
					{
						hwnd = m.HWnd,//Unused, but probably still good to assign.
						message = WindowsAPI.WM_HOTKEY,
						wParam = m.WParam,
						lParam = m.LParam,
					});
					break;
			}

			base.WndProc(ref m);
		}

		private void SendMenuCommand(MenuCommand command) =>
			WindowsAPI.SendMessage(Handle, WindowsAPI.WM_COMMAND, (nint)command, 0);

		private void HandleMenuCommand(MenuCommand command)
		{
			if (command >= MenuCommand.TrayOpen && command <= MenuCommand.TrayAccessibilitySpy
				&& OwnerScript.trayMenu?.InvokeStandardCommand(command) == true)
				return;

			switch (command)
			{
				case MenuCommand.TrayOpen:
					if (A_AllowMainWindow.Ab())
					{
						AllowShowDisplay = true;
						WindowState = lastWindowState == FormWindowState.Minimized ? FormWindowState.Normal : lastWindowState;
						Show();
						BringToFront();
						Focus();
						RefreshSelectedTab();
					}
					break;

				case MenuCommand.TrayReload:
				case MenuCommand.FileReload: Keysharp.Builtins.Flow.Reload(); break;
				case MenuCommand.TrayEdit:
				case MenuCommand.FileEdit: Builtins.Debug.Edit(); break;
				case MenuCommand.TrayWindowSpy:
				case MenuCommand.FileWindowSpy: LaunchWindowSpy(); break;
				case MenuCommand.TrayPause:
				case MenuCommand.FilePause: OwnerScript.LaunchTogglePause(); break;
				case MenuCommand.TraySuspend:
				case MenuCommand.FileSuspend: Script.SuspendHotkeys(); break;
				case MenuCommand.TrayExit:
				case MenuCommand.FileExit:
					_ = Keysharp.Internals.Flow.ExitAppInternal(OwnerScript, Keysharp.Builtins.Flow.ExitReasons.Menu, null, false);
					break;

				case MenuCommand.ViewVariables: ShowInternalVars(true); break;
				case MenuCommand.ViewHotkeys: ListHotkeys(); break;
				case MenuCommand.ViewKeyHistory: ShowHistory(); break;
				case MenuCommand.ViewRefresh: RefreshSelectedTab(); break;
				case MenuCommand.ViewClearDebugLog: OwnerScript.DebugOutput.Clear(); break;
				case MenuCommand.TrayHelp:
				case MenuCommand.HelpWebsite: Processes.Run("https://github.com/keysharp-org/Keysharp"); break;
				case MenuCommand.HelpUserManual: Dialogs.MsgBox("This feature is not implemented"); break;
				case MenuCommand.HelpAbout: ShowAbout(); break;
			}
		}

		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
			=> SendMenuCommand(MenuCommand.HelpAbout);

		private void ShowAbout()
		{
			if (about == null)
			{
				about = new AboutBox();
				about.FormClosing += (ss, ee) => about = null;
			}

			about.Show();
		}

		private void clearDebugLogToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.ViewClearDebugLog);

		private void editScriptToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.FileEdit);

		private void exitToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.FileExit);

		private MainFocusedTab GetFocusedTab(TabPage page)
		{

			return page == tpVars ? MainFocusedTab.Vars
				 : page == tpHotkeys ? MainFocusedTab.Hotkeys
				 : page == tpHistory ? MainFocusedTab.History
				 : MainFocusedTab.Debug;
		}

		private TabPage GetTab(MainFocusedTab tab)
		{

			return tab switch
			{
					MainFocusedTab.Debug => tpDebug,
					MainFocusedTab.Vars => tpVars,
					MainFocusedTab.Hotkeys => tpHotkeys,
					MainFocusedTab.History => tpHistory,
					_ => tpDebug,
			};
		}

		private TextBox GetText(MainFocusedTab tab)
		{

				return tab switch
			{
					MainFocusedTab.Debug => txtDebug,
					MainFocusedTab.Vars => txtVars,
					MainFocusedTab.Hotkeys => txtHotkeys,
					MainFocusedTab.History => txtHistory,
					_ => txtDebug,
			};
		}

		private void hotkeysAndTheirMethodsToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.ViewHotkeys);

		private void keyHistoryAndScriptInfoToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.ViewKeyHistory);

		/// <summary>
		/// This will get called if the user manually closes the main window,
		/// or if ExitApp() is called from somewhere within the code, which will also close the main window.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (!OwnerScript.IsDisposed && OwnerScript.FlowData.exitReason == null && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				this.Hide();
				return;
			}

			IsClosing = true;

			if (Keysharp.Internals.Flow.ExitAppInternal(OwnerScript, Keysharp.Builtins.Flow.ExitReasons.Close, null, false))
			{
				IsClosing = false;
				e.Cancel = true;
				return;
			}

			if (clipSuccess)
				_ = WindowsAPI.RemoveClipboardFormatListener(Handle);

			about?.Close();
		}

		private void MainWindow_Load(object sender, EventArgs e)
		{
			Visible = false;
			WindowState = FormWindowState.Minimized;
		}

		private void MainWindow_Shown(object sender, EventArgs e)
		{
		}

		private void MainWindow_SizeChanged(object sender, EventArgs e)
		{
			//Cannot call ShowInTaskbar at all here because it causes a full re-creation of the window.
			//So anything that previously used the window handle, including hotkeys, will no longer work.
			if (WindowState == FormWindowState.Minimized)
				this.Hide();
			else
				lastWindowState = WindowState;
		}

		private void pauseScriptToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.FilePause);

		private void refreshToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.ViewRefresh);

		private void reloadScriptToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.FileReload);

		private void SetTextInternal(string text, MainFocusedTab tab, TextBox txt, bool focus)
		{
			//This can sometimes scroll the textbox on each update due to a fractional line being displayed.
			//This is an artifact of how the Winforms textbox works. You can see this by sizing the window
			//such that pressing F5 in the Vars tab keeps scrolling the textbox.
			//Then, click on the last line of text, you will see it scroll one line each time you click.
			var lineHeight = TextRenderer.MeasureText("X", txtVars.Font).Height;
			var linesPerPage = (double)txt.ClientSize.Height / lineHeight;
			var oldCharIndex = txt.GetCharIndexFromPosition(new Point(0, 0));
			var oldLineIndex = txt.GetLineFromCharIndex(oldCharIndex);
			SetText(text, tab, focus);
			var newCharIndex = oldLineIndex == 0 ? 0 : txt.GetFirstCharIndexFromLine(Math.Max(0, oldLineIndex + (int)linesPerPage));
			//txtDebug.Text += $"lineHeight: {lineHeight}, linesPerPage: {linesPerPage}, oldCharIndex: {oldCharIndex}, oldLineIndex: {oldLineIndex}, newCharIndex: {newCharIndex}\r\n";
			//This must be done with BeginInvoke() or else it won't reposition the scroll bars.
			_ = this.BeginInvoke(() =>
			{
				txt.Select(Math.Max(0, newCharIndex), 0);
				txt.ScrollToCaret();
			});
		}

		private void ShowIfNeeded()
		{
			if (!AllowShowDisplay || WindowState == FormWindowState.Minimized)
			{
				AllowShowDisplay = true;
				Show();
				BringToFront();
				WindowState = FormWindowState.Normal;
			}
		}

		private void suspendHotkeysToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.FileSuspend);

		private void userManualToolStripMenuItem_Click(object sender, EventArgs e)
			=> SendMenuCommand(MenuCommand.HelpUserManual);

		private void variablesAndTheirContentsToolStripMenuItem_Click(object sender, EventArgs e) => SendMenuCommand(MenuCommand.ViewVariables);

		private void windowSpyToolStripMenuItem_Click(object sender, EventArgs e)
			=> SendMenuCommand(MenuCommand.FileWindowSpy);

		private void LaunchWindowSpy()
		{
			var path = Path.GetDirectoryName(A_AhkPath);
			var exe = path + "/Keysharp.exe";
			var spyCompiled = path + "/Scripts/WindowSpy.cks";
			var opt = File.Exists(spyCompiled) ? spyCompiled : path + "/Scripts/WindowSpy.ks";//Prefer the precompiled .cks for faster startup.
			object pid = VarRef.Empty;
			//Keysharp.Builtins.Dialogs.MsgBox(exe + "\r\n" + path + "\r\n" + opt);
			_ = Processes.Run("\"" + exe + "\"", path, "", pid, "\"" + opt + "\"");
		}

		public enum MainFocusedTab
		{
			Debug,
			Vars,
			Hotkeys,
			History
		}

		public event VariadicAction ClipboardUpdate;
	}

	/// <summary>
	/// Text boxes have a long standing behavior which is undesirable.
	/// They select all text whenever they get the focus.
	/// In order to prevent that, make a small derivation to do
	/// nothing on focus.
	/// https://github.com/dotnet/winforms/issues/5406
	/// </summary>
	internal class NonFocusTextBox : TextBox
	{
		protected override void OnGotFocus(EventArgs e)
		{
			return;
		}
	}
}
