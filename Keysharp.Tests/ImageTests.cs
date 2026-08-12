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

		[Test, Category("Image"), Category("Internal")]
		public void DrawImageResources()
		{
			if (Script.IsHeadless)
				Assert.Ignore("Image tests need an initialized graphics backend.");

			var canvas = KeysharpImage.Create(null, 20, 20) as KeysharpImage;
			canvas.mutable = true;
			var source = KeysharpImage.Create(null, 4, 4, "Red") as KeysharpImage;

			for (var i = 0; i < 8; i++)
				_ = canvas.DrawImage(source, 0, 0);

			Assert.AreEqual(0, canvas.PendingResourcesCount);
		}
	}
}
