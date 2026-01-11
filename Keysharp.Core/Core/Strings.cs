using System.Configuration;
using System.Formats.Tar;

namespace Keysharp.Core
{
	public static partial class KeysharpEnhancements
	{
		private static readonly object[] nullPlaceholder = [null];

		/// <summary>
		/// Decodes a Base64 character string to an <see cref="Array"/> of binary data.
		/// </summary>
		/// <param name="str">The Base64 string to decode.</param>
		/// <returns>The decoded Base64 string as an <see cref="Array"/> of bytes.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any errors occur.</exception>
		public static object Base64Decode(object str)
		{
			var s = str.As();

			try
			{
				return new Keysharp.Core.Buffer(Convert.FromBase64String(s));
			}
			catch (Exception ex)
			{
				return Errors.ErrorOccurred($"Error decoding base64 string {s}: {ex.Message}");
			}
		}

		/// <summary>
		/// Encodes binary data to a Base64 character string.
		/// </summary>
		/// <param name="value">The data to encode.</param>
		/// <returns>A Base64 string representation of the given binary data.</returns>
		public static string Base64Encode(object value) => Convert.ToBase64String(Crypt.ToByteArray(value));

		/// <summary>
		/// Formats a string using the same syntax used by string.Format(), except it uses 1-based indexing.
		/// This is made available for users who prefer standard C# style formatting over the AHK
		/// style used in Format().
		/// <see cref="https://learn.microsoft.com/en-us/dotnet/api/system.string.format"/>
		/// </summary>
		/// <param name="str">The format string.</param>
		/// <param name="args">The arguments to pass to the format string.</param>
		/// <returns>The newly formatted string.</returns>
		public static string FormatCs(object str, params object[] args) => string.Format(str.As(), nullPlaceholder.Concat(args));

		/// <summary>
		/// Joins together the string representation of all array elements, separated by the specified separator.
		/// </summary>
		/// <param name="sep">The separator to use between each item.</param>
		/// <param name="args">The parameters to join together as a string.</param>
		/// <returns>The newly joined string of all arguments separated by the specified separator.</returns>
		public static string Join(object sep, params object[] args) => string.Join(sep.ToString(), args);

		/// <summary>
		/// Makes all line endings in a string match the value passed in, or the default newline (DefaultNewLine).
		/// </summary>
		/// <param name="str">The string whose line endings will be normalized.</param>
		/// <param name="endOfLine">The line ending character to use. Default: DefaultNewLine.</param>
		/// <returns>A new copy of the string with all line endings set to the specified value.</returns>
		public static string NormalizeEol(object str, object endOfLine = null)
		{
			var text = str.As();
			var eol = endOfLine.As(DefaultNewLine);

			return text.ReplaceLineEndings(eol);
		}

		/// <summary>
		/// Determines if a string starts with a given string, using the current culture.
		/// </summary>
		/// <param name="str">The string to examine the start of.</param>
		/// <param name="str2">The string to search for.</param>
		/// <param name="ignoreCase">True to ignore case, else case sensitive. Default: case sensitive.</param>
		/// <returns>1 if str started with str2, else 0.</returns>
		public static long StartsWith(object str, object str2, object ignoreCase = null) => str.As().StartsWith(str2.As(), ignoreCase.Ab() ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase) ? 1L : 0L;
	}

	internal class StringsData
	{
		internal ConcurrentDictionary<nint, GCHandle> gcHandles = [];

		~StringsData() => Free();

		internal void Free()
		{
			foreach (var kv in gcHandles)
				kv.Value.Free();
		}
	}

	/// <summary>
	/// Public interface for strings-related functions.
	/// </summary>
	public static class Strings
	{
		/// <summary>
		/// Returns the string corresponding to number.<br/>
		/// This is always a single Unicode character, but for practical reasons,<br/>
		/// Unicode supplementary characters (where number is in the range 0x10000 to 0x10FFFF) are counted as two characters.
		/// </summary>
		/// <param name="number">A Unicode value.</param>
		/// <returns>The string corresponding to number. This is always a single Unicode character.</returns>
		public static string Chr(object number) => char.ConvertFromUtf32(number.Ai());

		/// <summary>
		/// Determines if a string ends with a given string, using the current culture.
		/// </summary>
		/// <param name="str">The string to examine the end of.</param>
		/// <param name="str2">The string to search for.</param>
		/// <param name="ignoreCase">True to ignore case, else case sensitive. Default: case sensitive.</param>
		/// <returns>1 if str ended with str2, else 0.</returns>
		public static long EndsWith(object str, object str2, object ignoreCase = null) => str.As().EndsWith(str2.As(), ignoreCase.Ab() ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase) ? 1L : 0L;

		/// <summary>
		/// Formats a string using a format string containing placeholders (e.g. "{1:05d}" or "{}")
		/// and a variable number of arguments. (Argument indices are 1–based when specified.)
		/// <param name="str">The format string.</param>
		/// <param name="args">The arguments to pass to the format string.</param>
		/// <returns>The newly formatted string.</returns>
		/// </summary>
		public static string Format(object str, params object[] args)
		{
			string formatStr = str.As();
			StringBuilder result = new StringBuilder();
			int pos = 0;
			int nextArg = 0; // if no explicit index is given, use the next argument.

			while (pos < formatStr.Length)
			{
				// Append literal text until the next '{'
				int braceIndex = formatStr.IndexOf('{', pos);

				if (braceIndex < 0)
				{
					_ = result.Append(formatStr, pos, formatStr.Length - pos);
					break;
				}

				_ = result.Append(formatStr, pos, braceIndex - pos);
				pos = braceIndex;

				// Check for literal escaped braces.
				// According to the spec, use {{} or {}} to output a literal { or }.
				if (pos + 2 < formatStr.Length &&
						(formatStr[pos + 1] == '{' || formatStr[pos + 1] == '}') &&
						formatStr[pos + 2] == '}')
				{
					_ = result.Append(formatStr[pos + 1]);
					pos += 3;
					continue;
				}

				int placeholderStart = pos;
				pos++; // skip the opening '{'
				// --- Parse an optional index (a sequence of digits) ---
				int indexStart = pos;

				while (pos < formatStr.Length && char.IsDigit(formatStr[pos]))
					pos++;

				int argIndex;

				if (pos > indexStart)
				{
					// Convert the (1–based) index from the format string to 0–based.
					var indexStr = formatStr.AsSpan(indexStart, pos - indexStart);

					if (!int.TryParse(indexStr, out argIndex))
					{
						// On parse error, output the placeholder literally.
						_ = result.Append(formatStr, placeholderStart, pos - placeholderStart);
						continue;
					}

					argIndex--;
				}
				else
				{
					// No index specified; use next argument.
					argIndex = nextArg;
				}

				if (argIndex < 0 || argIndex >= args.Length)
				{
					// Invalid index – simply include the entire placeholder text.
					int closingBrace = formatStr.IndexOf('}', pos);

					if (closingBrace < 0)
					{
						_ = result.Append(formatStr, placeholderStart, formatStr.Length - placeholderStart);
						break;
					}
					else
					{
						_ = result.Append(formatStr, placeholderStart, closingBrace - placeholderStart + 1);
						pos = closingBrace + 1;
						continue;
					}
				}

				if (pos == indexStart) // no explicit index was provided
					nextArg++;

				// --- Parse an optional format specifier ---
				SpecInfo spec;

				if (pos < formatStr.Length && formatStr[pos] == ':')
				{
					pos++; // skip ':'
					int specStart = pos;

					// First: skip any flags (valid flags: - + 0 space #)
					while (pos < formatStr.Length && "-+0 #".Contains(formatStr[pos]))
						pos++;

					// Then: width digits
					while (pos < formatStr.Length && char.IsDigit(formatStr[pos]))
						pos++;

					// Optionally: a precision, beginning with a dot.
					if (pos < formatStr.Length && formatStr[pos] == '.')
					{
						pos++; // skip '.'

						while (pos < formatStr.Length && char.IsDigit(formatStr[pos]))
							pos++;
					}

					// The specCore is the substring with flags, width and precision.
					var specCore = formatStr.AsSpan(specStart, pos - specStart);
					// Next comes the conversion type (if any)
					var typeChar = 's'; // default conversion is to string.

					if (pos < formatStr.Length)
					{
						char c = formatStr[pos];

						if ("diouxXeEfgGaAcCps".Contains(c))
						{
							typeChar = c;
							pos++;
						}
						else
						{
							typeChar = 's';
						}
					}

					// For string values, check for an optional case transformation specifier:
					// U (upper‐case), L (lower‐case) or T (title case). (Also accept lower–case letters.)
					char customFormat = '\0';

					if (typeChar == 's' && pos < formatStr.Length && "ULlTt".Contains(formatStr[pos]))
					{
						customFormat = char.ToUpperInvariant(formatStr[pos]);
						pos++;

						if (pos < formatStr.Length && formatStr[pos] == 's')
							pos++;
					}

					spec = ParseSpecInfo(specCore, typeChar);
					spec.CustomFormat = customFormat;
				}
				else
				{
					// No specifier: default to string conversion.
					spec = new SpecInfo { Type = 's' };
				}

				// The placeholder must end with a closing brace.
				if (pos >= formatStr.Length || formatStr[pos] != '}')
				{
					// If not, output the placeholder literally.
					_ = result.Append(formatStr, placeholderStart, pos - placeholderStart);
					continue;
				}

				pos++; // skip the closing '}'
				// --- Format the argument according to the parsed specifier ---
				string formattedArg = FormatArgument(args[argIndex], spec);

				// If a custom string–transformation was requested (U, L, or T), apply it.
				if (spec.CustomFormat != '\0' && spec.Type == 's')
				{
					switch (spec.CustomFormat)
					{
						case 'U':
							formattedArg = formattedArg.ToUpperInvariant();
							break;

						case 'L':
							formattedArg = formattedArg.ToLowerInvariant();
							break;

						case 'T':
							formattedArg = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(formattedArg.ToLower());
							break;
					}
				}

				_ = result.Append(formattedArg);
			}

			return result.ToString();
		}

