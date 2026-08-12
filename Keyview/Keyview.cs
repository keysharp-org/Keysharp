#if WINDOWS
using ScintillaNET;
#endif

namespace Keyview
{
	/// <summary>
	/// Much of the Scintilla-related code was taken from: https://github.com/robinrodricks/ScintillaNET.Demo
	/// </summary>
#if WINDOWS
	internal partial class Keyview : Form
	{
		/// <summary>
		/// the background color of the text area
		/// </summary>
		//private const int BACK_COLOR = 0x2A211C;
		private const int BACK_COLOR = 0xEEEEEE;

		/// <summary>
		/// change this to whatever margin you want the bookmarks/breakpoints to show in
		/// </summary>
		private const int BOOKMARK_MARGIN = 2;

		private const int BOOKMARK_MARKER = 2;

		/// <summary>
		/// set this true to show circular buttons for code folding (the [+] and [-] buttons on the margin)
		/// </summary>
		private const bool CODEFOLDING_CIRCULAR = true;

		/// <summary>
		/// change this to whatever margin you want the code folding tree (+/-) to show in
		/// </summary>
		private const int FOLDING_MARGIN = 3;

		/// <summary>
		/// default text color of the text area
		/// </summary>
		private const int FORE_COLOR = 0x858585;

		/// <summary>
		/// change this to whatever margin you want the line numbers to show in
		/// </summary>
		private const int NUMBER_MARGIN = 1;

		private readonly Button btnCopyFullCode = new ();
		private readonly Button btnCompileScript = new ();
		private readonly CheckBox chkFullCode = new ();
		private readonly ToolStripLabel documentStatusLabel = new ();
		private readonly string lastrun;
		private readonly UITimer timer = new ();
		private readonly char[] trimend = ['\n', '\r'];
		private readonly double updateFreqSeconds = 1;
		private readonly IScriptCompiler ch = KeyviewCompilerRunner.GetCompiler();
		private byte[] compiledBytes;
		private readonly CSharpStyler csStyler = new ();
		private bool force = false;
		private bool isCompiling = false;
		private string fullCode = "";
		private DateTime lastCompileTime = DateTime.UtcNow;
		private DateTime lastKeyTime = DateTime.UtcNow;
		private bool SearchIsOpen = false;
		private string trimmedCode = "";
		private readonly string trimstr = "{}\t";
		private Process scriptProcess = null;
		private readonly Button btnRunScript = new ();
		private readonly KeyviewDocumentState document = new ();
		private string baseTitle;
		private bool suppressDocumentChange;
		private bool scratchAutosavePending;
		private bool closing;
		private readonly Dictionary<string, string> btnRunScriptText = new Dictionary<string, string>()
		{
			{ "Run", "▶ Run script (F9)" },
			{ "Stop", "⏹ Stop script (F9)" }
		};

