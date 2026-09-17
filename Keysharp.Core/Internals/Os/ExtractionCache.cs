namespace Keysharp.Internals.Os
{
	/// <summary>
	/// Bounds the per-user directories that compiled scripts extract embedded payloads into. Each entry's last-write
	/// time records when it was last used, so the cache keeps what is in use and lets everything else go.
	/// </summary>
	internal static class ExtractionCache
	{
		// A few recent entries survive a rebuild or a version switch without being extracted again.
		private const int KeepRecent = 4;
		// A script started from an older entry may still be running and load from it later, so an entry is only
		// removed once it has gone unused for a while.
		private static readonly TimeSpan IdleGrace = TimeSpan.FromHours(1);
		private static readonly TimeSpan MaxIdle = TimeSpan.FromDays(30);

		internal static void Touch(string entryRoot)
		{
			try { Directory.SetLastWriteTimeUtc(entryRoot, DateTime.UtcNow); } catch { }
		}

		internal static void TouchAndPrune(string cacheRoot, string entryRoot)
		{
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
						try { Directory.Delete(others[i].Root, true); } catch { }
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
