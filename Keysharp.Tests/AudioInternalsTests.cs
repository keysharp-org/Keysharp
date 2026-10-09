using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The device-free half of the audio engine: WAV admission, PCM conversion, clip preparation and the mixer's
	/// voice arbitration. Every case here runs on a host with no sound card, which is what lets audio have any
	/// automated coverage at all — playback into a real endpoint stays a manual probe.
	/// </summary>
	[TestFixture, Category("Internal"), Category("Curated"), NonParallelizable]
	public partial class AudioInternalsTests
	{
		// ---- helpers -------------------------------------------------------------------------

		private static byte[] Wav(short bits, short channels, int rate, byte[] data, ushort tag = 1)
		{
			var blockAlign = channels * (bits / 8);
			using var ms = new MemoryStream();
			using var w = new BinaryWriter(ms);
			w.Write("RIFF"u8);
			w.Write(36 + data.Length);
			w.Write("WAVE"u8);
			w.Write("fmt "u8);
			w.Write(16);
			w.Write((short)tag);
			w.Write(channels);
			w.Write(rate);
			w.Write(rate * blockAlign);
			w.Write((short)blockAlign);
			w.Write(bits);
			w.Write("data"u8);
			w.Write(data.Length);
			w.Write(data);
			w.Flush();
			return ms.ToArray();
		}

		private static AudioPlaybackControl Voice(int frames, int channels, float value = 1f)
		{
			var data = new float[frames * channels];

			for (var i = 0; i < data.Length; i++)
				data[i] = value;

			return new AudioPlaybackControl { Prepared = data };
		}

		// ---- WAV admission -------------------------------------------------------------------

		[Test]
		public void WavRoundTrip()
		{
			// 16-bit mono, the format every synthesized bank uses.
			var pcm = new byte[200];

			for (var i = 0; i < 100; i++)
			{
				var s = (short)(i * 300);
				pcm[i * 2] = (byte)s;
				pcm[(i * 2) + 1] = (byte)(s >> 8);
			}

			Assert.IsTrue(WavCodec.TryDecode(Wav(16, 1, 44100, pcm), out var samples, out var rate, out var channels, out var error), error);
			Assert.AreEqual(44100, rate);
			Assert.AreEqual(1, channels);
			Assert.AreEqual(100, samples.Length);

			foreach (var s in samples)
				Assert.IsTrue(s >= -1f && s <= 1f, "every decoded sample is within -1 through 1");

			// Stereo interleave survives: a left-loud, right-silent image must decode that way round.
			var st = new byte[400];

			for (var i = 0; i < 100; i++)
				st[(i * 4) + 1] = 0x40;

			Assert.IsTrue(WavCodec.TryDecode(Wav(16, 2, 48000, st), out var s2, out _, out var ch2, out _));
			Assert.AreEqual(2, ch2);
			Assert.AreEqual(200, s2.Length);
			Assert.IsTrue(s2[0] > 0.4f, "left carries signal");
			Assert.AreEqual(0f, s2[1], 1e-6, "right is silent");
		}

		[Test]
		public void WavNormalizationIsAsymmetric()
		{
			// Full negative maps to exactly -1 in every signed width; a symmetric divide would leave it short.
			Assert.IsTrue(WavCodec.TryDecode(Wav(16, 1, 8000, [0x00, 0x80]), out var s16, out _, out _, out _));
			Assert.AreEqual(-1f, s16[0], 1e-6);
			Assert.IsTrue(WavCodec.TryDecode(Wav(24, 1, 8000, [0x00, 0x00, 0x80]), out var s24, out _, out _, out _));
			Assert.AreEqual(-1f, s24[0], 1e-6);
			// 8-bit is unsigned around a midpoint of 128.
			Assert.IsTrue(WavCodec.TryDecode(Wav(8, 1, 8000, [128, 255, 0]), out var s8, out _, out _, out _));
			Assert.AreEqual(0f, s8[0], 1e-6);
			Assert.IsTrue(s8[1] > 0.9f);
			Assert.AreEqual(-1f, s8[2], 1e-6);
		}

		[Test]
		public void WavRefusesByName()
		{
			// Each refusal must name its reason: a blank failure reaches the user as silence.
			Assert.IsFalse(WavCodec.TryDecode(Wav(16, 1, 44100, []), out _, out _, out _, out var e1));
			Assert.IsNotEmpty(e1);
			Assert.IsFalse(WavCodec.TryDecode(Wav(16, 3, 44100, new byte[6]), out _, out _, out _, out var e2));
			Assert.IsTrue(e2.Contains("channel"), e2);
			Assert.IsFalse(WavCodec.TryDecode(Wav(16, 1, 4000, new byte[2]), out _, out _, out _, out var e3));
			Assert.IsTrue(e3.Contains("sample rate"), e3);
			Assert.IsFalse(WavCodec.TryDecode(Wav(16, 1, 44100, new byte[4], 2), out _, out _, out _, out var e4));
			Assert.IsTrue(e4.Contains("compressed"), e4);

			var rifx = Wav(16, 1, 44100, new byte[2]);
			"RIFX"u8.CopyTo(rifx);
			Assert.IsFalse(WavCodec.TryDecode(rifx, out _, out _, out _, out var e5));
			Assert.IsTrue(e5.Contains("RIFX"), e5);

			// A data chunk that declares more than the file holds is malformed, not silently truncated.
			var trunc = Wav(16, 1, 44100, new byte[8]);
			Assert.IsFalse(WavCodec.TryDecode(trunc.AsSpan(0, trunc.Length - 4).ToArray(), out _, out _, out _, out var e6));
			Assert.IsNotEmpty(e6);
		}

		[Test]
		public void WavFloatRangeIsEnforced()
		{
			// An out-of-range or non-finite float would propagate through every later gain stage.
			Assert.IsFalse(WavCodec.TryDecode(Wav(32, 1, 44100, BitConverter.GetBytes(2.0f), 3), out _, out _, out _, out _));
			Assert.IsFalse(WavCodec.TryDecode(Wav(32, 1, 44100, BitConverter.GetBytes(float.NaN), 3), out _, out _, out _, out _));
			Assert.IsTrue(WavCodec.TryDecode(Wav(32, 1, 44100, BitConverter.GetBytes(-0.25f), 3), out var ok, out _, out _, out _));
			Assert.AreEqual(-0.25f, ok[0], 1e-6);
		}

		[Test]
		public void WavSkipsUnknownChunksAndConcatenatesData()
		{
			using var ms = new MemoryStream();
			var w = new BinaryWriter(ms);
			w.Write("RIFF"u8);
			w.Write(0);
			w.Write("WAVE"u8);
			// An odd-sized unknown chunk is followed by one pad byte that belongs to no chunk.
			w.Write("LIST"u8);
			w.Write(3);
			w.Write(new byte[] { 1, 2, 3 });
			w.Write((byte)0);
			w.Write("fmt "u8);
			w.Write(16);
			w.Write((short)1);
			w.Write((short)1);
			w.Write(8000);
			w.Write(16000);
			w.Write((short)2);
			w.Write((short)16);
			w.Write("data"u8);
			w.Write(2);
			w.Write(new byte[] { 0x00, 0x40 });
			w.Write("data"u8);
			w.Write(2);
			w.Write(new byte[] { 0x00, 0xC0 });
			w.Flush();
			var bytes = ms.ToArray();
			BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
			Assert.IsTrue(WavCodec.TryDecode(bytes, out var samples, out _, out _, out var error), error);
			Assert.AreEqual(2, samples.Length, "the two data chunks concatenate");
			Assert.IsTrue(samples[0] > 0f && samples[1] < 0f, "and they concatenate in file order");
		}

		// ---- clip preparation ----------------------------------------------------------------

		[Test]
		public void ClipPreparationMapsChannelsAndRates()
		{
			var mono = new AudioClipData(Enumerable.Repeat(0.5f, 100).ToArray(), 8000, 1, AudioFormats.Float32);
			Assert.AreEqual(100, mono.FrameCount);
			Assert.AreEqual(12.5, mono.DurationMilliseconds, 0.001);
			var stereo = mono.PrepareFor(8000, 2);
			Assert.AreEqual(200, stereo.Length);
			Assert.AreEqual(0.5f, stereo[0], 1e-6);
			Assert.AreEqual(0.5f, stereo[1], 1e-6, "mono duplicates into both sides");
			Assert.AreEqual(200, mono.PrepareFor(16000, 1).Length, "doubling the rate doubles the frames");
			Assert.AreEqual(50, mono.PrepareFor(4000, 1).Length, "halving the rate halves the frames");
			var st = new AudioClipData(Enumerable.Repeat(0.5f, 200).ToArray(), 8000, 2, AudioFormats.Float32);
			var mixed = st.PrepareFor(8000, 1);
			Assert.AreEqual(100, mixed.Length);
			Assert.AreEqual(0.5f, mixed[0], 1e-6, "stereo folds to mono by averaging");
		}

		// ---- mixer ---------------------------------------------------------------------------

		[Test]
		public void MixerSumsSaturatesAndEndsVoices()
		{
			var m = new AudioMixer(1, 4);
			var v = Voice(4, 1, 0.5f);
			Assert.IsTrue(m.TrySubmit(v, 0));
			var buf = new float[4];
			m.Fill(buf);
			Assert.AreEqual(0.5f, buf[0], 1e-6);
			// A clip whose last frame lands on the quantum boundary ends now, not a period later.
			Assert.IsTrue(v.IsTerminal);
			Assert.AreEqual(AudioPlaybackState.Ended, v.TerminalState);

			var m2 = new AudioMixer(1, 8);

			for (var i = 0; i < 5; i++)
				_ = m2.TrySubmit(Voice(8, 1, 1f), 0);

			var buf2 = new float[2];
			m2.Fill(buf2);
			Assert.AreEqual(1f, buf2[0], 1e-6, "five full-scale voices saturate rather than wrapping");
			Assert.AreEqual(1f, m2.Peak, 1e-6);
		}

		[Test]
		public void MixerOutputGainAppliesAfterTheSum()
		{
			var m = new AudioMixer(1, 4) { OutputVolume = 0.5f };
			_ = m.TrySubmit(Voice(8, 1, 1f), 0);
			var buf = new float[2];
			m.Fill(buf);
			Assert.AreEqual(0.5f, buf[0], 1e-6);
			m.OutputMuted = true;
			var buf2 = new float[2];
			m.Fill(buf2);
			Assert.AreEqual(0f, buf2[0], 1e-6);
		}

		[Test]
		public void MixerVoicePoliciesArbitrate()
		{
			// Reject declines and never steals.
			var reject = new AudioMixer(1, 2) { VoicePolicy = AudioMixer.PolicyReject };
			var a = Voice(1000, 1);
			var b = Voice(1000, 1);
			var c = Voice(1000, 1);
			_ = reject.TrySubmit(a, 0);
			_ = reject.TrySubmit(b, 0);
			_ = reject.TrySubmit(c, 0);
			reject.Fill(new float[2]);
			Assert.IsFalse(a.IsTerminal);
			Assert.IsFalse(b.IsTerminal);
			Assert.AreEqual(AudioPlaybackState.Stopped, c.TerminalState);
			Assert.AreEqual(1, reject.DroppedPlayCount);

			// Oldest steals the earliest admission.
			var oldest = new AudioMixer(1, 2) { VoicePolicy = AudioMixer.PolicyOldest };
			var d = Voice(1000, 1);
			var e = Voice(1000, 1);
			var f = Voice(1000, 1);
			_ = oldest.TrySubmit(d, 0);
			_ = oldest.TrySubmit(e, 0);
			_ = oldest.TrySubmit(f, 0);
			oldest.Fill(new float[2]);
			Assert.AreEqual(AudioPlaybackState.Stolen, d.TerminalState);
			Assert.IsFalse(e.IsTerminal);
			Assert.IsFalse(f.IsTerminal);

			// RoundRobin advances its cursor only on a steal, preserving the pool behaviour it replaces.
			var rr = new AudioMixer(1, 2) { VoicePolicy = AudioMixer.PolicyRoundRobin };
			var g = Voice(1000, 1);
			var h = Voice(1000, 1);
			_ = rr.TrySubmit(g, 0);
			_ = rr.TrySubmit(h, 0);
			rr.Fill(new float[2]);
			_ = rr.TrySubmit(Voice(1000, 1), 0);
			rr.Fill(new float[2]);
			Assert.IsTrue(g.IsTerminal, "the first steal took slot 0");
			_ = rr.TrySubmit(Voice(1000, 1), 0);
			rr.Fill(new float[2]);
			Assert.IsTrue(h.IsTerminal, "the second steal took slot 1");
		}

		[Test]
		public void MixerStopAllIsImmediateAndCannotBeRevived()
		{
			var m = new AudioMixer(1, 4);
			var live = Voice(1000, 1);
			_ = m.TrySubmit(live, 0);
			m.Fill(new float[2]);
			Assert.IsFalse(live.IsTerminal);

			// Submitted under the old epoch, so the stop that follows must discard it.
			var queued = Voice(1000, 1);
			_ = m.TrySubmit(queued, 0);
			m.StopAll();
			m.Fill(new float[2]);
			Assert.AreEqual(AudioPlaybackState.Stopped, live.TerminalState);
			Assert.AreEqual(AudioPlaybackState.Stopped, queued.TerminalState);

			var after = Voice(1000, 1);
			_ = m.TrySubmit(after, 0);
			m.Fill(new float[2]);
			Assert.IsFalse(after.IsTerminal, "a play admitted after the stop is allowed");
		}

		[Test]
		public void MixerHonoursLoopPauseAndSeek()
		{
			var loop = new AudioMixer(1, 2);
			var v = Voice(2, 1, 0.5f);
			_ = v.SetLoop(true);
			_ = loop.TrySubmit(v, 0);
			var buf = new float[6];
			loop.Fill(buf);
			Assert.IsFalse(v.IsTerminal, "a looping voice does not end at its clip length");

			foreach (var s in buf)
				Assert.AreEqual(0.5f, s, 1e-6);

			var m = new AudioMixer(1, 2);
			var p = Voice(100, 1, 0.5f);
			_ = m.TrySubmit(p, 0);
			m.Fill(new float[4]);
			var held = p.PositionFrames;
			_ = p.SetPaused(true);
			m.Fill(new float[4]);
			Assert.AreEqual(held, p.PositionFrames, "a paused voice holds its position");
			_ = p.SetPaused(false);
			_ = p.SetSeek(50);
			m.Fill(new float[4]);
			Assert.AreEqual(54, p.PositionFrames, "a seek takes effect on the next quantum");
		}

		[Test]
		public void MixerTerminalTransitionHasExactlyOneWinner()
		{
			var v = Voice(4, 1);
			Assert.IsTrue(v.TryFinish(AudioPlaybackState.Stopped));
			Assert.IsFalse(v.TryFinish(AudioPlaybackState.Ended), "a second terminal transition loses");
			Assert.AreEqual(AudioPlaybackState.Stopped, v.TerminalState);
			Assert.IsFalse(v.SetVolume(0.5f), "controls refuse after a terminal transition");
		}

		[Test]
		public void MixerRingRefusesWithoutAllocatingAVoice()
		{
			// Bounded rejection is what the script layer reports as a blank return rather than an error.
			var m = new AudioMixer(1, 1);
			var accepted = 0;

			for (var i = 0; i < 5000; i++)
				if (m.TrySubmit(Voice(10, 1), 0))
					accepted++;

			Assert.Less(accepted, 5000, "a full command ring refuses");
			Assert.AreEqual(5000 - accepted, m.DroppedPlayCount, "and every refusal is counted");
		}

		// ---- device loss ---------------------------------------------------------------------

		/// <summary>
		/// A backend that opens one stream whose device can be made to vanish on demand. Everything else is the
		/// smallest answer that lets an output open.
		/// </summary>
		private sealed class LosableBackend : IAudioBackend
		{
			internal LosableStream Opened;
			internal TestMeter Meter;
			internal bool Available = true;
			internal bool Present = true;
			internal bool? Running;
			internal int EnumerationError;
			internal Func<(IAudioOutputStream Stream, string Error)> OutputFactory;

			public bool IsAvailable => Available;
			public int LastError => EnumerationError;
			public bool Supports(AudioCapability capability)
				=> capability is AudioCapability.Playback or AudioCapability.DeviceEnumeration or AudioCapability.Metering;
			public string UnsupportedReason(AudioCapability capability) => "";
			public AudioDeviceDescriptor[] EnumerateDevices(AudioDeviceKind kind) => EnumerationError == 0 ? [Descriptor(kind)] : [];

			public bool TryGetDefaultDevice(AudioDeviceKind kind, out AudioDeviceDescriptor device)
			{
				device = Descriptor(kind);
				return true;
			}

			public bool TryGetDevice(string id, out AudioDeviceDescriptor device)
			{
				device = Descriptor(AudioDeviceKind.Output);
				return Present && id == device.Id;
			}

			public bool TryOpenOutput(in AudioOutputRequest request, IAudioRenderSource source, out IAudioOutputStream stream, out string error)
			{
				if (OutputFactory != null)
				{
					var result = OutputFactory();
					stream = result.Stream;
					error = result.Error;
					return stream != null;
				}

				Opened = new LosableStream();
				stream = Opened;
				error = null;
				return true;
			}

			public bool TryGetVolume(AudioDeviceKind kind, string id, out double volume) { volume = 0; return false; }
			public bool TrySetVolume(AudioDeviceKind kind, string id, double volume) => false;
			public bool TryGetMute(AudioDeviceKind kind, string id, out bool mute) { mute = false; return false; }
			public bool TrySetMute(AudioDeviceKind kind, string id, bool mute) => false;
			public bool TryGetIsRunning(AudioDeviceKind kind, string id, out bool running) { running = Running.GetValueOrDefault(); return Running.HasValue; }
			public object GetNativeDeviceObject(AudioDeviceKind kind, string id) => null;
			public IAudioDeviceWatcher WatchDevices(Action sink) => null;

			// Capture, sessions and decoding are all reported unsupported by Supports above, so these are
			// the refusals a caller that ignored that would get.
			public bool TryOpenInput(in AudioInputRequest request, IAudioCaptureSink sink, out IAudioInputStream stream, out string error)
			{
				stream = null;
				error = "unsupported";
				return false;
			}

			public AudioSessionDescriptor[] EnumerateSessions(string deviceId) => [];
			public bool TryRefreshSession(string sessionId, out AudioSessionDescriptor descriptor) { descriptor = default; return false; }
			public bool TryGetSessionVolume(string sessionId, out double linearVolume) { linearVolume = 0; return false; }
			public bool TrySetSessionVolume(string sessionId, double linearVolume) => false;
			public bool TryGetSessionMute(string sessionId, out bool mute) { mute = false; return false; }
			public bool TrySetSessionMute(string sessionId, bool mute) => false;
			public object GetNativeSessionObject(string sessionId) => null;

			public bool TryOpenMeter(string targetId, bool isSession, double intervalMilliseconds, out IAudioNativeMeter meter, out string error)
			{
				Meter = new TestMeter();
				meter = Meter;
				error = null;
				return true;
			}

			public string[] SupportedFormats => [];
			public bool TryDecodeFile(string path, out float[] samples, out int sampleRate, out int channels, out string error)
			{
				samples = null;
				sampleRate = 0;
				channels = 0;
				error = "unsupported";
				return false;
			}

			public void Dispose() { }

			private static AudioDeviceDescriptor Descriptor(AudioDeviceKind kind)
				=> new ($"test:{kind}", $"Test {kind}", kind, true);
		}

		private sealed class LosableStream : IAudioOutputStream
		{
			internal int Lost;
			internal int StartCount, StopCount;

			public AudioStreamFormat Format => new (48000, 2);
			public double LatencyMilliseconds => 10;
			public bool IsDeviceLost => Volatile.Read(ref Lost) != 0;
			public void Start() => Interlocked.Increment(ref StartCount);
			public void Stop() => Interlocked.Increment(ref StopCount);
			public void Dispose() { }
		}

		[Test]
		public void DeviceStatus()
		{
			var backend = new LosableBackend();
			using var service = new AudioService(null, backend);
			Assert.IsTrue(backend.TryGetDefaultDevice(AudioDeviceKind.Output, out var descriptor));
			var device = Ks.Audio.Device.Wrap(service, descriptor);

			foreach (var (running, status) in new (bool?, string)[] { (true, "Running"), (false, "Idle"), (null, "Unknown") })
			{
				backend.Running = running;
				Assert.AreEqual(status, device.Status);
				Assert.AreEqual(running == true, device.IsRunning);
			}

			backend.Running = true;
			backend.Available = false;
			Assert.AreEqual("", device.Refresh());
			Assert.AreEqual("Unknown", device.Status, "an unavailable backend cannot establish device removal");
			Assert.AreEqual(false, device.IsRunning);

			backend.Available = true;
			backend.Present = false;
			Assert.AreEqual("", device.Refresh());
			Assert.AreEqual("Missing", device.Status);
			Assert.AreEqual(false, device.IsRunning);

			backend.Present = true;
			Assert.AreSame(device, device.Refresh());
			Assert.AreEqual("Running", device.Status);
			Assert.AreEqual(true, device.IsRunning);

			var removed = Ks.Audio.Device.WrapMissing(service, descriptor);
			Assert.AreEqual("Missing", removed.Status);
			Assert.AreEqual(false, removed.IsRunning);
		}

		[Test]
		public void OutputObservesDeviceLoss()
		{
			// A backend raises IsDeviceLost from its own render thread and signals nothing, so the transition only
			// happens if the paths a script reads observe the flag. Nothing polled it before this was wired.
			var backend = new LosableBackend();
			var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			Assert.IsTrue(core.TryOpen(out var error), error);
			Assert.AreEqual(AudioOutputStatus.Open, core.Status);

			var clip = new AudioClipData(new float[48000 * 2], 48000, 2, AudioFormats.Float32);
			var playing = core.TryPlay(clip, 1f, 0f, false, 0, out _);
			Assert.IsNotNull(playing, "a healthy output admits the play");

			backend.Opened.Lost = 1;

			Assert.AreEqual(AudioOutputStatus.Unavailable, core.Status, "reading Status observes the loss");
			Assert.IsTrue(playing.IsTerminal);
			Assert.AreEqual(AudioPlaybackState.DeviceLost, playing.TerminalState);
			Assert.IsNull(core.TryPlay(clip, 1f, 0f, false, 0, out _), "and nothing new is admitted afterwards");
			core.Dispose();
		}

		[Test]
		public void StopAllDoesNotSwallowAPlayIssuedAfterIt()
		{
			// A StopAll and a Play landing between two render quanta. The play carries the new stop epoch, so the
			// retire sweep that the same quantum performs must not cut it: doing so silences a sound that never
			// began. Deliberately no Fill between the two, which is what would hide the ordering.
			var m = new AudioMixer(1, 4);
			var buffer = new float[64];
			var playing = Voice(4800, 1);
			Assert.IsTrue(m.TrySubmit(playing, 0));
			m.Fill(buffer);
			Assert.IsTrue(playing.IsAdmitted);

			m.StopAll();
			var afterStop = Voice(4800, 1, 0.5f);
			Assert.IsTrue(m.TrySubmit(afterStop, 0));
			m.Fill(buffer);

			Assert.IsTrue(playing.IsTerminal, "the stop ends what was already playing");
			Assert.AreEqual(AudioPlaybackState.Stopped, playing.TerminalState);
			Assert.IsFalse(afterStop.IsTerminal, "but not the play submitted after it");
			Assert.IsTrue(afterStop.IsAdmitted, "which holds a voice and sounds");
		}

		[Test]
		public void MixerSnapshotCollisionDoesNotRenderFullVolume()
		{
			// A control write racing the renderer must degrade to the values already published, never to the
			// defaults: a voice held at 5% that renders one quantum at 100% is an audible burst. The write has to
			// actually run concurrently, because a single-threaded Snapshot always succeeds and hides the seed.
			var m = new AudioMixer(1, 2);
			var voice = Voice(480000, 1);
			_ = voice.SetVolume(0.05f);
			Assert.IsTrue(m.TrySubmit(voice, 0));
			var buffer = new float[32];
			m.Fill(buffer);
			var stop = false;
			var writer = new Thread(() =>
			{
				while (!Volatile.Read(ref stop))
					_ = voice.SetVolume(0.05f);
			})
			{ IsBackground = true };
			writer.Start();
			var loudest = 0f;

			try
			{
				for (var pass = 0; pass < 20000; pass++)
				{
					m.Fill(buffer);

					for (var i = 0; i < buffer.Length; i++)
					{
						var a = Math.Abs(buffer[i]);

						if (a > loudest)
							loudest = a;
					}
				}
			}
			finally
			{
				Volatile.Write(ref stop, true);
				writer.Join();
			}

			Assert.Less(loudest, 0.5f, "a voice held at 5 percent never renders at full scale");
		}

		[Test]
		public void Float32RecordingRoundTripsThroughTheWavWriter()
		{
			// Recording with SampleFormat "Float32" must write IEEE-float samples under a float header. Packing
			// 16-bit samples into a 4-byte-per-sample buffer yields noise at half length.
			float[] source = [0f, 0.5f, -0.5f, 1f, -1f, 0.25f];
			var payload = new byte[source.Length * sizeof(float)];
			WavCodec.FromFloat(source, AudioFormats.Float32, payload);

			var header = WavCodec.Header(48000, 1, 32, true, payload.LongLength);
			var image = new byte[header.Length + payload.Length];
			header.CopyTo(image, 0);
			payload.CopyTo(image, header.Length);

			Assert.IsTrue(WavCodec.TryDecode(image, out var decoded, out var rate, out var channels, out var error), error);
			Assert.AreEqual(48000, rate);
			Assert.AreEqual(1, channels);
			Assert.AreEqual(source.Length, decoded.Length);

			for (var i = 0; i < source.Length; i++)
				Assert.AreEqual(source[i], decoded[i], 1e-6f, $"sample {i}");
		}

		[Test]
		public void StartOffsetIsInterpretedInTheStreamsRateNotTheClips()
		{
			// The voice indexes the prepared buffer, which is resampled to the stream's rate. Converting the
			// requested offset with the clip's rate instead seeks to the wrong place on any device that resamples
			// — an 8 kHz clip on a 48 kHz output would start at a sixth of the offset asked for.
			var backend = new LosableBackend();                       // negotiates 48 kHz stereo
			var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			Assert.IsTrue(core.TryOpen(out var error), error);

			// Two seconds of 8 kHz mono, so frame N of the prepared buffer is N/48000 seconds in.
			var clip = new AudioClipData(new float[8000 * 2], 8000, 1, AudioFormats.Float32);
			var playing = core.TryPlay(clip, 1f, 0f, false, 1000.0, out var playError);
			Assert.IsNotNull(playing, playError);

			// One quantum admits the command and moves the start offset into the voice.
			core.Mixer.Fill(new float[2 * 2]);
			Assert.AreEqual(48000, playing.PositionFrames - 2, 1,
							"one second in is 48000 frames of the prepared buffer, not 8000");
			core.Dispose();
		}

		private sealed class TestMeter : IAudioNativeMeter
		{
			internal bool Disposed;
			public double Peak => 0.5;
			public void Dispose() => Disposed = true;
		}

		[TestCase("", "test:Output")]
		[TestCase("1", "test:Output")]
		[TestCase("2", "test:Input")]
		[TestCase("Test:2", "test:Input")]
		[TestCase("test input", "test:Input")]
		public void SoundDeviceSelectors(string selector, string expected)
		{
			Assert.IsTrue(Sound.TryResolveSoundDevice(new LosableBackend(), selector, out var device));
			Assert.AreEqual(expected, device.Id);
		}

		private sealed class PendingOutputStream(int channels = 2) : IAudioOutputStream
		{
			internal int Starts;
			internal int Disposals;
			internal Action OnDispose;
			internal Exception StopError;
			internal Func<bool> DeviceLost;
			public AudioStreamFormat Format => new(48000, channels);
			public double LatencyMilliseconds => 10;
			public bool IsDeviceLost => DeviceLost?.Invoke() ?? false;
			public void Start() => Interlocked.Increment(ref Starts);
			public void Stop() { if (StopError != null) throw StopError; }
			public void Dispose() { Interlocked.Increment(ref Disposals); OnDispose?.Invoke(); }
		}

		[TestCase(false), TestCase(true)]
		public void DisposeRejectsReopenBeforeNativeCleanup(bool stopFails)
		{
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var native = new PendingOutputStream
			{
				StopError = stopFails ? new IOException("injected stop failure") : null,
				OnDispose = () =>
				{
					entered.Set();
					if (!release.Wait(5000)) throw new TimeoutException("The native cleanup was not released.");
				}
			};
			var opens = 0;
			var backend = new LosableBackend { OutputFactory = () => { opens++; return (native, null); } };
			using var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			Assert.IsTrue(core.TryOpen(out var error), error);
			var disposing = System.Threading.Tasks.Task.Run(core.Dispose);

			try
			{
				Assert.IsTrue(entered.Wait(5000), "native disposal began, including after a stop failure");
				Assert.AreEqual(AudioOutputStatus.Disposed, core.Status);
				Assert.IsFalse(core.TryOpen(out _), "terminal ownership must be published before native cleanup");
				Assert.AreEqual(1, opens, "a disposed output cannot request another native generation");
				Assert.IsNull(core.Mixer);
			}
			finally
			{
				release.Set();
				Assert.IsTrue(disposing.Wait(5000));
			}
			Assert.AreEqual(1, native.Disposals);
		}

		[Test]
		public void RetiredDeviceLossCannotCloseReopenedOutput()
		{
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var first = new PendingOutputStream();
			var second = new PendingOutputStream();
			var opens = 0;
			var backend = new LosableBackend { OutputFactory = () => (++opens == 1 ? first : second, null) };
			using var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			Assert.IsTrue(core.TryOpen(out var error), error);
			first.DeviceLost = () =>
			{
				entered.Set();
				if (!release.Wait(5000)) throw new TimeoutException("The old device-loss observation was not released.");
				return true;
			};
			var observing = System.Threading.Tasks.Task.Run(() => core.Status);

			try
			{
				Assert.IsTrue(entered.Wait(5000));
				core.Close();
				Assert.IsTrue(core.TryOpen(out error), error);
				release.Set();
				Assert.IsTrue(observing.Wait(5000));
				Assert.AreEqual(AudioOutputStatus.Open, observing.Result);
				Assert.AreEqual(0, second.Disposals, "loss observed on a retired generation cannot release its successor");
				Assert.AreEqual(1, second.Starts);
				Assert.IsNotNull(core.Mixer);
			}
			finally
			{
				release.Set();
				_ = observing.Wait(5000);
			}
		}

		[TestCase(false, 0)]
		[TestCase(false, 1)]
		[TestCase(false, 2)]
		[TestCase(true, 0)]
		[TestCase(true, 1)]
		[TestCase(true, 2)]
		public void PendingOpenCannotUndoCloseOrDispose(bool dispose, int outcome)
		{
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			var native = outcome == 0 ? null : new PendingOutputStream(outcome == 1 ? 0 : 2);
			var backend = new LosableBackend
			{
				OutputFactory = () =>
				{
					entered.Set();
					if (!release.Wait(5000)) throw new TimeoutException("The pending open was not released.");
					return (native, outcome == 0 ? "injected open failure" : null);
				}
			};
			using var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			var opening = System.Threading.Tasks.Task.Run(() => core.TryOpen(out _));

			try
			{
				Assert.IsTrue(entered.Wait(5000), "the backend received the open request");
				if (dispose) core.Dispose(); else core.Close();
				release.Set();
				Assert.IsTrue(opening.Wait(5000), "the pending open completed");
				Assert.IsFalse(opening.Result);
				Assert.AreEqual(dispose ? AudioOutputStatus.Disposed : AudioOutputStatus.Closed, core.Status);
				Assert.IsNull(core.Error, "retired attempts cannot publish an error into the current state");
				Assert.IsNull(core.Mixer);
				if (native != null)
				{
					Assert.AreEqual(0, native.Starts);
					Assert.AreEqual(1, native.Disposals);
				}
			}
			finally
			{
				release.Set();
				_ = opening.Wait(5000);
			}
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(2)]
		[TestCase(3)]
		public void RetiredOpenCannotPublishIntoReopen(int outcome)
		{
			using var firstEntered = new ManualResetEventSlim();
			using var secondEntered = new ManualResetEventSlim();
			using var releaseFirst = new ManualResetEventSlim();
			using var releaseSecond = new ManualResetEventSlim();
			var firstNative = outcome is 0 or 3 ? null : new PendingOutputStream(outcome == 1 ? 0 : 2);
			var secondNative = new PendingOutputStream();
			var calls = 0;
			var backend = new LosableBackend
			{
				OutputFactory = () =>
				{
					if (Interlocked.Increment(ref calls) == 1)
					{
						firstEntered.Set();
						if (!releaseFirst.Wait(5000)) throw new TimeoutException("The first open was not released.");
						if (outcome == 3) throw new IOException("injected open exception");
						return (firstNative, outcome == 0 ? "injected open failure" : null);
					}

					secondEntered.Set();
					if (!releaseSecond.Wait(5000)) throw new TimeoutException("The second open was not released.");
					return (secondNative, null);
				}
			};
			using var core = new AudioOutputCore(backend, "", 4, AudioMixer.PolicyOldest, 20);
			var first = System.Threading.Tasks.Task.Run(() =>
			{
				try { return core.TryOpen(out _); }
				catch (IOException) when (outcome == 3) { return false; }
			});
			System.Threading.Tasks.Task<bool> second = null;

			try
			{
				Assert.IsTrue(firstEntered.Wait(5000));
				core.Close();
				second = System.Threading.Tasks.Task.Run(() => core.TryOpen(out _));
				Assert.IsTrue(secondEntered.Wait(5000));
				releaseFirst.Set();
				Assert.IsTrue(first.Wait(5000));
				Assert.IsFalse(first.Result);
				Assert.AreEqual(AudioOutputStatus.Opening, core.Status, "only the current attempt may publish its outcome");
				Assert.IsNull(core.Error);
				releaseSecond.Set();
				Assert.IsTrue(second.Wait(5000));
				Assert.IsTrue(second.Result);
				Assert.AreEqual(AudioOutputStatus.Open, core.Status);
				Assert.AreEqual(1, secondNative.Starts);
				Assert.AreEqual(0, secondNative.Disposals);
				if (firstNative != null) Assert.AreEqual(1, firstNative.Disposals);
			}
			finally
			{
				releaseFirst.Set();
				releaseSecond.Set();
				_ = first.Wait(5000);
				_ = second?.Wait(5000);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference PlayTemporaryClip(AudioOutputCore core, bool prepare)
		{
			var clip = new AudioClipData(new float[480], 48000, 1, AudioFormats.Float32);
			if (prepare) Assert.IsTrue(core.TryPrepare(clip, out var error), error);
			Assert.IsNotNull(core.TryPlay(clip, 1f, 0f, false, 0, out var playError), playError);
			return new WeakReference(clip);
		}

		[Test]
		public void TemporaryClipLifetime()
		{
			using var core = new AudioOutputCore(new LosableBackend(), "", 4, AudioMixer.PolicyOldest, 20);
			Assert.IsTrue(core.TryOpen(out var error), error);
			var temporary = PlayTemporaryClip(core, false);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			Assert.IsFalse(temporary.IsAlive, "a queued playback keeps its samples without rooting its source clip");
			core.Mixer.Fill(new float[1024]);
			var pinned = PlayTemporaryClip(core, true);
			GC.Collect();
			Assert.IsTrue(pinned.IsAlive, "Prepare keeps a clip available for later format changes");
			GC.KeepAlive(core);
		}

		[Test]
		public void ConvenienceOutputSuspendsAndResumes()
		{
			var backend = new LosableBackend();
			using var service = new AudioService(null, backend);
			var core = service.GetConvenienceOutput("", out var error);
			Assert.IsNotNull(core, error);
			Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref backend.Opened.StopCount) > 0, 6000), "idle output stops its native stream");
			var starts = backend.Opened.StartCount;
			PlayTemporaryClip(core, false);
			Assert.AreEqual(starts + 1, backend.Opened.StartCount);
			Assert.IsFalse(core.Mixer.IsIdle, "queued commands prevent suspension before admission");
			core.Mixer.Fill(new float[1024]);
			Assert.IsTrue(core.Mixer.IsIdle);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference StartTemporaryMeter(AudioService service)
		{
			var meter = new Ks.Audio.Meter((object[])null) { service = service, targetId = "test:Output" };
			meter.Start();
			return new WeakReference(meter);
		}

		[Test]
		public void MeterRegistryDoesNotRootWrapper()
		{
			var backend = new LosableBackend();
			using var service = new AudioService(null, backend);
			var wrapper = StartTemporaryMeter(service);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			Assert.IsFalse(wrapper.IsAlive);
			service.Dispose();
			Assert.IsTrue(backend.Meter.Disposed, "service teardown also owns the native observation");
		}

		[TestCase("0", false)]
		[TestCase("-1", false)]
		[TestCase("Test:x", false)]
		[TestCase("Missing", true)]
		public void SoundSelectorErrorIdentity(string selector, bool nativeQuery)
		{
			const int NativeFailure = unchecked((int)0x80004005);
			var backend = new LosableBackend { EnumerationError = NativeFailure };
			Assert.IsFalse(Sound.TryResolveSoundDevice(backend, selector, out _, out var error));
			Assert.AreEqual(nativeQuery ? NativeFailure : 0, error, "invalid selectors must not inherit a previous native error");
		}

#if WINDOWS
		[TestCase("0", false), TestCase("Test:x", false), TestCase("Missing", true)]
		public void ComponentDeviceQueryPreservesNativeError(string selector, bool nativeQuery)
		{
			const int NativeFailure = unchecked((int)0x80004005);
			var backend = new LosableBackend { EnumerationError = NativeFailure };
			if (nativeQuery)
				Assert.AreEqual(NativeFailure, Assert.Throws<COMException>(() => Sound.GetDevice(selector, backend)).HResult);
			else
				Assert.IsNull(Sound.GetDevice(selector, backend));
		}
#endif

		[Test]
		public void MixerAdmissionPreventsIdle()
		{
			using var requested = new AutoResetEvent(false);
			using var rendered = new AutoResetEvent(false);
			AudioMixer mixer = null;
			var stopping = 0;
			var worker = new Thread(() =>
			{
				while (requested.WaitOne())
				{
					if (Volatile.Read(ref stopping) != 0) return;
					mixer.Fill(new float[1]);
					rendered.Set();
				}
			}) { IsBackground = true };
			worker.Start();

			try
			{
				for (var i = 0; i < 4096; i++)
				{
					mixer = new AudioMixer(1, 1);
					var voice = Voice(8, 1);
					Assert.IsTrue(voice.SetLoop(true));
					Assert.IsTrue(mixer.TrySubmit(voice, 0));
					requested.Set();
					Assert.IsTrue(SpinWait.SpinUntil(() =>
					{
						Assert.IsFalse(mixer.IsIdle, "a command transferring into a voice must keep the output awake");
						return rendered.WaitOne(0);
					}, 5000), "the renderer completed");
					Assert.IsFalse(mixer.IsIdle);
				}
			}
			finally
			{
				Volatile.Write(ref stopping, 1);
				requested.Set();
				Assert.IsTrue(worker.Join(5000), "the renderer stopped");
			}
		}

#if WINDOWS
		[Test]
		public void DisposedOwnerCannotOpenMci()
		{
			using var owner = new Script();
			owner.Dispose();
			Assert.IsFalse(SoundPlayback.TryPlay(owner, "keysharp-retired-player.wav", false, out var error));
			Assert.AreEqual("Cannot play sound file keysharp-retired-player.wav after its script has exited.", error);
		}
#else
		private static Process StartSleepingPlayer()
		{
			var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
			start.ArgumentList.Add("-c");
			start.ArgumentList.Add("exec sleep 30");
			return Process.Start(start);
		}

		[Test]
		public void ExternalPlayerConcurrentStarts()
		{
			using var owner = new Script();
			using var firstEntered = new ManualResetEventSlim();
			using var releaseFirst = new ManualResetEventSlim();
			using var secondAttempted = new ManualResetEventSlim();
			using var secondEntered = new ManualResetEventSlim();
			var first = System.Threading.Tasks.Task.Run(() => SoundPlayback.StartPlayer(owner, () =>
			{
				firstEntered.Set();
				if (!releaseFirst.Wait(5000)) throw new TimeoutException("The first player start was not released.");
				return StartSleepingPlayer();
			}));
			System.Threading.Tasks.Task<Process> second = null;
			System.Threading.Tasks.Task firstCleanup = null, secondCleanup = null;

			try
			{
				Assert.IsTrue(firstEntered.Wait(5000));
				second = System.Threading.Tasks.Task.Run(() =>
				{
					secondAttempted.Set();
					return SoundPlayback.StartPlayer(owner, () => { secondEntered.Set(); return StartSleepingPlayer(); });
				});
				Assert.IsTrue(secondAttempted.Wait(5000));
				Assert.IsFalse(secondEntered.Wait(100), "the second start waits for the first publication");
				releaseFirst.Set();
				Assert.IsTrue(System.Threading.Tasks.Task.WaitAll([first, second], 5000));
				firstCleanup = SoundPlayback.ReleaseWhenExited(first.Result);
				secondCleanup = SoundPlayback.ReleaseWhenExited(second.Result);
				Assert.IsTrue(firstCleanup.Wait(5000), "replacement kills and reaps the first player");
				Assert.IsFalse(secondCleanup.IsCompleted);
				SoundPlayback.StopCurrent(owner);
				Assert.IsTrue(secondCleanup.Wait(5000), "the current player remains reachable by its owner");
			}
			finally
			{
				releaseFirst.Set();
				_ = first.Wait(5000);
				_ = second?.Wait(5000);
				SoundPlayback.StopCurrent();
				if (first.IsCompletedSuccessfully) firstCleanup ??= SoundPlayback.ReleaseWhenExited(first.Result);
				if (second?.IsCompletedSuccessfully == true) secondCleanup ??= SoundPlayback.ReleaseWhenExited(second.Result);
				Assert.IsTrue(firstCleanup?.Wait(5000) ?? true);
				Assert.IsTrue(secondCleanup?.Wait(5000) ?? true);
			}
		}

		[Test]
		public void ExternalPlayerPendingStartIsStopped()
		{
			using var owner = new Script();
			using var entered = new ManualResetEventSlim();
			using var release = new ManualResetEventSlim();
			using var stopAttempted = new ManualResetEventSlim();
			var starting = System.Threading.Tasks.Task.Run(() => SoundPlayback.StartPlayer(owner, () =>
			{
				entered.Set();
				if (!release.Wait(5000)) throw new TimeoutException("The pending player start was not released.");
				return StartSleepingPlayer();
			}));
			System.Threading.Tasks.Task stopping = null, cleanup = null;

			try
			{
				Assert.IsTrue(entered.Wait(5000));
				stopping = System.Threading.Tasks.Task.Run(() => { stopAttempted.Set(); SoundPlayback.StopCurrent(owner); });
				Assert.IsTrue(stopAttempted.Wait(5000));
				Assert.IsFalse(stopping.Wait(100), "stop waits for the pending player's publication");
				release.Set();
				Assert.IsTrue(System.Threading.Tasks.Task.WaitAll([starting, stopping], 5000));
				cleanup = SoundPlayback.ReleaseWhenExited(starting.Result);
				Assert.IsTrue(cleanup.Wait(5000), "stop must see the player published by an in-flight start");
			}
			finally
			{
				release.Set();
				_ = starting.Wait(5000);
				_ = stopping?.Wait(5000);
				SoundPlayback.StopCurrent();
				if (starting.IsCompletedSuccessfully) cleanup ??= SoundPlayback.ReleaseWhenExited(starting.Result);
				Assert.IsTrue(cleanup?.Wait(5000) ?? true);
			}
		}

		[Test]
		public void ExternalPlayerRedirectedReadersReleased()
		{
			var start = new ProcessStartInfo("/bin/sh")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			start.ArgumentList.Add("-c");
			start.ArgumentList.Add("i=0; while [ $i -lt 8192 ]; do printf 'output exceeding the pipe buffer\\n'; i=$((i + 1)); done; printf 'injected player failure\\n' >&2; exit 7");
			using var process = Process.Start(start);
			var output = process.StandardOutput;
			var errors = process.StandardError;
			var waiting = System.Threading.Tasks.Task.Run(() =>
			{
				var played = SoundPlayback.WaitForPlayer(process, false, "test", "/bin/sh", out var error);
				return (Played: played, Error: error);
			});

			try
			{
				Assert.IsTrue(waiting.Wait(5000), "both redirected pipes drain while the child runs");
				Assert.IsFalse(waiting.Result.Played);
				Assert.AreEqual("Playing test with sh failed: injected player failure", waiting.Result.Error);
				Assert.Throws<ObjectDisposedException>(() => output.Peek());
				Assert.Throws<ObjectDisposedException>(() => errors.Peek());
			}
			finally
			{
				try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
				catch (InvalidOperationException) { }
				Assert.IsTrue(waiting.Wait(5000));
			}
		}

		[TestCase(0)]
		[TestCase(7)]
		public void ExternalPlayerInheritedPipesAreBounded(int exitCode)
		{
			var pidFile = Path.Combine(Path.GetTempPath(), $"keysharp-player-child-{Guid.NewGuid():N}");
			var start = new ProcessStartInfo("/bin/sh")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			start.ArgumentList.Add("-c");
			start.ArgumentList.Add("sleep 10 & printf '%s' \"$!\" > \"$1\"; exit \"$2\"");
			start.ArgumentList.Add("keysharp-drain-test");
			start.ArgumentList.Add(pidFile);
			start.ArgumentList.Add(exitCode.ToString(CultureInfo.InvariantCulture));
			using var process = Process.Start(start);
			var output = process.StandardOutput;
			var errors = process.StandardError;
			var waiting = System.Threading.Tasks.Task.Run(() =>
				SoundPlayback.WaitForPlayer(process, false, "test", "/bin/sh", out _));

			try
			{
				Assert.IsTrue(waiting.Wait(3000), "an exited player must not wait for a descendant's pipe handles");
				Assert.AreEqual(exitCode == 0, waiting.Result);
				using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture));
				Assert.IsFalse(child.HasExited, "the descendant still owns the inherited pipes");
				Assert.Throws<ObjectDisposedException>(() => output.Peek());
				Assert.Throws<ObjectDisposedException>(() => errors.Peek());
			}
			finally
			{
				try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
				catch (InvalidOperationException) { }
				try
				{
					if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), out var childId))
					{
						try
						{
							using var child = Process.GetProcessById(childId);
							if (!child.HasExited) child.Kill();
							Assert.IsTrue(child.WaitForExit(5000));
						}
						catch (ArgumentException) { }
					}
				}
				finally { File.Delete(pidFile); }
				Assert.IsTrue(waiting.Wait(5000));
			}
		}

		[Test]
		public void ExternalPlayerDisposedOwnerCannotStart()
		{
			using var owner = new Script();
			owner.Dispose();
			var called = false;
			Assert.IsNull(SoundPlayback.StartPlayer(owner, () => { called = true; return null; }));
			Assert.IsFalse(called);
		}
#endif
	}
}
