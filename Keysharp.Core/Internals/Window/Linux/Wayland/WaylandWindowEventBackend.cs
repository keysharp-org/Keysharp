#if LINUX
using System.Collections.Concurrent;
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// <see cref="IWindowEventBackend"/> for Wayland sessions. Wayland's core protocol gives a foreign client no
	/// window-event stream at all, so this delegates to the active compositor's <see cref="IWaylandBackend"/>
	/// (KWin via a persistent KWin script, GNOME via the shell extension, others via a generic polling source) and
	/// translates each <see cref="WaylandWindowEvent"/> into the platform-neutral <see cref="WindowEventRaw"/> the
	/// <c>WinEventManager</c> consumes — mirroring what the X11 backend does on X11.
	/// <para>
	/// The compositor source produces every event kind; the desired-category mask from Start/Stop is applied here
	/// as a filter rather than as a native subscribe/unsubscribe, since none of the IPC channels expose
	/// per-category subscription. A "Created" event also raises Show, mirroring how opening a window on Windows
	/// fires both.
	/// </para>
	/// <para>
	/// Compositor callbacks arrive on D-Bus / IPC threads, and the consumer (the WinEvent manager) immediately
	/// issues <em>synchronous</em> D-Bus queries back to the compositor to resolve window state — calling that from
	/// the IPC read/dispatch thread risks reentrancy and deadlock. So events are handed to a single dedicated
	/// dispatcher thread, which both decouples from the IPC channel and gives the manager the ordered,
	/// single-threaded delivery its per-subscription dedupe state assumes.
	/// </para>
	/// </summary>
	internal sealed class WaylandWindowEventBackend : IWindowEventBackend
	{
		private readonly IWaylandBackend backend;
		// No compositor exposes caret movement — the caret is an accessibility concept — so CaretMove is served by
		// the same display-server-agnostic AT-SPI source the X11 backend uses, and a caret-only subscription never
		// touches the compositor channel at all.
		private readonly LinuxAccessibility.CaretEventSource caretSource;
		private readonly Lock gate = new();
		private WindowEventMask enabledMask = WindowEventMask.None;
		private IDisposable subscription;
		private BlockingCollection<WaylandWindowEvent> queue;
		private Thread worker;
		private bool disposed;

		internal WaylandWindowEventBackend(Script owner, IWaylandBackend backend)
		{
			this.backend = backend;
			caretSource = new LinuxAccessibility.CaretEventSource(owner);
		}

		public Action<WindowEventRaw> Sink { get; set; }

		public void Start(WindowEventMask mask)
		{
			if ((mask & WindowEventMask.CaretMove) != 0)
				caretSource.Start(EmitCaretEvent);

			lock (gate)
			{
				if (disposed)
					return;

				enabledMask |= mask;

				if (subscription != null || !NeedsCompositor(enabledMask))
					return;

				// Spin up the dispatcher before subscribing so no event is dropped between the two.
				queue = new BlockingCollection<WaylandWindowEvent>();
				worker = new Thread(DispatchLoop) { IsBackground = true, Name = "WinEvent-Wayland" };
				worker.Start();
			}

			IDisposable sub;

			try
			{
				sub = backend.SubscribeWindowEvents(Enqueue);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"WinEvent Wayland subscribe failed ({backend?.Name}): {ex.Message}");
				StopWorker();
				return;
			}

			if (sub == null)
			{
				StopWorker();
				return;
			}

			bool drop;

			lock (gate)
			{
				drop = disposed || subscription != null;

				if (!drop)
					subscription = sub;
			}

			if (drop)
			{
				sub.Dispose();   // lost a race or disposed mid-subscribe
				StopWorker();
			}
		}

		public void Stop(WindowEventMask mask)
		{
			if ((mask & WindowEventMask.CaretMove) != 0)
				caretSource.Stop();

			IDisposable toDispose = null;

			lock (gate)
			{
				enabledMask &= ~mask;

				if (!NeedsCompositor(enabledMask))
				{
					toDispose = subscription;
					subscription = null;
				}
			}

			if (toDispose != null)
			{
				toDispose.Dispose();
				StopWorker();
			}
		}

		public void Dispose()
		{
			IDisposable toDispose;

			lock (gate)
			{
				disposed = true;
				enabledMask = WindowEventMask.None;
				toDispose = subscription;
				subscription = null;
			}

			caretSource.Dispose();
			toDispose?.Dispose();
			StopWorker();
		}

		/// <summary>Whether any category the compositor channel actually serves is enabled — i.e. anything but
		/// CaretMove, which comes from AT-SPI instead.</summary>
		private static bool NeedsCompositor(WindowEventMask mask) => (mask & ~WindowEventMask.CaretMove) != WindowEventMask.None;

		private void EmitCaretEvent(WindowEventRaw ev) => Sink?.Invoke(ev);

		private void StopWorker()
		{
			BlockingCollection<WaylandWindowEvent> q;

			lock (gate)
			{
				q = queue;
				queue = null;
				worker = null;
			}

			try { q?.CompleteAdding(); }
			catch { }
		}

		// Called on a compositor IPC thread — must not block; just hand off to the dispatcher.
		private void Enqueue(WaylandWindowEvent ev)
		{
			if (ev.Handle == 0)
				return;

			var q = queue;

			if (q == null)
				return;

			try { q.Add(ev); }
			catch { }   // queue completed/disposed between the null check and Add
		}

		private void DispatchLoop()
		{
			BlockingCollection<WaylandWindowEvent> q;

			lock (gate)
				q = queue;

			if (q == null)
				return;

			try
			{
				foreach (var ev in q.GetConsumingEnumerable())
				{
					try
					{
						Deliver(ev);
					}
					catch (Exception ex)
					{
						Diagnostics.Debug.WriteLine($"WinEvent Wayland dispatch error: {ex.Message}");
					}
				}
			}
			catch (Exception)
			{
				// Collection disposed/completed — normal shutdown.
			}
		}

		private void Deliver(WaylandWindowEvent ev)
		{
			var sink = Sink;

			if (sink == null)
				return;

			WindowEventMask mask;

			lock (gate)
				mask = enabledMask;

			// Created maps to Create and (since a Wayland toplevel is shown when it appears) also Show.
			if (ev.Kind == WaylandWindowEventKind.Created)
			{
				if ((mask & WindowEventMask.Create) != 0)
					sink(new WindowEventRaw(WindowEventType.Create, ev.Handle, NowMs()));

				if ((mask & WindowEventMask.Show) != 0)
					sink(new WindowEventRaw(WindowEventType.Show, ev.Handle, NowMs()));

				return;
			}

			var (type, bit) = Map(ev.Kind);

			if (bit != WindowEventMask.None && (mask & bit) != 0)
				// A compositor "toplevel closed" is an authoritative removal (like macOS AXUIElementDestroyed), so
				// mark Close confirmed — the manager then drops the window from every Exist/NotExist set without
				// re-checking a window list that may still momentarily resolve it.
				sink(new WindowEventRaw(type, ev.Handle, NowMs()) { Bounds = ev.Bounds, DestroyConfirmed = type == WindowEventType.Close });
		}

		private static (WindowEventType type, WindowEventMask bit) Map(WaylandWindowEventKind kind) => kind switch
		{
			WaylandWindowEventKind.Closed       => (WindowEventType.Close, WindowEventMask.Close),
			WaylandWindowEventKind.Activated    => (WindowEventType.Active, WindowEventMask.Active),
			WaylandWindowEventKind.TitleChanged => (WindowEventType.TitleChange, WindowEventMask.TitleChange),
			WaylandWindowEventKind.Minimized    => (WindowEventType.Minimize, WindowEventMask.Minimize),
			WaylandWindowEventKind.Restored     => (WindowEventType.Restore, WindowEventMask.Restore),
			WaylandWindowEventKind.MoveResized  => (WindowEventType.Move, WindowEventMask.Move),
			_                                   => (WindowEventType.Create, WindowEventMask.None)
		};

		private static long NowMs() => Environment.TickCount64;
	}
}
#endif
