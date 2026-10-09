namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class ScriptingComponentTests : TestRunner
{
	[SetUp]
	public void ResetComponents() => ScriptingComponentRegistry.ResetForTests();

	[TearDown]
	public void ClearComponents() => ScriptingComponentRegistry.ResetForTests();

	private const string embeddedCompilerSource = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { IsComponentAvailable, RunScript }\nif IsComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n";
	private const string leanBody = "#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\nFileAppend('lean-pass', '*')\nExitApp(0)\n";
	private readonly string sharedRoot = Path.Combine(Path.GetTempPath(), "ks-component-shared-" + Guid.NewGuid().ToString("N"));
	private readonly Lazy<IScriptCompilationResult> embeddedCompilerBuild;
	private readonly Lazy<string> leanExecutable;
	private readonly Lazy<(string Target, string HostRoot)> cksTarget;

	// Artifacts that several tests only read are built on first use, so a filtered run pays only for what it needs.
	public ScriptingComponentTests()
	{
		embeddedCompilerBuild = new(() => CompileEmbeddedCompiler("embedded-compiler"));
		leanExecutable = new(BuildLeanExecutable);
		cksTarget = new(BuildCksTarget);
	}

	[OneTimeTearDown]
	public void DeleteSharedArtifacts()
	{
		try { Directory.Delete(sharedRoot, true); } catch { }
	}

	[Test]
	public void RoslynIsolation()
	{
		var coreReferences = typeof(Script).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
		Assert.That(coreReferences.Any(name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)),
			Is.False,
			"Keysharp.Core must remain runnable without Roslyn.");

		Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
		Assert.That(parser.Id, Is.EqualTo(ScriptingComponentIds.Parser));
		var parserReferences = parser.GetType().Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
		Assert.That(parserReferences.Any(name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)),
			Is.False,
			"the parser component must support validation without Roslyn.");

		Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var compiler, out failure), failure);
		Assert.That(compiler.Id, Is.EqualTo(ScriptingComponentIds.Compiler));
		Assert.IsTrue((compiler.Capabilities & ScriptingCapability.Compilation) != 0);
	}

	[Test]
	public void ParserOnly()
	{
		Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
		Assert.IsTrue(parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := 1" }).Success);
		var scriptPath = Path.GetFullPath(Path.Combine("component-tests", "invalid.ahk"));
		var invalid = parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := (", ScriptPath = scriptPath });
		Assert.That(invalid.Success, Is.False);
		Assert.IsNotEmpty(invalid.Diagnostics);
		Assert.IsTrue(invalid.Diagnostics.All(diagnostic => diagnostic.FilePath == scriptPath),
			"parser diagnostics should retain the caller's complete script path");

		var routed = parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "#ErrorStdOut\nx := (" });
		Assert.That(routed.Success, Is.False);
		Assert.IsTrue(routed.ErrorStdOut);
	}

	[Test]
	public void WarningSuccess()
	{
		var warning = new ScriptSyntaxValidationResult
		{
			Diagnostics = [new("warning", ScriptDiagnosticSeverity.Warning)]
		};
		Assert.IsTrue(warning.Success);
		Assert.That(new ScriptSyntaxValidationResult
		{
			Diagnostics = [new("error", ScriptDiagnosticSeverity.Error)]
		}.Success, Is.False);
	}

	[Test]
	public void SourceSelection()
	{
		var compiler = new CompilerComponent();
		_ = Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest()));
		_ = Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
		{
			SourceText = "x := 1",
			ScriptPath = "script.ks",
		}));
	}

	[TestCase("#ErrorStdOut\nx := (\n", true)]
	[TestCase("x := (\n#ErrorStdOut\n", false)]
	[TestCase("text := \"#ErrorStdOut\"\n; #ErrorStdOut\nx := (\n", false)]
	[TestCase("#if false\n#ErrorStdOut\n#endif\nx := (\n", false)]
	[TestCase("#if false\n#ErrorStdOut\n#else\n#ErrorStdOut\n#endif\nx := (\n", true)]
	[TestCase("#if false\nx := \"unterminated\n#endif\n#ErrorStdOut\nx := (\n", true)]
	[TestCase("#ErrorStdOut\n#EndIf\n", true)]
	[TestCase("#EndIf\n#ErrorStdOut\n", false)]
	[TestCase("#ErrorStdOut\nx := \"unterminated\n", true)]
	[TestCase("x := \"unterminated\n#ErrorStdOut\n", false)]
	public void ErrorStdOutOrder(string source, bool expected)
	{
		var result = new ParserComponent().ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = source });
		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorStdOut, Is.EqualTo(expected));
	}

	[Test]
	public void ErrorStdOutIncludes()
	{
		var root = NewComponentRoot();
		_ = Directory.CreateDirectory(root);
		var dependency = Path.Combine(root, "Routing.ahk");
		var parser = new ParserComponent();

		try
		{
			ScriptSyntaxValidationResult Validate(string source) => parser.ValidateSyntax(new()
			{
				SourceText = source,
				ScriptPath = Path.Combine(root, "main.ahk"),
				IncludeDirectory = root,
			});

			Assert.IsTrue(Validate("#ErrorStdOut\n#Include \"missing.ahk\"\n").ErrorStdOut);
			Assert.That(Validate("#Include \"missing.ahk\"\n#ErrorStdOut\n").ErrorStdOut, Is.False);

			foreach (var body in new[] { "#ErrorStdOut\n#EndIf\n", "#ErrorStdOut\nx := \"unterminated\n" })
			{
				File.WriteAllText(dependency, body);
				var included = Validate("#Include \"Routing.ahk\"\nx := 1\n");
				Assert.That(included.Success, Is.False);
				Assert.IsTrue(included.ErrorStdOut);
			}

			IScriptCompilationResult CompileImport(string source) => new CompilerComponent().Compile(new ScriptCompileRequest
			{
				SourceText = source,
				IncludeDirectory = root,
				CompilationName = "routing-import",
				Output = ScriptCompilationOutput.InMemory,
			});

			File.WriteAllText(dependency, "#ErrorStdOut\nvalue := (\n");
			var imported = CompileImport("#Import \"Routing\"\nvalue := 1\n");
			Assert.That(imported.Success, Is.False);
			Assert.IsTrue(imported.ErrorStdOut);
			Assert.That(imported.ErrorText, Does.Contain("Routing.ahk"));

			File.WriteAllText(dependency, "value := (\n");
			Assert.IsTrue(CompileImport("#ErrorStdOut\n#Import \"Routing\"\n").ErrorStdOut);
			Assert.That(CompileImport("#Import \"Routing\"\n#ErrorStdOut\n").ErrorStdOut, Is.False);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[TestCase("#ErrorStdOut\nx := (\n", null, true)]
	[TestCase("#ErrorStdOut invalid\nx := 1\n", "accepts no arguments", true)]
	[TestCase("#ErrorStdOut\n#App { GuiTheme: \"Bad\" }\n", "GuiTheme", true)]
	[TestCase("#App { GuiTheme: \"Bad\" }\n#ErrorStdOut\n", "GuiTheme", false)]
	public void ErrorStdOutCompilation(string source, string diagnostic, bool expected)
	{
		var result = new CompilerComponent().Compile(new ScriptCompileRequest
		{
			SourceText = source,
			CompilationName = "routing-error",
			Output = ScriptCompilationOutput.InMemory,
		});
		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorStdOut, Is.EqualTo(expected));
		if (diagnostic != null)
			Assert.That(result.ErrorText, Does.Contain(diagnostic));
	}

	[Test]
	public void AppThemeIsolation()
	{
		const string hostTheme = "HostTheme";
		s.AccessorData.guiTheme = hostTheme;
		var result = new CompilerComponent().Compile(new ScriptCompileRequest
		{
			SourceText = "#App { GuiTheme: \"Dark\" }\nx := 1\n",
			CompilationName = "app-theme-host-isolation",
			Output = ScriptCompilationOutput.InMemory,
		});

		Assert.IsTrue(result.Success, result.ErrorText);
		Assert.That(Script.TheScript, Is.SameAs(s));
		Assert.That(s.AccessorData.guiTheme, Is.EqualTo(hostTheme));
	}

	// Keyview's full view shows the code as compiled, while the readable lowering, which --transpile prints, leaves the
	// location stamps out and stays parseable as C#: names that are C# keywords end up inside generated identifiers (a
	// nested function's FN_add_1, a label's KS_lbl_fixed), where '@' is not allowed.
	[Test]
	public void GeneratedCode()
	{
		var result = new CompilerComponent().Compile(new ScriptCompileRequest
		{
			SourceText = "Outer() {\n\tadd(x) => x + 1\n\tlock(x) {\n\t\treturn x * 2\n\t}\n\tGoto fixed\nfixed:\n\treturn lock(add(1))\n}\nOuter()\nswitch 2 {\ncase Outer(): y := 1\n}\n",
			CompilationName = "generated-code",
			Output = ScriptCompilationOutput.InMemory,
			EmitGeneratedCode = true,
		});

		Assert.IsTrue(result.Success, result.ErrorText);
		Assert.That(result.CompiledCode, Does.Contain("KS_line"));
		Assert.That(result.GeneratedCode, Does.Not.Contain("KS_line"));
		Assert.That(result.GeneratedCode, Does.Contain(".SwitchCase("));
		var errors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(result.GeneratedCode).GetDiagnostics()
			.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
		Assert.IsEmpty(errors, string.Join("\n", errors));
	}

	[Test]
	public void DefaultRuntimeDirectory()
	{
		var root = NewComponentRoot();

		try
		{
			var compiler = new CompilerComponent();
			var result = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\nx := 1\n",
				CompilationName = "default-runtime-directory",
				Output = ScriptCompilationOutput.Executable,
			});
			Assert.IsTrue(result.Success, result.ErrorText);
			Assert.IsNull(compiler.DeploySupportFiles(result, root));
			Assert.IsTrue(File.Exists(Path.Combine(root, "Keysharp.Core.dll")));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test]
	public void ComponentPolicy()
	{
		var compiler = new CompilerComponent();
		_ = Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
		{
			SourceText = "x := 1",
			AdditionalComponents = ["typo"],
		}));
		_ = Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
		{
			SourceText = "x := 1",
			AdditionalComponents = [ScriptingComponentIds.Compiler],
			ExcludedComponents = [ScriptingComponentIds.Compiler],
		}));
	}

	[Test]
	public void LoadableDiscovery()
	{
		var root = NewComponentRoot();

		try
		{
			var malformed = ComponentDirectory(root, "parser");
			_ = Directory.CreateDirectory(malformed);
			File.WriteAllText(Path.Combine(malformed, "component.json"), "{not-json");
			ScriptingComponentRegistry.SetSearchRootsForTests(root);
			Assert.That(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation), Is.False);
			Assert.That(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out var failure), Is.False);
			Assert.That(failure, Does.Contain("No installed 'parser' scripting component"));

			Directory.Delete(malformed, true);
			_ = Directory.CreateDirectory(malformed);
			File.WriteAllText(Path.Combine(malformed, "component.json"),
				// Declares the parser's full capability set on purpose: anything else is rejected as an
				// incomplete descriptor, and this fixture is meant to fail on its missing assembly instead.
				"{\"schemaVersion\":1,\"contractVersion\":1,\"id\":\"parser\",\"version\":\"1.0.0\",\"assembly\":\"missing.dll\",\"type\":\"Missing.Parser\",\"capabilities\":[\"SyntaxValidation\",\"Tokenization\"],\"files\":[\"missing.dll\"]}");
			ScriptingComponentRegistry.SetSearchRootsForTests(root);
			Assert.That(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation), Is.False);
			Assert.That(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure), Is.False);
			Assert.That(failure.ToLowerInvariant(), Does.Contain("missing"));

			Directory.Delete(malformed, true);
			CopyComponentPayload(typeof(Keysharp.Components.Scripting.Parser.ParserComponent).Assembly, root, "parser");
			var descriptorPath = Path.Combine(malformed, "component.json");
			var descriptor = File.ReadAllText(descriptorPath).Replace(
				"Keysharp.Components.Scripting.Parser.ParserComponent", "System.String", StringComparison.Ordinal);
			File.WriteAllText(descriptorPath, descriptor);
			ScriptingComponentRegistry.SetSearchRootsForTests(root);
			Assert.That(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation), Is.False);
			Assert.That(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure), Is.False);
			Assert.That(failure, Does.Contain("System.String"));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test]
	public void DescriptorVersion()
	{
		var root = NewComponentRoot();

		try
		{
			CopyComponentPayload(typeof(Keysharp.Components.Scripting.Parser.ParserComponent).Assembly, root, "parser");
			var descriptorPath = Path.Combine(ComponentDirectory(root, "parser"), "component.json");
			File.WriteAllText(descriptorPath, File.ReadAllText(descriptorPath)
				.Replace("\"contractVersion\":1", "\"contractVersion\":2", StringComparison.Ordinal));
			ScriptingComponentRegistry.SetSearchRootsForTests(root);
			Assert.That(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation), Is.False);
			Assert.That(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out var failure), Is.False);
			Assert.That(failure, Does.Contain("No installed 'parser' scripting component"));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test]
	public void DiscoveryFallback()
	{
		var brokenRoot = NewComponentRoot();
		var validRoot = NewComponentRoot();

		try
		{
			var broken = ComponentDirectory(brokenRoot, "parser");
			_ = Directory.CreateDirectory(broken);
			File.WriteAllText(Path.Combine(broken, "component.json"),
				// Declares the parser's full capability set on purpose: anything else is rejected as an
				// incomplete descriptor, and this fixture is meant to fail on its missing assembly instead.
				"{\"schemaVersion\":1,\"contractVersion\":1,\"id\":\"parser\",\"version\":\"1.0.0\",\"assembly\":\"missing.dll\",\"type\":\"Missing.Parser\",\"capabilities\":[\"SyntaxValidation\",\"Tokenization\"],\"files\":[\"missing.dll\"]}");
			CopyComponentPayload(typeof(Keysharp.Components.Scripting.Parser.ParserComponent).Assembly, validRoot, "parser");
			ScriptingComponentRegistry.SetSearchRootsForTests(brokenRoot, validRoot);

			Assert.IsTrue(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
			Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
			Assert.That(parser.Id, Is.EqualTo(ScriptingComponentIds.Parser));
		}
		finally
		{
			try { Directory.Delete(brokenRoot, true); } catch { }
			try { Directory.Delete(validRoot, true); } catch { }
		}
	}

	[Test]
	public void ParserRequired()
	{
		var root = NewComponentRoot();

		try
		{
			CopyComponentPayload(typeof(CompilerComponent).Assembly, root, "compiler");
			ScriptingComponentRegistry.SetSearchRootsForTests(root);
			Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out _, out var failure), failure);
			Assert.That(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure), Is.False);
			Assert.That(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation), Is.False);
			Assert.That(failure.ToLowerInvariant(), Does.Contain("parser"));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test]
	public void CliPolicy()
	{
		var script = Path.Combine(path, "assign-int.ahk");
		var syntax = Runner.Parse(["--validate-syntax", script]);
		Assert.IsTrue(syntax.SyntaxOnly);

		var policy = Runner.Parse(["--compile", "asm", "--with-parser", "--without-compiler", script]);
		Assert.That(policy.IncludeComponents, Has.Member("parser"));
		Assert.That(policy.ExcludeComponents, Has.Member("compiler"));

		var conflict = Runner.Parse(["--with-compiler", "--without-compiler", script]);
		Assert.That(conflict.Kind, Is.EqualTo(CliCommandKind.Error));

		Assert.That(Runner.Parse(["--with-component", "parsing", script]).Kind, Is.EqualTo(CliCommandKind.Error),
			"deployment policy accepts fixed unit IDs, not capability aliases");
		Assert.IsTrue((bool)Ks.IsComponentAvailable("parser"));
		Assert.IsTrue((bool)Ks.IsComponentAvailable("parsing"));
		Assert.IsTrue((bool)Ks.IsComponentAvailable("compiler"));
		Assert.IsTrue((bool)Ks.IsComponentAvailable("compilation"));
	}

	[Test]
	public void DeploymentPolicy()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-components-" + Guid.NewGuid().ToString("N"));
		var automaticRoot = Path.Combine(root, "automatic");
		var parserRoot = Path.Combine(root, "parser");

		try
		{
			var compiler = new CompilerComponent();
			var automatic = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { IsComponentAvailable, RunScript }\nif IsComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n",
				CompilationName = "automatic-component",
				RuntimeDirectory = AppContext.BaseDirectory,
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(automatic.Success, automatic.ErrorText);
			Assert.That(automatic.RequiredComponents, Has.Member("compiler"), automatic.GeneratedCode);
			Assert.IsNull(compiler.DeploySupportFiles(automatic, automaticRoot));
			Assert.IsTrue(File.Exists(Path.Combine(automaticRoot, "components", "scripting", "compiler", "Microsoft.CodeAnalysis.CSharp.dll")));
			ScriptingComponentRegistry.ResetForTests();
			ScriptingComponentRegistry.AddSearchRoot(automaticRoot);
			Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var deployedCompiler, out var loadFailure), loadFailure);
			Assert.That(deployedCompiler.Id, Is.EqualTo(ScriptingComponentIds.Compiler));

			var parserOnly = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\nx := 1\n",
				CompilationName = "parser-component",
				RuntimeDirectory = AppContext.BaseDirectory,
				AdditionalComponents = [ScriptingComponentIds.Parser],
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(parserOnly.Success, parserOnly.ErrorText);
			Assert.That(parserOnly.RequiredComponents, Is.EquivalentTo(new[] { "parser" }));
			Assert.IsNull(compiler.DeploySupportFiles(parserOnly, parserRoot));
			Assert.IsTrue(File.Exists(Path.Combine(parserRoot, "components", "scripting", "parser", "Keysharp.Components.Scripting.Parser.dll")));
			Assert.That(Directory.GetFiles(parserRoot, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any(), Is.False);
			ScriptingComponentRegistry.ResetForTests();
			ScriptingComponentRegistry.AddSearchRoot(parserRoot);
			Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var deployedParser, out loadFailure), loadFailure);
			Assert.IsTrue(deployedParser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := 1" }).Success);

			var excluded = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { IsComponentAvailable, RunScript }\nif IsComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n",
				CompilationName = "excluded-component",
				RuntimeDirectory = AppContext.BaseDirectory,
				ExcludedComponents = [ScriptingComponentIds.Compiler],
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(excluded.Success, excluded.ErrorText);
			Assert.IsEmpty(excluded.RequiredComponents);

			// Each run-time check pulls in the component it needs, so a compiled program carries it automatically.
			var compileScript = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { CompileScript }\nresult := CompileScript(\"x := 1\")\n",
				CompilationName = "automatic-compile-script-component",
				RuntimeDirectory = AppContext.BaseDirectory,
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(compileScript.Success, compileScript.ErrorText);
			Assert.That(compileScript.RequiredComponents, Has.Member("compiler"));

			var validateScript = compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { ValidateScript }\nresult := ValidateScript(\"x := 1\")\n",
				CompilationName = "automatic-validate-script-component",
				RuntimeDirectory = AppContext.BaseDirectory,
				Output = ScriptCompilationOutput.Assembly,
			});
			Assert.IsTrue(validateScript.Success, validateScript.ErrorText);
			Assert.That(validateScript.RequiredComponents, Has.Member("parser"));
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test]
	public void EmbeddedCompiler()
	{
		var extractedRoot = default(string);
		var extractedRootExisted = false;
		var staleRoot = default(string);
		try
		{
			var result = embeddedCompilerBuild.Value;
			Assert.IsTrue(result.Success, result.ErrorText);
			var assembly = Assembly.Load(result.AssemblyBytes);
			Assert.IsTrue(CompiledScriptingComponentManifest.HasCapability(assembly, ScriptingCapability.Compilation));
			Assert.That(assembly.GetManifestResourceNames().Any(name =>
				name.Equals("Deps.Keysharp.Components.Scripting.Compiler.dll", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Deps.Keysharp.Components.Scripting.Parser.dll", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("Deps.Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)),
				Is.False,
				"optional implementation assemblies must be component assets, not unconditional runtime dependencies");

			extractedRoot = CompiledScriptingComponentManifest.GetCacheDirectory(assembly, ScriptingCapability.Compilation);
			extractedRootExisted = Directory.Exists(extractedRoot);
			staleRoot = Path.Combine(Path.GetDirectoryName(extractedRoot), "stale-test-" + Guid.NewGuid().ToString("N"));
			_ = Directory.CreateDirectory(staleRoot);
			Directory.SetLastWriteTimeUtc(staleRoot, DateTime.UtcNow.AddDays(-31));
			ScriptingComponentRegistry.ResetForTests();
			Assert.IsTrue(CompiledScriptingComponentManifest.TryPrepare(assembly, ScriptingCapability.Compilation, out var failure), failure);
			Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var compiler, out failure), failure);
			Assert.That(compiler.Id, Is.EqualTo(ScriptingComponentIds.Compiler));
			Assert.That(Directory.Exists(staleRoot), Is.False, "stale component cache generations should be pruned");
		}
		finally
		{
			try { if (staleRoot != null) Directory.Delete(staleRoot, true); } catch { }
			try { if (!extractedRootExisted && extractedRoot != null) Directory.Delete(extractedRoot, true); } catch { }
		}
	}

	[Test]
	public void ContentAddressedCache()
	{
		var first = embeddedCompilerBuild.Value;
		var second = CompileEmbeddedCompiler("content-cache-b");
		Assert.IsTrue(first.Success, first.ErrorText);
		Assert.IsTrue(second.Success, second.ErrorText);
		var firstAssembly = Assembly.Load(first.AssemblyBytes);
		var secondAssembly = Assembly.Load(second.AssemblyBytes);
		Assert.That(secondAssembly.ManifestModule.ModuleVersionId, Is.Not.EqualTo(firstAssembly.ManifestModule.ModuleVersionId));
		Assert.That(
			CompiledScriptingComponentManifest.GetCacheDirectory(secondAssembly, ScriptingCapability.Compilation),
			Is.EqualTo(CompiledScriptingComponentManifest.GetCacheDirectory(firstAssembly, ScriptingCapability.Compilation)));
	}

	[Test, NonParallelizable]
	public void LeanExecutable()
	{
		// BuildLeanExecutable asserts the build carries no scripting components.
		var run = RunProcess(leanExecutable.Value, []);
		Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
		Assert.That(run.StdOut.Trim(), Is.EqualTo("lean-pass"), run.StdErr);
	}

	/// <summary>
	/// `#App { ConsoleApp: true }` builds a console (CUI) executable instead of the default GUI one. On Windows that choice is
	/// the PE subsystem field, which the shell reads before the process starts to decide whether to wait for it
	/// and whether to hand it the terminal's stdio - so it can only be made at build time, and the produced file
	/// is the only place it can be checked. Other platforms have no subsystem: there the directive is inert and
	/// only its acceptance (a clean compile) is asserted.
	/// </summary>
	[Test, NonParallelizable]
	public void ConsoleHost()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-component-console-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			// Same body as the shared lean build, so the subsystem is the only difference between the two executables.
			var consoleScript = Path.Combine(root, "console.ks");
			File.WriteAllText(consoleScript, "#App { ConsoleApp: true }\n" + leanBody);
			var consoleExe = BuildExecutable(consoleScript);
