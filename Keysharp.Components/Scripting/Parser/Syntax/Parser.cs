using System.Collections.Generic;
using Keysharp.Parsing.Lexing;

namespace Keysharp.Parsing.Syntax
{
	/// <summary>
	/// Hand-written recursive-descent / Pratt parser producing the strongly-typed AST.
	///
	/// Tracer-bullet scope: the full expression grammar (precedence taken from the authoritative
	/// AHK v2 source — bitwise binds tighter than comparison), calls, member/index
	/// access, fat-arrow lambdas, and the core statements (if/else, while, return, blocks,
	/// local/global/static decls, function definitions).
	///
	/// Deliberately deferred (later increments): implicit space-concatenation, dynamic vars
	/// (%...%), object/map literals, classes, switch/try/loop variants, hotkeys/directives,
	/// and no-parentheses command-style calls.
	/// </summary>
	internal sealed partial class Parser
	{
		private readonly List<Token> _t;
		private int _pos;
		private int _groupDepth;   // >0 inside ()/[] — newlines are insignificant
		private int _exprDepth;    // recursion guard for ParseExpression (defensive cap against malformed/unsupported input)
		private const int MaxExprDepth = 250;
		private bool _inDerefInner;   // true while parsing the inner expr of a %…% — a trailing '%' closes it, not name-building

		// Directives (e.g. #import) found in expression position, such as inside an object literal `{ #import … }`.
		// AHK processes directives per physical line regardless of nesting, so these are hoisted to program scope.
		private readonly List<Stmt> _hoistedStmts = new();
		private bool _inFlowCond;     // true while parsing a control-flow header — `(cond){…}` is the body, not an anon block fn
		// Preprocessor symbols for #if/#elif: the predefined set, plus whatever the caller supplied for this
		// compilation (`--define:NAME`, or Ks.RunScript's Defines option), and #define adds more. Seeded per Parser
		// and deliberately NOT inherited from an importing file — #include shares this Parser (so its #defines do
		// carry), but a module file gets its own, which is why the supplied set has to be passed down to those too.
		private readonly HashSet<string> _defines = new(System.StringComparer.OrdinalIgnoreCase)
		{
			"KEYSHARP"
#if WINDOWS
			, "WINDOWS"
#elif OSX
			, "OSX"
#elif LINUX
			, "LINUX"
#else
#error Unsupported platform symbol. Define exactly one of WINDOWS, LINUX, or OSX.
#endif
#if DEBUG
            , "DEBUG"
#endif
			// Exactly one architecture symbol - X64, ARM64, X86 or ARM - matching Ks.A_ProcessArch. This is a
			// runtime check rather than a C# #if because it must describe the process the script is being
			// compiled for and run in, which is what decides DllCall/ComCall calling conventions. A_PtrSize
			// cannot stand in for it: X64 and ARM64 are both 8.
			, ProcessArchitectureSymbol()
		};
		private Queue<IReadOnlyCollection<string>> _csharpDefineSnapshots;
		public readonly List<string> Diagnostics = new();

		private readonly string _includeDir;   // directory used to resolve relative #include paths (null => disabled)
		private readonly HashSet<string> _included = new(System.StringComparer.OrdinalIgnoreCase);   // #include dedup
		private int _includeDepth;             // current #include nesting depth (guards against circular #includeagain)
		private const int MaxIncludeDepth = 100;

		private static string ProcessArchitectureSymbol() => RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "X64",
			Architecture.Arm64 => "ARM64",
			Architecture.X86 => "X86",
			Architecture.Arm => "ARM",
			var architecture => architecture.ToString().ToUpperInvariant(),
		};

		// `defines` are the caller's extra preprocessor symbols for this compilation (null when there are none).
		public Parser(List<Token> tokens, string includeDir = null, IEnumerable<string> defines = null)
		{
			_includeDir = includeDir;

			if (defines != null)
				foreach (var d in defines) _ = _defines.Add(d);

			_t = Preprocess(tokens);
		}

		public static ProgramNode Parse(string source, string includeDir = null, string scriptFile = null) => ParseWithDiagnostics(source, includeDir, scriptFile).program;

		// Tokenizes + parses, returning the AST together with all lex + parse diagnostics (line:col: message).
		// scriptFile is the main script's full path (when known): it stamps the top-level tokens so %A_LineFile% in an
		// #include resolves to the real file (not just its directory) and main-file diagnostics name the file.
		public static (ProgramNode program, List<string> diagnostics) ParseWithDiagnostics(string source, string includeDir = null, string scriptFile = null, IEnumerable<string> defines = null)
		{
			var diags = new List<string>();
			try
			{
				var lexer = new Lexer(source, scriptFile);
				var tokens = LexForParsing(lexer);
				// A lex error (e.g. an unterminated string) terminates immediately — before parsing — with the first one.
				if (lexer.Diagnostics.Count > 0)
					throw new Keysharp.Builtins.ParseException(PrefixDiagnostic(scriptFile, lexer.Diagnostics[0]));
				var parser = new Parser(tokens, includeDir, defines);
				var prog = parser.ParseProgram();
				// Publish the parser-owned final symbol set without another copy.
				prog.Defines = parser._defines;
				return (prog, parser.Diagnostics);
			}
			catch (Keysharp.Builtins.ParseException ex)
			{
				diags.Add(ex.Message);
				return (new ProgramNode(new List<Stmt>()), diags);
			}
		}

		// Parses one expression, for a directive argument such as a `#HotIf` criterion. ParseProgram is the wrong
		// entry point: at statement level a trailing primary chain such as `obj.Member` is a zero-arg call
		// statement, so a criterion would be invoked rather than read. Returns null, with diagnostics, unless the
		// text is exactly one expression. It takes no preprocessor symbols because a directive argument is rebuilt
		// from already-preprocessed tokens.
		internal static (Expr expr, List<string> diagnostics) ParseExpressionWithDiagnostics(string source)
		{
			var diags = new List<string>();

			try
			{
				var lexer = new Lexer(source, null);
				var tokens = LexForParsing(lexer);

				if (lexer.Diagnostics.Count > 0)
					throw new Keysharp.Builtins.ParseException(lexer.Diagnostics[0]);

				var parser = new Parser(tokens, null);
				var expr = parser.ParseExpression(0);
				parser.SkipNewlines();

				if (!parser.At(TokenKind.EOF))
					parser.Error($"unexpected {parser.Got()} after the expression");

				return (parser.Diagnostics.Count == 0 ? expr : null, parser.Diagnostics);
			}
			catch (Keysharp.Builtins.ParseException ex)
			{
				diags.Add(ex.Message);
				return (null, diags);
			}
		}

		// ---- token helpers ----
		private Token Current => _pos < _t.Count ? _t[_pos] : _t[^1];
		private Token Peek(int k) => _pos + k < _t.Count ? _t[_pos + k] : _t[^1];
		private Token Advance() { var t = Current; if (_pos < _t.Count - 1) _pos++; return t; }
		private bool At(TokenKind k) => Current.Kind == k;
		private bool AtKeyword(string w) => Current.IsKeyword(w);
		private bool Match(TokenKind k) { if (At(k)) { Advance(); return true; } return false; }
		private void SkipNewlines() { while (Current.Kind == TokenKind.Newline) Advance(); }

		private Token Expect(TokenKind k, string ctx)
		{
			if (At(k)) return Advance();
			Error($"expected {Friendly(k)} in {ctx} but found {Got()}");
			return Current;
		}

		private string ExpectIdentifier(string ctx)
		{
			if (At(TokenKind.Identifier)) return Advance().Text;
			Error($"expected an identifier in {ctx} but found {Got()}");
			return "«error»";
		}

		// Reader-friendly names for tokens, used in diagnostics (so messages read "expected '}'" not "expected RBrace").
		private static string Friendly(TokenKind k) => k switch
		{
			TokenKind.RBrace => "'}'", TokenKind.LBrace => "'{'",
			TokenKind.RParen => "')'", TokenKind.LParen => "'('",
			TokenKind.RBracket => "']'", TokenKind.LBracket => "'['",
			TokenKind.Comma => "','", TokenKind.Colon => "':'", TokenKind.FatArrow => "'=>'",
			TokenKind.Newline => "end of line", TokenKind.EOF => "end of file",
			TokenKind.Identifier => "an identifier", TokenKind.Percent => "'%'",
			_ => k.ToString()
		};

		// Describes the current (unexpected) token for diagnostics.
		private string Got() => Current.Kind switch
		{
			TokenKind.EOF => "end of file",
			TokenKind.Newline => "end of line",
			_ => $"'{Current.Text}'"
		};

		// A member name after `.` — an identifier or a numeric index (`match.0` accesses property "0").
		private string ExpectMemberName(string ctx)
		{
			if (At(TokenKind.Identifier) || At(TokenKind.Number)) return Advance().Text;
			Error($"expected a member name in {ctx} but found {Got()}");
			return "«error»";
		}

		// Member access after a consumed `.`/`?.`. The member name is a run of adjacent parts: identifiers/numbers
		// (literal text) and `%expr%` derefs. A run containing any deref is DYNAMIC (`obj.%x%`, `obj.a%x%b`) and the
		// name is the parts concatenated (`Concat("a", x, "b")`); a single plain identifier/number is a static name.
		private Expr MakeMember(Expr target, bool nullConditional)
		{
			Expr nameExpr = null;       // accumulated name (concatenation) — used when dynamic
			string firstStatic = null;  // the lone identifier/number when the run is a single static part
			bool dynamic = false, first = true;
			while (true)
			{
				if (!first && Current.LeadingWhitespace) break;   // only adjacent parts continue the name
				Expr part;
				if (At(TokenKind.Percent) && !_inDerefInner)
				{   // a bare '%' opens a deref part — but ONLY at the top level. When we are already parsing the inner
					// expr of an enclosing %…% (e.g. the `a.b` in `obj.%a.b%`), a '%' is that deref's CLOSING delimiter,
					// not the start of a nested one, so we must leave it for the caller (mirrors ParsePrimary line ~1584).
					Advance();
					var savedDeref = _inDerefInner; _inDerefInner = true;
					part = ParseExpression(1);
					_inDerefInner = savedDeref;
					Expect(TokenKind.Percent, "dynamic member name"); dynamic = true;
				}
				else if (At(TokenKind.Identifier) || At(TokenKind.Number))
				{ var txt = Advance().Text; if (first) firstStatic = txt; part = new LiteralExpr(LiteralKind.String, "\"" + txt + "\""); }
				else break;
				nameExpr = nameExpr == null ? part : new BinaryExpr(".", nameExpr, part);
				first = false;
				if (Current.LeadingWhitespace || !(At(TokenKind.Identifier) || At(TokenKind.Number) || (At(TokenKind.Percent) && !_inDerefInner))) break;
			}
			if (nameExpr == null) { Error($"expected a member name in member access but found {Got()}"); return new MemberExpr(target, "«error»", nullConditional); }
			return dynamic ? new DynMemberExpr(target, nameExpr, nullConditional) : new MemberExpr(target, firstStatic, nullConditional);
		}

		// Keywords that can start a statement — used to disambiguate a trailing `?` (maybe operator) from a ternary.
		private static readonly System.Collections.Generic.HashSet<string> statementKeywords =
			new(System.StringComparer.OrdinalIgnoreCase)
			// NOTE: `throw` and `class` are intentionally excluded — both can appear as a ternary then-branch
			// (`cond ? throw(x) : y`, and `class` is a legal variable name: `cond ? class : y`), so a `?` followed
			// by them is a ternary, not the maybe operator.
			{ "if", "else", "while", "loop", "for", "switch", "try", "catch", "finally",
			  "break", "continue", "goto", "return", "local", "global", "static", "until" };
		private bool IsStatementKeyword() => At(TokenKind.Identifier) && statementKeywords.Contains(Current.Text);

		// Reserved words that cannot name a variable, function, or class (AHK "Reserved words"): the statement keywords
		// plus `case`. `throw` is excluded because it doubles as a function, and `class` is excluded because it is a
		// legal variable name (see the note above). Used where an identifier is consumed AS A NAME — member/method
		// names are unaffected (`obj.return` stays valid), since those go through a different path.
		private static readonly System.Collections.Generic.HashSet<string> reservedNames =
			new(statementKeywords, System.StringComparer.OrdinalIgnoreCase) { "case" };

		// Rejects a reserved word used where a name is expected (e.g. `x := return`, `a return` auto-concat, `class for`).
		private void RejectReservedName(string name, Token at, string role)
		{
			if (reservedNames.Contains(name))
				ErrorAt(at, $"'{name}' is a reserved word and cannot be used as {role}");
		}

		// A lex/parse error terminates parsing immediately by throwing (caught in ParseWithDiagnostics and surfaced as a
		// single diagnostic), so error-recovery can't silently produce a wrong AST or cascade bogus follow-on errors.
		private void Error(string msg) => ErrorAt(Current, msg);
		// Same, but pointing at a specific (already-consumed) token rather than the current one.
		private static string Diagnostic(Token token, string message) =>
			$"{(string.IsNullOrEmpty(token.File) ? "" : token.File + ":")}{token.Line}:{token.Column}: {message}";

		private static string PrefixDiagnostic(string file, string diagnostic) =>
			string.IsNullOrEmpty(file) ? diagnostic : $"{file}:{diagnostic}";

		private static void ErrorAt(Token t, string msg) =>
			throw new Keysharp.Builtins.ParseException(Diagnostic(t, msg));

		// ---- program / statements ----

