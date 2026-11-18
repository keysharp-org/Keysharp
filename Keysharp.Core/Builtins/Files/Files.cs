using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
#if WINDOWS
using Microsoft.VisualBasic.FileIO;
#elif OSX
using Foundation = MonoMac.Foundation;
#endif

namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for file-related functions.
	/// </summary>
	public static class Files
	{
		private static readonly SearchValues<char> wildcardsSv = SearchValues.Create("*?");
		private static readonly SearchValues<char> fileOptionSeparators = SearchValues.Create(" \t\n");
		private static readonly System.IO.EnumerationOptions timestampMatches = new()
		{
			AttributesToSkip = 0, IgnoreInaccessible = false, MatchType = MatchType.Win32, MatchCasing = MatchCasing.CaseInsensitive,
		};
		// Opened on first use: a GUI-subsystem process that touches the console before the script allocates one is
		// pinned to the null stream.
		private static Stream standardOutput, standardError;
		private static void EnsureFilePermission(string path, FilePermissionAccess access, string operation)
		{
			if (string.IsNullOrEmpty(path) || path.StartsWith("*", StringComparison.Ordinal))
				return;

			_ = Script.TheScript.Permissions.EnsureFileAccess(path, access, operation: operation);
		}

		private static void WriteStandardStream(string value, Encoding encoding, bool error)
		{
			var stream = error ? standardError : standardOutput;

			if (stream == null)
			{
				// Encoded bytes reach the process's own streams only when the process is Keysharp's: the launcher or a
				// compiled script. A test host or an embedding application may have replaced Console.Out with a capture.
				if (Assembly.GetEntryAssembly() is not { } entry
						|| entry != Script.TheScript.ProgramType?.Assembly && entry.GetName().Name != "Keysharp")
				{
					(error ? Console.Error : Console.Out).Write(value);
					return;
				}

				stream = error ? standardError = Console.OpenStandardError() : standardOutput = Console.OpenStandardOutput();
			}

			stream.Write(encoding == null ? MemoryMarshal.AsBytes(value.AsSpan()) : encoding.GetBytes(value));
		}

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
			if (!filename.CoerceString(out var file))
				return DefaultObject;

			ThreadAccessors.A_LastError = 0;

			if (text == null)
				return Errors.ValueErrorOccurred("Missing a required parameter: Text");

			if (!options.CoerceString(out var opts))
				return DefaultObject;

			var t = text;

			// Converted before any file is opened, so that returning on a continued error leaves none open.
			if (t is not (string or Buffer or byte[]))
			{
				if (!t.CoerceString(out var converted))
					return DefaultObject;

				t = converted;
			}

			var info = filename == null ? Loops.Peek(LoopType.File) : null;
			// Null means RAW; a Buffer gets a BOM only when an encoding is specified.
			var encoding = t is string ? EffectiveFileEncoding : null;

			if (info?.outputStream == null && !TryParseFileOptions(opts, false, ref encoding, out _))
				return DefaultObject;

			var crlf = opts.Contains('\n');
			var target = info?.filename ?? file;
			var isStandardStream = target is "*" or "**";

			if (isStandardStream && t is not string)
				return Errors.TypeErrorOccurred(t, typeof(string));

			if (target.Length == 0)
				return Errors.ValueErrorOccurred("FileAppend requires a file name, or a Loop Read with an output file when it is omitted.");

			EnsureFilePermission(target, FilePermissionAccess.Append, "FileAppend");

			try
			{
				if (isStandardStream)
				{
					var s = (string)t;
#if DEBUG

					if (s == "fail")
						_ = Diagnostics.Debug.WriteLine(s);

#endif
					WriteStandardStream(crlf && encoding != null ? TextStream.InsertCarriageReturns(s, '\0') : s, encoding, target == "**");
				}
				// The loop owns, and closes when it ends, only an output file it opened, never a standard stream.
				else if (info != null)
				{
					var raw = info.outputStream == null ? encoding == null : info.outputRaw;

					if (info.outputStream == null)
					{
						info.outputStream = OpenForAppend(target, encoding, crlf);
						// AHK applies a detected BOM to subsequent writes, while the first uses the requested RAW mode.
						info.outputRaw = raw && !info.outputStream.DetectedByteOrderMark;
					}

					Append(info.outputStream, t, raw);
				}
				else
				{
					using var stream = OpenForAppend(target, encoding, crlf);
					Append(stream, t, encoding == null);
				}

				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error appending text to file {target}");
			}
		}

		/// <summary>
		/// Copies one or more files.
		/// </summary>
		/// <param name="sourcePattern">The name of a single file or folder, or a wildcard pattern such as "C:\Temp\*.tmp".<br/>
		/// sourcePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="destPattern">The name or pattern of the destination, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
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
			if (!sourcePattern.CoerceString(out var source) || !destPattern.CoerceString(out var dest))
				return DefaultObject;

			EnsureFilePermission(source, FilePermissionAccess.Read, "FileCopy source");
			EnsureFilePermission(dest, FilePermissionAccess.Write, "FileCopy destination");
			FileCopyMove(source, dest, overwrite.Ab(), false);
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
			if (!target.CoerceString(out var t) || !linkFile.CoerceString(out var l) || !workingDir.CoerceString(out var w)
					|| !args.CoerceString(out var a) || !description.CoerceString(out var d) || !iconFile.CoerceString(out var icon))
				return DefaultObject;

			EnsureFilePermission(t, FilePermissionAccess.Read, "FileCreateShortcut target");
			EnsureFilePermission(l, FilePermissionAccess.Write, "FileCreateShortcut link");

			if (t == "")
				return Errors.ValueErrorOccurred("Shortcut target cannot be an empty string.");

#if !WINDOWS
			if (!shortcutType.CoerceString(out var type))
				return DefaultObject;

			try
			{
				DesktopEntry.Save(t, Path.GetFullPath(l), w, a, d, icon, type);
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Error creating shortcut {l}");
			}
