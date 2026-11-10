namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Clr
		{
			private const BindingFlags EventFlags = BindingFlags.Public | BindingFlags.NonPublic
													| BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase;

			/// <summary>
			/// Intercepts the event-related spellings on a managed node, before ordinary member dispatch sees them:
			/// <c>OnEvent(EventName, Callback, AddRemove := 1)</c> registers or removes a callback. Raw
			/// <c>add_X</c>/<c>remove_X</c> accessors raise so they cannot bypass owner-thread dispatch.
			/// <para>
			/// These spellings take precedence over CLR members with the same names.
			/// </para>
			/// </summary>
			private static bool TryEventCall(object instance, System.Type type, string name, object[] args, out object result)
			{
				result = null;

				// Every ordinary managed method call passes here, so test the synthetic name before reflection.
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

					result = Errors.MethodErrorOccurred($"Register with OnEvent(\"{eventName}\", Callback), or remove with OnEvent(\"{eventName}\", Callback, 0).");
					return true;
				}

				if (!onEvent)
					return false;

				if (args.Length is < 2 or > 3)
					result = Errors.ValueErrorOccurred("OnEvent takes (EventName, Callback, AddRemove := 1).");
				else if (!args[0].CoerceString(out var eventText))
					result = DefaultObject;
				else if (type.GetEvent(eventText, EventFlags) is EventInfo ev)
					result = Subscribe(instance, ev, args[1], args.Length == 3 ? args[2] : null);
				else
					result = Errors.ValueErrorOccurred($"Event '{Errors.Describe(args[0])}' not found on {type.FullName}.");

				return true;
			}

			/// <summary>Reads the parameterized Callbacks property before native CLR property dispatch.</summary>
			private static bool TryEventGet(object instance, System.Type type, string name, object[] args, out object result)
			{
				result = null;
				if (!name.Equals("Callbacks", StringComparison.OrdinalIgnoreCase)) return false;

				if (args.Length != 1)
					result = Errors.ValueErrorOccurred("Callbacks requires one EventName argument.");
				else if (!args[0].CoerceString(out var eventText))
					result = DefaultObject;
				else if (type.GetEvent(eventText, EventFlags) is EventInfo ev)
					result = Script.TheScript.ClrEventManager.Callbacks(instance, ev, Script.TheScript.EventScheduler);
				else
					result = Errors.ValueErrorOccurred($"Event '{Errors.Describe(args[0])}' not found on {type.FullName}.");

				return true;
			}

			private static bool TryEventSet(string name)
			{
				if (!name.Equals("Callbacks", StringComparison.OrdinalIgnoreCase)) return false;
				_ = Errors.PropertyErrorOccurred("Property 'Callbacks' is read-only.");
				return true;
			}

			/// <summary>
			/// Subscribes <paramref name="callback"/> to <paramref name="ev"/>. Shared by Clr.Instance (instance
			/// events, <paramref name="instance"/> non-null) and Clr.Type (static events).
			/// </summary>
			private static object Subscribe(object instance, EventInfo ev, object callback, object addRemove)
			{
				if (!Functions.TryAddRemove(addRemove, out var change) || Functions.ToCallback(callback) is not { } fo)
					return DefaultObject;

				if (change != 0 && !Functions.ValidateFunctor(fo, ev.EventHandlerType?.GetMethod("Invoke")?.GetParameters().Length ?? 0))
					return DefaultObject;

				var script = Script.TheScript;
				script.ClrEventManager.Modify(instance, ev, fo, change, script.EventScheduler);
				return DefaultObject;
			}
		}
	}

	/// <summary>
	/// A CLR callback and the real thread that registered it. Removed registrations remain inactive even if a
	/// callback they queued is still waiting on its owner.
	/// </summary>
	internal sealed class ClrEventRegistration(object callback, ScriptEventScheduler ownerScheduler)
		: CallbackRegistration(callback, ownerScheduler, true, false)
	{
		/// <summary>
		/// Owner-thread calls run inline so the CLR raiser can observe their result or cancellation. Foreign-thread
		/// calls queue on their owner: waiting synchronously could deadlock a script already inside the CLR call.
		/// </summary>
		internal object Dispatch(object[] args, object defaultReturn)
		{
			if (!IsActive || OwnerScheduler is not { IsDisposed: false } scheduler || scheduler.Owner.hasExited)
				return defaultReturn;

			if (scheduler.OwnsCurrentThread)
				return Script.InvokeOrNull(Callback, null, args);

			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunOnSchedulerThread(scheduler, args));
			return defaultReturn;
		}

		private ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler, object[] args)
		{
			// Removal cancels work already queued on the owner.
			if (!IsActive)
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Clr);

			if (!thread.Started)
				return thread.Result;

			try
			{
				_ = Script.InvokeOrNull(Callback, null, args);
			}
			catch (Exception ex) when (CallStack.RememberAndCatch(ex))
			{
				_ = Errors.ReportUncaught(ex);
			}

			return ScriptEventExecutionResult.Executed;
		}
	}

	/// <summary>
	/// Keeps one native delegate per CLR target/event and orders the script callbacks behind it. Retaining that
	/// delegate makes removal deterministic, including for static events that otherwise root the engine.
	/// </summary>
	internal sealed class ClrEventManager : IDisposable
	{
		private readonly Lock gate = new();
		private readonly List<Source> sources = [];
		private bool disposed;

		internal Keysharp.Builtins.Array Callbacks(object instance, EventInfo eventInfo, ScriptEventScheduler scheduler)
		{
			lock (gate)
			{
				var source = sources.Find(candidate => ReferenceEquals(candidate.Instance, instance) && candidate.Event.Equals(eventInfo));
				return source == null ? new() : CallbackRegistration.SnapshotCallbacks(source.Registrations, scheduler);
			}
		}

		private sealed class Source(object instance, EventInfo eventInfo)
		{
			internal readonly object Instance = instance;
			internal readonly EventInfo Event = eventInfo;
			internal readonly List<ClrEventRegistration> Registrations = [];
			internal Delegate Handler;
			private ClrEventRegistration[] snapshot = [];
			private readonly object defaultReturn = DefaultReturn(eventInfo.EventHandlerType?.GetMethod("Invoke")?.ReturnType);

			private static object DefaultReturn(Type returnType) => returnType == null || returnType == typeof(void) || !returnType.IsValueType
				? null : Activator.CreateInstance(returnType);

			internal void Publish() => Volatile.Write(ref snapshot, [.. Registrations]);

			internal object Dispatch(object[] args)
			{
				var result = defaultReturn;

				// CLR multicast events call every handler and return the last result; a nonempty script result does
				// not consume the event as it does for a GUI chain.
				foreach (var reg in Volatile.Read(ref snapshot))
					if (reg.IsActive)
						result = reg.Dispatch(args, defaultReturn);

				return result;
			}
		}

		internal void Modify(object instance, EventInfo eventInfo, object callback, long addRemove, ScriptEventScheduler scheduler)
		{
			if (addRemove == 0)
			{
				Remove(instance, eventInfo, callback, scheduler);
				return;
			}

			Source source = null;
			var attach = false;

			// Publish under the owner's cleanup gate before attaching: teardown either sees the registration or
			// refuses it. Arbitrary CLR event accessors run after both gates have been released.
			if (scheduler.TryRegisterOwnedResource(AddCore) && attach)
				Attach(source, scheduler);

			bool AddCore()
			{
				lock (gate)
				{
					if (disposed)
						return false;

					source = sources.Find(s => ReferenceEquals(s.Instance, instance) && s.Event.Equals(eventInfo));

					if (source == null)
					{
						sources.Add(source = new Source(instance, eventInfo));
						attach = true;
					}
					else if (source.Registrations.Exists(r => ReferenceEquals(r.OwnerScheduler, scheduler) && Functions.SameCallback(r.Callback, callback)))
						return true;

					var reg = new ClrEventRegistration(callback, scheduler);

					if (addRemove < 0)
						source.Registrations.Insert(0, reg);
					else
						source.Registrations.Add(reg);

					source.Publish();
					return true;
				}
			}
		}

		private void Attach(Source source, ScriptEventScheduler installingScheduler)
		{
			Delegate handler = null;

			try
			{
				var handlerType = source.Event.EventHandlerType
					?? throw new InvalidOperationException($"Event '{source.Event.Name}' has no handler type.");
				handler = ClrDelegateMarshaler.FromKeysharpFunc(handlerType, new ClrCallbackShim(source.Dispatch));
				source.Event.AddEventHandler(source.Instance, handler);
			}
			catch (Exception ex)
			{
				Fail(source, installingScheduler, ex);
				Detach(source, handler);
				_ = ManagedInvoke.ThrowMapped(ex, $"{source.Event.DeclaringType?.FullName}.{source.Event.Name} (subscribe)");
				return;
			}

			lock (gate)
			{
				if (!disposed && sources.Contains(source))
				{
					source.Handler = handler;
					return;
				}
			}

			// Removal can win while a custom event accessor attaches. The delegate only becomes ours to detach
			// after AddEventHandler returns, so finish the losing attach here.
			Detach(source, handler);
		}

		private void Fail(Source source, ScriptEventScheduler installingScheduler, Exception error)
		{
			ScriptEventScheduler[] owners;

			lock (gate)
			{
				owners = source.Registrations.Select(reg => reg.OwnerScheduler)
					.Where(owner => owner != null && !ReferenceEquals(owner, installingScheduler)).Distinct().ToArray();
				_ = sources.Remove(source);

				foreach (var reg in source.Registrations)
					reg.Clear();

				source.Registrations.Clear();
				source.Publish();
			}

			// A native add accessor can re-enter registration or need another owner to run, so waiting for its
			// completion could deadlock. Concurrent owners learn an install failure on their own callback threads;
			// same-thread reentrant registrations observe it through the outer registration's exception.
			foreach (var owner in owners)
				_ = owner.EnqueueThreadLaunch(0, false, false,
					() => _ = ManagedInvoke.ThrowMapped(error, $"{source.Event.DeclaringType?.FullName}.{source.Event.Name} (subscribe)"),
					useTryCatch: true, kind: ThreadKind.Clr);
		}

		private void Remove(object instance, EventInfo eventInfo, object callback, ScriptEventScheduler scheduler)
		{
			Source source;
			Delegate handler = null;

			lock (gate)
			{
				source = sources.Find(s => ReferenceEquals(s.Instance, instance) && s.Event.Equals(eventInfo));

				if (source == null)
					return;

				var reg = source.Registrations.Find(r => ReferenceEquals(r.OwnerScheduler, scheduler) && Functions.SameCallback(r.Callback, callback));

				if (reg == null)
					return;

				reg.Clear();
				_ = source.Registrations.Remove(reg);
				source.Publish();

				if (source.Registrations.Count == 0)
				{
					_ = sources.Remove(source);
					handler = source.Handler;
					source.Handler = null;
				}
			}

			Detach(source, handler);
		}

		/// <summary>Detaches every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread's scheduler goes away).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			List<(Source Source, Delegate Handler)> detached = null;
			var removed = false;

			lock (gate)
			{
				for (var i = sources.Count - 1; i >= 0; i--)
				{
					var source = sources[i];

					for (var j = source.Registrations.Count - 1; j >= 0; j--)
					{
						if (!ReferenceEquals(source.Registrations[j].OwnerScheduler, scheduler))
							continue;

						source.Registrations[j].Clear();
						source.Registrations.RemoveAt(j);
						removed = true;
					}

					source.Publish();

					if (source.Registrations.Count == 0)
					{
						sources.RemoveAt(i);
						(detached ??= []).Add((source, source.Handler));
						source.Handler = null;
					}
				}
			}

			if (detached != null)
				foreach (var (source, handler) in detached)
					Detach(source, handler);

			return removed;
		}

		public void Dispose()
		{
			List<(Source Source, Delegate Handler)> detached = [];

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				foreach (var source in sources)
				{
					foreach (var reg in source.Registrations)
						reg.Clear();

					source.Registrations.Clear();
					source.Publish();
					detached.Add((source, source.Handler));
					source.Handler = null;
				}

				sources.Clear();
			}

			foreach (var (source, handler) in detached)
				Detach(source, handler);
		}


		private static void Detach(Source source, Delegate handler)
		{
			if (handler == null)
				return;

			// A target that is already disposed (or a static event on a type being torn down) can throw here. Teardown
			// must not fail because of it: the subscription is going away either way.
			try
			{
				source.Event.RemoveEventHandler(source.Instance, handler);
			}
			catch
			{
			}
		}
	}
}