#if WINDOWS
			Assert.That(PeSubsystem(consoleExe), Is.EqualTo(3), "#App { ConsoleApp: true } must produce a console-subsystem executable");
			Assert.That(PeSubsystem(leanExecutable.Value), Is.EqualTo(2), "without the directive the executable must stay a GUI one");
#endif
			// The stamped host still has to run: a subsystem edit that corrupted it would fail only here.
			var run = RunProcess(consoleExe, []);
			Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
			Assert.That(run.StdOut.Trim(), Is.EqualTo("lean-pass"), run.StdErr);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[TestCase("apphost"), TestCase("dotnet"), TestCase("compiled")]
	public void InspectorProcess(string host)
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-inspector-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(root);

		try
		{
			var helper = Path.Combine(root, "inspector helper.ks");
			File.WriteAllText(helper, "#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n"
				+ "FileAppend('helper-pass:' A_Args.Length, '*')\nExitApp()\n");
			var compile = RunLauncher(["--errorstdout", "--compile", "asm", helper]);
			Assert.That(compile.ExitCode, Is.EqualTo(0), compile.StdErr);
			var parent = Path.Combine(root, "inspector parent.ks");
			File.WriteAllText(parent, """
				#NoTrayIcon
				#ErrorStdOut
				#Warn All, StdOut
				#CSharp
				public static object LaunchInspector(object target)
				{
					var runner = typeof(Keysharp.Runtime.Script).Assembly.GetType("Keysharp.Internals.Scripting.Runner", true);
					var method = runner.GetMethod("CreateRestartStartInfo", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
					var start = (System.Diagnostics.ProcessStartInfo)method.Invoke(null, new object[] { new string[] { "--script", (string)target } });
					start.RedirectStandardOutput = true;
					start.RedirectStandardError = true;
					using var child = System.Diagnostics.Process.Start(start);
					var output = child.StandardOutput.ReadToEndAsync();
					var error = child.StandardError.ReadToEndAsync();
					if (!child.WaitForExit(60000))
					{
						child.Kill(true);
						throw new System.TimeoutException("Inspector helper did not exit.");
					}
					if (child.ExitCode != 0)
						throw new System.Exception(error.GetAwaiter().GetResult());
					return output.GetAwaiter().GetResult();
				}
				#EndCSharp
				FileAppend(LaunchInspector(A_Args[1]), '*')
				ExitApp()
				""");
			var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Keysharp.exe" : "Keysharp");
			if (host == "compiled")
			{
				compile = RunLauncher(["--errorstdout", "--compile", "exe-min", "--with-compiler", parent]);
				Assert.That(compile.ExitCode, Is.EqualTo(0), compile.StdErr);
				executable = Path.ChangeExtension(parent, OperatingSystem.IsWindows() ? ".exe" : null);
			}

			foreach (var target in new[] { helper, Path.ChangeExtension(helper, ".cks") })
			{
				string[] arguments = host == "compiled" ? [target, "parent-only"] : [parent, target, "parent-only"];
				var run = host == "dotnet"
					? RunLauncher(arguments) : RunProcess(executable, arguments);
				Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
				Assert.That(run.StdOut.Trim(), Is.EqualTo("helper-pass:0"), target + ": " + run.StdErr);
			}
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test, NonParallelizable]
	public void EmbeddedCompilerProcess()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-component-minimal-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(root);

		try
		{
			var script = Path.Combine(root, "embedded-compiler.ks");
			File.WriteAllText(script,
				"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
				+ "info := RunScript(\"#NoTrayIcon`n#ErrorStdOut`nFileAppend('nested-pass', '*')`nExitApp(0)\")\n"
				+ "FileAppend(info.ExitCode ':' info.StdOut.Read(64), '*')\nExitApp()\n");

			var executable = BuildExecutable(script);
			Assert.That(Directory.Exists(Path.Combine(root, "components", "scripting")),
				Is.False,
				"a minimal executable must carry components as integrity-checked embedded assets");

			var run = RunProcess(executable, []);
			Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
			Assert.That(run.StdOut.Trim(), Is.EqualTo("0:nested-pass"), run.StdErr);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test, NonParallelizable]
	public void CksSidecar()
	{
		// BuildCksTarget asserts the sidecar compiler was deployed and the host carries none of its own.
		var (target, hostRoot) = cksTarget.Value;
		var run = RunProcess("dotnet", [Path.Combine(hostRoot, "Keysharp.dll"), "--errorstdout", target]);
		Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
		Assert.That(run.StdOut.Trim(), Is.EqualTo($"{target}:0:nested-pass"), run.StdErr);
	}

	[Test, NonParallelizable]
	public void RunScriptCks()
	{
		var (target, hostRoot) = cksTarget.Value;
		var root = Path.Combine(Path.GetTempPath(), "ks-component-runscript-cks-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(root);

		try
		{
			var outerSource = Path.Combine(root, "outer.ks");
			var outer = Path.Combine(root, "outer.cks");
			File.WriteAllText(outerSource,
				"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
				+ $"info := RunScript('{target.Replace("'", "''")}')\n"
				+ "FileAppend(info.ExitCode ':' info.StdOut.Read(512), '*')\nExitApp()\n");
			var outerCompile = RunLauncher(["--errorstdout", "--compile", "asm", "--without-compiler", "--dest", outer, outerSource]);
			Assert.That(outerCompile.ExitCode, Is.EqualTo(0), "outer compile failed: " + outerCompile.StdErr);
			Assert.That(Directory.Exists(Path.Combine(root, "components", "scripting")),
				Is.False,
				"the outer artifact must not carry its own compiler");

			var run = RunProcess("dotnet", [Path.Combine(hostRoot, "Keysharp.dll"), "--errorstdout", outer]);
			Assert.That(run.ExitCode, Is.EqualTo(0), run.StdErr);
			Assert.That(run.StdOut.Trim(), Is.EqualTo($"0:{target}:0:nested-pass"), run.StdErr);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	[Test, NonParallelizable]
	public void StdoutSidecar()
	{
		var root = Path.Combine(Path.GetTempPath(), "ks-component-stdout-" + Guid.NewGuid().ToString("N"));
		_ = Directory.CreateDirectory(root);

		try
		{
			var script = Path.Combine(root, "stdout.ks");
			File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\nx := 1\n");
			var result = RunLauncher(["--errorstdout", "--compile", "asm", "--with-compiler", "--dest", "*", script]);
			Assert.That(result.ExitCode, Is.Not.EqualTo(0));
			Assert.IsTrue((result.StdOut + result.StdErr).Contains("requires sidecar scripting components", StringComparison.Ordinal),
				result.StdErr);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch { }
		}
	}

	private static IScriptCompilationResult CompileEmbeddedCompiler(string name) => new CompilerComponent().Compile(new ScriptCompileRequest
	{
		SourceText = embeddedCompilerSource,
		CompilationName = name,
		RuntimeDirectory = AppContext.BaseDirectory,
		Output = ScriptCompilationOutput.MinimalExecutable,
	});

	private string BuildLeanExecutable()
	{
		var root = Path.Combine(sharedRoot, "lean");
		_ = Directory.CreateDirectory(root);
		var script = Path.Combine(root, "lean.ks");
		File.WriteAllText(script, leanBody);
		var executable = BuildExecutable(script);
		// Checked before any test runs it, so the lean shape cannot depend on test order.
		Assert.That(Directory.Exists(Path.Combine(root, "components", "scripting")), Is.False);
		Assert.That(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(file =>
			Path.GetFileName(file).StartsWith("Keysharp.Components.Scripting.Parser", StringComparison.OrdinalIgnoreCase)
			|| Path.GetFileName(file).StartsWith("Keysharp.Components.Scripting.Compiler", StringComparison.OrdinalIgnoreCase)
			|| Path.GetFileName(file).StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)), Is.False);
		return executable;
	}

	/// <summary>
	/// A .cks carrying a sidecar compiler, whose script runs a nested RunScript and prints its own path, plus a
	/// launcher host with no compiler of its own. Tests only run these, never modify them.
	/// </summary>
	private (string Target, string HostRoot) BuildCksTarget()
	{
		var root = Path.Combine(sharedRoot, "cks");
		var artifactRoot = Path.Combine(root, "artifact");
		var hostRoot = Path.Combine(root, "host");
		_ = Directory.CreateDirectory(artifactRoot);
		_ = Directory.CreateDirectory(hostRoot);

		var targetSource = Path.Combine(root, "target.ks");
		var target = Path.Combine(artifactRoot, "target.cks");
		File.WriteAllText(targetSource,
			"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
			+ "ownPath := A_ScriptFullPath\n"
			+ "info := RunScript(\"#NoTrayIcon`n#ErrorStdOut`nFileAppend('nested-pass', '*')`nExitApp(0)\")\n"
			+ "FileAppend(ownPath ':' info.ExitCode ':' info.StdOut.Read(64), '*')\nExitApp()\n");
		var targetCompile = RunLauncher(["--errorstdout", "--compile", "asm", "--with-compiler", "--dest", target, targetSource]);
		Assert.That(targetCompile.ExitCode, Is.EqualTo(0), "target compile failed: " + targetCompile.StdErr);
		Assert.IsTrue(File.Exists(target));
		Assert.IsTrue(File.Exists(Path.Combine(artifactRoot, "components", "scripting", "compiler", "component.json")));

		CopyLeanHost(hostRoot);
		Assert.That(Directory.Exists(Path.Combine(hostRoot, "components", "scripting")), Is.False);
		Assert.That(Directory.GetFiles(hostRoot, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any(), Is.False);
		return (target, hostRoot);
	}

	private static void CopyLeanHost(string destination)
	{
		var excluded = new[] { "Keysharp.Components.Scripting.Compiler", "Keysharp.Components.Scripting.Parser", "Microsoft.CodeAnalysis" };
		foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory))
		{
			var file = Path.GetFileName(source);
			if (!(file.Equals("Keysharp.dll", StringComparison.OrdinalIgnoreCase)
					|| file.Equals("Keysharp.deps.json", StringComparison.OrdinalIgnoreCase)
					|| file.Equals("Keysharp.runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
					|| file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
					|| excluded.Any(prefix => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
				continue;

			File.Copy(source, Path.Combine(destination, file), true);
		}

		var native = CompilerHelper.ResolveAppNativeDependencyPath(AppContext.BaseDirectory,
			CompilerHelper.requiredNativeDependencies.Single());
		if (File.Exists(native))
			File.Copy(native, Path.Combine(destination, Path.GetFileName(native)), true);
	}

	/// <summary>
	/// Compiles a script to a standalone executable through the real launcher and returns its path. The name
	/// differs per platform (only Windows appends .exe), which is why callers take it from here.
	/// </summary>
	private static string BuildExecutable(string script)
	{
		var compile = RunLauncher(["--errorstdout", "--compile", "exe-min", script]);
		Assert.That(compile.ExitCode, Is.EqualTo(0), "compile failed: " + compile.StdErr);
#if WINDOWS
		var executable = Path.ChangeExtension(script, ".exe");
#else
		var executable = Path.ChangeExtension(script, null);
#endif
		Assert.IsTrue(File.Exists(executable), $"compile produced no executable at {executable}");
		return executable;
	}

	/// <summary>
	/// Reads the PE subsystem field (2 = Windows GUI, 3 = console) straight out of the file, since nothing in
	/// the .NET API surfaces it: the DOS header's e_lfanew at 0x3C locates the PE signature, and the field sits
	/// 92 bytes past it (4 signature + 20 COFF header + 68 into the optional header).
	/// </summary>
	private static int PeSubsystem(string executable)
	{
		var image = File.ReadAllBytes(executable);
		return BitConverter.ToUInt16(image, BitConverter.ToInt32(image, 0x3C) + 92);
	}

	private static (int ExitCode, string StdOut, string StdErr) RunLauncher(IReadOnlyList<string> arguments) =>
		RunProcess("dotnet", [Path.Combine(AppContext.BaseDirectory, "Keysharp.dll"), .. arguments]);

	private static (int ExitCode, string StdOut, string StdErr) RunProcess(string executable, IReadOnlyList<string> arguments)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo(executable)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			}
		};
		process.StartInfo.Environment["KEYSHARP_DAEMON"] = "0";
		foreach (var argument in arguments)
			process.StartInfo.ArgumentList.Add(argument);

		_ = process.Start();
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(240000))
		{
			try { process.Kill(true); } catch { }
			Assert.Fail($"'{executable}' did not exit within 240 seconds.");
		}

		// A crashed child can leave a grandchild (its dump writer, an orphaned spawn) holding the redirected
		// pipes open; an unbounded read here then hangs the whole test host until the blame collector aborts
		// the run. Fail just this test instead, naming the survivors so the holder is identifiable from CI logs.
		if (!Task.WaitAll([output, error], 60000))
			Assert.Fail($"'{executable}' exited (code {process.ExitCode}) but its output pipes stayed open; a child process is still attached to them."
				+ $"\nstdout so far: [{PipeContent(output)}]\nstderr so far: [{PipeContent(error)}]\nsurviving processes:\n{SurvivingProcesses()}");

		return (process.ExitCode, output.Result, error.Result);
	}

	private static string PipeContent(Task<string> pipe) => pipe.IsCompletedSuccessfully ? pipe.Result.Trim() : "<still open>";

	private static string SurvivingProcesses() =>
