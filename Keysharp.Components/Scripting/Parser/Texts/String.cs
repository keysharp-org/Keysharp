using Keysharp.Builtins;

namespace Keysharp.Parsing
{
    internal partial class Parser
	{
		internal static string EscapedString(string code, bool resolve) => EscapedString(code.AsSpan(), resolve);

		internal static string EscapedString(ReadOnlySpan<char> code, bool resolve)
		{
			if (code.Length == 0)
				return DefaultObject;

			var sb = new StringBuilder(code.Length + 32);
			var escaped = false;

			foreach (var sym in code)
			{
				if (escaped)
				{

					_ = sym switch
					{
						'n' => sb.Append('\n'),
						'r' => sb.Append('\r'),
						'b' => sb.Append('\b'),
						't' => sb.Append('\t'),
						'v' => sb.Append('\v'),
						'a' => sb.Append('\a'),
						'f' => sb.Append('\f'),
						's' => sb.Append(' '),
						'0' => sb.Append('\0'),
						//case '"': _ = buffer.Append('"'); break;
						//
						//case '\'': _ = buffer.Append('\''); break;
						//
						//case ';': _ = buffer.Append(';'); break;
						//
						//case ':': _ = buffer.Append(':'); break;
						//
						//case '{': _ = buffer.Append('{'); break;
						_ => sb.Append(sym),//if (sym == Resolve)//This was likely here to parse legacy style syntax, but makes it impossible to send "'%", so we omit it.
						//_ = buffer.Append(Escape);
						};

					escaped = false;
				}
				else if (sym == Escape)
					escaped = true;
				else
					_ = sb.Append(sym);
			}

			if (escaped)
				_ = sb.Append(Escape);

			return sb.ToString();
		}

		/// <summary>
		/// The options written to the right of a continuation section's opening <c>(</c>. Shared by the two places
		/// that honour them: the lexer, which merges a *code* section's lines inline, and <see cref="MultilineString"/>,
		/// which joins a section that opened inside a quoted string.
		/// </summary>
		internal sealed class ContinuationOptions
		{
			/// <summary>Text placed between content lines — a linefeed unless a <c>Join</c> option said otherwise.</summary>
			internal string Join = DefaultNewLine;
			/// <summary>null means AHK's "smart" trim: strip the first content line's indentation from every line.</summary>
			internal bool? LTrim;
			internal bool RTrim = true;
			internal bool Comments;
			internal bool LiteralEscape;
			/// <summary>Legacy <c>%</c> option: keep percent signs literal instead of resolving them.</summary>
			internal bool PercentLiteral;
		}

		// AHK stores the Join string in a TCHAR[16], so anything past 15 characters is dropped.
		private const int MaxJoinLength = 15;

		/// <summary>
		/// Parses the option text to the right of a continuation section's <c>(</c> (the <c>(</c> itself excluded).
		/// </summary>
		internal static ContinuationOptions ParseContinuationOptions(string optionText, int lineNumber, string code, string name)
		{
			var opts = new ContinuationOptions();

			if (string.IsNullOrEmpty(optionText))
				return opts;

			if (optionText.Contains('%'))
			{
				opts.PercentLiteral = true;
				optionText = optionText.Replace("%", string.Empty);
			}

			var span = optionText.AsSpan().Trim();

			foreach (Range r in span.SplitAny(SpacesSv))
			{
				var option = span[r];

				if (option.IsEmpty)
					continue;

				if (option.StartsWith(Keyword_Join, StringComparison.OrdinalIgnoreCase))
				{
					// `Join` on its own joins the lines with nothing at all. Not routed through EscapedString: that
					// answers an empty span with the script's default value, which is null under v2.1 compatibility.
					var rest = option.Slice(Keyword_Join.Length);
					var join = rest.IsEmpty ? "" : EscapedString(rest, false);
					opts.Join = join.Length > MaxJoinLength ? join.Substring(0, MaxJoinLength) : join;
					continue;
				}

				switch (option)
				{
					case var _ when option.Equals("ltrim", StringComparison.OrdinalIgnoreCase):
						opts.LTrim = true;
						break;

					case var _ when option.Equals("ltrim0", StringComparison.OrdinalIgnoreCase):
						opts.LTrim = false;
						break;

					case var _ when option.Equals("rtrim", StringComparison.OrdinalIgnoreCase):
						opts.RTrim = true;
						break;

					case var _ when option.Equals("rtrim0", StringComparison.OrdinalIgnoreCase):
						opts.RTrim = false;
						break;

					// Any prefix of "Comments" is accepted, as in AHK; the documented spellings are C, Com, Comment, Comments.
					case var _ when option.Length <= 8 && "comments".AsSpan(0, option.Length).Equals(option, StringComparison.OrdinalIgnoreCase):
						opts.Comments = true;
						break;

					case var _ when option.Length == 1 && option[0] == Escape:
						opts.LiteralEscape = true;
						break;

					case var _ when option[0] == ';':
						return opts;   // a trailing comment ends the option list

					default:
						throw new ParseException(ExMultiStr, lineNumber, code, name);
				}
			}

			return opts;
		}

