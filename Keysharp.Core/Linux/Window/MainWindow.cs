#if !WINDOWS
namespace Keysharp.Scripting
{
	public class MainWindow : KeysharpForm
	{
		private readonly ToolStripMenuItem suspendHotkeysToolStripMenuItem = new ("&Suspend Hotkeys");
		private readonly Eto.Forms.MenuBar mainMenu = new ();
		private CheckMenuItem suspendHotkeysMenuItem;

		private readonly TabControl tcMain = new ();
		private readonly TabPage tpDebug = new () { Text = "Debug" };
		private readonly TabPage tpVars = new () { Text = "Vars" };
		private readonly TabPage tpHotkeys = new () { Text = "Hotkeys" };
		private readonly TabPage tpHistory = new () { Text = "History" };
		private readonly TextArea txtDebug = new () { Font = SystemFonts.Default(10), Wrap = false };
		private readonly TextArea txtVars = new () { Font = SystemFonts.Default(10), Wrap = false };
		private readonly TextArea txtHotkeys = new () { Font = SystemFonts.Default(10), Wrap = false };
		private readonly TextArea txtHistory = new () { Font = SystemFonts.Default(10), Wrap = false };

		public static Font OurDefaultFont = SystemFonts.Default(8F);
		internal FormWindowState lastWindowState = FormWindowState.Normal;
		private AboutBox about;
		private bool callingInternalVars = false;

		//private static Gdk.Atom clipAtom = Gdk.Atom.Intern("CLIPBOARD", false);
		//private Gtk.Clipboard gtkClipBoard = Gtk.Clipboard.Get(clipAtom);

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsClosing { get; private set; }

		internal ToolStripMenuItem SuspendHotkeysToolStripMenuItem => suspendHotkeysToolStripMenuItem;

		public MainWindow()
		{
			Title = "Keysharp";
			ShowInTaskbar = true;
			ClientSize = new Size(843, 535);
			BuildUi();

			Closing += MainWindow_Closing;
			Shown += MainWindow_Shown;
			SizeChanged += MainWindow_SizeChanged;
		}

		private void BuildUi()
		{
			BuildMenus();
			tpDebug.Content = txtDebug;
			tpVars.Content = txtVars;
			tpVars.Click += variablesAndTheirContentsToolStripMenuItem_Click;
			tpHotkeys.Content = txtHotkeys;
			tpHotkeys.Click += hotkeysAndTheirMethodsToolStripMenuItem_Click;
			tpHistory.Content = txtHistory;
			tpHistory.Click += keyHistoryAndScriptInfoToolStripMenuItem_Click;

			tcMain.Pages.Add(tpDebug);
			tcMain.Pages.Add(tpVars);
			tcMain.Pages.Add(tpHotkeys);
			tcMain.Pages.Add(tpHistory);

			Content = tcMain;
		}