#elif WINDOWS
			t = Path.GetFullPath(t);
			if (!shortcutKey.CoerceString(out var sc))
				return DefaultObject;

			if (!iconNumber.CoerceInt(out var iconNum))
				return DefaultObject;

			if (!runState.CoerceInt(out var state, 1))
				return DefaultObject;
			var shellLink = (WindowsAPI.IShellLinkW)new WindowsAPI.ShellLink();
			var persistFile = (IPersistFile)shellLink;
			iconNum -= iconNum > 0 ? 1 : 0; // Convert 1-based index to 0-based, but leave negative resource IDs as-is.
			shellLink.SetPath(t);
			shellLink.SetWorkingDirectory(w);
			shellLink.SetArguments(a);
			shellLink.SetDescription(d);
			shellLink.SetIconLocation(icon, iconNum);
			shellLink.SetShowCmd(state);
			if (sc != string.Empty)
			{
				int mods = 0;
				int i = 0;
				foreach (char ch in sc)
				{
					if (ch == '^')
						mods |= WindowsAPI.HOTKEYF_CONTROL;
					else if (ch == '!')
						mods |= WindowsAPI.HOTKEYF_ALT;
					else if (ch == '+')
						mods |= WindowsAPI.HOTKEYF_SHIFT;
					else
						break;
					++i;
				}

				// For backwards compatibility: if modifiers omitted, assume CTRL+ALT.
				if (0 == mods)
					mods = WindowsAPI.HOTKEYF_CONTROL | WindowsAPI.HOTKEYF_ALT;

				// If badly formatted, it's not a critical error, just continue.
				uint? dummy = null;
				uint vk = Script.TheScript.HookThread.TextToVK(sc[i..], ref dummy, layout: null);
				if (vk != 0)
					shellLink.SetHotkey( (short)((ushort)vk | mods << 8) );
			}

			persistFile.Save(Path.GetFullPath(l), true);
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
			if (!filePattern.CoerceString(out var s))
				return DefaultObject;

			if (s.Length == 0)
				return Errors.InvalidParameterErrorOccurred(1, "FileDelete", s);

			EnsureFilePermission(s, FilePermissionAccess.Write, "FileDelete");

			if (s.AsSpan().IndexOfAny(wildcardsSv) == -1)
			{
				try
				{
					// File.Delete ignores a missing file, which AutoHotkey reports.
					_ = File.GetAttributes(s);
					File.Delete(s);
					return DefaultObject;
				}
				catch (Exception ex)
				{
					return Errors.OSErrorOccurred(ex, $"Error deleting file {s}");
				}
			}

			var failures = 0;

			foreach (var file in MatchFiles(s, files: true, dirs: false))
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
				return Errors.ErrorOccurred($"Failed {failures} times deleting files.", null, failures);

			return DefaultObject;
		}

		/// <summary>
		/// Sets the default encoding for <see cref="FileRead"/>, <see cref="LoopRead"/>, <see cref="FileAppend"/>, and <see cref="FileOpen"/>,
		/// returning the previous one.
		/// </summary>
		/// <param name="encoding">Specify one of the following values:<br/>
		/// CP0 or empty string: The system default ANSI code page. See remarks below.<br/>
		/// UTF-8: Unicode UTF-8, equivalent to CP65001.<br/>
		/// UTF-8-RAW: As above, but no byte order mark is written when a new file is created.<br/>
		/// UTF-16: Unicode UTF-16 with little endian byte order, equivalent to CP1200.<br/>
		/// UTF-16-RAW: As above, but no byte order mark is written when a new file is created.<br/>
		/// CPnnn: A code page with numeric identifier nnn. See Code Page Identifiers.<br/>
		/// nnn: A numeric code page identifier.
		/// </param>
		public static object FileEncoding(object encoding)
		{
			var prev = A_FileEncoding;

			if (!TryGetFileEncoding(encoding, out var enc))
				return DefaultObject;

			ThreadAccessors.A_FileEncoding = enc;
			return prev;
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
			if (!filePattern.CoerceString(out var s) || s.Length == 0)
				return "";

			// As in AutoHotkey, a file with none of the listed attributes reports "X", so that the result is still true.
			foreach (var found in MatchFiles(s, files: true, dirs: true))
				return Conversions.FromFileAttribs(found.Attributes) is { Length: > 0 } attribs ? attribs : "X";

			return DefaultErrorString;
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
			if (!filename.CoerceString(out var s))
				return "";

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
				return (string)Errors.OSErrorOccurred(ex, $"Error getting file attributes for file {s}");
			}

			return DefaultErrorString;
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
		/// Takes different action based on whether it's a symlink or a desktop entry.<br/>
		/// If it's a symlink, then outTarget receives its final target, outDir that target's folder, and all others are blank.<br/>
		/// If it's a desktop entry, then the following mapping will be used to populate the passed in reference variables:<br/>
		/// outTarget=the program of Exec, or the link's URL (local path for file URLs)<br/>
		/// outDir=Path<br/>
		/// outArgs=the arguments of Exec, in Windows command-line form<br/>
		/// outDescription=Comment<br/>
		/// outIcon=Icon<br/>
		/// outType=Type<br/>
		/// outRunState is blank<br/>
		/// Any other file raises an <see cref="OSError"/>.
		/// </summary>
		/// <param name="linkFile">Name of the shortcut file to be analyzed, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// </param>
		/// <param name="outTarget">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the shortcut's target, not including any arguments.</param>
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
											 [ByRef] object outTarget = null,
											 [ByRef] object outDir = null,
											 [ByRef] object outArgs = null,
											 [ByRef] object outDescription = null,
											 [ByRef] object outIcon = null,
#if WINDOWS
											 [ByRef] object outIconNum = null,
#else
											 [ByRef] object outType = null,
