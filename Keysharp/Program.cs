using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using Keysharp.Builtins;
using Keysharp.Internals.ExtensionMethods;
using Keysharp.Internals.Scripting;
using Keysharp.Internals.Strings;
using Keysharp.Language;
using Keysharp.Runtime;
using Microsoft.NET.HostModel.AppHost;
#if WINDOWS
using Microsoft.Win32;
using System.Windows.Forms;
#elif OSX
using Eto.Forms;
using System.Threading;
#endif

namespace Keysharp.Main
{
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
			// On macOS, double-clicking a .ks/.ahk file sends an Apple Event rather than a command-line
			// argument. Receive it via Eto's AppDelegate before the normal arg-parsing pipeline.
			var launchedFromFinder = args.Length == 0;

			if (launchedFromFinder)
				args = WaitForMacOsDocumentOpen();
#endif
			var start = DateTime.UtcNow;
			var command = Runner.Parse(args);

#if OSX
			// macOS is single-instance by default: while a Keysharp process runs a (GUI) script, opening another
			// script from Finder routes an "open document" Apple Event to an already-running instance rather than
			// launching a fresh one. Make EVERY instance a dispatcher — run each such document as its own
			// independent process — so opens keep working no matter which instance macOS routes them to (in
			// particular, after the first one exits).
			//
			// The catch: AppKit ALSO re-delivers this instance's OWN launch argument as an open-document event
			// (application:openFiles:), an unpredictable number of times (observed twice for a directly-exec'd
			// child) — even though we already run that script from argv. Left unfiltered, a spawned instance would
			// re-spawn the script it is already running → runaway loop. Filter deterministically by PATH: record
			// the canonicalized script(s) this instance was launched with and ignore open-document events for them;
			// any OTHER path is a genuine request to open a new document and gets its own process. Populated before
			// subscribing so the launch document is known.
			// Only script-running commands can ever receive these events (delivery needs the AppKit run loop,
			// which utility commands like --compile/--validate never start), so keep those children away from
			// Eto.Mac/AppKit entirely.
			if (command.Kind is CliCommandKind.RunSource or CliCommandKind.RunAssembly)
			{
				if (!string.IsNullOrEmpty(command.ScriptName))
					macOsLaunchDocs.Add(CanonicalPath(command.ScriptName));

				// The re-delivery is not limited to the script: any argv entry naming an existing file can come
				// back as a "document" — for a "dotnet Keysharp.dll <script>" launch that includes Keysharp.dll
				// itself, and re-spawning THAT is a scriptless launcher stuck on its error dialog forever.
				foreach (var arg in Environment.GetCommandLineArgs())
					if (!string.IsNullOrEmpty(arg) && File.Exists(arg))
						macOsLaunchDocs.Add(CanonicalPath(arg));

				Eto.Mac.AppDelegate.FileOpened += SpawnScriptInNewProcess;
			}
#endif

			// Daemon fast path: a plain source run - or --validate, the same compile without the run - can offload
			// compilation to the shared daemon, so this lean launcher never loads the parser/Roslyn.
			// KEYSHARP_DAEMON forces it on/off; if unset, release builds use it and debug builds do not.
			// --define is excluded explicitly, not merely via KeysharpArgs: the daemon is sent nothing but a script
			// path, so it would compile with no symbols and silently resolve the #if branches the other way.
			// Other compilation-altering switches go via the KeysharpArgs test (see ValidateWithDefaultCompilation).
			if (command.Kind == CliCommandKind.RunSource
					&& !command.FromStdin
					&& !command.Transpile
					&& (command.KeysharpArgs.Length == 0 || command.ValidateWithDefaultCompilation)
					&& command.Defines.Length == 0
					&& ShouldUseDaemon())
			{
				switch (CompileClient.CompileViaServer(command.ScriptName, out var daemonBytes, out _,
					out var daemonWarnings))
				{
					case CompileDaemonStatus.Compiled:
						// The daemon compiled in a process whose stderr goes nowhere, so its #Warning text rides back
						// with the bytes and is reported here instead.
						if (!string.IsNullOrEmpty(daemonWarnings))
							Console.Error.WriteLine(daemonWarnings);

						// The daemon compiled the source but this process runs it: point A_ScriptFullPath/A_ScriptDir at
						// the source the user launched, not at a path baked in by the daemon.
						ScriptExecutionState.SourcePath = command.ScriptName;

						if (command.Validate)
						{
							Console.WriteLine($"Compilation succeeded in {(DateTime.UtcNow - start).TotalSeconds:N3}s.");
							return LoadCompiledBytes(daemonBytes, command);
						}
						else
							return RunCompiledBytes(daemonBytes, command.ScriptArgs);

					case CompileDaemonStatus.CompileFailed:
						// Recompile locally so source-level load policy is handled by the ordinary parser. Package restore
						// remains disabled for --validate, just as it was in the daemon.
						break;

						// Unreachable + unspawnable: fall through to the in-process runner below.
				}
			}

