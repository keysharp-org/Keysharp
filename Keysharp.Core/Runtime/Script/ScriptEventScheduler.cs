using Keysharp.Builtins;
using Keysharp.Internals.Invoke;
using Keysharp.Runtime.Keyboard;
using Keysharp.Internals.Threading;
using Keysharp.Internals.Window;
using System.Runtime.ExceptionServices;

namespace Keysharp.Runtime
{
	public partial class Script
	{
		private readonly Lock eventSchedulerGate = new();
		private ThreadLocal<ScriptEventScheduler> eventSchedulers;
		// What the walks below iterate, copy-on-write under the gate. The ThreadLocal does not track its values, so a
		// finished RealThread leaves nothing behind once ReleaseThreadScheduler drops it here.
		private ScriptEventScheduler[] liveSchedulers = [];
		internal ScriptEventScheduler uiEventScheduler;

		/// <summary>
		/// Gets or creates the per-thread scheduler for the current thread, without any fallback.
		/// Use this only during thread initialization (before the SynchronizationContext has been set),
		/// such as in RealThread setup. All other callers should use <see cref="EventScheduler"/>.
		/// </summary>
		internal ScriptEventScheduler ThreadScheduler
		{
			get
			{
				// No factory on the ThreadLocal, so this is a single slot read that cannot construct anything --
				// it is on the path of every thread launch and callback registration. Creation happens under the
				// gate instead, which is what keeps it from racing ShutdownEventSchedulers.
				var existing = Volatile.Read(ref eventSchedulers)?.Value;

				if (existing != null)
					return existing;

				lock (eventSchedulerGate)
				{
					var schedulers = eventSchedulers ??= new();

					if (schedulers.Value is { } created)
						return created;

					if (IsDisposed)
						throw new ObjectDisposedException(nameof(Script));

					var scheduler = CreateSchedulerForCurrentThread();
					liveSchedulers = [.. liveSchedulers, scheduler];
					return schedulers.Value = scheduler;
				}
			}
		}

		// Runs on the thread the scheduler will belong to, which is the only place its pseudo-thread stack can be
		// captured correctly (both are thread-local and 1:1 with the real thread). Binding them here is
		// what lets RealThread.Threads and RealThread.Main reach another thread's stack without a second registry.
		private ScriptEventScheduler CreateSchedulerForCurrentThread()
		{
			var scheduler = new ScriptEventScheduler(this, Thread.CurrentThread.ManagedThreadId, IsOnMainThread);
			scheduler.threadManager = Threads.CurrentManager;
			return scheduler;
		}

		internal ScriptEventScheduler UIEventScheduler
			=> uiEventScheduler ?? throw new InvalidOperationException("UI event scheduler has not been bound yet.");

		internal ScriptEventScheduler CurrentSchedulerIfCreated
			=> Volatile.Read(ref eventSchedulers)?.Value;

		/// <summary>Whether a synchronous task wait may safely pump Keysharp/UI events on this thread. Static
		/// initialization and ThreadPool workers must use an ordinary wait: pumping there would either touch a
		/// partially constructed Script or create/drain a scheduler on the wrong thread.</summary>
		internal bool CanPumpTaskWait
			=> mainEventScheduler != null
			   && threads is { IsValueCreated: true }
			   && IsOnMainThread;

		/// <summary>
		/// Returns the scheduler that should own work initiated on the current thread.
		/// On threads with a SynchronizationContext (UI thread, RealThreads), returns that thread's
		/// own scheduler. On threads without one (ad-hoc ThreadPool callbacks such as hook callbacks),
		/// falls back to the UI scheduler so that posted work is actually drained -- or, if the UI
		/// scheduler hasn't been bound yet (e.g. a hook callback fires before the UI thread has
		/// initialized its own scheduler), falls back to this thread's own scheduler instead of
		/// throwing.
		/// </summary>
		internal ScriptEventScheduler EventScheduler
			=> SynchronizationContext.Current == null ? (uiEventScheduler ?? ThreadScheduler) : ThreadScheduler;


		// Exit, input completion and scheduler disposal may release waiters on any thread.
		internal void WakeAllEventSchedulers()
		{
			foreach (var scheduler in Volatile.Read(ref liveSchedulers))
				scheduler.Wake();
		}

		internal void ScheduleBlockedEventSchedulers()
		{
			foreach (var scheduler in Volatile.Read(ref liveSchedulers))
			{
				if (scheduler.HasBlockedQueuedWork)
					scheduler.SchedulePump();
			}
		}

		// Runs on the worker's own thread as it ends, the only thread whose slot it can clear.
		internal void ReleaseThreadScheduler(ScriptEventScheduler scheduler)
		{
			lock (eventSchedulerGate)
			{
				var live = liveSchedulers;
				var i = System.Array.IndexOf(live, scheduler);

				if (i >= 0)
					liveSchedulers = [.. live.AsSpan(0, i), .. live.AsSpan(i + 1)];

				if (eventSchedulers is { } schedulers && schedulers.Value == scheduler)
					schedulers.Value = null;
			}
		}

		internal void ShutdownEventSchedulers()
		{
			ScriptEventScheduler[] shutdown;

			// Detached, not disposed: disposing would make Value throw for readers still holding the old reference.
			// Readers tolerate null; ThreadScheduler rebuilds only while the script is alive.
			lock (eventSchedulerGate)
			{
				var schedulers = new HashSet<ScriptEventScheduler>(liveSchedulers);
				eventSchedulers = null;
				liveSchedulers = [];

				if (uiEventScheduler != null)
					_ = schedulers.Add(uiEventScheduler);

				shutdown = [.. schedulers];
			}

			foreach (var scheduler in shutdown)
				scheduler.ShutdownForScriptDispose();
		}
	}

	internal enum ScriptEventQueue
	{
		Normal,
		Interactive
	}

	internal enum ScriptEventExecutionResult
	{
		Executed,
		GlobalBlocked,
		LocalBlocked,
		Dropped
	}

