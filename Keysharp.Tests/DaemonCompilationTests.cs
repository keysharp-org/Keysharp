namespace Keysharp.Tests;

public class DaemonCompilationTests
{
	[Test, Category("Misc"), Category("Curated")]
	public void ExistingDaemonAndLocalFallback()
	{
#if WINDOWS
		if (CompileDaemonSecurity.IsElevated)
			Assert.Ignore("The integration fixture owns its server process; elevation uses a detached unelevated daemon.");
#endif
		var launcherName = OperatingSystem.IsWindows() ? "Keysharp.exe" : "Keysharp";
		if (!File.Exists(Path.Combine(AppContext.BaseDirectory, launcherName)))
			Assert.Ignore("Keysharp launcher is not built.");
		var tempRoot = Path.GetFullPath(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp");
		var directory = Path.Combine(tempRoot, "keysharp-daemon-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		Process daemon = null;
		var completed = false;
		try
		{
			var installation = Path.Combine(directory, "host");
			CopyInstallation(installation);
			Directory.CreateDirectory(Path.Combine(directory, "runtime"));
			var launcher = Path.Combine(installation, launcherName);
			var parent = Path.Combine(directory, "parent.ahk");
			File.WriteAllText(parent, ParentSource);
			File.WriteAllText(Path.Combine(directory, "injected.ahk"), "included := 'include-ok'\n");
			File.WriteAllText(Path.Combine(directory, "file-child.ahk"), Directives + "#Include %A_ScriptName%.inc\nFileAppend(fileValue, '*')\n");
			File.WriteAllText(Path.Combine(directory, "file-child.ahk.inc"), "fileValue := 'file-ok'\n");
			File.WriteAllText(Path.Combine(directory, "compile-file.ahk"), Directives + "#Include %A_ScriptName%.inc\nvalue := fileValue\n");
			File.WriteAllText(Path.Combine(directory, "compile-file.ahk.inc"), "fileValue := 'compile-file-ok'\n");
			var compile = Run(launcher, directory, "0", true, "--errorstdout", "--compile", "asm", "--without-compiler", parent);
			Assert.That(compile.ExitCode, Is.Zero, compile.Error);
			var assembly = Path.ChangeExtension(parent, ".cks");
			Assert.That(File.Exists(assembly), Is.True, compile.Output + compile.Error);

			// Hosting the daemon with dotnet exercises payload lookup independently of the host executable's directory.
			daemon = Process.Start(StartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", directory, "1", false,
				Path.Combine(installation, "Keysharp.dll"), "--daemon"));
			_ = daemon.StandardOutput.ReadToEndAsync();
			var daemonError = daemon.StandardError.ReadToEndAsync();
			var watch = Stopwatch.StartNew();
			var ready = Run(launcher, directory, "1", false, "--daemon", "ping", Path.Combine(directory, "compile-file.ahk"));
			while (ready.ExitCode != 0 && !daemon.HasExited && watch.Elapsed < TimeSpan.FromSeconds(30))
			{
				Thread.Sleep(100);
				ready = Run(launcher, directory, "1", false, "--daemon", "ping", Path.Combine(directory, "compile-file.ahk"));
			}
			Assert.That(ready.ExitCode, Is.Zero, ready.Output + ready.Error + (daemonError.IsCompletedSuccessfully ? daemonError.Result : ""));
			Assert.That(daemon.HasExited, Is.False, "The isolated daemon exited during startup.");

			void CheckParent(string enabled, bool expectLocal)
			{
				var result = Run(launcher, directory, enabled, expectLocal, "--errorstdout", "--assembly", assembly);
				var daemonDiagnostics = "";
				if (result.ExitCode != 0)
				{
					try
					{
						Stop(daemon);
						daemonDiagnostics = daemonError.Wait(5000) ? "\n" + daemonError.GetAwaiter().GetResult()
							: "\nDaemon diagnostics did not close.";
					}
					catch (Exception ex)
					{
						daemonDiagnostics = "\nDaemon diagnostics unavailable: " + ex.GetBaseException().Message;
					}
				}
				Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error + daemonDiagnostics);
				Assert.That(result.Output.Trim(), Is.EqualTo("pass"), result.Error);
			}
			CheckParent("1", false);
			CheckParent("0", true);
			Stop(daemon);
			CheckParent("1", true);
			completed = true;
		}
		finally
		{
			try
			{
				if (daemon != null)
				{
					Stop(daemon);
					daemon.Dispose();
				}
				Assert.That(Path.GetFullPath(directory).StartsWith(tempRoot, StringComparison.Ordinal), Is.True);
				Directory.Delete(directory, true);
			}
			catch (Exception ex) when (!completed)
			{
				TestContext.Out.WriteLine("Daemon fixture cleanup failed: " + ex);
			}
		}
	}

