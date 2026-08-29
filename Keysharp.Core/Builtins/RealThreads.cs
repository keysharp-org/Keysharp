namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for real thread-related functions.
	/// These differ than the pseudo-threads used throughout the rest of the library.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// Runs a function object inside of a lock statement.
		/// This is useful for calling a function inside of a real thread.
		/// </summary>
		/// <param name="LockObject">The object to lock on when calling the function. Must be an object: a number
		/// is boxed afresh at each call site, which would produce a different monitor every time and therefore
		/// no mutual exclusion at all.</param>
		/// <param name="Callback">The name of the function or a function object.</param>
		/// <param name="Arguments">The arguments to pass to the function.</param>
		/// <returns>The object the function object returned.</returns>
		public static object LockRun(object LockObject, object Callback, params object[] Arguments)
		{
			if (LockObject is null || LockObject.GetType().IsValueType)
				return Errors.TypeErrorOccurred(LockObject, typeof(object));

			lock (LockObject)
			{
				var funcObj = Functions.GetKeysharpFunc(Callback, null, true);
				return funcObj.Call(funcObj.Inst == null ? Arguments : new[] { funcObj.Inst }.Concat(Arguments));
			}
		}

		/// <summary>
		/// A real operating-system thread running its own Keysharp event loop, as opposed to the cooperative
		/// pseudo-threads (<see cref="KeysharpThread"/>) that the rest of the library schedules.
		/// <para>
		/// Three kinds of object share this class. A <em>worker</em> is one this class started
		/// (<c>RealThread(fn)</c>) and is the only kind that can be waited on or shut down. <c>RealThread.Main</c>
		/// is the script's main thread, and <c>A_RealThread</c> on any other thread that runs script code is an
		/// <em>adopted</em> thread. Both of the latter have no completion to wait for, so
		/// <see cref="Wait"/>, <see cref="ContinueWith"/> and <see cref="Exit"/> report a <c>TargetError</c> on
		/// them; everything else works the same way, which is what makes
		/// <c>RealThread.Main.Post(fn)</c> the supported way to marshal work back to the main thread.</para>
		/// <para>
		/// Exactly one object exists per real thread, so identity comparison answers which kind one is:
		/// <c>rt == RealThread.Main</c>. There is deliberately no <c>IsMain</c> property for that;
		/// <see cref="IsActive"/> says whether a thread has not yet finished.</para>
		/// </summary>
		public sealed class RealThread : KeysharpObject
		{
			private readonly Script owner;

			// Completed by the worker loop when the worker has finished. Null for the main and adopted threads,
			// which never complete, and is what distinguishes them throughout this class.
			private readonly TaskCompletionSource<object> completion;
			// Resolved by the worker once its scheduler exists. For main/adopted threads it is already resolved.
			private readonly TaskCompletionSource<ScriptEventScheduler> schedulerSource;
			// False until this worker's thread has actually been launched. A ContinueWith continuation exists as an
			// object long before that, and waiting on its scheduler would block the caller for the whole of the
			// antecedent's run; this lets that state be reported instead of stalled on.
			private int launchState;
			private int outcome;
			private object result = DefaultObject;

			private const int LaunchPending = 0;
			private const int LaunchStarted = 1;
			private const int LaunchCanceled = 2;
			private const int OutcomeActive = 0;
			private const int OutcomeSucceeded = 1;
			private const int OutcomeFailed = 2;
			private const int OutcomeCanceled = 3;

			/// <summary>Creates a worker object. Its thread is started separately by <see cref="Start"/>.</summary>
			private RealThread(Script owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
				completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				schedulerSource = new TaskCompletionSource<ScriptEventScheduler>(TaskCreationOptions.RunContinuationsAsynchronously);
			}

			/// <summary>Creates the object for an already-running thread this class did not start.</summary>
			private RealThread(ScriptEventScheduler scheduler)
			{
				owner = scheduler?.Owner ?? throw new ArgumentNullException(nameof(scheduler));
				launchState = LaunchStarted;
				schedulerSource = new TaskCompletionSource<ScriptEventScheduler>(TaskCreationOptions.RunContinuationsAsynchronously);
				_ = schedulerSource.TrySetResult(scheduler);
			}

			/// <summary>
			/// This worker's completion, or null for the main and adopted threads, which never finish. Internal
			/// rather than public on purpose: <c>Await</c> and <c>Task.WhenAll</c>/<c>WhenAny</c> take a
			/// RealThread through this, but a script-visible <c>Task</c> property would be a second view of the
			/// same body which answers differently from <see cref="Result"/> about whether it failed.
			/// <see cref="ToClr"/> is the deliberate escape hatch to that view.
			/// </summary>
			internal Task<object> Task => completion?.Task;

			/// <summary>
			/// This worker's completion task as an ordinary <c>Ks.Clr</c> object. Unlike <see cref="Result"/> it
			/// answers as a raw CLR <c>Task</c> does — a failed body reads as faulted, not as a stored
			/// <c>Error</c> value. The main and adopted threads have no completion and report a
			/// <c>TargetError</c>, as <see cref="Wait"/> does.
			/// </summary>
			public object ToClr() => completion == null
				? Errors.TargetErrorOccurred(KeysharpTask.NoBodyToWaitFor)
				: ManagedInvoke.WrapManaged(completion.Task);

			private bool IsWorker => completion != null;

			// ---- construction ---------------------------------------------------------------------------------

			/// <summary>
			/// Runs a function object on a new real thread. Extra arguments are passed to the function.
			/// </summary>
			/// <param name="Callback">The name of the function or a function object.</param>
			/// <param name="Arguments">The arguments to pass to the function.</param>
			/// <returns>The <see cref="RealThread"/> object.</returns>
			public static object staticCall(object @this, object Callback, params object[] Arguments)
			{
				var funcObj = Functions.GetKeysharpFunc(Callback, null, true);
				var rt = new RealThread(Script.TheScript);
				rt.Start(() => funcObj.Call(Arguments));
				return rt;
			}

			/// <summary>The script's main thread (script: <c>RealThread.Main</c>).</summary>
			public static object staticget_Main(object @this) => MainInstance();

			/// <summary>
			/// The <see cref="RealThread"/> for the calling thread, creating and caching it on that thread's
			/// scheduler on first use. Backs <c>A_RealThread</c>.
			/// </summary>
			internal static RealThread ForCurrentThread()
			{
				var script = Script.TheScript;
				return script.IsOnMainThread ? MainInstance(script) : Bind(script.ThreadScheduler);
			}

			private static RealThread MainInstance(Script script = null)
			{
				script ??= Script.TheScript;
				var scheduler = script.uiEventScheduler;
				return scheduler == null ? null : Bind(scheduler);
			}

			/// <summary>
			/// Returns the object already bound to <paramref name="scheduler"/>, or binds a new one. A scheduler is
			/// owned by a single real thread, but <c>RealThread.Main</c> can be read from any of them, so the
			/// install is done with a CAS to keep exactly one object per thread.
			/// </summary>
			private static RealThread Bind(ScriptEventScheduler scheduler)
			{
				var existing = Volatile.Read(ref scheduler.realThread);

				if (existing != null)
					return existing;

				var created = new RealThread(scheduler);
				return Interlocked.CompareExchange(ref scheduler.realThread, created, null) ?? created;
			}

			// ---- properties -----------------------------------------------------------------------------------

			/// <summary>
			/// Gets the managed thread id of the worker backing this real thread.
			/// </summary>
			public long Id => GetAliveScheduler()?.OwnerManagedThreadId ?? (long)ReportThreadNotAlive(0L);

			/// <summary>True while this thread has not finished.</summary>
			public bool IsActive => !HasFinished;

			/// <summary>True only when this worker finished successfully.</summary>
			public bool IsSuccessful => HasOutcome(OutcomeSucceeded);

			/// <summary>True only when this worker finished because of an error.</summary>
			public bool IsFailed => HasOutcome(OutcomeFailed);

			/// <summary>True only when <see cref="Exit"/> ended this worker before its body returned.</summary>
			public bool IsCanceled => HasOutcome(OutcomeCanceled);

			/// <summary>
			/// The value this thread's body returned, or an empty string while it is still running, if it ended by
			/// throwing, or if it exited early. An uncaught error is reported on the thread where it happened, in
			/// the same way as one in a timer or hotkey, so it is never delivered here.
			/// </summary>
			public object Result => IsSuccessful ? result : DefaultObject;

			// The outcome is published with a full fence before the completion is set, so any reader that sees the
			// task completed also sees the terminal outcome.
			private bool HasFinished => IsWorker && Task.IsCompleted;
			private bool HasOutcome(int expected) => HasFinished && Volatile.Read(ref outcome) == expected;

			/// <summary>
			/// The active pseudo-threads of this real thread as an <see cref="Array"/>, oldest first, so
			/// <c>Threads[1]</c> is the one that has been running longest. The array is a snapshot taken when the
			/// property is read.
			/// </summary>
			public object Threads
			{
				get
				{
					var mgr = GetAliveScheduler()?.threadManager;

					if (mgr == null)
						return ReportThreadNotAlive(new Keysharp.Builtins.Array());

					var snapshot = mgr.SnapshotPseudoThreads();
					var wrappers = new List<object>(snapshot.Count);

					foreach (var tv in snapshot)
						wrappers.Add(Keysharp.Builtins.KeysharpThread.Wrap(mgr, tv));

					return new Keysharp.Builtins.Array(wrappers);
				}
			}

			// ---- work submission ------------------------------------------------------------------------------

			/// <summary>
			/// Encapsulates a call to <see cref="Task.ContinueWith()"/>.
			/// </summary>
			/// <param name="Callback">The name of the function or a function object.</param>
			/// <param name="Arguments">The arguments to pass to the function.</param>
			/// <returns>The new <see cref="RealThread"/> object</returns>
			public object ContinueWith(object Callback, params object[] Arguments)
			{
				if (!IsWorker)
					return ReportNotAWorker();

				var fo = Functions.GetKeysharpFunc(Callback, null, true);
				var rt = new RealThread(owner);
				_ = Task.ContinueWith(_ => rt.Start(() => fo.Call(Arguments)), TaskScheduler.Default);
				return rt;
			}

			/// <summary>
			/// Queues work onto this worker thread and returns immediately.
			/// </summary>
			public object Post(object Callback, params object[] Arguments)
			{
				var fo = Functions.GetKeysharpFunc(Callback, null, true);
				var scheduler = GetAliveScheduler();

				if (scheduler == null)
					return ReportThreadNotAlive();

				var queuedEvent = new PostQueuedEvent(scheduler, fo, Arguments);

				if (!scheduler.Enqueue(ScriptEventQueue.Normal, 0, queuedEvent.Execute))
					return ReportThreadNotAlive();

				return DefaultObject;
			}

			/// <summary>
			/// Executes work on this worker thread synchronously and returns the callback result.
			/// </summary>
			public object Send(object Callback, params object[] Arguments)
			{
				var fo = Functions.GetKeysharpFunc(Callback, null, true);
				var scheduler = GetAliveScheduler();

				if (scheduler == null)
					return ReportThreadNotAlive();

				try
				{
					return scheduler.InvokeSynchronous(() =>
					{
						var executionResult = RunOnSchedulerThread(scheduler, () => fo.Call(Arguments), false,
							out var result, out _);
						return executionResult == ScriptEventExecutionResult.Executed
							? result
							: Errors.ErrorOccurred("Unable to execute callback on RealThread.");
					});
				}
				catch (ObjectDisposedException)
				{
					return ReportThreadNotAlive();
				}
			}

			// ---- lifetime -------------------------------------------------------------------------------------

			/// <summary>
			/// Waits for this thread to finish, pumping events while it waits.
			/// </summary>
			/// <param name="Timeout">The time to wait in milliseconds. Default: wait indefinitely.</param>
			/// <returns>True if the thread finished before the timeout; false if the timeout elapsed first. Read
			/// <see cref="Result"/> for the value the body returned.</returns>
			public object Wait(object Timeout = null)
			{
				if (!IsWorker)
					return ReportNotAWorker(false);

				if (OwnsCurrentThread())
					return Errors.TargetErrorOccurred("A real thread cannot wait on itself.", false);

				return Keysharp.Internals.Flow.WaitForCompletion(Task, Timeout.Ai(-1));
			}

			/// <summary>
			/// Asks this thread to shut down. Work already queued on it is abandoned, a pseudo-thread currently
			/// running on it unwinds when it next processes events, and the thread's event loop then stops. This is
			/// cooperative: it does not asynchronously abort managed code. Called on the thread itself
			/// (<c>A_RealThread.Exit()</c>) the current pseudo-thread exits immediately and this does not return.
			/// </summary>
			/// <param name="ExitCode">The process exit code to apply to the pseudo-threads being exited. Default: 0.</param>
			public object Exit(object ExitCode = null)
			{
				if (!IsWorker)
					return ReportNotAWorker();

				if (HasFinished)
					return DefaultObject;

				if (Interlocked.CompareExchange(ref launchState, LaunchCanceled, LaunchPending) == LaunchPending)
				{
					_ = Interlocked.Exchange(ref outcome, OutcomeCanceled);
					_ = schedulerSource.TrySetResult(null);
					_ = completion.TrySetResult(DefaultObject);
					return DefaultObject;
				}

				_ = Interlocked.CompareExchange(ref outcome, OutcomeCanceled, OutcomeActive);

				var scheduler = GetAliveScheduler();

				if (scheduler == null)//Already gone: shutting down what has shut down is not an error.
					return DefaultObject;

				var code = ExitCode.Ai();
				scheduler.RequestWorkerExit();
				var mgr = scheduler.threadManager;

				if (mgr != null)
				{
					foreach (var tv in mgr.SnapshotPseudoThreads())
					{
						// The worker owns this stack and may pop and reuse a slot while we walk the snapshot, so
						// re-read the ID and skip a slot that has since been recycled or released. The write itself
						// still races with that thread by construction; what this bounds is which pseudo-thread the
						// exit code can land on — one of this worker's, which is the thread being shut down anyway.
						var id = Volatile.Read(ref tv.pseudoThreadId);

						if (id != 0L)
							tv.requestedExitCode = code;
					}
				}

				if (scheduler.OwnsCurrentThread)
					owner.Threads.ThrowIfExitRequested(owner.Threads.CurrentThread);

				return DefaultObject;
			}

			public override string ToString() => "RealThread";

			// ---- worker plumbing ------------------------------------------------------------------------------

			private void Start(Func<object> body)
			{
				if (Interlocked.CompareExchange(ref launchState, LaunchStarted, LaunchPending) != LaunchPending)
					return;

				_ = System.Threading.Tasks.Task.Factory.StartNew(() => RunWorkerLoop(body), CancellationToken.None,
						TaskCreationOptions.LongRunning, TaskScheduler.Default);
			}

			private object RunWorkerLoop(Func<object> body)
			{
				var script = owner;
				ScriptEventScheduler scheduler = null;
				var previousContext = SynchronizationContext.Current;

				try
				{
					scheduler = script.ThreadScheduler;
					scheduler.realThread = this;
					_ = schedulerSource.TrySetResult(scheduler);
					SynchronizationContext.SetSynchronizationContext(scheduler.DispatchContext);
					var launchResult = RunOnSchedulerThread(scheduler, body, true, out var bodyResult,
						out var bodyExited);

					if (launchResult == ScriptEventExecutionResult.Executed)
					{
						if (bodyExited)
							_ = Interlocked.CompareExchange(ref outcome, OutcomeCanceled, OutcomeActive);
						else
						{
							result = bodyResult;
							_ = Interlocked.CompareExchange(ref outcome, OutcomeSucceeded, OutcomeActive);
						}
					}
					else
					{
						if (launchResult != ScriptEventExecutionResult.Dropped)//Dropped means the body reported its own error.
							_ = Errors.ErrorOccurred($"Unable to start RealThread worker body ({launchResult}).");

						_ = Interlocked.Exchange(ref outcome, OutcomeFailed);
					}

					scheduler.RunWorkerEventLoop();
					return result;
				}
				catch (Exception ex)
				{
					// Reaching here means the failure was outside the body (which reports its own errors). Report it
					// here rather than rethrowing: the task this runs on is deliberately not observed by anything, so
					// a rethrow would fault it and .NET would drop the exception silently at GC — the exact hazard
					// this class was reworked to remove. HandleCaughtException also swallows a UserRequestedExitException
					// (an Exit that unwound the event loop), which is an ordinary end, not an error.
					var exited = Keysharp.Internals.Flow.TryGetException<Keysharp.Builtins.Flow.UserRequestedExitException>(ex, out _);

					if (exited)
						_ = Interlocked.CompareExchange(ref outcome, OutcomeCanceled, OutcomeActive);
					else
						_ = Interlocked.Exchange(ref outcome, OutcomeFailed);
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
					return result;
				}
				finally
				{
					// Unblock anything waiting on the scheduler before it ever appeared: completing with null makes
					// GetAliveScheduler report not-alive, whereas faulting this source would create a second task
					// nobody observes.
					_ = schedulerSource.TrySetResult(scheduler);

					try
					{
						scheduler?.DisposeWorker();
						script.ExitIfNotPersistent();
					}
					finally
					{
						SynchronizationContext.SetSynchronizationContext(previousContext);
						_ = completion.TrySetResult(result);
					}
				}
			}

			/// <summary>
			/// Runs <paramref name="work"/> as a pseudo-thread on the calling scheduler's thread.
			/// </summary>
			/// <param name="handleExceptions">When true, an uncaught error is reported through the standard script
			/// error path on this thread, exactly as one thrown by a timer or hotkey body, and the result is
			/// <see cref="ScriptEventExecutionResult.Dropped"/>. This is what keeps a failing RealThread body from
			/// disappearing into an unobserved task.</param>
			private static ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler,
				Func<object> work, bool handleExceptions, out object result, out bool exited)
			{
				result = DefaultObject;
				exited = false;
				using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.RealThread);

				if (!thread.Started)
					return thread.Result;

				if (!handleExceptions)
				{
					result = work();
					return ScriptEventExecutionResult.Executed;
				}

				try
				{
					result = work();
				}
				catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException(ex,
					out Flow.UserRequestedExitException _))
				{
					// Exit() ends this pseudo-thread, not the process, so it simply has no result.
					exited = true;
					return ScriptEventExecutionResult.Executed;
				}
				catch (Exception ex)
				{
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
					return ScriptEventExecutionResult.Dropped;
				}

				return ScriptEventExecutionResult.Executed;
			}

			/// <summary>
			/// This thread's scheduler, or null when there is none to work with. A worker that has been launched but
			/// has not yet published its scheduler is a race of microseconds and is waited out; one that has not been
			/// launched at all — a <see cref="ContinueWith"/> continuation whose antecedent is still running — is
			/// reported rather than waited on, because that wait would last as long as the antecedent and would block
			/// the caller's thread without pumping.
			/// </summary>
			private ScriptEventScheduler GetAliveScheduler()
			{
				if (Volatile.Read(ref launchState) != LaunchStarted)
					return null;

				var scheduler = schedulerSource.Task.GetAwaiter().GetResult();
				return scheduler == null || scheduler.IsDisposed ? null : scheduler;
			}

			private bool OwnsCurrentThread()
			{
				var scheduler = schedulerSource.Task;
				return scheduler.Status == TaskStatus.RanToCompletion && scheduler.Result is { } s && s.OwnsCurrentThread;
			}

			/// <summary>True when this object is the script's main thread, which owns the UI scheduler.</summary>
			private bool IsMainThread
			{
				get
				{
					var scheduler = schedulerSource.Task;
					return scheduler.Status == TaskStatus.RanToCompletion
						   && ReferenceEquals(scheduler.Result, owner.uiEventScheduler);
				}
			}

			private object ReportThreadNotAlive(object ret = null)
				=> Errors.ErrorOccurred(IsWorker && Volatile.Read(ref launchState) == LaunchPending
						? "Real thread has not started yet."
						: "Real thread is no longer alive.", ret);

			private object ReportNotAWorker(object ret = null)
				=> Errors.TargetErrorOccurred(IsMainThread
						? "The main real thread cannot be waited on, continued or exited. Use ExitApp to end the script."
						: "This real thread was not started by RealThread and cannot be waited on, continued or exited.",
						ret);

			private sealed class PostQueuedEvent(ScriptEventScheduler scheduler, KeysharpFunc callback, object[] args)
			{
				internal ScriptEventExecutionResult Execute()
					=> RunOnSchedulerThread(scheduler, () => callback.Call(args), true, out _, out _);
			}
		}
	}
}
