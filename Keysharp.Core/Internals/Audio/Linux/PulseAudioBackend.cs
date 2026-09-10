#if LINUX
namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// The Linux audio backend, bound at run time to the stable C API in <c>libpulse.so.0</c>.
	/// <para>
	/// The library is opened with <see cref="NativeLibrary.TryLoad(string, out nint)"/> and every export is
	/// resolved with <see cref="NativeLibrary.TryGetExport(nint, string, out nint)"/>, because a missing client
	/// library or a missing sound server is an ordinary outcome that must degrade into
	/// <see cref="IsAvailable"/> = false plus an actionable reason rather than into a load exception. No
	/// <c>DllImportResolver</c> is installed here: <c>Keysharp.Core</c> already owns the single one.
	/// </para>
	/// <para>
	/// All work goes through one <c>pa_threaded_mainloop</c> and one <c>pa_context</c>. Management callers hold
	/// the mainloop lock across every <c>pa_*</c> call and wait on <c>pa_threaded_mainloop_wait</c>; Pulse
	/// callbacks already run on the mainloop thread with that lock held, so they only copy borrowed data and
	/// signal. The render write callback additionally allocates nothing and touches no managed lock.
	/// </para>
	/// </summary>
	internal sealed class PulseAudioBackend : IAudioBackend
	{
		// ---- vocabulary ------------------------------------------------------------------------

		private const string SinkPrefix = "sink:";
		private const string SourcePrefix = "source:";

		// Session ids carry their index space: a sink-input index and a source-output index are unrelated numbers.
		private const string SinkInputPrefix = "si:";
		private const string SourceOutputPrefix = "so:";

		/// <summary>Every sink publishes a source of this name carrying what the sink is playing.</summary>
		private const string MonitorSuffix = ".monitor";

		private const string SessionActive = "Active";
		private const string SessionInactive = "Inactive";

		// PA_INVALID_INDEX; here it means "this meter observes a whole device, not one session".
		private const uint InvalidIndex = 0xFFFFFFFF;

		// pa_context_state_t
		private const int ContextReady = 4;
		private const int ContextFailed = 5;
		private const int ContextTerminated = 6;

		// pa_operation_state_t
		private const int OperationRunning = 0;
		private const int OperationDone = 1;

		// pa_stream_state_t
		private const int StreamReady = 2;
		private const int StreamFailed = 3;
		private const int StreamTerminated = 4;

		// pa_sample_format_t
		private const int SampleFloat32Le = 5;

		// pa_seek_mode_t
		private const int SeekRelative = 0;

		// pa_stream_flags_t
		private const int StreamStartCorked = 0x0001;
		private const int StreamInterpolateTiming = 0x0002;
		private const int StreamAutoTimingUpdate = 0x0008;
		private const int StreamAdjustLatency = 0x2000;

		// PA_STREAM_PEAK_DETECT. Its value follows from the same enum the flags above come from: the documented
		// flags between PA_STREAM_INTERPOLATE_TIMING (0x0002) and PA_STREAM_ADJUST_LATENCY (0x2000) fill every bit
		// in between, which puts peak detection at 0x0800 and PA_STREAM_START_MUTED at 0x1000.
		private const int StreamPeakDetect = 0x0800;

		// pa_subscription_mask_t: sinks, sources, and server (default-device changes).
		private const uint SubscriptionMask = 0x0001 | 0x0002 | 0x0080;

		// pa_subscription_mask_t: sink-inputs and source-outputs, added to the standing subscription once sessions
		// are first used so a session change invalidates the cache instead of waiting out its lifetime.
		private const uint SessionSubscriptionMask = 0x0004 | 0x0008;

		// pa_subscription_event_type_t: the low nibble is the facility, and these two are the session facilities.
		private const uint SubscriptionFacilityMask = 0x000F;
		private const uint FacilitySinkInput = 2;
		private const uint FacilitySourceOutput = 3;

		// pa_sink_state_t / pa_source_state_t share these values; -1 is the invalid marker.
		private const int EndpointRunning = 0;
		private const int EndpointIdle = 1;
		private const int EndpointSuspended = 2;

		private const int MaxChannels = 2;
		private const int DefaultSampleRate = 48_000;

		/// <summary>
		/// A peak-detect stream emits one float per fragment, so its "sample rate" is the observation rate, and the
		/// cadence the caller asked for is exactly that rate. Clamped to the range the public interval admits, which
		/// is 20 through 1000 ms.
		/// </summary>
		private static int MeterRateFor(double intervalMilliseconds)
			=> (int)Math.Clamp(Math.Round(1000.0 / Math.Max(1.0, intervalMilliseconds)), 1, 50);

		private const double MinLatencyMs = 5.0;
		private const double MaxLatencyMs = 2000.0;
		private const double DefaultLatencyMs = 50.0;

		/// <summary>Device snapshots older than this are re-read; a subscription event invalidates them sooner.</summary>
		private const long SnapshotLifetimeMs = 500;

		/// <summary>A failed connect is retried no more often than this, so a server started later is picked up.</summary>
		private const long ConnectRetryMs = 3000;

		/// <summary>How long a connect may take before it is treated as a server that will not answer.</summary>
		private const long ConnectTimeoutMs = 5000;
		private const int ConnectPollMs = 5;

		// ---- struct offsets --------------------------------------------------------------------

		// pa_sink_info and pa_source_info are field-for-field identical up to and including `state`, so one set of
		// offsets serves both. Derived from introspect.h with sizeof(pa_sample_spec) == 12 (int + uint32 + uint8,
		// 4-aligned), sizeof(pa_channel_map) == 132 and sizeof(pa_cvolume) == 132 (uint8 + 4-aligned uint32[32]):
		//   LP64:  name 0, index 8, description 16, sample_spec 24, channel_map 36, owner_module 168,
		//          volume 172, mute 304, monitor_* 308/312, latency 320, driver 328, flags 336, proplist 344,
		//          configured_latency 352, base_volume 360, state 364.
		//   ILP32: name 0, index 4, description 8, sample_spec 12, channel_map 24, owner_module 156,
		//          volume 160, mute 292.
		// Every field listed for ILP32 precedes `configured_latency`, which is where i386 (uint64 aligned to 4)
		// and ARM EABI (aligned to 8) diverge, so `state` is only read when IntPtr.Size is 8. These trailing
		// fields have only ever been appended to, so the offsets hold for every libpulse from 0.9.15 onward.
		private static readonly bool Lp64 = IntPtr.Size == 8;
		private static readonly int InfoIndexOffset = Lp64 ? 8 : 4;
		private static readonly int InfoDescriptionOffset = Lp64 ? 16 : 8;
		private static readonly int InfoSampleSpecOffset = Lp64 ? 24 : 12;
		private static readonly int InfoVolumeOffset = Lp64 ? 172 : 160;
		private static readonly int InfoMuteOffset = Lp64 ? 304 : 292;
		private const int InfoStateOffset = 364;

		// pa_server_info: user_name, host_name, server_version, server_name, sample_spec, default_sink_name,
		// default_source_name, cookie, channel_map. The sample_spec at 4 pointers in forces the LP64 pad to 48.
		private static readonly int ServerDefaultSinkOffset = Lp64 ? 48 : 28;
		private static readonly int ServerDefaultSourceOffset = Lp64 ? 56 : 32;

		// pa_cvolume: uint8 channels, then a 4-aligned pa_volume_t[PA_CHANNELS_MAX].
		private const int CVolumeValuesOffset = 4;
		private const int CVolumeSize = 132;
		private const int MaxCVolumeChannels = 32;

		// pa_sink_input_info and pa_source_output_info, LP64 only, derived from introspect.h with the same field
		// sizes the endpoint offsets use (pa_sample_spec 12, pa_channel_map 132, pa_cvolume 132):
		//   pa_sink_input_info:    index 0, name 8, owner_module 16, client 20, sink 24, sample_spec 28,
		//                          channel_map 40, volume 172, buffer_usec 304, sink_usec 312,
		//                          resample_method 320, driver 328, mute 336, proplist 344, corked 352,
		//                          has_volume 356, volume_writable 360.
		//   pa_source_output_info: index 0, name 8, owner_module 16, client 20, source 24, sample_spec 28,
		//                          channel_map 40, buffer_usec 176, source_usec 184, resample_method 192,
		//                          driver 200, proplist 208, corked 216, volume 220, mute 352, has_volume 356,
		//                          volume_writable 360.
		// Both structs put a uint64 after a 4-aligned run, which is exactly where i386 (uint64 aligned to 4) and
		// ARM EABI (aligned to 8) disagree, so a 32-bit process reports no sessions at all rather than reading a
		// guessed offset; a wrong proplist offset would be a dereferenced garbage pointer, not a wrong number.
		// corked, has_volume, volume_writable, and the whole source-output volume/mute block arrived in PulseAudio
		// 1.0, so they are read only when the loaded library reports major version 1 or above: an older struct
		// simply ends before them.
		private const int SessionIndexOffset = 0;
		private const int SessionNameOffset = 8;
		private const int SessionDeviceOffset = 24;
		private const int SinkInputVolumeOffset = 172;
		private const int SinkInputMuteOffset = 336;
		private const int SinkInputProplistOffset = 344;
		private const int SinkInputCorkedOffset = 352;
		private const int SinkInputHasVolumeOffset = 356;
		private const int SinkInputVolumeWritableOffset = 360;
		private const int SourceOutputProplistOffset = 208;
		private const int SourceOutputCorkedOffset = 216;
		private const int SourceOutputVolumeOffset = 220;
		private const int SourceOutputMuteOffset = 352;
		private const int SourceOutputHasVolumeOffset = 356;
		private const int SourceOutputVolumeWritableOffset = 360;

		// ---- instance state --------------------------------------------------------------------

		private readonly object sync = new ();
		private readonly List<PulseOutputStream> streams = [];
		private readonly List<PulseInputStream> inputs = [];
		private readonly List<PulseMeter> meters = [];
		private readonly List<PaEndpoint> scratch = [];
		private readonly List<PaSession> scratchSessions = [];

		// Rooted for as long as native code can reach them: the context and stream objects hold raw pointers to
		// the thunks these produce, and a collected delegate would leave the mainloop calling freed code.
		private readonly ContextNotifyCb onContextState;
		private readonly SinkInfoCb onSinkInfo;
		private readonly SourceInfoCb onSourceInfo;
		private readonly ServerInfoCb onServerInfo;
		private readonly ContextSuccessCb onSuccess;
		private readonly ContextSubscribeCb onSubscribe;
		private readonly SinkInputInfoCb onSinkInputInfo;
		private readonly SourceOutputInfoCb onSourceOutputInfo;

		private nint mainloop;
		private nint context;
		private nint volumeBlock;

		// The three proplist keys, allocated once because the info callbacks look them up on the mainloop thread.
		private nint keyProcessId;
		private nint keyProcessBinary;
		private nint keyApplicationName;

		private int probeState;             // 0 unknown, 1 usable, 2 permanently unusable
		private string probeReason;
		private long nextConnectAttempt;
		private bool disposed;

		private PaEndpoint[] sinks = [];
		private PaEndpoint[] sources = [];
		private string defaultSinkName = "";
		private string defaultSourceName = "";
		private long snapshotStamp;
		private int snapshotDirty = 1;

		// Watchers are published as a whole array so the subscription callback can fan out without a lock.
		private Action[] watchers = [];

		// The session view, rebuilt whenever a session event or the snapshot lifetime says it is stale.
		private PaSession[] sessionOrder = [];
		private Dictionary<string, PaSession> sessionsById = new (StringComparer.Ordinal);
		private Dictionary<ulong, SessionSlot> sessionSlots = new ();
		private long sessionStamp;
		private int sessionsDirty = 1;

		// Every id minted names one server connection and one admission of one index, so neither a reconnect nor a
		// recycled index can make an old id name a different stream.
		private long connectionGeneration;
		private long nextAdmission;
		private uint desiredMask = SubscriptionMask;

		// Written only by callbacks, read only by the management caller that started the operation and is
		// serialised by <see cref="sync"/>; the mainloop lock orders the two.
		private int scratchOutcome;
		private int scratchSuccess;
		private PaEndpoint scratchSingle;
		private PaSession scratchSession;
		private string scratchDefaultSink;
		private string scratchDefaultSource;

		internal PulseAudioBackend()
		{
			onContextState = OnContextState;
			onSinkInfo = OnSinkInfo;
			onSourceInfo = OnSourceInfo;
			onServerInfo = OnServerInfo;
			onSuccess = OnSuccess;
			onSubscribe = OnSubscribe;
			onSinkInputInfo = OnSinkInputInfo;
			onSourceOutputInfo = OnSourceOutputInfo;
		}

		// ---- IAudioBackend ---------------------------------------------------------------------

		public bool IsAvailable
		{
			get
			{
				lock (sync)
					return EnsureReady(out _);
			}
		}

		public bool Supports(AudioCapability capability)
		{
			// The escape hatch has no Pulse equivalent, so it is refused without touching the server.
			if (capability == AudioCapability.NativeObject)
				return false;

			lock (sync)
			{
				if (!EnsureReady(out _))
					return false;

				return capability switch
				{
					AudioCapability.Playback => Pa.StreamNew != null,
					AudioCapability.DeviceRunning => Lp64,
					AudioCapability.MicrophoneCapture => EnsureCaptureUsable(AudioCaptureSource.Microphone, out _),
					AudioCapability.SystemAudioCapture => EnsureCaptureUsable(AudioCaptureSource.SystemOutput, out _),
					AudioCapability.Sessions => EnsureSessionsUsable(out _),
					AudioCapability.Metering => EnsureMeteringUsable(out _),
					AudioCapability.Decoding => SupportedFormats.Length > 0,
					_ => true,
				};
			}
		}

		public string UnsupportedReason(AudioCapability capability)
		{
			if (capability == AudioCapability.NativeObject)
				return "PulseAudio exposes no per-device object; Clr.Wrap over an Audio.Device is available on Windows only.";

			lock (sync)
			{
				if (!EnsureReady(out var reason))
					return reason;

				if (capability == AudioCapability.DeviceRunning && !Lp64)
					return $"Reading a PulseAudio endpoint's running state needs a 64-bit process; this one is {IntPtr.Size * 8}-bit.";

				switch (capability)
				{
					case AudioCapability.MicrophoneCapture:
						_ = EnsureCaptureUsable(AudioCaptureSource.Microphone, out var micReason);
						return micReason;

					case AudioCapability.SystemAudioCapture:
						_ = EnsureCaptureUsable(AudioCaptureSource.SystemOutput, out var loopReason);
						return loopReason;

					case AudioCapability.Sessions:
						_ = EnsureSessionsUsable(out var sessionReason);
						return sessionReason;

					case AudioCapability.Metering:
						_ = EnsureMeteringUsable(out var meterReason);
						return meterReason;

					default:
						return "";
				}
			}
		}

		public AudioDeviceDescriptor[] EnumerateDevices(AudioDeviceKind kind)
		{
			lock (sync)
			{
				if (!RefreshSnapshot())
					return [];

				var list = kind == AudioDeviceKind.Output ? sinks : sources;
				var result = new AudioDeviceDescriptor[list.Length];

				for (var i = 0; i < list.Length; i++)
					result[i] = Describe(list[i]);

				return result;
			}
		}

		public bool TryGetDefaultDevice(AudioDeviceKind kind, out AudioDeviceDescriptor device)
		{
			device = default;

			lock (sync)
			{
				if (!RefreshSnapshot())
					return false;

				var endpoint = FindDefault(kind);

				if (endpoint == null)
					return false;

				device = Describe(endpoint);
				return true;
			}
		}

		public bool TryGetDevice(string id, out AudioDeviceDescriptor device)
		{
			device = default;

			if (!TrySplitId(id, out var kind, out var name))
				return false;

			lock (sync)
			{
				if (!RefreshSnapshot())
					return false;

				var endpoint = Find(kind, name);

				if (endpoint == null)
					return false;

				device = Describe(endpoint);
				return true;
			}
		}

		public bool TryOpenOutput(in AudioOutputRequest request, IAudioRenderSource source, out IAudioOutputStream stream, out string error)
		{
			stream = null;
			error = "";

			if (source == null)
			{
				error = "No render source was supplied for the output.";
				return false;
			}

			lock (sync)
			{
				if (!EnsureReady(out var reason))
				{
					error = reason;
					return false;
				}

				if (Pa.StreamNew == null)
				{
					error = "This libpulse.so.0 does not export the playback API; install a PulseAudio 0.9.15 or newer client library.";
					return false;
				}

				// A blank id follows the current default: the stream is connected with a null device name so the
				// server keeps moving it, and the format is negotiated against whatever the default sink is now.
				var followDefault = string.IsNullOrEmpty(request.DeviceId);
				string deviceName = null;

				if (!followDefault)
				{
					if (!TrySplitId(request.DeviceId, out var kind, out var name) || kind != AudioDeviceKind.Output)
					{
						error = $"'{request.DeviceId}' is not a PulseAudio output device id.";
						return false;
					}

					deviceName = name;
				}

				_ = RefreshSnapshot();
				var target = followDefault ? FindDefault(AudioDeviceKind.Output) : Find(AudioDeviceKind.Output, deviceName);

				if (!followDefault && target == null)
				{
					error = $"Output device '{request.DeviceId}' is not present.";
					return false;
				}

				var rate = target != null && AudioFormats.IsValidSampleRate(target.Rate) ? target.Rate : DefaultSampleRate;
				var channels = target != null ? Math.Clamp(target.Channels, AudioFormats.MinChannels, MaxChannels) : MaxChannels;
				var latency = request.RequestedLatencyMilliseconds > 0
							  ? Math.Clamp(request.RequestedLatencyMilliseconds, MinLatencyMs, MaxLatencyMs)
							  : DefaultLatencyMs;
				var opened = new PulseOutputStream(this, source, new AudioStreamFormat(rate, channels));

				if (!opened.TryConnect(deviceName, latency, out error))
				{
					opened.Dispose();
					return false;
				}

				streams.Add(opened);
				stream = opened;
				return true;
			}
		}

		public bool TryGetVolume(AudioDeviceKind kind, string id, out double volume)
		{
			volume = 0;

			lock (sync)
			{
				if (!TryReadEndpoint(kind, id, out var endpoint))
					return false;

				// The public boundary is 0..1; a Pulse endpoint may legitimately sit above PA_VOLUME_NORM, which is
				// reported as full rather than written back down.
				volume = Math.Clamp(endpoint.Volume, 0.0, 1.0);
				return true;
			}
		}

		public bool TrySetVolume(AudioDeviceKind kind, string id, double volume)
		{
			lock (sync)
			{
				if (!TryReadEndpoint(kind, id, out var endpoint))
					return false;

				if (Pa.SwVolumeFromLinear == null)
					return false;

				var channels = endpoint.Channels > 0 && endpoint.Channels <= MaxCVolumeChannels ? endpoint.Channels : 2;
				var raw = Pa.SwVolumeFromLinear(Math.Clamp(volume, 0.0, 1.0));
				var block = EnsureVolumeBlock();

				if (block == 0)
					return false;

				Marshal.WriteByte(block, (byte)channels);

				for (var i = 0; i < channels; i++)
					Marshal.WriteInt32(block, CVolumeValuesOffset + (i * 4), unchecked((int)raw));

				var setter = endpoint.Kind == AudioDeviceKind.Output ? Pa.SetSinkVolumeByIndex : Pa.SetSourceVolumeByIndex;
				var ok = RunLocked(() =>
				{
					scratchSuccess = 0;
					return RunOperation(setter(context, endpoint.Index, block, onSuccess, 0)) && scratchSuccess != 0;
				});
				Volatile.Write(ref snapshotDirty, 1);
				return ok;
			}
		}

		public bool TryGetMute(AudioDeviceKind kind, string id, out bool mute)
		{
			mute = false;

			lock (sync)
			{
				if (!TryReadEndpoint(kind, id, out var endpoint))
					return false;

				mute = endpoint.Mute;
				return true;
			}
		}

		public bool TrySetMute(AudioDeviceKind kind, string id, bool mute)
		{
			lock (sync)
			{
				if (!TryReadEndpoint(kind, id, out var endpoint))
					return false;

				var setter = endpoint.Kind == AudioDeviceKind.Output ? Pa.SetSinkMuteByIndex : Pa.SetSourceMuteByIndex;
				var ok = RunLocked(() =>
				{
					scratchSuccess = 0;
					return RunOperation(setter(context, endpoint.Index, mute ? 1 : 0, onSuccess, 0)) && scratchSuccess != 0;
				});
				Volatile.Write(ref snapshotDirty, 1);
				return ok;
			}
		}

		public bool TryGetIsRunning(AudioDeviceKind kind, string id, out bool running)
		{
			running = false;

			// The `state` field sits past the point where the 32-bit i386 and ARM struct layouts diverge, so on a
			// 32-bit process the honest answer is "cannot determine" rather than a guessed offset.
			if (!Lp64)
				return false;

			lock (sync)
			{
				if (!TryReadEndpoint(kind, id, out var endpoint))
					return false;

				switch (endpoint.State)
				{
					case EndpointRunning:
						running = true;
						return true;

					case EndpointIdle:
					case EndpointSuspended:
						running = false;
						return true;

					default:
						return false;   // PA_SINK_INVALID_STATE, or a value this binding does not recognise.
				}
			}
		}

		/// <summary>Always null: a Pulse endpoint is an index and a name, with no object of comparable reach.</summary>
		public object GetNativeDeviceObject(AudioDeviceKind kind, string id) => null;

		public IAudioDeviceWatcher WatchDevices(Action sink)
		{
			if (sink == null)
				return null;

			lock (sync)
			{
				if (!EnsureReady(out _) || Pa.Subscribe == null)
					return null;

				var next = new Action[watchers.Length + 1];
				Array.Copy(watchers, next, watchers.Length);
				next[watchers.Length] = sink;
				Volatile.Write(ref watchers, next);
				return new PulseDeviceWatcher(this, sink);
			}
		}

		public void Dispose()
		{
			lock (sync)
			{
				if (disposed)
					return;

				disposed = true;
				var open = streams.ToArray();
				var recording = inputs.ToArray();
				var observing = meters.ToArray();
				streams.Clear();
				inputs.Clear();
				meters.Clear();

				foreach (var s in open)
					s.Dispose();

				foreach (var s in recording)
					s.Dispose();

				foreach (var m in observing)
					m.Dispose();

				Volatile.Write(ref watchers, Array.Empty<Action>());
				ResetSessionCache();

				if (mainloop != 0)
				{
					if (context != 0)
					{
						Pa.ThreadedMainloopLock(mainloop);
						Pa.SetSubscribeCallback(context, null, 0);
						Pa.SetContextStateCallback(context, null, 0);
						Pa.ContextDisconnect(context);
						Pa.ContextUnref(context);
						context = 0;
						Pa.ThreadedMainloopUnlock(mainloop);
					}

					// Stopping before freeing is what makes the callbacks quiescent, which is the only point at
					// which the rooted delegates above stop being reachable from native code.
					Pa.ThreadedMainloopStop(mainloop);
					Pa.ThreadedMainloopFree(mainloop);
					mainloop = 0;
				}

				if (volumeBlock != 0)
				{
					Marshal.FreeHGlobal(volumeBlock);
					volumeBlock = 0;
				}

				FreeSessionKeys();
			}
		}

		// ---- readiness -------------------------------------------------------------------------

		/// <summary>
		/// Loads the library once, then connects the context, retrying a failed connect on a bounded interval so a
		/// server that starts after the script does is still picked up. Never throws.
		/// </summary>
		private bool EnsureReady(out string reason)
		{
			reason = "";

			if (disposed)
			{
				reason = "The audio backend has been shut down.";
				return false;
			}

			if (probeState == 2)
			{
				reason = probeReason;
				return false;
			}

			if (!Pa.TryLoad(out var loadReason))
			{
				probeState = 2;
				probeReason = loadReason;
				reason = loadReason;
				return false;
			}

			if (context != 0 && Pa.ContextGetState(context) == ContextReady)
			{
				probeState = 1;
				return true;
			}

			var now = Environment.TickCount64;

			if (probeState == 1 && now < nextConnectAttempt)
			{
				reason = string.IsNullOrEmpty(probeReason) ? "The PulseAudio connection is not ready." : probeReason;
				return false;
			}

			nextConnectAttempt = now + ConnectRetryMs;

			if (!TryConnectContext(out reason))
			{
				probeState = 1;   // The library is fine; only the server is missing, so keep retrying.
				probeReason = reason;
				return false;
			}

			probeState = 1;
			probeReason = "";
			Volatile.Write(ref snapshotDirty, 1);
			return true;
		}

		private bool TryConnectContext(out string reason)
		{
			reason = "";
			TeardownContext();

			if (mainloop == 0)
			{
				mainloop = Pa.ThreadedMainloopNew();

				if (mainloop == 0)
				{
					reason = "PulseAudio could not create its client mainloop.";
					return false;
				}

				if (Pa.ThreadedMainloopStart(mainloop) < 0)
				{
					Pa.ThreadedMainloopFree(mainloop);
					mainloop = 0;
					reason = "PulseAudio could not start its client mainloop thread.";
					return false;
				}
			}

			var name = Marshal.StringToCoTaskMemUTF8("Keysharp");
			Pa.ThreadedMainloopLock(mainloop);

			try
			{
				context = Pa.ContextNew(Pa.ThreadedMainloopGetApi(mainloop), name);

				if (context == 0)
				{
					reason = "PulseAudio could not create a client context.";
					return false;
				}

				Pa.SetContextStateCallback(context, onContextState, 0);

				// No local-socket precheck: libpulse itself resolves PULSE_SERVER, the client configuration, and
				// Unix or TCP addresses, and only it can report which of those failed.
				if (Pa.ContextConnect(context, 0, 0, 0) < 0)
				{
					reason = ServerReason();
					TeardownContextLocked();
					return false;
				}

				int state;

				// Bounded by polling rather than by pa_threaded_mainloop_wait: a server that accepts the socket and
				// then stalls in AUTHORIZING signals nothing, and an unbounded wait here parks the calling script
				// thread for good. The lock is released around the sleep so the mainloop can make progress.
				var deadline = Environment.TickCount64 + ConnectTimeoutMs;

				while ((state = Pa.ContextGetState(context)) != ContextReady && state != ContextFailed && state != ContextTerminated)
				{
					if (Environment.TickCount64 >= deadline)
						break;

					Pa.ThreadedMainloopUnlock(mainloop);
					Thread.Sleep(ConnectPollMs);
					Pa.ThreadedMainloopLock(mainloop);
				}

				if (state != ContextReady)
				{
					reason = ServerReason();
					TeardownContextLocked();
					return false;
				}

				// Every id handed out named the connection that has just been replaced, so the session view starts
				// empty and the next enumeration mints ids nothing can confuse with the old ones.
				connectionGeneration++;
				ResetSessionCache();

				// Subscribing unconditionally keeps the device snapshot honest even when no script watcher exists.
				if (Pa.Subscribe != null)
				{
					Pa.SetSubscribeCallback(context, onSubscribe, 0);
					scratchSuccess = 0;
					_ = RunOperation(Pa.Subscribe(context, desiredMask, onSuccess, 0));
				}

				return true;
			}
			catch (Exception ex)
			{
				reason = $"PulseAudio initialization failed: {ex.Message}";
				return false;
			}
			finally
			{
				Pa.ThreadedMainloopUnlock(mainloop);
				Marshal.FreeCoTaskMem(name);
			}
		}

		private string ServerReason()
		{
			var detail = "";

			if (context != 0 && Pa.ContextErrno != null && Pa.StrError != null)
			{
				var text = Marshal.PtrToStringUTF8(Pa.StrError(Pa.ContextErrno(context)));

				if (!string.IsNullOrEmpty(text))
					detail = $" ({text})";
			}

			return $"No PulseAudio-compatible sound server is reachable{detail}. Start pulseaudio or pipewire-pulse for this session.";
		}

		private void TeardownContext()
		{
			if (context == 0 || mainloop == 0)
			{
				context = 0;
				return;
			}

			Pa.ThreadedMainloopLock(mainloop);
			TeardownContextLocked();
			Pa.ThreadedMainloopUnlock(mainloop);
		}

		private void TeardownContextLocked()
		{
			if (context == 0)
				return;

			Pa.SetSubscribeCallback(context, null, 0);
			Pa.SetContextStateCallback(context, null, 0);
			Pa.ContextDisconnect(context);
			Pa.ContextUnref(context);
			context = 0;
		}

		// ---- mainloop helpers ------------------------------------------------------------------

		internal nint Mainloop => mainloop;

		internal nint Context => context;

		internal void LockMainloop() => Pa.ThreadedMainloopLock(mainloop);

		internal void UnlockMainloop() => Pa.ThreadedMainloopUnlock(mainloop);

		/// <summary>
		/// Releases the mainloop lock for a moment instead of waiting to be signalled. Used where the thing being
		/// waited for may never signal at all, so the caller needs its own deadline to remain answerable.
		/// </summary>
		internal void PollWait(int milliseconds)
		{
			Pa.ThreadedMainloopUnlock(mainloop);
			Thread.Sleep(milliseconds);
			Pa.ThreadedMainloopLock(mainloop);
		}

		internal void Signal() => Pa.ThreadedMainloopSignal(mainloop, 0);

		internal void Forget(PulseOutputStream stream)
		{
			lock (sync)
				_ = streams.Remove(stream);
		}

		/// <summary>Runs one management body with the mainloop locked; the body performs its own operation waits.</summary>
		private bool RunLocked(Func<bool> body)
		{
			Pa.ThreadedMainloopLock(mainloop);

			try
			{
				return body();
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				Pa.ThreadedMainloopUnlock(mainloop);
			}
		}

		/// <summary>
		/// Releases an operation whose completion nothing needs to observe. Corking carries no result the caller
		/// acts on, and it is issued with no callback, so nothing would ever signal the mainloop; waiting on it
		/// would park the caller until some unrelated Pulse event happened along.
		/// </summary>
		internal void DetachOperation(nint operation)
		{
			if (operation != 0)
				Pa.OperationUnref(operation);
		}

		/// <summary>
		/// Waits out one pa_operation. Called with the mainloop locked; a context that dies mid-flight breaks the
		/// wait so a vanished server cannot park the caller forever.
		/// </summary>
		internal bool RunOperation(nint operation)
		{
			if (operation == 0)
				return false;

			while (Pa.OperationGetState(operation) == OperationRunning)
			{
				if (Pa.ContextGetState(context) != ContextReady)
					break;

				Pa.ThreadedMainloopWait(mainloop);
			}

			var done = Pa.OperationGetState(operation) == OperationDone;
			Pa.OperationUnref(operation);
			return done;
		}

		private nint EnsureVolumeBlock()
		{
			if (volumeBlock == 0)
			{
				volumeBlock = Marshal.AllocHGlobal(CVolumeSize);

				if (volumeBlock != 0)
				{
					unsafe
					{
						new Span<byte>((void*)volumeBlock, CVolumeSize).Clear();
					}
				}
			}

			return volumeBlock;
		}

		// ---- snapshots and lookups -------------------------------------------------------------

		private bool RefreshSnapshot()
		{
			if (!EnsureReady(out _))
				return false;

			var stale = Volatile.Read(ref snapshotDirty) != 0
						|| Environment.TickCount64 - snapshotStamp > SnapshotLifetimeMs;

			if (!stale)
				return true;

			var ok = RunLocked(() =>
			{
				scratchDefaultSink = "";
				scratchDefaultSource = "";
				scratchOutcome = 0;
				var served = RunOperation(Pa.GetServerInfo(context, onServerInfo, 0));
				scratch.Clear();
				scratchOutcome = 0;
				var gotSinks = RunOperation(Pa.GetSinkInfoList(context, onSinkInfo, 0)) && scratchOutcome > 0;
				var newSinks = scratch.ToArray();
				scratch.Clear();
				scratchOutcome = 0;
				var gotSources = RunOperation(Pa.GetSourceInfoList(context, onSourceInfo, 0)) && scratchOutcome > 0;
				var newSources = scratch.ToArray();
				scratch.Clear();

				if (!gotSinks || !gotSources)
					return false;

				sinks = newSinks;
				sources = newSources;

				if (served)
				{
					defaultSinkName = scratchDefaultSink ?? "";
					defaultSourceName = scratchDefaultSource ?? "";
				}

				return true;
			});

			if (ok)
			{
				snapshotStamp = Environment.TickCount64;
				Volatile.Write(ref snapshotDirty, 0);
			}

			return ok;
		}

		private PaEndpoint Find(AudioDeviceKind kind, string name)
		{
			var list = kind == AudioDeviceKind.Output ? sinks : sources;

			// Pulse names are case-sensitive byte strings and the contract calls the id exact, so the comparison is too.
			foreach (var e in list)
				if (string.Equals(e.Name, name, StringComparison.Ordinal))
					return e;

			return null;
		}

		private PaEndpoint FindDefault(AudioDeviceKind kind)
		{
			var wanted = kind == AudioDeviceKind.Output ? defaultSinkName : defaultSourceName;
			var list = kind == AudioDeviceKind.Output ? sinks : sources;
			var match = string.IsNullOrEmpty(wanted) ? null : Find(kind, wanted);
			return match ?? (list.Length != 0 ? list[0] : null);
		}

		/// <summary>
		/// Resolves an id to a freshly read endpoint. The index comes from the cached snapshot but the values come
		/// from a targeted read, whose returned name is checked because Pulse reuses indexes.
		/// </summary>
		private bool TryReadEndpoint(AudioDeviceKind kind, string id, out PaEndpoint endpoint)
		{
			endpoint = null;

			if (!RefreshSnapshot())
				return false;

			PaEndpoint cached;

			if (string.IsNullOrEmpty(id))
			{
				cached = FindDefault(kind);
			}
			else
			{
				if (!TrySplitId(id, out var idKind, out var name) || idKind != kind)
					return false;

				cached = Find(kind, name);
			}

			if (cached == null)
				return false;

			var read = RunLocked(() =>
			{
				scratch.Clear();
				scratchSingle = null;
				scratchOutcome = 0;
				var op = kind == AudioDeviceKind.Output
						 ? Pa.GetSinkInfoByIndex(context, cached.Index, onSinkInfo, 0)
						 : Pa.GetSourceInfoByIndex(context, cached.Index, onSourceInfo, 0);
				return RunOperation(op) && scratchOutcome > 0 && scratchSingle != null;
			});

			if (!read)
			{
				Volatile.Write(ref snapshotDirty, 1);
				return false;
			}

			var fresh = scratchSingle;
			scratchSingle = null;

			if (!string.Equals(fresh.Name, cached.Name, StringComparison.Ordinal))
			{
				// The index was recycled onto a different endpoint; the snapshot is what is wrong, not the caller.
				Volatile.Write(ref snapshotDirty, 1);
				return false;
			}

			endpoint = fresh;
			return true;
		}

		private AudioDeviceDescriptor Describe(PaEndpoint e)
		{
			var isDefault = string.Equals(e.Name,
										  e.Kind == AudioDeviceKind.Output ? defaultSinkName : defaultSourceName,
										  StringComparison.Ordinal);
			return new AudioDeviceDescriptor(MakeId(e.Kind, e.Name),
											 string.IsNullOrEmpty(e.Description) ? e.Name : e.Description,
											 e.Kind,
											 isDefault);
		}

		/// <summary>Ids carry their kind so one string is unique across sinks and sources, which share a name space.</summary>
		private static string MakeId(AudioDeviceKind kind, string name)
			=> (kind == AudioDeviceKind.Output ? SinkPrefix : SourcePrefix) + name;

		private static bool TrySplitId(string id, out AudioDeviceKind kind, out string name)
		{
			kind = AudioDeviceKind.Output;
			name = null;

			if (string.IsNullOrEmpty(id))
				return false;

			if (id.StartsWith(SinkPrefix, StringComparison.Ordinal))
			{
				kind = AudioDeviceKind.Output;
				name = id.Substring(SinkPrefix.Length);
			}
			else if (id.StartsWith(SourcePrefix, StringComparison.Ordinal))
			{
				kind = AudioDeviceKind.Input;
				name = id.Substring(SourcePrefix.Length);
			}
			else
			{
				return false;
			}

			return name.Length != 0;
		}

		// ---- native callbacks ------------------------------------------------------------------
		// All of these run on the mainloop thread with its lock already held: they copy borrowed data and signal,
		// and never lock, wait, or re-enter Pulse.

		private void OnContextState(nint c, nint userData)
		{
			try
			{
				Pa.ThreadedMainloopSignal(mainloop, 0);
			}
			catch (Exception)
			{
			}
		}

		private void OnSinkInfo(nint c, nint info, int eol, nint userData) => OnEndpointInfo(info, eol, AudioDeviceKind.Output);

		private void OnSourceInfo(nint c, nint info, int eol, nint userData) => OnEndpointInfo(info, eol, AudioDeviceKind.Input);

		private void OnEndpointInfo(nint info, int eol, AudioDeviceKind kind)
		{
			try
			{
				if (eol != 0)
				{
					scratchOutcome = eol > 0 ? 1 : -1;
					Pa.ThreadedMainloopSignal(mainloop, 0);
					return;
				}

				if (info == 0)
					return;

				var endpoint = ReadEndpoint(info, kind);

				if (endpoint == null)
					return;

				scratchSingle = endpoint;
				scratch.Add(endpoint);
			}
			catch (Exception)
			{
				scratchOutcome = -1;
				Pa.ThreadedMainloopSignal(mainloop, 0);
			}
		}

		private void OnServerInfo(nint c, nint info, nint userData)
		{
			try
			{
				if (info != 0)
				{
					scratchDefaultSink = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, ServerDefaultSinkOffset)) ?? "";
					scratchDefaultSource = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, ServerDefaultSourceOffset)) ?? "";
				}
			}
			catch (Exception)
			{
			}

			Pa.ThreadedMainloopSignal(mainloop, 0);
		}

		private void OnSuccess(nint c, int success, nint userData)
		{
			scratchSuccess = success;
			Pa.ThreadedMainloopSignal(mainloop, 0);
		}

		private void OnSubscribe(nint c, uint eventType, uint index, nint userData)
		{
			// Bounded fan-out only: mark the affected cache stale and hand each device sink its cheap notification.
			// A stream appearing changes no device, so a session event only invalidates the session cache.
			var facility = eventType & SubscriptionFacilityMask;

			if (facility == FacilitySinkInput || facility == FacilitySourceOutput)
			{
				Volatile.Write(ref sessionsDirty, 1);
				return;
			}

			Volatile.Write(ref snapshotDirty, 1);
			var current = Volatile.Read(ref watchers);

			for (var i = 0; i < current.Length; i++)
			{
				try
				{
					current[i]();
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>Copies one borrowed pa_sink_info/pa_source_info; every pointer it holds dies with the callback.</summary>
		private PaEndpoint ReadEndpoint(nint info, AudioDeviceKind kind)
		{
			var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, 0));

			if (string.IsNullOrEmpty(name))
				return null;

			var linear = ReadLinearVolume(info, InfoVolumeOffset, out _);
			return new PaEndpoint
			{
				Kind = kind,
				Index = unchecked((uint)Marshal.ReadInt32(info, InfoIndexOffset)),
				Name = name,
				Description = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, InfoDescriptionOffset)) ?? "",
				Rate = Marshal.ReadInt32(info, InfoSampleSpecOffset + 4),
				Channels = Marshal.ReadByte(info, InfoSampleSpecOffset + 8),
				Volume = linear,
				Mute = Marshal.ReadInt32(info, InfoMuteOffset) != 0,
				State = Lp64 ? Marshal.ReadInt32(info, InfoStateOffset) : -1,
			};
		}

		/// <summary>
		/// Reduces one borrowed pa_cvolume to its loudest channel as a linear scalar, which is all a single number
		/// can honestly say about a per-channel volume. An unreadable block reports zero channels.
		/// </summary>
		private static double ReadLinearVolume(nint info, int cvolumeOffset, out int channels)
		{
			channels = Marshal.ReadByte(info, cvolumeOffset);

			if (channels <= 0 || channels > MaxCVolumeChannels || Pa.SwVolumeToLinear == null)
			{
				channels = 0;
				return 0.0;
			}

			var loudest = 0u;

			for (var i = 0; i < channels; i++)
			{
				var raw = unchecked((uint)Marshal.ReadInt32(info, cvolumeOffset + CVolumeValuesOffset + (i * 4)));

				if (raw > loudest)
					loudest = raw;
			}

			return Pa.SwVolumeToLinear(loudest);
		}

		internal void RemoveWatcher(Action sink)
		{
			lock (sync)
			{
				var current = watchers;
				var index = Array.IndexOf(current, sink);

				if (index < 0)
					return;

				var next = new Action[current.Length - 1];
				Array.Copy(current, next, index);
				Array.Copy(current, index + 1, next, index, current.Length - index - 1);
				Volatile.Write(ref watchers, next);
			}
		}

		// ---- capture ---------------------------------------------------------------

		public bool TryOpenInput(in AudioInputRequest request, IAudioCaptureSink sink, out IAudioInputStream stream, out string error)
		{
			stream = null;
			error = "";

			if (sink == null)
			{
				error = "No capture sink was supplied for the input.";
				return false;
			}

			lock (sync)
			{
				if (!EnsureCaptureUsable(request.Source, out error))
					return false;

				if (!TryResolveCaptureSource(request, out var sourceName, out var target, out error))
					return false;

				// The request wins when it asks for something valid; otherwise the endpoint's own shape is the
				// least resampled thing this stream can be, and the defaults are the last resort.
				var rate = AudioFormats.IsValidSampleRate(request.SampleRate)
						   ? request.SampleRate
						   : (target != null && AudioFormats.IsValidSampleRate(target.Rate) ? target.Rate : DefaultSampleRate);
				var channels = AudioFormats.IsValidChannels(request.Channels)
							   ? request.Channels
							   : (target != null ? Math.Clamp(target.Channels, AudioFormats.MinChannels, MaxChannels) : AudioFormats.MinChannels);
				var opened = new PulseInputStream(this, sink, new AudioStreamFormat(rate, channels));

				if (!opened.TryConnect(sourceName, Math.Clamp(request.ChunkMilliseconds, 10.0, 1000.0), out error))
				{
					opened.Dispose();
					return false;
				}

				inputs.Add(opened);
				stream = opened;
				return true;
			}
		}

		/// <summary>
		/// Both capture sources reach the server as a record stream on a source, so they need the same exports;
		/// system output additionally needs the server to publish monitor sources at all, which the cached device
		/// snapshot already answers without opening anything.
		/// </summary>
		private bool EnsureCaptureUsable(AudioCaptureSource source, out string reason)
		{
			if (!EnsureReady(out reason))
				return false;

			if (!CaptureExportsPresent())
			{
				reason = "This libpulse.so.0 does not export the recording API (pa_stream_connect_record); install a PulseAudio 0.9.15 or newer client library.";
				return false;
			}

			if (source == AudioCaptureSource.SystemOutput && RefreshSnapshot() && !HasMonitorSource())
			{
				reason = "This sound server publishes no monitor source, so what an output device is playing cannot be recorded.";
				return false;
			}

			reason = "";
			return true;
		}

		private static bool CaptureExportsPresent()
			=> Pa.StreamNew != null && Pa.StreamConnectRecord != null && Pa.StreamSetReadCallback != null
			   && Pa.StreamPeek != null && Pa.StreamDrop != null;

		private bool HasMonitorSource()
		{
			foreach (var s in sources)
				if (s.Name.EndsWith(MonitorSuffix, StringComparison.Ordinal))
					return true;

			return false;
		}

		/// <summary>
		/// Maps a request onto the single source that carries those frames. A microphone reads an input endpoint
		/// directly; system output reads the chosen sink's monitor source, which the server names after the sink.
		/// </summary>
		private bool TryResolveCaptureSource(in AudioInputRequest request, out string sourceName, out PaEndpoint target, out string error)
		{
			sourceName = null;
			target = null;
			error = "";
			var followDefault = string.IsNullOrEmpty(request.DeviceId);
			_ = RefreshSnapshot();

			if (request.Source == AudioCaptureSource.Microphone)
			{
				if (followDefault)
				{
					target = FindDefault(AudioDeviceKind.Input);

					if (target == null)
					{
						error = "This sound server reports no input device to record from.";
						return false;
					}
				}
				else
				{
					if (!TrySplitId(request.DeviceId, out var kind, out var name) || kind != AudioDeviceKind.Input)
					{
						error = $"'{request.DeviceId}' is not a PulseAudio input device id.";
						return false;
					}

					target = Find(AudioDeviceKind.Input, name);

					if (target == null)
					{
						error = $"Input device '{request.DeviceId}' is not present.";
						return false;
					}
				}

				sourceName = target.Name;
				return true;
			}

			PaEndpoint sink;

			if (followDefault)
			{
				sink = FindDefault(AudioDeviceKind.Output);

				if (sink == null)
				{
					error = "This sound server reports no output device whose playback could be recorded.";
					return false;
				}
			}
			else
			{
				if (!TrySplitId(request.DeviceId, out var kind, out var name) || kind != AudioDeviceKind.Output)
				{
					error = $"'{request.DeviceId}' is not a PulseAudio output device id; recording system output records what an output device plays.";
					return false;
				}

				sink = Find(AudioDeviceKind.Output, name);

				if (sink == null)
				{
					error = $"Output device '{request.DeviceId}' is not present.";
					return false;
				}
			}

			// A monitor is published as an ordinary source, so its absence from the snapshot is the real answer.
			sourceName = sink.Name + MonitorSuffix;
			target = Find(AudioDeviceKind.Input, sourceName);

			if (target == null)
			{
				error = $"Output device '{MakeId(AudioDeviceKind.Output, sink.Name)}' has no monitor source, so what it plays cannot be recorded.";
				return false;
			}

			return true;
		}

		// ---- application sessions ---------------------------------------------------------------

		public AudioSessionDescriptor[] EnumerateSessions(string deviceId)
		{
			lock (sync)
			{
				if (!RefreshSessions())
					return [];

				var all = sessionOrder;

				if (string.IsNullOrEmpty(deviceId))
				{
					var everything = new AudioSessionDescriptor[all.Length];

					for (var i = 0; i < all.Length; i++)
						everything[i] = DescribeSession(all[i]);

					return everything;
				}

				if (!TrySplitId(deviceId, out _, out _))
					return [];

				var matched = new List<AudioSessionDescriptor>();

				foreach (var s in all)
					if (string.Equals(s.DeviceId, deviceId, StringComparison.Ordinal))
						matched.Add(DescribeSession(s));

				return [.. matched];
			}
		}

		public bool TryRefreshSession(string sessionId, out AudioSessionDescriptor descriptor)
		{
			descriptor = default;

			lock (sync)
			{
				if (!TryResolveSession(sessionId, out var session) || !TryReadSessionFresh(session))
					return false;

				descriptor = DescribeSession(session);
				return true;
			}
		}

		public bool TryGetSessionVolume(string sessionId, out double linearVolume)
		{
			linearVolume = 0;

			lock (sync)
			{
				if (!TryResolveSession(sessionId, out var session) || !TryReadSessionFresh(session) || !session.HasVolume)
					return false;

				// The boundary is 0..1; a stream may legitimately sit above PA_VOLUME_NORM, which is reported as
				// full rather than written back down.
				linearVolume = Math.Clamp(session.Volume, 0.0, 1.0);
				return true;
			}
		}

		public bool TrySetSessionVolume(string sessionId, double linearVolume)
		{
			lock (sync)
			{
				if (!TryResolveSession(sessionId, out var session) || !TryReadSessionFresh(session))
					return false;

				var setter = session.IsSinkInput ? Pa.SetSinkInputVolume : Pa.SetSourceOutputVolume;

				if (!session.HasVolume || setter == null || Pa.SwVolumeFromLinear == null || session.VolumeChannels <= 0)
					return false;

				var block = EnsureVolumeBlock();

				if (block == 0)
					return false;

				var raw = Pa.SwVolumeFromLinear(Math.Clamp(linearVolume, 0.0, 1.0));
				Marshal.WriteByte(block, (byte)session.VolumeChannels);

				for (var i = 0; i < session.VolumeChannels; i++)
					Marshal.WriteInt32(block, CVolumeValuesOffset + (i * 4), unchecked((int)raw));

				var index = session.Index;
				var ok = RunLocked(() =>
				{
					scratchSuccess = 0;
					return RunOperation(setter(context, index, block, onSuccess, 0)) && scratchSuccess != 0;
				});
				Volatile.Write(ref sessionsDirty, 1);
				return ok;
			}
		}

		public bool TryGetSessionMute(string sessionId, out bool mute)
		{
			mute = false;

			lock (sync)
			{
				if (!TryResolveSession(sessionId, out var session) || !TryReadSessionFresh(session) || !session.HasMute)
					return false;

				mute = session.Mute;
				return true;
			}
		}

		public bool TrySetSessionMute(string sessionId, bool mute)
		{
			lock (sync)
			{
				if (!TryResolveSession(sessionId, out var session) || !TryReadSessionFresh(session))
					return false;

				var setter = session.IsSinkInput ? Pa.SetSinkInputMute : Pa.SetSourceOutputMute;

				if (!session.HasMute || setter == null)
					return false;

				var index = session.Index;
				var ok = RunLocked(() =>
				{
					scratchSuccess = 0;
					return RunOperation(setter(context, index, mute ? 1 : 0, onSuccess, 0)) && scratchSuccess != 0;
				});
				Volatile.Write(ref sessionsDirty, 1);
				return ok;
			}
		}

		/// <summary>Always null: a Pulse stream is an index and a proplist, with no object of comparable reach.</summary>
		public object GetNativeSessionObject(string sessionId) => null;

		/// <summary>
		/// Sessions need the introspection exports, the proplist reader, and a 64-bit process, because the two
		/// info structs are only laid out unambiguously there. A 32-bit process reports no sessions rather than
		/// reading a field at a guessed offset.
		/// </summary>
		private bool EnsureSessionsUsable(out string reason)
		{
			if (!EnsureReady(out reason))
				return false;

			if (!Lp64)
			{
				reason = $"Reading PulseAudio application sessions needs a 64-bit process; this one is {IntPtr.Size * 8}-bit.";
				return false;
			}

			if (Pa.GetSinkInputInfoList == null || Pa.GetSinkInputInfo == null || Pa.ProplistGets == null)
			{
				reason = "This libpulse.so.0 does not export the per-application stream API (pa_context_get_sink_input_info_list); install a PulseAudio 0.9.15 or newer client library.";
				return false;
			}

			reason = "";
			return true;
		}

		/// <summary>
		/// Widens the standing subscription to the two session facilities the first time sessions are used, so a
		/// stream appearing or changing invalidates the cache immediately instead of at the next lifetime expiry.
		/// </summary>
		private bool EnsureSessionSubscription()
		{
			if ((desiredMask & SessionSubscriptionMask) == SessionSubscriptionMask)
				return true;

			if (Pa.Subscribe == null || context == 0)
				return false;

			var mask = desiredMask | SessionSubscriptionMask;
			var ok = RunLocked(() =>
			{
				scratchSuccess = 0;
				return RunOperation(Pa.Subscribe(context, mask, onSuccess, 0)) && scratchSuccess != 0;
			});

			if (ok)
			{
				desiredMask = mask;
				Volatile.Write(ref sessionsDirty, 1);
			}

			return ok;
		}

		/// <summary>
		/// Rebuilds the whole session view when an event or the snapshot lifetime says it is stale. Sink-inputs are
		/// playback sessions and source-outputs are capture sessions; they are read into one ordered list, playback
		/// first, and each is bound to an admission generation before an id names it.
		/// </summary>
		private bool RefreshSessions()
		{
			if (!EnsureSessionsUsable(out _))
				return false;

			_ = EnsureSessionSubscription();
			var stale = Volatile.Read(ref sessionsDirty) != 0
						|| Environment.TickCount64 - sessionStamp > SnapshotLifetimeMs;

			if (!stale)
				return true;

			// The device snapshot names the sink or source each stream sits on, and the keys are what the info
			// callbacks read the process metadata with, so both are readied before the operations start.
			if (!RefreshSnapshot() || !EnsureSessionKeys())
				return false;

			PaSession[] playback = null;
			PaSession[] capture = null;
			var ok = RunLocked(() =>
			{
				scratchSessions.Clear();
				scratchOutcome = 0;

				if (!RunOperation(Pa.GetSinkInputInfoList(context, onSinkInputInfo, 0)) || scratchOutcome <= 0)
				{
					scratchSessions.Clear();
					return false;
				}

				playback = [.. scratchSessions];
				scratchSessions.Clear();
				scratchOutcome = 0;

				// Capture sessions are optional: a library without that export still reports playback sessions.
				if (Pa.GetSourceOutputInfoList != null)
				{
					if (!RunOperation(Pa.GetSourceOutputInfoList(context, onSourceOutputInfo, 0)) || scratchOutcome <= 0)
					{
						scratchSessions.Clear();
						return false;
					}

					capture = [.. scratchSessions];
				}

				scratchSessions.Clear();
				return true;
			});

			if (!ok)
				return false;

			RepublishSessions(playback ?? [], capture ?? []);
			sessionStamp = Environment.TickCount64;
			Volatile.Write(ref sessionsDirty, 0);
			return true;
		}

		/// <summary>
		/// Publishes one enumeration as the new session view. A slot keeps its admission generation only while the
		/// stream at that index still has the same identity; a slot that this enumeration did not see is dropped,
		/// so an index the server recycles is admitted afresh and no earlier id can name the new stream.
		/// </summary>
		private void RepublishSessions(PaSession[] playback, PaSession[] capture)
		{
			var slots = new Dictionary<ulong, SessionSlot>(playback.Length + capture.Length);
			var byId = new Dictionary<string, PaSession>(playback.Length + capture.Length, StringComparer.Ordinal);
			var ordered = new List<PaSession>(playback.Length + capture.Length);

			for (var pass = 0; pass < 2; pass++)
			{
				foreach (var s in pass == 0 ? playback : capture)
				{
					var key = SlotKey(s.IsSinkInput, s.Index);

					if (slots.ContainsKey(key))
						continue;   // one index cannot name two streams in one enumeration.

					s.Generation = sessionSlots.TryGetValue(key, out var known)
								   && string.Equals(known.Identity, s.Identity, StringComparison.Ordinal)
								   ? known.Generation
								   : ++nextAdmission;
					s.Id = MakeSessionId(s);
					ResolveSessionDevice(s);
					slots[key] = new SessionSlot(s.Generation, s.Identity);
					byId[s.Id] = s;
					ordered.Add(s);
				}
			}

			sessionSlots = slots;
			sessionsById = byId;
			sessionOrder = [.. ordered];
		}

		private void ResetSessionCache()
		{
			sessionSlots = new ();
			sessionsById = new Dictionary<string, PaSession>(StringComparer.Ordinal);
			sessionOrder = [];
			sessionStamp = 0;
			Volatile.Write(ref sessionsDirty, 1);
		}

		/// <summary>Resolves an id to its cached session, refusing anything minted against an older connection.</summary>
		private bool TryResolveSession(string sessionId, out PaSession session)
		{
			session = null;

			if (!TrySplitSessionId(sessionId, out _, out var generation, out _, out _))
				return false;

			if (generation != connectionGeneration)
				return false;

			if (!RefreshSessions())
				return false;

			return sessionsById.TryGetValue(sessionId, out session) && session != null;
		}

		/// <summary>
		/// Re-reads one session by index and confirms it is still the same stream before its values are used or a
		/// control is written. A changed identity means the index was recycled: the caller is refused and the cache
		/// is invalidated so the next enumeration admits the new stream under a new id.
		/// </summary>
		private bool TryReadSessionFresh(PaSession session)
		{
			if (!EnsureSessionKeys())
				return false;

			var sinkInput = session.IsSinkInput;
			var index = session.Index;
			var read = RunLocked(() =>
			{
				scratchSessions.Clear();
				scratchSession = null;
				scratchOutcome = 0;
				var op = sinkInput
						 ? Pa.GetSinkInputInfo(context, index, onSinkInputInfo, 0)
						 : (Pa.GetSourceOutputInfo != null ? Pa.GetSourceOutputInfo(context, index, onSourceOutputInfo, 0) : (nint)0);
				var done = RunOperation(op) && scratchOutcome > 0 && scratchSession != null;
				scratchSessions.Clear();
				return done;
			});
			var fresh = scratchSession;
			scratchSession = null;

			if (!read || fresh == null || !string.Equals(fresh.Identity, session.Identity, StringComparison.Ordinal))
			{
				Volatile.Write(ref sessionsDirty, 1);
				return false;
			}

			session.Adopt(fresh);
			ResolveSessionDevice(session);
			return true;
		}

		/// <summary>Names the sink or source this stream currently sits on; a stream the server moves reports the new one.</summary>
		private void ResolveSessionDevice(PaSession session)
		{
			var list = session.IsSinkInput ? sinks : sources;
			session.DeviceName = "";
			session.DeviceId = "";

			foreach (var e in list)
			{
				if (e.Index == session.DeviceIndex)
				{
					session.DeviceName = e.Name;
					session.DeviceId = MakeId(e.Kind, e.Name);
					return;
				}
			}
		}

		private AudioSessionDescriptor DescribeSession(PaSession s)
			=> new (s.Id,
					s.DeviceId,
					s.IsSinkInput ? AudioDeviceKind.Output : AudioDeviceKind.Input,
					s.ProcessId,
					s.ProcessName,
					s.DisplayName,
					s.State,
					// Pulse has no distinguished system-sounds stream; nothing here could report one honestly.
					false,
					s.HasVolume,
					s.HasMute,
					CanMeterSession(s));

		/// <summary>
		/// Only a playback session can be metered: a sink-input is observed through its sink's monitor, and a
		/// source-output has no monitor of its own.
		/// </summary>
		private bool CanMeterSession(PaSession s)
			=> s.IsSinkInput && Pa.SetMonitorStream != null && CaptureExportsPresent() && !string.IsNullOrEmpty(s.DeviceName);

		private static ulong SlotKey(bool sinkInput, uint index) => ((ulong)(sinkInput ? 1 : 0) << 32) | index;

		private string MakeSessionId(PaSession s)
			=> string.Concat(s.IsSinkInput ? SinkInputPrefix : SourceOutputPrefix,
							 connectionGeneration.ToString(CultureInfo.InvariantCulture),
							 ":",
							 s.Index.ToString(CultureInfo.InvariantCulture),
							 ":",
							 s.Generation.ToString(CultureInfo.InvariantCulture));

		private static bool TrySplitSessionId(string id, out bool sinkInput, out long generation, out uint index, out long admission)
		{
			sinkInput = false;
			generation = 0;
			index = 0;
			admission = 0;

			if (string.IsNullOrEmpty(id))
				return false;

			if (id.StartsWith(SinkInputPrefix, StringComparison.Ordinal))
				sinkInput = true;
			else if (!id.StartsWith(SourceOutputPrefix, StringComparison.Ordinal))
				return false;

			var parts = id.Substring((sinkInput ? SinkInputPrefix : SourceOutputPrefix).Length).Split(':');
			return parts.Length == 3
				   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out generation)
				   && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index)
				   && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out admission);
		}

		/// <summary>
		/// Allocates the three proplist keys once. The info callbacks look them up on the mainloop thread, so the
		/// keys are native memory owned here rather than something marshalled per lookup.
		/// </summary>
		private bool EnsureSessionKeys()
		{
			if (keyProcessId != 0 && keyProcessBinary != 0 && keyApplicationName != 0)
				return true;

			try
			{
				if (keyProcessId == 0)
					keyProcessId = Marshal.StringToCoTaskMemUTF8("application.process.id");

				if (keyProcessBinary == 0)
					keyProcessBinary = Marshal.StringToCoTaskMemUTF8("application.process.binary");

				if (keyApplicationName == 0)
					keyApplicationName = Marshal.StringToCoTaskMemUTF8("application.name");
			}
			catch (Exception)
			{
				return false;
			}

			return keyProcessId != 0 && keyProcessBinary != 0 && keyApplicationName != 0;
		}

		private void FreeSessionKeys()
		{
			if (keyProcessId != 0)
			{
				Marshal.FreeCoTaskMem(keyProcessId);
				keyProcessId = 0;
			}

			if (keyProcessBinary != 0)
			{
				Marshal.FreeCoTaskMem(keyProcessBinary);
				keyProcessBinary = 0;
			}

			if (keyApplicationName != 0)
			{
				Marshal.FreeCoTaskMem(keyApplicationName);
				keyApplicationName = 0;
			}
		}

		// ---- level metering -----------------------------------------------------------------

		public bool TryOpenMeter(string targetId, bool isSession, double intervalMilliseconds, out IAudioNativeMeter meter, out string error)
		{
			meter = null;
			error = "";

			lock (sync)
			{
				if (!EnsureMeteringUsable(out error))
					return false;

				if (!TryResolveMeterTarget(targetId, isSession, out var sourceName, out var monitorIndex, out error))
					return false;

				var opened = new PulseMeter(this);

				if (!opened.TryConnect(sourceName, monitorIndex, intervalMilliseconds, out error))
				{
					opened.Dispose();
					return false;
				}

				meters.Add(opened);
				meter = opened;
				return true;
			}
		}

		/// <summary>A meter is a peak-detecting record stream, so it needs exactly the recording exports.</summary>
		private bool EnsureMeteringUsable(out string reason)
		{
			if (!EnsureReady(out reason))
				return false;

			if (!CaptureExportsPresent())
			{
				reason = "This libpulse.so.0 does not export the recording API (pa_stream_connect_record) that a level meter observes through; install a PulseAudio 0.9.15 or newer client library.";
				return false;
			}

			reason = "";
			return true;
		}

		/// <summary>
		/// Resolves what a meter observes: an output device and a playback session are both observed through the
		/// sink's monitor source, a session additionally selecting one stream on it; an input device is observed
		/// directly.
		/// </summary>
		private bool TryResolveMeterTarget(string targetId, bool isSession, out string sourceName, out uint monitorIndex, out string error)
		{
			sourceName = null;
			monitorIndex = InvalidIndex;
			error = "";

			if (isSession)
			{
				if (!EnsureSessionsUsable(out error))
					return false;

				if (Pa.SetMonitorStream == null)
				{
					error = "This libpulse.so.0 does not export pa_stream_set_monitor_stream, so one application's level cannot be observed.";
					return false;
				}

				if (!TryResolveSession(targetId, out var session))
				{
					error = "That audio session is no longer present.";
					return false;
				}

				if (!session.IsSinkInput)
				{
					// A source-output has no monitor of its own and no verified observation point, so metering a
					// recording session is refused rather than answered with a number from somewhere else.
					error = "PulseAudio can meter a playback session only; a recording session has no monitor of its own.";
					return false;
				}

				if (string.IsNullOrEmpty(session.DeviceName))
				{
					error = "The output device behind that session could not be resolved, so its monitor source is unknown.";
					return false;
				}

				sourceName = session.DeviceName + MonitorSuffix;
				monitorIndex = session.Index;
				return true;
			}

			_ = RefreshSnapshot();
			PaEndpoint target;

			if (string.IsNullOrEmpty(targetId))
			{
				target = FindDefault(AudioDeviceKind.Output);

				if (target == null)
				{
					error = "This sound server reports no output device to observe.";
					return false;
				}
			}
			else
			{
				if (!TrySplitId(targetId, out var kind, out var name))
				{
					error = $"'{targetId}' is not a PulseAudio device id.";
					return false;
				}

				target = Find(kind, name);

				if (target == null)
				{
					error = $"Device '{targetId}' is not present.";
					return false;
				}

				if (kind == AudioDeviceKind.Input)
				{
					// An input endpoint is itself a source, so its level is observed on it directly.
					sourceName = target.Name;
					return true;
				}
			}

			sourceName = target.Name + MonitorSuffix;

			if (Find(AudioDeviceKind.Input, sourceName) == null)
			{
				error = $"Output device '{MakeId(AudioDeviceKind.Output, target.Name)}' has no monitor source, so its level cannot be observed.";
				return false;
			}

			return true;
		}

		internal void Forget(PulseInputStream stream)
		{
			lock (sync)
				_ = inputs.Remove(stream);
		}

		internal void Forget(PulseMeter meter)
		{
			lock (sync)
				_ = meters.Remove(meter);
		}

		// ---- session callbacks ------------------------------------------------------------------
		// These run on the mainloop thread with its lock held, exactly like the endpoint ones: they copy borrowed
		// data and signal, and never lock, wait, or re-enter Pulse.

		private void OnSinkInputInfo(nint c, nint info, int eol, nint userData) => OnSessionInfo(info, eol, true);

		private void OnSourceOutputInfo(nint c, nint info, int eol, nint userData) => OnSessionInfo(info, eol, false);

		private void OnSessionInfo(nint info, int eol, bool sinkInput)
		{
			try
			{
				if (eol != 0)
				{
					scratchOutcome = eol > 0 ? 1 : -1;
					Pa.ThreadedMainloopSignal(mainloop, 0);
					return;
				}

				if (info == 0)
					return;

				var session = ReadSession(info, sinkInput);

				if (session == null)
					return;

				scratchSession = session;
				scratchSessions.Add(session);
			}
			catch (Exception)
			{
				scratchOutcome = -1;
				Pa.ThreadedMainloopSignal(mainloop, 0);
			}
		}

		/// <summary>
		/// Copies one borrowed pa_sink_input_info or pa_source_output_info. Every pointer it holds, the proplist
		/// included, dies with the callback, so each string is materialised here. Metadata an application did not
		/// publish stays blank rather than being inferred from the stream name.
		/// </summary>
		private PaSession ReadSession(nint info, bool sinkInput)
		{
			var modern = Pa.SessionFieldsSince1;
			var proplist = Marshal.ReadIntPtr(info, sinkInput ? SinkInputProplistOffset : SourceOutputProplistOffset);
			var streamName = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, SessionNameOffset)) ?? "";
			var processName = ReadProperty(proplist, keyProcessBinary);
			var processText = ReadProperty(proplist, keyProcessId);
			_ = long.TryParse(processText, NumberStyles.None, CultureInfo.InvariantCulture, out var processId);
			var session = new PaSession
			{
				IsSinkInput = sinkInput,
				Index = unchecked((uint)Marshal.ReadInt32(info, SessionIndexOffset)),
				DeviceIndex = unchecked((uint)Marshal.ReadInt32(info, SessionDeviceOffset)),
				ProcessId = processId > 0 ? processId : 0,
				ProcessName = processName,
				DisplayName = ReadProperty(proplist, keyApplicationName),
				Identity = string.Concat(streamName, "|", processText, "|", processName),
			};

			if (sinkInput)
			{
				session.Volume = ReadLinearVolume(info, SinkInputVolumeOffset, out var channels);
				session.VolumeChannels = channels;
				session.Mute = Marshal.ReadInt32(info, SinkInputMuteOffset) != 0;
				session.HasMute = Pa.SetSinkInputMute != null;

				// Before 1.0 a sink-input had no has_volume/volume_writable pair and its volume was always writable.
				session.HasVolume = Pa.SetSinkInputVolume != null && channels > 0
									&& (!modern
										|| (Marshal.ReadInt32(info, SinkInputHasVolumeOffset) != 0
											&& Marshal.ReadInt32(info, SinkInputVolumeWritableOffset) != 0));

				if (modern)
				{
					session.Corked = Marshal.ReadInt32(info, SinkInputCorkedOffset) != 0;
					session.CorkedKnown = true;
				}
			}
			else if (modern)
			{
				// A source-output carries no volume, mute, or corked field before 1.0: its struct ends at proplist.
				session.Volume = ReadLinearVolume(info, SourceOutputVolumeOffset, out var channels);
				session.VolumeChannels = channels;
				session.Mute = Marshal.ReadInt32(info, SourceOutputMuteOffset) != 0;
				session.Corked = Marshal.ReadInt32(info, SourceOutputCorkedOffset) != 0;
				session.CorkedKnown = true;
				session.HasMute = Pa.SetSourceOutputMute != null;
				session.HasVolume = Pa.SetSourceOutputVolume != null && channels > 0
									&& Marshal.ReadInt32(info, SourceOutputHasVolumeOffset) != 0
									&& Marshal.ReadInt32(info, SourceOutputVolumeWritableOffset) != 0;
			}

			return session;
		}

		private static string ReadProperty(nint proplist, nint key)
		{
			if (proplist == 0 || key == 0 || Pa.ProplistGets == null)
				return "";

			return Marshal.PtrToStringUTF8(Pa.ProplistGets(proplist, key)) ?? "";
		}

		// ---- decoding ---------------------------------------------------------------

		/// <summary>Which decoder this host actually initialized. Chosen once by <see cref="EnsureDecoder"/>.</summary>
		private enum DecoderRoute
		{
			None,
			SndFile,
			Ffmpeg,
		}

		/// <summary>
		/// A decode is bounded so a wedged child process cannot hold the caller forever. Ten minutes is far above
		/// what ffmpeg needs for the largest clip the engine will accept.
		/// </summary>
		private const int DecodeTimeoutMs = 600_000;

		private const int ProbeTimeoutMs = 15_000;

		/// <summary>How long a child gets to exit on its own once its output has been read or its decode refused.</summary>
		private const int ExitGraceMs = 5_000;

		/// <summary>
		/// A source channel count above this is taken as evidence that the SF_INFO layout below does not match the
		/// loaded libsndfile, rather than as a real 100-channel file.
		/// </summary>
		private const int MaxDecodeSourceChannels = 64;

		/// <summary>What the ffmpeg route claims. Every one of these is a container ffmpeg has decoded since 0.5.</summary>
		private static readonly string[] FfmpegFormats = ["mp3", "m4a", "aac", "ogg", "opus", "flac", "wma"];

		private static readonly object decoderGate = new ();
		private static bool decoderProbed;
		private static DecoderRoute decoderRoute;
		private static string[] decoderFormats = [];
		private static string decoderReason = "";

		/// <summary>
		/// The containers this host can decode beyond WAV. The list comes from what actually loaded here: the
		/// libsndfile build's own format tables, or ffmpeg's fixed set, or nothing at all.
		/// </summary>
		public string[] SupportedFormats
			// Cloned because the cached array is shared by every caller and this is a probe, not a hot path.
			=> (string[])EnsureDecoder().Clone();

		/// <summary>
		/// Decodes a whole file into interleaved float32, eagerly: the source is read to its end and closed, or the
		/// child process has exited, before this returns.
		/// </summary>
		/// <param name="path">A file path; it is resolved and must exist.</param>
		/// <param name="samples">Interleaved float32 in [-1, 1], mono or stereo.</param>
		/// <param name="sampleRate">The decoded rate, inside the range <see cref="AudioFormats"/> accepts.</param>
		/// <param name="channels">1 or 2; a source with more is downmixed.</param>
		/// <param name="error">Why the file was refused, when this returns false.</param>
		public bool TryDecodeFile(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;
			error = "";

			if (string.IsNullOrWhiteSpace(path))
			{
				error = "No file was given to decode.";
				return false;
			}

			string full;

			try
			{
				full = Path.GetFullPath(path);
			}
			catch (Exception ex)
			{
				error = $"{path} is not a usable file path: {ex.Message}";
				return false;
			}

			if (!File.Exists(full))
			{
				error = $"The file {full} does not exist.";
				return false;
			}

			_ = EnsureDecoder();
			DecoderRoute route;
			string reason;

			lock (decoderGate)
			{
				route = decoderRoute;
				reason = decoderReason;
			}

			if (route == DecoderRoute.SndFile)
				return TryDecodeWithSndFile(full, out samples, out sampleRate, out channels, out error);

			if (route == DecoderRoute.Ffmpeg)
				return TryDecodeWithFfmpeg(full, out samples, out sampleRate, out channels, out error);

			error = reason;
			return false;
		}

		/// <summary>
		/// Picks this host's decoder exactly once, behind a lock, and caches the answer including the failure. The
		/// probing runs here rather than in a static constructor or a field initializer so that a missing codec can
		/// only ever surface as an empty <see cref="SupportedFormats"/>, never as a type-initialization exception on
		/// an unrelated member of this class.
		/// <para>
		/// libsndfile wins when it is present because it decodes in-process. ffmpeg is consulted only when
		/// libsndfile is absent: claiming ffmpeg's list on top of a libsndfile host would promise formats the
		/// primary route cannot deliver, and the two are never merged.
		/// </para>
		/// </summary>
		private static string[] EnsureDecoder()
		{
			lock (decoderGate)
			{
				if (decoderProbed)
					return decoderFormats;

				decoderProbed = true;

				try
				{
					if (Sf.TryLoad(out var sndFileReason))
					{
						decoderRoute = DecoderRoute.SndFile;
						decoderFormats = Sf.Formats;
						decoderReason = "";
						return decoderFormats;
					}

					if (Ffmpeg.TryLocate(out var ffmpegReason))
					{
						decoderRoute = DecoderRoute.Ffmpeg;
						decoderFormats = FfmpegFormats;
						decoderReason = "";
						return decoderFormats;
					}

					decoderRoute = DecoderRoute.None;
					decoderFormats = [];
					decoderReason = $"This host has no audio decoder beyond WAV. {sndFileReason} {ffmpegReason}";
				}
				catch (Exception ex)
				{
					// A probe that throws must still leave a usable, empty answer behind rather than rethrow on
					// every later call.
					decoderRoute = DecoderRoute.None;
					decoderFormats = [];
					decoderReason = $"No audio decoder could be initialized: {ex.Message}";
				}

				return decoderFormats;
			}
		}

		/// <summary>
		/// Decodes through libsndfile. The handle is opened, read to its end and closed inside this method, so no
		/// file stays open past the return, and the whole clip is materialized before the caller sees it.
		/// </summary>
		private static bool TryDecodeWithSndFile(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;
			error = "";
			var utf8 = Marshal.StringToCoTaskMemUTF8(path);
			var info = Marshal.AllocHGlobal(Sf.InfoBlockBytes);
			nint scratch = 0;
			nint file = 0;

			try
			{
				// SF_INFO must arrive zeroed for a read: libsndfile fills every field itself, and a stale `format`
				// would be read back as a caller-supplied description of a headerless file.
				for (var b = 0; b < Sf.InfoBlockBytes; b += 8)
					Marshal.WriteInt64(info, b, 0);

				file = Sf.Open(utf8, Sf.ReadMode, info);

				if (file == 0)
				{
					var why = Sf.LastError(0);
					error = why.Length != 0
							? $"libsndfile could not open {path}: {why}"
							: $"libsndfile could not open {path}.";
					return false;
				}

				var frames = Marshal.ReadInt64(info, Sf.InfoFramesOffset);
				var rate = Marshal.ReadInt32(info, Sf.InfoRateOffset);
				var sourceChannels = Marshal.ReadInt32(info, Sf.InfoChannelsOffset);

				// The SF_INFO offsets are an assumption about a C struct. Values no real stream can have mean the
				// assumption is wrong for this build, and decoding on that basis would hand the mixer noise rather
				// than an error, so it is refused by name here.
				if (sourceChannels < 1 || sourceChannels > MaxDecodeSourceChannels || rate <= 0 || frames < 0)
				{
					error = $"libsndfile described {path} as {rate} Hz with {sourceChannels} channels and {frames} frames, "
							+ "which no stream can be; this libsndfile does not match the SF_INFO layout Keysharp reads.";
					return false;
				}

				if (!AudioFormats.IsValidSampleRate(rate))
				{
					error = $"{path} is {rate} Hz; the engine accepts {AudioFormats.MinSampleRate} through {AudioFormats.MaxSampleRate} Hz.";
					return false;
				}

				// Assigned to the out parameter only on success, so a refused decode leaves every out at its default.
				var outChannels = Math.Min(sourceChannels, AudioFormats.MaxChannels);
				var maxSamples = AudioFormats.MaxClipBytes / sizeof(float);

				// Checked against the declaration first so an oversized file is refused before anything is allocated.
				if (frames > maxSamples / outChannels)
				{
					error = $"{path} decodes to more than {AudioFormats.MaxClipBytes} bytes, the limit for one clip.";
					return false;
				}

				const int chunkFrames = 8192;
				var interleaved = new float[chunkFrames * sourceChannels];
				scratch = Marshal.AllocHGlobal(interleaved.Length * sizeof(float));
				var buffer = new float[Math.Max(frames * outChannels, chunkFrames * outChannels)];
				long total = 0;

				while (true)
				{
					var got = Sf.ReadFloat(file, scratch, chunkFrames);

					if (got <= 0)
						break;

					// A library that returns more than was asked for would overrun the scratch block on the copy
					// below, so the count is treated as hostile rather than trusted.
					if (got > chunkFrames)
					{
						error = $"libsndfile returned {got} frames for a {chunkFrames} frame request while decoding {path}.";
						return false;
					}

					var produced = got * outChannels;

					// A declared frame count can be a lower bound for a streamed container, so the running total is
					// capped again rather than relying on the pre-check.
					if (total + produced > maxSamples)
					{
						error = $"{path} decodes to more than {AudioFormats.MaxClipBytes} bytes, the limit for one clip.";
						return false;
					}

					if (total + produced > buffer.Length)
						Array.Resize(ref buffer, (int)Math.Min(maxSamples, Math.Max(total + produced, buffer.Length * 2L)));

					Marshal.Copy(scratch, interleaved, 0, (int)(got * sourceChannels));
					Fold(interleaved, (int)got, sourceChannels, outChannels, buffer.AsSpan((int)total));
					total += produced;
				}

				if (total == 0)
				{
					error = $"{path} decoded to no audio.";
					return false;
				}

				if (buffer.Length != total)
					Array.Resize(ref buffer, (int)total);

				samples = buffer;
				sampleRate = rate;
				channels = outChannels;
				return true;
			}
			catch (Exception ex)
			{
				samples = null;
				error = $"libsndfile could not decode {path}: {ex.Message}";
				return false;
			}
			finally
			{
				if (file != 0)
					_ = Sf.Close(file);

				if (scratch != 0)
					Marshal.FreeHGlobal(scratch);

				Marshal.FreeHGlobal(info);
				Marshal.FreeCoTaskMem(utf8);
			}
		}

		/// <summary>
		/// Copies one block of interleaved frames into the clip buffer, downmixing anything above stereo.
		/// <para>
		/// Multichannel sources are downmixed rather than refused by name, so a 5.1 file decodes the same way on a
		/// libsndfile host as it does on the ffmpeg host, where <c>-ac 2</c> does the same job. The rule is the mean
		/// of the even-indexed source channels into the left and the mean of the odd-indexed ones into the right,
		/// which keeps front-left content left and front-right content right for every standard interleave order
		/// and, being a mean of values already inside [-1, 1], can never clip.
		/// </para>
		/// </summary>
		private static void Fold(ReadOnlySpan<float> source, int frames, int sourceChannels, int outChannels, Span<float> destination)
		{
			if (sourceChannels == outChannels)
			{
				var count = frames * sourceChannels;

				for (var i = 0; i < count; i++)
					destination[i] = Clamp(source[i]);

				return;
			}

			for (var f = 0; f < frames; f++)
			{
				var b = f * sourceChannels;
				float left = 0, right = 0;
				int lefts = 0, rights = 0;

				for (var c = 0; c < sourceChannels; c++)
				{
					if ((c & 1) == 0)
					{
						left += Clamp(source[b + c]);
						lefts++;
					}
					else
					{
						right += Clamp(source[b + c]);
						rights++;
					}
				}

				destination[f * 2] = left / lefts;
				destination[(f * 2) + 1] = right / rights;
			}
		}

		/// <summary>
		/// Brings one decoded sample into the range the mixer requires. Unlike a float32 WAV, whose samples are the
		/// author's own data and are refused when out of range, these come out of a lossy decoder that legitimately
		/// overshoots full scale by a fraction of a decibel on inter-sample peaks; failing a whole clip for that
		/// would be worse than clamping it. A non-finite sample is a decoder fault and becomes silence.
		/// </summary>
		private static float Clamp(float value)
			=> !float.IsFinite(value) ? 0f : value < -1f ? -1f : value > 1f ? 1f : value;

		/// <summary>
		/// Decodes by running ffmpeg and reading raw float32 off its stdout. Used only on a host with no libsndfile.
		/// </summary>
		private static bool TryDecodeWithFfmpeg(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;
			error = "";
			int outRate, outChannels;

			if (TryProbeWithFfprobe(path, out var probedRate, out var probedChannels))
			{
				outRate = AudioFormats.IsValidSampleRate(probedRate) ? probedRate : DefaultSampleRate;
				outChannels = Math.Clamp(probedChannels, AudioFormats.MinChannels, AudioFormats.MaxChannels);
			}
			else
			{
				// Without ffprobe the source shape is unknown, so the stream is asked for in the engine's own
				// default shape and ffmpeg resamples and downmixes into it.
				outRate = DefaultSampleRate;
				outChannels = MaxChannels;
			}

			Process process = null;

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = Ffmpeg.Exe,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				// ArgumentList (not a shell string): a path with spaces, quotes or $ is passed verbatim and cannot
				// be re-parsed as shell syntax. -nostdin keeps ffmpeg from reaching for this process's console.
				psi.ArgumentList.Add("-nostdin");
				psi.ArgumentList.Add("-v");
				psi.ArgumentList.Add("error");
				psi.ArgumentList.Add("-i");
				psi.ArgumentList.Add(path);
				psi.ArgumentList.Add("-f");
				psi.ArgumentList.Add("f32le");
				psi.ArgumentList.Add("-acodec");
				psi.ArgumentList.Add("pcm_f32le");
				psi.ArgumentList.Add("-ac");
				psi.ArgumentList.Add(outChannels.ToString(CultureInfo.InvariantCulture));
				psi.ArgumentList.Add("-ar");
				psi.ArgumentList.Add(outRate.ToString(CultureInfo.InvariantCulture));
				psi.ArgumentList.Add("-");
				process = Process.Start(psi);

				if (process == null)
				{
					error = $"Failed to start {Ffmpeg.Exe} to decode {path}.";
					return false;
				}

				// stderr is drained from the moment the child starts: ffmpeg writes there even under -v error, and
				// a full stderr pipe would stop it writing the stdout this thread is reading.
				var diagnostics = process.StandardError.ReadToEndAsync();
				var child = process;
				var pump = Task.Run(() => ReadCapped(child.StandardOutput.BaseStream, path));

				if (!pump.Wait(DecodeTimeoutMs))
				{
					TryKill(process);
					error = $"ffmpeg did not finish decoding {path} within {DecodeTimeoutMs / 1000} seconds.";
					return false;
				}

				var (raw, length, capError) = pump.Result;

				if (capError != null)
				{
					TryKill(process);
					error = capError;
					return false;
				}

				if (!process.WaitForExit(ExitGraceMs))
				{
					TryKill(process);
					error = $"ffmpeg kept running after producing its output for {path}.";
					return false;
				}

				if (process.ExitCode != 0)
				{
					var why = FirstLine(SafeResult(diagnostics));
					error = why.Length != 0
							? $"ffmpeg could not decode {path}: {why}"
							: $"ffmpeg could not decode {path} (exit code {process.ExitCode}).";
					return false;
				}

				var frameBytes = outChannels * sizeof(float);

				if (length < frameBytes)
				{
					error = $"ffmpeg produced no audio for {path}.";
					return false;
				}

				// A partial trailing frame is dropped rather than half-read; ffmpeg emits whole frames, so this only
				// ever trims a crashed or killed child's last write.
				var count = length / frameBytes * outChannels;
				var decoded = new float[count];

				for (var i = 0; i < count; i++)
					decoded[i] = Clamp(BitConverter.ToSingle(raw, i * sizeof(float)));

				samples = decoded;
				sampleRate = outRate;
				channels = outChannels;
				return true;
			}
			catch (Exception ex)
			{
				samples = null;
				error = $"ffmpeg could not decode {path}: {ex.Message}";
				return false;
			}
			finally
			{
				process?.Dispose();
			}
		}

		/// <summary>
		/// Reads a child's stdout to completion, refusing at the clip limit. Reading to the end is what keeps ffmpeg
		/// from blocking forever on a full pipe.
		/// </summary>
		/// <returns>The accumulated buffer and its used length, or a reason when the limit was passed.</returns>
		private static (byte[] Buffer, int Length, string Error) ReadCapped(Stream stdout, string path)
		{
			// Not disposed on purpose: the buffer is handed straight back to the caller, and a MemoryStream holds no
			// unmanaged resource.
			var accumulated = new MemoryStream(1 << 20);
			var chunk = new byte[1 << 16];

			while (true)
			{
				var got = stdout.Read(chunk, 0, chunk.Length);

				if (got <= 0)
					break;

				if (accumulated.Length + got > AudioFormats.MaxClipBytes)
					return (null, 0, $"{path} decodes to more than {AudioFormats.MaxClipBytes} bytes, the limit for one clip.");

				accumulated.Write(chunk, 0, got);
			}

			return (accumulated.GetBuffer(), (int)accumulated.Length, null);
		}

		/// <summary>
		/// Asks ffprobe for the first audio stream's rate and channel count so the decode can keep the source's own
		/// shape. False when ffprobe is absent or says nothing usable, in which case the caller settles for the
		/// engine default and lets ffmpeg resample into it.
		/// </summary>
		private static bool TryProbeWithFfprobe(string path, out int rate, out int channels)
		{
			rate = 0;
			channels = 0;

			if (Ffmpeg.Probe == null)
				return false;

			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = Ffmpeg.Probe,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				};
				psi.ArgumentList.Add("-v");
				psi.ArgumentList.Add("error");
				psi.ArgumentList.Add("-select_streams");
				psi.ArgumentList.Add("a:0");
				psi.ArgumentList.Add("-show_entries");
				psi.ArgumentList.Add("stream=sample_rate,channels");
				psi.ArgumentList.Add("-of");
				psi.ArgumentList.Add("default=noprint_wrappers=1");
				psi.ArgumentList.Add(path);
				using var process = Process.Start(psi);

				if (process == null)
					return false;

				var diagnostics = process.StandardError.ReadToEndAsync();
				var text = process.StandardOutput.ReadToEnd();

				if (!process.WaitForExit(ProbeTimeoutMs))
				{
					TryKill(process);
					return false;
				}

				_ = SafeResult(diagnostics);

				if (process.ExitCode != 0)
					return false;

				foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				{
					var eq = line.IndexOf('=');

					if (eq <= 0)
						continue;

					var key = line.AsSpan(0, eq).Trim();
					var value = line.AsSpan(eq + 1).Trim();

					// ffprobe prints "N/A" for a stream it cannot describe; a failed parse leaves the field zero,
					// which is what makes this return false.
					if (key.SequenceEqual("sample_rate"))
						_ = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out rate);
					else if (key.SequenceEqual("channels"))
						_ = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out channels);
				}

				return rate > 0 && channels > 0;
			}
			catch (Exception)
			{
				// A probe is an optimization; every failure here just means the default shape is requested.
				return false;
			}
		}

		/// <summary>Ends a child that will not finish on its own, so a refused decode leaves nothing running.</summary>
		private static void TryKill(Process process)
		{
			try
			{
				if (!process.HasExited)
					process.Kill(true);
			}
			catch (Exception)
			{
				// The child may have exited between the test and the kill, and a child that cannot be killed is
				// still not something this decode can do anything about.
			}
		}

		/// <summary>The finished text of a drain task, or blank when it did not finish. Diagnostics never throw.</summary>
		private static string SafeResult(Task<string> pending)
		{
			try
			{
				return pending.Wait(ExitGraceMs) ? pending.Result ?? "" : "";
			}
			catch (Exception)
			{
				return "";
			}
		}

		/// <summary>The first line of a child's diagnostics, which is the part worth putting in an error message.</summary>
		private static string FirstLine(string text)
		{
			var end = text.IndexOfAny(['\r', '\n']);
			return (end < 0 ? text : text.Substring(0, end)).Trim();
		}

		// ---- nested types ----------------------------------------------------------------------

		/// <summary>One copied endpoint record; nothing in it points back into Pulse memory.</summary>
		private sealed class PaEndpoint
		{
			internal AudioDeviceKind Kind;
			internal uint Index;
			internal string Name;
			internal string Description;
			internal int Rate;
			internal int Channels;
			internal double Volume;
			internal bool Mute;
			internal int State;
		}

		private sealed class PulseDeviceWatcher : IAudioDeviceWatcher
		{
			private readonly PulseAudioBackend backend;
			private Action sink;

			internal PulseDeviceWatcher(PulseAudioBackend backend, Action sink)
			{
				this.backend = backend;
				this.sink = sink;
			}

			public void Dispose()
			{
				var target = Interlocked.Exchange(ref sink, null);

				if (target != null)
					backend.RemoveWatcher(target);
			}
		}

		/// <summary>
		/// One pa_stream driving one mixer. The write callback is the only real-time path here: it asks Pulse for
		/// a block, hands the mixer a span over it, and writes it back, allocating nothing and taking no lock.
		/// </summary>
		internal sealed class PulseOutputStream : IAudioOutputStream
		{
			private readonly PulseAudioBackend owner;
			private readonly IAudioRenderSource source;
			private readonly int frameBytes;

			// Rooted for the stream's whole life; Pulse holds raw thunk pointers to all four.
			private readonly StreamRequestCb onWrite;
			private readonly StreamNotifyCb onState;
			private readonly StreamNotifyCb onUnderflow;
			private readonly StreamNotifyCb onLatency;
			private readonly StreamNotifyCb onMoved;

			private nint stream;
			private int running;
			private int deviceLost;
			private int closing;
			private int latencyStale = 1;
			private double latencyMs;

			internal PulseOutputStream(PulseAudioBackend owner, IAudioRenderSource source, AudioStreamFormat format)
			{
				this.owner = owner;
				this.source = source;
				Format = format;
				frameBytes = format.Channels * sizeof(float);
				onWrite = OnWriteRequest;
				onState = OnStreamState;
				onUnderflow = OnUnderflow;
				onLatency = OnTimingChanged;
				onMoved = OnTimingChanged;
			}

			public AudioStreamFormat Format { get; }

			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			/// <summary>
			/// The measured estimate. Refreshing happens here, on the management side, when a latency-update or
			/// moved callback has marked it stale; the callbacks themselves only set that flag.
			/// </summary>
			public double LatencyMilliseconds
			{
				get
				{
					if (Volatile.Read(ref latencyStale) == 0 || stream == 0 || owner.Mainloop == 0 || Volatile.Read(ref closing) != 0)
						return latencyMs;

					owner.LockMainloop();

					try
					{
						if (stream != 0 && Pa.StreamGetLatency != null && Pa.StreamGetLatency(stream, out var usec, out var negative) >= 0)
						{
							latencyMs = negative != 0 ? 0 : usec / 1000.0;
							Volatile.Write(ref latencyStale, 0);
						}
					}
					catch (Exception)
					{
					}
					finally
					{
						owner.UnlockMainloop();
					}

					return latencyMs;
				}
			}

			public void Start() => SetCork(false);

			public void Stop() => SetCork(true);

			internal bool TryConnect(string deviceName, double latencyRequestMs, out string error)
			{
				error = "";
				var device = string.IsNullOrEmpty(deviceName) ? 0 : Marshal.StringToCoTaskMemUTF8(deviceName);
				var label = Marshal.StringToCoTaskMemUTF8("Keysharp");
				var spec = new PaSampleSpec
				{
					Format = SampleFloat32Le,
					Rate = (uint)Format.SampleRate,
					Channels = (byte)Format.Channels,
				};
				var bytes = (long)(Format.SampleRate * frameBytes * latencyRequestMs / 1000.0);
				bytes -= bytes % frameBytes;

				if (bytes < frameBytes)
					bytes = frameBytes;

				var attr = new PaBufferAttr
				{
					MaxLength = uint.MaxValue,
					TLength = (uint)Math.Min(bytes, int.MaxValue),
					PreBuf = uint.MaxValue,
					MinReq = uint.MaxValue,
					FragSize = uint.MaxValue,
				};
				var flags = StreamAdjustLatency | StreamAutoTimingUpdate | StreamInterpolateTiming;

				// Corked-at-connect is what makes Start() the moment audio begins. Without pa_stream_cork the
				// stream runs from connect and Start/Stop gate the mixer instead, which is silence either way.
				if (Pa.StreamCork != null)
					flags |= StreamStartCorked;

				owner.LockMainloop();

				try
				{
					stream = Pa.StreamNew(owner.Context, label, ref spec, 0);

					if (stream == 0)
					{
						error = "PulseAudio refused a float32 playback stream for the negotiated format.";
						return false;
					}

					Pa.StreamSetWriteCallback(stream, onWrite, 0);
					Pa.StreamSetStateCallback(stream, onState, 0);

					if (Pa.StreamSetUnderflowCallback != null)
						Pa.StreamSetUnderflowCallback(stream, onUnderflow, 0);

					if (Pa.StreamSetLatencyUpdateCallback != null)
						Pa.StreamSetLatencyUpdateCallback(stream, onLatency, 0);

					if (Pa.StreamSetMovedCallback != null)
						Pa.StreamSetMovedCallback(stream, onMoved, 0);

					if (Pa.StreamConnectPlayback(stream, device, ref attr, flags, 0, 0) < 0)
					{
						error = owner.ServerReason();
						return false;
					}

					int state;

					var deadline = Environment.TickCount64 + ConnectTimeoutMs;

					while ((state = Pa.StreamGetState(stream)) != StreamReady && state != StreamFailed && state != StreamTerminated)
					{
						// A context that dies under the connect, or a server that never answers, must not park this
						// caller in a wait nothing will wake.
						if (Pa.ContextGetState(owner.Context) != ContextReady || Environment.TickCount64 >= deadline)
							break;

						owner.PollWait(ConnectPollMs);
					}

					if (state != StreamReady)
					{
						error = deviceName == null
								? "PulseAudio could not open the default output device."
								: $"PulseAudio could not open output device '{deviceName}'.";
						return false;
					}

					if (Pa.StreamCork == null)
						Volatile.Write(ref running, 0);

					return true;
				}
				catch (Exception ex)
				{
					error = $"PulseAudio playback failed to start: {ex.Message}";
					return false;
				}
				finally
				{
					owner.UnlockMainloop();

					if (device != 0)
						Marshal.FreeCoTaskMem(device);

					Marshal.FreeCoTaskMem(label);
				}
			}

			private void SetCork(bool cork)
			{
				Volatile.Write(ref running, cork ? 0 : 1);

				if (stream == 0 || Pa.StreamCork == null || Volatile.Read(ref closing) != 0)
					return;

				owner.LockMainloop();

				try
				{
					if (stream != 0)
						owner.DetachOperation(Pa.StreamCork(stream, cork ? 1 : 0, null, 0));
				}
				catch (Exception)
				{
				}
				finally
				{
					owner.UnlockMainloop();
				}
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref closing, 1) != 0)
					return;

				Volatile.Write(ref running, 0);
				owner.Forget(this);

				if (stream == 0 || owner.Mainloop == 0)
					return;

				owner.LockMainloop();

				try
				{
					var handle = stream;
					stream = 0;
					// Clearing the callbacks under the lock is the quiescence point: the mainloop thread cannot be
					// inside one of them while this thread holds the lock, so the delegates stop being reachable here.
					Pa.StreamSetWriteCallback(handle, null, 0);
					Pa.StreamSetStateCallback(handle, null, 0);

					if (Pa.StreamSetUnderflowCallback != null)
						Pa.StreamSetUnderflowCallback(handle, null, 0);

					if (Pa.StreamSetLatencyUpdateCallback != null)
						Pa.StreamSetLatencyUpdateCallback(handle, null, 0);

					if (Pa.StreamSetMovedCallback != null)
						Pa.StreamSetMovedCallback(handle, null, 0);

					_ = Pa.StreamDisconnect(handle);
					Pa.StreamUnref(handle);
				}
				catch (Exception)
				{
				}
				finally
				{
					owner.UnlockMainloop();
				}
			}

			// ---- native callbacks --------------------------------------------------------------

			private void OnWriteRequest(nint handle, nuint requested, nint userData)
			{
				try
				{
					var remaining = (long)requested;

					while (remaining >= frameBytes)
					{
						var want = (nuint)remaining;

						if (Pa.StreamBeginWrite(handle, out var block, ref want) < 0 || block == 0 || want == 0)
							return;

						// begin_write may hand back more than was asked for; only whole frames of the requested
						// amount are written, and the surplus is simply not claimed.
						var usable = Math.Min((long)want, remaining);
						usable -= usable % frameBytes;

						if (usable <= 0)
						{
							_ = Pa.StreamWrite(handle, block, 0, 0, 0, SeekRelative);
							return;
						}

						FillBlock(block, (int)(usable / sizeof(float)));

						if (Pa.StreamWrite(handle, block, (nuint)usable, 0, 0, SeekRelative) < 0)
						{
							// The block was borrowed from the stream, so a refused write has to give it back rather
							// than leave it held for the life of the stream.
							if (Pa.StreamCancelWrite != null)
								_ = Pa.StreamCancelWrite(handle);

							return;
						}

						remaining -= usable;
					}
				}
				catch (Exception)
				{
					// A managed exception must never unwind into libpulse's mainloop.
				}
			}

			private unsafe void FillBlock(nint block, int samples)
			{
				var span = new Span<float>((void*)block, samples);

				if (Volatile.Read(ref running) != 0 && Volatile.Read(ref closing) == 0)
					source.Fill(span);
				else
					span.Clear();
			}

			private void OnStreamState(nint handle, nint userData)
			{
				try
				{
					var state = Pa.StreamGetState(handle);

					if ((state == StreamFailed || state == StreamTerminated) && Volatile.Read(ref closing) == 0)
						Volatile.Write(ref deviceLost, 1);

					owner.Signal();
				}
				catch (Exception)
				{
				}
			}

			private void OnUnderflow(nint handle, nint userData) => source.NoteUnderrun();

			private void OnTimingChanged(nint handle, nint userData) => Volatile.Write(ref latencyStale, 1);
		}

		/// <summary>
		/// One copied per-application stream record; nothing in it points back into Pulse memory. Identity is what
		/// distinguishes two streams that have held the same index, and never includes the sink or source, so a
		/// stream the server moves keeps its identity and its id.
		/// </summary>
		private sealed class PaSession
		{
			internal bool IsSinkInput;
			internal uint Index;
			internal uint DeviceIndex;
			internal string DeviceName = "";
			internal string DeviceId = "";
			internal long ProcessId;
			internal string ProcessName = "";
			internal string DisplayName = "";
			internal string Identity = "";
			internal bool Corked;
			internal bool CorkedKnown;
			internal double Volume;
			internal int VolumeChannels;
			internal bool Mute;
			internal bool HasVolume;
			internal bool HasMute;
			internal long Generation;
			internal string Id = "";

			/// <summary>
			/// Corked is the one thing Pulse says about a stream that maps onto activity. A library older than 1.0
			/// has no such field, so every live stream it reports reads as active.
			/// </summary>
			internal string State => CorkedKnown && Corked ? SessionInactive : SessionActive;

			/// <summary>Takes the mutable values of a fresher read of the same stream, keeping id and identity.</summary>
			internal void Adopt(PaSession fresh)
			{
				DeviceIndex = fresh.DeviceIndex;
				ProcessId = fresh.ProcessId;
				ProcessName = fresh.ProcessName;
				DisplayName = fresh.DisplayName;
				Corked = fresh.Corked;
				CorkedKnown = fresh.CorkedKnown;
				Volume = fresh.Volume;
				VolumeChannels = fresh.VolumeChannels;
				Mute = fresh.Mute;
				HasVolume = fresh.HasVolume;
				HasMute = fresh.HasMute;
			}
		}

		/// <summary>What one index space slot was last admitted as, which is what an id is checked against.</summary>
		private readonly record struct SessionSlot(long Generation, string Identity);

		/// <summary>
		/// One pa_stream recording into one sink. The read callback is the real-time path: it drains every buffered
		/// fragment, handing each one to the sink as interleaved float32 and dropping it, and allocates nothing and
		/// takes no managed lock.
		/// </summary>
		internal sealed class PulseInputStream : IAudioInputStream
		{
			private readonly PulseAudioBackend owner;
			private readonly IAudioCaptureSink sink;
			private readonly int frameBytes;

			// Rooted for the stream's whole life; Pulse holds raw thunk pointers to both.
			private readonly StreamRequestCb onRead;
			private readonly StreamNotifyCb onState;

			private nint stream;
			private int running;
			private int deviceLost;
			private int closing;

			internal PulseInputStream(PulseAudioBackend owner, IAudioCaptureSink sink, AudioStreamFormat format)
			{
				this.owner = owner;
				this.sink = sink;
				Format = format;
				frameBytes = format.Channels * sizeof(float);
				onRead = OnReadable;
				onState = OnStreamState;
			}

			public AudioStreamFormat Format { get; }

			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			public void Start() => SetCork(false);

			public void Stop() => SetCork(true);

			internal bool TryConnect(string sourceName, double fragmentMs, out string error)
			{
				error = "";
				var device = Marshal.StringToCoTaskMemUTF8(sourceName);
				var label = Marshal.StringToCoTaskMemUTF8("Keysharp");
				var spec = new PaSampleSpec
				{
					Format = SampleFloat32Le,
					Rate = (uint)Format.SampleRate,
					Channels = (byte)Format.Channels,
				};
				var bytes = (long)(Format.SampleRate * frameBytes * fragmentMs / 1000.0);
				bytes -= bytes % frameBytes;

				if (bytes < frameBytes)
					bytes = frameBytes;

				// Only fragsize means anything to a record stream; the playback fields stay at the server default.
				var attr = new PaBufferAttr
				{
					MaxLength = uint.MaxValue,
					TLength = uint.MaxValue,
					PreBuf = uint.MaxValue,
					MinReq = uint.MaxValue,
					FragSize = (uint)Math.Min(bytes, int.MaxValue),
				};
				var flags = StreamAdjustLatency;

				// Corked-at-connect is what makes Start() the moment capture begins. Without pa_stream_cork the
				// stream runs from connect and the read callback drops what arrives first, which loses the same
				// frames either way.
				if (Pa.StreamCork != null)
					flags |= StreamStartCorked;

				owner.LockMainloop();

				try
				{
					stream = Pa.StreamNew(owner.Context, label, ref spec, 0);

					if (stream == 0)
					{
						error = "PulseAudio refused a float32 record stream for the requested format.";
						return false;
					}

					Pa.StreamSetReadCallback(stream, onRead, 0);
					Pa.StreamSetStateCallback(stream, onState, 0);

					if (Pa.StreamConnectRecord(stream, device, ref attr, flags) < 0)
					{
						error = owner.ServerReason();
						return false;
					}

					int state;

					var deadline = Environment.TickCount64 + ConnectTimeoutMs;

					while ((state = Pa.StreamGetState(stream)) != StreamReady && state != StreamFailed && state != StreamTerminated)
					{
						// A context that dies under the connect, or a server that never answers, must not park this
						// caller in a wait nothing will wake.
						if (Pa.ContextGetState(owner.Context) != ContextReady || Environment.TickCount64 >= deadline)
							break;

						owner.PollWait(ConnectPollMs);
					}

					if (state != StreamReady)
					{
						error = $"PulseAudio could not open source '{sourceName}' for recording.";
						return false;
					}

					return true;
				}
				catch (Exception ex)
				{
					error = $"PulseAudio capture failed to start: {ex.Message}";
					return false;
				}
				finally
				{
					owner.UnlockMainloop();
					Marshal.FreeCoTaskMem(device);
					Marshal.FreeCoTaskMem(label);
				}
			}

			private void SetCork(bool cork)
			{
				Volatile.Write(ref running, cork ? 0 : 1);

				if (stream == 0 || Pa.StreamCork == null || Volatile.Read(ref closing) != 0)
					return;

				owner.LockMainloop();

				try
				{
					if (stream != 0)
						owner.DetachOperation(Pa.StreamCork(stream, cork ? 1 : 0, null, 0));
				}
				catch (Exception)
				{
				}
				finally
				{
					owner.UnlockMainloop();
				}
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref closing, 1) != 0)
					return;

				Volatile.Write(ref running, 0);
				owner.Forget(this);

				if (stream == 0 || owner.Mainloop == 0)
					return;

				owner.LockMainloop();

				try
				{
					var handle = stream;
					stream = 0;
					// Clearing the callbacks under the lock is the quiescence point: the mainloop thread cannot be
					// inside one of them while this thread holds the lock, so the delegates stop being reachable here.
					Pa.StreamSetReadCallback(handle, null, 0);
					Pa.StreamSetStateCallback(handle, null, 0);
					_ = Pa.StreamDisconnect(handle);
					Pa.StreamUnref(handle);
				}
				catch (Exception)
				{
				}
				finally
				{
					owner.UnlockMainloop();
				}
			}

			// ---- native callbacks --------------------------------------------------------------

			private void OnReadable(nint handle, nuint bytes, nint userData)
			{
				try
				{
					while (true)
					{
						if (Pa.StreamPeek(handle, out var data, out var length) < 0)
							return;

						// A zero length is an empty read index: nothing is buffered and nothing must be dropped.
						if (length == 0)
							return;

						// A null pointer with a nonzero length is a server-side hole. It is dropped and never
						// written, because inventing silence for it would misreport what was captured.
						if (data != 0)
							Deliver(data, (long)length);

						if (Pa.StreamDrop(handle) < 0)
							return;
					}
				}
				catch (Exception)
				{
					// A managed exception must never unwind into libpulse's mainloop.
				}
			}

			private unsafe void Deliver(nint data, long length)
			{
				if (Volatile.Read(ref running) == 0 || Volatile.Read(ref closing) != 0)
					return;

				var samples = (int)Math.Min(length / sizeof(float), int.MaxValue);
				samples -= samples % Format.Channels;

				if (samples <= 0)
					return;

				sink.Write(new ReadOnlySpan<float>((void*)data, samples));
			}

			private void OnStreamState(nint handle, nint userData)
			{
				try
				{
					var state = Pa.StreamGetState(handle);

					if ((state == StreamFailed || state == StreamTerminated) && Volatile.Read(ref closing) == 0)
						Volatile.Write(ref deviceLost, 1);

					owner.Signal();
				}
				catch (Exception)
				{
				}
			}
		}

		/// <summary>
		/// One peak-detecting record stream. The server reduces each fragment to a single float, so the read
		/// callback only takes the newest one and publishes it; nothing else here allocates or locks.
		/// </summary>
		internal sealed class PulseMeter : IAudioNativeMeter
		{
			/// <summary>Published until an observation completes, which is what keeps "no data" out of silence.</summary>
			private const double NoObservation = -1.0;

			private readonly PulseAudioBackend owner;

			// Rooted for the meter's whole life; Pulse holds raw thunk pointers to both.
			private readonly StreamRequestCb onRead;
			private readonly StreamNotifyCb onState;

			private nint stream;
			private int closing;
			private long peakBits;

			internal PulseMeter(PulseAudioBackend owner)
			{
				this.owner = owner;
				onRead = OnReadable;
				onState = OnStreamState;
				peakBits = BitConverter.DoubleToInt64Bits(NoObservation);
			}

			public double Peak => BitConverter.Int64BitsToDouble(Interlocked.Read(ref peakBits));

			/// <summary>
			/// Connects to a monitor source, optionally narrowed to one stream on it. The narrowing call has to
			/// precede the connect: the server binds the observed stream when the record stream is connected.
			/// </summary>
			internal bool TryConnect(string sourceName, uint monitorIndex, double intervalMilliseconds, out string error)
			{
				error = "";
				var device = Marshal.StringToCoTaskMemUTF8(sourceName);
				var label = Marshal.StringToCoTaskMemUTF8("Keysharp meter");
				var spec = new PaSampleSpec
				{
					Format = SampleFloat32Le,
					Rate = (uint)MeterRateFor(intervalMilliseconds),
					Channels = 1,
				};
				var attr = new PaBufferAttr
				{
					MaxLength = uint.MaxValue,
					TLength = uint.MaxValue,
					PreBuf = uint.MaxValue,
					MinReq = uint.MaxValue,
					FragSize = sizeof(float),
				};
				owner.LockMainloop();

				try
				{
					stream = Pa.StreamNew(owner.Context, label, ref spec, 0);

					if (stream == 0)
					{
						error = "PulseAudio refused a peak-detecting record stream.";
						return false;
					}

					Pa.StreamSetReadCallback(stream, onRead, 0);
					Pa.StreamSetStateCallback(stream, onState, 0);

					if (monitorIndex != InvalidIndex && (Pa.SetMonitorStream == null || Pa.SetMonitorStream(stream, monitorIndex) < 0))
					{
						error = "PulseAudio refused to observe that application's stream on its output device.";
						return false;
					}

					if (Pa.StreamConnectRecord(stream, device, ref attr, StreamPeakDetect | StreamAdjustLatency) < 0)
					{
						error = owner.ServerReason();
						return false;
					}

					int state;

					var deadline = Environment.TickCount64 + ConnectTimeoutMs;

					while ((state = Pa.StreamGetState(stream)) != StreamReady && state != StreamFailed && state != StreamTerminated)
					{
						if (Pa.ContextGetState(owner.Context) != ContextReady || Environment.TickCount64 >= deadline)
							break;

						owner.PollWait(ConnectPollMs);
					}

					if (state != StreamReady)
					{
						error = $"PulseAudio could not observe the level of '{sourceName}'.";
						return false;
					}

					return true;
				}
				catch (Exception ex)
				{
					error = $"PulseAudio level observation failed to start: {ex.Message}";
					return false;
				}
				finally
				{
					owner.UnlockMainloop();
					Marshal.FreeCoTaskMem(device);
					Marshal.FreeCoTaskMem(label);
				}
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref closing, 1) != 0)
					return;

				owner.Forget(this);

				if (stream == 0 || owner.Mainloop == 0)
					return;

				owner.LockMainloop();

				try
				{
					var handle = stream;
					stream = 0;
					// The same quiescence point the other streams use: the mainloop thread cannot be inside a
					// callback while this thread holds the lock, so the delegates stop being reachable here.
					Pa.StreamSetReadCallback(handle, null, 0);
					Pa.StreamSetStateCallback(handle, null, 0);
					_ = Pa.StreamDisconnect(handle);
					Pa.StreamUnref(handle);
				}
				catch (Exception)
				{
				}
				finally
				{
					owner.UnlockMainloop();
				}
			}

			// ---- native callbacks --------------------------------------------------------------

			private void OnReadable(nint handle, nuint bytes, nint userData)
			{
				try
				{
					var latest = NoObservation;

					while (true)
					{
						if (Pa.StreamPeek(handle, out var data, out var length) < 0)
							break;

						if (length == 0)
							break;

						// A hole carries no observation, so it is dropped without displacing the last real one.
						if (data != 0)
						{
							var value = MaxAbs(data, (long)length);

							if (value >= 0)
								latest = value;
						}

						if (Pa.StreamDrop(handle) < 0)
							break;
					}

					// Only the newest fragment is published: peak detection already reduced each one to a single
					// value, so an older fragment in the same drain is a stale observation.
					if (latest >= 0)
						_ = Interlocked.Exchange(ref peakBits, BitConverter.DoubleToInt64Bits(Math.Min(latest, 1.0)));
				}
				catch (Exception)
				{
				}
			}

			private static unsafe double MaxAbs(nint data, long length)
			{
				var samples = (int)Math.Min(length / sizeof(float), int.MaxValue);

				if (samples <= 0)
					return NoObservation;

				var span = new ReadOnlySpan<float>((void*)data, samples);
				var peak = 0f;

				for (var i = 0; i < span.Length; i++)
				{
					var value = Math.Abs(span[i]);

					// A NaN fails this comparison and is ignored, which keeps one bad sample out of the meter.
					if (value > peak)
						peak = value;
				}

				return peak;
			}

			private void OnStreamState(nint handle, nint userData)
			{
				try
				{
					var state = Pa.StreamGetState(handle);

					// A dead observation goes back to reporting nothing, so a lost stream cannot keep reading as
					// whatever level it last saw, or as digital silence.
					if ((state == StreamFailed || state == StreamTerminated) && Volatile.Read(ref closing) == 0)
						_ = Interlocked.Exchange(ref peakBits, BitConverter.DoubleToInt64Bits(NoObservation));

					owner.Signal();
				}
				catch (Exception)
				{
				}
			}
		}

		// ---- native declarations ---------------------------------------------------------------

		[StructLayout(LayoutKind.Sequential)]
		private struct PaSampleSpec
		{
			internal int Format;        // pa_sample_format_t
			internal uint Rate;
			internal byte Channels;     // trailing pad brings this to the C size of 12
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PaBufferAttr
		{
			internal uint MaxLength;
			internal uint TLength;
			internal uint PreBuf;
			internal uint MinReq;
			internal uint FragSize;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ContextNotifyCb(nint context, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SinkInfoCb(nint context, nint info, int eol, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SourceInfoCb(nint context, nint info, int eol, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SinkInputInfoCb(nint context, nint info, int eol, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SourceOutputInfoCb(nint context, nint info, int eol, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ServerInfoCb(nint context, nint info, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ContextSuccessCb(nint context, int success, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void ContextSubscribeCb(nint context, uint eventType, uint index, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void StreamRequestCb(nint stream, nuint bytes, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void StreamNotifyCb(nint stream, nint userData);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void StreamSuccessCb(nint stream, int success, nint userData);

		/// <summary>
		/// The resolved libpulse entry points. Every field is filled once by <see cref="TryLoad"/>; a static
		/// initializer never runs native code, so a missing library can only surface as a cached reason.
		/// <para>
		/// The accepted minimum is PulseAudio 0.9.15 (2009): every export below, and every struct offset above,
		/// has been stable from that release forward, including under pipewire-pulse, which uses this same client
		/// library. <c>pa_stream_cork</c> is the only optional binding.
		/// </para>
		/// </summary>
		private static class Pa
		{
			private const string Library = "libpulse.so.0";

			private static readonly object gate = new ();
			private static int state;               // 0 unknown, 1 ready, 2 failed
			private static string failure;

			internal static nint Handle;            // rooted for the process lifetime once loaded
			internal static string Version = "";

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnNew();
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnVoidP(nint p);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnIntP(nint p);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnPtrP(nint p);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnVoidPI(nint p, int i);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnStrError(int error);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnContextNew(nint api, nint name);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnContextConnect(nint context, nint server, int flags, nint spawnApi);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnSetContextStateCb(nint context, ContextNotifyCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetServerInfo(nint context, ServerInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSinkInfoList(nint context, SinkInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSourceInfoList(nint context, SourceInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSinkInfoByIndex(nint context, uint index, SinkInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSourceInfoByIndex(nint context, uint index, SourceInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnSetVolumeByIndex(nint context, uint index, nint cvolume, ContextSuccessCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnSetMuteByIndex(nint context, uint index, int mute, ContextSuccessCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnSubscribe(nint context, uint mask, ContextSuccessCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnSetSubscribeCb(nint context, ContextSubscribeCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double FnVolumeToLinear(uint volume);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint FnVolumeFromLinear(double linear);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnStreamNew(nint context, nint name, ref PaSampleSpec spec, nint channelMap);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamConnectPlayback(nint stream, nint device, ref PaBufferAttr attr, int flags, nint volume, nint syncStream);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnStreamSetRequestCb(nint stream, StreamRequestCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FnStreamSetNotifyCb(nint stream, StreamNotifyCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamBeginWrite(nint stream, out nint data, ref nuint bytes);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamCancelWrite(nint stream);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamWrite(nint stream, nint data, nuint bytes, nint freeCb, long offset, int seek);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamGetLatency(nint stream, out ulong usec, out int negative);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnStreamCork(nint stream, int cork, StreamSuccessCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamConnectRecord(nint stream, nint device, ref PaBufferAttr attr, int flags);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamPeek(nint stream, out nint data, out nuint bytes);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnStreamSetMonitorStream(nint stream, uint sinkInputIndex);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSinkInputInfoList(nint context, SinkInputInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSinkInputInfoByIndex(nint context, uint index, SinkInputInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSourceOutputInfoList(nint context, SourceOutputInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnGetSourceOutputInfoByIndex(nint context, uint index, SourceOutputInfoCb cb, nint userData);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnProplistGets(nint proplist, nint key);

			internal static FnNew GetLibraryVersionRaw;
			internal static FnStrError StrError;
			internal static FnNew ThreadedMainloopNew;
			internal static FnVoidP ThreadedMainloopFree;
			internal static FnIntP ThreadedMainloopStart;
			internal static FnVoidP ThreadedMainloopStop;
			internal static FnVoidP ThreadedMainloopLock;
			internal static FnVoidP ThreadedMainloopUnlock;
			internal static FnVoidP ThreadedMainloopWait;
			internal static FnVoidPI ThreadedMainloopSignal;
			internal static FnPtrP ThreadedMainloopGetApi;
			internal static FnContextNew ContextNew;
			internal static FnVoidP ContextUnref;
			internal static FnContextConnect ContextConnect;
			internal static FnVoidP ContextDisconnect;
			internal static FnIntP ContextGetState;
			internal static FnIntP ContextErrno;
			internal static FnSetContextStateCb SetContextStateCallback;
			internal static FnIntP OperationGetState;
			internal static FnVoidP OperationUnref;
			internal static FnGetServerInfo GetServerInfo;
			internal static FnGetSinkInfoList GetSinkInfoList;
			internal static FnGetSourceInfoList GetSourceInfoList;
			internal static FnGetSinkInfoByIndex GetSinkInfoByIndex;
			internal static FnGetSourceInfoByIndex GetSourceInfoByIndex;
			internal static FnSetVolumeByIndex SetSinkVolumeByIndex;
			internal static FnSetVolumeByIndex SetSourceVolumeByIndex;
			internal static FnSetMuteByIndex SetSinkMuteByIndex;
			internal static FnSetMuteByIndex SetSourceMuteByIndex;
			internal static FnSubscribe Subscribe;
			internal static FnSetSubscribeCb SetSubscribeCallback;
			internal static FnVolumeToLinear SwVolumeToLinear;
			internal static FnVolumeFromLinear SwVolumeFromLinear;
			internal static FnStreamNew StreamNew;
			internal static FnVoidP StreamUnref;
			internal static FnStreamConnectPlayback StreamConnectPlayback;
			internal static FnIntP StreamDisconnect;
			internal static FnIntP StreamGetState;
			internal static FnStreamSetNotifyCb StreamSetStateCallback;
			internal static FnStreamSetRequestCb StreamSetWriteCallback;
			internal static FnStreamSetNotifyCb StreamSetUnderflowCallback;
			internal static FnStreamSetNotifyCb StreamSetLatencyUpdateCallback;
			internal static FnStreamSetNotifyCb StreamSetMovedCallback;
			internal static FnStreamBeginWrite StreamBeginWrite;
			internal static FnStreamCancelWrite StreamCancelWrite;   // optional; a host without it simply cannot release a refused block
			internal static FnStreamWrite StreamWrite;
			internal static FnStreamGetLatency StreamGetLatency;
			internal static FnStreamCork StreamCork;     // optional; absence degrades Start/Stop to a mixer gate

			// Conditional groups. Each one is optional on its own, so a library missing any of them loses exactly
			// the capability that needs it and keeps playback, devices, and endpoint volume.
			internal static FnStreamConnectRecord StreamConnectRecord;
			internal static FnStreamSetRequestCb StreamSetReadCallback;
			internal static FnStreamPeek StreamPeek;
			internal static FnIntP StreamDrop;
			internal static FnStreamSetMonitorStream SetMonitorStream;
			internal static FnGetSinkInputInfoList GetSinkInputInfoList;
			internal static FnGetSinkInputInfoByIndex GetSinkInputInfo;
			internal static FnGetSourceOutputInfoList GetSourceOutputInfoList;
			internal static FnGetSourceOutputInfoByIndex GetSourceOutputInfo;
			internal static FnSetVolumeByIndex SetSinkInputVolume;
			internal static FnSetMuteByIndex SetSinkInputMute;
			internal static FnSetVolumeByIndex SetSourceOutputVolume;
			internal static FnSetMuteByIndex SetSourceOutputMute;
			internal static FnProplistGets ProplistGets;

			/// <summary>
			/// Whether the loaded library is 1.0 or newer, which is where pa_sink_input_info.corked and the whole
			/// source-output volume block were added. An older struct ends before them, so they are never read.
			/// </summary>
			internal static bool SessionFieldsSince1;

			internal static bool TryLoad(out string reason)
			{
				lock (gate)
				{
					if (state == 1)
					{
						reason = "";
						return true;
					}

					if (state == 2)
					{
						reason = failure;
						return false;
					}

					if (!NativeLibrary.TryLoad(Library, out Handle) || Handle == 0)
					{
						Handle = 0;
						state = 2;
						failure = $"The PulseAudio client library {Library} was not found. Install it (Debian/Ubuntu: libpulse0, Fedora/RHEL: pulseaudio-libs, openSUSE: libpulse0, Arch: libpulse).";
						reason = failure;
						return false;
					}

					string missing = null;
					GetLibraryVersionRaw = Bind<FnNew>("pa_get_library_version", ref missing);
					StrError = Bind<FnStrError>("pa_strerror", ref missing);
					ThreadedMainloopNew = Bind<FnNew>("pa_threaded_mainloop_new", ref missing);
					ThreadedMainloopFree = Bind<FnVoidP>("pa_threaded_mainloop_free", ref missing);
					ThreadedMainloopStart = Bind<FnIntP>("pa_threaded_mainloop_start", ref missing);
					ThreadedMainloopStop = Bind<FnVoidP>("pa_threaded_mainloop_stop", ref missing);
					ThreadedMainloopLock = Bind<FnVoidP>("pa_threaded_mainloop_lock", ref missing);
					ThreadedMainloopUnlock = Bind<FnVoidP>("pa_threaded_mainloop_unlock", ref missing);
					ThreadedMainloopWait = Bind<FnVoidP>("pa_threaded_mainloop_wait", ref missing);
					ThreadedMainloopSignal = Bind<FnVoidPI>("pa_threaded_mainloop_signal", ref missing);
					ThreadedMainloopGetApi = Bind<FnPtrP>("pa_threaded_mainloop_get_api", ref missing);
					ContextNew = Bind<FnContextNew>("pa_context_new", ref missing);
					ContextUnref = Bind<FnVoidP>("pa_context_unref", ref missing);
					ContextConnect = Bind<FnContextConnect>("pa_context_connect", ref missing);
					ContextDisconnect = Bind<FnVoidP>("pa_context_disconnect", ref missing);
					ContextGetState = Bind<FnIntP>("pa_context_get_state", ref missing);
					ContextErrno = Bind<FnIntP>("pa_context_errno", ref missing);
					SetContextStateCallback = Bind<FnSetContextStateCb>("pa_context_set_state_callback", ref missing);
					OperationGetState = Bind<FnIntP>("pa_operation_get_state", ref missing);
					OperationUnref = Bind<FnVoidP>("pa_operation_unref", ref missing);
					GetServerInfo = Bind<FnGetServerInfo>("pa_context_get_server_info", ref missing);
					GetSinkInfoList = Bind<FnGetSinkInfoList>("pa_context_get_sink_info_list", ref missing);
					GetSourceInfoList = Bind<FnGetSourceInfoList>("pa_context_get_source_info_list", ref missing);
					GetSinkInfoByIndex = Bind<FnGetSinkInfoByIndex>("pa_context_get_sink_info_by_index", ref missing);
					GetSourceInfoByIndex = Bind<FnGetSourceInfoByIndex>("pa_context_get_source_info_by_index", ref missing);
					SetSinkVolumeByIndex = Bind<FnSetVolumeByIndex>("pa_context_set_sink_volume_by_index", ref missing);
					SetSourceVolumeByIndex = Bind<FnSetVolumeByIndex>("pa_context_set_source_volume_by_index", ref missing);
					SetSinkMuteByIndex = Bind<FnSetMuteByIndex>("pa_context_set_sink_mute_by_index", ref missing);
					SetSourceMuteByIndex = Bind<FnSetMuteByIndex>("pa_context_set_source_mute_by_index", ref missing);
					Subscribe = Bind<FnSubscribe>("pa_context_subscribe", ref missing);
					SetSubscribeCallback = Bind<FnSetSubscribeCb>("pa_context_set_subscribe_callback", ref missing);
					SwVolumeToLinear = Bind<FnVolumeToLinear>("pa_sw_volume_to_linear", ref missing);
					SwVolumeFromLinear = Bind<FnVolumeFromLinear>("pa_sw_volume_from_linear", ref missing);
					StreamNew = Bind<FnStreamNew>("pa_stream_new", ref missing);
					StreamUnref = Bind<FnVoidP>("pa_stream_unref", ref missing);
					StreamConnectPlayback = Bind<FnStreamConnectPlayback>("pa_stream_connect_playback", ref missing);
					StreamDisconnect = Bind<FnIntP>("pa_stream_disconnect", ref missing);
					StreamGetState = Bind<FnIntP>("pa_stream_get_state", ref missing);
					StreamSetStateCallback = Bind<FnStreamSetNotifyCb>("pa_stream_set_state_callback", ref missing);
					StreamSetWriteCallback = Bind<FnStreamSetRequestCb>("pa_stream_set_write_callback", ref missing);
					StreamSetUnderflowCallback = Bind<FnStreamSetNotifyCb>("pa_stream_set_underflow_callback", ref missing);
					StreamSetLatencyUpdateCallback = Bind<FnStreamSetNotifyCb>("pa_stream_set_latency_update_callback", ref missing);
					StreamSetMovedCallback = Bind<FnStreamSetNotifyCb>("pa_stream_set_moved_callback", ref missing);
					StreamBeginWrite = Bind<FnStreamBeginWrite>("pa_stream_begin_write", ref missing);
					StreamCancelWrite = BindOptional<FnStreamCancelWrite>("pa_stream_cancel_write");
					StreamWrite = Bind<FnStreamWrite>("pa_stream_write", ref missing);
					StreamGetLatency = Bind<FnStreamGetLatency>("pa_stream_get_latency", ref missing);
					StreamCork = BindOptional<FnStreamCork>("pa_stream_cork");
					StreamConnectRecord = BindOptional<FnStreamConnectRecord>("pa_stream_connect_record");
					StreamSetReadCallback = BindOptional<FnStreamSetRequestCb>("pa_stream_set_read_callback");
					StreamPeek = BindOptional<FnStreamPeek>("pa_stream_peek");
					StreamDrop = BindOptional<FnIntP>("pa_stream_drop");
					SetMonitorStream = BindOptional<FnStreamSetMonitorStream>("pa_stream_set_monitor_stream");
					GetSinkInputInfoList = BindOptional<FnGetSinkInputInfoList>("pa_context_get_sink_input_info_list");
					GetSinkInputInfo = BindOptional<FnGetSinkInputInfoByIndex>("pa_context_get_sink_input_info");
					GetSourceOutputInfoList = BindOptional<FnGetSourceOutputInfoList>("pa_context_get_source_output_info_list");
					GetSourceOutputInfo = BindOptional<FnGetSourceOutputInfoByIndex>("pa_context_get_source_output_info");
					SetSinkInputVolume = BindOptional<FnSetVolumeByIndex>("pa_context_set_sink_input_volume");
					SetSinkInputMute = BindOptional<FnSetMuteByIndex>("pa_context_set_sink_input_mute");
					SetSourceOutputVolume = BindOptional<FnSetVolumeByIndex>("pa_context_set_source_output_volume");
					SetSourceOutputMute = BindOptional<FnSetMuteByIndex>("pa_context_set_source_output_mute");
					ProplistGets = BindOptional<FnProplistGets>("pa_proplist_gets");

					if (missing != null)
					{
						state = 2;
						failure = $"{Library} is missing the required export '{missing}'; install a PulseAudio 0.9.15 or newer client library.";
						reason = failure;
						return false;
					}

					try
					{
						Version = Marshal.PtrToStringUTF8(GetLibraryVersionRaw()) ?? "";
					}
					catch (Exception)
					{
						Version = "";
					}

					SessionFieldsSince1 = MajorVersion(Version) >= 1;
					state = 1;
					reason = "";
					return true;
				}
			}

			/// <summary>
			/// The leading integer of the reported version, or zero when it does not start with one. Zero is the
			/// conservative answer: it keeps the fields added in 1.0 unread.
			/// </summary>
			private static int MajorVersion(string version)
			{
				var digits = 0;

				while (digits < version.Length && char.IsAsciiDigit(version[digits]))
					digits++;

				return digits != 0 && int.TryParse(version.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var major)
					   ? major
					   : 0;
			}

			private static T Bind<T>(string name, ref string missing) where T : Delegate
			{
				var bound = BindOptional<T>(name);

				if (bound == null)
					missing ??= name;

				return bound;
			}

			private static T BindOptional<T>(string name) where T : Delegate
			{
				try
				{
					if (NativeLibrary.TryGetExport(Handle, name, out var address) && address != 0)
						return Marshal.GetDelegateForFunctionPointer<T>(address);
				}
				catch (Exception)
				{
				}

				return null;
			}
		}

		/// <summary>
		/// The resolved libsndfile entry points, plus the format claim list taken from the loaded build's own
		/// tables. Loaded the same way as <see cref="Pa"/>: <see cref="NativeLibrary.TryLoad(string, out nint)"/>
		/// and <see cref="NativeLibrary.TryGetExport(nint, string, out nint)"/>, never a <c>DllImport</c>, because a
		/// host without the library is an ordinary outcome that must degrade into an empty format list.
		/// <para>
		/// Only five exports are used and all of them date from 1.0.0 (2002), so the accepted minimum is whatever
		/// the distribution ships. What differs between builds is which containers are compiled in, and that is read
		/// out of the library rather than guessed from its version.
		/// </para>
		/// </summary>
		private static class Sf
		{
			private const string Library = "libsndfile.so.1";

			/// <summary>SFM_READ.</summary>
			internal const int ReadMode = 0x10;

			// sf_command selectors, from sndfile.h. The two count selectors take an int, the two table selectors
			// take an SF_FORMAT_INFO whose `format` field carries the index in and the format code back out.
			private const int GetFormatMajorCount = 0x1030;
			private const int GetFormatMajor = 0x1031;
			private const int GetFormatSubtypeCount = 0x1032;
			private const int GetFormatSubtype = 0x1033;

			private const int TypeMask = 0x0FFF0000;    // SF_FORMAT_TYPEMASK
			private const int SubMask = 0x0000FFFF;     // SF_FORMAT_SUBMASK
			private const int FormatMpeg = 0x230000;    // SF_FORMAT_MPEG, added in 1.1.0
			private const int FormatOpus = 0x0064;      // SF_FORMAT_OPUS, added in 1.0.29

			/// <summary>A format table longer than this means the layout assumed below is wrong for this build.</summary>
			private const int MaxTableEntries = 4096;

			// SF_INFO, unchanged since 1.0.0:
			//     sf_count_t frames; int samplerate; int channels; int format; int sections; int seekable;
			// sf_count_t is int64_t on every platform libsndfile builds for, including 32-bit ones, so the int
			// fields always start at offset 8 and the struct is 32 bytes on LP64, 28 on i386 (int64 aligned to 4)
			// and 32 on ARM EABI (aligned to 8). The block handed to sf_open is 64 zeroed bytes, which is larger
			// than any of those, and only the first 20 bytes are ever read back; sf_open takes a pointer and no
			// size, so the extra room costs nothing and cannot be misread. A build whose layout differs from this
			// yields an impossible rate or channel count, which the caller refuses by name.
			internal const int InfoFramesOffset = 0;
			internal const int InfoRateOffset = 8;
			internal const int InfoChannelsOffset = 12;
			internal const int InfoBlockBytes = 64;

			// SF_FORMAT_INFO: int format; const char *name; const char *extension. Pointer alignment puts it at 24
			// bytes on LP64 and 12 on ILP32. sf_command rejects a datasize that is not exactly sizeof(SF_FORMAT_INFO),
			// so a wrong size here loses the mp3 and opus claims and cannot corrupt anything.
			private static readonly int FormatInfoBytes = IntPtr.Size == 8 ? 24 : 12;

			private static readonly object gate = new ();
			private static int state;               // 0 unknown, 1 ready, 2 failed
			private static string failure;

			internal static nint Handle;            // rooted for the process lifetime once loaded

			/// <summary>The extensions this build actually decodes, filled once by <see cref="TryLoad"/>.</summary>
			internal static string[] Formats = [];

			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnOpen(nint path, int mode, nint info);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnClose(nint file);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate long FnReadFloat(nint file, nint buffer, long frames);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint FnStrError(nint file);
			[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FnCommand(nint file, int command, nint data, int dataSize);

			internal static FnOpen Open;
			internal static FnClose Close;
			internal static FnReadFloat ReadFloat;
			internal static FnStrError StrErrorRaw;
			internal static FnCommand Command;      // optional; absence costs only the mp3 and opus claims

			internal static bool TryLoad(out string reason)
			{
				lock (gate)
				{
					if (state == 1)
					{
						reason = "";
						return true;
					}

					if (state == 2)
					{
						reason = failure;
						return false;
					}

					if (!NativeLibrary.TryLoad(Library, out Handle) || Handle == 0)
					{
						Handle = 0;
						state = 2;
						failure = $"The decoding library {Library} was not found. Install it (Debian/Ubuntu: libsndfile1, Fedora/RHEL: libsndfile, openSUSE: libsndfile1, Arch: libsndfile).";
						reason = failure;
						return false;
					}

					string missing = null;
					Open = Bind<FnOpen>("sf_open", ref missing);
					Close = Bind<FnClose>("sf_close", ref missing);
					ReadFloat = Bind<FnReadFloat>("sf_readf_float", ref missing);
					StrErrorRaw = Bind<FnStrError>("sf_strerror", ref missing);
					Command = BindOptional<FnCommand>("sf_command");

					if (missing != null)
					{
						state = 2;
						failure = $"{Library} is missing the required export '{missing}'; install a libsndfile 1.0.0 or newer runtime.";
						reason = failure;
						return false;
					}

					Formats = Enumerate();
					state = 1;
					reason = "";
					return true;
				}
			}

			/// <summary>The last libsndfile error, or blank. Pass 0 for the error a failed <c>sf_open</c> left behind.</summary>
			internal static string LastError(nint file)
			{
				try
				{
					return Marshal.PtrToStringUTF8(StrErrorRaw(file)) ?? "";
				}
				catch (Exception)
				{
					return "";
				}
			}

			/// <summary>
			/// Derives the claim list from the loaded build's own tables. mp3 arrived in libsndfile 1.1.0 and Opus in
			/// 1.0.29, and distributions additionally compile those in or out, so a version string is not a usable
			/// stand-in for what this build can do: the major-format table is walked for SF_FORMAT_MPEG and the
			/// subtype table for SF_FORMAT_OPUS, which is a subtype of the Ogg container rather than a major format
			/// of its own. The six containers below are claimed whenever the library loads at all.
			/// </summary>
			private static string[] Enumerate()
			{
				var claims = new List<string> { "flac", "ogg", "aiff", "au", "caf", "w64" };

				// A libsndfile without sf_command cannot describe itself, so nothing beyond the safe list is claimed.
				if (Command == null)
					return [.. claims];

				var block = Marshal.AllocHGlobal(FormatInfoBytes + 8);

				try
				{
					if (Has(block, GetFormatMajorCount, GetFormatMajor, TypeMask, FormatMpeg))
						claims.Add("mp3");

					if (Has(block, GetFormatSubtypeCount, GetFormatSubtype, SubMask, FormatOpus))
						claims.Add("opus");
				}
				catch (Exception)
				{
					// The tables are how a build describes itself; one that refuses to keeps only the safe claims.
				}
				finally
				{
					Marshal.FreeHGlobal(block);
				}

				return [.. claims];
			}

			/// <summary>Whether one of libsndfile's two format tables names <paramref name="wanted"/>.</summary>
			/// <param name="block">Scratch large enough for an SF_FORMAT_INFO.</param>
			/// <param name="countCommand">The selector returning how many entries the table has.</param>
			/// <param name="entryCommand">The selector filling one entry by index.</param>
			/// <param name="mask">SF_FORMAT_TYPEMASK for a major format, SF_FORMAT_SUBMASK for a subtype.</param>
			/// <param name="wanted">The masked format code to look for.</param>
			private static bool Has(nint block, int countCommand, int entryCommand, int mask, int wanted)
			{
				Marshal.WriteInt32(block, 0, 0);

				// Both count selectors return 0 on success and leave the count in the int they were handed.
				if (Command(0, countCommand, block, sizeof(int)) != 0)
					return false;

				var count = Marshal.ReadInt32(block, 0);

				if (count <= 0 || count > MaxTableEntries)
					return false;

				for (var i = 0; i < count; i++)
				{
					// Zeroed each time so a rejected call cannot leave a previous entry's code to be read as this one.
					for (var b = 0; b < FormatInfoBytes; b++)
						Marshal.WriteByte(block, b, 0);

					Marshal.WriteInt32(block, 0, i);

					if (Command(0, entryCommand, block, FormatInfoBytes) == 0 && (Marshal.ReadInt32(block, 0) & mask) == wanted)
						return true;
				}

				return false;
			}

			private static T Bind<T>(string name, ref string missing) where T : Delegate
			{
				var bound = BindOptional<T>(name);

				if (bound == null)
					missing ??= name;

				return bound;
			}

			private static T BindOptional<T>(string name) where T : Delegate
			{
				try
				{
					if (NativeLibrary.TryGetExport(Handle, name, out var address) && address != 0)
						return Marshal.GetDelegateForFunctionPointer<T>(address);
				}
				catch (Exception)
				{
				}

				return null;
			}
		}

		/// <summary>
		/// The external decoder used when libsndfile is absent. Both tools are located once with the same PATH scan
		/// <c>SoundPlayback</c> uses to find a player, which is private to that class; no shell is ever involved, so
		/// a directory name with a space or a quote in it is harmless.
		/// </summary>
		private static class Ffmpeg
		{
			private static readonly object gate = new ();
			private static int state;               // 0 unknown, 1 ready, 2 failed
			private static string failure;

			internal static string Exe;

			/// <summary>ffprobe, when the host has it. Its absence costs only the source's own rate and channels.</summary>
			internal static string Probe;

			internal static bool TryLocate(out string reason)
			{
				lock (gate)
				{
					if (state == 1)
					{
						reason = "";
						return true;
					}

					if (state == 2)
					{
						reason = failure;
						return false;
					}

					Exe = ResolveOnPath("ffmpeg");
					Probe = ResolveOnPath("ffprobe");

					if (Exe == null)
					{
						state = 2;
						failure = "No ffmpeg was found on PATH to decode with either; install ffmpeg for mp3, m4a, aac, ogg, opus, flac and wma.";
						reason = failure;
						return false;
					}

					state = 1;
					reason = "";
					return true;
				}
			}

			/// <summary>The first PATH entry holding an executable of this name, or null when there is none.</summary>
			private static string ResolveOnPath(string exe)
			{
				foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
				{
					try
					{
						var full = Path.Combine(dir.Trim(), exe);

						if (File.Exists(full))
							return full;
					}
					catch (Exception)
					{
						// A malformed PATH entry must not stop the scan.
					}
				}

				return null;
			}
		}
	}
}
#endif
