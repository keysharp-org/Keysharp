#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class CosmicCaptureTests
	{
		[Test]
		public void BrokerDesktopBounds()
		{
			DisplayInfo[] displays =
			[
				new("left", new ScreenRect(-1920, 120, 1920, 1080), default, 1, false),
				new("primary", new ScreenRect(0, 0, 2560, 1440), default, 1, true),
				new("disconnected", default, default, 1, false)
			];
			Assert.That(BrokerDesktopCapture.TryDesktopBounds(displays, out var bounds), Is.True);
			Assert.That(bounds, Is.EqualTo(new ScreenRect(-1920, 0, 4480, 1440)));
		}

		[Test]
		public void BrokerDesktopBoundsRejectsUnrepresentableUnion()
		{
			DisplayInfo[] displays =
			[
				new("left", new ScreenRect(int.MinValue, 0, 1, 1), default, 1, false),
				new("right", new ScreenRect(int.MaxValue, 0, 1, 1), default, 1, true)
			];
			Assert.That(BrokerDesktopCapture.TryDesktopBounds(displays, out _), Is.False);
		}

		[Test]
		public void FallbackPolicyIsPrivacyConservative()
		{
			Assert.That(CosmicScreen.ShouldTryDesktopFallback(DesktopCaptureStatus.Unavailable), Is.True);
			Assert.That(CosmicScreen.ShouldTryDesktopFallback(DesktopCaptureStatus.Failed), Is.True);
			Assert.That(CosmicScreen.ShouldTryDesktopFallback(DesktopCaptureStatus.DeniedOrStopped), Is.False);
			Assert.That(CosmicScreen.ShouldTryDesktopFallback(DesktopCaptureStatus.Captured), Is.False);
		}

		[Test]
		public void BrokerRequiresAnAffineWholeDesktopImage()
		{
			var desktop = new ScreenRect(-1920, 0, 4480, 1440);
			Assert.That(BrokerDesktopCapture.HasUniformDesktopMapping(desktop, new PixelSize(4480, 1440)), Is.True);
			Assert.That(BrokerDesktopCapture.HasUniformDesktopMapping(desktop, new PixelSize(8960, 2880)), Is.True);
			Assert.That(BrokerDesktopCapture.HasUniformDesktopMapping(desktop, new PixelSize(5120, 2160)), Is.False);
		}

	}
}
#endif
