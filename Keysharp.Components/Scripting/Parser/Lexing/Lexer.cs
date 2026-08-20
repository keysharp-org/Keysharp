using System.Collections.Generic;

namespace Keysharp.Parsing.Lexing
{
	/// <summary>
	/// A hand-written single-pass scanner for AutoHotkey v2(.1) source.
	///
	/// LOSSLESS: every non-whitespace character falls inside exactly one token, and tokens never overlap — so a
	/// consumer can classify a whole file from them (see IScriptTokenizer). When adding a scanner, keep it that way:
	/// if a branch consumes characters it must emit a token covering them, using a trivia kind
	/// (<see cref="TokenKind.Comment"/>/<see cref="TokenKind.Directive"/>/<see cref="TokenKind.ContinuationDelimiter"/>)
	/// for anything the parser should not see. Parser.LexForParsing drops those.
	///
	/// Scope of this increment: the DEFAULT lexing mode — whitespace/newlines, line and block
	/// comments, numbers (decimal/float/exponent, 0x/0o/0b, bigint <c>n</c> suffix), single- and
	/// double-quoted strings with backtick escapes, identifiers, and the full operator set.
	///
	/// Deliberately NOT handled here (resolved in the parser, or in later increments):
	///   - keyword recognition (every word is an Identifier),
	///   - deref <c>%...%</c>, object-literal-vs-block, maybe-operator (parser, by context),
	///   - continuation sections, hotkeys/hotstrings, directives/preprocessing.
	/// </summary>
	internal sealed class Lexer
	{
		private readonly string _s;
		private readonly int _n;
		private int _pos;
		private int _line = 1;
		private int _col = 1;

		/// <summary>Lexing diagnostics (e.g. unterminated string literals), as "line:col: message".</summary>
		public readonly List<string> Diagnostics = new();

		// Default hotstring execute mode, toggled by `#Hotstring X` / `#Hotstring X0`. In execute mode a hotstring's
		// replacement is treated as code (a function body) rather than literal text.
		private bool _hsExecuteDefault;

		// When a string token is a continuation section, ScanString stores the equivalent escaped double-quoted
		// literal here (so the existing DecodeString reproduces the joined text); the Tokenize loop uses it as the
		// token text instead of the raw source span.
		private string _strOverride;

		// Source file these tokens belong to; null when the caller did not supply a path. Stamped onto every token so
		// diagnostics can be reported per file without a post-pass.
		private readonly string _file;

		// Set on the nested lexer that reads a code continuation section's merged text. That text is the interior of
		// ONE logical line, so the line breaks the Join left in it are whitespace rather than statement separators,
		// nothing in it starts a line (no hotkeys, directives or further sections), and a quoted string runs straight
		// through a break — which is what lets a section split a name, an operator or a string across its lines.
		private readonly bool _mergedLine;

		public Lexer(string source, string file = null) : this(source, file, mergedLine: false) { }

		private Lexer(string source, string file, bool mergedLine)
		{
			_s = source ?? "";
			_n = _s.Length;
			_file = file;
			_mergedLine = mergedLine;
		}

		public static List<Token> Tokenize(string source) => new Lexer(source).Tokenize();

		/// <summary>Creates a token tagged with this lexer's source file.</summary>
		private Token Tok(TokenKind kind, string text, int line, int col, int offset, int length, bool leadingWs) =>
			new(kind, text, line, col, offset, length, leadingWs, _file);

		/// <summary>Emits <c>[from, to)</c> as a comment token, for a comment a scanner consumed itself.</summary>
		private void AddTrivia(List<Token> tokens, int from, int to, int line, int col)
		{
			// Callers pass everything up to the line break, which with no comment present is just a CRLF '\r'.
			while (from < to && (_s[to - 1] == '\r' || _s[to - 1] == ' ' || _s[to - 1] == '\t')) to--;

			while (from < to && (_s[from] == ' ' || _s[from] == '\t')) { from++; col++; }

			if (to <= from)
				return;

			tokens.Add(Tok(TokenKind.Comment, null, line, col, from, to - from, true));
		}

		private char Cur => _pos < _n ? _s[_pos] : '\0';
		private char At(int k) => _pos + k < _n ? _s[_pos + k] : '\0';

		private void Advance(int count = 1)
		{
			for (var i = 0; i < count && _pos < _n; i++)
			{
				if (_s[_pos] == '\n') { _line++; _col = 1; }
				else _col++;
				_pos++;
			}
		}

		private static bool IsDigit(char c) => c >= '0' && c <= '9';
		private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
		// AHK identifier rules (docs "Names"): an identifier is made of ASCII letters, digits, underscore and ANY
		// non-ASCII character (code >= 0x80) — the latter includes UTF-16 surrogate halves, so supplementary-plane
		// characters such as emoji are valid. Only digits are disallowed as the FIRST character (a leading digit is a
		// number). `c >= 0x80` (not char.IsLetter) is what AHK uses, so non-ASCII symbols (€, ①, …) are allowed too.
		private static bool IsIdentStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c >= (char)0x80;
		private static bool IsIdentPart(char c) => IsIdentStart(c) || (c >= '0' && c <= '9');
		private static bool IsInlineWhitespace(char c) =>
			c == ' ' || c == '\t' || c == '\r' || c == '\f' || c == '\v' || c == ' ';

		public List<Token> Tokenize()
		{
			var tokens = new List<Token>(_n / 4 + 16);
			var leadingWs = true;   // start-of-file counts as preceded by whitespace

			// A file (or an #Include) may open with a continuation section, which then merges onto the empty line above
			// it. Every other one is found at the line break that precedes it.
			if (!_mergedLine)
				_ = TryMergeCodeSection(tokens, ref leadingWs);

			while (_pos < _n)
			{
				var c = _s[_pos];

				if (IsInlineWhitespace(c))
				{
					Advance();
					leadingWs = true;
					continue;
				}

				// Newline: collapse a run of line breaks (and surrounding blank-line whitespace) into one token.
				if (c == '\n')
				{
					int sl = _line, sc = _col, so = _pos;

					while (_pos < _n && (_s[_pos] == '\n' || IsInlineWhitespace(_s[_pos])))
						Advance();

					// A merged line's own breaks are whitespace: the Join string put them there, and the text they sit
					// in is the interior of one logical line.
					if (_mergedLine) { leadingWs = true; continue; }

					// A '(' opener on a following line (after any blank/comment lines) does not end this line either —
					// it continues it, with the section's contents merged in as text.
					if (TryMergeCodeSection(tokens, ref leadingWs)) continue;

					tokens.Add(Tok(TokenKind.Newline, "\n", sl, sc, so, _pos - so, leadingWs));
					leadingWs = true;
					continue;
				}

				// Line comment: ';' is a comment only at line start or when preceded by whitespace.
				if (c == ';' && leadingWs)
				{
					int sl = _line, sc = _col, so = _pos;
					while (_pos < _n && _s[_pos] != '\n') Advance();
					tokens.Add(Tok(TokenKind.Comment, null, sl, sc, so, _pos - so, leadingWs));
					leadingWs = true;
					continue;
				}

				// Block comment '/* … */' (anywhere — '/*' is unambiguous vs '/'/'//'/'/='). A single-line block
				// comment is skipped like whitespace; a multi-line one acts as a line break (matches the canonical
				// lexer's EOL handling), so e.g. `55 /*…*/ + 2` rejoins via leading-operator continuation.
				if (c == '/' && At(1) == '*')
				{
					int cl = _line, cc = _col, co = _pos;
					Advance(2);
					while (_pos < _n && !(_s[_pos] == '*' && At(1) == '/')) Advance();
					if (_pos < _n) Advance(2);   // consume '*/'
					tokens.Add(Tok(TokenKind.Comment, null, cl, cc, co, _pos - co, leadingWs));
					// Spanned lines still act as a line break, but as an EMPTY token: the text is the comment's.
					if (!_mergedLine && _line > cl)
						tokens.Add(Tok(TokenKind.Newline, "\n", _line, _col, _pos, 0, true));
					leadingWs = true;
					continue;
				}

				// At the beginning of a logical line, a `::` (outside strings/comments) marks a hotkey, remap, or
				// hotstring definition. The trigger text is taken raw (it can contain operator characters).
				if (!_mergedLine && (tokens.Count == 0 || tokens[^1].Kind == TokenKind.Newline))
				{
					MaybeTrackHotstringDirective();   // `#Hotstring X` sets the default execute mode for later hotstrings
					// Capture C# before other line-start scanners can interpret its body.
					if (TryScanCSharpBlock(tokens, leadingWs)) { leadingWs = false; continue; }
					if (TryScanRawDirective(tokens, leadingWs)) { leadingWs = false; continue; }
					if (TryScanHot(tokens, leadingWs)) { leadingWs = false; continue; }
				}

				int tl = _line, tc = _col, to = _pos;
				TokenKind kind;

				// A leading '.' is only a number ('.5') at the start of an expression. After a value
				// (identifier, literal, or closing bracket) a '.' is the member/concat operator, and
				// whether it's member or concat is decided later by the parser from the adjacency flag.
				var prevIsValue = tokens.Count > 0 && tokens[^1].Kind is
					TokenKind.Identifier or TokenKind.Number or TokenKind.String or
					TokenKind.RParen or TokenKind.RBracket or TokenKind.RBrace;

				if (IsDigit(c) || (c == '.' && IsDigit(At(1)) && !prevIsValue))
				{
					ScanNumber();
					kind = TokenKind.Number;
				}
				else if (c == '"' || c == '\'')
				{
					ScanString();
					kind = TokenKind.String;
				}
				else if (IsIdentStart(c))
				{
					while (IsIdentPart(Cur)) Advance();
					kind = TokenKind.Identifier;
				}
				else
				{
					var (k, len) = ScanOperator();
					Advance(len);
					kind = k;
				}

				var text = _strOverride ?? _s.Substring(to, _pos - to);
				_strOverride = null;
				tokens.Add(Tok(kind, text, tl, tc, to, _pos - to, leadingWs));
				leadingWs = false;
			}

			tokens.Add(Tok(TokenKind.EOF, "", _line, _col, _pos, 0, leadingWs));
			return tokens;
		}

