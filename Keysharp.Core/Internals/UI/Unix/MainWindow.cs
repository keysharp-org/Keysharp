using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.UI.Unix
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
		// Conversions.ScaleFontSize scales the base point size on macOS (no-op elsewhere) so these debug-window
		// text areas match the other platforms visually. Safe here because instance initializers run at
		// construction, after Eto's platform is up.
		private readonly TextArea txtDebug = new () { Font = SystemFonts.Default(Conversions.ScaleFontSize(10F)), Wrap = false };
		private readonly TextArea txtVars = new () { Font = SystemFonts.Default(Conversions.ScaleFontSize(10F)), Wrap = false };
		private readonly TextArea txtHotkeys = new () { Font = SystemFonts.Default(Conversions.ScaleFontSize(10F)), Wrap = false };
		private readonly TextArea txtHistory = new () { Font = SystemFonts.Default(Conversions.ScaleFontSize(10F)), Wrap = false };

		private static Font ourDefaultFont;

		// Lazily initialized: SystemFonts.Default() requires Eto's platform to already be detected/running,
		// which is not the case at MainWindow's static-initialization time in headless/test contexts
		// (referencing it eagerly as a static field initializer throws NullReferenceException there).
		// ScaleFontSize scales the 8pt base on macOS (~11pt) to match other platforms; no-op elsewhere.
		public static Font OurDefaultFont => ourDefaultFont ??= SystemFonts.Default(Conversions.ScaleFontSize(8F));

		internal static void AppendDebugOutput(string text, bool clear) => Internals.UI.DebugOutputBuffer.Append(text, clear);

		internal static void ResetDebugOutputBuffer() => Internals.UI.DebugOutputBuffer.Reset();

		internal static void ResetDebugOutputFlush() => Internals.UI.DebugOutputBuffer.ResetFlush();

		internal FormWindowState lastWindowState = FormWindowState.Normal;
		private AboutBox about;
		private int clipboardMonitoringEnabled;
		private long clipboardMonitoringGeneration;
		private bool selectingTab;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsClosing { get; private set; }

		internal ToolStripMenuItem SuspendHotkeysToolStripMenuItem => suspendHotkeysToolStripMenuItem;

		internal MainWindow(Script owner) : base(owner)
		{
			Title = "Keysharp";
			ShowInTaskbar = true;
			ClientSize = new Size(843, 535);
			BuildUi();

			Closing += MainWindow_Closing;
			Shown += MainWindow_Shown;
			SizeChanged += MainWindow_SizeChanged;
		}

		internal void InitializeHidden()
		{
#if LINUX
			// Realize the GTK window so it has an X11 handle without mapping or activating it.
			if (this.ToNative() is Gtk.Window native)
				native.Realize();
#endif
			_ = this.Handle;
			beenShown = true;
		}

		private void BuildUi()
		{
			BuildMenus();
			tpDebug.Content = txtDebug;
			tpVars.Content = txtVars;
			tpHotkeys.Content = txtHotkeys;
			tpHistory.Content = txtHistory;

			tcMain.Pages.Add(tpDebug);
			tcMain.Pages.Add(tpVars);
			tcMain.Pages.Add(tpHotkeys);
			tcMain.Pages.Add(tpHistory);
			//Every tab shows a snapshot of live data (variables, hotkeys, key history, buffered debug output),
			//so it's regenerated whenever the user brings that tab into view, not just when a View menu item
			//is clicked. Without this, switching tabs in a freshly opened window shows empty text boxes.
			tcMain.SelectedIndexChanged += (_, _) =>
			{
				if (!selectingTab)
					RefreshSelectedTab();
			};

			Content = tcMain;
		}

		private void BuildMenus()
		{
			var fileMenu = new ButtonMenuItem { Text = "&File" };
			var reloadScriptItem = new ButtonMenuItem { Text = "&Reload Script", Shortcut = Eto.Forms.Application.Instance.CommonModifier | Eto.Forms.Keys.R };
			//Hidden rather than disabled, to match the WinForms main window.
			var editScriptItem = new ButtonMenuItem { Text = "&Edit Script", Shortcut = Eto.Forms.Application.Instance.CommonModifier | Eto.Forms.Keys.E, Visible = !A_IsCompiled };
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
			var varsItem = new ButtonMenuItem { Text = "&Variables and their contents", Shortcut = Eto.Forms.Application.Instance.CommonModifier | Eto.Forms.Keys.V };
			var hotkeysItem = new ButtonMenuItem { Text = "&Hotkeys and their methods", Shortcut = Eto.Forms.Application.Instance.CommonModifier | Eto.Forms.Keys.H };
			var historyItem = new ButtonMenuItem { Text = "&Key history and script info", Shortcut = Eto.Forms.Application.Instance.CommonModifier | Eto.Forms.Keys.K };
			var clearDebugItem = new ButtonMenuItem { Text = "&Clear debug log" };
			var refreshItem = new ButtonMenuItem { Text = "&Refresh", Shortcut = Eto.Forms.Keys.F5 };

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
			var userManualItem = new ButtonMenuItem { Text = "&User Manual", Shortcut = Eto.Forms.Keys.F1 };
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
			_ = QueueUiUpdate(() =>
			{
				GetText(tab).Append($"{s.ReplaceLineEndings(Environment.NewLine)}");//This should scroll to the bottom, if not, try this:
				if (focus)
					SelectTab(GetTab(tab));
			});
		}

		public void ClearText(MainFocusedTab tab) => SetText(string.Empty, tab, false);

		public void SetText(string s, MainFocusedTab tab, bool focus)
		{
			_ = QueueUiUpdate(() => //These need to be BeginInvoke(), otherwise they can freeze if called within a COM event.
			{
				GetText(tab).Text = s.ReplaceLineEndings(Environment.NewLine);

				if (focus)
					SelectTab(GetTab(tab));
			});
		}

		internal object ListHotkeys()
		{
			_ = QueueUiUpdate(() =>
			{
				ShowIfNeeded();
				SetTextInternal(HotkeyDefinition.GetHotkeyDescriptions(OwnerScript), MainFocusedTab.Hotkeys, true);
			});
			return DefaultObject;
		}

		internal object ShowDebug()
		{
			_ = QueueUiUpdate(() =>
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
			_ = QueueUiUpdate(() =>
			{
				ShowIfNeeded();
				SetTextInternal(Builtins.Debug.ListKeyHistory(), MainFocusedTab.History, true);
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
			_ = QueueUiUpdate(() =>
			{
				ShowIfNeeded();
				SetTextInternal(Builtins.Debug.GetVars(null, execLocals, execName), MainFocusedTab.Vars, showTab);
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

		private void Clipboard_Changed(object sender, EventArgs e) => ClipboardUpdate?.Invoke(null);


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

				if (!AllowShowDisplay)
				{
					_ = QueueUiUpdate(() => {
						beenShown = false;
						// Hide directly instead of minimizing; minimizing creates a Dock/taskbar entry on macOS.
						WindowState = WindowState.Normal;
						this.Hide();
						beenShown = true;
					});
				}

			if (AllowShowDisplay && Visible)
				RefreshSelectedTab();
		}

		private void MainWindow_SizeChanged(object sender, EventArgs e)
		{
			//Cannot call ShowInTaskbar at all here because it causes a full re-creation of the window.
			//So anything that previously used the window handle, including hotkeys, will no longer work.
			if (WindowState == FormWindowState.Minimized)
			{
				this.Hide();
			}
			else
				lastWindowState = WindowState;
		}

		private void MainWindow_Closing(object sender, CancelEventArgs e)
		{
			if (Script.TheScript?.FlowData?.exitReason == null)
			{
				e.Cancel = true;
				this.Hide();
				return;
			}

			IsClosing = true;
			SetClipboardMonitoringEnabled(false);

			if (Keysharp.Internals.Flow.ExitAppInternal(OwnerScript, Keysharp.Builtins.Flow.ExitReasons.Close, null, false))
			{
				IsClosing = false;
				e.Cancel = true;
				return;
			}

			//An open About box would otherwise outlive its owner and keep the Eto application running.
			about?.Close();
		}

		private void refreshToolStripMenuItem_Click(object sender, EventArgs e) => RefreshSelectedTab();

		private void reloadScriptToolStripMenuItem_Click(object sender, EventArgs e) => Keysharp.Builtins.Flow.Reload();

		public void SetTextInternal(string s, MainFocusedTab tab, TextArea txt, bool focus)
		{
			SetTextInternal(s, tab, focus);
		}

		private void ShowIfNeeded()
		{
			if (ShouldSkipUiUpdate())
				return;

			if (beenShown && (!Visible || !AllowShowDisplay || WindowState == WindowState.Minimized))
			{
				AllowShowDisplay = true;
				Show();
				Visible = true;
				BringToFront();
				WindowState = WindowState.Normal;
			}
		}

		private void suspendHotkeysToolStripMenuItem_Click(object sender, EventArgs e) => Script.SuspendHotkeys();

		private bool QueueUiUpdate(Action action)
		{
			if (ShouldSkipUiUpdate())
				return false;

			_ = this.BeginInvoke(() =>
			{
				if (ShouldSkipUiUpdate())
					return;

				action();
			});
			return true;
		}

		private bool ShouldSkipUiUpdate() => IsClosing || IsDisposed || OwnerScript.hasExited;

		private void SetTextInternal(string s, MainFocusedTab tab, bool focus)
		{
			GetText(tab).Text = s.ReplaceLineEndings(Environment.NewLine);

			if (focus)
				SelectTab(GetTab(tab));
		}

		private void userManualToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_ = Dialogs.MsgBox("This feature is not implemented");
		}

		private void variablesAndTheirContentsToolStripMenuItem_Click(object sender, EventArgs e) => ShowInternalVars(true);

		private void windowSpyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var path = Path.GetDirectoryName(A_AhkPath);
			var exe = path + "/Keysharp";
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

		private IDisposable clipboardSub;   // clipboard-change subscription from the resolved backend

		internal void SetClipboardMonitoringEnabled(bool enabled)
		{
			if ((Volatile.Read(ref clipboardMonitoringEnabled) != 0) == enabled)
				return;

			var generation = Interlocked.Increment(ref clipboardMonitoringGeneration);
			Volatile.Write(ref clipboardMonitoringEnabled, enabled ? 1 : 0);

			if (enabled)
				// The resolved backend owns how changes are detected (Eto's Clipboard.Changed, or the Wayland
				// shell extension's signal). Its callback may arrive off the UI thread, so marshal onto it.
				clipboardSub = OwnerScript.Clipboard.Subscribe(() =>
				{
					if (!IsClipboardNotificationCurrent(generation))
						return;

					Eto.Forms.Application.Instance?.AsyncInvoke(() =>
					{
						if (!IsClipboardNotificationCurrent(generation))
							return;

						Clipboard_Changed(null, EventArgs.Empty);
					});
				});
			else
			{
				var sub = clipboardSub;
				clipboardSub = null;
				sub?.Dispose();
			}
		}

		private bool IsClipboardNotificationCurrent(long generation)
			=> Volatile.Read(ref clipboardMonitoringEnabled) != 0
			   && Volatile.Read(ref clipboardMonitoringGeneration) == generation
			   && !ShouldSkipUiUpdate();
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
