namespace Keysharp.Core.Common.Strings
{
	internal static class Options
	{
		internal static bool? OnOff(object mode)
		{
			if (mode == null)
				return false;

			return mode.ToString().ToLower() switch
		{
				Keyword_On or "1" or "true" => true,
				Keyword_Off or "0" or "false" => false,
				_ => null,
		};
	}

	internal static bool IsOption(string options, string search)
		{
			return string.IsNullOrEmpty(options) || string.IsNullOrEmpty(search)
				   ? false
				   : options.Trim().Equals(search, StringComparison.OrdinalIgnoreCase);
		}

		internal static Dictionary<char, string> KeyValues(string Options, bool Lowercase, char[] Exceptions)
		{
			var table = new Dictionary<char, string>();
			var buf = new StringBuilder();
			var i = 0;
			var exp = false;
			char key = default;
			string val;

			if (Lowercase)
				for (i = 0; i < Exceptions.Length; i++)
					Exceptions[i] = char.ToLowerInvariant(Exceptions[i]);

			i = 0;

			while (i < Options.Length)
			{
				var sym = Options[i];
				i++;

				if (char.IsWhiteSpace(sym) || i == Options.Length)
				{
					if (buf.Length == 0)
						continue;

					if (exp)
					{
						exp = false;
						val = buf.ToString();
						buf.Length = 0;
						table[key] = val;
						continue;
					}

					key = buf[0];

					if (Lowercase)
						key = char.ToLowerInvariant(key);

					foreach (var ex in Exceptions)
						if (key == ex)
						{
							exp = true;
							buf.Length = 0;
							continue;
						}

					val = buf.Length > 1 ? buf.Remove(0, 1).ToString() : string.Empty;
					table[key] = val;
					buf.Length = 0;
				}
				else
					_ = buf.Append(sym);
			}

			if (exp && key != default(char))
			{
				if (table.ContainsKey(key))
					table[key] = null;
				else
					table.Add(key, null);
			}

			return table;
		}

		internal static bool OptionContains(string options, params string[] keys)
		{
			foreach (var key in keys)
				if (OptionContains(options, key))
					return true;

			return false;
		}

		internal static bool OptionContains(string options, string key, bool casesensitive = false)
		{
			if (string.IsNullOrEmpty(options) || string.IsNullOrEmpty(key))
				return false;

			var comp = casesensitive ? StringComparison.CurrentCulture : StringComparison.OrdinalIgnoreCase;
			var i = 0;

			while (i < options.Length)
			{
				var z = options.IndexOf(key, i, comp);
				if (z < 0)
					return false;

				var startOk = z == 0 || !char.IsLetter(options, z - 1);
				var end = z + key.Length;

				if (!startOk)
				{
					i = z + 1;
					continue;
				}

				var endOk = end >= options.Length || !char.IsLetter(options[end]);

				if (!endOk)
				{
					i = z + 1;
					continue;
				}

				return true;
			}

			return false;
		}

		internal static string[] ParseOptions(string options) => options.Split(Spaces, StringSplitOptions.RemoveEmptyEntries);

		internal static Dictionary<string, string> ParseOptionsRegex(ref string options, Dictionary<string, Regex> items, bool remove = true)
		{
			var results = new Dictionary<string, string>();

			foreach (var item in items)
			{
				if (item.Value.IsMatch(options))
				{
					var match = item.Value.Match(options).Groups[1].Captures[0];
					results.Add(item.Key, match.Value);

					if (remove)
						options = options.Substring(0, match.Index) + options.Substring(match.Index + match.Length);
				}
				else
				{
					results.Add(item.Key, "");
				}
			}

			return results;
		}

