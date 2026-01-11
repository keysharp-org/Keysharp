#if LINUX
using Eto.GtkSharp;
using Keysharp.Core.Linux.Proxies;
using Keysharp.Core.Linux.X11;
#endif
namespace Keysharp.Core
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

		public static Gui Gui(object obj0 = null, object obj1 = null, object obj2 = null) => new ([obj0, obj1, obj2]);

		public static object GuiCtrlFromHwnd(object obj)
		{
			if (Control.FromHandle(new nint(obj.Al())) is Control c)
				if (c.GetGuiControl() is Gui.Control gui)
					return gui;

			return DefaultObject;
		}

		public static object GuiFromHwnd(object obj0, object obj1 = null)
		{
			var hwnd = obj0.Al();
			var recurse = obj1.Ab();
			var allGuiHwnds = Script.TheScript.GuiData.allGuiHwnds;

			if (allGuiHwnds.TryGetValue(hwnd, out var gui))
				return gui;

			foreach (Form f in Application.OpenForms.Cast<Form>())
				if (f is KeysharpForm ksf)
					if (ksf.Tag is WeakReference<Gui> wr && wr.TryGetTarget(out var g))
						if (f.Handle == hwnd)
							return g;

			//Probably isn't needed because it won't have a different result than the OpenForms check above.
			if (Control.FromHandle(new nint(hwnd)) is Control ctrl)
				if (ctrl is KeysharpForm ksf && ksf.Tag is WeakReference<Gui> wr && wr.TryGetTarget(out var g))
					return g;

			if (recurse)
			{
				if (Control.FromHandle(new nint(hwnd)) is Control c)
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

		public static Menu Menu() => new ();

		public static MenuBar MenuBar() => new ();

		public static object MenuFromHandle(object obj)
		{
			var handle = new nint(obj.Al());
			var menu = Control.FromHandle(handle);

			if (menu != null)
				return menu;

			if ((menu = Control.FromHandle(handle)) != null)
				return menu;

			return DefaultObject;
		}

		internal static bool CallMessageHandler(Control control, ref System.Windows.Forms.Message m)
		{
			if (m.HWnd == control.Handle)
			{
				if (control.GetGuiControl() is Gui.Control ctrl)
				{
					var ret = ctrl.InvokeMessageHandlers(ref m);

					if (Script.ForceLong(ret) != 0L)
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
			Bitmap bmp;
			try {
#if WINDOWS
				var format = Forms.Screen.PrimaryScreen.BitsPerPixel switch
				{
					8 or 16 => PixelFormat.Format16bppRgb565,
					24 => PixelFormat.Format24bppRgb,
					32 => PixelFormat.Format32bppArgb,
					_ => PixelFormat.Format32bppArgb,
				};

				bmp = new Bitmap(w, h, format);

				using (var g = Graphics.FromImage(bmp))
				{
					g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
				}
#else
				var format = Forms.Screen.PrimaryScreen.BitsPerPixel switch
				{
					24 => PixelFormat.Format24bppRgb,
					32 => PixelFormat.Format32bppRgb,
					_ => PixelFormat.Format32bppRgb,
				};
				bmp = Eto.Forms.Screen.PrimaryScreen.GetImage(new RectangleF(x, y, w, h)) as Bitmap;
#endif
			} 
			catch
			{
				return null;
			}

			return bmp;
		}

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
#else
				src = icon.ToGdk().PixelBytes.Data;
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
									var newColor = Color.FromArgb((int)originalColor.A, (int)Math.Round(alpha * originalColor.R), (int)Math.Round(alpha * originalColor.G), (int)Math.Round(alpha * originalColor.B));
#else
									var newColor = Color.FromArgb(originalColor.Ab, (int)Math.Round(alpha * originalColor.Rb), (int)Math.Round(alpha * originalColor.Gb), (int)Math.Round(alpha * originalColor.Bb));
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
			public Font Font { get; }
			public nint HFont { get; private set; }

			public Entry(Font f) { Font = f; HFont = f.ToHfont(); }
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

		public static nint Get(Control c)
		{
			if (!table.TryGetValue(c, out var e) || !ReferenceEquals(e.Font, c.Font))
			{
				e?.Dispose();
				e = new Entry(c.Font);
				table.Remove(c);
				table.Add(c, e);

				// ensure cleanup on change/dispose
				c.FontChanged -= OnFontChanged;
				c.Disposed -= OnDisposed;
				c.HandleDestroyed -= OnDisposed;

				c.FontChanged += OnFontChanged;
				c.Disposed += OnDisposed;
				c.HandleDestroyed += OnDisposed;
			}
			return e.HFont;
		}

		private static void OnFontChanged(object sender, EventArgs e) => Release((Control)sender);
		private static void OnDisposed(object sender, EventArgs e) => Release((Control)sender);

		public static void Release(Control c)
		{
			if (table.TryGetValue(c, out var e))
			{
				e.Dispose();
				table.Remove(c);
			}
		}

		[DllImport(WindowsAPI.gdi32)]
		public static extern int GetObject(nint hgdiobj, int cbBuffer, out System.Drawing.Interop.LOGFONT lpvObject);

		[LibraryImport(WindowsAPI.gdi32, EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool DeleteObject(nint hObject);


	}
#endif
}
