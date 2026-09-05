#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Maps a complete logical-desktop image returned by keysharp-desktop onto a requested screen rectangle.
	/// The service decides whether the image comes from a compositor protocol or the desktop portal; Core owns
	/// only the platform-neutral crop and padding semantics used by PixelSearch, ImageSearch and screenshots.
	/// </summary>
	internal static class BrokerDesktopCapture
	{
		internal static DesktopCaptureStatus Capture(ScreenRect bounds,
			IReadOnlyList<DisplayInfo> displays, out Bitmap bitmap)
		{
			bitmap = null;

			if (!bounds.HasArea || !TryDesktopBounds(displays, out var desktop))
				return DesktopCaptureStatus.Failed;

			var intersection = bounds.Intersect(desktop);

			if (!intersection.HasArea)
				return DesktopCaptureStatus.Failed;

			var status = DesktopClient.CaptureGenericDesktopWithStatus(out var desktopImage);

			if (status != DesktopCaptureStatus.Captured)
				return status;

			if (!HasUniformDesktopMapping(desktop,
					new PixelSize(desktopImage.Width, desktopImage.Height)))
			{
				desktopImage.Dispose();
				return DesktopCaptureStatus.Failed;
			}

			var source = desktop.ScreenToPixelBounds(intersection,
				new PixelSize(desktopImage.Width, desktopImage.Height));

			if (source.Width <= 0 || source.Height <= 0)
			{
				desktopImage.Dispose();
				return DesktopCaptureStatus.Failed;
			}

			Bitmap segment;

			try
			{
				segment = intersection == desktop
					? desktopImage
					: ImageHelper.CropBitmap(desktopImage, source.X, source.Y,
						source.Width, source.Height);
			}
			catch (Exception ex)
			{
				desktopImage.Dispose();
				WaylandBridgeDiagnostics.Failure("keysharp-desktop", "crop desktop capture",
					WaylandBridgeDiagnostics.Describe(ex));
				return DesktopCaptureStatus.Failed;
			}

			if (!ReferenceEquals(segment, desktopImage))
				desktopImage.Dispose();

			if (segment == null || segment.Width <= 0 || segment.Height <= 0)
			{
				segment?.Dispose();
				return DesktopCaptureStatus.Failed;
			}

			var captures = new List<(ScreenRect Bounds, Bitmap Pixels)> { (intersection, segment) };

			try
			{
				bitmap = ScreenCaptureComposer.Compose(bounds, captures);
				return bitmap != null ? DesktopCaptureStatus.Captured : DesktopCaptureStatus.Failed;
			}
			finally
			{
				foreach (var capture in captures)
					capture.Pixels.Dispose();
			}
		}

		internal static bool TryDesktopBounds(IReadOnlyList<DisplayInfo> displays,
			out ScreenRect bounds)
		{
			bounds = default;

			if (displays == null || displays.Count == 0)
				return false;

			long left = long.MaxValue, top = long.MaxValue,
				right = long.MinValue, bottom = long.MinValue;

			foreach (var display in displays)
			{
				if (!display.Bounds.HasArea)
					continue;

				left = Math.Min(left, display.Bounds.X);
				top = Math.Min(top, display.Bounds.Y);
				right = Math.Max(right, display.Bounds.Right);
				bottom = Math.Max(bottom, display.Bounds.Bottom);
			}

			if (right <= left || bottom <= top || left < int.MinValue || top < int.MinValue
					|| right > int.MaxValue || bottom > int.MaxValue
					|| right - left > int.MaxValue || bottom - top > int.MaxValue)
				return false;

			bounds = new ScreenRect((int)left, (int)top,
				(int)(right - left), (int)(bottom - top));
			return bounds.HasArea;
		}

		internal static bool HasUniformDesktopMapping(ScreenRect desktop, PixelSize image)
		{
			if (!desktop.HasArea || !image.HasArea)
				return false;

			var cross = Math.Abs((long)image.Width * desktop.Height
				- (long)image.Height * desktop.Width);
			return cross <= Math.Max(image.Width, image.Height);
		}
	}
}
#endif
