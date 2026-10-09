// Platform-neutral: the SAME highlighter drives the Eto RichTextArea (Linux/macOS) and Scintilla
// (Windows) through ISyntaxSink, so both editors highlight identically and there is one set of rules.
namespace Keyview;

/// <summary>
/// Classifies spans of an editor buffer and reports them to an <see cref="ISyntaxSink"/>.
/// <para>The Keysharp box is colored from the shared lexer via <see cref="IScriptTokenizer"/>.
/// What remains is what the lexer deliberately does not decide: which identifiers are 
/// keywords, built-ins, calls or properties.</para>
/// <para>The C# box keeps a hand-written scanner — there is no C# tokenizer on hand — which also colors
/// <c>#CSharp</c> block bodies, handed over whole by the lexer.</para>
/// </summary>
internal sealed class SyntaxHighlighter
{
	private const SyntaxColor CommentColor = SyntaxColor.Comment;
	private const SyntaxColor StringColor = SyntaxColor.String;
	private const SyntaxColor NumberColor = SyntaxColor.Number;
	private const SyntaxColor KeywordColor = SyntaxColor.Keyword;
	private const SyntaxColor BuiltinColor = SyntaxColor.Builtin;
	private const SyntaxColor MethodColor = SyntaxColor.Method;
	private const SyntaxColor PropertyColor = SyntaxColor.Property;
	private const SyntaxColor KeyColor = SyntaxColor.Key;

	// C# keyword list (kept in sync with CSharpStyler used on Windows). The huge "all exported
	// type names" tier is intentionally omitted here to keep highlighting cheap and simple.
	private const string CSharpKeywords =
		"abstract partial as base break case catch checked continue default delegate do else event " +
		"explicit extern false finally fixed for foreach goto if implicit in interface internal is lock " +
		"namespace new null operator out override params private protected public readonly ref return " +
		"sealed sizeof stackalloc switch this throw true try typeof unchecked unsafe using virtual while " +
		"volatile yield var async await object bool byte char class const decimal double enum float int " +
		"long sbyte short static string struct uint ulong ushort void dynamic";

	// Above this many characters, Highlight only resets to the default color instead of
	// classifying, to avoid freezing the UI thread (GTK tag application is costly on huge buffers).
	private readonly int maxHighlightLength;

	private readonly HashSet<string> keywords;
	private readonly HashSet<string> builtins;

	// Without the parser component, the Keysharp editor stays uncolored.
	private readonly bool csharpMode;

	private readonly IScriptTokenizer tokenizer;   // Keysharp box only, and only if the component resolved

	// Reuse tokens when an unchanged buffer is reclassified, such as on a theme change.
	private string cachedText;
	private IReadOnlyList<ScriptToken> cachedTokens;

	private static readonly HashSet<string> csharpKeywords = ToSet(CSharpKeywords, StringComparer.Ordinal);

	private SyntaxHighlighter(HashSet<string> keywords, HashSet<string> builtins, IScriptTokenizer tokenizer,
		bool csharpMode, int maxHighlightLength)
	{
		this.keywords = keywords;
		this.builtins = builtins;
		this.tokenizer = tokenizer;
		this.csharpMode = csharpMode;
		this.maxHighlightLength = maxHighlightLength;
	}

	/// <summary>Highlighter for the Keysharp/AHK input box; colors nothing if the parser component is missing.</summary>
	internal static SyntaxHighlighter ForKeysharp()
	{
		var keywords = ToSet("true false this thishotkey super unset isset " + Keywords.GetKeywords(), StringComparer.OrdinalIgnoreCase);
		var builtins = ToSet(Script.TheScript?.GetPublicStaticPropertyNames() ?? "", StringComparer.OrdinalIgnoreCase);
		_ = ScriptingComponentRegistry.TryGetTokenizer(out var tokenizer, out _);
		return new SyntaxHighlighter(keywords, builtins, tokenizer, csharpMode: false, maxHighlightLength: 2_000_000);
	}