#endif
											 [ByRef] object outRunState = null)
		{
			if (!linkFile.CoerceString(out var linkText))
				return DefaultObject;

			string link = null;
			try
			{
				link = Path.GetFullPath(linkText);
#if !WINDOWS
				var linkInfo = new FileInfo(link);

				if (linkInfo.LinkTarget is { } linkTarget)
				{
					// The final target, which need not exist.
					var dest = linkInfo.ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(linkTarget, Path.GetDirectoryName(link));
					if (outTarget != null) Refs.SetValue(outTarget, dest);
					if (outDir != null) Refs.SetValue(outDir, Path.GetDirectoryName(dest));
					if (outArgs != null) Refs.SetValue(outArgs, "");
					if (outDescription != null) Refs.SetValue(outDescription, "");
					if (outIcon != null) Refs.SetValue(outIcon, "");
					if (outType != null) Refs.SetValue(outType, "");
				}
				else
				{
					var entry = DesktopEntry.Read(link);
					if (outTarget != null || outArgs != null)
					{
						var (target, args) = DesktopEntry.ReadCommand(entry, outArgs != null);
						if (outTarget != null) Refs.SetValue(outTarget, target);
						if (outArgs != null) Refs.SetValue(outArgs, args);
					}
					if (outDir != null) Refs.SetValue(outDir, entry.GetValueOrDefault("Path", entry.GetValueOrDefault("X-Keysharp-WorkingDirectory", "")));
					if (outDescription != null) Refs.SetValue(outDescription, entry.GetValueOrDefault("Comment", ""));
					if (outIcon != null) Refs.SetValue(outIcon, entry.GetValueOrDefault("Icon", ""));
					if (outType != null) Refs.SetValue(outType, entry.GetValueOrDefault("Type", ""));
				}

				if (outRunState != null) Refs.SetValue(outRunState, "");
#elif WINDOWS

				var shellLink = (WindowsAPI.IShellLinkW)new WindowsAPI.ShellLink();
				var persistFile = (IPersistFile)shellLink;
				persistFile.Load(Path.GetFullPath(link), 0);
				var sb = new StringBuilder(1024);
				if (outTarget != null)
				{
					shellLink.GetPath(sb, sb.Capacity, 0, 0x2); // SLGP_UNCPRIORITY
					string TargetPath = sb.ToString();
					Refs.SetValue(outTarget, TargetPath);
				}
				if (outDir != null)
				{
					shellLink.GetWorkingDirectory(sb, sb.Capacity);
					string WorkingDirectory = sb.ToString();
					Refs.SetValue(outDir, WorkingDirectory);
				}
				if (outArgs != null) {
					shellLink.GetArguments(sb, sb.Capacity);
					string Arguments = sb.ToString();
					Refs.SetValue(outArgs, Arguments);
				}
				if (outDescription != null) {
					shellLink.GetDescription(sb, sb.Capacity);
					string Description = sb.ToString();
					Refs.SetValue(outDescription, Description);
				}
				if (outIcon != null || outIconNum != null)
				{
					shellLink.GetIconLocation(sb, sb.Capacity, out int icon_index);
					var iconstr = sb.ToString();
					if (outIcon != null)
						Refs.SetValue(outIcon, iconstr);
					if (outIconNum != null)
					{
						if (iconstr.Length > 0)
							icon_index += (icon_index >> 31) ^ 1; // Convert from 0-based to 1-based for consistency with the Menu command, etc. but leave negative resource IDs as-is.
						Refs.SetValue(outIconNum, iconstr.Length > 0 ? icon_index : "");
					}
				}
				if (outRunState != null)
				{
					shellLink.GetShowCmd(out int WindowStyle);
					Refs.SetValue(outRunState, WindowStyle);
				}
#endif
			}
			catch (Exception ex)
			{
				if (outTarget != null) Refs.SetValue(outTarget, null);
				if (outDir != null) Refs.SetValue(outDir, null);
				if (outArgs != null) Refs.SetValue(outArgs, null);
				if (outDescription != null) Refs.SetValue(outDescription, null);
				if (outIcon != null) Refs.SetValue(outIcon, null);
#if WINDOWS
				if (outIconNum != null) Refs.SetValue(outIconNum, null);
#else
				if (outType != null) Refs.SetValue(outType, null);
#endif
				if (outRunState != null) Refs.SetValue(outRunState, null);
				return Errors.OSErrorOccurred(ex, $"Error getting shortcut information for {linkFile}");
			}
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

			if (!filename.CoerceString(out var file) || !units.CoerceString(out var u))
				return 0L;

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
			ThreadAccessors.A_LastError = 0;

			if (!CoerceFilePattern(filename, out var file) || !whichTime.CoerceString(out var time))
				return "";

			if (file.Length == 0)
				return (string)Errors.InvalidParameterErrorOccurred(1, "FileGetTime", file);

			var which = TimestampKind(time);

			if (which is not ('M' or 'C' or 'A'))
				return (string)Errors.ValueErrorOccurred($"Invalid WhichTime \"{time}\". Expected M, C or A.", time);

			try
			{
				FileSystemInfo info;
				if (file.AsSpan().IndexOfAny(wildcardsSv) < 0)
				{
					info = new FileInfo(file);
					// FileInfo caches the timestamps with Attributes, but returns -1 for a missing file or parent.
					if ((int)info.Attributes == -1)
						_ = File.GetAttributes(file);
				}
				else
				{
					var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file)));
					info = dir.EnumerateFileSystemInfos(Path.GetFileName(file), timestampMatches).FirstOrDefault()
						?? throw new FileNotFoundException("No matching file.", file);
				}

				return Conversions.ToYYYYMMDDHH24MISS(which switch
				{
					'C' => info.CreationTime,
					'A' => info.LastAccessTime,
					_ => info.LastWriteTime,
				});
			}
			catch (Exception ex)
			{
				var code = FileErrorCode(ex);
				ThreadAccessors.A_LastError = code;
				return (string)Errors.OSErrorOccurred(OperatingSystem.IsWindows() && code != 0 ? code : (object)ex, $"Error getting file time for {file}");
			}
		}

		// Looking up a loop's path walks the thread's loop stack and can resolve the directory's on-disk casing.
		private static bool CoerceFilePattern(object value, out string file) =>
			value.CoerceString(out file, value is string ? null : A_LoopFileFullPath);

		// Managed file operations can leave the P/Invoke error unchanged.
		private static int FileErrorCode(Exception error) => error switch
		{
			Win32Exception native => native.NativeErrorCode,
			IOException { HResult: > 0 } io => io.HResult,
			{ InnerException: { } inner } => FileErrorCode(inner),
			_ => (uint)error.HResult >> 16 == 0x8007 ? error.HResult & 0xffff : 0,
		};

		private static char TimestampKind(string whichTime) => whichTime.Length == 0 ? 'M' : char.ToUpperInvariant(whichTime[0]);

		/// <summary>
		/// Retrieves the version of a file.
		/// </summary>
		/// <param name="filename">If omitted, the current file of the innermost enclosing file loop will be used.<br/>
		/// Otherwise, specify the name of the target file.
		/// </param>
		/// <returns>The file version as four numbers, such as 1.2.3.4.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure, such as if the file lacks version information.</exception>
		public static string FileGetVersion(object filename = null)
		{
			ThreadAccessors.A_LastError = 0;

			if (!CoerceFilePattern(filename, out var file))
				return "";

			if (file.Length == 0)
				return (string)Errors.InvalidParameterErrorOccurred(1, "FileGetVersion", file);

#if WINDOWS
			// As AutoHotkey does, the fixed numeric version, not the FileVersion text, which may carry more than numbers.
			var size = WindowsAPI.GetFileVersionInfoSize(file, out _);
			var block = size > 0 ? Marshal.AllocHGlobal(size) : 0;

			try
			{
				if (block != 0 && WindowsAPI.GetFileVersionInfo(file, 0, size, block)
						&& WindowsAPI.VerQueryValue(block, "\\", out var fixedInfo, out var length) && length >= 16)
				{
					// dwFileVersionMS and dwFileVersionLS of VS_FIXEDFILEINFO.
					var ms = (uint)Marshal.ReadInt32(fixedInfo, 8);
					var ls = (uint)Marshal.ReadInt32(fixedInfo, 12);
					return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
				}

				var error = Marshal.GetLastPInvokeError();
				ThreadAccessors.A_LastError = error == 0 ? 1812 : error; // ERROR_RESOURCE_DATA_NOT_FOUND
				return (string)Errors.OSErrorOccurred(null, $"Error getting file version for file {file}");
			}
			finally
			{
				if (block != 0)
					Marshal.FreeHGlobal(block);
			}
#else
			try
			{
#if OSX
				// A folder has no version of its own, so a bundle's Info.plist is the only source.
				if (Directory.Exists(file))
					return MacBundleVersion(file, false) ?? throw new InvalidDataException($"{file} is a folder without bundle version information.");
#endif
				// Outside Windows, .NET reads only a managed assembly's AssemblyFileVersion, and reports nothing for other files.
				var info = FileVersionInfo.GetVersionInfo(file);

				if (!string.IsNullOrEmpty(info.FileVersion))
					return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
#if OSX
				// A bundle's executable usually carries no version, while the bundle that names it does.
				if (MacBundleVersion(file, true) is { } bundleVersion)
					return bundleVersion;
#endif
				throw new InvalidDataException($"{file} has no version information.");
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = FileErrorCode(ex);
				return (string)Errors.OSErrorOccurred(ex, $"Error getting file version for file {file}");
			}
#endif
		}

#if OSX
		private static string MacBundleVersion(string path, bool asExecutable)
		{
			using var pool = new Foundation.NSAutoreleasePool();
			var file = new FileInfo(Path.GetFullPath(path));
			var resolved = file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
			var bundle = asExecutable ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(resolved))) : resolved;
			if (bundle == null || asExecutable && Path.Combine(bundle, "Contents", "MacOS", Path.GetFileName(resolved)) != resolved)
				return null;

			var plist = Path.Combine(bundle, "Contents", "Info.plist");
			if (!asExecutable && !File.Exists(plist))
				plist = Path.Combine(bundle, "Resources", "Info.plist");

			// Read the file on each call so bundle metadata edits are visible within the process.
			using var info = Foundation.NSDictionary.FromFile(plist);
			if (info == null || asExecutable && Text(info["CFBundleExecutable"]) != Path.GetFileName(resolved))
				return null;

			return Text(info["CFBundleShortVersionString"]) ?? Text(info["CFBundleVersion"]);

			static string Text(Foundation.NSObject value) =>
				value is Foundation.NSString text && ((string)text).Trim() is { Length: > 0 } version ? version : null;
		}

