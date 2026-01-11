namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for Ini-related functions.<br/>
	/// Although the Windows API does provide functions for manipulating INI files, they are not cross platform.<br/>
	/// So the code here provides cross platform INI manipulation functions in pure C#.
	/// </summary>
	public static class Ini
	{
		/// <summary>
		/// Deletes a value from a standard format .ini file.
		/// </summary>
		/// <param name="filename">The name of the .ini file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="section">The section name in the .ini file, which is the heading phrase that appears in square brackets (do not include the brackets in this parameter).</param>
		/// <param name="key">If omitted, the entire section will be deleted. Otherwise, specify the key name in the .ini file.</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any file errors occur.</exception>
		public static object IniDelete(object filename, object section, object key = null)
		{
			var file = filename.As();
			var s = section.As();
			var k = key.As();
			file = Path.GetFullPath(file);

			if (!File.Exists(file))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return DefaultErrorObject;
			}

#if WINDOWS
			// Pass null for key to delete whole section; or pass the key to delete only that entry
			bool ok = WindowsAPI.WritePrivateProfileString(s, string.IsNullOrEmpty(k) ? null : k, null, file);
			// Flush the in-memory cache
			WindowsAPI.WritePrivateProfileString(null, null, null, file);
			ThreadAccessors.A_LastError = Marshal.GetLastWin32Error();

			if (ok)
				return DefaultObject;
			else
			{
				return Errors.OSErrorOccurred(
						   new Win32Exception(unchecked((int)ThreadAccessors.A_LastError)),
						   $"Error deleting {(string.IsNullOrEmpty(k) ? "section" : "key")} '{k}' from INI '{file}'"
					   );
			}

#else

			ThreadAccessors.A_LastError = 0;

			if (s != "")
				s = string.Format(Keyword_IniSectionOpen + "{0}]", s);

			try
			{
				var haskey = !string.IsNullOrEmpty(k);
				var hassec = !string.IsNullOrEmpty(s);
				var sb = new StringBuilder(1024);
				var writer = new StringWriter();
				var inidkt = IniLoad(file);

				if (hassec && haskey)
				{
					if (inidkt.Contains(s))
					{
						var secdkt = inidkt[s] as OrderedDictionary;
						secdkt.Remove(k);
					}
				}
				else if (hassec)
					inidkt.Remove(s);

				foreach (DictionaryEntry kv in inidkt)
				{
					writer.WriteLine(kv.Key);

					foreach (DictionaryEntry kv2 in (OrderedDictionary)kv.Value)
						if (((string)kv2.Key)[0] != ';')
							writer.WriteLine($"{kv2.Key}={kv2.Value}");
						else
							writer.WriteLine($"{kv2.Key}");

					writer.WriteLine();
				}

				writer.Flush();
				var text = writer.ToString();

				if (File.Exists(file))
					File.Delete(file);

				_ = Files.FileAppend("", file, "unicode");
				File.WriteAllText(file, text);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.ErrorOccurred(ex.Message);
			}

#endif
		}

		/// <summary>
		/// Reads a value from a standard format .ini file.
		/// </summary>
		/// <param name="filename">The name of the .ini file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="section">The section name in the .ini file, which is the heading phrase that appears in square brackets (do not include the brackets in this parameter).</param>
		/// <param name="key">The key name in the .ini file.</param>
		/// <param name="default">If omitted, an <see cref="OSError"/> is thrown on failure. Otherwise, specify the value to return on failure, such as if the requested key, section or file is not found.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the key can't be found and no default is supplied.</exception>
		public static object IniRead(object filename, object section = null, object key = null, object @default = null)
		{
			var file = filename.As();
			var s = section.As();
			var k = key.As();
			var def = @default.As();
			var result = "";
			file = Path.GetFullPath(file);

			if (!File.Exists(file))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return def ?? DefaultErrorObject;
			}

