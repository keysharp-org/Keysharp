namespace Keyview
{
	/// <summary>
	/// The token categories <see cref="SyntaxHighlighter"/> recognizes. A category rather than a color, since Eto
	/// takes RGB directly while Scintilla takes a style index configured up front.
	/// </summary>
	internal enum SyntaxColor
	{
		Default = 0,
		Comment,
		String,
		Number,
		Keyword,
		Builtin,
		Method,     // foo(...) / obj.Method(...)
		Property,   // obj.Property
		Key,        // hotkey/remap source & target keys ("::" itself stays default)
	}

	/// <summary>Where a tokenizer pass writes its results; the editor back end owns everything else.</summary>
	internal interface ISyntaxSink
	{
		/// <summary>Colors <c>[start, endExclusive)</c> as <paramref name="color"/>.</summary>
		void Style(int start, int endExclusive, SyntaxColor color);
	}
}
