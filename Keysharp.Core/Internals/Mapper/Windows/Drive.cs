using Keysharp.Builtins;
#if WINDOWS
namespace Keysharp.Internals.Mapper.Windows
{
	/// <summary>
	/// Drive operations through the Win32 calls AutoHotkey makes, on the path as the script gave it. Reducing the
	/// path to its drive letter first, as DriveInfo does, answers for a path that does not exist and rejects UNC
	/// shares and volume GUID paths.
	/// </summary>
	internal unsafe partial class Drive : DriveBase
	{
		private const int VolumeTextLength = 261;//MAX_PATH + 1, the most GetVolumeInformation writes.

		internal override object Capacity => Space(false, "capacity");

		internal override object FileSystem => VolumeText(false, "file system");

		internal override object Label => VolumeText(true, "label");

		internal override object Serial
		{
			get
			{
				uint serial;
				var error = VolumeInformation(null, &serial, null);
				return error == 0 ? (long)serial : QueryFailed(error, "serial number");
			}
		}

		internal override object SpaceFree => Space(true, "free space");

		internal override string Status
		{
			get
			{
				const int FileNotFound = 2, PathNotFound = 3, WriteProtect = 19, NotReady = 21;
				int error;

				using (new CriticalErrorsFail())
					error = GetDiskFreeSpace(Root, out _, out _, out _, out _) ? 0 : Marshal.GetLastWin32Error();

				return error switch
				{
					0 => "Ready",
					FileNotFound or PathNotFound => "Invalid",
					NotReady => "NotReady",
					WriteProtect => "ReadOnly",
					_ => "Unknown"
				};
			}
		}

		internal override string StatusCD
		{
			get
			{
				// The drive is spliced into an MCI command, so only a drive letter is accepted.
				if (path != null && !IsDriveLetter)
					return (string)Errors.InvalidParameterErrorOccurred(1, nameof(Builtins.Drive.DriveGetStatusCD), path, DefaultErrorString);

				var status = new StringBuilder(128);
				int error;

				if (path == null)
					error = WindowsAPI.mciSendString("status cdaudio mode", status, status.Capacity, 0);
				else
				{
					if (WindowsAPI.mciSendString($"open {path} type cdaudio alias cd wait shareable", null, 0, 0) != 0)
						return (string)Errors.ErrorOccurred($"Opening CD {path} failed.", DefaultErrorString);

					error = WindowsAPI.mciSendString("status cd mode", status, status.Capacity, 0);
					_ = WindowsAPI.mciSendString("close cd wait", null, 0, 0);
				}

				return error == 0 ? status.ToString() : (string)Errors.ErrorOccurred("Failed to get CD drive status.", DefaultErrorString);
			}
		}

		internal override string Type => Script.TheScript.DriveTypeMapper.LookUpKeysharpType(GetDriveType(Root));

		// "X", "X:" or "X:\".
		private bool IsDriveLetter => path.Length is >= 1 and <= 3 && char.IsAsciiLetter(path[0])
			&& (path.Length == 1 || path[1] == ':') && (path.Length < 3 || path[2] == '\\');

		// The volume calls fail on some paths that lack the trailing backslash.
		private string Root => path.Length == 0 || path.EndsWith('\\') ? path : path + '\\';

		internal Drive(string path)
			: base(path) { }

		internal override void Eject() => LoadMedia(WindowsAPI.IOCTL_STORAGE_EJECT_MEDIA);

		internal override void Lock() => SetMediaRemoval(true);

		internal override void Retract() => LoadMedia(WindowsAPI.IOCTL_STORAGE_LOAD_MEDIA);

		internal override void SetLabel(string label)
		{
			int error;

			using (new CriticalErrorsFail())
				error = SetVolumeLabel(Root, label) ? 0 : Marshal.GetLastWin32Error();

			if (error != 0)
				_ = Errors.OSErrorOccurred(error, $"Failed to change the volume label for {path}.");
		}

		internal override void UnLock() => SetMediaRemoval(false);