	internal sealed class ScriptEventSynchronizationContext(ScriptEventScheduler scheduler) : SynchronizationContext
	{
		private readonly ScriptEventScheduler scheduler = scheduler;

		public override SynchronizationContext CreateCopy()
			=> new ScriptEventSynchronizationContext(scheduler);

		public override void Post(SendOrPostCallback d, object state)
		{
			if (d == null)
				return;

			var owner = scheduler.Owner;

			if (owner.IsDisposed)
				return;

			// Once the owning thread is gone the queue is cleared and nothing will ever drain it, so dropping the
			// callback would leave whatever awaits it unfinished for good. There is no script thread left to
			// marshal to, which makes the pool the honest destination rather than a fallback.
			if (!scheduler.EnqueueCallback(() =>
				{
					if (!owner.IsDisposed)
						d(state);
				}))
				_ = ThreadPool.QueueUserWorkItem(_ =>
				{
					if (!owner.IsDisposed)
						d(state);
				});
		}

		public override void Send(SendOrPostCallback d, object state)
		{
			if (d == null)
				return;

			var owner = scheduler.Owner;

			if (owner.IsDisposed)
				throw new ObjectDisposedException(nameof(Script));

			// Nothing can retire the owner between the check above and the call: only this thread could, and it is here.
			if (scheduler.OwnsCurrentThread)
			{
				d(state);
				return;
			}

			_ = scheduler.InvokeSynchronous(() =>
			{
				if (owner.IsDisposed)
					throw new ObjectDisposedException(nameof(Script));

				d(state);
				return true;
			});
		}
	}

	internal readonly struct ScriptQueueEntry(long priority, Func<ScriptEventExecutionResult> execute, bool launchesThread)
	{
		internal readonly long Priority = priority;
		internal readonly Func<ScriptEventExecutionResult> Execute = execute;

		/// <summary>
		/// Whether this entry reports launch admission (#MaxThreads, interruptibility) to the pump,
		/// i.e. whether <see cref="Execute"/> can return <see cref="ScriptEventExecutionResult.GlobalBlocked"/>.
		/// Those conditions gate thread launches, not message dispatch — as in AutoHotkey, where a Critical thread
		/// still processes sent messages — so the pump keeps serving entries labelled false while launches are
		/// refused.
		/// <para>
		/// This describes what the pump sees, not what the body does: a dispatch body may still start a
		/// pseudo-thread internally (<see cref="ScriptEventScheduler.InvokeSynchronous"/> bodies do), in which case
		/// it handles its own refusal rather than deferring to the pump.</para>
		/// </summary>
		internal readonly bool LaunchesThread = launchesThread;
	}

	internal ref struct ScriptPseudoThreadScope
	{
		private readonly Script script;

		internal ScriptPseudoThreadScope(Script script, ThreadVariables threadVariables, ScriptEventExecutionResult result)
		{
			this.script = script;
			ThreadVariables = threadVariables;
			Result = result;
		}

		internal ThreadVariables ThreadVariables { get; }
		internal ScriptEventExecutionResult Result { get; }
		internal bool Started => Result == ScriptEventExecutionResult.Executed;

		public void Dispose()
		{
			if (Started)
				script.Threads.EndThread(ThreadVariables);
		}
	}

	internal sealed class ScriptEventScheduler
	{
		private readonly object gate = new();
		private readonly Lock ownedResourceCleanupGate = new();
		private readonly Lock ownedDelegateGate = new();
		private readonly HashSet<DelegateHolder> ownedDelegates = [];
		private readonly HashSet<PendingCallbackRegistration> pendingCallbacks = [];
		private readonly LinkedList<ScriptQueueEntry> interactiveQueue = new();
		private readonly LinkedList<ScriptQueueEntry> normalQueue = new();
		// Reused by EnqueueDueTimers (only the owning thread calls it), so the per-pump due-check doesn't allocate.
		private readonly List<Keysharp.Internals.Threading.ScriptTimerState> dueTimerBuffer = new();
		private AutoResetEvent pumpSignal;
		private int workerDisposed;
		private bool ownedResourcesDisposed;
		private int workerExitRequested;
		// The script-visible RealThread bound to this scheduler's thread (A_RealThread). Set by the RealThread
		// worker before its body runs, or created on demand for the main/adopted threads.
		internal Keysharp.Builtins.Ks.RealThread realThread;
		// This thread's pseudo-thread stack, captured when the scheduler is created (see
		// Script.CreateSchedulerForCurrentThread). Scheduler, stack and real thread are all 1:1.
		internal ThreadVariableManager threadManager;
		private int persistentRegistrationCount;
		private bool blockedQueuedWork;
		private object scheduledPumpTarget;
		private bool timerCheckPending;
		private readonly bool isUiScheduler;
		private readonly int ownerManagedThreadId;
		private readonly Script script;
		private readonly Action postedPump;
		private SynchronizationContext dispatchContext;

		/// <summary>
		/// This scheduler's dispatch transport: the <see cref="SynchronizationContext"/> which marshals a callback
		/// onto the thread it serves. Every scheduler has one of the same kind, so a worker and the main thread
		/// behave identically. Marshalling is all it does -- timers, hotkeys and the other thread launches are
		/// scheduled, never posted, and do not travel through it.
		/// </summary>
		internal SynchronizationContext DispatchContext
			=> dispatchContext ??= new ScriptEventSynchronizationContext(this);

		internal ScriptEventScheduler(Script owner, int ownerManagedThreadId, bool isUiScheduler)
		{
			script = owner;
			this.ownerManagedThreadId = ownerManagedThreadId;
			this.isUiScheduler = isUiScheduler;
			pumpSignal = new(false);
			if (isUiScheduler)
			{
				postedPump = PumpPosted;
				owner.uiEventScheduler = this;
			}
		}

