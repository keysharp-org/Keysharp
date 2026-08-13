// Platform-neutral: the SAME highlighter drives the Eto RichTextArea (Linux/macOS) and Scintilla
// (Windows) through ISyntaxSink, so both editors highlight identically and there is one set of rules.
namespace Keyview
{
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

		// Cooperative yielding state for a single Highlight() call: when pumpAction is set, ApplyColor
		// periodically runs it (a UI event-loop pump) so a large highlight stays responsive, and aborts
		// if the buffer is edited while yielded (which would make the remaining offsets stale).
		private const int PumpEvery = 512;
		private Action pumpAction;
		private Func<int> pumpLength;
		private int pumpSnapshotLength;
		private int applyCount;
		private bool aborted;

		private readonly HashSet<string> keywords;
		private readonly HashSet<string> builtins;

		// Explicit rather than inferred from `tokenizer != null`: with no parser component the Keysharp box must stay
		// uncolored, NOT fall through to C# rules and mis-highlight AutoHotkey source.
		private readonly bool csharpMode;

		private readonly IScriptTokenizer tokenizer;   // Keysharp box only, and only if the component resolved

		// Scintilla raises StyleNeeded per paint region and Restyle always classifies the whole document, so without
		// this the buffer is re-tokenized on events that changed nothing.
		private string cachedText;
		private IReadOnlyList<ScriptToken> cachedTokens;

		// C#-scanner mode only.
		private readonly char escapeChar;

		private SyntaxHighlighter(HashSet<string> keywords, HashSet<string> builtins, IScriptTokenizer tokenizer,
			bool csharpMode, char escapeChar, int maxHighlightLength)
		{
			this.keywords = keywords;
			this.builtins = builtins;
			this.tokenizer = tokenizer;
			this.csharpMode = csharpMode;
			this.escapeChar = escapeChar;
			this.maxHighlightLength = maxHighlightLength;
		}

		/// <summary>Highlighter for the Keysharp/AHK input box; colors nothing if the parser component is missing.</summary>
		internal static SyntaxHighlighter ForKeysharp()
		{
			var keywords = ToSet("true false this thishotkey super unset isset " + Keywords.GetKeywords(), StringComparer.OrdinalIgnoreCase);
			var builtins = ToSet(Script.TheScript?.GetPublicStaticPropertyNames() ?? "", StringComparer.OrdinalIgnoreCase);
			_ = ScriptingComponentRegistry.TryGetTokenizer(out var tokenizer, out _);
			return new SyntaxHighlighter(keywords, builtins, tokenizer, csharpMode: false, '`', maxHighlightLength: 2_000_000);
		}

		/// <summary>Highlighter for the generated C# output box, and for `#CSharp` block bodies.</summary>
		internal static SyntaxHighlighter ForCSharp()
		{
			var keywords = ToSet(CSharpKeywords, StringComparer.Ordinal);
			return new SyntaxHighlighter(keywords, new HashSet<string>(StringComparer.Ordinal), null, csharpMode: true,
				'\\', maxHighlightLength: 2_000_000);
		}

		private static HashSet<string> ToSet(string words, StringComparer comparer) =>
			new (words.Split((char[])null, StringSplitOptions.RemoveEmptyEntries), comparer);

		/// <summary>Whether <paramref name="length"/> is small enough to classify rather than only reset.</summary>
		internal bool CanHighlight(int length) => length <= maxHighlightLength;

		/// <summary>
		/// Classifies <paramref name="text"/> and reports every colored span to <paramref name="sink"/>.
		/// <para><paramref name="pump"/>, when supplied, is run periodically so a large buffer stays responsive;
		/// <paramref name="currentLength"/> is then consulted after each pump, and the pass aborts if the text
		/// changed underneath it (which would make every remaining offset stale). A back end with no event loop
		/// to pump &mdash; Scintilla styles synchronously in small ranges &mdash; passes neither.</para>
		/// </summary>
		internal void Highlight(ISyntaxSink sink, string text, Action pump = null, Func<int> currentLength = null)
		{
			var n = (text ?? "").Length;

			if (n == 0 || !CanHighlight(n))
				return;

			pumpAction = pump;
			pumpLength = currentLength;
			pumpSnapshotLength = n;
			applyCount = 0;
			aborted = false;

			if (csharpMode)
				HighlightCSharpSpan(sink, text, 0, n);
			else if (tokenizer != null)
				HighlightTokens(sink, text);
			// else: no parser component, so no Keysharp rules to color by — leave the buffer at its default color.

			pumpAction = null;
			pumpLength = null;
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

			for (var i = 0; i < tokens.Count && !aborted; i++)
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
					CSharpRegion.HighlightSpanShared(this, sink, text, t.Offset, t.Offset + t.Length);
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

		/// <summary>
		/// The C# highlighter for `#CSharp` regions. One shared instance (it holds only immutable keyword sets);
		/// the pump/abort state is copied across so a long block still yields and still aborts on an edit.
		/// </summary>
		private static class CSharpRegion
		{
			private static readonly SyntaxHighlighter instance = ForCSharp();

			internal static void HighlightSpanShared(SyntaxHighlighter owner, ISyntaxSink sink, string text, int from, int to)
			{
				instance.pumpAction = owner.pumpAction;
				instance.pumpLength = owner.pumpLength;
				instance.pumpSnapshotLength = owner.pumpSnapshotLength;
				instance.applyCount = owner.applyCount;
				instance.aborted = owner.aborted;

				try
				{
					instance.HighlightCSharpSpan(sink, text, from, to);
				}
				finally
				{
					owner.applyCount = instance.applyCount;
					owner.aborted = instance.aborted;   // an edit during the region must stop the outer pass too
					instance.pumpAction = null;
					instance.pumpLength = null;
				}
			}
		}

		/// <summary>Hand-written C# scanner for [from, to): the generated-code box and `#CSharp` block bodies.</summary>
		private void HighlightCSharpSpan(ISyntaxSink sink, string text, int from, int to)
		{
			var n = to;
			var i = from;

			while (i < n)
			{
				if (aborted)
					break;

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
				if (c == '"' || c == '\'')
				{
					var end = System.Math.Min(ScanString(text, i, c), n);
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

					if (keywords.Contains(text.Substring(start, i - start)))
						ApplyColor(sink, start, i, KeywordColor);

					continue;
				}

				i++;
			}
		}

		private int ScanString(string text, int start, char quote)
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

		private void ApplyColor(ISyntaxSink sink, int start, int endExclusive, SyntaxColor color)
		{
			if (aborted || endExclusive <= start)
				return;

			sink.Style(start, endExclusive, color);

			if (pumpAction != null && ++applyCount >= PumpEvery)
			{
				applyCount = 0;
				pumpAction();

				// An edit slipped in while we yielded, so the remaining token offsets are stale; stop.
				if (pumpLength != null && pumpLength() != pumpSnapshotLength)
					aborted = true;
			}
		}

		private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

		private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
	}
}
