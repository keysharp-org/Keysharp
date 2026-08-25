using Keysharp.Builtins;
namespace System
{
	/// <summary>
	/// Extension methods for the System.String class.
	/// </summary>
	public static class StringExtensions
	{
		/// <summary>
		/// Provides an easy way to iterate through the lines of a string without using as much memory as <see cref="string.Split"/>.<br/>
		/// Taken from https://stackoverflow.com/questions/1547476/easiest-way-to-split-a-string-on-newlines-in-net.
		/// </summary>
		/// <param name="str">The string whose lines will be traversed.</param>
		/// <returns>Each line one at a time as an element in an enumerable.</returns>
		public static IEnumerable<string> SplitLines(this string str)
		{
			if (string.IsNullOrEmpty(str))
				yield break;

			using (var reader = new StringReader(str))
			{
				string line;

				while ((line = reader.ReadLine()) != null)
					yield return line;
			}
		}

		/// <summary>
		/// Trims a given number of characters from a list from the beginning of a string.
		/// </summary>
		/// <param name="str">The string to trim.</param>
		/// <param name="any">The list of characters to trim.</param>
		/// <param name="n">The number of characters to trim.</param>
		/// <returns>The trimmed string.</returns>
		public static string TrimNofAnyFromStart(this string str, string any, int n)
		{
			var i = 0;
			var chars = any.ToCharArray();

			for (; i < n && i < str.Length; i++)
			{
				var pos = str.IndexOfAny(chars, i);

				if (pos != i)
					break;
			}

			return i == 0 ? str : str.Substring(i);
		}

		/// <summary>
		/// Determines if all characters within a string are hexadecimal.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <returns>True if all characters were hexadecimal, else false.</returns>
		internal static bool AllHex(this string str)
		{
			foreach (var ch in str)
				if (!ch.IsHex())
					return false;

			return true;
		}

#if !WINDOWS
		/// <summary>
		/// Runs a string as a Bash command.<br/>
		/// Gotten from: https://stackoverflow.com/questions/54325155/how-to-get-hard-disk-serial-number-compatible-with-linux-windows
		/// </summary>
		/// <param name="cmd">The Bash command to run</param>
		/// <param name="wait">Whether to wait for the program to finish before returning. Default: true.</param>
		/// <returns>The process exit code (0 on success).</returns>
		internal static int Bash(this string cmd, bool wait = true) => Bash(cmd, out _, wait);

		/// <summary>
		/// Runs a string as a Bash command and captures output.
		/// </summary>
		/// <param name="cmd">The Bash command to run.</param>
		/// <param name="output">Standard output from the command, if available.</param>
		/// <param name="wait">Whether to wait for the program to finish before returning. Default: true.</param>
		/// <returns>The process exit code (0 on success, -1 on launch/runtime failure).</returns>
		internal static int Bash(this string cmd, out string output, bool wait = true)
		{
			output = DefaultObject;

			try
			{
				var normalizedCmd = cmd.ReplaceLineEndings("\n");
				using var process = new Process()
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "bash",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true,
					}
				};

				process.StartInfo.ArgumentList.Add("-c");
				process.StartInfo.ArgumentList.Add(normalizedCmd);
				_ = process.Start();

				if (!wait)
					return 0;

				output = process.StandardOutput.ReadToEnd().Trim(CrLf);
				var err = process.StandardError.ReadToEnd().Trim(CrLf);
				process.WaitForExit();

				if (process.ExitCode != 0 && !err.IsNullOrEmpty())
					Diagnostics.Debug.WriteLine($"Bash failed with exit code {process.ExitCode}: {err}");

				return process.ExitCode;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Bash launch failed: {ex.Message}");
				return -1;
			}
		}

#endif

