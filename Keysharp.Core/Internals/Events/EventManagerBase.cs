using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Events
{
	/// <summary>
	/// Everything a per-<see cref="Script"/> event manager does that has nothing to do with what it listens to:
	/// holding the subscriptions, sweeping them by owning scheduler and at teardown, creating the native backend
	/// on first use and disposing it once, and running one callback in a pseudo-thread on its owner.
	/// <para>
	/// A subclass supplies only what genuinely differs per source: how the backend is created, how the native
	/// source is started and stopped to match the current subscriptions, and what thread state a callback sees.
	/// Everything above that was written twice before <c>Ks.WinEvent</c> and <c>Ks.Monitor.OnChange</c> shared
	/// this, and had already drifted in half a dozen places.
	/// </para>
	/// </summary>
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
		protected bool disposed;

		protected EventManagerBase(Script script) => this.script = script;

		/// <summary>The pseudo-thread kind this source's callbacks run as.</summary>
		protected abstract ThreadKind CallbackThreadKind { get; }

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

		/// <summary>
		/// The script handle of every live subscription, oldest first. A snapshot: stopping a hook while iterating
		/// mutates <see cref="registrations"/>, and a lazy view would have to hold <see cref="gate"/> across script
		/// code. Script-wide rather than per thread, so a loop reaches library hooks and other threads' hooks too.
		/// </summary>
		internal Keysharp.Builtins.Array Hooks()
		{
			TRegistration[] all;

			lock (gate)
				all = [.. registrations];

			// Built outside the gate, as the dispatch snapshots are, so the allocation is not under the lock.
			var result = new Keysharp.Builtins.Array(new List<object>(all.Length));

			foreach (var reg in all)
				_ = result.Push(reg.scriptObject);

			return result;
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

		/// <summary>Captures whatever baseline a new subscription must be measured against — a window enumeration,
		/// a display topology. Called OUTSIDE <see cref="gate"/> and before the subscription goes live, because
		/// every implementation reads the platform and holding the gate across that blocks every other subscribe
		/// and stop.</summary>
		/// <param name="isFirst">True when this is the only subscription, so the source can seed once.</param>
		protected virtual void PrepareRegistration(TRegistration reg, bool isFirst) { }

		/// <summary>Applies the thread state this source's callback expects — <c>A_EventInfo</c> and friends.</summary>
		protected abstract void ApplyThreadState(ThreadVariables tv, TRegistration reg, in TPayload payload);

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
					if (disposed)
						return false;

					registrations.Add(reg);
					OnRegistrationsChangedLocked();
					SyncNativeLocked();
					return true;
				}
			}
		}

		internal void Unregister(TRegistration reg)
		{
			lock (gate)
			{
				RemoveLocked(reg);
				OnRegistrationsChangedLocked();
				SyncNativeLocked();
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
						RemoveLocked(registrations[i]);
						removedAny = true;
					}

				if (removedAny)
				{
					OnRegistrationsChangedLocked();
					SyncNativeLocked();
				}
			}

			return removedAny;
		}

		private void RemoveLocked(TRegistration reg)
		{
			reg.Clear();
			_ = registrations.Remove(reg);
		}

		private static void EndAndClear(TRegistration reg, string reason)
		{
			reg.End(reason);
			reg.Clear();
		}

		/// <summary>
		/// Ends every subscription because the native source cannot be created. Called from a
		/// <see cref="SyncNativeLocked"/> that found no backend, which only happens while something is subscribed —
		/// and a hook left listed there would report itself running while never able to fire.
		/// </summary>
		protected void FailAllLocked()
		{
			foreach (var reg in registrations)
				EndAndClear(reg, EventSubscriptionBase.EndReasonFailed);

			registrations.Clear();
			OnRegistrationsChangedLocked();
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

		// ---- dispatch ------------------------------------------------------------------------

		/// <summary>
		/// Runs one callback in a fresh pseudo-thread on its owner. Every source's dispatch ends here, so the
		/// admission, the error handling and the persistence release are decided once.
		/// <para>
		/// This is the one place a stopped or paused hook is refused. Intake admits and queues regardless of pause,
		/// so the rule a script can derive is simply that a callback checks whether its hook is running at the
		/// moment it would run: <c>Stop()</c> and <c>Pause()</c> take effect when called, and a queued callback that
		/// has not started is discarded rather than deferred. The callback already running always finishes.
		/// </para>
		/// </summary>
		protected ScriptEventExecutionResult RunCallback(ScriptEventScheduler scheduler, TRegistration reg, object[] args, in TPayload payload)
		{
			// No exit check on this path: an event hook is not one of the things AnyPersistent counts, and a worker
			// scheduler is already woken by the persistence release itself, so there is nothing for one to find.
			if (!reg.IsActive || reg.Suppressed)
				return ScriptEventExecutionResult.Dropped;

			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, CallbackThreadKind);

			if (!thread.Started)
				return thread.Result;

			try
			{
				ApplyThreadState(thread.ThreadVariables, reg, payload);
				_ = reg.Callback.Call(args);
			}
			catch (Exception ex)
			{
				_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
			}
			finally
			{
				script.ExitIfNotPersistent();
			}

			return ScriptEventExecutionResult.Executed;
		}

		/// <summary>The scheduler a fired subscription should run on, or null when it can no longer run.</summary>
		protected static ScriptEventScheduler DispatchTarget(TRegistration reg)
		{
			var scheduler = reg.OwnerScheduler;
			return scheduler == null || scheduler.IsDisposed ? null : scheduler;
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
				OnRegistrationsChangedLocked();
				SyncNativeLocked();
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
