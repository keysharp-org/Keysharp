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
	/// chain. Keysharp's hand-rolled OnError loop (<c>Errors.cs:41</c>) applies the same rule inline.</item>
	/// <item><see cref="NonEmpty"/> — the <c>(aMsg, aMsgType, aGui)</c> overload:
	/// <c>if (result == EARLY_RETURN) break;</c>, where EARLY_RETURN means <c>CallMethod</c> saw a non-blank
	/// return (<c>script_object.cpp:53</c>, via <c>TokenIsBlank</c>). Used only by GUI event/message monitors,
	/// so <c>return 0</c> DOES stop the chain and reply 0, while <c>return ""</c> or no return continues.</item>
	/// </list>
	/// <para>
	/// Kept here, non-generic, because the rule has nothing to do with the registration type, and because a
	/// caller deciding "was this handled?" must test the very predicate the loop broke on rather than restate it.
	/// </para>
	/// </summary>
	internal static class CallbackStop
	{
		internal static readonly Func<object, bool> NonZero = static r => r.Al() != 0L;
		internal static readonly Func<object, bool> NonEmpty = static r => !r.IsNullOrEmpty();
	}

	internal sealed class CallbackRegistry<TRegistration> where TRegistration : CallbackRegistration
	{
		private readonly Lock gate = new();
		private readonly List<TRegistration> ordered = [];
		private readonly Dictionary<CallbackRegistrationKey, List<TRegistration>> byCallbackAndScheduler = [];
		private readonly Dictionary<ScriptEventScheduler, List<TRegistration>> byScheduler = new(ReferenceEqualityComparer<ScriptEventScheduler>.Instance);
		private Script script;
		private TRegistration[] snapshot = [];
		private bool snapshotDirty = true;

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

		internal bool Add(TRegistration registration, bool addFirst = false)
		{
			if (registration == null)
				return false;

			var scheduler = registration.OwnerScheduler;

			if (scheduler == null)
				return AddCore(registration, addFirst);

			// Adding under the scheduler's cleanup gate is what makes "registered" and "will be cleaned up" one
			// step: a scheduler that has already torn down refuses, leaving the registration inactive rather than
			// stranded in a registry nothing will ever sweep.
			if (scheduler.TryRegisterOwnedResource(() => AddCore(registration, addFirst)))
				return true;

			registration.SetActive(false);
			return false;
		}

		private bool AddCore(TRegistration registration, bool addFirst)
		{
			lock (gate)
			{
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

		internal TRegistration Find(KeysharpFunc callback, ScriptEventScheduler scheduler)
		{
			lock (gate)
				return byCallbackAndScheduler.TryGetValue(new CallbackRegistrationKey(callback, scheduler), out var registrations) && registrations.Count > 0
					? registrations[^1]
					: null;
		}

		internal bool Remove(KeysharpFunc callback, ScriptEventScheduler scheduler, bool matchScheduler = true)
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

					if (!Equals(registration.Callback, callback))
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

		internal bool ModifyEventHandlers(KeysharpFunc callback, long addRemove, Func<KeysharpFunc, long, TRegistration> createRegistration = null, bool matchCurrentSchedulerOnRemove = true)
		{
			if (callback == null)
				return false;

			Script currentOwner;

			lock (gate)
				currentOwner = script;

			var currentScheduler = (currentOwner ?? Script.TheScript)?.EventScheduler;

			if (createRegistration == null)
			{
				if (typeof(TRegistration) != typeof(CallbackRegistration))
					throw new InvalidOperationException($"A registration factory is required for {typeof(TRegistration).Name}.");

				createRegistration = (Func<KeysharpFunc, long, TRegistration>)(object)new Func<KeysharpFunc, long, CallbackRegistration>(
					(cb, _) => new CallbackRegistration(cb, currentScheduler, true));
			}

			if (addRemove > 0)
				return Add(createRegistration(callback, addRemove));

			if (addRemove < 0)
				return Add(createRegistration(callback, addRemove), true);

			return Remove(callback, matchCurrentSchedulerOnRemove ? currentScheduler : null, matchCurrentSchedulerOnRemove);
		}

		internal bool ModifyGlobalEventHandlers(KeysharpFunc callback, long addRemove)
		{
			if (typeof(TRegistration) != typeof(CallbackRegistration))
				throw new InvalidOperationException($"Global callback registration is only supported for {typeof(CallbackRegistration).Name}.");

			return ModifyEventHandlers(callback, addRemove, (Func<KeysharpFunc, long, TRegistration>)(object)CallbackRegistration.CreateGlobal, false);
		}

		/// <summary>
		/// Invoke all registered event handlers, with each being called in its own pseudo-thread.<br/>
		/// If any event handler returns a non-zero result, no further calls are made.
		/// </summary>
		/// <param name="args">The parameters to pass to each event handler.</param>
		/// <returns>The result of the last event handler that was called.</returns>
		internal object InvokeEventHandlers(params object[] args) =>
			InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, checkPersistence: true);

		/// <summary>
		/// Invoke handlers as part of the exit sequence (i.e. OnExit). Each is launched on a pseudo-thread with the
		/// admission AHK specifies for the OnExit thread: skipUninterruptible (starts even though the exit sequence
		/// has disabled interruption) and allowEmergencyOverflow (does not obey #MaxThreads — always launches). While
		/// it runs it is uninterruptible because ExitAppInternal keeps allowInterruption=false throughout. Persistence
		/// is NOT checked here (checkPersistence: false) — the caller (ExitAppInternal) drives the real exit and
		/// honours any non-zero (veto) return. Without this admission, OnExit handlers silently never run: the exit
		/// path sets allowInterruption=false first, so a normal (interruptible) start request is refused at the gate.
		/// </summary>
		internal object InvokeExitHandlers(params object[] args) =>
			InvokeHandlers(args, skipUninterruptible: true, allowEmergencyOverflow: true, checkPersistence: false);

		/// <summary>
		/// Invoke handlers for a window message (GuiObj.OnMessage / GuiCtrlObj.OnMessage). Identical to
		/// <see cref="InvokeEventHandlers"/> except that the chain stops on any NON-EMPTY return
		/// (<see cref="CallbackStop.NonEmpty"/>) rather than a non-zero one.
		/// </summary>
		/// <param name="args">The parameters to pass to each event handler.</param>
		/// <returns>The result of the last event handler that was called.</returns>
		internal object InvokeWindowMessageHandlers(params object[] args) =>
			InvokeHandlers(args, skipUninterruptible: false, allowEmergencyOverflow: false, checkPersistence: true, CallbackStop.NonEmpty);

		private object InvokeHandlers(object[] args, bool skipUninterruptible, bool allowEmergencyOverflow, bool checkPersistence, Func<object, bool> stopWhen = null)
		{
			stopWhen ??= CallbackStop.NonZero;
			object result = null;
			var snapshot = GetSnapshot();

			if (snapshot.Length == 0)
				return result;

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
			ScriptEventExecutionResult RunHandler(ScriptEventScheduler scheduler, Script script, KeysharpFunc handler, long priority)
			{
				var oldEventInfo = A_EventInfo;
				using var thread = scheduler.StartPseudoThreadScope(priority, skipUninterruptible, false, allowEmergencyOverflow, ThreadKind.Event);

				if (!thread.Started)
					return thread.Result;

				try
				{
					var tv = thread.ThreadVariables;
					tv.eventInfo = oldEventInfo;
					tv.hwndLastUsed = 0L;

					if (inst is Control ctrl && ctrl.FindForm() is Form form)
						script.HwndLastUsed = form.Handle;

					result = handler.Call(args);
				}
				catch (Exception ex)
				{
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
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
					result = null;
				}
				else if (targetScheduler.OwnsCurrentThread)
				{
					executionResult = RunHandler(targetScheduler, script, handler, priority);
				}
				else
				{
					executionResult = targetScheduler.InvokeSynchronous(() => RunHandler(targetScheduler, script, handler, priority));
				}

				if (executionResult != ScriptEventExecutionResult.Executed)
					continue;

				if (stopWhen(result))
					break;
			}

			if (checkPersistence)
				registryOwner?.ExitIfNotPersistent();

			return result;
		}

		internal static bool RemoveOwned<TKey>(ConcurrentDictionary<TKey, CallbackRegistry<TRegistration>> hubs, ScriptEventScheduler scheduler)
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

	internal readonly record struct CallbackRegistrationKey(KeysharpFunc Callback, ScriptEventScheduler Scheduler)
	{
		public bool Equals(CallbackRegistrationKey other)
			=> Equals(Callback, other.Callback) && ReferenceEquals(Scheduler, other.Scheduler);

		public override int GetHashCode()
		{
			unchecked
			{
				return ((Callback?.GetHashCode() ?? 0) * 397) ^ (Scheduler != null ? RuntimeHelpers.GetHashCode(Scheduler) : 0);
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
