using Keysharp.Builtins;
#if LINUX
using Eto.GtkSharp;
#elif OSX
using MonoMac.AppKit;
#endif

namespace Keysharp.Internals.Images
{
	internal static class ImageHelper
	{
		internal static Icon IconFromByteArray(byte[] bytes)
		{
#if WINDOWS
			using (var ms = new MemoryStream(bytes))
				return new Icon(ms);
#else
			using (var ms = new MemoryStream(bytes, writable: false))
				return new Icon(ms);
#endif
		}

		private static byte[] IconToByteArray(Icon icon)
		{
			if (icon == null)
				return null;

#if WINDOWS
			try
			{
				using var stream = new MemoryStream();
				icon.Save(stream);
				return stream.ToArray();
			}
			catch
			{
				using var bitmap = icon.ToBitmap();
				return BuildIconFile([EncodeIconFrame(bitmap)]);
			}
#else
			var frames = icon.Frames.Select(frame => EncodeIconFrame(frame.Bitmap)).ToArray();

			if (frames.Length == 0)
			{
				using var bitmap = icon.ToBitmap();
				frames = [EncodeIconFrame(bitmap)];
			}

			return BuildIconFile(frames);
#endif
		}

		private static (byte[] Bytes, int Width, int Height) EncodeIconFrame(Bitmap bitmap)
		{
			if (bitmap == null)
				return ([], 0, 0);

			var width = bitmap.Width;
			var height = bitmap.Height;
			Bitmap resized = null;

			if (width > 256 || height > 256)
			{
				var scale = Math.Min(256d / width, 256d / height);
				width = Math.Max(1, (int)Math.Round(width * scale));
				height = Math.Max(1, (int)Math.Round(height * scale));
#if WINDOWS
				resized = bitmap.Resize(width, height);
#else
				resized = new Bitmap(bitmap, width, height, ImageInterpolation.Default);
#endif
			}

			try
			{
				return (ToPngBytes(resized ?? bitmap), width, height);
			}
			finally
			{
				resized?.Dispose();
			}
		}

		private static byte[] BuildIconFile(IReadOnlyList<(byte[] Bytes, int Width, int Height)> frames)
		{
			var usable = frames.Where(frame => frame.Bytes is { Length: > 0 } && frame.Width > 0 && frame.Height > 0)
				.Take(ushort.MaxValue).ToArray();

			if (usable.Length == 0)
				return null;

			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			writer.Write((ushort)0);
			writer.Write((ushort)1);
			writer.Write((ushort)usable.Length);
			var offset = 6 + (16 * usable.Length);

			foreach (var frame in usable)
			{
				writer.Write((byte)(frame.Width >= 256 ? 0 : frame.Width));
				writer.Write((byte)(frame.Height >= 256 ? 0 : frame.Height));
				writer.Write((byte)0);
				writer.Write((byte)0);
				writer.Write((ushort)1);
				writer.Write((ushort)32);
				writer.Write(frame.Bytes.Length);
				writer.Write(offset);
				offset += frame.Bytes.Length;
			}

			foreach (var frame in usable)
				writer.Write(frame.Bytes);

			writer.Flush();
			return stream.ToArray();
		}

		internal static Bitmap ConvertCursorToBitmap(Cursor c)
		{
#if WINDOWS
			var bmp = new Bitmap(c.Size.Width, c.Size.Height);

			using (var g = Graphics.FromImage(bmp))
			{
				c.Draw(g, new Rectangle(0, 0, c.Size.Width, c.Size.Height));
			}

			return bmp;
#elif LINUX
			return new Bitmap(c.ToGdk().Image.SaveToBuffer("png"));
#else
			NSImage image = c?.ControlObject switch
			{
				NSCursor nsCursor => nsCursor.Image,
				NSImage nsImage => nsImage,
				_ => null
			};

			var tiff = image?.AsTiff();

			if (tiff != null)
			{
				var data = tiff.ToArray();

				if (data != null && data.Length > 0)
					return new Bitmap(data);
			}

			throw new InvalidOperationException("Unable to convert cursor to bitmap on macOS: expected MonoMac NSCursor/NSImage control object.");
#endif
		}

		/// <summary>Whether <see cref="LoadImage"/> loaded an icon or a cursor rather than a picture, given the source it
		/// was passed and the icon or cursor it returned.</summary>
		internal static bool IsIconSource(string filename, object loaded)
			=> loaded is Icon or Cursor || filename.StartsWith("HICON:", StringComparison.OrdinalIgnoreCase);

		internal static Icon LoadIconFromAssembly(string path, string iconName)
		{
			Icon icon = null;

			if (Script.TheScript.ReflectionsData.loadedAssemblies.TryGetValue(path, out var assembly))
			{
				icon = LoadIconHelper(assembly, iconName);
			}
			else//Hasn't been loaded, so temporarily load it.
			{
				try
				{
					var ac = new UnloadableAssemblyLoadContext(path);
					assembly = ac.LoadFromAssemblyPath(path);
					icon = LoadIconHelper(assembly, iconName);
					ac.Unload();
				}
				catch
				{
				}
			}

			return icon;
		}

		internal static Icon LoadIconHelper(Assembly assembly, string iconName)
		{
			Icon icon = null;
			var resourceNames = assembly.GetManifestResourceNames();
			var trim = ".resources";

			foreach (var resourceName in resourceNames)
			{
				var trimmedName = resourceName.EndsWith(".resources", StringComparison.CurrentCulture) ? resourceName.Substring(0, resourceName.Length - trim.Length) : resourceName;
				var resource = new System.Resources.ResourceManager(trimmedName, assembly);

				try
				{
					if (resource.GetObject(iconName) is byte[] bytes)
					{
#if WINDOWS
						using (var ms = new MemoryStream(bytes))
							icon = new Icon(ms);
#else
						icon = IconFromByteArray(bytes);
#endif
					}
					if (icon != null)
						break;
				}
				catch { }
			}

			return icon;
		}