#endif

		/// <summary>
		/// Extracts a file embedded into the script's assembly via `#App { Files: [...] }` to dest. Source must
		/// match the path listed there (relative to the script, / or \ separators). When the file is not embedded,
		/// source resolves relative to the live script and the call uses <see cref="FileCopy"/> semantics.
		/// </summary>
		/// <param name="source">The embedded logical path or live script-relative source path.</param>
		/// <param name="dest">The destination file name, assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified. Its parent directory must exist.</param>
		/// <param name="overwrite">True to overwrite an existing destination file; if omitted or false, an existing file raises an error.</param>
		/// <exception cref="Error">An <see cref="Error"/> is thrown on failure.</exception>
		public static object FileInstall(object source, object dest, object overwrite = null)
		{
			if (!source.CoerceString(out var src) || !dest.CoerceString(out var destText))
				return DefaultObject;

			var asm = Accessors.GetAssembly();
			var resourceKey = AppResourcePath.Normalize(src);
			var wanted = resourceKey != null ? AppManifest.FileResourceName(resourceKey) : null;
			var stream = wanted != null ? asm?.GetManifestResourceStream(wanted) : null;

			if (stream == null && asm != null && wanted != null)
			{
				var match = asm.GetManifestResourceNames().FirstOrDefault(n =>
								n.StartsWith(AppManifest.FileResourcePrefix, StringComparison.Ordinal)
								&& n.Equals(wanted, StringComparison.OrdinalIgnoreCase));

				if (match != null)
					stream = asm.GetManifestResourceStream(match);
			}

			if (stream == null)
			{
				var sourcePath = AppResourcePath.ToFileSystemPath(src);
				var destPath = AppResourcePath.ToFileSystemPath(destText);

				try
				{
					if (!Path.IsPathRooted(sourcePath) && Accessors.A_ScriptDir is { Length: > 0 } scriptDir)
						sourcePath = Path.Combine(scriptDir, sourcePath);

					var sourceFull = Path.GetFullPath(sourcePath);
					var destFull = Path.GetFullPath(destPath);
					var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

					if (File.Exists(sourceFull) && sourceFull.Equals(destFull, comparison))
						return DefaultObject;
				}
				catch (Exception ex) when (ex is not KeysharpException)
				{
					return Errors.ErrorOccurred($"FileInstall of {src} failed: {ex.Message}");
				}

				return FileCopy(sourcePath, destPath, overwrite);
			}

			using (stream)
			{
				var dst = AppResourcePath.ToFileSystemPath(destText);
				EnsureFilePermission(dst, FilePermissionAccess.Write, "FileInstall destination");

				try
				{
					var destPath = Path.GetFullPath(dst);
					var destDir = Path.GetDirectoryName(destPath);

					if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
						return Errors.ErrorOccurred($"Folder {destDir} did not exist.");

					using var fs = new FileStream(destPath, overwrite.Ab() ? FileMode.Create : FileMode.CreateNew,
						FileAccess.Write, FileShare.None);
					stream.CopyTo(fs);
				}
				catch (Exception ex) when (ex is not KeysharpException)
				{
					return Errors.ErrorOccurred($"FileInstall of {src} failed: {ex.Message}");
				}
			}

			return DefaultObject;
		}

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
			if (!sourcePattern.CoerceString(out var source) || !destPattern.CoerceString(out var dest))
				return DefaultObject;

			EnsureFilePermission(source, FilePermissionAccess.ReadWrite, "FileMove source");
			EnsureFilePermission(dest, FilePermissionAccess.ReadWrite, "FileMove destination");
			FileCopyMove(source, dest, overwrite.Ab(), true);
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
		/// <param name="encoding">If omitted, the default encoding (as set by FileEncoding) will be used. An empty string selects CP0.<br/>
		/// Otherwise, specify the encoding or code page to use for text I/O, e.g. "UTF-8", "UTF-16", "CP936" or 936.<br/>
		/// If the file contains a UTF-8 or UTF-16 byte order mark(BOM), or if the h(handle) flag is used,<br/>
		/// this parameter and the default encoding will be ignored, unless the file is being opened with write-only access (i.e.the previous contents of the file are being discarded).
		/// </param>
		/// <returns>A new KeysharpFile object encapsulating the open handle to the file.</returns>
		/// <exception cref="ValueError">Thrown if the encoding cannot be resolved.</exception>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileOpen(object filename, object flags, object encoding = null)
		{
			ThreadAccessors.A_LastError = 0;

			if (!filename.CoerceString(out var file) || !TryGetEncoding(encoding, encoding == null ? EffectiveFileEncoding : NativeType.AnsiEncoding, out var enc))
				return DefaultObject;

			// A number is AutoHotkey's flag values summed.
			if (!flags.TryCoerceLong(out var bits))
			{
				if (!flags.CoerceString(out var f))
					return DefaultObject;

				if (!TryParseOpenFlags(f, out bits))
					return Errors.ValueErrorOccurred("Invalid flags.", f);
			}

			var mode = (TextStreamMode)(bits & 3);
			var eol = bits & (TextStream.EolCrlf | TextStream.EolOrphanCr);

			if (file is "*" or "**")
				return new KeysharpFile(mode is TextStreamMode.Read or TextStreamMode.Update ? Console.In : null,
										mode == TextStreamMode.Read ? null : file == "*" ? Console.Out : Console.Error, eol);

			var useHandle = (bits & TextStream.UseHandle) != 0;

			if (!useHandle)
				EnsureFilePermission(file, mode switch
				{
					TextStreamMode.Read => FilePermissionAccess.Read,
					TextStreamMode.Write => FilePermissionAccess.Write,
					_ => FilePermissionAccess.ReadWrite
				}, "FileOpen");

			try
			{
				// A handle stays the caller's to close and is taken as it stands, with no byte order mark handling.
				return new KeysharpFile(useHandle
										? new TextStream(new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle((nint)(file.ParseLong() ?? 0), false), FileAccess.ReadWrite), enc, eol)
										: TextStream.Open(file, mode, (FileShare)(int)((bits >> 8) & 7), enc, eol, true), !useHandle);
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
			if (!filename.CoerceString(out var file))
				return DefaultObject;

			EnsureFilePermission(file, FilePermissionAccess.Read, "FileRead");

			if (!options.CoerceString(out var opts))
				return DefaultObject;

			ThreadAccessors.A_LastError = 0;

			if (string.IsNullOrEmpty(file))
				return Errors.ValueErrorOccurred("FileRead requires a file name.");

			// Null means RAW.
			var encoding = EffectiveFileEncoding;

			if (!TryParseFileOptions(opts, true, ref encoding, out var max))
				return DefaultObject;

			try
			{
				// Shared for writing and deletion, so that a log another process holds open can be read.
				using var stream = new FileStream(file, new FileStreamOptions
				{
					Mode = FileMode.Open,
					Access = FileAccess.Read,
					Share = FileShare.ReadWrite | FileShare.Delete,
					Options = FileOptions.SequentialScan,
					BufferSize = 0
				});
				// As AutoHotkey reads it, mN is a count of bytes, and a negative one is no limit.
				var limit = max < 0 ? long.MaxValue : max;
				string text;

				// A pipe, or a file such as those in /proc which reports no length, is read until it ends.
				if (!stream.CanSeek || stream.Length == 0)
				{
					using var memory = new MemoryStream();
					Span<byte> chunk = stackalloc byte[4096];

					for (int read; memory.Length < limit && (read = stream.Read(chunk[..(int)Math.Min(chunk.Length, limit - memory.Length)])) > 0;)
						memory.Write(chunk[..read]);

					var content = memory.GetBuffer().AsSpan(0, (int)memory.Length);

					if (encoding == null)
						return new Buffer(content.ToArray());

					text = DecodeFileText(content, encoding);
				}
				else
				{
					var length = Math.Min(stream.Length, limit);

					if (length > System.Array.MaxLength)
						return Errors.ErrorOccurred(new MemoryError("Out of memory."), DefaultObject);

					if (encoding == null)
					{
						var buffer = new Buffer(length);
						var read = stream.ReadAtLeast(buffer.AsSpan(), (int)length, false);

						if (read < length)
							buffer.Size = (long)read;

						return buffer;
					}

					// The shared pool keeps the arrays it lends, so only a small file borrows one.
					var rented = length <= 1 << 20 ? ArrayPool<byte>.Shared.Rent((int)length) : null;
					var bytes = rented ?? new byte[length];

					try
					{
						text = DecodeFileText(bytes.AsSpan(0, stream.ReadAtLeast(bytes.AsSpan(0, (int)length), (int)length, false)), encoding);
					}
					finally
					{
						if (rented != null)
							ArrayPool<byte>.Shared.Return(rented);
					}
				}

				return opts.Contains('\n') ? text.Replace("\r\n", "\n") : text;
			}
			catch (Exception ex) when (ex is not KeysharpException)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error reading file {file}");
			}
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
			if (!filePattern.CoerceString(out var s))
				return DefaultObject;

			if (s.Length == 0)
				return Errors.InvalidParameterErrorOccurred(1, "FileRecycle", s);

			EnsureFilePermission(s, FilePermissionAccess.Write, "FileRecycle");
			ThreadAccessors.A_LastError = 0;
			var found = false;

			try
			{
				// As SHFileOperation does in AutoHotkey, a wildcard matches folders as well as files.
				foreach (var target in MatchFiles(s, files: true, dirs: true))
				{
					found = true;
#if LINUX
					var result = RunCommand("gio", ["trash", target.FullName]);

					if (!result.Succeeded)
						return Errors.OSErrorOccurred(new InvalidOperationException(result.ErrorMessage),
							$"gio trash failed for {target.FullName}.");
#elif OSX
					MoveToMacTrash(target.FullName);
#elif WINDOWS
					if ((target.Attributes & FileAttributes.Directory) != 0)
						FileSystem.DeleteDirectory(target.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
					else
						FileSystem.DeleteFile(target.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
#endif
				}
			}
			catch (Exception ex) when (ex is not KeysharpException)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error recycling file(s) with pattern {s}");
			}

			// As SHFileOperation does in AutoHotkey, a name without wildcards must exist.
			if (!found && Path.GetFileName(s.AsSpan()).IndexOfAny(wildcardsSv) < 0)
			{
				ThreadAccessors.A_LastError = 2;//ERROR_FILE_NOT_FOUND
				return Errors.OSErrorOccurred(2L, $"Error recycling {s}");
			}

			return DefaultObject;
		}

		/// <summary>
		/// Empties the recycle bin.
		/// </summary>
		/// <param name="driveLetter">If omitted, the recycle bin for all drives is emptied. Otherwise, specify a drive letter such as C:\</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown on failure.</exception>
		public static object FileRecycleEmpty(object driveLetter = null)
		{
			if (!driveLetter.CoerceString(out var s))
				return DefaultObject;

			if (s.Contains('\0'))
				return Errors.InvalidParameterErrorOccurred(1, "FileRecycleEmpty", s);

			try
			{
#if LINUX
				if (s.Length > 0)
					Internals.Linux.Trash.Empty(s);
				else if (RunCommand("gio", ["trash", "--empty"]) is { Succeeded: false } result)
					return Errors.OSErrorOccurred(new InvalidOperationException(result.ErrorMessage), "gio trash --empty failed.");
#elif OSX
				EmptyMacTrash(s);
#elif WINDOWS
				var hr = WindowsAPI.SHEmptyRecycleBin(0, s != "" ? s : null, WindowsAPI.SHERB_NOCONFIRMATION | WindowsAPI.SHERB_NOPROGRESSUI | WindowsAPI.SHERB_NOSOUND);

				// As in AutoHotkey, E_UNEXPECTED is not an error: it is what an already empty recycle bin reports.
				if (hr < 0 && hr != unchecked((int)0x8000FFFF))
					return Errors.OSErrorOccurred((long)(uint)hr, $"Error emptying recycle bin for drive {s}");
#endif
				return DefaultObject;
			}
			catch (Exception ex) when (ex is not KeysharpException)
			{
				return Errors.OSErrorOccurred(ex, $"Error emptying recycle bin for drive {s}");
			}
		}

