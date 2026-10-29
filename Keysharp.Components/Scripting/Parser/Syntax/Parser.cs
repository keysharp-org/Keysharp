namespace Keysharp.Parsing.Syntax
{
	/// <summary>
	/// Hand-written recursive-descent / Pratt parser producing the strongly-typed AST.
	/// </summary>
	internal sealed partial class Parser
	{
		private List<Token> _t = [];
		private int _pos;
		private long _nextSourceOrder;
		private bool _errorStdOut;
		private readonly string _mainScriptPath;   // the compiled script's full path, or null for source text
		private bool _inFunction;
		private int _groupDepth;   // >0 inside ()/[] — newlines are insignificant
		private int _exprDepth;    // recursion guard for ParseExpression (defensive cap against malformed/unsupported input)
		private const int MaxExprDepth = 250;
		private bool _inDerefInner;   // true while parsing the inner expr of a %…% — a trailing '%' closes it, not name-building

		// Directives (e.g. #import) found in expression position, such as inside an object literal `{ #import … }`.
		// AHK processes directives per physical line regardless of nesting, so these are hoisted to program scope.
		private readonly List<Stmt> _hoistedStmts = new();
		// Functions and classes declared in a block outside any function, which are global (see DeclarationSite).
		private readonly List<Stmt> _programDecls = new();
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

		private readonly string _includeDir;   // directory used to resolve relative #include paths (null => disabled)
		// The default Windows and macOS file systems ignore case; Linux paths are case-sensitive.
		internal static readonly System.StringComparer SourcePathComparer =
			System.OperatingSystem.IsWindows() || System.OperatingSystem.IsMacOS() ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
		private readonly HashSet<string> _included = new(SourcePathComparer);   // #include dedup
		private readonly Dictionary<string, string> _includedSources = new(SourcePathComparer);
		private readonly List<int> _includeParents = [];   // see ProgramNode.IncludeParents
		private ObjectStackPool<HashSet<string>> _scratchHashStrings = new(() => new(System.StringComparer.OrdinalIgnoreCase), item => item.Clear());
		private ObjectStackPool<List<Token>> _scratchListTokens = new(() => [], item => item.Clear());

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
		private Parser(string includeDir = null, IEnumerable<string> defines = null, string mainScriptPath = null)
		{
			_includeDir = includeDir;
			_mainScriptPath = string.IsNullOrWhiteSpace(mainScriptPath) ? null : System.IO.Path.GetFullPath(mainScriptPath);
			if (defines != null)
				_defines.AddRange(defines);
		}

		private void LoadTokens(List<Token> tokens, IReadOnlyList<string> lexDiagnostics = null, string includeFile = null, string scriptDir = null)
		{
			_t = Preprocess(tokens, lexDiagnostics, includeFile, scriptDir);
			JoinContinuationLines(_t);
		}

		// Tokenizes + parses, returning the AST together with all lex + parse diagnostics (line:col: message).
		// scriptFile is the parsed file's full path (when known): it stamps the top-level tokens so %A_LineFile% in an
		// #include resolves to the real file (not just its directory) and diagnostics name the file. mainScriptPath is the
		// compiled script's path, for %A_ScriptFullPath% and %A_ScriptName%; it is null when that script is source text,
		// including for the module files such a script imports. includeFile is the file --include names.
		public static (ProgramNode program, List<string> diagnostics) ParseWithDiagnostics(string source, string includeDir = null, string scriptFile = null, IEnumerable<string> defines = null, string mainScriptPath = null, string includeFile = null, string scriptDir = null)
		{
			var diags = new List<string>();
			var parser = new Parser(includeDir, defines, mainScriptPath);
			// As in AutoHotkey, a file counts as included already while it is being parsed, so an #Include of it is skipped.
			if (!string.IsNullOrWhiteSpace(scriptFile))
				_ = parser._included.Add(System.IO.Path.GetFullPath(scriptFile));
			try
			{
				var lexer = new Lexer(source, scriptFile);
				var tokens = LexForParsing(lexer);
				parser.LoadTokens(tokens, lexer.Diagnostics, includeFile, scriptDir);
				// Preprocessing has finished. Parse directives again in statement order so a later #ErrorStdOut cannot
				// retroactively route an earlier syntax error.
				parser._errorStdOut = false;
				var prog = parser.ParseProgram();
				// Publish the parser-owned final symbol set without another copy.
				prog.Defines = parser._defines;
				prog.ErrorStdOut = parser._errorStdOut;
				prog.IncludedSources = parser._includedSources;
				prog.IncludeParents = parser._includeParents;
				return (prog, diags);
			}
			catch (Keysharp.Builtins.ParseException ex)
			{
				diags.Add(ex.Message);
				return (new ProgramNode(new List<Stmt>()) { ErrorStdOut = parser._errorStdOut }, diags);
			}
		}

		// Parses one expression, for a directive argument such as a `#HotIf` criterion. ParseProgram is the wrong
		// entry point: at statement level a trailing primary chain such as `obj.Member` is a zero-arg call
		// statement, so a criterion would be invoked rather than read. Returns null, with diagnostics, unless the
		// text is exactly one expression. It takes no preprocessor symbols because a directive argument is rebuilt
		// from already-preprocessed tokens. File and startingLine place the expression's nodes in the source script.
		internal static (Expr expr, List<string> diagnostics) ParseExpressionWithDiagnostics(string source, string file = null, int startingLine = 1)
		{
			var diags = new List<string>();

			try
			{
				var lexer = new Lexer(source, file);
				var tokens = LexForParsing(lexer);

				if (lexer.Diagnostics.Count > 0)
					throw new Keysharp.Builtins.ParseException(lexer.Diagnostics[0]);

				if (startingLine > 1)
					for (var i = 0; i < tokens.Count; i++)
					{
						var token = tokens[i];
						tokens[i] = new Token(token.Kind, token.Text, token.Line + startingLine - 1, token.Column,
							token.Offset, token.Length, token.LeadingWhitespace, token.File);
					}

				var parser = new Parser();
				parser.LoadTokens(tokens);
				var expr = parser.ParseExpression(0);
				parser.SkipNewlines();

				if (!parser.At(TokenKind.EOF))
					parser.Error($"unexpected {parser.Got()} after the expression");

				return (expr, diags);
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

		// Member access after a consumed `.`/`?.`. The member name is a run of adjacent parts: identifiers/numbers
		// (literal text) and `%expr%` derefs. A run containing any deref is DYNAMIC (`obj.%x%`, `obj.a%x%b`) and the
		// name is the parts concatenated (`Concat("a", x, "b")`); a single plain identifier/number is a static name.
		private Expr MakeMember(Expr target, bool nullConditional)
		{
			var start = Current;
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
			return dynamic ? new DynMemberExpr(target, nameExpr, nullConditional) { Line = start.Line, Column = start.Column, File = start.File }
				: new MemberExpr(target, firstStatic, nullConditional);
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

		// AutoHotkey recognizes the word that starts a statement only when it ends at whitespace, a '(' or end of line.
		// A comma glued to it is the v1 command syntax, which both engines reject (script.cpp:4601).
		private const string commandCommaMessage = "function calls require a space or \"(\" — use a comma only between parameters";

		private void RejectAdjacentComma()
		{
			if (At(TokenKind.Comma) && !Current.LeadingWhitespace) Error(commandCommaMessage);
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
				var (inclusion, added) = (Current.Inclusion, body.Count);
				var s = ParseStatement(programScope: true);
				if (s != null) body.Add(s);
				// Drain directives lifted out of an object literal in this statement at THIS position, so they stay in
				// the correct #Module segment (appending at the end would misplace them into the last module).
				if (_hoistedStmts.Count > 0) { body.AddRange(_hoistedStmts); _hoistedStmts.Clear(); }
				for (var i = added; i < body.Count; i++)
					body[i].Inclusion = inclusion;
				if (_programDecls.Count > 0) { body.AddRange(_programDecls); _programDecls.Clear(); }
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
		private Stmt ParseStatement(bool programScope = false)
		{
			SkipNewlines();
			int line = Current.Line, col = Current.Column;
			var file = Current.File;
			var inclusion = Current.Inclusion;
			var sourceOrder = ++_nextSourceOrder;
			var s = ParseStatementCore(programScope);
			if (s != null && s.Line == 0) { s.Line = line; s.Column = col; s.File = file; }
			if (s != null && s.SourceOrder == 0) s.SourceOrder = sourceOrder;

			if (!programScope && !_inFunction && s is FunctionDecl or ClassDecl)
			{
				if (s is ClassDecl cd) cd.InitializesAtSite = true;
				s.Inclusion = inclusion;
				_programDecls.Add(s);
				return new DeclarationSite(s) { Line = s.Line, Column = s.Column, File = s.File, SourceOrder = s.SourceOrder };
			}

			return s;
		}

		private Stmt ParseStatementCore(bool programScope)
		{
			if (At(TokenKind.RemapSourceKey))
			{
				var src = Advance().Text;
				_ = Expect(TokenKind.DoubleColon, "'::' in a remap");
				return new RemapDef(src, Expect(TokenKind.RemapTargetKey, "remap target").Text);
			}
			if (At(TokenKind.HotkeyTrigger)) return ParseHotkey();
			if (At(TokenKind.HotstringTrigger)) return ParseHotstring();
			if (At(TokenKind.Hash)) return ParseDirective(programScope);
			if (At(TokenKind.LBrace)) return ParseBlock();
			if (AtKeyword("if")) return ParseIf();
			if (AtKeyword("while")) return ParseWhile();
			if (AtKeyword("loop")) return ParseLoop();
			if (AtKeyword("for")) return ParseFor();
			if (AtKeyword("switch")) return ParseSwitch();
			if (AtKeyword("try")) return ParseTry();
			if (AtKeyword("throw")) return ParseThrow();
			if (AtKeyword("break")) { Advance(); var (target, expression) = ParseJumpTarget("break"); return new BreakStmt(target, expression); }
			if (AtKeyword("continue")) { Advance(); var (target, expression) = ParseJumpTarget("continue"); return new ContinueStmt(target, expression); }
			if (AtKeyword("goto")) return ParseGoto();
			if (AtKeyword("return")) return ParseReturn();
			if (AtKeyword("local") || AtKeyword("global") || AtKeyword("static")) return ParseDecl();
			if ((AtKeyword("class") || AtKeyword("struct")) && Peek(1).Kind == TokenKind.Identifier)
			{
				if (_inFunction) Error("Functions cannot contain classes.");
				return ParseClass(isStruct: AtKeyword("struct"));
			}
			// A label: `name:` alone on its own line. The colon must be ADJACENT to the name (no left whitespace) and
			// end the line — this distinguishes it from a ternary's `: arm` continued on its own line (`false :`).
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon && !Peek(1).LeadingWhitespace
				&& (Peek(2).Kind == TokenKind.Newline || Peek(2).Kind == TokenKind.EOF))
			{ var lbl = Advance().Text; Advance(); return new LabelStmt(lbl); }
			if (IsFunctionDefinition()) return ParseFunctionDecl();
			if (IsCommandStatement()) return ParseCommandCall();

			return new ExpressionStmt(ParseSequence());
		}

		private Expr ParseSequence()
		{
			var first = ParseExpression(1);
			if (!At(TokenKind.Comma)) return first;
			var items = new List<Expr> { first };
			while (Match(TokenKind.Comma))
			{
				SkipNewlines();
				items.Add(ParseExpression(1));
			}
			return new SequenceExpr(items);
		}

		private Block ParseBlock()
		{
			var brace = Current;
			Expect(TokenKind.LBrace, "block");
			// A block is a statement context of its own, also as a function expression's body inside brackets, where
			// skipping newlines would join each statement to the one before it.
			var outer = (_groupDepth, _inFlowCond, _inDerefInner);
			(_groupDepth, _inFlowCond, _inDerefInner) = (0, false, false);
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
			(_groupDepth, _inFlowCond, _inDerefInner) = outer;
			return new Block(body) { Line = brace.Line, Column = brace.Column, File = brace.File };
		}

		private Stmt ParseBodyStatement()
		{
			SkipNewlines();
			return At(TokenKind.LBrace) ? ParseBlock() : ParseStatement();
		}

		private T ParseCallable<T>(Func<T> parse)
		{
			var outer = _inFunction;
			_inFunction = true;
			try { return parse(); }
			finally { _inFunction = outer; }
		}

		// Parses a control-flow header expression: a trailing `(…){` is the flow body, not an anon block fn.
		private Expr ParseCondExpr()
		{
			var saved = _inFlowCond; _inFlowCond = true;
			var e = ParseExpression(1);
			_inFlowCond = saved;
			return e;
		}

		// Inside brackets newlines are insignificant, and a `(…) {` is a function expression: the flow header around
		// the brackets cannot take it as its body.
		private bool EnterGroup()
		{
			var inFlowCond = _inFlowCond;
			_groupDepth++;
			_inFlowCond = false;
			return inFlowCond;
		}

		private void LeaveGroup(bool inFlowCond)
		{
			_groupDepth--;
			_inFlowCond = inFlowCond;
		}

		private Stmt ParseIf()
		{
			Advance(); // if
			var cond = ParseCondExpr();
			var then = ParseBodyStatement();
			return new IfStmt(cond, then, ParseOptionalElse());
		}

		private Stmt ParseWhile()
		{
			Advance();
			var cond = ParseCondExpr();
			var body = ParseBodyStatement();
			return new WhileStmt(cond, body, ParseOptionalUntil()) { Else = ParseOptionalElse() };
		}

		private Expr ParseOptionalUntil()
		{
			var save = _pos;
			SkipNewlines();
			if (AtKeyword("until")) { Advance(); return ParseCondExpr(); }
			_pos = save;
			return null;
		}

		private Stmt ParseOptionalElse()
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
				return new SpecialLoopStmt(kind, args, sbody) { Until = ParseOptionalUntil(), Else = ParseOptionalElse() };
			}
			Expr count = (At(TokenKind.LBrace) || At(TokenKind.Newline) || At(TokenKind.EOF)) ? null : ParseCondExpr();
			var body = ParseBodyStatement();
			return new LoopStmt(count, body, ParseOptionalUntil()) { Else = ParseOptionalElse() };
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
			RejectAdjacentComma();
			bool paren = Match(TokenKind.LParen);   // optional parens around the header: for (i, v in arr)
			var vars = new List<string> { ParseForVar() };
			while (Match(TokenKind.Comma)) vars.Add(ParseForVar());
			if (!AtKeyword("in")) Error($"expected 'in' in for loop but found {Got()}");
			else Advance();
			var enumerable = ParseCondExpr();
			if (paren) Expect(TokenKind.RParen, "for header");
			return new ForStmt(vars, enumerable, ParseBodyStatement()) { Until = ParseOptionalUntil(), Else = ParseOptionalElse() };
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
				else Error($"expected 'case' or 'default' in switch but found {Got()}");
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
					types.Add(ParseCatchType());
					if (AtKeyword("as")) { Advance(); var = ExpectIdentifier("catch variable"); break; }
					if (!Match(TokenKind.Comma)) break;
				}
				Expect(TokenKind.RParen, "catch type list");
			}
			else if (At(TokenKind.Identifier) && !AtKeyword("as"))   // catch Type [, Type]* [as var | var]
			{
				types.Add(ParseCatchType());
				while (Match(TokenKind.Comma)) { if (AtKeyword("as")) break; types.Add(ParseCatchType()); }
			}
			if (var == null)
			{
				if (AtKeyword("as")) { Advance(); var = ExpectIdentifier("catch variable"); }
				else if (At(TokenKind.Identifier)) var = ExpectIdentifier("catch variable");   // catch Type var (no 'as')
			}
			return new CatchBlock(types, var, ParseBodyStatement());
		}

		private string ParseCatchType()
		{
			var name = ExpectIdentifier("catch type");
			while (Match(TokenKind.Dot)) name += "." + ExpectIdentifier("catch type");
			return name;
		}

		// #DirectiveName trailing-args  — the trailing text is consumed raw (directives use unquoted args).
		private Stmt ParseDirective(bool programScope = false)
		{
			// Positioned here rather than only in ParseStatement: a directive is also parsed straight from a class body
			// and from an object literal, and those callers would otherwise produce an unlocatable #Warning/#Error.
			var dirToken = Current;
			int dirLine = Current.Line, dirCol = Current.Column;
			var dirFile = Current.File;
			var sourceOrder = ++_nextSourceOrder;
			Advance(); // #
			var name = At(TokenKind.Identifier) ? Advance().Text : "";
			if (name.Equals("errorstdout", System.StringComparison.OrdinalIgnoreCase))
				_errorStdOut = true;

			if (name.Equals("startup", System.StringComparison.OrdinalIgnoreCase))
			{
				if (!programScope)
					ErrorAt(dirToken, "#Startup is only allowed at module top level");

				if (At(TokenKind.Newline) || At(TokenKind.EOF) || Current.Line != dirLine || Current.File != dirFile)
					ErrorAt(dirToken, "#Startup requires an expression");

				var value = ParseSequence();

				if (!At(TokenKind.Newline) && !At(TokenKind.EOF))
					Error($"unexpected '{Current.Text}' after #Startup expression");

				return new StartupDirective(value) { Line = dirLine, Column = dirCol, File = dirFile, SourceOrder = sourceOrder };
			}

			// #App carries one object-literal block. The `{` must open on the directive's own line; the literal
			// itself may then span as many lines as it likes (ParseObjectLiteral skips newlines inside `{ }`).
			if (name.Equals("app", System.StringComparison.OrdinalIgnoreCase))
			{
				if (!programScope)
					ErrorAt(dirToken, "#App is only allowed at the top level of the main module");

				if (!At(TokenKind.LBrace))
					Error("#App requires a { key: value, ... } block opening on the same line");

				var value = (ObjectExpr)ParseObjectLiteral();

				if (!At(TokenKind.Newline) && !At(TokenKind.EOF))
					Error($"unexpected '{Current.Text}' after #App — a directive must be alone on its line");

				var appDir = new AppDirective(value) { Line = dirLine, Column = dirCol, File = dirFile, SourceOrder = sourceOrder };
				return appDir;
			}

			// #CSharp carries either one verbatim block token or a quoted path.
			if (name.Equals("csharp", System.StringComparison.OrdinalIgnoreCase))
			{
				var cs = ParseCSharpDirective();
				cs.Line = dirLine;
				cs.Column = dirCol;
				cs.File = dirFile;
				cs.SourceOrder = sourceOrder;
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
			DirectiveStmt dir;

			if (name.Equals("import", System.StringComparison.OrdinalIgnoreCase))
				dir = ParseImportDirective(args, argToks);
			else
			{
				ValidateDirectiveArgs(name, argToks);
				if (name.Equals("inputlevel", System.StringComparison.OrdinalIgnoreCase) && args.Length > 0)
				{
					if (!args.TryParseLong(out var level) || level is < 0 or > 100)
						ErrorAt(dirToken, "#InputLevel must be an integer from 0 through 100");
					args = level.ToString(CultureInfo.InvariantCulture);
				}
				dir = new DirectiveStmt(name, args);
			}

			dir.Line = dirLine;
			dir.Column = dirCol;
			dir.File = dirFile;
			dir.SourceOrder = sourceOrder;
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
			if (maxArgs == 0)
			{
				ErrorAt(toks[0], $"#{name} accepts no arguments");
				return;
			}

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
			bool quoted = false, reExport = false;
			if (toks.Count == 0)
				return new ImportDirective(args, module, alias, named, quoted, reExport);   // nameless `#import` — a no-op for the lowerer
			// Preserve imports from a module literally named Export. The word is a modifier only when another module
			// specifier follows it, rather than `{` or `as`.
			if (!_inFunction && toks.Count > 1 && toks[0].IsKeyword("Export")
				&& toks[1].LeadingWhitespace
				&& toks[1].Kind != TokenKind.LBrace
				&& !toks[1].IsKeyword("as"))
			{
				reExport = true;
				i++;
			}
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
			if (i < toks.Count && toks[i].IsKeyword("as"))
			{
				i++;
				if (i < toks.Count && toks[i].Kind == TokenKind.Identifier) alias = toks[i++].Text;
				else ErrorAt(i < toks.Count ? toks[i] : toks[^1], "expected an alias name after 'as' in #import");
			}
			if (i < toks.Count && toks[i].Kind == TokenKind.LBrace)
			{
				i++;
				var entries = new List<string>();
				bool Word(Token token) => (token.Kind is TokenKind.Identifier or TokenKind.Number)
					&& token.Text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c >= 0x80);
				string ReadName()
				{
					if (i >= toks.Count || !Word(toks[i]))
						ErrorAt(i < toks.Count ? toks[i] : toks[^1], "expected a name in #import member list");
					var result = toks[i++].Text;
					while (i < toks.Count && !toks[i].LeadingWhitespace && Word(toks[i])) result += toks[i++].Text;
					return result;
				}
				while (i < toks.Count && toks[i].Kind != TokenKind.RBrace)
				{
					// AutoHotkey permits empty entries and numeric names in a member list.
					if (toks[i].Kind == TokenKind.Comma) { i++; continue; }
					var entry = toks[i].Kind == TokenKind.Star ? toks[i++].Text : ReadName();
					if (entry != "*" && i < toks.Count && toks[i].LeadingWhitespace
						&& toks[i].IsKeyword("as"))
					{
						i++;
						entry += " as " + ReadName();
					}
					entries.Add(entry);
					if (i < toks.Count && toks[i].Kind is not (TokenKind.Comma or TokenKind.RBrace))
						ErrorAt(toks[i], $"unexpected '{toks[i].Text}' in #import member list");
				}
				if (i >= toks.Count) ErrorAt(toks[^1], "expected '}' after #import member list");
				i++;
				named = string.Join(", ", entries);
			}
			if (i < toks.Count)
				ErrorAt(toks[i], $"unexpected '{toks[i].Text}' after #import — a directive must be alone on its line");
			return new ImportDirective(args, module, alias, named, quoted, reExport);
		}

		// Strips a matching pair of surrounding quotes (' or ") from a string-token's text.
		private static string Unquote(string s) =>
			s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0] ? s.Substring(1, s.Length - 2) : s;

		// Maximum comma-separated arguments a directive accepts (at paren/bracket/brace depth 0). -1 means the rest of
		// the line is a single literal value (a path, message, version, or option string) where commas are literal
		// text, not argument separators. Values taken from the AHK v2 source (Script::IsDirective).
		private static int MaxDirectiveArgs(string name) => (Ascii.IsValid(name) ? name.ToLowerInvariant() : name) switch
		{
			"notrayicon" or "errorstdout" => 0,
			"warn" => 2,   // #Warn [Type], [Mode]
			"trayicon" => 2,   // #TrayIcon [FileName [, IconNumber]]
			"hotif" or "hotiftimeout" or "inputlevel" or "clipboardtimeout" or "maxthreads"
				or "maxthreadsperhotkey" or "maxthreadsbuffer" or "suspendexempt" or "usehook"
				or "singleinstance" or "structpack" or "persistent" => 1,
			_ => -1,
		};

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
			return new HotkeyDef(triggers, ParseCallable(ParseBodyStatement), null);
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
			return new HotstringDef(triggers, null, ParseCallable(ParseBodyStatement), null);
		}

		// ---- conditional-compilation preprocessor ----
		// Includes and conditional branches are expanded before parsing, so a branch may split a statement.
		// Each directive consumes its physical line; only active source tokens reach the parser.
		private List<Token> Preprocess(List<Token> sourceTokens, IReadOnlyList<string> sourceDiagnostics, string includeFile, string scriptDir)
		{
			scriptDir ??= System.IO.Path.GetDirectoryName(_mainScriptPath) ?? _includeDir;
			var moduleFile = sourceTokens.Count > 0 ? sourceTokens[0].File : null;
			var scopes = new Stack<(bool Definition, bool BackCompat)>();
			var modules = new Dictionary<string, (bool BackCompat, string File, bool Set)>(StringComparer.OrdinalIgnoreCase)
			{
				["__Init"] = (true, moduleFile, false)
			};
			var outp = new List<Token>(sourceTokens.Count);
			var includeDepth = 0;

			// --include precedes the main script and shares ordinary #Include deduplication.
			if (!string.IsNullOrEmpty(includeFile))
			{
				includeFile = System.IO.Path.GetFullPath(includeFile);

				if (!includeFile.IsNamedPipe() && !System.IO.File.Exists(includeFile))
					throw new Keysharp.Builtins.ParseException($"--include file not found: {includeFile}");

				if (_included.Add(includeFile))
				{
					var included = LexIncludedFile(includeFile, out var includedDiagnostics);
					MarkInclusion(included, 0);
					PreprocessFile(included, System.IO.Path.GetDirectoryName(includeFile), includedDiagnostics, true, "__Init");
					outp.Add(new Token(TokenKind.Newline, "\n", 0, 0, 0, 0, true, includeFile));
				}
			}

			PreprocessFile(sourceTokens, _includeDir, sourceDiagnostics, true, "__Init");
			return outp;

			void PreprocessFile(List<Token> src, string includeDir, IReadOnlyList<string> lexDiagnostics, bool backCompat, string module)
			{
				var lexDiagnostic = lexDiagnostics?.FirstOrDefault();
				var (diagnosticLine, diagnosticColumn) = LexDiagnosticPosition(lexDiagnostic);
				var diagnosticFile = src.Count > 0 ? src[0].File : null;

				// Keep the opening directive's index for unterminated-block diagnostics.
				var stack = new Stack<(bool parentActive, bool taken, bool active, bool seenElse, int dirIdx)>();
				bool Emit() => stack.Count == 0 || stack.Peek().active;

				int i = 0;
				var curDir = includeDir;
				while (i < src.Count)
				{
					var t = src[i];
					bool emit = Emit();
					if (lexDiagnostic != null && (t.Line > diagnosticLine || (t.Line == diagnosticLine && t.Column >= diagnosticColumn)))
					{
						if (emit)
							throw new Keysharp.Builtins.ParseException(PrefixDiagnostic(t.File ?? diagnosticFile, lexDiagnostic));
						lexDiagnostic = null;
					}
					var nameTok = t.Kind == TokenKind.Hash && i + 1 < src.Count ? src[i + 1] : default;
					if (_includeDir != null && emit && (nameTok.IsKeyword("include") || nameTok.IsKeyword("includeagain")))
					{
						int j = i + 2;
						var fileToks = _scratchListTokens.Get();
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
						i = j;
						var path = ResolveIncludePath(fileToks, curDir, t, backCompat, out var ignoreMissing);
						if (path == null) continue;
						var isPipe = path.IsNamedPipe();
						if (!isPipe && System.IO.Directory.Exists(path))
						{
							_scratchListTokens.Return(fileToks);
							curDir = path;
							continue;
						}
						// Missing *i paths also count as included; #IncludeAgain can retry them.
						if (!_included.Add(path) && !nameTok.IsKeyword("includeagain")) continue;
						if (!isPipe && !System.IO.File.Exists(path))
						{
							_scratchListTokens.Return(fileToks);
							if (!ignoreMissing) throw IncludeNotFound(t, path);
							continue;
						}
						List<Token> included;
						IReadOnlyList<string> diagnostics;
						try { included = LexIncludedFile(path, out diagnostics); }
						catch (Exception ex) when (isPipe && ex is System.IO.IOException or UnauthorizedAccessException)
						{
							_scratchListTokens.Return(fileToks);
							if (!ignoreMissing) throw IncludeNotFound(t, path);
							continue;
						}
						if (++includeDepth > MaxIncludeDepth)
						{
							_scratchListTokens.Return(fileToks);
							throw new Keysharp.Builtins.ParseException(Diagnostic(t,
								"Too many nested #include directives (possible circular #includeagain)"));
						}
						outp.Add(new Token(TokenKind.Newline, "\n", 0, 0, 0, 0, true, path));
						MarkInclusion(included, t.Inclusion);
						PreprocessFile(included, System.IO.Path.GetDirectoryName(path), diagnostics, backCompat, module);
						includeDepth--;
						_scratchListTokens.Return(fileToks);
						continue;
					}
					if (nameTok.Kind == TokenKind.Identifier && IsCondDirective(nameTok.Text))
					{
						var name = nameTok.Text;
						int j = i + 2;
						var cond = _scratchListTokens.Get();
						while (j < src.Count && src[j].Kind != TokenKind.Newline && src[j].Kind != TokenKind.EOF) cond.Add(src[j++]);
						// Validate conditions even when their branch is inactive.
						if (nameTok.IsKeyword("if"))
						{ bool c = EvalCond(cond, nameTok); bool v = emit && c; stack.Push((emit, v, v, false, i + 1)); }
						else if (nameTok.IsKeyword("define") || nameTok.IsKeyword("undef"))
						{
							if (emit && cond.Count > 0 && cond[0].Kind == TokenKind.Identifier)
							{
								if (nameTok.IsKeyword("define")) _defines.Add(cond[0].Text);
								else _defines.Remove(cond[0].Text);
							}
						}
						else
						{
							if (stack.Count == 0) ErrorAt(nameTok, $"#{name} without a matching #If");
							if (nameTok.IsKeyword("elif"))
							{
								var f = stack.Pop();
								if (f.seenElse) ErrorAt(nameTok, "#ElIf after #Else");
								bool c = EvalCond(cond, nameTok); bool v = f.parentActive && !f.taken && c;
								stack.Push((f.parentActive, f.taken || v, v, false, f.dirIdx));
							}
							else
							{
								bool isElse = nameTok.IsKeyword("else");
								if (cond.Count > 0)
									ErrorAt(cond[0], $"#{name} takes no condition" + (isElse ? " (did you mean #ElIf?)" : ""));
								var f = stack.Pop();
								if (isElse)
								{
									if (f.seenElse) ErrorAt(nameTok, "duplicate #Else");
									stack.Push((f.parentActive, true, f.parentActive && !f.taken, true, f.dirIdx));
								}
							}
						}
						i = j;   // leave the trailing newline to be emitted normally, preserving line separation
						_scratchListTokens.Return(cond);
						continue;
					}
					if (emit)
					{
						if (t.Kind == TokenKind.LBrace)
							scopes.Push((BeginsDefinitionBlock(src, i), backCompat));
						else if (t.Kind == TokenKind.RBrace && scopes.TryPop(out var scope) && scope.Definition)
							backCompat = scope.BackCompat;
						if (i + 2 < src.Count && nameTok.IsKeyword("requires") && RequiresBackCompat(src[i + 2].Text) is { } compatible)
						{
							backCompat = compatible;
							var mode = modules[module];
							if (!mode.Set && SourcePathComparer.Equals(mode.File, t.File) && !scopes.Any(scope => scope.Definition))
								modules[module] = (compatible, mode.File, true);
						}
						else if (i + 2 < src.Count && nameTok.IsKeyword("module"))
						{
							var argument = src[i + 2].Text;
							if (argument.Equals("__Main", StringComparison.OrdinalIgnoreCase) && SourcePathComparer.Equals(moduleFile, _mainScriptPath)) argument = "__Init";
							if (!modules.TryGetValue(argument, out var mode))
								modules[argument] = mode = (modules["__Init"].BackCompat, t.File, false);
							module = argument;
							backCompat = mode.BackCompat;
						}

						if (nameTok.IsKeyword("errorstdout"))
							_errorStdOut = true;

						if (nameTok.IsKeyword("csharp"))
							(_csharpDefineSnapshots ??= new()).Enqueue([.. _defines]);

						outp.Add(t);
					}
					i++;
				}
				if (lexDiagnostic != null && Emit())
					throw new Keysharp.Builtins.ParseException(PrefixDiagnostic(diagnosticFile, lexDiagnostic));
				if (stack.Count > 0)
				{
					var open = src[stack.Peek().dirIdx];
					ErrorAt(open, $"#{open.Text} without a matching #EndIf");
				}
			}

			string ResolveIncludePath(List<Token> fileToks, string baseDir, Token directive, bool backCompat, out bool ignoreMissing)
			{
				ignoreMissing = false;
				if (fileToks.Count == 0) return null;
				string file;
				// The lexer takes the rest of the line as one token; only the continuation-section form arrives as code.
				if (fileToks.Count == 1)
					file = fileToks[0].Text;
				else
				{
					var sb = new System.Text.StringBuilder();
					for (int k = 0; k < fileToks.Count; k++) { if (k > 0 && fileToks[k].LeadingWhitespace) sb.Append(' '); sb.Append(fileToks[k].Text); }
					file = sb.ToString();
				}
				// As in AutoHotkey: a matching pair of quote marks around the whole argument is dropped, then a leading "*i"
				// and the one space or tab after it mean "ignore the file if it cannot be read".
				file = file.Trim();
				if (file.Length >= 2 && file[0] is '"' or '\'' && file[^1] == file[0])
					file = file[1..^1];
				ignoreMissing = file.Length > 2 && file[0] == '*' && file[1] is 'i' or 'I' && file[2] is ' ' or '\t';
				if (ignoreMissing)
					file = file[3..];
				// Library names are literal; ordinary paths expand built-in variables.
				if (file.Length >= 2 && file[0] == '<' && file[^1] == '>')
				{
					var path = FindLibraryFile(file[1..^1].Trim(), scriptDir, libExts,
						directive.File == null ? baseDir : System.IO.Path.GetDirectoryName(directive.File), moduleFile, _mainScriptPath, backCompat);
					if (path == null && !ignoreMissing)
						throw new Keysharp.Builtins.ParseException(Diagnostic(directive, $"#Include library not found: {file}"));
					return path;
				}
				file = NormalizeDirectiveSeparators(ExpandPathVars(file, scriptDir, directive.File ?? _includeDir, _mainScriptPath));
				try { return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(baseDir, file)); }
				catch { if (ignoreMissing) return null; throw IncludeNotFound(directive, file); }
			}
		}

		private static (int Line, int Column) LexDiagnosticPosition(string diagnostic)
		{
			if (diagnostic == null) return (int.MaxValue, int.MaxValue);
			var first = diagnostic.IndexOf(':');
			var second = first < 0 ? -1 : diagnostic.IndexOf(':', first + 1);
			return first > 0 && second > first
				&& int.TryParse(diagnostic.AsSpan(0, first), out var line)
				&& int.TryParse(diagnostic.AsSpan(first + 1, second - first - 1), out var column)
				? (line, column)
				: (int.MaxValue, int.MaxValue);
		}

		// The lexer stamps each token with the file's full path for diagnostics and A_LineFile.
		// Numbers a splice and marks its tokens with that number, so each top-level statement knows which inclusion it came
		// from; parent is the inclusion holding the #Include.
		private void MarkInclusion(List<Token> tokens, int parent)
		{
			_includeParents.Add(parent);

			for (var i = 0; i < tokens.Count; i++)
				tokens[i] = tokens[i] with { Inclusion = _includeParents.Count };
		}

		private List<Token> LexIncludedFile(string path, out IReadOnlyList<string> lexDiagnostics)
		{
			var lexer = new Lexing.Lexer(_includedSources[path] = System.IO.File.ReadAllText(path), path);
			var toks = LexForParsing(lexer);
			lexDiagnostics = lexer.Diagnostics;
			if (toks.Count > 0 && toks[^1].Kind == TokenKind.EOF) toks.RemoveAt(toks.Count - 1);   // drop the included EOF
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

		/// <summary>
		/// Merges a line that starts with an expression operator onto the line above it, as AutoHotkey does before it
		/// parses (IsSOLContExpr), so `a` ⏎ `?? b` and `MsgBox` ⏎ `.Call(x)` read as one line in every statement form.
		/// It runs on the preprocessed stream, so the lines of an #if block which is not taken, and the #if lines
		/// themselves, are not there to separate a line from its continuation. Lines AutoHotkey never continues keep
		/// their break: a line and one from another file, a directive other than #HotIf/#Import/#Startup, a line ending in ':'
		/// (a label or case; a ternary's ':' continues in the parser), and a hotkey, hotstring or remap line.
		/// </summary>
		private static void JoinContinuationLines(List<Token> tokens)
		{
			int w = 0, lineStart = 0;   // compacted in place: w is the write index, lineStart the current line's first token
			var classBodies = new Stack<bool>();
			var classHeader = false;

			for (var r = 0; r < tokens.Count; r++)
			{
				var t = tokens[r];
				if ((t.IsKeyword("class") || t.IsKeyword("struct")) && r + 1 < tokens.Count && tokens[r + 1].Kind == TokenKind.Identifier)
					classHeader = true;
				if (t.Kind == TokenKind.LBrace) { classBodies.Push(classHeader); classHeader = false; }
				else if (t.Kind == TokenKind.RBrace && classBodies.Count > 0) classBodies.Pop();

				if (t.Kind == TokenKind.Newline)
				{
					var next = r + 1;
					while (next < tokens.Count && tokens[next].Kind == TokenKind.Newline) next++;

					if (w > lineStart && StartsContinuationLine(tokens, next) && tokens[next].File == tokens[w - 1].File
						&& !(classBodies.TryPeek(out var inClass) && inClass && StartsOperatorDeclaration(tokens, next))
						&& CanContinueLine(tokens, lineStart, w))
					{
						r = next - 1;
						continue;
					}

					lineStart = w + 1;
				}

				tokens[w++] = t;
			}

			tokens.RemoveRange(w, tokens.Count - w);
		}

		private static bool StartsOperatorDeclaration(List<Token> tokens, int index) =>
			index + 1 < tokens.Count && Keysharp.Runtime.OperatorCatalog.Supports(tokens[index].Text)
			&& tokens[index + 1].Kind == TokenKind.LParen
			&& KindAfterParen(tokens, index + 1) is TokenKind.FatArrow or TokenKind.LBrace;

		// AutoHotkey's CONTINUATION_LINE_SYMBOLS less `++`, `--` and `::`, and the word operators unless something
		// adjoins them (`and(x)` is a call). A word operator alone on its line, which AutoHotkey rejects, continues too.
		private static bool StartsContinuationLine(List<Token> tokens, int i)
		{
			if (i >= tokens.Count)
				return false;

			var t = tokens[i];

			return t.Kind switch
			{
				TokenKind.Identifier => continuationWords.Contains(t.Text) && i + 1 < tokens.Count
					&& (tokens[i + 1].LeadingWhitespace || tokens[i + 1].Kind is TokenKind.Newline or TokenKind.EOF),
				TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.DoubleColon => false,
				> TokenKind.Identifier and < TokenKind.HotkeyTrigger   // the punctuation and operator kinds
					=> "<>=/|^,?:.+-*&!~".Contains(t.Text[0]),
				_ => false
			};
		}

		private static readonly HashSet<string> continuationWords =
			new(System.StringComparer.OrdinalIgnoreCase) { "and", "or", "is", "in", "contains", "as" };

		private static bool CanContinueLine(List<Token> tokens, int start, int end)
		{
			if (tokens[start].Kind == TokenKind.Hash)
				return start + 1 < end && (tokens[start + 1].IsKeyword("hotif") || tokens[start + 1].IsKeyword("import")
					|| tokens[start + 1].IsKeyword("startup"));

			return tokens[end - 1].Kind is not (TokenKind.Colon or TokenKind.DoubleColon or TokenKind.HotstringExpansion
				or TokenKind.RemapTargetKey or TokenKind.CSharpBlock);
		}

		// A "line:col: message" parse error for a #include whose target can't be found, attributed to the directive's
		// position (ToCompilerError parses the line:col prefix so the user is pointed at the offending line).
		private static Keysharp.Builtins.ParseException IncludeNotFound(Token directive, string target) =>
			new(Diagnostic(directive, $"#Include file not found: {target}"));

		// File extensions tried for a library include, Keysharp-native first then AHK-compatible.
		private static readonly string[] libExts = { ".ks", ".ahk" };

		// The Lib folders searched after the script's own: the user libraries, then the standard library beside the
		// executable. Compiled output names a file in one by its marker rather than by its path.
		internal static IEnumerable<(string Dir, string Marker)> SharedLibraryDirs()
		{
			string docs = null, exeDir = null;
			// The accessor, not GetFolderPath: an unverified Documents path still counts (see A_MyDocuments).
			try { docs = Keysharp.Builtins.Accessors.A_MyDocuments; } catch { }
			try { exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath); } catch { }

			if (!string.IsNullOrEmpty(docs))
			{
				yield return (System.IO.Path.Combine(docs, "Keysharp", "Lib"), "<UserLib>/");
				yield return (System.IO.Path.Combine(docs, "AutoHotkey", "Lib"), "<UserLib>/");
			}

			if (!string.IsNullOrEmpty(exeDir))
				yield return (System.IO.Path.Combine(exeDir, "Lib"), "<StdLib>/");
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

		// Expands %BuiltInVar% references in a path string. The script's own variables come from the caller (the parse
		// context), which is the script being compiled, whatever script is running; the rest come from the Accessors
		// built-ins (restricted to includePathVars). An unrecognized %name% (or one that can't be resolved, such as the
		// file name of a script compiled from text) is left verbatim, matching AutoHotkey's "interpreted literally" rule.
		internal static string ExpandPathVars(string s, string scriptDir, string lineFile, string mainScriptPath)
		{
			if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s;
			return ExpandPathRegEx().Replace(s, m =>
			{
				var name = m.Groups[1].Value;
				if (!includePathVars.Contains(name)) return m.Value;
				if (name.Equals("A_ScriptDir", System.StringComparison.OrdinalIgnoreCase)) return scriptDir ?? m.Value;
				if (name.Equals("A_LineFile", System.StringComparison.OrdinalIgnoreCase)) return lineFile ?? m.Value;
				if (name.Equals("A_ScriptFullPath", System.StringComparison.OrdinalIgnoreCase)) return mainScriptPath ?? m.Value;
				if (name.Equals("A_ScriptName", System.StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetFileName(mainScriptPath) ?? m.Value;
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
			int p = 0, depth = 0;
			var result = Or();
			if (p < toks.Count) ErrorAt(toks[p], CondErr(dir, toks[p]));
			return result;

			bool Or()
			{
				var v = And();
				while (p < toks.Count && (toks[p].Kind == TokenKind.LogicalOr || toks[p].IsKeyword("or"))) { p++; v = And() || v; }
				return v;
			}

			bool And()
			{
				var v = Unary();
				while (p < toks.Count && (toks[p].Kind == TokenKind.LogicalAnd || toks[p].IsKeyword("and"))) { p++; v = Unary() && v; }
				return v;
			}

			bool Unary()
			{
				if (p >= toks.Count) { ErrorAt(dir, $"incomplete #{dir.Text} condition"); return false; }
				if (++depth > MaxCondDepth) ErrorAt(toks[p], $"#{dir.Text} condition is nested too deeply");

				try
				{
					if (toks[p].Kind == TokenKind.Not || toks[p].IsKeyword("not")) { p++; return !Unary(); }
					if (toks[p].Kind == TokenKind.LParen)
					{
						p++;
						var v = Or();
						if (p < toks.Count && toks[p].Kind == TokenKind.RParen) p++;
						else ErrorAt(dir, $"missing ')' in the #{dir.Text} condition");
						return v;
					}
					// Parse source literals invariantly. Only zero is false; an overflowing literal cannot be zero.
					if (toks[p].Kind == TokenKind.Number)
					{
						var r = toks[p++].Text;
						return r.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)
							   ? !long.TryParse(r.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) || hex != 0
							   : !double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) || num != 0;
					}

					if (toks[p].Kind == TokenKind.Identifier)
					{
						// A defined symbol wins over the true/false literals. Symbols are case-insensitive here, so
						// `#define FALSE` names the same thing as the literal `false`; letting the literal win would
						// silently flip a branch that used to be taken. Only when nothing defines the name do they read
						// as the value keywords they are everywhere else, so `#if true` keeps its block.
						var s = toks[p++].Text;
						return _defines.Contains(s) || s.Equals(Keywords.TrueTxt, System.StringComparison.OrdinalIgnoreCase);
					}

					ErrorAt(toks[p], CondErr(dir, toks[p]));
					return false;
				}
				finally { depth--; }
			}
		}

		// Recursion guard for the condition grammar, mirroring MaxExprDepth for expressions. Conditions are evaluated
		// even in excluded branches, so without this a deeply parenthesized one anywhere in a file — including in dead
		// code the script never uses — overflows the stack, and a StackOverflowException cannot be caught and turned
		// into a diagnostic: the whole process dies with nothing the user can act on.
		private const int MaxCondDepth = 250;

		private static string CondErr(Token dir, Token t) => $"unexpected '{t.Text}' in the #{dir.Text} condition "
			+ "(only defined symbols, true/false, integers, !, &&, ||, and parentheses are supported)";

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

		// A break, continue or goto target, read as AutoHotkey reads one: a `(` directly after the keyword makes it an
		// expression, and otherwise the rest of the line is the target as written, so `break "outer"` names a label
		// `"outer"`, which no script can declare.
		private (string Target, Expr Expression) ParseJumpTarget(string keyword)
		{
			if (At(TokenKind.LParen) && !Current.LeadingWhitespace)
			{
				Advance();
				var expression = ParseExpression(1);
				Expect(TokenKind.RParen, $"{keyword} target");
				return (null, expression);
			}

			var text = new System.Text.StringBuilder();

			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RBrace))
			{
				if (Current.LeadingWhitespace && text.Length > 0) text.Append(' ');
				text.Append(Advance().Text);
			}

			return (text.Length > 0 ? text.ToString() : null, null);
		}

		// `Goto Label` / `Goto(Expression)` / `Goto, Label`.
		private Stmt ParseGoto()
		{
			Advance(); // goto
			RejectAdjacentComma();
			Match(TokenKind.Comma);
			var (target, expression) = ParseJumpTarget("goto");
			if (target == null && expression == null) _ = ExpectIdentifier("goto target");
			return new GotoStmt(target, expression);
		}

		private Stmt ParseDecl()
		{
			var kw = Advance().Text.ToLowerInvariant();
			// `static name(...) {…}` / `static name(...) => expr` is a (static, non-capturing) nested function definition.
			if (IsFunctionDefinition()) return ParseFunctionDecl(isStatic: kw == "static");
			var items = new List<Expr>();
			// A trailing comma continues a decl list onto the next line (`X := 1,` ⏎ `Y`).
			// `global`/`local` alone (assume-global/local) has no items and must not eat the next statement.
			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RBrace))
			{
				items.Add(ParseExpression(1));
				if (!Match(TokenKind.Comma)) break;
				SkipNewlines();
			}
			return new DeclStmt(kw, items);
		}

		private Stmt ParseClass(bool isStruct = false)
		{
			var start = Current;   // positions a nested class too, which ParseStatement does not stamp
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
			var operatorTokens = new Dictionary<ClassMethod, Token>();
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
						structPack = long.TryParse((dir.Args ?? "").AsSpan().Trim(), out var sp) ? sp : 0;
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
				// The modifier always precedes a member name or operator, so `Static := false` and `Static => 1` declare
				// members named Static.
				var isStatic = AtKeyword("static") && (Peek(1).Kind == TokenKind.Identifier || IsOperatorDeclaration(1));
				if (isStatic) Advance();

				if (IsOperatorDeclaration(0))
				{
					var symbolToken = Advance();
					var symbol = symbolToken.Text;
					var ps = ParseParamList();
					var label = isStatic ? "Static operator" : "Operator";
					if (!Keysharp.Runtime.OperatorCatalog.TryResolve(symbol, ps.Count + 1, out _))
					{
						var counts = Keysharp.Runtime.OperatorCatalog.All.Where(op => op.Symbol == symbol).Select(op => op.Arity - 1).Order().ToArray();
						var expected = counts.Length == 1 ? counts[0] == 0 ? "no parameters" : "one parameter" : "zero or one parameter";
						ErrorAt(symbolToken, $"{label} '{symbol}' requires {expected}");
					}
					if (ps.Any(p => p.ByRef || p.Variadic || p.Optional || p.Default != null))
						ErrorAt(symbolToken, $"{label} '{symbol}' parameters cannot be optional, ByRef, variadic, or have defaults");
					if (methods.Any(m => m.IsOperator && m.Static == isStatic && m.Name == symbol && m.Params.Count == ps.Count))
					{
						var count = ps.Count == 1 ? "1 parameter" : $"{ps.Count} parameters";
						ErrorAt(symbolToken, $"Duplicate {(isStatic ? "static " : "")}operator '{symbol}' with {count}");
					}
					SkipNewlines();
					ClassMethod method;
					if (Match(TokenKind.FatArrow))
						method = new ClassMethod(symbol, ps, null, ParseCallable(() => ParseExpression(1)), isStatic, true);
					else
						method = new ClassMethod(symbol, ps, ParseCallable(ParseBlock), null, isStatic, true);
					methods.Add(method);
					operatorTokens.Add(method, symbolToken);
				}
				else if (IsFunctionDefinition())
				{
					var mname = Advance().Text;
					var ps = ParseParamList();
					SkipNewlines();
					if (Match(TokenKind.FatArrow))
						methods.Add(new ClassMethod(mname, ps, null, ParseCallable(() => ParseExpression(1)), isStatic));
					else
						methods.Add(new ClassMethod(mname, ps, ParseCallable(ParseBlock), null, isStatic));
				}
				else if (At(TokenKind.Identifier))
				{
					// A single line may declare several comma-separated fields that share its static/instance
					// scope, e.g. `a := 1, b := 2`, `static x := 1, y := 2`, or `x : Int32, y : Int32`. A `{…}`
					// property body or `=>` shorthand getter is a complete member on its own and never continues
					// past a comma.
					while (true)
					{
						var nameToken = Advance();
						var fname = nameToken.Text;
						var indexParams = At(TokenKind.LBracket) ? ParseParamList(TokenKind.LBracket, TokenKind.RBracket) : new List<Param>();
						var save = _pos;
						SkipNewlines();
						if (At(TokenKind.LBrace))   // property body, possibly with the brace on the next line
						{
							if (!isStatic && indexParams.Count == 0 && fname.Equals("union", System.StringComparison.OrdinalIgnoreCase) && IsUnionBlock())
								ParseUnionBlock(fields, structPack);
							else
								properties.Add(ParsePropertyBody(fname, indexParams, isStatic));
							break;
						}
						_pos = save;   // field initializer / shorthand getter are on the same line
						if (At(TokenKind.Dot) && indexParams.Count == 0)   // member-target init: `static Template.Framework := X`
						{
							var target = ParsePostfix(new MemberExpr(new NameExpr("this"), fname, false));
							if (!Match(TokenKind.Assign)) Error($"expected ':=' in class member initializer but found {Got()}");
							var stmt = new ExpressionStmt(new AssignExpr(":=", target, ParseExpression(1)))
							{
								Line = nameToken.Line, Column = nameToken.Column, File = nameToken.File
							};
							(isStatic ? staticInits : instanceInits).Add(stmt);
						}
						else if (Match(TokenKind.FatArrow))
						{
							properties.Add(new ClassProperty(fname, indexParams, null, ParseCallable(() => ParseExpression(1)), null, null, isStatic));
							break;
						}
						else if (At(TokenKind.Colon))   // typed struct field `name : Type [:= init]`
						{
							Advance();
							var typeExpr = ParseFieldType();
							Expr tinit = Match(TokenKind.Assign) ? ParseExpression(1) : null;
							fields.Add(new ClassField(fname, tinit, isStatic, typeExpr, structPack));
						}
						else
						{
							Expr init = Match(TokenKind.Assign) ? ParseExpression(1) : null;
							fields.Add(new ClassField(fname, init, isStatic));
						}
						// Another comma-separated field may follow, after a trailing comma on the next line.
						var commaSave = _pos;
						if (!Match(TokenKind.Comma)) break;
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
			foreach (var method in methods.Where(m => m.IsOperator))
			{
				if (Keysharp.Runtime.OperatorCatalog.TryResolve(method.Name, method.Params.Count + 1, out var kind)
					&& Keysharp.Runtime.OperatorCatalog.Get(kind).RequiredPartner is { } partner
					&& !methods.Any(m => m.IsOperator && m.Static == method.Static && m.Name == partner))
				{
					var label = method.Static ? "Static operator" : "Operator";
					var required = method.Static ? "static operator" : "operator";
					ErrorAt(operatorTokens[method], $"{label} '{method.Name}' requires {required} '{partner}' in the same class");
				}
			}
			Expect(TokenKind.RBrace, isStruct ? "struct body" : "class body");
			return new ClassDecl(name, baseName, fields, methods, properties, nested, isStruct)
			{
				Requires = classRequires, Imports = classImports, StaticInit = staticInits, InstanceInit = instanceInits, CSharpBlocks = classCSharp,
				Line = start.Line, Column = start.Column, File = start.File,
			};
		}

		// v2.1-alpha.30 permits a parenthesized expression, or a name followed by calls, property access and indexing.
		// It is evaluated in the class's static initializer with `this` bound to the class object.
		private Expr ParseFieldType() =>
			At(TokenKind.LParen) ? ParsePostfix(ParsePrimary()) : ParsePostfix(new NameExpr(ExpectIdentifier("struct field type")));

		// `union {` opens a union block unless the block is empty or starts as a property body: "union" is also a valid
		// property name, so those keep their meaning. [v2.1-alpha.34+]
		private bool IsUnionBlock()
		{
			var save = _pos;
			Advance();   // {
			SkipNewlines();
			var union = !At(TokenKind.RBrace) && !((AtKeyword("get") || AtKeyword("set")) && Peek(1).Kind != TokenKind.Colon);
			_pos = save;
			return union;
		}

		// A union block holds only typed field declarations, each but the first taking the first one's offset, so the
		// union is as large as its largest member. [v2.1-alpha.34+]
		private void ParseUnionBlock(List<ClassField> fields, long structPack)
		{
			Expect(TokenKind.LBrace, "union block");
			SkipNewlines();
			string first = null;
			while (!At(TokenKind.RBrace) && !At(TokenKind.EOF))
			{
				if (!At(TokenKind.Identifier) || Peek(1).Kind != TokenKind.Colon)
					Error($"a union block may contain only typed property declarations, but found {Got()}");
				var name = Advance().Text;
				Advance();   // :
				var typeExpr = ParseFieldType();
				Expr init = Match(TokenKind.Assign) ? ParseExpression(1) : null;
				fields.Add(new ClassField(name, init, false, typeExpr, structPack, first));
				first ??= name;
				_ = Match(TokenKind.Comma);
				SkipNewlines();
			}
			Expect(TokenKind.RBrace, "union block");
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
				if (AtKeyword("get")) { Advance(); if (Match(TokenKind.FatArrow)) getArrow = ParseCallable(() => ParseExpression(1)); else { SkipNewlines(); getBody = ParseCallable(ParseBlock); } }
				else if (AtKeyword("set")) { Advance(); if (Match(TokenKind.FatArrow)) setArrow = ParseCallable(() => ParseExpression(1)); else { SkipNewlines(); setBody = ParseCallable(ParseBlock); } }
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
			var i = _pos + 1;   // past the leading identifier
			bool sawDot = false, sawBracket = false;
			while (i < _t.Count)
			{
				var t = _t[i];
				if (t.Kind == TokenKind.Dot && !t.LeadingWhitespace)
				{ sawDot = true; i++; if (i < _t.Count && _t[i].Kind == TokenKind.Identifier) i++; continue; }     // .member
				if (t.Kind == TokenKind.LBracket && !t.LeadingWhitespace)
				{ sawBracket = true; var d = 1; i++; while (i < _t.Count && d > 0) { if (_t[i].Kind == TokenKind.LBracket) d++; else if (_t[i].Kind == TokenKind.RBracket) d--; i++; } continue; }  // [index]
				break;
			}
			// A comma glued to a name or member chain is the v1 command syntax (`MsgBox, "a"`), which would otherwise
			// read `MsgBox` as a variable and call nothing at all. AutoHotkey rejects it because the word starting the
			// line ends at the comma rather than at whitespace, and for the same reason exempts a chain that indexes
			// (`arr[1], x := 1`) or calls (`fn(), x := 1`), whose leading word ends at the '[' or '('.
			if (i < _t.Count && _t[i].Kind == TokenKind.Comma && !_t[i].LeadingWhitespace && !sawBracket)
				ErrorAt(_t[i], commandCommaMessage);
			if (verbalOps.Contains(Current.Text)) return false;   // `not x`, `a and b` are operators, not commands
			// A primary-expression chain (`name`, `obj.method`) ending the line is a zero-arg function-call statement
			// (`ExitApp`, `obj.Method`). An index-access end (`arr[i]`) is an expression statement, not a call (mirrors
			// the canonical isFunctionCallStatement, which rejects a trailing CloseBracket).
			if (i >= _t.Count || _t[i].Kind == TokenKind.Newline || _t[i].Kind == TokenKind.EOF || _t[i].Kind == TokenKind.RBrace)
				return _t[i - 1].Kind != TokenKind.RBracket && (sawDot || _t[i - 1].Kind == TokenKind.Identifier);
			var next = _t[i];
			if (next.Kind == TokenKind.LParen && !next.LeadingWhitespace) return false;   // Name(...) call expression
			// A command-call statement (`MsgBox "x", "y"`) requires the name/chain to be followed by whitespace (then an
			// argument or a leading-comma omitted first argument), a comment, or end-of-line. Anything ADJACENT —
			// `:=` `.` `[` … — makes it an expression statement instead, so `a[i], b := 1` is a comma-sequence, while
			// `MsgBox ,"x"` (space before the comma) is a call with the first arg omitted.
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
			var callee = ParsePostfix(ParsePrimary(), commandName: true);   // name / member / index — no adjacent '(' (ensured by detection)
			return new ExpressionStmt(new CallExpr(callee, ParseCommandArgs()));
		}

		// Comma-separated command arguments, ending at end of line / '}'. Supports omitted args. A trailing comma
		// continues onto the next line.
		private List<Argument> ParseCommandArgs()
		{
			var args = new List<Argument>();
			var named = _scratchHashStrings.Get();

			while (!At(TokenKind.Newline) && !At(TokenKind.EOF) && !At(TokenKind.RBrace))
			{
				args.Add(ParseArgument(args, named));
				if (!Match(TokenKind.Comma)) break;
				SkipNewlines();
			}

			_scratchHashStrings.Return(named);
			return args;
		}

		private bool IsOperatorDeclaration(int k) =>
			Keysharp.Runtime.OperatorCatalog.Supports(Peek(k).Text) && Peek(k + 1).Kind == TokenKind.LParen;

		private bool IsFunctionDefinition()
		{
			if (!At(TokenKind.Identifier)) return false;
			var lp = Peek(1);
			return lp.Kind == TokenKind.LParen && !lp.LeadingWhitespace
				&& KindAfterParen(_t, _pos + 1) is TokenKind.LBrace or TokenKind.FatArrow;
		}

		private Stmt ParseFunctionDecl(bool isStatic = false)
		{
			var nameTok = Current;
			var name = Advance().Text;
			RejectReservedName(name, nameTok, "a function name");
			var ps = ParseParamList();
			SkipNewlines();
			var fn = Match(TokenKind.FatArrow)
				? new FunctionDecl(name, ps, null, ParseCallable(() => ParseExpression(1)), isStatic)
				: new FunctionDecl(name, ps, ParseCallable(ParseBlock), null, isStatic);
			// Named hotkey and hotstring callbacks bypass ParseStatement.
			(fn.Line, fn.Column, fn.File) = (nameTok.Line, nameTok.Column, nameTok.File);
			return fn;
		}

		private List<Param> ParseParamList() => ParseParamList(TokenKind.LParen, TokenKind.RParen);

		private List<Param> ParseParamList(TokenKind open, TokenKind close)
		{
			Expect(open, "parameter list");
			var inFlowCond = EnterGroup();
			var ps = new List<Param>();
			SkipNewlines();
			while (!At(close) && !At(TokenKind.EOF))
			{
				var byRef = Match(TokenKind.BitAnd);
				if (!byRef && At(TokenKind.Star))   // anonymous variadic `*`; its C# parameter gets an internal name
				{
					Advance();
					ps.Add(new Param(Keywords.InternalPrefix + "args", null, false, true, false, anonymous: true));
					SkipNewlines();
					break;
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
				// As in AutoHotkey, a variadic parameter ends the list.
				if (variadic || !Match(TokenKind.Comma)) break;
				SkipNewlines();
			}
			Expect(close, "parameter list");
			LeaveGroup(inFlowCond);
			return ps;
		}

		// ---- expressions (Pratt) ----

		// Binding power: higher = binds tighter. Taken from the AHK v2 operator-precedence order.
		private const int PrecPower = 17;

		private readonly struct Infix
		{
			public readonly int Prec; public readonly bool Right; public readonly bool Assign;
			public Infix(int prec, bool right = false, bool assign = false) { Prec = prec; Right = right; Assign = assign; }
		}

		private static Infix GetInfix(Token t)
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
					return new Infix(1, true, true);

				case TokenKind.NullCoalesce: return new Infix(3, true);
				case TokenKind.LogicalOr: return new Infix(4);
				case TokenKind.LogicalAnd: return new Infix(5);

				case TokenKind.Equal: case TokenKind.Identity:
				case TokenKind.NotEqual: case TokenKind.NotIdentity:
					return new Infix(7);

				case TokenKind.Less: case TokenKind.Greater:
				case TokenKind.LessEqual: case TokenKind.GreaterEqual:
					return new Infix(8);

				case TokenKind.RegexMatch: case TokenKind.NotRegexMatch:
					return new Infix(9);

				case TokenKind.Dot:   // Member-access dots are consumed by ParsePostfix.
					return new Infix(10);

				case TokenKind.BitOr: return new Infix(11);
				case TokenKind.BitXor: return new Infix(12);
				case TokenKind.BitAnd: return new Infix(13);

				case TokenKind.ShiftLeft: case TokenKind.ShiftRight: case TokenKind.ShiftRightLogical:
					return new Infix(14);

				case TokenKind.Plus: case TokenKind.Minus: return new Infix(15);
				case TokenKind.Star: case TokenKind.Slash: case TokenKind.IntDiv: return new Infix(16);
				case TokenKind.Power: return new Infix(PrecPower, true);

				case TokenKind.Identifier:
					if (t.IsKeyword("or")) return new Infix(4);
					if (t.IsKeyword("and")) return new Infix(5);
					if (t.IsKeyword("is") || t.IsKeyword("in") || t.IsKeyword("contains"))
						return new Infix(6);
					return default;

				default: return default;
			}
		}

		private Expr ParseExpression(int minPrec)
		{
			// Guard against unbounded recursion from unsupported/malformed continuations (e.g. non-string `(…)`
			// continuation sections) so a bad parse fails cleanly instead of overflowing the stack.
			if (_exprDepth >= MaxExprDepth)
				Error("expression nested too deeply (unterminated grouping or unsupported continuation?)");
			_exprDepth++;
			// Positioned like a statement, so code the lowering runs outside any statement, such as a case value or a
			// field initializer, still has a line to report.
			var start = Current;
			try
			{
				var e = ParseExpressionCore(minPrec);
				if (e != null && e.Line == 0) (e.Line, e.Column, e.File) = (start.Line, start.Column, start.File);
				return e;
			}
			finally { _exprDepth--; }
		}

		private Expr ParseExpressionCore(int minPrec)
		{
			var left = ParseUnary();

			while (true)
			{
				if (_groupDepth > 0) SkipNewlines();
				var t = Current;

				// Ternary `? :` (prec 2, right-assoc) and the maybe-operator `x?`.
				if (t.Kind == TokenKind.Question)
				{
					if (2 < minPrec) break;
					Advance();   // consume '?'
					// `?` is the maybe (unset-permissive) operator when the next SIGNIFICANT token (skipping newlines;
					// whitespace/comments are already stripped) is one of ) ] } , : ? . or a statement-starting
					// keyword, or the % closing a %…% it is inside; otherwise it's the ternary conditional. (So
					// `x := 1?\n2:\n3` is the ternary 1?2:3.)
					var qSave = _pos;
					SkipNewlines();
					if (At(TokenKind.RParen) || At(TokenKind.RBracket) || At(TokenKind.RBrace) || At(TokenKind.Comma)
						|| At(TokenKind.Colon) || At(TokenKind.Question) || At(TokenKind.Dot) || At(TokenKind.EOF)
						|| _inDerefInner && At(TokenKind.Percent) || IsStatementKeyword())
					{
						_pos = qSave;   // leave any trailing newline in place — the maybe value ends here
						left = new UnaryExpr("?", left, true);
						continue;
					}
					var then = ParseExpression(1);
					Expect(TokenKind.Colon, "ternary");
					SkipNewlines();
					var els = ParseExpression(1);
					left = new TernaryExpr(left, then, els);
					continue;
				}

				// `expr*` spread (argument/array): a trailing '*' before a terminator is not multiplication.
				if (t.Kind == TokenKind.Star && IsSpreadStar()) break;

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

		// Whether a token can start the right side of an auto-concatenation (`a b`). Inside `% … %` a spaced '%' is the
		// closing delimiter, as in `% "Name" %.Show()`.
		private bool IsImplicitConcatStart(Token t) => t.Kind switch
		{
			TokenKind.String or TokenKind.Number or TokenKind.LParen => true,
			TokenKind.Percent => !_inDerefInner,
			TokenKind.Identifier => !verbalOps.Contains(t.Text),
			_ => false
		};

		// A line ending in an operator continues onto the next, as AutoHotkey's EndsWithOperator does, so a '*' that
		// ends a line is a spread only when the next line cannot continue the expression: it closes a bracket, starts
		// with nothing an operand can begin with, or is a control-flow statement, class or label (`default:`). Inside
		// brackets only the first holds.
		private bool IsSpreadStar()
		{
			var i = 1;
			while (Peek(i).Kind == TokenKind.Newline) i++;
			var next = Peek(i);

			if (next.Kind is TokenKind.RParen or TokenKind.RBracket or TokenKind.RBrace or TokenKind.Comma or TokenKind.EOF)
				return true;
			if (i == 1 || _groupDepth > 0) return false;
			if (!CanStartOperand(next)) return true;
			if (next.Kind != TokenKind.Identifier) return false;

			var after = Peek(i + 1);
			return statementKeywords.Contains(next.Text) || next.IsKeyword("case")
				|| (next.IsKeyword("class") || next.IsKeyword("struct")) && after.Kind == TokenKind.Identifier
				|| after.Kind == TokenKind.Colon && !after.LeadingWhitespace && Peek(i + 2).Kind is TokenKind.Newline or TokenKind.EOF;
		}

		private static bool CanStartOperand(Token t) => t.Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.String
			or TokenKind.LParen or TokenKind.LBracket or TokenKind.LBrace or TokenKind.Percent or TokenKind.Minus
			or TokenKind.Plus or TokenKind.Not or TokenKind.BitNot or TokenKind.BitAnd or TokenKind.PlusPlus
			or TokenKind.MinusMinus;

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

		// A '[' indexes even after a space, as in AutoHotkey (`m ["k"]`), except after a command-style call's name, where it
		// opens the first argument (`MsgBox [1, 2]`).
		private Expr ParsePostfix(Expr e, bool commandName = false)
		{
			while (true)
			{
				var t = Current;
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
				if (t.Kind == TokenKind.LBracket && !(commandName && t.LeadingWhitespace))
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
						return ParseNameBuild(t, new LiteralExpr(LiteralKind.String, "\"" + t.Text + "\""));
					return new NameExpr(t.Text, t.Line) { File = t.File };
				case TokenKind.LParen:
					Advance();
					var inFlowCond = EnterGroup();
					var inner = ParseSequence();
					LeaveGroup(inFlowCond);
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
						return ParseNameBuild(t, nameExpr);
					return new DerefExpr(nameExpr) { Line = t.Line, Column = t.Column, File = t.File };
				default:
					Error($"unexpected {Got()}");
					return null;
			}
		}

		// Dynamic name-building: a run of adjacent identifier/number/%expr% parts forms a variable name at
		// runtime (`z%y%` -> deref of "z" . y). Lowered as a DerefExpr over the concatenation of the parts.
		private Expr ParseNameBuild(Token start, Expr firstPart)
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
			return new DerefExpr(name) { Line = start.Line, Column = start.Column, File = start.File };
		}

		/// <param name="namedError">
		/// Null when named arguments are allowed. Otherwise what to say about the `name:` that was found -- the two
		/// bracketed forms are wrong for different reasons, and a single message would misdiagnose one of them.
		/// </param>
		private List<Argument> ParseArgs(TokenKind open, TokenKind close, string namedError = null)
		{
			Expect(open, "argument list");
			var inFlowCond = EnterGroup();
			var args = new List<Argument>();
			var named = _scratchHashStrings.Get();
			SkipNewlines();
			// Comma-separated slots: each slot is an expression or omitted (empty). A TRAILING comma is ignored (it does
			// not add a slot), so `f(a,)`/`f(x,&y,)` keep their arg count and `f()` is zero, while `f(,a)`/`f(a,,b)` keep
			// the leading/interior omitted slots and `f(,)` is a single omitted arg.
			if (!At(close) && !At(TokenKind.EOF))
			{
				while (true)
				{
					args.Add(ParseArgument(args, named, namedError));
					SkipNewlines();
					if (!Match(TokenKind.Comma)) break;
					SkipNewlines();
					if (At(close)) break;   // trailing comma: no extra slot
				}
			}
			Expect(close, "argument list");
			LeaveGroup(inFlowCond);
			_scratchHashStrings.Return(named);
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

		private Argument ParseArgument(List<Argument> args, HashSet<string> names, string namedError = null)
		{
			// Named arguments are trailing, including names resolved at runtime.
			var followsNamed = args.Count > 0 && args[^1].IsNamed;
			if (At(TokenKind.Comma) || At(TokenKind.EOF))
			{
				if (followsNamed) Error("an omitted argument cannot follow a named argument");
				return new Argument(null, false);
			}

			var name = TryTakeArgName(namedError, names);
			var value = ParseExpression(1);
			var nameExpr = name == null ? TryTakeDynamicArgName(namedError, ref value) : null;
			var spread = Match(TokenKind.Star);
			var named = name != null || nameExpr != null;

			if (spread && named)
				Error("a named argument cannot be spread with '*'");

			if (!named && followsNamed)
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
			var inFlowCond = EnterGroup();
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
			LeaveGroup(inFlowCond);
			return new MapExpr(entries);
		}

		// Object literal { name: value, "str": value, ... }. Only reached in expression position
		// (statement-position '{' is parsed as a block), so no lexer disambiguation is needed.
		private Expr ParseObjectLiteral()
		{
			Expect(TokenKind.LBrace, "object literal");
			var inFlowCond = EnterGroup();
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
			LeaveGroup(inFlowCond);
			return new ObjectExpr(entries);
		}

		// ---- fat-arrow lambdas ----

		private bool IsFatArrowHead()
		{
			var i = _pos;
			if (At(TokenKind.Identifier))
			{
				if (Peek(1).Kind == TokenKind.FatArrow) return true;
				if (Peek(1).Kind != TokenKind.LParen || Peek(1).LeadingWhitespace) return false;
				i++;
			}
			else if (!At(TokenKind.LParen)) return false;
			// Outside brackets a `{` on a later line opens a block after the call (`x := f()`), not a function body.
			var next = KindAfterParen(_t, i, skipNewlines: _groupDepth > 0);
			return next == TokenKind.FatArrow || (next == TokenKind.LBrace && !_inFlowCond);
		}

		private static TokenKind KindAfterParen(List<Token> tokens, int i, bool skipNewlines = true)
		{
			// i is at '('
			var depth = 0;
			for (; i < tokens.Count; i++)
			{
				var k = tokens[i].Kind;
				if (k == TokenKind.LParen) depth++;
				else if (k == TokenKind.RParen) { depth--; if (depth == 0) { i++; break; } }
				else if (k == TokenKind.EOF) return TokenKind.EOF;
			}
			while (skipNewlines && i < tokens.Count && tokens[i].Kind == TokenKind.Newline) i++;
			return i < tokens.Count ? tokens[i].Kind : TokenKind.EOF;
		}

		private Expr ParseFatArrow()
		{
			// Optional name for a named fn `name(params) => …` / `name(params) { … }`: captured so the body can recurse
			// by that name (resolved to the lambda itself in the lowerer).
			string faName = null;
			var start = Current;
			if (At(TokenKind.Identifier) && Peek(1).Kind == TokenKind.LParen && !Peek(1).LeadingWhitespace)
			{ var fnTok = Current; faName = Advance().Text; RejectReservedName(faName, fnTok, "a function name"); }
			List<Param> ps;
			if (At(TokenKind.Identifier))   // single bare-name parameter: `x => …`
			{ var pTok = Current; RejectReservedName(pTok.Text, pTok, "a parameter name"); ps = new List<Param> { new Param(Advance().Text, null, false, false, false) }; }
			else
				ps = ParseParamList();
			if (_groupDepth > 0) SkipNewlines();
			if (At(TokenKind.LBrace))   // anonymous block-bodied function `(params) { … }`
				return new FatArrowExpr(ps, ParseCallable(ParseBlock)) { Name = faName, Line = start.Line, Column = start.Column, File = start.File };
			Expect(TokenKind.FatArrow, "fat-arrow function");
			return new FatArrowExpr(ps, ParseCallable(() => ParseExpression(1))) { Name = faName, Line = start.Line, Column = start.Column, File = start.File };
		}

		[GeneratedRegex("%([A-Za-z_][A-Za-z0-9_]*)%")]
		private static partial Regex ExpandPathRegEx();
	}
}
