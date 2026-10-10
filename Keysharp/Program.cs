[assembly: InternalsVisibleTo("Keysharp.Tests")]

namespace Keysharp.Main;

/// <summary>
/// The Keysharp launcher. Command-line parsing lives in <see cref="Runner.Parse"/> (in Keysharp.Core, so
/// it is shared with a compiled script's "/script" path). This launcher only adds the
/// two things that must stay out of Keysharp.Core: the compile daemon, and building an executable
/// (which needs the Microsoft.NET.HostModel package). Runner returns those as deferred results for us to
/// carry out here.
/// </summary>
public static class Program
{
	internal static Version Version => Assembly.GetExecutingAssembly().GetName().Version;

	[STAThread]
	public static int Main(string[] args)
	{
		// Run Script's static constructor eagerly so any error messageboxes render correctly even before a
		// Script instance exists (e.g. a daemon compile failure reported below).
		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Script).TypeHandle);

#if OSX
		args = MacHelper.HandleArgs(args);
#endif
		var start = DateTime.UtcNow;
		var command = Runner.Parse(args);

#if OSX
		MacHelper.HandleCommand(command);
#endif

		// Daemon fast path: a source run - or --validate, the same compile without the run - can offload
		// compilation to the shared daemon, so this lean launcher never loads the parser/Roslyn.
		// KEYSHARP_DAEMON forces it on/off; if unset, release builds use it and debug builds do not.
		if (command.Kind == CliCommandKind.RunSource
				&& !command.FromStdin
				&& !command.Transpile
				&& !command.SyntaxOnly
				&& ShouldUseDaemon())
		{
			var reply = CompileClient.CompileViaServer(command);

			switch (reply.Status)
			{
				case CompileDaemonStatus.Compiled:
					// The daemon compiled in a process whose stderr goes nowhere, so its #Warning text rides back
					// with the bytes and is reported here instead.
					if (!string.IsNullOrEmpty(reply.WarningText))
						Console.Error.WriteLine(reply.WarningText);

					// The daemon compiled the source but this process runs it: point A_ScriptFullPath/A_ScriptDir at
					// the source the user launched, not at a path baked in by the daemon.
					ScriptExecutionState.SourcePath = command.ScriptName;
					ScriptExecutionState.KeysharpArgs = command.KeysharpArgs;

					if (command.Validate)
					{
						Console.WriteLine($"Compilation succeeded in {(DateTime.UtcNow - start).TotalSeconds:N3}s.");
						return LoadCompiledBytes(reply.AssemblyBytes, command);
					}
					else
						return RunCompiledBytes(reply.AssemblyBytes, command.ScriptArgs);

				// Privileged callers retry locally because source and package lookup can hide access denial.
				// Ordinary runs with unrestored packages also retry, allowing restoration.
				case CompileDaemonStatus.CompileFailed when !CompileServer.IsPrivileged:
				case CompileDaemonStatus.PackageRestoreNeeded when command.Validate && !CompileServer.IsPrivileged:
					return Runner.ReportCompileFailure(command, reply.ErrorText, reply.ErrorStdOut);

					// Anything else, a run with unrestored packages or no daemon to answer, compiles in-process below.
			}
		}

		return command.Kind switch
		{
			CliCommandKind.CompileExe => CompileToExe(command),
			CliCommandKind.Daemon => HandleDaemon(command.DaemonArgs),
			CliCommandKind.Package => HandlePackage(command),
#if WINDOWS
			CliCommandKind.Install => WindowsHelper.InstallToPath(command.ExeDir, command.ScriptArgs),
			CliCommandKind.Uninstall => WindowsHelper.RemoveFromPath(command.ExeDir, command.ScriptArgs),
			CliCommandKind.CloseInstances => WindowsHelper.CloseRunningInstances(command.ExeDir, command.ScriptArgs),
#endif
			_ => Runner.Execute(command),
		};
	}

	// Compile-server control, deferred to us by Runner because CompileServer lives in this launcher.
	// daemonArgs[0] is the "--daemon" switch itself: bare "--daemon" starts it; "--daemon stop" stops the
	// running one; "--daemon ping <script>" compiles via a running daemon and reports only (no spawn/run).
	// Only the bare form starts a server: a malformed subcommand is a usage error, not a daemon the user
	// never asked for and now has for hours.
	private static int HandleDaemon(string[] daemonArgs)
	{
		var sub = daemonArgs.Length > 1 ? (Runner.TryGetSwitch(daemonArgs[1], out var daemonSub) ? daemonSub : daemonArgs[1]) : null;

		if (sub == null)
			return CompileServer.Run();

		if (string.Equals(sub, "stop", StringComparison.OrdinalIgnoreCase))
		{
			DaemonCoordinator.StopOwner();
			return 0;
		}

		if (string.Equals(sub, "ping", StringComparison.OrdinalIgnoreCase))
		{
			if (daemonArgs.Length < 3 || string.IsNullOrWhiteSpace(daemonArgs[2]))
				return DaemonUsageError("--daemon ping requires a script path.");

			var reply = CompileClient.TryCompile(new CliCommand { ScriptName = daemonArgs[2] }) ?? DaemonReply.Unavailable;
			Console.WriteLine(reply.Status switch
			{
				CompileDaemonStatus.Compiled => $"daemon ping: OK, {reply.AssemblyBytes.Length} bytes"
					+ (string.IsNullOrEmpty(reply.WarningText) ? "" : $"\n{reply.WarningText}"),
				CompileDaemonStatus.CompileFailed or CompileDaemonStatus.PackageRestoreNeeded => $"daemon ping: COMPILE ERROR\n{reply.ErrorText}",
				_ when reply.ErrorText != null => $"daemon ping: FAIL, the daemon could not compile\n{reply.ErrorText}",
				_ => "daemon ping: FAIL, no daemon reachable",
			});
			return reply.Status == CompileDaemonStatus.Compiled ? 0 : 1;
		}

		return DaemonUsageError($"Unknown --daemon subcommand \"{daemonArgs[1]}\".");
	}

	private static int DaemonUsageError(string problem)
	{
		Console.Error.WriteLine($"{problem} Valid forms: --daemon, --daemon stop, --daemon ping <script>.");
		return 1;
	}

	// --kpm hands the rest of the command line to the package manager. Its commands, arguments and
	// output are kpm's own, so there is one surface to learn and one implementation of it rather than a
	// second copy here that would drift from the standalone tool.
	//
	// Reached by reflection, and deliberately not referenced at build time: KPM.Core travels with an
	// install rather than being part of Keysharp, so a build that could not fetch it still compiles, still
	// packages and still runs everything else. Its absence is a message, not a crash.
	private static int HandlePackage(CliCommand command)
	{
		// Acquired before the first message and outside the try, because every path below reports through
		// it - including the catch. A `using` inside the try would detach the console while unwinding, and
		// send the failure message nowhere; and a plain Console.Error here reaches no terminal at all, since
		// this is a GUI-subsystem process until the parent's console is attached.
		using var console = ConsoleOutput.Acquire();
		var library = Path.Combine(command.ExeDir, "KPM.Core.dll");

		if (!File.Exists(library))
		{
			console.Error.WriteLine($"Package management is not available in this installation: {library} is missing."
									+ "\nkpm can be installed on its own from https://github.com/keysharp-org/KPM/releases.");
			return 1;
		}

		try
		{
			// A library too old to carry the command surface is the one failure worth naming: it says
			// "upgrade this install", where the generic reflection error would say nothing useful.
			var runner = Assembly.LoadFrom(library).GetType("Kpm.Cli.CommandRunner", throwOnError: false);
			var run = runner?.GetMethod("Run", [typeof(string[]), typeof(TextWriter), typeof(TextWriter), typeof(TextReader)]);

			if (run is null)
			{
				console.Error.WriteLine($"{library} is too old for this Keysharp: it does not provide the package"
										+ " commands. Reinstall Keysharp, or replace that file from"
										+ " https://github.com/keysharp-org/KPM/releases.");
				return 1;
			}

			// Which engine the registry resolves for: this one, whatever the caller's environment says.
			// The package manager reads it here rather than being told in every command.
			Environment.SetEnvironmentVariable("KPM_ENGINE_VERSION", Version.ToString());
			return (int)run.Invoke(null, [command.PackageArgs, console.Out, console.Error, console.In]);
		}
		catch (Exception ex)
		{
			// TargetInvocationException hides the real one, and a package command failing is ordinary news.
			var reported = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
			console.Error.WriteLine($"package command failed: {reported.GetType().Name}: {reported.Message}");
			return 1;
		}
	}

	// Builds an executable from a script. Deferred to us by Runner because HostWriter.CreateAppHost
	// requires the Microsoft.NET.HostModel package, which Keysharp.Core deliberately does not reference.
	private static int CompileToExe(CliCommand r)
	{
		var asm = Assembly.GetExecutingAssembly();
		var exePath = Path.GetFullPath(asm.Location);

		if (exePath.IsNullOrEmpty())
			exePath = Environment.ProcessPath;

		var exeDir = Path.GetFullPath(Path.GetDirectoryName(exePath));
		var namenoext = r.NameNoExt;
		var scriptdir = r.ScriptDir;
		var path = r.OutPath;

		if (r.DestPath.Length != 0)
		{
			if (r.DestPath == "*")
				return Runner.Message("--dest * is only valid with --compile asm.", true);

			(path, scriptdir, namenoext) = ResolveCompileExeOutput(r.DestPath, namenoext);
		}

		// The parser resolves built-ins through Script.TheScript, so a parse-context Script is needed for
		// the compile; dispose it once the assembly bytes are produced.
		byte[] arr;
		Keysharp.Components.Scripting.IScriptCompiler compiler;
		Keysharp.Components.Scripting.IScriptCompilationResult exeCompilation;

		using (Runner.NewCompileScript(r))
		{
			if (!ScriptingComponentRegistry.TryGetCompiler(out compiler, out var componentFailure))
				return Runner.Message(componentFailure, true);

			exeCompilation = compiler.Compile(new Keysharp.Components.Scripting.ScriptCompileRequest
			{
				SourceText = r.FromStdin ? r.ScriptName : null,
				ScriptPath = r.FromStdin ? null : r.ScriptName,
				CompilationName = namenoext,
				RuntimeDirectory = exeDir,
				Defines = r.Defines,
				AdditionalComponents = r.IncludeComponents,
				ExcludedComponents = r.ExcludeComponents,
				CodePage = r.CodePage,
				IncludeFile = r.IncludeFile,
				Output = r.MinimalExe
					? Keysharp.Components.Scripting.ScriptCompilationOutput.MinimalExecutable
					: Keysharp.Components.Scripting.ScriptCompilationOutput.Executable,
			});
			arr = exeCompilation.AssemblyBytes;
		}

		if (arr == null)
			return Runner.Message(exeCompilation.ErrorText, true, exeCompilation.ErrorStdOut);

		// #Warning from the compiled script; on the failure path above it is already inside the error text.
		if (!string.IsNullOrEmpty(exeCompilation?.WarningText))
			Console.Error.WriteLine(exeCompilation.WarningText);

		var finalPath = "";

		try
		{
			var outputRuntimeConfigPath = Path.ChangeExtension(path, "runtimeconfig.json");
			var currentRuntimeConfigPath = Path.ChangeExtension(exePath, "runtimeconfig.json");
			var outputDllPath = path + ".dll";
			File.WriteAllBytes(outputDllPath, arr);
			File.Copy(currentRuntimeConfigPath, outputRuntimeConfigPath, true);
			var outputDepsConfigPath = Path.ChangeExtension(path, "deps.json");
			var currentDepsConfigPath = Path.ChangeExtension(exePath, "deps.json");
			File.Copy(currentDepsConfigPath, outputDepsConfigPath, true);
#if LINUX || OSX
			finalPath = path;
			var appHostPath = UnixHelper.FindAppHostTemplate();

			if (appHostPath == null)
				return Runner.Message($"Could not find a .NET {Script.dotNetMajorVersion} apphost template under any dotnet install root, so no executable can be produced. Installing the .NET SDK provides one.", true);

			HostWriter.CreateAppHost(
				appHostSourceFilePath: appHostPath,
				appHostDestinationFilePath: finalPath,
				appBinaryFilePath: $"{namenoext}.dll",
				windowsGraphicalUserInterface: false,
				assemblyToCopyResorcesFrom: outputDllPath);
#if OSX
			if (MacHelper.AdHocSign(finalPath) is { } signError)
				return Runner.Message(signError, true);

#endif
#elif WINDOWS
			var ver = WindowsHelper.GetLatestDotNetVersion();
			finalPath = $"{path}.exe";
			// `#App { ConsoleApp: true }` inverts this: it is the PE subsystem field, and it is what makes a shell
			// wait for the process and hand it the terminal's stdin/stdout. Windows reads it before the process
			// starts, so it can only be chosen here, at build time - nothing the script does at runtime can change
			// either behaviour. GUI stays the default so a double-clicked script never flashes a console window.
			HostWriter.CreateAppHost(
				appHostSourceFilePath: @$"{WindowsHelper.WindowsHostPackRoot}{ver}\runtimes\{WindowsHelper.WindowsHostRid}\native\apphost.exe",
				appHostDestinationFilePath: finalPath,
				appBinaryFilePath: $"{namenoext}.dll",
				windowsGraphicalUserInterface: !exeCompilation.ConsoleApp,
				assemblyToCopyResorcesFrom: outputDllPath);
#endif

			if (compiler.DeploySupportFiles(exeCompilation, scriptdir) is { } deploymentError)
				return Runner.Message(deploymentError, true);
		}
		catch (Exception writeex)
		{
			return Runner.Message($"Writing executable to {finalPath} failed: {writeex.Message}", true);
		}

		return 0;
	}

	private static bool ShouldUseDaemon()
	{
		// KEYSHARP_DAEMON forces the daemon on/off; if unset (or unrecognized), default on for release
		// builds and off for debug builds.
		var value = Environment.GetEnvironmentVariable("KEYSHARP_DAEMON")?.Trim();
		return Conversions.ParseBoolish(value)
#if DEBUG
			   ?? false;
#else
			   ?? true;
#endif
	}

	// Loads a precompiled script assembly (bytes returned by the compile server) and invokes its entry
	// point in this process. No compile-context Script is created here: the compiled assembly's own Main
	// creates its runtime Script.
	private static int RunCompiledBytes(byte[] arr, string[] scriptArgs)
	{
		try
		{
			ScriptExecutionState.Assembly = Assembly.Load(arr);
			var program = ScriptExecutionState.Assembly.GetType($"{Keywords.MainNamespaceName}.{Keywords.MainClassName}");
			var main = program.GetMethod("Main");
#if DEBUG
			Ks.OutputDebugLine("Running compiled code (daemon).");
#endif
			_ = main.Invoke(null, [scriptArgs]).TryCoerceInt(out var exitCode);
			Environment.ExitCode = exitCode;
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
			Environment.ExitCode = Runner.Message(error.ToString(), true);
		}

		return Environment.ExitCode;
	}

	/// <summary>
	/// Validate stops here. The load stays part of the check - bytes the runtime refuses are still a failed
	/// compile - as it is in Runner.CompileAndMaybeRun.
	/// </summary>
	/// <param name="arr"></param>
	/// <param name="command"></param>
	/// <returns></returns>
	private static int LoadCompiledBytes(byte[] arr, CliCommand command)
	{
		try
		{
			ScriptExecutionState.Assembly = Assembly.Load(arr);
			return 0;
		}
		catch (Exception ex)
		{
			return Runner.ReportCompileFailure(command, $"Loading the compiled script failed.\n\n{ex.GetType().Name}: {ex.Message}", false);
		}
	}

	private static (string PathNoExtension, string OutputDir, string NameNoExt) ResolveCompileExeOutput(string outputPath, string scriptNameNoExt)
	{
		var fullPath = Path.GetFullPath(outputPath);
		var isDirectory = Directory.Exists(fullPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar);

		if (isDirectory)
		{
			var outputDir = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			_ = Directory.CreateDirectory(outputDir);
			return (Path.Combine(outputDir, scriptNameNoExt), outputDir, scriptNameNoExt);
		}

		var outputDirForFile = Path.GetDirectoryName(fullPath);

		if (outputDirForFile.IsNullOrEmpty())
			outputDirForFile = Environment.CurrentDirectory;
		else
			_ = Directory.CreateDirectory(outputDirForFile);

		var nameNoExt = Path.GetFileNameWithoutExtension(fullPath);

		if (nameNoExt.IsNullOrEmpty())
			nameNoExt = scriptNameNoExt;

		return (Path.Combine(outputDirForFile, nameNoExt), outputDirForFile, nameNoExt);
	}
}