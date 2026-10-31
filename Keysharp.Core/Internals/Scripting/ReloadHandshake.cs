using System.IO.Pipes;
using Keysharp.Builtins;

namespace Keysharp.Internals.Scripting
{
	// The replacement asks to retire its predecessor only after compiling. The pipe carries the request;
	// waiting for the process itself keeps replacement execution after the predecessor's entire teardown.
	internal sealed class ReloadHandshake : IDisposable
	{
		internal const string HandshakeVar = "KEYSHARP_RELOAD_HANDSHAKE";
		private const byte RetireRequest = 1;
		private const int WaitMs = 2000;
		private const int NonInteractiveWaitMs = 15000;
		private readonly Lock gate = new();
		private readonly Script script;
		private AnonymousPipeServerStream requests;
		private Process replacement;
		private CancellationTokenSource cancellation;
		private ScriptEventScheduler.PendingCallbackRegistration pending;
		private bool disposed;

		internal ReloadHandshake(Script script) => this.script = script;

#if !WINDOWS
		[DllImport("libc", EntryPoint = "getppid")]
		private static extern int PosixParentId();

		// The predecessor is this process's parent, so getppid reports its exit even if its PID is reused.
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

		internal void Start()
		{
			var registration = script.MainEventScheduler.RegisterPendingCallback(Dispose)
				?? throw new ObjectDisposedException(nameof(Script));

			try
			{
				Process process;

				lock (gate)
				{
					ObjectDisposedException.ThrowIf(disposed, this);
					pending = registration;
					registration = null;
					cancellation = new();
					requests = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
					var start = Runner.CreateRestartStartInfo();
					start.WorkingDirectory = Accessors.A_InitialWorkingDir;
					start.Environment[HandshakeVar] = $"{Environment.ProcessId},{requests.GetClientHandleAsString()}";
					process = replacement = Process.Start(start) ?? throw new InvalidOperationException("The replacement process did not start.");
					requests.DisposeLocalCopyOfClientHandle();
					process.Exited += (_, _) => Dispose();
					_ = AwaitRequestAsync(requests, cancellation.Token);
				}

				// Enabling events can call Dispose synchronously if the child has already exited.
				try
				{
					process.EnableRaisingEvents = true;
				}
				catch (InvalidOperationException) when (Volatile.Read(ref disposed))
				{
				}
			}
			finally
			{
				registration?.Dispose();
			}
		}

		private async Task AwaitRequestAsync(AnonymousPipeServerStream requests, CancellationToken token)
		{
			var request = new byte[1];

			try
			{
				if (await requests.ReadAsync(request, 0, 1, token).ConfigureAwait(false) == 1 && request[0] == RetireRequest)
					_ = script.MainEventScheduler.EnqueueCallback(Retire, ScriptEventQueue.Interactive);
			}
			catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
			{
				// Process exit or scheduler teardown cancels a request which never arrived.
			}
		}

		private void Retire()
		{
			// HasExited can raise Exited synchronously, so it must not run under the cleanup lock.
			try
			{
				if (replacement.HasExited)
					return;
			}
			catch (InvalidOperationException)
			{
				return;//Cleanup has already disposed the process.
			}

			lock (gate)
			{
				// A request can outlive a replacement which timed out while the main thread was busy.
				if (disposed || script.IsDisposed || script.hasExited || script.FlowData.exitReason != null
						|| Volatile.Read(ref script.FlowData.pendingReload) != this)
					return;
			}

			_ = Keysharp.Internals.Flow.ExitAppInternal(script, Keysharp.Builtins.Flow.ExitReasons.Reload, null, false);
		}

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				_ = Interlocked.CompareExchange(ref script.FlowData.pendingReload, null, this);
			}

			// Scheduler teardown calls this under its own lock. Release the registration outside ours.
			cancellation?.Cancel();
			requests?.Dispose();
			replacement?.Dispose();
			cancellation?.Dispose();
			pending?.Dispose();
		}

		internal static bool AskPredecessorToExit(bool errorStdOut)
		{
			var value = Environment.GetEnvironmentVariable(HandshakeVar);
			Environment.SetEnvironmentVariable(HandshakeVar, null);

			if (value == null)
				return true;

			var comma = value.IndexOf(',');

			if (comma < 0 || !int.TryParse(value.AsSpan(0, comma), out var pid) || pid <= 0)
			{
				Console.Error.WriteLine("Keysharp: invalid reload handshake.");
				return false;
			}

			try
			{
				using var requests = new AnonymousPipeClientStream(PipeDirection.Out, value[(comma + 1)..]);
#if WINDOWS
				Process predecessor;

				try
				{
					predecessor = Process.GetProcessById(pid);
				}
				catch (ArgumentException)
				{
					return true;//The old instance has already exited.
				}

				using (predecessor)
				{
					try
					{
						// Capture its identity before sending the request; waiting must not reopen a reused PID.
						_ = predecessor.SafeHandle;
					}
					catch (InvalidOperationException)
					{
						return true;
					}
#else
				{
					if (PosixParentId() != pid)
						return true;
#endif
					try
					{
						requests.WriteByte(RetireRequest);
					}
					catch (IOException)
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

					// Headless runs and scripts routing errors to stderr cannot use a retry dialog.
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
			}
			catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception)
			{
				Console.Error.WriteLine($"Keysharp: could not reach the instance being reloaded: {ex.Message}");
				return false;
			}

			return true;
		}
	}
}
