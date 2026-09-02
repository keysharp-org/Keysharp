#if WINDOWS
using NativeColor = System.Drawing.Color;
#else
using NativeColor = Eto.Drawing.Color;
#endif
using Opts = Keysharp.Internals.Strings.Options;

namespace Keysharp.Builtins
{
	/// <summary>
	/// The operations a platform's rich-text widget cannot perform, so that <see cref="Gui.RichEdit"/> can say
	/// so rather than quietly doing nothing or returning a made-up answer. Each <c>KeysharpRichEdit</c> declares
	/// its own set as a constant, which folds the checks away on the platform that has no gaps.
	/// <para>Only operations with no honest fallback are listed. Undo, URL detection and the unfocused-selection
	/// highlight are missing off Windows too, but each has one: undo reports itself unavailable through
	/// <c>CanUndo</c>, and the other two read back as false.</para>
	/// </summary>
	[Flags]
	internal enum RichEditGaps
	{
		None = 0,

		/// <summary>Reading or writing the rich text of a range rather than of the whole control.</summary>
		RtfSelection = 1,

		/// <summary>Rich text at all, in any form. GTK's text widget only knows how to read and write plain text.</summary>
		Rtf = 16,

		/// <summary>Paragraph formatting: alignment, indents and bullets.</summary>
		Paragraph = 2,

		/// <summary>Mapping between character positions and client coordinates.</summary>
		HitTest = 4,

		/// <summary>Reading or setting which line is scrolled to the top.</summary>
		ScrollPosition = 8,
	}

	/// <summary>
	/// One character-formatting change, as <c>Gui.RichEdit.SetFormat</c> takes it: the font vocabulary
	/// <see cref="Ks.Font"/> owns, plus the background colour, which no font type carries. Every part is
	/// optional and an unset one leaves that attribute of the text alone, which is what lets a highlighter
	/// colour a token without also deciding its size or weight.
	/// <para>Read back by <c>GetFormat</c> as well, where an unset part means "not the same throughout the
	/// range" - the same shape a sparse spec has, so a format read from one range applies to another unchanged.</para>
	/// </summary>
	internal sealed class RichEditFormat
	{
		internal string name;
		internal double? size;
		internal NativeColor? color;
		internal bool? bold, italic, underline, strike;
		internal NativeColor? back;

		//"BackgroundDefault"/"-Background": back to the control's own colour rather than to a chosen one.
		internal bool backDefault;

		internal bool IsEmpty => name == null && !size.HasValue && !color.HasValue && !bold.HasValue
								 && !italic.HasValue && !underline.HasValue && !strike.HasValue
								 && !back.HasValue && !backDefault;

		/// <summary>The font half as a <see cref="Ks.Font"/>, which is what <c>GetFormat</c> hands back.</summary>
		internal Ks.Font ToFont()
		{
			//null, not empty: Any's ctor runs __Init/__New for any non-null args, and a snapshot built here has
			//nothing for them to do.
			var f = new Ks.Font(null);
			f.name = name;
			f.size = size;
			f.color = color;
			f.weight = bold.HasValue ? (bold.Value ? 700 : 400) : null;
			f.italic = italic;
			f.underline = underline;
			f.strike = strike;
			return f;
		}

		/// <summary>
		/// Reads a format spec from what a script passed: either a <see cref="Ks.Font"/> or an option string in
		/// the <c>Gui.SetFont</c> vocabulary, extended with <c>Background&lt;colour&gt;</c>.
		/// </summary>
		/// <param name="options">The font and background options, or a Ks.Font.</param>
		/// <param name="fontName">The family name, which the option vocabulary has no token for.</param>
		/// <param name="error">Set to the first option that was not recognised, else left alone.</param>
		internal static RichEditFormat Parse(object options, object fontName, ref string error)
		{
			var fmt = new RichEditFormat();

			if (options is Ks.Font f)
				fmt.TakeFont(f);
			else
			{
				var opts = options.As();

				if (opts.Length > 0)
				{
					//Ks.Font owns the whole font vocabulary, so the two cannot drift apart; the background
					//tokens reach the unknown-option handler because a font has nowhere to put them.
					var spec = new Ks.Font(null);
					string bad = null;
					spec.Parse(opts, tok =>
					{
						var span = tok.AsSpan();
						var c = default(NativeColor);

						//"BackgroundDefault", a bare "Background" and any negated spelling all revert to the
						//control's own colour; only "Background<colour>" picks one. The same rule Gui.Opt uses.
						if (span.Equals("backgrounddefault", StringComparison.OrdinalIgnoreCase)
								|| span.Equals("background", StringComparison.OrdinalIgnoreCase)
								|| span.StartsWith("-background", StringComparison.OrdinalIgnoreCase))
							fmt.backDefault = true;
						else if (Opts.TryParse(span, "Background", ref c))
							fmt.back = c;
						else
							bad ??= tok;
					});
					fmt.TakeFont(spec);

					if (bad != null)
						error = bad;
				}
			}

			var n = fontName.As();

			if (n.Length > 0)
				fmt.name = n;

			return fmt;
		}

