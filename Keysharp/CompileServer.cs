namespace Keysharp.Main;

/// <summary>
/// Compile daemon ("--daemon" mode). Holds one warm compiler component and one reused
/// parse-context <see cref="Script"/>, accepts compile requests over a per-build/per-user named pipe, and
/// returns compiled assembly bytes so a thin launcher can run them in a lean process that never loads
/// the parser/Roslyn (see <see cref="CompileClient"/> and Program.RunCompiledBytes).
///
/// Correctness constraints:
///   - <see cref="Script.TheScript"/> is process-global, so compiles MUST be serialized. The accept
///     loop is single-threaded and runs on the (STA) thread that created the Script.
///   - Parsing only reads the built-in-only ReflectionsData and ensures thread vars, so one Script is
///     reused across parses.
/// </summary>
internal static class CompileServer
{
	// The environment variables a compile reads: those behind the A_ variables #Include and #Import paths use, and
	// the #Import search path. Package settings are not among them: NuGet caches those on first use.
	internal static readonly string[] CompileEnvironment =
	[
		"AhkImportPath", "COMSPEC", "HOME", "TEMP", "TMP", "TMPDIR", "USERPROFILE",
		"XDG_CONFIG_DIRS", "XDG_CONFIG_HOME", "XDG_DESKTOP_DIR", "XDG_DOCUMENTS_DIR"
	];

	// Far beyond any real request (a 24k-line script compiles in seconds): one which runs longer, such as an
	// #Include of a FIFO, a compiler hang or a stalled client, holds the single pipe instance, so the daemon exits
	// and the next client spawns a healthy one.
	internal static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(60);

	// The folder for the daemon's lock file and, off Windows, its socket. $XDG_RUNTIME_DIR is the user's own; the temp
	// folder is too on Windows and macOS, but on Linux it is usually the shared /tmp, where another user could create
	// them first.
	internal static readonly string RuntimeDirectory =
		Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtimeDirectory && Directory.Exists(runtimeDirectory)
		? runtimeDirectory : Path.GetTempPath();

	internal static readonly string UserKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
#if WINDOWS
		CompileDaemonSecurity.AccountId
#else
		Environment.UserName
#endif
	)))[..8];

	// Idle shutdown so an abandoned daemon does not linger forever (mirrors VBCSCompiler behavior).
	private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(4);

	internal static bool IsPrivileged =>
#if WINDOWS
		CompileDaemonSecurity.IsElevated;

