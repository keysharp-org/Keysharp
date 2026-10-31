using Keysharp.Builtins;

namespace Keysharp.Internals.Scripting
{
	internal static class ReloadHandshake
	{
		internal const string PredecessorVar = "KEYSHARP_RELOAD_PREDECESSOR";
		private const int WaitMs = 2000;
		private const int NonInteractiveWaitMs = 15000;
#if WINDOWS
		private const string PredecessorWindowVar = "KEYSHARP_RELOAD_PREDECESSOR_WINDOW";
		internal const nint ExitByReload = 0x4B53_0001;
#else
		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		private static extern int PosixKill(int pid, int signal);

		[DllImport("libc", EntryPoint = "getppid")]
		private static extern int PosixParentId();

		// The predecessor is this process's parent, so getppid reports its exit exactly, where a polled PID could already be reused.
		private static bool WaitForPredecessorExit(int pid, int timeoutMs)
		{
			var deadline = Environment.TickCount64 + timeoutMs;

			while (PosixParentId() == pid)
			{
				if (Environment.TickCount64 >= deadline)
					return false;

				Thread.Sleep(15);
			}

			return true;
		}
#endif

		internal static Process Start(Script script)
		{
#if WINDOWS
			if (script.mainWindow == null)
				throw new InvalidOperationException("The running script has no main window to close.");
#endif
			var start = Runner.CreateRestartStartInfo();
			start.WorkingDirectory = Accessors.A_InitialWorkingDir;

			start.Environment[PredecessorVar] = Environment.ProcessId.ToString();
#if WINDOWS
			start.Environment[PredecessorWindowVar] = script.MainWindowHandle.ToInt64().ToString();
#endif
			return Process.Start(start) ?? throw new InvalidOperationException("The replacement process did not start.");
		}

		internal static bool AskPredecessorToExit(bool errorStdOut)
		{
			var value = Environment.GetEnvironmentVariable(PredecessorVar);
			Environment.SetEnvironmentVariable(PredecessorVar, null);
#if WINDOWS
			var windowValue = Environment.GetEnvironmentVariable(PredecessorWindowVar);
			Environment.SetEnvironmentVariable(PredecessorWindowVar, null);
#endif
			if (!int.TryParse(value, out var pid))
				return true;

#if !WINDOWS
			// A live predecessor is still this process's parent; a reused PID must not be signaled.
			if (PosixParentId() != pid)
				return true;
#endif
			Process predecessor;

			try
			{
				predecessor = Process.GetProcessById(pid);
			}
			catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
			{
				return true;//The old instance has already exited.
			}

			using (predecessor)
			{
#if WINDOWS
				var asked = false;

				if (long.TryParse(windowValue, out var rawWindow) && rawWindow != 0)
				{
					var window = unchecked((nint)rawWindow);
					_ = WindowsAPI.GetWindowThreadProcessId(window, out var ownerPid);
					asked = ownerPid == (uint)pid && WindowsAPI.PostMessage(window, WindowsAPI.WM_CLOSE, ExitByReload, 0);
				}
#else
				var asked = PosixKill(pid, 15) == 0;//SIGTERM cannot carry the reason.
#endif
				if (!asked)
				{
#if WINDOWS
					if (predecessor.HasExited)
#else
					if (PosixParentId() != pid)
#endif
						return true;

					Console.Error.WriteLine($"Keysharp: could not ask the instance being reloaded (PID {pid}) to exit.");
					return false;
				}

				// Nobody answers a dialog without a desktop, and a script routing errors to stderr has asked not to be shown one.
				var interactive = !Script.IsHeadless && !errorStdOut && Env.FindCommandLineArg("errorstdout") == null;
				var waitMs = interactive ? WaitMs : NonInteractiveWaitMs;

#if WINDOWS
				while (!predecessor.WaitForExit(waitMs))
#else
				while (!WaitForPredecessorExit(pid, waitMs))
#endif
				{
					if (!interactive)
					{
						Console.Error.WriteLine($"Keysharp: the instance being reloaded (PID {pid}) did not exit within {waitMs} ms.");
						return false;
					}

#if !WINDOWS
					_ = Script.EnsureEtoApplication();
#endif
					if (Dialogs.MsgBox("Could not close the previous instance of this script.  Keep waiting?", null, "YesNo") != "Yes")
						return false;
				}
			}

			return true;
		}
	}
}
