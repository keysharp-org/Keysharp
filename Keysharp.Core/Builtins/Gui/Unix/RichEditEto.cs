#if !WINDOWS
namespace Keysharp.Builtins
{
	/// <summary>
	/// What <see cref="Gui.RichEdit"/> needs of Eto's <see cref="RichTextArea"/>. Formatting a range goes
	/// through its text buffer, which takes a range directly and so leaves the caret alone; everything the
	/// buffer has no answer for is named in <see cref="Gaps"/> instead of being faked.
	/// <para>The buffer reports every formatting change as a text change, which would otherwise mark the
	/// control modified and raise a script's Change event for a re-highlight that altered no text. Hence
	/// <see cref="IsFormatting"/>, which the holder's own bookkeeping and the Change event both consult.</para>
	/// </summary>
	public partial class KeysharpRichEdit
	{
		//Eto's text buffer formats a range and loads a document, and that is all it does: it cannot describe a
		//range's rich text, a paragraph, where a character is drawn, or where the view is scrolled to. GTK's
		//text widget additionally has no rich text of any kind - only Eto.Mac's does.
		internal const RichEditGaps Gaps = RichEditGaps.RtfSelection | RichEditGaps.Paragraph
										  | RichEditGaps.HitTest | RichEditGaps.ScrollPosition
#if LINUX
										  | RichEditGaps.Rtf
#endif
										  ;

		private int formatDepth;
		private bool modified;
		private float zoom = 1f;
		private float baseFontSize;

		/// <summary>Raised when the caret moves or the selection changes; the holder decides which of those matter.</summary>
		internal event EventHandler SelectionChange;

		//Eto has no notion of a link in the text, so nothing raises this. It is declared anyway so that the
		//holder wires the same two events on every platform.
#pragma warning disable CS0067
		/// <summary>Never raised here: no platform but Windows detects links in the text.</summary>
		internal event EventHandler<RichEditLinkEventArgs> LinkClick;
#pragma warning restore CS0067

		/// <summary>
		/// Whether a formatting change is being applied right now. The buffer reports one as a text change,
		/// which is neither an edit the user made nor one the Change event should carry.
		/// </summary>
		internal bool IsFormatting => formatDepth > 0;

		internal bool Modified
		{
			get => modified;
			set => modified = value;
		}

		public bool WordWrap
		{
			get => Wrap;
			set => Wrap = value;
		}

		//Neither is anything Eto offers. They read back as false rather than pretending to have been set.
		internal bool DetectUrls { get => false; set { } }

		internal bool HideSelection { get => false; set { } }

		internal int SelectionStart
		{
			get => Selection.Start;
			set => SelectRange(value, 0);
		}

		internal int SelectionLength
		{
			get
			{
				var sel = Selection;
				return Math.Max(0, sel.End - sel.Start + 1);
			}
			set => SelectRange(Selection.Start, value);
		}

		/// <summary>
		/// How much the text is magnified. Eto has no zoom, so this scales the control's own font - which
		/// leaves text whose size was set through the buffer at the size the buffer gave it.
		/// </summary>
		internal float ZoomFactor
		{
			get => zoom;
			set
			{
				var f = Font;

				if (f != null)
				{
					if (baseFontSize <= 0)
						baseFontSize = f.Size / zoom;

					Font = new Eto.Drawing.Font(f.Family, baseFontSize * value, f.FontStyle, f.FontDecoration);
				}

				zoom = value;
			}
		}

		//Neither GTK nor Cocoa gives Eto an undo history to drive, so a script is told there is nothing to undo
		//rather than being handed a call that silently does nothing it can detect.
		internal bool CanUndo => false;

		internal bool CanRedo => false;

		internal void Undo() { }

		internal void Redo() { }

		internal void ClearUndo() { }

		internal void Cut()
		{
			Copy();

			if (SelectionLength > 0)
				SelectedText = "";
		}

