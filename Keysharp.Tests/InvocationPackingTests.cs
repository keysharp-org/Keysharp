namespace Keysharp.Tests;

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
			Assert.That(first, Is.EqualTo(tail));
			Assert.That(supplied, Is.EqualTo(original));
			Assert.That(method.CallInst(receiver, supplied), Is.EqualTo(tail));
			Assert.That(function.Call(supplied), Is.EqualTo(tail));
			Assert.That(supplied, Is.EqualTo(original));
			if (first.Length > 0)
			{
				first[0] = "changed";
				Assert.That(supplied, Is.EqualTo(original));
			}
		}

		object[] packed = [1L, 2L];
		Assert.That(function.Call("head", (object)packed), Is.SameAs(packed));
		Assert.That(method.CallInst(receiver, "head", (object)packed), Is.SameAs(packed));
		var all = Functions.Closure((Func<object[], object>)Fixture.All);
		Assert.That(all.Call(packed), Is.SameAs(packed));
		Assert.That(all.CallInst(receiver, packed), Is.EqualTo([receiver, 1L, 2L]));
		Assert.That(packed, Is.EqualTo(new object[] { 1L, 2L }));

		var instance = new Fixture();
		var native = Functions.Closure((Func<object[], object>)instance.InstanceAll);
		Assert.That(native.Call(packed), Is.SameAs(packed));
		var unbound = new KeysharpFunc(typeof(Fixture).GetMethod(nameof(Fixture.InstanceAll)));
		Assert.That(unbound.Call(instance, (object)packed), Is.SameAs(packed));
		Assert.That(unbound.Call(instance, 1L, 2L), Is.EqualTo(packed));
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
			Assert.That(result[0], Is.EqualTo(keys));
			Assert.That(result[1], Is.EqualTo(42L));
			Assert.That(supplied, Is.EqualTo(original));
		}

		var fixedSetter = Functions.Closure((Func<object, object, object>)Fixture.set_Item);
		Assert.That(fixedSetter.Call("key", 42L, null), Is.EqualTo(42L));
		var keysSetter = Functions.Closure((Func<object[], object, object>)Fixture.set_Item);
		var receiverValue = (object[])keysSetter.CallInst(receiver);
		Assert.IsEmpty((object[])receiverValue[0]);
		Assert.That(receiverValue[1], Is.SameAs(receiver));
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