#if WINDOWS
		"<not captured on Windows>";
#else
		try
		{
			using var ps = Process.Start(new ProcessStartInfo("ps", "-axo pid,ppid,etime,args")
			{
				RedirectStandardOutput = true,
				UseShellExecute = false,
			});
			var listing = ps.StandardOutput.ReadToEnd();
			_ = ps.WaitForExit(5000);
			return string.Join('\n', listing.Split('\n').Where(line =>
				line.Contains("dotnet") || line.Contains("Keysharp") || line.Contains("createdump")));
		}
		catch (Exception ex)
		{
			return $"<ps failed: {ex.Message}>";
		}
#endif


	private static string NewComponentRoot() => Path.Combine(Path.GetTempPath(), "ks-components-" + Guid.NewGuid().ToString("N"));

	private static string ComponentDirectory(string root, string name) =>
		Path.Combine(root, "components", "scripting", name);

	private static void CopyComponentPayload(Assembly assembly, string root, string name)
	{
		var source = Path.Combine(AppContext.BaseDirectory, "components", "scripting", name);
		Assert.IsTrue(Directory.Exists(source), $"canonical {name} payload is missing at {source}");
		var destination = ComponentDirectory(root, name);
		_ = Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			var copy = Path.Combine(destination, Path.GetRelativePath(source, file));
			_ = Directory.CreateDirectory(Path.GetDirectoryName(copy));
			File.Copy(file, copy, true);
		}
	}
}
