using Keysharp.Internals.Audio;

namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>
		/// Per-platform <see cref="IAudioBackend"/> factory. One backend serves a whole process: it owns the
		/// connection to the audio server or the COM enumerator, both of which are process-level facts, while every
		/// object with a lifetime hangs off the owning <c>AudioService</c> instead.
		/// </summary>
		internal static class Audio
		{
			internal static IAudioBackend CreateBackend()
			{
#if WINDOWS
				return new WasapiAudioBackend();
#elif LINUX
				return new PulseAudioBackend();
#elif OSX
				return new AudioQueueBackend();
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}
	}
}

namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// The per-<see cref="Script"/> owner of everything audio. It creates the platform backend once, on demand, so
	/// a script that never touches audio pays nothing; it holds the open outputs so teardown can close them
	/// deterministically; and it caps how many can be open at once, because an output that is dropped without being
	/// closed keeps its native stream by design.
	/// <para>
	/// Nothing here reads <c>Script.TheScript</c>. The owning script is captured at construction, which is what lets
	/// two scripts in one process own independent audio without one's teardown silencing the other.
	/// </para>
	/// </summary>
	internal sealed class AudioService : IDisposable
	{
		/// <summary>How many outputs one script may hold open. See the design's D5: dropping a wrapper is not a close.</summary>
		internal const int MaxOpenOutputs = 16;

		private readonly Script owner;
		private readonly Lock gate = new();
		private readonly List<AudioOutputCore> outputs = [];
		private IAudioBackend backend;
		private bool backendResolved;
		private string backendFailure;
		private bool disposed;

		internal AudioService(Script owner, IAudioBackend backend = null)
		{
			this.owner = owner;
			this.backend = backend;
			backendResolved = backend != null;
		}

		internal Script Owner => owner;

		/// <summary>
		/// The backend, created on first use. Never throws: a platform whose library or server is missing yields a
		/// backend that reports itself unavailable with an actionable reason, and a backend that cannot even be
		/// constructed is remembered as a failure rather than retried on every property read.
		/// </summary>
		internal IAudioBackend Backend
		{
			get
			{
				lock (gate)
				{
					if (disposed)
						return null;

					if (backendResolved)
						return backend;

					backendResolved = true;

					try
					{
						backend = Platform.Audio.CreateBackend();
					}
					catch (Exception ex)
					{
						backendFailure = ex.Message;
						Diagnostics.Debug.WriteLine($"Audio backend creation failed: {ex.Message}");
					}

					return backend;
				}
			}
		}

		internal bool Supports(AudioCapability capability) => Backend is { } b && b.IsAvailable && b.Supports(capability);

		/// <summary>The actionable sentence a refused operation puts inside its OSError.</summary>
		internal string UnsupportedReason(AudioCapability capability)
		{
			var b = Backend;

			if (b == null)
				return backendFailure ?? "This platform has no audio backend.";

			var reason = b.UnsupportedReason(capability);
			return reason.IsNullOrEmpty() ? "This host does not provide that audio capability." : reason;
		}

		// ---- output registry ------------------------------------------------------------------

		/// <summary>
		/// Registers one output as open. False means the concurrency cap is reached, which is a program error
		/// rather than an environment one, so the caller raises instead of degrading.
		/// </summary>
		internal bool TryRegisterOpenOutput(AudioOutputCore core)
		{
			lock (gate)
			{
				if (disposed)
					return false;

				if (outputs.Contains(core))
					return true;

				if (outputs.Count >= MaxOpenOutputs)
					return false;

				outputs.Add(core);
				return true;
			}
		}

		internal void UnregisterOutput(AudioOutputCore core)
		{
			lock (gate)
				_ = outputs.Remove(core);
		}

		// ---- recorder and meter registry ------------------------------------------------------

		/// <summary>How many captures or meters one script may hold open, for the reason the output cap exists:
		/// dropping a wrapper without stopping it leaves a native stream running.</summary>
		internal const int MaxOpenRecorders = 8;
		internal const int MaxOpenMeters = 16;

		private readonly List<IDisposable> recorders = [];
		private readonly List<IDisposable> meters = [];

		internal bool TryRegisterRecorder(IDisposable recorder) => TryRegister(recorders, recorder, MaxOpenRecorders);

		internal void UnregisterRecorder(IDisposable recorder) => Unregister(recorders, recorder);

		internal bool TryRegisterMeter(IDisposable meter) => TryRegister(meters, meter, MaxOpenMeters);

		internal void UnregisterMeter(IDisposable meter) => Unregister(meters, meter);

		private bool TryRegister(List<IDisposable> into, IDisposable item, int cap)
		{
			lock (gate)
			{
				if (disposed)
					return false;

				if (into.Contains(item))
					return true;

				if (into.Count >= cap)
					return false;

				into.Add(item);
				return true;
			}
		}

		private void Unregister(List<IDisposable> from, IDisposable item)
		{
			lock (gate)
				_ = from.Remove(item);
		}

		// ---- decoded clip cache ---------------------------------------------------------------

		/// <summary>What a cached decode was made from. A file rewritten in place is a different clip.</summary>
		private readonly record struct ClipKey(string Path, long Length, long WriteTicks);

		private readonly Dictionary<ClipKey, AudioClipData> decoded = [];
		private readonly Queue<ClipKey> decodedOrder = new ();
		private long decodedBytes;

		/// <summary>Held decodes are capped well below one clip's own ceiling, since this is a convenience.</summary>
		private const long MaxDecodedCacheBytes = 64L * 1024 * 1024;

		/// <summary>
		/// The clip a path decodes to, reused across calls. <c>Audio.Play(path)</c> in a loop would otherwise
		/// re-read and re-decode the file every time and mint a fresh clip identity, which also defeated the
		/// output's prepared cache and grew it until playing anything at all failed.
		/// </summary>
		internal bool TryGetDecoded(string fullPath, out AudioClipData clip)
		{
			clip = null;

			try
			{
				var info = new FileInfo(fullPath);

				if (!info.Exists)
					return false;

				var key = new ClipKey(fullPath, info.Length, info.LastWriteTimeUtc.Ticks);

				lock (gate)
					return decoded.TryGetValue(key, out clip);
			}
			catch (Exception)
			{
				return false;
			}
		}

		internal void StoreDecoded(string fullPath, AudioClipData clip)
		{
			if (clip == null)
				return;

			try
			{
				var info = new FileInfo(fullPath);

				if (!info.Exists)
					return;

				var key = new ClipKey(fullPath, info.Length, info.LastWriteTimeUtc.Ticks);
				var bytes = clip.Samples.LongLength * 4L;

				if (bytes > MaxDecodedCacheBytes)
					return;                     // one clip larger than the whole budget is simply not held

				lock (gate)
				{
					if (!decoded.TryAdd(key, clip))
						return;

					decodedOrder.Enqueue(key);
					decodedBytes += bytes;

					while (decodedBytes > MaxDecodedCacheBytes && decodedOrder.Count > 1)
					{
						var oldest = decodedOrder.Dequeue();

						if (decoded.Remove(oldest, out var evicted))
							decodedBytes -= evicted.Samples.LongLength * 4L;
					}
				}
			}
			catch (Exception)
			{
				//A path that cannot be stat'd is simply not cached; the caller already has its clip.
			}
		}

		// ---- convenience output ---------------------------------------------------------------

		private readonly Dictionary<string, AudioOutputCore> convenience = [];

		/// <summary>
		/// The hidden output behind static <c>Audio.Play</c>, one per binding: blank for default-following and one
		/// per explicit device id. It is never exposed to script, so nothing can close it out from under a playback
		/// that is still running, and it counts against the same open-output cap as an explicit output.
		/// </summary>
		internal AudioOutputCore GetConvenienceOutput(string deviceId, out string error)
		{
			error = null;
			var key = deviceId ?? "";
			AudioOutputCore reopen = null;

			lock (gate)
			{
				if (disposed)
				{
					error = "This script is shutting down.";
					return null;
				}

				if (convenience.TryGetValue(key, out var existing) && existing.Status != AudioOutputStatus.Disposed)
				{
					if (existing.Status == AudioOutputStatus.Open)
						return existing;

					reopen = existing;
				}
			}

			// Opening touches the device, so it happens outside the gate: a backend that takes seconds to answer
			// would otherwise stall every other output registration and the service's own teardown for that long.
			if (reopen != null)
				return reopen.TryOpen(out error) ? reopen : null;

			var core = new AudioOutputCore(Backend, key, 16, AudioMixer.PolicyOldest, 20);

			if (!core.TryOpen(out error))
			{
				core.Dispose();
				return null;
			}

			lock (gate)
			{
				if (disposed)
				{
					core.Dispose();
					error = "This script is shutting down.";
					return null;
				}

				if (convenience.TryGetValue(key, out var winner) && winner.Status == AudioOutputStatus.Open)
				{
					core.Dispose();
					return winner;
				}

				convenience[key] = core;

				if (!outputs.Contains(core))
				{
					if (outputs.Count >= MaxOpenOutputs)
					{
						_ = convenience.Remove(key);
						core.Dispose();
						error = $"This script already has {MaxOpenOutputs} audio outputs open. Close one before playing.";
						return null;
					}

					outputs.Add(core);
				}

				core.EnableIdleSuspension();
			}

			return core;
		}

		public void Dispose()
		{
			AudioOutputCore[] toClose;
			IDisposable[] liveResources;
			IAudioBackend toDispose;

			lock (gate)
			{
				if (disposed)
					return;

				disposed = true;
				toClose = [.. outputs];
				outputs.Clear();
				liveResources = [.. recorders, .. meters];
				recorders.Clear();
				meters.Clear();
				toDispose = backend;
				backend = null;
			}

			// Captures and meters first: each holds a native stream of its own that nothing else in teardown reaches.
			foreach (var resource in liveResources)
			{
				try
				{
					resource.Dispose();
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"Audio resource teardown failed: {ex.Message}");
				}
			}

			// Outputs first: each one stops its native stream, so the backend is quiet before it is torn down.
			foreach (var core in toClose)
			{
				try
				{
					core.Dispose();
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"Audio output teardown failed: {ex.Message}");
				}
			}

			try
			{
				toDispose?.Dispose();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Audio backend teardown failed: {ex.Message}");
			}
		}
	}
}