		private void ScanNumber()
		{
			var c = _s[_pos];

			if (c == '0' && (At(1) == 'x' || At(1) == 'X'))
			{
				Advance(2);
				while (IsHex(Cur) || Cur == '_') Advance();
			}
			else if (c == '0' && (At(1) == 'o' || At(1) == 'O'))
			{
				Advance(2);
				while ((Cur >= '0' && Cur <= '7') || Cur == '_') Advance();
			}
			else if (c == '0' && (At(1) == 'b' || At(1) == 'B'))
			{
				Advance(2);
				while (Cur == '0' || Cur == '1' || Cur == '_') Advance();
			}
			else
			{
				if (c == '.')
				{
					Advance();
					while (IsDigit(Cur) || Cur == '_') Advance();
				}
				else
				{
					while (IsDigit(Cur) || Cur == '_') Advance();
					if (Cur == '.' && IsDigit(At(1)))
					{
						Advance();
						while (IsDigit(Cur) || Cur == '_') Advance();
					}
				}

				if (Cur == 'e' || Cur == 'E')
				{
					var n1 = At(1);
					if (IsDigit(n1) || ((n1 == '+' || n1 == '-') && IsDigit(At(2))))
					{
						Advance();
						if (Cur == '+' || Cur == '-') Advance();
						while (IsDigit(Cur) || Cur == '_') Advance();
					}
				}
			}

			if (Cur == 'n') Advance();  // bigint suffix
		}

		private void ScanString()
		{
			var quote = _s[_pos];
			var startLine = _line; var startCol = _col;
			Advance(); // opening quote

			// Continuation section: the rest of the opening line leaves the quote unterminated (AHK strips a trailing
			// `;`-comment per physical line, ignoring quotes) and the next non-blank line opens with '('. Any text
			// between the opening quote and that comment/EOL is the section's prefix (RTrimmed); the text between '('
			// and the closing ')' is joined onto it. e.g.  Var := "text, ; cmt  ⏎  (  ⏎  more  ⏎  )"
			// A merged line has no line of its own to look ahead to — its sections are already merged.
			if (!_mergedLine)
			{
				int sp = _pos, sl = _line, sc = _col;
				int prefixEnd = _pos;            // index (exclusive) just past the last non-blank prefix char
				bool closed = false;             // a real closing quote appears on this line -> normal string
				char prev = ' ';
				while (_pos < _n && Cur != '\n' && Cur != '\r')
				{
					if (Cur == '`') { Advance(); if (_pos < _n && Cur != '\n' && Cur != '\r') Advance(); prefixEnd = _pos; prev = '`'; continue; } // escape: skip next char but never the line terminator (CRLF/LF parity)
					if (Cur == quote) { closed = true; break; }
					if (Cur == ';' && (prev == ' ' || prev == '\t')) break;   // a `;`-comment ends the visible content
					prev = Cur;
					Advance();
					if (prev != ' ' && prev != '\t') prefixEnd = _pos;        // RTrim: extend only past non-blank chars
				}
				if (!closed)
				{
					while (Cur != '\n' && _pos < _n) Advance();   // skip past comment to EOL
					if (Cur == '\n')
					{
						Advance();
						SkipBlankAndCommentLines();   // both are permitted between the opening line and the section's '('
						if (Cur == '(' && (_pos + 1 >= _n || _s[_pos + 1] != ':'))
						{
							string prefix = _s.Substring(sp, System.Math.Max(0, prefixEnd - sp));
							ScanContinuationSection(quote, startLine, startCol, prefix);
							return;
						}
					}
				}
				_pos = sp; _line = sl; _col = sc;   // not a section — scan as a normal string
			}

			while (_pos < _n)
			{
				var ch = _s[_pos];
				// AHK strings do not span raw line breaks — except in a merged line, where the break is Join text that
				// the section put INSIDE the quotes, so the string runs on to close on a later content line.
				if (!_mergedLine && (ch == '\n' || ch == '\r')) break;
				if (ch == '`') { Advance(); if (_pos < _n && (_mergedLine || (Cur != '\n' && Cur != '\r'))) Advance(); continue; } // backtick escape: skip next char but never the line terminator (CRLF/LF parity)
				if (ch == quote) { Advance(); return; } // closing quote
				Advance();
			}
			// Reached end of line / file without a closing quote.
			Diagnostics.Add($"{startLine}:{startCol}: unterminated string literal (missing closing {quote})");
		}

		// Reads an AHK continuation section. _pos is at the '(' that opens it. The raw section text (from '(' through
		// the closing ')') is handed to the shared Parser.MultilineString, which honors all options (Join, LTrim/
		// LTrim0, RTrim/RTrim0, Comments, `, %, indent-trim) and escapes the result for a double-quoted literal; the
		// result is wrapped in quotes and used as the token text so the parser's DecodeString reproduces it.
		private void ScanContinuationSection(char quote, int startLine, int startCol, string prefix = "")
		{
			var sb = new System.Text.StringBuilder("\"").Append(prefix);
			// Several continuation sections may attach to a single open string back-to-back; consume each (and any
			// literal trailing text after its ')') until the closing quote is found.
			while (true)
			{
				int codeStart = _pos;   // at '('
				while (_pos < _n)
				{
					int k = 0;
					while (_pos + k < _n && (_s[_pos + k] == ' ' || _s[_pos + k] == '\t')) k++;
					if (_pos + k < _n && _s[_pos + k] == ')')
					{
						for (int j = 0; j <= k; j++) Advance();   // consume leading ws + ')'
						break;
					}
					while (_pos < _n && Cur != '\n') Advance();    // skip to end of the content line
					if (_pos < _n) Advance(); else { Diagnostics.Add($"{startLine}:{startCol}: unterminated continuation section (missing closing ')')"); _strOverride = "\"\""; return; }
				}
				var code = _s.Substring(codeStart, _pos - codeStart);   // '(' line … ')' (MultilineString stops at ')')

				try { sb.Append(Keysharp.Parsing.Parser.MultilineString(code, startLine, "lexer")); }
				catch (System.Exception ex) { Diagnostics.Add($"{startLine}:{startCol}: {ex.Message}"); }

				// Any text between ')' and the closing quote (or next section) is appended literally (escaped).
				while (_pos < _n && Cur != quote && Cur != '\n' && Cur != '\r')
				{
					sb.Append(Cur switch { '`' => "``", '"' => "`\"", _ => Cur.ToString() });
					Advance();
				}
				if (Cur == quote) { Advance(); break; }   // closing quote — done

				// EOL without a close: if the next non-blank line opens another section, keep going.
				int sp = _pos, sl = _line, sc = _col;
				while (Cur == '\n' || Cur == '\r' || Cur == ' ' || Cur == '\t') Advance();
				if (Cur == '(' && (_pos + 1 >= _n || _s[_pos + 1] != ':')) continue;
				_pos = sp; _line = sl; _col = sc;
				Diagnostics.Add($"{startLine}:{startCol}: unterminated string literal (missing closing {quote})");
				break;
			}
			_strOverride = sb.Append('"').ToString();
		}