#if OSX
		/// <summary>
		/// Runs a string as an AppleScript command via osascript.
		/// </summary>
		/// <param name="script">The AppleScript to run.</param>
		/// <param name="wait">Whether to wait for completion before returning. Default: true.</param>
		/// <returns>The process exit code (0 on success).</returns>
		internal static int AppleScript(this string script, bool wait = true) => AppleScript(script, out _, wait);

		/// <summary>
		/// Runs a string as an AppleScript command via osascript.
		/// </summary>
		/// <param name="script">The AppleScript to run.</param>
		/// <param name="output">Standard output from osascript when wait is true, else empty.</param>
		/// <param name="wait">Whether to wait for completion before returning. Default: true.</param>
		/// <returns>The process exit code (0 on success, -1 on launch/runtime failure).</returns>
		internal static int AppleScript(this string script, out string output, bool wait = true)
		{
			output = DefaultObject;

			try
			{
				var normalizedScript = script.ReplaceLineEndings("\n");
				using var process = new Process()
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "/usr/bin/osascript",
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true,
					}
				};

				process.StartInfo.ArgumentList.Add("-e");
				process.StartInfo.ArgumentList.Add(normalizedScript);
				_ = process.Start();

				if (!wait)
					return 0;

				output = process.StandardOutput.ReadToEnd().Trim(CrLf);
				var err = process.StandardError.ReadToEnd().Trim(CrLf);
				process.WaitForExit();

				if (process.ExitCode != 0 && !err.IsNullOrEmpty())
					Diagnostics.Debug.WriteLine($"AppleScript failed with exit code {process.ExitCode}: {err}");

				return process.ExitCode;
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"AppleScript launch failed: {ex.Message}");
				return -1;
			}
		}
