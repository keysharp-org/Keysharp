using Keysharp.Builtins;

namespace Keysharp.Internals
{
#if WINDOWS
	internal sealed class WindowsSession : ISession
	{
		public bool ExitProgram(uint flags, uint reason) => Keysharp.Internals.Os.Windows.WindowsAPI.ExitWindowsEx(flags, reason);
	}
#elif LINUX
	/// <summary>
	/// Linux session control. Logoff is per-DE (the desktop-environment flags are detected once at startup);
	/// shutdown/reboot use the standard coreutils commands. Taken from the per-DE logout reference.
	/// </summary>
	internal sealed class LinuxSession : ISession
	{
		public bool ExitProgram(uint flags, uint reason)
		{
			var cmd = "";
			var force = (flags & 4) == 4;   // close all programs

			if (flags == 0)   // Logoff
			{
				if (IsGnome)
					cmd = force ? "gnome-session-quit --force" : "gnome-session-quit";
				else if (IsKde)
					cmd = force ? "qdbus org.kde.ksmserver /KSMServer logout 0 0 2" : "qdbus org.kde.ksmserver /KSMServer logout 1 0 3";
				else if (IsXfce)
					cmd = force ? "xfce4-session-logout --fast" : "xfce4-session-logout";
				else if (IsMate)
					cmd = force ? "mate-session-save --logout --force" : "mate-session-save --logout";
				else if (IsCinnamon)
					cmd = force ? "cinnamon-session-quit --no-prompt" : "cinnamon-session-quit";
				else if (IsLxqt)
				{
					if (force)
						Diagnostics.Debug.WriteLine("LXQT doesn't support forced logouts.");

					cmd = "lxqt-leave";
				}
				else if (IsLxde)
				{
					if (force)
						Diagnostics.Debug.WriteLine("LXDE doesn't support forced logouts.");

					cmd = "lxde-logout";
				}

				if (!string.IsNullOrWhiteSpace(cmd) && cmd.Bash() != 0)
					Diagnostics.Debug.WriteLine($"ExitProgram logoff command failed: {cmd}");
			}
			else if ((flags & 1) == 1)   // Halt/shutdown
			{
				if ((flags & 8) == 8)   // Power down
				{
					if ("shutdown now".Bash() != 0)
						Diagnostics.Debug.WriteLine("ExitProgram shutdown command failed: shutdown now");
				}
				else
				{
					if ("halt".Bash() != 0)
						Diagnostics.Debug.WriteLine("ExitProgram halt command failed: halt");
				}
			}
			else if ((flags & 2) == 2)   // Reboot
			{
				if (force)
				{
					if ("reboot -f".Bash() != 0)
						Diagnostics.Debug.WriteLine("ExitProgram reboot command failed: reboot -f");
				}
				else
				{
					if ("reboot".Bash() != 0)
						Diagnostics.Debug.WriteLine("ExitProgram reboot command failed: reboot");
				}
			}
			else if ((flags & 8) == 8)   // Shutdown
			{
				if ("shutdown now".Bash() != 0)
					Diagnostics.Debug.WriteLine("ExitProgram shutdown command failed: shutdown now");
			}

			return true;
		}
	}
#elif OSX
	internal sealed class MacSession : ISession
	{
		public bool ExitProgram(uint flags, uint reason)
		{
			var action = (flags & 2) == 2
				? "restart"
				: (flags & (1 | 8)) != 0
					? "shut down"
					: "log out";

			// System Events has no equivalent of ExitWindowsEx's force bit. Keep the
			// operation safe and interactive instead of terminating applications.
			if ((flags & 4) == 4)
				Diagnostics.Debug.WriteLine("Shutdown force mode is not available on macOS; requesting a normal session action.");

			return $"tell application \"System Events\" to {action}".AppleScript() == 0;
		}
	}
#endif
}
