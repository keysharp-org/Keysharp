using Keysharp.Internals.Invoke;
using static Keysharp.Builtins.Types;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class MiscTests : TestRunner
	{
		[Test, Category("Misc"), NonParallelizable]
		public void MiscIs()
		{
			var x = 1;
			var o = new Keysharp.Builtins.Array(10, 20, 30);
			var map = new Keysharp.Builtins.Map("one", 1, "two", 2, "three", 3);
			Assert.IsTrue(IsInteger(x) == 1);
			x = -1;
			Assert.IsTrue(IsInteger(x) == 1);
			var d = 1.234;
			Assert.IsTrue(IsInteger(d) == 0);
			var f = 1.234f;
			Assert.IsTrue(IsInteger(f) == 0);
			var m = 1.234m;
			Assert.IsTrue(IsInteger(m) == 0);
			var s = "1234";
			Assert.IsTrue(IsInteger(s) == 1);
			s = "-1234";
			Assert.IsTrue(IsInteger(s) == 1);
			s = "+1234";
			Assert.IsTrue(IsInteger(s) == 1);
			s = "1234.1234";
			Assert.IsTrue(IsInteger(s) == 0);
			s = "-1234.1234";
			Assert.IsTrue(IsInteger(s) == 0);
			s = "+1234.1234";
			Assert.IsTrue(IsInteger(s) == 0);
			Assert.IsTrue(IsInteger(o) == 0);
			s = "A";
			Assert.IsTrue(IsInteger(s) == 0);
			s = "ABCDEF";
			Assert.IsTrue(IsInteger(s) == 0);
			s = "0xA";
			Assert.IsTrue(IsInteger(s) == 1);
			s = "0xABCDEF";
			Assert.IsTrue(IsInteger(s) == 1);
			//
			d = 1.234;
			Assert.IsTrue(IsFloat(d) == 1);
			d = -1.234;
			Assert.IsTrue(IsFloat(d) == 1);
			f = 1.234f;
			Assert.IsTrue(IsFloat(f) == 1);
			m = 1.234m;
			Assert.IsTrue(IsFloat(m) == 1);
			s = "1234";
			Assert.IsTrue(IsFloat(s) == 0);
			s = "-1234";
			Assert.IsTrue(IsFloat(s) == 0);
			s = "+1234";
			Assert.IsTrue(IsFloat(s) == 0);
			Assert.IsTrue(IsFloat(o) == 0);
			//
			Assert.IsTrue(IsNumber(0) == 1);
			Assert.IsTrue(IsNumber(1) == 1);
			Assert.IsTrue(IsNumber(-1) == 1);
			Assert.IsTrue(IsNumber(1.234) == 1);
			Assert.IsTrue(IsNumber(-1.234) == 1);
			Assert.IsTrue(IsNumber("1234") == 1);
			Assert.IsTrue(IsNumber("-1234") == 1);
			Assert.IsTrue(IsNumber("+1234") == 1);
			Assert.IsTrue(IsNumber("1.234") == 1);
			Assert.IsTrue(IsNumber("-1.234") == 1);
			Assert.IsTrue(IsNumber("+1.234") == 1);
			Assert.IsTrue(IsNumber(o) == 0);
			//
			Assert.IsTrue(IsObject(0) == 0);
			Assert.IsTrue(IsObject(1.234) == 0);
			Assert.IsTrue(IsObject("test") == 0);
			Assert.IsTrue(IsObject(o) == 1);
			Assert.IsTrue(IsObject(map) == 1);
			//
			Assert.IsTrue(IsDigit(1) == 1);
			Assert.IsTrue(IsDigit(-1) == 0);
			Assert.IsTrue(IsDigit(1.234) == 0);
			Assert.IsTrue(IsDigit("0123456789") == 1);
			Assert.IsTrue(IsDigit("1A") == 0);
			Assert.IsTrue(IsDigit("A1") == 0);
			Assert.IsTrue(IsDigit("0x01") == 0);
			Assert.IsTrue(IsDigit(o) == 0);
			Assert.IsTrue(IsDigit(m) == 0);
			//
			Assert.IsTrue(IsXDigit(1) == 1);
			Assert.IsTrue(IsXDigit(-1) == 0);
			Assert.IsTrue(IsXDigit(1.234) == 0);
			Assert.IsTrue(IsXDigit("0123456789") == 1);
			Assert.IsTrue(IsXDigit("1A") == 1);
			Assert.IsTrue(IsXDigit("0x01ABCdef") == 1);
			Assert.IsTrue(IsXDigit("0xg") == 0);
			Assert.IsTrue(IsXDigit(o) == 0);
			Assert.IsTrue(IsXDigit(m) == 0);
			//
			Assert.IsTrue(IsAlpha(1) == 0);
			Assert.IsTrue(IsAlpha(-1) == 0);
			Assert.IsTrue(IsAlpha(1.234) == 0);
			Assert.IsTrue(IsAlpha("0123456789") == 0);
			Assert.IsTrue(IsAlpha("ABC") == 1);
			Assert.IsTrue(IsAlpha("abc") == 1);
			Assert.IsTrue(IsAlpha("ABC123") == 0);
			Assert.IsTrue(IsAlpha(".") == 0);
			Assert.IsTrue(IsAlpha(o) == 0);
			Assert.IsTrue(IsAlpha(m) == 0);
			//
			Assert.IsTrue(IsUpper(1) == 0);
			Assert.IsTrue(IsUpper(-1) == 0);
			Assert.IsTrue(IsUpper(1.234) == 0);
			Assert.IsTrue(IsUpper("0123456789") == 0);
			Assert.IsTrue(IsUpper("ABC") == 1);
			Assert.IsTrue(IsUpper("abc") == 0);
			Assert.IsTrue(IsUpper("AbC123") == 0);
			Assert.IsTrue(IsUpper(".") == 0);
			Assert.IsTrue(IsUpper(o) == 0);
			Assert.IsTrue(IsUpper(m) == 0);
			//
			Assert.IsTrue(IsLower(1) == 0);
			Assert.IsTrue(IsLower(-1) == 0);
			Assert.IsTrue(IsLower(1.234) == 0);
			Assert.IsTrue(IsLower("0123456789") == 0);
			Assert.IsTrue(IsLower("ABC") == 0);
			Assert.IsTrue(IsLower("abc") == 1);
			Assert.IsTrue(IsLower("AbC123") == 0);
			Assert.IsTrue(IsLower(".") == 0);
			Assert.IsTrue(IsLower(o) == 0);
			Assert.IsTrue(IsLower(m) == 0);
			//
			Assert.IsTrue(IsAlnum(1) == 1);
			Assert.IsTrue(IsAlnum(-1) == 0);
			Assert.IsTrue(IsAlnum(1.234) == 0);
			Assert.IsTrue(IsAlnum("0123456789") == 1);
			Assert.IsTrue(IsAlnum("ABC") == 1);
			Assert.IsTrue(IsAlnum("abc") == 1);
			Assert.IsTrue(IsAlnum("AbC123") == 1);
			Assert.IsTrue(IsAlnum(".") == 0);
			Assert.IsTrue(IsAlnum(o) == 0);
			Assert.IsTrue(IsAlnum(m) == 0);
			//
			Assert.IsTrue(IsSpace(1) == 0);
			Assert.IsTrue(IsSpace(-1) == 0);
			Assert.IsTrue(IsSpace(1.234) == 0);
			Assert.IsTrue(IsSpace("0123456789") == 0);
			Assert.IsTrue(IsSpace("ABC") == 0);
			Assert.IsTrue(IsSpace("abc") == 0);
			Assert.IsTrue(IsSpace("AbC123") == 0);
			Assert.IsTrue(IsSpace(".") == 0);
			Assert.IsTrue(IsSpace(" 123") == 0);
			Assert.IsTrue(IsSpace(" \t\n\r\v\f") == 1);
			Assert.IsTrue(IsSpace(o) == 0);
			Assert.IsTrue(IsSpace(m) == 0);
			//
			Assert.IsTrue(IsTime("2021") == 1);
			Assert.IsTrue(IsTime("202106") == 1);
			Assert.IsTrue(IsTime("202199") == 0);//Unrepresentable
			Assert.IsTrue(IsTime("20211201") == 1);
			Assert.IsTrue(IsTime("20211299") == 0);
			Assert.IsTrue(IsTime("2021121513") == 1);
			Assert.IsTrue(IsTime("2021121555") == 0);
			Assert.IsTrue(IsTime("202112152033") == 1);
			Assert.IsTrue(IsTime("202112152099") == 0);
			Assert.IsTrue(IsTime("20211215203522") == 1);
			Assert.IsTrue(IsTime("20211215203599") == 0);
			Assert.IsTrue(IsTime(o) == 0);
			Assert.IsTrue(IsTime(m) == 0);
			//
			Assert.IsTrue(TestScript("misc-is", true));
		}

		[Test, Category("Misc"), NonParallelizable]
		public void MiscObject()
		{
			var a = new Keysharp.Builtins.Array(10L, 20L, 30L);
			var fo = (KeysharpFunc)Keysharp.Builtins.Any.GetMethod(a, "Push");
			_ = fo.Call(a, 40L);
			Assert.AreEqual(4L, a.Length);
			Assert.IsTrue(new KeysharpObject().Base.Base.type == typeof(Any));
			Assert.IsTrue(TestScript("misc-object", true));
		}

		[Test, Category("Misc"), NonParallelizable]
		public void MiscSyntax() => Assert.IsTrue(TestScript("misc-syntax", false));

		// Ks.Font: the option round-trip, the ""-means-unset contract, subclassing, and the Gui/Image
		// integration. Creates Guis but shows none, so it needs no interactive desktop. Not run through
		// the function-wrapped variant, since it declares a class to cover `extends Font`.
		[Test, Category("Misc"), NonParallelizable]
		public void KsFont()
		{
			SkipIfUiInitializationBlocked("Creating an AppKit window requires OS thread 1.");
			//Output passed as the assertion message, so a failure names the checks that broke.
			var output = RunScript(Path.Combine(path, "ks-font.ahk"), "ks-font", true, false);
			Assert.IsTrue(HasPassed(output), output);
		}

#if WINDOWS
		// A native IDispatch client reaching a Keysharp object through ObjPtr must be able to pass
		// VT_BYREF|VT_VARIANT out-parameters -- that is how an enumerator hands back its key and value.
		[Test, Category("Misc"), NonParallelizable]
		public void MiscComByRefEnum() => Assert.IsTrue(TestScript("misc-com-byref-enum", false));
#endif

		[Test, Category("Misc"), NonParallelizable]
		public void ComponentDiscovery()
		{
			var output = RunScript(Path.Combine(path, "component-available.ahk"), "component-available", true, false);
			Assert.IsTrue(HasPassed(output), output);
		}

		[Test, Category("Misc"), NonParallelizable]
		public void MiscReserved() => Assert.IsTrue(TestScript("misc-reserved", false));

		[Test, Category("Misc"), NonParallelizable]
		public void CapabilitiesStatus()
		{
			var caps = Keysharp.Builtins.Ks.RequestCapabilities();

#if WINDOWS
			Assert.AreEqual("NotApplicable", Script.GetPropertyValue(caps, "AccessibilityAutomation"));
			Assert.AreEqual("NotApplicable", Script.GetPropertyValue(caps, "BlockInput"));
			Assert.AreEqual("NotApplicable", Script.GetPropertyValue(caps, "InputInjection"));
			Assert.AreEqual("NotApplicable", Script.GetPropertyValue(caps, "InputMonitoring"));
			Assert.AreEqual("NotApplicable", Script.GetPropertyValue(caps, "ScreenCapture"));
			Assert.AreEqual(1L, Script.GetPropertyValue(caps, "IsGranted"));
#else
			Assert.IsNotNull(Script.GetPropertyValue(caps, "AccessibilityAutomation"));
			Assert.IsNotNull(Script.GetPropertyValue(caps, "BlockInput"));
			Assert.IsNotNull(Script.GetPropertyValue(caps, "InputInjection"));
			Assert.IsNotNull(Script.GetPropertyValue(caps, "InputMonitoring"));
			Assert.IsNotNull(Script.GetPropertyValue(caps, "ScreenCapture"));
			Assert.IsNotNull(Script.GetPropertyValue(caps, "IsGranted"));
#endif
		}

		[Test, Category("Misc"), NonParallelizable]
		public void KeyboardLayout()
		{
			var layout = Keysharp.Builtins.Ks.GetKeyboardLayout();
			Assert.IsFalse(string.IsNullOrWhiteSpace(layout));

			var lower = Keysharp.Builtins.Ks.GetKeyInfo("a");
			Assert.IsInstanceOf<KeysharpObject>(lower);
			Assert.IsTrue(PropLong(lower, "VK") > 0);
			Assert.IsFalse(string.IsNullOrEmpty(PropString(lower, "Name")));
			Assert.IsNotNull(Script.GetPropertyValue(lower, "Prefix"));

			var upper = Keysharp.Builtins.Ks.GetKeyInfo("A");
			Assert.IsInstanceOf<KeysharpObject>(upper);
			Assert.IsTrue((PropLong(upper, "Modifiers") & 4L) != 0);
			Assert.IsTrue(PropString(upper, "Prefix").Contains('+'));

			var newline = Keysharp.Builtins.Ks.GetKeyInfo("\n");
			Assert.IsInstanceOf<KeysharpObject>(newline);
			Assert.AreEqual("Enter", PropString(newline, "Name"));
			Assert.AreEqual("", PropString(newline, "Prefix"));

			var esc = Keysharp.Builtins.Ks.GetKeyInfo("Esc");
			Assert.IsInstanceOf<KeysharpObject>(esc);
			Assert.AreEqual(Keysharp.Builtins.Keyboard.GetKeyVK("Esc"), PropLong(esc, "VK"));
			Assert.AreEqual(Keysharp.Builtins.Keyboard.GetKeySC("Esc"), PropLong(esc, "SC"));

			var explicitLayout = Keysharp.Builtins.Ks.GetKeyInfo("a", layout);
			Assert.IsInstanceOf<KeysharpObject>(explicitLayout);
		}

		[Test, Category("Misc"), NonParallelizable]
		public void MiscTimer()
		{
			Assert.IsTrue(TestScript("misc-timer", false));
		}

		[Test, Category("Misc"), NonParallelizable]
		public void SimplePass() => Assert.IsTrue(TestScript("misc-pass", false));

		[Test, Category("Misc"), NonParallelizable]
		public void PropRef() => Assert.IsTrue(TestScript("misc-prop-ref", false));

		private static long PropLong(object obj, string name) => Convert.ToInt64(Script.GetPropertyValue(obj, name));

		private static string PropString(object obj, string name) => Script.GetPropertyValue(obj, name).As();
	}
}
