using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Mapper
{
	/// <summary>
	/// The queries Linux and macOS share: .NET resolves the path to the mounted file system that contains it.
	/// </summary>
	internal abstract class UnixDriveBase : DriveBase
	{
		private DriveInfo info;

		internal override object Capacity => Query(static d => d.TotalSize / 1024 / 1024, "capacity");
		internal override object FileSystem => Query(static d => d.DriveFormat, "file system");
		internal override object Label => Query(static d => d.VolumeLabel, "label");
		internal override object Serial => Query(_ => SerialNumber, "serial number");
		// The caller's available bytes rather than the volume's, which leaves out blocks reserved for root.
		internal override object SpaceFree => Query(static d => d.AvailableFreeSpace / (1024 * 1024), "free space");

		// As in AutoHotkey, a status is reported for every path and no error is raised.
		internal override string Status
		{
			get
			{
				try
				{
					_ = drive.DriveFormat;//Throws for a path that does not exist.
					return drive.IsReady ? "Ready" : "NotReady";
				}
				catch (Exception ex)
				{
					return ex is DriveNotFoundException or DirectoryNotFoundException or FileNotFoundException or ArgumentException ? "Invalid" : "Unknown";
				}
			}
		}

		internal override string Type
		{
			get
			{
				try
				{
					return Script.TheScript.DriveTypeMapper.LookUpKeysharpType(drive.DriveType);
				}
				catch
				{
					return "";
				}
			}
		}

		// Null when the drive was omitted and the machine has no removable drive, after the error is raised.
		protected DriveInfo drive => info ??= path != null ? new DriveInfo(path) : DefaultRemovableDrive();

		// The actions check this first: it raises the script's error for a path DriveInfo rejects, such as an empty one.
		protected bool HasDrive
		{
			get
			{
				try
				{
					return drive != null;
				}
				catch (ArgumentException ex)
				{
					_ = Errors.OSErrorOccurred(ex, $"Failed to access drive {path}.");
					return false;
				}
			}
		}

		protected abstract long SerialNumber { get; }

		internal UnixDriveBase(string path)
			: base(path) { }

		internal override void SetLabel(string label)
		{
			try
			{
				WriteLabel(label);
			}
			catch (Exception ex)
			{
				_ = Errors.OSErrorOccurred(ex, $"Failed to change the volume label for {path}.");
			}
		}

		protected abstract void WriteLabel(string label);

		private static DriveInfo DefaultRemovableDrive()
		{
			foreach (var candidate in DriveInfo.GetDrives())
				if (candidate.DriveType is DriveType.CDRom or DriveType.Removable)
					return candidate;

			_ = Errors.ErrorOccurred("Failed to find any CDROM or DVD drives.");
			return null;
		}

		private object Query(Func<DriveInfo, object> read, string what)
		{
			try
			{
				return read(drive);
			}
			catch (Exception ex)
			{
				return QueryFailed(ex, what);
			}
		}
	}
}
#endif
