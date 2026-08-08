using Keysharp.Builtins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keysharp.Parsing
{
	[PublicHiddenFromUser]
	public class CompilerHelper
	{
		//CodeEntryPointMethod entryPoint;
		/// <summary>
		/// For some reason, the CodeEntryPoint object doesn't seem to allow adding parameters, so we use the base and manually set values and add string[] args.
		/// </summary>
		//CodeMemberMethod entryPoint;
		//System.Web.Configuration.WebConfigurationManager cfg = new System.Web.Configuration.WebConfigurationManager();

		/// <summary>
		/// Needed as a static here so it can be accessed in other areas of Keysharp.Builtins, such as in Accessors,
		/// to determine if the executing code is a standalone executable, or a script that was compiled and ran through
		/// the main program.
		/// </summary>
		public static Assembly compiledasm;

		/// <summary>The script's `#AssemblyName`, captured while lowering; null when it does not set one.</summary>
		private string declaredAssemblyName;

		/// <summary>
		/// Formatted `#Warning` text from the last <see cref="CompileCodeToByteArray"/> call, or null when it produced
		/// none. Non-fatal, so it is published for the caller to route to wherever that caller shows output, rather
		/// than printed here (see the assignment for why).
		/// </summary>
		public string CompileWarnings { get; private set; }
		public static byte[] compiledBytes;

		/// <summary>
		/// The full path of the script the launcher is actually running, set before a compiled assembly's Main
		/// executes so A_ScriptFullPath/A_ScriptDir/A_ScriptName reflect the runtime location rather than a
		/// baked-in compile-time path (consumed by <see cref="Keysharp.Runtime.Script.SetName"/>). Left null for
		/// a standalone exe (falls back to A_AhkPath) and for stdin (the compiled "*" marker is used instead).
		/// </summary>
		public static string runScriptPath;

		public static readonly string[] requiredManagedDependencies = new[]
		{
			"Keysharp.Core.dll",
			"PCRE.NET.dll",
			"BitFaster.Caching.dll",
			"Semver.dll",
			"Microsoft.Extensions.Primitives.dll", // Required by Semver.dll
#if !WINDOWS
			"Eto.dll",
#endif
		};
		public static readonly string[] requiredNativeDependencies = new[]
		{
			"PCRE.NET.Native" + EmbeddedDependencyLoader.dllExt,
		};

		// Cache of parsed deps.json results, keyed by the deps.json path. Instance-scoped (not static) so a
		// long-lived compiler serving multiple scripts from different deps contexts can't return stale results.
		private readonly Dictionary<string, HashSet<string>> _compiledScriptDependencies = new (StringComparer.OrdinalIgnoreCase);

		public static string GetRidNativeDependencyPath(string fileName) =>
			Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);

		public static string ResolveAppNativeDependencyPath(string appDir, string fileName)
		{
			var ridNativePath = Path.Combine(appDir, GetRidNativeDependencyPath(fileName));

			if (File.Exists(ridNativePath))
				return ridNativePath;

			var rootPath = Path.Combine(appDir, fileName);
			return File.Exists(rootPath) ? rootPath : ridNativePath;
		}

		private static string ResolveDependencyAssetPath(string depsDir, string assetPath)
		{
			if (File.Exists(assetPath))
				return assetPath;

			var relativePath = assetPath.Replace('/', Path.DirectorySeparatorChar);
			var path = Path.Combine(depsDir, relativePath);

			if (File.Exists(path))
				return path;

			return Path.Combine(depsDir, Path.GetFileName(assetPath));
		}

		public HashSet<string> GetCompiledScriptDependencies(string depsJson)
		{
			if (!_compiledScriptDependencies.TryGetValue(depsJson, out var deps))
			{
				deps = new (StringComparer.OrdinalIgnoreCase);
				_compiledScriptDependencies[depsJson] = deps;

				// 2) load and parse
				using var doc = JsonDocument.Parse(File.ReadAllText(depsJson));
				var dir = Path.GetDirectoryName(depsJson);
				var rid = RuntimeInformation.RuntimeIdentifier;
				// 3) drill into the “libraries” section for runtime & native assets
				var targets = doc.RootElement.GetProperty("targets");

				foreach (var target in targets.EnumerateObject())
				{
					foreach (var library in target.Value.EnumerateObject())
					{
						var name = library.Name;
						var info = library.Value;

						// managed assemblies
						// asmEntry.Name might be "lib/netstandard2.0/PCRE.NET.dll"
						if (info.TryGetProperty("runtime", out var runTimeGroup))
							foreach (var asmEntry in runTimeGroup.EnumerateObject())
								switch (Path.GetFileName(asmEntry.Name).ToUpper())
								{
									// Don't include our entry assemblies
									case "KEYSHARP.DLL":
									case "KEYVIEW.DLL":
									case "KEYSHARP.OUTPUTTEST.DLL":
										break;

									default:
										_ = deps.Add(File.Exists(asmEntry.Name) ? asmEntry.Name : Path.Combine(dir, Path.GetFileName(asmEntry.Name)));
										break;
								}

						// native libraries
						// nativeEntry.Name might be "runtimes/win-x64/native/PCRE.NET.Native.dll"
						if (info.TryGetProperty("native", out var nativeGroup))
							foreach (var nativeEntry in nativeGroup.EnumerateObject())
								_ = deps.Add(ResolveDependencyAssetPath(dir, nativeEntry.Name));

						if (info.TryGetProperty("runtimeTargets", out var runtimeTargetsGroup))
							foreach (var nativeEntry in runtimeTargetsGroup.EnumerateObject())
								if (nativeEntry.Value.TryGetProperty("rid", out var targetRid) && targetRid.ValueEquals(rid))
									_ = deps.Add(ResolveDependencyAssetPath(dir, nativeEntry.Name));
					}
				}
			}

			return deps;
		}

		private readonly CodeGeneratorOptions cgo = new ()
		{
			IndentString = "\t",
			VerbatimOrder = true,
			BracingStyle = "C"
		};

		internal readonly CodeDomProvider provider = CodeDomProvider.CreateProvider("csharp", new Dictionary<string, string>
		{
			{
				"CompilerDirectoryPath", Path.Combine(Environment.CurrentDirectory, "./roslyn")
			}
		});

		/// <summary>
		/// Define the compile unit to use for code generation.
		/// </summary>
		//CodeCompileUnit targetUnit;
		public CompilerHelper()
		{
		}

		public static string GenerateRuntimeConfig()
		{
			using (var stream = new MemoryStream())
			{
				using (var writer = new Utf8JsonWriter(
					stream,
				new JsonWriterOptions() { Indented = true }
			))
				{
					writer.WriteStartObject();
					writer.WriteStartObject("runtimeOptions");
					writer.WriteStartObject("framework");
					writer.WriteString("name", "Microsoft.WindowsDesktop.App");
					writer.WriteString(
						"version",
						RuntimeInformation.FrameworkDescription.Replace(".NET ", "")
					);
					writer.WriteEndObject();
					writer.WriteEndObject();
					writer.WriteEndObject();
				}
				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}

		public static (string, string) GetCompilerErrors(CompilerErrorCollection results, string filename = "")
		{
			var sbe = new StringBuilder();
			var sbw = new StringBuilder();

			if (results.HasErrors)
			{
				_ = sbe.AppendLine("The following errors occurred:");
			}

			// Headed in every configuration, not just DEBUG: #Warning is a user-authored message that a release build
			// must still label, otherwise its text is printed with nothing saying it is a warning.
			if (results.HasWarnings)
			{
				_ = sbw.AppendLine("The following warnings occurred:");
			}

			foreach (CompilerError error in results)
			{
				var file = string.IsNullOrEmpty(error.FileName) ? filename : error.FileName;
				file = Path.GetFileName(file);

				if (file.Length == 0)
					file = "*";

				string lineinfo = "";
				if (file != "*")
					lineinfo += file;
				if (error.Line != 0 || error.Column != 0)
				{
					if (lineinfo != "")
						lineinfo += " ";
					lineinfo += $"{error.Line}:{error.Column}";
				}

				_ = !error.IsWarning
					? sbe.AppendLine($"\n{(lineinfo != "" ? lineinfo + ": " : "")}{error.ErrorText}")
					: sbw.AppendLine($"\n{(lineinfo != "" ? lineinfo + ": " : "")}{error.ErrorText}");
			}

			return (sbe.ToString(), sbw.ToString());
		}

		public static string HandleCompilerErrors(ImmutableArray<Diagnostic> diagnostics, string filename, string desc, string message = "")
		{
			var sbe = new StringBuilder();
			var sbw = new StringBuilder();

			foreach (var diag in diagnostics)
			{
				var str = $"{Path.GetFileName(filename)}{diag.Location.GetLineSpan()} - {diag.GetMessage()}";

				if (diag.Severity == DiagnosticSeverity.Warning)
					_ = sbw.AppendLine($"\t{str}");

				if (diag.Severity == DiagnosticSeverity.Error)
					_ = sbe.AppendLine($"\t{str}");
			}

			// Stays DEBUG-only, unlike the same header in GetCompilerErrors: these are Roslyn's warnings about the C#
			// WE generate, which a script author can neither cause nor fix, so a release build does not label them.
#if DEBUG

			if (sbw.Length != 0)
			{
				_ = sbw.Insert(0, "The following warnings occurred:\n");
			}

#endif

			if (sbe.Length != 0)
			{
				_ = sbe.Insert(0, "The following errors occurred:\n");
				return $"{desc} failed.\n\n{sbe}\n{sbw}" + (message != "" ? "\n" + message : "");//Needed to break this up so the AStyle formatter doesn't misformat it.
			}

			return DefaultObject;
		}

        public (EmitResult, MemoryStream, Exception) Compile(string code, string outputname, string currentDir, bool minimalexeout = false)
        {
            try
            {
                var tree = SyntaxFactory.ParseSyntaxTree(code,
                           new CSharpParseOptions(LanguageVersion.LatestMajor, DocumentationMode.None, SourceCodeKind.Regular));
                return CompileFromTree(tree, outputname, currentDir, minimalexeout);
            }
            catch (Exception e)
            {
                return (null, null, e);
            }
        }

		public (EmitResult, MemoryStream, Exception) Compile(CompilationUnitSyntax cu, string outputname, string currentDir, bool minimalexeout = false)
		{


			try
			{
				var parseOptions = new CSharpParseOptions(
					languageVersion: LanguageVersion.LatestMajor,
					documentationMode: DocumentationMode.None,
					kind: SourceCodeKind.Regular
				);
				var tree = SyntaxFactory.SyntaxTree(cu, parseOptions);
				return CompileFromTree(tree, outputname, currentDir, minimalexeout);
			}
			catch (Exception e)
			{
				return (null, null, e);
			}
		}

        public (EmitResult, MemoryStream, Exception) CompileFromTree(SyntaxTree tree, string outputname, string currentDir, bool minimalexeout = false)
		{
			IEnumerable<ResourceDescription> resourceDescriptions = null;
			HashSet<string> allDependencies = null;
			var coreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);
#if WINDOWS
			var desktopDir = Path.GetDirectoryName(typeof(Form).GetTypeInfo().Assembly.Location);
#endif
			var ksCoreDir = Path.GetDirectoryName(Keysharp.Builtins.Ks.A_KeysharpCorePath);

#if OSX
			// In macOS .app bundles, A_KeysharpCorePath may not resolve to the runtime folder.
			// Probe common bundle/runtime paths for managed dependency files.
			if (string.IsNullOrWhiteSpace(ksCoreDir) || !Directory.Exists(ksCoreDir))
			{
				var entryDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
				var baseDir = AppContext.BaseDirectory;
				var candidateDirs = new[]
				{
					baseDir,
					entryDir,
					currentDir,
					baseDir != null ? Path.Combine(baseDir, "..", "Resources") : null,
					baseDir != null ? Path.Combine(baseDir, "..", "..", "Resources") : null
				}
				.Where(d => !string.IsNullOrWhiteSpace(d))
				.Select(d => Path.GetFullPath(d))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

				ksCoreDir = candidateDirs.FirstOrDefault(dir =>
					requiredManagedDependencies.All(dep => File.Exists(Path.Combine(dir, dep))));
			}
#endif

			if (minimalexeout)
			{
				var currentDepsConfigPath = Path.Combine(ksCoreDir ?? "", $"{Assembly.GetEntryAssembly().GetName().Name}.deps.json");

				if (!File.Exists(currentDepsConfigPath))
				{
					currentDepsConfigPath = Path.Combine(currentDir, $"{Assembly.GetEntryAssembly().GetName().Name}.deps.json");

					if (!File.Exists(currentDepsConfigPath))
						currentDepsConfigPath = null;
				}

				if (currentDepsConfigPath != null)
				{
					allDependencies = GetCompiledScriptDependencies(currentDepsConfigPath);
					resourceDescriptions = allDependencies
											.Where(path =>
					{
						switch (Path.GetFileName(path).ToUpper())
						{
							// Exclude Keysharp.Core because it needs to dynamically load the other
							// embedded assemblies and native libraries.
							case "Keysharp.Core.DLL":

							// The following would need to be included if dynamic compilation
							// is desired by the resulting executable.
							case "MICROSOFT.CODEANALYSIS.DLL":
							case "MICROSOFT.CODEANALYSIS.CSHARP.DLL":
							case "MICROSOFT.CODEDOM.PROVIDERS.DOTNETCOMPILERPLATFORM.DLL":
							case "MICROSOFT.NET.HOSTMODEL.DLL":
								return false;

							default:
								return true;
						}
					})
					.Select(path =>
							new ResourceDescription(
						// Prefix with Deps to avoid any naming conflicts. Not sure if this is needed.
						resourceName: "Deps." + Path.GetFileName(path),
						dataProvider: () => File.OpenRead(path),
						isPublic: true
					)
							);
				}
			}

			// This curated list is intentional: as the note further below explains, we deliberately reference only
			// the few assemblies a user script can actually touch rather than everything in deps.json, because
			// pulling in large assemblies (e.g. Microsoft.CodeAnalysis) measurably slows script compilation.
			var references = new List<MetadataReference>
			{
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Collections.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Data.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.IO.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Linq.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Reflection.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")),
#if WINDOWS
				MetadataReference.CreateFromFile(Path.Combine(desktopDir, "System.Drawing.Common.dll")),
				MetadataReference.CreateFromFile(Path.Combine(desktopDir, "System.Windows.Forms.dll")),
#endif
			};

			// Do not load metadata from all dependencies, but just a select few. We need the metadata
			// for only those dependencies which types an user script can have contact with. Loading
			// metadata for unnecessary deps like Microsoft.CodeAnalysis leads to slowdowns because of huge file sizes.
			var hasManagedDepsInKsCoreDir =
				!string.IsNullOrWhiteSpace(ksCoreDir) &&
				requiredManagedDependencies.All(dep => File.Exists(Path.Combine(ksCoreDir, dep)));

			if (hasManagedDepsInKsCoreDir)
			{
				//This will be the build output folder when running from within the debugger, and the install folder when running from an installation.
				//Note that Keysharp.Core.dll and System.CodeDom.dll *must* remain in that location for a compiled executable to work.
				foreach (var dep in requiredManagedDependencies)
					references.Add(MetadataReference.CreateFromFile(Path.Combine(ksCoreDir, dep)));
			}
			else
			{
				var asm = Assembly.GetExecutingAssembly();

				if (!asm.GetManifestResourceNames().Any(s =>
						requiredManagedDependencies.Any(dep =>
							string.Equals(s, "Deps." + dep, StringComparison.OrdinalIgnoreCase))))
					asm = Assembly.GetEntryAssembly();

				var refs = requiredManagedDependencies.Select(logicalName =>
				{
					using var rs = asm.GetManifestResourceStream("Deps." + logicalName)!;
					return MetadataReference.CreateFromStream(rs);
				});
				references.AddRange(refs);
			}

			var ms = new MemoryStream();
			// AnyCpu, like the rest of Keysharp: the script assembly is loaded into this very process (or one
			// built the same way), and the CLR rejects an assembly whose machine type doesn't match the
			// process. Stamping an architecture would only create a mismatch to get wrong.
			const Microsoft.CodeAnalysis.Platform compiledPlatform = Microsoft.CodeAnalysis.Platform.AnyCpu;
			var compilation = CSharpCompilation.Create(outputname)
								.WithOptions(
									// No .WithUsings(): CSharpCompilationOptions.Usings applies only to
									// SourceCodeKind.Script submissions, and this tree is Regular. The lowered code
									// is fully qualified and needs no imports (see Lowerer.AssembleProgram).
									new CSharpCompilationOptions(OutputKind.WindowsApplication)
									.WithOptimizationLevel(OptimizationLevel.Release)
									.WithPlatform(compiledPlatform)
									.WithConcurrentBuild(true)
								)
								.AddReferences(references)
								.AddSyntaxTrees(tree)
								;
			EmitResult compilationResult = null;
#if WINDOWS
			// Apparently there isn't a good way to read app.manifest contents from the running process,
			// so instead we recreate it here.
			// Any change in the manifest should be reflected here and in Keysharp app.manifest file.
			var manifestContents =
				@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
				<assembly xmlns=""urn:schemas-microsoft-com:asm.v1"" manifestVersion=""1.0"">
				    <trustInfo xmlns=""urn:schemas-microsoft-com:asm.v2"">
				        <security>
				            <requestedPrivileges xmlns=""urn:schemas-microsoft-com:asm.v3"">
				                <requestedExecutionLevel level=""asInvoker"" uiAccess=""false"" />
				            </requestedPrivileges>
				        </security>
				    </trustInfo>
				    <asmv3:application xmlns:asmv3=""urn:schemas-microsoft-com:asm.v3"">
				        <asmv3:windowsSettings xmlns=""http://schemas.microsoft.com/SMI/2005/WindowsSettings"">
				            <!-- Extra info: https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests -->
				            <dpiAware>true/pm</dpiAware>
				            <dpiAwareness xmlns=""http://schemas.microsoft.com/SMI/2016/WindowsSettings"">PerMonitorV2,PerMonitor</dpiAwareness>
				            <disableWindowFiltering xmlns=""http://schemas.microsoft.com/SMI/2011/WindowsSettings"">true</disableWindowFiltering>
				            <longPathAware xmlns=""http://schemas.microsoft.com/SMI/2016/WindowsSettings"">true</longPathAware>
				        </asmv3:windowsSettings>
				    </asmv3:application>
					<compatibility xmlns=""urn:schemas-microsoft-com:compatibility.v1"">
						<application>
					        <!-- Earliest XAML Islands build (Win10 1903) -->
						    <maxversiontested Id=""10.0.18362.0""/>
						    <!-- Newer target for wider support range (Win11 23H2) -->
						    <maxversiontested Id=""10.0.22631.0""/>
						    <supportedOS Id=""{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}""/>
						</application>
					</compatibility>
				</assembly>";

			using (var manifestStream = new MemoryStream())
			{
				var writer = new StreamWriter(manifestStream);
				writer.Write(manifestContents);
				writer.Flush();
				manifestStream.Position = 0;
				using var msi = Assembly.GetEntryAssembly().GetManifestResourceStream("Keysharp.Keysharp.ico");
				using var res = compilation.CreateDefaultWin32Resources(true, false, manifestStream, msi);//The first argument must be true to embed version/assembly information.
				compilationResult = compilation.Emit(ms, win32Resources: res, manifestResources: resourceDescriptions);
			}
#else
			// Win32 manifest/icon resources are not applicable outside Windows.
			compilationResult = compilation.Emit(ms, manifestResources: resourceDescriptions);
#endif

			return (compilationResult, ms, null);
		}

        public (string, Exception) CreateCodeFromDom(CodeCompileUnit[] units)
		{
			var sb = new StringBuilder(100000);

			try
			{
				foreach (var unit in units)
				{
					var sourceWriter = new StringWriter();
					provider.GenerateCodeFromCompileUnit(unit, sourceWriter, cgo);//Generating code, then compiling that relieves us of any manual traversal of the DOM.
					_ = sb.Append(sourceWriter.ToString());
				}
			}
			catch (Exception e)
			{
				return (sb.ToString(), e);
			}

			return (sb.ToString(), null);
		}

		/// <summary>
		/// Prepares a (possibly long-lived, reused) <see cref="Script"/> for the next parse. A compile
		/// server reuses one Script across many parses so its built-in-only <c>ReflectionsData</c> and the
		/// lazily filled member caches stay warm; parsing never mutates those, so we deliberately preserve
		/// them. This resets only what a parse actually touches on the Script: the identity fields used for
		/// diagnostics, and the current thread's variable context the parser/accessors require. It is
		/// intentionally NOT a <see cref="Script"/> member because it does not fully reset a Script — no
		/// runtime, UI, hook, or reflection state is cleared.
		/// </summary>
		internal static void ResetScriptForParse(Script script, string scriptPath, string scriptName)
		{
			script.scriptPath = scriptPath;
			script.scriptName = scriptName;
			// Internal parsing can touch accessors, so a current thread context must exist,
			// but parsing itself should not consume a pseudo-thread slot.
			script.Threads.EnsureCurrentThreadVariables();
		}

		public (CompilationUnitSyntax, CompilerErrorCollection) CreateCompilationUnitFromFile(string fileName, string name = null, bool compileToFile = false, string includeDirOverride = null, IEnumerable<string> defines = null)
		{
			CompilationUnitSyntax unit = null;
			var errors = new CompilerErrorCollection();
			var enc = Encoding.Default;
			var x = Env.FindCommandLineArg("cp");
			var script = Script.TheScript;
			var isFile = File.Exists(fileName);
			string scriptPath, scriptName, startupName;

			if (isFile)
			{
				scriptPath = Path.GetFullPath(fileName);
				scriptName = Path.GetFileName(scriptPath);
				startupName = null;
				// Default the runtime script path to this file, so a compile-and-run in the same process (the test
				// runner, or any launcher that doesn't set it explicitly) reports the real path via SetName. Cross-
				// process runs (daemon client, or launching a built .cks/.exe) override runScriptPath themselves.
				runScriptPath = scriptPath;
			}
			else
			{
				scriptPath = "*";
				scriptName = name ?? "*";
				startupName = name;
			}

			ResetScriptForParse(script, scriptPath, scriptName);

			if (x != null)
			{
				x = x.Trim(DashSlash);

				if (x.Length > 2 && int.TryParse(x.AsSpan().Slice(2), out var codepage))
					enc = Encoding.GetEncoding(codepage);
			}

			try
			{
				var source = isFile ? File.ReadAllText(fileName, enc) : fileName;
				// For a real file the include base is its own directory. For in-memory source (e.g. Keyview
				// compiling live editor text), there is no script path, so a caller can supply the directory of
				// the document being edited via includeDirOverride; without it, #include resolution stays
				// disabled (null) as before.
				var includeDir = isFile ? Path.GetDirectoryName(scriptPath) : includeDirOverride;
				var buildName = name ?? (isFile ? Path.GetFileNameWithoutExtension(scriptName) : "*");

				var (prog, parseDiags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(source, includeDir, isFile ? scriptPath : null, defines);

				if (parseDiags.Count > 0)
				{
					foreach (var d in parseDiags)
						_ = errors.Add(ToCompilerError(d, scriptPath));
				}
				else
				{
					var lowerer = new Keysharp.Parsing.Syntax.Lowerer();
					unit = lowerer.Build(prog, buildName, scriptPath, startupName, includeDir, source, compileToFile, defines);
					declaredAssemblyName = lowerer.AssemblyName;   // #AssemblyName; overrides the script-derived name below

					if (unit == null || lowerer.Diagnostics.Count > 0)
						foreach (var d in lowerer.Diagnostics)
							_ = errors.Add(ToCompilerError(d, scriptPath));

					// #Warning: same collection, flagged non-fatal so HasErrors stays false and the build proceeds.
					foreach (var w in lowerer.CompileWarnings)
					{
						var warn = ToCompilerError(w, scriptPath);
						warn.IsWarning = true;
						_ = errors.Add(warn);
					}
				}
			}
			catch (ParseException e)
			{
				_ = errors.Add(new CompilerError(e.File, e.Line.Ai(), e.Column, "0", e.Message));
			}
			catch (Exception e)
			{
				_ = errors.Add(new CompilerError { ErrorText = e.Message + "\n\nStack trace:\n" + e.StackTrace.ToString() });
			}

			return (unit, errors);
		}

		// New-pipeline diagnostics are "line:col: message" strings (lex + parse + lowering). Tokens that originate in a
		// specific file (an #included file, or the named main script) prefix it as "name:line:col: message". Either way,
		// pull out line/col so the existing error-reporting path can surface them; when the embedded name differs from
		// `file` (i.e. an #included file) keep it in the message so the user can tell which file the error is in.
		private static CompilerError ToCompilerError(string diagnostic, string file)
		{
			var m = System.Text.RegularExpressions.Regex.Match(diagnostic ?? "", @"^(?:([^:\r\n]+):)?(\d+):(\d+):\s*(.*)$");
			if (m.Success)
			{
				var srcFile = m.Groups[1].Success ? m.Groups[1].Value : null;
				var text = m.Groups[4].Value;
				if (srcFile != null && !string.Equals(srcFile, System.IO.Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
					text = $"{srcFile}: {text}";
				return new CompilerError(file, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), "0", text);
			}
			return new CompilerError { ErrorText = diagnostic ?? "", FileName = file };
		}

		/// <summary>
		/// Reports compiler errors to the user, either by writing them to the console or, for an
		/// interactive run, by showing a fatal error dialog offering Edit/Reload/ExitApp.
		/// </summary>
		/// <param name="s">The error text to report.</param>
		/// <param name="stdout">When true, write the errors to stdout instead of showing a dialog.</param>
		/// <returns>True if the user chose "Reload" from the error dialog and the caller should restart the script.</returns>
		public bool ReportCompilerErrors(string s, bool stdout = false)
		{
			if (Env.FindCommandLineArg("errorstdout") != null)
				Console.Error.WriteLine(s);//For this to show on the command line, they need to pipe to more like: | more
			else if (stdout)
				Console.WriteLine(s);
			else if (TryShowErrorDialog(s, out var reloadRequested))
				return reloadRequested;
			else
				Console.Error.WriteLine(s);

			return false;
		}

		private static bool TryShowErrorDialog(string s, out bool reloadRequested)
		{
			reloadRequested = false;
			var fileToEdit = GetCurrentScriptFileToEdit();

#if WINDOWS
			reloadRequested = ErrorDialog.ShowFatal(s, fileToEdit) == ErrorDialog.ErrorDialogResult.Reload;
			return true;
#else
			if (Script.IsUiInitializationBlocked || Script.IsHeadless || Script.IsTestHost)
				return false;

			try
			{
				if (Application.Instance == null)
					_ = new Application();

				reloadRequested = ErrorDialog.ShowFatal(s, fileToEdit) == ErrorDialog.ErrorDialogResult.Reload;
				return true;
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"Unable to show compiler error dialog: {ex.Message}");
				return false;
			}
#endif
		}

		private static string GetCurrentScriptFileToEdit()
		{
			var path = Script.TheScript?.scriptPath;
			return ScriptEditor.CanEditFile(path) ? path : null;
		}

		internal string CodeToString(CodeExpression expr)
		{
			using (TextWriter tx = new StringWriter())
			{
				provider.GenerateCodeFromExpression(expr, tx, cgo);
				return tx.ToString();
			}
		}

		internal string CreateEscapedIdentifier(string variable) => provider.CreateEscapedIdentifier(variable);

		// `defines` are the preprocessor symbols for THIS compilation — from `--define:NAME`, or from a caller such as
		// Ks.RunScript. They are per-compilation rather than ambient so a nested compile can choose its own.
		public (byte[], string) CompileCodeToByteArray(string fileName, string nameNoExt, string exeDir = null, bool minimalexeout = false, bool emitCode = false, bool compileToFile = false, IEnumerable<string> defines = null)
		{
			var asm = Assembly.GetExecutingAssembly();
			exeDir ??= Path.GetFullPath(Path.GetDirectoryName(asm.Location.IsNullOrEmpty() ? Environment.ProcessPath : asm.Location));
			var (unit, errs) = CreateCompilationUnitFromFile(fileName, nameNoExt, compileToFile, null, defines);
			// `#AssemblyName` wins over the name derived from the script file; it is only known once the script has
			// been lowered, hence after the call above.
			var assemblyName = declaredAssemblyName ?? nameNoExt ?? "*";
			// #Warning text, published for the caller to route rather than written to a console here: this same method
			// serves the compile daemon (whose stderr belongs to no one) and Keyview (which has no console at all), so
			// printing in place would drop them for exactly the callers that have to forward them. Callers owning a
			// console print this; the daemon ships it to its client. Assigned before the failure branch below so it can
			// never be left stale from a previous compile.
			CompileWarnings = errs.HasWarnings ? GetCompilerErrors(errs).Item2.Trim() : null;

			if (errs.HasErrors || unit == null)
			{
				var (errors, warnings) = GetCompilerErrors(errs);

				var sb = new StringBuilder(1024);
				_ = sb.AppendLine($"Compiling script to DOM failed.");

				if (!string.IsNullOrEmpty(errors))
					_ = sb.Append(errors);

				if (!string.IsNullOrEmpty(warnings))
					_ = sb.Append(warnings);

				return (null, sb.ToString());
			}

			// PrettyPrinter.Print walks the whole syntax tree and is comparatively expensive, so only
			// generate the C# source when a caller actually wants it (emitCode, e.g. --codeout) or when a
			// compile error occurs and we need it for diagnostics. Debug builds always produce it to
			// validate PrettyPrinter against Roslyn's own normalizer.
			string code = null;
			string GetCode() => code ??= PrettyPrinter.Print(unit);
#if DEBUG
			var normalized = unit.NormalizeWhitespace("\t", Environment.NewLine).ToString();
			if (GetCode() != normalized)
			{
				throw new Exception("Code formatting mismatch");
			}
#endif

			if (emitCode)
				_ = GetCode();

			var (results, ms, compileexc) = Compile(unit, assemblyName, exeDir, minimalexeout);

			try
			{
				if (results == null)
				{
					return (null, $"Error compiling C# code to executable: {(compileexc != null ? compileexc.Message : string.Empty)}\n\n{GetCode()}");
				}
				else if (results.Success)
				{
					_ = ms.Seek(0, SeekOrigin.Begin);
					ms.Dispose();
					return (ms.ToArray(), code);
				}
				else
				{
					return (null, HandleCompilerErrors(results.Diagnostics, assemblyName, "Compiling C# code to executable", compileexc != null ? compileexc.Message : string.Empty) + "\n" + GetCode());
				}
			}
			finally
			{
				ms?.Dispose();
			}
		}

		internal object EvaluateCode(string code)
		{
			var coreDir = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);
			var references = new List<MetadataReference>
			{
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "mscorlib.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.dll")),
				MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll"))
			};
			string finalCode = @"
