using Keysharp.Builtins;
#if WINDOWS
using NativeColor = System.Drawing.Color;
#else
using NativeColor = Eto.Drawing.Color;
#endif
using Opts = Keysharp.Internals.Strings.Options;

namespace Keysharp.Internals.Strings
{
	internal struct FontOptions
	{
		internal string name;
		internal double? size;
		internal NativeColor? color;
		internal int? weight, quality;
		internal bool? italic, underline, strike;

		internal string Options => BuildOptions(true);
		internal string OptionsNoColor => BuildOptions(false);

		internal static NativeColor Opaque(NativeColor value) => NativeColor.FromArgb(value.ToArgb() | unchecked((int)0xFF000000));

		internal static bool ValidSize(double value)
		{
			if (double.IsFinite(value) && (float)value > 0 && value <= float.MaxValue)
				return true;
			_ = Errors.ValueErrorOccurred($"Invalid font size {value}. Expected a finite number greater than zero and at most {float.MaxValue}.");
			return false;
		}

		internal static bool ValidInteger(double value, string name, int minimum, int maximum)
		{
			if (double.IsFinite(value) && value == Math.Truncate(value) && value >= minimum && value <= maximum)
				return true;
			_ = Errors.ValueErrorOccurred($"Invalid font {name} {value}. Expected an integer from {minimum} to {maximum}.");
			return false;
		}
		private string BuildOptions(bool includeColor)
		{
			var sb = new StringBuilder(48);
			//"norm" clears all styles; "w400" alone preserves unspecified styles.
			var normed = italic == false || underline == false || strike == false;

			if (normed)
				Add(sb, "norm");

			if (size.HasValue)
				Add(sb, "s" + size.Value.ToString(CultureInfo.InvariantCulture));

			if (weight.HasValue && !(normed && weight.Value == 400))
				Add(sb, "w" + weight.Value.ToString(CultureInfo.InvariantCulture));

			if (quality.HasValue)
				Add(sb, "q" + quality.Value.ToString(CultureInfo.InvariantCulture));

			if (includeColor && color.HasValue)
				Add(sb, "c" + (color.Value.ToArgb() & 0x00FFFFFF).ToString("X6"));

			if (italic == true)
				Add(sb, "italic");

			if (underline == true)
				Add(sb, "underline");

			if (strike == true)
				Add(sb, "strike");

			return sb.ToString();
		}

		private static void Add(StringBuilder sb, string token)
		{
			if (sb.Length > 0)
				_ = sb.Append(' ');

			_ = sb.Append(token);
		}
		internal void Parse(string styles, Action<string> onUnknown = null)
		{
			if (string.IsNullOrEmpty(styles))
				return;

			foreach (Range r in styles.AsSpan().SplitAny(Spaces))
			{
				var opt = styles.AsSpan(r).Trim();

				if (opt.Length == 0)
					continue;

				int i = 0;
				var c = default(NativeColor);

				if (opt.StartsWith("s", StringComparison.OrdinalIgnoreCase)
					&& double.TryParse(opt[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { if (ValidSize(f)) size = f; }
				else if (Opts.TryParse(opt, "q", ref i)) { if (ValidInteger(i, "quality", 0, 5)) quality = i; }
				else if (Opts.TryParse(opt, "w", ref i)) { if (ValidInteger(i, "weight", 1, 1000)) weight = i; }
				else if (Opts.TryParse(opt, "c", ref c)) { color = Opaque(c); }
				else if (opt.Equals(Keyword_Bold, StringComparison.OrdinalIgnoreCase)) { weight = 700; }
				else if (opt.Equals(Keyword_Italic, StringComparison.OrdinalIgnoreCase)) { italic = true; }
				else if (opt.Equals(Keyword_Strike, StringComparison.OrdinalIgnoreCase)
						 || opt.Equals("strikeout", StringComparison.OrdinalIgnoreCase)) { strike = true; }
				else if (opt.Equals(Keyword_Underline, StringComparison.OrdinalIgnoreCase)) { underline = true; }
				else if (opt.Equals(Keyword_Norm, StringComparison.OrdinalIgnoreCase))
				{
					//Sets rather than clears: the explicit false is what makes Options emit "norm" again.
					weight = 400;
					italic = underline = strike = false;
				}
				else
					onUnknown?.Invoke(opt.ToString());
			}
		}
	}
}
