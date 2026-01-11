#if LINUX
using Eto.GtkSharp;
#endif

namespace Keysharp.Core.Common.Images
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
#else
			return new Bitmap(c.ToGdk().Image.SaveToBuffer("png"));
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

		internal static (Bitmap, object) LoadImage(string filename, int w, int h, object iconindex)
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
						bmp = GetBitmapFromHBitmap(ptr);
						bmp = ResizeBitmap(bmp, w, h);

						if (!dontClear)
							ImageHandleManager.Dispose(ptr, ImageHandleKind.Bitmap);
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
						bmp = ResizeBitmap(bmp, w, h);

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
							bmp = ResizeBitmap(bmp, w, h);

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
								bmp = ResizeBitmap(bmp, w, h);
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
							bmp = ResizeBitmap(bmp, w, h);
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
							bmp = ResizeBitmap(bmp, w, h);
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
						bmp = ResizeBitmap(bmp, w, h);
#endif
					}
					else
					{
						using (var tempBmp = (Bitmap)Image.FromFile(filename))//Must make a copy because the original will keep the file locked.
						{
							bmp = new Bitmap(tempBmp);
							bmp = ResizeBitmap(bmp, w, h);
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

		internal static Bitmap ResizeBitmap(Bitmap bmp, int w, int h)
		{
			if (w <= 0 && h <= 0)
				return bmp;

			if (w <= 0) w = h > 0 && w != 0 ? h : bmp.Width;
			if (h <= 0) h = w > 0 && h != 0 ? w : bmp.Height;

			if (bmp.Width != w || bmp.Height != h)
#if WINDOWS
				bmp = bmp.Resize(w, h);
#else
				bmp = new Bitmap(bmp, w, h, ImageInterpolation.Default);
#endif

			return bmp;
		}

		// Image.FromHbitmap doesn't support transparency, so the following code is used as a workaround.
		// Source: https://stackoverflow.com/questions/9275738/convert-hbitmap-to-bitmap-preserving-alpha-channel
		internal static Bitmap GetBitmapFromHBitmap(nint nativeHBitmap)
		{
			Bitmap bmp = Bitmap.FromHbitmap(nativeHBitmap);
#if WINDOWS
			if (Bitmap.GetPixelFormatSize(bmp.PixelFormat) < 32)
				return bmp;

			BitmapData bmpData;

			if (IsAlphaBitmap(bmp, out bmpData))
				return GetlAlphaBitmapFromBitmapData(bmpData);
#endif
			return bmp;
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

		private static Bitmap GetlAlphaBitmapFromBitmapData(BitmapData bmpData)
		{
			return new Bitmap(
				bmpData.Width,
				bmpData.Height,
				bmpData.Stride,
				PixelFormat.Format32bppArgb,
				bmpData.Scan0);
		}

		private static bool IsAlphaBitmap(Bitmap bmp, out BitmapData bmpData)
		{
			Rectangle bmBounds = new Rectangle(0, 0, bmp.Width, bmp.Height);

			bmpData = bmp.LockBits(bmBounds, ImageLockMode.ReadOnly, bmp.PixelFormat);

			try
			{
				for (int y = 0; y <= bmpData.Height - 1; y++)
				{
					for (int x = 0; x <= bmpData.Width - 1; x++)
					{
						Color pixelColor = Color.FromArgb(
							Marshal.ReadInt32(bmpData.Scan0, (bmpData.Stride * y) + (4 * x)));

						if (pixelColor.A > 0 & pixelColor.A < 255)
						{
							return true;
						}
					}
				}
			}
			finally
			{
				bmp.UnlockBits(bmpData);
			}

			return false;
		}
#endif

		internal static object PrepareIconNumber(object iconnumber)
		{
			if (iconnumber == null)
				return 0;
			else if (iconnumber.ParseLong(false) is long l && l > 0)//Note this allows us to pass the icon number as a string, however that also prevents us from loading an icon from a .NET DLL that happens to be named that same number. This is an extremely unlikely scenario.
				return l - 1;
			else
				return iconnumber;
		}

		internal static List<Bitmap> SplitBitmap(Bitmap bmp, int w, int h)
		{
			var list = new List<Bitmap>();

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
			finally
			{
				if (disposeSource)
					bmp.Dispose();
			}

			if (handle == 0)
				return false;

			handleCache[handle] = new ImageHandleEntry(kind, null, true);
			return true;
#else
			handle = ((Gdk.Pixbuf)bmp.ControlObject).Handle;

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
