using Keysharp.Builtins;
namespace Keysharp.Internals.Strings
{
	internal class RegexHolder
	{
		internal PcreRegex regex;
		internal string tag = "";
		internal string[] groupNames;
		internal bool hasCallout;//Whether the pattern uses callouts; if not, matching can skip the callback path.

		internal RegexHolder(string n)
		{
			var optionsLength = Conversions.RegexOptionsLength(n);
			// The A option is PCRE's anchored option and nothing more: it anchors the match at the starting
			// position, where a \A in the pattern would anchor it at the start of the haystack.
			var settings = Conversions.ToRegexOptions(n.AsSpan(0, optionsLength > 0 ? optionsLength - 1 : 0));
			settings.Options |= PcreOptions.Compiled;
			var pattern = n.Substring(optionsLength);

			regex = Compile(pattern, settings);
			var info = regex.PatternInfo;
			//Whether matching needs the (slower) callout callback path. Taken from the compiled pattern's callout
			//list (authoritative — covers numeric, named and translated callouts) plus the auto-callout option.
			hasCallout = (settings.Options & PcreOptions.AutoCallout) != 0 || info.Callouts.Any();
			groupNames = new string[info.CaptureCount + 1];

			foreach (var name in info.GroupNames)
			{
				foreach (var i in info.GetGroupIndexesByName(name))
					groupNames[i] = name;
			}

			for (int i = 0; i < groupNames.Length; i++)
				groupNames[i] ??= "";
		}

		// Compiles as-is first. AutoHotkey syntax PCRE2 rejects is rewritten only once compilation has thrown, so neither
		// rewriter sees a normal pattern: unquoted callout names, (?CName) and (?Cn:Name), become PCRE2's string form, and
		// a hyphen PCRE1 reads as literal is escaped. PCRE2 reports one error at a time, so the second can surface only
		// after the first is rewritten.
		private static PcreRegex Compile(string pattern, PcreRegexSettings settings)
		{
			bool calloutsTranslated = false, hyphensEscaped = false;

			while (true)
			{
				try
				{
					return new PcreRegex(pattern, settings);
				}
				catch (PcrePatternException) when (!calloutsTranslated && pattern.Contains("(?C", StringComparison.Ordinal))
				{
					calloutsTranslated = true;
					pattern = TranslateCallouts(pattern);
				}
				catch (PcrePatternException e) when (!hyphensEscaped && e.ErrorCode == PcreErrorCode.ClassInvalidRange)
				{
					hyphensEscaped = true;
					pattern = EscapeClassEscapeHyphens(pattern);
				}
			}
		}

		/// <summary>
		/// AutoHotkey's PCRE1 reads a hyphen beside a class escape or a POSIX class in a character class, as in
		/// <c>[\w-.]</c> or <c>[.-\d]</c>, as a literal hyphen, where PCRE2 rejects it as an invalid range. This escapes
		/// such hyphens so the pattern compiles as it does in AutoHotkey.
		/// </summary>
		internal static string EscapeClassEscapeHyphens(string pattern)
		{
			var sb = new StringBuilder(pattern.Length + 4);
			var inClass = false;
			var afterClassItem = false;   // the previous item in the class was a class escape or a POSIX class

			for (var i = 0; i < pattern.Length; i++)
			{
				var c = pattern[i];

				if (c == '\\' && i + 1 < pattern.Length)
				{
					var escape = pattern[i + 1];
					var end = i + 2;

					// \p{...} and \P{...} name a property class, and \pL a one-letter one.
					if (escape is 'p' or 'P' && end < pattern.Length)
						end = pattern[end] == '{' && pattern.IndexOf('}', end) is var close and >= 0 ? close + 1 : end + 1;

					_ = sb.Append(pattern, i, end - i);
					afterClassItem = inClass && IsClassEscape(escape);
					i = end - 1;
				}
				else if (!inClass)
				{
					_ = sb.Append(c);
					inClass = c == '[';

					// A ']' right after the opening '[' or '[^' is a literal member.
					if (inClass && i + 1 < pattern.Length && pattern[i + 1] == '^')
						_ = sb.Append(pattern[++i]);

					if (inClass && i + 1 < pattern.Length && pattern[i + 1] == ']')
						_ = sb.Append(pattern[++i]);
				}
				else if (c == '[' && i + 1 < pattern.Length && pattern[i + 1] == ':' && pattern.IndexOf(":]", i + 2, StringComparison.Ordinal) is var posixEnd and >= 0)
				{
					_ = sb.Append(pattern, i, posixEnd + 2 - i);
					afterClassItem = true;
					i = posixEnd + 1;
				}
				else if (c == ']')
				{
					_ = sb.Append(c);
					inClass = afterClassItem = false;
				}
				else if (c == '-' && i + 1 < pattern.Length && pattern[i + 1] != ']'
						 && (afterClassItem || pattern[i + 1] == '\\' && i + 2 < pattern.Length && IsClassEscape(pattern[i + 2])
							 || pattern[i + 1] == '[' && i + 2 < pattern.Length && pattern[i + 2] == ':'))
				{
					_ = sb.Append("\\-");
					afterClassItem = false;
				}
				else
				{
					_ = sb.Append(c);
					afterClassItem = false;
				}
			}

			return sb.ToString();

			static bool IsClassEscape(char c) => c is 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'h' or 'H' or 'v' or 'V' or 'p' or 'P';
		}