		internal Script Owner => script;
		internal bool OwnsCurrentThread => Thread.CurrentThread.ManagedThreadId == ownerManagedThreadId;
		internal int OwnerManagedThreadId => ownerManagedThreadId;
		internal bool IsDisposed => script.IsDisposed || Volatile.Read(ref workerDisposed) != 0;
		internal bool HasBlockedQueuedWork
		{
			get
			{
				lock (gate)
					return HasBlockedQueuedWorkUnsafe();
			}
		}

		internal void AdjustPersistenceRoot(int delta)
		{
			if (delta == 0 || isUiScheduler || delta > 0 && IsDisposed)
				return;

			if (delta > 0)
				_ = Interlocked.Add(ref persistentRegistrationCount, delta);
			else
			{
				var remaining = Interlocked.Add(ref persistentRegistrationCount, delta);

				if (remaining < 0)
					_ = Interlocked.Exchange(ref persistentRegistrationCount, 0);
			}

			SignalPump();
		}

		/// <summary>
		/// Keeps the script and this scheduler running for work whose callback is still to come, until the returned
		/// registration is released. <paramref name="invalidated"/> runs instead if the scheduler is torn down first.
		/// Null when it already has been.
		/// </summary>
		internal PendingCallbackRegistration RegisterPendingCallback(Action invalidated)
		{
			var registration = new PendingCallbackRegistration(this, invalidated);

			lock (ownedResourceCleanupGate)
			{
				if (ownedResourcesDisposed || IsDisposed)
					return null;

				_ = pendingCallbacks.Add(registration);
				AdjustPersistenceRoot(1);
				script.AdjustPendingSchedulerWork(1);
				return registration;
			}
		}

		private void ReleasePendingCallback(PendingCallbackRegistration registration)
		{
			lock (ownedResourceCleanupGate)
			{
				if (!pendingCallbacks.Remove(registration))
					return;

				AdjustPersistenceRoot(-1);
				script.AdjustPendingSchedulerWork(-1);
			}

			// The released work may have been all that kept the script running.
			if (!script.IsDisposed)
				_ = script.ExitIfNotPersistent();
		}

		/// <summary>
		/// One <see cref="RegisterPendingCallback"/> registration. Each is its own object, so two pieces of work
		/// pending at once hold two roots however alike their callbacks are.
		/// </summary>
		internal sealed class PendingCallbackRegistration(ScriptEventScheduler scheduler, Action invalidated) : IDisposable
		{
			internal Action Invalidated => invalidated;

			/// <summary>Ends the registration. Repeated disposal and disposal after scheduler teardown have no effect.</summary>
			public void Dispose() => scheduler.ReleasePendingCallback(this);
		}

		internal bool RegisterOwnedDelegate(DelegateHolder holder)
		{
			if (holder == null)
				return false;

			return TryRegisterOwnedResource(() =>
			{
				lock (ownedDelegateGate)
					return ownedDelegates.Add(holder);
			});
		}

		internal bool TryRegisterOwnedResource(Func<bool> register)
		{
			if (register == null)
				return false;

			lock (ownedResourceCleanupGate)
			{
				if (ownedResourcesDisposed || IsDisposed)
					return false;

				return register();
			}
		}

		internal void UnregisterOwnedDelegate(DelegateHolder holder)
		{
			if (holder == null)
				return;

			lock (ownedDelegateGate)
				_ = ownedDelegates.Remove(holder);
		}

		internal DelegateHolder[] GetOwnedDelegatesSnapshot()
		{
			lock (ownedDelegateGate)
				return ownedDelegates.Count != 0 ? [.. ownedDelegates] : [];
		}

		/// <param name="launchesThread">See <see cref="ScriptQueueEntry.LaunchesThread"/>. Defaults to true because
		/// every producer outside this class launches; non-launching work goes through EnqueueCallback.</param>
		internal bool Enqueue(ScriptEventQueue queueType, long priority, Func<ScriptEventExecutionResult> tryExecute, bool launchesThread = true)
		{
			if (tryExecute == null)
				return false;

			lock (gate)
			{
				if (IsDisposed)
					return false;

				var entry = new ScriptQueueEntry(priority, tryExecute, launchesThread);
				var queue = GetQueue(queueType);

				// Interactive queue is inserted FIFO (used by InvokeSynchronous — ordering must be strict). Service
				// order can still differ: a pass which has been refused a thread launch serves dispatch entries past
				// the parked launches ahead of them.
				// Normal queue uses priority-sorted insertion: higher priority events go before lower ones,
				// and same-priority events maintain FIFO order (new event goes after existing same-priority events).
				if (queueType == ScriptEventQueue.Interactive || queue.Count == 0 || priority <= queue.Last.Value.Priority)
				{
					queue.AddLast(entry);
				}
				else
				{
					// Scan backwards to find the last node whose priority >= the new entry's priority,
					// then insert after it so that same-priority entries stay in FIFO order.
					var node = queue.Last.Previous;

					while (node != null && node.Value.Priority < priority)
						node = node.Previous;

					if (node == null)
						queue.AddFirst(entry);
					else
						queue.AddAfter(node, entry);
				}
			}

			SchedulePump();
			return true;
		}

		internal bool EnqueueTimer(ScriptTimerState timer)
		{
			if (timer == null || timer.Callback == null)
				return false;

			var queuedEvent = new TimerQueuedEvent(this, timer);
			return Enqueue(ScriptEventQueue.Normal, timer.Priority, queuedEvent.Execute);
		}

		// Runs the timer due-check on the owning thread and enqueues any due timers through the TimerQueuedEvent path.
		// Called at the top of the pump and the worker loop so a timer fires inline when its scheduler next runs. The
		// timer manager's thread only wakes the scheduler (WakeForTimerCheck); this is where "should it fire" is decided.
		internal void EnqueueDueTimers()
		{
			if (IsDisposed)
				return;

			lock (gate)
				timerCheckPending = false;

			foreach (var timer in script.FlowData.timers.TakeDueTimers(this, dueTimerBuffer))
			{
				if (!EnqueueTimer(timer))
					script.FlowData.timers.ReleaseQueuedTimer(timer);
			}
		}

