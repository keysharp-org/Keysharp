using Keysharp.Builtins;
#if !WINDOWS
using MessageBoxIcon = Eto.Forms.MessageBoxType;
#endif

namespace Keysharp.Internals.Scripting
{
	internal enum CliCommandKind
	{
		Error,
		Help,
		Version,
		About,
		RunSource,
		RunAssembly,
		CompileAsm,
		CompileExe,
		Daemon,
		Install,
		Uninstall,
		CloseInstances,
	}

	internal sealed class CliCommand
	{
		internal CliCommandKind Kind;
		internal string ErrorText;
		internal string ExeDir;
		internal Assembly EntryAssembly;

		internal string ScriptName = "";
		internal string NameNoExt = "";
		internal string ScriptDir = "";
		internal string OutPath = "";
		internal string DestPath = "";
		internal string[] ScriptArgs = [];
		internal string[] KeysharpArgs = [];
		internal string[] Defines = [];   // --define:NAME symbols, handed to the compilation this command performs
		internal bool FromStdin;
		internal bool Validate;
		internal bool Transpile;
		internal bool MinimalExe;

		internal string AssemblyType = $"{Keywords.MainNamespaceName}.{Keywords.MainClassName}";
		internal string AssemblyMethod = "Main";
		internal string[] DaemonArgs = [];

		internal bool RequiresLauncher => Kind is CliCommandKind.CompileExe
												   or CliCommandKind.Daemon
												   or CliCommandKind.Install
												   or CliCommandKind.Uninstall
												   or CliCommandKind.CloseInstances;

		internal static CliCommand Error(string text) => new() { Kind = CliCommandKind.Error, ErrorText = text };
		internal static CliCommand Simple(CliCommandKind kind, Assembly asm, string exeDir) => new() { Kind = kind, EntryAssembly = asm, ExeDir = exeDir };
	}

	/// <summary>
	/// Single source of truth for Keysharp command-line parsing and Core-supported execution. The launcher
	/// calls <see cref="Parse"/> once, handles launcher-only command kinds, and delegates the rest to
	/// <see cref="Execute"/>. Compiled scripts use the same parser through "/script", but reject command
	/// kinds which require the launcher.
	/// </summary>
	internal class Runner
	{
		private static readonly CompilerHelper ch = new ();

