namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for directory-related functions.
	/// </summary>
	public static class Dir
	{
		/// <summary>
		/// Copies a folder along with all its sub-folders and files (similar to xcopy) or the entire contents of an archive file such as ZIP.
		/// </summary>
		/// <param name="source">Name of the source directory (with no trailing backslash), which is assumed to be in A_WorkingDir if an absolute path isn't specified.</param>
		/// <param name="dest">Name of the destination directory (with no trailing baskslash), which is assumed to be in A_WorkingDir if an absolute path isn't specified.</param>
		/// <param name="overwrite">
		/// If omitted, it defaults to 0. Otherwise, specify one of the following numbers to indicate whether to overwrite files if they already exist:<br/>
		///     0/false: Do not overwrite existing files. The operation will fail and have no effect if dest already exists as a file or directory.<br/>
		///     1/true: Overwrite existing files.However, any files or subfolders inside dest that do not have a counterpart in source will not be deleted.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		public static object DirCopy(object source, object dest, object overwrite = null)
		{
			if (!source.CoerceString(out var s) || !dest.CoerceString(out var d))
				return DefaultObject;

			CopyDirectory(s, d, overwrite.Ab());
			return DefaultObject;
		}

		/// <summary>
		/// Creates a folder.<br/>
		/// This function will also create all parent directories given in dirName if they do not already exist.
		/// </summary>
		/// <param name="dirName">Name of the directory to create, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		public static object DirCreate(object dirName)
		{
			ThreadAccessors.A_LastError = 0;

			if (!dirName.CoerceString(out var dir))
				return DefaultObject;

			try
			{
				_ = Directory.CreateDirectory(dir);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error creating directory {dirName}");
			}
		}

		/// <summary>
		/// Deletes a folder.
		/// </summary>
		/// <param name="dirName">Name of the directory to delete, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <param name="recurse">If omitted, it defaults to false.<br/>
		///     If false, files and subdirectories contained in dirName are not removed.In this case, if dirName is not empty, no action is taken and an exception is thrown.<br/>
		///     If true, all files and subdirectories are removed (like the Windows command "rmdir /S").
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		public static object DirDelete(object dirName, object recurse = null)
		{
			ThreadAccessors.A_LastError = 0;

			if (!dirName.CoerceString(out var dir))
				return DefaultObject;

			try
			{
				Directory.Delete(dir, recurse.Ab());
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error deleting directory {dirName}");
			}
		}

		/// <summary>
		/// Checks for the existence of a folder and returns its attributes.
		/// </summary>
		/// <param name="filePattern">The path, folder name, or file pattern to check. FilePattern is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		/// <returns>
		/// Returns the attributes of the first matching folder. This string is a subset of RASHNDOC, where each letter means the following:<br/>
		///     R = READONLY<br/>
		///     A = ARCHIVE<br/>
		///     S = SYSTEM<br/>
		///     H = HIDDEN<br/>
		///     N = NORMAL<br/>
		///     D = DIRECTORY<br/>
		///     O = OFFLINE<br/>
		///     C = COMPRESSED<br/>
		///     Since this function only checks for the existence of a folder, "D" is always present in the return value.If no folder is found, an empty string is returned.
		/// </returns>
		public static string DirExist(object filePattern)
		{
			if (!filePattern.CoerceString(out var pattern))
				return DefaultErrorString;

			// A pattern without wildcards names one item, which may be a file.
			foreach (var found in Files.MatchFiles(pattern, files: false, dirs: true))
				return (found.Attributes & FileAttributes.Directory) != 0 ? Conversions.FromFileAttribs(found.Attributes) : DefaultErrorString;

			return DefaultErrorString;
		}

		/// <summary>
		/// Moves a folder along with all its sub-folders and files. It can also rename a folder.
		/// </summary>
		/// <param name="source">Name of the source directory (with no trailing backslash), which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified. For example: C:\My Folder </param>
		/// <param name="dest">The new path and name of the directory (with no trailing baskslash), which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified. For example: D:\My Folder.<br/>
		/// Note: Dest is the actual path and name that the directory will have after it is moved; it is not the directory into which source is moved (except for the known limitation mentioned below).
		/// </param>
		/// <param name="overwriteOrRename">
		/// If omitted, it defaults to 0. Otherwise, specify one of the following values to indicate whether to overwrite or rename existing files:<br/>
		///     0: Do not overwrite existing files.The operation will fail if dest already exists as a file or directory.<br/>
		///     1: Overwrite existing files.However, any files or subfolders inside dest that do not have a counterpart in source will not be deleted.<br/>
		///         Known limitation: If dest already exists as a folder and it is on the same volume as source, source will be moved into it rather than overwriting it. To avoid this, see the next option.<br/>
		///     2: The same as mode 1 above except that the limitation is absent.<br/>
		///     R: Rename the directory rather than moving it. Although renaming normally has the same effect as moving, it is helpful in cases where you want "all or none" behavior;<br/>
		///     that is, when you don't want the operation to be only partially successful when source or one of its files is locked (in use).<br/>
		///     Although this method cannot move source onto a different volume, it can move it to any other directory on its own volume.<br/>
		///         The operation will fail if dest already exists as a file or directory.
		/// </param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		public static object DirMove(object source, object dest, object overwriteOrRename = null)
		{
			ThreadAccessors.A_LastError = 0;

			if (!source.CoerceString(out var s) || !dest.CoerceString(out var d) || !overwriteOrRename.CoerceString(out var flag))
				return DefaultObject;

			if (s.Length == 0)
				return Errors.InvalidParameterErrorOccurred(1, "DirMove", s);

			if (d.Length == 0)
				return Errors.InvalidParameterErrorOccurred(2, "DirMove", d);

			// As in AutoHotkey, the flag is a single character.
			var mode = flag.Length == 0 ? '0' : char.ToUpperInvariant(flag[0]);

			if (flag.Length > 1 || mode is not ('0' or '1' or '2' or 'R'))
				return Errors.InvalidParameterErrorOccurred(3, "DirMove", flag);

			var target = d;

			try
			{
				s = Path.TrimEndingDirectorySeparator(Path.GetFullPath(s));
				d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(d));
				target = d;
				var sameVolume = string.Equals(Path.GetPathRoot(s), Path.GetPathRoot(d), PathComparison);

				// A rename is all or nothing, so it is a single move that never copies or merges.
				if (mode != 'R')
				{
					if (!Directory.Exists(s))
						return Errors.OSErrorOccurred("", $"Cannot move {s} to {d} because source does not exist.");

					if (File.Exists(d))
						return Errors.OSErrorOccurred("", $"Cannot move {s} to {d} because destination is a file.");

					if (IsSameOrChildDirectory(s, d))
						return Errors.OSErrorOccurred("", $"Cannot move {s} into itself.");

					if (Directory.Exists(d))
					{
						if (mode == '0')
							return Errors.OSErrorOccurred("", $"Cannot use option 0/empty when {d} already exists.");

						RefuseLinkDestination(d);

						var sourceIsLink = (File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0;

						if (sourceIsLink && mode == '2')
							throw new IOException($"Cannot merge directory link {s}.");

#if !WINDOWS
						// Root paths cannot distinguish Unix devices, including symlinked and bind mounts.
						sameVolume = CanRenameBetween(mode == '1' ? Path.GetDirectoryName(s) : s, d);
#endif
						if (mode == '1' && sameVolume)
						{
							target = Path.Combine(d, Path.GetFileName(s));
							RefuseLinkDestination(target);
						}

						if (sourceIsLink && Directory.Exists(target))
							throw new IOException($"Cannot merge directory link {s}.");
					}
				}
				if (mode == 'R' || sameVolume && !Directory.Exists(target))
				{
					try
					{
						Directory.Move(s, target);
						return DefaultObject;
					}
					// Mount points can cross devices even when both paths have the same root.
					catch (IOException ex) when (mode != 'R' && (ex.HResult == CrossDeviceLink || ex.HResult == unchecked((int)0x80070011)))
					{
						sameVolume = false;
					}
				}

				// As in AutoHotkey, a merge on one volume moves, while across volumes the source stays until all of it is copied.
				if (sameVolume)
					MoveFolderContents(s, target);
				else
				{
#if WINDOWS
					if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0)
						throw new IOException($"Cannot move directory link {s} across volumes.");
#else
					if (new DirectoryInfo(s).LinkTarget is { } linkTarget)
					{
						_ = Directory.CreateSymbolicLink(target, linkTarget);
						Directory.Delete(s);
						return DefaultObject;
					}
#endif
					_ = Directory.CreateDirectory(target);
					CopyFolderContents(s, target, mode != '0');
				}

				Directory.Delete(s, true);
				return DefaultObject;
			}
			catch (Exception ex) when (ex is not KeysharpException)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Failed to move directory {s} to {target}: {ex.Message}");
			}
		}

		/// <summary>
		/// Changes the script's current working directory.
		/// </summary>
		/// <param name="dirName">The name of the new working directory, which is assumed to be a subfolder of the current <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		public static object SetWorkingDir(object dirName)
		{
			if (!dirName.CoerceString(out var dir))
				return DefaultObject;

			A_WorkingDir = dir;
			return DefaultObject;
		}

		/// <summary>
		/// Separates a file name or URL into its name, directory, extension, and drive.
		/// </summary>
		/// <param name="path">The file name or URL to be analyzed.</param>
		/// <param name="outFileName">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the file name without its path.<br/>
		/// The file's extension is included.
		/// </param>
		/// <param name="outDir">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the directory of the file, including drive letter or share name (if present).<br/>
		/// The final backslash is not included even if the file is located in a drive's root directory.
		/// </param>
		/// <param name="outExtension">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the file's extension (e.g. TXT, DOC, or EXE).<br/>
		/// The dot is not included.
		/// </param>
		/// <param name="outNameNoExt">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the file name without its path, dot and extension.
		/// </param>
		/// <param name="outDrive">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify a reference to the output variable in which to store the drive letter or server name of the file.<br/>
		/// If the file is on a local or mapped drive, the variable will be set to the drive letter followed by a colon (no backslash).<br/>
		/// If the file is on a network path (UNC), the variable will be set to the share name, e.g. \\Workstation01
		/// </param>
		public static object SplitPath(object path, [ByRef] object outFileName = null, [ByRef] object outDir = null, [ByRef] object outExtension = null, [ByRef] object outNameNoExt = null, [ByRef] object outDrive = null)
		{
			if (!path.CoerceString(out var p))
				return DefaultObject;

			var result = (outFileName ?? outDir ?? outExtension ?? outNameNoExt ?? outDrive) == null
				? new KeysharpObject()
				: null;

			var fileName = OutputTarget.Create(result, outFileName, "FileName");
			var dir = OutputTarget.Create(result, outDir, "Dir");
			var extension = OutputTarget.Create(result, outExtension, "Extension");
			var nameNoExt = OutputTarget.Create(result, outNameNoExt, "NameNoExt");
			var drive = OutputTarget.Create(result, outDrive, "Drive");

			if (p.Contains("://"))
			{
				var uri = new Uri(p);
				var root = drive != null || dir != null
					? uri.Scheme + "://" + uri.Host
					: "";

				drive?.Set(root);

				// Avoid retrieving LocalPath when only Drive was requested.
				if (fileName == null && dir == null && extension == null && nameNoExt == null)
					return DefaultObject;

				var localPath = uri.LocalPath.AsSpan();
				var lastSlash = localPath.LastIndexOf('/');

				// Objects get all properties; preserve the original by-reference behavior.
				if (lastSlash >= 0 || result != null)
				{
					var file = lastSlash >= 0 ? localPath[(lastSlash + 1)..] : default;

					if (file.Contains('.'))
						localPath = localPath[..lastSlash];
					else
						file = default;

					fileName?.Set(file.ToString());
					extension?.Set(Path.GetExtension(file).Trim('.').ToString());
					nameNoExt?.Set(Path.GetFileNameWithoutExtension(file).ToString());
				}

				dir?.Set(string.Concat(root.AsSpan(), localPath).TrimEnd('/'));
			}
			else if (p.StartsWith(@"\\"))
			{
				var serverEnd = p.IndexOf('\\', 2);
				var lastSlash = p.LastIndexOf('\\');
				var hasDot = p.Contains('.');
				var hasFile = hasDot && lastSlash > serverEnd && lastSlash + 1 < p.Length;
				var file = hasFile ? p.AsSpan(lastSlash + 1) : default;

				fileName?.Set(file.ToString());
				extension?.Set(hasFile ? Path.GetExtension(p).Trim('.') : "");
				nameNoExt?.Set(Path.GetFileNameWithoutExtension(file).ToString());
				drive?.Set(serverEnd < 0 ? p : p[..serverEnd]);
				dir?.Set(hasDot
					? p.AsSpan(0, lastSlash).TrimEnd('\\').ToString()
					: p.TrimEnd('\\'));
			}
			else
			{
				var input = p == "" ? "" : Path.GetFullPath(p);

				fileName?.Set(Path.GetFileName(input));
				extension?.Set(Path.GetExtension(input).Trim('.'));
				nameNoExt?.Set(Path.GetFileNameWithoutExtension(input));
				dir?.Set(Path.GetDirectoryName(input)?.TrimEnd('\\') ?? "");
				drive?.Set(Path.GetPathRoot(input)?.TrimEnd('\\') ?? "");
			}

			return (object)result ?? DefaultObject;
		}

		private readonly struct OutputTarget(KeysharpObject result, object reference, string property)
		{
			public static OutputTarget? Create(
				KeysharpObject result, object reference, string property)
				=> result == null && reference == null
					? null
					: new OutputTarget(result, reference, property);

			public void Set(string value)
			{
				if (result != null)
					result.DefinePropInternal(property, new OwnPropsDesc(value));
				else
					Refs.SetValue(reference, value);
			}
		}

		/// <summary>
		/// Private helper for copying a folder from source to dest.<br/>
		/// If source is an archive file (.zip, .tar, .tar.gz/.tgz) its contents are extracted into dest.<br/>
		/// A plain .gz holds a single compressed file rather than an archive of entries, so in that case
		/// dest names the decompressed file itself instead of a folder.
		/// </summary>
		/// <param name="source">The folder or archive file to copy from.</param>
		/// <param name="dest">The folder to copy to, or the file to decompress to when source is a plain .gz.</param>
		/// <param name="overwrite">Whether to overwrite the contents of dest.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		private static void CopyDirectory(string source, string dest, bool overwrite)
		{
			try
			{
				source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
				dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dest));
				var isFile = File.Exists(source);
				var isCompressedTar = isFile && (source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));

				// A plain gzip contains one file; a compressed tar contains an archive.
				if (isFile && !isCompressedTar && source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
				{
					if (!overwrite && File.Exists(dest))
						throw new IOException("File already exists and overwrite is false.");

					if (Path.GetDirectoryName(dest) is string parent && parent.Length > 0)
						_ = Directory.CreateDirectory(parent);

					using FileStream compressedFileStream = File.OpenRead(source);
					using FileStream outputFileStream = File.Create(dest);
					using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
					decompressor.CopyTo(outputFileStream);
					return;
				}

				if (!isFile)
				{
					if (!Directory.Exists(source))
						throw new DirectoryNotFoundException($"Source directory {source} does not exist.");

					// Refuse recursion into the destination before creating it.
					if (IsSameOrChildDirectory(source, dest))
						throw new IOException($"Cannot copy {source} into itself.");
				}

				RefuseLinkDestination(dest);

				if (!overwrite && Directory.Exists(dest))
					throw new IOException("Directory already exists and overwrite is false.");

				_ = Directory.CreateDirectory(dest);

				if (isFile && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					ZipFile.ExtractToDirectory(source, dest, overwrite);
				else if (isCompressedTar)
				{
					using var compressedFileStream = File.OpenRead(source);
					using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
					System.Formats.Tar.TarFile.ExtractToDirectory(decompressor, dest, overwrite);
				}
				else if (isFile && source.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
					System.Formats.Tar.TarFile.ExtractToDirectory(source, dest, overwrite);
				else
					CopyFolderContents(source, dest, overwrite);
			}
			catch (Exception ex) when (ex is not KeysharpException)
			{
				_ = Errors.OSErrorOccurred(ex, $"Failed to copy directory {source} to {dest}: {ex.Message}");
			}
		}

		/// <summary>
		/// Copies the files and subfolders of source into dest, which exists, stopping at the first failure.
		/// </summary>
		private static void CopyFolderContents(string source, string dest, bool overwrite)
		{
			foreach (var filepath in Directory.GetFiles(source))
			{
				var destfile = Path.Combine(dest, Path.GetFileName(filepath));
#if !WINDOWS
				RefuseLinkDestination(destfile);
				if (new FileInfo(filepath).LinkTarget is { } fileTarget)
				{
					if (overwrite)
						File.Delete(destfile);

					_ = File.CreateSymbolicLink(destfile, fileTarget);
					continue;
				}
#endif
				File.Copy(filepath, destfile, overwrite);
			}

			foreach (var dirpath in Directory.GetDirectories(source))
			{
				var destdir = Path.Combine(dest, Path.GetFileName(dirpath));

#if !WINDOWS
				RefuseLinkDestination(destdir);
				if (new DirectoryInfo(dirpath).LinkTarget is { } dirTarget)
				{
					_ = Directory.CreateSymbolicLink(destdir, dirTarget);
					continue;
				}
#endif

				_ = Directory.CreateDirectory(destdir);
				CopyFolderContents(dirpath, destdir, overwrite);
			}
		}

		/// <summary>
		/// Moves the files and subfolders of source into dest, which exists, overwriting files of the same name, as
		/// SHFileOperation merges a move in AutoHotkey. A subfolder dest lacks moves whole.
		/// </summary>
		private static void MoveFolderContents(string source, string dest)
		{
			if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
				throw new IOException($"Cannot merge directory link {source}.");

			foreach (var filepath in Directory.GetFiles(source))
				File.Move(filepath, Path.Combine(dest, Path.GetFileName(filepath)), overwrite: true);

			foreach (var dirpath in Directory.GetDirectories(source))
			{
				var destdir = Path.Combine(dest, Path.GetFileName(dirpath));
				RefuseLinkDestination(destdir);

				if (Directory.Exists(destdir) && (File.GetAttributes(dirpath) & FileAttributes.ReparsePoint) == 0)
					MoveFolderContents(dirpath, destdir);
				else
					Directory.Move(dirpath, destdir);
			}
		}

		private static void RefuseLinkDestination(string path)
		{
			if (new FileInfo(path).LinkTarget != null)
				throw new IOException($"Cannot merge into symbolic link {path}.");
		}

		private static bool IsSameOrChildDirectory(string source, string dest)
			=> string.Equals(source, dest, PathComparison)
				|| dest.StartsWith(Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar, PathComparison);

#if !WINDOWS
		private static bool CanRenameBetween(string source, string dest)
		{
			var name = ".keysharp-move-" + Path.GetRandomFileName();
			var from = Path.Combine(source, name);
			var to = Path.Combine(dest, name);
			var cleanup = from;
			_ = Directory.CreateDirectory(from);

			try
			{
				Directory.Move(from, to);
				cleanup = to;
				return true;
			}
			catch (IOException ex) when (ex.HResult == CrossDeviceLink)
			{
				return false;
			}
			finally
			{
				if (Directory.Exists(cleanup))
					Directory.Delete(cleanup);
			}
		}
#endif

		// EXDEV, which .NET gives as the HResult of a Unix rename across devices.
		private const int CrossDeviceLink = 18;

#if LINUX
		private const StringComparison PathComparison = StringComparison.Ordinal;
#else
		// macOS volumes are case-insensitive by default, as Windows ones are.
		private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
#endif
	}
}
