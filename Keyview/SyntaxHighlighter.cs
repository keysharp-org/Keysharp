// Platform-neutral: the SAME tokenizer drives the Eto RichTextArea (Linux/macOS) and Scintilla
// (Windows) through ISyntaxSink, so both editors highlight identically and there is one set of rules.
namespace Keyview
{
	/// <summary>
	/// Minimal single-pass tokenizer that classifies spans and reports them to an <see cref="ISyntaxSink"/>.
	/// It recognizes comments, strings, numbers and two tiers of keywords. This is deliberately a
	/// "good enough" highlighter for the Keyview input (Keysharp) and output (generated C#) boxes
	/// rather than a full lexer, since the Eto text controls have no built-in lexing like ScintillaNET.
	/// </summary>
	internal sealed class SyntaxHighlighter
	{
		// Colors mirror the light theme used by the Windows/Scintilla styler.
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
		// tokenizing, to avoid freezing the UI thread (GTK tag application is costly on huge buffers).
		// The input box is debounced so it can afford a high limit; the output box is re-colored
		// synchronously right after a compile, so it uses a much smaller limit.
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
		private readonly string lineComment;
		private readonly char escapeChar;
		private readonly bool semicolonCommentRule;
		private readonly bool singleQuoteStrings;
		private readonly bool memberAccess;
		private readonly bool hotkeys;

		private SyntaxHighlighter(HashSet<string> keywords, HashSet<string> builtins, string lineComment,
			char escapeChar, bool semicolonCommentRule, bool singleQuoteStrings, bool memberAccess, bool hotkeys,
			int maxHighlightLength)
		{
			this.keywords = keywords;
			this.builtins = builtins;
			this.lineComment = lineComment;
			this.escapeChar = escapeChar;
			this.semicolonCommentRule = semicolonCommentRule;
			this.singleQuoteStrings = singleQuoteStrings;
			this.memberAccess = memberAccess;
			this.hotkeys = hotkeys;
			this.maxHighlightLength = maxHighlightLength;
		}

		/// <summary>Highlighter for the Keysharp/AHK input box.</summary>
		internal static SyntaxHighlighter ForKeysharp()
		{
			var keywords = ToSet("true false this thishotkey super unset isset " + Keywords.GetKeywords(), StringComparer.OrdinalIgnoreCase);
			var builtins = ToSet(Script.TheScript?.GetPublicStaticPropertyNames() ?? "", StringComparer.OrdinalIgnoreCase);
			// AHK uses backtick as the escape char, ';' line comments and supports both quote styles.
			return new SyntaxHighlighter(keywords, builtins, ";", '`', semicolonCommentRule: true, singleQuoteStrings: true,
				memberAccess: true, hotkeys: true, maxHighlightLength: 2_000_000);
		}

		/// <summary>Highlighter for the generated C# output box.</summary>
		internal static SyntaxHighlighter ForCSharp()
		{
			var keywords = ToSet(CSharpKeywords, StringComparer.Ordinal);
			// Member-access and hotkey highlighting are off: the generated code is dominated by
			// fully-qualified names (System.X.Y) where coloring every dotted segment just adds noise,
			// and "::" in C# is the namespace-alias qualifier (global::), not a hotkey.
			return new SyntaxHighlighter(keywords, new HashSet<string>(StringComparer.Ordinal), "//", '\\',
				semicolonCommentRule: false, singleQuoteStrings: true, memberAccess: false, hotkeys: false, maxHighlightLength: 2_000_000);
		}

		private static HashSet<string> ToSet(string words, StringComparer comparer) =>
			new (words.Split((char[])null, StringSplitOptions.RemoveEmptyEntries), comparer);

		/// <summary>Whether <paramref name="length"/> is small enough to tokenize rather than only reset.</summary>
		internal bool CanHighlight(int length) => length <= maxHighlightLength;

		/// <summary>
		/// Tokenizes <paramref name="text"/> and reports every colored span to <paramref name="sink"/>.
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
			HighlightSpan(sink, text, 0, n);

			pumpAction = null;
			pumpLength = null;
		}