	/// <summary>Highlighter for the generated C# output box, and for `#CSharp` block bodies.</summary>
	internal static SyntaxHighlighter ForCSharp()
	{
		var keywords = ToSet(CSharpKeywords, StringComparer.Ordinal);
		return new SyntaxHighlighter(keywords, new HashSet<string>(StringComparer.Ordinal), null, csharpMode: true, maxHighlightLength: 2_000_000);
	}

	private static HashSet<string> ToSet(string words, StringComparer comparer) =>
		new (words.Split((char[])null, StringSplitOptions.RemoveEmptyEntries), comparer);

	/// <summary>Whether <paramref name="length"/> is small enough to classify rather than only reset.</summary>
	internal bool CanHighlight(int length) => length <= maxHighlightLength;

	/// <summary>Reports colored spans in source order.</summary>
	internal void Highlight(ISyntaxSink sink, string text)
	{
		var n = (text ?? "").Length;
		if (n == 0 || !CanHighlight(n)) return;
		if (csharpMode) HighlightCSharpSpan(sink, text, 0, n);
		else if (tokenizer != null) HighlightTokens(sink, text);
	}

	/// <summary>Tokenizes, reusing the previous result when the text has not changed.</summary>
	private IReadOnlyList<ScriptToken> Tokenize(string text)
	{
		if (cachedTokens != null && (ReferenceEquals(cachedText, text) || string.Equals(cachedText, text, StringComparison.Ordinal)))
			return cachedTokens;

		cachedTokens = tokenizer.Tokenize(text);
		cachedText = text;
		return cachedTokens;
	}

	/// <summary>Colors the Keysharp buffer by token kind; only the identifier tiers are decided here.</summary>
	private void HighlightTokens(ISyntaxSink sink, string text)
	{
		var tokens = Tokenize(text);
		var keywordLookup = keywords.GetAlternateLookup<ReadOnlySpan<char>>();
		var builtinLookup = builtins.GetAlternateLookup<ReadOnlySpan<char>>();

		for (var i = 0; i < tokens.Count; i++)
		{
			var t = tokens[i];

			if (t.Length <= 0 || t.Offset + t.Length > text.Length)
				continue;

			var color = t.Kind switch
			{
				ScriptTokenKind.Comment => CommentColor,
				ScriptTokenKind.Directive => KeywordColor,
				ScriptTokenKind.String or ScriptTokenKind.HotstringExpansion => StringColor,
				ScriptTokenKind.Number => NumberColor,
				ScriptTokenKind.HotkeyTrigger or ScriptTokenKind.RemapSourceKey or ScriptTokenKind.RemapTargetKey
					or ScriptTokenKind.HotstringTrigger => KeyColor,
				// `#Include`, `#Requires`, … — the '#' and the name after it.
				ScriptTokenKind.Hash => Follows(tokens, i, ScriptTokenKind.Identifier) ? KeywordColor : SyntaxColor.Default,
				ScriptTokenKind.Identifier => IdentifierColor(tokens, i, text, keywordLookup, builtinLookup),
				_ => SyntaxColor.Default,
			};

			if (t.Kind == ScriptTokenKind.CSharpBlock)
			{
				HighlightCSharpSpan(sink, text, t.Offset, t.Offset + t.Length);
				continue;
			}

			if (color != SyntaxColor.Default)
				ApplyColor(sink, t.Offset, t.Offset + t.Length, color);
		}
	}

	private static SyntaxColor IdentifierColor(IReadOnlyList<ScriptToken> tokens, int i, string text,
		HashSet<string>.AlternateLookup<ReadOnlySpan<char>> keywordLookup,
		HashSet<string>.AlternateLookup<ReadOnlySpan<char>> builtinLookup)
	{
		var word = tokens[i].Text(text);

		if (Preceded(tokens, i, ScriptTokenKind.Hash))
			return KeywordColor;   // the name of a directive

		if (keywordLookup.Contains(word))
			return KeywordColor;

		if (builtinLookup.Contains(word))
			return BuiltinColor;

		if (Follows(tokens, i, ScriptTokenKind.LParen))
			return MethodColor;    // foo(...) / obj.Method(...)

		if (Preceded(tokens, i, ScriptTokenKind.Dot))
			return PropertyColor;  // obj.Property

		return SyntaxColor.Default;
	}

