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
}
