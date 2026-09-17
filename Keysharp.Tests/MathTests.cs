using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class MathTests : TestRunner
	{
		[Test, Category("Math")]
		public void Abs() => Assert.IsTrue(TestScript("math-abs", true));

		[Test, Category("Math")]
		public void ACos() => Assert.IsTrue(TestScript("math-acos", true));

		[Test, Category("Math")]
		public void ASin() => Assert.IsTrue(TestScript("math-asin", true));

		[Test, Category("Math")]
		public void ATan() => Assert.IsTrue(TestScript("math-atan", true));

		[Test, Category("Math")]
		public void Atan2() => Assert.IsTrue(TestScript("math-atan2", true));

		[Test, Category("Math")]
		public void Ceil() => Assert.IsTrue(TestScript("math-ceil", true));

		[Test, Category("Math")]
		public void Cos() => Assert.IsTrue(TestScript("math-cos", true));

		[Test, Category("Math")]
		public void Cosh() => Assert.IsTrue(TestScript("math-cosh", true));

		[Test, Category("Math")]
		public void DateAdd() => Assert.IsTrue(TestScript("math-dateadd", true));

		[Test, Category("Math")]
		public void DateDiff() => Assert.IsTrue(TestScript("math-datediff", true));

		[Test, Category("Math")]
		public void Exp() => Assert.IsTrue(TestScript("math-exp", true));

		[Test, Category("Math")]
		public void Floor() => Assert.IsTrue(TestScript("math-floor", true));

		[Test, Category("Math")]
		public void Integer() => Assert.IsTrue(TestScript("math-integer", true));

		[Test, Category("Math")]
		public void Float() => Assert.IsTrue(TestScript("math-float", true));

		[Test, Category("Math")]
		public void Ln() => Assert.IsTrue(TestScript("math-ln", true));

		[Test, Category("Math")]
		public void Log() => Assert.IsTrue(TestScript("math-log", true));

		[Test, Category("Math")]
		public void Max() => Assert.IsTrue(TestScript("math-max", true));

		[Test, Category("Math")]
		public void Min() => Assert.IsTrue(TestScript("math-min", true));

		[Test, Category("Math")]
		public void Mod() => Assert.IsTrue(TestScript("math-mod", true));

		[Test, Category("Math")]
		public void Number() => Assert.IsTrue(TestScript("math-number", true));

		[Test, Category("Math")]
		public void Random() => Assert.IsTrue(TestScript("math-random", true));

		[Test, Category("Math")]
		public void Round() => Assert.IsTrue(TestScript("math-round", true));

		[Test, Category("Math")]
		public void Sin() => Assert.IsTrue(TestScript("math-sin", true));

		[Test, Category("Math")]
		public void Sinh() => Assert.IsTrue(TestScript("math-sinh", true));

		[Test, Category("Math")]
		public void Sqrt() => Assert.IsTrue(TestScript("math-sqrt", true));

		[Test, Category("Math")]
		public void Tan() => Assert.IsTrue(TestScript("math-tan", true));

		[Test, Category("Math")]
		public void Tanh() => Assert.IsTrue(TestScript("math-tanh", true));
	}
}