		public Keyview(string initialFile = null)
		{
			InitializeComponent();
			lastrun = $"{Accessors.A_AppData}/Keysharp/lastkeyviewrun.txt";
			Icon = Script.TheScript.normalIcon;
			btnCopyFullCode.Text = "Copy full code";
			btnCopyFullCode.Click += CopyFullCode_Click;
			btnCopyFullCode.Margin = new Padding(15);
			var host = new ToolStripControlHost(btnCopyFullCode)
			{
				Alignment = ToolStripItemAlignment.Right
			};
			_ = toolStrip1.Items.Add(host);
			chkFullCode.Text = "Full code";
			chkFullCode.CheckStateChanged += chkFullCode_CheckStateChanged;
			host = new ToolStripControlHost(chkFullCode)
			{
				Alignment = ToolStripItemAlignment.Right
			};
			_ = toolStrip1.Items.Add(host);
			Text += $" {Assembly.GetExecutingAssembly().GetName().Version}";
			baseTitle = Text;
			btnRunScript.Text = btnRunScriptText["Run"];
			btnRunScript.Margin = new Padding(15);
			host = new ToolStripControlHost(btnRunScript)
			{
				Alignment = ToolStripItemAlignment.Right
			};
			_ = toolStrip1.Items.Add(host);
			btnRunScript.Enabled = false;
			btnRunScript.Click += RunScript_Click;
			btnCompileScript.Text = "Compile .cks";
			btnCompileScript.Margin = new Padding(15);
			btnCompileScript.Click += (_, _) => CompileDocument();
			host = new ToolStripControlHost(btnCompileScript);
			_ = toolStrip1.Items.Add(host);
			documentStatusLabel.Alignment = ToolStripItemAlignment.Left;
			documentStatusLabel.AutoSize = false;
			documentStatusLabel.Width = 600;
			documentStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
			toolStrip1.Items.Insert(0, documentStatusLabel);

			if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile))
			{
				LoadDataFromFile(initialFile);
			}
			else if (File.Exists(lastrun))
			{
				LoadScratchDocument();
			}
			else
				UpdateDocumentUi();
		}

		private static Color IntToColor(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

		// The Keysharp/AHK tokenizer for the input box, shared with the Eto editor on Linux/macOS. Built lazily
		// because ForKeysharp() reads the built-in names off Script.TheScript, which is not up yet at field-init.
		private SyntaxHighlighter inputHighlighter;

		private void TxtIn_StyleNeeded(object sender, StyleNeededEventArgs e) =>
			ScintillaSyntaxSink.Restyle((Scintilla)sender, inputHighlighter ??= SyntaxHighlighter.ForKeysharp());

		private void BtnClearSearch_Click(object sender, EventArgs e) => CloseSearch();

		private void BtnNextSearch_Click(object sender, EventArgs e) => SearchManager.Find(true, false);

		private void BtnPrevSearch_Click(object sender, EventArgs e) => SearchManager.Find(false, false);

		private void chkFullCode_CheckStateChanged(object sender, EventArgs e) => SetTxtOut(chkFullCode.Checked ? fullCode : trimmedCode);

		private void clearSelectionToolStripMenuItem_Click(object sender, EventArgs e)
		{
			// Reset selection to the start without changing the caret position more than necessary.
			txtIn.SetEmptySelection(0);
			lastKeyTime = DateTime.UtcNow;
		}

		private void CloseSearch()
		{
			if (SearchIsOpen)
			{
				SearchIsOpen = false;
				InvokeIfNeeded(delegate ()
				{
					PanelSearch.Visible = false;
				});
			}
		}

		private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtIn.FoldAll(FoldAction.Contract);
		}

		private void CopyFullCode_Click(object sender, EventArgs e)
		{
			try
			{
				if (fullCode != "")
					Clipboard.SetText(fullCode);
				else
					Clipboard.SetText(txtOut.Text);
			}
			catch (Exception ex)
			{
				_ = MessageBox.Show($"Copying code failed: {ex.Message}", "Keyview", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void copyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtIn.Copy();
			lastKeyTime = DateTime.UtcNow;
		}

		private void cutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtIn.Cut();
			lastKeyTime = DateTime.UtcNow;
		}

		private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtIn.FoldAll(FoldAction.Expand);
		}

		private void findAndReplaceToolStripMenuItem_Click(object sender, EventArgs e) => OpenReplaceDialog();

		private void findDialogToolStripMenuItem_Click(object sender, EventArgs e) => OpenFindDialog();

		private void findToolStripMenuItem_Click(object sender, EventArgs e) => OpenSearch();

		private void GenerateKeystrokes(string keys)
		{
			HotKeyManager.Enable = false;
			_ = txtIn.Focus();
			SendKeys.Send(keys);
			HotKeyManager.Enable = true;
		}

		private void hiddenCharactersToolStripMenuItem_Click(object sender, EventArgs e)
		{
			hiddenCharactersItem.Checked = !hiddenCharactersItem.Checked;
			txtIn.ViewWhitespace = hiddenCharactersItem.Checked ? WhitespaceMode.VisibleAlways : WhitespaceMode.Invisible;
		}

		private void Indent()
		{
			//We use this hack to send "Shift+Tab" to scintilla, since there is no known API to indent,
			//although the indentation function exists. Pressing TAB with the editor focused confirms this.
			GenerateKeystrokes("{TAB}");
			lastKeyTime = DateTime.UtcNow;
		}

		private void indentGuidesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			indentGuidesItem.Checked = !indentGuidesItem.Checked;
			txtIn.IndentationGuides = indentGuidesItem.Checked ? IndentView.LookBoth : IndentView.None;
		}

		private void indentSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Indent();

		//private void InitBookmarkMargin()
		//{
		//  //TextArea.SetFoldMarginColor(true, IntToColor(BACK_COLOR));
		//  var margin = txtIn.Margins[BOOKMARK_MARGIN];
		//  margin.Width = 20;
		//  margin.Sensitive = true;
		//  margin.Type = MarginType.Symbol;
		//  margin.Mask = 1 << BOOKMARK_MARKER;
		//  //margin.Cursor = MarginCursor.Arrow;
		//  var marker = txtIn.Markers[BOOKMARK_MARKER];
		//  marker.Symbol = MarkerSymbol.Circle;
		//  marker.SetBackColor(IntToColor(0xFF003B));
		//  marker.SetForeColor(IntToColor(0x000000));
		//  marker.SetAlpha(100);
		//}
		private void InitCodeFolding(Scintilla txt)
		{
			txt.SetFoldMarginColor(true, IntToColor(BACK_COLOR));
			txt.SetFoldMarginHighlightColor(true, IntToColor(BACK_COLOR));
			//Enable code folding.
			txt.SetProperty("fold", "1");
			txt.SetProperty("fold.compact", "1");
			// Configure a margin to display folding symbols
			txt.Margins[FOLDING_MARGIN].Type = MarginType.Symbol;
			txt.Margins[FOLDING_MARGIN].Mask = Marker.MaskFolders;
			txt.Margins[FOLDING_MARGIN].Sensitive = true;
			txt.Margins[FOLDING_MARGIN].Width = 20;

			//Set colors for all folding markers.
			for (int i = 25; i <= 31; i++)
			{
				txt.Markers[i].SetForeColor(IntToColor(BACK_COLOR)); // styles for [+] and [-]
				txt.Markers[i].SetBackColor(IntToColor(FORE_COLOR)); // styles for [+] and [-]
			}

			//Configure folding markers with respective symbols.
			txt.Markers[Marker.Folder].Symbol = CODEFOLDING_CIRCULAR ? MarkerSymbol.CirclePlus : MarkerSymbol.BoxPlus;
			txt.Markers[Marker.FolderOpen].Symbol = CODEFOLDING_CIRCULAR ? MarkerSymbol.CircleMinus : MarkerSymbol.BoxMinus;
			txt.Markers[Marker.FolderEnd].Symbol = CODEFOLDING_CIRCULAR ? MarkerSymbol.CirclePlusConnected : MarkerSymbol.BoxPlusConnected;
			txt.Markers[Marker.FolderMidTail].Symbol = MarkerSymbol.TCorner;
			txt.Markers[Marker.FolderOpenMid].Symbol = CODEFOLDING_CIRCULAR ? MarkerSymbol.CircleMinusConnected : MarkerSymbol.BoxMinusConnected;
			txt.Markers[Marker.FolderSub].Symbol = MarkerSymbol.VLine;
			txt.Markers[Marker.FolderTail].Symbol = MarkerSymbol.LCorner;
			//Enable automatic folding.
			txt.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;
		}

		private void InitColors(Scintilla txt) => txt.CaretForeColor = Color.Black;

		private void InitNumberMargin(Scintilla txt)
		{
			txt.Styles[Style.LineNumber].BackColor = IntToColor(BACK_COLOR);
			txt.Styles[Style.LineNumber].ForeColor = IntToColor(FORE_COLOR);
			txt.Styles[Style.IndentGuide].ForeColor = IntToColor(FORE_COLOR);
			txt.Styles[Style.IndentGuide].BackColor = IntToColor(BACK_COLOR);
			var nums = txt.Margins[NUMBER_MARGIN];
			nums.Width = 30;
			nums.Type = MarginType.Number;
			nums.Sensitive = true;
			nums.Mask = 0;
			txt.MarginClick += txtIn_MarginClick;

			UpdateNumberMarginWidth(txt);
		}

		private void UpdateNumberMarginWidth(Scintilla txt)
		{
			// how many lines do we have?
			int maxLine = Math.Max(1, txt.Lines.Count);      // avoid 0
			int digits = (int)Math.Log10(maxLine) + 1;       // 1 for 1..9, 2 for 10..99, etc.

			// width in pixels needed to render that many '9' with the line-number style
			int px = txt.TextWidth(Style.LineNumber, new string('9', digits));

			// a little breathing room for padding glyphs
			txt.Margins[NUMBER_MARGIN].Width = px + 8;
		}

		// Base style only. The coloring is done by ScintillaSyntaxSink from the shared tokenizer, so no lexer
		// styles or keyword lists are configured here.
		private void InitInputStyle(Scintilla txt)
		{
			txt.StyleResetDefault();
			txt.Styles[Style.Default].Font = "Consolas";
			txt.Styles[Style.Default].Size = 10;
			txt.StyleClearAll();
			txt.SelectionBackColor = Color.FromArgb(153, 201, 239);
		}

		private void InitDragDropFile()
		{
			txtIn.AllowDrop = true;
			txtIn.DragEnter += TxtIn_DragEnter;
			txtIn.DragDrop += TxtIn_DragDrop;
		}

		private void InitHotkeys()
		{
			// register the hotkeys with the form
			HotKeyManager.AddHotKey(this, OpenSearch, Keys.F, true);
			HotKeyManager.AddHotKey(this, OpenFindDialog, Keys.F, true, false, true);
			HotKeyManager.AddHotKey(this, OpenReplaceDialog, Keys.R, true);
			HotKeyManager.AddHotKey(this, OpenReplaceDialog, Keys.H, true);
			HotKeyManager.AddHotKey(this, Uppercase, Keys.U, true);
			HotKeyManager.AddHotKey(this, Lowercase, Keys.L, true);
			HotKeyManager.AddHotKey(this, ZoomIn, Keys.Oemplus, true);
			HotKeyManager.AddHotKey(this, ZoomOut, Keys.OemMinus, true);
			HotKeyManager.AddHotKey(this, ZoomDefault, Keys.D0, true);
			HotKeyManager.AddHotKey(this, CloseSearch, Keys.Escape);
			HotKeyManager.AddHotKey(this, RunStopScript, Keys.F9);
			//Remove conflicting hotkeys from scintilla.
			txtIn.ClearCmdKey(Keys.Control | Keys.F);
			txtIn.ClearCmdKey(Keys.Control | Keys.R);
			txtIn.ClearCmdKey(Keys.Control | Keys.H);
			txtIn.ClearCmdKey(Keys.Control | Keys.L);
			txtIn.ClearCmdKey(Keys.Control | Keys.U);
		}

		private void InvokeIfNeeded(Action action)
		{
			if (InvokeRequired)
			{
				_ = BeginInvoke(action);
			}
			else
			{
				action.Invoke();
			}
		}

		private void Keyview_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (!ConfirmDiscardChanges())
			{
				e.Cancel = true;
				return;
			}

			timer.Stop();
			//Script.Stop();
			// Save while the editor text is still valid, then block any later (post-close) autosave —
			// e.g. an in-flight compile's writeLastRun callback — from overwriting with an empty string.
			AutosaveScratchDocument();
			closing = true;
		}

		private void AutosaveScratchDocument()
        {
			if (closing || !document.IsScratch)
				return;

			var dir = Path.GetDirectoryName(lastrun);
			try
			{
				if (!Directory.Exists(dir))
					_ = Directory.CreateDirectory(dir);

				File.WriteAllText(lastrun, txtIn.Text);
			}
			catch (Exception ex)
			{
				documentStatusLabel.Text = $"Scratch autosave failed: {ex.Message}";
			}
        }

		private void Keyview_Load(object sender, EventArgs e)
		{
			InitColors(txtIn);
			InitColors(txtOut);
			// The input box is container-styled from the SAME tokenizer the Eto editor uses: Scintilla has no
			// AutoHotkey lexer, and the C++ one it used to borrow could not switch to C# inside a #CSharp block.
			InitInputStyle(txtIn);
			ScintillaSyntaxSink.Attach(txtIn);
			txtIn.StyleNeeded += TxtIn_StyleNeeded;
			txtOut.StyleResetDefault();
			txtOut.Styles[Style.Default].Font = "Consolas";
			txtOut.Styles[Style.Default].Size = 10;
			//txt.Styles[Style.Default].BackColor = IntToColor(0xFFFCE1);
			//txt.Styles[Style.Default].BackColor = IntToColor(0x212121);
			//txt.Styles[Style.Default].ForeColor = IntToColor(0xFFFFFF);
			txtOut.StyleClearAll();
			csStyler.ApplyStyle(txtOut);//C# syntax for txtOut.
			csStyler.SetKeywords(txtOut);
			InitNumberMargin(txtIn);
			InitNumberMargin(txtOut);
			//InitBookmarkMargin();
			InitCodeFolding(txtIn);
			InitCodeFolding(txtOut);
			InitDragDropFile();
			InitHotkeys();

			timer.Interval = 1000;
			timer.Tick += Timer_Tick;
			timer.Start();
		}

		private void Keyview_ResizeEnd(object sender, EventArgs e) => splitContainer.SplitterDistance = Width / 2;

		private void LoadDataFromFile(string path)
		{
			if (!File.Exists(path))
				return;

			suppressDocumentChange = true;
			try
			{
				var fullPath = Path.GetFullPath(path);
				var text = File.ReadAllText(fullPath);
				FileName.Text = Path.GetFileName(fullPath);
				txtIn.Text = text;
				document.LoadFile(fullPath, text);
				txtIn.EmptyUndoBuffer();
			}
			finally
			{
				suppressDocumentChange = false;
			}

			lastKeyTime = DateTime.UtcNow;
			UpdateDocumentUi();
		}

		private void LoadScratchDocument()
		{
			suppressDocumentChange = true;
			try
			{
				var text = File.Exists(lastrun) ? File.ReadAllText(lastrun) : "";
				txtIn.Text = text;
				document.LoadScratch();
				txtIn.EmptyUndoBuffer();
			}
			finally
			{
				suppressDocumentChange = false;
			}

			lastKeyTime = DateTime.UtcNow;
			UpdateDocumentUi();
		}

		private void Lowercase()
		{
			var start = txtIn.SelectionStart;
			var end = txtIn.SelectionEnd;
			txtIn.ReplaceSelection(txtIn.GetTextRange(start, end - start).ToLower());
			txtIn.SetSelection(start, end);
			lastKeyTime = DateTime.UtcNow;
		}

		private void lowercaseSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Lowercase();

		private void OpenFindDialog()
		{
		}

		private void OpenReplaceDialog()
		{
		}

		private void OpenSearch()
		{
			SearchManager.SearchBox = TxtSearch;
			SearchManager.TextArea = txtIn;

			if (!SearchIsOpen)
			{
				SearchIsOpen = true;
				InvokeIfNeeded(delegate ()
				{
					PanelSearch.Visible = true;
					TxtSearch.Text = SearchManager.LastSearch;
					_ = TxtSearch.Focus();
					TxtSearch.SelectAll();
				});
			}
			else
			{
				InvokeIfNeeded(delegate ()
				{
					_ = TxtSearch.Focus();
					TxtSearch.SelectAll();
				});
			}
		}

		private void openToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (ConfirmDiscardChanges() && openFileDialog.ShowDialog() == DialogResult.OK)
			{
				LoadDataFromFile(openFileDialog.FileName);
			}
		}

		private bool ConfirmDiscardChanges()
		{
			if (!document.IsDirty(txtIn.Text))
				return true;

			var result = MessageBox.Show(
				$"Save changes to {document.DisplayName}?",
				"Keyview",
				MessageBoxButtons.YesNoCancel,
				MessageBoxIcon.Question);

			return result switch
			{
				DialogResult.Yes => SaveDocument(),
				DialogResult.No => true,
				_ => false
			};
		}

		private bool SaveDocument()
		{
			if (document.IsScratch)
				return false;

			try
			{
				File.WriteAllText(document.CurrentFilePath, txtIn.Text);
				document.MarkSaved(txtIn.Text);
				UpdateDocumentUi();
				return true;
			}
			catch (Exception ex)
			{
				_ = MessageBox.Show($"Unable to save file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		private void CompileDocument()
		{
			if (!document.CanCompile || (document.IsDirty(txtIn.Text) && !SaveDocument()))
				return;

			btnCompileScript.Enabled = false;
			tslCodeStatus.Text = "Writing .cks...";
			Refresh();

			if (KeyviewDocumentCompiler.TryCompile(document.CurrentFilePath, ch, out var outputPath, out var error))
			{
				tslCodeStatus.ForeColor = Color.Green;
				tslCodeStatus.Text = $"Wrote {outputPath}";
			}
			else
			{
				tslCodeStatus.ForeColor = Color.Red;
				tslCodeStatus.Text = "Compile failed";
				SetTxtOut(error);
			}

			UpdateDocumentUi();
		}

		private void UpdateDocumentUi(string text = null)
		{
			var dirty = document.IsDirty(text ?? txtIn.Text);
			Text = document.GetWindowTitle(baseTitle, dirty);
			documentStatusLabel.Text = document.GetStatusText(dirty);
			saveToolStripMenuItem.Enabled = !document.IsScratch && dirty;
			compileToolStripMenuItem.Enabled = document.CanCompile;
			btnCompileScript.Visible = !document.IsScratch;
			btnCompileScript.Enabled = document.CanCompile;
		}

		private void Outdent()
		{
			// we use this hack to send "Shift+Tab" to scintilla, since there is no known API to outdent,
			// although the indentation function exists. Pressing Shift+Tab with the editor focused confirms this.
			GenerateKeystrokes("+{TAB}");
			lastKeyTime = DateTime.UtcNow;
		}

		private void outdentSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Outdent();

		private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
		{
			txtIn.Paste();
			lastKeyTime = DateTime.UtcNow;
		}

		private void selectAllToolStripMenuItem_Click(object sender, EventArgs e) => txtIn.SelectAll();

		private void saveToolStripMenuItem_Click(object sender, EventArgs e) => SaveDocument();

		private void compileToolStripMenuItem_Click(object sender, EventArgs e) => CompileDocument();

		private void selectLineToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(txtIn.Text))
				return;
			Line line = txtIn.Lines[txtIn.CurrentLine];
			txtIn.SetSelection(line.Position + line.Length, line.Position);
		}

		private void SetFailure()
		{
			tslCodeStatus.ForeColor = Color.Red;
			tslCodeStatus.Text = "Error";
			SetTxtOut("");
			Refresh();
		}

		private void SetStart()
		{
			fullCode = trimmedCode = "";
			tslCodeStatus.ForeColor = Color.Black;
			tslCodeStatus.Text = "";
			//Don't clear txtOut, it causes flicker.
			Refresh();
		}

		private void SetSuccess(double seconds)
		{
			tslCodeStatus.ForeColor = Color.Green;
			tslCodeStatus.Text = $"Ok ({seconds:F1}s)";
			Refresh();
		}

		private void SetTxtOut(string txt)
		{
			txtOut.ReadOnly = false;
			txtOut.Text = txt;
			txtOut.ReadOnly = true;
			UpdateNumberMarginWidth(txtOut);
		}

		private void splitContainer_DoubleClick(object sender, EventArgs e) => splitContainer.SplitterDistance = Width / 2;

		//private void TextArea_MarginClick(object sender, MarginClickEventArgs e)
		//{
		//  if (e.Margin == BOOKMARK_MARGIN)
		//  {
		//      // Do we have a marker for this line?
		//      const uint mask = 1 << BOOKMARK_MARKER;
		//      var line = txtIn.Lines[txtIn.LineFromPosition(e.Position)];

		//      if ((line.MarkerGet() & mask) > 0)
		//      {
		//          // Remove existing bookmark
		//          line.MarkerDelete(BOOKMARK_MARKER);
		//      }
		//      else
		//      {
		//          // Add bookmark
		//          line.MarkerAdd(BOOKMARK_MARKER);
		//      }
		//  }
		//}

		private async void Timer_Tick(object sender, EventArgs e)
		{
			// Flush the debounced scratch autosave once typing has paused, keeping disk writes off the
			// per-keystroke path.
			if (scratchAutosavePending && (DateTime.UtcNow - lastKeyTime).TotalSeconds >= updateFreqSeconds)
			{
				scratchAutosavePending = false;
				AutosaveScratchDocument();
			}

			if (!isCompiling && (force || ((DateTime.UtcNow - lastKeyTime).TotalSeconds >= updateFreqSeconds && lastKeyTime > lastCompileTime)) && txtIn.Text != "")
			{
				timer.Enabled = false;
				isCompiling = true;
				lastCompileTime = DateTime.UtcNow;
				var oldIndex = txtOut.FirstVisibleLine;

				try
				{
					await KeyviewCompilerRunner.RunCompile(
						txtIn.Text,
						KeyviewCompilerRunner.IncludeDirFor(document),
						ch,
						bytes => compiledBytes = bytes,
						code => fullCode = code,
						code => trimmedCode = code,
						trimend,
						trimstr,
						SetStart,
						SetSuccess,
						SetFailure,
						text => tslCodeStatus.Text = text,
						Refresh,
						SetTxtOut,
						() => chkFullCode.Checked,
						() => btnRunScript.Enabled = false,
						() => btnRunScript.Enabled = true,
						AutosaveScratchDocument,
						() => oldIndex = txtOut.FirstVisibleLine,
						() => txtOut.FirstVisibleLine = oldIndex);
				}
				finally
				{
					isCompiling = false;
					timer.Enabled = true;
				}
			}

			if (force)
				force = false;
		}

		private void RunScript_Click(object sender, EventArgs e) => RunStopScript();

		private void RunStopScript()
		{
			if (scriptProcess != null)
			{
				scriptProcess.Kill();
				scriptProcess = null;
			}

			if (btnRunScript.Text == btnRunScriptText["Stop"])
				return;

			if (compiledBytes == null)
			{
				_ = MessageBox.Show("Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			scriptProcess = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = GetKeysharpExecutable(),
					Arguments = "--assembly *",
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};
			scriptProcess.EnableRaisingEvents = true;
			scriptProcess.Exited += (object sender, EventArgs e) =>
			{
				toolStrip1.Invoke((() =>
				{
					btnRunScript.Text = btnRunScriptText["Run"];
				}));
				scriptProcess = null;
			};
			_ = scriptProcess.Start();

			// Write the raw assembly bytes and close stdin; the child ("--assembly *") reads to EOF.
			using (var stdin = scriptProcess.StandardInput.BaseStream)
			{
				stdin.Write(compiledBytes, 0, compiledBytes.Length);
				stdin.Flush();
			}

			btnRunScript.Text = btnRunScriptText["Stop"];
		}

		private static string GetKeysharpExecutable() => "Keysharp.exe";

		private void TxtIn_DragDrop(object sender, DragEventArgs e)
		{
			var data = e.Data.GetData(DataFormats.FileDrop);

			if (data is string[] filenames)
			{
				try
				{
					if (filenames.Length > 0)
					{
						if (ConfirmDiscardChanges())
							LoadDataFromFile(filenames[0]);
					}
				}
				catch (Exception ex)
				{
					_ = Dialogs.MsgBox($"Unable to load file: {ex.Message}");
				}
			}
		}

		private void TxtIn_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
			else
				e.Effect = DragDropEffects.None;
		}

		private void txtIn_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.F5)
				force = true;
			else if (ReferenceEquals(sender, txtIn))
				lastKeyTime = DateTime.UtcNow;
		}

		private void txtIn_MarginClick(object sender, MarginClickEventArgs e)
		{
			var txt = sender as Scintilla;

			if (e.Margin == BOOKMARK_MARGIN)
			{
				// Do we have a marker for this line?
				const uint mask = 1 << BOOKMARK_MARKER;
				var line = txt.Lines[txt.LineFromPosition(e.Position)];

				if ((line.MarkerGet() & mask) > 0)
				{
					// Remove existing bookmark
					line.MarkerDelete(BOOKMARK_MARKER);
				}
				else
				{
					// Add bookmark
					_ = line.MarkerAdd(BOOKMARK_MARKER);
				}
			}
		}

		private void txtIn_TextChanged(object sender, EventArgs e)
		{
			UpdateNumberMarginWidth(txtIn);
			lastKeyTime = DateTime.UtcNow;

			if (!suppressDocumentChange)
			{
				// Read Text once (Scintilla rebuilds the whole string) and debounce the scratch autosave
				// onto the timer instead of writing the file to disk on every keystroke.
				UpdateDocumentUi(txtIn.Text);
				scratchAutosavePending = true;
			}
		}

		private void txtOut_TextChanged(object sender, EventArgs e)
		{
			UpdateNumberMarginWidth(txtOut);
		}

		private void txtOut_KeyDown(object sender, KeyEventArgs e) => txtIn_KeyDown(sender, e);

		private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
		{
			if (HotKeyManager.IsHotkey(e, Keys.Enter))
			{
				SearchManager.Find(true, false);
			}

			if (HotKeyManager.IsHotkey(e, Keys.Enter, true) || HotKeyManager.IsHotkey(e, Keys.Enter, false, true))
			{
				SearchManager.Find(false, false);
			}
		}

		private void TxtSearch_TextChanged(object sender, EventArgs e) => SearchManager.Find(true, true);

		private void Uppercase()
		{
			var start = txtIn.SelectionStart;
			var end = txtIn.SelectionEnd;
			txtIn.ReplaceSelection(txtIn.GetTextRange(start, end - start).ToUpper());
			txtIn.SetSelection(start, end);
			lastKeyTime = DateTime.UtcNow;
		}

		private void uppercaseSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Uppercase();

		private void wordWrapToolStripMenuItem1_Click(object sender, EventArgs e)
		{
			wordWrapItem.Checked = !wordWrapItem.Checked;
			txtOut.WrapMode = txtIn.WrapMode = wordWrapItem.Checked ? WrapMode.Word : WrapMode.None;
		}

		private void zoom100ToolStripMenuItem_Click(object sender, EventArgs e) => ZoomDefault();

		private void ZoomDefault()
		{
			txtIn.Zoom = 0;
			txtOut.Zoom = 0;
		}

		private void ZoomIn()
		{
			txtIn.ZoomIn();
			txtOut.ZoomIn();
			UpdateNumberMarginWidth(txtIn);
			UpdateNumberMarginWidth(txtOut);
		}

		private void zoomInToolStripMenuItem_Click(object sender, EventArgs e) => ZoomIn();

		private void ZoomOut()
		{
			txtIn.ZoomOut();
			txtOut.ZoomOut();
			UpdateNumberMarginWidth(txtIn);
			UpdateNumberMarginWidth(txtOut);
		}

		private void zoomOutToolStripMenuItem_Click(object sender, EventArgs e) => ZoomOut();
	}
