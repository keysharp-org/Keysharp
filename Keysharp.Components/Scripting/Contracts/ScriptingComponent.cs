using System;
using System.Collections.Generic;
using System.Linq;

namespace Keysharp.Components.Scripting;

public static class ScriptingComponentIds
{
	public const string Parser = "parser";
	public const string Compiler = "compiler";
}

[Flags]
public enum ScriptingCapability
{
	None = 0,
	SyntaxValidation = 1,
	Compilation = 2,
	Tokenization = 4,
}

/// <summary>
/// A lexical token kind, at the fidelity the lexer distinguishes: a consumer that must tell <c>:=</c> from <c>+</c>
/// cannot recover that from a coarse label. Use <see cref="ScriptTokenKindExtensions.Category"/> when the grouping is
/// all you need. New members may be added, so treat an unrecognized kind as
/// <see cref="ScriptTokenCategory.Other"/> rather than failing.
/// </summary>
public enum ScriptTokenKind
{
	Unknown = 0,
	EndOfFile,
	Newline,
	/// <summary>A line (<c>;</c>) or block (<c>/* */</c>) comment.</summary>
	Comment,
	/// <summary>A directive that carries no value to the parser — currently the <c>#EndCSharp</c> block closer.</summary>
	Directive,
	/// <summary>The <c>(</c> or <c>)</c> delimiting a code continuation section — a splice marker, not a grouping paren.</summary>
	ContinuationDelimiter,

	Number,
	String,
	Identifier,

	LParen, RParen,
	LBracket, RBracket,
	LBrace, RBrace,
	Comma,
	Colon,
	DoubleColon,
	Ellipsis,
	Hash,

	Dot,
	Question,
	QuestionDot,
	FatArrow,

	Assign,
	Equal,
	PlusAssign,
	MinusAssign,
	StarAssign,
	SlashAssign,
	IntDivAssign,
	PowerAssign,
	PercentAssign,
	DotAssign,
	BitAndAssign,
	BitOrAssign,
	BitXorAssign,
	ShiftLeftAssign,
	ShiftRightAssign,
	ShiftRightLogicalAssign,
	NullCoalesceAssign,

	Identity,
	NotIdentity,
	NotEqual,
	Less,
	Greater,
	LessEqual,
	GreaterEqual,
	RegexMatch,
	NotRegexMatch,

	Plus,
	Minus,
	Star,
	Slash,
	IntDiv,
	Power,
	Percent,

	BitAnd,
	BitOr,
	BitXor,
	BitNot,
	ShiftLeft,
	ShiftRight,
	ShiftRightLogical,

	LogicalAnd,
	LogicalOr,
	Not,

	PlusPlus,
	MinusMinus,
	NullCoalesce,

	HotkeyTrigger,
	RemapSourceKey,
	RemapTargetKey,
	HotstringTrigger,
	HotstringExpansion,

	/// <summary>Verbatim body of a <c>#CSharp</c> block — C# source, not AutoHotkey.</summary>
	CSharpBlock,
}

/// <summary>Coarse grouping of <see cref="ScriptTokenKind"/>, for consumers that do not care which operator it is.</summary>
public enum ScriptTokenCategory
{
	Other = 0,
	Trivia,
	Literal,
	Identifier,
	Operator,
	Punctuation,
	Hotkey,
	EmbeddedCode,
}

/// <summary>
/// One lexical token. <see cref="Offset"/>/<see cref="Length"/> index the source string that produced it, which is
/// how the text is recovered — the token does not carry a copy.
/// </summary>
public readonly struct ScriptToken
{
	public ScriptToken(ScriptTokenKind kind, int offset, int length)
	{
		Kind = kind;
		Offset = offset;
		Length = length;
	}

	public ScriptTokenKind Kind { get; }
	public int Offset { get; }
	public int Length { get; }

	/// <summary>The token's text, sliced out of the source it was produced from.</summary>
	public ReadOnlySpan<char> Text(string source) => source.AsSpan(Offset, Length);
}

