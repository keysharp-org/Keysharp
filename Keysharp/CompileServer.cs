using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Keysharp.Components.Scripting;
using Keysharp.Internals.Scripting;
using Keysharp.Runtime;

namespace Keysharp.Main
{
	internal enum CompileDaemonStatus
	{
		/// <summary>The daemon compiled the script; assembly bytes are available.</summary>
		Compiled,
		/// <summary>The daemon was reached but the script failed to compile; an error message is available.</summary>
		CompileFailed,
		/// <summary>No compatible daemon could be reached (and, where applicable, none could be spawned).</summary>
		Unreachable
	}

	/// <summary>
	/// Identifies the exact Keysharp build, used to key the compile-server pipe. A client must only ever
	/// talk to a daemon running the SAME Keysharp.exe AND Keysharp.Core.dll, otherwise it could receive
	/// assembly bytes compiled against different references/runtime. We fingerprint both modules by their
	/// Module Version IDs (MVIDs): the compiler stamps a distinct MVID into every build of an assembly, so
	/// any change to either binary changes the fingerprint and a mismatched client simply spawns its own
	/// daemon instead of reusing an incompatible one.
	/// </summary>
	internal static class KeysharpFingerprint
	{
		internal static string Value { get; } = Compute();

		private static string Compute()
		{
			// typeof(KeysharpFingerprint) lives in Keysharp.dll/exe; typeof(Script) lives in Keysharp.Core.dll.
			var keysharpMvid = typeof(KeysharpFingerprint).Module.ModuleVersionId;
			var coreMvid = typeof(Script).Module.ModuleVersionId;

			Span<byte> buf = stackalloc byte[32];
			_ = keysharpMvid.TryWriteBytes(buf.Slice(0, 16));
			_ = coreMvid.TryWriteBytes(buf.Slice(16, 16));
			Span<byte> hash = stackalloc byte[32];
			_ = SHA256.HashData(buf, hash);
			return Convert.ToHexString(hash.Slice(0, 8)); // 16 hex chars is plenty to avoid collisions.
		}
	}

	/// <summary>
	/// Ensures at most one compile daemon runs per user: a starting daemon kills any live daemon of a
	/// DIFFERENT build and takes over, and defers (exits) only if an IDENTICAL build is already running.
	/// Build identity is the fingerprint-keyed pipe name (distinct MVIDs => distinct pipe), so "different
	/// build" is just "different pipe name" — no version ordering is implied; the most recently started
	/// daemon wins. Coordination is a tiny per-user lock file guarded by a named mutex so the
	/// compare-and-kill is atomic across processes. Stale entries (dead PID, or a reused PID whose process
	/// name no longer matches) are treated as no owner, so the scheme self-heals.
	/// </summary>
	internal static class DaemonCoordinator
	{
		private static readonly string LockFile = Path.Combine(Path.GetTempPath(), $"keysharp-compile-server-{Environment.UserName}.lock");
		private static readonly string MutexName = $@"Local\keysharp-compile-coord-{Environment.UserName}";

		/// <summary>
		/// Acquires the coordination mutex, or reports that it could not be had. The result of WaitOne must
		/// never be discarded: on timeout the caller would otherwise run the compare-and-kill critical section
		/// with no mutual exclusion at all, and then throw from ReleaseMutex on the way out.
		/// </summary>
		private static bool TryAcquire(Mutex mutex, TimeSpan timeout, out bool acquired)
		{
			acquired = false;

			try
			{
				acquired = mutex.WaitOne(timeout);
			}
			catch (AbandonedMutexException)
			{
				acquired = true; // A previous owner died holding it; ownership passes to us.
			}
			catch
			{
				return false;
			}

			return acquired;
		}

		private static void Release(Mutex mutex, bool acquired)
		{
			if (acquired)
				try { mutex.ReleaseMutex(); } catch { }
		}

