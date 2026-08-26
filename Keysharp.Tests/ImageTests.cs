using Keysharp.Internals;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class ImageTests : TestRunner
	{
		[Test, Category("Image")]
		public void Image()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Image tests need an initialized graphics backend.");

			Assert.IsTrue(TestScript("image", false));
		}

		[Test, Category("Image"), Category("Internal")]
		public void DrawImageResources()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Image tests need an initialized graphics backend.");

			var canvas = KeysharpImage.Create(null, 20, 20) as KeysharpImage;
			canvas.eagerDraw = true;
			var source = KeysharpImage.Create(null, 4, 4, "Red") as KeysharpImage;

			for (var i = 0; i < 8; i++)
				_ = canvas.DrawImage(source, 0, 0);

			Assert.AreEqual(0, canvas.PendingResourcesCount);
		}

		[Test, Category("Image"), Category("Internal")]
		public void OverlaySurfaceReadBoundary()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Image tests need an initialized graphics backend.");

			using var surface = OverlaySurface.Plain(new PixelSize(8, 4));
			var canvas = surface.Image;

			_ = canvas.FillRect(0L, 0L, 8L, 4L, "0xFFFF0000");
			AssertPreparedPixel(surface, 0xFFFF0000u);

			// GTK materializes a Pixbuf while snapshotting. Drawing again must target the bitmap's current surface.
			_ = canvas.FillRect(0L, 0L, 8L, 4L, "0xFF00FF00");
			AssertPreparedPixel(surface, 0xFF00FF00u);

			_ = canvas.Clear();
			_ = canvas.FillRect(0L, 0L, 8L, 4L, "0xFF0000FF");
			AssertPreparedPixel(surface, 0xFF0000FFu);
		}

		private static void AssertPreparedPixel(OverlaySurface surface, uint expected)
		{
			using var snapshot = new Bitmap(surface.PrepareForRead());
			Assert.AreEqual(expected, (uint)snapshot.GetPixel(1, 1).ToArgb());
		}

#if LINUX
		[Test, Category("Image"), Category("Internal")]
		public void GtkOverlaySnapshotPreservesArgbPixels()
		{
			using var source = new Bitmap(4, 3, PixelFormat.Format32bppRgba);
			using (var graphics = new Graphics(source))
			{
				graphics.Clear(Colors.Transparent);
				graphics.FillRectangle(Colors.Red, 0, 0, 2, 3);
				graphics.FillRectangle(Colors.Blue, 2, 0, 2, 3);
			}

			using var snapshot = EtoImageOverlay.Snapshot(source);
			Assert.AreEqual(0xFFFF0000u, (uint)snapshot.GetPixel(0, 1).ToArgb());
			Assert.AreEqual(0xFF0000FFu, (uint)snapshot.GetPixel(3, 1).ToArgb());
		}
#endif
	}
}
