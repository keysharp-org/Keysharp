using static Keysharp.Builtins.Misc;
using static Keysharp.Builtins.Mouse;
using static Keysharp.Builtins.Screen;
using Keysharp.Internals.Images;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ScreenTests : TestRunner
	{
		[Test, Category("Screen")]
		public void ImageSearch()
		{
			if (Script.IsHeadless)
				Assert.Ignore("ImageSearch requires a non-headless GUI session.");

			_ = CoordMode("Pixel", "Screen");
			var screenWidth = A_ScreenWidth.Ai();
			var screenHeight = A_ScreenHeight.Ai();
			var capture = KeysharpImage.FromRect(null, 100, 100, 500, 500) as KeysharpImage;
			Assert.IsNotNull(capture, "Image.FromRect should return an Image.");
			var hbitmap = capture.ToBitmap();
			VarRef x = new(null), y = new(null);
			_ = Builtins.Screen.ImageSearch(x, y, 0, 0, screenWidth, screenHeight, "HBITMAP:" + hbitmap);

			if (x.__Value is long lx && lx == 100 && y.__Value is long ly && ly == 100)
				Assert.IsTrue(true);
			else
				Assert.IsTrue(false);

			Assert.IsTrue(TestScript("screen-imagesearch", false));
		}

#if WINDOWS
		[Test, Category("Screen")]
		public void ImageSearchDirection()
		{
			// Headless, deterministic check of the *Dir scan-order ranking in ImageFinder: build a
			// 10x10 source whose only matching (white) pixels sit at known positions, then assert
			// which one each numpad direction returns. ImageFinder works entirely in memory
			// (GDI+ LockBits), so no screen capture or GUI session is involved.
			var white = Color.FromArgb(255, 255, 255, 255);
			var matches = new[]
			{
				new Point(1, 0), new Point(9, 0), new Point(0, 1), new Point(8, 1), new Point(0, 8),
				new Point(1, 9), new Point(8, 9), new Point(9, 8), new Point(4, 4), new Point(5, 5)
			};

			using var source = new Bitmap(10, 10);//Defaults to 32bppArgb; unset pixels read as 0x000000.

			foreach (var m in matches)
				source.SetPixel(m.X, m.Y, white);

			using var needle = new Bitmap(1, 1);
			needle.SetPixel(0, 0, white);

			var finder = new ImageFinder(source) { Variation = 0 };
			// Expected first match per direction (see ImageFinder.Find): 1-4 are row-major,
			// 5-8 column-major, 9 returns the match nearest the region center.
			var expected = new Dictionary<int, Point>
			{
				[1] = new Point(1, 0),//(L→R) T→B: default, row-major early-exit
				[2] = new Point(9, 0),//(R→L) T→B
				[3] = new Point(1, 9),//(L→R) B→T
				[4] = new Point(8, 9),//(R→L) B→T
				[5] = new Point(0, 1),//(T→B) L→R: column-major
				[6] = new Point(0, 8),//(B→T) L→R
				[7] = new Point(9, 0),//(T→B) R→L
				[8] = new Point(9, 8),//(B→T) R→L
				[9] = new Point(5, 5),//from the center outwards
			};

			foreach (var kv in expected)
			{
				var got = finder.Find(needle, -1, kv.Key);
				Assert.IsTrue(got.HasValue, $"Direction {kv.Key} found no match.");
				Assert.AreEqual(kv.Value, got.Value, $"Direction {kv.Key} returned the wrong match.");
			}
		}
#endif

		[Test, Category("Screen")]
		public void PixelGetColor()
		{
			if (Script.IsHeadless)
				Assert.Ignore("PixelGetColor requires a non-headless GUI session.");

			int last = 0, white = 0xffffff, black = 0x000000;
			_ = CoordMode("Pixel", "Screen");
			var screenWidth = A_ScreenWidth.Ai();
			var screenHeight = A_ScreenHeight.Ai();

			for (var i = 0; i < screenHeight; i++)
			{
				for (var j = 0; j < screenWidth; j++)
				{
					var pix = Builtins.Screen.PixelGetColor(j, i);
					Assert.IsTrue(int.TryParse(pix.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ii));

					if (ii != last && ii != white && ii != black)
						goto pass;

					last = ii;
				}
			}

			Assert.IsTrue(false);
			pass:
			Assert.IsTrue(true);
			Assert.IsTrue(TestScript("screen-pixelgetcolor", true));
		}

		[Test, Category("Screen")]
		public void PixelSearch()
		{
			if (Script.IsHeadless)
				Assert.Ignore("PixelSearch requires a non-headless GUI session.");

			int last = 0, white = 0xffffff, black = 0x000000;
			_ = CoordMode("Pixel", "Screen");
			var screenWidth = A_ScreenWidth.Ai();
			var screenHeight = A_ScreenHeight.Ai();

			for (var i = 0; i < screenHeight; i++)
			{
				for (var j = 0; j < screenWidth; j++)
				{
					var pix = Builtins.Screen.PixelGetColor(j, i);
					Assert.IsTrue(int.TryParse(pix.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ii));

					if (ii != last && ii != white && ii != black)
					{
						VarRef outX = new(null);
						VarRef outY = new(null);
						var ret = Builtins.Screen.PixelSearch(outX, outY, j, i, j + 1, i + 1, pix);

						if (ret == 1L && (long)outX.__Value == j && (long)outY.__Value == i)
							goto pass;
						else
							goto fail;
					}

					last = ii;
				}
			}

			pass:
			Assert.IsTrue(true);
			Assert.IsTrue(TestScript("screen-pixelsearch", true));
			return;
			fail:
			Assert.IsTrue(false);
		}
	}
}