			return command.Kind switch
			{
				CliCommandKind.CompileExe => CompileToExe(command),
				CliCommandKind.Daemon => HandleDaemon(command.DaemonArgs),
				CliCommandKind.Package => HandlePackage(command),
#if WINDOWS
				CliCommandKind.Install => InstallToPath(command.ExeDir, command.ScriptArgs),
				CliCommandKind.Uninstall => RemoveFromPath(command.ExeDir, command.ScriptArgs),
				CliCommandKind.CloseInstances => CloseRunningInstances(command.ExeDir, command.ScriptArgs),
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

				var st = CompileClient.TryCompile(daemonArgs[2], out var b, out var err, out var warn);
				Console.WriteLine(st switch
				{
					CompileDaemonStatus.Compiled => $"daemon ping: OK, {b.Length} bytes"
						+ (string.IsNullOrEmpty(warn) ? "" : $"\n{warn}"),
					CompileDaemonStatus.CompileFailed => $"daemon ping: COMPILE ERROR\n{err}",
					_ => "daemon ping: FAIL, no daemon reachable",
				});
				return st == CompileDaemonStatus.Compiled ? 0 : 1;
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

		/// <summary>
		/// Writers that reach the terminal the command was launched from. Keysharp is a GUI-subsystem binary,
		/// so on Windows it starts with no console of its own: output to a redirected stdout arrives fine,
		/// but output meant for an interactive terminal goes nowhere until the parent's console is attached.
		/// Released again on dispose, so nothing later in the process inherits a console it did not ask for.
		/// </summary>
		private sealed class ConsoleOutput : IDisposable
		{
			private readonly bool attached;
			private readonly StreamWriter standardOut;
			private readonly StreamWriter standardError;
			private readonly StreamReader standardIn;

			internal TextWriter Out => standardOut ?? Console.Out;
			internal TextWriter Error => standardError ?? Console.Error;
			// Read the same way it is written. A prompt that asks before running a package's setup script
			// must not decode the answer through a different code page than the question was printed in.
			internal TextReader In => standardIn ?? Console.In;

			private ConsoleOutput(bool attached, StreamWriter standardOut, StreamWriter standardError, StreamReader standardIn)
			{
				this.attached = attached;
				this.standardOut = standardOut;
				this.standardError = standardError;
				this.standardIn = standardIn;
			}

			internal static ConsoleOutput Acquire()
			{
				var attached = false;
#if WINDOWS
				// Only when nothing is redirected: a pipe or a file already reaches the caller, and attaching
				// would put the output on the terminal instead of where they asked for it.
				if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
					attached = AttachConsole(AttachParentProcess);

#endif
				// UTF-8 explicitly, on both paths. A GUI-subsystem process gets the ANSI code page by default,
				// which turned the em dash in kpm's own help into a '?' — and package descriptions and author
				// names are full of characters that would go the same way. No BOM: this is piped as often as
				// it is read. On an attached console the code page has to be told as well, or it decodes those
				// bytes as ANSI and mangles them a second time.
				var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

				if (attached)
				{
					try
					{
						Console.OutputEncoding = encoding;
					}
					catch (IOException) { }        // no console after all; the writers below still work
				}

				return new ConsoleOutput(attached,
										 new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true },
										 new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true },
										 new StreamReader(Console.OpenStandardInput(), encoding));
			}

			public void Dispose()
			{
				try
				{
					standardOut?.Flush();
					standardError?.Flush();
				}
				catch (IOException) { }        // the console went away first; there is nothing left to say

#if WINDOWS
				if (attached)
					_ = FreeConsole();

#endif
			}

#if WINDOWS
			private const uint AttachParentProcess = 0xFFFFFFFF;

