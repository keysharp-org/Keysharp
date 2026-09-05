#if LINUX
using System.Buffers;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal readonly record struct WaylandFrameDamage(DamageKind Kind, PixelRect Bounds)
	{
		internal static WaylandFrameDamage None => new(DamageKind.None, default);
		internal static WaylandFrameDamage All => new(DamageKind.All, default);

		internal static WaylandFrameDamage Region(PixelRect bounds)
			=> bounds.IsEmpty ? None : new(DamageKind.Region, bounds);

		internal WaylandFrameDamage Merge(WaylandFrameDamage other)
		{
			if (Kind == DamageKind.All || other.Kind == DamageKind.All)
				return All;

			if (Kind == DamageKind.None)
				return other;

			if (other.Kind == DamageKind.None)
				return this;

			return Region(Bounds.Union(other.Bounds));
		}
	}

	internal readonly record struct WaylandBufferInterpretation(int Width, int Height, Rectangle Source, byte Opacity);

	internal sealed class WaylandDamageHistory
	{
		private readonly WaylandFrameDamage[] frames;
		private long nextFrame;

		internal WaylandDamageHistory(int capacity) => frames = new WaylandFrameDamage[capacity];

		internal WaylandFrameDamage Resolve(long lastPresentedFrame, WaylandFrameDamage current)
		{
			var age = nextFrame - lastPresentedFrame;

			if (lastPresentedFrame < 0 || age < 1 || age > frames.Length)
				return WaylandFrameDamage.All;

			var result = current;

			for (var frame = lastPresentedFrame + 1; frame < nextFrame; frame++)
				result = result.Merge(frames[(int)(frame % frames.Length)]);

			return result;
		}

		internal long Commit(WaylandFrameDamage damage)
		{
			var frame = nextFrame++;
			frames[(int)(frame % frames.Length)] = damage;
			return frame;
		}

		internal void Reset()
		{
			Array.Clear(frames);
			nextFrame = 0;
		}
	}

	/// <summary>
	/// Generic click-through image overlay backed by zwlr_layer_shell + wl_shm.
	/// Pixels are copied into a premultiplied ARGB8888 SHM buffer and displayed on the overlay layer.
	/// </summary>
	internal sealed class WaylandImageOverlay : IDisposable
	{
		// This string is both the layer-shell namespace and, on KWin, the surface's semantic scope.
		// KWin maps unknown scopes to a normal application window.  Using a private value here therefore
		// made our input-empty overlay look like an ordinary Keysharp window to windowAt/WinFromPoint,
		// causing own-PID guards to mistake it for an interactive popup.  "on-screen-display" is the
		// compositor-defined type which matches this passive, non-activating overlay surface.
		private const string LayerNamespace = "on-screen-display";
		private const int ConfigureTimeoutMs = 1000;

		private readonly WaylandLayerShellClient client;
		private readonly object stateSync = new();
		private WaylandLayerSurface surface;
		// Bounded triple buffering: in-flight buffers remain untouched until wl_buffer.release. If all three
		// are busy, the new frame is dropped and the last complete frame remains mapped.
		private readonly List<WaylandShmBuffer> bufferPool = new();
		private nint emptyRegion;
		private int marginLeft;
		private int marginTop;
		private WaylandShmBuffer preparedBuffer;
		private int preparedMarginLeft, preparedMarginTop;

		#region Partial updates

		private readonly WaylandDamageHistory damageHistory = new(WaylandBufferPoolPolicy.Capacity + 1);
		private WaylandBufferInterpretation? historyInterpretation;
		private WaylandFrameDamage preparedSemanticDamage;
		private WaylandFrameDamage preparedWriteDamage;

		private void ResetDamageHistory(int bufferWidth, int bufferHeight, Rectangle source, byte opacity)
		{
			var interpretation = new WaylandBufferInterpretation(bufferWidth, bufferHeight, source, opacity);

			if (historyInterpretation == interpretation)
				return;

			damageHistory.Reset();
			historyInterpretation = interpretation;

			// Every pooled buffer's recorded age describes a geometry that no longer exists.
			foreach (var b in bufferPool)
				b.LastPresentedFrame = -1;
		}

		#endregion
		private uint outputName;
		private bool disposed;
		private bool connectionInvalidated;

		internal nint Handle => surface?.Surface ?? 0;
		internal bool IsAvailable => !disposed && !connectionInvalidated && client.IsAvailable;

		// Pointer sink for an interactive overlay, set by LayerImageBacking with the fragment's segment offset
		// baked in. Invoked by the client's wl_pointer listener on its dispatcher thread with surface-local
		// logical coordinates; a plain delegate field read/written from different threads is a benign race
		// (either the old or new sink sees the event).
		internal Action<OverlayPointerKind, double, double> PointerSink;

		internal void DeliverPointer(OverlayPointerKind kind, double sx, double sy)
		{
			try { PointerSink?.Invoke(kind, sx, sy); }
			catch (Exception exception)
			{
				Diagnostics.Debug.WriteLine($"Wayland overlay pointer sink failed: {exception.Message}");
			}
		}

		internal WaylandImageOverlay(WaylandLayerShellClient client)
		{
			this.client = client ?? throw new ArgumentNullException(nameof(client));

			if (!client.Register(this))
				throw new IOException("The Wayland layer-shell connection is unavailable.");
		}

		// Returns true iff the overlay is now shown. False on a genuine layer-shell failure (surface could not be
		// created, or never configured within the timeout) so the caller can fall back to a visible Eto window
		// instead of recording a phantom "shown" overlay with nothing on screen.
		internal bool Show(Bitmap image, Rectangle sourcePixels, ScreenRect bounds,
			WaylandLayerShellClient.OutputTarget output, bool clickThrough,
			byte opacity = 255, DamageKind sourceDamageKind = DamageKind.All, PixelRect sourceDamage = default)
		{
			lock (stateSync)
				return PrepareCore(image, sourcePixels, bounds, output, clickThrough, opacity,
						sourceDamageKind, sourceDamage)
					   && CommitPreparedCore();
		}

		internal bool Prepare(Bitmap image, Rectangle sourcePixels, ScreenRect bounds,
			WaylandLayerShellClient.OutputTarget output, bool clickThrough,
			byte opacity = 255, DamageKind sourceDamageKind = DamageKind.All, PixelRect sourceDamage = default)
		{
			lock (stateSync)
				return PrepareCore(image, sourcePixels, bounds, output, clickThrough, opacity,
					sourceDamageKind, sourceDamage);
		}

		internal bool CommitPrepared()
		{
			lock (stateSync)
				return CommitPreparedCore();
		}

		private bool PrepareCore(Bitmap image, Rectangle sourcePixels, ScreenRect bounds,
			WaylandLayerShellClient.OutputTarget output, bool clickThrough, byte opacity,
			DamageKind sourceDamageKind, PixelRect sourceDamage)
		{
			if (disposed || connectionInvalidated || !client.IsAvailable)
				return false;

			if (surface?.IsClosed == true)
				TeardownSurface();

			if (image == null)
			{
				HideCore();
				return false;
			}

			if (bounds.Width <= 0) bounds = bounds with { Width = image.Width };
			if (bounds.Height <= 0) bounds = bounds with { Height = image.Height };

			if (!bounds.HasArea || !client.IsOutputCurrent(output))
			{
				HideCore();
				return false;
			}

			// A layer surface is permanently assigned to one wl_output, so crossing a monitor needs a fresh one.
			if (surface != null && output.RegistryName != outputName)
				TeardownSurface();

			EnsureSurface(output);

			if (surface == null)
				return false;

			var localX = bounds.X - output.Bounds.X;
			var localY = bounds.Y - output.Bounds.Y;
			var w = bounds.Width;
			var h = bounds.Height;

			// Prepare the complete replacement raster before touching pending surface geometry. Allocation or copy
			// failure therefore leaves the previous live frame and rectangle unchanged.
			var useViewport = client.Viewporter != 0;
			var bufferScale = Math.Max(1, output.IntegerScale);
			if (!TryResolvePixelLength(w, useViewport ? output.BufferScale : bufferScale, out var pixelWidth)
				|| !TryResolvePixelLength(h, useViewport ? output.BufferScale : bufferScale, out var pixelHeight))
				return false;

			var source = Rectangle.Intersect(sourcePixels, new Rectangle(0, 0, image.Width, image.Height));

			if (source.Width <= 0 || source.Height <= 0)
				return false;

			ResetDamageHistory(pixelWidth, pixelHeight, source, opacity);
			var target = AcquireBuffer(pixelWidth, pixelHeight);

			if (target == null)
				return false;

			var oneToOne = pixelWidth == source.Width && pixelHeight == source.Height;
			var semanticDamage = oneToOne
				? ToBufferDamage(sourceDamageKind, sourceDamage, source, pixelWidth, pixelHeight)
				: sourceDamageKind == DamageKind.None ? WaylandFrameDamage.None : WaylandFrameDamage.All;
			var writeDamage = damageHistory.Resolve(target.LastPresentedFrame, semanticDamage);

			if (writeDamage.Kind == DamageKind.All)
				CopyImageToBuffer(image, source, target, pixelWidth, pixelHeight, opacity);
			else if (writeDamage.Kind == DamageKind.Region)
				CopyImageRegionToBuffer(image, source, target, writeDamage.Bounds, opacity);

			preparedSemanticDamage = semanticDamage;
			preparedWriteDamage = writeDamage;
			var initiallyConfigured = surface.IsConfigured;
			surface.SetSize((uint)w, (uint)h);
			surface.SetMargin(localY, 0, 0, localX);
			surface.SetInputRegion(ResolveInputRegion(clickThrough, emptyRegion));
			_ = surface.ConfigureBufferMapping(w, h, bufferScale);

			if (!initiallyConfigured)
			{
				// Layer-shell requires one initial null-buffer commit and configure acknowledgement. Every later
				// resize combines geometry, viewport and replacement pixels in the single final commit below.
				if (!surface.Commit())
				{
					HideCore();
					return false;
				}

				if (!surface.WaitForConfigure(ConfigureTimeoutMs))
				{
					// The compositor never acked the initial configure — a real layer-shell failure. Tear the
					// surface down and report failure so the backing falls back to Eto rather than silently showing
					// nothing.
					HideCore();
					return false;
				}
			}

			preparedBuffer = target;
			preparedMarginLeft = localX;
			preparedMarginTop = localY;
			return true;
		}

		private bool CommitPreparedCore()
		{
			var target = preparedBuffer;

			if (target == null || surface == null || !client.IsAvailable)
				return false;

			preparedBuffer = null;
			var semanticDamage = preparedSemanticDamage;
			var writeDamage = preparedWriteDamage;
			preparedSemanticDamage = default;
			preparedWriteDamage = default;

			if (!surface.AttachBuffer(target, writeDamage) || !surface.Commit())
				return false;

			target.LastPresentedFrame = damageHistory.Commit(semanticDamage);
			marginLeft = preparedMarginLeft;
			marginTop = preparedMarginTop;
			return true;
		}

		/// <summary>
		/// Maps damage from the canvas's pixels into this fragment's buffer. Only meaningful when the buffer is
		/// 1:1 with <paramref name="source"/>; the caller checks that.
		/// </summary>
		private static WaylandFrameDamage ToBufferDamage(DamageKind kind, PixelRect canvasDamage,
			Rectangle source, int bufferWidth, int bufferHeight)
		{
			if (kind == DamageKind.All)
				return WaylandFrameDamage.All;

			if (kind == DamageKind.None)
				return WaylandFrameDamage.None;

			var clipped = canvasDamage.Intersect(new PixelRect(source.X, source.Y, source.Width, source.Height));
			var bounds = clipped.Offset(-source.X, -source.Y)
				.Intersect(new PixelRect(0, 0, bufferWidth, bufferHeight));
			return WaylandFrameDamage.Region(bounds);
		}

		private static bool TryResolvePixelLength(int logicalLength, double scale, out int pixels)
		{
			var value = Math.Round(logicalLength * scale);
			if (!double.IsFinite(value) || value < 1 || value > int.MaxValue)
			{
				pixels = 0;
				return false;
			}

			pixels = (int)value;
			return true;
		}

		// Reuse released buffers, reap stale sizes, and drop a frame once the bounded pool is full.
		private WaylandShmBuffer AcquireBuffer(int w, int h)
		{
			lock (WaylandLayerShellClient.Sync)
			{
				for (var i = bufferPool.Count - 1; i >= 0; i--)
				{
					var b = bufferPool[i];

					if ((b.Width != w || b.Height != h) && b.Released)
					{
						// A released buffer of the wrong size can never satisfy this frame.
						b.Dispose();
						bufferPool.RemoveAt(i);
					}
				}

				Span<WaylandBufferState> states = stackalloc WaylandBufferState[bufferPool.Count];

				for (var i = 0; i < bufferPool.Count; i++)
					states[i] = new WaylandBufferState(bufferPool[i].Width, bufferPool[i].Height,
						bufferPool[i].Released);

				var reusable = WaylandBufferPoolPolicy.FindReusable(states, w, h);

				if (reusable >= 0)
					return bufferPool[reusable];

				if (WaylandBufferPoolPolicy.CanAllocate(bufferPool.Count))
				{
					var chosen = WaylandShmBuffer.Create(client.Shm, w, h);
					bufferPool.Add(chosen);
					return chosen;
				}

				return null;
			}
		}

		internal static nint ResolveInputRegion(bool clickThrough, nint emptyRegion)
			=> clickThrough ? emptyRegion : 0;

		// Same-output reposition: one margin commit, no topology lookup, roundtrip, or pixel upload.
		internal bool Reposition(ScreenRect bounds, WaylandLayerShellClient.OutputTarget output)
		{
			lock (stateSync)
				return RepositionCore(bounds, output);
		}

		private bool RepositionCore(ScreenRect bounds, WaylandLayerShellClient.OutputTarget output)
		{
			if (disposed || connectionInvalidated || !client.IsAvailable || surface == null || !surface.IsConfigured || surface.IsClosed)
				return false;

			lock (WaylandLayerShellClient.Sync)
			{
				if (!client.IsOutputCurrent(output) || output.RegistryName != outputName)
					return false;

				var x = bounds.X - output.Bounds.X;
				var y = bounds.Y - output.Bounds.Y;

				if (x == marginLeft && y == marginTop)
					return true;

				surface.SetMargin(y, 0, 0, x);

				if (!surface.Commit() || !client.IsAvailable)
					return false;

				marginLeft = x;
				marginTop = y;
				return true;
			}
		}

		private void HideCore()
		{
			TeardownSurface(connectionInvalidated);
		}

		private void EnsureSurface(WaylandLayerShellClient.OutputTarget output)
		{
			if (surface != null || !client.IsAvailable)
				return;

			try
			{
				lock (WaylandLayerShellClient.Sync)
				{
					if (!client.IsOutputCurrent(output))
						return;

					surface = new WaylandLayerSurface(client, output.Proxy, WaylandNative.LayerOverlay, LayerNamespace);
				}
				outputName = output.RegistryName;
				surface.SetAnchor(WaylandNative.AnchorTop | WaylandNative.AnchorLeft);
				surface.SetExclusiveZone(-1);
				surface.SetKeyboardInteractivity(WaylandNative.KeyboardInteractivityNone);

				lock (WaylandLayerShellClient.Sync)
				{
					emptyRegion = WaylandNative.CompositorCreateRegion(client.Compositor);

					if (emptyRegion != 0 && surface.Surface != 0)
						WaylandNative.SurfaceSetInputRegion(surface.Surface, emptyRegion);
				}
			}
			catch
			{
				TeardownSurface();
			}
		}

		/// <summary>
		/// Writes only <paramref name="region"/> of <paramref name="target"/>, reading the matching pixels of
		/// <paramref name="source"/> 1:1. Falls back to a whole-buffer copy when the bitmap has no Cairo
		/// surface to read (nothing drawn yet, or a non-GTK backend).
		/// </summary>
		private static unsafe void CopyImageRegionToBuffer(Bitmap image, Rectangle source,
			WaylandShmBuffer target, PixelRect region, byte opacity)
		{
			if (image == null || target == null || target.Data == 0 || region.IsEmpty)
				return;

			if (image.Handler is not Eto.GtkSharp.Drawing.BitmapHandler handler
					|| handler.Surface is not { } surface || surface.Format != Cairo.Format.Argb32
					|| surface.DataPtr == 0)
			{
				CopyImageToBuffer(image, source, target, target.Width, target.Height, opacity);
				return;
			}

			surface.Flush();
			var srcBase = (byte*)surface.DataPtr;
			var srcStride = surface.Stride;
			var dstBase = (uint*)target.Data;
			var dstStride = target.Stride / 4;

			for (var y = region.Y; y < region.Bottom; y++)
			{
				var srcRow = (uint*)(srcBase + (long)(source.Y + y) * srcStride) + source.X + region.X;
				var dstRow = dstBase + (long)y * dstStride + region.X;

				if (opacity == 255)
					Buffer.MemoryCopy(srcRow, dstRow, (long)region.Width * 4, (long)region.Width * 4);
				else
					for (var x = 0; x < region.Width; x++)
						dstRow[x] = ScalePremultiplied(srcRow[x], opacity);
			}
		}

		// Both the source and the shm buffer are premultiplied, so a constant alpha scales all four channels
		// alike — including alpha itself, which is what keeps the result premultiplied.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static uint ScalePremultiplied(uint argb, byte opacity)
		{
			if (opacity == 255)
				return argb;

			if (opacity == 0)
				return 0;

			var a = ((argb >> 24) & 0xFF) * opacity / 255;
			var r = ((argb >> 16) & 0xFF) * opacity / 255;
			var g = ((argb >> 8) & 0xFF) * opacity / 255;
			var b = (argb & 0xFF) * opacity / 255;
			return (a << 24) | (r << 16) | (g << 8) | b;
		}

		private static unsafe void CopyImageToBuffer(Bitmap image, Rectangle sourcePixels,
			WaylandShmBuffer target, int width, int height, byte opacity = 255)
		{
			if (image == null || target == null || target.Data == 0)
				return;

			var source = Rectangle.Intersect(sourcePixels, new Rectangle(0, 0, image.Width, image.Height));

			if (source.Width <= 0 || source.Height <= 0)
				return;

			// Cairo's ARGB32 surface is current, premultiplied, and matches the SHM byte layout. Reading it directly
			// avoids the lossy surface-to-pixbuf conversion performed by Lock().
			if (CopyFromCairoSurface(image, source, target, width, height, opacity))
				return;

			var src32 = ImageHelper.EnsureOpaque32Bpp(image);
			int[] sourceXs = null;

			try
			{
				sourceXs = ArrayPool<int>.Shared.Rent(width);
				using var data = src32.Lock();
				var srcBase = (byte*)data.Data;
				var srcStride = data.ScanWidth;
				var srcBpp = data.BytesPerPixel;
				var dstBase = (uint*)target.Data;
				var dstStride = target.Stride / 4;

				for (var x = 0; x < width; x++)
					sourceXs[x] = source.X + SampleIndex(x, width, source.Width);

				// Probe the backend channel order once. Known layouts avoid virtual translation per pixel;
				// an unfamiliar or premultiplied layout takes the exact fallback below.
				const int marker = unchecked((int)0x80102030);
				var translated = (uint)data.TranslateDataToArgb(marker);
				var identity = translated == 0x80102030u;
				var rbSwap = translated == 0x80302010u;

				for (var y = 0; y < height; y++)
				{
					var sourceY = source.Y + SampleIndex(y, height, source.Height);
					var srcRow = srcBase + (long)sourceY * srcStride;
					var dstRow = dstBase + y * dstStride;

					for (var x = 0; x < width; x++)
					{
						var raw = (uint)*(int*)(srcRow + sourceXs[x] * srcBpp);
						uint argb;

						if (rbSwap)
							argb = (raw & 0xFF00FF00u) | ((raw >> 16) & 0xFFu) | ((raw & 0xFFu) << 16);
						else if (identity)
							argb = raw;
						else
							argb = (uint)data.TranslateDataToArgb((int)raw);

						dstRow[x] = ScalePremultiplied(Premultiply(argb), opacity);
					}
				}
			}
			finally
			{
				if (sourceXs != null)
					ArrayPool<int>.Shared.Return(sourceXs);

				if (!ReferenceEquals(src32, image))
					src32.Dispose();
			}
		}

		/// <summary>
		/// Copies straight out of the bitmap's Cairo surface when it has one, leaving that surface intact.
		/// False when there is none (nothing has been drawn yet, or this is not the GTK backend), and the
		/// caller falls back to the pixbuf path.
		/// </summary>
		private static unsafe bool CopyFromCairoSurface(Bitmap image, Rectangle source,
			WaylandShmBuffer target, int width, int height, byte opacity)
		{
			if (image.Handler is not Eto.GtkSharp.Drawing.BitmapHandler handler)
				return false;

			var surface = handler.Surface;

			// Argb32 only: a bitmap created without alpha gets an Rgb24 surface, whose 32-bit words carry no
			// usable alpha for a per-pixel-alpha overlay.
			if (surface == null || surface.Format != Cairo.Format.Argb32)
				return false;

			surface.Flush();
			var srcBase = (byte*)surface.DataPtr;

			if (srcBase == null)
				return false;

			var srcStride = surface.Stride;
			var dstBase = (uint*)target.Data;
			var dstStride = target.Stride / 4;
			var oneToOne = width == source.Width && height == source.Height;

			for (var y = 0; y < height; y++)
			{
				var sourceY = source.Y + (oneToOne ? y : SampleIndex(y, height, source.Height));
				var srcRow = (uint*)(srcBase + (long)sourceY * srcStride);
				var dstRow = dstBase + (long)y * dstStride;

				if (oneToOne && opacity == 255)
				{
					// Identical layout and identical alpha convention: whole rows move as memory.
					Buffer.MemoryCopy(srcRow + source.X, dstRow, (long)width * 4, (long)width * 4);
					continue;
				}

				for (var x = 0; x < width; x++)
					dstRow[x] = ScalePremultiplied(
						srcRow[source.X + (oneToOne ? x : SampleIndex(x, width, source.Width))], opacity);
			}

			return true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static int SampleIndex(int targetIndex, int targetLength, int sourceLength)
			=> (int)Math.Min(sourceLength - 1L,
				((2L * targetIndex + 1) * sourceLength) / (2L * targetLength));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint Premultiply(uint argb)
		{
			var a = (argb >> 24) & 0xFF;
			var r = (argb >> 16) & 0xFF;
			var g = (argb >> 8) & 0xFF;
			var b = argb & 0xFF;

			if (a == 0)
				return 0;

			if (a != 255)
			{
				r = (r * a + 127) / 255;
				g = (g * a + 127) / 255;
				b = (b * a + 127) / 255;
			}

			return (a << 24) | (r << 16) | (g << 8) | b;
		}

		private void TeardownSurface(bool abandon = false, bool forceLocal = false)
		{
			preparedBuffer = null;
			preparedSemanticDamage = default;
			preparedWriteDamage = default;
			historyInterpretation = null;
			damageHistory.Reset();
			if (abandon)
				surface?.Abandon();
			else
				surface?.Dispose();
			surface = null;

			lock (WaylandLayerShellClient.Sync)
			{
				// Released buffers are freed now. In-flight buffers retire themselves and keep their mapping alive
				// until wl_buffer.release, avoiding a compositor read from unmapped SHM after surface destruction.
				foreach (var b in bufferPool)
				{
					if (abandon)
						b.Abandon();
					else
					{
						b.Dispose();
						// Dispose defers an in-flight buffer to wl_buffer.release. Connection invalidation stops the
						// dispatcher immediately afterward, so force its local mapping/handle cleanup then.
						if (forceLocal) b.Abandon();
					}
				}

				bufferPool.Clear();

				if (emptyRegion != 0 && !abandon)
				{
					WaylandNative.RegionDestroy(emptyRegion);
				}

				emptyRegion = 0;
			}
			marginLeft = marginTop = int.MinValue;
			outputName = 0;
		}

		public void Dispose()
		{
			lock (stateSync)
			{
				if (disposed)
					return;

				TeardownSurface(connectionInvalidated || !client.IsAvailable);
				disposed = true;
			}

			client.Unregister(this);
		}

		/// <summary>Called by the owning connection before wl_display_disconnect. Removes every raw proxy pointer
		/// and listener handle, and force-releases local SHM resources when release events can no longer arrive.</summary>
		internal void InvalidateConnection(bool connectionLost)
		{
			lock (stateSync)
			{
				if (connectionInvalidated)
					return;

				connectionInvalidated = true;
				TeardownSurface(connectionLost, forceLocal: true);
			}

			client.Unregister(this);
		}
	}
}
#endif
