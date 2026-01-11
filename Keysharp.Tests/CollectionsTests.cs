using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Buffer = Keysharp.Core.Buffer;

namespace Keysharp.Tests
{
	public class CollectionsTests : TestRunner
	{
		[Test, Category("Collections"), NonParallelizable]
		public void Array()
		{
			var arr = new Keysharp.Core.Array(
				[
					10L,
					20L,
					30L
				]);
			var index = 0;

			foreach (var (i, v) in (IEnumerable<(object, object)>)arr)
			{
				if (index == 0)
				{
					Assert.AreEqual(i, 1L);
					Assert.AreEqual(v, 10L);
				}
				else if (index == 1)
				{
					Assert.AreEqual(i, 2L);
					Assert.AreEqual(v, 20L);
				}
				else if (index == 2)
				{
					Assert.AreEqual(i, 3L);
					Assert.AreEqual(v, 30L);
				}

				index++;
			}

			index = 0;

			foreach (var (i, _) in (IEnumerable<(object, object)>)arr)
			{
				if (index == 0)
					Assert.AreEqual(i, 1L);
				else if (index == 1)
					Assert.AreEqual(i, 2L);
				else if (index == 2)
					Assert.AreEqual(i, 3L);

				index++;
			}

			index = 0;

			foreach (var (_, v) in (IEnumerable<(object, object)>)arr)
			{
				if (index == 0)
					Assert.AreEqual(v, 10L);
				else if (index == 1)
					Assert.AreEqual(v, 20L);
				else if (index == 2)
					Assert.AreEqual(v, 30L);

				index++;
			}

			Assert.AreEqual(arr.ToString(), "[10, 20, 30]");
			Assert.IsTrue(TestScript("collections-array", true));
		}

		[Test, Category("Collections")]
		public void Map()
		{
			var arr = new Map(
						  [
							  "one", 1L,
							  "two", 2L,
							  "three", 3L
						  ]);

			foreach (var (k, v) in (IEnumerable<(object, object)>)arr)
			{
				if ((string)k == "one")
				{
					Assert.AreEqual(v, 1L);
				}
				else if ((string)k == "two")
				{
					Assert.AreEqual(v, 2L);
				}
				else if ((string)k == "three")
				{
					Assert.AreEqual(v, 3L);
				}
				else
					Assert.IsTrue(false);
			}

			foreach (var (_, v) in (IEnumerable<(object, object)>)arr)
			{
				if ((long)v == 1L)
				{
				}
				else if ((long)v == 2L)
				{
				}
				else if ((long)v == 3L)
				{
				}
				else
					Assert.IsTrue(false);
			}

			foreach (var (k, _) in (IEnumerable<(object, object)>)arr)
			{
				if ((string)k == "one")
				{
				}
				else if ((string)k == "two")
				{
				}
				else if ((string)k == "three")
				{
				}
				else
					Assert.IsTrue(false);
			}

			System.Array sa = new object[6];
			arr.CopyTo(sa, 0);
			Assert.AreEqual(sa.GetValue(0), "one");
			Assert.AreEqual(sa.GetValue(1), 1L);
			Assert.AreEqual(sa.GetValue(2), "three");
			Assert.AreEqual(sa.GetValue(3), 3L);
			Assert.AreEqual(sa.GetValue(4), "two");
			Assert.AreEqual(sa.GetValue(5), 2L);
			//
			sa = new object[3];
			arr.CopyTo(sa, 0);
			Assert.AreEqual(sa.GetValue(0), "one");
			Assert.AreEqual(sa.GetValue(1), 1L);
			Assert.AreEqual(sa.GetValue(2), "three");
			//
			Assert.AreEqual(arr.ToString(), "{\"one\": 1, \"three\": 3, \"two\": 2}");
			Assert.IsTrue(TestScript("collections-map", true));
		}

		[Test, Category("Collections")]
		public void Buffer()
		{
			var buf = new Buffer(5, 10);
			Assert.AreEqual(5L, (long)buf.Size);

			for (var i = 1; i <= (long)buf.Size; i++)
			{
				var p = buf[i];
				Assert.AreEqual(10L, p);
			}

			buf.Size = 10;
			Assert.AreEqual(10L, (long)buf.Size);

			for (var i = 1; i <= 5; i++)//Ensure original values were copied. Subsequent values are undefined.
			{
				var p = buf[i];
				Assert.AreEqual(10L, p);
			}

			Assert.IsTrue(TestScript("collections-buffer", true));
		}
	}
}