	private static void CopyInstallation(string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory))
		{
			var name = Path.GetFileName(source);
			if ((name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| name is "Keysharp.exe" or "Keysharp" or "Keysharp.deps.json" or "Keysharp.runtimeconfig.json")
				&& !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
				&& !name.StartsWith("Keysharp.Components.Scripting.Compiler", StringComparison.OrdinalIgnoreCase)
				&& !name.StartsWith("Keysharp.Components.Scripting.Parser", StringComparison.OrdinalIgnoreCase))
				File.Copy(source, Path.Combine(destination, name));
		}
		var scripting = Path.Combine(AppContext.BaseDirectory, "components", "scripting");
		foreach (var source in Directory.EnumerateFiles(scripting, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(destination, "components", "scripting", Path.GetRelativePath(scripting, source));
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			File.Copy(source, target);
		}
		var native = CompilerHelper.ResolveAppNativeDependencyPath(AppContext.BaseDirectory, CompilerHelper.requiredNativeDependencies.Single());
		if (File.Exists(native))
			File.Copy(native, Path.Combine(destination, Path.GetFileName(native)), true);
		// Fingerprinting includes extra files, while the component registry loads only its descriptor's listed payload.
		File.WriteAllText(Path.Combine(destination, "components", "scripting", "compiler", Guid.NewGuid().ToString("N") + ".test"), "isolated");
	}

	private static ProcessStartInfo StartInfo(string launcher, string directory, string daemonEnabled, bool expectLocal, params string[] arguments)
	{
		var start = new ProcessStartInfo(launcher)
		{
			WorkingDirectory = directory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		start.Environment["KEYSHARP_DAEMON"] = daemonEnabled;
		start.Environment["KEYSHARP_FORCE_HEADLESS"] = "1";
		start.Environment["KEYSHARP_TEST_EXPECT_LOCAL"] = expectLocal ? "1" : "0";
		start.Environment["XDG_RUNTIME_DIR"] = Path.Combine(directory, "runtime");
		foreach (var argument in arguments)
			start.ArgumentList.Add(argument);
		return start;
	}

	private static (int ExitCode, string Output, string Error) Run(string launcher, string directory, string daemonEnabled,
		bool expectLocal, params string[] arguments)
	{
		using var process = Process.Start(StartInfo(launcher, directory, daemonEnabled, expectLocal, arguments));
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();
		try
		{
			Assert.That(process.WaitForExit(60000), Is.True, "Keysharp process did not exit: " + string.Join(" ", arguments));
			Assert.That(Task.WaitAll([output, error], 5000), Is.True, "Keysharp output streams did not close");
			return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
		}
		finally { Stop(process); }
	}

	private static void Stop(Process process)
	{
		if (!process.HasExited)
		{
			process.Kill(entireProcessTree: true);
			Assert.That(process.WaitForExit(5000), Is.True, "Owned daemon or client did not stop.");
		}
	}

	private const string Directives = "#NoTrayIcon\n#ErrorStdOut\n#Warn All, StdOut\n";

	private const string ParentSource = """
		#NoTrayIcon
		#ErrorStdOut
		#Warn All, StdOut
		#Import Ks { RunScript, CompileScript }
		#CSharp
		public static string CompilationAssemblies() => string.Join(",", System.Linq.Enumerable.Select(
			System.Linq.Enumerable.Where(System.AppDomain.CurrentDomain.GetAssemblies(), a =>
				a.GetName().Name == "Keysharp.Components.Scripting.Parser" ||
				a.GetName().Name == "Keysharp.Components.Scripting.Compiler" ||
				a.GetName().Name.StartsWith("Microsoft.CodeAnalysis")), a => a.GetName().Name));
		#EndCSharp

		Check(condition, message) {
			if !condition
				throw Error(message)
		}
		HasMessage(messages, text) {
			for message in messages
				if InStr(message, text)
					return true
			return false
		}
		CheckComponents(expectLocal, request) {
			loaded := CompilationAssemblies()
			if expectLocal {
				Check(InStr(loaded, "Keysharp.Components.Scripting.Compiler") && InStr(loaded, "Keysharp.Components.Scripting.Parser") && InStr(loaded, "Microsoft.CodeAnalysis"), request " did not load local compilation components: " loaded)
			} else {
				Check(loaded == "", request " loaded local compilation components: " loaded)
			}
		}

		Check(CompilationAssemblies() == "", "caller loaded compilation components before the nested compile")
		expectLocal := EnvGet("KEYSHARP_TEST_EXPECT_LOCAL") == "1"
		headless := "#NoTrayIcon`n#ErrorStdOut`n#Warn All, StdOut`n"
		child := headless '
		(
			#if DAEMON_TEST
				stdin := FileOpen("*", "r")
				FileAppend(A_ScriptName "|" A_WorkingDir "|" included "|" stdin.ReadLine(), "*")
				FileAppend("stderr-ok", "**")
			#else
				#Error missing compile definition
			#endif
		)'
		info := RunScript(child, Async: true, Name: "ChosenChild", Options: ["--define:DAEMON_TEST", "--include", "injected.ahk"])
		try {
			info.StdIn.WriteLine("stdin-ok")
			info.StdIn.Close()
			deadline := A_TickCount + 20000
			while !info.HasExited && A_TickCount < deadline
				Sleep(10)
			if !info.HasExited {
				info.Kill()
				throw Error("RunScript child did not exit")
			}
			childError := info.StdErr.Read()
			Check(info.ExitCode == 0, "RunScript child failed: " childError)
			Check(info.StdOut.Read() == "ChosenChild|" A_WorkingDir "|include-ok|stdin-ok", "RunScript name, options, working directory or stdin differed")
			Check(childError == "stderr-ok", "RunScript stderr differed")
		} finally {
			if !info.HasExited
				info.Kill()
			info.Close()
		}
		CheckComponents(expectLocal, "RunScript source")

		fileChild := RunScript("file-child.ahk", Name: "IgnoredBuildName")
		Check(fileChild.ExitCode == 0, "file RunScript failed: " fileChild.StdErr.Read())
		Check(fileChild.StdOut.Read() == "file-ok", "file RunScript relative include differed")
		fileChild.Close()
		CheckComponents(expectLocal, "RunScript file")

		result := CompileScript(headless "#Warning compile-warning`nx := 1`n")
		Check(result.IsValid && result.Errors.Length == 0, "valid CompileScript failed")
		Check(HasMessage(result.Warnings, "compile-warning"), "successful compile lost its warning")
		CheckComponents(expectLocal, "CompileScript source")
		result := CompileScript("compile-file.ahk")
		Check(result.IsValid && result.Errors.Length == 0, "file CompileScript identity or relative include differed")
		CheckComponents(expectLocal, "CompileScript file")
		result := CompileScript(headless "x := (`n")
		Check(!result.IsValid && result.Errors.Length > 0, "CompileScript accepted invalid syntax")
		CheckComponents(expectLocal, "CompileScript syntax error")
		result := CompileScript(headless "#Warning failed-compile-warning`n#CSharp`npublic static object Bad() => new MissingDaemonType();`n#EndCSharp`nBad()`n")
		Check(!result.IsValid && HasMessage(result.Errors, "MissingDaemonType"), "CompileScript lost C# errors")
		Check(HasMessage(result.Warnings, "failed-compile-warning"), "failed compile lost its warning")

		CheckComponents(expectLocal, "CompileScript C# error")
		FileAppend("pass", "*")
		""";
}
