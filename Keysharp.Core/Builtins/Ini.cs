namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for Ini-related functions.<br/>
	/// On Windows these call the native profile functions, as AutoHotkey does. Elsewhere they follow those functions'
	/// rules and edit the file's lines in place, so that comments, blank lines and formatting survive a write.
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
			if (!filename.CoerceString(out var file) || !section.CoerceString(out var s) || !key.CoerceString(out var k))
				return DefaultObject;

			file = Path.GetFullPath(file);

			if (!File.Exists(file))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return DefaultObject;
			}

#if WINDOWS
			bool ok = WindowsAPI.WritePrivateProfileString(s, key == null ? null : k, null, file);
			ThreadAccessors.A_LastError = Marshal.GetLastWin32Error();
			_ = WindowsAPI.WritePrivateProfileString(null, null, null, file);

			return ok ? DefaultObject : Errors.OSErrorOccurred(new Win32Exception(unchecked((int)ThreadAccessors.A_LastError)),
				$"Error deleting {(key == null ? "section" : "key")} '{k}' from INI '{file}'");

#else

			ThreadAccessors.A_LastError = 0;

			try
			{
				var lines = IniLoad(file, out var encoding, out var newLine);
				var header = IniFindSection(lines, s, out var end, out var contentEnd);

				if (header < 0)
					return DefaultObject;

				// As WritePrivateProfileString does, a section goes with its entries and comments but not the blank lines after them.
				if (key == null)
					lines.RemoveRange(header, contentEnd - header);
				else if (IniFindKey(lines, header + 1, end, k, out _) is var at and >= 0)
					lines.RemoveAt(at);
				else
					return DefaultObject;

				IniSave(file, lines, encoding, newLine);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error deleting {(key == null ? "section" : "key")} '{k}' from INI '{file}'");
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
			if (!filename.CoerceString(out var file) || !section.CoerceString(out var s) || !key.CoerceString(out var k) || !@default.CoerceString(out var def))
				return DefaultObject;

			file = Path.GetFullPath(file);

			if (!File.Exists(file))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return @default != null ? def : Errors.OSErrorOccurred("", $"INI file '{file}' not found.");
			}

			bool hasKey = key != null;
			bool hasSec = section != null;

			if (hasKey && !hasSec)
				return Errors.OSErrorOccurred("", "Section name required when reading a single key.");

#if WINDOWS
			// AutoHotkey's limit, in a pooled buffer because a new one this size would land on the large object heap.
			const uint BUF_SIZE = 65535;
			var buf = ArrayPool<char>.Shared.Rent((int)BUF_SIZE);
			try
			{
				// The profile API can write to an empty section name in a UTF-16 file, so give it a private string.
				var read = hasKey ? WindowsAPI.GetPrivateProfileString(s.Length == 0 ? new string('\0', 1) : s, k, def, buf, BUF_SIZE, file)
					: hasSec ? WindowsAPI.GetPrivateProfileSection(s, buf, BUF_SIZE, file)
					: WindowsAPI.GetPrivateProfileSectionNames(buf, BUF_SIZE, file);
				var err = Marshal.GetLastWin32Error();
				ThreadAccessors.A_LastError = err;

				if (err != 0)
				{
					var item = hasKey ? $"key '{k}' in section '{s}'" : hasSec ? $"section '{s}'" : "sections";
					return @default != null ? def : Errors.OSErrorOccurred(new Win32Exception(err), $"Failed to read {item} from '{file}' (0x{err:X}).");
				}

				return hasKey ? new string(buf, 0, (int)read) : MultiStringToLines(buf, read);
			}
			finally
			{
				ArrayPool<char>.Shared.Return(buf);
			}