		internal static string MultilineString(string code, int lineNumber, string name)
		{
			var reader = new StringReader(code);
			string line = null;

			while ((line = reader.ReadLine()) != null)
			{
				var trimmed = line.AsSpan().Trim();

				if (trimmed.IsEmpty)
					continue;

				line = trimmed.ToString();
				break;
			}

			if (line == null || line.Length < 1 || line[0] != ParenOpen)
				throw new ParseException("Multiline string must start with '(' after optional leading blank lines.", lineNumber, code, name);

			var opts = ParseContinuationOptions(line.Substring(1), lineNumber, code, name);
			var join = opts.Join;
			var ltrim = opts.LTrim;
			bool rtrim = opts.RTrim, stripComments = opts.Comments, percentResolve = !opts.PercentLiteral, literalEscape = opts.LiteralEscape;
			var sb = new StringBuilder(code.Length);
			var resolve = Resolve.ToString();
			var escape = Escape.ToString();
			var cast = Multicast.ToString();
			var resolveEscaped = string.Concat(escape, resolve);
			var escapeEscaped = new string(Escape, 2);
			var castEscaped = string.Concat(escape, cast);
			// Track default indent from first content line
			string indentSample = null;
			bool firstLine = true;

			while ((line = reader.ReadLine()) != null)
			{
				var check = line.Trim();

				if (check.Length > 0 && check[0] == ParenClose)
					break;

				// A comment is removed before any trimming, and takes ALL the whitespace to its left with it — that much
				// happens whether or not RTrim is on, so it cannot be left to the trimming below.
				if (stripComments)
				{
					if (check.Length > 0 && check[0] == ';')
						continue;   // a comment on a line of its own contributes nothing at all, not even a blank line

					line = StripCommentSingle(line, out var stripped);

					if (stripped)
						line = line.TrimEnd(Spaces);
				}

				// On first content line, capture indent sample if trimming
				if (firstLine)
				{
					firstLine = false;

					if (!ltrim.HasValue)
					{
						// Capture only the first run of identical indent characters (space or tab)
						if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
						{
							char indentChar = line[0];
							int count = 1;

							while (count < line.Length && line[count] == indentChar)
								count++;

							indentSample = new string(indentChar, count);
						}
					}
				}

				if (!ltrim.HasValue && !string.IsNullOrEmpty(indentSample) && line.StartsWith(indentSample))
				{
					line = line.Substring(indentSample.Length);
				}

				if (ltrim.HasValue && ltrim.Value)
				{
					if (rtrim)
						line = line.Trim(Spaces);
					else
						line = line.TrimStart(Spaces);
				}
				else if (rtrim)
					line = line.TrimEnd(Spaces);

				if (!percentResolve)
					line = line.Replace(resolve, resolveEscaped);

				if (literalEscape)
					line = line.Replace(escape, escapeEscaped);

				line = line.Replace("\"", Escape + "\"");//Can't use interpolated string here because the AStyle formatter misinterprets it.
				line = line.Replace(cast, castEscaped);
				_ = sb.Append(line);
				_ = sb.Append(join);
			}

			if (sb.Length == 0)
				return DefaultObject;

			_ = sb.Remove(sb.Length - join.Length, join.Length);
			return sb.ToString();
		}

