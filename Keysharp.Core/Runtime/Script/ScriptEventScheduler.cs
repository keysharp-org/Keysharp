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
		private ThreadLocal<ScriptEventScheduler> eventSchedulers;
		internal ScriptEventScheduler uiEventScheduler;

		/// <summary>
		/// Gets or creates the per-thread scheduler for the current thread, without any fallback.
		/// Use this only during thread initialization (before the SynchronizationContext has been set),
		/// such as in RealThread setup. All other callers should use <see cref="EventScheduler"/>.
		/// </summary>
		internal ScriptEventScheduler ThreadScheduler
			=> (eventSchedulers ??= new(CreateSchedulerForCurrentThread, true)).Value;

		// The factory runs on the thread the scheduler will belong to, which is the only place its pseudo-thread
		// stack can be captured correctly (both are thread-local and 1:1 with the real thread). Binding them here is
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
			=> eventSchedulers != null && eventSchedulers.IsValueCreated ? eventSchedulers.Value : null;

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


		internal void ScheduleAllEventSchedulers()
			=> ScheduleEventSchedulers(static _ => true);

		internal void ScheduleBlockedEventSchedulers()
			=> ScheduleEventSchedulers(static scheduler => scheduler.HasBlockedQueuedWork);

		private void ScheduleEventSchedulers(Predicate<ScriptEventScheduler> shouldSchedule)
		{
			if (eventSchedulers == null)
				return;

			foreach (var scheduler in eventSchedulers.Values)
			{
				if (scheduler != null && (shouldSchedule == null || shouldSchedule(scheduler)))
					scheduler.SchedulePump();
			}
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

			// Once the owning thread is gone the queue is cleared and nothing will ever drain it, so dropping the
			// callback would leave whatever awaits it unfinished for good. There is no script thread left to
			// marshal to, which makes the pool the honest destination rather than a fallback.
			if (!scheduler.EnqueueCallback(() => d(state)))
				_ = ThreadPool.QueueUserWorkItem(_ => d(state));
		}

		public override void Send(SendOrPostCallback d, object state)
		{
			if (d == null)
				return;

			if (scheduler.OwnsCurrentThread)
				d(state);
			else
				_ = scheduler.InvokeSynchronous(() =>
				{
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
		/// Whether this entry reports launch admission (#MaxThreads, interruptibility, a visible menu) to the pump,
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
		private readonly Lock ownedDelegateGate = new();
		private readonly HashSet<DelegateHolder> ownedDelegates = [];
		private readonly LinkedList<ScriptQueueEntry> interactiveQueue = new();
		private readonly LinkedList<ScriptQueueEntry> normalQueue = new();
		// Reused by EnqueueDueTimers (only the owning thread calls it), so the per-pump due-check doesn't allocate.
		private readonly List<Keysharp.Internals.Threading.ScriptTimerState> dueTimerBuffer = new();
		private AutoResetEvent workerPumpSignal = new(false);
		private int workerDisposed;
		private int workerExitRequested;
		// The script-visible RealThread bound to this scheduler's thread (A_RealThread). Set by the RealThread
		// worker before its body runs, or created on demand for the main/adopted threads.
		internal Keysharp.Builtins.Ks.RealThread realThread;
		// This thread's pseudo-thread stack, captured when the scheduler is created (see
		// Script.CreateSchedulerForCurrentThread). Scheduler, stack and real thread are all 1:1.
		internal ThreadVariableManager threadManager;
		private int persistentRegistrationCount;
		private bool blockedQueuedWork;
		private bool pumpScheduled;
		private int pumpDepth;
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
			if (isUiScheduler)
			{
				postedPump = PumpPosted;
				owner.uiEventScheduler = this;
			}
		}

		internal bool OwnsCurrentThread => Thread.CurrentThread.ManagedThreadId == ownerManagedThreadId;
		internal int OwnerManagedThreadId => ownerManagedThreadId;
		internal bool IsDisposed => Volatile.Read(ref workerDisposed) != 0;
		// True for schedulers that are guaranteed to be pumped: the UI scheduler (driven by the
		// message loop) and RealThread schedulers (marked intentional before body runs, then
		// driven by RunWorkerEventLoop). False for ephemeral ad-hoc C# thread schedulers that
		// have no pump loop and would silently swallow any enqueued work.
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

			SignalWorkerPump();
		}

		internal void RegisterOwnedDelegate(DelegateHolder holder)
		{
			if (holder == null)
				return;

			lock (ownedDelegateGate)
				_ = ownedDelegates.Add(holder);
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

			foreach (var timer in script.FlowData.timers.TakeDueTimers(this, dueTimerBuffer))
				_ = EnqueueTimer(timer);
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

			if (isUiScheduler)
				return Script.InvokeOnUIThread(func);

			using var completed = new ManualResetEventSlim(false);
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
					completed.Set();
				}
			}, ScriptEventQueue.Interactive))
				throw new ObjectDisposedException(nameof(ScriptEventScheduler));

			WaitForSynchronousCompletion(completed);
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
			bool stalledOnLocalBlock = false;
			var consecutiveInteractiveLocalBlocks = 0;
			var consecutiveNormalLocalBlocks = 0;
			var preferNormalOnce = false;

			if (!TryBeginPump())
				return;

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
							// retried by the next pass, not this one, which is the back-pressure that stops the
							// timer thread refilling into the livelock described at TimerQueuedEvent.Execute.
							blocked = true;
							continue;
						case ScriptEventExecutionResult.LocalBlocked:
							stalledOnLocalBlock = true;
							return;
						case ScriptEventExecutionResult.Dropped:
							return;
					}
				}
			}
			finally
			{
				EndPump(blocked, stalledOnLocalBlock);
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
			SignalWorkerPump();
		}

		internal void RunWorkerEventLoop()
		{
			while (!script.hasExited && !IsDisposed && !IsWorkerExitRequested)
			{
				if (HasBlockedQueuedWork)
				{
					// Blocked work can become runnable either when another scheduler finishes a pseudo-thread
					// or when interruptibility naturally times out, so wait briefly rather than spinning.
					_ = WaitForWorkerPumpSignal((int)ThreadVariables.DefaultUninterruptiblePeekFrequency);

					// RequestWorkerExit signals this same wait, and a shutting-down worker abandons queued work
					// rather than serving one more entry on the way out.
					if (script.hasExited || IsDisposed || IsWorkerExitRequested)
						continue;

					// Then retry. TryBeginPump/EndPump are the only writers of the blocked flag, so a pump is the
					// only thing that can clear it -- going back to the top without one leaves this loop waiting
					// for a condition only it can produce. That outlives whatever caused the block: an entry
					// whose registration is later removed (SetTimer(fn, 0) while it sits parked) can never become
					// runnable again, and only a pump discovers that and drops it. The worker then spins forever
					// after its body has returned, so its completion -- published past this loop -- never lands.
					// The main thread is not exposed: its pump is driven from outside by PostToUIThread, which
					// does not consult this gate.
					PumpThreadQueuedEventsCore();

					// Same hand-off the normal path makes below: a pseudo-thread left running on this thread
					// owns the loop from here.
					if (script.Threads.ActivePseudoThreadCount > 0)
						return;

					continue;
				}

				// The timer waker only signals us (WakeForTimerCheck) when a timer is due; enqueue those due timers
				// here so the gate below sees them as queued work and we pump them instead of going back to sleep.
				EnqueueDueTimers();

				if (!HasQueuedEvents())
				{
					if (script.Threads.ActivePseudoThreadCount == 0 && Volatile.Read(ref persistentRegistrationCount) == 0)
						return;

					_ = WaitForWorkerPumpSignal();
					continue;
				}

				PumpThreadQueuedEventsCore();

				if (script.Threads.ActivePseudoThreadCount > 0)
					return;
			}
		}

		internal void DisposeWorker()
		{
			if (isUiScheduler || Interlocked.Exchange(ref workerDisposed, 1) != 0)
				return;

			ClearQueues();
			DisposeOwnedTimers();
			DisposeOwnedClipboardHandlers();
			var hotkeysChanged = Keysharp.Internals.Input.Keyboard.HotkeyDefinition.DisableOwnedVariants(this);
			var hotstringsChanged = script.HotstringManager.DisableOwnedHotstrings(this);
			DisposeOwnedMessageHandlers();
			DisposeOwnedGuiHandlers();
			DisposeOwnedMenuHandlers();
			DisposeOwnedWinEventHandlers();
			DisposeOwnedClrSubscriptions();
			DelegateHolder.DisposeOwnedByScheduler(this);
			_ = Interlocked.Exchange(ref persistentRegistrationCount, 0);

			if (hotkeysChanged || hotstringsChanged)
				_ = Keysharp.Internals.Input.Keyboard.HotkeyDefinition.ManifestAllHotkeysHotstringsHooks();

			SignalWorkerPump();

			var signal = Interlocked.Exchange(ref workerPumpSignal, null);
			signal?.Dispose();
		}

		internal void SchedulePump() => SchedulePump(requireQueued: true);

		// Wakes this scheduler so its pump runs the timer due-check (EnqueueDueTimers), even when the queue is currently
		// empty — the timer manager's waker calls this when a timer is due but not yet enqueued. Coalesced via
		// pumpScheduled, so repeated wakes before the pump runs collapse into one post.
		internal void WakeForTimerCheck() => SchedulePump(requireQueued: false);

		private void SchedulePump(bool requireQueued)
		{
			if (IsDisposed)
				return;

			lock (gate)
			{
				if (!TryMarkPumpScheduledUnsafe(requireQueued))
					return;
			}

			if (isUiScheduler)
				// This delegate can run from an idle native UI loop with no script exception boundary.
				// Keep exit requests pending for the next genuine script safe point instead of throwing here.
				Script.PostToUIThread(postedPump);
			else
				SignalWorkerPump();
		}

		private void PumpPosted()
			=> Keysharp.Internals.Flow.TryDoEvents(this, propagateExit: false, yieldTick: false, pumpUi: false);

		private LinkedList<ScriptQueueEntry> GetQueue(ScriptEventQueue queueType)
			=> queueType == ScriptEventQueue.Interactive ? interactiveQueue : normalQueue;

		private bool TryBeginPump()
		{
			lock (gate)
			{
				if (pumpDepth++ != 0)
					return true;

				blockedQueuedWork = false;
				pumpScheduled = false;
				return true;
			}
		}

		private void EndPump(bool blocked, bool stalledOnLocalBlock)
		{
			var isOuterPump = false;

			lock (gate)
			{
				pumpDepth--;

				if (pumpDepth != 0)
					return;

				isOuterPump = true;
				blockedQueuedWork = blocked || stalledOnLocalBlock;
			}

			if (isOuterPump && !blocked && !stalledOnLocalBlock)
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
				GetQueue(queueType).AddFirst(new ScriptQueueEntry(entry.Priority, entry.Execute, true));
		}

		private bool HandleLocalBlock(ScriptEventQueue queueType, ScriptQueueEntry entry, ref int consecutiveInteractiveLocalBlocks, ref int consecutiveNormalLocalBlocks, ref bool preferNormalOnce)
		{
			int interactiveCount;
			int normalCount;

			lock (gate)
			{
				GetQueue(queueType).AddLast(entry);
				interactiveCount = interactiveQueue.Count;
				normalCount = normalQueue.Count;
			}

			if (queueType == ScriptEventQueue.Interactive)
			{
				consecutiveInteractiveLocalBlocks++;
				consecutiveNormalLocalBlocks = 0;

				// If every currently queued interactive event is only locally blocked, allow normal
				// queued work to proceed rather than stalling the entire pump behind one hotkey/hotstring.
				if (normalCount != 0 && consecutiveInteractiveLocalBlocks >= interactiveCount)
				{
					consecutiveInteractiveLocalBlocks = 0;
					preferNormalOnce = true;
					return false;
				}

				return consecutiveInteractiveLocalBlocks >= interactiveCount;
			}

			consecutiveNormalLocalBlocks++;
			consecutiveInteractiveLocalBlocks = 0;
			return consecutiveNormalLocalBlocks >= normalCount;
		}

		private bool HasQueuedEvents()
		{
			lock (gate)
				return HasQueuedEventsUnsafe();
		}

		internal void SignalWorkerPump()
		{
			try
			{
				_ = Volatile.Read(ref workerPumpSignal)?.Set();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private bool WaitForWorkerPumpSignal()
			=> WaitForWorkerPumpSignal(Timeout.Infinite);

		private bool WaitForWorkerPumpSignal(int timeout)
		{
			try
			{
				return Volatile.Read(ref workerPumpSignal)?.WaitOne(timeout) == true;
			}
			catch (ObjectDisposedException)
			{
				return false;
			}
		}

		private bool HasQueuedEventsUnsafe() => interactiveQueue.Count != 0 || normalQueue.Count != 0;
		private bool HasBlockedQueuedWorkUnsafe() => blockedQueuedWork && HasQueuedEventsUnsafe();

		private bool TryMarkPumpScheduledUnsafe(bool requireQueued = true)
		{
			// requireQueued:false is the timer-waker path — wake to run the due-check even with an empty queue.
			if ((requireQueued && !HasQueuedEventsUnsafe()) || pumpScheduled || pumpDepth != 0)
				return false;

			pumpScheduled = true;
			return true;
		}

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

				if (!ReferenceEquals(timerRegistration, timer) || callback == null || !timer.Enabled)
				{
					timers.ReleaseQueuedTimer(timer);
					return ScriptEventExecutionResult.Dropped;
				}

#if WINDOWS
				if (Dialogs.HasPendingWindowsMsgBoxShow())
					return ScriptEventExecutionResult.LocalBlocked;
#endif

				var threads = script.Threads;

				// The thread pool is exhausted, the current thread is uninterruptible, or a menu is displayed: a global
				// condition that blocks every queued THREAD LAUNCH (the thread-launch path reports the same via
				// TryStartPseudoThread). It does not block dispatch work, which the pump keeps serving -- matching AHK,
				// where a Critical thread still processes sent messages and only refuses interrupting threads.
				// PARK this entry rather than dropping-and-continuing. Returning Dropped here made
				// TryProcessNextQueuedEvent report Executed, so the pump kept looping while the timer thread refilled the
				// queue — a livelock that pinned normalQueue, pegged the CPU, and starved the running pseudo-threads so
				// their slots never freed. Holding timers while a menu is open matches AutoHotkey's g_MenuIsVisible check.
				if (!threads.AnyThreadsAvailable() || !threads.IsInterruptible() || script.IsMenuVisible)
					return ScriptEventExecutionResult.GlobalBlocked;

				// Timers are disabled (A_AllowTimers) but other, non-timer queued work may still run, so drop just this
				// timer and let the pump continue. The timer thread reschedules it once timers are re-enabled.
				if (!threads.AllowTimers && script.totalExistingThreads > 0)
				{
					timers.ReleaseQueuedTimer(timer);
					return ScriptEventExecutionResult.Dropped;
				}

				// Per-event priority admission (the global conditions were handled above): a lower-priority timer may
				// not interrupt a higher-priority current thread. Defer it (re-arm, NOT consume) so a run-once timer is
				// preserved and fires once the higher thread ends — the forward NextDueTick avoids a tight re-serve
				// loop — then drop THIS event so the pump keeps serving other (possibly higher-priority) queued work.
				if (timer.Priority < threads.CurrentThread.priority)
				{
					timers.DeferPriorityBlocked(timer);
					return ScriptEventExecutionResult.Dropped;
				}

				timers.MarkCallbackStarted(timer);
				var executed = false;

				{
					using var thread = scheduler.StartPseudoThreadScope(timer.Priority, true, false, false, ThreadKind.Timer);

					if (thread.Started)
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
							btv.eventInfo = callback;
							_ = callback.Call();
						}
						catch (Exception ex)
						{
							_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
						}
					}
				}

				timers.MarkCallbackFinished(timer);

				if (timer.Callback == null)
					script.ExitIfNotPersistent();

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

		private void WaitForSynchronousCompletion(ManualResetEventSlim completed)
		{
			var waitingScheduler = script.CurrentSchedulerIfCreated;

			while (!completed.Wait(20))
			{
				if (script.hasExited)
					throw new Keysharp.Builtins.Flow.UserRequestedExitException();

				if (IsDisposed)
					throw new ObjectDisposedException(nameof(ScriptEventScheduler));

				PumpDuringSynchronousWait(waitingScheduler);
			}
		}

		private void PumpDuringSynchronousWait(ScriptEventScheduler waitingScheduler)
			=> Keysharp.Internals.Flow.TryDoEvents(waitingScheduler, propagateExit: true, yieldTick: false);

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
				pumpScheduled = false;
			}
		}

		private void DisposeOwnedTimers()
		{
			if (script.FlowData.timers.RemoveOwned(this))
				script.ExitIfNotPersistent();
		}

		private void DisposeOwnedClipboardHandlers()
		{
			if (script.ClipFunctions.RemoveOwned(this))
				script.UpdateClipboardMonitoring();
		}

		private void DisposeOwnedMessageHandlers()
		{
			foreach (var kv in script.GuiData.onMessageHandlers.ToArray())
			{
				if (kv.Value == null || !kv.Value.RemoveOwned(this))
					continue;

				if (kv.Value.IsEmpty)
					_ = script.GuiData.onMessageHandlers.TryRemove(kv.Key, out _);
			}
		}

		private void DisposeOwnedWinEventHandlers()
		{
			_ = script.WinEventManagerIfExists?.RemoveOwned(this);
			_ = script.MonitorEventManagerIfExists?.RemoveOwned(this);
		}

		private void DisposeOwnedClrSubscriptions()
			=> _ = script.ClrEventManagerIfExists?.RemoveOwned(this);

		private void DisposeOwnedGuiHandlers()
		{
			foreach (var gui in new HashSet<Gui>(script.GuiData.allGuiHwnds.Values))
			{
				_ = gui?.form?.RemoveOwnedHandlers(this);

				foreach (var control in gui?.controls?.Values.OfType<Gui.Control>() ?? [])
					_ = control?.RemoveOwnedHandlers(this);
			}
		}

		private void DisposeOwnedMenuHandlers()
		{
			foreach (var kv in script.GuiData.allMenus.ToArray())
			{
				if (!kv.Value.TryGetTarget(out var menu))
				{
					_ = script.GuiData.allMenus.TryRemove(kv.Key, out _);
					continue;
				}

				_ = menu.RemoveOwnedHandlers(this);
			}
		}
	}
}