	private static bool Follows(IReadOnlyList<ScriptToken> tokens, int i, ScriptTokenKind kind) =>
		i + 1 < tokens.Count && tokens[i + 1].Kind == kind;

	private static bool Preceded(IReadOnlyList<ScriptToken> tokens, int i, ScriptTokenKind kind) =>
		i > 0 && tokens[i - 1].Kind == kind;

	/// <summary>Hand-written C# scanner for [from, to): the generated-code box and `#CSharp` block bodies.</summary>
	private void HighlightCSharpSpan(ISyntaxSink sink, string text, int from, int to)
	{
		var n = to;
		var i = from;
		var keywordLookup = csharpKeywords.GetAlternateLookup<ReadOnlySpan<char>>();

		while (i < n)
		{
			var c = text[i];

			if (char.IsWhiteSpace(c))
			{
				i++;
				continue;
			}

			// Block comment /* ... */
			if (c == '/' && i + 1 < n && text[i + 1] == '*')
			{
				var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
				var end = close < 0 || close + 2 > n ? n : close + 2;
				ApplyColor(sink, i, end, CommentColor);
				i = end;
				continue;
			}

			// Line comment
			if (c == '/' && i + 1 < n && text[i + 1] == '/')
			{
				var end = text.IndexOf('\n', i);
				if (end < 0 || end > n)
					end = n;
				ApplyColor(sink, i, end, CommentColor);
				i = end;
				continue;
			}

			// String / character literal
			if (c is '"' or '\'')
			{
				var end = System.Math.Min(ScanString(text, i, c, '\\'), n);
				ApplyColor(sink, i, end, StringColor);
				i = end;
				continue;
			}

			// Number
			if (char.IsDigit(c))
			{
				var end = System.Math.Min(ScanNumber(text, i), n);
				ApplyColor(sink, i, end, NumberColor);
				i = end;
				continue;
			}

			// Identifier / keyword
			if (IsIdentStart(c))
			{
				var start = i++;
				while (i < n && IsIdentChar(text[i]))
					i++;

				if (keywordLookup.Contains(text.AsSpan(start, i - start)))
					ApplyColor(sink, start, i, KeywordColor);

				continue;
			}

			i++;
		}
	}

	private static int ScanString(string text, int start, char quote, char escapeChar)
	{
		var n = text.Length;
		var i = start + 1;

		while (i < n)
		{
			var c = text[i];

			if (c == escapeChar && i + 1 < n)
			{
				i += 2;
				continue;
			}

			if (c == quote)
			{
				// Doubled quote ("" or '') is an escaped literal quote, not a terminator.
				if (i + 1 < n && text[i + 1] == quote)
				{
					i += 2;
					continue;
				}

				return i + 1; // include the closing quote
			}

			if (c == '\n') // unterminated literal stops at the line break
				return i;

			i++;
		}

		return n;
	}

	private static int ScanNumber(string text, int start)
	{
		var n = text.Length;
		var i = start;

		if (text[i] == '0' && i + 1 < n && (text[i + 1] == 'x' || text[i + 1] == 'X'))
		{
			i += 2;
			while (i < n && Uri.IsHexDigit(text[i]))
				i++;
			return i;
		}

		while (i < n && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == '_'))
			i++;

		if (i < n && (text[i] == 'e' || text[i] == 'E'))
		{
			i++;
			if (i < n && (text[i] == '+' || text[i] == '-'))
				i++;
			while (i < n && char.IsDigit(text[i]))
				i++;
		}

		while (i < n && "fFdDmMlLuU".IndexOf(text[i]) >= 0)
			i++;

		return i;
	}

	private static void ApplyColor(ISyntaxSink sink, int start, int endExclusive, SyntaxColor color)
	{
		if (endExclusive > start) sink.Style(start, endExclusive, color);
	}

	private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

	private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
