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

		// Pipeline state belongs to ScriptCompilationResult; these statics describe the running script.

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

		// Framework metadata is immutable and expensive to remap on every compile.
		private static PortableExecutableReference[] frameworkReferences;
		private static readonly Lock frameworkReferencesLock = new();

		// Keysharp dependency references are keyed by their selected directory.
		private static MetadataReference[] curatedFrameworkRefs;
		private sealed record CuratedDeps(string Dir, MetadataReference[] Refs);
		private static CuratedDeps curatedKsDeps;

		/// <summary>All managed shared-framework references, used only for inline C#.</summary>
		private static PortableExecutableReference[] FrameworkReferences()
		{
			if (frameworkReferences != null)
				return frameworkReferences;

			lock (frameworkReferencesLock)
			{
				if (frameworkReferences != null)
					return frameworkReferences;

				var dirs = new List<string> { Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location) };
#if WINDOWS
				dirs.Add(Path.GetDirectoryName(typeof(Form).GetTypeInfo().Assembly.Location));
#endif
				var frameworkDirs = new HashSet<string>(dirs.Where(d => !string.IsNullOrWhiteSpace(d)), StringComparer.OrdinalIgnoreCase);
				var list = new List<PortableExecutableReference>();
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				// Exclude app-local TPA entries; the curated dependency set supplies them.
				if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa && tpa.Length != 0)
				{
					foreach (var file in tpa.Split(Path.PathSeparator))
						if (frameworkDirs.Contains(Path.GetDirectoryName(file) ?? "") && seen.Add(Path.GetFileName(file)))
							try { list.Add(MetadataReference.CreateFromFile(file)); }
							catch { }
				}

				if (list.Count == 0)
				{
					foreach (var dir in frameworkDirs.Where(Directory.Exists))
					{
						foreach (var file in Directory.EnumerateFiles(dir, "*.dll"))
						{
							if (!seen.Add(Path.GetFileName(file)))
								continue;

							try
							{
								using var fs = File.OpenRead(file);
								using var pe = new System.Reflection.PortableExecutable.PEReader(fs);

								if (!pe.HasMetadata)
									continue;
							}
							catch { continue; }

							try { list.Add(MetadataReference.CreateFromFile(file)); }
							catch { }
						}
					}
				}

				return frameworkReferences = [.. list];
			}
		}

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

		/// <summary>Formats a diagnostic with its source file and position.</summary>
		private static string FormatDiagnostic(Diagnostic diag, string fallbackFile)
		{
			var span = diag.Location.GetMappedLineSpan();
			var file = !string.IsNullOrEmpty(span.Path)
					   ? Path.GetFileName(span.Path)
					   : Path.GetFileName(fallbackFile);
			var pos = span.StartLinePosition;
			return $"{file}({pos.Line + 1},{pos.Character + 1}): {diag.GetMessage()}";
		}

		public static string HandleCompilerErrors(ImmutableArray<Diagnostic> diagnostics, string filename, string desc, string message = "")
		{
			var sbe = new StringBuilder();
			var sbw = new StringBuilder();

			foreach (var diag in diagnostics)
			{
				var str = FormatDiagnostic(diag, filename);

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

		/// <summary>Emits one self-contained parsed script.</summary>
		public (EmitResult, MemoryStream, Exception) Compile(ScriptCompilationResult compilation, string outputname, string currentDir, bool minimalexeout = false, List<Diagnostic> diagnoseSink = null)
		{
			try
			{
				var parseOptions = new CSharpParseOptions(
					languageVersion: LanguageVersion.LatestMajor,
					documentationMode: DocumentationMode.None,
					kind: SourceCodeKind.Regular
				);
				var tree = SyntaxFactory.SyntaxTree(compilation.Unit, parseOptions);
				return CompileFromTree(tree, outputname, currentDir, minimalexeout, BuildInlineTrees(compilation, parseOptions),
					diagnoseSink, compilation.Packages);
			}
			catch (Exception e)
			{
				return (null, null, e);
			}
		}

		/// <summary>Resolves packages into a build-time manifest.</summary>
		private static PackageManifest ResolvePackages(IReadOnlyList<Keysharp.Internals.Os.PackageResolver.PackageRef> packages,
													   CompilerErrorCollection errors, string scriptPath, bool allowRestore)
		{
			if (packages == null || packages.Count == 0)
				return null;

			var set = packages.ToList();
			// Even all-missing optional packages need an empty manifest because the script calls LoadPackages().
			var manifest = new PackageManifest();

			// Optional (`*i`) packages must not fail the build, and resolution is whole-graph, so one unavailable
			// optional package would take the required ones down with it. Same two-attempt shape the loader uses.
			if (!Keysharp.Internals.Os.PackageResolver.TryResolve(set, allowRestore, "#Package", out var resolved, out var failure))
			{
				var required = set.Where(p => !p.Optional).ToList();

				if (required.Count == set.Count
					|| (required.Count != 0 && !Keysharp.Internals.Os.PackageResolver.TryResolve(required, allowRestore, "#Package", out resolved, out _)))
				{
					_ = errors.Add(new CompilerError(scriptPath ?? "", 0, 0, "", failure));
					return null;
				}

				// Optional absence is non-fatal, not silent.
				_ = errors.Add(new CompilerError(scriptPath ?? "", 0, 0, "",
												 "#Package: optional package(s) not available, continuing without: "
												 + string.Join(", ", packages.Where(p => p.Optional).Select(p => p.Id))) { IsWarning = true });

				if (required.Count == 0)
					return manifest;   // every package was optional and none resolved: an empty manifest, and the script runs

				set = required;
			}

			var byId = new Dictionary<string, Keysharp.Internals.Os.PackageResolver.ResolvedPackage>(StringComparer.OrdinalIgnoreCase);

			foreach (var r in resolved)
				byId[r.Id] = r;

			try
			{
				foreach (var p in set)
					if (byId.TryGetValue(p.Id, out var hit))
						manifest.Add(hit, p.Version, p.Optional, true);

				// The whole closure is deployed too, but only script-named packages are loaded eagerly.
				var named = new HashSet<string>(manifest.Packages.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

				foreach (var r in resolved)
					if (named.Add(r.Id))
						manifest.Add(r, r.Version, true, false);
			}
			catch (Exception e)
			{
				_ = errors.Add(new CompilerError(scriptPath ?? "", 0, 0, "", $"#Package: could not read a resolved asset: {e.Message}"));
				return null;
			}

			// Report the build-time pins; runtime only reads the manifest.
			Keysharp.Internals.Os.PackageResolver.ReportResolved(set, resolved, "#Package");
			return manifest;
		}

		private static string InlineTreePath(ScriptCompilationResult compilation, InlineCSharpSource source, int index)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var module = new string((source.Module ?? "module").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
			return $"{compilation.ScriptPath ?? "inline"}.inline.{index}.{module}.cs";
		}

		/// <summary>Builds one inline tree per script module with distinct diagnostic paths.</summary>
		private static IReadOnlyList<SyntaxTree> BuildInlineTrees(ScriptCompilationResult compilation, CSharpParseOptions parseOptions)
		{
			if (compilation.InlineSources.Count == 0)
				return [];

			var options = parseOptions.WithPreprocessorSymbols(compilation.InlineDefines);
			return [.. compilation.InlineSources.Select((source, index) =>
				SyntaxFactory.ParseSyntaxTree(source.Code, options, path: InlineTreePath(compilation, source, index)))];
		}

		/// <summary>Binds with the production compilation configuration without emitting IL.</summary>
		public (ImmutableArray<Diagnostic> Diagnostics, Exception Error) DiagnoseFromTree(ScriptCompilationResult compilation, string outputname, string currentDir)
		{
			var diags = new List<Diagnostic>();
			var (_, ms, ex) = Compile(compilation, outputname, currentDir, diagnoseSink: diags);
			ms?.Dispose();
			return ([.. diags], ex);
		}

		internal (EmitResult, MemoryStream, Exception) CompileFromTree(SyntaxTree tree, string outputname, string currentDir, bool minimalexeout = false, IReadOnlyList<SyntaxTree> inlineTrees = null, List<Diagnostic> diagnoseSink = null, PackageManifest packages = null)
		{
			IEnumerable<ResourceDescription> resourceDescriptions = null;
			HashSet<string> allDependencies = null;
			var hasInlineTrees = inlineTrees is { Count: > 0 };
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

			// Keep the common path small; broad framework references are added only for inline C#.
			var curated = curatedFrameworkRefs ??=
			[
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
			];
			var references = new List<MetadataReference>(curated);

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
				var deps = curatedKsDeps;

				if (deps == null || !string.Equals(deps.Dir, ksCoreDir, StringComparison.OrdinalIgnoreCase))
					curatedKsDeps = deps = new(ksCoreDir, [.. requiredManagedDependencies.Select(dep =>
						(MetadataReference)MetadataReference.CreateFromFile(Path.Combine(ksCoreDir, dep)))]);

				references.AddRange(deps.Refs);
			}
			// A stream-created reference has no FilePath, so the dedup set below cannot learn its name from the
			// reference itself; the logical names are recorded here instead.
			List<string> streamRefNames = null;

			if (!hasManagedDepsInKsCoreDir)
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
				streamRefNames = [.. requiredManagedDependencies];
			}

			// Lazily share one file-name dedup set across package and framework additions.
			HashSet<string> have = null;
			HashSet<string> ByFileName()
			{
				if (have == null)
				{
					have = new HashSet<string>(references.OfType<PortableExecutableReference>()
											   .Select(r => Path.GetFileName(r.FilePath ?? "")), StringComparer.OrdinalIgnoreCase);

					if (streamRefNames != null)
						have.UnionWith(streamRefNames);
				}

				return have;
			}

			// Inline C# can name package types. Add pinned package assets before same-named framework assemblies.
			if (packages != null && hasInlineTrees)
			{
				foreach (var path in packages.Packages.SelectMany(p => p.Managed).Select(a => a.Source))
					if (ByFileName().Add(Path.GetFileName(path)))
						try { references.Add(MetadataReference.CreateFromFile(path)); }
						catch { }   // an unreadable asset is the resolver's problem to report, not a reason to fail here
			}

			// Dynamic-only scripts retain the curated reference set and its compile time.
			if (hasInlineTrees)
				foreach (var r in FrameworkReferences())
					if (ByFileName().Add(Path.GetFileName(r.FilePath ?? "")))
						references.Add(r);

			// Carried inside the assembly so the runtime binds what was compiled against, not what it re-decides —
			// for EVERY script with packages, inline C# or not: the runtime's only source of truth is this manifest.
			if (packages != null)
			{
				var json = packages.Write();
				resourceDescriptions = (resourceDescriptions ?? []).Append(
										   new ResourceDescription(PackageManifest.ResourceName,
																   () => new MemoryStream(Encoding.UTF8.GetBytes(json)), true));

				if (minimalexeout)
				{
					var packageResources = packages.Assets.Select(asset =>
					{
						var source = asset.Source;
						return new ResourceDescription(PackageManifest.AssetResourceName(asset), () => File.OpenRead(source), true);
					});
					resourceDescriptions = resourceDescriptions.Concat(packageResources);
				}
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
									// C#'s unsafe keyword is the opt-in; scripts already expose equivalent pointer operations.
									.WithAllowUnsafe(hasInlineTrees)
									.WithConcurrentBuild(true)
								)
								.AddReferences(references)
								// Roslyn merges each module tree's partial counterparts with the lowered tree.
								.AddSyntaxTrees(hasInlineTrees ? [tree, .. inlineTrees] : [tree])
								;
			// Validation binds without paying for emit and resource generation.
			if (diagnoseSink != null)
			{
				diagnoseSink.AddRange(compilation.GetDiagnostics());
				return (null, ms, null);
			}

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

		public ScriptCompilationResult CreateCompilationUnitFromFile(string fileName, string name = null, bool compileToFile = false, string includeDirOverride = null, IEnumerable<string> defines = null, bool allowPackageRestore = true)
		{
			var compilation = new ScriptCompilationResult();
			var errors = compilation.Errors;
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
				compilation.ScriptPath = scriptPath;
				// In-process runners use this default; launchers override it with the runtime path.
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
				// Editors can supply an include base for in-memory source.
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
					compilation.Unit = lowerer.Build(prog, buildName, scriptPath, startupName, includeDir, source, compileToFile, defines);
					compilation.DeclaredAssemblyName = lowerer.AssemblyName;
					compilation.InlineCode = lowerer.InlineSource;
					compilation.InlineSources = lowerer.InlineSources;
					compilation.InlineDefines = lowerer.InlineDefines;

					if (compilation.Unit == null || lowerer.Diagnostics.Count > 0)
						foreach (var d in lowerer.Diagnostics)
							_ = errors.Add(ToCompilerError(d, scriptPath));

					// Report syntax errors before a potentially slow package restore.
					if (!errors.HasErrors)
						compilation.Packages = ResolvePackages(lowerer.Packages, errors, scriptPath, allowPackageRestore);

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

			return compilation;
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
		public (byte[] Bytes, string Text, ScriptCompilationResult Compilation) CompileCodeToByteArray(string fileName, string nameNoExt, string exeDir = null, bool minimalexeout = false, bool emitCode = false, bool compileToFile = false, IEnumerable<string> defines = null, bool allowPackageRestore = true)
		{
			var asm = Assembly.GetExecutingAssembly();
			exeDir ??= Path.GetFullPath(Path.GetDirectoryName(asm.Location.IsNullOrEmpty() ? Environment.ProcessPath : asm.Location));
			var compilation = CreateCompilationUnitFromFile(fileName, nameNoExt, compileToFile, null, defines, allowPackageRestore);
			var unit = compilation.Unit;
			var errs = compilation.Errors;
			var assemblyName = compilation.DeclaredAssemblyName ?? nameNoExt ?? "*";
			// Let each host route warnings to its own output surface.
			if (errs.HasWarnings)
				compilation.AppendWarnings(GetCompilerErrors(errs).Item2);

			if (errs.HasErrors || unit == null)
			{
				var (errors, warnings) = GetCompilerErrors(errs);

				var sb = new StringBuilder(1024);
				_ = sb.AppendLine($"Compiling script to DOM failed.");

				if (!string.IsNullOrEmpty(errors))
					_ = sb.Append(errors);

				if (!string.IsNullOrEmpty(warnings))
					_ = sb.Append(warnings);

				return (null, sb.ToString(), compilation);
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

			var (results, ms, compileexc) = Compile(compilation, assemblyName, exeDir, minimalexeout);

			try
			{
				if (results == null)
				{
					return (null, $"Error compiling C# code to executable: {(compileexc != null ? compileexc.Message : string.Empty)}\n\n{GetCode()}", compilation);
				}
				else if (results.Success)
				{
					if (compilation.InlineCode != null)
					{
						var inlinePaths = compilation.InlineSources.Select((source, index) => InlineTreePath(compilation, source, index))
										 .ToHashSet(StringComparer.Ordinal);
						var userWarnings = results.Diagnostics.Where(d =>
											   d.Severity == DiagnosticSeverity.Warning
											   && inlinePaths.Contains(d.Location.SourceTree?.FilePath ?? ""))
										   .ToImmutableArray();

						if (userWarnings.Length != 0)
						{
							// HandleCompilerErrors intentionally returns no text for warning-only results.
							var sbw = new StringBuilder();
							_ = sbw.AppendLine("The following warnings occurred:");

							foreach (var d in userWarnings)
								_ = sbw.AppendLine($"\t{FormatDiagnostic(d, assemblyName)}");

							compilation.AppendWarnings(sbw.ToString());
						}
					}

					return (ms.ToArray(), code, compilation);
				}
				else
				{
					return (null, HandleCompilerErrors(results.Diagnostics, assemblyName, "Compiling C# code to executable", compileexc != null ? compileexc.Message : string.Empty) + "\n" + GetCode(), compilation);
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
