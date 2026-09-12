using Engine = Keysharp.Internals.Audio;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// The cross-platform audio surface: polyphonic playback from files or memory, device discovery and
		/// endpoint control. Use as <c>#Import Ks { Audio }</c>.
		/// <para>
		/// <c>Audio</c> itself is the static front door and the container for its related types, reached as
		/// <c>Audio.Clip</c>, <c>Audio.Device</c>, <c>Audio.Output</c> and <c>Audio.Playback</c>. The AHK-compatible
		/// <c>Sound*</c> functions are unchanged and unrelated: they keep their fuzzy device selector and their
		/// monophonic file playback, while this class works from exactly identified devices and can overlap sounds.
		/// </para>
		/// </summary>
		[Experimental]
		public partial class Audio : KeysharpObject
		{
			public Audio(params object[] args) : base(args) { }

			/// <summary>There is one audio system per script, so this class wraps <em>the</em> thing and has no instances.</summary>
			public override object __New(params object[] args)
				=> Errors.ErrorOccurred("Audio has no instances; use its members directly, e.g. Audio.Play(\"beep.wav\").");

			// ---- shared helpers ---------------------------------------------------------------

			/// <summary>
			/// The calling script's audio service. Correct for a static entry point, which by definition runs on
			/// the script that called it, and it is the only place the current script is resolved. Every object
			/// this class hands out captures the service it was created from instead, so a later Script swap
			/// cannot make a Device or an Output start operating on a different script's backend.
			/// </summary>
			internal static Engine.AudioService Service => Script.TheScript.AudioService;

			/// <summary>Raises an OSError naming what the host is missing, which is the only honest answer for an
			/// absent backend. Used by every member that cannot degrade to a blank result.</summary>
			internal static object Unsupported(Engine.AudioCapability capability, string operation)
				=> Errors.OSErrorOccurredWithMessage($"{operation} is unavailable: {Service.UnsupportedReason(capability)}");

			internal static object Unsupported(Engine.AudioService svc, Engine.AudioCapability capability, string operation)
				=> Errors.OSErrorOccurredWithMessage($"{operation} is unavailable: {svc.UnsupportedReason(capability)}");

			/// <summary>Script levels are 0 through 100; the engine works in linear 0..1.</summary>
			internal static bool TryLevel(object value, string name, out float scalar, out object failure)
			{
				scalar = 0f;
				failure = null;
				var d = value.Ad();

				if (!double.IsFinite(d) || d < 0 || d > 100)
				{
					failure = Errors.ValueErrorOccurred($"{name} must be a finite number from 0 through 100.", value);
					return false;
				}

				scalar = (float)(d / 100.0);
				return true;
			}

			/// <summary>
			/// The 0-100 form of an internal linear scalar. The round trip through float32 turns a written 40
			/// into 40.00000059604645 on the way back, so the public boundary rounds well below anything audible
			/// and far below what a script can hear, which keeps a setter and its getter agreeing on the value.
			/// </summary>
			internal static double Level(double scalar) => Math.Round(scalar * 100.0, 4);

			internal static bool TryPan(object value, out float pan, out object failure)
			{
				pan = 0f;
				failure = null;
				var d = value.Ad();

				if (!double.IsFinite(d) || d < -100 || d > 100)
				{
					failure = Errors.ValueErrorOccurred("Pan must be a finite number from -100 through 100.", value);
					return false;
				}

				pan = (float)(d / 100.0);
				return true;
			}

			/// <summary>Resolves the one device selector every member accepts: a Device, an exact Id, an exact
			/// unique Name, or blank for the current default.</summary>
			internal static bool TryResolveDevice(object selector, Engine.AudioDeviceKind kind, bool allowBlank,
												  out Engine.AudioDeviceDescriptor device, out object failure)
			{
				device = default;
				failure = null;
				var backend = Service.Backend;

				if (backend == null || !backend.IsAvailable)
				{
					failure = Unsupported(Engine.AudioCapability.DeviceEnumeration, "Device selection");
					return false;
				}

				if (selector is Device d)
				{
					if (d.Kind != KindName(kind))
					{
						failure = Errors.ValueErrorOccurred($"That device is an {d.Kind} device, but an {KindName(kind)} device is required.");
						return false;
					}

					if (!backend.TryGetDevice(d.Id, out device))
					{
						failure = Errors.OSErrorOccurredWithMessage($"The device {d.Name} is no longer available.");
						return false;
					}

					return true;
				}

				var text = selector.As();

				if (text.Length == 0)
				{
					if (!allowBlank)
					{
						failure = Errors.ValueErrorOccurred("A device is required here.");
						return false;
					}

					if (!backend.TryGetDefaultDevice(kind, out device))
					{
						failure = Errors.OSErrorOccurredWithMessage($"This host has no default {KindName(kind).ToLowerInvariant()} device.");
						return false;
					}

					return true;
				}

				// An exact opaque id wins; ids are case-sensitive because they are backend identity.
				if (backend.TryGetDevice(text, out device) && device.Kind == kind)
					return true;

				var matches = backend.EnumerateDevices(kind)
							  .Where(x => string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase))
							  .ToArray();

				if (matches.Length == 1)
				{
					device = matches[0];
					return true;
				}

				failure = matches.Length == 0
						  ? Errors.OSErrorOccurredWithMessage($"No {KindName(kind).ToLowerInvariant()} device matches {text}.")
						  : Errors.ValueErrorOccurred($"The name {text} matches {matches.Length} devices; use an exact Id instead: "
													  + string.Join(", ", matches.Select(x => x.Id)));
				return false;
			}

			internal static string KindName(Engine.AudioDeviceKind kind) => kind == Engine.AudioDeviceKind.Input ? "Input" : "Output";

			internal static bool TryKind(object value, string fallback, bool allowAll, out Engine.AudioDeviceKind kind, out bool all, out object failure)
			{
				kind = Engine.AudioDeviceKind.Output;
				all = false;
				failure = null;
				var text = value.As();

				if (text.Length == 0)
					text = fallback;

				if (string.Equals(text, "Output", StringComparison.OrdinalIgnoreCase))
					return true;

				if (string.Equals(text, "Input", StringComparison.OrdinalIgnoreCase))
				{
					kind = Engine.AudioDeviceKind.Input;
					return true;
				}

				if (allowAll && string.Equals(text, "All", StringComparison.OrdinalIgnoreCase))
				{
					all = true;
					return true;
				}

				failure = Errors.ValueErrorOccurred($"Unknown Kind \"{text}\". Expected {(allowAll ? "Output, Input or All" : "Output or Input")}.");
				return false;
			}

			// ---- capability probes -------------------------------------------------------------

			/// <summary>Whether this host has a playback implementation at all. Says nothing about devices.</summary>
			public static object staticget_IsPlaybackSupported(object @this) => Service.Supports(Engine.AudioCapability.Playback);

			public static object staticget_IsDeviceChangeSupported(object @this) => Service.Supports(Engine.AudioCapability.DeviceChange);

			/// <summary>Whether the cached device view currently holds a usable output. A supported host with no
			/// speakers answers false here and true to <c>IsPlaybackSupported</c>.</summary>
			public static object staticget_IsOutputAvailable(object @this)
				=> Service.Backend is { IsAvailable: true } b && b.TryGetDefaultDevice(Engine.AudioDeviceKind.Output, out _);

			public static object staticget_IsInputAvailable(object @this)
				=> Service.Backend is { IsAvailable: true } b && b.TryGetDefaultDevice(Engine.AudioDeviceKind.Input, out _);

			/// <summary>A conservative decoder probe. It accepts a token or extension and never inspects a file.</summary>
			[Static]
			public static object IsFormatSupported(object @this, object Format)
			{
				var text = Format.As().TrimStart('.');

				// The managed WAV reader ships unconditionally; everything else depends on a codec the host
				// already has, so the answer is whatever that host's decoder actually initialized with.
				if (string.Equals(text, "Wav", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(text, "Wave", StringComparison.OrdinalIgnoreCase))
					return true;

				var backend = Service.Backend;

				if (backend == null || !Service.Supports(Engine.AudioCapability.Decoding))
					return false;

				foreach (var supported in backend.SupportedFormats)
					if (string.Equals(supported, text, StringComparison.OrdinalIgnoreCase))
						return true;

				return false;
			}

			// ---- clips --------------------------------------------------------------------------

			/// <summary>Decodes a file into a reusable immutable clip. Only WAV is decoded without a platform codec.</summary>
			[Static]
			public static object Load(object @this, object Path)
			{
				var path = Path.As();

				if (path.Length == 0 || path.Contains('\0'))
					return Errors.ValueErrorOccurred("Path must be a non-empty file path without embedded NUL.");

				string full;

				try
				{
					full = System.IO.Path.GetFullPath(path);
				}
				catch (Exception ex)
				{
					return Errors.ValueErrorOccurred($"Path is not a valid file path: {ex.Message}", path);
				}

				if (!File.Exists(full))
					return Errors.OSErrorOccurredWithMessage($"Cannot read {path}: the file does not exist.");

				// WAV is read by the managed parser on every host, so the common case never depends on a platform
				// codec. Anything else goes to whatever decoder this host already ships.
				if (full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
						|| full.EndsWith(".wave", StringComparison.OrdinalIgnoreCase))
				{
					byte[] bytes;

					try
					{
						bytes = File.ReadAllBytes(full);
					}
					catch (Exception ex)
					{
						return Errors.OSErrorOccurredWithMessage($"Cannot read {path}: {ex.Message}");
					}

					if (!Engine.WavCodec.TryDecode(bytes, out var wavSamples, out var wavRate, out var wavChannels, out var wavError))
						return Errors.OSErrorOccurredWithMessage($"Cannot decode {path}: {wavError}");

					return Clip.Wrap(new Engine.AudioClipData(wavSamples, wavRate, wavChannels, Engine.AudioFormats.Float32));
				}

				var decoder = Service.Backend;

				if (decoder == null || !Service.Supports(Engine.AudioCapability.Decoding) || decoder.SupportedFormats.Length == 0)
					return Errors.OSErrorOccurredWithMessage(
							   $"Cannot decode {path}: this host has no audio decoder beyond WAV. Convert the file to WAV, or check Audio.IsFormatSupported() first.");

				if (!decoder.TryDecodeFile(full, out var samples, out var rate, out var channels, out var error))
					return Errors.OSErrorOccurredWithMessage($"Cannot decode {path}: {error}");

				if (!Engine.AudioFormats.IsValidSampleRate(rate) || !Engine.AudioFormats.IsValidChannels(channels))
					return Errors.OSErrorOccurredWithMessage(
							   $"Cannot decode {path}: it decoded to {rate} Hz and {channels} channels, which is outside the supported range.");

				return Clip.Wrap(new Engine.AudioClipData(samples, rate, channels, Engine.AudioFormats.Float32));
			}

			/// <summary>
			/// Copies headerless PCM out of a script <c>Buffer</c> into an immutable clip. The copy is synchronous
			/// and complete, so the caller may reuse or free its buffer the moment this returns.
			/// SampleFormat is "Unsigned8", "Signed16" (the default), "Signed24", "Signed32" or "Float32",
			/// matched without regard to case.
			/// </summary>
			[Static]
			public static object FromPcm(object @this, object Data, object SampleRate, object Channels = null, object SampleFormat = null)
			{
				if (Data is not Buffer buffer)
					return Errors.TypeErrorOccurred(Data, typeof(Buffer));

				var rate = SampleRate.Al();
				var channels = Channels == null ? 1L : Channels.Al();
				var formatToken = SampleFormat.As();

				if (formatToken.Length == 0)
					formatToken = Engine.AudioFormats.Signed16;

				if (!Engine.AudioFormats.IsValidSampleRate(rate))
					return Errors.ValueErrorOccurred($"SampleRate must be from {Engine.AudioFormats.MinSampleRate} through {Engine.AudioFormats.MaxSampleRate}.", rate);

				if (!Engine.AudioFormats.IsValidChannels(channels))
					return Errors.ValueErrorOccurred("Channels must be 1 or 2.", channels);

				if (!Engine.AudioFormats.TryResolve(formatToken, out var canonical, out var bytesPerSample))
					return Errors.ValueErrorOccurred($"Unknown SampleFormat \"{formatToken}\". Expected Unsigned8, Signed16, Signed24, Signed32 or Float32.");

				// The size is checked before the bytes are read: an empty Buffer has no allocation behind it, so
				// reading it first would fail natively instead of raising the ValueError this contract promises.
				var frameSize = (int)channels * bytesPerSample;
				var byteCount = buffer.Size.Al();

				if (byteCount <= 0 || byteCount % frameSize != 0)
					return Errors.ValueErrorOccurred($"Data must hold a whole number of {frameSize}-byte frames; it holds {byteCount} bytes.");

				var raw = buffer.ToByteArray();

				if (raw.Length < byteCount)
					return Errors.ValueErrorOccurred($"Data reported {byteCount} bytes but yielded {raw.Length}.");

				var sampleCount = raw.Length / bytesPerSample;

				if (sampleCount * 4L > Engine.AudioFormats.MaxClipBytes)
					return Errors.ValueErrorOccurred($"The clip would exceed the {Engine.AudioFormats.MaxClipBytes / (1024 * 1024)} MiB per-clip limit.");

				var samples = new float[sampleCount];

				if (!Engine.AudioFormats.TryConvert(raw, canonical, samples, out var convertError))
					return Errors.ValueErrorOccurred(convertError);

				return Clip.Wrap(new Engine.AudioClipData(samples, (int)rate, (int)channels, canonical));
			}

			// ---- convenience playback -----------------------------------------------------------

			/// <summary>The absolute form of a path, or blank when it is not one this process can resolve.</summary>
			internal static string FullPathOrEmpty(string path)
			{
				if (path.Length == 0 || path.Contains('\0'))
					return "";

				try
				{
					return System.IO.Path.GetFullPath(path);
				}
				catch (Exception)
				{
					return "";
				}
			}

			/// <summary>
			/// Plays a clip or a file path through a hidden per-script output and returns a stable playback. Unlike
			/// <c>Audio.Output.Play</c> it raises rather than returning blank, because a one-line convenience call
			/// has nowhere to report a bounded refusal.
			/// </summary>
			[Static]
			public static object Play(object @this, object Source, object Volume = null, object Loop = null,
									  object Pan = null, object StartMilliseconds = null, object Device = null)
			{
				if (!Service.Supports(Engine.AudioCapability.Playback))
					return Unsupported(Engine.AudioCapability.Playback, "Audio.Play");

				Engine.AudioClipData data;

				if (Source is Clip clip)
				{
					data = clip.Data;
				}
				else
				{
					// A path played repeatedly is decoded once. Beyond saving the read and the decode, this keeps the
					// clip identity stable, which is what lets the output's prepared cache hit instead of growing an
					// entry per call until nothing can play at all.
					var full = FullPathOrEmpty(Source.As());

					if (full.Length > 0 && Service.TryGetDecoded(full, out var cached))
					{
						data = cached;
					}
					else
					{
						var loaded = Load(null, Source);

						if (loaded is not Clip loadedClip)
							return loaded;   // an error already surfaced, or was suppressed by OnError

						data = loadedClip.Data;

						if (full.Length > 0)
							Service.StoreDecoded(full, data);
					}
				}

				if (!TryLevel(Volume == null ? 100L : Volume, "Volume", out var volume, out var failure))
					return failure;

				if (!TryPan(Pan == null ? 0L : Pan, out var pan, out failure))
					return failure;

				var loop = Loop.Ab();
				var startMs = StartMilliseconds == null ? 0.0 : StartMilliseconds.Ad();

				if (!double.IsFinite(startMs) || startMs < 0)
					return Errors.ValueErrorOccurred("StartMilliseconds must be a finite number of 0 or more.", StartMilliseconds);

				string deviceId = "";

				if (Device is Device || Device.As().Length > 0)
				{
					if (!TryResolveDevice(Device, Engine.AudioDeviceKind.Output, true, out var resolved, out failure))
						return failure;

					deviceId = resolved.Id;
				}

				var core = Service.GetConvenienceOutput(deviceId, out var error);

				if (core == null)
					return Errors.OSErrorOccurredWithMessage($"Audio.Play could not open an output: {error}");

				if (data.DurationMilliseconds > 0 && startMs >= data.DurationMilliseconds)
					return Errors.ValueErrorOccurred($"StartMilliseconds {startMs} is at or past the clip.s {data.DurationMilliseconds:0.##} ms duration.");

				var control = core.TryPlay(data, volume, pan, loop, startMs, out var playError);

				if (control == null)
					return Errors.OSErrorOccurredWithMessage(playError.IsNullOrEmpty()
															 ? "Audio.Play could not start: the output refused the request."
															 : $"Audio.Play could not start: {playError}");

				return Playback.Wrap(Service, control, core, data);
			}

			// ---- devices -------------------------------------------------------------------------

			/// <summary>Every usable device of Kind "Output", "Input" or "All" (the default), matched without regard
			/// to case. Outputs precede inputs for "All".</summary>
			[Static]
			public static object Devices(object @this, object Kind = null)
			{
				if (!TryKind(Kind, "All", true, out var kind, out var all, out var failure))
					return failure;

				var result = new Array();
				var backend = Service.Backend;

				if (backend == null || !backend.IsAvailable)
					return result;

				if (all || kind == Engine.AudioDeviceKind.Output)
					foreach (var d in backend.EnumerateDevices(Engine.AudioDeviceKind.Output))
						_ = result.Push(Device.Wrap(Service, d));

				if (all || kind == Engine.AudioDeviceKind.Input)
					foreach (var d in backend.EnumerateDevices(Engine.AudioDeviceKind.Input))
						_ = result.Push(Device.Wrap(Service, d));

				return result;
			}

			/// <summary>The default device of Kind "Output" (the default) or "Input", matched without regard to case,
			/// or blank when the host has none.</summary>
			[Static]
			public static object DefaultDevice(object @this, object Kind = null)
			{
				if (!TryKind(Kind, "Output", false, out var kind, out _, out var failure))
					return failure;

				var backend = Service.Backend;
				return backend is { IsAvailable: true } && backend.TryGetDefaultDevice(kind, out var d) ? Device.Wrap(Service, d) : "";
			}
		}
	}
}
