#if LINUX
using System.IO;
using System.Runtime.InteropServices;
using Eto.Drawing;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Screen capture via zwlr_screencopy_unstable_v1. A serialized session keeps its Wayland connection,
	/// registry, wl_shm and output topology across successful captures, then reconnects after a failed capture.
	/// It picks the output whose logical geometry
	/// contains the requested rect, copies one frame into a memfd-backed SHM
	/// buffer, and converts the result into an Eto <see cref="Bitmap"/>.
	///
	/// Returns null whenever the compositor doesn't advertise the protocol
	/// (GNOME, KDE without wlroots, ...). Callers fall back to whatever capture
	/// path they used before.
	///
	/// Targets protocol version 3 (sway, hyprland, Wayfire, labwc, river, KWin
	/// with wlroots-protocols enabled), but accepts whatever version the compositor advertises down to 1. Version 3
	/// waits for buffer_done before selecting the advertised SHM format, as required by the protocol.
	/// </summary>
	internal static class WaylandScreenCapture
	{
		private static readonly object sync = new();
		private static readonly RetryGate sessionProbes = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(200), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static readonly RetryGate captureAttempts = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(200), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static Session current;

		private const string ScreencopyManagerName = "zwlr_screencopy_manager_v1";
		private const string ScreencopyFrameName = "zwlr_screencopy_frame_v1";
		private const string ShmName = "wl_shm";
		private const string OutputName = "wl_output";
		private const string XdgOutputManagerName = "zxdg_output_manager_v1";

		private const uint ScreencopyCaptureOutputRegionOpcode = 1;
		private const uint ScreencopyFrameCopyOpcode = 0;
		private const uint ScreencopyFrameDestroyOpcode = 1;

		/// <summary>
		/// wlroots zwlr_screencopy region grab. The compositor flavor is already resolved by the
		/// <c>WlrootsScreen</c> impl (the only caller), so there is no backend dispatch here — just the protocol.
		/// KWin/GNOME region grabs go through <c>HelperClient</c> directly from their own IScreen impls.
		/// </summary>
		internal static Bitmap TryCapture(int x, int y, int w, int h)
		{
			if (w <= 0 || h <= 0)
				return null;

			lock (sync)
			{
				if (current == null)
				{
					using var probe = sessionProbes.TryBegin();

					if (probe == null)
						return null;

					current = Session.TryOpen(out var unavailable);

					if (current != null)
					{
						probe.Succeed();
						captureAttempts.Rearm();
					}
					else if (unavailable)
						sessionProbes.Suspend();
				}

				if (current == null)
					return null;

				using var capture = captureAttempts.TryBegin();

				if (capture == null)
					return null;

				var result = RunWithReusableSession(ref current,
					session => (session.Capture(x, y, w, h), session.IsUsable));

				if (result != null)
					capture.Succeed();

				if (current == null)
					sessionProbes.Rearm();

				return result;
			}
		}

		internal static TResult RunWithReusableSession<TSession, TResult>(ref TSession session,
			Func<TSession, (TResult Result, bool KeepSession)> operation)
			where TSession : class, IDisposable
			where TResult : class
		{
			TResult result = null;
			var keepSession = session != null;

			try
			{
				if (session != null)
					(result, keepSession) = operation(session);
			}
			catch { keepSession = false; }

			if (!keepSession)
			{
				session?.Dispose();
				session = null;
			}

			return result;
		}

		internal static void Reset()
		{
			Session retired;

			lock (sync)
			{
				retired = current;
				current = null;
				sessionProbes.Rearm();
				captureAttempts.Rearm();
			}

			retired?.Dispose();
		}

		private sealed class FrameState
		{
			internal uint Format;
			internal int Width;
			internal int Height;
			internal int Stride;
			internal bool BufferInfoReady;
			internal bool BufferDone;
			internal bool Ready;
			internal bool Failed;
			internal uint Flags;
		}

		private sealed class Session : IDisposable
		{
			private readonly nint display;
			private readonly GCHandle selfHandle;
			private readonly Dictionary<uint, WaylandOutput> outputsByName = [];
			private nint registry;
			private nint shm;
			private nint xdgOutputManager;
			private nint screencopyManager;
			private uint screencopyManagerVersion;
			private bool connectionLost;

			private Session(nint display)
			{
				this.display = display;
				selfHandle = GCHandle.Alloc(this);
			}

			internal bool IsUsable => !connectionLost && display != 0 && screencopyManager != 0 && shm != 0;

			internal static Session TryOpen(out bool unavailable)
			{
				unavailable = false;
				var display = WaylandNative.DisplayConnect(null);

				if (display == 0)
					return null;

				var self = new Session(display);

				try
				{
					self.registry = WaylandNative.DisplayGetRegistry(display);

					if (self.registry == 0 || WaylandNative.ProxyAddListener(self.registry, RegistryListener.Pointer, GCHandle.ToIntPtr(self.selfHandle)) != 0)
						return null;

					if (WaylandNative.DisplayRoundtrip(display) < 0)
						return null;

					if (self.screencopyManager == 0 || self.shm == 0 || self.outputsByName.Count == 0)
					{
						unavailable = true;
						return null;
					}

					// Second roundtrip delivers each wl_output's geometry / mode / done events
					// so we know each monitor's logical position and size.
					if (WaylandNative.DisplayRoundtrip(display) < 0)
						return null;

					var keep = self;
					self = null;
					return keep;
				}
				finally
				{
					self?.Dispose();
				}
			}

			internal Bitmap Capture(int x, int y, int w, int h)
			{
				var captured = new List<(ScreenRect Bounds, Bitmap Pixels)>();

				try
				{
					foreach (var output in outputsByName.Values)
					{
						var outputBounds = output.Bounds;

						if (!output.Done || !outputBounds.HasArea)
							continue;

						var left = Math.Max(x, outputBounds.X);
						var top = Math.Max(y, outputBounds.Y);
						var right = Math.Min((long)x + w, outputBounds.Right);
						var bottom = Math.Min((long)y + h, outputBounds.Bottom);

						if (right <= left || bottom <= top)
							continue;

						var segmentWidth = (int)(right - left);
						var segmentHeight = (int)(bottom - top);
						var image = CaptureOutput(output, left - outputBounds.X, top - outputBounds.Y,
							segmentWidth, segmentHeight);

						if (image == null)
							return null;

						captured.Add((new ScreenRect(left, top, segmentWidth, segmentHeight), image));
					}

					if (captured.Count == 0)
						return null;

					return ScreenCaptureComposer.Compose(new ScreenRect(x, y, w, h), captured);
				}
				finally
				{
					foreach (var capture in captured)
						capture.Pixels.Dispose();
				}
			}

			private Bitmap CaptureOutput(WaylandOutput output, int localX, int localY, int w, int h)
			{

				var frame = WaylandNative.MarshalCaptureOutputRegion(
					screencopyManager,
					ScreencopyCaptureOutputRegionOpcode,
					ScreencopyInterfaces.Frame.Pointer,
					screencopyManagerVersion,
					0,
					0,
					0,
					output.Proxy,
					localX, localY, w, h);

				if (frame == 0)
					return null;

				var state = new FrameState();
				var stateHandle = GCHandle.Alloc(state);

				try
				{
					if (WaylandNative.ProxyAddListener(frame, FrameListener.Pointer, GCHandle.ToIntPtr(stateHandle)) != 0)
						return null;

					var needsBufferDone = screencopyManagerVersion >= 3;

					if (!DispatchUntil(() => state.Failed
							|| state.BufferInfoReady && (!needsBufferDone || state.BufferDone), 1500)
							|| state.Failed || !state.BufferInfoReady || needsBufferDone && !state.BufferDone)
						return null;

					if (state.Format != WaylandNative.WlShmFormatArgb8888 && state.Format != WaylandNative.WlShmFormatXrgb8888)
						return null;

					using var buffer = ShmFrameBuffer.Create(shm, state.Width, state.Height, state.Stride, state.Format);

					if (buffer == null)
						return null;

					WaylandNative.MarshalObjectRequest(frame, ScreencopyFrameCopyOpcode, 0, WaylandNative.ProxyGetVersion(frame), 0, buffer.Buffer);
					_ = WaylandNative.DisplayFlush(display);

					if (!DispatchUntil(() => state.Ready || state.Failed, 3000) || !state.Ready || state.Failed)
						return null;

					return BuildBitmap(buffer.Data, state.Stride, state.Width, state.Height, state.Format, (state.Flags & 1) != 0);
				}
				finally
				{
					WaylandNative.MarshalRequest(frame, ScreencopyFrameDestroyOpcode, 0, WaylandNative.ProxyGetVersion(frame), WaylandNative.DestroyFlag);

					if (stateHandle.IsAllocated)
						stateHandle.Free();
				}
			}

			private static unsafe Bitmap BuildBitmap(nint src, int srcStride, int width, int height, uint format, bool flipY)
			{
				// On Gtk, Bitmap(w,h,Format32bppRgba) is backed by a Gdk.Pixbuf whose memory layout
				// is R,G,B,A — *not* the B,G,R,A layout that wl_shm ARGB8888/XRGB8888 deliver. We
				// can't ask Eto for a BGRA-laid-out bitmap directly, so we swap R↔B per pixel here.
				// For XRGB8888 the source alpha byte is undefined; force 0xFF so the result is fully
				// opaque (BitmapDataHandler reports A=0 as transparent otherwise).
				var pixelFormat = format == WaylandNative.WlShmFormatXrgb8888 ? PixelFormat.Format32bppRgb : PixelFormat.Format32bppRgba;
				var bitmap = new Bitmap(width, height, pixelFormat);
				var opaqueAlpha = format == WaylandNative.WlShmFormatXrgb8888;

				try
				{
					using var data = bitmap.Lock();

					for (var row = 0; row < height; row++)
					{
						var srcRow = flipY ? height - 1 - row : row;
						var srcPtr = (uint*)((byte*)src + ((long)srcRow * srcStride));
						var dstPtr = (uint*)((byte*)data.Data + ((long)row * data.ScanWidth));

						for (var col = 0; col < width; col++)
						{
							var p = srcPtr[col];
							// p in little-endian int is A_R_G_B (high..low), bytes-in-memory B,G,R,A.
							// Repack to bytes-in-memory R,G,B,A i.e. int A_B_G_R.
							var a = opaqueAlpha ? 0xFFu : (p >> 24) & 0xFFu;
							var r = (p >> 16) & 0xFFu;
							var g = (p >> 8) & 0xFFu;
							var b = p & 0xFFu;
							dstPtr[col] = (a << 24) | (b << 16) | (g << 8) | r;
						}
					}

					return bitmap;
				}
				catch
				{
					bitmap.Dispose();
					throw;
				}
			}

			public void Dispose()
			{
				foreach (var output in outputsByName.Values)
					WaylandOutputBinding.Release(output);

				outputsByName.Clear();

				if (xdgOutputManager != 0)
				{
					WaylandNative.XdgOutputManagerDestroy(xdgOutputManager);
					xdgOutputManager = 0;
				}

				if (screencopyManager != 0)
				{
					WaylandNative.ProxyDestroy(screencopyManager);
					screencopyManager = 0;
				}

				if (shm != 0)
				{
					WaylandNative.ProxyDestroy(shm);
					shm = 0;
				}

				if (registry != 0)
				{
					WaylandNative.ProxyDestroy(registry);
					registry = 0;
				}

				if (selfHandle.IsAllocated)
					selfHandle.Free();

				if (display != 0)
					WaylandNative.DisplayDisconnect(display);
			}

			private void BindManager(uint name, uint version)
			{
				if (screencopyManager != 0)
					return;

				screencopyManagerVersion = Math.Min(version, 3u);
				screencopyManager = WaylandNative.RegistryBind(registry, name, ScreencopyInterfaces.Manager, screencopyManagerVersion);
			}

			private void BindShm(uint name, uint version)
			{
				if (shm == 0)
					shm = WaylandNative.RegistryBind(registry, name, WaylandNative.ShmInterface, ShmName, Math.Min(version, 1u));
			}

			private void BindOutput(uint name, uint version)
			{
				if (outputsByName.ContainsKey(name))
					return;

				var output = WaylandOutputBinding.Bind(registry, name, version, xdgOutputManager);

				if (output != null)
					outputsByName.Add(name, output);
			}

			private void RemoveOutput(uint name)
			{
				if (!outputsByName.Remove(name, out var output))
					return;

				WaylandOutputBinding.Release(output);
			}

			private bool DispatchUntil(Func<bool> completed, int timeoutMs)
			{
				var result = WaylandDisplayPump.DispatchUntil(display, completed, timeoutMs);

				if (!result && WaylandNative.DisplayGetError(display) != 0)
					connectionLost = true;

				return result;
			}

			private void BindXdgOutputManager(uint name, uint version)
			{
				if (xdgOutputManager != 0)
					return;

				xdgOutputManager = WaylandNative.RegistryBind(registry, name,
					WaylandNative.Interfaces.XdgOutputManager, Math.Min(version, 3u));

				foreach (var output in outputsByName.Values)
					WaylandOutputBinding.BindXdgOutput(output, xdgOutputManager);
			}

			private static Session Self(nint data) => (Session)GCHandle.FromIntPtr(data).Target;
			private static FrameState Frame(nint data) => (FrameState)GCHandle.FromIntPtr(data).Target;
			private static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? string.Empty;

			private static class RegistryListener
			{
				private static readonly GlobalHandler onGlobal = Global;
				private static readonly GlobalRemoveHandler onGlobalRemove = GlobalRemove;
				internal static readonly nint Pointer = WaylandListenerTable.Allocate(onGlobal, onGlobalRemove);

				private static void Global(nint data, nint registry, uint name, nint protocolInterface, uint version)
				{
					var session = Self(data);

					switch (Utf8(protocolInterface))
					{
						case ScreencopyManagerName: session.BindManager(name, version); break;
						case ShmName: session.BindShm(name, version); break;
						case OutputName: session.BindOutput(name, version); break;
						case XdgOutputManagerName: session.BindXdgOutputManager(name, version); break;
					}
				}

				private static void GlobalRemove(nint data, nint registry, uint name) => Self(data).RemoveOutput(name);

				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void GlobalHandler(nint data, nint registry, uint name, nint protocolInterface, uint version);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void GlobalRemoveHandler(nint data, nint registry, uint name);
			}

			private static class FrameListener
			{
				private static readonly BufferHandler onBuffer = (data, _, format, width, height, stride) =>
				{
					var state = Frame(data);
					state.Format = format;
					state.Width = (int)width;
					state.Height = (int)height;
					state.Stride = (int)stride;
					state.BufferInfoReady = true;
				};

				private static readonly FlagsHandler onFlags = (data, _, flags) => Frame(data).Flags = flags;
				private static readonly ReadyHandler onReady = (data, _, _, _, _) => Frame(data).Ready = true;
				private static readonly VoidHandler onFailed = (data, _) => Frame(data).Failed = true;
				private static readonly DamageHandler onDamage = (data, _, _, _, _, _) => { };
				private static readonly DmabufHandler onDmabuf = (data, _, _, _, _) => { };
				private static readonly VoidHandler onBufferDone = (data, _) => Frame(data).BufferDone = true;

				internal static readonly nint Pointer = WaylandListenerTable.Allocate(onBuffer, onFlags, onReady, onFailed, onDamage, onDmabuf, onBufferDone);

				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void BufferHandler(nint data, nint frame, uint format, uint width, uint height, uint stride);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void FlagsHandler(nint data, nint frame, uint flags);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void ReadyHandler(nint data, nint frame, uint tvSecHi, uint tvSecLo, uint tvNsec);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void VoidHandler(nint data, nint frame);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void DamageHandler(nint data, nint frame, uint x, uint y, uint width, uint height);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void DmabufHandler(nint data, nint frame, uint format, uint width, uint height);
			}

		}

		// Local resource wrapper for the single-shot SHM buffer used during capture. Distinct from
		// WaylandShmBuffer because we don't need its wl_buffer.release listener and the stride
		// comes from the compositor (not assumed to be width*4).
		private sealed class ShmFrameBuffer : IDisposable
		{
			private int fd = -1;
			private nint mapping;
			private nuint mapLength;
			private nint pool;
			internal nint Buffer { get; private set; }
			internal nint Data => mapping;

			internal static ShmFrameBuffer Create(nint shm, int width, int height, int stride, uint format)
			{
				if (shm == 0 || width <= 0 || height <= 0 || stride < (long)width * 4)
					return null;

				var size = (long)stride * height;

				if (size <= 0 || size > int.MaxValue)
					return null;

				var fb = new ShmFrameBuffer();

				try
				{
					fb.fd = WaylandNative.MemfdCreate("keysharp-wlr-screencopy", WaylandNative.MFD_CLOEXEC);

					if (fb.fd < 0)
						return null;

					if (WaylandNative.Ftruncate(fb.fd, size) != 0)
						return null;

					fb.mapping = WaylandNative.Mmap(0, (nuint)size, WaylandNative.PROT_READ | WaylandNative.PROT_WRITE,
						WaylandNative.MAP_SHARED, fb.fd, 0);

					if (fb.mapping == WaylandNative.MAP_FAILED)
					{
						fb.mapping = 0;
						return null;
					}

					fb.mapLength = (nuint)size;
					fb.pool = WaylandNative.ShmCreatePool(shm, fb.fd, (int)size);

					if (fb.pool == 0)
						return null;

					fb.Buffer = WaylandNative.ShmPoolCreateBuffer(fb.pool, 0, width, height, stride, format);

					if (fb.Buffer == 0)
						return null;

					var keep = fb;
					fb = null;
					return keep;
				}
				finally
				{
					fb?.Dispose();
				}
			}

			public void Dispose()
			{
				if (Buffer != 0)
				{
					WaylandNative.BufferDestroy(Buffer);
					Buffer = 0;
				}

				if (pool != 0)
				{
					WaylandNative.ShmPoolDestroy(pool);
					pool = 0;
				}

				if (mapping != 0)
				{
					_ = WaylandNative.Munmap(mapping, mapLength);
					mapping = 0;
				}

				if (fd >= 0)
				{
					_ = WaylandNative.Close(fd);
					fd = -1;
				}
			}
		}

		private static class ScreencopyInterfaces
		{
			// Wire descriptions for zwlr_screencopy_manager_v1 and zwlr_screencopy_frame_v1. Built
			// locally because libwayland only exports symbols for the core protocol.
			internal static readonly WaylandNative.ProtocolInterface Frame = new(ScreencopyFrameName, 3,
				[
					("copy", "o", [0]),
					("destroy", "", []),
					("copy_with_damage", "2o", [0])
				],
				[
					("buffer", "uuuu", []),
					("flags", "u", []),
					("ready", "uuu", []),
					("failed", "", []),
					("damage", "2uuuu", []),
					("linux_dmabuf", "3uuu", []),
					("buffer_done", "3", [])
				]);

			internal static readonly WaylandNative.ProtocolInterface Manager = new(ScreencopyManagerName, 3,
				[
					("capture_output", "nio", [Frame.Pointer, 0, 0]),
					("capture_output_region", "nioiiii", [Frame.Pointer, 0, 0]),
					("destroy", "3", [])
				],
				[]);
		}
	}
}
#endif
