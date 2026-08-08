using Keysharp.Builtins;
namespace Keysharp.Internals.ExtensionMethods
{
	/// <summary>
	/// Extension methods for the System.Object class, mostly type-conversion helpers.
	/// Internal so that runtime-compiled user scripts referencing Keysharp.Core do not get
	/// these extensions injected onto every object; friend assemblies (Keysharp, Keysharp.Tests,
	/// Keysharp.Benchmark, Keyview) opt in via a using of this namespace.
	/// </summary>
	internal static class ObjectExtensions
	{
		/// <summary>
		/// Converts an object to a bool.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as a bool if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Ab(this object obj, bool def = default) => obj.TryParseBool(out var b) ? b : def;

		/// <summary>
		/// Converts an object to a double.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as a double if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Ad(this object obj, double def = default) => obj is double d ? d : obj.TryParseDouble(out double dd) ? dd : def;

		/// <summary>
		/// Converts an object to a float.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as a float if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Af(this object obj, float def = default) => obj.TryParseDouble(out double d) ? unchecked((float)d) : def;

		/// <summary>
		/// Converts an object to an int, truncating a Float toward zero to match AutoHotkey.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as an int if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Ai(this object obj, int def = default) => obj.TryCoerceLong(out long l) ? unchecked((int)l) : def;

		/// <summary>
		/// Converts an object to a long, truncating a Float toward zero to match AutoHotkey.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as a long if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long Al(this object obj, long def = default) => obj.TryCoerceLong(out long l) ? l : def;

		/// <summary>
		/// Coerces an object to a long for use in an integer context. This is the single place where
		/// the long/double composition lives: it is the backing logic for <see cref="Ai"/>, <see cref="Al"/>,
		/// <see cref="Aui"/> and the throwing To* converters. A Float (or float string) is truncated
		/// toward zero to match AutoHotkey, unless allowFloat is false.
		/// The leading type tests intentionally duplicate those inside the TryParse* primitives:
		/// they keep the hottest cases (long and double objects) free of any call into the larger,
		/// non-inlinable parsing methods.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="outvar">The resulting long.</param>
		/// <param name="allowFloat">True to truncate a Float (or float string) toward zero, false to reject it. Default: true.</param>
		/// <returns>True if the object yielded a numeric value, else false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryCoerceLong(this object obj, out long outvar, bool allowFloat = true)
		{
			if (obj is long l)//Hottest path: script integers.
			{
				outvar = l;
				return true;
			}

			if (obj is double d)//Second hottest: arithmetic results such as mW / 3.
			{
				if (allowFloat)
				{
					outvar = (long)d;
					return true;
				}

				outvar = 0L;
				return false;
			}

			if (obj is null)
			{
				outvar = 0L;
				return false;
			}

			if (obj.TryParseLong(out outvar))//Handles bool/int and integer/hex strings.
				return true;

			if (allowFloat && obj.TryParseDouble(out double dd))//Only reached for float strings such as "426.67".
			{
				outvar = (long)dd;
				return true;
			}

			outvar = 0L;
			return false;
		}

		/// <summary>
		/// Converts an object to a string.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null, or its ToString() returns no value.</param>
		/// <returns>The object as a string if it was not null, else def.</returns>
		public static string As(this object obj, string def = "")
		{
			if (obj is string s)
				return s;

			//Canonical formatting for the two types whose ToString() does not match AutoHotkey: a Float keeps its
			//point (380.0 => "380.0", not "380"), and a Boolean is an Integer to a script (true => "1", not "True").
			//The Boolean case only fires for a CLR bool -- a script's own `true`/`false` lower to the Integer
			//literals 1 and 0 (Lowerer: `case "true": return Num("1")`) and were always rendered "1"/"0". So this
			//reaches values that ORIGINATE in the CLR: a `bool` accessor such as A_IsSuspended assigned into a
			//string context, and anything coming back through the Ks.Clr boundary.
			if (obj is double or bool)
				return Script.ForceString(obj);

			//A ToString() which returns no value yields def, rather than raising an UnsetError. [v2.1-alpha.30+]
			return (obj is Any kso && Functions.HasMethod(kso, "ToString") != 0L ? Script.InvokeOrNull(kso, "ToString")?.ToString() : obj?.ToString()) ?? def;
		}

		/// <summary>
		/// Converts an object to an unsigned int, truncating a Float toward zero to match AutoHotkey.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="def">A default value to use if obj is null or the conversion fails.</param>
		/// <returns>The object as an unsigned int if conversion succeeded, else def.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Aui(this object obj, uint def = default) => obj.TryCoerceLong(out long l) ? unchecked((uint)l) : def;

		/// <summary>
		/// Converts an object to a long for a context where a number is required, throwing a
		/// <see cref="TypeError"/> if the object is not numeric. A Float (or float string) is
		/// truncated toward zero to match AutoHotkey, unless allowFloat is false, in which case
		/// any Float input throws.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="allowFloat">True to truncate a Float toward zero, false to reject it. Default: true.</param>
		/// <returns>The object as a long.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long ToLong(this object obj, bool allowFloat = true) => obj.TryCoerceLong(out long l, allowFloat) ? l : (long)Errors.TypeErrorOccurred(obj, typeof(long), 0L);