#if OSX
		// MonoMac has no binding for trashItemAtURL:resultingItemURL:error:.
		[DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool TrashItem(nint fileManager, nint selector, nint url, nint resultingUrl, out nint error);

		private static void MoveToMacTrash(string path)
		{
			using var pool = new Foundation.NSAutoreleasePool();
			using var url = Foundation.NSUrl.FromFilename(path);

			if (!TrashItem(Foundation.NSFileManager.DefaultManager.Handle, MonoMac.ObjCRuntime.Selector.GetHandle("trashItemAtURL:resultingItemURL:error:"),
						   url.Handle, 0, out var error))
				throw new IOException(MonoMac.ObjCRuntime.Runtime.GetNSObject(error) is Foundation.NSError e ? e.LocalizedDescription : $"Could not move {path} to the trash.");
		}

		private static void EmptyMacTrash(string volume)
		{
			using var pool = new Foundation.NSAutoreleasePool();
			var manager = Foundation.NSFileManager.DefaultManager;
			if (volume.Length == 0)
			{
				Empty(null);
				foreach (var mounted in manager.GetMountedVolumes(null, Foundation.NSVolumeEnumerationOptions.None) ?? [])
					Empty(mounted);
			}
			else
			{
				_ = File.GetAttributes(volume);
				using var url = Foundation.NSUrl.FromFilename(Path.GetFullPath(volume));
				Empty(url);
			}

			void Empty(Foundation.NSUrl volumeUrl)
			{
				using var trash = manager.GetUrl(Foundation.NSSearchPathDirectory.TrashDirectory, Foundation.NSSearchPathDomain.User, volumeUrl, false, out var lookupError);
				if (trash == null)
				{
					var missing = lookupError?.Domain switch
					{
						"NSCocoaErrorDomain" => lookupError.Code is 4 or 260, // NSFileNoSuchFileError, NSFileReadNoSuchFileError
						"NSPOSIXErrorDomain" => lookupError.Code is 2 or 20, // ENOENT, ENOTDIR
						_ => lookupError == null,
					};
					if (!missing)
						throw new IOException(lookupError.LocalizedDescription);
					return;
				}

				var folder = new DirectoryInfo(trash.Path);
				var attributes = folder.Attributes;
				if ((int)attributes == -1 || (attributes & FileAttributes.ReparsePoint) != 0)
					return;

				foreach (var entry in folder.EnumerateFileSystemInfos())
					if (!manager.Remove(entry.FullName, out var error))
						throw new IOException(error?.LocalizedDescription ?? $"Could not remove {entry.FullName}.");
			}
		}
#endif

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

			if (!attributes.CoerceString(out var attr) || !CoerceFilePattern(filePattern, out var file) || !mode.CoerceString(out var m))
				return DefaultObject;

			if (file.Length == 0)
				return Errors.InvalidParameterErrorOccurred(2, "FileSetAttrib", file);

			if (!Conversions.TryParseLoopMode(m, out var dofiles, out var dodirs, out var recurse))
				return DefaultObject;

			// An invalid attribute letter is an error for the call, as in AHK, not a failure for each file.
			if (!Conversions.TryParseFileAttribs(attr, out var masks))
				return DefaultObject;

			var failures = 0;

			foreach (var item in MatchFiles(file, dofiles, dodirs, recurse))
			{
				var path = item.FullName;

				try
				{
					var set = (item.Attributes & masks.Keep) ^ masks.Toggle;
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
				return Errors.ErrorOccurred($"Failed {failures} times setting file attributes.", null, failures);
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
		public static object FileSetTime([UserDeclaredName("YYYYMMDDHH24MISS")] object yyyymmddhh24miss = null,
			object filePattern = null, object whichTime = null, object mode = null)
		{
			ThreadAccessors.A_LastError = 0;

			if (!yyyymmddhh24miss.CoerceString(out var timestamp) || !CoerceFilePattern(filePattern, out var file)
					|| !whichTime.CoerceString(out var whichtime) || !mode.CoerceString(out var m))
				return DefaultObject;

			if (file.Length == 0)
				return Errors.InvalidParameterErrorOccurred(2, "FileSetTime", file);

			if (!Conversions.TryParseLoopMode(m, out var dofiles, out var dodirs, out var recurse))
				return DefaultObject;

			var which = TimestampKind(whichtime);

			if (which is not ('M' or 'C' or 'A'))
				return Errors.ValueErrorOccurred($"Invalid WhichTime \"{whichtime}\". Expected M, C or A.", whichtime);

			var failures = 0;

			var time = timestamp.Length == 0 ? DateTime.Now : Conversions.ToDateTime(timestamp);

			if (time == DateTime.MinValue)
				return Errors.ValueErrorOccurred($"Invalid timestamp \"{timestamp}\".");

			foreach (var item in MatchFiles(file, dofiles, dodirs, recurse))
			{
#if LINUX
				// Linux keeps no settable creation time, and .NET would change the modification time in its place.
				if (which == 'C')
				{
					failures++;
					ThreadAccessors.A_LastError = 95; // EOPNOTSUPP
					continue;
				}

#endif
				try
				{
					switch (which)
					{
						case 'C': item.CreationTime = time; break;
						case 'A': item.LastAccessTime = time; break;
						default: item.LastWriteTime = time; break;
					}
				}
				catch (Exception ex)
				{
					ThreadAccessors.A_LastError = FileErrorCode(ex);
					failures++;
				}
			}

			if (failures != 0)
			{
				return Errors.ErrorOccurred($"Failed {failures} times setting file time.", null, failures);
			}

			return DefaultObject;
		}

		private static void Append(TextStream stream, object value, bool raw)
		{
			_ = value switch
			{
				string s when !raw => stream.WriteText(s),
				string s => stream.WriteBytes(MemoryMarshal.AsBytes(s.AsSpan())),
				Buffer buffer => stream.WriteBytes(buffer.AsSpan()),
				byte[] bytes => stream.WriteBytes(bytes),
				_ => 0L
			};
			GC.KeepAlive(value);
		}

		// AHK takes UTF-16 as code units, preserving unpaired surrogates.
		private static string DecodeFileText(ReadOnlySpan<byte> bytes, Encoding encoding)
		{
			var mark = TextStream.DetectByteOrderMark(bytes, out var length);

			if (mark?.CodePage == 1200 || encoding.CodePage == 1200)
				return new string(MemoryMarshal.Cast<byte, char>(bytes[(mark?.CodePage == 1200 ? length : 0)..]));

			return (mark ?? encoding).GetString(bytes[length..]);
		}

		private static TextStream OpenForAppend(string path, Encoding encoding, bool crlf) =>
			TextStream.Open(path, TextStreamMode.Append, FileShare.Read, encoding ?? EffectiveFileEncoding, crlf ? TextStream.EolCrlf : 0, encoding != null);

		// `n can appear without a separating space. Null encoding means RAW.
		private static bool TryParseFileOptions(string options, bool takesMax, ref Encoding encoding, out long maxBytes)
		{
			maxBytes = long.MaxValue;

			foreach (Range r in options.AsSpan().SplitAny(fileOptionSeparators))
			{
				var option = options.AsSpan(r);

				if (option.IsEmpty || takesMax && Options.TryParse(option, "m", ref maxBytes))
					continue;

				if (option.Equals("raw", StringComparison.OrdinalIgnoreCase))
					encoding = null;
				else if (char.IsAsciiDigit(option[0]))
				{
					_ = Errors.ValueErrorOccurred("Invalid option.", option.ToString());
					return false;
				}
				else if (!TryGetEncoding(option.ToString(), out encoding))
					return false;
			}

			return true;
		}

		private static bool TryParseOpenFlags(string flags, out long bits)
		{
			var s = flags.AsSpan().TrimStart(SpaceTab);
			bits = 0;

			if (s.IsEmpty)
				return false;

			bits = char.ToLowerInvariant(s[0]) switch
			{
				'r' when s.StartsWith("rw", StringComparison.OrdinalIgnoreCase) => (long)TextStreamMode.Update,
				'r' => (long)TextStreamMode.Read,
				'w' => (long)TextStreamMode.Write,
				'a' => (long)TextStreamMode.Append,
				'h' => TextStream.UseHandle,
				_ => -1
			};

			if (bits < 0)
				return false;

			var start = bits == (long)TextStreamMode.Update ? 2 : 1;

			bits |= TextStream.ShareAll;

			for (var i = start; i < s.Length; i++)
			{
				switch (char.ToLowerInvariant(s[i]))
				{
					case '\n': bits |= TextStream.EolCrlf; break;

					case '\r': bits |= TextStream.EolOrphanCr; break;

					case ' ' or '\t': break;

					case '-':
						var dash = i;

						while (i + 1 < s.Length && char.ToLowerInvariant(s[i + 1]) is 'r' or 'w' or 'd')
							bits &= ~(char.ToLowerInvariant(s[++i]) switch
							{
								'r' => TextStream.ShareRead,
								'w' => TextStream.ShareWrite,
								_ => TextStream.ShareDelete
							});

						if (i == dash)
							bits &= ~TextStream.ShareAll;

						break;

					default: return false;
				}
			}

			return true;
		}

		/// <summary>
		/// The encoding a file function uses when the script names none: A_FileEncoding, or CP0 when that is unset.
		/// </summary>
		internal static Encoding EffectiveFileEncoding => ThreadAccessors.A_FileEncoding ?? NativeType.AnsiEncoding;

		/// <summary>
		/// The name AutoHotkey gives an encoding: UTF-8, UTF-16 or CPnnn, with -RAW for a Unicode encoding that writes
		/// no byte order mark when <paramref name="reportRaw"/> is set.
		/// </summary>
		internal static string EncodingName(Encoding encoding, bool reportRaw) => encoding.CodePage switch
		{
			65001 => reportRaw && encoding.Preamble.IsEmpty ? "UTF-8-RAW" : "UTF-8",
			1200 => reportRaw && encoding.Preamble.IsEmpty ? "UTF-16-RAW" : "UTF-16",
			var codePage => "CP" + codePage.ToString(CultureInfo.InvariantCulture)
		};

		/// <summary>
		/// Parses the file encoding setting, preserving CP0 as null.
		/// </summary>
		internal static bool TryGetFileEncoding(object value, out Encoding encoding) =>
			TryGetEncoding(value, null, resolveAnsi: false, out encoding);

		/// <summary>
		/// As <see cref="TryGetEncoding(object, Encoding, out Encoding)"/> for a dedicated encoding parameter:
		/// omitted means native UTF-16, and an explicit empty name means CP0.
		/// </summary>
		internal static bool TryGetEncoding(object s, out Encoding encoding) =>
			TryGetEncoding(s, s == null ? Encoding.Unicode : NativeType.AnsiEncoding, out encoding);

		/// <summary>
		/// Resolves an encoding name to an <see cref="Encoding"/>, falling back to the caller's default when the script
		/// named none.
		/// </summary>
		/// <param name="s">The encoding name: UTF-8, UTF-8-RAW, UTF-16, UTF-16-RAW, ASCII, a code page written
		/// either as CPnnn or as the bare number, or any other name .NET knows such as windows-1252. Empty or
		/// unset means <paramref name="def"/>.</param>
		/// <param name="def">The encoding to use when no name was given.</param>
		/// <param name="encoding">The corresponding <see cref="Encoding"/>.</param>
		/// <returns>False if the script continued the error raised for the name, and the caller then returns at once.</returns>
		/// <exception cref="TypeError">Thrown if the name does not convert to a string.</exception>
		/// <exception cref="ValueError">Thrown if the name is not one of those, or names a code page this system
		/// does not have.</exception>
		/// <remarks>A name which cannot be resolved is never quietly substituted: the caller would then read or
		/// write the wrong bytes and have no way to notice.</remarks>
		internal static bool TryGetEncoding(object s, Encoding def, out Encoding encoding) =>
			TryGetEncoding(s, def, resolveAnsi: true, out encoding);

		private static bool TryGetEncoding(object s, Encoding def, bool resolveAnsi, out Encoding encoding)
		{
			encoding = null;

			if (!s.CoerceString(out var name))
				return false;

			if (name.Length == 0)
			{
				encoding = def;
				return true;
			}

			var val = name.ToLowerInvariant();

			encoding = val switch
			{
				"ascii" or "us-ascii" => Encoding.ASCII,
				"utf-8" => Encoding.UTF8,
				"utf-8-raw" => new UTF8Encoding(false),//No byte order mark.
				"utf-16" or "unicode" => Encoding.Unicode,
				"utf-16-raw" => new UnicodeEncoding(false, false),//Little endian, no byte order mark.
				_ => null
			};

			if (encoding != null)
				return true;

			// Anything else which happens to start with "cp" falls through to the name lookup below.
			var number = val.StartsWith("cp") ? val.AsSpan(2) : val.AsSpan();

			if (int.TryParse(number, out var cp))
			{
				try
				{
					encoding = cp == 0 && !resolveAnsi ? null : Encoding.GetEncoding(cp);
					return true;
				}
				catch
				{
					_ = Errors.ValueErrorOccurred($"Code page {cp} is not available on this system.", val);
					return false;
				}
			}

			try
			{
				encoding = Encoding.GetEncoding(val);
				return true;
			}
			catch
			{
			}

			_ = Errors.ValueErrorOccurred("Unknown encoding. Specify UTF-8, UTF-8-RAW, UTF-16, UTF-16-RAW, ASCII, a code page as CPnnn or nnn, or a name .NET recognizes.", val);
			return false;
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

			if (Directory.Exists(dfull))
				dfull += $"{Path.DirectorySeparatorChar}*";

			var dfname = Path.GetFileName(dfull);
			var ddname = Path.GetDirectoryName(dfull);
			// Listed before the first move, so that a file renamed within the same folder is not found again.
			var files = MatchFiles(Directory.Exists(sfull) ? Path.Join(sfull, "*") : source, files: true, dirs: false).ToArray();

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
					var dname = dfname.Contains('*') ? ExpandFilenameWildcard(f.Name, dfname) : dfname;
					var d = Path.Combine(ddname, dname);

					if (move)
						File.Move(f.FullName, d, flag);
					else
						File.Copy(f.FullName, d, flag);
				}
				catch { failures++; }
			}

			if (failures > 0)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				_ = Errors.ErrorOccurred($"Failed {failures} times moving or copying files.", null, failures);
				return;
			}
		}

		/// <summary>
		/// The files and folders a pattern names, found as AutoHotkey's file functions find them: wildcards only in the
		/// last component, a relative path resolved against A_WorkingDir, and hidden and system entries included. A name
		/// without wildcards is that one item, file or folder, and with recursion the item of that name in every subfolder.
		/// </summary>
		/// <param name="files">Whether a wildcard matches files.</param>
		/// <param name="dirs">Whether a wildcard matches folders.</param>
		internal static IEnumerable<FileSystemInfo> MatchFiles(string pattern, bool files, bool dirs, bool recurse = false)
		{
			// Split as Loop Files splits it: a full path would lose the trailing period of "*.", which matches names without an extension.
			var name = Path.GetFileName(pattern);

			try
			{
				if (name.AsSpan().IndexOfAny(wildcardsSv) < 0)
				{
					if (!recurse)
					{
						var item = new FileInfo(Path.GetFullPath(pattern));
						var attributes = item.Attributes;
						if ((int)attributes == -1)
							return [];
						// DirectoryInfo opens a Windows directory with the backup semantics its time setters require.
						return [(attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(item.FullName) : item];
					}

					files = dirs = true;
				}

				var dir = pattern[..^name.Length];
				return Loops.GetFiles(Path.GetFullPath(dir.Length == 0 ? "." : dir), name, dirs, files, recurse).Select(static item => item.Info);
			}
			// As in AutoHotkey, a pattern that names no file, such as an empty one or one with a wildcard in a folder part, matches nothing.
			catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
			{
				return [];
			}
		}
	}

	public partial class Ks
	{
		/// <summary>
		/// Returns the full path of a filename which is assumed to be relative to <see cref="A_WorkingDir"/> if an absolute path isn't specified.
		/// </summary>
		/// <param name="filename">The filename to examine.</param>
		/// <returns>The full path to filename.</returns>
		public static string FileFullPath(object filename)
		{
			if (!filename.CoerceString(out var file))
				return "";

			return Path.GetFullPath(file);
		}

		/// <summary>
		/// Creates an empty temporary file and returns its full path.
		/// </summary>
		/// <returns>The full path of the newly created temporary file.</returns>
		public static string FileCreateTemp() => Path.GetTempFileName();
	}
