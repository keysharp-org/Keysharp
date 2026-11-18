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
		internal override string StatusCD => DefaultObject;

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

		protected DriveInfo drive => info ??= new DriveInfo(path);

		/// <summary>The volume serial, raising an exception when the volume has none or the query fails.</summary>
		protected abstract long SerialNumber { get; }

		internal UnixDriveBase(string path)
			: base(path) { }

		protected static string DriveCommand(string fileName, string[] arguments, int timeoutMs = -1)
		{
			var result = RunCommand(fileName, arguments, timeoutMs);
			return result.Succeeded ? result.StandardOutput : throw new IOException($"{Path.GetFileName(fileName)}: {result.ErrorMessage}");
		}

		protected void RunOnDrive(string what, Action action)
		{
			try
			{
				// Only an omitted path selects the default device.
				if (path == "")
					throw new ArgumentException("The drive path is empty.");

				action();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
			{
				_ = Errors.OSErrorOccurred(ex, $"Failed to {what} drive {path}.");
			}
		}

		internal override void SetLabel(string label) => RunOnDrive("label", () => WriteLabel(label));

		protected abstract void WriteLabel(string label);

		private object Query(Func<DriveInfo, object> read, string what)
		{
			try
			{
				return read(drive);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
			{
				return QueryFailed(ex, what);
			}
		}
	}
}
#endif
