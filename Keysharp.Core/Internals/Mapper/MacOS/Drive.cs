#if OSX
using Keysharp.Builtins;
using Keysharp.Internals.AppleEvents;
using MonoMac.Foundation;

namespace Keysharp.Internals.Mapper.MacOS
{
	/// <summary>
	/// Concrete implementation of Drive for the macOS platform.
	/// </summary>
	internal partial class Drive : UnixDriveBase
	{
		private const string diskutil = "/usr/sbin/diskutil";
		private const string discRecording = "/System/Library/Frameworks/DiscRecording.framework/DiscRecording";

		[LibraryImport(discRecording)]
		private static partial nint DRDeviceCopyDeviceForBSDName(nint name);

		[LibraryImport(discRecording)]
		private static partial int DRDeviceCloseTray(nint device);

		protected override long SerialNumber
		{
			get
			{
				var uuid = DiskProperty("VolumeUUID");

				if (uuid is { Length: >= 8 } && uint.TryParse(uuid.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var serial))
					return serial;

				throw new IOException($"The volume at {drive.Name} has no UUID to take a serial number from.");
			}
		}

		internal Drive(string path)
			: base(path) { }

		internal override void Eject() => RunOnDrive("eject", () =>
		{
			if (path == null)
				_ = DriveCommand("/usr/bin/drutil", ["-drive", "1", "tray", "eject"]);
			else
				_ = DriveCommand(diskutil, ["eject", path]);
		});

		internal override void Lock() => _ = Errors.UnsupportedErrorOccurred("DriveLock is not supported on macOS.");

		internal override void Retract() => RunOnDrive("retract", () =>
		{
			if (path == null)
			{
				_ = DriveCommand("/usr/bin/drutil", ["-drive", "1", "tray", "close"]);
				return;
			}

			// drutil can select a drive by index or product name only. DiscRecording accepts the exact BSD disk.
			var disk = DiskProperty("ParentWholeDisk", "DeviceIdentifier") ?? throw new IOException($"No disk found for {path}.");
			using var name = new NSString(disk);
			var device = DRDeviceCopyDeviceForBSDName(name.Handle);

			if (device == 0)
				throw new IOException($"{path} does not identify an optical drive.");

			try
			{
				var status = DRDeviceCloseTray(device);

				if (status != 0)
					throw new IOException($"DiscRecording could not close the tray of {path} (OSStatus {status}).");
			}
			finally { CF.CFRelease(device); }
		});

		private string DiskProperty(string key, string fallbackKey = null)
		{
			var output = DriveCommand(diskutil, ["info", "-plist", drive.Name], 5000);
			using var pool = new NSAutoreleasePool();
			using var data = NSData.FromString(output);
			var format = NSPropertyListFormat.Xml;
			using var plist = NSPropertyListSerialization.PropertyListWithData(data, ref format, out var error);

			using (error)
			{
				if (plist is not NSDictionary dict)
					throw new IOException(error?.LocalizedDescription ?? "Invalid diskutil property list.");

				return dict[key]?.ToString() ?? (fallbackKey == null ? null : dict[fallbackKey]?.ToString());
			}
		}

		protected override void WriteLabel(string label) => _ = DriveCommand(diskutil, ["renameVolume", drive.Name, label]);

		internal override void UnLock() => _ = Errors.UnsupportedErrorOccurred("DriveUnlock is not supported on macOS.");
	}
}
#endif
