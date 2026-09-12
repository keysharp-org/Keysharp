using Engine = Keysharp.Internals.Audio;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Audio
		{
			/// <summary>
			/// One decoded sound, immutable and reusable. Produced by <c>Audio.Load</c> or <c>Audio.FromPcm</c>;
			/// there is no public constructor, because a clip without samples has nothing to be.
			/// </summary>
			public class Clip : KeysharpObject
			{
				internal Engine.AudioClipData Data;

				public Clip(params object[] args) : base(args) { }

				public override object __New(params object[] args)
					=> Errors.ErrorOccurred("Audio.Clip is produced by Audio.Load() or Audio.FromPcm(), not constructed.");

				internal static Clip Wrap(Engine.AudioClipData data) => new (null) { Data = data };

				/// <summary>The clip's length in milliseconds, derived from its exact frame count.</summary>
				public object DurationMilliseconds => Data.DurationMilliseconds;

				public object FrameCount => Data.FrameCount;

				public object SampleRate => (long)Data.SampleRate;

				public object Channels => (long)Data.Channels;

				/// <summary>The samples' original format: "Unsigned8", "Signed16", "Signed24", "Signed32" or "Float32".</summary>
				public string SampleFormat => Data.SampleFormat;

				public override string ToString() => "Audio.Clip";
			}

			/// <summary>
			/// A snapshot of one audio endpoint, plus the controls that endpoint owns. The <c>Id</c> is the durable
			/// identity: names repeat and enumeration order changes, so every operation resolves by id.
			/// </summary>
			public class Device : KeysharpObject
			{
				internal Engine.AudioDeviceDescriptor Snapshot;
				internal bool missing;

				public Device(params object[] args) : base(args) { }

				public override object __New(params object[] args)
					=> Errors.ErrorOccurred("Audio.Device is produced by Audio.Devices() or Audio.DefaultDevice(), not constructed.");

				internal Engine.AudioService service;

				internal static Device Wrap(Engine.AudioService owner, Engine.AudioDeviceDescriptor d) => new (null) { service = owner, Snapshot = d };

				/// <summary>
				/// A snapshot marked gone, for the device handed to a "Removed" callback. Operating on it inside
				/// the handler raises the actionable "no longer available" error.
				/// </summary>
				internal static Device WrapMissing(Engine.AudioService owner, Engine.AudioDeviceDescriptor d)
					=> new (null) { service = owner, Snapshot = d, missing = true };

				public string Id => Snapshot.Id ?? "";

				public string Name => Snapshot.Name ?? "";

				/// <summary>"Output" or "Input". A device carries audio in exactly one direction.</summary>
				public string Kind => KindName(Snapshot.Kind);


				public object IsDefault => Snapshot.IsDefault;

				/// <summary>
				/// "Running" while any application holds a live stream, "Idle" when none does, "Unknown" when
				/// the backend cannot determine it, or "Missing" after removal is observed. A silent stream is running.
				/// </summary>
				public string Status
				{
					get
					{
						if (missing)
							return "Missing";

						var backend = service?.Backend;
						return backend is { IsAvailable: true } && backend.TryGetIsRunning(Snapshot.Kind, Snapshot.Id, out var running)
							   ? running ? "Running" : "Idle"
							   : "Unknown";
					}
				}

				/// <summary>Whether a live stream is known to be open. Read Status to distinguish Idle, Unknown and Missing.</summary>
				public object IsRunning => Status == "Running";

				/// <summary>This endpoint's own volume, from 0 through 100.</summary>
				public object Volume
				{
					get
					{
						var backend = RequireLive(out var failure);

						if (backend == null)
							return failure;

						if (!backend.TryGetVolume(Snapshot.Kind, Snapshot.Id, out var linear))
							return Errors.OSErrorOccurredWithMessage($"Cannot read the volume of {Name}: {service.UnsupportedReason(Engine.AudioCapability.EndpointVolume)}");

						return Level(Math.Clamp(linear, 0.0, 1.0));
					}

					set
					{
						var backend = RequireLive(out _);

						if (backend == null)
							return;

						if (!TryLevel(value, "Volume", out var scalar, out _))
							return;

						if (!backend.TrySetVolume(Snapshot.Kind, Snapshot.Id, scalar))
							_ = Errors.OSErrorOccurredWithMessage($"Cannot set the volume of {Name}: {service.UnsupportedReason(Engine.AudioCapability.EndpointVolume)}");
					}
				}

				/// <summary>This endpoint's own mute state.</summary>
				public object Mute
				{
					get
					{
						var backend = RequireLive(out var failure);

						if (backend == null)
							return failure;

						if (!backend.TryGetMute(Snapshot.Kind, Snapshot.Id, out var mute))
							return Errors.OSErrorOccurredWithMessage($"Cannot read the mute state of {Name}: {service.UnsupportedReason(Engine.AudioCapability.EndpointVolume)}");

						return mute;
					}

					set
					{
						var backend = RequireLive(out _);

						if (backend == null)
							return;

						if (!backend.TrySetMute(Snapshot.Kind, Snapshot.Id, value.Ab()))
							_ = Errors.OSErrorOccurredWithMessage($"Cannot set the mute state of {Name}: {service.UnsupportedReason(Engine.AudioCapability.EndpointVolume)}");
					}
				}

				/// <summary>
				/// Re-reads this exact device. Returns the receiver when present, or blank otherwise. An absent
				/// device is marked Missing; an unavailable backend does not establish removal.
				/// </summary>
				public object Refresh()
				{
					var backend = service.Backend;

					if (backend is { IsAvailable: true })
					{
						if (backend.TryGetDevice(Snapshot.Id, out var fresh))
						{
							Snapshot = fresh;
							missing = false;
							return this;
						}

						missing = true;
					}

					return "";
				}

				/// <summary>
				/// The platform's own device object, for the controls this class does not model. Its concrete type
				/// is platform-dependent and unspecified, and work done through it bypasses this class's caching.
				/// </summary>
				public object ToClr()
				{
					var backend = RequireLive(out var failure);

					if (backend == null)
						return failure;

					var native = backend.GetNativeDeviceObject(Snapshot.Kind, Snapshot.Id);

					return native == null
						   ? Errors.OSErrorOccurredWithMessage($"This platform exposes no native object for {Name}: {service.UnsupportedReason(Engine.AudioCapability.NativeObject)}")
						   : ManagedInvoke.WrapManaged(native);
				}

				private Engine.IAudioBackend RequireLive(out object failure)
				{
					failure = null;

					if (missing)
					{
						failure = Errors.OSErrorOccurredWithMessage($"The device {Name} is no longer available; call Refresh() first.");
						return null;
					}

					var backend = service.Backend;

					if (backend == null || !backend.IsAvailable)
					{
						failure = Unsupported(service, Engine.AudioCapability.DeviceEnumeration, "This device");
						return null;
					}

					return backend;
				}

				public override string ToString() => Name;
			}

			/// <summary>
			/// An explicit mixer and its one native stream. This is the object a game or any latency-sensitive
			/// script owns: it opens once, prepares its clips once, and then every play is a bounded, non-blocking
			/// submission that neither decodes nor resamples.
			/// </summary>
			public class Output : KeysharpObject
			{
				internal Engine.AudioOutputCore core;

				/// <summary>The service that created this output. Captured so a later Script swap cannot make it
				/// register, open or close against a different script's backend.</summary>
				internal Engine.AudioService service;

				public Output(params object[] args) : base(args) { }

				/// <summary>
				/// Configures a closed output. Nothing native is touched until <c>Open</c> or <c>TryOpen</c>, so
				/// construction cannot fail for an absent device. VoicePolicy is "Oldest" (the default), "RoundRobin"
				/// or "Reject", matched without regard to case.
				/// </summary>
				public object __New(object device = null, object voiceLimit = null, object voicePolicy = null, object latencyMilliseconds = null)
				{
					var voices = voiceLimit == null ? 16L : voiceLimit.Al();

					if (voices < 1 || voices > 256)
						return Errors.ValueErrorOccurred("VoiceLimit must be from 1 through 256.", voiceLimit);

					var policy = voicePolicy.As();

					if (policy.Length == 0)
						policy = Engine.AudioMixer.PolicyOldest;

					policy = string.Equals(policy, Engine.AudioMixer.PolicyOldest, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyOldest
							 : string.Equals(policy, Engine.AudioMixer.PolicyRoundRobin, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyRoundRobin
							 : string.Equals(policy, Engine.AudioMixer.PolicyReject, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyReject
							 : null;

					if (policy == null)
						return Errors.ValueErrorOccurred($"Unknown VoicePolicy \"{Errors.Describe(voicePolicy)}\". Expected Oldest, RoundRobin or Reject.", voicePolicy);

					var latency = latencyMilliseconds == null ? 20.0 : latencyMilliseconds.Ad();

					if (!double.IsFinite(latency) || latency < 5 || latency > 500)
						return Errors.ValueErrorOccurred("LatencyMilliseconds must be a finite number from 5 through 500.", latencyMilliseconds);

					var deviceId = "";

					if (device is Ks.Audio.Device || device.As().Length > 0)
					{
						if (!TryResolveDevice(device, Engine.AudioDeviceKind.Output, true, out var resolved, out var failure))
							return failure;

						deviceId = resolved.Id;
					}

					service = Service;
					core = new Engine.AudioOutputCore(service, deviceId, (int)voices, policy, latency);
					return DefaultObject;
				}

				internal Engine.AudioOutputCore Require(out object failure)
				{
					failure = null;

					if (core == null)
					{
						failure = Errors.ValueErrorOccurred("This Audio.Output was not constructed correctly.");
						return null;
					}

					if (core.Status == Engine.AudioOutputStatus.Disposed)
					{
						failure = Errors.ValueErrorOccurred("This Audio.Output has been disposed.");
						return null;
					}

					return core;
				}

				/// <summary>The device this output is bound to, or blank while it follows the current default.</summary>
				public object Device
				{
					get
					{
						if (core == null || core.BoundDeviceId.Length == 0)
							return "";

						var backend = service.Backend;
						return backend is { IsAvailable: true } && backend.TryGetDevice(core.BoundDeviceId, out var d) ? Ks.Audio.Device.Wrap(service, d) : "";
					}
				}

				/// <summary>"Closed", "Opening", "Open", "Transitioning", "Unavailable" or "Disposed".</summary>
				public string Status => core?.Status ?? Engine.AudioOutputStatus.Disposed;

				/// <summary>Whether the configured binding resolves to a usable device, even while manually closed.</summary>
				public object IsAvailable => core != null && core.IsAvailable;

				public object IsFollowingDefault => core != null && core.IsFollowingDefault;

				public object VoiceLimit => (long)(core?.VoiceLimit ?? 0);

				public object ActiveVoiceCount => (long)(core?.ActiveVoiceCount ?? 0);

				public object DroppedPlayCount => core?.DroppedPlayCount ?? 0L;

				public object UnderrunCount => core?.UnderrunCount ?? 0L;

				public object SampleRate => core != null && core.SampleRate > 0 ? (long)core.SampleRate : "";

				public object Channels => core != null && core.Channels > 0 ? (long)core.Channels : "";

				public object RequestedLatencyMilliseconds => core?.RequestedLatencyMilliseconds ?? 0.0;

				public object LatencyMilliseconds => core != null && core.MeasuredLatencyMilliseconds > 0 ? core.MeasuredLatencyMilliseconds : "";

				/// <summary>The loudest sample of the last completed quantum after output gain, or blank when closed.</summary>
				public object Peak => core?.PeakLevel is float p ? Level(p) : "";

				public object Error => core?.Error is string e && e.Length > 0 ? new Error(e) : (object)"";

				/// <summary>This output's own gain, applied after every voice is summed. It survives a close.</summary>
				public object Volume
				{
					get => Level(core?.OutputVolume ?? 1f);

					set
					{
						if (core != null && TryLevel(value, "Volume", out var scalar, out _))
							core.OutputVolume = scalar;
					}
				}

				public object Mute
				{
					get => core?.OutputMuted ?? false;

					set
					{
						if (core != null)
							core.OutputMuted = value.Ab();
					}
				}

				/// <summary>"Oldest", "RoundRobin" or "Reject". Changing it affects only future admissions.</summary>
				public object VoicePolicy
				{
					get => core?.VoicePolicy ?? Engine.AudioMixer.PolicyOldest;

					set
					{
						var text = value.As();
						var policy = string.Equals(text, Engine.AudioMixer.PolicyOldest, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyOldest
									 : string.Equals(text, Engine.AudioMixer.PolicyRoundRobin, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyRoundRobin
									 : string.Equals(text, Engine.AudioMixer.PolicyReject, StringComparison.OrdinalIgnoreCase) ? Engine.AudioMixer.PolicyReject
									 : null;

						if (policy == null)
						{
							_ = Errors.ValueErrorOccurred($"Unknown VoicePolicy \"{text}\". Expected Oldest, RoundRobin or Reject.", value);
							return;
						}

						if (core != null)
							core.VoicePolicy = policy;
					}
				}

				/// <summary>Opens the native stream, raising on failure. Returns the receiver so it chains.</summary>
				public object Open()
				{
					var c = Require(out var failure);

					if (c == null)
						return failure;

					if (!service.Supports(Engine.AudioCapability.Playback))
						return Unsupported(service, Engine.AudioCapability.Playback, "Audio.Output.Open");

					if (!service.TryRegisterOpenOutput(c))
						return Errors.OSErrorOccurredWithMessage($"This script already has {Engine.AudioService.MaxOpenOutputs} audio outputs open. Close one with Close() or Dispose() before opening another.");

					if (!c.TryOpen(out var error))
					{
						service.UnregisterOutput(c);
						return Errors.OSErrorOccurredWithMessage($"Cannot open the audio output: {error}");
					}

					return this;
				}

				/// <summary>
				/// Opens, tolerating device absence and transient failure by returning false. Still raises for an
				/// unsupported backend or an exhausted output budget, which are program errors rather than
				/// environment ones.
				/// </summary>
				public object TryOpen()
				{
					var c = Require(out var failure);

					if (c == null)
						return failure;

					if (!service.Supports(Engine.AudioCapability.Playback))
						return Unsupported(service, Engine.AudioCapability.Playback, "Audio.Output.TryOpen");

					if (!service.TryRegisterOpenOutput(c))
						return Errors.OSErrorOccurredWithMessage($"This script already has {Engine.AudioService.MaxOpenOutputs} audio outputs open. Close one with Close() or Dispose() before opening another.");

					if (c.TryOpen(out _))
						return true;

					service.UnregisterOutput(c);
					return false;
				}

				/// <summary>Converts a clip into this output's format ahead of time, so playing it never resamples.</summary>
				public object Prepare(object clip)
				{
					var c = Require(out var failure);

					if (c == null)
						return failure;

					if (clip is not Clip clipValue)
						return Errors.TypeErrorOccurred(clip, typeof(Clip));

					if (!c.TryPrepare(clipValue.Data, out var error))
						return Errors.OSErrorOccurredWithMessage(error);

					return this;
				}

				public object IsPrepared(object clip) => clip is Clip clipValue && core != null && core.IsPrepared(clipValue.Data);

				/// <summary>
				/// Submits one play. Returns a stable playback, or blank for a bounded refusal: not open, a full
				/// command ring, or a "Reject" policy with no free voice. Callers that do not need control simply
				/// ignore the result.
				/// </summary>
				public object Play(object clip, object volume = null, object loop = null, object pan = null, object startMilliseconds = null)
				{
					// Argument checking precedes the bounded refusal: otherwise the same wrong argument is a TypeError
					// on an open output and a silent blank on a closed one.
					if (clip is not Clip clipValue)
						return Errors.TypeErrorOccurred(clip, typeof(Clip));

					if (core == null || core.Status != Engine.AudioOutputStatus.Open)
						return "";

					if (!TryLevel(volume == null ? 100L : volume, "Volume", out var volumeValue, out var failure))
						return failure;

					if (!TryPan(pan == null ? 0L : pan, out var panValue, out failure))
						return failure;

					var startMs = startMilliseconds == null ? 0.0 : startMilliseconds.Ad();

					if (!double.IsFinite(startMs) || startMs < 0)
						return Errors.ValueErrorOccurred("StartMilliseconds must be a finite number of 0 or more.", startMilliseconds);

					var data = clipValue.Data;

					if (data.DurationMilliseconds > 0 && startMs >= data.DurationMilliseconds)
						return Errors.ValueErrorOccurred($"StartMilliseconds {startMs} is at or past the clip.s {data.DurationMilliseconds:0.##} ms duration.");

					var control = core.TryPlay(data, volumeValue, panValue, loop.Ab(), startMs, out var error);

					if (control == null)
						return error.IsNullOrEmpty() ? "" : Errors.OSErrorOccurredWithMessage(error);

					return Playback.Wrap(service, control, core, data);
				}

				/// <summary>Silences every voice on this output immediately. Idempotent, and valid while closed.</summary>
				public object StopAll()
				{
					core?.StopAll();
					return this;
				}

				/// <summary>Reversible: retires the native stream but keeps the prepared clip set for a later open.</summary>
				public object Close()
				{
					if (core != null)
					{
						core.Close();
						service.UnregisterOutput(core);
					}

					return this;
				}

				/// <summary>
				/// Releases the native stream when a script drops the output without closing it. Without this a
				/// forgotten output keeps its stream and one of the sixteen open slots until the script exits, and
				/// the error naming the cap would tell the user to close an object they no longer hold.
				/// </summary>
				public override object __Delete()
				{
					_ = Dispose();
					return base.__Delete();
				}

				/// <summary>Terminal. Releases caches and native state; later operations raise.</summary>
				public object Dispose()
				{
					if (core != null)
					{
						service.UnregisterOutput(core);
						core.Dispose();
					}

					return DefaultObject;
				}

				public override string ToString() => "Audio.Output";
			}

			/// <summary>
			/// One sound in flight. Its identity is stable: it never rebinds to a later sound, so a reference held
			/// after the voice was stolen controls nothing rather than controlling a stranger.
			/// </summary>
			public class Playback : KeysharpObject
			{
				internal Engine.AudioPlaybackControl control;
				internal Engine.AudioOutputCore owner;
				internal Engine.AudioClipData source;

				/// <summary>The service this sound was admitted under, captured for the same reason Device captures it.</summary>
				internal Engine.AudioService service;

				public Playback(params object[] args) : base(args) { }

				public override object __New(params object[] args)
					=> Errors.ErrorOccurred("Audio.Playback is produced by Audio.Play() or Audio.Output.Play(), not constructed.");

				internal static Playback Wrap(Engine.AudioService owner, Engine.AudioPlaybackControl c, Engine.AudioOutputCore o, Engine.AudioClipData s)
					=> new (null) { service = owner, control = c, owner = o, source = s };

				/// <summary>The device this sound was admitted on. It never tracks a later default change.</summary>
				public object Device
				{
					get
					{
						var backend = service.Backend;
						return owner != null && backend is { IsAvailable: true } && backend.TryGetDevice(owner.BoundDeviceId, out var d)
							   ? Ks.Audio.Device.Wrap(service, d)
							   : "";
					}
				}

				/// <summary>"Queued", "Playing", "Paused", "Ended", "Stopped", "Stolen", "DeviceLost" or "Error".</summary>
				public string Status
				{
					get
					{
						if (control == null)
							return "Error";

						if (control.IsTerminal)
							return control.TerminalState switch
							{
								Engine.AudioPlaybackState.Ended => "Ended",
								Engine.AudioPlaybackState.Stolen => "Stolen",
								Engine.AudioPlaybackState.DeviceLost => "DeviceLost",
								Engine.AudioPlaybackState.Error => "Error",
								_ => "Stopped",
							};

						if (control.IsPaused)
							return "Paused";

						return control.IsAdmitted ? "Playing" : "Queued";
					}
				}

				/// <summary>True while this sound is playing or queued. False while paused or after it ends.</summary>
				public object IsPlaying
				{
					get
					{
						var s = Status;
						return s == "Playing" || s == "Queued";
					}
				}

				public object DurationMilliseconds => source?.DurationMilliseconds ?? (object)"";

				/// <summary>The play position in milliseconds. Writable while the sound is live.</summary>
				public object PositionMilliseconds
				{
					get
					{
						if (control == null || owner == null || owner.SampleRate <= 0)
							return "";

						return control.PositionFrames * 1000.0 / owner.SampleRate;
					}

					set
					{
						if (control == null || control.IsTerminal)
						{
							_ = Errors.ValueErrorOccurred("This playback has ended.");
							return;
						}

						var ms = value.Ad();
						var duration = source?.DurationMilliseconds ?? 0;

						if (!double.IsFinite(ms) || ms < 0 || (duration > 0 && ms >= duration))
						{
							_ = Errors.ValueErrorOccurred($"PositionMilliseconds must be a finite number from 0 up to but not including {duration:0.##}.", value);
							return;
						}

						// The mixer's frames are in the output's format, not the clip's, so the seek converts here.
						var rate = owner?.SampleRate ?? source?.SampleRate ?? 0;
						_ = control.SetSeek((long)(ms * rate / 1000.0));
					}
				}

				public object Volume
				{
					get => Level(control?.VolumeValue ?? 1f);

					set
					{
						if (TryLevel(value, "Volume", out var scalar, out _))
							_ = control?.SetVolume(scalar);
					}
				}

				public object Pan
				{
					get => Level(control?.PanValue ?? 0f);

					set
					{
						if (TryPan(value, out var pan, out _))
							_ = control?.SetPan(pan);
					}
				}

				public object Loop
				{
					get => control != null && control.Looping;
					set => control?.SetLoop(value.Ab());
				}

				public object Mute
				{
					get => control != null && control.Muted;
					set => control?.SetMute(value.Ab());
				}

				/// <summary>This voice's own peak after its gain and pan, or blank before any frame was rendered.</summary>
				public object Peak => control == null || !control.IsAdmitted ? "" : Level(control.Peak);

				/// <summary>Pauses, holding the position and the voice. Returns the resulting paused state.</summary>
				public object Pause()
				{
					return control != null && control.SetPaused(true);
				}

				public object Resume()
				{
					_ = control?.SetPaused(false);
					return this;
				}

				/// <summary>Ends this sound. Idempotent and harmless after it already ended.</summary>
				public object Stop()
				{
					_ = control?.TryFinish(Engine.AudioPlaybackState.Stopped);
					return DefaultObject;
				}

				public override string ToString() => "Audio.Playback";
			}
		}
	}
}
