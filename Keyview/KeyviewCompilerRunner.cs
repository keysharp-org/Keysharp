namespace Keyview;

internal sealed record KeyviewCompileResult(byte[] AssemblyBytes, string TrimmedCode, Lazy<string> FullCode,
	string Error, TimeSpan Elapsed)
{
	internal bool Success => AssemblyBytes != null;
}

internal static class KeyviewCompilerRunner
{
	internal static IScriptCompiler GetCompiler() =>
		ScriptingComponentRegistry.TryGetCompiler(out var compiler, out var error)
			? compiler
			: throw new InvalidOperationException(error);

	internal static string IncludeDirFor(KeyviewDocumentState document) => document.IsScratch
		? Directory.GetCurrentDirectory() : Path.GetDirectoryName(document.CurrentFilePath);

	internal static Task<KeyviewCompileResult> RunCompile(string inputText, string includeDir, IScriptCompiler compiler) =>
		Task.Run(() =>
		{
			var elapsed = Stopwatch.StartNew();
			try
			{
				var result = compiler.Compile(new ScriptCompileRequest
				{
					SourceText = inputText,
					CompilationName = "Keyview",
					RuntimeDirectory = Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath)),
					IncludeDirectory = includeDir,
					Output = ScriptCompilationOutput.InMemory,
					EmitGeneratedCode = true,
					AllowPackageRestore = false
				});
				var inline = result.InlineCode is { Length: > 0 } inlineCode
					? Environment.NewLine + Environment.NewLine
					  + "// ---- #CSharp (compiled as a separate file) ----" + Environment.NewLine + Environment.NewLine + inlineCode : "";
				var code = string.Join(Environment.NewLine + Environment.NewLine,
					new[] { result.ErrorText, result.GeneratedCode }.Where(text => text != null)) + inline;
				if (!result.Success)
					return new KeyviewCompileResult(null, code, new Lazy<string>(() => code), result.ErrorText ?? code, elapsed.Elapsed);

				const string token = "[System.STAThread]";
				var start = code.IndexOf(token, StringComparison.Ordinal);
				var display = start < 0 ? code : code[(start + token.Length)..].TrimStart('\r', '\n');
				var trimmed = new StringBuilder(display.Length);
				foreach (var line in display.SplitLines())
					_ = trimmed.AppendLine(line.TrimNofAnyFromStart("{}\t", 2));
				return new KeyviewCompileResult(result.AssemblyBytes, trimmed.ToString().TrimEnd('\n', '\r'),
					new Lazy<string>(() => result.CompiledCode is { } compiled ? compiled + inline : code), null, elapsed.Elapsed);
			}
			catch (Exception ex)
			{
				var error = ex.ToString();
				return new KeyviewCompileResult(null, error, new Lazy<string>(() => error), error, elapsed.Elapsed);
			}
		});
}