namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Clr
		{
			private const BindingFlags EventFlags = BindingFlags.Public | BindingFlags.NonPublic
													| BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase;

			/// <summary>
			/// A live CLR event subscription. Returned by <c>OnEvent</c> on a <see cref="ManagedInstance"/> (instance
			/// events) or a <see cref="ManagedType"/> (static events, e.g. Microsoft.Win32.SystemEvents).
			/// <para>
			/// Holding the subscription is what makes detaching reliable. Removing a handler through the CLR requires
			/// the *same* Delegate instance that was added, and one is built per call from the script function object,
			/// so passing the same function to a raw remove_X accessor silently removes nothing. This object keeps the
			/// delegate it attached, so <see cref="Stop"/> always detaches the right one.
			/// </para>
			/// </summary>
			public sealed class EventSubscription : EventHook
			{
				private readonly object instance;                  // null for a static event
				private readonly Type declaringType;
				private readonly EventInfo eventInfo;
				private readonly object scriptTarget;

				internal EventSubscription(object instance, Type type, EventInfo eventInfo, object callback, object scriptTarget)
					: base(callback)
				{
					this.instance = instance;
					declaringType = type;
					this.eventInfo = eventInfo;
					this.scriptTarget = scriptTarget;
				}

				/// <summary>The event this subscription is attached to.</summary>
				public string EventName => eventInfo.Name;

				/// <summary>The ManagedInstance or ManagedType the subscription is attached to.</summary>
				public object Target => scriptTarget;

				// Each run attaches a delegate of its own when registered.
				private protected override Keysharp.Internals.Events.EventSubscriptionBase NewRun(ScriptEventScheduler owner)
				{
					var script = Script.TheScript;
					return new ClrEventRegistration(script, instance, declaringType, eventInfo, callback, owner, script.ClrEventManager);
				}
			}

			/// <summary>Every running CLR event subscription this script made, in start order (script: <c>Clr.Hooks</c>) —
			/// a snapshot to iterate and drop. It mixes instance and static events; filter with <c>Target</c>.</summary>
			public static object staticget_Hooks(object @this) => Script.TheScript.ClrEventManager.Hooks();

			/// <summary>
			/// Intercepts the event-related spellings on a managed node, before ordinary member dispatch sees them:
			/// <c>OnEvent(EventName, Callback)</c>, the one way to subscribe, and the compiler-generated
			/// <c>add_X</c>/<c>remove_X</c> accessors, which raise naming it. A raw accessor would run the callback on
			/// whatever thread raised the event, and could never be removed again, since a fresh delegate is built per
			/// call.
			/// <para>
			/// If a CLR type genuinely declares a member called <c>OnEvent</c>, this wins.
			/// </para>
			/// </summary>
			private static bool TryEventCall(object instance, Type type, object scriptTarget, string name, object[] args, out object result)
			{
				result = null;

				// Every ordinary method call through a managed node passes here, so the cheap tests come first: the
				// accessors take exactly one argument, and a name of another length fails the OnEvent test at once.
				var onEvent = name.Equals("OnEvent", StringComparison.OrdinalIgnoreCase);

				if (args.Length == 1 && !onEvent)
				{
					var isAdd = name.StartsWith("add_", StringComparison.OrdinalIgnoreCase);

					if (!isAdd && !name.StartsWith("remove_", StringComparison.OrdinalIgnoreCase))
						return false;

					// Only claim the call when it really names an event; a plain method called add_Something is not ours.
					var eventName = name[(isAdd ? 4 : 7)..];

					if (type.GetEvent(eventName, EventFlags) == null)
						return false;

					result = Errors.MethodErrorOccurred($"Subscribe with OnEvent(\"{eventName}\", Callback), and end the subscription with its Stop().");
					return true;
				}

				if (!onEvent)
					return false;

				// Any other count is still OnEvent, so it is refused as such rather than reported as a missing member.
				if (args.Length != 2)
					result = Errors.ValueErrorOccurred("OnEvent takes (EventName, Callback). End a subscription with the Stop() of the object OnEvent returned.");
				else if (type.GetEvent(args[0].As(), EventFlags) is EventInfo ev)
					result = Subscribe(instance, type, scriptTarget, ev, args[1]);
				else
					result = Errors.ValueErrorOccurred($"Event '{args[0].As()}' not found on {type.FullName}.");

				return true;
			}

			/// <summary>
			/// Subscribes <paramref name="callback"/> to <paramref name="ev"/>. Shared by ManagedInstance (instance
			/// events, <paramref name="instance"/> non-null) and ManagedType (static events).
			/// </summary>
			private static object Subscribe(object instance, Type type, object scriptTarget, EventInfo ev, object callback)
			{
				var argCount = ev.EventHandlerType?.GetMethod("Invoke")?.GetParameters().Length ?? 0;

				if (Functions.CheckedCallback(callback, argCount) is not { } fo)
					return DefaultObject;

				var subscription = new EventSubscription(instance, type, ev, fo, scriptTarget);
				_ = subscription.Start();
				return subscription;
			}
		}
	}

	/// <summary>
	/// One live CLR event subscription: the target, the event, the script callback, and the Delegate actually
	/// attached to the event (the only thing that can detach it again). It also owns the dispatch, so the delegate
	/// handed to the CLR can bind straight to <see cref="Dispatch"/> on this object -- a closure over the manager
	/// would allocate a display class per subscription to reach the same state.
	/// </summary>
	internal sealed class ClrEventRegistration(Script script, object instance, Type declaringType, EventInfo eventInfo,
			object callback, ScriptEventScheduler ownerScheduler, ClrEventManager manager)
		: Keysharp.Internals.Events.EventSubscriptionBase(callback, ownerScheduler, false)
	{
		private readonly Script script = script;
		internal readonly ClrEventManager manager = manager;
		internal readonly object instance = instance;               // null for a static event
		internal readonly Type declaringType = declaringType;
		internal readonly EventInfo eventInfo = eventInfo;
		internal Delegate handler;

		internal override void Register() => manager.Register(this);

		internal override void Unregister() => manager.Unregister(this);

		/// <summary>Marks the subscription dead and hands back the attached delegate for the caller to remove outside
		/// every lock. Only the first caller gets it, so it is detached once.</summary>
		internal Delegate Deactivate()
		{
			Clear();
			return Interlocked.Exchange(ref handler, null);
		}

		/// <summary>
		/// The threading rule: run inline when already on the owning script thread, otherwise enqueue.
		/// <para>
		/// Inline is not merely an optimisation. A delegate the script itself passed into a synchronous CLR call
		/// (a comparer, a predicate) and an event a CLR object raises as a direct result of a script-initiated call
		/// both arrive on the script thread, and handing those to the queue would either reorder them or deadlock --
		/// the script thread is inside the call and cannot pump until it returns.
		/// </para>
		/// <para>
		/// Off-thread, running the callback where it lands is what made A_ThreadId report the wrong pseudo-thread,
		/// left every ThreadVariables-backed value (A_LastError, Critical, CoordMode, SendMode) reading state
		/// belonging to another thread, allowed unsynchronised GUI access, and turned any error in the handler into an
		/// unhandled CLR exception that killed the process. Enqueueing fixes all four at once, because the callback
		/// then runs in a normal pseudo-thread on its owner.
		/// </para>
		/// </summary>
		internal object Dispatch(object[] args)
		{
			if (!IsRunning)
				return DefaultObject;

			var scheduler = OwnerScheduler;

			if (scheduler == null || scheduler.IsDisposed || script.hasExited)
				return DefaultObject;

			// Already on the owning thread: the caller is the script, so run in its current pseudo-thread and let the
			// value (and any exception) travel back to the CLR caller normally. This is the only path on which a
			// handler can cancel an event or return a value the raiser observes.
			if (scheduler.OwnsCurrentThread)
				return Script.InvokeOrNull(Callback, null, args);

			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunOnSchedulerThread(scheduler, args));
			return DefaultObject;
		}

		private ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler, object[] args)
		{
			// The same gate every event family applies when a queued callback would run: stopping takes effect at the
			// call, so an entry queued before it is discarded rather than delivered late.
			if (!IsRunning)
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

			if (!thread.Started)
				return thread.Result;

			try
			{
				_ = Script.InvokeOrNull(Callback, null, args);
			}
			catch (Exception ex) when (CallStack.Remember(ex))
			{
				// Reported as an ordinary script error on the owning thread. Letting this escape would put an unhandled
				// exception on a threadpool thread, which takes the process down.
				_ = Errors.ReportUncaught(ex);
			}

			return ScriptEventExecutionResult.Executed;
		}
	}

	/// <summary>
	/// Owns every live CLR event subscription so they can be detached deterministically -- on Stop(), when the
	/// owning thread's scheduler is disposed, and at engine teardown. That last one matters most: a subscription to a
	/// *static* event (Microsoft.Win32.SystemEvents and friends) is a root the CLR holds forever, so without an
	/// explicit sweep it keeps the callback, its closure and the engine behind it alive past dispose.
	/// </summary>
	internal sealed class ClrEventManager : IDisposable
	{
		private readonly Lock gate = new();
		private readonly List<ClrEventRegistration> registrations = [];
		private bool disposed;

		/// <summary>The handle of every running subscription, in start order. A snapshot, built outside the gate.</summary>
		internal Keysharp.Builtins.Array Hooks()
		{
			Keysharp.Internals.Events.EventSubscriptionBase[] all;

			lock (gate)
				all = [.. registrations];

			return Keysharp.Internals.Events.EventSubscriptionBase.Handles(all);
		}

		/// <summary>Attaches <paramref name="reg"/>'s delegate and lists it. A run that cannot be attached ends Failed,
		/// and the error is raised.</summary>
		internal void Register(ClrEventRegistration reg)
		{
			var handlerType = reg.eventInfo.EventHandlerType;

			if (handlerType == null)
			{
				reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonFailed);
				reg.Clear();
				_ = Errors.ErrorOccurred($"Event '{reg.eventInfo.Name}' has no handler type.");
				return;
			}

			try
			{
				// The shim runs Dispatch, not the script function, so the marshalling decision is made per event.
				var del = ClrDelegateMarshaler.FromKeysharpFunc(handlerType, new ClrCallbackShim(reg.Dispatch));
				reg.eventInfo.AddEventHandler(reg.instance, del);
				reg.handler = del;
			}
			catch (Exception ex)
			{
				reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonFailed);
				reg.Clear();
				_ = ManagedInvoke.ThrowMapped(ex, $"{reg.declaringType.FullName}.{reg.eventInfo.Name} (subscribe)");
				return;
			}

			var scheduler = reg.OwnerScheduler;

			// Listing under the owner's cleanup gate closes the gap between attaching the delegate and scheduler
			// teardown: cleanup either sees this run, or registration is refused and the delegate is detached below.
			if (scheduler != null ? scheduler.TryRegisterOwnedResource(AddCore) : AddCore())
				return;

			// Stopped or disposed while we were attaching: undo rather than leave a handler nothing will ever detach.
			// A Stop() already recorded its reason, which the first-reason rule keeps.
			reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonExit);
			Detach(reg);

			bool AddCore()
			{
				lock (gate)
				{
					if (disposed || !reg.IsRunning)
						return false;

					registrations.Add(reg);
					return true;
				}
			}
		}

		internal void Unregister(ClrEventRegistration reg)
		{
			if (reg == null)
				return;

			// Cleared under the gate, so a Register racing this Stop cannot list it after it was taken out.
			lock (gate)
			{
				_ = registrations.Remove(reg);
				reg.Clear();
			}

			Detach(reg);
		}

		/// <summary>Detaches every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread's scheduler goes away).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			List<ClrEventRegistration> owned = null;

			lock (gate)
			{
				for (var i = registrations.Count - 1; i >= 0; i--)
				{
					if (!ReferenceEquals(registrations[i].OwnerScheduler, scheduler))
						continue;

					(owned ??= []).Add(registrations[i]);
					registrations.RemoveAt(i);
				}
			}

			if (owned == null)
				return false;

			// Outside the gate: RemoveEventHandler runs arbitrary CLR code.
			foreach (var reg in owned)
			{
				reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonExit);
				Detach(reg);
			}

			return true;
		}

		public void Dispose()
		{
			List<ClrEventRegistration> all;

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				all = [.. registrations];
				registrations.Clear();
			}

			foreach (var reg in all)
			{
				reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonExit);
				Detach(reg);
			}
		}


		private static void Detach(ClrEventRegistration reg)
		{
			var del = reg.Deactivate();

			if (del == null)
				return;

			// A target that is already disposed (or a static event on a type being torn down) can throw here. Teardown
			// must not fail because of it: the subscription is going away either way.
			try
			{
				reg.eventInfo.RemoveEventHandler(reg.instance, del);
			}
			catch
			{
			}
		}
	}
}
