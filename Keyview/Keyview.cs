#if WINDOWS
using ScintillaNET;
#endif

namespace Keyview;

/// <summary>
/// Much of the Scintilla-related code was taken from: https://github.com/robinrodricks/ScintillaNET.Demo
/// </summary>
#if WINDOWS
internal partial class Keyview : Form
{
	[System.Runtime.InteropServices.LibraryImport("uxtheme.dll", EntryPoint = "SetWindowTheme",
		StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
	private static partial int SetWindowTheme(nint window, string subAppName, string subIdList);

	/// <summary>
	/// set this true to show circular buttons for code folding (the [+] and [-] buttons on the margin)
	/// </summary>
	private const bool CODEFOLDING_CIRCULAR = true;

	/// <summary>
	/// change this to whatever margin you want the code folding tree (+/-) to show in
	/// </summary>
	private const int FOLDING_MARGIN = 3;

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
	private readonly IScriptCompiler ch = KeyviewCompilerRunner.GetCompiler();
	private byte[] compiledBytes;
	private readonly CSharpStyler csStyler = new ();
	private readonly KeyviewCompileScheduler compileScheduler = new (TimeSpan.FromSeconds(1));
	private KeyviewCompileResult lastCompile;
	private bool SearchIsOpen = false;
	private string trimmedCode = "";
	private readonly KeyviewScriptRunner scriptRunner = new ();
	private bool scriptOwnsOutput;
	private bool runtimeOutputStarted;
	private const string ScriptOutputHeader = "--- Script output ---\n\n";
	private readonly Button btnRunScript = new ();
	private readonly KeyviewDocumentState document = new ();
	private readonly string baseTitle;
	private string displayedDocumentPath;
	private bool? displayedDirty;
	private bool suppressDocumentChange;
	private bool scratchAutosavePending;
	private bool closing;
	private readonly Dictionary<string, string> btnRunScriptText = new()
	{
		{ "Run", "▶ Run script (F9)" },
		{ "Stop", "⏹ Stop script (F9)" }
	};

	public Keyview(string initialFile = null)
	{
		InitializeComponent();
		InitializeScriptRunner();
		InitializeScintillaTheme(txtIn);
		InitializeScintillaTheme(txtOut);
		lastrun = KeyviewPaths.ScratchDocument;
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

	private static void ApplyScintillaTheme(Scintilla scintilla) =>
		_ = SetWindowTheme(scintilla.Handle, Application.IsDarkModeEnabled ? "DarkMode_Explorer" : null, null);

	private static void InitializeScintillaTheme(Scintilla scintilla)
	{
		// Scintilla owns its native scrollbars, so WinForms cannot theme them with the rest of the control tree.
		scintilla.HandleCreated += (_, _) => ApplyScintillaTheme(scintilla);

		if (scintilla.IsHandleCreated)
			ApplyScintillaTheme(scintilla);
	}

	// The Keysharp/AHK tokenizer for the input box, shared with the Eto editor on Linux/macOS. Built lazily
	// because ForKeysharp() reads the built-in names off Script.TheScript, which is not up yet at field-init.
	private SyntaxHighlighter inputHighlighter;

	private void TxtIn_StyleNeeded(object sender, StyleNeededEventArgs e) =>
		ScintillaSyntaxSink.Restyle((Scintilla)sender, inputHighlighter ??= SyntaxHighlighter.ForKeysharp(), e.Position);

	private void BtnClearSearch_Click(object sender, EventArgs e) => CloseSearch();

	private void BtnNextSearch_Click(object sender, EventArgs e) => SearchManager.Find(true, false);

	private void BtnPrevSearch_Click(object sender, EventArgs e) => SearchManager.Find(false, false);

	private void chkFullCode_CheckStateChanged(object sender, EventArgs e) => SetTxtOut(chkFullCode.Checked ? lastCompile?.FullCode.Value ?? trimmedCode : trimmedCode);

	private void clearSelectionToolStripMenuItem_Click(object sender, EventArgs e) =>
		// Reset selection to the start without changing the caret position more than necessary.
		txtIn.SetEmptySelection(0);

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

	private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e) => txtOut.FoldAll(FoldAction.Contract);

	private void CopyFullCode_Click(object sender, EventArgs e)
	{
		try
		{
			if (lastCompile?.Success == true)
				Clipboard.SetText(lastCompile.FullCode.Value);
			else
				Clipboard.SetText(txtOut.Text);
		}
		catch (Exception ex)
		{
			_ = MessageBox.Show($"Copying code failed: {ex.Message}", "Keyview", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void copyToolStripMenuItem_Click(object sender, EventArgs e) => txtIn.Copy();

	private void cutToolStripMenuItem_Click(object sender, EventArgs e) => txtIn.Cut();

	private void expandAllToolStripMenuItem_Click(object sender, EventArgs e) => txtOut.FoldAll(FoldAction.Expand);

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

	private void Indent() =>
		//We use this hack to send "Shift+Tab" to scintilla, since there is no known API to indent,
		//although the indentation function exists. Pressing TAB with the editor focused confirms this.
		GenerateKeystrokes("{TAB}");

	private void indentGuidesToolStripMenuItem_Click(object sender, EventArgs e)
	{
		indentGuidesItem.Checked = !indentGuidesItem.Checked;
		txtIn.IndentationGuides = indentGuidesItem.Checked ? IndentView.LookBoth : IndentView.None;
	}

	private void indentSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Indent();

	private void InitCodeFolding(Scintilla txt)
	{
		var marginBackground = SyntaxPalette.ToColor(SyntaxPalette.MarginBackground);
		var marginForeground = SyntaxPalette.ToColor(SyntaxPalette.MarginForeground);
		txt.SetFoldMarginColor(true, marginBackground);
		txt.SetFoldMarginHighlightColor(true, marginBackground);
		//Enable code folding.
		txt.SetProperty("fold", "1");
		txt.SetProperty("fold.compact", "1");
		// Configure a margin to display folding symbols
		txt.Margins[FOLDING_MARGIN].Type = MarginType.Symbol;
		txt.Margins[FOLDING_MARGIN].Mask = Marker.MaskFolders;
		txt.Margins[FOLDING_MARGIN].Sensitive = true;
		txt.Margins[FOLDING_MARGIN].Width = 20;

		//Set colors for all folding markers.
		for (var i = 25; i <= 31; i++)
		{
			txt.Markers[i].SetForeColor(marginBackground);
			txt.Markers[i].SetBackColor(marginForeground);
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

	private void InitColors(Scintilla txt)
	{
		txt.CaretForeColor = SyntaxPalette.ToColor(SyntaxPalette.Caret);
		txt.CaretLineBackColor = SyntaxPalette.ToColor(SyntaxPalette.CaretLine);
		txt.SelectionBackColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionBackground);
		txt.SelectionTextColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionForeground);
		txt.AutocompleteListBackColor = SyntaxPalette.ToColor(SyntaxPalette.EditorBackground);
		txt.AutocompleteListTextColor = SyntaxPalette.ToColor(SyntaxPalette.EditorForeground);
		txt.AutocompleteListSelectedBackColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionBackground);
		txt.AutocompleteListSelectedTextColor = SyntaxPalette.ToColor(SyntaxPalette.SelectionForeground);
	}

	private void InitNumberMargin(Scintilla txt)
	{
		txt.Styles[Style.LineNumber].BackColor = SyntaxPalette.ToColor(SyntaxPalette.MarginBackground);
		txt.Styles[Style.LineNumber].ForeColor = SyntaxPalette.ToColor(SyntaxPalette.MarginForeground);
		txt.Styles[Style.IndentGuide].ForeColor = SyntaxPalette.ToColor(SyntaxPalette.MarginForeground);
		txt.Styles[Style.IndentGuide].BackColor = SyntaxPalette.ToColor(SyntaxPalette.MarginBackground);
		var nums = txt.Margins[NUMBER_MARGIN];
		nums.Width = 30;
		nums.Type = MarginType.Number;
		nums.Sensitive = true;
		nums.Mask = 0;

		UpdateNumberMarginWidth(txt);
	}

	private void UpdateNumberMarginWidth(Scintilla txt)
	{
		// how many lines do we have?
		var maxLine = Math.Max(1, txt.Lines.Count);      // avoid 0
		var digits = (int)Math.Log10(maxLine) + 1;       // 1 for 1..9, 2 for 10..99, etc.

		// width in pixels needed to render that many '9' with the line-number style
		var px = txt.TextWidth(Style.LineNumber, new string('9', digits));

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
		txt.Styles[Style.Default].BackColor = SyntaxPalette.ToColor(SyntaxPalette.EditorBackground);
		txt.Styles[Style.Default].ForeColor = SyntaxPalette.ToColor(SyntaxPalette.EditorForeground);
		txt.StyleClearAll();
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
		HotKeyManager.AddHotKey(this, Uppercase, Keys.U, true);
		HotKeyManager.AddHotKey(this, Lowercase, Keys.L, true);
		HotKeyManager.AddHotKey(this, ZoomIn, Keys.Oemplus, true);
		HotKeyManager.AddHotKey(this, ZoomOut, Keys.OemMinus, true);
		HotKeyManager.AddHotKey(this, ZoomDefault, Keys.D0, true);
		HotKeyManager.AddHotKey(this, CloseSearch, Keys.Escape);
		HotKeyManager.AddHotKey(this, RunStopScript, Keys.F9);
		//Remove conflicting hotkeys from scintilla.
		txtIn.ClearCmdKey(Keys.Control | Keys.F);
		txtIn.ClearCmdKey(Keys.Control | Keys.L);
		txtIn.ClearCmdKey(Keys.Control | Keys.U);
	}

	private void InvokeIfNeeded(Action action)
	{
		if (closing || IsDisposed) return;
		if (InvokeRequired)
		{
			try { _ = BeginInvoke(action); }
			catch (InvalidOperationException) when (closing || IsDisposed) { }
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
		// Save while the editor text is still valid, then block any later (post-close) autosave —
		// e.g. an idle autosave callback — from overwriting with an empty string.
		AutosaveScratchDocument();
		closing = true;
		scriptRunner.Dispose();
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
		// The input box is container-styled from the SAME tokenizer the Eto editor uses: Scintilla has no
		// AutoHotkey lexer, and the C++ one it used to borrow could not switch to C# inside a #CSharp block.
		InitInputStyle(txtIn);
		ScintillaSyntaxSink.Attach(txtIn);
		txtIn.StyleNeeded += TxtIn_StyleNeeded;
		txtIn.Insert += (_, _) => ScintillaSyntaxSink.Invalidate(txtIn);
		txtIn.Delete += (_, _) => ScintillaSyntaxSink.Invalidate(txtIn);
		txtIn.SavePointLeft += (_, _) => UpdateDocumentUi();
		txtIn.SavePointReached += (_, _) => UpdateDocumentUi();
		txtOut.StyleResetDefault();
		txtOut.Styles[Style.Default].Font = "Consolas";
		txtOut.Styles[Style.Default].Size = 10;
		txtOut.Styles[Style.Default].BackColor = SyntaxPalette.ToColor(SyntaxPalette.EditorBackground);
		txtOut.Styles[Style.Default].ForeColor = SyntaxPalette.ToColor(SyntaxPalette.EditorForeground);
		txtOut.StyleClearAll();
		csStyler.ApplyStyle(txtOut);//C# syntax for txtOut.
		csStyler.SetKeywords(txtOut);
		InitColors(txtIn);
		InitColors(txtOut);
		InitNumberMargin(txtIn);
		InitNumberMargin(txtOut);
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
			txtIn.SetSavePoint();
		}
		finally
		{
			suppressDocumentChange = false;
		}

		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
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
			txtIn.SetSavePoint();
		}
		finally
		{
			suppressDocumentChange = false;
		}

		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
		UpdateDocumentUi();
	}

	private void Lowercase()
	{
		var start = txtIn.SelectionStart;
		var end = txtIn.SelectionEnd;
		txtIn.ReplaceSelection(txtIn.GetTextRange(start, end - start).ToLower());
		txtIn.SetSelection(start, end);
	}

	private void lowercaseSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Lowercase();

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
		if (document.IsScratch || !txtIn.Modified)
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
			txtIn.SetSavePoint();
			UpdateDocumentUi();
			return true;
		}
		catch (Exception ex)
		{
			_ = MessageBox.Show($"Unable to save file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			return false;
		}
	}

	private async void CompileDocument()
	{
		if (compileScheduler.IsCompiling || !document.CanCompile || (!document.IsScratch && txtIn.Modified && !SaveDocument())) return;
		if (!compileScheduler.TryBeginExplicit()) return;
		UpdateDocumentUi();
		tslCodeStatus.Text = "Writing .cks...";
		var sourcePath = document.CurrentFilePath;
		var version = compileScheduler.EditVersion;
		try
		{
			var result = await Task.Run(() =>
			{
				var success = KeyviewDocumentCompiler.TryCompile(sourcePath, ch, out var path, out var error);
				return (success, path, error);
			});
			if (closing || !compileScheduler.IsCurrent(version, sourcePath, document.CurrentFilePath)) return;
			if (result.success)
			{
				tslCodeStatus.ForeColor = SyntaxPalette.ToColor(SyntaxPalette.StatusSuccess);
				tslCodeStatus.Text = $"Wrote {result.path}";
			}
			else
			{
				compiledBytes = null;
				trimmedCode = result.error;
				lastCompile = new(null, result.error, new Lazy<string>(() => result.error), result.error, TimeSpan.Zero);
				btnRunScript.Enabled = scriptRunner.IsRunning;
				tslCodeStatus.ForeColor = SyntaxPalette.ToColor(SyntaxPalette.StatusError);
				tslCodeStatus.Text = "Compile failed";
				SetTxtOut(result.error);
			}
		}
		finally
		{
			compileScheduler.CompleteExplicit();
			if (!closing) UpdateDocumentUi();
		}
	}

	private void UpdateDocumentUi()
	{
		var dirty = !document.IsScratch && txtIn.Modified;
		if (displayedDirty != dirty || displayedDocumentPath != document.CurrentFilePath)
		{
			Text = document.GetWindowTitle(baseTitle, dirty);
			documentStatusLabel.Text = document.GetStatusText(dirty);
			displayedDirty = dirty;
			displayedDocumentPath = document.CurrentFilePath;
		}

		saveToolStripMenuItem.Enabled = !document.IsScratch && dirty;
		compileToolStripMenuItem.Enabled = document.CanCompile && !compileScheduler.IsCompiling;
		btnCompileScript.Visible = !document.IsScratch;
		btnCompileScript.Enabled = document.CanCompile && !compileScheduler.IsCompiling;
	}

	private void Outdent() =>
		// we use this hack to send "Shift+Tab" to scintilla, since there is no known API to outdent,
		// although the indentation function exists. Pressing Shift+Tab with the editor focused confirms this.
		GenerateKeystrokes("+{TAB}");

	private void outdentSelectionToolStripMenuItem_Click(object sender, EventArgs e) => Outdent();

	private void pasteToolStripMenuItem_Click(object sender, EventArgs e) => txtIn.Paste();

	private void selectAllToolStripMenuItem_Click(object sender, EventArgs e) => txtIn.SelectAll();

	private void saveToolStripMenuItem_Click(object sender, EventArgs e) => SaveDocument();

	private void compileToolStripMenuItem_Click(object sender, EventArgs e) => CompileDocument();

	private void selectLineToolStripMenuItem_Click(object sender, EventArgs e)
	{
		if (string.IsNullOrEmpty(txtIn.Text))
			return;
		var line = txtIn.Lines[txtIn.CurrentLine];
		txtIn.SetSelection(line.Position + line.Length, line.Position);
	}

	private void SetFailure()
	{
		tslCodeStatus.ForeColor = SyntaxPalette.ToColor(SyntaxPalette.StatusError);
		tslCodeStatus.Text = "Error";
	}

	private void SetStart()
	{
		lastCompile = null;
		trimmedCode = "";
		tslCodeStatus.ForeColor = SyntaxPalette.ToColor(SyntaxPalette.EditorForeground);
		tslCodeStatus.Text = "";
		//Don't clear txtOut, it causes flicker.
	}

	private void SetSuccess(double seconds)
	{
		tslCodeStatus.ForeColor = SyntaxPalette.ToColor(SyntaxPalette.StatusSuccess);
		tslCodeStatus.Text = $"Ok ({seconds:F1}s)";
	}

	private void SetTxtOut(string txt)
	{
		scriptOwnsOutput = false;
		if (txtOut.Text == txt) return;
		txtOut.ReadOnly = false;
		txtOut.Text = txt;
		txtOut.ReadOnly = true;
		UpdateNumberMarginWidth(txtOut);
	}

	private void splitContainer_DoubleClick(object sender, EventArgs e) => splitContainer.SplitterDistance = Width / 2;

	private async void Timer_Tick(object sender, EventArgs e)
	{
		if (scratchAutosavePending && compileScheduler.IsIdle(DateTime.UtcNow))
		{
			scratchAutosavePending = false;
			AutosaveScratchDocument();
		}

		if (closing || !compileScheduler.TryBegin(DateTime.UtcNow, txtIn.TextLength > 0, out var version)) return;
		compiledBytes = null;
		btnRunScript.Enabled = scriptRunner.IsRunning;
		SetStart();
		tslCodeStatus.Text = "Compiling script...";
		UpdateDocumentUi();
		var oldIndex = txtOut.FirstVisibleLine;
		var sourcePath = document.CurrentFilePath;
		try
		{
			var result = await KeyviewCompilerRunner.RunCompile(txtIn.Text, KeyviewCompilerRunner.IncludeDirFor(document), ch);
			if (closing || !compileScheduler.IsCurrent(version, sourcePath, document.CurrentFilePath)) return;
			lastCompile = result;
			compiledBytes = result.AssemblyBytes;
			trimmedCode = result.TrimmedCode;
			if (result.Success) SetSuccess(result.Elapsed.TotalSeconds); else SetFailure();
			btnRunScript.Enabled = result.Success || scriptRunner.IsRunning;
			SetTxtOut(chkFullCode.Checked ? result.FullCode.Value : result.TrimmedCode);
			txtOut.FirstVisibleLine = oldIndex;
		}
		finally
		{
			compileScheduler.Complete(version);
			if (!closing) UpdateDocumentUi();
		}
	}

	private void RunScript_Click(object sender, EventArgs e) => RunStopScript();

	private void InitializeScriptRunner()
	{
		scriptRunner.RunningChanged += (id, running) => InvokeIfNeeded(() =>
		{
			if (closing || !scriptRunner.IsCurrent(id)) return;
			btnRunScript.Text = btnRunScriptText[running ? "Stop" : "Run"];
			btnRunScript.Enabled = running || compiledBytes != null;
		});
		scriptRunner.OutputReceived += (id, text) => InvokeIfNeeded(() =>
		{
			if (closing || !scriptRunner.IsCurrent(id) || !scriptOwnsOutput) return;
			if (!runtimeOutputStarted)
			{
				runtimeOutputStarted = true;
				SetTxtOut(ScriptOutputHeader);
				scriptOwnsOutput = true;
			}

			txtOut.ReadOnly = false;
			txtOut.AppendText(text);
			txtOut.ReadOnly = true;
		});
	}

	private void RunStopScript()
	{
		try
		{
			if (scriptRunner.IsRunning) { scriptRunner.Stop(); return; }

			if (compiledBytes == null) { _ = MessageBox.Show(lastCompile?.Error ?? "Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

			runtimeOutputStarted = false;
			scriptOwnsOutput = true;
			scriptRunner.Start(GetKeysharpExecutable(), compiledBytes);
		}
		catch (Exception ex) { scriptOwnsOutput = false; _ = MessageBox.Show(ex.Message, "Process Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
	}

	private static string GetKeysharpExecutable() => Path.Combine(AppContext.BaseDirectory, "Keysharp.exe");

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

	private void TxtIn_DragEnter(object sender, DragEventArgs e) =>
		e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
			? DragDropEffects.Copy : DragDropEffects.None;

	private void txtIn_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.F5)
			compileScheduler.RequestCompile();
	}

	private void txtIn_TextChanged(object sender, EventArgs e)
	{
		UpdateNumberMarginWidth(txtIn);
		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
		btnRunScript.Enabled = scriptRunner.IsRunning;

		if (!suppressDocumentChange)
		{
			UpdateDocumentUi();
			scratchAutosavePending = true;
		}
	}

	private void txtOut_TextChanged(object sender, EventArgs e) => UpdateNumberMarginWidth(txtOut);

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
	private enum StatusTone
	{
		Default,
		Success,
		Error
	}

	private readonly KeyviewEditHistory editHistory = new ();
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
	private readonly double updateFreqSeconds = 1;
	private readonly string lastrun;
	private readonly KeyviewDocumentState document = new ();
	private readonly Dictionary<string, string> runScriptText = new ()
	{
		{ "Run", "▶ Run script (F9)" },
		{ "Stop", "◾️ Stop script (F9)" }
	};

	private readonly KeyviewCompileScheduler compileScheduler = new (TimeSpan.FromSeconds(1));
	private KeyviewCompileResult lastCompile;
	private bool searchIsOpen;
	private string trimmedCode = "";
	private readonly KeyviewScriptRunner scriptRunner = new ();
	private bool runtimeOutputStarted;
	// Shown in the output box (replacing the generated C#) the first time a running script emits output.
	// The C# stays available via "Copy full code" through the last compile result.
	private const string ScriptOutputHeader = "─── Script output ───\n\n";
	private string lastSearch = "";
	private bool suppressUndo;
	private string lastText = "";
	private int lastSelectionStart;
	private int lastSelectionLength;
	private string baseTitle;
	private string displayedDocumentPath;
	private bool? displayedDirty;
	private bool suppressDocumentChange;
	private bool highlighting;
	private bool outputHighlighting;
	private long outputVersion;
	private string pendingOutput;
	private bool themeRefreshPending;
	private StatusTone codeStatusTone;
	// True while the output box is displaying a running script's output rather than generated C#.
	// Cleared whenever the compiler writes C# back into the box, so a still-running (now stale)
	// script can't clobber the freshly displayed code with its continued output.
	private bool scriptOwnsOutput;

	private bool closing;

	public Keyview(string initialFile = null)
	{
		lastrun = KeyviewPaths.ScratchDocument;
		Title = $"Keyview {Assembly.GetExecutingAssembly().GetName().Version}";
		baseTitle = Title;
		InitializeWindowIcon();
		InitializeScriptRunner();
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
		Application.Instance.ThemeChanged += Application_ThemeChanged;

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
			Application.Instance.ThemeChanged -= Application_ThemeChanged;
			timer.Stop();
			highlightTimer.Stop();
			scriptRunner.Dispose();
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
		cutItem.Click += (_, _) => CutSelection();
		var copyItem = new ButtonMenuItem { Text = "&Copy" };
		copyItem.Click += (_, _) => CopySelection();
		var pasteItem = new ButtonMenuItem { Text = "&Paste" };
		pasteItem.Click += (_, _) => PasteFromClipboard();
		var selectLineItem = new ButtonMenuItem { Text = "Select &Line" };
		selectLineItem.Click += (_, _) => SelectLine();
		var selectAllItem = new ButtonMenuItem { Text = "Select &All" };
		selectAllItem.Click += (_, _) => inputArea.SelectAll();
		var clearSelectionItem = new ButtonMenuItem { Text = "Clea&r Selection" };
		clearSelectionItem.Click += (_, _) =>
		{
			var (start, _) = GetSelection();
			SetSelection(start, 0);
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
		if (inputArea.ControlObject is MonoMac.AppKit.NSTextView textView)
			textView.RichText = false;
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
		ApplyEditorTheme(false);
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

	private void Application_ThemeChanged(object sender, EventArgs e)
	{
		themeRefreshPending = true;
		Application.Instance.AsyncInvoke(ApplyPendingThemeRefresh);
	}

	private void ApplyPendingThemeRefresh()
	{
		if (!themeRefreshPending || highlighting || outputHighlighting || closing)
			return;

		themeRefreshPending = false;
		ApplyEditorTheme(true);
	}

	private void SetInputText(string text)
	{
		EtoHighlightExtensions.Invalidate(inputArea);
		inputArea.Text = text;
		RequestInputHighlight();
	}

	private void ApplyEditorTheme(bool recolor)
	{
		var background = SyntaxPalette.ToColor(SyntaxPalette.EditorBackground);
		var foreground = SyntaxPalette.ToColor(SyntaxPalette.EditorForeground);
		// Leave the form background theme-owned because GTK draws its client-side shadow on that surface.
		inputArea.BackgroundColor = background;
		inputArea.TextColor = foreground;
		outputArea.BackgroundColor = background;
		outputArea.TextColor = foreground;

		ApplyCodeStatusColor();

		if (recolor)
		{
			EtoHighlightExtensions.Invalidate(inputArea);
			EtoHighlightExtensions.Invalidate(outputArea);
			HighlightInput();
			RecolorOutputForTheme();
		}
	}

	private void ApplyCodeStatusColor() => codeStatusLabel.TextColor = SyntaxPalette.ToColor(codeStatusTone switch
	{
		StatusTone.Success => SyntaxPalette.StatusSuccess,
		StatusTone.Error => SyntaxPalette.StatusError,
		_ => SyntaxPalette.EditorForeground
	});

	private void SetCodeStatusTone(StatusTone tone)
	{
		codeStatusTone = tone;
		ApplyCodeStatusColor();
	}

	private void RecolorOutputForTheme()
	{
		if (!scriptOwnsOutput)
		{
			HighlightOutput();
			return;
		}

		var length = outputArea.TextLength;

		if (length > 0)
			outputArea.Buffer.SetForeground(new Range<int>(0, length - 1), outputArea.TextColor);
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
		nextSearchButton.Click += (_, _) => Find(true);
		prevSearchButton.Click += (_, _) => Find(false);
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
		if (EtoHighlightExtensions.IsApplyingStyle(inputArea))
			return;

		// Read the buffer once: inputArea.Text rebuilds the whole string, so calling it per consumer
		// (undo, dirty check, autosave) on every keystroke is a major cost on large scripts.
		var text = inputArea.Text ?? "";
		if (string.Equals(text, lastText, StringComparison.Ordinal)) return;
		RecordUndoSnapshot(text);
		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
		runScriptButton.Enabled = scriptRunner.IsRunning;

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
		if (closing) return;
		if (highlighting) { RequestInputHighlight(); return; }

		highlighting = true;
		try
		{
			// Yield to the UI loop periodically so re-highlighting a large script doesn't block typing.
			if (!inputHighlighter.Highlight(inputArea, Application.Instance.RunIteration, () => closing ? -1 : compileScheduler.EditVersion)
				&& !closing) RequestInputHighlight();
		}
		finally
		{
			highlighting = false;
			ApplyPendingThemeRefresh();
		}
	}

	private void InputArea_KeyDown(object sender, KeyEventArgs e)
	{
		var inputIsTarget = ReferenceEquals(sender, inputArea) || inputArea.HasFocus;
		var shortcutModifier = Application.Instance.CommonModifier;

		if (inputIsTarget)
			UpdateSelectionSnapshot();

		if (e.Key == Keys.F5)
		{
			compileScheduler.RequestCompile();
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
			if (e.Key == Keys.Z && e.Modifiers == shortcutModifier)
			{
				Undo();
				e.Handled = true;
				return;
			}

			if (e.Key == Keys.Y && e.Modifiers == shortcutModifier)
			{
				Redo();
				e.Handled = true;
				return;
			}

			if (e.Key == Keys.Z && e.Modifiers == (shortcutModifier | Keys.Shift))
			{
				Redo();
				e.Handled = true;
				return;
			}
		}

		if (e.Modifiers == shortcutModifier)
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
			Find(true);
			e.Handled = true;
			return;
		}

		if (e.Key == Keys.Enter && (e.Modifiers == Keys.Shift || e.Modifiers == Keys.Control))
		{
			Find(false);
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
		ApplyInputEdit(text.Remove(selection.Start, selection.Length()), selection.Start, 0);
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
		ApplyInputEdit(before + clip + after, start + clip.Length, 0);
	}

	private (int start, int length) GetSelection()
	{
		var selection = inputArea.Selection;
		return (Math.Max(0, selection.Start), Math.Max(0, selection.Length()));
	}

	private void SetSelection(int start, int length)
	{
		var safeStart = Math.Max(0, start);
		var safeLength = Math.Max(0, length);
		inputArea.Selection = new Range<int>(safeStart, safeStart + safeLength - 1);
		UpdateSelectionSnapshot();
	}

	private void UpdateSelectionSnapshot()
	{
		var (start, length) = GetSelection();
		lastSelectionStart = start;
		lastSelectionLength = length;
	}

	private void ApplyEdit((string Text, EditorSelection Selection) change)
	{
		suppressUndo = true;
		try
		{
			SetInputText(change.Text);
			SetSelection(change.Selection.Start, change.Selection.Length);
			lastText = change.Text;
			UpdateSelectionSnapshot();
		}
		finally { suppressUndo = false; }
		UpdateUndoRedoState();
	}

	private void ApplyInputEdit(string text, int start, int length)
	{
		var (beforeStart, beforeLength) = GetSelection();
		editHistory.RecordAppliedEdit(lastText, text, new EditorSelection(beforeStart, beforeLength), () =>
		{
			SetInputText(text);
			SetSelection(start, length);
			var (afterStart, afterLength) = GetSelection();
			return new EditorSelection(afterStart, afterLength);
		}, DateTime.UtcNow);
		lastText = text;
		UpdateSelectionSnapshot();
		UpdateUndoRedoState();
	}

	private void RecordUndoSnapshot(string currentText)
	{
		if (suppressUndo || editHistory.IsApplying || currentText == lastText) return;
		var (start, length) = GetSelection();
		editHistory.Record(lastText, currentText, new EditorSelection(lastSelectionStart, lastSelectionLength),
			new EditorSelection(start, length), DateTime.UtcNow);
		lastText = currentText;
		UpdateSelectionSnapshot();
		UpdateUndoRedoState();
	}

	private void ResetUndoHistory()
	{
		editHistory.Clear();
		lastText = inputArea.Text ?? "";
		UpdateSelectionSnapshot();
		UpdateUndoRedoState();
	}

	private void UpdateUndoRedoState()
	{
		undoMenuItem.Enabled = editHistory.CanUndo;
		redoMenuItem.Enabled = editHistory.CanRedo;
	}

	private void Undo()
	{
		if (editHistory.CanUndo) ApplyEdit(editHistory.Undo(lastText));
	}

	private void Redo()
	{
		if (editHistory.CanRedo) ApplyEdit(editHistory.Redo(lastText));
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
	}

	private void Find(bool next)
	{
		var needle = searchBox.Text ?? "";
		lastSearch = needle;

		if (string.IsNullOrEmpty(needle))
			return;

		var text = inputArea.Text ?? "";
		var (selectionStart, selectionLength) = GetSelection();
		var searchStart = selectionStart + selectionLength;

		int index;

		if (next)
		{
			index = text.IndexOf(needle, searchStart, StringComparison.Ordinal);
			if (index == -1 && searchStart > 0)
				index = text.IndexOf(needle, 0, StringComparison.Ordinal);
		}
		else
		{
			index = text.AsSpan(0, selectionStart).LastIndexOf(needle.AsSpan(), StringComparison.Ordinal);
			if (index == -1)
				index = text.LastIndexOf(needle, StringComparison.Ordinal);
		}

		if (index == -1)
		{
			SetSelection(0, 0);
			return;
		}

		SetSelection(index, needle.Length);
		inputArea.ScrollTo(new Range<int>(index, index + needle.Length - 1));
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
		ApplyInputEdit(before + newBlock + after, startLine, newBlock.Length);
	}

	private void TransformSelection(Func<string, string> transform)
	{
		var text = inputArea.Text ?? "";
		var (start, length) = GetSelection();

		if (length <= 0)
			return;

		var selected = text.Substring(start, length);
		var transformed = transform(selected);
		ApplyInputEdit(text.Substring(0, start) + transformed + text.Substring(start + length), start, transformed.Length);
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
		var text = lastCompile?.Success == true ? lastCompile.FullCode.Value : outputArea.Text;
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
			SetInputText(text);
			document.LoadFile(fullPath, text);
			ResetUndoHistory();
		}
		finally
		{
			suppressDocumentChange = false;
		}

		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
		UpdateDocumentUi();
	}

	private void LoadScratchDocument()
	{
		suppressDocumentChange = true;
		try
		{
			SetInputText(File.Exists(lastrun) ? File.ReadAllText(lastrun) : "");
			document.LoadScratch();
			ResetUndoHistory();
		}
		finally
		{
			suppressDocumentChange = false;
		}

		compileScheduler.TextChanged(DateTime.UtcNow);
		compiledBytes = null;
		UpdateDocumentUi();
	}

	private bool ConfirmDiscardChanges()
	{
		if (!document.IsDirty(lastText))
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
			File.WriteAllText(document.CurrentFilePath, lastText);
			document.MarkSaved(lastText);
			UpdateDocumentUi();
			return true;
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, $"Unable to save file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxType.Error);
			return false;
		}
	}

	private async void CompileDocument()
	{
		if (compileScheduler.IsCompiling || !document.CanCompile || (document.IsDirty(lastText) && !SaveDocument())) return;
		if (!compileScheduler.TryBeginExplicit()) return;
		UpdateDocumentUi();
		codeStatusLabel.Text = "Writing .cks...";
		var sourcePath = document.CurrentFilePath;
		var version = compileScheduler.EditVersion;
		try
		{
			var result = await Task.Run(() =>
			{
				var success = KeyviewDocumentCompiler.TryCompile(sourcePath, ch, out var path, out var error);
				return (success, path, error);
			});
			if (closing || !compileScheduler.IsCurrent(version, sourcePath, document.CurrentFilePath)) return;
			if (result.success)
			{
				SetCodeStatusTone(StatusTone.Success);
				codeStatusLabel.Text = $"Wrote {result.path}";
			}
			else
			{
				compiledBytes = null;
				trimmedCode = result.error;
				lastCompile = new(null, result.error, new Lazy<string>(() => result.error), result.error, TimeSpan.Zero);
				runScriptButton.Enabled = scriptRunner.IsRunning;
				SetCodeStatusTone(StatusTone.Error);
				codeStatusLabel.Text = "Compile failed";
				SetOutputText(result.error);
			}
		}
		finally
		{
			compileScheduler.CompleteExplicit();
			if (!closing) UpdateDocumentUi();
		}
	}

	private void UpdateDocumentUi(string text = null)
	{
		text ??= lastText;
		var dirty = document.IsDirty(text);
		if (displayedDirty != dirty || displayedDocumentPath != document.CurrentFilePath)
		{
			Title = document.GetWindowTitle(baseTitle, dirty);
			documentStatusLabel.Text = document.GetStatusText(dirty);
			displayedDirty = dirty;
			displayedDocumentPath = document.CurrentFilePath;
		}
		saveMenuItem.Enabled = !document.IsScratch && dirty;
		compileMenuItem.Enabled = document.CanCompile && !compileScheduler.IsCompiling;
		compileScriptButton.Visible = !document.IsScratch;
		compileScriptButton.Enabled = document.CanCompile && !compileScheduler.IsCompiling;
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
		lastCompile = null;
		trimmedCode = "";
		SetCodeStatusTone(StatusTone.Default);
		codeStatusLabel.Text = "";
	}

	private void SetSuccess(double seconds)
	{
		SetCodeStatusTone(StatusTone.Success);
		codeStatusLabel.Text = $"Ok ({seconds:F1}s)";
	}

	private void SetFailure()
	{
		SetCodeStatusTone(StatusTone.Error);
		codeStatusLabel.Text = "Error";
	}


	private void SetOutputText(string text)
	{
		scriptOwnsOutput = false;
		if (outputHighlighting) { pendingOutput = text; outputVersion++; return; }
		if (string.Equals(outputArea.Text, text, StringComparison.Ordinal)) { HighlightOutput(); return; }
		outputVersion++;
		EtoHighlightExtensions.Invalidate(outputArea);
		outputArea.Text = text;
		HighlightOutput();
	}

	private void HighlightOutput()
	{
		if (outputHighlighting || closing)
			return;

		outputHighlighting = true;
		try
		{
			// Yield to the UI loop periodically so highlighting a large generated file doesn't freeze.
			outputHighlighter.Highlight(outputArea, Application.Instance.RunIteration, () => closing ? -1 : outputVersion);
		}
		finally
		{
			outputHighlighting = false;
			if (closing) pendingOutput = null;
			else if (pendingOutput is { } next) { pendingOutput = null; SetOutputText(next); }
			ApplyPendingThemeRefresh();
		}
	}

	private void UpdateOutputFromCache()
	{
		var desired = fullCodeCheck.Checked == true ? lastCompile?.FullCode.Value ?? trimmedCode : trimmedCode;
		if (string.IsNullOrEmpty(desired))
			return;
		SetOutputText(desired);
	}

	private async void Timer_Elapsed(object sender, EventArgs e)
	{
		if (closing || !compileScheduler.TryBegin(DateTime.UtcNow, inputArea.TextLength > 0, out var version)) return;
		compiledBytes = null;
		runScriptButton.Enabled = scriptRunner.IsRunning;
		SetStart();
		codeStatusLabel.Text = "Compiling script...";
		UpdateDocumentUi();
		var sourcePath = document.CurrentFilePath;
		try
		{
			var result = await KeyviewCompilerRunner.RunCompile(inputArea.Text, KeyviewCompilerRunner.IncludeDirFor(document), ch);
			if (closing || !compileScheduler.IsCurrent(version, sourcePath, document.CurrentFilePath)) return;
			lastCompile = result;
			compiledBytes = result.AssemblyBytes;
			trimmedCode = result.TrimmedCode;
			if (result.Success) SetSuccess(result.Elapsed.TotalSeconds); else SetFailure();
			runScriptButton.Enabled = result.Success || scriptRunner.IsRunning;
			SetOutputText(fullCodeCheck.Checked == true ? result.FullCode.Value : result.TrimmedCode);
		}
		finally
		{
			compileScheduler.Complete(version);
			if (!closing) UpdateDocumentUi();
		}
	}

	private void DispatchScriptUpdate(Action action)
	{
		if (!closing) Application.Instance.AsyncInvoke(action);
	}

	private void InitializeScriptRunner()
	{
		scriptRunner.RunningChanged += (id, running) => DispatchScriptUpdate(() =>
		{
			if (closing || !scriptRunner.IsCurrent(id)) return;
			runScriptButton.Text = runScriptText[running ? "Stop" : "Run"];
			runScriptButton.Enabled = running || compiledBytes != null;
		});
		scriptRunner.OutputReceived += (id, text) => DispatchScriptUpdate(() =>
		{
			if (closing || !scriptRunner.IsCurrent(id) || !scriptOwnsOutput) return;
			outputVersion++;
			if (!runtimeOutputStarted)
			{
				runtimeOutputStarted = true;
				EtoHighlightExtensions.Invalidate(outputArea);
				outputArea.Text = "";
				outputArea.Append(ScriptOutputHeader, false);
			}
			outputArea.Append(text, true);
		});
	}

	private void RunStopScript()
	{
		try
		{
			if (scriptRunner.IsRunning) { scriptRunner.Stop(); return; }
			if (compiledBytes == null) { MessageBox.Show(this, lastCompile?.Error ?? "Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxType.Error); return; }
			runtimeOutputStarted = false;
			scriptOwnsOutput = true;
			pendingOutput = null;
			outputVersion++;
			scriptRunner.Start(GetKeysharpExecutable(), compiledBytes);
		}
		catch (Exception ex) { scriptOwnsOutput = false; MessageBox.Show(this, ex.Message, "Process Error", MessageBoxButtons.OK, MessageBoxType.Error); }
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

/// <summary>
/// Where Keysharp keeps per-user data, and the files Keyview puts there. One definition for both the
/// WinForms and Eto windows, and the same folder the bundled demos write demos.ini to - Keysharp has a
/// single data folder rather than one per component.
/// </summary>
internal static class KeyviewPaths
{
	internal static string DataDir => Path.Combine(Accessors.A_AppData, "Keysharp");

	/// <summary>The scratch buffer autosave: the never-saved document Keyview reopens on next launch.</summary>
	internal static string ScratchDocument => Path.Combine(DataDir, "lastkeyviewrun.txt");
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