		internal static bool TryParse(string opt, string prefix, ref int result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, int def = default) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty, def);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref int result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, int def = default) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> s, out int x) =>
		{
			if (int.TryParse(s, out x))
				return true;

			return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
				   int.TryParse(s.Slice(2), NumberStyles.HexNumber, CultureInfo.CurrentCulture, out x);
		}, ref result, comp, allowempty, def);

		internal static bool TryParse(string opt, string prefix, ref long result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, long def = default) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty, def);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref long result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, long def = default) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> s, out long x) =>
		{
			if (long.TryParse(s, out x))
				return true;

			return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
				   long.TryParse(s.Slice(2), NumberStyles.HexNumber, CultureInfo.CurrentCulture, out x);
		}, ref result, comp, allowempty, def);

		internal static bool TryParse(string opt, string prefix, ref float result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, float def = default) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty, def);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref float result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, float def = default) =>
		TryParseWrapper(opt, prefix, float.TryParse, ref result, comp, allowempty, def);

		internal static bool TryParse(string opt, string prefix, ref double result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, double def = default) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty, def);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref double result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, double def = default) =>
		TryParseWrapper(opt, prefix, double.TryParse, ref result, comp, allowempty, def);

		internal static bool TryParse(string opt, string prefix, ref Color result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref Color result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> v, out Color r) => Conversions.TryParseColor(v.ToString(), out r), ref result, comp, allowempty, Control.DefaultForeColor);

		internal static bool TryParse(string opt, string prefix, ref bool result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, bool def = default) =>
		TryParse(opt.AsSpan(), prefix, ref result, comp, allowempty, def);

		internal static bool TryParse(ReadOnlySpan<char> opt, string prefix, ref bool result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowempty = false, bool def = default)
		{
			if (opt[0] == '-' && opt.Slice(1).CompareTo(prefix, StringComparison.OrdinalIgnoreCase) == 0)
			{
				result = false;
				return true;
			}
			else if (opt[0] == '+' && opt.Slice(1).CompareTo(prefix, StringComparison.OrdinalIgnoreCase) == 0)
			{
				result = true;
				return true;
			}

			return TryParseWrapper(opt, prefix, (ReadOnlySpan<char> v, out bool r) =>
			{
				var b = OnOff(v.ToString());

				if (b != null)
				{
					r = b.Value;
					return true;
				}
				else if (allowempty)
				{
					r = false;
					return true;
				}

				r = false;
				return false;
			}, ref result, comp, allowempty, def);
		}

		/// <summary>
		/// Parse a string and get Coordinates
		/// </summary>
		/// <param name="input">String in Format X123 Y123</param>
		/// <param name="p">out Point Struct if possible</param>
		/// <returns>true if parsing succesful</returns>
		internal static bool TryParseCoordinate(string input, out Point p) => throw new NotImplementedException();

		internal static bool TryParseDateTime(string opt, string prefix, string format, ref DateTime result, StringComparison comp = StringComparison.OrdinalIgnoreCase) =>
		TryParseDateTime(opt.AsSpan(), prefix, format, ref result, comp);

		internal static bool TryParseDateTime(ReadOnlySpan<char> opt, string prefix, string format, ref DateTime result, StringComparison comp = StringComparison.OrdinalIgnoreCase) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> v, out DateTime r) =>
		{
			if (!DateTime.TryParseExact(v, format.AsSpan(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out r))
				r = Conversions.ToDateTime(v.ToString());

			return true;
		}, ref result, comp);

		internal static bool TryParseString(ReadOnlySpan<char> opt, string prefix, ref string result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowEmpty = false) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> v, out string r) => { r = v.ToString(); return true; }, ref result, comp, allowEmpty);

		internal static bool TryParseString(string opt, string prefix, ref string result, StringComparison comp = StringComparison.OrdinalIgnoreCase, bool allowEmpty = false) =>
		TryParseWrapper(opt, prefix, (ReadOnlySpan<char> v, out string r) => { r = v.ToString(); return true; }, ref result, comp, allowEmpty);

		private static bool TryParseWrapper<T>(string opt, string prefix, TryParseHandler<T> handler, ref T result, StringComparison comp = StringComparison.OrdinalIgnoreCase,
											   bool allowempty = false, T def = default) =>
		TryParseWrapper(opt.AsSpan(), prefix, handler, ref result, comp, allowempty, def);

		private static bool TryParseWrapper<T>(ReadOnlySpan<char> opt, string prefix, TryParseHandler<T> handler, ref T result, StringComparison comp = StringComparison.OrdinalIgnoreCase,
											   bool allowempty = false, T def = default)// where T : struct
		{
			var doit = false;
			var suffix = ReadOnlySpan<char>.Empty;

			if (opt.StartsWith(prefix, comp))
			{
				suffix = opt.Slice(prefix.Length);
				doit = true;
			}
			else if (opt.Length > 0 && opt[0] == '+' && opt.Slice(1).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				suffix = opt.Slice(prefix.Length + 1);
				doit = true;
			}

			if (doit)
			{
				if (allowempty && suffix.CompareTo([], StringComparison.OrdinalIgnoreCase) == 0)//Need CompareTo() because == doesn't work with spans and "".
				{
					result = def;
					return true;
				}
				else if (handler(suffix, out var res))
				{
					result = res;
					return true;
				}
			}

			return false;
		}

		internal delegate bool TryParseHandler<T>(ReadOnlySpan<char> value, out T result);
	}
}
