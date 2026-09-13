#if LINUX
using Eto.GtkSharp;
using Keysharp.Internals.Window.Linux.Proxies;
using Keysharp.Internals.Window.Linux.X11;
#endif
using Keysharp.Internals;
namespace Keysharp.Builtins
{
	public static class GuiHelper
	{
		internal static string DefaultGuiId
		{
			get => Script.TheScript.Threads.CurrentThread.defaultGui ?? "1";
			set => Script.TheScript.Threads.CurrentThread.defaultGui = value;
		}

		internal static Form DialogOwner
		{
			get => Script.TheScript.Threads.CurrentThread.dialogOwner;
			set => Script.TheScript.Threads.CurrentThread.dialogOwner = value;
		}

#if !WINDOWS
		// macOS routes the standard text-editing shortcuts (Cmd+C/V/X/A/Z) through the application's
		// Edit menu, so a top-level window with no menu has none of them in its text controls. Give
		// menu-less windows a minimal menu bar with just the App (for Quit) and Edit menus -- the File,
		// Window and View menus aren't useful for most script GUIs. macOS-only: on Linux/GTK the menu bar
		// is in-window, so adding one unprompted would be intrusive (and editing shortcuts work without it).
		internal static void EnsureSystemMenu(Eto.Forms.Window window)
		{
#if OSX
			if (window != null && window.Menu == null)
				window.Menu = new Eto.Forms.MenuBar { IncludeSystemItems = Eto.Forms.MenuBarSystemItems.Quit | Eto.Forms.MenuBarSystemItems.Edit };
#endif
		}
#endif

		public static object GuiCtrlFromHwnd(object hwnd)
		{
			if (Control.FromHandle(new nint(hwnd.Al())) is Control c)
				if (c.GetGuiControl() is Gui.Control gui)
					return gui;

			return DefaultObject;
		}

		public static object GuiFromHwnd(object hwnd, object recurseParent = null)
		{
			var hwndVal = hwnd.Al();
			var recurse = recurseParent.Ab();
			var allGuiHwnds = Script.TheScript.GuiData.allGuiHwnds;

			if (allGuiHwnds.TryGetValue(hwndVal, out var gui))
				return gui;

			foreach (Form f in Application.OpenForms.OfType<Form>())
				if (f is KeysharpForm ksf)
					if (ksf.Tag is WeakReference<Gui> wr && wr.TryGetTarget(out var g))
						if (f.Handle == hwndVal)
						return g;

			//Probably isn't needed because it won't have a different result than the OpenForms check above.
			if (Control.FromHandle(new nint(hwndVal)) is Control ctrl)
				if (ctrl is KeysharpForm ksf && ksf.Tag is WeakReference<Gui> wr && wr.TryGetTarget(out var g))
					return g;

			if (recurse)
			{
				if (Control.FromHandle(new nint(hwndVal)) is Control c)
				{
					while (c.Parent is Control cp)
					{
						if (allGuiHwnds.TryGetValue(cp.Handle.ToInt64(), out gui))
							return gui;

						c = cp;
					}
				}
			}

			return DefaultObject;
		}

		public static object MenuFromHandle(object handle)
		{
			return (object)Control.FromHandle(new nint(handle.Al())) ?? DefaultObject;
		}

		internal static bool CallMessageHandler(Control control, ref Message m)
		{
			if (m.HWnd == control.Handle)
			{
				if (control.GetGuiControl() is Gui.Control ctrl)
				{
					var ret = ctrl.InvokeMessageHandlers(ref m);

					if (ret.Al() != 0L)
						return true;
				}
			}

#if WINDOWS

			// WinForms controls mostly don't respond to window messages, so handle some of them here
			switch (m.Msg)
			{
				case WindowsAPI.PBM_SETBKCOLOR or WindowsAPI.EM_SETBKGNDCOLOR:
					int colorValue = m.LParam.ToInt32();
					Color requestedColor = Color.FromArgb(
											   (colorValue & 0xFF),
											   (colorValue >> 8) & 0xFF,
											   (colorValue >> 16) & 0xFF);

					if (control.BackColor.ToArgb() != requestedColor.ToArgb())
						control.BackColor = requestedColor;

					m.Result = new nint(colorValue);
					return true;

				case WindowsAPI.STM_SETIMAGE:
					control.BackgroundImage = Image.FromHbitmap(m.LParam);
					return true;

				case WindowsAPI.WM_GETFONT:
					m.Result = HFontCache.Get(control);
					return true;
			}

#endif
			return false;
		}