#endif

#if !WINDOWS
	internal sealed class Keyview : Eto.Forms.Form
	{
		private readonly struct TextSnapshot
		{
			public TextSnapshot(string text, int selectionStart, int selectionLength)
			{
				Text = text ?? "";
				SelectionStart = selectionStart;
				SelectionLength = selectionLength;
			}

			public string Text { get; }
			public int SelectionStart { get; }
			public int SelectionLength { get; }
		}

		private readonly Stack<TextSnapshot> undoStack = new ();
		private readonly Stack<TextSnapshot> redoStack = new ();
		private readonly RichTextArea inputArea = new ();
		private readonly RichTextArea outputArea = new ();
		private readonly SyntaxHighlighter inputHighlighter = SyntaxHighlighter.ForKeysharp();
		private readonly SyntaxHighlighter outputHighlighter = SyntaxHighlighter.ForCSharp();
		private readonly UITimer highlightTimer = new ();
		private readonly TextBox searchBox = new ();
		private readonly Button nextSearchButton = new () { Text = "Next" };
		private readonly Button prevSearchButton = new () { Text = "Prev" };
		private readonly Button closeSearchButton = new () { Text = "Close" };
		private readonly CheckBox fullCodeCheck = new () { Text = "Full code" };
		private readonly Button copyFullCodeButton = new () { Text = "Copy full code" };
		private readonly Button runScriptButton = new () { Text = "▶ Run script (F9)" };
		private readonly Button compileScriptButton = new () { Text = "Compile .cks" };
		private readonly Label codeStatusLabel = new () { Text = "", Wrap = WrapMode.None };
		private readonly Label documentStatusLabel = new () { Text = "", Wrap = WrapMode.None };
		private readonly Panel searchPanel = new ();
		private readonly ButtonMenuItem undoMenuItem = new () { Text = "&Undo" };
		private readonly ButtonMenuItem redoMenuItem = new () { Text = "&Redo" };
#if OSX
		private readonly ButtonMenuItem saveMenuItem = new () { Text = "&Save", Shortcut = Eto.Forms.Keys.Application | Eto.Forms.Keys.S };
#else
		private readonly ButtonMenuItem saveMenuItem = new () { Text = "&Save", Shortcut = Eto.Forms.Keys.Control | Eto.Forms.Keys.S };
#endif
		private readonly ButtonMenuItem compileMenuItem = new () { Text = "&Compile .cks" };
		private Splitter editorSplitter;
		private StackLayout statusRightCluster;
		private readonly UITimer timer = new ();
		private readonly IScriptCompiler ch = KeyviewCompilerRunner.GetCompiler();
		private byte[] compiledBytes;
		private readonly char[] trimend = ['\n', '\r'];
		private readonly string trimstr = "{}\t";
		private readonly double updateFreqSeconds = 1;
		private readonly string lastrun;
		private readonly KeyviewDocumentState document = new ();
		private readonly Dictionary<string, string> runScriptText = new ()
		{
			{ "Run", "▶ Run script (F9)" },
			{ "Stop", "◾️ Stop script (F9)" }
		};

		private bool force;
		private bool isCompiling;
		private string fullCode = "";
		private DateTime lastCompileTime = DateTime.UtcNow;
		private DateTime lastKeyTime = DateTime.UtcNow;
		private bool searchIsOpen;
		private string trimmedCode = "";
		private Process scriptProcess;
		// Shown in the output box (replacing the generated C#) the first time a running script emits output.
		// The C# stays available via "Copy full code" because fullCode/trimmedCode are left untouched.
		private const string ScriptOutputHeader = "─── Script output ───\n\n";
		private string lastSearch = "";
		private int lastSearchIndex;
		private bool suppressUndo;
		private string lastText = "";
		private int lastSelectionStart;
		private int lastSelectionLength;
		private string baseTitle;
		private bool suppressDocumentChange;
		private bool highlighting;
		private bool outputHighlighting;
		// True while the output box is displaying a running script's output rather than generated C#.
		// Cleared whenever the compiler writes C# back into the box, so a still-running (now stale)
		// script can't clobber the freshly displayed code with its continued output.
		private bool scriptOwnsOutput;
		private int highlightBaseLength;
		private bool closing;

		public Keyview(string initialFile = null)
		{
			lastrun = $"{Accessors.A_AppData}/Keysharp/lastkeyviewrun.txt";
			Title = $"Keyview {Assembly.GetExecutingAssembly().GetName().Version}";
			baseTitle = Title;
			InitializeWindowIcon();
			ShowInTaskbar = true;
			Resizable = true;
			Minimizable = true;
			Maximizable = true;
			WindowStyle = WindowStyle.Default;
			ClientSize = new Eto.Drawing.Size(1400, 900);

			InitializeMenu();
			InitializeEditors();
			InitializeSearchPanel();
			InitializeStatusBar();
			InitializeLayout();

			timer.Interval = updateFreqSeconds;
			timer.Elapsed += Timer_Elapsed;
			timer.Start();

			Shown += (_, _) =>
			{
				InitializeWindowIcon();
				EtoExtensions.SetWaylandAppId(this, "keyview");
				FitToScreen();
				if (editorSplitter != null)
					editorSplitter.Position = Math.Max(200, ClientSize.Width / 2);
				// Defer until after the first layout pass so the cluster reports its real width.
				Application.Instance.AsyncInvoke(LockStatusBarMinimums);
			};

			Closing += (_, e) =>
			{
				if (!ConfirmDiscardChanges())
				{
					e.Cancel = true;
					return;
				}

				// Save while the editor text is still valid. Once the window starts tearing down, the
				// text-area buffer is gone and reads as empty, so block any later autosave (e.g. a
				// debounce tick or compile callback) from overwriting the file with that empty string.
				AutosaveScratchDocument();
				closing = true;
			};
			Closed += (_, _) =>
			{
				timer.Stop();
				highlightTimer.Stop();
				try
				{
					scriptProcess?.Kill();
				}
				catch
				{
				}
#if OSX
				Eto.Mac.AppDelegate.FileOpened -= MacFileOpened;
				Application.Instance.Quit();
#endif
			};

			if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile))
			{
				LoadDataFromFile(initialFile);
			}
			else if (File.Exists(lastrun))
			{
				LoadScratchDocument();
			}
			else
				UpdateDocumentUi();