#endif

		/// <summary>
		/// String wrapper around <see cref="BeginNums"/>.
		/// </summary>
		internal static ReadOnlySpan<char> BeginNums(this string str, bool allowDecimal = false) =>
		BeginNums(str.AsSpan(), allowDecimal);

		/// <summary>
		/// Returns all characters that are digits, starting from the beginning of a string.
		/// </summary>
		/// <param name="str">The string to return the leading digits for.</param>
		/// <param name="allowDecimal">True to allow decimals, else false for integers only.</param>
		/// <returns>A string consisting only of digits, empty if none.</returns>
		internal static ReadOnlySpan<char> BeginNums(this ReadOnlySpan<char> str, bool allowDecimal = false)
		{
			var i = 0;

			while (i < str.Length && (char.IsDigit(str[i]) || (allowDecimal && str[i] == '.')))
				i++;

			return str.Slice(0, i);
		}

		/// <summary>
		/// Determines whether a string ends with any of a list of strings.<br/>
		/// The search is case insensitive.
		/// </summary>
		/// <param name="str">The string to search.</param>
		/// <param name="strings">The list of strings to search the end of the string for.</param>
		/// <returns>True if any of the strings in strings were found at the end of str.</returns>
		internal static int EndsWithAnyOf(this ReadOnlySpan<char> str, IEnumerable<string> strings)
		{
			foreach (var end in strings)
				if (str.EndsWith(end, StringComparison.OrdinalIgnoreCase))
					return end.Length;

			return -1;
		}

		/// <summary>
		/// Returns the index of the first occurrence of a substring in another string which is not inside quotes.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="s">The string to search for inside of str.</param>
		/// <returns>The index of the first occurrence of s inside of str outside of quotes, else -1 if not found.</returns>
		internal static int FindFirstNotInQuotes(this string str, string s)
		{
			var escape = false;
			var inquote = false;

			for (var i = 0; i < str.Length; i++)
			{
				var ch = str[i];

				if (ch == '\'')
				{
					if (!inquote)
					{
						if (i == 0 || str[i - 1] != '`')
							inquote = true;
					}
				}
				else if (ch == '\"')
				{
					if (!inquote)
					{
						if (i == 0 || str[i - 1] != '`')
							inquote = true;
					}
					else
					{
						if (i == 0 || str[i - 1] != '`' || !escape)//Checking escape accounts for ``.
							inquote = false;
					}
				}

				if (ch == Escape)
					escape = !escape;
				else
					escape = false;

				if (!inquote)
				{
					if (str.Substring(i).StartsWith(s, StringComparison.OrdinalIgnoreCase))
					{
						return i;
					}
				}
			}

			return -1;
		}

		//internal static int FindFirstNotOf(this string source, string chars, int offset = 0)
		//{
		//  if (source.Length == 0) return -1;

		//  if (chars.Length == 0) return -1;

		//  for (var i = offset; i < source.Length; i++)
		//      if (chars.IndexOf(source[i]) == -1)
		//          return i;

		//  return -1;
		//}

		/// <summary>
		/// String wrapper around <see cref="FindFirstNotOf"/>.
		/// </summary>
		internal static int FindFirstNotOf(this string str, char[] chars, int offset = 0) =>
		FindFirstNotOf(str.AsSpan(), chars, offset);

		/// <summary>
		/// Returns the index of the first character in a string which is not contained in a list of characters.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="chars">The list of characters to search the string for the absence of.</param>
		/// <param name="offset">The offset to begin searching at. Default: 0.</param>
		/// <returns>0 if str or chars are empty, else the index of the first character not contained in chars.
		/// If every character is contained in chars, then the str.Length is returned.
		/// </returns>
		internal static int FindFirstNotOf(this ReadOnlySpan<char> str, char[] chars, int offset = 0)
		{
			if (str.Length == 0) return 0;// -1;

			if (chars.Length == 0) return 0;// -1;

			for (var i = offset; i < str.Length; i++)
				if (!chars.Contains(str[i]))
					return i;

			return str.Length;// -1;
		}

		/// <summary>
		/// Returns the index of the first occurrence of a character that is not an valid identifier character (var, func, or obj.key name).
		/// This can only be used for hotkeys because it includes ';' which can be a hotkey trigger, but otherwise
		/// wouldn't be part of a valid variable name.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <returns>The index after an identifier is found.</returns>
		internal static int FindHotkeyIdentifierEnd(this string str)
		{
			var i = 0;

			for (; i < str.Length; i++)
				if (!str[i].IsIdentifierChar() && str[i] != ';')//Hotkey triggers can contain a ;
					return i;

			return i;
		}

		//internal static int FirstIndexOf(this string source, Func<char, bool> func, int offset = 0) =>
		//source.AsSpan().FirstIndexOf(func, offset);

		/// <summary>
		/// Returns the index of the first character in the string for which func returns true, optionally
		/// starting at a specified index.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="func">The Func to call for every character.</param>
		/// <param name="offset">The index to start searching at. Default: 0.</param>
		/// <returns>The first index for which func returns true, else -1.</returns>
		internal static int FirstIndexOf(this ReadOnlySpan<char> str, Func<char, bool> func, int offset = 0)
		{
			for (var i = offset; i < str.Length; i++)
				if (func(str[i]))
					return i;

			return -1;
		}

		/// <summary>
		/// Wrapper around searching for a character in a string starting at an offset.<br/>
		/// For some reason the built-in IndexOf() doesn't support searching from an offset.
		/// </summary>
		/// <param name="str">The string to search.</param>
		/// <param name="search">The character to search for.</param>
		/// <param name="offset">The offset to start searching at.</param>
		/// <returns>The index from the beginning of the string that search was found, else -1.</returns>
		internal static int IndexOf(this ReadOnlySpan<char> str, char search, int offset)
		{
			var val = str.Slice(offset).IndexOf(search);
			return val == -1 ? val : val + offset;
		}

		/// <summary>
		/// Wrapper around searching for a <see cref="SearchValues"/> in a string starting at an offset.<br/>
		/// For some reason the built-in IndexOf() doesn't support searching from an offset.
		/// </summary>
		/// <param name="str">The string to search.</param>
		/// <param name="search">The <see cref="SearchValues"/> to search for.</param>
		/// <param name="offset">The offset to start searching at.</param>
		/// <returns>The index from the beginning of the string that search was found, else -1.</returns>
		internal static int IndexOfAny(this ReadOnlySpan<char> str, SearchValues<char> search, int offset)
		{
			var val = str.Slice(offset).IndexOfAny(search);

			if (val == -1)
				return val;

			return val + offset;
		}

		/// <summary>
		/// Determines whether two characters occur in equal number within a string.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="ch1">The first character to search for.</param>
		/// <param name="ch2">The second character to search for.</param>
		/// <returns>True if ch1 and ch2 occur in str in equal number, else false.</returns>
		internal static bool IsBalanced(this string str, char ch1, char ch2)
		{
			int ct1 = 0, ct2 = 0;

			foreach (var ch in str)
				if (ch == ch1)
					ct1++;
				else if (ch == ch2)
					ct2++;

			return ct1 == ct2;
		}

		/// <summary>
		/// Reverse version of <see cref="NthIndexOf"/>.
		/// </summary>
		internal static int LastNthIndexOf(this string str, string substr, int pos, int n, StringComparison comp)
		{
			pos = str.Length + pos + 1;
			n = Math.Abs(n);

			do
			{
				pos = str.LastIndexOf(substr, pos - 1, comp);
			} while (--n > 0 && pos != -1);

			return pos;
		}

		/// <summary>
		/// Returns the index of the nth occurrence of a substring within a string, starting at a specified index.<br/>
		/// Gotten from https://stackoverflow.com/questions/186653/get-the-index-of-the-nth-occurrence-of-a-string
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="substr">The string to search for n occurrences of within str.</param>
		/// <param name="pos">The index in str to start searching for substr at.</param>
		/// <param name="n">The number of occurrences of substr to search for.</param>
		/// <param name="comp">The type of comparison to perform.</param>
		/// <returns>The position of the nth occurrence of substr within str, starting at pos. If n occurrences are not found, -1 is returned.</returns>
		internal static int NthIndexOf(this string str, string substr, int pos, int n, StringComparison comp)
		{
			pos--;

			do
			{
				pos = str.IndexOf(substr, pos + 1, comp);
			} while (--n > 0 && pos != -1);

			return pos;
		}

		/// <summary>
		/// Returns the index of the nth occurrence of a list of characters within a string, starting at a specified index.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="substr">The list of characters to compare each character in str against for n occurrences within str.</param>
		/// <param name="pos">The index in str to start searching for substr at.</param>
		/// <param name="n">The number of occurrences of substr to search for.</param>
		/// <returns>The position of the nth occurrence of any char within substr within str, starting at pos. If n occurrences are not found, -1 is returned.</returns>
		internal static int NthIndexOfAny(this string str, char[] substr, int pos, int n)
		{
			pos--;

			do
			{
				pos = str.IndexOfAny(substr, pos + 1);
			} while (--n > 0 && pos != -1);

			return pos;
		}

		//internal static bool OcurredInBalance(this string str, string s1, char ch1, char ch2)
		//{
		//  var b = 0;
		//  var index = str.IndexOf(s1);

		//  if (index == -1)
		//      return false;

		//  for (int i = 0; i < str.Length; i++)
		//  {
		//      char ch = str[i];

		//      if (ch == ch1)
		//          b++;
		//      else if (ch == ch2)
		//          b--;

		//      if (i == index)
		//          return b == 0;
		//  }

		//  return false;
		//}

		/// <summary>
		/// Removes the trailing spaces and tabs of a string starting after a specified index.
		/// </summary>
		/// <param name="str">The string to trim.</param>
		/// <param name="index">The index after which trimming will occur.</param>
		/// <returns>The trimmed string.</returns>
		internal static string OmitTrailingWhitespace(this string str, int index) => str.AsSpan(0, index).TrimEnd(SpaceTab).ToString();
		internal static ReadOnlySpan<char> OmitTrailingWhitespaceSpan(this string str, int index) => str.AsSpan(0, index).TrimEnd(SpaceTab);

		//internal static string RemoveAfter(this string str, string token)
		//{
		//  var index = str.IndexOf(token);
		//  return index > 0 ? str.Substring(0, index) : str;
		//}

		/// <summary>
		/// Read the span until a space, tab, or end is encountered.
		/// </summary>
		/// <param name="span">The span to read.</param>
		/// <returns>A string containing all characters read.</returns>
		internal static string ParseUntilSpace(this ReadOnlySpan<char> span)
		{
			var nextSpace = span.IndexOfAny(SpaceTab);

			if (nextSpace != -1)
				return span.Slice(0, nextSpace).ToString();
			else
				return span.ToString();
		}

		/// <summary>
		/// Removes all instances of a specified list of characters from a string.
		/// </summary>
		/// <param name="str">The string to remove characters from.</param>
		/// <param name="chars">The characters to remove from the string.</param>
		/// <returns>A new string with none of the characters in chars in it.</returns>
		internal static string RemoveAll(this string str, string chars)
		{
			var idx = 0;
			var buffer = new char[str.Length];

			foreach (var c in str)
				if (!chars.Contains(c))
					buffer[idx++] = c;

			return new string(buffer, 0, idx);
		}

		/// <summary>
		/// Removes all instances of a specified list of characters from a string.
		/// </summary>
		/// <param name="str">The string to remove characters from.</param>
		/// <param name="chars">The characters to remove from the string.</param>
		/// <returns>A new string with none of the characters in chars in it.</returns>
		internal static string RemoveAll(this string str, char[] chars)
		{
			var idx = 0;
			var buffer = new char[str.Length];

			foreach (var c in str)
				if (!chars.Contains(c))
					buffer[idx++] = c;

			return new string(buffer, 0, idx);
		}

		/// <summary>
		/// Search for the first instance of a substring within a string, and replace it with another string.<br/>
		/// Gotten from https://stackoverflow.com/questions/141045/how-do-i-replace-the-first-instance-of-a-string-in-net
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="search">The string to search str for.</param>
		/// <param name="replace">The string to replace search with.</param>
		/// <param name="comparison">The type of comparison to perform. Default: ordinal.</param>
		/// <returns>A new string consisting of the original with search replaced with replace, if search was found, else str.</returns>
		internal static string ReplaceFirst(this string str, string search, string replace, StringComparison comparison = StringComparison.Ordinal)
		{
			var pos = str.IndexOf(search, comparison);
			return pos < 0 ? str : str.Substring(0, pos) + replace + str.Substring(pos + search.Length);
		}

		/// <summary>
		/// Determines whether a string starts with any of a list of strings and returns the length of the first match if found, else -1.<br/>
		/// The search is case insensitive.
		/// </summary>
		/// <param name="str">The string to examine.</param>
		/// <param name="strings">The list of strings to check if str starts with.</param>
		/// <returns>The length of the first matched string, else -1.</returns>
		internal static int StartsWithAnyOf(this ReadOnlySpan<char> str, IEnumerable<string> strings)
		{
			foreach (var start in strings)
				if (str.StartsWith(start, StringComparison.OrdinalIgnoreCase))
					return start.Length;

			return -1;
		}

		/// <summary>
		/// Trims all alpha characters from the end of a string.
		/// </summary>
		/// <param name="str">The string to trim.</param>
		/// <returns>The trimmed string.</returns>
		internal static ReadOnlySpan<char> TrimEndAlpha(this string str)
		{
			var len = str.Length;

			while (len > 0 && char.IsLetter(str[len - 1]))
				len--;

			return str.AsSpan(0, len);
		}

		/// <summary>
		/// Trims a string from the end of another string.
		/// </summary>
		/// <param name="str">The string to trim the end of.</param>
		/// <param name="trim">The string to trim from the end of str.</param>
		/// <param name="ignoreCase">True to ignore case, else false.</param>
		/// <returns>The trimmed string.</returns>
		internal static string TrimEndOf(this string str, string trim, bool ignoreCase = true) =>
		str.EndsWith(trim, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
		? str.Substring(0, str.LastIndexOf(trim))
		: str;

		//internal static string TrimStartOf(this string str, string trim, bool ignoreCase = true) =>
		//str.StartsWith(trim, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
		//? str.Substring(trim.Length)
		//: str;

		/// <summary>
		/// Trim a string from the start of a string if it exists, else return
		/// the original string.
		/// </summary>
		/// <param name="str">The string to trim from.</param>
		/// <param name="s">The string to trim from the start.</param>
		/// <returns>The trimmed string if the search string was found, else the original string.</returns>
		internal static string TrimStartOf(this string str, string s) =>
		str.StartsWith(s) ? str.Substring(s.Length) : str;

		/// <summary>
		/// Truncates a string to a maximum length.
		/// </summary>
		/// <param name="str">The string to truncate.</param>
		/// <param name="s">The maximum length of the output string.</param>
		/// <returns>The truncated string.</returns>
		internal static string Truncate(this string str, int limit) =>
		str.Length <= limit ? str : str.Substring(0, limit);
	}
}
