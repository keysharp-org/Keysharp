#if LINUX
using System.ComponentModel;
using System.IO.Pipes;

namespace Keysharp.Internals.Linux
{
	internal static class LinuxTerminalAuthentication
	{
		private const int RegistrationTimeoutMilliseconds = 5000;
		private const int StopTimeoutMilliseconds = 1000;
		private const int O_RDWR = 2, O_NOCTTY = 0x100, O_CLOEXEC = 0x80000;
		private const short POLLIN = 1, POLLHUP = 0x10;
		private const int EINTR = 4, SIGTERM = 15;

		internal static bool HasControllingTerminal
		{
			get
			{
				var terminal = Open("/dev/tty", O_RDWR | O_NOCTTY | O_CLOEXEC);
				if (terminal < 0) return false;
				try
				{
					// A background job must not steal the foreground job's password input.
					return TerminalProcessGroup(terminal) == ProcessGroup();
				}
				finally { _ = Close(terminal); }
			}
		}

		internal static IDisposable Start(out string error)
		{
			error = null;
			if (!HasControllingTerminal) return null;
			var executable = FindAgent();
			if (executable == null)
			{
				error = "Terminal authentication requires pkttyagent from the polkit package.";
				return null;
			}
			return Start(executable, RegistrationTimeoutMilliseconds, out error);
		}

		internal static IDisposable Start(string executable, int registrationTimeoutMilliseconds, out string error)
		{
			error = null;
			Agent agent = null;
			try
			{
				if (!TryParseStartTime(File.ReadAllText("/proc/self/stat"), out var startTime))
				{
					error = "Could not identify this process for terminal authentication.";
					return null;
				}

				using var notification = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
				var options = new ProcessStartInfo(executable) { UseShellExecute = false };
				options.ArgumentList.Add("--process");
				options.ArgumentList.Add($"{Environment.ProcessId},{startTime.ToString(CultureInfo.InvariantCulture)}");
				options.ArgumentList.Add("--fallback");
				options.ArgumentList.Add("--notify-fd");
				options.ArgumentList.Add(notification.GetClientHandleAsString());
				System.Diagnostics.Process process;
				// Once exposed, the inheritable write end is closed only by this call, not by the server's Dispose.
				try { process = System.Diagnostics.Process.Start(options); }
				finally { notification.DisposeLocalCopyOfClientHandle(); }
				if (process == null)
				{
					error = "Could not start the terminal authentication agent.";
					return null;
				}
				agent = new(process);
				if (!WaitForRegistration(notification, process, registrationTimeoutMilliseconds, out error))
				{
					agent.Dispose();
					return null;
				}
				return agent;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
			{
				agent?.Dispose();
				error = $"Could not prepare terminal authentication: {exception.Message}";
				return null;
			}
		}

		private static string FindAgent()
		{
			// ProcessStartInfo's own lookup would also search the working directory.
			foreach (var directory in $"/usr/bin{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}".Split(Path.PathSeparator))
			{
				if (!Path.IsPathFullyQualified(directory)) continue;
				var candidate = Path.Combine(directory, "pkttyagent");
				if (File.Exists(candidate)) return candidate;
			}
			return null;
		}

		private static bool TryParseStartTime(ReadOnlySpan<char> stat, out ulong startTime)
		{
			startTime = 0;
			// The parenthesized command can contain spaces and closing parentheses.
			var end = stat.LastIndexOf(')');
			if (end < 0) return false;
			var fields = stat[(end + 1)..].TrimStart();
			for (var field = 3; field < 22; field++)
			{
				var separator = fields.IndexOf(' ');
				if (separator < 0) return false;
				fields = fields[(separator + 1)..].TrimStart();
			}
			var next = fields.IndexOf(' ');
			return ulong.TryParse(next < 0 ? fields : fields[..next], NumberStyles.None,
				CultureInfo.InvariantCulture, out startTime) && startTime != 0;
		}

		private static bool WaitForRegistration(AnonymousPipeServerStream notification,
			System.Diagnostics.Process process, int timeoutMilliseconds, out string error)
		{
			error = null;
			var descriptor = new PollDescriptor
			{
				Descriptor = notification.SafePipeHandle.DangerousGetHandle().ToInt32(),
				Events = POLLIN
			};
			var started = Environment.TickCount64;
			while (true)
			{
				var remaining = timeoutMilliseconds - (int)(Environment.TickCount64 - started);
				if (remaining <= 0)
				{
					error = "Timed out waiting for the terminal authentication agent to register.";
					return false;
				}
				var ready = Poll(ref descriptor, 1, remaining);
				if (ready < 0 && Marshal.GetLastPInvokeError() == EINTR) continue;
				if (ready == 0) continue;
				if (ready < 0 || (descriptor.ReturnedEvents & (POLLIN | POLLHUP)) == 0)
				{
					error = "Could not wait for terminal authentication agent registration.";
					return false;
				}
				// The agent closes the descriptor once registered, and exiting closes it as well.
				if (notification.ReadByte() != -1 || process.WaitForExit(25))
				{
					error = "The terminal authentication agent stopped before it was ready.";
					return false;
				}
				return true;
			}
		}

		private sealed class Agent : IDisposable
		{
			private System.Diagnostics.Process process;

			internal Agent(System.Diagnostics.Process process)
			{
				this.process = process;
				AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
			}

			private void OnProcessExit(object sender, EventArgs args) => Dispose();

			public void Dispose()
			{
				var running = Interlocked.Exchange(ref process, null);
				if (running == null) return;
				AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
				try
				{
					if (!running.HasExited)
					{
						// pkttyagent restores terminal echo in its SIGTERM handler.
						_ = Kill(running.Id, SIGTERM);
						if (!running.WaitForExit(StopTimeoutMilliseconds))
						{
							running.Kill();
							_ = running.WaitForExit(StopTimeoutMilliseconds);
						}
					}
				}
				catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
				{
					_ = Diagnostics.Debug.WriteLine($"Terminal authentication agent cleanup failed: {exception.Message}");
				}
				finally { running.Dispose(); }
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PollDescriptor
		{
			internal int Descriptor;
			internal short Events;
			internal short ReturnedEvents;
		}

		[DllImport("libc", EntryPoint = "open", SetLastError = true)]
		private static extern int Open(string path, int flags);
		[DllImport("libc", EntryPoint = "close")]
		private static extern int Close(int descriptor);
		[DllImport("libc", EntryPoint = "tcgetpgrp")]
		private static extern int TerminalProcessGroup(int descriptor);
		[DllImport("libc", EntryPoint = "getpgrp")]
		private static extern int ProcessGroup();
		[DllImport("libc", EntryPoint = "poll", SetLastError = true)]
		private static extern int Poll(ref PollDescriptor descriptor, nuint count, int timeout);
		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		private static extern int Kill(int processId, int signal);
	}
}
#endif