#if OSX
			Eto.Mac.AppDelegate.FileOpened += MacFileOpened;
#endif
		}

		private void InitializeMenu()
		{
			var fileMenu = new ButtonMenuItem { Text = "&File" };
			var openItem = new ButtonMenuItem { Text = "&Open..." };
			openItem.Click += (_, _) => OpenFile();
			saveMenuItem.Click += (_, _) => SaveDocument();
			compileMenuItem.Click += (_, _) => CompileDocument();
			fileMenu.Items.AddRange(new MenuItem[] { openItem, saveMenuItem, compileMenuItem });

			var editMenu = new ButtonMenuItem { Text = "&Edit" };
			undoMenuItem.Click += (_, _) => Undo();
			redoMenuItem.Click += (_, _) => Redo();
			var cutItem = new ButtonMenuItem { Text = "Cu&t" };
			cutItem.Click += (_, _) => { CutSelection(); lastKeyTime = DateTime.UtcNow; };
			var copyItem = new ButtonMenuItem { Text = "&Copy" };
			copyItem.Click += (_, _) => { CopySelection(); lastKeyTime = DateTime.UtcNow; };
			var pasteItem = new ButtonMenuItem { Text = "&Paste" };
			pasteItem.Click += (_, _) => { PasteFromClipboard(); lastKeyTime = DateTime.UtcNow; };
			var selectLineItem = new ButtonMenuItem { Text = "Select &Line" };
			selectLineItem.Click += (_, _) => SelectLine();
			var selectAllItem = new ButtonMenuItem { Text = "Select &All" };
			selectAllItem.Click += (_, _) => inputArea.SelectAll();
			var clearSelectionItem = new ButtonMenuItem { Text = "Clea&r Selection" };
			clearSelectionItem.Click += (_, _) =>
			{
				var (start, _) = GetSelection();
				SetSelection(start, 0);
				lastKeyTime = DateTime.UtcNow;
			};
			var indentItem = new ButtonMenuItem { Text = "&Indent" };
			indentItem.Click += (_, _) => AdjustIndent(false);
			var outdentItem = new ButtonMenuItem { Text = "&Outdent" };
			outdentItem.Click += (_, _) => AdjustIndent(true);
			var uppercaseItem = new ButtonMenuItem { Text = "&Uppercase" };
			uppercaseItem.Click += (_, _) => TransformSelection(s => s.ToUpperInvariant());
			var lowercaseItem = new ButtonMenuItem { Text = "&Lowercase" };
			lowercaseItem.Click += (_, _) => TransformSelection(s => s.ToLowerInvariant());
			editMenu.Items.AddRange(new MenuItem[]
			{
				undoMenuItem, redoMenuItem, new SeparatorMenuItem(),
				cutItem, copyItem, pasteItem, new SeparatorMenuItem(),
				selectLineItem, selectAllItem, clearSelectionItem, new SeparatorMenuItem(),
				indentItem, outdentItem, new SeparatorMenuItem(),
				uppercaseItem, lowercaseItem
			});

			var searchMenu = new ButtonMenuItem { Text = "&Search" };
			var findItem = new ButtonMenuItem { Text = "&Quick Find..." };
			findItem.Click += (_, _) => OpenSearch();
			searchMenu.Items.Add(findItem);

			var viewMenu = new ButtonMenuItem { Text = "&View" };
			var wordWrapItem = new CheckMenuItem { Text = "&Word Wrap", Checked = true };
			wordWrapItem.Click += (_, _) => SetWordWrap(wordWrapItem.Checked == true);
			var zoomInItem = new ButtonMenuItem { Text = "Zoom &In" };
			zoomInItem.Click += (_, _) => ZoomIn();
			var zoomOutItem = new ButtonMenuItem { Text = "Zoom &Out" };
			zoomOutItem.Click += (_, _) => ZoomOut();
			var zoomDefaultItem = new ButtonMenuItem { Text = "&Zoom 100%" };
			zoomDefaultItem.Click += (_, _) => ZoomDefault();
			viewMenu.Items.AddRange(new MenuItem[]
			{
				wordWrapItem, new SeparatorMenuItem(),
				zoomInItem, zoomOutItem, zoomDefaultItem
			});

			Menu = new Eto.Forms.MenuBar
			{
				Items = { fileMenu, editMenu, searchMenu, viewMenu }
			};
		}

		private void InitializeEditors()
		{
#if OSX
			var font = TryMonospaceFont(13);
#else
			var font = TryMonospaceFont(10);
#endif
			inputArea.Font = font;
			outputArea.Font = font;
			inputArea.TextReplacements = TextReplacements.None;
			outputArea.ReadOnly = true;
			inputArea.Wrap = true;
			outputArea.Wrap = true;
			inputArea.TextChanged += InputArea_TextChanged;
			// Debounce input highlighting so a full re-color runs once typing pauses rather than
			// on every keystroke (important for large scripts).
			highlightTimer.Interval = 0.25;
			highlightTimer.Elapsed += HighlightTimer_Elapsed;
#if LINUX
			KeyDown += InputArea_KeyDown;
#else
			inputArea.KeyDown += InputArea_KeyDown;
			outputArea.KeyDown += InputArea_KeyDown;
#endif
			inputArea.MouseUp += (_, _) => UpdateSelectionSnapshot();
		}

		private static Font TryMonospaceFont(float size)
		{
			var candidates = new[]
			{
				"Consolas",
				"JetBrains Mono",
				"Fira Code",
				"DejaVu Sans Mono",
				"Liberation Mono",
				"Monospace"
			};

			foreach (var name in candidates)
			{
				try
				{
					return new Font(name, size);
				}
				catch
				{
				}
			}

			return SystemFonts.Default(size);
		}

		private void InitializeSearchPanel()
		{
			searchBox.Width = 280;
			searchBox.TextChanged += (_, _) => UpdateSearchText();
			searchBox.KeyDown += SearchBox_KeyDown;
			nextSearchButton.Click += (_, _) => Find(true, false);
			prevSearchButton.Click += (_, _) => Find(false, false);
			closeSearchButton.Click += (_, _) => CloseSearch();
			searchPanel.Content = new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6,
				Items = { searchBox, prevSearchButton, nextSearchButton, closeSearchButton }
			};
			searchPanel.Visible = false;
		}

		private void InitializeStatusBar()
		{
			fullCodeCheck.CheckedChanged += (_, _) => UpdateOutputFromCache();
			copyFullCodeButton.Click += (_, _) => CopyFullCode();
			runScriptButton.Click += (_, _) => RunStopScript();
			runScriptButton.Enabled = false;
			compileScriptButton.Click += (_, _) => CompileDocument();
			// No fixed width: the filename label is the only flexible item in the status bar and is
			// allowed to shrink down to the window minimum. See InitializeLayout and
			// LockStatusBarMinimums for how the right-hand controls are kept at their natural size.
#if LINUX
			// Eto's GTK label paints its full text past its allocation instead of clipping, so a long
			// path would draw over the controls to its right as the window narrows. Ellipsizing makes
			// the native label truncate cleanly (with …) within whatever width it is given.
			if (documentStatusLabel.ControlObject is Gtk.Label nativeStatusLabel)
				nativeStatusLabel.Ellipsize = Pango.EllipsizeMode.Middle;
#endif
			codeStatusLabel.Text = "";
		}

		private void InitializeLayout()
		{
			editorSplitter = new Splitter
			{
				Orientation = Orientation.Horizontal,
				SplitterWidth = 2,
				FixedPanel = SplitterFixedPanel.None,
				RelativePosition = 0.5,
				Panel1MinimumSize = 200,
				Panel2MinimumSize = 200,
				Panel1 = new Panel { Content = inputArea },
				Panel2 = new Panel { Content = outputArea }
			};

			// The compile status and action buttons live in their own cluster so they can be pinned to
			// their natural width (see LockStatusBarMinimums). That keeps them from compacting or
			// disappearing as the window narrows.
			statusRightCluster = new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				Items =
				{
					new Label { Text = "Code compile:", Wrap = WrapMode.None },
					codeStatusLabel,
					fullCodeCheck,
					copyFullCodeButton,
					compileScriptButton,
					runScriptButton
				}
			};

			// The filename label is the lone Expand item, so all the slack (and all the shrink) is
			// applied to it: it grows to fill and clips its text as the window narrows, while the
			// cluster on the right stays put.
			var statusRow = new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				Items =
				{
					new StackLayoutItem(documentStatusLabel) { Expand = true },
					statusRightCluster
				}
			};

			var statusContainer = new Panel
			{
				Padding = new Padding(8, 6, 8, 6),
				Content = statusRow
			};

			Content = new TableLayout
			{
				Spacing = new Eto.Drawing.Size(0, 0),
				Rows =
				{
					new TableRow(searchPanel),
					new TableRow(editorSplitter) { ScaleHeight = true },
					new TableRow(statusContainer)
				}
			};
		}

		private void InitializeWindowIcon()
		{
			var icon = TryLoadIcon();
			if (icon != null)
				Icon = icon;
		}

		private static Icon TryLoadIcon()
		{
			foreach (var candidate in EnumerateIconPaths("Keysharp.png"))
			{
				if (!File.Exists(candidate))
					continue;

				try
				{
					return new Icon(1f, new Bitmap(candidate));
				}
				catch
				{
				}
			}

			foreach (var candidate in EnumerateIconPaths("Keysharp.ico"))
			{
				if (!File.Exists(candidate))
					continue;

				try
				{
					return new Icon(candidate);
				}
				catch
				{
					// Gtk cannot load some compressed .ico files; ignore.
				}
			}

			return null;
		}

		private static IEnumerable<string> EnumerateIconPaths(string fileName)
		{
			var candidates = new List<DirectoryInfo>();
			var appBase = new DirectoryInfo(AppContext.BaseDirectory);
			candidates.Add(appBase);
			if (!string.IsNullOrEmpty(Environment.CurrentDirectory))
				candidates.Add(new DirectoryInfo(Environment.CurrentDirectory));
			if (!string.IsNullOrEmpty(Environment.ProcessPath))
				candidates.Add(new DirectoryInfo(Path.GetDirectoryName(Environment.ProcessPath)));

			foreach (var baseDir in candidates.Where(dir => dir.Exists))
			{
				for (var current = baseDir; current != null; current = current.Parent)
				{
					yield return Path.Combine(current.FullName, fileName);
					yield return Path.Combine(current.FullName, "Keysharp", fileName);
				}
			}
		}

		private void FitToScreen()
		{
			var screen = Eto.Forms.Screen.PrimaryScreen;
			if (screen == null)
				return;

			var wayland = Keysharp.Internals.Platform.Desktop.IsWaylandSession;

			// "reliable" = the area excludes panels/docks, so we can size-and-center the window to fit it.
			// On X11/macOS Eto's WorkingArea already does. On Wayland a client can't compute it itself
			// (gdk_monitor_get_workarea returns the full monitor), so we ask the compositor backend; if no
			// backend can answer we fall back to maximizing and letting the compositor size the window.
			RectangleF area = RectangleF.Empty;
			bool reliable = false, got = false;

#if LINUX
			if (wayland
				&& Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Current is { } backend
				&& backend.TryGetWorkArea(out var wa) && wa.Width > 0 && wa.Height > 0)
			{
				area = new RectangleF(wa.X, wa.Y, wa.Width, wa.Height);
				reliable = true;
				got = true;
			}
#endif
			if (!got)
			{
				try { area = screen.WorkingArea; reliable = !wayland; got = true; }
				catch
				{
					try { area = screen.Bounds; got = true; } catch { return; }
				}
			}

			// Keep the OUTER window (titlebar/borders included) within the work area. The server-side
			// titlebar a Wayland compositor draws can be slightly taller than the frame Eto reports, so
			// leave a small margin there.
			var decoW = Math.Max(0, Size.Width - ClientSize.Width);
			var decoH = Math.Max(0, Size.Height - ClientSize.Height);
			var margin = wayland ? 16 : 0;
			var maxClientW = (int)area.Width - decoW;
			var maxClientH = (int)area.Height - decoH - margin;

			if (reliable && maxClientW >= 400 && maxClientH >= 300)
			{
				ClientSize = new Eto.Drawing.Size(
					Math.Min(ClientSize.Width, maxClientW),
					Math.Min(ClientSize.Height, maxClientH));
				Location = new Point(
					(int)area.X + Math.Max(0, ((int)area.Width - Size.Width) / 2),
					(int)area.Y + Math.Max(0, ((int)area.Height - Size.Height) / 2));
				return;
			}

			// No reliable work area: if the preferred size doesn't fit, maximize (the compositor then sizes
			// to its own work area, correctly excluding panels — and it matches the Windows build, which
			// opens Keyview maximized); otherwise center.
			if (ClientSize.Width > area.Width || ClientSize.Height > area.Height)
			{
				var w = Math.Max(400, (int)Math.Min(ClientSize.Width, area.Width));
				var h = Math.Max(300, (int)Math.Min(ClientSize.Height, area.Height));
				ClientSize = new Eto.Drawing.Size(w, h);
				WindowState = WindowState.Maximized;
				return;
			}

			Location = new Point(
				(int)area.X + Math.Max(0, (int)(area.Width - Size.Width) / 2),
				(int)area.Y + Math.Max(0, (int)(area.Height - Size.Height) / 2));
		}

		// Width below which the file-name label has shrunk as far as we let it; this is what defines
		// Keyview's minimum width.
		private const int FilenameLabelFloor = 80;

		// Pins the status bar's right-hand cluster to its natural width and locks the window's minimum
		// size, so narrowing the window shrinks only the file-name label (clipping its text) until it
		// reaches FilenameLabelFloor, at which point the window can shrink no further. Without this the
		// labels collapse to nothing (Eto reports a 0 minimum width for labels) and the buttons clip.
		private void LockStatusBarMinimums()
		{
			if (statusRightCluster == null)
				return;

			var clusterWidth = statusRightCluster.Width;
			if (clusterWidth <= 0)
				return;

			// Give the cluster a hard minimum equal to its natural width so its labels and buttons never
			// compact. It can still grow past this (stealing space from the file-name label) when, say,
			// the compile-status text lengthens.
			statusRightCluster.MinimumSize = new Eto.Drawing.Size(clusterWidth, 0);

			const int rowSpacing = 8;       // StackLayout spacing between the label and the cluster
			const int containerPadding = 16; // statusContainer left + right padding (8 + 8)
			const int editorMinWidth = 402;  // editorSplitter Panel1/Panel2 minimums (200 + 200) + handle
			var statusMinWidth = clusterWidth + rowSpacing + FilenameLabelFloor + containerPadding;
			MinimumSize = new Eto.Drawing.Size(Math.Max(statusMinWidth, editorMinWidth), 200);
		}

		private void InputArea_TextChanged(object sender, EventArgs e)
		{
			// Applying highlight tags raises spurious TextChanged events (same length); ignore those.
			// A real edit typed while a pumped highlight yields changes the length, so let it through
			// (the highlighter notices the change and aborts to avoid applying stale offsets).
			if (highlighting && inputArea.TextLength == highlightBaseLength)
				return;

			// Read the buffer once: inputArea.Text rebuilds the whole string, so calling it per consumer
			// (undo, dirty check, autosave) on every keystroke is a major cost on large scripts.
			var text = inputArea.Text ?? "";
			RecordUndoSnapshot(text);
			lastKeyTime = DateTime.UtcNow;

			if (!suppressDocumentChange)
				UpdateDocumentUi(text);

			RequestInputHighlight();
		}

		private void RequestInputHighlight()
		{
			// Restart the idle timer; highlighting fires once it stops being reset.
			highlightTimer.Stop();
			highlightTimer.Start();
		}

		private void HighlightTimer_Elapsed(object sender, EventArgs e)
		{
			highlightTimer.Stop();
			// Autosave is debounced onto this idle tick instead of running on every keystroke.
			AutosaveScratchDocument();
			HighlightInput();
		}

		private void HighlightInput()
		{
			if (highlighting)
				return;

			highlighting = true;
			highlightBaseLength = inputArea.TextLength;
			try
			{
				// Yield to the UI loop periodically so re-highlighting a large script doesn't block typing.
				inputHighlighter.Highlight(inputArea, Application.Instance.RunIteration);
			}
			finally
			{
				highlighting = false;
			}
		}

		private void InputArea_KeyDown(object sender, KeyEventArgs e)
		{
			var inputIsTarget = ReferenceEquals(sender, inputArea) || inputArea.HasFocus;

			if (inputIsTarget)
				UpdateSelectionSnapshot();

			if (e.Key == Keys.F5)
			{
				force = true;
				return;
			}

			if (e.Key == Keys.F9)
			{
				RunStopScript();
				e.Handled = true;
				return;
			}

			if (e.Key == Keys.Escape && searchIsOpen)
			{
				CloseSearch();
				e.Handled = true;
				return;
			}

			if (inputIsTarget)
			{
				if (e.Key == Keys.Z && e.Modifiers == Keys.Control)
				{
					Undo();
					e.Handled = true;
					return;
				}

				if (e.Key == Keys.Y && e.Modifiers == Keys.Control)
				{
					Redo();
					e.Handled = true;
					return;
				}

				if (e.Key == Keys.Z && e.Modifiers == (Keys.Control | Keys.Shift))
				{
					Redo();
					e.Handled = true;
					return;
				}
			}

			if (e.Modifiers == Keys.Control)
			{
				switch (e.Key)
				{
					case Keys.F:
						OpenSearch();
						e.Handled = true;
						break;
					case Keys.U:
						TransformSelection(s => s.ToUpperInvariant());
						e.Handled = true;
						break;
					case Keys.L:
						TransformSelection(s => s.ToLowerInvariant());
						e.Handled = true;
						break;
					case Keys.Equal:
						ZoomIn();
						e.Handled = true;
						break;
					case Keys.Minus:
						ZoomOut();
						e.Handled = true;
						break;
					case Keys.D0:
						ZoomDefault();
						e.Handled = true;
						break;
				}
			}
		}

		private void SearchBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Keys.Enter && e.Modifiers == Keys.None)
			{
				Find(true, false);
				e.Handled = true;
				return;
			}

			if (e.Key == Keys.Enter && (e.Modifiers == Keys.Shift || e.Modifiers == Keys.Control))
			{
				Find(false, false);
				e.Handled = true;
			}
		}

		private void CopySelection()
		{
			var selection = inputArea.Selection;
			if (selection.Length() <= 0)
				return;
			Clipboard.Instance.Text = inputArea.Text?.Substring(selection.Start, selection.Length()) ?? "";
		}

		private void CutSelection()
		{
			var selection = inputArea.Selection;
			if (selection.Length() <= 0)
				return;
			CopySelection();
			var text = inputArea.Text ?? "";
			inputArea.Text = text.Remove(selection.Start, selection.Length());
			SetSelection(selection.Start, 0);
		}

		private void PasteFromClipboard()
		{
			var clip = Clipboard.Instance.Text ?? "";
			if (clip.Length == 0)
				return;
			var (start, length) = GetSelection();
			var text = inputArea.Text ?? "";
			var before = text.Substring(0, start);
			var after = text.Substring(start + length);
			inputArea.Text = before + clip + after;
			SetSelection(start + clip.Length, 0);
		}

			private (int start, int length) GetSelection()
			{
				var selection = inputArea.Selection;
				var start = Math.Max(0, selection.Start);
				var end = Math.Max(0, selection.End);
			if (end < start)
				(end, start) = (start, end);
			return (start, end - start);
		}

			private void SetSelection(int start, int length)
			{
				var safeStart = Math.Max(0, start);
				var safeLength = Math.Max(0, length);
				// Eto.Range is inclusive at both ends; use start+length-1 for a length-based selection.
				var end = safeLength > 0 ? safeStart + safeLength - 1 : safeStart - 1;
				inputArea.Selection = new Range<int>(safeStart, end);
			}

		private void UpdateSelectionSnapshot()
		{
			var (start, length) = GetSelection();
			lastSelectionStart = start;
			lastSelectionLength = length;
		}

		private TextSnapshot CaptureSnapshot()
		{
			var (start, length) = GetSelection();
			return new TextSnapshot(inputArea.Text ?? "", start, length);
		}

		private void ApplySnapshot(TextSnapshot snapshot)
		{
			suppressUndo = true;
			try
			{
				inputArea.Text = snapshot.Text ?? "";
				var textLength = inputArea.Text?.Length ?? 0;
				var start = Math.Min(Math.Max(0, snapshot.SelectionStart), textLength);
				var length = Math.Min(Math.Max(0, snapshot.SelectionLength), Math.Max(0, textLength - start));
				SetSelection(start, length);
				lastText = inputArea.Text ?? "";
				lastSelectionStart = start;
				lastSelectionLength = length;
			}
			finally
			{
				suppressUndo = false;
			}

			lastKeyTime = DateTime.UtcNow;
			UpdateUndoRedoState();
		}

		private void RecordUndoSnapshot(string currentText)
		{
			if (suppressUndo)
				return;

			if (currentText == lastText)
				return;

			undoStack.Push(new TextSnapshot(lastText, lastSelectionStart, lastSelectionLength));
			redoStack.Clear();
			lastText = currentText;
			UpdateSelectionSnapshot();
			UpdateUndoRedoState();
		}

		private void ResetUndoHistory()
		{
			undoStack.Clear();
			redoStack.Clear();
			lastText = inputArea.Text ?? "";
			UpdateSelectionSnapshot();
			UpdateUndoRedoState();
		}

		private void UpdateUndoRedoState()
		{
			var canUndo = undoStack.Count > 0;
			var canRedo = redoStack.Count > 0;
			undoMenuItem.Enabled = canUndo;
			redoMenuItem.Enabled = canRedo;
		}

		private void Undo()
		{
			if (undoStack.Count == 0)
				return;

			redoStack.Push(CaptureSnapshot());
			ApplySnapshot(undoStack.Pop());
		}

		private void Redo()
		{
			if (redoStack.Count == 0)
				return;

			undoStack.Push(CaptureSnapshot());
			ApplySnapshot(redoStack.Pop());
		}

		private void OpenSearch()
		{
			if (!searchIsOpen)
			{
				searchIsOpen = true;
				searchPanel.Visible = true;
				searchBox.Text = lastSearch;
			}

			searchBox.Focus();
			searchBox.SelectAll();
		}

		private void CloseSearch()
		{
			searchIsOpen = false;
			searchPanel.Visible = false;
		}

		private void UpdateSearchText()
		{
			lastSearch = searchBox.Text ?? "";
			lastSearchIndex = 0;
		}

		private void Find(bool next, bool incremental)
		{
			var needle = searchBox.Text ?? "";
			lastSearch = needle;

			if (string.IsNullOrEmpty(needle))
				return;

			var text = inputArea.Text ?? "";
			var (selectionStart, selectionLength) = GetSelection();
			var searchStart = next
				? (incremental ? Math.Max(0, lastSearchIndex - 1) : selectionStart + selectionLength)
				: Math.Max(0, selectionStart - 1);

			int index;

			if (next)
			{
				index = text.IndexOf(needle, searchStart, StringComparison.CurrentCulture);
				if (index == -1 && searchStart > 0)
					index = text.IndexOf(needle, 0, StringComparison.CurrentCulture);
			}
			else
			{
				index = text.LastIndexOf(needle, searchStart, StringComparison.CurrentCulture);
				if (index == -1 && text.Length > 0)
					index = text.LastIndexOf(needle, text.Length - 1, StringComparison.CurrentCulture);
			}

				if (index == -1)
				{
					SetSelection(0, 0);
					return;
				}

				lastSearchIndex = index + needle.Length;
				SetSelection(index, needle.Length);
				var end = needle.Length > 0 ? index + needle.Length - 1 : index;
				inputArea.ScrollTo(new Range<int>(index, end));
				if (!incremental)
					inputArea.Focus();
			}

		private void SelectLine()
		{
			var caret = GetSelection().start;
			var text = inputArea.Text ?? "";
			var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
			var lineEnd = text.IndexOf('\n', caret);
			if (lineEnd < 0)
				lineEnd = text.Length;
			SetSelection(lineStart, Math.Max(0, lineEnd - lineStart));
		}

		private void AdjustIndent(bool outdent)
		{
			var text = inputArea.Text ?? "";
			var (selStart, selLength) = GetSelection();

			if (text.Length == 0)
				return;

			if (selLength == 0)
			{
				var lineStart = text.LastIndexOf('\n', Math.Max(0, selStart - 1)) + 1;
				var lineEnd = text.IndexOf('\n', selStart);
				if (lineEnd < 0)
					lineEnd = text.Length;

				selStart = lineStart;
				selLength = lineEnd - lineStart;
			}

			var startLine = text.LastIndexOf('\n', Math.Max(0, selStart - 1)) + 1;
			var endLine = text.IndexOf('\n', selStart + selLength);
			if (endLine < 0)
				endLine = text.Length;

			var before = text.Substring(0, startLine);
			var block = text.Substring(startLine, endLine - startLine);
			var after = text.Substring(endLine);
			var lines = block.Split('\n');

			for (var i = 0; i < lines.Length; i++)
			{
				if (outdent)
				{
					if (lines[i].StartsWith("\t", StringComparison.Ordinal))
						lines[i] = lines[i].Substring(1);
					else if (lines[i].StartsWith("  ", StringComparison.Ordinal))
						lines[i] = lines[i].Substring(2);
				}
				else
				{
					lines[i] = "\t" + lines[i];
				}
			}

			var newBlock = string.Join("\n", lines);
			inputArea.Text = before + newBlock + after;
			SetSelection(startLine, newBlock.Length);
			lastKeyTime = DateTime.UtcNow;
		}

		private void TransformSelection(Func<string, string> transform)
		{
			var text = inputArea.Text ?? "";
			var (start, length) = GetSelection();

			if (length <= 0)
				return;

			var selected = text.Substring(start, length);
			var transformed = transform(selected);
			inputArea.Text = text.Substring(0, start) + transformed + text.Substring(start + length);
			SetSelection(start, transformed.Length);
			lastKeyTime = DateTime.UtcNow;
		}

		private void SetWordWrap(bool enabled)
		{
			inputArea.Wrap = enabled;
			outputArea.Wrap = enabled;
		}

		private void ZoomDefault()
		{
#if OSX
			var font = TryMonospaceFont(13);
#else
			var font = TryMonospaceFont(10);
#endif
			inputArea.Font = font;
			outputArea.Font = font;
		}

		private void ZoomIn()
		{
			inputArea.Font = new Font(inputArea.Font.Family, Math.Min(48, inputArea.Font.Size * 1.1f));
			outputArea.Font = new Font(outputArea.Font.Family, Math.Min(48, outputArea.Font.Size * 1.1f));
		}

		private void ZoomOut()
		{
			inputArea.Font = new Font(inputArea.Font.Family, Math.Max(6, inputArea.Font.Size / 1.1f));
			outputArea.Font = new Font(outputArea.Font.Family, Math.Max(6, outputArea.Font.Size / 1.1f));
		}

		private void CopyFullCode()
		{
			var text = string.IsNullOrEmpty(fullCode) ? outputArea.Text : fullCode;
			Clipboard.Instance.Text = text ?? "";
		}

		private void OpenFile()
		{
			if (!ConfirmDiscardChanges())
				return;

			var dialog = new OpenFileDialog();
			if (dialog.ShowDialog(this) == DialogResult.Ok)
			{
				LoadDataFromFile(dialog.FileName);
			}
		}

		private void LoadDataFromFile(string path)
		{
			if (!File.Exists(path))
				return;

			suppressDocumentChange = true;
			try
			{
				var fullPath = Path.GetFullPath(path);
				var text = File.ReadAllText(fullPath);
				inputArea.Text = text;
				document.LoadFile(fullPath, text);
				ResetUndoHistory();
			}
			finally
			{
				suppressDocumentChange = false;
			}

			lastKeyTime = DateTime.UtcNow;
			UpdateDocumentUi();
		}

		private void LoadScratchDocument()
		{
			suppressDocumentChange = true;
			try
			{
				inputArea.Text = File.Exists(lastrun) ? File.ReadAllText(lastrun) : "";
				document.LoadScratch();
				ResetUndoHistory();
			}
			finally
			{
				suppressDocumentChange = false;
			}

			lastKeyTime = DateTime.UtcNow;
			UpdateDocumentUi();
		}

		private bool ConfirmDiscardChanges()
		{
			if (!document.IsDirty(inputArea.Text))
				return true;

			var result = MessageBox.Show(
				this,
				$"Save changes to {document.DisplayName}?",
				"Keyview",
				MessageBoxButtons.YesNoCancel,
				MessageBoxType.Question);

			return result switch
			{
				DialogResult.Yes => SaveDocument(),
				DialogResult.No => true,
				_ => false
			};
		}

		private bool SaveDocument()
		{
			if (document.IsScratch)
				return false;

			try
			{
				File.WriteAllText(document.CurrentFilePath, inputArea.Text ?? "");
				document.MarkSaved(inputArea.Text);
				UpdateDocumentUi();
				return true;
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, $"Unable to save file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxType.Error);
				return false;
			}
		}

		private void CompileDocument()
		{
			if (!document.CanCompile || (document.IsDirty(inputArea.Text) && !SaveDocument()))
				return;

			compileScriptButton.Enabled = false;
			codeStatusLabel.Text = "Writing .cks...";

			if (KeyviewDocumentCompiler.TryCompile(document.CurrentFilePath, ch, out var outputPath, out var error))
			{
				codeStatusLabel.TextColor = Colors.Green;
				codeStatusLabel.Text = $"Wrote {outputPath}";
			}
			else
			{
				codeStatusLabel.TextColor = Colors.Red;
				codeStatusLabel.Text = "Compile failed";
				SetOutputText(error);
			}

			UpdateDocumentUi();
		}

		private void UpdateDocumentUi(string text = null)
		{
			text ??= inputArea.Text;
			var dirty = document.IsDirty(text);
			Title = document.GetWindowTitle(baseTitle, dirty);
			documentStatusLabel.Text = document.GetStatusText(dirty);
			saveMenuItem.Enabled = !document.IsScratch && dirty;
			compileMenuItem.Enabled = document.CanCompile;
			compileScriptButton.Visible = !document.IsScratch;
			compileScriptButton.Enabled = document.CanCompile;
		}

