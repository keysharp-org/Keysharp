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
			var isDark = SyntaxPalette.IsDark;

			foreach (SyntaxColor color in Enum.GetValues<SyntaxColor>())
			{
				if (color == SyntaxColor.Default)
					continue;

				scintilla.Styles[StyleOf(color)].ForeColor = SyntaxPalette.ToColor(color, isDark);
			}

			// No lexer name = the container styles the document, so Scintilla raises StyleNeeded instead.
			scintilla.LexerName = "";
		}

		private sealed class Cache { internal SyntaxHighlightSnapshot Snapshot; }
		private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScintillaNET.Scintilla, Cache> caches = new ();
		internal static void Invalidate(ScintillaNET.Scintilla scintilla) => caches.GetOrCreateValue(scintilla).Snapshot = null;

		internal static void Restyle(ScintillaNET.Scintilla scintilla, SyntaxHighlighter highlighter, int requestedEnd)
		{
			var cache = caches.GetOrCreateValue(scintilla);
			var snapshot = cache.Snapshot ??= new SyntaxHighlightSnapshot(highlighter, scintilla.Text ?? "");
			var from = scintilla.Lines[scintilla.LineFromPosition(scintilla.GetEndStyled())].Position;
			var to = Math.Min(requestedEnd, snapshot.Text.Length);
			scintilla.StartStyling(from);
			var sink = new ScintillaSyntaxSink(scintilla, from);
			for (var i = snapshot.FirstSpan(from); i < snapshot.Spans.Count && snapshot.Spans[i].Start < to; i++)
			{
				var span = snapshot.Spans[i];
				sink.Style(Math.Max(from, span.Start), Math.Min(to, span.End), span.Color);
			}
			sink.Finish(to);
		}
	}
}
#endif