#if !WINDOWS
	/// <summary>Freedesktop shortcuts on Linux and macOS.</summary>
	internal static class DesktopEntry
	{
		private const string Group = "[Desktop Entry]";
		// The characters for which the specification requires an Exec argument to be quoted.
		private static readonly SearchValues<char> execReserved = SearchValues.Create(" \t\n\"'\\><~|&;$*?#()`");

		internal static void Save(string target, string link, string workingDir, string args, string description, string icon, string type)
		{
			var temp = Path.Combine(Path.GetDirectoryName(link), Path.GetRandomFileName());
			var created = false;

			try
			{
				if (type == "" && workingDir == "" && args == "" && description == "" && icon == "")
				{
					_ = File.CreateSymbolicLink(temp, Path.GetFullPath(target));
					created = true;
				}
				else
				{
					var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
					if (!OperatingSystem.IsWindows())
					{
						// The umask also applies when replacing an existing launcher.
						const UnixFileMode permissions = (UnixFileMode)0x1ff;
						var mode = new FileInfo(link) is { Exists: true, LinkTarget: null } existing ? existing.UnixFileMode & permissions : permissions;
						options.UnixCreateMode = mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
					}
					using var stream = new FileStream(temp, options);
					created = true;
					using var writer = new StreamWriter(stream);
					Write(writer, target, link, workingDir, args, description, icon, type);
				}

				// Rename installs a complete shortcut without opening an existing link or modifying a hard-linked file.
				File.Move(temp, link, overwrite: true);
				created = false;
			}
			finally
			{
				if (created)
					File.Delete(temp);
			}
		}

		internal static (string Target, string Args) ReadCommand(Dictionary<string, string> entry, bool includeArgs)
		{
			// Non-application entries may also supply their target through Exec.
			using var exec = Arguments(entry.GetValueOrDefault("Exec", ""), desktop: true).GetEnumerator();
			var target = exec.MoveNext() ? exec.Current : "";
			if (entry.GetValueOrDefault("Type") == "Link" && entry.TryGetValue("URL", out var url))
				target = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : url;
			else if (entry.TryGetValue("X-Keysharp-Target", out var custom))
				target = custom;

			var args = "";
			if (includeArgs && !entry.TryGetValue("X-Keysharp-Args", out args))
				args = JoinArguments(exec);
			return (target, args);
		}

		/// <summary>The keys of the [Desktop Entry] group, with their values unescaped.</summary>
		/// <exception cref="InvalidDataException">The file is not a desktop entry.</exception>
		internal static Dictionary<string, string> Read(string path)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			var inGroup = false;

			foreach (var text in File.ReadLines(path))
			{
				var line = text.AsSpan().Trim(" \t");

				if (line.IsEmpty || line[0] == '#')
					continue;

				// The specification makes [Desktop Entry] the first group, so anything before it means another format.
				if (line[0] == '[' && inGroup)
					break;

				if (!inGroup && !line.SequenceEqual(Group))
					throw new InvalidDataException($"{path} is not a desktop entry.");

				inGroup = true;
				var eq = line.IndexOf('=');

				if (eq > 0)
					_ = values.TryAdd(line[..eq].TrimEnd(" \t").ToString(), Unescape(line[(eq + 1)..].TrimStart(" \t")));
			}

			return inGroup ? values : throw new InvalidDataException($"{path} is not a desktop entry.");
		}

		private static void Write(StreamWriter writer, string target, string link, string workingDir, string args, string description, string icon, string type)
		{
			type = long.TryParse(type, out var kind) ? kind switch { 2 => "Link", 3 => "Directory", _ => "Application" }
				: type.Length == 0 ? "Application" : type;

			writer.WriteLine(Group);
			Field("Type", type);
			Field("Name", Path.GetFileNameWithoutExtension(link));
			Field("Comment", description);
			if (icon.Length != 0)
				Field("Icon", Path.GetFullPath(icon));

			if (type == "Application")
			{
				var exec = AppendExecArgument(new StringBuilder(), Path.GetFullPath(target));
				foreach (var arg in Arguments(args, desktop: false))
					_ = AppendExecArgument(exec.Append(' '), arg);
				Field("Exec", exec.ToString());
				Field("Path", workingDir);
			}
			else
			{
				if (type == "Link")
					Field("URL", Uri.TryCreate(target, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : new Uri(Path.GetFullPath(target)).AbsoluteUri);
				else
					Field("X-Keysharp-Target", Path.GetFullPath(target));

				// Exec and Path are application keys.
				Field("X-Keysharp-Args", args);
				Field("X-Keysharp-WorkingDirectory", workingDir);
			}

			void Field(string key, string value)
			{
				if (value.Length == 0)
					return;
				writer.Write(key);
				writer.Write('=');
				writer.WriteLine(Escape(value));
			}
		}

		// Desktop Exec accepts shell-style quotes and escapes. Script Args follows ProcessStartInfo.Arguments on Unix.
		private static IEnumerable<string> Arguments(string text, bool desktop)
		{
			var arg = new StringBuilder();
			var i = 0;
			while (i < text.Length)
			{
				if (IsSeparator(text[i]))
				{
					i++;
					continue;
				}

				var quote = '\0';
				while (i < text.Length && (quote != '\0' || !IsSeparator(text[i])))
				{
					var c = text[i++];
					if (!desktop && c == '\\')
					{
						var start = i - 1;
						while (i < text.Length && text[i] == '\\')
							i++;
						var count = i - start;
						var beforeQuote = i < text.Length && text[i] == '"';
						_ = arg.Append('\\', beforeQuote ? count / 2 : count);
						if (beforeQuote && count % 2 != 0)
							_ = arg.Append(text[i++]);
					}
					else if (quote == '\0' && (c == '"' || desktop && c == '\''))
						quote = c;
					else if (quote != '\0' && c == quote)
					{
						if (!desktop && i < text.Length && text[i] == '"')
							_ = arg.Append(text[i++]);
						else
							quote = '\0';
					}
					else if (desktop && i < text.Length && IsExecEscape(c, text[i], quote))
						_ = arg.Append(text[i++]);
					else
						_ = arg.Append(c);
				}
				yield return arg.ToString();
				_ = arg.Clear();
			}

			bool IsSeparator(char c) => c is ' ' or '\t' || desktop && c == '\n';

			static bool IsExecEscape(char c, char next, char quote) => c switch
			{
				'%' => next == '%',
				'\\' => quote == '\0' || quote == '"' && next is '"' or '`' or '$' or '\\',
				_ => false,
			};
		}

		private static string JoinArguments(IEnumerator<string> args)
		{
			var result = new StringBuilder();
			while (args.MoveNext())
			{
				if (result.Length != 0)
					_ = result.Append(' ');
				var arg = args.Current;
				if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"'))
				{
					_ = result.Append(arg);
					continue;
				}

				// Backslashes double before the closing quote or an embedded quote.
				_ = result.Append('"');
				var slashes = 0;
				foreach (var c in arg)
				{
					if (c == '\\')
					{
						slashes++;
						continue;
					}
					_ = result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
					slashes = 0;
				}
				_ = result.Append('\\', slashes * 2).Append('"');
			}
			return result.ToString();
		}

		private static StringBuilder AppendExecArgument(StringBuilder sb, string arg)
		{
			if (arg.Length != 0 && arg.AsSpan().IndexOfAny(execReserved) < 0)
				return sb.Append(arg.Replace("%", "%%"));

			_ = sb.Append('"');
			foreach (var c in arg)
			{
				if (c == '%')
					_ = sb.Append('%');
				else if (c is '"' or '`' or '$' or '\\')
					_ = sb.Append('\\');

				_ = sb.Append(c);
			}

			return sb.Append('"');
		}

		private static string Escape(string value)
		{
			if (!value.StartsWith(' ') && !value.EndsWith(' ') && value.AsSpan().IndexOfAny("\\\n\t\r") < 0)
				return value;

			var escaped = value.Replace("\\", @"\\").Replace("\n", @"\n").Replace("\t", @"\t").Replace("\r", @"\r");
			// Escaping the outermost space protects the entire run from trimming.
			if (escaped.StartsWith(' '))
				escaped = string.Concat(@"\s", escaped.AsSpan(1));
			if (escaped.EndsWith(' '))
				escaped = string.Concat(escaped.AsSpan(0, escaped.Length - 1), @"\s");
			return escaped;
		}

		private static string Unescape(ReadOnlySpan<char> value)
		{
			if (value.IndexOf('\\') < 0)
				return value.ToString();

			var sb = new StringBuilder(value.Length);

			for (var i = 0; i < value.Length; i++)
			{
				var c = value[i];

				// An unknown escape is kept as written.
				if (c == '\\' && i + 1 < value.Length && value[i + 1] is 's' or 'n' or 't' or 'r' or '\\')
					c = value[++i] switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', _ => '\\' };

				_ = sb.Append(c);
			}

			return sb.ToString();
		}
	}
#endif
}
