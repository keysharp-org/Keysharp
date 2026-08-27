#if LINUX
using Keysharp.Builtins;
using Keysharp.Internals.Window.Linux.X11;

namespace Keysharp.Internals.Window.Linux
{
	/// <summary>
	/// Linux <see cref="IWindowEventBackend"/>. Rather than running a private X11 connection on its own
	/// <c>XNextEvent</c> pump, this hooks GDK's existing event loop on the UI thread and reads the raw
	/// <c>XEvent</c>s it processes. Because per-window <c>PropertyNotify</c> (how title and minimize/restore
	/// changes arrive) is only ever delivered to a <em>global</em> GDK filter — the root-window filter sees only
	/// events routed to root — a global filter (<c>gdk_window_add_filter(NULL, …)</c>) is installed, and the
	/// masks we need are OR-ed onto GDK's own X connection without clobbering the masks GDK relies on.
	/// <para>
	/// Create/Close are driven by diffing <c>_NET_CLIENT_LIST</c> on the root window (the WM's authoritative set
	/// of managed top-level windows), which is exactly "windows" in the AHK sense — it excludes override-redirect
	/// popups/menus/tooltips and toolkit helper windows, and avoids the BadWindow races of querying raw
	/// <c>CreateNotify</c> children of root. On a WM that doesn't expose <c>_NET_CLIENT_LIST</c> we fall back to
	/// override-redirect-filtered <c>CreateNotify</c>/<c>DestroyNotify</c>. Each managed window is watched with
	/// <c>PropertyChange</c> so <c>_NET_WM_NAME</c> drives TitleChange and <c>_NET_WM_STATE</c>'s
	/// <c>_NET_WM_STATE_HIDDEN</c> drives Minimize/Restore, and <c>StructureNotify</c> so the client's own
	/// <c>ConfigureNotify</c>/<c>MapNotify</c>/<c>UnmapNotify</c> drive Move/Show/Minimize even after a reparenting WM
	/// moves it into a frame (root <c>SubstructureNotify</c> then no longer sees those events; it is kept as a
	/// fallback for windows we aren't separately watching, with a `watched` set de-duping the two paths).
	/// </para>
	/// <para>
	/// Threading: attach/detach and the filter all run on the GDK main-loop (UI) thread, so the client-list and
	/// minimize-state bookkeeping below needs no locking; only <see cref="enabledMask"/> (touched by Start/Stop on
	/// arbitrary threads) is guarded by <see cref="gate"/>.
	/// </para>
	/// </summary>
	internal sealed class WindowEventBackend : IWindowEventBackend
	{
		private readonly Script owner;

		// GdkFilterReturn (*GdkFilterFunc)(GdkXEvent *xevent, GdkEvent *event, gpointer data); return 0 = CONTINUE.
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int GdkFilterFuncNative(nint xevent, nint gdkEvent, nint data);

		[DllImport("libgdk-3.so.0")]
		private static extern void gdk_window_add_filter(nint window, GdkFilterFuncNative function, nint data);

		[DllImport("libgdk-3.so.0")]
		private static extern void gdk_window_remove_filter(nint window, GdkFilterFuncNative function, nint data);

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_x11_get_default_xdisplay();

		[DllImport("libgdk-3.so.0")]
		private static extern nint gdk_display_get_default();

		[DllImport("libgdk-3.so.0")]
		private static extern void gdk_x11_display_error_trap_push(nint display);

		[DllImport("libgdk-3.so.0")]
		private static extern void gdk_x11_display_error_trap_pop_ignored(nint display);

		// CaretMove doesn't come from X11 at all — the caret is an accessibility concept, so it is served by the
		// display-server-agnostic AT-SPI source (which the Wayland backend owns one of too). Every other category is
		// X11's, and the GDK filter is only attached when at least one of those is wanted.
		private readonly LinuxAccessibility.CaretEventSource caretSource;

		private readonly Lock gate = new();
		private WindowEventMask enabledMask = WindowEventMask.None;
		private GdkFilterFuncNative filter;    // kept alive while attached so GDK can call it
		private bool filterAttached;
		private nint gdkDisplay;               // GdkDisplay* (for error traps)
		private nint gdkXDisplay;              // GDK's X Display* (the connection we select events on)
		private long rootXid;
		private bool disposed;

