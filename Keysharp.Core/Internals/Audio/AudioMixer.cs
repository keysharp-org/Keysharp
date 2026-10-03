namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// The reasons a playback can end. The live states a script sees — Queued, Playing, Paused — are derived
	/// from admission and the pause flag instead, so only terminal reasons are named here.
	/// </summary>
	internal enum AudioPlaybackState
	{
		Ended,
		Stopped,
		Stolen,
		DeviceLost,
		Error,
	}

	/// <summary>
	/// The state one playback shares between the script thread that mutates it and the render thread that reads
	/// it. Numeric controls are plain fields published under a version counter; the terminal word is the single
	/// point every competing end path fights over, so exactly one of stop, steal, device loss and natural
	/// completion can win.
	/// </summary>
	internal sealed class AudioPlaybackControl
	{
		private int terminal = -1;          // -1 while live, else the winning AudioPlaybackState
		private int version;                // even = coherent, odd = a writer is mid-update
		private float volume = 1f;
		private float pan;
		private int muted;
		private int looping;
		private long seekRequest = -1;      // frames, or -1 for no pending seek
		private int paused;

		private int admitted;

		internal long PositionFrames;       // written by the renderer only
		internal float Peak;                // written by the renderer only
		internal long AdmissionSequence;

		/// <summary>
		/// False between a successful submit and the render quantum that moves the command into a voice slot,
		/// which is exactly the window the script contract calls "Queued".
		/// </summary>
		internal bool IsAdmitted => Volatile.Read(ref admitted) != 0;

		internal void MarkAdmitted() => Volatile.Write(ref admitted, 1);

		/// <summary>The clip data already converted to the owning output.s format.</summary>
		internal float[] Prepared;

		internal bool IsTerminal => Volatile.Read(ref terminal) >= 0;

		internal AudioPlaybackState TerminalState
		{
			get
			{
				var reason = Volatile.Read(ref terminal);
				return reason >= 0 ? (AudioPlaybackState)reason : AudioPlaybackState.Stopped;
			}
		}

		/// <summary>Claims the single terminal transition. Only the caller that gets true may publish an End.</summary>
		internal bool TryFinish(AudioPlaybackState state)
			=> Interlocked.CompareExchange(ref terminal, (int)state, -1) == -1;

		// ---- script-side writers -------------------------------------------------------------

		/// <summary>
		/// Writers are serialised against each other. Two of them incrementing independently would leave the
		/// version even while both were mid-update, and the renderer would accept a pair that never co-existed.
		/// The renderer never takes this lock; it only reads the version.
		/// </summary>
		private readonly Lock writeGate = new();

		private void BeginWrite() => Interlocked.Increment(ref version);

		private void EndWrite()
		{
			// The field writes land before the version says they are coherent.
			Interlocked.MemoryBarrier();
			_ = Interlocked.Increment(ref version);
		}

		internal bool SetVolume(float value)
		{
			if (IsTerminal) return false;

			lock (writeGate)
			{
				BeginWrite();
				volume = value;
				EndWrite();
			}

			return true;
		}

		internal bool SetPan(float value)
		{
			if (IsTerminal) return false;

			lock (writeGate)
			{
				BeginWrite();
				pan = value;
				EndWrite();
			}

			return true;
		}

		internal bool SetMute(bool value)
		{
			if (IsTerminal) return false;

			Volatile.Write(ref muted, value ? 1 : 0);
			return true;
		}

		internal bool SetLoop(bool value)
		{
			if (IsTerminal) return false;

			Volatile.Write(ref looping, value ? 1 : 0);
			return true;
		}

		internal bool SetPaused(bool value)
		{
			if (IsTerminal) return false;

			Volatile.Write(ref paused, value ? 1 : 0);
			return true;
		}

		internal bool IsPaused => Volatile.Read(ref paused) != 0;

		internal bool SetSeek(long frames)
		{
			if (IsTerminal) return false;

			Volatile.Write(ref seekRequest, frames);
			return true;
		}

		// ---- renderer-side reader ------------------------------------------------------------

		/// <summary>
		/// One coherent snapshot of the numeric controls. The renderer never waits on a writer: if it observes an
		/// update in progress it leaves the caller's seed values alone, so the caller must seed with the live
		/// per-field reads rather than with defaults.
		/// </summary>
		internal void Snapshot(ref float outVolume, ref float outPan)
		{
			var before = Volatile.Read(ref version);

			if ((before & 1) != 0)
				return;

			var v = volume;
			var p = pan;
			// The two reads above must not sink past the second version read, which a weakly ordered processor
			// would otherwise allow; without this the check could pass on values taken after a writer moved on.
			Interlocked.MemoryBarrier();

			if (Volatile.Read(ref version) != before)
				return;

			outVolume = v;
			outPan = p;
		}

		internal bool Muted => Volatile.Read(ref muted) != 0;

		internal bool Looping => Volatile.Read(ref looping) != 0;

		/// <summary>The current gain and pan for a script-side getter. At worst one quantum stale, which is the
		/// same freshness the renderer itself works from.</summary>
		internal float VolumeValue => Volatile.Read(ref volume);

		internal float PanValue => Volatile.Read(ref pan);

		internal long TakeSeek() => Interlocked.Exchange(ref seekRequest, -1);
	}

	/// <summary>What one admitted play carries from the script thread to the render thread.</summary>
	internal struct AudioCommand
	{
		internal AudioPlaybackControl Control;
		internal long StopEpoch;
		internal long StartFrame;
	}

	/// <summary>
	/// The fixed software mixer: one per open output, summing a bounded number of voices into the single native
	/// stream that output owns. Everything it does on the render thread is bounded and allocation-free; every
	/// interaction from a script thread is a non-blocking publish that either succeeds or is refused.
	/// </summary>
	internal sealed class AudioMixer : IAudioRenderSource
	{
		internal const string PolicyOldest = "Oldest";
		internal const string PolicyRoundRobin = "RoundRobin";
		internal const string PolicyReject = "Reject";

		private readonly int channels;
		private readonly int voiceLimit;
		private readonly AudioPlaybackControl[] voices;
		private readonly AudioCommand[] ring;
		private readonly int[] ringSequence;
		private readonly int ringMask;

		private long enqueuePosition;
		private long dequeuePosition;
		private long stopEpoch;
		private long admissionCounter;
		private int roundRobinCursor;
		private string policy = PolicyOldest;

		private float outputVolume = 1f;
		private int outputMuted;
		private float peak;
		private long droppedPlays;
		private int finishedAll;
		private long underruns;

		internal AudioMixer(int channels, int voiceLimit)
		{
			this.channels = channels;
			this.voiceLimit = voiceLimit;
			voices = new AudioPlaybackControl[voiceLimit];
			var capacity = Math.Min(4096, RoundUpPow2(Math.Max(64, 4 * voiceLimit)));
			ring = new AudioCommand[capacity];
			ringSequence = new int[capacity];
			ringMask = capacity - 1;

			for (var i = 0; i < capacity; i++)
				ringSequence[i] = i;
		}

		internal long DroppedPlayCount => Interlocked.Read(ref droppedPlays);

		internal long UnderrunCount => Interlocked.Read(ref underruns);

		internal float Peak => Volatile.Read(ref peak);

		internal bool IsIdle => Volatile.Read(ref enqueuePosition) == Volatile.Read(ref dequeuePosition) && ActiveVoiceCount == 0;

		internal string VoicePolicy
		{
			get => Volatile.Read(ref policy);
			set => Volatile.Write(ref policy, value);
		}

		internal float OutputVolume
		{
			get => Volatile.Read(ref outputVolume);
			set => Volatile.Write(ref outputVolume, value);
		}

		internal bool OutputMuted
		{
			get => Volatile.Read(ref outputMuted) != 0;
			set => Volatile.Write(ref outputMuted, value ? 1 : 0);
		}

		internal int ActiveVoiceCount
		{
			get
			{
				var n = 0;

				for (var i = 0; i < voices.Length; i++)
					if (Volatile.Read(ref voices[i]) != null)
						n++;

				return n;
			}
		}

		/// <summary>
		/// Publishes one play. Returns false without allocating when the ring is full, which is the bounded
		/// rejection a caller reports as a blank return rather than an error.
		/// </summary>
		internal bool TrySubmit(AudioPlaybackControl control, long startFrame)
		{
			control.AdmissionSequence = Interlocked.Increment(ref admissionCounter);

			// A lost CAS means another producer won the slot, not that the ring is full, so contention retries
			// rather than reporting a drop. The bound keeps a pathologically contended caller from spinning here.
			for (var attempt = 0; attempt < 64; attempt++)
			{
				var pos = Volatile.Read(ref enqueuePosition);
				var index = (int)(pos & ringMask);
				var seq = Volatile.Read(ref ringSequence[index]);
				var diff = seq - (int)pos;

				if (diff == 0)
				{
					if (Interlocked.CompareExchange(ref enqueuePosition, pos + 1, pos) == pos)
					{
						ring[index].Control = control;
						ring[index].StartFrame = startFrame;
						ring[index].StopEpoch = Interlocked.Read(ref stopEpoch);
						Volatile.Write(ref ringSequence[index], (int)pos + 1);

						// The sweep may have passed this slot between the reservation above and the publish. Re-reading
						// it here is what stops a play landing in that window from reporting Queued for ever.
						var swept = Volatile.Read(ref finishedAll);

						if (swept != 0)
							_ = control.TryFinish((AudioPlaybackState)(swept - 1));

						return true;
					}
				}
				else if (diff < 0)
				{
					break;   // full
				}
			}

			_ = Interlocked.Increment(ref droppedPlays);
			return false;
		}

		/// <summary>
		/// Silences every voice immediately and rejects any command admitted before this moment. Linearizes at the
		/// epoch increment, so a concurrent play either carries the new epoch and survives or carries an older one
		/// and is discarded.
		/// </summary>
		internal void StopAll() => Interlocked.Increment(ref stopEpoch);

		/// <summary>Ends every voice with the given reason, used when the device goes away or the output closes.</summary>
		internal void FinishAll(AudioPlaybackState reason)
		{
			StopAll();

			for (var i = 0; i < voices.Length; i++)
			{
				var v = voices[i];

				if (v != null)
				{
					_ = v.TryFinish(reason);
					voices[i] = null;
				}
			}

			// A play submitted but not yet drained holds no voice, so the sweep above cannot reach it, and after a
			// close or a device loss no render quantum will ever drain it either. Ending it here is what stops it
			// reporting Queued forever. The slots themselves are left alone, so an in-flight drain still consumes
			// them and skips whatever is already terminal.
			Volatile.Write(ref finishedAll, (int)reason + 1);

			for (var i = 0; i < ring.Length; i++)
				_ = ring[i].Control?.TryFinish(reason);
		}

		// ---- render thread -------------------------------------------------------------------

		/// <summary>
		/// Fills exactly one quantum. Bounded by construction: it drains at most the ring, advances at most
		/// voiceLimit voices, allocates nothing, and never calls script code.
		/// </summary>
		public void Fill(Span<float> destination)
		{
			destination.Clear();
			var epoch = Interlocked.Read(ref stopEpoch);
			// Retiring first is what lets a play submitted since the last StopAll survive: draining first would admit
			// it into a slot that the retire sweep then cuts, so the sound would be silently stopped the moment it began.
			RetireStoppedVoices(epoch);
			DrainCommands(epoch);

			var frames = destination.Length / channels;
			var gain = Volatile.Read(ref outputVolume);
			var mixed = false;

			if (Volatile.Read(ref outputMuted) != 0)
				gain = 0f;

			for (var slot = 0; slot < voices.Length; slot++)
			{
				var voice = voices[slot];

				if (voice == null || voice.IsTerminal)
				{
					if (voice != null)
					{
						voices[slot] = null;
					}

					continue;
				}

				if (voice.IsPaused)
					continue;

				MixVoice(voice, destination, frames, slot);
				mixed = true;
			}

			// Nothing was summed, so the buffer is already the silence Clear left: applying gain and hunting for a
			// peak in it would touch every sample of an idle output on every quantum.
			if (!mixed)
			{
				Volatile.Write(ref peak, 0f);
				return;
			}

			var localPeak = 0f;

			for (var i = 0; i < destination.Length; i++)
			{
				var s = destination[i] * gain;
				s = s < -1f ? -1f : s > 1f ? 1f : s;
				destination[i] = s;
				var a = s < 0 ? -s : s;

				if (a > localPeak)
					localPeak = a;
			}

			Volatile.Write(ref peak, localPeak);
		}

		private void MixVoice(AudioPlaybackControl voice, Span<float> destination, int frames, int slot)
		{
			var seek = voice.TakeSeek();

			if (seek >= 0)
				voice.PositionFrames = seek;

			var prepared = voice.Prepared;

			if (prepared == null || prepared.Length == 0)
			{
				_ = voice.TryFinish(AudioPlaybackState.Ended);
				voices[slot] = null;
				return;
			}

			var totalFrames = prepared.LongLength / channels;
			// Seeded from the live values so a collision with a writer degrades to a possibly-incoherent pair of
			// current ones. Seeding from defaults would render a quantum at full volume, which is audible.
			var volume = voice.VolumeValue;
			var pan = voice.PanValue;
			voice.Snapshot(ref volume, ref pan);

			if (voice.Muted)
				volume = 0f;

			// Constant-gain pan across a stereo bus; a mono bus ignores it because there is nowhere to place it.
			var left = channels == 2 ? volume * Math.Min(1f, 1f - pan) : volume;
			var right = channels == 2 ? volume * Math.Min(1f, 1f + pan) : volume;
			var position = voice.PositionFrames;
			var voicePeak = 0f;

			for (var f = 0; f < frames; f++)
			{
				if (position >= totalFrames)
				{
					if (!voice.Looping)
					{
						voice.PositionFrames = totalFrames;
						_ = voice.TryFinish(AudioPlaybackState.Ended);
						voices[slot] = null;
						voice.Peak = voicePeak;
						return;
					}

					position = 0;
				}

				var src = position * channels;
				var dst = f * channels;

				if (channels == 2)
				{
					var l = prepared[src] * left;
					var r = prepared[src + 1] * right;
					destination[dst] += l;
					destination[dst + 1] += r;
					voicePeak = Peak3(voicePeak, l, r);
				}
				else
				{
					var m = prepared[src] * left;
					destination[dst] += m;
					voicePeak = Peak3(voicePeak, m, 0f);
				}

				position++;
			}

			voice.PositionFrames = position;
			voice.Peak = voicePeak;

			// A clip whose last frame lands exactly on the quantum boundary is finished now, not next quantum.
			// Deferring it would hold the voice slot against the limit and delay End by a whole period.
			if (position >= totalFrames && !voice.Looping)
			{
				_ = voice.TryFinish(AudioPlaybackState.Ended);
				voices[slot] = null;
			}
		}

		private static float Peak3(float current, float a, float b)
		{
			var x = a < 0 ? -a : a;
			var y = b < 0 ? -b : b;

			if (x > current) current = x;

			return y > current ? y : current;
		}

		/// <summary>
		/// Moves admitted commands into voice slots, applying the current arbitration policy. Bounded by the ring
		/// capacity, and each admission is O(1) because the oldest slot is tracked rather than searched for.
		/// </summary>
		private void DrainCommands(long epoch)
		{
			for (var guard = 0; guard <= ringMask; guard++)
			{
				var pos = dequeuePosition;
				var index = (int)(pos & ringMask);
				var seq = Volatile.Read(ref ringSequence[index]);

				if (seq - (int)(pos + 1) != 0)
					return;

				var command = ring[index];
				var control = command.Control;

				if (control != null && !control.IsTerminal)
				{
					// A command reserved before the last StopAll must not revive after the silence it was cut by.
					if (command.StopEpoch < epoch)
						_ = control.TryFinish(AudioPlaybackState.Stopped);
					else
						Admit(control, command.StartFrame);
				}

				ring[index].Control = null;
				// An idle observer must see the admitted voice before it sees the queue become empty.
				Volatile.Write(ref dequeuePosition, pos + 1);
				Volatile.Write(ref ringSequence[index], (int)pos + ringMask + 1);
			}
		}

		private void Admit(AudioPlaybackControl control, long startFrame)
		{
			control.PositionFrames = startFrame;
			control.MarkAdmitted();

			for (var i = 0; i < voices.Length; i++)
			{
				if (voices[i] == null)
				{
					voices[i] = control;
					return;
				}
			}

			var current = VoicePolicy;

			if (current == PolicyReject)
			{
				_ = control.TryFinish(AudioPlaybackState.Stopped);
				Interlocked.Increment(ref droppedPlays);
				return;
			}

			var victim = current == PolicyRoundRobin ? NextRoundRobinSlot() : OldestSlot();
			var stolen = voices[victim];

			if (stolen != null)
				_ = stolen.TryFinish(AudioPlaybackState.Stolen);

			voices[victim] = control;
		}

		private int NextRoundRobinSlot()
		{
			var slot = roundRobinCursor;
			roundRobinCursor = (roundRobinCursor + 1) % voiceLimit;
			return slot;
		}

		/// <summary>
		/// The slot holding the earliest admission. Scanned rather than tracked: a cursor that simply advances
		/// after each steal assumes slot order matches admission order, which stops being true the moment a voice
		/// ends and its slot is refilled, and then steals a voice that is not the oldest. The scan is O(voice
		/// limit) and only runs once every slot is occupied.
		/// </summary>
		private int OldestSlot()
		{
			var best = 0;
			var bestSeq = long.MaxValue;

			for (var i = 0; i < voices.Length; i++)
			{
				var v = voices[i];

				if (v != null && v.AdmissionSequence < bestSeq)
				{
					bestSeq = v.AdmissionSequence;
					best = i;
				}
			}

			return best;
		}

		/// <summary>Cuts every voice admitted before the current stop epoch, which is what makes StopAll immediate.</summary>
		private void RetireStoppedVoices(long epoch)
		{
			if (epoch == lastObservedEpoch)
				return;

			lastObservedEpoch = epoch;

			for (var i = 0; i < voices.Length; i++)
			{
				var v = voices[i];

				if (v != null)
				{
					_ = v.TryFinish(AudioPlaybackState.Stopped);
					voices[i] = null;
				}
			}
		}

		private long lastObservedEpoch;

		public void NoteUnderrun() => Interlocked.Increment(ref underruns);

		private static int RoundUpPow2(int value)
		{
			var v = 1;

			while (v < value)
				v <<= 1;

			return v;
		}
	}
}
