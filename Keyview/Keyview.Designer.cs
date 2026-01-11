namespace Keyview
{
	partial class Keyview
	{
		/// <summary>
		///  Required designer variable.
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
		///  Required method for Designer support - do not modify
		///  the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Keyview));
			FileName = new Label();
			menuStrip1 = new MenuStrip();
			fileToolStripMenuItem = new ToolStripMenuItem();
			openToolStripMenuItem = new ToolStripMenuItem();
			editToolStripMenuItem = new ToolStripMenuItem();
			cutToolStripMenuItem = new ToolStripMenuItem();
			copyToolStripMenuItem = new ToolStripMenuItem();
			pasteToolStripMenuItem = new ToolStripMenuItem();
			toolStripSeparator1 = new ToolStripSeparator();
			selectLineToolStripMenuItem = new ToolStripMenuItem();
			selectAllToolStripMenuItem = new ToolStripMenuItem();
			clearSelectionToolStripMenuItem = new ToolStripMenuItem();
			toolStripSeparator2 = new ToolStripSeparator();
			indentSelectionToolStripMenuItem = new ToolStripMenuItem();
			outdentSelectionToolStripMenuItem = new ToolStripMenuItem();
			toolStripSeparator3 = new ToolStripSeparator();
			uppercaseSelectionToolStripMenuItem = new ToolStripMenuItem();
			lowercaseSelectionToolStripMenuItem = new ToolStripMenuItem();
			searchToolStripMenuItem = new ToolStripMenuItem();
			findToolStripMenuItem = new ToolStripMenuItem();
			viewToolStripMenuItem = new ToolStripMenuItem();
			wordWrapItem = new ToolStripMenuItem();
			indentGuidesItem = new ToolStripMenuItem();
			hiddenCharactersItem = new ToolStripMenuItem();
			toolStripSeparator4 = new ToolStripSeparator();
			zoomInToolStripMenuItem = new ToolStripMenuItem();
			zoomOutToolStripMenuItem = new ToolStripMenuItem();
			zoom100ToolStripMenuItem = new ToolStripMenuItem();
			toolStripSeparator5 = new ToolStripSeparator();
			collapseAllToolStripMenuItem = new ToolStripMenuItem();
			expandAllToolStripMenuItem = new ToolStripMenuItem();
			toolStripSeparator7 = new ToolStripSeparator();
			PanelSearch = new Panel();
			BtnNextSearch = new Button();
			BtnPrevSearch = new Button();
			BtnCloseSearch = new Button();
			TxtSearch = new TextBox();
			openFileDialog = new OpenFileDialog();
			splitContainer = new SplitContainer();
#if WINDOWS
			txtIn = new Scintilla();
			txtOut = new Scintilla();
#else
			txtIn = new RichTextBox();
			txtOut = new RichTextBox();
#endif
			toolStrip1 = new ToolStrip();
			tslCodeStatus = new ToolStripLabel();
			tssCode = new ToolStripSeparator();
			tslCodeCompile = new ToolStripLabel();
			menuStrip1.SuspendLayout();
			PanelSearch.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
			splitContainer.Panel1.SuspendLayout();
			splitContainer.Panel2.SuspendLayout();
			splitContainer.SuspendLayout();
			toolStrip1.SuspendLayout();
			SuspendLayout();
			//
			// FileName
			//
			FileName.Location = new Point(0, 0);
			FileName.Name = "FileName";
			FileName.Size = new Size(100, 23);
			FileName.TabIndex = 0;
			//
			// menuStrip1
			//
			menuStrip1.BackColor = Color.FromArgb(224, 224, 224);
			menuStrip1.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
			menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, editToolStripMenuItem, searchToolStripMenuItem, viewToolStripMenuItem });
			menuStrip1.Location = new Point(0, 0);
			menuStrip1.Name = "menuStrip1";
			menuStrip1.Padding = new Padding(4, 1, 0, 1);
			menuStrip1.Size = new Size(1680, 24);
			menuStrip1.TabIndex = 5;
			menuStrip1.Text = "menuStrip1";
			//
			// fileToolStripMenuItem
			//
			fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem });
			fileToolStripMenuItem.Name = "fileToolStripMenuItem";
			fileToolStripMenuItem.Size = new Size(37, 22);
			fileToolStripMenuItem.Text = "&File";
			//
			// openToolStripMenuItem
			//
			openToolStripMenuItem.Name = "openToolStripMenuItem";
			openToolStripMenuItem.Size = new Size(112, 22);
			openToolStripMenuItem.Text = "&Open...";
			openToolStripMenuItem.Click += openToolStripMenuItem_Click;
			//
			// editToolStripMenuItem
			//
			editToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { cutToolStripMenuItem, copyToolStripMenuItem, pasteToolStripMenuItem, toolStripSeparator1, selectLineToolStripMenuItem, selectAllToolStripMenuItem, clearSelectionToolStripMenuItem, toolStripSeparator2, indentSelectionToolStripMenuItem, outdentSelectionToolStripMenuItem, toolStripSeparator3, uppercaseSelectionToolStripMenuItem, lowercaseSelectionToolStripMenuItem });
			editToolStripMenuItem.Name = "editToolStripMenuItem";
			editToolStripMenuItem.Size = new Size(39, 22);
			editToolStripMenuItem.Text = "&Edit";
			//
			// cutToolStripMenuItem
			//
			cutToolStripMenuItem.Name = "cutToolStripMenuItem";
			cutToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+X";
			cutToolStripMenuItem.Size = new Size(175, 22);
			cutToolStripMenuItem.Text = "Cu&t";
			cutToolStripMenuItem.Click += cutToolStripMenuItem_Click;
			//
			// copyToolStripMenuItem
			//
			copyToolStripMenuItem.Name = "copyToolStripMenuItem";
			copyToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+C";
			copyToolStripMenuItem.Size = new Size(175, 22);
			copyToolStripMenuItem.Text = "&Copy";
			copyToolStripMenuItem.Click += copyToolStripMenuItem_Click;
			//
			// pasteToolStripMenuItem
			//
			pasteToolStripMenuItem.Name = "pasteToolStripMenuItem";
			pasteToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+V";
			pasteToolStripMenuItem.Size = new Size(175, 22);
			pasteToolStripMenuItem.Text = "&Paste";
			pasteToolStripMenuItem.Click += pasteToolStripMenuItem_Click;
			//
			// toolStripSeparator1
			//
			toolStripSeparator1.Name = "toolStripSeparator1";
			toolStripSeparator1.Size = new Size(172, 6);
			//
			// selectLineToolStripMenuItem
			//
			selectLineToolStripMenuItem.Name = "selectLineToolStripMenuItem";
			selectLineToolStripMenuItem.Size = new Size(175, 22);
			selectLineToolStripMenuItem.Text = "&Select Line";
			selectLineToolStripMenuItem.Click += selectLineToolStripMenuItem_Click;
			//
			// selectAllToolStripMenuItem
			//
			selectAllToolStripMenuItem.Name = "selectAllToolStripMenuItem";
			selectAllToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+A";
			selectAllToolStripMenuItem.Size = new Size(175, 22);
			selectAllToolStripMenuItem.Text = "Select &All";
			selectAllToolStripMenuItem.Click += selectAllToolStripMenuItem_Click;
			//
			// clearSelectionToolStripMenuItem
			//
			clearSelectionToolStripMenuItem.Name = "clearSelectionToolStripMenuItem";
			clearSelectionToolStripMenuItem.Size = new Size(175, 22);
			clearSelectionToolStripMenuItem.Text = "Clea&r Selection";
			clearSelectionToolStripMenuItem.Click += clearSelectionToolStripMenuItem_Click;
			//
			// toolStripSeparator2
			//
			toolStripSeparator2.Name = "toolStripSeparator2";
			toolStripSeparator2.Size = new Size(172, 6);
			//
			// indentSelectionToolStripMenuItem
			//
			indentSelectionToolStripMenuItem.Name = "indentSelectionToolStripMenuItem";
			indentSelectionToolStripMenuItem.ShortcutKeyDisplayString = "Tab";
			indentSelectionToolStripMenuItem.Size = new Size(175, 22);
			indentSelectionToolStripMenuItem.Text = "&Indent";
			indentSelectionToolStripMenuItem.Click += indentSelectionToolStripMenuItem_Click;
			//
			// outdentSelectionToolStripMenuItem
			//
			outdentSelectionToolStripMenuItem.Name = "outdentSelectionToolStripMenuItem";
			outdentSelectionToolStripMenuItem.ShortcutKeyDisplayString = "Shift+Tab";
			outdentSelectionToolStripMenuItem.Size = new Size(175, 22);
			outdentSelectionToolStripMenuItem.Text = "&Outdent";
			outdentSelectionToolStripMenuItem.Click += outdentSelectionToolStripMenuItem_Click;
			//
			// toolStripSeparator3
			//
			toolStripSeparator3.Name = "toolStripSeparator3";
			toolStripSeparator3.Size = new Size(172, 6);
			//
			// uppercaseSelectionToolStripMenuItem
			//
			uppercaseSelectionToolStripMenuItem.Name = "uppercaseSelectionToolStripMenuItem";
			uppercaseSelectionToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+U";
			uppercaseSelectionToolStripMenuItem.Size = new Size(175, 22);
			uppercaseSelectionToolStripMenuItem.Text = "&Uppercase";
			uppercaseSelectionToolStripMenuItem.Click += uppercaseSelectionToolStripMenuItem_Click;
			//
			// lowercaseSelectionToolStripMenuItem
			//
			lowercaseSelectionToolStripMenuItem.Name = "lowercaseSelectionToolStripMenuItem";
			lowercaseSelectionToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+L";
			lowercaseSelectionToolStripMenuItem.Size = new Size(175, 22);
			lowercaseSelectionToolStripMenuItem.Text = "&Lowercase";
			lowercaseSelectionToolStripMenuItem.Click += lowercaseSelectionToolStripMenuItem_Click;
			//
			// searchToolStripMenuItem
			//
			searchToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { findToolStripMenuItem });
			searchToolStripMenuItem.Name = "searchToolStripMenuItem";
			searchToolStripMenuItem.Size = new Size(54, 22);
			searchToolStripMenuItem.Text = "&Search";
			//
			// findToolStripMenuItem
			//
			findToolStripMenuItem.Name = "findToolStripMenuItem";
			findToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+F";
			findToolStripMenuItem.Size = new Size(180, 22);
			findToolStripMenuItem.Text = "&Quick Find...";
			findToolStripMenuItem.Click += findToolStripMenuItem_Click;
			//
			// viewToolStripMenuItem
			//
			viewToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { wordWrapItem, indentGuidesItem, hiddenCharactersItem, toolStripSeparator4, zoomInToolStripMenuItem, zoomOutToolStripMenuItem, zoom100ToolStripMenuItem, toolStripSeparator5, collapseAllToolStripMenuItem, expandAllToolStripMenuItem });
			viewToolStripMenuItem.Name = "viewToolStripMenuItem";
			viewToolStripMenuItem.Size = new Size(44, 22);
			viewToolStripMenuItem.Text = "&View";
			//
			// wordWrapItem
			//
			wordWrapItem.Checked = true;
			wordWrapItem.CheckState = CheckState.Checked;
			wordWrapItem.Name = "wordWrapItem";
			wordWrapItem.Size = new Size(196, 22);
			wordWrapItem.Text = "&Word Wrap";
			wordWrapItem.Click += wordWrapToolStripMenuItem1_Click;
			//
			// indentGuidesItem
			//
			indentGuidesItem.Checked = true;
			indentGuidesItem.CheckState = CheckState.Checked;
			indentGuidesItem.Name = "indentGuidesItem";
			indentGuidesItem.Size = new Size(196, 22);
			indentGuidesItem.Text = "Show Indent &Guides";
			indentGuidesItem.Click += indentGuidesToolStripMenuItem_Click;
			//
			// hiddenCharactersItem
			//
			hiddenCharactersItem.Name = "hiddenCharactersItem";
			hiddenCharactersItem.Size = new Size(196, 22);
			hiddenCharactersItem.Text = "Show W&hitespace";
			hiddenCharactersItem.Click += hiddenCharactersToolStripMenuItem_Click;
			//
			// toolStripSeparator4
			//
			toolStripSeparator4.Name = "toolStripSeparator4";
			toolStripSeparator4.Size = new Size(193, 6);
			//
			// zoomInToolStripMenuItem
			//
			zoomInToolStripMenuItem.Name = "zoomInToolStripMenuItem";
			zoomInToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+Plus";
			zoomInToolStripMenuItem.Size = new Size(196, 22);
			zoomInToolStripMenuItem.Text = "Zoom &In";
			zoomInToolStripMenuItem.Click += zoomInToolStripMenuItem_Click;
			//
			// zoomOutToolStripMenuItem
			//
			zoomOutToolStripMenuItem.Name = "zoomOutToolStripMenuItem";
			zoomOutToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+Minus";
			zoomOutToolStripMenuItem.Size = new Size(196, 22);
			zoomOutToolStripMenuItem.Text = "Zoom &Out";
			zoomOutToolStripMenuItem.Click += zoomOutToolStripMenuItem_Click;
			//
			// zoom100ToolStripMenuItem
			//
			zoom100ToolStripMenuItem.Name = "zoom100ToolStripMenuItem";
			zoom100ToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+0";
			zoom100ToolStripMenuItem.Size = new Size(196, 22);
			zoom100ToolStripMenuItem.Text = "&Zoom 100%";
			zoom100ToolStripMenuItem.Click += zoom100ToolStripMenuItem_Click;
			//
			// toolStripSeparator5
			//
			toolStripSeparator5.Name = "toolStripSeparator5";
			toolStripSeparator5.Size = new Size(193, 6);
			//
			// collapseAllToolStripMenuItem
			//
			collapseAllToolStripMenuItem.Name = "collapseAllToolStripMenuItem";
			collapseAllToolStripMenuItem.Size = new Size(196, 22);
			collapseAllToolStripMenuItem.Text = "&Collapse All";
			collapseAllToolStripMenuItem.Click += collapseAllToolStripMenuItem_Click;
			//
			// expandAllToolStripMenuItem
			//
			expandAllToolStripMenuItem.Name = "expandAllToolStripMenuItem";
			expandAllToolStripMenuItem.Size = new Size(196, 22);
			expandAllToolStripMenuItem.Text = "&Expand All";
			expandAllToolStripMenuItem.Click += expandAllToolStripMenuItem_Click;
			//
			// toolStripSeparator7
			//
			toolStripSeparator7.Name = "toolStripSeparator7";
			toolStripSeparator7.Size = new Size(280, 6);
			//
			// PanelSearch
			//
			PanelSearch.BackColor = Color.White;
			PanelSearch.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			PanelSearch.Controls.Add(BtnNextSearch);
			PanelSearch.Controls.Add(BtnPrevSearch);
			PanelSearch.Controls.Add(BtnCloseSearch);
			PanelSearch.Controls.Add(TxtSearch);
			PanelSearch.Dock = DockStyle.Top;
			PanelSearch.Location = new Point(0, 24);
			PanelSearch.Margin = new Padding(3, 2, 3, 2);
			PanelSearch.Name = "PanelSearch";
			PanelSearch.Padding = new Padding(3, 2, 3, 2);
			PanelSearch.Size = new Size(1680, 24);
			PanelSearch.TabIndex = 10;
			PanelSearch.Visible = false;
			//
			// BtnNextSearch
			//
			BtnNextSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			BtnNextSearch.FlatStyle = FlatStyle.Flat;
			BtnNextSearch.ForeColor = Color.White;
			BtnNextSearch.Image = (Image)resources.GetObject("BtnNextSearch.Image");
			BtnNextSearch.Location = new Point(1645, 2);
			BtnNextSearch.Margin = new Padding(2);
			BtnNextSearch.Name = "BtnNextSearch";
			BtnNextSearch.Size = new Size(15, 15);
			BtnNextSearch.TabIndex = 9;
			BtnNextSearch.Tag = "Find next (Enter)";
			BtnNextSearch.UseVisualStyleBackColor = true;
			BtnNextSearch.Click += BtnNextSearch_Click;
			//
			// BtnPrevSearch
			//
			BtnPrevSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			BtnPrevSearch.FlatStyle = FlatStyle.Flat;
			BtnPrevSearch.ForeColor = Color.White;
			BtnPrevSearch.Image = (Image)resources.GetObject("BtnPrevSearch.Image");
			BtnPrevSearch.Location = new Point(1629, 2);
			BtnPrevSearch.Margin = new Padding(2);
			BtnPrevSearch.Name = "BtnPrevSearch";
			BtnPrevSearch.Size = new Size(15, 15);
			BtnPrevSearch.TabIndex = 8;
			BtnPrevSearch.Tag = "Find previous (Shift+Enter)";
			BtnPrevSearch.UseVisualStyleBackColor = true;
			BtnPrevSearch.Click += BtnPrevSearch_Click;
			//
			// BtnCloseSearch
			//
			BtnCloseSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			BtnCloseSearch.FlatStyle = FlatStyle.Flat;
			BtnCloseSearch.ForeColor = Color.White;
			BtnCloseSearch.Image = (Image)resources.GetObject("BtnCloseSearch.Image");
			BtnCloseSearch.Location = new Point(1661, 2);
			BtnCloseSearch.Margin = new Padding(2);
			BtnCloseSearch.Name = "BtnCloseSearch";
			BtnCloseSearch.Size = new Size(15, 15);
			BtnCloseSearch.TabIndex = 7;
			BtnCloseSearch.Tag = "Close (Esc)";
			BtnCloseSearch.UseVisualStyleBackColor = true;
			BtnCloseSearch.Click += BtnClearSearch_Click;
			//
			// TxtSearch
			//
			TxtSearch.BorderStyle = System.Windows.Forms.BorderStyle.None;
			TxtSearch.Dock = DockStyle.Top;
			TxtSearch.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
			TxtSearch.Location = new Point(3, 2);
			TxtSearch.Margin = new Padding(2);
			TxtSearch.Name = "TxtSearch";
			TxtSearch.Size = new Size(1672, 16);
			TxtSearch.TabIndex = 6;
			TxtSearch.TextChanged += TxtSearch_TextChanged;
			TxtSearch.KeyDown += TxtSearch_KeyDown;
			//
			// openFileDialog
			//
			openFileDialog.DefaultExt = "txt";
			openFileDialog.FileName = "New File";
			openFileDialog.Filter = "All files|*.*";
			//
			// splitContainer
			//
			splitContainer.Cursor = Cursors.VSplit;
			splitContainer.Dock = DockStyle.Fill;
			splitContainer.Location = new Point(0, 48);
			splitContainer.Margin = new Padding(0);
			splitContainer.Name = "splitContainer";
			//
			// splitContainer.Panel1
			//
			splitContainer.Panel1.Controls.Add(txtIn);
			splitContainer.Panel1.Padding = new Padding(1, 0, 1, 0);
			//
			// splitContainer.Panel2
			//
			splitContainer.Panel2.Controls.Add(txtOut);
			splitContainer.Panel2.Padding = new Padding(1, 0, 1, 0);
			splitContainer.Size = new Size(1680, 404);
			splitContainer.SplitterDistance = 860;
			splitContainer.SplitterWidth = 2;
			splitContainer.TabIndex = 0;
			splitContainer.DoubleClick += splitContainer_DoubleClick;
			//
			// txtIn
			//