		//Copied out rather than held onto: a Ks.Font a script passed is its own object, and the family name
		//argument would otherwise change it.
		private void TakeFont(Ks.Font f)
		{
			name = f.name;
			size = f.size;
			color = f.color;
			//Only <=400 and >=700 mean anything, which is the rule Ks.Font applies to a weight everywhere else.
			bold = f.weight is int w ? (w >= 700 ? true : w <= 400 ? false : null) : null;
			italic = f.italic;
			underline = f.underline;
			strike = f.strike;
		}
	}

	/// <summary>
	/// Paragraph formatting for a range, in the same sparse shape as <see cref="RichEditFormat"/>: an unset
	/// part leaves that attribute alone. Indents are in the GUI's own DPI-scaled units, like every other size
	/// a script gives a control.
	/// </summary>
	internal sealed class RichEditParagraph
	{
		//0 left, 1 center, 2 right - the numbering RichTextBox's alignment enum uses.
		internal int? align;
		internal int? indent, hangingIndent, rightIndent;
		internal bool? bullet;

		internal bool IsEmpty => !align.HasValue && !indent.HasValue && !hangingIndent.HasValue
								 && !rightIndent.HasValue && !bullet.HasValue;

		internal static RichEditParagraph Parse(string options, ref string error)
		{
			var p = new RichEditParagraph();

			foreach (Range r in options.AsSpan().SplitAny(Spaces))
			{
				var opt = options.AsSpan(r).Trim();

				if (opt.Length == 0)
					continue;

				var i = 0;

				if (opt.Equals("left", StringComparison.OrdinalIgnoreCase)) p.align = 0;
				else if (opt.Equals("center", StringComparison.OrdinalIgnoreCase)
						 || opt.Equals("centre", StringComparison.OrdinalIgnoreCase)) p.align = 1;
				else if (opt.Equals("right", StringComparison.OrdinalIgnoreCase)) p.align = 2;
				else if (opt.Equals("bullet", StringComparison.OrdinalIgnoreCase)) p.bullet = true;
				else if (opt.Equals("-bullet", StringComparison.OrdinalIgnoreCase)) p.bullet = false;
				else if (Opts.TryParse(opt, "HangingIndent", ref i)) p.hangingIndent = i;
				else if (Opts.TryParse(opt, "RightIndent", ref i)) p.rightIndent = i;
				else if (Opts.TryParse(opt, "Indent", ref i)) p.indent = i;
				else error ??= opt.ToString();
			}

			return p;
		}

		/// <summary>The set parts as an option string <see cref="Parse"/> accepts, which is what GetParagraph returns.</summary>
		internal string ToOptions()
		{
			var sb = new StringBuilder(48);

			if (align is int a)
				_ = sb.Append(a == 1 ? "Center" : a == 2 ? "Right" : "Left");

			if (indent is int i)
				_ = Sep(sb).Append("Indent").Append(i);

			if (hangingIndent is int h)
				_ = Sep(sb).Append("HangingIndent").Append(h);

			if (rightIndent is int ri)
				_ = Sep(sb).Append("RightIndent").Append(ri);

			if (bullet == true)
				_ = Sep(sb).Append("Bullet");

			return sb.ToString();

			static StringBuilder Sep(StringBuilder sb) => sb.Length > 0 ? sb.Append(' ') : sb;
		}
	}

	/// <summary>What a <c>LinkClick</c> handler is told about the link that was clicked.</summary>
	internal sealed class RichEditLinkEventArgs(string text, int start, int length) : EventArgs
	{
		internal string LinkText { get; } = text;
		internal int LinkStart { get; } = start;
		internal int LinkLength { get; } = length;
	}

	public partial class Gui
	{
		/// <summary>
		/// The holder for a RichEdit control. Like <see cref="Gui.WebView"/> it carries members of its own,
		/// because formatted text needs far more than the Value/Text shape the base <see cref="Gui.Control"/>
		/// serves: any range of characters has a font, two colours and a paragraph of its own, and a syntax
		/// highlighter drives all of that a token at a time.
		/// <para>Character positions are 1-based, like every other string position in the language, and index
		/// the same text <c>Value</c> returns - one character per line break, whatever a saved file holds.
		/// A position of 0 means "the current selection" wherever a range is asked for.</para>
		/// </summary>
		public class RichEdit : Gui.Control
		{
			//The control's text as its own character offsets index it, plus where each line starts in it.
			//Rebuilt on the first line or position call after an edit: a highlighter walks lines in a loop, and
			//fetching the whole document from the widget once per call would make that quadratic.
			private string cachedText;
			private int[] lineStarts;
			private CallbackRegistry selectionChangeHandlers;
			private CallbackRegistry linkClickHandlers;
			//Each of this control's own events is wired to the widget only once a script asks for it, and then
			//stays wired: a handler list which has emptied and refilled must not wire a second one.
			private bool selectionChangeWired, linkClickWired;
			private long lastSelStart = -1, lastSelLength = -1;

