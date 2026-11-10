#if OSX
using System.Xml.Linq;

namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Gui"), Category("Internal")]
public class MacFinderTests
{
	[Test]
	public void FinderReload()
	{
		using var fixture = new FinderFixture(true);
		fixture.Open(true);
		var previous = fixture.WaitFor("ready");
		File.WriteAllText(Path.Combine(fixture.Directory, "reload"), "");
		var replacement = fixture.WaitFor("replacement");
		AssertReplacement(fixture, previous, replacement, "Reload");
	}

	[Test]
	public void FinderSingleInstanceForce()
	{
		using var fixture = new FinderFixture(false);
		fixture.Open(true);
		var previous = fixture.WaitFor("ready");
		fixture.Open(false);
		var replacement = fixture.WaitFor("replacement");
		AssertReplacement(fixture, previous, replacement, "Single");
	}

	private static void AssertReplacement(FinderFixture fixture, string[] previous, string[] replacement, string reason)
	{
		Assert.That(replacement[1], Is.Not.EqualTo(previous[1]));
		Assert.That(previous[2], Is.EqualTo(fixture.ScriptPath));
		Assert.That(replacement[2], Is.EqualTo(fixture.ScriptPath));
		Assert.That(previous[3], Is.EqualTo("0"), "Finder opens the document without script arguments.");
		Assert.That(replacement[3], Is.EqualTo(previous[3]), "The replacement must preserve script arguments.");
		Assert.That(replacement[5], Is.EqualTo(previous[5]), "The script must start in its own directory.");
		if (reason == "Reload")
			Assert.That(replacement[4], Is.EqualTo(previous[4]), "Reload must preserve the initial working directory.");
		Assert.That(replacement[6], Is.EqualTo("0"), "The replacement must run after its predecessor exits.");
		Assert.That(fixture.Lines, Does.Contain($"exit|{previous[1]}|{reason}"));
		fixture.WaitForExit(int.Parse(replacement[1], CultureInfo.InvariantCulture));
		Assert.That(File.ReadAllText(fixture.ErrorPath), Is.Empty);
	}

	private sealed class FinderFixture : IDisposable
	{
		internal string Directory { get; } = Path.Combine("/private/tmp", "keysharp-finder-" + Guid.NewGuid().ToString("N"));
		internal string ScriptPath => Path.Combine(Directory, "probe.ks");
		internal string ErrorPath => Path.Combine(Directory, "stderr");
		private string BundlePath => Path.Combine(Directory, "Keysharp.app");
		private string LogPath => Path.Combine(Directory, "events");
		internal string[] Lines => File.Exists(LogPath) ? File.ReadAllLines(LogPath) : [];

