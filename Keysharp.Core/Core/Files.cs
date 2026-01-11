namespace Keysharp.Core
{
	/// <summary>
	/// Public interface for file-related functions.
	/// </summary>
	public static class Files
	{
		private static readonly SearchValues<char> wildcardsSv = SearchValues.Create("*?");

		/// <summary>
		/// Writes text to the end of a file, creating it first if necessary.
		/// </summary>
		/// <param name="text">If blank or omitted, filename will be created as an empty file (but if the file already exists, its modification time will be updated).<br/>
		/// Otherwise, specify the text or raw binary data to append to the file.<br/>
		/// A <see cref="Buffer"/>-like object may be passed to append raw binary data.<br/>
		/// If a file is created, a byte order mark (BOM) is written only if "UTF-8" or "UTF-16" has been specified within options.<br/>
		/// The default encoding is ignored and the data contained by the object is written as-is, regardless of options.<br/>
		/// Any object which implements Ptr and Size properties may be used.
		/// </param>
		/// <param name="filename">the name of the file to use<br/>
		/// If omitted, the output file of the innermost enclosing file-reading loop will be used (if available).<br/>
		/// Otherwise, specify the name of the file to be appended, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// The destination directory must already exist.<br/>
		/// Standard Output (stdout): Specifying an asterisk (*) for filename causes Text to be sent to standard output (stdout).<br/>
		/// Specifying two asterisks (**) for filename causes Text to be sent to the standard error stream (stderr).
		/// </param>
		/// <param name="options">Zero or more of the following strings. Separate each option from the next with a single space or tab.<br/>
		/// Encoding: Specify any of the encoding names accepted by FileEncoding (excluding the empty string) to use that encoding if the file<br/>
		/// lacks a UTF-8 or UTF-16 byte order mark. If omitted, it defaults to <see cref="A_FileEncoding"/> (unless Text is an object, in which case no byte order mark is written).
		/// RAW: Specify the word RAW (case-insensitive) to write the exact bytes contained by Text to the file as-is, without any conversion.<br/>
		/// This option overrides any previously specified encoding and vice versa.<br/>
		/// If text is not an object, the data size is always a multiple of 2 bytes due to the use of UTF-16 strings.<br/>
		/// `n (a linefeed character): Inserts a carriage return (`r) before each linefeed (`n) if one is not already present.<br/>
		/// In other words, it translates from `n to `r`n. This translation typically does not affect performance.<br/>
		/// If this option is not used, line endings within text are not changed.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any errors occur.</exception>
		public static object FileAppend(object text, object filename = null, object options = null)
		{
			var file = filename.As();
			ThreadAccessors.A_LastError = 0;

			//if (text.ToString() != "pass")
			//  Console.WriteLine(text);

			try
			{
				if (text == null)
					throw new Keysharp.Core.ValueError("Missing a required parameter: Text");
				var t = text;
				var encoding = ThreadAccessors.A_FileEncodingRaw;
				var raw = false;
				var crlf = false;
				TextWriter tw = null;

				if (options.As() is string opts)
				{
					foreach (Range r in opts.AsSpan().SplitAny(SpaceTabSv))
					{
						var split = opts.AsSpan(r).Trim(SpaceTab);//Need to supply chars to trim else \n would get automatically trimmed.

						if (split.Length > 0)
						{
							switch (split)
							{
								case var b when split.Equals("ascii", StringComparison.OrdinalIgnoreCase):
								case var b2 when split.Equals("us-ascii", StringComparison.OrdinalIgnoreCase):
									encoding = Encoding.ASCII;
									break;

								case var b when split.Equals("utf-8", StringComparison.OrdinalIgnoreCase):
									encoding = Encoding.UTF8;
									break;

								case var b when split.Equals("utf-8-raw", StringComparison.OrdinalIgnoreCase):
									encoding = new UTF8Encoding(false);//Not byte order mark.
									break;

								case var b when split.Equals("utf-16", StringComparison.OrdinalIgnoreCase):
								case var b2 when split.Equals("unicode", StringComparison.OrdinalIgnoreCase):
									encoding = Encoding.Unicode;
									break;

								case var b when split.Equals("utf-16-raw", StringComparison.OrdinalIgnoreCase):
									encoding = new UnicodeEncoding(false, false);//Little endian, no byte order mark.
									break;

								case var b when split.Equals("raw", StringComparison.OrdinalIgnoreCase):
									raw = true;
									break;

								case var b when split.Equals("`n", StringComparison.OrdinalIgnoreCase):
								case var b2 when split.Equals("\n", StringComparison.OrdinalIgnoreCase):
									crlf = true;
									break;
							}
						}
					}
				}

				if (string.IsNullOrEmpty(file))
				{
					var info = Loops.Peek(LoopType.File);

					if (info != null)
					{
						if (info.filename != string.Empty)
						{
							if (info.sw == null)
							{
								if (info.filename == "*")
									info.sw = Console.Out;
								else if (info.filename == "**")
									info.sw = Console.Error;
								else
									info.sw = new StreamWriter(info.filename, true, encoding);
							}

							tw = info.sw;
						}
					}
				}
				else if (file == "*")
					tw = Console.Out;
				else if (file == "**")
					tw = Console.Error;
				else
					tw = new StreamWriter(file, true, encoding);

				if (tw != null)
				{
					if (t is string s)
					{
#if DEBUG

						if (s == "fail")
							_ = KeysharpEnhancements.OutputDebugLine(s);

#endif

						if (raw)
							//sw.Write(Encoding.Unicode.GetBytes(s));
							tw.Write(s.AsSpan());
						else if (crlf)
							tw.Write(s.ReplaceLineEndings("\r\n"));
						else
							tw.Write(s);
					}
					else if (tw is StreamWriter sw)
					{
						if (t is Array arr)//Most common will be array.
						{
							sw.BaseStream.Write(arr.ToByteArray().ToArray());
						}
						else if (t is Buffer buf)
						{
							var len = (int)(long)buf.Size;
							unsafe
							{
								var bytes = new byte[len];
								Marshal.Copy((nint)buf.Ptr, bytes, 0, len);
								sw.BaseStream.Write(bytes);
							}
						}
						else if (t is byte[] ib)
						{
							sw.BaseStream.Write(ib);
						}
						else if (t is IList il)//It was some other type of container, rare.
						{
							sw.BaseStream.Write(il.ToByteArray().ToArray());
						}
					}

					if (!string.IsNullOrEmpty(file) && !file.StartsWith("*"))
						tw.Close();
				}

				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error appending text to file {file}");
			}
		}

		/// <summary>
		/// Copies one or more files.
		/// </summary>
		/// <param name="source">The name of a single file or folder, or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// sourcePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="destination">The name or pattern of the destination, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// If present, the first asterisk (*) in the filename is replaced with the source filename excluding its extension,<br/>
		/// while the first asterisk after the last full stop (.) is replaced with the source file's extension.<br/>
		/// If an asterisk is present but the extension is omitted, the source file's extension is used.<br/>
		/// To perform a simple copy -- retaining the existing file name(s) -- specify only the folder name.
		/// </param>
		/// <param name="overwrite">If omitted, it defaults to 0. Otherwise, specify one of the following numbers to indicate whether to overwrite files if they already exist:<br/>
		/// 0: Do not overwrite existing files. The operation will fail and have no effect if DestPattern already exists as a file or directory.
		/// 1: Overwrite existing files. However, any files or subfolders inside destPattern that do not have a counterpart in sourcePattern will not be deleted.
		/// </param>
		/// <exception cref="Error">An <see cref="OSError"/> exception is thrown if any errors occur.</exception>
		public static object FileCopy(object sourcePattern, object destPattern, object overwrite = null)
		{
			FileCopyMove(sourcePattern.As(), destPattern.As(), overwrite.Ab(), false);
			return DefaultObject;
		}

		/// <summary>
		/// Creates a shortcut to a file.
#if WINDOWS
		/// Gotten from: https://www.fluxbytes.com/csharp/create-shortcut-programmatically-in-c/
#else
		/// If any of workingDir, args, description or icon are not empty, then it is assumed this is a desktop shortcut.<br/>
		/// Otherwise, this assumes the caller wanted a symlink.<br/>
		/// Note that when supplying either the target or the link file to a symlink, you must use the full path and not shorthand like ~/ for your home folder.<br/>
		/// The arguments for iconNumber and runState are ignored.
#endif
		/// </summary>
		/// <param name="target">Name of the file that the shortcut refers to, which should include an absolute path unless the file is integrated with the system (e.g. Notepad.exe).<br/>
		/// The file does not have to exist at the time the shortcut is created.
		/// </param>
		/// <param name="linkFile">Name of the shortcut file to be created, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// Be sure to include the .lnk extension on Windows.<br/>
		/// The destination directory must already exist. If the file already exists, it will be overwritten.</param>
		/// <param name="workingDir">If blank or omitted, linkFile will have a blank "Start in" field and the system will provide a default working directory when the shortcut is launched.<br/>
		/// Otherwise, specify the directory that will become target's current working directory when the shortcut is launched.
		/// </param>
		/// <param name="args">If blank or omitted, target will be launched without parameters.<br/>
		/// Otherwise, specify the parameters that will be passed to target when it is launched.<br/>
		/// Separate parameters with spaces. If a parameter contains spaces, enclose it in double quotes.
		/// </param>
		/// <param name="description">If blank or omitted, linkFile will have no description.<br/>
		/// Otherwise, specify comments that describe the shortcut (used by the OS to display a tooltip, etc...).
		/// </param>
		/// <param name="icon">If blank or omitted, linkFile will have target's icon.<br/>
		/// Otherwise, specify the full path and name of the icon to be displayed for linkFile.<br/>
		/// It must either be an .ICO file or the very first icon of an EXE or DLL file.
		/// </param>
#if WINDOWS
		/// <param name="shortcutKey">If blank or omitted, linkFile will have no shortcut key.<br/>
		/// Otherwise, specify a single letter, number, or the name of a single key from the key list (mouse buttons and other non-standard keys might not be supported).<br/>
		/// Do not include modifier symbols. Currently, all shortcut keys are created as Ctrl+Alt shortcuts.<br/>
		/// For example, if the letter B is specified for this parameter, the shortcut key will be Ctrl+Alt+B.
		/// </param>
		/// <param name="iconNumber">If omitted, it defaults to 1.<br/>
		/// Otherwise, specify the number of the icon to be used in iconFile.<br/>
		/// For example, 2 is the second icon.
		/// </param>
		/// <param name="runState">If omitted, it defaults to 1. Otherwise, specify one of the following digits to launch Target minimized or maximized:<br/>
		/// 1: Normal (this is the default)<br/>
		/// 3: Maximized<br/>
		/// 7: Minimized
		/// </param>
#else
		/// <param name="shortcutType">If blank or omitted, the shortcut will be of type Application.<br/>
		/// Otherwise, specify a number to indicate the type of target the shortcut is for:<br/>
		/// 1: Application<br/>
		/// 2: Link<br/>
		/// 3: Directory<br/>
		/// Verbatim text<br/>
		/// </param>
		/// <param name="iconNumber">Ignored</param>
		/// <param name="runState">Ignored</param>
		/// </param>
#endif
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if any errors occur.</exception>
		public static object FileCreateShortcut(object target, object linkFile, object workingDir = null, object args = null, object description = null,
												object iconFile = null,
#if WINDOWS
												object shortcutKey = null,
#else
												object shortcutType = null,
#endif
												object iconNumber = null, object runState = null)
		{
			var t = target.As();
			var l = linkFile.As();
			var w = workingDir.As();
			var a = args.As();
			var d = description.As();
			var icon = iconFile.As();

			if (t == "")
				return Errors.ValueErrorOccurred("Shortcut target cannot be an empty string.");

#if LINUX
			var type = shortcutType.As();
			t = Path.GetFullPath(t);

			if (w != "" || a != "" || d != "" || icon != "")
			{
				var creator = new ShortcutCreator();
				icon = Path.GetFullPath(icon);
				creator.Add("Icon", icon);
				creator.Add("Path", w);
				creator.Add("Comment", d);

				if (a != "")
					creator.Add("Exec", t + " " + a);
				else
					creator.Add("Exec", t);

				if (type != "")
				{
					if (long.TryParse(type, out var tp))
					{
						if (tp == 0)//Wasn't a number, assume text and use verbatim.
							creator.Add("Type", type);
						else if (tp == 1)//Was a number, test for 1 2 or 3.
							creator.Add("Type", "Application");
						else if (tp == 2)
							creator.Add("Type", "Link");
						else if (tp == 3)
							creator.Add("Type", "Directory");
						else//Was something else, so just assume it's of type Application.
							creator.Add("Type", "Application");
					}
					else
						creator.Add("Type", type);
				}
				else//Was not specified, so just assume it's of type Application.
					creator.Add("Type", "Application");

				creator.Save(l);
				$"chmod +x '{l}'".Bash();
			}
			else
				$"ln -sf '{t}' '{l}'".Bash();

#elif WINDOWS
			var sc = shortcutKey.As();
			var iconNum = iconNumber.Al(0);
			var state = runState.Al(1);
			var shell = new IWshRuntimeLibrary.WshShell();
			var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(Path.GetFullPath(l));
			shortcut.TargetPath = Path.GetFullPath(t);

			if (w != "")
				shortcut.WorkingDirectory = Path.GetFullPath(w);

			shortcut.Arguments = a;
			shortcut.Description = d;

			if (icon != "")
				shortcut.IconLocation = $"{Path.GetFullPath(icon)}, {iconNum}";

			var mods = "";
			var i = 0;

			for (; i < sc.Length; i++)
			{
				char ch = sc[i];

				if (ch == '^')
					mods += "Ctrl+";
				else if (ch == '!')
					mods += "Alt+";
				else if (ch == '+')
					mods += "Shift+";
				else
					break;
			}

			if (sc.Length > 0)
			{
				if (mods.Length == 0)// For backwards compatibility: if modifiers omitted, assume CTRL+ALT.
					shortcut.Hotkey = $"Ctrl+Alt+{sc}";
				else
					shortcut.Hotkey = $"{mods}{sc.Substring(i)}";
			}
			else
				shortcut.Hotkey = "";

			shortcut.WindowStyle = (int)state;
			shortcut.Save();
#endif
			return DefaultObject;
		}

		/// <summary>
		/// Deletes one or more files permanently.
		/// </summary>
		/// <param name="filePattern">The name of a single file or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any errors occur.</exception>
		public static object FileDelete(object filePattern)
		{
			var s = filePattern.As();
			var path = Path.GetDirectoryName(s);
			var dir = new DirectoryInfo(path?.Length == 0 ? "./" : path);
			var filename = Path.GetFileName(s);
			var failures = 0;

			foreach (var file in dir.EnumerateFiles(filename))
			{
				try
				{
					file.Delete();
				}
				catch
				{
					failures++;
				}
			}

			if (failures > 0)
				return Errors.ErrorOccurred($"Failed {failures} times moving or copying files.", "", failures);

			return DefaultObject;
		}

		/// <summary>
		/// Sets the default encoding for <see cref="FileRead"/>, <see cref="LoopRead"/>, <see cref="FileAppend"/>, and <see cref="FileOpen"/>.
		/// </summary>
		/// <param name="encoding">Specify one of the following values:<br/>
		/// or empty string: The system default ANSI code page.See remarks below.<br/>
		/// UTF-8: Unicode UTF-8, equivalent to CP65001.<br/>
		/// UTF-8-RAW: As above, but no byte order mark is written when a new file is created.<br/>
		/// UTF-16: Unicode UTF-16 with little endian byte order, equivalent to CP1200.<br/>
		/// UTF-16-RAW: As above, but no byte order mark is written when a new file is created.<br/>
		/// CPnnn: A code page with numeric identifier nnn. See Code Page Identifiers.<br/>
		/// nnn: A numeric code page identifier.
		/// </param>
		public static object FileEncoding(object encoding)
		{
			var s = encoding.As();

			if (s != "")
				A_FileEncoding = s;

			return DefaultObject;
		}

		/// <summary>
		/// Checks for the existence of a file or folder and returns its attributes.
		/// </summary>
		/// <param name="filePattern">The path, filename, or file pattern to check.<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <returns>
		/// The attributes of the first matching file or folder.<br/>
		/// This string is a subset of RASHNDOCTL, where each letter means the following:<br/>
		/// R: READONLY<br/>
		/// A: ARCHIVE<br/>
		/// S: SYSTEM<br/>
		/// H: HIDDEN<br/>
		/// N: NORMAL<br/>
		/// D: DIRECTORY<br/>
		/// O: OFFLINE<br/>
		/// C: COMPRESSED<br/>
		/// T: TEMPORARY<br/>
		/// L: REPARSE_POINT(typically a symbolic link)<br/>
		/// If the file has no attributes(rare), "X" is returned.<br/>
		/// If no file or folder is found, an empty string is returned.
		/// </returns>
		public static string FileExist(object filePattern)
		{
			var s = filePattern.As();

			try
			{
				var path = Path.GetDirectoryName(s);
				var dir = new DirectoryInfo(path);
				var filename = Path.GetFileName(s);

				if (Directory.Exists(s))
					return Conversions.FromFileAttribs(File.GetAttributes(s));

				foreach (var file in dir.EnumerateFiles(filename))
					return Conversions.FromFileAttribs(File.GetAttributes(file.FullName));
			}
			catch
			{
			}

			return DefaultObject;
		}

		/// <summary>
		/// Reports whether a file or folder is read-only, hidden, etc.
		/// </summary>
		/// <param name="filename">If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of the target file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <returns>the attributes of the file or folder. This string is a subset of RASHNDOCTL, where each letter means the following:<br/>
		/// R: READONLY<br/>
		/// A: ARCHIVE<br/>
		/// S: SYSTEM<br/>
		/// H: HIDDEN<br/>
		/// N: NORMAL<br/>
		/// D: DIRECTORY<br/>
		/// O: OFFLINE<br/>
		/// C: COMPRESSED<br/>
		/// T: TEMPORARY<br/>
		/// L: REPARSE_POINT(typically a symbolic link)<br/>
		/// </returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any errors occur.</exception>
		public static string FileGetAttrib(object filename)
		{
			var s = filename.As();
			ThreadAccessors.A_LastError = 0;

			try
			{
				if (Directory.Exists(s) || File.Exists(s))
					return Conversions.FromFileAttribs(File.GetAttributes(s));
				else if (s?.Length == 0)
					return Conversions.FromFileAttribs(File.GetAttributes(A_LoopFileFullPath));
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				_ = (string)Errors.OSErrorOccurred(ex, $"Error getting file attributes for file {s}", DefaultErrorString);
			}

			return DefaultObject;
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj)
		{
			object outTarget = VarRef.Empty;
			object outDir = VarRef.Empty;
			object outArgs = VarRef.Empty;
			object outDescription = VarRef.Empty;
			object outIcon = VarRef.Empty;
			object outIconNum = VarRef.Empty;
			object outRunState = VarRef.Empty;
			return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget)
		{
			object outDir = VarRef.Empty;
			object outArgs = VarRef.Empty;
			object outDescription = VarRef.Empty;
			object outIcon = VarRef.Empty;
			object outIconNum = VarRef.Empty;
			object outRunState = VarRef.Empty;
			return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget,
											 [ByRef] object outDir)
		{
            object outArgs = VarRef.Empty;
            object outDescription = VarRef.Empty;
            object outIcon = VarRef.Empty;
            object outIconNum = VarRef.Empty;
            object outRunState = VarRef.Empty;
            return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget,
											 [ByRef] object outDir,
											 [ByRef] object outArgs)
		{
            object outDescription = VarRef.Empty;
            object outIcon = VarRef.Empty;
            object outIconNum = VarRef.Empty;
            object outRunState = VarRef.Empty;
            return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget,
											 [ByRef] object outDir,
											 [ByRef] object outArgs,
											 [ByRef] object outDescription)
		{
            object outIcon = VarRef.Empty;
            object outIconNum = VarRef.Empty;
            object outRunState = VarRef.Empty;
            return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget,
											 [ByRef] object outDir,
											 [ByRef] object outArgs,
											 [ByRef] object outDescription,
											 [ByRef] object outIcon)
		{
            object outIconNum = VarRef.Empty;
            object outRunState = VarRef.Empty;
            return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

		/// <summary>
		/// <see cref="FileGetShortcut(object, ref object, ref object, ref object, ref object, ref object, ref object, ref object)"/>
		/// </summary>
		public static object FileGetShortcut(object obj,
											 [ByRef] object outTarget,
											 [ByRef] object outDir,
											 [ByRef] object outArgs,
											 [ByRef] object outDescription,
											 [ByRef] object outIcon,
											 [ByRef] object outIconNum)
		{
            object outRunState = VarRef.Empty;
            return FileGetShortcut(obj,
								   outTarget,
								   outDir,
								   outArgs,
								   outDescription,
								   outIcon,
								   outIconNum,
								   outRunState);
		}

#if WINDOWS
		/// <summary>
		/// Retrieves information about a shortcut file.<br/>
		/// </summary>
		/// <param name="linkFile">Name of the shortcut file to be analyzed, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// Be sure to include the .lnk extension.
		/// </param>
		/// <param name="outTarget">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's target (not including any arguments it might have).
		/// </param>
		/// <param name="outDir">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's working directory.
		/// </param>
		/// <param name="outArgs">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's parameters (blank if none).
		/// </param>
		/// <param name="outDescription">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's comments (blank if none).
		/// </param>
		/// <param name="outIcon">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the filename of the shortcut's icon (blank if none).
		/// </param>
		/// <param name="outIconNum">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's icon number within the icon file (blank if none).<br/>
		/// This value is most often 1, which means the first icon.
		/// </param>
		/// <param name="outRunState"></param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any errors occur.</exception>
#else
		/// <summary>
		/// Retrieves information about a shortcut file.<br/>
		/// Takes different action based on whether it's a simple symlink or a full desktop shortcut.<br/>
		/// If it's a symlink, then only the outTarget field will be populated and all others will be blank.<br/>
		/// If it's a full desktop shortcut, then the following mapping will be used to populate the passed in reference variables:<br/>
		/// outTarget=Exec including any arguments<br/>
		/// outDir=Path<br/>
		/// outArgs=Exec text after first space<br/>
		/// outDescription=Comment<br/>
		/// outIcon=Icon<br/>
		/// outIconNum=Type<br/>
		/// outRunState is ignored<br/>
		/// </summary>
		/// <param name="linkFile">Name of the shortcut file to be analyzed, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// </param>
		/// <param name="outTarget">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's target and any arguments it might have.</param>
		/// <param name="outDir">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's working directory.
		/// </param>
		/// <param name="outArgs">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's parameters (blank if none).
		/// </param>
		/// <param name="outDescription">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's comments (blank if none).</param>
		/// <param name="outIcon">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the filename of the shortcut's icon (blank if none).
		/// </param>
		/// <param name="outType">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's type.</param>
		/// <param name="outRunState">Ignored</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any errors occur.</exception>
#endif
		public static object FileGetShortcut(object linkFile,
											 [ByRef] object outTarget,
											 [ByRef] object outDir,
											 [ByRef] object outArgs,
											 [ByRef] object outDescription,
											 [ByRef] object outIcon,
#if WINDOWS
											 object outIconNum,
#else
											 object outType,
#endif
											 object outRunState)
		{
			var link = Path.GetFullPath(linkFile.As());
#if LINUX
			var dest = $"readlink -f '{link}'".Bash();

			if (link == dest)//Was not just a simple symlink.
			{
				var sc = new ShortcutCreator(link);
				Script.SetPropertyValue(outTarget, "__Value", sc.Get("Exec"));
				Script.SetPropertyValue(outDir, "__Value", sc.Get("Path"));
				Script.SetPropertyValue(outDescription, "__Value", sc.Get("Comment"));
				Script.SetPropertyValue(outIcon, "__Value", sc.Get("Icon"));
				Script.SetPropertyValue(outType, "__Value", sc.Get("Type"));

				if (GetPropertyValue(outTarget, "__Value") is string s && s.Length > 0)
				{
					if (s[0] != '"' && s[0] != '\'')
					{
						var splits = s.Split(SpaceTab, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

						if (splits.Length > 1)
							Script.SetPropertyValue(outArgs, "__Value", splits[1]);
						else
							Script.SetPropertyValue(outArgs, "__Value", "");
					}
					else//It was quoted.
					{
						var firstArgIndex = s.FindFirstNotInQuotes(" ");
						var tempArgs = firstArgIndex != -1 && firstArgIndex < s.Length - 1 ? s.Substring(firstArgIndex + 1) : "";
						Script.SetPropertyValue(outArgs, "__Value", tempArgs.Trim());
					}
				}
				else
					Script.SetPropertyValue(outArgs, "__Value", "");//No way to determine args.
			}
			else
			{
				Script.SetPropertyValue(outTarget, "__Value", dest);
				Script.SetPropertyValue(outDir, "__Value", Path.GetDirectoryName(dest));
				Script.SetPropertyValue(outArgs, "__Value", "");
				Script.SetPropertyValue(outDescription, "__Value", "");
				Script.SetPropertyValue(outIcon, "__Value", "");
				Script.SetPropertyValue(outType, "__Value", "");
			}

			Script.SetPropertyValue(outRunState, "__Value", "");
#elif WINDOWS

            try
            {
				var shell = new IWshRuntimeLibrary.WshShell();
				var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(Path.GetFullPath(link));
				var commaindex = shortcut.IconLocation.LastIndexOf(',');
				var iconno = "0";
				var iconstr = shortcut.IconLocation;

				if (commaindex != -1)
				{
					iconstr = iconstr.Substring(0, commaindex);
					iconno = shortcut.IconLocation.Substring(commaindex + 1).Trim();
				}

                Script.SetPropertyValue(outTarget, "__Value", shortcut.TargetPath);
                Script.SetPropertyValue(outDir, "__Value", shortcut.WorkingDirectory);
                Script.SetPropertyValue(outArgs, "__Value", shortcut.Arguments);
                Script.SetPropertyValue(outDescription, "__Value", shortcut.Description);
                Script.SetPropertyValue(outIcon, "__Value", iconstr);
                Script.SetPropertyValue(outIconNum, "__Value", iconno);//How to get this?
                Script.SetPropertyValue(outRunState, "__Value", shortcut.WindowStyle);//How to get this?
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Error getting shortcut information for {link}");
			}

#endif
			return DefaultObject;
		}

		/// <summary>
		/// Retrieves the size of a file.
		/// </summary>
		/// <param name="filename">If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of the target file, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <param name="units">If blank or omitted, it defaults to B.<br/>
		/// Otherwise, specify one of the following letters to cause the result to be returned in specific units:<br/>
		/// B: Bytes<br/>
		/// K: Kilobytes<br/>
		/// M: Megabytes<br/>
		/// G: Gigabytes<br/>
		/// T: Terabytes<br/>
		/// </param>
		/// <returns>The size of the specified file rounded down to the nearest whole number.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static long FileGetSize(object filename = null, object units = null)
		{
			long result;
			var file = filename.As();
			var u = units.As();
			ThreadAccessors.A_LastError = 0;

			if (file?.Length == 0)
				file = A_LoopFileFullPath;

			try
			{
				var scale = 1024L;
				var size = (new FileInfo(file)).Length;

				if (!string.IsNullOrEmpty(u))
				{
					switch (u[0])
					{
						case 'k':
						case 'K':
							size /= scale;
							break;

						case 'm':
						case 'M':
							size /= scale * scale;
							break;

						case 'g':
						case 'G':
							size /= scale * scale * scale;
							break;

						case 't':
						case 'T':
							size /= scale * scale * scale * scale;
							break;
					}
				}

				result = size;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (long)Errors.OSErrorOccurred(ex, $"Error getting file size for file {file}", DefaultErrorLong);
			}

			return result;
		}

		/// <summary>
		/// Retrieves the datetime stamp of a file or folder.
		/// </summary>
		/// <param name="filename">If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of the target file or folder, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <param name="whichTime">If blank or omitted, it defaults to M.<br/>
		/// Otherwise, specify one of the following letters to set which timestamp should be retrieved:<br/>
		/// M: Modification time
		/// C: Creation time
		/// A: Last access time
		/// </param>
		/// <returns>A string of digits in the YYYYMMDDHH24MISS format. The time is your own local time, not UTC/GMT.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static string FileGetTime(object filename = null, object whichTime = null)
		{
			var file = filename.As();
			ThreadAccessors.A_LastError = 0;

			try
			{
				var time = whichTime.As("M");

				if (file?.Length == 0)
					file = A_LoopFileFullPath;

				if (!File.Exists(file))
					return DefaultObject;

				var date = new DateTime();
				var info = new FileInfo(file);

				date = time[0] switch
				{
					'c' or 'C' => info.CreationTime,
					'a' or 'A' => info.LastAccessTime,
					_ => info.LastWriteTime,
				};

				return Conversions.ToYYYYMMDDHH24MISS(date);
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (string)Errors.OSErrorOccurred(ex, $"Error getting file time for file {file}", DefaultErrorString);
			}
		}

		/// <summary>
		/// Retrieves the version of a file.
		/// </summary>
		/// <param name="filename">If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of the target file.
		/// </param>
		/// <returns>This function returns the version number of the specified file.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static string FileGetVersion(object filename)
		{
			var file = filename.As();
			ThreadAccessors.A_LastError = 0;

			try
			{
				if (file?.Length == 0)
					file = A_LoopFileFullPath;

				var info = FileVersionInfo.GetVersionInfo(file);
				return info.FileVersion;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return (string)Errors.OSErrorOccurred(ex, $"Error getting file version for file {file}", DefaultErrorString);
			}
		}

		/// <summary>
		/// Unsupported functionality which always throws an exception.
		/// </summary>
		/// <exception cref="Error">An <see cref="Error"/> is always thrown.</exception>
		public static string FileInstall(object obj0, object obj1, object obj2 = null) => (string)Errors.ErrorOccurred("Compiling files into an executable is not supported in Keysharp", DefaultErrorString);

		/// <summary>
		/// Moves or renames one or more files.
		/// </summary>
		/// <param name="sourcePattern">The name of a single file or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// sourcePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <param name="destPattern">The name or pattern of the destination, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="overwrite">If omitted, it defaults to 0.<br/>
		/// Otherwise, specify one of the following numbers to indicate whether to overwrite files if they already exist:<br/>
		/// 0: Do not overwrite existing files.The operation will fail and have no effect if destPattern already exists as a file or directory.<br/>
		/// 1: Overwrite existing files. However, any files or subfolders inside destPattern that do not have a counterpart in sourcePattern will not be deleted.
		/// </param>
		public static object FileMove(object sourcePattern, object destPattern, object overwrite = null)
		{
			FileCopyMove(sourcePattern.As(), destPattern.As(), overwrite.Ab(), true);
			return DefaultObject;
		}

		/// <summary>
		/// Opens a file to read specific content from it and/or to write new content into it.
		/// </summary>
		/// <param name="filename">The path of the file to open, which is assumed to be in A_WorkingDir if an absolute path isn't specified.<br/>
		/// Specify an asterisk(or two) as shown below to open the standard input/output/error stream:<br/>
		/// FileOpen("*", "r") ; for stdin<br/>
		/// FileOpen("*", "w") ; for stdout<br/>
		/// FileOpen("**", "w") ; for stderr<br/>
		/// </param>
		/// <param name="flags">Either a string of characters indicating the desired access mode followed by other options (with optional spaces or tabs in between);<br/>
		/// or a combination (sum) of numeric flags.
		/// </param>
		/// <param name="encoding">If omitted, the default encoding (as set by FileEncoding or CP0 otherwise) will be used.<br/>
		/// If blank, it defaults to CP0 (the system default ANSI code page).<br/>
		/// Otherwise, specify the encoding or code page to use for text I/O, e.g. "UTF-8", "UTF-16", "CP936" or 936.<br/>
		/// If the file contains a UTF-8 or UTF-16 byte order mark(BOM), or if the h(handle) flag is used,<br/>
		/// this parameter and the default encoding will be ignored, unless the file is being opened with write-only access (i.e.the previous contents of the file are being discarded).
		/// </param>
		/// <returns>A new KeysharpFile object encapsulating the open handle to the file.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileOpen(object filename, object flags, object encoding = null)
		{
			ThreadAccessors.A_LastError = 0;
			var file = filename.As();
			var f = flags.As();
			var e = encoding.As();
			var enc = ThreadAccessors.A_FileEncodingRaw;
			var ienc = e.ParseInt(false);

			if (ienc.HasValue)
				enc = Encoding.GetEncoding(ienc.Value);
			else if (e != "")
				enc = GetEncoding(e);

			var mode = FileMode.Open;
			var access = FileAccess.ReadWrite;
			var share = FileShare.ReadWrite | FileShare.Delete;
			var shareset = false;
			var eolconv = 0;

			foreach (Range r in f.AsSpan().SplitAny(Spaces))
			{
				var i = 0;
				var flag = f.AsSpan(r).Trim();

				if (flag.Length > 0)
				{
					if (flag.Equals("r", StringComparison.OrdinalIgnoreCase))
					{
						mode = FileMode.Open;
						access = FileAccess.Read;
					}
					else if (flag.Equals("w", StringComparison.OrdinalIgnoreCase) || (int.TryParse(flag, out i) && i == 1))
					{
						mode = FileMode.Create;
						access = FileAccess.ReadWrite;
					}
					else if (flag.Equals("a", StringComparison.OrdinalIgnoreCase) || (int.TryParse(flag, out i) && i == 2))
					{
						mode = FileMode.Append;
						access = FileAccess.Write;
					}
					else if (flag.Equals("rw", StringComparison.OrdinalIgnoreCase) || (int.TryParse(flag, out i) && i == 3))
					{
						mode = FileMode.OpenOrCreate;
						access = FileAccess.ReadWrite;
					}
					else if (flag.Equals("h", StringComparison.OrdinalIgnoreCase))
					{
						file = "h*" + file;
					}
					else if (flag.Equals("\n", StringComparison.OrdinalIgnoreCase) || (int.TryParse(flag, out i) && i == 4))
					{
						eolconv = 4;
					}
					else if (flag.Equals("\r", StringComparison.OrdinalIgnoreCase) || (int.TryParse(flag, out i) && i == 8))
					{
						eolconv = 8;
					}
					else if (flag.Equals("-", StringComparison.OrdinalIgnoreCase))
					{
						share = FileShare.None;
						shareset = true;
					}
					else if (flag.StartsWith('-'))
					{
						if (flag.Contains("r", StringComparison.OrdinalIgnoreCase))
						{
							share &= ~FileShare.Read;
							shareset = true;
						}

						if (flag.Contains("w", StringComparison.OrdinalIgnoreCase))
						{
							share &= ~FileShare.Write;
							shareset = true;
						}

						if (flag.Contains("d", StringComparison.OrdinalIgnoreCase))
						{
							share &= ~FileShare.Delete;
							shareset = true;
						}
					}
					else
					{
						var b = int.TryParse(flag, out i);
						share = FileShare.None;

						if (!b)
						{
							shareset = true;
						}
						else
						{
							if ((i & 0x100) == 0x100)
							{
								share |= FileShare.Read;
								shareset = true;
							}

							if ((i & 0x200) == 0x200)
							{
								share |= FileShare.Write;
								shareset = true;
							}

							if ((i & 0x400) == 0x400)
							{
								share |= FileShare.Delete;
								shareset = true;
							}
						}
					}
				}
			}

			if (!shareset)
				share = FileShare.ReadWrite | FileShare.Delete;

			try
			{
				return new KeysharpFile(file, mode, access, share, enc, eolconv);
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error opening file {file}");
			}
		}

		/// <summary>
		/// Retrieves the contents of a file.
		/// </summary>
		/// <param name="filename">The name of the file to read, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="options">Zero or more of the following strings. Separate each option from the next with a single space or tab.<br/>
		/// For example: "`n m5000 UTF-8"<br/>
		/// Encoding: Specify any of the encoding names accepted by FileEncoding (excluding the empty string) to use that encoding<br/>
		/// if the file lacks a UTF-8 or UTF-16 byte order mark. If omitted, it defaults to <see cref="A_FileEncoding"/>.<br/>
		/// RAW: Specify the word RAW (case-insensitive) to read the file's content as raw binary data and return a Buffer object instead of a string.<br/>
		/// This option overrides any previously specified encoding and vice versa.<br/>
		/// m1024: If this option is omitted, the entire file is loaded unless there is insufficient memory,<br/>
		/// in which case an error message is shown and the thread exits (but Try can be used to avoid this).<br/>
		/// Otherwise, replace 1024 with a decimal or hexadecimal number of bytes.If the file is larger than this, only its leading part is loaded.<br/>
		/// Note: This might result in the last line ending in a naked carriage return (`r) rather than `r`n.<br/>
		/// `n (a linefeed character): Replaces any/all occurrences of carriage return & linefeed (`r`n) with linefeed (`n).<br/>
		/// However, this translation reduces performance and is usually not necessary.<br/>
		/// For example, text containing `r`n is already in the right format to be added to a Gui Edit control.<br/>
		/// </param>
		/// <returns>The contents of the specified file.<br/>
		/// The return value is a <see cref="Buffer"/> object if the RAW option is in effect and the file can be opened; otherwise, it is a string.<br/>
		/// If the file does not exist or cannot be opened for any other reason, an empty string is returned.
		/// </returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileRead(object filename, object options = null)
		{
			object output = null;
			var file = filename.As();
			var opts = options.As();
			var enc = ThreadAccessors.A_FileEncodingRaw;
			ThreadAccessors.A_LastError = 0;

			if (string.IsNullOrEmpty(file))
				return DefaultErrorObject;

			var max = -1;
			bool binary = false, nocrlf = false;

			foreach (Range r in opts.AsSpan().SplitAny(SpaceTabSv))
			{
				var split = opts.AsSpan(r).Trim(SpaceTab);//Need to supply chars to trim else \n would get automatically trimmed.

				if (split.Length > 0)
				{
					if (Options.TryParse(split, "m", ref max))
					{
					}
					else
					{
						if (split.Equals("raw", StringComparison.OrdinalIgnoreCase))
							binary = true;
						else if (split[0] == '\n')
							nocrlf = true;
						else
							enc = GetEncoding(split.ToString());//Will internally convert to string.
					}
				}
			}

			if (max == 0)
				return DefaultErrorObject;

			if (binary)
			{
				try
				{
					var temparr = max == -1 ? File.ReadAllBytes(file) : new BinaryReader(File.OpenRead(file)).ReadBytes(max);
					output = new Buffer(temparr);
				}
				catch (Exception ex)
				{
					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred(ex, $"Error reading file {file}");
				}
			}
			else
			{
				string text;

				try
				{
					if (max != -1)
					{
						using (var fs = new FileStream(file, FileMode.Open))
						{
							using (var br = new BinaryReader(fs))
							{
								var buf = br.ReadBytes(max);
								text = enc.GetString(buf);
							}
						}
					}
					else
						text = File.ReadAllText(file);
				}
				catch (Exception ex)
				{
					ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
					return Errors.OSErrorOccurred(ex, $"Error reading file {file}");
				}

				if (max != -1)
					text = text.Substring(0, max);

				if (nocrlf)
					text = text.Replace("\r\n", "\n");

				output = text;
			}

			return output;
		}

		/// <summary>
		/// Sends a file or directory to the recycle bin, if possible.
		/// </summary>
		/// <param name="filePattern">
		/// The name of a single file or a wildcard pattern such as C:\Temp\*.tmp.<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// To recycle an entire directory, provide its name without a trailing backslash.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileRecycle(object filePattern)
		{
			var s = filePattern.As();
			ThreadAccessors.A_LastError = 0;

			try
			{
				var path = Path.GetDirectoryName(s);
				var dir = new DirectoryInfo(path);
				var filename = Path.GetFileName(s);
#if LINUX
				$"gio trash {s}".Bash();
#elif WINDOWS

				foreach (var file in dir.EnumerateFiles(filename))
					FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

#endif
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error recycling file(s) with pattern {s}");
			}
		}

		/// <summary>
		/// Empties the recycle bin.
		/// </summary>
		/// <param name="driveLetter">If omitted, the recycle bin for all drives is emptied. Otherwise, specify a drive letter such as C:\</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileRecycleEmpty(object driveLetter = null)
		{
			var s = driveLetter.As();

			try
			{
#if LINUX
				"gio trash --empty".Bash();
#elif WINDOWS
				_ = WindowsAPI.SHEmptyRecycleBin(0, s != "" ? s : null, WindowsAPI.SHERB_NOCONFIRMATION | WindowsAPI.SHERB_NOPROGRESSUI | WindowsAPI.SHERB_NOSOUND);
#endif
				return DefaultObject;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Error emptying recycle bin for drive {s}");
			}
		}

		/// <summary>
		/// Changes the attributes of one or more files or folders. Wildcards are supported.
		/// </summary>
		/// <param name="attributes">
		/// The attributes to change. For example, "+HA-R".<br/>
		/// To easily turn on, turn off or toggle attributes, prefix one or more of the following attribute letters with a plus(+), minus(-) or caret(^) symbol, respectively:<br/>
		/// R: READONLY<br/>
		/// A: ARCHIVE<br/>
		/// S: SYSTEM<br/>
		/// H: HIDDEN<br/>
		/// N: NORMAL (this is valid only when used without any other attributes)<br/>
		/// O: OFFLINE<br/>
		/// T: TEMPORARY<br/>
		/// If no symbol precedes the attribute letters, the file's attributes are replaced with the given attributes.<br/>
		/// To remove all attributes, use "N" on its own.
		/// </param>
		/// <param name="filePattern">
		/// If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of a single file or folder, or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <param name="mode">
		/// If blank or omitted, only files are operated upon and subdirectories are not recursed into.<br/>
		/// Otherwise, specify one or more of the following letters:<br/>
		/// D: Include directories (folders).<br/>
		/// F: Include files.If both F and D are omitted, files are included but not folders.<br/>
		/// R: Subfolders are recursed into so that files and folders contained therein are operated upon if they match filePattern.<br/>
		/// All subfolders will be recursed into, not just those whose names match filePattern.<br/>
		/// If R is omitted, files and folders in subfolders are not included.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		public static object FileSetAttrib(object attributes, object filePattern = null, object mode = null)
		{
			ThreadAccessors.A_LastError = 0;
			var attr = attributes.As();
			var file = filePattern.As(A_LoopFileFullPath);
			var m = mode.As();
			var dodirs = m.Contains('d', StringComparison.OrdinalIgnoreCase);
			var dofiles = m.Contains('f', StringComparison.OrdinalIgnoreCase);
			var recurse = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			var failures = 0;

			if (!dodirs && !dofiles)
				dofiles = true;

			foreach (var path in Conversions.ToFiles(file, dofiles, dodirs, recurse))
			{
				try
				{
					var set = Conversions.ToFileAttribs(attr, File.GetAttributes(path));
					File.SetAttributes(path, set);

					if (set == FileAttributes.None)
						set = FileAttributes.Normal;
					else if (set.HasFlag(FileAttributes.Normal) && set != FileAttributes.Normal)//It was Normal and something else.
							set &= ~FileAttributes.Normal;//Remove normal because it can only be used alone. Just use the other bits.

					if (File.GetAttributes(path) != set)
						failures++;
				}
				catch (Exception)
				{
					failures++;
				}
			}
			
			if (failures != 0)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.ErrorOccurred($"Failed {failures} times setting file attributes.", "", failures);
			}

			return DefaultObject;
		}

		/// <summary>
		/// Changes the datetime stamp of one or more files or folders. Wildcards are supported.
		/// </summary>
		/// <param name="yyyymmddhh24miss">If blank or omitted, it defaults to the current time.<br/>
		/// Otherwise, specify the time to use for the operation (see Remarks for the format).<br/>
		/// Years prior to 1601 are not supported.
		/// </param>
		/// <param name="filePattern">
		/// If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of a single file or folder, or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// filePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </param>
		/// <param name="whichTime">If blank or omitted, it defaults to M.<br/>
		/// Otherwise, specify one of the following letters to set which timestamp should be changed:<br/>
		/// M: Modification time<br/>
		/// C: Creation time<br/>
		/// A: Last access time
		/// </param>
		/// <param name="mode">If blank or omitted, only files are operated upon and subdirectories are not recursed into.<br/>
		/// Otherwise, specify one or more of the following letters:<br/>
		/// D: Include directories.<br/>
		/// F: Include files. If both F and D are omitted, files are included but not folders.<br/>
		/// R: Subfolders are recursed into so that files and folders contained therein are operated upon if they match FilePattern.<br/>
		/// All subfolders will be recursed into, not just those whose names match filePattern.<br/>
		/// If R is omitted, files and folders in subfolders are not included.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		public static object FileSetTime(object yyyymmddhh24miss = null, object filePattern = null, object whichTime = null, object mode = null)
		{
			ThreadAccessors.A_LastError = 0;
			var YYYYMMDDHH24MISS = yyyymmddhh24miss.As();
			var file = filePattern.As();
			var whichtime = whichTime.As("M");
			var m = mode.As();
			var dodirs = m.Contains('d', StringComparison.OrdinalIgnoreCase);
			var dofiles = m.Contains('f', StringComparison.OrdinalIgnoreCase);
			var recurse = m.Contains('r', StringComparison.OrdinalIgnoreCase);
			var failures = 0;

			if (!dodirs && !dofiles)
				dofiles = true;

			if (file?.Length == 0)
				file = A_LoopFileFullPath;

			var time = Conversions.ToDateTime(YYYYMMDDHH24MISS);

			foreach (var path in Conversions.ToFiles(file, dofiles, dodirs, recurse))
			{
				try
				{
					var set = new DateTime();

					switch (whichtime[0])
					{
						case 'm':
						case 'M':
							File.SetLastWriteTime(path, time);
							set = File.GetLastWriteTime(path);
							break;

						case 'c':
						case 'C':
							File.SetCreationTime(path, time);
							set = File.GetCreationTime(path);
							break;

						case 'a':
						case 'A':
							File.SetLastAccessTime(path, time);
							set = File.GetLastAccessTime(path);
							break;

						default:
							ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
							return Errors.ErrorOccurred($"WhichTime value of {whichTime} was not m, c or a.");
					}

					if (set != time)
							failures++;
				}
				catch { failures++; }
			}

			if (failures != 0)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.ErrorOccurred($"Failed {failures} times setting file time.", "", failures);
			}

			return DefaultObject;
		}

		/// <summary>
		/// Internal helper for retrieving an <see cref="Encoding"/> object from options specified as a string.
		/// </summary>
		/// <param name="s">The encoding options.</param>
		/// <returns>A new <see cref="Encoding"/> object created based on the options specified in s.</returns>
		internal static Encoding GetEncoding(object s)
		{
			var val = s.ToString().ToLowerInvariant();
			Encoding tempenc;

			if (val.StartsWith("cp"))
				return Encoding.GetEncoding(val.Substring(2).ParseInt().Value);

			if (int.TryParse(val, out var cp))
				return Encoding.GetEncoding(cp);

			switch (val)
			{
				case "ascii":
				case "us-ascii":
					return Encoding.ASCII;

				case "utf-8":
					return Encoding.UTF8;

				case "utf-8-raw":
					return new UTF8Encoding(false);//No byte order mark.

				case "utf-16":
				case "unicode":
					return Encoding.Unicode;

				case "utf-16-raw":
					return new UnicodeEncoding(false, false);//Little endian, no byte order mark.
			}

			try
			{
				tempenc = Encoding.GetEncoding(val);
				return tempenc;
			}
			catch
			{
			}

			return Encoding.Unicode;
		}

		/// <summary>
		/// Internal helper for expanding wildcards in a filename and extension.
		/// </summary>
		/// <param name="source">The source filename, with wildcards.</param>
		/// <param name="dest">The destination filename, with wildcards to be expanded.</param>
		/// <returns>The expanded destination string if any wildcards were present, else the destination string as it was passed in.</returns>
		private static string ExpandFilenameWildcard(string source, string dest)
		{
			var srcfile = Path.GetFileNameWithoutExtension(source);
			var srcext = Path.GetExtension(source);
			var destfile = Path.GetFileNameWithoutExtension(dest);
			var destex = Path.GetExtension(dest);

			if (destex?.Length == 0)
				destex = srcext.Contains('.') ? ".*" : "*";

			srcext = srcext.TrimStart('.');
			return destfile.ReplaceFirst("*", srcfile) + (destex.Contains('*') ? destex.ReplaceFirst("*", srcext) : destex);
		}

		/// <summary>
		/// Internal helper for copying or moving a file.
		/// </summary>
		/// <param name="source">The source file.</param>
		/// <param name="dest">The destination file.</param>
		/// <param name="flag">Whether to overwrite an existing file in the destination.</param>
		/// <param name="move">True to move, else copy the file to the destination.</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown on failure.</exception>
		private static void FileCopyMove(string source, string dest, bool flag, bool move)
		{
			ThreadAccessors.A_LastError = 0;
			var failures = 0;
			var dfull = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar);
			var sfull = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);

			if (Directory.Exists(sfull))
				sfull += $"{Path.DirectorySeparatorChar}*";

			if (Directory.Exists(dfull))
				dfull += $"{Path.DirectorySeparatorChar}*";

			var sfname = Path.GetFileName(sfull);
			var dfname = Path.GetFileName(dfull);
			var sdname = Path.GetDirectoryName(sfull);
			var ddname = Path.GetDirectoryName(dfull);
			var files = Directory.GetFiles(sdname, sfname, System.IO.SearchOption.TopDirectoryOnly);

			if (files.Length == 0 && sfull.AsSpan().IndexOfAny(wildcardsSv) == -1)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				_ = Errors.ErrorOccurred($"{sfull} did not contain any files.");
				return;
			}

			if (!Directory.Exists(ddname))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				_ = Errors.ErrorOccurred($"Folder {ddname} did not exist.");
				return;
			}

			foreach (var f in files)
			{
				try
				{
					var name = Path.GetFileName(f);
					var dname = dfname.Contains('*') ? ExpandFilenameWildcard(name, dfname) : dfname;
					var s = Path.Combine(sdname, name);
					var d = Path.Combine(ddname, dname);

					if (Directory.Exists(s))//Ensure it's not a folder (again) just to be safe. AHK did this.
						continue;

					if (move)
						File.Move(s, d, flag);
					else
						File.Copy(s, d, flag);
				}
				catch { failures++; }
			}

			if (failures > 0)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				_ = Errors.ErrorOccurred($"Failed {failures} times moving or copying files.", "", failures);
				return;
			}
		}
	}

	public static partial class KeysharpEnhancements
	{
		/// <summary>
		/// Returns the full path to the directory of the specified filename.<br/>
		/// Ex: C:\dir1\dir2\file.txt => C:\dir1\dir2
		/// </summary>
		/// <param name="filename">The filename to examine.</param>
		/// <returns>The full path to the directory of the specified filename, without the trailing directory separator.</returns>
		public static string FileDirName(object filename) => Path.GetDirectoryName(Path.GetFullPath(filename.As()));

		/// <summary>
		/// Returns the full path of a filename which is assumed to be relative to <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </summary>
		/// <param name="filename">The filename to examine.</param>
		/// <returns>The full path to filename.</returns>
		public static string FileFullPath(object filename) => Path.GetFullPath(filename.As());
	}

