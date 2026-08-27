using Keysharp.Builtins;
namespace Keysharp.Internals.UI.Windows
{
	public partial class MainWindow : KeysharpForm
	{
		public static Font OurDefaultFont = new ("MS Shell Dlg", 8F);
		internal FormWindowState lastWindowState = FormWindowState.Normal;
		private readonly bool clipSuccess;
		private AboutBox about;
		private bool selectingTab;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsClosing { get; private set; }

		internal static void AppendDebugOutput(string text, bool clear) => Internals.UI.DebugOutputBuffer.Append(text, clear);

		internal static void ResetDebugOutputBuffer() => Internals.UI.DebugOutputBuffer.Reset();

		internal static void ResetDebugOutputFlush() => Internals.UI.DebugOutputBuffer.ResetFlush();

		internal ToolStripMenuItem SuspendHotkeysToolStripMenuItem => suspendHotkeysToolStripMenuItem;

		internal MainWindow(Script owner) : base(owner)
		{
			InitializeComponent();
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

				// Flush any OutputDebug text accumulated while the window was hidden/not yet
				// shown -- otherwise it only appears once another OutputDebug call comes in.
				AppendDebugOutput(string.Empty, false);
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
			// Snapshot the running function's locals on THIS (script) thread before the async UI hop; the scope is
			// [ThreadStatic], so the UI thread has its own (null) value and would otherwise see no executing-function scope.
			var execScope = Script.executingUserFunc;
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

				//Flush any OutputDebug text accumulated while the window was hidden/not yet
				//shown -- otherwise it only appears once another OutputDebug call comes in.
				default: AppendDebugOutput(string.Empty, false); break;
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
			switch (m.Msg)
			{
				case WindowsAPI.WM_CLIPBOARDUPDATE:
					if (clipSuccess)
						ClipboardUpdate?.Invoke(null);

					break;

				case WindowsAPI.WM_ENDSESSION:
					_ = Keysharp.Internals.Flow.ExitAppInternal(OwnerScript, (m.Msg & WindowsAPI.ENDSESSION_LOGOFF) != 0 ? Keysharp.Builtins.Flow.ExitReasons.LogOff : Keysharp.Builtins.Flow.ExitReasons.Shutdown, null, false);
					break;

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

		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (about == null)
			{
				about = new AboutBox();
				about.FormClosing += (ss, ee) => about = null;
			}

			about.Show();
		}

		private void clearDebugLogToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtDebug.Text = "";
			AppendDebugOutput(string.Empty, true);
		}

		private void editScriptToolStripMenuItem_Click(object sender, EventArgs e) => Builtins.Debug.Edit();

		private void exitToolStripMenuItem_Click(object sender, EventArgs e) => _ = Keysharp.Internals.Flow.ExitAppInternal(OwnerScript, Keysharp.Builtins.Flow.ExitReasons.Menu, null, false);

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

		private void hotkeysAndTheirMethodsToolStripMenuItem_Click(object sender, EventArgs e) => ListHotkeys();

		private void keyHistoryAndScriptInfoToolStripMenuItem_Click(object sender, EventArgs e) => ShowHistory();

		/// <summary>
		/// This will get called if the user manually closes the main window,
		/// or if ExitApp() is called from somewhere within the code, which will also close the main window.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (string.IsNullOrEmpty(A_ExitReason as string) && e.CloseReason == CloseReason.UserClosing)
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

		private void refreshToolStripMenuItem_Click(object sender, EventArgs e) => RefreshSelectedTab();

		private void reloadScriptToolStripMenuItem_Click(object sender, EventArgs e) => Keysharp.Builtins.Flow.Reload();

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

		private void suspendHotkeysToolStripMenuItem_Click(object sender, EventArgs e) => Script.SuspendHotkeys();

		private void userManualToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_ = Dialogs.MsgBox("This feature is not implemented");
		}

		private void variablesAndTheirContentsToolStripMenuItem_Click(object sender, EventArgs e) => ShowInternalVars(true);

		private void windowSpyToolStripMenuItem_Click(object sender, EventArgs e)
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
