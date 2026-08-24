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
	/// Only the x64 shim rents from this pool today, so on other architectures the pool is constructed and
	/// never used. Anyone adding an ARM64 shim must also flush the instruction cache over the chunk before
	/// jumping to it (FlushInstructionCache): ARM64 has separate, non-coherent I and D caches, so freshly
	/// written bytes are not guaranteed to be visible to the fetcher. x64 needs no such flush, which is why
	/// nothing here does one.
	/// </summary>
	public sealed class ExecutableMemoryPoolManager : IDisposable
	{
		// A whole page is mapped at once regardless, so carve the full page rather than reserving a granule to
		// hand out only a few chunks.
		private const int PageSize = 4096;
		//Internal so the shim emitter can prove at compile time that what it writes fits (see NativeInvoker.ShimFitsChunk).
		internal const int ChunkSize = 64;
		private readonly Lock _lock = new();

		// Head of the free-chunk list, each chunk holding the address of the next (0 == empty).
		private nint _freeList;

		// All allocated pages
		private readonly List<nint> _pages = new List<nint>();

		// Current page and offset
		private nint _currentPage;
		private int _currentOffset = 0;

#if !WINDOWS
		[DllImport("libc", SetLastError = true)]
		private static extern nint mmap(nint addr, nint length, int prot, int flags, int fd, nint offset);
		[DllImport("libc", SetLastError = true)]
		private static extern int munmap(nint addr, nint length);

		private const int PROT_READ = 1;
		private const int PROT_WRITE = 2;
		private const int PROT_EXEC = 4;
		private const int MAP_PRIVATE = 2;
#if LINUX
		private const int MAP_ANONYMOUS = 0x20;
#elif OSX
		private const int MAP_ANONYMOUS = 0x1000; // MAP_ANON / MAP_ANONYMOUS on macOS
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
#endif

		public ExecutableMemoryPoolManager()
		{
			// Eagerly allocate the first page so _currentPage != 0.
			_currentPage = AllocatePage();
			_pages.Add(_currentPage);
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
					_pages.Add(_currentPage);
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

		/// <summary>
		/// Releases all allocated pages. Nothing calls this today: the pool is a process-lifetime singleton on
		/// Script, and the pages are reclaimed by the OS at exit, so it exists for a host which needs to tear a
		/// script down without ending the process.
		/// </summary>
		public void Dispose()
		{
			lock (_lock)
			{
#if WINDOWS

				foreach (var page in _pages)
					WindowsAPI.VirtualFree(page, 0, (uint)VirtualAllocExTypes.MEM_RELEASE);

#else

				foreach (var page in _pages)
					munmap(page, (nint)PageSize);

#endif
				_pages.Clear();
				_currentPage = 0;
				// Every chunk lived in the pages just released, so the free list must go too: handing one out
				// again would return a dangling executable pointer.
				_freeList = 0;
				_currentOffset = PageSize;
			}
		}

		private nint AllocatePage()
		{
#if WINDOWS
			var ptr = WindowsAPI.VirtualAlloc(0, (nint)PageSize, (uint)VirtualAllocExTypes.MEM_COMMIT, (uint)AccessProtectionFlags.PAGE_EXECUTE_READWRITE);
			return ptr == 0 ? throw new InvalidOperationException($"VirtualAlloc failed: {Marshal.GetLastWin32Error()}") : ptr;
#else
			// Anything not Windows uses mmap; the platform itself is already validated where MAP_ANONYMOUS is set.
			var ptr = mmap(0, (nint)PageSize, PROT_READ | PROT_WRITE | PROT_EXEC, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);

			if (ptr == new nint(-1))
				throw new InvalidOperationException("mmap failed");

			return ptr;
#endif
		}
	}
}
