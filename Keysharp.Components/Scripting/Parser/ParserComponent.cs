using Keysharp.Components.Scripting;

namespace Keysharp.Components.Scripting.Parser;

public sealed class ParserComponent : IScriptSyntaxValidator, IScriptTokenizer
{
	public string Id => ScriptingComponentIds.Parser;
	public ScriptingCapability Capabilities => ScriptingCapability.SyntaxValidation | ScriptingCapability.Tokenization;

	public IReadOnlyList<ScriptToken> Tokenize(string source)
	{
		var tokens = Keysharp.Parsing.Lexing.Lexer.Tokenize(source ?? "");
		var result = new ScriptToken[tokens.Count];

		for (var i = 0; i < tokens.Count; i++)
			result[i] = new(ToKind(tokens[i].Kind), tokens[i].Offset, tokens[i].Length);

		return result;
	}

	/// <summary>
	/// Maps the lexer's internal kind onto the published one. Written out rather than cast so the internal enum can
	/// be reordered or renamed without breaking the contract; TokenizerContractTests.MapsEveryKind catches a new
	/// internal kind that nobody mapped.
	/// </summary>
	private static ScriptTokenKind ToKind(Keysharp.Parsing.Lexing.TokenKind kind) => kind switch
	{
		Keysharp.Parsing.Lexing.TokenKind.EOF => ScriptTokenKind.EndOfFile,
		Keysharp.Parsing.Lexing.TokenKind.Unknown => ScriptTokenKind.Unknown,
		Keysharp.Parsing.Lexing.TokenKind.Newline => ScriptTokenKind.Newline,
		Keysharp.Parsing.Lexing.TokenKind.Comment => ScriptTokenKind.Comment,
		Keysharp.Parsing.Lexing.TokenKind.Directive => ScriptTokenKind.Directive,
		Keysharp.Parsing.Lexing.TokenKind.ContinuationDelimiter => ScriptTokenKind.ContinuationDelimiter,
		Keysharp.Parsing.Lexing.TokenKind.Number => ScriptTokenKind.Number,
		Keysharp.Parsing.Lexing.TokenKind.String => ScriptTokenKind.String,
		Keysharp.Parsing.Lexing.TokenKind.Identifier => ScriptTokenKind.Identifier,
		Keysharp.Parsing.Lexing.TokenKind.LParen => ScriptTokenKind.LParen,
		Keysharp.Parsing.Lexing.TokenKind.RParen => ScriptTokenKind.RParen,
		Keysharp.Parsing.Lexing.TokenKind.LBracket => ScriptTokenKind.LBracket,
		Keysharp.Parsing.Lexing.TokenKind.RBracket => ScriptTokenKind.RBracket,
		Keysharp.Parsing.Lexing.TokenKind.LBrace => ScriptTokenKind.LBrace,
		Keysharp.Parsing.Lexing.TokenKind.RBrace => ScriptTokenKind.RBrace,
		Keysharp.Parsing.Lexing.TokenKind.Comma => ScriptTokenKind.Comma,
		Keysharp.Parsing.Lexing.TokenKind.Colon => ScriptTokenKind.Colon,
		Keysharp.Parsing.Lexing.TokenKind.DoubleColon => ScriptTokenKind.DoubleColon,
		Keysharp.Parsing.Lexing.TokenKind.Ellipsis => ScriptTokenKind.Ellipsis,
		Keysharp.Parsing.Lexing.TokenKind.Hash => ScriptTokenKind.Hash,
		Keysharp.Parsing.Lexing.TokenKind.Dot => ScriptTokenKind.Dot,
		Keysharp.Parsing.Lexing.TokenKind.Question => ScriptTokenKind.Question,
		Keysharp.Parsing.Lexing.TokenKind.QuestionDot => ScriptTokenKind.QuestionDot,
		Keysharp.Parsing.Lexing.TokenKind.FatArrow => ScriptTokenKind.FatArrow,
		Keysharp.Parsing.Lexing.TokenKind.Assign => ScriptTokenKind.Assign,
		Keysharp.Parsing.Lexing.TokenKind.Equal => ScriptTokenKind.Equal,
		Keysharp.Parsing.Lexing.TokenKind.PlusAssign => ScriptTokenKind.PlusAssign,
		Keysharp.Parsing.Lexing.TokenKind.MinusAssign => ScriptTokenKind.MinusAssign,
		Keysharp.Parsing.Lexing.TokenKind.StarAssign => ScriptTokenKind.StarAssign,
		Keysharp.Parsing.Lexing.TokenKind.SlashAssign => ScriptTokenKind.SlashAssign,
		Keysharp.Parsing.Lexing.TokenKind.IntDivAssign => ScriptTokenKind.IntDivAssign,
		Keysharp.Parsing.Lexing.TokenKind.PowerAssign => ScriptTokenKind.PowerAssign,
		Keysharp.Parsing.Lexing.TokenKind.PercentAssign => ScriptTokenKind.PercentAssign,
		Keysharp.Parsing.Lexing.TokenKind.DotAssign => ScriptTokenKind.DotAssign,
		Keysharp.Parsing.Lexing.TokenKind.BitAndAssign => ScriptTokenKind.BitAndAssign,
		Keysharp.Parsing.Lexing.TokenKind.BitOrAssign => ScriptTokenKind.BitOrAssign,
		Keysharp.Parsing.Lexing.TokenKind.BitXorAssign => ScriptTokenKind.BitXorAssign,
		Keysharp.Parsing.Lexing.TokenKind.ShiftLeftAssign => ScriptTokenKind.ShiftLeftAssign,
		Keysharp.Parsing.Lexing.TokenKind.ShiftRightAssign => ScriptTokenKind.ShiftRightAssign,
		Keysharp.Parsing.Lexing.TokenKind.ShiftRightLogicalAssign => ScriptTokenKind.ShiftRightLogicalAssign,
		Keysharp.Parsing.Lexing.TokenKind.NullCoalesceAssign => ScriptTokenKind.NullCoalesceAssign,
		Keysharp.Parsing.Lexing.TokenKind.Identity => ScriptTokenKind.Identity,
		Keysharp.Parsing.Lexing.TokenKind.NotIdentity => ScriptTokenKind.NotIdentity,
		Keysharp.Parsing.Lexing.TokenKind.NotEqual => ScriptTokenKind.NotEqual,
		Keysharp.Parsing.Lexing.TokenKind.Less => ScriptTokenKind.Less,
		Keysharp.Parsing.Lexing.TokenKind.Greater => ScriptTokenKind.Greater,
		Keysharp.Parsing.Lexing.TokenKind.LessEqual => ScriptTokenKind.LessEqual,
		Keysharp.Parsing.Lexing.TokenKind.GreaterEqual => ScriptTokenKind.GreaterEqual,
		Keysharp.Parsing.Lexing.TokenKind.RegexMatch => ScriptTokenKind.RegexMatch,
		Keysharp.Parsing.Lexing.TokenKind.NotRegexMatch => ScriptTokenKind.NotRegexMatch,
		Keysharp.Parsing.Lexing.TokenKind.Plus => ScriptTokenKind.Plus,
		Keysharp.Parsing.Lexing.TokenKind.Minus => ScriptTokenKind.Minus,
		Keysharp.Parsing.Lexing.TokenKind.Star => ScriptTokenKind.Star,
		Keysharp.Parsing.Lexing.TokenKind.Slash => ScriptTokenKind.Slash,
		Keysharp.Parsing.Lexing.TokenKind.IntDiv => ScriptTokenKind.IntDiv,
		Keysharp.Parsing.Lexing.TokenKind.Power => ScriptTokenKind.Power,
		Keysharp.Parsing.Lexing.TokenKind.Percent => ScriptTokenKind.Percent,
		Keysharp.Parsing.Lexing.TokenKind.BitAnd => ScriptTokenKind.BitAnd,
		Keysharp.Parsing.Lexing.TokenKind.BitOr => ScriptTokenKind.BitOr,
		Keysharp.Parsing.Lexing.TokenKind.BitXor => ScriptTokenKind.BitXor,
		Keysharp.Parsing.Lexing.TokenKind.BitNot => ScriptTokenKind.BitNot,
		Keysharp.Parsing.Lexing.TokenKind.ShiftLeft => ScriptTokenKind.ShiftLeft,
		Keysharp.Parsing.Lexing.TokenKind.ShiftRight => ScriptTokenKind.ShiftRight,
		Keysharp.Parsing.Lexing.TokenKind.ShiftRightLogical => ScriptTokenKind.ShiftRightLogical,
		Keysharp.Parsing.Lexing.TokenKind.LogicalAnd => ScriptTokenKind.LogicalAnd,
		Keysharp.Parsing.Lexing.TokenKind.LogicalOr => ScriptTokenKind.LogicalOr,
		Keysharp.Parsing.Lexing.TokenKind.Not => ScriptTokenKind.Not,
		Keysharp.Parsing.Lexing.TokenKind.PlusPlus => ScriptTokenKind.PlusPlus,
		Keysharp.Parsing.Lexing.TokenKind.MinusMinus => ScriptTokenKind.MinusMinus,
		Keysharp.Parsing.Lexing.TokenKind.NullCoalesce => ScriptTokenKind.NullCoalesce,
		Keysharp.Parsing.Lexing.TokenKind.HotkeyTrigger => ScriptTokenKind.HotkeyTrigger,
		Keysharp.Parsing.Lexing.TokenKind.RemapSourceKey => ScriptTokenKind.RemapSourceKey,
		Keysharp.Parsing.Lexing.TokenKind.RemapTargetKey => ScriptTokenKind.RemapTargetKey,
		Keysharp.Parsing.Lexing.TokenKind.HotstringTrigger => ScriptTokenKind.HotstringTrigger,
		Keysharp.Parsing.Lexing.TokenKind.HotstringExpansion => ScriptTokenKind.HotstringExpansion,
		Keysharp.Parsing.Lexing.TokenKind.CSharpBlock => ScriptTokenKind.CSharpBlock,
		_ => ScriptTokenKind.Unknown,
	};

	public ScriptSyntaxValidationResult ValidateSyntax(ScriptSyntaxValidationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var (program, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(
			request.SourceText ?? "", request.IncludeDirectory, request.ScriptPath, request.Defines);
		return new()
		{
			Diagnostics = diagnostics.Select(diagnostic => ToDiagnostic(diagnostic, request.ScriptPath)).ToArray(),
			ErrorStdOut = program.ErrorStdOut,
		};
	}

	private static ScriptDiagnostic ToDiagnostic(string diagnostic, string defaultFile)
	{
		var match = Regex.Match(diagnostic ?? "", @"^(?:(.*):)?(\d+):(\d+):\s*(.*)$");
		return match.Success
			? new(match.Groups[4].Value, ScriptDiagnosticSeverity.Error,
				match.Groups[1].Success ? match.Groups[1].Value : defaultFile ?? "",
				int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
			: new(diagnostic ?? "", FilePath: defaultFile ?? "");
	}
}
