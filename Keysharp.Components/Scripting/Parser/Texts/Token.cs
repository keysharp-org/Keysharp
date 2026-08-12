using Keysharp.Builtins;

namespace Keysharp.Parsing
{
    internal partial class Parser
	{
		internal static bool IsAssignment(string code, bool checkexprassign = false)
		{
			var i = 0;

			while (i < code.Length && (IsIdentifier(code[i]) || code[i] == Resolve)) i++;

			if (i == 0 || i == code.Length)
				return false;

			while (i < code.Length && IsSpace(code[i])) i++;

			if (i < code.Length && code[i] == Equal)
				return true;

			//The statement above only checks for simple =, not :=. For some reason, checking for := completely throws off other parts of the parsing logic.
			//My hunch is that := is intended to be treated as an expression, not an assignment.
			//So it's better to leave this out for now, and figure out what would be ideal in the future if needed.
			//if (checkexprassign)
			//  if (i < code.Length - 1 && (code[i] == AssignPre && code[i + 1] == Equal))
			//      return true;
			return false;
		}

		internal static bool IsAssignOp(string code)
		{
			if (!(code.Length >= 2 && code.Length <= 4))
				return false;

			if (code[0] == Equal || code[^1] != Equal)
				return false;

			if (code.Length == 3)
			{
				if (code[0] == code[1])
				{

					return code[0] switch
				{
					//>>=
					Keywords.Greater or Keywords.Less or Keywords.Divide or Keywords.TernaryA => true,
						_ => false,
				};
			}
			else
				return false;
		}
		else if (code.Length == 4)
			{
				if (code[0] == code[1] && code[1] == code[2])
				{

					return code[0] switch
				{
						Greater => true,
						_ => false,
				};
			}
			else
				return false;
		}
		else
		{

			return code[0] switch
			{
					Greater or Less or Not or BitNOT => false,
					_ => true,
			};
		}
	}

		internal static bool IsDynamicReference(string code)
		{
			var d = false;

			for (var i = 0; i < code.Length; i++)
			{
				var sym = code[i];

				if (sym == Resolve)
				{
					if (d)
						if (code[i - 1] == Resolve)
							return false;

					d = !d;
				}
				else if (!IsIdentifier(sym))
					return false;
			}

			return code.Length != 0;
		}

		internal static bool IsExpressionIf(string code)
		{
			code = code.TrimStart(Spaces);
			var i = 0;

			if (code.Length == 0)
				return true;

			if (code[0] == ParenOpen)
				return true;

			while (i < code.Length && IsIdentifier(code[i])) i++;

			if (i == 0 || IsKeyword(code[..i]))
				return true;

			while (i < code.Length && IsSpace(code[i])) i++;

			if (i == 0 || i == code.Length)
				return false;

			return code[i] switch
		{
				Equal or Not or Greater or Less => false,
				_ => true,
		};
	}

	internal static bool IsExpressionParameter(string code)
		{
			code = code.TrimStart(Spaces);
			var z = code.IndexOf(Resolve);
			return z == 0 && (code.Length == 1 || IsSpace(code[1]));
		}

		internal static bool IsFunction(string code, string next)
		{
			if (code.Length == 0 || code[0] == ParenOpen)
				return false;

			var stage = 0;
			var str = false;

			if (code.StartsWith("static", StringComparison.OrdinalIgnoreCase))
				code = code.AsSpan(6).Trim(SpaceTab).ToString();

			for (var i = 0; i < code.Length; i++)
			{
				var sym = code[i];

				switch (stage)
				{
					case 0:
						if (sym == ParenOpen)
							stage++;
						else if (!IsIdentifier(sym))
							return false;

						break;

					case 1:
						if (sym == DoubleQuote)
						{
							str = !str;
						}
						else if (!str)
						{
							if (sym == ParenClose)
								stage++;
							else if (sym == ParenOpen)
								stage--;
						}

						break;

					case 2:
						if (sym == BlockOpen)
							return true;
						else if (i < code.Length - 2 && sym == '=' && code[i + 1] == '>')
							return true;
						else if (IsCommentAt(code, i))
							goto donext;
						else if (!IsSpace(sym))
							return false;

						break;
				}
			}

			donext:

			if (next.Length == 0)
				return false;

			var reader = new StringReader(next);

			while (reader.Peek() != -1)
			{
				var sym = (char)reader.Read();

				if (sym == BlockOpen)
					return true;
				else if (!IsSpace(sym))
					return false;
			}

			return false;
		}

		internal static bool IsHotkeyLabel(string code)
		{
			var z = code.FindFirstNotInQuotes(HotkeySignal);

			if (z == -1)
				return false;

			var p = false;

			for (var i = 0; i < z; i++)
			{
				var sym = code[i];

				switch (sym)
				{
					case '#':
					case '!':
					case '^':
					case '+':
					case '<':
					case '>':
					case '*':
					case '~':
					case '$':
					case ';':
						break;

					case '&':
						p = false;
						break;

					default:
						if (!IsSpace(sym) && !char.IsLetterOrDigit(sym))
						{
							if (p)
								return false;
							else
								p = true;
						}

						break;
				}
			}

			return true;
		}

		internal static bool IsHotstringLabel(string code) => code.Length > 0 && code[0] == HotkeyBound&& code.FindFirstNotInQuotes(HotkeySignal) != -1 && code.Count(ch => ch == HotkeyBound) >= 4;

		internal static bool IsIdentifier(char symbol) => char.IsLetterOrDigit(symbol) || VarExt.Contains(symbol);

