namespace Keysharp.Main;

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
	private static readonly string LockFile = Path.Combine(CompileServer.RuntimeDirectory, $"keysharp-compile-server-{CompileServer.UserKey}.lock");
	private static readonly string MutexName = $@"Local\keysharp-compile-coord-{CompileServer.UserKey}";

	/// <summary>
	/// Whether the lock file names a live daemon serving <paramref name="pipeName"/>. Read without the coordination
	/// mutex, since it only decides whether a client waits for that daemon's pipe or first spawns a daemon.
	/// </summary>
	internal static bool HasLiveOwner(string pipeName, int? expectedPid = null) =>
		Read() is { } owner && (!expectedPid.HasValue || owner.Pid == expectedPid)
		&& string.Equals(owner.Pipe, pipeName, StringComparison.Ordinal) && IsLiveDaemon(owner);

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

			// Clients authenticate the pipe server against this ownership record.
			if (!Write(pipeName))
			{
				CompileServer.Log("could not record daemon ownership; exiting.");
				return false;
			}

			return true;
		}
		finally
		{
			Release(mutex, acquired);
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

			return !p.HasExited && string.Equals(p.ProcessName, owner.ProcName, StringComparison.OrdinalIgnoreCase) && StartStamp(p) == owner.StartStamp;
		}
		catch
		{
			return false;
		}
	}

	// Lock file is a single line: "pid|procName|startStamp|pipeName".
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
					&& long.TryParse(parts[2], out var startStamp))
				return new Owner { Pid = pid, ProcName = parts[1], StartStamp = startStamp, Pipe = parts[3] };
		}
		catch { }

		return null;
	}

	private static void Release(Mutex mutex, bool acquired)
	{
		if (acquired)
			try
			{ mutex.ReleaseMutex(); }
			catch { }
	}

	// Compared across processes, so it has to be exact. Linux derives Process.StartTime from a boot time which
	// each process computes from the wall clock, so two processes disagree on it by up to a clock tick; the
	// kernel's own stamp, in clock ticks since boot, is field 22 of the process's stat line.
	private static long StartStamp(Process p)
	{
#if LINUX
		var stat = File.ReadAllText($"/proc/{p.Id}/stat");
		// Field 2, the command name, is parenthesised and may itself contain spaces and parentheses.
		return long.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19]);
#else
		return p.StartTime.ToUniversalTime().Ticks;
#endif
	}

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

	private static bool Write(string pipeName)
	{
		try
		{
			using var self = Process.GetCurrentProcess();
			File.WriteAllText(LockFile, $"{Environment.ProcessId}|{self.ProcessName}|{StartStamp(self)}|{pipeName}");
			return true;
		}
		catch
		{
			return false;
		}
	}

	private sealed class Owner
	{
		internal int Pid;
		internal string Pipe;
		internal string ProcName;
		internal long StartStamp;
	}
}