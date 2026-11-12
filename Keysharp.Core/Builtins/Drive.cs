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
			if (!OptionalDrive(drive, out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(text).Eject();
			return DefaultObject;
		}

		/// <summary>
		/// Returns the total capacity of the drive which contains the specified path, in megabytes.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The capacity of the drive in megabytes.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if path is empty.</exception>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetCapacity(object path)
		{
			if (!path.CoerceString(out var text))
				return DefaultObject;

			return text.Length == 0 ? Errors.InvalidParameterErrorOccurred(1, nameof(DriveGetCapacity), text) : Platform.Drive.CreateDrive(text).Capacity;
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

			return Platform.Drive.CreateDrive(text).FileSystem;
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

			return Platform.Drive.CreateDrive(text).Label;
		}

		/// <summary>
		/// Returns a string of letters, one character for each drive letter in the system.
		/// </summary>
		/// <param name="driveType">If omitted, all drive types are retrieved.<br/>
		/// Otherwise, specify one of the following words, in any case, to retrieve only a specific type of drive: CDROM, REMOVABLE, FIXED, NETWORK, RAMDISK, UNKNOWN.
		/// </param>
		/// <returns>The drive letters in the system, depending on DriveType. For example: ACDEZ.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if driveType is not one of those words.</exception>
		public static string DriveGetList(object driveType = null)
		{
			DriveType? type = null;

			// As in AutoHotkey, only an omitted type lists every drive; an empty or unknown word is an error.
			if (driveType != null)
			{
				if (!driveType.CoerceString(out var drivetype))
					return "";

				type = Script.TheScript.DriveTypeMapper.LookUpCLRType(drivetype);

				if (!type.HasValue)
					return (string)Errors.ValueErrorOccurred($"Unknown drive type \"{drivetype}\". Expected CDROM, REMOVABLE, FIXED, NETWORK, RAMDISK or UNKNOWN.", drivetype, DefaultErrorString);
			}

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

			return Platform.Drive.CreateDrive(text).Serial;
		}

		/// <summary>
		/// Returns the free disk space of the drive which contains the specified path, in megabytes.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The free disk space of the drive which contains Path, in megabytes (rounded down to the nearest megabyte).</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if path is empty.</exception>
		/// <exception cref="OSError">An <see cref="OSError"/> exception is thrown if the drive could not be accessed.</exception>
		public static object DriveGetSpaceFree(object path)
		{
			if (!path.CoerceString(out var text))
				return DefaultObject;

			return text.Length == 0 ? Errors.InvalidParameterErrorOccurred(1, nameof(DriveGetSpaceFree), text) : Platform.Drive.CreateDrive(text).SpaceFree;
		}

		/// <summary>
		/// Returns the status of the drive which contains the specified path.
		/// </summary>
		/// <param name="path">Any path contained by the drive (might also work on UNC paths and mapped drives).</param>
		/// <returns>The status of the drive which contains path:<br/>
		/// Unknown: Might indicate unformatted/RAW file system.<br/>
		/// Ready: This is the most common.<br/>
		/// NotReady: Typical for removable drives that don't contain media.<br/>
		/// ReadOnly: The drive is write-protected.<br/>
		/// Invalid: Path does not exist or is a network drive that is presently inaccessible, etc.
		/// </returns>
		public static string DriveGetStatus(object path)
		{
			if (!path.CoerceString(out var text))
				return "";

			return Platform.Drive.CreateDrive(text).Status;
		}

		/// <summary>
		/// Returns the media status of the specified CD/DVD drive.
		/// </summary>
		/// <param name="drive">If omitted, the default CD/DVD drive will be used. Otherwise, specify the drive letter optionally followed by a colon or a colon and backslash.</param>
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
			if (!OptionalDrive(drive, out var text))
				return "";

			return Platform.Drive.CreateDrive(text).StatusCD;
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

			return Platform.Drive.CreateDrive(text).Type;
		}

		/// <summary>
		/// Prevents the eject feature of the specified drive from working.
		/// </summary>
		/// <param name="drive">The drive letter optionally followed by a colon or a colon and backslash.</param>
		/// <exception cref="ValueError">On Windows, a <see cref="ValueError"/> exception is thrown if drive is not a drive letter.</exception>
		/// <exception cref="OSError">On Windows, an <see cref="OSError"/> exception is thrown if the drive could not be locked.</exception>
		public static object DriveLock(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(text).Lock();
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
		public static object DriveRetract(object drive = null)
		{
			if (!OptionalDrive(drive, out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(text).Retract();
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

			Platform.Drive.CreateDrive(driveName).SetLabel(label);
			return DefaultObject;
		}

		/// <summary>
		/// Restores the eject feature of the specified drive.
		/// </summary>
		/// <param name="drive">The drive letter optionally followed by a colon or a colon and backslash.</param>
		/// <exception cref="ValueError">On Windows, a <see cref="ValueError"/> exception is thrown if drive is not a drive letter.</exception>
		/// <exception cref="OSError">On Windows, an <see cref="OSError"/> exception is thrown if the drive could not be unlocked.</exception>
		public static object DriveUnlock(object drive)
		{
			if (!drive.CoerceString(out var text))
				return DefaultObject;

			Platform.Drive.CreateDrive(text).UnLock();
			return DefaultObject;
		}

		// An omitted drive stays null, which selects the default CD/DVD drive; an empty string is a drive that does not exist.
		private static bool OptionalDrive(object drive, out string text)
		{
			text = null;
			return drive == null || drive.CoerceString(out text);
		}
	}
}