		internal static Icon GetIcon(string source, int n)
		{
#if WINDOWS

			using (var prc = Process.GetCurrentProcess())
			{
				var hPrc = prc.Handle;
				var icon = WindowsAPI.ExtractIcon(hPrc, source, n);

				if (icon != 0)
					return Icon.FromHandle(icon);

				return Icon.ExtractAssociatedIcon(source);
			}

#else
			return null;
#endif
		}

		internal static Bitmap GetScreen(int x, int y, int w, int h)
		{
			// Keep capture permission enforcement centralized for any direct screen grabs.
			_ = Script.TheScript?.Permissions?.EnsureScreenCapture(operation: "screen capture");

			try {
				return Platform.Screen.TryCaptureRegion(new ScreenRect(x, y, w, h), out var bmp) ? bmp : null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Captures the content of a single window directly from the OS, so it works even when the
		/// window is occluded or partially off-screen (unlike a plain screen grab). Returns null when
		/// the platform can't do a true window capture (X11/Wayland), so the caller can fall back to
		/// grabbing the window's screen rectangle with <see cref="GetScreen"/>.
		/// </summary>
		/// <param name="handle">The native window handle: HWND on Windows, CGWindowID on macOS.</param>
		/// <param name="mode">Windows capture technique (see Image.FromWindow): 0/1 = GetDC+BitBlt
		/// (1 forces the window opaque first), 2/3 = PrintWindow (3 forces opaque first), 4 = PrintWindow
		/// + PW_RENDERFULLCONTENT. Ignored on macOS/Linux.</param>
		/// <param name="includeDecoration">Whether to capture the title bar/borders. Honored only by the KWin
		/// Wayland backend (false = client area only); ignored elsewhere, which capture a fixed extent.</param>
		/// <returns>The captured bitmap (the whole window including the title bar, except a client-area-only
		/// KWin capture when <paramref name="includeDecoration"/> is false) and its
		/// image-pixels-per-native-screen-unit scale for each axis, or (null, 1:1) when no true window capture is possible
		/// (the caller then falls back to a screen-rectangle grab).</returns>
		internal static (Bitmap bmp, PixelScale pixelScale) CaptureWindowContent(nint handle, int mode, bool includeDecoration)
		{
			if (handle == 0)
				return (null, PixelScale.One);

			_ = Script.TheScript?.Permissions?.EnsureScreenCapture(operation: "window capture");

			try
			{
#if WINDOWS
				// Windows captures in device pixels (no Retina-style doubling), so scale 1.
				return (CaptureWindowWin(handle, mode), PixelScale.One);
#elif OSX
				var bmp = Keysharp.Internals.Window.MacOS.MacNativeWindows.TryCaptureWindow(unchecked((uint)handle.ToInt64()));

				if (bmp == null)
					return (null, PixelScale.One);

				// The window-server image is physical pixels; the window's logical (point) width comes
				// from its bounds, so their ratio is the HiDPI scale.
				var pixelScale = PixelScale.One;

				if (Keysharp.Internals.Window.MacOS.MacNativeWindows.TryGetWindowInfo(handle, out var info))
					pixelScale = PixelScale.From(bmp, ScreenRect.FromRectangle(info.Bounds));

				return (bmp, pixelScale);
#elif LINUX
				// Per-compositor window capture, resolved once inside Platform.Screen: X11 (incl. Cinnamon) uses
				// XGetImage + XComposite so occluded windows still capture; GNOME images the window actor's buffer
				// via the Keysharp Shell extension; KWin uses ScreenShot2; wlroots/unknown return false so the
				// caller falls back to a rectangle grab of the window's on-screen bounds.
				return Platform.Screen.TryCaptureWindow(handle, includeDecoration, out var wbmp, out var pixelScale)
					? (wbmp, pixelScale)
					: (null, PixelScale.One);
#else
				return (null, PixelScale.One);
#endif
			}
			catch
			{
				return (null, PixelScale.One);
			}
		}

#if WINDOWS
		// Captures the whole window (title bar and borders included) using the requested technique.
		// mode 0/1 use GetWindowDC+BitBlt (1 forces the window opaque first), 2/3 use PrintWindow (3
		// forces opaque first), 4 uses PrintWindow with the undocumented PW_RENDERFULLCONTENT flag.
		private static Bitmap CaptureWindowWin(nint hwnd, int mode)
		{
			if (!WindowsAPI.GetWindowRect(hwnd, out var rc))
				return null;

			int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;

			if (w <= 0 || h <= 0)
				return null;

			var usePrintWindow = mode >= 2;
			var forceOpaque = (mode & 1) != 0;   // modes 1 and 3
			var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			LayeredWindowState saved = default;
			var toggled = false;

			if (forceOpaque)
				toggled = TryForceWindowOpaque(hwnd, out saved);

			try
			{
				using (var g = Graphics.FromImage(bmp))
				{
					var hdc = g.GetHdc();
					bool ok;

					try
					{
						if (usePrintWindow)
						{
							var flags = mode == 4 ? WindowsAPI.PW_RENDERFULLCONTENT : 0u;
							ok = WindowsAPI.PrintWindow(hwnd, hdc, flags);
						}
						else
						{
							var srcDC = WindowsAPI.GetWindowDC(hwnd);   // whole-window DC

							if (srcDC == 0)
							{
								ok = false;
							}
							else
							{
								ok = WindowsAPI.BitBlt(hdc, 0, 0, w, h, srcDC, 0, 0, WindowsAPI.SRCCOPY);
								_ = WindowsAPI.ReleaseDC(hwnd, srcDC);
							}
						}
					}
					finally
					{
						g.ReleaseHdc(hdc);
					}

					if (!ok)
					{
						bmp.Dispose();
						return null;
					}
				}
			}
			finally
			{
				if (toggled)
					RestoreWindowOpacity(hwnd, saved);
			}

			// PrintWindow/BitBlt copy RGB through the HDC but never write the alpha channel, leaving a
			// 32bpp ARGB bitmap fully transparent (it would save as a blank PNG). Force every pixel
			// opaque. PrintWindow also sometimes returns true while rendering nothing (GPU-composited
			// windows); FinalizeCapture reports whether any RGB content was actually present so the
			// caller can fall back to a plain screen grab in that case.
			if (!FinalizeCapture(bmp))
			{
				bmp.Dispose();
				return null;
			}

			return bmp;
		}

		// Saved layered-window state so a temporarily-forced-opaque window can be restored exactly.
		private struct LayeredWindowState
		{
			internal bool wasLayered;
			internal uint crKey;
			internal byte alpha;
			internal uint flags;
		}

		// Forces the window fully opaque (so modes 1/3 capture a clean image of a layered/transparent
		// window), remembering enough to restore it afterwards.
		private static bool TryForceWindowOpaque(nint hwnd, out LayeredWindowState saved)
		{
			saved = default;
			var ex = WindowsAPI.GetWindowLongPtr(hwnd, WindowsAPI.GWL_EXSTYLE).ToInt64();
			saved.wasLayered = (ex & WindowsAPI.WS_EX_LAYERED) != 0;

			if (saved.wasLayered)
			{
				if (!WindowsAPI.GetLayeredWindowAttributes(hwnd, out saved.crKey, out saved.alpha, out saved.flags))
				{
					saved.crKey = 0;
					saved.alpha = 255;
					saved.flags = (uint)WindowsAPI.LWA_ALPHA;
				}
			}
			else
			{
				_ = WindowsAPI.SetWindowLongPtr(hwnd, WindowsAPI.GWL_EXSTYLE, new nint(ex | WindowsAPI.WS_EX_LAYERED));
			}

			_ = WindowsAPI.SetLayeredWindowAttributes(hwnd, 0, 255, (uint)WindowsAPI.LWA_ALPHA);
			return true;
		}

		// Restores the layered state captured by TryForceWindowOpaque.
		private static void RestoreWindowOpacity(nint hwnd, LayeredWindowState saved)
		{
			if (saved.wasLayered)
			{
				_ = WindowsAPI.SetLayeredWindowAttributes(hwnd, saved.crKey, saved.alpha, saved.flags);
			}
			else
			{
				var ex = WindowsAPI.GetWindowLongPtr(hwnd, WindowsAPI.GWL_EXSTYLE).ToInt64();
				_ = WindowsAPI.SetWindowLongPtr(hwnd, WindowsAPI.GWL_EXSTYLE, new nint(ex & ~(long)WindowsAPI.WS_EX_LAYERED));
			}
		}

		// Sets every pixel's alpha to opaque and returns true if any pixel had non-zero RGB content.
		private static unsafe bool FinalizeCapture(Bitmap bmp)
		{
			var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
			byte rgbAccum = 0;

			try
			{
				var basePtr = (byte*)data.Scan0;

				for (var y = 0; y < data.Height; y++)
				{
					var row = basePtr + (long)y * data.Stride;

					for (var x = 0; x < data.Width; x++)
					{
						rgbAccum |= (byte)(row[x * 4 + 0] | row[x * 4 + 1] | row[x * 4 + 2]);
						row[x * 4 + 3] = 0xFF;   // BGRA in memory: alpha is byte 3
					}
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}

			return rgbAccum != 0;
		}
#endif

		internal static string GuiId(ref string command)
		{
			var id = DefaultGuiId;

			if (command.Length == 0)
				return id;

			var z = command.IndexOf(':');
			var pre = string.Empty;

			if (z != -1)
			{
				pre = command.Substring(0, z).Trim();
				z++;
				command = z == command.Length ? string.Empty : command.Substring(z);
			}

			return pre.Length == 0 ? id : pre;
		}

		internal static (string, List<Tuple<int, int, Tuple<string, string>>>) ParseLinkLabelText(string txt)
		{
			var sb = new StringBuilder(txt.Length);
			var splits = txt.Split(["<a", "</a>"], StringSplitOptions.RemoveEmptyEntries);//Do not trim splits here.
			var links = new List<Tuple<int, int, Tuple<string, string>>>();
			var quotes = new char[] { '\'', '\"' };

			foreach (var split in splits)
			{
				var trimSplit = split.TrimStart();

				if (trimSplit.StartsWith("href=", StringComparison.OrdinalIgnoreCase) || trimSplit.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
				{
					var id = "";
					var url = "";
					var pos = split.NthIndexOf("id=", 0, 1, StringComparison.OrdinalIgnoreCase);

					if (pos >= 0)
					{
						var idstartindex = split.NthIndexOfAny(quotes, pos, 1);
						var idstopindex = split.NthIndexOfAny(quotes, pos, 2);

						if (idstartindex >= 0 && idstopindex >= 0)
						{
							idstartindex++;
							id = split.Substring(idstartindex, idstopindex - idstartindex);
						}
					}

					var index1 = split.IndexOf('>') + 1;
					var linktext = split.Substring(index1);
					pos = split.NthIndexOf("href=", 0, 1, StringComparison.OrdinalIgnoreCase);
					index1 = split.NthIndexOfAny(quotes, pos, 1);
					var index2 = split.NthIndexOfAny(quotes, pos, 2);

					if (index1 >= 0 && index2 >= 0)
					{
						index1++;
						url = split.Substring(index1, index2 - index1);
					}

					links.Add(new Tuple<int, int, Tuple<string, string>>(sb.Length, linktext.Length, new Tuple<string, string>(id, url)));
					_ = sb.Append(linktext);
				}
				else
					_ = sb.Append(split);
			}

			var newtxt = sb.ToString();
			return (newtxt, links);
		}

		/// <summary>
		/// The Windows API functions have serious limitations when it comes to loading icons.
		/// They can't load any of size 256 or larger, plus they are platform specific.
		/// This loads the desired size and is cross platform.
		/// Gotten from https://www.codeproject.com/Articles/26824/Extract-icons-from-EXE-or-DLL-files
		/// </summary>
		/// <param name="icon"></param>
		/// <returns></returns>
		internal static List<(Icon, Bitmap)> SplitIcon(Icon icon)
		{
			if (icon == null)
			{
				_ = Errors.UnsetErrorOccurred("icon");
				return default;
			}

            try
            {
                //Get an .ico file in memory, then split it into separate icons and bitmaps.
                byte[] src = null;
#if WINDOWS
                using (var stream = new MemoryStream())
                {
                    icon.Save(stream);
                    src = stream.ToArray();
                }
#elif LINUX
				src = icon.ToGdk().PixelBytes.Data;
#else
				var bitmap = icon.ToBitmap();
				if (bitmap != null)
					return [(icon, bitmap)];
				return default;
#endif

                int count = BitConverter.ToInt16(src, 4);
                var splitIcons = new List<(Icon, Bitmap)>(count);

                for (var i = 0; i < count; i++)
                {
                    var bpp = BitConverter.ToInt16(src, 6 + (16 * i) + 6);//ICONDIRENTRY.wBitCount
                    var length = BitConverter.ToInt32(src, 6 + (16 * i) + 8);//ICONDIRENTRY.dwBytesInRes
                    var offset = BitConverter.ToInt32(src, 6 + (16 * i) + 12);//ICONDIRENTRY.dwImageOffset

					using (var dst = new BinaryWriter(new MemoryStream(6 + 16 + length)))
					{
						dst.Write(src, 0, 4);//Copy ICONDIR and set idCount to 1.
						dst.Write((short)1);
						//Copy ICONDIRENTRY and set dwImageOffset to 22.
						dst.Write(src, 6 + (16 * i), 12);//ICONDIRENTRY except dwImageOffset.
						dst.Write(22);
						dst.Write(src, offset, length);//Copy the image data. This can either be in uncompressed ARGB bitmap format with no header, or compressed PNG with a header.
						_ = dst.BaseStream.Seek(0, SeekOrigin.Begin);//Create an icon from the in-memory file.
						var icon2 = new Icon(dst.BaseStream);
						var bmp = icon2.ToBitmap();

						//If there is an alpha channel on this icon, it needs to be applied here,
						//because to mimic the behavior of raw Windows API calls, alpha must be pre-multiplied.
						if (bpp == 32)
						{
							for (var y = 0; y < bmp.Height; ++y)
							{
								for (var x = 0; x < bmp.Width; ++x)
								{
									var originalColor = bmp.GetPixel(x, y);
									var alpha = originalColor.A / 255.0;
#if WINDOWS
									var newColor = Color.FromArgb((int)originalColor.A, Convert.ToInt32(alpha * originalColor.R), Convert.ToInt32(alpha * originalColor.G), Convert.ToInt32(alpha * originalColor.B));
#else
									var newColor = Color.FromArgb(originalColor.Ab, Convert.ToInt32(alpha * originalColor.Rb), Convert.ToInt32(alpha * originalColor.Gb), Convert.ToInt32(alpha * originalColor.Bb));
#endif
									bmp.SetPixel(x, y, newColor);
								}
							}
						}

						splitIcons.Add((icon2, bmp));
					}
				}

				return splitIcons;
			}
			catch (Exception e)
			{
				_ = Errors.ErrorOccurred($"Error splitting icon: {e.Message}");
				return default;
			}
		}

        private static Control GuiControlGetFocused(Control parent)
		{
			foreach (Control child in parent.Controls)
			{
				if (child.Focused)
					return child;
#if WINDOWS
				else if (child.Controls.Count != 0)
#else
				else if (child.Controls.Count() != 0)
#endif
				{
					var item = GuiControlGetFocused(child);

					if (item != null)
						return item;
				}
			}

			return null;
		}

		//private static Control GuiFindControl(string name, Form gui)
		//{
		//  if (gui == null)
		//      return null;

		//  foreach (Control control in gui.Controls)
		//      if (control.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
		//          return control;

		//  return null;
		//}

		//private static void OnEditKeyPress(object sender, KeyPressEventArgs e)
		//{
		//  if (!(char.IsDigit(e.KeyChar)
		//          || char.IsNumber(e.KeyChar)
		//          || e.KeyChar == '.'
		//          || e.KeyChar == ','
		//          || (int)e.KeyChar == 8
		//          || (int)e.KeyChar == 58
		//          || (int)e.KeyChar == 59))
		//  {
		//      e.Handled = true;
		//  }
		//}
	}

#if WINDOWS
	internal static partial class HFontCache
	{
		private sealed class Entry : IDisposable
		{
			public Font Font { get; private set; }
			public int? Quality { get; set; }
			public nint HFont { get; private set; }

			public Entry(Control control) => Refresh(control);

			public void Refresh(Control control)
			{
				Dispose();
				Font = control.Font;

				if (Quality is not int quality)
				{
					HFont = Font.ToHfont();
					return;
				}

				var logFont = new System.Drawing.Interop.LOGFONT();
				Font.ToLogFont(logFont);
				logFont.lfQuality = (byte)quality;
				HFont = CreateFontIndirect(logFont);
			}

			public void Dispose()
			{
				if (HFont != 0)
				{
					DeleteObject(HFont);
					HFont = 0;
				}
			}
		}

		private static readonly ConditionalWeakTable<Control, Entry> table = new();

		private static Entry GetEntry(Control control)
		{
			if (!table.TryGetValue(control, out var entry))
			{
				entry = new Entry(control);
				table.Add(control, entry);
				control.Disposed += OnDisposed;
				control.HandleCreated += OnHandleCreated;
			}
			else if (!ReferenceEquals(entry.Font, control.Font) || entry.HFont == 0)
				entry.Refresh(control);

			return entry;
		}

		internal static nint Get(Control control) => GetEntry(control).HFont;

		internal static int? GetQuality(Control control)
			=> table.TryGetValue(control, out var entry) ? entry.Quality : null;

		internal static void SetQuality(Control control, int? quality)
		{
			if (quality is int value)
			{
				var entry = GetEntry(control);
				if (entry.Quality != value)
				{
					entry.Quality = value;
					entry.Refresh(control);
				}
			}

			Apply(control);
		}

		internal static void Inherit(Control parent, Control child)
		{
			if (GetQuality(child) == null && GetQuality(parent) is int quality)
				SetQuality(child, quality);
		}

		private static void OnHandleCreated(object sender, EventArgs e) => Apply((Control)sender);

		private static void OnDisposed(object sender, EventArgs e)
		{
			var control = (Control)sender;
			if (table.TryGetValue(control, out var entry))
				entry.Dispose();
			table.Remove(control);
		}

		private static void Apply(Control control)
		{
			if (!control.IsHandleCreated || GetQuality(control) == null)
				return;

			_ = WindowsAPI.SendMessage(control.Handle, (uint)WindowsAPI.WM_SETFONT, Get(control), (nint)1);
		}

		[DllImport(WindowsAPI.gdi32, CharSet = CharSet.Unicode)]
		private static extern nint CreateFontIndirect([In] System.Drawing.Interop.LOGFONT lplf);

		[LibraryImport(WindowsAPI.gdi32, EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool DeleteObject(nint hObject);


	}
#endif
}
