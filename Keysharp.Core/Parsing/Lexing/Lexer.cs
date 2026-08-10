using System.Collections.Generic;

namespace Keysharp.Parsing.Lexing
{
	/// <summary>
	/// A hand-written single-pass scanner for AutoHotkey v2(.1) source.
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

		// Depth of open *code* continuation sections (a `(`-line whose options contain no extra parens and that does
		// not follow an open string). Their delimiting parens are stripped and the inner lines are spliced inline as
		// code (newlines suppressed), so e.g.  Var :=  ⏎ ( ⏎ obj ⏎ )  becomes  Var := obj.
		private int _codeSectionDepth;

		// Source file these tokens belong to (an #included file); null for the main script. Stamped onto every token so
		// diagnostics can be reported per file without a post-pass.
		private readonly string _file;

		public Lexer(string source, string file = null)
		{
			_s = source ?? "";
			_n = _s.Length;
			_file = file;
		}

		public static List<Token> Tokenize(string source) => new Lexer(source).Tokenize();

		/// <summary>Creates a token tagged with this lexer's source file.</summary>
		private Token Tok(TokenKind kind, string text, int line, int col, int offset, int length, bool leadingWs) =>
			new(kind, text, line, col, offset, length, leadingWs, _file);

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
					// Inside a code continuation section, line breaks between content lines are suppressed (the lines
					// are spliced together). Outside one, a following '(' section opener (after any blank/comment lines)
					// likewise suppresses the break and splices its inner code onto the current logical line.
					if (_codeSectionDepth > 0) { leadingWs = true; continue; }
					// A section's first content token glues directly onto the preceding line (AHK joins with no
					// separator and LTrims the line), so `obj` ⏎ (section) `.prop` becomes member access `obj.prop`,
					// not concat. Inner line breaks keep leadingWs=true so `a` ⏎ `b` stays implicit concatenation.
					if (TryEnterCodeSection()) { leadingWs = false; continue; }
					tokens.Add(Tok(TokenKind.Newline, "\n", sl, sc, so, _pos - so, leadingWs));
					leadingWs = true;
					continue;
				}

				// Closing ')' of a code continuation section (its delimiting parens are not emitted).
				if (_codeSectionDepth > 0 && leadingWs && c == ')')
				{
					Advance();
					_codeSectionDepth--;
					leadingWs = false;
					continue;
				}

				// Line comment: ';' is a comment only at line start or when preceded by whitespace.
				if (c == ';' && leadingWs)
				{
					while (_pos < _n && _s[_pos] != '\n') Advance();
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
					if (_line > cl)              // spanned lines -> emit a single Newline token
					{
						tokens.Add(Tok(TokenKind.Newline, "\n", cl, cc, co, _pos - co, leadingWs));
					}
					leadingWs = true;
					continue;
				}

				// At the beginning of a logical line, a `::` (outside strings/comments) marks a hotkey, remap, or
				// hotstring definition. The trigger text is taken raw (it can contain operator characters).
				if (tokens.Count == 0 || tokens[^1].Kind == TokenKind.Newline)
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
						while (Cur == ' ' || Cur == '\t' || Cur == '\r' || Cur == '\n') Advance();
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
				if (ch == '\n' || ch == '\r') break;   // AHK strings do not span raw line breaks (unterminated)
				if (ch == '`') { Advance(); if (_pos < _n && Cur != '\n' && Cur != '\r') Advance(); continue; } // backtick escape: skip next char but never the line terminator (CRLF/LF parity)
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

		// From just past a collapsed newline run, skip blank/comment lines; if the next line opens a *code* continuation
		// section (a '(' whose options contain no extra parens), consume the opener line and enter the section. Returns
		// true on entry (caller suppresses the line break so the section's inner code splices onto the current line).
		private bool TryEnterCodeSection()
		{
			int sp = _pos, sl = _line, sc = _col;
			while (_pos < _n)
			{
				if (Cur == ' ' || Cur == '\t' || Cur == '\r' || Cur == '\n') { Advance(); continue; }
				if (Cur == ';') { while (_pos < _n && Cur != '\n') Advance(); continue; }
				if (Cur == '/' && At(1) == '*')
				{
					Advance(2);
					while (_pos < _n && !(Cur == '*' && At(1) == '/')) Advance();
					if (_pos < _n) Advance(2);
					continue;
				}
				break;
			}
			if (Cur == '(' && IsCodeSectionOpener(_pos))
			{
				Advance();                                  // '('
				while (_pos < _n && Cur != '\n') Advance(); // rest of the opener line (options/comment) is ignored
				if (Cur == '\n') Advance();
				while (Cur == ' ' || Cur == '\t' || Cur == '\r') Advance();   // LTrim the first content line
				_codeSectionDepth++;
				return true;
			}
			_pos = sp; _line = sl; _col = sc;
			return false;
		}

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
			if (cmt >= 0) oe = os + cmt;
			while (oe > os && (_s[oe - 1] == ' ' || _s[oe - 1] == '\t')) oe--;
			if (oe > os) tokens.Add(Tok(TokenKind.Identifier, _s.Substring(os, oe - os), ol, oc, os, oe - os, true));

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
			while (_pos < _n && Cur != '\n') Advance();  // consume the trailing comment
			while (re > rs && (_s[re - 1] == ' ' || _s[re - 1] == '\t')) re--;
			if (re > rs) tokens.Add(Tok(TokenKind.Identifier, _s.Substring(rs, re - rs), rl, rc, rs, re - rs, true));
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
				tokens.Add(Tok(TokenKind.RemapTargetKey, tgt, sl, sc + sepLocal + 2, start + sepLocal + 2, tgt.Length, false));
				return true;
			}

			// Plain hotkey: token text is `trigger::` (through the separator); the rest of the line lexes normally.
			string trig = _s.Substring(start, afterSep - start);
			while (_pos < afterSep) Advance();
			tokens.Add(Tok(TokenKind.HotkeyTrigger, trig, sl, sc, start, afterSep - start, leadingWs));
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
			string trigTok = _s.Substring(start, afterSep - start);          // `:opts:trigger::`
			while (_pos < afterSep) Advance();
			tokens.Add(Tok(TokenKind.HotstringTrigger, trigTok, sl, sc, start, afterSep - start, leadingWs));

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
			while (_pos < _n && Cur != '\n') Advance();   // consume any trailing comment so it is not re-lexed
			// Trim a trailing run of whitespace (a preceding-comment delimiter or line padding).
			while (expEnd > expStart && (_s[expEnd - 1] == ' ' || _s[expEnd - 1] == '\t')) expEnd--;
			string exp = _s.Substring(expStart, expEnd - expStart);
			tokens.Add(Tok(TokenKind.HotstringExpansion, exp, el, ec, expStart, expEnd - expStart, false));
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
