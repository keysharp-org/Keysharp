namespace Keysharp.Parsing.Lexing
{
	/// <summary>
	/// A single lexical token. Lightweight value type; <see cref="Text"/> is the raw source
	/// slice (string literals keep their quotes/escapes, numbers keep their raw form — decoding
	/// happens later during AST construction).
	/// </summary>
	internal readonly struct Token
	{
		public readonly TokenKind Kind;
		public readonly string Text;
		public readonly int Line;
		public readonly int Column;
		public readonly int Offset;
		public readonly int Length;

		/// <summary>
		/// True when whitespace, a comment, or a line break immediately preceded this token.
		/// Lets the parser make adjacency decisions (e.g. <c>Foo()</c> call vs <c>Foo ()</c>)
		/// without whitespace being a token in the stream.
		/// </summary>
		public readonly bool LeadingWhitespace;

		/// <summary>Source file this token came from (an <c>#include</c>d file); null for the main script. The
		/// <see cref="Line"/>/<see cref="Column"/> are relative to that file, so diagnostics can be reported per file.</summary>
		public readonly string File;

		public Token(TokenKind kind, string text, int line, int column, int offset, int length, bool leadingWhitespace,
			string file = null)
		{
			Kind = kind;
			Text = text;
			Line = line;
			Column = column;
			Offset = offset;
			Length = length;
			LeadingWhitespace = leadingWhitespace;
			File = file;
		}

		public bool Is(TokenKind kind) => Kind == kind;

		/// <summary>Case-insensitive match of the token text (used for contextual keywords).</summary>
		public bool IsKeyword(string word) =>
			Kind == TokenKind.Identifier && string.Equals(Text, word, System.StringComparison.OrdinalIgnoreCase);

		public override string ToString() =>
			Kind switch
			{
				TokenKind.Newline => $"[Newline @{Line}:{Column}]",
				TokenKind.EOF => $"[EOF @{Line}:{Column}]",
				_ => $"[{Kind} '{Text}' @{Line}:{Column}]"
			};
	}
}
