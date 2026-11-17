namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Native memory for a Buffer or Struct, reported as memory pressure when the block is large enough.
	/// </summary>
	internal static class NativeAllocation
	{
		private const long PressureThreshold = 64 * 1024;

		internal static nint Allocate(long bytes)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(bytes);
			if (bytes == 0)
				return 0;

			var ptr = Marshal.AllocHGlobal(checked((nint)bytes));

			try
			{
				if (bytes >= PressureThreshold)
					GC.AddMemoryPressure(bytes);

				return ptr;
			}
			catch
			{
				Marshal.FreeHGlobal(ptr);
				throw;
			}
		}

		internal static void Free(nint ptr, long bytes)
		{
			if (ptr == 0)
				return;

			Marshal.FreeHGlobal(ptr);

			if (bytes >= PressureThreshold)
				GC.RemoveMemoryPressure(bytes);
		}
	}

	/// <summary>Collects native-handle owners using <see cref="HandleCollector"/>'s policy without sleeping the script thread.</summary>
	internal sealed class HandlePressure(int initialThreshold)
	{
		internal static readonly HandlePressure Files = new(128);
		internal static readonly HandlePressure Com = new(512);

		private readonly Lock gate = new();
		private readonly int minimum = initialThreshold;
		private int count;
		private int threshold = initialThreshold;
		private int generation;

		internal void Add()
		{
			int collect;

			lock (gate)
			{
				var open = ++count;

				if (open <= threshold)
					return;

				threshold = open + open / 10;
				collect = generation;
				generation = Math.Min(2, generation + 1);
			}

			GC.Collect(collect);
		}

		internal void Remove()
		{
			lock (gate)
			{
				var open = --count;
				var next = open + open / 10;

				if (next < threshold - threshold / 10)
				{
					threshold = Math.Max(minimum, next);
					generation = 0;
				}
			}
		}
	}
}