#if WINDOWS
			const uint BUF_SIZE = 65535;
			uint read;
			bool hasKey = !string.IsNullOrEmpty(k);
			bool hasSec = !string.IsNullOrEmpty(s);

			if (hasKey)
			{
				// must specify section
				if (!hasSec)
					return Errors.OSErrorOccurred("", "Section name required when reading a single key.");

				var chars = new char[BUF_SIZE];
				// lpAppName = section (no brackets), wParam default = def
				read = WindowsAPI.GetPrivateProfileString(s, k, def, chars, BUF_SIZE, file);
				// error or not found?
				var err = Marshal.GetLastWin32Error();
				ThreadAccessors.A_LastError = err;

				if (err != 0 || read < 0)
					return @default != null ? def : Errors.OSErrorOccurred(new Win32Exception(err), $"Failed to read key '{k}' in section '{s}' from '{file}' (0x{err:X}).");

				result = new string(chars, 0, (int)read);
			}
			else if (hasSec)
			{
				// read entire section: returns a double-null–terminated list of "key=value" entries
				var buf = new char[BUF_SIZE];
				read = WindowsAPI.GetPrivateProfileSection(s, buf, BUF_SIZE, file);
				var err = Marshal.GetLastWin32Error();
				ThreadAccessors.A_LastError = err;

				if (err != 0)
					return @default != null ? def : Errors.OSErrorOccurred(new Win32Exception(err), $"Failed to read section '{s}' from '{file}' (0x{err:X}).");

				// convert double-null list into lines
				result = MultiStringToLines(buf, read);
			}
			else
			{
				// no section/key → list all section names
				var buf = new char[BUF_SIZE];
				read = WindowsAPI.GetPrivateProfileSectionNames(buf, BUF_SIZE, file);
				var err = Marshal.GetLastWin32Error();
				ThreadAccessors.A_LastError = err;

				if (err != 0)
					return @default != null ? def : Errors.OSErrorOccurred(new Win32Exception(err), $"Failed to list sections in '{file}' (0x{err:X}).");

				result = MultiStringToLines(buf, read);
			}

			return result;
#else

			ThreadAccessors.A_LastError = 0;
			if (s != "")
				s = $"[{s}]";

			var haskey = !string.IsNullOrEmpty(k);
			var hassec = !string.IsNullOrEmpty(s);
			var sb = new StringBuilder(1024);
			var inidkt = IniLoad(file);

			if (!haskey && !hassec)
			{
				foreach (DictionaryEntry kv in inidkt)
					_ = sb.AppendLine(((string)kv.Key).Trim(TrimSec));
			}
			else if (haskey && hassec)
			{
				var secdkt = inidkt.GetOrAdd<string, OrderedDictionary, IEqualityComparer>(s, StringComparer.CurrentCultureIgnoreCase);

				if (secdkt.Contains(k))
				{
					var val = secdkt.GetOrAdd<string, string, string>(k, def);
					_ = sb.Append(val);
				}
				else if (def.Length > 0)
					_ = sb.Append(def);
				else 
				{
					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred("", $"Failed to find key {k} in section {s} in INI file {file}.");
				}
			}
			else if (hassec)
			{
				var secdkt = inidkt.GetOrAdd<string, OrderedDictionary, IEqualityComparer>(s, StringComparer.CurrentCultureIgnoreCase);

				foreach (DictionaryEntry kv in secdkt)
					if (((string)kv.Key)[0] != ';')
						_ = sb.Append($"{kv.Key}={kv.Value}\n");
			}

			result = sb.ToString().TrimEnd('\n');
			return result;