		public ProgramNode ParseProgram()
		{
			var body = new List<Stmt>();
			SkipNewlines();
			while (!At(TokenKind.EOF))
			{
				var p = _pos;
				var s = ParseStatement();
				if (s != null) body.Add(s);
				// Drain directives lifted out of an object literal in this statement at THIS position, so they stay in
				// the correct #Module segment (appending at the end would misplace them into the last module).
				if (_hoistedStmts.Count > 0) { body.AddRange(_hoistedStmts); _hoistedStmts.Clear(); }
				if (_pos == p) Advance();  // guarantee progress on error
				SkipNewlines();
			}
			return new ProgramNode(body);
		}

		// Stamps every parsed statement with the source position of its first token, so diagnostics (e.g. #Warn
		// "unreachable") can report a line even for statements that carry no inner expression (break/continue/goto,
		// bare return, declarations). The file goes with it: line numbers are per-file, so a statement from an
		// #included file is only locatable when both travel together. Sub-parsers that already set a more specific
		// position are left as-is.
		private Stmt ParseStatement()
		{
			SkipNewlines();
			int line = Current.Line, col = Current.Column;
			var file = Current.File;
			var s = ParseStatementCore();
			if (s != null && s.Line == 0) { s.Line = line; s.Column = col; s.File = file; }
			return s;
		}

		private Stmt ParseStatementCore()
		{
			SkipNewlines();
			if (At(TokenKind.RemapSourceKey))
			{
				var src = Advance().Text;
				_ = Expect(TokenKind.DoubleColon, "'::' in a remap");
				return new RemapDef(src, Expect(TokenKind.RemapTargetKey, "remap target").Text);
			}
			if (At(TokenKind.HotkeyTrigger)) return ParseHotkey();
			if (At(TokenKind.HotstringTrigger)) return ParseHotstring();
			if (At(TokenKind.Hash)) return ParseDirective();
			if (At(TokenKind.LBrace)) return ParseBlock();
			if (AtKeyword("if")) return ParseIf();
			if (AtKeyword("while")) return ParseWhile();
			if (AtKeyword("loop")) return ParseLoop();
			if (AtKeyword("for")) return ParseFor();
			if (AtKeyword("switch")) return ParseSwitch();
			if (AtKeyword("try")) return ParseTry();
			if (AtKeyword("throw")) return ParseThrow();
			if (AtKeyword("break")) { Advance(); return new BreakStmt(ParseLoopJumpTarget()); }
			if (AtKeyword("continue")) { Advance(); return new ContinueStmt(ParseLoopJumpTarget()); }
			if (AtKeyword("goto")) return ParseGoto();
			if (AtKeyword("return")) return ParseReturn();
			if (AtKeyword("export") && Peek(1).Kind == TokenKind.Identifier) return ParseExport();
			if (AtKeyword("local") || AtKeyword("global") || AtKeyword("static")) return ParseDecl();
			if (AtKeyword("class") && Peek(1).Kind == TokenKind.Identifier) return ParseClass();
			if (AtKeyword("struct") && Peek(1).Kind == TokenKind.Identifier) return ParseClass(isStruct: true);
			// A label: `name:` alone on its own line. The colon must be ADJACENT to the name (no left whitespace) and
			// end the line — this distinguishes it from a ternary's `: arm` continued on its own line (`false :`).
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon && !Peek(1).LeadingWhitespace
				&& (Peek(2).Kind == TokenKind.Newline || Peek(2).Kind == TokenKind.EOF))
			{ var lbl = Advance().Text; Advance(); return new LabelStmt(lbl); }
			if (IsFunctionDefinition()) return ParseFunctionDecl();
			if (IsCommandStatement()) return ParseCommandCall();