		internal static CliCommand Parse(string[] args)
		{
			var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
			var exePath = Path.GetFullPath(asm.Location);

			if (exePath.IsNullOrEmpty())
				exePath = Environment.ProcessPath;

			var exeName = Path.GetFileNameWithoutExtension(exePath);
			var exeDir = Path.GetFullPath(Path.GetDirectoryName(exePath));
			var transpile = false;
			var compileExe = false;
			var compileMinimalExe = false;
			var loadAsm = false;
			var asmType = $"{Keywords.MainNamespaceName}.{Keywords.MainClassName}";
			var asmMethod = "Main";
			var scriptName = string.Empty;
			var scriptArgs = System.Array.Empty<string>();
			var keysharpArgs = System.Array.Empty<string>();
			var fromstdin = false;
			var validate = false;
			var compileAsm = false;
			var compileDestPath = "";
			var defines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (var i = 0; i < args.Length; i++)
			{
				if (!TryGetSwitch(args[i], out var option) && !TryGetAhkSlashSwitch(args[i], out option))
				{
					SetInput(args, i, ref scriptName, out scriptArgs, out keysharpArgs);
					break;
				}

				var opt = option.ToLowerInvariant();

				if (opt.StartsWith("asm:", StringComparison.OrdinalIgnoreCase)
						|| opt.StartsWith("assembly:", StringComparison.OrdinalIgnoreCase))
				{
					loadAsm = true;
					var entryPoint = option.Substring(option.IndexOf(':') + 1);

					if (!TryParseAsmEntryPoint(entryPoint, out asmType, out asmMethod, out var entryError))
						return CliCommand.Error(entryError);

					continue;
				}

				// --define:NAME[,NAME...] predefines conditional-compilation symbols, like C#'s -define. Repeatable.
				// This is also the only way to configure an IMPORTED module's compilation: a module is compiled once
				// and shared by every importer, so an importer's own #defines cannot reach it.
				if (opt.StartsWith("define:", StringComparison.OrdinalIgnoreCase))
				{
					if (AddDefineSymbols(option, defines) is string defineError)
						return CliCommand.Error("--define: " + defineError);

					continue;
				}

				if (opt.StartsWith("errorstdout=", StringComparison.OrdinalIgnoreCase)
						|| IsCodePageSwitch(opt))
					continue;

				switch (opt)
				{
					case "force":
					case "f":
					case "restart":
					case "r":
					case "errorstdout":
					case "debug":
						break;

					case "version":
					case "v":
						return CliCommand.Simple(CliCommandKind.Version, asm, exeDir);

					case "help":
					case "h":
					case "?":
						return CliCommand.Simple(CliCommandKind.Help, asm, exeDir);

					case "daemon":
						return new CliCommand { Kind = CliCommandKind.Daemon, EntryAssembly = asm, ExeDir = exeDir, DaemonArgs = [.. args.Skip(i)] };

					case "validate":
						validate = true;
						break;

					case "about":
						return CliCommand.Simple(CliCommandKind.About, asm, exeDir);

					case "asm":
					case "assembly":
						loadAsm = true;
						break;

					case "transpile":
						transpile = true;
						break;

					case "compile":
						if (i + 1 >= args.Length)
						{
							compileAsm = true;
							break;
						}

						switch (args[i + 1].ToLowerInvariant())
						{
							case "exe":
								i++;
								compileExe = true;
								break;

							case "exe-min":
								i++;
								compileMinimalExe = true;
								compileExe = true;
								break;

							case "asm":
							case "dll":
								i++;
								compileAsm = true;
								break;

							default:
								compileAsm = true;
								break;
						}

						break;

					case "dest":
						if (i + 1 >= args.Length || TryGetSwitch(args[i + 1], out _))
							return CliCommand.Error("--dest requires an output path.");

						compileDestPath = args[++i];
						break;

					case "include":
						if (i + 1 >= args.Length)
							return CliCommand.Error("--include requires a file path.");

						i++;
						break;

					case "ilib":
						if (i + 1 >= args.Length)
							return CliCommand.Error("--iLib requires an output path.");

						validate = true;
						i++;
						break;

					case "script":
						break;

					// Without this, a colon-less --define falls through to `default:` and is taken for the script name,
					// so an obvious typo reports "Could not find the script file ...\--define".
					case "define":
						return CliCommand.Error("--define requires a symbol, as --define:NAME[,NAME...].");
#if WINDOWS

					// Register/unregister the shell integration and the PATH entry. An optional trailing
					// "user" or "machine" picks the scope; without one it follows what this process can
					// actually do - machine-wide when elevated, otherwise the current user only.
					case "install":
					case "uninstall":
						return new CliCommand
						{
							Kind = opt == "install" ? CliCommandKind.Install : CliCommandKind.Uninstall,
							EntryAssembly = asm,
							ExeDir = exeDir,
							ScriptArgs = [.. args.Skip(i + 1)],
						};

					// Closes every running process of this install (scripts launched via Keysharp.exe, the
					// compile daemon, and Keyview) so a locked Keysharp.exe / Keysharp.Core.dll can be replaced
					// or removed. Offered as a manual command; the MSI itself closes instances with an injected
					// version-independent action (see Keysharp.Install/package-windows.ps1). An optional trailing
					// number is treated as a UILevel: >= 5 shows a confirmation before closing.
					case "close-instances":
						return new CliCommand
						{
							Kind = CliCommandKind.CloseInstances,
							EntryAssembly = asm,
							ExeDir = exeDir,
							ScriptArgs = [.. args.Skip(i + 1)],
						};
#endif

					default:
						SetInput(args, i, ref scriptName, out scriptArgs, out keysharpArgs);
						break;
				}

				if (!string.IsNullOrEmpty(scriptName))
					break;
			}

			if (!string.IsNullOrEmpty(scriptName) && IsCompiledScriptInput(scriptName))
				loadAsm = true;

			if (string.IsNullOrEmpty(scriptName) && !loadAsm)
			{
				var candidates = new List<string>(6);

				foreach (var dir in new[] { Environment.CurrentDirectory, exeDir })
					foreach (var ext in new[] { ".ahk", ".ks", ".cks" })
						candidates.Add(Path.Combine(dir, exeName + ext));

				foreach (var candidate in candidates)
				{
					if (File.Exists(candidate))
					{
						scriptName = candidate;
						break;
					}
				}

				// A discovered .cks must take the assembly path; the explicit-argument equivalent was routed
				// there at the IsCompiledScriptInput check above, which ran before discovery.
				if (IsCompiledScriptInput(scriptName))
					loadAsm = true;

				// The script was discovered rather than named, so SetInput never ran and every argument was a switch —
				// they are all Keysharp's. Without this they vanish from KeysharpArgs, and callers that gate on it read
				// the run as plain: the compile daemon in particular would then compile without the --define symbols
				// it was never sent, silently taking the other branch.
				if (!string.IsNullOrEmpty(scriptName))
					keysharpArgs = args;
			}

			if (loadAsm && string.IsNullOrEmpty(scriptName))
				return CliCommand.Error("No assembly was specified.");

			if (loadAsm)
				return new CliCommand
				{
					Kind = CliCommandKind.RunAssembly,
					EntryAssembly = asm,
					ExeDir = exeDir,
					ScriptName = scriptName,
					ScriptArgs = scriptArgs,
					KeysharpArgs = keysharpArgs,
					AssemblyType = asmType,
					AssemblyMethod = asmMethod,
				};

			if (scriptName == "*")
			{
				fromstdin = true;
				string s;
				var sb = new StringBuilder(2048);

				while ((s = Console.ReadLine()) != null)
					sb.AppendLine(s);

				scriptName = sb.ToString();
			}

			if (string.IsNullOrEmpty(scriptName))
				return CliCommand.Error($"No script was specified, no text was read from stdin, and no default script named {exeName}.ahk, {exeName}.ks or {exeName}.cks was found in the current folder or next to the executable.");

			if (!fromstdin && !File.Exists(scriptName))
				return CliCommand.Error($"Could not find the script file {scriptName}.");

			if (!compileAsm && !compileExe && compileDestPath.Length != 0)
				return CliCommand.Error("--dest is only valid with --compile.");

			var (nameNoExt, scriptDir, outPath) = GetScriptOutputPaths(scriptName, fromstdin);

			// Only the kinds that COMPILE carry the symbols; RunAssembly above deliberately does not, since a
			// precompiled assembly had its conditionals resolved when it was built.
			return new CliCommand
			{
				Kind = compileExe ? CliCommandKind.CompileExe : compileAsm ? CliCommandKind.CompileAsm : CliCommandKind.RunSource,
				EntryAssembly = asm,
				ExeDir = exeDir,
				ScriptName = scriptName,
				NameNoExt = nameNoExt,
				ScriptDir = scriptDir,
				OutPath = outPath,
				DestPath = compileDestPath,
				ScriptArgs = scriptArgs,
				KeysharpArgs = keysharpArgs,
				Defines = [.. defines],
				FromStdin = fromstdin,
				Validate = validate,
				Transpile = transpile,
				MinimalExe = compileMinimalExe,
			};
		}