#else
		GetEffectiveUserId() == 0;

	[System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
	private static extern uint GetEffectiveUserId();
#endif

	/// <summary>
	/// Pipe name keyed on build fingerprint + user, so a client only connects to its own user's daemon of the
	/// same Keysharp and compiler component, which therefore speaks the same wire format. Null when the build
	/// has no fingerprint.
	/// </summary>
	internal static string PipeName { get; } = CreatePipeName();

	// All daemon-side diagnostics go to stderr with a common prefix, which reaches a console only when the
	// daemon was started by hand: a client-spawned one inherits none of its launcher's standard handles (see
	// TrySpawnServer). Logging must never be able to take the daemon down, whatever it is or is not attached to.
	internal static void Log(string message)
	{
		try
		{ Console.Error.WriteLine($"[keysharp --daemon] {message}"); }
		catch { }
	}

	internal static int Run()
	{
		if (IsPrivileged)
		{
			if (CompileClient.TrySpawnServer())
				return 0;
			Log("a normal compile daemon could not be started; scripts will compile in the requesting process.");
			return 1;
		}

		if (PipeName == null)
		{
			Log("the compiler component is missing or could not be read; exiting.");
			return 1;
		}

		// Only one daemon runs per user. A daemon of any different build already up is killed and we take
		// over; if an identical-build daemon already owns the slot we defer and exit. This keeps exactly
		// one daemon alive across builds, even though their fingerprint-keyed pipe names differ.
		if (!DaemonCoordinator.TryBecomeOwner(PipeName))
		{
			Log("an identical-build compile daemon already owns the slot; exiting.");
			return 0;
		}

		try
		{
			return Listen();
		}
		finally
		{
			DaemonCoordinator.ReleaseOwnership();
		}
	}

	internal static void WriteStrings(BinaryWriter writer, string[] values)
	{
		writer.Write(values.Length);

		foreach (var value in values)
			writer.Write(value);
	}

	private static string CreatePipeName()
	{
		if (KeysharpFingerprint.Value is not { } fingerprint)
			return null;

		var name = $"ksc-{fingerprint}-{UserKey}";
		// Off Windows a pipe is a socket file, which .NET creates in the temp folder unless its name is a full path.
		return OperatingSystem.IsWindows() ? name : Path.Combine(RuntimeDirectory, name);
	}

	// Wire format (length-prefixed, BinaryReader/Writer):
	//   request : string scriptPath, string[] defines, string[] additionalComponents, string[] excludedComponents,
	//             int32 codePage, string includeFile ("" when none), string[] the caller's value of each
	//             CompileEnvironment variable ("" when unset); a string[] is an int32 count, then the strings
	//   response: byte CompileDaemonStatus, then
	//             Compiled                  -> int32 byteLen, byte[] assemblyBytes, string warnings ("" when none)
	//             CompileFailed/
	//             PackageRestoreNeeded      -> string errorMessage, bool errorStdOut
	//             Unavailable               -> string reason: the daemon itself failed, so the client compiles
	// `warnings` carries the script's #Warning text to the client: the daemon compiles in a detached process
	// whose stderr goes nowhere, so a warning printed here would be lost on the path most runs take. A failure
	// carries what the client needs to report it as an in-process compile would, without compiling again.
	private static void HandleRequest(NamedPipeServerStream server, IScriptCompiler ch, string exeDir)
	{
		// Killed rather than exited: exit handlers could wait on whatever hangs, such as a key-mapper lock held by
		// a stalled layout query.
		using var watchdog = new System.Threading.Timer(_ =>
		{
			Log($"a request took longer than {RequestDeadline.TotalSeconds:0} s; exiting.");
			Process.GetCurrentProcess().Kill();
		}, null, RequestDeadline, Timeout.InfiniteTimeSpan);
		using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
		using var writer = new BinaryWriter(server, Encoding.UTF8, leaveOpen: true);
		var scriptPath = reader.ReadString();
		var request = new ScriptCompileRequest
		{
			ScriptPath = scriptPath,
			CompilationName = Path.GetFileNameWithoutExtension(scriptPath),
			Defines = ReadStrings(reader),
			AdditionalComponents = ReadStrings(reader),
			ExcludedComponents = ReadStrings(reader),
			CodePage = reader.ReadInt32(),
			IncludeFile = reader.ReadString(),
			RuntimeDirectory = exeDir,
			Output = ScriptCompilationOutput.InMemory,
			// A shared background process does no network I/O for a caller it cannot see: an unrestored
			// #Package fails here, and the client recompiles in-process.
			AllowPackageRestore = false,
		};
		var callerValues = ReadStrings(reader);
		var sw = Stopwatch.StartNew();
		IScriptCompilationResult compilation = null;
		string fault = null;

		try
		{
			// The compile reads the caller's values. A request sets every one, "" removing a variable the caller has
			// unset, so none of an earlier caller's remain.
			for (var i = 0; i < CompileEnvironment.Length; i++)
				Environment.SetEnvironmentVariable(CompileEnvironment[i], callerValues[i]);

			compilation = ch.Compile(request);
		}
		catch (Exception ex)
		{
			fault = $"{ex.GetType().Name}: {ex.Message}";
		}

		sw.Stop();

		// A failure of the daemon rather than of the script is not the script's error to report: the client compiles
		// in-process instead, as it does when no daemon answers.
		if (fault != null)
		{
			Log($"compiling '{scriptPath}' failed in the daemon after {sw.ElapsedMilliseconds} ms: {fault}");
			writer.Write((byte)CompileDaemonStatus.Unavailable);
			writer.Write(fault);
		}
		else if (compilation.AssemblyBytes is { } bytes)
		{
			Log($"compiled '{scriptPath}' ({bytes.Length} bytes) in {sw.ElapsedMilliseconds} ms.");
			writer.Write((byte)CompileDaemonStatus.Compiled);
			writer.Write(bytes.Length);
			writer.Write(bytes);
			writer.Write(compilation.WarningText ?? "");
		}
		else
		{
			Log($"compile error for '{scriptPath}' in {sw.ElapsedMilliseconds} ms.");
			writer.Write((byte)(compilation.PackageRestoreNeeded ? CompileDaemonStatus.PackageRestoreNeeded : CompileDaemonStatus.CompileFailed));
			writer.Write(compilation.ErrorText ?? "Unknown compile error.");
			writer.Write(compilation.ErrorStdOut);
		}

		writer.Flush();

#if WINDOWS
		server.WaitForPipeDrain();
#endif
	}

	private static int Listen()
	{
		// Establish the single warm parse-context Script on this (STA) thread. The server never
		// registers hotkeys/hotstrings, so no input hooks or message pumps are ever activated.
		using var script = new Script();
		script.SuppressErrorOccurredDialog = true;

		if (!ScriptingComponentRegistry.TryGetCompiler(out var ch, out var componentError))
		{
			Log(componentError);
			return 1;
		}

		var exeDir = Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath));
		SetDaemonWorkingDirectory(exeDir);

		// Deliberately no priority tuning here. Lowering the process to BelowNormal for the duration of
		// the warmup - on the theory that it should yield to the client compiling the same script in the
		// foreground - measured consistently SLOWER (4.4 s cold against 3.5 s), so it was removed.
		Warmup(ch, exeDir);

		Log($"listening on pipe '{PipeName}' (idle timeout {IdleTimeout.TotalHours:0} h).");

		while (true)
		{
			NamedPipeServerStream server;

			try
			{
				// Only this user's processes may connect, and a client checks the same of the pipe: a request carries
				// the caller's paths, and a reply is code the caller runs.
				server = new NamedPipeServerStream(
					PipeName,
					PipeDirection.InOut,
					maxNumberOfServerInstances: 1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			}
			catch (IOException)
			{
				// The pipe name is already taken: another daemon with the same fingerprint owns it.
				// Two daemons would be redundant, so defer to the existing one and exit.
				Log("a compatible daemon is already running; exiting.");
				return 0;
			}

			using (server)
			{
				try
				{
					if (!WaitForConnection(server, IdleTimeout))
					{
						Log("idle timeout reached, exiting.");
						return 0;
					}
				}
				// Another user's connection, which the pipe refuses; a new instance waits for the next one.
				catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
				{
					continue;
				}

				try
				{
					HandleRequest(server, ch, exeDir);
				}
				catch (Exception ex)
				{
					// A single bad request must not take down the daemon.
					Log($"request failed: {ex.Message}");
				}
			}
		}
	}

	private static string[] ReadStrings(BinaryReader reader)
	{
		var values = new string[reader.ReadInt32()];

		for (var i = 0; i < values.Length; i++)
			values[i] = reader.ReadString();

		return values;
	}

	private static void SetDaemonWorkingDirectory(string exeDir)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
				Directory.SetCurrentDirectory(exeDir);
		}
		catch (Exception ex)
		{
			Log($"could not set daemon working directory to '{exeDir}': {ex.Message}");
		}
	}

	private static bool WaitForConnection(NamedPipeServerStream server, TimeSpan timeout)
	{
		var task = server.WaitForConnectionAsync();

		if (task.Wait(timeout))
			return true;

		// Unblock the pending async accept so the stream can be disposed cleanly.
		try
		{ server.Dispose(); }
		catch { }

		return false;
	}

	// Compile a trivial script once so the first real client request is already warm (parser + Roslyn
	// JITted, reference metadata loaded). Failures here are non-fatal; the first request just pays cold.
	private static void Warmup(IScriptCompiler ch, string exeDir)
	{
		try
		{
			var sw = Stopwatch.StartNew();
			_ = ch.Compile(new ScriptCompileRequest
			{
				SourceText = "x := 1",
				CompilationName = "warmup",
				RuntimeDirectory = exeDir,
				Output = ScriptCompilationOutput.InMemory,
			});
			Log($"warmup compile took {sw.ElapsedMilliseconds} ms.");
		}
		catch (Exception ex)
		{
			Log($"warmup failed (ignored): {ex.Message}");
		}
	}
}

internal enum CompileDaemonStatus
{
	/// <summary>The daemon compiled the script; assembly bytes are available.</summary>
	Compiled,

	/// <summary>The daemon was reached but the script failed to compile; an error message is available.</summary>
	CompileFailed,

	/// <summary>
	/// A required #Package failed to resolve, and the daemon never restores packages; a compile allowed to restore
	/// them may succeed. An error message is available.
	/// </summary>
	PackageRestoreNeeded,

	/// <summary>
	/// No compatible daemon could be reached (and, where applicable, none could be spawned), or the daemon itself
	/// failed to compile; either way the caller compiles in-process.
	/// </summary>
	Unavailable
}

/// <summary>What a compile request to the daemon produced.</summary>
/// <param name="ErrorStdOut">The script asks, through #ErrorStdOut, for its compile error to go to stderr.</param>
internal sealed record DaemonReply(CompileDaemonStatus Status, byte[] AssemblyBytes = null, string ErrorText = null,
	string WarningText = null, bool ErrorStdOut = false)
{
	internal static readonly DaemonReply Unavailable = new(CompileDaemonStatus.Unavailable);
}