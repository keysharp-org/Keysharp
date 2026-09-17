using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class CollectionsTests : TestRunner
	{
		[Test, Category("Collections"), NonParallelizable]
		public void Array() => Assert.IsTrue(TestScript("collections-array", true));

		[Test, Category("Collections")]
		public void Map() => Assert.IsTrue(TestScript("collections-map", true));

		[Test, Category("Collections")]
		public void HashMap()
		{
			Assert.IsTrue(TestScript("collections-hashmap", false));
		}

		[Test, Category("Collections")]
		public void Buffer() => Assert.IsTrue(TestScript("collections-buffer", true));

		[Test, Category("Collections")]
		public void Object()
		{
			Assert.IsTrue(TestScript("collections-object", false));
		}
	}
}
