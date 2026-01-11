using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Keysharp.Scripting
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
		//Need to manually add the using static statements.
#if WINDOWS

		public static readonly string GlobalUsingStr =
			@"using static Keysharp.Core.Accessors;
using static Keysharp.Core.COM.Com;
using static Keysharp.Core.Common.Keyboard.HotkeyDefinition;
using static Keysharp.Core.Common.Keyboard.HotstringDefinition;
using static Keysharp.Core.Common.Keyboard.HotstringManager;
using static Keysharp.Core.ControlX;
using static Keysharp.Core.Debug;
using static Keysharp.Core.Dialogs;
using static Keysharp.Core.Dir;
using static Keysharp.Core.Dll;
using static Keysharp.Core.Drive;
using static Keysharp.Core.EditX;
using static Keysharp.Core.Env;
using static Keysharp.Core.Errors;
using static Keysharp.Core.External;
using static Keysharp.Core.Files;
using static Keysharp.Core.Flow;
using static Keysharp.Core.Functions;
using static Keysharp.Core.GuiHelper;
using static Keysharp.Core.ImageLists;
using static Keysharp.Core.Images;
using static Keysharp.Core.Ini;
using static Keysharp.Core.Input;
using static Keysharp.Core.Keyboard;
using static Keysharp.Core.KeysharpEnhancements;
using static Keysharp.Core.Loops;
using static Keysharp.Core.Maths;
using static Keysharp.Core.Menu;
using static Keysharp.Core.Misc;
using static Keysharp.Core.Monitor;
using static Keysharp.Core.Mouse;
using static Keysharp.Core.Network;
using static Keysharp.Core.Processes;
using static Keysharp.Core.RegEx;
using static Keysharp.Core.Registrys;
using static Keysharp.Core.Screen;
using static Keysharp.Core.Sound;
using static Keysharp.Core.Strings;
using static Keysharp.Core.ToolTips;
using static Keysharp.Core.Types;
using static Keysharp.Core.WindowX;
using static Keysharp.Core.Windows.WindowsAPI;
using static Keysharp.Scripting.Script.Operator;
using static Keysharp.Scripting.Script;
";

#else
		public static readonly string GlobalUsingStr =
			@"using static Keysharp.Core.Accessors;
using static Keysharp.Core.Common.Keyboard.HotkeyDefinition;
using static Keysharp.Core.Common.Keyboard.HotstringDefinition;
using static Keysharp.Core.Common.Keyboard.HotstringManager;
using static Keysharp.Core.ControlX;
using static Keysharp.Core.Debug;
using static Keysharp.Core.Dialogs;
using static Keysharp.Core.Dir;
using static Keysharp.Core.Dll;
using static Keysharp.Core.Drive;
using static Keysharp.Core.EditX;
using static Keysharp.Core.Env;
using static Keysharp.Core.Errors;
using static Keysharp.Core.External;
using static Keysharp.Core.Files;
using static Keysharp.Core.Flow;
using static Keysharp.Core.Functions;
using static Keysharp.Core.GuiHelper;
using static Keysharp.Core.ImageLists;
using static Keysharp.Core.Images;
using static Keysharp.Core.Ini;
using static Keysharp.Core.Input;
using static Keysharp.Core.Keyboard;
using static Keysharp.Core.KeysharpEnhancements;
using static Keysharp.Core.Loops;
using static Keysharp.Core.Maths;
using static Keysharp.Core.Menu;
using static Keysharp.Core.Misc;
using static Keysharp.Core.Monitor;
using static Keysharp.Core.Mouse;
using static Keysharp.Core.Network;
using static Keysharp.Core.Processes;
using static Keysharp.Core.RegEx;
using static Keysharp.Core.Screen;
using static Keysharp.Core.Sound;
using static Keysharp.Core.Strings;
using static Keysharp.Core.ToolTips;
using static Keysharp.Core.Types;
using static Keysharp.Core.WindowX;
using static Keysharp.Scripting.Script.Operator;
using static Keysharp.Scripting.Script;
";
#endif

		public static readonly string NamespaceUsingStr = $@"
