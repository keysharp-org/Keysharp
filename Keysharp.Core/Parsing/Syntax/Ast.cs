using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keysharp.Parsing.Syntax
{
	// Strongly-typed AST for the hand-written parser. One sealed node per construct; the lowering
	// pass (AST -> Roslyn) will switch over these types. Tracer-bullet scope: expressions, calls,
	// member/index access, fat-arrow lambdas, and the core statements.

	internal abstract class Node
	{
		public int Line;
		public int Column;
		// Full path of the source file this node came from (an #included file); null for the main script. Used to fold
		// A_LineFile to the including file's path, matching AHK.
		public string File;
	}

	internal abstract class Expr : Node { }
	internal abstract class Stmt : Node { }

	internal enum LiteralKind { Number, String }

	internal sealed class ProgramNode : Node
	{
		public readonly List<Stmt> Body;

		/// <summary>The final preprocessor symbol set.</summary>
		public IReadOnlyCollection<string> Defines = [];
		public ProgramNode(List<Stmt> body) => Body = body;
	}

	// ---- Expressions ----------------------------------------------------------

	internal sealed class LiteralExpr : Expr
	{
		public readonly LiteralKind Kind;
		public readonly string Raw;   // raw source text (quotes/escapes kept for strings)
		public LiteralExpr(LiteralKind kind, string raw) { Kind = kind; Raw = raw; }
	}

	internal sealed class NameExpr : Expr
	{
		public readonly string Name;
		// Line (inherited Node.Line) is the source line of the reference — used to fold A_LineNumber to a literal.
		public NameExpr(string name, int line = 0) { Name = name; Line = line; }
	}

	internal sealed class UnaryExpr : Expr
	{
		public readonly string Op;
		public readonly Expr Operand;
		public readonly bool Postfix;   // true for x++, x--, and the maybe-operator x?
		public UnaryExpr(string op, Expr operand, bool postfix) { Op = op; Operand = operand; Postfix = postfix; }
	}

	internal sealed class BinaryExpr : Expr
	{
		public readonly string Op;
		public readonly Expr Left, Right;
		public BinaryExpr(string op, Expr left, Expr right) { Op = op; Left = left; Right = right; }
	}

	internal sealed class AssignExpr : Expr
	{
		public readonly string Op;      // :=, +=, .=, ...
		public readonly Expr Target, Value;
		public AssignExpr(string op, Expr target, Expr value) { Op = op; Target = target; Value = value; }
	}

	internal sealed class TernaryExpr : Expr
	{
		public readonly Expr Cond, Then, Else;
		public TernaryExpr(Expr cond, Expr then, Expr els) { Cond = cond; Then = then; Else = els; }
	}

	internal sealed class Argument
	{
		public readonly Expr Value;     // null => omitted (e.g. f(a,,b))
		public readonly bool Spread;    // trailing * (variadic spread)
		// `name: value` — a NAMED argument, bound to the callee's parameter of that name rather than by position.
		// Null for a positional argument. Named arguments always trail the positional ones (the parser enforces it),
		// which is what lets the runtime detect them with a single test on the last element of the argument array.
		public readonly string Name;
		// `%x%: value` / `a%b%c: value` — the name is computed at run time, exactly as an object literal's key is.
		// Set INSTEAD of Name (never both), so `Name != null` still means "a name known at build time" and only
		// that form can be checked against the callee's signature.
		public readonly Expr NameExpr;
		public bool IsNamed => Name != null || NameExpr != null;
		public Argument(Expr value, bool spread, string name = null, Expr nameExpr = null)
		{ Value = value; Spread = spread; Name = name; NameExpr = nameExpr; }
	}

	internal sealed class CallExpr : Expr
	{
		public readonly Expr Callee;
		public readonly List<Argument> Args;
		public readonly bool NullConditional;   // `obj?.()` — short-circuit to unset if callee target is unset
		public CallExpr(Expr callee, List<Argument> args, bool nullConditional = false) { Callee = callee; Args = args; NullConditional = nullConditional; }
	}

	internal sealed class MemberExpr : Expr
	{
		public readonly Expr Target;
		public readonly string Name;
		public readonly bool NullConditional;   // ?.
		public MemberExpr(Expr target, string name, bool nullConditional) { Target = target; Name = name; NullConditional = nullConditional; }
	}

	// Dynamic member access: `obj.%nameExpr%` — the member name is computed at runtime (ForceString'd).
	internal sealed class DynMemberExpr : Expr
	{
		public readonly Expr Target;
		public readonly Expr NameExpr;
		public readonly bool NullConditional;
		public DynMemberExpr(Expr target, Expr nameExpr, bool nullConditional) { Target = target; NameExpr = nameExpr; NullConditional = nullConditional; }
	}

	internal sealed class IndexExpr : Expr
	{
		public readonly Expr Target;
		public readonly List<Argument> Args;
		public readonly bool NullConditional;   // `obj?.[i]` — short-circuit to unset if target is unset
		public IndexExpr(Expr target, List<Argument> args, bool nullConditional = false) { Target = target; Args = args; NullConditional = nullConditional; }
	}

	internal sealed class GroupExpr : Expr
	{
		public readonly Expr Inner;
		public GroupExpr(Expr inner) => Inner = inner;
	}

	// Comma expression sequence: `a, b, c` (statement level) or `(a, b, c)`. Evaluates all, yields the last.
	internal sealed class SequenceExpr : Expr
	{
		public readonly List<Expr> Items;
		public SequenceExpr(List<Expr> items) => Items = items;
	}

	// Dynamic variable dereference: `%nameExpr%` — reads/writes the variable named by nameExpr.
	internal sealed class DerefExpr : Expr
	{
		public readonly Expr Name;
		public DerefExpr(Expr name) => Name = name;
	}

	internal sealed class ArrayExpr : Expr
	{
		public readonly List<Argument> Elements;
		public ArrayExpr(List<Argument> elements) => Elements = elements;
	}

	// Map-creation literal `[k1: v1, k2: v2]` (keys are arbitrary expressions). Lowers to `new Map(k1, v1, k2, v2)`.
	internal sealed class MapExpr : Expr
	{
		public readonly List<(Expr Key, Expr Value)> Entries;
		public MapExpr(List<(Expr, Expr)> entries) => Entries = entries;
	}

	internal sealed class ObjectEntry
	{
		public readonly Expr Key;     // NameExpr (property name) or string literal; dynamic keys deferred
		public readonly Expr Value;
		public ObjectEntry(Expr key, Expr value) { Key = key; Value = value; }
	}

	internal sealed class ObjectExpr : Expr
	{
		public readonly List<ObjectEntry> Entries;
		public ObjectExpr(List<ObjectEntry> entries) => Entries = entries;
	}

	internal sealed class Param
	{
		public readonly string Name;
		public readonly Expr Default;
		public readonly bool ByRef;
		public readonly bool Variadic;
		public readonly bool Optional;
		public Param(string name, Expr def, bool byRef, bool variadic, bool optional)
		{ Name = name; Default = def; ByRef = byRef; Variadic = variadic; Optional = optional; }
	}

	internal sealed class FatArrowExpr : Expr
	{
		public readonly List<Param> Params;
		public readonly Expr Body;        // `=> expr` body (null when a block body is used)
		public readonly Block BlockBody;  // `{ … }` body for anonymous block functions (null for arrow body)
		public string Name;               // optional name of a `name(params) => …` expression — lets the body recurse by name
		public FatArrowExpr(List<Param> ps, Expr body) { Params = ps; Body = body; }
		public FatArrowExpr(List<Param> ps, Block block) { Params = ps; BlockBody = block; }
	}

	// ---- Statements -----------------------------------------------------------

	internal sealed class ExpressionStmt : Stmt
	{
		public readonly Expr Expr;
		public ExpressionStmt(Expr e) => Expr = e;
	}

	internal sealed class Block : Stmt
	{
		public readonly List<Stmt> Body;
		public Block(List<Stmt> body) => Body = body;
	}

	internal sealed class IfStmt : Stmt
	{
		public readonly Expr Cond;
		public readonly Stmt Then, Else;
		public IfStmt(Expr cond, Stmt then, Stmt els) { Cond = cond; Then = then; Else = els; }
	}

	internal sealed class WhileStmt : Stmt
	{
		public readonly Expr Cond;
		public readonly Stmt Body;
		public readonly Expr Until;   // trailing `Until cond`; null if absent
		public Stmt Else;             // trailing `else` block (runs iff the loop body never executed); null if absent
		public WhileStmt(Expr cond, Stmt body, Expr until = null) { Cond = cond; Body = body; Until = until; }
	}

	internal sealed class ReturnStmt : Stmt
	{
		public readonly Expr Value;   // null => bare return
		public ReturnStmt(Expr value) => Value = value;
	}

	internal sealed class LoopStmt : Stmt
	{
		public readonly Expr Count;   // null => infinite loop
		public readonly Stmt Body;
		public readonly Expr Until;   // trailing `Until cond` (do-until); null if absent
		public Stmt Else;             // trailing `else` block (runs iff the loop body never executed); null if absent
		public LoopStmt(Expr count, Stmt body, Expr until = null) { Count = count; Body = body; Until = until; }
	}

	// Specialized loops: Loop Parse/Files/Read/Reg <args...>. Kind is the lowercase sub-keyword.
	internal sealed class SpecialLoopStmt : Stmt
	{
		public readonly string Kind;          // "parse" | "files" | "read" | "reg"
		public readonly List<Expr> Args;      // the comma-separated argument expressions (omitted => null)
		public readonly Stmt Body;
		public Expr Until;                    // trailing `Until cond`; null if absent
		public Stmt Else;                     // trailing `else` block (runs iff the loop body never executed); null if absent
		public SpecialLoopStmt(string kind, List<Expr> args, Stmt body) { Kind = kind; Args = args; Body = body; }
	}

	internal sealed class ForStmt : Stmt
	{
		public readonly List<string> Vars;
		public readonly Expr Enumerable;
		public readonly Stmt Body;
		public Expr Until;   // trailing `Until cond`; null if absent
		public Stmt Else;    // trailing `else` block (runs iff the loop body never executed); null if absent
		public ForStmt(List<string> vars, Expr enumerable, Stmt body) { Vars = vars; Enumerable = enumerable; Body = body; }
	}

	internal sealed class SwitchCase
	{
		public readonly List<Expr> Values;
		public readonly List<Stmt> Body;
		public SwitchCase(List<Expr> values, List<Stmt> body) { Values = values; Body = body; }
	}

	internal sealed class SwitchStmt : Stmt
	{
		public readonly Expr Value;
		public readonly Expr CaseSense;       // optional 2nd arg `switch v, caseSense`; null => default (sensitive)
		public readonly List<SwitchCase> Cases;
		public readonly List<Stmt> Default;   // null => no default
		public SwitchStmt(Expr value, Expr caseSense, List<SwitchCase> cases, List<Stmt> def) { Value = value; CaseSense = caseSense; Cases = cases; Default = def; }
	}

	internal sealed class CatchBlock
	{
		public readonly List<string> Types;   // empty => catch-all
		public readonly string Var;           // null => no bound variable
		public readonly Stmt Body;
		public CatchBlock(List<string> types, string var, Stmt body) { Types = types; Var = var; Body = body; }
	}

	internal sealed class TryStmt : Stmt
	{
		public readonly Stmt Body;
		public readonly List<CatchBlock> Catches;   // zero or more catch clauses
		public readonly Stmt Else;                  // try/catch/else: runs when no exception; null if absent
		public readonly Stmt Finally;               // null => no finally
		public TryStmt(Stmt body, List<CatchBlock> catches, Stmt elseBody, Stmt finallyBody)
		{ Body = body; Catches = catches; Else = elseBody; Finally = finallyBody; }
	}

	internal sealed class ThrowStmt : Stmt
	{
		public readonly Expr Value;   // null => bare throw
		public ThrowStmt(Expr value) => Value = value;
	}

	// break/continue with an optional target: a loop level (integer N) or a source label name; null => innermost loop.
	internal sealed class BreakStmt : Stmt { public readonly string Target; public BreakStmt(string target = null) => Target = target; }
	internal sealed class ContinueStmt : Stmt { public readonly string Target; public ContinueStmt(string target = null) => Target = target; }
	internal sealed class GotoStmt : Stmt { public readonly string Target; public GotoStmt(string target) => Target = target; }
	internal sealed class LabelStmt : Stmt { public readonly string Name; public LabelStmt(string name) => Name = name; }

	// A #directive line. Most are compile-time/no-op for the runtime; Args holds the raw trailing text.
	internal class DirectiveStmt : Stmt
	{
		public readonly string Name;
		public readonly string Args;
		public DirectiveStmt(string name, string args) { Name = name; Args = args; }
	}

	// A `#import <module> [as <alias>] [{ names }]` directive, parsed into its parts by the parser (the single
	// authority on import syntax) so the lowerer can consume the fields directly instead of re-parsing Args.
	// `Module` is the name without quotes; `Named` is the raw text inside `{ … }` (null when no list, "*" for
	// wildcard) — split into individual names by the lowerer where the binding semantics live.
	internal sealed class ImportDirective : DirectiveStmt
	{
		public readonly string Module;
		public readonly string Alias;
		public readonly string Named;
		public readonly bool Quoted;
		public ImportDirective(string args, string module, string alias, string named, bool quoted)
			: base("import", args) { Module = module; Alias = alias; Named = named; Quoted = quoted; }
	}

	internal sealed class CSharpDirective : DirectiveStmt
	{
		public string Code;
		public int CodeLine;
		public string CodeFile;
		public IReadOnlyCollection<string> Defines = [];
		public string FilePath;
		public CSharpDirective(string options, string code, int codeLine)
			: base("CSharp", options ?? "") { Code = code; CodeLine = codeLine; }

		public bool IsFileForm => FilePath != null;
	}

	internal sealed class DeclStmt : Stmt
	{
		public readonly string Keyword;   // local / global / static
		public readonly List<Expr> Items;
		public DeclStmt(string keyword, List<Expr> items) { Keyword = keyword; Items = items; }
	}

	// `export <decl>` / `export default <decl>` — marks a function/class/variable declaration as a module export.
	internal sealed class ExportStmt : Stmt
	{
		public readonly bool Default;
		public readonly Stmt Decl;        // FunctionDecl, ClassDecl, or an ExpressionStmt assignment / DeclStmt
		public ExportStmt(bool isDefault, Stmt decl) { Default = isDefault; Decl = decl; }
	}

	// One or more stacked hotkey triggers (each raw text ends with `::`) sharing one body: a block, a single
	// statement (the action), or a named function declaration.
	internal sealed class HotkeyDef : Stmt
	{
		public readonly List<string> Triggers;   // raw trigger text including the trailing `::`
		public readonly Stmt Body;               // block/statement action (null if Func)
		public readonly FunctionDecl Func;       // named-function body form (null otherwise)
		public HotkeyDef(List<string> triggers, Stmt body, FunctionDecl func) { Triggers = triggers; Body = body; Func = func; }
	}

	// A remap line `source::target` (e.g. `a::b`, `^x::^c`). The lexer already split the source and target key text.
	internal sealed class RemapDef : Stmt
	{
		public readonly string Source;
		public readonly string Target;
		public RemapDef(string source, string target) { Source = source; Target = target; }
	}

	// One or more stacked hotstring triggers (raw `:opts:trigger::`) sharing a body: an inline/continuation
	// expansion string, a block/statement, or a named function.
	internal sealed class HotstringDef : Stmt
	{
		public readonly List<string> Triggers;   // raw `:opts:trigger::`
		public readonly string Expansion;        // replacement text (null if a code body is used)
		public readonly Stmt Body;               // code body (null if Expansion)
		public readonly FunctionDecl Func;       // named-function body form
		public HotstringDef(List<string> triggers, string expansion, Stmt body, FunctionDecl func)
		{ Triggers = triggers; Expansion = expansion; Body = body; Func = func; }
	}

	internal sealed class ClassField
	{
		public readonly string Name;
		public readonly Expr Init;     // null => no initializer
		public readonly bool Static;
		public readonly Expr TypeExpr;     // for a typed struct field `name : TypeExpression`; null otherwise
		public readonly long Pack;         // #StructPack alignment in effect for this typed field (0 = default)
		public ClassField(string name, Expr init, bool isStatic, Expr typeExpr = null, long pack = 0) { Name = name; Init = init; Static = isStatic; TypeExpr = typeExpr; Pack = pack; }
	}

	internal sealed class ClassMethod
	{
		public readonly string Name;
		public readonly List<Param> Params;
		public readonly Block Body;
		public readonly Expr ArrowBody;
		public readonly bool Static;
		public ClassMethod(string name, List<Param> ps, Block body, Expr arrowBody, bool isStatic)
		{ Name = name; Params = ps; Body = body; ArrowBody = arrowBody; Static = isStatic; }
	}

	internal sealed class ClassProperty
	{
		public readonly string Name;
		public readonly List<Param> Params;   // index params for `Prop[i] { ... }`; empty for a plain property
		public readonly Block GetBody;     // get { ... }
		public readonly Expr GetArrow;     // get => expr  (or shorthand `Name => expr`)
		public readonly Block SetBody;     // set { ... }
		public readonly Expr SetArrow;     // set => expr
		public readonly bool Static;
		public ClassProperty(string name, List<Param> ps, Block getBody, Expr getArrow, Block setBody, Expr setArrow, bool isStatic)
		{ Name = name; Params = ps; GetBody = getBody; GetArrow = getArrow; SetBody = setBody; SetArrow = setArrow; Static = isStatic; }
		public bool HasGet => GetBody != null || GetArrow != null;
		public bool HasSet => SetBody != null || SetArrow != null;
	}

	internal sealed class ClassDecl : Stmt
	{
		public readonly string Name;
		public readonly string Base;   // null => implicitly KeysharpObject (or Struct when IsStruct)
		public readonly List<ClassField> Fields;
		public readonly List<ClassMethod> Methods;
		public readonly List<ClassProperty> Properties;
		public readonly List<ClassDecl> Nested;   // nested class declarations
		public readonly bool IsStruct;   // `struct Name { … }` — base is Struct; instance fields are typed
		public string Requires;          // a `#Requires` directive in the class body (its args) → per-class compat mode
		public List<ImportDirective> Imports = new();   // `#import` directives in the class body → class-scoped bindings
		public List<CSharpDirective> CSharpBlocks;
		public List<Stmt> StaticInit = new();     // `static x.y := z` member/index-target static initializers (run in static __Init)
		public List<Stmt> InstanceInit = new();   // `x.y := z` member/index-target instance initializers (run in __Init)
		public ClassDecl(string name, string baseName, List<ClassField> fields, List<ClassMethod> methods, List<ClassProperty> properties, List<ClassDecl> nested = null, bool isStruct = false)
		{ Name = name; Base = baseName; Fields = fields; Methods = methods; Properties = properties; Nested = nested ?? new List<ClassDecl>(); IsStruct = isStruct; }
	}

	internal sealed class FunctionDecl : Stmt
	{
		public readonly string Name;
		public readonly List<Param> Params;
		public readonly Block Body;        // block form
		public readonly Expr ArrowBody;    // => expr form
		public readonly bool Static;       // `static name(){…}` — a non-capturing (module-level) nested function
		public FunctionDecl(string name, List<Param> ps, Block body, Expr arrowBody, bool isStatic = false)
		{ Name = name; Params = ps; Body = body; ArrowBody = arrowBody; Static = isStatic; }
	}

	// ---- Debug printer (compact S-expressions, used by tests) -----------------

	internal static class AstPrinter
	{
		public static string Print(Node n)
		{
			var sb = new StringBuilder();
			Write(sb, n);
			return sb.ToString();
		}

		private static void Args(StringBuilder sb, List<Argument> args)
		{
			foreach (var a in args)
			{
				sb.Append(' ');
				if (a.Name != null) sb.Append(a.Name).Append(':');
				else if (a.NameExpr != null) { Write(sb, a.NameExpr); sb.Append(':'); }
				if (a.Value == null) sb.Append('_');
				else { Write(sb, a.Value); if (a.Spread) sb.Append('*'); }
			}
		}

		private static void Params(StringBuilder sb, List<Param> ps)
		{
			sb.Append('(');
			for (var i = 0; i < ps.Count; i++)
			{
				if (i > 0) sb.Append(' ');
				var p = ps[i];
				if (p.ByRef) sb.Append('&');
				sb.Append(p.Name);
				if (p.Variadic) sb.Append('*');
				if (p.Optional) sb.Append('?');
				if (p.Default != null) { sb.Append(":="); Write(sb, p.Default); }
			}
			sb.Append(')');
		}

		private static void Write(StringBuilder sb, Node n)
		{
			switch (n)
			{
				case ProgramNode p:
					for (var i = 0; i < p.Body.Count; i++) { if (i > 0) sb.Append('\n'); Write(sb, p.Body[i]); }
					break;
				case LiteralExpr l: sb.Append(l.Raw); break;
				case NameExpr v: sb.Append(v.Name); break;
				case GroupExpr g: Write(sb, g.Inner); break;
				case SequenceExpr seq:
					sb.Append("(seq"); foreach (var it in seq.Items) { sb.Append(' '); Write(sb, it); } sb.Append(')');
					break;
				case DerefExpr dr: sb.Append("(deref "); Write(sb, dr.Name); sb.Append(')'); break;
				case UnaryExpr u:
					if (u.Postfix) { sb.Append('('); Write(sb, u.Operand); sb.Append(u.Op == "?" ? "?)" : u.Op + ")"); }
					else { sb.Append('(').Append(u.Op).Append(' '); Write(sb, u.Operand); sb.Append(')'); }
					break;
				case BinaryExpr b:
					sb.Append('(').Append(b.Op == "." ? "concat" : b.Op).Append(' '); Write(sb, b.Left); sb.Append(' '); Write(sb, b.Right); sb.Append(')');
					break;
				case AssignExpr a:
					sb.Append('(').Append(a.Op).Append(' '); Write(sb, a.Target); sb.Append(' '); Write(sb, a.Value); sb.Append(')');
					break;
				case TernaryExpr t:
					sb.Append("(?: "); Write(sb, t.Cond); sb.Append(' '); Write(sb, t.Then); sb.Append(' '); Write(sb, t.Else); sb.Append(')');
					break;
				case CallExpr c:
					sb.Append("(call "); Write(sb, c.Callee); Args(sb, c.Args); sb.Append(')');
					break;
				case MemberExpr m:
					sb.Append(m.NullConditional ? "(?. " : "(. "); Write(sb, m.Target); sb.Append(' ').Append(m.Name).Append(')');
					break;
				case IndexExpr ix:
					sb.Append("(index "); Write(sb, ix.Target); Args(sb, ix.Args); sb.Append(')');
					break;
				case ArrayExpr ar:
					sb.Append("(array"); Args(sb, ar.Elements); sb.Append(')');
					break;
				case MapExpr mp:
					sb.Append("(map");
					foreach (var (k, v) in mp.Entries) { sb.Append(' '); Write(sb, k); sb.Append(':'); Write(sb, v); }
					sb.Append(')');
					break;
				case ObjectExpr o:
					sb.Append("(object");
					foreach (var en in o.Entries) { sb.Append(' '); Write(sb, en.Key); sb.Append(':'); Write(sb, en.Value); }
					sb.Append(')');
					break;
				case FatArrowExpr f:
					sb.Append("(=> "); Params(sb, f.Params); sb.Append(' '); Write(sb, f.Body); sb.Append(')');
					break;
				case ExpressionStmt es: Write(sb, es.Expr); break;
				case Block bl:
					sb.Append("{ ");
					for (var i = 0; i < bl.Body.Count; i++) { if (i > 0) sb.Append("; "); Write(sb, bl.Body[i]); }
					sb.Append(" }");
					break;
				case IfStmt iff:
					sb.Append("(if "); Write(sb, iff.Cond); sb.Append(' '); Write(sb, iff.Then);
					if (iff.Else != null) { sb.Append(" else "); Write(sb, iff.Else); }
					sb.Append(')');
					break;
				case WhileStmt w:
					sb.Append("(while "); Write(sb, w.Cond); sb.Append(' '); Write(sb, w.Body); sb.Append(')');
					break;
				case ReturnStmt r:
					sb.Append("(return"); if (r.Value != null) { sb.Append(' '); Write(sb, r.Value); } sb.Append(')');
					break;
				case DeclStmt d:
					sb.Append('(').Append(d.Keyword); foreach (var it in d.Items) { sb.Append(' '); Write(sb, it); } sb.Append(')');
					break;
				case LoopStmt lp:
					sb.Append("(loop "); if (lp.Count != null) { Write(sb, lp.Count); sb.Append(' '); } Write(sb, lp.Body); sb.Append(')');
					break;
				case ForStmt fr:
					sb.Append("(for ").Append(string.Join(",", fr.Vars)).Append(" in "); Write(sb, fr.Enumerable); sb.Append(' '); Write(sb, fr.Body); sb.Append(')');
					break;
				case SwitchStmt sw:
					sb.Append("(switch "); Write(sb, sw.Value);
					foreach (var cs in sw.Cases)
					{
						sb.Append(" (case");
						foreach (var v in cs.Values) { sb.Append(' '); Write(sb, v); }
						sb.Append(" ->"); foreach (var st in cs.Body) { sb.Append(' '); Write(sb, st); } sb.Append(')');
					}
					if (sw.Default != null) { sb.Append(" (default ->"); foreach (var st in sw.Default) { sb.Append(' '); Write(sb, st); } sb.Append(')'); }
					sb.Append(')');
					break;
				case TryStmt tr:
					sb.Append("(try "); Write(sb, tr.Body);
					foreach (var cb in tr.Catches)
					{
						sb.Append(" catch");
						if (cb.Types.Count > 0) sb.Append('[').Append(string.Join(",", cb.Types)).Append(']');
						if (cb.Var != null) sb.Append(' ').Append(cb.Var);
						sb.Append(' '); Write(sb, cb.Body);
					}
					if (tr.Else != null) { sb.Append(" else "); Write(sb, tr.Else); }
					if (tr.Finally != null) { sb.Append(" finally "); Write(sb, tr.Finally); }
					sb.Append(')');
					break;
				case ThrowStmt th:
					sb.Append("(throw"); if (th.Value != null) { sb.Append(' '); Write(sb, th.Value); } sb.Append(')');
					break;
				case BreakStmt: sb.Append("(break)"); break;
				case ContinueStmt: sb.Append("(continue)"); break;
				case DirectiveStmt dir: sb.Append("(directive ").Append(dir.Name).Append(')'); break;
				case HotkeyDef hk:
					sb.Append("(hotkey ").Append(string.Join(",", hk.Triggers)).Append(' ');
					if (hk.Func != null) Write(sb, hk.Func); else Write(sb, hk.Body);
					sb.Append(')');
					break;
				case RemapDef rm: sb.Append("(remap ").Append(rm.Source).Append("::").Append(rm.Target).Append(')'); break;
				case ExportStmt ex: sb.Append(ex.Default ? "(export-default " : "(export "); Write(sb, ex.Decl); sb.Append(')'); break;
				case HotstringDef hs:
					sb.Append("(hotstring ").Append(string.Join(",", hs.Triggers));
					if (hs.Expansion != null) sb.Append(" => \"").Append(hs.Expansion).Append('"');
					else if (hs.Func != null) { sb.Append(' '); Write(sb, hs.Func); }
					else { sb.Append(' '); Write(sb, hs.Body); }
					sb.Append(')');
					break;
				case FunctionDecl fn:
					sb.Append("(func ").Append(fn.Name).Append(' '); Params(sb, fn.Params); sb.Append(' ');
					if (fn.ArrowBody != null) Write(sb, fn.ArrowBody); else Write(sb, fn.Body);
					sb.Append(')');
					break;
				case ClassDecl cl:
					sb.Append("(class ").Append(cl.Name);
					if (cl.Base != null) sb.Append(" extends ").Append(cl.Base);
					foreach (var fld in cl.Fields)
					{
						sb.Append(fld.Static ? " static-field " : " field ").Append(fld.Name);
						if (fld.Init != null) { sb.Append(":="); Write(sb, fld.Init); }
					}
					foreach (var mth in cl.Methods)
					{
						sb.Append(mth.Static ? " static-method " : " method ").Append(mth.Name).Append(' ');
						Params(sb, mth.Params);
					}
					foreach (var prop in cl.Properties)
					{
						sb.Append(prop.Static ? " static-prop " : " prop ").Append(prop.Name);
						if (prop.HasGet) sb.Append(" get");
						if (prop.HasSet) sb.Append(" set");
					}
					sb.Append(')');
					break;
				default: sb.Append('?'); break;
			}
		}
	}
}