		internal static bool TryBecomeOwner(string pipeName)
		{
			using var mutex = new Mutex(false, MutexName);

			// Deliberately shorter than the client's SpawnWaitTimeout. The mutex is held across TryKill's
			// WaitForExit, so multi-second holds are normal rather than pathological; but if this wait were as
			// long as the client's whole budget, a contended startup could never finish in time to be used.
			if (!TryAcquire(mutex, TimeSpan.FromSeconds(4), out var acquired))
			{
				CompileServer.Log("could not acquire the coordination mutex; another daemon is starting, so exiting.");
				return false;
			}

			try
			{
				var owner = Read();

				if (owner != null && owner.Pid != Environment.ProcessId && IsLiveDaemon(owner))
				{
					if (string.Equals(owner.Pipe, pipeName, StringComparison.Ordinal))
						return false; // An identical-build daemon already owns the slot.

					TryKill(owner.Pid); // Any different build: replace it so only one runs.
				}

				// A lock file we cannot write is not fatal: this daemon still serves its pipe, and a second one
				// that reaches the same conclusion simply fails to create the single-instance pipe and exits.
				// Crashing here instead would take the daemon down at startup - with its stderr going nowhere,
				// since it was spawned detached - and silently cost every later run the full client timeout.
				if (!Write(pipeName))
					CompileServer.Log("could not record daemon ownership; continuing without it.");

				return true;
			}
			finally
			{
				Release(mutex, acquired);
			}
		}

		/// <param name="timeout">
		/// Kept short on the shutdown path. This runs inside the WM_ENDSESSION handler, and Windows' default
		/// HungAppTimeout is five seconds - spending that long waiting on a mutex gets the daemon classed as
		/// not responding and listed, captionless, on the "these apps are preventing shutdown" screen.
		/// Releasing ownership is only tidiness: a lock file left behind is recognised as stale by IsLiveDaemon.
		/// </param>
		internal static void ReleaseOwnership(TimeSpan? timeout = null)
		{
			using var mutex = new Mutex(false, MutexName);

			if (!TryAcquire(mutex, timeout ?? TimeSpan.FromSeconds(5), out var acquired))
				return;

			try
			{
				var owner = Read();

				if (owner != null && owner.Pid == Environment.ProcessId && File.Exists(LockFile))
					File.Delete(LockFile);
			}
			catch { }
			finally
			{
				Release(mutex, acquired);
			}
		}

		internal static void StopOwner()
		{
			using var mutex = new Mutex(false, MutexName);

			if (!TryAcquire(mutex, TimeSpan.FromSeconds(5), out var acquired))
				return;

			try
			{
				var owner = Read();
				var killed = true;

				if (owner != null && owner.Pid != Environment.ProcessId && IsLiveDaemon(owner))
				{
					TryKill(owner.Pid);
					// Only drop the record once the process is actually gone. Deleting it after a failed kill -
					// a daemon running as another user, say - would hide a live daemon from the next one, which
					// would then start alongside it instead of replacing it.
					killed = !IsLiveDaemon(owner);
				}

				if (killed && File.Exists(LockFile))
					File.Delete(LockFile);
			}
			catch { }
			finally
			{
				Release(mutex, acquired);
			}
		}

		private sealed class Owner
		{
			internal int Pid;
			internal string ProcName;
			internal long StartedAtTicks;
			internal string Pipe;
		}

		// Lock file is a single line: "pid|procName|startTimeUtcTicks|pipeName".
		private static Owner Read()
		{
			try
			{
				if (!File.Exists(LockFile))
					return null;

				var parts = File.ReadAllText(LockFile).Split('|');

				// Anything shorter is either corrupt or written by a build that recorded no start time. Both
				// are treated as no owner at all, which is safe: the worst case is that this daemon takes over
				// a slot that was already free.
				if (parts.Length >= 4
						&& int.TryParse(parts[0], out var pid)
						&& long.TryParse(parts[2], out var startedAt))
					return new Owner { Pid = pid, ProcName = parts[1], StartedAtTicks = startedAt, Pipe = parts[3] };
			}
			catch { }

			return null;
		}

		private static bool Write(string pipeName)
		{
			try
			{
				using var self = Process.GetCurrentProcess();
				File.WriteAllText(LockFile, $"{Environment.ProcessId}|{self.ProcessName}|{self.StartTime.ToUniversalTime().Ticks}|{pipeName}");
				return true;
			}
			catch
			{
				return false;
			}
		}