		/// <summary>Offset of the line break (or end of file) that ends the physical line containing <paramref name="p"/>.</summary>
		private int PhysicalLineEnd(int p)
		{
			while (p < _n && _s[p] != '\n' && _s[p] != '\r') p++;

			return p;
		}

		// Offset of the `;` that comments out the rest of this line, or lineEnd when there is none. A ';' is a comment
		// only at the start of the physical line or after whitespace. <paramref name="quoteBlind"/> takes one inside a
		// quoted string as well, which is what the Comments option asks for; otherwise the scan carries the quote state
		// the merge is already in, so a `;` written inside a string stays part of it.
		private int SectionCommentStart(int from, int lineEnd, char quote, bool quoteBlind)
		{
			var prev = from > 0 ? _s[from - 1] : ' ';

			for (var i = from; i < lineEnd; i++)
			{
				var ch = _s[i];

				if (!quoteBlind)
				{
					if (quote != '\0')
					{
						if (ch == '`') { i++; prev = '\0'; continue; }   // an escape: the next character cannot close the string

						if (ch == quote) quote = '\0';

						prev = ch;
						continue;
					}

					if (ch == '"' || ch == '\'') { quote = ch; prev = ch; continue; }
				}

				if (ch == ';' && (prev == ' ' || prev == '\t' || prev == '\n' || prev == '\r'))
					return i;

				prev = ch;
			}

			return lineEnd;
		}

		// Skips over blank lines, `;` lines and `/* … */` blocks, leaving _pos on the next line with real content. AHK
		// permits both between a line and a continuation section that continues it. Comments found are collected into
		// <paramref name="trivia"/>, which stays null (and unallocated) while there are none to collect.
		private void SkipBlankAndCommentLines(ref List<Token> trivia)
		{
			while (_pos < _n)
			{
				if (Cur == ' ' || Cur == '\t' || Cur == '\r' || Cur == '\n') { Advance(); continue; }

				int cs = _pos, cl = _line, cc = _col;

				if (Cur == ';')
					while (_pos < _n && Cur != '\n') Advance();
				else if (Cur == '/' && At(1) == '*')
				{
					Advance(2);
					while (_pos < _n && !(Cur == '*' && At(1) == '/')) Advance();
					if (_pos < _n) Advance(2);
				}
				else
					break;

				(trivia ??= []).Add(Tok(TokenKind.Comment, null, cl, cc, cs, _pos - cs, true));
			}
		}

		/// <summary>Overload for the callers that only need the skipping, not the comments.</summary>
		private void SkipBlankAndCommentLines()
		{
			List<Token> none = null;
			SkipBlankAndCommentLines(ref none);
		}

		// One run of merged text taken verbatim from a span of ONE physical source line, so a merged offset inside it
		// maps back to a file offset (and line/column) by arithmetic. Text the merge inserts itself — the Join string,
		// the space after a name character — comes from no line and gets no segment.
		private readonly struct MergeSegment
		{
			internal readonly int TextStart, SrcStart, Length, Line, Column;

			internal MergeSegment(int textStart, int srcStart, int length, int line, int column)
			{
				TextStart = textStart; SrcStart = srcStart; Length = length; Line = line; Column = column;
			}
		}

		// From just past a collapsed newline run: if the next line (after any blank/comment lines) opens a *code*
		// continuation section, merge the rest of the logical line the way AHK does — its content lines joined into one
		// string of TEXT — lex that, and splice the resulting tokens in. Returns false, having rewound, if there is no
		// section here.
		//
		// The merge cannot be faked by splicing each line's tokens together, because AHK's merge happens BEFORE
		// anything is parsed: a section can therefore split a name, an operator or a quoted string across lines
		// (`(Join` ⏎ `MyV` ⏎ `ar` is the one name `MyVar`), which no amount of token joining reproduces.
		private bool TryMergeCodeSection(List<Token> tokens, ref bool leadingWs)
		{
			int sp = _pos, sl = _line, sc = _col;
			// Held back rather than emitted: this runs at every line break, and rewinds when there is no section here.
			List<Token> trivia = null;

			SkipBlankAndCommentLines(ref trivia);

			if (!(Cur == '(' && IsCodeSectionOpener(_pos)))
			{
				_pos = sp; _line = sl; _col = sc;
				return false;
			}

			trivia ??= [];

			var text = new System.Text.StringBuilder();
			var segs = new List<MergeSegment>();
			// AHK appends the section to the line above as text, so the last token on that line can be extended by what
			// follows (`c :` ⏎ (Join) ⏎ `= 5` is `c := 5`). Pull it back into the merge to be re-lexed with them. A
			// comment ending that line is not in the way — AHK has already stripped it — but it has to move into the
			// trivia to stay in source order with the tokens the merge produces.
			var at = tokens.Count;

			while (at > 0 && tokens[at - 1].Kind == TokenKind.Comment) at--;

			var pull = at > 0 ? tokens[at - 1] : default;
			var pulled = at > 0 && CanMergeAcross(pull.Kind) && pull.Length > 0
						 && _s.AsSpan(pull.Offset, pull.Length).IndexOfAny('\n', '\r') < 0;
			// Without a token to pull there is still the line break between, so the merged text does not glue onto
			// whatever came before it.
			var firstLeadingWs = !pulled || pull.LeadingWhitespace;

			if (pulled)
			{
				trivia.InsertRange(0, tokens.GetRange(at, tokens.Count - at));   // the comments that stood between
				tokens.RemoveRange(at - 1, tokens.Count - at + 1);
				AppendMerged(text, segs, pull.Offset, pull.Length, pull.Line, pull.Column);
			}

			MergeSectionLines(text, segs, trivia);
			SpliceMerged(tokens, text.ToString(), segs, trivia, firstLeadingWs);
			leadingWs = false;
			return true;
		}