			[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
			[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
			private static extern bool AttachConsole(uint processId);

			[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
			[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
			private static extern bool FreeConsole();
#endif
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

				(path, scriptdir, namenoext) = ResolveCompileExeOutput(r.DestPath, scriptdir, namenoext);
			}

			// The parser resolves built-ins through Script.TheScript, so a parse-context Script is needed for
			// the compile; dispose it once the assembly bytes are produced.
			byte[] arr;
			string compileResult;
			Keysharp.Components.Scripting.IScriptCompiler compiler;
			Keysharp.Components.Scripting.IScriptCompilationResult exeCompilation;

			using (var script = new Script())
			{
				// Error reporting reads --errorstdout off the Script (Env.FindCommandLineArg); without these a
				// compile failure on a headless CI runner opens a modal dialog and the process hangs forever.
				script.KeysharpArgs = r.KeysharpArgs;
				script.ScriptArgs = r.ScriptArgs;

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
					Output = r.MinimalExe
						? Keysharp.Components.Scripting.ScriptCompilationOutput.MinimalExecutable
						: Keysharp.Components.Scripting.ScriptCompilationOutput.Executable,
				});
				arr = exeCompilation.AssemblyBytes;
				compileResult = exeCompilation.Success ? exeCompilation.GeneratedCode : exeCompilation.ErrorText;
			}

			if (arr == null)
				return Runner.Message(compileResult, true, exeCompilation.ErrorStdOut);

			// #Warning from the compiled script; on the failure path above it is already inside compileResult.
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
				var appHostPath = FindAppHostTemplate();

				if (appHostPath == null)
					return Runner.Message($"Could not find a .NET {Script.dotNetMajorVersion} apphost template under any dotnet install root, so no executable can be produced. Installing the .NET SDK provides one.", true);

				HostWriter.CreateAppHost(
					appHostSourceFilePath: appHostPath,
					appHostDestinationFilePath: finalPath,
					appBinaryFilePath: $"{namenoext}.dll",
					windowsGraphicalUserInterface: false,
					assemblyToCopyResorcesFrom: outputDllPath);
#elif WINDOWS
				var ver = GetLatestDotNetVersion();
				finalPath = $"{path}.exe";
				// `#App { ConsoleApp: true }` inverts this: it is the PE subsystem field, and it is what makes a shell
				// wait for the process and hand it the terminal's stdin/stdout. Windows reads it before the process
				// starts, so it can only be chosen here, at build time - nothing the script does at runtime can change
				// either behaviour. GUI stays the default so a double-clicked script never flashes a console window.
				HostWriter.CreateAppHost(
					appHostSourceFilePath: @$"{WindowsHostPackRoot}{ver}\runtimes\{WindowsHostRid}\native\apphost.exe",
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
				Environment.ExitCode = main.Invoke(null, [scriptArgs]).Ai();
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

		// --validate stops here. The load stays part of the check - bytes the runtime refuses are still a failed
		// compile - as it is in Runner.CompileAndMaybeRun.
		private static int LoadCompiledBytes(byte[] arr, CliCommand command)
		{
			try
			{
				ScriptExecutionState.Assembly = Assembly.Load(arr);
				return 0;
			}
			catch (Exception ex)
			{
				return ReportDaemonFailure(command, $"Loading the compiled script failed.\n\n{ex.GetType().Name}: {ex.Message}");
			}
		}

		// Error reporting reads its switches off the Script, not the command line (see Env.FindCommandLineArg):
		// without KeysharpArgs, --errorstdout goes unseen and a headless --validate stops at a modal dialog.
		private static int ReportDaemonFailure(CliCommand command, string error)
		{
			using var script = new Script();
			script.scriptPath = Path.GetFullPath(command.ScriptName);
			script.scriptName = Path.GetFileName(script.scriptPath);
			script.KeysharpArgs = command.KeysharpArgs;
			script.ScriptArgs = command.ScriptArgs;
			return Runner.Message(error, true);
		}

#if WINDOWS
		// The apphost stamped onto a compiled exe has to match the architecture of the Keysharp that produced
		// it, since that exe loads the same Keysharp.Core and native dependencies sitting next to it.
		internal static string WindowsHostRid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

		internal static string WindowsHostPackRoot => @$"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.{WindowsHostRid}\";
#endif

#if WINDOWS
		internal static string GetLatestDotNetVersion()
			=> Directory.GetDirectories(WindowsHostPackRoot).Select(Path.GetFileName).Where(x => x.StartsWith(Script.dotNetMajorVersion)).OrderByDescending(x => new Version(x.Contains("-rc", StringComparison.OrdinalIgnoreCase) ? x.Substring(0, x.IndexOf("-rc", StringComparison.OrdinalIgnoreCase)) : x)).FirstOrDefault();
#else
		// Locates the newest matching-major apphost template, trying every plausible dotnet install root:
		// the running runtime's own root covers however this process was launched, DOTNET_ROOT covers CI
		// (GitHub runners use /usr/share/dotnet on Linux and ~/.dotnet on macOS), and the rest are the
		// distro/pkg defaults (apt's /usr/lib/dotnet is also visible as /lib/dotnet).
		private static string FindAppHostTemplate()
		{
			var roots = new List<string>();

			void AddRoot(string root)
			{
				if (!root.IsNullOrEmpty() && Directory.Exists(root) && !roots.Contains(root))
					roots.Add(Path.GetFullPath(root));
			}

			try
			{
				// shared/Microsoft.NETCore.App/<ver>/ -> three levels up is the root.
				AddRoot(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
			}
			catch { }

			AddRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
#if LINUX
			AddRoot("/usr/share/dotnet");
			AddRoot("/usr/lib/dotnet");
			AddRoot("/lib/dotnet");
#else
			AddRoot("/usr/local/share/dotnet");
			AddRoot("/usr/share/dotnet");
			AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"));
#endif
			var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
			var rid = $"{(OperatingSystem.IsMacOS() ? "osx" : "linux")}-{arch}";

			foreach (var root in roots)
			{
				// A full SDK carries the template directly; a runtime with the host pack still has one under packs.
				if (NewestHost(Path.Combine(root, "sdk"), Path.Combine("AppHostTemplate", "apphost")) is { } sdkHost)
					return sdkHost;

				if (NewestHost(Path.Combine(root, "packs", $"Microsoft.NETCore.App.Host.{rid}"), Path.Combine("runtimes", rid, "native", "apphost")) is { } packHost)
					return packHost;
			}

			return null;

			static string NewestHost(string versionsDir, string hostRelativePath)
			{
				if (!Directory.Exists(versionsDir))
					return null;

				return Directory.GetDirectories(versionsDir)
					.Where(dir => Path.GetFileName(dir).StartsWith(Script.dotNetMajorVersion))
					.OrderByDescending(dir => ParseVersion(Path.GetFileName(dir)))
					.Select(dir => Path.Combine(dir, hostRelativePath))
					.FirstOrDefault(File.Exists);
			}

			static Version ParseVersion(string name)
			{
				var dash = name.IndexOf('-');   // 10.0.100-rc.1.25451.107 -> 10.0.100

				if (dash > 0)
					name = name.Substring(0, dash);

				return Version.TryParse(name, out var version) ? version : new Version(0, 0);
			}
		}
#endif

		private static (string PathNoExtension, string OutputDir, string NameNoExt) ResolveCompileExeOutput(string outputPath, string scriptDir, string scriptNameNoExt)
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

#if OSX

		// Start a minimal Eto Application, wait up to 1 s for macOS to deliver the "open file" Apple
		// Event (via AppDelegate.OpenFile / OpenFiles), then stop the loop and return the first path.
		// If no event arrives the method returns an empty array and the normal "no script" error follows.
		// The Application instance is deliberately NOT disposed: EnsureEtoApplication() reuses it for
		// GUI scripts that call RunMainWindow → app.Run() afterwards.
		private static string[] WaitForMacOsDocumentOpen()
		{
			string openedPath = null;
			var app = Application.Instance ?? new Application();

			void OnFileOpened(string path)
			{
				Volatile.Write(ref openedPath, path);
				Eto.Mac.AppDelegate.FileOpened -= OnFileOpened;
				app.AsyncInvoke(app.Quit);
			}

			Eto.Mac.AppDelegate.FileOpened += OnFileOpened;

			var timeoutThread = new Thread(() =>
			{
				Thread.Sleep(1000);

				if (Volatile.Read(ref openedPath) == null)
					app.AsyncInvoke(app.Quit);
			}) { IsBackground = true };
			timeoutThread.Start();

			app.Run();

			Eto.Mac.AppDelegate.FileOpened -= OnFileOpened;
			var path = Volatile.Read(ref openedPath);
			return path != null ? [path] : [];
		}

		// Canonicalized script paths this instance was launched with; open-document events for these are macOS
		// re-delivering our own launch argument (which we already run) and must NOT be re-spawned. Written once in
		// Main before the FileOpened handler is subscribed, then only read, so no synchronization is needed.
		private static readonly HashSet<string> macOsLaunchDocs = new(StringComparer.OrdinalIgnoreCase);

		// Launch a script that macOS handed to this already-running instance as its own Keysharp process, so each
		// script runs independently (like double-clicking one when nothing is running). Re-exec THIS apphost with
		// the script path as a plain argument, which bypasses LaunchServices' single-instance document routing.
		private static void SpawnScriptInNewProcess(string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path))
					return;

				// Ignore macOS re-delivering our own launch argument as an open-document event (see Main): this
				// instance already runs that script, so re-spawning it would loop. Matched by canonical path, so
				// it holds however many times AppKit re-delivers it; opening any OTHER script still spawns.
				if (macOsLaunchDocs.Contains(CanonicalPath(path)))
					return;

				// Any existing file is a runnable document (the CLI takes scripts of any extension, and a
				// precompiled .dll runs like a .cks) — except files directly inside Keysharp's own install
				// directory, which are runtime components: only AppKit's argv re-delivery produces those (the
				// entry dll of a "dotnet Keysharp.dll …" launch), and respawning one starts a scriptless
				// launcher that parks on its error dialog holding the caller's pipes.
				if (!File.Exists(path))
					return;

				if (string.Equals(
						Path.GetDirectoryName(CanonicalPath(path)),
						CanonicalPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
						StringComparison.OrdinalIgnoreCase))
					return;

				var exe = Environment.ProcessPath; // the Keysharp apphost inside this .app bundle

				if (!string.IsNullOrEmpty(exe))
				{
					var start = new System.Diagnostics.ProcessStartInfo
					{
						FileName = exe,
						UseShellExecute = false,
					};

					// Under the muxer ("dotnet Keysharp.dll …") the entry dll must be re-inserted; handing dotnet
					// just the script path would try to run the script itself as a managed app.
					if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
							&& Assembly.GetEntryAssembly()?.Location is { Length: > 0 } entryAsm)
						start.ArgumentList.Add(entryAsm);

					start.ArgumentList.Add(path);
					_ = System.Diagnostics.Process.Start(start);
				}
			}
			catch
			{
			}
		}

		[System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
		private static extern IntPtr realpath(string path, IntPtr resolved);

		[System.Runtime.InteropServices.DllImport("libc")]
		private static extern void free(IntPtr ptr);

		// Canonicalize including symlinked path components (macOS's open-document event delivers the resolved
		// path, e.g. /private/tmp/x, while a launch argument may be /tmp/x); Path.GetFullPath does not resolve
		// symlinks, so use realpath.
		private static string CanonicalPath(string path)
		{
			try
			{
				var ptr = realpath(path, IntPtr.Zero);

				if (ptr != IntPtr.Zero)
				{
					try { return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr) ?? Path.GetFullPath(path); }
					finally { free(ptr); }
				}
			}
			catch { }

			try { return Path.GetFullPath(path); } catch { return path; }
		}

