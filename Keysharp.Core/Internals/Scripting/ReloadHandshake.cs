using Keysharp.Builtins;

namespace Keysharp.Internals.Scripting
{
	// The replacement asks to retire its predecessor only after compiling: on Windows through its main window, as AHK's
	// does, otherwise through its ScriptEndpoint. Waiting for the process itself keeps replacement execution after the
	// predecessor's entire teardown.
	internal sealed class ReloadHandshake : IDisposable
	{
		// Names the predecessor's PID and main window, 0 without one, to the replacement, and marks the replacement as one.
		internal const string HandshakeVar = "KEYSHARP_RELOAD_HANDSHAKE";
		private static int requestSequence;
		internal readonly int RequestToken;
		private readonly Lock gate = new();
		private readonly Script script;
		private Process replacement;
		private ScriptEventScheduler.PendingCallbackRegistration pending;
		private bool disposed;

		internal ReloadHandshake(Script script)
		{
			this.script = script;
			RequestToken = Interlocked.Increment(ref requestSequence);

			if (RequestToken == 0)
				RequestToken = Interlocked.Increment(ref requestSequence);
		}

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
					var start = Runner.CreateRestartStartInfo();
					start.WorkingDirectory = Accessors.A_InitialWorkingDir;
#if WINDOWS
					var window = script.mainWindow != null ? script.MainWindowHandle : 0;
#else
					nint window = 0;
#endif
					start.Environment[HandshakeVar] = FormattableString.Invariant($"{Environment.ProcessId},{window},{ScriptEndpoint.CurrentProcessStartTicks},{RequestToken}");
					process = replacement = Process.Start(start) ?? throw new InvalidOperationException("The replacement process did not start.");
					process.Exited += (_, _) => Dispose();
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

		/// <summary>Queues the exit the replacement asked for through this script's endpoint.</summary>
		internal bool RequestRetire(int requestToken)
		{
			lock (gate)
			{
				if (disposed || replacement == null || requestToken != RequestToken)
					return false;
			}

			return script.MainEventScheduler.EnqueueCallback(Retire, ScriptEventQueue.Interactive);
		}

		private void Retire()
		{
			// HasExited can raise Exited synchronously, so it must not run under the cleanup lock.
			try
			{
				if (replacement.HasExited)
					return;
			}
			catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
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
			replacement?.Dispose();
			pending?.Dispose();
		}

		internal static bool AskPredecessorToExit(bool errorStdOut)
		{
			var value = Environment.GetEnvironmentVariable(HandshakeVar);
			Environment.SetEnvironmentVariable(HandshakeVar, null);

			if (value == null)
				return true;

			var parts = value.Split(',');

			if (parts.Length != 4 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0
					|| !long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var window)
					|| !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expectedProcessStartTicks) || expectedProcessStartTicks <= 0
					|| !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var requestToken) || requestToken == 0)
			{
				Console.Error.WriteLine("Keysharp: invalid reload handshake.");
				return false;
			}

			// Headless runs and scripts routing errors to stderr cannot use a retry dialog.
			var interactive = !Script.IsHeadless && !errorStdOut && Env.FindCommandLineArg("errorstdout") == null;
			return Script.RetireAndWait(pid, (nint)window, Keysharp.Builtins.Flow.ExitReasons.Reload, interactive, expectedProcessStartTicks, requestToken);
		}
	}
}
