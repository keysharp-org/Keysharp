using Antlr4.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

		private static readonly string keywords1 = "true false this thishotkey super unset isset " + Keywords.GetKeywords();
		private readonly string keywords2;
		private readonly Button btnCopyFullCode = new ();
		private readonly CheckBox chkFullCode = new ();
		private readonly string lastrun;
		private readonly UITimer timer = new ();
		private readonly char[] trimend = ['\n', '\r'];
		private readonly double updateFreqSeconds = 1;
		private readonly CompilerHelper ch = new ();
		private readonly CSharpStyler csStyler = new ();
		private bool force = false;
		private string fullCode = "";
		private DateTime lastCompileTime = DateTime.UtcNow;
		private DateTime lastKeyTime = DateTime.UtcNow;
		private bool SearchIsOpen = false;
		private string trimmedCode = "";
		private readonly string trimstr = "{}\t";
		private Process scriptProcess = null;
		private readonly Button btnRunScript = new ();
		private readonly Dictionary<string, string> btnRunScriptText = new Dictionary<string, string>()
		{
			{ "Run", "▶ Run script (F9)" },
			{ "Stop", "⏹ Stop script (F9)" }
		};

		public Keyview(string initialFile = null)
		{
			InitializeComponent();
			keywords2 = Script.TheScript.GetPublicStaticPropertyNames();
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
			btnRunScript.Text = btnRunScriptText["Run"];
			btnRunScript.Margin = new Padding(15);
			host = new ToolStripControlHost(btnRunScript)
			{
				Alignment = ToolStripItemAlignment.Right
			};
			_ = toolStrip1.Items.Add(host);
			btnRunScript.Enabled = false;
			btnRunScript.Click += RunScript_Click;

			if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile))
			{
				LoadDataFromFile(initialFile);
			}
			else if (File.Exists(lastrun))
			{
				LoadDataFromFile(lastrun);
			}
		}

		private static Color IntToColor(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

		//private void TxtIn_StyleNeeded(object sender, StyleNeededEventArgs e)
		//{
		//  var scintilla = sender as Scintilla;
		//  var startPos = scintilla.GetEndStyled();
		//  var endPos = e.Position;
		//  // Start styling
		//  //scintilla.StartStyling(startPos);
		//  //while (startPos < endPos)
		//  //{
		//  //  var c = (char)scintilla.GetCharAt(startPos);
		//  //  if (c == ';')
		//  //  {
		//  //      scintilla.SetStyling(endPos - startPos, Style.Cpp.CommentLine);
		//  //      break;
		//  //  }
		//  //  startPos++;
		//  //}
		//}
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

		private void InitSyntaxColoring(Scintilla txt)
		{
			//Configure the default style.
			txt.StyleResetDefault();
			txt.Styles[Style.Default].Font = "Consolas";
			txt.Styles[Style.Default].Size = 10;
			//txt.Styles[Style.Default].BackColor = IntToColor(0xFFFCE1);
			//txt.Styles[Style.Default].BackColor = IntToColor(0x212121);
			//txt.Styles[Style.Default].ForeColor = IntToColor(0xFFFFFF);
			txt.StyleClearAll();
			var orig = false;

			if (orig)
			{
				txt.Styles[Style.Cpp.Identifier].ForeColor = IntToColor(0xD0DAE2);
				txt.Styles[Style.Cpp.Comment].ForeColor = IntToColor(0xBD758B);
				txt.Styles[Style.Cpp.CommentLine].ForeColor = IntToColor(0x40BF57);
				txt.Styles[Style.Cpp.CommentDoc].ForeColor = IntToColor(0x2FAE35);
				txt.Styles[Style.Cpp.Number].ForeColor = IntToColor(0xFFFF00);
				txt.Styles[Style.Cpp.String].ForeColor = IntToColor(0xFFFF00);
				txt.Styles[Style.Cpp.Character].ForeColor = IntToColor(0xE95454);
				txt.Styles[Style.Cpp.Preprocessor].ForeColor = IntToColor(0x8AAFEE);
				txt.Styles[Style.Cpp.Operator].ForeColor = IntToColor(0xE0E0E0);
				txt.Styles[Style.Cpp.CommentLineDoc].ForeColor = IntToColor(0x77A7DB);
				txt.Styles[Style.Cpp.Word].ForeColor = IntToColor(0x48A8EE);
				txt.Styles[Style.Cpp.Word2].ForeColor = IntToColor(0xF98906);
				txt.SelectionBackColor = IntToColor(0x114D9C);
			}
			else
			{
				txt.Styles[Style.Cpp.Default].ForeColor = Color.Black;
				txt.Styles[Style.Cpp.Comment].ForeColor = Color.FromArgb(0, 128, 0); // Green
				txt.Styles[Style.Cpp.CommentLine].ForeColor = Color.FromArgb(0, 128, 0); // Green
				txt.Styles[Style.Cpp.CommentLineDoc].ForeColor = Color.FromArgb(0, 128, 0); // Green
				txt.Styles[Style.Cpp.Number].ForeColor = Color.DarkOliveGreen;
				txt.Styles[Style.Cpp.String].ForeColor = Color.FromArgb(163, 21, 21); // Red
				txt.Styles[Style.Cpp.Character].ForeColor = Color.FromArgb(163, 21, 21); // Red
				txt.Styles[Style.Cpp.Preprocessor].ForeColor = Color.FromArgb(128, 128, 128); // Gray
				txt.Styles[Style.Cpp.Operator].ForeColor = Color.FromArgb(0, 0, 120); // Dark Blue
				txt.Styles[Style.Cpp.Regex].ForeColor = IntToColor(0xff00ff);
				txt.Styles[Style.Cpp.Word].ForeColor = Color.Blue;
				txt.Styles[Style.Cpp.Word2].ForeColor = Color.FromArgb(52, 146, 184); // Turqoise
				txt.SelectionBackColor = Color.FromArgb(153, 201, 239);
			}

			//Extras.
			txt.Styles[Style.Cpp.CommentDocKeyword].ForeColor = IntToColor(0xB3D991);
			txt.Styles[Style.Cpp.CommentDocKeywordError].ForeColor = IntToColor(0xFF0000);
			txt.Styles[Style.Cpp.GlobalClass].ForeColor = IntToColor(0x48A8EE);
			txt.Styles[Style.Cpp.Verbatim].ForeColor = Color.FromArgb(163, 21, 21); // Red
			txt.Styles[Style.Cpp.StringEol].BackColor = Color.Pink;
			txt.LexerName = "cpp";
			//txt.LexerName = "";
			txt.SetKeywords(0, keywords1);
			txt.SetKeywords(1, keywords2);
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
			timer.Stop();
			//Script.Stop();
			WriteLastRunText();
		}

		private void WriteLastRunText()
        {
			var dir = Path.GetDirectoryName(lastrun);

			if (!Directory.Exists(dir))
				_ = Directory.CreateDirectory(dir);
				
			File.WriteAllText(lastrun, txtIn.Text);
        }

		private void Keyview_Load(object sender, EventArgs e)
		{
			InitColors(txtIn);
			InitColors(txtOut);
			InitSyntaxColoring(txtIn);//Keysharp syntax for txtIn.
			//InitSyntaxColoring(txtOut);
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
			//txtIn.StyleNeeded += TxtIn_StyleNeeded;

			if (File.Exists(lastrun))
				txtIn.Text = File.ReadAllText(lastrun);

			timer.Interval = 1000;
			timer.Tick += Timer_Tick;
			timer.Start();
		}

		private void Keyview_ResizeEnd(object sender, EventArgs e) => splitContainer.SplitterDistance = Width / 2;

		private void LoadDataFromFile(string path)
		{
			if (File.Exists(path))
			{
				FileName.Text = Path.GetFileName(path);
				txtIn.Text = File.ReadAllText(path);
				lastKeyTime = DateTime.UtcNow;
			}
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
			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				LoadDataFromFile(openFileDialog.FileName);
			}
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

		private void Timer_Tick(object sender, EventArgs e)
		{
			if ((force || ((DateTime.UtcNow - lastKeyTime).TotalSeconds >= updateFreqSeconds && lastKeyTime > lastCompileTime)) && txtIn.Text != "")
			{
				timer.Enabled = false;
				var oldIndex = txtOut.FirstVisibleLine;

				KeyviewCompilerRunner.RunCompile(
					txtIn.Text,
					ch,
					ref fullCode,
					ref trimmedCode,
					trimend,
					trimstr,
					ref lastCompileTime,
					SetStart,
					SetSuccess,
					SetFailure,
					text => tslCodeStatus.Text = text,
					Refresh,
					SetTxtOut,
					() => chkFullCode.Checked,
					() => btnRunScript.Enabled = false,
					() => btnRunScript.Enabled = true,
					WriteLastRunText,
					() => oldIndex = txtOut.FirstVisibleLine,
					() => txtOut.FirstVisibleLine = oldIndex);

				timer.Enabled = true;
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

			if (CompilerHelper.compiledasm == null)
			{
				_ = MessageBox.Show("Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			scriptProcess = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "Keysharp.exe",
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

			using (var writer = new BinaryWriter(scriptProcess.StandardInput.BaseStream))
			{
				writer.Write(CompilerHelper.compiledBytes.Length);
				writer.Write(CompilerHelper.compiledBytes);
				writer.Flush();
			}

			btnRunScript.Text = btnRunScriptText["Stop"];
		}


		private void TxtIn_DragDrop(object sender, DragEventArgs e)
		{
			var data = e.Data.GetData(DataFormats.FileDrop);

			if (data is string[] filenames)
			{
				try
				{
					if (filenames.Length > 0)
						txtIn.Text = File.ReadAllText(filenames[0]);
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
		private readonly TextArea inputArea = new ();
		private readonly TextArea outputArea = new ();
		private readonly TextBox searchBox = new ();
		private readonly Button nextSearchButton = new () { Text = "Next" };
		private readonly Button prevSearchButton = new () { Text = "Prev" };
		private readonly Button closeSearchButton = new () { Text = "Close" };
		private readonly CheckBox fullCodeCheck = new () { Text = "Full code" };
		private readonly Button copyFullCodeButton = new () { Text = "Copy full code" };
		private readonly Button runScriptButton = new () { Text = "▶ Run script (F9)" };
		private readonly Label codeStatusLabel = new () { Text = "" };
		private readonly Panel searchPanel = new ();
		private readonly ButtonMenuItem undoMenuItem = new () { Text = "&Undo" };
		private readonly ButtonMenuItem redoMenuItem = new () { Text = "&Redo" };
		private Splitter editorSplitter;
		private readonly UITimer timer = new ();
		private readonly CompilerHelper ch = new ();
		private readonly char[] trimend = ['\n', '\r'];
		private readonly string trimstr = "{}\t";
		private readonly double updateFreqSeconds = 1;
		private readonly string lastrun;
		private readonly Dictionary<string, string> runScriptText = new ()
		{
			{ "Run", "▶ Run script (F9)" },
			{ "Stop", "◾️ Stop script (F9)" }
		};

		private bool force;
		private string fullCode = "";
		private DateTime lastCompileTime = DateTime.UtcNow;
		private DateTime lastKeyTime = DateTime.UtcNow;
		private bool searchIsOpen;
		private string trimmedCode = "";
		private Process scriptProcess;
		private string lastSearch = "";
		private int lastSearchIndex;
		private bool suppressUndo;
		private string lastText = "";
		private int lastSelectionStart;
		private int lastSelectionLength;

		public Keyview(string initialFile = null)
		{
			lastrun = $"{Accessors.A_AppData}/Keysharp/lastkeyviewrun.txt";
			Title = $"Keyview {Assembly.GetExecutingAssembly().GetName().Version}";
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
				FitToScreen();
				if (editorSplitter != null)
					editorSplitter.Position = Math.Max(200, ClientSize.Width / 2);
			};

			Closed += (_, _) =>
			{
				timer.Stop();
				WriteLastRunText();
			};

				if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile))
				{
					LoadDataFromFile(initialFile);
				}
				else if (File.Exists(lastrun))
				{
					LoadDataFromFile(lastrun);
				}
			}

		private void InitializeMenu()
		{
			var fileMenu = new ButtonMenuItem { Text = "&File" };
			var openItem = new ButtonMenuItem { Text = "&Open..." };
			openItem.Click += (_, _) => OpenFile();
			fileMenu.Items.Add(openItem);

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
			var font = TryMonospaceFont(10);
			inputArea.Font = font;
			outputArea.Font = font;
			outputArea.ReadOnly = true;
			inputArea.Wrap = true;
			outputArea.Wrap = true;
			inputArea.TextChanged += InputArea_TextChanged;
			inputArea.KeyDown += InputArea_KeyDown;
			inputArea.MouseUp += (_, _) => UpdateSelectionSnapshot();
			outputArea.KeyDown += InputArea_KeyDown;
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

			var statusRow = new StackLayout
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				Items =
				{
					new StackLayoutItem(new Panel()) { Expand = true },
					new Label { Text = "Code compile:" },
					codeStatusLabel,
					fullCodeCheck,
					copyFullCodeButton,
					runScriptButton
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
			
			RectangleF area = new RectangleF(0, 0, 1200, 800);
			try {
				area = screen.WorkingArea;
			} catch { }
			var width = (int)Math.Min(ClientSize.Width, area.Width);
			var height = (int)Math.Min(ClientSize.Height, area.Height);
			ClientSize = new Eto.Drawing.Size(width, height);
			Location = new Point(
				(int)area.X + Math.Max(0, (int)(area.Width - Size.Width) / 2),
				(int)area.Y + Math.Max(0, (int)(area.Height - Size.Height) / 2));
		}

		private void InputArea_TextChanged(object sender, EventArgs e)
		{
			RecordUndoSnapshot();
			lastKeyTime = DateTime.UtcNow;
		}

		private void InputArea_KeyDown(object sender, KeyEventArgs e)
		{
			if (ReferenceEquals(sender, inputArea))
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

			if (ReferenceEquals(sender, inputArea))
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

		private void RecordUndoSnapshot()
		{
			if (suppressUndo)
				return;

			var currentText = inputArea.Text ?? "";
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
			var font = TryMonospaceFont(10);
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

			inputArea.Text = File.ReadAllText(path);
			lastKeyTime = DateTime.UtcNow;
			ResetUndoHistory();
		}

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
			outputArea.Text = text;
		}

		private void UpdateOutputFromCache()
		{
			var desired = fullCodeCheck.Checked == true ? fullCode : trimmedCode;
			if (string.IsNullOrEmpty(desired))
				return;
			SetOutputText(desired);
		}

		private void Timer_Elapsed(object sender, EventArgs e)
		{
			if ((force || ((DateTime.UtcNow - lastKeyTime).TotalSeconds >= updateFreqSeconds && lastKeyTime > lastCompileTime)) && !string.IsNullOrEmpty(inputArea.Text))
			{
				timer.Stop();

				KeyviewCompilerRunner.RunCompile(
					inputArea.Text,
					ch,
					ref fullCode,
					ref trimmedCode,
					trimend,
					trimstr,
					ref lastCompileTime,
					SetStart,
					SetSuccess,
					SetFailure,
					text => codeStatusLabel.Text = text,
					null,
					SetOutputText,
					() => fullCodeCheck.Checked == true,
					() => runScriptButton.Enabled = false,
					() => runScriptButton.Enabled = true,
					WriteLastRunText,
					null,
					null);

				timer.Start();
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

			if (CompilerHelper.compiledasm == null)
			{
				MessageBox.Show(this, "Please wait, code is still compiling...", "Error", MessageBoxButtons.OK, MessageBoxType.Error);
				return;
			}

			scriptProcess = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "Keysharp",
					Arguments = "--assembly *",
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};
			scriptProcess.EnableRaisingEvents = true;
			scriptProcess.Exited += (_, _) =>
			{
				Application.Instance.AsyncInvoke(() =>
				{
					runScriptButton.Text = runScriptText["Run"];
					scriptProcess = null;
				});
			};
			_ = scriptProcess.Start();

			using (var writer = new BinaryWriter(scriptProcess.StandardInput.BaseStream))
			{
				writer.Write(CompilerHelper.compiledBytes.Length);
				writer.Write(CompilerHelper.compiledBytes);
				writer.Flush();
			}

			runScriptButton.Text = runScriptText["Stop"];
		}

		private void WriteLastRunText()
		{
			var dir = Path.GetDirectoryName(lastrun);
			if (!Directory.Exists(dir))
				_ = Directory.CreateDirectory(dir);
			File.WriteAllText(lastrun, inputArea.Text ?? "");
		}
	}
#endif

	internal static class KeyviewCompilerRunner
	{
		internal static void RunCompile(
			string inputText,
			CompilerHelper compiler,
			ref string fullCode,
			ref string trimmedCode,
			char[] trimend,
			string trimstr,
			ref DateTime lastCompileTime,
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
			Script script = null;
			try
			{
				lastCompileTime = DateTime.UtcNow;
				script = new Script();
				CompilerHelper.compiledasm = null;
				disableRunButton?.Invoke();
				setStart?.Invoke();
				setStatus?.Invoke("Creating DOM from script...");
				refreshStatus?.Invoke();
				var (units, domerrs) = compiler.CreateCompilationUnitFromFile(inputText);

				if (domerrs.HasErrors)
				{
					var (errors, warnings) = CompilerHelper.GetCompilerErrors(domerrs);
					setFailure?.Invoke();
					var txt = "Error creating DOM from script.";
					if (errors.Length > 0)
						txt += $"\n\n{errors}";
					if (warnings.Length > 0)
						txt += $"\n\n{warnings}";
					setOutput?.Invoke(txt);
					return;
				}

				setStatus?.Invoke("Creating C# code from DOM...");
				refreshStatus?.Invoke();
				var code = PrettyPrinter.Print(units[0]);

#if DEBUG
				var normalized = units[0].NormalizeWhitespace("\t", Environment.NewLine).ToString();
				if (code != normalized)
					throw new Exception("Code formatting mismatch");
#endif

				setStatus?.Invoke("Compiling C# code...");
				refreshStatus?.Invoke();
				var (results, ms, compileexc) = compiler.Compile(units[0], "Keyview", Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath)));

				if (results == null)
				{
					setFailure?.Invoke();
					setOutput?.Invoke($"Error compiling C# code to executable: {(compileexc != null ? compileexc.Message : string.Empty)}\n\n{code}");
				}
				else if (results.Success)
				{
					setSuccess?.Invoke((DateTime.UtcNow - lastCompileTime).TotalSeconds);
					fullCode = code;
					var token = "[System.STAThreadAttribute()]";
					var start = code.IndexOf(token);
					code = code.AsSpan(start + token.Length + 2).TrimEnd(trimend).ToString();
					var sb = new StringBuilder(code.Length);

					foreach (var line in code.SplitLines())
						_ = sb.AppendLine(line.TrimNofAnyFromStart(trimstr, 2));

					trimmedCode = sb.ToString().TrimEnd(trimend);
					beforeOutput?.Invoke();
					setOutput?.Invoke(useFullCode() ? fullCode : trimmedCode);
					afterOutput?.Invoke();
					writeLastRun?.Invoke();
					_ = ms.Seek(0, SeekOrigin.Begin);
					var arr = ms.ToArray();
					CompilerHelper.compiledBytes = arr;
					CompilerHelper.compiledasm = Assembly.Load(arr);
				}
				else
				{
					setFailure?.Invoke();
					setOutput?.Invoke(CompilerHelper.HandleCompilerErrors(results.Diagnostics, "Keyview", "Compiling C# code to executable", compileexc != null ? compileexc.Message : string.Empty) + "\n" + code);
				}

				ms?.Dispose();
			}
			catch (Exception ex)
			{
				setFailure?.Invoke();
				setOutput?.Invoke(ex.ToString());
			}
			finally
			{
				script?.Stop();
				enableRunButton?.Invoke();
			}
		}
	}
}
