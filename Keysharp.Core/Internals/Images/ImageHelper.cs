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
			return new Icon(1.0f, new Bitmap(bytes));
#endif
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

		internal static bool IsIcon(string filename)
		{
			var ext = Path.GetExtension(filename).ToLower();
			return ext == ".exe" || ext == ".dll" || ext == ".icl" || ext == ".cpl" || ext == ".scr" || ext == ".ico";
		}

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
						icon = new Icon(1.0f, new Bitmap(bytes));
#endif
					}
					if (icon != null)
						break;
				}
				catch { }
			}

			return icon;
		}

		/// <param name="exactPixels">See <see cref="ResizeBitmap"/>: pixel consumers
		/// (ImageSearch needles) must pass true so *w/*h options resample real pixels.</param>
		internal static (Bitmap, object) LoadImage(string filename, int w, int h, object iconindex, bool exactPixels = false)
		{
			Bitmap bmp = null;
			object temp = null;

			try
			{
				if (filename.StartsWith("HBITMAP:", StringComparison.OrdinalIgnoreCase))
				{
					var hstr = filename.AsSpan(8);
					var dontClear = hstr[0] == '*';

					if (dontClear)
						hstr = hstr.Trim('*');

					if (long.TryParse(hstr, out var handle))
					{
						var ptr = new nint(handle);

						try
						{
							if (ImageHandleManager.TryGetImage(ptr, out var img) && img is Bitmap cachedBmp)
								bmp = new Bitmap(cachedBmp);
							else
								bmp = GetBitmapFromHBitmap(ptr);

							bmp = ResizeBitmap(bmp, w, h, exactPixels);
						}
						finally
						{
							if (!dontClear)
								ImageHandleManager.Dispose(ptr, ImageHandleKind.Bitmap);
						}
					}
				}
				else if (filename.StartsWith("HICON:", StringComparison.OrdinalIgnoreCase))
				{
					var hstr = filename.AsSpan(6);
					var dontClear = hstr[0] == '*';

					if (dontClear)
						hstr = hstr.Trim('*');

					if (long.TryParse(hstr, out var handle))
					{
						var ptr = new nint(handle);

#if WINDOWS
						using (var tempico = Icon.FromHandle(ptr))
							bmp = tempico.ToBitmap();
						bmp = ResizeBitmap(bmp, w, h, exactPixels);

						if (!dontClear)
							ImageHandleManager.Dispose(ptr, ImageHandleKind.Icon);
#else
						if (ImageHandleManager.TryGetImage(ptr, out var img))
						{
							if (img is Icon ico)
								bmp = ico.ToBitmap();
							else if (img is Bitmap hBmp)
								bmp = hBmp.Clone();
						}

						if (bmp != null)
							bmp = ResizeBitmap(bmp, w, h, exactPixels);

						if (!dontClear)
							ImageHandleManager.Dispose(ptr, ImageHandleKind.Icon);
#endif
					}
				}

				if (bmp == null)//Wasn't a handle, and instead was a filename.
				{
					var ext = Path.GetExtension(filename).ToLower();

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
							var idx = iconindex.Ai();
							ico = ExtractIconWithSizeFromModule(filename, idx, w, h) ?? GuiHelper.GetIcon(filename, idx);
						}

#endif

						if (ico != null)
						{
							bmp = ico.ToBitmap();

							if (w > 0 || h > 0)
							{
								bmp = ResizeBitmap(bmp, w, h, exactPixels);
							}
#if WINDOWS
							else if (bmp.Size != SystemInformation.IconSize)
							{
								bmp = bmp.Resize(SystemInformation.IconSize.Width, SystemInformation.IconSize.Height);
							}
#endif

							temp = ico;
						}
					}
					else if (ext == ".ico")
					{
						if (w > 0 && h < 0) h = w;
						if (h > 0 && w < 0) w = h;

#if WINDOWS
						Icon ico = (w <= 0 || h <= 0) ? new Icon(filename) : new Icon(filename, w, h);

						var icos = GuiHelper.SplitIcon(ico);

						if (w > 0 || h > 0)
						{
							var tempIcoBmp = icos.FirstOrDefault(tempico => (w <= 0 || tempico.Item1.Width == w) && (h <= 0 || tempico.Item1.Height == h));
							var tempIco = tempIcoBmp.Item1;

							if (tempIco == null)
								tempIco = icos[0].Item1;

							temp = tempIco;
							bmp = tempIcoBmp.Item2;
						}
						else
						{
							var iconint = iconindex.Ai(int.MaxValue);

							if (iconint < icos.Count)
							{
								var tempIcoBmp = icos[iconint];
								temp = tempIcoBmp.Item1;
								bmp = tempIcoBmp.Item2;
							}
						}

						if (bmp == null)
						{
							var tempIcoBmp = icos[0];
							temp = tempIcoBmp.Item1;
							bmp = tempIcoBmp.Item2;
						}

						if (w > 0 || h > 0)
							bmp = ResizeBitmap(bmp, w, h, exactPixels);
						else if (bmp.Size != SystemInformation.IconSize)
							bmp = bmp.Resize(SystemInformation.IconSize.Width, SystemInformation.IconSize.Height);
#else
						var ico = new Icon(filename);
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
								var iconint = iconindex.Ai(int.MaxValue);
								frame = iconint >= 0 && iconint < frames.Count ? frames[iconint] : frames[0];
							}

							temp = ico;
							bmp = frame.Bitmap;
						}

						if (bmp != null && (w > 0 || h > 0))
							bmp = ResizeBitmap(bmp, w, h, exactPixels);
