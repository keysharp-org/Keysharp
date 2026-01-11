namespace Keysharp.Core
{
	/// <summary>
	/// A class to put functions which are new to Keysharp that serve as an
	/// improvement/addition to AHK.
	/// </summary>
	public static partial class KeysharpEnhancements
	{
		private static readonly IEnumerable<string> dataFormats = typeof(DataFormats).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Select(f => f.Name);

		/// <summary>
		/// Calls GC.Collect().
		/// According to .NET design guidelines, this should never be necessary.
		/// </summary>
		public static object Collect()
		{
			GC.Collect();
			return DefaultObject;
		}

		/// <summary>
		/// Copies an image to a clipboard.
		/// </summary>
		/// <param name="filename">The filename of the picture, which is usually assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// If the name of a DLL or EXE file is given without a path, it may be loaded from the directory of the current executable or a system directory.
		/// </param>
		/// <param name="options">If blank or omitted, it defaults to no options. Otherwise, specify a string of one or more of the following options,<br/>
		/// each separated from the next with a space or tab:<br/>
		/// Wn and Hn: The width and height to load the image at, where n is an integer. If one dimension is omitted or -1, it is calculated automatically based on the other dimension,<br/>
		/// preserving aspect ratio. If both are omitted, the image's original size is used. If either dimension is 0, the original size is used for that dimension.<br/>
		/// For example: "w80 h50", "w48 h-1" or "w48" (preserve aspect ratio), "h0 w100" (use original height but override width).<br/>
		/// Iconn: Indicates which icon to load from a file with multiple icons (generally an EXE or DLL file). For example, "Icon2" loads the file's second icon.<br/>
		/// Any supported image format can be converted to an icon by specifying "Icon1".
		/// </param>
		public static object CopyImageToClipboard(object filename, object options = null)
		{
			var file = filename.As();
			var opts = options.As();
			var width = int.MinValue;
			var height = int.MinValue;
			var icon = "";
			object iconnumber = 0L;

			foreach (Range r in opts.AsSpan().SplitAny(Spaces))
			{
				var opt = opts.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (Options.TryParse(opt, "w", ref width)) { }
					else if (Options.TryParse(opt, "h", ref height)) { }
					else if (Options.TryParseString(opt, "icon", ref icon)) { iconnumber = ImageHelper.PrepareIconNumber(icon); }
				}
			}

			var ext = Path.GetExtension(file).ToLower();

			if (ext == ".cur")
			{
				using (var cur = new Cursor(file))
				{
					Clipboard.SetImage(ImageHelper.ConvertCursorToBitmap(cur));
				}
			}
			else if (ImageHelper.LoadImage(file, width, height, iconnumber).Item1 is Bitmap bmp)
			{
				Clipboard.SetImage(new Bitmap(bmp));
			}

			return DefaultObject;
		}

		/// <summary>
		/// The clipboard object doesn't provide a way to determine if it's truly empty or not.<br/>
		/// Gotten from: https://www.codeproject.com/questions/1089557/check-with-csharp-if-the-clipboard-is-really-empty <br/>
		/// attempts to provide such functionality.
		/// </summary>
		/// <returns>True if empty, else false.</returns>
		public static bool IsClipboardEmpty() => !dataFormats.Any(
#if WINDOWS
			Clipboard.ContainsData
#else
			Clipboard.Instance.Contains
#endif
		);

		/// <summary>
		/// Shows the debug tab in the main window.
		/// Using this anywhere in the script will also make it persistent.
		/// </summary>
		public static object ShowDebug()
		{
			Script.TheScript.mainWindow?.ShowDebug();
			return DefaultObject;
		}

		/// <summary>
		/// Sends a string followed by a newline to the debugger (if any) for display.
		/// </summary>
		/// <param name="obj0">The text to send to the debugger for display.</param>
		/// <param name="obj1">True to first clear the display, else false to append.</param>
		public static object OutputDebugLine(object obj0, object obj1 = null) => Debug.OutputDebugCommon($"{obj0.As()}{Environment.NewLine}", obj1.Ab());

		/// <summary>
		/// Creates a new <see cref="HashMap"/> object.
		/// </summary>
		/// <param name="obj">The optional data to initialize the <see cref="HashMap"/> with. This can be:<br/>
		///     An existing <see cref="HashMap"/> object.<br/>
		///     An <see cref="Array"/> of key,value pairs.<br/>
		///     An existing <see cref="Dictionary{string, object}"/> object.<br/>
		///     An object[] of key,value pairs.
		/// </param>
		/// <returns>A new <see cref="Map"/> object.</returns>
		public static HashMap HashMap(params object[] obj) => new HashMap(obj);


		/// <summary>
		/// Returns the handle of the window located at the given screen coordinates.
		/// </summary>
		/// <param name="x">The X screen coordinate.</param>
		/// <param name="y">The Y screen coordinate.</param>
		/// <returns>The window handle at the specified point, or 0 if none is found.</returns>
		public static long WinFromPoint(object x, object y)
		{
			if (x == null || y == null)
			{
				GetCursorPos(out var point);
				x ??= point.X; y ??= point.Y;
			}
			return WindowManager.WindowFromPoint(new Common.Window.POINT(x.Ai(), y.Ai())).Handle;
		}
	}
}