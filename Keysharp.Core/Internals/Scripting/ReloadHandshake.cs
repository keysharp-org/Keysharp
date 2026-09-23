using Keysharp.Builtins;

namespace Keysharp.Internals.Scripting
{
	internal static class ReloadHandshake
	{
		internal const string PredecessorVar = "KEYSHARP_RELOAD_PREDECESSOR";
		private const int WaitMs = 2000;
#if WINDOWS
		private const string PredecessorWindowVar = "KEYSHARP_RELOAD_PREDECESSOR_WINDOW";
		internal const nint ExitByReload = 0x4B53_0001;
#else
		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		private static extern int PosixKill(int pid, int signal);

		[DllImport("libc", EntryPoint = "getppid")]
		private static extern int PosixParentId();
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

		internal static bool AskPredecessorToExit()
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
				if (!long.TryParse(windowValue, out var rawWindow) || rawWindow == 0)
					return false;

				var window = unchecked((nint)rawWindow);
				_ = WindowsAPI.GetWindowThreadProcessId(window, out var ownerPid);
				var asked = ownerPid == (uint)pid
						&& WindowsAPI.PostMessage(window, WindowsAPI.WM_CLOSE, ExitByReload, 0);
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

					_ = Diagnostics.Debug.WriteLine("Keysharp: could not ask the instance being reloaded to exit.");
					return false;
				}

#if WINDOWS
				while (!predecessor.WaitForExit(WaitMs))
#else
				while (!SpinWait.SpinUntil(() => PosixParentId() != pid, WaitMs))
#endif
				{
					if (Script.IsHeadless)
						return false;

#if !WINDOWS
					_ = Script.EnsureEtoApplication();
#endif
					if (Dialogs.MsgBox("Could not close the previous instance of this script.  Keep waiting?", "", "YesNo") != "Yes")
						return false;
				}
			}

			return true;
		}
	}
}
