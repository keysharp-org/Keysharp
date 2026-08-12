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

public interface IScriptCompiler : IScriptingComponent
{
	IScriptCompilationResult Compile(ScriptCompileRequest request);
	string DeploySupportFiles(IScriptCompilationResult result, string destinationDirectory);
}
