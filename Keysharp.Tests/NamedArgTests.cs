using System.Collections.Generic;
using Keysharp.Internals.Invoke;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class NamedArgTests : TestRunner
	{
		private string Warnings(string source) =>
			RunScript("#ErrorStdOut\n#Warn NamedArg, StdOut\n" + source,
				"named_arg_" + Guid.NewGuid().ToString("N"), execute: true, exeout: false) ?? "";

		[Test, Category("Misc")]
		public void WarnNamedArgTypo()
		{
			var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, betaa: 3)\n");
			Assert.IsTrue(warning.Contains("betaa"), warning);
			Assert.IsTrue(warning.Contains("alpha") && warning.Contains("beta"), warning);
		}

		[Test, Category("Misc")]
		public void WarnNamedArgValid()
		{
			var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, beta: 3)\n");
			Assert.IsFalse(warning.Contains("not a parameter"), warning);
		}

		[Test, Category("Misc")]
		public void WarnNamedArgDuplicate()
		{
			var warning = Warnings("f(alpha, beta := 2) => alpha\nx := f(1, alpha: 3)\n");
			Assert.IsTrue(warning.Contains("alpha") && warning.Contains("more than once"), warning);
		}

		[Test, Category("Misc")]
		public void ConstructorWarning()
		{
			var warning = Warnings("class W {\n__New(alpha := 1) {\nthis.a := alpha\n}\n}\nx := W(nosuch: 1)\n");
			Assert.IsTrue(warning.Contains("nosuch"), warning);
		}

		[Test, Category("Misc")]
		public void WarnNamedArgBuiltin()
		{
			var warning = Warnings("try b := Buffer(nosuch: 1)\ntry c := Buffer(ByteCount: 4)\n");
			Assert.IsTrue(warning.Contains("nosuch") && warning.Contains("ByteCount") && warning.Contains("FillByte"), warning);
			Assert.IsFalse(warning.Contains("'ByteCount' is not"), warning);
		}

		[Test, Category("Misc")]
		public void DynamicCallWarning()
		{
			var warning = Warnings("f(alpha) => alpha\ng := f\nx := g(nosuch: 1)\n");
			Assert.IsFalse(warning.Contains("not a parameter"), warning);
		}

		[Test, Category("Misc"), Category("Internal")]
		public void ComNamedArgLayout()
		{
			var named = new object[] { "pos0", Script.NamedArgs("Key", "k", "Item", "v") };
			var values = NamedArgBinder.ToComLayout(named, out var names);
			var expected = new Dictionary<string, object> { ["Key"] = "k", ["Item"] = "v" };

			Assert.AreEqual(2, names.Length);
			Assert.AreEqual(3, values.Length);

			for (var i = 0; i < names.Length; i++)
				Assert.AreEqual(expected[names[i]], values[i], $"names[{i}] must name values[{i}]");

			Assert.AreEqual("pos0", values[2]);

			var positional = new object[] { "a", "b" };
			Assert.AreSame(positional, NamedArgBinder.ToComLayout(positional, out var none));
			Assert.IsEmpty(none);
		}

		[Test, Category("Misc"), Category("Internal")]
		public void DispatchLayout()
		{
			var named = new object[] { "pos0", Script.NamedArgs("Key", "k", "Item", "v") };
			var values = NamedArgBinder.StripNames(named, out var names);
			var expected = new Dictionary<string, object> { ["Key"] = "k", ["Item"] = "v" };

			Assert.AreEqual(2, names.Length);
			Assert.AreEqual("pos0", values[0]);

			for (var i = 0; i < names.Length; i++)
				Assert.AreEqual(expected[names[i]], values[1 + i], $"names[{i}] must name values[{1 + i}]");
		}
	}
}
