using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class MathTests : TestRunner
	{
		[Test, Category("Math")]
		public void Abs()
		{
			Assert.AreEqual(1, Maths.Abs(1));
			Assert.AreEqual(1, Maths.Abs(-1));
			Assert.AreEqual(9.81, Maths.Abs(-9.81));
			Assert.AreEqual(0, Maths.Abs(-0));
			Assert.IsTrue(TestScript("math-abs", true));
		}

		[Test, Category("Math")]
		public void ACos()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Acos(n), Maths.ACos(n));
			var caught = false;

			try
			{
				var x = Maths.ACos(-1.1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				var x = Maths.ACos(-1.1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-acos", true));
		}

		[Test, Category("Math")]
		public void ASin()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Asin(n), Maths.ASin(n));
			var caught = false;

			try
			{
				var x = Maths.ASin(-1.1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				var x = Maths.ASin(-1.1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-asin", true));
		}

		[Test, Category("Math")]
		public void ATan()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Atan(n), Maths.ATan(n));
			Assert.IsTrue(TestScript("math-atan", true));
		}

		[Test, Category("Math")]
		public void Atan2()
		{
			var n = new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 };

			for (var i = 1; i < n.Length; i++)
			{
				var x = n[i];
				var y = n[i - 1];
				Assert.AreEqual(Math.Atan2(y, x), KeysharpEnhancements.ATan2(y, x));
			}

			Assert.IsTrue(TestScript("math-atan2", true));
		}

		[Test, Category("Math")]
		public void Ceil()
		{
			foreach (var n in new[] { -1, -2.1, 0, -0, 1.000001 })
				Assert.AreEqual(Math.Ceiling(n), Maths.Ceil(n));
			Assert.IsTrue(TestScript("math-ceil", true));
		}

		[Test, Category("Math")]
		public void Cos()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Cos(v), Maths.Cos(v));
			}
			Assert.IsTrue(TestScript("math-cos", true));
		}

		[Test, Category("Math")]
		public void Cosh()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Cosh(v), KeysharpEnhancements.Cosh(v));
			}
			Assert.IsTrue(TestScript("math-cosh", true));
		}

		[Test, Category("Math")]
		public void DateAdd()
		{
			var d1 = "20040126000000";
			var d2 = "20050126000000";
			var val = Maths.DateAdd(d1, 366, "days");
			Assert.AreEqual(d2, val);
			val = Maths.DateAdd(d2, -366L, "days");
			Assert.AreEqual(d1, val);
			d1 = "2023021002";
			d2 = "20230210070000";
			val = Maths.DateAdd(d1, 5L, "h");
			Assert.AreEqual(d2, val);
			d1 = "20230210020000";
			val = Maths.DateAdd(d2, -5L, "h");
			Assert.AreEqual(d1, val);
			d1 = "202302100225";
			d2 = "20230210023000";
			val = Maths.DateAdd(d1, 5L, "m");
			Assert.AreEqual(d2, val);
			d1 = "20230210022500";
			val = Maths.DateAdd(d2, -5L, "m");
			Assert.AreEqual(d1, val);
			d1 = "20230210022510";
			d2 = "20230210022515";
			val = Maths.DateAdd(d1, 5L, "s");
			Assert.AreEqual(d2, val);
			val = Maths.DateAdd(d2, -5L, "s");
			Assert.AreEqual(d1, val);
			d1 = "20230210022500";
			d2 = "20230210022530";
			val = Maths.DateAdd(d1, 0.5, "m");
			Assert.AreEqual(d2, val);
			val = Maths.DateAdd(d2, -0.5, "m");
			Assert.AreEqual(d1, val);
			d1 = "20230210020000";
			d2 = "20230210023000";
			val = Maths.DateAdd(d1, 0.5, "h");
			Assert.AreEqual(d2, val);
			val = Maths.DateAdd(d2, -0.5, "h");
			Assert.AreEqual(d1, val);
			d1 = "20040126000000";
			d2 = "20040126120000";
			val = Maths.DateAdd(d1, 0.5, "d");
			Assert.AreEqual(d2, val);
			val = Maths.DateAdd(d2, -0.5, "d");
			Assert.AreEqual(d1, val);
			d1 = "20230210023015.100";
			d2 = "20230210023015.500";
			val = Maths.DateAdd(d1, 400, "l");
			Assert.AreEqual(d2, val);
			d1 = "20230210023015.100";
			d2 = "20230210023016.100";
			val = Maths.DateAdd(d1, 1, "s");
			Assert.AreEqual(d2, val);
			Assert.IsTrue(TestScript("math-dateadd", true));
		}

		[Test, Category("Math")]
		public void DateDiff()
		{
			var d1 = "20050126";
			var d2 = "20040126";
			var val = Maths.DateDiff(d1, d2, "days");
			Assert.AreEqual(366L, val);
			d1 = "20230110";
			d2 = "20230115";
			val = Maths.DateDiff(d2, d1, "days");
			Assert.AreEqual(5L, val);
			val = Maths.DateDiff(d1, d2, "days");
			Assert.AreEqual(-5L, val);
			d1 = "2023021002";
			d2 = "2023021001";
			val = Maths.DateDiff(d1, d2, "h");
			Assert.AreEqual(1L, val);
			val = Maths.DateDiff(d2, d1, "h");
			Assert.AreEqual(-1L, val);
			d1 = "202302100230";
			d2 = "202302100225";
			val = Maths.DateDiff(d1, d2, "m");
			Assert.AreEqual(5L, val);
			val = Maths.DateDiff(d2, d1, "m");
			Assert.AreEqual(-5L, val);
			d1 = "20230210023015";
			d2 = "20230210022510";
			val = Maths.DateDiff(d1, d2, "s");
			Assert.AreEqual(305L, val);
			val = Maths.DateDiff(d2, d1, "s");
			Assert.AreEqual(-305L, val);
			d1 = "20230210023015.500";
			d2 = "20230210023015.100";
			val = Maths.DateDiff(d1, d2, "l");
			Assert.AreEqual(400L, val);
			val = Maths.DateDiff(d2, d1, "l");
			Assert.AreEqual(-400L, val);
			Assert.IsTrue(TestScript("math-datediff", true));
		}

		[Test, Category("Math")]
		public void Exp()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Exp(n), Maths.Exp(n));
			Assert.IsTrue(TestScript("math-exp", true));
		}

		[Test, Category("Math")]
		public void Floor()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.67 })
				Assert.AreEqual(Math.Floor(n), Maths.Floor(n));
			Assert.IsTrue(TestScript("math-floor", true));
		}

		[Test, Category("Math")]
		public void Integer()
		{
			foreach (var n in new[] { -1, -2.1, 0, -0, 0.5, 1.000001 })
				Assert.AreEqual((double)(long)(n), Core.Integer.Call(null, n));
			Assert.IsTrue(TestScript("math-integer", true));
		}

		[Test, Category("Math")]
		public void Float()
		{
			foreach (var n in new object[] { -1, 1, -2.1, 0, -0, 0.5, 1.000001 })
				Assert.AreEqual(n, Core.Float.Call(null, n));
			Assert.IsTrue(TestScript("math-float", true));
		}

		[Test, Category("Math")]
		public void Ln()
		{
			foreach (var n in new[] { 0, -0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Log(n), Maths.Ln(n));
			var caught = false;

			try
			{
				var x = Maths.Ln(-1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				var x = Maths.Ln(-0.5);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-ln", true));
		}

		[Test, Category("Math")]
		public void Log()
		{
			foreach (var n in new[] { 0, 0.5, 1, 0.675 })
			{
				foreach (var b in new[] { -1, 0, 1, 2, 3, 4 })
					Assert.AreEqual(Math.Log(n, b), Maths.Log(n, b));
			}

			foreach (var n in new[] { 0, 0.5, 1, 0.675 })
				Assert.AreEqual(Math.Log10(n), Maths.Log(n));
			var caught = false;

			try
			{
				var x = Maths.Log(-1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				var x = Maths.Log(-0.5);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
		}

		[Test, Category("Math")]
		public void Max()
		{
			Assert.AreEqual(-6, Maths.Max(-6, -6));
			Assert.AreEqual(-6, Maths.Max(-6, "-6"));
			Assert.AreEqual(-5, Maths.Max(-6, -5));
			Assert.AreEqual(-4.2, Maths.Max(-4.2, -5.0));
			Assert.AreEqual(-4.2, Maths.Max("-4.2", -5.0));
			Assert.AreEqual(-4.2, Maths.Max(-4.2, "-5.0"));
			Assert.AreEqual(-4.2, Maths.Max("-4.2", "-5.0"));
			Assert.AreEqual(0, Maths.Max(0, 0));
			Assert.AreEqual(1, Maths.Max("0", 1L));
			Assert.AreEqual(1, Maths.Max(1, "0"));
			Assert.AreEqual(1, Maths.Max("1", "1"));
			Assert.AreEqual(2.3, Maths.Max(1.5, 2.3));
			Assert.AreEqual(0.675, Maths.Max([-1.0, 0.675]));
			Assert.AreEqual(1, Maths.Max([-1.0, -0.5, 0, 0.5, 1, 0.675]));
			Assert.AreEqual(2, Maths.Max([-1.0, -0.5, 0, 0.5, 1, "0.675", "2.0"]));
			Assert.AreEqual(0.675, Maths.Max(new Keysharp.Core.Array([-1.0, 0.675])));
			Assert.AreEqual(1, Maths.Max(new Keysharp.Core.Array([-1.0, -0.5, 0, 0.5, 1, 0.675])));
			Assert.AreEqual(2, Maths.Max(new Keysharp.Core.Array([-1.0, -0.5, 0, 0.5, 1, "0.675", 2.0])));
			Assert.AreEqual(1, Maths.Max(-1.0, -0.5, 0, "0.5", 1, 0.675));
			Assert.AreEqual(2, Maths.Max(-1.0, -0.5, 0, "0.5", 1, 0.675, 2.0));
			Assert.AreEqual(typeof(long), Maths.Max(-1.0, 1L).GetType());
			Assert.AreEqual(typeof(double), Maths.Max(1.0, -1L).GetType());
			var caught = false;

			try
			{
				Assert.AreEqual(string.Empty, Maths.Max([-1.0, "asdf"]));
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				Assert.AreEqual(string.Empty, Maths.Max(new Keysharp.Core.Array([-1.0, "asdf"])));
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-max", true));
		}

		[Test, Category("Math")]
		public void Min()
		{
			Assert.AreEqual(-6, Maths.Min(-6, -6));
			Assert.AreEqual(-6, Maths.Min(-6, "5"));
			Assert.AreEqual(-5.0, Maths.Min(-4.2, -5.0));
			Assert.AreEqual(-5.0, Maths.Min("-4.2", -5.0));
			Assert.AreEqual(-5.0, Maths.Min(-4.2, "-5.0"));
			Assert.AreEqual(-5.0, Maths.Min("-4.2", "-5.0"));
			Assert.AreEqual(0, Maths.Min(0, 0));
			Assert.AreEqual(0, Maths.Min("0", 1));
			Assert.AreEqual(0, Maths.Min(0, "1"));
			Assert.AreEqual(1, Maths.Min("1", "1"));
			Assert.AreEqual(1.5, Maths.Min(1.5, 2.3));
			Assert.AreEqual(-1.0, Maths.Min([-1.0, 0.675]));
			Assert.AreEqual(-1.0, Maths.Min([-1.0, -0.5, 0, 0.5, 1, 0.675]));
			Assert.AreEqual(-1.0, Maths.Min([-1.0, -0.5, 0, 0.5, 1, "0.675", "2.0"]));
			Assert.AreEqual(-1.0, Maths.Min(new Keysharp.Core.Array([-1.0, 0.675])));
			Assert.AreEqual(-1.0, Maths.Min(new Keysharp.Core.Array([-1.0, -0.5, 0, 0.5, 1, 0.675])));
			Assert.AreEqual(-1.0, Maths.Min(new Keysharp.Core.Array([-1.0, -0.5, 0, 0.5, 1, "0.675", 2.0])));
			Assert.AreEqual(-1.0, Maths.Min(-1.0, -0.5, 0, "0.5", 1, 0.675));
			Assert.AreEqual(-1.0, Maths.Min(-1.0, -0.5, 0, "0.5", 1, 0.675, 2.0));
			Assert.AreEqual(typeof(double), Maths.Min(-1.0, 1L).GetType());
			Assert.AreEqual(typeof(long), Maths.Min(1.0, -1L).GetType());
			var caught = false;

			try
			{
				Assert.AreEqual(string.Empty, Maths.Min([-1.0, "asdf"]));
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			caught = false;

			try
			{
				Assert.AreEqual(string.Empty, Maths.Min(new Keysharp.Core.Array([-1.0, "asdf"])));
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-min", true));
		}

		[Test, Category("Math")]
		public void Mod()
		{
			foreach (var n in new[] { -1, 0, 1, 2, 3, 4 })
			{
				foreach (var d in new[] { -1, -0.5, 0.5, 1, 0.675 })
					Assert.AreEqual(d == 0 ? 0 : n % d, Maths.Mod(n, d));
			}
			var caught = false;

			try
			{
				var x = Maths.Mod(1, 0);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
		}

		[Test, Category("Math")]
		public void Number()
		{
			Assert.AreEqual(Maths.Number("0"), 0L);
			Assert.AreEqual(Maths.Number("0.0"), 0.0);
			Assert.AreEqual(Maths.Number("1"), 1L);
			Assert.AreEqual(Maths.Number("1.0"), 1.0);
			Assert.AreNotEqual(Maths.Number("1.5"), 1L);
			Assert.AreEqual(Maths.Number(1L), 1L);
			Assert.AreNotEqual(Maths.Number(1.5), 1L);
			Assert.AreEqual(Maths.Number(-1L), -1L);
			Assert.AreEqual(Maths.Number("-1"), -1L);
			Assert.AreEqual(Maths.Number("1L"), 1L);
			Assert.AreEqual(Maths.Number("+1L"), 1L);
			Assert.AreEqual(Maths.Number("-1.0"), -1.0);
			Assert.AreEqual(Maths.Number("1.0D"), 1.0);
			Assert.AreEqual(Maths.Number("1.0d"), 1.0);
			Assert.AreEqual(Maths.Number("-1.0D"), -1.0);
			Assert.AreEqual(Maths.Number("-1.0d"), -1.0);
			Assert.AreEqual(Maths.Number("0xF"), 15L);
			Assert.AreEqual(Maths.Number("-0xF"), -15L);
		}

		[Test, Category("Math")]
		public void Random()
		{
			var ct = 100000;

			for (var i = 0; i < ct; i++)
			{
				var d = (double)Maths.Random();
				Assert.IsTrue(d >= 0 && d <= 1.0);
			}

			for (var i = 0; i < ct; i++)
			{
				var d = (long)Maths.Random(-long.MaxValue);
				Assert.IsTrue(d >= -long.MaxValue && d <= 0L);
			}

			for (var i = 0; i < ct; i++)
			{
				var d = (long)Maths.Random(null, -long.MaxValue);
				Assert.IsTrue(d >= -long.MaxValue && d <= 0L);
			}

			for (var i = 0; i < ct; i++)
			{
				var d = (long)Maths.Random(-long.MaxValue, 0L);
				Assert.IsTrue(d >= -long.MaxValue && d <= 0L);
			}

			for (var i = 0; i < ct; i++)
			{
				var d = (long)Maths.Random(-10L, 10L);
				Assert.IsTrue(d >= -10L && d <= 10L);
			}

			for (var i = 0; i < ct; i++)
			{
				var d = (double)Maths.Random(-5.123, 5.123);
				Assert.IsTrue(d >= -5.123 && d <= 5.123);
			}

			Assert.IsTrue(TestScript("math-random", true));
		}

		[Test, Category("Math")]
		public void Round()
		{
			Assert.AreEqual(3, Maths.Round(3.14));
			Assert.AreEqual(3.1, Maths.Round(3.14, 1));
			Assert.AreEqual(350, Maths.Round(345, -1));
			Assert.AreEqual(300, Maths.Round(345, -2));
			Assert.AreEqual(-350, Maths.Round(-345, -1));
			Assert.AreEqual(-300, Maths.Round(-345, -2));
			Assert.AreEqual(0, Maths.Round(-0, -2));
			Assert.IsTrue(TestScript("math-round", true));
		}

		[Test, Category("Math")]
		public void Sin()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Sin(v), Maths.Sin(v));
			}
			Assert.IsTrue(TestScript("math-sin", true));
		}

		[Test, Category("Math")]
		public void Sinh()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Sinh(v), KeysharpEnhancements.Sinh(v));
			}
			Assert.IsTrue(TestScript("math-sinh", true));
		}

		[Test, Category("Math")]
		public void Sqrt()
		{
			foreach (var n in new[] { 0, 1, 4, 9, 36, 12769, 8 })
				Assert.AreEqual(Math.Sqrt(n), Maths.Sqrt(n));
			var caught = false;

			try
			{
				var x = Maths.Sqrt(-1);
			}
			catch (Exception)
			{
				caught = true;
			}

			Assert.IsTrue(caught);
			Assert.IsTrue(TestScript("math-sqrt", true));
		}

		[Test, Category("Math")]
		public void Tan()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Tan(v), Maths.Tan(v));
			}
			Assert.IsTrue(TestScript("math-tan", true));
		}

		[Test, Category("Math")]
		public void Tanh()
		{
			foreach (var n in new[] { -1, -0.5, 0, -0, 0.5, 1, 0.675 })
			{
				var v = n * Math.PI;
				Assert.AreEqual(Math.Tanh(v), KeysharpEnhancements.Tanh(v));
			}
			Assert.IsTrue(TestScript("math-tanh", true));
		}
	}
}