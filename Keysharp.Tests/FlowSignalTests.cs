#if !WINDOWS
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class FlowTests
	{
		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		private static extern int SendSignal(int pid, int signal);

		[Test, Category("Flow")]
		public void FlowPosixTerminationSignals()
		{
			var launcher = Path.Combine(AppContext.BaseDirectory, "Keysharp");

			if (!File.Exists(launcher))
				Assert.Ignore($"launcher not built at {launcher}");

			var directory = Path.Combine(Path.GetTempPath(), "keysharp-signals-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var scriptPath = Path.Combine(directory, "signal.ahk");
			var readyPath = Path.Combine(directory, "ready");
			var exitsPath = Path.Combine(directory, "exits");
			var vetoPath = Path.Combine(directory, "veto-complete");
			File.WriteAllText(scriptPath, """
				#ErrorStdOut
				#Warn All, StdOut
				#NoTrayIcon
				exitCount := 0
				OnExit(Exiting)
				FileAppend('ready', A_ScriptDir '/ready')
				Exiting(reason, code) {
				    global exitCount
				    exitCount += 1
				    FileAppend(reason ' ' exitCount ';', A_ScriptDir '/exits')
				    if exitCount = 1
				        SetTimer(() => FileAppend('ready', A_ScriptDir '/veto-complete'), -1)
				    return exitCount = 1
				}
				Loop {
				    Sleep(20)
				}
				""");

			using var process = Process.Start(new ProcessStartInfo(launcher)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				ArgumentList = { scriptPath },
			});

			try
			{
				Assert.IsTrue(SpinWait.SpinUntil(() => File.Exists(readyPath) || process.HasExited, 30000), "script did not start");
				Assert.IsTrue(File.Exists(readyPath), process.HasExited ? $"script exited before it was ready: {process.StandardError.ReadToEnd()}" : "script did not become ready");
				Assert.AreEqual(0, SendSignal(process.Id, 15), "first SIGTERM failed");
				Assert.IsTrue(SpinWait.SpinUntil(() => File.Exists(vetoPath), 10000), "OnExit veto did not complete after SIGTERM");
				Assert.IsFalse(process.HasExited, "OnExit veto was ignored");
				Assert.AreEqual(0, SendSignal(process.Id, 15), "second SIGTERM failed");
				Assert.IsTrue(process.WaitForExit(20000), "script did not exit after the second SIGTERM");
				Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
				Assert.AreEqual("Close 1;Close 2;", File.ReadAllText(exitsPath));
			}
			finally
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
					process.WaitForExit(5000);
				}

				Directory.Delete(directory, true);
			}
		}
	}
}
#endif
