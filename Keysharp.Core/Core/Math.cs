namespace Keysharp.Core
{
	public static partial class KeysharpEnhancements
	{
		/// <summary>
		/// Returns the angle whose tangent is the y/x number.
		/// </summary>
		/// <param name="y">A number representing the Y value.</param>
		/// <param name="x">A number representing the X value.</param>
		/// <returns>An angle, θ, measured in radians, such that -(y/x)/2 ≤ θ ≤ (y/x)/2.</returns>
		public static object ATan2(object y, object x) => Math.Atan2(y is double d0 ? d0 : y.Ad(), x is double d1 ? d1 : x.Ad());

		/// <summary>
		/// Returns the hyperbolic cosine of the specified angle.
		/// </summary>
		/// <param name="n">An angle, measured in radians.</param>
		/// <returns>The hyperbolic cosine of <paramref name="n"/>.</returns>
		public static object Cosh(object obj) => Math.Cosh(obj is double d ? d : obj.Ad());

		/// <summary>
		/// Reinitializes the random number generator for the current thread with the specified numerical seed.
		/// </summary>
		/// <param name="obj">The numerical seed to create the random number generator with.</param>
		public static object RandomSeed(object obj)
		{
			Script.TheScript.Threads.CurrentThread.RandomGenerator = new Random(obj.Ai());
			return DefaultObject;
		}

		/// <summary>
		/// Returns the hyperbolic sine of the specified angle.
		/// </summary>
		/// <param name="n">An angle, measured in radians.</param>
		/// <returns>The hyperbolic sine of <paramref name="n"/>.</returns>
		public static object Sinh(object obj) => Math.Sinh(obj is double d ? d : obj.Ad());

		/// <summary>
		/// Returns the hyperbolic tangent of the specified angle.
		/// </summary>
		/// <param name="n">An angle, measured in radians.</param>
		/// <returns>The hyperbolic tangent of <paramref name="n"/>.</returns>
		public static object Tanh(object obj) => Math.Tanh(obj is double d ? d : obj.Ad());
	}

	/// <summary>
	/// Public interface for math-related functions.
	/// Most functions here do not take variadic parameters so they can run as fast as possible.
	/// Also, an attempt to cast the object argument to a double is made first because it's the
	/// most common and fastest case. If it's not a double, Ad() is used.
	/// </summary>
	public static class Maths
	{
		/// <summary>
		/// Internal helper to get a random number generator for the current thread.
		/// </summary>
		private static Random RandomGenerator => Script.TheScript.Threads.CurrentThread.RandomGenerator;

		/// <summary>
		/// Returns the absolute value of a number.
		/// </summary>
		/// <param name="number">Any number.</param>
		/// <returns>The magnitude of <paramref name="n"/>.</returns>
		public static object Abs(object number)
		{
			number = Number(number);
			if (number is long ll)
				return Math.Abs(ll);
			else if (number is bool bl)
				return bl;
			else if (number is double dd)
				return Math.Abs(dd);
			else
				return Errors.ErrorOccurred($"Abs() argument of {number} was not numeric");
		}

		/// <summary>
		/// Returns the angle whose cosine is the specified number.
		/// </summary>
		/// <param name="number">A number representing a cosine, where -1 ≤ <paramref name="n"/> ≤ 1.</param>
		/// <returns>An angle, θ, measured in radians, such that 0 ≤ θ ≤ π.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the argument was not between -1 and 1.</exception>
		public static object ACos(object number)
		{
			var n = number is double d ? d : number.Ad();

			if (n < -1 || n > 1)
				return Errors.ErrorOccurred($"ACos() argument of {n} was not between -1 and 1.");

			return Math.Acos(n);
		}

		/// <summary>
		/// Returns the angle whose sine is the specified number.
		/// </summary>
		/// <param name="number">A number representing a sine, where -1 ≤ <paramref name="n"/> ≤ 1.</param>
		/// <returns>An angle, θ, measured in radians, such that -π/2 ≤ θ ≤ π/2.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the argument was not between -1 and 1.</exception>
		public static object ASin(object number)
		{
			var n = number is double d ? d : number.Ad();

			if (n < -1 || n > 1)
				return Errors.ErrorOccurred($"ASin() argument of {n} was not between -1 and 1.");

			return Math.Asin(n);
		}

		/// <summary>
		/// Returns the angle whose tangent is the specified number.
		/// </summary>
		/// <param name="number">A number representing a tangent.</param>
		/// <returns>An angle, θ, measured in radians, such that -π/2 ≤ θ ≤ π/2.</returns>
		public static object ATan(object number) => Math.Atan(number is double d ? d : number.Ad());

		/// <summary>
		/// Returns the specified number rounded up to the nearest integer.
		/// </summary>
		/// <param name="number">A number.</param>
		/// <returns>The smallest integer greater than or equal to <paramref name="n"/>.</returns>
		public static object Ceil(object number) => (long)Math.Ceiling(number is double d ? d : number.Ad());

		/// <summary>
		/// Returns the cosine of the specified angle.
		/// </summary>
		/// <param name="number">An angle, measured in radians.</param>
		/// <returns>The cosine of <paramref name="n"/>.</returns>
		public static object Cos(object number) => Math.Cos(number is double d ? d : number.Ad());

		/// <summary>
		/// Adds or subtracts time from a date-time value.
		/// </summary>
		/// <param name="dateTime">A date-time stamp in the YYYYMMDDHH24MISS format.</param>
		/// <param name="time">The amount of time to add, as an integer or floating-point number. Specify a negative number to perform subtraction.</param>
		/// <param name="timeUnits">The meaning of the Time parameter. TimeUnits may be one of the following strings (or just the first letter): L (miLliseconds), Seconds, Minutes, Hours or Days.</param>
		/// <returns>The new date-time value as a string of digits in the YYYYMMDDHH24MISS format if timeUnits was not "L",
		/// and the YYYYMMDDHH24MISS.FFF format if timeUnits was "L".</returns>
		public static string DateAdd(object dateTime, object time, object timeUnits)
		{
			var s1 = dateTime.As();
			var t = time.Ad();
			var units = timeUnits.As();
			var wasMs = s1.Contains('.');

			if (s1.Length == 0)
				s1 = A_NowMs;

			var d1 = Conversions.ToDateTime(s1);

			if (units.StartsWith("s", StringComparison.OrdinalIgnoreCase))
				d1 = d1.AddSeconds(t);
			else if (units.StartsWith("m", StringComparison.OrdinalIgnoreCase))
				d1 = d1.AddMinutes(t);
			else if (units.StartsWith("h", StringComparison.OrdinalIgnoreCase))
				d1 = d1.AddHours(t);
			else if (wasMs |= units.StartsWith("l", StringComparison.OrdinalIgnoreCase))
				d1 = d1.AddMilliseconds(t);
			else
				d1 = d1.AddDays(t);

			return wasMs ? Conversions.ToYYYYMMDDHH24MISSFFF(d1) : Conversions.ToYYYYMMDDHH24MISS(d1);
		}

		/// <summary>
		/// Compares two date-time values and returns the difference.
		/// </summary>
		/// <param name="dateTime1">Date-time stamps in the YYYYMMDDHH24MISS format.<br/>
		/// If either is an empty string, the current local date and time (A_Now) is used.
		/// </param>
		/// <param name="dateTime2">See <paramref name="dateTime1"/>.</param>
		/// <param name="timeUnits">Units to measure the difference in.<br/>
		/// timeUnits may be one of the following strings (or just the first letter): L (miLliseconds), Seconds, Minutes, Hours or Days.
		/// </param>
		/// <returns>The difference between the two timestamps, in the units specified by timeUnits.<br/>
		/// If dateTime1 is earlier than dateTime2, a negative number is returned.
		/// </returns>
		public static object DateDiff(object dateTime1, object dateTime2, object timeUnits)
		{
			var s1 = dateTime1.As();
			var s2 = dateTime2.As();
			var units = timeUnits.As();

			if (s1.Length == 0)
				s1 = A_NowMs;

			if (s2.Length == 0)
				s2 = A_NowMs;

			var d1 = Conversions.ToDateTime(s1);
			var d2 = Conversions.ToDateTime(s2);
			var diff = d1 - d2;

			if (units.StartsWith("s", StringComparison.OrdinalIgnoreCase))
				return (long)diff.TotalSeconds;
			else if (units.StartsWith("m", StringComparison.OrdinalIgnoreCase))
				return (long)diff.TotalMinutes;
			else if (units.StartsWith("h", StringComparison.OrdinalIgnoreCase))
				return (long)diff.TotalHours;
			else if (units.StartsWith("l", StringComparison.OrdinalIgnoreCase))
				return (long)diff.TotalMilliseconds;
			else
				return (long)diff.TotalDays;
		}

		/// <summary>
		/// Returns <c>e</c> raised to the specified power.
		/// </summary>
		/// <param name="n">A number specifying a power.</param>
		/// <returns>The number <c>e</c> raised to the power <paramref name="n"/>.</returns>
		public static object Exp(object n) => Math.Exp(n is double d ? d : n.Ad());

		/// <summary>
		/// Returns the largest integer less than or equal to the specified double number.
		/// </summary>
		/// <param name="number">A number.</param>
		/// <returns>The largest integer less than or equal to <paramref name="n"/>.</returns>
		public static object Floor(object number) => (long)Math.Floor(number is double d ? d : number.Ad());

		/// <summary>
		/// Returns the natural (base e) logarithm of a specified number.
		/// </summary>
		/// <param name="number">A number whose logarithm is to be found.</param>
		/// <returns>The natural logarithm of <paramref name="n"/> if it's positive, else an exception is thrown.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the argument was negative.</exception>
		public static object Ln(object number)
		{
			var n = number is double d ? d : number.Ad();

			if (n < 0)
				return Errors.ErrorOccurred($"Ln() argument {n} was negative.");

			return Math.Log(n);
		}

		/// <summary>
		/// Returns the logarithm of a specified number in a specified base.
		/// </summary>
		/// <param name="number">A number whose logarithm is to be found.</param>
		/// <param name="base">The base of the logarithm. If unspecified this is <c>10</c>.</param>
		/// <returns>The logarithm of <paramref name="n"/> to base <paramref name="b"/> if n is positive, else an exception is thrown.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the argument was negative.</exception>
		public static object Log(object number, object @base = null)
		{
			var n = number.Ad(double.MinValue);
			var b = @base.Ad(double.MinValue);

			if (n < 0)
				return Errors.ErrorOccurred($"Log() argument {n} was negative.");

			if (b != double.MinValue)
				return b == 10 ? Math.Log10(n) : Math.Log(n, b);
			else if (n != double.MinValue)
				return Math.Log10(n);

			return 0.0;
		}

		/// <summary>
		/// Returns the greatest of a list of numbers.
		/// </summary>
		/// <param name="dividend">The first number to compare.</param>
		/// <param name="divisor">The second number to compare.</param>
		/// <returns>The larger of the two numbers, or the empty string if either number is not numeric.</returns>
		public static object Max(params object[] obj)
		{
			var o = obj.L();//Flatten here to find the max of anything contained in any sub item.

			if (o.Count > 1)
			{
				object max = double.MinValue;

				for (var i = 1; i < o.Count; ++i)
				{
					var left = o[i - 1];
					var right = o[i];

					if (Script.ParseNumericArgs(left, right, "Max", out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
						{
							if (secondIsDouble)
							{
								var tempd = Math.Max(firstd, secondd);

								if (tempd > max.Ad())
									max = tempd;
							}
							else
							{
								if (firstd > secondl)
								{
									if (firstd > max.Ad())
										max = firstd;
								}
								else if (secondl > max.Ad())
									max = secondl;
							}
						}
						else
						{
							if (secondIsDouble)
							{
								if (firstl > secondd)
								{
									if (firstl > max.Ad())
										max = firstl;
								}
								else if (secondd > max.Ad())
									max = secondd;
							}
							else
							{
								var templ = Math.Max(firstl, secondl);

								if (templ > max.Ad())
									max = templ;
							}
						}
					}
				}

				if (max.Ad() != double.MinValue)
					return max;
			}

			return DefaultObject;
		}

		/// <summary>
		/// Returns the least of a list of numbers.
		/// </summary>
		/// <param name="dividend">The first number to compare.</param>
		/// <param name="divisor">The second number to compare.</param>
		/// <returns>The lesser of the two numbers, or the empty string if either number is not numeric.</returns>
		public static object Min(params object[] obj)
		{
			var o = obj.L();//Flatten here to find the max of anything contained in any sub item.

			if (o.Count > 1)
			{
				object min = double.MaxValue;

				for (var i = 1; i < o.Count; ++i)
				{
					var left = o[i - 1];
					var right = o[i];

					if (Script.ParseNumericArgs(left, right, "Min", out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
						{
							if (secondIsDouble)
							{
								var tempd = Math.Min(firstd, secondd);

								if (tempd < min.Ad())
									min = tempd;
							}
							else
							{
								if (firstd < secondl)
								{
									if (firstd < min.Ad())
										min = firstd;
								}
								else if (secondl < min.Ad())
									min = secondl;
							}
						}
						else
						{
							if (secondIsDouble)
							{
								if (firstl < secondd)
								{
									if (firstl < min.Ad())
										min = firstl;
								}
								else if (secondd < min.Ad())
									min = secondd;
							}
							else
							{
								var templ = Math.Min(firstl, secondl);

								if (templ < min.Ad())
									min = templ;
							}
						}
					}
				}

				if (min.Ad() != double.MaxValue)
					return min;
			}

			return DefaultObject;
		}

		/// <summary>
		/// Returns the remainder after dividing two numbers.
		/// Throws an exception if divisor is 0.
		/// </summary>
		/// <param name="dividend">The dividend.</param>
		/// <param name="divisor">The divisor.</param>
		/// <returns>The remainder after dividing <paramref name="dividend"/> by <paramref name="divisor"/>.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the divisor == 0.</exception>
		public static object Mod(object dividend, object divisor)
		{
			if (dividend is double || divisor is double)
			{
				var divid = dividend.Ad();
				var divis = divisor.Ad();

				if (divis == 0)
					return Errors.ErrorOccurred($"Mod() divisor argument of {divis} was 0.");

				//return Math.IEEERemainder(dividend, divisor);
				return divid % divis;
			}
			else
			{
				var divid = dividend.Al();
				var divis = divisor.Al();

				if (divis == 0)
					return Errors.ErrorOccurred($"Mod() divisor argument of {divis} was 0.");

				return divid % divis;
			}
		}

		/// <summary>
		/// Converts a numeric string to a pure integer or floating-point number.
		/// </summary>
		/// <param name="value">The value to convert.</param>
		/// <returns>The result of converting Value to a pure integer or floating-point number, or value itself if it is<br/>
		/// already an Integer or Float value.
		/// </returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the value cannot be converted.</exception>
		public static object Number(object value)
		{
			if (value is long l)
				return l;
			else if (value is double d)
				return d;
			else
			{
				var s = value.As();

				if (s.Contains('.'))
				{
					var val = s.ParseDouble(false);

					if (val.HasValue)
						return val.Value;
				}
				else
				{
					var val = s.ParseLong(false);

					if (val.HasValue)
						return val.Value;
				}

				return Errors.TypeErrorOccurred(s, typeof(double));
			}
		}

		/// <summary>
		/// Returns a random number within a specified range.
		/// If both are omitted, the default is 0.0 to 1.0.<br/>
		/// If only one parameter is specified, the other parameter defaults to 0. Otherwise, specify the minimum and maximum number to be generated, in either order.<br/>
		/// For integers, the minimum value and maximum value are both included in the set of possible numbers that may be returned.<br />
		/// The full range of 64-bit integers is supported.<br />
		/// For floating point numbers, the maximum value is generally excluded.
		/// </summary>
		/// <param name="a">The inclusive lower bound of the random number returned. Default: 0.</param>
		/// <param name="b">The exclusive upper bound of the random number returned. Default 1.</param>
		/// <returns>A random number in the range of <c><paramref name="a"/> - <paramref name="b"/></c>,
		/// If either <paramref name="a"/> or <paramref name="b"/> is a floating point number or both are omitted,<br/>
		/// the result will be a floating point number. Otherwise, the result will be an integer.
		/// </returns>
		public static object Random(object a = null, object b = null)
		{
			var r = RandomGenerator;

			if (a is null && b is null)
				return r.NextDouble();

			if (a is long l0)
			{
				if (b is long l1)
				{
					var min = Math.Min(l0, l1);
					var max = Math.Max(l0, l1);
					return r.NextInt64(min, max + 1L);//Integer ranges include the max number.
				}
				else if (b is null)
				{
					var min = Math.Min(0L, l0);
					var max = Math.Max(0L, l0);
					return r.NextInt64(min, max + 1L);//If one param is omitted, it defaults to 0.
				}
			}
			else if (b is long l11)
			{
				var min = Math.Min(0L, l11);
				var max = Math.Max(0L, l11);
				return r.NextInt64(min, max + 1L);//If one param is omitted, it defaults to 0.
			}

			var mind = a.Ad();
			var maxd = b.Ad();
			var lower = Math.Min(mind, maxd);
			var upper = Math.Max(mind, maxd);
			return r.NextDouble(lower, upper);
		}

		/// <summary>
		/// Rounds a number to a specified number of fractional digits.
		/// </summary>
		/// <param name="number">A double number to be rounded.</param>
		/// <param name="n">The number of double places in the return value.</param>
		/// <returns>The number nearest to <paramref name="number"/> that contains a number of fractional digits equal to <paramref name="n"/>.<br />
		/// If
		/// </returns>
		public static object Round(object number, object n = null)
		{
			var num = number.Ad();
			var places = n.Al();

			if (places == 0L)
				return Convert.ToInt64(num);

			var mult = Math.Pow(10, places);//Code taken from AHK.
			return (num >= 0.0 ? Math.Floor(num * mult + 0.5) : Math.Ceiling((num * mult) - 0.5)) / mult;
		}

		/// <summary>
		/// Returns the sine of the specified angle.
		/// </summary>
		/// <param name="number">An angle, measured in radians.</param>
		/// <returns>The sine of <paramref name="n"/>.</returns>
		public static object Sin(object number) => Math.Sin(number is double d ? d : number.Ad());

		/// <summary>
		/// Returns the square root of a specified number.
		/// </summary>
		/// <param name="number">A number.</param>
		/// <returns>The positive square root of <paramref name="n"/> if positive, else an exception is thrown.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if the value is negative.</exception>
		public static object Sqrt(object number)
		{
			var n = number is double d ? d : number.Ad();

			if (n < 0)
				return Errors.ErrorOccurred($"Sqrt() argument of {n} was negative.");

			return Math.Sqrt(n);
		}

		/// <summary>
		/// Returns the tangent of the specified angle.
		/// </summary>
		/// <param name="number">An angle, measured in radians.</param>
		/// <returns>The tangent of <paramref name="n"/>.</returns>
		public static object Tan(object number) => Math.Tan(number is double d ? d : number.Ad());
	}
}