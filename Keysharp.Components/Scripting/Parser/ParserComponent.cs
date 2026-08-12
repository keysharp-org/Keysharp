using Keysharp.Components.Scripting;

namespace Keysharp.Components.Scripting.Parser;

public sealed class ParserComponent : IScriptSyntaxValidator
{
	public string Id => ScriptingComponentIds.Parser;
	public ScriptingCapability Capabilities => ScriptingCapability.SyntaxValidation;

	public ScriptSyntaxValidationResult ValidateSyntax(ScriptSyntaxValidationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var (_, diagnostics) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(
			request.SourceText ?? "", request.IncludeDirectory, request.ScriptPath, request.Defines);
		return new() { Diagnostics = diagnostics.Select(diagnostic => ToDiagnostic(diagnostic, request.ScriptPath)).ToArray() };
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