		private void BuildMenus()
		{
			var fileMenu = new ButtonMenuItem { Text = "&File" };
			var reloadScriptItem = new ButtonMenuItem { Text = "&Reload Script" };
			var editScriptItem = new ButtonMenuItem { Text = "&Edit Script", Enabled = !A_IsCompiled };
			var windowSpyItem = new ButtonMenuItem { Text = "&Window Spy" };
			suspendHotkeysMenuItem = new CheckMenuItem { Text = "&Suspend Hotkeys" };
			var exitItem = new ButtonMenuItem { Text = "E&xit" };

			reloadScriptItem.Click += reloadScriptToolStripMenuItem_Click;
			editScriptItem.Click += editScriptToolStripMenuItem_Click;
			windowSpyItem.Click += windowSpyToolStripMenuItem_Click;
			suspendHotkeysMenuItem.Click += (_, _) =>
			{
				suspendHotkeysToolStripMenuItem_Click(this, EventArgs.Empty);
				suspendHotkeysMenuItem.Checked = suspendHotkeysToolStripMenuItem.Checked;
			};
			exitItem.Click += exitToolStripMenuItem_Click;

			suspendHotkeysToolStripMenuItem.CheckedChanged += (_, _) =>
			{
				if (suspendHotkeysMenuItem != null)
					suspendHotkeysMenuItem.Checked = suspendHotkeysToolStripMenuItem.Checked;
			};

			suspendHotkeysMenuItem.Checked = suspendHotkeysToolStripMenuItem.Checked;

			fileMenu.Items.Add(reloadScriptItem);
			fileMenu.Items.Add(editScriptItem);
			fileMenu.Items.Add(windowSpyItem);
			fileMenu.Items.Add(new SeparatorMenuItem());
			fileMenu.Items.Add(suspendHotkeysMenuItem);
			fileMenu.Items.Add(exitItem);

			var viewMenu = new ButtonMenuItem { Text = "&View" };
			var varsItem = new ButtonMenuItem { Text = "&Variables and their contents" };
			var hotkeysItem = new ButtonMenuItem { Text = "&Hotkeys and their methods" };
			var historyItem = new ButtonMenuItem { Text = "&Key history and script info" };
			var clearDebugItem = new ButtonMenuItem { Text = "&Clear debug log" };
			var refreshItem = new ButtonMenuItem { Text = "&Refresh" };

			varsItem.Click += variablesAndTheirContentsToolStripMenuItem_Click;
			hotkeysItem.Click += hotkeysAndTheirMethodsToolStripMenuItem_Click;
			historyItem.Click += keyHistoryAndScriptInfoToolStripMenuItem_Click;
			clearDebugItem.Click += clearDebugLogToolStripMenuItem_Click;
			refreshItem.Click += refreshToolStripMenuItem_Click;

			viewMenu.Items.Add(varsItem);
			viewMenu.Items.Add(hotkeysItem);
			viewMenu.Items.Add(historyItem);
			viewMenu.Items.Add(clearDebugItem);
			viewMenu.Items.Add(new SeparatorMenuItem());
			viewMenu.Items.Add(refreshItem);

			var helpMenu = new ButtonMenuItem { Text = "&Help" };
			var userManualItem = new ButtonMenuItem { Text = "&User Manual" };
			var aboutItem = new ButtonMenuItem { Text = "&About" };

			userManualItem.Click += userManualToolStripMenuItem_Click;
			aboutItem.Click += aboutToolStripMenuItem_Click;

			helpMenu.Items.Add(userManualItem);
			helpMenu.Items.Add(aboutItem);

			mainMenu.Items.Clear();
			mainMenu.Items.Add(fileMenu);
			mainMenu.Items.Add(viewMenu);
			mainMenu.Items.Add(helpMenu);

			Menu = mainMenu;
		}

		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (about == null)
			{
				about = new AboutBox();
				about.Closed += (_, __) => about = null;
			}

			about.Show();
		}

		public void AddText(string s, MainFocusedTab tab, bool focus)
		{
			//Use CheckedBeginInvoke() because CheckedInvoke() seems to crash if this is called right as the window is closing.
			//Such as with a hotkey that prints on mouse click, which will cause a print when the X is clicked to close.
			this.CheckedBeginInvoke(() =>
			{
				GetText(tab).Append($"{s.ReplaceLineEndings(Environment.NewLine)}");//This should scroll to the bottom, if not, try this:
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

		private TextArea GetText(MainFocusedTab tab)
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

		//private void gtkClipBoard_OwnerChange(object o, Gtk.OwnerChangeArgs args)
		//{
		//  ClipboardUpdate?.Invoke(null);
		//}

		private void MainWindow_Shown(object sender, EventArgs e)
		{
			RectangleF area;
			try
			{
				area = Eto.Forms.Screen.PrimaryScreen.WorkingArea;
			}
			catch
			{
				area = Eto.Forms.Screen.DisplayBounds;
			}
			Location = new Point(
				(int)(area.X + (area.Width - Size.Width) / 2),
				(int)(area.Y + (area.Height - Size.Height) / 2)
			);

			beenShown = false;
			if (!AllowShowDisplay)
			{
				this.BeginInvoke(() => {
					Visible = false;
					WindowState = WindowState.Minimized;
				});
			}
			beenShown = true;

			if (AllowShowDisplay && Visible)
				_ = ShowInternalVars(false);
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

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			IsClosing = true;

			if (Flow.ExitAppInternal(Flow.ExitReasons.Close, null, false))
			{
				IsClosing = false;
				e.Cancel = true;
				return;
			}
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

		public void SetTextInternal(string s, MainFocusedTab tab, TextArea txt, bool focus)
		{
			_ = this.BeginInvoke(() =>
			{
				GetText(tab).Text = s.ReplaceLineEndings(Environment.NewLine);
				if (focus)
					tcMain.SelectedTab = GetTab(tab);
			});
		}

		private void ShowIfNeeded()
		{
			if (beenShown && (!AllowShowDisplay || WindowState == WindowState.Minimized))
			{
				AllowShowDisplay = true;
				Show();
				Visible = true;
				BringToFront();
				WindowState = WindowState.Normal;
				
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
			var exe = path + "/Keysharp";
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
}
#endif
