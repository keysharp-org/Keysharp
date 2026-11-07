namespace Keysharp.Builtins
{
	public partial class Ks : Module
	{
		/// <summary>
		/// The .NET flavours of Format, RegExMatch and RegExReplace, named as the global functions are so that a named
		/// import replaces one in the module or function holding it.
		/// </summary>
		public class DotNet : Module
		{
			private static readonly object[] nullPlaceholder = [null];

			public DotNet(params object[] args) : base(args) { }

			// The compiled pattern, or null once a compile error has been raised and continued.
			private static RegexWithTag CachedRegex(string needle, bool reverse)
			{
				var data = Script.TheScript.RegExData;
				var regdkt = reverse ? data.regdktDotNetReverse : data.regdktDotNet;

				lock (data.locker)//KeyedCollection is not threadsafe, the way ConcurrentDictionary is, so we must lock. We use KeyedCollection because we need to preserve order to remove the first entry.
				{
					if (regdkt.TryGetValue(needle, out var exp))
						return exp;

					try
					{
						exp = Conversions.ParseRegExDotNet(needle, reverse);
					}
					catch (ArgumentException ex)
					{
						_ = Errors.ErrorOccurred("Regular expression compile error", null, ex.Message);
						return null;
					}

					exp.tag = needle;
					regdkt.Add(exp);

					while (regdkt.Count > 100)
						regdkt.RemoveAt(0);

					return exp;
				}
			}

			private static int StartIndex(int position, int length)
			{
				if (position >= 0)
					return Math.Min(Math.Max(0, position - 1), length);

				position = length + position + 1;
				return position < 0 ? length : position;
			}

			/// <summary>
			/// Formats a string using the same syntax used by string.Format(), except it uses 1-based indexing.
			/// This is made available for users who prefer standard C# style formatting over the AHK
			/// style used in the global Format().
			/// <see cref="https://learn.microsoft.com/en-us/dotnet/api/system.string.format"/>
			/// </summary>
			/// <param name="formatStr">The format string.</param>
			/// <param name="args">The arguments to pass to the format string.</param>
			/// <returns>The newly formatted string.</returns>
			/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if .NET rejects the format string.</exception>
			public static string Format(object formatStr, params object[] args)
			{
				if (!formatStr.CoerceString(out var format))
					return "";

				try
				{
					return string.Format(format, nullPlaceholder.Concat(args));
				}
				catch (FormatException ex)
				{
					return (string)Errors.ValueErrorOccurred(ex.Message, format, DefaultErrorString);
				}
			}

			/// <summary>
			/// Determines whether a string contains a pattern (regular expression).
			/// </summary>
			/// <param name="haystack">The string whose content is searched.</param>
			/// <param name="needleRegEx">The pattern to search for, which is a .NET regular expression.<br/>
			/// The pattern's options (if any) must be included at the beginning of the string followed by a close-parenthesis.<br/>
			/// For example, the pattern i)abc.*123 would turn on the case-insensitive option and search for "abc",<br/>
			/// followed by zero or more occurrences of any character, followed by "123".<br/>
			/// If there are no options, the ")" is optional; for example, )abc is equivalent to abc.
			/// </param>
			/// <param name="outputVar">If omitted, no output variable will be used.<br/>
			/// Otherwise, specify a reference to the output variable in which to store a match object,<br/>
			/// which can be used to retrieve the position, length and value of the overall match and of each captured subpattern, if any are present.
			/// </param>
			/// <param name="startingPos">
			/// If omitted, it defaults to 1 (the beginning of haystack).<br/>
			/// Otherwise, specify 2 to start at the second character, 3 to start at the third, and so on.<br/>
			/// If startingPos is beyond the length of haystack, the search starts at the empty string that lies at the end of haystack (which typically results in no match).<br/>
			/// Specify a negative startingPos to start at that position from the right.<br/>
			/// For example, -1 starts at the last character and -2 starts at the next-to-last character.<br/>
			/// If startingPos tries to go beyond the left end of haystack, all of Haystack is searched.<br/>
			/// A startingPos of 0 searches leftward from the beginning of haystack, so only a zero-width match can succeed.<br/>
			/// Regardless of the value of startingPos, the return value is always relative to the first character of haystack.<br/>
			/// For example, the position of "abc" in "123abc789" is always 4.
			/// </param>
			/// <returns>The position of the match, where 1 is the first character of haystack, or 0 if there is no match.</returns>
			/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
			public static long RegExMatch(object haystack, object needleRegEx, [ByRef] object outputVar = null, object startingPos = null)
			{
				outputVar ??= VarRef.Empty;

				if (!haystack.CoerceString(out var input) || !needleRegEx.CoerceString(out var n))
					return 0L;

				if (!startingPos.CoerceInt(out var index, 1))
					return 0L;

				if (CachedRegex(n, index < 1) is not { } exp)
					return DefaultErrorLong;

				try
				{
					var match = exp.Match(input, StartIndex(index, input.Length));
					long pos = match.Success ? match.Index + 1 : 0;
					Refs.SetValue(outputVar, pos > 0 ? new RegExMatchInfo(match) : DefaultObject);
					return pos;
				}
				catch (RegexMatchTimeoutException ex)
				{
					return (long)Errors.ErrorOccurred("Regular expression execution error", null, ex.Message, DefaultErrorLong);
				}
			}

			/// <summary>
			/// Replaces occurrences of a pattern (regular expression) inside a string.
			/// </summary>
			/// <param name="haystack">The string whose content is searched and replaced.</param>
			/// <param name="needleRegEx">The pattern to search for, which is a .NET regular expression.<br/>
			/// The pattern's options (if any) must be included at the beginning of the string followed by a close-parenthesis.<br/>
			/// For example, the pattern i)abc.*123 would turn on the case-insensitive option and search for "abc",<br/>
			/// followed by zero or more occurrences of any character, followed by "123".<br/>
			/// If there are no options, the ")" is optional; for example, )abc is equivalent to abc.<br/>
			/// Although needleRegEx cannot contain binary zero, the pattern \x00 can be used to match a binary zero within haystack.
			/// </param>
			/// <param name="replacement">
			/// If blank or omitted, NeedleRegEx will be replaced with blank (empty), meaning it will be omitted from the return value.<br/>
			/// Otherwise, specify the string to be substituted for each match, which may refer to captured groups through .NET's
			/// substitutions, such as $1.
			/// </param>
			/// <param name="outputVarCount">If omitted, the corresponding value will not be stored.<br/>
			/// Otherwise, specify a reference to the output variable in which to store the number of replacements that occurred (0 if none).
			/// </param>
			/// <param name="limit">If omitted, it defaults to -1, which replaces all occurrences of the pattern found in Haystack.<br/>
			/// Otherwise, specify the maximum number of replacements to allow.<br/>
			/// The part of Haystack to the right of the last replacement is left unchanged.
			/// </param>
			/// <param name="startingPos">
			/// If omitted, it defaults to 1 (the beginning of haystack).<br/>
			/// Otherwise, specify 2 to start at the second character, 3 to start at the third, and so on.<br/>
			/// If startingPos is beyond the length of Haystack, the search starts at the empty string that lies at the end of haystack (which typically results in no replacements).<br/>
			/// Specify a negative startingPos to start at that position from the right.<br/>
			/// For example, -1 starts at the last character and -2 starts at the next-to-last character.<br/>
			/// If startingPos tries to go beyond the left end of haystack, all of haystack is searched.<br/>
			/// A startingPos of 0 searches leftward from the beginning of haystack, so only a zero-width match can be replaced.<br/>
			/// Regardless of the value of startingPos, the return value is always a complete copy of haystack -- the only difference is that<br/>
			/// more of its left side might be unaltered compared to what would have happened with a startingPos of 1.
			/// </param>
			/// <returns>A version of haystack whose contents have been replaced by the operation. If no replacements are needed, haystack is returned unaltered.</returns>
			/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
			public static string RegExReplace(object haystack, object needleRegEx, object replacement = null, [ByRef] object outputVarCount = null, object limit = null, object startingPos = null)
			{
				if (!haystack.CoerceString(out var input) || !needleRegEx.CoerceString(out var needle) || !replacement.CoerceString(out var replace))
					return "";

				outputVarCount ??= VarRef.Empty;

				if (!limit.CoerceInt(out var l, -1) || !startingPos.CoerceInt(out var index, 1))
					return "";

				if (CachedRegex(needle, index < 1) is not { } exp)
					return DefaultErrorString;

				var n = 0;

				// As in the global RegExReplace, a negative limit replaces every match and 0 replaces none, which is also what
				// .NET's Replace makes of -1 and 0.
				if (l < 0)
					l = -1;

				string match(Match hit)
				{
					n++;
					return hit.Result(replace);
				}

				try
				{
					var result = exp.Replace(input, match, l, StartIndex(index, input.Length));
					Refs.SetValue(outputVarCount, (long)n);
					return result;
				}
				catch (RegexMatchTimeoutException ex)
				{
					return (string)Errors.ErrorOccurred("Regular expression execution error", null, ex.Message, DefaultErrorString);
				}
			}
		}
	}

	public static partial class RegEx
	{
		internal class RegexEntryDotNet : KeyedCollection<string, RegexWithTag>
		{
			protected override string GetKeyForItem(RegexWithTag item) => item.tag;
		}
	}
}
