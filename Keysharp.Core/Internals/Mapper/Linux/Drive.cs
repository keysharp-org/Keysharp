using Keysharp.Builtins;
#if LINUX

namespace Keysharp.Internals.Mapper.Linux
{
	/// <summary>
	/// Concrete implementation of Drive for the linux platfrom.
	/// </summary>
	internal class Drive : DriveBase
	{
		internal override long Serial
		{
			get
			{
				if ($"udevadm info --query=property --name={drive.Name} | grep ID_SERIAL_SHORT".Bash(out var serial) != 0)
					return 0L;

				if (!string.IsNullOrEmpty(serial))
				{
					var components = serial.Split('=');

					if (components.Length >= 2)
						return components[1].Al();
				}

				return 0L;
			}
		}

		internal override string StatusCD
		{
			get
			{
				Diagnostics.Debug.WriteLine($"Obtaining the status of the CD/DVD drive is not supported on linux.");
				return DefaultObject;
			}
		}

		internal Drive(DriveInfo drv)
			: base(drv) { }

		internal override void Eject()
		{
			if ($"eject {drive.Name}".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.Eject failed for {drive.Name}");
		}

		internal override void Lock()
		{
			if ($"eject -i 1 {drive.Name}".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.Lock failed for {drive.Name}");
		}

		internal override void Retract()
		{
			if ($"eject -t {drive.Name}".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.Retract failed for {drive.Name}");
		}

		internal override void SetLabel(string label)
		{
			// DriveInfo.Name is normally a mount point on Linux. Label utilities generally
			// need the backing block device, so resolve it through findmnt first.
			var lookup = RunCommand("findmnt", "--noheadings", "--output", "SOURCE", "--target", drive.Name);

			if (!lookup.Succeeded)
				throw new IOException($"Could not resolve the block device for {drive.Name}: {lookup.ErrorMessage}");
			if (string.IsNullOrWhiteSpace(lookup.StandardOutput))
				throw new IOException($"findmnt returned no block device for {drive.Name}.");

			var source = lookup.StandardOutput.Trim();
			var format = drive.DriveFormat.ToLowerInvariant();
			(string FileName, string[] Arguments)? command = format switch
			{
				"ext2" or "ext3" or "ext4" => ("e2label", [source, label]),
				"btrfs" => ("btrfs", ["filesystem", "label", drive.Name, label]),
				"xfs" => ("xfs_admin", ["-L", label, source]),
				"vfat" or "fat" or "fat32" or "msdos" => ("fatlabel", [source, label]),
				"ntfs" or "ntfs3" => ("ntfslabel", [source, label]),
				"exfat" => ("exfatlabel", [source, label]),
				_ => null
			};

			if (command == null)
				throw new PlatformNotSupportedException($"Changing a {drive.DriveFormat} volume label is not supported on Linux.");

			var result = RunCommand(command.Value.FileName, command.Value.Arguments);

			if (!result.Succeeded)
				throw new IOException(result.ErrorMessage);
		}

		internal override void UnLock()
		{
			if ($"eject -i 0 {drive.Name}".Bash() != 0)
				Diagnostics.Debug.WriteLine($"Drive.UnLock failed for {drive.Name}");
		}
	}
}
#endif