		internal void Copy()
		{
			var s = SelectedText;

			if (!string.IsNullOrEmpty(s))
				Eto.Forms.Clipboard.Instance.Text = s;
		}

		internal void Paste()
		{
			if (Eto.Forms.Clipboard.Instance.Text is string s && s.Length > 0)
				SelectedText = s;
		}

		internal void AppendText(string text) => Append(text ?? "", false);

		internal void ScrollToCaret()
		{
			var caret = Selection.Start;
			ScrollTo(new Eto.Forms.Range<int>(caret, caret));
		}

		internal void SelectRange(int start, int length)
		{
			if (length > 0)
				Selection = new Eto.Forms.Range<int>(start, start + length - 1);
			else
				CaretIndex = start;
		}

		internal void LoadRichFile(string path, bool rtf)
		{
			using var stream = File.OpenRead(path);
			Buffer.Load(stream, rtf ? RichTextAreaFormat.Rtf : RichTextAreaFormat.PlainText);
		}

		internal void SaveRichFile(string path, bool rtf)
		{
			using var stream = File.Create(path);
			Buffer.Save(stream, rtf ? RichTextAreaFormat.Rtf : RichTextAreaFormat.PlainText);
		}

		/// <summary>
		/// Marks the start of a formatting change. Eto's buffer needs nothing frozen - it takes a range and
		/// repaints once - so this only marks the work as the control's own, keeping it out of the Change and
		/// SelectionChange events and out of <see cref="Modified"/>.
		/// </summary>
		internal void BeginFormatUpdate() => formatDepth++;

		internal void EndFormatUpdate()
		{
			if (formatDepth > 0)
				formatDepth--;
		}

		/// <summary>Applies <paramref name="fmt"/> to the characters in <c>[start, start + length)</c>.</summary>
		internal void ApplyFormat(int start, int length, RichEditFormat fmt)
		{
			//An empty range is the caret: Eto has no range call for it, but the selection properties set the
			//attributes the next typed characters take, which is what formatting a caret means.
			if (length <= 0)
			{
				ApplyToCaret(start, fmt);
				return;
			}

			var range = new Eto.Forms.Range<int>(start, start + length - 1);
			var buffer = Buffer;

			if (fmt.color is Eto.Drawing.Color c)
				buffer.SetForeground(range, c);

			if (fmt.back is Eto.Drawing.Color bc)
				buffer.SetBackground(range, bc);
			else if (fmt.backDefault)
				buffer.SetBackground(range, DefaultBack);

			if (fmt.bold is bool b)
				buffer.SetBold(range, b);

			if (fmt.italic is bool i)
				buffer.SetItalic(range, i);

			if (fmt.underline is bool u)
				buffer.SetUnderline(range, u);

			if (fmt.strike is bool s)
				buffer.SetStrikethrough(range, s);

			if (fmt.name is string name && FamilyAvailable(name))
				buffer.SetFamily(range, new Eto.Drawing.FontFamily(name));

			//The buffer has no "just the size" call, so a size change is applied as a whole font - built from
			//the one already there so that nothing else about it moves.
			if (fmt.size is double size)
			{
				var at = ReadSelectionFont(start, length);

				if (at != null)
					buffer.SetFont(range, new Eto.Drawing.Font(at.Family,
									Conversions.ScaleFontSize((float)size), at.FontStyle, at.FontDecoration));
			}
		}

