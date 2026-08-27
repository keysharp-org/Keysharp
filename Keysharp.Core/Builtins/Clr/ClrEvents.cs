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
			public sealed class EventSubscription : KeysharpObject
			{
				internal ClrEventRegistration reg;

				/// <summary>The event this subscription is attached to.</summary>
				public string EventName => reg?.eventInfo.Name ?? "";

				/// <summary>True while the subscription is still attached.</summary>
				public bool IsActive => reg?.active ?? false;

				/// <summary>The ManagedInstance or ManagedType the subscription is attached to.</summary>
				public object Target => reg?.scriptTarget ?? DefaultObject;

				/// <summary>Detaches the handler. Idempotent.</summary>
				public object Stop()
				{
					var r = reg;

					if (r != null && r.active)
						r.manager.Unregister(r);

					return DefaultObject;
				}

				public override object __Delete()
				{
					_ = Stop();
					return base.__Delete();
				}
			}

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
			/// <param name="addRemove">1 = add (default), -1 = add ahead of previously registered handlers,
			/// 0 = remove this callback. Matches Gui.OnEvent/OnMessage.</param>
			private static object Subscribe(object instance, Type type, object scriptTarget, EventInfo ev, object callback, object addRemove)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				var script = Script.TheScript;
				var manager = script.ClrEventManager;

				if (addRemove.Al(1L) == 0)
				{
					_ = manager.RemoveByCallback(instance, type, ev, fo);
					return DefaultObject;
				}

				// -1 ("call before previously registered handlers") cannot be honoured through the CLR: a multicast
				// delegate's invocation list is append-only from outside, and reordering it would mean detaching and
				// reattaching handlers this script may not own. Ordering across *separate* subscriptions is the CLR's
				// to decide, so -1 is accepted and behaves as 1 rather than failing.
				var reg = new ClrEventRegistration(script, instance, type, ev, fo, scriptTarget, script.EventScheduler, manager);
				return manager.Register(reg) ? new EventSubscription { reg = reg } : DefaultObject;
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
		// Held directly, like WinEventRegistration/MonitorEventRegistration: __Delete unregisters from the
		// finalizer path, where reaching the manager through the Script could otherwise create one to find it empty.
		internal readonly ClrEventManager manager = manager;
		internal readonly object instance = instance;               // null for a static event
		internal readonly Type type = type;
		internal readonly EventInfo eventInfo = eventInfo;
		internal readonly KeysharpFunc callback = callback;
		internal readonly object scriptTarget = scriptTarget;
		internal readonly ScriptEventScheduler ownerScheduler = ownerScheduler;
		internal Delegate handler;
		internal bool active;

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
			if (!active)
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
						   && ReferenceEquals(r.callback, callback), single: true);

		/// <summary>Detaches every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread's scheduler goes away -- does not rely on GC/__Delete).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
			=> scheduler != null && DetachWhere(r => ReferenceEquals(r.ownerScheduler, scheduler), single: false);

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
				Detach(reg);
		}

		/// <summary>
		/// Removes the matching registrations under the lock, then detaches them outside it -- RemoveEventHandler runs
		/// arbitrary CLR code, which must never happen while holding the gate.
		/// </summary>
		private bool DetachWhere(Func<ClrEventRegistration, bool> match, bool single)
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
				Detach(reg);

			return true;
		}

		private static void Detach(ClrEventRegistration reg)
		{
			reg.active = false;
			var del = Interlocked.Exchange(ref reg.handler, null);

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