		// UI-thread-only bookkeeping (filter + attach/detach all run on the GDK main-loop thread):
		private bool useClientList;
		private readonly HashSet<nint> knownClients = new();      // last seen _NET_CLIENT_LIST
		private readonly HashSet<nint> watched = new();           // windows we've selected StructureNotify/PropertyChange on
		private readonly Dictionary<nint, bool> hiddenStates = new();  // per-window _NET_WM_STATE_HIDDEN
		private readonly Dictionary<nint, string> lastTitles = new(); // per-window resolved title (TitleChange dedup)
		private nint lastActive;                                 // last emitted active window (Active dedup)

		// X server timestamps (PropertyNotify.time; unsigned 32-bit ms since server start, wraps ~every 49 days) are
		// converted to the Environment.TickCount64 timebase so PropertyNotify-sourced events report when they occurred
		// rather than the receipt time. serverTimeOffset is the running minimum of (TickCount64_at_receipt - extended
		// server time): the minimum strips per-event queue/receipt latency, leaving the near-constant epoch offset.
		private bool serverTimeInited;
		private uint lastServerTime;    // last raw 32-bit server time (for 64-bit wrap extension)
		private long serverTimeHigh;    // accumulated wraps (* 2^32): extended = serverTimeHigh + rawServerTime
		private long serverTimeOffset;  // extended server time -> TickCount64 base

		public Action<WindowEventRaw> Sink { get; set; }

		internal WindowEventBackend(Script owner)
		{
			this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
			caretSource = new LinuxAccessibility.CaretEventSource(owner);
		}

		public void Start(WindowEventMask mask)
		{
			if ((mask & WindowEventMask.CaretMove) != 0)
				caretSource.Start(EmitCaretEvent);

			lock (gate)
			{
				if (disposed)
					return;

				enabledMask |= mask;

				if (filterAttached || !NeedsX11Filter(enabledMask))
					return;
			}

			owner.PostToUIThread(AttachOnUI);
		}

		public void Stop(WindowEventMask mask)
		{
			if ((mask & WindowEventMask.CaretMove) != 0)
				caretSource.Stop();

			bool detach;

			lock (gate)
			{
				enabledMask &= ~mask;
				detach = !NeedsX11Filter(enabledMask);
			}

			if (detach)
				owner.PostToUIThread(DetachOnUI);
		}

		public void Dispose()
		{
			disposed = true;

			lock (gate)
				enabledMask = WindowEventMask.None;

			caretSource.Dispose();
			owner.PostToUIThread(DetachOnUI);
		}

		/// <summary>Whether any category the X11 filter actually serves is enabled — i.e. anything but CaretMove,
		/// which comes from AT-SPI instead. A caret-only subscription must not put a global filter on GDK's event
		/// stream for events nothing would look at.</summary>
		private static bool NeedsX11Filter(WindowEventMask mask) => (mask & ~WindowEventMask.CaretMove) != WindowEventMask.None;

		private void EmitCaretEvent(WindowEventRaw ev) => Sink?.Invoke(ev);

		// ---- UI-thread attach/detach (runs on the GDK main loop) ----------------------------

		private void AttachOnUI()
		{
			lock (gate)
				// No X11-served category left means a Stop cleared them all before this queued attach ran — skip it (a
				// queued DetachOnUI will settle the state). Combined with DetachOnUI re-checking enabledMask, the last
				// enabledMask writer wins on the UI thread, so a rapid Stop→Start can't end up detached-while-enabled.
				if (disposed || filterAttached || !NeedsX11Filter(enabledMask))
					return;

			gdkDisplay = gdk_display_get_default();
			gdkXDisplay = gdk_x11_get_default_xdisplay();

			if (gdkDisplay == 0 || gdkXDisplay == 0)
			{
				Diagnostics.Debug.WriteLine("WinEvent: GDK display unavailable for the X11 event filter.");
				return;
			}

			rootXid = Xlib.XDefaultRootWindow(gdkXDisplay);

			// Select on root: SubstructureNotify (Show/Move of top-levels, and the Create/Close fallback) and
			// PropertyChange (_NET_ACTIVE_WINDOW → Active, _NET_CLIENT_LIST → Create/Close). Preserve GDK's mask.
			gdk_x11_display_error_trap_push(gdkDisplay);

			try
			{
				var attr = new XWindowAttributes();

				if (Xlib.XGetWindowAttributes(gdkXDisplay, rootXid, ref attr) != 0)
				{
					var combined = (EventMasks)attr.your_event_mask.ToInt64() | EventMasks.SubstructureNofity | EventMasks.PropertyChange;
					_ = Xlib.XSelectInput(gdkXDisplay, rootXid, combined);
				}
			}
			finally
			{
				gdk_x11_display_error_trap_pop_ignored(gdkDisplay);
			}

			// Seed the managed-window set and start watching each member for title/state changes. Seed BEFORE
			// installing the filter so the first _NET_CLIENT_LIST diff doesn't report pre-existing windows as new.
			SeedClientList();

			// A *global* filter (null window): per-window PropertyNotify for app windows is not routed to the root
			// filter, only to global filters.
			filter = NativeFilter;

			try
			{
				gdk_window_add_filter(0, filter, 0);

				lock (gate)
					filterAttached = true;
			}
			catch (Exception ex)
			{
				filter = null;
				Diagnostics.Debug.WriteLine($"WinEvent X11 filter attach failed: {ex.Message}");
			}
		}

