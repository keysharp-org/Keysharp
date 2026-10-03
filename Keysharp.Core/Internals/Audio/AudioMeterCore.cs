namespace Keysharp.Internals.Audio
{
	internal sealed class AudioMeterCore : IDisposable
	{
		private readonly Lock gate = new();
		private IAudioNativeMeter native;
		private bool disposed;

		internal double Peak
		{
			get { lock (gate) return native?.Peak ?? -1; }
		}

		internal bool TryOpen(IAudioBackend backend, string targetId, bool isSession, double interval, out string error)
		{
			lock (gate)
			{
				if (disposed)
				{
					error = "This script is shutting down.";
					return false;
				}

				return backend.TryOpenMeter(targetId, isSession, interval, out native, out error);
			}
		}

		public void Dispose()
		{
			lock (gate)
			{
				disposed = true;
				var old = native;
				native = null;
				old?.Dispose();
			}
		}
	}
}