		/// <summary>
		/// Loads a picture, an icon or a cursor from a file, a module's icon resources, or an "HBITMAP:"/"HICON:" handle,
		/// which is consumed unless "*" follows the colon. A width or height of 0 keeps the source's own size, and -1
		/// follows the other dimension's aspect ratio.
		/// </summary>
		/// <param name="exactPixels">See <see cref="ResizeBitmap"/>: pixel consumers
		/// (ImageSearch needles) must pass true so *w/*h options resample real pixels.</param>
		/// <returns>The bitmap, and the icon or cursor it came from, which the caller disposes, or null for a picture or a
		/// handle. A null bitmap means the source could not be loaded; nothing is raised, so each caller reports that as
		/// its own error.</returns>
		internal static (Bitmap, object) LoadImage(string filename, int w, int h, object iconindex, bool exactPixels = false)
		{
			Bitmap bmp = null;
			object source = null;

			// Every source below is decoded into a bitmap nothing else holds, so the one it is resized from goes.
			Bitmap Sized(Bitmap loaded)
			{
				var resized = loaded == null ? null : ResizeBitmap(loaded, w, h, exactPixels);

				if (!ReferenceEquals(resized, loaded))
					loaded.Dispose();

				return resized;
			}

			try
			{
				if (TryParseHandle(filename, out var handle, out var kind, out var keepHandle))
				{
					try
					{
#if WINDOWS
						if (kind == ImageHandleKind.Icon)
						{
							using var icon = Icon.FromHandle(handle);
							return (Sized(icon.ToBitmap()), null);
						}
#endif
						return (Sized(GetBitmapFromHBitmap(handle)), null);
					}
					finally
					{
						if (!keepHandle)
							ImageHandleManager.Dispose(handle, kind);
					}
				}

				//Invariant, not the current culture: in tr-TR the dotless i makes ".ICO".ToLower() ".ıco".
				var ext = Path.GetExtension(filename).ToLowerInvariant();

				if (ext == ".dll"
#if WINDOWS
						|| ext == ".exe" || ext == ".icl" || ext == ".cpl" || ext == ".scr"
#endif
				   )
				{
					Icon ico = null;

					if (iconindex is string iconstr)
						ico = LoadIconFromAssembly(filename, iconstr);

#if WINDOWS
					else
					{
						_ = iconindex.TryCoerceInt(out var idx);
						ico = ExtractIconWithSizeFromModule(filename, idx, w, h) ?? GuiHelper.GetIcon(filename, idx);
					}

#endif

					if (ico != null)
					{
						source = ico;
						bmp = Sized(ico.ToBitmap());
					}
				}
				else if (ext == ".ico")
				{
					if (w > 0 && h < 0) h = w;
					if (h > 0 && w < 0) w = h;

#if WINDOWS
					List<(Icon, Bitmap)> frames;

					using (var ico = new Icon(filename))
						frames = GuiHelper.SplitIcon(ico);

					var chosen = w > 0 || h > 0
								 ? frames.FindIndex(frame => (w <= 0 || frame.Item1.Width == w) && (h <= 0 || frame.Item1.Height == h))
								 : iconindex.TryCoerceInt(out var iconint) && iconint >= 0 && iconint < frames.Count ? iconint : 0;

					if (chosen < 0)
						chosen = 0;

					for (var i = 0; i < frames.Count; i++)
						if (i != chosen)
						{
							frames[i].Item1.Dispose();
							frames[i].Item2.Dispose();
						}

					source = frames[chosen].Item1;
					bmp = Sized(frames[chosen].Item2);
#else
					using (var ico = new Icon(filename))
					{
						var frames = ico.Frames.ToList();

						if (frames.Count > 0)
						{
							IconFrame frame;
							if (w > 0 || h > 0)
							{
								var targetSize = new Size(w > 0 ? w : h, h > 0 ? h : w);
								frame = frames.FirstOrDefault(tempFrame => tempFrame.PixelSize == targetSize) ?? frames[0];
							}
							else
							{
								_ = iconindex.TryCoerceInt(out var iconint, int.MaxValue);
								frame = iconint >= 0 && iconint < frames.Count ? frames[iconint] : frames[0];
							}

							bmp = new Bitmap(frame.Bitmap);
							source = new Icon(1f, new Bitmap(frame.Bitmap));
						}
					}

					bmp = Sized(bmp);
#endif
				}
				else if (ext == ".cur")
				{
					var cursor = new Cursor(filename);
					source = cursor;
					bmp = Sized(ConvertCursorToBitmap(cursor));
				}
				else
				{
					//The decoded file keeps the file locked, so a bitmap that is not resized from it is copied.
					using var file = (Bitmap)Image.FromFile(filename);
					var resized = ResizeBitmap(file, w, h, exactPixels);
					bmp = ReferenceEquals(resized, file) ? new Bitmap(file) : resized;
				}
			}
			catch (Exception)
			{
				bmp?.Dispose();
				(source as IDisposable)?.Dispose();
				return (null, null);
			}

			return (bmp, source);
		}

		// Reads an "HBITMAP:" or "HICON:" source, whose handle LoadImage consumes unless "*" marks the script as keeping it.
		private static bool TryParseHandle(string filename, out nint handle, out ImageHandleKind kind, out bool keep)
		{
			var prefix = filename.StartsWith("HBITMAP:", StringComparison.OrdinalIgnoreCase) ? 8
						 : filename.StartsWith("HICON:", StringComparison.OrdinalIgnoreCase) ? 6 : 0;
			var value = filename.AsSpan(prefix);
			keep = value.StartsWith("*");
			kind = prefix == 6 ? ImageHandleKind.Icon : ImageHandleKind.Bitmap;
			handle = prefix > 0 && long.TryParse(keep ? value[1..] : value, out var number) ? (nint)number : 0;
			return handle != 0;
		}

#if OSX
		// Returns a copy of an NSImage-backed bitmap whose logical size is (w, h) points while
		// retaining the original full-resolution representations: on Retina displays the GUI
		// draws it sharp from the high-res rep instead of from resampled pixels, and pixel
		// access renders at (w, h) just like a true resize would. Returns null when the bitmap
		// isn't NSImage-backed so the caller can fall back to a resampling resize.
		internal static Bitmap ResizeNSImagePointSize(Bitmap bmp, int w, int h)
		{
			if (bmp?.Handler is not Eto.Mac.Drawing.BitmapHandler handler || handler.Control == null)
				return null;

			var copy = (MonoMac.AppKit.NSImage)handler.Control.Copy();
			copy.Size = new MonoMac.CoreGraphics.CGSize(w, h);
			return new Bitmap(new Eto.Mac.Drawing.BitmapHandler(copy));
		}
#endif

#if !WINDOWS
		// Returns a 32bpp opaque copy of `bmp` when its Pixbuf storage is 3 bytes per
		// pixel; otherwise returns `bmp` unchanged. ImageFinder reads pixels as 4-byte
		// ints, so 24bpp bitmaps would otherwise pull the next pixel's R byte (or row
		// padding) into the high byte and break the comparison. Mirrors AutoHotkey's
		// getbits()-via-GetDIBits step that forces both source and needle to 32bpp.
		internal static Bitmap EnsureOpaque32Bpp(Bitmap bmp)
		{
			if (bmp == null)
				return null;

			int bpp;
			using (var check = bmp.Lock())
				bpp = check.BytesPerPixel;

			if (bpp == 4)
				return bmp;

			// Copy 3bpp (RGB, no alpha) → 4bpp (RGBA, A=255) with a direct byte copy rather than
			// Cairo/Graphics.DrawImage. Going through DrawImage would premultiply and apply the
			// selected interpolation filter, which can perturb exact pixel values relative to the raw
			// Pixbuf read used for the 4bpp source path; a direct byte copy keeps channels identical so
			// ImageFinder compares like with like.
			var result = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppRgba);
			using var src = bmp.Lock();
			using var dst = result.Lock();

