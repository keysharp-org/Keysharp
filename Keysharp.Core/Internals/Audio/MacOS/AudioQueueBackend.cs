#if OSX
using static Keysharp.Internals.Os.MacOS.CoreAudioHal;

namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// The macOS audio backend: the Core Audio HAL for device discovery, endpoint volume, mute, activity and
	/// change notification, and AudioToolbox AudioQueue for playback and microphone capture. Every framework
	/// is reached by absolute path and probed once, so an unusual system produces <see cref="IsAvailable"/>
	/// false plus an actionable reason instead of a type-initialization exception. Per-application sessions
	/// and native level meters have no public macOS mechanism, so those two surfaces report unsupported with
	/// a reason rather than approximating one. Compressed file decoding goes through the AudioToolbox
	/// ExtAudioFile layer, probed separately so a host without it reports fewer formats instead of failing
	/// at decode time.
	/// </summary>
	internal sealed class AudioQueueBackend : IAudioBackend
	{
		private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

		// ---- Core Audio selectors, mirroring the values Sound.cs already relies on ----------------------
		private const uint kAudioHardwarePropertyDevices                          = 0x64657623u; // 'dev#'
		private const uint kAudioHardwarePropertyDefaultOutputDevice              = 0x644F7574u; // 'dOut'
		private const uint kAudioHardwarePropertyDefaultInputDevice               = 0x64496E20u; // 'dIn '
		private const uint kAudioDevicePropertyDeviceUID                          = 0x75696420u; // 'uid '
		private const uint kAudioDevicePropertyDeviceIsRunningSomewhere           = 0x676F6E65u; // 'gone'
		private const uint kAudioDevicePropertyNominalSampleRate                  = 0x6E737274u; // 'nsrt'

		// ---- AudioToolbox ------------------------------------------------------------------------------
		private const uint kAudioFormatLinearPCM        = 0x6C70636Du; // 'lpcm'
		private const uint kLinearPCMFormatFlagIsFloat  = 1u << 0;
		private const uint kLinearPCMFormatFlagIsPacked = 1u << 3;

		// 'aqcd'. This is the one AudioQueue constant with no shipping Keysharp call site to cross-check
		// against, so a failed set is reported as an open failure instead of being ignored: playing to, or
		// recording from, the wrong endpoint would be worse than refusing to open.
		private const uint kAudioQueuePropertyCurrentDevice = 0x61716364u;

		/// <summary>Three buffers is the AudioQueue convention: one playing, one queued, one being filled.</summary>
		private const int BufferCount = 3;

		private const double DefaultLatencyMilliseconds = 60.0;
		private const int DefaultSampleRate = 48_000;
		private const int MinFramesPerBuffer = 128;
		private const int MaxFramesPerBuffer = 16_384;

		/// <summary>CoreAudioTypes AudioStreamBasicDescription: forty bytes of scalars with no padding.</summary>
		[StructLayout(LayoutKind.Sequential)]
		private struct AudioStreamBasicDescription
		{
			public double mSampleRate;
			public uint mFormatID;
			public uint mFormatFlags;
			public uint mBytesPerPacket;
			public uint mFramesPerPacket;
			public uint mBytesPerFrame;
			public uint mChannelsPerFrame;
			public uint mBitsPerChannel;
			public uint mReserved;
		}

		/// <summary>
		/// AudioQueue.h AudioQueueBuffer, laid out explicitly because the render callback dereferences it in
		/// place. The offsets are the 64-bit natural-alignment layout, and macOS ships only 64-bit targets.
		/// The trailing packet-description fields are omitted: LPCM enqueues carry no packet descriptions.
		/// </summary>
		[StructLayout(LayoutKind.Explicit)]
		private struct AudioQueueBuffer
		{
			[FieldOffset(0)] public uint mAudioDataBytesCapacity;
			[FieldOffset(8)] public nint mAudioData;
			[FieldOffset(16)] public uint mAudioDataByteSize;
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void AudioQueueOutputCallback(nint userData, nint queue, nint buffer);

		/// <summary>
		/// AudioQueue.h AudioQueueInputCallback. The start time arrives as a pointer to an AudioTimeStamp and
		/// the packet descriptions are absent for LPCM, so neither is dereferenced; the frame count comes from
		/// the buffer itself.
		/// </summary>
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void AudioQueueInputCallback(nint userData, nint queue, nint buffer, nint startTime, uint numberPacketDescriptions, nint packetDescs);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate int AudioObjectPropertyListenerProc(uint objectId, uint numberAddresses, nint addresses, nint clientData);

		[DllImport(CoreAudio)]
		private static extern int AudioObjectAddPropertyListener(uint objectId, ref AudioObjectPropertyAddress addr, AudioObjectPropertyListenerProc listener, nint clientData);

		[DllImport(CoreAudio)]
		private static extern int AudioObjectRemovePropertyListener(uint objectId, ref AudioObjectPropertyAddress addr, AudioObjectPropertyListenerProc listener, nint clientData);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueNewOutput(ref AudioStreamBasicDescription format, AudioQueueOutputCallback callback, nint userData, nint callbackRunLoop, nint callbackRunLoopMode, uint flags, out nint audioQueue);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueNewInput(ref AudioStreamBasicDescription format, AudioQueueInputCallback callback, nint userData, nint callbackRunLoop, nint callbackRunLoopMode, uint flags, out nint audioQueue);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueAllocateBuffer(nint audioQueue, uint bufferByteSize, out nint buffer);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueEnqueueBuffer(nint audioQueue, nint buffer, uint numPacketDescs, nint packetDescs);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueStart(nint audioQueue, nint startTime);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueuePause(nint audioQueue);

		// Boolean is an unsigned char in the C API, so it crosses as a byte rather than as a marshalled bool.
		[DllImport(AudioToolbox)]
		private static extern int AudioQueueStop(nint audioQueue, byte immediate);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueDispose(nint audioQueue, byte immediate);

		[DllImport(AudioToolbox)]
		private static extern int AudioQueueSetProperty(nint audioQueue, uint propertyId, nint data, uint dataSize);

		// ---- one-shot probe -----------------------------------------------------------------------------

		private static readonly object probeLock = new ();
		private static int probed;
		private static bool coreAudioReady;
		private static bool audioToolboxReady;
		private static string coreAudioReason = "";
		private static string audioToolboxReason = "";

		/// <summary>
		/// Loads and exercises each framework exactly once. A missing framework or a dead audio server is an
		/// expected outcome, so every failure becomes a reason string rather than an exception.
		/// </summary>
		private static void EnsureProbed()
		{
			if (Volatile.Read(ref probed) != 0)
				return;

			lock (probeLock)
			{
				if (probed != 0)
					return;

				try
				{
					ProbeCoreAudio();
				}
				catch (Exception ex)
				{
					coreAudioReady = false;
					coreAudioReason = $"Core Audio could not be used: {ex.Message}. Keysharp needs the CoreAudio framework at {CoreAudio}.";
				}

				try
				{
					ProbeAudioToolbox();
				}
				catch (Exception ex)
				{
					audioToolboxReady = false;
					audioToolboxReason = $"AudioQueue playback could not be used: {ex.Message}. Keysharp needs the AudioToolbox framework at {AudioToolbox}.";
				}

				Volatile.Write(ref probed, 1);
			}
		}

		private static void ProbeCoreAudio()
		{
			// The handle is deliberately not freed: it keeps the framework loaded for the DllImports above.
			if (!NativeLibrary.TryLoad(CoreAudio, out var handle) || handle == nint.Zero)
			{
				coreAudioReason = $"The CoreAudio framework was not found at {CoreAudio}. Run Keysharp on a standard macOS installation.";
				return;
			}

			var addr = Address(kAudioHardwarePropertyDevices, kAudioObjectPropertyScopeGlobal);
			var status = AudioObjectGetPropertyDataSize(kAudioObjectSystemObject, ref addr, 0, nint.Zero, out _);

			if (status != 0)
			{
				coreAudioReason = $"Core Audio refused the device list query (status 0x{(uint)status:X8}). Restart the audio server with: sudo launchctl kickstart -k system/com.apple.audio.coreaudiod";
				return;
			}

			coreAudioReady = true;
		}

		private static void ProbeAudioToolbox()
		{
			if (!NativeLibrary.TryLoad(AudioToolbox, out var handle) || handle == nint.Zero)
			{
				audioToolboxReason = $"The AudioToolbox framework was not found at {AudioToolbox}. Run Keysharp on a standard macOS installation.";
				return;
			}

			if (!NativeLibrary.TryGetExport(handle, "AudioQueueNewOutput", out _))
			{
				audioToolboxReason = $"The AudioToolbox framework at {AudioToolbox} exports no AudioQueueNewOutput, so playback cannot start. Run Keysharp on a supported macOS version.";
				return;
			}

			audioToolboxReady = true;
		}

		private static readonly object captureProbeLock = new ();
		private static int captureProbed;
		private static bool audioQueueInputReady;
		private static string audioQueueInputReason = "";

		/// <summary>
		/// Probes the recording half of AudioToolbox exactly once, kept apart from <see cref="EnsureProbed"/>
		/// so a system that plays but cannot record still reports playback correctly. Looking up an export
		/// opens no device and triggers no privacy prompt, which is what lets the capture probe be cached.
		/// </summary>
		private static void EnsureCaptureProbed()
		{
			EnsureProbed();

			if (Volatile.Read(ref captureProbed) != 0)
				return;

			lock (captureProbeLock)
			{
				if (captureProbed != 0)
					return;

				try
				{
					if (!audioToolboxReady)
					{
						audioQueueInputReason = audioToolboxReason.Length != 0 ? audioToolboxReason : $"The AudioToolbox framework at {AudioToolbox} is unavailable, so recording cannot start.";
					}
					// As above, the handle is deliberately not freed: it keeps the framework loaded.
					else if (!NativeLibrary.TryLoad(AudioToolbox, out var handle) || handle == nint.Zero || !NativeLibrary.TryGetExport(handle, "AudioQueueNewInput", out _))
					{
						audioQueueInputReason = $"The AudioToolbox framework at {AudioToolbox} exports no AudioQueueNewInput, so recording cannot start. Run Keysharp on a supported macOS version.";
					}
					else
					{
						audioQueueInputReady = true;
					}
				}
				catch (Exception ex)
				{
					audioQueueInputReady = false;
					audioQueueInputReason = $"AudioQueue recording could not be used: {ex.Message}. Keysharp needs the AudioToolbox framework at {AudioToolbox}.";
				}

				Volatile.Write(ref captureProbed, 1);
			}
		}

		// ---- capability surface -------------------------------------------------------------------------

		public bool IsAvailable
		{
			get
			{
				EnsureProbed();
				return coreAudioReady;
			}
		}

		public bool Supports(AudioCapability capability)
		{
			EnsureProbed();
			return capability switch
			{
				AudioCapability.Playback => coreAudioReady && audioToolboxReady,
				AudioCapability.DeviceEnumeration => coreAudioReady,
				AudioCapability.DeviceChange => coreAudioReady,
				AudioCapability.EndpointVolume => coreAudioReady,
				AudioCapability.DeviceRunning => coreAudioReady,
				AudioCapability.NativeObject => coreAudioReady,
				AudioCapability.MicrophoneCapture => MicrophoneCaptureReady(),
				// Core Audio models devices and streams, not application sessions, and publishes no endpoint peak.
				AudioCapability.Sessions or AudioCapability.Metering or AudioCapability.SystemAudioCapture => false,
				AudioCapability.Decoding => coreAudioReady && SupportedFormats.Length > 0,
				_ => false,
			};
		}

		/// <summary>Blank when the capability works, otherwise the concrete thing the user has to fix.</summary>
		public string UnsupportedReason(AudioCapability capability)
		{
			EnsureProbed();

			if (Supports(capability))
				return "";

			if (capability == AudioCapability.Playback && coreAudioReady)
				return audioToolboxReason.Length != 0 ? audioToolboxReason : "AudioQueue playback is unavailable on this system.";

			// These three are absent from the platform itself rather than from this host, so they name the reason
			// the API does not exist rather than something the user could install or start.
			switch (capability)
			{
				case AudioCapability.Sessions:
					return SessionReason;

				case AudioCapability.Metering:
					return MeterReason;

				case AudioCapability.SystemAudioCapture:
					return SystemOutputReason;

				case AudioCapability.MicrophoneCapture:
					EnsureCaptureProbed();

					if (!coreAudioReady && coreAudioReason.Length != 0)
						return coreAudioReason;

					return audioQueueInputReason.Length != 0 ? audioQueueInputReason : "AudioQueue recording is unavailable on this system.";
			}

			if (coreAudioReason.Length != 0)
				return coreAudioReason;

			return $"The macOS audio backend does not provide {capability}.";
		}

		public void Dispose()
		{
			// Streams and watchers own their native lifetimes, so the backend itself holds nothing to release.
		}

		// ---- enumeration ---------------------------------------------------------------------------------

		public AudioDeviceDescriptor[] EnumerateDevices(AudioDeviceKind kind)
		{
			var snapshot = Snapshot(kind);

			if (snapshot.Count == 0)
				return [];

			var result = new AudioDeviceDescriptor[snapshot.Count];

			for (var i = 0; i < result.Length; i++)
				result[i] = snapshot[i].Descriptor;

			return result;
		}

		public bool TryGetDefaultDevice(AudioDeviceKind kind, out AudioDeviceDescriptor device)
		{
			device = default;
			EnsureProbed();

			if (!coreAudioReady)
				return false;

			try
			{
				var id = GetDefaultDeviceId(kind);

				if (id == 0)
					return false;

				device = Describe(id, kind, true);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool TryGetDevice(string id, out AudioDeviceDescriptor device)
		{
			device = default;

			if (!TryParseKind(id, out var kind))
				return false;

			foreach (var entry in Snapshot(kind))
			{
				if (string.Equals(entry.Descriptor.Id, id, StringComparison.Ordinal))
				{
					device = entry.Descriptor;
					return true;
				}
			}

			return false;
		}

		/// <summary>One device as both the HAL object id and the descriptor built from it.</summary>
		private readonly record struct DeviceEntry(uint Native, AudioDeviceDescriptor Descriptor);

		/// <summary>
		/// Snapshots older than this are re-read. A HAL listener marks them stale sooner, so this only bounds how
		/// long a change no listener reported can go unnoticed. Matches what the Pulse backend keeps.
		/// </summary>
		private const long SnapshotLifetimeMs = 500;

		private static readonly object snapshotGate = new ();
		private static readonly List<DeviceEntry>[] cachedSnapshots = new List<DeviceEntry>[2];
		private static readonly long[] cachedStamps = new long[2];
		private static int snapshotDirty = 1;

		/// <summary>Marks both cached views stale. Called from the HAL listener, which must stay bounded.</summary>
		private static void InvalidateSnapshots() => Volatile.Write(ref snapshotDirty, 1);

		/// <summary>
		/// Every present device that carries at least one stream in the requested direction, in the HAL's own
		/// order. Cached, because every endpoint member resolves its device through here and the walk costs a
		/// property read per device plus two CFString reads for each one that qualifies.
		/// </summary>
		private List<DeviceEntry> Snapshot(AudioDeviceKind kind)
		{
			EnsureProbed();

			if (!coreAudioReady)
				return [];

			var slot = kind == AudioDeviceKind.Output ? 0 : 1;

			lock (snapshotGate)
			{
				if (Interlocked.Exchange(ref snapshotDirty, 0) != 0)
				{
					cachedSnapshots[0] = null;
					cachedSnapshots[1] = null;
				}

				var cached = cachedSnapshots[slot];

				if (cached != null && Environment.TickCount64 - cachedStamps[slot] <= SnapshotLifetimeMs)
					return cached;

				var fresh = ReadSnapshot(kind);
				cachedSnapshots[slot] = fresh;
				cachedStamps[slot] = Environment.TickCount64;
				return fresh;
			}
		}

		private static List<DeviceEntry> ReadSnapshot(AudioDeviceKind kind)
		{
			var list = new List<DeviceEntry>();

			try
			{
				var ids = GetPropertyUInts(kAudioObjectSystemObject, Address(kAudioHardwarePropertyDevices, kAudioObjectPropertyScopeGlobal));
				var scope = ScopeOf(kind);
				var defaultId = GetDefaultDeviceId(kind);

				foreach (var id in ids)
				{
					if (GetChannelCount(id, scope) <= 0)
						continue;

					list.Add(new DeviceEntry(id, Describe(id, kind, id != 0 && id == defaultId)));
				}
			}
			catch (Exception)
			{
				list.Clear();
			}

			return list;
		}

		private static AudioDeviceDescriptor Describe(uint deviceId, AudioDeviceKind kind, bool isDefault)
		{
			var uid = GetPropertyString(deviceId, Address(kAudioDevicePropertyDeviceUID, kAudioObjectPropertyScopeGlobal));
			var name = GetPropertyString(deviceId, Address(kAudioObjectPropertyName, kAudioObjectPropertyScopeGlobal));

			if (name.Length == 0)
				name = uid.Length != 0 ? uid : $"Audio device {deviceId}";

			return new AudioDeviceDescriptor(MakeId(kind, uid, deviceId), name, kind, isDefault);
		}

		/// <summary>
		/// One HAL object can carry both directions, so the kind prefix is what keeps ids unique across kinds.
		/// The UID survives reboots and reconnects; the numeric object id is the last resort and does not.
		/// </summary>
		private static string MakeId(AudioDeviceKind kind, string uid, uint deviceId)
		{
			var prefix = kind == AudioDeviceKind.Output ? "out:" : "in:";
			return uid.Length != 0 ? prefix + uid : prefix + "#" + deviceId.ToString();
		}

		/// <summary>Reads the kind an id was minted for, which is the prefix that makes ids unique.</summary>
		private static bool TryParseKind(string id, out AudioDeviceKind kind)
		{
			kind = AudioDeviceKind.Output;

			if (string.IsNullOrEmpty(id))
				return false;

			if (id.StartsWith("out:", StringComparison.Ordinal))
				return true;

			if (id.StartsWith("in:", StringComparison.Ordinal))
			{
				kind = AudioDeviceKind.Input;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Turns an opaque id back into a live HAL object, which fails once the device is gone. The id carries
		/// its own kind, so an id minted for the other direction is a miss rather than a silent redirection.
		/// </summary>
		private bool TryResolve(string id, out uint deviceId, out AudioDeviceKind kind)
		{
			deviceId = 0;

			if (!TryParseKind(id, out kind))
				return false;

			try
			{
				var uid = id.AsSpan(kind == AudioDeviceKind.Output ? 4 : 3);
				deviceId = GetDeviceId(uid);

				if (deviceId == 0 && uid.Length > 1 && uid[0] == '#'
					&& uint.TryParse(uid[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var numericId)
					&& GetPropertyString(numericId, Address(kAudioDevicePropertyDeviceUID, kAudioObjectPropertyScopeGlobal)).Length == 0)
					deviceId = numericId;

				if (deviceId != 0 && GetChannelCount(deviceId, ScopeOf(kind)) > 0)
					return true;
			}
			catch (Exception)
			{
			}

			deviceId = 0;
			return false;
		}

		/// <summary>The endpoint members carry a kind beside the id; a disagreement is a miss, not a redirect.</summary>
		private bool TryResolveFor(AudioDeviceKind kind, string id, out uint deviceId)
		{
			if (TryResolve(id, out deviceId, out var actual) && actual == kind)
				return true;

			deviceId = 0;
			return false;
		}

		private static uint GetDefaultDeviceId(AudioDeviceKind kind)
		{
			var selector = kind == AudioDeviceKind.Output ? kAudioHardwarePropertyDefaultOutputDevice : kAudioHardwarePropertyDefaultInputDevice;
			var addr = Address(selector, kAudioObjectPropertyScopeGlobal);
			return GetPropertyUInt(kAudioObjectSystemObject, addr, out var id) == 0 ? id : 0u;
		}

		// ---- volume, mute, activity ----------------------------------------------------------------------

		public bool TryGetVolume(AudioDeviceKind kind, string id, out double volume)
		{
			volume = 0;

			if (!Supports(AudioCapability.EndpointVolume) || !TryResolveFor(kind, id, out var deviceId))
				return false;

			try
			{
				if (GetDeviceVolume(deviceId, ScopeOf(kind), out var value) != 0)
					return false;

				volume = Math.Clamp(value, 0f, 1f);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool TrySetVolume(AudioDeviceKind kind, string id, double volume)
		{
			if (!Supports(AudioCapability.EndpointVolume) || !TryResolveFor(kind, id, out var deviceId))
				return false;

			try
			{
				return SetDeviceVolume(deviceId, ScopeOf(kind), (float)Math.Clamp(volume, 0.0, 1.0)) == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool TryGetMute(AudioDeviceKind kind, string id, out bool mute)
		{
			mute = false;

			if (!Supports(AudioCapability.EndpointVolume) || !TryResolveFor(kind, id, out var deviceId))
				return false;

			try
			{
				return GetDeviceMute(deviceId, ScopeOf(kind), out mute) == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool TrySetMute(AudioDeviceKind kind, string id, bool mute)
		{
			if (!Supports(AudioCapability.EndpointVolume) || !TryResolveFor(kind, id, out var deviceId))
				return false;

			try
			{
				return SetDeviceMute(deviceId, ScopeOf(kind), mute) == 0;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// Reads kAudioDevicePropertyDeviceIsRunningSomewhere. Bluetooth input devices are reported not to
		/// update this property and read as idle, so a false idle on those is expected rather than a bug.
		/// </summary>
		public bool TryGetIsRunning(AudioDeviceKind kind, string id, out bool running)
		{
			running = false;

			if (!Supports(AudioCapability.DeviceRunning) || !TryResolveFor(kind, id, out var deviceId))
				return false;

			try
			{
				var addr = Address(kAudioDevicePropertyDeviceIsRunningSomewhere, kAudioObjectPropertyScopeGlobal);

				if (GetPropertyUInt(deviceId, addr, out var value) != 0)
					return false;   // could not determine, which the caller surfaces as blank

				running = value != 0;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>
		/// macOS exposes no device object, only an AudioObjectID. That integer is exactly what a script needs
		/// to call CoreAudio itself through DllCall, so it is what the escape hatch hands out, as a long
		/// because that is the integer type scripts see.
		/// </summary>
		public object GetNativeDeviceObject(AudioDeviceKind kind, string id)
		{
			if (!Supports(AudioCapability.NativeObject) || !TryResolveFor(kind, id, out var deviceId))
				return null;

			return (long)deviceId;
		}

		// ---- device change notification -------------------------------------------------------------------

		private static readonly ConcurrentDictionary<long, DeviceWatcher> watchers = new ();
		private static readonly AudioObjectPropertyListenerProc listenerProc = OnHardwarePropertyChanged;
		private static long watcherTokens;

		public IAudioDeviceWatcher WatchDevices(Action sink)
		{
			EnsureProbed();

			if (sink == null || !coreAudioReady)
				return null;

			var token = Interlocked.Increment(ref watcherTokens);
			var watcher = new DeviceWatcher(token, sink);

			if (!watchers.TryAdd(token, watcher))
				return null;

			// Device arrival/removal and either default moving are all "the device picture changed".
			var selectors = new[] { kAudioHardwarePropertyDevices, kAudioHardwarePropertyDefaultOutputDevice, kAudioHardwarePropertyDefaultInputDevice };
			var installed = new List<uint>(selectors.Length);

			try
			{
				foreach (var selector in selectors)
				{
					var addr = Address(selector, kAudioObjectPropertyScopeGlobal);

					if (AudioObjectAddPropertyListener(kAudioObjectSystemObject, ref addr, listenerProc, (nint)token) == 0)
						installed.Add(selector);
				}
			}
			catch (Exception)
			{
				// Fall through: whatever was installed is torn down below when nothing took.
			}

			if (installed.Count == 0)
			{
				_ = watchers.TryRemove(token, out _);
				return null;
			}

			watcher.Installed = [.. installed];
			return watcher;
		}

		/// <summary>
		/// Runs on a Core Audio notification thread: one dictionary read, one delegate call, nothing else. An
		/// exception must never cross back into native code, so a failing sink is swallowed here.
		/// </summary>
		private static int OnHardwarePropertyChanged(uint objectId, uint numberAddresses, nint addresses, nint clientData)
		{
			try
			{
				InvalidateSnapshots();

				if (watchers.TryGetValue((long)clientData, out var watcher))
					watcher.Notify();
			}
			catch (Exception)
			{
			}

			return 0;
		}

		/// <summary>
		/// One installed set of HAL listeners. The registry entry, not a GCHandle, is what native code holds,
		/// because a removed Core Audio listener can still be in flight and a freed handle would crash it;
		/// a stale token simply misses the lookup.
		/// </summary>
		private sealed class DeviceWatcher : IAudioDeviceWatcher
		{
			private readonly long token;
			private Action sink;
			private int disposed;

			internal uint[] Installed = [];

			internal DeviceWatcher(long token, Action sink)
			{
				this.token = token;
				this.sink = sink;
			}

			internal void Notify() => Volatile.Read(ref sink)?.Invoke();

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				// Silence first, so a callback already running finds nothing to call.
				Volatile.Write(ref sink, null);

				try
				{
					foreach (var selector in Installed)
					{
						var addr = Address(selector, kAudioObjectPropertyScopeGlobal);
						_ = AudioObjectRemovePropertyListener(kAudioObjectSystemObject, ref addr, listenerProc, (nint)token);
					}
				}
				catch (Exception)
				{
				}

				_ = watchers.TryRemove(token, out _);
			}
		}

		// ---- playback --------------------------------------------------------------------------------------

		public bool TryOpenOutput(in AudioOutputRequest request, IAudioRenderSource source, out IAudioOutputStream stream, out string error)
		{
			stream = null;
			error = "";
			EnsureProbed();

			if (source == null)
			{
				error = "No render source was supplied for the audio output.";
				return false;
			}

			if (!coreAudioReady)
			{
				error = coreAudioReason;
				return false;
			}

			if (!audioToolboxReady)
			{
				error = audioToolboxReason;
				return false;
			}

			var requested = request.DeviceId ?? "";
			var uid = "";
			uint deviceId;

			try
			{
				if (requested.Length == 0)
				{
					// A blank id follows the current default, so the queue is left on its own device.
					deviceId = GetDefaultDeviceId(AudioDeviceKind.Output);

					if (deviceId == 0)
					{
						error = "No default audio output device is present.";
						return false;
					}
				}
				else
				{
					if (!TryResolve(requested, out deviceId, out var kind))
					{
						error = $"Audio device '{requested}' is not present.";
						return false;
					}

					if (kind != AudioDeviceKind.Output)
					{
						error = $"Audio device '{requested}' is an input device and cannot be opened for playback.";
						return false;
					}

					uid = GetPropertyString(deviceId, Address(kAudioDevicePropertyDeviceUID, kAudioObjectPropertyScopeGlobal));

					if (uid.Length == 0)
					{
						error = $"Audio device '{requested}' exposes no UID, which AudioQueue requires to select a device.";
						return false;
					}
				}

				var rate = TryGetSampleRate(deviceId, out var deviceRate) ? deviceRate : DefaultSampleRate;
				// The managed bus is mono or stereo; AudioQueue spreads that across whatever the device has.
				var channels = GetChannelCount(deviceId, kAudioObjectPropertyScopeOutput) >= 2 ? 2 : 1;
				var latency = request.RequestedLatencyMilliseconds > 0 ? request.RequestedLatencyMilliseconds : DefaultLatencyMilliseconds;

				if (OutputStream.TryCreate(source, rate, channels, latency, uid, out var created, out error))
				{
					stream = created;
					return true;
				}

				return false;
			}
			catch (Exception ex)
			{
				error = $"Opening the audio output failed: {ex.Message}";
				return false;
			}
		}

		/// <summary>
		/// Points one queue, input or output, at a specific endpoint by UID. The property value is the
		/// CFStringRef itself, so the queue is handed a pointer to that reference rather than the string.
		/// </summary>
		private static bool TrySetQueueDevice(nint queue, string uid, out string error)
		{
			error = "";
			var chars = Marshal.StringToHGlobalUni(uid);
			var cfString = nint.Zero;
			var holder = nint.Zero;

			try
			{
				cfString = CFStringCreateWithCharacters(nint.Zero, chars, uid.Length);

				if (cfString == nint.Zero)
				{
					error = $"The device UID '{uid}' could not be converted for AudioQueue.";
					return false;
				}

				holder = Marshal.AllocHGlobal(nint.Size);
				Marshal.WriteIntPtr(holder, cfString);
				var status = AudioQueueSetProperty(queue, kAudioQueuePropertyCurrentDevice, holder, (uint)nint.Size);

				if (status != 0)
				{
					error = $"AudioQueue refused device '{uid}' (status 0x{(uint)status:X8}).";
					return false;
				}

				return true;
			}
			finally
			{
				if (holder != nint.Zero)
					Marshal.FreeHGlobal(holder);

				if (cfString != nint.Zero)
					CFRelease(cfString);

				Marshal.FreeHGlobal(chars);
			}
		}

		/// <summary>
		/// One AudioQueue and its three buffers. The queue's own thread pulls the mixer through
		/// <see cref="FillAndEnqueue"/>; every managed object that thread can reach is rooted from queue
		/// creation until after AudioQueueDispose returns.
		/// </summary>
		private sealed class OutputStream : IAudioOutputStream
		{
			private static readonly AudioQueueOutputCallback renderProc = RenderCallback;

			private readonly IAudioRenderSource source;
			private readonly int bytesPerFrame;
			private readonly int channels;
			private readonly int framesPerBuffer;
			private readonly nint[] buffers = new nint[BufferCount];

			private GCHandle self;
			private nint queue;
			private int disposed;
			private int deviceLost;

			private OutputStream(IAudioRenderSource source, int sampleRate, int channels, int framesPerBuffer)
			{
				this.source = source;
				this.channels = channels;
				this.framesPerBuffer = framesPerBuffer;
				bytesPerFrame = channels * sizeof(float);
				Format = new AudioStreamFormat(sampleRate, channels);
				LatencyMilliseconds = BufferCount * framesPerBuffer * 1000.0 / sampleRate;
			}

			public AudioStreamFormat Format { get; }

			public double LatencyMilliseconds { get; }

			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			internal static bool TryCreate(IAudioRenderSource source, int sampleRate, int channels, double latencyMilliseconds, string uid, out OutputStream created, out string error)
			{
				created = null;
				var frames = (int)Math.Round(latencyMilliseconds / 1000.0 * sampleRate / BufferCount);
				frames = Math.Clamp(frames, MinFramesPerBuffer, MaxFramesPerBuffer);
				var stream = new OutputStream(source, sampleRate, channels, frames);

				if (!stream.Initialize(uid, out error))
				{
					stream.Dispose();
					return false;
				}

				created = stream;
				return true;
			}

			private bool Initialize(string uid, out string error)
			{
				error = "";
				// Rooted before the queue exists, since the queue's thread reaches this object through it.
				self = GCHandle.Alloc(this, GCHandleType.Normal);
				var format = new AudioStreamBasicDescription
				{
					mSampleRate = Format.SampleRate,
					mFormatID = kAudioFormatLinearPCM,
					mFormatFlags = kLinearPCMFormatFlagIsFloat | kLinearPCMFormatFlagIsPacked,
					mBytesPerPacket = (uint)bytesPerFrame,
					mFramesPerPacket = 1,
					mBytesPerFrame = (uint)bytesPerFrame,
					mChannelsPerFrame = (uint)channels,
					mBitsPerChannel = 32,
					mReserved = 0,
				};
				// A null run loop asks AudioQueue for its own callback thread, which is what a pull renderer wants.
				var status = AudioQueueNewOutput(ref format, renderProc, GCHandle.ToIntPtr(self), nint.Zero, nint.Zero, 0, out queue);

				if (status != 0 || queue == nint.Zero)
				{
					error = $"AudioQueueNewOutput failed (status 0x{(uint)status:X8}).";
					return false;
				}

				if (uid.Length != 0 && !TrySelectDevice(uid, out error))
					return false;

				for (var i = 0; i < BufferCount; i++)
				{
					status = AudioQueueAllocateBuffer(queue, (uint)(framesPerBuffer * bytesPerFrame), out buffers[i]);

					if (status != 0 || buffers[i] == nint.Zero)
					{
						error = $"AudioQueueAllocateBuffer failed (status 0x{(uint)status:X8}).";
						return false;
					}
				}

				// Prime every buffer before the queue runs. No voice exists yet, so this enqueues silence.
				for (var i = 0; i < BufferCount; i++)
					FillAndEnqueue(buffers[i]);

				return true;
			}

			private bool TrySelectDevice(string uid, out string error) => TrySetQueueDevice(queue, uid, out error);

			public void Start()
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				var q = queue;

				if (q == nint.Zero)
					return;

				if (AudioQueueStart(q, nint.Zero) != 0)
					Volatile.Write(ref deviceLost, 1);
			}

			/// <summary>
			/// Pauses rather than stops, which leaves all three buffers owned by the queue so a later Start
			/// needs no re-priming. The cost is that a resume replays whatever was mixed just before the
			/// pause, up to one full buffer set.
			/// </summary>
			public void Stop()
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				var q = queue;

				if (q != nint.Zero)
					_ = AudioQueuePause(q);
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				var q = Interlocked.Exchange(ref queue, nint.Zero);

				if (q != nint.Zero)
				{
					_ = AudioQueueStop(q, 1);
					_ = AudioQueueDispose(q, 1);   // immediate: returns only once the callback is quiescent
				}

				if (self.IsAllocated)
					self.Free();
			}

			/// <summary>
			/// The AudioQueue output callback. It runs on a native thread with no managed context of its own,
			/// so it allocates nothing, takes no lock, calls no script, and never lets an exception escape.
			/// </summary>
			private static void RenderCallback(nint userData, nint queue, nint buffer)
			{
				try
				{
					if (userData == nint.Zero || buffer == nint.Zero)
						return;

					if (GCHandle.FromIntPtr(userData).Target is OutputStream stream)
						stream.FillAndEnqueue(buffer);
				}
				catch (Exception)
				{
				}
			}

			private unsafe void FillAndEnqueue(nint bufferRef)
			{
				var buffer = (AudioQueueBuffer*)bufferRef;
				var frames = (int)(buffer->mAudioDataBytesCapacity / (uint)bytesPerFrame);
				var samples = frames * channels;
				var span = new Span<float>((void*)buffer->mAudioData, samples);

				if (Volatile.Read(ref disposed) != 0)
					return;   // the queue is going away; leave the buffer with it

				// A mixer that threw still owes the device a defined buffer; the flag keeps the span, a ref
				// struct, out of the handler itself.
				var filled = true;

				try
				{
					source.Fill(span);
				}
				catch (Exception)
				{
					filled = false;
				}

				if (!filled)
					span.Clear();

				buffer->mAudioDataByteSize = (uint)(samples * sizeof(float));
				var q = queue;

				if (q == nint.Zero || Volatile.Read(ref disposed) != 0)
					return;

				// A refused enqueue is how an explicitly selected device announces that it went away; with no
				// explicit device the queue follows the default instead and this never fires.
				if (AudioQueueEnqueueBuffer(q, bufferRef, 0, nint.Zero) != 0)
					Volatile.Write(ref deviceLost, 1);
			}
		}

		// ---- capture ----------------------------------------------------------------------------------

		private const string SystemOutputReason = "Capturing what a macOS device is playing is not implemented. It needs AudioHardwareCreateProcessTap plus an aggregate device to carry the tap, a verified AUHAL or AudioQueue transport on that device, and the system-audio privacy entitlement (NSAudioCaptureUsageDescription); the design gates all of that behind a feasibility spike that has not run, so no route is guessed at here. Record from a microphone instead, or install a loopback device such as BlackHole and select it as the input device.";

		/// <summary>
		/// Microphone capture is available whenever the recording half of AudioToolbox loaded; system-output
		/// capture is not implemented on macOS at all. The probe is a symbol lookup, so it never opens a
		/// device and never prompts, and it deliberately says nothing about privacy authorization: macOS
		/// exposes no status this backend can read without linking AVFoundation, so the recorder's own
		/// permission gate is what decides whether an open is attempted.
		/// </summary>
		private bool MicrophoneCaptureReady()
		{
			EnsureCaptureProbed();
			return coreAudioReady && audioQueueInputReady;
		}

		/// <summary>
		/// Opens one AudioQueue input. A blank device id follows the current default input; a selected id must
		/// have been minted for an input device, because recording the wrong endpoint yields plausible audio
		/// from the wrong source rather than an obvious failure.
		/// </summary>
		/// <remarks>
		/// Microphone access is enforced by TCC against the bundle's Info.plist, which must carry
		/// NSMicrophoneUsageDescription. The Keysharp bundle does not carry that key yet, and macOS terminates
		/// a process that touches an input device without it instead of returning an error, so this path
		/// cannot be exercised until packaging adds the key.
		/// </remarks>
		public bool TryOpenInput(in AudioInputRequest request, IAudioCaptureSink sink, out IAudioInputStream stream, out string error)
		{
			stream = null;
			error = "";
			EnsureCaptureProbed();

			if (sink == null)
			{
				error = "No capture sink was supplied for the audio input.";
				return false;
			}

			if (request.Source != AudioCaptureSource.Microphone)
			{
				var want = request.Source == AudioCaptureSource.Microphone
						   ? AudioCapability.MicrophoneCapture
						   : AudioCapability.SystemAudioCapture;
				error = UnsupportedReason(want);
				return false;
			}

			if (!coreAudioReady)
			{
				error = coreAudioReason;
				return false;
			}

			if (!audioQueueInputReady)
			{
				error = audioQueueInputReason;
				return false;
			}

			var requested = request.DeviceId ?? "";
			var uid = "";
			uint deviceId;

			try
			{
				if (requested.Length == 0)
				{
					// A blank id follows the current default, so the queue is left on its own device.
					deviceId = GetDefaultDeviceId(AudioDeviceKind.Input);

					if (deviceId == 0)
					{
						error = "No default audio input device is present.";
						return false;
					}
				}
				else
				{
					if (!TryResolve(requested, out deviceId, out var kind))
					{
						error = $"Audio device '{requested}' is not present.";
						return false;
					}

					if (kind != AudioDeviceKind.Input)
					{
						error = $"Audio device '{requested}' is an output device and cannot be opened for capture.";
						return false;
					}

					uid = GetPropertyString(deviceId, Address(kAudioDevicePropertyDeviceUID, kAudioObjectPropertyScopeGlobal));

					if (uid.Length == 0)
					{
						error = $"Audio device '{requested}' exposes no UID, which AudioQueue requires to select a device.";
						return false;
					}
				}

				var deviceRate = TryGetSampleRate(deviceId, out var rate) ? rate : DefaultSampleRate;
				var deviceChannels = GetChannelCount(deviceId, kAudioObjectPropertyScopeInput) >= 2 ? 2 : 1;
				var wantRate = AudioFormats.IsValidSampleRate(request.SampleRate) ? request.SampleRate : deviceRate;
				var wantChannels = AudioFormats.IsValidChannels(request.Channels) ? request.Channels : deviceChannels;

				if (InputStream.TryCreate(sink, wantRate, wantChannels, request.ChunkMilliseconds, uid, out var created, out error))
				{
					stream = created;
					return true;
				}

				// AudioQueue converts for an input queue only within limits it does not publish, so a refused
				// format retries at the device's own and the stream reports the format it actually got.
				if (wantRate != deviceRate || wantChannels != deviceChannels)
				{
					var refused = error;

					if (InputStream.TryCreate(sink, deviceRate, deviceChannels, request.ChunkMilliseconds, uid, out created, out error))
					{
						stream = created;
						return true;
					}

					error = $"{refused} The device's own format ({deviceRate} Hz, {deviceChannels} channels) was refused as well: {error}";
				}

				return false;
			}
			catch (Exception ex)
			{
				error = $"Opening the audio input failed: {ex.Message}";
				return false;
			}
		}

		/// <summary>
		/// One AudioQueue input and its three buffers. The queue's own thread hands captured frames to the
		/// sink through <see cref="DeliverAndEnqueue"/>; every managed object that thread can reach is rooted
		/// from queue creation until after AudioQueueDispose returns.
		/// </summary>
		private sealed class InputStream : IAudioInputStream
		{
			private static readonly AudioQueueInputCallback captureProc = CaptureCallback;

			private readonly IAudioCaptureSink sink;
			private readonly int bytesPerFrame;
			private readonly int channels;
			private readonly int framesPerBuffer;
			private readonly nint[] buffers = new nint[BufferCount];

			private GCHandle self;
			private nint queue;
			private int disposed;
			private int deviceLost;

			private InputStream(IAudioCaptureSink sink, int sampleRate, int channels, int framesPerBuffer)
			{
				this.sink = sink;
				this.channels = channels;
				this.framesPerBuffer = framesPerBuffer;
				bytesPerFrame = channels * sizeof(float);
				Format = new AudioStreamFormat(sampleRate, channels);
			}

			public AudioStreamFormat Format { get; }

			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			internal static bool TryCreate(IAudioCaptureSink sink, int sampleRate, int channels, double chunkMilliseconds, string uid, out InputStream created, out string error)
			{
				created = null;
				var frames = (int)Math.Round(Math.Clamp(chunkMilliseconds, 10.0, 1000.0) / 1000.0 * sampleRate);
				frames = Math.Clamp(frames, MinFramesPerBuffer, MaxFramesPerBuffer);
				var stream = new InputStream(sink, sampleRate, channels, frames);

				if (!stream.Initialize(uid, out error))
				{
					stream.Dispose();
					return false;
				}

				created = stream;
				return true;
			}

			private bool Initialize(string uid, out string error)
			{
				error = "";
				// Rooted before the queue exists, since the queue's thread reaches this object through it.
				self = GCHandle.Alloc(this, GCHandleType.Normal);
				var format = new AudioStreamBasicDescription
				{
					mSampleRate = Format.SampleRate,
					mFormatID = kAudioFormatLinearPCM,
					mFormatFlags = kLinearPCMFormatFlagIsFloat | kLinearPCMFormatFlagIsPacked,
					mBytesPerPacket = (uint)bytesPerFrame,
					mFramesPerPacket = 1,
					mBytesPerFrame = (uint)bytesPerFrame,
					mChannelsPerFrame = (uint)channels,
					mBitsPerChannel = 32,
					mReserved = 0,
				};
				// A null run loop asks AudioQueue for its own callback thread, matching the playback path.
				var status = AudioQueueNewInput(ref format, captureProc, GCHandle.ToIntPtr(self), nint.Zero, nint.Zero, 0, out queue);

				if (status != 0 || queue == nint.Zero)
				{
					error = $"AudioQueueNewInput failed (status 0x{(uint)status:X8}).";
					return false;
				}

				if (uid.Length != 0 && !TrySetQueueDevice(queue, uid, out error))
					return false;

				for (var i = 0; i < BufferCount; i++)
				{
					status = AudioQueueAllocateBuffer(queue, (uint)(framesPerBuffer * bytesPerFrame), out buffers[i]);

					if (status != 0 || buffers[i] == nint.Zero)
					{
						error = $"AudioQueueAllocateBuffer failed (status 0x{(uint)status:X8}).";
						return false;
					}
				}

				// Enqueueing an input buffer hands it to the queue to be filled, so every buffer starts there.
				for (var i = 0; i < BufferCount; i++)
				{
					status = AudioQueueEnqueueBuffer(queue, buffers[i], 0, nint.Zero);

					if (status != 0)
					{
						error = $"AudioQueueEnqueueBuffer failed (status 0x{(uint)status:X8}).";
						return false;
					}
				}

				return true;
			}

			/// <summary>
			/// Starts capture. Without NSMicrophoneUsageDescription in the bundle Info.plist macOS terminates
			/// the process here rather than failing the call, so a nonzero status means the device went away,
			/// never that access was refused.
			/// </summary>
			public void Start()
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				var q = queue;

				if (q == nint.Zero)
					return;

				if (AudioQueueStart(q, nint.Zero) != 0)
					Volatile.Write(ref deviceLost, 1);
			}

			/// <summary>
			/// Pauses rather than stops, which leaves all three buffers owned by the queue so a later Start
			/// needs no re-enqueue. Nothing is captured while paused, so this costs no frames.
			/// </summary>
			public void Stop()
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				var q = queue;

				if (q != nint.Zero)
					_ = AudioQueuePause(q);
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				var q = Interlocked.Exchange(ref queue, nint.Zero);

				if (q != nint.Zero)
				{
					_ = AudioQueueStop(q, 1);
					_ = AudioQueueDispose(q, 1);   // immediate: returns only once the callback is quiescent
				}

				if (self.IsAllocated)
					self.Free();
			}

			/// <summary>
			/// The AudioQueue input callback. It runs on a native thread with no managed context of its own,
			/// so it allocates nothing, takes no lock, calls no script, and never lets an exception escape.
			/// </summary>
			private static void CaptureCallback(nint userData, nint queue, nint buffer, nint startTime, uint numberPacketDescriptions, nint packetDescs)
			{
				try
				{
					if (userData == nint.Zero || buffer == nint.Zero)
						return;

					if (GCHandle.FromIntPtr(userData).Target is InputStream stream)
						stream.DeliverAndEnqueue(buffer);
				}
				catch (Exception)
				{
				}
			}

			private unsafe void DeliverAndEnqueue(nint bufferRef)
			{
				if (Volatile.Read(ref disposed) != 0)
					return;   // the queue is going away; leave the buffer with it

				var buffer = (AudioQueueBuffer*)bufferRef;
				var samples = (int)(buffer->mAudioDataByteSize / sizeof(float));
				// A short buffer at the end of a run is normal, so only whole frames are ever published.
				samples -= samples % channels;

				if (samples > 0 && buffer->mAudioData != nint.Zero)
				{
					var frames = new ReadOnlySpan<float>((void*)buffer->mAudioData, samples);

					try
					{
						sink.Write(frames);
					}
					catch (Exception)
					{
						// A sink that threw still owes the queue its buffer, so the enqueue below still runs.
					}
				}

				var q = queue;

				if (q == nint.Zero || Volatile.Read(ref disposed) != 0)
					return;

				// A refused enqueue is how an explicitly selected device announces that it went away; with no
				// explicit device the queue follows the default instead and this never fires.
				if (AudioQueueEnqueueBuffer(q, bufferRef, 0, nint.Zero) != 0)
					Volatile.Write(ref deviceLost, 1);
			}
		}

		// ---- per-application sessions -------------------------------------------------------------------

		private const string SessionReason = "macOS exposes no public per-application audio API. Core Audio models devices and streams, not application sessions, so per-application volume, mute, metering and session events have nothing to build on, and a process list would name applications this backend still could not control. Use the endpoint volume and mute on Audio.Device, or the application's own volume control.";

		/// <summary>
		/// Always false: this is the honest "none" cell the design's support matrix records for macOS, and the
		/// members below exist only so the probing cast succeeds and the caller reports that reason instead of
		/// meeting a missing interface.
		/// </summary>
		public AudioSessionDescriptor[] EnumerateSessions(string deviceId) => [];

		public bool TryRefreshSession(string sessionId, out AudioSessionDescriptor descriptor)
		{
			descriptor = default;
			return false;
		}

		public bool TryGetSessionVolume(string sessionId, out double linearVolume)
		{
			linearVolume = 0;
			return false;
		}

		public bool TrySetSessionVolume(string sessionId, double linearVolume) => false;

		public bool TryGetSessionMute(string sessionId, out bool mute)
		{
			mute = false;
			return false;
		}

		public bool TrySetSessionMute(string sessionId, bool mute) => false;

		public object GetNativeSessionObject(string sessionId) => null;

		// ---- metering ------------------------------------------------------------------------------------

		private const string MeterReason = "macOS exposes no public level meter for an audio endpoint. The Core Audio HAL publishes no peak property for a device, and the one public alternative, AudioQueue level metering, measures only the frames flowing through a queue Keysharp itself opened: an output device therefore cannot be metered that way at all, and metering an input device would open the microphone, prompt for access, and report Keysharp's own capture rather than the endpoint. Read Audio.Output.Peak, Audio.Playback.Peak or a recorder's Peak, which measure frames Keysharp already owns, or meter on Windows or Linux.";

		/// <summary>
		/// Always false. AudioQueue level metering is public and would compile, but it observes a queue this
		/// backend created, which is a different signal from the endpoint the caller named; binding it would
		/// report a confident number for the wrong thing, so the design's requirement for a verified
		/// observation point is left unmet rather than approximated.
		/// </summary>
		public bool TryOpenMeter(string targetId, bool isSession, double intervalMilliseconds, out IAudioNativeMeter meter, out string error)
		{
			meter = null;
			// A session target fails for the stronger reason: there is no session to meter in the first place.
			error = isSession ? SessionReason : MeterReason;
			return false;
		}

		// ---- file decoding -----------------------------------------------------------------------------

		// ExtendedAudioFile.h property selectors, each spelled out beside its FourCC. They are reached only
		// through the helpers below, so a wrong value shows up as a refused decode rather than a misread buffer.
		private const uint kExtAudioFileProperty_FileDataFormat   = 0x66666D74u; // 'ffmt'
		private const uint kExtAudioFileProperty_ClientDataFormat = 0x63666D74u; // 'cfmt'
		private const uint kExtAudioFileProperty_FileLengthFrames = 0x2366726Du; // '#frm'

		/// <summary>CFURLPathStyle.kCFURLPOSIXPathStyle.</summary>
		private const long kCFURLPOSIXPathStyle = 0;

		/// <summary>
		/// Frames pulled per ExtAudioFileRead: few enough calls for a long file, while never handing the
		/// converter a hundred-megabyte request in one go.
		/// </summary>
		private const int DecodeFramesPerRead = 8192;

		/// <summary>
		/// What a host with a working ExtAudioFile decodes beyond WAV. Ogg and Opus are absent because Core
		/// Audio ships no decoder for either; one of those files is refused with a message naming this list.
		/// </summary>
		private static readonly string[] decoderFormats = ["mp3", "m4a", "aac", "alac", "flac", "aiff", "caf"];

		/// <summary>
		/// CoreAudioTypes AudioBufferList carrying exactly one AudioBuffer, which is all an interleaved client
		/// format ever needs. The offsets are the same 64-bit layout <see cref="GetChannelCount"/> already walks
		/// by hand: a UInt32 buffer count, then 16-byte AudioBuffer entries starting at offset 8. It is declared
		/// here because no AudioBufferList type existed in this file to reuse.
		/// </summary>
		[StructLayout(LayoutKind.Explicit)]
		private struct AudioBufferListOne
		{
			[FieldOffset(0)] public uint mNumberBuffers;
			[FieldOffset(8)] public uint mNumberChannels;
			[FieldOffset(12)] public uint mDataByteSize;
			[FieldOffset(16)] public nint mData;
		}

		[DllImport(AudioToolbox)]
		private static extern int ExtAudioFileOpenURL(nint url, out nint file);

		[DllImport(AudioToolbox)]
		private static extern int ExtAudioFileGetProperty(nint file, uint propertyId, ref uint ioDataSize, nint outData);

		[DllImport(AudioToolbox)]
		private static extern int ExtAudioFileSetProperty(nint file, uint propertyId, uint dataSize, nint inData);

		[DllImport(AudioToolbox)]
		private static extern int ExtAudioFileRead(nint file, ref uint ioNumberFrames, ref AudioBufferListOne ioData);

		[DllImport(AudioToolbox)]
		private static extern int ExtAudioFileDispose(nint file);

		// CFURLPathStyle is a CFIndex, a signed 64-bit integer on every macOS target, and Boolean is an unsigned
		// char, so both cross as their own widths rather than as a marshalled enum or bool.
		[DllImport(CoreFoundation)]
		private static extern nint CFURLCreateWithFileSystemPath(nint allocator, nint filePath, long pathStyle, byte isDirectory);

		private static readonly object decoderProbeLock = new ();
		private static int decoderProbed;
		private static bool extAudioFileReady;
		private static string extAudioFileReason = "";

		/// <summary>
		/// Probes the file layer exactly once, apart from <see cref="EnsureProbed"/> so that decoding neither
		/// waits on nor depends on the HAL: a machine whose audio server is dead can still turn a file into a
		/// clip. Looking up exports opens no file and touches no device, which is what lets this be cached.
		/// </summary>
		private static void EnsureDecoderProbed()
		{
			if (Volatile.Read(ref decoderProbed) != 0)
				return;

			lock (decoderProbeLock)
			{
				if (decoderProbed != 0)
					return;

				try
				{
					// As elsewhere, the handles are deliberately not freed: they keep the frameworks loaded.
					if (!NativeLibrary.TryLoad(AudioToolbox, out var toolbox) || toolbox == nint.Zero)
					{
						extAudioFileReason = $"The AudioToolbox framework was not found at {AudioToolbox}, so only WAV files can be decoded. Run Keysharp on a standard macOS installation.";
					}
					else if (!NativeLibrary.TryGetExport(toolbox, "ExtAudioFileOpenURL", out _))
					{
						extAudioFileReason = $"The AudioToolbox framework at {AudioToolbox} exports no ExtAudioFileOpenURL, so only WAV files can be decoded. Run Keysharp on a supported macOS version.";
					}
					else if (!NativeLibrary.TryLoad(CoreFoundation, out var cf) || cf == nint.Zero || !NativeLibrary.TryGetExport(cf, "CFURLCreateWithFileSystemPath", out _))
					{
						extAudioFileReason = $"The CoreFoundation framework at {CoreFoundation} exports no CFURLCreateWithFileSystemPath, which is the only way a file path reaches Core Audio, so only WAV files can be decoded.";
					}
					else
					{
						extAudioFileReady = true;
					}
				}
				catch (Exception ex)
				{
					extAudioFileReady = false;
					extAudioFileReason = $"Core Audio file decoding could not be used: {ex.Message}. Keysharp needs the AudioToolbox framework at {AudioToolbox}.";
				}

				Volatile.Write(ref decoderProbed, 1);
			}
		}

		/// <summary>
		/// The containers this host decodes beyond WAV, or empty when the file layer did not initialize. It
		/// reports what actually resolved on this machine, not what Core Audio can do in principle.
		/// </summary>
		public string[] SupportedFormats
		{
			get
			{
				EnsureDecoderProbed();
				// A fresh copy: the cached array is shared state that a caller must not be able to edit.
				return extAudioFileReady ? (string[])decoderFormats.Clone() : [];
			}
		}

		/// <summary>
		/// Decodes a whole file through ExtAudioFile into interleaved float32 and closes it before returning, so
		/// nothing native outlives the call. The client format keeps the file's own sample rate, which means no
		/// rate conversion runs and the frame count read from the file describes the frames delivered exactly.
		/// </summary>
		/// <remarks>
		/// A source with more than two channels is refused by name rather than downmixed. ExtAudioFile would
		/// accept a two-channel client format and mix into it, but which matrix Core Audio picks for a 5.1 or
		/// ambisonic source cannot be verified from this host, and a silently wrong mix is worse than a refusal
		/// the user can read.
		/// </remarks>
		public bool TryDecodeFile(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;
			error = "";
			EnsureDecoderProbed();

			if (!extAudioFileReady)
			{
				error = extAudioFileReason.Length != 0 ? extAudioFileReason : "Core Audio file decoding is unavailable on this system.";
				return false;
			}

			if (string.IsNullOrEmpty(path))
			{
				error = "No audio file path was supplied.";
				return false;
			}

			// Asking first turns the most common failure into a sentence instead of an OSStatus.
			try
			{
				if (!File.Exists(path))
				{
					error = $"The audio file '{path}' does not exist.";
					return false;
				}
			}
			catch (Exception ex)
			{
				error = $"The audio file '{path}' could not be examined: {ex.Message}";
				return false;
			}

			var chars = nint.Zero;
			var cfPath = nint.Zero;
			var url = nint.Zero;
			var file = nint.Zero;

			try
			{
				chars = Marshal.StringToHGlobalUni(path);
				cfPath = CFStringCreateWithCharacters(nint.Zero, chars, path.Length);

				if (cfPath == nint.Zero)
				{
					error = $"The audio file path '{path}' could not be converted for Core Audio.";
					return false;
				}

				url = CFURLCreateWithFileSystemPath(nint.Zero, cfPath, kCFURLPOSIXPathStyle, 0);

				if (url == nint.Zero)
				{
					error = $"The audio file path '{path}' could not be turned into a file URL.";
					return false;
				}

				var status = ExtAudioFileOpenURL(url, out file);

				if (status != 0 || file == nint.Zero)
				{
					error = DescribeOpenFailure(path, status);
					return false;
				}

				if (!TryGetFileFormat(file, out var fileFormat, out status))
				{
					error = $"Core Audio could not report the format of '{path}' (status 0x{(uint)status:X8}).";
					return false;
				}

				var fileChannels = fileFormat.mChannelsPerFrame;

				if (fileChannels == 0)
				{
					error = $"'{path}' declares no audio channels.";
					return false;
				}

				if (!AudioFormats.IsValidChannels(fileChannels))
				{
					error = $"'{path}' carries {fileChannels} channels; only mono and stereo are supported. Convert it to stereo first.";
					return false;
				}

				var rate = (long)Math.Round(fileFormat.mSampleRate);

				if (!AudioFormats.IsValidSampleRate(rate))
				{
					error = $"'{path}' is at {rate} Hz, outside {AudioFormats.MinSampleRate} through {AudioFormats.MaxSampleRate} Hz.";
					return false;
				}

				if (!TryGetFileLengthFrames(file, out var frames, out status))
				{
					error = $"Core Audio could not report the length of '{path}' (status 0x{(uint)status:X8}).";
					return false;
				}

				// A declared length of zero is treated as an empty file rather than as "length unknown": every
				// container claimed here carries a real frame count, and guessing a growth loop instead would
				// read an unbounded amount from a file that says it holds nothing.
				if (frames <= 0)
				{
					error = $"'{path}' declares no audio frames.";
					return false;
				}

				// The limit is compared as a frame count rather than a byte count, so a nonsense length from a
				// damaged file is refused here instead of overflowing the multiplication that would check it.
				if (frames > AudioFormats.MaxClipBytes / (4 * fileChannels))
				{
					error = $"The decoded audio would exceed the {AudioFormats.MaxClipBytes / (1024 * 1024)} MiB per-clip limit.";
					return false;
				}

				var totalSamples = (int)(frames * fileChannels);
				var clientChannels = (int)fileChannels;
				status = SetClientFormat(file, (int)rate, clientChannels);

				if (status != 0)
				{
					error = $"Core Audio refused a float32 client format for '{path}' (status 0x{(uint)status:X8}).";
					return false;
				}

				var buffer = new float[totalSamples];

				if (!TryReadAll(file, buffer, clientChannels, out var written, out var readError))
				{
					error = $"Reading '{path}' failed: {readError}";
					return false;
				}

				if (written == 0)
				{
					error = $"'{path}' declares {frames} frames but decoded to none.";
					return false;
				}

				// A file that ends early is normal for a container whose frame count is an upper bound, so the
				// short result is kept and trimmed; the copy only happens in that case.
				samples = written == buffer.Length ? buffer : buffer.AsSpan(0, written).ToArray();
				sampleRate = (int)rate;
				channels = clientChannels;
				return true;
			}
			catch (Exception ex)
			{
				samples = null;
				error = $"Decoding '{path}' failed: {ex.Message}";
				return false;
			}
			finally
			{
				// Every native handle is released on both paths, in the reverse of the order it was taken.
				if (file != nint.Zero)
					_ = ExtAudioFileDispose(file);

				if (url != nint.Zero)
					CFRelease(url);

				if (cfPath != nint.Zero)
					CFRelease(cfPath);

				if (chars != nint.Zero)
					Marshal.FreeHGlobal(chars);
			}
		}

		/// <summary>
		/// Names the likely reason an open failed. An extension this host does not claim is the common case and
		/// deserves the list, rather than an OSStatus the user cannot act on.
		/// </summary>
		private static string DescribeOpenFailure(string path, int status)
		{
			var extension = Path.GetExtension(path);
			extension = extension.Length > 1 ? extension.Substring(1).ToLowerInvariant() : "";

			if (extension.Length != 0 && extension != "wav" && Array.IndexOf(decoderFormats, extension) < 0)
				return $"Core Audio does not decode '{extension}' files. This host decodes wav plus {string.Join(", ", decoderFormats)}.";

			return $"Core Audio could not open '{path}' (status 0x{(uint)status:X8}). The file may be truncated, DRM-protected, or in a variant this system's codecs do not handle.";
		}

		private static bool TryGetFileFormat(nint file, out AudioStreamBasicDescription format, out int status)
		{
			format = default;
			var size = (uint)Marshal.SizeOf<AudioStreamBasicDescription>();
			var ptr = Marshal.AllocHGlobal((int)size);

			try
			{
				status = ExtAudioFileGetProperty(file, kExtAudioFileProperty_FileDataFormat, ref size, ptr);

				if (status != 0)
					return false;

				format = Marshal.PtrToStructure<AudioStreamBasicDescription>(ptr);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		/// <summary>Frames in the file's own format, which is also the client format here, so they are the same frames.</summary>
		private static bool TryGetFileLengthFrames(nint file, out long frames, out int status)
		{
			frames = 0;
			var size = (uint)sizeof(long);
			var ptr = Marshal.AllocHGlobal((int)size);

			try
			{
				status = ExtAudioFileGetProperty(file, kExtAudioFileProperty_FileLengthFrames, ref size, ptr);

				if (status != 0)
					return false;

				frames = Marshal.ReadInt64(ptr);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		/// <summary>
		/// Asks for interleaved packed float32 at the file's own rate. Leaving kAudioFormatFlagIsNonInterleaved
		/// unset is what makes it interleaved, matching the playback and capture descriptions above.
		/// </summary>
		private static int SetClientFormat(nint file, int sampleRate, int channels)
		{
			var bytesPerFrame = (uint)(channels * sizeof(float));
			var format = new AudioStreamBasicDescription
			{
				mSampleRate = sampleRate,
				mFormatID = kAudioFormatLinearPCM,
				mFormatFlags = kLinearPCMFormatFlagIsFloat | kLinearPCMFormatFlagIsPacked,
				mBytesPerPacket = bytesPerFrame,
				mFramesPerPacket = 1,
				mBytesPerFrame = bytesPerFrame,
				mChannelsPerFrame = (uint)channels,
				mBitsPerChannel = 32,
				mReserved = 0,
			};
			var size = Marshal.SizeOf<AudioStreamBasicDescription>();
			var ptr = Marshal.AllocHGlobal(size);

			try
			{
				Marshal.StructureToPtr(format, ptr, false);
				return ExtAudioFileSetProperty(file, kExtAudioFileProperty_ClientDataFormat, (uint)size, ptr);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		/// <summary>
		/// Pulls frames straight into the managed result, pinned for the whole loop so the converter writes
		/// where the caller will read. A read of zero frames is end of file, which is the only way out other
		/// than filling the buffer; a file longer than it declared is stopped at that capacity.
		/// </summary>
		private static unsafe bool TryReadAll(nint file, float[] destination, int channels, out int samplesWritten, out string error)
		{
			samplesWritten = 0;
			error = "";
			var totalFrames = destination.Length / channels;
			var framesRead = 0;

			fixed (float* origin = destination)
			{
				while (framesRead < totalFrames)
				{
					var want = Math.Min(DecodeFramesPerRead, totalFrames - framesRead);
					var list = new AudioBufferListOne
					{
						mNumberBuffers = 1,
						mNumberChannels = (uint)channels,
						mDataByteSize = (uint)(want * channels * sizeof(float)),
						mData = (nint)(origin + ((long)framesRead * channels)),
					};
					var frames = (uint)want;
					var status = ExtAudioFileRead(file, ref frames, ref list);

					if (status != 0)
					{
						error = $"Core Audio stopped after {framesRead} frames (status 0x{(uint)status:X8}).";
						return false;
					}

					if (frames == 0)
						break;   // end of file

					// The converter must never report more than it was given room for; treating that as a
					// success would mean trusting a count that says memory outside the buffer was written.
					if (frames > (uint)want)
					{
						error = $"Core Audio reported {frames} frames for a {want} frame request, which would mean it wrote past the buffer.";
						return false;
					}

					// Lossy decoders legitimately overshoot full scale on an intersample peak, and the managed
					// bus admits only finite samples within [-1, 1]. The frames just written are clamped here,
					// while they are still cache-warm, rather than a whole file being refused over one peak.
					var written = destination.AsSpan(framesRead * channels, (int)frames * channels);

					for (var i = 0; i < written.Length; i++)
					{
						var v = written[i];
						written[i] = !float.IsFinite(v) ? 0f : v > 1f ? 1f : v < -1f ? -1f : v;
					}

					framesRead += (int)frames;
				}
			}

			samplesWritten = framesRead * channels;
			return true;
		}

		// ---- HAL property helpers, mirroring Sound.cs -------------------------------------------------------


		private static bool TryGetSampleRate(uint deviceId, out int rate)
		{
			rate = 0;
			var addr = Address(kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal);
			var ptr = Marshal.AllocHGlobal(sizeof(double));

			try
			{
				uint size = sizeof(double);

				if (AudioObjectGetPropertyData(deviceId, ref addr, 0, nint.Zero, ref size, ptr) != 0)
					return false;

				var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(ptr));

				if (!AudioFormats.IsValidSampleRate((long)Math.Round(value)))
					return false;

				rate = (int)Math.Round(value);
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		/// <summary>The HAL scope a direction lives on. A duplex device exposes both.</summary>
		private static uint ScopeOf(AudioDeviceKind kind)
			=> kind == AudioDeviceKind.Output ? kAudioObjectPropertyScopeOutput : kAudioObjectPropertyScopeInput;
	}
}
#endif