#if WINDOWS
			txtIn.AutocompleteListSelectedBackColor = Color.FromArgb(0, 120, 215);
			txtIn.CaretLineBackColor = Color.AliceBlue;
#endif
			txtIn.Dock = DockStyle.Fill;
			txtIn.Font = new Font("Consolas", 10F);
#if !WINDOWS
			txtIn.BorderStyle = BorderStyle.None;
#endif
			txtIn.Location = new Point(1, 0);
			txtIn.Margin = new Padding(0);
			txtIn.Name = "txtIn";
			txtIn.Size = new Size(858, 404);
			txtIn.TabIndex = 1;
#if WINDOWS
			txtIn.WrapMode = WrapMode.Word;
#else
			txtIn.WordWrap = true;
#endif
			txtIn.TextChanged += txtIn_TextChanged;
			txtIn.KeyDown += txtIn_KeyDown;
			//
			// txtOut
			//
#if WINDOWS
			txtOut.AutocompleteListSelectedBackColor = Color.FromArgb(0, 120, 215);
			txtOut.CaretLineBackColor = Color.AliceBlue;
#endif
			txtOut.Dock = DockStyle.Fill;
			txtOut.Font = new Font("Consolas", 10F);
#if !WINDOWS
			txtOut.BorderStyle = BorderStyle.None;
