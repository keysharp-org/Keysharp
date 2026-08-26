#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class CosmicCaptureTests
	{
		[Test]
		public void ScreencopyRetirement()
		{
			FakeSession current = new();
			var first = current;
			Assert.That(WaylandScreenCapture.RunWithReusableSession(ref current, _ => (new object(), true)), Is.Not.Null);
			Assert.That(current, Is.SameAs(first));
			Assert.That(WaylandScreenCapture.RunWithReusableSession<FakeSession, object>(ref current,
				_ => (null, true)), Is.Null);
			Assert.That(first.Disposed, Is.False);
			Assert.That(current, Is.SameAs(first));
			Assert.That(WaylandScreenCapture.RunWithReusableSession<FakeSession, object>(ref current,
				_ => (null, false)), Is.Null);
			Assert.That(first.Disposed, Is.True);
			Assert.That(current, Is.Null);
		}

		[Test]
		public void ImageCopyPixelsCoverFormatsTransformsAndStride()
		{
			const int width = 3, height = 2, stride = 5;
			uint[] colors =
			[
				0x10112233u, 0x40556677u, 0x8099AABBu,
				0xC0CCDDEEu, 0xFF123456u, 0x7FABCDEFu
			];
			uint[] formats =
			[
				CosmicImageCapture.WlShmFormatAbgr8888,
				CosmicImageCapture.WlShmFormatXbgr8888,
				WaylandNative.WlShmFormatArgb8888,
				WaylandNative.WlShmFormatXrgb8888
			];
			int[][] sourceByDestination =
			[
				[0, 1, 2, 3, 4, 5], [3, 0, 4, 1, 5, 2],
				[5, 4, 3, 2, 1, 0], [2, 5, 1, 4, 0, 3],
				[2, 1, 0, 5, 4, 3], [0, 3, 1, 4, 2, 5],
				[3, 4, 5, 0, 1, 2], [5, 2, 4, 1, 3, 0]
			];

			foreach (var format in formats)
				for (uint transform = 0; transform < 8; transform++)
				{
					var source = Enumerable.Repeat(0xDEADBEEFu, stride * height).ToArray();

					for (var y = 0; y < height; y++)
						for (var x = 0; x < width; x++)
							source[y * stride + x] = EncodePixel(colors[y * width + x], format);

					var size = CosmicImageCapture.TransformedSize(width, height, transform);

					for (var y = 0; y < size.Height; y++)
						for (var x = 0; x < size.Width; x++)
						{
							var sourceIndex = sourceByDestination[transform][y * size.Width + x];
							var argb = IsOpaque(format) ? colors[sourceIndex] | 0xFF000000u : colors[sourceIndex];
							var expected = EncodePixel(argb, CosmicImageCapture.WlShmFormatAbgr8888);
							Assert.That(CosmicImageCapture.TryReadPixel(source, stride, width, height, format,
								transform, x, y, out var actual), Is.True);
							Assert.That(actual, Is.EqualTo(expected),
								$"format {format:X8}, transform {transform}, destination ({x},{y})");
						}
				}
		}

		[Test]
		public void ImageCopyShmFormatSelection()
		{
			Assert.That(CosmicImageCapture.TryChooseShmFormat(
				[WaylandNative.WlShmFormatArgb8888, CosmicImageCapture.WlShmFormatAbgr8888],
				out var preferred), Is.True);
			Assert.That(preferred, Is.EqualTo(CosmicImageCapture.WlShmFormatAbgr8888));
			Assert.That(CosmicImageCapture.TryChooseShmFormat([0x30334241u], out _), Is.False);
		}

		[Test]
		public void PortalDesktopBounds()
		{
			DisplayInfo[] displays =
			[
				new("left", new ScreenRect(-1920, 120, 1920, 1080), default, 1, false),
				new("primary", new ScreenRect(0, 0, 2560, 1440), default, 1, true),
				new("disconnected", default, default, 1, false)
			];
			Assert.That(PortalScreenCapture.TryDesktopBounds(displays, out var bounds), Is.True);
			Assert.That(bounds, Is.EqualTo(new ScreenRect(-1920, 0, 4480, 1440)));
		}

		[Test]
		public void PortalDesktopBoundsRejectsUnrepresentableUnion()
		{
			DisplayInfo[] displays =
			[
				new("left", new ScreenRect(int.MinValue, 0, 1, 1), default, 1, false),
				new("right", new ScreenRect(int.MaxValue, 0, 1, 1), default, 1, true)
			];
			Assert.That(PortalScreenCapture.TryDesktopBounds(displays, out _), Is.False);
		}

		[Test]
		public void FallbackPolicyIsPrivacyConservative()
		{
			Assert.That(CosmicScreen.ShouldTryPortal(CosmicCaptureStatus.Unavailable), Is.True);
			Assert.That(CosmicScreen.ShouldTryPortal(CosmicCaptureStatus.Failed), Is.True);
			Assert.That(CosmicScreen.ShouldTryPortal(CosmicCaptureStatus.DeniedOrStopped), Is.False);
			Assert.That(CosmicScreen.ShouldTryPortal(CosmicCaptureStatus.Captured), Is.False);
		}

		[Test]
		public void PortalRequiresAnAffineWholeDesktopImage()
		{
			var desktop = new ScreenRect(-1920, 0, 4480, 1440);
			Assert.That(PortalScreenCapture.HasUniformDesktopMapping(desktop, new PixelSize(4480, 1440)), Is.True);
			Assert.That(PortalScreenCapture.HasUniformDesktopMapping(desktop, new PixelSize(8960, 2880)), Is.True);
			Assert.That(PortalScreenCapture.HasUniformDesktopMapping(desktop, new PixelSize(5120, 2160)), Is.False);
		}

		[Test]
		public void PortalSelectsAdvertisedScreenTarget()
		{
			Assert.That(PortalScreenCapture.ChooseTargetMode(2, 0),
				Is.EqualTo(PortalScreenCapture.TargetMode.Legacy));
			Assert.That(PortalScreenCapture.ChooseTargetMode(3, 2 | 4),
				Is.EqualTo(PortalScreenCapture.TargetMode.Unsupported));
			Assert.That(PortalScreenCapture.ChooseTargetMode(3, 1 | 2),
				Is.EqualTo(PortalScreenCapture.TargetMode.Screen));
		}

		private sealed class FakeSession : IDisposable
		{
			internal bool Disposed;
			public void Dispose() => Disposed = true;
		}

		private static bool IsOpaque(uint format)
			=> format is CosmicImageCapture.WlShmFormatXbgr8888 or WaylandNative.WlShmFormatXrgb8888;

		private static uint EncodePixel(uint argb, uint format)
		{
			var alpha = IsOpaque(format) ? 0u : argb & 0xFF000000u;
			return format is WaylandNative.WlShmFormatArgb8888 or WaylandNative.WlShmFormatXrgb8888
				? alpha | (argb & 0x00FFFFFFu)
				: alpha | ((argb & 0xFFu) << 16) | (argb & 0xFF00u) | ((argb >> 16) & 0xFFu);
		}
	}
}
#endif
