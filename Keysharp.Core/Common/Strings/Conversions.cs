using SearchOption = System.IO.SearchOption;
using Calendar = System.Globalization.Calendar;

namespace Keysharp.Core.Common.Strings
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

		internal static ToggleValueType ConvertOnOff(object mode, ToggleValueType def = ToggleValueType.Invalid)
		{
			if (mode == null)
				return ToggleValueType.Neutral;

			var str = mode.ToString();

			if (str?.Length == 0)
				return ToggleValueType.Neutral;

			if (string.Compare(str, "true", true) == 0 || string.Compare(str, "on", true) == 0 || str == "1") return ToggleValueType.On;

			if (string.Compare(str, "false", true) == 0 || string.Compare(str, "off", true) == 0 || str == "0") return ToggleValueType.Off;

			return def;
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
			var size = standard.Size;
			var display = standard.Style;
			var weight = 400;
			var quality = 0;

			foreach (Range r in styles.AsSpan().SplitAny(Spaces))
			{
				var opt = styles.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (Options.TryParse(opt, "s", ref size)) { }
					else if (Options.TryParse(opt, "q", ref quality)) { }
					else if (Options.TryParse(opt, "w", ref weight))
					{
						if (weight <= 400)
							display &= ~FontStyle.Bold;
						else if (weight >= 700)
							display |= FontStyle.Bold;
					}

					switch (opt)
					{
						case var b when opt.Equals(Keyword_Bold, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Bold;
							break;

						case var b when opt.Equals(Keyword_Italic, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Italic;
							break;

						case var b when opt.Equals(Keyword_Strike, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Strikeout;
							break;

						case var b when opt.Equals(Keyword_Underline, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Underline;
							break;

						case var b when opt.Equals(Keyword_Norm, StringComparison.OrdinalIgnoreCase):
							display = FontStyle.Regular;
							break;
					}
				}
			}

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
			var size = standard.Size;
			var display = standard.FontStyle;
			var weight = 400;
			var quality = 0;
			var decorations = FontDecoration.None;

			foreach (Range r in styles.AsSpan().SplitAny(Spaces))
			{
				var opt = styles.AsSpan(r).Trim();

				if (opt.Length > 0)
				{
					if (Options.TryParse(opt, "s", ref size)) { }
					else if (Options.TryParse(opt, "q", ref quality)) { }
					else if (Options.TryParse(opt, "w", ref weight))
					{
						if (weight <= 400)
							display &= ~FontStyle.Bold;
						else if (weight >= 700)
							display |= FontStyle.Bold;
					}

					switch (opt)
					{
						case var b when opt.Equals(Keyword_Bold, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Bold;
							break;

						case var b when opt.Equals(Keyword_Italic, StringComparison.OrdinalIgnoreCase):
							display |= FontStyle.Italic;
							break;

						case var b when opt.Equals(Keyword_Strike, StringComparison.OrdinalIgnoreCase):
							decorations |= FontDecoration.Strikethrough;
							break;

						case var b when opt.Equals(Keyword_Underline, StringComparison.OrdinalIgnoreCase):
							decorations |= FontDecoration.Underline;
							break;

						case var b when opt.Equals(Keyword_Norm, StringComparison.OrdinalIgnoreCase):
							decorations = FontDecoration.None;
							break;
					}
				}
			}

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
			var filelist = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path),
											  recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

			if (dirs)
			{
				var dirlist = new HashSet<string>();

				foreach (var file in filelist)
					_ = dirlist.Add(Path.GetDirectoryName(file));

				if (files)
				{
					int i;
					var merge = new string[dirlist.Count + filelist.Length];

					for (i = 0; i < filelist.Length; i++)
						merge[i] = filelist[i];

					foreach (var dir in dirlist)
						merge[i++] = dir;

					return merge;
				}
				else return dirlist.ToArray();
			}
			else if (files)
				return filelist;

			return [];
		}

		internal static string ToOSType(PlatformID id)
		{
			return id switch
			{
					PlatformID.MacOSX => "MACOSX",
					PlatformID.Unix => "UNIX",
					PlatformID.Win32NT => "WIN32_NT",
					PlatformID.Win32S => "WIN32_S",
					PlatformID.Win32Windows => "WIN32_WINDOWS",
					PlatformID.WinCE => "WINCE",
					PlatformID.Xbox => "XBOX",
					_ => "UNKNOWN",
			};
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
				if (uint.TryParse(name, NumberStyles.HexNumber, Parser.inv, out var val))
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
