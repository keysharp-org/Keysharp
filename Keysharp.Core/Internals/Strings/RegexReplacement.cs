using Keysharp.Builtins;
namespace Keysharp.Internals.Strings
{
	/// <summary>
	/// A RegExReplace replacement string, compiled once with AutoHotkey's syntax rather than .NET's. $0 to $9 take a
	/// single digit, so $15 is subpattern 1 followed by a 5, while ${number} and ${name} take any; a U, L or T after the $
	/// converts that subpattern to upper, lower or title case; and $$ is a literal $. A subpattern which does not exist
	/// or did not participate is empty, and any other $ is literal text.
	/// </summary>
	internal static class RegexReplacement
	{
		// PCRE limits a subpattern name to 32 characters, which is the buffer AutoHotkey reads one into.
		private const int MaxNameLength = 32;

		private readonly record struct Part(string Literal, int Number, string Name, char Transform);

		internal static Action<PcreMatch, StringBuilder> Parse(string replacement)
		{
			var parts = new List<Part>();
			var literal = new StringBuilder();

			void Reference(int number, string name, char transform)
			{
				if (literal.Length > 0)
				{
					parts.Add(new(literal.ToString(), 0, null, '\0'));
					_ = literal.Clear();
				}

				parts.Add(new(null, number, name, transform));
			}

			for (var i = 0; i < replacement.Length; i++)
			{
				if (replacement[i] != '$')
				{
					_ = literal.Append(replacement[i]);
					continue;
				}

				var next = i + 1;
				var transform = next < replacement.Length ? char.ToUpperInvariant(replacement[next]) : '\0';

				if (transform is 'U' or 'L' or 'T')
					next++;
				else
					transform = '\0';

				var after = next < replacement.Length ? replacement[next] : '\0';

				if (after == '{' && replacement.IndexOf('}', next + 1) is var close && close > next + 1)
				{
					var name = replacement.AsSpan(next + 1, close - next - 1);

					// A name too long to be one leaves the closing brace as text, as AutoHotkey does.
					if (name.Length > MaxNameLength)
						_ = literal.Append('}');
					else if (ObjectExtensions.ScanNumber(name, out _, out _, out _))
						Reference(LeadingInteger(name), null, transform);
					else
						Reference(-1, name.ToString(), transform);

					i = close;
				}
				else if (after == '$')
					_ = literal.Append(replacement[++i]);
				else if (char.IsAsciiDigit(after))
				{
					Reference(after - '0', null, transform);
					i = next;
				}
				else
					_ = literal.Append('$');
			}

			if (parts.Count == 0)
			{
				var text = literal.ToString();
				return (_, sb) => sb.Append(text);
			}

			if (literal.Length > 0)
				parts.Add(new(literal.ToString(), 0, null, '\0'));

			var compiled = parts.ToArray();
			return (match, sb) =>
			{
				foreach (var part in compiled)
				{
					if (part.Literal != null)
					{
						_ = sb.Append(part.Literal);
						continue;
					}

					var found = part.Name != null ? match.TryGetGroup(part.Name, out var group) : match.TryGetGroup(part.Number, out group);

					if (!found || !group.Success || group.Length == 0)
						continue;

					_ = part.Transform switch
					{
						'U' => sb.Append(group.Value.ToUpperInvariant()),
						'L' => sb.Append(group.Value.ToLowerInvariant()),
						'T' => sb.Append(Builtins.Strings.StrTitle(group.Value)),
						_ => sb.Append(group.ValueSpan)
					};
				}
			};
		}

		// The integer a numeric ${...} names, read as C's atoi does: digits after an optional sign, up to the first other
		// character, so ${1.0} is subpattern 1 and a negative number names none.
		private static int LeadingInteger(ReadOnlySpan<char> name)
		{
			name = name.Trim(" \t");
			var negative = name.Length > 0 && name[0] == '-';
			var i = name.Length > 0 && name[0] is '-' or '+' ? 1 : 0;
			var value = 0L;

			for (; i < name.Length && char.IsAsciiDigit(name[i]) && value <= int.MaxValue; i++)
				value = value * 10 + name[i] - '0';

			var magnitude = (int)Math.Min(value, int.MaxValue);
			return negative ? -magnitude : magnitude;
		}
	}
}
