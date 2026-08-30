namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for real thread-related functions.
	/// These differ than the pseudo-threads used throughout the rest of the library.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// A real operating-system thread running its own Keysharp event loop, as opposed to the cooperative
		/// pseudo-threads (<see cref="KeysharpThread"/>) that the rest of the library schedules.
		/// <para>
		/// Three kinds of object share this class. A <em>worker</em> is one this class started
		/// (<c>RealThread(fn)</c>) and is the only kind with a body to wait for or a lifetime this class ends.
		/// <c>RealThread.Main</c> is the script's main thread, and <c>A_RealThread</c> on any other thread that
		/// runs script code is an <em>adopted</em> thread. Neither of the latter has either, so
		/// <see cref="Task"/>, <see cref="Terminated"/> and <see cref="Exit"/> report a <c>TargetError</c> on
		/// them; everything else works the same way, which is what makes <c>RealThread.Main.Post(fn)</c> the
		/// supported way to marshal work back to the main thread.</para>
		/// <para>
		/// A worker has two completions. <see cref="Task"/> is the entry function's result and settles the moment
		/// the body leaves, before the event loop is entered; <see cref="Terminated"/> settles once the OS thread
		/// is gone. A worker that registered a timer or a hotkey keeps serving them long after its body
		/// returned.</para>
		/// <para>
		/// Exactly one object exists per real thread, so identity comparison answers which kind one is:
		/// <c>rt == RealThread.Main</c>. There is deliberately no <c>IsMain</c> property for that.</para>
		/// </summary>
		public sealed class RealThread : KeysharpObject
		{
			internal const string NotAWorkerMain =
				"The main real thread has no body and cannot be ended here. Use ExitApp to end the script.";
			internal const string NotAWorkerAdopted =
				"This real thread was not started by RealThread, so it has no body and no end this class controls.";

			private readonly Script owner;

			// The entry function's result, settled as the body leaves. Null for the main and adopted threads,
			// which have no body, and that null is what distinguishes them throughout this class.
			private readonly TaskCompletionSource<object> entryCompletion;
			// Settled in RunWorkerLoop's finally: scheduler disposed, thread about to end.
			private readonly TaskCompletionSource<object> terminationCompletion;
			// Resolved by the worker once its scheduler exists. For main/adopted threads it is already resolved.
			private readonly TaskCompletionSource<ScriptEventScheduler> schedulerSource;
			// KeysharpTask.Wrap registers a task with the unobserved-failure hook, so these are wrapped up front
			// rather than on first read.
			private readonly KeysharpTask entryTask;
			private readonly KeysharpTask terminatedTask;
			// Posts queued and not yet settled. A queue entry can be discarded without ever running (ClearQueues at
			// teardown), and its task still has to be settled or an Await on it never returns.
			private readonly ConcurrentDictionary<PostRequest, byte> pendingPosts;

			/// <summary>Creates a worker object. Its thread is started separately by <see cref="Start"/>.</summary>
			private RealThread(Script owner)
			{
				this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
				entryCompletion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				terminationCompletion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				schedulerSource = new TaskCompletionSource<ScriptEventScheduler>(TaskCreationOptions.RunContinuationsAsynchronously);
				pendingPosts = new ConcurrentDictionary<PostRequest, byte>();
				entryTask = KeysharpTask.Wrap(entryCompletion.Task);
				terminatedTask = KeysharpTask.Wrap(terminationCompletion.Task);
			}

			/// <summary>Creates the object for an already-running thread this class did not start.</summary>
			private RealThread(ScriptEventScheduler scheduler)
			{
				owner = scheduler?.Owner ?? throw new ArgumentNullException(nameof(scheduler));
				schedulerSource = new TaskCompletionSource<ScriptEventScheduler>(TaskCreationOptions.RunContinuationsAsynchronously);
				pendingPosts = new ConcurrentDictionary<PostRequest, byte>();
				_ = schedulerSource.TrySetResult(scheduler);
			}

			private bool IsWorker => entryCompletion != null;

			// ---- construction ---------------------------------------------------------------------------------

			/// <summary>
			/// Runs a function object on a new real thread. Extra arguments are passed to the function.
			/// </summary>
			/// <param name="Callback">The function object to run.</param>
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

			/// <summary>
			/// True while the operating-system thread has not terminated. This is the thread's lifetime, not its
			/// body's: a worker whose entry function returned long ago is still alive while it serves the timers or
			/// hotkeys that function registered. <c>Task.IsPending</c> is the question about the body.
			/// </summary>
			public bool IsAlive => IsWorker ? !terminationCompletion.Task.IsCompleted : GetAliveScheduler() != null;

			/// <summary>
			/// The entry function's eventual result. It settles as the body leaves — succeeded with its return
			/// value, failed with its error, or canceled if <see cref="Exit"/> ended it first — so the value is
			/// readable immediately, whether or not the thread stays up afterwards.
			/// </summary>
			public object Task => IsWorker ? entryTask : ReportNotAWorker();

			/// <summary>
			/// Completes when this thread has terminated. Reading it asks nothing of the thread, so it is the way
			/// to wait for a worker that ends on its own; <see cref="Exit"/> requests shutdown and returns this
			/// same task.
			/// </summary>
			public object Terminated => IsWorker ? terminatedTask : ReportNotAWorker();

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
			/// Queues work onto this thread and returns immediately with the <c>Task</c> that carries its result.
			/// Ignore the task for fire-and-forget, <c>Then</c> it to react, or <c>Await</c> it to wait.
			/// <para>
			/// This is the queued form, so an uninterruptible target or an exhausted <c>#MaxThreads</c> defers the
			/// work rather than refusing it. <see cref="Send"/> is the synchronous form and differs in more than
			/// blocking — see its remarks.</para>
			/// </summary>
			public object Post(object Callback, params object[] Arguments)
			{
				var fo = Functions.GetKeysharpFunc(Callback, null, true);
				var scheduler = GetAliveScheduler();

				if (scheduler == null)
					return ReportThreadNotAlive();

				var request = new PostRequest(this, scheduler, fo, Arguments);
				pendingPosts[request] = 0;

				if (!scheduler.Enqueue(ScriptEventQueue.Normal, 0, request.Execute))
				{
					request.Settle(ThreadNotAlive);
					return ReportThreadNotAlive();
				}

				return request.Task;
			}

			/// <summary>
			/// Runs work on this thread, blocks until it finishes, and returns its value; an error there is
			/// re-thrown here.
			/// <para>
			/// A call to your own thread is a direct call, the main thread is reached through the UI framework, and
			/// the request is served past queued work whose launch is parked — after which an uninterruptible
			/// target refuses it outright. Use this when a busy target should fail fast, and
			/// <c>Await(Post(...))</c> when it should be waited for.</para>
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
						var executionResult = RunOnSchedulerThread(scheduler, () => fo.Call(Arguments), out var result, out _);
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
			/// Asks this thread to shut down and returns <see cref="Terminated"/>, so <c>Await(worker.Exit())</c>
			/// stops it and waits. Work already queued on it is abandoned, a pseudo-thread currently running on it
			/// unwinds when it next processes events, and the thread's event loop then stops. This is cooperative:
			/// it does not asynchronously abort managed code. Called on the thread itself
			/// (<c>A_RealThread.Exit()</c>) the current pseudo-thread exits immediately and this does not return,
			/// so the task is unobservable there.
			/// </summary>
			/// <param name="ExitCode">The process exit code to apply to the pseudo-threads being exited. Default: 0.</param>
			public object Exit(object ExitCode = null)
			{
				if (!IsWorker)
					return ReportNotAWorker();

				// Exit is a request, so the entry task is left to the body, which settles it as it unwinds;
				// RunWorkerLoop's finally covers a body that never ran at all.
				var scheduler = GetAliveScheduler();

				if (scheduler == null)//Already gone: shutting down what has shut down is not an error.
					return terminatedTask;

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

				return terminatedTask;
			}

			public override string ToString() => "RealThread";

			// ---- worker plumbing ------------------------------------------------------------------------------

			private void Start(Func<object> body)
			{
				// A live worker is a persistence root: nothing else covers the window between the body returning,
				// which pops its pseudo-thread, and the event loop unwinding.
				owner.AdjustPendingSchedulerWork(1);
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
					SettleEntry(scheduler, body);
					scheduler.RunWorkerEventLoop();
					return DefaultObject;
				}
				catch (Exception ex)
				{
					// A failure outside the body; the body settles its own task above. Reported here because the
					// task this runs on is unobserved, so a rethrow would fault it and .NET would drop the
					// exception at GC. HandleCaughtException also swallows a UserRequestedExitException, which is
					// an ordinary end.
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
					return DefaultObject;
				}
				finally
				{
					// Unblock anything waiting on the scheduler before it ever appeared: completing with null makes
					// GetAliveScheduler report not-alive, whereas faulting this source would create a second task
					// nobody observes.
					_ = schedulerSource.TrySetResult(scheduler);
					// The body may never have run at all (the scheduler failed to appear), so the entry task still
					// needs an answer before anything awaiting it is released by the termination below.
					_ = entryCompletion.TrySetCanceled();

					try
					{
						scheduler?.DisposeWorker();
					}
					finally
					{
						SynchronizationContext.SetSynchronizationContext(previousContext);
						_ = terminationCompletion.TrySetResult(DefaultObject);
						script.AdjustPendingSchedulerWork(-1);
						script.ExitIfNotPersistent();
					}
				}
			}

			/// <summary>
			/// Runs the entry body and settles <see cref="entryCompletion"/> from its outcome, before the event
			/// loop is entered. The error goes to whoever awaits the task; one nobody ever looks at is reported by
			/// the unobserved-task hook, on the same terms every other task in the script is.
			/// </summary>
			private void SettleEntry(ScriptEventScheduler scheduler, Func<object> body)
			{
				try
				{
					var launchResult = RunOnSchedulerThread(scheduler, body, out var bodyResult, out var bodyExited);

					if (launchResult != ScriptEventExecutionResult.Executed)
						_ = entryCompletion.TrySetException(
								new Error($"Unable to start RealThread worker body ({launchResult}).").Exception);
					else if (bodyExited)
						_ = entryCompletion.TrySetCanceled();
					else
						_ = entryCompletion.TrySetResult(bodyResult);
				}
				catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException(ex, out Flow.UserRequestedExitException _))
				{
					_ = entryCompletion.TrySetCanceled();
					throw;
				}
				catch (Exception ex)
				{
					_ = entryCompletion.TrySetException(ex);
				}
			}

			/// <summary>
			/// Runs <paramref name="work"/> as a pseudo-thread on the calling scheduler's thread. An exception
			/// other than an exit request propagates to the caller, which decides whether it faults a task
			/// (<see cref="Post"/>, the entry body) or is re-thrown at a synchronous caller (<see cref="Send"/>).
			/// </summary>
			private static ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler,
				Func<object> work, out object result, out bool exited)
			{
				result = DefaultObject;
				exited = false;
				using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.RealThread);

				if (!thread.Started)
					return thread.Result;

				try
				{
					result = work();
				}
				catch (Exception ex) when (Keysharp.Internals.Flow.TryGetException(ex,
					out Flow.UserRequestedExitException _))
				{
					// Exit() ends this pseudo-thread, not the process, so it simply has no result.
					exited = true;
				}

				return ScriptEventExecutionResult.Executed;
			}

			/// <summary>
			/// Fails every post whose queue entry will never run. Called from the scheduler teardown that clears
			/// the queue, so the guarantee that a post task always settles holds for adopted and main threads too.
			/// </summary>
			internal void FailPendingPosts()
			{
				foreach (var request in pendingPosts.Keys)
					request.Settle("The real thread shut down before this work could run.");
			}

			internal void ForgetPost(PostRequest request) => _ = pendingPosts.TryRemove(request, out _);

			/// <summary>
			/// This thread's scheduler, or null when there is none to work with. A worker that has been launched but
			/// has not yet published its scheduler is a race of microseconds and is waited out.
			/// </summary>
			private ScriptEventScheduler GetAliveScheduler()
			{
				var scheduler = schedulerSource.Task.GetAwaiter().GetResult();
				return scheduler == null || scheduler.IsDisposed ? null : scheduler;
			}

			internal bool OwnsCurrentThread()
			{
				var scheduler = schedulerSource.Task;
				return scheduler.Status == TaskStatus.RanToCompletion && scheduler.Result is { } s && s.OwnsCurrentThread;
			}

			/// <summary>The entry task of this object, without wrapping, for the self-wait guard.</summary>
			internal Task EntryTask => entryCompletion?.Task;

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

			private const string ThreadNotAlive = "Real thread is no longer alive.";

			private object ReportThreadNotAlive(object ret = null)
				=> Errors.ErrorOccurred(ThreadNotAlive, ret);

			private object ReportNotAWorker(object ret = null)
				=> Errors.TargetErrorOccurred(IsMainThread ? NotAWorkerMain : NotAWorkerAdopted, ret);

			/// <summary>
			/// One queued <see cref="Post"/>. It owns the task the caller holds and is responsible for settling it
			/// exactly once on every path, including the ones where the queue discards the entry without ever
			/// running it.
			/// </summary>
			internal sealed class PostRequest
			{
				private readonly RealThread thread;
				private readonly ScriptEventScheduler scheduler;
				private readonly KeysharpFunc callback;
				private readonly object[] args;
				private readonly TaskCompletionSource<object> completion =
					new(TaskCreationOptions.RunContinuationsAsynchronously);
				private readonly KeysharpTask task;

				internal PostRequest(RealThread thread, ScriptEventScheduler scheduler, KeysharpFunc callback, object[] args)
				{
					this.thread = thread;
					this.scheduler = scheduler;
					this.callback = callback;
					this.args = args;
					task = KeysharpTask.Wrap(completion.Task);
					task.MarkPostTarget(scheduler);
				}

				internal KeysharpTask Task => task;

				internal void Settle(string reason)
				{
					if (completion.TrySetException(new Error(reason).Exception))
						thread.ForgetPost(this);
				}

				internal ScriptEventExecutionResult Execute()
				{
					ScriptEventExecutionResult result;
					object value = DefaultObject;
					var exited = false;

					try
					{
						result = RunOnSchedulerThread(scheduler, () => callback.Call(args), out value, out exited);
					}
					catch (Exception ex)
					{
						_ = completion.TrySetException(ex);
						thread.ForgetPost(this);
						return ScriptEventExecutionResult.Executed;
					}

					// A blocked launch is parked and this runs again later, so it must not settle: only a result
					// which will not be retried is final.
					if (result is ScriptEventExecutionResult.GlobalBlocked or ScriptEventExecutionResult.LocalBlocked)
						return result;

					if (result != ScriptEventExecutionResult.Executed)
						_ = completion.TrySetException(
								new Error("The real thread dropped this work without running it.").Exception);
					else if (exited)
						_ = completion.TrySetCanceled();
					else
						_ = completion.TrySetResult(value);

					thread.ForgetPost(this);
					return result;
				}
			}
		}
	}
}