		/// <summary>
		/// Transforms a YYYYMMDDHH24MISS timestamp into the specified date/time format.
		/// </summary>
		/// <param name="stamp">If blank or omitted, it defaults to the current local date and time.<br/>
		/// Otherwise, specify all or the leading part of a timestamp in the YYYYMMDDHH24MISS format.
		/// </param>
		/// <param name="format">
		/// If blank or omitted, it defaults to the time followed by the long date, both of which will be<br/>
		/// formatted according to the current user's locale. For example: 4:55 PM Saturday, November 27, 2004.<br/>
		/// Otherwise, specify one or more of the date-time formats from the tables below, along with any literal spaces<br/>
		/// and punctuation in between (commas do not need to be escaped; they can be used normally).<br/>
		/// Date formats:
		///     d    : Day of the month without leading zero (1 – 31).<br/>
		///     dd   : Day of the month with leading zero(01 – 31).<br/>
		///     ddd  : Abbreviated name for the day of the week (e.g.Mon) in the current user's language.<br/>
		///     dddd : Full name for the day of the week (e.g.Monday) in the current user's language.<br/>
		///     M    : Month without leading zero (1 – 12).<br/>
		///     MM   : Month with leading zero (01 – 12).<br/>
		///     MMM  : Abbreviated month name (e.g.Jan) in the current user's language.<br/>
		///     MMMM : Full month name (e.g.January) in the current user's language.<br/>
		///     y    : Year without century, without leading zero (0 – 99).<br/>
		///     yy   : Year without century, with leading zero (00 – 99).<br/>
		///     yyyy : Year with century.For example: 2005.<br/>
		///     gg   : Period/era string for the current user's locale (blank if none).<br/>
		/// Time formats:
		///     h   : Hours without leading zero; 12-hour format (1 – 12).<br/>
		///     hh  : Hours with leading zero; 12-hour format (01 – 12).<br/>
		///     H   : Hours without leading zero; 24-hour format (0 – 23).<br/>
		///     HH  : Hours with leading zero; 24-hour format (00 – 23).<br/>
		///     m   : Minutes without leading zero (0 – 59).<br/>
		///     mm  : Minutes with leading zero (00 – 59).<br/>
		///     s   : Seconds without leading zero (0 – 59).<br/>
		///     ss  : Seconds with leading zero (00 – 59).<br/>
		///     t   : Single character time marker, such as A or P (depends on locale).<br/>
		///     tt  : Multi-character time marker, such as AM or PM (depends on locale).<br/>
		/// Standalone formats:
		///     (Blank)   : Leave Format blank to produce the time followed by the long date. For example, in some locales it might appear as 4:55 PM Saturday, November 27, 2004.<br/>
		///     Time      : Time representation for the current user's locale, such as 5:26 PM.<br/>
		///     ShortDate : Short date representation for the current user's locale, such as 02/29/04.<br/>
		///     LongDate  : Long date representation for the current user's locale, such as Friday, April 23, 2004.<br/>
		///     YearMonth : Year and month format for the current user's locale, such as February, 2004.<br/>
		///     YDay      : Day of the year without leading zeros(1 – 366).<br/>
		///     YDay0     : Day of the year with leading zeros(001 – 366).<br/>
		///     WDay      : Day of the week (1 – 7). Sunday is 1.<br/>
		///     YWeek     : The ISO 8601 full year and week number.For example: 200453.<br/>
		///     If the week containing January 1st has four or more days in the new year, it is considered week 1.<br/>
		///     Otherwise, it is the last week of the previous year, and the next week is week 1.<br/>
		///     Consequently, both January 4th and the first Thursday of January are always in week 1.<br/>
		/// Additional options:
		///     The following options can appear inside the YYYYMMDDHH24MISS parameter immediately after the timestamp<br/>
		///     (if there is no timestamp, they may be used alone).<br/>
		///     R: Reverse. Have the date come before the time (meaningful only when Format is blank).<br/>
		///     Ln: If this option is not present, the current user's locale is used to format the string.<br/>
		///     To use the system's locale instead, specify LSys. To use a specific locale, specify the letter L followed by a<br/>
		///     hexadecimal or decimal locale identifier (LCID).<br/>
		///     Dn: Date options. Specify for n one of the following numbers:<br/>
		///     0          : Force the default options to be used. This also causes the short date to be in effect.<br/>
		///     1          : Use short date (meaningful only when Format is blank; not compatible with 2 and 8).<br/>
		///     2          : Use long date (meaningful only when Format is blank; not compatible with 1 and 8).<br/>
		///     4          : Use alternate calendar (if any).<br/>
		///     8          : Use Year-Month format (meaningful only when Format is blank; not compatible with 1 and 2).<br/>
		///     0x10       : Add marks for left-to-right reading order layout.<br/>
		///     0x20       : Add marks for right-to-left reading order layout.<br/>
		///     0x80000000 : Do not obey any overrides the user may have in effect for the system's default date format.<br/>
		///     0x40000000 : Use the system ANSI code page for string translation instead of the locale's code page.<br/>
		/// Tn: Time options. Specify for n one of the following numbers:<br/>
		///     0          : Force the default options to be used.This also causes minutes and seconds to be shown.<br/>
		///     1          : Omit minutes and seconds.<br/>
		///     2          : Omit seconds.<br/>
		///     4          : Omit time marker (e.g.AM/PM).<br/>
		///     8          : Always use 24-hour time rather than 12-hour time.<br/>
		///     12         : Combination of the above two.<br/>
		///     0x80000000 : Do not obey any overrides the user may have in effect for the system's default time format.<br/>
		///     0x40000000 : Use the system ANSI code page for string translation instead of the locale's code page.
		/// </param>
		/// <returns>The formatted date/time string</returns>
		public static string FormatTime(object stamp = null, object format = null)
		{
			var s = stamp.As();
			var f = format.As();
			DateTime time;
			var output = string.Empty;
			var splits = s.Split(' ');
			var ci = CultureInfo.CurrentCulture;

			if (s?.Length == 0)
			{
				time = DateTime.Now;
			}
			else
			{
				s = splits[0];
				var haslsys = splits.Contains("LSys", StringComparer.OrdinalIgnoreCase);

				if (haslsys)
					ci = new CultureInfo(ci.LCID, false);

				for (var i = 1; i < splits.Length; i++)
				{
					if (!haslsys && splits[i].StartsWith("L", StringComparison.OrdinalIgnoreCase))
					{
						ci = new CultureInfo(splits[i].Substring(1).ParseInt(false).Value, false);
					}
					else if (splits[i].StartsWith("D", StringComparison.OrdinalIgnoreCase))
					{
						var di = splits[i].Substring(1).ParseLong(false).Value;

						if (di == 0x80000000)
							if (!haslsys)
								ci = new CultureInfo(ci.LCID, false);//No user overrides, if we haven't already done this above.
					}
					else if (splits[i].StartsWith("T", StringComparison.OrdinalIgnoreCase))
					{
						var ti = splits[i].Substring(1).ParseLong(false).Value;

						if (ti == 0x80000000)
							if (!haslsys)
								ci = new CultureInfo(ci.LCID, false);//No user overrides, if we haven't already done this above.
					}
				}

				try
				{
					time = Conversions.ToDateTime(s, ci.Calendar);
				}
				catch
				{
					output = null;
					return output;
				}
			}

			if (f != string.Empty)
			{
				var fl = f.ToLowerInvariant();

				switch (fl)
				{
					case Keyword_Time:
						f = "h:mm tt";
						break;

					case Keyword_ShortDate:
						f = "d";
						break;

					case Keyword_LongDate:
						f = "D";
						break;

					case Keyword_YearMonth:
						f = "Y";
						break;

					case Keyword_YDay:
						output = ci.Calendar.GetDayOfYear(time).ToString();
						return output;

					case Keyword_YDay0:
						output = ci.Calendar.GetDayOfYear(time).ToString().PadLeft(3, '0');
						return output;

					case Keyword_WDay:
						output = ((int)ci.Calendar.GetDayOfWeek(time) + 1).ToString();
						return output;

					case Keyword_YWeek:
					{
						var week = ci.Calendar.GetWeekOfYear(time, CalendarWeekRule.FirstFourDayWeek, ci.DateTimeFormat.FirstDayOfWeek);
						output = ci.Calendar.GetYear(time).ToString() + week;
						return output;
					}

					default:
						if (f.Contains('\''))
						{
							f = f.Replace("'", "\"");
							f = f.Replace("\"\"\"\"", "\\'");
						}

						break;
				}

				if (fl.Length == 1)
					f = "%" + f;
			}
			else
			{
				if (splits.Contains("R", StringComparer.OrdinalIgnoreCase))
					f = "f";
				else
					f = "h:mm tt dddd, MMMM d, yyyy";
			}

			try
			{
				output = time.ToString(f, ci);
			}
			catch
			{
				output = DefaultErrorString;
			}

			return output;
		}

