using System.Runtime.InteropServices;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// One ARGB8888 buffer backed by a memfd-backed wl_shm_pool. Lifecycle:
	///   1. Create() allocates the memfd, maps it, and creates wl_shm_pool and wl_buffer proxies.
	///   2. Caller writes pixels into Data.
	///   3. Surface attaches Buffer, damages, and commits; the compositor reads from the same mapping.
	///   4. Dispose() destroys both proxies, unmaps, and closes the fd after compositor release.
	///
	/// Buffers must not be disposed while the compositor still owns them. Callers should keep the
	/// buffer alive until either (a) a newer buffer has been attached and the compositor has
	/// released this one (via the wl_buffer.release event). Destroying a surface does not make it safe to unmap an
	/// in-flight buffer immediately; Dispose therefore retires it and lets the release callback finish cleanup.
	/// </summary>
	internal sealed class WaylandShmBuffer : IDisposable
	{
		internal int Width { get; }
		internal int Height { get; }
		internal int Stride { get; }
		internal uint Format { get; }
		internal nint Buffer { get; private set; }
		internal nint Data { get; private set; }
		private nuint MapLength { get; }

		private int fd;
		private nint pool;
		private GCHandle listenerHandle;
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

		private WaylandShmBuffer(int fd, nint mapping, nuint mapLength, nint pool, nint buffer,
			int width, int height, int stride, uint format)
		{
			this.fd = fd;
			Data = mapping;
			MapLength = mapLength;
			this.pool = pool;
			Buffer = buffer;
			Width = width;
			Height = height;
			Stride = stride;
			Format = format;
			listenerHandle = GCHandle.Alloc(this);
			if (WaylandNative.ProxyAddListener(buffer, ReleaseListener.Pointer, GCHandle.ToIntPtr(listenerHandle)) != 0)
			{
				listenerHandle.Free();
				Buffer = 0;
				throw new IOException("wl_buffer listener setup failed.");
			}
		}

		/// <summary>
		/// Allocates a new ARGB8888 buffer of the requested pixel size. The compositor must already
		/// have advertised wl_shm.format for ARGB8888, which the protocol requires.
		/// </summary>
		internal static WaylandShmBuffer Create(nint shm, int width, int height, uint format = WaylandNative.WlShmFormatArgb8888)
		{
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

				buffer = WaylandNative.ShmPoolCreateBuffer(pool, 0, width, height, stride, format);

				if (buffer == 0)
					throw new IOException("wl_shm_pool.create_buffer returned null.");

				// The wl_buffer and mmap outlive their creation pool; neither the pool proxy nor the local fd
				// needs to remain open for a cached buffer.
				WaylandNative.ShmPoolDestroy(pool);
				pool = 0;
				_ = WaylandNative.Close(fd);
				fd = -1;
				var result = new WaylandShmBuffer(fd, mapping, (nuint)size, pool, buffer, width, height, stride,
					format);
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

			if (disposePending)
				DisposeReleased();
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

			DisposeReleased();
		}

		/// <summary>Force-releases local resources after the display dispatcher has stopped. Protocol objects are
		/// deliberately not destroyed; wl_display_disconnect owns them and no release callback can still arrive.</summary>
		internal void Abandon() => DisposeCore(destroyProxies: false);

		private void DisposeReleased()
		{
			DisposeCore(destroyProxies: true);
		}

		private void DisposeCore(bool destroyProxies)
		{
			if (disposed)
				return;

			disposePending = false;

			if (Buffer != 0 && destroyProxies)
			{
				WaylandNative.BufferDestroy(Buffer);
			}
			Buffer = 0;

			if (pool != 0 && destroyProxies)
			{
				WaylandNative.ShmPoolDestroy(pool);
			}
			pool = 0;

			if (Data != 0)
			{
				_ = WaylandNative.Munmap(Data, MapLength);
				Data = 0;
			}

			if (fd >= 0)
			{
				_ = WaylandNative.Close(fd);
				fd = -1;
			}

			if (listenerHandle.IsAllocated)
				listenerHandle.Free();

			disposed = true;
		}

		internal bool Matches(int width, int height, uint format)
			=> Buffer != 0 && Width == width && Height == height && Format == format;

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
