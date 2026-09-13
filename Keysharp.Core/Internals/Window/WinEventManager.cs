using Keysharp.Builtins;
using Keysharp.Internals.Scripting;

namespace Keysharp.Internals.Window
{
	/// <summary>
	/// One run of a <c>Ks.WinEvent</c>: its parsed criteria, the search settings captured when it was constructed, and
	/// its callback slots. Which slots are set is fixed for the run, because the native events and tracking follow from
	/// it; the object builds a fresh run on every <c>Start()</c>, so callbacks queued by an earlier run hold that run's
	/// registration and are discarded with it.
	/// </summary>
	internal sealed class WinEventRegistration : EventSubscriptionBase
	{
		internal readonly object[] callbacks;                 // indexed by WindowEventType; null where the run has no slot
		internal readonly WindowEventMask mask;               // the native events the run's slots need
		internal readonly SearchCriteria criteria;            // null => match any window
		internal readonly WindowSearchOptions inheritedOptions;
		internal readonly WinEventManager manager;
		internal readonly bool detectHidden;                  // effective DetectHiddenWindows for this run
		internal nint activeReported;                         // the matching window last reported active, or 0
		internal Rectangle? lastCaretRect;                    // CaretMove: last caret rectangle seen, so a native
		                                                      // source repeating an unchanged position doesn't "move"

		// A run with an OnExist or OnNotExist slot keeps the set of top-level windows that currently satisfy it.
		// Mirroring the AHK WinEvent library's MatchingWinList, the set is seeded silently at registration and kept
		// current as windows enter/leave (Create/Show/Restore/TitleChange add; Close/Minimize/TitleChange remove), so
		// both slots fire on genuine transitions rather than the raw lifecycle event.
		internal readonly HashSet<nint> matchingWindows;
		internal readonly Lock matchGate;

		internal bool Has(WindowEventType slot) => callbacks[(int)slot] != null;
		internal bool TracksMembership => matchingWindows != null;
		internal bool TracksActive => Has(WindowEventType.Active) || Has(WindowEventType.NotActive);

		internal override void Unregister() => manager.Unregister(this);

		internal override void Register() => manager.Register(this);

		/// <summary>Gives a slot that already has a callback another one, in the running run: the events the run
		/// watches are unchanged, so only the callback the next event calls differs.</summary>
		internal void ReplaceCallback(int slot, object callback) => Volatile.Write(ref callbacks[slot], callback);

		internal WinEventRegistration(SearchCriteria criteria, WindowSearchOptions options, object[] callbacks,
			ScriptEventScheduler ownerScheduler, WinEventManager manager)
			: base(null, ownerScheduler, manager.KeepsScriptRunning)
		{
			this.criteria = criteria;
			this.callbacks = callbacks;
			this.manager = manager;
			inheritedOptions = options;
			detectHidden = options.DetectHiddenWindows == true;

			for (var i = 0; i < callbacks.Length; i++)
				if (callbacks[i] != null)
					mask |= ((WindowEventType)i).ToMask();

			// NotActive is derived from the foreground changes, as Exist/NotExist are from the lifecycle events. A title
			// change can move the foreground window into or out of a match only when there are criteria.
			if (TracksActive)
				mask |= WindowEventMask.Active | (criteria != null ? WindowEventMask.TitleChange : WindowEventMask.None);

			if (Has(WindowEventType.Exist) || Has(WindowEventType.NotExist))
			{
				matchingWindows = new HashSet<nint>();
				matchGate = new Lock();
				mask |= WindowEventMask.Create | WindowEventMask.Show | WindowEventMask.Restore
						| WindowEventMask.Close | WindowEventMask.Minimize | WindowEventMask.TitleChange;
			}
		}