#if OSX
		private void MacFileOpened(string path)
		{
			Application.Instance.AsyncInvoke(() =>
			{
				if (ConfirmDiscardChanges())
					LoadDataFromFile(path);
			});
		}
#endif

		private void SetStart()
		{
			fullCode = trimmedCode = "";
			codeStatusLabel.TextColor = Colors.Black;
			codeStatusLabel.Text = "";
		}

		private void SetSuccess(double seconds)
		{
			codeStatusLabel.TextColor = Colors.Green;
			codeStatusLabel.Text = $"Ok ({seconds:F1}s)";
		}

		private void SetFailure()
		{
			codeStatusLabel.TextColor = Colors.Red;
			codeStatusLabel.Text = "Error";
			SetOutputText("");
		}

		private void SetOutputText(string text)
		{
			if (outputHighlighting)
				return; // ignore re-entrant updates (e.g. a Full-code toggle) while a pumped highlight runs

			scriptOwnsOutput = false; // the compiler is reclaiming the output box from any running script
			outputArea.Text = text;
			outputHighlighting = true;
			try
			{
				// Yield to the UI loop periodically so highlighting a large generated file doesn't freeze.
				outputHighlighter.Highlight(outputArea, Application.Instance.RunIteration);
			}
			finally
			{
				outputHighlighting = false;
			}
		}

		private void UpdateOutputFromCache()
		{
			var desired = fullCodeCheck.Checked == true ? fullCode : trimmedCode;
			if (string.IsNullOrEmpty(desired))
				return;
			SetOutputText(desired);
		}

		private async void Timer_Elapsed(object sender, EventArgs e)
		{
			if (!isCompiling && (force || ((DateTime.UtcNow - lastKeyTime).TotalSeconds >= updateFreqSeconds && lastKeyTime > lastCompileTime)) && !string.IsNullOrEmpty(inputArea.Text))
			{
				timer.Stop();
				isCompiling = true;
				lastCompileTime = DateTime.UtcNow;

				try
				{
					await KeyviewCompilerRunner.RunCompile(
						inputArea.Text,
						KeyviewCompilerRunner.IncludeDirFor(document),
						ch,
						bytes => compiledBytes = bytes,
						code => fullCode = code,
						code => trimmedCode = code,
						trimend,
						trimstr,
						SetStart,
						SetSuccess,
						SetFailure,
						text => codeStatusLabel.Text = text,
						null,
						SetOutputText,
						() => fullCodeCheck.Checked == true,
						() => runScriptButton.Enabled = false,
						() => runScriptButton.Enabled = true,
						AutosaveScratchDocument,
						null,
						null);
				}
				finally
				{
					isCompiling = false;
					timer.Start();
				}
			}

			if (force)
				force = false;
		}

		private void RunStopScript()
		{
			if (scriptProcess != null)
			{
				scriptProcess.Kill();
				scriptProcess = null;
			}

			if (runScriptButton.Text == runScriptText["Stop"])
				return;

			if (compiledBytes == null)
			{
				MessageBox.Show(this, "Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxType.Error);
				return;
			}

			var keysharpExe = GetKeysharpExecutable();

			if (!File.Exists(keysharpExe))
			{
				MessageBox.Show(this, $"Keysharp executable not found:\n{keysharpExe}\n\nCopy Keysharp.app to /Applications/ or run Keyview from the same folder as Keysharp.", "Launch Error", MessageBoxButtons.OK, MessageBoxType.Error);
				return;
			}

			scriptProcess = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = keysharpExe,
					Arguments = "--assembly *",
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};
			scriptProcess.EnableRaisingEvents = true;
			var runtimeOutputStarted = false;
			scriptProcess.ErrorDataReceived += (_, e) =>
			{
				if (string.IsNullOrEmpty(e.Data))
					return;

				Application.Instance.AsyncInvoke(() =>
				{
					if (!runtimeOutputStarted)
					{
						// Replace the generated C# with the script's runtime output so the two don't
						// get mixed together. The C# remains available via "Copy full code". This
						// take-over happens once per run, the first time the script emits output.
						runtimeOutputStarted = true;
						scriptOwnsOutput = true;
						outputArea.Text = "";
						outputArea.Append(ScriptOutputHeader, false);
					}
					else if (!scriptOwnsOutput)
					{
						// A recompile reclaimed the output box after this script took it over; don't
						// clobber the freshly displayed C# with this (now stale) run's later output.
						return;
					}

					outputArea.Append($"{e.Data}\n", true);
				});
			};
			scriptProcess.Exited += (_, _) =>
			{
				Application.Instance.AsyncInvoke(() =>
				{
					runScriptButton.Text = runScriptText["Run"];
					scriptProcess = null;
				});
			};
			_ = scriptProcess.Start();
			scriptProcess.BeginErrorReadLine();

			// Write the raw assembly bytes and close stdin; the child ("--assembly *") reads to EOF.
			try
			{
				using var stdin = scriptProcess.StandardInput.BaseStream;
				stdin.Write(compiledBytes, 0, compiledBytes.Length);
				stdin.Flush();
			}
			catch (IOException)
			{
				// Keysharp exited before reading stdin — error output will appear via stderr above.
				runScriptButton.Text = runScriptText["Run"];
				scriptProcess = null;
				return;
			}

			runScriptButton.Text = runScriptText["Stop"];
		}

		private static string GetKeysharpExecutable()
		{
#if OSX && !DEBUG
			// Prefer the binary installed to /Applications/ (pkg install).
			// Check the binary itself rather than the /usr/local/bin shim, which may be stale.
			const string installed = "/Applications/Keysharp.app/Contents/MacOS/Keysharp";
			if (File.Exists(installed))
				return installed;
#endif
			// Fallback: sibling binary (DMG run, ~/Applications/, or debug build).
			return Path.Combine(AppContext.BaseDirectory ?? Path.GetDirectoryName(Environment.ProcessPath), "Keysharp");
		}

		private void AutosaveScratchDocument()
		{
			if (closing || !document.IsScratch)
				return;

			var dir = Path.GetDirectoryName(lastrun);
			try
			{
				if (!Directory.Exists(dir))
					_ = Directory.CreateDirectory(dir);

				File.WriteAllText(lastrun, inputArea.Text ?? "");
			}
			catch (Exception ex)
			{
				documentStatusLabel.Text = $"Scratch autosave failed: {ex.Message}";
			}
		}
	}
