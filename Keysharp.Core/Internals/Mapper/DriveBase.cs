using Keysharp.Builtins;
namespace Keysharp.Internals.Mapper
{
	/// <summary>
	/// A drive as the script named it. The path is kept as given so each platform decides what it identifies,
	/// and each member raises its own script error, returning the continued error's result.
	/// </summary>
	internal abstract class DriveBase
	{
		// Null when the script omitted the drive, which selects the default CD/DVD drive.
		protected readonly string path;

		internal abstract object Capacity { get; }
		internal abstract object FileSystem { get; }
		internal abstract object Label { get; }
		internal abstract object Serial { get; }
		internal abstract object SpaceFree { get; }
		internal abstract string Status { get; }
		internal abstract string StatusCD { get; }
		internal abstract string Type { get; }

		internal DriveBase(string path) => this.path = path;

		/// <summary>
		/// Ejects the CD Drive
		/// </summary>
		internal abstract void Eject();

		/// <summary>
		/// Locks the drives Eject ability
		/// </summary>
		internal abstract void Lock();

		/// <summary>
		/// Retracts the CD Drive
		/// </summary>
		internal abstract void Retract();

		/// <summary>
		/// Changes the volume label.
		/// </summary>
		internal abstract void SetLabel(string label);

		/// <summary>
		/// Unlocks the drives Eject ability
		/// </summary>
		internal abstract void UnLock();

		// The error is the system's code or the exception that carried it.
		protected object QueryFailed(object error, string what) => Errors.OSErrorOccurred(error, $"Failed to get {what} for drive {path}.");
	}
}
