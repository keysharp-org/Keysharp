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
	internal sealed class MonitorEventRegistration(KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler,
		MonitorEventManager manager)
		: EventSubscriptionBase(callback, count, ownerScheduler)
	{
		internal readonly MonitorEventManager manager = manager;

		internal override void Unregister() => manager.Unregister(this);
	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Ks.Monitor.OnChange</c>. <see cref="EventManagerBase{TRegistration, TBackend, TPayload}"/>
	/// owns the subscriptions, the backend lifecycle and the dispatch tail; what is left here is the part that is
	/// actually about displays — turning one native "something changed" notification into a classified script event.
	/// <para>
	/// Classification is done here rather than per platform, by diffing the previous topology snapshot against a
	/// fresh one — see <see cref="IMonitorEventBackend"/> for why the backends report only one bit. A notification
	/// whose diff is empty fires nothing, which is what absorbs the duplicate notifications every platform emits.
	/// </para>
	/// </summary>
	internal sealed class MonitorEventManager(Script script)
		: EventManagerBase<MonitorEventRegistration, IMonitorEventBackend, MonitorEventManager.Payload>(script)
	{
		/// <summary>The set of attached monitors changed — one was plugged in, unplugged, or the session docked.</summary>
		internal const string KindTopology = "topology";
		/// <summary>The same monitors are attached, but something about them changed — resolution, position,
		/// scale or which one is primary.</summary>
		internal const string KindSettings = "settings";

		/// <summary>What a classified change carries to the callback: the monitor count after it. A plain long, so
		/// nothing is allocated to reach <c>A_EventInfo</c>.</summary>
		internal readonly record struct Payload(long Count);

		// Serializes topology diffing, so one notification's baseline update cannot interleave with another's.
		// Separate from `gate` because the enumeration it guards is slow (a per-display DPI query on every
		// platform), and holding the registration gate across it would block Register/Stop for the length of a
		// display reconfiguration. Where both are taken, `classifyGate` is taken first.
		private readonly Lock classifyGate = new();
		private bool started;

		// The topology the last dispatch was measured against. Only touched under `classifyGate`. Seeded at the
		// first registration so the first real change is diffed against the layout that existed when the script
		// subscribed, rather than firing spuriously for the initial state.
		private DisplayInfo[] lastTopology = [];

		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;

		// ---- source hooks --------------------------------------------------------------------

		protected override IMonitorEventBackend CreateBackend()
		{
			var created = Platform.MonitorEvents.CreateBackend(script);

			if (created != null)
				created.Sink = OnNativeChange;

			return created;
		}

		/// <summary>Starts the native notification on the first subscription and stops it after the last one, so a
		/// script that never calls OnChange pays nothing.</summary>
		protected override void SyncNativeLocked()
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
				Backend?.Stop();
			}

			started = wanted;
		}

		/// <summary>
		/// Seeds the baseline from the layout as it is right now, so the first callback reports a change the script
		/// could not already see at subscription time. An enumeration that fails here leaves the baseline empty,
		/// which costs one spurious "topology" on the next notification — better than a null baseline, and the
		/// alternative (refusing to subscribe) would be worse.
		/// </summary>
		protected override void PrepareRegistration(MonitorEventRegistration reg, bool isFirst)
		{
			if (!isFirst)
				return;

			lock (classifyGate)
				lastTopology = Snapshot() ?? [];
		}

		/// <summary>A_EventInfo holds the monitor count after the change — the fact a "topology" handler most often
		/// branches on (docked vs undocked).</summary>
		protected override void ApplyThreadState(ThreadVariables tv, MonitorEventRegistration reg, in Payload payload)
			=> tv.eventInfo = payload.Count;

		// ---- native intake (arbitrary thread) ----------------------------------------------

		private void OnNativeChange()
		{
			if (disposed)
				return;

			string kind;
			long count;
			MonitorEventRegistration[] toFire;

			lock (classifyGate)
			{
				if (disposed || !HasSubscriptions)
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
			}

			lock (gate)
			{
				if (disposed)
					return;

				toFire = [.. registrations];
			}

			foreach (var reg in toFire)
				Dispatch(reg, kind, count);
		}

		private void Dispatch(MonitorEventRegistration reg, string kind, long count)
		{
			// A paused hook stays registered and keeps the topology baseline current, but doesn't fire or consume
			// its remaining-count budget.
			if (reg.Suppressed || !reg.TryConsumeFire())
				return;

			var scheduler = DispatchTarget(reg);

			if (scheduler == null)
				return;

			// Callback shape (locked): (hook, kind). Everything else a handler needs is a plain read of Monitor.All /
			// Monitor.Count at callback time, which is also the only way to get it consistently — the layout can
			// change again between the event and the callback running.
			object[] args = [reg.scriptObject, kind];
			var payload = new Payload(count);
			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunCallback(scheduler, reg, args, payload));

			if (reg.IsExhausted)
				Unregister(reg);
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
				Diagnostics.Debug.WriteLine($"Monitor topology enumeration failed: {ex.Message}");
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
	}
}
