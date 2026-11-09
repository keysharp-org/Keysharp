using Keysharp.Builtins;
using ExitReasons = Keysharp.Builtins.Flow.ExitReasons;

namespace Keysharp.Runtime
{
	public partial class Script
	{
		private const int RetireWaitMs = 2000;
		private const int NonInteractiveRetireWaitMs = 15000;

		/// <summary>
		/// The main window's title, as AHK's: the script's full path followed by the version, or a compiled script's path
		/// alone. On Windows another instance of the script finds this one by it, as AHK's does.
		/// </summary>
		internal static string MainWindowTitle => Accessors.A_IsCompiled ? TheScript.scriptPath
			: TheScript.scriptPath + " - Keysharp v" + typeof(Script).Assembly.GetName().Version;

		public static bool HandleSingleInstance(string title, eScriptInstance inst) => HandleSingleInstance(title, inst, false);

		public static bool HandleSingleInstance(string title, eScriptInstance inst, bool errorStdOut)
		{
			// "*" names source read from stdin or compiled from text, which has no file to be an instance of. The test host
			// runs every script in one process.
			if (title.Length == 0 || title == "*" || IsUiInitializationBlocked || IsTestHost)
				return false;

			// A replacement launched by Reload retires the instance it replaces through the handshake, so the
			// single-instance policy must not act on it as well: Prompt would ask about that very instance.
			if (Environment.GetEnvironmentVariable(ReloadHandshake.HandshakeVar) != null)
				return false;

			if (Env.FindCommandLineArg("force") != null || Env.FindCommandLineArg("f") != null)
				inst = eScriptInstance.Off;

			if (Env.FindCommandLineArg("restart") != null || Env.FindCommandLineArg("r") != null)
				inst = eScriptInstance.Force;

			// Nothing can answer a prompt without a desktop.
			if (inst == eScriptInstance.Off || (inst == eScriptInstance.Prompt && IsHeadless))
				return false;

#if WINDOWS
			// As AHK finds an instance: by the main window titled with the same path and version.
			var hwnd = ScriptEndpoint.FindInstance(MainWindowTitle);
			var pid = ScriptEndpoint.ProcessIdOf(hwnd);
			var processStartTicks = ScriptEndpoint.ProcessStartTicks(pid);

			if (hwnd == 0)
			{
				if (ScriptEndpoint.FindOtherInstance(TheScript.scriptPath) is not { } peer)
					return false;

				pid = peer.Pid;
				processStartTicks = peer.ProcessStartTicks;
			}
#else
			if (ScriptEndpoint.FindOtherInstance(TheScript.scriptPath) is not { } peer)
				return false;

			var (pid, hwnd) = (peer.Pid, (nint)0);
			var processStartTicks = peer.ProcessStartTicks;
#endif

			// Ignore keeps the old instance, Force replaces it, and Prompt asks which.
			if (inst == eScriptInstance.Ignore || (inst != eScriptInstance.Force && !ConfirmReplace(title)))
				return true;

			// As AHK, which replaces the one instance its window search finds, and gives up running if it does not exit.
			var interactive = !IsHeadless && !errorStdOut && Env.FindCommandLineArg("errorstdout") == null;

			if (RetireAndWait(pid, hwnd, ExitReasons.Single, interactive, processStartTicks))
				return false;

			Environment.ExitCode = 2;
			return true;

			// AutoHotkey's text, and its title, the script's file name.
			static bool ConfirmReplace(string title)
			{
				const string question = "An older instance of this script is already running.  Replace it with this instance?\nNote: To avoid this message, see #SingleInstance in the help file.";
#if WINDOWS
				// No message loop has activated the visual-styles context yet, and MessageBox.Show activates it itself.
				return MessageBox.Show(question, title, MessageBoxButtons.YesNo) == DialogResult.Yes;
#else
				// MsgBox needs Application.Instance, which the rest of the UI has not created yet.
				_ = EnsureEtoApplication();
				return Dialogs.MsgBox(question, title, "YesNo") == "Yes";
#endif
			}
		}

		// ---- retiring an instance --------------------------------------------------------------------------