			unsafe
			{
				var sp = (byte*)src.Data;
				var dp = (byte*)dst.Data;

				for (var y = 0; y < bmp.Height; y++)
				{
					var sr = sp + (long)y * src.ScanWidth;
					var dr = dp + (long)y * dst.ScanWidth;

					for (var x = 0; x < bmp.Width; x++)
					{
						dr[x * 4 + 0] = sr[x * 3 + 0]; // R
						dr[x * 4 + 1] = sr[x * 3 + 1]; // G
						dr[x * 4 + 2] = sr[x * 3 + 2]; // B
						dr[x * 4 + 3] = 0xFF;           // A = 255
					}
				}
			}

			return result;
		}
#endif

		/// <param name="exactPixels">True to force a true pixel resample. On macOS the default
		/// is a point-size change that keeps the high-res representation, which displays sharp
		/// on Retina; pixel consumers (e.g. ImageSearch needles with *w/*h) need real pixels,
		/// otherwise EnsurePixelSize would see the original representation and undo the resize.</param>
		internal static Bitmap ResizeBitmap(Bitmap bmp, int w, int h, bool exactPixels = false)
		{
			if (w <= 0 && h <= 0)
				return bmp;

			// Negative = derive from the other dimension preserving the aspect ratio; 0 = keep original.
			if (w <= 0) w = w < 0 && h > 0 ? Math.Max(1, (int)Math.Round(bmp.Width * (h / (double)bmp.Height))) : bmp.Width;
			if (h <= 0) h = h < 0 ? Math.Max(1, (int)Math.Round(bmp.Height * (w / (double)bmp.Width))) : bmp.Height;

			if (bmp.Width != w || bmp.Height != h)
#if WINDOWS
				bmp = bmp.Resize(w, h);
#elif OSX
				bmp = (exactPixels ? null : ResizeNSImagePointSize(bmp, w, h)) ?? new Bitmap(bmp, w, h, ImageInterpolation.Default);
#else
				bmp = new Bitmap(bmp, w, h, ImageInterpolation.Default);
#endif

			return bmp;
		}

