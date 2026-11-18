#if LINUX

namespace Keysharp.Internals.Mapper.Linux
{
	/// <summary>
	/// Drive queries and media controls for Linux.
	/// </summary>
	internal class Drive : UnixDriveBase
	{
		protected override long SerialNumber
		{
			get
			{
				var (number, device) = MountSource();
				// The device number resolves /dev/root. Btrfs uses an anonymous number and supplies the device by name.
				var sysPath = "/sys/dev/block/" + number;
				var target = Directory.Exists(sysPath) ? "--path=" + sysPath
					: device.StartsWith("/dev/", StringComparison.Ordinal) ? "--name=" + device : null;

				if (target != null)
				{
					var properties = DriveCommand("udevadm", ["info", "--query=property", target], 5000);

					if (ParseVolumeSerial(properties) is { } serial)
						return serial;
				}

				throw new IOException($"The file system on {device} has no UUID to take a serial number from.");
			}
		}

		// blkid reports FAT serials as XXXX-XXXX and the full 64-bit NTFS serial. Windows exposes its low 32 bits.
		private static long? ParseVolumeSerial(string properties)
		{
			string uuid = null;
			var ntfs = false;

			foreach (var line in properties.AsSpan().EnumerateLines())
			{
				if (line is "ID_FS_TYPE=ntfs")
					ntfs = true;
				else if (line.StartsWith("ID_FS_UUID=", StringComparison.Ordinal))
					uuid = line["ID_FS_UUID=".Length..].ToString();
			}

			if (uuid == null)
				return null;

			var hex = uuid.Replace("-", "");
			var digits = ntfs && hex.Length == 16 ? hex.AsSpan(8) : hex.AsSpan(0, Math.Min(hex.Length, 8));
			return digits.Length == 8 && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var serial)
				? serial : null;
		}

		internal Drive(string path)
			: base(path) { }

		internal override void Eject() => RunEject("eject");

		internal override void Lock() => RunEject("lock", "-i", "1");

		internal override void Retract() => RunEject("retract", "-t");

		internal override void UnLock() => RunEject("unlock", "-i", "0");

		private void RunEject(string what, params string[] options) =>
			RunOnDrive(what, () => DriveCommand("eject", path == null ? options : [.. options, "--", path]));

		// The device number and source of the file system that holds the path. --nofsroot leaves out the [/dir]
		// findmnt otherwise adds to the source for a bind mount or a btrfs subvolume.
		private (string Number, string Source) MountSource()
		{
			var output = DriveCommand("findmnt", ["--noheadings", "--nofsroot", "--output", "MAJ:MIN,SOURCE", "--target", drive.Name], 5000);
			var line = output.AsSpan().Trim();
			line = line[(line.LastIndexOf('\n') + 1)..].Trim();
			var gap = line.IndexOfAny(' ', '\t');

			if (gap < 0)
				throw new IOException($"findmnt found no file system for {drive.Name}.");

			return (line[..gap].ToString(), line[gap..].Trim().ToString());
		}

		protected override void WriteLabel(string label)
		{
			var source = MountSource().Source;
			var format = drive.DriveFormat.ToLowerInvariant();
			// Older fatlabel versions expect two arguments and reject "--".
			(string FileName, string[] Arguments) command = format switch
			{
				"ext2" or "ext3" or "ext4" => ("e2label", [source, label]),
				"btrfs" => ("btrfs", ["filesystem", "label", "--", drive.Name, label]),
				"xfs" => ("xfs_admin", ["-L", label, source]),
				"vfat" or "fat" or "fat32" or "msdos" => ("fatlabel", label.StartsWith('-') ? ["--", source, label] : [source, label]),
				"ntfs" or "ntfs3" => ("ntfslabel", ["--", source, label]),
				"exfat" => ("exfatlabel", ["--", source, label]),
				_ => throw new PlatformNotSupportedException($"Changing a {format} volume label is not supported on Linux.")
			};

			_ = DriveCommand(command.FileName, command.Arguments);
		}
	}
}
#endif