		// Walks the section(s) making up the rest of this logical line, appending each content line to the merged text
		// under that section's options. Leaves _pos at the end of the last section's ')' line.
		private void MergeSectionLines(System.Text.StringBuilder text, List<MergeSegment> segs, List<Token> trivia)
		{
			var opts = ReadSectionOpener(trivia);
			string indent = null;   // "smart" LTrim sample: the first content line's leading run of one indent character
			var lineCount = 0;      // content lines merged from THIS section; the Join goes before all but the first
			var quote = '\0';       // whether the merged text so far is inside a quoted string, scanned incrementally
			var scan = 0;

			if (Cur == '\n') Advance();

			while (true)
			{
				if (_pos >= _n)
				{
					Diagnostics.Add($"{_line}:{_col}: unterminated continuation section (missing closing ')')");
					return;
				}

				var pe = PhysicalLineEnd(_pos);
				var cp = _pos;

				while (cp < pe && (_s[cp] == ' ' || _s[cp] == '\t')) cp++;

				if (cp < pe && _s[cp] == ')')
				{
					// The ')' closes the section. Whatever follows it on that line is appended verbatim — no Join before
					// it, no trimming beyond the line's own, and no options-driven translation.
					Advance(cp - _pos);
					trivia.Add(Tok(TokenKind.ContinuationDelimiter, null, _line, _col, _pos, 1, true));
					Advance();
					var tail = pe;

					while (tail > _pos && (_s[tail - 1] == ' ' || _s[tail - 1] == '\t')) tail--;

					if (tail > _pos)
						AppendMerged(text, segs, _pos, tail - _pos, _line, _col);

					if (_pos < pe) Advance(pe - _pos);

					// A '(' on a following line continues this same logical line under fresh options, which is how a
					// script varies them partway through.
					int sp = _pos, sl = _line, sc = _col;
					List<Token> next = null;
					SkipBlankAndCommentLines(ref next);

					if (!(Cur == '(' && IsCodeSectionOpener(_pos)))
					{
						_pos = sp; _line = sl; _col = sc;
						return;
					}

					if (next != null) trivia.AddRange(next);
					opts = ReadSectionOpener(trivia);
					indent = null;
					lineCount = 0;   // AHK counts continuation lines per section, so the name-character space applies again

					if (Cur == '\n') Advance();

					continue;
				}

				// A content line. Its comment comes off first (so the whitespace to the comment's left goes with it),
				// then LTrim, then RTrim — the order AHK trims in before the line is appended.
				var end = pe;
				var commented = false;
				quote = QuoteStateAfter(text, ref scan, quote);
				// With the Comments option a `;` ends the line wherever it falls, quoted string included, because AHK
				// strips it before anything is parsed. Without it AHK keeps the comment as literal text, which nearly
				// always makes the merged line a syntax error; Keysharp instead drops a comment that falls OUTSIDE a
				// string, which is what reading these lines one at a time used to do. Keeping it would be worse than
				// either: with a Join that is not a line break, the comment would swallow every line after it.
				var cs = SectionCommentStart(_pos, pe, quote, quoteBlind: opts.Comments);

				if (cs < pe)
				{
					if (cs == cp)
					{
						// A line that is only a comment contributes nothing at all — not a blank line for the Join to
						// delimit, and not an indent sample for the smart LTrim.
						AddTrivia(trivia, _pos, pe, _line, _col);
						Advance(pe - _pos);
						AdvanceLineBreak();
						continue;
					}

					AddTrivia(trivia, cs, pe, _line, _col + (cs - _pos));
					end = cs;
					commented = true;
				}

				var from = _pos;

				if (opts.LTrim is bool all)
				{
					if (all) from = cp;
				}
				else
				{
					if (lineCount == 0) indent = CaptureIndent(_pos, pe);

					// A line indented less than the first, or with the other character, keeps its whitespace as is.
					if (indent != null && _pos + indent.Length <= pe
							&& _s.AsSpan(_pos, indent.Length).SequenceEqual(indent))
						from = _pos + indent.Length;
				}

				if (opts.RTrim || commented)
					while (end > from && (_s[end - 1] == ' ' || _s[end - 1] == '\t')) end--;

				// A single space separates a section's first content line from a line above that ends with a name
				// character — that is what keeps `Var :=` ⏎ (section) ⏎ `x` from gluing, and `obj` ⏎ (section) ⏎ `.prop`
				// a member access. It goes in here, not when the section opened, because AHK writes it as part of
				// appending that line: a section with no content lines at all never gets one.
				if (lineCount == 0 && text.Length > 0 && quote == '\0' && IsIdentPart(text[text.Length - 1]))
					_ = text.Append(' ');

				if (lineCount++ > 0)
					_ = text.Append(opts.Join);

				Advance(from - _pos);
				AppendContent(text, segs, _pos, end, _line, _col, opts);

				if (_pos < pe) Advance(pe - _pos);

				AdvanceLineBreak();
			}
		}

		// Emits the delimiter token for the `(` line at _pos (plus any comment on it) and returns its parsed options,
		// leaving _pos at that line's break. An unrecognized option is an error in AHK, not something to ignore.
		private Keysharp.Parsing.Parser.ContinuationOptions ReadSectionOpener(List<Token> trivia)
		{
			int po = _pos, pl = _line, pc = _col;
			Advance();                                  // '('
			int os = _pos;
			while (_pos < _n && Cur != '\n') Advance(); // rest of the opener line (options/comment)
			int lineEnd = _pos;
			// The '(' and its options (Join, LTrim, …) are one delimiter token; a trailing comment is separate.
			var cmt = DirectiveCommentStart(_s.AsSpan(os, lineEnd - os));
			int optEnd = cmt >= 0 ? os + cmt : lineEnd;

			while (optEnd > po && (_s[optEnd - 1] == ' ' || _s[optEnd - 1] == '\t' || _s[optEnd - 1] == '\r')) optEnd--;

			trivia.Add(Tok(TokenKind.ContinuationDelimiter, null, pl, pc, po, optEnd - po, true));

			if (cmt >= 0) AddTrivia(trivia, os + cmt, lineEnd, pl, pc + 1 + cmt);

			try { return Keysharp.Parsing.Parser.ParseContinuationOptions(optEnd > os ? _s[os..optEnd] : "", pl, null, _file); }
			catch (System.Exception ex) { Diagnostics.Add($"{pl}:{pc}: {ex.Message}"); return new(); }
		}

		/// <summary>Copies <c>[from, from + length)</c> of the source onto the merged text, recording where it came from.</summary>
		private void AppendMerged(System.Text.StringBuilder text, List<MergeSegment> segs, int from, int length, int line, int col)
		{
			// Recorded even for an empty run, so that a Join between two blank lines still has a position to map back to.
			segs.Add(new MergeSegment(text.Length, from, length, line, col));
			_ = text.Append(_s, from, length);
		}

		// Appends one content line, honouring the two options that alter the text as it is merged: the accent option
		// doubles every backtick so it survives as a literal one, and the legacy `%` option escapes every percent sign.
		// Both insert a character that came from no line, so the run is split around it and the source map stays exact.
		private void AppendContent(System.Text.StringBuilder text, List<MergeSegment> segs, int from, int to, int line, int col,
								   Keysharp.Parsing.Parser.ContinuationOptions opts)
		{
			if (!opts.LiteralEscape && !opts.PercentLiteral)
			{
				AppendMerged(text, segs, from, to - from, line, col);
				return;
			}

			var run = from;

			for (var i = from; i < to; i++)
			{
				// The backtick keeps its place and gains a second one; the percent sign gets one put in front of it.
				var after = opts.LiteralEscape && _s[i] == '`';

				if (!after && !(opts.PercentLiteral && _s[i] == '%'))
					continue;

				var cut = after ? i + 1 : i;
				AppendMerged(text, segs, run, cut - run, line, col + (run - from));
				_ = text.Append('`');
				run = cut;
			}

			AppendMerged(text, segs, run, to - run, line, col + (run - from));
		}

		// Whether the merged text is inside a quoted string once <paramref name="scan"/> has caught up with it, carrying
		// on from the state it was left in. AHK tracks the same thing over its merged line, to know that a section
		// starting inside a string neither takes the name-character space nor lets its quote marks stay live.
		private static char QuoteStateAfter(System.Text.StringBuilder text, ref int scan, char quote)
		{
			for (; scan < text.Length; scan++)
			{
				var ch = text[scan];

				if (quote != '\0')
				{
					if (ch == '`') scan++;               // an escape, so the character after it cannot close the string
					else if (ch == quote) quote = '\0';
				}
				else if (ch == '"' || ch == '\'')
					quote = ch;
			}

			return quote;
		}

		/// <summary>Steps over one line break, treating CRLF as a single one.</summary>
		private void AdvanceLineBreak()
		{
			if (Cur == '\r') Advance();

			if (Cur == '\n') Advance();
		}

		// Lexes the merged text and adds its tokens, remapped onto the source spans they came from, with the section's
		// own trivia (the delimiters and any comment the Comments option removed) woven back in by position. Trivia a
		// merged token covers is dropped: when a token spans a ')' the token owns those characters, and the published
		// stream may not contain two tokens over the same span.
		private void SpliceMerged(List<Token> tokens, string text, List<MergeSegment> segs, List<Token> trivia, bool firstLeadingWs)
		{
			var sub = new Lexer(text, _file, mergedLine: true);
			var first = true;
			var t = 0;
			var seg = 0;   // segments are visited in order, so this cursor saves rescanning them for every token

			foreach (var m in sub.Tokenize())
			{
				if (m.Kind == TokenKind.EOF)
					break;

				var mapped = MapMerged(segs, ref seg, m, first ? firstLeadingWs : m.LeadingWhitespace);
				first = false;

				while (t < trivia.Count && trivia[t].Offset + trivia[t].Length <= mapped.Offset)
					tokens.Add(trivia[t++]);

				while (t < trivia.Count && trivia[t].Offset < mapped.Offset + mapped.Length)
					t++;   // covered by this token

				tokens.Add(mapped);
			}

			while (t < trivia.Count)
				tokens.Add(trivia[t++]);

			foreach (var d in sub.Diagnostics)
				Diagnostics.Add(RemapDiagnostic(text, segs, d));
		}

