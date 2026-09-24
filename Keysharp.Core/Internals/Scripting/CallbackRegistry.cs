using Keysharp.Builtins;
using System.Runtime.CompilerServices;

using Keysharp.Internals.Invoke;

namespace Keysharp.Internals.Scripting
{
	/// <summary>
	/// The rules for "this callback consumed the event, stop calling the rest".
	/// <para>
	/// AHK has exactly two, and which one applies is a property of the callback family — see the two
	/// <c>MsgMonitorList::Call</c> overloads in <c>source/script_object.cpp</c>:
	/// </para>
	/// <list type="bullet">
	/// <item><see cref="NonZero"/> — the <c>(aInitNewThreadIndex)</c> overload: <c>if (retval) break;</c>. Used
	/// by OnError, OnExit and OnClipboardChange, so both <c>return 0</c> and <c>return ""</c> continue the
	/// chain. Keysharp's hand-rolled OnError loop (<c>Errors.CallOnErrorHandlers</c>) applies the same rule inline.</item>
	/// <item><see cref="NonEmpty"/> — the <c>(aMsg, aMsgType, aGui)</c> overload:
	/// <c>if (result == EARLY_RETURN) break;</c>, where EARLY_RETURN means <c>CallMethod</c> saw a non-blank
	/// return (<c>script_object.cpp:53</c>, via <c>TokenIsBlank</c>). Used by every GUI event chain (OnEvent,
	/// OnNotify, OnCommand, OnMessage), so <c>return 0</c> DOES stop the chain, while <c>return ""</c> or no
	/// return continues. What the stopping value means is the event's own business: Close stays open only on a
	/// non-zero one.</item>
	/// </list>
	/// <para>
	/// Kept here, non-generic, because the rule has nothing to do with the registration type, and because a
	/// caller deciding "was this handled?" must test the very predicate the loop broke on rather than restate it.
	/// A registry takes its rule when constructed, so every Gui and GuiControl registry passes NonEmpty.
	/// </para>
	/// </summary>
	internal static class CallbackStop
	{
		internal static readonly Func<object, bool> NonZero = static r => r.Al() != 0L;
		internal static readonly Func<object, bool> NonEmpty = static r => !r.IsNullOrEmpty();
	}

	internal class CallbackRegistry<TRegistration> where TRegistration : CallbackRegistration
	{
		private readonly Lock gate = new();
		private readonly List<TRegistration> ordered = [];
		private readonly Dictionary<CallbackRegistrationKey, List<TRegistration>> byCallbackAndScheduler = [];
		private readonly Dictionary<ScriptEventScheduler, List<TRegistration>> byScheduler = new(ReferenceEqualityComparer<ScriptEventScheduler>.Instance);
		private Script script;
		private TRegistration[] snapshot = [];
		private bool snapshotDirty = true;
		private readonly Func<object, bool> stopRule;
		private readonly string threadName;

		/// <param name="stopRule">The <see cref="CallbackStop"/> rule that ends this family's chain; NonZero when omitted.</param>
		/// <param name="threadName">What Error.Stack calls a thread a handler runs in, as AutoHotkey calls it, such as Gui;
		/// <c>Event</c> when omitted.</param>
		internal CallbackRegistry(Func<object, bool> stopRule = null, string threadName = null)
		{
			this.stopRule = stopRule ?? CallbackStop.NonZero;
			this.threadName = threadName;
		}

		internal int Count
		{
			get
			{
				lock (gate)
					return ordered.Count;
			}
		}

		internal bool IsEmpty
		{
			get
			{
				lock (gate)
					return ordered.Count == 0;
			}
		}

		internal bool Add(TRegistration registration, bool addFirst = false) => Add(registration, addFirst, false, out _);

		// With unique, a callback already registered for the registration's owner is not added, and present says so.
		private bool Add(TRegistration registration, bool addFirst, bool unique, out bool present)
		{
			present = false;

			if (registration == null)
				return false;

			var scheduler = registration.OwnerScheduler;

			if (scheduler == null)
				return AddCore(registration, addFirst, unique, ref present);

			var found = false;

			// Adding under the scheduler's cleanup gate is what makes "registered" and "will be cleaned up" one
			// step: a scheduler that has already torn down refuses, leaving the registration inactive rather than
			// stranded in a registry nothing will ever sweep.
			if (scheduler.TryRegisterOwnedResource(() => AddCore(registration, addFirst, unique, ref found)))
				return true;

			if (!(present = found))
				registration.SetActive(false);

			return false;
		}