		private int FindNextBalanced(string s, char ch1, char ch2)
		{
			var level = 0;
			var escape = false;
			var inquote = false;

			for (int i = 0; i < s.Length; i++)
			{
				char ch = s[i];

				if (ch == '\"')
				{
					if (!inquote)
					{
						if (i == 0 || s[i - 1] != Escape)
							inquote = true;
					}
					else
					{
						if (i == 0 || s[i - 1] != Escape || !escape)//Checking escape accounts for ``.
							inquote = false;
					}
				}

				escape = ch == Escape ? !escape : false;

				if (!inquote)
				{
					if (ch == ch1)
					{
						level++;
					}
					else if (ch == ch2)
					{
						level--;

						if (level == 0)
							return i;
					}
				}
			}

			return -1;
		}

		private void RemoveExcessParentheses(CodeLine codeLine, List<object> parts)
		{
			while (parts.Count > 1)
			{
				var level = 0;
				var last = parts.Count - 1;

				if (!(--last > 1 &&
						parts[0] is string s0 && s0.Length == 1 && s0[0] == ParenOpen &&
						parts[last] is string sl && sl.Length == 1 && sl[0] == ParenClose))
					return;

				for (var i = 0; i < last; i++)
				{
					var check = parts[i] as string;

					if (string.IsNullOrEmpty(check))
						continue;

					switch (check[check.Length - 1])
					{
						case ParenOpen:
							level++;
							break;

						case ParenClose:
							if (check.Length != 1)
								break;
							else if (--level < 0)
								throw new ParseException(ExUnbalancedParens, codeLine.LineNumber, codeLine.Code, codeLine.FileName);

							break;
					}
				}

				if (level != 0)
					return;

				parts.RemoveAt(last);
				parts.RemoveAt(0);
			}
		}

		private List<string> SplitStringBalanced(string s, char delim, bool addEmpty = false)
		{
			var escape = false;
			var inquote = false;
			var parenLevel = 0;
			var braceLevel = 0;
			var bracketLevel = 0;
			var parts = new List<string>();
			var sb = new StringBuilder();

			for (int i = 0; i < s.Length; i++)
			{
				char ch = s[i];

				if (ch == '\"')
				{
					if (!inquote)
					{
						if (i == 0 || s[i - 1] != Escape)
							inquote = true;
					}
					else
					{
						if (i == 0 || s[i - 1] != Escape || !escape)//Checking escape accounts for ``.
							inquote = false;
					}
				}

				escape = ch == Escape ? !escape : false;

				if (ch == '(' && !inquote)//Either it's a ( or a function call which will end with a (.
				{
					parenLevel++;
					_ = sb.Append(ch);
				}
				else if (ch == ')' && !inquote)
				{
					if (parenLevel > 0)
						_ = sb.Append(ch);

					parenLevel--;
				}
				else if (ch == '{' && !inquote)
				{
					braceLevel++;
					_ = sb.Append(ch);
				}
				else if (ch == '}' && !inquote)
				{
					if (braceLevel > 0)
						_ = sb.Append(ch);

					braceLevel--;
				}
				else if (ch == '[' && !inquote)
				{
					_ = sb.Append(ch);
					bracketLevel++;
				}
				else if (ch == ']' && !inquote)
				{
					if (bracketLevel > 0)
						_ = sb.Append(ch);

					bracketLevel--;
				}
				else if (parenLevel == 0 && braceLevel == 0 && bracketLevel == 0 && ch == delim && !inquote)//Assuming delim is != to any of the above characters.
				{
					//parts.Add(EscapedString(sb.ToString(), false));
					parts.Add(sb.ToString());
					_ = sb.Clear();
				}
				else
					_ = sb.Append(ch);
			}

			if (sb.Length > 0 || (addEmpty && s.Length > 0 && s[s.Length - 1] == delim))
			{
				//parts.Add(EscapedString(sb.ToString(), false));
				parts.Add(sb.ToString());
			}

			return parts;
		}

		//private string Replace(string input, string search, string replace)
		//{
		//  var sb = new StringBuilder(input.Length);
		//  int z = 0, n = 0, l = search.Length;
		//
		//  while (z < input.Length && (z = input.IndexOf(search, z, System.StringComparison.OrdinalIgnoreCase)) != -1)
		//  {
		//      if (n < z)
		//          _ = sb.Append(input, n, z - n);
		//
		//      _ = sb.Append(replace);
		//      z += l;
		//      n = z;
		//  }
		//
		//  if (n < input.Length)
		//      _ = sb.Append(input, n, input.Length - n);
		//
		//  return sb.ToString();
		//}
	}
}
