using Keysharp.Components.Scripting;
using Keysharp.Internals.Scripting;
using Keysharp.Compilation;

namespace Keysharp.Components.Scripting.Compiler;

public sealed class CompilerComponent : IScriptCompiler
{
	public string Id => ScriptingComponentIds.Compiler;
	public ScriptingCapability Capabilities => ScriptingCapability.Compilation;

	public IScriptCompilationResult Compile(ScriptCompileRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var hasText = request.SourceText != null;
		var hasPath = !string.IsNullOrWhiteSpace(request.ScriptPath);
		if (hasText == hasPath)
			throw new ArgumentException("Specify exactly one of SourceText or ScriptPath.", nameof(request));
		var additionalComponents = NormalizeComponentIds(request.AdditionalComponents, nameof(request.AdditionalComponents));
		var excludedComponents = NormalizeComponentIds(request.ExcludedComponents, nameof(request.ExcludedComponents));
		if (additionalComponents.Intersect(excludedComponents, StringComparer.OrdinalIgnoreCase).Any())
			throw new ArgumentException("A scripting component cannot be both included and excluded.", nameof(request));

		using var context = CompilationContext.CreateIfNeeded();
		var helper = new CompilerHelper();
		var runtimeDirectory = string.IsNullOrWhiteSpace(request.RuntimeDirectory)
			? AppContext.BaseDirectory
			: request.RuntimeDirectory;
		var minimal = request.Output == ScriptCompilationOutput.MinimalExecutable;
		var writesArtifact = request.Output != ScriptCompilationOutput.InMemory;
		var (bytes, text, compilation) = helper.CompileCodeToByteArray(
			hasPath ? request.ScriptPath : request.SourceText, request.CompilationName, runtimeDirectory, minimal,
			request.EmitGeneratedCode, writesArtifact, request.Defines, request.AllowPackageRestore,
			additionalComponents, request.IncludeDirectory, excludedComponents, hasPath);
		return new CompilationResult(request, runtimeDirectory, bytes, text, compilation);
	}

	private static IReadOnlyCollection<string> NormalizeComponentIds(IEnumerable<string> values, string parameterName)
	{
		var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var value in values ?? [])
		{
			var id = value?.Trim().ToLowerInvariant();
			if (id is not (ScriptingComponentIds.Parser or ScriptingComponentIds.Compiler))
				throw new ArgumentException($"Unknown first-party scripting component '{value}'.", parameterName);
			_ = normalized.Add(id);
		}
		return normalized.ToArray();
	}

	public string DeploySupportFiles(IScriptCompilationResult result, string destinationDirectory)
	{
		if (result is not CompilationResult compilationResult)
			throw new ArgumentException("The compilation result was not created by this compiler.", nameof(result));
		return compilationResult.DeploySupportFiles(destinationDirectory);
	}

	private sealed class CompilationContext : IDisposable
	{
		private readonly Keysharp.Runtime.Script owned;
		private CompilationContext(Keysharp.Runtime.Script owned) => this.owned = owned;
		internal static CompilationContext CreateIfNeeded() =>
			new(Keysharp.Runtime.Script.TheScript == null ? new Keysharp.Runtime.Script() : null);
		public void Dispose() => owned?.Dispose();
	}

	private sealed class CompilationResult : IScriptCompilationResult
	{
		private readonly ScriptCompileRequest request;
		private readonly string runtimeDirectory;
		private readonly ScriptCompilationResult compilation;

		internal CompilationResult(ScriptCompileRequest request, string runtimeDirectory, byte[] bytes, string text, ScriptCompilationResult compilation)
		{
			this.request = request;
			this.runtimeDirectory = runtimeDirectory;
			this.compilation = compilation;
			AssemblyBytes = bytes;
			GeneratedCode = bytes == null ? null : text;
			ErrorText = bytes == null ? text : null;
		}

		public bool Success => AssemblyBytes != null;
		public byte[] AssemblyBytes { get; }
		public string GeneratedCode { get; }
		public string ErrorText { get; }
		public string WarningText => compilation?.Warnings;
		public string InlineCode => compilation?.InlineCode;
		public IReadOnlyCollection<string> RequiredComponents => compilation?.RequiredComponents ?? [];
		public bool ConsoleApp => compilation?.ConsoleApp ?? false;

		internal string DeploySupportFiles(string destination)
		{
			if (!Success || string.IsNullOrWhiteSpace(destination))
				return null;

			if (request.Output is ScriptCompilationOutput.Executable or ScriptCompilationOutput.MinimalExecutable)
			{
				var runtimeError = CopyRuntime(destination);
				if (runtimeError != null)
					return runtimeError;
			}

			if (request.Output != ScriptCompilationOutput.MinimalExecutable && compilation?.Packages?.CopyTo(destination) is { } packageError)
				return packageError;

			if (request.Output == ScriptCompilationOutput.MinimalExecutable)
				return null;

			if (compilation.RequiredProviders is { Count: > 0 })
			{
				if (!CompiledPackageProviderManifest.TryBuild(compilation.RequiredProviders, out var providers, out var providerFailure))
					return providerFailure;
				if (providers.CopyTo(destination) is { } providerCopyError)
					return providerCopyError;
			}

			if (compilation.RequiredComponents is { Count: > 0 })
			{
				if (!CompiledScriptingComponentManifest.TryBuild(compilation.RequiredComponents, out var components, out var componentFailure))
					return componentFailure;
				if (components.CopyTo(destination) is { } componentCopyError)
					return componentCopyError;
			}

			return null;
		}

		private string CopyRuntime(string destination)
		{
			var sourceRoot = runtimeDirectory;

			var dependencies = request.Output == ScriptCompilationOutput.MinimalExecutable
				? new[] { "Keysharp.Core.dll" }
				: CompilerHelper.requiredManagedDependencies.Concat(
					CompilerHelper.requiredNativeDependencies.Select(CompilerHelper.GetRidNativeDependencyPath));

			try
			{
				foreach (var dependency in dependencies)
				{
					var source = CompilerHelper.requiredNativeDependencies.Contains(Path.GetFileName(dependency), StringComparer.OrdinalIgnoreCase)
						? CompilerHelper.ResolveAppNativeDependencyPath(sourceRoot, Path.GetFileName(dependency))
						: Path.Combine(sourceRoot, dependency);
					if (!File.Exists(source))
						continue;

					var target = Path.GetFullPath(Path.Combine(destination, dependency));
					if (Path.GetFullPath(source).Equals(target, PathComparison))
						continue;
					_ = Directory.CreateDirectory(Path.GetDirectoryName(target));
					File.Copy(source, target, true);
				}
			}
			catch (Exception exception)
			{
				return $"Copying runtime dependencies to {destination} failed: {exception.Message}";
			}

			return null;
		}

#if WINDOWS
		private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
#else
		private const StringComparison PathComparison = StringComparison.Ordinal;
#endif
	}
}