		/// <summary>
		/// The formatting at the start of <c>[start, start + length)</c>. Unlike Windows, Eto cannot say
		/// whether an attribute varies across a range, so this reports what is true where the range begins.
		/// </summary>
		internal RichEditFormat ReadFormat(int start, int length)
		{
			//Reading has to move the selection to name the character it asks about, which is not a selection
			//change a script asked about; the pair is what tells the holder to drop the events it raises.
			BeginFormatUpdate();
			var here = Selection;
			SelectRange(start, Math.Min(length, 1));
			var fmt = new RichEditFormat();
			var font = SelectionFont;

			if (font != null)
			{
				fmt.name = font.FamilyName;
				fmt.size = Math.Round(Conversions.UnscaleFontSize(font.Size), 3);
			}

			fmt.bold = SelectionBold;
			fmt.italic = SelectionItalic;
			fmt.underline = SelectionUnderline;
			fmt.strike = SelectionStrikethrough;
			var fore = SelectionForeground;

			if (fore.Ab > 0)
				fmt.color = fore;

			var back = SelectionBackground;

			//Transparent is how a backend reports "no background of its own", and the control's own colour is
			//what the buffer had to be given for "BackgroundDefault" - neither is a colour this range chose.
			if (back.Ab > 0 && back != DefaultBack)
				fmt.back = back;

			Selection = here;
			EndFormatUpdate();
			return fmt;
		}

		// ---- what Eto has no answer for; the holder refuses these before it reaches them -----------------

		internal string SelectedRtf { get => ""; set { } }

		internal int TopPos { get => 0; set { } }

		internal int PosFromPointCore(int x, int y) => 0;

		internal void PointFromPosCore(int pos, out int x, out int y) => (x, y) = (0, 0);

		internal void ApplyParagraph(int start, int length, RichEditParagraph para) { }

		internal RichEditParagraph ReadParagraph(int pos) => new ();

		// ---- internals ----------------------------------------------------------------------------------

		//Asked of the platform rather than by scanning every installed family, which a highlighter naming one
		//per token would otherwise pay for on each call. This is how Conversions.ParseFont checks too.
		private static bool FamilyAvailable(string name) =>
			Eto.Platform.Instance.CreateShared<Eto.Drawing.Fonts.IHandler>().FontFamilyAvailable(name);

		//What "no background of its own" has to be painted as. GTK's text tags carry no alpha, so clearing one
		//is not possible: the control's own colour is the nearest thing that looks unpainted.
		private Eto.Drawing.Color DefaultBack =>
			BackgroundColor.Ab > 0 ? BackgroundColor : Eto.Drawing.Colors.Transparent;

		private void ApplyToCaret(int start, RichEditFormat fmt)
		{
			SelectRange(start, 0);

			if (fmt.color is Eto.Drawing.Color c)
				SelectionForeground = c;

			if (fmt.back is Eto.Drawing.Color bc)
				SelectionBackground = bc;
			else if (fmt.backDefault)
				SelectionBackground = DefaultBack;

			if (fmt.bold is bool b)
				SelectionBold = b;

			if (fmt.italic is bool i)
				SelectionItalic = i;

			if (fmt.underline is bool u)
				SelectionUnderline = u;

			if (fmt.strike is bool s)
				SelectionStrikethrough = s;

			if (fmt.name is string name || fmt.size is double)
			{
				var at = SelectionFont;

				if (at != null)
				{
					var family = fmt.name is string n && FamilyAvailable(n) ? new Eto.Drawing.FontFamily(n) : at.Family;
					var size = fmt.size is double sz ? Conversions.ScaleFontSize((float)sz) : at.Size;
					SelectionFont = new Eto.Drawing.Font(family, size, at.FontStyle, at.FontDecoration);
				}
			}
		}

		//SelectionFont reads whatever the caret sits in, so the range's first character is selected to ask.
		private Eto.Drawing.Font ReadSelectionFont(int start, int length)
		{
			BeginFormatUpdate();
			var here = Selection;
			SelectRange(start, Math.Min(length, 1));
			var font = SelectionFont;
			Selection = here;
			EndFormatUpdate();
			return font;
		}

		//Eto reports a caret move and a selection change separately; both mean the same thing to a script.
		private void HookSelectionEvents()
		{
			SelectionChanged += (s, e) => SelectionChange?.Invoke(this, e);
			CaretIndexChanged += (s, e) => SelectionChange?.Invoke(this, e);
		}
	}
}
#endif