		// A recorded PID counts as a live daemon only if it is running AND is the same process instance that
		// wrote the record.
		//
		// The process NAME is nowhere near enough on its own: every script the user launches is also a process
		// called "Keysharp", so a lock file left behind by a hard kill - the MSI closing the daemon, Task
		// Manager, a crash - whose PID Windows later hands to one of those scripts would satisfy a name check
		// and get the user's own running script killed as an "older daemon". Start time is what actually
		// identifies the instance; a recycled PID cannot match it.
		private static bool IsLiveDaemon(Owner owner)
		{
			try
			{
				using var p = Process.GetProcessById(owner.Pid);

				if (p.HasExited || !string.Equals(p.ProcessName, owner.ProcName, StringComparison.OrdinalIgnoreCase))
					return false;

				return p.StartTime.ToUniversalTime().Ticks == owner.StartedAtTicks;
			}
			catch
			{
				return false;
			}
		}

		private static void TryKill(int pid)
		{
			try
			{
				using var p = Process.GetProcessById(pid);
				p.Kill();
				_ = p.WaitForExit(5000);
				CompileServer.Log($"killed older compile daemon (pid {pid}).");
			}
			catch (Exception ex)
			{
				CompileServer.Log($"could not kill older daemon (pid {pid}): {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Compile daemon ("--daemon" mode). Holds one warm compiler component and one reused
	/// parse-context <see cref="Script"/>, accepts script paths over a per-build/per-user named pipe, and
	/// returns compiled assembly bytes so a thin launcher can run them in a lean process that never loads
	/// the parser/Roslyn (see <see cref="CompileClient"/> and Program.RunCompiledBytes).
	///
	/// Correctness constraints:
	///   - <see cref="Script.TheScript"/> is process-global, so compiles MUST be serialized. The accept
	///     loop is single-threaded and runs on the (STA) thread that created the Script.
	///   - Parsing only reads the built-in-only ReflectionsData and writes scriptPath/scriptName + thread
	///     vars, so one Script is reused across parses via ResetScriptForParse.
	/// </summary>
	internal static class CompileServer
	{
		// Bump when the wire protocol changes. (The fingerprint already separates incompatible builds; this
		// guards against a protocol change within an otherwise-identical build during development.)
		internal const int ProtocolVersion = 1;

		// Idle shutdown so an abandoned daemon does not linger forever (mirrors VBCSCompiler behavior).
		private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(4);

		// All daemon-side diagnostics go to stderr with a common prefix, which reaches a console only when the
		// daemon was started by hand: a client-spawned one inherits no standard handles at all (see
		// SuppressStandardHandleInheritance). Logging must never be able to take the daemon down, whatever it
		// is or is not attached to.
		internal static void Log(string message)
		{
			try { Console.Error.WriteLine($"[keysharp --daemon] {message}"); }
			catch { }
		}

		/// <summary>
		/// Pipe name keyed on protocol + build fingerprint + user, so a client never connects to a daemon
		/// built from different Keysharp/Keysharp.Core binaries, or to another user's daemon.
		/// </summary>
		internal static string PipeName { get; } = CreatePipeName();

		private static string CreatePipeName()
		{
			var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName))).Substring(0, 8);
			return $"ksc-{ProtocolVersion}-{KeysharpFingerprint.Value}-{userHash}";
		}

		internal static int Run()
		{
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
#if WINDOWS
			StartShutdownListener();
#endif

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
					server = new NamedPipeServerStream(
						PipeName,
						PipeDirection.InOut,
						maxNumberOfServerInstances: 1,
						PipeTransmissionMode.Byte,
						PipeOptions.Asynchronous);
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
					if (!WaitForConnection(server, IdleTimeout))
					{
						Log("idle timeout reached, exiting.");
						return 0;
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

#if WINDOWS

		/// <summary>
		/// Lets Windows shut the daemon down cleanly instead of having it killed.
		///
		/// The daemon holds Keysharp.exe and Keysharp.Core.dll open, so an installer replacing or removing
		/// them has to close it first. The Restart Manager, which every modern MSI uses, does that by
		/// *asking* a process to exit: it enumerates the process's top-level windows and sends
		/// WM_QUERYENDSESSION / WM_ENDSESSION. A daemon with no window has nothing to ask, so the Restart
		/// Manager reports it as blocking the operation and then leaves it running - which is exactly how
		/// an uninstall came to fail until the daemon was killed by hand.
		///
		/// Two details matter, and both are easy to get wrong:
		///
		///  * the window must be a genuine TOP-LEVEL window. A message-only window (HWND_MESSAGE parent) is
		///    not returned by EnumWindows and never receives session messages, so it would look right and
		///    silently do nothing. Default CreateParams gives a top-level window; it is never shown, so
		///    without WS_VISIBLE it stays out of the taskbar and Alt-Tab anyway.
		///  * session messages are SENT to windows, not posted to the thread queue, so a bare message loop
		///    on a window-less thread would never see them either.
		///
		/// The window lives on its own thread because Listen() blocks on the pipe from the warm parse-context
		/// STA thread and must keep doing so - compilation depends on running there.
		///
		/// This also covers WM_CLOSE, which is what the installer's util:CloseApplication sends before it
		/// resorts to terminating the process, so an up-to-date daemon now exits on its own during an
		/// upgrade or uninstall and never reaches the force-kill path.
		/// </summary>
		private static void StartShutdownListener()
		{
			// Nothing downstream reads the window, so there is nothing to wait for: the pump thread is started
			// and the caller goes straight on to warm up. An earlier version blocked here for up to five
			// seconds, which at best only ordered the log lines and at worst spent half the client's patience
			// before the daemon had begun its warmup.
			var thread = new Thread(() =>
			{
				try
				{
					shutdownWindow = new ShutdownWindow();
					shutdownWindow.CreateHandle(new System.Windows.Forms.CreateParams());
					Log($"shutdown window ready (hwnd 0x{shutdownWindow.Handle:X}).");
					System.Windows.Forms.Application.Run(); // Pumps until the process exits.
					Log("shutdown window message loop returned unexpectedly.");
				}
				catch (Exception ex)
				{
					// Best effort: a daemon without the window still works, it just has to be terminated
					// rather than asked to leave, which is what every build before this one did.
					Log($"could not create the shutdown window ({ex.Message}); the daemon will have to be terminated to close it.");
				}
			})
			{ IsBackground = true, Name = "Keysharp compile daemon shutdown listener" };
			thread.SetApartmentState(ApartmentState.STA);

			try
			{
				thread.Start();
			}
			catch (Exception ex)
			{
				Log($"could not start the shutdown listener ({ex.Message}); the daemon will have to be terminated to close it.");
			}
		}

		/// <summary>
		/// Roots the shutdown window for the life of the process. NativeWindow destroys its handle from its
		/// finalizer, and the local in <see cref="StartShutdownListener"/> is dead the moment CreateHandle
		/// returns - Application.Run does not reference it - so without this the window is collected and
		/// silently unregistered. The warmup compile allocates enough to make that a certainty rather than
		/// a race, and the symptom is invisible: the handle is logged, the message loop keeps running, and
		/// the window is simply gone from EnumWindows.
		/// </summary>
		private static ShutdownWindow shutdownWindow;

		private sealed class ShutdownWindow : System.Windows.Forms.NativeWindow
		{
			private const int WM_CLOSE = 0x0010;
			private const int WM_QUERYENDSESSION = 0x0011;
			private const int WM_ENDSESSION = 0x0016;

			protected override void WndProc(ref System.Windows.Forms.Message m)
			{
				switch (m.Msg)
				{
					case WM_QUERYENDSESSION:
						// Non-zero means "nothing here needs saving, go ahead". Returning without calling
						// base keeps DefWindowProc from answering for us.
						m.Result = (nint)1;
						return;

					case WM_ENDSESSION:
					case WM_CLOSE:
						Log("shutdown requested; exiting.");
						// The daemon holds nothing that needs unwinding - it is respawned on demand, and a lock
						// file left behind by a hard kill is already recognised as stale. Releasing ownership is
						// therefore a courtesy, and it is given a deliberately tiny slice of the shutdown budget:
						// the default five-second wait is exactly Windows' HungAppTimeout, so a contended mutex
						// here would get the daemon reported as hung and listed on the shutdown-blocking screen
						// as a captionless entry.
						try { DaemonCoordinator.ReleaseOwnership(TimeSpan.FromMilliseconds(250)); } catch { }

						Environment.Exit(0);
						return;
				}

				base.WndProc(ref m);
			}
		}

#endif

		private static bool WaitForConnection(NamedPipeServerStream server, TimeSpan timeout)
		{
			var task = server.WaitForConnectionAsync();

			if (task.Wait(timeout))
				return true;

			// Unblock the pending async accept so the stream can be disposed cleanly.
			try { server.Dispose(); } catch { }

			return false;
		}

		// Wire format (length-prefixed, BinaryReader/Writer):
		//   request : int32 protocolVersion, string scriptPath
		//   response: bool success,
		//             if success -> int32 byteLen, byte[] assemblyBytes, string warnings ("" when none)
		//             else        -> string errorMessage
		// `warnings` carries the script's #Warning text to the client: the daemon compiles in a detached process
		// whose stderr goes nowhere, so a warning printed here would be lost on the path most runs take.
		private static void HandleRequest(NamedPipeServerStream server, IScriptCompiler ch, string exeDir)
		{
			using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
			using var writer = new BinaryWriter(server, Encoding.UTF8, leaveOpen: true);

			var clientProtocol = reader.ReadInt32();

			if (clientProtocol != ProtocolVersion)
			{
				writer.Write(false);
				writer.Write($"Protocol mismatch: server={ProtocolVersion}, client={clientProtocol}.");
				return;
			}

			var scriptPath = reader.ReadString();

			var sw = Stopwatch.StartNew();
			var nameNoExt = scriptPath == "*" ? "pipestdin" : Path.GetFileNameWithoutExtension(scriptPath);

			byte[] bytes;
			string error;
			string warnings = null;

			try
			{
				var compilation = ch.Compile(new ScriptCompileRequest
				{
					SourceText = scriptPath == "*" ? scriptPath : null,
					ScriptPath = scriptPath == "*" ? null : scriptPath,
					CompilationName = nameNoExt,
					RuntimeDirectory = exeDir,
					Output = ScriptCompilationOutput.InMemory,
					// A shared background process does no network I/O for a caller it cannot see: an unrestored
					// #Package fails here, and the client reports it (--validate) or recompiles in-process.
					AllowPackageRestore = false,
				});
				bytes = compilation.AssemblyBytes;
				error = compilation.ErrorText;
				warnings = compilation.WarningText;
			}
			catch (Exception ex)
			{
				bytes = null;
				error = $"Compiling script failed.\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
			}

			sw.Stop();

			if (bytes != null)
			{
				Log($"compiled '{scriptPath}' ({bytes.Length} bytes) in {sw.ElapsedMilliseconds} ms.");
				writer.Write(true);
				writer.Write(bytes.Length);
				writer.Write(bytes);
				writer.Write(warnings ?? "");
			}
			else
			{
				Log($"compile error for '{scriptPath}' in {sw.ElapsedMilliseconds} ms.");
				writer.Write(false);
				writer.Write(error ?? "Unknown compile error.");
			}

			writer.Flush();

#if WINDOWS
			server.WaitForPipeDrain();
#endif
		}
	}

	/// <summary>
	/// Thin client for the compile server. Connects to (and, on request, spawns) a daemon with a matching
	/// build fingerprint, and returns compiled assembly bytes so the caller can run them via the lean path.
	/// </summary>
	internal static class CompileClient
	{
		// How long to wait for a freshly spawned daemon to finish warmup and start listening before giving up
		// and compiling in-process instead. The warmup is one Roslyn compile, ~2 s, so this is generous; the
		// cap exists so that a daemon which never becomes healthy cannot hold the user's script hostage. It
		// was 30 s, which is long enough that a stuck daemon looks like a hang.
		private static readonly TimeSpan SpawnWaitTimeout = TimeSpan.FromSeconds(10);

		/// <summary>
		/// Compiles <paramref name="scriptPath"/> via a running daemon, spawning one and waiting for it when
		/// none is reachable. Returns <see cref="CompileDaemonStatus.Unreachable"/> if no daemon can be
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
		/// pipe until warmup has finished, so no client can reach a half-initialised daemon; it just fails to
		/// connect and keeps polling.
		/// </summary>
		internal static CompileDaemonStatus CompileViaServer(string scriptPath, out byte[] bytes, out string error, out string warnings)
		{
			var status = TryCompile(scriptPath, out bytes, out error, out warnings, connectTimeoutMs: 300);

			if (status != CompileDaemonStatus.Unreachable)
				return status;

			if (!TrySpawnServer(out error))
				return CompileDaemonStatus.Unreachable;

			// Poll until the daemon (ours, or one another client spawned at the same moment) begins listening.
			var sw = Stopwatch.StartNew();

			while (sw.Elapsed < SpawnWaitTimeout)
			{
				status = TryCompile(scriptPath, out bytes, out error, out warnings, connectTimeoutMs: 500);

				if (status != CompileDaemonStatus.Unreachable)
					return status;

				Thread.Sleep(200);
			}

			error ??= "Compile server did not become ready in time.";
			return CompileDaemonStatus.Unreachable;
		}

		/// <summary>
		/// Attempts a single compile against an already-running daemon. Returns
		/// <see cref="CompileDaemonStatus.Unreachable"/> (no exception) when no daemon is listening.
		/// </summary>
		internal static CompileDaemonStatus TryCompile(string scriptPath, out byte[] bytes, out string error, out string warnings, int connectTimeoutMs = 1000)
		{
			bytes = null;
			error = null;
			warnings = null;

			using var client = new NamedPipeClientStream(".", CompileServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

			try
			{
				client.Connect(connectTimeoutMs);
			}
			catch (TimeoutException)
			{
				return CompileDaemonStatus.Unreachable;
			}
			catch (IOException)
			{
				return CompileDaemonStatus.Unreachable;
			}

			using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
			using var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);

			try
			{
				writer.Write(CompileServer.ProtocolVersion);
				writer.Write(scriptPath == "*" ? "*" : Path.GetFullPath(scriptPath));
				writer.Flush();

				if (reader.ReadBoolean())
				{
					var len = reader.ReadInt32();
					bytes = reader.ReadBytes(len);
					warnings = reader.ReadString();
					return CompileDaemonStatus.Compiled;
				}

				error = reader.ReadString();
				return CompileDaemonStatus.CompileFailed;
			}
			catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
			{
				// The daemon went away mid-request (e.g. it was replaced by a different build, or it idled
				// out between connect and reply). Treat as unreachable so the caller can spawn/fall back.
				bytes = null;
				error = null;
				return CompileDaemonStatus.Unreachable;
			}
		}

		// Launches "<this host> --daemon" detached so it outlives the current process. The spawned daemon runs
		// DaemonCoordinator.TryBecomeOwner at startup, which kills any different-build daemon and defers to an
		// identical one, so racing spawns converge on a single owner.
#if WINDOWS

		private const int STD_INPUT_HANDLE = -10;
		private const int STD_OUTPUT_HANDLE = -11;
		private const int STD_ERROR_HANDLE = -12;
		private const int HANDLE_FLAG_INHERIT = 1;

		[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
		private static extern nint GetStdHandle(int nStdHandle);

		[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetHandleInformation(nint hObject, out int lpdwFlags);

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
				if (handle == 0 || handle == -1)
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

		// Only Windows needs this. On Unix, .NET opens descriptors close-on-exec, so an exec'd child does not
		// inherit anything beyond the three standard ones it is explicitly given.
		private static IDisposable SuppressStandardHandleInheritance() => null;

#endif

		private static bool TrySpawnServer(out string error)
		{
			error = null;

			try
			{
				var processPath = Environment.ProcessPath;

				if (string.IsNullOrEmpty(processPath))
				{
					error = "Cannot determine process path to spawn compile server.";
					return false;
				}

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

				// The daemon must not inherit this process's standard handles. It outlives us by up to four
				// hours, so a handle it keeps open is a pipe that never reaches end-of-stream: piping a
				// Keysharp run and reading to EOF hangs long after the script exited (`keysharp x.ks | more`
				// never returns), and redirecting to a file leaves that file locked.
				//
				// Redirecting the child's streams does NOT fix this, which is worth recording because it is
				// the obvious thing to try. .NET calls CreateProcess with bInheritHandles=TRUE, so the child
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
				using (SuppressStandardHandleInheritance())
					return Process.Start(psi) != null;
			}
			catch (Exception ex)
			{
				error = $"Failed to spawn compile server: {ex.Message}";
				return false;
			}
		}
	}
}