		// Rewrites one "line:col: message" the nested lexer produced so that it points at the file position the
		// offending text was written at, rather than anywhere in the merged text, which exists only in memory.
		private static string RemapDiagnostic(string text, List<MergeSegment> segs, string d)
		{
			var sep = d.IndexOf(": ");

			if (sep < 0 || segs.Count == 0)
				return d;

			var colon = d.AsSpan(0, sep).IndexOf(':');

			if (colon < 0 || !int.TryParse(d.AsSpan(0, colon), out var line) || !int.TryParse(d.AsSpan(colon + 1, sep - colon - 1), out var col))
				return d;

			var at = 0;

			for (var l = 1; l < line; l++)
			{
				var nl = text.IndexOf('\n', at);

				if (nl < 0) break;

				at = nl + 1;
			}

			var seg = 0;
			var (_, srcLine, srcCol) = MapMergedPos(segs, ref seg, System.Math.Min(text.Length, at + col - 1));
			return $"{srcLine}:{srcCol}: {d[(sep + 2)..]}";
		}

		// Gives a token lexed from merged text the file position it came from. Its span runs from the first source
		// character it covers to the last, which for a token built out of several lines takes in the line breaks, the
		// trimmed indentation and the ')' in between — the characters really are all part of it. Text the merge
		// inserted itself has no source, so a token made only of that (the ',' of `(Join,`) lands empty on the
		// boundary between the lines it separates.
		private Token MapMerged(List<MergeSegment> segs, ref int seg, Token m, bool leadingWs)
		{
			var (start, line, col) = MapMergedPos(segs, ref seg, m.Offset);
			var end = -1;

			for (var i = seg; i < segs.Count && segs[i].TextStart < m.Offset + m.Length; i++)
				end = segs[i].SrcStart + System.Math.Min(m.Offset + m.Length - segs[i].TextStart, segs[i].Length);

			return new Token(m.Kind, m.Text, line, col, start, end > start ? end - start : 0, leadingWs, _file);
		}

		/// <summary>Where in the file the merged character at <paramref name="at"/> came from.</summary>
		private static (int Offset, int Line, int Column) MapMergedPos(List<MergeSegment> segs, ref int seg, int at)
		{
			// `<=`, so that a position exactly where a segment ends belongs to the next one — the line it really begins
			// on, not the end of the line before it.
			while (seg + 1 < segs.Count && segs[seg].TextStart + segs[seg].Length <= at)
				seg++;

			var s = segs.Count > 0 ? segs[seg] : default;

			if (at <= s.TextStart)
				return (s.SrcStart, s.Line, s.Column);

			var d = System.Math.Min(at - s.TextStart, s.Length);
			return (s.SrcStart + d, s.Line, s.Column + d);
		}

		// The leading run of one indent character (space or tab) on the line at <paramref name="p"/>, or null when the
		// line is not indented. Only one kind of character counts, so a line indented with a mix of both keeps
		// everything past the first run.
		private string CaptureIndent(int p, int lineEnd)
		{
			if (p >= lineEnd || (_s[p] != ' ' && _s[p] != '\t')) return null;

			var end = p;
			while (end < lineEnd && _s[end] == _s[p]) end++;

			return _s[p..end];
		}

		// Whether a token can be pulled into a merge and re-lexed there. Trivia cannot (it is not part of the merged
		// text), and neither can anything the lexer only recognizes at the start of a line, since the merged text is
		// the middle of one.
		private static bool CanMergeAcross(TokenKind kind) => kind is not (
					TokenKind.EOF or TokenKind.Newline or TokenKind.Comment or TokenKind.Directive or
					TokenKind.ContinuationDelimiter or TokenKind.CSharpBlock or TokenKind.DoubleColon or
					TokenKind.HotkeyTrigger or TokenKind.RemapSourceKey or TokenKind.RemapTargetKey or
					TokenKind.HotstringTrigger or TokenKind.HotstringExpansion);

		// A '(' at the start of a line opens a continuation section unless it's '(:' or its option text contains another
		// '(' or ')', in which case AHK treats the line as an ordinary parenthesised expression (e.g. `(x.y)()`, `(a OR b)`).
		private bool IsCodeSectionOpener(int p)
		{
			if (p + 1 < _n && _s[p + 1] == ':') return false;
			char prev = ' ';
			for (int i = p + 1; i < _n; i++)
			{
				char ch = _s[i];
				if (ch == '\n') break;
				if (ch == ';' && (prev == ' ' || prev == '\t')) break;   // trailing comment
				if (ch == '(' || ch == ')') return false;
				prev = ch;
			}
			return true;
		}

		// ---- hotkeys / hotstrings ----

		private static bool IsModifierKeyChar(char c) => c is '#' or '!' or '^' or '+' or '<' or '>';

		// True if every char in [from, to) is a hotkey modifier/prefix symbol (the set accepted by AHK's
		// Hotkey::TextToModifiers). Used to tell a quote in key position (`'::`, `+"::`) from a string opener.
		private bool IsHotkeyPrefixOnly(int from, int to)
		{
			for (int j = from; j < to; j++)
				if (_s[j] is not ('#' or '!' or '^' or '+' or '<' or '>' or '*' or '~' or '$'))
					return false;
			return true;
		}

		// Resolves whether a hotstring with the given option text runs in execute mode (replacement is code): an explicit
		// `X` (or `X0` to turn it off) overrides the `#Hotstring`-set default.
		private bool HotstringExecutes(string opts)
		{
			bool exec = _hsExecuteDefault;
			for (int k = 0; k < opts.Length; k++)
				if (opts[k] is 'x' or 'X') exec = !(k + 1 < opts.Length && opts[k + 1] == '0');
			return exec;
		}

		// Directives whose argument is free-form raw text (may contain quotes, `;`, brackets) — captured verbatim so it
		// is not lexed as code. Token-needing directives (#if/#include/#import/#HotIf) are deliberately excluded.
		// Internal so the parser can tell which directives arrive as a single verbatim token (commas inside its text)
		// versus normally-lexed tokens, when it validates per-directive argument counts.
		internal static readonly HashSet<string> RawArgDirectives = new(System.StringComparer.OrdinalIgnoreCase)
		{
			"hotstring", "requires", "dllload", "singleinstance", "warn", "hookmutexname", "errorstdout", "package",
			// #Error/#Warning carry an English sentence, so they are the likeliest of all to contain an apostrophe or a
			// brace. Lexed as code, `#Warning don't` is an unterminated string and `#Warning fix the {` swallows every
			// following line until the braces balance — for #Warning silently, since it does not fail the build.
			"error", "warning"
		};