		/// <summary>
		/// Tokenizes and colors [from, to). Split out so a region can be colored by a DIFFERENT highlighter than
		/// the one owning the buffer, which is how a `#CSharp` block gets C# rules.
		/// </summary>
		private void HighlightSpan(ISyntaxSink sink, string text, int from, int to)
		{
			var n = to;
			var i = from;
			var atLineStart = true;
			while (i < n)
			{
				if (aborted)
					break;

				var c = text[i];

				if (c == '\n')
				{
					atLineStart = true;
					i++;
					continue;
				}

				if (char.IsWhiteSpace(c))
				{
					i++;
					continue;
				}

				// A `#CSharp` block body is C#: under AHK rules `//` is not a comment, a `;` in a for-loop is, and
				// an apostrophe opens a string that swallows the rest of the block.
				if (hotkeys && atLineStart && IsCSharpBlockStart(text, i, n, out var bodyStart, out var bodyEnd, out var blockEnd))
				{
					ApplyColor(sink, i, bodyStart, KeywordColor);            // the `#CSharp [unsafe]` line
					CSharpRegion.HighlightSpanShared(this, sink, text, bodyStart, bodyEnd);
					ApplyColor(sink, bodyEnd, blockEnd, KeywordColor);       // the `#EndCSharp` line
					i = blockEnd;
					atLineStart = true;
					continue;
				}

				// Hotkey / hotstring / remap at the start of a line (e.g. "^!a::", "a::b", ":*:btw::").
				if (hotkeys && atLineStart)
				{
					var next = HighlightHotkeyLine(text, i, n, sink);
					if (next > i)
					{
						i = next;
						atLineStart = false;
						continue;
					}
				}

				atLineStart = false;

				// Block comment /* ... */
				if (c == '/' && i + 1 < n && text[i + 1] == '*')
				{
					var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
					var end = close < 0 ? n : close + 2;
					ApplyColor(sink, i, end, CommentColor);
					i = end;
					continue;
				}

				// Line comment (';' for Keysharp, '//' for C#)
				if (IsLineCommentAt(text, i))
				{
					var end = text.IndexOf('\n', i);
					if (end < 0)
						end = n;
					ApplyColor(sink, i, end, CommentColor);
					i = end;
					continue;
				}

				// String / character literal
				if (c == '"' || (singleQuoteStrings && c == '\''))
				{
					var end = ScanString(text, i, c);
					ApplyColor(sink, i, end, StringColor);
					i = end;
					continue;
				}

				// Number
				if (char.IsDigit(c))
				{
					var end = ScanNumber(text, i);
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
					var word = text.Substring(start, i - start);

					if (keywords.Contains(word))
						ApplyColor(sink, start, i, KeywordColor);
					else if (builtins.Contains(word))
						ApplyColor(sink, start, i, BuiltinColor);
					else if (IsFollowedByCall(text, i))
						ApplyColor(sink, start, i, MethodColor);   // foo(...) / obj.Method(...)
					else if (memberAccess && start > 0 && text[start - 1] == '.')
						ApplyColor(sink, start, i, PropertyColor); // obj.Property
					continue;
				}

				i++;
			}
		}

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
					instance.HighlightSpan(sink, text, from, to);
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

