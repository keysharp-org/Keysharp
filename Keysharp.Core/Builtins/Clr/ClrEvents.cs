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
			/// <para>
			/// Pausing detaches the delegate and <c>Start()</c> re-attaches it, rather than leaving a flagged handler in
			/// the invocation list: a present-but-silent handler would still be invoked, and for a delegate with a
			/// return value its empty result would overwrite the one an earlier handler produced. The cost is the
			/// handler's position in the invocation list after a resume, which the CLR decides anyway.
			/// </para>
			/// </summary>
			public sealed class EventSubscription : EventHook
			{
				internal ClrEventRegistration reg;

				internal EventSubscription() : base() { }

				/// <summary>The event this subscription is attached to.</summary>
				public string EventName => reg?.eventInfo.Name ?? "";

				/// <summary>The ManagedInstance or ManagedType the subscription is attached to.</summary>
				public object Target => reg?.scriptTarget ?? DefaultObject;

				public override bool InProgress => reg is { active: true, paused: false };

				public override string EndReason => reg?.EndReason ?? "";

				public override object Start()
				{
					reg?.Resume();
					return DefaultObject;
				}

				public override object Stop()
				{
					var r = reg;

					if (r != null && r.active)
					{
						r.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonStopped);
						r.manager.Unregister(r);
					}

					return DefaultObject;
				}

				public override object Pause()
				{
					reg?.Pause();
					return DefaultObject;
				}
			}

			/// <summary>Every live CLR event subscription this script made, oldest first (script: <c>Clr.Hooks</c>) —
			/// a snapshot to iterate and drop. It mixes instance and static events; filter with <c>Target</c>.</summary>
			public static object staticget_Hooks(object @this) => Script.TheScript.ClrEventManager.Hooks();

			/// <summary>
			/// Intercepts the event-related spellings on a managed node, before ordinary member dispatch sees them:
			/// <c>OnEvent(name, cb [, addRemove])</c>, and the compiler-generated <c>add_X</c>/<c>remove_X</c>
			/// accessors.
			/// <para>
			/// Routing the raw accessors through here rather than letting them reach the event directly is deliberate.
			/// They already worked, but every subscription made that way ran the callback on whatever thread raised the
			/// event and could never be removed again (a fresh Delegate is built per call, so remove_X silently matched
			/// nothing). Leaving that second, broken spelling in place would mean two code paths, one of them a trap.
			/// </para>
			/// <para>
			/// If a CLR type genuinely declares a member called <c>OnEvent</c>, this wins; that member stays reachable
			/// through its <c>add_</c>/<c>remove_</c> form or by another name.
			/// </para>
			/// </summary>
			private static bool TryEventCall(object instance, Type type, object scriptTarget, string name, object[] args, out object result)
			{
				result = null;

				// Every ordinary method call through a managed node passes here, so the cheap arity test comes first:
				// neither spelling can match any other count, and it rejects the common case without touching a string.
				if (args.Length == 1)
				{
					var isAdd = name.StartsWith("add_", StringComparison.OrdinalIgnoreCase);

					if (isAdd || name.StartsWith("remove_", StringComparison.OrdinalIgnoreCase))
					{
						// Only claim the call when it really names an event; a plain method called add_Something is not ours.
						if (type.GetEvent(name[(isAdd ? 4 : 7)..], EventFlags) is not EventInfo ev)
							return false;

						result = Subscribe(instance, type, scriptTarget, ev, args[0], isAdd ? 1L : 0L);
						return true;
					}

					return false;
				}

				if (args.Length < 2 || !name.Equals("OnEvent", StringComparison.OrdinalIgnoreCase))
					return false;

				var eventName = args[0].As();

				result = type.GetEvent(eventName, EventFlags) is EventInfo target
						 ? Subscribe(instance, type, scriptTarget, target, args[1], args.Length > 2 ? args[2] : null)
						 : Errors.MethodErrorOccurred($"Event '{eventName}' not found on {type.FullName}.");
				return true;
			}

			/// <summary>
			/// Subscribes <paramref name="callback"/> to <paramref name="ev"/>. Shared by ManagedInstance (instance
			/// events, <paramref name="instance"/> non-null) and ManagedType (static events).
			/// </summary>
			/// <param name="addRemove">1 = add (default), 0 = remove this callback.</param>
			private static object Subscribe(object instance, Type type, object scriptTarget, EventInfo ev, object callback, object addRemove)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				var script = Script.TheScript;
				var manager = script.ClrEventManager;
				var mode = addRemove.Al(1L);

				// Gui.OnEvent's -1 ("call before the handlers already registered") cannot be honoured: a multicast
				// delegate's invocation list is append-only from outside, and reordering it would mean detaching and
				// reattaching handlers this script may not own. Refusing it is honest where accepting it as 1 was not.
				if (mode is not (0L or 1L))
					return Errors.ValueErrorOccurred($"AddRemove must be 1 (add) or 0 (remove), not {mode}. A CLR event decides the order of its own handlers.", addRemove);

				if (mode == 0)
				{
					_ = manager.RemoveByCallback(instance, type, ev, fo);
					return DefaultObject;
				}

				var reg = new ClrEventRegistration(script, instance, type, ev, fo, scriptTarget, script.EventScheduler, manager);
				var subscription = new EventSubscription { reg = reg };

				// Set before registering, so the manager roots the handle from the moment it lists the registration:
				// dropping the handle then never detaches a live handler, matching every other event family.
				reg.scriptObject = subscription;
				return manager.Register(reg) ? subscription : DefaultObject;
			}
		}
	}

	/// <summary>
	/// One live CLR event subscription: the target, the event, the script callback, and the Delegate actually
	/// attached to the event (the only thing that can detach it again). It also owns the dispatch, so the delegate
	/// handed to the CLR can bind straight to <see cref="Dispatch"/> on this object -- a closure over the manager
	/// would allocate a display class per subscription to reach the same state.
	/// </summary>
	internal sealed class ClrEventRegistration(Script script, object instance, Type type, EventInfo eventInfo,
			KeysharpFunc callback, object scriptTarget, ScriptEventScheduler ownerScheduler, ClrEventManager manager)
	{
		private readonly Script script = script;
		// Held directly, like every other event family's registration, so Stop reaches its manager without the Script.
		internal readonly ClrEventManager manager = manager;
		internal readonly object instance = instance;               // null for a static event
		internal readonly Type type = type;
		internal readonly EventInfo eventInfo = eventInfo;
		internal readonly KeysharpFunc callback = callback;
		internal readonly object scriptTarget = scriptTarget;
		internal readonly ScriptEventScheduler ownerScheduler = ownerScheduler;
		internal Delegate handler;
		// Volatile: the CLR raises on any thread, and both are read there without the manager's gate.
		internal volatile bool active;
		internal volatile bool paused;
		internal object scriptObject;                              // the EventSubscription handle, rooted through here

		// Pairs the pause flag with whether the delegate is attached, so a pause racing a resume or a stop cannot
		// leave the handler attached while reporting paused, or detach it twice.
		private readonly Lock stateGate = new();
		private string endReason;

		/// <summary><c>""</c> while the subscription can still fire, otherwise why it ended.</summary>
		internal string EndReason => Volatile.Read(ref endReason) ?? "";

		/// <summary>Records why this subscription ended; the first reason wins. Written before liveness is cleared.</summary>
		internal void End(string reason) => _ = Interlocked.CompareExchange(ref endReason, reason, null);

		internal void Pause()
		{
			lock (stateGate)
			{
				if (!active || paused)
					return;

				paused = true;

				try
				{
					eventInfo.RemoveEventHandler(instance, handler);
				}
				catch
				{
				}
			}
		}

		internal void Resume()
		{
			lock (stateGate)
			{
				if (!active || !paused)
					return;

				try
				{
					eventInfo.AddEventHandler(instance, handler);
				}
				catch (Exception ex)
				{
					_ = ManagedInvoke.ThrowMapped(ex, $"{type.FullName}.{eventInfo.Name} (resume)");
					return;
				}

				paused = false;
			}
		}

		/// <summary>Marks the subscription dead and hands back the delegate that is still attached, if any, for the
		/// caller to remove outside every lock. A paused subscription has already detached it.</summary>
		internal Delegate Deactivate()
		{
			lock (stateGate)
			{
				var wasAttached = !paused;
				active = false;
				var del = Interlocked.Exchange(ref handler, null);
				return wasAttached ? del : null;
			}
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
			if (!active || paused)
				return DefaultObject;

			var scheduler = ownerScheduler;

			if (scheduler == null || scheduler.IsDisposed || script.hasExited)
				return DefaultObject;

			// Already on the owning thread: the caller is the script, so run in its current pseudo-thread and let the
			// value (and any exception) travel back to the CLR caller normally. This is the only path on which a
			// handler can cancel an event or return a value the raiser observes.
			if (scheduler.OwnsCurrentThread)
				return callback.Call(args);

			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunOnSchedulerThread(scheduler, args));
			return DefaultObject;
		}

		private ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler, object[] args)
		{
			// The same gate every event family applies when a queued callback would run: stopping or pausing takes
			// effect at the call, so an entry queued before it is discarded rather than delivered late.
			if (!active || paused)
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

			if (!thread.Started)
				return thread.Result;

			try
			{
				_ = callback.Call(args);
			}
			catch (Exception ex)
			{
				// Reported as an ordinary script error on the owning thread. Letting this escape would put an unhandled
				// exception on a threadpool thread, which takes the process down.
				_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
			}
			finally
			{
				script.ExitIfNotPersistent();
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

		/// <summary>The handle of every live subscription, oldest first. A snapshot, built outside the gate.</summary>
		internal Keysharp.Builtins.Array Hooks()
		{
			ClrEventRegistration[] all;

			lock (gate)
				all = [.. registrations];

			var result = new Keysharp.Builtins.Array(new List<object>(all.Length));

			foreach (var reg in all)
				_ = result.Push(reg.scriptObject);

			return result;
		}

		internal bool Register(ClrEventRegistration reg)
		{
			var handlerType = reg.eventInfo.EventHandlerType;

			if (handlerType == null)
			{
				_ = Errors.ErrorOccurred($"Event '{reg.eventInfo.Name}' has no handler type.");
				return false;
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
				_ = ManagedInvoke.ThrowMapped(ex, $"{reg.type.FullName}.{reg.eventInfo.Name} (subscribe)");
				return false;
			}

			reg.active = true;

			lock (gate)
			{
				if (!disposed)
				{
					registrations.Add(reg);
					return true;
				}
			}

			// Disposed while we were attaching: undo rather than leave a handler nothing will ever detach.
			reg.End(Keysharp.Internals.Events.EventSubscriptionBase.EndReasonExit);
			Detach(reg);
			return false;
		}

		internal void Unregister(ClrEventRegistration reg)
		{
			if (reg == null)
				return;

			lock (gate)
				_ = registrations.Remove(reg);

			Detach(reg);
		}

		/// <summary>Removes a subscription by (target, event, script function), for <c>OnEvent(..., 0)</c>.</summary>
		internal bool RemoveByCallback(object instance, Type type, EventInfo ev, KeysharpFunc callback)
			=> DetachWhere(r => ReferenceEquals(r.instance, instance)
						   && r.type == type
						   && r.eventInfo.MetadataToken == ev.MetadataToken
						   && ReferenceEquals(r.callback, callback), single: true,
						   Keysharp.Internals.Events.EventSubscriptionBase.EndReasonStopped);

		/// <summary>Detaches every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread's scheduler goes away).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
			=> scheduler != null && DetachWhere(r => ReferenceEquals(r.ownerScheduler, scheduler), single: false,
												Keysharp.Internals.Events.EventSubscriptionBase.EndReasonExit);

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

		/// <summary>
		/// Removes the matching registrations under the lock, then detaches them outside it -- RemoveEventHandler runs
		/// arbitrary CLR code, which must never happen while holding the gate.
		/// </summary>
		private bool DetachWhere(Func<ClrEventRegistration, bool> match, bool single, string reason)
		{
			List<ClrEventRegistration> hits = null;

			lock (gate)
			{
				for (var i = registrations.Count - 1; i >= 0; i--)
				{
					if (!match(registrations[i]))
						continue;

					(hits ??= []).Add(registrations[i]);
					registrations.RemoveAt(i);

					if (single)
						break;
				}
			}

			if (hits == null)
				return false;

			foreach (var reg in hits)
			{
				reg.End(reason);
				Detach(reg);
			}

			return true;
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
