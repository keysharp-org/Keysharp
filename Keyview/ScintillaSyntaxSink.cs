#if WINDOWS
namespace Keyview
{
	/// <summary>
	/// Drives Scintilla's styling from the shared <see cref="SyntaxHighlighter"/> instead of a built-in lexer
	/// (there is no AutoHotkey one; the C++ lexer was standing in for it).
	/// <para>Scintilla styles <em>sequentially</em>: every byte from <c>GetEndStyled</c> onward must be assigned
	/// before the next range can be, so gaps between colored spans are filled rather than skipped.</para>
	/// </summary>
	internal sealed class ScintillaSyntaxSink : ISyntaxSink
	{
		// Scintilla reserves style 32 and up for itself, so the token styles live below that.
		private const int StyleBase = 11;

		private readonly ScintillaNET.Scintilla scintilla;
		private int styled;

		private ScintillaSyntaxSink(ScintillaNET.Scintilla scintilla, int from)
		{
			this.scintilla = scintilla;
			styled = from;
		}

		private static int StyleOf(SyntaxColor color) => color == SyntaxColor.Default ? ScintillaNET.Style.Default : StyleBase + (int)color;

		public void Style(int start, int endExclusive, SyntaxColor color)
		{
			if (endExclusive <= start || start < styled)
				return;

			if (start > styled)
				scintilla.SetStyling(start - styled, ScintillaNET.Style.Default);   // the gap Scintilla insists on

			scintilla.SetStyling(endExclusive - start, StyleOf(color));
			styled = endExclusive;
		}

		/// <summary>Assigns the default style to everything left, which Scintilla requires before it will repaint.</summary>
		private void Finish(int to)
		{
			if (to > styled)
				scintilla.SetStyling(to - styled, ScintillaNET.Style.Default);

			styled = to;
		}

		/// <summary>
		/// Configures the token styles and switches <paramref name="scintilla"/> to the container lexer, so that
		/// <see cref="Restyle"/> is what colors it from then on.
		/// </summary>
		internal static void Attach(ScintillaNET.Scintilla scintilla)
		{
			// Our style indices overlap the built-in lexers', so clear whatever a previous one left behind.
			scintilla.StyleClearAll();

			foreach (SyntaxColor color in Enum.GetValues<SyntaxColor>())
			{
				if (color == SyntaxColor.Default)
					continue;

				var rgb = SyntaxPalette.ToRgb(color);
				scintilla.Styles[StyleOf(color)].ForeColor = Color.FromArgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
			}

			// No lexer name = the container styles the document, so Scintilla raises StyleNeeded instead.
			scintilla.LexerName = "";
		}

		/// <summary>
		/// Styles the whole document with <paramref name="highlighter"/>. Called from StyleNeeded. Tokenizes the
		/// entire text rather than the requested range, since the rules are context-sensitive from the file start.
		/// </summary>
		internal static void Restyle(ScintillaNET.Scintilla scintilla, SyntaxHighlighter highlighter)
		{
			var text = scintilla.Text ?? "";
			var n = text.Length;

			scintilla.StartStyling(0);

			if (n == 0)
				return;

			var sink = new ScintillaSyntaxSink(scintilla, 0);

			// Over the size limit the tokenizer declines to run; everything still has to be styled, or Scintilla
			// keeps asking for the same range forever.
			if (highlighter.CanHighlight(n))
				highlighter.Highlight(sink, text);

			sink.Finish(n);
		}
	}
}
#endif