#endif

#if WINDOWS

		/// <summary>
		/// Where a manual install writes. Per-machine needs administrator rights; per-user needs none, which
		/// is what makes the portable zip usable without them - the MSI is per-machine only, because WiX v5
		/// cannot build a package that is both (see docs/design-wix-installer-migration.md, D1).
		/// </summary>
		private enum InstallScope
		{
			Machine,
			User
		}

		private static bool IsElevated()
		{
			try
			{
				using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
				return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Reads an explicit "user" / "machine" argument, falling back to what this process can actually do.
		/// Defaulting by elevation keeps the old behaviour for an administrator while letting an ordinary
		/// user run the same command instead of failing with an access-denied exception part-way through.
		/// </summary>
		private static bool TryResolveScope(string[] args, out InstallScope scope, out string error)
		{
			error = null;
			var requested = args?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))?.Trim();

			if (string.IsNullOrEmpty(requested))
			{
				scope = IsElevated() ? InstallScope.Machine : InstallScope.User;
				return true;
			}

			if (string.Equals(requested, "machine", StringComparison.OrdinalIgnoreCase))
			{
				scope = InstallScope.Machine;

				if (!IsElevated())
				{
					error = "A machine-wide install needs administrator rights. Run this from an elevated prompt, or omit \"machine\" to install for the current user only.";
					return false;
				}

				return true;
			}

			if (string.Equals(requested, "user", StringComparison.OrdinalIgnoreCase))
			{
				scope = InstallScope.User;
				return true;
			}

			scope = InstallScope.User;
			error = $"Unrecognized scope '{requested}'. Use \"user\" or \"machine\", or pass nothing to choose automatically.";
			return false;
		}

		// The two hives the scope selects between. HKCU\Software\Classes is a genuine per-user class store -
		// the same place the MSI's HKMU-rooted rows land when a package installs per-user - so the key paths
		// below are identical in both cases and only the root differs.
		private static RegistryKey RootFor(InstallScope scope) => scope == InstallScope.Machine ? Registry.LocalMachine : Registry.CurrentUser;

		// Machine PATH lives under Session Manager and is spelled PATH; the per-user one is a top-level
		// Environment key and is conventionally spelled Path. Both are written back as REG_EXPAND_SZ so
		// entries such as %SystemRoot% in the existing value keep working.
		private static (RegistryKey Root, string Key, string Name) PathLocationFor(InstallScope scope) =>
			scope == InstallScope.Machine
			? (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "PATH")
			: (Registry.CurrentUser, "Environment", "Path");

		// Trailing separators are ignored when matching, so an entry written by the MSI - which resolves
		// [INSTALLFOLDER] with a trailing backslash - is still recognised as the same directory here.
		private static bool SamePathEntry(string a, string b) =>
			string.Equals(a?.TrimEnd('\\', '/'), b?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

		private static int InstallToPath(string path, string[] args)
		{
			if (!TryResolveScope(args, out var scope, out var scopeError))
			{
				Console.Error.WriteLine(scopeError);
				return 1;
			}

			try
			{
				var (root, keyName, valueName) = PathLocationFor(scope);
				using var key = root.CreateSubKey(keyName);
				var oldPath = (string)key.GetValue(valueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames);

				if (!oldPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(s => SamePathEntry(s, path)))
					key.SetValue(valueName, oldPath.Length == 0 ? path : oldPath + (oldPath.EndsWith(';') ? path : $";{path}"), RegistryValueKind.ExpandString);

				RegisterShellIntegration(path, RootFor(scope));
				Console.WriteLine($"Registered Keysharp at '{path}' for {(scope == InstallScope.Machine ? "all users" : "the current user")}.");
				return 0;
			}
			catch (UnauthorizedAccessException)
			{
				Console.Error.WriteLine("Access denied writing the registry. Run from an elevated prompt for a machine-wide install, or pass \"user\" to install for the current user only.");
				return 1;
			}
		}

		private static int RemoveFromPath(string path, string[] args)
		{
			if (!TryResolveScope(args, out var scope, out var scopeError))
			{
				Console.Error.WriteLine(scopeError);
				return 1;
			}

			DaemonCoordinator.StopOwner();

			// An administrator may have registered either way round, so clean both hives; an ordinary user can
			// only have written their own, and the machine attempt is skipped rather than failed. Removing what
			// is not there is a no-op, so this cannot damage the other scope's registration.
			var scopes = scope == InstallScope.Machine
						 ? new[] { InstallScope.Machine, InstallScope.User }
						 : new[] { InstallScope.User };

			foreach (var target in scopes)
			{
				try
				{
					var (root, keyName, valueName) = PathLocationFor(target);
					using var key = root.CreateSubKey(keyName);
					var oldPath = (string)key.GetValue(valueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames);
					var newPath = string.Join(';', oldPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !SamePathEntry(s, path)));

					if (!string.Equals(newPath, oldPath, StringComparison.Ordinal))
						key.SetValue(valueName, newPath, RegistryValueKind.ExpandString);

					UnregisterShellIntegration(RootFor(target));
				}
				catch (UnauthorizedAccessException)
				{
					// Only reachable for the machine hive, and only if rights were lost between the check and
					// here; the per-user half still completed.
				}
			}

			Console.WriteLine($"Unregistered Keysharp for {(scope == InstallScope.Machine ? "all users and the current user" : "the current user")}.");
			return 0;
		}

		// Closes every process belonging to THIS install — scripts launched through Keysharp.exe, the compile
		// daemon, and the Keyview editor — so a locked Keysharp.exe / Keysharp.Core.dll can be replaced or
		// deleted. Windows refuses to overwrite a running image or a loaded DLL, so an upgrade or uninstall
		// performed while Keysharp is running otherwise fails or defers files to a reboot, which can leave a
		// stale-version compile daemon serving against the new binaries. This is a manual command; the MSI does
		// its own version-independent close before InstallValidate (see Keysharp.Install/package-windows.ps1).
		// Best-effort: a kill failure never blocks; only an explicit "No" at the optional prompt returns nonzero.
		private static int CloseRunningInstances(string exeDir, string[] args)
		{
			// Stop the compile daemon through its coordinator first: it may run as a different user, and
			// StopOwner kills by the recorded PID regardless. The scan below mops up anything still holding files.
			try { DaemonCoordinator.StopOwner(); } catch { }

			var dir = Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var selfId = Environment.ProcessId;

			var targets = Process.GetProcessesByName("Keysharp")
						  .Concat(Process.GetProcessesByName("Keyview"))
						  .Where(p =>
			{
				if (p.Id == selfId)
					return false;

				try
				{
					var moduleDir = Path.GetDirectoryName(Path.GetFullPath(p.MainModule.FileName));
					return string.Equals(moduleDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), dir, StringComparison.OrdinalIgnoreCase);
				}
				catch
				{
					return false; // Exited, or a process we cannot inspect (different elevation/user).
				}
			}).ToArray();

			if (targets.Length == 0)
				return 0;

			// Confirm before closing when a UILevel >= 5 is passed. Uses MessageBox.Show, matching how the rest
			// of Keysharp reports to the user (see Runner.Message).
			if (args.Length > 0 && int.TryParse(args[0], out var uiLevel) && uiLevel >= 5)
			{
				var prompt = $"Keysharp is currently running ({targets.Length} process(es)).\n\n"
							 + "It must be closed to continue. Close it now?\n\n"
							 + "Choose No to cancel.";

				if (MessageBox.Show(prompt, "Keysharp", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
					return 1; // Caller asked to confirm and the user declined.
			}

			foreach (var p in targets)
			{
				try
				{
					// Ask GUI scripts / Keyview to close gracefully (runs OnExit, lets them save), then force-kill
					// anything that ignores it or has no window (background scripts, the console daemon).
					if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000))
						continue;

					if (!p.HasExited)
					{
						p.Kill();
						_ = p.WaitForExit(5000);
					}
				}
				catch { /* already gone, or cannot be killed; best-effort */ }
				finally { p.Dispose(); }
			}

			return 0;
		}

		private static void RegisterShellIntegration(string path, RegistryKey root)
		{
			var exe = Path.Combine(path, "Keysharp.exe");
			var keyviewExe = Path.Combine(path, "Keyview.exe");
			var command = $"\"{exe}\" \"%1\"";
			var compileCommand = $"\"{exe}\" --compile \"%1\"";
			var editCommand = $"\"{keyviewExe}\" \"%1\"";

			using (var ext = root.CreateSubKey(@"Software\Classes\.ks"))
				ext.SetValue("", "Keysharp");

			// Explorer's New > Keysharp script entry, seeded from the same template the Dash writes and the
			// MSI/MSIX register. Skipped rather than dangling if the file is missing from this layout.
			var templatePath = Path.Combine(path, "Scripts", "Template.ks");

			if (File.Exists(templatePath))
				using (var shellNew = root.CreateSubKey(@"Software\Classes\.ks\ShellNew"))
					shellNew.SetValue("FileName", templatePath);

			using (var type = root.CreateSubKey(@"Software\Classes\Keysharp"))
				type.SetValue("", "Keysharp script");

			using (var icon = root.CreateSubKey(@"Software\Classes\Keysharp\DefaultIcon"))
				icon.SetValue("", $"\"{exe}\",0");

			using (var open = root.CreateSubKey(@"Software\Classes\Keysharp\shell\open\command"))
				open.SetValue("", command);

			using (var ext = root.CreateSubKey(@"Software\Classes\.cks"))
				ext.SetValue("", "Keysharp.CompiledScript");

			using (var type = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript"))
				type.SetValue("", "Compiled Keysharp script");

			using (var icon = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript\DefaultIcon"))
				icon.SetValue("", $"\"{exe}\",0");

			using (var open = root.CreateSubKey(@"Software\Classes\Keysharp.CompiledScript\shell\open\command"))
				open.SetValue("", command);

			// Older installers registered this iconless verb under the .ks ProgID. Remove it so only the
			// SystemFileAssociations verb below remains.
			root.DeleteSubKeyTree(@"Software\Classes\Keysharp\shell\compile", false);
			RegisterCompileVerb(".ahk", compileCommand, exe, root);
			RegisterCompileVerb(".ks", compileCommand, exe, root);

			if (File.Exists(keyviewExe))
			{
				RegisterEditVerb(".ahk", editCommand, keyviewExe, root);
				RegisterEditVerb(".ks", editCommand, keyviewExe, root);
				RegisterKeyviewOpenWith(keyviewExe, root);
			}
		}

		// Registers Keyview as an application Windows offers under "Open with" for script files, WITHOUT making
		// it the default handler: .ks keeps its "Keysharp" ProgID (set above), so double-clicking still runs the
		// script through Keysharp.exe. SupportedTypes scopes Keyview to the recommended list for these extensions,
		// and the per-extension OpenWithList entries make it show deterministically.
		private static void RegisterKeyviewOpenWith(string keyviewExe, RegistryKey root)
		{
			using (var app = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe"))
				app.SetValue("FriendlyAppName", "Keyview");

			using (var icon = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\DefaultIcon"))
				icon.SetValue("", $"\"{keyviewExe}\",0");

			using (var open = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\shell\open\command"))
				open.SetValue("", $"\"{keyviewExe}\" \"%1\"");

			using (var supported = root.CreateSubKey(@"Software\Classes\Applications\Keyview.exe\SupportedTypes"))
			{
				supported.SetValue(".ahk", "");
				supported.SetValue(".ks", "");
			}

			using (root.CreateSubKey(@"Software\Classes\.ahk\OpenWithList\Keyview.exe")) { }
			using (root.CreateSubKey(@"Software\Classes\.ks\OpenWithList\Keyview.exe")) { }
		}

		private static void RegisterCompileVerb(string extension, string command, string exe, RegistryKey root)
		{
			using var shell = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{extension}\shell\KeysharpCompile");
			shell.SetValue("", "Compile");
			shell.SetValue("Icon", $"\"{exe}\",0");

			using var commandKey = shell.CreateSubKey("command");
			commandKey.SetValue("", command);
		}

		private static void RegisterEditVerb(string extension, string command, string exe, RegistryKey root)
		{
			using var shell = root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{extension}\shell\KeyviewEdit");
			shell.SetValue("", "Edit with Keyview");
			shell.SetValue("Icon", $"\"{exe}\",0");

			using var commandKey = shell.CreateSubKey("command");
			commandKey.SetValue("", command);
		}

		private static void UnregisterShellIntegration(RegistryKey root)
		{
			root.DeleteSubKeyTree(@"Software\Classes\.cks", false);
			root.DeleteSubKeyTree(@"Software\Classes\.ks", false);
			root.DeleteSubKeyTree(@"Software\Classes\Keysharp", false);
			root.DeleteSubKeyTree(@"Software\Classes\Keysharp.CompiledScript", false);
			root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ahk\shell\KeysharpCompile", false);
			root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ks\shell\KeysharpCompile", false);
			root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ahk\shell\KeyviewEdit", false);
			root.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\.ks\shell\KeyviewEdit", false);
			// Keyview "Open with" registration. The .ks OpenWithList entry is removed with the .ks tree above; the
			// .ahk entry and the shared Applications\Keyview.exe registration must be removed explicitly (.ahk keeps
			// its own default ProgID, so we never delete the whole .ahk key).
			root.DeleteSubKeyTree(@"Software\Classes\Applications\Keyview.exe", false);
			root.DeleteSubKeyTree(@"Software\Classes\.ahk\OpenWithList\Keyview.exe", false);
		}

#endif
	}
}
