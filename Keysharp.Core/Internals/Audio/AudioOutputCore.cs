namespace Keysharp.Internals.Audio
{
	/// <summary>The states an output moves through. Script sees these exact spellings.</summary>
	internal static class AudioOutputStatus
	{
		internal const string Closed = "Closed";
		internal const string Opening = "Opening";
		internal const string Open = "Open";
		internal const string Transitioning = "Transitioning";
		internal const string Unavailable = "Unavailable";
		internal const string Disposed = "Disposed";
	}

	/// <summary>
	/// One output's engine state: the mixer, the single native stream it owns while open, and the prepared-clip
	/// cache converted to whatever format that stream negotiated. The script-facing wrapper holds one of these and
	/// adds nothing but argument checking, so closing, reopening and device loss are decided in exactly one place.
	/// <para>
	/// A format generation is the unit of consistency. Opening publishes one; closing retires it; a prepared cache
	/// belongs to the generation that converted it. Nothing from a retired generation can play, which is what keeps
	/// a device change from replaying stale audio at the wrong rate.
	/// </para>
	/// </summary>
	internal sealed class AudioOutputCore : IDisposable
	{
		// Either an explicit backend (the convenience path and tests already hold one) or the service to resolve
		// one from on first use, so constructing an output touches nothing native — which is what __New promises.
		private readonly IAudioBackend explicitBackend;
		private readonly AudioService owner;
		private readonly Lock gate = new();

		// The clips the script asked to prepare, kept so a format change can rebuild every cache without the
		// script re-preparing. Keyed by identity: two clips with equal samples are still two clips.
		private readonly Dictionary<AudioClipData, byte> preparedClips = [];
		private readonly Dictionary<AudioClipData, float[]> cache = [];

		private readonly ConditionalWeakTable<AudioClipData, float[]> adHocCache = new();
		private System.Threading.Timer idleTimer;
		private long idleSince;
		private bool suspended;

		private AudioMixer mixer;
		private IAudioOutputStream stream;
		private string status = AudioOutputStatus.Closed;
		private long openVersion;

		/// <summary>The backend to work through, resolved on first use when this output was built from a service.</summary>
		private IAudioBackend backend => explicitBackend ?? owner?.Backend;
		private long cacheBytes;
		private float pendingVolume = 1f;
		private bool pendingMute;
		private string pendingPolicy;

		internal AudioOutputCore(AudioService service, string deviceId, int voiceLimit, string voicePolicy, double requestedLatencyMs)
			: this(deviceId, voiceLimit, voicePolicy, requestedLatencyMs) => owner = service;

		internal AudioOutputCore(IAudioBackend backend, string deviceId, int voiceLimit, string voicePolicy, double requestedLatencyMs)
			: this(deviceId, voiceLimit, voicePolicy, requestedLatencyMs) => explicitBackend = backend;

		private AudioOutputCore(string deviceId, int voiceLimit, string voicePolicy, double requestedLatencyMs)
		{
			DeviceId = deviceId ?? "";
			VoiceLimit = voiceLimit;
			pendingPolicy = voicePolicy;
			RequestedLatencyMilliseconds = requestedLatencyMs;
		}

		/// <summary>The selector this output was created with. Blank follows the current default.</summary>
		internal string DeviceId { get; private set; }

		/// <summary>The device actually bound at the last successful open, or blank while never opened.</summary>
		internal string BoundDeviceId { get; private set; } = "";

		internal int VoiceLimit { get; }

		internal double RequestedLatencyMilliseconds { get; }

		internal bool IsFollowingDefault => DeviceId.Length == 0;

		internal string Status
		{
			get
			{
				ObserveDeviceLoss();

				lock (gate)
					return status;
			}
		}

		/// <summary>
		/// Bus gain, mute and the voice policy survive a close, so a value set while the output is closed is the
		/// value the next open starts from. The live mixer and the surviving value move together here rather than
		/// in the script wrapper, so there is one place that knows the rule.
		/// </summary>
		internal float OutputVolume
		{
			get { lock (gate) return mixer?.OutputVolume ?? pendingVolume; }

			set
			{
				lock (gate)
				{
					pendingVolume = value;

					if (mixer is { } m)
						m.OutputVolume = value;
				}
			}
		}

		internal bool OutputMuted
		{
			get { lock (gate) return mixer?.OutputMuted ?? pendingMute; }

			set
			{
				lock (gate)
				{
					pendingMute = value;

					if (mixer is { } m)
						m.OutputMuted = value;
				}
			}
		}

		internal string VoicePolicy
		{
			get { lock (gate) return pendingPolicy; }

			set
			{
				lock (gate)
				{
					pendingPolicy = value;

					if (mixer is { } m)
						m.VoicePolicy = value;
				}
			}
		}

		/// <summary>Mixer counters, so the wrapper never reaches through the output into the mixer.</summary>
		internal int ActiveVoiceCount
		{
			get { lock (gate) return mixer?.ActiveVoiceCount ?? 0; }
		}

		internal long DroppedPlayCount
		{
			get { lock (gate) return mixer?.DroppedPlayCount ?? 0L; }
		}

		internal long UnderrunCount
		{
			get { lock (gate) return mixer?.UnderrunCount ?? 0L; }
		}

		internal object PeakLevel
		{
			get { lock (gate) return mixer == null ? null : (object)mixer.Peak; }
		}

		internal string Error { get; private set; }

		internal AudioMixer Mixer
		{
			get { lock (gate) return mixer; }
		}

		internal int SampleRate
		{
			get { lock (gate) return stream?.Format.SampleRate ?? 0; }
		}

		internal int Channels
		{
			get { lock (gate) return stream?.Format.Channels ?? 0; }
		}

		internal double MeasuredLatencyMilliseconds
		{
			get { lock (gate) return stream?.LatencyMilliseconds ?? 0; }
		}

		/// <summary>
		/// Whether the configured binding currently resolves to a usable device. Deliberately not an alias for
		/// "open": a manually closed output that would open successfully still reports true.
		/// </summary>
		internal bool IsAvailable
		{
			get
			{
				ObserveDeviceLoss();

				lock (gate)
				{
					if (status == AudioOutputStatus.Disposed || status == AudioOutputStatus.Transitioning)
						return false;
				}

				var b = backend;

				if (b == null || !b.Supports(AudioCapability.Playback))
					return false;

				return DeviceId.Length == 0
					   ? b.TryGetDefaultDevice(AudioDeviceKind.Output, out _)
					   : b.TryGetDevice(DeviceId, out var d) && d.Kind == AudioDeviceKind.Output;
			}
		}

		/// <summary>
		/// Opens the native stream and publishes a format generation. Returns false only for device absence or a
		/// transient open failure, with <see cref="Error"/> set; anything the caller must not tolerate throws
		/// from the wrapper instead.
		/// </summary>
		internal bool TryOpen(out string error)
		{
			long version;

			lock (gate)
			{
				if (status == AudioOutputStatus.Disposed)
				{
					error = "This output has been disposed.";
					return false;
				}

				if (status == AudioOutputStatus.Open)
				{
					error = null;
					return true;
				}

				// Opening happens outside this lock, so the state itself is the claim: a second caller that finds
				// an open already in flight refuses instead of opening a second native stream and leaking one.
				if (status == AudioOutputStatus.Opening)
				{
					error = "This output is already being opened.";
					return false;
				}

				status = AudioOutputStatus.Opening;
				version = ++openVersion;
			}

			// The mixer cannot exist until the backend has negotiated a channel count, but the backend needs a
			// render source at open time. The adapter closes that circle and answers with silence in the window
			// between the stream existing and the mixer being installed.
			var opened = default(IAudioOutputStream);

			try
			{
				return TryOpenCore(version, out opened, out error);
			}
			finally
			{
				// An exception between claiming Opening and publishing Open would otherwise leave the output
				// answering "already being opened" for the rest of its life.
				lock (gate)
					if (openVersion == version && status == AudioOutputStatus.Opening)
						status = AudioOutputStatus.Closed;
			}
		}

		private bool TryOpenCore(long version, out IAudioOutputStream opened, out string error)
		{
			var request = new AudioOutputRequest(DeviceId, RequestedLatencyMilliseconds);
			var adapter = new DeferredRenderSource();

			var b = backend;

			if (b == null)
			{
				opened = null;
				error = "This platform has no audio backend.";
				return false;
			}

			if (!b.TryOpenOutput(request, adapter, out opened, out error))
			{
				lock (gate)
				{
					if (openVersion == version && status == AudioOutputStatus.Opening)
					{
						status = AudioOutputStatus.Unavailable;
						Error = error;
					}
				}

				return false;
			}

			if (!AudioFormats.IsValidChannels(opened.Format.Channels) || opened.Format.SampleRate <= 0)
			{
				error = $"The audio device negotiated {opened.Format.Channels} channels at {opened.Format.SampleRate} Hz, which this output cannot render.";
				opened.Dispose();

				lock (gate)
				{
					if (openVersion == version && status == AudioOutputStatus.Opening)
					{
						status = AudioOutputStatus.Unavailable;
						Error = error;
					}
				}

				return false;
			}

			var bound = new AudioMixer(opened.Format.Channels, VoiceLimit)
			{
				VoicePolicy = pendingPolicy,
				OutputVolume = pendingVolume,
				OutputMuted = pendingMute,
			};
			adapter.Install(bound);

			lock (gate)
			{
				if (openVersion != version || status != AudioOutputStatus.Opening)
				{
					opened.Dispose();
					error = "The output was closed while it was opening.";
					return false;
				}

				mixer = bound;
				stream = opened;
				status = AudioOutputStatus.Open;
				Error = null;
				BoundDeviceId = ResolveBoundId();
				RebuildCacheLocked();
				opened.Start();
				suspended = false;
				idleSince = Environment.TickCount64;
			}

			error = null;
			return true;
		}

		/// <summary>
		/// Forwards render callbacks to a mixer that is created only after the stream reports its negotiated
		/// format. Until then it writes silence, so a backend that calls back during Initialize is safe.
		/// </summary>
		private sealed class DeferredRenderSource : IAudioRenderSource
		{
			private AudioMixer target;

			internal void Install(AudioMixer m) => Volatile.Write(ref target, m);

			public void Fill(Span<float> interleavedFrames)
			{
				var m = Volatile.Read(ref target);

				if (m == null)
					interleavedFrames.Clear();
				else
					m.Fill(interleavedFrames);
			}

			public void NoteUnderrun() => Volatile.Read(ref target)?.NoteUnderrun();
		}

		private string ResolveBoundId()
			=> DeviceId.Length > 0
			   ? DeviceId
			   : backend is { } rb && rb.TryGetDefaultDevice(AudioDeviceKind.Output, out var d) ? d.Id : "";

		/// <summary>Converts one clip into the current generation's format and registers it for later rebuilds.</summary>
		internal bool TryPrepare(AudioClipData clip, out string error)
		{
			lock (gate)
			{
				preparedClips[clip] = 0;

				if (status != AudioOutputStatus.Open || stream == null)
				{
					// Registered but not converted: the next successful open builds it with everything else.
					error = null;
					return true;
				}

				return TryCacheLocked(clip, out error);
			}
		}

		private bool TryCacheLocked(AudioClipData clip, out string error)
		{
			error = null;

			if (cache.ContainsKey(clip))
				return true;

			// Projected before the conversion runs, not after: upsampling 8 kHz to 192 kHz is a 24x blowup, so
			// preparing first would allocate gigabytes only to reject them.
			var targetRate = stream.Format.SampleRate;
			var targetChannels = stream.Format.Channels;
			var projected = clip.SampleRate > 0
							? (long)(clip.FrameCount * (double)targetRate / clip.SampleRate) * targetChannels * 4L
							: 0L;

			var pinned = preparedClips.ContainsKey(clip);

			if ((pinned ? cacheBytes : 0) + projected > AudioFormats.MaxClipBytes)
			{
				error = $"Preparing this clip would exceed the {AudioFormats.MaxClipBytes / (1024 * 1024)} MiB prepared-cache budget for one output.";
				return false;
			}

			var prepared = clip.PrepareFor(targetRate, targetChannels);
			var bytes = prepared.LongLength * 4L;

			if (pinned)
			{
				cache[clip] = prepared;
				cacheBytes += bytes;
				adHocCache.Remove(clip);
			}
			else
				adHocCache.Add(clip, prepared);

			return true;
		}

		internal void EnableIdleSuspension()
		{
			lock (gate)
			{
				idleSince = Environment.TickCount64;
				idleTimer ??= new System.Threading.Timer(static state => ((AudioOutputCore)state).SuspendIfIdle(), this, 1000, 1000);
			}
		}

		private void SuspendIfIdle()
		{
			lock (gate)
			{
				if (status != AudioOutputStatus.Open || stream == null || suspended)
					return;

				if (mixer?.IsIdle != true)
					idleSince = Environment.TickCount64;
				else if (Environment.TickCount64 - idleSince >= 2000)
				{
					try
					{
						stream.Stop();
						suspended = true;
					}
					catch (Exception ex)
					{
						Diagnostics.Debug.WriteLine($"Audio idle suspension failed: {ex.Message}");
					}
				}
			}
		}

		private void RebuildCacheLocked()
		{
			cache.Clear();
			adHocCache.Clear();
			cacheBytes = 0;

			foreach (var clip in preparedClips.Keys)
				_ = TryCacheLocked(clip, out _);
		}

		internal bool IsPrepared(AudioClipData clip)
		{
			lock (gate)
				return status == AudioOutputStatus.Open && (cache.ContainsKey(clip) || adHocCache.TryGetValue(clip, out _));
		}

		/// <summary>
		/// Admits one play. The start offset arrives in milliseconds and is converted here, because the frames a
		/// voice is indexed by are the prepared buffer's — resampled to the stream's rate — not the clip's, and a
		/// caller converting with the clip's rate would seek to the wrong place on any device that resamples.
		/// Returns null for every bounded rejection the contract reports as blank: not open, transitioning, a full
		/// command ring, or a policy refusal.
		/// </summary>
		internal AudioPlaybackControl TryPlay(AudioClipData clip, float volume, float pan, bool loop, double startMilliseconds, out string error)
		{
			ObserveDeviceLoss();
			error = null;
			AudioMixer m;
			float[] prepared;
			int rate;

			lock (gate)
			{
				if (status != AudioOutputStatus.Open || stream == null || mixer == null)
					return null;

				if (!cache.TryGetValue(clip, out prepared) && !adHocCache.TryGetValue(clip, out prepared))
				{
					// An unprepared clip is converted here rather than refused; a caller that needs the bounded
					// path calls Prepare during setup, exactly as the design's hot path requires.
					if (!TryCacheLocked(clip, out error))
						return null;

					if (!cache.TryGetValue(clip, out prepared))
						adHocCache.TryGetValue(clip, out prepared);
				}

				m = mixer;
				rate = stream.Format.SampleRate;

				var startFrame = rate > 0 ? (long)(startMilliseconds * rate / 1000.0) : 0L;

				var control = new AudioPlaybackControl
				{
					Prepared = prepared,
				};
				_ = control.SetVolume(volume);
				_ = control.SetPan(pan);
				_ = control.SetLoop(loop);

				if (suspended)
				{
					stream.Start();
					suspended = false;
				}

				idleSince = Environment.TickCount64;

				if (!m.TrySubmit(control, startFrame))
					return null;

				return control;
			}
		}

		internal void StopAll()
		{
			AudioMixer m;

			lock (gate)
				m = mixer;

			m?.StopAll();
		}

		/// <summary>Reversible: retires the native generation but keeps the registered clip set for a later open.</summary>
		internal void Close() => Close(false);

		private void Close(bool disposing)
		{
			IAudioOutputStream toDispose;
			AudioMixer toStop;

			lock (gate)
			{
				if (status == AudioOutputStatus.Disposed || (!disposing && status == AudioOutputStatus.Closed))
					return;

				if (disposing)
				{
					preparedClips.Clear();
					idleTimer?.Dispose();
					idleTimer = null;
				}

				toDispose = stream;
				toStop = mixer;
				stream = null;
				mixer = null;
				cache.Clear();
				adHocCache.Clear();
				cacheBytes = 0;
				status = disposing ? AudioOutputStatus.Disposed : AudioOutputStatus.Closed;
				openVersion++;
			}

			// The stream is stopped and released first: disposing it is what quiesces the render callback, and
			// ending the voices while one is still running would race the sweep against a concurrent admission.
			try
			{
				try { toDispose?.Stop(); }
				finally { toDispose?.Dispose(); }
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Audio output close failed: {ex.Message}");
			}

			toStop?.FinishAll(AudioPlaybackState.Stopped);
		}

		/// <summary>
		/// Promotes a stream the backend marked lost into the Unavailable transition. A backend raises that flag
		/// from its own render thread and signals nothing, so it is observed on the paths a script already reads,
		/// which costs one volatile read while the device is fine.
		/// </summary>
		private void ObserveDeviceLoss()
		{
			IAudioOutputStream current;

			lock (gate)
			{
				if (status != AudioOutputStatus.Open)
					return;

				current = stream;
			}

			if (current is { IsDeviceLost: true })
				NoteDeviceLost(current);
		}

		/// <summary>Ends every voice and retires the output once the bound device has gone away under it.</summary>
		private void NoteDeviceLost(IAudioOutputStream lostStream)
		{
			AudioMixer toStop;
			IAudioOutputStream toDispose;

			lock (gate)
			{
				if (status != AudioOutputStatus.Open || !ReferenceEquals(stream, lostStream))
					return;

				toStop = mixer;
				toDispose = stream;
				stream = null;
				mixer = null;
				status = AudioOutputStatus.Unavailable;
				Error = "The audio device this output was using is no longer available.";
			}

			// Released before the voices are ended, for the reason Close gives: the sweep must not run against a
			// render callback that is still admitting commands.
			try
			{
				try { toDispose?.Stop(); }
				finally { toDispose?.Dispose(); }
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"Audio output release after device loss failed: {ex.Message}");
			}

			toStop?.FinishAll(AudioPlaybackState.DeviceLost);
		}

		public void Dispose() => Close(true);
	}
}
