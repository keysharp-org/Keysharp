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
			internal bool Busy;                       // a worker is currently servicing this form
			internal long Generation;                 // bumped by every request, so a worker can tell one arrived while it correlated
			internal bool RemoveBorder;               // strip the server-side titlebar once correlated
			internal bool BorderRemoved;              // ...and we have done so
			internal bool KeepAbove;                  // assert keep-above (Eto +AlwaysOnTop is a no-op on Wayland)
			internal bool KeptAbove;                  // ...and we have done so
			internal bool SkipTaskbar;                // hide from taskbar/pager/switcher (GTK's hint is X11-only)
			internal bool TaskbarSkipped;             // ...and we have done so
			internal object Opacity;                  // requested whole-window alpha (null = never set)
			internal bool OpacityApplied;             // ...and we have done so
			internal bool PositionSettled;            // a placement has been verified (or given up on) once; see ApplyPosition
			internal FormWindowState? PendingWindowState; // maximize/minimize/restore to reassert via the backend
			internal Point SurfaceOrigin;             // where the compositor last said this form's surface starts
			internal long SurfaceTick;                // ...and when, since the user can move the window at any time
			internal bool SurfaceKnown;               // false = the compositor could not say
		}

		private static readonly object sync = new();
		private static readonly Dictionary<nint, FormState> states = new();
		private static readonly HashSet<string> claimedIds = new();

		// A freshly-shown window may not be in the compositor's list instantly; poll briefly.
		private const int CorrelateTimeoutMs = 1000;
		private const int CorrelatePollMs = 20;
		// Allow the frame (which includes server-side decorations) to be larger than the requested
		// client size when matching by size.
		private const int SizeTolerance = 120;

		internal static bool IsSupported => Platform.Desktop.IsWaylandSession && WaylandBackend.Current != null;

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

			// Nothing to do without a position to apply, a border to strip, a keep-above or a taskbar entry to hide.
			if (x == WindowInfoBase.Unchanged && y == WindowInfoBase.Unchanged && !removeBorder && !keepAbove && !skipTaskbar)
				return;

			var startWorker = false;
			FormState state;

			lock (sync)
			{
				state = GetOrCreateState(formHandle, form);

				if (x != WindowInfoBase.Unchanged) state.TargetX = x;
				if (y != WindowInfoBase.Unchanged) state.TargetY = y;
				state.Title = title ?? "";
				if (matchW > 0) state.MatchW = matchW;
				if (matchH > 0) state.MatchH = matchH;
				if (removeBorder) state.RemoveBorder = true;
				if (keepAbove) state.KeepAbove = true;
				if (skipTaskbar) state.SkipTaskbar = true;

				if (!state.Busy)
					startWorker = state.Busy = true;
			}

			if (startWorker)
				_ = Task.Run(() => Worker(state));
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
			nint cachedHandle;

			lock (sync)
			{
				state = GetOrCreateState(formHandle, form);

				cachedHandle = state.CompositorHandle;
			}

			if (cachedHandle != 0)
			{
				if (backend.TryGetWindow(cachedHandle, out _))
				{
					compositorHandle = cachedHandle;
					return true;
				}

				lock (sync)
				{
					if (state.CompositorHandle == cachedHandle)
					{
						if (state.CompositorId.Length > 0)
							_ = claimedIds.Remove(state.CompositorId);

						state.CompositorHandle = 0;
						state.CompositorId = "";
					}
				}
			}

			lock (sync)
			{
				state.Title = title ?? "";
				if (matchW > 0) state.MatchW = matchW;
				if (matchH > 0) state.MatchH = matchH;
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

			var startWorker = false;
			FormState state;

			lock (sync)
			{
				state = GetOrCreateState(formHandle, form);

				state.Title = title ?? "";
				if (matchW > 0) state.MatchW = matchW;
				if (matchH > 0) state.MatchH = matchH;
				state.PendingWindowState = windowState;

				if (!state.Busy)
					startWorker = state.Busy = true;
			}

			if (startWorker)
				_ = Task.Run(() => Worker(state));
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

		/// <summary>Whether any requested compositor-side trait is still unapplied. Caller must hold <see cref="sync"/>.</summary>
		private static bool PendingTraits(FormState state)
			=> (state.RemoveBorder && !state.BorderRemoved)
			   || (state.KeepAbove && !state.KeptAbove)
			   || (state.SkipTaskbar && !state.TaskbarSkipped);

		private static FormState GetOrCreateState(nint formHandle, Eto.Forms.Form form)
		{
			if (!states.TryGetValue(formHandle, out var state))
				states[formHandle] = state = new FormState { FormHandle = formHandle };

			state.Form ??= form;
			return state;
		}

		private static void Worker(FormState state)
		{
			try
			{
				var backend = WaylandBackend.Current;

				if (backend == null || (state.CompositorHandle == 0 && !Correlate(backend, state)))
				{
					lock (sync) state.Busy = false;
					return;
				}

				while (true)
				{
					int tx, ty;
					FormWindowState? ws;

					lock (sync)
					{
						tx = state.TargetX;
						ty = state.TargetY;
						ws = state.PendingWindowState;
						state.PendingWindowState = null;
					}

					// Reassert maximize/minimize/restore via the backend (Eto already issued the GTK request;
					// this makes it stick on compositors that drop a client's xdg-toplevel state request).
					if (ws.HasValue)
						_ = backend.TrySetWindowState(state.CompositorHandle, ws.Value);

					if (tx != WindowInfoBase.Unchanged || ty != WindowInfoBase.Unchanged)
					{
						var rect = new Rectangle(tx, ty, WindowInfoBase.Unchanged, WindowInfoBase.Unchanged);
						_ = backend.TryMoveResizeWindow(state.CompositorHandle, rect, true, false);
					}

					// Assert the traits GTK cannot express on Wayland, AFTER the first move: a freshly mapped window
					// may not be fully decorated by the compositor at correlation time, so removing the border
					// before it is drawn doesn't stick, while doing it once the move round-trip has settled does.
					// Each trait is guarded by its own "requested but not yet applied" pair rather than a
					// once-per-worker flag: a worker started by an earlier flagless SetWindowState/Move would
					// otherwise latch that flag and silently drop traits set by the Show that follows it.
					bool removeBorder, keepAbove, skipTaskbar;
					lock (sync)
					{
						removeBorder = state.RemoveBorder && !state.BorderRemoved;
						keepAbove = state.KeepAbove && !state.KeptAbove;
						skipTaskbar = state.SkipTaskbar && !state.TaskbarSkipped;
					}

					if (removeBorder)
					{
						_ = backend.TrySetNoBorder(state.CompositorHandle, true);
						lock (sync) state.BorderRemoved = true;   // best-effort: applied once either way, so a
					}                                             // failure can't spin this loop

					// GTK's skip-taskbar hint only exists on X11, so a +ToolWindow overlay is listed in the
					// taskbar/pager/switcher like a normal window here until the compositor is told otherwise.
					if (skipTaskbar)
					{
						_ = backend.TrySetSkipTaskbar(state.CompositorHandle, true);
						lock (sync) state.TaskbarSkipped = true;
					}

					// Eto's +AlwaysOnTop (gtk keep-above) is a no-op on Wayland — a client can't keep
					// itself above — so assert it through the compositor.
					if (keepAbove)
					{
						_ = backend.TrySetAlwaysOnTop(state.CompositorHandle, true);
						lock (sync) state.KeptAbove = true;
					}

					lock (sync)
					{
						// Done only when no newer target, window-state request or trait arrived while we were
						// working; otherwise the lock guarantees a fresh Position()/SetWindowState() either sees
						// Busy and lets us loop, or (once we clear Busy here) starts its own worker. No update is lost.
						if (tx == state.TargetX && ty == state.TargetY && !state.PendingWindowState.HasValue
								&& !PendingTraits(state))
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

					Thread.Sleep(CorrelatePollMs);
				}
			}
			finally
			{
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
