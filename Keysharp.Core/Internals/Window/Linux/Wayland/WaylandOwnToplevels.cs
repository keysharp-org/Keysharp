using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Positions Keysharp's OWN top-level windows on Wayland. A GTK/Eto client cannot set its own
	/// xdg-toplevel position (Eto's <c>window.Location</c> is a silent no-op on Wayland), so — just
	/// like <c>WinMove</c> does for foreign windows — we drive the active compositor backend (KWin
	/// scripting, the GNOME Shell extension, or Cinnamon eval) to move the window once it has been
	/// mapped.
	///
	/// <para>The tricky part is correlating our just-shown Eto window with the compositor's window
	/// id. We first stamp the window with a unique temporary Wayland app_id and match that exact
	/// value in the compositor's list. If a backend can't observe app_id, we fall back to a strict
	/// unique metadata match (title/size/PID/active) while tracking claimed compositor ids so two
	/// Keysharp windows never resolve to the same one. The resolved id is cached per form, so only
	/// the first Show pays the correlation/polling cost; later Move calls reuse it.</para>
	///
	/// <para>Each move is a compositor round-trip, so moves run on a background thread and are
	/// coalesced per form (latest position wins). A rapid stream of Moves — e.g. a Highlight
	/// tracking a moving target — collapses to the most recent position instead of queuing.</para>
	///
	/// <para>This is best-effort: a brief map-then-move is unavoidable (Wayland maps the window
	/// where the compositor chooses, then we move it), and on compositors that cannot move windows
	/// (foreign-toplevel-only: sway/Hyprland/COSMIC) it degrades to a no-op.</para>
	/// </summary>
	internal static class WaylandOwnToplevels
	{
		private const string NormalAppId = "keysharp";
		private const string CorrelationAppIdPrefix = "keysharp.self.";

		private sealed class FormState
		{
			internal nint FormHandle;
			internal Eto.Forms.Form Form;
			internal nint CompositorHandle;          // resolved compositor window handle; 0 until correlated
			internal string CompositorId = "";       // its id, kept for claim bookkeeping
			internal string Title = "";
			internal int TargetX = WindowInfoBase.Unchanged;
			internal int TargetY = WindowInfoBase.Unchanged;
			internal int MatchW;
			internal int MatchH;
			internal bool Busy;                       // a reconcile pass is running for this form
			internal bool Dirty;                      // ...and a request arrived after it read the desired state
			internal bool RemoveBorder;               // strip the server-side titlebar
			internal bool KeepAbove;                  // assert keep-above (Eto +AlwaysOnTop is a no-op on Wayland)
			internal bool SkipTaskbar;                // hide from taskbar/pager/switcher (GTK's hint is X11-only)
			internal object Opacity;                  // whole-window alpha (null = never asked for)
			internal nint AppliedTo;                  // the compositor window Applied was pushed to
			internal Traits Applied = Traits.None;    // ...and what it was told, so a pass only issues what changed
			internal bool PositionSettled;            // a placement has been verified (or given up on) once; see ApplyPosition
			internal FormWindowState? PendingWindowState; // maximize/minimize/restore to reassert via the backend
			internal Point SurfaceOrigin;             // where the compositor last said this form's surface starts
			internal long SurfaceTick;                // ...and when, since the user can move the window at any time
			internal bool SurfaceKnown;               // false = the compositor could not say
		}

		/// <summary>
		/// What a compositor window has already been told. Compared against the desired state so a pass issues only
		/// what differs, and paired with the handle it was told to: unmapping destroys the xdg-toplevel, so a window
		/// shown again is a different one and everything is pushed afresh with nothing to invalidate by hand.
		/// </summary>
		private readonly record struct Traits(int X, int Y, bool Border, bool Above, bool Taskbar, object Opacity)
		{
			internal static readonly Traits None = new(WindowInfoBase.Unchanged, WindowInfoBase.Unchanged, false, false, false, null);
		}

		private static readonly object sync = new();
		private static readonly Dictionary<nint, FormState> states = new();
		private static readonly HashSet<string> claimedIds = new();
		//The app_id a form is being correlated under, while that is in flight. See CurrentAppId.
		private static readonly Dictionary<nint, string> correlationAppIds = new();

		// A freshly-shown window may not be in the compositor's list instantly; poll briefly.
		private const int CorrelateTimeoutMs = 1000;
		private const int CorrelatePollMs = 20;
		// Allow the frame (which includes server-side decorations) to be larger than the requested
		// client size when matching by size.
		private const int SizeTolerance = 120;

		internal static bool IsSupported => Platform.Desktop.IsWaylandSession && WaylandBackend.Current != null;

		/// <summary>
		/// The app_id a form should be carrying right now: the caller's value, unless a correlation is matching
		/// this window, in which case its unique token wins until it is done.
		/// <para>
		/// The Wayland app_id is single-valued and has two writers - the taskbar icon wants <c>keysharp</c> on
		/// every window, correlation needs something unique on one - and the icon's write is deferred to an
		/// AsyncInvoke whenever the window is still unmapped, which is the normal case at Shown. That deferred
		/// write used to land in the middle of a correlation and erase the token, so the match could never
		/// succeed and every first correlation paid the full poll timeout before falling back to guessing by
		/// title and size. Routing both writers through here is what keeps a single owner of the value.
		/// </para>
		/// </summary>
		internal static string CurrentAppId(Eto.Forms.Form form, string baseAppId)
		{
			var formHandle = form is { IsDisposed: false } ? form.Handle : 0;

			if (formHandle == 0)
				return baseAppId;

			lock (sync)
				return correlationAppIds.TryGetValue(formHandle, out var token) ? token : baseAppId;
		}

		/// <summary>
		/// Establishes the compositor backend and its command channel on a background thread, so the first
		/// <see cref="Position"/> doesn't pay that setup while the window is already on screen at the wrong
		/// position. Fire-and-forget and idempotent: the backend caches its probe and the channel is resident,
		/// so a later query reuses whatever this warmed; if it fails, the normal (re-probing) paths are
		/// unaffected. No-op off Wayland.
		/// </summary>
		internal static void Prewarm()
		{
			if (!Platform.Desktop.IsWaylandSession)
				return;

			_ = Task.Run(() =>
			{
				// Probe() resolves the backend; the window list is the cheapest op that builds the command
				// channel, and is exactly what Correlate issues first.
				try { _ = WaylandBackend.Current?.TryListWindows(true, out _); }
				catch { }
			});
		}

		/// <summary>
		/// Request that our own window (identified by <paramref name="form"/>) be
		/// moved so its top-left sits at screen (<paramref name="x"/>, <paramref name="y"/>). Either
		/// coordinate may be <see cref="WindowInfoBase.Unchanged"/> to leave it untouched.
		/// <paramref name="title"/> and the match size are used only to correlate the window the
		/// first time. Returns immediately; the move runs asynchronously. No-op off Wayland or when
		/// there is no capable backend.
		/// </summary>
		internal static void Position(Eto.Forms.Form form, string title, int x, int y, int matchW, int matchH, bool removeBorder = false, bool keepAbove = false, bool skipTaskbar = false)
		{
			var formHandle = form?.Handle ?? 0;

			if (formHandle == 0 || !IsSupported)
				return;

			lock (sync)
			{
				// A window with nothing to assert has nothing to reconcile. One that already has desired state
				// still does, even on a plain Show: unmapping dropped everything the compositor had been told.
				if (!states.ContainsKey(formHandle) && x == WindowInfoBase.Unchanged && y == WindowInfoBase.Unchanged
						&& !removeBorder && !keepAbove && !skipTaskbar)
					return;

				var state = Track(formHandle, form, title, matchW, matchH);

				if (x != WindowInfoBase.Unchanged) state.TargetX = x;
				if (y != WindowInfoBase.Unchanged) state.TargetY = y;
				if (removeBorder) state.RemoveBorder = true;
				if (keepAbove) state.KeepAbove = true;
				if (skipTaskbar) state.SkipTaskbar = true;

				Reconcile(state);
			}
		}

		/// <summary>
		/// The inverse of <see cref="TryGetCompositorHandle"/>: the handle one of OUR OWN windows is known by in
		/// Eto/GTK, given the handle the compositor knows it by.
		/// <para>
		/// Every window of ours ends up with BOTH, because a client cannot place itself on Wayland and so has to
		/// be correlated to its compositor window to be moved at all. Only the compositor's handle reaches the
		/// window enumeration, though, and a script never sees that one - <c>Gui.Hwnd</c>, and therefore every
		/// comparison a script makes against it, is the Eto handle. So the enumeration has to hand back the Eto
		/// handle for a window of ours, which is what this resolves.
		/// </para>
		/// <para>
		/// Reads only what correlation has already cached, so it costs no IPC and cannot itself correlate: a
		/// window that has never been shown simply is not found, and the caller leaves the compositor's handle
		/// alone - exactly what it did before this existed.
		/// </para>
		/// </summary>
		internal static bool TryGetFormHandle(nint compositorHandle, out nint formHandle)
		{
			formHandle = 0;

			if (compositorHandle == 0 || !IsSupported)
				return false;

			lock (sync)
			{
				foreach (var state in states.Values)
				{
					if (state.CompositorHandle != compositorHandle)
						continue;

					//A form whose window is gone (disposed, or never realized) would otherwise resolve to a
					//handle that answers nothing, which is worse than reporting the compositor's own.
					if (state.Form is not { IsDisposed: false } || state.FormHandle == 0)
						return false;

					formHandle = state.FormHandle;
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Resolve one of our OWN top-level windows to the active compositor's window handle, correlating once
		/// and caching the result. Lets the synchronous
		/// window verbs (WinMove / WinGetPos against a Gui object) take the same compositor path foreign windows
		/// use, instead of Eto's self-position/-query which is a no-op on Wayland. Returns false off Wayland,
		/// without a capable backend, or when the window can't be correlated (e.g. foreign-toplevel-only
		/// compositors).
		/// </summary>
		//The window can be dragged, tiled or maximized at any moment and a Wayland client is never told, so the
		//origin is only ever a snapshot; one frame is as current as anything on screen can be. A form nothing can
		//answer for is re-asked far less often, since that only changes when it is correlated.
		private const long surfaceOriginValidMs = 16;
		private const long surfaceOriginMissMs = 500;

		/// <summary>
		/// Where the form's surface starts, which is what its own toolkit coordinates are relative to: a Wayland
		/// client is never told where it sits, so only the compositor can say.
		/// <para>
		/// Reads the correlation already cached and never starts one. Correlation pumps the message loop, and this
		/// is reached from hit tests and from pointer motion, where dispatching an event mid-read would re-enter.
		/// An uncorrelated form is simply not found, leaving the caller uncorrected.
		/// </para>
		/// </summary>
		internal static bool TryGetSurfaceOrigin(Eto.Forms.Form form, out Point origin)
		{
			origin = default;
			//Asking a disposed form for its handle throws rather than answering.
			var formHandle = form is { IsDisposed: false } ? form.Handle : 0;

			if (formHandle == 0 || !IsSupported)
				return false;

			nint compositorHandle;

			lock (sync)
			{
				if (!states.TryGetValue(formHandle, out var cached))
					return false;

				if (Environment.TickCount64 - cached.SurfaceTick < (cached.SurfaceKnown ? surfaceOriginValidMs : surfaceOriginMissMs))
				{
					origin = cached.SurfaceOrigin;
					return cached.SurfaceKnown;
				}

				compositorHandle = cached.CompositorHandle;
			}

			//Outside the lock: this is IPC, and every other holder of it is on the placement path.
			var backend = WaylandBackend.Current;
			var known = false;

			if (compositorHandle != 0 && backend != null && backend.TryGetWindow(compositorHandle, out var info)
					&& info.SurfaceGeometry.Width > 0 && info.SurfaceGeometry.Height > 0)
			{
				origin = new Point(info.SurfaceGeometry.X, info.SurfaceGeometry.Y);
				known = true;
			}

			lock (sync)
			{
				if (states.TryGetValue(formHandle, out var state))
				{
					//Stamped after the round trip, not before: a slow answer would otherwise be born expired and
					//every control of the same walk would repeat it. A failure is cached for the same reason.
					state.SurfaceOrigin = origin;
					state.SurfaceTick = Environment.TickCount64;
					state.SurfaceKnown = known;
				}
			}

			return known;
		}

		internal static bool TryGetCompositorHandle(Eto.Forms.Form form, string title, int matchW, int matchH, out nint compositorHandle)
		{
			compositorHandle = 0;
			var formHandle = form?.Handle ?? 0;

			if (formHandle == 0 || !IsSupported)
				return false;

			var backend = WaylandBackend.Current;

			if (backend == null)
				return false;

			FormState state;

			lock (sync)
				state = Track(formHandle, form, title, matchW, matchH);

			if (StillLive(backend, state))
			{
				lock (sync)
					compositorHandle = state.CompositorHandle;

				return true;
			}

			if (!Correlate(backend, state))
				return false;

			lock (sync)
				compositorHandle = state.CompositorHandle;

			return compositorHandle != 0;
		}

		/// <summary>
		/// Reassert a window state (maximize / minimize / restore) on our own window through the compositor
		/// backend. Eto's <c>WindowState</c> setter (gtk_window_(un)maximize / iconify, i.e. an xdg-toplevel
		/// request) is the primary path and works on most compositors, but some drop a client's request, so
		/// driving the backend too makes it stick. Best-effort and asynchronous; no-op off Wayland or without
		/// a capable backend. <paramref name="title"/> and the match size correlate the window the first time,
		/// exactly like <see cref="Position"/>.
		/// </summary>
		internal static void SetWindowState(Eto.Forms.Form form, string title, int matchW, int matchH, FormWindowState windowState)
		{
			var formHandle = form?.Handle ?? 0;

			if (formHandle == 0 || !IsSupported)
				return;

			lock (sync)
			{
				var state = Track(formHandle, form, title, matchW, matchH);
				state.PendingWindowState = windowState;
				Reconcile(state);
			}
		}

		/// <summary>
		/// Set whole-window opacity on one of OUR OWN windows (AHK alpha: 0 transparent, 255 opaque, "Off" opaque).
		/// A Wayland client cannot make its own surface translucent, so the request goes through the compositor
		/// backend. Returns false only when the compositor cannot do this at all, which is what makes
		/// <c>WinSetTransparent</c> raise its OSError there.
		/// </summary>
		internal static bool SetTransparency(Eto.Forms.Form form, string title, int matchW, int matchH, object alpha)
		{
			var formHandle = form?.Handle ?? 0;
			var backend = WaylandBackend.Current;

			if (formHandle == 0 || !IsSupported || backend?.SupportsTransparency != true)
				return false;

			FormState state;

			lock (sync)
			{
				state = Track(formHandle, form, title, matchW, matchH);
				state.Opacity = alpha;
			}

			// Issue it here when the compositor already knows the window, so the caller gets the real result and a
			// WinGetTransparent straight after reads back what was just set rather than racing a background pass.
			if (StillLive(backend, state))
			{
				nint live;

				lock (sync)
					live = state.CompositorHandle;

				return backend.TrySetTransparency(live, alpha);
			}

			// Not mapped yet, so there is nothing to correlate to: the desired state is recorded and the Show that
			// maps the window reconciles it. Setting transparency before Show is ordinary usage.
			lock (sync)
				Reconcile(state);

			return true;
		}

		/// <summary>
		/// Notify that some other path (e.g. <c>WinMove</c> via the neutral <see cref="WindowInfo"/>) just moved a
		/// compositor window. If it's one of ours that is still being placed, fold the new position into our
		/// target so a pending background placement converges to it rather than reverting to the original Show
		/// position. Harmless for windows we aren't tracking (foreign windows, or ones already settled).
		/// </summary>
		internal static void NotifyExternalMove(nint compositorHandle, int x, int y)
		{
			if (compositorHandle == 0 || (x == WindowInfoBase.Unchanged && y == WindowInfoBase.Unchanged))
				return;

			lock (sync)
			{
				foreach (var state in states.Values)
				{
					if (state.CompositorHandle != compositorHandle)
						continue;

					if (x != WindowInfoBase.Unchanged) state.TargetX = x;
					if (y != WindowInfoBase.Unchanged) state.TargetY = y;
					break;
				}
			}
		}

		/// <summary>Drops cached correlation for a destroyed form so a recycled native handle can't
		/// inherit a stale compositor window.</summary>
		internal static void Forget(nint formHandle)
		{
			lock (sync)
			{
				if (states.Remove(formHandle, out var state) && state.CompositorId.Length > 0)
					_ = claimedIds.Remove(state.CompositorId);
			}
		}

		/// <summary>
		/// Schedules a reconcile pass. Caller must hold <see cref="sync"/>.
		/// <para>
		/// Marking dirty before the check is what makes this lossless: a request landing while a pass is running
		/// either sets the flag before that pass tests it (so the pass loops) or after it cleared <c>Busy</c> (so
		/// this starts a new one). There is no window in which a request is seen by neither.
		/// </para>
		/// </summary>
		private static void Reconcile(FormState state)
		{
			state.Dirty = true;

			if (state.Busy)
				return;

			state.Busy = true;
			_ = Task.Run(() => Worker(state));
		}

		/// <summary>
		/// Whether the cached correlation still names a window the compositor knows, dropping it if it doesn't so the
		/// caller re-correlates. Hiding a window unmaps it and destroys its xdg-toplevel, so the Show that follows
		/// produces a NEW compositor window: without this, every trait reasserted after a re-Show would be applied to
		/// a window that is gone.
		/// </summary>
		private static bool StillLive(IWaylandBackend backend, FormState state)
		{
			nint cached;

			lock (sync)
				cached = state.CompositorHandle;

			if (cached == 0)
				return false;

			if (backend.TryGetWindow(cached, out _))
				return true;

			lock (sync)
			{
				if (state.CompositorHandle == cached)
				{
					if (state.CompositorId.Length > 0)
						_ = claimedIds.Remove(state.CompositorId);

					state.CompositorHandle = 0;
					state.CompositorId = "";
				}
			}

			return false;
		}

		/// <summary>The form's desired state, refreshed with the correlation metadata every request carries.
		/// Caller must hold <see cref="sync"/>.</summary>
		private static FormState Track(nint formHandle, Eto.Forms.Form form, string title, int matchW, int matchH)
		{
			if (!states.TryGetValue(formHandle, out var state))
				states[formHandle] = state = new FormState { FormHandle = formHandle };

			// Always adopt the caller's form, never just the first one seen: GTK can recycle a native handle, so a
			// state that outlived its window (a teardown path that never reached Forget) would otherwise keep
			// stamping the correlation app_id on the DEAD form and hand TryGetFormHandle a handle that answers
			// nothing. The live form is the one this call is about.
			if (form != null)
				state.Form = form;

			state.Title = title ?? "";
			if (matchW > 0) state.MatchW = matchW;
			if (matchH > 0) state.MatchH = matchH;
			return state;
		}

		// Drive the form's desired state into the compositor until it is up to date and nothing new has arrived.
		private static void Worker(FormState state)
		{
			try
			{
				while (true)
				{
					lock (sync)
						state.Dirty = false;

					var backend = WaylandBackend.Current;

					// Failing to correlate normally just means the window is not mapped yet. The desired state stays
					// recorded and the Show that maps it reconciles again - the only moment this can succeed.
					if (backend != null && (StillLive(backend, state) || Correlate(backend, state)))
						Push(backend, state);

					lock (sync)
					{
						if (!state.Dirty)
						{
							state.Busy = false;
							return;
						}
					}
				}
			}
			catch
			{
				lock (sync) state.Busy = false;
			}
		}

		// Tell the compositor what it does not already know about this window. Skipping what matches Applied keeps a
		// stream of Moves from re-issuing traits, while a window that was unmapped and shown again has a new handle,
		// so it is told everything afresh - the flags cannot go stale against a live window.
		private static void Push(IWaylandBackend backend, FormState state)
		{
			nint handle;
			FormWindowState? ws;
			Traits want, have;

			lock (sync)
			{
				handle = state.CompositorHandle;
				ws = state.PendingWindowState;
				state.PendingWindowState = null;   // a one-shot reassert, not part of the desired state: re-applying
				want = new Traits(state.TargetX, state.TargetY, state.RemoveBorder,   // it would undo a later user unmaximize
								  state.KeepAbove, state.SkipTaskbar, state.Opacity);
				have = state.AppliedTo == handle ? state.Applied : Traits.None;
			}

			// Reassert maximize/minimize/restore via the backend (Eto already issued the GTK request; this makes it
			// stick on compositors that drop a client's xdg-toplevel state request).
			if (ws.HasValue)
				_ = backend.TrySetWindowState(handle, ws.Value);

			if ((want.X != WindowInfoBase.Unchanged || want.Y != WindowInfoBase.Unchanged)
					&& (want.X != have.X || want.Y != have.Y))
			{
				var rect = new Rectangle(want.X, want.Y, WindowInfoBase.Unchanged, WindowInfoBase.Unchanged);
				_ = backend.TryMoveResizeWindow(handle, rect, true, false);
			}

			// The traits GTK cannot express on Wayland go AFTER the move: a freshly mapped window may not be fully
			// decorated at correlation time, so removing the border before it is drawn doesn't stick, while doing it
			// once the move round-trip has settled does.
			if (want.Border && !have.Border)
				_ = backend.TrySetNoBorder(handle, true);

			// GTK's skip-taskbar hint only exists on X11, so a +ToolWindow overlay is listed in the
			// taskbar/pager/switcher like a normal window here until the compositor is told otherwise.
			if (want.Taskbar && !have.Taskbar)
				_ = backend.TrySetSkipTaskbar(handle, true);

			// Eto's +AlwaysOnTop (gtk keep-above) is a no-op on Wayland — a client can't keep itself above.
			if (want.Above && !have.Above)
				_ = backend.TrySetAlwaysOnTop(handle, true);

			// Nor can a client make its own surface translucent.
			if (want.Opacity != null && !Equals(want.Opacity, have.Opacity))
				_ = backend.TrySetTransparency(handle, want.Opacity);

			// Recorded even when a call failed: each is best-effort and applied at most once per compositor window,
			// so a compositor that refuses one can't make every later pass retry it.
			lock (sync)
			{
				state.AppliedTo = handle;
				state.Applied = want;
			}
		}

		// Locate our window in the compositor's list and claim it. Polls because a just-mapped
		// window may not be reported on the first list.
		private static bool Correlate(IWaylandBackend backend, FormState state)
		{
			var pid = (long)Environment.ProcessId;
			string title, token;
			Eto.Forms.Form form;
			int mw, mh;

			lock (sync)
			{
				title = state.Title;
				mw = state.MatchW;
				mh = state.MatchH;
				form = state.Form;
				token = $"{CorrelationAppIdPrefix}{pid}.{state.FormHandle.ToInt64():x}.{Guid.NewGuid():N}";
				//Claim the app_id for the whole attempt, so the icon's deferred write re-applies the token
				//rather than erasing it. See CurrentAppId.
				correlationAppIds[state.FormHandle] = token;
			}

			var deadline = Environment.TickCount64 + CorrelateTimeoutMs;
			var stamped = false;

			try
			{
				while (true)
				{
					string existingId;

					lock (sync)
					{
						if (state.CompositorHandle != 0)
							return true;

						existingId = state.CompositorId;
					}

					// Retry the stamp only until it takes (the window must be realized for the app_id to stick);
					// once stamped, don't re-invoke the UI-thread setter every 20ms poll.
					if (!stamped)
						stamped = TrySetAppIdOnUiThread(form, token);

					if (backend.TryListWindows(true, out var windows) && windows != null
						&& Pick(windows, pid, title, mw, mh, existingId, stamped ? token : "") is WaylandWindowInfo pick)
					{
						Claim(state, pick);
						return true;
					}

					if (Environment.TickCount64 >= deadline)
					{
						// If the client accepted the temporary app_id but this backend doesn't expose it, allow one
						// final conservative metadata match before giving up. During the normal polling window, an
						// accepted app_id disables fallback so we don't race app_id propagation and pick the wrong
						// same-title/same-size window.
						if (stamped && backend.TryListWindows(true, out windows) && windows != null
							&& Pick(windows, pid, title, mw, mh, existingId, "") is { } fallback)
						{
							Claim(state, fallback);
							return true;
						}

						return false;
					}

					//On the UI thread this poll must PUMP rather than sleep: that thread IS the GTK main loop,
					//and a freshly shown window only gets its compositor toplevel once the loop runs. Sleeping
					//here keeps the very window being waited for from ever appearing, so a WinGetPos/WinMove
					//issued right after Show correlates against a window that is not there yet and fails.
					//Off the UI thread - the worker a request starts - a plain sleep is right.
					if (Script.TheScript?.IsOnMainThread == true)
					{
						var resume = Environment.TickCount64 + CorrelatePollMs;
						Keysharp.Internals.Flow.WaitWithMessagePump(() => Environment.TickCount64 < resume);
					}
					else
						Thread.Sleep(CorrelatePollMs);
				}
			}
			finally
			{
				lock (sync)
					_ = correlationAppIds.Remove(state.FormHandle);

				if (stamped)
					_ = TrySetAppIdOnUiThread(form, NormalAppId);
			}
		}

		private static void Claim(FormState state, WaylandWindowInfo pick)
		{
			lock (sync)
			{
				if (state.CompositorId.Length > 0 && state.CompositorId != pick.CompositorId)
					_ = claimedIds.Remove(state.CompositorId);

				state.CompositorHandle = pick.Handle;
				state.CompositorId = pick.CompositorId;
				_ = claimedIds.Add(pick.CompositorId);
			}
		}

		private static bool TrySetAppIdOnUiThread(Eto.Forms.Form form, string appId)
		{
			if (form == null || string.IsNullOrEmpty(appId))
				return false;

			try
			{
				var app = Eto.Forms.Application.Instance;

				if (app == null || app.IsUIThread)
					return Eto.Forms.EtoExtensions.SetWaylandAppId(form, appId);

				return app.Invoke(() => Eto.Forms.EtoExtensions.SetWaylandAppId(form, appId));
			}
			catch
			{
				return false;
			}
		}

		private static WaylandWindowInfo Pick(IReadOnlyList<WaylandWindowInfo> windows, long pid, string title, int matchW, int matchH, string existingId, string appIdToken)
		{
			List<WaylandWindowInfo> candidates;

			lock (sync)
				candidates = windows.Where(w => w != null
					&& !string.IsNullOrEmpty(w.CompositorId)
					//A window another process owns can never be one of ours. Only excluded when the compositor
					//actually reports an owner: it answers -1 for a Wayland client on backends that have not
					//been taught to fall back to the client pid.
					&& (w.PID <= 0 || w.PID == pid)
					&& (w.CompositorId == existingId || !claimedIds.Contains(w.CompositorId))).ToList();

			if (candidates.Count == 0)
				return null;

			WaylandWindowInfo Unique(Func<WaylandWindowInfo, bool> predicate)
			{
				WaylandWindowInfo match = null;

				foreach (var candidate in candidates)
				{
					if (!predicate(candidate))
						continue;

					if (match != null)
						return null;

					match = candidate;
				}

				return match;
			}

			if (!string.IsNullOrEmpty(appIdToken))
				return Unique(w => string.Equals(w.AppId, appIdToken, StringComparison.Ordinal));

			bool TitleMatch(WaylandWindowInfo w) =>
				!string.IsNullOrEmpty(title) && string.Equals(w.Title, title, StringComparison.Ordinal);

			bool SizeMatch(WaylandWindowInfo w) =>
				matchW > 0 && matchH > 0
				&& Math.Abs(w.FrameGeometry.Width - matchW) <= SizeTolerance
				&& Math.Abs(w.FrameGeometry.Height - matchH) <= SizeTolerance;

			return Unique(w => w.PID == pid && TitleMatch(w) && SizeMatch(w))
				?? Unique(w => TitleMatch(w) && SizeMatch(w))
				?? Unique(w => w.PID == pid && TitleMatch(w))
				?? Unique(w => w.PID == pid && SizeMatch(w))
				?? Unique(w => w.Active && TitleMatch(w))
				?? Unique(w => w.Active && SizeMatch(w))
				?? Unique(TitleMatch)
				?? Unique(SizeMatch);
		}
	}
}
#endif