using System
using System.Runtime.InteropServices
using Keysharp.Core
using Keysharp.Core.Common
using Keysharp.Core.Common.File
using Keysharp.Core.Common.Invoke
using Keysharp.Core.Common.ObjectBase
using Keysharp.Core.Common.Strings
using Keysharp.Core.Common.Threading
using Keysharp.Scripting
using Array = Keysharp.Core.Array
using Buffer = Keysharp.Core.Buffer
using String = Keysharp.Core.String
using static {MainClassName}.{UserDeclaredClassesContainerName}
";

		/// <summary>
		/// Needed as a static here so it can be accessed in other areas of Keysharp.Core, such as in Accessors,
		/// to determine if the executing code is a standalone executable, or a script that was compiled and ran through
		/// the main program.
		/// </summary>
		public static Assembly compiledasm;
		public static byte[] compiledBytes;

		public static readonly string[] requiredManagedDependencies = new[]
		{
			"Keysharp.Core.dll",
			"System.CodeDom.dll",
			"PCRE.NET.dll",
			"BitFaster.Caching.dll",
#if !WINDOWS
			"Eto.dll",
#endif
		};
		public static readonly string[] requiredNativeDependencies = new[]
		{
			"PCRE.NET.Native" + EmbeddedDependencyLoader.dllExt,
		};

		private static readonly string[] usings = new[]  //These aren't what show up in the output .cs file. See Parser.GenerateCompileUnit() for that.
		{
			"System",
			"System.Collections",
			"System.Collections.Generic",
			"System.Data",
#if WINDOWS
			"System.Drawing",
			"System.Windows.Forms",
#else
			"Eto.Drawing",
			"Eto.Forms",
#endif
			"System.IO",
			"System.Linq",
			"System.Reflection",
			"System.Runtime",
			"System.Runtime.InteropServices",
			"Keysharp.Core",
		};

		private static HashSet<string> _compiledScriptDependencies;
		public static HashSet<string> GetCompiledScriptDependencies(string depsJson)
		{
			if (_compiledScriptDependencies == null)
			{
				_compiledScriptDependencies = new (StringComparer.OrdinalIgnoreCase);

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
										_ = _compiledScriptDependencies.Add(File.Exists(asmEntry.Name) ? asmEntry.Name : Path.Combine(dir, Path.GetFileName(asmEntry.Name)));
										break;
								}

						// native libraries
						// nativeEntry.Name might be "runtimes/win-x64/native/PCRE.NET.Native.dll"
						if (info.TryGetProperty("native", out var nativeGroup))
							foreach (var nativeEntry in nativeGroup.EnumerateObject())
								_ = _compiledScriptDependencies.Add(File.Exists(nativeEntry.Name) ? nativeEntry.Name : Path.Combine(dir, Path.GetFileName(nativeEntry.Name)));

						if (info.TryGetProperty("runtimeTargets", out var runtimeTargetsGroup))
							foreach (var nativeEntry in runtimeTargetsGroup.EnumerateObject())
								if (nativeEntry.Value.TryGetProperty("rid", out var targetRid) && targetRid.ValueEquals(rid))
									_ = _compiledScriptDependencies.Add(File.Exists(nativeEntry.Name) ? nativeEntry.Name : Path.Combine(dir, Path.GetFileName(nativeEntry.Name)));
					}
				}
			}

			return _compiledScriptDependencies;
		}

		private readonly CodeGeneratorOptions cgo = new ()
		{
			IndentString = "\t",
			VerbatimOrder = true,
			BracingStyle = "C"
		};

		private readonly CodeDomProvider provider = CodeDomProvider.CreateProvider("csharp", new Dictionary<string, string>
		{
			{
				"CompilerDirectoryPath", Path.Combine(Environment.CurrentDirectory, "./roslyn")
			}
		});

		private Parser parser;

		/// <summary>
		/// Define the compile unit to use for code generation.
		/// </summary>
		//CodeCompileUnit targetUnit;
		public CompilerHelper()
		{
			parser = new Parser(this);
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

#if DEBUG

			if (results.HasWarnings)
			{
				_ = sbw.AppendLine("The following warnings occurred:");
			}

#endif

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
			var ksCoreDir = Path.GetDirectoryName(A_KeysharpCorePath);

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
					allDependencies = CompilerHelper.GetCompiledScriptDependencies(currentDepsConfigPath);
					resourceDescriptions = allDependencies
											.Where(path =>
					{
						switch (Path.GetFileName(path).ToUpper())
						{
							// Exclude Keysharp.Core because it needs to dynamically load the other
							// embedded assemblies and native libraries.
							case "KEYSHARP.CORE.DLL":

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

			// should probably try to extract these from deps.json as well, TODO
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
			if (ksCoreDir != null)
			{
				//This will be the build output folder when running from within the debugger, and the install folder when running from an installation.
				//Note that Keysharp.Core.dll and System.CodeDom.dll *must* remain in that location for a compiled executable to work.
				foreach (var dep in requiredManagedDependencies)
					references.Add(MetadataReference.CreateFromFile(Path.Combine(ksCoreDir, dep)));
			}
			else
			{
				var asm = Assembly.GetExecutingAssembly();

				if (!asm.GetManifestResourceNames().Any(s => requiredManagedDependencies.Contains(s)))
					asm = Assembly.GetEntryAssembly();

				var refs = requiredManagedDependencies.Select(logicalName =>
				{
					using var rs = asm.GetManifestResourceStream("Deps." + logicalName)!;
					return MetadataReference.CreateFromStream(rs);
				});
				references.AddRange(refs);
			}

			var ms = new MemoryStream();
			var compilation = CSharpCompilation.Create(outputname)
								.WithOptions(
									new CSharpCompilationOptions(OutputKind.WindowsApplication)
									.WithUsings(usings)
									.WithOptimizationLevel(OptimizationLevel.Release)
									.WithPlatform(Platform.X64)
									.WithConcurrentBuild(true)
								)
								.AddReferences(references)
								.AddSyntaxTrees(tree)
								;
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
			EmitResult compilationResult = null;

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

		public (CompilationUnitSyntax[], CompilerErrorCollection) CreateCompilationUnitFromFile(params string[] fileNames)
        {
            var units = new CompilationUnitSyntax[fileNames.Length];
            var errors = new CompilerErrorCollection();
            var enc = Encoding.Default;
            var x = Env.FindCommandLineArg("cp");
			var script = Script.TheScript;
			bool needsThread = script.totalExistingThreads == 0;
			bool pushed = true;
			ThreadVariables btv = null;
			if (needsThread)
				(pushed, btv) = script.Threads.BeginThread();//Some internal parsing uses Accessors, so a thread must be present.

            if (pushed)
            {
                parser = new Parser(this);

                if (x != null)
                {
                    x = x.Trim(DashSlash);

                    if (x.Length > 2 && int.TryParse(x.AsSpan().Slice(2), out var codepage))
                        enc = Encoding.GetEncoding(codepage);
                }

                for (var i = 0; i < fileNames.Length; i++)//This has likely never been tested with more than one file at a time. Need to figure that out.//TODO
                {
                    try
                    {
                        if (File.Exists(fileNames[i]))
                        {
                            script.scriptName = fileNames[i];
                            units[i] = parser.Parse<CompilationUnitSyntax>(new StreamReader(fileNames[i], enc), Path.GetFullPath(fileNames[i]));
                        }
                        else
                        {
                            script.scriptName = "*";
                            units[i] = parser.Parse<CompilationUnitSyntax>(new StringReader(fileNames[i]), "*");//In memory.
                        }
                    }
                    catch (ParseException e)
                    {
                        _ = errors.Add(new CompilerError(e.File, (int)e.Line, e.Column, "0", e.Message));
                    }
                    catch (Exception e)
                    {
                        _ = errors.Add(new CompilerError { ErrorText = e.Message + "\n\nStack trace:\n" + e.StackTrace.ToString() });
                    }
                    finally { }
                }

				if (needsThread)
					_ = script.Threads.EndThread((pushed, btv));
			}

            return (units, errors);
        }

		public void PrintCompilerErrors(string s, bool stdout = false)
		{
			if (parser.errorStdOut || Env.FindCommandLineArg("errorstdout") != null)
			{
				Console.Error.WriteLine(s);//For this to show on the command line, they need to pipe to more like: | more
			}
			else
			{
				if (stdout)
					Console.WriteLine(s);
				else
#if WINDOWS
					_ = MessageBox.Show(s, "Keysharp", MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
					_ = MessageBox.Show(s, "Keysharp", MessageBoxButtons.OK, MessageBoxType.Error);
#endif
			}
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

		public (byte[], string) CompileCodeToByteArray(string[] fileNames, string nameNoExt, string exeDir = null, bool minimalexeout = false)
		{
			if (fileNames.Length == 0)
				throw new Error("At least one file name must be provided");

			var asm = Assembly.GetExecutingAssembly();
			exeDir ??= Path.GetFullPath(Path.GetDirectoryName(asm.Location.IsNullOrEmpty() ? Environment.ProcessPath : asm.Location));

			var (units, errs) = CreateCompilationUnitFromFile(fileNames);

			if (errs.HasErrors || units[0] == null)
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

			var code = PrettyPrinter.Print(units[0]);
#if DEBUG
			var normalized = units[0].NormalizeWhitespace("\t", Environment.NewLine).ToString();
			if (code != normalized)
			{
				throw new Exception("Code formatting mismatch");
			}
#endif

			var (results, ms, compileexc) = Compile(units[0], nameNoExt, exeDir, minimalexeout);

			try
			{
				if (results == null)
				{
					return (null, $"Error compiling C# code to executable: {(compileexc != null ? compileexc.Message : string.Empty)}\n\n{code}");
				}
				else if (results.Success)
				{
					_ = ms.Seek(0, SeekOrigin.Begin);
					ms.Dispose();
					return (ms.ToArray(), code);
				}
				else
				{
					return (null, HandleCompilerErrors(results.Diagnostics, nameNoExt, "Compiling C# code to executable", compileexc != null ? compileexc.Message : string.Empty) + "\n" + code);
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
			var usings = new List<string>()//These aren't what show up in the output .cs file.
			{
				"System"
			};
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
								  .WithUsings(usings)
								  .WithOptimizationLevel(OptimizationLevel.Debug)//Quick evaluations don't need to be optimized.
								  .WithPlatform(Platform.AnyCpu)
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