public static class ScriptTokenKindExtensions
{
	/// <summary>Groups a kind into its coarse category. An unrecognized kind is <see cref="ScriptTokenCategory.Other"/>.</summary>
	public static ScriptTokenCategory Category(this ScriptTokenKind kind) => kind switch
	{
		ScriptTokenKind.Comment or ScriptTokenKind.Directive or ScriptTokenKind.Newline
			or ScriptTokenKind.EndOfFile => ScriptTokenCategory.Trivia,
		ScriptTokenKind.Number or ScriptTokenKind.String => ScriptTokenCategory.Literal,
		ScriptTokenKind.Identifier => ScriptTokenCategory.Identifier,
		ScriptTokenKind.CSharpBlock => ScriptTokenCategory.EmbeddedCode,
		ScriptTokenKind.HotkeyTrigger or ScriptTokenKind.RemapSourceKey or ScriptTokenKind.RemapTargetKey
			or ScriptTokenKind.HotstringTrigger or ScriptTokenKind.HotstringExpansion => ScriptTokenCategory.Hotkey,
		ScriptTokenKind.LParen or ScriptTokenKind.RParen or ScriptTokenKind.LBracket or ScriptTokenKind.RBracket
			or ScriptTokenKind.LBrace or ScriptTokenKind.RBrace or ScriptTokenKind.Comma or ScriptTokenKind.Colon
			or ScriptTokenKind.DoubleColon or ScriptTokenKind.Ellipsis or ScriptTokenKind.Hash
			or ScriptTokenKind.ContinuationDelimiter => ScriptTokenCategory.Punctuation,
		ScriptTokenKind.Unknown => ScriptTokenCategory.Other,
		_ => ScriptTokenCategory.Operator,
	};
}

public enum ScriptCompilationOutput
{
	InMemory,
	Assembly,
	Executable,
	MinimalExecutable,
}

public enum ScriptDiagnosticSeverity
{
	Info,
	Warning,
	Error,
}

public sealed record ScriptDiagnostic(
	string Message,
	ScriptDiagnosticSeverity Severity = ScriptDiagnosticSeverity.Error,
	string FilePath = "",
	int Line = 0,
	int Column = 0);

public sealed class ScriptSyntaxValidationRequest
{
	public string SourceText { get; init; }
	public string ScriptPath { get; init; }
	public string IncludeDirectory { get; init; }
	public IReadOnlyCollection<string> Defines { get; init; } = Array.Empty<string>();
}

public sealed class ScriptSyntaxValidationResult
{
	public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = Array.Empty<ScriptDiagnostic>();
	public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != ScriptDiagnosticSeverity.Error);
}

public sealed class ScriptCompileRequest
{
	public string SourceText { get; init; }
	public string ScriptPath { get; init; }
	public string CompilationName { get; init; }
	public string RuntimeDirectory { get; init; }
	public string IncludeDirectory { get; init; }
	public IReadOnlyCollection<string> Defines { get; init; } = Array.Empty<string>();
	public IReadOnlyCollection<string> AdditionalComponents { get; init; } = Array.Empty<string>();
	public IReadOnlyCollection<string> ExcludedComponents { get; init; } = Array.Empty<string>();
	public ScriptCompilationOutput Output { get; init; }
	public bool EmitGeneratedCode { get; init; }
	public bool AllowPackageRestore { get; init; } = true;
}

public interface IScriptCompilationResult
{
	bool Success { get; }
	byte[] AssemblyBytes { get; }
	string GeneratedCode { get; }
	string ErrorText { get; }
	string WarningText { get; }
	string InlineCode { get; }
	IReadOnlyCollection<string> RequiredComponents { get; }

	/// <summary>
	/// True when the script carries `#ConsoleApp`, asking for a console (CUI) executable rather than a GUI one.
	/// Only an executable can honour it: Windows fixes the choice in the PE subsystem field before the process
	/// starts, so the host applies it when it stamps the apphost.
	/// </summary>
	bool ConsoleApp { get; }
}

public interface IScriptingComponent
{
	string Id { get; }
	ScriptingCapability Capabilities { get; }
}

public interface IScriptSyntaxValidator : IScriptingComponent
{
	ScriptSyntaxValidationResult ValidateSyntax(ScriptSyntaxValidationRequest request);
}

/// <summary>
/// Lexes script source into tokens. The stream is LOSSLESS: every non-whitespace character falls inside exactly one
/// token, ordered by <see cref="ScriptToken.Offset"/> and never overlapping. Spans the parser itself discards
/// (comments, <c>#EndCSharp</c>, continuation-section delimiters) are published too, under kinds naming them, so a
/// consumer can classify a whole file instead of reimplementing the language's lexical rules.
/// </summary>
public interface IScriptTokenizer : IScriptingComponent
{
	/// <summary>Tokenizes <paramref name="source"/>. Never throws on malformed or half-typed input.</summary>
	IReadOnlyList<ScriptToken> Tokenize(string source);
}

public interface IScriptCompiler : IScriptingComponent
{
	IScriptCompilationResult Compile(ScriptCompileRequest request);
	string DeploySupportFiles(IScriptCompilationResult result, string destinationDirectory);
}
