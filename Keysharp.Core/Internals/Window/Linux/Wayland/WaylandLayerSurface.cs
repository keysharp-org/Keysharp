using System.Runtime.InteropServices;
using System.Threading;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// One <c>zwlr_layer_surface_v1</c> wrapping its underlying <c>wl_surface</c>. Owns the
	/// configure/ack/commit dance and the input-region setup. Higher-level helpers (tooltip,
	/// cursor monitor) compose this with a <see cref="WaylandShmBuffer"/>.
	///
	/// All Wayland calls must be performed while holding <see cref="WaylandLayerShellClient.Sync"/>;
	/// the configure callback is invoked from the dispatcher thread under that same lock.
	/// </summary>
	internal sealed class WaylandLayerSurface : IDisposable
	{
		private readonly WaylandLayerShellClient client;
		private readonly GCHandle selfHandle;
		private readonly ManualResetEventSlim configuredEvent = new(false);
		private nint surface;
		private nint layerSurface;
		private nint viewport;
		private uint pendingSerial;
		private bool ackPending;
		private bool disposed;

		internal bool IsConfigured { get; private set; }
		internal bool IsClosed { get; private set; }
		internal nint Surface => surface;

		internal WaylandLayerSurface(WaylandLayerShellClient client, nint output, uint layer, string ns)
		{
			this.client = client ?? throw new ArgumentNullException(nameof(client));
			selfHandle = GCHandle.Alloc(this);

			try
			{
				lock (WaylandLayerShellClient.Sync)
				{
					surface = WaylandNative.CompositorCreateSurface(client.Compositor);

					if (surface == 0)
						throw new IOException("wl_compositor.create_surface returned null.");

					layerSurface = WaylandNative.LayerShellGetLayerSurface(client.LayerShell, surface, output, layer, ns ?? "keysharp");

					if (layerSurface == 0)
						throw new IOException("zwlr_layer_shell_v1.get_layer_surface returned null.");

					if (WaylandNative.ProxyAddListener(layerSurface, LayerSurfaceListener.Pointer,
							GCHandle.ToIntPtr(selfHandle)) != 0)
						throw new IOException("zwlr_layer_surface_v1 listener setup failed.");

					if (client.Viewporter != 0)
						viewport = WaylandNative.ViewporterGetViewport(client.Viewporter, surface);
				}
			}
			catch
			{
				lock (WaylandLayerShellClient.Sync)
				{
					if (viewport != 0) { WaylandNative.ViewportDestroy(viewport); viewport = 0; }
					if (layerSurface != 0) { WaylandNative.LayerSurfaceDestroy(layerSurface); layerSurface = 0; }
					if (surface != 0) { WaylandNative.SurfaceDestroy(surface); surface = 0; }
				}

				if (selfHandle.IsAllocated)
					selfHandle.Free();

				throw;
			}
		}

		/// <summary>
		/// Maps a raster buffer to the requested logical surface size. Viewporter handles arbitrary fractional
		/// density; without it, an exact integer output scale uses wl_surface.set_buffer_scale, and fractional
		/// displays fall back to a 1x logical buffer so geometry remains correct.
		/// </summary>
		internal bool ConfigureBufferMapping(int logicalWidth, int logicalHeight, int integerScale)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (surface == 0)
					return false;

				if (viewport != 0)
				{
					WaylandNative.SurfaceSetBufferScale(surface, 1);
					WaylandNative.ViewportSetDestination(viewport, logicalWidth, logicalHeight);
					return true;
				}

				WaylandNative.SurfaceSetBufferScale(surface, Math.Max(1, integerScale));
				return false;
			}
		}

		internal void SetSize(uint width, uint height)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (layerSurface != 0)
					WaylandNative.LayerSurfaceSetSize(layerSurface, width, height);
			}
		}

		internal void SetAnchor(uint anchor)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (layerSurface != 0)
					WaylandNative.LayerSurfaceSetAnchor(layerSurface, anchor);
			}
		}

		internal void SetMargin(int top, int right, int bottom, int left)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (layerSurface != 0)
					WaylandNative.LayerSurfaceSetMargin(layerSurface, top, right, bottom, left);
			}
		}

		internal void SetInputRegion(nint region)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (surface != 0)
					WaylandNative.SurfaceSetInputRegion(surface, region);
			}
		}

		internal void SetExclusiveZone(int zone)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (layerSurface != 0)
					WaylandNative.LayerSurfaceSetExclusiveZone(layerSurface, zone);
			}
		}

		internal void SetKeyboardInteractivity(uint interactivity)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (layerSurface != 0)
					WaylandNative.LayerSurfaceSetKeyboardInteractivity(layerSurface, interactivity);
			}
		}

		internal bool AttachBuffer(WaylandShmBuffer buffer, WaylandFrameDamage damage)
		{
			if (buffer == null)
				return false;

			lock (WaylandLayerShellClient.Sync)
			{
				if (surface == 0 || buffer.Buffer == 0 || !client.IsAvailable)
					return false;

				WaylandNative.SurfaceAttach(surface, buffer.Buffer, 0, 0);

				if (damage.Kind == DamageKind.All
						|| damage.Kind == DamageKind.Region && !WaylandNative.SupportsBufferDamage(surface))
					WaylandNative.SurfaceDamageBuffer(surface, 0, 0, buffer.Width, buffer.Height);
				else if (damage.Kind == DamageKind.Region)
					WaylandNative.SurfaceDamageBuffer(surface, damage.Bounds.X, damage.Bounds.Y,
						damage.Bounds.Width, damage.Bounds.Height);

				buffer.MarkInFlight();
				return true;
			}
		}

		internal bool Commit()
		{
			lock (WaylandLayerShellClient.Sync)
			{
				if (surface == 0 || !client.IsAvailable)
					return false;

				// The protocol requires ack_configure before the commit that attaches its buffer.
				if (ackPending && layerSurface != 0)
				{
					WaylandNative.LayerSurfaceAckConfigure(layerSurface, pendingSerial);
					ackPending = false;
				}

				WaylandNative.SurfaceCommit(surface);

				if (!client.TryFlush())
				{
					return false;
				}

				return true;
			}
		}

		/// <summary>Waits for the initial configure event delivered by the connection dispatcher. Later
		/// margin/size commits are asynchronous and do not use this initialization barrier.</summary>
		internal bool WaitForConfigure(int timeoutMs = 1000)
		{
			if (client.IsAvailable && IsConfigured && !IsClosed)
				return true;

			return configuredEvent.Wait(timeoutMs) && client.IsAvailable && IsConfigured && !IsClosed;
		}

		// --- listener callback ---

		private void OnConfigure(uint serial)
		{
			pendingSerial = serial;
			ackPending = true;
			IsConfigured = true;
			configuredEvent.Set();
		}

		private void OnClosed()
		{
			IsClosed = true;
			IsConfigured = false;
			configuredEvent.Set();
		}

		public void Dispose()
		{
			if (disposed)
				return;

			configuredEvent.Set();

			lock (WaylandLayerShellClient.Sync)
			{
				if (viewport != 0)
				{
					WaylandNative.ViewportDestroy(viewport);
					viewport = 0;
				}

				if (layerSurface != 0)
				{
					WaylandNative.LayerSurfaceDestroy(layerSurface);
					layerSurface = 0;
				}

				if (surface != 0)
				{
					WaylandNative.SurfaceDestroy(surface);
					surface = 0;
				}

				if (client.Display != 0)
					_ = WaylandNative.DisplayFlush(client.Display);
			}

			if (selfHandle.IsAllocated)
				selfHandle.Free();

			configuredEvent.Dispose();
			disposed = true;
		}

		/// <summary>Drops managed ownership without issuing requests through a failed connection. The owning
		/// wl_display will free the protocol objects during disconnect; no callbacks can run after its dispatcher stops.</summary>
		internal void Abandon()
		{
			if (disposed)
				return;

			configuredEvent.Set();
			viewport = layerSurface = surface = 0;

			if (selfHandle.IsAllocated)
				selfHandle.Free();

			configuredEvent.Dispose();
			disposed = true;
		}

		private static WaylandLayerSurface Self(nint data) => (WaylandLayerSurface)GCHandle.FromIntPtr(data).Target;

		private static class LayerSurfaceListener
		{
			private static readonly ConfigureHandler onConfigure = Configure;
			private static readonly ClosedHandler onClosed = ClosedEvent;
			internal static readonly nint Pointer = Build();

			private static nint Build()
			{
				var block = Marshal.AllocHGlobal(IntPtr.Size * 2);
				Marshal.WriteIntPtr(block, 0, Marshal.GetFunctionPointerForDelegate(onConfigure));
				Marshal.WriteIntPtr(block, IntPtr.Size, Marshal.GetFunctionPointerForDelegate(onClosed));
				return block;
			}

			private static void Configure(nint data, nint layerSurface, uint serial, uint width, uint height)
				=> Self(data).OnConfigure(serial);

			private static void ClosedEvent(nint data, nint layerSurface)
				=> Self(data).OnClosed();

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void ConfigureHandler(nint data, nint layerSurface, uint serial, uint width, uint height);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void ClosedHandler(nint data, nint layerSurface);
		}
	}
}
#endif