#else
			ThreadAccessors.A_LastError = 0;
			List<string> lines;

			try
			{
				lines = IniLoad(file, out _, out _);
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return @default != null ? def : Errors.OSErrorOccurred(ex, $"Error reading INI '{file}'");
			}

			var sb = new StringBuilder();

			if (!hasSec)
			{
				foreach (var line in lines)
				{
					if (IniSectionName(line, out var name))
						_ = sb.Append(name).Append('\n');
				}

				return sb.ToString().TrimEnd('\n');
			}

			var header = IniFindSection(lines, s, out var end, out _);

			if (header >= 0)
			{
				if (!hasKey)
				{
					// As GetPrivateProfileSection does, the entries without comments and blank lines, each key=value trimmed.
					for (var i = header + 1; i < end; i++)
					{
						var text = lines[i].AsSpan().Trim();

						if (text.IsEmpty || text[0] == ';')
							continue;

						if (IniEntry(text, out var entryName, out var entryValue))
							_ = sb.Append(entryName).Append('=').Append(entryValue);
						else
							_ = sb.Append(text);

						_ = sb.Append('\n');
					}

					return sb.ToString().TrimEnd('\n');
				}

				if (IniFindKey(lines, header + 1, end, k, out var found) >= 0)
				{
					// As GetPrivateProfileString does, a value enclosed in matching quotes loses them.
					if (found.Length >= 2 && found[0] is '"' or '\'' && found[^1] == found[0])
						found = found[1..^1];

					return found.ToString();
				}
			}

			ThreadAccessors.A_LastError = 2;//ERROR_FILE_NOT_FOUND, as the native functions report it.
			return @default != null ? def : Errors.OSErrorOccurred("", "The requested key, section or file was not found.");
#endif
		}

#if WINDOWS
		private static string MultiStringToLines(char[] buf, uint length)
		{
			var text = buf.AsSpan(0, (int)length);
			var end = text.IndexOf("\0\0");
			return (end < 0 ? text.TrimEnd('\0') : text[..end]).ToString().Replace('\0', '\n');
		}
#endif

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
			if (!value.CoerceString(out var v) || !filename.CoerceString(out var file) || !section.CoerceString(out var s) || !key.CoerceString(out var k))
				return DefaultObject;

#if WINDOWS
			// On Windows use the native INI APIs directly:
			file = Path.GetFullPath(file);
			bool ok;

			if (!File.Exists(file))
			{
				// As in AutoHotkey, a new file starts as UTF-16 with a byte order mark, without which the profile functions
				// write the ANSI code page, and with the section header, before which they would add a blank line.
				try
				{
					File.WriteAllText(file, $"[{s}]", Encoding.Unicode);
				}
				catch (Exception ex)
				{
					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred(ex, $"Error creating INI '{file}'");
				}
			}

			ok = key != null ? WindowsAPI.WritePrivateProfileString(s, k, v, file)
				: WindowsAPI.WritePrivateProfileSection(s, v.Replace('\n', '\0') + "\0\0", file);

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
					$"Error writing {(key == null ? "section" : "key")} to INI '{file}'"
				);
			}

#else
			ThreadAccessors.A_LastError = 0;
			file = Path.GetFullPath(file);

			try
			{
				// A new file gets UTF-8 without a byte order mark, the usual encoding of text files outside Windows.
				Encoding encoding = new UTF8Encoding(false);
				var newLine = "\n";
				var lines = File.Exists(file) ? IniLoad(file, out encoding, out newLine) : [];
				var header = IniFindSection(lines, s, out var end, out var contentEnd);

				if (header < 0)
				{
					lines.Add($"[{s}]");
					header = lines.Count - 1;
					end = contentEnd = lines.Count;
				}

				if (key != null)
				{
					// As WritePrivateProfileString does, an existing entry keeps its spelling up to the '=', and a new
					// one follows the last line of the section that is not blank.
					if (IniFindKey(lines, header + 1, end, k, out _) is var at and >= 0)
						lines[at] = string.Concat(lines[at].AsSpan(0, lines[at].IndexOf('=') + 1), v);
					else
						lines.Insert(contentEnd, $"{k}={v}");
				}
				else
				{
					var pairs = new List<string>();

					foreach (var range in v.AsSpan().Split('\n'))
					{
						var pair = v.AsSpan(range).TrimEnd('\r');

						// The native function takes the pairs as a list that an empty string ends.
						if (pair.IsEmpty)
							break;

						pairs.Add(pair.ToString());
					}

					// As WritePrivateProfileSection does, the pairs replace everything up to the section's last line that is not blank.
					lines.RemoveRange(header + 1, contentEnd - header - 1);
					lines.InsertRange(header + 1, pairs);
				}

				IniSave(file, lines, encoding, newLine);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error writing {(key == null ? "section" : "key")} to INI '{file}'");
			}

