#if LINUX
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal enum CosmicCaptureStatus
	{
		Unavailable,
		Failed,
		DeniedOrStopped,
		Captured
	}

	/// <summary>
	/// Direct output capture through the staging ext-image-capture-source and ext-image-copy-capture protocols.
	/// COSMIC exposes these globals to ordinary desktop clients, but filters them from security-context sandboxed
	/// clients. In the latter case this client returns null and the caller can use the desktop portal instead.
	/// </summary>
	internal static class CosmicImageCapture
	{
		private const string CaptureManagerName = "ext_image_copy_capture_manager_v1";
		private const string OutputSourceManagerName = "ext_output_image_capture_source_manager_v1";
		private const string OutputName = "wl_output";
		private const string ShmName = "wl_shm";
		private const string XdgOutputManagerName = "zxdg_output_manager_v1";

		// The fourcc-valued wl_shm formats which COSMIC advertises for image-copy capture.
		internal const uint WlShmFormatAbgr8888 = 0x34324241; // AB24: memory is R,G,B,A on little-endian hosts.
		internal const uint WlShmFormatXbgr8888 = 0x34324258; // XB24: memory is R,G,B,X on little-endian hosts.

		private const uint OutputSourceCreateOpcode = 0;
		private const uint OutputSourceManagerDestroyOpcode = 1;
		private const uint CaptureManagerCreateSessionOpcode = 0;
		private const uint CaptureManagerDestroyOpcode = 2;
		private const uint SourceDestroyOpcode = 0;
		private const uint SessionCreateFrameOpcode = 0;
		private const uint SessionDestroyOpcode = 1;
		private const uint FrameDestroyOpcode = 0;
		private const uint FrameAttachBufferOpcode = 1;
		private const uint FrameDamageBufferOpcode = 2;
		private const uint FrameCaptureOpcode = 3;
		private static readonly object sync = new();
		private static readonly RetryGate clientProbes = new(maximumAttempts: 3,
			initialRetryDelay: TimeSpan.FromMilliseconds(200), maximumRetryDelay: TimeSpan.FromSeconds(2));
		private static Client current;
		private static bool? lastAvailability;

		internal static bool WasAvailable
		{
			get
			{
				lock (sync)
					return lastAvailability == true;
			}
		}

		internal static bool IsAvailable()
		{
			lock (sync)
			{
				try
				{
					lastAvailability = EnsureClient();
					return lastAvailability.Value;
				}
				catch (Exception ex)
				{
					lastAvailability = false;
					WaylandBridgeDiagnostics.Failure("COSMIC image-copy", "probe",
						WaylandBridgeDiagnostics.Describe(ex));
					return false;
				}
			}
		}

		internal static void Reset()
		{
			Client retired;

			lock (sync)
			{
				retired = current;
				current = null;
				lastAvailability = null;
				clientProbes.Rearm();
			}

			retired?.Dispose();
		}

		internal static CosmicCaptureStatus Capture(ScreenRect bounds, Func<bool> authorize, out Bitmap bitmap)
		{
			bitmap = null;

			if (!bounds.HasArea || !BitConverter.IsLittleEndian)
				return CosmicCaptureStatus.Failed;

			lock (sync)
			{
				try
				{
					if (!EnsureClient())
						return CosmicCaptureStatus.Unavailable;

					if (authorize != null && !authorize())
						return CosmicCaptureStatus.DeniedOrStopped;

					var status = current.Capture(bounds, out bitmap);

					if (!current.IsUsable)
					{
						current.Dispose();
						current = null;
						lastAvailability = null;
						clientProbes.Rearm();
					}

					return status;
				}
				catch (Exception ex)
				{
					bitmap?.Dispose();
					bitmap = null;
					current?.Dispose();
					current = null;
					lastAvailability = null;
					clientProbes.Rearm();
					WaylandBridgeDiagnostics.Failure("COSMIC image-copy", "capture",
						WaylandBridgeDiagnostics.Describe(ex));
					return CosmicCaptureStatus.Failed;
				}
			}
		}

		private static bool EnsureClient()
		{
			if (current?.IsUsable == true)
				return true;

			current?.Dispose();
			current = null;
			using var probe = clientProbes.TryBegin();

			if (probe == null)
				return false;

			current = Client.TryOpen(out var unavailable);

			if (current != null)
				probe.Succeed();
			else if (unavailable)
				clientProbes.Suspend();

			lastAvailability = current != null;
			return lastAvailability.Value;
		}

		internal static PixelSize TransformedSize(int width, int height, uint transform)
			=> transform is 1u or 3u or 5u or 7u
				? new PixelSize(height, width) : new PixelSize(width, height);

		internal static bool TryChooseShmFormat(IEnumerable<uint> formats, out uint format)
		{
			format = 0;

			if (formats == null)
				return false;

			var available = formats as ISet<uint> ?? formats.ToHashSet();

			foreach (var candidate in new[]
					{
						WlShmFormatAbgr8888, WlShmFormatXbgr8888,
						WaylandNative.WlShmFormatArgb8888, WaylandNative.WlShmFormatXrgb8888
					})
				if (available.Contains(candidate))
				{
					format = candidate;
					return true;
				}

			return false;
		}

		private sealed class CaptureState
		{
			internal uint Width;
			internal uint Height;
			internal readonly HashSet<uint> ShmFormats = [];
			internal bool ConstraintsDone;
			internal bool Stopped;
			internal bool Ready;
			internal bool Failed;
			internal uint FailureReason;
			internal uint Transform;
		}

		private enum FrameCaptureStatus
		{
			Failed,
			RetryConstraints,
			DeniedOrStopped,
			Captured
		}

		private sealed class Client : IDisposable
		{
			private readonly nint display;
			private readonly GCHandle selfHandle;
			private readonly Dictionary<uint, WaylandOutput> outputsByName = [];
			private readonly Dictionary<uint, WaylandShmBuffer> buffersByOutput = [];
			private readonly HashSet<uint> deferredOutputRemovals = [];
			private nint registry;
			private nint shm;
			private nint captureManager;
			private nint outputSourceManager;
			private nint xdgOutputManager;
			private uint captureManagerName;
			private uint outputSourceManagerName;
			private uint shmName;
			private uint xdgOutputManagerName;
			private bool captureInProgress;
			private bool connectionLost;

			private Client(nint display)
			{
				this.display = display;
				selfHandle = GCHandle.Alloc(this);
			}

			internal bool IsUsable => !connectionLost && display != 0 && captureManager != 0
				&& outputSourceManager != 0 && shm != 0;

			internal static Client TryOpen(out bool unavailable)
			{
				unavailable = false;
				var display = WaylandNative.DisplayConnect(null);

				if (display == 0)
					return null;

				var client = new Client(display);

				try
				{
					client.registry = WaylandNative.DisplayGetRegistry(display);

					if (client.registry == 0 || WaylandNative.ProxyAddListener(client.registry,
							RegistryListener.Pointer, GCHandle.ToIntPtr(client.selfHandle)) != 0)
						return null;

					if (!WaylandDisplayPump.Roundtrip(display, 1000))
						return null;

					if (client.captureManager == 0 || client.outputSourceManager == 0 || client.shm == 0)
					{
						unavailable = true;
						return null;
					}

					// Populate wl_output modes and xdg-output logical positions before mapping capture buffers.
					if (!WaylandDisplayPump.Roundtrip(display, 1000))
						return null;

					var keep = client;
					client = null;
					return keep;
				}
				finally
				{
					client?.Dispose();
				}
			}

			internal CosmicCaptureStatus Capture(ScreenRect requested, out Bitmap bitmap)
			{
				bitmap = null;
				var captures = new List<(ScreenRect Bounds, Bitmap Pixels)>();

				try
				{
					if (!DispatchPending())
						return CosmicCaptureStatus.Failed;

					var targets = outputsByName.Values
						.Select(output => (Output: output, Bounds: output.Bounds,
							Intersection: requested.Intersect(output.Bounds)))
						.Where(target => target.Output.Done && target.Bounds.HasArea && target.Intersection.HasArea)
						.ToArray();
					captureInProgress = true;

					foreach (var target in targets)
					{
						var status = CaptureOutput(target.Output, target.Bounds, target.Intersection,
							out var segment);

						if (status != CosmicCaptureStatus.Captured)
							return status;

						if (segment == null || segment.Width <= 0 || segment.Height <= 0)
						{
							segment?.Dispose();
							return CosmicCaptureStatus.Failed;
						}

						captures.Add((target.Intersection, segment));
					}

					if (captures.Count == 0)
						return CosmicCaptureStatus.Failed;

					bitmap = ScreenCaptureComposer.Compose(requested, captures);
					return bitmap != null ? CosmicCaptureStatus.Captured : CosmicCaptureStatus.Failed;
				}
				finally
				{
					foreach (var capture in captures)
						capture.Pixels.Dispose();

					captureInProgress = false;

					foreach (var name in deferredOutputRemovals.ToArray())
						RemoveOutputCore(name);

					deferredOutputRemovals.Clear();
				}
			}

			private CosmicCaptureStatus CaptureOutput(WaylandOutput output, ScreenRect outputBounds,
				ScreenRect intersection, out Bitmap bitmap)
			{
				bitmap = null;

				for (var attempt = 0; attempt < 2; attempt++)
				{
					var status = CaptureOutputOnce(output, outputBounds, intersection, out bitmap);

					if (status == FrameCaptureStatus.Captured)
						return CosmicCaptureStatus.Captured;

					if (status == FrameCaptureStatus.DeniedOrStopped)
						return CosmicCaptureStatus.DeniedOrStopped;

					if (status != FrameCaptureStatus.RetryConstraints)
						return CosmicCaptureStatus.Failed;

					RetireBuffer(output.RegistryName);
				}

				return CosmicCaptureStatus.Failed;
			}

			private FrameCaptureStatus CaptureOutputOnce(WaylandOutput output, ScreenRect outputBounds,
				ScreenRect intersection, out Bitmap bitmap)
			{
				bitmap = null;
				var source = WaylandNative.MarshalConstructorObject(outputSourceManager,
					OutputSourceCreateOpcode, Interfaces.Source.Pointer, 1, 0, 0, output.Proxy);

				if (source == 0)
					return FrameCaptureStatus.Failed;

				nint session = 0;
				nint frame = 0;
				GCHandle stateHandle = default;

				try
				{
					session = MarshalConstructorObjectU(captureManager, CaptureManagerCreateSessionOpcode,
						Interfaces.Session.Pointer, 1, 0, 0, source, 0);

					if (session == 0)
						return FrameCaptureStatus.Failed;

					var state = new CaptureState();
					stateHandle = GCHandle.Alloc(state);
					var data = GCHandle.ToIntPtr(stateHandle);

					if (WaylandNative.ProxyAddListener(session, SessionListener.Pointer, data) != 0
							|| !DispatchUntil(() => state.ConstraintsDone || state.Stopped, 2000)
							|| state.Stopped
							|| state.Width == 0 || state.Height == 0
							|| state.Width > int.MaxValue || state.Height > int.MaxValue
							|| !TryChooseShmFormat(state.ShmFormats, out var format))
						return state.Stopped ? FrameCaptureStatus.DeniedOrStopped : FrameCaptureStatus.Failed;

					var buffer = GetBuffer(output.RegistryName, (int)state.Width, (int)state.Height, format);

					if (buffer == null)
						return FrameCaptureStatus.Failed;

					frame = WaylandNative.MarshalConstructor(session, SessionCreateFrameOpcode,
						Interfaces.Frame.Pointer, 1, 0, 0);

					if (frame == 0 || WaylandNative.ProxyAddListener(frame, FrameListener.Pointer, data) != 0)
						return FrameCaptureStatus.Failed;

					WaylandNative.MarshalObjectRequest(frame, FrameAttachBufferOpcode, 0, 1, 0, buffer.Buffer);
					WaylandNative.Marshal4I(frame, FrameDamageBufferOpcode, 0, 1, 0,
						0, 0, buffer.Width, buffer.Height);
					WaylandNative.MarshalRequest(frame, FrameCaptureOpcode, 0, 1, 0);
					_ = WaylandNative.DisplayFlush(display);

					if (!DispatchUntil(() => state.Ready || state.Failed || state.Stopped, 4000))
						return FrameCaptureStatus.Failed;

					if (state.Stopped || state.Failed && state.FailureReason == 2)
						return FrameCaptureStatus.DeniedOrStopped;

					if (state.Failed)
						return state.FailureReason == 1
							? FrameCaptureStatus.RetryConstraints : FrameCaptureStatus.Failed;

					if (!state.Ready)
						return FrameCaptureStatus.Failed;

					bitmap = BuildBitmapRegion(buffer.Data, buffer.Stride, buffer.Width, buffer.Height, format,
						state.Transform, outputBounds, intersection);
					return bitmap != null ? FrameCaptureStatus.Captured : FrameCaptureStatus.Failed;
				}
				finally
				{
					if (frame != 0)
						WaylandNative.MarshalRequest(frame, FrameDestroyOpcode, 0, 1, WaylandNative.DestroyFlag);

					if (session != 0)
						WaylandNative.MarshalRequest(session, SessionDestroyOpcode, 0, 1, WaylandNative.DestroyFlag);

					WaylandNative.MarshalRequest(source, SourceDestroyOpcode, 0, 1, WaylandNative.DestroyFlag);

					if (stateHandle.IsAllocated)
						stateHandle.Free();
				}
			}

			private WaylandShmBuffer GetBuffer(uint outputName, int width, int height, uint format)
			{
				if (buffersByOutput.TryGetValue(outputName, out var buffer) && buffer.Matches(width, height, format))
					return buffer;

				RetireBuffer(outputName);
				try
				{
					buffer = WaylandShmBuffer.Create(shm, width, height, format);
				}
				catch
				{
					return null;
				}

				if (buffer != null)
					buffersByOutput[outputName] = buffer;

				return buffer;
			}

			private void RetireBuffer(uint outputName)
			{
				if (buffersByOutput.Remove(outputName, out var buffer))
					buffer.Dispose();
			}

			private bool DispatchUntil(Func<bool> completed, int timeoutMs)
			{
				var result = WaylandDisplayPump.DispatchUntil(display, completed, timeoutMs);

				if (!result && WaylandNative.DisplayGetError(display) != 0)
					connectionLost = true;

				return result;
			}

			private bool DispatchPending()
			{
				var result = WaylandDisplayPump.DispatchPending(display);

				if (!result && WaylandNative.DisplayGetError(display) != 0)
					connectionLost = true;

				return result;
			}

			private void BindCaptureManager(uint name, uint version)
			{
				if (captureManager == 0)
				{
					captureManager = WaylandNative.RegistryBind(registry, name, Interfaces.Manager,
						Math.Min(version, 1u));

					if (captureManager != 0)
						captureManagerName = name;
				}
			}

			private void BindOutputSourceManager(uint name, uint version)
			{
				if (outputSourceManager == 0)
				{
					outputSourceManager = WaylandNative.RegistryBind(registry, name, Interfaces.OutputSourceManager,
						Math.Min(version, 1u));

					if (outputSourceManager != 0)
						outputSourceManagerName = name;
				}
			}

			private void BindShm(uint name, uint version)
			{
				if (shm == 0)
				{
					shm = WaylandNative.RegistryBind(registry, name, WaylandNative.ShmInterface, ShmName,
						Math.Min(version, 1u));

					if (shm != 0)
						shmName = name;
				}
			}

			private void BindOutput(uint name, uint version)
			{
				if (outputsByName.ContainsKey(name))
					return;

				var output = WaylandOutputBinding.Bind(registry, name, version, xdgOutputManager);

				if (output != null)
					outputsByName.Add(name, output);
			}

			private void BindXdgOutputManager(uint name, uint version)
			{
				if (xdgOutputManager != 0)
					return;

				xdgOutputManager = WaylandNative.RegistryBind(registry, name,
					WaylandNative.Interfaces.XdgOutputManager, Math.Min(version, 3u));

				if (xdgOutputManager != 0)
					xdgOutputManagerName = name;

				foreach (var output in outputsByName.Values)
					WaylandOutputBinding.BindXdgOutput(output, xdgOutputManager);
			}

			private void RemoveOutput(uint name)
			{
				if (captureInProgress)
				{
					deferredOutputRemovals.Add(name);
					return;
				}

				RemoveOutputCore(name);
			}

			private void RemoveOutputCore(uint name)
			{
				RetireBuffer(name);

				if (outputsByName.Remove(name, out var output))
					WaylandOutputBinding.Release(output);
			}

			private void RemoveGlobal(uint name)
			{
				if (outputsByName.ContainsKey(name))
				{
					RemoveOutput(name);
					return;
				}

				if (name == captureManagerName || name == outputSourceManagerName || name == shmName
						|| name == xdgOutputManagerName)
					connectionLost = true;
			}

			public void Dispose()
			{
				foreach (var buffer in buffersByOutput.Values)
					buffer.Dispose();

				buffersByOutput.Clear();

				foreach (var output in outputsByName.Values)
					WaylandOutputBinding.Release(output);

				outputsByName.Clear();

				if (xdgOutputManager != 0)
				{
					WaylandNative.XdgOutputManagerDestroy(xdgOutputManager);
					xdgOutputManager = 0;
				}

				if (outputSourceManager != 0)
				{
					WaylandNative.MarshalRequest(outputSourceManager, OutputSourceManagerDestroyOpcode,
						0, 1, WaylandNative.DestroyFlag);
					outputSourceManager = 0;
				}

				if (captureManager != 0)
				{
					WaylandNative.MarshalRequest(captureManager, CaptureManagerDestroyOpcode,
						0, 1, WaylandNative.DestroyFlag);
					captureManager = 0;
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

			private static Client Self(nint data) => (Client)GCHandle.FromIntPtr(data).Target;
			private static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? string.Empty;

			private static class RegistryListener
			{
				private static readonly GlobalHandler onGlobal = Global;
				private static readonly GlobalRemoveHandler onGlobalRemove = (data, _, name) => Self(data).RemoveGlobal(name);
				internal static readonly nint Pointer = WaylandListenerTable.Allocate(onGlobal, onGlobalRemove);

				private static void Global(nint data, nint registry, uint name, nint protocolInterface, uint version)
				{
					var client = Self(data);

					switch (Utf8(protocolInterface))
					{
						case CaptureManagerName: client.BindCaptureManager(name, version); break;
						case OutputSourceManagerName: client.BindOutputSourceManager(name, version); break;
						case ShmName: client.BindShm(name, version); break;
						case OutputName: client.BindOutput(name, version); break;
						case XdgOutputManagerName: client.BindXdgOutputManager(name, version); break;
					}
				}

				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void GlobalHandler(nint data, nint registry, uint name, nint protocolInterface, uint version);
				[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
				private delegate void GlobalRemoveHandler(nint data, nint registry, uint name);
			}
		}

		private static class SessionListener
		{
			private static readonly SizeHandler onBufferSize = (data, _, width, height) =>
			{
				var state = State(data);
				state.Width = width;
				state.Height = height;
			};
			private static readonly FormatHandler onShmFormat = (data, _, format) => State(data).ShmFormats.Add(format);
			private static readonly ArrayHandler onDmabufDevice = (_, _, _) => { };
			private static readonly FormatArrayHandler onDmabufFormat = (_, _, _, _) => { };
			private static readonly VoidHandler onDone = (data, _) => State(data).ConstraintsDone = true;
			private static readonly VoidHandler onStopped = (data, _) => State(data).Stopped = true;

			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onBufferSize, onShmFormat,
				onDmabufDevice, onDmabufFormat, onDone, onStopped);

			private static CaptureState State(nint data) => (CaptureState)GCHandle.FromIntPtr(data).Target;

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void SizeHandler(nint data, nint session, uint width, uint height);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void FormatHandler(nint data, nint session, uint format);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void ArrayHandler(nint data, nint session, nint array);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void FormatArrayHandler(nint data, nint session, uint format, nint array);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void VoidHandler(nint data, nint session);
		}

		private static class FrameListener
		{
			private static readonly TransformHandler onTransform = (data, _, transform) => State(data).Transform = transform;
			private static readonly DamageHandler onDamage = (_, _, _, _, _, _) => { };
			private static readonly PresentationHandler onPresentation = (_, _, _, _, _) => { };
			private static readonly VoidHandler onReady = (data, _) => State(data).Ready = true;
			private static readonly FailedHandler onFailed = (data, _, reason) =>
			{
				var state = State(data);
				state.Failed = true;
				state.FailureReason = reason;
			};

			internal static readonly nint Pointer = WaylandListenerTable.Allocate(onTransform, onDamage,
				onPresentation, onReady, onFailed);

			private static CaptureState State(nint data) => (CaptureState)GCHandle.FromIntPtr(data).Target;

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void TransformHandler(nint data, nint frame, uint transform);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void DamageHandler(nint data, nint frame, int x, int y, int width, int height);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void PresentationHandler(nint data, nint frame, uint secondsHigh, uint secondsLow, uint nanoseconds);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void VoidHandler(nint data, nint frame);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
			private delegate void FailedHandler(nint data, nint frame, uint reason);
		}

		private static Bitmap BuildBitmapRegion(nint source, int stride, int width, int height,
			uint format, uint transform, ScreenRect outputBounds, ScreenRect intersection)
		{
			var size = TransformedSize(width, height, transform);
			var region = outputBounds.ScreenToPixelBounds(intersection, size);
			return BuildBitmapRegion(source, stride, width, height, format, transform, region);
		}

		internal static unsafe bool TryReadPixel(uint[] source, int stridePixels, int width, int height,
			uint format, uint transform, int x, int y, out uint pixel)
		{
			pixel = 0;
			var size = TransformedSize(width, height, transform);

			if (source == null || stridePixels < width || stridePixels <= 0
					|| stridePixels > int.MaxValue / sizeof(uint) || width <= 0 || height <= 0
					|| (long)stridePixels * height > source.LongLength
					|| x < 0 || y < 0 || x >= size.Width || y >= size.Height)
				return false;

			var opaque = format == WlShmFormatXbgr8888 || format == WaylandNative.WlShmFormatXrgb8888;
			var swapRedBlue = format == WaylandNative.WlShmFormatArgb8888
				|| format == WaylandNative.WlShmFormatXrgb8888;

			fixed (uint* pixels = source)
				pixel = ReadPixel((nint)pixels, checked(stridePixels * sizeof(uint)), width, height,
					transform, x, y, swapRedBlue, opaque);

			return true;
		}

		private static unsafe Bitmap BuildBitmapRegion(nint source, int stride, int width, int height,
			uint format, uint transform, Rectangle region)
		{
			var size = TransformedSize(width, height, transform);

			if (source == 0 || stride < (long)width * sizeof(uint) || width <= 0 || height <= 0
					|| region.Width <= 0 || region.Height <= 0 || region.X < 0 || region.Y < 0
					|| (long)region.Right > size.Width || (long)region.Bottom > size.Height)
				return null;

			var opaque = format == WlShmFormatXbgr8888 || format == WaylandNative.WlShmFormatXrgb8888;
			var swapRedBlue = format == WaylandNative.WlShmFormatArgb8888
				|| format == WaylandNative.WlShmFormatXrgb8888;
			var bitmap = new Bitmap(region.Width, region.Height,
				opaque ? PixelFormat.Format32bppRgb : PixelFormat.Format32bppRgba);

			try
			{
				using var data = bitmap.Lock();

				if (transform == 0 && format == WlShmFormatAbgr8888)
				{
					var rowBytes = (long)region.Width * sizeof(uint);

					for (var y = 0; y < region.Height; y++)
					{
						var sourceRow = (byte*)source + (long)(region.Y + y) * stride
							+ (long)region.X * sizeof(uint);
						var destinationRow = (byte*)data.Data + (long)y * data.ScanWidth;
						System.Buffer.MemoryCopy(sourceRow, destinationRow, rowBytes, rowBytes);
					}
				}
				else
					for (var y = 0; y < region.Height; y++)
					{
						var destinationRow = (uint*)((byte*)data.Data + (long)y * data.ScanWidth);

						for (var x = 0; x < region.Width; x++)
						{
							destinationRow[x] = ReadPixel(source, stride, width, height, transform,
								region.X + x, region.Y + y, swapRedBlue, opaque);
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

		private static unsafe uint ReadPixel(nint source, int stride, int width, int height, uint transform,
			int x, int y, bool swapRedBlue, bool opaque)
		{
			var sourcePoint = InverseTransformPoint(x, y, width, height, transform);
			var sourceRow = (uint*)((byte*)source + (long)sourcePoint.Y * stride);
			return ConvertPixel(sourceRow[sourcePoint.X], swapRedBlue, opaque);
		}

		internal static Point InverseTransformPoint(int x, int y, int width, int height, uint transform)
			=> transform switch
			{
				0 => new Point(x, y),
				1 => new Point(y, height - 1 - x),
				2 => new Point(width - 1 - x, height - 1 - y),
				3 => new Point(width - 1 - y, x),
				4 => new Point(width - 1 - x, y),
				5 => new Point(y, x),
				6 => new Point(x, height - 1 - y),
				7 => new Point(width - 1 - y, height - 1 - x),
				_ => new Point(x, y),
			};

		private static uint ConvertPixel(uint pixel, bool swapRedBlue, bool opaque)
		{
			if (!swapRedBlue)
				return opaque ? pixel | 0xFF000000u : pixel;

			var alpha = opaque ? 0xFFu : pixel >> 24;
			return (alpha << 24) | ((pixel & 0xFFu) << 16) | (pixel & 0xFF00u)
				| ((pixel >> 16) & 0xFFu);
		}

		private static class Interfaces
		{
			internal static readonly WaylandNative.ProtocolInterface Source = new("ext_image_capture_source_v1", 1,
				[("destroy", "", [])], []);

			internal static readonly WaylandNative.ProtocolInterface Frame = new("ext_image_copy_capture_frame_v1", 1,
				[
					("destroy", "", []),
					("attach_buffer", "o", [WaylandNative.BufferInterface]),
					("damage_buffer", "iiii", []),
					("capture", "", [])
				],
				[
					("transform", "u", []),
					("damage", "iiii", []),
					("presentation_time", "uuu", []),
					("ready", "", []),
					("failed", "u", [])
				]);

			internal static readonly WaylandNative.ProtocolInterface Session = new("ext_image_copy_capture_session_v1", 1,
				[
					("create_frame", "n", [Frame.Pointer]),
					("destroy", "", [])
				],
				[
					("buffer_size", "uu", []),
					("shm_format", "u", []),
					("dmabuf_device", "a", []),
					("dmabuf_format", "ua", []),
					("done", "", []),
					("stopped", "", [])
				]);

			internal static readonly WaylandNative.ProtocolInterface CursorSession = new(
				"ext_image_copy_capture_cursor_session_v1", 1,
				[
					("destroy", "", []),
					("get_capture_session", "n", [Session.Pointer])
				],
				[
					("enter", "", []),
					("leave", "", []),
					("position", "ii", []),
					("hotspot", "ii", [])
				]);

			internal static readonly WaylandNative.ProtocolInterface Manager = new(CaptureManagerName, 1,
				[
					("create_session", "nou", [Session.Pointer, Source.Pointer, 0]),
					("create_pointer_cursor_session", "noo", [CursorSession.Pointer, Source.Pointer,
						WaylandNative.PointerInterface]),
					("destroy", "", [])
				], []);

			internal static readonly WaylandNative.ProtocolInterface OutputSourceManager = new(OutputSourceManagerName, 1,
				[
					("create_source", "no", [Source.Pointer, WaylandNative.OutputInterface]),
					("destroy", "", [])
				], []);
		}

		[DllImport(WaylandNative.ClientLibrary, EntryPoint = "wl_proxy_marshal_flags",
			CallingConvention = CallingConvention.Cdecl)]
		private static extern nint MarshalConstructorObjectU(nint proxy, uint opcode, nint protocolInterface,
			uint version, uint flags, nint newId, nint source, uint options);
	}
}
#endif