		// PCRE2 starting delimiters for a string callout, e.g. (?C'name'), (?C"name"), (?C{name}).
		private const string Pcre2CalloutDelimiters = "`'\"^%#${";

		/// <summary>
		/// AutoHotkey (patched PCRE1) accepts an unquoted callout name — (?CName), (?C123Func) or (?Cn:Name) —
		/// which it stores as the callout function name. Stock PCRE2 (PCRE.NET) only accepts a numeric callout
		/// (?Cn) or a delimited string callout (?C'name'). This rewrites the AHK forms to the PCRE2 string form
		/// so existing scripts compile; the name then arrives as PcreCallout.String, which the callout handler
		/// already resolves to a function. Numeric callouts and already-delimited (?C'...') callouts are left
		/// untouched, and escapes, character classes and (?#...) comments are skipped so literal "(?C" is not
		/// rewritten. Note: the rewritten pattern is only what PCRE2 compiles; the script still sees the
		/// original needle as NeedleRegEx.
		/// </summary>
		internal static string TranslateCallouts(string pattern)
		{
			if (pattern is null || pattern.IndexOf("(?C", StringComparison.Ordinal) < 0)
				return pattern;

			var sb = new StringBuilder(pattern.Length + 8);
			int i = 0, len = pattern.Length;
			var inClass = false;

			while (i < len)
			{
				var c = pattern[i];

				if (c == '\\')//Escaped char: copy it and the next verbatim.
				{
					sb.Append(c);

					if (i + 1 < len)
						sb.Append(pattern[i + 1]);

					i += 2;
				}
				else if (inClass)
				{
					sb.Append(c);

					if (c == ']')
						inClass = false;

					i++;
				}
				else if (c == '[')
				{
					inClass = true;
					sb.Append(c);
					i++;
				}
				else if (c == '(' && i + 2 < len && pattern[i + 1] == '?' && pattern[i + 2] == '#')//(?#...) comment.
				{
					var j = i + 3;

					while (j < len && pattern[j] != ')')
						j += pattern[j] == '\\' && j + 1 < len ? 2 : 1;

					if (j < len)
						j++;

					sb.Append(pattern, i, j - i);
					i = j;
				}
				else if (c == '(' && i + 2 < len && pattern[i + 1] == '?' && pattern[i + 2] == 'C')//(?C...) callout.
				{
					i = AppendCallout(sb, pattern, i, len);
				}
				else
				{
					sb.Append(c);
					i++;
				}
			}

			return sb.ToString();
		}

		// Handles a single (?C...) starting at 'start'; appends the (possibly rewritten) callout and returns
		// the index just past it.
		private static int AppendCallout(StringBuilder sb, string pattern, int start, int len)
		{
			var p = start + 3;//Past "(?C".

			while (p < len && pattern[p] >= '0' && pattern[p] <= '9')
				p++;

			if (p < len && pattern[p] == ')')//(?C) or (?Cn): numeric callout, already valid PCRE2.
			{
				sb.Append(pattern, start, p + 1 - start);
				return p + 1;
			}

			if (p < len && Pcre2CalloutDelimiters.IndexOf(pattern[p]) >= 0)//Already a PCRE2 string callout.
			{
				var end = SkipPcre2String(pattern, p, len);

				if (end < 0)//Malformed; leave the rest for PCRE2 to reject.
				{
					sb.Append(pattern, start, len - start);
					return len;
				}

				sb.Append(pattern, start, end - start);
				return end;
			}

			//AHK unquoted name. A ':' right after the (optional) digits separates a number from the name
			//(the number is dropped, since PCRE2 string callouts are always number 0); otherwise the whole
			//tail after "(?C" is the name (e.g. (?C123Func) -> name "123Func").
			var nameStart = p < len && pattern[p] == ':' ? p + 1 : start + 3;
			var nameEnd = nameStart;

			while (nameEnd < len && pattern[nameEnd] != ')')
				nameEnd++;

			if (nameEnd >= len)//No closing ')'; leave the rest for PCRE2 to reject.
			{
				sb.Append(pattern, start, len - start);
				return len;
			}

			sb.Append("(?C'");

			foreach (var ch in pattern.AsSpan(nameStart, nameEnd - nameStart))
			{
				sb.Append(ch);

				if (ch == '\'')//Double single-quotes to escape one inside a '-delimited PCRE2 string.
					sb.Append('\'');
			}

			sb.Append("')");
			return nameEnd + 1;
		}

		// Given 'delimPos' at the opening delimiter of a PCRE2 string callout, returns the index just past the
		// closing ")", or -1 if malformed.
		private static int SkipPcre2String(string pattern, int delimPos, int len)
		{
			var open = pattern[delimPos];
			var close = open == '{' ? '}' : open;
			var q = delimPos + 1;

			while (q < len)
			{
				if (pattern[q] == close)
				{
					if (close != '}' && q + 1 < len && pattern[q + 1] == close)//Doubled delimiter = literal.
					{
						q += 2;
						continue;
					}

					q++;//Past the closing delimiter.
					return q < len && pattern[q] == ')' ? q + 1 : -1;
				}

				q++;
			}

			return -1;
		}
	}
}
