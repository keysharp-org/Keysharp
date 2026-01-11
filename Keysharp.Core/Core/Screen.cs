namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for screen-related functions.
	/// </summary>
	public static partial class Screen
	{
		private static readonly Dictionary<string, Regex> optsItems = new (StringComparer.OrdinalIgnoreCase)
		{
			{ Keyword_Icon, IconRegex() },
			{ Keyword_Trans, TransRegex() },
			{ Keyword_Variation, VariationRegex() },
			{ "w", WidthRegex() },
			{ "h", HeightRegex() }
		};

		private static readonly Size size1 = new (1, 1);

		/// <summary>
		/// Gets a screenclip from a specified region of the screen and return it as a <see cref="Bitmap"/>
		/// </summary>
		/// <param name="left">The x coordinate of the left side of the clip rectangle.</param>
		/// <param name="top">The y coordinate of the top side of the clip rectangle.</param>
		/// <param name="width">The width of the clip rectangle.</param>
		/// <param name="height">The height of the clip rectangle.</param>
		/// <param name="filename">An optional filename to save the clip to. Default: empty, no saving done.</param>
		/// <returns>The clipped region as a <see cref="Bitmap"/>.</returns>
		public static object ImageCapture(object left, object top, object width, object height, object filename = null)
		{
			var x = left.Ai();
			var y = top.Ai();
			var w = width.Ai();
			var h = height.Ai();
			var f = filename.As();

			CoordToScreen(ref x, ref y, CoordMode.Pixel);
			
			var bmp = GuiHelper.GetScreen(x, y, w, h);

			if (f.Length > 0)
				bmp?.Save(f);

			if (bmp != null && ImageHandleManager.TryAddBitmap(bmp, ImageHandleKind.Bitmap, out var handle))
				return handle.ToInt64();

			return 0L;
		}

		/// <summary>
		/// Searches a region of the screen for an image.
		/// </summary>
		/// <param name="outX">
		/// References to the output variables in which to store the X and Y coordinates of the upper-left pixel of where the<br/>
		/// image was found on the screen (if no match is found, the variables are made blank).<br/>
		/// Coordinates are relative to the active window's client area unless CoordMode was used to change that.
		/// </param>
		/// <param name="outY">See <paramref name="outX"/>.</param>
		/// <param name="X1">The X and Y coordinates of the upper left corner of the rectangle to search, which can be expressions.<br/>
		/// Coordinates are relative to the active window unless CoordMode was used to change that.
		/// </param>
		/// <param name="Y1">See <paramref name="X1"/>.</param>
		/// <param name="X2">The X and Y coordinates of the lower right corner of the rectangle to search, which can be expressions.<br/>
		/// Coordinates are relative to the active window unless CoordMode was used to change that.
		/// </param>
		/// <param name="Y2">See <paramref name="X2"/>.</param>
		/// <param name="imageFile">
		/// <para>The file name of an image, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// All operating systems support GIF, JPG, BMP, ICO, CUR, and ANI images (BMP images must be 16-bit or higher).<br/>
		/// Other sources of icons include the following types of files: EXE, DLL, CPL, SCR, and other types that contain icon resources. On Windows XP or later, additional image formats such as PNG, TIF, Exif, WMF, and EMF are supported. Operating systems older than XP can be given support by copying Microsoft's free GDI+ DLL into the AutoHotkey.exe folder (but in the case of a compiled script, copy the DLL into the script's folder). To download the DLL, search for the following phrase at www.microsoft.com: gdi redistributable</para>
		/// <param name="options">
		/// Options: Zero or more of the following strings may be also be present.<br/>
		/// Separate each option from the next with a single space or tab. For example: *2 *w100 *h-1<br/>
		/// *IconN: To use an icon group other than the first one in the file, specify *Icon followed immediately by the number of the group.<br/>
		///     For example, *Icon2 would load the default icon from the second icon group.<br/>
		/// *n (variation): Specify for n a number between 0 and 255 (inclusive) to indicate the allowed number of shades of variation<br/>
		///     in either direction for the intensity of the red, green, and blue components of each pixel's color.<br/>
		///     For example, *2 would allow two shades of variation.<br/>
		///     This parameter is helpful if the coloring of the image varies slightly or if imageFile uses a format such<br/>
		///     as GIF or JPG that does not accurately represent an image on the screen.<br/>
		///     If you specify 255 shades of variation, all colors will match. The default is 0 shades.<br/>
		/// *TransN: This option makes it easier to find a match by specifying one color within the image that will match any color on the screen.<br/>
		///     It is most commonly used to find PNG, GIF, and TIF files that have some transparent areas<br/>
		///     (however, icons do not need this option because their transparency is automatically supported).<br/>
		///     For GIF files, *TransWhite might be most likely to work. For PNG and TIF files, *TransBlack might be best.<br/>
		///     Otherwise, specify for N some other color name or RGB value (see the color chart for guidance, or use <see cref="PixelGetColor"/> in its RGB<br/>
		///     mode). Examples: *TransBlack, *TransFFFFAA, *Trans0xFFFFAA<br/>
		/// *wn and *hn: Width and height to which to scale the image (this width and height also determines which icon to load from a multi-icon .ICO file).<br/>
		///     If both these options are omitted, icons loaded from ICO, DLL, or EXE files are scaled to the system's default small-icon size,<br/>
		///     which is usually 16 by 16 (you can force the actual/internal size to be used by specifying *w0 *h0).<br/>
		///     Images that are not icons are loaded at their actual size. To shrink or enlarge the image while preserving its aspect ratio,<br/>
		///     specify -1 for one of the dimensions and a positive number for the other.<br/>
		///     For example, specifying *w200 *h-1 would make the image 200 pixels wide and cause its height to be set automatically.<br/>
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		/// <exception cref="ValueError ">A <see cref="ValueError "/> exception thrown if an invalid parameter was detected or the image could not be loaded.</exception>
		public static object ImageSearch([ByRef] object outX, [ByRef] object outY, object x1, object y1, object x2, object y2, object imageFile, object options = null)
		{
            outX ??= VarRef.Empty; outY ??= VarRef.Empty;
            var _x1 = x1.Ai();
			var _y1 = y1.Ai();
			var _x2 = x2.Ai();
			var _y2 = y2.Ai();
			var filename = imageFile.As();
			var o = options.As();
			var opts = Options.ParseOptionsRegex(ref o, optsItems, false);
			Bitmap bmp;
			object iconnumber = 0L;
			int w = 0, h = 0;
			long trans = -1;
			byte variation = 0;

			if (opts.TryGetValue(Keyword_Icon, out var iconopt) && iconopt != "")
				iconnumber = ImageHelper.PrepareIconNumber(iconopt);

			if (opts.TryGetValue(Keyword_Variation, out var varopt) && varopt != "")
				_ = byte.TryParse(varopt, out variation);

			if (opts.TryGetValue(Keyword_Trans, out var vartrans) && vartrans != "")
			{
				var temp = vartrans.ParseInt(false);

				if (temp.HasValue)
					trans = temp.Value;
				else
					trans = Color.FromName(vartrans).ToArgb();
			}

			if (opts.TryGetValue("w", out var wopt) && wopt != "")
				_ = int.TryParse(wopt, out w);

			if (opts.TryGetValue("h", out var hopt) && hopt != "")
				_ = int.TryParse(hopt, out h);

			try
			{
				bmp = ImageHelper.LoadImage(filename, w, h, iconnumber).Item1;
			}
			catch (Exception ex)
			{
				return Errors.ValueErrorOccurred(ex.Message);
			}

			if (bmp == null)
				return Errors.ValueErrorOccurred($"Loading icon or bitmap from {filename} failed.");

			Point? location;
			Bitmap source = null;

			try
			{
				int _px1 = _x1, _py1 = _y1;
				CoordToScreen(ref _x1, ref _y1, CoordMode.Pixel);
				_x2 += _x1 - _px1; _y2 += _y1 - _py1;

				var start = new Point(_x1, _y1);
				//Ensure we're not trying to search outside of the screen bounds,
				//because X11 will throw an exception if we do.
				var maxX = Math.Min(A_TotalScreenWidth.Ai(), _x2) - start.X;
				var maxY = Math.Min(A_TotalScreenHeight.Ai(), _y2) - start.Y;
				source = GuiHelper.GetScreen(_x1, _y1, maxX, maxY);
				var searchImg = new ImageFinder(source) { Variation = variation };

				location = searchImg.Find(bmp, trans);
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, "Error searching the screen for an image.");
			}
			finally
			{
				source?.Dispose();
				bmp?.Dispose();
			}

			if (location.HasValue)
			{
				int x = location.Value.X + _x1, y = location.Value.Y + _y1;
				ScreenToCoord(ref x, ref y, CoordMode.Pixel);
				Script.SetPropertyValue(outX, "__Value", (long)x);
                Script.SetPropertyValue(outY, "__Value", (long)y);
			}
			else
			{
                Script.SetPropertyValue(outX, "__Value", "");
                Script.SetPropertyValue(outY, "__Value", "");
			}

			return DefaultObject;
		}

		/// <summary>
		/// Retrieves the color of the pixel at the specified x,y screen coordinates.
		/// </summary>
		/// <param name="x">The X coordinate of the pixel, which can be expressions. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y">The Y coordinate of the pixel, see <paramref name="X"/>.</param>
		/// <returns>The color as a hexadecimal string in red-green-blue (RGB) format.<br/>
		/// For example, the color purple is defined 0x800080 because it has an intensity of 80 for its blue and red<br/>
		/// components but an intensity of 00 for its green component.
		/// </returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		public static string PixelGetColor(object x, object y, object unsed = null)
		{
			int pixel;
			var _x = x.Ai();
			var _y = y.Ai();

			try
			{
				CoordToScreen(ref _x, ref _y, CoordMode.Pixel);

				using (var bmp = GuiHelper.GetScreen(_x, _y, 1, 1))
					pixel = (bmp?.GetPixel(0, 0).ToArgb() & 0xffffff) ?? 0;

				return $"0x{pixel:X6}";
			}
			catch (Exception ex)
			{
				return (string)Errors.OSErrorOccurred(ex, $"Error getting the pixel color at {_x},{_y}.", DefaultErrorString);
			}
		}

		/// <summary>
		/// Searches a region of the screen for a pixel of the specified color.
		/// </summary>
		/// <param name="outX">References to the output variables in which to store the X and Y coordinates of the first pixel that<br/>
		/// matches colorID (if no match is found, the variables are made blank).<br/>
		/// Coordinates are relative to the active window's client area unless CoordMode was used to change that.
		/// </param>
		/// <param name="outY">See <paramref name="outX"/>.</param>
		/// <param name="x1">The X and Y coordinates of the upper left corner of the rectangle to search. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y1">See <paramref name="X1"/>.</param>
		/// <param name="x2">The X and Y coordinates of the lower right corner of the rectangle to search. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y2">See <paramref name="X2"/>.</param>
		/// <param name="colorID">The color ID to search for. This is typically expressed as a hexadecimal number in Red-Green-Blue (RGB) format.<br/>
		/// For example: 0x9d6346. Color IDs can be determined using Window Spy (accessible from the tray menu) or via <see cref="PixelGetColor"/>.
		/// </param>
		/// <param name="variation">If omitted, it defaults to 0. Otherwise, specify a number between 0 and 255 (inclusive) to<br/>
		/// indicate the allowed number of shades of variation in either direction for the intensity of the red, green,<br/>
		/// and blue components of the color.
		/// </param>
		/// <returns>This function returns 1 if the color was found in the specified region, or 0 if it was not found.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		public static long PixelSearch(object outX, object outY, object obj0, object obj1, object obj2, object obj3, object obj4, object obj5 = null)
		{
			outX ??= VarRef.Empty; outY ??= VarRef.Empty;
			var x1 = obj0.Ai();
			var y1 = obj1.Ai();
			var x2 = obj2.Ai();
			var y2 = obj3.Ai();
			var colorID = obj4.Al();
			var variation = obj5.Al();
			variation = Math.Clamp(variation, byte.MinValue, byte.MaxValue);

			int px1 = x1, py1 = y1;
			CoordToScreen(ref x1, ref y1, CoordMode.Pixel);
			x2 += x1 - px1; y2 += y1 - py1;

			var ltr = x1 <= x2;
			var ttb = y1 <= y2;
			var x1temp = Math.Min(x1, x2);
			var x2temp = Math.Max(x1, x2);
			var y1temp = Math.Min(y1, y2);
			var y2temp = Math.Max(y1, y2);
			x1 = x1temp;
			x2 = x2temp;
			y1 = y1temp;
			y2 = y2temp;
			Bitmap source = null;
			ImageFinder finder = null;
			var needle = Color.FromArgb((int)((uint)colorID | 0xFF000000));
			Point? location;

			try
			{
				source = GuiHelper.GetScreen(x1, y1, x2 - x1, y2 - y1);
				finder = new ImageFinder(source) { Variation = (byte)variation };
				location = finder.Find(needle, ltr, ttb);
			}
			catch (Exception ex)
			{
				return (long)Errors.OSErrorOccurred(ex, "Error searching a region of the screen for a pixel color.", DefaultErrorLong);
			}
			finally
			{
				source?.Dispose();
			}

			if (location.HasValue)
			{
				int x = location.Value.X + x1, y = location.Value.Y + y1;
				ScreenToCoord(ref x, ref y, CoordMode.Pixel);
				Script.SetPropertyValue(outX, "__Value", (long)x);
                Script.SetPropertyValue(outY, "__Value", (long)y);
				return 1L;
			}
			else
			{
                Script.SetPropertyValue(outX, "__Value", 0L);
                Script.SetPropertyValue(outY, "__Value", 0L);
				return 0L;
			}
		}

		[GeneratedRegex(@"\*[hH]([-0-9]*)")]
		private static partial Regex HeightRegex();

		[GeneratedRegex(@"\*Icon([0-9a-zA-Z]*)", RegexOptions.IgnoreCase)]
		private static partial Regex IconRegex();

		[GeneratedRegex(@"\*Trans([0-9a-zA-Z]*)", RegexOptions.IgnoreCase)]
		private static partial Regex TransRegex();

		[GeneratedRegex(@"\*([0-9]*)")]
		private static partial Regex VariationRegex();

		[GeneratedRegex(@"\*[wW]([-0-9]*)")]
		private static partial Regex WidthRegex();
	}
}
