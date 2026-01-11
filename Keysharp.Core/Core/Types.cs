namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for class/object reflection-related functions.
	/// </summary>
	public static class Types
	{
		/// <summary>
		/// Returns 1 if the specified value is derived from the specified base object, else 0.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <param name="baseObj">The potential base object to test.</param>
		/// <returns>This function returns 1 if baseObj is in value's chain of base objects, else 0.</returns>
		public static long HasBase(object value, object baseObj) {
            if (value is not Any any)
				return baseObj.GetType().IsAssignableFrom(value.GetType()) ? 1L : 0L;
			
            while (any != null)
            {
				if (baseObj == any)
					return 1L;
				any = any.Base;
            }
			return 0L;
        }

		/// <summary>
		/// Same as <see cref="IsAlpha"/> except that integers and characters 0 through 9 are also allowed.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <param name="locale">If set to "Locale" then Unicode letters are considered as well.</param>
		/// <returns>1 if the object was a string which contained all alpha numeric characters, else 0.</returns>
		public static long IsAlnum(object value, object locale = null)
		{
			var s = value.As();
			Func<char, bool> predicate = locale != null && locale.As().Equals("locale", StringComparison.OrdinalIgnoreCase) ? char.IsLetterOrDigit : char.IsAsciiLetterOrDigit;
			return s?.Length == 0 || s.All(predicate) ? 1L : 0L;
		}

		/// <summary>
		/// Returns 1 if value is a string and is empty or contains only alphabetic characters. 0 if there are any digits, spaces, tabs, punctuation,<br/>
		/// or other non-alphabetic characters anywhere in the string.<br/>
		/// For example, if Value contains a space followed by a letter, it is not considered to be alpha.<br/>
		/// By default only ASCII letters are considered.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <param name="locale">If set to "Locale" then Unicode letters are considered as well.</param>
		/// <returns>1 if the object was a string which contained all alpha characters, else 0.</returns>
		public static long IsAlpha(object value, object locale = null)
		{
			var s = value.As();
			Func<char, bool> predicate = locale != null && locale.As().Equals("locale", StringComparison.OrdinalIgnoreCase) ? char.IsLetter : char.IsAsciiLetter;
			return s?.Length == 0 || s.All(predicate) ? 1L : 0L;
		}

		/// <summary>
		/// Returns 1 if value is a positive integer, an empty string, or a string which contains only the characters 0 through 9, else 0.<br/>
		/// Other characters such as the following are not allowed: spaces, tabs, plus signs, minus signs, decimal points, hexadecimal digits, and the 0x prefix.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if the object was a string of digits, else 0.</returns>
		public static long IsDigit(object value)
		{
			var s = value.As();
			return s?.Length == 0 || s.All(char.IsDigit) ? 1L : 0L;
		}

		/// <summary>
		/// Returns 1 if value is a floating point number or a purely numeric string containing a decimal point. Leading and trailing spaces and tabs are allowed.<br/>
		/// The string may start with a plus sign, minus sign, or decimal point and must not be empty.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value was a floating point number, else 0.</returns>
		public static long IsFloat(object value)
		{
			var o = value;

			if (o is double)// || o is float || o is decimal)
				return 1L;

			double? val;

			if (value is string s)
				val = s.Trim().ParseDouble(false, true);
			else
				val = o.ParseDouble(false, true);

			return val.HasValue ? 1L : 0L;
		}

		/// <summary>
		/// Returns 1 if Value is an integer or a purely numeric string (decimal or hexadecimal) without a decimal point.<br/>
		/// Leading and trailing spaces and tabs are allowed. The string may start with a plus or minus sign and must not be empty.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value was a whole number, else 0.</returns>
		public static long IsInteger(object value)
		{
			var o = value;

			//if (o is long || o is int || o is uint || o is ulong)
			if (o is long)
				return 1L;

			if (o is double)// || o is float || o is decimal)
				return 0L;

			long? val;

			if (value is string s)
				val = s.Trim().ParseLong(false, false);
			else
				val = o.ParseLong(false, false);

			return val.HasValue ? 1L : 0L;
		}

		/// <summary>
		/// Unsupported.
		/// </summary>
		/// <param name="name">Unused.</param>
		/// <returns>Unused.</returns>
		/// <exception cref="Error">Throws an <see cref="Error"/> exception because Keysharp does not support querying labels at runtime.</exception>
		public static long IsLabel(object name) => (long)Errors.ErrorOccurred("C# does not allow querying labels at runtime.", DefaultErrorLong);

		/// <summary>
		/// Returns 1 if value is a string and is empty or contains only lowercase characters.<br/>
		/// 0 if there are any digits, spaces, tabs, punctuation, or other non-lowercase characters anywhere in the string.
		/// By default only ASCII letters are considered.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <param name="locale">If set to "Locale" then Unicode letters are considered as well.</param>
		/// <returns>1 if value contained all lowercase characters, else 0.</returns>
		public static long IsLower(object value, object locale = null)
		{
			var s = value.As();
			if (locale != null && locale.As().Equals("locale", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var ch in s)
					if (!char.IsLower(ch))
						return 0L;
			}
			else
			{
				foreach (var ch in s)
					if (!char.IsAsciiLetterLower(ch))
						return 0L;
			}
			return 1L;
		}

		/// <summary>
		/// Returns 1 if <see cref="IsInteger(object)"/> or <see cref="IsFloat(object)"/> return 1, else 0.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value is a number, else 0.</returns>
		public static long IsNumber(object value) => IsInteger(value) | IsFloat(value);

		/// <summary>
		/// Returns 1 if the specified value is derived from Any, else 0.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value is derived from KeysharpObject, else 0.</returns>
		public static long IsObject(object value) => value is Any ? 1L : 0L;

		/// <summary>
		/// Returns 1 if the specified variable has been assigned a value, meaning it is not null, else 0.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value is not null, else 0.</returns>
		public static long IsSet(object value) => value != null ? 1L : 0L;

		/// <summary>
		/// Returns 1 if the specified VarRef target has been assigned a value, meaning it is not null, else 0.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value is not null, else 0.</returns>
		public static long IsSetRef(object value)
		{
			Any val = value as Any;
			if (val == null) return (long)Errors.ErrorOccurred("IsSetRef requires a VarRef parameter.", DefaultErrorLong);
			return TryGetPropertyValue(out object refvalue, val, "__Value") && refvalue != null ? 1L : 0L;
		}

		/// <summary>
		/// 1 if value is a string and is empty or contains only whitespace consisting of the following characters, else false:<br/>
		/// space (<see cref="A_Space"/> or `s), tab (<see cref="A_Tab"/> or `t), linefeed (`n), return (`r), vertical tab (`v), and formfeed (`f).
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if only spaces are found, else 0.</returns>
		public static long IsSpace(object value) => value.ToString().AsSpan().IndexOfAnyExcept(SpacesSv) != -1 ? 0L : 1L;

		/// <summary>
		/// 1 if value is a valid date-time stamp, which can be all or just the leading part of the YYYYMMDDHH24MISS format.<br/>
		/// For example, a 4-digit string such as 2004 is considered valid. Use StrLen to determine whether additional time components are present.<br/>
		/// value must have an even number of digits between 4 and 14 (inclusive) to be considered valid.<br/>
		/// Years less than 1601 are not considered valid because the operating system generally does not support them.<br/>
		/// The maximum year considered valid is 9999.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value is a valid date-time stamp, else 0.</returns>
		public static long IsTime(object value)
		{
			var s = value.As();
			DateTime dt;

			try
			{
				dt = Conversions.ToDateTime(s);

				if (dt == DateTime.MinValue)
					return 0L;
			}
			catch
			{
				return 0L;
			}

			var y = DateTime.Now.Year;
			int[] t = [y / 100, y % 100, 1, 1, 0, 0, 0, 0];
			var tempdt = new DateTime(t[1], t[2], t[3], t[4], t[5], t[6], CultureInfo.CurrentCulture.Calendar);//Will be wrong if parsing totally failed.
			return dt != tempdt ? 1L : 0L;
		}

		/// <summary>
		/// Returns 1 if value is a string and is empty or contains only uppercase characters.<br/>
		/// 0 if there are any digits, spaces, tabs, punctuation, or other non-lowercase characters anywhere in the string.
		/// By default only ASCII letters are considered.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <param name="locale">If set to "Locale" then Unicode letters are considered as well.</param>
		/// <returns>1 if value contained all uppercase characters, else 0.</returns>
		public static long IsUpper(object value, object locale = null)
		{
			var s = value.As();
			if (locale != null && locale.As().Equals("locale", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var ch in s)
					if (!char.IsUpper(ch))
						return 0L;
			}
			else
			{
				foreach (var ch in s)
					if (!char.IsAsciiLetterUpper(ch))
						return 0L;
			}
			return 1L;
		}

		/// <summary>
		/// Same as <see cref="IsDigit"/> except the characters A through F (uppercase or lowercase) are also allowed. A prefix of 0x is tolerated if present.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>1 if value contains only valid hexadecimal characters (optionally including the 0x prefix), else 0.</returns>
		public static long IsXDigit(object value)
		{
			var s = value.As();
			var sp = s.AsSpan();

			if (sp.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				sp = sp.Slice(2);

			foreach (var ch in sp)
				if (!char.IsAsciiHexDigit(ch))
					return 0L;

			return 1L;
		}

		/// <summary>
		/// Retrieves an object's base object.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>The name of the object's base.</returns>
		public static string ObjGetBase(object value) => value.GetType().BaseType.Name;

		/// <summary>
		/// Returns the class name of an object.
		/// </summary>
		/// <param name="value">The object to examine.</param>
		/// <returns>The class name of value.</returns>
		public static string Type(object value)
		{
			if (value != null)
			{
				string type = null;
				if (value is KeysharpObject kso && kso.op != null) {
					if (kso.op.ContainsKey("__Class"))
						return "Prototype";
                    else if (Script.TryGetPropertyValue(out object oname, kso, "__Class") && oname is string name && name != null)
						type = name;
                    else
						return "Object";
				} else
					type = value.GetType().Name;

				return type switch
			{
					"Double" => "Float",
					"Int64" => "Integer",
					"Boolean" => "Integer",
					"KeysharpObject" => "Object",
					_ => type,
			};
		}
		else
			return "unset";
	}
}
}