		/// <summary>
		/// Recognizes a `#CSharp` block opening at <paramref name="i"/>, reporting its body and end. The one-line
		/// `#CSharp "file.cs"` form has no body and is left to the AutoHotkey rules, as the lexer does.
		/// </summary>
		private static bool IsCSharpBlockStart(string text, int i, int n, out int bodyStart, out int bodyEnd, out int blockEnd)
		{
			bodyStart = bodyEnd = blockEnd = i;
			const string directive = "#csharp";

			if (i + directive.Length > n || string.Compare(text, i, directive, 0, directive.Length, StringComparison.OrdinalIgnoreCase) != 0)
				return false;

			// Must be the whole word: `#CSharpFoo` is not this directive.
			var after = i + directive.Length;

			if (after < n && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
				return false;

			var lineEnd = text.IndexOf('\n', i);
			if (lineEnd < 0 || lineEnd >= n) lineEnd = n;
			var firstLine = text.Substring(i, lineEnd - i);

			if (firstLine.Contains('"') || firstLine.Contains('\''))
				return false;   // the file form

			bodyStart = System.Math.Min(lineEnd + 1, n);
			var p = bodyStart;

			while (p < n)
			{
				var eol = text.IndexOf('\n', p);
				if (eol < 0 || eol > n) eol = n;
				var q = p;

				while (q < eol && (text[q] == ' ' || text[q] == '\t')) q++;

				if (q < eol && string.Compare(text, q, "#endcsharp", 0, 10, StringComparison.OrdinalIgnoreCase) == 0)
				{
					bodyEnd = p;
					blockEnd = System.Math.Min(eol + 1, n);
					return true;
				}

				p = System.Math.Min(eol + 1, n);
				if (eol >= n) break;
			}

			// Unterminated: color to the end rather than reverting the whole rest of the file to AHK rules, which
			// is what the user is looking at while still typing the block.
			bodyEnd = blockEnd = n;
			return true;
		}

		private bool IsLineCommentAt(string text, int i)
		{
			if (i + lineComment.Length > text.Length)
				return false;

			for (var k = 0; k < lineComment.Length; k++)
			{
				if (text[i + k] != lineComment[k])
					return false;
			}

			// In AHK a ';' only starts a comment at the start of a line or after whitespace.
			if (semicolonCommentRule && i > 0)
			{
				var prev = text[i - 1];
				return prev == '\n' || prev == '\r' || prev == ' ' || prev == '\t';
			}

			return true;
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

		/// <summary>
		/// Highlights a hotkey, hotstring or remap at the start of a line and returns the index from
		/// which normal tokenizing should resume, or <paramref name="start"/> if the line is not such
		/// a definition. The source key (and a remap's target key) are colored red-ish while "::" is
		/// left at the default color; a code action such as "^!a::Run("x")" is tokenized normally.
		/// </summary>
		private int HighlightHotkeyLine(string text, int start, int n, ISyntaxSink sink)
		{
			var lineEnd = text.IndexOf('\n', start);
			if (lineEnd < 0)
				lineEnd = n;

			// Hotstring:  :options:abbreviation::[replacement]
			if (text[start] == ':')
			{
				var optionsEnd = IndexOfOnLine(text, ':', start + 1, lineEnd);
				if (optionsEnd < 0)
					return start;

				var dbl = IndexOfDoubleColon(text, optionsEnd + 1, lineEnd);
				if (dbl < 0)
					return start;

				ApplyColor(sink, optionsEnd + 1, dbl, KeyColor); // abbreviation; colons stay default
				return dbl + 2;                                    // replacement tokenizes normally
			}

			// Hotkey / remap:  <source key>::<action or target key>
			var sep = FindHotkeySeparator(text, start, lineEnd);
			if (sep < 0)
				return start;

			ApplyColor(sink, start, sep, KeyColor); // source key & modifiers; "::" stays default

			var afterSep = sep + 2;

			// A bare key (optionally with modifiers) after "::" is a remap target -> color it too.
			// A name that is a known function/keyword (e.g. "^a::Reload") is treated as a code action.
			if (TryReadKeySpec(text, afterSep, lineEnd, out var keyStart, out var nameStart, out var keyEnd))
			{
				var name = text.Substring(nameStart, keyEnd - nameStart);
				if (nameStart > keyStart || (!keywords.Contains(name) && !builtins.Contains(name)))
				{
					ApplyColor(sink, keyStart, keyEnd, KeyColor);
					return keyEnd; // resume after the key (handles any trailing comment)
				}
			}

			return afterSep; // tokenize the code action normally
		}

		// Scans for the "::" separator of a hotkey/remap, returning the index of its first ':' or -1.
		// Bails if a string or comment is seen first, which means the line is an expression, not a hotkey.
		private static int FindHotkeySeparator(string text, int start, int lineEnd)
		{
			for (var p = start; p + 1 < lineEnd; p++)
			{
				var ch = text[p];

				if (ch == '"' || ch == '\'')
					return -1;
				if (ch == ';' && (p == start || text[p - 1] == ' ' || text[p - 1] == '\t'))
					return -1;

				if (ch == ':' && text[p + 1] == ':')
					return p > start ? p : -1; // require a non-empty source key
			}

			return -1;
		}

		// Reads "<modifiers><name>" occupying the rest of the line (only whitespace/comment may follow).
		// Used to recognize a remap target key. Returns false when other code follows the name.
		private static bool TryReadKeySpec(string text, int from, int lineEnd, out int keyStart, out int nameStart, out int keyEnd)
		{
			var p = from;
			while (p < lineEnd && (text[p] == ' ' || text[p] == '\t'))
				p++;

			keyStart = p;
			while (p < lineEnd && "^!+#<>*~$&".IndexOf(text[p]) >= 0)
				p++;

			nameStart = p;
			while (p < lineEnd && IsIdentChar(text[p]))
				p++;

			keyEnd = p;
			if (p == nameStart)
				return false; // no key name

			while (p < lineEnd && (text[p] == ' ' || text[p] == '\t'))
				p++;

			return p >= lineEnd || text[p] == ';'; // nothing but whitespace/comment may follow
		}

		private static int IndexOfOnLine(string text, char ch, int from, int lineEnd)
		{
			for (var p = from; p < lineEnd; p++)
			{
				if (text[p] == ch)
					return p;
			}

			return -1;
		}

		private static int IndexOfDoubleColon(string text, int from, int lineEnd)
		{
			for (var p = from; p + 1 < lineEnd; p++)
			{
				if (text[p] == ':' && text[p + 1] == ':')
					return p;
			}

			return -1;
		}

		// True when the next non-space char after an identifier is '(', i.e. it's being called.
		private static bool IsFollowedByCall(string text, int i)
		{
			while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
				i++;
			return i < text.Length && text[i] == '(';
		}

		private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

		private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
	}
}