		private void DetachOnUI()
		{
			GdkFilterFuncNative f;

			lock (gate)
			{
				if (!filterAttached)
					return;

				// Last-writer-wins after a rapid Stop→Start: Stop queued this detach, then Start re-enabled a category
				// but saw filterAttached still true and skipped posting AttachOnUI, relying on this re-check. If a
				// category is enabled again, keep the filter attached (detaching now would leave the new subscription
				// dead — the original bug). Only a real teardown (disposed) detaches while something is still enabled.
				if (!disposed && NeedsX11Filter(enabledMask))
					return;

				f = filter;
				filter = null;
				filterAttached = false;
			}

			// Leave the extra masks selected (root and per-window): harmless surplus events GDK ignores, and the
			// per-window targets are often gone anyway. Restoring could remove masks GDK added meanwhile.
			try
			{
				if (f != null)
					gdk_window_remove_filter(0, f, 0);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"WinEvent X11 filter detach failed: {ex.Message}");
			}

			knownClients.Clear();
			watched.Clear();
			hiddenStates.Clear();
			lastTitles.Clear();
			lastActive = 0;
			serverTimeInited = false;   // re-seed the server-time offset on the next attach (may be a different server)
			serverTimeHigh = 0;
			serverTimeOffset = 0;
			lastServerTime = 0;
		}

		// ---- GDK filter (runs on the UI thread, must never block long or consume) ------------

		private int NativeFilter(nint xevent, nint gdkEvent, nint data)
		{
			try
			{
				if (xevent != 0)
					Handle(xevent);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"WinEvent X11 filter error: {ex.Message}");
			}

