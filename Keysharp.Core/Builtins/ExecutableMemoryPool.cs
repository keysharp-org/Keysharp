#if WINDOWS
namespace Keysharp.Builtins
{
	/// <summary>
	/// Manages executable memory in pages, providing fixed 64-byte chunks to DllCall, which writes a small
	/// machine-code shim into one when it has to copy floating-point arguments into general purpose registers
	/// (see NativeInvoker). Automatically allocates new pages when needed and reuses freed chunks.
	/// This is needed because VirtualAlloc is quite a heavy function, best called as few times as possible.
	///
	/// A rented chunk is normally kept for as long as the function it shims can be called, rather than returned
	/// per call: writing into a chunk that was just executed trips the processor's self-modifying-code detection
	/// and costs a pipeline flush. Return is for a chunk whose function is going away, which is what
	/// CallbackFree does.
	///
	/// Renting and returning are serialised, because a lock-free free list would need tagged pointers to be
	/// safe here: chunks hold executable code, so handing the same chunk to two callers would let one overwrite
	/// a shim the other is about to jump to. The lock costs nothing next to the DllCall it accompanies.
	///
	/// Only the x64 shim rents from this pool, and Script creates it when that first needs a chunk. Anyone
	/// adding an ARM64 shim must also flush the instruction cache over the chunk before jumping to it
	/// (FlushInstructionCache): ARM64 has separate, non-coherent I and D caches, so freshly written bytes are not
	/// guaranteed to be visible to the fetcher. x64 needs no such flush, which is why nothing here does one. The
	/// pages last as long as the process.
	/// </summary>
	public sealed class ExecutableMemoryPoolManager
	{
		// A whole page is mapped at once regardless, so carve the full page rather than reserving a granule to
		// hand out only a few chunks.
		private const int PageSize = 4096;
		//Internal so the shim emitter can prove at compile time that what it writes fits (see NativeInvoker.ShimFitsChunk).
		internal const int ChunkSize = 64;
		private readonly Lock _lock = new();

		// Head of the free-chunk list, each chunk holding the address of the next (0 == empty).
		private nint _freeList;

		// Current page and offset
		private nint _currentPage;
		private int _currentOffset = 0;

		public ExecutableMemoryPoolManager()
		{
			// Eagerly allocate the first page so _currentPage != 0.
			_currentPage = AllocatePage();
		}

		/// <summary>
		/// Rents an executable chunk of ChunkSize bytes.
		/// </summary>
		public nint Rent()
		{
			lock (_lock)
			{
				// Reuse a returned chunk if there is one. The free list threads itself through the chunks: each
				// holds the address of the next one in its first pointer-sized bytes.
				if (_freeList != 0)
				{
					var head = _freeList;
					_freeList = Marshal.ReadIntPtr(head);
					return head;
				}

				// Otherwise carve the next chunk off the current page, allocating a fresh one when it is full.
				if (_currentOffset + ChunkSize > PageSize)
				{
					_currentPage = AllocatePage();
					_currentOffset = 0;
				}

				var chunk = _currentPage + _currentOffset;
				_currentOffset += ChunkSize;
				return chunk;
			}
		}

		public void Return(nint ptr)
		{
			if (ptr == 0)
				return;

			lock (_lock)
			{
				Marshal.WriteIntPtr(ptr, _freeList);
				_freeList = ptr;
			}
		}

		private static nint AllocatePage()
		{
			var ptr = WindowsAPI.VirtualAlloc(0, (nint)PageSize, (uint)VirtualAllocExTypes.MEM_COMMIT, (uint)AccessProtectionFlags.PAGE_EXECUTE_READWRITE);
			return ptr == 0 ? throw new InvalidOperationException($"VirtualAlloc failed: {Marshal.GetLastWin32Error()}") : ptr;
		}
	}
}
#endif