		internal FinderFixture(bool reload)
		{
			var source = Path.Combine(AppContext.BaseDirectory, "Keysharp.app");
			if (!File.Exists(Path.Combine(source, "Contents", "MacOS", "Keysharp")))
				Assert.Ignore($"Keysharp.app is not built at {source}.");

			System.IO.Directory.CreateDirectory(Directory);
			try
			{
				// A separate bundle identity keeps LaunchServices from selecting an installed Keysharp.
				RunNative("/usr/bin/ditto", source, BundlePath);
				var plistPath = Path.Combine(BundlePath, "Contents", "Info.plist");
				var plist = XDocument.Load(plistPath);
				var dictionary = plist.Root.Element("dict");
				dictionary.Elements("key").Single(key => key.Value == "CFBundleIdentifier")
					.ElementsAfterSelf().First().Value = "org.keysharp.tests.finder." + Guid.NewGuid().ToString("N");
				foreach (var key in dictionary.Descendants("key").Where(key => key.Value == "LSHandlerRank"))
					key.ElementsAfterSelf().First().Value = "None";
				if (dictionary.Elements("key").FirstOrDefault(key => key.Value == "UTExportedTypeDeclarations") is { } exported)
				{
					exported.ElementsAfterSelf().First().Remove();
					exported.Remove();
				}
				plist.Save(plistPath);
				var signature = Path.Combine(BundlePath, "Contents", "_CodeSignature");
				if (System.IO.Directory.Exists(signature))
					System.IO.Directory.Delete(signature, true);
				File.WriteAllText(ErrorPath, "");
				File.WriteAllText(ScriptPath, Source(reload));
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		internal void Open(bool newInstance)
		{
			var arguments = new List<string> { "-g", "-a", BundlePath, "--env", "KEYSHARP_DAEMON=0",
				"--env", "KEYSHARP_FORCE_HEADLESS=0", "--stdout", Path.Combine(Directory, "stdout"), "--stderr", ErrorPath };
			if (newInstance)
				arguments.Add("-n");
			arguments.Add(ScriptPath);
			RunNative("/usr/bin/open", arguments.ToArray());
		}

		internal string[] WaitFor(string kind)
		{
			var watch = Stopwatch.StartNew();
			while (watch.Elapsed < TimeSpan.FromSeconds(20))
			{
				if (Lines.FirstOrDefault(line => line.StartsWith(kind + "|", StringComparison.Ordinal)) is { } line)
					return line.Split('|');
				Thread.Sleep(25);
			}
			throw new AssertionException($"Finder never logged {kind}.\n{string.Join('\n', Lines)}\n{File.ReadAllText(ErrorPath)}");
		}

		internal void WaitForExit(int pid)
		{
			var watch = Stopwatch.StartNew();
			while (watch.Elapsed < TimeSpan.FromSeconds(5))
			{
				try
				{
					using var process = Process.GetProcessById(pid);
					if (process.HasExited)
						return;
				}
				catch (ArgumentException) { return; }
				Thread.Sleep(25);
			}
			Assert.Fail($"Finder script PID {pid} did not exit.");
		}

		public void Dispose()
		{
			foreach (var process in Process.GetProcessesByName("Keysharp"))
				using (process)
					try
					{
						if (process.MainModule?.FileName.StartsWith(BundlePath + Path.DirectorySeparatorChar, StringComparison.Ordinal) == true)
						{
							process.Kill(entireProcessTree: true);
							_ = process.WaitForExit(5000);
						}
					}
					catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
			if (System.IO.Directory.Exists(Directory))
				System.IO.Directory.Delete(Directory, true);
		}

		private static string Source(bool reload) => $$"""
			#ErrorStdOut
			#Warn All, StdOut
			#NoTrayIcon
			#SingleInstance Force
			OnExit((Reason, Code) => FileAppend("exit|" ProcessExist() "|" Reason "`n", A_ScriptDir "/events"))
			SetTimer(() => ExitApp(88), -20000)
			initialPath := A_ScriptDir "/initial.pid"
			if !FileExist(initialPath) {
				FileAppend(ProcessExist(), initialPath)
				FileAppend("ready|" ProcessExist() "|" A_ScriptFullPath "|" A_Args.Length "|" A_InitialWorkingDir "|" A_WorkingDir "`n", A_ScriptDir "/events")
				Persistent true
				{{(reload ? "SetTimer(CheckReload, 20)" : "")}}
			} else {
				previousPid := Integer(FileRead(initialPath))
				FileAppend("replacement|" ProcessExist() "|" A_ScriptFullPath "|" A_Args.Length "|" A_InitialWorkingDir "|" A_WorkingDir "|" ProcessExist(previousPid) "`n", A_ScriptDir "/events")
				ExitApp()
			}
			CheckReload() {
				if FileExist(A_ScriptDir "/reload") {
					SetTimer(CheckReload, 0)
					Reload()
				}
			}
			""";

		private static void RunNative(string executable, params string[] arguments)
		{
			var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var argument in arguments)
				start.ArgumentList.Add(argument);
			using var process = Process.Start(start);
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(10000))
			{
				process.Kill(entireProcessTree: true);
				Assert.Fail($"{executable} did not finish.");
			}
			Assert.That(process.ExitCode, Is.Zero, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
		}
	}
}
#endif