		internal bool EnqueueThreadLaunch(long priority, bool skipUninterruptible, bool isCritical, Action action, ThreadKind kind = ThreadKind.None)
			=> action != null
				&& Enqueue(ScriptEventQueue.Normal, priority, () => TryExecuteThreadLaunch(priority, skipUninterruptible, isCritical, _ => action(), kind));

		internal bool EnqueueThreadLaunch(long priority, bool skipUninterruptible, bool isCritical, Action action, bool useTryCatch, ThreadKind kind = ThreadKind.None)
			=> useTryCatch
				? EnqueueThreadLaunch(priority, skipUninterruptible, isCritical, () => _ = Keysharp.Internals.Flow.TryCatch(action), kind)
				: EnqueueThreadLaunch(priority, skipUninterruptible, isCritical, action, kind);

		internal bool EnqueueCallback(Action action, ScriptEventQueue queueType = ScriptEventQueue.Normal, long priority = 0)
			=> action != null
				&& Enqueue(queueType, priority, () =>
			{
				if (script.hasExited)
					return ScriptEventExecutionResult.Dropped;

				action();
				return ScriptEventExecutionResult.Executed;
			}, launchesThread: false);

		internal bool EnqueueCallback(Action action, ScriptEventQueue queueType, bool useTryCatch, long priority = 0)
			=> useTryCatch
				? action != null
					&& Enqueue(queueType, priority, () =>
					{
						if (script.hasExited)
							return ScriptEventExecutionResult.Dropped;

						_ = Keysharp.Internals.Flow.TryCatch(action);
						return ScriptEventExecutionResult.Executed;
					}, launchesThread: false)
				: EnqueueCallback(action, queueType, priority);

		internal T InvokeSynchronous<T>(Func<T> func)
		{
			if (func == null)
				return default;

			if (IsDisposed)
				throw new ObjectDisposedException(nameof(ScriptEventScheduler));

			if (OwnsCurrentThread)
				return func();

			// Hand off to the UI framework only when there is one. Without it the queue below is the marshal --
			// it targets this same thread, and whoever is pumping serves it.
			if (isUiScheduler && script.UIThreadContext != null)
				return script.InvokeOnUIThread(func);

			// The caller may unwind before the callback finishes, so completion cannot own a disposable wait handle.
			var completed = new TaskCompletionSource();
			var waiter = script.CurrentSchedulerIfCreated;
			ExceptionDispatchInfo captured = null;
			T result = default;

			if (!EnqueueCallback(() =>
			{
				try
				{
					result = func();
				}
				catch (Exception ex)
				{
					captured = ExceptionDispatchInfo.Capture(ex);
				}
				finally
				{
					completed.SetResult();
					waiter?.Wake();
				}
			}, ScriptEventQueue.Interactive))
				throw new ObjectDisposedException(nameof(ScriptEventScheduler));

			WaitForSynchronousCompletion(completed.Task, waiter);
			captured?.Throw();
			return result;
		}

		// Raw scheduler pump for infrastructure which has no interrupted script pseudo-thread to resume.
		// Script safe points must use Keysharp.Internals.Flow.TryDoEvents so pending thread/app exits are observed.
		internal void PumpThreadQueuedEventsCore()
		{
			if (!OwnsCurrentThread || IsDisposed)
				return;

			bool blocked = false;
			var consecutiveInteractiveLocalBlocks = 0;
			var consecutiveNormalLocalBlocks = 0;
			var preferNormalOnce = false;

			BeginPump();

			try
			{
				// Run the timer due-check first, so any timer that has come due is enqueued before we drain the queue.
				EnqueueDueTimers();

				while (true)
				{
					switch (TryProcessNextQueuedEvent(blocked, ref consecutiveInteractiveLocalBlocks, ref consecutiveNormalLocalBlocks, ref preferNormalOnce))
					{
						case ScriptEventExecutionResult.Executed:
							continue;
						case ScriptEventExecutionResult.GlobalBlocked:
							// The entry is parked at the front of its queue. Latch so the rest of this pass skips
							// launches, keeping that class in order, and keep draining dispatch. A parked launch is
							// retried by the next pass, not this one, so blocked launches cannot spin in place.
							blocked = true;
							continue;
						case ScriptEventExecutionResult.LocalBlocked:
							blocked = true;
							return;
						case ScriptEventExecutionResult.Dropped:
							return;
					}
				}
			}
			finally
			{
				EndPump(blocked);
			}
		}

		/// <summary>
		/// Whether <see cref="RequestWorkerExit"/> has asked this worker's event loop to stop.
		/// </summary>
		internal bool IsWorkerExitRequested => Volatile.Read(ref workerExitRequested) != 0;

		/// <summary>
		/// Asks this worker's event loop to stop at its next iteration and wakes it. Needed because a worker that
		/// registered a timer, hotkey or callback holds a persistence root and would otherwise loop until the
		/// process ends. The loop only checks between events, so work already running finishes first; a pseudo-thread
		/// that must be unwound is handled separately through its requested exit code.
		/// </summary>
		internal void RequestWorkerExit()
		{
			if (isUiScheduler)
				return;

			_ = Interlocked.Exchange(ref workerExitRequested, 1);
			SignalPump();
		}

		internal void RunWorkerEventLoop()
		{
			while (!script.hasExited && !IsDisposed && !IsWorkerExitRequested)
			{
				if (HasBlockedQueuedWork)
				{
					// Blocked work can become runnable either when another scheduler finishes a pseudo-thread
					// or when interruptibility naturally times out, so wait briefly rather than spinning.
					WaitForPumpSignal((int)ThreadVariables.DefaultUninterruptiblePeekFrequency);

					// RequestWorkerExit signals this same wait, and a shutting-down worker abandons queued work
					// rather than serving one more entry on the way out.
					if (script.hasExited || IsDisposed || IsWorkerExitRequested)
						continue;
				}

				// Pump after every wake to release the scheduled flag and discard cancelled blocked work.
				PumpThreadQueuedEventsCore();

				if (script.Threads.ActivePseudoThreadCount > 0)
					return;

				if (!HasQueuedEvents())
				{
					if (Volatile.Read(ref persistentRegistrationCount) == 0)
						return;

					WaitForPumpSignal(Timeout.Infinite);
				}
			}
		}

