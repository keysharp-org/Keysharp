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
		private volatile bool stopped;

		// Last-seen state, keyed by stable handle. Only touched on the poll thread.
		private readonly Dictionary<nint, Snapshot> previous = new();
		private nint previousActive;
		private bool seeded;

		private readonly record struct Snapshot(string Title, bool Minimized, Rectangle Frame);

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

			var current = new Dictionary<nint, Snapshot>(windows.Count);
			var activeHandle = ActiveHandle(windows);

			foreach (var w in windows)
			{
				if (w.Handle == 0)
					continue;

				current[w.Handle] = new Snapshot(w.Title ?? string.Empty, w.Minimized, w.FrameGeometry);
			}

			// First successful poll just establishes the baseline — pre-existing windows must not fire Create.
			if (!seeded)
			{
				foreach (var kvp in current)
					previous[kvp.Key] = kvp.Value;

				seeded = true;
				previousActive = activeHandle;
				return;
			}

			// Additions and per-window state changes.
			foreach (var kvp in current)
			{
				if (!previous.TryGetValue(kvp.Key, out var old))
				{
					Emit(WaylandWindowEventKind.Created, kvp.Key);
					continue;
				}

				if (!string.Equals(old.Title, kvp.Value.Title, StringComparison.Ordinal))
					Emit(WaylandWindowEventKind.TitleChanged, kvp.Key);

				if (old.Minimized != kvp.Value.Minimized)
					Emit(kvp.Value.Minimized ? WaylandWindowEventKind.Minimized : WaylandWindowEventKind.Restored, kvp.Key);

				if (old.Frame != kvp.Value.Frame)
					Emit(WaylandWindowEventKind.MoveResized, kvp.Key, kvp.Value.Frame);
			}

			// Removals.
			foreach (var handle in previous.Keys)
				if (!current.ContainsKey(handle))
					Emit(WaylandWindowEventKind.Closed, handle);

			previous.Clear();

			foreach (var kvp in current)
				previous[kvp.Key] = kvp.Value;

			if (activeHandle != 0 && activeHandle != previousActive)
				Emit(WaylandWindowEventKind.Activated, activeHandle);

			previousActive = activeHandle;
		}

		internal static nint ActiveHandle(IReadOnlyList<WaylandWindowInfo> windows)
		{
			if (windows != null)
				foreach (var window in windows)
					if (window is { Active: true } && window.Handle != 0)
						return window.Handle;

			return 0;
		}

		private void Emit(WaylandWindowEventKind kind, nint handle, Rectangle? bounds = null)
		{
			if (stopped)
				return;

			try { sink(new WaylandWindowEvent(kind, handle) { Bounds = bounds }); }
			catch { }
		}
	}
}
#endif
