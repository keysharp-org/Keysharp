using Keysharp.Components.Scripting.Compiler;
using Keysharp.Components.Scripting;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class ScriptingComponentTests : TestRunner
	{
		[SetUp]
		public void ResetComponents() => ScriptingComponentRegistry.ResetForTests();

		[TearDown]
		public void ClearComponents() => ScriptingComponentRegistry.ResetForTests();

		[Test]
		public void RoslynIsolation()
		{
			var coreReferences = typeof(Script).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
			Assert.IsFalse(coreReferences.Any(name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)),
				"Keysharp.Core must remain runnable without Roslyn.");

			Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
			Assert.AreEqual(ScriptingComponentIds.Parser, parser.Id);
			var parserReferences = parser.GetType().Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
			Assert.IsFalse(parserReferences.Any(name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)),
				"the parser component must support validation without Roslyn.");

			Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var compiler, out failure), failure);
			Assert.AreEqual(ScriptingComponentIds.Compiler, compiler.Id);
			Assert.IsTrue((compiler.Capabilities & ScriptingCapability.Compilation) != 0);
		}

		[Test]
		public void ParserOnly()
		{
			Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
			Assert.IsTrue(parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := 1" }).Success);
			var scriptPath = Path.GetFullPath(Path.Combine("component-tests", "invalid.ahk"));
			var invalid = parser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := (", ScriptPath = scriptPath });
			Assert.IsFalse(invalid.Success);
			Assert.IsNotEmpty(invalid.Diagnostics);
			Assert.IsTrue(invalid.Diagnostics.All(diagnostic => diagnostic.FilePath == scriptPath),
				"parser diagnostics should retain the caller's complete script path");
		}

		[Test]
		public void WarningSuccess()
		{
			var warning = new ScriptSyntaxValidationResult
			{
				Diagnostics = [new("warning", ScriptDiagnosticSeverity.Warning)]
			};
			Assert.IsTrue(warning.Success);
			Assert.IsFalse(new ScriptSyntaxValidationResult
			{
				Diagnostics = [new("error", ScriptDiagnosticSeverity.Error)]
			}.Success);
		}

		[Test]
		public void SourceSelection()
		{
			var compiler = new CompilerComponent();
			Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest()));
			Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "x := 1",
				ScriptPath = "script.ks",
			}));
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
			Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "x := 1",
				AdditionalComponents = ["typo"],
			}));
			Assert.Throws<ArgumentException>(() => compiler.Compile(new ScriptCompileRequest
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
				Directory.CreateDirectory(malformed);
				File.WriteAllText(Path.Combine(malformed, "component.json"), "{not-json");
				ScriptingComponentRegistry.SetSearchRootsForTests(root);
				Assert.IsFalse(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				Assert.IsFalse(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out var failure));
				StringAssert.Contains("No installed 'parser' scripting component", failure);

				Directory.Delete(malformed, true);
				Directory.CreateDirectory(malformed);
				File.WriteAllText(Path.Combine(malformed, "component.json"),
					// Declares the parser's full capability set on purpose: anything else is rejected as an
					// incomplete descriptor, and this fixture is meant to fail on its missing assembly instead.
					"{\"schemaVersion\":1,\"contractVersion\":1,\"id\":\"parser\",\"version\":\"1.0.0\",\"assembly\":\"missing.dll\",\"type\":\"Missing.Parser\",\"capabilities\":[\"SyntaxValidation\",\"Tokenization\"],\"files\":[\"missing.dll\"]}");
				ScriptingComponentRegistry.SetSearchRootsForTests(root);
				Assert.IsFalse(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				Assert.IsFalse(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure));
				StringAssert.Contains("missing", failure.ToLowerInvariant());

				Directory.Delete(malformed, true);
				CopyComponentPayload(typeof(Keysharp.Components.Scripting.Parser.ParserComponent).Assembly, root, "parser");
				var descriptorPath = Path.Combine(malformed, "component.json");
				var descriptor = File.ReadAllText(descriptorPath).Replace(
					"Keysharp.Components.Scripting.Parser.ParserComponent", "System.String", StringComparison.Ordinal);
				File.WriteAllText(descriptorPath, descriptor);
				ScriptingComponentRegistry.SetSearchRootsForTests(root);
				Assert.IsFalse(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				Assert.IsFalse(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure));
				StringAssert.Contains("System.String", failure);
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
				Assert.IsFalse(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				Assert.IsFalse(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out var failure));
				StringAssert.Contains("No installed 'parser' scripting component", failure);
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
				Directory.CreateDirectory(broken);
				File.WriteAllText(Path.Combine(broken, "component.json"),
					// Declares the parser's full capability set on purpose: anything else is rejected as an
					// incomplete descriptor, and this fixture is meant to fail on its missing assembly instead.
					"{\"schemaVersion\":1,\"contractVersion\":1,\"id\":\"parser\",\"version\":\"1.0.0\",\"assembly\":\"missing.dll\",\"type\":\"Missing.Parser\",\"capabilities\":[\"SyntaxValidation\",\"Tokenization\"],\"files\":[\"missing.dll\"]}");
				CopyComponentPayload(typeof(Keysharp.Components.Scripting.Parser.ParserComponent).Assembly, validRoot, "parser");
				ScriptingComponentRegistry.SetSearchRootsForTests(brokenRoot, validRoot);

				Assert.IsTrue(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var parser, out var failure), failure);
				Assert.AreEqual(ScriptingComponentIds.Parser, parser.Id);
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
				Assert.IsFalse(ScriptingComponentRegistry.TryGetSyntaxValidator(out _, out failure));
				Assert.IsFalse(ScriptingComponentRegistry.IsAvailable(ScriptingCapability.SyntaxValidation));
				StringAssert.Contains("parser", failure.ToLowerInvariant());
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
			CollectionAssert.Contains(policy.IncludeComponents, "parser");
			CollectionAssert.Contains(policy.ExcludeComponents, "compiler");

			var conflict = Runner.Parse(["--with-compiler", "--without-compiler", script]);
			Assert.AreEqual(CliCommandKind.Error, conflict.Kind);

			Assert.AreEqual(CliCommandKind.Error, Runner.Parse(["--with-component", "parsing", script]).Kind,
				"deployment policy accepts fixed unit IDs, not capability aliases");
			Assert.IsTrue((bool)Ks.ComponentAvailable("parser"));
			Assert.IsTrue((bool)Ks.ComponentAvailable("parsing"));
			Assert.IsTrue((bool)Ks.ComponentAvailable("compiler"));
			Assert.IsTrue((bool)Ks.ComponentAvailable("compilation"));
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
					SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { ComponentAvailable, RunScript }\nif ComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n",
					CompilationName = "automatic-component",
					RuntimeDirectory = AppContext.BaseDirectory,
					Output = ScriptCompilationOutput.Assembly,
				});
				Assert.IsTrue(automatic.Success, automatic.ErrorText);
				CollectionAssert.Contains(automatic.RequiredComponents, "compiler", automatic.GeneratedCode);
				Assert.IsNull(compiler.DeploySupportFiles(automatic, automaticRoot));
				Assert.IsTrue(File.Exists(Path.Combine(automaticRoot, "components", "scripting", "compiler", "Microsoft.CodeAnalysis.CSharp.dll")));
				ScriptingComponentRegistry.ResetForTests();
				ScriptingComponentRegistry.AddSearchRoot(automaticRoot);
				Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var deployedCompiler, out var loadFailure), loadFailure);
				Assert.AreEqual(ScriptingComponentIds.Compiler, deployedCompiler.Id);

				var parserOnly = compiler.Compile(new ScriptCompileRequest
				{
					SourceText = "#NoTrayIcon\n#ErrorStdOut\nx := 1\n",
					CompilationName = "parser-component",
					RuntimeDirectory = AppContext.BaseDirectory,
					AdditionalComponents = [ScriptingComponentIds.Parser],
					Output = ScriptCompilationOutput.Assembly,
				});
				Assert.IsTrue(parserOnly.Success, parserOnly.ErrorText);
				CollectionAssert.AreEquivalent(new[] { "parser" }, parserOnly.RequiredComponents);
				Assert.IsNull(compiler.DeploySupportFiles(parserOnly, parserRoot));
				Assert.IsTrue(File.Exists(Path.Combine(parserRoot, "components", "scripting", "parser", "Keysharp.Components.Scripting.Parser.dll")));
				Assert.IsFalse(Directory.GetFiles(parserRoot, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any());
				ScriptingComponentRegistry.ResetForTests();
				ScriptingComponentRegistry.AddSearchRoot(parserRoot);
				Assert.IsTrue(ScriptingComponentRegistry.TryGetSyntaxValidator(out var deployedParser, out loadFailure), loadFailure);
				Assert.IsTrue(deployedParser.ValidateSyntax(new ScriptSyntaxValidationRequest { SourceText = "x := 1" }).Success);

				var excluded = compiler.Compile(new ScriptCompileRequest
				{
					SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { ComponentAvailable, RunScript }\nif ComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n",
					CompilationName = "excluded-component",
					RuntimeDirectory = AppContext.BaseDirectory,
					ExcludedComponents = [ScriptingComponentIds.Compiler],
					Output = ScriptCompilationOutput.Assembly,
				});
				Assert.IsTrue(excluded.Success, excluded.ErrorText);
				Assert.IsEmpty(excluded.RequiredComponents);

				var parseScript = compiler.Compile(new ScriptCompileRequest
				{
					SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { ParseScript }\nresult := ParseScript(\"x := 1\")\n",
					CompilationName = "automatic-parse-script-component",
					RuntimeDirectory = AppContext.BaseDirectory,
					Output = ScriptCompilationOutput.Assembly,
				});
				Assert.IsTrue(parseScript.Success, parseScript.ErrorText);
				CollectionAssert.Contains(parseScript.RequiredComponents, "compiler");
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
				var result = new CompilerComponent().Compile(new ScriptCompileRequest
				{
					SourceText = "#NoTrayIcon\n#ErrorStdOut\n#import \"Ks\" { ComponentAvailable, RunScript }\nif ComponentAvailable(\"compiler\")\n\tRunScript(\"x := 1\")\n",
					CompilationName = "embedded-compiler",
					RuntimeDirectory = AppContext.BaseDirectory,
					Output = ScriptCompilationOutput.MinimalExecutable,
				});
				Assert.IsTrue(result.Success, result.ErrorText);
				var assembly = Assembly.Load(result.AssemblyBytes);
				Assert.IsTrue(CompiledScriptingComponentManifest.HasCapability(assembly, ScriptingCapability.Compilation));
				Assert.IsFalse(assembly.GetManifestResourceNames().Any(name =>
					name.Equals("Deps.Keysharp.Components.Scripting.Compiler.dll", StringComparison.OrdinalIgnoreCase)
					|| name.Equals("Deps.Keysharp.Components.Scripting.Parser.dll", StringComparison.OrdinalIgnoreCase)
					|| name.StartsWith("Deps.Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)),
					"optional implementation assemblies must be component assets, not unconditional runtime dependencies");

				extractedRoot = CompiledScriptingComponentManifest.GetCacheDirectory(assembly, ScriptingCapability.Compilation);
				extractedRootExisted = Directory.Exists(extractedRoot);
				staleRoot = Path.Combine(Path.GetDirectoryName(extractedRoot), "stale-test-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(staleRoot);
				Directory.SetLastWriteTimeUtc(staleRoot, DateTime.UtcNow.AddDays(-31));
				ScriptingComponentRegistry.ResetForTests();
				Assert.IsTrue(CompiledScriptingComponentManifest.TryPrepare(assembly, ScriptingCapability.Compilation, out var failure), failure);
				Assert.IsTrue(ScriptingComponentRegistry.TryGetCompiler(out var compiler, out failure), failure);
				Assert.AreEqual(ScriptingComponentIds.Compiler, compiler.Id);
				Assert.IsFalse(Directory.Exists(staleRoot), "stale component cache generations should be pruned");
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
			var compiler = new CompilerComponent();
			IScriptCompilationResult Compile(string name) => compiler.Compile(new ScriptCompileRequest
			{
				SourceText = "#NoTrayIcon\n#ErrorStdOut\n#Import \"Ks\" { RunScript }\nRunScript('x := 1')\n",
				CompilationName = name,
				RuntimeDirectory = AppContext.BaseDirectory,
				Output = ScriptCompilationOutput.MinimalExecutable,
			});

			var first = Compile("content-cache-a");
			var second = Compile("content-cache-b");
			Assert.IsTrue(first.Success, first.ErrorText);
			Assert.IsTrue(second.Success, second.ErrorText);
			var firstAssembly = Assembly.Load(first.AssemblyBytes);
			var secondAssembly = Assembly.Load(second.AssemblyBytes);
			Assert.AreNotEqual(firstAssembly.ManifestModule.ModuleVersionId, secondAssembly.ManifestModule.ModuleVersionId);
			Assert.AreEqual(
				CompiledScriptingComponentManifest.GetCacheDirectory(firstAssembly, ScriptingCapability.Compilation),
				CompiledScriptingComponentManifest.GetCacheDirectory(secondAssembly, ScriptingCapability.Compilation));
		}

		[Test, NonParallelizable]
		public void LeanExecutable()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-component-lean-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);

			try
			{
				var script = Path.Combine(root, "lean.ks");
				File.WriteAllText(script,
					"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\nFileAppend('lean-pass', '*')\nExitApp(0)\n");

				var executable = BuildExecutable(script);
				Assert.IsFalse(Directory.Exists(Path.Combine(root, "components", "scripting")));
				Assert.IsFalse(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(file =>
					Path.GetFileName(file).StartsWith("Keysharp.Components.Scripting.Parser", StringComparison.OrdinalIgnoreCase)
					|| Path.GetFileName(file).StartsWith("Keysharp.Components.Scripting.Compiler", StringComparison.OrdinalIgnoreCase)
					|| Path.GetFileName(file).StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)));

				var run = RunProcess(executable, []);
				Assert.AreEqual(0, run.ExitCode, run.StdErr);
				Assert.AreEqual("lean-pass", run.StdOut.Trim(), run.StdErr);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		/// <summary>
		/// #ConsoleApp builds a console (CUI) executable instead of the default GUI one. On Windows that choice is
		/// the PE subsystem field, which the shell reads before the process starts to decide whether to wait for it
		/// and whether to hand it the terminal's stdio - so it can only be made at build time, and the produced file
		/// is the only place it can be checked. Other platforms have no subsystem: there the directive is inert and
		/// only its acceptance (a clean compile) is asserted.
		/// </summary>
		[Test, NonParallelizable]
		public void ConsoleAppDirective()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-component-console-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);

			try
			{
				// Same body twice, so the subsystem is the only difference between the two executables.
				const string body = "#NoTrayIcon\n#ErrorStdOut\nFileAppend('console-pass', '*')\nExitApp(0)\n";
				var consoleScript = Path.Combine(root, "console.ks");
				var guiScript = Path.Combine(root, "gui.ks");
				File.WriteAllText(consoleScript, "#ConsoleApp\n" + body);
				File.WriteAllText(guiScript, body);
				var consoleExe = BuildExecutable(consoleScript);
				var guiExe = BuildExecutable(guiScript);
#if WINDOWS
				Assert.AreEqual(3, PeSubsystem(consoleExe), "#ConsoleApp must produce a console-subsystem executable");
				Assert.AreEqual(2, PeSubsystem(guiExe), "without the directive the executable must stay a GUI one");
#endif
				// The stamped host still has to run: a subsystem edit that corrupted it would fail only here.
				var run = RunProcess(consoleExe, []);
				Assert.AreEqual(0, run.ExitCode, run.StdErr);
				Assert.AreEqual("console-pass", run.StdOut.Trim(), run.StdErr);
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
			Directory.CreateDirectory(root);

			try
			{
				var script = Path.Combine(root, "embedded-compiler.ks");
				File.WriteAllText(script,
					"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
					+ "info := RunScript(\"#NoTrayIcon`n#ErrorStdOut`nFileAppend('nested-pass', '*')`nExitApp(0)\")\n"
					+ "FileAppend(info.ExitCode ':' info.StdOut.Read(64), '*')\nExitApp()\n");

				var executable = BuildExecutable(script);
				Assert.IsFalse(Directory.Exists(Path.Combine(root, "components", "scripting")),
					"a minimal executable must carry components as integrity-checked embedded assets");

				var run = RunProcess(executable, []);
				Assert.AreEqual(0, run.ExitCode, run.StdErr);
				Assert.AreEqual("0:nested-pass", run.StdOut.Trim(), run.StdErr);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, NonParallelizable]
		public void CksSidecar()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-component-cks-" + Guid.NewGuid().ToString("N"));
			var artifactRoot = Path.Combine(root, "artifact");
			var hostRoot = Path.Combine(root, "host");
			Directory.CreateDirectory(artifactRoot);
			Directory.CreateDirectory(hostRoot);

			try
			{
				var script = Path.Combine(root, "sidecar.ks");
				var compiled = Path.Combine(artifactRoot, "sidecar.cks");
				File.WriteAllText(script,
					"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
					+ "info := RunScript(\"#NoTrayIcon`n#ErrorStdOut`nFileAppend('sidecar-pass', '*')`nExitApp(0)\")\n"
					+ "FileAppend(info.ExitCode ':' info.StdOut.Read(64), '*')\nExitApp()\n");

				var compile = RunLauncher(["--errorstdout", "--compile", "asm", "--with-compiler", "--dest", compiled, script]);
				Assert.AreEqual(0, compile.ExitCode, "compile failed: " + compile.StdErr);
				Assert.IsTrue(File.Exists(compiled));
				Assert.IsTrue(File.Exists(Path.Combine(artifactRoot, "components", "scripting", "compiler", "component.json")));

				CopyLeanHost(hostRoot);
				Assert.IsFalse(Directory.Exists(Path.Combine(hostRoot, "components", "scripting")));
				Assert.IsFalse(Directory.GetFiles(hostRoot, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories).Any());

				var run = RunProcess("dotnet", [Path.Combine(hostRoot, "Keysharp.dll"), "--errorstdout", compiled]);
				Assert.AreEqual(0, run.ExitCode, run.StdErr);
				Assert.AreEqual("0:sidecar-pass", run.StdOut.Trim(), run.StdErr);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
		}

		[Test, NonParallelizable]
		public void RunScriptCks()
		{
			var root = Path.Combine(Path.GetTempPath(), "ks-component-runscript-cks-" + Guid.NewGuid().ToString("N"));
			var artifactRoot = Path.Combine(root, "artifact");
			var hostRoot = Path.Combine(root, "host");
			Directory.CreateDirectory(artifactRoot);
			Directory.CreateDirectory(hostRoot);

			try
			{
				var targetSource = Path.Combine(root, "target.ks");
				var target = Path.Combine(artifactRoot, "target.cks");
				File.WriteAllText(targetSource,
					"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
					+ "ownPath := A_ScriptFullPath\n"
					+ "info := RunScript(\"#NoTrayIcon`n#ErrorStdOut`nFileAppend('nested-pass', '*')`nExitApp(0)\")\n"
					+ "FileAppend(ownPath ':' info.ExitCode ':' info.StdOut.Read(64), '*')\nExitApp()\n");
				var targetCompile = RunLauncher(["--errorstdout", "--compile", "asm", "--with-compiler", "--dest", target, targetSource]);
				Assert.AreEqual(0, targetCompile.ExitCode, "target compile failed: " + targetCompile.StdErr);

				var outerSource = Path.Combine(root, "outer.ks");
				var outer = Path.Combine(root, "outer.cks");
				File.WriteAllText(outerSource,
					"#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n#Import \"Ks\" { RunScript }\n"
					+ $"info := RunScript('{target.Replace("'", "''")}')\n"
					+ "FileAppend(info.ExitCode ':' info.StdOut.Read(512), '*')\nExitApp()\n");
				var outerCompile = RunLauncher(["--errorstdout", "--compile", "asm", "--without-compiler", "--dest", outer, outerSource]);
				Assert.AreEqual(0, outerCompile.ExitCode, "outer compile failed: " + outerCompile.StdErr);
				Assert.IsFalse(Directory.Exists(Path.Combine(root, "components", "scripting")),
					"the outer artifact must not carry its own compiler");

				CopyLeanHost(hostRoot);
				var run = RunProcess("dotnet", [Path.Combine(hostRoot, "Keysharp.dll"), "--errorstdout", outer]);
				Assert.AreEqual(0, run.ExitCode, run.StdErr);
				Assert.AreEqual($"0:{target}:0:nested-pass", run.StdOut.Trim(), run.StdErr);
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
			Directory.CreateDirectory(root);

			try
			{
				var script = Path.Combine(root, "stdout.ks");
				File.WriteAllText(script, "#NoTrayIcon\n#ErrorStdOut\nx := 1\n");
				var result = RunLauncher(["--errorstdout", "--compile", "asm", "--with-compiler", "--dest", "*", script]);
				Assert.AreNotEqual(0, result.ExitCode);
				Assert.IsTrue((result.StdOut + result.StdErr).Contains("requires sidecar scripting components", StringComparison.Ordinal),
					result.StdErr);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
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
			Assert.AreEqual(0, compile.ExitCode, "compile failed: " + compile.StdErr);
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

			process.Start();
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(240000))
			{
				try { process.Kill(true); } catch { }
				Assert.Fail($"'{executable}' did not exit within 240 seconds.");
			}

			// A crashed child can leave a grandchild (its dump writer, an orphaned spawn) holding the redirected
			// pipes open; an unbounded read here then hangs the whole test host until the blame collector aborts
			// the run. Fail just this test instead.
			if (!Task.WaitAll([output, error], 60000))
				Assert.Fail($"'{executable}' exited (code {process.ExitCode}) but its output pipes stayed open; a child process is still attached to them.");

			return (process.ExitCode, output.Result, error.Result);
		}

		private static string NewComponentRoot() => Path.Combine(Path.GetTempPath(), "ks-components-" + Guid.NewGuid().ToString("N"));

		private static string ComponentDirectory(string root, string name) =>
			Path.Combine(root, "components", "scripting", name);

		private static void CopyComponentPayload(Assembly assembly, string root, string name)
		{
			var source = Path.Combine(AppContext.BaseDirectory, "components", "scripting", name);
			Assert.IsTrue(Directory.Exists(source), $"canonical {name} payload is missing at {source}");
			var destination = ComponentDirectory(root, name);
			Directory.CreateDirectory(destination);
			foreach (var file in Directory.EnumerateFiles(source))
				File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
		}
	}
}
