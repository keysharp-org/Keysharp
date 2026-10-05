#if LINUX
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Keysharp.Internals.Linux
{
	internal sealed class LinuxConnectionOwner : ILinuxConnectionDispatcher
	{
		private readonly ConcurrentQueue<Action> commands = new();
		private readonly object lifecycle = new();
		private readonly Func<int> connectionFd;
		private readonly Func<bool> drain;
		private readonly Action cleanup;
		private readonly Thread thread;
		private int wakeFd;
		private int stopping;
		private Exception failure;

		internal LinuxConnectionOwner(string name, Func<int> fd, Func<bool> drain, Action cleanup)
		{
			connectionFd = fd;
			this.drain = drain;
			this.cleanup = cleanup;
			wakeFd = eventfd(0, 0x800 | 0x80000);

			if (wakeFd < 0)
				throw new IOException($"eventfd failed: errno={Marshal.GetLastPInvokeError()}");

			thread = new Thread(Run) { IsBackground = true, Name = name };
		}

		public bool IsOwnerThread => Thread.CurrentThread == thread;
		public bool IsRunning => Volatile.Read(ref stopping) == 0;
		public void Start()
		{
			lock (lifecycle)
			{
				ObjectDisposedException.ThrowIf(stopping != 0, this);
				thread.Start();
			}
		}

		public T Invoke<T>(Func<T> command, int timeoutMs = 130_000)
		{
			ObjectDisposedException.ThrowIf(!IsRunning, this);

			if (IsOwnerThread)
				return command();

			var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			var started = 0;

			Enqueue(() =>
			{
				if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
					return;

				if (Volatile.Read(ref stopping) != 0)
					completion.TrySetException(failure ?? new ObjectDisposedException(nameof(LinuxConnectionOwner)));
				else
				{
					try { completion.TrySetResult(command()); }
					catch (Exception error) { completion.TrySetException(error); }
				}
			});

			try
			{
				return completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).GetAwaiter().GetResult();
			}
			catch (TimeoutException) when (!completion.Task.IsCompleted)
			{
				Interlocked.CompareExchange(ref started, 2, 0);
				throw new TimeoutException($"{thread.Name} command did not finish within {timeoutMs} ms.");
			}
		}

		public void Post(Action command)
		{
			ObjectDisposedException.ThrowIf(!IsRunning, this);
			if (IsOwnerThread) { command(); return; }
			Enqueue(() => { if (Volatile.Read(ref stopping) == 0) command(); });
		}

		private void Enqueue(Action command)
		{
			lock (lifecycle)
			{
				ObjectDisposedException.ThrowIf(stopping != 0, this);
				commands.Enqueue(command);
				Signal();
			}
		}

		private unsafe void Signal()
		{
			ulong value = 1;
			while (write(wakeFd, &value, 8) < 0 && Marshal.GetLastPInvokeError() == 4) { }
		}

		private unsafe void Run()
		{
			try
			{
				var fds = new PollFd[2];

				while (Volatile.Read(ref stopping) == 0)
				{
					while (commands.TryDequeue(out var command))
						command();

					if (Volatile.Read(ref stopping) != 0)
						break;

					// RPC replies may have buffered stream records without leaving the socket readable.
					if (drain())
						continue;

					fds[0] = new PollFd { Fd = connectionFd(), Events = 1 };
					fds[1] = new PollFd { Fd = wakeFd, Events = 1 };
					var ready = poll(fds, 2, -1);

					if (ready < 0)
					{
						if (Marshal.GetLastPInvokeError() == 4)
							continue;

						throw new IOException($"poll failed: errno={Marshal.GetLastPInvokeError()}");
					}

					if (fds[1].ReturnedEvents != 0)
					{
						ulong value;
						while (read(wakeFd, &value, 8) < 0 && Marshal.GetLastPInvokeError() == 4) { }
					}

					if ((fds[0].ReturnedEvents & (8 | 16 | 32)) != 0 && !drain())
						throw new IOException($"{thread.Name} connection closed.");
				}
			}
			catch (Exception error)
			{
				failure = error;
			}
			finally
			{
				lock (lifecycle)
					Volatile.Write(ref stopping, 1);

				while (commands.TryDequeue(out var command))
					command();

				try { cleanup(); }
				catch (Exception error) { Diagnostics.Debug.WriteLine(error.Message); }
				finally
				{
					lock (lifecycle)
					{
						close(wakeFd);
						wakeFd = -1;
					}
				}
			}
		}

		public void Dispose()
		{
			lock (lifecycle)
			{
				Volatile.Write(ref stopping, 1);
				if ((thread.ThreadState & System.Threading.ThreadState.Unstarted) != 0) thread.Start();
				if (wakeFd >= 0)
					Signal();
			}

			while (commands.TryDequeue(out var command))
				command();

			if (!IsOwnerThread && thread.IsAlive)
				thread.Join(100);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PollFd { internal int Fd; internal short Events, ReturnedEvents; }
		[DllImport("libc", SetLastError = true)] private static extern int eventfd(uint initial, int flags);
		[DllImport("libc", SetLastError = true)] private static extern int poll([In, Out] PollFd[] fds, uint count, int timeout);
		[DllImport("libc", SetLastError = true)] private static extern unsafe nint read(int fd, void* buffer, nuint count);
		[DllImport("libc", SetLastError = true)] private static extern unsafe nint write(int fd, void* buffer, nuint count);
		[DllImport("libc")] private static extern int close(int fd);
	}
}
#endif