			//How deep the BeginUpdate/EndUpdate pairs are nested; only the outermost freezes the control.
			private int updateDepth;

			public RichEdit(params object[] args) : base(args)
			{
				if (Ctrl is KeysharpRichEdit rt)
					rt.TextChanged += (_, _) => cachedText = null;
			}

			private KeysharpRichEdit Rt => Ctrl as KeysharpRichEdit;

			// ---- content ---------------------------------------------------------------------------------

			/// <summary>
			/// The whole control's contents as an RTF document. Assigning something that is not valid RTF
			/// raises a ValueError rather than leaving the control half-loaded.
			/// </summary>
			public object RichText
			{
				get
				{
					if (Rt is not { } rt)
						return NoControl();

					return Unsupported(RichEditGaps.Rtf, "RichText") ?? (object)(rt.Rtf ?? "");
				}
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					if (Unsupported(RichEditGaps.Rtf, "RichText") != null)
						return;

					cachedText = null;

					try { rt.Rtf = value.As(); }
					catch (Exception ex) { _ = Errors.ValueErrorOccurred($"The text is not valid RTF: {ex.Message}"); }
				}
			}

			/// <summary>The selected text, without its formatting. Assigning replaces the selection.</summary>
			public object SelectedText
			{
				get => Rt is { } rt ? rt.SelectedText ?? "" : NoControl();
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					cachedText = null;
					rt.SelectedText = value.As();
				}
			}

			/// <summary>
			/// The selected text as an RTF document. Assigning replaces the selection with it, which is how
			/// formatted text is inserted without rewriting the whole control. Windows only.
			/// </summary>
			public object SelectedRichText
			{
				get
				{
					if (Rt is not { } rt)
						return NoControl();

					return Unsupported(RichEditGaps.RtfSelection, "SelectedRichText") ?? (object)(rt.SelectedRtf ?? "");
				}
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					if (Unsupported(RichEditGaps.RtfSelection, "SelectedRichText") != null)
						return;

					cachedText = null;

