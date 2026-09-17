using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>Owns one event family's subscriptions, native backend and callback dispatch.</summary>
	/// <typeparam name="TRegistration">The source's subscription type.</typeparam>
	/// <typeparam name="TBackend">The source's platform backend.</typeparam>
	/// <typeparam name="TPayload">What one event carries from the native intake to the callback's thread state.
	/// A struct, so the shared dispatch tail costs no allocation the sources did not already pay.</typeparam>
	internal abstract class EventManagerBase<TRegistration, TBackend, TPayload> : IDisposable
		where TRegistration : EventSubscriptionBase
		where TBackend : class, IDisposable
		where TPayload : struct
	{
		protected readonly Script script;
		protected readonly Lock gate = new();
		protected readonly List<TRegistration> registrations = [];

		private TBackend backend;
		private bool backendInitFailed;
		private readonly bool keepsScriptRunning;
		protected bool disposed;
		private volatile bool keepingScriptRunning;

		protected EventManagerBase(Script script, bool keepsScriptRunning)
		{
			this.script = script;
			this.keepsScriptRunning = keepsScriptRunning;
		}

		/// <summary>The pseudo-thread kind this source's callbacks run as.</summary>
		protected abstract ThreadKind CallbackThreadKind { get; }

		internal bool KeepsScriptRunning => keepsScriptRunning;

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

		/// <summary>A snapshot of every running hook in start order.</summary>
		internal Keysharp.Builtins.Array Hooks()
		{
			EventSubscriptionBase[] all;

			lock (gate)
				all = [.. registrations];

			return EventSubscriptionBase.Handles(all);
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

		// ---- registration --------------------------------------------------------------------

		internal void Register(TRegistration reg)
		{
			bool first;

			lock (gate)
			{
				if (disposed)
				{
					// Clear, not just return: the registration was born holding a persistence root, and a handle
					// that is never listed here is never swept to release it.
					EndAndClear(reg, EventSubscriptionBase.EndReasonExit);
					return;
				}

				// A Start retries a backend whose creation failed, so a failure that was transient can recover.
				backendInitFailed = false;
				first = registrations.Count == 0;
			}

			PrepareRegistration(reg, first);
			var scheduler = reg.OwnerScheduler;

			// Adding under the owning scheduler's cleanup gate makes "registered" and "will be swept" one step: a
			// scheduler that has already torn down refuses, leaving the subscription inactive rather than stranded
			// in a manager nothing will ever sweep. This is the same rule CallbackRegistry follows, and the same
			// lock order teardown takes (cleanup gate, then this manager's), so the two cannot invert.
			if (scheduler != null ? !scheduler.TryRegisterOwnedResource(AddCore) : !AddCore())
				EndAndClear(reg, EventSubscriptionBase.EndReasonExit);

			bool AddCore()
			{
				lock (gate)
				{
					// A run stopped before it was listed stays out, or it would sit here counted as alive.
					if (disposed || !reg.IsRunning)
						return false;

					registrations.Add(reg);
					RegistrationsChangedLocked();

					if (!TrySyncNativeLocked("install"))
					{
						// A source that throws while installing ends the run Failed, as a missing one does, rather than
						// leaving it listed and keeping the script alive while it can never fire. The source is then
						// synced to the runs that remain, which undoes whatever part of the install took.
						_ = reg.End(EventSubscriptionBase.EndReasonFailed);
						_ = RemoveLocked(reg);
						RegistrationsChangedLocked();
						_ = TrySyncNativeLocked("recovery");
					}

					return true;
				}
			}
		}

		internal void Unregister(TRegistration reg)
		{
			lock (gate)
			{
				// Not listed (stopped before it was, or already swept): cleared, with nothing else to redo.
				if (!RemoveLocked(reg))
					return;

				RegistrationsChangedLocked();
				_ = TrySyncNativeLocked("uninstall");
			}
		}

		/// <summary>Removes every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread's scheduler is disposed — does not rely on GC/__Delete).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			var removedAny = false;

			lock (gate)
			{
				for (var i = registrations.Count - 1; i >= 0; i--)
					if (ReferenceEquals(registrations[i].OwnerScheduler, scheduler))
					{
						registrations[i].End(EventSubscriptionBase.EndReasonExit);
						_ = RemoveLocked(registrations[i]);
						removedAny = true;
					}

				if (removedAny)
				{
					RegistrationsChangedLocked();
					_ = TrySyncNativeLocked("owner cleanup");
				}
			}

			return removedAny;
		}

		private bool RemoveLocked(TRegistration reg)
		{
			reg.Clear();
			return registrations.Remove(reg);
		}

		private static void EndAndClear(TRegistration reg, string reason)
		{
			reg.End(reason);
			reg.Clear();
		}

		/// <summary>Ends every subscription after the native source cannot be created.</summary>
		protected void FailAllLocked()
		{
			foreach (var reg in registrations)
				EndAndClear(reg, EventSubscriptionBase.EndReasonFailed);

			registrations.Clear();
			RegistrationsChangedLocked();
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
			if (!reg.IsRunning)
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, CallbackThreadKind);

			if (!thread.Started)
				return thread.Result;

			try
			{
				ApplyThreadState(thread.ThreadVariables, reg, payload);
				_ = Script.InvokeOrNull(callback, null, args);
			}
			catch (Exception ex)
			{
				_ = Errors.ReportUncaught(ex);
			}

			return ScriptEventExecutionResult.Executed;
		}

		/// <summary>Queues one callback to the thread that owns <paramref name="reg"/>, where <see cref="RunCallback"/>
		/// decides whether it still runs. A run that has ended, or whose thread is gone, queues nothing.</summary>
		protected void Fire(TRegistration reg, object callback, object[] args, TPayload payload = default)
		{
			if (!reg.IsRunning || reg.OwnerScheduler is not { IsDisposed: false } scheduler)
				return;

			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunCallback(scheduler, reg, callback, args, payload));
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
				RegistrationsChangedLocked();
				_ = TrySyncNativeLocked("teardown");
				toDispose = backend;
				backend = null;
			}

			foreach (var reg in all)
				EndAndClear(reg, EventSubscriptionBase.EndReasonExit);

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
