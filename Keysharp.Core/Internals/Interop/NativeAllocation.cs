namespace Keysharp.Internals.Interop
{
	/// <summary>
	/// Native memory for a Buffer or Struct, which the GC cannot see. A large block is reported as memory pressure, so a
	/// loop allocating big buffers collects, and frees, them instead of committing native memory until a gen0 GC.
	/// </summary>
	internal static class NativeAllocation
	{
		private const long PressureThreshold = 64 * 1024;

		internal static nint Allocate(long bytes)
		{
			var ptr = Marshal.AllocHGlobal((nint)bytes);

			if (bytes >= PressureThreshold)
				GC.AddMemoryPressure(bytes);

			return ptr;
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
