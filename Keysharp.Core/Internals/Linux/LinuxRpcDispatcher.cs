#if LINUX
namespace Keysharp.Internals.Linux
{
	internal interface ILinuxConnectionDispatcher : IDisposable
	{
		bool IsOwnerThread { get; }
		bool IsRunning { get; }
		void Start();
		T Invoke<T>(Func<T> command, int timeoutMs = 130_000);
		void Post(Action command);
	}

	/// <summary>Serializes RPC on the calling thread and defers cleanup until an active call returns.</summary>
	internal sealed class LinuxRpcDispatcher(Action cleanup) : ILinuxConnectionDispatcher
	{
		private readonly object sync = new();
		private int stopping, ownerThread, depth;
		private bool cleaned;
		public bool IsOwnerThread => Volatile.Read(ref ownerThread) == Environment.CurrentManagedThreadId;
		public bool IsRunning => Volatile.Read(ref stopping) == 0;
		public void Start() => ObjectDisposedException.ThrowIf(!IsRunning, this);

		public T Invoke<T>(Func<T> command, int timeoutMs = 130_000)
		{
			var deadline = Environment.TickCount64 + timeoutMs;
			lock (sync)
			{
				ObjectDisposedException.ThrowIf(!IsRunning, this);
				while (depth != 0 && !IsOwnerThread)
				{
					var remaining = deadline - Environment.TickCount64;
					if (remaining <= 0 || !Monitor.Wait(sync, (int)remaining))
						throw new TimeoutException("The Linux RPC connection is busy.");
					ObjectDisposedException.ThrowIf(!IsRunning, this);
				}
				Volatile.Write(ref ownerThread, Environment.CurrentManagedThreadId);
				depth++;
			}
			try { return command(); }
			finally
			{
				var release = false;
				lock (sync)
				{
					if (--depth == 0)
					{
						Volatile.Write(ref ownerThread, 0);
						release = ClaimCleanup();
						Monitor.PulseAll(sync);
					}
				}
				if (release) cleanup();
			}
		}

		public void Post(Action command) => Invoke(() => { command(); return true; });

		public void Dispose()
		{
			bool release;
			lock (sync)
			{
				Volatile.Write(ref stopping, 1);
				Monitor.PulseAll(sync);
				release = ClaimCleanup();
			}
			if (release) cleanup();
		}

		private bool ClaimCleanup()
		{
			if (IsRunning || depth != 0 || cleaned) return false;
			cleaned = true;
			return true;
		}
	}
}
#endif
