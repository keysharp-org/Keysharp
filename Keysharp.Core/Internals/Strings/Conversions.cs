using Keysharp.Builtins;
using SearchOption = System.IO.SearchOption;
using Calendar = System.Globalization.Calendar;

namespace Keysharp.Internals.Strings
{
	public static class Conversions
	{
#if WINDOWS
		private const string bin = "REG_BINARY";

		private const string dword = "REG_DWORD";

		private const string multisz = "REG_MULTI_SZ";

		private const string none = "NONE";

		private const string qword = "REG_QWORD";

		private const string regexpsz = "REG_EXPAND_SZ";

		private const string regsz = "REG_SZ";

		private const string unk = "UNKNOWN";
#endif

		/// <summary>
		/// Need to manually provide a wrapper because calendar is not a constant, which is required for default parameters.
		/// </summary>
		/// <param name="time"></param>
		/// <returns></returns>
		internal static DateTime ToDateTime(string time)
		{
			switch (time.Length)
			{
				case 18:
					if (DateTime.TryParseExact(time, "yyyyMMddHHmmss.fff", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dt))
						return dt;

					break;

				case 14:
					if (DateTime.TryParseExact(time, "yyyyMMddHHmmss", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;

				case 12:
					if (DateTime.TryParseExact(time, "yyyyMMddHHmm", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;

				case 10:
					if (DateTime.TryParseExact(time, "yyyyMMddHH", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;

				case 8:
					if (DateTime.TryParseExact(time, "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;

				case 6:
					if (DateTime.TryParseExact(time, "yyyyMM", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;

				case 4:
					if (DateTime.TryParseExact(time, "yyyy", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
						return dt;

					break;
			}

			return DateTime.MinValue;
		}

		internal static Font ConvertFont(Font font)
		{
#if WINDOWS
			return font;
#else
			return font;
#endif
		}

		// AHK font sizes follow the Windows convention (points at a 96 DPI baseline). macOS renders points at
		// a 72 DPI baseline, so the same nominal size appears smaller; scale by 96/72 and round to the nearest
		// whole point so macOS GUIs match the other platforms visually. No-op on Windows/Linux (96 DPI). This
		// is the single point of font-size scaling: use it for every default/explicit point size so they stay
		// consistent (default font, script-specified sizes, dialog and debug-window fonts).
		internal static float ScaleFontSize(float size)
		{
#if OSX
			return (float)Math.Round(size * (96f / 72f), MidpointRounding.AwayFromZero);
#else
			return size;
#endif
		}

		/// <summary>
		/// The inverse of <see cref="ScaleFontSize"/>: a native point size back to the Windows-convention
		/// number a script would have written, so that reading a size and writing it back is a no-op on
		/// macOS too. Not exactly lossless there, since ScaleFontSize rounds to whole points going in.
		/// </summary>
		/// <summary>Sets or clears one flag of an enum, since the toolkits model styles as flag sets.</summary>
		private static T Flag<T>(T flags, T bit, bool on) where T : struct, Enum
		{
			var f = Convert.ToInt64(flags);
			var b = Convert.ToInt64(bit);
			return (T)Enum.ToObject(typeof(T), on ? f | b : f & ~b);
		}

		internal static float UnscaleFontSize(float size)
		{
#if OSX
			return size * (72f / 96f);
#else
			return size;
#endif
		}

		/// <summary>
		/// Parses a string as a boolean flag: "1"/"true"/"on" => true, "0"/"false"/"off" => false (words
		/// case-insensitive). Returns null for null/empty or any unrecognized value, so callers can supply
		/// their own default. Does not trim - trim beforehand if leading/trailing whitespace should be ignored.
		/// </summary>
		public static bool? ParseBoolish(string value)
		{
			if (string.IsNullOrEmpty(value))
				return null;

			if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
				return true;

			if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
				return false;

			return null;
		}

		internal static ToggleValueType ConvertOnOff(object mode, ToggleValueType def = ToggleValueType.Invalid)
		{
			if (mode == null)
				return ToggleValueType.Neutral;

			var str = mode.ToString();

			if (str?.Length == 0)
				return ToggleValueType.Neutral;

			return ParseBoolish(str) switch
			{
				true => ToggleValueType.On,
				false => ToggleValueType.Off,
				_ => def,
			};
		}

		internal static ToggleValueType ConvertOnOffAlways(string buf, ToggleValueType def = ToggleValueType.Invalid)
		{
			var toggle = ConvertOnOff(buf);

			if (toggle != ToggleValueType.Invalid)
				return toggle;

			if (string.Compare(buf, "AlwaysOn", true) == 0)
				return ToggleValueType.AlwaysOn;

			if (string.Compare(buf, "AlwaysOff", true) == 0)
				return ToggleValueType.AlwaysOff;

			return def;
		}

		internal static ToggleValueType ConvertOnOffToggle(object mode, ToggleValueType def = ToggleValueType.Default)
		{
			var toggle = ConvertOnOff(mode);

			if (toggle != ToggleValueType.Invalid)
				return toggle;

			var str = mode.ToString();
			return string.Compare(str, "Toggle", true) == 0 || str == "-1" ? ToggleValueType.Toggle : def;
		}

		/// <summary>
		/// The canonical clipboard kind a script-facing name denotes ("text", "image", "files", "html", "rtf"),
		/// or null when the name is not a kind at all — in which case callers treat it as a platform-native
		/// format name instead. Shared by the Ks clipboard surface and base-AHK <c>ClipWait</c> so both resolve
		/// names identically.
		/// </summary>
		internal static ClipboardKind? ConvertClipboardKind(string name) => name switch
		{
			var x when x.Equals("text", StringComparison.OrdinalIgnoreCase) => ClipboardKind.Text,
			var x when x.Equals("image", StringComparison.OrdinalIgnoreCase) => ClipboardKind.Image,
			var x when x.Equals("files", StringComparison.OrdinalIgnoreCase) => ClipboardKind.Files,
			var x when x.Equals("html", StringComparison.OrdinalIgnoreCase) => ClipboardKind.Html,
			var x when x.Equals("rtf", StringComparison.OrdinalIgnoreCase) => ClipboardKind.Rtf,
			_ => null,
		};

		/// <summary>
		/// Makes all line endings in a string match the value passed in, or the default newline. The runtime
		/// needs this on its own account — GUI edit controls and the Windows clipboard backend both normalize
		/// what they read and write — and <c>Ks.NormalizeEol</c> is a thin wrapper over it.
		/// </summary>
		/// <param name="str">The string whose line endings will be normalized.</param>
		/// <param name="endOfLine">The line ending character to use. Default: DefaultNewLine.</param>
		internal static string NormalizeEol(object str, object endOfLine = null) =>
			str.As().ReplaceLineEndings(endOfLine.As(DefaultNewLine));

		internal static string FromFileAttribs(FileAttributes attribs)
		{
			var str = new StringBuilder(9);

			if ((attribs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				_ = str.Append('R');

			if ((attribs & FileAttributes.Archive) == FileAttributes.Archive)
				_ = str.Append('A');

			if ((attribs & FileAttributes.System) == FileAttributes.System)
				_ = str.Append('S');

			if ((attribs & FileAttributes.Hidden) == FileAttributes.Hidden)
				_ = str.Append('H');

			if ((attribs & FileAttributes.Normal) == FileAttributes.Normal)
				_ = str.Append('N');

			if ((attribs & FileAttributes.Directory) == FileAttributes.Directory)
				_ = str.Append('D');

			if ((attribs & FileAttributes.Offline) == FileAttributes.Offline)
				_ = str.Append('O');

			if ((attribs & FileAttributes.Compressed) == FileAttributes.Compressed)
				_ = str.Append('C');

			if ((attribs & FileAttributes.Temporary) == FileAttributes.Temporary)
				_ = str.Append('T');

			if (str.Length == 0)
				_ = str.Append('X');

			return str.ToString();
		}

		internal static long FromTime(DateTime time)
		{
			const int len = 2;
			const char pad = '0';
			var str = new StringBuilder(4 + 2 * 5);
			_ = str.Append(time.Year.ToString().PadLeft(len * 2, pad));
			_ = str.Append(time.Month.ToString().PadLeft(len, pad));
			_ = str.Append(time.Day.ToString().PadLeft(len, pad));
			_ = str.Append(time.Hour.ToString().PadLeft(len, pad));
			_ = str.Append(time.Minute.ToString().PadLeft(len, pad));
			_ = str.Append(time.Second.ToString().PadLeft(len, pad));
			return long.Parse(str.ToString());
		}

#if WINDOWS

		internal static RegistryValueKind GetRegistryType(string val)
		{

			return val switch
		{
				none => RegistryValueKind.None,
				unk => RegistryValueKind.Unknown,
				regsz => RegistryValueKind.String,
				regexpsz => RegistryValueKind.ExpandString,
				bin => RegistryValueKind.Binary,
				dword => RegistryValueKind.DWord,
				multisz => RegistryValueKind.MultiString,
				qword => RegistryValueKind.QWord,
				_ => RegistryValueKind.Unknown,
		};
	}

	internal static string GetRegistryTypeName(RegistryValueKind val)
		{

			return val switch
		{
				RegistryValueKind.None => none,
				RegistryValueKind.Unknown => unk,
				RegistryValueKind.String => regsz,
				RegistryValueKind.ExpandString => regexpsz,
				RegistryValueKind.Binary => bin,
				RegistryValueKind.DWord => dword,
				RegistryValueKind.MultiString => multisz,
				RegistryValueKind.QWord => qword,
				_ => unk,
		};
	}

#endif

	internal static byte HighByte(int i) => (byte)((((ulong)i) >> 8) & 0xff);

		internal static short HighWord(int i) => (short)((((ulong)i) >> 16) & 0xffff);

		internal static byte LowByte(int i) => (byte)(((ulong)i) & 0xff);

		internal static short LowWord(int i) => (short)(((ulong)i) & 0xffff);

		internal static int MakeInt(int lowPart, int highPart) => (lowPart & 0x0000FFFF) | (highPart << 16);

		/// <summary>
		/// Parses a CaseSense option the way <c>Map.CaseSense</c> spells it, which is also the spelling
		/// <c>Json.Decode</c> takes. Returns null for anything that is neither on/off nor "locale", leaving
		/// the caller to choose between a default and an error -- Map keeps its previous mode, Json raises.
		/// </summary>
		/// <param name="option">The option as the script supplied it.</param>
		/// <returns>The mode, or null if option named none.</returns>
		internal static eCaseSense? ParseCaseSense(object option)
		{
			if (option == null)
				return null;//Options.OnOff maps null to false, which would silently mean Off here.

			if (Options.OnOff(option) is bool onOff)
				return onOff ? eCaseSense.On : eCaseSense.Off;

			return string.Equals(option.ToString().Trim(), Keyword_Locale, StringComparison.OrdinalIgnoreCase)
				   ? eCaseSense.Locale : null;
		}

		/// <summary>The string comparer a <see cref="eCaseSense"/> mode selects.</summary>
		/// <param name="caseSense">The mode.</param>
		/// <returns>The comparer, culture-aware only for <see cref="eCaseSense.Locale"/>.</returns>
		internal static StringComparer ComparerFor(eCaseSense caseSense) => caseSense switch
		{
			eCaseSense.On => StringComparer.Ordinal,
			eCaseSense.Off => StringComparer.OrdinalIgnoreCase,
			_ => StringComparer.CurrentCultureIgnoreCase,
		};

		internal static StringComparison ParseComparisonOption(object option) => option.ToString().Trim().ToLowerInvariant() switch
	{
			"1" or TrueTxt or Keyword_On => StringComparison.Ordinal,
			Keyword_Locale => StringComparison.CurrentCulture,
			_ => StringComparison.OrdinalIgnoreCase,
	};

#if WINDOWS
		internal static Font ParseFont(Font standard, string styles, string family = null)
	{
			family = string.IsNullOrEmpty(family) ? standard.FontFamily.Name : family;
			//Tokenized by Ks.Font, which owns the whole option vocabulary; this only applies the result.
			var spec = new Ks.Font(null);
			spec.Parse(styles);
			var size = spec.SizeOr(standard.Size);
			var display = standard.Style;
			display = Flag(display, FontStyle.Bold, spec.BoldOr(display.HasFlag(FontStyle.Bold)));
			display = Flag(display, FontStyle.Italic, spec.ItalicOr(display.HasFlag(FontStyle.Italic)));
			display = Flag(display, FontStyle.Underline, spec.UnderlineOr(display.HasFlag(FontStyle.Underline)));
			display = Flag(display, FontStyle.Strikeout, spec.StrikeOr(display.HasFlag(FontStyle.Strikeout)));

			FontFamily fam;
			try
			{
				// new Font() doesn't throw if the font family is not found and just defaults
				// to Microsoft Sans Serif, so do this roundabout way instead which does throw
				fam = new FontFamily(family);
			}
			catch
			{
				return standard;
			}
			if (!fam.IsStyleAvailable(display))
				display = FontStyle.Regular;

			return ConvertFont(new Font(fam, size, display));
		}
#else
		internal static Font ParseFont(Font standard, string styles, string family = null)
		{
			string resolvedFamily = family;
			try {
				if (string.IsNullOrEmpty(family))
					resolvedFamily = standard.FamilyName;
			}
			catch
			{
				return standard;
			}
			//Tokenized by Ks.Font, which owns the whole option vocabulary; this only applies the result.
			var spec = new Ks.Font(null);
			spec.Parse(styles);
			//The script's size is in the Windows convention, so convert to this platform's points.
			var size = spec.size.HasValue ? ScaleFontSize((float)spec.size.Value) : standard.Size;
			var display = standard.FontStyle;
			display = Flag(display, FontStyle.Bold, spec.BoldOr(display.HasFlag(FontStyle.Bold)));
			display = Flag(display, FontStyle.Italic, spec.ItalicOr(display.HasFlag(FontStyle.Italic)));
			var decorations = standard.FontDecoration;
			decorations = Flag(decorations, FontDecoration.Underline, spec.UnderlineOr(decorations.HasFlag(FontDecoration.Underline)));
			decorations = Flag(decorations, FontDecoration.Strikethrough, spec.StrikeOr(decorations.HasFlag(FontDecoration.Strikethrough)));

			// When the requested family isn't installed on this system (e.g. "Comic Sans MS"
			// on Linux), Eto's Font constructor throws ArgumentOutOfRangeException. Mirror
			// AutoHotkey's behavior by still applying the size/style/decoration changes and
			// falling back to the current font's family instead of discarding the call.
			if (!string.IsNullOrEmpty(resolvedFamily)
					&& !Eto.Platform.Instance.CreateShared<Fonts.IHandler>().FontFamilyAvailable(resolvedFamily))
				resolvedFamily = standard.FamilyName;

			try
			{
				return ConvertFont(new Font(resolvedFamily, size, display, decorations));
			}
			catch
			{
				return standard;
			}
		}
#endif

		internal static List<int> ParseRange(string[] splits)
		{
			var vals = new List<int>();

			for (var i = 0; i < splits.Length; i++)
			{
				if (splits[i]?.Length == 0)
					continue;

				if (int.TryParse(splits[i], out var val))
				{
					if (i > 0)
						if (splits[i - 1]?.Length == 0)
							val = -val;

					vals.Add(val);
				}
			}

			return vals;
		}

		internal static void ParseRange(string range, out DateTime dtlow, out DateTime dthigh)
		{
			var splits = range.Split('-', StringSplitOptions.None);
			dtlow = DateTime.MinValue;
			dthigh = DateTime.MaxValue;

			if (splits.Length == 2)
			{
				if (string.IsNullOrEmpty(splits[0]) && !string.IsNullOrEmpty(splits[1]))
				{
					dtlow = DateTime.MinValue;
					_ = DateTime.TryParseExact(splits[1], "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dthigh);
				}
				else if (!string.IsNullOrEmpty(splits[0]) && string.IsNullOrEmpty(splits[1]))
				{
					_ = DateTime.TryParseExact(splits[0], "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dtlow);
					dthigh = DateTime.MaxValue;
				}
				else if (!string.IsNullOrEmpty(splits[0]) && !string.IsNullOrEmpty(splits[1]))
				{
					_ = DateTime.TryParseExact(splits[0], "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dtlow);
					_ = DateTime.TryParseExact(splits[1], "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dthigh);
				}
			}
			else if (splits.Length == 1 && !string.IsNullOrEmpty(splits[0]))
			{
				_ = DateTime.TryParseExact(splits[0], "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dtlow);
				dthigh = DateTime.MaxValue;
			}
		}

		internal static RegexWithTag ParseRegExCs(string exp, bool reverse = false)
		{
			var opts = reverse ? RegexOptions.RightToLeft : RegexOptions.None;
			opts |= RegexOptions.Compiled;
			var parenIndex = exp.IndexOf(')');

			if (parenIndex != -1)
			{
				var leftParenIndex = exp.IndexOf('(');

				if (leftParenIndex == -1 || (leftParenIndex > parenIndex))//Make sure it was just a ) for options and not a ().
				{
					var span = exp.AsSpan(0, parenIndex);
					var substr = exp.Substring(parenIndex + 1);
					opts |= ToRegexOptionsCs(span);

					if (span.Contains('A'))
					{
						substr = "\\A" + substr;
					}

					return new RegexWithTag(substr, opts);
				}
			}

			return new RegexWithTag(exp, opts);
		}

		/// <summary>
		/// Gotten from https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa
		/// </summary>
		/// <param name="hex">The hexadecimal formatted input string to convert</param>
		/// <returns>The hexadecimal formatted input string converted to an array of bytes</returns>
		internal static byte[] StringToByteArray(string hex)
		{
			var len = hex.Length;
			var bytes = new byte[len / 2];

			for (var i = 0; i < len; i += 2)
				bytes[i / 2] = byte.Parse(hex.AsSpan(i, 2), NumberStyles.AllowHexSpecifier);

			return bytes;
		}

		/// <summary>
		/// Converts a script value to the bytes an API which works in bytes operates on.
		/// </summary>
		/// <param name="value">A String, <see cref="Keysharp.Builtins.Buffer"/>,
		/// <see cref="Keysharp.Builtins.Array"/> or byte array. Unset is no bytes at all.</param>
		/// <param name="enc">The encoding a string is taken in.</param>
		/// <returns>The bytes, or null if value is none of those. Null rather than an empty array, so that a
		/// caller which is not throwing reports nothing instead of operating on no bytes at all.</returns>
		/// <exception cref="TypeError">Thrown if value is none of those.</exception>
		internal static byte[] ToByteArray(object value, Encoding enc)
		{
			if (value is string s)
				return enc.GetBytes(s);

			if (value is byte[] b)
				return b;

			if (value is Keysharp.Builtins.Buffer buf)
				return buf.ToByteArray();

			if (value is Keysharp.Builtins.Array arr)
			{
				try
				{
					return arr.ToByteArray().ToArray();
				}
				catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
				{
					// An Array holds anything, and the conversion coerces each element: a non-numeric one raises
					// a .NET exception that no script can catch, so it is reported as the type error it is.
					_ = Errors.TypeErrorOccurred(value, typeof(byte[]));
					return null;
				}
			}

			if (value == null)
				return [];

			_ = Errors.TypeErrorOccurred(value, typeof(byte[]));
			return null;
		}

		internal static DateTime ToDateTime(string time, Calendar cal) => ToDateTime(time.AsSpan(), cal);

		internal static DateTime ToDateTime(ReadOnlySpan<char> time, Calendar cal)
		{
			var y = DateTime.Now.Year;
			int[] t = [y / 100, y % 100, 1, 1, 0, 0, 0, 0];
			int i, k;

			for (i = 0, k = 0; i < t.Length; i++, k += 2)
				if (k + 1 >= time.Length || !int.TryParse(time.Slice(k, 2), out t[i]))
					break;

			return i == 0 ? DateTime.MinValue : new DateTime((t[0] * 100) + t[1], t[2], t[3], t[4], t[5], t[6], cal);
		}

		internal static FileAttributes ToFileAttribs(string set, FileAttributes attribs)
		{
			var op = '=';
			var mask = 0u;
			var attrMask = new FileSetAttribData();
			attrMask.and_mask = 0xFFFFFFFF; // Set default: keep all bits.
			attrMask.xor_mask = 0; // Set default: affect none.

			foreach (var flag in set)
			{
				switch (flag)
				{
					case '+':
					case '-':
					case '^':
						op = flag;
						continue;

					case ' ':
					case '\t':
						continue;

					case 'r':
					case 'R':
						mask = (uint)FileAttributes.ReadOnly;
						break;

					case 'a':
					case 'A':
						mask = (uint)FileAttributes.Archive;
						break;

					case 's':
					case 'S':
						mask = (uint)FileAttributes.System;
						break;

					case 'h':
					case 'H':
						mask = (uint)FileAttributes.Hidden;
						break;

					case 'n':
					case 'N':
						mask = (uint)FileAttributes.Normal;
						break;

					//case 'd':
					//case 'D':
					//  mask = (uint)FileAttributes.Directory;
					//  break;

					case 'o':
					case 'O':
						mask = (uint)FileAttributes.Offline;
						break;

					//case 'c':
					//case 'C':
					//  mask = (uint)FileAttributes.Compressed;
					//  break;

					case 't':
					case 'T':
						mask = (uint)FileAttributes.Temporary;
						break;
				}

				switch (op)
				{
					case '+':
						attrMask.and_mask &= ~mask;//Reset bit to 0.
						attrMask.xor_mask |= mask;//Set bit to 1.
						break;

					case '-':
						attrMask.and_mask &= ~mask;//Reset bit to 0.
						attrMask.xor_mask &= ~mask;//Override any prior + or ^.
						break;

					case '^':
						attrMask.xor_mask ^= mask;//Toggle bit. ^= vs |= to invert any prior + or ^.
						//Leave and_mask as is, so any prior + or - will be inverted.
						break;

					default: //No +/-/^ specified, so overwrite attributes (equal and opposite to FileGetAttrib).
						attrMask.and_mask = 0;//Reset all bits to 0.
						attrMask.xor_mask |= mask;//Set bit to 1. |= to accumulate if multiple attributes are present.
						break;
				}
			}

			return (FileAttributes)(((uint)attribs & attrMask.and_mask) ^ attrMask.xor_mask);
		}

		internal static string[] ToFiles(string path, bool files, bool dirs, bool recurse)
		{
			if (string.IsNullOrEmpty(path))
				return [];

			var fullPath = Path.GetFullPath(path);
			var hasWildcard = fullPath.AsSpan().IndexOfAny("*?".AsSpan()) != -1;
			var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			var results = new List<string>();

			try
			{
				if (!hasWildcard)
				{
					if (System.IO.File.Exists(fullPath))
					{
						if (files)
							results.Add(fullPath);

						return results.ToArray();
					}

					if (System.IO.Directory.Exists(fullPath))
					{
						if (dirs)
							results.Add(fullPath);

						if (recurse)
						{
							if (files)
								results.AddRange(System.IO.Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories));

							if (dirs)
								results.AddRange(System.IO.Directory.GetDirectories(fullPath, "*", SearchOption.AllDirectories));
						}

						return results.ToArray();
					}

					// Treat as a literal path that currently does not exist.
					return [];
				}

				var root = Path.GetDirectoryName(fullPath);
				root = string.IsNullOrEmpty(root) ? "./" : root;
				var pattern = Path.GetFileName(fullPath);

				if (string.IsNullOrEmpty(pattern))
					pattern = "*";

				if (files)
					results.AddRange(System.IO.Directory.GetFiles(root, pattern, option));

				if (dirs)
					results.AddRange(System.IO.Directory.GetDirectories(root, pattern, option));
			}
			catch
			{
			}

			return results.ToArray();
		}

		internal static PcreRegexSettings ToRegexOptions(ReadOnlySpan<char> sequence)
		{
			var settings = new PcreRegexSettings();

			foreach (var modifier in sequence)
			{
				switch (modifier)
				{
					case 'i':
					case 'I':
						settings.Options |= PcreOptions.IgnoreCase;
						break;

					case 'm':
					case 'M':
						settings.Options |= PcreOptions.MultiLine;
						break;

					case 's':
						settings.Options |= PcreOptions.DotAll;
						break;

					case 'S':
						settings.Options |= PcreOptions.Singleline;
						break;

					case 'x':
						settings.Options |= PcreOptions.IgnorePatternWhitespace;
						break;

					case 'A':
						settings.Options |= PcreOptions.Anchored;
						break;

					case 'D':
						settings.Options |= PcreOptions.DollarEndOnly;
						break;

					case 'J':
						settings.Options |= PcreOptions.DupNames;
						break;

					case 'U':
						settings.Options |= PcreOptions.Ungreedy;
						break;

					case 'X':
						settings.Options |= PcreOptions.Extended;
						break;

					case 'c':
					case 'C':
						settings.Options |= PcreOptions.AutoCallout;
						break;

					case 'a':
						settings.NewLine = PcreNewLine.Any;
						break;

					case 'r':
						settings.NewLine = PcreNewLine.Cr;
						break;

					case 'n':
						settings.NewLine = PcreNewLine.Lf;
						break;

					case 'u':
						settings.Options |= PcreOptions.NoAutoPossess | PcreOptions.NoStartOptimize | PcreOptions.NoDotStarAnchor;
						break;
				}
			}

			return settings;
		}

		internal static RegexOptions ToRegexOptionsCs(ReadOnlySpan<char> sequence)
		{
			var options = RegexOptions.None;

			foreach (var modifier in sequence)
			{
				switch (modifier)
				{
					case 'i':
					case 'I':
						options |= RegexOptions.IgnoreCase;
						break;

					case 'm':
					case 'M':
						options |= RegexOptions.Multiline;
						break;

					case 's':
					case 'S':
						options |= RegexOptions.Singleline;
						break;

					case 'x':
					case 'X':
						options |= RegexOptions.IgnorePatternWhitespace;
						break;
				}
			}

			return options;
		}


#if WINDOWS
		internal static (RegistryKey, string, string) ToRegKey(string root, bool writable = false)
		{
			var (reg, comp, key) = ToRegRootKey(root);
			var regkey = reg.OpenSubKey(key, writable);

			if (regkey == null)
				regkey = reg.CreateSubKey(key, writable);

			return (regkey, comp, key);
		}

		internal static (RegistryKey, string, string) ToRegRootKey(string name)
		{
			var computername = "";

			if (name.StartsWith("\\\\"))
			{
				var index = name.IndexOf("\\", 2);
				computername = name.Substring(2, index - 2);
				name = name.Substring(index + 1);
			}

			var index2 = name.IndexOf("\\");
			var root = name.Substring(0, index2).ToLowerInvariant();
			var key = name.Substring(index2 + 1);

			(RegistryKey, string, string) HandleError()//Hack to work around pattern matching switch statements not supporting multiple lines in the default case.
			{
				_ = Errors.ValueErrorOccurred($"{root} was not a valid registry type.");
				return (default, default, default);
			}

			return root switch
			{
				Keyword_HKey_Local_Machine or Keyword_HKLM => (RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Registrys.GetRegView()), computername, key),
				Keyword_HKey_Users or Keyword_HKU => (RegistryKey.OpenBaseKey(RegistryHive.Users, Registrys.GetRegView()), computername, key),
				Keyword_HKey_Current_User or Keyword_HKCU => (RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, Registrys.GetRegView()), computername, key),
				Keyword_HKey_Classes_Root or Keyword_HKCR => (RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, Registrys.GetRegView()), computername, key),
				Keyword_HKey_Current_Config or Keyword_HKCC => (RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, Registrys.GetRegView()), computername, key),
				Keyword_HKey_Performance_Data or Keyword_HKPD => (RegistryKey.OpenBaseKey(RegistryHive.PerformanceData, Registrys.GetRegView()), computername, key),
				_ => HandleError()
			};
		}
#endif
		internal static string ToStringCaseSense(StringComparison type)
		{

			return type switch
		{
				StringComparison.CurrentCultureIgnoreCase => Keyword_Locale,
				StringComparison.Ordinal => Keyword_On,
				StringComparison.OrdinalIgnoreCase => Keyword_Off,
				_ => Keyword_Off,
		};
	}

	internal static string ToYYYYMMDDHH24MISS(DateTime time) => time.ToString("yyyyMMddHHmmss");

		internal static string ToYYYYMMDDHH24MISSFFF(DateTime time) => time.ToString("yyyyMMddHHmmss.fff");

		internal static bool TryParseColor(string name, out Color c)
		{
			var isnum = false;
			name = name.Trim();

			if (name.Length > 0 && name[0] == '#')
			{
				isnum = true;
				name = name.Substring(1);
			}
			else if (name.Length > 1 && name[0] == '0' && (name[1] == 'x' || name[1] == 'X'))
			{
				isnum = true;
				name = name.Substring(2);
			}
			else if (name.AllHex())
				isnum = true;

			if (isnum)
			{
				if (uint.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var val))
				{
					if (name.Length <= 6)
						val |= 0xFF000000;

					c = Color.FromArgb((int)val);
					return true;
				}
			}

			if (string.Compare(name, "default", true) == 0)
			{
				c = Control.DefaultForeColor;
				return true;
			}

			c = Color.FromName(name);
			return c.IsKnownColor;
		}

		internal struct FileSetAttribData
		{
			internal uint and_mask, xor_mask;
		}
	}
}
