using System.Runtime.InteropServices;
using System.Threading;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Dedicated Wayland connection for layer-shell overlays. Besides the surface factories it owns a live
	/// wl_output + xdg-output topology, so global Keysharp screen coordinates can be resolved to one explicit output
	/// and output-local layer margins. All proxy access is serialized by <see cref="Sync"/>.
	/// </summary>
	internal sealed class WaylandLayerShellClient : IDisposable
	{
		internal readonly record struct OutputTarget(uint RegistryName, nint Proxy, ScreenRect Bounds,
			double BufferScale, int IntegerScale);
		internal readonly record struct OutputSegment(OutputTarget Output, ScreenRect Bounds, int SourceOffsetX,
			int SourceOffsetY);

		private static readonly object sync = new();
		private static readonly RetryGate probes = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(200), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static WaylandLayerShellClient current;
		private static RetryGate.Attempt creating;

		internal static object Sync => sync;

		internal static WaylandLayerShellClient Current
		{
			get
			{
				WaylandLayerShellClient stale = null;
				RetryGate.Attempt attempt;

				lock (sync)
				{
					// IsConnected, not IsAvailable: a client with no layer-shell is still the correct thing to keep
					// around for output topology. Only a genuinely dead connection should be retired and retried.
					if (current != null && !current.IsConnected)
					{
						stale = current;
						current = null;
						probes.Rearm();
					}

					if (current != null)
						return current;

					attempt = creating == null ? probes.TryBegin() : null;
					if (attempt != null)
						creating = attempt;
				}

				// Native discovery performs round trips. No global Wayland lock is held while the compositor answers.
				stale?.Dispose();

				if (attempt == null)
					return null;

				var candidate = TryCreate(out var unavailable);
				WaylandLayerShellClient result;
				lock (sync)
				{
					if (ReferenceEquals(creating, attempt))
					{
						creating = null;
						current = candidate;
						candidate = null;
						if (current != null) attempt.Succeed();
						if (unavailable) probes.Suspend();
					}
					result = current;
				}

				attempt.Dispose();
				candidate?.Dispose();
				return result;
			}
		}

		internal nint Display { get; private set; }
		internal nint Compositor { get; private set; }
		internal nint Shm { get; private set; }
		internal nint LayerShell { get; private set; }
		internal nint Viewporter { get; private set; }
		internal uint LayerShellVersion { get; private set; }

		private readonly Dictionary<uint, WaylandOutput> outputs = [];
		private readonly HashSet<WaylandImageOverlay> children = [];
		private nint registry;
		private nint xdgOutputManager;
		private readonly GCHandle selfHandle;
		private CancellationTokenSource dispatcherCancel;
		private Thread dispatcherThread;
		private volatile bool disposed;
		private volatile bool connectionLost;

		// wl_seat/wl_pointer for INTERACTIVE overlays (Overlay.OnEvent). An interactive layer surface already
		// takes a full input region (WaylandImageOverlay.ResolveInputRegion), so the compositor routes pointer
		// events to it; this listener is what turns those events into reports. All state below is touched only
		// on the dispatcher thread under Sync (events) or in OnGlobal/Dispose (also under Sync).
		private const uint SeatCapabilityPointer = 1;
		private const uint PointerButtonLeft = 0x110;    // BTN_LEFT
		private const uint PointerButtonRight = 0x111;   // BTN_RIGHT
		private const uint PointerButtonStateReleased = 0;
		private const long DoubleClickTimeMs = 400;      // no compositor double-click event exists; synthesized
		private const double DoubleClickSlop = 4.0;      // like the desktop defaults: two clicks, close in time+space
		private nint seat;
		private uint seatRegistryName;
		private nint pointerDevice;
		private WaylandImageOverlay pointerFocus;
		private double pointerX, pointerY;               // surface-local logical units, from enter/motion
		private WaylandImageOverlay lastClickTarget;
		private long lastClickTimeMs;
		private double lastClickX, lastClickY;
		private const int PointerQueueCapacity = 256;
		private readonly object pointerQueueSync = new();
		private readonly LinkedList<PointerDelivery> pointerQueue = [];
		private bool pointerDrainScheduled;
		private bool pointerQueueStopped;

		private readonly record struct PointerDelivery(WaylandImageOverlay Target,
			OverlayPointerKind Kind, double X, double Y);

		private WaylandLayerShellClient() => selfHandle = GCHandle.Alloc(this);

		/// <summary>
		/// True once the wl_display connection is up and the baseline globals (compositor, shm) are bound — enough
		/// to read output topology (<see cref="GetDisplays"/>, <see cref="TryGetOutputMetrics"/>) regardless of
		/// whether the compositor also advertises <c>zwlr_layer_shell_v1</c>. Kept separate from
		/// <see cref="IsAvailable"/> so a compositor with no layer-shell (notably GNOME/Mutter) still gets a live
		/// client instead of one that is created, found "unavailable" on the LayerShell check alone, and discarded
		/// — which would throw away perfectly good wl_output data along with it.
		/// </summary>
		internal bool IsConnected => !disposed && !connectionLost && Display != 0 && Compositor != 0 && Shm != 0;

		/// <summary>True when overlay surfaces can actually be created — additionally requires the compositor to
		/// advertise <c>zwlr_layer_shell_v1</c>. Use this (not <see cref="IsConnected"/>) to gate any layer-shell
		/// surface operation.</summary>
		internal bool IsAvailable => IsConnected && LayerShell != 0;

		internal bool Register(WaylandImageOverlay child)
		{
			lock (sync)
			{
				if (!IsAvailable || child == null)
					return false;

				return children.Add(child);
			}
		}

		internal void Unregister(WaylandImageOverlay child)
		{
			lock (sync)
				_ = children.Remove(child);
		}

		internal IReadOnlyList<DisplayInfo> GetDisplays()
		{
			lock (sync)
			{
				return outputs.Values
					.Where(o => o.Proxy != 0 && o.Bounds.HasArea)
					.OrderBy(o => o.RegistryName)
					.Select(o => new DisplayInfo(o.StableName, o.Bounds, o.Bounds, 1.0,
						o.Bounds.X == 0 && o.Bounds.Y == 0, o.RegistryName))
					.ToArray();
			}
		}

		/// <summary>
		/// The per-output metadata wl_output already delivered (physical size, make/model, current mode refresh and
		/// transform), keyed by the registry name carried in <see cref="DisplayInfo.NativeId"/>. Nothing is queried
		/// here — these values arrive with the geometry/mode events and were previously discarded.
		/// </summary>
		internal bool TryGetOutputMetrics(uint registryName, out double refreshRate, out int orientation,
			out int physicalWidthMm, out int physicalHeightMm, out string make, out string model)
		{
			lock (sync)
			{
				if (outputs.TryGetValue(registryName, out var output) && output.Proxy != 0)
				{
					// wl_output reports refresh in mHz; 59940 mHz is the 59.94 Hz a script expects to see.
					refreshRate = output.RefreshMilliHertz > 0 ? output.RefreshMilliHertz / 1000.0 : 0.0;
					orientation = output.Orientation;
					physicalWidthMm = output.PhysicalWidthMm;
					physicalHeightMm = output.PhysicalHeightMm;
					make = output.Make ?? "";
					model = output.Model ?? "";
					return true;
				}
			}

			refreshRate = 0.0;
			orientation = 0;
			physicalWidthMm = physicalHeightMm = 0;
			make = model = "";
			return false;
		}

		internal bool TryResolveOutput(ScreenRect bounds, out OutputTarget target)
		{
			lock (sync)
			{
				var displays = outputs.Values
					.Where(o => o.Proxy != 0 && o.Bounds.HasArea)
					.Select(o => new DisplayInfo(o.StableName, o.Bounds, o.Bounds, 1.0,
						o.Bounds.X == 0 && o.Bounds.Y == 0, o.RegistryName))
					.ToArray();

				if (DisplayTopology.TryFind(displays, bounds, out var selected)
					&& outputs.TryGetValue((uint)selected.NativeId, out var output))
				{
					target = new OutputTarget(output.RegistryName, output.Proxy, output.Bounds,
						output.BufferScale, Math.Max(1, output.IntegerScale));
					return true;
				}

				// A compositor can configure a layer surface before output metadata is complete. Still select a real
				// wl_output rather than null; the next Show will use its finished logical geometry.
				var fallback = outputs.Values.FirstOrDefault(o => o.Proxy != 0);

				if (fallback != null)
				{
					target = new OutputTarget(fallback.RegistryName, fallback.Proxy, fallback.Bounds,
						fallback.BufferScale, Math.Max(1, fallback.IntegerScale));
					return true;
				}

				target = default;
				return false;
			}
		}

		/// <summary>
		/// Splits a global rectangle at output boundaries. Layer-shell surfaces are permanently assigned to one
		/// wl_output and are clipped there, so a spanning overlay needs one surface per returned segment.
		/// </summary>
		internal IReadOnlyList<OutputSegment> GetOutputSegments(ScreenRect bounds)
		{
			lock (sync)
			{
				var segments = new List<OutputSegment>(Math.Min(outputs.Count, 4));

				foreach (var output in outputs.Values)
				{
					if (output.Proxy == 0 || !output.Bounds.HasArea)
						continue;

					var ob = output.Bounds;
					var left = Math.Max(bounds.X, ob.X);
					var top = Math.Max(bounds.Y, ob.Y);
					var right = Math.Min((long)bounds.X + bounds.Width, (long)ob.X + ob.Width);
					var bottom = Math.Min((long)bounds.Y + bounds.Height, (long)ob.Y + ob.Height);

					if (right <= left || bottom <= top)
						continue;

					var segmentBounds = new ScreenRect(left, top, (int)(right - left), (int)(bottom - top));
					var target = new OutputTarget(output.RegistryName, output.Proxy, ob, output.BufferScale,
						Math.Max(1, output.IntegerScale));
					segments.Add(new OutputSegment(target, segmentBounds,
						left - bounds.X, top - bounds.Y));
				}

				if (segments.Count > 0)
					return segments;

				// Preserve the historical nearest-output behavior for an entirely off-desktop rectangle. It will be
				// clipped by that output, but remains movable back on screen instead of failing to create a backing.
				if (TryResolveOutput(bounds, out var nearest))
					segments.Add(new OutputSegment(nearest, bounds, 0, 0));

				return segments;
			}
		}

		/// <summary>Checks a captured output identity and geometry against the live topology.</summary>
		internal bool IsOutputCurrent(OutputTarget target)
		{
			lock (sync)
				return !disposed && target.Proxy != 0 && outputs.TryGetValue(target.RegistryName, out var output)
					&& output.Proxy == target.Proxy && output.Bounds == target.Bounds
					&& output.IntegerScale == target.IntegerScale && output.BufferScale == target.BufferScale;
		}

		private static WaylandLayerShellClient TryCreate(out bool unavailable)
		{
			unavailable = false;

			if (!Platform.Desktop.IsWaylandSession)
			{
				unavailable = true;
				return null;
			}

			var display = WaylandNative.DisplayConnect(null);

			if (display == 0)
				return null;

			var client = new WaylandLayerShellClient { Display = display };

			try
			{
				client.registry = WaylandNative.DisplayGetRegistry(display);

				if (client.registry == 0
					|| WaylandNative.ProxyAddListener(client.registry, RegistryListener.Pointer,
						GCHandle.ToIntPtr(client.selfHandle)) != 0)
					throw new IOException("wl_registry listener setup failed.");

				// Only the baseline globals (compositor, shm) gate success here. zwlr_layer_shell_v1 is not
				// required: a compositor without it (notably GNOME/Mutter) still has a perfectly usable
				// connection for output topology, just not for creating layer-shell overlay surfaces — callers
				// that need those check IsAvailable, which additionally requires LayerShell.
				for (var i = 0; i < 8 && !client.IsConnected; i++)
					if (WaylandNative.DisplayRoundtrip(display) < 0)
						throw new IOException("wl_display.roundtrip failed.");

				if (!client.IsConnected)
				{
					// A connected registry that does not advertise the required globals is a stable
					// capability absence for this compositor generation, not a transport retry.
					unavailable = true;
					client.Dispose();
					return null;
				}

				// Registry globals arrive on the first trip; output/xdg-output properties are child-object events and
				// need another trip before the first overlay is placed.
				if (WaylandNative.DisplayRoundtrip(display) < 0)
					throw new IOException("wl_display output roundtrip failed.");

				client.StartDispatcher();
				return client;
			}
			catch
			{
				client.Dispose();
				return null;
			}
		}

		internal static void Reset()
		{
			WaylandLayerShellClient retired;

			lock (sync)
			{
				creating = null;
				retired = current;
				current = null;
				probes.Rearm();
			}

			retired?.Dispose();
		}

		private void StartDispatcher()
		{
			dispatcherCancel = new CancellationTokenSource();
			var token = dispatcherCancel.Token;
			dispatcherThread = new Thread(() => DispatchLoop(token))
			{
				IsBackground = true,
				Name = "KeysharpWaylandLayerShell"
			};
			dispatcherThread.Start();
		}

		private void DispatchLoop(CancellationToken token)
		{
			try
			{
				var pollFds = new WaylandNative.PollFd[1];

				while (!token.IsCancellationRequested)
				{
					var readPrepared = false;
					try
					{
						int fd;

						lock (sync)
						{
							if (disposed || Display == 0)
								return;

							while (WaylandNative.DisplayPrepareRead(Display) != 0)
								if (WaylandNative.DisplayDispatchPending(Display) < 0)
									return;

							readPrepared = true;
							_ = WaylandNative.DisplayFlush(Display);
							fd = WaylandNative.DisplayGetFd(Display);
							pollFds[0] = new WaylandNative.PollFd
							{
								FileDescriptor = fd,
								Events = WaylandNative.POLLIN
							};
						}

						// Requests may be marshalled and flushed while a read is prepared; this dispatcher remains
						// the sole event reader. Polling outside Sync removes animation latency and idle busy-waiting.
						var ready = fd >= 0 ? WaylandNative.Poll(pollFds, 1, 100) : -1;

						lock (sync)
						{
							if (disposed || Display == 0)
							{
								if (readPrepared && Display != 0)
									WaylandNative.DisplayCancelRead(Display);
								return;
							}

							if (ready > 0 && (pollFds[0].ReturnedEvents & WaylandNative.POLLIN) != 0)
							{
								var readResult = WaylandNative.DisplayReadEvents(Display);
								readPrepared = false;

								if (readResult < 0)
									return;
							}
							else
							{
								WaylandNative.DisplayCancelRead(Display);
								readPrepared = false;
							}

							if (WaylandNative.DisplayDispatchPending(Display) < 0)
								return;
						}
					}
					catch
					{
						if (readPrepared)
							try
							{
								lock (sync)
									if (Display != 0) WaylandNative.DisplayCancelRead(Display);
							}
							catch { }
					}
				}
			}
			finally
			{
				if (!token.IsCancellationRequested)
					connectionLost = true;
			}
		}

		internal bool TryFlush()
		{
			if (Display == 0)
				return false;

			if (WaylandNative.DisplayFlush(Display) >= 0)
				return true;

			// EAGAIN means the request is queued in libwayland and the dispatcher will flush it when the socket is
			// writable; it is not a lost transaction. Every other error is terminal for this connection.
			if (Marshal.GetLastPInvokeError() == 11)
				return true;

			connectionLost = true;
			return false;
		}

		private void OnGlobal(uint name, string interfaceName, uint version)
		{
			switch (interfaceName)
			{
				case "wl_compositor" when Compositor == 0:
					var compositorVersion = Math.Min(version, 5u);
					Compositor = WaylandNative.RegistryBind(registry, name, WaylandNative.CompositorInterface,
						"wl_compositor", compositorVersion);
					break;

				case "wl_shm" when Shm == 0:
					Shm = WaylandNative.RegistryBind(registry, name, WaylandNative.ShmInterface, "wl_shm", 1);
					break;

				case "wl_output":
					BindOutput(name, version);
					break;

				case "zxdg_output_manager_v1" when xdgOutputManager == 0:
					xdgOutputManager = WaylandNative.RegistryBind(registry, name,
						WaylandNative.Interfaces.XdgOutputManager, Math.Min(version, 3u));

					foreach (var output in outputs.Values)
						WaylandOutputBinding.BindXdgOutput(output, xdgOutputManager);
					break;

				case "wp_viewporter" when Viewporter == 0:
					Viewporter = WaylandNative.RegistryBind(registry, name,
						WaylandNative.Interfaces.WpViewporter, Math.Min(version, 1u));
					break;

				case "zwlr_layer_shell_v1" when LayerShell == 0:
					LayerShellVersion = Math.Min(version, 4u);
					LayerShell = WaylandNative.RegistryBind(registry, name,
						WaylandNative.Interfaces.WlrLayerShell, LayerShellVersion);
					break;

				case "wl_seat" when seat == 0:
					// v5 caps the pointer-listener vtable below at axis_discrete; newer seat versions add more
					// pointer events, which would need a longer vtable, so deliberately do not bind past 5.
					seatRegistryName = name;
					seat = WaylandNative.RegistryBind(registry, name, WaylandNative.SeatInterface,
						"wl_seat", Math.Min(version, 5u));

					if (seat != 0 && WaylandNative.ProxyAddListener(seat, SeatListener.Pointer,
							GCHandle.ToIntPtr(selfHandle)) != 0)
					{
						WaylandNative.ProxyDestroy(seat);
						seat = 0;
					}

					break;
			}
		}

		// ----- pointer input (interactive overlays) ------------------------------------------------

		private void OnSeatCapabilities(uint capabilities)
		{
			var hasPointer = (capabilities & SeatCapabilityPointer) != 0;

			if (hasPointer && pointerDevice == 0 && seat != 0)
			{
				pointerDevice = WaylandNative.SeatGetPointer(seat);

				if (pointerDevice != 0 && WaylandNative.ProxyAddListener(pointerDevice, PointerListener.Pointer,
						GCHandle.ToIntPtr(selfHandle)) != 0)
					ReleasePointerDevice();
			}
			else if (!hasPointer)
			{
				ReleasePointerDevice();
			}
		}

		private void ReleasePointerDevice()
		{
			if (pointerDevice != 0)
			{
				WaylandNative.PointerRelease(pointerDevice);
				pointerDevice = 0;
			}

			pointerFocus = null;
			lastClickTarget = null;
		}

		private void OnPointerEnter(nint surfaceHandle, double sx, double sy)
		{
			// Route by the entered wl_surface. `children` is a handful of overlays at most, and enter is rare
			// (motion uses the cached focus), so a linear scan under Sync is fine.
			pointerFocus = children.FirstOrDefault(c => c.Handle == surfaceHandle);
			pointerX = sx;
			pointerY = sy;
		}

		private void OnPointerLeave()
		{
			pointerFocus = null;
		}

		private void OnPointerMotion(double sx, double sy)
		{
			pointerX = sx;
			pointerY = sy;

			if (pointerFocus != null)
				QueuePointerDelivery(pointerFocus, OverlayPointerKind.MouseMove, sx, sy);
		}

		private void OnPointerButton(uint button, uint state)
		{
			// wl_pointer.button carries no position; the last enter/motion coordinates are the click point.
			// Click fires on RELEASE, matching the Windows/Eto backings.
			if (state != PointerButtonStateReleased || pointerFocus == null)
				return;

			if (button == PointerButtonRight)
			{
				QueuePointerDelivery(pointerFocus, OverlayPointerKind.ContextMenu, pointerX, pointerY);
				return;
			}

			if (button != PointerButtonLeft)
				return;

			// Synthesized double-click (the second click reports DoubleClick INSTEAD of a second Click, the
			// WinForms sequencing the other backings inherit from their toolkits).
			var now = Environment.TickCount64;

			if (ReferenceEquals(lastClickTarget, pointerFocus)
					&& now - lastClickTimeMs <= DoubleClickTimeMs
					&& Math.Abs(pointerX - lastClickX) <= DoubleClickSlop
					&& Math.Abs(pointerY - lastClickY) <= DoubleClickSlop)
			{
				var target = pointerFocus;
				lastClickTarget = null;
				QueuePointerDelivery(target, OverlayPointerKind.DoubleClick, pointerX, pointerY);
				return;
			}

			lastClickTarget = pointerFocus;
			lastClickTimeMs = now;
			lastClickX = pointerX;
			lastClickY = pointerY;
			QueuePointerDelivery(pointerFocus, OverlayPointerKind.Click, pointerX, pointerY);
		}

		private void QueuePointerDelivery(WaylandImageOverlay target, OverlayPointerKind kind,
			double x, double y)
		{
			var schedule = false;

			lock (pointerQueueSync)
			{
				if (pointerQueueStopped || target == null)
					return;

				if (kind == OverlayPointerKind.MouseMove
					&& pointerQueue.Last is { } last
					&& last.Value.Kind == OverlayPointerKind.MouseMove
					&& ReferenceEquals(last.Value.Target, target))
				{
					last.Value = new PointerDelivery(target, kind, x, y);
					return;
				}

				if (pointerQueue.Count >= PointerQueueCapacity)
				{
					var staleMove = pointerQueue.First;

					while (staleMove != null && staleMove.Value.Kind != OverlayPointerKind.MouseMove)
						staleMove = staleMove.Next;

					if (staleMove != null)
						pointerQueue.Remove(staleMove);
					else if (kind == OverlayPointerKind.MouseMove)
						return;
					else
						pointerQueue.RemoveFirst();
				}

				pointerQueue.AddLast(new PointerDelivery(target, kind, x, y));

				if (!pointerDrainScheduled)
				{
					pointerDrainScheduled = true;
					schedule = true;
				}
			}

			if (schedule)
				ThreadPool.QueueUserWorkItem(static state => state.DrainPointerQueue(), this,
					preferLocal: false);
		}

		private void DrainPointerQueue()
		{
			while (true)
			{
				PointerDelivery delivery;

				lock (pointerQueueSync)
				{
					if (pointerQueueStopped || pointerQueue.Count == 0)
					{
						pointerDrainScheduled = false;
						return;
					}

					delivery = pointerQueue.First.Value;
					pointerQueue.RemoveFirst();
				}

				try { delivery.Target.DeliverPointer(delivery.Kind, delivery.X, delivery.Y); }
				catch (Exception exception)
				{
					Diagnostics.Debug.WriteLine($"Wayland overlay pointer callback failed: {exception.Message}");
				}
			}
		}

		private void StopPointerQueue()
		{
			lock (pointerQueueSync)
			{
				pointerQueueStopped = true;
				pointerQueue.Clear();
			}
		}

		private void BindOutput(uint name, uint version)
		{
			if (outputs.ContainsKey(name))
				return;

			var output = WaylandOutputBinding.Bind(registry, name, version, xdgOutputManager);

			if (output != null)
				outputs.Add(name, output);
		}

		private void OnGlobalRemove(uint name)
		{
			if (name == seatRegistryName && seat != 0)
			{
				ReleasePointerDevice();
				WaylandNative.ProxyDestroy(seat);
				seat = 0;
				seatRegistryName = 0;
				return;
			}

			RemoveOutput(name);
		}

		private void RemoveOutput(uint name)
		{
			if (!outputs.Remove(name, out var output))
				return;

			WaylandOutputBinding.Release(output);
		}

		private static WaylandLayerShellClient Self(nint data)
			=> (WaylandLayerShellClient)GCHandle.FromIntPtr(data).Target;
		private static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? string.Empty;

		private static class RegistryListener
		{
			private static readonly GlobalHandler onGlobal = Global;
			private static readonly GlobalRemoveHandler onGlobalRemove = GlobalRemove;
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onGlobal, onGlobalRemove);

			private static void Global(nint data, nint registry, uint name, nint protocolInterface, uint version)
				=> Self(data).OnGlobal(name, Utf8(protocolInterface), version);
			private static void GlobalRemove(nint data, nint registry, uint name) => Self(data).OnGlobalRemove(name);

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void GlobalHandler(nint data, nint registry, uint name, nint protocolInterface, uint version);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void GlobalRemoveHandler(nint data, nint registry, uint name);
		}

		private static class SeatListener
		{
			private static readonly CapabilitiesHandler onCapabilities = Capabilities;
			private static readonly NameHandler onName = Name;
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onCapabilities, onName);

			private static void Capabilities(nint data, nint seat, uint capabilities)
				=> Self(data).OnSeatCapabilities(capabilities);
			private static void Name(nint data, nint seat, nint name) { }

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void CapabilitiesHandler(nint data, nint seat, uint capabilities);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void NameHandler(nint data, nint seat, nint name);
		}

		// wl_pointer events for the seat version bound above (<= 5): the vtable must cover every event of the
		// bound version, so the axis family is present as no-ops. Coordinates are wl_fixed_t, surface-local.
		private static class PointerListener
		{
			private static readonly EnterHandler onEnter = Enter;
			private static readonly LeaveHandler onLeave = Leave;
			private static readonly MotionHandler onMotion = Motion;
			private static readonly ButtonHandler onButton = Button;
			private static readonly AxisHandler onAxis = Axis;
			private static readonly FrameHandler onFrame = Frame;
			private static readonly AxisSourceHandler onAxisSource = AxisSource;
			private static readonly AxisStopHandler onAxisStop = AxisStop;
			private static readonly AxisDiscreteHandler onAxisDiscrete = AxisDiscrete;
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onEnter, onLeave, onMotion,
				onButton, onAxis, onFrame, onAxisSource, onAxisStop, onAxisDiscrete);

			private static void Enter(nint data, nint pointer, uint serial, nint surface, int sx, int sy)
				=> Self(data).OnPointerEnter(surface, WaylandNative.FixedToDouble(sx), WaylandNative.FixedToDouble(sy));
			private static void Leave(nint data, nint pointer, uint serial, nint surface)
				=> Self(data).OnPointerLeave();
			private static void Motion(nint data, nint pointer, uint time, int sx, int sy)
				=> Self(data).OnPointerMotion(WaylandNative.FixedToDouble(sx), WaylandNative.FixedToDouble(sy));
			private static void Button(nint data, nint pointer, uint serial, uint time, uint button, uint state)
				=> Self(data).OnPointerButton(button, state);
			private static void Axis(nint data, nint pointer, uint time, uint axis, int value) { }
			private static void Frame(nint data, nint pointer) { }
			private static void AxisSource(nint data, nint pointer, uint source) { }
			private static void AxisStop(nint data, nint pointer, uint time, uint axis) { }
			private static void AxisDiscrete(nint data, nint pointer, uint axis, int discrete) { }

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void EnterHandler(nint data, nint pointer, uint serial, nint surface, int sx, int sy);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void LeaveHandler(nint data, nint pointer, uint serial, nint surface);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void MotionHandler(nint data, nint pointer, uint time, int sx, int sy);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void ButtonHandler(nint data, nint pointer, uint serial, uint time, uint button, uint state);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void AxisHandler(nint data, nint pointer, uint time, uint axis, int value);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void FrameHandler(nint data, nint pointer);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void AxisSourceHandler(nint data, nint pointer, uint source);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void AxisStopHandler(nint data, nint pointer, uint time, uint axis);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void AxisDiscreteHandler(nint data, nint pointer, uint axis, int discrete);
		}

		public void Dispose()
		{
			WaylandImageOverlay[] liveChildren;

			lock (sync)
			{
				if (disposed)
					return;

				disposed = true;
				if (ReferenceEquals(current, this)) current = null;
				liveChildren = children.ToArray();
			}

			StopPointerQueue();
			try { dispatcherCancel?.Cancel(); } catch { }
			var dispatcherStopped = true;
			try { dispatcherStopped = dispatcherThread?.Join(1000) ?? true; } catch { dispatcherStopped = false; }

			// Never free proxies/listener handles while the dispatch thread could still be inside libwayland. Its bounded
			// poll wakes for cancellation, so this is only a defensive connection leak on a genuinely wedged native call.
			if (!dispatcherStopped)
				return;
			dispatcherCancel?.Dispose();
			dispatcherCancel = null;
			dispatcherThread = null;

			// Children retain raw wl_proxy pointers. Invalidate every child before display_disconnect so a later
			// Overlay.Dispose cannot marshal through freed memory. On connection loss the child abandons protocol
			// objects locally and force-frees its SHM mappings because wl_buffer.release can no longer arrive.
			foreach (var child in liveChildren)
				try { child.InvalidateConnection(connectionLost); }
				catch { }

			lock (sync)
			{
				children.Clear();

				if (connectionLost)
				{
					foreach (var output in outputs.Values)
						WaylandOutputBinding.Abandon(output);

					outputs.Clear();
					// Protocol objects on a dead connection are abandoned locally, never released via requests.
					pointerDevice = seat = 0;
					pointerFocus = lastClickTarget = null;
					Viewporter = xdgOutputManager = LayerShell = Shm = Compositor = registry = 0;

					if (Display != 0)
					{
						WaylandNative.DisplayDisconnect(Display);
						Display = 0;
					}

					if (selfHandle.IsAllocated) selfHandle.Free();
					return;
				}

				foreach (var name in outputs.Keys.ToArray())
					RemoveOutput(name);

				ReleasePointerDevice();

				if (seat != 0) { WaylandNative.ProxyDestroy(seat); seat = 0; }

				if (Viewporter != 0) { WaylandNative.ViewporterDestroy(Viewporter); Viewporter = 0; }
				if (xdgOutputManager != 0) { WaylandNative.XdgOutputManagerDestroy(xdgOutputManager); xdgOutputManager = 0; }

				if (LayerShell != 0)
				{
					if (LayerShellVersion >= 3) WaylandNative.LayerShellDestroy(LayerShell);
					else WaylandNative.ProxyDestroy(LayerShell);
					LayerShell = 0;
				}

				if (Shm != 0) { WaylandNative.ProxyDestroy(Shm); Shm = 0; }
				if (Compositor != 0) { WaylandNative.ProxyDestroy(Compositor); Compositor = 0; }
				if (registry != 0) { WaylandNative.ProxyDestroy(registry); registry = 0; }

				if (Display != 0)
				{
					WaylandNative.DisplayDisconnect(Display);
					Display = 0;
				}
			}

			if (selfHandle.IsAllocated)
				selfHandle.Free();
		}
	}
}
#endif