		internal static int Execute(CliCommand command)
		{
			MethodInfo runtimeEntryPoint = null;
			object[] runtimeEntryArgs = null;

			try
			{
				switch (command.Kind)
				{
					case CliCommandKind.Error:
						return Message(command.ErrorText, true);

					case CliCommandKind.Version:
						return Message($"{command.EntryAssembly.GetName().Version}", false);

					case CliCommandKind.Help:
						return Message(HelpText(command.EntryAssembly), false);

					case CliCommandKind.About:
						return Message(new StreamReader(Path.Combine(command.ExeDir, "license.txt")).ReadToEnd(), false);

					case CliCommandKind.RunAssembly:
						// A precompiled assembly run from a file (or "*" from stdin): its own path is the running script,
						// so A_ScriptFullPath/A_ScriptDir reflect where the .cks/.dll actually is, not where it was built.
						CompilerHelper.runScriptPath = command.ScriptName;
						runtimeEntryPoint = LoadAssemblyEntryPoint(command);
						runtimeEntryArgs = [command.ScriptArgs];
						goto InvokeRuntimeEntryPoint;

					case CliCommandKind.RunSource:
					case CliCommandKind.CompileAsm:
						return CompileAndMaybeRun(command);

					default:
						return Message("This option is only available from the Keysharp launcher.", true);
				}

			InvokeRuntimeEntryPoint:
#if DEBUG
				Ks.OutputDebugLine("Running compiled code.");
#endif
				return runtimeEntryPoint.Invoke(null, runtimeEntryArgs).Ai();
			}
			catch (Exception ex)
			{
				if (ex is TargetInvocationException)
					ex = ex.InnerException;

				var error = new StringBuilder();
				_ = error.AppendLine("Execution error:\n");
				_ = error.AppendLine($"{ex.GetType().Name}: {ex.Message}");
				_ = error.AppendLine();
				_ = error.AppendLine(ex.StackTrace);
				return Message(error.ToString(), true);
			}
		}