		internal void DisposeWorker()
		{
			if (isUiScheduler || Interlocked.Exchange(ref workerDisposed, 1) != 0)
				return;

			DisposeOwnedResources();
			script.ReleaseThreadScheduler(this);

			SignalPump();

			// Only the worker's finally path closes its wait handle. Script shutdown can run concurrently on
			// another thread, so it limits itself to requesting exit and waking this handle.
			var signal = Interlocked.Exchange(ref pumpSignal, null);
			signal?.Dispose();
		}

		internal void ShutdownForScriptDispose()
		{
			DisposeOwnedResources();

			// A GUI main thread is released by the hasExited wake that precedes Dispose, not by this signal, which is for
			// a headless one still waiting on it. Only the owner can know that nobody waits, so only it closes the handle.
			if (isUiScheduler)
			{
				SignalPump();

				if (OwnsCurrentThread)
					Interlocked.Exchange(ref pumpSignal, null)?.Dispose();
			}
			else
				RequestWorkerExit();
		}

		internal void SchedulePump() => SchedulePump(requireQueued: true);

		// Wake even with an empty queue, for example when a task completes.
		internal void Wake() => SchedulePump(requireQueued: false);

		// A due timer needs a pump even before its callback is enqueued.
		internal void WakeForTimerCheck()
		{
			lock (gate)
				timerCheckPending = true;

			Wake();
		}

		private void SchedulePump(bool requireQueued)
		{
			if (IsDisposed)
				return;

			object target = isUiScheduler ? script.UIThreadContext : null;
#if !WINDOWS
			if (target == null && isUiScheduler)
				target = Script.EtoApplication;
#endif
			bool post;

			lock (gate)
			{
				if (requireQueued && !HasQueuedEventsUnsafe())
					return;

				// A pending post can only wake the UI transport it was sent to.
				post = target != null && !ReferenceEquals(scheduledPumpTarget, target);

				if (post)
					scheduledPumpTarget = target;
			}

			// A nested wait can consume the signal before the posted pump runs. Coalesce posts, never wakeups.
			SignalPump();

			if (post)
			{
				// Native loops need a posted pump; the scheduler's own loop is already woken by its signal.
				if (target is SynchronizationContext context)
					context.Post(static state => ((Action)state)(), postedPump);
#if !WINDOWS
				else if (target is Eto.Forms.Application app)
					app.AsyncInvoke(postedPump);
#endif
			}
		}

		// Native dispatch has no script exception boundary; defer exit propagation to a script safe point.
		private void PumpPosted()
		{
			if (!IsDisposed)
				Keysharp.Internals.Flow.TryDoEvents(this, propagateExit: false, pumpUi: false);
		}

		private LinkedList<ScriptQueueEntry> GetQueue(ScriptEventQueue queueType)
			=> queueType == ScriptEventQueue.Interactive ? interactiveQueue : normalQueue;

		private void BeginPump()
		{
			// Nested passes do their own bookkeeping: the pass beneath one is parked in a callback, which may be a
			// dialog's message loop that only a posted wake reaches.
			lock (gate)
			{
				blockedQueuedWork = false;
				scheduledPumpTarget = null;
			}
		}

		private void EndPump(bool blocked)
		{
			bool checkTimers;

			lock (gate)
			{
				blockedQueuedWork = blocked;
				checkTimers = timerCheckPending;
			}

			// A timer notification can arrive after this pass's due-check, including during a callback.
			if (checkTimers)
				SchedulePump(requireQueued: false);
			else if (!blocked)
				SchedulePump();
		}

		private ScriptEventExecutionResult TryProcessNextQueuedEvent(bool skipLaunches, ref int consecutiveInteractiveLocalBlocks, ref int consecutiveNormalLocalBlocks, ref bool preferNormalOnce)
		{
			if (!TryGetNextQueuedEvent(preferNormalOnce, skipLaunches, out var queueType, out var entry))
				return ScriptEventExecutionResult.Dropped;

			preferNormalOnce = false;
			var result = entry.Execute();

			if (result == ScriptEventExecutionResult.GlobalBlocked)
			{
				RestoreBlockedWork(queueType, entry);
				return result;
			}

			if (result == ScriptEventExecutionResult.LocalBlocked)
				return HandleLocalBlock(queueType, entry, ref consecutiveInteractiveLocalBlocks, ref consecutiveNormalLocalBlocks, ref preferNormalOnce)
					? ScriptEventExecutionResult.LocalBlocked
					: ScriptEventExecutionResult.Executed;

			consecutiveInteractiveLocalBlocks = 0;
			consecutiveNormalLocalBlocks = 0;
			return result == ScriptEventExecutionResult.Dropped
				? ScriptEventExecutionResult.Executed
				: result;
		}

		private bool TryGetNextQueuedEvent(bool preferNormal, bool skipLaunches, out ScriptEventQueue queueType, out ScriptQueueEntry entry)
		{
			lock (gate)
			{
				// Interactive first, unless a run of locally blocked interactive events has asked to let normal work
				// past. Each queue is examined once either way.
				var firstType = preferNormal ? ScriptEventQueue.Normal : ScriptEventQueue.Interactive;
				var secondType = preferNormal ? ScriptEventQueue.Interactive : ScriptEventQueue.Normal;

				if (TryTakeNextServable(GetQueue(firstType), skipLaunches, out entry))
				{
					queueType = firstType;
					return true;
				}

				if (TryTakeNextServable(GetQueue(secondType), skipLaunches, out entry))
				{
					queueType = secondType;
					return true;
				}

				queueType = ScriptEventQueue.Normal;
				entry = default;
				return false;
			}
		}