		private bool AddCore(TRegistration registration, bool addFirst, bool unique, ref bool present)
		{
			lock (gate)
			{
				// Decided under the gate the add takes, so two threads registering one callback at once add it once.
				if (unique && byCallbackAndScheduler.TryGetValue(new CallbackRegistrationKey(registration.Callback, registration.OwnerScheduler), out var existing)
						&& existing.Count > 0)
				{
					present = true;
					return false;
				}

				script ??= registration.OwnerScheduler?.Owner ?? Script.TheScript;

				if (addFirst)
					ordered.Insert(0, registration);
				else
					ordered.Add(registration);

				IndexAdd(registration);
				snapshotDirty = true;
				return true;
			}
		}

		internal TRegistration[] GetSnapshot()
		{
			lock (gate)
			{
				EnsureSnapshotLocked();
				return snapshot;
			}
		}

		internal TRegistration Find(object callback, ScriptEventScheduler scheduler)
		{
			lock (gate)
				return byCallbackAndScheduler.TryGetValue(new CallbackRegistrationKey(callback, scheduler), out var registrations) && registrations.Count > 0
					? registrations[^1]
					: null;
		}

		internal bool Remove(object callback, ScriptEventScheduler scheduler, bool matchScheduler = true)
		{
			lock (gate)
			{
				if (matchScheduler)
				{
					if (!byCallbackAndScheduler.TryGetValue(new CallbackRegistrationKey(callback, scheduler), out var registrations) || registrations.Count == 0)
						return false;

					return RemoveRegistrationsLocked([.. registrations]);
				}

				List<TRegistration> removals = null;

				for (var i = ordered.Count - 1; i >= 0; i--)
				{
					var registration = ordered[i];

					if (!Functions.SameCallback(registration.Callback, callback))
						continue;

					(removals ??= []).Add(registration);
				}

				return removals != null && RemoveRegistrationsLocked(removals);
			}
		}

		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			lock (gate)
			{
				if (!byScheduler.TryGetValue(scheduler, out var registrations) || registrations.Count == 0)
					return false;

				return RemoveRegistrationsLocked([.. registrations]);
			}
		}

		internal bool Remove(Predicate<TRegistration> shouldRemove)
		{
			lock (gate)
			{
				if (ordered.Count == 0)
					return false;

				var removals = new List<TRegistration>();

				for (var i = ordered.Count - 1; i >= 0; i--)
				{
					if (shouldRemove(ordered[i]))
						removals.Add(ordered[i]);
				}

				return removals.Count != 0 && RemoveRegistrationsLocked(removals);
			}
		}

		internal void Clear()
		{
			lock (gate)
			{
				foreach (var registration in ordered)
					registration.SetActive(false);

				ordered.Clear();
				byCallbackAndScheduler.Clear();
				byScheduler.Clear();
				snapshot = [];
				snapshotDirty = false;
			}
		}

		/// <summary>The scheduler a registration made right now belongs to: the one that owns this registry, or the
		/// current script's when the registry has not seen an owner yet.</summary>
		protected ScriptEventScheduler CurrentScheduler
		{
			get
			{
				Script currentOwner;

				lock (gate)
					currentOwner = script;

				return (currentOwner ?? Script.TheScript)?.EventScheduler;
			}
		}

		internal bool ModifyEventHandlers(object callback, long addRemove, Func<object, long, TRegistration> createRegistration, bool matchCurrentSchedulerOnRemove = true)
		{
			if (callback == null)
				return false;

			if (addRemove == 0)
				return Remove(callback, matchCurrentSchedulerOnRemove ? CurrentScheduler : null, matchCurrentSchedulerOnRemove);

			var registration = createRegistration(callback, addRemove);

			// A callback its owner already registered stays where it is and is not added again, as AHK's OnScriptEvent
			// and Gui OnEvent find it first.
			var added = Add(registration, addRemove < 0, true, out var present);

			if (present)
				registration.Clear();

			return added || present;
		}

