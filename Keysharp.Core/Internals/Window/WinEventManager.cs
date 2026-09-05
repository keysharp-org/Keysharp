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
		internal readonly WinEventManager manager;
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

		/// <summary>Window events have a manager-wide pause switch (<c>WinEvent.Paused</c>) on top of each hook's own.</summary>
		internal override bool Suppressed => paused || manager.GlobalPaused;

		internal override void Unregister() => manager.Unregister(this);

		internal WinEventRegistration(WindowEventType type, SearchCriteria criteria, KeysharpFunc callback, long count,
			ScriptEventScheduler ownerScheduler, WinEventManager manager)
			: base(callback, count, ownerScheduler)
		{
			this.type = type;
			this.criteria = criteria;
			this.manager = manager;

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
			var config = ownerScheduler.Owner.Threads.CurrentThread.configData;
			detectHidden = forceHidden || config.detectHiddenWindows;
			inheritedOptions = new WindowSearchOptions
			{
				DetectHiddenWindows = detectHidden,
				DetectHiddenText = forceHidden || config.detectHiddenText,
				TitleMatchMode = config.titleMatchMode,
				TitleMatchModeSpeed = config.titleMatchModeSpeed
			};
		}
	}

	/// <summary>
	/// The per-<see cref="Script"/> engine behind <c>Ks.WinEvent</c>. <see cref="EventManagerBase{TRegistration, TBackend, TPayload}"/>
	/// owns the subscriptions, the backend lifecycle and the dispatch tail; what is left here is the part that is
	/// actually about windows — installing native hooks for exactly the categories the subscriptions need, and
	/// turning each incoming <see cref="WindowEventRaw"/> into criteria matching, membership transitions and
	/// per-subscription dedup. Move events are delivered as-is (no coalescing).
	/// </summary>
	internal sealed class WinEventManager(Script script)
		: EventManagerBase<WinEventRegistration, IWindowEventBackend, WinEventManager.Payload>(script)
	{
		private static readonly int typeCount = Enum.GetValues<WindowEventType>().Length;

		/// <summary>
		/// What one window event carries to the callback's thread state.
		/// <para>
		/// Callback-time contract (locked, cross-platform): <c>TimeMs</c> — the callback's 3rd argument, and
		/// <c>A_EventInfo</c> for every event that does not carry a rectangle — is a 64-bit monotonic
		/// milliseconds-since-boot timestamp on the <c>Environment.TickCount64</c> timebase, reporting when the
		/// event occurred wherever possible. Windows reconstructs it from the native event time
		/// (<c>WindowEventBackend.ToMonotonicMs</c>); Linux and macOS stamp it when the managed backend delivers
		/// the event. It never wraps (unlike Windows' raw 32-bit dwmsEventTime) and is comparable across backends,
		/// but is not wall-clock time and is only meaningful relative to itself.
		/// </para>
		/// </summary>
		internal readonly record struct Payload(long TimeMs, nint Hwnd, Rectangle? Bounds);

		// The intake's read-only view of the subscriptions, indexed by (int)WindowEventType and republished as a
		// whole on every registration change. Native events arrive at Move/CaretMove rates, so reading them must
		// neither take `gate` nor allocate; every array here is immutable once published, so an intake that reads
		// the field once sees one consistent generation.
		private volatile WinEventRegistration[][] byType = Empty();
		private WindowEventMask installedMask = WindowEventMask.None;
		private volatile bool globalPaused;
		private volatile bool foregroundTracking;
		private volatile bool foregroundEvents;
		private nint foregroundWindowHandle;
		private long foregroundGeneration;

		protected override ThreadKind CallbackThreadKind => ThreadKind.WinEvent;

		// ---- global pause ------------------------------------------------------------------

		/// <summary>True while all hooks are globally paused.</summary>
		internal bool GlobalPaused => globalPaused;

		/// <summary>The last foreground handle observed while the input hook requests tracking.</summary>
		internal nint ForegroundWindowHandle
		{
			get
			{
				if (foregroundTracking && !foregroundEvents)
					try { return WindowQuery.GetForegroundWindowHandle(); }
					catch { }

				return Volatile.Read(ref foregroundWindowHandle);
			}
		}

		/// <summary>
		/// Adds or removes the input hook's internal demand for foreground events. The input hook calls this on
		/// its serialized background queue because native backend setup and teardown can block; later changes are
		/// event-driven.
		/// </summary>
		internal void SetForegroundTracking(bool enabled)
		{
			long generation;

			lock (gate)
			{
				if (disposed || foregroundTracking == enabled)
					return;

				foregroundTracking = enabled;
				generation = ++foregroundGeneration;

				if (!enabled)
					Volatile.Write(ref foregroundWindowHandle, 0);

				try
				{
					SyncNativeLocked();
				}
				catch (Exception exception)
				{
					if (enabled)
					{
						foregroundTracking = false;
						generation = ++foregroundGeneration;
						Volatile.Write(ref foregroundWindowHandle, 0);
					}

					Diagnostics.Debug.WriteLine(
						$"Foreground window tracking could not be {(enabled ? "started" : "stopped")}: {exception.Message}");
				}
			}

			if (!enabled)
				return;

			nint queried;

			try
			{
				queried = WindowQuery.GetForegroundWindowHandle();
			}
			catch (Exception exception)
			{
				Diagnostics.Debug.WriteLine($"Foreground window query failed: {exception.Message}");
				return;
			}

			lock (gate)
				if (!disposed && foregroundTracking && foregroundGeneration == generation)
					Volatile.Write(ref foregroundWindowHandle, queried);
		}

		/// <summary>Pauses (1), unpauses (0) or toggles (-1) all hooks; returns the resulting state.</summary>
		internal bool SetGlobalPause(long newState)
		{
			globalPaused = newState == -1 ? !globalPaused : newState != 0;
			return globalPaused;
		}

		// ---- source hooks --------------------------------------------------------------------

		protected override IWindowEventBackend CreateBackend()
		{
			var created = Platform.WindowEvents.CreateBackend(script);

			if (created != null)
				created.Sink = OnNativeEvent;

			return created;
		}

		/// <summary>Recomputes which event categories are needed and installs/uninstalls native hooks on the
		/// backend to match.</summary>
		protected override void SyncNativeLocked()
		{
			var desired = WindowEventMask.None;
			var tracksMembership = false;
			var hasActive = false;

			foreach (var reg in registrations)
			{
				desired |= reg.type.ToMask();
				tracksMembership |= reg.TracksMembership;
				hasActive |= reg.type == WindowEventType.Active;
			}

			foregroundEvents = foregroundTracking && !disposed
				&& (Backend ?? EnsureBackend())?.SupportsEfficientActiveTracking == true;

			if (foregroundEvents)
				desired |= WindowEventMask.Active | WindowEventMask.Close;

			// Exist/NotExist are membership transitions derived from the lifecycle events, so they need every event
			// that can move a window into or out of the matching set: appear (Create/Show/Restore), disappear
			// (Close/Minimize) and re-match (TitleChange).
			if (tracksMembership)
				desired |= WindowEventMask.Create | WindowEventMask.Show | WindowEventMask.Restore
						   | WindowEventMask.Close | WindowEventMask.Minimize | WindowEventMask.TitleChange;

			// Active also fires on the active window's title change, so observe TitleChange when any Active sub exists.
			if (hasActive)
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
				Backend?.Stop(installedMask);
			}

			installedMask = desired;
		}

		/// <summary>Seeds the matching-window set so a Close fires for windows that existed before the subscription,
		/// and so Exist/NotExist only fire on genuine transitions (not for windows already matching at
		/// registration). Mirrors the AHK WinEvent library seeding its MatchingWinList up front.</summary>
		protected override void PrepareRegistration(WinEventRegistration reg, bool isFirst)
		{
			if (!reg.TracksMembership)
				return;

			try
			{
				// EnumerateWindows already yields top-level windows respecting the captured DetectHiddenWindows.
				foreach (var win in WindowQuery.EnumerateWindows(reg.detectHidden))
					if (win.IsSpecified && SubscriptionMatches(reg, win))
						lock (reg.matchGate)
							_ = reg.matchingWindows.Add(win.Handle);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"WinEvent match seed failed: {ex.Message}");
			}
		}

		protected override void OnRegistrationsChangedLocked()
		{
			var grouped = new List<WinEventRegistration>[typeCount];

			foreach (var reg in registrations)
				(grouped[(int)reg.type] ??= []).Add(reg);

			var next = new WinEventRegistration[typeCount][];

			for (var i = 0; i < typeCount; i++)
				next[i] = grouped[i] is { } list ? [.. list] : [];

			byType = next;
		}

		/// <summary>
		/// Move (window geometry) and CaretMove (caret rectangle) expose their rectangle via A_EventInfo, built only
		/// if the callback reads it; every other event keeps the event time there. The geometry itself was captured
		/// at event time by the intake, so this only allocates.
		/// </summary>
		protected override void ApplyThreadState(ThreadVariables tv, WinEventRegistration reg, in Payload payload)
		{
			if (reg.type is WindowEventType.Move or WindowEventType.CaretMove)
			{
				var bounds = payload.Bounds;
				tv.SetEventInfo(() => BuildRectEventInfo(bounds));
			}
			else
			{
				tv.eventInfo = payload.TimeMs;
			}

			tv.hwndLastUsed = payload.Hwnd.ToInt64();
		}

		private static WinEventRegistration[][] Empty()
		{
			var empty = new WinEventRegistration[typeCount][];

			for (var i = 0; i < empty.Length; i++)
				empty[i] = [];

			return empty;
		}

		// ---- native event intake (arbitrary thread, from the backend) -----------------------

		private void OnNativeEvent(WindowEventRaw raw)
		{
			if (disposed)
				return;

			if (foregroundTracking
				&& raw.Type is WindowEventType.Active or WindowEventType.Close)
			{
				lock (gate)
				{
					if (foregroundTracking)
					{
						if (raw.Type == WindowEventType.Active)
						{
							foregroundGeneration++;
							Volatile.Write(ref foregroundWindowHandle, raw.Hwnd);
						}
						else if (ForegroundWindowHandle == raw.Hwnd)
						{
							foregroundGeneration++;
							Volatile.Write(ref foregroundWindowHandle, 0);
						}
					}
				}
			}

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

			var snapshot = byType[(int)raw.Type];

			if (snapshot.Length == 0)
				return;

			if (raw.Type == WindowEventType.Active)
			{
				// Record which window each Active subscription reported, and reset it on every activation so
				// re-activating the same window still fires while a mere title-change of the already-reported
				// active window (handled in DispatchActiveOnTitleChange) does not duplicate.
				foreach (var reg in snapshot)
				{
					if (!reg.IsActive)
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
			// carry the bounds on the event (raw.Bounds); X11/Windows do the cheap local query here.
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
				if (!reg.IsActive || !Matches(reg, raw.Hwnd))
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

			foreach (var reg in byType[(int)WindowEventType.Active])
			{
				// Only criteria subscriptions need the title-change re-fire — it exists to catch a window that
				// became active before its title (hence its match) was set. A match-any Active subscription
				// already fired on the activation itself, so re-firing on its title changes is pure duplication.
				// activeReported then dedupes the case where the activation already matched and fired.
				if (reg.IsActive && reg.criteria != null && reg.activeReported != hwnd && Matches(reg, hwnd))
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

			// One read of the published view, so both membership kinds are evaluated against the same generation.
			var view = byType;

			foreach (var reg in view[(int)WindowEventType.Exist])
				UpdateMembershipFor(reg, hwnd, timeMs, windowGone);

			foreach (var reg in view[(int)WindowEventType.NotExist])
				UpdateMembershipFor(reg, hwnd, timeMs, windowGone);
		}

		private void UpdateMembershipFor(WinEventRegistration reg, nint hwnd, long timeMs, bool windowGone)
		{
			if (!reg.IsActive)
				return;

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
			if (reg.Suppressed || !reg.TryConsumeFire())
				return;

			var scheduler = DispatchTarget(reg);

			if (scheduler == null)
				return;

			// Every event uses the same callback shape: (hook, hwnd, time). Event-specific extras live in
			// A_EventInfo instead — see Payload for what the timestamp means, and ApplyThreadState for the rest.
			object[] args = [reg.scriptObject, hwnd.ToInt64(), timeMs];
			var payload = new Payload(timeMs, hwnd, eventBounds);
			_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunCallback(scheduler, reg, args, payload));

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
		/// event-time bounds.</summary>
		private static object BuildRectEventInfo(Rectangle? bounds)
		{
			var r = bounds ?? Rectangle.Empty;
			return Keysharp.Builtins.Objects.RectObject(r.X, r.Y, r.Width, r.Height);
		}
	}
}
