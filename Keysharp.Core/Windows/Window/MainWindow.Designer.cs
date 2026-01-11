namespace Keysharp.Scripting
{
	partial class MainWindow
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		///  Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}

			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			mainMenu = new System.Windows.Forms.MenuStrip();
			fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			reloadScriptToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			editScriptToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			windowSpyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			suspendHotkeysToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			viewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			variablesAndTheirContentsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			hotkeysAndTheirMethodsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			keyHistoryAndScriptInfoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			clearDebugLogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
			refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			userManualToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			tcMain = new System.Windows.Forms.TabControl();
			tpDebug = new System.Windows.Forms.TabPage();
			txtDebug = new Keysharp.Scripting.NonFocusTextBox();
			tpVars = new System.Windows.Forms.TabPage();
			txtVars = new Keysharp.Scripting.NonFocusTextBox();
			tpHotkeys = new System.Windows.Forms.TabPage();
			txtHotkeys = new Keysharp.Scripting.NonFocusTextBox();
			tpHistory = new System.Windows.Forms.TabPage();
			txtHistory = new Keysharp.Scripting.NonFocusTextBox();
			mainMenu.SuspendLayout();
			tcMain.SuspendLayout();
			tpDebug.SuspendLayout();
			tpVars.SuspendLayout();
			tpHotkeys.SuspendLayout();
			tpHistory.SuspendLayout();
			SuspendLayout();
			//
			// mainMenu
			//
			mainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { fileToolStripMenuItem, viewToolStripMenuItem, helpToolStripMenuItem });
			mainMenu.Location = new System.Drawing.Point(0, 0);
			mainMenu.Name = "mainMenu";
			mainMenu.Padding = new System.Windows.Forms.Padding(3, 1, 0, 1);
			mainMenu.Size = new System.Drawing.Size(843, 24);
			mainMenu.TabIndex = 2;
			mainMenu.Text = "MainMenu";
			//
			// fileToolStripMenuItem
			//
			fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { reloadScriptToolStripMenuItem, editScriptToolStripMenuItem, windowSpyToolStripMenuItem, toolStripSeparator1, suspendHotkeysToolStripMenuItem, exitToolStripMenuItem });
			fileToolStripMenuItem.Name = "fileToolStripMenuItem";
			fileToolStripMenuItem.Size = new System.Drawing.Size(37, 22);
			fileToolStripMenuItem.Text = "&File";
			//
			// reloadScriptToolStripMenuItem
			//
			reloadScriptToolStripMenuItem.Name = "reloadScriptToolStripMenuItem";
			reloadScriptToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.R;
			reloadScriptToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
			reloadScriptToolStripMenuItem.Text = "&Reload Script";
			reloadScriptToolStripMenuItem.Click += reloadScriptToolStripMenuItem_Click;
			//
			// editScriptToolStripMenuItem
			//
			editScriptToolStripMenuItem.Name = "editScriptToolStripMenuItem";
			editScriptToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.E;
			editScriptToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
			editScriptToolStripMenuItem.Text = "&Edit Script";
			editScriptToolStripMenuItem.Click += editScriptToolStripMenuItem_Click;
			//
			// windowSpyToolStripMenuItem
			//
			windowSpyToolStripMenuItem.Name = "windowSpyToolStripMenuItem";
			windowSpyToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
			windowSpyToolStripMenuItem.Text = "&Window Spy";
			windowSpyToolStripMenuItem.Click += windowSpyToolStripMenuItem_Click;
			//
			// toolStripSeparator1
			//
			toolStripSeparator1.Name = "toolStripSeparator1";
			toolStripSeparator1.Size = new System.Drawing.Size(181, 6);
			//
			// suspendHotkeysToolStripMenuItem
			//
			suspendHotkeysToolStripMenuItem.Name = "suspendHotkeysToolStripMenuItem";
			suspendHotkeysToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
			suspendHotkeysToolStripMenuItem.Text = "&Suspend Hotkeys";
			suspendHotkeysToolStripMenuItem.Click += suspendHotkeysToolStripMenuItem_Click;
			//
			// exitToolStripMenuItem
			//
			exitToolStripMenuItem.Name = "exitToolStripMenuItem";
			exitToolStripMenuItem.Size = new System.Drawing.Size(184, 22);
			exitToolStripMenuItem.Text = "E&xit";
			exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;
			//
			// viewToolStripMenuItem
			//
			viewToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { variablesAndTheirContentsToolStripMenuItem, hotkeysAndTheirMethodsToolStripMenuItem, keyHistoryAndScriptInfoToolStripMenuItem, clearDebugLogToolStripMenuItem, toolStripSeparator2, refreshToolStripMenuItem });
			viewToolStripMenuItem.Name = "viewToolStripMenuItem";
			viewToolStripMenuItem.Size = new System.Drawing.Size(44, 22);
			viewToolStripMenuItem.Text = "&View";
			//
			// variablesAndTheirContentsToolStripMenuItem
			//
			variablesAndTheirContentsToolStripMenuItem.Name = "variablesAndTheirContentsToolStripMenuItem";
			variablesAndTheirContentsToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.V;
			variablesAndTheirContentsToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
			variablesAndTheirContentsToolStripMenuItem.Text = "&Variables and their contents";
			variablesAndTheirContentsToolStripMenuItem.Click += variablesAndTheirContentsToolStripMenuItem_Click;
			//
			// hotkeysAndTheirMethodsToolStripMenuItem
			//
			hotkeysAndTheirMethodsToolStripMenuItem.Name = "hotkeysAndTheirMethodsToolStripMenuItem";
			hotkeysAndTheirMethodsToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.H;
			hotkeysAndTheirMethodsToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
			hotkeysAndTheirMethodsToolStripMenuItem.Text = "&Hotkeys and their methods";
			hotkeysAndTheirMethodsToolStripMenuItem.Click += hotkeysAndTheirMethodsToolStripMenuItem_Click;
			//
			// keyHistoryAndScriptInfoToolStripMenuItem
			//
			keyHistoryAndScriptInfoToolStripMenuItem.Name = "keyHistoryAndScriptInfoToolStripMenuItem";
			keyHistoryAndScriptInfoToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.K;
			keyHistoryAndScriptInfoToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
			keyHistoryAndScriptInfoToolStripMenuItem.Text = "&Key history and script info";
			keyHistoryAndScriptInfoToolStripMenuItem.Click += keyHistoryAndScriptInfoToolStripMenuItem_Click;
			//
			// clearDebugLogToolStripMenuItem
			//
			clearDebugLogToolStripMenuItem.Name = "clearDebugLogToolStripMenuItem";
			clearDebugLogToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
			clearDebugLogToolStripMenuItem.Text = "&Clear debug log";
			clearDebugLogToolStripMenuItem.Click += clearDebugLogToolStripMenuItem_Click;
			//
			// toolStripSeparator2
			//
			toolStripSeparator2.Name = "toolStripSeparator2";
			toolStripSeparator2.Size = new System.Drawing.Size(257, 6);
			//
			// refreshToolStripMenuItem
			//
			refreshToolStripMenuItem.Name = "refreshToolStripMenuItem";
			refreshToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F5;
			refreshToolStripMenuItem.Size = new System.Drawing.Size(260, 22);
			refreshToolStripMenuItem.Text = "&Refresh";
			refreshToolStripMenuItem.Click += refreshToolStripMenuItem_Click;
			//
			// helpToolStripMenuItem
			//
			helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { userManualToolStripMenuItem, aboutToolStripMenuItem });
			helpToolStripMenuItem.Name = "helpToolStripMenuItem";
			helpToolStripMenuItem.Size = new System.Drawing.Size(44, 22);
			helpToolStripMenuItem.Text = "&Help";
			//
			// userManualToolStripMenuItem
			//
			userManualToolStripMenuItem.Name = "userManualToolStripMenuItem";
			userManualToolStripMenuItem.ShortcutKeys = System.Windows.Forms.Keys.F1;
			userManualToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
			userManualToolStripMenuItem.Text = "&User Manual";
			userManualToolStripMenuItem.Click += userManualToolStripMenuItem_Click;
			//
			// aboutToolStripMenuItem
			//
			aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
			aboutToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
			aboutToolStripMenuItem.Text = "&About";
			aboutToolStripMenuItem.Click += aboutToolStripMenuItem_Click;
			//
			// tcMain
			//
			tcMain.Controls.Add(tpDebug);
			tcMain.Controls.Add(tpVars);
			tcMain.Controls.Add(tpHotkeys);
			tcMain.Controls.Add(tpHistory);
			tcMain.Dock = System.Windows.Forms.DockStyle.Fill;
			tcMain.Location = new System.Drawing.Point(0, 24);
			tcMain.Margin = new System.Windows.Forms.Padding(2);
			tcMain.Name = "tcMain";
			tcMain.SelectedIndex = 0;
			tcMain.Size = new System.Drawing.Size(843, 511);
			tcMain.TabIndex = 3;
			//
			// tpDebug
			//
			tpDebug.Controls.Add(txtDebug);
			tpDebug.Location = new System.Drawing.Point(4, 24);
			tpDebug.Margin = new System.Windows.Forms.Padding(2);
			tpDebug.Name = "tpDebug";
			tpDebug.Padding = new System.Windows.Forms.Padding(2);
			tpDebug.Size = new System.Drawing.Size(835, 483);
			tpDebug.TabIndex = 1;
			tpDebug.Text = "Debug";
			tpDebug.UseVisualStyleBackColor = true;
			//
			// txtDebug
			//
			txtDebug.BackColor = System.Drawing.SystemColors.MenuBar;
			txtDebug.Dock = System.Windows.Forms.DockStyle.Fill;
			txtDebug.Font = new System.Drawing.Font("Cascadia Code", 9F);
			txtDebug.Location = new System.Drawing.Point(2, 2);
			txtDebug.Margin = new System.Windows.Forms.Padding(2);
			txtDebug.Multiline = true;
			txtDebug.Name = "txtDebug";
			txtDebug.ReadOnly = true;
			txtDebug.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			txtDebug.Size = new System.Drawing.Size(831, 479);
			txtDebug.TabIndex = 1;
			//
			// tpVars
			//
			tpVars.Controls.Add(txtVars);
			tpVars.Location = new System.Drawing.Point(4, 24);
			tpVars.Margin = new System.Windows.Forms.Padding(2);
			tpVars.Name = "tpVars";
			tpVars.Padding = new System.Windows.Forms.Padding(2);
			tpVars.Size = new System.Drawing.Size(835, 483);
			tpVars.TabIndex = 2;
			tpVars.Text = "Vars";
			tpVars.UseVisualStyleBackColor = true;
			//
			// txtVars
			//
			txtVars.BackColor = System.Drawing.SystemColors.MenuBar;
			txtVars.Dock = System.Windows.Forms.DockStyle.Fill;
			txtVars.Font = new System.Drawing.Font("Cascadia Code", 9F);
			txtVars.Location = new System.Drawing.Point(2, 2);
			txtVars.Margin = new System.Windows.Forms.Padding(2);
			txtVars.Multiline = true;
			txtVars.Name = "txtVars";
			txtVars.ReadOnly = true;
			txtVars.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			txtVars.Size = new System.Drawing.Size(831, 479);
			txtVars.TabIndex = 2;
			//
			// tpHotkeys
			//
			tpHotkeys.Controls.Add(txtHotkeys);
			tpHotkeys.Location = new System.Drawing.Point(4, 24);
			tpHotkeys.Name = "tpHotkeys";
			tpHotkeys.Padding = new System.Windows.Forms.Padding(3);
			tpHotkeys.Size = new System.Drawing.Size(835, 483);
			tpHotkeys.TabIndex = 3;
			tpHotkeys.Text = "Hotkeys";
			tpHotkeys.UseVisualStyleBackColor = true;
			//
			// txtHotkeys
			//
			txtHotkeys.BackColor = System.Drawing.SystemColors.MenuBar;
			txtHotkeys.Dock = System.Windows.Forms.DockStyle.Fill;
			txtHotkeys.Font = new System.Drawing.Font("Cascadia Code", 9F);
			txtHotkeys.Location = new System.Drawing.Point(3, 3);
			txtHotkeys.Margin = new System.Windows.Forms.Padding(2);
			txtHotkeys.Multiline = true;
			txtHotkeys.Name = "txtHotkeys";
			txtHotkeys.ReadOnly = true;
			txtHotkeys.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			txtHotkeys.Size = new System.Drawing.Size(829, 477);
			txtHotkeys.TabIndex = 3;
			//
			// tpHistory
			//
			tpHistory.Controls.Add(txtHistory);
			tpHistory.Location = new System.Drawing.Point(4, 24);
			tpHistory.Margin = new System.Windows.Forms.Padding(2);
			tpHistory.Name = "tpHistory";
			tpHistory.Padding = new System.Windows.Forms.Padding(2);
			tpHistory.Size = new System.Drawing.Size(835, 483);
			tpHistory.TabIndex = 4;
			tpHistory.Text = "History";
			tpHistory.UseVisualStyleBackColor = true;
			//
			// txtHistory
			//
			txtHistory.BackColor = System.Drawing.SystemColors.MenuBar;
			txtHistory.Dock = System.Windows.Forms.DockStyle.Fill;
			txtHistory.Font = new System.Drawing.Font("Cascadia Code", 9F);
			txtHistory.Location = new System.Drawing.Point(2, 2);
			txtHistory.Margin = new System.Windows.Forms.Padding(2);
			txtHistory.Multiline = true;
			txtHistory.Name = "txtHistory";
			txtHistory.ReadOnly = true;
			txtHistory.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			txtHistory.Size = new System.Drawing.Size(831, 479);
			txtHistory.TabIndex = 3;
			//
			// MainWindow
			//
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			ClientSize = new System.Drawing.Size(843, 535);
			Controls.Add(tcMain);
			Controls.Add(mainMenu);
			MainMenuStrip = mainMenu;
			Margin = new System.Windows.Forms.Padding(2);
			Name = "MainWindow";
			ShowInTaskbar = false;
			StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			Text = "Keysharp";
			FormClosing += MainWindow_FormClosing;
			Load += MainWindow_Load;
			Shown += MainWindow_Shown;
			SizeChanged += MainWindow_SizeChanged;
			mainMenu.ResumeLayout(false);
			mainMenu.PerformLayout();
			tcMain.ResumeLayout(false);
			tpDebug.ResumeLayout(false);
			tpDebug.PerformLayout();
			tpVars.ResumeLayout(false);
			tpVars.PerformLayout();
			tpHotkeys.ResumeLayout(false);
			tpHotkeys.PerformLayout();
			tpHistory.ResumeLayout(false);
			tpHistory.PerformLayout();
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion
		private System.Windows.Forms.MenuStrip mainMenu;
		private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem viewToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem reloadScriptToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem editScriptToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem windowSpyToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem suspendHotkeysToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem variablesAndTheirContentsToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem hotkeysAndTheirMethodsToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem keyHistoryAndScriptInfoToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
		private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem userManualToolStripMenuItem;
		private System.Windows.Forms.TabControl tcMain;
		private System.Windows.Forms.TabPage tpDebug;
		private System.Windows.Forms.TabPage tpVars;
		private Keysharp.Scripting.NonFocusTextBox txtDebug;
		private Keysharp.Scripting.NonFocusTextBox txtVars;
		private System.Windows.Forms.TabPage tpHotkeys;
		private Keysharp.Scripting.NonFocusTextBox txtHotkeys;
		private System.Windows.Forms.TabPage tpHistory;
		private Keysharp.Scripting.NonFocusTextBox txtHistory;
		private System.Windows.Forms.ToolStripMenuItem clearDebugLogToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
	}
}