		/// <summary>
		/// Searches for a given occurrence of a string, from the left or the right.
		/// </summary>
		/// <param name="haystack">The string whose content is searched.</param>
		/// <param name="needle">The string to search for.</param>
		/// <param name="caseSensitive">If omitted, it defaults to Off. Otherwise, specify one of the following values:<br/>
		///     On or 1 (true)  : The search is case-sensitive.<br/>
		///     Off or 0 (false): The search is not case-sensitive, i.e.the letters A-Z are considered identical to their lowercase counterparts.<br/>
		///     Locale          : The search is not case-sensitive according to the rules of the current user's locale.<br/>
		///     For example, most English and Western European locales treat not only the letters A-Z as identical to their lowercase counterparts,<br/>
		///     but also non-ASCII letters like Ä and Ü as identical to theirs.
		/// </param>
		/// <param name="startingPos">If omitted, the entire string is searched.<br/>
		/// Otherwise, specify the position at which to start the search, where 1 is the first character, 2 is the second character, and so on.<br/>
		/// Negative values count from the end of haystack, so -1 is the last character, -2 is the second-last, and so on.<br/>
		/// If occurrence is omitted, a negative startingPos causes the search to be conducted from right to left.<br/>
		/// However, startingPos has no effect on the direction of the search if occurrence is specified.<br/>
		/// If the absolute value of StartingPos is greater than the length of Haystack, 0 is returned.
		/// </param>
		/// <param name="occurrence">
		/// If omitted, it defaults to the first match in haystack.<br/>
		/// The search is conducted from right to left if startingPos is negative; otherwise it is conducted from left to right.<br/>
		/// If occurrence is positive, the search is always conducted from left to right.<br/>
		/// Specify 2 for Occurrence to return the position of the second match, 3 for the third match, etc.<br/>
		/// If occurrence is negative, the search is always conducted from right to left.<br/>
		/// For example, -2 searches for the second occurrence from the right.
		/// </param>
		/// <returns>This function returns the position of an occurrence of the string needle in the string haystack.<br/>
		/// Position 1 is the first character; this is because 0 is synonymous with "false", making it an intuitive "not found" indicator.<br/>
		/// Regardless of the values of startingPos or Occurrence, the return value is always relative to the first character of Haystack.
		/// </returns>
		public static long InStr(object haystack, object needle, object caseSensitive = null, object startingPos = null, object occurrence = null)
		{
			var input = haystack.As();
			var n = needle.As();
			var comp = caseSensitive.As();
			var o = occurrence.Ai(1);
			var startPos = startingPos.Ai(o < 0 ? -1 : 1);

			if (string.IsNullOrEmpty(n))
				return (long)Errors.ValueErrorOccurred("Search string was empty", null, DefaultErrorLong);

			if (startPos == 0 || o == 0)
				return (long)Errors.ValueErrorOccurred("StartingPos and Occurrence must be non-zero", null, DefaultErrorLong);

			// determine direction:
			//  - if Occurrence was omitted and startPos<0, reverse
			//  - or if Occurrence was supplied and o<0, reverse
			bool reverse = (occurrence == null && startPos < 0)    // default-occurrence + negative startPos
						   || (occurrence != null && o < 0);           // explicit negative occurrence
			var cs = comp != "" ? Conversions.ParseComparisonOption(comp) : StringComparison.OrdinalIgnoreCase;
			const int offset = 1;//Everything is 1-based indexing.

			if (reverse)
			{
				return offset + input.LastNthIndexOf(n, startPos > 0 ? startPos - input.Length - 1 : startPos, Math.Abs(o), cs);
			}
			else
			{
				if (startPos > input.Length)
					return 0L;

				return offset + input.NthIndexOf(n, startPos > 0 ? startPos - 1 : input.Length + startPos, o, cs);
			}
		}

		/// <summary>
		/// Trims characters from the beginning of a string.
		/// </summary>
		/// <param name="str">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning of the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string LTrim(object str, object omitChars = null) => str.As().TrimStart(omitChars.As(" \t").ToCharArray());

		/// <summary>
		/// Returns the ordinal value (numeric character code) of the first character in the specified string.
		/// </summary>
		/// <param name="str">The string whose ordinal value is retrieved.</param>
		/// <returns>
		/// The ordinal value of the string, or 0 if String is empty.<br/>
		/// If the string begins with a Unicode supplementary character, returns the corresponding Unicode character code (a number between 0x10000 and 0x10FFFF).<br/>
		/// Otherwise returns a value in the range 0 to 0xFFFF (for Unicode).
		/// </returns>
		public static long Ord(object str)
		{
			var s = str.As();
			return !string.IsNullOrEmpty(s) ? char.ConvertToUtf32(s, 0) : 0L;
		}

		/// <summary>
		/// Trims characters from the end of a string.
		/// </summary>
		/// <param name="str">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the endof the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string RTrim(object str, object omitChars = null) => str.As().TrimEnd(omitChars.As(" \t").ToCharArray());

