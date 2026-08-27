using Keysharp.Builtins;
namespace Keysharp.Internals.Threading
{
	public class Threads
	{
		private readonly Script script;

		/// <summary>
		/// Each thread has its own TVM. This means the main UI thread gets one, and worker threads get their own.
		/// This allows the hook thread to run #HotIf evaluations separately without interfering with the main thread.
		/// It could conceivably lead to more threads being in existence than the user allowed, for a very brief moment.
		/// This shouldn't be a problem though.
		/// Note that we use ThreadLocal<T> here because it allows initialization for each thread, whereas
		/// [ThreadStatic] doesn't.
		/// Always add 1 because a dummy entry is always added in the constructor.
		/// Add the emergency reserve so synchronous unbufferable emergencies can exceed the normal limit safely.
		/// </summary>
		private readonly ThreadLocal<ThreadVariableManager> tvm;

		// The top of the current real thread's pseudo-thread stack. Derived from tvm on each read (a single
		// ThreadLocal access plus a stack peek); there is intentionally no separate cache field, because every
		// caller funnels through here and a stale cache would be a correctness hazard, not a speedup.
		internal ThreadVariables CurrentThread => EnsureCurrentThreadVariables();

		private ThreadVariableManager ThreadVariableManagerForCurrentThread => tvm.Value;

		internal ThreadVariables UnderlyingThread => ThreadVariableManagerForCurrentThread.threadVars.TryPeekSecond();
		internal int ActivePseudoThreadCount => ThreadVariableManagerForCurrentThread.PseudoThreadCount;

		/// <summary>
		/// Whether timers may run in the current pseudo-thread. This is the runtime's own state, so the
		/// scheduler and Flow read it here; the script-facing <c>Ks.A_AllowTimers</c> wraps this property.
		/// </summary>
		internal bool AllowTimers
		{
			get => CurrentThread?.configData.allowTimers ?? true;
			set
			{
				if (CurrentThread is { } tv)
					tv.configData.allowTimers = value;
			}
		}

		internal Threads(Script script)
		{
			this.script = script ?? throw new ArgumentNullException(nameof(script));
			var tvmSize = (int)script.MaxThreadsTotal + Script.maxEmergencyThreads + 1;
			//No trackAllValues: nothing enumerates them, and tracking would pin every thread's manager for as long
			//as this Script lives -- one per thread per create-dispose cycle in a host that makes many.
			tvm = new ThreadLocal<ThreadVariableManager>(() => new ThreadVariableManager(script, tvmSize));
			_ = EnsureCurrentThreadVariables();
		}

		internal ThreadVariables EnsureCurrentThreadVariables()
		{
			var tvmLocal = ThreadVariableManagerForCurrentThread;

			//Ensure there is always one thread in existence for reference purposes, but do not increment the actual thread counter.
			return tvmLocal.threadVars.Index != 0
				   ? tvmLocal.threadVars.TryPeek()
				   : tvmLocal.PushThreadVariables(0, true, false);
		}

		public bool TryBeginThread(out ThreadVariables tv)
		{
			var skip = script.FlowData.allowInterruption == false;
			return TryPushThreadVariables(0, skip, false, true, false, out tv);
		}

		public bool TryBeginEmergencyThread(out ThreadVariables tv)
		{
			var skip = script.FlowData.allowInterruption == false;
			return TryPushThreadVariables(0, skip, false, true, true, out tv);
		}

		internal bool TryReserveThreadCount(bool allowEmergencyOverflow = false)
		{
			while (true)
			{
				var existingCount = Volatile.Read(ref script.totalExistingThreads);

				if (existingCount >= script.MaxThreadsTotal)
				{
					if (!allowEmergencyOverflow)
						return false;

					if (existingCount >= script.MaxThreadsTotal + Script.maxEmergencyThreads)
						return false;
				}

				if (Interlocked.CompareExchange(ref script.totalExistingThreads, existingCount + 1, existingCount) == existingCount)
					return true;
			}
		}

		public void EndThread(ThreadVariables tv, bool checkThread = false)
		{
			if (tv == null)
				return;

			tv.task = false;

			PopThreadVariables(tv, checkThread);
			_ = Interlocked.Decrement(ref script.totalExistingThreads);

			script.EventScheduler.SchedulePump();
			script.ScheduleBlockedEventSchedulers();
		}

		internal bool TryPushThreadVariables(long priority, bool skipUninterruptible,
				bool isCritical, bool inc, bool allowEmergencyOverflow, out ThreadVariables tv, ThreadKind kind = ThreadKind.None)
		{
			tv = null;

			if (inc)
			{
				if (!TryReserveThreadCount(allowEmergencyOverflow))
					return false;
			}

			tv = ThreadVariableManagerForCurrentThread.PushThreadVariables(priority, skipUninterruptible, isCritical, kind);

			if (tv == null)
			{
				// Roll back counter (if we incremented it) and undo pause
				if (inc)
					_ = Interlocked.Decrement(ref script.totalExistingThreads);

				return false;
			}

			// Park the interrupted thread's executing-function scope on the pushed pseudo-thread and start fresh, so
			// an interrupting timer/hotkey body never inherits or leaks it (restored in PopThreadVariables). The scope
			// itself is [ThreadStatic] on Script; this is the rare interrupt boundary, not the hot call path.
			tv.savedExecScope = Script.executingUserFunc;
			Script.executingUserFunc = null;

			//We successfully pushed—and if inc == true, we’ve already counted it
			tv.task = true;
			return true;
		}

