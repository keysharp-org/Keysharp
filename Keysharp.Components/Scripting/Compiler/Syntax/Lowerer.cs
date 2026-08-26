using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Keysharp.Components.Scripting;
using Keysharp.Parsing.Syntax;
using Keysharp.Runtime;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.Mouse;

namespace Keysharp.Compilation.Syntax
{
	/// <summary>
	/// Lowering pass: AST -> Roslyn CompilationUnit, built directly with SyntaxFactory (no text
	/// round-trips), matching the conventions the Keysharp runtime expects (Script.Invoke /
	/// Script.&lt;Operator&gt; / Script.IfTest, FN_ methods, Func-bound fields). Reuses shared
	/// infrastructure (the builtin registry, runtime operator methods, CompilerHelper.Compile).
	///
	/// Everything is emitted fully-qualified, so no `using` directives are produced and the compiler
	/// binds the structural tree directly.
	///
	/// Identifier model: in AutoHotkey a name is one case-insensitive slot, and a function name IS a
	/// variable holding the KeysharpFunc — so every global reference lowers to a single static field named
	/// by the lowercased identifier. Locals (params + names assigned in a function) become hoisted
	/// method locals; names go through <see cref="NameMangler"/>.
	/// </summary>
	internal sealed class Lowerer
	{
		public readonly List<string> Diagnostics = new();
		// Non-fatal compile-time messages (#Warning). Kept apart from Diagnostics because anything in that list aborts
		// the build; these are surfaced alongside it and compilation continues. Named for the compile phase to keep it
		// distinct from `_warnings` below, which is the unrelated #Warn analysis emitted into the generated program.
		public readonly List<string> CompileWarnings = new();

		// Global slot table: lowercased name -> emitted C# field identifier (keyword-escaped).
		private readonly Dictionary<string, string> _fields = new(System.StringComparer.Ordinal);
		// Names resolved INLINE to a fresh expression each reference (e.g. `#import __Main` self-import → `new __Main()`).
		private readonly Dictionary<string, System.Func<ExpressionSyntax>> _inlineAliases = new(System.StringComparer.Ordinal);
		private readonly List<MemberDeclarationSyntax> _fieldDecls = new();
		// Where a static-LOCAL variable's backing field is emitted: a function's static-locals live in the C# type that
		// holds the function's impl, so a method's `static x` goes in its CLASS (not flat in __Main, where two same-named
		// methods in different classes would collide). Defaults to _fieldDecls (module class); LowerClass redirects it to
		// the class's own field list while lowering that class's members. `_staticFieldDeclIdx` indexes into THIS list.
		private List<MemberDeclarationSyntax> _staticFieldSink;
		private readonly Dictionary<string, string> _userFuncByLower = new(System.StringComparer.Ordinal);
		// The same top-level functions, keeping their declarations so #Warn NamedArg can check a named argument
		// against the callee's parameter list.
		private readonly Dictionary<string, FunctionDecl> _userFuncDeclByLower = new(System.StringComparer.Ordinal);
		// The same for classes, so a constructor call `MyClass(x: 1)` can be checked against __New's parameters.
		private readonly Dictionary<string, ClassDecl> _userClassDeclByLower = new(System.StringComparer.Ordinal);
		private readonly Dictionary<string, string> _userClassByLower = new(System.StringComparer.Ordinal);
		private bool _inMethod;
		private string _currentClassBase;   // base type name of the class being lowered (for `super`)
		// Dotted C# path of the class currently being lowered (e.g. "Outer.Inner"), or null at module scope. Used to make
		// a class method's static-local one-time-init guard key unique per declaring type (the C# field can repeat across
		// classes now that it lives in its own class), so two same-named methods' statics don't share an init guard.
		private string _currentClassPath;
		private string _currentClassUserPath;
		private string _structTypeName;     // C# type name of the struct being lowered (for typed-field DefineProp)
		private bool _currentMethodStatic;   // the method being lowered is static (super -> Statics vs Prototypes)
		private string _currentThisFuncName;
		private bool _loweringParamDefault;
		private int _flowCounter;

		// Enclosing-loop stack for break/continue with a level/label. Each loop gets an id (its KS_e<id> base); the
		// optional source Label is the `name:` that preceded the loop. NeedsNext/NeedsEnd record whether a targeted
		// continue/break referenced this loop so we only emit the (otherwise-unused) `_next`/`_end` labels when needed.
		private sealed class LoopFrame { public int Id; public string Label; public bool NeedsNext; public bool NeedsEnd; }
		private readonly List<LoopFrame> _loopFrames = new();
		private string _pendingLoopLabel;   // a `name:` immediately preceding the next loop (its break/continue target)
		// Stack of label name-sets for the currently-open blocks (a real C# `switch` contributes one set shared by all
		// its cases). A `goto` emits a real C# goto only when its target is in some active scope (a valid C# jump).
		private readonly List<HashSet<string>> _labelScopes = new();
		private bool _inDerefFunc;   // inside a deref-using function: %name% routes through the local GetVar/SetVar functions
		private int _lambdaCounter;
		private readonly List<MemberDeclarationSyntax> _pendingLambdas = new();   // anonymous fat-arrow functions
		// C# local functions emitted for fat-arrows in the current callable scope — flushed into that scope's body so
		// they capture @this / enclosing locals via normal C# closure semantics (matches the canonical).
		private List<StatementSyntax> _pendingScopeFuncs = new();
		// Local KeysharpFunc/Closure var declarations for NAMED nested functions, prepended to the scope body so the name is
		// bound before any (possibly forward-referenced) call — AHK hoists nested functions.
		private List<StatementSyntax> _pendingScopeClosureInits = new();
		// Names of bare (non-static) nested functions declared anywhere in the CURRENT scope's body. They become local
		// closure variables (hoisted), so even a forward-referenced call resolves to the local — NOT a module field.
		private HashSet<string> _scopeClosureNames = new(System.StringComparer.Ordinal);
		// SL_ static-local field name -> its index in _fieldDecls, so a constant-valued `static x := 1+2` can rewrite the
		// field's initializer to the folded literal (`SL_… = 3L`) instead of a runtime InitStaticVariable.
		private readonly Dictionary<string, int> _staticFieldDeclIdx = new(System.StringComparer.Ordinal);
		private readonly HashSet<string> _emittedFuncImpls = new();   // guards against duplicate hoisted nested functions
		private readonly List<Type> _wildcardModules = new();   // `#import "Mod" { * }` types — members resolved on demand
		private readonly List<string> _classFieldIds = new();   // class slot fields — referenced at auto-exec start to force static init
		private readonly List<(string Attr, string Value)> _asmAttributes = new();   // #Assembly* directives -> [assembly: …]
		/// <summary>`#AssemblyName`, or null when the script does not set one. Read by CompilerHelper after Build.</summary>
		public string AssemblyName;
		// Package resolution is program-wide; identity tracking catches declarations below module scope.
		private readonly List<Keysharp.Internals.Os.PackageResolver.PackageRef> _packages = [];
		private readonly HashSet<Stmt> _packagesSeen = new(ReferenceEqualityComparer.Instance);
		private readonly HashSet<string> _requiredProviders = new(System.StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _requiredComponents = new(System.StringComparer.OrdinalIgnoreCase);
		public IReadOnlyCollection<string> RequiredProviders => _requiredProviders;
		public IReadOnlyCollection<string> RequiredComponents => _requiredComponents;

		// #ConsoleApp: build the executable as a console (CUI) application rather than a GUI one. Compile-time
		// only - it emits no code, and is surfaced to the host through the compilation result (see
		// CompilerHelper.Build), which stamps the PE subsystem when it writes the apphost.
		private bool _consoleApp;
		/// <summary>True when the script asked to be built as a console application via `#ConsoleApp`.</summary>
		public bool ConsoleApp => _consoleApp;

		// Inline C# is emitted as one additional tree per script module; identity tracking catches unsupported nesting.
		private List<(string Module, string ClassPath, CSharpDirective Dir)> _inlineBlocks;
		private HashSet<Stmt> _csharpSeen;
		/// <summary>The combined tooling view of the module-scoped inline C# compilation units, if any.</summary>
		public string InlineSource;
		public IReadOnlyList<InlineCSharpSource> InlineSources = [];
		private Dictionary<string, HashSet<string>> _moduleGlobals;
		private HashSet<string> _inlineFuncNames;

		// Compatibility mode (`#Requires AutoHotkey v2.0`/`v2.1`): lexically scoped per module/class/function. Controls
		// the default/implicit return value (v2.0 → "", v2.1 → unset) and the per-method [CompatibilityMode] attribute.
		private static readonly Semver.SemVersion[] CompatCandidates = { new(2, 0, 0), new(2, 1, 0) };
		private Semver.SemVersion _moduleCompat = Keysharp.Runtime.Script.DefaultCompatibilityVersion;   // current module's mode
		private Semver.SemVersion _currentCompat = Keysharp.Runtime.Script.DefaultCompatibilityVersion;  // current scope's mode
		private string _currentModuleClass = "__Main";   // the module class being lowered — used to qualify module-level
														 // field references from inside a (nested) class method (`Program.<Mod>.field`)
		// Hotkey/hotstring/remap support: extra __Main methods (the trigger callbacks) and the DHHR list
		// (Directives/Hotkeys/Hotstrings/Remaps registration calls) emitted into Program.AutoExecSection.
		private readonly List<MemberDeclarationSyntax> _hotMembers = new();
		private readonly List<StatementSyntax> _dhhr = new();
		private bool _persistent;            // a hotkey/hotstring makes the script persistent
		private string _singleInstanceMode;  // #SingleInstance mode (Force/Ignore/Prompt/Off), null = directive absent
		private bool _noTrayIcon;            // #NoTrayIcon present: applied at the top of Main, before any tray chrome is created
		private string _hookMutexName;       // #HookMutexName argument (passed to the Script constructor), null = absent
		// #Warn config: per-type output mode ("MsgBox"/"StdOut"/"OutputDebug") or null when that warning is off.
		// Matching AHK, VarUnset and Unreachable are ENABLED by default (MsgBox mode); LocalSameAsGlobal is off until a
		// `#Warn` directive turns it on. A `#Warn` directive overrides these. `_warnDefaultMode` is the program-wide
		// default mode applied by `#Warn On` / a bare `#Warn <Mode>` (default MsgBox, like AHK).
		private string _warnVarUnset = "MsgBox", _warnUnreachable = "MsgBox", _warnLocalSameAsGlobal = null;
		// NamedArg checks `f(Name: v)` against the callee's signature when the callee is a bare name. On by default
		// (like VarUnset/Unreachable): the check is high-signal and its only false-positive shape is a script that
		// reassigns a function name and calls the replacement by name.
		private string _warnNamedArg = "MsgBox";
		private string _warnDefaultMode = "MsgBox";
		private bool _warnScopeIsGlobal;   // current VarUnset-analysis scope is the module top-level (else a function)
		// Names provided at the module top-level. Keysharp resolves any bare name that isn't a local to a module-level
		// global field (see LowerName's EnsureGlobalField fallback), so a top-level-assigned name is readable from EVERY
		// nested scope — including class methods/properties, which do NOT inherit `outerProvided`. Threaded into every
		// scope's `readable` set so a read of such a global isn't a false "never assigned" positive (null when VarUnset off).
		private HashSet<string> _warnGlobalReadable;
		// `line` is relative to `file` — the #included file the warned-about node came from, or null for the main
		// script. The two MUST travel together: an included file has its own line numbering, so rendering its line
		// number against the main script's text quotes an unrelated line (and blames the wrong file).
		private readonly List<(string mode, int line, string desc, string file)> _warnings = new();
		private int _hotCount;
		private readonly HashSet<string> _exportedNames = new(System.StringComparer.OrdinalIgnoreCase);   // names marked `export`

		private static string ExportName(Stmt decl) => decl switch
		{
			FunctionDecl f => f.Name,
			ClassDecl c => c.Name,
			ExpressionStmt { Expr: AssignExpr { Target: NameExpr n } } => n.Name,
			_ => ""
		};

		private HashSet<string> _locals;
		// Locals of ENCLOSING function scopes — a nested closure (fat-arrow/local fn) that references one captures it
		// by reference (it's the same C# local the C# local function closes over), rather than making its own.
		private HashSet<string> _enclosingLocals = new();
		// Enclosing-scope statics (name -> emitted module-level SL_ field), so a nested closure's deref `%name%` resolves
		// an enclosing function's static the same way it resolves its own.
		private Dictionary<string, string> _enclosingStatics = new(System.StringComparer.Ordinal);
		// Lexical `#import` frames for class and function scopes (module scope is NOT a frame — it is the
		// EnsureGlobalField fallback). Innermost is last. A frame is pushed while lowering a class body / callable body
		// and popped after, so a nested closure — whose body lowers WHILE the enclosing frame is still on the stack —
		// sees the enclosing scope's imports automatically, with zero capture machinery. NameRef walks it innermost-
		// first; scoped bindings are inline expressions (no emitted declarations). See BuildImportScope.
		private readonly List<ImportScope> _importScopes = new();
		// Multi-module only: module name -> ModInfo, so a function/class-scoped `#import "ScriptMod" { … }` can resolve
		// the module's exports at body-lowering time (byName is otherwise a BuildMultiModule local). Null on the single-
		// module path (which only ever sees built-in modules).
		private Dictionary<string, ModInfo> _modulesByName;
		// Set by NameRef when it resolves a captured enclosing local or `this`; read (scoped) by LowerFatArrow to pick
		// Closure (capturing → `is Closure` true) vs Func (non-capturing) binding.
		private bool _capturedInScope;
		// True while lowering a `static` nested function: it can READ the enclosing scope's statics (module-level SL_
		// fields, shared across calls) but those references are not per-call state, so they must NOT mark the function
		// as a capturing closure — a static nested function stays a plain Func (AHK semantics).
		private bool _inStaticNested;
		private HashSet<string> _forcedGlobals;
		private Dictionary<string, string> _statics;
		private HashSet<string> _byRefParams;   // &name params: reads/writes route through the VarRef's __Value
		private int _tempCounter;
		private List<string> _scopeTemps = new();   // object temps the current scope needs (declared at its top)

		private string NewTemp() { var n = "KS_temp" + (++_tempCounter); _scopeTemps.Add(n); return n; }

		// Null-conditional access `target?.<access>`: evaluate target once into a temp; if it is unset (null),
		// short-circuit the whole access (and any nested args/calls) to C# null — which reads back as unset —
		// otherwise apply `access` to the temp. Nested `?.` compose because each wrap yields null on short-circuit.
		private ExpressionSyntax NullCondWrap(Expr target, System.Func<ExpressionSyntax, ExpressionSyntax> access)
		{
			var t = NewTemp();
			return Op("MultiStatement",
				Assign(Id(t), LowerExpr(target)),
				SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
					SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, Id(t), Null),
					Null,
					access(Id(t)))));
		}

		private static bool IsUnsetKeyword(Expr expr) =>
			expr is NameExpr name && name.Name.Equals("unset", System.StringComparison.OrdinalIgnoreCase);

		// The variable of an unset test written as a comparison against the `unset` keyword — `x = unset`, `x == unset`,
		// `x is unset`, their negations, and the same with the operands reversed. This is a Keysharp extension (AutoHotkey
		// rejects it) which means exactly what IsSet(x) means, so it gets IsSet's treatment in the VarUnset analysis:
		// testing a variable for unset is not a read of it, and counts as accounting for it. Null when expr is not one.
		private static NameExpr UnsetTestOperand(BinaryExpr binary)
		{
			// `is` is a verbal operator, so it carries whatever casing the source used.
			var isTypeTest = binary.Op.Equals("is", System.StringComparison.OrdinalIgnoreCase);

			if (!isTypeTest && binary.Op is not ("=" or "==" or "!=" or "!=="))
				return null;

			if (IsUnsetKeyword(binary.Right) && binary.Left is NameExpr left)
				return left;

			// `unset is x` asks about x's type, not whether it is set, so only the equality forms reverse.
			return !isTypeTest && IsUnsetKeyword(binary.Left) && binary.Right is NameExpr right ? right : null;
		}

		// Whether the maybe operator (or an optional chain) makes this expression able to yield unset, in which case
		// the operators applied to it short-circuit instead of raising. [v2.1-alpha.29+]
		// Short-circuiting is a property of `?` alone. A bare `unset` keyword operand is deliberately excluded:
		// Keysharp accepts `x = unset` as a way to test for unset (which AutoHotkey rejects), so the keyword has to
		// stay a comparable operand there rather than propagating unset out of the comparison.
		private static bool MayYieldUnset(Expr expr) => expr switch
		{
			null => false,
			UnaryExpr { Op: "?" } => true,
			UnaryExpr unary => MayYieldUnset(unary.Operand),
			BinaryExpr binary when binary.Op == "??" => MayYieldUnset(binary.Right),
			BinaryExpr binary => MayYieldUnset(binary.Left) || MayYieldUnset(binary.Right),
			AssignExpr assign => MayYieldUnset(assign.Value),
			TernaryExpr ternary => MayYieldUnset(ternary.Cond) || MayYieldUnset(ternary.Then) || MayYieldUnset(ternary.Else),
			MemberExpr member => member.NullConditional || MayYieldUnset(member.Target),
			DynMemberExpr member => member.NullConditional || MayYieldUnset(member.Target),
			IndexExpr index => index.NullConditional || MayYieldUnset(index.Target),
			CallExpr call => call.NullConditional || MayYieldUnset(call.Callee),
			GroupExpr group => MayYieldUnset(group.Inner),
			SequenceExpr sequence => sequence.Items.Count > 0 && MayYieldUnset(sequence.Items[^1]),
			_ => false
		};

		private ExpressionSyntax PropagateBinary(BinaryExpr binary, string method)
		{
			var left = NewTemp();
			var right = NewTemp();
			var operation = Op(method, Id(left), Id(right));
			var rightPart = Op("MultiStatement",
				Assign(Id(right), LowerExpr(binary.Right)),
				SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
					SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, Id(right), Null),
					Null, operation)));
			return Op("MultiStatement",
				Assign(Id(left), LowerExpr(binary.Left)),
				SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
					SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, Id(left), Null),
					Null, rightPart)));
		}

		private ExpressionSyntax PropagateUnary(Expr operand, System.Func<ExpressionSyntax, ExpressionSyntax> operation) =>
			NullCondWrap(operand, operation);

		// Case-insensitive so verbal operators carry any source casing (Is, AND, Or).
		private static readonly Dictionary<string, string> BinOps = new(System.StringComparer.OrdinalIgnoreCase)
		{
			["+"] = "Add", ["-"] = "Subtract", ["*"] = "Multiply", ["/"] = "Divide",
			["//"] = "FloorDivide", ["**"] = "Power", ["."] = "Concat",
			["&"] = "BitwiseAnd", ["|"] = "BitwiseOr", ["^"] = "BitwiseXor",
			["<<"] = "BitShiftLeft", [">>"] = "BitShiftRight", [">>>"] = "LogicalBitShiftRight",
			["<"] = "LessThan", ["<="] = "LessThanOrEqual", [">"] = "GreaterThan", [">="] = "GreaterThanOrEqual",
			["="] = "ValueEquality", ["=="] = "IdentityEquality", ["!="] = "ValueInequality", ["!=="] = "IdentityInequality",
			["~="] = "RegEx", ["!~="] = "NotRegEx",
			["&&"] = "BooleanAnd", ["||"] = "BooleanOr", ["and"] = "BooleanAnd", ["or"] = "BooleanOr",
			["is"] = "Is",   // `??` / `??=` are lowered to C#'s short-circuiting null-coalescing operator, not a helper call.
		};

		// The compiled script's full path ("*" for a from-string compile) and the caller-supplied startup name; used to
		// emit `MainScript.SetName(...)` so A_ScriptName/A_ScriptFullPath are correct.
		private string _scriptPath = "*";
		private string[] _sourceLines;   // raw MAIN-script lines, for embedding the offending line text in #Warn dialogs
		// The same, per #included file (full path -> lines, null when unreadable); filled on demand by IncludedSourceLines.
		private readonly Dictionary<string, string[]> _includedSourceLines = new(System.StringComparer.OrdinalIgnoreCase);
		private string _startupName;
		private string _includeDir;   // directory for resolving file-based `#import "name"` module imports
		// This compilation's caller-supplied preprocessor symbols (null when there are none), forwarded to the separate
		// parse that each imported module file gets so its #if branches resolve as they do in the main script.
		private IEnumerable<string> _defines;
		private bool _compileToFile;   // emitting a distributable .cks/.exe: relativize #include paths in A_LineFile

		public CompilationUnitSyntax Build(ProgramNode prog, string name, string scriptPath = "*", string startupName = null, string includeDir = null, string source = null, bool compileToFile = false, IEnumerable<string> defines = null)
		{
			_scriptPath = scriptPath ?? "*";
			_compileToFile = compileToFile;
			_defines = defines;
			// Include predefined, command-line and script symbols.
			_inlineDefines = prog.Defines is { Count: > 0 } ? prog.Defines : [.. defines ?? []];
			_staticFieldSink = _fieldDecls;   // module scope by default (the single-module path skips ClearPerModuleState)
			_sourceLines = source?.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
			_startupName = startupName;
			_includeDir = includeDir;
			// Package requirements are program-wide, so they are gathered before either lowering path runs. Scanning
			// prog.Body covers both: the multi-module path partitions this same list into modules, so every module's
			// top-level statements are already here.
			PrescanPackageDirectives(prog.Body);
			// Prescan after module partitioning so blocks keep their declaring module.
			// Use the dedicated multi-module path when the file defines/uses modules: a `#Module`, an `export`, or a
			// `#import "name"` that resolves to a separate <name>.ahk file. Otherwise the common single-module path is
			// completely unaffected (a builtin `#import "Ks"` stays here). The file-import scan is DEEP (any nesting):
			// a `#import "helper"` inside a function must still trigger module loading, even though its binding is
			// function-scoped — otherwise the file is never parsed (a silent-vaporize bug).
			bool IsFileImport(ImportDirective im) =>
				_includeDir != null && im.Module.Length > 0 && ResolveModuleFile(im.Module, _includeDir) != null;
			var allImports = new List<ImportDirective>();
			foreach (var s in prog.Body) CollectAllImports(s, allImports);
			if (prog.Body.Any(s => s is DirectiveStmt d && d.Name.Equals("Module", System.StringComparison.OrdinalIgnoreCase))
				|| prog.Body.Any(s => s is ExportStmt)
				|| allImports.Any(IsFileImport))
				return BuildMultiModule(prog, name);

			// `export <decl>` outside a `#Module` file is just a normal top-level declaration.
			var body = prog.Body.Select(s => s is ExportStmt e ? e.Decl : s).ToList();
			_moduleCompat = ScanRequires(body) ?? Keysharp.Runtime.Script.DefaultCompatibilityVersion;
			_currentCompat = _moduleCompat;
			// Prescan before lowering so C# function calls resolve normally.
			PrescanCSharpDirectives(body, "__Main", _includeDir);
			RegisterInlineFunctionsFor("__Main");
			var (members, auto) = LowerProgramBody(body, liftControlFlowImports: true);
			if (Diagnostics.Count > 0) return null;
			return BuildUnit(name, members, auto);
		}

		// ---- multi-module (`#Module` / `export` / cross-module `#import`) ----

		private enum ExportK { Function, Variable, Type }
		private sealed class ModInfo
		{
			public string Name;
			public string Dir;   // directory of the source file this module came from (for "importing file dir first")
			public List<Stmt> Body = new();
			// Imports bound at MODULE scope: top-level directives + control-flow-lifted ones (consumed by EmitImports).
			public List<ImportDirective> ModuleBindings = new();
			// EVERY import in the module regardless of nesting (top-level, control-flow, function/class/switch/hotkey
			// bodies) — drives file loading and execution order, which stay eager and scope-blind. Superset of the above.
			public List<ImportDirective> AllImports = new();
			public Dictionary<string, ExportK> Exports = new(System.StringComparer.OrdinalIgnoreCase);
			public HashSet<string> InlineMembers;
			public string DefaultName;          // user name of the explicit or same-name implicit default (or null)
			public bool HasExplicitExports;
		}

		// For each quoted `#import "name"` whose module isn't defined in this file (and isn't the built-in AHK module),
		// loads `<name>.ahk` from the include dir, parses it, and adds its content as a module named `name`.
		private void LoadFileModules(List<ModInfo> mods, Dictionary<string, ModInfo> byName)
		{
			if (_includeDir == null) return;
			var queue = new Queue<ModInfo>(mods);
			while (queue.Count > 0)
			{
				var m = queue.Dequeue();
				foreach (var im in m.AllImports)   // load files for imports at ANY nesting depth, not just module scope
				{
					var modName = im.Module;
					if (modName.Length == 0 || byName.ContainsKey(modName) || modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase)) continue;
					// Resolve via the AHK module search: the importing file's own directory first, then the search path.
					var file = ResolveModuleFile(modName, m.Dir ?? _includeDir);
					if (file == null) continue;
					var fileDir = System.IO.Path.GetDirectoryName(file);
					var fileName = System.IO.Path.GetFileName(file);
					ProgramNode fileProg;
					try
					{
						// The module file is parsed on its own, so this compilation's symbols have to be handed to it
						// explicitly — its #if branches must resolve the same way they do in the main script.
						// `file` (not null) stamps its tokens with their real path, so %A_LineFile% resolves to the
						// module file rather than its directory, and #Warn/diagnostics raised inside it name it.
						var (p, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(System.IO.File.ReadAllText(file), fileDir, file, _defines);
						// A parse error in the imported module file is surfaced as the script's error (prefixed with the
						// module file name), not swallowed — otherwise the user would only see a misleading "module not
						// found" for a file that does exist. BuildMultiModule aborts as soon as a diagnostic is recorded.
						// Parser diagnostics already carry the file name now that tokens are stamped (ErrorAt); lexer
						// diagnostics do not, so prefix only what is missing rather than doubling it up.
						if (diags.Count > 0)
						{
							foreach (var d in diags)
								Diag(d.StartsWith(fileName + ":", System.StringComparison.OrdinalIgnoreCase) ? d : $"{fileName}:{d}");
							continue;
						}
						fileProg = p;
					}
					catch (System.Exception ex) { Diag($"#Import: failed to read module '{modName}' from {fileName}: {ex.Message}"); continue; }
					// The file's own segments: its `__Main` (top-level) becomes the imported module `modName`; any inner
					// `#Module`s keep their own names. Each remembers its source dir so ITS imports resolve from there.
					var fileMods = PartitionModules(fileProg.Body);
					foreach (var fm in fileMods)
					{
						if (fm.Name == "__Main") fm.Name = modName;
						if (byName.ContainsKey(fm.Name)) continue;
						fm.Dir = fileDir;
						ScanExports(fm);
						mods.Add(fm); byName[fm.Name] = fm; queue.Enqueue(fm);
					}
				}
			}
		}

		// File names tried for a module reference, Keysharp-native first then AHK-compatible.
		private static readonly string[] moduleExts = { ".ks", ".ahk" };
		private static readonly string[] moduleInitNames = { "__Init.ks", "__Init.ahk" };
		private List<string> _moduleSearchPath;

		// The local name introduced by a bare unquoted import. A path import such as `#import Lib/OCR` binds its leaf
		// (`OCR`), while the full specifier remains the module identity used for file lookup and generated type mapping.
		private static string ImportBindingName(string modName)
		{
			var sep = System.Math.Max(modName.LastIndexOf('/'), modName.LastIndexOf('\\'));
			return sep >= 0 ? modName.Substring(sep + 1) : modName;
		}

		// The #import search path, mirroring AutoHotkey's InitModuleSearchPath: the AhkImportPath environment variable
		// (a ';'-delimited list) or, when unset, the default below. Built-in %vars% are expanded (environment variables
		// are NOT, matching AHK); relative items resolve against A_ScriptDir; nonexistent directories are dropped. It is
		// resolved once per build (so %A_LineFile% consistently refers to the main script).
		private List<string> ModuleSearchPath()
		{
			if (_moduleSearchPath != null) return _moduleSearchPath;
			_moduleSearchPath = new List<string>();
			var spec = System.Environment.GetEnvironmentVariable("AhkImportPath");
			if (string.IsNullOrEmpty(spec))
				spec = ".;%A_MyDocuments%\\Keysharp;%A_MyDocuments%\\AutoHotkey;%A_AhkPath%\\..";
			var baseDir = string.IsNullOrEmpty(_includeDir) ? System.IO.Directory.GetCurrentDirectory() : _includeDir;
			foreach (var raw in spec.Split(';'))
			{
				// The default spec is written with '\' (AHK's own), so without this the user-Documents and exe-adjacent
				// tiers expand to non-existent directories on Unix and are silently dropped from the search path.
				var item = Parser.NormalizeDirectiveSeparators(Parser.ExpandPathVars(raw.Trim(), _includeDir, _scriptPath).Trim());
				if (item.Length == 0) continue;
				string full;
				try { full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(item) ? item : System.IO.Path.Combine(baseDir, item)); }
				catch { continue; }
				if (System.IO.Directory.Exists(full) && !_moduleSearchPath.Contains(full, System.StringComparer.OrdinalIgnoreCase))
					_moduleSearchPath.Add(full);
			}
			return _moduleSearchPath;
		}

		// Resolves an `#import <name>` to a module file, mirroring AutoHotkey's FindModuleFileIndex: the importing file's
		// own directory is searched first, then each directory of the module search path in order. Within a directory the
		// candidates are tried as: <name> (an exact existing file), then <name>\__Init.ks/.ahk (when <name> is a
		// directory), then <name>.ks/.ahk. Returns the full path of the first match, or null.
		private string ResolveModuleFile(string modName, string localDir)
		{
			if (string.IsNullOrEmpty(modName)) return null;

			// A module name may carry a subdirectory (`#import "Sub\Mod"`); both separators work on every platform.
			modName = Parser.NormalizeDirectiveSeparators(modName);

			// The directory walk itself lives in SearchModulePath, shared with the `#CSharp "file.cs"` form; only
			// these candidates are specific to a module NAME.
			return SearchModulePath(localDir, dir =>
			{
				var exact = System.IO.Path.Combine(dir, modName);
				var cands = new List<string> { exact };                                   // <name> exact file

				if (System.IO.Directory.Exists(exact))                                    // <name>\__Init.ks/.ahk
					cands.AddRange(moduleInitNames.Select(init => System.IO.Path.Combine(exact, init)));

				cands.AddRange(moduleExts.Select(ext => exact + ext));                    // <name>.ks/.ahk
				return cands;
			});
		}

		// Splits the program into module segments by `#Module Name` directives (segment 0 is `__Main`), collecting each
		// segment's `#import` directives into two lists: ModuleBindings (top-level + control-flow-lifted, bound at module
		// scope) and AllImports (every nesting depth, for file loading and execution order — see the ModInfo fields).
		private static List<ModInfo> PartitionModules(List<Stmt> body)
		{
			var mods = new List<ModInfo>();
			var cur = new ModInfo { Name = "__Main" };
			mods.Add(cur);
			foreach (var s in body)
			{
				if (s is DirectiveStmt d && d.Name.Equals("Module", System.StringComparison.OrdinalIgnoreCase))
				{
					cur = new ModInfo { Name = (d.Args ?? "").Trim() };
					mods.Add(cur);
				}
				else if (s is ImportDirective im)
				{
					cur.ModuleBindings.Add(im);
					cur.AllImports.Add(im);
				}
				else
				{
					cur.Body.Add(s);
					CollectNestedImports(s, cur.ModuleBindings);   // control-flow-nested → module-scope binding
					CollectAllImports(s, cur.AllImports);          // any nesting → loading / execution order
				}
			}
			return mods;
		}

		// AHK processes `#import` directives at load time regardless of nesting, so an import inside a block / if / loop
		// (e.g. `if true { #import "M" { X as Y } }`) still binds at module scope. Recursively lift those out. This
		// covers only CONTROL FLOW — an import inside a function/class body is lexically scoped (see CollectAllImports).
		private static void CollectNestedImports(Stmt s, List<ImportDirective> into)
		{
			switch (s)
			{
				case ImportDirective d: into.Add(d); break;
				case Block b: foreach (var c in b.Body) CollectNestedImports(c, into); break;
				case IfStmt i: CollectNestedImports(i.Then, into); if (i.Else != null) CollectNestedImports(i.Else, into); break;
				case WhileStmt w: CollectNestedImports(w.Body, into); if (w.Else != null) CollectNestedImports(w.Else, into); break;
				case LoopStmt l: CollectNestedImports(l.Body, into); if (l.Else != null) CollectNestedImports(l.Else, into); break;
				case SpecialLoopStmt sl: CollectNestedImports(sl.Body, into); if (sl.Else != null) CollectNestedImports(sl.Else, into); break;
				case ForStmt f: CollectNestedImports(f.Body, into); if (f.Else != null) CollectNestedImports(f.Else, into); break;
				case SwitchStmt sw:
					foreach (var c in sw.Cases) foreach (var cs in c.Body) CollectNestedImports(cs, into);
					if (sw.Default != null) foreach (var ds in sw.Default) CollectNestedImports(ds, into);
					break;
				case TryStmt t:
					CollectNestedImports(t.Body, into);
					foreach (var cb in t.Catches) CollectNestedImports(cb.Body, into);
					if (t.Else != null) CollectNestedImports(t.Else, into);
					if (t.Finally != null) CollectNestedImports(t.Finally, into);
					break;
			}
		}

		// Every `#import` reachable from a statement regardless of nesting depth — control flow AND function/class/switch/
		// hotkey-hotstring bodies. Feeds file loading and execution order, which are load-time and position-independent
		// even though the BINDING of a function/class-nested import is lexically scoped (Phase 3). Superset of
		// CollectNestedImports; the two are kept separate so a function-scoped import never binds at module scope.
		private static void CollectAllImports(Stmt s, List<ImportDirective> into)
		{
			switch (s)
			{
				case ImportDirective d: into.Add(d); break;
				case Block b: foreach (var c in b.Body) CollectAllImports(c, into); break;
				case IfStmt i: CollectAllImports(i.Then, into); if (i.Else != null) CollectAllImports(i.Else, into); break;
				case WhileStmt w: CollectAllImports(w.Body, into); if (w.Else != null) CollectAllImports(w.Else, into); break;
				case LoopStmt l: CollectAllImports(l.Body, into); if (l.Else != null) CollectAllImports(l.Else, into); break;
				case SpecialLoopStmt sl: CollectAllImports(sl.Body, into); if (sl.Else != null) CollectAllImports(sl.Else, into); break;
				case ForStmt f: CollectAllImports(f.Body, into); if (f.Else != null) CollectAllImports(f.Else, into); break;
				case SwitchStmt sw:
					foreach (var c in sw.Cases) foreach (var cs in c.Body) CollectAllImports(cs, into);
					if (sw.Default != null) foreach (var ds in sw.Default) CollectAllImports(ds, into);
					break;
				case TryStmt t:
					CollectAllImports(t.Body, into);
					foreach (var cb in t.Catches) CollectAllImports(cb.Body, into);
					if (t.Else != null) CollectAllImports(t.Else, into);
					if (t.Finally != null) CollectAllImports(t.Finally, into);
					break;
				case FunctionDecl fn: if (fn.Body != null) CollectAllImports(fn.Body, into); break;
				case ExportStmt ex: CollectAllImports(ex.Decl, into); break;
				case HotkeyDef hk: if (hk.Body != null) CollectAllImports(hk.Body, into); if (hk.Func?.Body != null) CollectAllImports(hk.Func.Body, into); break;
				case HotstringDef hs: if (hs.Body != null) CollectAllImports(hs.Body, into); if (hs.Func?.Body != null) CollectAllImports(hs.Func.Body, into); break;
				case ClassDecl cd: CollectClassImports(cd, into); break;
			}
		}

		// Every `#import` declared anywhere in a class: its class-body imports (ClassDecl.Imports), method bodies,
		// property get/set bodies, member initializers, and nested classes. Load/order only, like CollectAllImports.
		private static void CollectClassImports(ClassDecl cd, List<ImportDirective> into)
		{
			into.AddRange(cd.Imports);
			foreach (var mth in cd.Methods) if (mth.Body != null) CollectAllImports(mth.Body, into);
			foreach (var pr in cd.Properties) { if (pr.GetBody != null) CollectAllImports(pr.GetBody, into); if (pr.SetBody != null) CollectAllImports(pr.SetBody, into); }
			foreach (var init in cd.StaticInit) CollectAllImports(init, into);
			foreach (var init in cd.InstanceInit) CollectAllImports(init, into);
			foreach (var nc in cd.Nested) CollectClassImports(nc, into);
		}

		// Collects a module's exports. Explicit `export` declarations win; a module with none implicitly exports all of
		// its top-level functions/classes/variables. In that implicit form, a function or class matching the module's
		// leaf name is also its default export (`OCR.ks` containing `class OCR`), regardless of additional helpers.
		private static void ScanExports(ModInfo m)
		{
			foreach (var s in m.Body)
			{
				if (s is ExportStmt e)
				{
					m.HasExplicitExports = true;
					var nm = ExportName(e.Decl);
					var kind = e.Decl is FunctionDecl ? ExportK.Function : e.Decl is ClassDecl ? ExportK.Type : ExportK.Variable;
					if (nm.Length > 0) m.Exports[nm] = kind;
					if (e.Default && nm.Length > 0) m.DefaultName = nm;
				}
			}
			if (m.HasExplicitExports) return;
			foreach (var s in m.Body)   // no explicit exports -> everything top-level is exportable
			{
				switch (s)
				{
					case FunctionDecl f: m.Exports[f.Name] = ExportK.Function; break;
					case ClassDecl c: m.Exports[c.Name] = ExportK.Type; break;
					case ExpressionStmt { Expr: AssignExpr { Target: NameExpr n } }: m.Exports[n.Name] = ExportK.Variable; break;
				}
			}
			var leaf = ImportBindingName(m.Name);
			foreach (var ex in m.Exports)
				if (ex.Value != ExportK.Variable && ex.Key.Equals(leaf, System.StringComparison.OrdinalIgnoreCase))
				{
					m.DefaultName = ex.Key;
					break;
				}
		}

		// Execution order: a module runs after the modules it imports (topological); ties/cycles fall back to declaration
		// order. Importing `__Main` is what positions `__Main` (so a module that imports __Main runs after it).
		private static List<string> ComputeExecOrder(List<ModInfo> mods, Dictionary<string, ModInfo> byName)
		{
			var order = new List<string>();
			var state = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);   // 0=unseen,1=visiting,2=done
			void Visit(ModInfo m)
			{
				if (state.ContainsKey(m.Name)) return;   // done or already on stack (cycle) -> skip
				state[m.Name] = 1;
				foreach (var dep in ImportTargets(m))
					if (byName.TryGetValue(dep, out var dm)) Visit(dm);
				state[m.Name] = 2;
				order.Add(m.Name);
			}
			// Drive in REVERSE declaration order (matches the canonical GetModuleExecutionOrder): combined with the
			// cycle skip, a module that imports __Main runs after __Main even though __Main also imports it.
			for (int i = mods.Count - 1; i >= 0; i--) Visit(mods[i]);
			return order;
		}

		private static IEnumerable<string> ImportTargets(ModInfo m)
		{
			foreach (var im in m.AllImports)   // an import at any nesting depth still contributes an execution-order edge
				if (im.Module.Length > 0) yield return im.Module;
		}

		private void ClearPerModuleState()
		{
			_fields.Clear(); _fieldDecls.Clear(); _staticFieldDeclIdx.Clear(); _userFuncByLower.Clear(); _userFuncDeclByLower.Clear(); _userClassByLower.Clear(); _userClassDeclByLower.Clear();
			_staticFieldSink = _fieldDecls;   // module scope until a class redirects it

			_inlineAliases.Clear(); _wildcardModules.Clear(); _classFieldIds.Clear(); _emittedFuncImpls.Clear();
			_exportedNames.Clear(); _pendingLambdas.Clear(); _inlineFuncNames?.Clear(); _scopeTemps.Clear(); _tempCounter = 0;
			_importScopes.Clear();   // class/function import frames never straddle a module boundary
		}

		private CompilationUnitSyntax BuildMultiModule(ProgramNode prog, string name)
		{
			var mods = PartitionModules(prog.Body);
			foreach (var m in mods) { m.Dir = _includeDir; ScanExports(m); }   // main-file modules resolve imports from A_ScriptDir
			var byName = mods.ToDictionary(m => m.Name, m => m, System.StringComparer.OrdinalIgnoreCase);
			_modulesByName = byName;   // lets a function/class-scoped `#import "ScriptMod"` resolve exports at body-lowering time
			LoadFileModules(mods, byName);   // pull in `#import "name"` modules defined in a separate <name>.ahk
			if (Diagnostics.Count > 0) return null;   // an imported module file failed to parse — surface that, not a later "module not found"

			// An imported module file's statements never reach prog.Body, so its top-level `#Package` directives are
			// only visible now. Without this a library declaring its own package is rejected as if it were nested.
			// __Main's body is rescanned here; the prescan is idempotent per directive node, so that is a no-op.
			foreach (var m in mods)
			{
				PrescanPackageDirectives(m.Body);
				// Attribute blocks to their declaring module.
				PrescanCSharpDirectives(m.Body, m.Name, m.Dir);
				// Imports need inline members and exports before binding.
				RegisterInlineModuleMembers(m);
			}

			var execOrder = ComputeExecOrder(mods, byName);

			// A top-level `#Requires` (in __Main, before any #Module) sets the script-wide default that other modules
			// inherit; each module may override it with its own `#Requires`.
			var globalCompat = ScanRequires(mods[0].Body) ?? Keysharp.Runtime.Script.DefaultCompatibilityVersion;

			var moduleClasses = new List<MemberDeclarationSyntax>();
			foreach (var m in mods)
			{
				ClearPerModuleState();
				_currentModuleClass = NameMangler.ModuleClass(m.Name);
				_moduleCompat = ScanRequires(m.Body) ?? globalCompat;
				_currentCompat = _moduleCompat;
				RegisterInlineFunctionsFor(m.Name);
				var importMembers = EmitImports(m, byName);
				var body = m.Body.Select(s => s is ExportStmt e ? e.Decl : s).ToList();
				var (members, auto) = LowerProgramBody(body);
				if (Diagnostics.Count > 0) return null;
				MarkExports(m);
				moduleClasses.Add(BuildModuleClass(m.Name, _fieldDecls, members, _pendingLambdas,
					m.Name == "__Main" ? _hotMembers : null, auto, importMembers, _moduleCompat));
			}
			return AssembleProgram(name, moduleClasses, execOrder);
		}

		// References an exported member field of another module: `Program.<Mod>.<escapedName>`.
		private static ExpressionSyntax ModuleMemberField(string modName, string memberName) =>
			Member(Member(Id("Program"), NameMangler.ModuleClass(modName)), NameMangler.Global(memberName));

		// The fallback value of a whole-module import with no default export: a fresh MODULE INSTANCE.
		// Script modules → `new Program.<Mod>()`; the built-in catch-all AHK module → the Ahk meta-object;
		// a built-in Module type (Ks, …) → `new <Type>()`. All derive from Module and implement IMetaObject, so member
		// access and method calls (`Ks.Cosh(0)`) dispatch through Get/Call — a Statics *class* singleton would not.
		// Returns null if the name is not a known module. Shared by the single- and multi-module binding paths.
		private static ExpressionSyntax ModuleObjectExpr(string modName, bool isAhk, bool isScript)
		{
			if (isScript)
				return SyntaxFactory.ObjectCreationExpression(Ty("Program." + NameMangler.ModuleClass(modName))).WithArgumentList(SyntaxFactory.ArgumentList());
			if (isAhk)
				return SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Runtime.Ahk")).WithArgumentList(SyntaxFactory.ArgumentList());
			return Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var t)
				&& typeof(Keysharp.Runtime.Module).IsAssignableFrom(t)
				? SyntaxFactory.ObjectCreationExpression(Ty(t.FullName.Replace('+', '.'))).WithArgumentList(SyntaxFactory.ArgumentList()) : null;
		}

		// The inline expression a bare global name resolves to when it names a built-in function/property/AHK
		// class (NOT a user declaration), else null. Factored out of EnsureGlobalField so a `#import AHK { X }`
		// member — the AHK module being the catch-all over every global built-in — binds the same way.
		private ExpressionSyntax ResolveGlobalBuiltin(string lower)
		{
			var rd = Script.TheScript.ReflectionsData;
			if (rd.flatPublicStaticMethods.TryGetValue(lower, out var mi))
			{
				TrackRuntimeComponent(mi);
				return FuncBind($"{mi.DeclaringType.FullName.Replace('+', '.')}.{mi.Name}");
			}
			if (rd.flatPublicStaticProperties.TryGetValue(lower, out var prop))
				return Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name);
			if (rd.stringToTypes.TryGetValue(lower, out var type) && IsGlobalAhkClass(type))
				return TypeSingleton(type.FullName.Replace('+', '.'));
			return null;
		}

		// Resolves an explicit member of a BUILT-IN module to its inline binding expression: a specific Module
		// type (Ks) via BindModuleMember (method→Func, nested type→singleton, property→access); the catch-all
		// AHK module via the global built-in tables. Returns null when the module genuinely has no such member.
		private ExpressionSyntax BindBuiltinMember(string modName, bool isAhk, string member) =>
			isAhk ? ResolveGlobalBuiltin(member.ToLowerInvariant())
				: Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var t) ? BindModuleMember(t, member) : null;

		// Emits the import binding members for a module (and registers the bound names in `_fields`). Returns property
		// members (imported variables) to add to the class body; field bindings go straight into `_fieldDecls`.
		// Names declared locally in a module (functions/classes/hotkey-or-hotstring funcs). A local declaration takes
		// precedence over any imported name (even a later import), so these are excluded from import binding.
		private static HashSet<string> CollectLocalDeclNames(List<Stmt> body)
		{
			var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var s in body)
			{
				var st = s is ExportStmt e ? e.Decl : s;
				switch (st)
				{
					case FunctionDecl fd: set.Add(fd.Name); break;
					case ClassDecl cd: set.Add(cd.Name); break;
					case HotkeyDef { Func: { } hkf }: set.Add(hkf.Name); break;
					case HotstringDef { Func: { } hsf }: set.Add(hsf.Name); break;
				}
			}
			return set;
		}

		private List<MemberDeclarationSyntax> EmitImports(ModInfo m, Dictionary<string, ModInfo> byName)
		{
			var props = new List<MemberDeclarationSyntax>();
			var localNames = CollectLocalDeclNames(m.Body);
			// Local inline exports shadow imports like script declarations do.
			if (_inlineFuncNames != null) localNames.UnionWith(_inlineFuncNames);
			// Wildcard `{ * }` exports are deferred and resolved with LAST-import-wins (a later `#import "B" { * }`
			// overrides an earlier `#import "A" { * }` for a shared name); explicit imports and locals take priority.
			var wild = new Dictionary<string, (string mod, string key, ExportK kind)>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var im in m.ModuleBindings)   // module-scope bindings only; function/class-nested imports are scoped elsewhere
			{
				var (modName, alias, named, quoted) = (im.Module, im.Alias, im.Named, im.Quoted);
				if (modName.Length == 0) continue;
				if (!ModuleResolves(modName, byName))
				{
					Diag($"{im.Line}:{im.Column}: #Import: module not found: {modName}");
					continue;
				}
				bool isAhk = modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase);
				bool isScript = byName.ContainsKey(modName);

				// `as Alias`: bind the module's default export if it has one, else the module object. Use the same named
				// binding path as `{ Name as Alias }` so type defaults stay lazy and variable defaults write through.
				if (alias != null && !localNames.Contains(alias))
				{
					var script = isScript ? byName[modName] : null;
					if (script?.DefaultName != null && script.Exports.TryGetValue(script.DefaultName, out var defaultKind))
						BindNamedImport(modName, script.DefaultName, alias, defaultKind, props);
					else if (ModuleObjectExpr(modName, isAhk, isScript) is { } val)
						RegisterImportField(alias, val);
				}
				// Named imports `{ a, b as c, * }`.
				if (named != null)
				{
					foreach (var raw in named.Split(','))
					{
						var part = raw.Trim();
						if (part.Length == 0) continue;
						if (part == "*")   // wildcard: record every export (resolved last-wins after the loop)
						{
							if (isScript)
								foreach (var ex in byName[modName].Exports)
									if (!localNames.Contains(ex.Key)) wild[ex.Key] = (modName, ex.Key, ex.Value);
							// A built-in `{ * }` resolves members on demand (matching the single-module RegisterImport
							// path) instead of eagerly emitting every export — the module type is consulted in NameRef.
							else if (!isAhk && Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var wt) && !_wildcardModules.Contains(wt))
								_wildcardModules.Add(wt);
							continue;
						}
						string impName = part, impAlias = part;
						var asIdx = part.IndexOf(" as ", System.StringComparison.OrdinalIgnoreCase);
						if (asIdx >= 0) { impName = part.Substring(0, asIdx).Trim(); impAlias = part.Substring(asIdx + 4).Trim(); }
						if (localNames.Contains(impAlias)) continue;   // a local declaration overrides this import
						wild.Remove(impAlias);                         // an explicit import overrides a wildcard one
						if (isScript && byName[modName].Exports.TryGetValue(impName, out var k))
							BindNamedImport(modName, impName, impAlias, k, props);
						else if (isScript)
							RegisterImportField(impAlias, ModuleMemberField(modName, impName));   // best-effort (script modules bind non-exported declarations too)
						else if (BindBuiltinMember(modName, isAhk, impName) is { } bind)
							BindBuiltinImport(impAlias, bind, ResolvedBuiltinProperty(modName, isAhk, impName), props);   // built-in method/type/property
						else if (!isAhk && Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var bmt) && BuiltinModuleHasMember(bmt, impName))
							{ }                                                                    // has the member but not bindable here — leave to ambient resolution
						else
							Diag($"{im.Line}:{im.Column}: #Import: module '{modName}' has no exported member named '{impName}'");
					}
				}
				// A bare `#import Mod` (no alias/braces) binds its default export under the module leaf name when one
				// exists, otherwise the module object. A quoted `#import "Mod"` introduces no name unless aliased.
				var bareName = ImportBindingName(modName);
				if (alias == null && named == null && !quoted && !localNames.Contains(bareName))
				{
					var script = isScript ? byName[modName] : null;
					if (script?.DefaultName != null && script.Exports.TryGetValue(script.DefaultName, out var defaultKind))
						BindNamedImport(modName, script.DefaultName, bareName, defaultKind, props);
					else if (ModuleObjectExpr(modName, isAhk, isScript) is { } bareObj)
						RegisterImportField(bareName, bareObj);
				}
			}
			// Emit the surviving wildcard bindings (those not shadowed by an explicit import or local declaration).
			foreach (var (lower, w) in wild)
				if (!_fields.ContainsKey(lower.ToLowerInvariant()))
					BindNamedImport(w.mod, w.key, w.key, w.kind, props);
			return props;
		}

		// A method export binds as a cached field (`alias = Mod.name`); a type export binds as a getter-only property
		// (preserving lazy class initialization); and a variable export binds as a get/set property so writes propagate.
		private void BindNamedImport(string modName, string name, string alias, ExportK kind, List<MemberDeclarationSyntax> props)
		{
			if (kind == ExportK.Function)
				RegisterImportField(alias, ModuleMemberField(modName, name));
			else
			{
				var lower = alias.ToLowerInvariant();
				if (_fields.ContainsKey(lower)) return;
				_fields[lower] = NameMangler.Escape(lower);
				var src = ModuleMemberField(modName, name);
				if (kind == ExportK.Type)
					props.Add(ObjArrowProp(NameMangler.Escape(lower), src));
				else
					props.Add(SyntaxFactory.PropertyDeclaration(ObjType, NameMangler.Escape(lower))
						.AddModifiers(PublicTok, StaticTok)
						.AddAccessorListAccessors(
							SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(src)).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
							SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(Assign(src, Id("value")))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))));
			}
		}

		private void RegisterImportField(string name, ExpressionSyntax value)
		{
			var lower = name.ToLowerInvariant();
			if (_fields.ContainsKey(lower)) return;
			_fields[lower] = NameMangler.Escape(lower);
			_fieldDecls.Add(ObjField(NameMangler.Escape(lower), value));
		}

		// A built-in METHOD or nested TYPE is an immutable reference and binds as a cached field. A built-in PROPERTY
		// must bind as a property instead: values such as A_Thread, A_NowMs, A_LastError or A_TickCount are recomputed
		// on every read — several of them per pseudo-thread — so caching the first value in a field at module-init
		// time froze the imported name for the rest of the run. This mirrors what BindNamedImport already does for a
		// script module's variable exports. The get/set form is used only for an `object`-typed property, which every
		// writable one currently is; a future typed setter degrades to read-only here rather than emitting C# that
		// cannot compile.
		private void BindBuiltinImport(string alias, ExpressionSyntax read, System.Reflection.PropertyInfo prop, List<MemberDeclarationSyntax> props)
		{
			if (prop == null)
			{
				RegisterImportField(alias, read);
				return;
			}

			var lower = alias.ToLowerInvariant();
			if (_fields.ContainsKey(lower)) return;
			_fields[lower] = NameMangler.Escape(lower);
			var name = NameMangler.Escape(lower);
			var access = Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name);

			if (prop.SetMethod?.IsPublic != true || prop.PropertyType != typeof(object))
			{
				props.Add(ObjArrowProp(name, access));
				return;
			}

			props.Add(SyntaxFactory.PropertyDeclaration(ObjType, name)
				.AddModifiers(PublicTok, StaticTok)
				.AddAccessorListAccessors(
					SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(access)).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
					SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithExpressionBody(SyntaxFactory.ArrowExpressionClause(Assign(access, Id("value")))).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))));
		}

		// The PropertyInfo an explicitly imported built-in member resolves to, or null when it resolves to a method or
		// a nested type instead (or does not resolve at all). Mirrors the resolution ORDER of ResolveGlobalBuiltin and
		// BindModuleMember exactly, so a name those bind as a method is never mistaken for a property here.
		private static System.Reflection.PropertyInfo ResolvedBuiltinProperty(string modName, bool isAhk, string member)
		{
			var rd = Script.TheScript.ReflectionsData;

			if (isAhk)
			{
				var lower = member.ToLowerInvariant();
				return rd.flatPublicStaticMethods.ContainsKey(lower) ? null
					   : rd.flatPublicStaticProperties.TryGetValue(lower, out var globalProp) ? globalProp : null;
			}

			if (!rd.stringToTypes.TryGetValue(modName, out var modType))
				return null;

			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
			bool Matches(System.Reflection.MemberInfo m) =>
				(Script.GetUserDeclaredName(m) ?? m.Name).Equals(member, System.StringComparison.OrdinalIgnoreCase)
				|| m.Name.Equals(member, System.StringComparison.OrdinalIgnoreCase);

			if (modType.GetMethods(flags).Any(mi => !mi.IsSpecialName && Matches(mi)))
				return null;

			if (modType.GetNestedTypes(System.Reflection.BindingFlags.Public).Any(Matches))
				return null;

			return modType.GetProperties(flags).FirstOrDefault(Matches);
		}

		// Adds `[Keysharp.Runtime.Export]` to the backing fields of a module's exported names.
		private void MarkExports(ModInfo m)
		{
			for (int i = 0; i < _fieldDecls.Count; i++)
			{
				if (_fieldDecls[i] is not FieldDeclarationSyntax fd) continue;
				var varName = fd.Declaration.Variables[0].Identifier.Text.TrimStart('@');
				if (m.Exports.ContainsKey(varName))
					_fieldDecls[i] = fd.AddAttributeLists(Attr("Keysharp.Runtime.Export"));
			}
		}

		// Pre-pass (register functions/classes/imports) + the top-level lowering loop for one module body, returning
		// the module-class members and its auto-execute statements. Shared by the single- and multi-module paths.
		// liftControlFlowImports: single-module only — an import nested in a top-level control-flow block (if/loop/…)
		// binds at module scope like a bare top-level one (AHK load-time semantics), matching the multi-module lift.
		// The multi-module path leaves this off: EmitImports already bound its ModuleBindings before this runs.
		private (List<MemberDeclarationSyntax> members, List<StatementSyntax> auto) LowerProgramBody(List<Stmt> body, bool liftControlFlowImports = false)
		{
			foreach (var s in body)
			{
				// Only TOP-LEVEL functions are module-global. Nested functions (static or not) are scoped to their
				// enclosing function — bound as local KeysharpFunc/Closure vars when that scope is lowered — so same-named
				// nested functions in different scopes don't collide.
				if (s is FunctionDecl fd)
				{
					// Inline exports were registered first, so report this collision from the script side.
					if (_inlineFuncNames?.Contains(fd.Name) == true)
						Diag($"'{fd.Name}' is declared both as a script function and as a public method in a #CSharp block; rename one of them");

					_userFuncByLower[fd.Name.ToLowerInvariant()] = fd.Name; _userFuncDeclByLower[fd.Name.ToLowerInvariant()] = fd;
				}
				else if (s is HotkeyDef { Func: { } hkf }) { _userFuncByLower[hkf.Name.ToLowerInvariant()] = hkf.Name; }
				else if (s is HotstringDef { Func: { } hsf }) { _userFuncByLower[hsf.Name.ToLowerInvariant()] = hsf.Name; }
				else if (s is ClassDecl cd) { RegisterClass(cd); }
				else if (s is ImportDirective dir) RegisterImport(dir);
			}
			if (liftControlFlowImports)
			{
				// Imports inside a top-level control-flow block bind at module scope too (function/class bodies are
				// deliberately excluded — those are lexically scoped and handled when their body is lowered).
				var lifted = new List<ImportDirective>();
				foreach (var s in body) if (s is not ImportDirective) CollectNestedImports(s, lifted);
				foreach (var dir in lifted) RegisterImport(dir);
			}
			foreach (var lower in _userFuncByLower.Keys.ToList())
				EnsureGlobalField(lower);

			// #Warn: apply directive config (location-independent) then run the warning analysis over the whole module,
			// now that user funcs/classes are registered. Collected warnings are emitted at load time (see BuildOuterAuto).
			PrescanWarnDirectives(body);
			AnalyzeWarnings(body);

			var members = new List<MemberDeclarationSyntax>();
			var auto = new List<StatementSyntax>();
			// The top-level (auto-exec) scope's fat-arrow local functions/closures must start empty per module, or one
			// module's top-level lambdas leak into the next module's AutoExecSection (referencing fields it lacks).
			_pendingScopeFuncs = new();
			_pendingScopeClosureInits = new();
			_labelScopes.Add(CollectDirectLabels(body));   // top-level labels (goto targets from auto-exec loops)
			for (int i = 0; i < body.Count; i++)
			{
				var s = body[i];
				if (s is FunctionDecl fd) { _emittedFuncImpls.Add(NameMangler.FunctionMethod(fd.Name)); members.Add(LowerFunction(fd)); }
				else if (s is ClassDecl cd)
				{
					members.Add(LowerClass(cd));
					// AHK initializes a class when execution reaches its declaration — model that by referencing the
					// class slot at that point in the auto-exec, which triggers the lazy Statics[typeof(Class)] init.
					auto.Add(ExprStmt(Op("MultiStatement", Id(_fields[cd.Name.ToLowerInvariant()]))));
				}
				else if (s is HotkeyDef hk) LowerHotkey(hk);
				else if (s is HotstringDef hs) LowerHotstring(hs);
				else if (s is RemapDef rm) LowerRemap(rm);
				else if (s is DirectiveStmt hd && hd.Name.Equals("Hotstring", System.StringComparison.OrdinalIgnoreCase)) LowerHotstringDirective(hd);
				// `#HotIf <expr>` sets the context for the hotkeys/hotstrings that follow it; a bare `#HotIf` clears it.
				// Emitted into the DHHR in SOURCE ORDER so it brackets the AddHotkey calls (matches the canonical).
				else if (s is DirectiveStmt hi && hi.Name.Equals("HotIf", System.StringComparison.OrdinalIgnoreCase)) LowerHotIf(hi);
				else
				{
					// A `name:` label immediately before a top-level loop becomes that loop's break/continue target.
					if (s is LabelStmt ll && i + 1 < body.Count && IsLoopStmt(body[i + 1])) _pendingLoopLabel = ll.Name;
					var st = LowerStmt(s); if (st != null) auto.Add(st);
				}
			}
			_labelScopes.RemoveAt(_labelScopes.Count - 1);
			// Declare temps introduced by postfix ++/-- in the top-level (auto-execute) scope.
			for (int i = _scopeTemps.Count - 1; i >= 0; i--) auto.Insert(0, DeclLocal(ObjType, _scopeTemps[i], Null));
			auto.AddRange(_pendingScopeFuncs);   // top-level fat-arrow local functions (auto-exec scope)
			return (members, auto);
		}

		// ---- symbol resolution ----

		// Collects the names of nested functions (static or not) declared in a scope body (recursing into control-flow
		// blocks but NOT into nested function bodies, which are separate scopes). Used so a forward-referenced call to a
		// nested function resolves to its local variable rather than (mis)resolving to a module field.
		private static void CollectBareNestedFuncNames(Stmt s, HashSet<string> into)
		{
			switch (s)
			{
				case FunctionDecl fd: into.Add(fd.Name.ToLowerInvariant()); break;
				case Block bl: foreach (var x in bl.Body) CollectBareNestedFuncNames(x, into); break;
				case IfStmt iff: CollectBareNestedFuncNames(iff.Then, into); if (iff.Else != null) CollectBareNestedFuncNames(iff.Else, into); break;
				case WhileStmt w: CollectBareNestedFuncNames(w.Body, into); break;
				case LoopStmt lp: CollectBareNestedFuncNames(lp.Body, into); break;
				case SpecialLoopStmt slp: CollectBareNestedFuncNames(slp.Body, into); break;
				case ForStmt fr: CollectBareNestedFuncNames(fr.Body, into); break;
				case TryStmt tr:
					CollectBareNestedFuncNames(tr.Body, into);
					foreach (var cb in tr.Catches) CollectBareNestedFuncNames(cb.Body, into);
					if (tr.Else != null) CollectBareNestedFuncNames(tr.Else, into);
					if (tr.Finally != null) CollectBareNestedFuncNames(tr.Finally, into);
					break;
				case SwitchStmt sw:
					foreach (var c in sw.Cases) foreach (var x in c.Body) CollectBareNestedFuncNames(x, into);
					if (sw.Default != null) foreach (var x in sw.Default) CollectBareNestedFuncNames(x, into);
					break;
			}
		}

		// Minimal `#import "Module" { name1, name2 }`: binds each named export of an external/builtin module
		// (a Keysharp.Runtime.Module subtype, e.g. "Ks") to a global slot — methods as Func, types as the
		// type singleton, properties as a direct member access. Script modules / wildcard / aliases are deferred.
		// Maps a `#Requires` argument (e.g. "AutoHotkey v2.1-alpha") to its compatibility line (2.0.0 or 2.1.0), or null
		// if the requirement isn't an AutoHotkey/Keysharp version requirement. Mirrors GetCompatibilityModeFromRequirement.
		private static Semver.SemVersion MapRequires(string args)
		{
			var parts = (args ?? "").Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 2) return null;
			if (!parts[0].StartsWith("AutoHotkey", System.StringComparison.OrdinalIgnoreCase) &&
				!parts[0].StartsWith("Keysharp", System.StringComparison.OrdinalIgnoreCase)) return null;
			var ver = parts[1].TrimStart('v', 'V');
			foreach (var c in CompatCandidates)
				if (Keysharp.Runtime.CompatibilityVersions.RequirementAllowsCompatibilityLine(ver, c)) return c;
			return CompatCandidates[^1];
		}

		// The last `#Requires` directive among a statement list (its mode), or null if none — used to resolve a
		// module/function body's compatibility mode (a later directive wins).
		private static Semver.SemVersion ScanRequires(IEnumerable<Stmt> body)
		{
			Semver.SemVersion result = null;
			if (body != null)
				foreach (var s in body)
					if (s is DirectiveStmt d && d.Name.Equals("Requires", System.StringComparison.OrdinalIgnoreCase) && MapRequires(d.Args) is { } m)
						result = m;
			return result;
		}

		// The default/implicit return value for the current scope: unset (null) under v2.1+, "" under v2.0.
		private ExpressionSyntax DefaultReturnExpr() =>
			Keysharp.Runtime.Script.ReturnsUnsetByDefault(_currentCompat) ? Null : Str("");

		// A [CompatibilityMode] attribute list, emitted on a member only when its mode differs from the module's.
		private AttributeListSyntax[] CompatAttr() =>
			_currentCompat.CompareSortOrderTo(_moduleCompat) != 0
				? new[] { Attr("Keysharp.Runtime.CompatibilityMode", Str(_currentCompat.ToString())) }
				: System.Array.Empty<AttributeListSyntax>();

		// Whether an `#import` module name resolves: the built-in AHK/standard module, the self-module __Main, a built-in
		// Module type (e.g. Ks), or — in the multi-module path — a script module defined in this file or pulled in from
		// an #import file (byName, null in the single-module path). Anything else is unresolved and reported as an error,
		// matching AutoHotkey (which raises a load-time error for a module that can't be found).
		private static bool ModuleResolves(string modName, Dictionary<string, ModInfo> byName)
		{
			if (modName.Length == 0) return true;   // malformed; handled by the callers
			if (modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase)
				|| modName.Equals("__Main", System.StringComparison.OrdinalIgnoreCase)) return true;
			if (byName != null && byName.ContainsKey(modName)) return true;
			return Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var t)
				&& typeof(Keysharp.Runtime.Module).IsAssignableFrom(t);
		}

		private void RegisterImport(ImportDirective d)
		{
			// The module name may be quoted or unquoted (`#import "Ks" {…}` and `#import Ks {…}` are both valid), and
			// the brace list may be `{ * }` or named — the parser normalizes all of these onto ImportDirective.
			var (modName, alias, named) = (d.Module, d.Alias, d.Named);
			if (modName.Length == 0) return;
			if (!ModuleResolves(modName, null))
			{
				Diag($"{d.Line}:{d.Column}: #Import: module not found: {modName}");
				return;
			}
			if (modName.Equals("__Main", System.StringComparison.OrdinalIgnoreCase) && named == null)
			{
				// `#import __Main` — self-import of the current module: the name resolves to a fresh Module instance
				// whose IMetaObject Get/Set reflect to the module's static fields (matches the canonical).
				if (!_inlineAliases.ContainsKey("__main"))
					_inlineAliases["__main"] = () => SyntaxFactory.ObjectCreationExpression(Ty("__Main")).WithArgumentList(SyntaxFactory.ArgumentList());
				return;
			}
			bool isAhk = modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase);

			// Functions are known before their backing global fields are emitted.
			bool IsUserDeclared(string n) => _userFuncByLower.ContainsKey(n.ToLowerInvariant());

			// `#import Mod as Alias`: the alias binds the module object (single-module has no script modules, so no
			// default-export form). A bindable module object is required; otherwise the alias is left unbound.
			if (alias != null && !IsUserDeclared(alias) && ModuleObjectExpr(modName, isAhk, false) is { } aliasVal)
				RegisterImportField(alias, aliasVal);

			var names = named ?? "*";
			if (names == "*" || names.Length == 0)
			{
				// A bare unquoted `#import Mod` (no braces, no alias) also introduces the module NAME as the module
				// object, so `Mod.Member` dispatches through IMetaObject (Get/Call) — matching the multi-module and
				// function-scope paths. Members still resolve unqualified through the wildcard below (existing behavior).
				if (named == null && alias == null && !d.Quoted && !IsUserDeclared(ImportBindingName(modName))
						&& ModuleObjectExpr(modName, isAhk, false) is { } bareObj)
					RegisterImportField(ImportBindingName(modName), bareObj);
				if (!isAhk && Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var wildType)
					&& !_wildcardModules.Contains(wildType))   // `#import "Mod"` / `#import "Mod" { * }`: resolve members on demand
					_wildcardModules.Add(wildType);
				return;
			}
			foreach (var raw in names.Split(','))
			{
				var part = raw.Trim();
				if (part.Length == 0 || part == "*") continue;   // `*` is the wildcard branch above; ignore it inside a mixed list
				string impName = part, impAlias = part;
				var asIdx = part.IndexOf(" as ", System.StringComparison.OrdinalIgnoreCase);
				if (asIdx >= 0) { impName = part.Substring(0, asIdx).Trim(); impAlias = part.Substring(asIdx + 4).Trim(); }
				var lower = impAlias.ToLowerInvariant();
				if (_fields.ContainsKey(lower) || _userFuncByLower.ContainsKey(lower)) continue;
				var init = BindBuiltinMember(modName, isAhk, impName);
				if (init == null)
				{
					// Not bindable as an import here. Only an error if the module truly has no such member;
					// otherwise leave the name to normal global resolution, as before this validation existed.
					if (!isAhk && Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out var mt) && !BuiltinModuleHasMember(mt, impName))
						Diag($"{d.Line}:{d.Column}: #Import: module '{modName}' has no exported member named '{impName}'");
					continue;
				}
				BindBuiltinImport(impAlias, init, ResolvedBuiltinProperty(modName, isAhk, impName), _fieldDecls);
			}
		}

		// ---- scoped (function/class) import frames ----

		// One resolved import name: an inline read expression and (only for a writable script variable) a write target.
		// Both are lazily materialised factories so a name referenced N times reuses the same inline shape without a
		// declaration; a null Write makes an assignment to the name a load-time diagnostic.
		private sealed class ImportBinding
		{
			public System.Func<ExpressionSyntax> Read;
			public System.Func<ExpressionSyntax> Write;   // null => not assignable (function/type/module-object/live-property)
		}

		// A `{ * }` wildcard source in a scope, resolved per referenced name in NameRef (last-added wins).
		private sealed class ImportWildcard
		{
			public string ModName;
			public bool IsAhk;
			public System.Type BuiltinType;   // built-in module type, or null for a script module
			public ModInfo Script;            // script module, or null for a built-in
		}

		// A lexical import scope (one class body or one callable body): explicit alias bindings plus wildcard sources.
		private sealed class ImportScope
		{
			public Dictionary<string, ImportBinding> Named = new(System.StringComparer.OrdinalIgnoreCase);
			public List<ImportWildcard> Wildcards = new();   // searched last-first so a later `{ * }` shadows an earlier one
		}

		// Builds a scope frame from the `#import` directives written directly in a class or callable body. Explicit
		// names bind eagerly (no declarations — just inline-expression factories); wildcards are recorded for on-demand
		// resolution in NameRef. Module-not-found / no-such-member are diagnosed here with the directive's position,
		// keeping validation eager even though binding is lazy. Returns null when the frame would be empty.
		private ImportScope BuildImportScope(IEnumerable<ImportDirective> imports)
		{
			ImportScope scope = null;
			ImportScope Frame() => scope ??= new ImportScope();
			foreach (var im in imports)
			{
				var (modName, alias, named, quoted) = (im.Module, im.Alias, im.Named, im.Quoted);
				if (modName.Length == 0) continue;
				bool isAhk = modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase);
				bool isMain = modName.Equals("__Main", System.StringComparison.OrdinalIgnoreCase);
				ModInfo script = null;
				bool isScript = _modulesByName != null && _modulesByName.TryGetValue(modName, out script);
				System.Type builtinType = null;
				if (!isScript && !isAhk && !isMain)
					Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out builtinType);
				if (!isScript && !isAhk && !isMain && builtinType == null)
				{
					Diag($"{im.Line}:{im.Column}: #Import: module not found: {modName}");
					continue;
				}

				// The fallback module object for a bare/aliased whole-module import with no default. `#import __Main` in the single-module path
				// (no _modulesByName) is a fresh `new __Main()` proxy over the current module's statics, matching the
				// existing self-import alias; in the multi-module path __Main is a normal script module (`new Program.__Main()`).
				System.Func<ExpressionSyntax> ModuleObj = (isMain && !isScript)
					? () => SyntaxFactory.ObjectCreationExpression(Ty("__Main")).WithArgumentList(SyntaxFactory.ArgumentList())
					: () => ModuleObjectExpr(modName, isAhk, isScript);

				// `as Alias`: the module's default export (script) or the module object.
				if (alias != null)
				{
					var a = alias.ToLowerInvariant();
					if (isScript && script.DefaultName != null)
						Frame().Named[a] = ScopedMemberBinding(modName, isAhk, isScript, script, builtinType, script.DefaultName, im);
					else
						Frame().Named[a] = new ImportBinding { Read = ModuleObj };
				}
				// Named `{ a, b as c, * }`.
				if (named != null)
				{
					foreach (var raw in named.Split(','))
					{
						var part = raw.Trim();
						if (part.Length == 0) continue;
						if (part == "*") { Frame().Wildcards.Add(new ImportWildcard { ModName = modName, IsAhk = isAhk, BuiltinType = builtinType, Script = script }); continue; }
						string impName = part, impAlias = part;
						var asIdx = part.IndexOf(" as ", System.StringComparison.OrdinalIgnoreCase);
						if (asIdx >= 0) { impName = part.Substring(0, asIdx).Trim(); impAlias = part.Substring(asIdx + 4).Trim(); }
						var b = ScopedMemberBinding(modName, isAhk, isScript, script, builtinType, impName, im);
						if (b != null) Frame().Named[impAlias.ToLowerInvariant()] = b;
					}
				}
				// Bare `#import Mod` binds the default export under the module leaf when present, else the module object.
				if (alias == null && named == null && !quoted)
				{
					var bare = ImportBindingName(modName).ToLowerInvariant();
					Frame().Named[bare] = isScript && script.DefaultName != null
						? ScopedMemberBinding(modName, isAhk, isScript, script, builtinType, script.DefaultName, im)
						: new ImportBinding { Read = ModuleObj };
				}
			}
			return scope;
		}

		// The binding for one explicit member of a module in a scope, or null (with a diagnostic) when the module has no
		// such member. Script variable exports and built-in properties with public setters are writable (write-through
		// to the source field/property); functions, types, read-only properties and module objects remain read-only, with
		// assignment diagnosed later in LowerAssign.
		private ImportBinding ScopedMemberBinding(string modName, bool isAhk, bool isScript, ModInfo script, System.Type builtinType, string member, ImportDirective im)
		{
			if (isScript)
			{
				// Reject a genuinely absent member (a typo like `Widht`) before binding: without this the frame would emit
				// `Program.<Mod>.@G_Widht`, an undefined field → a confusing Roslyn CS0117 at a generated position. Leniency
				// is preserved — a non-exported top-level declaration (`hiddenFn`/`hiddenVar`) is still importable by name.
				if (!ScriptModuleHasMember(script, member))
				{
					Diag($"{im.Line}:{im.Column}: #Import: module '{modName}' has no exported member named '{member}'");
					return null;
				}
				bool writable = script.Exports.TryGetValue(member, out var kind) && kind == ExportK.Variable;
				return new ImportBinding
				{
					Read = () => ModuleMemberField(modName, member),
					Write = writable ? () => ModuleMemberField(modName, member) : null,
				};
			}
			var expr = BindBuiltinMember(modName, isAhk, member);
			if (expr != null)
			{
				var writable = BindWritableBuiltinProperty(isAhk, builtinType, member);
				return new ImportBinding
				{
					Read = () => BindBuiltinMember(modName, isAhk, member),
					Write = writable != null ? () => writable : null,
				};
			}
			// The member is unknown: an error only if the module genuinely lacks it (a member we simply can't bind here
			// still resolves through ambient global lookup, unchanged).
			if (!isAhk && builtinType != null && !BuiltinModuleHasMember(builtinType, member))
				Diag($"{im.Line}:{im.Column}: #Import: module '{modName}' has no exported member named '{member}'");
			return null;
		}

		// Whether a script module makes `member` importable by name: an explicit `export`, a top-level function/class/
		// hotkey/hotstring, OR any variable assigned/declared at module scope — INCLUDING one inside top-level control
		// flow (if/loop/while/for/switch/try). Such a name still materializes a module field (EnsureGlobalField) and was
		// importable before this diagnostic existed, so it must stay accepted — see module-import.ahk's "Mixed"/"NoExports".
		// The walk descends into control-flow bodies but never into a nested function/class body (a name assigned there is
		// a local, not a module member). It deliberately over-accepts at the margins: a name that turns out to have no
		// emitted field falls back to the pre-existing behavior (a Roslyn CS0117), whereas rejecting a real member would be
		// a regression — only a genuine typo, appearing nowhere at module scope, is rejected here.
		private static bool ScriptModuleHasMember(ModInfo m, string member)
		{
			if (m.Exports.ContainsKey(member) || m.InlineMembers?.Contains(member) == true) return true;
			foreach (var s in m.Body)
				if (StmtDeclaresModuleMember(s, member, insideCallable: false)) return true;
			return false;
		}

		// Follows an assignment CHAIN (a := b := c := expr): every NameExpr target becomes a module field, not just the
		// outermost one (LowerAssign lowers the RHS through NameRef -> EnsureGlobalField for each link).
		private static bool AssignChainDeclares(Expr e, string member)
		{
			while (e is AssignExpr ae)
			{
				if (ae.Target is NameExpr n && n.Name.Equals(member, System.StringComparison.OrdinalIgnoreCase)) return true;
				e = ae.Value;
			}
			return false;
		}

		// insideCallable=false (module scope): ANY assigned/declared name — including one inside top-level control flow
		// (if/loop/while/for/switch/try) or an assignment chain — materializes a module field (EnsureGlobalField) and is
		// importable. insideCallable=true (inside a function/method body): a bare local is NOT a module member, so only an
		// explicit `global X` declaration counts (functions are assume-local; a module global is reachable there only via
		// `global`), and function/class NAMES are not re-matched. The walk is deliberately generous where materialization
		// is ambiguous: an over-accepted name with no emitted field falls back to the pre-existing Roslyn CS0117, whereas
		// rejecting a real member is a regression — only a genuine typo, declared nowhere, is rejected here.
		private static bool StmtDeclaresModuleMember(Stmt s, string member, bool insideCallable)
		{
			if (s == null) return false;
			bool Is(string name) => name != null && name.Equals(member, System.StringComparison.OrdinalIgnoreCase);
			bool Rec(Stmt st) => StmtDeclaresModuleMember(st, member, insideCallable);
			bool AnyOf(List<Stmt> body)
			{
				if (body != null)
					foreach (var st in body)
						if (Rec(st)) return true;
				return false;
			}
			switch (s is ExportStmt e ? e.Decl : s)
			{
				case FunctionDecl f:
					// The function name is a module member; its body can also declare module globals via `global`.
					return (!insideCallable && Is(f.Name)) || StmtDeclaresModuleMember(f.Body, member, insideCallable: true);
				case ClassDecl c:
					if (!insideCallable && Is(c.Name)) return true;   // the class itself is importable at module scope
					// A `global X` inside a method / (static-)init / nested method also adds a MODULE field; class members
					// and nested-class names are NOT module-scope importables, so recurse in callable mode.
					if (c.Methods != null)
						foreach (var meth in c.Methods)
							if (StmtDeclaresModuleMember(meth.Body, member, insideCallable: true)) return true;
					if (c.StaticInit != null)
						foreach (var init in c.StaticInit)
							if (StmtDeclaresModuleMember(init, member, insideCallable: true)) return true;
					if (c.InstanceInit != null)
						foreach (var init in c.InstanceInit)
							if (StmtDeclaresModuleMember(init, member, insideCallable: true)) return true;
					if (c.Nested != null)
						foreach (var n in c.Nested)
							if (StmtDeclaresModuleMember(n, member, insideCallable: true)) return true;
					return false;
				case HotkeyDef { Func: { } hkf }: return !insideCallable && Is(hkf.Name);
				case HotstringDef { Func: { } hsf }: return !insideCallable && Is(hsf.Name);
				case ExpressionStmt { Expr: AssignExpr ae }: return !insideCallable && AssignChainDeclares(ae, member);
				case DeclStmt d:
					// Module scope: every declared name is a field. Inside a callable: only `global` declarations are.
					if (d.Items != null && (!insideCallable || string.Equals(d.Keyword, "global", System.StringComparison.OrdinalIgnoreCase)))
						foreach (var it in d.Items)
							if ((it is NameExpr dn && Is(dn.Name)) || (it is AssignExpr cae && AssignChainDeclares(cae, member))) return true;
					return false;
				// Control-flow bodies run in the enclosing scope, so recurse preserving insideCallable.
				case Block b: return AnyOf(b.Body);
				case IfStmt i: return Rec(i.Then) || Rec(i.Else);
				case WhileStmt w: return Rec(w.Body) || Rec(w.Else);
				case LoopStmt l: return Rec(l.Body) || Rec(l.Else);
				case SpecialLoopStmt sl: return Rec(sl.Body) || Rec(sl.Else);
				case ForStmt fo:
					if (!insideCallable && fo.Vars != null)
						foreach (var v in fo.Vars)
							if (Is(v)) return true;
					return Rec(fo.Body) || Rec(fo.Else);
				case SwitchStmt sw:
					if (sw.Cases != null)
						foreach (var cse in sw.Cases)
							if (AnyOf(cse.Body)) return true;
					return AnyOf(sw.Default);
				case TryStmt t:
					if (Rec(t.Body)) return true;
					if (t.Catches != null)
						foreach (var cb in t.Catches)
							if ((!insideCallable && Is(cb.Var)) || Rec(cb.Body)) return true;
					return Rec(t.Else) || Rec(t.Finally);
			}
			return false;
		}

		// Resolves `lower` against the scope frames' EXPLICIT bindings, innermost-first. Explicit imports shadow module
		// globals and built-in class-name aliases (but not locals/params/statics, checked earlier in NameRef).
		private bool TryScopedExplicit(string lower, out ImportBinding binding)
		{
			for (int i = _importScopes.Count - 1; i >= 0; i--)
				if (_importScopes[i].Named.TryGetValue(lower, out binding)) return true;
			binding = null;
			return false;
		}

		// Resolves `lower` against the scope frames' WILDCARD sources, innermost-first (and last-added within a frame).
		// A hit is cached into that frame's Named map so later references and %name% deref arms are O(1). Placed BELOW
		// built-in A_* properties in NameRef so a wildcard never shadows a built-in variable (matching module scope).
		private bool TryScopedWildcard(string lower, out ImportBinding binding)
		{
			for (int i = _importScopes.Count - 1; i >= 0; i--)
			{
				var frame = _importScopes[i];
				for (int w = frame.Wildcards.Count - 1; w >= 0; w--)
				{
					var wc = frame.Wildcards[w];
					ImportBinding b = null;
					if (wc.Script != null)
					{
						if (wc.Script.Exports.TryGetValue(lower, out var kind))
							b = new ImportBinding
							{
								Read = () => ModuleMemberField(wc.ModName, lower),
								Write = kind == ExportK.Variable ? () => ModuleMemberField(wc.ModName, lower) : null,
							};
					}
					else if (BindBuiltinMember(wc.ModName, wc.IsAhk, lower) is { } expr)
						b = new ImportBinding { Read = () => BindBuiltinMember(wc.ModName, wc.IsAhk, lower) };
					if (b != null) { frame.Named[lower] = b; binding = b; return true; }
				}
			}
			binding = null;
			return false;
		}

		// The names a set of `#import` directives makes visible in a scope, added to the #Warn VarUnset "readable" set so
		// a reference to an imported function/type/variable is not flagged "never assigned". Deliberately generous (a
		// wildcard contributes every exportable member name) — over-providing only suppresses a warning, never adds one.
		// Emits NO diagnostics (unlike BuildImportScope), so the warn pre-pass doesn't double-report module errors.
		private void AddImportProvidedNames(IEnumerable<ImportDirective> imports, HashSet<string> into)
		{
			foreach (var im in imports)
			{
				var (modName, alias, named, quoted) = (im.Module, im.Alias, im.Named, im.Quoted);
				if (modName.Length == 0) continue;
				if (alias != null) into.Add(alias.ToLowerInvariant());
				bool isAhk = modName.Equals("AHK", System.StringComparison.OrdinalIgnoreCase);
				ModInfo script = null;
				bool isScript = _modulesByName != null && _modulesByName.TryGetValue(modName, out script);
				System.Type builtinType = null;
				if (!isScript && !isAhk) Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(modName, out builtinType);
				if (named != null)
				{
					foreach (var raw in named.Split(','))
					{
						var part = raw.Trim();
						if (part.Length == 0) continue;
						if (part == "*")
						{
							if (isScript) foreach (var k in script.Exports.Keys) into.Add(k.ToLowerInvariant());
							else if (builtinType != null) foreach (var nm in BuiltinMemberNames(builtinType)) into.Add(nm);
							continue;   // an AHK `{ * }` provides every global — those already never warn
						}
						var asIdx = part.IndexOf(" as ", System.StringComparison.OrdinalIgnoreCase);
						into.Add((asIdx >= 0 ? part.Substring(asIdx + 4).Trim() : part).ToLowerInvariant());
					}
				}
				if (alias == null && named == null && !quoted) into.Add(ImportBindingName(modName).ToLowerInvariant());
			}
		}

		// The lowercased AHK-visible names of a built-in module type's public static methods, nested types and
		// properties — the members a `#import "Mod" { * }` can bind. Used only by the #Warn provided-name set.
		private static IEnumerable<string> BuiltinMemberNames(System.Type modType)
		{
			// DeclaredOnly to match BindModuleMember: a wildcard binds what the module declares, not what it inherits.
			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
			foreach (var mi in modType.GetMethods(flags)) if (!mi.IsSpecialName) yield return (Script.GetUserDeclaredName(mi) ?? mi.Name).ToLowerInvariant();
			foreach (var nt in modType.GetNestedTypes(System.Reflection.BindingFlags.Public)) yield return (Script.GetUserDeclaredName(nt) ?? nt.Name).ToLowerInvariant();
			foreach (var pr in modType.GetProperties(flags)) yield return (Script.GetUserDeclaredName(pr) ?? pr.Name).ToLowerInvariant();
		}

		// Whether `lower` actually resolves to a scoped import at THIS reference (used by LowerAssign for the write gate),
		// respecting the same shadowing as NameRef: a local/param/static/closure/enclosing name of the same spelling —
		// or, for a wildcard-only match, a built-in A_* property — takes precedence and means "not an import here".
		private bool ResolvesToScopedImport(string lower, out ImportBinding binding)
		{
			binding = null;
			if (_importScopes.Count == 0) return false;
			if ((_byRefParams != null && _byRefParams.Contains(lower))
				|| (_statics != null && _statics.ContainsKey(lower))
				|| (_forcedGlobals != null && _forcedGlobals.Contains(lower))
				|| (_locals != null && _locals.Contains(lower))
				|| _scopeClosureNames.Contains(lower)
				|| _enclosingLocals.Contains(lower)
				|| _enclosingStatics.ContainsKey(lower))
				return false;
			if (TryScopedExplicit(lower, out binding)) return true;
			if (Script.TheScript.ReflectionsData.flatPublicStaticProperties.ContainsKey(lower)) return false;   // built-in var beats a wildcard
			return TryScopedWildcard(lower, out binding);
		}

		// True when `name` is provided by ANY enclosing `#import` frame currently on the stack — this callable's own
		// frame, an enclosing class frame, or an enclosing function's frame — as an explicit named binding or a wildcard
		// export. Consulted during local classification (LowerCallableBody) so such a name is NOT turned into a fresh
		// local by an assignment to it (`counter := 5` / `counter++`): that would shadow the import and lose the
		// write-through to the module variable. Real locals/params/statics/forced-globals are handled by the caller's
		// other guards and by explicitLocals added afterwards, so they still take precedence over the import. Unlike
		// ResolvesToScopedImport this does NOT consult _locals (which is being built at the call site) — it answers
		// purely from the import stack, mirroring that method's explicit-then-wildcard ordering (a built-in variable
		// still beats a wildcard).
		private bool BoundByEnclosingImport(string name)
		{
			if (_importScopes.Count == 0) return false;
			var lower = name.ToLowerInvariant();
			if (TryScopedExplicit(lower, out _)) return true;
			if (Script.TheScript.ReflectionsData.flatPublicStaticProperties.ContainsKey(lower)) return false;   // built-in var beats a wildcard
			return TryScopedWildcard(lower, out _);
		}

		// Resolves one exported name on a module type to its binding: method->Func, nested type->singleton, property->access.
		private ExpressionSyntax BindModuleMember(Type modType, string name)
		{
			// DeclaredOnly: a module exports what it DECLARES. A module type derives from Module -> Any -> object,
			// so without this the inherited public statics of those bases (object's Equals/ReferenceEquals, Any's
			// HasBase/HasProp/HasMethod/GetMethod) would import from every module as if it had declared them.
			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
			// Match on the AHK-visible name (a [UserDeclaredName], e.g. `Image` for the CLR type KeysharpImage)
			// as well as the raw CLR name — mirroring how Reflections keys every other built-in member. Without
			// the user-declared name, a member exposed under a different script name (Image, …) would not resolve.
			bool Matches(System.Reflection.MemberInfo m) =>
				(Script.GetUserDeclaredName(m) ?? m.Name).Equals(name, System.StringComparison.OrdinalIgnoreCase)
				|| m.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase);
			var method = modType.GetMethods(flags).FirstOrDefault(mi => !mi.IsSpecialName && Matches(mi));
			if (method != null)
			{
				TrackRuntimeComponent(method);
				return FuncBind(method.DeclaringType.FullName.Replace('+', '.') + "." + method.Name);
			}
			var nested = modType.GetNestedTypes(System.Reflection.BindingFlags.Public).FirstOrDefault(Matches);
			if (nested != null) return TypeSingleton(nested.FullName.Replace('+', '.'));
			var prop = modType.GetProperties(flags).FirstOrDefault(Matches);
			if (prop != null) return Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name);
			return null;
		}

		// Returns an assignable access expression only when an explicitly imported built-in member is a public static
		// property with a public setter. The AHK module is the flattened global built-in surface; other built-in modules
		// (Ks, ...) resolve against their own type.
		private ExpressionSyntax BindWritableBuiltinProperty(bool isAhk, Type modType, string name)
		{
			System.Reflection.PropertyInfo prop = null;

			if (isAhk)
				_ = Script.TheScript.ReflectionsData.flatPublicStaticProperties.TryGetValue(name.ToLowerInvariant(), out prop);
			else if (modType != null)
			{
				const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
				bool Matches(System.Reflection.MemberInfo m) =>
					(Script.GetUserDeclaredName(m) ?? m.Name).Equals(name, System.StringComparison.OrdinalIgnoreCase)
					|| m.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase);
				prop = modType.GetProperties(flags).FirstOrDefault(Matches);
			}

			return prop?.SetMethod?.IsPublic == true
				? Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name)
				: null;
		}

		// Whether a built-in module type exposes `name` as ANY public static member — method, nested type,
		// property or field — under its AHK-visible (UserDeclaredName) or CLR name. Deliberately broader than
		// what BindModuleMember can bind, so import validation only rejects a name the module genuinely does not
		// define (a member it can't bind here still resolves through normal global lookup, as it did before).
		private static bool BuiltinModuleHasMember(Type modType, string name)
		{
			// DeclaredOnly for the same reason as BindModuleMember: inherited base-class statics are not exports.
			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
			bool M(System.Reflection.MemberInfo m) =>
				(Script.GetUserDeclaredName(m) ?? m.Name).Equals(name, System.StringComparison.OrdinalIgnoreCase)
				|| m.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase);
			return modType.GetMethods(flags).Any(mi => !mi.IsSpecialName && M(mi))
				|| modType.GetNestedTypes(System.Reflection.BindingFlags.Public).Any(M)
				|| modType.GetProperties(flags).Any(M)
				|| modType.GetFields(flags).Any(M);
		}

		// A type from `stringToTypes` is a global AHK class only if it is a top-level Any-derived type
		// (Array, Map, Gui, ...). Nested types remain available through their declaring class or module
		// (Gui.List, an imported Ks.Highlight, Clr.ManagedType), but must not leak into the global namespace.
		// Static method containers (Dir, Files, Maths -- their methods are global functions) are not classes.
		private static bool IsGlobalAhkClass(System.Type type) =>
			!type.IsNested
			&& typeof(Keysharp.Builtins.Any).IsAssignableFrom(type);

		private string EnsureGlobalField(string lower)
		{
			if (_fields.TryGetValue(lower, out var existing)) return existing;

			var id = NameMangler.Escape(lower);
			_fields[lower] = id;

			ExpressionSyntax init;
			if (_userFuncByLower.TryGetValue(lower, out var orig))
				init = FuncBind(NameMangler.FunctionMethod(orig));
			else if (Script.TheScript.ReflectionsData.flatPublicStaticMethods.TryGetValue(lower, out var mi))
			{
				TrackRuntimeComponent(mi);
				init = FuncBind($"{mi.DeclaringType.FullName.Replace('+', '.')}.{mi.Name}");
			}
			else if (Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(lower, out var type) && IsGlobalAhkClass(type))
				init = TypeSingleton(type.FullName.Replace('+', '.'));
			// (AHK class-name aliases Object/Func/File are resolved INLINE in NameRef, not as a cached field.)
			else if (_wildcardModules.Select(t => BindModuleMember(t, lower)).FirstOrDefault(b => b != null) is { } wild)
				init = wild;   // resolved through a `#import "Mod" { * }` wildcard
			else
				init = Null;

			_fieldDecls.Add(ObjField(id, init));
			return id;
		}

		private string EnsureTypeField(string typeFullName, string fieldLower)
		{
			if (_fields.TryGetValue(fieldLower, out var id)) return id;
			id = NameMangler.Escape(fieldLower);
			_fields[fieldLower] = id;
			_fieldDecls.Add(ObjField(id, TypeSingleton(typeFullName)));
			return id;
		}

		// Whether a name is a declared local/static/param/forced-global (so A_LineNumber/A_LineFile shadowing works).
		private bool IsDeclaredLocal(string lower) =>
			(_locals != null && _locals.Contains(lower)) || (_statics != null && _statics.ContainsKey(lower))
			|| (_forcedGlobals != null && _forcedGlobals.Contains(lower)) || _enclosingLocals.Contains(lower);

		// The baked A_LineFile value for an #included line. Running/transpiling keeps the include's full path;
		// compiling to a distributable .cks/.exe relativizes it to the main script's directory (so a local
		// "Lib\Foo.ks" stays "Lib\Foo.ks") and never bakes an absolute path - falling back to the bare file name
		// when there is no main-script directory (a from-"*" compile) or the include resolves onto another root.
		private string IncludeLineFile(string includeFull)
		{
			if (!_compileToFile)
				return includeFull;

			if (_scriptPath != "*" && !string.IsNullOrEmpty(_includeDir))
			{
				var rel = System.IO.Path.GetRelativePath(_includeDir, includeFull);

				if (!System.IO.Path.IsPathRooted(rel))
					return rel;
			}

			return System.IO.Path.GetFileName(includeFull);
		}

		private ExpressionSyntax NameRef(string name)
		{
			var lower = name.ToLowerInvariant();
			if (_inMethod && lower == "this") { _capturedInScope = true; return Id("@this"); }
			if (_byRefParams != null && _byRefParams.Contains(lower))   // &param read: through the VarRef
				return Op("GetPropertyValue", Id(NameMangler.Escape(lower)), Str("__Value"));
			if (_statics != null && _statics.TryGetValue(lower, out var sf)) return Id(sf);
			if (_forcedGlobals != null && _forcedGlobals.Contains(lower)) return Id(EnsureGlobalField(lower));
			if (_locals != null && _locals.Contains(lower)) return Id(NameMangler.Escape(lower));
			// A bare nested function in this scope is a local closure var (hoisted) — resolve even forward references to
			// the local, never to a (qualified) module field.
			if (_scopeClosureNames.Contains(lower)) return Id(NameMangler.Escape(lower));
			// A captured enclosing-scope local: the same C# local the surrounding scope declared (C# closes over it).
			if (_enclosingLocals.Contains(lower)) { _capturedInScope = true; return Id(NameMangler.Escape(lower)); }
			// An enclosing-scope static is a module-level SL_ field reachable directly from a nested function. It is shared
			// state, not a per-call capture, so a `static` nested function reading it stays a plain Func; a non-static
			// closure keeps marking the scope captured (unchanged) so its Closure typing is preserved.
			if (_enclosingStatics.TryGetValue(lower, out var esf)) { if (!_inStaticNested) _capturedInScope = true; return Id(esf); }
			// An EXPLICIT scoped `#import` (function/class frame) shadows module globals and built-in class-name aliases,
			// but not the locals/params/statics checked above. Resolves to an inline expression — no emitted declaration.
			if (_importScopes.Count > 0 && TryScopedExplicit(lower, out var expImp)) return expImp.Read();
			if (_inlineAliases.TryGetValue(lower, out var alias)) return alias();
			// Built-in variables (A_Index, A_Clipboard, A_TickCount, …) resolve to their accessor property.
			if (Script.TheScript.ReflectionsData.flatPublicStaticProperties.TryGetValue(lower, out var prop))
				return Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name);
			// A built-in whose CLR name is not the name scripts use for it (Object->KeysharpObject, Func->KeysharpFunc,
			// File->KeysharpFile, Int32->StructInt32, ...) resolves to the class singleton INLINE (lazy — never a
			// cached static field, which for Func would perturb its init order), so `Object.Prototype`,
			// `x is Func`, `Func("name")` etc. work.
			if (!_userFuncByLower.ContainsKey(lower)
				&& Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(lower, out var renamedType)
				&& IsGlobalAhkClass(renamedType)
				&& Script.GetUserDeclaredName(renamedType) != null)
				return TypeSingleton(renamedType.FullName.Replace('+', '.'));
			// A scoped `{ * }` WILDCARD import resolves here — below built-in A_* properties (so a wildcard never shadows
			// a built-in variable) but above module-scope resolution (so a function/class wildcard shadows a module global).
			if (_importScopes.Count > 0 && TryScopedWildcard(lower, out var wildImp)) return wildImp.Read();
			// A wildcard-imported PROPERTY reflects live runtime state (e.g. A_DefaultHotstring*), so read it through
			// a direct member access each reference rather than caching it in a static field initialised once.
			if (WildcardLiveProperty(lower) is { } liveProp) return liveProp;
			if (!_userFuncByLower.ContainsKey(lower)
					&& Script.TheScript.ReflectionsData.flatPublicStaticMethods.TryGetValue(lower, out var builtinMethod))
				TrackRuntimeComponent(builtinMethod);
			var gf = EnsureGlobalField(lower);
			// Inside a (nested) class method the module's static field isn't in scope unqualified — qualify it as
			// `Program.<Module>.<field>` (e.g. a class property `Prop => ModuleFunc()`).
			return _inMethod ? Member(Member(Id("Program"), _currentModuleClass), gf) : Id(gf);
		}

		// If `lower` names a (non-method) property of a `#import "Mod" { * }` wildcard module, returns a live member
		// access to it; otherwise null. Methods/types are fine cached as fields, but properties must be re-read.
		private ExpressionSyntax WildcardLiveProperty(string lower)
		{
			// DeclaredOnly to match BindModuleMember: a wildcard binds what the module declares, not what it inherits.
			const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
			foreach (var modType in _wildcardModules)
			{
				if (modType.GetMethods(flags).Any(mi => !mi.IsSpecialName && mi.Name.Equals(lower, System.StringComparison.OrdinalIgnoreCase))) continue;
				var prop = modType.GetProperties(flags).FirstOrDefault(p => p.Name.Equals(lower, System.StringComparison.OrdinalIgnoreCase));
				if (prop != null) return Access(prop.DeclaringType.FullName.Replace('+', '.') + "." + prop.Name);
			}
			return null;
		}

		// ---- statements ----

		private StatementSyntax LowerStmt(Stmt s)
		{
			switch (s)
			{
				case ExpressionStmt es:
					if (es.Expr is CallExpr call) return ExprStmt(LowerCall(call, statement: true));
					// A comma list as a statement (`a(), b()`) discards every item's value, the last one included.
					if (es.Expr is SequenceExpr stmtSeq)
						return ExprStmt(Op("MultiStatement", stmtSeq.Items.Select(LowerAllowingUnset).ToArray()));
					var le = LowerExpr(es.Expr);
					// C# only allows call/assignment/new as a statement; wrap anything else (bare value, ternary, …).
					return ExprStmt(le is InvocationExpressionSyntax or AssignmentExpressionSyntax ? le : Op("MultiStatement", le));
				case ReturnStmt r:
					return SyntaxFactory.ReturnStatement(r.Value != null ? LowerExpr(r.Value) : DefaultReturnExpr());
				case Block b:
					return SyntaxFactory.Block(LowerStmtList(b.Body));
				case IfStmt iff:
					var elseClause = iff.Else != null ? SyntaxFactory.ElseClause(AsBlock(LowerStmt(iff.Else))) : null;
					return SyntaxFactory.IfStatement(IfTest(LowerExpr(iff.Cond)), AsBlock(LowerStmt(iff.Then)), elseClause);
				case WhileStmt w: return LowerWhile(w);
				case DeclStmt d: return LowerDecl(d);
				case LoopStmt lp: return LowerLoop(lp);
				case SpecialLoopStmt sl: return LowerSpecialLoop(sl);
				case ForStmt fr: return LowerFor(fr);
				case SwitchStmt sw: return LowerSwitch(sw);
				case TryStmt tr: return LowerTry(tr);
				case ThrowStmt th: return LowerThrow(th);
				case BreakStmt bs: return LowerBreakContinue(bs.Target, isBreak: true);
				case ContinueStmt cs: return LowerBreakContinue(cs.Target, isBreak: false);
				case GotoStmt g:
					// Emit a real C# goto only to a label in an enclosing scope / the same C# switch (a valid C# jump);
					// an out-of-scope target (which AHK forbids anyway) degrades to a no-op rather than failing to compile.
					return _labelScopes.Any(sc => sc.Contains(g.Target))
						? SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, Id(UserLabelId(g.Target)))
						: SyntaxFactory.EmptyStatement();
				case LabelStmt l: return SyntaxFactory.LabeledStatement(UserLabelId(l.Name), SyntaxFactory.EmptyStatement());
				case FunctionDecl fd:
					// A function nested inside another function (_locals != null) is scoped to it: emitted as a uniquely
					// named local function bound to a local var, so same-named nested functions in sibling scopes don't
					// collide and siblings/the parent can reference it by name. (A `static` nested function differs only in
					// not capturing the parent's non-static locals — handled leniently.) Top-level functions
					// (_locals == null) are module-level methods.
					if (_locals != null) return LowerNestedClosure(fd);
					if (_emittedFuncImpls.Add(NameMangler.FunctionMethod(fd.Name))) _pendingLambdas.Add(LowerFunction(fd));
					return null;
				case DirectiveStmt dir: return LowerDirective(dir);   // value-setting directives; rest are no-ops here
				// A hotkey/hotstring/remap inside a plain block — the `{ … }` commonly used to group a `#HotIf`
				// section — still registers globally at load; only a function or class body forbids one.
				case HotkeyDef or HotstringDef or RemapDef when _locals != null:
					Diag("Hotkeys/hotstrings are not allowed inside functions or classes.");
					return null;
				case HotkeyDef nhk: LowerHotkey(nhk); return null;
				case HotstringDef nhs: LowerHotstring(nhs); return null;
				case RemapDef nrm: LowerRemap(nrm); return null;
				default: Diag($"statement not yet lowerable: {s.GetType().Name}"); return null;
			}
		}

		// Value-setting directives lower to an assignment of the matching runtime accessor/field at their position in
		// the auto-exec (matches the canonical, which assigns the same A_* / Script members). Unknown or purely
		// load-time directives (#Requires, #ErrorStdOut, #MaxThreads, #DllLoad, #Include, #import handled elsewhere…)
		// are no-ops here.
		private StatementSyntax LowerDirective(DirectiveStmt d)
		{
			var args = (d.Args ?? "").Trim();
			// A numeric/true/false directive argument as a numeric literal (default when omitted).
			ExpressionSyntax NumArg(long deflt) =>
				args.Length == 0 ? Num(deflt.ToString())
				: args.Equals("true", System.StringComparison.OrdinalIgnoreCase) ? Num("1")
				: args.Equals("false", System.StringComparison.OrdinalIgnoreCase) ? Num("0")
				: Num(args);
			StatementSyntax Set(string target, ExpressionSyntax val) => ExprStmt(Assign(Access(target), val));
			var True = SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);

			switch (d.Name.ToUpperInvariant())
			{
				case "CLIPBOARDTIMEOUT": return Set("Keysharp.Builtins.Ks.A_ClipboardTimeout", NumArg(1000));
				case "HOTIFTIMEOUT": return Set("Keysharp.Builtins.Ks.A_HotIfTimeout",
						NumArg(Keysharp.Builtins.Accessors.DefaultHotIfTimeout));
				case "INPUTLEVEL": return Set("Keysharp.Builtins.Ks.A_InputLevel", NumArg(0));   // setter clamps 0..100
				case "SUSPENDEXEMPT": return Set("Keysharp.Builtins.Ks.A_SuspendExempt", NumArg(1));   // on Ks; setter ForceBools
				case "MAXTHREADSBUFFER":
					return Set("Keysharp.Builtins.Ks.A_MaxThreadsBuffer",
						Num(args.Equals("false", System.StringComparison.OrdinalIgnoreCase) || args == "0" ? "0" : "1"));
				case "MAXTHREADSPERHOTKEY":
					long.TryParse(args, out var mtph);
					return Set("Keysharp.Builtins.Ks.A_MaxThreadsPerHotkey", Num(System.Math.Clamp(mtph, 1, 255).ToString()));
				case "USEHOOK": return Set("MainScript.ForceKeybdHook", Op("ForceBool", NumArg(0)));
				// #NoTrayIcon: recorded here and applied at the top of Main (see BuildMain), before any tray
				// chrome is created. Emitting it inline in the auto-exec section runs too late -- the default
				// tray icon would already have been created/shown by RunMainWindow, so the directive would be
				// ignored (a brief tray flash on Linux).
				case "NOTRAYICON": _noTrayIcon = true; return null;
				// #ConsoleApp: build the compiled executable as a console (CUI) application, so a shell waits for it
				// and its stdout/stdin are the terminal's. Windows decides both from the PE subsystem field, which is
				// fixed before the process starts, so this cannot be a runtime setting - it is recorded here and
				// applied when the host stamps the apphost (see Program.CompileToExe). It affects `--compile exe`
				// only: running the script through the interpreter, or compiling it to a .cks, writes no apphost.
				// Elsewhere it is simply inert - Linux and macOS have no subsystem concept, so a shell there always
				// waits and stdio is always connected.
				case "CONSOLEAPP": _consoleApp = true; return null;
				case "NOMAINWINDOW": return Set("MainScript.NoMainWindow", True);
				case "WINACTIVATEFORCE": return Set("MainScript.WinActivateForce", True);
				// #MaxThreads N: global concurrent-thread cap (AHK clamps 1..255). MaxThreadsTotal is a uint field.
				case "MAXTHREADS":
					long.TryParse(args, out var mt);
					return Set("MainScript.MaxThreadsTotal", SyntaxFactory.CastExpression(
						SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.UIntKeyword)),
						Num(System.Math.Clamp(mt, 1, 255).ToString())));
				// #MenuMaskKey <key>: the key A_MenuMaskKey sends to mask Win/Alt; settable accessor takes the raw key text.
				case "MENUMASKKEY": return Set("Keysharp.Builtins.Accessors.A_MenuMaskKey", Str(args.Trim('"', '\'')));
				// #DllLoad [*i] file: load a DLL at startup so later DllCalls resolve it. The `*i` prefix ignores a
				// missing file (LoadDll's throwOnFailure=false). Runtime method pins the module (see Script.LoadDll).
				case "DLLLOAD":
				{
					var lib = args.Trim('"', '\'');
					bool ignoreMissing = false;
					if (lib.StartsWith("*i", System.StringComparison.OrdinalIgnoreCase)) { ignoreMissing = true; lib = lib.Substring(2).Trim().Trim('"', '\''); }
					return ExprStmt(Inv(Access("MainScript.LoadDll"), Str(lib), ignoreMissing ? False : True));
				}
				// Prescanned blocks are emitted separately; unseen ones are nested in an unsupported scope.
				case "CSHARP":
					if (_csharpSeen?.Contains(d) != true)
						Diag($"{DirectiveAnchor(d)}#CSharp must appear at the top level of a script or module, or in a class body, not inside a function or block");

					return null;
				// A valid terminator is consumed with its block.
				case "ENDCSHARP":
					Diag($"{DirectiveAnchor(d)}#EndCSharp without a matching #CSharp");
					return null;
				// #Package [*i] <id> [version]: make a package's assemblies available to `Clr` before the script uses
				// them. The program's whole package set was gathered by PrescanPackageDirectives, so the first
				// directive emits one call covering all of them and the rest emit nothing (see the _packages field).
				case "PACKAGE": return LowerPackageDirective(d);
				// #Persistent [false|0]: keep the script running with no hotkeys/timers. Sets the same flag the DHHR uses
				// to emit Flow.Persistent() (see BuildOuterAuto); a `false`/`0` argument leaves it off.
				case "PERSISTENT":
					if (!(args.Equals("false", System.StringComparison.OrdinalIgnoreCase) || args == "0")) _persistent = true;
					return null;
				// #SingleInstance [Force|Ignore|Prompt|Off]: recorded here and emitted as a HandleSingleInstance guard at
				// the top of Main (see BuildMain). Default option is Force (matches AHK v2 and the canonical).
				case "SINGLEINSTANCE":
					_singleInstanceMode = args.Length == 0 ? "Force" : args.ToUpperInvariant() switch
					{
						"FORCE" => "Force", "IGNORE" => "Ignore", "PROMPT" => "Prompt", "OFF" => "Off",
						_ => null
					};
					if (_singleInstanceMode == null) Diag($"#SingleInstance: unrecognized option '{args}'");
					return null;
				// #HookMutexName <name>: names the keyboard/mouse hook mutex (so multiple scripts can share a hook). Passed
				// to the Script constructor (see AssembleProgram), which sets HookThread.MutexName.
				case "HOOKMUTEXNAME":
					if (args.Length > 0) _hookMutexName = args.Trim('"', '\'');
					return null;
				// #ErrorStdOut: send uncaught errors to stderr instead of a dialog (runtime honors MainScript.ErrorStdOut).
				case "ERRORSTDOUT": return Set("MainScript.ErrorStdOut", True);
				// #Requires capability <Name, ...>: request the listed platform capabilities at startup so a script's
				// permissions are resolved up front (one batched prompt) rather than sprung on the user the first time
				// a gated feature runs. Emitted at the directive's position in the auto-exec, i.e. before the script's
				// own hotkeys/hooks/actions. A version requirement (`#Requires AutoHotkey v2.0`) carries no capability
				// list and is handled by the compatibility scan (ScanRequires/MapRequires) — a no-op here.
				case "REQUIRES":
				{
					var reqParts = args.Split((char[])null, 2, System.StringSplitOptions.RemoveEmptyEntries);
					if (reqParts.Length == 2
						&& (reqParts[0].Equals("capability", System.StringComparison.OrdinalIgnoreCase)
							|| reqParts[0].Equals("capabilities", System.StringComparison.OrdinalIgnoreCase))
						&& reqParts[1].Trim() is { Length: > 0 } caps)
						// RequireCapabilities (not RequestCapabilities): a #Requires directive is a hard
						// requirement, so a denied prompt exits the app. The runtime builtin does not exit.
						return ExprStmt(Inv(Access("Keysharp.Runtime.Script.RequireCapabilities"), Str(caps)));
					return null;
				}
				// #Warn config is applied in a prescan (PrescanWarnDirectives) so it is location-independent (per the
				// docs); here it is a no-op. #Nullable / #NoDynamicVars are recognized compile-time hints with no effect.
				case "WARN":
				case "NULLABLE":
				case "NODYNAMICVARS":
				// Source-folding region markers and diagnostic-pragma directives have no runtime semantics.
				case "REGION":
				case "ENDREGION":
				case "LINE":
				case "PRAGMA":
				// #StructPack is applied per-field by the parser inside a class/struct body; at module level it is a no-op.
				case "STRUCTPACK":
					return null;
				// #Error emits a user-specified compile-time diagnostic, positioned like #Warning below.
				case "ERROR":
					Diag(DirectiveMessage(d, args, "#Error directive"));
					return null;
				// #Warning is the non-fatal sibling of #Error: reported at compile time like one, but it does not stop
				// the compile. Deliberately NOT routed through #Warn's load-time output — that would bake the message
				// into a compiled exe and re-show it to the script's users on every run, and this is a message for
				// whoever builds the script. #Warn's Off/StdOut/MsgBox config does not apply for the same reason.
				case "WARNING":
					CompileWarnings.Add(DirectiveMessage(d, args, "#Warning directive"));
					return null;
				// #Assembly* metadata -> assembly attributes on the generated unit (A_Assembly* read them at runtime).
				case "ASSEMBLYTITLE": _asmAttributes.Add(("System.Reflection.AssemblyTitleAttribute", args)); return null;
				case "ASSEMBLYDESCRIPTION": _asmAttributes.Add(("System.Reflection.AssemblyDescriptionAttribute", args)); return null;
				case "ASSEMBLYCONFIGURATION": _asmAttributes.Add(("System.Reflection.AssemblyConfigurationAttribute", args)); return null;
				case "ASSEMBLYCOMPANY": _asmAttributes.Add(("System.Reflection.AssemblyCompanyAttribute", args)); return null;
				case "ASSEMBLYPRODUCT": _asmAttributes.Add(("System.Reflection.AssemblyProductAttribute", args)); return null;
				case "ASSEMBLYCOPYRIGHT": _asmAttributes.Add(("System.Reflection.AssemblyCopyrightAttribute", args)); return null;
				case "ASSEMBLYTRADEMARK": _asmAttributes.Add(("System.Reflection.AssemblyTrademarkAttribute", args)); return null;
				case "ASSEMBLYVERSION":
					_asmAttributes.Add(("System.Reflection.AssemblyVersionAttribute", args));
					_asmAttributes.Add(("System.Reflection.AssemblyFileVersionAttribute", args));   // A_AssemblyVersion reads FileVersion
					return null;
				// #AssemblyName <name>: the assembly's identity, which is what Assembly.GetName().Name reports. Unlike
				// its siblings this is not an attribute — .NET has no AssemblyNameAttribute — so it is surfaced to the
				// compiler instead (see AssemblyName below), which passes it to CSharpCompilation.Create.
				case "ASSEMBLYNAME":
					if (args.Length > 0)
						AssemblyName = args.Trim('"', '\'');

					return null;
				default:
					// A directive nobody handles is a typo, a v1 leftover, or an unsupported feature. Letting it vanish
					// means the script runs with the setting silently absent and fails somewhere unrelated later, so it
					// is rejected here. Deprecated AutoHotkey v1 directives (#InstallMouseHook, #NoEnv, #LTrim, …) are
					// deliberately in that set: Keysharp targets v2, and silently ignoring them hides a porting bug.
					// The allow-list below is only for directives that ARE handled, just earlier in the pipeline.
					if (!DirectivesHandledElsewhere.Contains(d.Name))
						Diag($"{Keywords.ExUnknownDirv}: #{d.Name}");

					return null;
			}
		}

		/// <summary>
		/// Directives consumed before the lowerer's switch sees them: the parser splices the #Include/#Define/#If
		/// families, and the DHHR takes #HotIf/#Hotstring at top level (so only a nested one reaches the switch).
		/// They reach the default arm as legitimate no-ops. Anything genuinely implemented has a `case` above instead.
		/// </summary>
		private static readonly HashSet<string> DirectivesHandledElsewhere = new(System.StringComparer.OrdinalIgnoreCase)
		{
			"Include", "IncludeAgain", "Import", "Module", "Export", "HotIf", "Hotstring",
			"Define", "Undef", "If", "ElIf", "Else", "EndIf",
		};

		// ---- #Warn (compile-time warning analysis) ----

		private static readonly HashSet<string> WarnModeNames = new(System.StringComparer.OrdinalIgnoreCase)
		{ "MsgBox", "StdOut", "OutputDebug", "Off", "On" };
		private static string CanonWarnMode(string m) => m.ToLowerInvariant() switch
		{ "stdout" => "StdOut", "outputdebug" => "OutputDebug", _ => "MsgBox" };

		// Applies one `#Warn [Type], [Mode]` directive to the per-type mode config (see the field declarations).
		private void ApplyWarnDirective(string args)
		{
			args = (args ?? "").Trim();
			if (args.Length == 0)   // bare `#Warn`: VarUnset + Unreachable + NamedArg on (default mode), LSAG off.
			{ _warnVarUnset = _warnUnreachable = _warnNamedArg = _warnDefaultMode; _warnLocalSameAsGlobal = null; return; }
			int comma = args.IndexOf(',');
			var typeStr = (comma < 0 ? args : args.Substring(0, comma)).Trim();
			var modeStr = comma < 0 ? "" : args.Substring(comma + 1).Trim();
			// A lone mode (`#Warn StdOut` / `#Warn Off` / `#Warn On`) sets the program-wide default + the on warnings.
			if (comma < 0 && WarnModeNames.Contains(typeStr))
			{
				if (typeStr.Equals("On", System.StringComparison.OrdinalIgnoreCase))
				{ _warnVarUnset = _warnUnreachable = _warnNamedArg = _warnDefaultMode; return; }
				_warnDefaultMode = typeStr.Equals("Off", System.StringComparison.OrdinalIgnoreCase) ? null : CanonWarnMode(typeStr);
				if (_warnVarUnset != null) _warnVarUnset = _warnDefaultMode;
				if (_warnUnreachable != null) _warnUnreachable = _warnDefaultMode;
				if (_warnLocalSameAsGlobal != null) _warnLocalSameAsGlobal = _warnDefaultMode;
				if (_warnNamedArg != null) _warnNamedArg = _warnDefaultMode;
				return;
			}
			var mode = modeStr.Length == 0 || modeStr.Equals("On", System.StringComparison.OrdinalIgnoreCase) ? _warnDefaultMode
					 : modeStr.Equals("Off", System.StringComparison.OrdinalIgnoreCase) ? null : CanonWarnMode(modeStr);
			switch (typeStr.ToLowerInvariant())
			{
				case "": case "all": _warnVarUnset = _warnUnreachable = _warnLocalSameAsGlobal = _warnNamedArg = mode; break;
				case "varunset": _warnVarUnset = mode; break;
				case "unreachable": _warnUnreachable = mode; break;
				case "localsameasglobal": _warnLocalSameAsGlobal = mode; break;
				case "namedarg": _warnNamedArg = mode; break;
				default: Diag($"#Warn: unrecognized warning type '{typeStr}'"); break;
			}
		}

		// ---- #CSharp ----
		private void PrescanCSharpDirectives(List<Stmt> body, string moduleName, string moduleDir)
		{
			foreach (var s in body)
				if ((s is ExportStmt ex ? ex.Decl : s) is ClassDecl cd)
					PrescanClassCSharp(cd, moduleName, moduleDir, null);

			foreach (var s in body)
			{
				if (s is not CSharpDirective d || !(_csharpSeen ??= new(ReferenceEqualityComparer.Instance)).Add(d))
					continue;

				if (!ResolveInlineBlockSource(d, moduleDir))
					continue;

				(_inlineBlocks ??= []).Add((moduleName, null, d));
			}
		}

		private bool ResolveInlineBlockSource(CSharpDirective d, string moduleDir)
		{
			if (d.Args.Length > 0)
				Diag($"{DirectiveAnchor(d)}#CSharp takes no options"
					 + (d.Args.Trim().Equals("unsafe", System.StringComparison.OrdinalIgnoreCase)
						? " - `unsafe` is no longer needed, pointer code is always allowed"
						: $"; got `{d.Args}`"));

			if (d.IsFileForm)
			{
				var baseDir = d.File != null ? System.IO.Path.GetDirectoryName(d.File) : moduleDir ?? _includeDir;
				var path = ResolveInlineFile(d, baseDir);

				if (path == null)
					return false;

				try
				{
					d.Code = System.IO.File.ReadAllText(path);
				}
				catch (System.Exception ex)
				{
					Diag($"{DirectiveAnchor(d)}#CSharp: cannot read '{path}': {ex.Message}");
					return false;
				}

				d.CodeLine = 1;
				d.CodeFile = path;
			}
			else
				d.CodeFile = d.File ?? _scriptPath;

			return true;
		}

		private string ResolveInlineFile(CSharpDirective d, string baseDir)
		{
			if (d.LibraryForm)
			{
				var name = Parser.NormalizeDirectiveSeparators((d.FilePath ?? "").Trim());
				var library = Parser.FindLibraryFile(name, baseDir, [".cs"]);

				if (library != null)
					return library;

				Diag($"{DirectiveAnchor(d)}#CSharp: library not found: <{name}>");
				return null;
			}

			var raw = Parser.NormalizeDirectiveSeparators(
						  Parser.ExpandPathVars((d.FilePath ?? "").Trim(), _includeDir, d.File ?? _scriptPath ?? _includeDir));

			if (raw.Length == 0)
			{
				Diag($"{DirectiveAnchor(d)}#CSharp: the file form needs a path, e.g. `#CSharp \"helper.cs\"`");
				return null;
			}

			if (System.IO.Path.IsPathRooted(raw))
			{
				if (System.IO.File.Exists(raw))
					return System.IO.Path.GetFullPath(raw);

				Diag($"{DirectiveAnchor(d)}#CSharp: cannot find '{raw}'");
				return null;
			}

			var tried = new List<string>();
			// A file path is used verbatim; module suffix rules do not apply.
			var found = SearchModulePath(baseDir, dir => [System.IO.Path.Combine(dir, raw)], tried);

			if (found != null)
				return found;

			Diag($"{DirectiveAnchor(d)}#CSharp: cannot find '{raw}'. Looked in: {string.Join(", ", tried)}");
			return null;
		}

		private string SearchModulePath(string localDir, System.Func<string, IEnumerable<string>> candidates, List<string> tried = null)
		{
			var dirs = new List<string>();

			if (!string.IsNullOrEmpty(localDir))
				dirs.Add(localDir);

			dirs.AddRange(ModuleSearchPath());

			foreach (var dir in dirs)
			{
				IEnumerable<string> cands;

				// One invalid candidate must not prevent searching later directories.
				try { cands = [.. candidates(dir).Select(System.IO.Path.GetFullPath)]; }
				catch { continue; }

				foreach (var c in cands)
				{
					if (System.IO.File.Exists(c))
						return c;

					if (tried != null && !tried.Contains(c, System.StringComparer.OrdinalIgnoreCase))
						tried.Add(c);
				}
			}

			return null;
		}

		private void RegisterInlineModuleMembers(ModInfo m)
		{
			if (_inlineBlocks == null)
				return;

			List<(CSharpDirective Directive, MethodDeclarationSyntax Method, AttributeSyntax Attribute)> inlineExports = null;

			foreach (var (module, classPath, d) in _inlineBlocks)
			{
				if (module != m.Name || classPath != null)
					continue;

				foreach (var mem in Parsed(d).Members)
				{
					var method = mem as MethodDeclarationSyntax;
					var isPublicStaticMethod = method != null && IsScriptVisible(method)
						&& method.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword));

					if (classPath == null && isPublicStaticMethod)
						(m.InlineMembers ??= new(System.StringComparer.OrdinalIgnoreCase)).Add(method.Identifier.Text);

					// Misuse (non-public, non-static, non-method) is diagnosed in BuildInlineSources, which every
					// lowering path runs — this pass exists only on the multi-module one, and a library
					// validated standalone must get the same verdict its importers will. Consume valid exports.
					if (isPublicStaticMethod && InlineExportAttribute(mem) is { } attr)
						(inlineExports ??= []).Add((d, method, attr));
				}
			}

			if (inlineExports == null)
				return;

			if (!m.HasExplicitExports)
			{
				m.Exports.Clear();
				m.DefaultName = null;
				m.HasExplicitExports = true;
			}

			foreach (var (d, method, attr) in inlineExports)
			{
				var name = method.Identifier.Text;
				m.Exports[name] = ExportK.Function;

				if (InlineExportIsDefault(attr))
				{
					if (m.DefaultName != null && !m.DefaultName.Equals(name, System.StringComparison.OrdinalIgnoreCase))
						Diag($"{InlineMemberLocation(d, method)}#CSharp: module '{m.Name}' has more than one default export");
					else
						m.DefaultName = name;
				}
			}
		}

		private void RegisterInlineFunctionsFor(string moduleName)
		{
			foreach (var (module, classPath, d) in _inlineBlocks ?? [])
			{
				if (module != moduleName || classPath != null)
					continue;

				var parsed = Parsed(d);

				foreach (var m in parsed.Members)
				{
					if (m is not MethodDeclarationSyntax meth || !IsScriptVisible(meth) || !meth.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)))
						continue;

					var name = meth.Identifier.Text;
					var lower = name.ToLowerInvariant();

					if (_userFuncByLower.TryGetValue(lower, out var existing))
					{
						// The script-function pre-pass reports the opposite collision direction.
						var line = d.CodeLine - 1 + meth.GetLocation().GetLineSpan().StartLinePosition.Line - parsed.WrapperOffset + 1;
						Diag($"{System.IO.Path.GetFileName(d.CodeFile ?? _scriptPath ?? "*")}:{line}:1: "
							 + $"#CSharp: public method '{name}' collides with another #CSharp function, '{existing}' (function names are case-insensitive); rename one of them");
						continue;
					}

					_userFuncByLower[lower] = name;
					_ = (_inlineFuncNames ??= new(System.StringComparer.OrdinalIgnoreCase)).Add(name);
				}
			}
		}

		private sealed record InlineUsing(string Text, bool Global);
		private sealed record ParsedBlock(List<InlineUsing> Usings, List<MemberDeclarationSyntax> Members,
										  CompilationUnitSyntax Probe, int WrapperOffset, string Body, CSharpParseOptions Options);

		private Dictionary<CSharpDirective, ParsedBlock> _parsedBlocks;

		private ParsedBlock Parsed(CSharpDirective d)
		{
			if (_parsedBlocks?.TryGetValue(d, out var hit) == true)
				return hit;

			var options = new CSharpParseOptions(LanguageVersion.LatestMajor, DocumentationMode.None,
										  SourceCodeKind.Regular, d.Defines ?? _inlineDefines);
			var (usings, body) = SplitUsings(d.Code ?? "", options);
			var members = ParseInlineMembers(body, options, out var wrapperOffset, out var probe);
			return (_parsedBlocks ??= [])[d] = new ParsedBlock(usings, [.. members], probe, wrapperOffset, body, options);
		}

		// Extract leading usings without changing member line positions.
		private static (List<InlineUsing> Usings, string Body) SplitUsings(string code, CSharpParseOptions options)
		{
			var normalized = code.Replace("\r\n", "\n");
			var unit = SyntaxFactory.ParseCompilationUnit(normalized, options: options);
			var firstMember = unit.Members.FirstOrDefault()?.SpanStart ?? int.MaxValue;
			var directives = unit.Usings.Where(u => u.SpanStart < firstMember && !u.SemicolonToken.IsMissing).ToList();
			// One C# tree is emitted per script module. Treat `global using` as module-global by placing the
			// normalized directive inside that tree's namespace; a real C# global using would leak into every
			// imported module because Roslyn applies it compilation-wide, across syntax trees.
			var usings = directives.Select(u => new InlineUsing(
										 u.WithGlobalKeyword(default).ToString().Trim(),
										 u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))).ToList();
			var body = normalized.ToCharArray();

			// Keep newlines and conditional trivia intact.
			foreach (var directive in directives)
				for (var i = directive.Span.Start; i < directive.Span.End; i++)
					if (body[i] != '\n' && body[i] != '\r')
						body[i] = ' ';

			return (usings, new string(body));
		}

		// A probe class makes bare method declarations parseable as members.
		private static IEnumerable<MemberDeclarationSyntax> ParseInlineMembers(string body, CSharpParseOptions options,
															out int wrapperOffset, out CompilationUnitSyntax probe)
		{
			const string open = "class __KS_Probe {\n";
			wrapperOffset = 1;   // one synthesized line precedes the body
			probe = SyntaxFactory.ParseCompilationUnit(open + body + "\n}", options: options);
			return probe.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
				   .FirstOrDefault()?.Members ?? Enumerable.Empty<MemberDeclarationSyntax>();
		}

		// The merged inline tree has one symbol set, so preserve the branch selected when each block was parsed.
		private sealed class InlineConditionalFreezer() : CSharpSyntaxRewriter(true)
		{
			private static ExpressionSyntax Value(bool taken) => SyntaxFactory.LiteralExpression(
				taken ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);

			public override SyntaxNode VisitIfDirectiveTrivia(IfDirectiveTriviaSyntax node) =>
				base.VisitIfDirectiveTrivia(node.WithCondition(Value(node.BranchTaken)));

			public override SyntaxNode VisitElifDirectiveTrivia(ElifDirectiveTriviaSyntax node) =>
				base.VisitElifDirectiveTrivia(node.WithCondition(Value(node.BranchTaken)));
		}

		// Per-Lowerer, not static: CSharpSyntaxRewriter keeps an internal recursion-depth counter across Visit
		// calls, so one shared instance is mutable process-wide state that concurrent compiles would corrupt.
		private readonly InlineConditionalFreezer inlineConditionalFreezer = new();
		private MemberDeclarationSyntax FreezeConditionals(MemberDeclarationSyntax member) =>
			(MemberDeclarationSyntax)inlineConditionalFreezer.Visit(member);

		private IReadOnlyCollection<string> _inlineDefines = [];

		// The final tree receives the script's platform symbols; block-local choices are already frozen.
		public IReadOnlyCollection<string> InlineDefines => _inlineDefines;

		private bool ReportInlineSyntaxErrors(ParsedBlock parsed, int lineOffset, string file)
		{
			var errors = parsed.Probe.GetDiagnostics()
						 .Where(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
						 .ToList();

			if (errors.Count == 0)
				return true;

			var shortFile = string.IsNullOrEmpty(file) ? "" : System.IO.Path.GetFileName(file);
			// Lines are the block's own, so a position from the member parse first sheds the probe's wrapper --
			// clamped, since a diagnostic anchored on the wrapper itself would otherwise point above the block.
			string At(Microsoft.CodeAnalysis.Text.LinePosition pos, int wrapperLines = 0) =>
				$"{(shortFile.Length != 0 ? shortFile + ":" : "")}{lineOffset + System.Math.Max(pos.Line - wrapperLines, 0) + 1}:{pos.Character + 1}: #CSharp: ";

			// A member parse can only report a bare statement as cascading token errors that never name it, so
			// ask the script-kind parse, which classifies statements and declarations exactly.
			if (FirstInlineStatement(parsed) is { } stmt)
			{
				Diag(At(stmt.GetLocation().GetLineSpan().StartLinePosition)
					 + $"`{InlineSnippet(stmt)}` is a statement, but a #CSharp block holds declarations only. "
					 + "Move it into a method the script can call, or give it a type if you meant to declare a field.");
				return false;
			}

			foreach (var e in errors.Take(5))
				Diag(At(e.Location.GetLineSpan().StartLinePosition, parsed.WrapperOffset) + e.GetMessage());

			return false;
		}

		// Only a clean script-kind parse is trustworthy: if it errors too, the block has a genuine syntax
		// problem and the compiler's own messages are the better report.
		private static GlobalStatementSyntax FirstInlineStatement(ParsedBlock parsed)
		{
			var script = SyntaxFactory.ParseCompilationUnit(parsed.Body, options: parsed.Options.WithKind(SourceCodeKind.Script));
			return script.GetDiagnostics().Any(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
				   ? null
				   : script.Members.OfType<GlobalStatementSyntax>().FirstOrDefault();
		}

		// Collapsed to one capped line: the message already carries the statement's position, and a wrapped
		// statement cut at its first newline would quote a fragment too short to recognise.
		private static string InlineSnippet(SyntaxNode node)
		{
			var text = string.Join(" ", node.ToString().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
			return text.Length > 40 ? text[..40] + "..." : text;
		}

		private static bool IsScriptVisible(MemberDeclarationSyntax m) =>
			m.Modifiers.Any(t => t.IsKind(SyntaxKind.PublicKeyword));

		private static bool ReferencesThisFunc(MemberDeclarationSyntax m) =>
			m.DescendantNodes().OfType<IdentifierNameSyntax>()
			 .Any(n => n.Identifier.ValueText.Equals("A_ThisFunc", System.StringComparison.OrdinalIgnoreCase));

		private static AttributeSyntax InlineExportAttribute(MemberDeclarationSyntax member) =>
			member.AttributeLists.SelectMany(list => list.Attributes).FirstOrDefault(attr =>
				attr.Name.ToString() is "Export" or "Keysharp.Runtime.Export" or "global::Keysharp.Runtime.Export");

		private static bool InlineExportIsDefault(AttributeSyntax attr) =>
			attr.ArgumentList?.Arguments.Any(arg =>
				arg.NameEquals?.Name.Identifier.ValueText == "Default"
				&& arg.Expression.IsKind(SyntaxKind.TrueLiteralExpression)) == true;

		private string InlineMemberLocation(CSharpDirective directive, MemberDeclarationSyntax member)
		{
			var parsed = Parsed(directive);
			var line = directive.CodeLine + member.GetLocation().GetLineSpan().StartLinePosition.Line - parsed.WrapperOffset;
			return $"{System.IO.Path.GetFileName(directive.CodeFile ?? _scriptPath ?? "*")}:{line}:1: ";
		}

		private static string[] DeclaredNames(MemberDeclarationSyntax m) => m switch
		{
			MethodDeclarationSyntax meth => [meth.Identifier.Text],
			FieldDeclarationSyntax fd => [.. fd.Declaration.Variables.Select(v => v.Identifier.Text)],
			PropertyDeclarationSyntax pd => [pd.Identifier.Text],
			TypeDeclarationSyntax td => [td.Identifier.Text],
			_ => []
		};

		private void CheckInlineNameCollision(MemberDeclarationSyntax m, string module, bool isFunction, string at)
		{
			foreach (var n in DeclaredNames(m))
			{
				if (isFunction)
				{
					// Module functions may use script names, but not their generated binding names.
					if (n.Equals(NameMangler.ModuleClass(module), System.StringComparison.Ordinal))
						Diag($"{at}#CSharp: a public method cannot have its module's exact name ('{n}') — the generated module class "
							 + "is named that, and C# forbids a member named like its enclosing type. Re-case it (but not to "
							 + "all-lowercase); script calls and the default-export match are case-insensitive.");
					else if (n == n.ToLowerInvariant())
						Diag($"{at}#CSharp: a public method cannot be all-lowercase ('{n}') — its script binding is emitted as a "
							 + "field of that exact name in the same class. Capitalize any letter; script calls are case-insensitive.");

					continue;
				}

				if (_moduleGlobals?.TryGetValue(module, out var globals) == true && globals.Contains(n))
					Diag($"{at}#CSharp: '{n}' collides with a variable or function the script declares; rename one of them");
				else if (Keywords.InlineReservedModuleNames.Contains(n) || n.Equals(NameMangler.ModuleClass(module), System.StringComparison.OrdinalIgnoreCase))
					Diag($"{at}#CSharp: '{n}' is a name Keysharp generates into this class; rename it");
			}
		}

		// Named types remain valid so callers may use a receiver type more precise than object.
		private static bool CanHoldReceiver(ParameterSyntax p) =>
			!p.Modifiers.Any(t => t.IsKind(SyntaxKind.ParamsKeyword) || t.IsKind(SyntaxKind.RefKeyword)
								  || t.IsKind(SyntaxKind.OutKeyword) || t.IsKind(SyntaxKind.InKeyword))
			&& p.Type switch
			{
				PredefinedTypeSyntax pt => pt.Keyword.IsKind(SyntaxKind.ObjectKeyword),
				PointerTypeSyntax or ArrayTypeSyntax or NullableTypeSyntax or TupleTypeSyntax or FunctionPointerTypeSyntax => false,
				_ => true
			};

		// A returned Task<T> reaches the script as a Ks.Task whose Result is T, so T crosses the boundary too and
		// has to answer the same question -- otherwise every rejected return type is reachable again just by
		// wrapping it. A Task<T> parameter crosses nothing: CoerceBoundaryCast hands over the task itself, so T
		// is never converted and the rule does not apply there.
		private static bool IsUnsupportedBoundaryReturn(TypeSyntax type)
		{
			// Only Task is intercepted on the way out, so a ValueTask would reach the script as an opaque Clr
			// object with no Status, Wait or Then -- which reads as the feature silently not working.
			if (TypeNameOf(type) is "ValueTask")
				return true;

			if (AwaitedTypeArgument(type) is { } awaited)
				return IsUnsupportedBoundaryReturn(awaited);

			return IsUnsupportedBoundaryType(type);
		}

		/// <summary>The T of a <c>Task&lt;T&gt;</c> in any spelling, else null.</summary>
		private static TypeSyntax AwaitedTypeArgument(TypeSyntax type)
		{
			var generic = type switch
			{
				GenericNameSyntax g => g,
				QualifiedNameSyntax { Right: GenericNameSyntax qg } => qg,
				AliasQualifiedNameSyntax { Name: GenericNameSyntax ag } => ag,
				_ => null
			};
			return generic is { TypeArgumentList.Arguments.Count: 1 }
				   && generic.Identifier.ValueText is "Task"
				   ? generic.TypeArgumentList.Arguments[0]
				   : null;
		}

		/// <summary>The rightmost identifier of a named type in any spelling, else null.</summary>
		private static string TypeNameOf(TypeSyntax type) => type switch
		{
			IdentifierNameSyntax id => id.Identifier.ValueText,
			GenericNameSyntax generic => generic.Identifier.ValueText,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
			AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
			_ => null
		};

		private static bool IsUnsupportedBoundaryType(TypeSyntax type)
		{
			if (type is PointerTypeSyntax or FunctionPointerTypeSyntax or RefTypeSyntax or NullableTypeSyntax or TupleTypeSyntax)
				return true;

			if (type is PredefinedTypeSyntax predefined)
				return predefined.Keyword.IsKind(SyntaxKind.CharKeyword) || predefined.Keyword.IsKind(SyntaxKind.DecimalKeyword);

			var name = TypeNameOf(type);
			// Char/Decimal/Nullable: the spelled-out forms of the keyword rejections above, so `System.Char` gets
			// the same verdict as `char`. This matches on the name by design, since no binding happens during
			// lowering, so a `using` alias for one of these, or a user-defined ref struct, is not recognized here
			// — those fail at the call instead, and the docs say so.
			return name is "Span" or "ReadOnlySpan" or "TypedReference" or "ArgIterator" or "Char" or "Decimal" or "Nullable";
		}

		private static bool IsObjectParams(ParameterSyntax parameter) =>
			parameter.Modifiers.Any(t => t.IsKind(SyntaxKind.ParamsKeyword))
			&& parameter.Type is ArrayTypeSyntax
			{
				ElementType: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.ObjectKeyword },
				RankSpecifiers.Count: 1
			};

		// ONE signature rule for every script-callable method, whichever scope declares it. Class members are as
		// reachable as module functions (Script.InitClass registers them), and a shape the dispatcher cannot marshal
		// fails there at CALL time while the delegate is being compiled -- outside the [InlineCSharp] boundary, as a
		// raw CLR error. The receiver slot of a class-static member is checked like any other parameter: object and
		// named types pass, and an unmarshallable receiver (`Span<T> @this`) is as broken as an unmarshallable
		// argument.
		private bool CheckInlineBoundarySignature(MethodDeclarationSyntax meth, string at)
		{
			var bad = meth.ParameterList.Parameters.FirstOrDefault(p =>
						  IsUnsupportedBoundaryType(p.Type)
						  || p.Modifiers.Any(t => t.IsKind(SyntaxKind.RefKeyword) || t.IsKind(SyntaxKind.OutKeyword) || t.IsKind(SyntaxKind.InKeyword))
						  || p.Modifiers.Any(t => t.IsKind(SyntaxKind.ParamsKeyword)) && !IsObjectParams(p));

			if (bad != null)
			{
				Diag($"{at}#CSharp: public method '{meth.Identifier.Text}' has a parameter ('{bad.Identifier.Text}') that cannot be passed from a script. "
					 + "Use a supported scalar/reference type or params object[], or make the method non-public.");
				return false;
			}

			// Only Task crosses as a script Task; a ValueTask would arrive as an opaque Clr object with no
			// Status, Wait or Then, which reads as the feature silently not working. Ahead of the general
			// return-type check so the message names the actual problem.
			if (TypeNameOf(meth.ReturnType) is "ValueTask")
			{
				Diag($"{at}#CSharp: public method '{meth.Identifier.Text}' returns ValueTask, which a script cannot wait "
					 + "for. Return 'Task' or 'Task<T>', or make the method non-public.");
				return false;
			}

			if (IsUnsupportedBoundaryReturn(meth.ReturnType))
			{
				Diag($"{at}#CSharp: public method '{meth.Identifier.Text}' has a return type that cannot be handed to a script. "
					 + "Use a supported scalar/reference type or make the method non-public.");
				return false;
			}

			if (meth.TypeParameterList != null)
			{
				Diag($"{at}#CSharp: public method '{meth.Identifier.Text}' is generic, and a script cannot supply a type argument. "
					 + "Make it non-generic, or non-public if no script was meant to call it.");
				return false;
			}

			// `async void` has nowhere to put a failure: the exception is raised on whatever context the method
			// captured rather than travelling back with a task, so the script can neither await it nor catch it,
			// and on the scheduler path it is swallowed outright. `async Task` carries both the completion and
			// the failure, which is what Await() and Task.Error read.
			if (meth.Modifiers.Any(t => t.IsKind(SyntaxKind.AsyncKeyword))
					&& meth.ReturnType is PredefinedTypeSyntax { Keyword: var kw } && kw.IsKind(SyntaxKind.VoidKeyword))
			{
				Diag($"{at}#CSharp: public method '{meth.Identifier.Text}' is 'async void', so a script cannot wait for it "
					 + "or see it fail. Return 'async Task' or 'async Task<T>', or make the method non-public.");
				return false;
			}

			return true;
		}

		private void CheckInlineClassMemberReceiver(MemberDeclarationSyntax m, string at)
		{
			if (!IsScriptVisible(m) || !m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)))
				return;

			if (m is MethodDeclarationSyntax me)
			{
				if (me.ParameterList.Parameters.FirstOrDefault() is ParameterSyntax first && CanHoldReceiver(first))
					return;

				Diag($"{at}#CSharp: public static method '{me.Identifier.Text}' in a class body must take the receiver as its first "
					 + "parameter, conventionally 'object @this' -- the object for an instance member, the class for a [Static] one. "
					 + "Drop 'static' to let C# bind the receiver instead, or make the method non-public if no script was meant to call it.");
			}
			else if (m is PropertyDeclarationSyntax pd)
			{
				Diag($"{at}#CSharp: public property '{pd.Identifier.Text}' in a class body cannot be static: a property has no parameter "
					 + $"to receive the class. Drop 'static' for an instance property, or write a class-static one as the accessor "
					 + $"method 'staticget_{pd.Identifier.Text}(object @this)'.");
			}
		}

		private void PrescanClassCSharp(ClassDecl cd, string moduleName, string moduleDir, string outerPath)
		{
			var path = outerPath == null ? NameMangler.ClassType(cd.Name) : outerPath + "." + NameMangler.ClassType(cd.Name);

			foreach (var d in cd.CSharpBlocks ?? [])
			{
				if (!(_csharpSeen ??= new(ReferenceEqualityComparer.Instance)).Add(d))
					continue;

				if (!ResolveInlineBlockSource(d, moduleDir))
					continue;

				(_inlineBlocks ??= []).Add((moduleName, path, d));
			}

			foreach (var nested in cd.Nested)
				PrescanClassCSharp(nested, moduleName, moduleDir, path);
		}

		private sealed record InlineEmission(string ClassPath, System.Text.StringBuilder Body);

		private IReadOnlyList<InlineCSharpSource> BuildInlineSources()
		{
			if (_inlineBlocks == null)
				return [];

			var sources = new List<InlineCSharpSource>();

			// Each script module gets one namespace declaration in one syntax tree. Its using directives apply to
			// every module/class block in that tree, but not to another module's declaration of the same namespace.
			foreach (var module in _inlineBlocks.Select(block => block.Module).Distinct(System.StringComparer.Ordinal))
			{
				var blocks = _inlineBlocks.Where(block => block.Module == module).ToList();
				var usings = new List<string>();
				var seenUsing = new HashSet<string>(System.StringComparer.Ordinal);

				// Keysharp.Builtins is opt-in because Array, String and Buffer conflict with System.
				foreach (var name in new[] { "System", "System.Collections.Generic", "System.Runtime.CompilerServices", "System.Runtime.InteropServices", "Keysharp.Runtime" })
				{
					var directive = "using " + name + ";";

					if (seenUsing.Add(directive))
						usings.Add(directive);
				}

				foreach (var (_, _, directive) in blocks)
					foreach (var use in Parsed(directive).Usings)
						if (seenUsing.Add(use.Text))
							usings.Add(use.Text);

				var emissions = new List<InlineEmission>();

				// Preserve block order so stateful C# directives such as #nullable and #pragma reach later
				// blocks in this module and naturally stop at the module tree boundary.
				foreach (var (_, classPath, d) in blocks)
				{
					var parsed = Parsed(d);
					var body = new System.Text.StringBuilder();
					var rawFile = d.CodeFile ?? _scriptPath ?? "*";
					var lineOffset = d.CodeLine - 1;
					var wrapperOffset = parsed.WrapperOffset;

					if (!ReportInlineSyntaxErrors(parsed, lineOffset, rawFile))
						continue;

					foreach (var m in parsed.Members)
					{
						var isFunction = classPath == null && m is MethodDeclarationSyntax me && IsScriptVisible(me)
									   && me.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword));
						// A_ThisFunc has no emission site to fold in inline C#, so it falls back to a stack walk --
						// which finds nothing if the JIT inlined the very member it is meant to name. Methods only:
						// MethodImpl is legal on nothing else, and a property or class would fail to compile.
						var needsThisFuncFrame = m is MethodDeclarationSyntax && ReferencesThisFunc(m);
						var startLine = m.GetLocation().GetLineSpan().StartLinePosition.Line - wrapperOffset;
						var at = $"{System.IO.Path.GetFileName(rawFile)}:{lineOffset + startLine + 1}:1: ";

						if (classPath == null)
						{
							CheckInlineNameCollision(m, module, isFunction, at);

							if (!isFunction && m is MethodDeclarationSyntax im && IsScriptVisible(im)
									&& !im.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)))
								Diag($"{at}#CSharp: public method '{im.Identifier.Text}' at module scope must be 'static' to be callable "
									 + "from a script -- the module class is never instantiated, so an instance method can never run. "
									 + "Add 'static', or make it non-public if no script was meant to call it.");

							if (!isFunction && InlineExportAttribute(m) != null)
								Diag($"{at}#CSharp: [Export] is supported only on public static module methods");
						}
						else
						{
							if (InlineExportAttribute(m) != null)
								Diag($"{at}#CSharp: [Export] is valid only at module scope");

							CheckInlineClassMemberReceiver(m, at);

							if (m is MethodDeclarationSyntax cm && IsScriptVisible(cm))
								_ = CheckInlineBoundarySignature(cm, at);

							foreach (var cn in DeclaredNames(m))
								if (Keywords.InlineReservedClassNames.Contains(cn))
									Diag($"{at}#CSharp: '{cn}' is a name Keysharp generates into this class; rename it");
						}

						if (m is PropertyDeclarationSyntax pv && IsScriptVisible(pv) && IsUnsupportedBoundaryReturn(pv.Type))
							Diag($"{at}#CSharp: public property '{pv.Identifier.Text}' has a type that cannot be handed to a script. "
								 + "Use a supported scalar/reference type or make it non-public.");

						// Mark script-visible inline members for boundary policy and function-name recovery. A module-scope
						// method is marked too, where only a class-scope one used to be: it now takes the same boundary
						// policy as its FN_ forwarder (conversion, error mapping, and whether a trailing object[] reads
						// as variadic), so which of the two a lookup resolves no longer changes behaviour. Module fields
						// are marked as well so they can be distinguished from generated script globals.
						if (IsScriptVisible(m) && (m is PropertyDeclarationSyntax
								|| m is MethodDeclarationSyntax
								|| classPath == null && m is FieldDeclarationSyntax))
							_ = body.AppendLine("[Keysharp.Runtime.InlineCSharp]");
						if (needsThisFuncFrame)
							_ = body.AppendLine("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]");

						_ = body.AppendLine(FreezeConditionals(m).ToString());

						// Supply the FN_ method expected by the generated function binding.
						if (isFunction && m is MethodDeclarationSyntax em)
						{
							if (!CheckInlineBoundarySignature(em, at))
								continue;

							var argNames = string.Join(", ", em.ParameterList.Parameters.Select(p => p.Identifier.Text));
							var isVoid = em.ReturnType is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);
							var call = $"{em.Identifier.Text}({argNames})";
							var fwdBody = isVoid ? $"{call};" : $"return {call};";
							_ = body.AppendLine("[Keysharp.Runtime.PublicHiddenFromUser]");
							_ = body.AppendLine("[Keysharp.Runtime.InlineCSharp]");
							_ = body.AppendLine($"[Keysharp.Runtime.UserDeclaredName(\"{em.Identifier.ValueText}\")]");
							if (needsThisFuncFrame)
								_ = body.AppendLine("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]");
							_ = body.AppendLine(
									$"public static {em.ReturnType} {NameMangler.FunctionMethod(em.Identifier.Text)}"
									+ $"{em.ParameterList} {{ {fwdBody} }}");
						}
					}

					emissions.Add(new InlineEmission(classPath, body));
				}

				var sb = new System.Text.StringBuilder();
				_ = sb.AppendLine("namespace Keysharp.CompiledMain");
				_ = sb.AppendLine("{");

				foreach (var directive in usings)
					_ = sb.AppendLine(directive);

				foreach (var emission in emissions)
				{
					_ = sb.AppendLine("public partial class Program");
					_ = sb.AppendLine("{");
					_ = sb.AppendLine($"public partial class {NameMangler.ModuleClass(module)}");
					_ = sb.AppendLine("{");
					var parts = emission.ClassPath?.Split('.') ?? [];

					foreach (var part in parts)
					{
						_ = sb.AppendLine($"public partial class {part}");
						_ = sb.AppendLine("{");
					}

					_ = sb.Append(emission.Body);

					for (var i = parts.Length - 1; i >= 0; i--)
						_ = sb.AppendLine("}");

					_ = sb.AppendLine("}");
					_ = sb.AppendLine("}");
				}

				_ = sb.AppendLine("}");
				var text = sb.ToString();
				var unit = SyntaxFactory.ParseCompilationUnit(text, options: new CSharpParseOptions(
							   LanguageVersion.LatestMajor, DocumentationMode.None, SourceCodeKind.Regular, _inlineDefines));
				var code = unit.GetDiagnostics().Any(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
						   ? text
						   : PrettyPrinter.Print(unit);
				sources.Add(new InlineCSharpSource(module, code));
			}

			return sources;
		}

		private void TrackRuntimeComponent(System.Reflection.MethodInfo method)
		{
			if (method?.DeclaringType == typeof(Keysharp.Builtins.Ks)
					&& method.Name is nameof(Keysharp.Builtins.Ks.RunScript) or nameof(Keysharp.Builtins.Ks.ParseScript))
				_ = _requiredComponents.Add(ScriptingComponentIds.Compiler);
		}

		// Gathers the program's `#Package [*i] <id> [version]` directives into one package set (see the _packages field
		// for why they cannot be resolved one at a time) and validates each. Validation belongs here rather than at
		// runtime so a malformed provider/id/version is a compile error before provider resolution begins.
		private void PrescanPackageDirectives(List<Stmt> body)
		{
			foreach (var s in body)
			{
				if (s is not DirectiveStmt d || !d.Name.Equals("Package", System.StringComparison.OrdinalIgnoreCase))
					continue;

				var args = (d.Args ?? "").Trim();

				if (!_packagesSeen.Add(d))   // the multi-module path rescans __Main, whose body the first scan already covered
					continue;

				var parts = args.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
				// `*i` marks the package optional: an unavailable one is skipped instead of stopping the script.
				var optional = parts.Length != 0 && parts[0].Equals("*i", System.StringComparison.OrdinalIgnoreCase);

				if (optional)
					parts = parts.Skip(1).ToArray();

				if (parts.Length == 0)
				{
					Diag("#Package: expected a package name (e.g. `#Package Newtonsoft.Json 13.0.3`)");
					continue;
				}

				var qualifiedId = parts[0].Trim('"', '\'');
				var provider = "nuget";
				var colon = qualifiedId.IndexOf(':');

				if (colon >= 0)
				{
					provider = qualifiedId[..colon];
					qualifiedId = qualifiedId[(colon + 1)..];
				}

				if (!Keysharp.Internals.Os.PackageProviderRegistry.IsValidProviderName(provider))
				{
					Diag($"#Package: '{provider}' is not a valid package provider name");
					continue;
				}

				var id = qualifiedId;
				// Everything after the name is the version expression. It is joined rather than limited to one token
				// because a bounded form takes two (`>=13.0 <14`), exactly as `#Requires` writes them.
				var version = string.Join(" ", parts.Skip(1)).Trim('"', '\'');

				string range;

				if (!Keysharp.Internals.Os.PackageProviderRegistry.TryGet(provider, out var packageProvider, out var providerError))
				{
					// An optional package may name an optional provider installation. Its grammar cannot be
					// normalized while that provider is absent, but the compile-time resolver will drop the
					// whole optional request and emit the ordinary optional-package warning.
					if (optional)
						range = version;
					else
					{
						Diag($"#Package: {providerError}");
						continue;
					}
				}
				else if (!packageProvider.IsValidPackageId(id))
				{
					Diag($"#Package: '{id}' is not a valid package name for provider '{provider}'");
					continue;
				}
				// Translated at compile time so a bad version is a compile error, and so the runtime cache key is
				// canonical (two spellings of the same range share one resolved set).
				else if (!packageProvider.TryNormalizeVersion(version, out range, out var verr))
				{
					Diag($"#Package: {verr} for package '{id}'");
					continue;
				}

				version = range;

				// The same package twice is harmless if identical; conflicting versions are not, since the resolver
				// would have to unify them and the script author almost certainly did not mean it.
				var dup = _packages.FindIndex(p => p.Provider.Equals(provider, System.StringComparison.OrdinalIgnoreCase)
												  && p.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase));

				if (dup >= 0)
				{
					if (!_packages[dup].Version.Equals(version, System.StringComparison.OrdinalIgnoreCase))
						Diag($"#Package: package '{id}' is requested twice with different versions ('{_packages[dup].Version}' and '{version}')");
					else if (!optional)
						_packages[dup] = _packages[dup] with { Optional = false };   // one required mention makes it required

					continue;
				}

				_packages.Add(new Keysharp.Internals.Os.PackageResolver.PackageRef(id, version, optional, provider));
			}
		}

		/// <summary>
		/// Lowers a `#Package` directive. The prescan gathered and validated the program's whole package set and
		/// BuildOuterAuto emits the single call for it, so a directive in its own right emits nothing. All this does
		/// is report one the prescan never visited: such a directive sits below the top level of a script or module,
		/// and a package declaration buried in a function body would otherwise vanish.
		/// </summary>
		private StatementSyntax LowerPackageDirective(DirectiveStmt d)
		{
			if (!_packagesSeen.Contains(d))
				Diag($"{DirectiveAnchor(d)}#Package must appear at the top level of a script or module, not inside a function, class or block");

			return null;
		}

		/// <summary>
		/// Every `#Package` the program declared, for the compiler to resolve at BUILD time (see
		/// CompilerHelper.ResolvePackages). Exposed rather than emitted, because what the script wrote is a
		/// constraint and what it resolves to is the answer — and the answer belongs in the built artifact.
		/// </summary>
		public IReadOnlyList<Keysharp.Internals.Os.PackageResolver.PackageRef> Packages => _packages;

		/// <summary>
		/// The one call that loads every `#Package` the program declared, or null when it declared none (or when all
		/// of them were malformed and already reported).
		/// <para>It takes NO arguments. The packages were resolved at compile time and the exact resolution is
		/// embedded in this assembly, so passing the requested ids and versions here would only invite the runtime to
		/// re-decide — which is how a floating version came to mean one thing on the build machine and another on the
		/// machine that runs the script.</para>
		/// </summary>
		private StatementSyntax EmitPackageLoad() =>
			_packages.Count == 0 ? null : ExprStmt(Inv(Access("MainScript.LoadPackages")));

		// Scans the module body for `#Warn` directives up-front so the config is location-independent (per the docs).
		private void PrescanWarnDirectives(List<Stmt> body)
		{
			foreach (var s in body)
				if (s is DirectiveStmt d && d.Name.Equals("Warn", System.StringComparison.OrdinalIgnoreCase))
					ApplyWarnDirective(d.Args);
		}

		// A user-authored #Error/#Warning message, positioned like every other diagnostic ("[file:]line:col: text") so
		// it points at the directive rather than the top of the main script. Positioning is also what makes it SAFE:
		// the reader that splits a diagnostic apart takes the leading line:col, so an unpositioned message that itself
		// began "10:30:" would be reported as coming from line 10 of the script.
		private static string DirectiveMessage(DirectiveStmt d, string args, string whenEmpty) =>
			DirectiveAnchor(d) + (string.IsNullOrWhiteSpace(args) ? whenEmpty : args.Trim());

		/// <summary>The "[file:]line:col: " prefix every directive diagnostic carries, so a script with several
		/// blocks or includes learns WHICH one is being complained about.</summary>
		private static string DirectiveAnchor(DirectiveStmt d) =>
			$"{(d.File != null ? System.IO.Path.GetFileName(d.File) + ":" : "")}{d.Line}:{d.Column}: ";

		private void Warn(string mode, int line, string desc, string file = null) { if (mode != null) _warnings.Add((mode, line, desc, file)); }

		// Runs the enabled warning checks over a module body (the top-level scope) and all nested function/method/
		// property/lambda scopes. Must run AFTER the user-func/class prescan (so names resolve) and BEFORE lowering.
		private void AnalyzeWarnings(List<Stmt> body)
		{
			if (_warnVarUnset == null && _warnUnreachable == null && _warnLocalSameAsGlobal == null) return;
			_warnGlobalReadable = null;
			var globals = _warnLocalSameAsGlobal != null ? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) : null;
			if (globals != null) { var acc = new List<string>(); var seen = new HashSet<string>(); foreach (var s in body) CollectAssignedStmt(s, acc, seen); globals.UnionWith(acc); }
			AnalyzeScope(body, null, System.Array.Empty<string>(), globals, topLevel: true);
		}

		// Analyzes one scope (a statement list and/or an arrow expression) plus recurses into the scopes nested in it.
		// `outerProvided` is the union of names provided by all ENCLOSING function/lambda scopes (params, locals, nested
		// functions): a nested function or fat-arrow closes over them, so reading one is not "unset" (null at top level).
		private void AnalyzeScope(IReadOnlyList<Stmt> body, Expr arrow, System.Collections.Generic.IEnumerable<string> paramNames, HashSet<string> globals, bool topLevel, HashSet<string> outerProvided = null)
		{
			if (_warnUnreachable != null && body != null) CheckUnreachable(body);
			// Names visible to scopes NESTED in this one (this scope's provided plus everything inherited); defaults to
			// what we inherited so Unreachable-only mode still threads the closure set through untouched.
			var visible = outerProvided;
			// VarUnset / LocalSameAsGlobal both need the set of names "provided" in this scope.
			if (_warnVarUnset != null || (_warnLocalSameAsGlobal != null && !topLevel))
			{
				_warnScopeIsGlobal = topLevel;
				// A bare `global` switches a function/method to assume-global: every bare name is a GLOBAL, which may be
				// assigned in another scope we can't see here. We can't locally prove such a global is never assigned, so
				// skip both VarUnset and LocalSameAsGlobal for this scope's own reads/locals (params still resolve normally).
				bool assumeGlobal = !topLevel && body != null
					&& body.Any(st => st is DeclStmt dg && dg.Keyword == "global" && dg.Items.Count == 0);
				var provided = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
				foreach (var p in paramNames) provided.Add(p.ToLowerInvariant());
				var declaredGlobal = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
				if (body != null) foreach (var s in body) CollectProvided(s, provided, declaredGlobal);
				if (arrow != null) CollectProvidedExpr(arrow, provided, declaredGlobal);
				// The top-level scope's names are the module globals; capture them so every nested scope can read them
				// (Keysharp resolves any non-local bare name to a global field, so such a read is never "unset").
				if (topLevel) _warnGlobalReadable = provided;
				// Names readable here = this scope's provided + everything from enclosing scopes (closure capture) + the
				// module globals (always reachable, even from class methods/properties that don't inherit outer locals).
				var readable = provided;
				// This scope's own `#import` names (control-flow-nested and direct) are readable here — a reference to an
				// imported function/type/variable is not "never assigned". Class-body imports arrive via outerProvided.
				var ownImports = new List<ImportDirective>();
				if (body != null) foreach (var s in body) { if (s is ImportDirective id) ownImports.Add(id); else CollectNestedImports(s, ownImports); }
				if (outerProvided != null || (!topLevel && _warnGlobalReadable != null) || ownImports.Count > 0)
				{
					readable = new HashSet<string>(provided, System.StringComparer.OrdinalIgnoreCase);
					if (outerProvided != null) readable.UnionWith(outerProvided);
					if (!topLevel && _warnGlobalReadable != null) readable.UnionWith(_warnGlobalReadable);
					if (ownImports.Count > 0) AddImportProvidedNames(ownImports, readable);
				}
				if (_warnVarUnset != null && !assumeGlobal)
				{
					var warned = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
					if (body != null) foreach (var s in body) CheckReadsStmt(s, readable, warned);
					if (arrow != null) CheckReadsExpr(arrow, readable, warned);
				}
				// LocalSameAsGlobal compares only THIS scope's own locals (not inherited ones) against the globals.
				if (_warnLocalSameAsGlobal != null && !topLevel && !assumeGlobal && globals != null)
					foreach (var n in provided) if (!declaredGlobal.Contains(n) && globals.Contains(n)) Warn(_warnLocalSameAsGlobal, 0, $"This local variable has the same name as a global variable: {n}.");
				visible = readable;
			}
			// Recurse into nested scopes (their bodies are separate scopes), passing this scope's visible names down so
			// closures over our locals aren't flagged.
			if (body != null) foreach (var s in body) RecurseScopes(s, globals, visible);
			if (arrow != null) RecurseScopesExpr(arrow, globals, visible);
		}

		private static bool IsFlowTerminator(Stmt s) => s is ReturnStmt or BreakStmt or ContinueStmt or ThrowStmt or GotoStmt;

		// Definitions (function/class/hotkey/hotstring/remap) are hoisted or registered at load regardless of textual
		// position, and a directive is processed at compile time — none execute in sequence, so they are never
		// "unreachable" and do not break an unreachable run (e.g. a nested helper `func` defined AFTER a `return`).
		private static bool IsHoistedOrDirective(Stmt s) =>
			s is FunctionDecl or ClassDecl or HotkeyDef or HotstringDef or RemapDef or DirectiveStmt;

		// Unreachable: an executable statement following a Return/Break/Continue/Throw/Goto at the same level. Hoisted
		// definitions/directives are skipped (never unreachable); a label is a goto target, so it makes flow reachable
		// again. Only the first statement of each unreachable run is reported (matching AHK).
		private void CheckUnreachable(IReadOnlyList<Stmt> list)
		{
			bool unreachable = false;
			foreach (var s in list)
			{
				if (IsHoistedOrDirective(s)) continue;
				if (s is LabelStmt) { unreachable = false; continue; }
				if (unreachable) { var at = StmtAnchor(s); Warn(_warnUnreachable, at.Line, "This line will never be executed.", at.File); unreachable = false; }
				if (IsFlowTerminator(s)) unreachable = true;
			}
		}

		// Collects names "provided" (so a read of them is not unset) in THIS scope only (not descending into nested
		// function/lambda bodies): params, direct `:=` targets, `&var`, `IsSet(var)`, for/catch vars, and declarations.
		private void CollectProvided(Stmt s, HashSet<string> provided, HashSet<string> declaredGlobal)
		{
			switch (s)
			{
				// A nested function/class declaration binds its name as a (read-only) local throughout the enclosing
				// scope (AHK hoists nested functions), so a reference to it is not "unset". Only TOP-LEVEL functions are
				// in _userFuncByLower, so nested ones must be recorded here. The body is a separate scope (analyzed by
				// RecurseScopes) — do not descend into it.
				case FunctionDecl fd: provided.Add(fd.Name.ToLowerInvariant()); break;
				case ClassDecl cd: provided.Add(cd.Name.ToLowerInvariant()); break;
				case DeclStmt d:
					foreach (var item in d.Items) { var nm = DeclItemName(item); if (nm != null) { provided.Add(nm.ToLowerInvariant()); if (d.Keyword == "global") declaredGlobal.Add(nm.ToLowerInvariant()); } CollectProvidedExpr(item, provided, declaredGlobal); }
					break;
				case ExpressionStmt es: CollectProvidedExpr(es.Expr, provided, declaredGlobal); break;
				case ReturnStmt r: if (r.Value != null) CollectProvidedExpr(r.Value, provided, declaredGlobal); break;
				case ThrowStmt t: if (t.Value != null) CollectProvidedExpr(t.Value, provided, declaredGlobal); break;
				case IfStmt iff: CollectProvidedExpr(iff.Cond, provided, declaredGlobal); CollectProvided(iff.Then, provided, declaredGlobal); if (iff.Else != null) CollectProvided(iff.Else, provided, declaredGlobal); break;
				case Block b: foreach (var x in b.Body) CollectProvided(x, provided, declaredGlobal); break;
				case WhileStmt w: CollectProvidedExpr(w.Cond, provided, declaredGlobal); CollectProvided(w.Body, provided, declaredGlobal); break;
				case LoopStmt lp: if (lp.Count != null) CollectProvidedExpr(lp.Count, provided, declaredGlobal); CollectProvided(lp.Body, provided, declaredGlobal); break;
				case SpecialLoopStmt slp: if (slp.Args != null) foreach (var a in slp.Args) if (a != null) CollectProvidedExpr(a, provided, declaredGlobal); CollectProvided(slp.Body, provided, declaredGlobal); break;
				case ForStmt fr: foreach (var v in fr.Vars) if (v != null) provided.Add(v.ToLowerInvariant()); CollectProvidedExpr(fr.Enumerable, provided, declaredGlobal); CollectProvided(fr.Body, provided, declaredGlobal); break;
				case SwitchStmt sw:
					if (sw.Value != null) CollectProvidedExpr(sw.Value, provided, declaredGlobal);
					foreach (var c in sw.Cases) { foreach (var v in c.Values) CollectProvidedExpr(v, provided, declaredGlobal); foreach (var st in c.Body) CollectProvided(st, provided, declaredGlobal); }
					if (sw.Default != null) foreach (var st in sw.Default) CollectProvided(st, provided, declaredGlobal);
					break;
				case TryStmt tr:
					CollectProvided(tr.Body, provided, declaredGlobal);
					foreach (var cb in tr.Catches) { if (cb.Var != null) provided.Add(cb.Var.ToLowerInvariant()); CollectProvided(cb.Body, provided, declaredGlobal); }
					if (tr.Else != null) CollectProvided(tr.Else, provided, declaredGlobal);
					if (tr.Finally != null) CollectProvided(tr.Finally, provided, declaredGlobal);
					break;
			}
		}

		private void CollectProvidedExpr(Expr e, HashSet<string> provided, HashSet<string> declaredGlobal)
		{
			switch (e)
			{
				case AssignExpr a:
					// `:=` plainly assigns the target. `.=` (concat-assign) also counts: AHK treats an unset target as ""
					// for string concatenation, so `x .= 'a'` is a valid assignment to `x` and must not warn. Numeric
					// compound ops (`+=`, `-=`, `*=`, ...) require a value and DO warn on an unset target, so they are excluded.
					if ((a.Op == ":=" || a.Op == ".=") && a.Target is NameExpr tn) provided.Add(tn.Name.ToLowerInvariant());
					CollectProvidedExpr(a.Target, provided, declaredGlobal); CollectProvidedExpr(a.Value, provided, declaredGlobal);
					break;
				case UnaryExpr u:
					if (u.Op == "&" && u.Operand is NameExpr rn) provided.Add(rn.Name.ToLowerInvariant());      // &var
					CollectProvidedExpr(u.Operand, provided, declaredGlobal);
					break;
				case CallExpr c:
					if (c.Callee is NameExpr ne && ne.Name.Equals("IsSet", System.StringComparison.OrdinalIgnoreCase)
						&& c.Args.Count == 1 && c.Args[0].Value is NameExpr iv) provided.Add(iv.Name.ToLowerInvariant());   // IsSet(var)
					CollectProvidedExpr(c.Callee, provided, declaredGlobal); foreach (var ar in c.Args) if (ar.Value != null) CollectProvidedExpr(ar.Value, provided, declaredGlobal);
					break;
				case BinaryExpr b:
					// `x = unset` (and friends) tests x the same way IsSet(x) does, so it counts as a provide too.
					if (UnsetTestOperand(b) is NameExpr uv) provided.Add(uv.Name.ToLowerInvariant());
					CollectProvidedExpr(b.Left, provided, declaredGlobal); CollectProvidedExpr(b.Right, provided, declaredGlobal); break;
				case TernaryExpr t: CollectProvidedExpr(t.Cond, provided, declaredGlobal); CollectProvidedExpr(t.Then, provided, declaredGlobal); CollectProvidedExpr(t.Else, provided, declaredGlobal); break;
				case GroupExpr g: CollectProvidedExpr(g.Inner, provided, declaredGlobal); break;
				case SequenceExpr sq: foreach (var it in sq.Items) CollectProvidedExpr(it, provided, declaredGlobal); break;
				case MemberExpr m: CollectProvidedExpr(m.Target, provided, declaredGlobal); break;
				case DynMemberExpr dm: CollectProvidedExpr(dm.Target, provided, declaredGlobal); CollectProvidedExpr(dm.NameExpr, provided, declaredGlobal); break;
				case IndexExpr ix: CollectProvidedExpr(ix.Target, provided, declaredGlobal); foreach (var ar in ix.Args) if (ar.Value != null) CollectProvidedExpr(ar.Value, provided, declaredGlobal); break;
				case ObjectExpr oe: foreach (var en in oe.Entries) { CollectProvidedExpr(en.Key, provided, declaredGlobal); CollectProvidedExpr(en.Value, provided, declaredGlobal); } break;
				case ArrayExpr ar2: foreach (var el in ar2.Elements) if (el.Value != null) CollectProvidedExpr(el.Value, provided, declaredGlobal); break;
				case MapExpr mp: foreach (var (k, v) in mp.Entries) { CollectProvidedExpr(k, provided, declaredGlobal); CollectProvidedExpr(v, provided, declaredGlobal); } break;
				case DerefExpr dr: CollectProvidedExpr(dr.Name, provided, declaredGlobal); break;
			}
		}

		// Walks reads; warns at the first read of a name that is not provided/param/builtin/user-func/class/keyword.
		// Does NOT descend into nested function/lambda bodies (separate scopes) or into the OK positions themselves.
		private void CheckReadsStmt(Stmt s, HashSet<string> provided, HashSet<string> warned)
		{
			switch (s)
			{
				case DeclStmt d: foreach (var item in d.Items) CheckReadsExpr(item, provided, warned); break;
				case ExpressionStmt es: CheckReadsExpr(es.Expr, provided, warned); break;
				case ReturnStmt r: if (r.Value != null) CheckReadsExpr(r.Value, provided, warned); break;
				case ThrowStmt t: if (t.Value != null) CheckReadsExpr(t.Value, provided, warned); break;
				case IfStmt iff: CheckReadsExpr(iff.Cond, provided, warned); CheckReadsStmt(iff.Then, provided, warned); if (iff.Else != null) CheckReadsStmt(iff.Else, provided, warned); break;
				case Block b: foreach (var x in b.Body) CheckReadsStmt(x, provided, warned); break;
				case WhileStmt w: CheckReadsExpr(w.Cond, provided, warned); CheckReadsStmt(w.Body, provided, warned); break;
				case LoopStmt lp: if (lp.Count != null) CheckReadsExpr(lp.Count, provided, warned); CheckReadsStmt(lp.Body, provided, warned); break;
				case SpecialLoopStmt slp: if (slp.Args != null) foreach (var a in slp.Args) if (a != null) CheckReadsExpr(a, provided, warned); CheckReadsStmt(slp.Body, provided, warned); break;
				case ForStmt fr: CheckReadsExpr(fr.Enumerable, provided, warned); CheckReadsStmt(fr.Body, provided, warned); break;
				case SwitchStmt sw:
					if (sw.Value != null) CheckReadsExpr(sw.Value, provided, warned);
					foreach (var c in sw.Cases) { foreach (var v in c.Values) CheckReadsExpr(v, provided, warned); foreach (var st in c.Body) CheckReadsStmt(st, provided, warned); }
					if (sw.Default != null) foreach (var st in sw.Default) CheckReadsStmt(st, provided, warned);
					break;
				case TryStmt tr:
					CheckReadsStmt(tr.Body, provided, warned);
					foreach (var cb in tr.Catches) CheckReadsStmt(cb.Body, provided, warned);
					if (tr.Else != null) CheckReadsStmt(tr.Else, provided, warned);
					if (tr.Finally != null) CheckReadsStmt(tr.Finally, provided, warned);
					break;
			}
		}

		private void CheckReadsExpr(Expr e, HashSet<string> provided, HashSet<string> warned)
		{
			switch (e)
			{
				case NameExpr n:
					var lo = n.Name.ToLowerInvariant();
					if (!provided.Contains(lo) && !warned.Contains(lo) && IsUnsetCandidate(lo))
					{ warned.Add(lo); Warn(_warnVarUnset, n.Line, $"This {(_warnScopeIsGlobal ? "global" : "local")} variable appears to never be assigned a value: {n.Name}.", n.File); }
					break;
				case AssignExpr a:   // a direct `:=` target is NOT a read; a compound/member/index target IS evaluated.
					if (!(a.Op == ":=" && a.Target is NameExpr)) CheckReadsExpr(a.Target, provided, warned);
					CheckReadsExpr(a.Value, provided, warned);
					break;
				case UnaryExpr u:
					if (u.Op == "&") break;                            // &var is a provide, not a read
					if (u.Op == "?" && u.Operand is NameExpr) break;   // `var?` (maybe) is a guarded read — never warns
					CheckReadsExpr(u.Operand, provided, warned);
					break;
				case CallExpr c:
					var isIsSet = c.Callee is NameExpr cn && cn.Name.Equals("IsSet", System.StringComparison.OrdinalIgnoreCase) && c.Args.Count == 1 && c.Args[0].Value is NameExpr;
					CheckReadsExpr(c.Callee, provided, warned);
					if (!isIsSet) foreach (var ar in c.Args) if (ar.Value != null) CheckReadsExpr(ar.Value, provided, warned);
					break;
				case BinaryExpr b:
					// `x = unset` and friends test for unset rather than reading x, so they never warn (like IsSet()).
					if (UnsetTestOperand(b) != null) break;
					// `a ?? b`: the left operand is a maybe-unset (guarded) read — a bare unset variable there is
					// intentional (it falls back to the right), so AHK does not warn (like IsSet()). Still check the right
					// operand, and sub-reads of a non-bare left (e.g. `obj.p ?? x` still needs `obj` to be set).
					if (!(b.Op == "??" && b.Left is NameExpr)) CheckReadsExpr(b.Left, provided, warned);
					CheckReadsExpr(b.Right, provided, warned);
					break;
				case TernaryExpr t: CheckReadsExpr(t.Cond, provided, warned); CheckReadsExpr(t.Then, provided, warned); CheckReadsExpr(t.Else, provided, warned); break;
				case GroupExpr g: CheckReadsExpr(g.Inner, provided, warned); break;
				case SequenceExpr sq: foreach (var it in sq.Items) CheckReadsExpr(it, provided, warned); break;
				case MemberExpr m: CheckReadsExpr(m.Target, provided, warned); break;
				case DynMemberExpr dm: CheckReadsExpr(dm.Target, provided, warned); CheckReadsExpr(dm.NameExpr, provided, warned); break;
				case IndexExpr ix: CheckReadsExpr(ix.Target, provided, warned); foreach (var ar in ix.Args) if (ar.Value != null) CheckReadsExpr(ar.Value, provided, warned); break;
				case ObjectExpr oe: foreach (var en in oe.Entries) { if (en.Key is not NameExpr) CheckReadsExpr(en.Key, provided, warned); CheckReadsExpr(en.Value, provided, warned); } break;
				case ArrayExpr ar2: foreach (var el in ar2.Elements) if (el.Value != null) CheckReadsExpr(el.Value, provided, warned); break;
				case MapExpr mp: foreach (var (k, v) in mp.Entries) { CheckReadsExpr(k, provided, warned); CheckReadsExpr(v, provided, warned); } break;
				case DerefExpr dr: CheckReadsExpr(dr.Name, provided, warned); break;
				// FatArrowExpr: a separate scope, analyzed via RecurseScopes — do NOT check its reads here.
			}
		}

		// True when a bare name has no binding (so a read of it is statically unset): not a builtin var/func/type, a
		// user func/class, a value keyword, or a wildcard/inline alias.
		private bool IsUnsetCandidate(string lower)
		{
			if (lower is "this" or "true" or "false" or "unset" or "super") return false;
			if (lower.StartsWith("a_", System.StringComparison.Ordinal)) return false;   // built-in vars (A_*) — be conservative
			if (_userFuncByLower.ContainsKey(lower) || _userClassByLower.ContainsKey(lower)) return false;
			if (_inlineAliases.ContainsKey(lower)) return false;
			// A name already bound to a global field slot at analysis time is provided externally — most importantly an
			// imported name (`#import "Ks" { Cosh }`, `#import "X" { Calc as C }`, which RegisterImport/EmitImports add to
			// _fields before the warning pass). Regular global variable slots are created later (during lowering), so this
			// cannot mask a genuinely-unset global.
			if (_fields.ContainsKey(lower)) return false;
			// A `#import "Mod"` / `#import "Mod" { * }` wildcard (single-module path) resolves member names on demand.
			if (_wildcardModules.Count > 0 && _wildcardModules.Any(t => BindModuleMember(t, lower) != null)) return false;
			var rd = Script.TheScript.ReflectionsData;
			if (rd.flatPublicStaticProperties.ContainsKey(lower) || rd.flatPublicStaticMethods.ContainsKey(lower)) return false;
			if (rd.stringToTypes.TryGetValue(lower, out var builtinType) && IsGlobalAhkClass(builtinType)) return false;
			return true;
		}

		// Recurses into the scopes nested in a statement (function/method/property/lambda bodies are separate scopes).
		// `outerProvided` flows the enclosing scope's visible names into nested closures; same-scope blocks pass it
		// through unchanged. Class methods/properties do NOT close over enclosing function locals, so they reset it.
		private void RecurseScopes(Stmt s, HashSet<string> globals, HashSet<string> outerProvided = null)
		{
			switch (s)
			{
				case FunctionDecl fd: AnalyzeScope(fd.Body?.Body, fd.ArrowBody, fd.Params.Select(p => p.Name), globals, topLevel: false, outerProvided); break;
				case ClassDecl cd:
					// Class-body `#import` names are visible to every method/property/nested class of the class — pass them
					// down as the "outer provided" set (combined with any enclosing class's imports) so VarUnset doesn't
					// flag a class-imported name. Methods do NOT capture outer locals, so this carries only imports.
					var classProvided = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
					if (outerProvided != null) classProvided.UnionWith(outerProvided);
					AddImportProvidedNames(cd.Imports, classProvided);
					var classArg = classProvided.Count > 0 ? classProvided : null;
					foreach (var m in cd.Methods) AnalyzeScope(m.Body?.Body, m.ArrowBody, m.Params.Select(p => p.Name), globals, topLevel: false, classArg);
					foreach (var pr in cd.Properties)
					{
						if (pr.HasGet) AnalyzeScope(pr.GetBody?.Body, pr.GetArrow, pr.Params.Select(p => p.Name), globals, topLevel: false, classArg);
						if (pr.HasSet) AnalyzeScope(pr.SetBody?.Body, pr.SetArrow, pr.Params.Select(p => p.Name).Append("value"), globals, topLevel: false, classArg);
					}
					foreach (var nc in cd.Nested) RecurseScopes(nc, globals, classArg);
					break;
				case Block b: foreach (var x in b.Body) RecurseScopes(x, globals, outerProvided); break;
				case IfStmt iff: RecurseScopes(iff.Then, globals, outerProvided); if (iff.Else != null) RecurseScopes(iff.Else, globals, outerProvided); RecurseScopesExpr(iff.Cond, globals, outerProvided); break;
				case WhileStmt w: RecurseScopesExpr(w.Cond, globals, outerProvided); RecurseScopes(w.Body, globals, outerProvided); break;
				case LoopStmt lp: RecurseScopes(lp.Body, globals, outerProvided); break;
				case SpecialLoopStmt slp: RecurseScopes(slp.Body, globals, outerProvided); break;
				case ForStmt fr: RecurseScopesExpr(fr.Enumerable, globals, outerProvided); RecurseScopes(fr.Body, globals, outerProvided); break;
				case SwitchStmt sw:
					foreach (var c in sw.Cases) foreach (var st in c.Body) RecurseScopes(st, globals, outerProvided);
					if (sw.Default != null) foreach (var st in sw.Default) RecurseScopes(st, globals, outerProvided);
					break;
				case TryStmt tr:
					RecurseScopes(tr.Body, globals, outerProvided);
					foreach (var cb in tr.Catches) RecurseScopes(cb.Body, globals, outerProvided);
					if (tr.Else != null) RecurseScopes(tr.Else, globals, outerProvided);
					if (tr.Finally != null) RecurseScopes(tr.Finally, globals, outerProvided);
					break;
				case ExpressionStmt es: RecurseScopesExpr(es.Expr, globals, outerProvided); break;
				case ReturnStmt r: if (r.Value != null) RecurseScopesExpr(r.Value, globals, outerProvided); break;
				case DeclStmt d: foreach (var item in d.Items) RecurseScopesExpr(item, globals, outerProvided); break;
			}
		}

		private void RecurseScopesExpr(Expr e, HashSet<string> globals, HashSet<string> outerProvided = null)
		{
			switch (e)
			{
				case FatArrowExpr fa: AnalyzeScope(fa.BlockBody?.Body, fa.Body, fa.Params.Select(p => p.Name), globals, topLevel: false, outerProvided); break;
				case AssignExpr a: RecurseScopesExpr(a.Target, globals, outerProvided); RecurseScopesExpr(a.Value, globals, outerProvided); break;
				case BinaryExpr b: RecurseScopesExpr(b.Left, globals, outerProvided); RecurseScopesExpr(b.Right, globals, outerProvided); break;
				case UnaryExpr u: RecurseScopesExpr(u.Operand, globals, outerProvided); break;
				case TernaryExpr t: RecurseScopesExpr(t.Cond, globals, outerProvided); RecurseScopesExpr(t.Then, globals, outerProvided); RecurseScopesExpr(t.Else, globals, outerProvided); break;
				case GroupExpr g: RecurseScopesExpr(g.Inner, globals, outerProvided); break;
				case SequenceExpr sq: foreach (var it in sq.Items) RecurseScopesExpr(it, globals, outerProvided); break;
				case CallExpr c: RecurseScopesExpr(c.Callee, globals, outerProvided); foreach (var ar in c.Args) if (ar.Value != null) RecurseScopesExpr(ar.Value, globals, outerProvided); break;
				case MemberExpr m: RecurseScopesExpr(m.Target, globals, outerProvided); break;
				case DynMemberExpr dm: RecurseScopesExpr(dm.Target, globals, outerProvided); RecurseScopesExpr(dm.NameExpr, globals, outerProvided); break;
				case IndexExpr ix: RecurseScopesExpr(ix.Target, globals, outerProvided); foreach (var ar in ix.Args) if (ar.Value != null) RecurseScopesExpr(ar.Value, globals, outerProvided); break;
				case ObjectExpr oe: foreach (var en in oe.Entries) { RecurseScopesExpr(en.Key, globals, outerProvided); RecurseScopesExpr(en.Value, globals, outerProvided); } break;
				case ArrayExpr ar2: foreach (var el in ar2.Elements) if (el.Value != null) RecurseScopesExpr(el.Value, globals, outerProvided); break;
				case MapExpr mp: foreach (var (k, v) in mp.Entries) { RecurseScopesExpr(k, globals, outerProvided); RecurseScopesExpr(v, globals, outerProvided); } break;
			}
		}

		// The node a statement-level warning is positioned at — read BOTH its Line and its File, never one from here and
		// the other from elsewhere (a line number only means anything paired with the file it was counted in). The parser
		// stamps every statement with the line of its first token (see Parser.ParseStatement), so prefer that; fall back
		// to digging for a positioned expression in any unstamped node, and to the statement itself when there is none.
		private static Node StmtAnchor(Stmt s)
		{
			if (s.Line != 0) return s;
			var inner = s switch
			{
				ExpressionStmt es => es.Expr,
				ReturnStmt r => r.Value,
				ThrowStmt t => t.Value,
				IfStmt iff => iff.Cond,
				WhileStmt w => w.Cond,
				_ => null
			};
			return (inner != null ? ExprAnchor(inner) : null) ?? s;
		}
		// The first positioned node inside an expression, or null when nothing in it carries a line.
		private static Node ExprAnchor(Expr e) => e switch
		{
			NameExpr n => n.Line != 0 ? n : null,
			BinaryExpr b => ExprAnchor(b.Left) ?? ExprAnchor(b.Right),
			UnaryExpr u => ExprAnchor(u.Operand),
			AssignExpr a => ExprAnchor(a.Target) ?? ExprAnchor(a.Value),
			CallExpr c => ExprAnchor(c.Callee) ?? (c.Args.Count > 0 && c.Args[0].Value != null ? ExprAnchor(c.Args[0].Value) : null),
			MemberExpr m => ExprAnchor(m.Target),
			IndexExpr ix => ExprAnchor(ix.Target),
			GroupExpr g => ExprAnchor(g.Inner),
			_ => null
		};

		// Generates the load-time warning output (one call per warning, per its mode), prepended to the outer auto-exec.
		private List<StatementSyntax> EmitWarnings()
		{
			var stmts = new List<StatementSyntax>();
			foreach (var (mode, line, desc, nodeFile) in _warnings)
			{
				// Main-script nodes carry no file, or the main path itself; only a genuine #include needs naming.
				var file = string.IsNullOrEmpty(nodeFile) || string.Equals(nodeFile, _scriptPath, StringComparison.OrdinalIgnoreCase) ? null : nodeFile;
				// Every mode names the #included file a warning came from: its line number counts from that file's
				// first line, so on its own it points at an unrelated line of the main script.
				var where = line > 0 && file != null ? $" of {System.IO.Path.GetFileName(file)}" : "";
				stmts.Add(mode switch
				{
					// StdOut uses the editor-friendly "(line) : ==> Warning: …" format so editors can jump to the line
					// (prefixed with the file path, as AHK does, when the line is not in the main script).
					"StdOut" => CallStmt("Keysharp.Builtins.Files.FileAppend", Str((line > 0 ? $"{(file != null ? file + " " : "")}({line}) : ==> " : "") + "Warning: " + desc + "\n"), Str("*")),
					"OutputDebug" => CallStmt("Keysharp.Builtins.Debug.OutputDebug", Str("Warning: " + desc + (line > 0 ? $"\n\nLine: {line}{where}" : ""))),
					// MsgBox mode shows the standard continuable warning dialog (test-host-aware; see Errors.ShowWarning).
					_ => CallStmt("Keysharp.Builtins.Errors.ShowWarning", Str("Warning: " + desc + WarnLineContext(line, file, where))),
				});
			}
			return stmts;
		}

		// The source-line excerpt shown in a #Warn dialog: the offending line marked with ▶, then up to two following
		// non-blank lines for context, plus a pointer to the docs — mirroring AHK's #Warn warning dialog. `file` is the
		// #included file the line belongs to (null = the main script), and the excerpt MUST be read from it: quoting the
		// main script at an included file's line number shows text that has nothing to do with the warning.
		private string WarnLineContext(int line, string file, string where)
		{
			if (line <= 0)
				return "";

			var lines = file == null ? _sourceLines : IncludedSourceLines(file);

			if (lines == null || line > lines.Length)
				return $"\n\nLine: {line}{where}";

			var sb = new System.Text.StringBuilder("\n\n");

			if (file != null)
				sb.Append("In ").Append(System.IO.Path.GetFileName(file)).Append(":\n");

			for (int i = line, shown = 0; i <= lines.Length && shown < 3; i++)
			{
				var text = lines[i - 1].Trim();
				if (i != line && text.Length == 0)
					continue;   // skip blank context lines (matching AHK)
				sb.Append(i == line ? "▶\t" : "\t").Append(i).Append(": ").Append(text).Append('\n');
				shown++;
			}
			sb.Append("\nFor more details, read the documentation for #Warn.");
			return sb.ToString();
		}

		// Lines of an #included file, re-read (and cached) only when a warning actually needs to quote one. Null when
		// the file can no longer be read — the caller then falls back to a bare line number rather than misquoting.
		private string[] IncludedSourceLines(string file)
		{
			if (_includedSourceLines.TryGetValue(file, out var lines))
				return lines;

			try { lines = System.IO.File.ReadAllText(file).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'); }
			catch { lines = null; }

			_includedSourceLines[file] = lines;
			return lines;
		}

		private static BlockSyntax AsBlock(StatementSyntax s) =>
			s as BlockSyntax ?? SyntaxFactory.Block(s ?? SyntaxFactory.EmptyStatement());

		private BlockSyntax LowerBlock(IEnumerable<Stmt> stmts) =>
			SyntaxFactory.Block(LowerStmtList(stmts as System.Collections.Generic.IReadOnlyList<Stmt> ?? stmts.ToList()));

		// ---- expressions ----

		private ExpressionSyntax LowerExpr(Expr e)
		{
			switch (e)
			{
				case LiteralExpr l: return l.Kind == LiteralKind.Number ? Num(l.Raw) : Str(DecodeString(l.Raw));
				case NameExpr n:
					// AHK reserves true/false/unset as value keywords (booleans, and the no-value sentinel null).
					// The booleans lower to real C# booleans rather than 1/0: everything downstream reads one as the
					// Integer 1 or 0 anyway (MatchTypes folds it, Type() names it "Integer"), so nothing changes for
					// the script, but the value keeps the type Ks.Boolean names and Json.Encode can write true/false.
					switch (n.Name.ToLowerInvariant())
					{
						case "true": return BoolLit(true);
						case "false": return BoolLit(false);
						case "unset": return Null;
						case "super": return SuperTuple();
						// A_LineNumber folds to a compile-time literal (the source line). A_LineFile inside an #included
						// file is that file's path (a literal stamped on the node); for a main-script line it equals
						// A_ScriptFullPath and is emitted as the runtime accessor, so it reflects where the script actually
						// runs and never bakes the compile-time path into the assembly.
						case "a_linenumber" when !IsDeclaredLocal("a_linenumber"): return Num(n.Line.ToString());
						case "a_linefile" when !IsDeclaredLocal("a_linefile"):
							// A main-script line's file IS the running script, so fold to the A_ScriptFullPath accessor
							// (n.File unset, or stamped with the main path _scriptPath). This keeps A_LineFile ==
							// A_ScriptFullPath true whether the main script runs as source (.ks) or compiled (.cks/.exe,
							// whose runtime path differs from the baked source name). Only #included files bake a path.
							return string.IsNullOrEmpty(n.File) || string.Equals(n.File, _scriptPath, StringComparison.OrdinalIgnoreCase)
								   ? Access("Keysharp.Builtins.Accessors.A_ScriptFullPath")
								   : Str(IncludeLineFile(n.File));
						// AutoHotkey evaluates a parameter default in the CALLER's frame, so A_ThisFunc there names
						// the caller -- a name this lowering cannot know, since the default runs in the callee's
						// prologue. Fold it to "", AutoHotkey's own answer for a top-level call, rather than leave
						// it to a stack walk that reports whichever ancestor the JIT happened to leave standing.
						case "a_thisfunc" when !IsDeclaredLocal("a_thisfunc"):
							return Str(_loweringParamDefault ? "" : _currentThisFuncName ?? "");
					}
					return NameRef(n.Name);
				case GroupExpr g: return SyntaxFactory.ParenthesizedExpression(LowerExpr(g.Inner));
				// Only the last item is the sequence's value; the rest are evaluated for their side effects alone,
				// so they are lowered as statements — a discarded unset return must not raise.
				case SequenceExpr seq:
					return Op("MultiStatement", seq.Items.Select((item, i) =>
						i == seq.Items.Count - 1 ? LowerExpr(item) : LowerAllowingUnset(item)).ToArray());
				case AssignExpr a: return LowerAssign(a);
				case BinaryExpr b:
					// && / and and || / or short-circuit and yield an operand (not a bool), so they can't be plain calls.
					if (b.Op == "&&" || b.Op.Equals("and", System.StringComparison.OrdinalIgnoreCase)) return LowerShortCircuit(b, andOp: true);
					if (b.Op == "||" || b.Op.Equals("or", System.StringComparison.OrdinalIgnoreCase)) return LowerShortCircuit(b, andOp: false);
					// `a ?? b` (maybe): the left's unset must yield null (not throw) so the default applies — rewrite its
					// outer GetPropertyValue/GetIndex/Invoke to the *OrNull form (as for IsSet). Lowered to C#'s `??` so b is
					// only evaluated when a is unset (short-circuit) — `b ?? MsgBox()` must not call MsgBox() when b is set.
					if (b.Op == "??") return Coalesce(RewriteToOrNull(LowerExpr(b.Left)), LowerExpr(b.Right));
					// `X is unset` tests whether X is unset, so X itself must NOT raise when unset — make its
					// outer GetIndex/GetPropertyValue/Invoke lenient (`*OrNull`); `Is(null, null)` is then true.
					if (b.Op.Equals("is", System.StringComparison.OrdinalIgnoreCase)
						&& b.Right is NameExpr { Name: "unset" })
						return Op("Is", RewriteToOrNull(LowerExpr(b.Left)), Null);
					if (BinOps.TryGetValue(b.Op, out var m))
					{
						if (MayYieldUnset(b.Left) || MayYieldUnset(b.Right))
							return PropagateBinary(b, m);
						var le = LowerExpr(b.Left); var re = LowerExpr(b.Right);
						return TryFoldBinary(b.Op, le, re) ?? Op(m, le, re);   // constant-fold literal operands (1+2 -> 3L)
					}
					Diag($"operator not yet lowerable: '{b.Op}'"); return Str("");
				case UnaryExpr u: return LowerUnary(u);
				case TernaryExpr t:
					return MayYieldUnset(t.Cond)
						? NullCondWrap(t.Cond, cond => SyntaxFactory.ParenthesizedExpression(
							SyntaxFactory.ConditionalExpression(IfTest(cond), LowerExpr(t.Then), LowerExpr(t.Else))))
						: SyntaxFactory.ParenthesizedExpression(
							SyntaxFactory.ConditionalExpression(IfTest(LowerExpr(t.Cond)), LowerExpr(t.Then), LowerExpr(t.Else)));
				// `?.` chains are unset-lenient: the base is guarded non-null by NullCondWrap, and the access itself uses
				// the *OrNull form so an unset-VALUED member yields null (which short-circuits the next `?.`) rather than
				// raising (e.g. `optObj?.prop?.inner` when `prop` is unset).
				case MemberExpr mem:
					TrackLoadPackageMember(mem);
					return mem.NullConditional || MayYieldUnset(mem.Target)
						? NullCondWrap(mem.Target, tt => Op("GetPropertyValueOrNull", tt, Str(mem.Name)))
						: Op("GetPropertyValue", LowerExpr(mem.Target), Str(mem.Name));
				case DynMemberExpr dmem:
					return dmem.NullConditional || MayYieldUnset(dmem.Target)
						? NullCondWrap(dmem.Target, tt => Op("GetPropertyValueOrNull", tt, LowerExpr(dmem.NameExpr)))
						: Op("GetPropertyValue", LowerExpr(dmem.Target), LowerExpr(dmem.NameExpr));
				case IndexExpr ix:
					// `obj.member[args]` folds the index into the member access (`GetPropertyValue(obj, "member", args)`)
					// so the runtime routes the args to an index-property getter (`Prop[i] { get }`) — or indexes the
					// member's value when it is a plain field. A bare `name[args]` / `expr[args]` stays a GetIndex.
					// A spread index (`x[idx*]`, AHK's Alpha[Params*]) flattens into the params array like a call's
					// arguments do. `[]` takes no named arguments of its own (the parser rejects them); a container
					// a spread source carries simply rides through as a value, like any other element.
					List<ExpressionSyntax> IdxArgs() =>
						ix.Args.Any(ia => ia.Spread) ? new() { SpreadParams(ix.Args) } : LowerArgs(ix.Args);

					if (!ix.NullConditional && ix.Target is MemberExpr { NullConditional: false } im)
						return MayYieldUnset(im.Target)
							? NullCondWrap(im.Target, tt => Op("GetPropertyValue", new[] { tt, Str(im.Name) }.Concat(IdxArgs()).ToArray()))
							: Op("GetPropertyValue", new[] { LowerExpr(im.Target), Str(im.Name) }.Concat(IdxArgs()).ToArray());
					if (!ix.NullConditional && ix.Target is DynMemberExpr { NullConditional: false } idm)
						return MayYieldUnset(idm.Target)
							? NullCondWrap(idm.Target, tt => Op("GetPropertyValue", new[] { tt, LowerExpr(idm.NameExpr) }.Concat(IdxArgs()).ToArray()))
							: Op("GetPropertyValue", new[] { LowerExpr(idm.Target), LowerExpr(idm.NameExpr) }.Concat(IdxArgs()).ToArray());
					return ix.NullConditional || MayYieldUnset(ix.Target)
						? NullCondWrap(ix.Target, tt => Op("GetIndexOrNull", Cons(tt, IdxArgs())))
						: Op("GetIndex", Cons(LowerExpr(ix.Target), IdxArgs()));
				case ArrayExpr ar:
					var arArgs = ar.Elements.Any(el => el.Spread)
						? SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(SpreadParams(ar.Elements))))
						: SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(LowerArgs(ar.Elements).Select(Arg)));
					return SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Builtins.Array")).WithArgumentList(arArgs);
				case MapExpr mp:   // [k1: v1, k2: v2] -> new Map(k1, v1, k2, v2)
					var mapArgs = new List<ExpressionSyntax>();
					foreach (var (k, v) in mp.Entries) { mapArgs.Add(LowerExpr(k)); mapArgs.Add(LowerExpr(v)); }
					return SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Builtins.Map"))
						.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(mapArgs.Select(Arg))));
				case ObjectExpr o: return LowerObject(o);
				case FatArrowExpr fa: return LowerFatArrow(fa);
				case DerefExpr dr:   // %name% read -> DerefGet(name) (in a deref fn) or ModuleData.Vars[name] (global)
					return _inDerefFunc ? DerefGet(LowerExpr(dr.Name)) : VarsIndex(LowerExpr(dr.Name));
				case CallExpr c: return LowerCall(c, statement: false);
				default: Diag($"expression not yet lowerable: {e.GetType().Name}"); return Str("");
			}
		}

		private ExpressionSyntax LowerAssign(AssignExpr a)
		{
			if (a.Target is UnaryExpr amp && amp.Op == "&")   // &x := v  ->  assign, then pass a ref to x
				return MakeRefFor(new AssignExpr(a.Op, amp.Operand, a.Value));
			if (a.Target is DerefExpr dt)   // %name% := v -> SetVar(name,v) (in a deref fn) or SetObject(Vars,name,v) (global)
			{
				ExpressionSyntax DerefRead(ExpressionSyntax nm) => _inDerefFunc ? DerefGet(nm) : VarsIndex(nm);
				ExpressionSyntax DerefWrite(ExpressionSyntax nm, ExpressionSyntax v) => _inDerefFunc ? DerefSet(nm, v) : VarsWrite(nm, v);
				if (a.Op == ":=")
					return DerefWrite(LowerExpr(dt.Name), LowerExpr(a.Value));
				// Compound `%name% op= v`: capture the (possibly side-effecting) name once, then write op(read, v) back.
				var nt = NewTemp();
				var dval = CompoundValue(a.Op[..^1], DerefRead(Id(nt)), LowerExpr(a.Value));
				if (dval == null) { Diag($"compound assignment to a dereference ('{a.Op}') not yet lowerable"); return Str(""); }
				return Op("MultiStatement", Assign(Id(nt), LowerExpr(dt.Name)), DerefWrite(Id(nt), dval));
			}
			if (a.Target is MemberExpr me)
			{
				if (a.Op == ":=")   // target evaluated once — no temp needed
					return me.NullConditional || MayYieldUnset(me.Target)
						? NullCondWrap(me.Target, target => Op("SetPropertyValue", target, Str(me.Name), LowerExpr(a.Value)))
						: Op("SetPropertyValue", LowerExpr(me.Target), Str(me.Name), LowerExpr(a.Value));
				if (me.NullConditional || MayYieldUnset(me.Target))
					return NullCondWrap(me.Target, target =>
					{
						var value = CompoundValue(a.Op[..^1], Op("GetPropertyValue", target, Str(me.Name)), LowerExpr(a.Value));
						return value == null ? Str("") : Op("SetPropertyValue", target, Str(me.Name), value);
					});
				// Compound: capture the target once so a side-effecting target expression runs a single time.
				var t = NewTemp();
				var mval = CompoundValue(a.Op[..^1], Op("GetPropertyValue", Id(t), Str(me.Name)), LowerExpr(a.Value));
				if (mval == null) { Diag($"compound assignment to a member ('{a.Op}') not yet lowerable"); return Str(""); }
				return Op("MultiStatement", Assign(Id(t), LowerExpr(me.Target)),
					Op("SetPropertyValue", Id(t), Str(me.Name), mval));
			}
			if (a.Target is DynMemberExpr dme)   // obj.%x% := v / obj.%x% op= v — capture target and the computed name once
			{
				if (a.Op == ":=")
					return dme.NullConditional || MayYieldUnset(dme.Target)
						? NullCondWrap(dme.Target, target => Op("SetPropertyValue", target, LowerExpr(dme.NameExpr), LowerExpr(a.Value)))
						: Op("SetPropertyValue", LowerExpr(dme.Target), LowerExpr(dme.NameExpr), LowerExpr(a.Value));
				if (dme.NullConditional || MayYieldUnset(dme.Target))
					return NullCondWrap(dme.Target, target =>
					{
						var name = NewTemp();
						var value = CompoundValue(a.Op[..^1], Op("GetPropertyValue", target, Id(name)), LowerExpr(a.Value));
						return value == null ? Str("") : Op("MultiStatement",
							Assign(Id(name), LowerExpr(dme.NameExpr)),
							Op("SetPropertyValue", target, Id(name), value));
					});
				var dmt = NewTemp(); var dmn = NewTemp();
				var dmval = CompoundValue(a.Op[..^1], Op("GetPropertyValue", Id(dmt), Id(dmn)), LowerExpr(a.Value));
				if (dmval == null) { Diag($"compound assignment to a member ('{a.Op}') not yet lowerable"); return Str(""); }
				return Op("MultiStatement", Assign(Id(dmt), LowerExpr(dme.Target)), Assign(Id(dmn), LowerExpr(dme.NameExpr)),
					Op("SetPropertyValue", Id(dmt), Id(dmn), dmval));
			}
			if (a.Target is IndexExpr ie)
			{
				if (a.Op == ":=")
				{
					ExpressionSyntax SetIndex(ExpressionSyntax target)
					{
						// `x[idx*] := v`: the flattened index and the value share SetObject's params array, value last.
						if (ie.Args.Any(x => x.Spread))
							return Op("SetObject", target, SpreadParams(ie.Args, trailing: LowerExpr(a.Value)));

						var argv = new List<ExpressionSyntax> { target };
						argv.AddRange(LowerArgs(ie.Args));
						argv.Add(LowerExpr(a.Value));
						return Op("SetObject", argv.ToArray());
					}
					return ie.NullConditional || MayYieldUnset(ie.Target)
						? NullCondWrap(ie.Target, SetIndex)
						: SetIndex(LowerExpr(ie.Target));
				}
				// The compound forms capture each index into its own temp for the read-then-write; a spread would need
				// its flattened array captured instead. Rare enough that an honest diagnostic beats silently indexing
				// with the un-flattened array.
				if (ie.Args.Any(x => x.Spread))
				{ Diag($"compound assignment ('{a.Op}') with a spread index is not yet supported"); return Str(""); }

				if (ie.NullConditional || MayYieldUnset(ie.Target))
					return NullCondWrap(ie.Target, target =>
					{
						var lowered = LowerArgs(ie.Args);
						var temps = lowered.Select(_ => NewTemp()).ToList();
						var ops = new List<ExpressionSyntax>();
						for (var k = 0; k < temps.Count; ++k)
							ops.Add(Assign(Id(temps[k]), lowered[k]));
						var ids = temps.Select(name => (ExpressionSyntax)Id(name)).ToList();
						var newValue = CompoundValue(a.Op[..^1], Op("GetIndex", Cons(target, ids)), LowerExpr(a.Value));
						if (newValue == null)
							return Str("");
						var set = new List<ExpressionSyntax> { target };
						set.AddRange(ids);
						set.Add(newValue);
						ops.Add(Op("SetObject", set.ToArray()));
						return Op("MultiStatement", ops.ToArray());
					});
				// Compound: capture the target and each index once.
				var tt = NewTemp();
				var loweredArgs = LowerArgs(ie.Args);
				var argTemps = loweredArgs.Select(_ => NewTemp()).ToList();
				var ops = new List<ExpressionSyntax> { Assign(Id(tt), LowerExpr(ie.Target)) };
				for (int k = 0; k < argTemps.Count; k++) ops.Add(Assign(Id(argTemps[k]), loweredArgs[k]));
				List<ExpressionSyntax> IdxIds() => argTemps.Select(n => (ExpressionSyntax)Id(n)).ToList();
				var newVal = CompoundValue(a.Op[..^1], Op("GetIndex", Cons(Id(tt), IdxIds())), LowerExpr(a.Value));
				if (newVal == null) { Diag($"compound assignment to an index ('{a.Op}') not yet lowerable"); return Str(""); }
				var setArgs = new List<ExpressionSyntax> { Id(tt) };
				setArgs.AddRange(IdxIds());
				setArgs.Add(newVal);
				ops.Add(Op("SetObject", setArgs.ToArray()));
				return Op("MultiStatement", ops.ToArray());
			}
			if (a.Target is not NameExpr tn)
			{
				Diag($"unsupported assignment target at {a.Line}:{a.Column}: {a.Target.GetType().Name}" + (a.Target is BinaryExpr be ? $" op={be.Op}" : a.Target is UnaryExpr ue ? $" op={ue.Op}" : ""));
				return Str("");
			}
			// &param write: through the VarRef's __Value (rather than reassigning the local VarRef).
			if (_byRefParams != null && _byRefParams.Contains(tn.Name.ToLowerInvariant()))
			{
				var refId = Id(NameMangler.Escape(tn.Name.ToLowerInvariant()));
				var rv = a.Op == ":=" ? LowerExpr(a.Value)
					: CompoundValue(a.Op[..^1], Op("GetPropertyValue", refId, Str("__Value")), LowerExpr(a.Value));
				if (rv == null) { Diag($"compound assignment '{a.Op}' not yet lowerable"); return Str(""); }
				return Op("SetPropertyValue", refId, Str("__Value"), rv);
			}
			// A scoped `#import` binding is a valid assignment target only when it is a writable script VARIABLE export
			// (the write goes through to the source module's field). Assigning to an imported function/type/module
			// object/built-in member is a load-time error — otherwise it would silently reassign the importer's own
			// binding (or emit uncompilable C#). Locals/params/statics of the same name shadow the import (handled above).
			if (ResolvesToScopedImport(tn.Name.ToLowerInvariant(), out var impBind))
			{
				if (impBind.Write == null)
				{
					Diag($"{a.Line}:{a.Column}: #Import: cannot assign to imported name '{tn.Name}'");
					return Str("");
				}
				var wval = a.Op == ":=" ? LowerExpr(a.Value) : CompoundValue(a.Op[..^1], impBind.Read(), LowerExpr(a.Value));
				if (wval == null) { Diag($"compound assignment '{a.Op}' not yet lowerable"); return Str(""); }
				return Assign(impBind.Write(), wval);
			}
			var target = NameRef(tn.Name);

			ExpressionSyntax value;
			if (a.Op == ":=") value = LowerExpr(a.Value);
			else
			{
				value = CompoundValue(a.Op[..^1], target, LowerExpr(a.Value));
				if (value == null) { Diag($"compound assignment '{a.Op}' not yet lowerable"); return Str(""); }
			}
			return Assign(target, value);
		}

		private StatementSyntax LowerDecl(DeclStmt d)
		{
			var stmts = new List<StatementSyntax>();
			foreach (var item in d.Items)
			{
				var name = DeclItemName(item);
				if (name == null) { Diag($"unsupported declaration item: {item.GetType().Name}"); continue; }
				var lower = name.ToLowerInvariant();
				var keyword = _locals != null ? d.Keyword : "global";

				if (keyword == "static")
				{
					if (_statics == null || !_statics.TryGetValue(lower, out var field)) { Diag($"static '{name}' not registered"); continue; }
					var value = item is AssignExpr sa && sa.Op == ":=" ? sa.Value : null;
					// A static with no initializer stays unset (the backing field defaults to null) — only emit the
					// one-time InitStaticVariable when there's an initializer, so `static z` ⇒ `z is unset` holds.
					if (value != null)
					{
						var lowered = LowerExpr(value);
						// A constant initializer (e.g. `static a := 1 + 2` → 3L) has no side effects, so initialize the
						// backing field directly (`SL_… = 3L`) instead of a lazy InitStaticVariable. The field was added to
						// the current `_staticFieldSink` (same scope as this rewrite), so `fi` indexes into it.
						if (lowered is LiteralExpressionSyntax && _staticFieldDeclIdx.TryGetValue(field, out var fi))
							_staticFieldSink[fi] = ObjField(field, lowered);
						else
							stmts.Add(ExprStmt(InitStatic(field, lowered)));
					}
				}
				// A declaration item may also be an executable operation (`global x := 1`, `global x += 1`,
				// `global log .= "x"`, `global x++`): the name is declared and the operation runs.
				else if (item is AssignExpr a)
					stmts.Add(ExprStmt(LowerAssign(a)));
				else if (item is UnaryExpr)
					stmts.Add(LowerStmt(new ExpressionStmt(item)));
			}
			if (stmts.Count == 0) return null;
			return stmts.Count == 1 ? stmts[0] : SyntaxFactory.Block(stmts);
		}

		// The variable name a declaration item refers to (`x`, `x := v`, `x += v`, `x++`).
		private static string DeclItemName(Expr item) => item switch
		{
			NameExpr n => n.Name,
			AssignExpr a when a.Target is NameExpr tn => tn.Name,
			UnaryExpr u when (u.Op == "++" || u.Op == "--") && u.Operand is NameExpr un => un.Name,
			_ => null
		};


		// a && b -> (KS_temp = a, IfTest(KS_temp) ? b : KS_temp);  a || b -> (KS_temp = a, IfTest(KS_temp) ? KS_temp : b).
		// The temp captures the left operand once; the right is only evaluated when needed (true short-circuit),
		// and the result is the operand value (matching Script.BooleanAnd/Or) rather than a bool.
		private ExpressionSyntax LowerShortCircuit(BinaryExpr b, bool andOp)
		{
			var tmp = NewTemp();
			var assign = Assign(Id(tmp), LowerExpr(b.Left));
			var cond = IfTest(Id(tmp));
			ExpressionSyntax ternary = SyntaxFactory.ParenthesizedExpression(andOp
				? SyntaxFactory.ConditionalExpression(cond, LowerExpr(b.Right), Id(tmp))
				: SyntaxFactory.ConditionalExpression(cond, Id(tmp), LowerExpr(b.Right)));
			if (MayYieldUnset(b.Left))
				ternary = SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
					SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, Id(tmp), Null),
					Null, ternary));
			return Op("MultiStatement", assign, ternary);
		}

		private ExpressionSyntax LowerUnary(UnaryExpr u)
		{
			if (u.Op == "++" || u.Op == "--") return LowerIncDec(u);
			if (u.Postfix)
			{
				// Maybe / unset-permissive `expr?`: the inner call/member/index must not raise on an unset result, so
				// rewrite it to its *OrNull variant (`f()?` -> InvokeOrNull, `obj.p?` -> GetPropertyValueOrNull).
				if (u.Op == "?") return RewriteToOrNull(LowerExpr(u.Operand));
				Diag($"postfix '{u.Op}' not yet lowerable"); return Str("");
			}
			var propagate = MayYieldUnset(u.Operand);
			switch (u.Op)
			{
				case "!": case "not": return propagate ? PropagateUnary(u.Operand, value => Op("LogicalNot", value)) : Op("LogicalNot", LowerExpr(u.Operand));
				case "-": return propagate ? PropagateUnary(u.Operand, value => Op("Subtract", Num("0"), value)) : Op("Subtract", Num("0"), LowerExpr(u.Operand));
				case "+": return propagate ? PropagateUnary(u.Operand, value => value) : LowerExpr(u.Operand);
				case "&": return MakeRefFor(u.Operand);
				case "~": return propagate ? PropagateUnary(u.Operand, value => Op("BitwiseNot", value)) : Op("BitwiseNot", LowerExpr(u.Operand));
				default: Diag($"unary '{u.Op}' not yet lowerable"); return Str("");
			}
		}

		// ++/--. The write-back expression yields the NEW value. For the postfix form we capture the old value
		// into a temp first: `x++` -> MultiStatement(KS_temp = x, x = Add(x,1), KS_temp), so `y := x++` sees the
		// pre-increment value without re-deriving it (avoids a redundant op and any side effects).
		private ExpressionSyntax LowerIncDec(UnaryExpr u)
		{
			var op = u.Op == "++" ? "Add" : "Subtract";
			var setup = new List<ExpressionSyntax>();   // temp captures so a member/index target runs exactly once
			ExpressionSyntax read, write;
			switch (u.Operand)
			{
				// A by-ref param's value lives in its VarRef's __Value, so the write must go through SetPropertyValue
				// (the same as a compound assignment) — `GetPropertyValue(p,"__Value") = …` is not a C# lvalue.
				case NameExpr bn when _byRefParams != null && _byRefParams.Contains(bn.Name.ToLowerInvariant()):
					var brefId = Id(NameMangler.Escape(bn.Name.ToLowerInvariant()));
					read = Op("GetPropertyValue", brefId, Str("__Value"));
					write = Op("SetPropertyValue", brefId, Str("__Value"), Op(op, Op("GetPropertyValue", brefId, Str("__Value")), Num("1")));
					break;
				case NameExpr:
					read = LowerExpr(u.Operand);
					write = SyntaxFactory.ParenthesizedExpression(Assign(LowerExpr(u.Operand), Op(op, LowerExpr(u.Operand), Num("1"))));
					break;
				case MemberExpr me:
					var mt = NewTemp();
					setup.Add(Assign(Id(mt), LowerExpr(me.Target)));
					read = Op("GetPropertyValue", Id(mt), Str(me.Name));
					write = Op("SetPropertyValue", Id(mt), Str(me.Name), Op(op, Op("GetPropertyValue", Id(mt), Str(me.Name)), Num("1")));
					break;
				case IndexExpr ie:
					if (ie.Args.Any(x => x.Spread))
					{ Diag($"'{u.Op}' with a spread index is not yet supported"); return Str(""); }

					var it = NewTemp();
					setup.Add(Assign(Id(it), LowerExpr(ie.Target)));
					var idx = LowerArgs(ie.Args);
					var argTemps = idx.Select(_ => NewTemp()).ToList();
					for (int k = 0; k < argTemps.Count; k++) setup.Add(Assign(Id(argTemps[k]), idx[k]));
					List<ExpressionSyntax> IdxIds() => argTemps.Select(n => (ExpressionSyntax)Id(n)).ToList();
					read = Op("GetIndex", Cons(Id(it), IdxIds()));
					var setArgs = new List<ExpressionSyntax> { Id(it) };
					setArgs.AddRange(IdxIds());
					setArgs.Add(Op(op, Op("GetIndex", Cons(Id(it), IdxIds())), Num("1")));
					write = Op("SetObject", setArgs.ToArray());
					break;
				case DerefExpr dr:   // %n%++ : write back through the deref machinery
					read = LowerExpr(dr);
					var inc = Op(op, LowerExpr(dr), Num("1"));
					write = _inDerefFunc ? DerefSet(LowerExpr(dr.Name), inc) : VarsWrite(LowerExpr(dr.Name), inc);
					break;
				default:
					Diag($"'{u.Op}' on {u.Operand.GetType().Name} not yet lowerable"); return Str("");
			}
			if (u.Postfix)   // capture targets, read the OLD value into a temp, write, then yield the old value
			{
				var old = NewTemp();
				var seq = new List<ExpressionSyntax>(setup) { Assign(Id(old), read), write, Id(old) };
				return Op("MultiStatement", seq.ToArray());
			}
			if (setup.Count == 0) return write;   // prefix on a plain name: the write expression yields the new value
			var pre = new List<ExpressionSyntax>(setup) { write };
			return Op("MultiStatement", pre.ToArray());
		}

		// IsSet(expr) must not throw when expr is an unset property/index/call: rewrite the argument's
		// GetPropertyValue/GetIndex/Invoke to its *OrNull variant (which yields null instead of erroring),
		// then IsSet's plain null-check works. Mirrors the canonical RewriteIsSetArgumentList.
		private static readonly Dictionary<string, string> IsSetOrNull = new(System.StringComparer.Ordinal)
		{ { "GetPropertyValue", "GetPropertyValueOrNull" }, { "GetIndex", "GetIndexOrNull" }, { "Invoke", "InvokeOrNull" } };

		private static ExpressionSyntax RewriteToOrNull(ExpressionSyntax e)
		{
			if (e is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma
				&& IsSetOrNull.TryGetValue(ma.Name.Identifier.Text, out var orNull))
				return inv.WithExpression(ma.WithName(SyntaxFactory.IdentifierName(orNull)));
			return e;
		}

		// An expression whose unset result must not raise, either because the value is discarded (a statement, or a
		// non-final item of a sequence) or because it becomes this function's own no-value return (a tail call, which
		// propagates an implicit blank-unset return). Lowering a call the way a call-as-statement is lowered yields
		// null rather than raising. [v2.1-alpha.29+]
		private ExpressionSyntax LowerAllowingUnset(Expr e) =>
			e is CallExpr call ? LowerCall(call, statement: true) : LowerExpr(e);

		// #Warn NamedArg: check `f(Name: v)` against the callee's signature when the callee is a BARE NAME whose
		// signature is knowable here -- a function declared in this compilation, or a built-in. Dispatch is by value at
		// run time (a function name is an ordinary variable that could hold something else by the time the call runs),
		// so this is a warning rather than an error; the binder re-checks and throws. It still catches a misspelling at
		// build time across the whole built-in surface, which is where named arguments are most used.
		private void CheckNamedArgs(CallExpr c)
		{
			if (_warnNamedArg == null || c.Callee is not NameExpr ne) return;

			// Nothing to check unless the call actually names something. This runs for every bare-name call in the
			// program, and resolving the callee below forces the callee's parameter map to be built -- so the test
			// has to come before that work, not after it.
			var named = false;

			foreach (var a in c.Args)
				if (a.Name != null) { named = true; break; }

			// Only a name written literally is checkable. A call whose names are ALL dynamic (`f(%x%: 1)`) has
			// nothing to compare against a signature, so it must not force the callee's parameter map to be built.
			if (!named) return;

			var lower = ne.Name.ToLowerInvariant();

			// Shadowed by a local/param/closure: it isn't the declared function at all.
			if (IsDeclaredLocal(lower) || _scopeClosureNames?.Contains(lower) == true) return;

			// The names the callee accepts, in parameter order -- the ordering matters, because a name resolving to
			// a position the positional arguments already covered is the "supplied twice" case.
			List<string> names;

			// A VARIADIC callee absorbs any name it does not declare and hands it on (see NamedArgBinder.Bind), so
			// there is nothing to warn about: whatever it forwards to is not visible from here.
			if (_userFuncDeclByLower.TryGetValue(lower, out var fd))
			{
				if (fd.Params.Any(p => p.Variadic)) return;

				names = fd.Params.Select(p => p.Name).ToList();
			}
			// `MyClass(x: 1)` constructs, so the names are __New's. The declaration carries no receiver -- `this` is
			// prepended when the method is lowered -- so these line up with the call's arguments as written.
			else if (_userClassDeclByLower.TryGetValue(lower, out var cd)
					 && cd.Methods.FirstOrDefault(m => m.Name.Equals("__New", System.StringComparison.OrdinalIgnoreCase)) is { } ctor)
			{
				if (ctor.Params.Any(p => p.Variadic)) return;

				names = ctor.Params.Select(p => p.Name).ToList();
			}
			else if (Script.TheScript.ReflectionsData.flatPublicStaticMethods.TryGetValue(lower, out var mi))
			{
				// The SAME MethodPropertyHolder the runtime binder will use, so the two cannot disagree about which
				// names are bindable (the receiver and the variadic tail are excluded there, once). Any variadic
				// absorbs whatever it does not declare, so only a name that cannot be a declared parameter of a
				// NON-variadic callee is checkable here.
				var mph = Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(mi);

				if (mph.variadicParamIndex >= 0) return;

				// ParamScan, not ParamIndexByName: the map can hold TWO spellings for one parameter ([UserDeclaredName]
				// aliases), and a list index over it would no longer be a parameter position -- shifting the
				// supplied-twice check for every name after an alias. The scan has one canonical entry per slot.
				names = mph.ParamScan.Where(e => !e.Variadic).Select(e => e.Name).ToList();
			}
			// `Buffer(nosuch: 1)` constructs a BUILT-IN class: the names are its __New's, exactly as the
			// user-class branch above reads a declared __New. Checked last so a user declaration shadowing a
			// built-in name wins, mirroring runtime resolution order.
			else if (Script.TheScript.Vars.classTypesByName.TryGetValue(lower, out var bt)
					 && Keysharp.Internals.Invoke.MethodPropertyHolder.FindConstructor(bt) is { } ctorMi)
			{
				var mph = Keysharp.Internals.Invoke.MethodPropertyHolder.GetOrAdd(ctorMi);

				if (mph.variadicParamIndex >= 0) return;

				// ParamScan, not ParamIndexByName: the map can hold TWO spellings for one parameter ([UserDeclaredName]
				// aliases), and a list index over it would no longer be a parameter position -- shifting the
				// supplied-twice check for every name after an alias. The scan has one canonical entry per slot.
				names = mph.ParamScan.Where(e => !e.Variadic).Select(e => e.Name).ToList();
			}
			else return;

			var at = ExprAnchor(c);   // carries the file too, so an #included call site reports its own path
			// Positional arguments cover the leading parameters one for one -- but an OMITTED slot (`f(, x: 1)`)
			// supplied nothing, so a name is entitled to fill it; only a slot holding a real value collides. A
			// spread's length is unknowable here, so the collision half of the check is skipped when one appears.
			// `!IsNamed`, not `Name == null`: a dynamically named slot supplies a parameter by name, so counting it as
			// positional here would have it collide with a literal name that legitimately fills a later parameter.
			var positional = c.Args.Any(a => a.Spread) ? 0 : c.Args.Count(a => !a.IsNamed);

			foreach (var a in c.Args)
			{
				// Dynamic names carry no literal to match; the runtime binder is what checks those.
				if (a.Name == null)
					continue;

				var at1 = names.FindIndex(n => string.Equals(n, a.Name, System.StringComparison.OrdinalIgnoreCase));

				// The same two messages the runtime binder raises, from the same builders -- the warning and the
				// error a script hits for one mistake must not read differently.
				if (at1 < 0)
					Warn(_warnNamedArg, at?.Line ?? 0,
						 Keysharp.Internals.Invoke.NamedArgBinder.UnknownNameMessage(a.Name, ne.Name, names), at?.File);
				else if (at1 < positional && c.Args[at1].Value != null)
					Warn(_warnNamedArg, at?.Line ?? 0,
						 Keysharp.Internals.Invoke.NamedArgBinder.SuppliedTwiceMessage(a.Name, ne.Name), at?.File);
			}
		}

		private ExpressionSyntax LowerCall(CallExpr c, bool statement)
		{
			// Declarative #Package is resolved while compiling and needs no provider in the produced artifact.
			// A statically visible imperative call does: a literal provider prefix is precise, while computed package
			// names conservatively carry NuGet and let an unanticipated provider report its normal runtime error.
			if (c.Callee is MemberExpr member && IsLoadPackageMember(member))
				_ = _requiredProviders.Add(LoadPackageProvider(c));

			if (c.Args.Count != 0) CheckNamedArgs(c);

			// IsSet(member/index/call) must not throw on an unset target — rewrite the arg to its *OrNull form.
			bool isSet = c.Callee is NameExpr ne && ne.Name.Equals("IsSet", System.StringComparison.OrdinalIgnoreCase)
				&& c.Args.Count == 1 && c.Args[0].Value != null && !c.Args[0].Spread;

			// The call arguments (shared by both the normal and the null-conditional path).
			List<ExpressionSyntax> CallArgs() =>
				c.Args.Any(a => a.Spread) ? new() { SpreadParams(c.Args) }
				: isSet ? new() { RewriteToOrNull(LowerExpr(c.Args[0].Value)) }
				: LowerArgs(c.Args);

			// `obj?.M(args)` (null-conditional method) / `obj?.()` (null-conditional call): evaluate the target once;
			// if unset, short-circuit to null without evaluating the call (or its args). Matches the canonical chain.
			if (c.Callee is MemberExpr mc && (mc.NullConditional || MayYieldUnset(mc.Target)))
				return NullCondWrap(mc.Target, tt => Op("Invoke", Cons2(tt, Str(mc.Name), CallArgs())));
			if (c.Callee is DynMemberExpr dmc && (dmc.NullConditional || MayYieldUnset(dmc.Target)))
				return NullCondWrap(dmc.Target, tt => Op("Invoke", Cons2(tt, LowerExpr(dmc.NameExpr), CallArgs())));
			if (c.NullConditional || MayYieldUnset(c.Callee))
				return NullCondWrap(c.Callee, tt => Op("Invoke", Cons2(tt, Null, CallArgs())));

			// A member callee names the member to resolve; a bare callee is the call form `f(args)`, which the
			// runtime distinguishes by a null name (see Script.InvokeOrNull).
			List<ExpressionSyntax> args;
			if (c.Callee is MemberExpr m) args = new() { LowerExpr(m.Target), Str(m.Name) };
			else if (c.Callee is DynMemberExpr dm) args = new() { LowerExpr(dm.Target), LowerExpr(dm.NameExpr) };
			else args = new() { LowerExpr(c.Callee), Null };
			args.AddRange(CallArgs());
			return Op(statement ? "InvokeOrNull" : "Invoke", args.ToArray());
		}

		private void TrackLoadPackageMember(MemberExpr member)
		{
			if (IsLoadPackageMember(member))
				_ = _requiredProviders.Add("nuget");
		}

		private string LoadPackageProvider(CallExpr call)
		{
			if (call.Args.Count != 0 && call.Args[0].Value is LiteralExpr { Kind: LiteralKind.String } literal)
			{
				var id = DecodeString(literal.Raw);
				var colon = id.IndexOf(':');

				if (colon > 0)
				{
					var provider = id[..colon];

					if (Keysharp.Internals.Os.PackageProviderRegistry.IsValidProviderName(provider))
						return provider.ToLowerInvariant();
				}
			}

			return "nuget";
		}

		private static bool IsLoadPackageMember(MemberExpr callee)
		{
			if (!callee.Name.Equals("LoadPackage", System.StringComparison.OrdinalIgnoreCase))
				return false;

			var target = callee.Target;

			if (target is NameExpr direct)
				return direct.Name.Equals("Clr", System.StringComparison.OrdinalIgnoreCase);

			return target is MemberExpr { Name: var clr, Target: NameExpr root }
				   && clr.Equals("Clr", System.StringComparison.OrdinalIgnoreCase)
				   && root.Name.Equals("Ks", System.StringComparison.OrdinalIgnoreCase);
		}

		private ExpressionSyntax LowerObject(ObjectExpr o)
		{
			var args = new List<ExpressionSyntax> { Id(EnsureTypeField("Keysharp.Builtins.KeysharpObject", "object")), Str("Call") };
			foreach (var en in o.Entries) { args.Add(KeyToString(en.Key)); args.Add(LowerExpr(en.Value)); }
			return Op("Invoke", args.ToArray());
		}

		// An object-literal key. A bare identifier and a string literal are literal property names; everything else
		// (`%x%` deref, `(expr)`, numbers) is a DYNAMIC key — evaluated and forced to a string at runtime.
		private ExpressionSyntax KeyToString(Expr key) => key switch
		{
			NameExpr n => Str(n.Name),
			LiteralExpr l when l.Kind == LiteralKind.String => Str(DecodeString(l.Raw)),
			// `{ %x% : v }`: the key is the VALUE of x (a dynamic property name), not a variable deref of x's value.
			DerefExpr d => Op("ForceString", LowerExpr(d.Name)),
			_ => Op("ForceString", LowerExpr(key))
		};

		// Builds a collection expression for an argument list containing a spread.
		// Targets a params object[]: normal args are expression elements, `arr*` becomes a spread element.
		//
		// A spread re-emits whatever its source carries, named arguments included -- that is the whole forwarding
		// story, and it is why no shape here needs a repair pass or an order check. The parser keeps a spread from
		// following a named argument, so the container NamedArgs() builds still lands last.
		//
		// `trailing` appends one extra expression after everything (SetObject wants the assigned value after the
		// flattened index).
		private ExpressionSyntax SpreadParams(List<Argument> args, ExpressionSyntax trailing = null)
		{
			var elems = new List<CollectionElementSyntax>();
			var positional = SplitNamed(args, out var named);

			foreach (var a in positional)
				elems.Add(a.Spread
						  ? SyntaxFactory.SpreadElement(Op("FlattenValues", a.Value == null ? Null : LowerExpr(a.Value)))
						  : SyntaxFactory.ExpressionElement(a.Value == null ? Null : LowerExpr(a.Value)));

			if (named != null)
				elems.Add(SyntaxFactory.ExpressionElement(NamedArgsOf(named)));

			if (trailing != null)
				elems.Add(SyntaxFactory.ExpressionElement(trailing));

			return SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList(elems));
		}

		private List<ExpressionSyntax> LowerArgs(List<Argument> args)
		{
			var list = new List<ExpressionSyntax>();
			var positional = SplitNamed(args, out var named);

			foreach (var a in positional)
				list.Add(a.Value == null ? Null : LowerExpr(a.Value));   // omitted argument -> null (runtime treats as unset)

			if (named != null)
				list.Add(NamedArgsOf(named));

			// `f(unset)` lowers to a bare null, which a `params object[]` takes as the array itself -- no
			// arguments at all, where AutoHotkey passes one unset element. The cast forces the packing.
			// Keyed on the source keyword, since an omitted argument lowers to the same null and stays omitted.
			if (list.Count == 1 && named == null && positional.Count == 1
					&& positional[0].Value is NameExpr un && un.Name.Equals("unset", System.StringComparison.OrdinalIgnoreCase))
				list[0] = SyntaxFactory.CastExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), list[0]);

			return list;
		}

		// Splits an argument list at its first NAMED argument, which the parser has already kept trailing. Returns
		// the positional head; `named` is null when the call names nothing, which is every ordinary call.
		private static List<Argument> SplitNamed(List<Argument> args, out List<Argument> named)
		{
			for (var i = 0; i < args.Count; i++)
				if (args[i].IsNamed)
				{
					named = args.GetRange(i, args.Count - i);
					return args.GetRange(0, i);
				}

			named = null;
			return args;
		}

		// One `Script.NamedArgs("n1", v1, "n2", v2)` carrying every name of a call, which the callers above append
		// LAST. That is what lets the runtime find a call's names with a single test on the last element, and every
		// forwarding hop -- a spread, a variadic collection, Func.Bind -- pass them along without knowing they are
		// there. Lowered after the positional arguments so the call still evaluates left to right.
		private ExpressionSyntax NamedArgsOf(List<Argument> named)
		{
			var nameValues = new List<ExpressionSyntax>(named.Count * 2);

			foreach (var a in named)
			{
				// A dynamic name (`%x%: v`) is lowered exactly as the same text lowers as an object-literal key, so
				// the two forms cannot drift apart in what they compute or in how a non-string result is coerced.
				nameValues.Add(a.Name != null ? Str(a.Name) : KeyToString(a.NameExpr));
				nameValues.Add(LowerExpr(a.Value));
			}

			return Op("NamedArgs", nameValues.ToArray());
		}

		// ---- control flow ----

		// while cond { body } -> Push; try { while(IsTrueAndRunning(cond)) { Inc(); body [; if(until) break] } } finally { Pop }
		// (Push/Inc/Pop give the loop a working A_Index, matching AHK.)
		// IsTrueAndRunning rather than a bare IfTest, as every other loop form uses: it carries the per-iteration
		// message check, and without it a `while` whose body calls nothing never polls and never sees the exit.
		private StatementSyntax LowerWhile(WhileStmt w)
		{
			var id = ++_flowCounter;
			var frame = PushLoop(id);
			var body = AsBlock(LowerStmt(w.Body));
			body = body.WithStatements(body.Statements.Insert(0, CallStmt("Keysharp.Runtime.Loops.Inc")));
			body = WrapLoopBody(body, w.Until, frame);
			PopLoop();
			var loop = SyntaxFactory.WhileStatement(
						   Inv(Access("Keysharp.Runtime.Flow.IsTrueAndRunning"), LowerExpr(w.Cond)), body);
			var block = new List<StatementSyntax> {
				CallStmt("Keysharp.Runtime.Loops.Push", Access("Keysharp.Runtime.LoopType.Normal")),
				TryFinally(loop, LoopFinally(w.Else)) };
			if (frame.NeedsEnd) block.Add(EndLabel(id));
			return SyntaxFactory.Block(block);
		}

		private StatementSyntax LowerLoop(LoopStmt lp)
		{
			// Both finite and infinite loops use Loops.Loop(count) (count -1 == infinite); its enumerator advances
			// A_Index on MoveNext and IsTrueAndRunning runs the per-iteration message check.
			var id = ++_flowCounter;
			var ev = "KS_e" + id;
			var countExpr = lp.Count == null ? Num("-1") : LowerExpr(lp.Count);
			var enumInit = Inv(Member(Inv(Access("Keysharp.Runtime.Loops.Loop"), countExpr), "GetEnumerator"));
			var cond = Inv(Access("Keysharp.Runtime.Flow.IsTrueAndRunning"), Inv(Member(Id(ev), "MoveNext")));
			var frame = PushLoop(id);
			var body = WrapLoopBody(AsBlock(LowerStmt(lp.Body)), lp.Until, frame);
			PopLoop();
			var loop = SyntaxFactory.WhileStatement(cond, body);
			var block = new List<StatementSyntax> {
				CallStmt("Keysharp.Runtime.Loops.Push", Access("Keysharp.Runtime.LoopType.Normal")),
				LocalDecl(Ty("System.Collections.IEnumerator"), ev, enumInit),
				TryFinally(loop, LoopFinally(lp.Else)) };
			if (frame.NeedsEnd) block.Add(EndLabel(id));
			return SyntaxFactory.Block(block);
		}

		// Maps a specialized-loop sub-keyword to its (enumerator method, LoopType) pair.
		private static readonly Dictionary<string, (string Method, string LoopType)> SpecialLoops = new(System.StringComparer.Ordinal)
		{
			{ "parse", ("LoopParse", "Parse") },
			{ "files", ("LoopFile", "Directory") },
			{ "read",  ("LoopRead", "File") },
			{ "reg",   ("LoopRegistry", "Registry") },
		};

		// Loop Parse/Files/Read/Reg <args>: same Push/while(MoveNext)/Pop shape as LowerLoop, but the enumerator is
		// the matching Loops.LoopXxx(args) and the pushed LoopType drives A_LoopField/A_LoopFile* accessors.
		private StatementSyntax LowerSpecialLoop(SpecialLoopStmt sl)
		{
			var (method, loopType) = SpecialLoops[sl.Kind];
			var id = ++_flowCounter;
			var ev = "KS_e" + id;
			var argExprs = sl.Args.Select(a => a == null ? Null : LowerExpr(a)).ToArray();
			var enumInit = Inv(Member(Inv(Access("Keysharp.Runtime.Loops." + method), argExprs), "GetEnumerator"));
			var cond = Inv(Access("Keysharp.Runtime.Flow.IsTrueAndRunning"), Inv(Member(Id(ev), "MoveNext")));
			var frame = PushLoop(id);
			var body = WrapLoopBody(AsBlock(LowerStmt(sl.Body)), sl.Until, frame);
			PopLoop();
			var loop = SyntaxFactory.WhileStatement(cond, body);
			var block = new List<StatementSyntax> {
				CallStmt("Keysharp.Runtime.Loops.Push", Access("Keysharp.Runtime.LoopType." + loopType)),
				LocalDecl(Ty("System.Collections.IEnumerator"), ev, enumInit),
				TryFinally(loop, LoopFinally(sl.Else)) };
			if (frame.NeedsEnd) block.Add(EndLabel(id));
			return SyntaxFactory.Block(block);
		}

		private static bool IsLoopStmt(Stmt s) => s is WhileStmt or LoopStmt or SpecialLoopStmt or ForStmt;

		// Pushes a loop frame (consuming any `name:` label that preceded this loop) and returns it.
		private LoopFrame PushLoop(int id)
		{
			var f = new LoopFrame { Id = id, Label = _pendingLoopLabel };
			_pendingLoopLabel = null;
			_loopFrames.Add(f);
			return f;
		}
		private void PopLoop() => _loopFrames.RemoveAt(_loopFrames.Count - 1);

		private static StatementSyntax NextLabel(int id) => SyntaxFactory.LabeledStatement("KS_e" + id + "_next", SyntaxFactory.EmptyStatement());
		private static StatementSyntax EndLabel(int id) => SyntaxFactory.LabeledStatement("KS_e" + id + "_end", SyntaxFactory.EmptyStatement());
		// A user label `name:` / goto target -> a mangled, collision-free C# label identifier.
		private static string UserLabelId(string name) => "KS_lbl_" + NameMangler.Escape(name.ToLowerInvariant());

		// Appends the trailing `Until` break and, when a level/label continue targeted this loop, the `_next:` label.
		private BlockSyntax WrapLoopBody(BlockSyntax body, Expr until, LoopFrame frame)
		{
			if (until != null)
				body = body.WithStatements(body.Statements.Add(SyntaxFactory.IfStatement(IfTest(LowerExpr(until)), SyntaxFactory.BreakStatement())));
			if (frame.NeedsNext)
				body = body.WithStatements(body.Statements.Add(NextLabel(frame.Id)));
			return body;
		}

		// break/continue always `goto`s the target loop's `_end`/`_next` label (never native C# break/continue): AHK
		// break/continue refer to the enclosing LOOP, and a switch is lowered as a real C# switch, so a native `break`
		// in a case would wrongly break the switch. Resolves an optional level-N or source-label target. Matches canonical.
		private StatementSyntax LowerBreakContinue(string target, bool isBreak)
		{
			if (_loopFrames.Count == 0)   // no enclosing loop (malformed) — emit a harmless native break/continue
				return isBreak ? SyntaxFactory.BreakStatement() : (StatementSyntax)SyntaxFactory.ContinueStatement();
			LoopFrame frame;
			if (target == null)
				frame = _loopFrames[^1];
			else if (int.TryParse(target, out var n) && n >= 1)
			{
				var idx = _loopFrames.Count - n;
				frame = _loopFrames[idx < 0 ? 0 : idx];
			}
			else
				frame = _loopFrames.LastOrDefault(f => f.Label != null && f.Label.Equals(target, System.StringComparison.OrdinalIgnoreCase))
					?? _loopFrames[^1];
			if (isBreak) { frame.NeedsEnd = true; return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, Id("KS_e" + frame.Id + "_end")); }
			frame.NeedsNext = true; return SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, Id("KS_e" + frame.Id + "_next"));
		}

		private static HashSet<string> CollectDirectLabels(System.Collections.Generic.IEnumerable<Stmt> stmts)
		{
			var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var s in stmts) if (s is LabelStmt l) set.Add(l.Name);
			return set;
		}

		// Lowers a statement sequence in its own label scope, associating a `name:` label that immediately precedes a
		// loop with that loop (so `break name`/`continue name` can target it). Labels also emit a plain C# label.
		private List<StatementSyntax> LowerStmtList(System.Collections.Generic.IReadOnlyList<Stmt> stmts)
		{
			_labelScopes.Add(CollectDirectLabels(stmts));
			var result = new List<StatementSyntax>();
			for (int i = 0; i < stmts.Count; i++)
			{
				if (stmts[i] is LabelStmt ll && i + 1 < stmts.Count && IsLoopStmt(stmts[i + 1])) _pendingLoopLabel = ll.Name;
				var ls = LowerStmt(stmts[i]);
				if (ls != null) result.Add(ls);
			}
			_labelScopes.RemoveAt(_labelScopes.Count - 1);
			return result;
		}

		// The loop's `finally` body. Always pops the loop frame; when an `else` block is present it runs iff the
		// loop body never executed (`Loops.Pop().index == 0`), mirroring AHK loop-else and the canonical visitor.
		private StatementSyntax LoopFinally(Stmt elseStmt)
		{
			var pop = Inv(Access("Keysharp.Runtime.Loops.Pop"));
			if (elseStmt == null) return ExprStmt(pop);
			return SyntaxFactory.IfStatement(
				SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, Member(pop, "index"), Num("0")),
				AsBlock(LowerStmt(elseStmt)));
		}

		private StatementSyntax LowerFor(ForStmt fr)
		{
			var id = ++_flowCounter;
			var ev = "KS_e" + id;
			var meArgs = new List<ExpressionSyntax> { LowerExpr(fr.Enumerable) };
			// An omitted loop variable (`for (, v in arr)`) still consumes a slot: pass a discarding VarRef.
			meArgs.AddRange(fr.Vars.Select(v => v == null ? DiscardVarRef() : MakeRefFor(new NameExpr(v))));
			var enumInit = Inv(Member(Inv(Access("Keysharp.Runtime.Loops.MakeEnumerable"), meArgs.ToArray()), "GetEnumerator"));

			var frame = PushLoop(id);
			var body = AsBlock(LowerStmt(fr.Body));
			PopLoop();
			body = body.WithStatements(body.Statements.Insert(0, CallStmt("Keysharp.Runtime.Loops.Inc")));
			var cond = Inv(Access("Keysharp.Runtime.Flow.IsTrueAndRunning"), Inv(Member(Id(ev), "MoveNext")));
			var loop = SyntaxFactory.WhileStatement(cond, WrapLoopBody(body, fr.Until, frame));

			// AHK scopes for-loop variables: they're restored to their pre-loop values after the loop. Back each one
			// up into a temp before the loop and restore it in the finally (matches the canonical backup/restore).
			var loopVars = fr.Vars.Where(v => v != null).ToList();
			var backups = loopVars.Select(_ => NewTemp()).ToList();

			var finallyStmts = new List<StatementSyntax>();
			for (int i = 0; i < loopVars.Count; i++) finallyStmts.Add(ExprStmt(Assign(NameRef(loopVars[i]), Id(backups[i]))));
			finallyStmts.Add(LoopFinally(fr.Else));

			var block = new List<StatementSyntax> { CallStmt("Keysharp.Runtime.Loops.Push") };
			for (int i = 0; i < loopVars.Count; i++) block.Add(ExprStmt(Assign(Id(backups[i]), NameRef(loopVars[i]))));
			block.Add(LocalDeclVar(ev, enumInit));
			block.Add(TryFinally(loop, finallyStmts.Count == 1 ? finallyStmts[0] : SyntaxFactory.Block(finallyStmts)));
			if (frame.NeedsEnd) block.Add(EndLabel(id));
			return SyntaxFactory.Block(block);
		}

		// Lowered as a real C# `switch` (not an if-chain) so that all case bodies share one label scope — a `goto`
		// in one case can target a `name:` label in another (mirrors the canonical visitor). A value switch governs on
		// `ForceString(value)` with `case string KS_swN when KS_swN.Equals(caseValue)`; a value-less switch governs on
		// `true` with `case true when IfTest(cond)`. Each section ends with a (switch-)break; AHK user break/continue
		// inside a case go to the enclosing loop via goto (see LowerBreakContinue).
		private StatementSyntax LowerSwitch(SwitchStmt s)
		{
			bool valueless = s.Value == null;
			// Optional CaseSense arg: a literal 0/"off"/false makes string comparison case-insensitive (default: sensitive).
			bool insensitive = s.CaseSense is LiteralExpr cl &&
				(cl.Kind == LiteralKind.Number ? cl.Raw is "0" or "0.0"
				 : DecodeString(cl.Raw) is var cs && (cs.Equals("off", System.StringComparison.OrdinalIgnoreCase) || cs == "0" || cs.Equals("false", System.StringComparison.OrdinalIgnoreCase)));

			// All labels directly inside any case/default body share the switch's (single) C# label scope.
			var switchLabels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var c in s.Cases) switchLabels.UnionWith(CollectDirectLabels(c.Body));
			if (s.Default != null) switchLabels.UnionWith(CollectDirectLabels(s.Default));
			_labelScopes.Add(switchLabels);

			SwitchLabelSyntax CaseLabel(Expr v)
			{
				if (valueless)
					return SyntaxFactory.CasePatternSwitchLabel(
						SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)),
						SyntaxFactory.WhenClause(IfTest(LowerExpr(v))), SyntaxFactory.Token(SyntaxKind.ColonToken));
				var pv = "KS_sw" + (++_flowCounter);
				var pattern = SyntaxFactory.DeclarationPattern(
					SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
					SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(pv)));
				var guard = insensitive
					? Inv(Member(Id(pv), "Equals"), CaseValueString(v), Access("System.StringComparison.OrdinalIgnoreCase"))
					: Inv(Member(Id(pv), "Equals"), CaseValueString(v));
				return SyntaxFactory.CasePatternSwitchLabel(pattern, SyntaxFactory.WhenClause(guard), SyntaxFactory.Token(SyntaxKind.ColonToken));
			}

			SwitchSectionSyntax Section(IEnumerable<SwitchLabelSyntax> labels, List<StatementSyntax> body)
			{
				body.Add(SyntaxFactory.BreakStatement());   // AHK cases don't fall through; terminate the C# section
				return SyntaxFactory.SwitchSection(SyntaxFactory.List(labels), SyntaxFactory.List(body));
			}

			var sections = new List<SwitchSectionSyntax>();
			foreach (var c in s.Cases)
				sections.Add(Section(c.Values.Select(CaseLabel), LowerStmtList(c.Body)));
			if (s.Default != null)
				sections.Add(Section(new SwitchLabelSyntax[] { SyntaxFactory.DefaultSwitchLabel() }, LowerStmtList(s.Default)));

			_labelScopes.RemoveAt(_labelScopes.Count - 1);

			var govern = valueless
				? (ExpressionSyntax)SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
				: Op("ForceString", LowerExpr(s.Value));
			return SyntaxFactory.SwitchStatement(govern, SyntaxFactory.List(sections));
		}

		// The string a `case` value compares against. String/integer literals are folded to a constant at
		// lower-time (no runtime ForceString); everything else is forced to a string at runtime.
		private ExpressionSyntax CaseValueString(Expr v)
		{
			if (v is LiteralExpr l)
			{
				if (l.Kind == LiteralKind.String) return Str(DecodeString(l.Raw));
				if (l.Kind == LiteralKind.Number && TryFoldIntLiteral(l.Raw, out var folded)) return Str(folded);
			}
			return Op("ForceString", LowerExpr(v));
		}

		// Folds an integer literal (decimal/hex/octal/binary, optional sign/underscores) to its decimal string.
		// Floats and anything unparseable return false so the caller falls back to a runtime ForceString.
		private static bool TryFoldIntLiteral(string raw, out string result)
		{
			result = null;
			var t = raw.Replace("_", "");
			if (t.Length == 0 || t.Contains('.')) return false;
			bool neg = t.StartsWith("-"); if (neg || t.StartsWith("+")) t = t.Substring(1);
			bool isHex = t.StartsWith("0x") || t.StartsWith("0X");
			if (!isHex && (t.Contains('e') || t.Contains('E'))) return false;   // exponent => float in AHK
			try
			{
				long val =
					isHex ? System.Convert.ToInt64(t.Substring(2), 16) :
					(t.StartsWith("0b") || t.StartsWith("0B")) ? System.Convert.ToInt64(t.Substring(2), 2) :
					(t.StartsWith("0o") || t.StartsWith("0O")) ? System.Convert.ToInt64(t.Substring(2), 8) :
					long.Parse(t);
				result = (neg ? -val : val).ToString(System.Globalization.CultureInfo.InvariantCulture);
				return true;
			}
			catch { return false; }
		}

		private StatementSyntax LowerTry(TryStmt tr)
		{
			var body = AsBlock(LowerStmt(tr.Body));
			var finallyBlock = tr.Finally != null ? AsBlock(LowerStmt(tr.Finally)) : null;

			// try/catch/else: the else body runs only if the try completed without an exception — guard with a flag.
			if (tr.Else != null)
			{
				var ok = "KS_ok" + (++_flowCounter);
				body = body.WithStatements(body.Statements.Add(ExprStmt(Assign(Id(ok), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))));
				var inner = BuildTry(body, tr.Catches, null);
				var elseBlock = SyntaxFactory.Block(
					LocalDecl(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)), ok, SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)),
					inner, SyntaxFactory.IfStatement(Id(ok), AsBlock(LowerStmt(tr.Else))));
				if (finallyBlock == null) return elseBlock;
				return SyntaxFactory.TryStatement().WithBlock(elseBlock).WithFinally(SyntaxFactory.FinallyClause(finallyBlock));
			}

			return BuildTry(body, tr.Catches, finallyBlock);
		}

		private StatementSyntax BuildTry(BlockSyntax body, List<CatchBlock> catchBlocks, BlockSyntax finallyBlock)
		{
			var catches = new List<CatchClauseSyntax>();
			foreach (var cb in catchBlocks)
				catches.Add(LowerCatch(cb));
			// Bare `try` (no catch, no finally) still swallows Keysharp exceptions to mirror AHK.
			if (catches.Count == 0 && finallyBlock == null)
				catches.Add(SyntaxFactory.CatchClause()
					.WithDeclaration(SyntaxFactory.CatchDeclaration(Ty("Keysharp.Builtins.KeysharpException")))
					.WithBlock(SyntaxFactory.Block()));

			// When the try actually catches (has catch clauses), register the caught types on the runtime try-stack for
			// the duration of the body, so a runtime-RAISED error (a ComCall HRESULT, a type/property error via
			// Errors.*Occurred) knows it is inside a catching `try` and throws to be caught here rather than surfacing
			// the unhandled-error dialog / exiting the thread. A `try`/`finally` with no catch does NOT catch, so it
			// gets no PushTry. PopTry runs via an inner finally so it is balanced even when the body throws.
			if (catches.Count > 0)
			{
				var caughtTypes = new List<string>();
				foreach (var cb in catchBlocks)
					if (cb.Types.Count == 0) caughtTypes.Add("Keysharp.Builtins.Error");
					else foreach (var t in cb.Types) caughtTypes.Add(ResolveErrorType(t));
				if (catchBlocks.Count == 0) caughtTypes.Add("Keysharp.Builtins.Error");   // synthetic bare-`try` catch
				var pushArgs = caughtTypes.Select(tn => (ExpressionSyntax)SyntaxFactory.TypeOfExpression(Ty(tn))).ToArray();
				var innerTry = SyntaxFactory.TryStatement().WithBlock(body)
					.WithFinally(SyntaxFactory.FinallyClause(SyntaxFactory.Block(CallStmt("Keysharp.Runtime.Loops.PopTry"))));
				body = SyntaxFactory.Block(CallStmt("Keysharp.Runtime.Loops.PushTry", pushArgs), innerTry);
			}

			var stmt = SyntaxFactory.TryStatement().WithBlock(body).WithCatches(SyntaxFactory.List(catches));
			if (finallyBlock != null) stmt = stmt.WithFinally(SyntaxFactory.FinallyClause(finallyBlock));
			return stmt;
		}

		// catch (KeysharpException ex) when (ex.UserError is Type1 || ...) { [var := ex.UserError;] body }
		private CatchClauseSyntax LowerCatch(CatchBlock cb)
		{
			var ex = "KS_ex" + (++_flowCounter);
			var userErr = Member(Id(ex), "UserError");
			var block = AsBlock(LowerStmt(cb.Body));
			if (cb.Var != null)
				block = block.WithStatements(block.Statements.Insert(0, ExprStmt(Assign(NameRef(cb.Var), userErr))));

			ExpressionSyntax cond;
			if (cb.Types.Count == 0)
				cond = SyntaxFactory.IsPatternExpression(userErr, SyntaxFactory.TypePattern(Ty("Keysharp.Builtins.Error")));
			else
			{
				cond = null;
				foreach (var t in cb.Types)
				{
					var test = SyntaxFactory.IsPatternExpression(userErr, SyntaxFactory.TypePattern(Ty(ResolveErrorType(t))));
					cond = cond == null ? test : SyntaxFactory.BinaryExpression(SyntaxKind.LogicalOrExpression, cond, test);
				}
			}
			return SyntaxFactory.CatchClause()
				.WithDeclaration(SyntaxFactory.CatchDeclaration(Ty("Keysharp.Builtins.KeysharpException"), SyntaxFactory.Identifier(ex)))
				.WithFilter(SyntaxFactory.CatchFilterClause(cond))
				.WithBlock(block);
		}

		// Resolve a catch type name to a C# type: a user class, a known builtin, else Keysharp.Builtins.<name>.
		private string ResolveErrorType(string name)
		{
			var lower = name.ToLowerInvariant();
			if (_userClassByLower.TryGetValue(lower, out var ut)) return ut;
			if (Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(lower, out var type)) return type.FullName.Replace('+', '.');
			return "Keysharp.Builtins." + name;
		}

		private StatementSyntax LowerThrow(ThrowStmt th)
		{
			var errType = Ty("Keysharp.Builtins.Error");
			if (th.Value == null)
				return SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(errType).WithArgumentList(SyntaxFactory.ArgumentList()));
			var v = LowerExpr(th.Value);
			if (v is LiteralExpressionSyntax)
				return SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(errType)
					.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(v)))));
			return SyntaxFactory.ThrowStatement(SyntaxFactory.CastExpression(errType, SyntaxFactory.ParenthesizedExpression(v)));
		}

		// ---- classes ----

		// Resolves a dotted builtin base type like `Gui.Custom` (root via stringToTypes / a type-name alias, then nested
		// types case-insensitively) to its C# full name, or null if not a builtin.
		private static string ResolveDottedBuiltinType(string dotted)
		{
			if (dotted == null || !dotted.Contains('.')) return null;
			var parts = dotted.Split('.');
			var rootLower = parts[0].ToLowerInvariant();
			if (!Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(rootLower, out var t)) return null;
			for (int i = 1; i < parts.Length && t != null; i++)
				t = t.GetNestedTypes(System.Reflection.BindingFlags.Public).FirstOrDefault(n => n.Name.Equals(parts[i], System.StringComparison.OrdinalIgnoreCase));
			return t?.FullName.Replace('+', '.');
		}

		private void RegisterClass(ClassDecl c, string outerLower = null, string outerType = null)
		{
			var lower = outerLower == null ? c.Name.ToLowerInvariant() : outerLower + "." + c.Name.ToLowerInvariant();
			var typeName = outerType == null ? NameMangler.ClassType(c.Name) : outerType + "." + NameMangler.ClassType(c.Name);
			_userClassByLower[lower] = typeName;
			_userClassDeclByLower[lower] = c;
			if (outerLower == null)   // only top-level classes get a global slot field; nested ones are static props of the parent
			{
				var id = NameMangler.Escape(lower);
				_fields[lower] = id;
				_classFieldIds.Add(id);
				_fieldDecls.Add(ObjArrowProp(id, TypeSingleton(typeName)));
			}
			// Register nested classes under their dotted path (`Outer.Inner` -> `OuterType.InnerType`) so a sibling can
			// extend them by qualified name (e.g. `class B extends Outer.A`, common in UIA.ahk).
			foreach (var nc in c.Nested) RegisterClass(nc, lower, typeName);
		}

		private MemberDeclarationSyntax LowerClass(ClassDecl c)
		{
			var baseType = c.IsStruct ? "Keysharp.Builtins.Struct" : "Keysharp.Builtins.KeysharpObject";
			if (c.Base != null)
			{
				var bl = c.Base.ToLowerInvariant();
				if (_userClassByLower.TryGetValue(bl, out var bn)) baseType = bn;
				else if (Script.TheScript.ReflectionsData.stringToTypes.TryGetValue(bl, out var bt)) baseType = bt.FullName.Replace('+', '.');
				else if (ResolveDottedBuiltinType(c.Base) is { } dotted) baseType = dotted;   // e.g. `Gui.Custom`
				else Diag($"extending unknown class '{c.Base}' is not yet lowerable");
			}

			var typeName = NameMangler.ClassType(c.Name);
			var savedBase = _currentClassBase; _currentClassBase = baseType;
			var savedCompat = _currentCompat;
			_currentCompat = (c.Requires != null ? MapRequires(c.Requires) : null) ?? _currentCompat;   // class-level `#Requires`
			// Static-locals of this class's methods/properties belong to THIS type, not the module class — redirect the
			// sink so their backing fields are emitted as members of this class (a nested LowerClass redirects again to
			// its own list and restores to ours). This both scopes them correctly and avoids cross-class name collisions.
			var savedSink = _staticFieldSink;
			var classStaticFields = new List<MemberDeclarationSyntax>();
			_staticFieldSink = classStaticFields;
			var savedPath = _currentClassPath;
			var savedUserPath = _currentClassUserPath;
			_currentClassPath = _currentClassPath == null ? typeName : _currentClassPath + "." + typeName;
			_currentClassUserPath = _currentClassUserPath == null ? c.Name : _currentClassUserPath + "." + c.Name;
			// Class-body `#import` frame: visible to every member lowered below — methods, property accessors, field and
			// static initializers, and (via the still-pushed stack) lexically nested classes. Pushed AFTER the `extends`
			// clause is resolved above, so a base-class name is never shadowed by a class import.
			var classFrame = c.Imports.Count > 0 ? BuildImportScope(c.Imports) : null;
			if (classFrame != null) _importScopes.Add(classFrame);
			var members = new List<MemberDeclarationSyntax> { ClassCtor(typeName) };
			foreach (var m in c.Methods) members.Add(LowerMethod(m, typeName));
			foreach (var pr in c.Properties) members.AddRange(LowerProperty(pr));
			// Nested classes become nested C# types; the runtime registers them as static properties on the parent.
			foreach (var nc in c.Nested) members.Add(LowerClass(nc));

			var instFields = c.Fields.Where(f => !f.Static && f.Init != null).ToList();
			if (instFields.Count > 0 || c.InstanceInit.Count > 0)
				members.Add(InitMethod(NameMangler.InstanceInit, baseType, instFields, null, c.InstanceInit, staticCtx: false));
			var statFields = c.Fields.Where(f => f.Static && f.Init != null).ToList();
			// A struct's typed fields are registered on the prototype (DefineStructFieldOnPrototype with the field's .NET type) in the
			// static initializer, so the runtime knows the struct layout.
			var typedFields = c.Fields.Where(f => f.TypeExpr != null).ToList();
			_structTypeName = typeName;
			var savedInMethod = _inMethod;
			var savedMethodStatic = _currentMethodStatic;
			var savedThisFuncName = _currentThisFuncName;
			_inMethod = true;
			_currentMethodStatic = true;
			_currentThisFuncName = ClassMemberFuncName("__Init", true);
			var staticPre = typedFields.Select(StructFieldDefineProp).Where(s => s != null).ToList();
			_inMethod = savedInMethod;
			_currentMethodStatic = savedMethodStatic;
			_currentThisFuncName = savedThisFuncName;
			if (statFields.Count > 0 || staticPre.Count > 0 || c.StaticInit.Count > 0)
				members.Add(InitMethod(NameMangler.StaticInit(), null, statFields, staticPre, c.StaticInit, staticCtx: true));

			// Emit this class's collected static-local backing fields as its own members, then restore the sink/path/frame.
			members.AddRange(classStaticFields);
			_staticFieldSink = savedSink;
			_currentClassPath = savedPath;
			_currentClassUserPath = savedUserPath;
			if (classFrame != null) _importScopes.RemoveAt(_importScopes.Count - 1);

			var decl = SyntaxFactory.ClassDeclaration(typeName)
				.AddModifiers(PublicTok)
				.WithBaseList(BaseList(baseType))
				.WithMembers(SyntaxFactory.List(members));

			if (HasInlineCSharp(c))
				decl = decl.AddModifiers(PartialTok);
			// Preserve the user's original spelling so the runtime can resolve the class by name (and display it).
			if (!string.Equals(typeName, c.Name, System.StringComparison.Ordinal))
				decl = decl.AddAttributeLists(Attr("Keysharp.Runtime.UserDeclaredName", Str(c.Name)));
			_currentClassBase = savedBase;
			_currentCompat = savedCompat;
			return decl;
		}

		// Registers a typed struct field on the prototype. The type expression is evaluated at runtime in the
		// class's static initializer, as required by v2.1-alpha.30.
		private StatementSyntax StructFieldDefineProp(ClassField f)
		{
			var proto = SyntaxFactory.ElementAccessExpression(Access("MainScript.Vars.Prototypes"))
				.WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(SyntaxFactory.TypeOfExpression(Ty(_structTypeName))))));
			var typeValue = LowerExpr(f.TypeExpr);

			// Pass the #StructPack alignment as a 4th argument when one is in effect (0 = default packing).
			return f.Pack != 0
				? ExprStmt(Inv(Access("Keysharp.Builtins.Objects.DefineStructFieldOnPrototype"), proto, Str(f.Name), typeValue, Num(f.Pack.ToString())))
				: ExprStmt(Inv(Access("Keysharp.Builtins.Objects.DefineStructFieldOnPrototype"), proto, Str(f.Name), typeValue));
		}

		private static bool HasInlineCSharp(ClassDecl c) =>
			c.CSharpBlocks != null || c.Nested.Any(HasInlineCSharp);

		// __Init / static__Init: optionally chain the base prototype's __Init, then run extra prologue statements
		// (typed struct-field registrations) and set each field.
		private MemberDeclarationSyntax InitMethod(string name, string baseProtoType, List<ClassField> fields,
			List<StatementSyntax> prologue = null, List<Stmt> extra = null, bool staticCtx = false)
		{
			var savedThisFuncName = _currentThisFuncName;
			_currentThisFuncName = ClassMemberFuncName("__Init", staticCtx);
			var savedScopeFuncs = _pendingScopeFuncs; _pendingScopeFuncs = new();   // field-init fat-arrow local funcs
			var stmts = new List<StatementSyntax>();
			if (prologue != null) stmts.AddRange(prologue);
			if (baseProtoType != null)
			{
				var proto = SyntaxFactory.ElementAccessExpression(Access("MainScript.Vars.Prototypes"))
					.WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(SyntaxFactory.TypeOfExpression(Ty(baseProtoType))))));
				var tuple = SyntaxFactory.CastExpression(ObjType,
					SyntaxFactory.TupleExpression(SyntaxFactory.SeparatedList(new[] { Arg(proto), Arg(Id("@this")) })));
				stmts.Add(ExprStmt(Op("Invoke", tuple, Str("__Init"))));
			}
			foreach (var f in fields) stmts.Add(FieldSet(f));
			// Member/index-target initializers (`static x.y := z`) run after the plain field sets, with `this` bound to
			// the class object (static) or instance.
			if (extra != null && extra.Count > 0)
			{
				var savedM = _inMethod; var savedS = _currentMethodStatic; _inMethod = true; _currentMethodStatic = staticCtx;
				foreach (var st in extra) { var ls = LowerStmt(st); if (ls != null) stmts.Add(ls); }
				_inMethod = savedM; _currentMethodStatic = savedS;
			}
			stmts.AddRange(_pendingScopeFuncs);   // fat-arrow field values become local funcs that capture @this
			stmts.Add(SyntaxFactory.ReturnStatement(Str("")));
			_pendingScopeFuncs = savedScopeFuncs;
			_currentThisFuncName = savedThisFuncName;
			return ObjMethod(name, ParamThis(), SyntaxFactory.Block(stmts));
		}

		// `super` lowers to the tuple (object)(MainScript.Vars.{Prototypes|Statics}[typeof(Base)], @this); the runtime
		// resolves a member/method against the base's prototype while keeping `this` bound to the current instance.
		private ExpressionSyntax SuperTuple()
		{
			var baseType = _currentClassBase ?? "Keysharp.Builtins.KeysharpObject";
			var table = _currentMethodStatic ? "MainScript.Vars.Statics" : "MainScript.Vars.Prototypes";
			var proto = SyntaxFactory.ElementAccessExpression(Access(table))
				.WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(SyntaxFactory.TypeOfExpression(Ty(baseType))))));
			return SyntaxFactory.CastExpression(ObjType,
				SyntaxFactory.TupleExpression(SyntaxFactory.SeparatedList(new[] { Arg(proto), Arg(Id("@this")) })));
		}

		private StatementSyntax FieldSet(ClassField f)
		{
			var saved = _inMethod; _inMethod = true;
			var v = LowerExpr(f.Init);
			_inMethod = saved;
			return ExprStmt(Op("SetPropertyValue", Id("@this"), Str(f.Name), v));
		}

		private MemberDeclarationSyntax LowerMethod(ClassMethod m, string classType)
		{
			var paramLowers = m.Params.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
			var implName = m.Static ? NameMangler.StaticMethod(m.Name) : NameMangler.Method(m.Name);
			var thisFuncName = ClassMemberFuncName(m.Name, m.Static);
			// A method whose impl name collides with the enclosing type (or its constructor) must be renamed;
			// the runtime still resolves it via the UserDeclaredName attribute.
			bool renamed = !m.Static && implName == classType;
			if (renamed) implName += "_KSm";
			var saved = _inMethod; _inMethod = true;
			var savedStatic = _currentMethodStatic; _currentMethodStatic = m.Static;
			var savedCompat = _currentCompat;
			_currentCompat = ScanRequires(m.Body?.Body) ?? _currentCompat;   // a `#Requires` in the method body sets its mode
			var body = LowerCallableBody(paramLowers, m.Body, m.ArrowBody, implName,
				thisFuncName, ByRefSet(m.Params), m.Params);
			_inMethod = saved; _currentMethodStatic = savedStatic;
			var attrs = new List<AttributeListSyntax>();
			// Always stamped, not only when the mangler changed the spelling: the exact source case is what
			// OwnProps() enumeration, case-sensitive (`==`/`!==`) comparisons and Func.Name all report, and a name
			// carried in metadata is one the runtime never has to recover from the emitted identifier.
			attrs.Add(Attr("Keysharp.Runtime.UserDeclaredName", Str(m.Name)));
			attrs.AddRange(CompatAttr());
			_currentCompat = savedCompat;
			return ObjMethod(implName, ParamDecls(m.Params, includeThis: true, wrapVariadics: true), body, attrs.ToArray());
		}

		private List<MemberDeclarationSyntax> LowerProperty(ClassProperty pr)
		{
			var result = new List<MemberDeclarationSyntax>();
			var idxLowers = pr.Params.Select(p => p.Name.ToLowerInvariant()).ToList();
			// Index params with full variadic (`a*` -> params object[]) / optional handling, like a normal param list.
			// wrapVariadics so the body sees an Array (`__Item[a*]` uses `a.Length`); LowerCallableBody adds the wrap.
			List<ParameterSyntax> IdxParams() => ParamDecls(pr.Params, includeThis: false, wrapVariadics: true).Parameters.ToList();
			// A static property's accessors get the ClassStaticPrefix (registered on the class's static object); `this`
			// inside them is the static class object. Otherwise identical to instance-property accessors.
			var getterName = pr.Static ? NameMangler.StaticGetter(pr.Name) : NameMangler.Getter(pr.Name);
			var setterName = pr.Static ? NameMangler.StaticSetter(pr.Name) : NameMangler.Setter(pr.Name);

			if (pr.HasGet)
			{
				var thisFuncName = ClassMemberFuncName(pr.Name, pr.Static) + ".Get";
				var savedM = _inMethod; var savedS = _currentMethodStatic; _inMethod = true; _currentMethodStatic = pr.Static;
				var body = LowerCallableBody(new HashSet<string>(idxLowers), pr.GetBody, pr.GetArrow, getterName,
					thisFuncName, ByRefSet(pr.Params), pr.Params);
				_inMethod = savedM; _currentMethodStatic = savedS;
				var ps = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(Prepend(ThisParam(), IdxParams().ToArray())));
				result.Add(ObjMethod(getterName, ps, body, Attr("Keysharp.Runtime.UserDeclaredName", Str(pr.Name))));
			}
			if (pr.HasSet)
			{
				var thisFuncName = ClassMemberFuncName(pr.Name, pr.Static) + ".Set";
				var setParams = new HashSet<string>(idxLowers) { "value" };
				var savedM = _inMethod; var savedS = _currentMethodStatic; _inMethod = true; _currentMethodStatic = pr.Static;
				var body = LowerCallableBody(setParams, pr.SetBody, pr.SetArrow, setterName,
					thisFuncName, ByRefSet(pr.Params), pr.Params);
				_inMethod = savedM; _currentMethodStatic = savedS;
				var idx = IdxParams();
				// `value` is always the LAST setter param, so a trailing `params object[]` index param drops `params`
				// (C# requires params to be last) — it becomes a plain object[] the runtime fills with the index args.
				if (idx.Count > 0 && idx[^1].Modifiers.Any(SyntaxKind.ParamsKeyword))
					idx[^1] = idx[^1].WithModifiers(SyntaxFactory.TokenList());
				var all = new List<ParameterSyntax> { ThisParam() };
				all.AddRange(idx);
				all.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(NameMangler.Escape("value"))).WithType(ObjType));
				result.Add(ObjMethod(setterName, SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(all)),
					body, Attr("Keysharp.Runtime.UserDeclaredName", Str(pr.Name))));
			}
			return result;
		}

		// ---- functions ----

		// A fat-arrow used as a value: emit it as a C# LOCAL FUNCTION in the current callable scope and bind it as a
		// KeysharpFunc via its method group (`Func((Delegate)FN)`). Because it's a local function it captures `@this` and
		// the enclosing locals through normal C# closure semantics (matches the canonical — no Functions.Closure /
		// top-level method needed). The local functions are flushed into the scope body by LowerCallableBody /
		// InitMethod / the auto-exec assembly (C# hoists local functions, so forward references are fine).
		private ExpressionSyntax LowerFatArrow(FatArrowExpr fa)
		{
			var n = ++_lambdaCounter;
			var implName = "FN_KS_AnonLambda_" + n;
			var thisFuncName = fa.Name ?? "";
			var paramLowers = fa.Params.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
			var savedCaptured = _capturedInScope; _capturedInScope = false;
			// A named fn-expression `name(params) => …` can call itself: resolve `name` inside the body to a Func over
			// this lambda's impl (the local function is hoisted, so a forward self-reference is fine). Save/restore any
			// outer alias of the same name. Skip when the name is a real local/param (it shadows the self-reference).
			string selfName = fa.Name?.ToLowerInvariant();
			bool aliasInBody = selfName != null && !paramLowers.Contains(selfName)
				&& !(_locals != null && _locals.Contains(selfName));
			System.Func<ExpressionSyntax> savedAlias = null;
			bool hadAlias = aliasInBody && _inlineAliases.TryGetValue(selfName, out savedAlias);
			if (aliasInBody) _inlineAliases[selfName] = () => FuncBind(implName);
			var body = LowerCallableBody(paramLowers, fa.BlockBody, fa.BlockBody == null ? fa.Body : null, implName,
				thisFuncName, ByRefSet(fa.Params), fa.Params, capturing: true);
			if (aliasInBody) { if (hadAlias) _inlineAliases[selfName] = savedAlias; else _inlineAliases.Remove(selfName); }
			bool captured = _capturedInScope; _capturedInScope = savedCaptured;
			var localFunction = SyntaxFactory.LocalFunctionStatement(ObjType, SyntaxFactory.Identifier(implName))
				.WithParameterList(ParamDecls(fa.Params, includeThis: false, wrapVariadics: true))
				.WithBody(body);
			// Always stamped, "" for an anonymous lambda: Roslyn renames a local function to `<Outer>g__name|n_m`,
			// and the attribute is what spares the runtime having to read that scheme back.
			localFunction = localFunction.AddAttributeLists(Attr("Keysharp.Runtime.UserDeclaredName", Str(fa.Name ?? "")));
			_pendingScopeFuncs.Add(localFunction);
			// A closure that captured `this`/an enclosing local binds as a Closure (so `x is Closure`); otherwise a Func.
			return captured ? ClosureBind(implName) : FuncBind(implName);
		}

		// A bare nested function: emit a capturing C# local function (like a fat-arrow) and bind its name to a local
		// KeysharpFunc/Closure var (declared at this position) so `name(...)` calls it. Captures `@this`/enclosing locals.
		private StatementSyntax LowerNestedClosure(FunctionDecl fd)
		{
			var nameLower = fd.Name.ToLowerInvariant();
			var implName = "FN_" + NameMangler.Escape(nameLower) + "_" + (++_lambdaCounter);
			var paramLowers = fd.Params.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
			// The name is already in _scopeClosureNames (pre-collected by the enclosing scope), so a recursive reference
			// in the body resolves to this local closure var rather than a module field.
			var savedCaptured = _capturedInScope; _capturedInScope = false;
			var savedCompat = _currentCompat;
			_currentCompat = ScanRequires(fd.Body?.Body) ?? _currentCompat;   // nested-function `#Requires` (restored after)
			var body = LowerCallableBody(paramLowers, fd.Body, fd.ArrowBody, implName, fd.Name,
				ByRefSet(fd.Params), fd.Params, capturing: true, staticNested: fd.Static);
			_currentCompat = savedCompat;
			bool captured = _capturedInScope; _capturedInScope = savedCaptured;
			_pendingScopeFuncs.Add(SyntaxFactory.LocalFunctionStatement(ObjType, SyntaxFactory.Identifier(implName))
				.WithParameterList(ParamDecls(fd.Params, includeThis: false, wrapVariadics: true))
				.WithBody(body)
				.AddAttributeLists(Attr("Keysharp.Runtime.UserDeclaredName", Str(fd.Name))));
			// Assign (not declare — the var is hoisted) at the scope TOP so forward-referenced calls work and a
			// self-recursive binding doesn't trip definite assignment; the delegate's captures read lazily.
			_pendingScopeClosureInits.Add(ExprStmt(Assign(Id(NameMangler.Escape(nameLower)), captured ? ClosureBind(implName) : FuncBind(implName))));
			return null;
		}

		// ---- hotkeys / hotstrings / remaps ----

		private static readonly ExpressionSyntax UintZero = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0u));
		private const string CopilotDeclarationAlias = "Copilot";
		private const string CopilotDeclarationChord = "<#<+F23";
		private const uint CopilotFirmwareModifiersLR = KeyboardUtils.MOD_LWIN | KeyboardUtils.MOD_LSHIFT;

		// Restores a dangling backtick and resolves AHK escapes in a trigger (matches the canonical
		// VisitHotkey/VisitHotstring trigger handling). The `::` is its own token, so there is none to strip.
		private static string ProcessTriggerText(string trigger)
		{
			var t = trigger;
			if (t.Length > 0 && t[^1] == '`' && (t.Length < 2 || t[^2] != '`')) t += '`';
			return Keysharp.Parsing.Parser.EscapedString(t, true);
		}

		// Copilot is a declaration-time convenience alias, not a key name. Expanding it here keeps runtime
		// APIs such as Send, KeyWait, GetKeyState and Hotkey() honest while still allowing the concise spelling
		// in static hotkeys, custom combinations and remaps. Match a complete identifier only, so an ordinary
		// name such as MyCopilot is left untouched.
		private static string ExpandCopilotDeclarationAlias(string text, out bool usedAlias)
		{
			usedAlias = false;
			var searchFrom = 0;

			while (searchFrom < text.Length)
			{
				var index = text.IndexOf(CopilotDeclarationAlias, searchFrom, System.StringComparison.OrdinalIgnoreCase);

				if (index < 0)
					break;

				var after = index + CopilotDeclarationAlias.Length;
				var hasIdentifierBefore = index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_');
				var hasIdentifierAfter = after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_');

				if (hasIdentifierBefore || hasIdentifierAfter)
				{
					searchFrom = after;
					continue;
				}

				text = text.Substring(0, index) + CopilotDeclarationChord + text.Substring(after);
				searchFrom = index + CopilotDeclarationChord.Length;
				usedAlias = true;
			}

			return text;
		}

		// Emits a callback method (`public static object <name>(object thishotkey) { … }`) onto __Main and returns its name.
		private string EmitHotCallback(Block body, string name)
		{
			var ps = new List<Param> { new Param("thishotkey", null, false, false, false) };
			// AutoHotkey builds every hotkey, hotstring and #HotIf body from one `<Hotkey>(ThisHotkey)` template
			// (Script::CreateHotFunc), so that is the name A_ThisFunc reports inside all three.
			var lowered = LowerCallableBody(new HashSet<string> { "thishotkey" }, body, null, name, "<Hotkey>");
			_hotMembers.Add(ObjMethod(name, ParamDecls(ps, includeThis: false), lowered));
			return name;
		}

		private void LowerHotkey(HotkeyDef hk)
		{
			_persistent = true;
			string fnName;
			if (hk.Func != null)
			{
				if (_emittedFuncImpls.Add(NameMangler.FunctionMethod(hk.Func.Name))) _hotMembers.Add(LowerFunction(hk.Func));
				fnName = NameMangler.FunctionMethod(hk.Func.Name);
			}
			else
			{
				var block = hk.Body as Block ?? new Block(new List<Stmt> { hk.Body });
				fnName = EmitHotCallback(block, "__Hotkey_" + (++_hotCount));
			}
			foreach (var trig in hk.Triggers)
			{
				var text = ExpandCopilotDeclarationAlias(ProcessTriggerText(trig), out _);
				_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Keyboard.HotkeyDefinition.AddHotkey"),
					FuncBind("__Main." + fnName), UintZero, Str(text))));
			}
		}

		private void LowerHotstring(HotstringDef hs)
		{
			_persistent = true;
			bool hasExpansion = hs.Expansion != null;
			string expansionText = hasExpansion ? Keysharp.Parsing.Parser.EscapedString(hs.Expansion, true) : "";
			ExpressionSyntax funcArg = Null;
			if (!hasExpansion)
			{
				string fnName;
				if (hs.Func != null)
				{
					if (_emittedFuncImpls.Add(NameMangler.FunctionMethod(hs.Func.Name))) _hotMembers.Add(LowerFunction(hs.Func));
					fnName = NameMangler.FunctionMethod(hs.Func.Name);
				}
				else
				{
					var block = hs.Body as Block ?? new Block(new List<Stmt> { hs.Body ?? new Block(new List<Stmt>()) });
					fnName = EmitHotCallback(block, "__Hotstring_" + (++_hotCount));
				}
				funcArg = FuncBind("__Main." + fnName);
			}
			foreach (var trig in hs.Triggers)
			{
				var name = ProcessTriggerText(trig);   // `:opts:key` (escapes resolved)
				var colon = name.IndexOf(':', 1);
				var options = colon > 0 ? name.Substring(1, colon - 1) : "";
				var key = colon > 0 ? name.Substring(colon + 1) : name;
				_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Keyboard.HotstringManager.AddHotstring"),
					Str(name), funcArg, Str($"{options}:{key}"), Str(key), Str(expansionText), False)));
			}
		}

		private static StatementSyntax SetDelay(bool isMouse) =>
			CallStmt("Keysharp.Builtins." + (isMouse ? "Mouse.SetMouseDelay" : "Keyboard.SetKeyDelay"),
				SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1L)));
		private static StatementSyntax SendStmt(string text) => CallStmt("Keysharp.Builtins.Keyboard.Send", Str(text));
		private static StatementSyntax SendStmt(ExpressionSyntax text) => CallStmt("Keysharp.Builtins.Keyboard.Send", text);

		private MemberDeclarationSyntax RemapCallback(string name, List<StatementSyntax> body) =>
			SyntaxFactory.MethodDeclaration(ObjType, SyntaxFactory.Identifier(name)).AddModifiers(PublicTok, StaticTok)
				.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Parameter(SyntaxFactory.Identifier("args")).WithType(ObjArrayType).AddModifiers(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)))))
				.WithBody(SyntaxFactory.Block(body));

		// Port of VisitGeneral.VisitRemap: turns `source::target` into a down hotkey and an up hotkey that Send the
		// target key, computing the Send strings at compile time from the runtime key tables.
		private void LowerRemap(RemapDef rm)
		{
			_persistent = true;
			// The lexer already split the remap into its source/target key text; just decode the backtick escapes.
			var sourceKey = ExpandCopilotDeclarationAlias(Keysharp.Parsing.Parser.EscapedString(rm.Source, true), out var sourceUsesCopilotAlias);
			var targetKey = ExpandCopilotDeclarationAlias(Keysharp.Parsing.Parser.EscapedString(rm.Target, true), out _);
			// A physical Copilot key releases its synthetic LWin+LShift before F23 comes up. Keep releasing those
			// modifiers when a modifier is the remap target, but do not synthesize them back down afterward. The
			// literal `<#<+F23` spelling retains the generic chord behavior and therefore still restores them.
			var firmwareSourceModifiersLR = sourceUsesCopilotAlias ? CopilotFirmwareModifiersLR : 0u;

			uint remapDestVk = 0u, remapDestSc = 0u; uint? modLR = null, modifiersLR = null;
			var remapName = targetKey; var hotName = sourceKey;
			var ht = Script.TheScript.HookThread;
			var kbLayout = new KeybdLayoutRef(); // Lazy: resolved only if a single-char remap key actually needs the layout.

			remapName = HotkeyDefinition.TextToModifiers(remapName, null);
			var remapDestSource = KeySource.None;
			ht.TextToVKandSC(remapName, ref remapDestVk, ref remapDestSc, ref remapDestSource, ref modLR, kbLayout);

			var tempcp1 = HotkeyDefinition.TextToModifiers(hotName, null);
			var remapSourceVk = ht.TextToVK(tempcp1, ref modifiersLR, kbLayout);
			var remapSourceIsCombo = tempcp1.Contains(HotkeyDefinition.COMPOSITE_DELIMITER);
			var remapSourceIsMouse = MouseUtils.IsMouseVK(remapSourceVk);
			var remapDestIsMouse = MouseUtils.IsMouseVK(remapDestVk);
			var remapKeybdToMouse = !remapSourceIsMouse && remapDestIsMouse;
			var remapWheel = MouseUtils.IsWheelVK(remapSourceVk) || MouseUtils.IsWheelVK(remapDestVk);
			var remapSource = (remapSourceIsCombo ? "" : "*") + (tempcp1.Length == 1 && char.IsUpper(tempcp1[0]) ? "+" : "") + hotName;

			var remapDest = remapName[0] == '"' ? "\"" : remapName;
			var remapDestModifiers = targetKey.Substring(0, targetKey.IndexOf(remapName));
			// Remap targets use hotkey syntax, which allows the < and > side qualifiers. Send does not
			// recognize them and would type them as literal characters, so a destination which uses them is
			// emitted as explicit modifier key events instead of as a prefix. Neutral modifiers keep the
			// prefix form, which Send already resolves to the left-hand key.
			var destProperties = new HotkeyProperties();
			_ = HotkeyDefinition.TextToModifiers(targetKey, null, destProperties);
			// A neutral modifier is resolved to its left-hand key, which is what Send does with the ^ ! + #
			// prefixes. KeyboardUtils.ConvertModifiers must not be used here: it expands a neutral modifier to
			// BOTH side bits, which would press RAlt as well - that is AltGr on most non-US layouts and would
			// change the character produced.
			var neutralAsLeft = 0u;

			if ((destProperties.modifiers & KeyboardUtils.MOD_CONTROL) != 0) neutralAsLeft |= KeyboardUtils.MOD_LCONTROL;

			if ((destProperties.modifiers & KeyboardUtils.MOD_ALT) != 0) neutralAsLeft |= KeyboardUtils.MOD_LALT;

			if ((destProperties.modifiers & KeyboardUtils.MOD_SHIFT) != 0) neutralAsLeft |= KeyboardUtils.MOD_LSHIFT;

			if ((destProperties.modifiers & KeyboardUtils.MOD_WIN) != 0) neutralAsLeft |= KeyboardUtils.MOD_LWIN;

			var remapDestModifiersLR = destProperties.modifiersLR | neutralAsLeft;
			var remapDestIsSided = destProperties.modifiersLR != 0;
			string[] modifierKeyNames = ["LCtrl", "RCtrl", "LAlt", "RAlt", "LShift", "RShift", "LWin", "RWin"];
			var destModifiersDown = "";
			var destModifiersUp = "";

			if (remapDestIsSided)
				for (var i = 0; i < 8; ++i)
					if ((remapDestModifiersLR & (1u << i)) != 0)
					{
						destModifiersDown += $"{{{modifierKeyNames[i]} down}}";
						destModifiersUp = $"{{{modifierKeyNames[i]} up}}" + destModifiersUp;
					}

			var remapDestModifiersSend = remapDestIsSided ? destModifiersDown : remapDestModifiers;
			var remapDestKey = (Keys)remapDestVk;

			if (remapDestKey == Keys.Pause && remapDestModifiers.Length == 0 && string.Compare(remapDest, "Pause", true) == 0)
				return;   // a hotkey to pause the script, not a remap to the Pause key

			var altTabAction = HotkeyDefinition.ConvertAltTab(targetKey, false);
			if (altTabAction != 0)
			{
				_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Keyboard.HotkeyDefinition.AddHotkey"),
					Null, SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(altTabAction)), Str(hotName))));
				return;
			}

			var blindMods = "";
			var temphk = new HotkeyDefinition(999, null, (uint)HotkeyTypeEnum.Normal, hotName, 0);
			var remapSourceModifiersLR = temphk.modifiersConsolidatedLR;
			bool? remapDestIsNeutral = null;
			var remapDestModifierLR = ht.KeyToModifiersLR(remapDestVk, remapDestSc, ref remapDestIsNeutral);
			var releasedSourceModifiers = new List<string>();
			var restoredSourceModifiers = new List<string>();
			string[] modifierNames = ["LCtrl", "RCtrl", "LAlt", "RAlt", "LShift", "RShift", "LWin", "RWin"];

			for (var i = 0; i < 8; ++i)
				if ((remapSourceModifiersLR & (1 << i)) != 0 && (remapDestModifiersLR & (1u << i)) == 0)
				{
					blindMods += KeyboardMouseSender.ModLRString[i * 2];
					blindMods += KeyboardMouseSender.ModLRString[i * 2 + 1];

					// Selective Blind only releases a source modifier for the duration of one Send. If the remap
					// destination is itself a modifier, its useful lifetime extends through the separate up hotkey,
					// so explicitly release source-only modifiers there.
					if ((remapDestModifierLR & (1u << i)) == 0)
					{
						releasedSourceModifiers.Add(modifierNames[i]);

						if ((firmwareSourceModifiersLR & (1u << i)) == 0)
							restoredSourceModifiers.Add(modifierNames[i]);
					}
				}

			// A wheel source has no up event, so its up hotkey never fires and must not be relied upon to
			// release the destination; such a remap keeps the plain press-and-release form below.
			var explicitlyReleaseSourceModifiers = !remapWheel && remapDestModifierLR != 0 && releasedSourceModifiers.Count != 0;
			var downExtra = explicitlyReleaseSourceModifiers
				? string.Concat(releasedSourceModifiers.Select(mod => $"{{{mod} up}}"))
				: "";
			// Sided destination modifiers are pressed and released within this one Send, matching the neutral
			// prefix form which Send scopes the same way. Holding them across the whole remap would leave them
			// active while the source key is held, and releasing them in the up hotkey would clear a modifier
			// the user was holding independently.
			// The destination key's own token stays last: the macOS wrapper below rewrites the final brace to
			// mark an auto-repeat, and that marker only means anything on a down event. Sided destination
			// modifiers are therefore released by a following Send rather than being appended here.
			var p = explicitlyReleaseSourceModifiers
				? $"{{Blind}}{downExtra}{remapDestModifiersSend}{{{remapDest} DownR}}"
				: $"{{Blind{blindMods}}}{remapDestModifiersSend}{{{remapDest}{(remapWheel ? "" : " DownR")}}}";

#if OSX
			StatementSyntax RemapDownSend()
			{
				if (remapWheel)
					return SendStmt(p);

				// Preserve macOS's native press-and-hold behavior by marking only physical repeat
				// callbacks as repeat events. Send itself never consults A_EventInfo implicitly.
				var isAutoRepeat = Op("GetPropertyValue", Access("Keysharp.Builtins.Accessors.A_EventInfo"), Str("IsAutoRepeat"));
				var suffix = SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
					IfTest(isAutoRepeat), Str(" AutoRepeat}"), Str("}")));
				return SendStmt(Op("Concat", Str(p[..^1]), suffix));
			}
#else
			StatementSyntax RemapDownSend() => SendStmt(p);
#endif

			var downName = "__Remap_" + (++_hotCount);
			var upName = "__Remap_" + (++_hotCount);
			var downStatements = new List<StatementSyntax> { SetDelay(remapDestIsMouse) };
			if (remapKeybdToMouse && !remapWheel)
				downStatements.Add(SyntaxFactory.IfStatement(
					SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression,
						IfTest(Inv(Access("Keysharp.Builtins.Keyboard.GetKeyState"), Str(remapDest))), False),
					SyntaxFactory.Block(RemapDownSend())));
			else
				downStatements.Add(RemapDownSend());

			// Release sided destination modifiers straight after, so they are scoped to this one keystroke
			// exactly as Send scopes the neutral ^ ! + # prefixes, rather than staying down for as long as the
			// source key is held.
			if (destModifiersUp.Length != 0)
				downStatements.Add(SendStmt(Str($"{{Blind}}{destModifiersUp}")));
			downStatements.Add(SyntaxFactory.ReturnStatement(Str("")));
			_hotMembers.Add(RemapCallback(downName, downStatements));

			var upSendParts = new List<ExpressionSyntax> { Str($"{{Blind}}{{{remapDest} Up}}") };

			if (explicitlyReleaseSourceModifiers)
				foreach (var mod in restoredSourceModifiers)
					upSendParts.Add(
						SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(
							IfTest(Inv(Access("Keysharp.Builtins.Keyboard.GetKeyState"), Str(mod), Str("P"))),
							Str($"{{{mod} DownR}}"), Str(""))));

			var upSendText = upSendParts.Count == 1
				? upSendParts[0]
				: Inv(Access("System.String.Concat"), upSendParts.ToArray());

			_hotMembers.Add(RemapCallback(upName, new List<StatementSyntax>
				{ SetDelay(remapDestIsMouse), SendStmt(upSendText), SyntaxFactory.ReturnStatement(Str("")) }));

			_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Keyboard.HotkeyDefinition.AddHotkey"),
				FuncBind("__Main." + downName), UintZero, Str(remapSource))));
			_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Keyboard.HotkeyDefinition.AddHotkey"),
				FuncBind("__Main." + upName), UintZero, Str(remapSource + " up"))));
		}

		// `#Hotstring` directive: sets default options (`#Hotstring X`), end chars, or mouse-reset for subsequent
		// hotstrings (mirrors VisitHotstringDirective). Emitted into the DHHR so it precedes later registrations.
		private void LowerHotstringDirective(DirectiveStmt d)
		{
			var args = (d.Args ?? "").Trim();
			if (args.StartsWith("NoMouse", System.StringComparison.OrdinalIgnoreCase))
				_dhhr.Insert(0, CallStmt("Keysharp.Builtins.Keyboard.Hotstring", Str("MouseReset"), False));
			else if (args.StartsWith("EndChars", System.StringComparison.OrdinalIgnoreCase))
				_dhhr.Insert(0, CallStmt("Keysharp.Builtins.Keyboard.Hotstring", Str("EndChars"),
					Str(Keysharp.Parsing.Parser.EscapedString(args.Substring("EndChars".Length).Trim().Trim('"', '\''), false))));
			else
				_dhhr.Add(CallStmt("Keysharp.Builtins.Keyboard.Hotstring", Str(args.Trim('"', '\''))));
		}

		// `#HotIf <expr>` registers a hot-criterion: the condition becomes a callback (called with the hotkey name,
		// like a hotkey body) whose return value gates the following hotkeys/hotstrings; a bare `#HotIf` clears it
		// with `HotIf("")`. Emitted into the DHHR at this source position so it brackets the AddHotkey calls.
		private void LowerHotIf(DirectiveStmt d)
		{
			var args = (d.Args ?? "").Trim();
			if (args.Length == 0)
			{
				_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Builtins.Keyboard.HotIf"), Str(""))));
				return;
			}
			var cond = ParseExprFragment(args, out var err);
			if (cond == null) { Diag($"#HotIf condition is not a valid expression: '{args}'{(err == null ? "" : $" ({err})")}"); return; }
			var fnName = EmitHotCallback(new Block(new List<Stmt> { new ReturnStmt(cond) }), "__HotIf_" + (++_hotCount));
			_dhhr.Add(ExprStmt(Inv(Access("Keysharp.Builtins.Keyboard.HotIf"), FuncBind("__Main." + fnName))));
		}

		// Re-parses a directive's reconstructed expression argument (e.g. a `#HotIf` condition) back into an Expr,
		// or null if it is not exactly one expression. `error` is positioned within the fragment rather than the
		// script, so the caller folds it into its own message instead of reporting it as a diagnostic.
		private static Expr ParseExprFragment(string text, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(text)) return null;
			var (expr, diags) = Keysharp.Parsing.Syntax.Parser.ParseExpressionWithDiagnostics(text);
			if (diags.Count == 0) return expr;
			error = diags[0];
			return null;
		}

		private MemberDeclarationSyntax LowerFunction(FunctionDecl f)
		{
			var savedCompat = _currentCompat;
			_currentCompat = ScanRequires(f.Body?.Body) ?? _currentCompat;   // a `#Requires` in the body sets this function's mode
			var paramLowers = f.Params.Select(p => p.Name.ToLowerInvariant()).ToHashSet();
			var implName = NameMangler.FunctionMethod(f.Name);
			var body = LowerCallableBody(paramLowers, f.Body, f.ArrowBody, implName, f.Name, ByRefSet(f.Params), f.Params);
			var attrs = new List<AttributeListSyntax> { Attr("Keysharp.Runtime.UserDeclaredName", Str(f.Name)) };
			attrs.AddRange(CompatAttr());
			var method = ObjMethod(implName, ParamDecls(f.Params, includeThis: false, wrapVariadics: true), body, attrs.ToArray());
			_currentCompat = savedCompat;
			return method;
		}

		// Emits a C# parameter list. name:=literal -> [Optional, DefaultParameterValue]; name? -> [Optional];
		// name* -> params object[]. By-ref '&name' params get the [ByRef] attribute (a VarRef is passed).
		// The variadic param is exposed to the body as an AHK Array; the raw `params object[]` is the `KS_`-prefixed
		// signature param, and LowerCallableBody prepends `<name> = new Array(KS_<name>)` (see VariadicWrap).
		// The `KS_` prefix already guarantees a non-keyword identifier, so do NOT @-escape (a variadic param named
		// `params` must become `KS_params`, never the invalid `KS_@params`).
		private static string VariadicRawName(string name) => "KS_" + name.ToLowerInvariant();

		private ParameterListSyntax ParamDecls(List<Param> ps, bool includeThis, bool wrapVariadics = false)
		{
			var list = new List<ParameterSyntax>();
			if (includeThis) list.Add(ThisParam());
			foreach (var p in ps)
			{
				var lowered = p.Name.ToLowerInvariant();
				var ident = SyntaxFactory.Identifier(NameMangler.Escape(lowered));
				ParameterSyntax param;
				if (p.Variadic)
					param = SyntaxFactory.Parameter(wrapVariadics ? SyntaxFactory.Identifier(VariadicRawName(p.Name)) : ident)
						.WithType(ObjArrayType).AddModifiers(SyntaxFactory.Token(SyntaxKind.ParamsKeyword));
				else
				{
					param = SyntaxFactory.Parameter(ident).WithType(ObjType);
					// The emitted identifier is lower-cased, and named-argument binding then matches it
					// case-insensitively -- which works for every identifier whose case folds symmetrically. A few do
					// not: U+1E9E LATIN CAPITAL LETTER SHARP S lower-cases to U+00DF, which OrdinalIgnoreCase does NOT
					// match back, so `f(ẞ: 1)` would fail to find its own parameter. Worse, which characters behave this
					// way depends on the runtime's Unicode tables (ICU vs NLS), so it cannot be enumerated here.
					// When the fold does not round-trip, carry the user's spelling so the name written in the script
					// binds regardless. Emitted rarely -- an ordinary `Foo`/`foo` pair round-trips fine.
					if (!string.Equals(p.Name, lowered, System.StringComparison.OrdinalIgnoreCase))
						param = param.AddAttributeLists(Attr("Keysharp.Runtime.UserDeclaredName", Str(p.Name)));
					if (p.ByRef) param = param.AddAttributeLists(Attr("Keysharp.Runtime.ByRef"));   // &name: receives a VarRef
					// Optionality is independent of by-ref: `&name?` / `&name:=v` are optional by-ref params, so the
					// [Optional]/[DefaultParameterValue] attributes must still be applied (else the runtime counts them
					// as required and rejects calls that omit them).
					if (p.Default != null)
					{
						var d = LowerParamDefault(p.Default);
						// A constant default goes straight into the attribute; a non-constant one defaults to null and gets a
						// `param ??= <expr>` prologue. A by-ref default is ALWAYS declared null so an omitted `&p` arrives null
						// (its default is substituted into the VarRef in LowerCallableBody, never via the attribute).
						param = param.AddAttributeLists(Attr("System.Runtime.InteropServices.Optional"),
													   Attr("System.Runtime.InteropServices.DefaultParameterValue", d is LiteralExpressionSyntax && !p.ByRef ? d : Null));
					}
					else if (p.Optional) param = param.AddAttributeLists(Attr("System.Runtime.InteropServices.Optional"));
				}
				list.Add(param);
			}
			return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(list));
		}

		private ExpressionSyntax LowerParamDefault(Expr expr)
		{
			var saved = _loweringParamDefault;
			_loweringParamDefault = true;
			var result = LowerExpr(expr);
			_loweringParamDefault = saved;
			return result;
		}

		// funcName is the mangled C# method name used for static-local field mangling. thisFuncName is the exact
		// AHK-visible name used by A_ThisFunc and by an externally visible function scope.
		private BlockSyntax LowerCallableBody(HashSet<string> paramLowers, Block bodyBlock, Expr arrowBody, string funcName,
			string thisFuncName, HashSet<string> byRefParams = null, List<Param> paramDefaults = null,
			bool capturing = false, bool staticNested = false)
		{
			// A `static` nested function resolves sibling nested-function names (so `static f2() => f1()` works) and can
			// still access the enclosing function's STATIC variables (shared across calls), but does NOT capture the
			// enclosing function's non-static LOCALS (AHK semantics) — varCapture covers only the local capture.
			bool varCapture = capturing && !staticNested;
			var savedThisFuncName = _currentThisFuncName;
			var savedLoweringParamDefault = _loweringParamDefault;
			_currentThisFuncName = thisFuncName;
			_loweringParamDefault = false;
			var savedByRef = _byRefParams;
			_byRefParams = byRefParams;
			var savedTemps = _scopeTemps; _scopeTemps = new();
			var savedScopeFuncs = _pendingScopeFuncs; _pendingScopeFuncs = new();   // fat-arrow local funcs for THIS scope
			var savedClosureInits = _pendingScopeClosureInits; _pendingScopeClosureInits = new();
			var forcedGlobals = new HashSet<string>();
			var explicitLocals = new HashSet<string>();
			var statics = new Dictionary<string, string>();
			if (bodyBlock != null) foreach (var st in bodyBlock.Body) PrescanDecls(st, forcedGlobals, explicitLocals, statics, funcName);

			var assignedOrdered = new List<string>();
			var seen = new HashSet<string>();
			if (arrowBody != null) CollectAssignedExpr(arrowBody, assignedOrdered, seen);
			else if (bodyBlock != null) foreach (var st in bodyBlock.Body) CollectAssignedStmt(st, assignedOrdered, seen);

			var savedLocals = _locals; var savedForced = _forcedGlobals; var savedStatics = _statics;
			// Only a CLOSURE (fat-arrow / bare nested function) captures the enclosing scope's locals by reference; a
			// module-level function/method does NOT (it's not lexically a C# local function), so reset to empty there.
			var savedEnclosing = _enclosingLocals;
			_enclosingLocals = !varCapture ? new HashSet<string>()
				: savedLocals == null ? savedEnclosing
				: new HashSet<string>(savedEnclosing.Concat(savedLocals));
			// Any nested function (static or not) sees the enclosing scope's statics (module-level SL_ fields) for both
			// derefs and bare references — they're shared across calls, not per-call locals, so a `static` nested function
			// keeps access to them even though it forgoes the enclosing non-static locals above.
			var savedEnclosingStatics = _enclosingStatics;
			_enclosingStatics = !capturing ? new Dictionary<string, string>(System.StringComparer.Ordinal)
				: new Dictionary<string, string>(savedEnclosingStatics.Concat(savedStatics ?? Enumerable.Empty<KeyValuePair<string, string>>())
					.GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value), System.StringComparer.Ordinal);
			var savedInStaticNested = _inStaticNested;
			_inStaticNested = staticNested;
			// A bare `global` declaration switches the function to assume-global: assigned variables default
			// to global storage unless explicitly declared local/static.
			bool assumeGlobal = bodyBlock != null && bodyBlock.Body.Any(st => st is DeclStmt d && d.Keyword == "global" && d.Items.Count == 0);
			_forcedGlobals = forcedGlobals;
			_statics = statics;
			var savedScopeClosureNames = _scopeClosureNames;
			// Bare nested-function names declared in THIS scope's body (become local closure vars, hoisted below).
			var ownClosureNames = new HashSet<string>(System.StringComparer.Ordinal);
			if (bodyBlock != null) foreach (var st in bodyBlock.Body) CollectBareNestedFuncNames(st, ownClosureNames);
			// A capturing scope (fat-arrow / bare nested function) ALSO sees the enclosing scope's nested-closure names, so
			// a reference to a sibling/enclosing closure resolves to the captured local rather than a module field.
			_scopeClosureNames = capturing ? new HashSet<string>(savedScopeClosureNames, System.StringComparer.Ordinal)
											: new HashSet<string>(System.StringComparer.Ordinal);
			_scopeClosureNames.UnionWith(ownClosureNames);
			// This callable's own `#import` frame: its body's imports (control-flow-nested included, nested callables
			// excluded — those build their own frames). Built and pushed here — BEFORE local/static classification — so a
			// name it explicitly binds is not turned into a fresh local by an assignment to it (which would shadow the
			// import and defeat write-through). A nested closure lowered below inherits the frame via the still-pushed
			// stack. Names bound by the frame:
			var bodyImports = new List<ImportDirective>();
			if (bodyBlock != null) foreach (var st in bodyBlock.Body) CollectNestedImports(st, bodyImports);
			var importFrame = bodyImports.Count > 0 ? BuildImportScope(bodyImports) : null;
			if (importFrame != null) _importScopes.Add(importFrame);
			// The exemption consults the FULL enclosing import stack — this body's own frame PLUS any enclosing class /
			// function frame (e.g. a `#import "Mod" { export }` in the class body, read/written by a method) — not just the
			// own frame. Otherwise a name bound only by an enclosing frame is reclassified as a fresh local on assignment,
			// severing the write-through to the module variable. See BoundByEnclosingImport.
			// A bare `static` declaration switches the function to assume-static: undeclared assigned variables become
			// per-function STATIC locals (persisting across calls) rather than fresh locals — they need backing SL_ fields
			// like explicit statics. `global` wins if both are present. Params, explicit locals, and nested-closure names
			// are excluded (the latter so `_statics` doesn't shadow the closure var in NameRef).
			bool assumeStatic = !assumeGlobal && bodyBlock != null
				&& bodyBlock.Body.Any(st => st is DeclStmt d && d.Keyword == "static" && d.Items.Count == 0);
			if (assumeStatic)
				foreach (var n in assignedOrdered)
					if (!forcedGlobals.Contains(n) && !statics.ContainsKey(n) && !explicitLocals.Contains(n)
						&& !paramLowers.Contains(n) && !_enclosingLocals.Contains(n) && !ownClosureNames.Contains(n) && !BoundByEnclosingImport(n))
					{
						var field = NameMangler.StaticLocalField(funcName, n);
						statics[n] = field;
						_staticFieldDeclIdx[field] = _staticFieldSink.Count;
						_staticFieldSink.Add(ObjField(field, Null));
					}
			_locals = new HashSet<string>(paramLowers);
			if (!assumeGlobal)
				foreach (var n in assignedOrdered)
					if (!forcedGlobals.Contains(n) && !statics.ContainsKey(n) && !_enclosingLocals.Contains(n) && !BoundByEnclosingImport(n)) _locals.Add(n);
			foreach (var n in explicitLocals) _locals.Add(n);
			// In a method (or a closure nested in one), `this` is always the special @this (the receiver), never a local —
			// even when assigned (`this := 0`), which reassigns the captured receiver. Don't shadow it with a null local.
			if (_inMethod) _locals.Remove("this");

			var body = new List<StatementSyntax>();
			var hoisted = new HashSet<string>(paramLowers);
			// Emitted names whose managed lifetime must span the scope.
			var declared = new List<string>();
			void Hoist(string n) { if (_locals.Contains(n) && hoisted.Add(n)) { var e = NameMangler.Escape(n); declared.Add(e); body.Add(DeclLocal(ObjType, e, Null)); } }
			foreach (var n in assignedOrdered) Hoist(n);
			foreach (var n in explicitLocals) Hoist(n);
			// Declare this scope's nested-closure vars up front (`object f = null;`) so a self-/mutually-recursive binding
			// `f = Closure(FN_f)` (where FN_f captures f) satisfies C# definite assignment.
			foreach (var n in ownClosureNames) if (hoisted.Add(n)) { var e = NameMangler.Escape(n); declared.Add(e); body.Add(DeclLocal(ObjType, e, Null)); }

			// Expose a variadic param to the body as an AHK Array: `object <name> = new Array(KS_<name>)` (matches the
			// canonical). The raw `params object[]` keeps the `KS_`-prefixed signature name (see ParamDecls). Declared
			// BEFORE the deref GetVar/SetVar functions, which may reference it by name.
			if (paramDefaults != null)
				foreach (var p in paramDefaults)
					if (p.Variadic)
						body.Add(LocalDecl(ObjType, NameMangler.Escape(p.Name.ToLowerInvariant()),
							SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Builtins.Array"))
								.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(Id(VariadicRawName(p.Name))))))));

			// If the body dereferences (%name%), route those reads/writes through this scope's reader/writer delegates
			// (KS_readVar/KS_writeVar, declared in the prologue below); a name that isn't a local/static/closure falls
			// back to MainScript.ModuleData.Vars. The same reader also backs external access (callouts, ListVars).
			var savedDeref = _inDerefFunc;
			_inDerefFunc = BodyHas(bodyBlock, arrowBody, IsDeref);
			// A function that calls a scope-consuming builtin also publishes its scope, even when the body never uses
			// %name%. RegExMatch/RegExReplace only need it when the function HAS closures a callout could resolve by name
			// (`_scopeClosureNames` — own nested functions plus captured enclosing ones); with none, the callout resolves
			// globally and the scope would be pointless. ListVars needs it regardless, to display the function's locals.
			// (LowerCallableBody is never the global scope, where globals are already shown.) Such a function publishes
			// only the reader — with no in-body %name% write it needs no writer.
			bool callsScopeApi = BodyHas(bodyBlock, arrowBody, IsListVarsTrigger)
				|| (_scopeClosureNames.Count > 0 && BodyHas(bodyBlock, arrowBody, IsRegexTrigger));
			bool exposeScope = _inDerefFunc || callsScopeApi;
			// A writer is only needed when the body assigns a dynamic name (`%n% := v`, `%n%++`, `&%n%`); a read-only
			// scope (including the RegExMatch/ListVars triggers) is a single reader function.
			// INVARIANT: IsDerefWrite (via BodyHas) must match EVERY place the deref lowering can emit a DerefSet —
			// i.e. wherever a DerefExpr appears in a write/ref position under `_inDerefFunc` (see LowerAssign, the ++/--
			// path, and MakeRefFor's DerefExpr case). If a reachable write is missed here, KS_writeVar is never
			// declared yet the body references it → CS0103. So a new statement/expression form in BodyHas's walk must
			// stay in lockstep with the lowering's traversal (that is why BodyHas includes SpecialLoopStmt).
			bool needsWriter = exposeScope && BodyHas(bodyBlock, arrowBody, IsDerefWrite);
			if (exposeScope)
			{
				// The reader (and writer, if any) lambdas, bound to typed delegates so the in-body %name% access and
				// the scope external callers read share the one delegate, then publish the scope. KeysharpFunc.Call
				// clears/restores executingUserFunc around the call, so no matching teardown is emitted here.
				body.Add(LocalDecl(Ty("Keysharp.Runtime.FuncScope.Reader"), "KS_readVar", BuildReaderLambda()));
				if (needsWriter) body.Add(LocalDecl(Ty("Keysharp.Runtime.FuncScope.Writer"), "KS_writeVar", BuildWriterLambda()));
				body.Add(ExprStmt(Inv(Access("Keysharp.Runtime.Script.EnterScope"), Id("KS_readVar"), ScopeNamesLambda(), Str(thisFuncName ?? ""))));
			}

			// A by-ref param is read/written through its VarRef's __Value. Only when the caller OMITS an optional `&p`
			// does it arrive null (by-ref defaults are declared as null, see ParamDecls) — substitute a VarRef holding
			// the declared default (or "") so __Value access behaves like a local. A passed argument is left untouched:
			// it may be a real VarRef OR a "virtual reference" (an object exposing `.Value`/`__Value` but not derived
			// from VarRef) — re-wrapping the latter in a new VarRef would sever it from its backing store.
			if (paramDefaults != null)
				foreach (var p in paramDefaults)
					if (p.ByRef && !p.Variadic)
					{
						var rid = Id(NameMangler.Escape(p.Name.ToLowerInvariant()));
						var deflt = p.Default != null ? LowerParamDefault(p.Default) : Str("");
						body.Add(ExprStmt(SyntaxFactory.AssignmentExpression(SyntaxKind.CoalesceAssignmentExpression, rid,
							SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Builtins.VarRef"))
								.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(deflt)))))));
					}

			// Non-literal parameter defaults: the param is declared [Optional, DefaultParameterValue(null)],
			// so assign the real default when the caller omitted it (`param ??= <expr>`).
			if (paramDefaults != null)
				foreach (var p in paramDefaults)
					if (!p.ByRef && !p.Variadic && p.Default != null)
					{
						var defaultExpr = LowerParamDefault(p.Default);
						if (defaultExpr is not LiteralExpressionSyntax)
							body.Add(ExprStmt(SyntaxFactory.AssignmentExpression(SyntaxKind.CoalesceAssignmentExpression,
								Id(NameMangler.Escape(p.Name.ToLowerInvariant())), defaultExpr)));
					}

			// A fat-arrow `=> expr` propagates an unset result rather than raising (unlike an explicit `return f()`),
			// so its outermost call/member/index uses the non-raising *OrNull form.
			var setupEnd = body.Count;   // boundary after local hoists + param setup, before the executable body
			if (arrowBody != null) body.Add(SyntaxFactory.ReturnStatement(RewriteToOrNull(LowerExpr(arrowBody))));
			else if (bodyBlock != null) body.AddRange(LowerStmtList(bodyBlock.Body));

			if (body.Count == 0 || body[^1] is not ReturnStatementSyntax)
				body.Add(SyntaxFactory.ReturnStatement(DefaultReturnExpr()));

			int execStart = setupEnd, execEnd = body.Count;
			// Declare any temps introduced by postfix ++/-- at the top of the scope.
			for (int i = _scopeTemps.Count - 1; i >= 0; i--) body.Insert(0, DeclLocal(ObjType, _scopeTemps[i], Null));
			execStart += _scopeTemps.Count; execEnd += _scopeTemps.Count;
			// Emit fat-arrow local functions for this scope (C# hoists them, so position doesn't matter).
			body.AddRange(_pendingScopeFuncs);
			// Bind named nested functions just below the local hoists (so captured locals are definitely-assigned before
			// a closure is converted to a delegate — C# CS0165) but above the body (so forward-referenced calls work).
			body.InsertRange(setupEnd + _scopeTemps.Count, _pendingScopeClosureInits);
			execStart += _pendingScopeClosureInits.Count; execEnd += _pendingScopeClosureInits.Count;
			WrapBodyWithKeepAlive(body, declared, execStart, execEnd);

			_inDerefFunc = savedDeref;
			_locals = savedLocals; _forcedGlobals = savedForced; _statics = savedStatics; _byRefParams = savedByRef;
			_scopeClosureNames = savedScopeClosureNames;
			_scopeTemps = savedTemps; _pendingScopeFuncs = savedScopeFuncs; _enclosingLocals = savedEnclosing;
			_pendingScopeClosureInits = savedClosureInits; _enclosingStatics = savedEnclosingStatics;
			_inStaticNested = savedInStaticNested;
			_currentThisFuncName = savedThisFuncName;
			_loweringParamDefault = savedLoweringParamDefault;
			if (importFrame != null) _importScopes.RemoveAt(_importScopes.Count - 1);
			return SyntaxFactory.Block(body);
		}

		private string ClassMemberFuncName(string memberName, bool isStatic) =>
			$"{_currentClassUserPath}.{(isStatic ? "" : "Prototype.")}{memberName}";

		// AHK keeps locals alive to scope exit; the JIT may otherwise collect a Buffer after its last managed
		// read while native code still uses its Ptr. KeepAlive restores that lifetime on every exit path.
		private static void WrapBodyWithKeepAlive(List<StatementSyntax> body, List<string> declared, int execStart, int execEnd)
		{
			if (declared.Count == 0 || execEnd <= execStart)
				return;

			var keep = new List<StatementSyntax>(declared.Count);

			foreach (var n in declared)
				keep.Add(ExprStmt(Inv(Access("System.GC.KeepAlive"), Id(n))));

			var inner = body.GetRange(execStart, execEnd - execStart);
			body.RemoveRange(execStart, execEnd - execStart);
			body.Insert(execStart, SyntaxFactory.TryStatement(
										SyntaxFactory.Block(inner),
										default,
										SyntaxFactory.FinallyClause(SyntaxFactory.Block(keep))));
		}

		// The reader and writer over this scope's variables, each a lambda (assigned to a FuncScope.Reader/Writer
		// delegate in the prologue) whose body is a constant-string switch expression — one arm per variable; Roslyn
		// lowers it to a hash jump table, so resolution is O(1). A name that isn't one of the function's variables
		// yields Script.DerefMiss, the signal for the runtime (DerefGet/DerefSet, FuncScope) to fall back to the global
		// store:
		//   FuncScope.Reader KS_readVar  = KS_name           => key switch { "a" => a, ..., _ => Script.DerefMiss };
		//   FuncScope.Writer KS_writeVar = (KS_name, KS_val) => key switch { "a" => a = KS_val, ..., _ => Script.DerefMiss };
		// The writer is emitted only when the body writes a dynamic name, so a read-only scope is a single reader.
		private ExpressionSyntax BuildReaderLambda() =>
			SyntaxFactory.SimpleLambdaExpression(SyntaxFactory.Parameter(SyntaxFactory.Identifier("KS_name")), BuildScopeSwitch(isWriter: false));
		private ExpressionSyntax BuildWriterLambda() =>
			SyntaxFactory.ParenthesizedLambdaExpression(
				SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Parameter(SyntaxFactory.Identifier("KS_name")), SyntaxFactory.Parameter(SyntaxFactory.Identifier("KS_val")) })),
				BuildScopeSwitch(isWriter: true));

		// The reader switch reads every arm; the writer switch assigns only arms with a write target (a read-only
		// import arm is omitted, so a dynamic write to it falls through to DerefMiss / the global store).
		private ExpressionSyntax BuildScopeSwitch(bool isWriter)
		{
			var swArms = new List<SwitchExpressionArmSyntax>();
			foreach (var a in DerefArms())
			{
				var target = isWriter ? a.write : a.read;
				if (target == null) continue;
				swArms.Add(SyntaxFactory.SwitchExpressionArm(SyntaxFactory.ConstantPattern(Str(a.key)), isWriter ? Assign(target, Id("KS_val")) : target));
			}
			swArms.Add(SyntaxFactory.SwitchExpressionArm(SyntaxFactory.DiscardPattern(), Access("Keysharp.Runtime.Script.DerefMiss")));
			return SyntaxFactory.SwitchExpression(LowerKey(), SyntaxFactory.SeparatedList(swArms));
		}

		// `%name%` read/write inside a deref function body, routed through the runtime so the call sites stay tiny;
		// KS_readVar/KS_writeVar are this function's reader/writer delegates (the prologue lambdas).
		private static ExpressionSyntax DerefGet(ExpressionSyntax name) => Op("DerefGet", Id("KS_readVar"), name);
		private static ExpressionSyntax DerefSet(ExpressionSyntax name, ExpressionSyntax value) => Op("DerefSet", Id("KS_writeVar"), name, value);

		// The (key -> read/write target) arms shared by the reader/writer switches, in NameRef's resolution priority
		// order (locals, statics, this scope's closures, captured enclosing locals/statics, then scoped `#import`
		// names), deduped so the switch never emits a duplicate `case` label. `write` is null for a read-only arm.
		private List<(string key, ExpressionSyntax read, ExpressionSyntax write)> DerefArms()
		{
			var arms = new List<(string, ExpressionSyntax, ExpressionSyntax)>();
			var seen = new HashSet<string>(System.StringComparer.Ordinal);
			void Arm(string key, ExpressionSyntax read, ExpressionSyntax write) { if (seen.Add(key)) arms.Add((key, read, write)); }
			void LValue(string key) { var id = Id(NameMangler.Escape(key)); Arm(key, id, id); }
			foreach (var n in _locals) LValue(n);
			foreach (var kv in _statics) { var id = Id(kv.Value); Arm(kv.Key, id, id); }
			// Named nested functions in this scope are local closure vars (not in _locals); exposing them here lets a
			// dynamic `%name%` — and external callers resolving by name — reach the closure rather than a global.
			foreach (var n in _scopeClosureNames) LValue(n);
			// Captured enclosing-scope locals and statics, after this scope's own, so a nested closure's `%name%`
			// reaches the enclosing function's variables, not just globals.
			foreach (var n in _enclosingLocals) LValue(n);
			foreach (var kv in _enclosingStatics) { var id = Id(kv.Value); Arm(kv.Key, id, id); }
			// Scoped `#import` names last (locals/statics of the same name shadow them, matching NameRef). Explicit
			// bindings first, then each wildcard's members fully expanded — a built-in wildcard member is not reachable
			// via the runtime global store (Ks is not in the reflection tables), so it MUST be an arm here. Read-only
			// members contribute a read arm only. Walk every frame on the stack so an enclosing scope's imports resolve.
			for (int i = _importScopes.Count - 1; i >= 0; i--)
			{
				var frame = _importScopes[i];
				foreach (var kv in frame.Named) Arm(kv.Key, kv.Value.Read(), kv.Value.Write?.Invoke());
				for (int w = frame.Wildcards.Count - 1; w >= 0; w--)
				{
					var wc = frame.Wildcards[w];
					if (wc.Script != null)
						foreach (var ex in wc.Script.Exports)
							Arm(ex.Key.ToLowerInvariant(), ModuleMemberField(wc.ModName, ex.Key), ex.Value == ExportK.Variable ? ModuleMemberField(wc.ModName, ex.Key) : null);
					else if (wc.BuiltinType != null)
						foreach (var nm in BuiltinMemberNames(wc.BuiltinType))
							if (BindBuiltinMember(wc.ModName, wc.IsAhk, nm) is { } expr) Arm(nm, expr, null);
				}
			}
			return arms;
		}

		// MainScript.ModuleData.Vars[name] — the global dynamic-variable store. Read and write go through the same
		// C# indexer so a deref write (`%n% := v`, `y%x%++`) and a later read see the same value (the field slot).
		private static ExpressionSyntax VarsIndex(ExpressionSyntax name) =>
			SyntaxFactory.ElementAccessExpression(Access("MainScript.ModuleData.Vars"))
				.WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(name))));

		// A bare assignment (not parenthesized) so it's valid both as a statement-expression / lambda body and
		// as a sub-expression (e.g. inside MultiStatement(...) or `z := y%x%++`).
		private static ExpressionSyntax VarsWrite(ExpressionSyntax name, ExpressionSyntax value) => Assign(VarsIndex(name), value);

		private static ExpressionSyntax LowerKey() => Inv(Member(Op("ForceString", Id("KS_name")), "ToLowerInvariant"));

		private static ParameterSyntax Param(string name, TypeSyntax type) =>
			SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)).WithType(type);

		// `() => new string[] { "<name>", ... }` over this scope's variable names — passed to Script.EnterScope so
		// ListVars can enumerate the running function's locals. Non-capturing, so the C# compiler caches the delegate;
		// the array is only built if something actually enumerates the scope.
		private ExpressionSyntax ScopeNamesLambda() =>
			SyntaxFactory.ParenthesizedLambdaExpression().WithExpressionBody(StrArrayCreation(DerefArms().Select(a => a.key)));

		private static ExpressionSyntax StrArrayCreation(IEnumerable<string> values)
		{
			var arrType = SyntaxFactory.ArrayType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))
				.WithRankSpecifiers(SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
					SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression()))));
			return SyntaxFactory.ArrayCreationExpression(arrType,
				SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
					SyntaxFactory.SeparatedList(values.Select(v => (ExpressionSyntax)Str(v)))));
		}

		// Generic "does any node in this scope's OWN body satisfy <pred>?" walk, shared by the deref / scope-trigger /
		// deref-write checks below. It walks control flow and the whole expression tree but NOT into nested
		// functions/closures (no FatArrowExpr / FunctionDecl cases) — a nested construct belongs to that nested scope,
		// which is analysed when it is lowered. <pred> is tested on every node; the switch only handles recursion.
		private static bool BodyHas(Block body, Expr arrow, Func<Expr, bool> pred) =>
			(body != null && body.Body.Any(s => AnyStmt(s, pred))) || AnyExpr(arrow, pred);

		private static bool AnyStmt(Stmt s, Func<Expr, bool> pred) => s switch
		{
			ExpressionStmt es => AnyExpr(es.Expr, pred),
			ReturnStmt r => AnyExpr(r.Value, pred),
			Block b => b.Body.Any(x => AnyStmt(x, pred)),
			IfStmt iff => AnyExpr(iff.Cond, pred) || AnyStmt(iff.Then, pred) || (iff.Else != null && AnyStmt(iff.Else, pred)),
			// Each loop also lowers its trailing `Until` condition (WrapLoopBody) and `Else` body (LoopFinally), so a
			// %name% / scope-trigger / deref-write confined to either must be seen here too — else _inDerefFunc /
			// needsWriter would be wrong (mislowering, or a missing KS_writeVar → CS0103). AnyExpr tolerates nulls.
			WhileStmt w => AnyExpr(w.Cond, pred) || AnyStmt(w.Body, pred) || AnyExpr(w.Until, pred) || (w.Else != null && AnyStmt(w.Else, pred)),
			LoopStmt lp => AnyExpr(lp.Count, pred) || AnyStmt(lp.Body, pred) || AnyExpr(lp.Until, pred) || (lp.Else != null && AnyStmt(lp.Else, pred)),
			ForStmt fr => AnyExpr(fr.Enumerable, pred) || AnyStmt(fr.Body, pred) || AnyExpr(fr.Until, pred) || (fr.Else != null && AnyStmt(fr.Else, pred)),
			SpecialLoopStmt slp => (slp.Args != null && slp.Args.Any(a => AnyExpr(a, pred))) || AnyStmt(slp.Body, pred) || AnyExpr(slp.Until, pred) || (slp.Else != null && AnyStmt(slp.Else, pred)),
			SwitchStmt sw => AnyExpr(sw.Value, pred) || sw.Cases.Any(c => c.Values.Any(v => AnyExpr(v, pred)) || c.Body.Any(x => AnyStmt(x, pred))) || (sw.Default != null && sw.Default.Any(x => AnyStmt(x, pred))),
			TryStmt tr => AnyStmt(tr.Body, pred) || tr.Catches.Any(cb => AnyStmt(cb.Body, pred)) || (tr.Else != null && AnyStmt(tr.Else, pred)) || (tr.Finally != null && AnyStmt(tr.Finally, pred)),
			ThrowStmt th => AnyExpr(th.Value, pred),
			DeclStmt d => d.Items.Any(x => AnyExpr(x, pred)),
			_ => false
		};

		private static bool AnyExpr(Expr e, Func<Expr, bool> pred) => e != null && (pred(e) || e switch
		{
			BinaryExpr b => AnyExpr(b.Left, pred) || AnyExpr(b.Right, pred),
			UnaryExpr u => AnyExpr(u.Operand, pred),
			AssignExpr a => AnyExpr(a.Target, pred) || AnyExpr(a.Value, pred),
			TernaryExpr t => AnyExpr(t.Cond, pred) || AnyExpr(t.Then, pred) || AnyExpr(t.Else, pred),
			GroupExpr g => AnyExpr(g.Inner, pred),
			SequenceExpr seq => seq.Items.Any(x => AnyExpr(x, pred)),
			CallExpr c => AnyExpr(c.Callee, pred) || c.Args.Any(ar => AnyExpr(ar.Value, pred)),
			MemberExpr m => AnyExpr(m.Target, pred),
			DynMemberExpr dm => AnyExpr(dm.Target, pred) || AnyExpr(dm.NameExpr, pred),
			IndexExpr ix => AnyExpr(ix.Target, pred) || ix.Args.Any(ar => AnyExpr(ar.Value, pred)),
			ArrayExpr ar => ar.Elements.Any(el => AnyExpr(el.Value, pred)),
			ObjectExpr o => o.Entries.Any(en => AnyExpr(en.Key, pred) || AnyExpr(en.Value, pred)),
			_ => false
		});

		// A %name% dereference — in-body reads/writes route through the function's scope (DerefGet/DerefSet).
		private static bool IsDeref(Expr e) => e is DerefExpr;

		// Calls to builtins that consume the executing-function scope, even when the body never uses %name%.
		// RegExMatch/RegExReplace: a callout can resolve this function's closures by name — but only worth a scope when
		// the function actually has closures (gated at the call site). ListVars: display this function's locals.
		private static readonly HashSet<string> RegexTriggerCallees = new(System.StringComparer.OrdinalIgnoreCase)
		{ "RegExMatch", "RegExReplace" };
		private static bool IsRegexTrigger(Expr e) => e is CallExpr c && c.Callee is NameExpr ne && RegexTriggerCallees.Contains(ne.Name);
		private static bool IsListVarsTrigger(Expr e) => e is CallExpr c && c.Callee is NameExpr ne && ne.Name.Equals("ListVars", System.StringComparison.OrdinalIgnoreCase);

		// An assignment to a dynamically-named variable (`%n% := v` / `%n% op= v`, `%n%++/--`, `&%n%`) — i.e. the scope
		// needs a WRITER, not just a reader.
		private static bool IsDerefWrite(Expr e) =>
			(e is AssignExpr a && a.Target is DerefExpr)
			|| (e is UnaryExpr u && (u.Op == "++" || u.Op == "--" || u.Op == "&") && u.Operand is DerefExpr);

		private void PrescanDecls(Stmt s, HashSet<string> fg, HashSet<string> el, Dictionary<string, string> st, string funcName)
		{
			switch (s)
			{
				case DeclStmt d:
					foreach (var item in d.Items)
					{
						var name = DeclItemName(item);
						if (name == null) continue;
						var lower = name.ToLowerInvariant();
						switch (d.Keyword)
						{
							case "global": fg.Add(lower); break;
							case "local": el.Add(lower); break;
							case "static":
								if (!st.ContainsKey(lower))
								{
									var field = NameMangler.StaticLocalField(funcName, lower);
									st[lower] = field;
									_staticFieldDeclIdx[field] = _staticFieldSink.Count;
									_staticFieldSink.Add(ObjField(field, Null));
								}
								break;
						}
					}
					break;
				case Block b: foreach (var x in b.Body) PrescanDecls(x, fg, el, st, funcName); break;
				case IfStmt iff: PrescanDecls(iff.Then, fg, el, st, funcName); if (iff.Else != null) PrescanDecls(iff.Else, fg, el, st, funcName); break;
				case WhileStmt w: PrescanDecls(w.Body, fg, el, st, funcName); if (w.Else != null) PrescanDecls(w.Else, fg, el, st, funcName); break;
				case LoopStmt lp: PrescanDecls(lp.Body, fg, el, st, funcName); if (lp.Else != null) PrescanDecls(lp.Else, fg, el, st, funcName); break;
				case SpecialLoopStmt slp: PrescanDecls(slp.Body, fg, el, st, funcName); if (slp.Else != null) PrescanDecls(slp.Else, fg, el, st, funcName); break;
				case ForStmt fr: PrescanDecls(fr.Body, fg, el, st, funcName); if (fr.Else != null) PrescanDecls(fr.Else, fg, el, st, funcName); break;
				case SwitchStmt sw:
					foreach (var c in sw.Cases) foreach (var s2 in c.Body) PrescanDecls(s2, fg, el, st, funcName);
					if (sw.Default != null) foreach (var s2 in sw.Default) PrescanDecls(s2, fg, el, st, funcName);
					break;
				case TryStmt tr:
					PrescanDecls(tr.Body, fg, el, st, funcName);
					foreach (var cb in tr.Catches) PrescanDecls(cb.Body, fg, el, st, funcName);
					if (tr.Else != null) PrescanDecls(tr.Else, fg, el, st, funcName);
					if (tr.Finally != null) PrescanDecls(tr.Finally, fg, el, st, funcName);
					break;
			}
		}

		// ---- collect assigned (local) names ----

		private static void CollectAssignedStmt(Stmt s, List<string> acc, HashSet<string> seen)
		{
			switch (s)
			{
				case ExpressionStmt es: CollectAssignedExpr(es.Expr, acc, seen); break;
				// A declaration's initializers may contain nested assignments / `&`-refs (`local a := (b := 5)`); those
				// targets are function-locals too. The declared names themselves are registered by PrescanDecls.
				case DeclStmt d: foreach (var item in d.Items) CollectAssignedExpr(item, acc, seen); break;
				case ReturnStmt r: if (r.Value != null) CollectAssignedExpr(r.Value, acc, seen); break;
				case Block b: foreach (var x in b.Body) CollectAssignedStmt(x, acc, seen); break;
				case IfStmt iff:
					CollectAssignedExpr(iff.Cond, acc, seen);
					CollectAssignedStmt(iff.Then, acc, seen);
					if (iff.Else != null) CollectAssignedStmt(iff.Else, acc, seen);
					break;
				case WhileStmt w: CollectAssignedExpr(w.Cond, acc, seen); CollectAssignedStmt(w.Body, acc, seen); if (w.Else != null) CollectAssignedStmt(w.Else, acc, seen); break;
				case LoopStmt lp: if (lp.Count != null) CollectAssignedExpr(lp.Count, acc, seen); CollectAssignedStmt(lp.Body, acc, seen); if (lp.Else != null) CollectAssignedStmt(lp.Else, acc, seen); break;
				case SpecialLoopStmt slp:
					if (slp.Args != null) foreach (var a in slp.Args) if (a != null) CollectAssignedExpr(a, acc, seen);
					CollectAssignedStmt(slp.Body, acc, seen); if (slp.Else != null) CollectAssignedStmt(slp.Else, acc, seen);
					break;
				case ForStmt fr:
					foreach (var v in fr.Vars) { if (v == null) continue; var lo = v.ToLowerInvariant(); if (seen.Add(lo)) acc.Add(lo); }
					CollectAssignedExpr(fr.Enumerable, acc, seen); CollectAssignedStmt(fr.Body, acc, seen); if (fr.Else != null) CollectAssignedStmt(fr.Else, acc, seen);
					break;
				case SwitchStmt sw:
					CollectAssignedExpr(sw.Value, acc, seen);
					foreach (var c in sw.Cases) { foreach (var v in c.Values) CollectAssignedExpr(v, acc, seen); foreach (var st in c.Body) CollectAssignedStmt(st, acc, seen); }
					if (sw.Default != null) foreach (var st in sw.Default) CollectAssignedStmt(st, acc, seen);
					break;
				case TryStmt tr:
					CollectAssignedStmt(tr.Body, acc, seen);
					foreach (var cb in tr.Catches)
					{
						if (cb.Var != null) { var lo = cb.Var.ToLowerInvariant(); if (seen.Add(lo)) acc.Add(lo); }
						CollectAssignedStmt(cb.Body, acc, seen);
					}
					if (tr.Else != null) CollectAssignedStmt(tr.Else, acc, seen);
					if (tr.Finally != null) CollectAssignedStmt(tr.Finally, acc, seen);
					break;
				case ThrowStmt th: if (th.Value != null) CollectAssignedExpr(th.Value, acc, seen); break;
			}
		}

		private static void CollectAssignedExpr(Expr e, List<string> acc, HashSet<string> seen)
		{
			switch (e)
			{
				case AssignExpr a:
					// A builtin var (A_Clipboard, A_SendLevel, …) is never a local even when assigned inside a function —
					// it resolves to its accessor (whose setter validates/throws), so don't shadow it with a local slot.
					if (a.Target is NameExpr n && !Script.TheScript.ReflectionsData.flatPublicStaticProperties.ContainsKey(n.Name.ToLowerInvariant()))
					{ var lo = n.Name.ToLowerInvariant(); if (seen.Add(lo)) acc.Add(lo); }
					CollectAssignedExpr(a.Target, acc, seen);   // a nested `x:=` inside a member/index target (e.g. obj[x:=v]:=w)
					CollectAssignedExpr(a.Value, acc, seen);
					break;
				case BinaryExpr b: CollectAssignedExpr(b.Left, acc, seen); CollectAssignedExpr(b.Right, acc, seen); break;
				case UnaryExpr u:
					// `&var` (a reference, typically an output param like `SplitPath(p,,,, &name)`) makes that variable a
					// local — it may be written through the ref. (`&obj.prop` is a PropRef, not a local; handled by recursion.)
					if (u.Op == "&" && u.Operand is NameExpr rn && !Script.TheScript.ReflectionsData.flatPublicStaticProperties.ContainsKey(rn.Name.ToLowerInvariant()))
					{ var lo = rn.Name.ToLowerInvariant(); if (seen.Add(lo)) acc.Add(lo); }
					CollectAssignedExpr(u.Operand, acc, seen);
					break;
				case TernaryExpr t: CollectAssignedExpr(t.Cond, acc, seen); CollectAssignedExpr(t.Then, acc, seen); CollectAssignedExpr(t.Else, acc, seen); break;
				case GroupExpr g: CollectAssignedExpr(g.Inner, acc, seen); break;
				case SequenceExpr seq: foreach (var it in seq.Items) CollectAssignedExpr(it, acc, seen); break;
				case DerefExpr dr: CollectAssignedExpr(dr.Name, acc, seen); break;
				case MemberExpr m: CollectAssignedExpr(m.Target, acc, seen); break;
				case DynMemberExpr dm: CollectAssignedExpr(dm.Target, acc, seen); CollectAssignedExpr(dm.NameExpr, acc, seen); break;
				case IndexExpr ix: CollectAssignedExpr(ix.Target, acc, seen); foreach (var ar in ix.Args) if (ar.Value != null) CollectAssignedExpr(ar.Value, acc, seen); break;
				case ObjectExpr oe: foreach (var en in oe.Entries) { CollectAssignedExpr(en.Key, acc, seen); CollectAssignedExpr(en.Value, acc, seen); } break;
				case CallExpr c: CollectAssignedExpr(c.Callee, acc, seen); foreach (var ar in c.Args) if (ar.Value != null) CollectAssignedExpr(ar.Value, acc, seen); break;
			}
		}

		// ---- scaffold ----

		private CompilationUnitSyntax BuildUnit(string name, List<MemberDeclarationSyntax> members, List<StatementSyntax> autoStmts)
		{
			var moduleClass = BuildModuleClass("__Main", _fieldDecls, members, _pendingLambdas, _hotMembers, autoStmts, null, _moduleCompat);
			return AssembleProgram(name, new[] { (MemberDeclarationSyntax)moduleClass }, new[] { "__Main" });
		}

		// Builds a `className : Keysharp.Runtime.Module` class: import bindings, fields, member functions, lambdas,
		// hotkey callbacks, then its AutoExecSection and constructor.
		private MemberDeclarationSyntax BuildModuleClass(string moduleName, IEnumerable<MemberDeclarationSyntax> fieldDecls,
			IEnumerable<MemberDeclarationSyntax> members, IEnumerable<MemberDeclarationSyntax> lambdas,
			IEnumerable<MemberDeclarationSyntax> hotMembers, List<StatementSyntax> autoStmts, IEnumerable<MemberDeclarationSyntax> importMembers,
			Semver.SemVersion compat = null)
		{
			// The C# class name is the module name, disambiguated if it shadows a framework/structural root; the runtime
			// resolves the original AHK module name back through [UserDeclaredName] (Reflections keys stringToTypes by it).
			var csName = NameMangler.ModuleClass(moduleName);
			var autoBody = new List<StatementSyntax>(autoStmts) { SyntaxFactory.ReturnStatement(Str("")) };
			var mm = new List<MemberDeclarationSyntax>();
			if (importMembers != null) mm.AddRange(importMembers);
			mm.AddRange(fieldDecls);
			// Avoid building the collision index for scripts without inline C#.
			if (_inlineBlocks != null)
				// Runtime variable lookup is case-insensitive even though C# member lookup is not.
				(_moduleGlobals ??= new(System.StringComparer.Ordinal))[moduleName] = new HashSet<string>(fieldDecls.OfType<FieldDeclarationSyntax>()
											 .SelectMany(f => f.Declaration.Variables).Select(v => v.Identifier.Text),
											 System.StringComparer.OrdinalIgnoreCase);
			mm.AddRange(members);
			if (lambdas != null) mm.AddRange(lambdas);
			if (hotMembers != null) mm.AddRange(hotMembers);
			mm.Add(ObjMethod("AutoExecSection", EmptyParams(), SyntaxFactory.Block(autoBody)));
			mm.Add(ClassCtor(csName));
			var decl = SyntaxFactory.ClassDeclaration(csName)
				.AddModifiers(PublicTok).WithBaseList(BaseList("Keysharp.Runtime.Module"))
				.AddAttributeLists(Attr("Keysharp.Runtime.CompatibilityMode", Str((compat ?? Keysharp.Runtime.Script.DefaultCompatibilityVersion).ToString())))
				.WithMembers(SyntaxFactory.List(mm));
			if (_inlineBlocks?.Any(b => b.Module == moduleName) == true) decl = decl.AddModifiers(PartialTok);
			if (csName != moduleName) decl = decl.AddAttributeLists(Attr("Keysharp.Runtime.UserDeclaredName", Str(moduleName)));
			return decl;
		}

		// Wraps the module classes in the Program class (with Main, MainScript and the outer AutoExecSection that drives
		// DHHR + per-module auto-exec in execution order) and the compilation unit (+ #Assembly* attributes).
		private CompilationUnitSyntax AssembleProgram(string name, IEnumerable<MemberDeclarationSyntax> moduleClasses, IReadOnlyList<string> execOrder)
		{
			var mainScriptField = SyntaxFactory.FieldDeclaration(
				SyntaxFactory.VariableDeclaration(Ty("Keysharp.Runtime.Script")).AddVariables(
					SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("MainScript")).WithInitializer(SyntaxFactory.EqualsValueClause(
						SyntaxFactory.ObjectCreationExpression(Ty("Keysharp.Runtime.Script"))
							.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
								_hookMutexName == null
									? new[] { Arg(SyntaxFactory.TypeOfExpression(Id("Program"))) }
									: new[] { Arg(SyntaxFactory.TypeOfExpression(Id("Program"))), Arg(Str(_hookMutexName)) })))))))
				.AddModifiers(PrivateTok, StaticTok);

			var programMembers = new List<MemberDeclarationSyntax> { BuildMain(name), mainScriptField };
			programMembers.AddRange(moduleClasses);
			programMembers.Add(BuildOuterAuto(execOrder));

			InlineSources = BuildInlineSources();
			InlineSource = InlineSources.Count == 0 ? null
				: string.Join(System.Environment.NewLine + System.Environment.NewLine, InlineSources.Select(source => source.Code));
			var programClass = SyntaxFactory.ClassDeclaration("Program").AddModifiers(PublicTok).WithMembers(SyntaxFactory.List(programMembers));
			if (_inlineBlocks != null) programClass = programClass.AddModifiers(PartialTok);
			var ns = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.QualifiedName(Id("Keysharp"), Id("CompiledMain"))).AddMembers(programClass);
			// No using directives: generated code uses clean, fully-qualified framework names (Keysharp.Runtime.Script.*,
			// System.*). A user class/module that would shadow such a root is given a non-colliding C# type name by the
			// mangler (NameMangler.ClassType / ModuleClass) + [UserDeclaredName], so no aliases/global:: are ever needed.
			var unit = SyntaxFactory.CompilationUnit().AddMembers(ns);
			// #Assembly* directives -> `[assembly: System.Reflection.Assembly*Attribute("value")]` on the compiled unit.
			foreach (var (attr, value) in _asmAttributes)
				unit = unit.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
						SyntaxFactory.Attribute(QName(attr)).WithArgumentList(SyntaxFactory.AttributeArgumentList(
							SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(Str(value)))))))
					.WithTarget(SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.AssemblyKeyword))));
			return unit;
		}

		private MemberDeclarationSyntax BuildMain(string name)
		{
			// SetName(<"*" for stdin, else null>[, <startup name>]); the 2nd arg only when a distinct name was given.
			// We deliberately do NOT bake the absolute compile-time path: emitting null for a file lets SetName pick up
			// the launcher-supplied runtime path (or the host exe), so A_ScriptFullPath/A_ScriptDir track where the
			// script actually runs (correct for a relocated .cks) and no build path is embedded in the assembly.
			var setNameArgs = new List<ExpressionSyntax>
			{
				_scriptPath == "*" ? Str("*") : SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
			};
			var fileName = _scriptPath == "*" ? null : System.IO.Path.GetFileName(_scriptPath);
			// For a real file, derive A_ScriptName from the path (don't override with the build name); for a from-string
			// compile, the explicit startup name (or the build name) becomes A_ScriptName.
			var givenName = _startupName ?? (_scriptPath == "*" ? name : null);
			if (givenName != null && givenName != fileName) setNameArgs.Add(Str(givenName));
			var tryStmts = new List<StatementSyntax>
			{
				ExprStmt(Inv(Member(Id("MainScript"), "SetName"), setNameArgs.ToArray())),
			};
			// #NoTrayIcon: suppress the tray icon before RunMainWindow creates the default tray chrome.
			if (_noTrayIcon)
				tryStmts.Add(ExprStmt(Assign(Access("MainScript.NoTrayIcon"),
					SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))));
			// #SingleInstance: bail out (return 0) when another instance already handles this one — placed right after
			// SetName so A_ScriptName is set, before any window/auto-exec runs (matches the canonical Main).
			if (_singleInstanceMode != null)
				tryStmts.Add(SyntaxFactory.IfStatement(
					Inv(Access("Keysharp.Runtime.Script.HandleSingleInstance"),
						Access("Keysharp.Builtins.Accessors.A_ScriptName"), Access("Keysharp.Runtime.eScriptInstance." + _singleInstanceMode)),
					SyntaxFactory.ReturnStatement(IntLit(0))));
			tryStmts.Add(CallStmt("Keysharp.Builtins.Env.HandleCommandLineParams", Id("args")));
			// Load after runtime identity/error options, but before JITting auto-exec can resolve package-backed fields.
			if (EmitPackageLoad() is StatementSyntax loadPackages) tryStmts.Add(loadPackages);

			tryStmts.Add(ExprStmt(Inv(Member(Id("MainScript"), "RunMainWindow"), Access("Keysharp.Builtins.Accessors.A_ScriptName"), Id("AutoExecSection"), False)));
			var catchStmts = new List<StatementSyntax>
			{
				LocalDeclVar("ex", Inv(Access("Keysharp.Runtime.Flow.UnwrapException"), Id("mainex"))),
				SyntaxFactory.IfStatement(
					SyntaxFactory.BinaryExpression(SyntaxKind.IsExpression, Id("ex"), Ty("Keysharp.Builtins.Flow.UserRequestedExitException")),
					SyntaxFactory.ReturnStatement(Access("System.Environment.ExitCode"))),
				SyntaxFactory.IfStatement(
					SyntaxFactory.IsPatternExpression(Id("ex"), SyntaxFactory.DeclarationPattern(Ty("Keysharp.Builtins.KeysharpException"),
						SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier("kserr")))),
					ExprStmt(Op("TryProcessKeysharpException", Id("MainScript"), Id("kserr"))),
					SyntaxFactory.ElseClause(ExprStmt(Op("TryProcessUnhandledException", Id("MainScript"), Id("ex"))))),
				ExprStmt(Op("SafeExit", IntLit(1))),
			};
			var catchClause = SyntaxFactory.CatchClause()
				.WithDeclaration(SyntaxFactory.CatchDeclaration(Ty("System.Exception"), SyntaxFactory.Identifier("mainex")))
				.WithBlock(SyntaxFactory.Block(catchStmts));
			var body = SyntaxFactory.Block(
				SyntaxFactory.TryStatement().WithBlock(SyntaxFactory.Block(tryStmts)).WithCatches(SyntaxFactory.SingletonList(catchClause)),
				SyntaxFactory.ReturnStatement(Access("System.Environment.ExitCode")));

			return SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)), "Main")
				.AddModifiers(PublicTok, StaticTok)
				.AddAttributeLists(Attr("System.STAThread"))
				.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Parameter(SyntaxFactory.Identifier("args")).WithType(
						ArrayOf(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))))))
				.WithBody(body);
		}

		private MemberDeclarationSyntax BuildOuterAuto(IReadOnlyList<string> execOrder)
		{
			var stmts = new List<StatementSyntax>();
			// #Warn output runs first (at load time, before any script logic), per the configured mode.
			stmts.AddRange(EmitWarnings());
			// DHHR: hotkey/hotstring/remap registration runs before the manifest, then Persistent() if any were defined.
			stmts.AddRange(_dhhr);
			if (_persistent) stmts.Add(CallStmt("Keysharp.Builtins.Flow.Persistent"));
			stmts.Add(CallStmt("Keysharp.Runtime.Keyboard.HotkeyDefinition.ManifestAllHotkeysHotstringsHooks"));
			// Run each module's auto-exec in dependency order, scoping CurrentModuleType to that module.
			foreach (var mod in execOrder)
			{
				var modClass = NameMangler.ModuleClass(mod);
				stmts.Add(ExprStmt(Assign(Member(Id("MainScript"), "CurrentModuleType"), SyntaxFactory.TypeOfExpression(Ty("Program." + modClass)))));
				stmts.Add(CallStmt("Program." + modClass + ".AutoExecSection"));
			}
			stmts.Add(ExprStmt(Assign(Member(Id("MainScript"), "CurrentModuleType"), Null)));
			stmts.Add(SyntaxFactory.ReturnStatement(Str("")));
			return ObjMethod("AutoExecSection", EmptyParams(), SyntaxFactory.Block(stmts));
		}

		// ---- SyntaxFactory helpers ----

		private static readonly SyntaxToken PublicTok = SyntaxFactory.Token(SyntaxKind.PublicKeyword);
		private static readonly SyntaxToken StaticTok = SyntaxFactory.Token(SyntaxKind.StaticKeyword);
		// The separate inline tree supplies matching partial declarations.
		private static readonly SyntaxToken PartialTok = SyntaxFactory.Token(SyntaxKind.PartialKeyword);
		private static readonly SyntaxToken PrivateTok = SyntaxFactory.Token(SyntaxKind.PrivateKeyword);
		private static readonly TypeSyntax ObjType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword));
		private static ArrayTypeSyntax ArrayOf(TypeSyntax elem) => SyntaxFactory.ArrayType(elem,
			SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression()))));
		private static readonly TypeSyntax ObjArrayType = ArrayOf(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)));
		private static ExpressionSyntax Null => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
		private static ExpressionSyntax False => SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);

		private static IdentifierNameSyntax Id(string n) => SyntaxFactory.IdentifierName(n);

		// Generated code uses clean, fully-qualified framework names. A user class/module that would shadow a framework
		// namespace (System/Keysharp) or structural root (Program/…) is instead given a non-colliding C# type name by
		// the mangler (NameMangler.ClassType / ModuleClass), so these helpers never need alias/global:: qualification.
		private static ExpressionSyntax Access(string dotted)
		{
			var parts = dotted.Split('.');
			ExpressionSyntax e = Id(parts[0]);
			for (var i = 1; i < parts.Length; i++)
				e = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, e, Id(parts[i]));
			return e;
		}

		private static NameSyntax QName(string dotted)
		{
			var parts = dotted.Split('.');
			NameSyntax n = Id(parts[0]);
			for (var i = 1; i < parts.Length; i++) n = SyntaxFactory.QualifiedName(n, Id(parts[i]));
			return n;
		}

		private static TypeSyntax Ty(string dotted) => QName(dotted);
		private static ExpressionSyntax Member(ExpressionSyntax e, string name) =>
			SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, e, Id(name));
		private static ArgumentSyntax Arg(ExpressionSyntax e) => SyntaxFactory.Argument(e);

		private static InvocationExpressionSyntax Inv(ExpressionSyntax callee, params ExpressionSyntax[] args) =>
			SyntaxFactory.InvocationExpression(callee, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args.Select(Arg))));

		private static ExpressionSyntax Op(string method, params ExpressionSyntax[] args) =>
			Inv(Access("Keysharp.Runtime.Script." + method), args);

		// AHK's `??` / `??=`: yield the left operand if it is set (non-null), otherwise the right. Lowered to C#'s native
		// null-coalescing operator so the right operand is NOT evaluated when the left is set (true short-circuit) — a plain
		// helper call would eagerly evaluate both, e.g. `b ?? MsgBox()` would always call MsgBox(). The left is cast to
		// object so a value-type operand (e.g. a numeric literal) is still a valid `??` left-hand side. Callers pass an
		// unset-permissive (RewriteToOrNull) left so an unset read yields null rather than raising.
		private static ExpressionSyntax Coalesce(ExpressionSyntax left, ExpressionSyntax right) =>
			SyntaxFactory.ParenthesizedExpression(SyntaxFactory.BinaryExpression(SyntaxKind.CoalesceExpression,
				SyntaxFactory.CastExpression(ObjType, SyntaxFactory.ParenthesizedExpression(left)), right));

		// The new value for a compound assignment `target op= rhs`, where `read` is the lowered current value of the target.
		// `??=` short-circuits via Coalesce (skipping rhs when the target is already set) and reads the target leniently so
		// an unset target yields null instead of raising; every other op is a plain Script.<Op>(read, rhs). Null = not lowerable.
		private ExpressionSyntax CompoundValue(string baseOp, ExpressionSyntax read, ExpressionSyntax rhs) =>
			baseOp == "??" ? Coalesce(RewriteToOrNull(read), rhs)
			: BinOps.TryGetValue(baseOp, out var m) ? Op(m, read, rhs)
			: null;

		private static ExpressionSyntax IfTest(ExpressionSyntax cond) => Op("IfTest", cond);
		private static ExpressionSyntax Assign(ExpressionSyntax target, ExpressionSyntax value) =>
			SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value);

		private static StatementSyntax ExprStmt(ExpressionSyntax e) => SyntaxFactory.ExpressionStatement(e);
		private static StatementSyntax CallStmt(string dotted, params ExpressionSyntax[] args) => ExprStmt(Inv(Access(dotted), args));

		private static StatementSyntax TryFinally(StatementSyntax tryBody, StatementSyntax finallyStmt) =>
			SyntaxFactory.TryStatement().WithBlock(SyntaxFactory.Block(tryBody)).WithFinally(SyntaxFactory.FinallyClause(SyntaxFactory.Block(finallyStmt)));

		private static StatementSyntax LocalDecl(TypeSyntax type, string name, ExpressionSyntax init) =>
			SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(type).AddVariables(
				SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name)).WithInitializer(SyntaxFactory.EqualsValueClause(init))));
		private static StatementSyntax LocalDeclVar(string name, ExpressionSyntax init) => LocalDecl(SyntaxFactory.IdentifierName("var"), name, init);
		private static StatementSyntax DeclLocal(TypeSyntax type, string name, ExpressionSyntax init) => LocalDecl(type, name, init);

		// Keysharp.Builtins.Functions.Func((System.Delegate)<methodPath>)
		private static ExpressionSyntax FuncBind(string methodPath) =>
			Inv(Access("Keysharp.Builtins.Functions.Func"), SyntaxFactory.CastExpression(Ty("System.Delegate"), Access(methodPath)));

		// Functions.Closure((System.Delegate)<localFn>) — wraps a capturing C# local function as a Closure-typed KeysharpFunc
		// (the captures live in the delegate; the runtime type is Closure so `x is Closure` holds).
		private static ExpressionSyntax ClosureBind(string methodPath) =>
			Inv(Access("Keysharp.Builtins.Functions.Closure"), SyntaxFactory.CastExpression(Ty("System.Delegate"), Access(methodPath)));

		// MainScript.Vars.Statics[typeof(T)]
		private static ExpressionSyntax TypeSingleton(string typeFullName) =>
			SyntaxFactory.ElementAccessExpression(Access("MainScript.Vars.Statics"))
				.WithArgumentList(SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(SyntaxFactory.TypeOfExpression(Ty(typeFullName))))));

		// Keysharp.Builtins.Misc.MakeVarRef(() => getter, (KS_value) => setter)
		private static ExpressionSyntax MakeVarRefGS(ExpressionSyntax getter, ExpressionSyntax setter) =>
			Inv(Access("Keysharp.Builtins.Misc.MakeVarRef"),
				SyntaxFactory.ParenthesizedLambdaExpression().WithExpressionBody(getter),
				SyntaxFactory.ParenthesizedLambdaExpression()
					.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Parameter(SyntaxFactory.Identifier("KS_value")))))
					.WithExpressionBody(setter));

		// A VarRef that ignores writes — used for omitted for-loop variables (`for (, v in arr)`).
		private static ExpressionSyntax DiscardVarRef() =>
			Inv(Access("Keysharp.Builtins.Misc.MakeVarRef"),
				SyntaxFactory.ParenthesizedLambdaExpression().WithExpressionBody(Str("")),
				SyntaxFactory.ParenthesizedLambdaExpression()
					.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Parameter(SyntaxFactory.Identifier("KS_value")))))
					.WithBlock(SyntaxFactory.Block()));

		// &lvalue : a VarRef whose getter reads and whose setter writes the lvalue (variable, member or index).
		private ExpressionSyntax MakeRefFor(Expr lvalue)
		{
			switch (lvalue)
			{
				case NameExpr n:
					var lown = n.Name.ToLowerInvariant();
					// &param: the param already holds a VarRef (or virtual reference) aliasing the caller's variable, so
					// forward THAT ref directly. Re-wrapping it in a fresh VarRef would add a needless layer of indirection
					// and — because MakeVarRef eagerly probes its getter — read the caller's variable, throwing UnsetError
					// when it is currently unset (e.g. `f(&out)` where `f` fills `out` via a nested `g(&out)`).
					if (_byRefParams != null && _byRefParams.Contains(lown))
						return Id(NameMangler.Escape(lown));
					// A scoped `#import` name is not a C# lvalue. A READ-ONLY import (module object, function, type, built-in
					// member) resolves to a value expression (`new Ks()`, `FuncBind(...)`, …) whose "setter" `<read> = v`
					// would assign to a non-lvalue → uncompilable C# (CS0131/CS1656); diagnose it at the source position
					// instead. A WRITABLE script-variable export builds its ref from the import's Read/Write targets (the
					// module's backing field, an lvalue), so `&writableExport` works and writes propagate to the module.
					if (ResolvesToScopedImport(lown, out var impRef))
					{
						if (impRef.Write == null)
						{
							Diag($"{n.Line}:{n.Column}: #Import: cannot take a reference to imported name '{n.Name}'");
							return Str("");
						}
						return MakeVarRefGS(impRef.Read(), Assign(impRef.Write(), Id("KS_value")));
					}
					var nr = NameRef(n.Name);
					return MakeVarRefGS(nr, Assign(nr, Id("KS_value")));
				// `&obj.prop` / `&obj[i]` produce a v2.1 PropRef bound to the property slot, via obj.__Ref(name[, args]).
				case MemberExpr me:
					return Op("Invoke", LowerExpr(me.Target), Str("__Ref"), Str(me.Name));
				case DynMemberExpr dme:
					return Op("Invoke", LowerExpr(dme.Target), Str("__Ref"), LowerExpr(dme.NameExpr));
				// `&obj.prop[i,j]` binds to the parameterized PROPERTY slot (obj.__Ref("prop", i, j)), re-reading the
				// property each access — not to whatever array `obj.prop` currently resolves to.
				case IndexExpr ie when ie.Target is MemberExpr ipm:
					var pmArgs = new List<ExpressionSyntax> { LowerExpr(ipm.Target), Str("__Ref"), Str(ipm.Name) };
					pmArgs.AddRange(LowerArgs(ie.Args));
					return Op("Invoke", pmArgs.ToArray());
				case IndexExpr ie when ie.Target is DynMemberExpr ipd:
					var pdArgs = new List<ExpressionSyntax> { LowerExpr(ipd.Target), Str("__Ref"), LowerExpr(ipd.NameExpr) };
					pdArgs.AddRange(LowerArgs(ie.Args));
					return Op("Invoke", pdArgs.ToArray());
				case IndexExpr ie:   // &obj[i] (obj a plain value/var) -> obj.__Ref("__Item", i)
					var refArgs = new List<ExpressionSyntax> { LowerExpr(ie.Target), Str("__Ref"), Str("__Item") };
					refArgs.AddRange(LowerArgs(ie.Args));
					return Op("Invoke", refArgs.ToArray());
				case DerefExpr dr:   // &%name% : ref to a dynamically-named variable, through DerefGet/DerefSet or ModuleData.Vars
					if (_inDerefFunc)
						return MakeVarRefGS(DerefGet(LowerExpr(dr.Name)), DerefSet(LowerExpr(dr.Name), Id("KS_value")));
					return MakeVarRefGS(VarsIndex(LowerExpr(dr.Name)), VarsWrite(LowerExpr(dr.Name), Id("KS_value")));
				case GroupExpr g:
					return MakeRefFor(g.Inner);
				case AssignExpr a:   // &(x := v): perform the assignment, then yield a ref to its target.
					return Op("MultiStatement", LowerAssign(a), MakeRefFor(a.Target));
				default:
					Diag($"'&' on {lvalue.GetType().Name} is not yet lowerable"); return Str("");
			}
		}

		// Keysharp.Runtime.Script.InitStaticVariable(ref <field>, "<scope>_<field>", () => <init>) — the second arg is a
		// GLOBAL one-time-init guard key, so it must be unique per field LOCATION. The field name alone isn't: a class
		// method's SL_ field now lives in its class and can repeat across classes, so qualify the key with the declaring
		// type (module class, plus the nested class path for a class member). Module-level statics keep "__Main_<field>".
		private ExpressionSyntax InitStatic(string field, ExpressionSyntax init)
		{
			var scope = _currentClassPath == null ? _currentModuleClass : _currentModuleClass + "." + _currentClassPath;
			return SyntaxFactory.InvocationExpression(Access("Keysharp.Runtime.Script.InitStaticVariable"),
				SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
				{
					SyntaxFactory.Argument(Id(field)).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)),
					Arg(Str(scope + "_" + field)),
					Arg(SyntaxFactory.ParenthesizedLambdaExpression().WithExpressionBody(init)),
				})));
		}

		private static FieldDeclarationSyntax ObjField(string name, ExpressionSyntax init) =>
			SyntaxFactory.FieldDeclaration(SyntaxFactory.VariableDeclaration(ObjType).AddVariables(
				SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name)).WithInitializer(SyntaxFactory.EqualsValueClause(init))))
			.AddModifiers(PublicTok, StaticTok);

		private static PropertyDeclarationSyntax ObjArrowProp(string name, ExpressionSyntax expr) =>
			SyntaxFactory.PropertyDeclaration(ObjType, SyntaxFactory.Identifier(name)).AddModifiers(PublicTok, StaticTok)
				.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(expr)).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

		private static MethodDeclarationSyntax ObjMethod(string name, ParameterListSyntax pl, BlockSyntax body, params AttributeListSyntax[] attrs)
		{
			var m = SyntaxFactory.MethodDeclaration(ObjType, SyntaxFactory.Identifier(name)).AddModifiers(PublicTok, StaticTok).WithParameterList(pl).WithBody(body);
			return attrs.Length > 0 ? m.AddAttributeLists(attrs) : m;
		}

		private static AttributeListSyntax Attr(string dottedName, params ExpressionSyntax[] args)
		{
			var attr = SyntaxFactory.Attribute(QName(dottedName));
			if (args.Length > 0)
				attr = attr.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(args.Select(a => SyntaxFactory.AttributeArgument(a)))));
			return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr));
		}

		private static BaseListSyntax BaseList(string typeName) =>
			SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(SyntaxFactory.SimpleBaseType(Ty(typeName))));

		private static ParameterSyntax ThisParam() => SyntaxFactory.Parameter(SyntaxFactory.Identifier("@this")).WithType(ObjType);
		private static ParameterListSyntax ParamThis() => SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(ThisParam()));
		private static ParameterListSyntax EmptyParams() => SyntaxFactory.ParameterList();

		private static MemberDeclarationSyntax ClassCtor(string className) =>
			SyntaxFactory.ConstructorDeclaration(className).AddModifiers(PublicTok)
				.WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(
					SyntaxFactory.Parameter(SyntaxFactory.Identifier("args")).WithType(ObjArrayType).AddModifiers(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)))))
				.WithInitializer(SyntaxFactory.ConstructorInitializer(SyntaxKind.BaseConstructorInitializer,
					SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(Arg(Null)))))
				.WithBody(SyntaxFactory.Block());

		private static ExpressionSyntax IntLit(int n) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(n));
		private static ExpressionSyntax Str(string value) => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(value));

		private static ExpressionSyntax BoolLit(bool v) => SyntaxFactory.LiteralExpression(v ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
		private static ExpressionSyntax NumLit(long v) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(v));
		private static ExpressionSyntax NumLit(double v) => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(v));

		// True if the lowered expression is a numeric (or true/false) C# literal, returning its value.
		private static bool TryGetNumLit(ExpressionSyntax e, out bool isDouble, out double dbl, out long lng)
		{
			isDouble = false; dbl = 0; lng = 0;
			if (e is not LiteralExpressionSyntax lit) return false;
			if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) { lng = 1; return true; }
			if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) { lng = 0; return true; }
			if (!lit.IsKind(SyntaxKind.NumericLiteralExpression)) return false;
			switch (lit.Token.Value) { case long l: lng = l; return true; case double d: isDouble = true; dbl = d; return true; }
			return false;
		}

		// True if the lowered expression is a literal that can be concatenated (string/number/true/false/null).
		private static bool TryGetConcatLit(ExpressionSyntax e, out object value)
		{
			value = null;
			if (e is not LiteralExpressionSyntax lit) return false;
			if (lit.IsKind(SyntaxKind.StringLiteralExpression)) { value = lit.Token.Value as string ?? ""; return true; }
			if (lit.IsKind(SyntaxKind.NumericLiteralExpression)) { value = lit.Token.Value; return true; }
			if (lit.IsKind(SyntaxKind.TrueLiteralExpression)) { value = true; return true; }
			if (lit.IsKind(SyntaxKind.FalseLiteralExpression)) { value = false; return true; }
			if (lit.IsKind(SyntaxKind.NullLiteralExpression)) { value = null; return true; }
			return false;
		}

		// Constant-folds a binary op over already-lowered LITERAL operands (matches the canonical TryFoldBinaryExpression):
		// `1 + 2` -> `3L`, `1 . "a"` -> `"1a"`. Returns null when not foldable (non-literal operand, div-by-zero, etc.).
		private static ExpressionSyntax TryFoldBinary(string op, ExpressionSyntax le, ExpressionSyntax re)
		{
			if (op == ".")
			{
				if (TryGetConcatLit(le, out var lv) && TryGetConcatLit(re, out var rv) && rv != null)   // Concat() raises on null right
					return Str(Keysharp.Runtime.Script.ForceString(lv) + Keysharp.Runtime.Script.ForceString(rv));
				return null;
			}
			if (!TryGetNumLit(le, out var lD, out var ld, out var ll) || !TryGetNumLit(re, out var rD, out var rd, out var rl))
				return null;
			bool useD = lD || rD;
			double L() => lD ? ld : ll;
			double R() => rD ? rd : rl;
			switch (op)
			{
				case "+": return useD ? NumLit(L() + R()) : NumLit(ll + rl);
				case "-": return useD ? NumLit(L() - R()) : NumLit(ll - rl);
				case "*": return useD ? NumLit(L() * R()) : NumLit(ll * rl);
				case "/": return R() == 0 ? null : NumLit(L() / R());
				case "//": return useD || rl == 0 ? null : NumLit(ll / rl);
				case "**": return useD ? NumLit(System.Math.Pow(L(), R())) : NumLit((long)System.Math.Pow(ll, rl));
				case "<<": return useD || rl < 0 || rl > 63 ? null : NumLit(ll << (int)rl);
				case ">>": return useD || rl < 0 || rl > 63 ? null : NumLit(ll >> (int)rl);
				case ">>>": return useD || rl < 0 || rl > 63 ? null : NumLit((long)((ulong)ll >> (int)rl));
				case "&": return useD ? null : NumLit(ll & rl);
				case "|": return useD ? null : NumLit(ll | rl);
				case "^": return useD ? null : NumLit(ll ^ rl);
				default: return null;
			}
		}

		private static ExpressionSyntax Num(string raw)
		{
			var t = raw.Replace("_", "");
			if (t.EndsWith("n") || t.EndsWith("N")) t = t[..^1];
			if (TryParseAhkInt(t, out var v)) return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(v));
			if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
				return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(d));
			return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0L));
		}

		private static bool TryParseAhkInt(string t, out long v)
		{
			v = 0;
			if (t.Length == 0) return false;
			try
			{
				if (t.StartsWith("0x") || t.StartsWith("0X")) { v = System.Convert.ToInt64(t[2..], 16); return true; }
				if (t.StartsWith("0b") || t.StartsWith("0B")) { v = System.Convert.ToInt64(t[2..], 2); return true; }
				if (t.StartsWith("0o") || t.StartsWith("0O")) { v = System.Convert.ToInt64(t[2..], 8); return true; }
				if (t.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) return false;
				return long.TryParse(t, out v);
			}
			catch { return false; }
		}

		private static string DecodeString(string raw)
		{
			if (raw.Length < 2) return "";
			var inner = raw.Substring(1, raw.Length - 2);
			var sb = new System.Text.StringBuilder(inner.Length);
			for (var i = 0; i < inner.Length; i++)
			{
				var ch = inner[i];
				if (ch == '`' && i + 1 < inner.Length)
				{
					var n = inner[++i];
					sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', 'v' => '\v', 's' => ' ', 'a' => '\a', _ => n });
				}
				else sb.Append(ch);
			}
			return sb.ToString();
		}

		private static ExpressionSyntax[] Cons(ExpressionSyntax head, List<ExpressionSyntax> tail)
		{
			var arr = new ExpressionSyntax[tail.Count + 1];
			arr[0] = head;
			tail.CopyTo(arr, 1);
			return arr;
		}

		private static ExpressionSyntax[] Cons2(ExpressionSyntax a, ExpressionSyntax b, List<ExpressionSyntax> tail)
		{
			var arr = new ExpressionSyntax[tail.Count + 2];
			arr[0] = a;
			arr[1] = b;
			tail.CopyTo(arr, 2);
			return arr;
		}

		private static ParameterSyntax[] Prepend(ParameterSyntax head, ParameterSyntax[] tail)
		{
			var arr = new ParameterSyntax[tail.Length + 1];
			arr[0] = head;
			tail.CopyTo(arr, 1);
			return arr;
		}

		private static HashSet<string> ByRefSet(List<Param> ps)
		{
			HashSet<string> set = null;
			foreach (var p in ps) if (p.ByRef) (set ??= new()).Add(p.Name.ToLowerInvariant());
			return set;
		}

		private void Diag(string msg) => Diagnostics.Add(msg);
	}
}