		internal static int Run(string[] args) => Execute(Parse(args));

		/// <summary>
		/// Deploys a compiled script's packages into its private package/version/asset hierarchy. Returns an error
		/// message, or null on success (including when the script declares no packages).
		/// </summary>
		internal static string CopyPackageAssemblies(ScriptCompilationResult compilation, string destDir)
		{
			if (compilation?.Packages == null || string.IsNullOrEmpty(destDir))
				return null;

			return compilation.Packages.CopyTo(destDir);
		}

		/// <summary>Deploys providers used by imperative package calls beside a full standalone executable.</summary>
		internal static string CopyRequiredProviders(ScriptCompilationResult compilation, string destDir)
		{
			if (compilation?.RequiredProviders is not { Count: > 0 } || string.IsNullOrEmpty(destDir))
				return null;

			if (!CompiledPackageProviderManifest.TryBuild(compilation.RequiredProviders, out var manifest, out var failure))
				return failure;

			return manifest.CopyTo(destDir);
		}

		private static int CompileAndMaybeRun(CliCommand command)
		{
			// Tell the (about-to-run) compiled assembly where it is actually running from: the script file's full
			// path, or null for stdin so the compiled "*" marker stands. Drives A_ScriptFullPath/A_ScriptDir.
			CompilerHelper.runScriptPath = command.FromStdin ? null : command.ScriptName;

			using var script = new Script();
			script.ValidateThenExit = command.Validate;
			script.ScriptArgs = command.ScriptArgs;
			script.KeysharpArgs = command.KeysharpArgs;
			// Validation reports unrestored packages; runs and builds may fetch them.
			var (arr, result, compilation) = ch.CompileCodeToByteArray(command.ScriptName, command.NameNoExt, command.ExeDir, false, command.Transpile, command.Kind == CliCommandKind.CompileAsm, command.Defines,
														  allowPackageRestore: !command.Validate);
			// Failed-compilation text already includes its warnings.
			if (arr != null && !string.IsNullOrEmpty(compilation?.Warnings))
				Console.Error.WriteLine(compilation.Warnings);

			if (command.Transpile)
			{
				var codePath = $"{command.OutPath}.cs";

				try
				{
					using var sourceWriter = new StreamWriter(codePath);
					sourceWriter.WriteLine(result);
				}
				catch (Exception writeex)
				{
					return Message($"Writing code to {codePath} failed: {writeex.Message}", true);
				}

				// Hoisted usings make the inline tree invalid if appended to the lowered source.
				if (compilation?.InlineCode is { Length: > 0 } inline)
				{
					var inlinePath = $"{command.OutPath}.inline.cs";

					try
					{
						using var inlineWriter = new StreamWriter(inlinePath);
						inlineWriter.WriteLine(inline);
					}
					catch (Exception writeex)
					{
						return Message($"Writing inline C# to {inlinePath} failed: {writeex.Message}", true);
					}
				}
			}

			if (arr == null)
				return Message(result, true);

			if (command.Kind == CliCommandKind.CompileAsm)
			{
				var compileAsmPath = ResolveCompileAsmOutput(command.DestPath, command.ScriptDir, command.NameNoExt);

				try
				{
					using var outStream = compileAsmPath == "*" ? Console.OpenStandardOutput() : File.Create(compileAsmPath);
					outStream.Write(arr, 0, arr.Length);
				}
				catch (Exception writeex)
				{
					return Message($"Writing assembly to {compileAsmPath} failed: {writeex.Message}", true);
				}

				if (compileAsmPath != "*" && CopyPackageAssemblies(compilation, Path.GetDirectoryName(Path.GetFullPath(compileAsmPath))) is { } copyErr)
					return Message(copyErr, true);

				return 0;
			}

			if (command.Transpile)
				return 0;

			CompilerHelper.compiledasm = Assembly.Load(arr);

			if (command.Validate)
				return 0;

			if (CompilerHelper.compiledasm == null)
				throw new Exception("Compilation failed.");

			var program = CompilerHelper.compiledasm.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}");
			var main = program.GetMethod("Main");
			script.Dispose();
#if DEBUG
			Ks.OutputDebugLine("Running compiled code.");
#endif
			return main.Invoke(null, [command.ScriptArgs]).Ai();
		}

