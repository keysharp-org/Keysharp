using Engine = Keysharp.Internals.Audio;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Audio
		{
			/// <summary>
			/// One capture, from a microphone or from what an output device is playing. A recorder is configured
			/// first and started second, so handlers can be registered before any frame arrives and an immediate
			/// permission or device failure is observable.
			/// </summary>
			public class Recorder : KeysharpObject
			{
				internal Engine.AudioRecorderCore core;
				internal Engine.AudioService service;
				internal Recording result;
				internal string configuredPath = "";
				internal Engine.AudioCaptureSource source;
				internal string deviceSelector = "";
				internal int rate = 48000;
				internal int channels = 1;
				internal string format = Engine.AudioFormats.Signed16;
				internal long maximumMs;
				internal long chunkMs = 100;

				public Recorder(params object[] args) : base(args) { }

				/// <summary>
				/// Configures a recorder without touching the device. Every bound is checked here, so a bad option
				/// is a ValueError before anything is opened. Source defaults to "Microphone" and SampleFormat to
				/// "Signed16"; their tokens are matched without regard to case.
				/// </summary>
				public object __New(object source = null, object path = null, object device = null,
									object sampleRate = null, object channels = null, object sampleFormat = null,
									object chunkMilliseconds = null, object maximumDurationMilliseconds = null)
				{
					service = Service;
					var sourceText = source.As();

					if (sourceText.Length == 0 || string.Equals(sourceText, "Microphone", StringComparison.OrdinalIgnoreCase))
						this.source = Engine.AudioCaptureSource.Microphone;
					else if (string.Equals(sourceText, "SystemOutput", StringComparison.OrdinalIgnoreCase))
						this.source = Engine.AudioCaptureSource.SystemOutput;
					else
						return Errors.ValueErrorOccurred($"Unknown Source \"{sourceText}\". Expected Microphone or SystemOutput.");

					configuredPath = path.As();

					if (configuredPath.Contains('\0'))
						return Errors.ValueErrorOccurred("Path must not contain an embedded NUL.");

					if (configuredPath.Length > 0)
					{
						if (!configuredPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
							return Errors.ValueErrorOccurred("Recording writes WAV, so Path must end in .wav.", configuredPath);

						try
						{
							configuredPath = System.IO.Path.GetFullPath(configuredPath);
						}
						catch (Exception ex)
						{
							return Errors.ValueErrorOccurred($"Path is not a valid file path: {ex.Message}", path);
						}
					}

					rate = (int)(sampleRate == null ? 48000L : sampleRate.Al());

					if (!Engine.AudioFormats.IsValidSampleRate(rate))
						return Errors.ValueErrorOccurred($"SampleRate must be from {Engine.AudioFormats.MinSampleRate} through {Engine.AudioFormats.MaxSampleRate}.", sampleRate);

					this.channels = (int)(channels == null ? 1L : channels.Al());

					if (!Engine.AudioFormats.IsValidChannels(this.channels))
						return Errors.ValueErrorOccurred("Channels must be 1 or 2.", channels);

					var formatToken = sampleFormat.As();

					if (formatToken.Length == 0)
						formatToken = Engine.AudioFormats.Signed16;

					if (!Engine.AudioFormats.TryResolve(formatToken, out format, out _))
						return Errors.ValueErrorOccurred($"Unknown SampleFormat \"{formatToken}\". Expected Unsigned8, Signed16, Signed24, Signed32 or Float32.");

					chunkMs = chunkMilliseconds == null ? 100L : chunkMilliseconds.Al();

					if (chunkMs < 10 || chunkMs > 1000)
						return Errors.ValueErrorOccurred("ChunkMilliseconds must be from 10 through 1000.", chunkMilliseconds);

					maximumMs = maximumDurationMilliseconds == null ? 0L : maximumDurationMilliseconds.Al();

					if (maximumMs < 0 || maximumMs > 86_400_000)
						return Errors.ValueErrorOccurred("MaximumDurationMilliseconds must be 0 for unbounded, or 1 through 86400000.", maximumDurationMilliseconds);

					if (device is Ks.Audio.Device || device.As().Length > 0)
					{
						var kind = this.source == Engine.AudioCaptureSource.Microphone ? Engine.AudioDeviceKind.Input : Engine.AudioDeviceKind.Output;

						if (!TryResolveDevice(device, kind, true, out var resolved, out var failure))
							return failure;

						deviceSelector = resolved.Id;
					}

					return DefaultObject;
				}

				/// <summary>"Microphone" or "SystemOutput".</summary>
				public string Source => source == Engine.AudioCaptureSource.SystemOutput ? "SystemOutput" : "Microphone";

				public object Device
				{
					get
					{
						var id = core?.DeviceId ?? deviceSelector;

						if (id.IsNullOrEmpty())
							return "";

						var backend = service?.Backend;
						return backend is { IsAvailable: true } && backend.TryGetDevice(id, out var d) ? Ks.Audio.Device.Wrap(service, d) : "";
					}
				}

				public object Path => configuredPath.Length > 0 ? configuredPath : "";

				/// <summary>"Ready", "Opening", "Recording", "Paused", "Finalizing", "Stopped", "DeviceLost", "Error" or "Disposed".</summary>
				public string Status => core?.Status ?? Engine.AudioRecorderStatus.Ready;

				public object DurationMilliseconds => core?.DurationMilliseconds ?? 0.0;

				public object MaximumDurationMilliseconds => maximumMs;

				public object SampleRate => (long)(core?.SampleRate ?? rate);

				public object Channels => (long)(core?.Channels ?? channels);

				/// <summary>"Unsigned8", "Signed16", "Signed24", "Signed32" or "Float32".</summary>
				public string SampleFormat => format;

				public object ChunkMilliseconds => chunkMs;

				/// <summary>How many captured chunks were dropped because the destination could not keep up. Nonzero
				/// means the recording has gaps, which is otherwise invisible.</summary>
				public object OverrunCount => core?.OverrunCount ?? 0L;

				/// <summary>The loudest captured sample so far, or blank before any frame arrived.</summary>
				public object Peak
				{
					get
					{
						var p = core?.Peak ?? -1;
						return p < 0 ? "" : Level(Math.Clamp(p, 0.0, 1.0));
					}
				}

				public object Error => core?.Error is string e && e.Length > 0 ? new Error(e) : (object)"";

				/// <summary>The finished recording, or blank until one exists and when none could be produced.</summary>
				public object Result
				{
					get
					{
						EnsureResult();
						return result ?? (object)"";
					}
				}

				/// <summary>
				/// Opens the device and begins capture. A permission denial or an absent device raises before
				/// anything is written, and leaves the recorder retryable.
				/// </summary>
				public object Start()
				{
					if (core != null)
						return Errors.ValueErrorOccurred("This recorder has already been started; construct another for a second recording.");

					var want = source == Engine.AudioCaptureSource.Microphone
							   ? Engine.AudioCapability.MicrophoneCapture
							   : Engine.AudioCapability.SystemAudioCapture;

					if (service == null || !service.Supports(want))
						return Errors.OSErrorOccurredWithMessage($"{Source} capture is unavailable: {service?.UnsupportedReason(want) ?? "this platform has no audio capture backend."}");

					// Capture consent is asked for strictly: only an explicit grant, or a platform where the
					// concept does not apply, reaches a native open.
					if (!TryEnsureCapturePermission(out var permissionFailure))
						return permissionFailure;

					var created = new Engine.AudioRecorderCore(service, source, configuredPath, deviceSelector, rate, channels, format, maximumMs, chunkMs);

					if (!service.TryRegisterRecorder(created))
						return Errors.OSErrorOccurredWithMessage($"This script already has {Engine.AudioService.MaxOpenRecorders} recorders running. Stop one before starting another.");

					if (!created.TryStart(out var failure))
					{
						service.UnregisterRecorder(created);
						return Errors.OSErrorOccurredWithMessage($"Cannot start {Source.ToLowerInvariant()} capture: {failure}");
					}

					core = created;
					return this;
				}

				private bool TryEnsureCapturePermission(out object failure)
				{
					failure = null;

					try
					{
						// The generic manager logs and continues for an unsupported scope; a recorder must not,
						// because starting a capture the user never allowed is the one outcome worth refusing.
						_ = service.Owner.Permissions.EnsureAudioCapture(operation: $"Audio.Recorder({Source})");
						return true;
					}
					catch (Exception ex)
					{
						failure = Errors.OSErrorOccurredWithMessage(
									  $"{Source} capture was denied: {ex.Message}. Grant microphone access to Keysharp in the system privacy settings, then retry.");
						return false;
					}
				}

				/// <summary>Holds media time without ending the recording. Returns the resulting paused state.</summary>
				public object Pause() => core != null && core.Pause(true);

				public object Resume()
				{
					_ = core?.Pause(false);
					return this;
				}

				/// <summary>
				/// Ends the recording and returns its result, or blank when no honest result could be produced.
				/// Repeating it returns the same outcome rather than starting a second finalization.
				/// </summary>
				public object Stop()
				{
					if (core == null)
						return Errors.ValueErrorOccurred("This recorder was never started, so there is nothing to stop.");

					core.Stop();
					service.UnregisterRecorder(core);
					EnsureResult();
					return result ?? (object)"";
				}

				private void EnsureResult()
				{
					if (result != null || core == null || !core.HasResult)
						return;

					result = Recording.Wrap(core);
				}

				/// <summary>Stops a capture a script dropped without stopping, for the reason Audio.Output gives.</summary>
				public override object __Delete()
				{
					_ = Dispose();
					return base.__Delete();
				}

				public object Dispose()
				{
					if (core != null)
					{
						service.UnregisterRecorder(core);
						core.Dispose();
					}

					EnsureResult();
					return DefaultObject;
				}

				public override string ToString() => "Audio.Recorder";
			}

			/// <summary>
			/// What a finished recording produced: exactly one of an in-memory clip or a file path, never both and
			/// never neither. A recorder that could not produce an honest result reports blank instead of this.
			/// </summary>
			public class Recording : KeysharpObject
			{
				internal Engine.AudioClipData clip;
				internal string path = "";
				internal double duration;
				internal long frames;
				internal int rate;
				internal int channels;
				internal string format = "";

				public Recording(params object[] args) : base(args) { }

				public override object __New(params object[] args)
					=> Errors.ErrorOccurred("Audio.Recording is produced by Audio.Recorder.Stop(), not constructed.");

				internal static Recording Wrap(Engine.AudioRecorderCore core) => new (null)
				{
					clip = core.ResultClip,
					path = core.ResultClip == null ? core.Path : "",
					duration = core.DurationMilliseconds,
					frames = core.FrameCount,
					rate = core.SampleRate,
					channels = core.Channels,
					format = core.ResultClip == null ? core.SampleFormat : Engine.AudioFormats.Float32,
				};

				/// <summary>The captured audio as a clip, or blank when the recording went to a file.</summary>
				public object Clip => clip == null ? "" : Ks.Audio.Clip.Wrap(clip);

				/// <summary>The file that was written, or blank when the recording was kept in memory.</summary>
				public object Path => path.Length > 0 ? path : "";

				public object DurationMilliseconds => duration;

				public object FrameCount => frames;

				public object SampleRate => (long)rate;

				public object Channels => (long)channels;

				/// <summary>"Float32" for an in-memory result; otherwise the recorder's configured SampleFormat.</summary>
				public string SampleFormat => format;

				public override string ToString() => "Audio.Recording";
			}
		}
	}
}
