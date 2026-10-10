namespace Keysharp.Main;

/// <summary>
/// Thin client for the compile server. Connects to (and, on request, spawns) a daemon with a matching
/// build fingerprint, and returns compiled assembly bytes so the caller can run them via the lean path.
/// </summary>
internal static class CompileClient
{
#if WINDOWS
	private const int HANDLE_FLAG_INHERIT = 1;

	private const int STD_ERROR_HANDLE = -12;

	private const int STD_INPUT_HANDLE = -10;

	private const int STD_OUTPUT_HANDLE = -11;
#endif

	// How long to wait for a freshly spawned daemon to finish warmup and start listening before giving up
	// and compiling in-process instead. The warmup is one Roslyn compile, ~2 s, so this is generous; the
	// cap exists so that a daemon which never becomes healthy cannot hold the user's script hostage. It
	// was 30 s, which is long enough that a stuck daemon looks like a hang.
	private static readonly TimeSpan SpawnWaitTimeout = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Compiles <paramref name="command"/>'s script via a running daemon, spawning one and waiting for it when
	/// none is reachable. Returns <see cref="CompileDaemonStatus.Unavailable"/> if no daemon can be
	/// started or one does not become ready within <see cref="SpawnWaitTimeout"/>, and the caller then
	/// compiles in-process.
	///
	/// Waiting is deliberate, and was measured against the alternative of spawning the daemon and
	/// immediately compiling in-process instead (8 cores):
	///
	///   single cold start (n=5, median)   wait 3 636 ms | no wait 3 710 ms | no daemon 3 041 ms
	///   4 concurrent cold (n=4, mean)     wait 5 167 ms | no wait 7 464 ms
	///
	/// Not waiting is no faster for one script and markedly slower for several at once, because waiting
	/// funnels every client through the one warm daemon - whose accept loop is single-threaded, so the
	/// compiles serialise and share warm Roslyn - whereas not waiting has every client run its own
	/// Roslyn compile at the same time, alongside the daemon's warmup, all competing for cores.
	///
	/// Concurrency is safe: several clients starting at once each spawn a daemon, but DaemonCoordinator
	/// arbitrates with a named mutex and a pid/procname lock file and the losers exit inside
	/// TryBecomeOwner, before Listen and therefore before any warmup work. The winner does not create its
	/// pipe until warmup has finished, so no client can reach a half-initialised daemon; its connect attempt
	/// just keeps waiting.
	/// </summary>
	internal static DaemonReply CompileViaServer(CliCommand command)
	{
		// A warm daemon answers at once. Otherwise one is spawned, unless the lock file names one for this build which
		// is still warming up or busy, and Connect waits for its pipe. A request the daemon dropped only once its
		// deadline passed is not sent again, as the next daemon would end the same way.
		var sw = Stopwatch.StartNew();

		if (TryCompile(command, 0) is { } reply)
			return reply;

		if (sw.Elapsed >= CompileServer.RequestDeadline || (!DaemonCoordinator.HasLiveOwner(CompileServer.PipeName) && !TrySpawnServer()))
			return DaemonReply.Unavailable;

		// Another attempt follows only when a daemon went away mid-request, such as one replaced by a different build.
		sw.Restart();

		do
			if (TryCompile(command, (int)Math.Max(1, (SpawnWaitTimeout - sw.Elapsed).TotalMilliseconds)) is { } answer)
				return answer;
		while (sw.Elapsed < SpawnWaitTimeout);

		return DaemonReply.Unavailable;
	}

