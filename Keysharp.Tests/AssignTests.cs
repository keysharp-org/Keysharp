using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public class AssignTests : TestRunner
	{
		[Test, Category("Assign")]
		public void AssignDynVar() => Assert.IsTrue(TestScript("assign-dyn-var", false));

		[Test, Category("Assign")]
		public void AssignEmpty() => Assert.IsTrue(TestScript("assign-empty", true));

		[Test, Category("Assign")]
		public void AssignFloat() => Assert.IsTrue(TestScript("assign-float", true));

		[Test, Category("Assign")]
		public void AssignHex() => Assert.IsTrue(TestScript("assign-hex", true));

		[Test, Category("Assign")]
		public void AssignInt() => Assert.IsTrue(TestScript("assign-int", true));

		[Test, Category("Assign")]
		public void AssignMultiline() => Assert.IsTrue(TestScript("assign-multiline", false));

		[Test, Category("Assign")]
		public void AssignUnset() => Assert.IsTrue(TestScript("assign-unset", true));

		[Test, Category("Assign")]
		public void AssignNumerics() => Assert.IsTrue(TestScript("assign-numerics", true));

		[Test, Category("Assign")]
		public void AssignQuoted() => Assert.IsTrue(TestScript("assign-quoted", true));

		[Test, Category("Assign")]
		public void AssignStringCaseCompare() => Assert.IsTrue(TestScript("assign-string-case-compare", true));

		[Test, Category("Assign")]
		public void AssignStringConcat() => Assert.IsTrue(TestScript("assign-string-concat", true));

		[Test, Category("Assign")]
		public void AssignVar() => Assert.IsTrue(TestScript("assign-var", false));//Dynamic variables only work as global statics, not function variables.
	}
}