		// Capture block contents verbatim; quoted file forms remain ordinary directives.
		private bool TryScanCSharpBlock(List<Token> tokens, bool leadingWs)
		{
			if (Cur != '#') return false;
			int lineEnd = _pos;
			while (lineEnd < _n && _s[lineEnd] != '\n' && _s[lineEnd] != '\r') lineEnd++;
			if (!IsCSharpBlockOpener(_s.AsSpan(_pos, lineEnd - _pos))) return false;

			int hl = _line, hc = _col;
			tokens.Add(Tok(TokenKind.Hash, "#", hl, hc, _pos, 1, leadingWs));
			Advance();                                   // '#'
			while (Cur == ' ' || Cur == '\t') Advance();
			int nl = _line, nc = _col, no = _pos;
			while (_pos < _n && (char.IsLetterOrDigit(Cur) || Cur == '_')) Advance();
			tokens.Add(Tok(TokenKind.Identifier, _s.Substring(no, _pos - no), nl, nc, no, _pos - no, true));

			// Preserve unexpected options for a precise lowerer diagnostic.
			while (Cur == ' ' || Cur == '\t') Advance();
			int os = _pos, ol = _line, oc = _col;
			while (_pos < _n && Cur != '\n' && Cur != '\r') Advance();
			int oe = _pos;
			var cmt = DirectiveCommentStart(_s.AsSpan(os, oe - os));
			var optEnd = oe;
			if (cmt >= 0) oe = os + cmt;
			while (oe > os && (_s[oe - 1] == ' ' || _s[oe - 1] == '\t')) oe--;
			if (oe > os) tokens.Add(Tok(TokenKind.Identifier, _s.Substring(os, oe - os), ol, oc, os, oe - os, true));
			if (cmt >= 0) AddTrivia(tokens, os + cmt, optEnd, ol, oc + cmt);

			while (_pos < _n && Cur != '\n') Advance();
			if (_pos < _n) Advance();                    // consume the newline that ends the `#CSharp` line

			// Body: every line up to (not including) the one whose first token is `#EndCSharp`.
			int bodyStart = _pos, bodyLine = _line, bodyCol = _col;
			int bodyEnd = -1;
			while (_pos < _n)
			{
				int lineStart = _pos;
				while (_pos < _n && Cur != '\n') Advance();

				if (IsCSharpBlockTerminator(_s.AsSpan(lineStart, _pos - lineStart)))
				{
					bodyEnd = lineStart;   // the #EndCSharp line is consumed, but not its newline
					break;
				}

				if (_pos < _n) Advance();
			}

			if (bodyEnd < 0)
			{
				Diagnostics.Add($"{hl}:{hc}: #CSharp without a matching #EndCSharp");
				bodyEnd = _n;
			}

			var body = _s.Substring(bodyStart, System.Math.Max(0, bodyEnd - bodyStart));
			tokens.Add(Tok(TokenKind.CSharpBlock, body, bodyLine, bodyCol, bodyStart, body.Length, true));

			// Consumed above but not part of the body, so it needs a token of its own.
			if (_pos > bodyEnd)
				tokens.Add(Tok(TokenKind.Directive, null, _line, 1, bodyEnd, _pos - bodyEnd, true));

			// Leave the newline for Tokenize's line-start guard.
			return true;
		}

		/// <summary>Recognizes a block opener after stripping its trailing AHK comment.</summary>
		internal static bool IsCSharpBlockOpener(ReadOnlySpan<char> line)
		{
			if (line.IsEmpty || line[0] != '#') return false;
			int p = 1;
			while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
			int ns = p;
			while (p < line.Length && (char.IsLetterOrDigit(line[p]) || line[p] == '_')) p++;
			if (!line[ns..p].Equals("csharp", System.StringComparison.OrdinalIgnoreCase)) return false;

			var opt = line[p..];
			var cmt = DirectiveCommentStart(opt);

			if (cmt >= 0)
				opt = opt[..cmt];

			return !opt.Contains('"') && !opt.Contains('\'');
		}