			var e = ParseExpression(1);
			// A comma continues the statement as a sequence (`x := 1, y := 2`). The comma may also start the NEXT
			// line — a leading comma is a line continuation — so look past newlines before each comma.
			var commaSave = _pos;
			SkipNewlines();
			if (At(TokenKind.Comma))   // comma statement: `x := 1, y := 2` (possibly split across lines)
			{
				var items = new List<Expr> { e };
				while (true)
				{
					if (!Match(TokenKind.Comma)) break;
					SkipNewlines();
					items.Add(ParseExpression(1));
					var s2 = _pos;
					SkipNewlines();
					if (!At(TokenKind.Comma)) { _pos = s2; break; }
				}
				return new ExpressionStmt(new SequenceExpr(items));
			}
			_pos = commaSave;
			return new ExpressionStmt(e);
		}

		private Block ParseBlock()
		{
			Expect(TokenKind.LBrace, "block");
			var body = new List<Stmt>();
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				var p = _pos;
				body.Add(ParseStatement());
				// A directive lifted out of an object literal in this statement lands in THIS block (the nearest
				// enclosing function/class/accessor body), not at program scope — so `foo(){ x := { #import M } }`
				// scopes the import to foo. A top-level literal instead drains in ParseProgram (module scope).
				if (_hoistedStmts.Count > 0) { body.AddRange(_hoistedStmts); _hoistedStmts.Clear(); }
				if (_pos == p) Advance();
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, "block");
			return new Block(body);
		}

		private Stmt ParseBodyStatement()
		{
			SkipNewlines();
			return At(TokenKind.LBrace) ? ParseBlock() : ParseStatement();
		}

		// Parses a control-flow header expression: a trailing `(…){` is the flow body, not an anon block fn.
		private Expr ParseCondExpr()
		{
			var saved = _inFlowCond; _inFlowCond = true;
			var e = ParseExpression(1);
			_inFlowCond = saved;
			return e;
		}

		private Stmt ParseIf()
		{
			Advance(); // if
			var cond = ParseCondExpr();
			var then = ParseBodyStatement();
			Stmt els = null;
			var save = _pos;
			SkipNewlines();
			if (AtKeyword("else")) { Advance(); els = ParseBodyStatement(); }
			else _pos = save;
			return new IfStmt(cond, then, els);
		}

		private Stmt ParseWhile()
		{
			Advance();
			var cond = ParseCondExpr();
			var body = ParseBodyStatement();
			Expr until = null;
			var save = _pos;
			SkipNewlines();
			if (AtKeyword("until")) { Advance(); until = ParseCondExpr(); }
			else _pos = save;
			return new WhileStmt(cond, body, until) { Else = ParseOptionalLoopElse() };
		}

		// A loop may be followed by `else { … }` (or `else <stmt>`), which runs iff the loop body never executed.
		// Returns the else body or null (restoring position when no else is present).
		private Stmt ParseOptionalLoopElse()
		{
			var save = _pos;
			SkipNewlines();
			if (AtKeyword("else")) { Advance(); return ParseBodyStatement(); }
			_pos = save;
			return null;
		}

		private static readonly System.Collections.Generic.HashSet<string> loopSubKinds =
			new(System.StringComparer.OrdinalIgnoreCase) { "parse", "files", "read", "reg" };

		private Stmt ParseLoop()
		{
			Advance(); // loop
			// Specialized loops: `Loop Parse/Files/Read/Reg <arg>, <arg>, …`. The sub-keyword is an identifier
			// directly after Loop and not itself the loop count (it must be followed by an argument, '{' or EOL —
			// not by an operator like `:=`/`.`/`+`, which would mean it's a variable used as the count expression).
			if (At(TokenKind.Identifier) && loopSubKinds.Contains(Current.Text) && IsSpecialLoopFollow(Peek(1)))
			{
				var kind = Advance().Text.ToLowerInvariant();
				Match(TokenKind.Comma);   // the comma separating the sub-keyword from the first arg (`Loop Parse, X`)
				var args = new System.Collections.Generic.List<Expr>();
				while (!At(TokenKind.LBrace) && !At(TokenKind.Newline) && !At(TokenKind.EOF))
				{
					if (At(TokenKind.Comma)) { args.Add(null); Advance(); continue; }   // omitted arg
					args.Add(ParseCondExpr());   // header expr: a trailing `(…){` is the loop body, not an anon block fn
					if (!Match(TokenKind.Comma)) break;
				}
				var sbody = ParseBodyStatement();
				var ssave = _pos;
				SkipNewlines();
				var sl = new SpecialLoopStmt(kind, args, sbody);
				if (AtKeyword("until")) { Advance(); sl.Until = ParseCondExpr(); }
				else _pos = ssave;
				sl.Else = ParseOptionalLoopElse();
				return sl;
			}
			Expr count = (At(TokenKind.LBrace) || At(TokenKind.Newline) || At(TokenKind.EOF)) ? null : ParseCondExpr();
			var body = ParseBodyStatement();
			// Optional trailing `Until cond` (on its own line) turns the loop into a do-until.
			Expr lpUntil = null;
			var save = _pos;
			SkipNewlines();
			if (AtKeyword("until")) { Advance(); lpUntil = ParseCondExpr(); }
			else _pos = save;
			return new LoopStmt(count, body, lpUntil) { Else = ParseOptionalLoopElse() };
		}

		// True when the token after a Loop sub-keyword starts an argument list (or ends the header), i.e. the
		// sub-keyword is a real specialized-loop keyword rather than a variable used as the loop count.
		private static bool IsSpecialLoopFollow(Token t) => t.Kind switch
		{
			TokenKind.String or TokenKind.Number or TokenKind.Identifier or TokenKind.Percent
				or TokenKind.LParen or TokenKind.Comma or TokenKind.LBrace or TokenKind.Newline or TokenKind.EOF => true,
			_ => false,
		};

		private Stmt ParseFor()
		{
			Advance(); // for
			bool paren = Match(TokenKind.LParen);   // optional parens around the header: for (i, v in arr)
			var vars = new List<string> { ParseForVar() };
			while (Match(TokenKind.Comma)) vars.Add(ParseForVar());
			if (!AtKeyword("in")) Error($"expected 'in' in for loop but found {Got()}");
			else Advance();
			var enumerable = ParseCondExpr();
			if (paren) Expect(TokenKind.RParen, "for header");
			var forStmt = new ForStmt(vars, enumerable, ParseBodyStatement());
			var fsave = _pos;
			SkipNewlines();
			if (AtKeyword("until")) { Advance(); forStmt.Until = ParseCondExpr(); }
			else _pos = fsave;
			forStmt.Else = ParseOptionalLoopElse();
			return forStmt;
		}

		// A for-loop variable, or null if omitted (e.g. `for (, v in arr)` discards the first value).
		private string ParseForVar() => (At(TokenKind.Comma) || AtKeyword("in")) ? null : ExpectIdentifier("for variable");

		private Stmt ParseSwitch()
		{
			Advance(); // switch
			// The switch value is on the same line; a newline or '{' right after `switch` means a value-less switch.
			Expr value = null, caseSense = null;
			if (!At(TokenKind.Newline) && !At(TokenKind.LBrace))
			{
				value = ParseCondExpr();
				if (Match(TokenKind.Comma)) caseSense = ParseCondExpr();   // `switch v, caseSense` (still a header: `(…){` is the body)
			}
			SkipNewlines();
			Expect(TokenKind.LBrace, "switch body");
			var cases = new List<SwitchCase>();
			List<Stmt> defaultBody = null;
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				if (AtKeyword("case"))
				{
					Advance();
					var values = new List<Expr> { ParseExpression(1) };
					while (Match(TokenKind.Comma)) values.Add(ParseExpression(1));
					Expect(TokenKind.Colon, "case");
					cases.Add(new SwitchCase(values, ParseCaseBody()));
				}
				else if (AtKeyword("default"))
				{
					Advance();
					Expect(TokenKind.Colon, "default");
					defaultBody = ParseCaseBody();
				}
				else { Error($"expected 'case' or 'default' in switch but found {Got()}"); Advance(); }
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, "switch body");
			return new SwitchStmt(value, caseSense, cases, defaultBody);
		}

		// Statements up to the next case/default or closing brace.
		private List<Stmt> ParseCaseBody()
		{
			var body = new List<Stmt>();
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF) && !AtKeyword("case") && !AtKeyword("default"))
			{
				var p = _pos;
				body.Add(ParseStatement());
				if (_pos == p) Advance();
				SkipNewlines();
			}
			return body;
		}

		private Stmt ParseTry()
		{
			Advance(); // try
			var body = ParseBodyStatement();
			var catches = new List<CatchBlock>();
			Stmt elseBody = null, finallyBody = null;

			while (true)
			{
				var save = _pos;
				SkipNewlines();
				if (!AtKeyword("catch")) { _pos = save; break; }
				Advance(); // catch
				catches.Add(ParseCatchClause());
			}
			var save2 = _pos;
			SkipNewlines();
			if (AtKeyword("else")) { Advance(); elseBody = ParseBodyStatement(); save2 = _pos; SkipNewlines(); }
			if (AtKeyword("finally")) { Advance(); finallyBody = ParseBodyStatement(); }
			else _pos = save2;

			return new TryStmt(body, catches, elseBody, finallyBody);
		}

		// A catch clause after the `catch` keyword: optional type list (parenthesized or not) and optional `as`/trailing var.
		private CatchBlock ParseCatchClause()
		{
			var types = new List<string>();
			string var = null;
			if (Match(TokenKind.LParen))   // catch (Type1, Type2 as var) | catch (Type) | catch ()
			{
				while (!At(TokenKind.RParen) && !At(TokenKind.EOF))
				{
					if (AtKeyword("as")) { Advance(); var = ExpectIdentifier("catch variable"); break; }
					types.Add(ExpectIdentifier("catch type"));
					if (AtKeyword("as")) { Advance(); var = ExpectIdentifier("catch variable"); break; }
					if (!Match(TokenKind.Comma)) break;
				}
				Expect(TokenKind.RParen, "catch type list");
			}
			else if (At(TokenKind.Identifier) && !AtKeyword("as"))   // catch Type [, Type]* [as var | var]
			{
				types.Add(ExpectIdentifier("catch type"));
				while (Match(TokenKind.Comma)) { if (AtKeyword("as")) break; types.Add(ExpectIdentifier("catch type")); }
			}
			if (var == null)
			{
				if (AtKeyword("as")) { Advance(); var = ExpectIdentifier("catch variable"); }
				else if (At(TokenKind.Identifier)) var = ExpectIdentifier("catch variable");   // catch Type var (no 'as')
			}
			return new CatchBlock(types, var, ParseBodyStatement());
		}

		// #DirectiveName trailing-args  — the trailing text is consumed raw (directives use unquoted args).
		private Stmt ParseDirective()
		{
			// Positioned here rather than only in ParseStatement: a directive is also parsed straight from a class body
			// and from an object literal, and those callers would otherwise produce an unlocatable #Warning/#Error.
			int dirLine = Current.Line, dirCol = Current.Column;
			var dirFile = Current.File;
			Advance(); // #
			var name = At(TokenKind.Identifier) ? Advance().Text : "";

			// #CSharp carries either one verbatim block token or a quoted path.
			if (name.Equals("csharp", System.StringComparison.OrdinalIgnoreCase))
			{
				var cs = ParseCSharpDirective();
				cs.Line = dirLine;
				cs.Column = dirCol;
				cs.File = dirFile;
				cs.Defines = _csharpDefineSnapshots?.Count > 0 ? _csharpDefineSnapshots.Dequeue() : [];
				return cs;
			}

			var argToks = new List<Token>();
			var sb = new System.Text.StringBuilder();
			int brace = 0;   // a `{ … }` import list (e.g. #import "M" { a as b, … }) may span several lines
			while ((brace > 0 || !At(TokenKind.Newline)) && !At(TokenKind.EOF))
			{
				if (At(TokenKind.Newline)) { Advance(); continue; }
				if (At(TokenKind.LBrace)) brace++;
				else if (At(TokenKind.RBrace) && brace > 0) brace--;
				if (Current.LeadingWhitespace && sb.Length > 0) sb.Append(' ');
				argToks.Add(Current);
				sb.Append(Advance().Text);
			}
			var args = sb.ToString();
			Stmt dir;

			if (name.Equals("import", System.StringComparison.OrdinalIgnoreCase))
				dir = ParseImportDirective(args, argToks);
			else
			{
				ValidateDirectiveArgs(name, argToks);
				dir = new DirectiveStmt(name, args);
			}

			dir.Line = dirLine;
			dir.Column = dirCol;
			dir.File = dirFile;
			return dir;
		}

		// `#CSharp [options]` <block> | `#CSharp "file.cs"` | `#CSharp <Library>`.
		private CSharpDirective ParseCSharpDirective()
		{
			var opts = new System.Text.StringBuilder();
			string path = null;
			var library = false;

			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.CSharpBlock))
			{
				if (At(TokenKind.String))
				{
					var t = Advance();

					if (path != null)
						ErrorAt(t, "#CSharp accepts exactly one quoted .cs file path");
					else
						path = Unquote(t.Text);

					continue;
				}
				else if (At(TokenKind.Less))
				{
					var t = Advance();

					if (path != null)
						ErrorAt(t, "#CSharp accepts exactly one file path");

					var name = new System.Text.StringBuilder();

					while (!At(TokenKind.Greater) && !At(TokenKind.Newline) && !At(TokenKind.EOF))
					{
						var part = Advance();
						if (name.Length > 0 && part.LeadingWhitespace) _ = name.Append(' ');
						_ = name.Append(part.Text);
					}

					if (!Match(TokenKind.Greater) || name.Length == 0)
						ErrorAt(t, "#CSharp library form requires a name enclosed in < and >");

					path = name.ToString();
					library = true;
					continue;
				}

				if (opts.Length > 0) _ = opts.Append(' ');
				_ = opts.Append(Advance().Text);
			}

			if (At(TokenKind.CSharpBlock))
			{
				var blk = Advance();
				return new CSharpDirective(opts.ToString(), blk.Text, blk.Line);
			}

			if (path == null)
				Error("#CSharp requires a block terminated by #EndCSharp, a quoted .cs path, or a <Library> name");

			// The lowerer owns file resolution.
			return new CSharpDirective(opts.ToString(), null, 0) { FilePath = path, LibraryForm = library };
		}

		// Each directive accepts a fixed number of comma-separated arguments (taken from the AHK v2 source's
		// Script::IsDirective). The raw collector above happily swallows anything crammed onto the directive's line, so
		// reject excess arguments that would otherwise be silently dropped — e.g. `#HotIf 1, 2`, where only a single
		// expression is allowed (`#HotIf (1, 2)` passes one). `#import` has a richer shape and is parsed separately.
		private void ValidateDirectiveArgs(string name, List<Token> toks)
		{
			int maxArgs = MaxDirectiveArgs(name);
			if (maxArgs < 0 || toks.Count == 0) return;   // -1: the line is one literal value (path/message/options) — commas are literal

			// The lexer captures a few directives' arguments verbatim as a single token (Lexer.RawArgDirectives), so for
			// those any value-separating commas live inside the token text; all other directives are normally lexed and
			// separate their arguments with standalone Comma tokens (commas nested in ()/[]/{} or strings don't count).
			bool raw = Keysharp.Parsing.Lexing.Lexer.RawArgDirectives.Contains(name);
			int depth = 0, args = 1;
			bool excess = false;
			Token offender = default;
			foreach (var t in toks)
			{
				if (raw)
				{ foreach (var c in t.Text) if (c == ',' && ++args > maxArgs) { excess = true; offender = t; break; } }
				else switch (t.Kind)
				{
					case TokenKind.LParen: case TokenKind.LBracket: case TokenKind.LBrace: depth++; break;
					case TokenKind.RParen: case TokenKind.RBracket: case TokenKind.RBrace: if (depth > 0) depth--; break;
					case TokenKind.Comma when depth == 0: if (++args > maxArgs) { excess = true; offender = t; } break;
				}
				if (excess) break;
			}
			if (excess)
				ErrorAt(offender, $"#{name} accepts {(maxArgs == 1 ? "a single argument" : "at most " + maxArgs + " arguments")} " +
					"but received more (parenthesize a comma expression to pass it as one argument)");
		}

		// Parses `#import <module> [as <alias>] [{ name, … }]` into a structured node and rejects any trailing text —
		// AHK's ParseImportDirective fails on a second module (`#import "A", "B"`), a `} as X` after the list, or any
		// other leftover. This is the single authority on import-directive syntax; the lowerer consumes the fields.
		private ImportDirective ParseImportDirective(string args, List<Token> toks)
		{
			int i = 0;
			string module = "", alias = null, named = null;
			bool quoted = false;
			if (toks.Count == 0)
				return new ImportDirective(args, module, alias, named, quoted);   // nameless `#import` — a no-op for the lowerer
			if (toks[i].Kind == TokenKind.String) { quoted = true; module = Unquote(toks[i++].Text); }
			else if (toks[i].Kind == TokenKind.Identifier)
			{
				module = toks[i++].Text;
				// An unquoted module specifier may be a slash-separated relative path (`Lib/OCR`). Require adjacency
				// around each slash so ordinary trailing expressions such as `Mod / value` remain invalid directives.
				while (i + 1 < toks.Count
					&& toks[i].Kind == TokenKind.Slash && !toks[i].LeadingWhitespace
					&& toks[i + 1].Kind == TokenKind.Identifier && !toks[i + 1].LeadingWhitespace)
				{
					module += toks[i++].Text;
					module += toks[i++].Text;
				}
			}
			else ErrorAt(toks[i], $"expected a module name after #import but found '{toks[i].Text}'");
			if (i < toks.Count && toks[i].Kind == TokenKind.Identifier && toks[i].Text.Equals("as", System.StringComparison.OrdinalIgnoreCase))
			{
				i++;
				if (i < toks.Count && toks[i].Kind == TokenKind.Identifier) alias = toks[i++].Text;
				else ErrorAt(i < toks.Count ? toks[i] : toks[^1], "expected an alias name after 'as' in #import");
			}
			if (i < toks.Count && toks[i].Kind == TokenKind.LBrace)   // `{ name, name as alias, * }` — captured raw, split by the lowerer
			{
				var nb = new System.Text.StringBuilder();
				int depth = 0;
				for (; i < toks.Count; i++)
				{
					var t = toks[i];
					if (t.Kind == TokenKind.LBrace) { if (++depth == 1) continue; }
					else if (t.Kind == TokenKind.RBrace && --depth == 0) { i++; break; }
					if (nb.Length > 0 && t.LeadingWhitespace) nb.Append(' ');
					nb.Append(t.Text);
				}
				named = nb.ToString().Trim();
			}
			if (i < toks.Count)
				ErrorAt(toks[i], $"unexpected '{toks[i].Text}' after #import — a directive must be alone on its line");
			return new ImportDirective(args, module, alias, named, quoted);
		}

		// Strips a matching pair of surrounding quotes (' or ") from a string-token's text.
		private static string Unquote(string s) =>
			s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0] ? s.Substring(1, s.Length - 2) : s;

		// Maximum comma-separated arguments a directive accepts (at paren/bracket/brace depth 0). -1 means the rest of
		// the line is a single literal value (a path, message, version, or option string) where commas are literal
		// text, not argument separators. Values taken from the AHK v2 source (Script::IsDirective).
		private static int MaxDirectiveArgs(string name) => name.ToUpperInvariant() switch
		{
			"WARN" => 2,   // #Warn [Type], [Mode]
			"HOTIF" or "HOTIFTIMEOUT" or "INPUTLEVEL" or "CLIPBOARDTIMEOUT" or "MAXTHREADS"
				or "MAXTHREADSPERHOTKEY" or "MAXTHREADSBUFFER" or "SUSPENDEXEMPT" or "USEHOOK"
				or "SINGLEINSTANCE" or "STRUCTPACK" or "PERSISTENT" => 1,
			_ => -1,
		};

		// export [default] <function | class | variable-assignment> — marks a module export.
		private Stmt ParseExport()
		{
			Advance();   // 'export'
			bool isDefault = AtKeyword("default");
			if (isDefault) Advance();
			return new ExportStmt(isDefault, ParseStatement());
		}

		// hotkey : HotkeyTrigger (EOL HotkeyTrigger)* s* (functionDeclaration | statement)
		// Stacked trigger-only lines share the single following body (block, statement, or named function).
		private Stmt ParseHotkey()
		{
			var triggers = new List<string>();
			while (true)
			{
				triggers.Add(Advance().Text);          // HotkeyTrigger
				_ = Expect(TokenKind.DoubleColon, "'::' after a hotkey trigger");
				var save = _pos;
				SkipNewlines();
				if (At(TokenKind.HotkeyTrigger)) continue;
				_pos = save;
				break;
			}
			SkipNewlines();
			if (IsFunctionDefinition()) return new HotkeyDef(triggers, null, (FunctionDecl)ParseFunctionDecl());
			if (At(TokenKind.EOF) || At(TokenKind.HotkeyTrigger) || At(TokenKind.HotstringTrigger) || At(TokenKind.RemapSourceKey))
				return new HotkeyDef(triggers, new Block(new List<Stmt>()), null);
			return new HotkeyDef(triggers, ParseBodyStatement(), null);
		}

		// hotstring : HotstringTrigger (EOL HotstringTrigger)* WS* (Expansion | EOL? functionDeclaration | EOL? statement)
		private Stmt ParseHotstring()
		{
			var triggers = new List<string>();
			while (true)
			{
				triggers.Add(Advance().Text);          // HotstringTrigger
				_ = Expect(TokenKind.DoubleColon, "'::' after a hotstring trigger");
				if (At(TokenKind.HotstringExpansion))  // inline / continuation-section expansion
					return new HotstringDef(triggers, Advance().Text, null, null);
				var save = _pos;
				SkipNewlines();
				if (At(TokenKind.HotstringTrigger)) continue;
				_pos = save;
				break;
			}
			SkipNewlines();
			if (IsFunctionDefinition()) return new HotstringDef(triggers, null, null, (FunctionDecl)ParseFunctionDecl());
			if (At(TokenKind.EOF) || At(TokenKind.HotkeyTrigger) || At(TokenKind.HotstringTrigger) || At(TokenKind.RemapSourceKey))
				return new HotstringDef(triggers, "", null, null);
			return new HotstringDef(triggers, null, ParseBodyStatement(), null);
		}

		// ---- conditional-compilation preprocessor ----
		// Resolves #if/#elif/#else/#endif and #define/#undef on the token stream BEFORE parsing, so a directive can
		// split a single statement — the branches need not each be a whole statement:
		//     if (
		//     #if WINDOWS
		//         cond1
		//     #else
		//         cond2
		//     #endif
		//     )
		// Each directive still owns its own line, though: the condition is everything up to the newline, so the
		// one-line form (`if (#if WINDOWS cond1 #else cond2 #endif)`) does NOT work — the first directive swallows
		// the rest of the line. Only tokens in the active branch survive; the directive lines themselves are removed.
		private List<Token> Preprocess(List<Token> src) => Preprocess(src, _includeDir);

		// `includeDir` is the directory relative includes in THIS token stream resolve against (the main script dir, or
		// an #included file's own dir for its nested includes).
		private List<Token> Preprocess(List<Token> src, string includeDir)
		{
			var outp = new List<Token>(src.Count);
			// `dirIdx` indexes the opening #if's name token in `src`, so an unterminated block can point back at it
			// (an index, not the Token itself: this tuple is copied on every Emit() below); `seenElse` rejects a
			// duplicate #else and an #elif that follows one.
			var stack = new Stack<(bool parentActive, bool taken, bool active, bool seenElse, int dirIdx)>();
			// Every `active` pushed below already folds in its parent's, so the innermost frame answers for all of them.
			bool Emit() => stack.Count == 0 || stack.Peek().active;

			int i = 0;
			var curDir = includeDir;   // base dir for THIS file's relative includes; `#Include <dir>` updates it
			while (i < src.Count)
			{
				var t = src[i];
				// #include / #includeagain <file>: read the file, lex it, and splice its tokens in (recursing into the
				// outer loop handles nested includes). Plain #include dedups already-included files; #includeagain doesn't.
				if (_includeDir != null && t.Kind == TokenKind.Hash && i + 1 < src.Count && src[i + 1].Kind == TokenKind.Identifier
					&& (src[i + 1].Text.Equals("include", System.StringComparison.OrdinalIgnoreCase)
						|| src[i + 1].Text.Equals("includeagain", System.StringComparison.OrdinalIgnoreCase)) && Emit())
				{
					bool again = src[i + 1].Text.Equals("includeagain", System.StringComparison.OrdinalIgnoreCase);
					int j = i + 2;
					var fileToks = new List<Token>();
					while (j < src.Count && src[j].Kind != TokenKind.Newline && src[j].Kind != TokenKind.EOF) fileToks.Add(src[j++]);
					if (fileToks.Count == 0)   // continuation form: `#include` <nl> `(` file `)`
					{
						while (j < src.Count && src[j].Kind == TokenKind.Newline) j++;
						if (j < src.Count && src[j].Kind == TokenKind.LParen)
						{
							j++;
							while (j < src.Count && src[j].Kind != TokenKind.RParen && src[j].Kind != TokenKind.EOF)
							{ if (src[j].Kind != TokenKind.Newline) fileToks.Add(src[j]); j++; }
							if (j < src.Count && src[j].Kind == TokenKind.RParen) j++;
						}
					}
					var included = ResolveAndLexInclude(fileToks, again, curDir, t, out var includedDir, out var dirChange);
					// `#Include <dir>` names a directory rather than a file: it changes the base directory for the rest
					// of THIS file's relative includes and splices no content. Otherwise recursively preprocess the
					// included tokens against THEIR directory (so nested relative includes resolve correctly) and emit.
					if (dirChange != null) curDir = dirChange;
					else if (included != null && Emit())
					{
						// Depth-guard the recursion so a circular #includeagain (which, unlike #include, never dedups)
						// fails with a clean error instead of overflowing the stack and crashing the process.
						if (++_includeDepth > MaxIncludeDepth)
							throw new Keysharp.Builtins.ParseException(Diagnostic(t,
								"Too many nested #include directives (possible circular #includeagain)"));
						outp.AddRange(Preprocess(included, includedDir));
						_includeDepth--;
					}
					i = j;
					continue;
				}
				if (t.Kind == TokenKind.Hash && i + 1 < src.Count && src[i + 1].Kind == TokenKind.Identifier && IsCondDirective(src[i + 1].Text))
				{
					var nameTok = src[i + 1];
					var name = nameTok.Text;
					int j = i + 2;
					var cond = new List<Token>();
					while (j < src.Count && src[j].Kind != TokenKind.Newline && src[j].Kind != TokenKind.EOF) cond.Add(src[j++]);
					bool emit = Emit();
					// Every closing/continuing directive needs an open #if to act on; the message echoes what was written.
					void RequireOpenIf() { if (stack.Count == 0) ErrorAt(nameTok, $"#{name} without a matching #If"); }

					// #Else/#EndIf take no condition, and quietly dropping one is the very failure this grammar is
					// strict to avoid: `#else OSX` reads as "the OSX case" but compiles its block on every platform.
					void RejectCondition()
					{
						if (cond.Count > 0)
							ErrorAt(cond[0], $"#{name} takes no condition"
								   + (name.Equals("else", System.StringComparison.OrdinalIgnoreCase) ? " (did you mean #ElIf?)" : ""));
					}

					// EvalCond is called before being AND-ed rather than short-circuited into the result, so a dead
					// branch's condition is still validated — a malformed one is then caught on every platform, not
					// only on the platform that happens to take that branch.
					if (name.Equals("if", System.StringComparison.OrdinalIgnoreCase))
					{ bool c = EvalCond(cond, nameTok); bool v = emit && c; stack.Push((emit, v, v, false, i + 1)); }
					else if (name.Equals("elif", System.StringComparison.OrdinalIgnoreCase))
					{
						RequireOpenIf();
						var f = stack.Pop();
						if (f.seenElse) ErrorAt(nameTok, "#ElIf after #Else");
						bool c = EvalCond(cond, nameTok); bool v = f.parentActive && !f.taken && c;
						stack.Push((f.parentActive, f.taken || v, v, false, f.dirIdx));
					}
					else if (name.Equals("else", System.StringComparison.OrdinalIgnoreCase))
					{
						RequireOpenIf();
						RejectCondition();
						var f = stack.Pop();
						if (f.seenElse) ErrorAt(nameTok, "duplicate #Else");
						stack.Push((f.parentActive, true, f.parentActive && !f.taken, true, f.dirIdx));
					}
					else if (name.Equals("endif", System.StringComparison.OrdinalIgnoreCase))
					{
						RequireOpenIf();
						RejectCondition();
						stack.Pop();
					}
					else if (name.Equals("define", System.StringComparison.OrdinalIgnoreCase))
					{ if (emit && cond.Count > 0 && cond[0].Kind == TokenKind.Identifier) _defines.Add(cond[0].Text); }
					else if (name.Equals("undef", System.StringComparison.OrdinalIgnoreCase))
					{ if (emit && cond.Count > 0 && cond[0].Kind == TokenKind.Identifier) _defines.Remove(cond[0].Text); }
					i = j;   // leave the trailing newline to be emitted normally, preserving line separation
					continue;
				}
				if (Emit())
				{
					if (t.Kind == TokenKind.Hash && i + 1 < src.Count && src[i + 1].IsKeyword("csharp"))
						(_csharpDefineSnapshots ??= new()).Enqueue([.. _defines]);

					outp.Add(t);
				}
				i++;
			}
			if (stack.Count > 0)   // innermost unterminated block; without this its excluded region silently eats the rest of the file
			{
				var open = src[stack.Peek().dirIdx];
				ErrorAt(open, $"#{open.Text} without a matching #EndIf");
			}
			// The parser indexes off the trailing EOF token, so a stream that conditionals emptied still needs it.
			// (Returning `src` instead would resurrect every token the conditionals just excluded — and an #included
			// stream carries no EOF, so leaving it genuinely empty is the right answer there.)
			if (outp.Count == 0 && src.Count > 0 && src[^1].Kind == TokenKind.EOF) outp.Add(src[^1]);
			return outp;
		}

		// Reconstructs the include filename from its tokens, expands %BuiltInVar% references, and resolves it against
		// the include dir, returning the included file's tokens (minus EOF). Returns null with `dirChange` set for the
		// `#Include <dir>` directory form, and null for a deduped or *i-ignored file. A missing file/dir WITHOUT the
		// *i flag throws, so a broken #include fails loudly instead of silently doing nothing.
		private List<Token> ResolveAndLexInclude(List<Token> fileToks, bool again, string baseDir, Token directive, out string includedDir, out string dirChange)
		{
			includedDir = baseDir;
			dirChange = null;
			if (fileToks.Count == 0) return null;
			string file;
			if (fileToks.Count == 1 && fileToks[0].Kind == TokenKind.String && fileToks[0].Text.Length >= 2)
				file = fileToks[0].Text.Substring(1, fileToks[0].Text.Length - 2);   // strip quotes
			else
			{
				var sb = new System.Text.StringBuilder();
				for (int k = 0; k < fileToks.Count; k++) { if (k > 0 && fileToks[k].LeadingWhitespace) sb.Append(' '); sb.Append(fileToks[k].Text); }
				file = sb.ToString();
			}
			file = file.Trim();
			// An optional leading "*i " flag means "ignore the file if it cannot be read" — only then is a missing
			// include silent. Strip exactly the flag, never arbitrary leading i/I/* characters (which would corrupt a
			// filename such as "IncludeFile.ks" -> "ncludeFile.ks").
			bool ignoreMissing = false;
			if (file.StartsWith("*i", System.StringComparison.OrdinalIgnoreCase)) { ignoreMissing = true; file = file.Substring(2).TrimStart(); }
			string path;
			// Library form: `#include <Name>` searches the Lib folders for Name.ks/.ahk (no %var% expansion — AHK
			// disallows variable references here). Everything else expands %BuiltInVar% and resolves against the
			// including file's directory (baseDir) so nested relative includes work; %A_ScriptDir% is the MAIN dir.
			if (file.Length >= 2 && file[0] == '<' && file[^1] == '>')
			{
				path = FindLibraryFile(file.Substring(1, file.Length - 2).Trim());
				if (path == null)
				{
					if (ignoreMissing) return null;
					throw new Keysharp.Builtins.ParseException(Diagnostic(directive, $"#Include library not found: {file}"));
				}
			}
			else
			{
				// Built-in variables enclosed in percent signs are expanded (e.g. "%A_ScriptDir%\Lib"); a "%name%" that
				// is not a recognized built-in is interpreted literally, matching AutoHotkey.
				file = NormalizeDirectiveSeparators(ExpandIncludeVars(file, directive));   // '\' separates on every platform
				try { path = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(baseDir, file)); }
				catch { if (ignoreMissing) return null; throw IncludeNotFound(directive, file); }
				// Directory form: change the base dir used by the rest of this file's relative includes (no content spliced).
				if (System.IO.Directory.Exists(path)) { dirChange = path; return null; }
			}
			if (!again && !_included.Add(path)) return null;   // already included (plain #include dedups)
			else if (again) _included.Add(path);
			if (!System.IO.File.Exists(path)) { if (ignoreMissing) return null; throw IncludeNotFound(directive, path); }
			includedDir = System.IO.Path.GetDirectoryName(path);   // nested includes in this file resolve against its dir
			// The lexer stamps each token with this file's full path for diagnostics and A_LineFile.
			var lexer = new Lexing.Lexer(System.IO.File.ReadAllText(path), path);
			var toks = LexForParsing(lexer);
			// A lex error in the included file terminates immediately, reported against that file.
			if (lexer.Diagnostics.Count > 0)
				throw new Keysharp.Builtins.ParseException(PrefixDiagnostic(path, lexer.Diagnostics[0]));
			if (toks.Count > 0 && toks[^1].Kind == TokenKind.EOF) toks.RemoveAt(toks.Count - 1);   // drop the included EOF
			toks.Insert(0, new Token(TokenKind.Newline, "\n", 0, 0, 0, 0, true, path));   // keep line separation from the host
			return toks;
		}

		/// <summary>
		/// Lexes and drops the trivia the lossless lexer emits for other consumers. Every path into the parser — the
		/// main file and each <c>#Include</c>d one — must come through here, or trivia reaches the parser.
		/// RemoveAll compacts in place; a <c>Where(…).ToList()</c> copies the whole list and costs more than the
		/// trivia it removes.
		/// </summary>
		private static List<Token> LexForParsing(Lexing.Lexer lexer)
		{
			var tokens = lexer.Tokenize();
			_ = tokens.RemoveAll(token => token.Kind is TokenKind.Comment or TokenKind.Directive
				or TokenKind.ContinuationDelimiter);
			return tokens;
		}

		// A "line:col: message" parse error for a #include whose target can't be found, attributed to the directive's
		// position (ToCompilerError parses the line:col prefix so the user is pointed at the offending line).
		private static Keysharp.Builtins.ParseException IncludeNotFound(Token directive, string target) =>
			new(Diagnostic(directive, $"#Include file not found: {target}"));

		// File extensions tried for a library include, Keysharp-native first then AHK-compatible.
		private static readonly string[] libExts = { ".ks", ".ahk" };

		// Resolves `#include <Name>` to a Lib-folder file, mirroring AutoHotkey's search order: local (A_ScriptDir\Lib),
		// the user libs (Documents\Keysharp\Lib then Documents\AutoHotkey\Lib), then the standard lib next to the
		// executable (<exe>\Lib). Each directory is tried for Name.ks/.ahk; if nothing matches and Name contains an
		// underscore, the part before the FIRST underscore is tried too (so <lib_func> resolves to lib). Returns the
		// full path of the first existing candidate, or null.
		private string FindLibraryFile(string name) => FindLibraryFile(name, _includeDir, libExts);

		// Shared with #CSharp <Name>, whose library files use the same directories and fallback rule but a .cs extension.
		internal static string FindLibraryFile(string name, string scriptDir, IReadOnlyList<string> extensions)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			// A library name may carry a subdirectory (`<Aris/Author/Lib>`, `<Lib\UIA>`); both separators work anywhere.
			name = NormalizeDirectiveSeparators(name);

			var libDirs = new List<string>();

			if (!string.IsNullOrEmpty(scriptDir))
				libDirs.Add(System.IO.Path.Combine(scriptDir, "Lib"));

			string docs = null, exeDir = null;
			// The accessor, not GetFolderPath: an unverified Documents path still counts (see A_MyDocuments).
			try { docs = Keysharp.Builtins.Accessors.A_MyDocuments; } catch { }
			try { exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath); } catch { }

			if (!string.IsNullOrEmpty(docs))
			{
				libDirs.Add(System.IO.Path.Combine(docs, "Keysharp", "Lib"));
				libDirs.Add(System.IO.Path.Combine(docs, "AutoHotkey", "Lib"));
			}

			if (!string.IsNullOrEmpty(exeDir))
				libDirs.Add(System.IO.Path.Combine(exeDir, "Lib"));

			// AHK searches every Lib dir for the full name before falling back to the underscore-truncated name.
			var candidates = new List<string> { name };
			var us = name.IndexOf('_');

			if (us > 0)
				candidates.Add(name.Substring(0, us));

			foreach (var candidate in candidates)
				foreach (var dir in libDirs)
					foreach (var ext in extensions)
					{
						string p;
						try { p = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, candidate + ext)); }
						catch { continue; }   // invalid chars in the name -> not a match

						if (System.IO.File.Exists(p))
							return p;
					}

			return null;
		}

		// Built-in variables AutoHotkey permits inside an #include path (the compile-time set). Restricting to this
		// list keeps parsing from invoking accessors with side effects or runtime-only state.
		private static readonly HashSet<string> includePathVars = new(System.StringComparer.OrdinalIgnoreCase)
		{
			"A_AhkPath", "A_AppData", "A_AppDataCommon", "A_ComputerName", "A_ComSpec", "A_Desktop", "A_DesktopCommon",
			"A_IsCompiled", "A_LineFile", "A_MyDocuments", "A_ProgramFiles", "A_Programs", "A_ProgramsCommon",
			"A_ScriptDir", "A_ScriptFullPath", "A_ScriptName", "A_Space", "A_StartMenu", "A_StartMenuCommon",
			"A_Startup", "A_StartupCommon", "A_Tab", "A_Temp", "A_UserName", "A_WinDir"
		};

		// Expands %BuiltInVar% references in an #include path. A_ScriptDir is the main-script dir; A_LineFile is the
		// directive's own file. Delegates to the shared ExpandPathVars (also used by the #import search path).
		private string ExpandIncludeVars(string s, Token directive) => ExpandPathVars(s, _includeDir, directive.File ?? _includeDir);

		// Expands %BuiltInVar% references in a path string. A_ScriptDir/A_LineFile are supplied by the caller (the parse
		// context); the rest come from the Accessors built-ins (restricted to includePathVars). An unrecognized %name%
		// (or one that can't be resolved) is left verbatim, matching AutoHotkey's "interpreted literally" rule.
		internal static string ExpandPathVars(string s, string scriptDir, string lineFile)
		{
			if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
			return ExpandPathRegEx().Replace(s, m =>
			{
				var name = m.Groups[1].Value;
				if (!includePathVars.Contains(name)) return m.Value;
				if (name.Equals("A_ScriptDir", System.StringComparison.OrdinalIgnoreCase)) return scriptDir ?? m.Value;
				if (name.Equals("A_LineFile", System.StringComparison.OrdinalIgnoreCase)) return lineFile ?? m.Value;
				try
				{
					var prop = typeof(Keysharp.Builtins.Accessors).GetProperty(name,
						System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
					return prop?.GetValue(null)?.ToString() ?? m.Value;
				}
				catch { return m.Value; }
			});
		}

		/// <summary>
		/// Maps the path separators of a compile-time directive path (<c>#Include</c>, <c>#Import</c>, <c>#CSharp</c>)
		/// onto the host's. Such a path is a literal written by the script author and never carries user data, so the
		/// separator set is fixed by the LANGUAGE — '\' and '/' both separate components on every platform — rather
		/// than by the host filesystem. Without this, an AHK-ecosystem `#Include Lib\Thing.ahk` resolves on Unix to a
		/// single file whose NAME contains a backslash and fails. Windows needs no mapping: Path.GetFullPath already
		/// folds '/' to '\'. The cost is that a Unix file with a literal '\' in its name cannot be named by a
		/// directive (rename it, or read it at run time), which is the better trade — a botched unzip of a
		/// Windows-authored archive is the one realistic way such a file appears, and resolving to it is never meant.
		/// <paramref name="sep"/> defaults to the host separator and exists so the Unix mapping stays testable from a
		/// Windows host.
		/// </summary>
		internal static string NormalizeDirectiveSeparators(string path, char sep = '\0')
		{
			if (sep == '\0') sep = System.IO.Path.DirectorySeparatorChar;
			return sep == '\\' || string.IsNullOrEmpty(path) || path.IndexOf('\\') < 0 ? path : path.Replace('\\', sep);
		}

		private static bool IsCondDirective(string name) =>
			name.Equals("if", System.StringComparison.OrdinalIgnoreCase) || name.Equals("elif", System.StringComparison.OrdinalIgnoreCase)
			|| name.Equals("else", System.StringComparison.OrdinalIgnoreCase) || name.Equals("endif", System.StringComparison.OrdinalIgnoreCase)
			|| name.Equals("define", System.StringComparison.OrdinalIgnoreCase) || name.Equals("undef", System.StringComparison.OrdinalIgnoreCase);

		// Evaluates an #if/#elif condition: defined symbols, true/false, integer literals, !, &&/and, ||/or, and
		// parentheses. Anything outside that grammar is a hard error rather than a silent `false` — `#if defined(X)`
		// and `#if X == 1` read as though they work (they do in C), but there is no defined() and no comparison
		// operator here, so dropping the tokens quietly would take the wrong branch with nothing to notice it by.
		private bool EvalCond(List<Token> toks, Token dir)
		{
			if (toks.Count == 0) ErrorAt(dir, $"#{dir.Text} requires a condition");
			int p = 0;
			_condDepth = 0;
			var v = EvalCondOr(toks, ref p, dir);
			if (p < toks.Count) ErrorAt(toks[p], CondErr(dir, toks[p]));
			return v;
		}

		// Recursion guard for the condition grammar, mirroring MaxExprDepth for expressions. Conditions are evaluated
		// even in excluded branches, so without this a deeply parenthesized one anywhere in a file — including in dead
		// code the script never uses — overflows the stack, and a StackOverflowException cannot be caught and turned
		// into a diagnostic: the whole process dies with nothing the user can act on.
		private int _condDepth;
		private const int MaxCondDepth = 250;

		private static string CondErr(Token dir, Token t) => $"unexpected '{t.Text}' in the #{dir.Text} condition "
			+ "(only defined symbols, true/false, integers, !, &&, ||, and parentheses are supported)";

		private bool EvalCondOr(List<Token> t, ref int p, Token dir)
		{
			var v = EvalCondAnd(t, ref p, dir);
			while (p < t.Count && (t[p].Kind == TokenKind.LogicalOr || t[p].IsKeyword("or"))) { p++; v = EvalCondAnd(t, ref p, dir) || v; }
			return v;
		}

		private bool EvalCondAnd(List<Token> t, ref int p, Token dir)
		{
			var v = EvalCondUnary(t, ref p, dir);
			while (p < t.Count && (t[p].Kind == TokenKind.LogicalAnd || t[p].IsKeyword("and"))) { p++; v = EvalCondUnary(t, ref p, dir) && v; }
			return v;
		}

		private bool EvalCondUnary(List<Token> t, ref int p, Token dir)
		{
			if (p >= t.Count) { ErrorAt(dir, $"incomplete #{dir.Text} condition"); return false; }
			if (++_condDepth > MaxCondDepth) ErrorAt(t[p], $"#{dir.Text} condition is nested too deeply");

			try
			{
				if (t[p].Kind == TokenKind.Not || t[p].IsKeyword("not")) { p++; return !EvalCondUnary(t, ref p, dir); }
				if (t[p].Kind == TokenKind.LParen)
				{
					p++;
					var v = EvalCondOr(t, ref p, dir);
					if (p < t.Count && t[p].Kind == TokenKind.RParen) p++;
					else ErrorAt(dir, $"missing ')' in the #{dir.Text} condition");
					return v;
				}
				// A numeric literal is false only when its VALUE is zero: comparing the text against "0" made 0x0 and
				// 00 true. Parsed invariantly because this is source text, and a literal too large for the type fails
				// to parse — which is certainly not zero, hence the leading `!`.
				if (t[p].Kind == TokenKind.Number)
				{
					var r = t[p++].Text;
					return r.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
						   ? !long.TryParse(r.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) || hex != 0
						   : !double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) || num != 0;
				}

				if (t[p].Kind == TokenKind.Identifier)
				{
					// A defined symbol wins over the true/false literals. Symbols are case-insensitive here, so
					// `#define FALSE` names the same thing as the literal `false`; letting the literal win would
					// silently flip a branch that used to be taken. Only when nothing defines the name do they read
					// as the value keywords they are everywhere else, so `#if true` keeps its block.
					var s = t[p++].Text;
					return _defines.Contains(s) || s.Equals(Keywords.TrueTxt, System.StringComparison.OrdinalIgnoreCase);
				}

				ErrorAt(t[p], CondErr(dir, t[p]));
				return false;
			}
			finally { _condDepth--; }
		}

		private Stmt ParseThrow()
		{
			Advance(); // throw
			if (At(TokenKind.Newline) || At(TokenKind.EOF) || At(TokenKind.RBrace)) return new ThrowStmt(null);
			var tvalue = ParseExpression(1);
			RejectMultiParam("Throw");
			return new ThrowStmt(tvalue);
		}

		private Stmt ParseReturn()
		{
			Advance();
			if (At(TokenKind.Newline) || At(TokenKind.EOF) || At(TokenKind.RBrace))
				return new ReturnStmt(null);
			var rvalue = ParseExpression(1);
			RejectMultiParam("Return");
			return new ReturnStmt(rvalue);
		}

		// `return a, b` / `throw a, b` is invalid — both accept a single expression. AHK rejects it with
		// "Return/Throw accepts at most 1 parameter."; without this guard the trailing comma is silently
		// re-parsed as a following statement, which (when the file is #included) swallows the code after it.
		// Parenthesize to pass a comma expression as one value: `return (a, b)`.
		private void RejectMultiParam(string keyword)
		{
			if (At(TokenKind.Comma))
				Error($"\"{keyword}\" accepts at most 1 parameter.");
		}

		// Optional `break`/`continue` target on the same line: a loop level (number) or a source label name.
		private string ParseLoopJumpTarget()
		{
			if (At(TokenKind.Number) || At(TokenKind.Identifier)) return Advance().Text;
			return null;
		}

		// `Goto Label` / `Goto(Label)` / `Goto, Label` — the destination label name.
		private Stmt ParseGoto()
		{
			Advance(); // goto
			Match(TokenKind.Comma);
			bool paren = Match(TokenKind.LParen);
			var target = ExpectIdentifier("goto target");
			if (paren) Expect(TokenKind.RParen, "goto target");
			return new GotoStmt(target);
		}

		private Stmt ParseDecl()
		{
			var kw = Advance().Text.ToLowerInvariant();
			// `static name(...) {…}` / `static name(...) => expr` is a (static, non-capturing) nested function definition.
			if (IsFunctionDefinition()) return ParseFunctionDecl(isStatic: kw == "static");
			var items = new List<Expr>();
			// A decl list continues across lines on a comma — trailing (`X := 1,` ⏎ `Y`) OR leading (`X := 1` ⏎ `, Y`).
			// `global`/`local` alone (assume-global/local) has no items and must not eat the next statement.
			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RBrace))
			{
				items.Add(ParseExpression(1));
				var save = _pos;
				SkipNewlines();
				if (At(TokenKind.Comma)) { Advance(); SkipNewlines(); continue; }
				_pos = save;
				break;
			}
			return new DeclStmt(kw, items);
		}

		private Stmt ParseClass(bool isStruct = false)
		{
			Advance(); // class / struct
			var nameTok = Current;
			var name = ExpectIdentifier(isStruct ? "struct name" : "class name");
			RejectReservedName(name, nameTok, isStruct ? "a struct name" : "a class name");
			string baseName = null;
			if (AtKeyword("extends"))
			{
				Advance();
				baseName = ExpectIdentifier("base class");
				while (At(TokenKind.Dot)) { Advance(); baseName += "." + ExpectIdentifier("base class"); }  // dotted base e.g. Gui.Control
			}
			SkipNewlines();   // `class Name` and `{` are often on separate lines
			Expect(TokenKind.LBrace, "class body");

			var fields = new List<ClassField>();
			var methods = new List<ClassMethod>();
			var properties = new List<ClassProperty>();
			var nested = new List<ClassDecl>();
			var staticInits = new List<Stmt>();     // `static x.y := z` member/index-target static initializers
			var instanceInits = new List<Stmt>();   // `x.y := z` member/index-target instance initializers
			var classImports = new List<ImportDirective>();   // `#import` directives → class-scoped bindings (Lowerer)
			string classRequires = null;
			List<CSharpDirective> classCSharp = null;
			long structPack = 0;   // #StructPack alignment in effect for subsequent typed fields (0 = default)
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				var p = _pos;
				// A directive in the class body (e.g. `#Requires AutoHotkey v2.1` sets the class's compatibility mode).
				if (At(TokenKind.Hash))
				{
					var dir = (DirectiveStmt)ParseDirective();
					if (dir.Name.Equals("Requires", System.StringComparison.OrdinalIgnoreCase)) classRequires = dir.Args;
					// #StructPack [1|2|4|8] sets the max alignment for subsequent typed fields (0/omitted resets to default).
					else if (dir.Name.Equals("StructPack", System.StringComparison.OrdinalIgnoreCase))
						structPack = long.TryParse((dir.Args ?? "").Trim(), out var sp) ? sp : 0;
					// A class-body `#import` scopes its bindings to this class (retained for the Lowerer instead of
					// being silently discarded).
					else if (dir is ImportDirective imp) classImports.Add(imp);
					// Keep class members with their declaring class.
					else if (dir is CSharpDirective cs) (classCSharp ??= []).Add(cs);
					else if (dir.Name.Equals("Module", System.StringComparison.OrdinalIgnoreCase))
						Error("#Module is only allowed at the top level, not inside a class body");
					// Everything else is hoisted to program scope rather than dropped, exactly as a directive found in
					// expression position is. A class body has nowhere to put a statement, but the directive still has
					// to be SEEN: dropping it silently discarded a #Warning or #Error written to be read, and let a
					// misspelled directive through in the one place the "unrecognized directive" check could not reach.
					else _hoistedStmts.Add(dir);
					SkipNewlines();
					continue;
				}
				// A nested class/struct declaration (`class Inner { … }` / `struct Inner { … }`).
				if ((AtKeyword("class") || AtKeyword("struct")) && Peek(1).Kind == TokenKind.Identifier)
				{
					if (ParseClass(isStruct: AtKeyword("struct")) is ClassDecl ncd) nested.Add(ncd);
					SkipNewlines();
					continue;
				}
				var isStatic = AtKeyword("static");
				if (isStatic) Advance();

				if (IsFunctionDefinition())
				{
					var mname = Advance().Text;
					var ps = ParseParamList();
					SkipNewlines();
					if (Match(TokenKind.FatArrow))
						methods.Add(new ClassMethod(mname, ps, null, ParseExpression(1), isStatic));
					else
						methods.Add(new ClassMethod(mname, ps, ParseBlock(), null, isStatic));
				}
				else if (At(TokenKind.Identifier))
				{
					// A single line may declare several comma-separated fields that share its static/instance
					// scope, e.g. `a := 1, b := 2`, `static x := 1, y := 2`, or `x : Int32, y : Int32`. A `{…}`
					// property body or `=>` shorthand getter is a complete member on its own and never continues
					// past a comma. A leading comma on the next line continues the run (as with comma statements).
					while (true)
					{
						var fname = Advance().Text;
						var indexParams = At(TokenKind.LBracket) ? ParseParamList(TokenKind.LBracket, TokenKind.RBracket) : new List<Param>();
						var save = _pos;
						SkipNewlines();
						if (At(TokenKind.LBrace))   // property body, possibly with the brace on the next line
						{
							properties.Add(ParsePropertyBody(fname, indexParams, isStatic));
							break;
						}
						_pos = save;   // field initializer / shorthand getter are on the same line
						if (At(TokenKind.Dot) && indexParams.Count == 0)   // member-target init: `static Template.Framework := X`
						{
							var target = ParsePostfix(new MemberExpr(new NameExpr("this"), fname, false));
							if (!Match(TokenKind.Assign)) Error($"expected ':=' in class member initializer but found {Got()}");
							var stmt = new ExpressionStmt(new AssignExpr(":=", target, ParseExpression(1)));
							(isStatic ? staticInits : instanceInits).Add(stmt);
						}
						else if (Match(TokenKind.FatArrow))
						{
							properties.Add(new ClassProperty(fname, indexParams, null, ParseExpression(1), null, null, isStatic));
							break;
						}
						else if (At(TokenKind.Colon))   // typed struct field `name : Type [:= init]`
						{
							Advance();
							// v2.1-alpha.30 permits a parenthesized expression, or a name followed by calls,
							// property access and indexing. It is evaluated in the class's static initializer
							// with `this` bound to the class object.
							Expr typeExpr;
							if (At(TokenKind.LParen))
								typeExpr = ParsePostfix(ParsePrimary());
							else
								typeExpr = ParsePostfix(new NameExpr(ExpectIdentifier("struct field type")));
							Expr tinit = Match(TokenKind.Assign) ? ParseExpression(1) : null;
							fields.Add(new ClassField(fname, tinit, isStatic, typeExpr, structPack));
						}
						else
						{
							Expr init = Match(TokenKind.Assign) ? ParseExpression(1) : null;
							fields.Add(new ClassField(fname, init, isStatic));
						}
						// Another comma-separated field may follow on this or the next line (a leading comma
						// continues the declaration). Look past newlines for the comma, then for the next name.
						var commaSave = _pos;
						SkipNewlines();
						if (!Match(TokenKind.Comma)) { _pos = commaSave; break; }
						SkipNewlines();
						if (!At(TokenKind.Identifier)) { _pos = commaSave; break; }
					}
				}
				else
				{
					Error($"unexpected token in class body: '{Current.Text}'");
				}

				if (_pos == p) Advance();
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, isStruct ? "struct body" : "class body");
			return new ClassDecl(name, baseName, fields, methods, properties, nested, isStruct)
			{ Requires = classRequires, Imports = classImports, StaticInit = staticInits, InstanceInit = instanceInits, CSharpBlocks = classCSharp };
		}

		// Property body: { get [=> expr | { ... }]  set [=> expr | { ... }] }
		private ClassProperty ParsePropertyBody(string name, List<Param> indexParams, bool isStatic)
		{
			Expect(TokenKind.LBrace, "property");
			Block getBody = null, setBody = null;
			Expr getArrow = null, setArrow = null;
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				var p = _pos;
				if (AtKeyword("get")) { Advance(); if (Match(TokenKind.FatArrow)) getArrow = ParseExpression(1); else { SkipNewlines(); getBody = ParseBlock(); } }
				else if (AtKeyword("set")) { Advance(); if (Match(TokenKind.FatArrow)) setArrow = ParseExpression(1); else { SkipNewlines(); setBody = ParseBlock(); } }
				else Error($"expected 'get' or 'set' in property but found {Got()}");
				if (_pos == p) Advance();
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, "property");
			return new ClassProperty(name, indexParams, getBody, getArrow, setBody, setArrow, isStatic);
		}

		// No-parentheses command call:  Name arg1, arg2   /   obj.Method arg   /   Name , arg (omit first).
		// Detected by lookahead: the callee primary (name/member/index) is followed by whitespace + an
		// argument start, or a comma — but not by an adjacent '(' (a call expression) or an operator.
		private static readonly System.Collections.Generic.HashSet<string> verbalOps =
			new(System.StringComparer.OrdinalIgnoreCase) { "not", "and", "or", "is", "in", "contains" };

		private bool IsCommandStatement()
		{
			if (!At(TokenKind.Identifier)) return false;
			if (verbalOps.Contains(Current.Text)) return false;   // `not x`, `a and b` are operators, not commands
			var i = _pos + 1;   // past the leading identifier
			bool sawDot = false;
			while (i < _t.Count)
			{
				var t = _t[i];
				if (t.Kind == TokenKind.Dot && !t.LeadingWhitespace)
				{ sawDot = true; i++; if (i < _t.Count && _t[i].Kind == TokenKind.Identifier) i++; continue; }     // .member
				if (t.Kind == TokenKind.LBracket && !t.LeadingWhitespace)
				{ var d = 1; i++; while (i < _t.Count && d > 0) { if (_t[i].Kind == TokenKind.LBracket) d++; else if (_t[i].Kind == TokenKind.RBracket) d--; i++; } continue; }  // [index]
				break;
			}
			// A primary-expression chain (`name`, `obj.method`) ending the line is a zero-arg function-call statement
			// (`ExitApp`, `obj.Method`). An index-access end (`arr[i]`) is an expression statement, not a call (mirrors
			// the canonical isFunctionCallStatement, which rejects a trailing CloseBracket).
			if (i >= _t.Count || _t[i].Kind == TokenKind.Newline || _t[i].Kind == TokenKind.EOF || _t[i].Kind == TokenKind.RBrace)
			{
				// …unless a leading comma on a following line continues it. Joined, `Name` + `, x := 1` is
				// `Name, x := 1`, and an ADJACENT comma makes that a comma-sequence expression statement (which
				// evaluates `Name` without calling it), so hand it to ParseStatement rather than calling it here.
				var j = i;
				while (j < _t.Count && _t[j].Kind == TokenKind.Newline) j++;
				if (j < _t.Count && _t[j].Kind == TokenKind.Comma) return false;
				return _t[i - 1].Kind != TokenKind.RBracket && (sawDot || _t[i - 1].Kind == TokenKind.Identifier);
			}
			var next = _t[i];
			if (next.Kind == TokenKind.LParen && !next.LeadingWhitespace) return false;   // Name(...) call expression
			// A command-call statement (`MsgBox "x", "y"`) requires the name/chain to be followed by whitespace (then an
			// argument or a leading-comma omitted first argument), a comment, or end-of-line. Anything ADJACENT — `,`
			// `:=` `.` `[` … — makes it an expression statement instead, so `MsgBox, "x"` and `a[i], b := 1` are
			// comma-sequences, while `MsgBox ,"x"` (space before the comma) is a call with the first arg omitted.
			if (!next.LeadingWhitespace) return false;                                     // adjacent operator/comma/postfix => expression
			switch (next.Kind)
			{
				case TokenKind.Comma:
					return _t[i - 1].Kind != TokenKind.RBracket;   // `Func ,arg` => command with an omitted first argument
				case TokenKind.Identifier:
					return !verbalOps.Contains(next.Text);   // `a and b` is a binary expression, not `a(and …)`
				case TokenKind.String: case TokenKind.Number:
				case TokenKind.Percent: case TokenKind.LBrace:
				case TokenKind.LParen:   // a SPACE before '(' is a command-style call: `OnExit (*) => f`, `MsgBox (1+1)`
					return true;
				case TokenKind.Minus: case TokenKind.Plus: case TokenKind.Not: case TokenKind.BitNot:
				case TokenKind.Star: case TokenKind.BitAnd: case TokenKind.LBracket:
					return i + 1 < _t.Count && !_t[i + 1].LeadingWhitespace;   // unary/ref (adjacent operand) => arg; spaced => binary
				default: return false;                                          // := / + / = etc. => expression statement
			}
		}

		private Stmt ParseCommandCall()
		{
			var callee = ParsePostfix(ParsePrimary());   // name / member / index — no adjacent '(' (ensured by detection)
			return new ExpressionStmt(new CallExpr(callee, ParseCommandArgs()));
		}

		// Comma-separated command arguments, ending at end of line / '}'. Supports omitted args.
		private List<Argument> ParseCommandArgs()
		{
			var args = new List<Argument>();
			var named = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RBrace))
			{
				if (At(TokenKind.Comma))
				{
					if (AnyNamed(args)) Error("an omitted argument cannot follow a named argument");
					args.Add(new Argument(null, false)); Advance(); continue;
				}
				var name = TryTakeArgName(null, named);   // `MsgBox "text", Options: "OK"`
				var ex = ParseExpression(1);
				var nameExpr = name == null ? TryTakeDynamicArgName(null, ref ex) : null;
				var spread = Match(TokenKind.Star);
				args.Add(NewArgSlot(ex, spread, name, nameExpr, args));
				if (!Match(TokenKind.Comma))
				{
					// The comma may instead start the NEXT line — a leading comma is a line continuation, so look
					// past newlines for it (as with comma statements).
					var save = _pos;
					SkipNewlines();
					if (!Match(TokenKind.Comma)) { _pos = save; break; }
				}
			}
			return args;
		}

		private bool IsFunctionDefinition()
		{
			if (!At(TokenKind.Identifier)) return false;
			var lp = Peek(1);
			if (lp.Kind != TokenKind.LParen || lp.LeadingWhitespace) return false; // name( must be adjacent

			var i = _pos + 1;
			var depth = 0;
			for (; i < _t.Count; i++)
			{
				if (_t[i].Kind == TokenKind.LParen) depth++;
				else if (_t[i].Kind == TokenKind.RParen) { depth--; if (depth == 0) { i++; break; } }
			}
			while (i < _t.Count && _t[i].Kind == TokenKind.Newline) i++;
			if (i >= _t.Count) return false;
			return _t[i].Kind == TokenKind.LBrace || _t[i].Kind == TokenKind.FatArrow;
		}

		private Stmt ParseFunctionDecl(bool isStatic = false)
		{
			var nameTok = Current;
			var name = Advance().Text;
			RejectReservedName(name, nameTok, "a function name");
			var ps = ParseParamList();
			SkipNewlines();
			if (Match(TokenKind.FatArrow))
				return new FunctionDecl(name, ps, null, ParseExpression(1), isStatic);
			return new FunctionDecl(name, ps, ParseBlock(), null, isStatic);
		}

		private List<Param> ParseParamList() => ParseParamList(TokenKind.LParen, TokenKind.RParen);

		private List<Param> ParseParamList(TokenKind open, TokenKind close)
		{
			Expect(open, "parameter list");
			_groupDepth++;
			var ps = new List<Param>();
			SkipNewlines();
			while (!At(close) && !At(TokenKind.EOF))
			{
				var byRef = Match(TokenKind.BitAnd);
				if (!byRef && At(TokenKind.Star))   // anonymous variadic `*` — AHK binds it to the implicit local `args`
				{
					Advance();
					ps.Add(new Param("args", null, false, true, false));
					SkipNewlines();
					if (!Match(TokenKind.Comma)) break;
					SkipNewlines();
					continue;
				}
				var nameTok = Current;
				var name = ExpectIdentifier("parameter");
				RejectReservedName(name, nameTok, "a parameter name");
				var variadic = false; var optional = false; Expr def = null;
				if (Match(TokenKind.Star)) variadic = true;
				else if (Match(TokenKind.Question)) optional = true;
				else if (Match(TokenKind.Assign)) def = ParseExpression(1);
				ps.Add(new Param(name, def, byRef, variadic, optional));
				SkipNewlines();
				if (!Match(TokenKind.Comma)) break;
				SkipNewlines();
			}
			Expect(close, "parameter list");
			_groupDepth--;
			return ps;
		}

		// ---- expressions (Pratt) ----

		// Binding power: higher = binds tighter. Taken from the AHK v2 operator-precedence order.
		private const int PrecPower = 17;

		private readonly struct Infix
		{
			public readonly int Prec; public readonly bool Right; public readonly bool Assign; public readonly bool Concat;
			public Infix(int prec, bool right, bool assign, bool concat) { Prec = prec; Right = right; Assign = assign; Concat = concat; }
		}

		private Infix GetInfix(Token t)
		{
			switch (t.Kind)
			{
				case TokenKind.Assign:
				case TokenKind.PlusAssign: case TokenKind.MinusAssign:
				case TokenKind.StarAssign: case TokenKind.SlashAssign: case TokenKind.IntDivAssign:
				case TokenKind.PowerAssign: case TokenKind.PercentAssign: case TokenKind.DotAssign:
				case TokenKind.BitAndAssign: case TokenKind.BitOrAssign: case TokenKind.BitXorAssign:
				case TokenKind.ShiftLeftAssign: case TokenKind.ShiftRightAssign: case TokenKind.ShiftRightLogicalAssign:
				case TokenKind.NullCoalesceAssign:
					return new Infix(1, true, true, false);

				case TokenKind.NullCoalesce: return new Infix(3, true, false, false);
				case TokenKind.LogicalOr: return new Infix(4, false, false, false);
				case TokenKind.LogicalAnd: return new Infix(5, false, false, false);

				case TokenKind.Equal: case TokenKind.Identity:
				case TokenKind.NotEqual: case TokenKind.NotIdentity:
					return new Infix(7, false, false, false);

				case TokenKind.Less: case TokenKind.Greater:
				case TokenKind.LessEqual: case TokenKind.GreaterEqual:
					return new Infix(8, false, false, false);

				case TokenKind.RegexMatch: case TokenKind.NotRegexMatch:
					return new Infix(9, false, false, false);

				case TokenKind.Dot:   // any Dot reaching the binary loop is a spaced concat (adjacent dots
					return new Infix(10, false, false, true);  // are consumed as member access in ParsePostfix)

				case TokenKind.BitOr: return new Infix(11, false, false, false);
				case TokenKind.BitXor: return new Infix(12, false, false, false);
				case TokenKind.BitAnd: return new Infix(13, false, false, false);

				case TokenKind.ShiftLeft: case TokenKind.ShiftRight: case TokenKind.ShiftRightLogical:
					return new Infix(14, false, false, false);

				case TokenKind.Plus: case TokenKind.Minus: return new Infix(15, false, false, false);
				case TokenKind.Star: case TokenKind.Slash: case TokenKind.IntDiv: return new Infix(16, false, false, false);
				case TokenKind.Power: return new Infix(PrecPower, true, false, false);

				case TokenKind.Identifier:
					if (t.IsKeyword("or")) return new Infix(4, false, false, false);
					if (t.IsKeyword("and")) return new Infix(5, false, false, false);
					if (t.IsKeyword("is") || t.IsKeyword("in") || t.IsKeyword("contains"))
						return new Infix(6, false, false, false);
					return default;

				default: return default;
			}
		}

		private Expr ParseExpression(int minPrec)
		{
			// Guard against unbounded recursion from unsupported/malformed continuations (e.g. non-string `(…)`
			// continuation sections) so a bad parse fails cleanly instead of overflowing the stack.
			if (_exprDepth >= MaxExprDepth)
			{
				Error("expression nested too deeply (unterminated grouping or unsupported continuation?)");
				while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RParen) && !At(TokenKind.RBracket) && !At(TokenKind.RBrace)) Advance();
				return new NameExpr("«error»");
			}
			_exprDepth++;
			try { return ParseExpressionCore(minPrec); }
			finally { _exprDepth--; }
		}

		private Expr ParseExpressionCore(int minPrec)
		{
			var left = ParseUnary();

			while (true)
			{
				if (_groupDepth > 0) SkipNewlines();
				var t = Current;

				// Leading-operator line continuation: `expr`<newline>`<binary-op> ...` joins the lines.
				if (t.Kind == TokenKind.Newline)
				{
					var save = _pos;
					SkipNewlines();
					if (GetInfix(Current).Prec > 0 || Current.Kind == TokenKind.Question) t = Current;   // ?: may continue a line
					else _pos = save;
				}

				// Ternary `? :` (prec 2, right-assoc) and the maybe-operator `x?`.
				if (t.Kind == TokenKind.Question)
				{
					if (2 < minPrec) break;
					Advance();   // consume '?'
					// `?` is the maybe (unset-permissive) operator when the next SIGNIFICANT token (skipping newlines;
					// whitespace/comments are already stripped) is one of ) ] } , : ? . or a statement-starting
					// keyword; otherwise it's the ternary conditional. (So `x := 1?\n2:\n3` is the ternary 1?2:3.)
					var qSave = _pos;
					SkipNewlines();
					if (At(TokenKind.RParen) || At(TokenKind.RBracket) || At(TokenKind.RBrace) || At(TokenKind.Comma)
						|| At(TokenKind.Colon) || At(TokenKind.Question) || At(TokenKind.Dot) || At(TokenKind.EOF)
						|| IsStatementKeyword())
					{
						_pos = qSave;   // leave any trailing newline in place — the maybe value ends here
						left = new UnaryExpr("?", left, true);
						continue;
					}
					var then = ParseExpression(1);
					SkipNewlines();   // the ':' arm may be on a continuation line
					Expect(TokenKind.Colon, "ternary");
					SkipNewlines();
					var els = ParseExpression(1);
					left = new TernaryExpr(left, then, els);
					continue;
				}

				// `expr*` spread (argument/array): a trailing '*' before a terminator is not multiplication.
				if (t.Kind == TokenKind.Star && IsArgTerminator(Peek(1))) break;

				var inf = GetInfix(t);
				if (inf.Prec == 0 || inf.Prec < minPrec)
				{
					// Implicit concatenation: `a b` => `a . b` (auto-concat of space-separated operands).
					if (minPrec <= 10 && t.LeadingWhitespace && IsImplicitConcatStart(t))
					{
						left = new BinaryExpr(".", left, ParseExpression(11));
						continue;
					}
					break;
				}

				Advance();
				SkipNewlines();   // a dangling operator continues onto the next line
				var rhs = ParseExpression(inf.Right ? inf.Prec : inf.Prec + 1);
				left = inf.Assign ? AttachAssign(t.Text, left, rhs)
					 : inf.Concat ? new BinaryExpr(".", left, rhs)
					 : new BinaryExpr(t.Text, left, rhs);
			}
			return left;
		}

		// AHK raises assignment precedence to avoid a syntax error / give intuitive behavior: the assignment binds to the
		// variable immediately on its left, not to a whole boolean/relational/unary expression. So `x==y && z:=1` is
		// `x==y && (z:=1)`, `not x:=y` is `not (x:=y)`, and `++Var:=X` is `++(Var:=X)`. (Ternary arms already parse this
		// way since each arm is a full expression.) Push the assignment into the apparent target's rightmost operand.
		private static Expr AttachAssign(string op, Expr target, Expr rhs) =>
			target is BinaryExpr be ? new BinaryExpr(be.Op, be.Left, AttachAssign(op, be.Right, rhs))
			: target is UnaryExpr { Postfix: false } ue ? new UnaryExpr(ue.Op, AttachAssign(op, ue.Operand, rhs), false)
			: new AssignExpr(op, target, rhs);

		// Whether a token can start the right side of an auto-concatenation (`a b`).
		private bool IsImplicitConcatStart(Token t) => t.Kind switch
		{
			TokenKind.String or TokenKind.Number or TokenKind.LParen or TokenKind.Percent => true,
			TokenKind.Identifier => !verbalOps.Contains(t.Text),
			_ => false
		};

		// A token that terminates an argument/array element, so a preceding * is a spread marker not multiplication.
		private static bool IsArgTerminator(Token t) => t.Kind switch
		{
			TokenKind.RParen or TokenKind.RBracket or TokenKind.RBrace or TokenKind.Comma
			or TokenKind.Newline or TokenKind.EOF => true,
			_ => false
		};

		private Expr ParseUnary()
		{
			SkipNewlines();
			if (IsFatArrowHead()) return ParseFatArrow();

			var t = Current;
			switch (t.Kind)
			{
				case TokenKind.Minus: case TokenKind.Plus: case TokenKind.Not: case TokenKind.BitNot:
					Advance();
					return new UnaryExpr(t.Text, ParseExpression(PrecPower), false); // power binds into the operand
				case TokenKind.PlusPlus: case TokenKind.MinusMinus:
					Advance();
					return new UnaryExpr(t.Text, ParseUnary(), false);
				case TokenKind.BitAnd:   // reference / address-of
					Advance();
					return new UnaryExpr("&", ParsePostfix(ParsePrimary()), false);
			}
			if (t.IsKeyword("not"))   // low-precedence verbal not
			{
				Advance();
				return new UnaryExpr("not", ParseExpression(6), false);
			}
			return ParsePostfix(ParsePrimary());
		}

		private Expr ParsePostfix(Expr e)
		{
			while (true)
			{
				var t = Current;
				// Leading-dot member-access continuation: `expr` ⏎ `.member` (a '.' on the next line immediately followed
				// by the member name). This is method/member access, NOT concat (`expr` ⏎ `. member`, with a space after
				// the dot, stays a concat handled by the infix loop).
				if (t.Kind == TokenKind.Newline)
				{
					var save = _pos;
					SkipNewlines();
					if (At(TokenKind.Dot) && !Peek(1).LeadingWhitespace
						&& Peek(1).Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.Percent)
					{ Advance(); e = MakeMember(e, false); continue; }
					_pos = save;
					break;
				}
				// Concatenation wants whitespace on BOTH sides of the dot, so a dot with a space on only one side is
				// member access: `a .b` reads as `a.b`, the same as `a. b` does, while `a . b` concatenates. The
				// member-name check keeps a trailing `.` continuation (`"a" .` ⏎ `"b"`) a concat.
				if (t.Kind == TokenKind.Dot
						&& (!t.LeadingWhitespace || (!Peek(1).LeadingWhitespace
								&& Peek(1).Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.Percent)))
				{ Advance(); e = MakeMember(e, false); continue; }
				if (t.Kind == TokenKind.QuestionDot)
				{
					Advance();
					if (At(TokenKind.LBracket))
						Error("`a?.[]` was removed in v2.1-alpha.30; use `(a?)[]`.");
					if (At(TokenKind.LParen))
						Error("`a?.()` was removed in v2.1-alpha.30; use `(a?)()`.");
					e = MakeMember(e, true); continue;
				}
				if (t.Kind == TokenKind.LParen && !t.LeadingWhitespace)
				{ e = new CallExpr(e, ParseArgs(TokenKind.LParen, TokenKind.RParen)); continue; }
				if (t.Kind == TokenKind.LBracket && !t.LeadingWhitespace)
				{
					e = new IndexExpr(e, ParseArgs(TokenKind.LBracket, TokenKind.RBracket,
												   "named arguments are not supported in an index expression; '[]' takes positional arguments only"));
					continue;
				}
				if ((t.Kind == TokenKind.PlusPlus || t.Kind == TokenKind.MinusMinus) && !t.LeadingWhitespace)
				{ Advance(); e = new UnaryExpr(t.Text, e, true); continue; }
				break;
			}
			return e;
		}

		private Expr ParsePrimary()
		{
			SkipNewlines();
			var t = Current;
			switch (t.Kind)
			{
				case TokenKind.Number: Advance(); return new LiteralExpr(LiteralKind.Number, t.Text);
				case TokenKind.String: Advance(); return new LiteralExpr(LiteralKind.String, t.Text);
				case TokenKind.Identifier:
					Advance();
					// A reserved word here is being used as a variable (a real one would have been dispatched as a
					// statement, or is a member name handled elsewhere) — e.g. the `return` in `x := y() return z`,
					// where auto-concat would otherwise swallow it as a variable. Reject it instead.
					RejectReservedName(t.Text, t, "a variable name");
					// `pre%mid%…` with no intervening whitespace is dynamic name-building, not a plain variable —
					// but a '%' that closes the enclosing %…% is a delimiter, not the start of a new name.
					if (!_inDerefInner && !Current.LeadingWhitespace && At(TokenKind.Percent))
						return ParseNameBuild(new LiteralExpr(LiteralKind.String, "\"" + t.Text + "\""));
					return new NameExpr(t.Text, t.Line) { File = t.File };
				case TokenKind.LParen:
					Advance(); _groupDepth++;
					var inner = ParseExpression(1);
					if (At(TokenKind.Comma))   // parenthesized sequence `(a, b)`
					{
						var items = new List<Expr> { inner };
						while (Match(TokenKind.Comma)) { SkipNewlines(); items.Add(ParseExpression(1)); }
						inner = new SequenceExpr(items);
					}
					_groupDepth--;
					Expect(TokenKind.RParen, "parenthesized expression");
					return inner is SequenceExpr ? inner : new GroupExpr(inner);
				case TokenKind.LBracket:
					return ParseArrayOrMap();
				case TokenKind.LBrace:
					return ParseObjectLiteral();
				case TokenKind.Percent:   // %expr% dynamic dereference (or start of name-building)
					Advance();
					var savedDeref = _inDerefInner; _inDerefInner = true;
					var nameExpr = ParseExpression(1);
					_inDerefInner = savedDeref;
					Expect(TokenKind.Percent, "dereference");
					if (!Current.LeadingWhitespace && (At(TokenKind.Identifier) || At(TokenKind.Number) || At(TokenKind.Percent)))
						return ParseNameBuild(nameExpr);
					return new DerefExpr(nameExpr);
				default:
					Error($"unexpected {Got()}");
					// Resync to an expression/statement boundary so one bad token can't cascade via auto-concat.
					Advance();
					while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RParen)
						&& !At(TokenKind.RBracket) && !At(TokenKind.RBrace) && !At(TokenKind.Comma)) Advance();
					return new NameExpr("«error»");
			}
		}

		// Dynamic name-building: a run of adjacent identifier/number/%expr% parts forms a variable name at
		// runtime (`z%y%` -> deref of "z" . y). Lowered as a DerefExpr over the concatenation of the parts.
		private Expr ParseNameBuild(Expr firstPart)
		{
			var parts = new List<Expr> { firstPart };
			while (!Current.LeadingWhitespace)
			{
				if (At(TokenKind.Identifier) || At(TokenKind.Number))
					parts.Add(new LiteralExpr(LiteralKind.String, "\"" + Advance().Text + "\""));
				else if (At(TokenKind.Percent))
				{
					Advance();
					var savedDeref = _inDerefInner; _inDerefInner = true;
					var e = ParseExpression(1);
					_inDerefInner = savedDeref;
					Expect(TokenKind.Percent, "dereference");
					parts.Add(e);
				}
				else break;
			}
			Expr name = parts[0];
			for (int i = 1; i < parts.Count; i++) name = new BinaryExpr(".", name, parts[i]);
			return new DerefExpr(name);
		}

		/// <param name="namedError">
		/// Null when named arguments are allowed. Otherwise what to say about the `name:` that was found -- the two
		/// bracketed forms are wrong for different reasons, and a single message would misdiagnose one of them.
		/// </param>
		private List<Argument> ParseArgs(TokenKind open, TokenKind close, string namedError = null)
		{
			Expect(open, "argument list");
			_groupDepth++;
			var args = new List<Argument>();
			var named = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			SkipNewlines();
			// Comma-separated slots: each slot is an expression or omitted (empty). A TRAILING comma is ignored (it does
			// not add a slot), so `f(a,)`/`f(x,&y,)` keep their arg count and `f()` is zero, while `f(,a)`/`f(a,,b)` keep
			// the leading/interior omitted slots and `f(,)` is a single omitted arg.
			if (!At(close) && !At(TokenKind.EOF))
			{
				while (true)
				{
					if (At(TokenKind.Comma) || At(close) || At(TokenKind.EOF))
					{
						if (AnyNamed(args)) Error("an omitted argument cannot follow a named argument");
						args.Add(new Argument(null, false));   // omitted slot
					}
					else
					{
						// Named arguments are a call-parenthesis form only: inside '[]' a leading `name:` belongs to the
						// map-creation literal, so allowing it here would blur two different meanings of the same syntax.
						var name = TryTakeArgName(namedError, named);
						var ex = ParseExpression(1);
						var nameExpr = name == null ? TryTakeDynamicArgName(namedError, ref ex) : null;
						args.Add(NewArgSlot(ex, Match(TokenKind.Star), name, nameExpr, args));
					}
					SkipNewlines();
					if (!At(TokenKind.Comma)) break;
					Advance(); SkipNewlines();
					if (At(close)) break;   // trailing comma: no extra slot
				}
			}
			Expect(close, "argument list");
			_groupDepth--;
			return args;
		}

		// `name:` at the start of an argument slot introduces a NAMED argument (bound to the callee's parameter of that
		// name instead of by position). One token of lookahead, and applied ONLY at a slot boundary: a ternary's ':' is
		// always preceded by a '?' that the expression parse has already consumed, so a slot-initial Identifier followed
		// by ':' cannot begin any other valid expression. (':=' lexes as Assign and '::' as DoubleColon, so neither is
		// mistaken for one.) Reserved words are accepted -- the token names a parameter, it is never read as a variable.
		private string TryTakeArgName(string namedError, HashSet<string> seen)
		{
			if (!At(TokenKind.Identifier) || Peek(1).Kind != TokenKind.Colon) return null;

			var t = Current;

			if (namedError != null)
				ErrorAt(t, namedError);

			if (!seen.Add(t.Text))
				ErrorAt(t, $"duplicate named argument '{t.Text}'");

			Advance();   // name
			Advance();   // ':'
			SkipNewlines();
			return t.Text;
		}

		// The DYNAMIC spellings of the same thing: `%x%: v` and the name-building `a%b%c: v`, whose name is only known
		// at run time. Neither is a single Identifier, so the lookahead above cannot see them -- they are recognised
		// after the fact, by the slot's expression having parsed to a deref with a ':' left over. Nothing else can end
		// a slot at a bare ':': a ternary's ':' is consumed with the '?' that introduced it, and ':=' lexes as Assign.
		//
		// `parsed` is the deref, which becomes the name; it is replaced with the value expression that follows.
		// Duplicates cannot be detected here (two derefs may yield the same name), and neither can #Warn NamedArg
		// check one -- the runtime binder reports both, as it does for any name it cannot match.
		private Expr TryTakeDynamicArgName(string namedError, ref Expr parsed)
		{
			if (!At(TokenKind.Colon)) return null;

			if (namedError != null)
				Error(namedError);

			// A quoted key is deliberately NOT a named argument. Parameters are named by identifiers, so a string
			// would be a second spelling for one thing -- and `f("a": 1)` reads like a Map entry, which is what the
			// same text means one bracket away in `[ "a": 1 ]`.
			if (parsed is not DerefExpr)
				Error("the name of a named argument must be an identifier or a '%expr%' dereference");

			Advance();   // ':'
			SkipNewlines();
			var name = parsed;
			parsed = ParseExpression(1);
			return name;
		}

		// Whether any slot parsed so far is named -- what the ordering rules below are stated against. Counting the
		// args rather than the seen-names set is what keeps dynamic names, which contribute no name to that set,
		// subject to the same rules.
		private static bool AnyNamed(List<Argument> args)
		{
			foreach (var a in args)
				if (a.IsNamed)
					return true;

			return false;
		}

		// Named arguments always TRAIL the positional ones. Enforcing that here is what lets the runtime find them with a
		// single test on the last element of the argument array, and keeps the reading order of a call site unambiguous.
		private Argument NewArgSlot(Expr value, bool spread, string name, Expr nameExpr, List<Argument> args)
		{
			var named = name != null || nameExpr != null;

			if (spread && named)
				Error("a named argument cannot be spread with '*'");

			if (!named && AnyNamed(args))
				Error(spread ? "a spread argument cannot follow a named argument"
							 : "a positional argument cannot follow a named argument");

			return new Argument(value, spread, name, nameExpr);
		}

		// `[ … ]` is an array literal, or a map-creation literal `[k1: v1, k2: v2]` when a top-level `:` (not a ternary
		// colon) appears before the first top-level `,`/`]`.
		private Expr ParseArrayOrMap() =>
			IsMapLiteral() ? ParseMap()
						   : new ArrayExpr(ParseArgs(TokenKind.LBracket, TokenKind.RBracket,
													 "'name: value' is only a map-creation literal when it is the whole '[]' list"));

		// Scans the bracketed group (from the '[') for a top-level ':' that is not consumed by a ternary '?'.
		private bool IsMapLiteral()
		{
			int i = _pos + 1, depth = 0, ternary = 0;
			while (i < _t.Count)
			{
				switch (_t[i].Kind)
				{
					case TokenKind.LParen: case TokenKind.LBracket: case TokenKind.LBrace: depth++; break;
					case TokenKind.RParen: case TokenKind.RBrace: depth--; break;
					case TokenKind.RBracket: if (depth == 0) return false; depth--; break;
					case TokenKind.Question: if (depth == 0) ternary++; break;
					case TokenKind.Colon:
						if (depth == 0) { if (ternary > 0) ternary--; else return true; }
						break;
					case TokenKind.Comma: if (depth == 0) return false; break;
				}
				i++;
			}
			return false;
		}

		private Expr ParseMap()
		{
			Expect(TokenKind.LBracket, "map literal");
			_groupDepth++;
			var entries = new List<(Expr, Expr)>();
			SkipNewlines();
			while (!At(TokenKind.RBracket) && !At(TokenKind.EOF))
			{
				if (At(TokenKind.Comma)) { Advance(); SkipNewlines(); continue; }
				var key = ParseExpression(1);
				SkipNewlines();
				Expect(TokenKind.Colon, "map literal");
				SkipNewlines();
				entries.Add((key, ParseExpression(1)));
				SkipNewlines();
				if (!Match(TokenKind.Comma)) break;
				SkipNewlines();
			}
			Expect(TokenKind.RBracket, "map literal");
			_groupDepth--;
			return new MapExpr(entries);
		}

		// Object literal { name: value, "str": value, ... }. Only reached in expression position
		// (statement-position '{' is parsed as a block), so no lexer disambiguation is needed.
		private Expr ParseObjectLiteral()
		{
			Expect(TokenKind.LBrace, "object literal");
			_groupDepth++;
			var entries = new List<ObjectEntry>();
			SkipNewlines();
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				if (At(TokenKind.Hash))   // a directive (e.g. #import) on its own line inside the literal — hoist it out
				{
					_hoistedStmts.Add(ParseDirective());
					SkipNewlines();
					Match(TokenKind.Comma);
					SkipNewlines();
					continue;
				}
				Expr key = At(TokenKind.Identifier) ? new NameExpr(Advance().Text)
					: At(TokenKind.String) ? new LiteralExpr(LiteralKind.String, Advance().Text)
					: ParsePrimary();
				SkipNewlines();   // inside `{ }` the key, ':' and value may each be on their own line
				Expect(TokenKind.Colon, "object literal");
				SkipNewlines();
				entries.Add(new ObjectEntry(key, ParseExpression(1)));
				SkipNewlines();
				if (!Match(TokenKind.Comma)) break;
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, "object literal");
			_groupDepth--;
			return new ObjectExpr(entries);
		}

		// ---- fat-arrow lambdas ----

		private bool IsFatArrowHead()
		{
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.FatArrow) return true;   // a => …
			// named arrow/block fn `name(params) => …` or `name(params) { … }` (name adjacent to its '(')
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.LParen && !Peek(1).LeadingWhitespace)
				return ParenMatchFollowedBy(TokenKind.FatArrow, _pos + 1) || (!_inFlowCond && ParenMatchFollowedBy(TokenKind.LBrace, _pos + 1));
			// anonymous `(params) => …`, or `(params) { … }` (the block form is ambiguous with a control-flow body)
			if (At(TokenKind.LParen))
				return ParenMatchFollowedBy(TokenKind.FatArrow, _pos) || (!_inFlowCond && ParenMatchFollowedBy(TokenKind.LBrace, _pos));
			return false;
		}

		private bool ParenMatchFollowedBy(TokenKind kind, int i)
		{
			// i is at '('
			var depth = 0;
			for (; i < _t.Count; i++)
			{
				var k = _t[i].Kind;
				if (k == TokenKind.LParen) depth++;
				else if (k == TokenKind.RParen) { depth--; if (depth == 0) { i++; break; } }
				else if (k == TokenKind.EOF) return false;
			}
			while (i < _t.Count && _t[i].Kind == TokenKind.Newline) i++;
			return i < _t.Count && _t[i].Kind == kind;
		}

		private Expr ParseFatArrow()
		{
			// Optional name for a named fn `name(params) => …` / `name(params) { … }`: captured so the body can recurse
			// by that name (resolved to the lambda itself in the lowerer).
			string faName = null;
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.LParen && !Peek(1).LeadingWhitespace)
			{ var fnTok = Current; faName = Advance().Text; RejectReservedName(faName, fnTok, "a function name"); }
			List<Param> ps;
			if (At(TokenKind.Identifier))   // single bare-name parameter: `x => …`
			{ var pTok = Current; RejectReservedName(pTok.Text, pTok, "a parameter name"); ps = new List<Param> { new Param(Advance().Text, null, false, false, false) }; }
			else
				ps = ParseParamList();
			if (At(TokenKind.LBrace))   // anonymous block-bodied function `(params) { … }`
				return new FatArrowExpr(ps, ParseBlock()) { Name = faName };
			Expect(TokenKind.FatArrow, "fat-arrow function");
			return new FatArrowExpr(ps, ParseExpression(1)) { Name = faName };
		}

		[GeneratedRegex("%([A-Za-z_][A-Za-z0-9_]*)%")]
		private static partial Regex ExpandPathRegEx();
	}
}
