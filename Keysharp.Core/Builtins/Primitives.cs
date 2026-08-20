namespace Keysharp.Builtins
{
	public class Primitive : Any
	{
		internal static bool IsNative(object item) => item is string || item is long || item is double || item is bool;
		internal static Type MapPrimitiveToNativeType(object item)
		{
			if (item is string)
				return typeof(Keysharp.Builtins.@String);
			else if (item is bool)
				return typeof(Keysharp.Builtins.Ks.Boolean);
			else if (item is long)
				return typeof(Keysharp.Builtins.Integer);
			else
				return typeof(Keysharp.Builtins.Float);
		}
	}

	public class String : Primitive
	{
		/// <summary>
		/// Converts a value to a string.
		/// </summary>
		/// <param name="value">The value to convert.</param>
		/// <returns>The result of converting value to a string, or value itself if it was a string.<br/>
		/// If value's ToString() returns no value, so does this. [v2.1-alpha.30+]
		/// </returns>
		public static object staticCall(object @this, object value) => value.As(DefaultObject);

		/// <summary>
		/// Determines whether a string starts with a given string.
		/// </summary>
		/// <param name="this">The string to examine the start of.</param>
		/// <param name="token">The string to search for.</param>
		/// <param name="caseSense">If omitted, it defaults to Off (case-insensitive). Otherwise, one of:<br/>
		///     On/1/True: case-sensitive, culture-invariant.<br/>
		///     Off/0/False: case-insensitive, culture-invariant.<br/>
		///     Locale: case-sensitive, compared according to the current user's locale.
		/// </param>
		/// <returns>1 if the string started with <paramref name="token"/>, else 0.</returns>
		public static long StartsWith(object @this, object token, object caseSense = null) =>
			@this.As().StartsWith(token.As(), CaseSenseComparison(caseSense)) ? 1L : 0L;

		/// <summary>
		/// Determines whether a string ends with a given string.
		/// </summary>
		/// <param name="this">The string to examine the end of.</param>
		/// <param name="token">The string to search for.</param>
		/// <param name="caseSense">See <see cref="StartsWith"/>.</param>
		/// <returns>1 if the string ended with <paramref name="token"/>, else 0.</returns>
		public static long EndsWith(object @this, object token, object caseSense = null) =>
			@this.As().EndsWith(token.As(), CaseSenseComparison(caseSense)) ? 1L : 0L;

		/// <summary>
		/// The comparison mode for a <c>CaseSense</c> argument, routed through the same helper InStr and StrCompare
		/// use rather than inventing a second convention: omitted or Off is case-INSENSITIVE, On/1/True is
		/// case-sensitive, and both are Ordinal (culture-invariant); only the explicit <c>Locale</c> option
		/// consults the current culture, and it compares case-sensitively.
		/// </summary>
		private static StringComparison CaseSenseComparison(object caseSense)
		{
			var opt = caseSense.As();
			return opt.Length != 0 ? Conversions.ParseComparisonOption(opt) : StringComparison.OrdinalIgnoreCase;
		}
	}

	public class Number : Primitive
	{
		/// <summary>
		/// Converts a numeric string to a pure integer or floating-point number.
		/// </summary>
		/// <param name="value">The value to convert.</param>
		/// <returns>The result of converting Value to a pure integer or floating-point number, or value itself if it is<br/>
		/// already an Integer or Float value.
		/// </returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the value cannot be converted.</exception>
		public static object staticCall(object @this, object value)
		{
			if (value is long l)
				return l;
			else if (value is double d)
				return d;
			else
			{
				var s = value.As();

				if (!s.Contains('.') && s.TryParseLong(out long ll))
					return ll;

				if (s.TryParseDouble(out double dd))//Also handles scientific notation without a dot, such as "1e5".
					return dd;

				return Errors.TypeErrorOccurred(s, typeof(double));
			}
		}
	}

	public class Integer : Number
	{
		/// <summary>
		/// Converts a numeric string or floating-point value to an integer.
		/// </summary>
		/// <param name="value">The object to be converted</param>
		/// <returns>The converted value as a long.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		public new static object staticCall(object @this, object value) => value.ToLong();
	}

	public class Float : Number
	{
		/// <summary>
		/// Converts a numeric string or integer value to a floating-point number.
		/// </summary>
		/// <param name="value">The object to be converted</param>
		/// <returns>The converted value as a double.</returns>
		/// <exception cref="TypeError">A <see cref="TypeError"/> exception is thrown if the conversion failed.</exception>
		public new static object staticCall(object @this, object value) => value.ToDouble();
	}

	public partial class Ks
	{
		/// <summary>
		/// The type of a boolean value. It extends <see cref="Integer"/> because that is what a boolean is
		/// everywhere else in the language: <c>Type()</c> names it "Integer", it compares equal to 1 or 0 and
		/// does arithmetic as one, so <c>x is Integer</c> has to stay true for it.
		/// <para>The distinction is worth modelling because the language produces booleans on its own -- a
		/// comparison, a negation and <c>Map.Has()</c> all yield one -- and something has to be able to name
		/// that. <see cref="Json.Encode"/> is the visible consequence: a boolean is written as JSON true or
		/// false where the Integer 1 is written as 1.</para>
		/// <para>It lives in the Ks module rather than the global namespace because the global namespace
		/// belongs to AutoHotkey, which has no boolean type, and every Keysharp addition is reached through
		/// Ks. Only the NAME needs the import; the values themselves need nothing.</para>
		/// </summary>
		public class Boolean : Integer
		{
			/// <summary>
			/// Converts a value to a boolean, deciding it exactly as <c>if</c> would.
			/// </summary>
			/// <param name="this">The class object, supplied by the script-static call.</param>
			/// <param name="value">The value to convert.</param>
			/// <returns>True if value is truthy, else false.</returns>
			/// <exception cref="UnsetError">Thrown if value is unset, as <c>if</c> throws.</exception>
			public new static object staticCall(object @this, object value) => Script.ForceBool(value);
		}
	}
}