		private static MethodInfo LoadAssemblyEntryPoint(CliCommand command)
		{
			byte[] asmBytes;

			if (command.ScriptName == "*")
			{
				using var stdin = Console.OpenStandardInput();
				using var ms = new MemoryStream();
				stdin.CopyTo(ms);
				asmBytes = ms.ToArray();
			}
			else
				asmBytes = File.ReadAllBytes(command.ScriptName);

			var scriptAsm = Assembly.Load(asmBytes);
			var type = scriptAsm.GetType(command.AssemblyType);

			if (type == null)
				throw new Exception($"Could not find assembly {command.AssemblyType}");

			var method = type.GetMethod(command.AssemblyMethod);

			if (method == null)
				throw new Exception($"Could not find method {command.AssemblyMethod}");

			return method;
		}

		private static string HelpText(Assembly asm)
		{
#if WINDOWS
			const string platformHelp = "  --install [user|machine]    Register Keysharp shell integration and add it to PATH. Defaults to\n                              machine-wide when elevated, otherwise the current user only.\n  --uninstall [user|machine]  Reverse --install.\n  --close-instances           Close every running Keysharp process of this install (used by the installer).\n";
#else
			const string platformHelp = "";
#endif
			return
			$"""
			Keysharp {asm.GetName().Version} - a C# port and enhancement of AutoHotkey.

			Usage:
			  keysharp [options] <script> [script-args...]   Run a .ahk/.ks script.
			  keysharp <script.cks> [script-args...]         Run a precompiled script.

			Run:
			  <script>                Run a .ahk/.ks source file, or a .cks/.dll compiled assembly.
			  --asm, --assembly       Run a precompiled assembly from a file, or from stdin ("*").
			  --asm:NS.Type.Method    Run a precompiled assembly using a custom entry point.
			  --script                In a compiled exe, ignore the embedded script and run the one given next.
			  --force, --restart      Override single-instance behavior.
			  --errorstdout[=ENC]     Write load-time errors to stderr.
			  --cpN                   Read script files using codepage N.
			  --include <file>        Include a file before the main script.
			  --define:NAME[,NAME]    Predefine #if symbols (repeatable). Also reaches imported modules.
			  --debug                 Reserve the AutoHotkey debugging-client switch.
			  --iLib <ignored>        Deprecated alias for --validate.

			Compile:
			  --compile [asm|dll]     Compile the script to a .cks assembly (the default).
			  --compile exe           Compile the script to a standalone executable.
			  --compile exe-min       Compile to a minimal executable (dependencies embedded).
			  --dest <path>           Output file or directory for --compile ("*" writes the asm to stdout).
			  --transpile             Write the generated C# source next to the script (.cs).
			  --validate              Parse and compile the script without running it.

			Compile daemon:
			  --daemon                Start the background compile server (speeds up startup).
			  --daemon stop           Stop the running compile server.
			  --daemon ping <script>  Compile <script> via a running daemon and report only.

			Other:
			  --version, -v           Print the version.
			  --about                 Print license information.
			  --help, -h, -?          Show this help.
			{platformHelp}
			Options may be prefixed with "-" or "--". AutoHotkey-compatible run options may also use "/".
			The KEYSHARP_DAEMON environment variable (1/0/true/false/on/off) forces the compile daemon on or off.
			""";
		}

		internal static bool TryGetSwitch(string value, out string option)
		{
			option = null;

			if (string.IsNullOrEmpty(value) || value.Length < 2)
				return false;

			if (value[0] != '-'
#if WINDOWS
			 && value[0] != '/'
#endif
			 )
				return false;

			option = value.TrimStart(Keywords.DashSlash);
			return option.Length > 0;
		}

