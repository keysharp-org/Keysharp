using static Keysharp.Internals.Scripting.CompileDaemonProtocol;

namespace Keysharp.Main;

/// <summary>
/// Compile daemon ("--daemon" mode). Holds one warm compiler component and one reused
/// parse-context <see cref="Script"/>, accepts compile requests over a per-build/per-user named pipe, and
/// returns compiled assembly bytes so a thin launcher can run them in a lean process that never loads
/// the parser/Roslyn (see <see cref="CompileClient"/> and Program.RunCompiledBytes).
///
/// Compiles are serialized on the Script's original STA thread because its state is process-global.
/// Parsing only needs built-in reflection data and thread variables, so the parse context can be reused.
/// </summary>
internal static class CompileServer
{
	// Idle shutdown so an abandoned daemon does not linger forever (mirrors VBCSCompiler behavior).
	private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(4);

	/// <summary>
	/// Pipe name keyed on build fingerprint + user, so a client only connects to its own user's daemon of the
	/// same Keysharp and compiler component, which therefore speaks the same wire format. Null when the build
	/// has no fingerprint.
	/// </summary>
	internal static string PipeName { get; } = CreatePipeName(
		CompileDaemonFingerprint.Compute(typeof(CompileServer).Module.ModuleVersionId, AppContext.BaseDirectory));

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
#if WINDOWS
		if (CompileDaemonSecurity.IsElevated)
		{
			if (CompileClient.TrySpawnServer())
				return 0;
			Log("a normal compile daemon could not be started; scripts will compile in the requesting process.");
			return 1;
		}
#endif

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

	// Wire format (length-prefixed, BinaryReader/Writer):
	//   request : bool fromStdin, string path/source, string workingDirectory, bool hasCompilationName + optional name,
	//             string includeDirectory ("" when none), string[] defines, string[] additionalComponents, string[] excludedComponents,
	//             int32 codePage, string includeFile ("" when none), string[] the caller's value of each
	//             CompileEnvironment variable ("" when unset); a string[] is an int32 count, then the strings
	//   response: byte CompileDaemonStatus, then
	//             Compiled                  -> int32 byteLen, byte[] assemblyBytes, string warnings ("" when none)
	//             CompileFailed/
	//             PackageRestoreNeeded      -> string errorMessage, bool errorStdOut, string warnings
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
		var fromStdin = reader.ReadBoolean();
		var script = reader.ReadString();
		var workingDirectory = reader.ReadString();
		var compilationName = reader.ReadBoolean() ? reader.ReadString() : null;
		var includeDirectory = reader.ReadString();
		var logName = fromStdin ? "<stdin>" : script;
		var request = new ScriptCompileRequest
		{
			ScriptPath = fromStdin ? null : script,
			SourceText = fromStdin ? script : null,
			CompilationName = compilationName,
			IncludeDirectory = string.IsNullOrEmpty(includeDirectory) ? null : includeDirectory,
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

			// Compile where an in-process compile would: the working directory is a stdin script's #Include base,
			// A_ScriptDir and package-settings folder. Compiles are serialized, so each can borrow it.
			if (workingDirectory != null)
				Directory.SetCurrentDirectory(workingDirectory);

			compilation = ch.Compile(request);
		}
		catch (Exception ex)
		{
			fault = $"{ex.GetType().Name}: {ex.Message}";
		}
		finally
		{
			if (workingDirectory != null)
				SetDaemonWorkingDirectory(exeDir);
		}

		sw.Stop();

		// A failure of the daemon rather than of the script is not the script's error to report: the client compiles
		// in-process instead, as it does when no daemon answers.
		if (fault != null)
		{
			Log($"compiling '{logName}' failed in the daemon after {sw.ElapsedMilliseconds} ms: {fault}");
			writer.Write((byte)CompileDaemonStatus.Unavailable);
			writer.Write(fault);
		}
		else if (compilation.AssemblyBytes is { } bytes)
		{
			Log($"compiled '{logName}' ({bytes.Length} bytes) in {sw.ElapsedMilliseconds} ms.");
			writer.Write((byte)CompileDaemonStatus.Compiled);
			writer.Write(bytes.Length);
			writer.Write(bytes);
			writer.Write(compilation.WarningText ?? "");
		}
		else
		{
			Log($"compile error for '{logName}' in {sw.ElapsedMilliseconds} ms.");
			writer.Write((byte)(compilation.PackageRestoreNeeded ? CompileDaemonStatus.PackageRestoreNeeded : CompileDaemonStatus.CompileFailed));
			writer.Write(compilation.ErrorText ?? "Unknown compile error.");
			writer.Write(compilation.ErrorStdOut);
			writer.Write(compilation.WarningText ?? "");
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
		var exeDir = AppContext.BaseDirectory;
		SetDaemonWorkingDirectory(exeDir);
#if WINDOWS
		ShutdownWindow.StartShutdownListener();
#endif

		// Deliberately no priority tuning here. Lowering the process to BelowNormal for the duration of
		// the warmup - on the theory that it should yield to the client compiling the same script in the
		// foreground - measured consistently SLOWER (4.4 s cold against 3.5 s), so it was removed.
		Warmup(ch, exeDir);

		Log($"listening on pipe '{PipeName}' (idle timeout {IdleTimeout.TotalHours:0} h).");

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

		// Retain the Unix listening socket so a queued next client survives the previous client's disconnect.
		using (server)
		{
			while (true)
			{
				try
				{
					if (!WaitForConnection(server, IdleTimeout))
					{
						Log("idle timeout reached, exiting.");
						return 0;
					}
				}
				// The pipe refuses another user's connection and keeps waiting for this user's clients.
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
				finally
				{
					server.Disconnect();
				}
			}
		}
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