		/// <summary>
		/// Arranges a variable's contents in alphabetical, numerical, or random order (optionally removing duplicates).
		/// </summary>
		/// <param name="str">The string to sort.</param>
		/// <param name="options">If blank or omitted, str will be sorted in ascending alphabetical order (case-insensitive),<br/>
		/// using a linefeed (`n) as separator. Otherwise, specify a string of one or more options from the options section below<br/>
		/// (in any order, with optional spaces in between).
		/// C, C1 or COn: Case-sensitive sort (ignored if the N option is also present).<br/>
		/// <br/>
		/// C0 or COff: Case-insensitive sort.<br/>
		/// The uppercase letters A-Z are considered identical to their lowercase counterparts for the purpose of the sort.<br/>
		/// This is the default mode if none of the other case sensitivity options are used.<br/>
		/// <br/>
		/// CL or CLocale: Case-insensitive sort based on the current user's locale.<br/>
		/// For example, most English and Western European locales treat the letters A-Z and ANSI letters like Ä and Ü as identical<br/> to their lowercase counterparts.<br/>
		/// This method also uses a "word sort", which treats hyphens and apostrophes in such a way that words like "coop" and "co-op" stay together.<br/>
		/// Depending on the content of the items being sorted, the performance will be 1 to 8 times worse than the default method of insensitivity.<br/>
		/// <br/>
		/// CLogical: Like CLocale, but digits in the strings are considered as numerical content rather than text.<br/>
		/// For example, "A2" is considered less than "A10". However, if two numbers differ only by the presence of a leading zero,<br/>
		/// the string with leading zero may be considered less than the other string. The exact behavior may differ between OS versions.<br/>
		/// <br/>
		/// Dx: Specifies x as the delimiter character, which determines where each item begins and ends.<br/>
		/// The delimiter is always case-sensitive. If this option is not present, x defaults to linefeed (`n). In most cases this will work<br/>
		/// even if lines end with CR+LF (`r`n), but the carriage return (`r) is included in comparisons and therefore affects the sort order.<br/>
		/// For example, "B`r`nA" will sort as expected, but "A`r`nA`t`r`nB" will place A`t`r before A`r.<br/>
		/// <br/>
		/// N: Numeric sort. Each item is assumed to be a number rather than a<br/>
		/// string (for example, if this option is not present, the string 233 is considered to be less than the string 40 due to alphabetical ordering).<br/>
		/// Both decimal and hexadecimal strings (e.g. 0xF1) are considered to be numeric.<br/>
		/// Strings that do not start with a number are considered to be zero for the purpose of the sort.<br/>
		/// Numbers are treated as 64-bit floating point values so that the decimal portion of each number (if any) is taken into account.<br/>
		/// <br/>
		/// Pn: Sorts items based on character position n (do not use hexadecimal for n).<br/>
		/// If this option is not present, n defaults to 1, which is the position of the first character.<br/>
		/// The sort compares each string to the others starting at its nth character.<br/>
		/// If n is greater than the length of any string, that string is considered to be blank for the purpose of the sort.<br/>
		/// When used with option N (numeric sort), the string's character position is used, which is not necessarily the same as<br/>
		/// the number's digit position.<br/>
		/// <br/>
		/// R: Sorts in reverse order (alphabetically or numerically depending on the other options).<br/>
		/// <br/>
		/// Random: Sorts in random order. This option causes all other options except D, Z, and U to be ignored<br/>
		/// (though N, C, and CL still affect how duplicates are detected). Examples:<br/>
		/// <br/>
		/// MyVar := Sort(MyVar, "Random")<br/>
		/// MyVar := Sort(MyVar, "Random Z D|")<br/>
		/// U: Removes duplicate items from the list so that every item is unique.<br/>
		/// If the C option is in effect, the case of items must match for them to be considered identical.<br/>
		/// If the N option is in effect, an item such as 2 would be considered a duplicate of 2.0.<br/>
		/// If either the P or \ (backslash) option is in effect, the entire item must be a duplicate,<br/>
		/// not just the substring that is used for sorting.<br/>
		/// If the Random option or custom sorting is in effect, duplicates are removed only if they appear adjacent to each other<br/>
		/// as a result of the sort. For example, when "A|B|A" is sorted randomly, the result could contain either one or two A's.<br/>
		/// <br/>
		/// Z: To understand this option, consider a variable that contains "RED`nGREEN`nBLUE`n".<br/>
		/// If the Z option is not present, the last linefeed (`n) is considered to be part of the last item,<br/>
		/// and thus there are only 3 items. But by specifying Z, the last `n (if present) will be considered to<br/>
		/// delimit a blank item at the end of the list, and thus there are 4 items (the last being blank).<br/>
		/// <br/>
		/// \: Sorts items based on the substring that follows the last backslash in each.<br/>
		/// If an item has no backslash, the entire item is used as the substring.<br/>
		/// This option is useful for sorting bare filenames (i.e. excluding their paths), such as the example below,<br/>
		/// in which the AAA.txt line is sorted above the BBB.txt line because their directories are ignored for the purpose of the sort:<br/>
		/// <br/>
		/// C:\BBB\AAA.txt<br/>
		/// C:\AAA\BBB.txt<br/>
		/// <br/>
		/// Note: Options N and P are ignored when the \ (backslash) option is present.
		/// </param>
		/// <param name="callback">If omitted, no custom sorting will be performed.<br/>
		/// Otherwise, specify the name of a function or function object to call that compares any two items in the list.<br/>
		/// The callback accepts three parameters and can be defined as follows:<br/>
		///     The first item.<br/>
		///     The second item.<br/>
		///     The offset (in characters) of the second item from the first as seen in the original/unsorted list (see examples).
		/// You can omit one or more parameters from the end of the callback's parameter list if the corresponding information is not needed,<br/>
		/// but in this case an asterisk must be specified as the final parameter, e.g. MyCallback(param1, *).<br/>
		/// When the callback deems the first parameter to be greater than the second, it should return a positive integer;<br/>
		/// when it deems the two parameters to be equal, it should return 0, "", or nothing; otherwise, it should return a negative integer.
		/// </param>
		/// <returns>The sorted version of the specified string.</returns>
		public static string Sort(object str, object options = null, object callback = null)
		{
			IFuncObj function = null;
			var input = str.As();
			var opts = options.As();
			var splits = opts.Split(' ');
			var numeric = false;
			var random = false;
			var reverse = false;
			var slash = false;
			var slashtype = Path.DirectorySeparatorChar;
			var sortAt = 1;
			var split = '\n';
			var unique = false;
			var withcase = false;
			var withlocale = false;
			var zopt = false;

			for (var i = 0; i < opts.Length; i++)
			{
				var c = char.ToLower(opts[i]);

				switch (c)
				{
					case ' ':
						continue;

					case 'c':
						if (i < opts.Length - 1)
						{
							if (char.ToLower(opts[i + 1]) == 'l')
								withlocale = true;

							i++;
						}

						if (!withlocale)
							withcase = true;

						break;

					case 'd':
						if (i < opts.Length - 1)
						{
							split = opts[i + 1];
							i++;
						}

						break;

					case 'p':
						if (i < opts.Length - 1)
						{
							var digits = opts.AsSpan(i + 1).BeginNums();

							if (int.TryParse(digits, out var pp))
								sortAt = pp;

							i += digits.Length;
						}

						break;

					case 'n':
						numeric = true;
						break;

					case 'r':
						if (opts.AsSpan(i).StartsWith("random", StringComparison.OrdinalIgnoreCase))
						{
							i += "random".Length;
							random = true;
						}
						else
							reverse = true;

						break;

					case 'u':
						unique = true;
						break;

					case '/':
					case '\\':
						slash = true;
						slashtype = c;
						break;

					case 'z':
						zopt = true;
						break;

					default:
						break;
				}
			}

			if (callback != null)
				function = Functions.GetFuncObj(callback, null, true);//If supplied, throw if bad.

			var list = input.Split([split], zopt ? StringSplitOptions.None : StringSplitOptions.RemoveEmptyEntries);

			if (split == '\n')
			{
				for (var i = 0; i < list.Length; i++)
				{
					var x = list[i].Length - 1;

					if (x >= 0 && list[i][x] == '\r')
						list[i] = list[i].Substring(0, x);
				}
			}

			sortAt = Math.Max(0, sortAt - 1);
			var compl = withlocale ? StringComparison.CurrentCultureIgnoreCase : StringComparison.OrdinalIgnoreCase;
			var comp = withcase ? StringComparison.Ordinal : compl;

			if (random)
			{
				var rand = new Random();
				list = list.OrderBy(x => rand.Next()).ToArray();
			}
			else if (function != null)
			{
				var index = 0L;
				var indexedlist = new ValueTuple<string, long>[list.Length];//Temporary needed to include the indices for the third argument.

				foreach (var val in list)
					indexedlist[index] = (val, index++);

				var args = new object[3];//Cache to avoid allocations inside of the sort function.
				System.Array.Sort(indexedlist, delegate (ValueTuple<string, long> x, ValueTuple<string, long> y)
				{
					object value = null;
					args[0] = x.Item1;
					args[1] = y.Item1;
					args[2] = y.Item2 - x.Item2;

					try { value = function.Call(args); }
					catch (Exception) { }

					if (value is long l)
						return (int)l;
					//else if (value is int i)
					//  return i;
					else if (value is double d)
						return (int)d;
					else
						return 0;
				});
				index = 0;

				foreach (var val in indexedlist)
					list[index++] = val.Item1;
			}
			else
			{
				System.Array.Sort(list, (x, y) =>
				{
					ReadOnlySpan<char> xs, ys;

					if (withlocale)
					{
						xs = sortAt == 0 || slash ? x.RemoveAll("'-").AsSpan() : sortAt < x.Length ? x.RemoveAll("'-").AsSpan(sortAt) : string.Empty.AsSpan();
						ys = sortAt == 0 || slash ? y.RemoveAll("'-").AsSpan() : sortAt < y.Length ? y.RemoveAll("'-").AsSpan(sortAt) : string.Empty.AsSpan();
					}
					else
					{
						xs = sortAt == 0 || slash ? x.AsSpan() : sortAt < x.Length ? x.AsSpan(sortAt) : string.Empty.AsSpan();
						ys = sortAt == 0 || slash ? y.AsSpan() : sortAt < y.Length ? y.AsSpan(sortAt) : string.Empty.AsSpan();
					}

					if (xs == ys)
					{
						return 0;
					}
					else if (numeric && !slash)
					{
						return double.TryParse(xs, out var a) && double.TryParse(ys, out var b) ?
							   a.CompareTo(b) : xs.CompareTo(ys, compl);
					}
					else
					{
						if (slash)
						{
							var z = xs.LastIndexOf(slashtype);

							if (z != -1)
								xs = xs.Slice(z + 1);

							z = ys.LastIndexOf(slashtype);

							if (z != -1)
								ys = ys.Slice(z + 1);

							if (xs == ys)
								return 0;
						}

						return xs.CompareTo(ys, comp);
					}
				});
			}

			if (unique)
			{
				var ulist = new List<string>(list.Length);

				for (var i = 0; i < list.Length; i++)
				{
					var item = list[i];

					if (numeric)
					{
						if (double.TryParse(item, out var dd))
							if (!list.Any(val => double.TryParse(val, out var d) && d == dd))
								ulist.Add(item);
					}
					else if (random || function != null)
					{
						if (i == 0 || !item.Equals(list[i - 1], comp))
							ulist.Add(item);
					}
					else if (!ulist.Any(val => item.Equals(val, comp)))
						ulist.Add(item);
				}

				list = ulist.ToArray();
			}

			if (reverse && function == null)
				System.Array.Reverse(list);

			return string.Join(split.ToString(), list);
		}

		/// <summary>
		/// Compares two strings alphabetically.
		/// </summary>
		/// <param name="str1">The first string to be compared.</param>
		/// <param name="str2">The second string to be compared.</param>
		/// <param name="caseSense">If omitted, it defaults to Off. Otherwise, specify one of the following values:<br/>
		///     On or 1 (true): The comparison is case-sensitive.<br/>
		///     Off or 0 (false): The comparison is not case-sensitive, i.e. the letters A-Z are considered identical to their lowercase counterparts.<br/>
		///     Locale: The comparison is not case-sensitive according to the rules of the current user's locale.<br/>
		///     For example, most English and Western European locales treat not only the letters A-Z as identical to their lowercase counterparts,<br/>
		///     but also non-ASCII letters like Ä and Ü as identical to theirs.<br/>
		///     Locale is 1 to 8 times slower than Off depending on the nature of the strings being compared.<br/>
		///     Logical: Like Locale, but digits in the strings are considered as numerical content rather than text.<br/>
		///     For example, "A2" is considered less than "A10". However, if two numbers differ only by the presence of a leading zero,<br/>
		///     the string with leading zero may be considered less than the other string.
		/// </param>
		/// <returns>
		/// To indicate the relationship between str1 and str2, this function returns one of the following:<br/>
		///     0, if str1 is identical to str2.<br/>
		///     a positive integer, if str1 is greater than str2.<br/>
		///     a negative integer, if str1 is less than str2.
		/// </returns>
		public static long StrCompare(object str1, object str2, object caseSense = null)
		{
			var s1 = str1.As();
			var s2 = str2.As();
			var s3 = caseSense.As();

			if (s1 != "" || s2 != "")
			{
				var cs = StringComparison.OrdinalIgnoreCase;

				if (s3 != "")
				{
					if (string.Compare(s3, "logical", true) == 0)
						return NaturalComparer.NaturalCompare(s1, s2);

					cs = Conversions.ParseComparisonOption(s3);
				}

				return string.Compare(s1, s2, cs);
			}

			return 0L;
		}