		// Takes the queue head, or -- once launch admission has failed this pass -- the first entry which does not
		// need it, walking past the parked launches. Only walks while blocked, but the parked prefix has no bound
		// (an event source can keep enqueuing through a long Critical section) and the walk holds the gate, which
		// producers such as the input hook thread also take. Caller holds the gate.
		private static bool TryTakeNextServable(LinkedList<ScriptQueueEntry> queue, bool skipLaunches, out ScriptQueueEntry entry)
		{
			for (var node = queue.First; node != null; node = node.Next)
			{
				if (skipLaunches && node.Value.LaunchesThread)
					continue;

				entry = node.Value;
				queue.Remove(node);
				return true;
			}

			entry = default;
			return false;
		}

		private void RestoreBlockedWork(ScriptEventQueue queueType, ScriptQueueEntry entry)
		{
			// Re-labelling is a no-op copy for a correct producer; it exists so a mislabelled one cannot be refetched
			// by the skip-walk and re-run for the rest of the pass.
			lock (gate)
			{
				GetQueue(queueType).AddFirst(new ScriptQueueEntry(entry.Priority, entry.Execute, true));
				// Dispatch later in this pass can enter a nested wait before EndPump runs.
				blockedQueuedWork = true;
			}
		}

		private bool HandleLocalBlock(ScriptEventQueue queueType, ScriptQueueEntry entry, ref int consecutiveInteractiveLocalBlocks, ref int consecutiveNormalLocalBlocks, ref bool preferNormalOnce)
		{
			int interactiveCount;
			int normalCount;

			lock (gate)
			{
				GetQueue(queueType).AddLast(entry);
				blockedQueuedWork = true;
				interactiveCount = interactiveQueue.Count;
				normalCount = normalQueue.Count;
			}

			if (queueType == ScriptEventQueue.Interactive)
				consecutiveInteractiveLocalBlocks++;
			else
				consecutiveNormalLocalBlocks++;

			var interactiveBlocked = consecutiveInteractiveLocalBlocks >= interactiveCount;
			var normalBlocked = consecutiveNormalLocalBlocks >= normalCount;

			// Keep each queue's progress while checking the other, so two blocked queues finish a pass.
			preferNormalOnce = interactiveBlocked && !normalBlocked;
			return interactiveBlocked && normalBlocked;
		}

		internal bool HasQueuedEvents()
		{
			lock (gate)
				return HasQueuedEventsUnsafe();
		}