#endif
					}
					else if (ext == ".cur")
					{
						var tempcur = new Cursor(filename);
#if WINDOWS
						var curbm = new Bitmap(tempcur.Size.Width, tempcur.Size.Height);

						using (var gr = Graphics.FromImage(curbm))
						{
							tempcur.Draw(gr, new Rectangle(0, 0, tempcur.Size.Width, tempcur.Size.Height));
							bmp = curbm;
							temp = tempcur;
						}
#else
						temp = tempcur;
						bmp = ImageHelper.ConvertCursorToBitmap(tempcur);
						bmp = ResizeBitmap(bmp, w, h, exactPixels);
#endif
					}
					else
					{
						using (var tempBmp = (Bitmap)Image.FromFile(filename))//Must make a copy because the original will keep the file locked.
						{
							bmp = new Bitmap(tempBmp);
							bmp = ResizeBitmap(bmp, w, h, exactPixels);
						}
					}
				}
			}
			catch (Exception ex)
			{
				throw new TypeError(ex.Message);
			}

			return (bmp, temp);
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
		/// Multiplies every pixel's alpha of <paramref name="bmp"/> by <paramref name="alpha"/>/255 (whole-image
		/// opacity for overlay fades), IN PLACE, and returns the same bitmap. The caller decides whether the
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
		/// lock/translate scaffolding mirrors <c>ImageFinder.ToRgbArray</c> (read) and <c>ApplyOpacity</c>
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
			// premultiplied backend sees A=255, making the translate lossless (mirrors ToRgbArray).
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

		internal static Bitmap GetBitmapFromHBitmap(nint nativeHBitmap)
		{
#if WINDOWS
			using var nativeBitmap = Bitmap.FromHbitmap(nativeHBitmap);
			return nativeBitmap.Clone(new Rectangle(Point.Empty, nativeBitmap.Size), nativeBitmap.PixelFormat);
#else
			return Bitmap.FromHbitmap(nativeHBitmap);
#endif
		}

#if WINDOWS
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

			return (count > 0 && hicons[0] != 0) ? Icon.FromHandle(hicons[0]) : null;
		}