		/// <summary>
		/// Resizes a <see cref="Bitmap"/> to a new width and height.
		/// </summary>
		/// <param name="src">The <see cref="Bitmap"/> to resize.</param>
		/// <param name="width">The new width to use. Use a number less than 0 to maintain the aspect ratio.</param>
		/// <param name="height">The new height to use. Use a number less than 0 to maintain the aspect ratio.</param>
		/// <returns>A new <see cref="Bitmap"/> with the specified size.</returns>
		internal static Bitmap Resize(this Bitmap src, int width, int height)
		{
			// Keep aspect if one dimension is negative
			if (width < 0)
				width = (int)((double)src.Width / src.Height * height + 0.5);
			else if (height < 0)
				height = (int)((double)src.Height / src.Width * width + 0.5);

			// If same size, return a copy so callers can safely dispose the original
			if (src.Width == width && src.Height == height)
				return new Bitmap(src);

#if WINDOWS
			// Use premultiplied ARGB for best compositing behavior
			var dst = new Bitmap(width, height, PixelFormat.Format32bppPArgb);

			using (var g = Graphics.FromImage(dst))
			using (var ia = new ImageAttributes())
			{
				// High-quality sampling + edge-safe wrap
				g.CompositingMode = CompositingMode.SourceOver;
				g.CompositingQuality = CompositingQuality.HighQuality;
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;

				// Prevent sampling transparent/out-of-bounds pixels at the edges
				ia.SetWrapMode(WrapMode.TileFlipXY);

				// Start from a transparent canvas
				g.Clear(Color.Transparent);

				g.DrawImage(src, new Rectangle(0, 0, width, height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
			}

			return dst;
#else
			return new Bitmap(src, width, height, ImageInterpolation.High);
#endif
		}

		/// <summary>
		/// Multiplies every pixel's alpha of <paramref name="bmp"/> by <paramref name="alpha"/>/255 (whole-image
		/// opacity for overlay fades), in place, and returns the same bitmap. The caller decides whether the
		/// original must be preserved: if so, clone it and pass the clone (so, e.g., an overlay can fade its live
		/// canvas by fading a throwaway copy). Both platforms mutate in place — Windows via LockBits, others via
		/// the backend's pixel lock — so there is no hidden allocation. A no-op (returns <paramref name="bmp"/>
		/// unchanged) for alpha == 255.
		/// </summary>
		internal static Bitmap ApplyOpacity(Bitmap bmp, byte alpha)
		{
			if (alpha == 255)
				return bmp;

#if WINDOWS
			var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

			try
			{
				unsafe
				{
					var basePtr = (byte*)data.Scan0;

					for (var y = 0; y < bmp.Height; y++)
					{
						var row = (uint*)(basePtr + (long)y * data.Stride);   // Format32bppArgb: little-endian uint = 0xAARRGGBB

						for (var x = 0; x < bmp.Width; x++)
						{
							var argb = row[x];
							var a = (uint)((argb >> 24) * alpha / 255);
							row[x] = (argb & 0x00FFFFFFu) | (a << 24);
						}
					}
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}

			return bmp;
#else
			using (var data = bmp.Lock())
			{
				unsafe
				{
					var basePtr = (byte*)data.Data;

					for (var y = 0; y < bmp.Height; y++)
					{
						var row = basePtr + (long)y * data.ScanWidth;

						for (var x = 0; x < bmp.Width; x++)
						{
							var px = (int*)(row + x * data.BytesPerPixel);
							var argb = (uint)data.TranslateDataToArgb(*px);
							var a = (uint)((argb >> 24) * alpha / 255);
							*px = data.TranslateArgbToData((int)((argb & 0x00FFFFFFu) | (a << 24)));
						}
					}
				}
			}

			return bmp;
#endif
		}

		/// <summary>
		/// Returns a NEW bitmap that is <paramref name="src"/> with <paramref name="map"/> applied to every
		/// pixel. Both the input and output pixels are packed as 0xAARRGGBB, so a caller writes a pure color
		/// transform without touching backend channel order or premultiplication. The cross-platform
		/// lock/translate scaffolding mirrors <c>ImageFinder.ReadArgb</c> (read) and <c>ApplyOpacity</c>
		/// (write) so the three stay consistent across the System.Drawing and Eto backends.
		/// </summary>
		internal static Bitmap MapPixelsArgb(Bitmap src, Func<uint, uint> map)
		{
			if (src == null)
				return null;

			int w = src.Width, h = src.Height;
			var dst = NewArgbCanvas(w, h);
#if WINDOWS
			// Acquire the source lock first, then the destination lock INSIDE the try, so a throw on the second
			// LockBits can't strand the first lock (or leak the destination lock). Each is unlocked in finally
			// only if it was actually acquired.
			var sdata = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			BitmapData ddata = null;

			try
			{
				ddata = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

				unsafe
				{
					// Format32bppArgb stores BGRA in memory, so a little-endian uint read/write is 0xAARRGGBB.
					var sBase = (byte*)sdata.Scan0;
					var dBase = (byte*)ddata.Scan0;

					for (var y = 0; y < h; y++)
					{
						var srow = (uint*)(sBase + (nint)y * sdata.Stride);
						var drow = (uint*)(dBase + (nint)y * ddata.Stride);

						for (var x = 0; x < w; x++)
							drow[x] = map(srow[x]);
					}
				}
			}
			finally
			{
				if (ddata != null)
					dst.UnlockBits(ddata);

				src.UnlockBits(sdata);
			}

#else
			// Force 32bpp first so the 4-byte reads are valid (Pixbuf is 3bpp for 24-bit images) and so a
			// premultiplied backend sees A=255, making the translate lossless (mirrors ImageFinder.ReadArgb).
			var src32 = EnsureOpaque32Bpp(src);

			try
			{
				using var sdata = src32.Lock();
				using var ddata = dst.Lock();

				unsafe
				{
					var sBase = (byte*)sdata.Data;
					var sStride = sdata.ScanWidth;
					var sBpp = sdata.BytesPerPixel;
					var dBase = (byte*)ddata.Data;
					var dStride = ddata.ScanWidth;
					var dBpp = ddata.BytesPerPixel;

					for (var y = 0; y < h; y++)
					{
						var srow = sBase + (long)y * sStride;
						var drow = dBase + (long)y * dStride;

						for (var x = 0; x < w; x++)
						{
							// TranslateDataToArgb un-premultiplies + reorders to 0xAARRGGBB; TranslateArgbToData
							// re-premultiplies + reorders back into the destination's in-memory layout.
							var argb = (uint)sdata.TranslateDataToArgb(*(int*)(srow + x * sBpp));
							*(int*)(drow + x * dBpp) = ddata.TranslateArgbToData((int)map(argb));
						}
					}
				}
			}
			finally
			{
				if (!ReferenceEquals(src32, src))
					src32.Dispose();
			}

#endif
			return dst;
		}

		// Writes raw pixel bytes from src into bmp — the inverse of KeysharpImage.WritePixelData. bpp 1 = one
		// 8-bit grayscale byte per pixel (becomes an opaque gray, A=255); bpp 4 = R,G,B,A bytes. src must point
		// at bmp.Width*bmp.Height*bpp readable bytes. The cross-platform lock/translate scaffolding mirrors
		// MapPixelsArgb/ApplyOpacity so all the pixel-writers stay consistent across backends.
		internal static unsafe void WriteBufferToBitmap(Bitmap bmp, byte* src, int bpp)
		{
			int w = bmp.Width, h = bmp.Height;
#if WINDOWS
			var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

			try
			{
				// Format32bppArgb stores BGRA in memory, so a little-endian uint write of 0xAARRGGBB is correct.
				var basePtr = (byte*)data.Scan0;

				for (var y = 0; y < h; y++)
				{
					var row = (uint*)(basePtr + (nint)y * data.Stride);
					var srcRow = y * w;

					for (var x = 0; x < w; x++)
						row[x] = DecodePixel(src, srcRow + x, bpp);
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}

#else
			using var data = bmp.Lock();
			var basePtr = (byte*)data.Data;
			var stride = data.ScanWidth;
			var dstBpp = data.BytesPerPixel;

			for (var y = 0; y < h; y++)
			{
				var row = basePtr + (long)y * stride;
				var srcRow = y * w;

				// TranslateArgbToData rewrites 0xAARRGGBB into the backend's in-memory layout (channel order +
				// premultiplication), the inverse of the TranslateDataToArgb read used by GetPixelData.
				for (var x = 0; x < w; x++)
					*(int*)(row + x * dstBpp) = data.TranslateArgbToData((int)DecodePixel(src, srcRow + x, bpp));
			}

#endif
		}

		// Reads one pixel from src at pixel index idx in the bpp layout and returns it packed as 0xAARRGGBB —
		// the inverse of KeysharpImage.EmitPixel. Grayscale (bpp 1) expands one byte to an opaque gray; bpp 4
		// reads R,G,B,A. Aggressively inlined; the bpp test is constant for the whole call so it is free.
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe uint DecodePixel(byte* src, int idx, int bpp)
		{
			if (bpp == 1)
			{
				uint g = src[idx];
				return 0xFF000000u | (g << 16) | (g << 8) | g;
			}
			else
			{
				var o = idx * 4;
				uint r = src[o], g = src[o + 1], b = src[o + 2], a = src[o + 3];
				return (a << 24) | (r << 16) | (g << 8) | b;
			}
		}

		/// <summary>
		/// Creates a new zero-initialised (fully transparent) ARGB canvas of the given size.
		/// Centralises the one pixel-format name that differs between the System.Drawing
		/// (Windows) and Eto.Drawing (Mac/Linux) backends.
		/// </summary>
		internal static Bitmap NewArgbCanvas(int w, int h)
		{
#if WINDOWS
			var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
			// GDI+ stamps a new bitmap with the DPI it read from the screen when it initialised, so in a
			// per-monitor-DPI-aware process on a scaled display this canvas would be, e.g., 192 DPI. Drawing
			// primitives take pixel coordinates (DPI-independent), but DrawString converts a point-size font
			// through the Graphics' DpiY — so on a 192-DPI canvas "13pt" text renders at 2x and overflows the
			// pixel-sized shapes around it. Pin every drawing canvas to 96 DPI so a point size maps to the same
			// pixel count regardless of display scaling, keeping text and shapes on one coordinate system.
			bmp.SetResolution(96f, 96f);
			return bmp;
#else
			return new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppRgba);
#endif
		}

		/// <summary>
		/// Restores the background in <paramref name="region"/> without replacing the bitmap. Pixels outside
		/// the region must already have <paramref name="argb"/>. Windows transparent clears wipe just these rows;
		/// other paths can clear the full bitmap with the same result.
		/// </summary>
		internal static void ClearInPlace(Bitmap bmp, int argb, PixelRect region)
		{
			if (bmp == null)
				return;

#if WINDOWS
			if (((uint)argb >> 24) == 0)
			{
				var data = bmp.LockBits(new Rectangle(region.X, region.Y, region.Width, region.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);

				try
				{
					unsafe
					{
						var stride = Math.Abs(data.Stride);
						var rowBytes = data.Width * 4;
						var start = data.Stride < 0 ? (byte*)data.Scan0 + (long)data.Stride * (data.Height - 1) : (byte*)data.Scan0;

						if (stride == rowBytes)
							NativeMemory.Clear(start, (nuint)((long)stride * data.Height));
						else
							for (var row = 0; row < data.Height; row++)
								NativeMemory.Clear(start + (long)row * stride, (nuint)rowBytes);
					}
				}
				finally
				{
					bmp.UnlockBits(data);
				}

				return;
			}

#endif
			using var g = MakeGraphics(bmp, highQuality: false);
			// Outside region the pixels already have this color; a full clear is equivalent.
			g.Clear(ArgbToColor(argb));
		}

		/// <summary>
		/// Starts one draw op's transform on a Graphics that may be reused, so a per-op ScaleTransform cannot
		/// accumulate across ops. The two backends spell this differently and neither has the other's call:
		/// System.Drawing resets to identity outright, while Eto only has a transform stack, so there it is a
		/// push that <see cref="PopDrawTransform"/> unwinds. Always pair the two.
		/// </summary>
		internal static void PushDrawTransform(Graphics g)
		{
#if WINDOWS
			g.ResetTransform();
#else
			g.SaveTransform();
#endif
		}

		/// <summary>Ends what <see cref="PushDrawTransform"/> began. A no-op on System.Drawing, where the next
		/// op resets rather than pops.</summary>
		internal static void PopDrawTransform(Graphics g)
		{
#if !WINDOWS
			g.RestoreTransform();
#endif
		}

		/// <summary>
		/// Applies the quality settings <see cref="MakeGraphics"/> would have set, to a Graphics that already
		/// exists. Lets a live drawing surface keep one Graphics across ops — worth about 2 us per draw
		/// — while still letting each op choose its own filtering.
		/// </summary>
		internal static void ConfigureGraphics(Graphics g, bool highQuality)
		{
#if WINDOWS
			g.InterpolationMode = highQuality ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
			g.SmoothingMode = highQuality ? SmoothingMode.AntiAlias : SmoothingMode.None;
#else
			g.ImageInterpolation = highQuality ? ImageInterpolation.High : ImageInterpolation.None;
			g.AntiAlias = highQuality;
#endif
		}

		/// <summary>
		/// Returns a <see cref="Graphics"/> targeting <paramref name="bmp"/>. The only backend
		/// divergence is how a Graphics is obtained (Graphics.FromImage vs the Eto constructor) and
		/// the interpolation/anti-alias property names; the transform and DrawImage calls used by
		/// callers are identical on both, and both compose transforms in prepend (GDI+) order.
		/// </summary>
		/// <param name="highQuality">High-quality bicubic + anti-alias (scale/rotate). When false,
		/// nearest-neighbour with no anti-alias, for exact pixel-preserving ops such as flips.</param>
		internal static Graphics MakeGraphics(Bitmap bmp, bool highQuality = true)
		{
#if WINDOWS
			var g = Graphics.FromImage(bmp);
			g.InterpolationMode = highQuality ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
			g.SmoothingMode = highQuality ? SmoothingMode.AntiAlias : SmoothingMode.None;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			// Grayscale AA, not the SystemDefault the machine's font smoothing picks. That default is normally
			// ClearType, whose subpixel coverage is three separate per-channel masks; on an opaque window GDI
			// resolves them against the pixels behind, but on a transparent canvas there is nothing behind, so
			// the glyph edges come out colour-fringed with alpha that belongs to no single channel. Every canvas
			// here is alpha-capable, and this is also what keeps DrawText and MeasureText measuring the same way.
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			return g;
#else
			var g = new Graphics(bmp);
			g.ImageInterpolation = highQuality ? ImageInterpolation.High : ImageInterpolation.None;
			g.AntiAlias = highQuality;
			return g;
#endif
		}

		/// <summary>Measures the pixel size <paramref name="text"/> occupies in <paramref name="font"/> on
		/// <paramref name="g"/>, matching what DrawString/DrawText lays out. The two backends order the
		/// MeasureString arguments differently, so it is wrapped here (like the draw calls).</summary>
		internal static SizeF MeasureText(Graphics g, Font font, string text)
		{
#if WINDOWS
			return g.MeasureString(text, font);
#else
			return g.MeasureString(font, text);
#endif
		}

		// Builds a Color from a packed 0xAARRGGBB int. The 4-argument FromArgb overload orders its
		// parameters differently in the two backends (alpha-first vs alpha-last), so it is wrapped here.
		internal static Color ArgbToColor(int argb)
		{
			var a = (byte)((argb >> 24) & 0xFF);
			var r = (byte)((argb >> 16) & 0xFF);
			var g = (byte)((argb >> 8) & 0xFF);
			var b = (byte)(argb & 0xFF);
#if WINDOWS
			return Color.FromArgb(a, r, g, b);
#else
			return Color.FromArgb(r, g, b, a);
#endif
		}

		/// <summary>
		/// Rotates <paramref name="bmp"/> clockwise by <paramref name="angleDegrees"/>, growing the
		/// canvas so no content is clipped. <paramref name="bgArgb"/> is the fill behind the rotated
		/// image as packed 0xAARRGGBB (0 = transparent). The original is left unchanged; a new bitmap
		/// is returned.
		/// </summary>
		internal static Bitmap RotateBitmap(Bitmap bmp, double angleDegrees, int bgArgb = 0)
		{
			if (bmp == null)
				return null;

			var norm = ((angleDegrees % 360) + 360) % 360;

			if (norm == 0)
				return new Bitmap(bmp);

			int srcW = bmp.Width, srcH = bmp.Height;
			var rad = norm * Math.PI / 180.0;
			var cos = Math.Abs(Math.Cos(rad));
			var sin = Math.Abs(Math.Sin(rad));

			// cos/sin of exact 90 degree multiples are tiny non-zero values (e.g. cos(90 deg) is
			// ~6e-17, not 0), which would otherwise inflate an axis-aligned rotation by one pixel.
			if (cos < 1e-9) cos = 0;
			if (sin < 1e-9) sin = 0;

			var dstW = (int)Math.Ceiling(srcW * cos + srcH * sin);
			var dstH = (int)Math.Ceiling(srcW * sin + srcH * cos);
			var dst = NewArgbCanvas(dstW, dstH);

			using (var g = MakeGraphics(dst))
			{
				if (((bgArgb >> 24) & 0xFF) != 0)
					g.Clear(ArgbToColor(bgArgb));

				// Prepend order (both backends): translate to the new centre, rotate, translate back
				// by the original centre, then draw the source at its native pixel size.
				g.TranslateTransform(dstW / 2f, dstH / 2f);
				g.RotateTransform((float)norm);
				g.TranslateTransform(-srcW / 2f, -srcH / 2f);
#if WINDOWS
				// Explicit source-rectangle overload so the draw stays in source pixels regardless of the
				// bitmap's DPI metadata (System.Drawing's DrawImage(image, RectangleF) would otherwise
				// apply a DPI-ratio scale for a non-96-DPI loaded image).
				g.DrawImage(bmp, new Rectangle(0, 0, srcW, srcH), 0, 0, srcW, srcH, GraphicsUnit.Pixel);
#else
				g.DrawImage(bmp, new RectangleF(0, 0, srcW, srcH));
#endif
			}

			return dst;
		}

		/// <summary>
		/// Mirrors <paramref name="bmp"/> horizontally (left-right) or vertically (top-bottom).
		/// Returns a new bitmap; the original is unchanged.
		/// </summary>
		internal static Bitmap FlipBitmap(Bitmap bmp, bool horizontal)
		{
			if (bmp == null)
				return null;

			int w = bmp.Width, h = bmp.Height;
			var dst = NewArgbCanvas(w, h);

			using (var g = MakeGraphics(dst, highQuality: false))
			{
				// Prepend order: shift the far edge to the origin, then scale by -1 on the mirror axis.
				g.TranslateTransform(horizontal ? w : 0, horizontal ? 0 : h);
				g.ScaleTransform(horizontal ? -1f : 1f, horizontal ? 1f : -1f);
#if WINDOWS
				// Explicit source-rectangle overload — DPI-immune, exact pixel mirror (see RotateBitmap).
				g.DrawImage(bmp, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel);
#else
				g.DrawImage(bmp, new RectangleF(0, 0, w, h));
#endif
			}

			return dst;
		}

		/// <summary>
		/// Returns a new bitmap containing the (x, y, w, h) sub-region of <paramref name="bmp"/>,
		/// clamped to its bounds.
		/// </summary>
		internal static Bitmap CropBitmap(Bitmap bmp, int x, int y, int w, int h)
		{
			if (bmp == null)
				return null;

			x = Math.Clamp(x, 0, bmp.Width);
			y = Math.Clamp(y, 0, bmp.Height);
			w = Math.Clamp(w, 0, bmp.Width - x);
			h = Math.Clamp(h, 0, bmp.Height - y);

			if (w <= 0 || h <= 0)
				return NewArgbCanvas(w, h);

			var rect = new Rectangle(x, y, w, h);
#if WINDOWS
			return bmp.Clone(rect, bmp.PixelFormat);
#else
			// Normalise to 4 channels first: an X11 screen capture is a 3-channel (no-alpha) Gdk.Pixbuf,
			// and Eto's Clone builds a 4-channel destination, which gdk_pixbuf_copy_area rejects on the
			// channel-count mismatch. EnsureOpaque32Bpp is a no-op for an already-32bpp bitmap.
			var src = EnsureOpaque32Bpp(bmp);
			var cropped = src.Clone(rect);

			if (!ReferenceEquals(src, bmp))
				src.Dispose();

			return cropped;
#endif
		}

		/// <summary>
		/// Saves <paramref name="bmp"/> to <paramref name="path"/>, inferring the encoder from the
		/// file extension (defaulting to PNG).
		/// </summary>
		internal static void SaveBitmap(Bitmap bmp, string path)
		{
			if (bmp == null || string.IsNullOrEmpty(path))
				return;

			// Choose the encoder explicitly from the extension on both backends (defaulting to PNG) so a
			// path with an unknown or missing extension never throws a GDI+ "generic error".
			bmp.Save(path, ImageFormatFromExtension(path));
		}

		internal static byte[] ToPngBytes(Bitmap bmp)
		{
			if (bmp == null)
				return [];

#if WINDOWS
			using var ms = new MemoryStream();
			bmp.Save(ms, ImageFormat.Png);
			return ms.ToArray();
#else
			return bmp.ToByteArray(ImageFormat.Png) ?? [];
#endif
		}

		private static ImageFormat ImageFormatFromExtension(string path)
		{
			switch (Path.GetExtension(path))
			{
				case var x when x.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
				x.Equals(".jpeg", StringComparison.OrdinalIgnoreCase): return ImageFormat.Jpeg;
				case var x when x.Equals(".gif", StringComparison.OrdinalIgnoreCase): return ImageFormat.Gif;
				case var x when x.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
				x.Equals(".tiff", StringComparison.OrdinalIgnoreCase): return ImageFormat.Tiff;
#if WINDOWS
				case var x when x.Equals(".bmp", StringComparison.OrdinalIgnoreCase): return ImageFormat.Bmp;
#else
				case var x when x.Equals(".bmp", StringComparison.OrdinalIgnoreCase): return ImageFormat.Bitmap;
#endif
				default: return ImageFormat.Png;
			}
		}

		/// <summary>
		/// Copies the bitmap a handle names. On Windows a 32bpp DIB section, the kind LoadPicture and Image.ToBitmap
		/// hand out, is read with its premultiplied alpha, unless no pixel has any: GDI drawing leaves the alpha bytes
		/// zero, so such a section is opaque. Any other bitmap goes through FromHbitmap, which reads no alpha.
		/// </summary>
		internal static Bitmap GetBitmapFromHBitmap(nint nativeHBitmap)
		{
#if WINDOWS
			var size = Marshal.SizeOf<DIBSECTION>();

			if (WindowsAPI.GetObject(nativeHBitmap, size, out var dib) == size && dib.bmBitsPixel == 32 && dib.bmBits != 0)
			{
				int w = dib.bmWidth, h = dib.bmHeight, stride = dib.bmWidthBytes;
				// A positive header height stores the rows bottom-up, which a negative stride walks from the top row.
				var topDown = dib.dsBmih.biHeight < 0;
				_ = WindowsAPI.GdiFlush();//GDI drawing into the section may still be batched.
				var format = HasAlpha(dib.bmBits, w * h) ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb;
				using var view = new Bitmap(w, h, topDown ? stride : -stride, format,
											topDown ? dib.bmBits : dib.bmBits + (nint)(h - 1) * stride);
				return view.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb);
			}

			using var nativeBitmap = Bitmap.FromHbitmap(nativeHBitmap);
			return nativeBitmap.Clone(new Rectangle(Point.Empty, nativeBitmap.Size), nativeBitmap.PixelFormat);
#else
			return Bitmap.FromHbitmap(nativeHBitmap);
#endif
		}

#if WINDOWS
		private static unsafe bool HasAlpha(nint bits, int pixels)
		{
			var pixel = (uint*)bits;

			for (var i = 0; i < pixels; i++)
				if (pixel[i] >> 24 != 0)
					return true;

			return false;
		}

		/// <summary>
		/// Copies a bitmap into a new top-down 32bpp DIB section with premultiplied alpha, the HBITMAP AutoHotkey's
		/// LoadPicture gets from GdipCreateHBITMAPFromBitmap with no background color, so alpha survives the handle.
		/// The caller owns the section; 0 if GDI cannot create one.
		/// </summary>
		internal static nint CreateDibSection(Bitmap bmp)
		{
			int w = bmp.Width, h = bmp.Height;
			var header = new BITMAPINFOHEADER
			{
				biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = w,
				biHeight = -h,
				biPlanes = 1,
				biBitCount = 32,
			};
			var dib = WindowsAPI.CreateDIBSection(0, ref header, 0 /* DIB_RGB_COLORS */, out var bits, 0, 0);

			if (dib == 0)
				return 0;

			// GDI+ converts the pixels straight into the section's memory.
			var target = new BitmapData { Width = w, Height = h, Stride = w * 4, PixelFormat = PixelFormat.Format32bppPArgb, Scan0 = bits };

			try
			{
				_ = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly | ImageLockMode.UserInputBuffer, PixelFormat.Format32bppPArgb, target);
				bmp.UnlockBits(target);
				return dib;
			}
			catch
			{
				_ = WindowsAPI.DeleteObject(dib);
				throw;
			}
		}

		internal static Icon ExtractIconWithSizeFromModule(string path, int index, int w, int h)
		{
			if (w <= 0 && h > 0) w = h;
			if (h <= 0 && w > 0) h = w;
			if (w <= 0 || h <= 0)
			{
				w = SystemInformation.IconSize.Width;
				h = SystemInformation.IconSize.Height;
			}

			var hicons = new nint[1];
			var ids = new uint[1];
			var count = WindowsAPI.PrivateExtractIcons(path, index, w, h, hicons, ids, 1, 0);

			if (count == 0 || hicons[0] == 0)
				return null;

			//Clone into an icon which owns its handle, and release the extracted one here. Icon.FromHandle does NOT
			//take ownership, so returning that directly leaks the HICON however carefully the caller disposes.
			//Clone copies through CopyIcon on .NET Core; on .NET Framework it shared the handle, which would make
			//the DestroyIcon below a use-after-free rather than a release.
			try
			{
				using var borrowed = Icon.FromHandle(hicons[0]);
				return (Icon)borrowed.Clone();
			}
			finally
			{
				_ = DestroyIcon(hicons[0]);
			}
		}

#endif

		/// <summary>
		/// Loads an icon for a window or the tray. <see cref="LoadImage"/> answers with one bitmap, which is the
		/// wrong shape here twice over: a window wants an Icon, and LoadImage splits a multi-size .ico down to a
		/// single frame, where a window wants every size the file carries so the toolkit can pick one per slot --
		/// a small frame for the title bar, a larger one for alt-tab. Every other source is LoadImage's job.
		/// </summary>
		/// <param name="filename">What <see cref="LoadImage"/> accepts: a file, an "HICON:"/"HBITMAP:" handle, or
		/// a module holding icon resources.</param>
		/// <param name="iconNumber">The icon within a module, already through <see cref="PrepareIconNumber"/>.</param>
		/// <param name="size">The size the icon reports as its own, which is what a large-icon consumer such as the
		/// alt-tab switcher receives. Non-positive keeps the source's own size. Window icons are square.</param>
		/// <returns>The icon, or null when the source holds none or cannot be read. Nothing is thrown: a caller
		/// reports a bad source as a ValueError naming it, which is more use to a script than a decoder's message
		/// about a stream.</returns>
		internal static Icon LoadIconSet(string filename, object iconNumber, int size = 0)
		{
			try
			{
				//Invariant, not the current culture: in tr-TR the dotless i makes ".ICO".ToLower() ".ıco".
				if (Path.GetExtension(filename).ToLowerInvariant() == ".ico" && File.Exists(filename))
#if WINDOWS
					return size > 0 ? new Icon(filename, size, size) : new Icon(filename);

#else
				{
					var iconSet = new Icon(filename);

					if (size <= 0)
						return iconSet;

					using (iconSet)
					{
						var frames = iconSet.Frames.ToArray();

						if (frames.Length == 0)
							return null;

						var preferred = frames.MinBy(frame => Math.Abs(frame.PixelSize.Width - size)
							+ Math.Abs(frame.PixelSize.Height - size));
						var ordered = new[] { preferred }.Concat(frames.Where(frame => !ReferenceEquals(frame, preferred)));
						return new Icon(ordered.Select(frame => new IconFrame(frame.Scale, new Bitmap(frame.Bitmap))));
					}
				}

#endif
				//A module hands back the icon itself at the size asked for, and an image format carries one size,
				//so LoadImage already resolves both -- it just also produces a bitmap this caller has no use for.
				var (bmp, source) = LoadImage(filename, size, size, iconNumber);

				using (bmp)
				{
					if (source is Icon icon)
						return icon;

					(source as IDisposable)?.Dispose();
					return IconFromBitmap(bmp);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>Resolves a TraySetIcon source and selector now, returning a portable .ico payload suitable
		/// for embedding in a compiled script.</summary>
		internal static byte[] LoadIconSetBytes(string filename, object iconNumber)
		{
			try
			{
				using var icon = LoadIconSet(filename, PrepareIconNumber(iconNumber));
				return IconToByteArray(icon);
			}
			catch
			{
				return null;
			}
		}

		internal static object IconNumberForDisplay(object iconNumber) =>
			iconNumber == null ? 1L : iconNumber.TryCoerceLong(out var number) ? number : iconNumber;

		/// <summary>
		/// Turns one bitmap into an icon. The bitmap is only read; the icon owns its own handle.
		/// </summary>
		internal static Icon IconFromBitmap(Bitmap bmp)
		{
			if (bmp == null)
				return null;

#if WINDOWS

			//GetHicon's handle belongs to the caller and Icon.FromHandle does not adopt it, so it is cloned into
			//an icon which owns its handle and the original is released here.
			var handle = bmp.GetHicon();

			try
			{
				using var borrowed = Icon.FromHandle(handle);
				return (Icon)borrowed.Clone();
			}
			finally
			{
				_ = DestroyIcon(handle);
			}

#else
			return new Icon(1f, new Bitmap(bmp));
#endif
		}

		internal static object PrepareIconNumber(object iconnumber)
		{
			if (iconnumber == null)
				return 0;
			else if (iconnumber.TryCoerceLong(out long l) && l > 0)//Note this allows us to pass the icon number as a number (including a Float) or a string, however that also prevents us from loading an icon from a .NET DLL that happens to be named that same number. This is an extremely unlikely scenario.
				return l - 1;
			else
				return iconnumber;
		}

		/// <summary>
		/// Divides a strip into w by h images across its width, from its top rows, as ImageList_AddMasked does: only
		/// whole images, so a strip narrower than one image gives none.
		/// </summary>
		internal static List<Bitmap> SplitBitmap(Bitmap bmp, int w, int h)
		{
			var list = new List<Bitmap>();

			// A zero step would never advance.
			if (w <= 0 || h <= 0)
				return list;

			for (var x = 0; x + w <= bmp.Width; x += w)
				list.Add(CropBitmap(bmp, x, 0, w, Math.Min(h, bmp.Height)));

			return list;
		}
	}

	internal enum ImageHandleKind
	{
		Bitmap = 0,
		Icon = 1
	}

	/// <summary>
	/// Hands out native handles for bitmaps and consumes them again. A Windows handle belongs to the script once it is
	/// handed out, as in AutoHotkey, so nothing is kept for it. On Linux and macOS a handle only names the toolkit image
	/// kept here until a consumer takes it.
	/// </summary>
	internal static class ImageHandleManager
	{
#if !WINDOWS
		private static readonly ConcurrentDictionary<nint, Image> handleCache = new ();
#endif

		/// <summary>Takes ownership of <paramref name="bmp"/> and returns a handle for it: an HICON for an icon, else an
		/// HBITMAP.</summary>
		internal static bool TryAddBitmap(Bitmap bmp, ImageHandleKind kind, out nint handle)
		{
			handle = 0;

			if (bmp == null)
				return false;

#if WINDOWS
			using (bmp)
				handle = kind == ImageHandleKind.Icon ? bmp.GetHicon() : ImageHelper.CreateDibSection(bmp);

			return handle != 0;
#else
#if LINUX
			handle = (bmp.ControlObject as Gdk.Pixbuf)?.Handle ?? nint.Zero;
#elif OSX
			handle = (bmp.ControlObject as NSImage)?.Handle ?? nint.Zero;
#endif

			if (handle == 0)
			{
				bmp.Dispose();
				return false;
			}

			handleCache[handle] = bmp;
			return true;
#endif
		}

#if !WINDOWS
		internal static bool TryGetImage(nint handle, out Image image)
		{
			image = null;
			return handle != 0 && handleCache.TryGetValue(handle, out image);
		}
#endif

		/// <summary>Releases a handle a consumer was given without the "*" that keeps it for the script.</summary>
		internal static void Dispose(nint handle, ImageHandleKind kind)
		{
			if (handle == 0)
				return;

#if WINDOWS
			if (kind == ImageHandleKind.Bitmap)
				_ = WindowsAPI.DeleteObject(handle);
			else
				_ = DestroyIcon(handle);
#else
			if (handleCache.TryRemove(handle, out var image))
				image.Dispose();
#endif
		}
	}
}
