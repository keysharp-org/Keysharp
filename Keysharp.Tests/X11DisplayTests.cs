#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.X11;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class X11DisplayTests
	{
		[Test]
		public void MixedScaleSeam()
		{
			ScreenRect[] native =
			[
				new(-3000, 0, 3000, 1800),
				new(0, 0, 1920, 1080)
			];
			ScreenRect[] toolkit =
			[
				new(-2000, 0, 2000, 1200),
				new(0, 0, 1920, 1080)
			];
			var nativeBounds = new ScreenRect(-600, 150, 1200, 450);

			var toolkitBounds = X11DisplayTopology.MapAcrossDisplays(nativeBounds, native, toolkit);

			Assert.That(toolkitBounds, Is.EqualTo(new ScreenRect(-400, 100, 1000, 500)));
			Assert.That(X11DisplayTopology.MapAcrossDisplays(toolkitBounds, toolkit, native),
				Is.EqualTo(nativeBounds));
		}

		[Test]
		public void NegativeOrigin()
		{
			ScreenRect[] native =
			[
				new(-3000, -900, 3000, 1800),
				new(0, 0, 1920, 1080)
			];
			ScreenRect[] toolkit =
			[
				new(-2000, -600, 2000, 1200),
				new(0, 0, 1920, 1080)
			];
			var nativeBounds = new ScreenRect(-2700, -750, 900, 450);

			var toolkitBounds = X11DisplayTopology.MapAcrossDisplays(nativeBounds, native, toolkit);

			Assert.That(toolkitBounds, Is.EqualTo(new ScreenRect(-1800, -500, 600, 300)));
			Assert.That(X11DisplayTopology.MapAcrossDisplays(toolkitBounds, toolkit, native),
				Is.EqualTo(nativeBounds));
		}

	}
}
#endif
