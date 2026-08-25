#if WINDOWS
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class WindowsOverlayTests : TestRunner
	{
		[Test, Category("Gui")]
		public void DibSourceDcOutlivesCreatorThread()
		{
			DibOverlaySurface surface = null;
			Exception creationError = null;
			var creator = new Thread(() =>
			{
				try { surface = DibOverlaySurface.TryCreate(new PixelSize(8, 4)); }
				catch (Exception ex) { creationError = ex; }
			}) { IsBackground = true };

			creator.Start();
			Assert.IsTrue(creator.Join(TimeSpan.FromSeconds(10)), "surface creation should not block");
			Assert.IsNull(creationError);
			Assert.IsNotNull(surface);

			using (surface)
			{
				using (var graphics = Graphics.FromImage(surface.Bitmap))
				{
					graphics.Clear(Color.Blue);
					graphics.Flush();
				}

				uint pixel = uint.MaxValue;
				Assert.IsTrue(surface.TryAcquireSourceDC(out var dc),
								"the source DC must remain valid after its creator thread exits");

				try
				{
					pixel = WindowsAPI.GetPixel(dc, 1, 1);
				}
				finally
				{
					surface.ReleaseSourceDC();
				}

				Assert.AreNotEqual(uint.MaxValue, pixel, "the source DC must accept reads after its creator thread exits");
				Assert.AreEqual(0x00FF0000u, pixel & 0x00FFFFFFu);
			}
		}
	}
}
#endif
