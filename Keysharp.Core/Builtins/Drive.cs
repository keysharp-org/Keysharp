namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for Drive-related functions.
	/// </summary>
	public static class Drive
	{
		/// <summary>
		/// Ejects the tray of the specified CD/DVD drive.<br/>
		/// It can also eject a removable drive.
		/// </summary>
		/// <param name="drive">
		/// If omitted, it defaults to the first CD/DVD drive found by iterating from A to Z (an exception is thrown if no drive is found).<br/>
		/// Otherwise, specify the drive letter optionally followed by a colon or a colon and backslash. For example, "D", "D:" or "D:\".<br/>
		/// This can also be a device path in the form "\\?\Volume{...}", which can be discovered by running mountvol at the command line.<br/>
		/// In this case the drive is not required to be assigned a drive letter.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static object DriveEject(object drive = null)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			_ = DriveHelper(text, true);
			return DefaultObject;
		}

		/// <summary>
		/// Returns the total capacity of the drive which contains the specified path, in megabytes.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The capacity of the drive in megabytes.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetCapacity(object path)
		{
			if (!path.CoerceString(out var text))
				return DefaultObject;

			try
			{
				return new DriveInfo(text).TotalSize / 1024 / 1024;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to get capacity for drive {text}.");
			}
		}

		/// <summary>
		/// Returns the type of the specified drive's file system.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash, or a UNC name such as "\server1\share1".</param>
		/// <returns>The type of Drive's file system.<br/>
		/// The possible values are defined by the system; they include (but are not limited to) the following:<br/>
		/// NTFS, FAT32, FAT, CDFS (typically indicates a CD), or UDF (typically indicates a DVD).
		/// </returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetFileSystem(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			try
			{
				return new DriveInfo(text).DriveFormat;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to get file system for drive {text}.");
			}
		}

		/// <summary>
		/// Returns the volume label of the specified drive.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash, or a UNC name such as "\server1\share1".</param>
		/// <returns>The drive's volume label.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetLabel(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			try
			{
				return new DriveInfo(text).VolumeLabel;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to get label for drive {text}.");
			}
		}

		/// <summary>
		/// Returns a string of letters, one character for each drive letter in the system.
		/// </summary>
		/// <param name="driveType">If omitted, all drive types are retrieved.<br/>
		/// Otherwise, specify one of the following words to retrieve only a specific type of drive: CDROM, REMOVABLE, FIXED, NETWORK, RAMDISK, UNKNOWN.
		/// </param>
		/// <returns>The drive letters in the system, depending on DriveType. For example: ACDEZ.</returns>
		public static string DriveGetList(object driveType = null)
		{
			if (!driveType.CoerceString(out var drivetype))
				return "";

			DriveType? type = null;

			if (!string.IsNullOrEmpty(drivetype))
				type = Script.TheScript.DriveTypeMapper.LookUpCLRType(drivetype);

			var drives = DriveInfo.GetDrives();
#if !WINDOWS
			var list = new List<string>(drives.Length);
#else
			var matchingDevices = "";
#endif

			for (var i = 0; i < drives.Length; i++)
			{
				if (type.HasValue)
				{
					if (i == 0) continue; // performance hack: skip A:\\

					try
					{
						if (drives[i].DriveType == type.Value)
#if WINDOWS
							matchingDevices += drives[i].Name[0];

#else
							list.Add(drives[i].Name);
#endif
					}
					catch
					{
						// ignore
					}
				}
				else
				{
#if WINDOWS
					matchingDevices += drives[i].Name[0];
#else
					list.Add(drives[i].Name);
#endif
				}
			}

#if WINDOWS
			return matchingDevices;
#else
			return string.Join(';', list);
#endif
		}

		/// <summary>
		/// Returns the volume serial number of the specified drive.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash, or a UNC name such as "\server1\share1".</param>
		/// <returns>The drive's volume serial number.</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetSerial(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			try
			{
				return Platform.Drive.CreateDrive(new DriveInfo(text)).Serial;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to get serial number for drive {text}.");
			}
		}

		/// <summary>
		/// Returns the free disk space of the drive which contains the specified path, in megabytes.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The free disk space of the drive which contains Path, in megabytes (rounded down to the nearest megabyte).</returns>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetSpaceFree(object path)
		{
			if (!path.CoerceString(out var text))
				return DefaultObject;

			try
			{
				return new DriveInfo(text).TotalFreeSpace / (1024 * 1024);
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to get free space for drive containing {text}.");
			}
		}

		/// <summary>
		/// Returns the status of the drive which contains the specified path.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The status of the drive which contains path:<br/>
		/// Unknown: Might indicate unformatted/RAW file system.<br/>
		/// Ready: This is the most common.<br/>
		/// NotReady: Typical for removable drives that don't contain media.<br/>
		/// Invalid: Path does not exist or is a network drive that is presently inaccessible, etc.
		/// </returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static string DriveGetStatus(object path)
		{
			if (!path.CoerceString(out var text))
				return "";

			try
			{
				var drv = new DriveInfo(text.TrimEnd('\\'));
				var val = drv.DriveFormat;//Will throw DriveNotFoundException on invalid paths.
				return drv.IsReady ? "Ready" : "NotReady";
			}
			catch (Exception ex)
			{
				return (string)Errors.ErrorOccurred($"Failed to get drive status: {ex.Message}.");
			}
		}

		/// <summary>
		/// Returns the media status of the specified CD/DVD drive.
		/// </summary>
		/// <param name="drive">If omitted, the default CD/DVD drive will be used. Otherwise, specify the drive letter followed by a colon.</param>
		/// <returns>The drive's media status:<br/>
		/// not ready: The drive is not ready to be accessed, perhaps due to being engaged in a write operation. Known limitation: "not ready" also occurs when the drive contains a DVD rather than a CD.<br/>
		/// open: The drive contains no disc, or the tray is ejected.<br/>
		/// playing: The drive is playing a disc.<br/>
		/// paused: The previously playing audio or video is now paused.<br/>
		/// seeking: The drive is seeking.<br/>
		/// stopped: The drive contains a CD but is not currently accessing it.
		/// </returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static string DriveGetStatusCD(object drive = null)
		{
			if (!drive.CoerceString(out var text))
				return "";

			try
			{
				var d = GetRemovableDrive(text.TrimEnd('\\'));
				return d.StatusCD;
			}
			catch (Exception e)
			{
				return (string)Errors.ErrorOccurred($"Failed to get CD drive status: {e.Message}.");
			}
		}

		/// <summary>
		/// Returns the type of the drive which contains the specified path.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The  type of the drive which contains Path:<br/>
		/// Unknown, Removable, Fixed, Network, CDROM, or RAMDisk.<br/>
		/// If Path is invalid (e.g. because the drive does not exist), the return value is an empty string.
		/// </returns>
		public static string DriveGetType(object path)
		{
			if (!path.CoerceString(out var text))
				return "";

			try
			{
				return Script.TheScript.DriveTypeMapper.LookUpKeysharpType(new DriveInfo(text).DriveType);
			}
			catch
			{
				return "";
			}
		}

		/// <summary>
		/// Prevents the eject feature of the specified drive from working.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash (might also work on UNC paths and mapped drives).</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static object DriveLock(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(new DriveInfo(text)).Lock();
			return DefaultObject;
		}

		/// <summary>
		/// Retracts the tray of the specified CD/DVD drive.
		/// </summary>
		/// <param name="drive">
		/// If omitted, it defaults to the first CD/DVD drive found by iterating from A to Z (an exception is thrown if no drive is found).<br/>
		/// Otherwise, specify the drive letter optionally followed by a colon or a colon and backslash. For example, "D", "D:" or "D:\".<br/>
		/// This can also be a device path in the form "\\?\Volume{...}", which can be discovered by running mountvol at the command line.<br/>
		/// In this case the drive is not required to be assigned a drive letter.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static object DriveRetract(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			_ = DriveHelper(text, false);
			return DefaultObject;
		}
		/// <summary>
		/// Changes the volume label of the specified drive.<br/>
		/// The operation may require administrator/root privileges.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash (might also work on UNC paths and mapped drives).</param>
		/// <param name="newLabel">If omitted, the drive will have no label. Otherwise, specify the new label to set.</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static object DriveSetLabel(object drive, object newLabel = null)
		{
			if (!drive.CoerceString(out var driveName) || !newLabel.CoerceString(out var label))
				return DefaultObject;

			try
			{
				var d = Platform.Drive.CreateDrive(new DriveInfo(driveName));
				d.SetLabel(label);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				return Errors.OSErrorOccurred(ex, $"Failed to change the volume label for {driveName}.");
			}
		}
		/// <summary>
		/// Restores the eject feature of the specified drive.
		/// </summary>
		/// <param name="drive">The drive letter followed by a colon and an optional backslash (might also work on UNC paths and mapped drives).</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		public static object DriveUnlock(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(new DriveInfo(text)).UnLock();
			return DefaultObject;
		}

		/// <summary>
		/// Private helper to either eject or retract a removable drive.
		/// </summary>
		/// <param name="dr">The drive to operate on.</param>
		/// <param name="b">Eject if true, else retract..</param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		private static object DriveHelper(string drive, bool b)
		{
			DriveBase d = GetRemovableDrive(drive);

			if (b)
				d.Eject();
			else
				d.Retract();

			return DefaultObject;
		}

		/// <summary>
		/// Private helper to retrieve a removable drive.
		/// </summary>
		/// <param name="dr">The drive to retrieve. If empty, retrieve the first detected removable drive.</param>
		/// <returns>The detected drive.</returns>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any failure is detected.</exception>
		private static DriveBase GetRemovableDrive(string dr)
		{
			DriveBase drive = null;

			if (dr.Length == 0)
			{
				var allDrives = DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.Removable).ToList();

				if (allDrives.Count > 0)
					drive = Platform.Drive.CreateDrive(new DriveInfo(allDrives[0].Name));
				else
					_ = Errors.ErrorOccurred("Failed to find any CDROM or DVD drives.");
			}
			else
				drive = Platform.Drive.CreateDrive(new DriveInfo(dr));

			return drive;
		}

		/// <summary>
		/// Internal helper to implement globbing which will return files matching a pattern.<br/>
		/// Adapted from http://stackoverflow.com/questions/398518/how-to-implement-glob-in-c
		/// </summary>
		/// <param name="glob">The pattern to match.</param>
		/// <returns>All files matching the specified pattern.</returns>
		internal static IEnumerable<string> Glob(string glob)
		{
			if (File.Exists(glob) || Directory.Exists(glob))
			{
				yield return glob;
				yield break;
			}

			foreach (var path in Glob(Dir.PathHead(glob) + Path.DirectorySeparatorChar, Dir.PathTail(glob)))
				yield return path;
		}

		/// <summary>
		/// Private helper to implement globbing which will return files matching a pattern.<br/>
		/// Adapted from http://stackoverflow.com/questions/398518/how-to-implement-glob-in-c
		/// </summary>
		/// <param name="head">The directory prefix to examine.</param>
		/// <param name="tail">The pattern to search for.</param>
		/// <returns>The list of files/folders matching the pattern specified in tail.</returns>
		private static IEnumerable<string> Glob(string head, string tail)
		{
			if (Dir.PathTail(tail) == tail)
			{
				foreach (var path in Directory.GetFiles(head, tail))
					yield return path;
			}
			else
			{
				foreach (var dir in Directory.GetDirectories(head, Dir.PathHead(tail)))
				{
					foreach (var path in Glob(Path.Combine(head, dir), Dir.PathTail(tail)))
						yield return path;
				}
			}
		}
	}
}