#endif

	internal static class KeyviewCompilerRunner
	{
		internal static IScriptCompiler GetCompiler()
		{
			if (ScriptingComponentRegistry.TryGetCompiler(out var compiler, out var error))
				return compiler;

			throw new InvalidOperationException(error);
		}

		// The base directory for resolving #include directives when compiling the live editor text (which has no
		// script path of its own). Uses the open document's folder so relative, absolute and library (<Name>)
		// includes all resolve as they would when the file is run directly; a never-saved scratch buffer falls
		// back to the current working directory so includes (at least absolute ones) still work.
		internal static string IncludeDirFor(KeyviewDocumentState document)
			=> document.IsScratch ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(document.CurrentFilePath);

		internal static async Task RunCompile(
			string inputText,
			string includeDir,
			IScriptCompiler compiler,
			Action<byte[]> setCompiledBytes,
			Action<string> setFullCode,
			Action<string> setTrimmedCode,
			char[] trimend,
			string trimstr,
			Action setStart,
			Action<double> setSuccess,
			Action setFailure,
			Action<string> setStatus,
			Action refreshStatus,
			Action<string> setOutput,
			Func<bool> useFullCode,
			Action disableRunButton,
			Action enableRunButton,
			Action writeLastRun,
			Action beforeOutput,
			Action afterOutput)
		{
			var startTime = DateTime.UtcNow;
			try
			{
				setCompiledBytes(null);
				disableRunButton?.Invoke();
				setStart?.Invoke();
				setStatus?.Invoke("Compiling script...");
				refreshStatus?.Invoke();
				// Live validation must not restore packages; an explicit run may.
				var result = await Task.Run(() => compiler.Compile(new ScriptCompileRequest
				{
					SourceText = inputText,
					CompilationName = "Keyview",
					RuntimeDirectory = Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath)),
					IncludeDirectory = includeDir,
					Output = ScriptCompilationOutput.InMemory,
					EmitGeneratedCode = true,
					AllowPackageRestore = false,
				})).ConfigureAwait(true);
				var code = result.GeneratedCode ?? result.ErrorText ?? "Compilation failed.";

				// Show the separate inline tree without pretending the two sources form one valid C# unit.
				if (result.InlineCode is { Length: > 0 } inlineCode)
					code += Environment.NewLine + Environment.NewLine
							+ "// ---- #CSharp (compiled as a separate file) ----" + Environment.NewLine + Environment.NewLine
							+ inlineCode;

				if (result.Success)
				{
					setSuccess?.Invoke((DateTime.UtcNow - startTime).TotalSeconds);
					setFullCode(code);

					// Load the compiled assembly and re-enable the Run button as soon as the script is
					// actually runnable, before the (potentially slow) trimming and C# syntax highlighting
					// below. Otherwise the button stays greyed out for a second or two after the compile
					// has really finished, just while the generated code is being highlighted.
					_ = Assembly.Load(result.AssemblyBytes);
					setCompiledBytes(result.AssemblyBytes);
					enableRunButton?.Invoke();

					// Trimming the generated code is pure string work over the whole file; do it on a
					// background thread so the UI thread is not blocked between compile and display.
					var trimmedCode = await Task.Run(() =>
					{
						var token = "[System.STAThread]";
						var start = code.IndexOf(token);
						var display = code.AsSpan(start + token.Length + 2).TrimEnd(trimend).ToString();
						var sb = new StringBuilder(display.Length);

						foreach (var line in display.SplitLines())
							_ = sb.AppendLine(line.TrimNofAnyFromStart(trimstr, 2));

						return sb.ToString().TrimEnd(trimend);
					}).ConfigureAwait(true);

					setTrimmedCode(trimmedCode);
					beforeOutput?.Invoke();
					setOutput?.Invoke(useFullCode() ? code : trimmedCode);
					afterOutput?.Invoke();
					writeLastRun?.Invoke();
				}
				else
				{
					setFailure?.Invoke();
					setOutput?.Invoke(code);
				}
			}
			catch (Exception ex)
			{
				setFailure?.Invoke();
				setOutput?.Invoke(ex.ToString());
			}
			finally
			{
				enableRunButton?.Invoke();
			}
		}
	}

	internal sealed class KeyviewDocumentState
	{
		private string savedText = "";

		internal string CurrentFilePath { get; private set; }
		internal bool IsScratch => string.IsNullOrEmpty(CurrentFilePath);
		internal bool CanCompile => !IsScratch && IsSourceFile(CurrentFilePath);
		internal string DisplayName => IsScratch ? "Scratch document" : Path.GetFileName(CurrentFilePath);

		internal void LoadFile(string path, string text)
		{
			CurrentFilePath = Path.GetFullPath(path);
			savedText = text ?? "";
		}

		internal void LoadScratch()
		{
			CurrentFilePath = null;
			savedText = "";
		}

		internal void MarkSaved(string text) => savedText = text ?? "";

		internal bool IsDirty(string text) => !IsScratch && !string.Equals(savedText, text ?? "", StringComparison.Ordinal);

		internal string GetWindowTitle(string baseTitle, bool dirty)
		{
			if (IsScratch)
				return $"{baseTitle} — Scratchpad (autosaved)";

			return $"{DisplayName}{(dirty ? " *" : "")} — {baseTitle}";
		}

		internal string GetStatusText(bool dirty)
		{
			if (IsScratch)
				return "Scratchpad document — autosaved";

			return $"{CurrentFilePath}{(dirty ? " — Modified" : "")}";
		}

		private static bool IsSourceFile(string path)
		{
			var extension = Path.GetExtension(path);
			return extension.Equals(".ahk", StringComparison.OrdinalIgnoreCase)
				   || extension.Equals(".ks", StringComparison.OrdinalIgnoreCase);
		}
	}

	internal static class KeyviewDocumentCompiler
	{
		internal static bool TryCompile(string sourcePath, IScriptCompiler compiler, out string outputPath, out string error)
		{
			outputPath = Path.ChangeExtension(sourcePath, ".cks");
			error = null;

			try
			{
				var sourceDirectory = Path.GetDirectoryName(sourcePath);
				var nameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
				// Explicit builds may restore packages; live validation opts out above.
				var result = compiler.Compile(new ScriptCompileRequest
				{
					ScriptPath = sourcePath,
					CompilationName = nameNoExt,
					RuntimeDirectory = Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath)),
					IncludeDirectory = sourceDirectory,
					Output = ScriptCompilationOutput.Assembly,
				});

				if (!result.Success)
				{
					error = result.ErrorText;
					return false;
				}

				File.WriteAllBytes(outputPath, result.AssemblyBytes);
				if (compiler.DeploySupportFiles(result, sourceDirectory) is { } deploymentError)
				{
					error = deploymentError;
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				error = ex.ToString();
				return false;
			}
		}
	}
}
