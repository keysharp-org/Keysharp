#if WINDOWS

namespace Keysharp.Tests;

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
		Assert.That(surface, Is.Not.Null);

		using (surface)
		{
			using (var graphics = Graphics.FromImage(surface.Bitmap))
			{
				graphics.Clear(Color.Blue);
				graphics.Flush();
			}

			var pixel = uint.MaxValue;
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

			Assert.That(pixel, Is.Not.EqualTo(uint.MaxValue), "the source DC must accept reads after its creator thread exits");
			Assert.That(pixel & 0x00FFFFFFu, Is.EqualTo(0x00FF0000u));
		}
	}
}
#endif