		/// <summary>
		/// Copies a string from a memory address or buffer, optionally converting it from a given code page.
		/// </summary>
		/// <param name="source">A <see cref="Buffer"/>-like object containing the string, or the memory address of the string.</param>
		/// <param name="length">If omitted (or when using 2-parameter mode), it defaults to the current length of the string,<br/>
		/// provided the string is null-terminated. Otherwise, specify the maximum number of characters to read.
		/// </param>
		/// <param name="enc">If omitted, the string is simply copied without any conversion taking place.<br/>
		/// Otherwise, specify the source encoding; for example, "UTF-8", "UTF-16" or "CP936".<br/>
		/// For numeric identifiers, the prefix "CP" can be omitted only in 3-parameter mode.<br/>
		/// Specify an empty string or "CP0" to use the system default ANSI code page.
		/// </param>
		/// <returns>This function returns the copied or converted string. If the source encoding was specified correctly,<br/>
		/// the return value always uses the native encoding.
		/// </returns>
		/// <exception cref="ValueError">Throws a <see cref="ValueError"/> exception if source is null or 0.</exception>
		public static string StrGet(object source, object length = null, object enc = null)
		{
			var hasThree = enc != null;
			var encoding = Encoding.Unicode;
			var len = long.MinValue;

			if (hasThree)
			{
				len = length.Al(long.MinValue);
				encoding = Files.GetEncoding(enc.As());
			}
			else//Second argument could have been either length or encoding.
			{
				var l = length != null ? length.ParseLong(false) : long.MinValue;

				if (l != null)
					len = l.Value;
				else if (length is string encstr)
					encoding = Files.GetEncoding(encstr);
				else
					encoding = Encoding.Unicode;
			}

			nint ptr = 0;
			var buf = source as Buffer;

			if (source is IPointable ip)
				ptr = (nint)ip.Ptr;
			else if (source is long l)
				ptr = new nint(l);

			if (ptr == 0)
				return (string)Errors.ValueErrorOccurred($"No valid address or buffer was supplied.", null, DefaultErrorString);
			else if (ptr.ToInt64() < 65536)//65536 is the first valid address.
				return (string)Errors.ValueErrorOccurred($"Address of {ptr.ToInt64()} is less than the minimum allowable address of 65,536.", null, DefaultErrorString);

			unsafe
			{
				if (len == long.MinValue)//No length specified, only copy up to the first 0.
				{
					if (buf != null)
						len = (long)buf.Size;
					else
						return encoding == Encoding.Unicode ? Marshal.PtrToStringUni(ptr) : Marshal.PtrToStringAnsi(ptr);
				}

				//If length is negative, copy exactly the absolute value of len, regardless of 0s. Clamp to buf size of buf.
				//If length is positive, copy as long as length is not reached and value is not 0.
				var raw = (byte*)ptr.ToPointer();
				int abs = (int)Math.Abs(len);
				int byteCount;

				if (encoding is UnicodeEncoding) byteCount = abs * 2;
				else if (encoding is UTF32Encoding) byteCount = abs * 4;
				else byteCount = abs; // ANSI, UTF-8 (approx: 1 char ≈ 1 byte)

				int maxBytes = buf != null ? (int)Math.Min((long)buf.Size, byteCount) : byteCount;

				Span<byte> span = new Span<byte>(raw, maxBytes);

				if (len > 0)
				{
					int terminatorIndex;
					if (encoding is UnicodeEncoding) // UTF-16, 2-byte code‐units
					{
						// reinterpret as chars, look for '\0', then convert back to byte‐index
						var charSpan = MemoryMarshal.Cast<byte, char>(span);
						int ci = charSpan.IndexOf('\0');
						terminatorIndex = (ci >= 0) ? ci * sizeof(char) : -1;
					}
					else if (encoding is UTF32Encoding) // UTF-32, 4-byte code‐units
					{
						// reinterpret as ints, look for 0, then convert back to byte‐index
						var intSpan = MemoryMarshal.Cast<byte, int>(span);
						int ii = intSpan.IndexOf(0);
						terminatorIndex = (ii >= 0) ? ii * sizeof(int) : -1;
					}
					else // all single-byte encodings (ANSI, UTF-8, etc.)
					{
						terminatorIndex = span.IndexOf((byte)0);
					}

					if (terminatorIndex != -1)
						span = span.Slice(0, terminatorIndex);
				}

				return encoding.GetString(span);
			}
		}

		/// <summary>
		/// Retrieves the count of how many characters are in a string.
		/// </summary>
		/// <param name="str">The string whose contents will be measured.</param>
		/// <returns>The length of the specified string.</returns>
		public static long StrLen(object str) => str.As().Length;

		/// <summary>
		/// Converts a string to lowercase.
		/// </summary>
		/// <param name="str">The string to convert to lowercase.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrLower(object str) => str.As().ToLowerInvariant();

		/// <summary>
		/// Returns the current memory address of a string.
		/// Note, this does not actually point to the string. Instead, it
		/// points to a copy of the bytes of the string.
		/// Note, the caller will have to manually free the returned pointer by calling ObjFree.
		/// </summary>
		/// <param name="value">The string to return a pointer to.</param>
		/// <returns>The memory address of a copy of the string bytes.</returns>
		public static object StrPtr(object value)
		{
			if (value is StringBuffer sb) {
				return sb.Ptr;
			} 
			else if (value is KeysharpObject kso)
			{
				var str = Script.GetPropertyValue(kso, "__Value");
				if (str is StringBuffer sb2)
					return sb2.Ptr;
				var sbr = new StringBuffer(str);
				sbr.EntangledString = kso;
				return sbr;
			}
			value = Encoding.Unicode.GetBytes(value.ToString());
			var gch = GCHandle.Alloc(value, GCHandleType.Pinned);
			var ptr = gch.AddrOfPinnedObject();
			var script = Script.TheScript;

			if (script.StringsData.gcHandles.Remove(ptr, out var oldGch))
				oldGch.Free();

			script.StringsData.gcHandles[ptr] = gch;
			return ptr;
		}

		/// <summary>
		/// Copies a string to a memory address or buffer, optionally converting it to a given code page.
		/// This includes the null terminator (0) character(s).
		/// </summary>
		/// <param name="str">Any string. If a number is given, it is automatically converted to a string.</param>
		/// <param name="target">A Buffer-like object or memory address to which the string will be written.</param>
		/// <param name="length">The maximum number of characters to write, including the null-terminator if required.</param>
		/// <param name="encoding">If omitted, the string is simply copied or measured without any conversion taking place.<br/>
		/// Otherwise, specify the target encoding; for example, "UTF-8", "UTF-16" or "CP936".<br/>
		/// For numeric identifiers, the prefix "CP" can be omitted only in 4-parameter mode.<br/>
		/// Specify an empty string or "CP0" to use the system default ANSI code page.
		/// </param>
		/// <returns>In 4- or 3-parameter mode, this function returns the number of bytes written.<br/>
		/// In 2-parameter mode, this function returns the required buffer size in bytes, including space for the null-terminator.
		/// </returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> is thrown if invalid parameters are detected,<br/>
		/// such as if the converted string would be longer than allowed by length or target.Size.
		/// </exception>
		public static long StrPut(params object[] obj)//Leave this as variadic because the parameter scheme is complex.
		{
			if (obj.Length > 0 && obj[0] != null)
			{
				// Raw string (no terminator here; we handle it explicitly below).
				var s = obj.As(0);
				var len = long.MinValue;
				var encoding = Encoding.Unicode;
				nint ptr = 0;
				Any buf = null;
				var lengthProvided = false;

				if (obj.Length == 1)
					return (long)encoding.GetByteCount(s) + encoding.GetByteCount("\0");

				if (obj.Length > 1)
				{
					buf = obj[1] as Any;

					if (obj[1] is IPointable ip)
						ptr = (nint)ip.Ptr;
					else if (buf != null && Reflections.GetPtrProperty(buf) is long lp)
						ptr = new nint(lp);
					else if (obj[1] is long l)
						ptr = new nint(l);
					else if (obj[1] is string ec)
					{
						var enc = Files.GetEncoding(ec);
						return enc.GetByteCount(s) + enc.GetByteCount("\0");
					}
				}

				if (ptr.ToInt64() < 65536)//65536 is the first valid address.
					return (long)Errors.ValueErrorOccurred($"Address of {ptr.ToInt64()} is less than the minimum allowable address of 65,536.", null, DefaultErrorLong);

				if (obj.Length > 2)
				{
					if (obj.Length == 4)
					{
						encoding = Files.GetEncoding(obj[3]);
						var lengthChars = obj[2].Al(0);
						lengthProvided = true;

						if (lengthChars <= 0)
							return (long)Errors.ValueErrorOccurred(
								"Length must be greater than zero.", null, DefaultErrorLong);

						// Convert characters to bytes according to target encoding "char" width
						len = lengthChars * CharSize(encoding);
					}
					// 3-parameter with Length (String, Target, Length) – native encoding
					else if ((obj[2].ParseLong(false, true) ?? long.MinValue) is long ll && ll != long.MinValue)
					{
						lengthProvided = true;
						var lengthChars = ll;

						if (lengthChars <= 0)
							return (long)Errors.ValueErrorOccurred(
								"Length must be greater than zero.", null, DefaultErrorLong);

						len = lengthChars * CharSize(encoding);
					}
					// 3-parameter with Encoding (String, Target, Encoding)
					else
					{
						encoding = Files.GetEncoding(obj[2]);
						len = long.MinValue;
					}
				}

				// Convert the string *without* the terminator.
				byte[] dataBytes;
				try
				{
					dataBytes = encoding.GetBytes(s);
				}
				catch (Exception ex)
				{
					// Per docs: throw OSError if conversion failed.
					return (long)Errors.OSErrorOccurred(
						$"String conversion failed for the specified encoding: {ex.Message}", null, DefaultErrorLong);
				}
				var terminatorSize = encoding.GetByteCount("\0");

				// Determine capacity in BYTES.
				long capacity = (len == long.MinValue) ? long.MaxValue : len;

				// If Target is a buffer-like object, cap capacity to its Size and enforce rules.
				if (buf != null)
				{
					var hasSize = TryGetPropertyValue(out object sz, buf, "Size");
					var bufSize = hasSize ? sz.Al() : 0L;

					if (!hasSize)
						return (long)Errors.ValueErrorOccurred(
							"Target object is missing a valid Size property.", null, DefaultErrorLong);

					// If Length was supplied, it must NOT exceed Target.Size (error even if data would fit).
					if (lengthProvided && capacity > bufSize)
						return (long)Errors.ValueErrorOccurred(
							"Specified Length exceeds Target.Size.", null, DefaultErrorLong);

					// Effective capacity is the smaller of (Length-in-bytes if supplied) and Target.Size.
					capacity = Math.Min(capacity, bufSize);
				}

				// If we have a known capacity (buffer or Length given), ensure the converted string fits.
				if (capacity != long.MaxValue && dataBytes.Length > capacity)
				{
					// Per docs: throw ValueError if the converted string would be longer than allowed.
					return (long)Errors.ValueErrorOccurred(
						"Converted string is longer than allowed by Length or Target.Size.",
						null, DefaultErrorLong);
				}

				// Decide whether we can include the null-terminator.
				var canWriteNull = capacity - dataBytes.Length >= terminatorSize;

				// Write data (and null if it fits).
				var bytesWritten = dataBytes.Length;

				if (dataBytes.Length > 0)
					Marshal.Copy(dataBytes, 0, ptr, dataBytes.Length);

				if (canWriteNull)
				{
					// Write a zero terminator of the correct width (1/2/4 bytes of zero).
					var zeros = new byte[terminatorSize];
					Marshal.Copy(zeros, 0, ptr + dataBytes.Length, terminatorSize);
					bytesWritten += terminatorSize;
				}

				return bytesWritten;
			}

			return 0L;

			// Helper: size in bytes of one "character" unit for the target encoding.
			static int CharSize(Encoding enc)
			{
				// CodePage: 1200/1201 = UTF-16 LE/BE, 12000/12001 = UTF-32 LE/BE
				switch (enc.CodePage)
				{
					case 1200: // UTF-16LE
					case 1201: // UTF-16BE
						return 2;
					case 12000: // UTF-32LE
					case 12001: // UTF-32BE
						return 4;
					default:
						// UTF-8 and ANSI/multibyte code pages: "character" size is 1 byte for buffer sizing.
						return 1;
				}
			}
		}