		/// <summary>Recognizes the exact EndCSharp directive name.</summary>
		internal static bool IsCSharpBlockTerminator(ReadOnlySpan<char> line)
		{
			int p = 0;
			while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
			if (p >= line.Length || line[p] != '#') return false;
			p++;
			while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
			int ns = p;
			while (p < line.Length && (char.IsLetterOrDigit(line[p]) || line[p] == '_')) p++;
			return line[ns..p].Equals("endcsharp", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Finds a directive's whitespace-delimited trailing comment.</summary>
		internal static int DirectiveCommentStart(ReadOnlySpan<char> text)
		{
			for (var i = 0; i < text.Length; i++)
				if (text[i] == ';' && (i == 0 || text[i - 1] == ' ' || text[i - 1] == '\t'))
					return i;

			return -1;
		}

		// If the current line is a raw-argument directive (`#Hotstring …`), emits `#`, the name identifier, and the rest
		// of the line as a single raw token (so quotes/semicolons in the argument do not start strings/comments).
		private bool TryScanRawDirective(List<Token> tokens, bool leadingWs)
		{
			if (Cur != '#') return false;
			int p = _pos + 1;
			while (p < _n && (_s[p] == ' ' || _s[p] == '\t')) p++;
			int ns = p;
			while (p < _n && (char.IsLetterOrDigit(_s[p]) || _s[p] == '_')) p++;
			var name = _s.Substring(ns, p - ns);
			if (!RawArgDirectives.Contains(name)) return false;

			int hl = _line, hc = _col, ho = _pos;
			tokens.Add(Tok(TokenKind.Hash, "#", hl, hc, ho, 1, leadingWs));
			Advance();                                  // '#'
			while (Cur == ' ' || Cur == '\t') Advance();
			int nl = _line, nc = _col, no = _pos;
			while (_pos < _n && (char.IsLetterOrDigit(Cur) || Cur == '_')) Advance();
			tokens.Add(Tok(TokenKind.Identifier, _s.Substring(no, _pos - no), nl, nc, no, _pos - no, true));
			while (Cur == ' ' || Cur == '\t') Advance();

			int rs = _pos, rl = _line, rc = _col;
			bool prevWs = true;
			while (_pos < _n && Cur != '\n' && Cur != '\r')
			{
				if (Cur == ';' && prevWs) break;        // a real trailing comment
				prevWs = Cur == ' ' || Cur == '\t';
				Advance();
			}
			int re = _pos;
			int cs = _pos, cl = _line, cc = _col;
			while (_pos < _n && Cur != '\n') Advance();  // consume the trailing comment
			while (re > rs && (_s[re - 1] == ' ' || _s[re - 1] == '\t')) re--;
			if (re > rs) tokens.Add(Tok(TokenKind.Identifier, _s.Substring(rs, re - rs), rl, rc, rs, re - rs, true));
			AddTrivia(tokens, cs, _pos, cl, cc);
			return true;
		}

		// At a line start, if the line is a `#Hotstring X`/`#Hotstring X0` directive, update the default execute mode.
		// Does not consume input — the directive is still lexed normally and emitted to the DHHR.
		private void MaybeTrackHotstringDirective()
		{
			if (Cur != '#') return;
			int i = _pos + 1;
			while (i < _n && (_s[i] == ' ' || _s[i] == '\t')) i++;
			const string kw = "hotstring";
			if (i + kw.Length > _n) return;
			for (int k = 0; k < kw.Length; k++) if (char.ToLowerInvariant(_s[i + k]) != kw[k]) return;
			i += kw.Length;
			int s = i;
			while (i < _n && _s[i] != '\n' && _s[i] != '\r') i++;
			var opts = _s.Substring(s, i - s).Trim();
			for (int k = 0; k < opts.Length; k++)
				if (opts[k] is 'x' or 'X') _hsExecuteDefault = !(k + 1 < opts.Length && opts[k + 1] == '0');
		}

		// At a logical line start, try to recognise a hotkey (`^!a::…`), remap (`a::b`), or hotstring
		// (`:opts:trigger::…`). On success emits the appropriate token(s) and returns true; otherwise
		// fully restores the scanner position so the line is lexed as ordinary code.
		private bool TryScanHot(List<Token> tokens, bool leadingWs)
		{
			int sp = _pos, sl = _line, sc = _col;
			if (Cur == ':')
			{
				if (TryScanHotstring(tokens, leadingWs)) return true;
				_pos = sp; _line = sl; _col = sc;
			}
			if (TryScanHotkey(tokens, leadingWs)) return true;
			_pos = sp; _line = sl; _col = sc;
			return false;
		}

		// Finds the offset of the hotkey/hotstring `::` separator on the current line, starting at `from`.
		// Honors backtick escapes (`` `: `` is a literal colon, not part of a separator) when escapeAware.
		// Stops (returns -1) at end of line, a whitespace-preceded comment, or a string-opening quote
		// (a quote in key position immediately followed by `::` is a single-char trigger, not a string).
		private int FindSeparator(int from, bool escapeAware, bool allowEmpty)
		{
			bool escaped = false;
			for (int i = from; i < _n; i++)
			{
				char c = _s[i];
				if (c == '\n' || c == '\r') return -1;
				if (c == '"' || c == '\'')
				{
					// A quote almost always means the line is an expression and any following `::` is string
					// content, not a hotkey separator (e.g. `MsgBox("a::b")`, `x := "::"`). The exception is a
					// quote used as a single-character key: it sits in key position (only modifier symbols
					// precede it) and is immediately followed by the `::` separator, e.g. `'::;` or `+"::x`.
					if (i + 2 < _n && _s[i + 1] == ':' && _s[i + 2] == ':' && IsHotkeyPrefixOnly(from, i))
						return i + 1;
					return -1;                                        // a real string opener: not a hotkey
				}
				if (!escaped && (c == ' ' || c == '\t') && i + 1 < _n && _s[i + 1] == ';') return -1; // comment
				if (!escaped && c == ':' && i + 1 < _n && _s[i + 1] == ':')
				{
					if (allowEmpty || i > from) return i;             // at least one trigger char required
				}
				escaped = escapeAware && !escaped && c == '`';
			}
			return -1;
		}

		private bool TryScanHotkey(List<Token> tokens, bool leadingWs)
		{
			int start = _pos, sl = _line, sc = _col;
			int sep = FindSeparator(_pos, escapeAware: true, allowEmpty: false);
			if (sep < 0) sep = FindSeparator(_pos, escapeAware: false, allowEmpty: false);
			if (sep < 0) return false;

			// `sep` indexes the first ':' of the '::' separator. Decide remap (target is a single key) vs hotkey.
			int afterSep = sep + 2;
			int rk = TryMatchRemapTarget(afterSep);
			if (rk >= 0)
			{
				// The whole line is a remap `source::target`. Split it here (the parser/lowerer no longer re-parse) and
				// emit a RemapSourceKey then RemapTargetKey pair covering source..target.
				string remap = _s.Substring(start, rk - start);
				while (_pos < rk) Advance();
				SplitRemap(remap, out var src, out var tgt);
				int sepLocal = remap.Length - tgt.Length - 2;   // index of `::` within `remap`
				tokens.Add(Tok(TokenKind.RemapSourceKey, src, sl, sc, start, sepLocal, leadingWs));
				tokens.Add(Tok(TokenKind.DoubleColon, "::", sl, sc + sepLocal, start + sepLocal, 2, false));
				tokens.Add(Tok(TokenKind.RemapTargetKey, tgt, sl, sc + sepLocal + 2, start + sepLocal + 2, tgt.Length, false));
				return true;
			}

			// Plain hotkey: the rest of the line lexes normally.
			string trig = _s.Substring(start, sep - start);
			while (_pos < afterSep) Advance();
			tokens.Add(Tok(TokenKind.HotkeyTrigger, trig, sl, sc, start, sep - start, leadingWs));
			tokens.Add(Tok(TokenKind.DoubleColon, "::", sl, sc + (sep - start), sep, 2, false));
			return true;
		}

		// If the text starting at `from` is a valid remap target — optional modifier keys, one key name, then only
		// whitespace/comment to end of line — returns the offset just past the target. Otherwise -1.
		private int TryMatchRemapTarget(int from)
		{
			int i = from;
			while (i < _n && (_s[i] == ' ' || _s[i] == '\t')) i++;          // leading whitespace
			while (i < _n && IsModifierKeyChar(_s[i])) i++;                 // modifier keys (#!^+<>)
			if (i >= _n || _s[i] == '\n' || _s[i] == '\r') return -1;       // empty target -> not a remap
			if (_s[i] == '{' || _s[i] == '}') return -1;                    // brace -> OTB block body, not a remap
			int keyStart = i;
			bool identifierRun = false;
			// One key: an identifier-like run (a key name) or a single character.
			if (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')
			{
				identifierRun = true;
				while (i < _n && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')) i++;
			}
			// Backtick escape: skip the escaped char too, but never the line terminator. A trailing
			// backtick at EOL is the literal backtick key; consuming the following char would eat the
			// '\n' on LF files (the next char after '`' is '\r' on CRLF, '\n' on LF), making an
			// otherwise-valid remap like "+w::`" fail to parse on Linux while passing on Windows.
			else if (_s[i] == '`') { i++; if (i < _n && _s[i] != '\n' && _s[i] != '\r') i++; }
			else i++;
			int keyEnd = i;
			// A multi-character identifier is only a remap target if it actually names a key (e.g. `Enter`,
			// `Space`, `Numpad0`, `F1`, `vk1B`). Otherwise `x::MsgBox` is a hotkey whose body calls MsgBox(),
			// not a remap to a non-existent "MsgBox" key — bail out so it lexes as a normal hotkey body.
			// Single characters are always valid remap targets, matching AutoHotkey.
			if (identifierRun && keyEnd - keyStart > 1 && !IsRemapTargetKeyName(_s.AsSpan(keyStart, keyEnd - keyStart)))
				return -1;
			while (i < _n && (_s[i] == ' ' || _s[i] == '\t')) i++;          // trailing whitespace
			if (i >= _n || _s[i] == '\n' || _s[i] == '\r') return keyEnd;   // EOL -> remap
			if (_s[i] == ';') return keyEnd;                               // trailing comment -> remap
			if (_s[i] == '/' && i + 1 < _n && _s[i + 1] == '*') return keyEnd;
			return -1;                                                      // something else follows -> hotkey body
		}

		// Whether `keyName` resolves to a real key (named key, vk/sc notation) via the same table the hotkey
		// engine uses, so remap detection never accepts an ordinary identifier (a function name) as a key.
		// Falls back to true when no runtime key tables are available (e.g. standalone tooling), preserving
		// the legacy permissive behavior in that case.
		private static bool IsRemapTargetKeyName(System.ReadOnlySpan<char> keyName)
		{
			// Copilot is accepted only so static remap syntax can lower it to `<#<+F23`. It deliberately remains
			// absent from the runtime key-name tables used by Send, KeyWait, GetKeyState, Hotkey(), and similar APIs.
			if (keyName.Equals("Copilot".AsSpan(), System.StringComparison.OrdinalIgnoreCase))
				return true;

			// AltTab, ShiftAltTab, AltTabMenu, AltTabAndMenu and AltTabMenuDismiss aren't real keys but are valid
			// remap targets — `x::AltTab` registers a hotkey with that special hook action (handled in the remap
			// lowering), so accept them here instead of lexing the line as a `AltTab()` function-call body.
			if (Keysharp.Internals.Input.Keyboard.HotkeyDefinition.ConvertAltTab(keyName.ToString(), false) != 0)
				return true;

			var ht = Keysharp.Runtime.Script.TheScript?.HookThread;
			if (ht == null)
				return true;
			uint vk = 0, sc = 0;
			var source = Keysharp.Internals.Input.Keyboard.KeySource.None;
			uint? mods = null;
			// Allow the combined vk+sc pair form (e.g. `vk42sc030`): it's a valid remap target and the pattern
			// (`vk<hex>sc<hex>`) can't be mistaken for an ordinary function name, so accepting it is safe.
			return ht.TextToVKandSC(keyName, ref vk, ref sc, ref source, ref mods, layout: null, allowVkScPair: true);
		}

		// Splits a validated remap line `source::target` into its source/target key text, honoring backtick escapes
		// before the `::` (a trailing single backtick in the source is restored, matching AHK remap parsing).
		private static void SplitRemap(string remapKey, out string sourceKey, out string targetKey)
		{
			int index = -1; bool escape = false; int lastIndex = remapKey.Length - 2;
			for (int i = 0; i < remapKey.Length - 1; i++)
			{
				if (i == lastIndex) escape = false;
				else if (remapKey[i] == '`' && remapKey[i + 1] != ':') escape = !escape;
				else if (remapKey[i] == ':' && remapKey[i + 1] == ':' && !escape && i != 0) { index = i; break; }
				else escape = false;
			}
			sourceKey = remapKey.Substring(0, index);
			if (sourceKey[^1] == '`' && (sourceKey.Length < 2 || sourceKey[^2] != '`')) sourceKey += '`';
			targetKey = remapKey.Substring(index + 2);
		}

		private bool TryScanHotstring(List<Token> tokens, bool leadingWs)
		{
			int start = _pos, sl = _line, sc = _col;
			if (Cur != ':') return false;
			// options: between the first ':' and the next ':' (no newline, no second ':')
			int i = _pos + 1;
			while (i < _n && _s[i] != ':' && _s[i] != '\n' && _s[i] != '\r') i++;
			if (i >= _n || _s[i] != ':') return false;
			var opts = _s.Substring(_pos + 1, i - (_pos + 1));              // option chars between the two colons
			int trigStart = i + 1;                                          // after the second ':'
			int sep = FindSeparator(trigStart, escapeAware: true, allowEmpty: true);
			if (sep < 0) return false;
			int afterSep = sep + 2;
			string trigTok = _s.Substring(start, sep - start);                // `:opts:trigger` (separator excluded)
			while (_pos < afterSep) Advance();
			tokens.Add(Tok(TokenKind.HotstringTrigger, trigTok, sl, sc, start, sep - start, leadingWs));
			tokens.Add(Tok(TokenKind.DoubleColon, "::", sl, sc + (sep - start), sep, 2, false));

			// Body: a block `{`, an inline expansion, a continuation-section expansion, or (EOL) a following statement.
			int save = _pos, saveL = _line, saveC = _col;
			while (Cur == ' ' || Cur == '\t') Advance();
			if (Cur == '{') return true;                                     // OTB block body — lexes normally
			if (Cur == ';') { while (_pos < _n && Cur != '\n') Advance(); }  // trailing comment then EOL
			if (Cur == '\n' || Cur == '\r' || _pos >= _n)
			{
				// No inline expansion. A continuation section on the next non-blank line is the expansion.
				int np = _pos, nl = _line, nc = _col;
				while (Cur == '\n' || Cur == '\r' || Cur == ' ' || Cur == '\t') Advance();
				if (Cur == '(')
				{
					EmitHotstringContinuation(tokens, sl, sc, leadingWs);
					return true;
				}
				_pos = save; _line = saveL; _col = saveC;                   // code body follows on later lines
				return true;
			}
			// In execute mode the replacement is code: leave the rest of the line to lex as a normal statement body.
			if (HotstringExecutes(opts)) { _pos = save; _line = saveL; _col = saveC; return true; }
			// Inline expansion: the rest of the line (stop at a whitespace-preceded comment).
			_pos = save; _line = saveL; _col = saveC;
			int expStart = _pos, el = _line, ec = _col;
			bool prevWs = true;
			while (_pos < _n && Cur != '\n' && Cur != '\r')
			{
				if (Cur == ';' && prevWs) break;
				prevWs = Cur == ' ' || Cur == '\t';
				Advance();
			}
			int expEnd = _pos;
			int cs = _pos, cl = _line, cc = _col;
			while (_pos < _n && Cur != '\n') Advance();   // consume any trailing comment so it is not re-lexed
			// Trim a trailing run of whitespace (a preceding-comment delimiter or line padding).
			while (expEnd > expStart && (_s[expEnd - 1] == ' ' || _s[expEnd - 1] == '\t')) expEnd--;
			string exp = _s.Substring(expStart, expEnd - expStart);
			tokens.Add(Tok(TokenKind.HotstringExpansion, exp, el, ec, expStart, expEnd - expStart, false));
			AddTrivia(tokens, cs, _pos, cl, cc);
			return true;
		}

		// Captures a continuation-section hotstring expansion (`::trigger::` then `(`…`)` on following lines),
		// joining with real newlines, and emits it as a HotstringExpansion token.
		private void EmitHotstringContinuation(List<Token> tokens, int line, int col, bool leadingWs)
		{
			int codeStart = _pos;   // at '('
			while (_pos < _n)
			{
				int k = 0;
				while (_pos + k < _n && (_s[_pos + k] == ' ' || _s[_pos + k] == '\t')) k++;
				if (_pos + k < _n && _s[_pos + k] == ')') { for (int j = 0; j <= k; j++) Advance(); break; }
				while (_pos < _n && Cur != '\n') Advance();
				if (_pos < _n) Advance(); else break;
			}
			var code = _s.Substring(codeStart, _pos - codeStart);
			string inner;
			// MultilineString joins with real newlines and preserves indentation (escaping `"`/`*` for re-escaping);
			// the lowerer runs the result back through EscapedString, which reverses those escapes.
			try { inner = Keysharp.Parsing.Parser.MultilineString(code, line, "lexer"); }
			catch (System.Exception ex) { Diagnostics.Add($"{line}:{col}: {ex.Message}"); inner = ""; }
			tokens.Add(Tok(TokenKind.HotstringExpansion, inner, line, col, codeStart, _pos - codeStart, false));
		}

		private (TokenKind kind, int length) ScanOperator()
		{
			char c = _s[_pos], c1 = At(1), c2 = At(2), c3 = At(3);

			switch (c)
			{
				case '(': return (TokenKind.LParen, 1);
				case ')': return (TokenKind.RParen, 1);
				case '[': return (TokenKind.LBracket, 1);
				case ']': return (TokenKind.RBracket, 1);
				case '{': return (TokenKind.LBrace, 1);
				case '}': return (TokenKind.RBrace, 1);
				case ',': return (TokenKind.Comma, 1);
				case '#': return (TokenKind.Hash, 1);

				case '?':
					if (c1 == '?') return c2 == '=' ? (TokenKind.NullCoalesceAssign, 3) : (TokenKind.NullCoalesce, 2);
					if (c1 == '.') return (TokenKind.QuestionDot, 2);
					return (TokenKind.Question, 1);

				case ':':
					if (c1 == '=') return (TokenKind.Assign, 2);
					if (c1 == ':') return (TokenKind.DoubleColon, 2);
					return (TokenKind.Colon, 1);

				case '.':
					if (c1 == '.' && c2 == '.') return (TokenKind.Ellipsis, 3);
					if (c1 == '=') return (TokenKind.DotAssign, 2);
					return (TokenKind.Dot, 1);

				case '+':
					if (c1 == '+') return (TokenKind.PlusPlus, 2);
					if (c1 == '=') return (TokenKind.PlusAssign, 2);
					return (TokenKind.Plus, 1);

				case '-':
					if (c1 == '-') return (TokenKind.MinusMinus, 2);
					if (c1 == '=') return (TokenKind.MinusAssign, 2);
					return (TokenKind.Minus, 1);

				case '*':
					if (c1 == '*') return c2 == '=' ? (TokenKind.PowerAssign, 3) : (TokenKind.Power, 2);
					if (c1 == '=') return (TokenKind.StarAssign, 2);
					return (TokenKind.Star, 1);

				case '/':
					if (c1 == '/') return c2 == '=' ? (TokenKind.IntDivAssign, 3) : (TokenKind.IntDiv, 2);
					if (c1 == '=') return (TokenKind.SlashAssign, 2);
					return (TokenKind.Slash, 1);

				case '%':
					if (c1 == '=') return (TokenKind.PercentAssign, 2);
					return (TokenKind.Percent, 1);

				case '~':
					if (c1 == '=') return (TokenKind.RegexMatch, 2);
					return (TokenKind.BitNot, 1);

				case '!':
					if (c1 == '~' && c2 == '=') return (TokenKind.NotRegexMatch, 3);
					if (c1 == '=') return c2 == '=' ? (TokenKind.NotIdentity, 3) : (TokenKind.NotEqual, 2);
					return (TokenKind.Not, 1);

				case '=':
					if (c1 == '=') return (TokenKind.Identity, 2);
					if (c1 == '>') return (TokenKind.FatArrow, 2);
					return (TokenKind.Equal, 1);

				case '<':
					if (c1 == '<') return c2 == '=' ? (TokenKind.ShiftLeftAssign, 3) : (TokenKind.ShiftLeft, 2);
					if (c1 == '=') return (TokenKind.LessEqual, 2);
					return (TokenKind.Less, 1);

				case '>':
					if (c1 == '>' && c2 == '>') return c3 == '=' ? (TokenKind.ShiftRightLogicalAssign, 4) : (TokenKind.ShiftRightLogical, 3);
					if (c1 == '>') return c2 == '=' ? (TokenKind.ShiftRightAssign, 3) : (TokenKind.ShiftRight, 2);
					if (c1 == '=') return (TokenKind.GreaterEqual, 2);
					return (TokenKind.Greater, 1);

				case '&':
					if (c1 == '&') return (TokenKind.LogicalAnd, 2);
					if (c1 == '=') return (TokenKind.BitAndAssign, 2);
					return (TokenKind.BitAnd, 1);

				case '|':
					if (c1 == '|') return (TokenKind.LogicalOr, 2);
					if (c1 == '=') return (TokenKind.BitOrAssign, 2);
					return (TokenKind.BitOr, 1);

				case '^':
					if (c1 == '=') return (TokenKind.BitXorAssign, 2);
					return (TokenKind.BitXor, 1);

				default:
					return (TokenKind.Unknown, 1);
			}
		}
	}
}