#if LINUX
	/// <summary>
	/// Helper class to create a shortcut on linux.
	/// </summary>
	internal class ShortcutCreator
	{
		private Dictionary<string, string> fields = new Dictionary<string, string>()
		{
			{ "Type", "Application" },
			{ "Name", "" },
			{ "Comment", "" },
			{ "Icon", "" },
			{ "Exec", "" },
			{ "Path", "" },
		};

		internal ShortcutCreator()
		{
		}

		internal ShortcutCreator(string path)
		{
			var lines = File.ReadAllLines(path);

			if (lines.Length > 0)
			{
				if (lines[0].StartsWith("[Desktop Entry]", StringComparison.OrdinalIgnoreCase))
				{
					for (var i = 1; i < lines.Length; i++)
					{
						var splits = lines[i].Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

						if (splits.Length > 1)
						{
							Add(splits[0], splits[1]);
						}
					}
				}
			}
		}

		internal void Add(string name, string value)
		{
			if (fields.ContainsKey(name))
				fields[name] = value;
		}

		internal string Get(string name) => fields.TryGetValue(name, out var val) ? val : "";

		internal void Save(string path) => File.WriteAllText(path, Write());

		internal string Write()
		{
			return "[Desktop Entry]" + Environment.NewLine + string.Join(Environment.NewLine, fields.Where(kv => kv.Value != "").Select(kv => $"{kv.Key}={kv.Value}"));
		}
	}
#endif
}