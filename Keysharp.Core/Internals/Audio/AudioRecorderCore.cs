namespace Keysharp.Internals.Audio
{
	/// <summary>The states a recorder moves through. Script sees these exact spellings.</summary>
	internal static class AudioRecorderStatus
	{
		internal const string Ready = "Ready";
		internal const string Opening = "Opening";
		internal const string Recording = "Recording";
		internal const string Paused = "Paused";
		internal const string Finalizing = "Finalizing";
		internal const string Stopped = "Stopped";
		internal const string DeviceLost = "DeviceLost";
		internal const string Error = "Error";
		internal const string Disposed = "Disposed";
	}

	/// <summary>
	/// One capture in progress. It owns the native input stream, the sink that receives frames on the backend's
	/// capture thread, and whichever destination the caller asked for: a WAV file written as frames arrive, or an
	/// in-memory clip bounded by a hard ceiling.
	/// <para>
	/// The capture callback only measures and publishes. Everything that could block it — format conversion, the
	/// file write, the limit checks, finalization — happens on this recorder's own writer thread, which is the
	/// only thread that touches the destination. The two share a single-producer single-consumer ring, so the
	/// capture thread takes no lock a script thread can hold and never waits on the disk.
	/// </para>
	/// <para>
	/// A recorder is single-use once capture is admitted. Everything that can end it — a manual stop, the
	/// requested maximum, the memory ceiling, device loss, a write failure, teardown — competes for one terminal
	/// transition, so exactly one reason is reported and the result is published exactly once.
	/// </para>
	/// </summary>
	internal sealed class AudioRecorderCore : IAudioCaptureSink, IDisposable
	{
		/// <summary>Ten minutes or 256 MiB, whichever comes first, for a recording with no file behind it.</summary>
		internal const long MaxMemoryBytes = 256L * 1024 * 1024;
		internal const long MaxMemoryMilliseconds = 10 * 60 * 1000;

		/// <summary>How long a stop waits for the writer to publish before reporting what it has.</summary>
		private const int FinalizeTimeoutMs = 5000;

		/// <summary>
		/// Idle poll period for the writer. The capture thread signals nothing, because signalling a waiter is
		/// the one remaining thing on that thread that could take a lock.
		/// </summary>

		private readonly AudioService service;

		/// <summary>Guards the destination and the counters. Taken by the writer thread and by script threads,
		/// never by the capture thread.</summary>
		private readonly Lock gate = new();

		private readonly List<float[]> memoryChunks = [];
		private readonly string path;
		private readonly string sampleFormat;
		private readonly int bytesPerSample;
		private readonly long maximumMs;
		private readonly double chunkMs;

		// ---- capture ring: single producer (the capture thread), single consumer (the writer thread) ----
		private readonly float[][] slots;
		private readonly int[] slotLengths;
		private readonly int slotMask;
		private long slotWrite;
		private long slotRead;
		private long overruns;

		private IAudioInputStream stream;
		private FileStream file;
		private Thread writer;
		private ManualResetEventSlim finished;
		private long framesWritten;
		private long dataBytes;
		private long memorySamples;
		private int terminal;                 // 0 while live, 1 once a terminal winner is chosen
		private int accepting;                // the capture thread's whole view of whether to publish
		private int capturePaused;            // read by the capture thread, so paused frames never enter the ring
		private string status = AudioRecorderStatus.Ready;
		private string pendingStatus;
		private string error;
		private byte[] scratch = [];
		private float peak = -1f;

		internal AudioRecorderCore(AudioService service, AudioCaptureSource source, string path, string deviceId,
								   int sampleRate, int channels, string sampleFormat, long maximumMs, double chunkMs)
		{
			this.service = service;
			this.path = path ?? "";
			this.sampleFormat = sampleFormat;
			this.maximumMs = maximumMs;
			this.chunkMs = chunkMs;
			Source = source;
			DeviceId = deviceId ?? "";
			SampleRate = sampleRate;
			Channels = channels;
			_ = AudioFormats.TryResolve(sampleFormat, out _, out bytesPerSample);

			// Sized from the chunk the caller asked for, with headroom for a backend that hands over more at once,
			// and deep enough to absorb about a second of disk stall before a frame is dropped.
			var clamped = Math.Clamp(chunkMs, 10.0, 1000.0);
			var perSlot = Math.Max(4096, (int)(clamped / 1000.0 * sampleRate) * Math.Max(1, channels) * 2);
			var depth = RoundUpPow2(Math.Max(8, (int)(1000.0 / clamped) + 4));
			slots = new float[depth][];
			slotLengths = new int[depth];
			slotMask = depth - 1;

			for (var i = 0; i < depth; i++)
				slots[i] = new float[perSlot];
		}

		internal AudioCaptureSource Source { get; }
		internal string DeviceId { get; private set; }
		internal int SampleRate { get; private set; }
		internal int Channels { get; private set; }
		internal string SampleFormat => sampleFormat;
		internal string Path => path;

		internal string Status
		{
			get
			{
				ObserveDeviceLoss();

				lock (gate)
					return status;
			}
		}

		internal string Error
		{
			get { lock (gate) return error; }
		}

		/// <summary>How many captured chunks were dropped because the writer could not keep up.</summary>
		internal long OverrunCount => Interlocked.Read(ref overruns);

		/// <summary>The loudest sample observed so far as a linear scalar, or negative before any frame arrived,
		/// which is what keeps silence distinct from no data. The 0-100 boundary conversion belongs to the wrapper.</summary>
		internal double Peak => Volatile.Read(ref peak);

		internal double DurationMilliseconds
		{
			get { lock (gate) return SampleRate <= 0 ? 0 : framesWritten * 1000.0 / SampleRate; }
		}

		internal long FrameCount
		{
			get { lock (gate) return framesWritten; }
		}

		/// <summary>The finished recording once one exists: an in-memory clip, or blank when the sink was a file.</summary>
		internal AudioClipData ResultClip { get; private set; }

		internal bool HasResult { get; private set; }

		/// <summary>
		/// Opens the device and the destination. The device comes first so a permission or hardware failure
		/// leaves no empty file behind, and the file is created only once capture is certain to start.
		/// </summary>
		internal bool TryStart(out string failure)
		{
			failure = null;
			var capture = service.Backend;
			var want = Source == AudioCaptureSource.Microphone ? AudioCapability.MicrophoneCapture : AudioCapability.SystemAudioCapture;

			if (capture == null || !service.Supports(want))
			{
				failure = service.UnsupportedReason(want);
				return false;
			}

			lock (gate)
			{
				if (status != AudioRecorderStatus.Ready)
				{
					failure = "This recorder has already been started.";
					return false;
				}

				status = AudioRecorderStatus.Opening;
			}

			var request = new AudioInputRequest(DeviceId, Source, SampleRate, Channels, chunkMs);

			if (!capture.TryOpenInput(request, this, out var opened, out failure))
			{
				lock (gate)
					status = AudioRecorderStatus.Ready;   // a pre-admission failure is retryable

				return false;
			}

			// The negotiated format is what actually arrives, so the header and the result describe that rather
			// than what was asked for.
			SampleRate = opened.Format.SampleRate;
			Channels = opened.Format.Channels;

			if (path.Length > 0 && !TryOpenFile(out failure))
			{
				opened.Dispose();

				lock (gate)
					status = AudioRecorderStatus.Ready;

				return false;
			}

			finished = new ManualResetEventSlim(false);
			writer = new Thread(WriterLoop)
			{
				IsBackground = true,
				Name = "Keysharp audio recorder",
			};

			lock (gate)
			{
				stream = opened;
				status = AudioRecorderStatus.Recording;
			}

			writer.Start();
			// Published last: the capture thread does nothing until the destination and the writer both exist.
			Volatile.Write(ref accepting, 1);
			opened.Start();
			return true;
		}

		private bool TryOpenFile(out string failure)
		{
			failure = null;

			try
			{
				var directory = System.IO.Path.GetDirectoryName(path);

				if (!directory.IsNullOrEmpty() && !Directory.Exists(directory))
				{
					failure = $"The folder {directory} does not exist.";
					return false;
				}

				file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
				// A placeholder header keeps the file parseable if the process dies mid-recording.
				file.Write(WavCodec.Header(SampleRate, Channels, bytesPerSample * 8, sampleFormat == AudioFormats.Float32, 0));
				return true;
			}
			catch (Exception ex)
			{
				// A half-opened file must not be left behind for a retry to overwrite blindly.
				try
				{
					file?.Dispose();
				}
				catch (Exception)
				{
				}

				file = null;
				failure = ex.Message;
				return false;
			}
		}

		// ---- capture thread ------------------------------------------------------------------

		/// <summary>
		/// Receives frames on the backend's capture thread. Measures the peak, copies the frames into the ring and
		/// returns: no lock, no allocation, no I/O, and nothing that can block on another thread. A ring the writer
		/// has not kept up with drops the chunk and counts it, which is the only bounded answer available here.
		/// </summary>
		public void Write(ReadOnlySpan<float> interleavedFrames)
		{
			if (Volatile.Read(ref accepting) == 0 || Volatile.Read(ref capturePaused) != 0 || interleavedFrames.Length == 0)
				return;

			var localPeak = 0f;

			for (var i = 0; i < interleavedFrames.Length; i++)
			{
				var a = interleavedFrames[i];
				a = a < 0 ? -a : a;

				if (a > localPeak)
					localPeak = a;
			}

			if (localPeak > Volatile.Read(ref peak))
				Volatile.Write(ref peak, localPeak);

			var offset = 0;

			// A callback larger than one slot is split rather than dropped, which keeps the slot size a tuning
			// choice instead of a correctness one.
			while (offset < interleavedFrames.Length)
			{
				if (slotWrite - Volatile.Read(ref slotRead) >= slots.Length)
				{
					_ = Interlocked.Increment(ref overruns);
					return;
				}

				var index = (int)(slotWrite & slotMask);
				var slot = slots[index];
				var take = Math.Min(slot.Length, interleavedFrames.Length - offset);
				interleavedFrames.Slice(offset, take).CopyTo(slot);
				slotLengths[index] = take;
				// Release: the frames land before the writer can observe the position that publishes them.
				Volatile.Write(ref slotWrite, slotWrite + 1);
				offset += take;
			}
		}

		// ---- writer thread -------------------------------------------------------------------

		/// <summary>
		/// Owns the destination for the whole life of the recording. It is the only thread that converts, writes,
		/// accounts or finalizes, so none of that can stall the capture callback.
		/// </summary>
		private void WriterLoop()
		{
			try
			{
				while (Volatile.Read(ref terminal) == 0)
					if (!DrainOnce())
						Thread.Sleep((int)Math.Clamp(chunkMs / 2, 5, 50));

				// Whoever claimed the terminal transition left the already-captured frames in the ring; they are
				// part of the recording and are written before the destination is closed.
				while (DrainOnce())
				{
				}
			}
			catch (Exception ex)
			{
				lock (gate)
					error ??= ex.Message;
			}
			finally
			{
				Publish();
				finished.Set();
			}
		}

		/// <summary>Consumes one published chunk. False when the ring is empty.</summary>
		private bool DrainOnce()
		{
			if (Volatile.Read(ref slotWrite) == slotRead)
				return false;

			var index = (int)(slotRead & slotMask);
			var length = slotLengths[index];

			lock (gate)
			{
				// Finalizing counts: the frames already in the ring when the stop was claimed are part of the
				// recording, and refusing them here would silently drop its tail. Paused frames never arrive,
				// because the capture thread stops publishing rather than the writer discarding.
				if (length > 0 && (status == AudioRecorderStatus.Recording || status == AudioRecorderStatus.Finalizing))
					Consume(slots[index].AsSpan(0, length));
			}

			// Released only after the frames have been consumed, so the producer cannot overwrite them.
			Volatile.Write(ref slotRead, slotRead + 1);
			return true;
		}

		/// <summary>Writes one chunk to the destination and applies whichever ceiling ends the recording.</summary>
		private void Consume(ReadOnlySpan<float> frames)
		{
			var frameCount = Channels > 0 ? frames.Length / Channels : 0;

			if (frameCount == 0)
				return;

			try
			{
				if (file != null)
				{
					var needed = frames.Length * bytesPerSample;

					if (scratch.Length < needed)
						scratch = new byte[needed];

					WavCodec.FromFloat(frames, sampleFormat, scratch.AsSpan(0, needed));
					file.Write(scratch, 0, needed);
					dataBytes += needed;
				}
				else
				{
					// A memory recording is bounded, because nothing else would ever stop it. Chunks are kept
					// whole and joined once at the end, so no growth step ever copies the whole recording.
					if ((memorySamples + frames.Length) * 4L > MaxMemoryBytes)
					{
						ClaimTerminal(AudioRecorderStatus.Stopped, null);
						return;
					}

					memoryChunks.Add(frames.ToArray());
					memorySamples += frames.Length;
				}

				framesWritten += frameCount;
			}
			catch (Exception ex)
			{
				ClaimTerminal(AudioRecorderStatus.Error, ex.Message);
				return;
			}

			var elapsed = SampleRate > 0 ? framesWritten * 1000.0 / SampleRate : 0;

			if (maximumMs > 0 && elapsed >= maximumMs)
				ClaimTerminal(AudioRecorderStatus.Stopped, null);
			else if (file == null && elapsed >= MaxMemoryMilliseconds)
				ClaimTerminal(AudioRecorderStatus.Stopped, null);
		}

		// ---- lifecycle -----------------------------------------------------------------------

		internal bool Pause(bool value)
		{
			lock (gate)
			{
				// A recorder that is not running cannot change its pause state, and says so by reporting the state
				// it is actually in rather than a bare false that resume-succeeded also returns.
				if (status != AudioRecorderStatus.Recording && status != AudioRecorderStatus.Paused)
					return status == AudioRecorderStatus.Paused;

				status = value ? AudioRecorderStatus.Paused : AudioRecorderStatus.Recording;
				Volatile.Write(ref capturePaused, value ? 1 : 0);
				return value;
			}
		}

		/// <summary>Ends the recording and publishes its result. Idempotent: a later call returns the same outcome.</summary>
		internal void Stop() => Finish(AudioRecorderStatus.Stopped, null);

		private void ObserveDeviceLoss()
		{
			IAudioInputStream current;

			lock (gate)
			{
				if (status != AudioRecorderStatus.Recording && status != AudioRecorderStatus.Paused)
					return;

				current = stream;
			}

			if (current is { IsDeviceLost: true })
				Finish(AudioRecorderStatus.DeviceLost, null);
		}

		/// <summary>
		/// Requests the single terminal transition from a script thread and waits for the writer to publish, so a
		/// caller that asked to stop can read what it produced. A writer that will not finish in time leaves the
		/// result blank rather than blocking the script for good.
		/// </summary>
		private void Finish(string finalStatus, string failure)
		{
			ClaimTerminal(finalStatus, failure);
			var waitFor = finished;

			if (waitFor != null && writer != null && Thread.CurrentThread != writer)
				_ = waitFor.Wait(FinalizeTimeoutMs);
		}

		/// <summary>
		/// Names the outcome. Exactly one caller wins, and the writer thread performs the finalization the winner
		/// asked for whichever thread claimed it.
		/// </summary>
		private void ClaimTerminal(string finalStatus, string failure)
		{
			if (Interlocked.Exchange(ref terminal, 1) != 0)
				return;

			Volatile.Write(ref accepting, 0);

			lock (gate)
			{
				pendingStatus = finalStatus;
				error ??= failure;
				status = AudioRecorderStatus.Finalizing;
			}
		}

		/// <summary>
		/// Closes the destination and publishes the result. Runs on the writer thread only, so the header rewrite
		/// and the in-memory join never happen on a capture callback.
		/// </summary>
		private void Publish()
		{
			IAudioInputStream toStop;

			lock (gate)
			{
				toStop = stream;
				stream = null;
			}

			// Outside the lock: a backend that drains its callback during Stop would otherwise deadlock against
			// the writer's own use of the same lock.
			try
			{
				toStop?.Stop();
				toStop?.Dispose();
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Audio input stop failed: {ex.Message}");
			}

			lock (gate)
			{
				var finalStatus = pendingStatus ?? AudioRecorderStatus.Stopped;

				try
				{
					if (file != null)
					{
						// Rewrite the header now that the true length is known, so the file is a valid WAV.
						file.Flush();
						file.Seek(0, SeekOrigin.Begin);
						file.Write(WavCodec.Header(SampleRate, Channels, bytesPerSample * 8, sampleFormat == AudioFormats.Float32, dataBytes));
						file.Flush();
						file.Dispose();
						file = null;
						HasResult = dataBytes > 0;
					}
					else if (memorySamples > 0)
					{
						ResultClip = new AudioClipData(Join(), SampleRate, Channels, AudioFormats.Float32);
						memoryChunks.Clear();
						memorySamples = 0;
						HasResult = true;
					}
				}
				catch (Exception ex)
				{
					error ??= ex.Message;
					finalStatus = AudioRecorderStatus.Error;
					HasResult = false;
				}

				if (status != AudioRecorderStatus.Disposed)
					status = finalStatus;
			}
		}

		/// <summary>Joins the captured chunks into the one contiguous array a clip needs, allocated exactly once.</summary>
		private float[] Join()
		{
			var joined = new float[memorySamples];
			var offset = 0;

			foreach (var chunk in memoryChunks)
			{
				chunk.CopyTo(joined, offset);
				offset += chunk.Length;
			}

			return joined;
		}

		public void Dispose()
		{
			Finish(AudioRecorderStatus.Stopped, null);

			lock (gate)
				status = AudioRecorderStatus.Disposed;

			// The handle is deliberately not disposed. Stop and Dispose are both public and both wait on it, so
			// releasing it here would throw ObjectDisposedException into a script that called either one twice;
			// it is a managed object with no unmanaged handle to reclaim once the writer has finished.
		}

		private static int RoundUpPow2(int value)
		{
			var v = 1;

			while (v < value)
				v <<= 1;

			return v;
		}
	}
}
