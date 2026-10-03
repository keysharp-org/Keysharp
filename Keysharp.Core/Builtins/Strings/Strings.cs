using System.Configuration;
using System.Formats.Tar;

using StringBuffer = Keysharp.Builtins.Ks.StringBuffer;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		private static readonly object[] nullPlaceholder = [null];

		/// <summary>
		/// Formats a string using the same syntax used by string.Format(), except it uses 1-based indexing.
		/// This is made available for users who prefer standard C# style formatting over the AHK
		/// style used in Format().
		/// <see cref="https://learn.microsoft.com/en-us/dotnet/api/system.string.format"/>
		/// </summary>
		/// <param name="str">The format string.</param>
		/// <param name="args">The arguments to pass to the format string.</param>
		/// <returns>The newly formatted string.</returns>
		public static string FormatCs(object str, params object[] args) => str.CoerceString(out var format) ? string.Format(format, nullPlaceholder.Concat(args)) : "";

		/// <summary>
		/// Makes all line endings in a string match the value passed in, or the default newline (DefaultNewLine).
		/// </summary>
		/// <param name="str">The string whose line endings will be normalized.</param>
		/// <param name="endOfLine">The line ending character to use. Default: DefaultNewLine.</param>
		/// <returns>A new copy of the string with all line endings set to the specified value.</returns>
		public static string ReplaceLineEndings(object str, object endOfLine = null) =>
			str.CoerceString(out var text) && endOfLine.CoerceString(out var newLine, DefaultNewLine) ? Conversions.ReplaceLineEndings(text, newLine) : "";
	}

	internal class StringsData
	{
		// The pinned copy StrPtr gives a string which is not a variable's, alive as long as the string is.
		internal readonly ConditionalWeakTable<string, char[]> pinnedCopies = new();
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
		public static string Chr(object number)
		{
			if (!number.CoerceInt(out var n))
				return "";

			if (n < 0 || n > 0x10FFFF)
				return (string)Errors.InvalidParameterErrorOccurred(1, "Chr", number, "");

			// A surrogate code unit is returned as it is, as in AHK, which ConvertFromUtf32 would refuse.
			return n < 0x10000 ? ((char)n).ToString() : char.ConvertFromUtf32(n);
		}

		/// <summary>
		/// Formats a string using a format string containing placeholders (e.g. "{1:05d}" or "{}")
		/// and a variable number of arguments. (Argument indices are 1–based when specified.)
		/// <param name="formatStr">The format string.</param>
		/// <param name="args">The arguments to pass to the format string.</param>
		/// <returns>The newly formatted string.</returns>
		/// </summary>
		public static string Format(object formatStr, params object[] args)
		{
			if (!formatStr.CoerceString(out var format))
				return "";

			StringBuilder result = new StringBuilder();
			int pos = 0;
			int nextArg = 0; // if no explicit index is given, use the next argument.

			while (pos < format.Length)
			{
				// Append literal text until the next '{'
				int braceIndex = format.IndexOf('{', pos);

				if (braceIndex < 0)
				{
					_ = result.Append(format, pos, format.Length - pos);
					break;
				}

				_ = result.Append(format, pos, braceIndex - pos);
				pos = braceIndex;

				// Check for literal escaped braces.
				// According to the spec, use {{} or {}} to output a literal { or }.
				if (pos + 2 < format.Length &&
						(format[pos + 1] == '{' || format[pos + 1] == '}') &&
						format[pos + 2] == '}')
				{
					_ = result.Append(format[pos + 1]);
					pos += 3;
					continue;
				}

				int placeholderStart = pos;
				pos++; // skip the opening '{'
				// --- Parse an optional index (a sequence of digits) ---
				int indexStart = pos;

				while (pos < format.Length && char.IsDigit(format[pos]))
					pos++;

				int argIndex;

				if (pos > indexStart)
				{
					// Convert the (1–based) index from the format string to 0–based.
					var indexStr = format.AsSpan(indexStart, pos - indexStart);

					if (!int.TryParse(indexStr, out argIndex))
					{
						// On parse error, output the placeholder literally.
						_ = result.Append(format, placeholderStart, pos - placeholderStart);
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
					int closingBrace = format.IndexOf('}', pos);

					if (closingBrace < 0)
					{
						_ = result.Append(format, placeholderStart, format.Length - placeholderStart);
						break;
					}
					else
					{
						_ = result.Append(format, placeholderStart, closingBrace - placeholderStart + 1);
						pos = closingBrace + 1;
						continue;
					}
				}

				if (pos == indexStart) // no explicit index was provided
					nextArg++;

				// --- Parse an optional format specifier ---
				SpecInfo spec;

				if (pos < format.Length && format[pos] == ':')
				{
					pos++; // skip ':'
					int specStart = pos;

					// First: skip any flags (valid flags: - + 0 space #)
					while (pos < format.Length && "-+0 #".Contains(format[pos]))
						pos++;

					// Then: width digits
					while (pos < format.Length && char.IsDigit(format[pos]))
						pos++;

					// Optionally: a precision, beginning with a dot.
					if (pos < format.Length && format[pos] == '.')
					{
						pos++; // skip '.'

						while (pos < format.Length && char.IsDigit(format[pos]))
							pos++;
					}

					// The specCore is the substring with flags, width and precision.
					var specCore = format.AsSpan(specStart, pos - specStart);
					// Next comes the conversion type (if any)
					var typeChar = 's'; // default conversion is to string.

					if (pos < format.Length)
					{
						char c = format[pos];

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

					if (typeChar == 's' && pos < format.Length && "ULlTt".Contains(format[pos]))
					{
						customFormat = char.ToUpperInvariant(format[pos]);
						pos++;

						if (pos < format.Length && format[pos] == 's')
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
				if (pos >= format.Length || format[pos] != '}')
				{
					// If not, output the placeholder literally.
					_ = result.Append(format, placeholderStart, pos - placeholderStart);
					continue;
				}

				pos++; // skip the closing '}'

				// --- Format the argument according to the parsed specifier ---
				if (!TryCoerceArgument(args[argIndex], spec.Type, out var arg))
					return "";

				string formattedArg = FormatArgument(arg, spec);

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
							formattedArg = StrTitle(formattedArg);
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
		/// <param name="timestamp">If blank or omitted, it defaults to the current local date and time.<br/>
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
		public static string FormatTime([UserDeclaredName("YYYYMMDDHH24MISS")] object timestamp = null, object format = null)
		{
			if (!timestamp.CoerceString(out var s) || !format.CoerceString(out var f))
				return "";

			// As in AutoHotkey, options follow the timestamp after spaces or tabs, or stand alone when the first character
			// is not a digit, and then the time is the current one.
			DateTime time;
			var output = string.Empty;
			var splits = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			var ci = CultureInfo.CurrentCulture;
			var hasTimestamp = splits.Length > 0 && char.IsAsciiDigit(splits[0][0]);
			var unknownLocale = false;

			if (splits.Contains("LSys", StringComparer.OrdinalIgnoreCase))
				ci = new CultureInfo(ci.Name, false);
			else
				for (var i = hasTimestamp ? 1 : 0; i < splits.Length; i++)
				{
					// An option whose number is missing or does not parse is read as 0, which changes nothing.
					var n = splits[i].Substring(1).ParseLong() ?? 0;

					switch (char.ToUpperInvariant(splits[i][0]))
					{
						// Windows' system default LCID is the locale LSys names, and 0x80000000 disallows user overrides.
						case 'L' when n == 0x800L:
						case 'D' or 'T' when n == 0x80000000:
							ci = new CultureInfo(ci.Name, false);
							break;

						// Windows' user, custom and unspecified default LCIDs name the default locale, which .NET knows by no
						// LCID. A locale .NET does not know gives an empty result, as one Windows does not know does in AHK.
						case 'L' when n is not (0L or 0x400L or 0xC00L or 0x1000L):
							try
							{
								ci = new CultureInfo((int)n, false);
							}
							catch (Exception e) when (e is CultureNotFoundException or ArgumentOutOfRangeException)
							{
								unknownLocale = true;
							}

							break;
					}
				}

			if (!hasTimestamp)
				time = DateTime.Now;
			else
			{
				try
				{
					time = Conversions.ToDateTime(splits[0], ci.Calendar);
				}
				catch
				{
					return DefaultObject;
				}
			}

			if (f != string.Empty)
			{
				// As in AutoHotkey, leading whitespace is ignored when matching a keyword and kept in a picture.
				switch (f.TrimStart(' ', '\t').ToLowerInvariant())
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
						return Conversions.ToIsoYearWeek(time);

					default:
						f = ToCustomTimeFormat(f);

						// A lone specifier would be read as one of .NET's standard formats.
						if (f.Length == 1)
							f = "%" + f;

						break;
				}
			}
			else
			{
				if (splits.Contains("R", StringComparer.OrdinalIgnoreCase))
					f = "f";
				else
					f = "h:mm tt dddd, MMMM d, yyyy";
			}

			if (unknownLocale)
				return "";

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
		/// <param name="caseSense">If omitted, it defaults to Off. Otherwise, specify one of the following values:<br/>
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
		/// A position beyond either end of haystack is taken as that end.
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
		public static long InStr(object haystack, object needle, object caseSense = null, object startingPos = null, object occurrence = null)
		{
			if (!haystack.CoerceString(out var input) || !needle.CoerceString(out var n) || !caseSense.CoerceString(out var comp))
				return 0L;

			if (string.IsNullOrEmpty(n))
				return (long)Errors.ValueErrorOccurred("Search string was empty", null, DefaultErrorLong);

			if (!startingPos.CoerceLong(out var offset, 1L) || !occurrence.CoerceInt(out var o, 1))
				return 0L;

			if (offset == 0 || o == 0)
				return (long)Errors.ValueErrorOccurred("StartingPos and Occurrence must be non-zero", null, DefaultErrorLong);

			if (!Conversions.TryParseComparisonOption(comp, out var cs))
				return 0L;

			cs = CaseCompare.ForSearch(cs);

			// As AHK's BIF_InStr: a negative StartingPos counts from the end and, with Occurrence omitted, searches right
			// to left. A position outside the string is clamped to it, and occurrences never overlap. Right to left, a
			// StartingPos limits the search to the characters up to that position.
			if (offset < 0)
			{
				if (occurrence == null)
					o = -1;

				offset += input.Length + 1;
			}

			if (o > 0)
				offset--;

			var start = (int)Math.Clamp(offset, 0L, input.Length);

			if (o > 0)
				return input.NthIndexOf(n, start, o, cs) + 1L;

			var span = input.AsSpan(0, startingPos != null ? start : input.Length);

			for (var remaining = -o; ;)
			{
				var found = span.LastIndexOf(n.AsSpan(), cs);

				if (found < 0)
					return 0L;

				if (--remaining == 0)
					return found + 1L;

				span = span.Slice(0, found);
			}
		}

		/// <summary>
		/// Trims characters from the beginning of a string.
		/// </summary>
		/// <param name="string">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning of the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string LTrim(object @string, object omitChars = null) =>
		@string.CoerceString(out var s) && omitChars.CoerceString(out var omit, " \t") ? s.TrimAnyOf(omit, end: false) : "";

		/// <summary>
		/// Returns the ordinal value (numeric character code) of the first character in the specified string.
		/// </summary>
		/// <param name="string">The string whose ordinal value is retrieved.</param>
		/// <returns>
		/// The ordinal value of the string, or 0 if String is empty.<br/>
		/// If the string begins with a Unicode supplementary character, returns the corresponding Unicode character code (a number between 0x10000 and 0x10FFFF).<br/>
		/// Otherwise returns a value in the range 0 to 0xFFFF (for Unicode).
		/// </returns>
		public static long Ord(object @string)
		{
			if (!@string.CoerceString(out var s))
				return 0L;

			// A lone surrogate, which splitting emoji text by code unit produces, is its own code unit, as in AHK.
			return string.IsNullOrEmpty(s) ? 0L : char.IsSurrogatePair(s, 0) ? char.ConvertToUtf32(s[0], s[1]) : s[0];
		}

		/// <summary>
		/// Trims characters from the end of a string.
		/// </summary>
		/// <param name="string">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the endof the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string RTrim(object @string, object omitChars = null) =>
		@string.CoerceString(out var s) && omitChars.CoerceString(out var omit, " \t") ? s.TrimAnyOf(omit, start: false) : "";

		/// <summary>
		/// Arranges a variable's contents in alphabetical, numerical, or random order (optionally removing duplicates).
		/// </summary>
		/// <param name="string">The string to sort.</param>
		/// <param name="options">If blank or omitted, the string will be sorted in ascending alphabetical order (case-insensitive),<br/>
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
		/// Otherwise, specify a function object to call that compares any two items in the list.<br/>
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
		public static string Sort(object @string, object options = null, object callback = null)
		{
			if (!@string.CoerceString(out var input) || !options.CoerceString(out var opts))
				return "";

			var delimiter = '\n';
			var comparison = StringComparison.OrdinalIgnoreCase;
			var logical = false;
			var numeric = false;
			var random = false;
			var reverse = false;
			var unique = false;
			var trailingBlank = false;
			var pathSeparator = '\0';
			var offset = 0;

			// As AHK's BIF_Sort reads them.
			for (var i = 0; i < opts.Length; i++)
			{
				switch (char.ToUpperInvariant(opts[i]))
				{
					case 'C':
						var rest = opts.AsSpan(i + 1);
						logical = false;

						if (rest.Length != 0 && char.ToUpperInvariant(rest[0]) == 'L')
						{
							if (rest[1..].StartsWith("ogical", StringComparison.OrdinalIgnoreCase))
							{
								logical = true;
								i += 7;
							}
							else
							{
								comparison = StringComparison.CurrentCultureIgnoreCase;
								i += rest[1..].StartsWith("ocale", StringComparison.OrdinalIgnoreCase) ? 6 : 1;
							}
						}
						else if (rest.StartsWith("Off", StringComparison.OrdinalIgnoreCase))
						{
							comparison = StringComparison.OrdinalIgnoreCase;
							i += 3;
						}
						else if (rest.StartsWith("0"))
							comparison = StringComparison.OrdinalIgnoreCase;
						else
						{
							comparison = StringComparison.Ordinal;

							if (rest.StartsWith("On", StringComparison.OrdinalIgnoreCase))
								i += 2;
						}

						break;

					case 'D':
						if (i + 1 < opts.Length)
							delimiter = opts[++i];

						break;

					case 'N':
						numeric = true;
						break;

					case 'P':
						offset = Math.Max(1, int.TryParse(opts.AsSpan(i + 1).BeginNums(), out var column) ? column : 0) - 1;
						break;

					case 'R':
						if (opts.AsSpan(i).StartsWith("Random", StringComparison.OrdinalIgnoreCase))
						{
							random = true;
							i += 5;
						}
						else
							reverse = true;

						break;

					case 'U':
						unique = true;
						break;

					case 'Z':
						trailingBlank = true;
						break;

					// A '/' sorts by the name after the last forward slash, for paths off Windows.
					case '\\':
					case '/':
						pathSeparator = opts[i];
						break;
				}
			}

			object function = null;

			if (callback != null)
			{
				//Any object, unchecked, as AHK's Sort takes it; anything else is an invalid parameter, as there.
				if (callback is not (Any or Delegate))
					return (string)Errors.InvalidParameterErrorOccurred(3, "Sort", callback, "");

				function = Functions.ToCallback(callback);
			}

			if (input.Length == 0)
				return input;

			// As in AHK, without Z a delimiter at the end ends the last item, rather than starting a blank one, and is put
			// back after the sort. A list whose first line ends in CRLF gives its last line a CRLF for the sort, so that
			// every item ends in `r, which compares and is written back with it.
			var count = input.AsSpan().Count(delimiter) + 1;
			var terminateLast = false;
			var addedCrLf = false;

			if (!trailingBlank && input[^1] == delimiter)
			{
				terminateLast = true;
				count--;
			}
			else if (delimiter == '\n')
			{
				var first = input.IndexOf('\n');
				terminateLast = addedCrLf = first > 0 && input[first - 1] == '\r';
			}

			if (count == 1)
				return input;

			var items = new string[count];
			// Every path sorts indexes into items, so a lower index is also the earlier item.
			var order = new int[count];

			for (int i = 0, start = 0; i < count; i++)
			{
				var end = input.IndexOf(delimiter, start);

				if (end < 0)
					end = input.Length;

				items[i] = input.Substring(start, end - start);
				order[i] = i;
				start = end + 1;
			}

			if (addedCrLf)
				items[^1] += "\r";

			if (function != null)
			{
				// As AHK: the third argument is how far the second item starts after the first in the unsorted list.
				var starts = new int[count];

				for (var i = 1; i < count; i++)
					starts[i] = starts[i - 1] + items[i - 1].Length + 1;

				var args = new object[3];
				Array.SortByCallback(order, (x, y) =>
				{
					args[0] = items[x];
					args[1] = items[y];
					args[2] = (long)(starts[y] - starts[x]);
					return Script.InvokeOrNull(function, null, args);
				});
			}
			else if (random)
				Random.Shared.Shuffle(order);
			else
			{
				// Each item's sort key, read once: the name after the last separator for \, otherwise the item from the P
				// column on, as a number for N. N and P do not apply to \, as in AHK. CL leaves hyphens and apostrophes out,
				// as Windows' word sort does, and puts the items equal that way with fewer of them first, so that coop and
				// co-op stay together but apart.
				var numbers = numeric && pathSeparator == '\0' ? new double[count] : null;
				var keys = numbers == null ? new string[count] : null;
				var wordSort = keys != null && !logical && comparison == StringComparison.CurrentCultureIgnoreCase;
				var strippedCount = wordSort ? new int[count] : null;

				for (var i = 0; i < count; i++)
				{
					var item = items[i];
					var start = pathSeparator != '\0' ? item.LastIndexOf(pathSeparator) + 1 : Math.Min(offset, item.Length);

					if (numbers != null)
						numbers[i] = Atof(item.AsSpan(start));
					else
					{
						var key = start == 0 ? item : item.Substring(start);
						keys[i] = wordSort ? key.RemoveAll("'-") : key;

						if (wordSort)
							strippedCount[i] = key.Length - keys[i].Length;
					}
				}

				System.Array.Sort(order, (x, y) =>
				{
					int result;

					// As in AHK, a pair which compares equal is ordered by position, which R reverses along with the rest,
					// except for equal numbers, which keep their order.
					if (numbers != null)
					{
						result = numbers[x].CompareTo(numbers[y]);

						if (result == 0)
							return x.CompareTo(y);
					}
					else
					{
						result = logical ? LogicalComparer.Compare(keys[x], keys[y]) : CaseCompare.Compare(keys[x], keys[y], comparison);

						if (result == 0 && wordSort)
							result = strippedCount[x].CompareTo(strippedCount[y]);

						if (result == 0)
							result = x.CompareTo(y);
					}

					return reverse ? -result : result;
				});
			}

			// As in AHK, U drops each item equal to the one kept before it, numerically for N without P.
			var output = new StringBuilder(input.Length + 2);
			string prev = null;

			for (var i = 0; i < count; i++)
			{
				var item = items[order[i]];

				if (unique && prev != null
						&& (numeric && offset == 0 ? Atof(item) == Atof(prev)
							: logical ? LogicalComparer.Compare(item, prev) == 0
							: CaseCompare.Equals(item, prev, comparison)))
					continue;

				if (prev != null)
					_ = output.Append(delimiter);

				_ = output.Append(item);
				prev = item;
			}

			if (terminateLast)
				_ = output.Append(delimiter);

			if (addedCrLf)
				output.Length -= 2;

			return output.ToString();
		}

		/// <summary>
		/// Compares two strings alphabetically.
		/// </summary>
		/// <param name="string1">The first string to be compared.</param>
		/// <param name="string2">The second string to be compared.</param>
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
		/// To indicate the relationship between string1 and string2, this function returns one of the following:<br/>
		///     0, if string1 is identical to string2.<br/>
		///     a positive integer, if string1 is greater than string2.<br/>
		///     a negative integer, if string1 is less than string2.
		/// </returns>
		public static long StrCompare(object string1, object string2, object caseSense = null)
		{
			if (!string1.CoerceString(out var s1) || !string2.CoerceString(out var s2) || !caseSense.CoerceString(out var s3))
				return 0L;

			if (s3.Equals("Logical", StringComparison.OrdinalIgnoreCase))
				return LogicalComparer.Compare(s1, s2);

			if (!Conversions.TryParseComparisonOption(s3, out var comparison, additionalDiagnosticChoice: "Logical"))
				return 0L;

			return CaseCompare.Compare(s1, s2, comparison);
		}

		/// <summary>
		/// Copies a string from a memory address or buffer, optionally converting it from a given code page.
		/// </summary>
		/// <param name="source">A <see cref="Buffer"/>-like object containing the string, or the memory address of the string.</param>
		/// <param name="length">If omitted (or when using 2-parameter mode), it defaults to the current length of the string,<br/>
		/// provided the string is null-terminated. Otherwise, specify the maximum number of characters to read.
		/// </param>
		/// <param name="encoding">If omitted, the string is simply copied without any conversion taking place.<br/>
		/// Otherwise, specify the source encoding; for example, "UTF-8", "UTF-16" or "CP936".<br/>
		/// For numeric identifiers, the prefix "CP" can be omitted only in 3-parameter mode.<br/>
		/// Specify an empty string to use the native UTF-16 encoding, where AutoHotkey uses the system default<br/>
		/// ANSI code page. A name which cannot be resolved raises a <see cref="ValueError"/>.
		/// </param>
		/// <returns>This function returns the copied or converted string. If the source encoding was specified correctly,<br/>
		/// the return value always uses the native encoding.
		/// </returns>
		/// <exception cref="ValueError">Throws a <see cref="ValueError"/> exception if source is null or 0.</exception>
		public static string StrGet(object source, object length = null, object encoding = null)
		{
			var hasThree = encoding != null;
			var enc = Encoding.Unicode;
			var len = long.MinValue;

			if (hasThree)
			{
				if ((length is not null && !length.CoerceLong(out len)) || !Files.TryGetEncoding(encoding, out enc))
					return "";
			}
			else//Second argument could have been either length or encoding.
			{
				var l = length != null ? length.ParseLong() : long.MinValue;

				if (l != null)
					len = l.Value;
				else if (length is string encstr)
				{
					if (!Files.TryGetEncoding(encstr, out enc))
						return "";
				}
				else
					enc = Encoding.Unicode;
			}

			if (!Reflections.TryGetPtrProperty(source, out var addr))//No usable (non-null) pointer.
				return (string)Errors.ValueErrorOccurred($"No valid address or buffer was supplied.");

			nint ptr = new nint(addr);
			bool hasSize = Reflections.TryGetSizeProperty(source, out var srcSize);//false for a raw address (long) or a source with no Size.

			if (ptr.ToInt64() < 65536)//65536 is the first valid address.
				return (string)Errors.ValueErrorOccurred($"Address of {ptr.ToInt64()} is less than the minimum allowable address of 65,536.");

			unsafe
			{
				if (len == long.MinValue)//No length specified, only copy up to the first 0.
				{
					if (hasSize)
						len = srcSize;
					else
						return enc == Encoding.Unicode ? Marshal.PtrToStringUni(ptr) : Marshal.PtrToStringAnsi(ptr);
				}

				//If length is negative, copy exactly the absolute value of len, regardless of 0s. Clamp to buf size of buf.
				//If length is positive, copy as long as length is not reached and value is not 0.
				var raw = (byte*)ptr.ToPointer();
				int abs = (int)Math.Abs(len);
				int byteCount;

				if (enc is UnicodeEncoding) byteCount = abs * 2;
				else if (enc is UTF32Encoding) byteCount = abs * 4;
				else byteCount = abs; // ANSI, UTF-8 (approx: 1 char ≈ 1 byte)

				int maxBytes = hasSize ? (int)Math.Min(srcSize, byteCount) : byteCount;

				Span<byte> span = new Span<byte>(raw, maxBytes);

				if (len > 0)
				{
					int terminatorIndex;
					if (enc is UnicodeEncoding) // UTF-16, 2-byte code‐units
					{
						// reinterpret as chars, look for '\0', then convert back to byte‐index
						var charSpan = MemoryMarshal.Cast<byte, char>(span);
						int ci = charSpan.IndexOf('\0');
						terminatorIndex = (ci >= 0) ? ci * sizeof(char) : -1;
					}
					else if (enc is UTF32Encoding) // UTF-32, 4-byte code‐units
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

				return enc.GetString(span);
			}
		}

		/// <summary>
		/// Retrieves the count of how many characters are in a string.
		/// </summary>
		/// <param name="string">The string whose contents will be measured.</param>
		/// <returns>The length of the specified string.</returns>
		public static long StrLen(object @string) => @string.CoerceString(out var s) ? s.Length : 0L;

		/// <summary>
		/// Converts a string to lowercase.
		/// </summary>
		/// <param name="string">The string to convert to lowercase.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrLower(object @string) => @string.CoerceString(out var s) ? s.ToLowerInvariant() : "";

		/// <summary>
		/// Returns the address of a string. A variable, which a direct call passes by reference, has memory of its own,
		/// which takes the variable's value here when the variable holds a different string than the memory last did: the
		/// same string assigned again, such as "" to a variable already empty, leaves what native code wrote there. That
		/// reaches the variable at VarSetStrCapacity(&amp;v, -1) or its next Str argument, and the address stays the same
		/// while the variable exists and its value fits. Any other string is copied to pinned memory, which stays at the
		/// same address as long as the string exists; a temporary, such as a concatenation, lasts at least until the
		/// statement that made it finishes.
		/// </summary>
		/// <param name="value">A string, a reference to a variable holding one, or a StringBuffer.</param>
		/// <returns>The address.</returns>
		public static object StrPtr(object value)
		{
			//Nothing declares this parameter a reference, so an object only takes this path if it provably carries a
			//__Value. An unset variable referred to explicitly is empty, as an output variable starts out. A reference to
			//anything but a variable, such as a property, keeps no memory, so its value is copied as any other is.
			if (Refs.DeclaresValue(value))
			{
				var inner = Refs.GetValueOrNull(value) ?? "";

				if (inner is string text && StringMemory.Of(value) is { } own)
					return own.AddressOf(text);

				value = inner;
			}

			if (value is StringBuffer sb)
				return sb.Ptr;

			if (value is not string str)
				return Errors.TypeErrorOccurred(value, typeof(string), 0L);

			var copy = Script.TheScript.StringsData.pinnedCopies.GetValue(str, static s =>
			{
				var chars = GC.AllocateArray<char>(s.Length + 1, true);//The last element stays zero: the terminator a native reader looks for.
				s.CopyTo(chars);
				return chars;
			});
			return (long)Marshal.UnsafeAddrOfPinnedArrayElement(copy, 0);
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
		/// Specify an empty string to use the native UTF-16 encoding, where AutoHotkey uses the system default<br/>
		/// ANSI code page. A name which cannot be resolved raises a <see cref="ValueError"/>.
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
				if (!obj[0].CoerceString(out var s))
					return 0L;

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
					else if (buf != null && Reflections.TryGetPtrProperty(buf, out var lp))
						ptr = new nint(lp);
					else if (obj[1] is long l)
						ptr = new nint(l);
					else if (obj[1] is string ec)
					{
						if (!Files.TryGetEncoding(ec, out var enc))
							return 0L;

						return enc.GetByteCount(s) + enc.GetByteCount("\0");
					}
				}

				if (ptr.ToInt64() < 65536)//65536 is the first valid address.
					return (long)Errors.ValueErrorOccurred($"Address of {ptr.ToInt64()} is less than the minimum allowable address of 65,536.", null, DefaultErrorLong);

				if (obj.Length > 2)
				{
					if (obj.Length == 4)
					{
						if (!Files.TryGetEncoding(obj[3], out encoding))
							return 0L;

						_ = obj[2].TryCoerceLong(out var lengthChars, 0);
						lengthProvided = true;

						if (lengthChars <= 0)
							return (long)Errors.ValueErrorOccurred(
								"Length must be greater than zero.", null, DefaultErrorLong);

						// Convert characters to bytes according to target encoding "char" width
						len = lengthChars * CharSize(encoding);
					}
					// 3-parameter with Length (String, Target, Length) – native encoding
					else if ((obj[2].ParseLong() ?? long.MinValue) is long ll && ll != long.MinValue)
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
						if (!Files.TryGetEncoding(obj[2], out encoding))
							return 0L;

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
					if (!Reflections.TryGetSizeProperty(buf, out var bufSize))
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
		/// <param name="outputVarCount">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the number of replacements that occurred (0 if none).</param>
		/// <param name="limit">If omitted, it defaults to -1, which replaces all occurrences of the pattern found in haystack.<br/>
		/// Otherwise, specify the maximum number of replacements to allow.
		/// </param>
		/// <returns>The newly modified string.</returns>
		public static string StrReplace(object haystack, object needle, object replaceText = null, object caseSense = null, [ByRef] object outputVarCount = null, object limit = null)
		{
			if (!haystack.CoerceString(out var input) || !needle.CoerceString(out var search) || !replaceText.CoerceString(out var replace)
					|| !caseSense.CoerceString(out var comp, "Off"))
				return "";

			if (!limit.CoerceLong(out var lim, -1))
				return "";

			if (!Conversions.TryParseComparisonOption(comp, out var compare))
				return "";

			compare = CaseCompare.ForSearch(compare);
			var z = input.Length == 0 || search.Length == 0 || lim == 0 ? -1 : input.IndexOf(search, compare);

			if (z < 0)
			{
				if (outputVarCount != null) Refs.SetValue(outputVarCount, 0L);
				return input;
			}

			// Replacing every match without counting is what string.Replace does, in one pass and one allocation.
			if (lim < 0 && outputVarCount == null)
				return input.Replace(search, replace, compare);

			var ct = 0L;
			var buf = new StringBuilder(input.Length);
			var n = 0;

			for (; z >= 0 && (lim < 0 || ct < lim); z = input.IndexOf(search, z, compare))
			{
				if (n < z)
					_ = buf.Append(input, n, z - n);

				_ = buf.Append(replace);
				z += search.Length;
				n = z;
				ct++;
			}

			if (n < input.Length)
				_ = buf.Append(input, n, input.Length - n);

			if (outputVarCount != null) Refs.SetValue(outputVarCount, ct);
			return buf.ToString();
		}

		/// <summary>
		/// Separates a string into an array of substrings using the specified delimiters.
		/// </summary>
		/// <param name="string">The string to split.</param>
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
		public static Array StrSplit(object @string, object delimiters = null, object omitChars = null, object maxParts = null)
		{
			if (!@string.CoerceString(out var input))
				return null;

			string[] delims = null;

			// As in AHK, a list holds non-empty strings only, and an empty list is taken for a mistake rather than a request
			// for characters.
			if (delimiters is Array list)
			{
				if (list.Count > 0 && list.array.TrueForAll(x => x is string { Length: > 0 }))
					delims = [.. list.array.Cast<string>()];
			}
			else if (delimiters is not Any)
			{
				if (!delimiters.CoerceString(out var d))
					return null;

				delims = d.Length > 0 ? [d] : [];
			}

			if (delims == null)
				return (Array)Errors.InvalidParameterErrorOccurred(2, "StrSplit", delimiters, new Array());

			if (!omitChars.CoerceString(out var omit) || !maxParts.CoerceInt(out var parts, -1))
				return null;

			// MaxParts 0 gives no items and a negative one no limit, as AHK's splits_left does.
			if (input.Length == 0 || parts == 0)
				return new Array();

			if (delims.Length != 0)
			{
				// Split takes the first delimiter in the list at the earliest position, as AHK's InStrAny does.
				var split = input.Split(delims, parts > 0 ? parts : int.MaxValue, StringSplitOptions.None);

				for (var i = 0; i < split.Length; i++)
					split[i] = split[i].TrimAnyOf(omit);

				return new Array(split);
			}

			// Each character not omitted is an item, until the last of MaxParts takes the rest of the string.
			var items = new List<object>();

			for (var i = 0; i < input.Length; i++)
			{
				if (omit.Contains(input[i]))
					continue;

				if (items.Count == parts - 1)
				{
					items.Add(input.AsSpan(i).TrimAnyOf(omit).ToString());
					break;
				}

				items.Add(input[i].ToString());
			}

			return new Array(items);
		}

		/// <summary>
		/// Converts a string to title case as AHK's StrToTitleCase does, which Format's T flag also uses: only whitespace
		/// starts a word, and every letter but a word's first is lower case.
		/// </summary>
		/// <param name="string">The string to convert to title case.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrTitle(object @string) => !@string.CoerceString(out var s) ? "" :
			string.Create(s.Length, s, static (dest, src) =>
			{
				var upper = true;

				for (var i = 0; i < src.Length; i++)
				{
					var c = src[i];

					if (char.IsLetter(c))
					{
						c = upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
						upper = false;
					}
					else if (char.IsWhiteSpace(c))
						upper = true;

					dest[i] = c;
				}
			});

		/// <summary>
		/// Converts a string to uppercase.
		/// </summary>
		/// <param name="string">The string to convert to uppercase.</param>
		/// <returns>The newly converted version of the string.</returns>
		public static string StrUpper(object @string) => @string.CoerceString(out var s) ? s.ToUpperInvariant() : "";

		/// <summary>
		/// Retrieves one or more characters from the specified position in a string.
		/// </summary>
		/// <param name="string">The string whose content is copied. This may contain binary zero.</param>
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
		public static string SubStr(object @string, object startingPos = null, object length = null)
		{
			if (!@string.CoerceString(out var input))
				return "";

			if (!startingPos.CoerceInt(out var index, 1) || !length.CoerceInt(out var len, int.MaxValue))
				return "";

			if (string.IsNullOrEmpty(input) || len == 0 || index == 0 || index > input.Length)
				return DefaultErrorString;

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
				return DefaultErrorString;

			if (len < 0)
				len += d;

			len = Math.Max(0, Math.Min(len, d));
			return input.Substring(index, len);
		}

		/// <summary>
		/// Trims characters from the beginning and end of a string.
		/// </summary>
		/// <param name="string">Any string value or variable. Numbers are not supported.</param>
		/// <param name="omitChars">If omitted, spaces and tabs will be removed.<br/>
		/// Otherwise, specify a list of characters (case-sensitive) to exclude from the beginning and end of the specified string.
		/// </param>
		/// <returns>Returns the trimmed version of the specified string.</returns>
		public static string Trim(object @string, object omitChars = null) =>
		@string.CoerceString(out var s) && omitChars.CoerceString(out var omit, " \t") ? s.TrimAnyOf(omit) : "";

		/// <summary>
		/// Enlarges a variable's capacity or frees its memory, as AutoHotkey does. The capacity belongs to the memory the
		/// variable keeps for native code (see <see cref="StrPtr"/>), while the variable itself holds an ordinary string.
		/// </summary>
		/// <param name="targetVar">A reference to the variable.</param>
		/// <param name="requestedCapacity">If omitted, the capacity is returned and the variable is left as it is. Otherwise
		/// the variable becomes empty, whatever it held, with room for this many characters, excluding the null terminator;
		/// 0 frees that room. -1 instead sets the variable to its contents up to the first null character: what native code
		/// wrote to its memory, unless the variable was assigned since.</param>
		/// <returns>The capacity, or for -1 the variable's new length.</returns>
		public static object VarSetStrCapacity([ByRef] object targetVar, object requestedCapacity = null)
		{
			Refs.Demand(targetVar);
			_ = requestedCapacity.TryCoerceLong(out var capacity, 0);
			var memory = (targetVar as VarRef)?.Memory;

			if (requestedCapacity == null || capacity == -1)
			{
				var value = Refs.GetValueOrNull(targetVar);

				//As in AutoHotkey, -1 measures a number by its string form, while a query needs a string.
				if ((value ?? "") is not string text)
				{
					if (value is not (long or double) || requestedCapacity == null)
						return Errors.TypeErrorOccurred(value, typeof(string), 0L);

					return value.CoerceString(out var number) ? (long)number.Length : DefaultObject;
				}

				if (requestedCapacity == null)
					return (long)Math.Max(memory?.Room ?? 0, text.Length);

				var written = memory?.Written(text) ?? (text.IndexOf('\0') is var end and >= 0 ? text[..end] : text);

				if (!ReferenceEquals(written, text))
					_ = Refs.SetValue(targetVar, written);

				return (long)written.Length;
			}

			if (capacity is < 0 or >= int.MaxValue)
				return Errors.ValueErrorOccurred($"Invalid capacity {capacity}.", requestedCapacity, 0L);

			//As in AutoHotkey, only a variable has memory to size; a property would report room it never keeps.
			if (capacity != 0 && (memory ??= StringMemory.Of(targetVar)) == null)
				return Errors.TypeErrorOccurred("Only a variable's own reference has memory to size, not a property's or one made afresh, such as for a function's own variable through %name%.", 0L);

			_ = Refs.SetValue(targetVar, "");
			memory?.Reserve((int)capacity);
			return (long)(memory?.Room ?? 0);
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
			static string TrimVersionPrefix(string version)
			{
				if (!string.IsNullOrEmpty(version) && (version[0] == 'v' || version[0] == 'V'))
					return version.Substring(1).TrimStart();

				return version;
			}

			// Version.Version requires at least 2 components and at most 4, and all must be numeric.
			static string NormalizeSystemVersion(string version)
			{
				if (string.IsNullOrEmpty(version))
					return version;

				foreach (var c in version)
				{
					if (!char.IsDigit(c) && c != '.')
						return version;
				}

				return version.Contains('.') ? version : version + ".0";
			}

			if (!versionA.CoerceString(out var versionAText) || !versionB.CoerceString(out var versionBText))
				return 0L;

			var v1 = TrimVersionPrefix(versionAText.Trim());
			var v2 = versionBText.Trim();
			Exception ex = null;

			//SemVer cannot parse a C# style version string with 4 numbers.
			//So we have to first try SemVer, then if it fails, try C# style.
			//If that fails, throw the original exception.
			//The shortcoming here is that a C# version string can't be compared to a SemVer style one.

			if (v2.StartsWith("<="))
			{
				v2 = TrimVersionPrefix(v2.Substring(2));

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2) <= 0 ? 1L : 0L;
				}
				catch (Exception)
				{
				}
			}
			else if (v2.StartsWith('<'))
			{
				v2 = TrimVersionPrefix(v2.Substring(1));

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2) < 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}
			}
			else if (v2.StartsWith(">="))
			{
				v2 = TrimVersionPrefix(v2.Substring(2));

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2) >= 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}
			}
			else if (v2.StartsWith('>'))
			{
				v2 = TrimVersionPrefix(v2.Substring(1));

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2) > 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}
			}
			else if (v2.StartsWith('='))
			{
				v2 = TrimVersionPrefix(v2.Substring(1));

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2) == 0 ? 1L : 0L;
				}
				catch (Exception e)
				{
					ex = e;
				}
			}
			else
			{
				v2 = TrimVersionPrefix(v2);

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
					var csV1 = new Version(NormalizeSystemVersion(v1));
					var csV2 = new Version(NormalizeSystemVersion(v2));
					return csV1.CompareTo(csV2);
				}
				catch (Exception e)
				{
					ex = e;
				}
			}

			if (TheScript == null)
				throw ex;

			return (long)Errors.ErrorOccurred($"Error comparing version {versionA} to {versionB}: {ex?.Message ?? "Unspecified error"}", DefaultErrorLong);
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
		/// Returns whether a character is a space or a tab.
		/// </summary>
		/// <param name="c">The character to examine.</param>
		/// <returns>True if the character was a space or a tab.</returns>
		internal static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

		/// <summary>
		/// An internal optimized version of StrCompare().
		/// </summary>
		internal static int StrCmp(string left, string right, bool caseSensitive) => string.Compare(left, right, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

		// As AHK's ATOF, by which Sort's N option and numeric options such as KeyWait's T read a number: the number
		// text starts with after any whitespace, read as hexadecimal after 0x, or 0 when it starts with none.
		internal static double Atof(ReadOnlySpan<char> s)
		{
			s = s.TrimStart(" \t\n\v\f\r");
			var i = s.Length != 0 && s[0] is '+' or '-' ? 1 : 0;

			if (s.Length > i + 2 && s[i] == '0' && (s[i + 1] | 0x20) == 'x' && char.IsAsciiHexDigit(s[i + 2]))
			{
				// Past 64 bits the value wraps, as AHK's does.
				var hex = 0L;

				for (var j = i + 2; j < s.Length && char.IsAsciiHexDigit(s[j]); j++)
					hex = (hex << 4) | (long)(char.IsAsciiDigit(s[j]) ? s[j] - '0' : (s[j] | 0x20) - 'a' + 10);

				return s[0] == '-' ? -hex : hex;
			}

			var end = i;

			while (end < s.Length && char.IsAsciiDigit(s[end]))
				end++;

			var digits = end - i;

			if (end < s.Length && s[end] == '.')
			{
				var fraction = ++end;

				while (end < s.Length && char.IsAsciiDigit(s[end]))
					end++;

				digits += end - fraction;
			}

			if (digits == 0)
				return 0;

			if (end < s.Length && (s[end] | 0x20) == 'e')
			{
				var exponent = end + 1;

				if (exponent < s.Length && s[exponent] is '+' or '-')
					exponent++;

				if (exponent < s.Length && char.IsAsciiDigit(s[exponent]))
				{
					end = exponent;

					while (end < s.Length && char.IsAsciiDigit(s[end]))
						end++;
				}
			}

			return double.Parse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		// As AHK's ATOI for option counts such as Send's {Key N}: Atof's number without its fraction, which is exact up to
		// 2^53 and saturates beyond the range of a long, where ATOI wraps.
		internal static long Atoi(ReadOnlySpan<char> s) => (long)Atof(s);

		// FormatTime's picture as a .NET custom format: as in AHK, only the date and time specifiers mean anything, text in
		// single quotes is literal with '' a quote, and five or more y read as yyyy. Every other character is escaped, and
		// an empty quoted section is kept, since it separates the specifiers around it.
		private static string ToCustomTimeFormat(string picture)
		{
			var sb = new StringBuilder(picture.Length * 2);

			for (var i = 0; i < picture.Length; i++)
			{
				var c = picture[i];

				if (c == '\'')
				{
					var start = sb.Length;

					for (i++; i < picture.Length; i++)
					{
						if (picture[i] == '\'')
						{
							if (i + 1 == picture.Length || picture[i + 1] != '\'')
								break;

							i++;
						}

						_ = sb.Append('\\').Append(picture[i]);
					}

					if (sb.Length == start)
						_ = sb.Append("''");
				}
				else if (c == 'y')
				{
					var run = 1;

					for (; i + 1 < picture.Length && picture[i + 1] == 'y'; i++)
						run++;

					_ = sb.Append('y', Math.Min(run, 4));
				}
				else if (c is 'd' or 'M' or 'g' or 'h' or 'H' or 'm' or 's' or 't')
					_ = sb.Append(c);
				else
					_ = sb.Append('\\').Append(c);
			}

			return sb.ToString();
		}

		/// <summary>
		/// The bit pattern of a Format() argument read as unsigned, for the conversions that have no sign:
		/// u, o, x, X and p. A negative value is its two's complement, the way AutoHotkey formats one, so
		/// <c>Format("{:X}", -1)</c> is FFFFFFFFFFFFFFFF and a handle whose high bit is set keeps all 64 bits.
		/// </summary>
		private static ulong ToUnsignedBits(object arg)
		{
			_ = arg.TryCoerceLong(out var l);
			return unchecked((ulong)l);
		}

		/// <summary>
		/// Whether a conversion takes a number, which is every one of them except s.
		/// </summary>
		private static bool IsNumericSpec(char type) =>
		type is 'd' or 'i' or 'u' or 'o' or 'x' or 'X' or 'e' or 'E' or 'f' or 'F' or 'g' or 'G' or 'a' or 'A' or 'c' or 'C' or 'p' or 'P';

		/// <summary>
		/// Whether a conversion takes a floating-point number rather than an integer.
		/// </summary>
		private static bool IsFloatSpec(char type) => type is 'e' or 'E' or 'f' or 'F' or 'g' or 'G' or 'a' or 'A';

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
		/// Converts a Format() argument to what its conversion takes: a number for a numeric one, which AutoHotkey
		/// rejects rather than substituting a value the script did not supply, and text for s. Coercing it here also
		/// spares each case of <see cref="FormatArgument"/> a second parse. False once the script continued the error.
		/// </summary>
		private static bool TryCoerceArgument(object arg, char type, out object value)
		{
			value = arg;

			if (!IsNumericSpec(type))
			{
				if (!arg.CoerceString(out var text))
					return false;

				value = text;
			}
			else if (IsFloatSpec(type))
			{
				if (arg is not double)
				{
					if (!arg.CoerceDouble(out var d))
						return false;

					value = d;
				}
			}
			else if (arg is not long)
			{
				if (!arg.CoerceLong(out var l))
					return false;

				value = l;
			}

			return true;
		}

		/// <summary>
		/// Formats one argument, as <see cref="TryCoerceArgument"/> converted it, according to the given SpecInfo.
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
					var unum = ToUnsignedBits(arg);

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
					var hexnum = ToUnsignedBits(arg);

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
					var onum = ToUnsignedBits(arg);

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
							charStr = new string(spec.ZeroPad ? '0' : ' ', pad) + charStr;
					}

					return charStr;
				}

				// Pointer – format the numeric value as a pointer in hexadecimal.
				case 'p':
				case 'P':
				{
					var ptrVal = ToUnsignedBits(arg);

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
					var s = (string)arg;

					// If a precision is given, use it as the maximum number of characters.
					if (spec.Precision.HasValue && s.Length > spec.Precision.Value)
						s = s.Substring(0, spec.Precision.Value);

					if (spec.Width.HasValue && s.Length < spec.Width.Value)
					{
						int pad = spec.Width.Value - s.Length;

						// AHK formats through MSVC's printf, whose %0Ns pads with zeros, so {:02} turns 9 into "09".
						if (spec.LeftAlign)
							s += new string(' ', pad);
						else
							s = new string(spec.ZeroPad ? '0' : ' ', pad) + s;
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