		private static bool TryGetAhkSlashSwitch(string value, out string option)
		{
			option = null;

			if (string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != '/')
				return false;

			var candidate = value.Substring(1);

			if (candidate.Equals("force", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("f", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("restart", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("r", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("errorstdout", StringComparison.OrdinalIgnoreCase)
					|| candidate.StartsWith("errorstdout=", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("debug", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("validate", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("ilib", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("include", StringComparison.OrdinalIgnoreCase)
					|| candidate.Equals("script", StringComparison.OrdinalIgnoreCase)
					|| IsCodePageSwitch(candidate))
			{
				option = candidate;
				return true;
			}

			return false;
		}

		private static bool IsCodePageSwitch(string option)
		{
			if (option.Length <= 2 || !option.StartsWith("cp", StringComparison.OrdinalIgnoreCase))
				return false;

			return option.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
		}

		// A --define symbol must be spellable as an #if operand, i.e. exactly what the lexer would produce as a single
		// identifier token — so it is tested with the same character predicates the lexer uses.
		private static bool IsDefineName(string name) =>
			name.Length > 0 && name[0].IsLeadingIdentifierChar() && name.All(c => c.IsIdentifierChar());

		// Parses one `define:NAME[,NAME...]` switch value into `into`, returning an error message for the first name
		// that is not a valid symbol, else null. Shared by the command-line parser and SplitDefines below so the two
		// cannot drift on what a symbol may look like.
		private static string AddDefineSymbols(string option, ICollection<string> into)
		{
			foreach (var name in option.Substring(option.IndexOf(':') + 1)
					 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				// Reject anything that is not a bare identifier rather than defining a symbol no #if can ever name —
				// `--define:FOO=1` is the likely mistake, since a preprocessor symbol carries no value.
				if (!IsDefineName(name))
					return $"'{name}' is not a valid symbol name — it must be a plain identifier, spelled the way an #if would name it. Symbols carry no value.";

				into.Add(name);
			}

			return null;
		}

		/// <summary>
		/// Splits a Keysharp command line into its `--define:NAME` symbols and everything else, so a caller that both
		/// compiles and launches (Ks.RunScript) can apply the symbols to its own compile and forward the rest to the
		/// process it starts. --define cannot simply be forwarded: it selects which code is COMPILED, and by the time
		/// the launched process exists that has already happened.
		/// </summary>
		/// <returns>An error message when a symbol name is invalid, else null.</returns>
		internal static string SplitDefines(IEnumerable<string> args, out List<string> defines, out List<string> rest)
		{
			defines = [];
			rest = [];

			foreach (var arg in args)
			{
				if ((TryGetSwitch(arg, out var option) || TryGetAhkSlashSwitch(arg, out option))
						&& option.StartsWith("define:", StringComparison.OrdinalIgnoreCase))
				{
					if (AddDefineSymbols(option, defines) is string error)
						return error;

					continue;
				}

				rest.Add(arg);
			}

			return null;
		}

		private static void SetInput(string[] args, int index, ref string scriptName, out string[] scriptArgs, out string[] keysharpArgs)
		{
			scriptName = args[index] == "*" ? "*" : Path.GetFullPath(args[index]);
			scriptArgs = [.. args.Skip(index + 1)];
			keysharpArgs = [.. args.Take(index)];
		}

		private static (string NameNoExt, string ScriptDir, string OutPath) GetScriptOutputPaths(string scriptName, bool fromstdin)
		{
			if (!fromstdin)
			{
				var nameNoExt = Path.GetFileNameWithoutExtension(scriptName);
				var scriptDir = Path.GetDirectoryName(scriptName);
				return (nameNoExt, scriptDir, $"{scriptDir}{Path.DirectorySeparatorChar}{nameNoExt}");
			}
			else
			{
				var nameNoExt = "pipestdin";
				var scriptDir = Environment.CurrentDirectory;
				return (nameNoExt, scriptDir, $".{Path.DirectorySeparatorChar}{nameNoExt}");
			}
		}

		private static bool TryParseAsmEntryPoint(string value, out string typeName, out string methodName, out string error)
		{
			typeName = $"{Keywords.MainNamespaceName}.{Keywords.MainClassName}";
			methodName = "Main";
			error = null;

			if (string.IsNullOrWhiteSpace(value))
			{
				error = "Assembly entry point cannot be empty. Use --asm or --asm:Namespace.Type.Method.";
				return false;
			}

			var lastDot = value.LastIndexOf('.');

			if (lastDot <= 0 || lastDot == value.Length - 1)
			{
				error = $"Invalid assembly entry point '{value}'. Use --asm:Namespace.Type.Method.";
				return false;
			}

			typeName = value.Substring(0, lastDot);
			methodName = value.Substring(lastDot + 1);
			return true;
		}

		internal static bool IsCompiledScriptInput(string path)
		{
			var ext = Path.GetExtension(path);
			return ext.Equals(".cks", StringComparison.OrdinalIgnoreCase)
				   || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
		}

		private static string ResolveCompileAsmOutput(string outputPath, string scriptDir, string scriptNameNoExt)
		{
			if (outputPath.Length == 0)
				return Path.Combine(scriptDir, $"{scriptNameNoExt}.cks");

			if (outputPath == "*")
				return "*";

			var fullPath = Path.GetFullPath(outputPath);
			var isDirectory = Directory.Exists(fullPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar);

			if (isDirectory)
			{
				var outputDir = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				_ = Directory.CreateDirectory(outputDir);
				return Path.Combine(outputDir, $"{scriptNameNoExt}.cks");
			}

			var outputDirForFile = Path.GetDirectoryName(fullPath);

			if (!outputDirForFile.IsNullOrEmpty())
				_ = Directory.CreateDirectory(outputDirForFile);

			return fullPath;
		}

		// Reports a message to the user. Errors go through the compiler-error reporter (an error dialog on
		// Windows, or stdout/stderr when redirected/headless); informational text uses an info box or stdout.
		internal static int Message(string text, bool error)
		{
			const string marker = "\nusing static ";
			int idx = text.IndexOf(marker, StringComparison.Ordinal);

			if (idx >= 0)
				text = text.Substring(0, idx);

			if (error)
			{
				try
				{
					if (ch.ReportCompilerErrors(text))
						return RestartCurrentProcess();
				}
				catch (Exception ex)
				{
					var fallback = $"{text}\n\nError reporting failed: {ex.GetType().Name}: {ex.Message}";

					if (TryShowInfoMessageBox(fallback))
						_ = MessageBox.Show(fallback, "Keysharp", MessageBoxButtons.OK, MessageBoxIcon.Error);
					else
						Console.Error.WriteLine(fallback);
				}
			}
			else
			{
				if (TryShowInfoMessageBox(text))
					_ = MessageBox.Show(text, "Keysharp", MessageBoxButtons.OK, MessageBoxIcon.Information);
				else
					Console.WriteLine(text);

				_ = Ks.OutputDebugLine(text);
			}

			return error ? 1 : 0;
		}

		// Relaunches this process with the same arguments. Used when the user clicks "Reload" on a
		// *compile-time* syntax-error dialog, which happens before the script ever runs. Flow.Reload() can't
		// be used here: it posts the restart to the UI thread and waits for the running app to exit, but at
		// this point the only message loop was the modal error dialog (now closed). This also re-passes the
		// managed dll path when running as "dotnet Keysharp.dll <script>".
		private static int RestartCurrentProcess()
		{
			var processPath = Environment.ProcessPath;

			if (processPath.IsNullOrEmpty())
				return 1;

			try
			{
				var args = Environment.GetCommandLineArgs();
				var start = new ProcessStartInfo
				{
					FileName = processPath,
					UseShellExecute = false,
				};
				var processName = Path.GetFileNameWithoutExtension(processPath);
				var includeArg0 = args.Length > 0
					&& processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
					&& args[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
				var firstArg = includeArg0 ? 0 : 1;

				for (var i = firstArg; i < args.Length; i++)
					start.ArgumentList.Add(args[i]);

				_ = Process.Start(start);
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Reload failed: {ex.Message}");
				return 1;
			}
		}

		private static bool TryShowInfoMessageBox(string text)
		{
#if WINDOWS
			return true;
#else
			if (Script.IsHeadless || Script.IsTestHost)
				return false;

			try
			{
				if (Application.Instance == null)
					_ = new Application();

				return true;
			}
			catch (Exception ex)
			{
				_ = Ks.OutputDebugLine($"Unable to initialize UI for message box: {ex.Message}");
				return false;
			}
#endif
		}
	}
}
