#if LINUX
using Keysharp.Builtins;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Adapts compositor window events to the platform-neutral stream consumed by
	/// <see cref="WinEventManager"/>. Compositor callbacks are serialized on a bounded dispatcher because the
	/// consumer may synchronously query the compositor and must never run on its IPC thread.
	/// </summary>
	internal sealed class WaylandWindowEventBackend : IWindowEventBackend
	{
		private const int QueueCapacity = 2048;

		private readonly IWaylandBackend backend;
		private readonly LinuxAccessibility.CaretEventSource caretSource;
		private readonly Lock gate = new();
		private WindowEventMask enabledMask;
		private IDisposable subscription;
		private EventDispatcher dispatcher;
		private bool disposed;

		internal WaylandWindowEventBackend(Script owner, IWaylandBackend backend)
		{
			this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
			caretSource = new LinuxAccessibility.CaretEventSource(owner);
		}

		public Action<WindowEventRaw> Sink { get; set; }
		public bool SupportsEfficientActiveTracking => backend.SupportsPushWindowEvents;

		public void Start(WindowEventMask mask)
		{
			if (mask == WindowEventMask.None)
				return;

			lock (gate)
			{
				if (disposed)
					return;

				var prior = enabledMask;
				enabledMask |= mask;

				if ((prior & WindowEventMask.CaretMove) == 0
					&& (enabledMask & WindowEventMask.CaretMove) != 0)
					caretSource.Start(EmitCaretEvent);
			}

			EnsureCompositorSubscription();
		}

		public void Stop(WindowEventMask mask)
		{
			IDisposable retiredSubscription = null;
			EventDispatcher retiredDispatcher = null;

			lock (gate)
			{
				var prior = enabledMask;
				enabledMask &= ~mask;

				if ((prior & WindowEventMask.CaretMove) != 0
					&& (enabledMask & WindowEventMask.CaretMove) == 0)
					caretSource.Stop();

				if (NeedsCompositor(enabledMask))
					return;

				retiredSubscription = subscription;
				retiredDispatcher = dispatcher;
				subscription = null;
				dispatcher = null;
			}

			Retire(retiredSubscription, retiredDispatcher);
		}

		public void Dispose()
		{
			IDisposable retiredSubscription;
			EventDispatcher retiredDispatcher;

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				enabledMask = WindowEventMask.None;
				retiredSubscription = subscription;
				retiredDispatcher = dispatcher;
				subscription = null;
				dispatcher = null;
			}

			caretSource.Dispose();
			Retire(retiredSubscription, retiredDispatcher);
			Sink = null;
		}

		private static bool NeedsCompositor(WindowEventMask mask)
			=> (mask & ~WindowEventMask.CaretMove) != WindowEventMask.None;

		private void EnsureCompositorSubscription()
		{
			EventDispatcher candidateDispatcher;
			lock (gate)
			{
				if (disposed || !NeedsCompositor(enabledMask) || dispatcher != null)
					return;

				candidateDispatcher = new EventDispatcher(Deliver, QueueCapacity, backend.Name);
				dispatcher = candidateDispatcher;
			}

			IDisposable candidate = null;
			try
			{
				// Backend subscriptions own their reconnection and polling fallback.
				candidate = backend.SubscribeWindowEvents(candidateDispatcher.Enqueue);
			}
			catch (Exception exception)
			{
				Diagnostics.Debug.WriteLine(
					$"WinEvent Wayland subscribe failed ({backend.Name}): {exception.Message}");
			}

			lock (gate)
			{
				if (ReferenceEquals(dispatcher, candidateDispatcher))
				{
					if (candidate != null)
					{
						subscription = candidate;
						return;
					}
					dispatcher = null;
				}
			}
			Retire(candidate, candidateDispatcher);
		}

		private static void Retire(IDisposable retiredSubscription, EventDispatcher retiredDispatcher)
		{
			try { retiredSubscription?.Dispose(); }
			catch (Exception exception)
			{
				Diagnostics.Debug.WriteLine($"WinEvent Wayland unsubscribe failed: {exception.Message}");
			}

			retiredDispatcher?.Dispose();
		}

		private void EmitCaretEvent(WindowEventRaw windowEvent)
		{
			Action<WindowEventRaw> sink;

			lock (gate)
			{
				if (disposed || (enabledMask & WindowEventMask.CaretMove) == 0)
					return;

				sink = Sink;
			}

			sink?.Invoke(windowEvent);
		}

		private void Deliver(EventDispatcher source, WaylandWindowEvent windowEvent)
		{
			Action<WindowEventRaw> sink;
			WindowEventMask mask;

			lock (gate)
			{
				if (disposed || !ReferenceEquals(dispatcher, source))
					return;

				sink = Sink;
				mask = enabledMask;
			}

			if (sink == null)
				return;

			var now = Environment.TickCount64;

			if (windowEvent.Kind == WaylandWindowEventKind.Created)
			{
				if ((mask & WindowEventMask.Create) != 0)
					sink(new WindowEventRaw(WindowEventType.Create, windowEvent.Handle, now));

				if ((mask & WindowEventMask.Show) != 0)
					sink(new WindowEventRaw(WindowEventType.Show, windowEvent.Handle, now));

				return;
			}

			var (type, bit) = Map(windowEvent.Kind);

			if (bit != WindowEventMask.None && (mask & bit) != 0)
				sink(new WindowEventRaw(type, windowEvent.Handle, now)
				{
					Bounds = windowEvent.Bounds,
					DestroyConfirmed = type == WindowEventType.Close
				});
		}

		private static (WindowEventType Type, WindowEventMask Bit) Map(WaylandWindowEventKind kind)
			=> kind switch
			{
				WaylandWindowEventKind.Closed       => (WindowEventType.Close, WindowEventMask.Close),
				WaylandWindowEventKind.Activated    => (WindowEventType.Active, WindowEventMask.Active),
				WaylandWindowEventKind.TitleChanged => (WindowEventType.TitleChange, WindowEventMask.TitleChange),
				WaylandWindowEventKind.Minimized    => (WindowEventType.Minimize, WindowEventMask.Minimize),
				WaylandWindowEventKind.Restored     => (WindowEventType.Restore, WindowEventMask.Restore),
				WaylandWindowEventKind.MoveResized  => (WindowEventType.Move, WindowEventMask.Move),
				// WinEvent has no deactivation event; the following Active event identifies the new window.
				WaylandWindowEventKind.ActiveStateChanged => (WindowEventType.Active, WindowEventMask.None),
				_                                   => (WindowEventType.Create, WindowEventMask.None)
			};

		/// <summary>
		/// A bounded, non-blocking producer queue. Repeated move events for one window replace their pending value;
		/// lifecycle events evict a pending move first if the consumer falls behind.
		/// </summary>
		private sealed class EventDispatcher : IDisposable
		{
			private readonly object sync = new();
			private readonly LinkedList<WaylandWindowEvent> pending = [];
			private readonly Dictionary<nint, LinkedListNode<WaylandWindowEvent>> pendingMoves = [];
			private readonly Action<EventDispatcher, WaylandWindowEvent> deliver;
			private readonly Thread worker;
			private readonly int capacity;
			private readonly string backendName;
			private bool overloadReported;
			private bool stopped;

			internal EventDispatcher(Action<EventDispatcher, WaylandWindowEvent> deliver, int capacity, string backendName)
			{
				this.deliver = deliver;
				this.capacity = capacity;
				this.backendName = backendName;
				worker = new Thread(DispatchLoop) { IsBackground = true, Name = "WinEvent-Wayland" };
				worker.Start();
			}

			internal void Enqueue(WaylandWindowEvent windowEvent)
			{
				if (windowEvent.Handle == 0)
					return;

				var move = windowEvent.Kind == WaylandWindowEventKind.MoveResized;
				var dropped = false;

				lock (sync)
				{
					if (stopped)
						return;

					if (move && pendingMoves.TryGetValue(windowEvent.Handle, out var existing))
					{
						existing.Value = windowEvent;
						return;
					}

					if (pending.Count >= capacity)
					{
						if (pendingMoves.Count != 0)
						{
							var obsolete = pendingMoves.First();
							pending.Remove(obsolete.Value);
							pendingMoves.Remove(obsolete.Key);
						}
						else if (move)
							dropped = true;
						else
							pending.RemoveFirst();
					}

					if (!dropped)
					{
						var node = pending.AddLast(windowEvent);

						if (move)
							pendingMoves[windowEvent.Handle] = node;

						System.Threading.Monitor.Pulse(sync);
					}

					if ((dropped || pending.Count >= capacity) && !overloadReported)
					{
						overloadReported = true;
						Diagnostics.Debug.WriteLine(
							$"WinEvent Wayland queue overloaded ({backendName}); coalescing or dropping stale events.");
					}
				}
			}

			private void DispatchLoop()
			{
				while (true)
				{
					WaylandWindowEvent windowEvent;

					lock (sync)
					{
						while (!stopped && pending.Count == 0)
							System.Threading.Monitor.Wait(sync);

						if (stopped)
							return;

						var first = pending.First;
						pending.RemoveFirst();
						windowEvent = first.Value;

						if (windowEvent.Kind == WaylandWindowEventKind.MoveResized)
							pendingMoves.Remove(windowEvent.Handle);
					}

					try { deliver(this, windowEvent); }
					catch (Exception exception)
					{
						Diagnostics.Debug.WriteLine($"WinEvent Wayland dispatch error: {exception.Message}");
					}
				}
			}

			public void Dispose()
			{
				lock (sync)
				{
					if (stopped)
						return;

					stopped = true;
					pending.Clear();
					pendingMoves.Clear();
					System.Threading.Monitor.PulseAll(sync);
				}

				if (Thread.CurrentThread != worker)
					try { worker.Join(1000); }
					catch { }
			}
		}
	}
}
#endif
