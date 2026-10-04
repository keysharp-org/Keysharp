using System.Runtime.InteropServices;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>A mapped ARGB8888 buffer a frame is copied into: a Wayland shm buffer, or a frame shared with a
	/// shell extension by descriptor.</summary>
	internal interface IPixelBuffer
	{
		int Width { get; }
		int Height { get; }
		int Stride { get; }
		nint Data { get; }
	}

	/// <summary>Keeps its mapping until compositor release; the client owns retired buffers through disconnect.
	/// Proxy access and lifetime changes require <see cref="WaylandLayerShellClient.Sync"/>.</summary>
	internal sealed class WaylandShmBuffer : IDisposable, IPixelBuffer
	{
		public int Width { get; }
		public int Height { get; }
		public int Stride { get; }
		internal nint Buffer { get; private set; }
		public nint Data { get; private set; }
		private nuint MapLength { get; }

		private GCHandle listenerHandle;
		private readonly WaylandLayerShellClient client;
		private readonly ManualResetEventSlim releaseSignal;
		private volatile bool released = true;
		private bool disposePending;
		private bool disposed;
		internal bool Released => released;

		/// <summary>
		/// The frame number this buffer last carried to the compositor, or -1 if it never has. A pool buffer
		/// still holds the pixels from that frame, so reusing it for a partial update means first replaying
		/// everything that changed since — its "age". Without this, a partial update into a recycled buffer
		/// would show whatever that older frame had outside the damaged region.
		/// </summary>
		internal long LastPresentedFrame { get; set; } = -1;

		private WaylandShmBuffer(WaylandLayerShellClient client, nint mapping, nuint mapLength, nint buffer,
			int width, int height, int stride, ManualResetEventSlim releaseSignal)
		{
			this.client = client;
			this.releaseSignal = releaseSignal;
			Data = mapping;
			MapLength = mapLength;
			Buffer = buffer;
			Width = width;
			Height = height;
			Stride = stride;
			listenerHandle = GCHandle.Alloc(this);
			if (WaylandNative.ProxyAddListener(buffer, ReleaseListener.Pointer, GCHandle.ToIntPtr(listenerHandle)) != 0)
			{
				listenerHandle.Free();
				Buffer = 0;
				throw new IOException("wl_buffer listener setup failed.");
			}
			client.Register(this);
		}

		/// <summary>
		/// Allocates a new ARGB8888 buffer of the requested pixel size. The compositor must already
		/// have advertised wl_shm.format for ARGB8888, which the protocol requires.
		/// </summary>
		/// <param name="releaseSignal">Set whenever the compositor releases the buffer.</param>
		internal static WaylandShmBuffer Create(WaylandLayerShellClient client, int width, int height,
			ManualResetEventSlim releaseSignal)
		{
			var shm = client.Shm;
			if (shm == 0)
				throw new InvalidOperationException("wl_shm global is not bound.");

			if (width <= 0 || height <= 0)
				throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");

			var strideValue = (long)width * 4;

			if (strideValue > int.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(width), "The SHM row stride exceeds the Wayland protocol limit.");

			var stride = (int)strideValue;
			var size = (long)stride * height;

			if (size <= 0 || size > int.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(height), "The SHM buffer exceeds the Wayland protocol limit.");

			var fd = WaylandNative.MemfdCreate("keysharp-wl-shm", WaylandNative.MFD_CLOEXEC);
			nint mapping = 0;
			nint pool = 0;
			nint buffer = 0;

			if (fd < 0)
				throw new IOException($"memfd_create failed: errno={Marshal.GetLastPInvokeError()}");

			try
			{
				if (WaylandNative.Ftruncate(fd, size) != 0)
					throw new IOException($"ftruncate({size}) failed: errno={Marshal.GetLastPInvokeError()}");

				mapping = WaylandNative.Mmap(0, (nuint)size, WaylandNative.PROT_READ | WaylandNative.PROT_WRITE,
					WaylandNative.MAP_SHARED, fd, 0);

				if (mapping == WaylandNative.MAP_FAILED)
					throw new IOException($"mmap failed: errno={Marshal.GetLastPInvokeError()}");

				pool = WaylandNative.ShmCreatePool(shm, fd, (int)size);

				if (pool == 0)
					throw new IOException("wl_shm.create_pool returned null.");

				buffer = WaylandNative.ShmPoolCreateBuffer(pool, 0, width, height, stride, WaylandNative.WlShmFormatArgb8888);

				if (buffer == 0)
					throw new IOException("wl_shm_pool.create_buffer returned null.");

				// The wl_buffer and mmap outlive their creation pool; neither the pool proxy nor the local fd
				// needs to remain open for a cached buffer.
				WaylandNative.ShmPoolDestroy(pool);
				pool = 0;
				_ = WaylandNative.Close(fd);
				fd = -1;
				var result = new WaylandShmBuffer(client, mapping, (nuint)size, buffer, width, height, stride, releaseSignal);
				mapping = buffer = 0;
				return result;
			}
			finally
			{
				if (buffer != 0) WaylandNative.BufferDestroy(buffer);
				if (pool != 0) WaylandNative.ShmPoolDestroy(pool);
				if (mapping != 0 && mapping != WaylandNative.MAP_FAILED) _ = WaylandNative.Munmap(mapping, (nuint)size);
				if (fd >= 0) _ = WaylandNative.Close(fd);
			}
		}

		// Listener glue for the wl_buffer.release event. The compositor sends this exactly when the
		// buffer is no longer in use and the client may safely reuse or destroy it.
		private void MarkReleased()
		{
			released = true;
			releaseSignal?.Set();

			if (disposePending)
				DisposeCore(sendRequest: true);
		}

		internal void MarkInFlight() => released = false;

		public void Dispose()
		{
			if (disposed)
				return;

			if (!released)
			{
				disposePending = true;
				return;
			}

			DisposeCore(sendRequest: true);
		}

		/// <summary>Releases local proxies and mappings after the display dispatcher stops, without requests.</summary>
		internal void Abandon() => DisposeCore(sendRequest: false);

		private void DisposeCore(bool sendRequest)
		{
			if (disposed)
				return;

			disposePending = false;

			if (Buffer != 0)
			{
				if (sendRequest) WaylandNative.BufferDestroy(Buffer);
				else WaylandNative.ProxyDestroy(Buffer);
			}
			Buffer = 0;

			if (Data != 0)
			{
				_ = WaylandNative.Munmap(Data, MapLength);
				Data = 0;
			}

			if (listenerHandle.IsAllocated)
				listenerHandle.Free();

			client.Unregister(this);
			disposed = true;
		}

		private static class ReleaseListener
		{
			private static readonly ReleaseHandler onRelease = Release;
			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onRelease);

			private static void Release(nint data, nint buffer)
			{
				var handle = GCHandle.FromIntPtr(data);

				if (handle.IsAllocated && handle.Target is WaylandShmBuffer self)
					self.MarkReleased();
			}

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void ReleaseHandler(nint data, nint buffer);
		}
	}
}
#endif
