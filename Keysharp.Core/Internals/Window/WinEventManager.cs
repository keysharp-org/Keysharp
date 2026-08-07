using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Window
{
	/// <summary>
	/// Engine-side state for a single <c>Ks.WinEvent</c> subscription: the event type, the parsed window-matching
	/// criteria, the script callback, a remaining-fire counter, and a persistence registration. The script-facing
	/// <c>Ks.WinEvent</c> object wraps one of these, mirroring how <c>InputHook</c> wraps <c>InputType</c>.
	/// </summary>
	internal sealed class WinEventRegistration : EventSubscriptionBase
	{
		internal readonly WindowEventType type;
		internal readonly SearchCriteria criteria;            // null => match any window
		internal readonly WindowSearchOptions inheritedOptions;
		internal readonly bool detectHidden;                  // effective DetectHiddenWindows for this subscription
		internal nint activeReported;                         // Active subs: hwnd last reported active, so a
		                                                      // title-change re-fire of the same window doesn't duplicate
		internal Rectangle? lastCaretRect;                    // CaretMove subs: last caret rectangle seen, so a native
		                                                      // source repeating an unchanged position doesn't "move"

		// Membership-tracking subscriptions (Exist, NotExist) keep the set of top-level windows that currently
		// satisfy this subscription. Mirroring the AHK WinEvent library's MatchingWinList, the set is seeded at registration
		// and kept current as windows enter/leave (Create/Show/Restore/TitleChange add; Close/Minimize/TitleChange
		// remove). Exist fires when a window enters the set, NotExist when one leaves (both respecting
		// DetectHiddenWindows), so the set lets each fire on genuine transitions rather than the raw lifecycle event.
		internal readonly HashSet<nint> matchingWindows;
		internal readonly Lock matchGate;

		/// <summary>True for an Exist subscription (fires when a matching window appears).</summary>
		internal bool IsExist => type == WindowEventType.Exist;
		/// <summary>True for a NotExist subscription (fires when a matching window disappears).</summary>
		internal bool IsNotExist => type == WindowEventType.NotExist;
		/// <summary>True for any subscription that maintains a matching-window set (Exist/NotExist).</summary>
		internal bool TracksMembership => matchingWindows != null;

		internal WinEventRegistration(WindowEventType type, SearchCriteria criteria, KeysharpFunc callback, long count, ScriptEventScheduler ownerScheduler)
			: base(callback, count, ownerScheduler)
		{
			this.type = type;
			this.criteria = criteria;

			if (type is WindowEventType.Exist or WindowEventType.NotExist)
			{
				matchingWindows = new HashSet<nint>();
				matchGate = new Lock();
			}

			// Snapshot the window-search context from the registering thread, mirroring the AHK WinEvent library
			// (which captures A_DetectHiddenWindows/Text and the title-match mode at registration). Show forces
			// hidden detection on, because a freshly shown window is often still hidden for a short time. All other
			// event types respect the thread's DetectHiddenWindows setting.
			var forceHidden = type is WindowEventType.Show;
			detectHidden = forceHidden || ThreadAccessors.A_DetectHiddenWindows;
			inheritedOptions = new WindowSearchOptions
			{
				DetectHiddenWindows = detectHidden,
				DetectHiddenText = forceHidden || ThreadAccessors.A_DetectHiddenText,
				TitleMatchMode = ThreadAccessors.A_TitleMatchMode,
				TitleMatchModeSpeed = ThreadAccessors.A_TitleMatchModeSpeed
			};
		}

	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Ks.WinEvent</c>. It owns the platform
	/// <see cref="IWindowEventBackend"/> directly, installs/uninstalls native hooks for exactly the categories its
	/// subscriptions need, and for each incoming <see cref="WindowEventRaw"/> performs window-criteria matching,
	/// the remaining-fire count, and marshalling of the script callback onto its owning scheduler (the same path
	/// <c>OnMessage</c> uses, so script code always runs on its own pseudo-thread). Move events are delivered
	/// as-is (no coalescing).
	/// </summary>
	internal sealed class WinEventManager : IDisposable
	{
		private readonly Script script;
		private readonly Lock gate = new();
		private readonly Dictionary<WindowEventType, List<WinEventRegistration>> buckets = new();
		private IWindowEventBackend backend;
		private bool backendInitFailed;
		private WindowEventMask installedMask = WindowEventMask.None;
		private volatile bool globalPaused;
		private bool disposed;

		internal WinEventManager(Script script) => this.script = script;

		// ---- global pause ------------------------------------------------------------------

		/// <summary>True while all hooks are globally paused.</summary>
		internal bool GlobalPaused => globalPaused;

		/// <summary>Pauses (1), unpauses (0) or toggles (-1) all hooks; returns the resulting state.</summary>
		internal bool SetGlobalPause(long newState)
		{
			globalPaused = newState == -1 ? !globalPaused : newState != 0;
			return globalPaused;
		}

		// ---- registration ------------------------------------------------------------------

		internal void Register(WinEventRegistration reg)
		{
			lock (gate)
			{
				if (disposed)
					return;

				if (!buckets.TryGetValue(reg.type, out var list))
					buckets[reg.type] = list = new List<WinEventRegistration>();

				list.Add(reg);

				// Seed the matching-window set so a Close fires for windows that existed before the subscription,
				// and so Exist/NotExist only fire on genuine transitions (not for windows already matching at
				// registration). Mirrors the AHK WinEvent library seeding its MatchingWinList up front.
				if (reg.TracksMembership)
					SeedMatches(reg);

				SyncBackendMask();
			}
		}

		internal void Unregister(WinEventRegistration reg)
		{
			lock (gate)
			{
				RemoveLocked(reg);
				SyncBackendMask();
			}
		}

		private void RemoveLocked(WinEventRegistration reg)
		{
			reg.active = false;
			reg.registration.Clear();

			if (buckets.TryGetValue(reg.type, out var list))
			{
				_ = list.Remove(reg);

				if (list.Count == 0)
					_ = buckets.Remove(reg.type);
			}
		}

		/// <summary>Removes every subscription owned by <paramref name="scheduler"/> (deterministic teardown
		/// when a worker thread/scheduler is disposed — does not rely on GC/__Delete).</summary>
		internal bool RemoveOwned(ScriptEventScheduler scheduler)
		{
			if (scheduler == null)
				return false;

			var removedAny = false;

			lock (gate)
			{
				foreach (var reg in buckets.Values.SelectMany(l => l).Where(r => ReferenceEquals(r.ownerScheduler, scheduler)).ToArray())
				{
					RemoveLocked(reg);
					removedAny = true;
				}

				if (removedAny)
					SyncBackendMask();
			}

			return removedAny;
		}

		// ---- native hook management ---------------------------------------------------------

		/// <summary>Recomputes which event categories are needed and installs/uninstalls native hooks on the
		/// backend to match. Must be called under <see cref="gate"/>.</summary>
		private void SyncBackendMask()
		{
			var desired = WindowEventMask.None;

			foreach (var type in buckets.Keys)
				desired |= type.ToMask();

			// Exist/NotExist are membership transitions derived from the lifecycle events, so they need every event
			// that can move a window into or out of the matching set: appear (Create/Show/Restore), disappear
			// (Close/Minimize) and re-match (TitleChange).
			if ((buckets.TryGetValue(WindowEventType.Exist, out var existList) && existList.Count > 0)
				|| (buckets.TryGetValue(WindowEventType.NotExist, out var notExistList) && notExistList.Count > 0))
			{
				desired |= WindowEventMask.Create | WindowEventMask.Show | WindowEventMask.Restore
						   | WindowEventMask.Close | WindowEventMask.Minimize | WindowEventMask.TitleChange;
			}

			// Active also fires on the active window's title change, so observe TitleChange when any Active sub exists.
			if (buckets.TryGetValue(WindowEventType.Active, out var activeList) && activeList.Count > 0)
				desired |= WindowEventMask.TitleChange;

			if (desired == installedMask)
				return;

			if (desired != WindowEventMask.None)
			{
				var b = EnsureBackend();

				if (b == null)
					return;                                   // unsupported environment; nothing to install

				var toRemove = installedMask & ~desired;
				var toAdd = desired & ~installedMask;

				if (toRemove != WindowEventMask.None)
					b.Stop(toRemove);

				if (toAdd != WindowEventMask.None)
					b.Start(toAdd);
			}
			else
			{
				backend?.Stop(installedMask);
			}

			installedMask = desired;
		}

		private IWindowEventBackend EnsureBackend()
		{
			if (backend != null || backendInitFailed)
				return backend;

			try
			{
				backend = Platform.Events.Backend;

				if (backend != null)
					backend.Sink = OnNativeEvent;
				else
					backendInitFailed = true;
			}
			catch (Exception ex)
			{
				backendInitFailed = true;
				Ks.OutputDebugLine($"Window event backend creation failed: {ex.Message}");
			}

			return backend;
		}

		// ---- native event intake (arbitrary thread, from the hub) ---------------------------

		private void OnNativeEvent(WindowEventRaw raw)
		{
			if (disposed)
				return;

			// Drive Exist/NotExist membership transitions. Any lifecycle event that can move a window into or out of
			// the matching set is a trigger: appear (Create/Show/Restore), disappear (Close/Minimize) or re-match
			// (TitleChange). A confirmed destruction forces the window out regardless of DetectHiddenWindows (and
			// ahead of any window-server list lag); a hide arrives as a Close too but only leaves the set when the
			// criteria stop matching under the subscription's DetectHiddenWindows.
			if (raw.Type is WindowEventType.Create or WindowEventType.Show or WindowEventType.Restore
				or WindowEventType.TitleChange or WindowEventType.Close or WindowEventType.Minimize)
				UpdateMembership(raw.Hwnd, raw.TimeMs, raw.Type == WindowEventType.Close && raw.DestroyConfirmed);

			// Like the reference, Active also re-fires when the active window's title changes (so criteria that
			// only become true after the title is set are still caught).
			if (raw.Type == WindowEventType.TitleChange)
				DispatchActiveOnTitleChange(raw.Hwnd, raw.TimeMs);

			WinEventRegistration[] snapshot;

			lock (gate)
			{
				if (!buckets.TryGetValue(raw.Type, out var list) || list.Count == 0)
					return;

				snapshot = list.ToArray();
			}

			if (raw.Type == WindowEventType.Active)
			{
				// Record which window each Active subscription reported, and reset it on every activation so
				// re-activating the same window still fires while a mere title-change of the already-reported
				// active window (handled in DispatchActiveOnTitleChange) does not duplicate.
				foreach (var reg in snapshot)
				{
					if (!reg.active)
						continue;

					var matched = Matches(reg, raw.Hwnd);
					reg.activeReported = matched ? raw.Hwnd : 0;

					if (matched)
						FireOnce(reg, raw.Hwnd, raw.TimeMs);
				}

				return;
			}

			// Resolve Move geometry at event time (not when the queued callback later reads A_EventInfo — by then the
			// window may have drifted during a drag/backlog), but only once a registration actually matches: a full
			// WindowQuery per unrelated window drag on the pump thread is wasteful, so defer the query to the first
			// matching reg and memoize it for the rest. Nothing is queried when nothing matches. The Wayland backends
			// carry the bounds on the event (raw.Bounds); X11/Windows do the cheap local query here. Only the
			// script-object construction is deferred further (BuildRectEventInfo via SetEventInfo).
			var isMove = raw.Type == WindowEventType.Move;
			var isCaret = raw.Type == WindowEventType.CaretMove;

			// A caret has no queryable identity after the fact — it belongs to whichever control had focus at event
			// time — so its rectangle is captured by the backend and rides on the event. Nothing to report without it.
			if (isCaret && raw.Bounds == null)
				return;

			Rectangle? eventBounds = null;
			var boundsResolved = false;

			foreach (var reg in snapshot)
			{
				if (!reg.active || !Matches(reg, raw.Hwnd))
					continue;

				if (isCaret)
				{
					// "CaretMove" promises an actual move, but native sources re-report an unchanged position (a
					// re-notified selection change, a caret repaint, a focus round trip), so an identical rectangle is
					// suppressed. Tracked per subscription rather than globally so a hook registered mid-stream still
					// receives its first event.
					if (reg.lastCaretRect == raw.Bounds)
						continue;

					reg.lastCaretRect = raw.Bounds;
					eventBounds = raw.Bounds;
				}
				else if (isMove && !boundsResolved)
				{
					// Queried at event time (still synchronously inside this native intake), just only now that we know
					// a subscription cares — preserving the event-time-capture rationale without paying it speculatively.
					eventBounds = raw.Bounds ?? QueryBounds(raw.Hwnd);
					boundsResolved = true;
				}

				FireOnce(reg, raw.Hwnd, raw.TimeMs, eventBounds);
			}
		}

		/// <summary>Fires Active subscriptions for the active window when its title changes.</summary>
		private void DispatchActiveOnTitleChange(nint hwnd, long timeMs)
		{
			if (hwnd == 0 || hwnd != WindowQuery.GetForegroundWindowHandle())
				return;

			WinEventRegistration[] activeSubs;

			lock (gate)
			{
				if (!buckets.TryGetValue(WindowEventType.Active, out var list) || list.Count == 0)
					return;

				activeSubs = list.ToArray();
			}

			foreach (var reg in activeSubs)
			{
				// Only criteria subscriptions need the title-change re-fire — it exists to catch a window that
				// became active before its title (hence its match) was set. A match-any Active subscription
				// already fired on the activation itself, so re-firing on its title changes is pure duplication.
				// activeReported then dedupes the case where the activation already matched and fired.
				if (reg.active && reg.criteria != null && reg.activeReported != hwnd && Matches(reg, hwnd))
				{
					reg.activeReported = hwnd;
					FireOnce(reg, hwnd, timeMs);
				}
			}
		}

		private static bool Matches(WinEventRegistration reg, nint hwnd)
		{
			if (hwnd == 0)
				return false;

			if (reg.criteria == null)
			{
				// Match-any: respect the registration-time DetectHiddenWindows setting so the callback isn't
				// flooded with transient/hidden windows when DHW is off. A single visibility read — no item needed.
				if (reg.detectHidden)
					return true;

				return Platform.Window.GetVisible(hwnd);
			}

			// Criteria matching reads several properties, so build the one item and match against it.
			return WindowQuery.CreateWindow(hwnd) is WindowInfoBase win && win.Equals(reg.criteria, reg.inheritedOptions);
		}

		/// <summary>Whether <paramref name="hwnd"/> currently satisfies a membership subscription (Exist/NotExist):
		/// the window must actually exist and match the criteria, honoring DetectHiddenWindows. Unlike
		/// <see cref="Matches"/> (used for fire-and-forget events, where the hwnd is known live), this verifies
		/// existence — a match-any subscription must not treat an already-destroyed handle as still matching.</summary>
		private static bool CurrentlyMatches(WinEventRegistration reg, nint hwnd)
		{
			if (hwnd == 0)
				return false;

			if (reg.criteria == null)
			{
				if (!WindowQuery.IsWindow(hwnd))
					return false;                             // gone — no longer a member

				if (reg.detectHidden)
					return true;

				return Platform.Window.GetVisible(hwnd);     // single visibility read — no item needed
			}

			// Criteria matching reads the window's properties, which a destroyed window no longer has, so a genuine
			// destruction naturally fails the match (the criteria path also applies the captured DetectHiddenWindows).
			var win = WindowQuery.CreateWindow(hwnd);
			return win != null && win.IsSpecified && win.Equals(reg.criteria, reg.inheritedOptions);
		}

		/// <summary>Re-evaluates a window's membership against every Exist/NotExist subscription and fires the
		/// transitions: Exist when a window enters a subscription's matching set, NotExist when one leaves it.
		/// <paramref name="windowGone"/> forces the window out (a confirmed destruction) regardless of
		/// DetectHiddenWindows and ahead of any window-server list lag.</summary>
		private void UpdateMembership(nint hwnd, long timeMs, bool windowGone)
		{
			if (hwnd == 0)
				return;

			WinEventRegistration[] subs;

			lock (gate)
			{
				var hasExist = buckets.TryGetValue(WindowEventType.Exist, out var existList) && existList.Count > 0;
				var hasNotExist = buckets.TryGetValue(WindowEventType.NotExist, out var notExistList) && notExistList.Count > 0;

				if (!hasExist && !hasNotExist)
					return;

				subs = (hasExist ? existList : Enumerable.Empty<WinEventRegistration>())
					   .Concat(hasNotExist ? notExistList : Enumerable.Empty<WinEventRegistration>())
					   .ToArray();
			}

			foreach (var reg in subs)
			{
				if (!reg.active)
					continue;

				var matches = !windowGone && CurrentlyMatches(reg, hwnd);

				// Test-and-set the membership atomically: HashSet.Add/Remove return whether the set actually changed,
				// so the fire decision is driven by the real transition under a single lock. (Snapshotting Contains
				// and then mutating under a second lock would let two threads both observe the same pre-state and
				// double-fire one transition — the matching set exists precisely to make each transition fire once.)
				bool changed;

				lock (reg.matchGate)
					changed = matches ? reg.matchingWindows.Add(hwnd) : reg.matchingWindows.Remove(hwnd);

				if (changed && ((matches && reg.IsExist) || (!matches && reg.IsNotExist)))
					FireOnce(reg, hwnd, timeMs);
			}
		}

		/// <summary>Seeds a membership subscription (Exist/NotExist) with the windows that already satisfy it, so
		/// Exist/NotExist only fire on genuine later transitions (not for windows already present at registration).
		/// Called under <see cref="gate"/>.</summary>
		private static void SeedMatches(WinEventRegistration reg)
		{
			try
			{
				// EnumerateWindows already yields top-level windows respecting the captured DetectHiddenWindows.
				foreach (var win in WindowQuery.EnumerateWindows(reg.detectHidden))
				{
					if (win.IsSpecified && SubscriptionMatches(reg, win))
						lock (reg.matchGate)
							_ = reg.matchingWindows.Add(win.Handle);
				}
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"WinEvent match seed failed: {ex.Message}");
			}
		}

		/// <summary>Whether <paramref name="win"/> (a live top-level window) satisfies membership subscription
		/// <paramref name="reg"/>: criteria subscriptions match the criteria; match-any subscriptions track any
		/// top-level window, respecting the captured DetectHiddenWindows setting.</summary>
		private static bool SubscriptionMatches(WinEventRegistration reg, WindowInfoBase win)
			=> reg.criteria != null
				? win.Equals(reg.criteria, reg.inheritedOptions)
				: reg.detectHidden || win.Visible;

		// ---- dispatch -----------------------------------------------------------------------

		private void FireOnce(WinEventRegistration reg, nint hwnd, long timeMs, Rectangle? eventBounds = null)
		{
			// A paused hook (or globally paused manager) stays registered and keeps its matching-window set
			// current, but doesn't fire or consume its remaining-count budget.
			if (globalPaused || reg.paused)
				return;

			if (!reg.TryConsumeFire())
				return;

			var scheduler = reg.ownerScheduler ?? script.EventScheduler;

			if (scheduler == null || scheduler.IsDisposed)
				return;

			// Every event uses the same callback shape: (hook, hwnd, time). Event-specific extras live in A_EventInfo
			// instead — for Move, an object with { x, y, w, h } (the window's position/size, matching WinGetPos), and
			// for CaretMove the same shape holding the caret's screen rectangle. The geometry was captured at event
			// time by the caller (eventBounds); only building the script object is deferred (see
			// RunOnSchedulerThread), so no allocation happens unless A_EventInfo is read.
			//
			// Callback-time contract (locked, cross-platform): the 3rd arg is a 64-bit monotonic milliseconds-since-boot
			// timestamp on the Environment.TickCount64 timebase, reporting when the event occurred wherever possible.
			// Windows reconstructs it from the native event time (WindowEventBackend.ToMonotonicMs); macOS stamps it at
			// delivery. On X11 it is the X server time converted to that base for events that carry one (PropertyNotify
			// → Active/TitleChange/Minimize/Restore/Create/Close), else the receipt time (ConfigureNotify/Map/Unmap →
			// Move/Show/Minimize). It never wraps (unlike Windows' raw 32-bit dwmsEventTime) and is comparable across
			// backends, but is NOT wall-clock time — only meaningful relative to itself.
			object[] args = [reg.scriptObject, hwnd.ToInt64(), timeMs];
			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunOnSchedulerThread(scheduler, reg, hwnd, timeMs, args, eventBounds));

			if (reg.IsExhausted)
				Unregister(reg);
		}

		/// <summary>The window's screen bounds (matching WinGetPos), or empty if it can't be resolved.</summary>
		private static Rectangle QueryBounds(nint hwnd)
		{
			try
			{
				var win = WindowQuery.CreateWindow(hwnd);

				if (win != null && win.IsSpecified)
					return win.Bounds;
			}
			catch
			{
			}

			return Rectangle.Empty;
		}

		/// <summary>Builds the A_EventInfo object for a Move (the window's rectangle, in WinGetPos coordinates) or
		/// CaretMove (the caret's screen rectangle) event — <c>{ x, y, w, h }</c> — from the already-captured
		/// event-time bounds. Invoked lazily (only if the callback reads A_EventInfo); whatever produced
		/// <paramref name="bounds"/> happened eagerly at event time, so this just allocates.</summary>
		private static object BuildRectEventInfo(Rectangle? bounds)
		{
			var r = bounds ?? Rectangle.Empty;
			return Keysharp.Builtins.Objects.RectObject(r.X, r.Y, r.Width, r.Height);
		}

		private ScriptEventExecutionResult RunOnSchedulerThread(ScriptEventScheduler scheduler, WinEventRegistration reg, nint hwnd, long timeMs, object[] args, Rectangle? eventBounds)
		{
			// No reg.active re-check here: TryConsumeFire (at enqueue time) is the authoritative gate. Re-checking
			// would drop callbacks that were already consumed/queued when the subscription auto-stops on its last
			// allowed fire (Count reaching 0 deactivates the registration before the queued callback runs).
			using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.WinEvent);

			if (!thread.Started)
				return thread.Result;

			try
			{
				var tv = thread.ThreadVariables;

				// Move (window geometry) and CaretMove (caret rectangle) expose their rectangle via A_EventInfo
				// (lazily); all other events keep the event time there, as before.
				if (reg.type is WindowEventType.Move or WindowEventType.CaretMove)
					tv.SetEventInfo(() => BuildRectEventInfo(eventBounds));
				else
					tv.eventInfo = timeMs;

				tv.hwndLastUsed = hwnd.ToInt64();
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

		// ---- teardown -----------------------------------------------------------------------

		public void Dispose()
		{
			List<WinEventRegistration> all;

			IWindowEventBackend toDispose;

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				all = buckets.Values.SelectMany(l => l).ToList();
				buckets.Clear();
				toDispose = backend;
				backend = null;
				installedMask = WindowEventMask.None;
			}

			foreach (var reg in all)
			{
				reg.active = false;
				reg.registration.Clear();
			}

			try
			{
				toDispose?.Dispose();
			}
			catch (Exception ex)
			{
				Ks.OutputDebugLine($"Window event backend dispose failed: {ex.Message}");
			}
		}
	}
}