		/// <summary>The window-search context of the calling thread, captured once, as the AHK WinEvent library captures
		/// A_DetectHiddenWindows/Text and the title-match mode when a hook is made.</summary>
		internal static WindowSearchOptions CaptureSearchOptions(Script script)
		{
			var config = script.Threads.CurrentThread.configData;
			return new WindowSearchOptions
			{
				DetectHiddenWindows = config.detectHiddenWindows,
				DetectHiddenText = config.detectHiddenText,
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
	/// per-subscription dedup. Move events are delivered as the backend reports them; only the Linux backend merges
	/// a window's queued moves.
	/// </summary>
	internal sealed class WinEventManager(Script script)
		: EventManagerBase<WinEventRegistration, IWindowEventBackend, WinEventManager.Payload>(script, true)
	{
		internal static readonly int typeCount = Enum.GetValues<WindowEventType>().Length;

		/// <summary>What one window event carries to the callback's thread state: the slot it fires, for the rectangle
		/// OnMove and OnCaretMove put in <c>A_EventInfo</c>, and the window, which becomes the Last Found Window.</summary>
		internal readonly record struct Payload(WindowEventType Slot, nint Hwnd, Rectangle? Bounds);

		// The intake's read-only view of the subscriptions, indexed by (int)WindowEventType and republished as a
		// whole on every registration change. Native events arrive at Move/CaretMove rates, so reading them must
		// neither take `gate` nor allocate; every array here is immutable once published, so an intake that reads
		// the field once sees one consistent generation.
		private volatile WinEventRegistration[][] byType = Empty();
		// Whether any run tracking the active window has criteria, the only kind a title change can move into or out of
		// a match. Published with byType.
		private volatile bool activeHasCriteria;
		private WindowEventMask installedMask = WindowEventMask.None;
		private volatile bool foregroundTracking;
		private volatile bool foregroundEvents;
		private nint foregroundWindowHandle;
		private long foregroundGeneration;

		protected override ThreadKind CallbackThreadKind => ThreadKind.WinEvent;

		// ---- foreground tracking -----------------------------------------------------------

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

			foreach (var reg in registrations)
				desired |= reg.mask;

			foregroundEvents = foregroundTracking && !disposed
				&& (Backend ?? EnsureBackend())?.SupportsEfficientActiveTracking == true;

			if (foregroundEvents)
				desired |= WindowEventMask.Active | WindowEventMask.Close;

			if (desired == installedMask)
				return;

			if (desired != WindowEventMask.None)
			{
				var b = EnsureBackend();

				if (b == null)
				{
					FailAllLocked();                          // unsupported environment; these hooks can never fire
					return;
				}

				var toRemove = installedMask & ~desired;
				var toAdd = desired & ~installedMask;

				if (toRemove != WindowEventMask.None)
				{
					b.Stop(toRemove);
					installedMask &= ~toRemove;
				}

				// Counted before the install, so a partial install that throws is undone by the next sync.
				if (toAdd != WindowEventMask.None)
				{
					installedMask |= toAdd;
					b.Start(toAdd);
				}
			}
			else
			{
				Backend?.Stop(installedMask);
				installedMask = WindowEventMask.None;
			}
		}

		/// <summary>Seeds the matching-window set so a Close fires for windows that existed before the subscription,
		/// and so Exist/NotExist only fire on genuine transitions (not for windows already matching at
		/// registration). Mirrors the AHK WinEvent library seeding its MatchingWinList up front.</summary>
		protected override void PrepareRegistration(WinEventRegistration reg, bool isFirst)
		{
			// Seeded silently, so a window already active at Start() is reported only when it stops being active.
			if (reg.TracksActive)
				try
				{
					var foreground = WindowQuery.GetForegroundWindowHandle();

					if (Matches(reg, foreground))
						reg.activeReported = foreground;
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"WinEvent active seed failed: {ex.Message}");
				}

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
			{
				for (var t = 0; t < typeCount; t++)
					if (reg.callbacks[t] != null && t != (int)WindowEventType.NotActive)
						(grouped[t] ??= []).Add(reg);

				// NotActive rides on the Active events, which also keep activeReported current for it.
				if (reg.Has(WindowEventType.NotActive) && !reg.Has(WindowEventType.Active))
					(grouped[(int)WindowEventType.Active] ??= []).Add(reg);
			}

			var next = new WinEventRegistration[typeCount][];

			for (var i = 0; i < typeCount; i++)
				next[i] = grouped[i] is { } list ? [.. list] : [];

			activeHasCriteria = next[(int)WindowEventType.Active].Any(reg => reg.criteria != null);
			byType = next;
		}

		/// <summary>OnMove (the window's geometry) and OnCaretMove (the caret's rectangle) expose their rectangle through
		/// <c>A_EventInfo</c>, built only if the callback reads it; the geometry itself was captured at event time.</summary>
		protected override void ApplyThreadState(ThreadVariables tv, WinEventRegistration reg, in Payload payload)
		{
			if (payload.Slot is WindowEventType.Move or WindowEventType.CaretMove)
			{
				var bounds = payload.Bounds;
				tv.SetEventInfo(() => BuildRectEventInfo(bounds));
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

		internal void OnNativeEvent(WindowEventRaw raw)
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
				// activeReported is the matching window last reported active. NotActive is set-level: it fires when the
				// foreground leaves the matching windows, not when it moves from one to another. Every matching
				// activation reports Active, while a mere title change of the reported window (handled in
				// DispatchActiveOnTitleChange) does not.
				foreach (var reg in snapshot)
				{
					if (!reg.IsActive)
						continue;

					var matched = Matches(reg, raw.Hwnd);
					var left = reg.activeReported;
					reg.activeReported = matched ? raw.Hwnd : 0;

					if (left != 0 && !matched)
						FireOnce(reg, WindowEventType.NotActive, left, raw.TimeMs);
					else if (matched)
						FireOnce(reg, WindowEventType.Active, raw.Hwnd, raw.TimeMs);
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

				FireOnce(reg, raw.Type, raw.Hwnd, raw.TimeMs, eventBounds);
			}
		}

		/// <summary>Reports a foreground window whose title change moved it into or out of the criteria.</summary>
		private void DispatchActiveOnTitleChange(nint hwnd, long timeMs)
		{
			// Every title change arrives here, so the foreground query waits until some run's verdict could change.
			if (!activeHasCriteria || hwnd == 0 || hwnd != WindowQuery.GetForegroundWindowHandle())
				return;

			var runs = byType[(int)WindowEventType.Active];

			foreach (var reg in runs)
			{
				// Only criteria can change their verdict with a title: a window that became active before its title
				// matched is reported Active now, and the reported window retitled out of the match NotActive.
				if (!reg.IsActive || reg.criteria == null)
					continue;

				// Evaluated even without an OnNotActive slot, so a window retitled out and back in reports Active again.
				var reported = reg.activeReported == hwnd;
				var matched = Matches(reg, hwnd);

				if (matched && !reported)
				{
					reg.activeReported = hwnd;
					FireOnce(reg, WindowEventType.Active, hwnd, timeMs);
				}
				else if (!matched && reported)
				{
					reg.activeReported = 0;
					FireOnce(reg, WindowEventType.NotActive, hwnd, timeMs);
				}
			}
		}

		private static bool Matches(WinEventRegistration reg, nint hwnd)
		{
			if (hwnd == 0)
				return false;

			var control = Control.FromHandle(hwnd);
			return control == null ? MatchesCore(reg, hwnd) : MatchOnUiThread(control, reg, hwnd, false);
		}

		private static bool MatchesCore(WinEventRegistration reg, nint hwnd)
		{
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

			var control = Control.FromHandle(hwnd);
			return control == null ? CurrentlyMatchesCore(reg, hwnd) : MatchOnUiThread(control, reg, hwnd, true);
		}

		private static bool CurrentlyMatchesCore(WinEventRegistration reg, nint hwnd)
		{
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

		// A window of this script has UI-thread affinity, so it is matched there. Kept apart from the callers so their
		// foreign-window path, the one native events take, allocates no closure.
		private static bool MatchOnUiThread(Control control, WinEventRegistration reg, nint hwnd, bool current)
			=> control.CheckedInvoke(() => current ? CurrentlyMatchesCore(reg, hwnd) : MatchesCore(reg, hwnd), false);

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

			// A run with both slots is in both lists and shares one set, so it was handled above.
			foreach (var reg in view[(int)WindowEventType.NotExist])
				if (!reg.Has(WindowEventType.Exist))
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

			if (changed)
				FireOnce(reg, matches ? WindowEventType.Exist : WindowEventType.NotExist, hwnd, timeMs);
		}

		/// <summary>Whether <paramref name="win"/> (a live top-level window) satisfies membership subscription
		/// <paramref name="reg"/>: criteria subscriptions match the criteria; match-any subscriptions track any
		/// top-level window, respecting the captured DetectHiddenWindows setting.</summary>
		private static bool SubscriptionMatches(WinEventRegistration reg, WindowInfoBase win)
			=> reg.criteria != null
				? win.Equals(reg.criteria, reg.inheritedOptions)
				: reg.detectHidden || win.Visible;

		// ---- dispatch -----------------------------------------------------------------------

		private void FireOnce(WinEventRegistration reg, WindowEventType slot, nint hwnd, long timeMs, Rectangle? eventBounds = null)
		{
			// Every slot has the same callback shape: (Hook, Hwnd, Time). Time is a monotonic millisecond timestamp on
			// the A_TickCount timebase: Windows rebuilds it from the native event time, Linux and macOS stamp delivery.
			if (reg.callbacks[(int)slot] is { } callback)
				Fire(reg, callback, [reg.scriptObject, hwnd.ToInt64(), timeMs], new Payload(slot, hwnd, eventBounds));
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
		/// CaretMove (the caret's screen rectangle) event — <c>{X, Y, Width, Height}</c> — from the already-captured
		/// event-time bounds.</summary>
		private static object BuildRectEventInfo(Rectangle? bounds)
		{
			var r = bounds ?? Rectangle.Empty;
			return Keysharp.Builtins.Objects.RectObject(r.X, r.Y, r.Width, r.Height);
		}
	}
}