		/// <summary>
		/// Converts an object to an int for a context where a number is required, throwing a
		/// <see cref="TypeError"/> if the object is not numeric. See <see cref="ToLong"/>.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="allowFloat">True to truncate a Float toward zero, false to reject it. Default: true.</param>
		/// <returns>The object as an int.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ToInt(this object obj, bool allowFloat = true) => unchecked((int)obj.ToLong(allowFloat));

		/// <summary>
		/// Converts an object to a double for a context where a number is required, throwing a
		/// <see cref="TypeError"/> if the object is not numeric.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <returns>The object as a double.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double ToDouble(this object obj) => obj is double d ? d : obj.TryParseDouble(out double dd) ? dd : (double)Errors.TypeErrorOccurred(obj, typeof(double), 0.0);

		/// <summary>
		/// Attempts to convert an object to a <see cref="Control"/>.
		/// </summary>
		/// <param name="obj">The object to examine.</param>
		/// <returns>A <see cref="Control"/> if the conversion succeeded, else null.</returns>
		public static Control GetControl(this object obj)
		{
			if (obj is Gui gui)
				return gui.form;
			else if (obj is Gui.Control ctrl)
				return ctrl.Ctrl;
#if WINDOWS
			else if (obj is Keysharp.Builtins.Menu menu)
				return menu.GetMenu();
#endif
			else if (obj is Control control)//Final check in the event it's some kind of native control or form.
				return control;

			return null;
		}

		/// <summary>
		/// Returns whether a callback result non-empty.
		/// </summary>
		/// <param name="result">The callback result to examine.</param>
		/// <returns>True if non-empty, else false.</returns>
		public static bool IsCallbackResultNonEmpty(this object result)
		{
			if (result == null) return false;
			else if (result is long ll) return ll != 0;
			else if (result is double dbl) return dbl != 0.0;
			else if (result is bool b) return b;
			else if (result is string str)
			{
				if (str.AsSpan().Trim().Length == 0) return false;
				if (str.TryParseLong(out long l))
					return l != 0;
				if (str.TryParseDouble(out double dl))
					return dl != 0.0;
				return true;
			}
			return true;
		}

		/// <summary>
		/// Returns whether an object is a <see cref="Gui"/>, <see cref="GuiControl"/> or <see cref="Menu"/>.
		/// </summary>
		/// <param name="obj">The object to examine.</param>
		/// <returns>True if obj was a <see cref="Gui"/>, <see cref="GuiControl"/> or <see cref="Menu"/>, else false.</returns>
		public static bool IsKeysharpGui(this object obj) => obj is Gui || obj is Gui.Control || obj is Keysharp.Builtins.Menu;

		/// <summary>
		/// Returns whether an object is a string that is not empty.
		/// </summary>
		/// <param name="obj">The obj to examine.</param>
		/// <returns>True if obj was a string that was not empty, else false.</returns>
		public static bool IsNotNullOrEmpty(this object obj) => obj != null&& !(obj is string s&& s?.Length == 0);

		/// <summary>
		/// Returns whether an object is null or an empty string.
		/// </summary>
		/// <param name="obj">The obj to examine.</param>
		/// <returns>True if obj was null or an empty string, else false.</returns>
		public static bool IsNullOrEmpty(this object obj) => obj == null ? true : obj is string s ? s?.Length == 0 : false;

		/// <summary>
		/// Attempt to convert an object to a bool.
		/// This treats 0, false, and optionally "off" and "false" string literals as false.
		/// and 1, true and optionally "on" and "true" string literals as true.
		/// This is sugar over <see cref="TryParseBool"/> for ?? composition; no parsing logic lives here.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="allowKeywords">Whether to also accept the "on"/"off"/"true"/"false" string keywords.</param>
		/// <returns>The nullable bool resulting from the conversion.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool? ParseBool(this object obj, bool allowKeywords = false) => obj.TryParseBool(out bool b, allowKeywords) ? b : null;

		/// <summary>
		/// Attempt to convert an object to a bool.
		/// This treats 0, false, and optionally "off" and "false" string literals as false.
		/// and 1, true and optionally "on" and "true" string literals as true.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="outvar">The resulting bool.</param>
		/// <param name="allowKeywords">Whether to also accept the "on"/"off"/"true"/"false" string keywords.</param>
		/// <returns>True if the conversion succeeded, else false.</returns>
		public static bool TryParseBool(this object obj, out bool outvar, bool allowKeywords = false)
		{
			if (obj is bool b)
			{
				outvar = b;
				return true;
			}

			if (obj is long l && (l == 0 || l == 1))
			{
				outvar = l != 0;
				return true;
			}

			if (allowKeywords && obj != null)
			{
				var onoff = Options.OnOff(obj);
				if (onoff != null)
				{
					outvar = onoff.Value;
					return true;
				}
			}

			outvar = false;
			return false;
		}

