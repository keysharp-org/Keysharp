#if LINUX
using Keysharp.Internals.Window.Linux;

namespace Keysharp.Tests;

[TestFixture, Category("Internal"), Category("Curated")]
public class LinuxAccessibilityTests
{
	[Test]
	public void CoordinateOrigin()
	{
		// What Chromium, Gecko and GTK 3 reported as native Wayland clients for a top level and its children,
		// against where the compositor had placed the window.
		AssertOrigin(new(0, 32), root: new(0, 0, 2434, 1449), window: new(0, 32, 2434, 1449));
		AssertOrigin(new(66, 426), root: new(20, 20, 1048, 710), window: new(86, 446, 1048, 710));
		AssertOrigin(new(1147, 265), root: new(0, 0, 675, 561), window: new(1167, 285, 635, 521),
			content: new(20, 20, 635, 521));

		// A top level on screen already, at the window's size and at another.
		AssertOrigin(Point.Empty, root: new(300, 200, 800, 600), window: new(300, 200, 800, 600));
		AssertOrigin(Point.Empty, root: new(280, 180, 840, 640), window: new(300, 200, 800, 600));

		// A top level the size of the client area, and one a tiling compositor clipped.
		AssertOrigin(new(300, 230), root: new(0, 0, 800, 570), window: new(300, 200, 800, 600),
			content: Rectangle.Empty, client: new(300, 230, 800, 570));
		AssertOrigin(new(300, 200), root: new(0, 0, 900, 700), window: new(300, 200, 500, 400),
			content: new(0, 0, 900, 700));
	}

	/// <param name="content">The bounds of the top level's children, or null when the root has to settle the
	/// layout without reading them.</param>
	private static void AssertOrigin(Point expected, Rectangle root, Rectangle window, Rectangle? content = null,
		Rectangle client = default)
	{
		var origin = LinuxAccessibility.ResolveCoordinateOrigin(root,
			() => content ?? throw new AssertionException($"The children of {root} were read."), window, client);

		Assert.That(origin, Is.EqualTo(expected), $"root {root} in window {window}");
	}
}
#endif
