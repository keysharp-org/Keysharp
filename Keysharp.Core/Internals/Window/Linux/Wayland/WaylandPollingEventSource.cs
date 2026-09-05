#if LINUX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Generic, compositor-agnostic window-event source built on top of <see cref="IWaylandBackend.TryListWindows"/>.
	/// It polls one complete backend snapshot on a background thread and diffs
	/// successive snapshots to synthesize create/close/title/minimize/restore/move/active events.
	/// <para>
	/// This is the fallback for compositors that can enumerate windows but offer no push channel (e.g. Cinnamon), and
	/// also the graceful degradation path for KWin/GNOME when their native push setup fails (extension missing,
	/// scripting unavailable). Polling adds up to one interval of latency and a steady IPC trickle, so the
	/// push backends prefer their native channels and only fall back to this. Interval is configurable via
	/// <c>KEYSHARP_WAYLAND_POLL_MS</c> (default 250ms, clamped to [50, 5000]).
	/// </para>
	/// </summary>
	internal sealed class WaylandPollingEventSource : IDisposable
	{
		private readonly IWaylandBackend backend;
		private readonly Action<WaylandWindowEvent> sink;
		private readonly Thread thread;
		private readonly int intervalMs;
		private readonly WaylandWindowSnapshotTracker tracker = new();
		private volatile bool stopped;

		internal WaylandPollingEventSource(IWaylandBackend backend, Action<WaylandWindowEvent> sink)
		{
			this.backend = backend;
			this.sink = sink;
			intervalMs = ReadInterval();
			thread = new Thread(Loop) { IsBackground = true, Name = "WinEvent-WaylandPoll" };
			thread.Start();
		}

		private static int ReadInterval()
		{
			var raw = Environment.GetEnvironmentVariable("KEYSHARP_WAYLAND_POLL_MS");

			if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var ms))
				return Math.Clamp(ms, 50, 5000);

			return 250;
		}

		public void Dispose()
		{
			stopped = true;

			try { thread?.Join(1000); }
			catch { }
		}

		private void Loop()
		{
			while (!stopped)
			{
				try
				{
					Poll();
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"WinEvent Wayland poll error ({backend?.Name}): {ex.Message}");
				}

				// Sleep in small slices so Dispose() is responsive even with a long interval.
				for (var slept = 0; slept < intervalMs && !stopped; slept += 50)
					Thread.Sleep(Math.Min(50, intervalMs - slept));
			}
		}

		private void Poll()
		{
			if (!backend.TryListWindows(true, out var windows) || windows == null)
				return;

			tracker.Update(windows, Emit);
		}

		internal static nint ActiveHandle(IReadOnlyList<WaylandWindowInfo> windows)
			=> WaylandWindowSnapshotTracker.TryGetActiveHandle(windows, out var handle) ? handle : 0;

		private void Emit(WaylandWindowEvent windowEvent)
		{
			if (stopped)
				return;

			try { sink(windowEvent); }
			catch { }
		}
	}

	internal sealed class WaylandWindowSnapshotTracker
	{
		private const WaylandWindowFields TrackedFields = WaylandWindowFields.Title
			| WaylandWindowFields.Minimized | WaylandWindowFields.Frame;

		private Dictionary<nint, Snapshot> previous = [];
		private nint previousActive;
		private bool seeded;

		private readonly record struct Snapshot(string Title, bool Minimized, Rectangle Frame,
			WaylandWindowFields KnownFields)
		{
			internal bool Knows(WaylandWindowFields field) => (KnownFields & field) != 0;

			internal static Snapshot From(WaylandWindowInfo window)
				=> new(window.Title ?? string.Empty, window.Minimized, window.FrameGeometry,
					window.KnownFields & TrackedFields);

			internal static Snapshot Merge(Snapshot prior, Snapshot current)
				=> new(
					current.Knows(WaylandWindowFields.Title) ? current.Title : prior.Title,
					current.Knows(WaylandWindowFields.Minimized) ? current.Minimized : prior.Minimized,
					current.Knows(WaylandWindowFields.Frame) ? current.Frame : prior.Frame,
					current.KnownFields | prior.KnownFields);
		}

		internal void Update(IReadOnlyList<WaylandWindowInfo> windows, Action<WaylandWindowEvent> emit)
		{
			ArgumentNullException.ThrowIfNull(windows);
			ArgumentNullException.ThrowIfNull(emit);

			var current = new Dictionary<nint, Snapshot>(windows.Count);
			var activeKnown = TryGetActiveHandle(windows, out var activeHandle);

			foreach (var window in windows)
			{
				if (window == null || window.Handle == 0 || current.ContainsKey(window.Handle))
					continue;

				var snapshot = Snapshot.From(window);

				if (seeded)
				{
					if (!previous.TryGetValue(window.Handle, out var prior))
						emit(new WaylandWindowEvent(WaylandWindowEventKind.Created, window.Handle));
					else
					{
						EmitChanges(window.Handle, prior, snapshot, emit);
						snapshot = Snapshot.Merge(prior, snapshot);
					}
				}

				current[window.Handle] = snapshot;
			}

			if (!seeded)
			{
				seeded = true;
				previous = current;

				if (activeKnown)
					previousActive = activeHandle;

				return;
			}

			foreach (var handle in previous.Keys)
				if (!current.ContainsKey(handle))
					emit(new WaylandWindowEvent(WaylandWindowEventKind.Closed, handle));

			previous = current;

			if (activeKnown)
			{
				if (activeHandle != 0 && activeHandle != previousActive)
					emit(new WaylandWindowEvent(WaylandWindowEventKind.Activated, activeHandle));

				previousActive = activeHandle;
			}
			else if (previousActive != 0 && !current.ContainsKey(previousActive))
				previousActive = 0;
		}

		internal static bool TryGetActiveHandle(IReadOnlyList<WaylandWindowInfo> windows, out nint handle)
		{
			handle = 0;

			if (windows == null)
				return false;

			var allKnown = true;

			foreach (var window in windows)
			{
				if (window == null || window.Handle == 0)
					continue;

				if (!window.HasKnownField(WaylandWindowFields.Active))
					allKnown = false;
				else if (window.Active)
				{
					handle = window.Handle;
					return true;
				}
			}

			return allKnown;
		}

		private static void EmitChanges(nint handle, Snapshot prior, Snapshot current,
			Action<WaylandWindowEvent> emit)
		{
			if (prior.Knows(WaylandWindowFields.Title) && current.Knows(WaylandWindowFields.Title)
				&& !string.Equals(prior.Title, current.Title, StringComparison.Ordinal))
				emit(new WaylandWindowEvent(WaylandWindowEventKind.TitleChanged, handle));

			if (prior.Knows(WaylandWindowFields.Minimized) && current.Knows(WaylandWindowFields.Minimized)
				&& prior.Minimized != current.Minimized)
				emit(new WaylandWindowEvent(current.Minimized
					? WaylandWindowEventKind.Minimized : WaylandWindowEventKind.Restored, handle));

			if (prior.Knows(WaylandWindowFields.Frame) && current.Knows(WaylandWindowFields.Frame)
				&& prior.Frame != current.Frame)
				emit(new WaylandWindowEvent(WaylandWindowEventKind.MoveResized, handle) { Bounds = current.Frame });
		}

	}
}
#endif
