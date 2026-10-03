namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Bounds the per-user directories that compiled scripts extract embedded payloads into. Each entry's last-write
	/// time records recency; a shared lease prevents pruning while a process can still load its payloads.
	/// </summary>
	internal static class ExtractionCache
	{
		// A few recent entries survive a rebuild or a version switch without being extracted again.
		private const int KeepRecent = 4;
		// A script started from an older entry may still be running and load from it later, so an entry is only
		// removed once it has gone unused for a while.
		private static readonly TimeSpan IdleGrace = TimeSpan.FromHours(1);
		private static readonly TimeSpan MaxIdle = TimeSpan.FromDays(30);
		private static readonly Dictionary<string, FileStream> active = new(OperatingSystem.IsWindows()
			? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		internal static void Use(string entryRoot)
		{
			entryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entryRoot));
			lock (active)
			{
				if (active.ContainsKey(entryRoot)) return;
				Directory.CreateDirectory(Path.GetDirectoryName(entryRoot));
				if (!TryLease(entryRoot, false, out var lease))
					throw new IOException("The embedded payload cache entry is being pruned.");
				try { Directory.CreateDirectory(entryRoot); }
				catch { lease.Dispose(); throw; }
				active.Add(entryRoot, lease);
			}
		}

		internal static bool TryLease(string entryRoot, bool exclusive, out FileStream lease)
		{
			lease = null;
			try
			{
				// Keep the lock inode outside the entry so pruning cannot replace it beneath a waiting user.
				lease = new FileStream(Path.TrimEndingDirectorySeparator(Path.GetFullPath(entryRoot)) + ".in-use", FileMode.OpenOrCreate,
					exclusive ? FileAccess.ReadWrite : FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
#if !WINDOWS
				if (flock(lease.SafeFileHandle.DangerousGetHandle().ToInt32(), (exclusive ? 2 : 1) | 4) != 0)
					throw new IOException("The embedded payload cache entry is in use.");
#endif
				return true;
			}
			catch
			{
				lease?.Dispose();
				lease = null;
				return false;
			}
		}

#if !WINDOWS
		[DllImport("libc", SetLastError = true)]
		private static extern int flock(int descriptor, int operation);
#endif

		internal static void Touch(string entryRoot)
		{
			try { Directory.SetLastWriteTimeUtc(entryRoot, DateTime.UtcNow); } catch { }
		}

		internal static void TouchAndPrune(string cacheRoot, string entryRoot)
		{
			Use(entryRoot);
			Touch(entryRoot);

			try
			{
				var now = DateTime.UtcNow;
				var current = Path.GetFullPath(entryRoot);
				var others = Directory.EnumerateDirectories(cacheRoot)
					.Where(directory => !Path.GetFullPath(directory).Equals(current, PathComparison))
					.Select(directory => (Root: directory, Used: Directory.GetLastWriteTimeUtc(directory)))
					.OrderByDescending(entry => entry.Used)
					.ToList();

				for (var i = 0; i < others.Count; i++)
				{
					var idle = now - others[i].Used;

					if (idle > MaxIdle || (i >= KeepRecent - 1 && idle > IdleGrace))
					{
						if (!TryLease(others[i].Root, true, out var lease)) continue;
						using (lease)
							try
							{ Directory.Delete(others[i].Root, true); }
							catch { }
					}
				}
			}
			catch
			{
			}
		}

#if WINDOWS
		private const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
#else
		private const StringComparison PathComparison = StringComparison.Ordinal;
#endif
	}
}
