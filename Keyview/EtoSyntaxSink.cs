#if !WINDOWS
namespace Keyview
{
	/// <summary>
	/// Applies <see cref="SyntaxHighlighter"/> results to an Eto <see cref="RichTextArea"/>.
	/// The tokenizer itself is platform-neutral; this is only the "what color is that category" half.
	/// </summary>
	internal sealed class EtoSyntaxSink : ISyntaxSink
	{
		private readonly ITextBuffer buffer;
		private readonly bool isDark;

		internal EtoSyntaxSink(ITextBuffer buffer, bool isDark)
		{
			this.buffer = buffer;
			this.isDark = isDark;
		}

		public void Style(int start, int endExclusive, SyntaxColor color) =>
			buffer.SetForeground(new Range<int>(start, endExclusive - 1), SyntaxPalette.ToColor(color, isDark));
	}

	internal static class EtoHighlightExtensions
	{
		/// <summary>
		/// Re-colors the entire contents of <paramref name="area"/>. Stale colors from a previous pass are cleared
		/// by first resetting the whole buffer to the control's default text color.
		/// </summary>
		internal static void Highlight(this SyntaxHighlighter highlighter, RichTextArea area, Action pump = null)
		{
			var text = area.Text ?? "";
			var n = text.Length;

			if (n == 0)
				return;

			var buffer = area.Buffer;
			buffer.SetForeground(new Range<int>(0, n - 1), area.TextColor);

			if (!highlighter.CanHighlight(n))
				return;

			highlighter.Highlight(new EtoSyntaxSink(buffer, SyntaxPalette.IsDark), text, pump, () => area.TextLength);
		}
	}
}
#endif
