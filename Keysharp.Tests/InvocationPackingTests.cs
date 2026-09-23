using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	[TestFixture, Category("Function"), Category("Internal")]
	public class InvocationPackingTests : TestRunner
	{
		[Test]
		public void VariadicPackingPreservesCallerArrays()
		{
			var function = Functions.Closure((Func<object, object[], object>)Fixture.Collect);
			var method = Functions.Closure((Func<object, object, object[], object>)Fixture.CollectReceiver);
			var receiver = new object();

			foreach (var tail in new object[][] { [], [1L], [1L, 2L, 3L] })
			{
				object[] supplied = ["head", ..tail];
				var original = (object[])supplied.Clone();
				var first = (object[])method.CallInst(receiver, supplied);
				Assert.AreEqual(tail, first);
				Assert.AreEqual(original, supplied);
				Assert.AreEqual(tail, method.CallInst(receiver, supplied));
				Assert.AreEqual(tail, function.Call(supplied));
				Assert.AreEqual(original, supplied);
				if (first.Length > 0)
				{
					first[0] = "changed";
					Assert.AreEqual(original, supplied);
				}
			}

			object[] packed = [1L, 2L];
			Assert.AreSame(packed, function.Call("head", (object)packed));
			Assert.AreSame(packed, method.CallInst(receiver, "head", (object)packed));
			var all = Functions.Closure((Func<object[], object>)Fixture.All);
			Assert.AreSame(packed, all.Call(packed));
			Assert.AreEqual(new object[] { receiver, 1L, 2L }, all.CallInst(receiver, packed));
			Assert.AreEqual(new object[] { 1L, 2L }, packed);

			var instance = new Fixture();
			var native = Functions.Closure((Func<object[], object>)instance.InstanceAll);
			Assert.AreSame(packed, native.Call(packed));
			var unbound = new KeysharpFunc(typeof(Fixture).GetMethod(nameof(Fixture.InstanceAll)));
			Assert.AreSame(packed, unbound.Call(instance, (object)packed));
			Assert.AreEqual(packed, unbound.Call(instance, 1L, 2L));
		}

		[Test]
		public void IndexSetterPackingPreservesCallerArrays()
		{
			var setter = Functions.Closure((Func<object, object[], object, object>)Fixture.set_Item);
			var receiver = new object();
			foreach (var keys in new object[][] { [], ["a"], ["a", "b"] })
			{
				object[] supplied = [..keys, 42L];
				var original = (object[])supplied.Clone();
				var result = (object[])setter.CallInst(receiver, supplied);
				Assert.AreEqual(keys, result[0]);
				Assert.AreEqual(42L, result[1]);
				Assert.AreEqual(original, supplied);
			}
			var fixedSetter = Functions.Closure((Func<object, object, object>)Fixture.set_Item);
			Assert.AreEqual(42L, fixedSetter.Call("key", 42L, null));
			var keysSetter = Functions.Closure((Func<object[], object, object>)Fixture.set_Item);
			var receiverValue = (object[])keysSetter.CallInst(receiver);
			Assert.IsEmpty((object[])receiverValue[0]);
			Assert.AreSame(receiver, receiverValue[1]);
		}

		private class Fixture
		{
			public static object Collect(object head, params object[] tail) => tail;
			public static object CollectReceiver(object @this, object head, params object[] tail) => tail;
			public static object All(params object[] args) => args;
			public object InstanceAll(params object[] args) => args;
			public static object set_Item(object @this, object[] keys, object value) => new object[] { keys, value };
			public static object set_Item(object key, object value) => value;
			public static object set_Item(object[] keys, object value) => new object[] { keys, value };
		}
	}
}