#endif
		}

#if !WINDOWS
		/// <summary>
		/// The lines of an .ini file, with the encoding and line break it uses so that a rewrite keeps them.
		/// </summary>
		private static List<string> IniLoad(string file, out Encoding encoding, out string newLine)
		{
			string text;

			using (var reader = new StreamReader(file, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
			{
				text = reader.ReadToEnd();
				encoding = reader.CurrentEncoding;
			}

			newLine = text.Contains("\r\n") ? "\r\n" : "\n";
			var lines = new List<string>();

			foreach (var range in text.AsSpan().Split('\n'))
				lines.Add(text.AsSpan(range).TrimEnd('\r').ToString());

			// The break that ends the last line leaves an empty piece behind it.
			if (lines[^1].Length == 0)
				lines.RemoveAt(lines.Count - 1);

			return lines;
		}

		/// <summary>
		/// Writes the lines to a temporary file beside the .ini file and moves it over the file, so that a failure leaves
		/// the old contents whole. A symbolic link keeps pointing to the edited target.
		/// </summary>
		private static void IniSave(string file, List<string> lines, Encoding encoding, string newLine)
		{
			var info = new FileInfo(file);

			if (info.LinkTarget != null)
				file = info.ResolveLinkTarget(returnFinalTarget: true).FullName;

			var temp = file + "." + Path.GetRandomFileName();

			try
			{
				File.WriteAllText(temp, string.Join(newLine, lines) + newLine, encoding);

				// The platform test is for the analyzer: this branch is compiled only off Windows.
				if (!OperatingSystem.IsWindows() && File.Exists(file))
					File.SetUnixFileMode(temp, File.GetUnixFileMode(file));

				File.Move(temp, file, overwrite: true);
			}
			catch
			{
				File.Delete(temp);
				throw;
			}
		}

		/// <summary>
		/// The index of the first section header with the given name, compared as the native functions compare it, or -1.
		/// end receives the index of the next header or the line count, and contentEnd the index after the section's last
		/// line that is not blank.
		/// </summary>
		private static int IniFindSection(List<string> lines, string name, out int end, out int contentEnd)
		{
			var header = -1;
			end = lines.Count;

			for (var i = 0; i < lines.Count; i++)
			{
				if (!IniSectionName(lines[i], out var sectionName))
					continue;

				if (header >= 0)
				{
					end = i;
					break;
				}

				if (sectionName.Equals(name, StringComparison.OrdinalIgnoreCase))
					header = i;
			}

			contentEnd = end;

			while (contentEnd > header + 1 && string.IsNullOrWhiteSpace(lines[contentEnd - 1]))
				contentEnd--;

			return header;
		}

		/// <summary>
		/// The index of the first entry named key between the lines start and end, compared as the native functions
		/// compare it, or -1.
		/// </summary>
		private static int IniFindKey(List<string> lines, int start, int end, string key, out ReadOnlySpan<char> value)
		{
			for (var i = start; i < end; i++)
			{
				var text = lines[i].AsSpan().Trim();

				// A comment is no entry.
				if (text is not [';', ..] && IniEntry(text, out var name, out value) && name.Equals(key, StringComparison.OrdinalIgnoreCase))
					return i;
			}

			value = default;
			return -1;
		}

		/// <summary>
		/// Splits a trimmed key=value line at its first '=' and trims both sides. A line without '=' is no entry.
		/// </summary>
		private static bool IniEntry(ReadOnlySpan<char> text, out ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
		{
			var equals = text.IndexOf('=');

			name = equals < 0 ? default : text[..equals].TrimEnd();
			value = equals < 0 ? default : text[(equals + 1)..].TrimStart();
			return equals >= 0;
		}

		/// <summary>
		/// The trimmed name of a [name] line.
		/// </summary>
		private static bool IniSectionName(string line, out ReadOnlySpan<char> name)
		{
			var text = line.AsSpan().Trim();
			var close = text.IndexOf(']');

			if (text.IsEmpty || text[0] != '[' || close < 1)
			{
				name = default;
				return false;
			}

			name = text[1..close].Trim();
			return true;
		}
#endif
	}
}
