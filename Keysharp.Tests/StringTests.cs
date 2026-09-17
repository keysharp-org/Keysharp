using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class StringTests : TestRunner
	{
		//[Test]
		//public void TestHotstringCaps()
		//{
		//  var kbh = new Keysharp.Internals.Os.Windows.KeyboardHook();
		//  var str = kbh.ApplyCase("werent", "weren't");
		//  Assert.AreEqual(str, "weren't");
		//  str = kbh.ApplyCase("Werent", "weren't");
		//  Assert.AreEqual(str, "Weren't");
		//  str = kbh.ApplyCase("WerEnt", "weren't");
		//  Assert.AreEqual(str, "WerEn't");
		//  str = kbh.ApplyCase("WerEnT", "weren't");
		//  Assert.AreEqual(str, "WerEn'T");
		//  str = kbh.ApplyCase("WERENT", "weren't");
		//  Assert.AreEqual(str, "WEREN'T");
		//}

		[Test, Category("String")]
		public void Chr() => Assert.IsTrue(TestScript("string-chr", true));

		[Test, Category("String")]
		public void CompareCase() => Assert.IsTrue(TestScript("string-compare-case", true));

		[Test, Category("String")]
		public void Concat() => Assert.IsTrue(TestScript("string-concat", true));

		[Test, Category("String")]
		public void Continuation() => Assert.IsTrue(TestScript("string-continuation", false));//False because WrapInFunc() adds tabs to the lines.

		[Test, Category("String")]
		public void Escape() => Assert.IsTrue(TestScript("string-escape", true));

		[Test, Category("String")]
		public void Format() => Assert.IsTrue(TestScript("string-format", true));

		[Test, Category("String")]
		public void FormatTime() => Assert.IsTrue(TestScript("string-formattime", true));

		[Test, Category("String")]
		public void InStr() => Assert.IsTrue(TestScript("string-instr", true));

		[Test, Category("String")]
		public void LTrim() => Assert.IsTrue(TestScript("string-ltrim", true));

		[Test, Category("String")]
		public void Ord() => Assert.IsTrue(TestScript("string-ord", true));

		[Test, Category("String")]
		public void RegExMatch() => Assert.IsTrue(TestScript("string-regexmatch", false));

		[Test, Category("String")]
		public void RegExMatchCs() => Assert.IsTrue(TestScript("string-regexmatch-cs", false));

		[Test, Category("String")]
		public void RegExReplace() => Assert.IsTrue(TestScript("string-regexreplace", true));

		[Test, Category("String")]
		public void RegExReplaceCs() => Assert.IsTrue(TestScript("string-regexreplace-cs", true));

		[Test, Category("String")]
		public void RTrim() => Assert.IsTrue(TestScript("string-rtrim", true));

		[Test, Category("String")]
		public void Sort() => Assert.IsTrue(TestScript("string-sort", true));

		[Test, Category("String")]
		public void StartsEndsWith() => Assert.IsTrue(TestScript("string-startsendswith", true));

		[Test, Category("String")]
		public void StrCompare() => Assert.IsTrue(TestScript("string-strcompare", true));

		[Test, Category("String")]
		public void String() => Assert.IsTrue(TestScript("string-string", true));

		[Test, Category("String")]
		public void StrLen() => Assert.IsTrue(TestScript("string-strlen", true));

		[Test, Category("String")]
		public void StrLower() => Assert.IsTrue(TestScript("string-strlower", true));

		[Test, Category("String")]
		public void StrPutStrGet() => Assert.IsTrue(TestScript("string-strputstrget", true));

		[Test, Category("String")]
		public void StrReplace() => Assert.IsTrue(TestScript("string-strreplace", false));//The count output variable must be global.

		[Test, Category("String")]
		public void StrSplit() => Assert.IsTrue(TestScript("string-strsplit", true));

		[Test, Category("String")]
		public void StrUpper() => Assert.IsTrue(TestScript("string-strupper", true));

		[Test, Category("String")]
		public void SubStr() => Assert.IsTrue(TestScript("string-substr", true));

		[Test, Category("String")]
		public void Trim() => Assert.IsTrue(TestScript("string-trim", true));

		[Test, Category("String")]
		public void VerCompare() => Assert.IsTrue(TestScript("string-vercompare", true));

		[Test, Category("String")]
		public void Base64DecodeEncode() => Assert.IsTrue(TestScript("string-base64", true));
	}
}
