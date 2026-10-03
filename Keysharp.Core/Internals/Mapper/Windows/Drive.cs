using Keysharp.Builtins;
#if WINDOWS
namespace Keysharp.Internals.Mapper.Windows
{
	/// <summary>
	/// Implementation for native Windows Drive Operations
	/// </summary>
	internal partial class Drive : DriveBase
	{
		private static readonly string IOPathPrefix = @"\\.\";

		internal string CreateDeviceIOPath => IOPathPrefix + drive.Name.Substring(0, 1) + ":";

		internal override long Serial => ReadSerial(drive.Name);

		internal static long ReadSerial(string root)
		{
			const uint FailCriticalErrors = 0x0001;
			if (!root.EndsWith('\\')) root += '\\';

			// Empty removable drives must report a failure without an insert-media dialog.
			if (!SetThreadErrorMode(GetThreadErrorMode() | FailCriticalErrors, out var previousMode))
			{
				_ = Errors.OSErrorOccurred(Marshal.GetLastWin32Error(), $"Failed to query drive {root} without prompting.");
				return 0L;
			}

			uint serial;
			bool success;
			int error;

			try
			{
				success = GetVolumeInformation(root, 0, 0, out serial, out _, out _, 0, 0);
				error = Marshal.GetLastWin32Error();
			}
			finally
			{
				_ = SetThreadErrorMode(previousMode, out _);
			}

			if (success)
				return serial;

			_ = Errors.OSErrorOccurred(error, $"Failed to get serial number for drive {root}.");
			return 0L;
		}

		[LibraryImport("kernel32.dll")]
		private static partial uint GetThreadErrorMode();

		[LibraryImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool SetThreadErrorMode(uint mode, out uint previousMode);

		[LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetVolumeInformation(string root, nint name, uint nameLength, out uint serial,
			out uint maximumComponentLength, out uint flags, nint fileSystemName, uint fileSystemNameLength);

		internal override string StatusCD
		{
			get
			{
				var sb = new StringBuilder(128);
				var str = $"open {drive.Name} type cdaudio alias cd wait shareable";

				if (WindowsAPI.mciSendString(str, sb, sb.Capacity, 0) != 0)
					return (string)Errors.ErrorOccurred($"Opening CD {drive.Name} failed.", DefaultErrorString);

				var res = WindowsAPI.mciSendString("status cdaudio mode", sb, sb.Capacity, 0);
				_ = WindowsAPI.mciSendString("close cd wait", null, 0, 0);

				if (res == 0)
					return sb.ToString();

				return (string)Errors.ErrorOccurred($"Obtaining status for CD {drive.Name} failed.", DefaultErrorString);
			}
		}

		internal Drive(DriveInfo drv)
			: base(drv) { }

		internal override void Eject() => DeviceControl(WindowsAPI.IOCTL_STORAGE_EJECT_MEDIA, WindowsAPI.GENERICREAD | WindowsAPI.GENERICWRITE);

		internal override void Lock() => DeviceControl(WindowsAPI.IOCTL_STORAGE_MEDIA_REMOVAL, WindowsAPI.GENERICREAD, preventRemoval: true);

		internal override void Retract() => DeviceControl(WindowsAPI.IOCTL_STORAGE_LOAD_MEDIA, WindowsAPI.GENERICREAD | WindowsAPI.GENERICWRITE);

		internal override void SetLabel(string label) => drive.VolumeLabel = label;

		internal override void UnLock() => DeviceControl(WindowsAPI.IOCTL_STORAGE_MEDIA_REMOVAL, WindowsAPI.GENERICREAD, preventRemoval: false);

		// As in AutoHotkey: the volume is opened shared, since a drive in use can still be ejected or locked, and
		// the media-removal request takes a one-byte PREVENT_MEDIA_REMOVAL.
		private unsafe void DeviceControl(uint control, uint access, bool? preventRemoval = null)
		{
			var handle = WindowsAPI.CreateFile(CreateDeviceIOPath, access, WindowsAPI.FILE_SHARE_READ | WindowsAPI.FILE_SHARE_WRITE,
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
	}
}

#endif