		private static string DefaultCDDevice()
		{
			for (var letter = 'A'; letter <= 'Z'; letter++)
				if (GetDriveType($@"{letter}:\") == DriveType.CDRom)
					return DevicePath(letter);

			return null;
		}

		// As in AutoHotkey: the volume is opened shared, since a drive in use can still be ejected or locked, and
		// the media-removal request takes a one-byte PREVENT_MEDIA_REMOVAL.
		private static void DeviceControl(string device, uint control, uint access, bool? preventRemoval = null)
		{
			using var noMediaPrompt = new CriticalErrorsFail();
			var handle = WindowsAPI.CreateFile(device, access, WindowsAPI.FILE_SHARE_READ | WindowsAPI.FILE_SHARE_WRITE,
											   0, WindowsAPI.OPENEXISTING, 0, 0);

			if (handle == WindowsAPI.INVALID_HANDLE)
			{
				_ = Errors.OSErrorOccurred(Marshal.GetLastWin32Error());
				return;
			}

			var prevent = (byte)(preventRemoval == true ? 1 : 0);
			var ok = WindowsAPI.DeviceIoControl(handle, control, preventRemoval.HasValue ? &prevent : null, preventRemoval.HasValue ? 1u : 0u,
												null, 0, out _, 0);
			var error = Marshal.GetLastWin32Error();
			_ = WindowsAPI.CloseHandle(handle);

			if (!ok)
				_ = Errors.OSErrorOccurred(error);
		}

		// The volume itself, which is what the storage requests are sent to, so no trailing backslash.
		private static string DevicePath(char letter) => $@"\\.\{letter}:";

		private void LoadMedia(uint control)
		{
			// Anything but a drive letter is opened as given, so a volume GUID path works with no letter assigned.
			var device = path == null ? DefaultCDDevice() : IsDriveLetter ? DevicePath(path[0]) : path;

			if (device == null)
				_ = Errors.ErrorOccurred("Failed to find any CDROM or DVD drives.");
			else
				DeviceControl(device, control, WindowsAPI.GENERICREAD | WindowsAPI.GENERICWRITE);
		}

		private void SetMediaRemoval(bool prevent)
		{
			if (IsDriveLetter)
				DeviceControl(DevicePath(path[0]), WindowsAPI.IOCTL_STORAGE_MEDIA_REMOVAL, WindowsAPI.GENERICREAD, prevent);
			else
				_ = Errors.InvalidParameterErrorOccurred(1, prevent ? nameof(Builtins.Drive.DriveLock) : nameof(Builtins.Drive.DriveUnlock), path);
		}

		private object Space(bool free, string what)
		{
			int error;
			ulong available, total;

			using (new CriticalErrorsFail())
				error = GetDiskFreeSpaceEx(Root, out available, out total, out _) ? 0 : Marshal.GetLastWin32Error();

			// The caller's available bytes rather than the volume's, so a disk quota is honored.
			return error == 0 ? (long)((free ? available : total) / (1024 * 1024)) : QueryFailed(error, what);
		}

		private object VolumeText(bool label, string what)
		{
			var text = stackalloc char[VolumeTextLength];
			var error = label ? VolumeInformation(text, null, null) : VolumeInformation(null, null, text);
			return error == 0 ? new string(text) : QueryFailed(error, what);
		}

		// The Win32 error, or 0. Each buffer is VolumeTextLength characters or null.
		private int VolumeInformation(char* label, uint* serial, char* fileSystem)
		{
			using (new CriticalErrorsFail())
				return GetVolumeInformation(Root, label, label != null ? VolumeTextLength : 0u, serial, null, null,
											fileSystem, fileSystem != null ? VolumeTextLength : 0u) ? 0 : Marshal.GetLastWin32Error();
		}

		[LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector,
				out uint freeClusters, out uint totalClusters);

		[LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailable, out ulong totalBytes,
				out ulong totalFreeBytes);

		// DriveType's values are the Win32 DRIVE_* constants.
		[LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
		private static partial DriveType GetDriveType(string root);

		[LibraryImport("kernel32.dll")]
		private static partial uint GetThreadErrorMode();

		[LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetVolumeInformation(string root, char* name, uint nameLength, uint* serial,
				uint* maximumComponentLength, uint* flags, char* fileSystemName, uint fileSystemNameLength);

		// No SetLastError: restoring the mode must not replace the error of the call it wrapped.
		[LibraryImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool SetThreadErrorMode(uint mode, out uint previousMode);

		[LibraryImport("kernel32.dll", EntryPoint = "SetVolumeLabelW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool SetVolumeLabel(string root, string label);

		/// <summary>
		/// Makes an empty removable drive report a failure rather than show an insert-media dialog, keeping the
		/// thread's other error-mode flags.
		/// </summary>
		private readonly struct CriticalErrorsFail : IDisposable
		{
			private readonly bool changed;
			private readonly uint previousMode;

			public CriticalErrorsFail()
			{
				const uint FailCriticalErrors = 0x0001;
				changed = SetThreadErrorMode(GetThreadErrorMode() | FailCriticalErrors, out previousMode);
			}

			public void Dispose()
			{
				if (changed)
					_ = SetThreadErrorMode(previousMode, out _);
			}
		}
	}
}

#endif