		/// <summary>
		/// <see cref="StrReplace(object, object, object, object, VarRef, object)"/>
		/// </summary>
		public static string StrReplace(object haystack, object needle, object replaceText = null, object caseSense = null)
		{
			object obj4 = null;
			object outputVarCount = new VarRef(null);
			return StrReplace(haystack, needle, replaceText, caseSense, outputVarCount, obj4);
		}

		/// <summary>
		/// <see cref="StrReplace(object, object, object, object, VarRef, object)"/>
		/// </summary>
		public static string StrReplace(object haystack, object needle, object replaceText, object caseSense, object outputVarCount)
		{
			object obj4 = null;
			return StrReplace(haystack, needle, replaceText, caseSense, outputVarCount, obj4);
		}

		/// <summary>
		/// Replaces the specified substring with a new string.
		/// </summary>
		/// <param name="haystack">The string whose content is searched and replaced.</param>
		/// <param name="needle">The string to search for.</param>
		/// <param name="replaceText">If blank or omitted, needle will be replaced with blank (empty),<br/>
		/// meaning it will be omitted from the return value.<br/>
		/// Otherwise, specify the string to replace Needle with.
		/// </param>
		/// <param name="caseSense">If omitted, it defaults to Off. Otherwise, specify one of the following values:
		/// On or 1 (true): The search is case-sensitive.
		/// Off or 0 (false): The search is not case-sensitive, i.e.the letters A-Z are considered identical to their lowercase counterparts.
		/// Locale: The search is not case-sensitive according to the rules of the current user's locale.<br/>
		/// For example, most English and Western European locales treat not only the letters A-Z as identical to their lowercase counterparts, but also non-ASCII letters like Ä and Ü as identical to theirs. Locale is 1 to 8 times slower than Off depending on the nature of the strings being compared.
		/// </param>
		/// <param name="outvarcount">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the number of replacements that occurred (0 if none).</param>
		/// <param name="limit">If omitted, it defaults to -1, which replaces all occurrences of the pattern found in haystack.<br/>
		/// Otherwise, specify the maximum number of replacements to allow.
		/// </param>
		/// <returns>The newly modified string.</returns>
		public static string StrReplace(object haystack, object needle, object replaceText, object caseSense, [ByRef] object outputVarCount, object limit)
		{
			var input = haystack.As();
			var search = needle.As();
			var replace = replaceText.As();
			var comp = caseSense.As("Off");
			var lim = limit.Al(-1);

			if (IsAnyBlank(input, search))
			{
                Script.SetPropertyValue(outputVarCount, "__Value", 0L);
                return DefaultObject;
			}

			var compare = Conversions.ParseComparisonOption(comp);
			var ct = 0L;
			var buf = new StringBuilder(input.Length);
			int z = 0, n = 0, l = search.Length;

			while (z < input.Length &&
					(z = input.IndexOf(search, z, compare)) != -1 &&
					(lim < 0 || ct < lim))
			{
				if (n < z)
					_ = buf.Append(input, n, z - n);

				_ = buf.Append(replace);
				z += l;
				n = z;
				ct++;
			}

			if (n < input.Length)
				_ = buf.Append(input, n, input.Length - n);

			Script.SetPropertyValue(outputVarCount, "__Value", ct);
			return buf.ToString();
		}

		/// <summary>
		/// Separates a string into an array of substrings using the specified delimiters.
		/// </summary>
		/// <param name="str">The string to split.</param>
		/// <param name="delimiters">If blank or omitted, each character of the input string will be treated as a separate substring.<br/>
		/// Otherwise, specify either a single string or an array of strings(case-sensitive),<br/>
		/// each of which is used to determine where the boundaries between substrings occur.<br/>
		/// Since the delimiters are not considered to be part of the substrings themselves, they are never included in the returned array.<br/>
		/// Also, if there is nothing between a pair of delimiters within the input string, the corresponding array element will be blank.
		/// </param>
		/// <param name="omitChars">If blank or omitted, no characters will be excluded.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning and end of each array element.<br/>
		/// For example, if omitChars is " `t", spaces and tabs will be removed from the beginning and end (but not the middle) of every element.<br/>
		/// If delimiters is blank, omitChars indicates which characters should be excluded from the array.
		/// </param>
		/// <param name="maxParts">If omitted, it defaults to -1, which means "no limit". Otherwise, specify the maximum number of substrings to return.<br/>
		/// If non-zero, the string is split a maximum of MaxParts-1 times and the remainder of the string is returned<br/>
		/// in the last substring (excluding any leading or trailing omitChars).
		/// </param>
		/// <returns>This function returns an array containing the substrings of the specified string.</returns>
		public static Array StrSplit(object str, object delimiters = null, object omitChars = null, object maxParts = null)
		{
			List<string> del = new ();
			string trim = string.Empty;

			if (str is string input)
			{
				var count = maxParts.Ai(-1);

				if (delimiters is string d)
					del.Add(d);
				else if (delimiters is IList il)
					foreach (var id in il.Flatten(false))
						del.Add(id.ToString());

				if (omitChars is string t)
					trim = t;
				else if (omitChars is IList il)
					foreach (var id in il.Flatten(false))
						trim += id.ToString();

				if (del.Count == 0)
				{
					var list = new List<string>(input.Length);

					if (count > 0)
					{
						int i = 0, ct = 0;

						for (; ct < count - 1 && i < input.Length; i++)
						{
							var ch = input[i];

							if (!trim.Contains(ch))
							{
								list.Add(ch.ToString());
								ct++;
							}
						}

						if (ct < input.Length && i < input.Length)
						{
							list.Add(input.Substring(i));
						}
					}
					else
					{
						foreach (var letter in input)
							if (!trim.Contains(letter))
								list.Add(letter.ToString());
					}

					return new Array(list.Cast<object>());
				}

				var output = count > 0 ? input.Split(del.ToArray(), count, StringSplitOptions.None) : input.Split(del.ToArray(), StringSplitOptions.None);

				if (trim.Length != 0)
				{
					var omit = trim.ToCharArray();

					for (var i = 0; i < output.Length; i++)
						output[i] = output[i].Trim(omit);
				}

				return new Array(output.Cast<object>());
			}

			return [];
		}

		/// <summary>
		/// Converts a string to title case.
		/// </summary>
		/// <param name="str">The string to convert to title case.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrTitle(object str) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.As());

		/// <summary>
		/// Converts a string to uppercase.
		/// </summary>
		/// <param name="str">The string to convert to uppercase.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrUpper(object str) => str.As().ToUpperInvariant();

		/// <summary>
		/// Retrieves one or more characters from the specified position in a string.
		/// </summary>
		/// <param name="str">The string whose content is copied. This may contain binary zero.</param>
		/// <param name="startingPos">Specify 1 to start at the first character, 2 to start at the second, and so on.<br/>
		/// If startingPos is 0 or beyond String's length, an empty string is returned.<br/>
		/// Specify a negative startingPos to start at that position from the right.<br/>
		/// For example, -1 extracts the last character and -2 extracts the two last characters.<br/>
		/// If startingPos tries to go beyond the left end of the string, the extraction starts at the first character.
		/// </param>
		/// <param name="length">If omitted, it defaults to "all characters". Otherwise, specify the maximum number of characters to retrieve<br/>
		/// (fewer than the maximum are retrieved whenever the remaining part of the string is too short).<br/>
		/// You can also specify a negative Length to omit that many characters from the end of the returned<br/>
		/// string (an empty string is returned if all or too many characters are omitted).
		/// </param>
		/// <returns>This function returns the requested substring of the specified string.</returns>
		public static string SubStr(object str, object startingPos = null, object obj2 = null)
		{
			var input = str.As();
			var index = startingPos.Ai(1);
			var length = obj2.Ai(int.MaxValue);

			if (string.IsNullOrEmpty(input) || length == 0 || index == 0 || index > input.Length)
				return DefaultObject;

			if (index < 1)
			{
				index += input.Length;

				if (index < 0)
					index = 0;
			}
			else
				index--;

			var d = input.Length - index;

			if (index < 0 || index >= input.Length)
				return DefaultObject;

			if (length < 0)
				length += d;

			length = Math.Max(0, Math.Min(length, d));
			return input.Substring(index, length);
		}

