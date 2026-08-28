using Keysharp.Internals;

namespace Keysharp.Builtins
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
			{ Keyword_Dir, DirectionRegex() },
			{ Keyword_Variation, VariationRegex() },
			{ "w", WidthRegex() },
			{ "h", HeightRegex() }
		};

		private static readonly Size size1 = new (1, 1);

		/// <summary>
		/// Searches a region of the screen for an image.
		/// </summary>
		/// <param name="outputVarX">
		/// Optional references to the output variables in which to store the X and Y coordinates of the upper-left pixel of where the<br/>
		/// image was found on the screen (if no match is found, the variables are made blank).<br/>
		/// Coordinates are relative to the active window's client area unless CoordMode was used to change that.
		/// </param>
		/// <param name="outputVarY">See <paramref name="outputVarX"/>.</param>
		/// <param name="x1">The X and Y coordinates of the upper left corner of the rectangle to search, which can be expressions.<br/>
		/// Coordinates are relative to the active window unless CoordMode was used to change that.
		/// </param>
		/// <param name="y1">See <paramref name="x1"/>.</param>
		/// <param name="x2">The X and Y coordinates of the lower right corner of the rectangle to search, which can be expressions.<br/>
		/// Coordinates are relative to the active window unless CoordMode was used to change that.
		/// </param>
		/// <param name="y2">See <paramref name="x2"/>.</param>
		/// <param name="imageFile">
		/// <para>The file name of an image, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// All operating systems support GIF, JPG, BMP, ICO, CUR, and ANI images (BMP images must be 16-bit or higher).<br/>
		/// Other sources of icons include the following types of files: EXE, DLL, CPL, SCR, and other types that contain icon resources. On Windows XP or later, additional image formats such as PNG, TIF, Exif, WMF, and EMF are supported. Operating systems older than XP can be given support by copying Microsoft's free GDI+ DLL into the AutoHotkey.exe folder (but in the case of a compiled script, copy the DLL into the script's folder). To download the DLL, search for the following phrase at www.microsoft.com: gdi redistributable</para>
		/// <para>Options: Zero or more of the following strings may also be present immediately before the file name, separated from<br/>
		/// it and from each other by a single space or tab. For example: "*2 *w100 *h-1 C:\Main Logo.bmp"<br/>
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
		/// *DirN: Sets the scan order, where N is a digit from 1 to 9 (the parenthesized sweep is the inner axis):<br/>
		///     1=(Left to Right) Top to Bottom (default), 2=(Right to Left) Top to Bottom, 3=(Left to Right) Bottom to Top, 4=(Right to Left) Bottom to Top,<br/>
		///     5=(Top to Bottom) Left to Right, 6=(Bottom to Top) Left to Right, 7=(Top to Bottom) Right to Left, 8=(Bottom to Top) Right to Left, 9=from the center outwards.<br/>
		///     This only changes which match is returned first when several are present. For example, *Dir2 returns the top-right-most match.<br/>
		/// </para>
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		/// <exception cref="ValueError ">A <see cref="ValueError "/> exception thrown if an invalid parameter was detected or the image could not be loaded.</exception>
		public static object ImageSearch([ByRef][Optional] object outputVarX, [ByRef][Optional] object outputVarY, object x1, object y1, object x2, object y2, object imageFile)
		{
			var _x1 = x1.Ai();
			var _y1 = y1.Ai();
			var _x2 = x2.Ai();
			var _y2 = y2.Ai();
			// As in AHK, options are specified as a series of *-prefixed tokens immediately
			// preceding the file name/handle within the same string, e.g. "*2 *w100 *h-1 C:\Main Logo.bmp".
			var spec = imageFile.As();
			var idx = 0;

			while (idx < spec.Length)
			{
				while (idx < spec.Length && char.IsWhiteSpace(spec[idx]))
					idx++;

				var tokenStart = idx;

				while (idx < spec.Length && !char.IsWhiteSpace(spec[idx]))
					idx++;

				if (tokenStart == idx || spec[tokenStart] != '*')
				{
					idx = tokenStart;
					break;
				}
			}

			var o = spec[..idx];
			var filename = spec[idx..].TrimStart();
			var opts = Options.ParseOptionsRegex(ref o, optsItems, false);
			Bitmap bmp;
			object iconnumber = 0L;
			int w = 0, h = 0;
			long trans = -1;
			byte variation = 0;
			// Scan direction (see ImageFinder.Find). 1 = the legacy top-left, row-major scan,
			// used when *Dir is absent or specifies an invalid value.
			var direction = 1;

			if (opts.TryGetValue(Keyword_Icon, out var iconopt) && iconopt != "")
				iconnumber = ImageHelper.PrepareIconNumber(iconopt);

			if (opts.TryGetValue(Keyword_Dir, out var diropt) && diropt != ""
					&& int.TryParse(diropt, out var d) && d >= 1 && d <= 9)
				direction = d;

			if (opts.TryGetValue(Keyword_Variation, out var varopt) && varopt != "")
				_ = byte.TryParse(varopt, out variation);

			if (opts.TryGetValue(Keyword_Trans, out var vartrans) && vartrans != "")
			{
				var temp = vartrans.ParseLong();

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
				bmp = ImageHelper.LoadImage(filename, w, h, iconnumber, exactPixels: true).Item1;
			}
			catch (Exception ex)
			{
				return Errors.ValueErrorOccurred(ex.Message);
			}

			if (bmp == null)
				return Errors.ValueErrorOccurred($"Loading icon or bitmap from {filename} failed.");

			using (bmp)
			try
			{
				int _px1 = _x1, _py1 = _y1;
				CoordToScreen(ref _x1, ref _y1, CoordMode.Pixel);
				_x2 += _x1 - _px1; _y2 += _y1 - _py1;

				var boundsFailure = ResolveSearchBounds(_x1, _y1, _x2, _y2, out var searchBounds);

				if (boundsFailure != SearchBoundsFailure.None)
					return Errors.ErrorOccurred(boundsFailure == SearchBoundsFailure.OutsideDesktop
						? "The ImageSearch rectangle does not intersect the virtual desktop."
						: "The ImageSearch rectangle is too large to capture as one bitmap.");

				using var capture = GuiHelper.GetScreen(searchBounds.X, searchBounds.Y,
					searchBounds.Width, searchBounds.Height);

				if (capture == null)
					return Errors.ErrorOccurred("Screen capture failed while searching for an image.");

				if (new ImageFinder(capture) { Variation = variation }.Find(bmp, trans, direction) is { } match)
				{
					var location = searchBounds.PixelToScreen(match, new PixelSize(capture.Width, capture.Height));
					int foundX = location.X, foundY = location.Y;
					ScreenToCoord(ref foundX, ref foundY, CoordMode.Pixel);
					if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", (long)foundX);
					if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", (long)foundY);
					return 1L;
				}

				if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", "");
				if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", "");
				return 0L;
			}
			catch (KeysharpException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, "Error searching the screen for an image.");
			}
		}

		/// <summary>
		/// Retrieves the color of the pixel at the specified x,y screen coordinates.
		/// </summary>
		/// <param name="x">The X coordinate of the pixel, which can be expressions. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y">The Y coordinate of the pixel, see <paramref name="x"/>.</param>
		/// <param name="mode">Accepted for AutoHotkey compatibility and ignored; Keysharp always reads the pixel directly.</param>
		/// <returns>The color as a hexadecimal string in red-green-blue (RGB) format.<br/>
		/// For example, the color purple is defined 0x800080 because it has an intensity of 80 for its blue and red<br/>
		/// components but an intensity of 00 for its green component.
		/// </returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		public static string PixelGetColor(object x, object y, object mode = null)
		{
			int pixel;
			var _x = x.Ai();
			var _y = y.Ai();

			try
			{
				CoordToScreen(ref _x, ref _y, CoordMode.Pixel);

				var bounds = new ScreenRect(_x, _y, 1, 1);
				using var capture = GuiHelper.GetScreen(_x, _y, 1, 1);

				if (capture == null)
					return (string)Errors.ErrorOccurred($"Screen capture failed at {_x},{_y}.", DefaultErrorString);

				var sample = bounds.ScreenToPixel(_x, _y, new PixelSize(capture.Width, capture.Height));
				pixel = capture.GetPixel(sample.X, sample.Y).ToArgb() & 0xffffff;

				return $"0x{pixel:X6}";
			}
			catch (KeysharpException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return (string)Errors.OSErrorOccurred(ex, $"Error getting the pixel color at {_x},{_y}.", DefaultErrorString);
			}
		}

		/// <summary>
		/// Searches a region of the screen for a pixel of the specified color.
		/// </summary>
		/// <param name="outputVarX">Optional references to the output variables in which to store the X and Y coordinates of the first pixel that<br/>
		/// matches colorID (if no match is found, the variables are made blank).<br/>
		/// Coordinates are relative to the active window's client area unless CoordMode was used to change that.
		/// </param>
		/// <param name="outputVarY">See <paramref name="outputVarX"/>.</param>
		/// <param name="x1">The X and Y coordinates of the upper left corner of the rectangle to search. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y1">See <paramref name="x1"/>.</param>
		/// <param name="x2">The X and Y coordinates of the lower right corner of the rectangle to search. Coordinates are relative to the active window unless CoordMode was used to change that.</param>
		/// <param name="y2">See <paramref name="x2"/>.</param>
		/// <param name="colorID">The color ID to search for. This is typically expressed as a hexadecimal number in Red-Green-Blue (RGB) format.<br/>
		/// For example: 0x9d6346. Color IDs can be determined using Window Spy (accessible from the tray menu) or via <see cref="PixelGetColor"/>.
		/// </param>
		/// <param name="variation">If omitted, it defaults to 0. Otherwise, specify a number between 0 and 255 (inclusive) to<br/>
		/// indicate the allowed number of shades of variation in either direction for the intensity of the red, green,<br/>
		/// and blue components of the color.
		/// </param>
		/// <returns>This function returns 1 if the color was found in the specified region, or 0 if it was not found.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		public static long PixelSearch([ByRef][Optional] object outputVarX, [ByRef][Optional] object outputVarY, object x1, object y1, object x2, object y2, object colorID, object variation = null)
		{
			var x1v = x1.Ai();
			var y1v = y1.Ai();
			var x2v = x2.Ai();
			var y2v = y2.Ai();
			var colorIDv = colorID.Al();
			var variationv = variation.Al();
			variationv = Math.Clamp(variationv, byte.MinValue, byte.MaxValue);

			int px1 = x1v, py1 = y1v;
			CoordToScreen(ref x1v, ref y1v, CoordMode.Pixel);
			x2v += x1v - px1; y2v += y1v - py1;

			var ltr = x1v <= x2v;
			var ttb = y1v <= y2v;
			var x1temp = Math.Min(x1v, x2v);
			var x2temp = Math.Max(x1v, x2v);
			var y1temp = Math.Min(y1v, y2v);
			var y2temp = Math.Max(y1v, y2v);
			x1v = x1temp;
			x2v = x2temp;
			y1v = y1temp;
			y2v = y2temp;
			var needle = Color.FromArgb((int)((uint)colorIDv | 0xFF000000));

			try
			{
				var boundsFailure = ResolveSearchBounds(x1v, y1v, x2v, y2v, out var bounds);

				if (boundsFailure != SearchBoundsFailure.None)
					return (long)Errors.ErrorOccurred(boundsFailure == SearchBoundsFailure.OutsideDesktop
						? "The PixelSearch rectangle does not intersect the virtual desktop."
						: "The PixelSearch rectangle is too large to capture as one bitmap.", DefaultErrorLong);

				using var capture = GuiHelper.GetScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height);

				if (capture == null)
					return (long)Errors.ErrorOccurred("Screen capture failed while searching for a pixel color.", DefaultErrorLong);

				if (new ImageFinder(capture) { Variation = (byte)variationv }.Find(needle, ltr, ttb) is { } match)
				{
					var location = bounds.PixelToScreen(match, new PixelSize(capture.Width, capture.Height));
					int foundX = location.X, foundY = location.Y;
					ScreenToCoord(ref foundX, ref foundY, CoordMode.Pixel);
					if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", (long)foundX);
					if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", (long)foundY);
					return 1L;
				}

				if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", "");
				if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", "");
				return 0L;
			}
			catch (KeysharpException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return (long)Errors.OSErrorOccurred(ex, "Error searching a region of the screen for a pixel color.", DefaultErrorLong);
			}
		}

		private enum SearchBoundsFailure { None, OutsideDesktop, TooLarge }

		private static SearchBoundsFailure ResolveSearchBounds(int x1, int y1, int x2, int y2,
			out ScreenRect bounds)
		{
			var (virtualLeft, virtualTop, virtualWidth, virtualHeight) = Monitor.GetVirtualScreenBounds();
			var left = Math.Max((long)x1, virtualLeft);
			var top = Math.Max((long)y1, virtualTop);
			var right = Math.Min((long)x2 + 1, virtualLeft + virtualWidth);
			var bottom = Math.Min((long)y2 + 1, virtualTop + virtualHeight);
			var width = right - left;
			var height = bottom - top;

			if (width <= 0 || height <= 0)
			{
				bounds = default;
				return SearchBoundsFailure.OutsideDesktop;
			}

			if (width > int.MaxValue || height > int.MaxValue)
			{
				bounds = default;
				return SearchBoundsFailure.TooLarge;
			}

			bounds = new ScreenRect((int)left, (int)top, (int)width, (int)height);
			return SearchBoundsFailure.None;
		}

		[GeneratedRegex(@"\*Dir([0-9]*)", RegexOptions.IgnoreCase)]
		private static partial Regex DirectionRegex();

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

	public partial class Ks
	{
		/// <summary>
		/// Confines the mouse cursor to a rectangular region of the screen. Subsequent physical
		/// mouse movement is clamped to the rectangle until the clip is released. Calling
		/// <see cref="ClipCursor"/> with no arguments releases any active clip.
		/// </summary>
		/// <param name="x1">The x coordinate of the first corner. Omit all four to release the clip.</param>
		/// <param name="y1">The y coordinate of the first corner.</param>
		/// <param name="x2">The exclusive x coordinate of the opposite corner.</param>
		/// <param name="y2">The exclusive y coordinate of the opposite corner.</param>
		/// <remarks>The corners may be given in any order and are always in screen coordinates.
		/// Throws an <see cref="OSError"/> if clipping is unsupported in the current environment
		/// (e.g. on Wayland without keysharp-inputd and a compositor mouse backend).</remarks>
		public static object ClipCursor(object x1 = null, object y1 = null, object x2 = null, object y2 = null)
		{
			var ht = Script.TheScript.HookThread;

			// No arguments releases any active clip.
			if (x1 == null && y1 == null && x2 == null && y2 == null)
			{
				ht.ClearCursorClip();
				return DefaultObject;
			}

			if (x1 == null || y1 == null || x2 == null || y2 == null)
				return Errors.ValueErrorOccurred("ClipCursor requires either zero or four coordinates.");

			var px1 = x1.Ai();
			var py1 = y1.Ai();
			var px2 = x2.Ai();
			var py2 = y2.Ai();

			ht.SetCursorClip(px1, py1, px2, py2);
			return DefaultObject;
		}
	}
}
