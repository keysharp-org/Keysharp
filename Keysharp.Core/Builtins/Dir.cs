namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for directory-related functions.
	/// </summary>
	public static class Dir
	{
		private static readonly string pathStart = new (Path.DirectorySeparatorChar, 2);

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
			var s = Path.GetFullPath(source.As());
			var d = Path.GetFullPath(dest.As());
			var o = overwrite.Ab();
			CopyDirectory(s, d, o);
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
			try
			{
				_ = Directory.CreateDirectory(dirName.As());
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
			try
			{
				Directory.Delete(dirName.As(), recurse.Ab());
				return DefaultObject;
			}
			catch (Exception ex)
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred(ex, $"Error creating directory {dirName}");
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
			try//This can throw if the directory doesn't exist.
			{
				foreach (var file in Drive.Glob(filePattern.As()))
					return Conversions.FromFileAttribs(File.GetAttributes(file));
			}
			catch (Exception)
			{
				//Swallow the exception since we still want to return an empty string even if it doesn't exist.
			}

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
			var s = source.As();
			var d = dest.As();
			var flag = overwriteOrRename.As();
			var rename = false;
			var movein = false;

			//If dest exists as a file, never copy.
			if (File.Exists(d))
			{
				ThreadAccessors.A_LastError = Marshal.GetLastSystemError();
				return Errors.OSErrorOccurred("", $"Cannot move {s} to {d} because destination is a file.");
			}

			switch (flag.ToUpperInvariant())
			{
				case "1":
					movein = true;
					break;

				case "2":
					break;

				case "R":
					rename = true;
					break;

				case "0":
				default:
					if (Directory.Exists(d))
						return Errors.OSErrorOccurred("", $"Cannot use option 0/empty when {d} already exists.");

					break;
			}

			if (rename && Directory.Exists(d))
				return Errors.OSErrorOccurred("", $"Cannot rename {s} to {d} because it already exists.");

			if (!Directory.Exists(s))
				return Errors.OSErrorOccurred("", $"Cannot move {s} to {d} because source does not exist.");

			if (movein && Directory.Exists(d))
				d = Path.Combine(d, Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar)));

			MoveDirectory(s, d);
			return DefaultObject;
		}

		/// <summary>
		/// Returns the drive portion of a path without the backslash.<br/>
		/// Ex: C:\folder => C: or \\uncdrive\folder\*.txt => \\uncdrive\folder<br/>
		/// Adapted from http://stackoverflow.com/questions/398518/how-to-implement-glob-in-c
		/// </summary>
		/// <param name="path">The path to retrieve the head for.</param>
		/// <returns>The drive portion of the path without the backslash.</returns>
		internal static string PathHead(string path)
		{
			if (path.StartsWith(pathStart))
			{
				var dirSep = Path.DirectorySeparatorChar;
				var parts = path.Substring(2).Split(dirSep);
				var head = path.Substring(0, 2) + parts[0] + dirSep;

				if (parts.Length > 1)
					head += parts[1];

				return head;
			}

			return path.Split(Path.DirectorySeparatorChar)[0];
		}

		/// <summary>
		/// Changes the script's current working directory.
		/// </summary>
		/// <param name="dirName">The name of the new working directory, which is assumed to be a subfolder of the current <see cref="A_WorkingDir"/> if an absolute path isn't specified.</param>
		public static object SetWorkingDir(object dirName)
		{
			A_WorkingDir = dirName.As();
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
            var p = path.As();

			if (p.Contains("://"))
			{
				var uri = new Uri(p);
				string drive = uri.Scheme + "://" + uri.Host;
				if (outDrive != null) Refs.SetValue(outDrive, drive);
				var lastSlash = uri.LocalPath.LastIndexOf('/');
				var localPath = uri.LocalPath;

				if (lastSlash != -1)
				{
					var tempFilename = localPath.Substring(lastSlash + 1);

					if (tempFilename.Contains('.'))
					{
						if (outFileName != null) Refs.SetValue(outFileName, tempFilename);
						if (outExtension != null) Refs.SetValue(outExtension, Path.GetExtension(tempFilename).Trim('.'));
						if (outNameNoExt != null) Refs.SetValue(outNameNoExt, Path.GetFileNameWithoutExtension(tempFilename));
						localPath = localPath.Substring(0, lastSlash);
					}
					else
					{
						if (outFileName != null) Refs.SetValue(outFileName, "");
						if (outExtension != null) Refs.SetValue(outExtension, "");
						if (outNameNoExt != null) Refs.SetValue(outNameNoExt, "");
                    }
				}

				if (outDir != null) Refs.SetValue(outDir, (drive + localPath).TrimEnd('/'));
			}
			else
			{
				var input = p == "" ? DefaultObject : (p.StartsWith(@"\\") ? p : Path.GetFullPath(p));

				if (p.StartsWith(@"\\"))
				{
					//There appear to be no built in methods to process UNC paths, so do it manually here.
					var nextSlash = input.IndexOf('\\', 2);
					var lastSlash = input.LastIndexOf('\\');
					var hasFileComponent = input.Contains('.') && lastSlash > nextSlash && lastSlash >= 0 && lastSlash + 1 < input.Length;

					if (outFileName != null) Refs.SetValue(outFileName, hasFileComponent ? input[(lastSlash + 1)..] : "");
					if (outExtension != null) Refs.SetValue(outExtension, hasFileComponent ? Path.GetExtension(input).Trim('.') : "");
					if (outNameNoExt != null) Refs.SetValue(outNameNoExt, hasFileComponent ? Path.GetFileNameWithoutExtension(input[(lastSlash + 1)..]) : "");

					if (outDrive != null)
					{
						if (nextSlash == -1)
							Refs.SetValue(outDrive, p);
						else
							Refs.SetValue(outDrive, input.Substring(0, nextSlash));
					}

					if (outDir != null)
					{
						if (input.Contains('.'))
						{
							if (lastSlash == -1)
								Refs.SetValue(outDir, input);
							else
								Refs.SetValue(outDir, input.AsSpan().Slice(0, lastSlash).TrimEnd('\\').ToString());
						}
						else
							Refs.SetValue(outDir, input.TrimEnd('\\'));
					}
				}
				else
				{
					if (outFileName != null) Refs.SetValue(outFileName, Path.GetFileName(input) ?? DefaultObject);
					if (outExtension != null) Refs.SetValue(outExtension, Path.GetExtension(input)?.Trim('.') ?? DefaultObject);
					if (outNameNoExt != null) Refs.SetValue(outNameNoExt, Path.GetFileNameWithoutExtension(input) ?? DefaultObject);
					if (outDir != null) Refs.SetValue(outDir, Path.GetDirectoryName(input)?.TrimEnd('\\') ?? DefaultObject);
					if (outDrive != null) Refs.SetValue(outDrive, Path.GetPathRoot(input)?.TrimEnd('\\') ?? DefaultObject);
				}
			}

			return DefaultObject;
		}

		/// <summary>
		/// Returns path with the value from <see cref="PathHead"/> removed from the start.
		/// </summary>
		/// <param name="path">The path to retrieve the tail for.</param>
		/// <returns>The path with the drive portion removed.</returns>
		internal static string PathTail(string path) => !path.Contains(Path.DirectorySeparatorChar.ToString()) ? path : path.Substring(1 + PathHead(path).Length);

		/// <summary>
		/// Private helper for copying a folder from source to dest.
		/// </summary>
		/// <param name="source">The folder to copy from.</param>
		/// <param name="dest">The folder to copy to.</param>
		private static void CopyDirectory(DirectoryInfo source, DirectoryInfo dest)
		{
			if (!dest.Exists)
				dest.Create();

			foreach (var fiSrcFile in source.GetFiles())
				_ = fiSrcFile.CopyTo(Path.Combine(dest.FullName, fiSrcFile.Name));

			foreach (var diSrcDirectory in source.GetDirectories())
				CopyDirectory(diSrcDirectory, new DirectoryInfo(Path.Combine(dest.FullName, diSrcDirectory.Name)));
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
			var isFile = File.Exists(source);
			//Check the compressed tar suffixes before the plain .gz one, else foo.tar.gz would match .gz and the inner tar would never be extracted.
			var isCompressedTar = isFile && (source.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));
			var isSingleGz = isFile && !isCompressedTar && source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

			//A plain .gz decompresses to a single file, so dest must not be created as a directory.
			if (isSingleGz)
			{
				try
				{
					if (!overwrite && File.Exists(dest))
						throw new IOException($"File already exists and overwrite is false.");

					if (Path.GetDirectoryName(dest) is string parent && parent.Length > 0)
						_ = Directory.CreateDirectory(parent);

					using FileStream compressedFileStream = File.OpenRead(source);
					using FileStream outputFileStream = File.Create(dest);
					using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
					decompressor.CopyTo(outputFileStream);
				}
				catch (Exception ex)
				{
					_ = Errors.OSErrorOccurred(ex, $"Failed to copy file {source} to {dest}: {ex.Message}");
				}

				return;
			}

			try
			{
				if (!overwrite && Directory.Exists(dest))
					throw new IOException($"Directory already exists and overwrite is false.");

				_ = Directory.CreateDirectory(dest);
			}
			catch (IOException ioe)
			{
				if (!overwrite)
				{
					_ = Errors.OSErrorOccurred(ioe, $"Failed to create directory {dest}: {ioe.Message}");
					return;
				}
			}

			try
			{
				//Special check for archive files.
				if (isFile && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					ZipFile.ExtractToDirectory(source, dest, overwrite);
				}
				else if (isCompressedTar)
				{
					//Decompress to a temporary tar first, then let TarFile extract it so its entry path validation still applies.
					var tempTar = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".tar"));

					try
					{
						using (FileStream compressedFileStream = File.OpenRead(source))
						using (FileStream tarFileStream = File.Create(tempTar))
						using (var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress))
							decompressor.CopyTo(tarFileStream);

						System.Formats.Tar.TarFile.ExtractToDirectory(tempTar, dest, overwrite);
					}
					finally
					{
						if (File.Exists(tempTar))
							File.Delete(tempTar);
					}
				}
				else if (isFile && source.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
				{
					System.Formats.Tar.TarFile.ExtractToDirectory(source, dest, overwrite);
				}
				else
				{
					foreach (var filepath in Directory.GetFiles(source))
					{
						var basename = Path.GetFileName(filepath);
						var destfile = Path.Combine(dest, basename);
						File.Copy(filepath, destfile, overwrite);
					}

					foreach (var dirpath in Directory.GetDirectories(source))
					{
						var basename = Path.GetFileName(dirpath);
						var destdir = Path.Combine(dest, basename);
						CopyDirectory(dirpath, destdir, overwrite);
					}
				}
			}
			catch (Exception ex)
			{
				_ = Errors.OSErrorOccurred(ex, $"Failed to copy directory {source} to {dest}: {ex.Message}");
			}
		}

		/// <summary>
		/// Move source folder to dest if on the same drive. Copy then delete the source if on a different drive.
		/// Gotten from: https://social.msdn.microsoft.com/forums/windows/en-US/b43cc316-ab96-49cb-8e3b-6de48fbc3453/how-to-move-a-folder-from-one-volume-drive-to-another-in-vbnet<br/>
		/// </summary>
		/// <param name="source">Source folder to copy</param>
		/// <param name="dest">Destination to copy source to</param>
		/// <param name="del">True to delete the source after copying if on different drives, else false to keep both copies.</param>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if any failure happens while attempting to perform the operation.</exception>
		private static void MoveDirectory(string source, string dest, bool del = true)
		{
			if (Directory.Exists(source))
			{
				if (Directory.GetDirectoryRoot(source) == Directory.GetDirectoryRoot(dest))
				{
					try
					{
						Directory.Move(source, dest);
					}
					catch (Exception ex)
					{
						_ = Errors.OSErrorOccurred(ex, $"Failed to move directory {source} to {dest}: {ex.Message}");
					}
				}
				else
				{
					try
					{
						CopyDirectory(new DirectoryInfo(source), new DirectoryInfo(dest));

						if (del)
							Directory.Delete(source, true);
					}
					catch (Exception ex)
					{
						_ = Errors.OSErrorOccurred(ex, $"Failed to copy directory {source} to {dest}: {ex.Message}");
					}
				}
			}
		}
	}
}