		/// <summary>
		/// Trims characters from the beginning and end of a string.
		/// </summary>
		/// <param name="str">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning and end of the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string Trim(object str, object omitChars = null) => str.As().Trim(omitChars.As(" \t").ToCharArray());

		/// <summary>
		/// Sets a string capacity by replacing it with a StringBuffer instance.
		/// </summary>
		/// <param name="targetVar">The string to wrap</param>
		/// <param name="requestedCapacity">Capacity for the StringBuffer. If this is -1 then
		/// <paramref name="targetVar"/> contents are replaced with the content of the StringBuffer.</param>
		/// <returns>StringBuffer</returns>
		public static object VarSetStrCapacity([ByRef] object targetVar, object requestedCapacity = null)
		{
			if (!(targetVar is KeysharpObject))
				throw new TypeError($"Expected argument of type VarRef, but received {targetVar.GetType()}");

			var target = Script.GetPropertyValue(targetVar, "__Value") ?? "";
			int capacity;
			if (target is string targetStr)
			{
				if (requestedCapacity == null)
					return (long)targetStr.Length;
				capacity = requestedCapacity.Ai();
				if (capacity < 0)
					return (long)targetStr.Length;
                var sbr = new StringBuffer(targetStr, capacity);
				Script.SetPropertyValue(targetVar, "__Value", sbr);
				return (long)capacity;
			}
			else if (target is StringBuffer sbr)
			{
				if (requestedCapacity == null)
					return sbr.Capacity;

				capacity = requestedCapacity.Ai();
				if (capacity == -1)
				{
					var str = sbr.ToString();
					Script.SetPropertyValue(targetVar, "__Value", str);
					return (long)str.Length;
				}
				else
				{
					sbr.Capacity = capacity;
					return sbr.Capacity;
				}
			}

            throw new TypeError($"Expected referred argument of type string or StringBuffer, but received {target.GetType()}");
        }

