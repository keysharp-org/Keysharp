namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A .NET <see cref="System.Threading.Tasks.Task"/> given a face a script can use. Scripts know it as
		/// <c>Task</c>; the CLR type is named differently because <c>Task</c> is already taken in C#, the same
		/// reason <c>Lock</c> is <see cref="KeysharpLock"/> and <c>Func</c> is <c>KeysharpFunc</c>.
		/// <para>
		/// <see cref="Then"/> runs a continuation on the calling script thread and yields a further <c>Task</c>
		/// carrying the callback's own outcome. <see cref="Result"/> is a snapshot and does not wait, matching
		/// <see cref="RealThread.Result"/>; <c>Await</c> and <see cref="Wait"/> are the waiting forms.</para>
		/// <para>
		/// <see cref="Wrap"/> is the only producer, so exactly one wrapper exists per underlying task in each
		/// script and <c>t1 == t2</c> answers "the same work".</para>
		/// </summary>
		[UserDeclaredName("Task")]
		public class KeysharpTask : KeysharpObject
		{
			// UnobservedTaskException is process-wide, so this table retains only a weak route back to the active
			// script which received each task. Wrapper identity itself is scoped to Script.
			private static readonly ConditionalWeakTable<Task, WeakReference<Script>> unobservedOwners = new();
			// Task<T>.Result is reached by reflection (T is not known here) and Result is read once per element
			// of a WhenAll, so the accessor is resolved once per closed type rather than per read.
			private static readonly ConcurrentDictionary<Type, Func<Task, object>> resultReaders = new();
			// The marker the BCL uses for "an async method with no result". Resolved by name because it is
			// internal to the framework; null simply means nothing is filtered.
			private static readonly Type voidTaskResultType = typeof(Task).Assembly.GetType("System.Threading.Tasks.VoidTaskResult");
			private static int unobservedHooked;

			private readonly Task task;
			private Error mappedError;

			private KeysharpTask(Task task) => this.task = task;

			/// <summary>
			/// Wraps a task or value task reached some other way. One returned from a CLR call is wrapped
			/// already, so this is only for one a script got hold of by another route.
			/// </summary>
			public static object staticCall(object @this, object Value)
			{
				var t = FromAwaitable(Value);
				return t == null ? Errors.TypeErrorOccurred(Value, typeof(KeysharpTask)) : Wrap(t);
			}

			// The main and adopted threads are not workers, so every entry point which accepts a RealThread says
			// the same thing about them rather than reporting a type error about Task.
			internal const string NoBodyToWaitFor = "The main and adopted real threads have no body to wait for.";

			/// <summary>Returns the one wrapper for <paramref name="t"/>, creating it on first crossing.</summary>
			internal static KeysharpTask Wrap(Task t)
			{
				if (t == null)
					return null;

				EnsureUnobservedHook();
				var script = Script.TheScript;

				if (script != null)
					unobservedOwners.GetValue(t, _ => new(script)).SetTarget(script);

				if (script == null)
					return new KeysharpTask(t);

				return script.TaskWrappers.GetValue(t, static key => new KeysharpTask(key));
			}

			/// <summary>The underlying <see cref="Task"/>, for runtime code that needs it rather than the wrapper.</summary>
			internal Task Underlying => task;

			// ---- properties -------------------------------------------------------------------------------

			/// <summary>True while the task has not reached any terminal outcome.</summary>
			public bool Active => !task.IsCompleted;

			/// <summary>True only when the task completed successfully.</summary>
			public bool Succeeded => task.IsCompletedSuccessfully;

			/// <summary>True only when the task completed with an error.</summary>
			public bool Failed => task.IsFaulted;

			/// <summary>True only when the task completed as canceled.</summary>
			public bool Canceled => task.IsCanceled;

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
			public object Error => task.IsFaulted ? GetMappedError() : DefaultObject;

			/// <summary>
			/// The underlying CLR task as an ordinary <c>Ks.Clr</c> object, so members this class does not mirror
			/// — <c>IsCompleted</c>, <c>Exception</c>, <c>ContinueWith</c> — stay reachable. CLR
			/// <c>ContinueWith</c> has neither <see cref="Then"/>'s script-thread affinity nor its result flattening.
			/// </summary>
			public object Clr => ManagedInvoke.WrapManaged(task);

			// ---- waiting ----------------------------------------------------------------------------------

			/// <summary>
			/// Waits for this task to finish, pumping events while it waits so timers, hotkeys and the GUI stay
			/// responsive. It does not throw on failure; read <see cref="Failed"/> and <see cref="Error"/>. A timeout
			/// stops only this wait and does not cancel the task.
			/// </summary>
			/// <param name="Timeout">Milliseconds to wait. Default: wait indefinitely.</param>
			/// <returns>True if the task finished, false if the timeout elapsed first.</returns>
			public object Wait(object Timeout = null)
			{
				if (IsCurrentRealThreadTask(task))
					return Errors.TargetErrorOccurred("A real thread cannot wait on itself.", false);

				return Keysharp.Internals.Flow.WaitForTask(task, Timeout.Ai(-1));
			}

			// ---- continuation -----------------------------------------------------------------------------

			/// <summary>
			/// Runs <paramref name="OnSuccess"/> after this task succeeds, or <paramref name="OnFailure"/> after it
			/// fails, without blocking. The selected callback receives the value or catchable <c>Error</c>, and may
			/// declare no parameter if it does not want it. With no failure callback, the same error propagates;
			/// cancellation invokes neither callback and propagates unchanged.
			/// <para>
			/// It runs on the script thread where <c>Then</c> was called, in its own pseudo-thread, so <c>A_*</c>
			/// variables, <c>Critical</c> and GUI access all behave normally.</para>
			/// </summary>
			/// <returns>
			/// A new <c>Task</c> carrying the selected callback's outcome. Native tasks and value tasks returned at
			/// any depth are adopted rather than nested; a directly returned <c>__Await()</c> object is adopted too. A
			/// <c>RealThread</c> follows <c>Await</c> semantics: its body error remains reported on that worker and the
			/// chain receives its <c>Result</c>.
			/// </returns>
			public object Then(object OnSuccess, object OnFailure = null)
			{
				var success = Functions.GetKeysharpFunc(OnSuccess, null, null, true);

				if (success == null)
					return DefaultObject;

				var successArity = Script.CallbackArgCounts(success, 1);

				if (successArity.Required > 1)
					return Errors.ValueErrorOccurred(
						$"Task.Then OnSuccess requires at least {successArity.Required} parameters, but receives at most one.");

				var failure = OnFailure == null ? null : Functions.GetKeysharpFunc(OnFailure, null, null, true);

				if (OnFailure != null && failure == null)
					return DefaultObject;

				if (failure != null)
				{
					var failureArity = Script.CallbackArgCounts(failure, 1);

					if (failureArity.Required > 1)
						return Errors.ValueErrorOccurred(
							$"Task.Then OnFailure requires at least {failureArity.Required} parameters, but receives at most one.");
				}

				var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				var continuation = new TaskContinuation(this, Script.TheScript?.EventScheduler, success, failure, completion);
				continuation.Attach(task);
				return Wrap(completion.Task);
			}

			// ---- combinators ------------------------------------------------------------------------------

			/// <summary>
			/// A task that finishes when every one of <paramref name="Tasks"/> has finished, producing an
			/// <see cref="Array"/> of their results in the order they were given. If any of them fails, so does
			/// this one, so <c>Await</c> on it reports the failure. If at least one is canceled and none fail, this
			/// task is canceled.
			/// </summary>
			public static object staticWhenAll(object @this, params object[] Tasks)
			{
				var underlying = Underlyings(Tasks);
				return underlying == null ? DefaultObject : Wrap(CompleteAll(underlying));
			}

			/// <summary>
			/// A task that finishes with the first outcome among <paramref name="Tasks"/>: the winner's value,
			/// failure or cancellation. The other tasks keep running and are not observed or canceled here.
			/// </summary>
			public static object staticWhenAny(object @this, params object[] Tasks)
			{
				var underlying = Underlyings(Tasks);

				if (underlying == null)
					return DefaultObject;

				if (underlying.Length == 0)
					return Errors.ValueErrorOccurred("Task.WhenAny requires at least one task.");

				return Wrap(CompleteFirst(underlying));
			}

			/// <summary>
			/// Creates a task and synchronously calls <paramref name="Producer"/> with the prefix it accepts of
			/// Succeed, Fail and Cancel settlement functions. The first settlement wins; the producer's return value
			/// is ignored.
			/// </summary>
			public static object staticCreate(object @this, object Producer)
			{
				var producer = Functions.GetKeysharpFunc(Producer, null, null, true);

				if (producer == null)
					return DefaultObject;

				var arity = Script.CallbackArgCounts(producer, 3);

				if (arity.Required > 3)
					return Errors.ValueErrorOccurred(
						$"Task.Create producer requires at least {arity.Required} parameters, but receives at most three.");

				var source = new TaskProducerCompletion();
				var args = new object[]
				{
					new KeysharpFunc((Func<object, object>)source.Succeed),
					new KeysharpFunc((Func<object, object>)source.Fail),
					new KeysharpFunc((Func<object>)source.Cancel)
				};

				try
				{
					_ = producer.Call(args[.. arity.Accepted]);
				}
				catch (Exception ex) when (TryGetUserExit(ex, out var exit))
				{
					_ = source.Cancel();
					throw exit;
				}
				catch (Exception ex)
				{
					_ = source.Fail(ex);
				}

				return Wrap(source.Task);
			}

			public override string ToString() => "Task";

			// ---- internals --------------------------------------------------------------------------------

			private static async Task<object> CompleteAll(Task[] tasks)
			{
				await Task.WhenAll(tasks).ConfigureAwait(false);
				return new Array(tasks.Select(ReadResult));
			}

			private static async Task<object> CompleteFirst(Task[] tasks)
			{
				var winner = await Task.WhenAny(tasks).ConfigureAwait(false);
				await winner.ConfigureAwait(false);
				return ReadResult(winner);
			}

			private sealed class TaskProducerCompletion
			{
				private readonly TaskCompletionSource<object> completion =
					new(TaskCreationOptions.RunContinuationsAsynchronously);
				private int settled;

				internal Task Task => completion.Task;

				internal object Succeed(object Value = null)
				{
					if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
						return false;

					ResolveCompletion(completion, Value);
					return true;
				}

				internal object Fail(object Reason = null)
				{
					if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
						return false;

					try
					{
						_ = completion.TrySetException(Reason switch
						{
							Error e => e.Exception,
							Exception ex => ex,
							null => new Error("The task failed.").Exception,
							_ => new Error(Reason.As()).Exception
						});
					}
					catch (Exception ex) when (TryGetUserExit(ex, out var exit))
					{
						_ = completion.TrySetCanceled();
						throw exit;
					}
					catch (Exception ex)
					{
						_ = completion.TrySetException(ex);
					}

					return true;
				}

				internal object Cancel()
				{
					if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
						return false;

					_ = completion.TrySetCanceled();
					return true;
				}

			}

			/// <summary>
			/// Reports a failure nobody ever looked at, on the same terms .NET does: at collection, once the task
			/// can no longer be observed. Reporting at fault time instead would give the script a window of zero
			/// -- <c>t := Foo()</c> on one line and <c>Await(t)</c> on the next would race it -- and would fire
			/// for work a combinator has not selected, where the caller may still choose to inspect it.
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

					var ex = Unwrap(e.Exception);
					if (scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
					{
						using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

						if (!thread.Started)
							return thread.Result;

						_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
						return ScriptEventExecutionResult.Executed;
					}))
						e.SetObserved();
				};
			}

			internal static ScriptEventScheduler GetUnobservedScheduler(object sender)
			{
				if (sender is not Task task
						|| !unobservedOwners.TryGetValue(task, out var owner)
						|| !owner.TryGetTarget(out var script)
						|| script is not { hasExited: false, IsDisposed: false }
						|| !ReferenceEquals(Script.TheScript, script)
						|| script.uiEventScheduler is not { IsDisposed: false } scheduler)
					return null;

				return scheduler;
			}

			private sealed class TaskContinuation
			{
				private const string OwnerUnavailable = "The Task continuation could not run because its owning script thread is no longer available.";
				private KeysharpTask antecedent;
				private ScriptEventScheduler scheduler;
				private KeysharpFunc successCallback;
				private KeysharpFunc failureCallback;
				private readonly TaskCompletionSource<object> completion;
				private readonly Action invalidated;

				internal TaskContinuation(KeysharpTask antecedent, ScriptEventScheduler scheduler,
					KeysharpFunc successCallback, KeysharpFunc failureCallback, TaskCompletionSource<object> completion)
				{
					this.antecedent = antecedent;
					this.scheduler = scheduler;
					this.successCallback = successCallback;
					this.failureCallback = failureCallback;
					this.completion = completion;
					invalidated = OwnerInvalidated;
				}

				internal void Attach(Task task)
				{
					if (scheduler == null || !scheduler.RegisterPendingCallback(invalidated))
					{
						Finish();
						return;
					}

					try
					{
						_ = task.ContinueWith(static (_, state) => ((TaskContinuation)state).Dispatch(), this,
							CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
					}
					catch (Exception ex)
					{
						Finish(ex);
					}
				}

				private void Dispatch()
				{
					var owner = Volatile.Read(ref scheduler);
					var source = Volatile.Read(ref antecedent);

					if (owner == null || source == null)
						return;

					var script = owner.Owner;

					try
					{
						if (source.Canceled || (source.Failed && Volatile.Read(ref failureCallback) == null))
						{
							if (source.Canceled)
								_ = completion.TrySetCanceled();
							else
								_ = completion.TrySetException(source.GetMappedError().Exception);

							Release();
							return;
						}

						if (owner.IsDisposed || script is not { hasExited: false, IsDisposed: false }
								|| !owner.Enqueue(ScriptEventQueue.Normal, 0, Run))
						{
							Finish();
						}
					}
					catch (Exception ex)
					{
						Finish(ex);
					}
					finally
					{
						if (!script.IsDisposed)
							script.ExitIfNotPersistent();
					}
				}

				private ScriptEventExecutionResult Run()
				{
					var owner = Volatile.Read(ref scheduler);
					var source = Volatile.Read(ref antecedent);
					var action = source?.Failed == true
						? Volatile.Read(ref failureCallback)
						: Volatile.Read(ref successCallback);

					if (owner == null || action == null || source == null)
						return ScriptEventExecutionResult.Dropped;

					var result = ScriptEventExecutionResult.Dropped;

					try
					{
						using (var thread = owner.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr))
						{
							result = thread.Result;

							if (!thread.Started)
							{
								if (result != ScriptEventExecutionResult.Dropped)
									return result;

								Fail();
							}
							else
							{
								result = ScriptEventExecutionResult.Executed;
								var argument = source.Failed ? source.GetMappedError() : source.Result;
								var value = Script.CallbackArgCount(action, 1) == 0
									? action.Call() : action.Call(argument);
								ResolveCompletion(completion, value);
							}
						}
					}
					catch (Exception ex) when (TryGetUserExit(ex, out _))
					{
						_ = completion.TrySetCanceled();
					}
					catch (Exception ex)
					{
						Fail(ex);
					}

					// Returned background work is not rooted; a downstream Then owns its own callback root.
					Release();

					if (!owner.Owner.IsDisposed)
						owner.Owner.ExitIfNotPersistent();

					return result;
				}

				private void Fail(Exception exception = null)
					=> _ = completion.TrySetException(exception ?? new InvalidOperationException(OwnerUnavailable));

				private void Finish(Exception exception = null)
				{
					Fail(exception);
					Release();
				}

				private void OwnerInvalidated() => Finish();

				private void Release()
				{
					var owner = Volatile.Read(ref scheduler);
					owner?.ReleasePendingCallback(invalidated);
					Volatile.Write(ref antecedent, null);
					Volatile.Write(ref scheduler, null);
					Volatile.Write(ref successCallback, null);
					Volatile.Write(ref failureCallback, null);
				}
			}

			private static void ResolveCompletion(TaskCompletionSource<object> completion, object value)
			{
				try
				{
					var nested = FromAwaitable(value);

					if (nested == null)
						_ = completion.TrySetResult(value ?? DefaultObject);
					else
						_ = CompleteFrom(completion, nested);
				}
				catch (Exception ex) when (TryGetUserExit(ex, out var exit))
				{
					_ = completion.TrySetCanceled();
					throw exit;
				}
				catch (Exception ex)
				{
					_ = completion.TrySetException(ex);
				}
			}

			private static async Task CompleteFrom(TaskCompletionSource<object> completion, Task nested)
			{
				try
				{
					var seen = new HashSet<Task>();

					while (true)
					{
						if (ReferenceEquals(nested, completion.Task) || !seen.Add(nested))
						{
							_ = completion.TrySetException(new InvalidOperationException("Task resolution cycle detected."));
							return;
						}

						try
						{
							await nested.ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							if (nested.IsCanceled || (nested.IsFaulted && IsSoleUserExit(nested.Exception)))
								_ = completion.TrySetCanceled();
							else if (nested.IsFaulted)
								_ = completion.TrySetException(Wrap(nested).GetMappedError().Exception);
							else
								_ = completion.TrySetException(ex);

							return;
						}

						var value = ReadRawResult(nested);

						if (value is RealThread { Task: null })
						{
							_ = completion.TrySetException(new TargetError(NoBodyToWaitFor).Exception);
							return;
						}

						nested = FromScriptValue(value);

						if (nested == null)
						{
							_ = completion.TrySetResult(value ?? DefaultObject);
							return;
						}
					}
				}
				catch (Exception ex)
				{
					_ = completion.TrySetException(ex);
				}
			}

			/// <summary>Also accepts a script object whose zero-argument <c>__Await()</c> leads to native work.</summary>
			internal static Task FromAwaitable(object value)
			{
				HashSet<object> seen = null;

				while (true)
				{
					var task = FromScriptValue(value);

					if (task != null)
						return task;

					if (value is RealThread { Task: null })
						throw new TargetError(NoBodyToWaitFor).Exception;

					if (value is not Any || Script.ResolveMember(value, "__Await", out _) is not KeysharpFunc awaitMethod)
					{
						if (seen == null)
							return null;

						throw new TypeError("__Await() must return awaitable work.").Exception;
					}

					seen ??= new(ReferenceEqualityComparer.Instance);

					if (!seen.Add(value))
						throw new Error("__Await resolution cycle detected.").Exception;

					if (awaitMethod.MinParams > 1)
						throw new TypeError("__Await() must not require arguments.").Exception;

					value = awaitMethod.Call(value);
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

			private static Task[] Underlyings(object[] Tasks)
			{
				var underlyings = new Task[Tasks?.Length ?? 0];

				for (var i = 0; i < underlyings.Length; i++)
				{
					var value = Tasks[i];
					// Null rather than skipping the element: OnError can suppress the raise, and waiting on the
					// rest would report success for a set the script never asked for.
					if (value is RealThread { Task: null })
					{
						_ = Errors.TargetErrorOccurred(NoBodyToWaitFor);
						return null;
					}

					var underlying = FromAwaitable(value);

					if (underlying == null)
					{
						_ = Errors.TypeErrorOccurred(value, typeof(KeysharpTask));
						return null;
					}

					underlyings[i] = underlying;
				}

				return underlyings;
			}

			/// <summary>
			/// Reads <c>Task&lt;T&gt;.Result</c> without knowing T. <see cref="ReadResult"/> performs the script conversion.
			/// <para>
			/// The runtime type is not a usable test on its own: an <c>async Task</c> method's state-machine box
			/// is a <c>Task&lt;VoidTaskResult&gt;</c>, so asking whether the type is generic answers yes for the
			/// most common async signature there is and hands the script that internal marker struct. The closed
			/// <c>Task&lt;T&gt;</c> base is what actually says whether there is a value.</para>
			/// </summary>
			private static object ReadRawResult(Task t)
			{
				if (t == null)
					return null;

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

				return reader?.Invoke(t);
			}

			internal static object ReadResult(Task t)
			{
				var value = ReadRawResult(t);
				return value == null ? DefaultObject : ManagedInvoke.ConvertOut(value);
			}

			internal Error GetMappedError()
				=> LazyInitializer.EnsureInitialized(ref mappedError,
					() => ManagedInvoke.MapException(Unwrap(task.Exception), "Task"));

			internal static bool IsCurrentRealThreadTask(Task candidate)
			{
				var scheduler = Script.TheScript?.CurrentSchedulerIfCreated;
				return scheduler?.realThread is { } realThread && ReferenceEquals(realThread.Task, candidate);
			}

			internal static Exception Unwrap(Exception ex) =>
				ex is AggregateException agg && agg.InnerExceptions.Count == 1 ? agg.InnerExceptions[0] : ex;

			private static bool TryGetUserExit(Exception ex, out Flow.UserRequestedExitException exit)
				=> Keysharp.Internals.Flow.TryGetException(ex, out exit);

			private static bool IsSoleUserExit(Exception ex)
			{
				while (ex != null && ex is not Flow.UserRequestedExitException)
				{
					if (ex is AggregateException aggregate)
					{
						if (aggregate.InnerExceptions.Count != 1)
							return false;

						ex = aggregate.InnerExceptions[0];
					}
					else
						ex = ex.InnerException;
				}

				return ex != null;
			}
		}

	}
}