using System;

namespace Dyn
{
	public class DynamicCode
	{
		public object Evaluate()
		{
			return " + code + @";
		}
	}
}";
			var tree = SyntaxFactory.ParseSyntaxTree(finalCode,
					   new CSharpParseOptions(LanguageVersion.LatestMajor, DocumentationMode.None, SourceCodeKind.Regular));
			var ms = new MemoryStream();
			var compilation = CSharpCompilation.Create("DynamicCode")
							  .WithOptions(
								  new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
								  .WithOptimizationLevel(OptimizationLevel.Debug)//Quick evaluations don't need to be optimized.
								  .WithPlatform(Microsoft.CodeAnalysis.Platform.AnyCpu)
								  .WithConcurrentBuild(true)
							  )
							  .AddReferences(references)
							  .AddSyntaxTrees(tree)
							  ;
			var results = compilation.Emit(ms);

			if (results.Success)
			{
				_ = ms.Seek(0, SeekOrigin.Begin);
				var arr = ms.ToArray();
				var compiledasm = Assembly.Load(arr);
				object o = compiledasm.CreateInstance("Dyn.DynamicCode");
				Type t = o.GetType();
				return t.GetMethod("Evaluate").Invoke(o, null);
			}
			else
				throw new ParseException($"Failed to compile: {code}.");
		}

		internal bool IsValidIdentifier(string variable) => provider.IsValidIdentifier(variable);
	}
}
