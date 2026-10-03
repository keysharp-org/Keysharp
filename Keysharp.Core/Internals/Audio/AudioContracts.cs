namespace Keysharp.Internals.Audio
{
	/// <summary>Which direction an endpoint carries audio. A device has exactly one.</summary>
	internal enum AudioDeviceKind
	{
		Output,
		Input,
	}

	/// <summary>
	/// The distinct things a backend may or may not be able to do. Each one is probed and reported separately,
	/// because a host can enumerate devices without being able to open one, and can open one without exposing
	/// endpoint volume.
	/// </summary>
	internal enum AudioCapability
	{
		Playback,
		DeviceEnumeration,
		DeviceChange,
		EndpointVolume,
		DeviceRunning,
		NativeObject,
		MicrophoneCapture,
		SystemAudioCapture,
		Sessions,
		Metering,
		Decoding,
	}

	/// <summary>
	/// One device as the backend sees it. <paramref name="Id"/> is opaque, exact, case-sensitive and unique
	/// across both kinds; it is the only durable selector. Name is for humans and may repeat.
	/// </summary>
	internal readonly record struct AudioDeviceDescriptor(string Id, string Name, AudioDeviceKind Kind, bool IsDefault);

	/// <summary>The negotiated shape of a native stream. The managed boundary is always interleaved float32.</summary>
	internal readonly record struct AudioStreamFormat(int SampleRate, int Channels);

	/// <summary>What a caller asks of an output. A blank device id follows the current default.</summary>
	internal readonly record struct AudioOutputRequest(string DeviceId, double RequestedLatencyMilliseconds);

	/// <summary>
	/// The mixer, from the backend's point of view. Called on the backend's own render or event thread with a
	/// buffer of exactly the frames it wants; must fill every sample and must not allocate, block, take a
	/// contended lock, or call script code.
	/// </summary>
	internal interface IAudioRenderSource
	{
		void Fill(Span<float> interleavedFrames);

		/// <summary>
		/// The backend noticed the device run dry. Counted rather than acted on, and callable from the render
		/// thread, so it must stay bounded and allocation-free like <see cref="Fill"/>.
		/// </summary>
		void NoteUnderrun();
	}

	/// <summary>One open native output. Disposing it stops and releases the stream.</summary>
	internal interface IAudioOutputStream : IDisposable
	{
		AudioStreamFormat Format { get; }

		/// <summary>The backend's measured latency estimate, refreshed as the backend learns it. May be above or
		/// below what was requested, and is zero before the backend knows.</summary>
		double LatencyMilliseconds { get; }

		/// <summary>Set when the device went away under the stream, so the owner can transition rather than poll.</summary>
		bool IsDeviceLost { get; }

		void Start();
		void Stop();
	}

	/// <summary>An installed device-change notification. Disposing it unsubscribes.</summary>
	internal interface IAudioDeviceWatcher : IDisposable
	{
	}

	/// <summary>
	/// The whole per-OS surface the audio engine needs. Methods are Try*/return-bool so a partially capable
	/// backend degrades rather than throwing, matching <c>IPlatformServices</c>; a caller turns a false into an
	/// actionable OSError using <see cref="UnsupportedReason"/>. Every method is safe to call from the engine's
	/// management thread; none may be called from a render callback.
	/// </summary>
	internal interface IAudioBackend : IDisposable
	{
		/// <summary>Whether this backend loaded at all. False means the library or server is absent.</summary>
		bool IsAvailable { get; }

		/// <summary>The last native error on the calling thread, or zero when no native status is available.</summary>
		int LastError => 0;

		/// <summary>Whether one capability is usable right now. Cached; never performs a device open.</summary>
		bool Supports(AudioCapability capability);

		/// <summary>A concrete next action for the user, used verbatim inside the OSError a refused call raises.</summary>
		string UnsupportedReason(AudioCapability capability);

		/// <summary>Currently present, usable endpoints of one kind, in the backend's stable order.</summary>
		AudioDeviceDescriptor[] EnumerateDevices(AudioDeviceKind kind);

		AudioDeviceDescriptor[] EnumerateAllDevices() => [.. EnumerateDevices(AudioDeviceKind.Output), .. EnumerateDevices(AudioDeviceKind.Input)];

		bool TryGetDefaultDevice(AudioDeviceKind kind, out AudioDeviceDescriptor device);

		/// <summary>Resolves an exact opaque id. False when that device is no longer present.</summary>
		bool TryGetDevice(string id, out AudioDeviceDescriptor device);

		/// <summary>
		/// Opens one output. A blank device id in the request follows the current default. Returns false with a
		/// message for device absence and transient open failure; the caller decides whether that is an error.
		/// </summary>
		bool TryOpenOutput(in AudioOutputRequest request, IAudioRenderSource source, out IAudioOutputStream stream, out string error);

		/// <summary>Endpoint volume as the system mixer slider position from 0 through 1.</summary>
		bool TryGetVolume(AudioDeviceKind kind, string id, out double volume);

		bool TrySetVolume(AudioDeviceKind kind, string id, double volume);

		bool TryGetMute(AudioDeviceKind kind, string id, out bool mute);

		bool TrySetMute(AudioDeviceKind kind, string id, bool mute);

		/// <summary>
		/// Whether any application currently holds a live stream on this device. False from this method means the
		/// backend could not determine it, which the caller surfaces as blank rather than as a confident idle;
		/// a determined answer sets <paramref name="running"/> and returns true.
		/// </summary>
		bool TryGetIsRunning(AudioDeviceKind kind, string id, out bool running);

		/// <summary>The platform's own device object for the script escape hatch, or null where there is none.</summary>
		object GetNativeDeviceObject(AudioDeviceKind kind, string id);

		/// <summary>
		/// Installs a device-change notification. The sink is invoked on an arbitrary backend thread and must do
		/// bounded work only. Returns null where the environment exposes no notification.
		/// </summary>
		IAudioDeviceWatcher WatchDevices(Action sink);

		/// <summary>Opens one capture. Refused when the matching capture capability is unsupported.</summary>
		bool TryOpenInput(in AudioInputRequest request, IAudioCaptureSink sink, out IAudioInputStream stream, out string error);

		/// <summary>Every live session, or only those on one device when an id is given.</summary>
		AudioSessionDescriptor[] EnumerateSessions(string deviceId);

		bool TryRefreshSession(string sessionId, out AudioSessionDescriptor descriptor);

		bool TryGetSessionVolume(string sessionId, out double linearVolume);

		bool TrySetSessionVolume(string sessionId, double linearVolume);

		bool TryGetSessionMute(string sessionId, out bool mute);

		bool TrySetSessionMute(string sessionId, bool mute);

		object GetNativeSessionObject(string sessionId);

		/// <summary>Opens an observation of a device endpoint or, when <paramref name="isSession"/>, of one session.
		/// <paramref name="intervalMilliseconds"/> is the publication cadence the caller asked for; a backend whose
		/// meter is read on demand rather than published has nothing to apply it to.</summary>
		bool TryOpenMeter(string targetId, bool isSession, double intervalMilliseconds, out IAudioNativeMeter meter, out string error);

		/// <summary>
		/// The canonical lower-case extensions this host can decode beyond WAV, without the leading dot. Empty when
		/// no platform decoder initialized. Probed once and cached; never opens a file.
		/// </summary>
		string[] SupportedFormats { get; }

		/// <summary>
		/// Decodes a whole file into interleaved float32. Returns false with a human-readable reason for an
		/// unreadable file, an unsupported container, or a decoder the host does not have.
		/// </summary>
		bool TryDecodeFile(string path, out float[] samples, out int sampleRate, out int channels, out string error);
	}

	/// <summary>Where a recorder takes its frames from.</summary>
	internal enum AudioCaptureSource
	{
		Microphone,
		SystemOutput,
	}

	/// <summary>
	/// What a caller asks of an input stream. A blank device id uses the current default for the source.
	/// <c>ChunkMilliseconds</c> is how often the caller wants frames handed to its sink; a backend sizes its
	/// capture buffer from it, and may deliver more often when the device period is shorter.
	/// </summary>
	internal readonly record struct AudioInputRequest(string DeviceId, AudioCaptureSource Source, int SampleRate, int Channels, double ChunkMilliseconds);

	/// <summary>
	/// The recorder, from the backend's point of view. Called on the backend's capture thread with the frames it
	/// just received; it must copy what it needs and return promptly, and must not call script code.
	/// </summary>
	internal interface IAudioCaptureSink
	{
		void Write(ReadOnlySpan<float> interleavedFrames);
	}

	/// <summary>One open native input. Disposing it stops and releases the stream.</summary>
	internal interface IAudioInputStream : IDisposable
	{
		AudioStreamFormat Format { get; }
		bool IsDeviceLost { get; }
		void Start();
		void Stop();
	}

	/// <summary>A live native observation of a device or session level. Disposing it releases the observation.</summary>
	internal interface IAudioNativeMeter : IDisposable
	{
		/// <summary>The most recent peak as a linear 0..1 scalar, or negative before any observation.</summary>
		double Peak { get; }
	}

	/// <summary>One application audio session as the backend sees it.</summary>
	internal readonly record struct AudioSessionDescriptor(
		string Id,
		string DeviceId,
		AudioDeviceKind DeviceKind,
		long ProcessId,
		string ProcessName,
		string DisplayName,
		string State,
		bool IsSystemSounds,
		bool HasVolume,
		bool HasMute,
		bool HasMetering);
}