		/// <summary>
		/// Asks process <paramref name="pid"/> to exit for <paramref name="reason"/> and waits until it is gone, as AHK waits
		/// for the instance it closes before running the new one. When that takes longer, an interactive run asks whether to
		/// keep waiting, with AHK's question, and any other gives up.
		/// </summary>
		/// <param name="hwnd">The script's main window, which is asked on Windows, or 0 to ask its socket.</param>
		/// <returns>False when the request was refused or the wait abandoned.</returns>
		internal static bool RetireAndWait(int pid, nint hwnd, ExitReasons reason, bool interactive,
			long expectedProcessStartTicks, int reloadToken = 0)
		{
			var role = reason == ExitReasons.Reload ? "instance being reloaded" : "previous instance";
			Process process;

			try
			{
				process = Process.GetProcessById(pid);
			}
			catch (ArgumentException)
			{
				return true;//Already gone.
			}

			using (process)
			{
				if (expectedProcessStartTicks == 0)
				{
					Console.Error.WriteLine($"Keysharp: could not identify the {role} (PID {pid}).");
					return false;
				}

#if WINDOWS
				try
				{
					// The retained handle pins the PID; inspect that handle before sending a window close.
					_ = process.SafeHandle;

					if (process.StartTime.ToUniversalTime().Ticks != expectedProcessStartTicks)
						return true;
				}
				catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					if (ProcessHasExited(process, pid, expectedProcessStartTicks))
						return true;

					Console.Error.WriteLine($"Keysharp: could not wait for the {role} (PID {pid}): {ex.Message}");
					return false;
				}
				var accepted = hwnd != 0 && ScriptEndpoint.PostClose(hwnd, pid, reason, reloadToken);

				if (hwnd == 0)
#else
				var accepted = false;
#endif
				{
					using var timeout = new CancellationTokenSource(ScriptEndpoint.QueryTimeoutMs);

					try
					{
						accepted = ScriptEndpoint.RequestAsync(pid, expectedProcessStartTicks, ScriptEndpoint.ExitRequest(reason, expectedProcessStartTicks, reloadToken), timeout.Token)
							.GetAwaiter().GetResult() is [ScriptEndpoint.Accepted];
					}
					catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
					{
					}
				}

				if (!accepted)
				{
					if (ProcessHasExited(process, pid, expectedProcessStartTicks))
						return true;

					Console.Error.WriteLine($"Keysharp: could not ask the {role} (PID {pid}) to exit.");
					return false;
				}

				var waitMs = interactive ? RetireWaitMs : NonInteractiveRetireWaitMs;

#if WINDOWS
				while (!process.WaitForExit(waitMs))
#else
				while (!WaitForExit(process, pid, waitMs, expectedProcessStartTicks))
#endif
				{
					if (!interactive)
					{
						Console.Error.WriteLine($"Keysharp: the {role} (PID {pid}) did not exit within {waitMs} ms.");
						return false;
					}

#if !WINDOWS
					_ = Script.EnsureEtoApplication();
#endif
					if (Dialogs.MsgBox("Could not close the previous instance of this script.  Keep waiting?", null, "YesNo") != "Yes")
						return false;
				}

				return true;
			}
		}

		private static bool ProcessHasExited(Process process, int pid, long expectedProcessStartTicks)
		{
#if !WINDOWS
			var current = ScriptEndpoint.ProcessStartTicks(pid);

			if (current != 0 && current != expectedProcessStartTicks)
				return true;
#endif
			try
			{
				return process.HasExited;
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
			{
				return false;
			}
		}

#if !WINDOWS
		[DllImport("libc", EntryPoint = "getppid")]
		private static extern int PosixParentId();

		private static bool WaitForExit(Process process, int pid, int timeoutMs, long expectedProcessStartTicks)
		{
			var deadline = Environment.TickCount64 + timeoutMs;
			// A Reload replacement waits for its parent, whose exit getppid reports even if the PID is reused.
			var isParent = pid == PosixParentId();

			while (isParent ? PosixParentId() == pid : !ProcessHasExited(process, pid, expectedProcessStartTicks))
			{
				if (Environment.TickCount64 >= deadline)
					return false;

				Thread.Sleep(15);
			}

			return true;
		}
#endif
	}
}