		internal static bool IsKeyword(string code) => keywords.Contains(code);

		internal static bool IsKeyword(char symbol)
		{

			return symbol switch
		{
				TernaryA => true,
				_ => false,
		};
	}

	internal static bool IsLabel(string code)
		{
			for (var i = 0; i < code.Length; i++)
			{
				var sym = code[i];

				if (IsIdentifier(sym))
					continue;

				switch (sym)
				{
					case HotkeyBound:
						if (i == 0)
							return false;
						else if (i == code.Length - 1)
							return true;
						else
						{
							var sub = StripCommentSingle(code[i..]);
							return sub.Length == 0 || IsSpace(sub);
						}

					case ParenOpen:
					case ParenClose:
						break;

					default:
						return false;
				}
			}

			return false;
		}

		internal static bool IsPrimitiveObject(string code, out object result)
		{
			result = null;

			if (code == null)
				return true;

			if (code.Length == 0)
			{
				result = code;
				return true;
			}

			switch (code.ToLowerInvariant())
			{
				case TrueTxt:
					//result = 1L;
					result = true;//Althought the AHK documentation says true/false are really just 1/0 under the hood, that causes problems here.
					return true;//Particularly with the Force[type]() functions used in binary operator methods. Having them be a bool type makes it much easier to determine the caller's intent when comparing values.

				case FalseTxt:
					//result = 0L;
					result = false;
					return true;

				case Unset:
					result = null;
					return true;
			}

			//Mono incorrectly determines "." as a numeric value.
			if (code.Length == 1 && code[0] == Concatenate)
				return false;

			var codeTrim = code.Trim(Spaces);
			var longresult = codeTrim.ParseLong();//Also supports hex, but do not consider raw hex, because then a variable name like a would be returned as 10.

			if (longresult.HasValue)
			{
				result = longresult.Value;
				goto exp;
			}

			if (double.TryParse(codeTrim, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))//This will make any number be a double internally. Not sure if this is what AHK does.
			{
				result = d;
				goto exp;
			}

			result = null;
			return false;
			exp:
			return true;
		}

		internal static bool IsSpace(char sym) => SpacesSv.Contains(sym);

		internal static bool IsSpace(string code)
		{
			foreach (var sym in code)
				if (!IsSpace(sym))
					return false;

			return true;
		}

		internal static bool IsUnaryOperator(Script.Operator op)
		{

			return op switch
		{
				//Minus doesn't seem to be needed here.
				Script.Operator.Subtract or Script.Operator.LogicalNot or Script.Operator.LogicalNotEx or Script.Operator.BitwiseNot or Script.Operator.BitwiseAnd or Script.Operator.Dereference => true,
				//Add is intentionally excluded: treating it as a unary operator breaks the postfix operator
				//when used in an assignment like y := x++
				//case Script.Operator.Add:
				//return true;
				_ => false,
		};
	}

		internal static bool IsNumericString(ReadOnlySpan<char> str) => long.TryParse(str, out _) || double.TryParse(str, out _);

		private static string ParseString(CodeLine codeLine, string code, ref int i, bool prependVerbatimEscape)
		{
			var escape = false;
			var sym = code[i];
			var verbatim = sym == SingleQuote;
			var str = new StringBuilder(code.Length);
			_ = str.Append(DoubleQuote);
			i++;

			if (i == code.Length)
				throw new ParseException(ExUntermStr, codeLine.LineNumber, codeLine.Code, codeLine.FileName);

			for (; i < code.Length; i++)
			{
				sym = code[i];

				if (verbatim)
				{
					var isbound = sym == SingleQuote;

					if (!escape && isbound)
					{
						_ = str.Append(DoubleQuote);
						break;
					}

					if (prependVerbatimEscape && sym == DoubleQuote)
						_ = str.Append(Escape);

					_ = str.Append(sym);

					if ((!isbound || escape) && i == code.Length - 1)//If we've reached the end and it's not a quote. or it is a quote but we are in escape, then it's an unterminated string.
						throw new ParseException(ExUntermStr, codeLine.LineNumber, codeLine.Code, codeLine.FileName);
				}
				else
				{
					var isbound = sym == DoubleQuote;
					_ = str.Append(sym);

					if (!escape && isbound)
						break;

					if ((!isbound || escape) && i == code.Length - 1)//If we've reached the end and it's not a quote. or it is a quote but we are in escape, then it's an unterminated string.
						throw new ParseException(ExUntermStr, codeLine.LineNumber, codeLine.Code, codeLine.FileName);
				}

				escape = sym == Escape && !escape;
			}

			return str.ToString();
		}

		private List<object> ExtractRange(List<object> parts, int start, int end)
		{
			var extracted = new List<object>(end - start);

			for (var i = start; i < end; i++)
			{
				extracted.Add(parts[start]);
				parts.RemoveAt(start);
			}

			return extracted;
		}

		private bool IsDirective(string code) => code.Length > 2 && code[0] == Directive;
		private void MoveToEOL(string code, ref int i)
		{
			while (i < code.Length)
			{
				switch (code[i])
				{
					case CR:
						var n = i + 1;

						if (n < code.Length && code[n] == LF)
							i = n;

					goto case LF;

					case LF:
						return;

					default:
						i++;
						break;
				}
			}
		}
		private enum Token
		{ Unknown, Assign, Command, Label, Hotkey, Flow, Throw, Expression, Directive, Prop, PropGet, PropSet }
	}
}