		/// <summary>
		/// Invoke all registered event handlers, each in its own pseudo-thread, from the scheduler's queue
		/// rather than from the caller.
		/// <para>
		/// AutoHotkey raises these by posting a window message (<c>POST_AHK_GUI_ACTION</c>) and launching the
		/// thread when the message loop reaches it. Two things follow, and both are the point of queueing here:
		/// a handler never runs inside the code which raised the event, so a resize or a click cannot re-enter
		/// the script mid-statement; and an event raised while no thread may launch — the raising thread is
		/// still inside its uninterruptible window, or is Critical — waits rather than being lost.</para>
		/// <para>
		/// The chain is queued as one item, not one per handler, so a handler which stops the chain still stops
		/// the ones after it. Nothing can read a result from here; an event whose return value decides what
		/// happens next needs <see cref="InvokeSynchronousEventHandlers"/> or
		/// <see cref="InvokeWindowMessageHandlers"/>.</para>
		/// </summary>
		/// <param name="args">The parameters to pass to each event handler.</param>
		internal void InvokeEventHandlers(params object[] args)
		{
			if (IsEmpty || CurrentScheduler is not { } scheduler)
				return;

			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0,
								  () => InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, stopWhen: null, out _));
		}

		/// <summary>
		/// Invoke handlers for an event whose return value decides what happens next, such as Close, where a
		/// handler returning non-zero keeps the window open. Runs inline, because the answer is needed before
		/// the caller can go on.
		/// </summary>
		/// <param name="args">The parameters to pass to each event handler.</param>
		/// <returns>The result of the last event handler that was called.</returns>
		internal object InvokeSynchronousEventHandlers(params object[] args)
		{
			_ = InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, stopWhen: null, out var result);
			return result;
		}

		/// <summary>
		/// Invoke handlers as part of the exit sequence (i.e. OnExit). Each is launched on a pseudo-thread with the
		/// admission AHK specifies for the OnExit thread: skipUninterruptible (starts even though the exit sequence
		/// has disabled interruption) and allowEmergencyOverflow (does not obey #MaxThreads — always launches). While
		/// it runs it is uninterruptible because ExitAppInternal keeps allowInterruption=false throughout. Persistence
		/// is not checked here: the caller (ExitAppInternal) drives the real exit and
		/// honours any non-zero (veto) return. Without this admission, OnExit handlers silently never run: the exit
		/// path sets allowInterruption=false first, so a normal (interruptible) start request is refused at the gate.
		/// </summary>
		internal object InvokeExitHandlers(params object[] args)
		{
			_ = InvokeHandlers(args, skipUninterruptible: true, allowEmergencyOverflow: true, stopWhen: null, out var result);
			return result;
		}

		/// <summary>
		/// Invoke handlers inline for a window message (GuiObj.OnMessage / GuiCtrlObj.OnMessage) or another event
		/// whose non-empty return claims it. The chain stops on <see cref="CallbackStop.NonEmpty"/> whatever the
		/// registry's own rule, because callers test that same predicate to decide whether it was claimed.
		/// </summary>
		/// <param name="args">The parameters to pass to each event handler.</param>
		/// <returns>The result of the last event handler that was called.</returns>
		internal object InvokeWindowMessageHandlers(params object[] args)
		{
			_ = InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, CallbackStop.NonEmpty, out var result);
			return result;
		}

		/// <summary>
		/// Runs the chain from inside an item already on the scheduler's queue, for an event whose one queued item spans
		/// more than one registry (ContextMenu: the control's, then the window's). The status is the one that item reports
		/// back to the queue, as <see cref="InvokeEventHandlers"/> reports it.
		/// </summary>
		internal ScriptEventExecutionResult InvokeQueuedEventHandlers(object[] args, out object result)
			=> InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, stopWhen: null, out result);

		/// <summary>
		/// Runs the chain. The status is what the scheduler's queue reads: a chain whose FIRST handler could not
		/// start is reported blocked so the queue keeps it and tries again, while one which got going reports
		/// Executed, since replaying it would call the handlers that already ran a second time.
		/// </summary>
		private ScriptEventExecutionResult InvokeHandlers(object[] args, bool skipUninterruptible, bool allowEmergencyOverflow,
				Func<object, bool> stopWhen, out object result)
		{
			stopWhen ??= stopRule;
			var anyRan = false;
			var blocked = ScriptEventExecutionResult.Executed;
			//A local rather than the out parameter directly: RunHandler below assigns it, and a local function
			//cannot capture an out parameter.
			object chainResult = null;
			var failed = false;
			result = null;
			var snapshot = GetSnapshot();

			if (snapshot.Length == 0)
				return ScriptEventExecutionResult.Executed;

			var inst = args.Length > 0 ? args[0].GetControl() : null;
			Script registryOwner;

			lock (gate)
				registryOwner = script;

			// Run one handler in a fresh pseudo-thread on the given scheduler's own thread. Kept as a LOCAL function
			// so it captures the admission flags/args/inst/result rather than threading them through a separate
			// static method and its two call sites. For the OnExit sequence both admission flags are set (see
			// InvokeExitHandlers): skipUninterruptible starts the thread even though the exit sequence has disabled
			// interruption, and allowEmergencyOverflow bypasses #MaxThreads. That thread still runs UNINTERRUPTIBLE for
			// free: ExitAppInternal holds allowInterruption=false for the whole handler, so any hotkey/menu/timer that
			// tries to launch while it runs is refused at that same gate. Do NOT also pass isCritical: on a veto the
			// exit is cancelled and the script keeps running, and a leftover Critical scope then wedges later thread
			// launches (subsequent timers/hotkeys stop firing).
			ScriptEventExecutionResult RunHandler(ScriptEventScheduler scheduler, Script script, object handler, long priority)
			{
				var oldEventInfo = A_EventInfo;
				using var thread = scheduler.StartPseudoThreadScope(priority, skipUninterruptible, false, allowEmergencyOverflow, ThreadKind.Event);

				if (!thread.Started)
					return thread.Result;

				if (threadName != null)
					CallStack.Current.NameThread(threadName);

				try
				{
					var tv = thread.ThreadVariables;
					tv.eventInfo = oldEventInfo;
					tv.hwndLastUsed = 0L;

					if (inst is Control ctrl && ctrl.FindForm() is Form form)
						script.HwndLastUsed = form.Handle;

					chainResult = Script.InvokeOrNull(handler, null, args);
				}
				catch (Exception ex) when (CallStack.Remember(ex))
				{
					chainResult = null;
					// ReportUncaught is true only for Exit; anything else is an uncaught error which ended the thread.
					failed = !Errors.ReportUncaught(ex);
				}

				return ScriptEventExecutionResult.Executed;
			}

			foreach (var entry in snapshot)
			{
				if (entry == null || !entry.IsActive)
					continue;

				var handler = entry.Callback;

				if (handler == null)
					continue;

				var priority = entry.Priority;   // per-registration thread priority (0 except for menu items' "Pn")
				var targetScheduler = entry.OwnerScheduler ?? registryOwner?.EventScheduler;

				if (targetScheduler == null)
					continue;

				var script = targetScheduler.Owner;
				ScriptEventExecutionResult executionResult;

				if (targetScheduler.IsDisposed)
				{
					executionResult = ScriptEventExecutionResult.Dropped;
					chainResult = null;
				}
				else if (targetScheduler.OwnsCurrentThread)
				{
					executionResult = RunHandler(targetScheduler, script, handler, priority);
				}
				else
				{
					executionResult = targetScheduler.InvokeSynchronous(() => RunHandler(targetScheduler, script, handler, priority));
				}

				if (executionResult is ScriptEventExecutionResult.GlobalBlocked or ScriptEventExecutionResult.LocalBlocked)
					blocked = executionResult;

				if (executionResult != ScriptEventExecutionResult.Executed)
					continue;

				anyRan = true;

				// Both of AHK's MsgMonitorList::Call overloads break on FAIL (an uncaught error) whatever the stop rule,
				// but not on EARLY_EXIT, so Exit ends only its own handler.
				if (failed || stopWhen(chainResult))
					break;
			}

			result = chainResult;
			return anyRan ? ScriptEventExecutionResult.Executed : blocked;
		}

		private bool RemoveRegistrationsLocked(IReadOnlyCollection<TRegistration> removals)
		{
			if (removals == null || removals.Count == 0)
				return false;

			foreach (var registration in removals)
			{
				registration.SetActive(false);
				IndexRemove(registration);
			}

			if (removals.Count == 1)
			{
				foreach (var registration in removals)
					_ = ordered.Remove(registration);
			}
			else
			{
				var removalSet = new HashSet<TRegistration>(removals);
				_ = ordered.RemoveAll(removalSet.Contains);
			}

			snapshotDirty = true;
			return true;
		}

		private void EnsureSnapshotLocked()
		{
			if (!snapshotDirty)
				return;

			snapshot = ordered.Count != 0 ? [.. ordered] : [];
			snapshotDirty = false;
		}

		private void IndexAdd(TRegistration registration)
		{
			if (registration.Callback != null)
			{
				byCallbackAndScheduler.GetOrAdd(new CallbackRegistrationKey(registration.Callback, registration.OwnerScheduler), static () => []).Add(registration);
			}

			if (registration.OwnerScheduler != null)
				byScheduler.GetOrAdd(registration.OwnerScheduler, static () => []).Add(registration);
		}

		private void IndexRemove(TRegistration registration)
		{
			if (registration.Callback != null)
			{
				RemoveFromIndex(byCallbackAndScheduler, new CallbackRegistrationKey(registration.Callback, registration.OwnerScheduler), registration);
			}

			if (registration.OwnerScheduler != null)
				RemoveFromIndex(byScheduler, registration.OwnerScheduler, registration);
		}

		private static void RemoveFromIndex<TKey>(Dictionary<TKey, List<TRegistration>> index, TKey key, TRegistration registration) where TKey : notnull
		{
			if (!index.TryGetValue(key, out var registrations))
				return;

			_ = registrations.Remove(registration);

			if (registrations.Count == 0)
				_ = index.Remove(key);
		}
	}

	/// <summary>
	/// A registry of plain <see cref="CallbackRegistration"/> entries — every handler family except OnMessage,
	/// which carries per-registration instance limits and so supplies its own registration type. Because the
	/// registration type is known here, callers need not pass a factory, and "register globally" (no owning
	/// scheduler, for OnError/OnExit) is expressible at all.
	/// </summary>
	internal sealed class CallbackRegistry : CallbackRegistry<CallbackRegistration>
	{
		internal CallbackRegistry(Func<object, bool> stopRule = null, string threadName = null) : base(stopRule, threadName) { }

		internal bool ModifyEventHandlers(object callback, long addRemove, bool matchCurrentSchedulerOnRemove = true)
		{
			var scheduler = CurrentScheduler;
			return ModifyEventHandlers(callback, addRemove, (cb, _) => new CallbackRegistration(cb, scheduler, true), matchCurrentSchedulerOnRemove);
		}

		/// <summary>Registers a handler owned by no scheduler, so it survives the registering thread and is removed
		/// by callback identity from any thread (OnError/OnExit).</summary>
		internal bool ModifyGlobalEventHandlers(object callback, long addRemove)
			=> ModifyEventHandlers(callback, addRemove, CallbackRegistration.CreateGlobal, false);

		/// <summary>Sweeps a scheduler's registrations out of a keyed set of registries, dropping the ones left
		/// empty (Gui/GuiControl keep one registry per message or notification code).</summary>
		internal static bool RemoveOwned<TKey>(ConcurrentDictionary<TKey, CallbackRegistry> hubs, ScriptEventScheduler scheduler)
		{
			if (hubs == null || scheduler == null)
				return false;

			var removedAny = false;

			foreach (var kv in hubs.ToArray())
			{
				if (!kv.Value.RemoveOwned(scheduler))
					continue;

				removedAny = true;

				if (kv.Value.IsEmpty)
					_ = hubs.TryRemove(kv.Key, out _);
			}

			return removedAny;
		}
	}

	internal readonly record struct CallbackRegistrationKey(object Callback, ScriptEventScheduler Scheduler)
	{
		public bool Equals(CallbackRegistrationKey other)
			=> Functions.SameCallback(Callback, other.Callback) && ReferenceEquals(Scheduler, other.Scheduler);

		public override int GetHashCode()
		{
			unchecked
			{
				return (Functions.CallbackHash(Callback) * 397) ^ (Scheduler != null ? RuntimeHelpers.GetHashCode(Scheduler) : 0);
			}
		}
	}

	internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
	{
		internal static readonly ReferenceEqualityComparer<T> Instance = new();

		public bool Equals(T x, T y) => ReferenceEquals(x, y);
		public int GetHashCode(T obj) => obj != null ? RuntimeHelpers.GetHashCode(obj) : 0;
	}
}
