#if WINDOWS
namespace Keysharp.Internals.Scripting
{
	// Windows instance replacement retains AHK's window-title discovery and native close messages.
	internal sealed partial class ScriptEndpoint
	{
		/// <summary>Marks a script's main window, which tells it apart from other windows of the same title.</summary>
		internal const string MainWindowProperty = "Keysharp.Script";

		/// <summary>The main window of another instance whose title is <paramref name="title"/>, or 0: AHK's FindWindow, so
		/// another version's instance is not found.</summary>
		internal static nint FindInstance(string title)
		{
			for (nint hwnd = 0; (hwnd = WindowsAPI.FindWindowEx(0, hwnd, null, title)) != 0;)
				if (WindowsAPI.GetProp(hwnd, MainWindowProperty) != 0 && ProcessIdOf(hwnd) != Environment.ProcessId)
					return hwnd;

			return 0;
		}

		internal static int ProcessIdOf(nint hwnd)
		{
			_ = WindowsAPI.GetWindowThreadProcessId(hwnd, out var pid);
			return (int)pid;
		}

		/// <summary>The main window of process <paramref name="pid"/>, or 0.</summary>
		internal static nint MainWindowOf(long pid)
		{
			nint found = 0;
			_ = WindowsAPI.EnumWindows((hwnd, _) =>
			{
				if (WindowsAPI.GetProp(hwnd, MainWindowProperty) == 0 || ProcessIdOf(hwnd) != pid)
					return true;

				found = hwnd;
				return false;
			}, 0);
			return found;
		}

		/// <summary>Asks a script's main window to exit, as AHK does: WM_CLOSE, with Reload and SingleInstance told apart
		/// from a plain close by AHK's codes in wParam.</summary>
		internal static bool PostClose(nint hwnd, long pid, Keysharp.Builtins.Flow.ExitReasons reason, int reloadToken)
			=> IsMainWindowOf(hwnd, pid) && WindowsAPI.PostMessage(hwnd, WindowsAPI.WM_CLOSE, (nint)(reason switch
			{
				Keysharp.Builtins.Flow.ExitReasons.Reload => (uint)UserMessages.AHK_EXIT_BY_RELOAD,
				Keysharp.Builtins.Flow.ExitReasons.Single => (uint)UserMessages.AHK_EXIT_BY_SINGLEINSTANCE,
				_ => 0u
			}), (nint)reloadToken);

		private static bool IsMainWindowOf(nint hwnd, long pid)
			=> hwnd != 0 && WindowsAPI.GetProp(hwnd, MainWindowProperty) != 0 && ProcessIdOf(hwnd) == pid;

		/// <summary>The exit reason a WM_CLOSE to a script's main window carries, as AHK reads it.</summary>
		internal static Keysharp.Builtins.Flow.ExitReasons CloseReason(nint wParam) => (uint)wParam switch
		{
			(uint)UserMessages.AHK_EXIT_BY_RELOAD => Keysharp.Builtins.Flow.ExitReasons.Reload,
			(uint)UserMessages.AHK_EXIT_BY_SINGLEINSTANCE => Keysharp.Builtins.Flow.ExitReasons.Single,
			_ => Keysharp.Builtins.Flow.ExitReasons.Close
		};
	}
}
#endif