		internal bool AnyThreadsAvailable()
		{
			return Volatile.Read(ref script.totalExistingThreads) < script.MaxThreadsTotal;
		}

		/// <summary>
		/// Requests that <paramref name="targetThread"/> exit with <paramref name="exitCode"/>. The current
		/// pseudo-thread exits immediately (by throw); any other one is marked and unwinds when it next reaches an
		/// exit-enabled TryDoEvents safe point. A later request wins until the target actually exits.
		/// </summary>
		/// <returns>The targeted pseudo-thread's ID.</returns>
		internal long RequestExit(ThreadVariables targetThread, int exitCode)
		{
			if (targetThread == null)
				return 0L;

			targetThread.requestedExitCode = exitCode;

			if (ReferenceEquals(targetThread, ThreadVariableManagerForCurrentThread.threadVars.TryPeek()))
				ThrowIfExitRequested(targetThread);

			return targetThread.pseudoThreadId;
		}

		/// <summary>Requests that the current pseudo-thread exit. Does not return.</summary>
		internal long RequestExit(int exitCode)
			=> RequestExit(ThreadVariableManagerForCurrentThread.threadVars.TryPeek(), exitCode);

		/// <summary>The pseudo-thread stack manager of the calling real thread.</summary>
		internal ThreadVariableManager CurrentManager => ThreadVariableManagerForCurrentThread;

		/// <summary>
		/// The script-visible <c>KeysharpThread</c> for the current pseudo-thread (<c>A_Thread</c>), created on first
		/// read and cached on the pooled <see cref="ThreadVariables"/> so repeated reads do not allocate.
		/// </summary>
		internal Keysharp.Builtins.KeysharpThread CurrentThreadObject
			=> Keysharp.Builtins.KeysharpThread.Wrap(ThreadVariableManagerForCurrentThread, CurrentThread);

		internal void ThrowIfExitRequested(ThreadVariables tv)
		{
			if (tv.requestedExitCode is not int exitCode)
				return;

			Accessors.A_ExitReason = exitCode;
			Environment.ExitCode = exitCode;
			throw new Keysharp.Builtins.Flow.UserRequestedExitException();
		}

		internal bool IsInterruptible()
		{
			if (!script.FlowData.allowInterruption)
				return false;

			if (Volatile.Read(ref script.totalExistingThreads) == 0)//Before _ks_UserMainCode() starts to run.1
				return true;

			var tv = CurrentThread;

			if (!tv.isCritical//Added this whereas AHK doesn't check it. We should never make a critical thread interruptible.
					&& !tv.allowThreadToBeInterrupted // Those who check whether g->AllowThreadToBeInterrupted==false should then check whether it should be made true.
					&& tv.UninterruptibleDuration > -1 // Must take precedence over the below.  g_script.mUninterruptibleTime is not checked because it's supposed to go into effect during thread creation, not after the thread is running and has possibly changed the timeout via 'Thread "Interrupt"'.
					&& Environment.TickCount64 - tv.threadStartTick >= tv.UninterruptibleDuration// See big comment section above.
					&& !script.FlowData.callingCritical // In case of "Critical" on the first line.  See v2.0 comment above.
			   )
			{
				// Once the thread becomes interruptible by any means, g->ThreadStartTime/UninterruptibleDuration
				// can never matter anymore because only Critical (never "Thread Interrupt") can turn off the
				// interruptibility again, and it resets g->UninterruptibleDuration.
					tv.allowThreadToBeInterrupted = true; // Avoids issues with 49.7 day limit of 32-bit TickCount, and also helps performance future callers of this function (they can skip most of the checking above).

					if (!tv.isCritical)
						tv.configData.peekFrequency = ThreadVariables.DefaultPeekFrequency;
				}

			return tv.allowThreadToBeInterrupted;
		}

		internal void LaunchThreadInMain(Action act, long priority = 0, bool skipUninterruptible = false,
							 bool isCritical = false, ThreadKind kind = ThreadKind.None)
		{
			try
			{
				script.UIEventScheduler.EnqueueThreadLaunch(priority, skipUninterruptible, isCritical, act, kind);
			}
			catch (Exception ex) when (ex.InnerException is not null)
			{
				ExceptionDispatchInfo.Throw(ex.InnerException);
			}
		}

		internal void PopThreadVariables(ThreadVariables tv, bool checkThread = false)
		{
			ThreadVariableManagerForCurrentThread.PopThreadVariables(tv, checkThread);
			// Restore the executing-function scope captured when this pseudo-thread was pushed (see
			// TryPushThreadVariables), so the interrupted thread resumes with its own scope.
			Script.executingUserFunc = tv.savedExecScope;
		}
	}
}
