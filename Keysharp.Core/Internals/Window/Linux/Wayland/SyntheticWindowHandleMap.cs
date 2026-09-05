#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Gives opaque compositor identifiers process-local window handles without assuming a 64-bit address space.
	/// Handles remain stable while an identifier is live and are not reused during the map's lifetime.
	/// </summary>
	internal sealed class SyntheticWindowHandleMap<T> where T : notnull
	{
		private readonly object sync = new();
		private readonly Dictionary<T, nint> handlesByValue = [];
		private readonly Dictionary<nint, T> valuesByHandle = [];
		private readonly HashSet<nint> absentOnce = [];
		private readonly HashSet<nint> liveHandles = [];
		private readonly HashSet<nint> retainedHandles = [];
		private readonly List<nint> expiredHandles = [];
		private int nextHandle;

		internal nint GetOrCreate(T value)
		{
			lock (sync)
			{
				if (handlesByValue.TryGetValue(value, out var existing))
				{
					_ = absentOnce.Remove(existing);
					_ = liveHandles.Add(existing);
					return existing;
				}

				// X11 resource identifiers occupy the non-negative range on supported X servers.
				// Negative values keep opaque Wayland identities distinct on both 32- and 64-bit runtimes.
				if (nextHandle == int.MinValue)
					throw new InvalidOperationException("The compositor window-handle space is exhausted.");

				var handle = new nint(--nextHandle);
				handlesByValue[value] = handle;
				valuesByHandle[handle] = value;
				_ = liveHandles.Add(handle);
				return handle;
			}
		}

		internal bool TryGetValue(nint handle, out T value)
		{
			lock (sync)
			{
				value = default;
				return liveHandles.Contains(handle) && valuesByHandle.TryGetValue(handle, out value);
			}
		}

		internal bool Contains(nint handle)
		{
			lock (sync)
				return liveHandles.Contains(handle);
		}

		internal IReadOnlyList<nint> Retain(IEnumerable<nint> currentHandles)
		{
			lock (sync)
			{
				retainedHandles.Clear();

				foreach (var handle in currentHandles)
					_ = retainedHandles.Add(handle);

				List<nint> removed = null;

				foreach (var handle in liveHandles)
					if (!retainedHandles.Contains(handle))
						(removed ??= []).Add(handle);

				expiredHandles.Clear();

				foreach (var pair in valuesByHandle)
					if (retainedHandles.Contains(pair.Key))
						_ = absentOnce.Remove(pair.Key);
					else if (!absentOnce.Add(pair.Key))
						expiredHandles.Add(pair.Key);

				foreach (var handle in expiredHandles)
				{
					var value = valuesByHandle[handle];
					_ = valuesByHandle.Remove(handle);
					_ = handlesByValue.Remove(value);
					_ = absentOnce.Remove(handle);
				}

				liveHandles.Clear();
				liveHandles.UnionWith(retainedHandles);

				return removed ?? [];
			}
		}
	}
}
#endif
