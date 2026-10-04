#if LINUX
namespace Keysharp.Internals.Window.Linux
{
	/// <summary>Forwards the process-wide Linux display signal while started.</summary>
	internal sealed class MonitorEventBackend : IMonitorEventBackend
	{
		private readonly Lock gate = new();
		private bool started;
		private bool disposed;

		public Action Sink { get; set; }

		public void Start()
		{
			lock (gate)
			{
				if (started || disposed)
					return;

				started = true;
				LinuxDisplayChanges.Changed += OnChanged;
			}

			LinuxDisplayChanges.EnsureConnected();
		}

		public void Stop()
		{
			lock (gate)
				Detach();
		}

		public void Dispose()
		{
			lock (gate)
			{
				disposed = true;
				Detach();
				Sink = null;
			}
		}

		private void Detach()
		{
			if (!started)
				return;

			started = false;
			LinuxDisplayChanges.Changed -= OnChanged;
		}

		private void OnChanged()
		{
			Action sink;
			lock (gate)
				sink = started ? Sink : null;

			// The sink can stop this backend, so it runs outside the lifecycle lock.
			sink?.Invoke();
		}
	}
}
#endif