			return 0;   // GDK_FILTER_CONTINUE — never consume; let GDK process the event normally.
		}

		private void Handle(nint xeventPtr)
		{
			var sink = Sink;

			if (sink == null)
				return;

			WindowEventMask mask;

			lock (gate)
				mask = enabledMask;

			// Read just the type first; most events (key/pointer/expose) aren't ours and don't need marshalling.
			var type = (XEventName)Marshal.ReadInt32(xeventPtr);

			switch (type)
			{
				case XEventName.PropertyNotify:
					HandleProperty(Marshal.PtrToStructure<XPropertyEvent>(xeventPtr), mask, sink);
					break;

				case XEventName.CreateNotify:
					// Fallback only (no _NET_CLIENT_LIST): top-level, non-override-redirect children of root.
					if (!useClientList && (mask & WindowEventMask.Create) != 0)
					{
						var e = Marshal.PtrToStructure<XCreateWindowEvent>(xeventPtr);

						if (e.parent == rootXid && !e.override_redirect)
						{
							WatchWindow((nint)e.window);   // so TitleChange works in fallback mode too
							sink(new WindowEventRaw(WindowEventType.Create, (nint)e.window, NowMs()));
						}
					}

					break;

				case XEventName.DestroyNotify:
					if (!useClientList && (mask & WindowEventMask.Close) != 0)
					{
						var e = Marshal.PtrToStructure<XDestroyWindowEvent>(xeventPtr);

						if (e.xevent == rootXid)
							// A DestroyNotify is an authoritative "window is gone", so mark it confirmed: the manager
							// then drops it from every Exist/NotExist matching set without re-checking a window list
							// that may still briefly resolve the just-destroyed X window.
							sink(new WindowEventRaw(WindowEventType.Close, (nint)e.window, NowMs()) { DestroyConfirmed = true });
					}

					break;

				case XEventName.MapNotify:
					if ((mask & WindowEventMask.Show) != 0)
					{
						var e = Marshal.PtrToStructure<XMapEvent>(xeventPtr);

						// A watched client reports its own map via StructureNotify (e.xevent == e.window), which survives
						// reparenting; root SubstructureNotify (e.xevent == rootXid) covers windows we aren't watching.
						// The `watched` guard stops a watched direct-child of root firing twice (both paths see it).
						if (e.xevent == e.window || (e.xevent == rootXid && !watched.Contains(e.window)))
							sink(new WindowEventRaw(WindowEventType.Show, e.window, NowMs()));
					}

					break;

				case XEventName.UnmapNotify:
					// Fallback minimize signal; when _NET_WM_STATE is available we use _NET_WM_STATE_HIDDEN instead
					// (which is reparenting-proof and also yields Restore). X11 has no dedicated minimize event.
					if (!useClientList && (mask & WindowEventMask.Minimize) != 0)
					{
						var e = Marshal.PtrToStructure<XUnmapEvent>(xeventPtr);

						// As with Move/Show: the watched client's own StructureNotify (e.xevent == e.window) survives
						// reparenting, with root SubstructureNotify covering unwatched windows and `watched` de-duping.
						// Caveat: a reparenting WM briefly unmaps the client while inserting it into its frame, which
						// looks like a minimize here — an accepted false positive of this fallback (WMs lacking
						// _NET_CLIENT_LIST are typically simple/non-reparenting; the HIDDEN path above is preferred).
						if (e.xevent == e.window || (e.xevent == rootXid && !watched.Contains(e.window)))
							sink(new WindowEventRaw(WindowEventType.Minimize, e.window, NowMs()));
					}

					break;

				case XEventName.ConfigureNotify:
					if ((mask & WindowEventMask.Move) != 0)
					{
						var e = Marshal.PtrToStructure<XConfigureEvent>(xeventPtr);

						// A watched client reports its own moves/resizes via StructureNotify (e.xevent == e.window) — the
						// only path that survives reparenting, since the real ConfigureNotify goes to the WM frame and
						// root never sees it (per ICCCM the WM sends the client a synthetic root-relative ConfigureNotify,
						// which StructureNotify receives). Root SubstructureNotify covers windows we aren't watching, and
						// `watched` stops a watched direct-child of root double-firing. Screen bounds are NOT taken from
						// the event (frame-relative for real events) — the manager resolves them via WindowQuery.
						if (e.xevent == e.window || (e.xevent == rootXid && !watched.Contains(e.window)))
							sink(new WindowEventRaw(WindowEventType.Move, e.window, NowMs()));
					}

					break;
			}
		}

		private void HandleProperty(XPropertyEvent ev, WindowEventMask mask, Action<WindowEventRaw> sink)
		{
			var atom = (long)ev.atom;
			var win = (nint)ev.window;
			// PropertyNotify carries an X server timestamp: convert it to the TickCount64 base so these events report
			// when they occurred rather than when we happened to dequeue them (the cross-platform WinEvent contract).
			var timeMs = ServerTimeToTickCount((uint)ev.time);

			if ((long)win == rootXid)
			{
				if (atom == (long)XDisplay.Default._NET_ACTIVE_WINDOW)
				{
					if ((mask & WindowEventMask.Active) != 0)
					{
						// The WM may rewrite _NET_ACTIVE_WINDOW with the same value (e.g. when a window regains
						// focus after a transient popup); only emit when the active window actually changes.
						var active = ReadActiveWindow();

						if (active != 0 && active != lastActive)
						{
							lastActive = active;
							sink(new WindowEventRaw(WindowEventType.Active, active, timeMs));
						}
					}
				}
				else if (useClientList && atom == (long)XDisplay.Default._NET_CLIENT_LIST)
				{
					HandleClientListChange(mask, sink, timeMs);
				}

				return;
			}

			// Per-window property change on a watched managed window.
			if ((mask & WindowEventMask.TitleChange) != 0
					&& (atom == (long)XDisplay.Default._NET_WM_NAME || atom == (long)(uint)XAtom.XA_WM_NAME))
			{
				// Apps set both _NET_WM_NAME and the legacy WM_NAME, and toolkits rewrite the title repeatedly
				// during startup — each is a separate PropertyNotify for the same resolved title. Only surface an
				// actual change so the consumer isn't flooded with identical TitleChange events.
				var title = ReadTitle(win);

				if (!lastTitles.TryGetValue(win, out var prev) || prev != title)
				{
					lastTitles[win] = title;
					sink(new WindowEventRaw(WindowEventType.TitleChange, win, timeMs));
				}
			}

			if ((mask & (WindowEventMask.Minimize | WindowEventMask.Restore)) != 0
					&& atom == (long)XDisplay.Default._NET_WM_STATE)
				HandleWmState(win, mask, sink, timeMs);
		}

		/// <summary>Converts an X server timestamp (PropertyNotify.time — unsigned 32-bit ms since server start, which
		/// wraps) to the WinEvent time contract (Environment.TickCount64 timebase), so PropertyNotify-sourced events
		/// report when they occurred rather than the receipt time. The raw 32-bit value is first extended to 64-bit
		/// (tracking wraps), then shifted by a receipt-latency-corrected offset: the running minimum of
		/// (TickCount64_at_receipt - extended). The minimum strips per-event queue/dispatch latency, approximating the
		/// (near-constant) offset between the server's and the boot clock's epochs. UI-thread-only, like the callers.</summary>
		private long ServerTimeToTickCount(uint serverTime)
		{
			var now = Environment.TickCount64;
			long extended;

			if (!serverTimeInited)
			{
				serverTimeInited = true;
				lastServerTime = serverTime;
				serverTimeHigh = 0;
				extended = serverTime;
				serverTimeOffset = now - extended;   // seeded with this sample's latency; the min below corrects it down
			}
			else
			{
				// A large backward jump in the raw 32-bit value is the ~49-day wrap, not time running backwards.
				if (serverTime < lastServerTime && lastServerTime - serverTime > 0x8000_0000u)
					serverTimeHigh += 0x1_0000_0000L;

				lastServerTime = serverTime;
				extended = serverTimeHigh + serverTime;

				var sample = now - extended;

				if (sample < serverTimeOffset)
					serverTimeOffset = sample;       // running minimum removes receipt latency
			}

			return extended + serverTimeOffset;
		}

		/// <summary>Diffs the new <c>_NET_CLIENT_LIST</c> against the last seen set: added windows fire Create (and
		/// are watched for title/state changes), removed windows fire Close.</summary>
		private void HandleClientListChange(WindowEventMask mask, Action<WindowEventRaw> sink, long timeMs)
		{
			var current = ReadClientList();

			if (current == null)
				return;   // transient read failure — keep the previous set rather than spuriously closing everything

			var currentSet = new HashSet<nint>(current);
			var added = new List<nint>();
			var removed = new List<nint>();

			foreach (var w in current)
				if (!knownClients.Contains(w))
					added.Add(w);

			foreach (var w in knownClients)
				if (!currentSet.Contains(w))
					removed.Add(w);

			if (added.Count > 0)
			{
				WatchWindows(added);

				foreach (var w in added)
					Track(w);
			}

			knownClients.Clear();

			foreach (var w in currentSet)
				_ = knownClients.Add(w);

			foreach (var w in removed)
				Untrack(w);

			if ((mask & WindowEventMask.Create) != 0)
				foreach (var w in added)
					sink(new WindowEventRaw(WindowEventType.Create, w, timeMs));

			if ((mask & WindowEventMask.Close) != 0)
				foreach (var w in removed)
					// Removal from _NET_CLIENT_LIST is the window manager's authoritative "no longer a managed
					// top-level window", so mark it confirmed (the manager then drops it from every Exist/NotExist
					// set without re-checking, avoiding a race where the X window briefly outlives the list change).
					sink(new WindowEventRaw(WindowEventType.Close, w, timeMs) { DestroyConfirmed = true });
		}

		/// <summary>Translates a <c>_NET_WM_STATE</c> change into Minimize/Restore by tracking whether
		/// <c>_NET_WM_STATE_HIDDEN</c> is present (a minimized window keeps its place in <c>_NET_CLIENT_LIST</c>).</summary>
		private void HandleWmState(nint win, WindowEventMask mask, Action<WindowEventRaw> sink, long timeMs)
		{
			var nowHidden = IsHidden(win);
			var wasHidden = hiddenStates.TryGetValue(win, out var h) && h;

			if (nowHidden == wasHidden)
				return;

			hiddenStates[win] = nowHidden;

			if (nowHidden)
			{
				if ((mask & WindowEventMask.Minimize) != 0)
					sink(new WindowEventRaw(WindowEventType.Minimize, win, timeMs));
			}
			else if ((mask & WindowEventMask.Restore) != 0)
			{
				sink(new WindowEventRaw(WindowEventType.Restore, win, timeMs));
			}
		}

		// ---- helpers ------------------------------------------------------------------------

		/// <summary>Reads <c>_NET_CLIENT_LIST</c> once and populates the watched set; decides whether this WM
		/// supports it (else the backend falls back to CreateNotify/DestroyNotify).</summary>
		private void SeedClientList()
		{
			var clients = ReadClientList();

			if (clients == null)
			{
				useClientList = false;   // WM doesn't expose _NET_CLIENT_LIST
				return;
			}

			useClientList = true;
			knownClients.Clear();
			hiddenStates.Clear();

			foreach (var c in clients)
				_ = knownClients.Add(c);

			WatchWindows(knownClients);

			foreach (var c in knownClients)
				Track(c);
		}

		/// <summary>Begins tracking a managed window's title and minimized state (baselines so the initial values
		/// don't fire spurious TitleChange/Minimize events — those appearances are already covered by Create).</summary>
		private void Track(nint w)
		{
			hiddenStates[w] = IsHidden(w);
			lastTitles[w] = ReadTitle(w);
		}

		private void Untrack(nint w)
		{
			_ = hiddenStates.Remove(w);
			_ = lastTitles.Remove(w);
			_ = watched.Remove(w);
		}

		/// <summary>Selects <c>PropertyChange</c> and <c>StructureNotify</c> on each managed window on GDK's connection,
		/// preserving any mask already selected — <c>PropertyChange</c> so <c>_NET_WM_NAME</c>/<c>_NET_WM_STATE</c>
		/// changes reach the global filter (TitleChange, and HIDDEN-based Minimize/Restore), and <c>StructureNotify</c>
		/// so the window's own <c>ConfigureNotify</c>/<c>MapNotify</c>/<c>UnmapNotify</c> (Move/Show/Minimize) are seen
		/// even after a reparenting WM moves it into a frame — at which point those events are delivered to the frame,
		/// not root, so the root <c>SubstructureNotify</c> never sees them (per ICCCM the WM instead sends the client a
		/// synthetic root-relative <c>ConfigureNotify</c>, which <c>StructureNotify</c> receives). Our own GTK windows
		/// appear in <c>_NET_CLIENT_LIST</c> too, and clobbering GDK's mask on them would break the app, so masks are
		/// OR-ed. Wrapped in a GDK error trap so a BadWindow on a window that just vanished is ignored rather than
		/// aborting. Watched windows are recorded so the per-window and root paths can de-dupe (see <see cref="Handle"/>).</summary>
		private void WatchWindows(IEnumerable<nint> windows)
		{
			if (gdkXDisplay == 0 || gdkDisplay == 0)
				return;

			gdk_x11_display_error_trap_push(gdkDisplay);

			try
			{
				var attr = new XWindowAttributes();

				foreach (var w in windows)
				{
					var existing = Xlib.XGetWindowAttributes(gdkXDisplay, (long)w, ref attr) != 0
						? (EventMasks)attr.your_event_mask.ToInt64()
						: EventMasks.NoEvent;
					_ = Xlib.XSelectInput(gdkXDisplay, (long)w, existing | EventMasks.PropertyChange | EventMasks.StructureNotify);
					_ = watched.Add(w);
				}
			}
			finally
			{
				gdk_x11_display_error_trap_pop_ignored(gdkDisplay);   // also XSyncs, absorbing any BadWindow
			}
		}

		private void WatchWindow(nint window) => WatchWindows([window]);

		/// <summary>The current <c>_NET_CLIENT_LIST</c> (managed top-level windows), or null if the WM doesn't
		/// expose the property.</summary>
		private static List<nint> ReadClientList()
		{
			nint prop = 0;

			try
			{
				if (X11Server.TryGetWindowProperty(XDisplay.Default.Handle, XDisplay.Default.Root.ID,
						XDisplay.Default._NET_CLIENT_LIST, 0, new nint(8192), false, (nint)XAtom.AnyPropertyType,
						out var actualType, out _, out var nitems, out _, out prop))
				{
					if (actualType == 0)
						return null;   // property absent → unsupported WM (an empty-but-present list is supported)

					var n = prop != 0 ? nitems.ToInt64() : 0;
					var list = new List<nint>((int)n);

					for (long i = 0; i < n; i++)
					{
						// Format-32 properties are returned as an array of C longs (8 bytes each on LP64), not 4.
						var id = Marshal.ReadInt64(prop, (int)(i * 8));

						if (id != 0)
							list.Add((nint)id);
					}

					return list;
				}

				return null;
			}
			finally
			{
				if (prop != 0)
					_ = Xlib.XFree(prop);
			}
		}

		/// <summary>Whether <paramref name="win"/>'s <c>_NET_WM_STATE</c> currently contains
		/// <c>_NET_WM_STATE_HIDDEN</c> (i.e. it is minimized/iconified).</summary>
		private static bool IsHidden(nint win)
		{
			nint prop = 0;

			try
			{
				if (X11Server.TryGetWindowProperty(XDisplay.Default.Handle, (long)win,
						XDisplay.Default._NET_WM_STATE, 0, new nint(64), false, (nint)XAtom.AnyPropertyType,
						out _, out _, out var nitems, out _, out prop)
					&& prop != 0)
				{
					var hidden = (long)XDisplay.Default._NET_WM_STATE_HIDDEN;
					var n = nitems.ToInt64();

					for (long i = 0; i < n; i++)
						if (Marshal.ReadInt64(prop, (int)(i * 8)) == hidden)
							return true;
				}

				return false;
			}
			finally
			{
				if (prop != 0)
					_ = Xlib.XFree(prop);
			}
		}

		/// <summary>The window's resolved title — <c>_NET_WM_NAME</c> (UTF-8) preferred, legacy <c>WM_NAME</c> as a
		/// fallback — matching how the title is normally read; used only to dedupe TitleChange notifications.</summary>
		private static string ReadTitle(nint win)
			=> ReadTextProperty(win, XDisplay.Default._NET_WM_NAME)
				?? ReadTextProperty(win, (nint)(uint)XAtom.XA_WM_NAME)
				?? "";

		private static string ReadTextProperty(nint win, nint atom)
		{
			nint prop = 0;

			try
			{
				if (X11Server.TryGetWindowProperty(XDisplay.Default.Handle, (long)win, atom,
						0, new nint(1024), false, (nint)XAtom.AnyPropertyType,
						out var actualType, out _, out var nitems, out _, out prop)
					&& actualType != 0 && prop != 0 && nitems.ToInt64() > 0)
				{
					// Xlib appends a trailing NUL to property data, so PtrToStringUTF8 is safe. WM_NAME may be
					// Latin-1 rather than UTF-8, but exact decoding doesn't matter for change-detection.
					return Marshal.PtrToStringUTF8(prop) ?? "";
				}

				return null;
			}
			finally
			{
				if (prop != 0)
					_ = Xlib.XFree(prop);
			}
		}

		private static nint ReadActiveWindow()
		{
			nint prop = 0;

			try
			{
				if (X11Server.TryGetWindowProperty(XDisplay.Default.Handle, XDisplay.Default.Root.ID, XDisplay.Default._NET_ACTIVE_WINDOW,
					0, new nint(1), false, (nint)XAtom.AnyPropertyType, out _, out _, out var nitems, out _, out prop)
					&& nitems.ToInt64() > 0 && prop != 0)
				{
					return new nint(Marshal.ReadInt64(prop));
				}
			}
			catch
			{
			}
			finally
			{
				if (prop != 0)
					_ = Xlib.XFree(prop);
			}

			return 0;
		}

		/// <summary>Receipt-time value for the WinEvent callback's 3rd arg — a 64-bit monotonic ms-since-boot timestamp
		/// (Environment.TickCount64), on the same clock the macOS backend uses and that Windows reconstructs from its
		/// native event time, so all three are comparable. Used for the events whose native X form carries NO server
		/// timestamp: ConfigureNotify/Map/UnmapNotify (Move/Show/Minimize) and the CreateNotify/DestroyNotify fallback.
		/// This is the dequeue time — the best available when there is no server time. PropertyNotify-sourced events
		/// instead report the true event time via <see cref="ServerTimeToTickCount"/>.</summary>
		private static long NowMs() => Environment.TickCount64;
	}
}
#endif
