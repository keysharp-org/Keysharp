#if WINDOWS
namespace Keysharp.Scripting
{
	public partial class MainWindow : KeysharpForm
	{
		public static Font OurDefaultFont = new ("MS Shell Dlg", 8F);
		internal FormWindowState lastWindowState = FormWindowState.Normal;
		private readonly bool clipSuccess;
		private AboutBox about;
		private bool callingInternalVars = false;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsClosing { get; private set; }

		internal ToolStripMenuItem SuspendHotkeysToolStripMenuItem => suspendHotkeysToolStripMenuItem;

		public MainWindow()
		{
			InitializeComponent();
			//FormBorderStyle = FormBorderStyle.SizableToolWindow;
			SetStyle(ControlStyles.StandardClick, true);
			SetStyle(ControlStyles.StandardDoubleClick, true);
			SetStyle(ControlStyles.EnableNotifyMessage, true);
			clipSuccess = WindowsAPI.AddClipboardFormatListener(Handle);//Need a cross platform way to do this.//TODO
			tpVars.HandleCreated += TpVars_HandleCreated;
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
				{
					var sel = GetTab(tab);

					if (sel != null)
						tcMain.SelectedTab = sel;
				}
			}, false, false);
		}

		public void ClearText(MainFocusedTab tab) => SetText(string.Empty, tab, false);

		public void SetText(string s, MainFocusedTab tab, bool focus)
		{
			_ = this.BeginInvoke(() => //These need to be BeginInvoke(), otherwise they can freeze if called within a COM event.
			{
				GetText(tab).Text = s.ReplaceLineEndings(Environment.NewLine);

				if (focus)
				{
					var sel = GetTab(tab);

					if (sel != null)
						tcMain.SelectedTab = sel;
				}
			});
		}

		internal object ListHotkeys()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SetTextInternal(HotkeyDefinition.GetHotkeyDescriptions(), MainFocusedTab.Hotkeys, txtHotkeys, true);
			});
			return DefaultObject;
		}

		internal object ShowDebug()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				tcMain.SelectedTab = tpDebug;
			});
			return DefaultObject;
		}

		internal object ShowHistory()
		{
			_ = this.BeginInvoke(() =>
			{
				ShowIfNeeded();
				SetTextInternal(Core.Debug.ListKeyHistory(), MainFocusedTab.History, txtHistory, true);
			});
			return DefaultObject;
		}

		internal object ShowInternalVars(bool showTab)
		{
			callingInternalVars = true;//Gets called twice if called before first showing.
			_ = this.BeginInvoke(() =>
			{
				try
				{
					ShowIfNeeded();
					SetTextInternal(Core.Debug.GetVars(), MainFocusedTab.Vars, txtVars, showTab);
				}
				finally
				{
					callingInternalVars = false;
				}
			});
			return DefaultObject;
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
					_ = Flow.ExitAppInternal((m.Msg & WindowsAPI.ENDSESSION_LOGOFF) != 0 ? Flow.ExitReasons.LogOff : Flow.ExitReasons.Shutdown, null, false);
					break;

				case WindowsAPI.WM_HOTKEY://We will need to find a cross platform way to do this. At the moment, hotkeys appear to be a built in feature in Windows.//TODO
					_ = Script.TheScript.HookThread.channel.Writer.TryWrite(new KeysharpMsg()
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

		private void clearDebugLogToolStripMenuItem_Click(object sender, EventArgs e) => txtDebug.Text = "";

		private void editScriptToolStripMenuItem_Click(object sender, EventArgs e) => Core.Debug.Edit();

		private void exitToolStripMenuItem_Click(object sender, EventArgs e) => _ = Flow.ExitAppInternal(Flow.ExitReasons.Menu, null, false);

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

			if (Flow.ExitAppInternal(Flow.ExitReasons.Close, null, false))
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

		private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (tcMain.SelectedTab == tpVars)
				_ = ShowInternalVars(true);
			else if (tcMain.SelectedTab == tpHotkeys)
				_ = ListHotkeys();
			else if (tcMain.SelectedTab == tpHistory)
				_ = ShowHistory();
		}

		private void reloadScriptToolStripMenuItem_Click(object sender, EventArgs e) => Flow.Reload();

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

		private void TpVars_HandleCreated(object sender, EventArgs e)
		{
			if (!callingInternalVars)
				_ = ShowInternalVars(false);
		}

		private void userManualToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_ = Dialogs.MsgBox("This feature is not implemented");
		}

		private void variablesAndTheirContentsToolStripMenuItem_Click(object sender, EventArgs e) => ShowInternalVars(true);

		private void windowSpyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var path = Path.GetDirectoryName(A_KeysharpPath);
			var exe = path + "/Keysharp.exe";
			var opt = path + "/Scripts/WindowSpy.ks";
			object pid = VarRef.Empty;
			//Keysharp.Core.Dialogs.MsgBox(exe + "\r\n" + path + "\r\n" + opt);
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
#endif