	/// <summary>
	/// Attempts a single compile against an already-running daemon. Returns null (no exception) when no daemon
	/// answered: none is listening, or it went away mid-request. A build without a pipe, and a daemon which
	/// itself failed, answer <see cref="CompileDaemonStatus.Unavailable"/>.
	/// </summary>
	internal static DaemonReply TryCompile(CliCommand command, int connectTimeoutMs = 1000)
	{
		if (CompileServer.PipeName == null)
			return DaemonReply.Unavailable;

#if WINDOWS
		// A shared dotnet host does not identify which managed application the server runs.
		if (CompileServer.IsPrivileged && Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
			return DaemonReply.Unavailable;
		using var client = new NamedPipeClientStream(".", CompileServer.PipeName, PipeDirection.InOut,
			PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
#else
		using var client = new NamedPipeClientStream(".", CompileServer.PipeName, PipeDirection.InOut,
			PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif

		try
		{
			client.Connect(connectTimeoutMs);
#if WINDOWS
			if (!CompileDaemonSecurity.IsExpectedServer(client))
				return DaemonReply.Unavailable;
#endif
		}
		catch (Exception ex) when (ex is TimeoutException or IOException)
		{
			return null;
		}
		// An unauthenticated pipe gets no request: the caller compiles at once.
		catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
		{
			return DaemonReply.Unavailable;
		}

		using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
		using var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);

		try
		{
			writer.Write(Path.GetFullPath(command.ScriptName));
			CompileServer.WriteStrings(writer, command.Defines);
			CompileServer.WriteStrings(writer, command.IncludeComponents);
			CompileServer.WriteStrings(writer, command.ExcludeComponents);
			writer.Write(command.CodePage);
			writer.Write(command.IncludeFile ?? "");
			CompileServer.WriteStrings(writer, System.Array.ConvertAll(CompileServer.CompileEnvironment, name => Environment.GetEnvironmentVariable(name) ?? ""));
			writer.Flush();

			switch ((CompileDaemonStatus)reader.ReadByte())
			{
				case CompileDaemonStatus.Compiled:
					var bytes = reader.ReadBytes(reader.ReadInt32());
					return new(CompileDaemonStatus.Compiled, bytes, WarningText: reader.ReadString());

				case var status and (CompileDaemonStatus.CompileFailed or CompileDaemonStatus.PackageRestoreNeeded):
					var error = reader.ReadString();
					return new(status, ErrorText: error, ErrorStdOut: reader.ReadBoolean());

				default:
					return new(CompileDaemonStatus.Unavailable, ErrorText: reader.ReadString());
			}
		}
		// The daemon went away mid-request: it was replaced by a different build, idled out between connect and reply,
		// or was ended by its request deadline.
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			return null;
		}
	}

	/// <summary>
	/// Launches "<this host> --daemon" detached so it outlives the current process. The spawned daemon runs
	/// DaemonCoordinator.TryBecomeOwner at startup, which kills any different-build daemon and defers to an
	/// identical one, so racing spawns converge on a single owner.
	/// A daemon which cannot be spawned only means compiling in-process, so the reason is not reported.
	/// </summary>
	/// <returns></returns>
	internal static bool TrySpawnServer()
	{
		try
		{
			var processPath = Environment.ProcessPath;

			if (string.IsNullOrEmpty(processPath))
				return false;

			var psi = new ProcessStartInfo
			{
				FileName = processPath,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(processPath),
			};

			// When launched as "dotnet Keysharp.dll", re-pass the managed dll so the child runs Keysharp.
			var entryDll = Assembly.GetEntryAssembly()?.Location;

			if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrEmpty(entryDll)
					&& entryDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				psi.ArgumentList.Add(entryDll);

			psi.ArgumentList.Add("--daemon");

			// A compile is short and allocation-heavy: Dynamic PGO's instrumented tier never pays for itself in the
			// daemon, and workstation GC leaves Roslyn's parallel binding waiting on collections (3.5 s → 1.7 s per
			// warm compile of a 24k-line script). Scripts run in the launching process, which keeps the defaults.
			psi.Environment.TryAdd("DOTNET_TieredPGO", "0");
			psi.Environment.TryAdd("DOTNET_gcServer", "1");
			psi.Environment.TryAdd("DOTNET_gcConcurrent", "0");

#if WINDOWS
			if (CompileServer.IsPrivileged)
				return CompileDaemonSecurity.TryStartUnelevated(psi);
#else
			if (CompileServer.IsPrivileged)
				return false;
#endif

			// The daemon must not inherit this process's standard handles. It outlives us by up to four
			// hours, so a handle it keeps open is a pipe that never reaches end-of-stream: piping a
			// Keysharp run and reading to EOF hangs long after the script exited (`keysharp x.ks | more`
			// never returns), and redirecting to a file leaves that file locked.
			//
			// On Windows, redirecting the child's streams does not fix this. .NET calls CreateProcess with
			// bInheritHandles=TRUE, so the child
			// receives a copy of *every* inheritable handle we hold, whatever its own std handles are set
			// to - including the pipe someone handed us as our stdout. The handles have to stop being
			// inheritable instead, which is what this does, restoring them immediately afterwards so
			// nothing else in the process is affected.
			//
			// Scope, precisely: this covers the three standard handles, which are the ones that cause the
			// visible hang. Any OTHER inheritable handle open at this moment is still copied into the
			// daemon. That is acceptable only because of where this runs - Program.Main, before any script
			// is loaded, so the process holds little else - and because the flags are process-global, this
			// must stay on a path with no concurrent Process.Start.
#if !WINDOWS
			psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
#endif
			using (SuppressStandardHandleInheritance())
			{
				using var daemon = Process.Start(psi);

				if (daemon == null)
					return false;
#if !WINDOWS
				daemon.StandardInput.Close();
				daemon.StandardOutput.Close();
				daemon.StandardError.Close();
#endif
				return true;
			}
		}
		catch (Exception)
		{
			return false;
		}
	}

#if WINDOWS

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetHandleInformation(nint hObject, out int lpdwFlags);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint GetStdHandle(int nStdHandle);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetHandleInformation(nint hObject, int dwMask, int dwFlags);

	/// <summary>
	/// Clears the inheritable flag on this process's standard handles, restoring it on dispose. Scoped as
	/// tightly as possible around the spawn, since it is process-global state.
	/// </summary>
	private static IDisposable SuppressStandardHandleInheritance()
	{
		var restore = new List<(nint Handle, int Flags)>(3);

		foreach (var id in new[] { STD_INPUT_HANDLE, STD_OUTPUT_HANDLE, STD_ERROR_HANDLE })
		{
			var handle = GetStdHandle(id);

			// A GUI process launched from Explorer has no standard handles at all; nothing to do.
			if (handle is 0 or -1)
				continue;

			if (GetHandleInformation(handle, out var flags) && (flags & HANDLE_FLAG_INHERIT) != 0)
				if (SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0))
					restore.Add((handle, flags));
		}

		return new HandleInheritanceScope(restore);
	}

	private sealed class HandleInheritanceScope(List<(nint Handle, int Flags)> restore) : IDisposable
	{
		public void Dispose()
		{
			foreach (var (handle, flags) in restore)
				_ = SetHandleInformation(handle, HANDLE_FLAG_INHERIT, flags & HANDLE_FLAG_INHERIT);
		}
	}

#else

	// Only Windows needs this. On Unix, .NET opens its own descriptors close-on-exec, and TrySpawnServer replaces the
	// three standard ones with pipes of the daemon's own.
	private static IDisposable SuppressStandardHandleInheritance() => null;

#endif
}