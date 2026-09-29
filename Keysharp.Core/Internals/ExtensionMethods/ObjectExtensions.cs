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

		// Two families convert a value. TryCoerce* never raises: false means no value or one which does not convert. Coerce*
		// takes a built-in's parameter: no value gives the default, and a value which does not convert raises a TypeError,
		// returning false when the script continues it, and the caller then returns at once, as an AutoHotkey built-in does.
		// Both leave def in the out value whenever they return false.

		/// <summary>
		/// Converts a value to an Integer, truncating a Float (or float string) toward zero as AutoHotkey does, unless
		/// <paramref name="allowFloat"/> is false. Never raises.
		/// The leading type tests intentionally duplicate those inside the TryParse* primitives: they keep the hottest cases
		/// (long and double objects) free of any call into the larger, non-inlinable parsing methods.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryCoerceLong(this object obj, out long value, long def = 0L, bool allowFloat = true)
		{
			if (obj is long l)//Hottest path: script integers.
			{
				value = l;
				return true;
			}

			if (obj is double d)//Second hottest: arithmetic results such as mW / 3.
			{
				value = allowFloat ? unchecked((long)d) : def;
				return allowFloat;
			}

			if (obj is not null)
			{
				if (obj is not (string or bool or int or Any))
				{
					if (!obj.TryCoerceString(out var text))
					{
						value = def;
						return false;
					}

					obj = text;
				}

				if (obj.TryParseLong(out value))//Handles bool/int and integer/hex strings.
					return true;

				if (allowFloat && obj.TryParseDouble(out double dd))//Only reached for float strings such as "426.67".
				{
					value = unchecked((long)dd);
					return true;
				}
			}

			value = def;
			return false;
		}

		/// <summary>As <see cref="TryCoerceLong"/>, for an int.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryCoerceInt(this object obj, out int value, int def = 0, bool allowFloat = true)
		{
			var ok = obj.TryCoerceLong(out var l, def, allowFloat);
			value = unchecked((int)l);
			return ok;
		}

		/// <summary>Converts a value to a number. Never raises.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool TryCoerceDouble(this object obj, out double value, double def = 0.0)
		{
			if (obj is double d)
			{
				value = d;
				return true;
			}

			if (obj is not null and not (string or long or int or bool or Any))
			{
				if (!obj.TryCoerceString(out var text))
				{
					value = def;
					return false;
				}

				obj = text;
			}

			if (obj.TryParseDouble(out value))
				return true;

			value = def;
			return false;
		}

		/// <summary>
		/// Converts a scalar, or an object through one ToString call, to text. Never raises: what a ToString raises is
		/// contained, unreported, as a try contains it, and only a script exit propagates. A ToString which returns no value
		/// gives no text.
		/// </summary>
		internal static bool TryCoerceString(this object obj, out string value, string def = "")
		{
			bool ok;

			if (obj is null or string or long or double or bool)
				ok = ToStringCore(obj, out value);
			else
			{
				try
				{
					if (Script.TheScript == null)
						ok = ToStringCore(obj, out value);
					else
					{
						using var scope = Keysharp.Runtime.Flow.EnterTry();
						ok = ToStringCore(obj, out value);
					}
				}
				catch (Exception ex) when (!Internals.Flow.TryGetException<Builtins.Flow.UserRequestedExitException>(ex, out _))
				{
					ok = false;
					value = null;
				}
			}

			if (ok && value != null)
				return true;

			value = def ?? "";
			return false;
		}

		/// <summary>
		/// Converts a built-in's Integer parameter: see <see cref="TryCoerceLong"/>, and the family comment above.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool CoerceLong(this object obj, out long value, long def = 0L, bool allowFloat = true) =>
			obj.TryCoerceLong(out value, def, allowFloat) || obj == null || RaiseTypeError(obj, typeof(long));

		/// <summary>As <see cref="CoerceLong"/>, for an int.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool CoerceInt(this object obj, out int value, int def = 0, bool allowFloat = true)
		{
			var ok = obj.CoerceLong(out var l, def, allowFloat);
			value = unchecked((int)l);
			return ok;
		}

		/// <summary>Converts a built-in's number parameter: see the family comment above.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static bool CoerceDouble(this object obj, out double value, double def = 0.0) =>
			obj.TryCoerceDouble(out value, def) || obj == null || RaiseTypeError(obj, typeof(double));

		/// <summary>
		/// Converts a built-in's String parameter: see the family comment above. What an object's ToString raises
		/// propagates, and a ToString which returns no value gives <paramref name="def"/>.
		/// </summary>
		internal static bool CoerceString(this object obj, out string value, string def = "")
		{
			if (ToStringCore(obj, out value))
			{
				value ??= def ?? "";
				return true;
			}

			_ = Errors.TypeErrorOccurred(obj, typeof(string));
			value = def ?? "";
			return false;
		}

		// A scalar's text, or the scalar result of an object's ToString. False when the value has no text form; null text
		// for no value. What the ToString raises propagates.
		private static bool ToStringCore(object obj, out string text)
		{
			if (obj is Any && (!Script.TryInvoke(obj, "ToString", out obj) || obj is Any))
			{
				text = null;
				return false;
			}

			text = obj switch
			{
				null => null,
				string s => s,
				long l => l.ToString(CultureInfo.InvariantCulture),
				double d => Script.FormatFloat(d),
				bool b => b ? "1" : "0",
				_ => obj.ToString()
			};
			return true;
		}

		// Raises the TypeError of a failed conversion, and returns false for a caller to return at once when the script
		// continues it.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool RaiseTypeError(object obj, Type type)
		{
			_ = Errors.TypeErrorOccurred(obj, type);
			return false;
		}

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

			if (!ScanNumber((obj as string ?? obj.ToString()).AsSpan(), out var s, out var isFloat, out var isHex) || (requireDot && !isFloat))
			{
				outvar = 0.0D;
				return false;
			}

			if (isHex)
			{
				var parsed = TryParseHex(s, out var l2);
				outvar = l2;
				return parsed;
			}

			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out outvar);
		}

		/// <summary>
		/// AutoHotkey's numeric string grammar: optional spaces and tabs around an optional sign, then 0x and hex digits,
		/// or decimal digits with at most one '.' and an optional exponent such as e-5. A '.' or an exponent makes the
		/// string a Float. <paramref name="number"/> is the string without the surrounding spaces and tabs.
		/// </summary>
		internal static bool ScanNumber(ReadOnlySpan<char> s, out ReadOnlySpan<char> number, out bool isFloat, out bool isHex)
		{
			number = s = s.Trim(" \t");
			isFloat = isHex = false;
			bool digits = false, exponent = false;
			var i = s.Length > 0 && (s[0] == '-' || s[0] == '+') ? 1 : 0;

			if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] | 0x20) == 'x')
			{
				isHex = true;
				i += 2;
			}

			for (; i < s.Length; i++)
			{
				var c = s[i];

				if (isHex ? char.IsAsciiHexDigit(c) : char.IsAsciiDigit(c))
					digits = true;
				else if (c == '.' && !isHex && !isFloat)
					isFloat = true;
				else if ((c | 0x20) == 'e' && !isHex && digits && !exponent)
				{
					if (i + 1 < s.Length && (s[i + 1] == '-' || s[i + 1] == '+'))
						i++;

					if (i + 1 >= s.Length || !char.IsAsciiDigit(s[i + 1]))
						return false;

					exponent = isFloat = true;
				}
				else
					return false;
			}

			return digits;
		}

		// A signed 0x number, which wraps to a negative Integer above 0x7FFFFFFFFFFFFFFF as in AutoHotkey.
		private static bool TryParseHex(ReadOnlySpan<char> s, out long value)
		{
			var neg = s[0] == '-';
			var digits = s.Slice(s[0] is '-' or '+' ? 3 : 2);

			if (!long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
				return false;

			if (neg)
				value = unchecked(-value);

			return true;
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

			var text = (obj as string ?? obj.ToString()).AsSpan();

			if (ScanNumber(text, out var s, out var isFloat, out var isHex) && !isFloat)
				return isHex ? TryParseHex(s, out outvar) : long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out outvar);

			if (donoprefixhex)
			{
				s = text.Trim(" \t");
				var neg = s.Length > 0 && s[0] == Keywords.Minus;

				if (long.TryParse(neg ? s.Slice(1) : s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out outvar))
				{
					if (neg)
						outvar = -outvar;

					return true;
				}
			}

			outvar = 0L;
			return false;
		}
	}
}
