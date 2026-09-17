using Keysharp.Internals.Images;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ScreenTests : TestRunner
	{
		[Test, Category("Screen"), Category("Curated")]
		public void ImageSearchOptions() => Assert.IsTrue(TestScript("screen-imagesearch-options", false));

		[Test, Category("Screen")]
		public void ImageSearch()
		{
			if (Script.IsHeadless)
				Assert.Ignore("ImageSearch requires a non-headless GUI session.");

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

			Assert.IsTrue(TestScript("screen-pixelgetcolor", true));
		}

		[Test, Category("Screen")]
		public void PixelSearch()
		{
			if (Script.IsHeadless)
				Assert.Ignore("PixelSearch requires a non-headless GUI session.");

			Assert.IsTrue(TestScript("screen-pixelsearch", true));
		}
	}
}