#endif
			txtOut.Location = new Point(1, 0);
			txtOut.Margin = new Padding(0);
			txtOut.Name = "txtOut";
			txtOut.ReadOnly = true;
			txtOut.Size = new Size(816, 404);
			txtOut.TabIndex = 2;
#if WINDOWS
			txtOut.WrapMode = WrapMode.Word;
#else
			txtOut.WordWrap = true;
#endif
			txtOut.KeyDown += txtOut_KeyDown;
			//
			// toolStrip1
			//
			toolStrip1.AutoSize = false;
			toolStrip1.Dock = DockStyle.Bottom;
			toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
			toolStrip1.Items.AddRange(new ToolStripItem[] { tslCodeStatus, tssCode, tslCodeCompile });
			toolStrip1.Location = new Point(0, 452);
			toolStrip1.Margin = new Padding(15);
			toolStrip1.Name = "toolStrip1";
			toolStrip1.Size = new Size(1680, 30);
			toolStrip1.TabIndex = 10;
			//
			// tslCodeStatus
			//
			tslCodeStatus.Alignment = ToolStripItemAlignment.Right;
			tslCodeStatus.AutoSize = false;
			tslCodeStatus.Name = "tslCodeStatus";
			tslCodeStatus.Size = new Size(300, 39);
			tslCodeStatus.Text = "------------------------";
			//
			// tssCode
			//
			tssCode.Alignment = ToolStripItemAlignment.Right;
			tssCode.Name = "tssCode";
			tssCode.Size = new Size(6, 30);
			//
			// tslCodeCompile
			//
			tslCodeCompile.Alignment = ToolStripItemAlignment.Right;
			tslCodeCompile.Name = "tslCodeCompile";
			tslCodeCompile.Size = new Size(84, 27);
			tslCodeCompile.Text = "Code compile:";
			//
			// Keyview
			//
			AutoScaleDimensions = new SizeF(7F, 15F);
			AutoScaleMode = AutoScaleMode.Font;
			BackColor = Color.White;
			ClientSize = new Size(1680, 482);
			Controls.Add(splitContainer);
			Controls.Add(PanelSearch);
			Controls.Add(toolStrip1);
			Controls.Add(menuStrip1);
			Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
			MainMenuStrip = menuStrip1;
			Margin = new Padding(2);
			Name = "Keyview";
			StartPosition = FormStartPosition.CenterScreen;
			Text = "Keyview";
			WindowState = FormWindowState.Maximized;
			FormClosing += Keyview_FormClosing;
			Load += Keyview_Load;
			ResizeEnd += Keyview_ResizeEnd;
			menuStrip1.ResumeLayout(false);
			menuStrip1.PerformLayout();
			PanelSearch.ResumeLayout(false);
			PanelSearch.PerformLayout();
			splitContainer.Panel1.ResumeLayout(false);
			splitContainer.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
			splitContainer.ResumeLayout(false);
			toolStrip1.ResumeLayout(false);
			toolStrip1.PerformLayout();
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion
		private System.Windows.Forms.Label FileName;
		private System.Windows.Forms.MenuStrip menuStrip1;
		private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem viewToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem wordWrapItem;
		private System.Windows.Forms.ToolStripMenuItem hiddenCharactersItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
		private System.Windows.Forms.ToolStripMenuItem zoomInToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem zoomOutToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem zoom100ToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator5;
		private System.Windows.Forms.ToolStripMenuItem collapseAllToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem expandAllToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem editToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem cutToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem copyToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem pasteToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem selectAllToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
		private System.Windows.Forms.ToolStripMenuItem indentSelectionToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem outdentSelectionToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
		private System.Windows.Forms.ToolStripMenuItem uppercaseSelectionToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem lowercaseSelectionToolStripMenuItem;
		private System.Windows.Forms.Panel PanelSearch;
		private System.Windows.Forms.Button BtnNextSearch;
		private System.Windows.Forms.Button BtnPrevSearch;
		private System.Windows.Forms.Button BtnCloseSearch;
		private System.Windows.Forms.TextBox TxtSearch;
		private System.Windows.Forms.OpenFileDialog openFileDialog;
		private System.Windows.Forms.ToolStripMenuItem selectLineToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem clearSelectionToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem searchToolStripMenuItem;
		//private System.Windows.Forms.ToolStripMenuItem goToLineToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem findToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator7;
		private System.Windows.Forms.ToolStripMenuItem indentGuidesItem;
		private System.Windows.Forms.SplitContainer splitContainer;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripLabel tslCodeCompile;
		private System.Windows.Forms.ToolStripSeparator tssCode;
		private System.Windows.Forms.ToolStripLabel tslCodeStatus;
#if WINDOWS
		private ScintillaNET.Scintilla txtIn;
		private ScintillaNET.Scintilla txtOut;
#else
		private System.Windows.Forms.RichTextBox txtIn;
		private System.Windows.Forms.RichTextBox txtOut;
#endif
	}
}