					try { rt.SelectedRtf = value.As(); }
					catch (Exception ex) { _ = Errors.ValueErrorOccurred($"The text is not valid RTF: {ex.Message}"); }
				}
			}

			/// <summary>How many characters the control holds, counting each line break as one.</summary>
			public object TextLength => Rt is { } rt ? (object)(long)Lines(rt).Length : NoControl();

			/// <summary>How many lines the control holds. A trailing line break leaves an empty last line.</summary>
			public object LineCount
			{
				get
				{
					if (Rt is not { } rt)
						return NoControl();

					_ = Lines(rt);
					return (long)lineStarts.Length;
				}
			}

			/// <summary>
			/// Whether the contents have been edited since this was last cleared. Set it to false after saving
			/// to know later whether there is anything to save.
			/// </summary>
			public object Modified
			{
				get => Rt is { } rt ? rt.Modified : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.Modified = Options.OnOff(value) ?? false;
					else
						_ = NoControl();
				}
			}

			/// <summary>Whether the user can edit the text. A read-only control can still be changed by script.</summary>
			public object ReadOnly
			{
				get => Rt is { } rt ? rt.ReadOnly : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.ReadOnly = Options.OnOff(value) ?? false;
					else
						_ = NoControl();
				}
			}

			/// <summary>
			/// Whether a line too long to fit is wrapped instead of scrolled to. Off is what a code editor
			/// wants, since it keeps a line number and a display row the same thing.
			/// </summary>
			public object WordWrap
			{
				get => Rt is { } rt ? rt.WordWrap : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.WordWrap = Options.OnOff(value) ?? false;
					else
						_ = NoControl();
				}
			}

			/// <summary>
			/// Whether URLs in the text become clickable links, which raise the LinkClick event. Windows only;
			/// reads back as false elsewhere.
			/// </summary>
			public object DetectUrls
			{
				get => Rt is { } rt ? rt.DetectUrls : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.DetectUrls = Options.OnOff(value) ?? false;
					else
						_ = NoControl();
				}
			}

			/// <summary>
			/// Whether the selection stays highlighted while the control does not have focus. Windows only;
			/// reads back as false elsewhere.
			/// </summary>
			public object HideSelection
			{
				get => Rt is { } rt ? rt.HideSelection : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.HideSelection = Options.OnOff(value) ?? false;
					else
						_ = NoControl();
				}
			}

			/// <summary>
			/// How much the text is magnified, 1 being unmagnified. Off Windows this scales the control's own
			/// font, so text whose size SetFormat chose keeps that size.
			/// </summary>
			public object Zoom
			{
				get => Rt is { } rt ? (object)(double)rt.ZoomFactor : NoControl();
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					var z = value.Ad(1.0);

					//The widget's own range. A zoom outside it is refused rather than clamped, since a zoom of
					//zero cannot be undone by dividing by it.
					if (!double.IsFinite(z) || z < 0.015625 || z > 64.0)
						_ = Errors.ValueErrorOccurred($"Zoom must be between 1/64 and 64, not {value}.");
					else
						rt.ZoomFactor = (float)z;
				}
			}

			// ---- selection and caret ---------------------------------------------------------------------

			/// <summary>Where the selection starts, 1-based. Assigning moves the caret there and selects nothing.</summary>
			public object SelectionStart
			{
				get => Rt is { } rt ? (object)(long)(rt.SelectionStart + 1) : NoControl();
				set
				{
					if (Rt is { } rt)
						rt.SelectRange(ClampPos(rt, value.Al()), 0);
					else
						_ = NoControl();
				}
			}

			/// <summary>How many characters are selected. Assigning extends the selection from where it starts.</summary>
			public object SelectionLength
			{
				get => Rt is { } rt ? (object)(long)rt.SelectionLength : NoControl();
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					var start = rt.SelectionStart;
					rt.SelectRange(start, ClampLength(rt, start, value.Al()));
				}
			}

			/// <summary>The 1-based line the caret is on.</summary>
			public object CurrentLine => Rt is { } rt ? (object)(long)(LineOf(rt, rt.SelectionStart) + 1) : NoControl();

			/// <summary>The 1-based column the caret is on; 1 at the start of a line.</summary>
			public object CurrentCol
			{
				get
				{
					if (Rt is not { } rt)
						return NoControl();

					var pos = rt.SelectionStart;
					return (long)(pos - lineStarts[LineOf(rt, pos)] + 1);
				}
			}

			/// <summary>
			/// The 1-based line scrolled to the top of the control. Assigning one scrolls there without moving
			/// the caret. Windows only, since no other platform's widget reports where it is scrolled to.
			/// </summary>
			public object FirstVisibleLine
			{
				get
				{
					if (Rt is not { } rt)
						return NoControl();

					return Unsupported(RichEditGaps.ScrollPosition, "FirstVisibleLine") ?? (object)(long)(LineOf(rt, rt.TopPos) + 1);
				}
				set
				{
					if (Rt is not { } rt)
					{
						_ = NoControl();
						return;
					}

					if (Unsupported(RichEditGaps.ScrollPosition, "FirstVisibleLine") == null)
						rt.TopPos = lineStarts[ClampLine(rt, value.Al())];
				}
			}

			/// <summary>
			/// Selects <paramref name="length"/> characters starting at the 1-based <paramref name="start"/>.
			/// A length of 0 just moves the caret there.
			/// </summary>
			public object Select(object start, object length = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var s = ClampPos(rt, start.Al());
				rt.SelectRange(s, ClampLength(rt, s, length.Al()));
				return DefaultObject;
			}

			/// <summary>Selects everything.</summary>
			public object SelectAll()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.SelectAll();
				return DefaultObject;
			}

			/// <summary>Scrolls so that the caret is visible.</summary>
			public object ScrollCaret()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.ScrollToCaret();
				return DefaultObject;
			}

			// ---- lines and positions ---------------------------------------------------------------------

			/// <summary>The text of the 1-based <paramref name="line"/>, without its line break.</summary>
			public object GetLine(object line)
			{
				if (Rt is not { } rt)
					return NoControl();

				var text = Lines(rt);
				var n = ClampLine(rt, line.Al());
				var start = lineStarts[n];
				var end = n + 1 < lineStarts.Length ? lineStarts[n + 1] - 1 : text.Length;
				return text[start..end];
			}

			/// <summary>How many characters the 1-based <paramref name="line"/> holds, not counting its line break.</summary>
			public object LineLength(object line) => GetLine(line) is string s ? (object)(long)s.Length : DefaultErrorLong;

			/// <summary>The 1-based line the 1-based <paramref name="pos"/> is on.</summary>
			public object LineFromPos(object pos)
			{
				if (Rt is not { } rt)
					return NoControl();

				return (long)(LineOf(rt, ClampPos(rt, pos.Al())) + 1);
			}

			/// <summary>The 1-based position of the first character of the 1-based <paramref name="line"/>.</summary>
			public object PosFromLine(object line)
			{
				if (Rt is not { } rt)
					return NoControl();

				return (long)(lineStarts[ClampLine(rt, line.Al())] + 1);
			}

			/// <summary>
			/// The 1-based position of the character nearest the client point <paramref name="x"/>,
			/// <paramref name="y"/> - where a click there would leave the caret. Windows only.
			/// </summary>
			public object PosFromPoint(object x, object y)
			{
				if (Rt is not { } rt)
					return NoControl();

				if (Unsupported(RichEditGaps.HitTest, "PosFromPoint") is { } err)
					return err;

				var scale = ((Gui)Gui).DpiScale;
				return (long)(rt.PosFromPointCore((int)Math.Round(x.Ad() * scale), (int)Math.Round(y.Ad() * scale)) + 1);
			}

			/// <summary>
			/// Where the 1-based <paramref name="pos"/> is drawn, as a <c>{X, Y}</c> object in the control's
			/// client coordinates: the top left of that character, which is where a tooltip or a completion
			/// list belongs. Windows only.
			/// </summary>
			public object PointFromPos(object pos)
			{
				if (Rt is not { } rt)
					return NoControl();

				if (Unsupported(RichEditGaps.HitTest, "PointFromPos") is { } err)
					return err;

				rt.PointFromPosCore(ClampPos(rt, pos.Al()), out var x, out var y);
				var scale = ((Gui)Gui).DpiScale;
				var o = new KeysharpObject();
				o.DefinePropInternal("X", new OwnPropsDesc(o, (long)Math.Round(x / scale)));
				o.DefinePropInternal("Y", new OwnPropsDesc(o, (long)Math.Round(y / scale)));
				return o;
			}

			// ---- editing ---------------------------------------------------------------------------------

			/// <summary>Whether there is an edit to undo. Always false off Windows, where the widget keeps no history.</summary>
			public object CanUndo => Rt is { } rt ? rt.CanUndo : NoControl();

			/// <summary>Whether there is an undone edit to redo. Always false off Windows.</summary>
			public object CanRedo => Rt is { } rt ? rt.CanRedo : NoControl();

			/// <summary>Undoes the last edit, or does nothing when there is none.</summary>
			public object Undo()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.Undo();
				return DefaultObject;
			}

			/// <summary>Redoes the last undone edit, or does nothing when there is none.</summary>
			public object Redo()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.Redo();
				return DefaultObject;
			}

			/// <summary>Forgets the undo history, so that what is in the control now cannot be undone away.</summary>
			public object ClearUndo()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.ClearUndo();
				return DefaultObject;
			}

			/// <summary>Moves the selection to the clipboard, formatting and all.</summary>
			public object Cut()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.Cut();
				return DefaultObject;
			}

			/// <summary>Copies the selection to the clipboard, formatting and all.</summary>
			public object Copy()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.Copy();
				return DefaultObject;
			}

			/// <summary>Replaces the selection with the clipboard's contents.</summary>
			public object Paste()
			{
				if (Rt is not { } rt)
					return NoControl();

				rt.Paste();
				return DefaultObject;
			}

			/// <summary>
			/// Adds <paramref name="text"/> to the end, formatted like the text already there. Cheaper than
			/// assigning Value, which rewrites and reformats everything.
			/// </summary>
			public object Append(object text)
			{
				if (Rt is not { } rt)
					return NoControl();

				cachedText = null;
				rt.AppendText(text.As());
				return DefaultObject;
			}

			/// <summary>
			/// Replaces <paramref name="length"/> characters at the 1-based <paramref name="start"/> with
			/// <paramref name="text"/>, which takes on the formatting of what it replaced. One undoable edit,
			/// unlike rewriting Value.
			/// </summary>
			public object Replace(object start, object length, object text)
			{
				if (Rt is not { } rt)
					return NoControl();

				var s = ClampPos(rt, start.Al());
				rt.SelectRange(s, ClampLength(rt, s, length.Al()));
				cachedText = null;
				rt.SelectedText = text.As();
				return DefaultObject;
			}

			/// <summary>
			/// The 1-based position of <paramref name="needle"/>, or 0 when it is not there. The selection is
			/// left alone, so the caller decides for itself whether a hit is worth showing.
			/// </summary>
			/// <param name="needle">The text to look for.</param>
			/// <param name="start">Where to start looking, 1-based. Omitted means the start of the text, or its
			/// end when searching in reverse.</param>
			/// <param name="options">Any of <c>MatchCase</c>, <c>WholeWord</c> and <c>Reverse</c>.</param>
			public object Find(object needle, object start = null, object options = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var what = needle.As();

				if (what.Length == 0)
					return 0L;

				var (matchCase, wholeWord, reverse, bad) = ParseFindOptions(options.As());

				if (bad != null)
					return Errors.ValueErrorOccurred($"Unrecognized Find option \"{bad}\".");

				var text = Lines(rt);
				var from = start == null ? (reverse ? text.Length : 0) : ClampPos(rt, start.Al());
				var comp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

				while (true)
				{
					int at;

					if (reverse)
					{
						//Backwards from the character before the start, which is what "the previous match"
						//means to a caller stepping through hits.
						var upTo = Math.Min(from, text.Length) - 1;
						at = upTo < 0 ? -1 : text.LastIndexOf(what, upTo, comp);
					}
					else
						at = from > text.Length - what.Length ? -1 : text.IndexOf(what, from, comp);

					if (at < 0)
						return 0L;

					if (!wholeWord || IsWholeWord(text, at, what.Length))
						return (long)(at + 1);

					if (reverse)
					{
						if (at == 0)
							return 0L;

						from = at;
					}
					else
						from = at + 1;
				}
			}

			// ---- formatting ------------------------------------------------------------------------------

			/// <summary>
			/// Applies character formatting to <paramref name="length"/> characters at the 1-based
			/// <paramref name="start"/>, or to the current selection when <paramref name="start"/> is 0.
			/// Attributes the options do not mention are left as they are, so colouring a token does not also
			/// decide its size or weight.
			/// </summary>
			/// <param name="options">A <c>Gui.SetFont</c> option string, extended with
			/// <c>Background&lt;colour&gt;</c> and <c>BackgroundDefault</c>, or a <c>Ks.Font</c>.</param>
			/// <param name="fontName">The font family, which the option vocabulary has no token for.</param>
			public object SetFormat(object start, object length, object options = null, object fontName = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				string error = null;
				var fmt = RichEditFormat.Parse(options, fontName, ref error);

				if (error != null)
					return Errors.ValueErrorOccurred($"Unrecognized formatting option \"{error}\".");

				if (fmt.IsEmpty)
					return DefaultObject;

				var (s, len) = Range(rt, start, length);
				//Formatting a range moves the selection there, so a call outside a Begin/EndUpdate pair puts it
				//back itself; inside one, the pair does it once for the whole batch.
				var batched = updateDepth > 0;

				if (!batched)
					rt.BeginFormatUpdate();

				rt.ApplyFormat(s, len, fmt);

				if (!batched)
					rt.EndFormatUpdate();

				return DefaultObject;
			}

			/// <summary>
			/// The font of <paramref name="length"/> characters at the 1-based <paramref name="start"/>, or of
			/// the current selection when <paramref name="start"/> is 0. An attribute that is not the same
			/// throughout the range reads back as "", exactly as an unset one does on any other
			/// <c>Ks.Font</c> - so what is read from one range can be applied to another unchanged.
			/// </summary>
			public object GetFormat(object start = null, object length = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var (s, len) = Range(rt, start, length);
				return rt.ReadFormat(s, len).ToFont();
			}

			/// <summary>
			/// The background colour of a range as a 6-digit RRGGBB string, or "" when the range is not all one
			/// colour or is drawn on the control's own background. The arguments are as for
			/// <see cref="GetFormat"/>; the colour is read separately because no font type carries one.
			/// </summary>
			public object GetBackColor(object start = null, object length = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var (s, len) = Range(rt, start, length);
				var back = rt.ReadFormat(s, len).back;
				return back.HasValue ? (back.Value.ToArgb() & 0x00FFFFFF).ToString("X6") : "";
			}

			/// <summary>
			/// Applies paragraph formatting to every paragraph the range touches: <paramref name="length"/>
			/// characters at the 1-based <paramref name="start"/>, or the current selection when it is 0.
			/// Windows only.
			/// </summary>
			/// <param name="options">Any of <c>Left</c>, <c>Center</c>, <c>Right</c>, <c>Indent&lt;n&gt;</c>,
			/// <c>HangingIndent&lt;n&gt;</c>, <c>RightIndent&lt;n&gt;</c>, <c>Bullet</c> and <c>-Bullet</c>.</param>
			public object SetParagraph(object start, object length, object options = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				if (Unsupported(RichEditGaps.Paragraph, "SetParagraph") is { } err)
					return err;

				string error = null;
				var para = RichEditParagraph.Parse(options.As(), ref error);

				if (error != null)
					return Errors.ValueErrorOccurred($"Unrecognized paragraph option \"{error}\".");

				if (para.IsEmpty)
					return DefaultObject;

				Scale(para, ((Gui)Gui).DpiScale, true);
				var (s, len) = Range(rt, start, length);
				var batched = updateDepth > 0;

				if (!batched)
					rt.BeginFormatUpdate();

				rt.ApplyParagraph(s, len, para);

				if (!batched)
					rt.EndFormatUpdate();

				return DefaultObject;
			}

			/// <summary>
			/// The paragraph formatting where a range starts, as an option string <see cref="SetParagraph"/>
			/// accepts. Only the range's first paragraph is read, so unlike <see cref="GetFormat"/> there is no
			/// "varies across the range" answer to give. The arguments are as for GetFormat. Windows only.
			/// </summary>
			public object GetParagraph(object start = null, object length = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				if (Unsupported(RichEditGaps.Paragraph, "GetParagraph") is { } err)
					return err;

				var (s, _) = Range(rt, start, length);
				var para = rt.ReadParagraph(s);
				Scale(para, ((Gui)Gui).DpiScale, false);
				return para.ToOptions();
			}

			// ---- batching --------------------------------------------------------------------------------

			/// <summary>
			/// Stops the control redrawing until the matching <see cref="EndUpdate"/>, and remembers what was
			/// selected and scrolled to so that formatting a range does not have to put either back. A syntax
			/// highlighter wraps its whole pass in one pair: without it every token repaints the control and
			/// drags the caret across the document.
			/// <para>Pairs nest, and only the outermost one does anything.</para>
			/// </summary>
			public object BeginUpdate()
			{
				if (Rt is not { } rt)
					return NoControl();

				if (updateDepth++ == 0)
					rt.BeginFormatUpdate();

				return DefaultObject;
			}

			/// <summary>Ends the innermost <see cref="BeginUpdate"/>, redrawing once when it was the outermost.</summary>
			public object EndUpdate()
			{
				if (Rt is not { } rt)
					return NoControl();

				if (updateDepth == 0)
					return Errors.ErrorOccurred("EndUpdate was called without a matching BeginUpdate.");

				if (--updateDepth == 0)
				{
					//A frozen control reports no text change, so an edit made between the pair - through Value,
					//say, which knows nothing of this class - left the line cache describing the old text.
					cachedText = null;
					rt.EndFormatUpdate();
				}

				return DefaultObject;
			}

			// ---- files -----------------------------------------------------------------------------------

			/// <summary>Replaces the contents with the contents of <paramref name="path"/>.</summary>
			/// <param name="format">"RTF", "Text", or omitted to go by the file's extension.</param>
			public object LoadFile(object path, object format = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var file = path.As();

				if (!ParseFormat(format, file, out var rtf))
					return Errors.ValueErrorOccurred($"Unrecognized rich text format \"{format.As()}\".");

				if (rtf && Unsupported(RichEditGaps.Rtf, "LoadFile in RTF") is { } err)
					return err;

				cachedText = null;

				try { rt.LoadRichFile(file, rtf); }
				catch (Exception ex) { return Errors.ErrorOccurred($"Failed to load {file}: {ex.Message}"); }

				return DefaultObject;
			}

			/// <summary>Writes the contents to <paramref name="path"/>, replacing whatever is there.</summary>
			/// <param name="format">"RTF", "Text", or omitted to go by the file's extension.</param>
			public object SaveFile(object path, object format = null)
			{
				if (Rt is not { } rt)
					return NoControl();

				var file = path.As();

				if (!ParseFormat(format, file, out var rtf))
					return Errors.ValueErrorOccurred($"Unrecognized rich text format \"{format.As()}\".");

				if (rtf && Unsupported(RichEditGaps.Rtf, "SaveFile in RTF") is { } err)
					return err;

				try { rt.SaveRichFile(file, rtf); }
				catch (Exception ex) { return Errors.ErrorOccurred($"Failed to save {file}: {ex.Message}"); }

				return DefaultObject;
			}

			// ---- events ----------------------------------------------------------------------------------

			/// <summary>
			/// Whether <paramref name="e"/> is one of this control's own events. Named separately from
			/// <c>Gui.Control.SupportsEvent</c> so both the gate and the registration read from one list.
			/// </summary>
			internal static bool IsRichEditEvent(string e) => e is "selectionchange" or "linkclick";

			/// <summary>Registers one of this control's own events, wiring it to the widget on first use.</summary>
			internal void ModifyEventHandlers(string e, KeysharpFunc del, long addRemove)
			{
				var link = e == "linkclick";
				ref var hub = ref link ? ref linkClickHandlers : ref selectionChangeHandlers;
				hub ??= new();
				_ = hub.ModifyEventHandlers(del, addRemove);

				if (addRemove == 0 || Rt is not { } rt)
					return;

				if (link)
				{
					if (!linkClickWired)
					{
						linkClickWired = true;
						rt.LinkClick += Rt_LinkClick;
					}
				}
				else if (!selectionChangeWired)
				{
					selectionChangeWired = true;
					rt.SelectionChange += Rt_SelectionChange;
				}
			}

			internal bool RemoveOwnedRichEditHandlers(ScriptEventScheduler scheduler)
			{
				var removedAny = selectionChangeHandlers?.RemoveOwned(scheduler) == true;
				removedAny |= linkClickHandlers?.RemoveOwned(scheduler) == true;
				return removedAny;
			}

			//The widgets report a caret move and a selection change separately, and reading or applying a range's
			//formatting moves the selection twice more. One event per change a script would notice is what it
			//wants, so anything the control is doing to itself is dropped, as is anything which leaves the
			//selection where it already was.
			private void Rt_SelectionChange(object sender, EventArgs e)
			{
				if (!eventHandlerActive || updateDepth > 0 || Rt is not { } rt || rt.IsFormatting)
					return;

				long start = rt.SelectionStart, len = rt.SelectionLength;

				if (start == lastSelStart && len == lastSelLength)
					return;

				lastSelStart = start;
				lastSelLength = len;
				_ = (selectionChangeHandlers?.InvokeEventHandlers(this, start + 1, len));
			}

			private void Rt_LinkClick(object sender, RichEditLinkEventArgs e)
			{
				if (eventHandlerActive)
					_ = (linkClickHandlers?.InvokeEventHandlers(this, e.LinkText, (long)(e.LinkStart + 1), (long)e.LinkLength));
			}

			// ---- internals -------------------------------------------------------------------------------

			private object NoControl() => Errors.ErrorOccurred("GUI control is no longer available.");

			/// <summary>
			/// The error for an operation this platform's widget cannot perform, or null when it can. Reads as
			/// a guard clause at each call site: <c>if (Unsupported(...) is { } err) return err;</c>.
			/// </summary>
			private static object Unsupported(RichEditGaps gap, string member) =>
				(KeysharpRichEdit.Gaps & gap) == 0 ? null
				: Errors.ErrorOccurred($"RichEdit.{member} is not supported on this platform.");

			/// <summary>
			/// The control's text as its own character offsets index it, rebuilt only after an edit. Line
			/// breaks are normalized to one character each, which is how the widget counts them and what
			/// <c>Value</c> returns - so a position taken from one is valid in the other.
			/// </summary>
			private string Lines(KeysharpRichEdit rt)
			{
				if (cachedText != null)
					return cachedText;

				var text = Conversions.NormalizeEol(rt.Text ?? "");
				var count = 1;

				for (var i = 0; i < text.Length; i++)
					if (text[i] == '\n')
						count++;

				var starts = new int[count];
				var k = 1;

				for (var i = 0; i < text.Length; i++)
					if (text[i] == '\n')
						starts[k++] = i + 1;

				lineStarts = starts;
				cachedText = text;
				return text;
			}

			/// <summary>The 0-based line holding the 0-based <paramref name="pos"/>.</summary>
			private int LineOf(KeysharpRichEdit rt, int pos)
			{
				_ = Lines(rt);
				var i = System.Array.BinarySearch(lineStarts, pos);
				return i >= 0 ? i : ~i - 1;
			}

			/// <summary>A script's 1-based position as a 0-based offset into the text.</summary>
			private int ClampPos(KeysharpRichEdit rt, long pos) => (int)Math.Clamp(pos - 1, 0, Lines(rt).Length);

			private int ClampLength(KeysharpRichEdit rt, int start, long length) =>
				(int)Math.Clamp(length, 0, Lines(rt).Length - start);

			/// <summary>A script's 1-based line number as a 0-based line index.</summary>
			private int ClampLine(KeysharpRichEdit rt, long line)
			{
				_ = Lines(rt);
				return (int)Math.Clamp(line - 1, 0, lineStarts.Length - 1);
			}

			/// <summary>
			/// The 0-based range a start/length pair names, where a start of 0 or an omitted one means the
			/// range currently selected.
			/// </summary>
			private (int start, int length) Range(KeysharpRichEdit rt, object start, object length)
			{
				var s = start.Al();

				if (s <= 0)
					return (rt.SelectionStart, rt.SelectionLength);

				var from = ClampPos(rt, s);
				return (from, ClampLength(rt, from, length.Al()));
			}

			/// <summary>
			/// Converts a paragraph's indents between the units a script uses and the control's pixels. Sizes
			/// are DPI-scaled the same way every other size given to a control is.
			/// </summary>
			private static void Scale(RichEditParagraph p, double scale, bool toPixels)
			{
				if (Math.Abs(scale - 1.0) < 0.0001)
					return;

				var f = toPixels ? scale : 1.0 / scale;

				if (p.indent is int i) p.indent = (int)Math.Round(i * f);

				if (p.hangingIndent is int h) p.hangingIndent = (int)Math.Round(h * f);

				if (p.rightIndent is int r) p.rightIndent = (int)Math.Round(r * f);
			}

			private static bool ParseFormat(object format, string path, out bool rtf)
			{
				var f = format.As();

				if (f.Length == 0)
				{
					rtf = path.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase);
					return true;
				}

				rtf = string.Equals(f, "rtf", StringComparison.OrdinalIgnoreCase);
				return rtf || string.Equals(f, "text", StringComparison.OrdinalIgnoreCase);
			}

			private static (bool matchCase, bool wholeWord, bool reverse, string bad) ParseFindOptions(string options)
			{
				bool matchCase = false, wholeWord = false, reverse = false;
				string bad = null;

				foreach (Range r in options.AsSpan().SplitAny(Spaces))
				{
					var opt = options.AsSpan(r).Trim();

					if (opt.Length == 0)
						continue;

					if (opt.Equals("matchcase", StringComparison.OrdinalIgnoreCase)) matchCase = true;
					else if (opt.Equals("wholeword", StringComparison.OrdinalIgnoreCase)) wholeWord = true;
					else if (opt.Equals("reverse", StringComparison.OrdinalIgnoreCase)) reverse = true;
					else bad ??= opt.ToString();
				}

				return (matchCase, wholeWord, reverse, bad);
			}

			private static bool IsWholeWord(string text, int at, int len) =>
				(at == 0 || !IsWordChar(text[at - 1])) && (at + len == text.Length || !IsWordChar(text[at + len]));

			private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
		}
	}
}
