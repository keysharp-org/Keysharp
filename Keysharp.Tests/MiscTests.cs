using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class MiscTests : TestRunner
	{
		[Test, Category("Misc"), NonParallelizable]
		public void MiscIs() => Assert.IsTrue(TestScript("misc-is", true));

		[Test, Category("Misc"), NonParallelizable]
		public void MiscObject() => Assert.IsTrue(TestScript("misc-object", true));

		[Test, Category("Misc"), NonParallelizable]
		public void MiscSyntax() => Assert.IsTrue(TestScript("misc-syntax", false));

		// Ks.Font: the option round-trip, the ""-means-unset contract, subclassing, and the Gui/Image
		// integration. Creates Guis but shows none, so it needs no interactive desktop. Not run through
		// the function-wrapped variant, since it declares a class to cover `extends Font`.
		[Test, Category("Misc"), NonParallelizable]
		public void KsFont()
		{
			SkipIfUiInitializationBlocked("Creating an AppKit window requires OS thread 1.");
			Assert.IsTrue(TestScript("ks-font", false));
		}

#if WINDOWS
		// A native IDispatch client reaching a Keysharp object through ObjPtr must be able to pass
		// VT_BYREF|VT_VARIANT out-parameters -- that is how an enumerator hands back its key and value.
		[Test, Category("Misc"), NonParallelizable]
		public void MiscComByRefEnum() => Assert.IsTrue(TestScript("misc-com-byref-enum", false));
#endif

		[Test, Category("Misc"), NonParallelizable]
		public void ComponentDiscovery() => Assert.IsTrue(TestScript("component-available", false));

		[Test, Category("Misc"), NonParallelizable]
		public void MiscReserved() => Assert.IsTrue(TestScript("misc-reserved", false));

		[Test, Category("Misc"), NonParallelizable]
		public void CapabilitiesStatus() => Assert.IsTrue(TestScript("misc-capabilities", true));

		[Test, Category("Misc"), NonParallelizable]
		public void KeyboardLayout() => Assert.IsTrue(TestScript("misc-keyboard-layout", true));

		[Test, Category("Misc"), NonParallelizable]
		public void MiscTimer()
		{
			Assert.IsTrue(TestScript("misc-timer", false));
		}

		[Test, Category("Misc"), NonParallelizable]
		public void SimplePass() => Assert.IsTrue(TestScript("misc-pass", false));

		[Test, Category("Misc"), NonParallelizable]
		public void PropRef() => Assert.IsTrue(TestScript("misc-prop-ref", false));

		[Test, Category("Misc"), NonParallelizable]
		public void VarRefOutputs() => Assert.IsTrue(TestScript("misc-var-ref", false));
	}
}
