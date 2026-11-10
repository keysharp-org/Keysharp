using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// Empty payload declared in Core so ReadyToRun can compile the corresponding event manager instantiations.
	/// </summary>
	internal readonly struct NoPayload;

	/// <summary>Owns one event family's subscriptions, native backend and callback dispatch.</summary>
	/// <typeparam name="TRegistration">The source's subscription type.</typeparam>
	/// <typeparam name="TBackend">The source's platform backend.</typeparam>
	/// <typeparam name="TPayload">What one event carries from the native intake to the callback's thread state.
	/// A struct, so the shared dispatch tail costs no allocation the sources did not already pay.</typeparam>
	internal abstract class EventManagerBase<TRegistration, TBackend, TPayload> : IDisposable
		where TRegistration : CallbackRegistration
		where TBackend : class, IDisposable
		where TPayload : struct
	{
		protected readonly Script script;
		protected readonly Lock gate = new();
		protected readonly List<TRegistration> registrations = [];

		private TBackend backend;
		private bool backendInitFailed;
		private readonly bool keepsScriptRunning;
		private int refreshState; // 0: idle, 1: running, 2: refresh requested.
		protected bool disposed;
		private volatile bool keepingScriptRunning;
		private bool installing;
		private readonly Dictionary<TRegistration, PendingDelivery> pendingDeliveries = [];

		private sealed class PendingDelivery
		{
			internal TRegistration Registration;
			internal readonly Queue<object[]> Items = [];
		}

		protected EventManagerBase(Script script, bool keepsScriptRunning)
		{
			this.script = script;
			this.keepsScriptRunning = keepsScriptRunning;
		}

		/// <summary>The pseudo-thread kind this source's callbacks run as.</summary>
		protected abstract ThreadKind CallbackThreadKind { get; }

		/// <summary>The callback registration API, or null for a session with its own lifecycle.</summary>
		protected virtual string RegistrationName => null;

		internal bool KeepsScriptRunning => keepsScriptRunning;

		internal Keysharp.Builtins.Array Callbacks()
		{
			var owner = script.EventScheduler;
			lock (gate)
				return CallbackRegistration.SnapshotCallbacks(registrations, owner);
		}

		/// <summary>Whether a hook of this source keeps the script running now: the rule above, applied to the running
		/// hooks. Read without the gate by <see cref="Script.AnyPersistent"/>.</summary>
		internal bool IsKeepingScriptRunning => keepingScriptRunning;

		/// <summary>The backend, once created. Null before the first subscription and after teardown.</summary>
		protected TBackend Backend => backend;

		/// <summary>Whether anything is currently subscribed.</summary>
		internal bool HasSubscriptions
		{
			get
			{
				lock (gate)
					return registrations.Count > 0;
			}
		}

		// ---- source hooks --------------------------------------------------------------------

		/// <summary>Creates the platform backend and wires its sink. Returns null where the environment has none.</summary>
		protected abstract TBackend CreateBackend();

		/// <summary>Installs or uninstalls exactly the native sources the current <see cref="registrations"/>
		/// need. Called under <see cref="gate"/> after every registration change, including teardown.</summary>
		protected abstract void SyncNativeLocked();

		/// <summary>Rebuilds whatever index the source keeps over <see cref="registrations"/>. Called under
		/// <see cref="gate"/> after every registration change.</summary>
		protected virtual void OnRegistrationsChangedLocked() { }

		// Every change to the list passes here, so the published state cannot drift from it.
		private void RegistrationsChangedLocked()
		{
			keepingScriptRunning = keepsScriptRunning && registrations.Count > 0;

			OnRegistrationsChangedLocked();
		}

		/// <summary>Captures a new subscription's baseline outside <see cref="gate"/>.</summary>
		/// <param name="isFirst">True when this is the only subscription, so the source can seed once.</param>
		protected virtual void PrepareRegistration(TRegistration reg, bool isFirst) { }

		/// <summary>Applies the thread state this source's callback expects beyond its arguments, such as
		/// <c>A_EventInfo</c>. Most sources pass everything as arguments.</summary>
		protected virtual void ApplyThreadState(ThreadVariables tv, TRegistration reg, in TPayload payload) { }

		// Backend callbacks queue refreshes to avoid re-entering the device API.
		protected void QueueRefresh()
		{
			if (!disposed && Interlocked.Exchange(ref refreshState, 2) == 0)
				_ = ThreadPool.UnsafeQueueUserWorkItem(static manager => manager.DrainRefreshes(), this, false);
		}

		protected virtual void ProcessRefresh() { }

		private void DrainRefreshes()
		{
			do
			{
				Interlocked.Exchange(ref refreshState, 1);
				try
				{
					if (!disposed) ProcessRefresh();
				}
				catch (Exception error)
				{
					Diagnostics.Debug.WriteLine($"Event refresh failed: {error.Message}");
				}
			}
			while (Interlocked.CompareExchange(ref refreshState, 0, 1) != 1);
		}

		// ---- registration --------------------------------------------------------------------

		protected virtual bool IsRunning(TRegistration reg) => reg.IsActive;
		protected virtual void EndRegistration(TRegistration reg, string reason) => reg.Clear();

		internal void Register(TRegistration reg) => _ = Register(reg, null, null);

		/// <summary>Changes the calling thread's callback registration.</summary>
		internal bool Modify(TRegistration reg, long addRemove, Func<TRegistration, TRegistration, bool> sameOptions = null)
		{
			if (addRemove != 0)
				return Register(reg, addRemove, sameOptions);

			lock (gate)
			{
				var index = FindRegistration(reg);
				reg.Clear();

				if (index >= 0)
					Unregister(registrations[index]);
			}

			return true;
		}

		private int FindRegistration(TRegistration reg) => registrations.FindIndex(existing =>
			ReferenceEquals(existing.OwnerScheduler, reg.OwnerScheduler) && Functions.SameCallback(existing.Callback, reg.Callback));

		private bool Register(TRegistration reg, long? addRemove, Func<TRegistration, TRegistration, bool> sameOptions)
		{
			bool first;

			lock (gate)
			{
				if (disposed)
				{
					EndRegistration(reg, EventSubscriptionBase.EndReasonExit);
					return false;
				}

				backendInitFailed = false;
				first = registrations.Count == 0;
			}

			try
			{
				PrepareRegistration(reg, first);
			}
			catch
			{
				EndRegistration(reg, EventSubscriptionBase.EndReasonFailed);
				if (addRemove.HasValue) return false;
				throw;
			}

			var scheduler = reg.OwnerScheduler;
			var installed = true;
			if (scheduler != null ? !scheduler.TryRegisterOwnedResource(AddCore) : !AddCore())
			{
				EndRegistration(reg, EventSubscriptionBase.EndReasonExit);
				return false;
			}
			return installed;

			bool AddCore()
			{
				lock (gate)
				{
					if (disposed || !IsRunning(reg)) return false;
					var index = addRemove.HasValue ? FindRegistration(reg) : -1;

					if (index >= 0)
					{
						var existing = registrations[index];
						if (sameOptions == null || sameOptions(existing, reg))
						{
							reg.Clear();
							return true;
						}

						EndRegistration(existing, EventSubscriptionBase.EndReasonStopped);
						registrations[index] = reg;
						if (pendingDeliveries.Remove(existing, out var pending))
						{
							// Keep an executing callback's serial queue while discarding its old-filter work.
							pending.Items.Clear();
							pending.Registration = reg;
							pendingDeliveries.Add(reg, pending);
						}
					}
					else if (addRemove < 0)
						registrations.Insert(0, reg);
					else
						registrations.Add(reg);

					RegistrationsChangedLocked();
					installing = true;
					try
					{
						if (!TrySyncNativeLocked("install"))
						{
							_ = RemoveLocked(reg, EventSubscriptionBase.EndReasonFailed);
							RegistrationsChangedLocked();
							_ = TrySyncNativeLocked("recovery");
						}
						installed = IsRunning(reg);
					}
					finally { installing = false; }
					return true;
				}
			}
		}

		internal void Unregister(TRegistration reg)
		{
			lock (gate)
			{
				if (!RemoveLocked(reg, EventSubscriptionBase.EndReasonStopped)) return;
				RegistrationsChangedLocked();
				_ = TrySyncNativeLocked("uninstall");
			}
		}

		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null) return false;
			var removed = false;
			lock (gate)
			{
				for (var i = registrations.Count - 1; i >= 0; i--)
					if (ReferenceEquals(registrations[i].OwnerScheduler, scheduler))
						removed |= RemoveLocked(registrations[i], EventSubscriptionBase.EndReasonExit);

				if (removed)
				{
					RegistrationsChangedLocked();
					_ = TrySyncNativeLocked("owner cleanup");
				}
			}
			return removed;
		}

		private bool RemoveLocked(TRegistration reg, string reason)
		{
			EndRegistration(reg, reason);
			_ = pendingDeliveries.Remove(reg);
			return registrations.Remove(reg);
		}

		protected void FailAllLocked()
		{
			var owners = !installing && RegistrationName != null
				? registrations.Select(reg => reg.OwnerScheduler).Where(owner => owner != null).Distinct().ToArray() : [];
			foreach (var reg in registrations)
				EndRegistration(reg, EventSubscriptionBase.EndReasonFailed);
			registrations.Clear();
			pendingDeliveries.Clear();
			RegistrationsChangedLocked();
			foreach (var owner in owners)
				_ = owner.EnqueueThreadLaunch(0, false, false,
					() => _ = Errors.OSErrorOccurredWithMessage($"{RegistrationName} notifications are unavailable."),
					useTryCatch: true, kind: CallbackThreadKind);
		}

		// ---- backend -------------------------------------------------------------------------

		protected TBackend EnsureBackend()
		{
			if (backend != null || backendInitFailed)
				return backend;

			try
			{
				backend = CreateBackend();
			}
			catch (Exception ex)
			{
				backendInitFailed = true;
				Diagnostics.Debug.WriteLine($"{GetType().Name} backend creation failed: {ex.Message}");
			}

			return backend;
		}

		private bool TrySyncNativeLocked(string operation)
		{
			try
			{
				SyncNativeLocked();
				return true;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"{GetType().Name} native {operation} failed: {ex.Message}");
				return false;
			}
		}

		// ---- dispatch ------------------------------------------------------------------------

		/// <summary>Runs a callback in a fresh pseudo-thread if its hook still runs.</summary>
		protected ScriptEventExecutionResult RunCallback(ScriptEventScheduler scheduler, TRegistration reg, object callback,
			object[] args, in TPayload payload)
		{
			// No exit check on this path: a drop releases nothing, so there is nothing to re-check. A release made
			// by the callback is checked when its thread ends.
			if (!IsRunning(reg))
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, CallbackThreadKind);

			if (!thread.Started)
				return thread.Result;

			try
			{
				ApplyThreadState(thread.ThreadVariables, reg, payload);
				_ = Script.InvokeOrNull(callback, null, args);
			}
			catch (Exception ex) when (CallStack.RememberAndCatch(ex))
			{
				_ = Errors.ReportUncaught(ex);
			}

			return ScriptEventExecutionResult.Executed;
		}

		/// <summary>Queues a session callback carrying its event-specific payload.</summary>
		protected void Fire(TRegistration reg, object callback, object[] args, TPayload payload = default)
		{
			if (IsRunning(reg) && reg.OwnerScheduler is { IsDisposed: false } scheduler)
				_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunCallback(scheduler, reg, callback, args, payload));
		}

		/// <summary>Queues notifications serially, including when a callback pumps its owner's scheduler.</summary>
		protected void FireNotification(TRegistration reg, object[] args)
		{
			if (!IsRunning(reg) || reg.OwnerScheduler is not { IsDisposed: false } scheduler) return;
			lock (gate)
			{
				if (disposed || !IsRunning(reg)) return;
				if (pendingDeliveries.TryGetValue(reg, out var pending))
				{
					pending.Items.Enqueue(args);
					return;
				}
				pending = new() { Registration = reg };
				pending.Items.Enqueue(args);
				pendingDeliveries.Add(reg, pending);
				_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => DeliverNext(scheduler, pending));
			}
		}

		private ScriptEventExecutionResult DeliverNext(ScriptEventScheduler scheduler, PendingDelivery pending)
		{
			TRegistration reg;
			object[] args;
			lock (gate)
			{
				reg = pending.Registration;
				if (!IsRunning(reg) || !pendingDeliveries.TryGetValue(reg, out var current) || current != pending)
					return ScriptEventExecutionResult.Dropped;
				if (pending.Items.Count == 0)
				{
					_ = pendingDeliveries.Remove(reg);
					return ScriptEventExecutionResult.Dropped;
				}
				args = pending.Items.Peek();
			}

			var result = RunCallback(scheduler, reg, reg.Callback, args, default);
			if (result is ScriptEventExecutionResult.GlobalBlocked or ScriptEventExecutionResult.LocalBlocked) return result;
			lock (gate)
			{
				var current = pending.Registration;
				if (pendingDeliveries.TryGetValue(current, out var active) && active == pending)
				{
					if (current == reg) _ = pending.Items.Dequeue();
					if (pending.Items.Count == 0)
						_ = pendingDeliveries.Remove(current);
					else
						_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => DeliverNext(scheduler, pending));
				}
			}
			return result;
		}

		// ---- teardown ------------------------------------------------------------------------

		public void Dispose()
		{
			TBackend toDispose;
			TRegistration[] all;

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				all = [.. registrations];
				registrations.Clear();
				pendingDeliveries.Clear();
				RegistrationsChangedLocked();
				_ = TrySyncNativeLocked("teardown");
				toDispose = backend;
				backend = null;
			}

			foreach (var reg in all)
				EndRegistration(reg, EventSubscriptionBase.EndReasonExit);

			// Outside the lock: a backend's Stop/Dispose may marshal onto another thread that calls back in.
			try
			{
				toDispose?.Dispose();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"{GetType().Name} backend dispose failed: {ex.Message}");
			}
		}
	}
}
