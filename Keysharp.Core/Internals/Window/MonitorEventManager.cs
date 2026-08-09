using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Window
{
	/// <summary>
	/// Engine-side state for a single <c>Ks.Monitor.OnChange</c> subscription. A display-change subscription has no
	/// filtering criteria and no per-event state, so it adds nothing to <see cref="EventSubscriptionBase"/>; the
	/// script-facing <c>Ks.MonitorHook</c> object wraps one of these, mirroring how <c>Ks.WinEvent</c> wraps
	/// <see cref="WinEventRegistration"/>.
	/// </summary>
	internal sealed class MonitorEventRegistration(KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler)
		: EventSubscriptionBase(callback, count, ownerScheduler);

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Ks.Monitor.OnChange</c>. It owns the platform
	/// <see cref="IMonitorEventBackend"/>, which is installed only while at least one subscription exists, and turns
	/// each native "something changed" notification into a classified script callback marshalled onto its owning
	/// scheduler (the same path <c>OnMessage</c> and <c>Ks.WinEvent</c> use, so script code always runs on its own
	/// pseudo-thread).
	/// <para>
	/// Classification is done here rather than per platform, by diffing the previous topology snapshot against a
	/// fresh one — see <see cref="IMonitorEventBackend"/> for why the backends report only one bit. A notification
	/// whose diff is empty fires nothing, which is what absorbs the duplicate notifications every platform emits.
	/// </para>
	/// </summary>
	internal sealed class MonitorEventManager : IDisposable
	{
		/// <summary>The set of attached monitors changed — one was plugged in, unplugged, or the session docked.</summary>
		internal const string KindTopology = "topology";
		/// <summary>The same monitors are attached, but something about them changed — resolution, position,
		/// scale or which one is primary.</summary>
		internal const string KindSettings = "settings";

		private readonly Script script;
		private readonly Lock gate = new();
		private readonly List<MonitorEventRegistration> registrations = new();
		private IMonitorEventBackend backend;
		private bool backendInitFailed;
		private bool started;
		private bool disposed;

		// The topology the last dispatch was measured against. Only touched under `gate`. Seeded at the first
		// registration so the first real change is diffed against the layout that existed when the script subscribed,
		// rather than firing spuriously for the initial state.
		private DisplayInfo[] lastTopology = [];

		internal MonitorEventManager(Script script) => this.script = script;

		// ---- registration ------------------------------------------------------------------

		internal void Register(MonitorEventRegistration reg)
		{
			lock (gate)
			{
				if (disposed)
					return;

				// Seed from the layout as it is right now, so the first callback reports a change the script could
				// not already see at subscription time. An enumeration that fails here leaves the baseline empty,
				// which costs one spurious "topology" on the next notification — better than a null baseline, and
				// the alternative (refusing to subscribe) would be worse.
				if (registrations.Count == 0)
					lastTopology = Snapshot() ?? [];

				registrations.Add(reg);
				SyncBackend();
			}
		}

		internal void Unregister(MonitorEventRegistration reg)
		{
			lock (gate)
			{
				RemoveLocked(reg);
				SyncBackend();
			}
		}

		private void RemoveLocked(MonitorEventRegistration reg)
		{
			reg.active = false;
			reg.registration.Clear();
			_ = registrations.Remove(reg);
		}

		/// <summary>Removes every subscription owned by <paramref name="scheduler"/> (deterministic teardown when a
		/// worker thread/scheduler is disposed — does not rely on GC/__Delete).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			var removedAny = false;

			lock (gate)
			{
				for (var i = registrations.Count - 1; i >= 0; i--)
					if (ReferenceEquals(registrations[i].ownerScheduler, scheduler))
					{
						RemoveLocked(registrations[i]);
						removedAny = true;
					}

				if (removedAny)
					SyncBackend();
			}

			return removedAny;
		}

		/// <summary>Starts the native notification on the first subscription and stops it after the last one, so a
		/// script that never calls OnChange pays nothing.</summary>
		private void SyncBackend()
		{
			var wanted = registrations.Count > 0 && !disposed;

			if (wanted == started)
				return;

			if (wanted)
			{
				var b = EnsureBackend();

				if (b == null)
					return;                                   // unsupported environment; nothing to install

				b.Start();
			}
			else
			{
				backend?.Stop();
			}

			started = wanted;
		}

		private IMonitorEventBackend EnsureBackend()
		{
			if (backend != null || backendInitFailed)
				return backend;

			try
			{
				backend = Platform.MonitorEvents.Backend;

				if (backend != null)
					backend.Sink = OnNativeChange;
				else
					backendInitFailed = true;
			}
			catch (Exception ex)
			{
				backendInitFailed = true;
				Ks.OutputDebugLine($"Monitor event backend creation failed: {ex.Message}");
			}

			return backend;
		}

		// ---- native intake (arbitrary thread) ----------------------------------------------

		private void OnNativeChange()
		{
			if (disposed)
				return;

			string kind;
			long count;
			MonitorEventRegistration[] toFire;

			lock (gate)
			{
				if (disposed || registrations.Count == 0)
					return;

				var current = Snapshot();

				// A failed enumeration is NOT "zero monitors": treating it as one would report a bogus topology
				// change to an empty desktop and, worse, leave that as the baseline, so the next notification would
				// report a second bogus change back. Skip it and keep the baseline; the next notification re-reads.
				if (current == null)
					return;

				kind = Classify(lastTopology, current);

				// Every platform emits duplicate/redundant notifications; a diff that nets out to nothing is one of
				// them, and is dropped here rather than in four separate backends.
				if (kind == null)
					return;

				count = current.Length;
				lastTopology = current;
				toFire = registrations.ToArray();
			}

			foreach (var reg in toFire)
				Dispatch(reg, kind, count);
		}

		private void Dispatch(MonitorEventRegistration reg, string kind, long count)
		{
			// A paused hook stays registered and keeps the topology baseline current, but doesn't fire or consume
			// its remaining-count budget.
			if (reg.paused)
				return;

			if (!reg.TryConsumeFire())
				return;

			var scheduler = reg.ownerScheduler ?? script.EventScheduler;

			if (scheduler == null || scheduler.IsDisposed)
				return;

			// Callback shape (locked): (hook, kind). Everything else a handler needs is a plain read of Monitor.All /
			// Monitor.Count at callback time, which is also the only way to get it consistently — the layout can
			// change again between the event and the callback running.
			object[] args = [reg.scriptObject, kind];
			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunOnSchedulerThread(scheduler, reg, args, count));

			if (reg.IsExhausted)
				Unregister(reg);
		}

		private ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler,
			MonitorEventRegistration reg, object[] args, long count)
		{
			// No reg.active re-check here, for the same reason as WinEventManager: TryConsumeFire at enqueue time is
			// the authoritative gate, and re-checking would drop the last allowed callback of a counted subscription.
			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Event);

			if (!thread.Started)
				return thread.Result;

			try
			{
				// A_EventInfo holds the monitor count after the change — the fact a "topology" handler most often
				// branches on (docked vs undocked), and a plain long, so nothing is allocated to carry it.
				thread.ThreadVariables.eventInfo = count;
				_ = reg.callback.Call(args);
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

		// ---- topology diffing --------------------------------------------------------------

		/// <summary>The current topology, or null if the platform could not enumerate it — which mid-reconfiguration
		/// it briefly may. Null is deliberately distinct from an empty array, which is a real (if unusual) state:
		/// a session with no attached monitors.</summary>
		private static DisplayInfo[] Snapshot()
		{
			try
			{
				return Platform.Screen.GetDisplays().ToArray();
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"Monitor topology enumeration failed: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Classifies one topology transition: <see cref="KindTopology"/> when the set of attached monitors changed,
		/// <see cref="KindSettings"/> when the same monitors are attached but something script-visible about them
		/// changed, and <c>null</c> when nothing a script can observe changed — the last of which is how the
		/// duplicate notifications every platform emits get dropped.
		/// </summary>
		internal static string Classify(DisplayInfo[] previous, DisplayInfo[] current)
		{
			if (Unchanged(previous, current))
				return null;

			return SameMonitorSet(previous, current) ? KindSettings : KindTopology;
		}

		/// <summary>
		/// Whether nothing a script can observe about the layout changed. <c>NativeId</c> is deliberately excluded:
		/// it is an opaque per-session handle (a RandR output XID, a wl_output global name) that can be reassigned
		/// without anything script-visible changing, which would fire a spurious "settings" event.
		/// </summary>
		private static bool Unchanged(DisplayInfo[] a, DisplayInfo[] b)
		{
			if (a.Length != b.Length)
				return false;

			for (var i = 0; i < a.Length; i++)
				if (!SameDisplay(a[i], b[i]))
					return false;

			return true;
		}

		private static bool SameDisplay(in DisplayInfo a, in DisplayInfo b)
			=> string.Equals(a.Name, b.Name, StringComparison.Ordinal)
				&& a.Bounds.Equals(b.Bounds)
				&& a.WorkArea.Equals(b.WorkArea)
				&& a.SizeScale == b.SizeScale
				&& a.IsPrimary == b.IsPrimary;

		/// <summary>
		/// Whether the two snapshots describe the same SET of attached monitors, by name — the test that separates a
		/// hotplug/dock ("topology") from a change to monitors that were already attached ("settings"). Enumeration
		/// order is not significant, because the platform may report the same monitors in a different order after a
		/// rearrangement.
		/// </summary>
		private static bool SameMonitorSet(DisplayInfo[] a, DisplayInfo[] b)
		{
			if (a.Length != b.Length)
				return false;

			// Monitor counts are single digits in practice, so the quadratic scan beats allocating a set. `matched`
			// makes it a multiset comparison, so two monitors reporting the same name still need two matches.
			var matched = new bool[b.Length];

			for (var i = 0; i < a.Length; i++)
			{
				var found = false;

				for (var j = 0; j < b.Length; j++)
					if (!matched[j] && string.Equals(a[i].Name, b[j].Name, StringComparison.Ordinal))
					{
						matched[j] = found = true;
						break;
					}

				if (!found)
					return false;
			}

			return true;
		}

		// ---- teardown ----------------------------------------------------------------------

		public void Dispose()
		{
			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;

				foreach (var reg in registrations.ToArray())
					RemoveLocked(reg);

				started = false;
			}

			// Outside the lock: a backend's Stop/Dispose may marshal onto another thread that calls back in.
			try
			{
				backend?.Dispose();
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"Monitor event backend disposal failed: {ex.Message}");
			}

			backend = null;
		}
	}
}
