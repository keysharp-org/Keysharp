using Keysharp.Internals;

namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for screen-related functions.
	/// </summary>
	public static class Screen
	{
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
		///     mode). Examples: *TransBlack, *TransFFFFAA, *Trans0xFFFFAA. A value that is neither raises a ValueError.<br/>
		/// *wn and *hn: Width and height to which to scale the image (this width and height also determines which icon to load from a multi-icon .ICO file).<br/>
		///     If both these options are omitted, icons loaded from ICO, DLL, or EXE files are scaled to the system's default small-icon size,<br/>
		///     which is usually 16 by 16 (you can force the actual/internal size to be used by specifying *w0 *h0).<br/>
		///     Images that are not icons are loaded at their actual size. To shrink or enlarge the image while preserving its aspect ratio,<br/>
		///     specify -1 for one of the dimensions and a positive number for the other.<br/>
		///     For example, specifying *w200 *h-1 would make the image 200 pixels wide and cause its height to be set automatically.<br/>
		/// *DirName: Sets the scan order with the direction names Image.Search takes: *DirTopLeft (default), *DirTopRight, *DirBottomLeft or<br/>
		///     *DirBottomRight scan rows; *DirLeftTop, *DirLeftBottom, *DirRightTop or *DirRightBottom scan columns; *DirCenter starts nearest<br/>
		///     the center. The first word is the outer sweep and the second the inner one. Names are case-insensitive, and anything else raises a ValueError.<br/>
		///     This only changes which match is returned first when several are present. For example, *DirTopRight returns the top-right-most match.<br/>
		/// </para>
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if an internal function call fails.</exception>
		/// <exception cref="ValueError ">A <see cref="ValueError "/> exception thrown if an invalid parameter was detected or the image could not be loaded.</exception>
		public static object ImageSearch([ByRef][Optional] object outputVarX, [ByRef][Optional] object outputVarY, object x1, object y1, object x2, object y2, object imageFile)
		{
			if (!x1.CoerceInt(out var left) || !y1.CoerceInt(out var top) ||
				!x2.CoerceInt(out var right) || !y2.CoerceInt(out var bottom))
				return DefaultObject;

			if (!imageFile.CoerceString(out var spec))
				return DefaultObject;

			// As in AutoHotkey, options are *-prefixed tokens ahead of the file name or handle in the same string, each
			// read once in order: "*2 *w100 *h-1 C:\Main Logo.bmp".
			var variation = 0;
			var trans = -1L;
			object iconNumber = 0L;
			int? width = null, height = null;
			var direction = 1;
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

				var option = spec.AsSpan(tokenStart + 1, idx - tokenStart - 1);

				if (option.Length > 0 && char.ToUpperInvariant(option[0]) == 'W')
					width = (int)Strings.Atoi(option[1..]);
				else if (option.Length > 0 && char.ToUpperInvariant(option[0]) == 'H')
					height = (int)Strings.Atoi(option[1..]);
				else if (option.StartsWith("Icon", StringComparison.OrdinalIgnoreCase))
				{
					if (option.Length > 4)
						iconNumber = ImageHelper.PrepareIconNumber(option[4..].ToString());
				}
				else if (option.StartsWith("Trans", StringComparison.OrdinalIgnoreCase))
				{
					var name = option[5..].ToString();

					if (!Conversions.TryParseColor(name, out var color))
						return Errors.ValueErrorOccurred($"Invalid *Trans color \"{name}\".", name);

					trans = color.ToArgb() & 0xFFFFFF;
				}
				else if (option.StartsWith("Dir", StringComparison.OrdinalIgnoreCase))
				{
					var name = option[3..].ToString();
					direction = ImageFinder.ParseDirection(name);

					if (direction == 0)
						return Errors.ValueErrorOccurred($"Unknown *Dir direction \"{name}\". Expected {ImageFinder.DirectionNames}.", name);
				}
				else//The only option without a name.
					variation = Math.Clamp((int)Strings.Atoi(option), 0, 255);
			}

			var filename = spec[idx..];

			if (width == null && height == null && Path.GetExtension(filename).ToLowerInvariant() is ".ico" or ".exe" or ".dll")
			{
				width = SystemInformation.SmallIconSize.Width;
				height = SystemInformation.SmallIconSize.Height;
			}

			var (needle, source) = ImageHelper.LoadImage(filename, width ?? 0, height ?? 0, iconNumber, exactPixels: true);

			if (needle == null)
				return Errors.ValueErrorOccurred($"Loading icon or bitmap from {filename} failed.");

			var iconMask = ImageHelper.IsIconSource(filename, source);
			(source as IDisposable)?.Dispose();

			using (needle)
			try
			{
				// As in AutoHotkey, the origin is read once, so the found point is relative to the window searched.
				int originX = 0, originY = 0;
				CoordToScreen(ref originX, ref originY, CoordMode.Pixel);
				var boundsFailure = ResolveSearchBounds(left + originX, top + originY, right + originX, bottom + originY,
														out var searchBounds);

				if (boundsFailure != SearchBoundsFailure.None)
					return Errors.ErrorOccurred(boundsFailure == SearchBoundsFailure.OutsideDesktop
						? "The ImageSearch rectangle does not intersect the virtual desktop."
						: "The ImageSearch rectangle is too large to capture as one bitmap.");

				using var finder = ImageFinder.FromScreen(searchBounds);

				if (finder == null)
					return Errors.ErrorOccurred("Screen capture failed while searching for an image.");

				finder.Variation = (byte)variation;

				if (finder.Find(needle, trans, direction, iconMask) is { } match)
				{
					var location = searchBounds.PixelToScreen(match, new PixelSize(finder.Width, finder.Height));
					if (outputVarX != null) Refs.SetValue(outputVarX, (long)(location.X - originX));
					if (outputVarY != null) Refs.SetValue(outputVarY, (long)(location.Y - originY));
					return 1L;
				}

				if (outputVarX != null) Refs.SetValue(outputVarX, "");
				if (outputVarY != null) Refs.SetValue(outputVarY, "");
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
			if (!x.CoerceInt(out var _x) || !y.CoerceInt(out var _y))
				return "";

			try
			{
				CoordToScreen(ref _x, ref _y, CoordMode.Pixel);
				_ = Script.TheScript?.Permissions?.EnsureScreenCapture(operation: "screen capture");

				if (!Platform.Screen.TryGetPixel(_x, _y, out var pixel))
					return (string)Errors.ErrorOccurred($"Screen capture failed at {_x},{_y}.", DefaultErrorString);

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
			if (!x1.CoerceInt(out var x1v) || !y1.CoerceInt(out var y1v) ||
				!x2.CoerceInt(out var x2v) || !y2.CoerceInt(out var y2v) ||
				!colorID.CoerceLong(out var colorIDv) || !variation.CoerceLong(out var variationv))
				return 0L;

			variationv = Math.Clamp(variationv, byte.MinValue, byte.MaxValue);
			// As in AutoHotkey, the origin is read once, so the found point is relative to the window searched.
			int originX = 0, originY = 0;
			CoordToScreen(ref originX, ref originY, CoordMode.Pixel);
			x1v += originX;
			y1v += originY;
			x2v += originX;
			y2v += originY;
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

				using var finder = ImageFinder.FromScreen(bounds);

				if (finder == null)
					return (long)Errors.ErrorOccurred("Screen capture failed while searching for a pixel color.", DefaultErrorLong);

				finder.Variation = (byte)variationv;

				if (finder.Find(needle, ltr, ttb) is { } match)
				{
					var location = bounds.PixelToScreen(match, new PixelSize(finder.Width, finder.Height));
					if (outputVarX != null) Refs.SetValue(outputVarX, (long)(location.X - originX));
					if (outputVarY != null) Refs.SetValue(outputVarY, (long)(location.Y - originY));
					return 1L;
				}

				if (outputVarX != null) Refs.SetValue(outputVarX, "");
				if (outputVarY != null) Refs.SetValue(outputVarY, "");
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
			var desktop = Platform.Screen.GetVirtualScreenBounds();
			var left = Math.Max((long)x1, desktop.X);
			var top = Math.Max((long)y1, desktop.Y);
			var right = Math.Min((long)x2 + 1, desktop.Right);
			var bottom = Math.Min((long)y2 + 1, desktop.Bottom);
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
		/// (e.g. on Wayland without keysharp-input and a compositor mouse backend).</remarks>
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

			if (!x1.CoerceInt(out var px1) || !y1.CoerceInt(out var py1) ||
				!x2.CoerceInt(out var px2) || !y2.CoerceInt(out var py2))
				return DefaultObject;

			ht.SetCursorClip(px1, py1, px2, py2);
			return DefaultObject;
		}
	}
}
