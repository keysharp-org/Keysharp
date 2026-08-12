namespace Keysharp.Internals
{
	/// <summary>
	/// A rectangle in the host platform's native virtual-desktop coordinates. Windows PMv2 and X11 use desktop
	/// pixels; Cocoa and Wayland use native logical units. Bitmap dimensions remain independent raster pixels.
	/// </summary>
	internal readonly record struct ScreenRect(int X, int Y, int Width, int Height)
	{
		internal long Right => (long)X + Width;
		internal long Bottom => (long)Y + Height;
		internal bool HasArea => Width > 0 && Height > 0;
		internal Rectangle ToRectangle() => new(X, Y, Width, Height);
		internal static ScreenRect FromRectangle(Rectangle value) => new(value.X, value.Y, value.Width, value.Height);

		internal static ScreenRect FromRectangle(RectangleF value)
		{
			var left = RoundEdge(value.Left);
			var top = RoundEdge(value.Top);
			var right = RoundEdge(value.Right);
			var bottom = RoundEdge(value.Bottom);
			return new ScreenRect(left, top,
				(int)Math.Clamp((long)right - left, 0, int.MaxValue),
				(int)Math.Clamp((long)bottom - top, 0, int.MaxValue));
		}

		internal ScreenRect Intersect(ScreenRect other)
		{
			var left = Math.Max((long)X, other.X);
			var top = Math.Max((long)Y, other.Y);
			var right = Math.Min((long)X + Width, (long)other.X + other.Width);
			var bottom = Math.Min((long)Y + Height, (long)other.Y + other.Height);
			return right > left && bottom > top
				? new ScreenRect((int)left, (int)top, (int)(right - left), (int)(bottom - top)) : default;
		}

		/// <summary>Maps a raster pixel centre back into native screen coordinates.</summary>
		internal Point PixelToScreen(Point pixel, PixelSize bitmap)
		{
			var x = (long)X + (Width > 0
				? (long)Math.Floor((pixel.X + 0.5) * Width / Math.Max(1, bitmap.Width)) : 0);
			var y = (long)Y + (Height > 0
				? (long)Math.Floor((pixel.Y + 0.5) * Height / Math.Max(1, bitmap.Height)) : 0);
			return new Point((int)Math.Clamp(x, int.MinValue, int.MaxValue),
				(int)Math.Clamp(y, int.MinValue, int.MaxValue));
		}

		/// <summary>Maps a native screen position to the corresponding raster pixel.</summary>
		internal Point ScreenToPixel(int x, int y, PixelSize bitmap)
		{
			var px = MapEdge((long)x - X, Width, bitmap.Width);
			var py = MapEdge((long)y - Y, Height, bitmap.Height);
			return new Point((int)Math.Clamp(px, 0, Math.Max(0L, (long)bitmap.Width - 1)),
				(int)Math.Clamp(py, 0, Math.Max(0L, (long)bitmap.Height - 1)));
		}

		/// <summary>Maps a screen subrectangle to the raster edges used by composition and cropping.</summary>
		internal Rectangle ScreenToPixelBounds(ScreenRect area, PixelSize bitmap)
		{
			var left = Math.Clamp(MapEdge((long)area.X - X, Width, bitmap.Width), 0, bitmap.Width);
			var top = Math.Clamp(MapEdge((long)area.Y - Y, Height, bitmap.Height), 0, bitmap.Height);
			var right = Math.Clamp(MapEdge(area.Right - X, Width, bitmap.Width), left, bitmap.Width);
			var bottom = Math.Clamp(MapEdge(area.Bottom - Y, Height, bitmap.Height), top, bitmap.Height);
			return new Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top));
		}

		private static long MapEdge(long offset, int screenLength, int pixelLength)
			=> screenLength > 0 && pixelLength > 0
				? (long)Math.Ceiling(offset * (double)pixelLength / screenLength - 0.5) : 0;

		private static int RoundEdge(float value)
		{
			if (float.IsNaN(value))
				return 0;

			return (int)Math.Clamp(Math.Round((double)value), int.MinValue, int.MaxValue);
		}
	}

	/// <summary>Integer raster-buffer dimensions.</summary>
	internal readonly record struct PixelSize(int Width, int Height)
	{
		internal bool HasArea => Width > 0 && Height > 0;
	}

	/// <summary>Raster pixels per native screen unit on each axis.</summary>
	internal readonly record struct PixelScale(double X, double Y)
	{
		internal static PixelScale One => new(1.0, 1.0);

		internal static PixelScale From(Bitmap bitmap, ScreenRect bounds)
			=> new(bounds.Width > 0 ? (double)bitmap.Width / bounds.Width : 1.0,
				bounds.Height > 0 ? (double)bitmap.Height / bounds.Height : 1.0);

		internal static PixelScale From(Bitmap bitmap, Rectangle bounds)
			=> From(bitmap, ScreenRect.FromRectangle(bounds));

		internal static PixelScale From(Bitmap bitmap, RectangleF bounds)
			=> From(bitmap, ScreenRect.FromRectangle(bounds));
	}

	/// <summary>
	/// One display in native screen coordinates. <paramref name="SizeScale"/> maps deliberately authored sizes into
	/// native units; it never changes positions. <paramref name="NativeId"/> is valid only for this topology snapshot.
	/// </summary>
	internal readonly record struct DisplayInfo(string Name, ScreenRect Bounds, ScreenRect WorkArea,
		double SizeScale, bool IsPrimary, ulong NativeId = 0);

	/// <summary>
	/// The per-display facts that cost a native query BEYOND plain topology enumeration — EDID reads, DisplayConfig
	/// round-trips, mode queries. Deliberately NOT part of <see cref="DisplayInfo"/>: <c>GetDisplays</c> is on the
	/// path of every <c>MonitorGet</c>, <c>A_ScreenWidth</c>, overlay placement and screen capture, and must stay as
	/// cheap as it is today. This record is produced only when a script actually asks for the metadata.
	/// <para>Every field is "unknown"-tolerant: an empty string or 0 means the platform could not answer, and callers
	/// surface that as an unset script value rather than inventing a plausible-looking one.</para>
	/// </summary>
	internal sealed record DisplayDetails
	{
		/// <summary>Marketing/model name from EDID or the OS ("U2720Q", "Built-in Retina Display").</summary>
		internal string Model { get; init; } = "";

		/// <summary>EDID PNP vendor id ("DEL") or the OS's vendor string.</summary>
		internal string Manufacturer { get; init; } = "";

		/// <summary>Display serial number, when the panel reports one.</summary>
		internal string Serial { get; init; } = "";

		/// <summary>Graphics adapter driving this display ("NVIDIA GeForce RTX 4080").</summary>
		internal string Adapter { get; init; } = "";

		/// <summary>Physical connection: "HDMI", "DisplayPort", "eDP", "DVI", "VGA", "Internal" or "".</summary>
		internal string Connection { get; init; } = "";

		/// <summary>
		/// An identifier that survives reboots and re-plugging, derived from EDID (vendor + product + serial) where
		/// available. Two identical panels reporting the same EDID serial are disambiguated by connector/device path,
		/// which makes the id stable per PORT for that case — documented on the script-facing property.
		/// </summary>
		internal string StableId { get; init; } = "";

		/// <summary>Vertical refresh in Hz (59.94, not 59); 0 when unknown.</summary>
		internal double RefreshRate { get; init; }

		/// <summary>Physical panel size in millimetres; 0 when unknown.</summary>
		internal int PhysicalWidthMm { get; init; }

		/// <summary>Physical panel size in millimetres; 0 when unknown.</summary>
		internal int PhysicalHeightMm { get; init; }

		/// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270.</summary>
		internal int Orientation { get; init; }

		/// <summary>Whether this is a built-in panel (laptop/iMac) rather than an external display.</summary>
		internal bool IsInternal { get; init; }

		internal static readonly DisplayDetails Empty = new();
	}

	internal static class ScaleFactor
	{
		internal static double Normalize(double value, double fallback = 1.0)
			=> double.IsFinite(value) && value > 0 ? value : fallback;

		/// <summary>
		/// The primary display's authored-size scale: 1.0 is 100%, 1.5 is 150%. Not a screen-coordinate
		/// conversion. The script-facing <c>Ks.A_ScreenScale</c> wraps this.
		/// </summary>
		internal static double PrimaryScale
			=> Normalize(Keysharp.Builtins.Monitor.ResolveDisplay(null).Display.SizeScale);
	}

	/// <summary>Shared display-selection rules.</summary>
	internal static class DisplayTopology
	{
		internal static bool TryFind(IReadOnlyList<DisplayInfo> displays, ScreenRect target, out DisplayInfo display)
		{
			display = default;

			if (displays == null || displays.Count == 0)
				return false;

			long bestArea = -1;
			double bestDistance = double.MaxValue;
			var targetRight = (long)target.X + Math.Max(0, target.Width);
			var targetBottom = (long)target.Y + Math.Max(0, target.Height);

			foreach (var candidate in displays)
			{
				var bounds = candidate.Bounds;
				var candidateRight = (long)bounds.X + Math.Max(0, bounds.Width);
				var candidateBottom = (long)bounds.Y + Math.Max(0, bounds.Height);
				var intersection = target.Intersect(bounds);
				var area = (long)intersection.Width * intersection.Height;

				if (!target.HasArea && target.X >= bounds.X && target.X < candidateRight
					&& target.Y >= bounds.Y && target.Y < candidateBottom)
					area = 1;

				var targetRightGap = targetRight < bounds.X ? bounds.X - targetRight
					: candidateRight < target.X ? target.X - candidateRight : 0;
				var targetBottomGap = targetBottom < bounds.Y ? bounds.Y - targetBottom
					: candidateBottom < target.Y ? target.Y - candidateBottom : 0;
				var distance = (double)targetRightGap * targetRightGap
					+ (double)targetBottomGap * targetBottomGap;

				if (area > bestArea || area == bestArea && distance < bestDistance)
				{
					display = candidate;
					bestArea = area;
					bestDistance = distance;
				}
			}

			return true;
		}
	}
}
