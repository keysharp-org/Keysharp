#if WINDOWS
namespace Keysharp.Internals.Audio
{
	/// <summary>
	/// The per-stream WASAPI client. It lives here rather than in the shared vendored set because that set's
	/// commented-out copy names types this project does not have. Every method is [PreserveSig] int, following
	/// the rule stated at the top of ComInterfaces.cs, so a failure is a status the render loop can classify
	/// instead of an exception thrown out of a timing-critical region.
	/// </summary>
	[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioClient
	{
		[PreserveSig]
		int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity,
					   nint pFormat, nint audioSessionGuid);

		[PreserveSig] int GetBufferSize(out int numBufferFrames);
		[PreserveSig] int GetStreamLatency(out long hnsLatency);
		[PreserveSig] int GetCurrentPadding(out int numPaddingFrames);
		[PreserveSig] int IsFormatSupported(int shareMode, nint pFormat, out nint closestMatch);
		[PreserveSig] int GetMixFormat(out nint deviceFormatPointer);
		[PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
		[PreserveSig] int Start();
		[PreserveSig] int Stop();
		[PreserveSig] int Reset();
		[PreserveSig] int SetEventHandle(nint eventHandle);

		[PreserveSig]
		int GetService([In, MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
					   [MarshalAs(UnmanagedType.IUnknown)] out object service);
	}

	/// <summary>The render half of an initialized <see cref="IAudioClient"/>: borrow frames, write, give them back.</summary>
	[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioRenderClient
	{
		[PreserveSig] int GetBuffer(int numFramesRequested, out nint dataPointer);
		[PreserveSig] int ReleaseBuffer(int numFramesWritten, int bufferFlags);
	}

	/// <summary>
	/// The v2 session manager, which is the only way to enumerate an endpoint's existing sessions. Its two
	/// notification parameters stay raw pointers: declaring those interfaces here would claim a surface this file
	/// does not implement, while a pointer still occupies the slot.
	/// </summary>
	[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionManager2
	{
		// IAudioSessionManager's two methods are redeclared rather than inherited. .NET COM interop numbers a
		// vtable slot by a method's position among the methods THIS interface declares, so an inherited ComImport
		// base contributes none: inheriting would put GetSessionEnumerator at slot 3 and call GetAudioSessionControl
		// through it, which is an access violation.
		[PreserveSig]
		int GetAudioSessionControl(
			[In, Optional][MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
			[In][MarshalAs(UnmanagedType.U4)] uint streamFlags,
			[Out][MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl sessionControl);

		[PreserveSig]
		int GetSimpleAudioVolume(
			[In, Optional][MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
			[In][MarshalAs(UnmanagedType.U4)] uint streamFlags,
			[Out][MarshalAs(UnmanagedType.Interface)] out ISimpleAudioVolume audioVolume);

		[PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
		[PreserveSig] int RegisterSessionNotification(nint sessionNotification);
		[PreserveSig] int UnregisterSessionNotification(nint sessionNotification);

		[PreserveSig]
		int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint duckNotification);

		[PreserveSig] int UnregisterDuckNotification(nint duckNotification);
	}

	/// <summary>A snapshot list of the sessions on one endpoint, taken at the moment the enumerator was created.</summary>
	[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionEnumerator
	{
		[PreserveSig] int GetCount(out int sessionCount);
		[PreserveSig] int GetSession(int sessionIndex, out IAudioSessionControl session);
	}

	/// <summary>
	/// The v2 session control. GetSessionInstanceIdentifier is what makes a session id stable:
	/// it names one instance, so a reused process id or list position can never resolve to an earlier session.
	/// </summary>
	[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionControl2
	{
		// IAudioSessionControl's nine methods are redeclared for the reason given on IAudioSessionManager2 above,
		// so that GetSessionIdentifier lands on slot 12 rather than slot 3.
		[PreserveSig] int GetState([Out] out AudioSessionState state);

		[PreserveSig] int GetDisplayName([Out][MarshalAs(UnmanagedType.LPWStr)] out string displayName);

		[PreserveSig]
		int SetDisplayName([In][MarshalAs(UnmanagedType.LPWStr)] string displayName,
						   [In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		[PreserveSig] int GetIconPath([Out][MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

		[PreserveSig]
		int SetIconPath([In][MarshalAs(UnmanagedType.LPWStr)] string iconPath,
						[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		[PreserveSig] int GetGroupingParam([Out] out Guid groupingId);

		[PreserveSig]
		int SetGroupingParam([In][MarshalAs(UnmanagedType.LPStruct)] Guid groupingId,
							 [In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		[PreserveSig] int RegisterAudioSessionNotification([In] IAudioSessionEvents client);

		[PreserveSig] int UnregisterAudioSessionNotification([In] IAudioSessionEvents client);

		[PreserveSig] int GetSessionIdentifier([Out][MarshalAs(UnmanagedType.LPWStr)] out string sessionId);

		[PreserveSig] int GetSessionInstanceIdentifier([Out][MarshalAs(UnmanagedType.LPWStr)] out string instanceId);

		[PreserveSig] int GetProcessId(out uint processId);

		/// <summary>S_OK when this is the system sounds session, S_FALSE when it is not; both are successes.</summary>
		[PreserveSig] int IsSystemSoundsSession();

		[PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
	}

	/// <summary>The capture half of an initialized <see cref="IAudioClient"/>: borrow a packet, read it, give it back.</summary>
	[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioCaptureClient
	{
		[PreserveSig]
		int GetBuffer(out nint dataPointer, out int numFramesToRead, out uint bufferFlags,
					  out long devicePosition, out long qpcPosition);

		[PreserveSig] int ReleaseBuffer(int numFramesRead);

		[PreserveSig] int GetNextPacketSize(out int numFramesInNextPacket);
	}

	/// <summary>The WAVEFORMATEX header, packed to its true 18 bytes rather than the 20 natural alignment would give.</summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct WaveFormatEx
	{
		internal ushort wFormatTag;
		internal ushort nChannels;
		internal uint nSamplesPerSec;
		internal uint nAvgBytesPerSec;
		internal ushort nBlockAlign;
		internal ushort wBitsPerSample;
		internal ushort cbSize;
	}

	/// <summary>WAVEFORMATEXTENSIBLE: the header plus the 22 extra bytes its cbSize announces.</summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct WaveFormatExtensible
	{
		internal WaveFormatEx Format;
		internal ushort wValidBitsPerSample;
		internal uint dwChannelMask;
		internal Guid SubFormat;
	}

	/// <summary>
	/// The kernel and COM entry points the render worker needs. Handle waiting and apartment initialization have
	/// no managed equivalent that keeps the audio-ready and cancellation handles in a single wait.
	/// </summary>
	internal static partial class WasapiNative
	{
		internal const uint CoinitMultithreaded = 0x0;
		internal const uint WaitObject0 = 0;
		internal const uint WaitFailed = 0xFFFFFFFF;

		[LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
		internal static partial nint CreateEvent(nint eventAttributes,
												 [MarshalAs(UnmanagedType.Bool)] bool manualReset,
												 [MarshalAs(UnmanagedType.Bool)] bool initialState,
												 nint name);

		[LibraryImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool SetEvent(nint handle);

		[LibraryImport("kernel32.dll", SetLastError = true)]
		internal static partial uint WaitForMultipleObjects(uint count,
															nint[] handles,
															[MarshalAs(UnmanagedType.Bool)] bool waitAll,
															uint milliseconds);

		[LibraryImport("ole32.dll")]
		internal static partial int CoInitializeEx(nint reserved, uint coInit);

		[LibraryImport("ole32.dll")]
		internal static partial void CoUninitialize();

		/// <summary>
		/// Multimedia Class Scheduler registration. Without it a render thread competes as an ordinary thread and
		/// drops out under load; a host without avrt.dll simply keeps default scheduling.
		/// </summary>
		[LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		internal static partial nint AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

		[LibraryImport("avrt.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static partial bool AvRevertMmThreadCharacteristics(nint handle);
	}

	/// <summary>
	/// One Media Foundation attribute store, declared only as far as the setters and getters this file calls.
	/// Every method is [PreserveSig] int and every inherited slot is redeclared, for the reason stated on
	/// IAudioSessionManager2 above: .NET COM interop numbers a vtable slot by a method's position among the
	/// methods THIS interface declares, so an inherited ComImport base contributes no slots at all. Methods that
	/// are never called keep pointer-shaped parameters, which is all a slot needs in order to exist.
	/// </summary>
	[Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMFMediaType
	{
		[PreserveSig] int GetItem(nint key, nint value);
		[PreserveSig] int GetItemType(nint key, out int attributeType);
		[PreserveSig] int CompareItem(nint key, nint value, out int result);
		[PreserveSig] int Compare(nint theirs, int matchType, out int result);
		[PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out uint value);
		[PreserveSig] int GetUINT64(nint key, out ulong value);
		[PreserveSig] int GetDouble(nint key, out double value);
		[PreserveSig] int GetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
		[PreserveSig] int GetStringLength(nint key, out uint length);
		[PreserveSig] int GetString(nint key, nint buffer, uint bufferSize, nint length);
		[PreserveSig] int GetAllocatedString(nint key, out nint value, out uint length);
		[PreserveSig] int GetBlobSize(nint key, out uint size);
		[PreserveSig] int GetBlob(nint key, nint buffer, uint bufferSize, nint size);
		[PreserveSig] int GetAllocatedBlob(nint key, out nint buffer, out uint size);
		[PreserveSig] int GetUnknown(nint key, nint interfaceId, out nint value);
		[PreserveSig] int SetItem(nint key, nint value);
		[PreserveSig] int DeleteItem(nint key);
		[PreserveSig] int DeleteAllItems();
		[PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, uint value);
		[PreserveSig] int SetUINT64(nint key, ulong value);
		[PreserveSig] int SetDouble(nint key, double value);

		[PreserveSig]
		int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
	}

	/// <summary>
	/// One decoded sample. ConvertToContiguousBuffer is the only method called, and it sits behind the thirty
	/// IMFAttributes slots and eight IMFSample slots redeclared here so that it lands where the native vtable
	/// actually puts it. A wrong slot here is an access violation, not an exception.
	/// </summary>
	[Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMFSample
	{
		[PreserveSig] int GetItem(nint key, nint value);
		[PreserveSig] int GetItemType(nint key, out int attributeType);
		[PreserveSig] int CompareItem(nint key, nint value, out int result);
		[PreserveSig] int Compare(nint theirs, int matchType, out int result);
		[PreserveSig] int GetUINT32(nint key, out uint value);
		[PreserveSig] int GetUINT64(nint key, out ulong value);
		[PreserveSig] int GetDouble(nint key, out double value);
		[PreserveSig] int GetGUID(nint key, out Guid value);
		[PreserveSig] int GetStringLength(nint key, out uint length);
		[PreserveSig] int GetString(nint key, nint buffer, uint bufferSize, nint length);
		[PreserveSig] int GetAllocatedString(nint key, out nint value, out uint length);
		[PreserveSig] int GetBlobSize(nint key, out uint size);
		[PreserveSig] int GetBlob(nint key, nint buffer, uint bufferSize, nint size);
		[PreserveSig] int GetAllocatedBlob(nint key, out nint buffer, out uint size);
		[PreserveSig] int GetUnknown(nint key, nint interfaceId, out nint value);
		[PreserveSig] int SetItem(nint key, nint value);
		[PreserveSig] int DeleteItem(nint key);
		[PreserveSig] int DeleteAllItems();
		[PreserveSig] int SetUINT32(nint key, uint value);
		[PreserveSig] int SetUINT64(nint key, ulong value);
		[PreserveSig] int SetDouble(nint key, double value);
		[PreserveSig] int SetGUID(nint key, nint value);
		[PreserveSig] int SetString(nint key, nint value);
		[PreserveSig] int SetBlob(nint key, nint buffer, uint bufferSize);
		[PreserveSig] int SetUnknown(nint key, nint value);
		[PreserveSig] int LockStore();
		[PreserveSig] int UnlockStore();
		[PreserveSig] int GetCount(out uint count);
		[PreserveSig] int GetItemByIndex(uint index, out Guid key, nint value);
		[PreserveSig] int CopyAllItems(nint destination);
		[PreserveSig] int GetSampleFlags(out uint flags);
		[PreserveSig] int SetSampleFlags(uint flags);
		[PreserveSig] int GetSampleTime(out long time);
		[PreserveSig] int SetSampleTime(long time);
		[PreserveSig] int GetSampleDuration(out long duration);
		[PreserveSig] int SetSampleDuration(long duration);
		[PreserveSig] int GetBufferCount(out uint count);

		[PreserveSig]
		int GetBufferByIndex(uint index, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer buffer);

		[PreserveSig]
		int ConvertToContiguousBuffer([MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer buffer);
	}

	/// <summary>One flattened block of decoded bytes. Lock hands out the pointer; Unlock must always follow it.</summary>
	[Guid("045FA593-8799-42B8-BC8D-8968C6453507"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMFMediaBuffer
	{
		[PreserveSig] int Lock(out nint buffer, out uint maxLength, out uint currentLength);
		[PreserveSig] int Unlock();
		[PreserveSig] int GetCurrentLength(out uint length);
		[PreserveSig] int SetCurrentLength(uint length);
		[PreserveSig] int GetMaxLength(out uint length);
	}

	/// <summary>
	/// The synchronous source reader: it resolves a container to a decoder, negotiates an output format and
	/// hands back one sample at a time. Declared standalone in native vtable order for the same reason as the
	/// interfaces above.
	/// </summary>
	[Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMFSourceReader
	{
		[PreserveSig] int GetStreamSelection(uint streamIndex, out int selected);

		[PreserveSig]
		int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

		[PreserveSig]
		int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex,
							   [MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);

		[PreserveSig]
		int GetCurrentMediaType(uint streamIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);

		[PreserveSig]
		int SetCurrentMediaType(uint streamIndex, nint reserved, [MarshalAs(UnmanagedType.Interface)] IMFMediaType mediaType);

		[PreserveSig]
		int SetCurrentPosition([In, MarshalAs(UnmanagedType.LPStruct)] Guid timeFormat, nint position);

		[PreserveSig]
		int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags,
					   out long timestamp, [MarshalAs(UnmanagedType.Interface)] out IMFSample sample);

		[PreserveSig] int Flush(uint streamIndex);
		[PreserveSig] int GetServiceForStream(uint streamIndex, nint service, nint interfaceId, out nint value);
		[PreserveSig] int GetPresentationAttribute(uint streamIndex, nint attribute, nint value);
	}

	/// <summary>
	/// The Media Foundation entry points the decoder needs. They are plain [DllImport] rather than
	/// [LibraryImport] because the source generator does not marshal COM interfaces, and they are called only
	/// after the one-shot probe has loaded both modules: a Windows N edition ships with neither.
	/// </summary>
	internal static class MediaFoundationNative
	{
		[DllImport("mfplat.dll", ExactSpelling = true)]
		internal static extern int MFStartup(uint version, uint flags);

		[DllImport("mfplat.dll", ExactSpelling = true)]
		internal static extern int MFCreateMediaType([MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);

		[DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, nint attributes,
															   [MarshalAs(UnmanagedType.Interface)] out IMFSourceReader reader);
	}

	/// <summary>
	/// The Windows audio backend: MMDevice for discovery and endpoint volume, IAudioSessionManager2 for the
	/// one-boolean running scan, shared-mode event-driven WASAPI for playback, and Media Foundation for the
	/// containers the managed WAV reader does not handle. Nothing here throws at type load or construction; the
	/// MMDevice service and Media Foundation are each probed once on first use and every refusal carries the
	/// action the user has to take.
	/// </summary>
	internal sealed class WasapiAudioBackend : IAudioBackend
	{
		private const string ServiceReason =
			"Windows audio endpoints are unavailable. Start the \"Windows Audio\" (Audiosrv) service, then retry.";
		private const string DisposedReason = "The audio backend has been shut down.";
		private const string LoopbackVersionReason =
			"Capturing system output needs Windows 10 version 1703 or later. Record from \"Microphone\" instead, or update Windows.";
		private const string CapturePrivacyReason =
			"Windows denied audio capture. Turn on Settings > Privacy & security > Microphone and \"Let desktop apps access your microphone\", then retry; capturing system output is gated by the same setting.";

		private const int ShareModeShared = 0;
		private const int OpenTimeoutMs = 5000;
		private const int JoinTimeoutMs = 2000;
		private const uint WaitPeriodMs = 2000;
		private const uint StreamFlagsEventCallback = 0x00040000;
		private const uint StreamFlagsLoopback = 0x00020000;
		private const uint StreamFlagsAutoConvertPcm = 0x80000000;
		private const uint StreamFlagsSrcDefaultQuality = 0x08000000;
		private const int BufferFlagsSilent = 2;
		private const int AudclntSBufferEmpty = 0x08890001;
		private const int AudclntSNoSingleProcess = 0x0889000D;
		private const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);
		private const int AudclntEUnsupportedFormat = unchecked((int)0x88890008);
		private const int AudclntEServiceNotRunning = unchecked((int)0x88890010);
		private const int EAccessDenied = unchecked((int)0x80070005);
		private const int ENoInterface = unchecked((int)0x80004002);
		private const ushort WaveFormatPcmTag = 1;
		private const ushort WaveFormatIeeeFloatTag = 3;
		private const ushort WaveFormatExtensibleTag = 0xFFFE;
		private const ushort ExtensibleExtraBytes = 22;

		private static readonly Guid IID_IAudioSessionManager2 = new ("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
		private static readonly Guid IID_IAudioCaptureClient = new ("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
		private static readonly Guid SubtypePcm = new ("00000001-0000-0010-8000-00AA00389B71");
		private static readonly Guid SubtypeIeeeFloat = new ("00000003-0000-0010-8000-00AA00389B71");

		/// <summary>Both endpoint directions, in the order a blank selector visits them.</summary>
		private static readonly AudioDeviceKind[] BothKinds = [AudioDeviceKind.Output, AudioDeviceKind.Input];

		/// <summary>
		/// Event-driven loopback capture without a companion render stream is reliable from Windows 10 version
		/// 1703, so an older host reports the SystemAudioCapture capability unsupported rather than opening a stream that
		/// would deliver packets only while something else happens to be playing.
		/// </summary>
		private static readonly bool loopbackCapable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063);

		private readonly object probeLock = new ();
		private readonly ConcurrentDictionary<IAudioDeviceWatcher, byte> watchers = new ();

		/// <summary>
		/// Every stream this backend opened and has not been told about since. The service closes outputs it owns,
		/// but a backend torn down first would otherwise leave a live render or capture thread on the device.
		/// </summary>
		private readonly ConcurrentDictionary<IDisposable, byte> streams = new ();

		private int probeState;   // 0 = not probed, 1 = available, 2 = unavailable
		private string probeReason = "";
		private int disposed;

		public bool IsAvailable => EnsureProbed(out _);

		public bool Supports(AudioCapability capability)
		{
			if (!EnsureProbed(out _))
				return false;

			//Every admitted capability rests on the same MMDevice service, so one successful probe answers all of
			//them. Whether one particular endpoint exposes volume is a per-device fact a Try* call reports.
			//Every admitted capability rests on the same MMDevice service, so one successful probe answers all of
			//them; the two that do not are the OS-version-gated loopback and decoding, which has its own probe.
			return capability switch
			{
				AudioCapability.SystemAudioCapture => loopbackCapable,
				AudioCapability.Decoding => EnsureMediaFoundation(out _),
				_ => true,
			};
		}

		public string UnsupportedReason(AudioCapability capability)
		{
			if (!EnsureProbed(out var reason))
				return reason;

			return capability switch
			{
				AudioCapability.SystemAudioCapture when !loopbackCapable => LoopbackVersionReason,
				AudioCapability.Decoding when !EnsureMediaFoundation(out var mfReason) => mfReason,
				_ => "",
			};
		}

		public AudioDeviceDescriptor[] EnumerateDevices(AudioDeviceKind kind) => EnumerateDevicesCore(FlowOf(kind));

		public AudioDeviceDescriptor[] EnumerateAllDevices() => EnumerateDevicesCore(DataFlow.All, DeviceState.Active | DeviceState.Unplugged);

		private AudioDeviceDescriptor[] EnumerateDevicesCore(DataFlow flow, DeviceState states = DeviceState.Active)
		{
			if (!EnsureProbed(out _))
				return [];

			MMDeviceEnumerator enumerator = null;

			try
			{
				enumerator = new MMDeviceEnumerator();
				var outputDefault = DefaultIdOrEmpty(enumerator, DataFlow.Render);
				var inputDefault = DefaultIdOrEmpty(enumerator, DataFlow.Capture);
				var collection = enumerator.EnumerateAudioEndPoints(flow, states);
				var count = collection.Count;
				var result = new List<AudioDeviceDescriptor>(count);

				for (var i = 0; i < count; i++)
				{
					MMDevice device = null;

					try
					{
						device = collection[i];
						var kind = KindOf(device.DataFlow);
						var id = device.ID;
						result.Add(new AudioDeviceDescriptor(id, NameOf(device), kind, id == (kind == AudioDeviceKind.Output ? outputDefault : inputDefault)));
					}
					catch (Exception)
					{
						//An endpoint that fails mid-enumeration is one that just went away, which is smaller than
						//the whole list failing.
					}
					finally
					{
						device?.Dispose();
					}
				}

				return [.. result];
			}
			catch (Exception)
			{
				return [];
			}
			finally
			{
				enumerator?.Dispose();
			}
		}

		public bool TryGetDefaultDevice(AudioDeviceKind kind, out AudioDeviceDescriptor device)
		{
			device = default;

			if (!EnsureProbed(out _))
				return false;

			MMDeviceEnumerator enumerator = null;
			MMDevice mmDevice = null;

			try
			{
				enumerator = new MMDeviceEnumerator();
				var flow = FlowOf(kind);

				if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
					return false;

				mmDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
				device = new AudioDeviceDescriptor(mmDevice.ID, NameOf(mmDevice), kind, true);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				mmDevice?.Dispose();
				enumerator?.Dispose();
			}
		}

		public bool TryGetDevice(string id, out AudioDeviceDescriptor device)
		{
			device = default;

			if (string.IsNullOrEmpty(id) || !EnsureProbed(out _))
				return false;

			MMDeviceEnumerator enumerator = null;
			MMDevice mmDevice = null;

			try
			{
				enumerator = new MMDeviceEnumerator();
				mmDevice = enumerator.GetDevice(id);

				if (mmDevice == null || mmDevice.State != DeviceState.Active)
					return false;

				var flow = mmDevice.DataFlow;

				if (flow != DataFlow.Render && flow != DataFlow.Capture)
					return false;

				//The id is the durable selector, so only the spelling the endpoint itself reports resolves;
				//GetDevice is looser than that.
				var actual = mmDevice.ID;

				if (!string.Equals(actual, id, StringComparison.Ordinal))
					return false;

				var kind = KindOf(flow);
				device = new AudioDeviceDescriptor(actual, NameOf(mmDevice), kind, actual == DefaultIdOrEmpty(enumerator, flow));
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				mmDevice?.Dispose();
				enumerator?.Dispose();
			}
		}

		public bool TryGetVolume(AudioDeviceKind kind, string id, out double volume)
		{
			var read = 0.0;
			var ok = WithDevice(kind, id, d =>
			{
				var got = 0.0;
				var ok = WithEndpointVolume(d, e =>
				{
					if (e.GetMasterVolumeLevelScalar(out var level) < 0)
						return false;

					got = Math.Clamp((double)level, 0.0, 1.0);
					return true;
				});
				read = got;
				return ok;
			});
			volume = read;
			return ok;
		}

		public bool TrySetVolume(AudioDeviceKind kind, string id, double volume)
		{
			if (double.IsNaN(volume))
				return false;

			return WithDevice(kind, id, d =>
			{
				return WithEndpointVolume(d, e =>
				{
					var context = Guid.Empty;
					return e.SetMasterVolumeLevelScalar((float)Math.Clamp(volume, 0.0, 1.0), ref context) >= 0;
				});
			});
		}

		public bool TryGetMute(AudioDeviceKind kind, string id, out bool mute)
		{
			var read = false;
			var ok = WithDevice(kind, id, d =>
			{
				var got = false;
				var ok = WithEndpointVolume(d, e =>
				{
					if (e.GetMute(out var muted) < 0)
						return false;

					got = muted;
					return true;
				});
				read = got;
				return ok;
			});
			mute = read;
			return ok;
		}

		public bool TrySetMute(AudioDeviceKind kind, string id, bool mute)
			=> WithDevice(kind, id, d =>
			{
				return WithEndpointVolume(d, e =>
				{
					var context = Guid.Empty;
					return e.SetMute(mute, ref context) >= 0;
				});
			});

		public bool TryGetIsRunning(AudioDeviceKind kind, string id, out bool running)
		{
			var active = false;
			var determined = WithDevice(kind, id, d =>
			{
				IAudioSessionManager2 manager = null;
				IAudioSessionEnumerator sessions = null;

				try
				{
					var iid = IID_IAudioSessionManager2;

					if (d.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated) < 0
							|| activated is not IAudioSessionManager2 sessionManager)
						return false;

					manager = sessionManager;

					if (manager.GetSessionEnumerator(out sessions) < 0 || sessions == null
							|| sessions.GetCount(out var count) < 0)
						return false;

					for (var i = 0; i < count; i++)
					{
						IAudioSessionControl control = null;

						try
						{
							if (sessions.GetSession(i, out control) < 0 || control == null)
								continue;

							if (control.GetState(out var state) >= 0 && state == AudioSessionState.AudioSessionStateActive)
							{
								active = true;
								return true;
							}
						}
						finally
						{
							ReleaseCom(control);
						}
					}

					//Every session answered and none was active: a determined idle, not an unknown.
					return true;
				}
				finally
				{
					ReleaseCom(sessions);
					ReleaseCom(manager);
				}
			});
			running = active;
			return determined;
		}

		public object GetNativeDeviceObject(AudioDeviceKind kind, string id)
		{
			object native = null;
			//The IMMDevice runtime callable wrapper outlives this method: disposing the MMDevice wrapper only
			//releases the endpoint-volume object it may have cached, never the device pointer itself.
			_ = WithDevice(kind, id, d =>
			{
				native = d.deviceInterface;
				return true;
			});
			return native;
		}

		public IAudioDeviceWatcher WatchDevices(Action sink)
		{
			if (sink == null || !EnsureProbed(out _))
				return null;

			MMDeviceEnumerator enumerator = null;

			try
			{
				enumerator = new MMDeviceEnumerator();
				var client = new DeviceNotificationClient(sink);

				if (enumerator.RegisterEndpointNotificationCallback(client) < 0)
				{
					enumerator.Dispose();
					return null;
				}

				var watcher = new DeviceWatcher(this, enumerator, client);
				_ = watchers.TryAdd(watcher, 0);
				return watcher;
			}
			catch (Exception)
			{
				enumerator?.Dispose();
				return null;
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			foreach (var watcher in watchers.Keys)
			{
				try
				{
					watcher.Dispose();
				}
				catch (Exception)
				{
					//A registration whose enumerator already died cannot be unregistered, and teardown continues.
				}
			}

			watchers.Clear();

			foreach (var open in streams.Keys)
			{
				try
				{
					open.Dispose();
				}
				catch (Exception)
				{
					//A stream whose worker will not quiesce keeps its handles by design; teardown continues.
				}
			}

			streams.Clear();
			InvalidateEnumerator();
		}

		public bool TryOpenOutput(in AudioOutputRequest request, IAudioRenderSource source,
								  out IAudioOutputStream stream, out string error)
		{
			stream = null;

			if (!EnsureProbed(out error))
				return false;

			if (source == null)
			{
				error = "No render source was supplied for the output.";
				return false;
			}

			var candidate = new WasapiOutputStream(request.DeviceId ?? "", request.RequestedLatencyMilliseconds, source);

			if (!candidate.WaitForOpen(out error))
			{
				candidate.Dispose();
				return false;
			}

			_ = streams.TryAdd(candidate, 0);
			stream = candidate;
			return true;
		}

		public bool TryOpenInput(in AudioInputRequest request, IAudioCaptureSink sink, out IAudioInputStream stream, out string error)
		{
			stream = null;

			if (!EnsureProbed(out error))
				return false;

			if (sink == null)
			{
				error = "No capture sink was supplied for the input.";
				return false;
			}

			var want = request.Source == AudioCaptureSource.Microphone
					   ? AudioCapability.MicrophoneCapture
					   : AudioCapability.SystemAudioCapture;

			if (!Supports(want))
			{
				error = UnsupportedReason(want);
				return false;
			}

			var candidate = new WasapiInputStream(request.DeviceId ?? "", request.Source, request.SampleRate,
												  request.Channels, request.ChunkMilliseconds, sink);

			if (!candidate.WaitForOpen(out error))
			{
				candidate.Dispose();
				return false;
			}

			_ = streams.TryAdd(candidate, 0);
			stream = candidate;
			return true;
		}

		public AudioSessionDescriptor[] EnumerateSessions(string deviceId)
		{
			if (!EnsureProbed(out _))
				return [];

			MMDeviceEnumerator enumerator = null;

			try
			{
				enumerator = new MMDeviceEnumerator();
				var result = new List<AudioSessionDescriptor>();
				//One instance identifier can be reached through more than one endpoint view, and a caller that saw
				//the same application twice could not tell which entry its control calls would reach.
				var seen = new HashSet<string>(StringComparer.Ordinal);

				if (!string.IsNullOrEmpty(deviceId))
				{
					if (TryResolveExact(enumerator, deviceId, out var single, out var singleKind))
					{
						try
						{
							CollectSessions(single, singleKind, result, seen);
						}
						finally
						{
							single.Dispose();
						}
					}

					return [.. result];
				}

				foreach (var kind in BothKinds)
				{
					MMDeviceCollection collection;

					try
					{
						collection = enumerator.EnumerateAudioEndPoints(FlowOf(kind), DeviceState.Active);
					}
					catch (Exception)
					{
						continue;
					}

					var count = collection.Count;

					for (var i = 0; i < count; i++)
					{
						MMDevice device = null;

						try
						{
							device = collection[i];
							CollectSessions(device, kind, result, seen);
						}
						catch (Exception)
						{
							//An endpoint that went away mid-scan contributes no sessions; the rest of the scan stands.
						}
						finally
						{
							device?.Dispose();
						}
					}
				}

				return [.. result];
			}
			catch (Exception)
			{
				return [];
			}
			finally
			{
				enumerator?.Dispose();
			}
		}

		public bool TryRefreshSession(string sessionId, out AudioSessionDescriptor descriptor)
		{
			descriptor = default;

			if (!TryFindSession(sessionId, out var control, out var kind, out var endpointId))
				return false;

			try
			{
				return TryDescribe(control, endpointId, kind, out descriptor);
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				ReleaseCom(control);
			}
		}

		public bool TryGetSessionVolume(string sessionId, out double linearVolume)
		{
			linearVolume = 0;

			if (!TryFindSession(sessionId, out var control, out _, out _))
				return false;

			try
			{
				if (control is not ISimpleAudioVolume volume || volume.GetMasterVolume(out var level) < 0 || !float.IsFinite(level))
					return false;

				linearVolume = Math.Clamp((double)level, 0.0, 1.0);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				ReleaseCom(control);
			}
		}

		public bool TrySetSessionVolume(string sessionId, double linearVolume)
		{
			if (double.IsNaN(linearVolume) || !TryFindSession(sessionId, out var control, out _, out _))
				return false;

			try
			{
				var context = Guid.Empty;
				return control is ISimpleAudioVolume volume
					   && volume.SetMasterVolume((float)Math.Clamp(linearVolume, 0.0, 1.0), context) >= 0;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				ReleaseCom(control);
			}
		}

		public bool TryGetSessionMute(string sessionId, out bool mute)
		{
			mute = false;

			if (!TryFindSession(sessionId, out var control, out _, out _))
				return false;

			try
			{
				if (control is not ISimpleAudioVolume volume || volume.GetMute(out var muted) < 0)
					return false;

				mute = muted;
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				ReleaseCom(control);
			}
		}

		public bool TrySetSessionMute(string sessionId, bool mute)
		{
			if (!TryFindSession(sessionId, out var control, out _, out _))
				return false;

			try
			{
				var context = Guid.Empty;
				return control is ISimpleAudioVolume volume && volume.SetMute(mute, context) >= 0;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				ReleaseCom(control);
			}
		}

		public object GetNativeSessionObject(string sessionId)
		{
			//The runtime callable wrapper outlives this method on purpose: it is the script's escape hatch, and the
			//session control it wraps holds its own reference to the native session.
			return TryFindSession(sessionId, out var control, out _, out _) ? control : null;
		}

		//The WASAPI meter is read on demand through IAudioMeterInformation, so it publishes nothing and the
		//caller's cadence has nothing to apply to.
		public bool TryOpenMeter(string targetId, bool isSession, double intervalMilliseconds, out IAudioNativeMeter meter, out string error)
		{
			meter = null;

			if (!EnsureProbed(out error))
				return false;

			if (string.IsNullOrEmpty(targetId))
			{
				error = "No audio device or session was named for the meter.";
				return false;
			}

			return isSession
				   ? TryOpenSessionMeter(targetId, out meter, out error)
				   : TryOpenDeviceMeter(targetId, out meter, out error);
		}

		private const string MediaFoundationReason =
			"Media Foundation is not installed, so only WAV files can be decoded. Install the Media Feature Pack (Windows N editions ship without it), then retry.";

		private const uint MfVersion = 0x00020070;
		private const uint MfStartupNoSocket = 1;
		private const uint MfFirstAudioStream = 0xFFFFFFFD;
		private const uint MfAllStreams = 0xFFFFFFFE;
		private const uint MfReaderError = 0x1;
		private const uint MfReaderEndOfStream = 0x2;
		private const uint MfReaderCurrentMediaTypeChanged = 0x20;
		private const int MfBitsPerFloatSample = 32;
		private const int MfFloatSampleBytes = MfBitsPerFloatSample / 8;
		private const int MfENotFound = unchecked((int)0x80070002);
		private const int MfEPathNotFound = unchecked((int)0x80070003);
		private const int MfEUnsupportedByteStreamType = unchecked((int)0xC00D36FA);
		private const int MfEInvalidMediaType = unchecked((int)0xC00D36B4);

		private static readonly Guid MfMediaTypeAudio = new ("73647561-0000-0010-8000-00AA00389B71");
		private static readonly Guid MfMtMajorType = new ("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
		private static readonly Guid MfMtSubtype = new ("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
		private static readonly Guid MfMtAudioNumChannels = new ("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
		private static readonly Guid MfMtAudioSamplesPerSecond = new ("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
		private static readonly Guid MfMtAudioBlockAlignment = new ("322DE230-9EEB-43BD-AB7A-FF412251541D");
		private static readonly Guid MfMtAudioAvgBytesPerSecond = new ("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
		private static readonly Guid MfMtAudioBitsPerSample = new ("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");

		/// <summary>
		/// Media Foundation's FLAC decoder ships from Windows 10 version 1703, so an older host claims one format
		/// fewer rather than accepting a file it would then fail to open.
		/// </summary>
		private static readonly bool flacCapable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063);

		/// <summary>
		/// The containers a stock Windows 10 or 11 install decodes. Ogg and Opus are deliberately absent: they
		/// need the Store's Web Media Extensions, which this backend cannot detect.
		/// </summary>
		private static readonly string[] decodableFormats = ["mp3", "m4a", "aac", "wma", "flac"];

		private static readonly string[] decodableFormatsBeforeFlac = ["mp3", "m4a", "aac", "wma"];

		private static readonly object decoderProbeLock = new ();

		private static int decoderProbeState;   // 0 = not probed, 1 = available, 2 = unavailable
		private static string decoderProbeReason = "";

		public string[] SupportedFormats
		{
			get
			{
				if (Volatile.Read(ref disposed) != 0 || !EnsureMediaFoundation(out _))
					return [];

				//The cached array is copied out: a caller that sorted or cleared it in place would change what
				//every later caller sees.
				return (string[])(flacCapable ? decodableFormats : decodableFormatsBeforeFlac).Clone();
			}
		}

		public bool TryDecodeFile(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;

			if (Volatile.Read(ref disposed) != 0)
			{
				error = DisposedReason;
				return false;
			}

			if (string.IsNullOrEmpty(path))
			{
				error = "No file was named to decode.";
				return false;
			}

			if (!EnsureMediaFoundation(out error))
				return false;

			string full;

			try
			{
				full = Path.GetFullPath(path);
			}
			catch (Exception ex)
			{
				error = $"\"{path}\" is not a usable file path ({ex.Message}).";
				return false;
			}

			if (!File.Exists(full))
			{
				error = $"The file \"{full}\" does not exist.";
				return false;
			}

			var claimed = SupportedFormats;
			var extension = Path.GetExtension(full);
			extension = extension.Length > 1 ? extension[1..].ToLowerInvariant() : "";

			//The advertised list is also the gate, so a host that reports fewer formats refuses the files it
			//would otherwise carry all the way to a decoder it does not have.
			if (Array.IndexOf(claimed, extension) < 0)
			{
				error = $"\"{full}\" is not one of the containers this host decodes ({string.Join(", ", claimed)}).";
				return false;
			}

			float[] decoded = null;
			var rate = 0;
			var count = 0;
			var reason = "";
			var thrown = RunOnMtaWorker("Keysharp media decode", () =>
			{
				if (!TryDecodeOnWorker(full, out decoded, out rate, out count, out reason))
					decoded = null;
			});

			if (thrown != null)
			{
				error = $"Decoding \"{full}\" failed: {thrown.Message}";
				return false;
			}

			if (decoded == null)
			{
				error = string.IsNullOrEmpty(reason) ? $"\"{full}\" could not be decoded." : reason;
				return false;
			}

			samples = decoded;
			sampleRate = rate;
			channels = count;
			error = "";
			return true;
		}

		/// <summary>
		/// The whole decode, on the worker that owns every object it creates. A source with more than two
		/// channels is downmixed to stereo by Media Foundation's own audio resampler, which knows the standard
		/// speaker layouts; a source the resampler will not convert is refused by name rather than folded down
		/// here with a guessed matrix.
		/// </summary>
		private static bool TryDecodeOnWorker(string path, out float[] samples, out int sampleRate, out int channels, out string error)
		{
			samples = null;
			sampleRate = 0;
			channels = 0;
			error = "";
			IMFSourceReader reader = null;

			try
			{
				var hr = MediaFoundationNative.MFCreateSourceReaderFromURL(path, 0, out reader);

				if (hr < 0 || reader == null)
				{
					error = DescribeMediaFoundation($"Opening \"{path}\"", hr);
					return false;
				}

				//Every stream is deselected first so that a video track in the same container is never decoded.
				_ = reader.SetStreamSelection(MfAllStreams, false);

				if (reader.SetStreamSelection(MfFirstAudioStream, true) < 0)
				{
					error = $"\"{path}\" carries no audio stream.";
					return false;
				}

				hr = RequestFloatOutput(reader, 0, 0);

				if (hr < 0)
				{
					error = DescribeMediaFoundation("Selecting an uncompressed audio format", hr);
					return false;
				}

				if (!TryReadNegotiatedFormat(reader, out sampleRate, out channels, out error))
					return false;

				if (channels > AudioFormats.MaxChannels)
				{
					var sourceChannels = channels;
					hr = RequestFloatOutput(reader, AudioFormats.MaxChannels, sampleRate);

					if (hr < 0
							|| !TryReadNegotiatedFormat(reader, out sampleRate, out channels, out error)
							|| channels != AudioFormats.MaxChannels)
					{
						error = $"\"{path}\" carries {sourceChannels} channels and this host will not downmix it to stereo.";
						return false;
					}
				}

				if (!AudioFormats.IsValidChannels(channels))
				{
					error = $"The decoded audio declares {channels} channels, which is outside the supported mono or stereo range.";
					return false;
				}

				if (!AudioFormats.IsValidSampleRate(sampleRate))
				{
					error = $"The decoded sample rate {sampleRate} Hz is outside {AudioFormats.MinSampleRate} through {AudioFormats.MaxSampleRate} Hz.";
					return false;
				}

				return TryReadAllSamples(reader, channels, out samples, out error);
			}
			finally
			{
				ReleaseCom(reader);
			}
		}

		/// <summary>
		/// Asks the first audio stream for uncompressed 32-bit float. A non-zero <paramref name="forcedChannels"/>
		/// fully specifies the type, which is what makes the reader insert its audio resampler; leaving it zero
		/// keeps whatever layout the source already has, so a mono file stays mono.
		/// </summary>
		private static int RequestFloatOutput(IMFSourceReader reader, int forcedChannels, int forcedRate)
		{
			IMFMediaType type = null;

			try
			{
				var hr = MediaFoundationNative.MFCreateMediaType(out type);

				if (hr < 0)
					return hr;

				if (type == null)
					return ENoInterface;

				hr = type.SetGUID(MfMtMajorType, MfMediaTypeAudio);

				//SubtypeIeeeFloat is MFAudioFormat_Float: the WAVE_FORMAT_IEEE_FLOAT subtype GUID this file
				//already carries for the render path.
				if (hr >= 0)
					hr = type.SetGUID(MfMtSubtype, SubtypeIeeeFloat);

				if (hr >= 0 && forcedChannels > 0)
				{
					var blockAlign = forcedChannels * MfFloatSampleBytes;
					hr = type.SetUINT32(MfMtAudioBitsPerSample, MfBitsPerFloatSample);

					if (hr >= 0)
						hr = type.SetUINT32(MfMtAudioNumChannels, (uint)forcedChannels);

					if (hr >= 0)
						hr = type.SetUINT32(MfMtAudioSamplesPerSecond, (uint)forcedRate);

					if (hr >= 0)
						hr = type.SetUINT32(MfMtAudioBlockAlignment, (uint)blockAlign);

					if (hr >= 0)
						hr = type.SetUINT32(MfMtAudioAvgBytesPerSecond, (uint)(forcedRate * blockAlign));
				}

				if (hr >= 0)
					hr = reader.SetCurrentMediaType(MfFirstAudioStream, 0, type);

				return hr;
			}
			finally
			{
				ReleaseCom(type);
			}
		}

		/// <summary>Reads back what the reader actually agreed to, which is the only authority on rate and layout.</summary>
		private static bool TryReadNegotiatedFormat(IMFSourceReader reader, out int sampleRate, out int channels, out string error)
		{
			sampleRate = 0;
			channels = 0;
			error = "";
			IMFMediaType type = null;

			try
			{
				var hr = reader.GetCurrentMediaType(MfFirstAudioStream, out type);

				if (hr < 0 || type == null)
				{
					error = DescribeMediaFoundation("Reading the negotiated audio format", hr);
					return false;
				}

				var channelHr = type.GetUINT32(MfMtAudioNumChannels, out var negotiatedChannels);
				var rateHr = type.GetUINT32(MfMtAudioSamplesPerSecond, out var negotiatedRate);

				if (channelHr < 0 || rateHr < 0)
				{
					error = "The decoded audio format declares no channel count and sample rate.";
					return false;
				}

				//Compared before the narrowing cast, because a nonsensical declaration has to be refused rather
				//than wrapped into a plausible int.
				if (negotiatedChannels > int.MaxValue || negotiatedRate > int.MaxValue)
				{
					error = "The decoded audio format declares an impossible channel count or sample rate.";
					return false;
				}

				channels = (int)negotiatedChannels;
				sampleRate = (int)negotiatedRate;
				return true;
			}
			finally
			{
				ReleaseCom(type);
			}
		}

		/// <summary>
		/// Drains the reader to end of stream, appending every contiguous buffer. Chunks are joined once at the
		/// end rather than grown into: a doubling array would copy the whole clip repeatedly on the way to the
		/// half-gigabyte ceiling.
		/// </summary>
		private static bool TryReadAllSamples(IMFSourceReader reader, int channels, out float[] samples, out string error)
		{
			samples = null;
			error = "";
			var chunks = new List<float[]>();
			var frameBytes = (uint)(channels * MfFloatSampleBytes);
			long total = 0;

			while (true)
			{
				IMFSample sample = null;
				IMFMediaBuffer buffer = null;
				var locked = false;

				try
				{
					var hr = reader.ReadSample(MfFirstAudioStream, 0, out _, out var flags, out _, out sample);

					if (hr < 0)
					{
						error = DescribeMediaFoundation("Decoding the audio stream", hr);
						return false;
					}

					if ((flags & MfReaderError) != 0)
					{
						error = "The audio stream reported an error part-way through the file.";
						return false;
					}

					if ((flags & MfReaderCurrentMediaTypeChanged) != 0)
					{
						error = "The file changes audio format part-way through, which this decoder does not follow.";
						return false;
					}

					if ((flags & MfReaderEndOfStream) != 0)
						break;

					//A stream tick or a gap carries no sample and is not the end of anything.
					if (sample == null)
						continue;

					hr = sample.ConvertToContiguousBuffer(out buffer);

					if (hr < 0 || buffer == null)
					{
						error = DescribeMediaFoundation("Flattening a decoded audio sample", hr);
						return false;
					}

					hr = buffer.Lock(out var data, out _, out var byteLength);

					if (hr < 0)
					{
						error = DescribeMediaFoundation("Reading a decoded audio buffer", hr);
						return false;
					}

					locked = true;
					//Only whole frames are taken: a partial frame would shift every later sample into the wrong
					//channel for the rest of the clip.
					var usable = byteLength - (byteLength % frameBytes);

					if (usable == 0)
						continue;

					var count = (int)(usable / MfFloatSampleBytes);

					if ((total + count) * (long)MfFloatSampleBytes > AudioFormats.MaxClipBytes)
					{
						error = $"The decoded audio would exceed the {AudioFormats.MaxClipBytes / (1024 * 1024)} MiB per-clip limit.";
						return false;
					}

					var chunk = new float[count];
					Marshal.Copy(data, chunk, 0, count);
					chunks.Add(chunk);
					total += count;
				}
				finally
				{
					if (locked)
						_ = buffer.Unlock();

					ReleaseCom(buffer);
					ReleaseCom(sample);
				}
			}

			if (total == 0)
			{
				error = "The file decoded to no audio frames.";
				return false;
			}

			var result = new float[total];
			var written = 0;

			foreach (var chunk in chunks)
			{
				chunk.CopyTo(result, written);
				written += chunk.Length;
			}

			//A lossy decoder legitimately reconstructs a sample a fraction of a decibel past full scale, so the
			//overshoot is clamped into the range every consumer is promised instead of refusing a good file.
			for (var i = 0; i < result.Length; i++)
			{
				var v = result[i];
				result[i] = !float.IsFinite(v) ? 0f : v < -1f ? -1f : v > 1f ? 1f : v;
			}

			samples = result;
			return true;
		}

		/// <summary>
		/// Probes Media Foundation exactly once per process and caches the answer as a tri-state. Absence is an
		/// expected outcome on a Windows N edition, so it becomes an empty format list rather than an exception
		/// at type load or a failure discovered at decode time.
		/// </summary>
		private static bool EnsureMediaFoundation(out string reason)
		{
			var state = Volatile.Read(ref decoderProbeState);

			if (state == 0)
			{
				lock (decoderProbeLock)
				{
					if (Volatile.Read(ref decoderProbeState) == 0)
					{
						var failure = MediaFoundationReason;
						var thrown = RunOnMtaWorker("Keysharp media probe", () => failure = ProbeMediaFoundation());

						if (thrown != null)
							failure = $"{MediaFoundationReason} ({thrown.Message})";

						decoderProbeReason = failure;
						Volatile.Write(ref decoderProbeState, failure.Length == 0 ? 1 : 2);
					}
				}

				state = Volatile.Read(ref decoderProbeState);
			}

			reason = state == 1 ? "" : decoderProbeReason;
			return state == 1;
		}

		/// <summary>The probe body, on an MTA worker. Returns an empty string when the platform really came up.</summary>
		private static string ProbeMediaFoundation()
		{
			IMFMediaType probe = null;

			try
			{
				//The modules are loaded by name before any P/Invoke can bind, because a missing export has to
				//read as "no decoder on this host" rather than as a DllNotFoundException out of a property get.
				//Neither handle is freed: the process keeps Media Foundation loaded for as long as it may decode.
				if (!NativeLibrary.TryLoad("mfplat.dll", out _) || !NativeLibrary.TryLoad("mfreadwrite.dll", out _))
					return MediaFoundationReason;

				//MFStartup is process-scoped and reference-counted. It is never matched with MFShutdown, because
				//a decode on any other thread depends on it and this backend cannot know when the last one ended.
				var hr = MediaFoundationNative.MFStartup(MfVersion, MfStartupNoSocket);

				if (hr < 0)
					return $"{MediaFoundationReason} (MFStartup failed with HRESULT 0x{hr:X8}.)";

				//Creating one media type proves the platform initialized rather than merely loaded, which is what
				//separates a usable host from one whose modules are present but broken.
				hr = MediaFoundationNative.MFCreateMediaType(out probe);
				return hr < 0 || probe == null
					   ? $"{MediaFoundationReason} (MFCreateMediaType failed with HRESULT 0x{hr:X8}.)"
					   : "";
			}
			catch (Exception ex)
			{
				return $"{MediaFoundationReason} ({ex.Message})";
			}
			finally
			{
				ReleaseCom(probe);
			}
		}

		/// <summary>
		/// Runs one body on a private MTA worker and waits for it, so that every Media Foundation object is
		/// created, called and released on the thread that owns it, exactly as the render stream does. It also
		/// keeps a whole-file decode off a caller's message-pumping thread.
		/// </summary>
		private static Exception RunOnMtaWorker(string name, Action body)
		{
			Exception thrown = null;
			var worker = new Thread(() =>
			{
				var comInitialized = false;

				try
				{
					var hr = WasapiNative.CoInitializeEx(0, WasapiNative.CoinitMultithreaded);
					comInitialized = hr == 0 || hr == 1;   // S_OK and S_FALSE each owe one CoUninitialize
					body();
				}
				catch (Exception ex)
				{
					thrown = ex;
				}
				finally
				{
					if (comInitialized)
						WasapiNative.CoUninitialize();
				}
			})
			{
				IsBackground = true,
				Name = name,
			};
			worker.SetApartmentState(ApartmentState.MTA);
			worker.Start();
			worker.Join();
			return thrown;
		}

		/// <summary>Turns one Media Foundation HRESULT into a sentence that names what actually went wrong.</summary>
		private static string DescribeMediaFoundation(string what, int hr) => hr switch
		{
			MfENotFound => $"{what} failed: the file was not found.",
			MfEPathNotFound => $"{what} failed: the path was not found.",
			EAccessDenied => $"{what} failed: Windows denied access to the file.",
			MfEUnsupportedByteStreamType => $"{what} failed: the container is not one Media Foundation recognizes.",
			MfEInvalidMediaType => $"{what} failed: this host has no decoder for the file's audio.",
			_ => $"{what} failed with HRESULT 0x{hr:X8}.",
		};

		private void Forget(IAudioDeviceWatcher watcher) => watchers.TryRemove(watcher, out _);

		/// <summary>Activates the endpoint's own meter, which observes the endpoint ahead of its volume control.</summary>
		private bool TryOpenDeviceMeter(string deviceId, out IAudioNativeMeter meter, out string error)
		{
			meter = null;
			MMDeviceEnumerator enumerator = null;
			MMDevice device = null;

			try
			{
				enumerator = new MMDeviceEnumerator();

				if (!TryResolveExact(enumerator, deviceId, out device, out _))
				{
					error = $"Audio device \"{deviceId}\" is not present.";
					return false;
				}

				var iid = MMDevice.IID_IAudioMeterInformation;
				var hr = device.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated);

				if (hr < 0 || activated is not IAudioMeterInformation information)
				{
					error = FormatHr("IMMDevice.Activate(IAudioMeterInformation)", hr < 0 ? hr : ENoInterface);
					return false;
				}

				meter = new WasapiMeter(information);
				error = "";
				return true;
			}
			catch (Exception ex)
			{
				error = $"Audio device \"{deviceId}\" exposes no level meter. ({ex.Message})";
				return false;
			}
			finally
			{
				device?.Dispose();
				enumerator?.Dispose();
			}
		}

		private bool TryOpenSessionMeter(string sessionId, out IAudioNativeMeter meter, out string error)
		{
			meter = null;

			if (!TryFindSession(sessionId, out var control, out _, out _))
			{
				error = $"Audio session \"{sessionId}\" is no longer present.";
				return false;
			}

			try
			{
				if (control is not IAudioMeterInformation information)
				{
					error = $"Audio session \"{sessionId}\" exposes no level meter.";
					ReleaseCom(control);
					return false;
				}

				//The meter is the same runtime callable wrapper as the control, so it releases exactly once.
				meter = new WasapiMeter(information);
				error = "";
				return true;
			}
			catch (Exception ex)
			{
				ReleaseCom(control);
				error = $"Audio session \"{sessionId}\" exposes no level meter. ({ex.Message})";
				return false;
			}
		}

		/// <summary>Adds every describable session on one endpoint to the running result.</summary>
		private static void CollectSessions(MMDevice device, AudioDeviceKind kind, List<AudioSessionDescriptor> result,
											HashSet<string> seen)
		{
			IAudioSessionManager2 manager = null;
			IAudioSessionEnumerator sessions = null;

			try
			{
				var endpointId = device.ID;
				var iid = IID_IAudioSessionManager2;

				if (device.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated) < 0
						|| activated is not IAudioSessionManager2 sessionManager)
					return;

				manager = sessionManager;

				if (manager.GetSessionEnumerator(out sessions) < 0 || sessions == null || sessions.GetCount(out var count) < 0)
					return;

				for (var i = 0; i < count; i++)
				{
					IAudioSessionControl control = null;

					try
					{
						if (sessions.GetSession(i, out control) < 0 || control == null)
							continue;

						if (TryDescribe(control, endpointId, kind, out var descriptor) && seen.Add(descriptor.Id))
							result.Add(descriptor);
					}
					catch (Exception)
					{
						//A session that ended between GetCount and this read is simply not in the answer.
					}
					finally
					{
						ReleaseCom(control);
					}
				}
			}
			catch (Exception)
			{
				//An endpoint whose session manager refuses activation contributes nothing and fails nothing.
			}
			finally
			{
				ReleaseCom(sessions);
				ReleaseCom(manager);
			}
		}

		/// <summary>
		/// Reads one session into the portable descriptor. A session without an instance identifier is skipped:
		/// without it there is no id that survives a process id or list position being reused.
		/// </summary>
		private static bool TryDescribe(IAudioSessionControl control, string endpointId, AudioDeviceKind kind,
										out AudioSessionDescriptor descriptor)
		{
			descriptor = default;

			if (control is not IAudioSessionControl2 control2
					|| control2.GetSessionInstanceIdentifier(out var instance) < 0
					|| string.IsNullOrEmpty(instance))
				return false;

			var state = "Inactive";

			if (control.GetState(out var nativeState) >= 0)
				state = nativeState switch
				{
					AudioSessionState.AudioSessionStateActive => "Active",
					AudioSessionState.AudioSessionStateExpired => "Expired",
					_ => "Inactive",
				};

			long processId = 0;
			var processName = "";
			var hr = control2.GetProcessId(out var nativeProcessId);

			//AUDCLNT_S_NO_SINGLE_PROCESS is a success whose process id names no single owner, so the session is
			//reported without one rather than with one that would point at the wrong application.
			if (hr >= 0 && hr != AudclntSNoSingleProcess && nativeProcessId != 0)
			{
				processId = nativeProcessId;
				processName = ProcessNameOrEmpty(nativeProcessId);
			}

			var displayName = "";

			if (control.GetDisplayName(out var name) >= 0 && name != null)
				displayName = name;

			var hasVolume = control is ISimpleAudioVolume;
			descriptor = new AudioSessionDescriptor($"{endpointId}|{instance}", endpointId, kind, processId, processName,
													displayName, state, control2.IsSystemSoundsSession() == 0, hasVolume,
													hasVolume, control is IAudioMeterInformation);
			return true;
		}

		private static string ProcessNameOrEmpty(uint processId)
		{
			try
			{
				using var process = Process.GetProcessById((int)processId);
				return process.ProcessName;
			}
			catch (Exception)
			{
				//The owning process may already have exited, or be one this token cannot open; a session whose
				//name cannot be read is still a session that can be listed and controlled.
				return "";
			}
		}

		/// <summary>
		/// Resolves a session id back to its live control, which the caller owns and must release. The id is exact:
		/// a session that ended, or one whose instance identifier changed, resolves to nothing rather than to its
		/// successor on the same endpoint.
		/// </summary>
		private bool TryFindSession(string sessionId, out IAudioSessionControl control, out AudioDeviceKind kind,
									out string endpointId)
		{
			control = null;
			kind = AudioDeviceKind.Output;
			endpointId = "";

			if (string.IsNullOrEmpty(sessionId) || !EnsureProbed(out _))
				return false;

			//The endpoint id is the part before the first separator: an endpoint id never contains one, while the
			//session instance identifier after it routinely does.
			var split = sessionId.IndexOf('|');

			if (split <= 0)
				return false;

			endpointId = sessionId[..split];
			MMDeviceEnumerator enumerator = null;
			MMDevice device = null;
			IAudioSessionManager2 manager = null;
			IAudioSessionEnumerator sessions = null;

			try
			{
				enumerator = new MMDeviceEnumerator();

				if (!TryResolveExact(enumerator, endpointId, out device, out kind))
					return false;

				var iid = IID_IAudioSessionManager2;

				if (device.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated) < 0
						|| activated is not IAudioSessionManager2 sessionManager)
					return false;

				manager = sessionManager;

				if (manager.GetSessionEnumerator(out sessions) < 0 || sessions == null || sessions.GetCount(out var count) < 0)
					return false;

				for (var i = 0; i < count; i++)
				{
					IAudioSessionControl candidate = null;

					try
					{
						if (sessions.GetSession(i, out candidate) < 0 || candidate == null)
							continue;

						if (candidate is IAudioSessionControl2 candidate2
								&& candidate2.GetSessionInstanceIdentifier(out var instance) >= 0
								&& string.Equals($"{endpointId}|{instance}", sessionId, StringComparison.Ordinal))
						{
							control = candidate;
							candidate = null;
							return true;
						}
					}
					finally
					{
						ReleaseCom(candidate);
					}
				}

				return false;
			}
			catch (Exception)
			{
				ReleaseCom(control);
				control = null;
				return false;
			}
			finally
			{
				ReleaseCom(sessions);
				ReleaseCom(manager);
				device?.Dispose();
				enumerator?.Dispose();
			}
		}

		/// <summary>
		/// Probes the MMDevice service exactly once and caches the answer as a tri-state. A missing or stopped
		/// service is an expected outcome that becomes an actionable reason, never an exception.
		/// </summary>
		private bool EnsureProbed(out string reason)
		{
			if (Volatile.Read(ref disposed) != 0)
			{
				reason = DisposedReason;
				return false;
			}

			var state = Volatile.Read(ref probeState);

			if (state == 0)
			{
				lock (probeLock)
				{
					if (Volatile.Read(ref probeState) == 0)
					{
						try
						{
							var probe = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;

							if (probe != null)
							{
								_ = Marshal.ReleaseComObject(probe);
								probeReason = "";
								Volatile.Write(ref probeState, 1);
							}
							else
							{
								probeReason = ServiceReason;
								Volatile.Write(ref probeState, 2);
							}
						}
						catch (Exception ex)
						{
							probeReason = $"{ServiceReason} ({ex.Message})";
							Volatile.Write(ref probeState, 2);
						}
					}
				}

				state = Volatile.Read(ref probeState);
			}

			reason = state == 1 ? "" : probeReason;
			return state == 1;
		}

		private static DataFlow FlowOf(AudioDeviceKind kind) => kind == AudioDeviceKind.Output ? DataFlow.Render : DataFlow.Capture;

		private static AudioDeviceKind KindOf(DataFlow flow) => flow == DataFlow.Capture ? AudioDeviceKind.Input : AudioDeviceKind.Output;

		/// <summary>A name is for humans, so an endpoint that will not surrender one is still a usable device.</summary>
		private static string NameOf(MMDevice device)
		{
			try
			{
				return device.FriendlyName;
			}
			catch (Exception)
			{
				return "Unknown";
			}
		}

		private static string DefaultIdOrEmpty(MMDeviceEnumerator enumerator, DataFlow flow)
		{
			MMDevice device = null;

			try
			{
				if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
					return "";

				device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
				return device.ID;
			}
			catch (Exception)
			{
				return "";
			}
			finally
			{
				device?.Dispose();
			}
		}

		/// <summary>Resolves a blank id to the current default endpoint of that kind, and any other id exactly.</summary>
		/// <summary>
		/// One enumerator reused across calls. Creating it is a CoCreateInstance, which a script polling volume
		/// or activity would otherwise pay on every property read. The MMDevice API objects are registered
		/// ThreadingModel=Both, so the instance is usable from whichever thread a script member runs on; any
		/// failure through it drops the cache and the caller retries once with a fresh one, so the worst outcome
		/// is the per-call creation this replaces.
		/// </summary>
		private MMDeviceEnumerator shared;

		private MMDeviceEnumerator RentEnumerator()
		{
			var existing = Volatile.Read(ref shared);

			if (existing != null)
				return existing;

			var created = new MMDeviceEnumerator();
			return Interlocked.CompareExchange(ref shared, created, null) ?? created;
		}

		private void InvalidateEnumerator()
		{
			var stale = Interlocked.Exchange(ref shared, null);

			try
			{
				stale?.Dispose();
			}
			catch (Exception)
			{
				//A dead enumerator cannot be released cleanly, and nothing downstream depends on it having been.
			}
		}

		/// <summary>
		/// Activates the endpoint's raw volume interface. The vendored <c>AudioEndpointVolume</c> wrapper is
		/// bypassed on purpose: its constructor enumerates channels, reads step information, hardware support and
		/// the volume range, and registers a change-notification callback, which is roughly a dozen extra COM
		/// calls plus a CCW registration for what is a single scalar read on this path.
		/// </summary>
		/// <summary>
		/// Runs one operation against the endpoint's raw volume interface and releases it again. The vendored
		/// <c>AudioEndpointVolume</c> wrapper is bypassed on purpose: its constructor enumerates channels, reads
		/// step information, hardware support and the volume range, and registers a change-notification callback,
		/// which is roughly a dozen extra COM calls plus a CCW registration for what is one scalar here.
		/// </summary>
		private static bool WithEndpointVolume(MMDevice device, Func<IAudioEndpointVolume, bool> action)
		{
			var iid = MMDevice.IID_IAudioEndpointVolume;

			if (device.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated) < 0)
				return false;

			if (activated is not IAudioEndpointVolume endpoint)
				return false;

			try
			{
				return action(endpoint);
			}
			finally
			{
				//Activate handed over a reference this method owns; without this every property read abandons an
				//RCW to the finalizer queue.
				ReleaseCom(endpoint);
			}
		}

		/// <summary>
		/// Runs one bounded operation against a resolved endpoint. Every endpoint member has the same shape —
		/// make an enumerator, resolve the exact id, act, release both whatever happened — and an MMDevice must
		/// not outlive the enumerator that produced it, so the ownership lives here once rather than per member.
		/// </summary>
		private bool WithDevice(AudioDeviceKind kind, string id, Func<MMDevice, bool> action)
		{
			if (!EnsureProbed(out _))
				return false;

			if (TryWithDevice(kind, id, action, out var result))
				return result;

			//The shared enumerator failed. It is discarded and the call retried once on a fresh one, which is what
			//keeps a stale or apartment-hostile instance from turning into a permanent failure.
			InvalidateEnumerator();
			return TryWithDevice(kind, id, action, out result) && result;
		}

		private bool TryWithDevice(AudioDeviceKind kind, string id, Func<MMDevice, bool> action, out bool result)
		{
			result = false;
			MMDevice device = null;

			try
			{
				result = TryResolveDevice(RentEnumerator(), kind, id, out device) && action(device);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
			finally
			{
				device?.Dispose();
			}
		}

		private static bool TryResolveDevice(MMDeviceEnumerator enumerator, AudioDeviceKind kind, string id, out MMDevice device)
		{
			device = null;
			var flow = FlowOf(kind);

			try
			{
				if (string.IsNullOrEmpty(id))
				{
					if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
						return false;

					device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
					return true;
				}

				var resolved = enumerator.GetDevice(id);

				if (resolved == null)
					return false;

				if (resolved.DataFlow != flow || !string.Equals(resolved.ID, id, StringComparison.Ordinal))
				{
					resolved.Dispose();
					return false;
				}

				device = resolved;
				return true;
			}
			catch (Exception)
			{
				device?.Dispose();
				device = null;
				return false;
			}
		}

		/// <summary>
		/// Resolves an exact endpoint id of either direction, which is how a session id and a meter target name
		/// their device: the caller already knows the id came from one particular endpoint.
		/// </summary>
		private static bool TryResolveExact(MMDeviceEnumerator enumerator, string id, out MMDevice device, out AudioDeviceKind kind)
		{
			device = null;
			kind = AudioDeviceKind.Output;

			try
			{
				var resolved = enumerator.GetDevice(id);

				if (resolved == null)
					return false;

				var flow = resolved.DataFlow;

				if ((flow != DataFlow.Render && flow != DataFlow.Capture)
						|| !string.Equals(resolved.ID, id, StringComparison.Ordinal))
				{
					resolved.Dispose();
					return false;
				}

				kind = KindOf(flow);
				device = resolved;
				return true;
			}
			catch (Exception)
			{
				device?.Dispose();
				device = null;
				return false;
			}
		}

		/// <summary>
		/// One initialization attempt on a brand new client, because an IAudioClient whose Initialize failed
		/// cannot be initialized a second time.
		/// </summary>
		private static int TryFormat(MMDevice device, nint pFormat, uint flags, long duration, out IAudioClient client)
		{
			client = null;
			var hr = ActivateClient(device, out var candidate);

			if (hr < 0)
				return hr;

			hr = candidate.Initialize(ShareModeShared, flags, duration, 0, pFormat, 0);

			if (hr < 0)
			{
				ReleaseCom(candidate);
				return hr;
			}

			client = candidate;
			return 0;
		}

		private static int ActivateClient(MMDevice device, out IAudioClient client)
		{
			client = null;
			var iid = MMDevice.IID_IAudioClient;
			var hr = device.deviceInterface.Activate(ref iid, ClsCtx.ALL, 0, out var activated);

			if (hr < 0)
				return hr;

			client = activated as IAudioClient;
			return client == null ? ENoInterface : 0;
		}

		private static bool MixIsFloat(nint pFormat, in WaveFormatEx header)
		{
			if (header.wFormatTag == WaveFormatIeeeFloatTag)
				return true;

			if (header.wFormatTag != WaveFormatExtensibleTag || header.cbSize < ExtensibleExtraBytes)
				return false;

			return Marshal.PtrToStructure<WaveFormatExtensible>(pFormat).SubFormat == SubtypeIeeeFloat;
		}

		/// <summary>Builds a WAVEFORMATEXTENSIBLE in unmanaged memory; the caller frees it.</summary>
		private static nint AllocExtensible(int rate, int channels, bool float32)
		{
			var bits = (ushort)(float32 ? 32 : 16);
			var blockAlign = (ushort)(channels * bits / 8);
			var wf = new WaveFormatExtensible
			{
				Format = new WaveFormatEx
				{
					wFormatTag = WaveFormatExtensibleTag,
					nChannels = (ushort)channels,
					nSamplesPerSec = (uint)rate,
					nAvgBytesPerSec = (uint)(rate * blockAlign),
					nBlockAlign = blockAlign,
					wBitsPerSample = bits,
					cbSize = ExtensibleExtraBytes,
				},
				wValidBitsPerSample = bits,
				//A single channel is the centre speaker; anything wider takes the low speaker bits in order.
				dwChannelMask = channels == 1 ? 0x4u : (1u << channels) - 1u,
				SubFormat = float32 ? SubtypeIeeeFloat : SubtypePcm,
			};
			var block = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatExtensible>());
			Marshal.StructureToPtr(wf, block, false);
			return block;
		}

		private static string FormatHr(string what, int hr) => hr switch
		{
			AudclntEDeviceInvalidated => $"The audio device was removed or reconfigured while {what} ran.",
			AudclntEServiceNotRunning => ServiceReason,
			_ => $"{what} failed with HRESULT 0x{hr:X8}.",
		};

		private static void SafeStop(IAudioClient client)
		{
			if (client == null)
				return;

			try
			{
				_ = client.Stop();
			}
			catch (Exception)
			{
				//A client whose device already vanished cannot be stopped, and teardown continues regardless.
			}
		}

		private static void ReleaseCom(object comObject)
		{
			if (comObject == null)
				return;

			try
			{
				_ = Marshal.ReleaseComObject(comObject);
			}
			catch (Exception)
			{
				//A wrapper the runtime already tore down is one less thing to release, not a teardown failure.
			}
		}

		/// <summary>
		/// The endpoint notification sink. Its methods arrive on system COM threads, so each one does the single
		/// cheapest thing the contract allows: hand the caller its own bounded signal and return.
		/// </summary>
		private sealed class DeviceNotificationClient : IMMNotificationClient
		{
			private readonly Action sink;

			internal DeviceNotificationClient(Action sink) => this.sink = sink;

			public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify();

			public void OnDeviceAdded(string pwstrDeviceId) => Notify();

			public void OnDeviceRemoved(string deviceId) => Notify();

			public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Notify();

			public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => Notify();

			private void Notify()
			{
				try
				{
					sink();
				}
				catch (Exception)
				{
					//A COM notification has nowhere to report to: throwing would only hand the audio service an
					//HRESULT it does not act on, and logging is not bounded work.
				}
			}
		}

		/// <summary>One installed endpoint notification, alive for exactly as long as the caller holds it.</summary>
		private sealed class DeviceWatcher : IAudioDeviceWatcher
		{
			private readonly WasapiAudioBackend owner;
			private MMDeviceEnumerator enumerator;
			private IMMNotificationClient client;   // rooted here so its CCW outlives every native call

			internal DeviceWatcher(WasapiAudioBackend owner, MMDeviceEnumerator enumerator, IMMNotificationClient client)
			{
				this.owner = owner;
				this.enumerator = enumerator;
				this.client = client;
			}

			public void Dispose()
			{
				var e = Interlocked.Exchange(ref enumerator, null);

				if (e == null)
					return;

				//Unregister first: only after it returns can no further notification reach the sink, which is what
				//makes releasing the callback object safe.
				try
				{
					_ = e.UnregisterEndpointNotificationCallback(client);
				}
				catch (Exception)
				{
					//An enumerator whose service went away has already dropped every registration it held.
				}

				client = null;
				e.Dispose();
				owner.Forget(this);
			}
		}

		/// <summary>
		/// One shared-mode, event-driven WASAPI render stream. Every COM object it uses is created, called and
		/// released on its own MTA worker, so no interface pointer ever crosses an apartment, and the worker is
		/// the only thread that touches the device.
		/// </summary>
		private sealed class WasapiOutputStream : IAudioOutputStream
		{
			private const double MinLatencyMs = 3.0;
			private const double MaxLatencyMs = 2000.0;

			private static readonly Guid IID_IAudioRenderClient = new ("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

			private readonly string requestedDeviceId;
			private readonly double requestedLatencyMs;
			private readonly IAudioRenderSource source;
			private readonly Thread worker;
			private readonly ManualResetEventSlim openCompleted = new (false);

			private nint audioEvent;
			private nint cancelEvent;
			private nint controlEvent;

			private float[] renderBuffer;
			private short[] pcmBuffer;
			private int bufferFrames;
			private int channels;
			private int sampleRate;
			private bool renderPcm16;

			private AudioStreamFormat negotiated;
			private string openError = "";
			private bool openSucceeded;

			private long latencyBits;
			private int desiredRunning = 1;   // an opened stream is a running stream; Start/Stop toggle it after that
			private int deviceLost;
			private int disposed;

			internal WasapiOutputStream(string deviceId, double latencyMilliseconds, IAudioRenderSource source)
			{
				requestedDeviceId = deviceId;
				requestedLatencyMs = latencyMilliseconds;
				this.source = source;
				audioEvent = WasapiNative.CreateEvent(0, false, false, 0);
				cancelEvent = WasapiNative.CreateEvent(0, true, false, 0);
				controlEvent = WasapiNative.CreateEvent(0, false, false, 0);
				worker = new Thread(Run)
				{
					IsBackground = true,
					Name = "Keysharp WASAPI render",
				};
				worker.SetApartmentState(ApartmentState.MTA);
				worker.Start();
			}

			/// <inheritdoc/>
			public AudioStreamFormat Format => negotiated;

			/// <inheritdoc/>
			public double LatencyMilliseconds => BitConverter.Int64BitsToDouble(Interlocked.Read(ref latencyBits));

			/// <inheritdoc/>
			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			/// <inheritdoc/>
			public void Start() => RequestRunning(true);

			/// <inheritdoc/>
			public void Stop() => RequestRunning(false);

			/// <summary>Blocks the opening caller until the worker has either a live stream or a reason it has none.</summary>
			internal bool WaitForOpen(out string error)
			{
				if (!openCompleted.Wait(OpenTimeoutMs))
				{
					error = "Timed out waiting for the WASAPI output stream to open.";
					return false;
				}

				error = openSucceeded ? "" : openError;
				return openSucceeded;
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				var cancel = Volatile.Read(ref cancelEvent);

				if (cancel != 0)
					_ = WasapiNative.SetEvent(cancel);

				//A worker that will not quiesce still owns these handles, so leaving them open is the only safe
				//outcome; closing them would free memory the render loop can still be waiting on.
				if (worker.IsAlive && !worker.Join(JoinTimeoutMs))
					return;

				CloseHandles();
			}

			private void RequestRunning(bool running)
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				Volatile.Write(ref desiredRunning, running ? 1 : 0);
				var handle = Volatile.Read(ref controlEvent);

				if (handle != 0)
					_ = WasapiNative.SetEvent(handle);
			}

			private void CloseHandles()
			{
				var a = Interlocked.Exchange(ref audioEvent, 0);
				var c = Interlocked.Exchange(ref cancelEvent, 0);
				var k = Interlocked.Exchange(ref controlEvent, 0);

				if (a != 0)
					_ = WindowsAPI.CloseHandle(a);

				if (c != 0)
					_ = WindowsAPI.CloseHandle(c);

				if (k != 0)
					_ = WindowsAPI.CloseHandle(k);

				openCompleted.Dispose();
			}

			/// <summary>The worker body: one apartment, one device, one stream, from first Activate to last release.</summary>
			private void Run()
			{
				var comInitialized = false;
				MMDeviceEnumerator enumerator = null;
				MMDevice device = null;
				IAudioClient client = null;
				IAudioRenderClient render = null;

				try
				{
					var hr = WasapiNative.CoInitializeEx(0, WasapiNative.CoinitMultithreaded);
					comInitialized = hr == 0 || hr == 1;   // S_OK and S_FALSE each owe one CoUninitialize

					if (TryOpen(out enumerator, out device, out client, out render, out var error))
					{
						openSucceeded = true;
						openCompleted.Set();
						RenderLoop(client, render);
					}
					else
					{
						openError = error;
					}
				}
				catch (Exception ex)
				{
					//An exception after the stream went live can only end the render loop, which is device loss as
					//far as the owner is concerned.
					if (openSucceeded)
						MarkLost();
					else
						openError = ex.Message;
				}
				finally
				{
					SafeStop(client);
					ReleaseCom(render);
					ReleaseCom(client);
					device?.Dispose();
					enumerator?.Dispose();

					if (comInitialized)
						WasapiNative.CoUninitialize();

					//A worker that ends before it ever opened must not leave its caller waiting out the timeout.
					openCompleted.Set();
				}
			}

			private bool TryOpen(out MMDeviceEnumerator enumerator, out MMDevice device, out IAudioClient client,
								 out IAudioRenderClient render, out string error)
			{
				enumerator = null;
				device = null;
				client = null;
				render = null;

				if (audioEvent == 0 || cancelEvent == 0 || controlEvent == 0)
				{
					error = "The audio stream could not create its wait handles.";
					return false;
				}

				try
				{
					enumerator = new MMDeviceEnumerator();

					if (string.IsNullOrEmpty(requestedDeviceId))
					{
						if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
						{
							error = "No default audio output device is present.";
							return false;
						}

						device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
					}
					else
					{
						device = enumerator.GetDevice(requestedDeviceId);

						if (device == null || device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
						{
							error = $"Audio output device \"{requestedDeviceId}\" is not present.";
							return false;
						}
					}
				}
				catch (Exception ex)
				{
					error = $"Audio output device \"{requestedDeviceId}\" could not be opened. ({ex.Message})";
					return false;
				}

				if (!TryInitializeClient(device, out client, out error))
					return false;

				var hr = client.SetEventHandle(audioEvent);

				if (hr < 0)
				{
					error = FormatHr("IAudioClient.SetEventHandle", hr);
					return false;
				}

				hr = client.GetBufferSize(out bufferFrames);

				if (hr < 0 || bufferFrames <= 0)
				{
					error = FormatHr("IAudioClient.GetBufferSize", hr);
					return false;
				}

				hr = client.GetService(IID_IAudioRenderClient, out var service);

				if (hr < 0 || service is not IAudioRenderClient renderClient)
				{
					error = FormatHr("IAudioClient.GetService(IAudioRenderClient)", hr < 0 ? hr : ENoInterface);
					return false;
				}

				render = renderClient;
				renderBuffer = new float[bufferFrames * channels];

				if (renderPcm16)
					pcmBuffer = new short[bufferFrames * channels];

				negotiated = new AudioStreamFormat(sampleRate, channels);
				PublishLatency(client);

				//Fill the whole endpoint buffer before the first Start, so the device never plays the memory it
				//was handed uninitialized.
				hr = WriteAvailable(client, render, false);

				if (hr < 0)
				{
					error = FormatHr("IAudioRenderClient prefill", hr);
					return false;
				}

				hr = client.Start();

				if (hr < 0)
				{
					error = FormatHr("IAudioClient.Start", hr);
					return false;
				}

				error = "";
				return true;
			}

			/// <summary>
			/// Waits on cancellation, the start/stop request and the audio-ready event together, so a stopped
			/// stream that stops signalling still wakes for its next command.
			/// </summary>
			private void RenderLoop(IAudioClient client, IAudioRenderClient render)
			{
				//Registered as a Pro Audio task so the scheduler treats a missed quantum as the glitch it is. A host
				//without avrt.dll, or a refused registration, simply keeps default scheduling.
				nint mmcss = 0;
				uint taskIndex = 0;

				try
				{
					mmcss = WasapiNative.AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
				}
				catch (Exception)
				{
				}

				try
				{
					RenderQuanta(client, render);
				}
				finally
				{
					if (mmcss != 0)
					{
						try
						{
							_ = WasapiNative.AvRevertMmThreadCharacteristics(mmcss);
						}
						catch (Exception)
						{
						}
					}
				}
			}

			private void RenderQuanta(IAudioClient client, IAudioRenderClient render)
			{
				var handles = new[] { cancelEvent, controlEvent, audioEvent };
				var running = true;

				while (true)
				{
					var wait = WasapiNative.WaitForMultipleObjects(3, handles, false, WaitPeriodMs);

					if (wait == WasapiNative.WaitObject0 || wait == WasapiNative.WaitFailed)
						break;

					var wanted = Volatile.Read(ref desiredRunning) != 0;

					if (wanted != running)
					{
						var toggle = wanted ? client.Start() : client.Stop();

						if (toggle < 0)
						{
							MarkLost();
							break;
						}

						running = wanted;
					}

					if (!running || wait != WasapiNative.WaitObject0 + 2)
						continue;

					var hr = WriteAvailable(client, render, true);

					if (hr < 0)
					{
						MarkLost();
						break;
					}
				}
			}

			/// <summary>
			/// One quantum: ask how much room the endpoint has, take exactly that, let the mixer fill it, hand it
			/// back. Allocates nothing and calls no script.
			/// </summary>
			private int WriteAvailable(IAudioClient client, IAudioRenderClient render, bool running)
			{
				var hr = client.GetCurrentPadding(out var padding);

				if (hr < 0)
					return hr;

				//A running endpoint whose buffer drained to nothing missed a deadline; the prefill legitimately
				//starts empty, which is why this only counts once the stream is live.
				if (running && padding == 0)
					source.NoteUnderrun();

				var frames = bufferFrames - padding;

				if (frames <= 0)
					return 0;

				hr = render.GetBuffer(frames, out var data);

				if (hr < 0)
					return hr;

				var samples = frames * channels;
				var flags = 0;

				try
				{
					if (renderPcm16)
					{
						source.Fill(new Span<float>(renderBuffer, 0, samples));
						var pcm = pcmBuffer;

						for (var i = 0; i < samples; i++)
						{
							var s = renderBuffer[i];
							s = s < -1f ? -1f : s > 1f ? 1f : s;
							pcm[i] = (short)(s * 32767f);
						}

						Marshal.Copy(pcm, 0, data, samples);
					}
					else
					{
						//Float32 is what the mixer already produces, so it fills the endpoint buffer in place rather
						//than filling a managed one and copying the whole quantum across on every callback.
						unsafe
						{
							source.Fill(new Span<float>((void*)data, samples));
						}
					}
				}
				catch (Exception)
				{
					//A borrowed buffer must be returned whatever happened to it; silence costs one quantum and
					//keeps the stream alive for the next.
					flags = BufferFlagsSilent;
				}

				return render.ReleaseBuffer(frames, flags);
			}

			/// <summary>
			/// Any HRESULT that ends the render loop leaves the stream permanently silent, so the owner is told to
			/// transition whether the cause was the documented device-invalidated code or something rarer.
			/// </summary>
			private void MarkLost() => Volatile.Write(ref deviceLost, 1);

			/// <summary>
			/// Negotiates the stream format: float32 first, then 16-bit PCM, then the engine mix format verbatim.
			/// The first two lean on AUTOCONVERTPCM so the engine, not this code, resamples and remixes.
			/// </summary>
			private bool TryInitializeClient(MMDevice device, out IAudioClient client, out string error)
			{
				client = null;
				nint mixFormat = 0;
				nint requested = 0;

				try
				{
					var hr = ActivateClient(device, out var probe);

					if (hr < 0)
					{
						error = FormatHr("IMMDevice.Activate(IAudioClient)", hr);
						return false;
					}

					hr = probe.GetMixFormat(out mixFormat);
					ReleaseCom(probe);

					if (hr < 0 || mixFormat == 0)
					{
						error = FormatHr("IAudioClient.GetMixFormat", hr);
						return false;
					}

					var mix = Marshal.PtrToStructure<WaveFormatEx>(mixFormat);
					var mixRate = (int)mix.nSamplesPerSec;
					var mixIsFloat = MixIsFloat(mixFormat, mix);
					//The managed bus is mono or stereo, so a wider endpoint is upmixed by the engine rather than here.
					var wantedChannels = Math.Clamp((int)mix.nChannels, AudioFormats.MinChannels, AudioFormats.MaxChannels);
					var duration = BufferDuration();
					var convertFlags = StreamFlagsEventCallback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality;

					requested = AllocExtensible(mixRate, wantedChannels, true);
					hr = TryFormat(device, requested, convertFlags, duration, out client);
					Marshal.FreeHGlobal(requested);
					requested = 0;

					if (hr >= 0)
					{
						sampleRate = mixRate;
						channels = wantedChannels;
						renderPcm16 = false;
						error = "";
						return true;
					}

					if (hr == AudclntEUnsupportedFormat)
					{
						requested = AllocExtensible(mixRate, wantedChannels, false);
						hr = TryFormat(device, requested, convertFlags, duration, out client);
						Marshal.FreeHGlobal(requested);
						requested = 0;

						if (hr >= 0)
						{
							sampleRate = mixRate;
							channels = wantedChannels;
							renderPcm16 = true;
							error = "";
							return true;
						}
					}

					if (hr == AudclntEUnsupportedFormat)
					{
						//Last resort: the engine format itself, which it cannot refuse, provided it is one this
						//code can write.
						var verbatimFloat = mixIsFloat && mix.wBitsPerSample == 32;
						var verbatimPcm16 = !mixIsFloat && mix.wBitsPerSample == 16;

						//Unlike the two attempts above, this one takes the engine format as it stands rather than letting
						//AUTOCONVERTPCM remix it, so a wider endpoint has to be refused: the managed bus carries one or two
						//channels and would leave the rest of a 5.1 frame silent.
						if ((!verbatimFloat && !verbatimPcm16)
								|| mix.nChannels < AudioFormats.MinChannels || mix.nChannels > AudioFormats.MaxChannels)
						{
							error = $"The audio engine mix format ({mix.wBitsPerSample}-bit, {mix.nChannels} channels) cannot be rendered.";
							return false;
						}

						hr = TryFormat(device, mixFormat, StreamFlagsEventCallback, duration, out client);

						if (hr >= 0)
						{
							sampleRate = mixRate;
							channels = mix.nChannels;
							renderPcm16 = verbatimPcm16;
							error = "";
							return true;
						}
					}

					error = FormatHr("IAudioClient.Initialize", hr);
					return false;
				}
				catch (Exception ex)
				{
					error = ex.Message;
					return false;
				}
				finally
				{
					if (requested != 0)
						Marshal.FreeHGlobal(requested);

					if (mixFormat != 0)
						Marshal.FreeCoTaskMem(mixFormat);
				}
			}

			/// <summary>A requested latency in 100 ns units, or zero to let the engine pick its own buffer.</summary>
			private long BufferDuration()
				=> requestedLatencyMs > 0 ? (long)(Math.Clamp(requestedLatencyMs, MinLatencyMs, MaxLatencyMs) * 10_000.0) : 0;

			/// <summary>
			/// What a caller hears is the endpoint buffer it waits through plus whatever the engine adds behind it.
			/// </summary>
			private void PublishLatency(IAudioClient client)
			{
				var latency = sampleRate > 0 ? bufferFrames * 1000.0 / sampleRate : 0.0;

				if (client.GetStreamLatency(out var hns) >= 0 && hns > 0)
					latency += hns / 10_000.0;

				_ = Interlocked.Exchange(ref latencyBits, BitConverter.DoubleToInt64Bits(latency));
			}
		}

		/// <summary>
		/// One shared-mode, event-driven WASAPI capture stream. Microphone capture opens an input endpoint;
		/// SystemOutput opens the same client on an output endpoint with AUDCLNT_STREAMFLAGS_LOOPBACK. Every COM
		/// object is created, called and released on this stream's own MTA worker, exactly as the render stream
		/// does, so no interface pointer crosses an apartment.
		/// </summary>
		private sealed class WasapiInputStream : IAudioInputStream
		{
			private readonly string requestedDeviceId;
			private readonly AudioCaptureSource captureSource;
			private readonly int requestedSampleRate;
			private readonly int requestedChannels;
			private readonly long requestedBufferDuration;
			private readonly IAudioCaptureSink sink;
			private readonly Thread worker;
			private readonly ManualResetEventSlim openCompleted = new (false);

			private nint audioEvent;
			private nint cancelEvent;
			private nint controlEvent;

			private float[] captureBuffer;
			private byte[] packetBuffer;
			private string packetFormat = AudioFormats.Float32;
			private int bytesPerSample = 4;
			private int channels;
			private int sampleRate;
			private bool captureFloat;

			private AudioStreamFormat negotiated;
			private string openError = "";
			private bool openSucceeded;

			private int desiredRunning = 1;   // an opened stream is a running stream; Start/Stop toggle it after that
			private int deviceLost;
			private int disposed;

			internal WasapiInputStream(string deviceId, AudioCaptureSource source, int rate, int channelCount,
									   double chunkMilliseconds, IAudioCaptureSink sink)
			{
				requestedDeviceId = deviceId;
				//A shared-mode capture client delivers on the device period, so this only guarantees the endpoint
				//buffer is large enough to hold one requested chunk; the sink re-chunks whatever actually arrives.
				requestedBufferDuration = (long)(Math.Clamp(chunkMilliseconds, 10.0, 1000.0) * 10_000.0);
				captureSource = source;
				requestedSampleRate = rate;
				requestedChannels = channelCount;
				this.sink = sink;
				audioEvent = WasapiNative.CreateEvent(0, false, false, 0);
				cancelEvent = WasapiNative.CreateEvent(0, true, false, 0);
				controlEvent = WasapiNative.CreateEvent(0, false, false, 0);
				worker = new Thread(Run)
				{
					IsBackground = true,
					Name = "Keysharp WASAPI capture",
				};
				worker.SetApartmentState(ApartmentState.MTA);
				worker.Start();
			}

			/// <inheritdoc/>
			public AudioStreamFormat Format => negotiated;

			/// <inheritdoc/>
			public bool IsDeviceLost => Volatile.Read(ref deviceLost) != 0;

			/// <inheritdoc/>
			public void Start() => RequestRunning(true);

			/// <inheritdoc/>
			public void Stop() => RequestRunning(false);

			/// <summary>Blocks the opening caller until the worker has either a live stream or a reason it has none.</summary>
			internal bool WaitForOpen(out string error)
			{
				if (!openCompleted.Wait(OpenTimeoutMs))
				{
					error = "Timed out waiting for the WASAPI input stream to open.";
					return false;
				}

				error = openSucceeded ? "" : openError;
				return openSucceeded;
			}

			public void Dispose()
			{
				if (Interlocked.Exchange(ref disposed, 1) != 0)
					return;

				var cancel = Volatile.Read(ref cancelEvent);

				if (cancel != 0)
					_ = WasapiNative.SetEvent(cancel);

				//A worker that will not quiesce still owns these handles, so leaving them open is the only safe
				//outcome; closing them would free memory the capture loop can still be waiting on.
				if (worker.IsAlive && !worker.Join(JoinTimeoutMs))
					return;

				CloseHandles();
			}

			private void RequestRunning(bool running)
			{
				if (Volatile.Read(ref disposed) != 0)
					return;

				Volatile.Write(ref desiredRunning, running ? 1 : 0);
				var handle = Volatile.Read(ref controlEvent);

				if (handle != 0)
					_ = WasapiNative.SetEvent(handle);
			}

			private void CloseHandles()
			{
				var a = Interlocked.Exchange(ref audioEvent, 0);
				var c = Interlocked.Exchange(ref cancelEvent, 0);
				var k = Interlocked.Exchange(ref controlEvent, 0);

				if (a != 0)
					_ = WindowsAPI.CloseHandle(a);

				if (c != 0)
					_ = WindowsAPI.CloseHandle(c);

				if (k != 0)
					_ = WindowsAPI.CloseHandle(k);

				openCompleted.Dispose();
			}

			/// <summary>The worker body: one apartment, one device, one stream, from first Activate to last release.</summary>
			private void Run()
			{
				var comInitialized = false;
				MMDeviceEnumerator enumerator = null;
				MMDevice device = null;
				IAudioClient client = null;
				IAudioCaptureClient capture = null;

				try
				{
					var hr = WasapiNative.CoInitializeEx(0, WasapiNative.CoinitMultithreaded);
					comInitialized = hr == 0 || hr == 1;   // S_OK and S_FALSE each owe one CoUninitialize

					if (TryOpen(out enumerator, out device, out client, out capture, out var error))
					{
						openSucceeded = true;
						openCompleted.Set();
						CaptureLoop(client, capture);
					}
					else
					{
						openError = error;
					}
				}
				catch (Exception ex)
				{
					//An exception after the stream went live can only end the capture loop, which is device loss as
					//far as the owner is concerned.
					if (openSucceeded)
						MarkLost();
					else
						openError = ex.Message;
				}
				finally
				{
					SafeStop(client);
					ReleaseCom(capture);
					ReleaseCom(client);
					device?.Dispose();
					enumerator?.Dispose();

					if (comInitialized)
						WasapiNative.CoUninitialize();

					//A worker that ends before it ever opened must not leave its caller waiting out the timeout.
					openCompleted.Set();
				}
			}

			private bool TryOpen(out MMDeviceEnumerator enumerator, out MMDevice device, out IAudioClient client,
								 out IAudioCaptureClient capture, out string error)
			{
				enumerator = null;
				device = null;
				client = null;
				capture = null;

				if (audioEvent == 0 || cancelEvent == 0 || controlEvent == 0)
				{
					error = "The audio stream could not create its wait handles.";
					return false;
				}

				//System output is captured from the endpoint that plays it, so its device selector is an output.
				var flow = captureSource == AudioCaptureSource.Microphone ? DataFlow.Capture : DataFlow.Render;
				var what = flow == DataFlow.Capture ? "input" : "output";

				try
				{
					enumerator = new MMDeviceEnumerator();

					if (string.IsNullOrEmpty(requestedDeviceId))
					{
						if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
						{
							error = $"No default audio {what} device is present.";
							return false;
						}

						device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
					}
					else
					{
						device = enumerator.GetDevice(requestedDeviceId);

						if (device == null || device.DataFlow != flow || device.State != DeviceState.Active)
						{
							error = $"Audio {what} device \"{requestedDeviceId}\" is not present.";
							return false;
						}
					}
				}
				catch (Exception ex)
				{
					error = $"Audio {what} device \"{requestedDeviceId}\" could not be opened. ({ex.Message})";
					return false;
				}

				if (!TryInitializeClient(device, out client, out error))
					return false;

				var hr = client.SetEventHandle(audioEvent);

				if (hr < 0)
				{
					error = DescribeOpen("IAudioClient.SetEventHandle", hr);
					return false;
				}

				hr = client.GetBufferSize(out var bufferFrames);

				if (hr < 0 || bufferFrames <= 0)
				{
					error = DescribeOpen("IAudioClient.GetBufferSize", hr);
					return false;
				}

				hr = client.GetService(IID_IAudioCaptureClient, out var service);

				if (hr < 0 || service is not IAudioCaptureClient captureClient)
				{
					error = DescribeOpen("IAudioClient.GetService(IAudioCaptureClient)", hr < 0 ? hr : ENoInterface);
					return false;
				}

				capture = captureClient;
				//Sized for the whole endpoint buffer, which is the largest packet the engine can hand over, so the
				//capture loop itself allocates nothing.
				captureBuffer = new float[bufferFrames * channels];

				if (!captureFloat)
					packetBuffer = new byte[bufferFrames * channels * bytesPerSample];

				negotiated = new AudioStreamFormat(sampleRate, channels);
				hr = client.Start();

				if (hr < 0)
				{
					error = DescribeOpen("IAudioClient.Start", hr);
					return false;
				}

				error = "";
				return true;
			}

			/// <summary>
			/// Negotiates the capture format. A microphone is asked for the requested rate and channel count as
			/// float32 first, letting the engine resample and remix; loopback and every fallback take the engine
			/// mix format verbatim and convert here, because that is the shape the loopback path guarantees.
			/// </summary>
			private bool TryInitializeClient(MMDevice device, out IAudioClient client, out string error)
			{
				client = null;
				nint mixFormat = 0;
				nint requested = 0;

				try
				{
					var hr = ActivateClient(device, out var probe);

					if (hr < 0)
					{
						error = DescribeOpen("IMMDevice.Activate(IAudioClient)", hr);
						return false;
					}

					hr = probe.GetMixFormat(out mixFormat);
					ReleaseCom(probe);

					if (hr < 0 || mixFormat == 0)
					{
						error = DescribeOpen("IAudioClient.GetMixFormat", hr);
						return false;
					}

					var mix = Marshal.PtrToStructure<WaveFormatEx>(mixFormat);
					var flags = StreamFlagsEventCallback
								| (captureSource == AudioCaptureSource.SystemOutput ? StreamFlagsLoopback : 0u);

					if (captureSource == AudioCaptureSource.Microphone
							&& AudioFormats.IsValidSampleRate(requestedSampleRate)
							&& AudioFormats.IsValidChannels(requestedChannels))
					{
						requested = AllocExtensible(requestedSampleRate, requestedChannels, true);
						hr = TryFormat(device, requested, flags | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality,
									   requestedBufferDuration, out client);
						Marshal.FreeHGlobal(requested);
						requested = 0;

						if (hr >= 0)
						{
							sampleRate = requestedSampleRate;
							channels = requestedChannels;
							captureFloat = true;
							bytesPerSample = 4;
							error = "";
							return true;
						}

						if (hr == EAccessDenied)
						{
							error = CapturePrivacyReason;
							return false;
						}
					}

					if (!TryClassifyMix(mixFormat, mix, out packetFormat, out bytesPerSample, out captureFloat))
					{
						error = $"The audio engine mix format ({mix.wBitsPerSample}-bit, {mix.nChannels} channels) cannot be captured.";
						return false;
					}

					hr = TryFormat(device, mixFormat, flags, requestedBufferDuration, out client);

					if (hr >= 0)
					{
						sampleRate = (int)mix.nSamplesPerSec;
						channels = mix.nChannels;
						error = "";
						return true;
					}

					error = DescribeOpen("IAudioClient.Initialize", hr);
					return false;
				}
				catch (Exception ex)
				{
					error = ex.Message;
					return false;
				}
				finally
				{
					if (requested != 0)
						Marshal.FreeHGlobal(requested);

					if (mixFormat != 0)
						Marshal.FreeCoTaskMem(mixFormat);
				}
			}

			/// <summary>
			/// Waits on cancellation, the start/stop request and the capture-ready event together, so a stopped
			/// stream that stops signalling still wakes for its next command. A loopback stream signals nothing
			/// while the endpoint is idle, which the wait period turns into a harmless timeout rather than a spin.
			/// </summary>
			private void CaptureLoop(IAudioClient client, IAudioCaptureClient capture)
			{
				var handles = new[] { cancelEvent, controlEvent, audioEvent };
				var running = true;

				while (true)
				{
					var wait = WasapiNative.WaitForMultipleObjects(3, handles, false, WaitPeriodMs);

					if (wait == WasapiNative.WaitObject0 || wait == WasapiNative.WaitFailed)
						break;

					var wanted = Volatile.Read(ref desiredRunning) != 0;

					if (wanted != running)
					{
						var toggle = wanted ? client.Start() : client.Stop();

						if (toggle < 0)
						{
							MarkLost();
							break;
						}

						running = wanted;
					}

					if (!running || wait != WasapiNative.WaitObject0 + 2)
						continue;

					if (DrainPackets(capture) < 0)
					{
						MarkLost();
						break;
					}
				}
			}

			/// <summary>
			/// Takes every packet the engine has ready. Each borrowed packet is released before the next one is
			/// asked for, which is what the capture client requires.
			/// </summary>
			private int DrainPackets(IAudioCaptureClient capture)
			{
				var hr = capture.GetNextPacketSize(out var frames);

				while (hr >= 0 && frames > 0)
				{
					hr = capture.GetBuffer(out var data, out var framesRead, out var flags, out _, out _);

					if (hr < 0)
						return hr;

					//AUDCLNT_S_BUFFER_EMPTY is still a successful GetBuffer, and an unmatched one makes the next call
					//fail with AUDCLNT_E_OUT_OF_ORDER, which reads as device loss and kills the stream for good. The
					//release is unconditional and its status ignored, which is correct whether or not one was borrowed.
					if (hr == AudclntSBufferEmpty)
					{
						_ = capture.ReleaseBuffer(0);
						return 0;
					}

					if (framesRead > 0 && data != 0)
						Deliver(data, framesRead, (flags & BufferFlagsSilent) != 0);

					hr = capture.ReleaseBuffer(framesRead);

					if (hr < 0)
						return hr;

					hr = capture.GetNextPacketSize(out frames);
				}

				return hr;
			}

			/// <summary>
			/// One packet: convert it into interleaved float32 and hand it to the sink. Allocates nothing for a
			/// packet the endpoint buffer can hold, and calls no script.
			/// </summary>
			private void Deliver(nint data, int frames, bool silent)
			{
				var samples = frames * channels;

				if (captureBuffer == null || captureBuffer.Length < samples)
					captureBuffer = new float[samples];

				var span = new Span<float>(captureBuffer, 0, samples);

				if (silent)
				{
					//A silent packet carries no meaningful data at all, so its buffer contents are not read.
					span.Clear();
				}
				else if (captureFloat)
				{
					Marshal.Copy(data, captureBuffer, 0, samples);

					//A captured float sample may legitimately sit outside the range clip admission accepts, so
					//capture saturates it here instead of refusing the packet the device already recorded.
					for (var i = 0; i < samples; i++)
					{
						var value = captureBuffer[i];
						captureBuffer[i] = !float.IsFinite(value) ? 0f : value < -1f ? -1f : value > 1f ? 1f : value;
					}
				}
				else
				{
					var bytes = samples * bytesPerSample;

					if (packetBuffer == null || packetBuffer.Length < bytes)
						packetBuffer = new byte[bytes];

					Marshal.Copy(data, packetBuffer, 0, bytes);

					if (!AudioFormats.TryConvert(new ReadOnlySpan<byte>(packetBuffer, 0, bytes), packetFormat, span, out _))
						span.Clear();
				}

				try
				{
					sink.Write(span);
				}
				catch (Exception)
				{
					//A sink that faults loses its own packet; the stream stays open so the next one still arrives.
				}
			}

			/// <summary>
			/// Any HRESULT that ends the capture loop leaves the stream permanently silent, so the owner is told to
			/// transition whether the cause was the documented device-invalidated code or something rarer.
			/// </summary>
			private void MarkLost() => Volatile.Write(ref deviceLost, 1);

			/// <summary>
			/// Names the engine mix layout in the vocabulary <see cref="AudioFormats"/> converts from. A 24-bit
			/// sample in a 32-bit container reads as Signed32, whose unused low bits are already zero.
			/// </summary>
			private static bool TryClassifyMix(nint pFormat, in WaveFormatEx header, out string canonical,
											   out int bytesPerSample, out bool isFloat)
			{
				canonical = AudioFormats.Float32;
				bytesPerSample = 4;
				isFloat = MixIsFloat(pFormat, header);

				if (isFloat)
					return header.wBitsPerSample == 32;

				if (header.wFormatTag == WaveFormatExtensibleTag)
				{
					if (header.cbSize < ExtensibleExtraBytes
							|| Marshal.PtrToStructure<WaveFormatExtensible>(pFormat).SubFormat != SubtypePcm)
						return false;
				}
				else if (header.wFormatTag != WaveFormatPcmTag)
				{
					return false;
				}

				switch (header.wBitsPerSample)
				{
					case 8:
						canonical = AudioFormats.Unsigned8;
						bytesPerSample = 1;
						return true;

					case 16:
						canonical = AudioFormats.Signed16;
						bytesPerSample = 2;
						return true;

					case 24:
						canonical = AudioFormats.Signed24;
						bytesPerSample = 3;
						//Only the packed three-byte layout is this token; a padded one would misalign every frame.
						return header.nBlockAlign == header.nChannels * 3;

					case 32:
						canonical = AudioFormats.Signed32;
						bytesPerSample = 4;
						return true;

					default:
						return false;
				}
			}

			/// <summary>Capture is the one path where E_ACCESSDENIED has a specific, actionable cause.</summary>
			private static string DescribeOpen(string what, int hr)
				=> hr == EAccessDenied ? CapturePrivacyReason : FormatHr(what, hr);
		}

		/// <summary>
		/// One live native level observation. Every read is one bounded COM call under a gate that only disposal
		/// contends, so a read never blocks on anything but another read of the same meter.
		/// </summary>
		private sealed class WasapiMeter : IAudioNativeMeter
		{
			private readonly object gate = new ();
			private IAudioMeterInformation meter;

			internal WasapiMeter(IAudioMeterInformation meter) => this.meter = meter;

			/// <inheritdoc/>
			public double Peak
			{
				get
				{
					lock (gate)
					{
						if (meter == null)
							return -1.0;

						try
						{
							//A refused read is no completed observation, which stays distinct from a measured zero.
							if (meter.GetPeakValue(out var value) < 0 || !float.IsFinite(value))
								return -1.0;

							return Math.Clamp((double)value, 0.0, 1.0);
						}
						catch (Exception)
						{
							return -1.0;
						}
					}
				}
			}

			public void Dispose()
			{
				lock (gate)
				{
					//Releasing under the gate is what keeps a concurrent read from calling through a dead wrapper.
					ReleaseCom(meter);
					meter = null;
				}
			}
		}
	}
}
#endif
