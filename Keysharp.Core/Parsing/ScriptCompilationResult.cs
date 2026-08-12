using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keysharp.Parsing
{
	internal sealed record InlineCSharpSource(string Module, string Code);

	/// <summary>Outputs and diagnostics for one script compilation.</summary>
	public sealed class ScriptCompilationResult
	{
		/// <summary>The lowered tree, or null on failure.</summary>
		public CompilationUnitSyntax Unit { get; internal set; }

		internal string ScriptPath { get; set; }

		/// <summary>Parse and lower diagnostics.</summary>
		public CompilerErrorCollection Errors { get; } = new();

		/// <summary>The generated inline C# tree source, if any.</summary>
		public string InlineCode { get; internal set; }

		internal IReadOnlyList<InlineCSharpSource> InlineSources { get; set; } = [];

		internal IReadOnlyCollection<string> InlineDefines { get; set; } = [];

		internal IReadOnlyCollection<string> RequiredProviders { get; set; } = [];

		internal PackageManifest Packages { get; set; }

		internal string DeclaredAssemblyName { get; set; }

		/// <summary>Formatted script and inline-C# warnings.</summary>
		public string Warnings { get; internal set; }

		internal void AppendWarnings(string more)
		{
			if (!string.IsNullOrEmpty(more))
				Warnings = string.IsNullOrEmpty(Warnings) ? more.Trim() : Warnings + "\n" + more.Trim();
		}
	}
}