		/// <summary>
		/// Attempts various methods for converting an object to a double value.<br/>
		/// This is sugar over <see cref="TryParseDouble"/> for ?? composition; no parsing logic lives here.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="requireDot">Whether to require a . character in the string when parsing after other attempts have failed.</param>
		/// <returns>The converted value as a nullable double.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double? ParseDouble(this object obj, bool requireDot = false) => obj.TryParseDouble(out double d, requireDot) ? d : null;

		/// <summary>
		/// Attempts various methods for converting an object to a double value.<br/>
		/// This will first attempt direct casting because it's the most efficient and the most likely scenario.<br/>
		/// String parsing will be attempted after that.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="outvar">The resulting double.</param>
		/// <param name="requireDot">Whether to require a . character in the string when parsing after other attempts have failed.</param>
		/// <returns>True if the conversion succeeded, else false.</returns>
		public static bool TryParseDouble(this object obj, out double outvar, bool requireDot = false)
		{
			if (obj is double d)
			{
				outvar = d;
				return true;
			}

			if (obj is long l)
			{
				if (requireDot) { outvar = default; return false; }
				outvar = l;
				return true;
			}

			if (obj is int i)//int is seldom used in Keysharp, so check last.
			{
				if (requireDot) { outvar = default; return false; }
				outvar = i;
				return true;
			}

			if (obj is null || obj is Any)
			{
				outvar = default;
				return false;
			}

			var s = (obj as string ?? obj.ToString()).AsSpan().Trim();

			if (s.Length == 0)
			{
				outvar = 0.0D;
				return false;
			}

			if (requireDot && !s.Contains('.'))
			{
				outvar = 0.0D;
				return false;
			}

			if (double.TryParse(s, out outvar))
				return true;

			if (!char.IsNumber(s[s.Length - 1]))//Handle a string specifying a double like "123.0D".
				if (double.TryParse(s.Slice(0, s.Length - 1), out outvar))
					return true;

			return false;
		}

		/// <summary>
		/// Attempts various methods for converting an object to a long value.<br/>
		/// This is sugar over <see cref="TryParseLong"/> for ?? composition; no parsing logic lives here.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="donoprefixhex">Whether to treat a hexadecimal string without an 0x prefix as valid.</param>
		/// <returns>The converted value as a nullable long.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long? ParseLong(this object obj, bool donoprefixhex = false) => obj.TryParseLong(out long l, donoprefixhex) ? l : null;

		/// <summary>
		/// Attempts various methods for converting an object to a long value.<br/>
		/// This will first attempt direct casting because it's the most efficient and the most likely scenario.<br/>
		/// String parsing will be attempted after that.<br/>
		/// This is the STRICT integer parse: a double (Float) is rejected. Callers that want
		/// AutoHotkey's Float-to-integer truncation must use <see cref="TryCoerceLong"/> instead.
		/// </summary>
		/// <param name="obj">The object to convert.</param>
		/// <param name="outvar">The resulting long.</param>
		/// <param name="donoprefixhex">Whether to treat a hexadecimal string without an 0x prefix as valid.</param>
		/// <returns>True if the conversion succeeded, else false.</returns>
		public static bool TryParseLong(this object obj, out long outvar, bool donoprefixhex = false)
		{
			if (obj is long l)
			{
				outvar = l;
				return true;
			}
			else if (obj is bool b)
			{
				outvar = b ? 1L : 0L;
				return true;
			}

			if (obj is double)//Fast-reject Floats. Integer coercion of Floats lives in TryCoerceLong.
			{
				outvar = default;
				return false;
			}

			if (obj is null || obj is Any)
			{
				outvar = default;
				return false;
			}

			ReadOnlySpan<char> s = (obj as string ?? obj.ToString()).AsSpan().Trim();

			if (s.Length == 0)
			{
				outvar = 0L;
				return false;
			}

			if (long.TryParse(s, out l))
			{
				outvar = l;
				return true;
			}

			if (!char.IsNumber(s[s.Length - 1]))//Handle a string specifying a long like "123L".
				if (long.TryParse(s.Slice(0, s.Length - 1), out outvar))
					return true;

			var neg = false;

			if (s[0] == Keywords.Minus)
			{
				neg = true;
				s = s.Slice(1);
			}

			if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
					long.TryParse(s.Slice(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out outvar))
			{
				if (neg)
					outvar = -outvar;

				return true;
			}

			if (donoprefixhex &&
					long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out outvar))
			{
				if (neg)
					outvar = -outvar;

				return true;
			}

			outvar = 0L;
			return false;
		}

		/// <summary>
		/// Returns the string representation of an object.
		/// </summary>
		/// <param name="obj">The object to examine.</param>
		/// <returns>If obj is not null, the result of calling obj.ToString(), else empty string.</returns>
		public static string Str(this object obj) => obj != null ? obj.ToString() : "";
	}
}