		/// <summary>
		/// Compares two version strings.
		/// </summary>
		/// <param name="versionA">The first version string to be compared.</param>
		/// <param name="versionB">The second version string to be compared, optionally prefixed with one of the following operators: <, <=, >, >= or =.</param>
		/// <returns>If versionB begins with an operator symbol, this function returns 1 (true) or 0 (false).<br/>
		/// Otherwise, this function returns one of the following to indicate the relationship between versionA and versionB:<br/>
		///     0 if versionA is equal to versionB.<br/>
		///     a positive integer if versionA is greater than versionB.<br/>
		///     a negative integer if versionA is less than versionB.<br/>
		/// </returns>
		public static long VerCompare(object versionA, object versionB)
		{
			var v1 = versionA.As().Trim();
			var v2 = versionB.As().Trim();
			Exception ex;

			//SemVer cannot parse a C# style version string with 4 numbers.
			//So we have to first try SemVer, then if it fails, try C# style.
			//If that fails, throw the original exception.
			//The shortcoming here is that a C# version string can't be compared to a SemVer style one.

			if (v2.StartsWith("<="))
			{
				v2 = v2.Substring(2);

				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2) <= 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2) <= 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
			else if (v2.StartsWith('<'))
			{
				v2 = v2.Substring(1);

				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2) < 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2) < 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
			else if (v2.StartsWith(">="))
			{
				v2 = v2.Substring(2);

				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2) >= 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2) >= 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
			else if (v2.StartsWith('>'))
			{
				v2 = v2.Substring(1);

				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2) > 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2) > 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
			else if (v2.StartsWith('='))
			{
				v2 = v2.Substring(1);

				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2) == 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2) == 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
			else
			{
				try
				{
					var semver1 = Semver.SemVersion.Parse(v1, Semver.SemVersionStyles.Any);
					var semver2 = Semver.SemVersion.Parse(v2, Semver.SemVersionStyles.Any);
					return semver1.CompareSortOrderTo(semver2);
				}
				catch (Exception e)
				{
					ex = e;
				}

				try
				{
					var csV1 = new Version(v1);
					var csV2 = new Version(v2);
					return csV1.CompareTo(csV2);
				}
				catch (Exception)
				{
				}

				return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex.Message}", DefaultErrorLong);
			}
		}

		/// <summary>
		/// Internal helpers used deep in the keyboard hook to examine certain types of characters.
		/// </summary>
		/// <param name="c">The character to examine.</param>
		/// <returns>bool</returns>
		internal static bool Cisalnum(char c) => (c & 0x80) == 0 && char.IsLetterOrDigit(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisalpha(char c) => (c & 0x80) == 0 && char.IsLetter(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisdigit(char c) => (c & 0x80) == 0 && char.IsDigit(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cislower(char c) => (c & 0x80) == 0 && char.IsLower(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisprint(char c) => (c & 0x80) == 0 && !char.IsControl(c) || char.IsWhiteSpace(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisspace(char c) => (c & 0x80) == 0 && char.IsWhiteSpace(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisupper(char c) => (c & 0x80) == 0 && char.IsUpper(c);

		/// <summary>
		/// See above.
		/// </summary>
		internal static bool Cisxdigit(char c) => (c & 0x80) == 0 && c.IsHex();

		/// <summary>
		/// Examines an array of strings, checking if any are null or empty.
		/// </summary>
		/// <param name="args">The array of strings to examine.</param>
		/// <returns>True if any were null or empty, else false.</returns>
		internal static bool IsAnyBlank(params string[] args)
		{
			foreach (var str in args)
				if (string.IsNullOrEmpty(str))
					return true;

			return false;
		}

		/// <summary>
		/// Returns whether a character is a space or a tab.
		/// </summary>
		/// <param name="c">The character to examine.</param>
		/// <returns>True if the character was a space or a tab.</returns>
		internal static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

		/// <summary>
		/// An internal optimized version of StrCompare().
		/// </summary>
		internal static int StrCmp(string left, string right, bool caseSensitive) => string.Compare(left, right, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Converts an unsigned integer to its octal (base‑8) representation.
		/// </summary>
		private static string ConvertToOctal(ulong num)
		{
			if (num == 0)
				return "0";

			StringBuilder sb = new StringBuilder();

			while (num > 0)
			{
				int digit = (int)(num % 8);
				_ = sb.Insert(0, digit.ToString());
				num /= 8;
			}

			return sb.ToString();
		}

		/// <summary>
		/// Formats one argument according to the given SpecInfo.
		/// (This method “emulates” many of the printf–style conversions.)
		/// </summary>
		private static string FormatArgument(object arg, SpecInfo spec)
		{
			switch (spec.Type)
			{
				// Integer formats – d or i.
				case 'd':
				case 'i':
				{
					long num;

					try
					{
						num = Convert.ToInt64(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						num = 0;
					}

					// Use the precision (if given) as the minimum number of digits.
					string numberStr = spec.Precision.HasValue
									   ? Math.Abs(num).ToString("D" + spec.Precision.Value, CultureInfo.InvariantCulture)
									   : Math.Abs(num).ToString(CultureInfo.InvariantCulture);

					if (num < 0)
						numberStr = "-" + numberStr;
					else if (spec.Plus)
						numberStr = "+" + numberStr;
					else if (spec.Space)
						numberStr = " " + numberStr;

					// Apply padding if a field width was specified.
					if (spec.Width.HasValue && numberStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - numberStr.Length;

						if (spec.LeftAlign)
							numberStr += new string(' ', pad);
						else if (spec.ZeroPad)
						{
							// If there’s a sign character, insert zeros after it.
							if (numberStr.StartsWith("-") || numberStr.StartsWith("+") || numberStr.StartsWith(" "))
							{
								char sign = numberStr[0];
								numberStr = sign + new string('0', pad) + numberStr.Substring(1);
							}
							else
								numberStr = new string('0', pad) + numberStr;
						}
						else
							numberStr = new string(' ', pad) + numberStr;
					}

					return numberStr;
				}

				// Unsigned integer.
				case 'u':
				{
					ulong unum;

					try
					{
						unum = Convert.ToUInt64(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						unum = 0;
					}

					string unumStr = spec.Precision.HasValue
									 ? unum.ToString("D" + spec.Precision.Value, CultureInfo.InvariantCulture)
									 : unum.ToString(CultureInfo.InvariantCulture);

					if (spec.Width.HasValue && unumStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - unumStr.Length;

						if (spec.LeftAlign)
							unumStr += new string(' ', pad);
						else if (spec.ZeroPad)
							unumStr = new string('0', pad) + unumStr;
						else
							unumStr = new string(' ', pad) + unumStr;
					}

					return unumStr;
				}

				// Hexadecimal (lowercase or uppercase)
				case 'x':
				case 'X':
				{
					ulong hexnum;

					try
					{
						hexnum = Convert.ToUInt64(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						hexnum = 0;
					}

					string hexStr = hexnum.ToString(spec.Type == 'x' ? "x" : "X", CultureInfo.InvariantCulture);

					if (spec.Precision.HasValue && hexStr.Length < spec.Precision.Value)
						hexStr = new string('0', spec.Precision.Value - hexStr.Length) + hexStr;

					// If the alternate (#) flag is given and the value is nonzero, prepend 0x or 0X.
					if (spec.Alternate && hexnum != 0)
						hexStr = (spec.Type == 'x' ? "0x" : "0X") + hexStr;

					if (spec.Width.HasValue && hexStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - hexStr.Length;

						if (spec.LeftAlign)
							hexStr += new string(' ', pad);
						else if (spec.ZeroPad)
							hexStr = new string('0', pad) + hexStr;
						else
							hexStr = new string(' ', pad) + hexStr;
					}

					return hexStr;
				}

				// Octal – not built in, so we convert manually.
				case 'o':
				{
					ulong onum;

					try
					{
						onum = Convert.ToUInt64(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						onum = 0;
					}

					string octStr = ConvertToOctal(onum);

					if (spec.Precision.HasValue && octStr.Length < spec.Precision.Value)
						octStr = new string('0', spec.Precision.Value - octStr.Length) + octStr;

					if (spec.Width.HasValue && octStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - octStr.Length;

						if (spec.LeftAlign)
							octStr += new string(' ', pad);
						else if (spec.ZeroPad)
							octStr = new string('0', pad) + octStr;
						else
							octStr = new string(' ', pad) + octStr;
					}

					return octStr;
				}

				// Floating–point formats (f, e, E, g, G)
				case 'f':
				case 'F':
				case 'e':
				case 'E':
				case 'g':
				case 'G':
				{
					double d;

					try
					{
						d = Convert.ToDouble(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						d = 0;
					}

					// Build a .NET numeric format string – e.g. "F2" or "E3".
					string formatSpec = spec.Type.ToString();

					if (spec.Precision.HasValue)
						formatSpec += spec.Precision.Value.ToString();

					string floatStr = d.ToString(formatSpec, CultureInfo.InvariantCulture);

					if (d >= 0)
					{
						if (spec.Plus)
							floatStr = "+" + floatStr;
						else if (spec.Space)
							floatStr = " " + floatStr;
					}

					if (spec.Width.HasValue && floatStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - floatStr.Length;

						if (spec.LeftAlign)
							floatStr += new string(' ', pad);
						else if (spec.ZeroPad)
							floatStr = new string('0', pad) + floatStr;
						else
							floatStr = new string(' ', pad) + floatStr;
					}

					return floatStr;
				}

				// Hexadecimal floating–point (a or A) – not exactly the same as C’s %a but a best–effort.
				case 'a':
				case 'A':
				{
					double d;

					try
					{
						d = Convert.ToDouble(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						d = 0;
					}

					return FormatHexFloat(d, spec);
				}

				// Character – treat the argument as an integer (or its numeric value) and convert to char.
				case 'c':
				case 'C':
				{
					int charCode;

					try
					{
						charCode = Convert.ToInt32(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						charCode = 0;
					}

					char ch = (char)charCode;
					string charStr = ch.ToString();

					if (spec.Width.HasValue && charStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - charStr.Length;

						if (spec.LeftAlign)
							charStr += new string(' ', pad);
						else
							charStr = new string(' ', pad) + charStr;
					}

					return charStr;
				}

				// Pointer – format the numeric value as a pointer in hexadecimal.
				case 'p':
				case 'P':
				{
					ulong ptrVal;

					try
					{
						ptrVal = Convert.ToUInt64(arg, CultureInfo.InvariantCulture);
					}
					catch
					{
						ptrVal = 0;
					}

					// For example, output as 0x followed by 16 hexadecimal digits.
					string ptrStr = ptrVal.ToString("x16", CultureInfo.InvariantCulture).ToUpperInvariant();

					if (spec.Width.HasValue && ptrStr.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - ptrStr.Length;

						if (spec.LeftAlign)
							ptrStr += new string(' ', pad);
						else
							ptrStr = new string(' ', pad) + ptrStr;
					}

					return ptrStr;
				}

				// Default – treat as a string.
				case 's':
				default:
				{
					string s = arg?.ToString() ?? "";

					// If a precision is given, use it as the maximum number of characters.
					if (spec.Precision.HasValue && s.Length > spec.Precision.Value)
						s = s.Substring(0, spec.Precision.Value);

					if (spec.Width.HasValue && s.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - s.Length;

						if (spec.LeftAlign)
							s += new string(' ', pad);
						else
							s = new string(' ', pad) + s;
					}

					return s;
				}
			}
		}

		/// <summary>
		/// Converts a double value to a hexadecimal floating–point string (using the %a/%A style).
		/// </summary>
		private static string FormatHexFloat(double d, SpecInfo spec)
		{
			// Determine if we should use uppercase letters.
			var uppercase = spec.Type == 'A';
			// Handle sign.
			var signStr = "";

			if (d < 0 || (d == 0 && 1.0 / d < 0))
			{
				signStr = "-";
				d = -d;
			}
			else if (spec.Plus)
				signStr = "+";
			else if (spec.Space)
				signStr = " ";

			if (double.IsNaN(d))
				return signStr + (uppercase ? "NAN" : "nan");

			if (double.IsInfinity(d))
				return signStr + (uppercase ? "INF" : "inf");

			if (d == 0.0)
			{
				int prec = spec.Precision.HasValue ? spec.Precision.Value : 13;
				string frac = prec > 0 ? "." + new string('0', prec) : "";
				return signStr + (uppercase ? "0X0" : "0x0") + frac + (uppercase ? "P+0" : "p+0");
			}

			// Obtain the raw bits of the double.
			long bits = BitConverter.DoubleToInt64Bits(d);
			int exponentBits = (int)((bits >> 52) & 0x7FF);
			long fractionBits = bits & ((1L << 52) - 1);
			int exponentUnbiased;
			bool isSubnormal = false;

			if (exponentBits == 0)
			{
				isSubnormal = true;
				exponentUnbiased = 1 - 1023;
			}
			else
			{
				exponentUnbiased = exponentBits - 1023;
				fractionBits |= (1L << 52); // add the implicit 1 for normalized values
			}

			// Determine the desired number of hex digits after the point.
			int totalHexDigits = spec.Precision.HasValue ? spec.Precision.Value : 13;
			// For normalized numbers we show the value as “1.[fraction]”; for subnormals, as “0.[fraction]”
			int intPart = isSubnormal ? 0 : 1;
			// For a double, the fractional part is 52 bits = exactly 13 hex digits.
			int fullFractionDigits = 13;
			long fraction;

			if (!isSubnormal)
				fraction = fractionBits - (1L << 52);
			else
				fraction = fractionBits; // subnormals have no implicit bit

			string fracStr;

			if (totalHexDigits >= fullFractionDigits)
			{
				// Format the available 13 hex digits; if more were requested, pad with trailing zeros.
				fracStr = fraction.ToString("x" + fullFractionDigits, CultureInfo.InvariantCulture);

				if (totalHexDigits > fullFractionDigits)
					fracStr += new string('0', totalHexDigits - fullFractionDigits);
			}
			else
			{
				// When fewer than 13 hex digits are requested, shift right and round.
				int shift = (fullFractionDigits - totalHexDigits) * 4;
				long truncated = fraction >> shift;
				long remainder = fraction & ((1L << shift) - 1);

				if (shift > 0 && remainder >= (1L << (shift - 1)))
				{
					truncated++;

					if (truncated >= (1L << (totalHexDigits * 4)))
					{
						// Rounding causes carry into the integer part.
						intPart++;
						truncated = 0;
					}
				}

				fracStr = truncated.ToString("x").PadLeft(totalHexDigits, '0');
			}

			// If rounding caused the integer part to be 2 or more, re–normalize.
			if (intPart > 1)
			{
				intPart = 1;
				exponentUnbiased++;
			}

			string intPartStr = intPart.ToString(uppercase ? "X" : "x");
			string prefix = uppercase ? "0X" : "0x";
			string pChar = uppercase ? "P" : "p";
			string fracPart = (totalHexDigits > 0) ? "." + (uppercase ? fracStr.ToUpperInvariant() : fracStr) : "";
			string expStr = (exponentUnbiased >= 0 ? "+" : "") + exponentUnbiased.ToString();
			return signStr + prefix + intPartStr + fracPart + pChar + expStr;
		}

		/// <summary>
		/// Parses the “specCore” (the flags, width and precision portion) plus the conversion type.
		/// </summary>
		private static SpecInfo ParseSpecInfo(ReadOnlySpan<char> specCore, char typeChar)
		{
			var spec = new SpecInfo();
			spec.Type = typeChar;
			int pos = 0;

			// Parse any flags.
			while (pos < specCore.Length && "-+0 #".Contains(specCore[pos]))
			{
				switch (specCore[pos])
				{
					case '-': spec.LeftAlign = true; break;

					case '+': spec.Plus = true; break;

					case '0': spec.ZeroPad = true; break;

					case ' ': spec.Space = true; break;

					case '#': spec.Alternate = true; break;
				}

				pos++;
			}

			// Parse the (optional) width.
			int startWidth = pos;

			while (pos < specCore.Length && char.IsDigit(specCore[pos]))
				pos++;

			if (pos > startWidth)
			{
				if (int.TryParse(specCore.Slice(startWidth, pos - startWidth), out int width))
					spec.Width = width;
			}

			// Parse an optional precision (after a dot).
			if (pos < specCore.Length && specCore[pos] == '.')
			{
				pos++; // skip dot
				int startPrec = pos;

				while (pos < specCore.Length && char.IsDigit(specCore[pos]))
					pos++;

				if (pos > startPrec)
				{
					if (int.TryParse(specCore.Slice(startPrec, pos - startPrec), out int prec))
						spec.Precision = prec;
					else
						spec.Precision = 0;
				}
				else
					spec.Precision = 0;
			}

			return spec;
		}

		/// <summary>
		/// Holds the parsed details of a format specifier.
		/// </summary>
		private class SpecInfo
		{
			public bool Alternate = false;
			public char CustomFormat = '\0';
			public bool LeftAlign = false;
			public bool Plus = false;
			public int? Precision = null;
			public bool Space = false;
			public char Type = 's';
			public int? Width = null;
			public bool ZeroPad = false;
			// For U (upper), L (lower) or T (title) – only for strings.
		}
	}
}