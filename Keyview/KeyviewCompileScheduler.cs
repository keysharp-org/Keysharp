namespace Keyview;

internal sealed class KeyviewCompileScheduler
{
	private readonly TimeSpan idleDelay;
	private long completedVersion;
	private bool force;
	private DateTime lastEdit;

	internal long EditVersion { get; private set; }

	internal bool IsCompiling { get; private set; }

	internal KeyviewCompileScheduler(TimeSpan idleDelay) => this.idleDelay = idleDelay;

	internal void Complete(long version)
	{ completedVersion = version; IsCompiling = false; }

	internal void CompleteExplicit() => IsCompiling = false;

	internal bool IsCurrent(long version) => version == EditVersion;

	internal bool IsCurrent(long version, string sourcePath, string currentPath) => IsCurrent(version) && sourcePath == currentPath;

	internal bool IsIdle(DateTime now) => now - lastEdit >= idleDelay;

	internal void RequestCompile() => force = true;

	internal void TextChanged(DateTime now)
	{ EditVersion++; lastEdit = now; }

	internal bool TryBegin(DateTime now, bool hasText, out long version)
	{
		version = EditVersion;
		if (IsCompiling || !hasText || (!force && (version == completedVersion || !IsIdle(now))))
			return false;
		force = false;
		IsCompiling = true;
		return true;
	}

	internal bool TryBeginExplicit()
	{
		if (IsCompiling)
			return false;
		IsCompiling = true;
		return true;
	}
}