using static Keysharp.Builtins.Processes;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ProcessTests : TestRunner
	{
		[Test, Category("Process")]
		public void ProcessRunWaitClose()
		{
			VarRef pid = new(null);
#if WINDOWS
			_ = Run("cmd.exe", "", "max", pid);
			_ = ProcessWait(pid.__Value);
			_ = ProcessSetPriority("H", pid.__Value);

			if (ProcessExist(pid.__Value) != 0)
			{
				Thread.Sleep(2000);
				_ = ProcessClose(pid.__Value);
				_ = ProcessWaitClose(pid.__Value);
			}

			Thread.Sleep(1000);
			pid.__Value = ProcessExist("cmd.exe");
			Assert.AreEqual(0L, pid.__Value);
			_ = RunWait("cmd.exe", "", "max");
			Thread.Sleep(1000);
			Assert.AreEqual(0L, ProcessExist("cmd.exe"));
			//Admin tools.
			Run("shell:::{D20EA4E1-3957-11D2-A40B-0C5020524153}");
			//This PC.
			Run("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
			//edit verb.
			Run("edit \"..\\..\\..\\Keysharp.Tests\\Code\\testini.ini\"");
			//explore verb.
			Run("explore " + Accessors.A_ProgramFiles.ToString());
			//find verb.
			if (Directory.Exists("D:\\"))
				Run("find D:\\");
			else if (Directory.Exists("C:\\"))
				Run("find C:\\");
			else
				throw new Exception("Cannot test find verb");
			//Open file with default.
			Run("..\\..\\..\\Keysharp.Tests\\Code\\test-text-file.txt");
			//Run program as admin to open file.
			//Relative paths don't work with *runas, so convert to absolute.
			var fullPath = Path.GetFullPath("..\\..\\..\\Keysharp.Tests\\Code\\DirCopy\\file1.txt");
			Run("*runas C:\\Windows\\Notepad.exe \"" + fullPath + "\"");
			//URL.
			Run("https://www.google.com");
			//Create command line.
			Run(Accessors.A_ComSpec);

			//Create command line and run program.
			if (File.Exists("./DirTest.txt"))
				File.Delete("./DirTest.txt");

			RunWait(Keysharp.Runtime.Script.Concat(Accessors.A_ComSpec, " /c dir C:\\ >>./DirTest.txt"), null, "Min");
			MessageBox.Show("Close everything that was opened by the process test before proceeding.");
			Assert.IsTrue(TestScript("process-run-wait-close", false));
			//Can't really test RunAs() or Shutdown(), but they have been manually tested individually.
#else
			_ = Run("/usr/bin/sleep", "", "max", pid, "60");
			_ = ProcessWait(pid.__Value, 2);

			//Skip process priority raising on linux, it can't be raised
			//above normal without being root.
			if (ProcessExist(pid.__Value) != 0)
			{
				_ = ProcessClose(pid.__Value);
				_ = ProcessWaitClose(pid.__Value, 5);
			}

			pid.__Value = ProcessExist(pid.__Value);
			Assert.AreEqual(0L, pid.__Value);
			Assert.AreEqual(0L, RunWait("/usr/bin/true", "", "max"));
			Assert.IsTrue(TestScript("process-run-wait-close", false));
#endif
		}

		[Test, Category("Process")]
		public void ProcessRunScript()
		{
			Assert.IsTrue(TestScript("process-runscript", false));
		}

#if !WINDOWS
		//Off Windows only: ShellExecute accepts a quoted file name, so exec is the only launcher a stray
		//quote can break.
		[Test, Category("Process")]
		public void RunQuotedTarget()
		{
			//Quoting the program is how a path with spaces can still carry arguments.
			var dir = Path.Combine(Path.GetTempPath(), $"keysharp run {Environment.ProcessId}");
			_ = Directory.CreateDirectory(dir);

			try
			{
				//A link rather than a script, so the executable bit comes from the target and the test needs
				//no platform-specific call to set it.
				var exe = Path.Combine(dir, "spaced shell");
				_ = File.CreateSymbolicLink(exe, "/bin/sh");
				Assert.AreEqual(7L, RunWait($"\"{exe}\" -c \"exit 7\""));//Arguments parsed out of the target.
				Assert.AreEqual(7L, RunWait($"\"{exe}\"", args: "-c \"exit 7\""));//Arguments passed separately.
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}
#endif
	}
}
