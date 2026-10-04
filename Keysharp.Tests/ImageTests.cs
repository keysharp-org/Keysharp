using Keysharp.Internals;
using Keysharp.Internals.Images;
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

		[Test, Category("Image")]
		public void VectorDrawing()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Image tests need an initialized graphics backend.");

			Assert.IsTrue(TestScript("image-vector", false));
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

#if WINDOWS
		[TestCase(true, true), TestCase(true, false), TestCase(false, true), TestCase(false, false)]
		[Category("Image"), Category("Internal"), Category("Curated")]
		public void BitmapHandleOrientation(bool topDown, bool hasAlpha)
		{
			const int width = 2, height = 3;
			var colors = new uint[] { 0xFFFF0000u, 0xFF00FF00u, 0xFF0000FFu, 0xFFFFFF00u, 0xFFFF00FFu, 0xFF00FFFFu };
			var pixels = new int[colors.Length];

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var color = colors[y * width + x];
					var pixel = hasAlpha ? 0x80000000u | (color & 0x00808080u) : color & 0x00FFFFFFu;
					pixels[(topDown ? y : height - 1 - y) * width + x] = unchecked((int)pixel);
				}

			var header = new BITMAPINFOHEADER
			{
				biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = width,
				biHeight = topDown ? -height : height,
				biPlanes = 1,
				biBitCount = 32,
			};
			var handle = WindowsAPI.CreateDIBSection(0, ref header, 0, out var bits, 0, 0);
			Assert.AreNotEqual((nint)0, handle);

			try
			{
				Marshal.Copy(pixels, 0, bits, pixels.Length);
				using var bitmap = ImageHelper.GetBitmapFromHBitmap(handle);

				for (var y = 0; y < height; y++)
					for (var x = 0; x < width; x++)
					{
						var expected = colors[y * width + x];

						if (hasAlpha)
							expected = 0x80000000u | (expected & 0x00FFFFFFu);

						Assert.AreEqual(expected, (uint)bitmap.GetPixel(x, y).ToArgb(), $"pixel ({x}, {y})");
					}
			}
			finally
			{
				_ = WindowsAPI.DeleteObject(handle);
			}
		}

		[TestCase(1), TestCase(-1), Category("Image"), Category("Internal")]
		public void RegionalClearStride(int direction)
		{
			const int width = 7, height = 5, stride = 40, guard = 16;
			var expected = Enumerable.Repeat((byte)0xA5, guard * 2 + stride * height).ToArray();
			var memory = Marshal.AllocHGlobal(expected.Length);

			try
			{
				Marshal.Copy(expected, 0, memory, expected.Length);
				var firstRow = memory + guard + (direction < 0 ? stride * (height - 1) : 0);
				using var bitmap = new Bitmap(width, height, direction * stride,
					System.Drawing.Imaging.PixelFormat.Format32bppPArgb, firstRow);
				var region = new PixelRect(2, 1, 3, 2);
				ImageHelper.ClearInPlace(bitmap, 0, region);

				for (var y = region.Y; y < region.Bottom; y++)
				{
					var row = direction > 0 ? y : height - 1 - y;
					expected.AsSpan(guard + row * stride + region.X * 4, region.Width * 4).Clear();
				}

				var actual = new byte[expected.Length];
				Marshal.Copy(memory, actual, 0, actual.Length);

				// Include surrounding pixels, row padding and allocation guards in the comparison.
				for (var i = 0; i < actual.Length; i++)
					Assert.AreEqual(expected[i], actual[i], $"byte {i}, stride {direction * stride}");
			}
			finally
			{
				Marshal.FreeHGlobal(memory);
			}
		}
#endif

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

			using var snapshot = EtoImageOverlay.Snapshot(source, 255);
			Assert.AreEqual(0xFFFF0000u, (uint)snapshot.GetPixel(0, 1).ToArgb());
			Assert.AreEqual(0xFF0000FFu, (uint)snapshot.GetPixel(3, 1).ToArgb());
		}
#endif
	}
}
