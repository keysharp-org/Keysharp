namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A .NET <see cref="System.Threading.Tasks.Task"/> given a face a script can use. Scripts know it as
		/// <c>Task</c>; the CLR type is named differently because <c>Task</c> is already taken in C#, the same
		/// reason <c>Lock</c> is <see cref="KeysharpLock"/> and <c>Func</c> is <c>KeysharpFunc</c>.
		/// <para>
		/// The one thing here a script could not write for itself is <see cref="Then"/>: a continuation which
		/// runs on the owning script thread and yields a further <c>Task</c> carrying the callback's own
		/// outcome. <see cref="Result"/> is a snapshot and does not wait, matching
		/// <see cref="RealThread.Result"/>; <c>Await</c> and <see cref="Wait"/> are the waiting forms.</para>
		/// <para>
		/// <see cref="Wrap"/> is the only producer, so exactly one wrapper exists per underlying task and
		/// <c>t1 == t2</c> answers "the same work".</para>
		/// </summary>
		[UserDeclaredName("Task")]
		public class KeysharpTask : KeysharpObject
		{
			// One wrapper per underlying task. ConvertOut runs at every CLR->script crossing, so without this a
			// script would get a different object for the same work each time and identity would mean nothing.
			private static readonly ConditionalWeakTable<Task, KeysharpTask> wrappers = new();
			// Task<T>.Result is reached by reflection (T is not known here) and Result is read once per element
			// of a WhenAll, so the accessor is resolved once per closed type rather than per read.
			private static readonly ConcurrentDictionary<Type, Func<Task, object>> resultReaders = new();
			// The marker the BCL uses for "an async method with no result". Resolved by name because it is
			// internal to the framework; null simply means nothing is filtered.
			private static readonly Type voidTaskResultType = typeof(Task).Assembly.GetType("System.Threading.Tasks.VoidTaskResult");
			private static int unobservedHooked;

			private readonly Task task;
			// The scheduler owning the thread this task was first seen on. Continuations go here rather than to
			// whatever SynchronizationContext happens to be current, which is what gives a Then handler a real
			// pseudo-thread with correct A_* state.
			private readonly ScriptEventScheduler ownerScheduler;
			private Error mappedError;

			private KeysharpTask(Task task, ScriptEventScheduler owner)
			{
				this.task = task;
				ownerScheduler = owner;
			}

			/// <summary>
			/// Wraps a task or value task reached some other way. One returned from a CLR call is wrapped
			/// already, so this is only for one a script got hold of by another route.
			/// </summary>
			public static object staticCall(object @this, object value)
			{
				if (value is RealThread { Task: null })
					return Errors.TargetErrorOccurred(NoBodyToWaitFor);

				var t = FromScriptValue(value);
				return t == null ? Errors.TypeErrorOccurred(value, typeof(KeysharpTask)) : Wrap(t);
			}

			// The main and adopted threads are not workers, so every entry point which accepts a RealThread says
			// the same thing about them rather than reporting a type error about Task.
			internal const string NoBodyToWaitFor = "The main and adopted real threads have no body to wait for.";

			/// <summary>Returns the one wrapper for <paramref name="t"/>, creating it on first crossing.</summary>
			internal static KeysharpTask Wrap(Task t)
			{
				if (t == null)
					return null;

				if (wrappers.TryGetValue(t, out var existing))
					return existing;

				EnsureUnobservedHook();
				// The owner is resolved inside the factory so it belongs to the wrapper which actually wins:
				// ConditionalWeakTable runs the factory outside its lock, so several threads can each build one
				// and all but one are discarded. Nothing may happen in the constructor for the same reason -- a
				// side effect there would be performed once per loser and never undone.
				return wrappers.GetValue(t, key => new KeysharpTask(key, Script.TheScript?.EventScheduler));
			}

			/// <summary>The underlying <see cref="Task"/>, for runtime code that needs it rather than the wrapper.</summary>
			internal Task Underlying => task;

			// ---- properties -------------------------------------------------------------------------------

			/// <summary>
			/// <c>"Running"</c> until the work finishes, then <c>"Done"</c>, <c>"Error"</c> or <c>"Canceled"</c>.
			/// Cancellation comes from a <c>CancellationToken</c> the script passed into the call which produced
			/// this task; there is deliberately no <c>Cancel()</c> here, because .NET cannot cancel a task it did
			/// not create.
			/// </summary>
			public string Status =>
				!task.IsCompleted ? "Running"
				: task.IsCanceled ? "Canceled"
				: task.IsFaulted ? "Error"
				: "Done";

			/// <summary>
			/// The value this task produced, or an empty string while it is still running, if it failed, or if it
			/// produced none. This is a snapshot and never waits — <c>Await(task)</c> and <see cref="Wait"/> are
			/// the waiting forms.
			/// </summary>
			public object Result => task.IsCompletedSuccessfully ? ReadResult(task) : DefaultObject;

			/// <summary>
			/// The failure as a catchable <c>Error</c> object once this task has failed, otherwise an empty
			/// string. It is the same object <c>Await</c> would have thrown, so a script reads <c>Error.Message</c>
			/// the same way whichever route it took.
			/// </summary>
			public object Error
			{
				get
				{
					if (!task.IsFaulted)
						return DefaultObject;

					// Mapped once so repeated reads hand back the same object, which is what lets a script compare
					// or store it, and so a read costs nothing after the first.
					return mappedError ??= ManagedInvoke.MapException(Unwrap(task.Exception), "Task");
				}
			}

			/// <summary>
			/// The underlying CLR task as an ordinary <c>Ks.Clr</c> object, so members this class does not mirror
			/// — <c>IsCompleted</c>, <c>Exception</c>, <c>ContinueWith</c> — stay reachable.
			/// </summary>
			public object Clr => ManagedInvoke.WrapManaged(task);

			// ---- waiting ----------------------------------------------------------------------------------

			/// <summary>
			/// Waits for this task to finish, pumping events while it waits so timers, hotkeys and the GUI stay
			/// responsive. It does not throw on failure — read <see cref="Status"/> and <see cref="Error"/>.
			/// </summary>
			/// <param name="timeout">Milliseconds to wait. Default: wait indefinitely.</param>
			/// <returns>True if the task finished, false if the timeout elapsed first.</returns>
			public object Wait(object timeout = null) => Keysharp.Internals.Flow.WaitForTask(task, timeout.Ai(-1));

			// ---- continuation -----------------------------------------------------------------------------

			/// <summary>
			/// Runs <paramref name="callback"/> once this task finishes, without blocking. The callback receives
			/// this <c>Task</c>, and may declare no parameter at all if it does not want it.
			/// <para>
			/// It runs on the script thread which owns this task, in its own pseudo-thread, so <c>A_*</c>
			/// variables, <c>Critical</c> and GUI access all behave normally.</para>
			/// </summary>
			/// <returns>
			/// A new <c>Task</c> carrying the callback's own outcome — its return value, or its failure — so a
			/// chain reports what went wrong rather than swallowing it.
			/// </returns>
			public object Then(object callback)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, null, true);
				var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				_ = task.ContinueWith((_, state) => ((KeysharpTask)state).Dispatch(fo, completion), this,
									  CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
				return Wrap(completion.Task);
			}

			// ---- combinators ------------------------------------------------------------------------------

			/// <summary>
			/// A task that finishes when every one of <paramref name="tasks"/> has finished, producing an
			/// <see cref="Array"/> of their results in the order they were given. If any of them fails, so does
			/// this one, so <c>Await</c> on it reports the failure.
			/// </summary>
			public static object staticWhenAll(object @this, params object[] tasks)
			{
				var underlying = Underlyings(tasks);

				if (underlying == null)
					return DefaultObject;

				var all = Task.WhenAll(underlying);
				var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				_ = all.ContinueWith(t => Settle(completion, t, () =>
				{
					var results = new List<object>(underlying.Length);

					foreach (var u in underlying)
						results.Add(ReadResult(u));

					return new Array(results);
				}), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
				return Wrap(completion.Task);
			}

			/// <summary>
			/// A task that finishes as soon as the first of <paramref name="tasks"/> finishes, producing that
			/// one's outcome — its value, or its failure. The others keep running; nothing here cancels them.
			/// </summary>
			public static object staticWhenAny(object @this, params object[] tasks)
			{
				var underlying = Underlyings(tasks);

				if (underlying == null)
					return DefaultObject;

				// A loser which fails still has to have its failure looked at, or it surfaces as an uncaught script
				// error at collection long after the race was decided.
				foreach (var u in underlying)
					_ = u.ContinueWith(static x => _ = x.Exception, CancellationToken.None,
									   TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
									   TaskScheduler.Default);

				var any = Task.WhenAny(underlying);
				var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				// WhenAny's own task never fails -- it succeeds carrying whichever task won, however that one
				// ended. So the winner's outcome has to be unpacked here; reading its Result blindly would throw
				// inside the continuation and leave this completion unset, hanging every waiter forever.
				_ = any.ContinueWith(t => Settle(completion, t.Result, () => ReadResult(t.Result)),
									 CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
				return Wrap(completion.Task);
			}

			/// <summary>
			/// Creates a task the script settles itself. This is what turns a callback-shaped API -- a hotkey, a
			/// GUI event, a device notification -- into something <see cref="staticWhenAny"/> can race against
			/// real work, and it is the one shape a script cannot otherwise build.
			/// </summary>
			public static object staticSource(object @this) => new TaskSource();

			public override string ToString() => "Task";

			// ---- internals --------------------------------------------------------------------------------

			/// <summary>
			/// Copies <paramref name="settled"/>'s outcome onto <paramref name="completion"/>, projecting a value
			/// only when it actually succeeded. Any throw from <paramref name="project"/> faults the completion
			/// rather than escaping the continuation, where it would strand every waiter.
			/// </summary>
			private static void Settle(TaskCompletionSource<object> completion, Task settled, Func<object> project)
			{
				try
				{
					if (settled.IsFaulted)
						_ = completion.TrySetException(Unwrap(settled.Exception));
					else if (settled.IsCanceled)
						_ = completion.TrySetCanceled();
					else
						_ = completion.TrySetResult(project());
				}
				catch (Exception ex)
				{
					_ = completion.TrySetException(ex);
				}
			}

			/// <summary>
			/// Reports a failure nobody ever looked at, on the same terms .NET does: at collection, once the task
			/// can no longer be observed. Reporting at fault time instead would give the script a window of zero
			/// -- <c>t := Foo()</c> on one line and <c>Await(t)</c> on the next would race it -- and would fire
			/// for every loser of a <c>WhenAny</c>, where a failure is the expected outcome.
			/// </summary>
			private static void EnsureUnobservedHook()
			{
				if (Interlocked.Exchange(ref unobservedHooked, 1) != 0)
					return;

				TaskScheduler.UnobservedTaskException += (sender, e) =>
				{
					var scheduler = GetUnobservedScheduler(sender);

					// This handler is process-wide, so it sees tasks belonging to a host as well. Marking one
					// observed is a claim that it has been reported; leave the ones this script will not report
					// to whoever else is listening.
					if (scheduler == null)
						return;

					e.SetObserved();
					var ex = Unwrap(e.Exception);
					_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
					{
						using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

						if (!thread.Started)
							return thread.Result;

						_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
						return ScriptEventExecutionResult.Executed;
					});
				};
			}

			internal static ScriptEventScheduler GetUnobservedScheduler(object sender)
			{
				if (sender is not Task task
						|| !wrappers.TryGetValue(task, out var wrapper)
						|| wrapper.ownerScheduler is not { IsDisposed: false } scheduler
						|| scheduler.Owner is not { hasExited: false, IsDisposed: false })
					return null;

				return scheduler;
			}

			/// <summary>
			/// Runs a <see cref="Then"/> callback on the owning script thread. Same rule as a CLR event handler:
			/// inline when we are already there, otherwise queued as a normal pseudo-thread on its owner. Never a
			/// synchronous hop onto the script thread -- a pool thread blocking on a script thread deadlocks
			/// whenever that script thread is itself inside a CLR call.
			/// </summary>
			private void Dispatch(KeysharpFunc fo, TaskCompletionSource<object> completion)
			{
				var scheduler = ownerScheduler ?? Script.TheScript?.EventScheduler;

				if (scheduler == null || scheduler.IsDisposed || Script.TheScript is not { hasExited: false })
				{
					_ = completion.TrySetResult(DefaultObject);
					return;
				}

				if (scheduler.OwnsCurrentThread)
				{
					RunCallback(fo, completion);
					return;
				}

				if (!scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
				{
					using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

					if (!thread.Started)
					{
						// GlobalBlocked and LocalBlocked ask for a retry -- the entry goes back on the queue and
						// runs later -- so settling here would resolve the chained task before the callback has
						// run at all, and under Critical that is the ordinary path rather than an edge case. Only
						// Dropped means it will never run.
						if (thread.Result == ScriptEventExecutionResult.Dropped)
							_ = completion.TrySetResult(DefaultObject);

						return thread.Result;
					}

					RunCallback(fo, completion);
					Script.TheScript.ExitIfNotPersistent();
					return ScriptEventExecutionResult.Executed;
				}))
					_ = completion.TrySetResult(DefaultObject);
			}

			private void RunCallback(KeysharpFunc fo, TaskCompletionSource<object> completion)
			{
				try
				{
					// Arity-trimmed: a handler which does not want the task may declare no parameter, the same
					// allowance CLR event handlers get.
					_ = completion.TrySetResult(fo.MaxParams == 0 && !fo.IsVariadic ? fo.Call() : fo.Call(this));
				}
				catch (Exception ex)
				{
					// The chained task carries the failure, so `Await(t.Then(cb))` reports it like any other call
					// instead of resolving to nothing. If nobody looks at that task, the unobserved hook reports
					// it, which is the same treatment any other dropped failure gets.
					_ = completion.TrySetException(ex);
				}
			}

			/// <summary>Accepts a Task, a wrapper, a RealThread, a ValueTask or a ManagedInstance over any of those.</summary>
			internal static Task FromScriptValue(object value)
			{
				switch (value)
				{
					case KeysharpTask kt:
						return kt.task;

					case Task t:
						return t;

					case RealThread rt:
						// Null for the main and adopted threads, which never finish. Callers report that in
						// RealThread's own wording rather than as a type error.
						return rt.Task;

					case Clr.ManagedInstance mi:
						return mi._instance is Task inner ? inner : AsTaskOrNull(mi._instance);
				}

				return AsTaskOrNull(value);
			}

			// ValueTask is a struct, so it is deliberately not auto-wrapped at the ConvertOut boundary; it is
			// accepted here instead, where the cost is paid once by a script which asked for it.
			private static Task AsTaskOrNull(object v) =>
				v switch
				{
					null => null,
					ValueTask vt => vt.AsTask(),
					_ => v.GetType() is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(ValueTask<>)
						 ? (Task)t.GetMethod(nameof(ValueTask.AsTask))!.Invoke(v, null)
						 : null
				};

			private static Task[] Underlyings(object[] tasks)
			{
				var list = new List<Task>(tasks?.Length ?? 0);

				if (tasks != null)
				{
					foreach (var t in tasks)
					{
						// Null rather than skipping the element: OnError can suppress the raise, and waiting on the
						// rest would report success for a set the script never asked for.
						if (t is RealThread { Task: null })
						{
							_ = Errors.TargetErrorOccurred(NoBodyToWaitFor);
							return null;
						}

						var u = FromScriptValue(t);

						if (u == null)
						{
							_ = Errors.TypeErrorOccurred(t, typeof(KeysharpTask));
							return null;
						}

						list.Add(u);
					}
				}

				return [.. list];
			}

			/// <summary>
			/// Reads <c>Task&lt;T&gt;.Result</c> without knowing T, converted for the script.
			/// <para>
			/// The runtime type is not a usable test on its own: an <c>async Task</c> method's state-machine box
			/// is a <c>Task&lt;VoidTaskResult&gt;</c>, so asking whether the type is generic answers yes for the
			/// most common async signature there is and hands the script that internal marker struct. The closed
			/// <c>Task&lt;T&gt;</c> base is what actually says whether there is a value.</para>
			/// </summary>
			internal static object ReadResult(Task t)
			{
				if (t == null)
					return DefaultObject;

				var reader = resultReaders.GetOrAdd(t.GetType(), static type =>
				{
					for (var b = type; b != null; b = b.BaseType)
					{
						if (!b.IsGenericType || b.GetGenericTypeDefinition() != typeof(Task<>))
							continue;

						if (b.GetGenericArguments()[0] == voidTaskResultType)
							return null;

						var getter = b.GetProperty(nameof(Task<int>.Result))!;
						return task => getter.GetValue(task);
					}

					return null;
				});

				if (reader == null)
					return DefaultObject;

				var value = reader(t);
				return value == null ? DefaultObject : ManagedInvoke.ConvertOut(value);
			}

			internal static Exception Unwrap(Exception ex) =>
				ex is AggregateException agg && agg.InnerExceptions.Count == 1 ? agg.InnerExceptions[0] : ex;
		}

		/// <summary>
		/// The settling half of a <see cref="KeysharpTask"/> the script owns: hand <see cref="Task"/> to whoever
		/// waits, then call <see cref="Resolve"/> or <see cref="Reject"/> when the answer arrives. Without it a
		/// script could only consume tasks the CLR produced, so a hotkey or GUI event could never be raced
		/// against real work in <c>Task.WhenAny</c>.
		/// <para>
		/// Settling is one-shot and idempotent: the first call decides the outcome and returns true, later ones
		/// return false without raising, which is what makes it safe from several handlers racing.</para>
		/// <para>
		/// <c>Task.Source()</c> is the spelling the docs use, so a single <c>#import KS { Task }</c> reaches the
		/// whole surface. Constructing this class directly does the same, as it does for any class.</para>
		/// </summary>
		public class TaskSource : KeysharpObject
		{
			private readonly TaskCompletionSource<object> completion =
				new(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskSource(params object[] args) : base(args) { }

			/// <summary>The task to hand to <c>Await</c>, <c>Then</c>, <c>WhenAll</c> or <c>WhenAny</c>.</summary>
			public object Task => KeysharpTask.Wrap(completion.Task);

			/// <summary>
			/// Finishes the task successfully with <paramref name="value"/>.
			/// </summary>
			/// <returns>True if this call settled the task, false if it was already settled.</returns>
			public object Resolve(object value = null) => completion.TrySetResult(value ?? DefaultObject);

			/// <summary>
			/// Finishes the task as failed. <paramref name="reason"/> may be an <see cref="Error"/> object, which
			/// is what <c>Await</c> rethrows and <c>Task.Error</c> hands back, or a string to describe one.
			/// </summary>
			/// <returns>True if this call settled the task, false if it was already settled.</returns>
			public object Reject(object reason = null) =>
				completion.TrySetException(reason switch
				{
					Error e => e.Exception,
					Exception ex => ex,
					null => new Error("The task was rejected.").Exception,
					_ => new Error(reason.As()).Exception
				});

			public override string ToString() => "TaskSource";
		}
	}
}