#endif

		internal static object PrepareIconNumber(object iconnumber)
		{
			if (iconnumber == null)
				return 0;
			else if (iconnumber.TryCoerceLong(out long l) && l > 0)//Note this allows us to pass the icon number as a number (including a Float) or a string, however that also prevents us from loading an icon from a .NET DLL that happens to be named that same number. This is an extremely unlikely scenario.
				return l - 1;
			else
				return iconnumber;
		}

		internal static List<Bitmap> SplitBitmap(Bitmap bmp, int w, int h)
		{
			var list = new List<Bitmap>();

			// A non-positive tile size cannot tile: the `i += h` / `j += w` steps would never advance, spinning
			// forever (IL_Add on a zero-sized ImageList hit exactly this hang on Linux/macOS). Fall back to a
			// single tile that is the whole bitmap so no caller can lock up.
			if (w <= 0 || h <= 0)
			{
				list.Add(new Bitmap(bmp));
				return list;
			}

			for (var i = 0; i < bmp.Height; i += h)
				for (var j = 0; j < bmp.Width; j += w)
					if (i + h < bmp.Height && j + w < bmp.Width)
#if WINDOWS
						list.Add(bmp.Clone(new Rectangle(j, i, w, h), bmp.PixelFormat));
#else
						list.Add(bmp.Clone(new Rectangle(j, i, w, h)));
#endif

			return list;
		}
	}

	internal enum ImageHandleKind
	{
		Bitmap = 0,
		Icon = 1,
		Cursor = 2
	}

	internal static class ImageHandleManager
	{
		private sealed class ImageHandleEntry
		{
			internal ImageHandleEntry(ImageHandleKind kind, IDisposable owner, bool destroyHandle)
			{
				Kind = kind;
				Owner = owner;
				DestroyHandle = destroyHandle;
			}

			internal ImageHandleKind Kind { get; }
			internal IDisposable Owner { get; }
			internal bool DestroyHandle { get; }
		}

		private static readonly ConcurrentDictionary<nint, ImageHandleEntry> handleCache = new ();

		internal static bool TryAddBitmap(Bitmap bmp, ImageHandleKind kind, out nint handle, bool disposeSource = true)
		{
			handle = 0;

			if (bmp == null)
				return false;

#if WINDOWS
			try
			{
				handle = kind == ImageHandleKind.Icon ? bmp.GetHicon() : bmp.GetHbitmap();
			}
			catch
			{
				if (disposeSource)
					bmp.Dispose();
				throw;
			}

			if (handle == 0)
			{
				if (disposeSource)
					bmp.Dispose();
				return false;
			}

			handleCache[handle] = new ImageHandleEntry(kind, disposeSource ? bmp : new Bitmap(bmp), true);
			return true;
#else
#if LINUX
			handle = (bmp.ControlObject as Gdk.Pixbuf)?.Handle ?? nint.Zero;
#elif OSX
			handle = (bmp.ControlObject as NSImage)?.Handle ?? nint.Zero;
#endif

			if (handle == 0)
			{
				if (disposeSource)
					bmp.Dispose();
				return false;
			}

			handleCache[handle] = new ImageHandleEntry(kind, bmp, false);
			return true;
#endif
		}

		internal static bool TryAddCursor(Cursor cursor, out nint handle)
		{
			handle = 0;

			if (cursor == null)
				return false;

#if WINDOWS
			handle = cursor.Handle;

			if (handle == 0)
				return false;

			handleCache[handle] = new ImageHandleEntry(ImageHandleKind.Cursor, cursor, false);
			return true;
#else
			var bmp = ImageHelper.ConvertCursorToBitmap(cursor);
			var added = TryAddBitmap(bmp, ImageHandleKind.Cursor, out handle);

			if (cursor is IDisposable id)
				id.Dispose();

			return added;
#endif
		}

		internal static bool TryGetImage(nint handle, out Image image)
		{
			image = null;

			if (handle == 0)
				return false;

			if (handleCache.TryGetValue(handle, out var entry) && entry.Owner is Image img)
			{
				image = img;
				return true;
			}

			return false;
		}

		internal static void Dispose(nint handle, ImageHandleKind kind)
		{
			if (handle == 0)
				return;

			if (handleCache.TryRemove(handle, out var entry))
			{
				entry.Owner?.Dispose();
				if (entry.DestroyHandle)
					DestroyHandleForKind(handle, entry.Kind);
				return;
			}

#if WINDOWS
			DestroyHandleForKind(handle, kind);
#endif
		}

		private static void DestroyHandleForKind(nint handle, ImageHandleKind kind)
		{
#if WINDOWS
			if (kind == ImageHandleKind.Bitmap)
				_ = WindowsAPI.DeleteObject(handle);
			else
				_ = DestroyIcon(handle);
#endif
		}
	}
}