#endif
		}

		/// <summary>
		/// Convert a double-null–terminated char[] into '\n'-delimited string.
		/// </summary>
		/// <param name="buf">The multiline buffer to convert.</param>
		/// <param name="length">The length of buf to process.</param>
		/// <returns>The new string delimited by '\n'.</returns>
		private static string MultiStringToLines(char[] buf, uint length)
		{
			var sb = new StringBuilder((int)length);
			int i = 0;

			while (i < length)
			{
				var start = i;

				while (i < length && buf[i] != '\0')
					i++;

				if (i == start)
					break; // two nulls in a row = end

				_ = sb.Append(buf, start, i - start);
				_ = sb.Append('\n');
				i++;
			}

			return sb.ToString().TrimEnd('\n');
		}

		/// <summary>
		/// Writes a value to a standard format .ini file.
		/// </summary>
		/// <param name="value">The string or number that will be written to the right of <paramref name="key"/>'s equal sign (=).
		/// or
		/// The complete content of a section to write to the .ini file, excluding the [SectionName] header.<br/>
		/// Key must be omitted. Pairs must not contain any blank lines. If the section already exists, everything up to the last key=value pair is overwritten.<br/>
		/// Pairs can contain lines without an equal sign (=), but this may produce inconsistent results.<br/>
		/// Comments can be written to the file but are stripped out when they are read back by <see cref="IniRead"/>.
		/// </param>
		/// <param name="filename">The name of the .ini file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="section">The section name in the .ini file, which is the heading phrase that appears in square brackets (do not include the brackets in this parameter).</param>
		/// <param name="key">The key name in the .ini file.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object IniWrite(object value, object filename, object section, object key = null)
		{
			var v = value.As();
			var file = filename.As();
			var s = section.As();
			var k = key.As();
#if WINDOWS
			// On Windows use the native INI APIs directly:
			file = Path.GetFullPath(file);
			bool ok;

			if (!File.Exists(file))
			{
				// Ensure the file exists so WritePrivateProfile* won’t fail
				File.WriteAllText(file, "\uFEFF");  // BOM to hint Unicode
			}

			if (!string.IsNullOrEmpty(k))
			{
				// single key
				ok = WindowsAPI.WritePrivateProfileString(s, k, v, file);
			}
			else
			{
				// whole section; convert "\n"-delimited to "\0"-delimited
				// and append double-null terminator
				var lines = v.Split('\n', StringSplitOptions.None);
				var sb = new StringBuilder();

				foreach (var line in lines)
				{
					sb.Append(line.TrimEnd('\r'));
					sb.Append('\0');
				}

				sb.Append('\0');  // extra null for end-of-section
				ok = WindowsAPI.WritePrivateProfileSection(s, sb.ToString(), file);
			}

			if (ok)
			{
				// flush the cache
				WindowsAPI.WritePrivateProfileString(null, null, null, file);
				ThreadAccessors.A_LastError = Marshal.GetLastWin32Error();
				return DefaultObject;
			}
			else
			{
				var err = Marshal.GetLastWin32Error();
				ThreadAccessors.A_LastError = err;
				return Errors.OSErrorOccurred(
					new System.ComponentModel.Win32Exception(err),
					$"Error writing {(string.IsNullOrEmpty(k) ? "section" : "key")} to INI '{file}'"
				);
			}

#else
			ThreadAccessors.A_LastError = 0;
			var within = string.IsNullOrEmpty(s);
			s = string.Format("[{0}]", s ?? string.Empty);
			var haskey = !string.IsNullOrEmpty(k);
			var writer = new StringWriter();

			try
			{
				if (!File.Exists(file))
				{
					writer.WriteLine(s);

					if (haskey)
						writer.WriteLine($"{k}={v}");
					else
						writer.WriteLine(v);
				}
				else
				{
					var inidkt = IniLoad(file);

					if (s != "")
					{
						var kvdkt = inidkt.GetOrAdd<string, OrderedDictionary, IEqualityComparer>(s, StringComparer.CurrentCultureIgnoreCase);

						if (haskey)
						{
							kvdkt[k] = v;
						}
						else
						{
							kvdkt.Clear();//Documentation seems to suggest it should overwrite all in the specified section.

							foreach (Range r in v.AsSpan().SplitAny(TrimLine))
							{
								var pair = v.AsSpan(r).Trim();

								if (pair.Length > 0)
								{
									var equalsIndex = pair.IndexOf('=');

									if (equalsIndex != -1 && equalsIndex != pair.Length - 1)
									{
										var first = pair.Slice(0, equalsIndex);
										var second = pair.Slice(equalsIndex + 1);
										kvdkt[first.Trim(TrimLine).ToString()] = second.Trim(TrimLine).ToString();
									}
								}
							}
						}
					}

					foreach (DictionaryEntry kv in inidkt)
					{
						writer.WriteLine(kv.Key);

						foreach (DictionaryEntry kv2 in (OrderedDictionary)kv.Value)
							if (((string)kv2.Key)[0] != ';')
								writer.WriteLine($"{kv2.Key}={kv2.Value}");
							else
								writer.WriteLine($"{kv2.Key}");

						writer.WriteLine();
					}
				}

				writer.Flush();
				var text = writer.ToString().TrimEnd('\n', '\r');

				if (File.Exists(file))
					File.Delete(file);

				_ = Files.FileAppend("", file, "unicode");
				File.WriteAllText(file, text);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error writing key {k} with value {v} in section {s} to INI file {file}.");
			}

#endif
		}

#if !WINDOWS
		/// <summary>
		/// Private helper to load an .ini file.
		/// </summary>
		/// <param name="filename">The name of the .ini file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <returns>An <see cref="OrderedDictionary"/> with all of the file data in it.</returns>
		private static OrderedDictionary IniLoad(object filename)
		{
			var f = filename.As();
			OrderedDictionary kvdkt = null;
			var inidkt = new OrderedDictionary(StringComparer.CurrentCultureIgnoreCase);

			foreach (var line in File.ReadLines(f))
			{
				var ln = line.Trim(TrimLine);

				if (ln.Length > 0)
				{
					var split = ln.Split('=').Select(l => l.Trim(TrimLine)).ToArray();

					if (ln[0] == ';')
					{
						if (kvdkt == null)
							_ = inidkt.GetOrAdd<string, OrderedDictionary, IEqualityComparer>(ln, StringComparer.CurrentCultureIgnoreCase);
						else
							kvdkt[ln] = "";
					}
					else
					{
						if (split.Length == 1)
							kvdkt = inidkt.GetOrAdd<string, OrderedDictionary, IEqualityComparer>(split[0], StringComparer.CurrentCultureIgnoreCase);
						else if (split.Length == 2 && kvdkt != null)
							kvdkt[split[0]] = split[1];
					}
				}
			}

			return inidkt;
		}
#endif
	}
}