		// Signals only the managed wait handle. Use Wake when a native UI loop must also be woken.
		internal void SignalPump()
		{
			try
			{
				_ = Volatile.Read(ref pumpSignal)?.Set();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		// Only the owner waits; a handle its own shutdown has already closed leaves nothing to wait for.
		internal void WaitForPumpSignal(int timeout) => _ = Volatile.Read(ref pumpSignal)?.WaitOne(timeout);

#if WINDOWS
		// The handle is kept open across the native wait; a failed wait is slept through in a bounded slice rather than spun.
		internal void WaitForMessagesOrPumpSignal(int timeout)
		{
			if (Volatile.Read(ref pumpSignal) is not { } signal)
				return;

			var handle = signal.SafeWaitHandle;
			var added = false;

			try
			{
				handle.DangerousAddRef(ref added);
				var nativeHandle = handle.DangerousGetHandle();

				if (WindowsAPI.MsgWaitForMultipleObjectsEx(1, ref nativeHandle, unchecked((uint)timeout),
					WindowsAPI.QS_ALLINPUT, WindowsAPI.MWMO_INPUTAVAILABLE) == uint.MaxValue)
					Thread.Sleep(timeout < 0 ? Script.SLEEP_INTERVAL : Math.Min(timeout, Script.SLEEP_INTERVAL));
			}
			finally
			{
				if (added)
					handle.DangerousRelease();
			}
		}
#endif

		private bool HasQueuedEventsUnsafe() => interactiveQueue.Count != 0 || normalQueue.Count != 0;
		private bool HasBlockedQueuedWorkUnsafe() => blockedQueuedWork && HasQueuedEventsUnsafe();

		private sealed class TimerQueuedEvent(ScriptEventScheduler scheduler, ScriptTimerState timer)
		{
			internal ScriptEventExecutionResult Execute()
			{
				var script = scheduler.script;
				var timers = script.FlowData.timers;

				if (script.hasExited)
				{
					timers.ReleaseQueuedTimer(timer);
					return ScriptEventExecutionResult.Dropped;
				}

				var callback = timer.Callback;
				var timerRegistration = timers.Find(callback, timer.OwnerScheduler);

				if (!ReferenceEquals(timerRegistration, timer) || callback == null || !timer.IsActive)
				{
					timers.ReleaseQueuedTimer(timer);
					return ScriptEventExecutionResult.Dropped;
				}

#if WINDOWS
				if (Dialogs.HasPendingWindowsMsgBoxShow(script))
					return ScriptEventExecutionResult.LocalBlocked;
#endif

				var threads = script.Threads;

				// Interruptibility blocks every queued launch. Park the timer so the pump can
				// keep serving dispatch work without retrying blocked launches in the same pass.
				if (!threads.IsInterruptible())
					return ScriptEventExecutionResult.GlobalBlocked;

				// Keep the overdue callback queued while other eligible events continue. Admission changes and
				// pseudo-thread completion wake the scheduler without resetting the timer or polling its owner.
				// As AHK's g_nPausedThreads: no timer runs while any thread is paused, the idle one included.
				if ((!threads.AllowTimers && script.totalExistingThreads > 0)
					|| threads.pausedThreadCount > 0
					|| timer.Priority < threads.CurrentThread.priority)
					return ScriptEventExecutionResult.LocalBlocked;

				// SetTimer can reset a callback which is already parked in this queue.
				if (Environment.TickCount64 < timer.NextDueTick)
				{
					timers.ReleaseQueuedTimer(timer);
					return ScriptEventExecutionResult.Dropped;
				}

				var executed = false;

				{
					using var thread = scheduler.StartPseudoThreadScope(timer.Priority, true, false, false, ThreadKind.Timer);

					if (!thread.Started)
					{
						if (thread.Result == ScriptEventExecutionResult.Dropped)
							timers.ReleaseQueuedTimer(timer);

						return thread.Result;
					}

					var timerInfo = timers.MarkCallbackStarted(timer);

					if (timerInfo != null)
					{
						// Timers honour the Thread-Interrupt uninterruptible startup window like any new thread. The launch
						// passed skipUninterruptible=true (the pump vetted admission above), which also skips the window setup,
						// so apply it here.
						thread.ThreadVariables.ApplyUninterruptibleStartupWindow();

						executed = true;

						try
						{
							var btv = thread.ThreadVariables;
							btv.currentTimer = timer;
							btv.eventInfo = timerInfo;
							_ = Script.InvokeOrNull(callback, null);
						}
						catch (Exception ex) when (CallStack.RememberAndCatch(ex))
						{
							_ = Errors.ReportUncaught(ex);
						}
					}
				}

				timers.MarkCallbackFinished(timer);

				return executed
					? ScriptEventExecutionResult.Executed
					: ScriptEventExecutionResult.Dropped;
			}
		}

		// Pseudo-thread execution has three separate concerns:
		// 1. Admission: TryStartPseudoThread decides whether a pseudo-thread may start.
		// 2. Affinity: TryExecuteThreadLaunch/TryInvokePseudoThread either execute immediately on the
		//    owning scheduler thread or marshal through InvokeSynchronous to get there.
		// 3. Execution policy: the scheduler core is raw. Semantic callers that want handled behavior
		//    wrap their callback with Keysharp.Internals.Flow.TryCatch before calling into the scheduler. The scheduler
		//    runners always end the pseudo-thread in one place.
		internal ScriptEventExecutionResult TryExecuteThreadLaunch(long priority, bool skipUninterruptible, bool isCritical, Action<ThreadVariables> action, ThreadKind kind = ThreadKind.None, bool allowEmergencyOverflow = false)
		{
			if (action == null || IsDisposed)
				return ScriptEventExecutionResult.Dropped;

			if (OwnsCurrentThread)
			{
				var status = TryStartPseudoThread(priority, skipUninterruptible, isCritical, allowEmergencyOverflow, out var threadVariables, kind);
				return status != ScriptEventExecutionResult.Executed
					? status
					: RunPseudoThreadAction(threadVariables, action);
			}

			return InvokeSynchronous(() =>
			{
				var status = TryStartPseudoThread(priority, skipUninterruptible, isCritical, allowEmergencyOverflow, out var threadVariables, kind);
				return status != ScriptEventExecutionResult.Executed
					? status
					: RunPseudoThreadAction(threadVariables, action);
			});
		}

		internal ScriptEventExecutionResult TryInvokePseudoThread<T>(long priority, bool skipUninterruptible, bool isCritical, Func<ThreadVariables, T> action, out T result, bool allowEmergencyOverflow = false)
		{
			result = default;

			if (action == null || IsDisposed)
				return ScriptEventExecutionResult.Dropped;

			(ScriptEventExecutionResult status, T result) execution;

			if (OwnsCurrentThread)
			{
				var status = TryStartPseudoThread(priority, skipUninterruptible, isCritical, allowEmergencyOverflow, out var threadVariables);
				execution = status != ScriptEventExecutionResult.Executed
					? (status, default)
					: EvaluatePseudoThreadFunc(threadVariables, () => action(threadVariables));
			}
			else
			{
				execution = InvokeSynchronous(() =>
				{
					var status = TryStartPseudoThread(priority, skipUninterruptible, isCritical, allowEmergencyOverflow, out var threadVariables);
					return status != ScriptEventExecutionResult.Executed
						? (status, default(T))
						: EvaluatePseudoThreadFunc(threadVariables, () => action(threadVariables));
				});
			}

			result = execution.result;
			return execution.status;
		}

		private ScriptEventExecutionResult RunPseudoThreadAction(ThreadVariables threadVariables, Action<ThreadVariables> action)
		{
			try
			{
				action(threadVariables);
				return ScriptEventExecutionResult.Executed;
			}
			finally
			{
				script.Threads.EndThread(threadVariables);
			}
		}

		private (ScriptEventExecutionResult status, T result) EvaluatePseudoThreadFunc<T>(ThreadVariables threadVariables, Func<T> func)
		{
			try
			{
				return (ScriptEventExecutionResult.Executed, func());
			}
			finally
			{
				script.Threads.EndThread(threadVariables);
			}
		}

		// The completion wakes the waiter's pump; a thread without one checks for shutdown between timed waits.
		private void WaitForSynchronousCompletion(Task completed, ScriptEventScheduler waiter)
		{
			while (!completed.IsCompleted)
			{
				if (script.hasExited)
					throw new Keysharp.Builtins.Flow.UserRequestedExitException();

				if (IsDisposed)
					throw new ObjectDisposedException(nameof(ScriptEventScheduler));

				if (waiter != null)
					Keysharp.Internals.Flow.TryDoEvents(waiter, propagateExit: true, waitMs: Timeout.Infinite);
				else
					_ = completed.Wait(20);
			}
		}

		internal ScriptEventExecutionResult TryStartPseudoThread(long priority, bool skipUninterruptible, bool isCritical, bool allowEmergencyOverflow, out ThreadVariables threadVariables, ThreadKind kind = ThreadKind.None)
		{
			threadVariables = null;

			if (script.hasExited)
				return ScriptEventExecutionResult.Dropped;

			var threads = script.Threads;

			if (!allowEmergencyOverflow && !threads.AnyThreadsAvailable())
				return ScriptEventExecutionResult.GlobalBlocked;

			if (!skipUninterruptible && !threads.IsInterruptible())
				return ScriptEventExecutionResult.GlobalBlocked;

			// Emergencies (OnExit, a non-buffered OnMessage) must launch regardless of the current thread's priority,
			// just as they ignore #MaxThreads and interruptibility above. Matches AHK: "the OnExit callback function
			// will always run when called for, regardless of the current thread's priority."
			if (!allowEmergencyOverflow && priority < threads.CurrentThread.priority)
				return ScriptEventExecutionResult.Dropped;

			return threads.TryPushThreadVariables(priority, skipUninterruptible, isCritical, true, allowEmergencyOverflow, out threadVariables, kind)
				? ScriptEventExecutionResult.Executed
				: ScriptEventExecutionResult.GlobalBlocked;
		}

		internal ScriptPseudoThreadScope StartPseudoThreadScope(long priority, bool skipUninterruptible, bool isCritical, bool allowEmergencyOverflow, ThreadKind kind = ThreadKind.None)
		{
			var result = TryStartPseudoThread(priority, skipUninterruptible, isCritical, allowEmergencyOverflow, out var threadVariables, kind);
			return new(script, threadVariables, result);
		}

		private void ClearQueues()
		{
			lock (gate)
			{
				interactiveQueue.Clear();
				normalQueue.Clear();
				blockedQueuedWork = false;
				scheduledPumpTarget = null;
			}
		}

		private void DisposeOwnedResources()
		{
			// Cleanup may be initiated by Script.Dispose while a worker is entering its finally block. Keep the
			// operation idempotent and make the losing caller wait until the winning cleanup has actually finished,
			// since Script.Dispose can tear down the managers these removals use as soon as this method returns.
			// The gate is held across ManifestAllHotkeysHotstringsHooks, which blocks on the hook thread via
			// ht.Invoke. That cannot deadlock: the hook thread never registers an owned resource on a scheduler,
			// so it never takes this gate and can always finish the work it is handed.
			lock (ownedResourceCleanupGate)
			{
				if (ownedResourcesDisposed)
					return;

				ownedResourcesDisposed = true;
				ClearQueues();
				// Queued posts can no longer run, and their tasks are what an Await is holding.
				realThread?.FailPendingPosts();
				DisposeOwnedTimers();
				DisposeOwnedClipboardHandlers();
				var hotkeysChanged = Keysharp.Internals.Input.Keyboard.HotkeyDefinition.DisableOwnedVariants(this);
				var hotstringsChanged = script.hotstringManager?.DisableOwnedHotstrings(this) == true;
				DisposeOwnedMessageHandlers();
				DisposeOwnedGuiHandlers();
				DisposeOwnedMenuHandlers();
				DisposeOwnedEventSubscriptions();
				DisposeOwnedClrSubscriptions();
				DelegateHolder.DisposeOwnedByScheduler(this);
				InvalidatePendingCallbacks();
				_ = Interlocked.Exchange(ref persistentRegistrationCount, 0);

				if (hotkeysChanged || hotstringsChanged)
					_ = Keysharp.Internals.Input.Keyboard.HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(script);
			}

			// Release other schedulers waiting for work discarded here, such as InvokeSynchronous.
			script.WakeAllEventSchedulers();
		}

		private void InvalidatePendingCallbacks()
		{
			if (pendingCallbacks.Count == 0)
				return;

			var callbacks = pendingCallbacks.ToArray();
			pendingCallbacks.Clear();
			AdjustPersistenceRoot(-callbacks.Length);
			script.AdjustPendingSchedulerWork(-callbacks.Length);

			foreach (var callback in callbacks)
				callback.Invalidated();
		}

		private void DisposeOwnedTimers()
		{
			if (script.flowData?.timers.RemoveOwned(this) == true && !script.IsDisposed)
				script.ExitIfNotPersistent();
		}

		private void DisposeOwnedClipboardHandlers()
		{
			if (script.ClipFunctions.RemoveOwned(this))
				script.UpdateClipboardMonitoring();
		}

		private void DisposeOwnedMessageHandlers()
		{
			if (script.guiData is not { } data)
				return;

			foreach (var kv in data.onMessageHandlers.ToArray())
			{
				if (kv.Value == null || !kv.Value.RemoveOwned(this))
					continue;

				if (kv.Value.IsEmpty)
					_ = data.onMessageHandlers.TryRemove(kv.Key, out _);
			}
		}

		private void DisposeOwnedEventSubscriptions()
		{
			_ = script.WinEventManager.RemoveOwned(this);
			_ = script.MonitorEventManager.RemoveOwned(this);
			_ = script.AudioEventManager.RemoveOwned(this);
			_ = script.InputEventManager.RemoveOwned(this);
			_ = script.ClipboardEventManager.RemoveOwned(this);
		}

		private void DisposeOwnedClrSubscriptions()
			=> _ = script.ClrEventManager.RemoveOwned(this);

		private void DisposeOwnedGuiHandlers()
		{
			if (script.guiData is not { } data)
				return;

			foreach (var gui in new HashSet<Gui>(data.allGuiHwnds.Values))
			{
				_ = gui?.form?.RemoveOwnedHandlers(this);

				foreach (var control in gui?.controls?.Values.OfType<Gui.Control>() ?? [])
					_ = control?.RemoveOwnedHandlers(this);
			}

			foreach (var kv in data.overlayHandlerCleanups.ToArray())
			{
				if (kv.Value.TryGetTarget(out var cleanup))
					cleanup(this);
				else
					_ = data.overlayHandlerCleanups.TryRemove(kv.Key, out _);
			}
		}

		private void DisposeOwnedMenuHandlers()
		{
			if (script.guiData is not { } data)
				return;

			foreach (var kv in data.allMenus.ToArray())
			{
				if (!kv.Value.TryGetTarget(out var menu))
				{
					_ = data.allMenus.TryRemove(kv.Key, out _);
					continue;
				}

				_ = menu.RemoveOwnedHandlers(this);
			}
		}
	}
}
