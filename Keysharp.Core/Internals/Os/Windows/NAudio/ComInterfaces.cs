using Keysharp.Builtins;
#if WINDOWS
namespace Keysharp.Internals.Os.Windows
{
	// Every client-called method below declares an int return and MUST carry [PreserveSig].
	//
	// Without it the CLR treats the declared int as the [retval] out-parameter, so it calls
	// `HRESULT M(args..., int* retval)` - one argument more than the interface actually has - and
	// returns the contents of its own zero-filled retval local instead of the HRESULT. Two things
	// follow: every `if (x.M(...) < 0)` check in this codebase silently tests a constant 0 and can
	// never observe a failure, and a real failure surfaces as a thrown COMException from a place
	// that looks like it returns a status code. The extra argument is benign under the x64/ARM64
	// calling conventions this project ships, but it is not benign on __stdcall x86.
	//
	// Callback interfaces that Keysharp *implements* (IAudioEndpointVolumeCallback,
	// IMMNotificationClient) are the opposite case: a managed `void` method is the correct CCW
	// shape, because the runtime maps it to a native HRESULT return automatically.
	//
	// Interface member order is vtable order. It, the IIDs, and the signatures were verified
	// against the Windows SDK headers (devicetopology.h, mmdeviceapi.h, endpointvolume.h).
	[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioEndpointVolume
	{
		[PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
		[PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
		[PreserveSig] int GetChannelCount(out int pnChannelCount);
		[PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
		[PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
		[PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
		[PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
		[PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
		[PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
		[PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
		[PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
		[PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] Boolean bMute, ref Guid pguidEventContext);
		[PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
		[PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
		[PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
		[PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
		[PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
		[PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
	}

	[Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioEndpointVolumeCallback
	{
		void OnNotify(nint notifyData);
	};

	[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioMeterInformation
	{
		[PreserveSig] int GetPeakValue(out float pfPeak);
		[PreserveSig] int GetMeteringChannelCount(out int pnChannelCount);
		[PreserveSig] int GetChannelsPeakValues(int u32ChannelCount, [In] nint afPeakValues);
		[PreserveSig] int QueryHardwareSupport(out int pdwHardwareSupportMask);
	};

	///// <summary>
	///// Windows CoreAudio IAudioClient interface
	///// Defined in AudioClient.h
	///// </summary>
	//[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
	// InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	// ComImport]
	//internal interface IAudioClient
	//{
	//  [PreserveSig]
	//  int Initialize(AudioClientShareMode shareMode,
	//                 AudioClientStreamFlags streamFlags,
	//                 long hnsBufferDuration, // REFERENCE_TIME
	//                 long hnsPeriodicity, // REFERENCE_TIME
	//                 [In] WaveFormat pFormat,
	//                 [In] ref Guid audioSessionGuid);

	//  /// <summary>
	//  /// The GetBufferSize method retrieves the size (maximum capacity) of the endpoint buffer.
	//  /// </summary>
	//  int GetBufferSize(out uint bufferSize);

	//  [return: MarshalAs(UnmanagedType.I8)]
	//  long GetStreamLatency();

	//  int GetCurrentPadding(out int currentPadding);

	//  [PreserveSig]
	//  int IsFormatSupported(
	//      AudioClientShareMode shareMode,
	//      [In] WaveFormat pFormat,
	//      nint closestMatchFormat); // or outnint??

	//  int GetMixFormat(out nint deviceFormatPointer);

	//  // REFERENCE_TIME is 64 bit int
	//  int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

	//  int Start();

	//  int Stop();

	//  int Reset();

	//  int SetEventHandle(nint eventHandle);

	//  /// <summary>
	//  /// The GetService method accesses additional services from the audio client object.
	//  /// </summary>
	//  /// <param name="interfaceId">The interface ID for the requested service.</param>
	//  /// <param name="interfacePointer">Pointer to a pointer variable into which the method writes the address of an instance of the requested interface. </param>
	//  [PreserveSig]
	//  int GetService([In, MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, [Out, MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
	//}

	/// <summary>
	/// Windows CoreAudio IAudioSessionControl interface
	/// Defined in AudioPolicy.h
	/// </summary>
	[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionEvents
	{
		/// <summary>
		/// Notifies the client that the display name for the session has changed.
		/// </summary>
		/// <param name="displayName">The new display name for the session.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnDisplayNameChanged(
			[In][MarshalAs(UnmanagedType.LPWStr)] string displayName,
			[In] ref Guid eventContext);

		/// <summary>
		/// Notifies the client that the display icon for the session has changed.
		/// </summary>
		/// <param name="iconPath">The path for the new display icon for the session.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnIconPathChanged(
			[In][MarshalAs(UnmanagedType.LPWStr)] string iconPath,
			[In] ref Guid eventContext);

		/// <summary>
		/// Notifies the client that the volume level or muting state of the session has changed.
		/// </summary>
		/// <param name="volume">The new volume level for the audio session.</param>
		/// <param name="isMuted">The new muting state.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnSimpleVolumeChanged(
			[In][MarshalAs(UnmanagedType.R4)] float volume,
			[In][MarshalAs(UnmanagedType.Bool)] bool isMuted,
			[In] ref Guid eventContext);

		/// <summary>
		/// Notifies the client that the volume level of an audio channel in the session submix has changed.
		/// </summary>
		/// <param name="channelCount">The channel count.</param>
		/// <param name="newVolumes">An array of volumnes cooresponding with each channel index.</param>
		/// <param name="channelIndex">The number of the channel whose volume level changed.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnChannelVolumeChanged(
			[In][MarshalAs(UnmanagedType.U4)] UInt32 channelCount,
			[In][MarshalAs(UnmanagedType.SysInt)] nint newVolumes, // Pointer to float array
			[In][MarshalAs(UnmanagedType.U4)] UInt32 channelIndex,
			[In] ref Guid eventContext);

		/// <summary>
		/// Notifies the client that the grouping parameter for the session has changed.
		/// </summary>
		/// <param name="groupingId">The new grouping parameter for the session.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnGroupingParamChanged(
			[In] ref Guid groupingId,
			[In] ref Guid eventContext);

		/// <summary>
		/// Notifies the client that the stream-activity state of the session has changed.
		/// </summary>
		/// <param name="state">The new session state.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnStateChanged(
			[In] AudioSessionState state);

		/// <summary>
		/// Notifies the client that the session has been disconnected.
		/// </summary>
		/// <param name="disconnectReason">The reason that the audio session was disconnected.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int OnSessionDisconnected(
			[In] AudioSessionDisconnectReason disconnectReason);
	}

	/// <summary>
	/// Windows CoreAudio IAudioSessionControl interface
	/// Defined in AudioPolicy.h
	/// </summary>
	[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionControl
	{
		/// <summary>
		/// Retrieves the current state of the audio session.
		/// </summary>
		/// <param name="state">Receives the current session state.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetState(
			[Out] out AudioSessionState state);

		/// <summary>
		/// Retrieves the display name for the audio session.
		/// </summary>
		/// <param name="displayName">Receives a string that contains the display name.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetDisplayName(
			[Out][MarshalAs(UnmanagedType.LPWStr)] out string displayName);

		/// <summary>
		/// Assigns a display name to the current audio session.
		/// </summary>
		/// <param name="displayName">A string that contains the new display name for the session.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int SetDisplayName(
			[In][MarshalAs(UnmanagedType.LPWStr)] string displayName,
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		/// <summary>
		/// Retrieves the path for the display icon for the audio session.
		/// </summary>
		/// <param name="iconPath">Receives a string that specifies the fully qualified path of the file that contains the icon.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetIconPath(
			[Out][MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

		/// <summary>
		/// Assigns a display icon to the current session.
		/// </summary>
		/// <param name="iconPath">A string that specifies the fully qualified path of the file that contains the new icon.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int SetIconPath(
			[In][MarshalAs(UnmanagedType.LPWStr)] string iconPath,
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		/// <summary>
		/// Retrieves the grouping parameter of the audio session.
		/// </summary>
		/// <param name="groupingId">Receives the grouping parameter ID.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetGroupingParam(
			[Out] out Guid groupingId);

		/// <summary>
		/// Assigns a session to a grouping of sessions.
		/// </summary>
		/// <param name="groupingId">The new grouping parameter ID.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int SetGroupingParam(
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid groupingId,
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		/// <summary>
		/// Registers the client to receive notifications of session events, including changes in the session state.
		/// </summary>
		/// <param name="client">A client-implemented <see cref="IAudioSessionEvents"/> interface.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int RegisterAudioSessionNotification(
			[In] IAudioSessionEvents client);

		/// <summary>
		/// Deletes a previous registration by the client to receive notifications.
		/// </summary>
		/// <param name="client">A client-implemented <see cref="IAudioSessionEvents"/> interface.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int UnregisterAudioSessionNotification(
			[In] IAudioSessionEvents client);
	}

	/// <summary>
	/// Windows CoreAudio ISimpleAudioVolume interface
	/// Defined in AudioClient.h
	/// </summary>
	[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface ISimpleAudioVolume
	{
		/// <summary>
		/// Sets the master volume level for the audio session.
		/// </summary>
		/// <param name="levelNorm">The new volume level expressed as a normalized value between 0.0 and 1.0.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int SetMasterVolume(
			[In][MarshalAs(UnmanagedType.R4)] float levelNorm,
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		/// <summary>
		/// Retrieves the client volume level for the audio session.
		/// </summary>
		/// <param name="levelNorm">Receives the volume level expressed as a normalized value between 0.0 and 1.0. </param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetMasterVolume(
			[Out][MarshalAs(UnmanagedType.R4)] out float levelNorm);

		/// <summary>
		/// Sets the muting state for the audio session.
		/// </summary>
		/// <param name="isMuted">The new muting state.</param>
		/// <param name="eventContext">A user context value that is passed to the notification callback.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int SetMute(
			[In][MarshalAs(UnmanagedType.Bool)] bool isMuted,
			[In][MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);

		/// <summary>
		/// Retrieves the current muting state for the audio session.
		/// </summary>
		/// <param name="isMuted">Receives the muting state.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetMute(
			[Out][MarshalAs(UnmanagedType.Bool)] out bool isMuted);
	}

	/// <summary>
	/// Windows CoreAudio IAudioSessionManager interface
	/// Defined in AudioPolicy.h
	/// </summary>
	[Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioSessionManager
	{
		/// <summary>
		/// Retrieves an audio session control.
		/// </summary>
		/// <param name="sessionId">A new or existing session ID.</param>
		/// <param name="streamFlags">Audio session flags.</param>
		/// <param name="sessionControl">Receives an <see cref="IAudioSessionControl"/> interface for the audio session.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetAudioSessionControl(
			[In, Optional][MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
			[In][MarshalAs(UnmanagedType.U4)] UInt32 streamFlags,
			[Out][MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl sessionControl);

		/// <summary>
		/// Retrieves a simple audio volume control.
		/// </summary>
		/// <param name="sessionId">A new or existing session ID.</param>
		/// <param name="streamFlags">Audio session flags.</param>
		/// <param name="audioVolume">Receives an <see cref="ISimpleAudioVolume"/> interface for the audio session.</param>
		/// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
		[PreserveSig]
		int GetSimpleAudioVolume(
			[In, Optional][MarshalAs(UnmanagedType.LPStruct)] Guid sessionId,
			[In][MarshalAs(UnmanagedType.U4)] UInt32 streamFlags,
			[Out][MarshalAs(UnmanagedType.Interface)] out ISimpleAudioVolume audioVolume);
	}

	[Guid("D666063F-1587-4E43-81F1-B948E807363F"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMMDevice
	{
		// activationParams is a propvariant
		[PreserveSig]
		int Activate(ref Guid id, ClsCtx clsCtx, nint activationParams,
					 [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

		[PreserveSig]
		int OpenPropertyStore(StorageAccessMode stgmAccess, out IPropertyStore properties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

		[PreserveSig]
		int GetState(out DeviceState state);
	}

	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMMDeviceCollection
	{
		[PreserveSig] int GetCount(out int numDevices);
		[PreserveSig] int Item(int deviceNumber, out IMMDevice device);
	}

	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(DataFlow dataFlow, DeviceState stateMask,
							   out IMMDeviceCollection devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);

		[PreserveSig]
		int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice deviceName);

		[PreserveSig]
		int RegisterEndpointNotificationCallback(IMMNotificationClient client);

		[PreserveSig]
		int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
	}

	[Guid("82149A85-DBA6-4487-86BB-EA8F7FEFCC71"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface ISubunit
	{
		// Stub, Not Implemented
	}

	[Guid("45d37c3f-5140-444a-ae24-400789f3cbf3"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IControlInterface
	{
		[PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
		[PreserveSig] int GetIID(out Guid iid);
	}

	/// <summary>
	/// Windows CoreAudio IPartsList interface
	/// Defined in devicetopology.h
	/// </summary>
	[Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IPartsList
	{
		[PreserveSig] int GetCount(out uint count);
		[PreserveSig] int GetPart(uint index, out IPart part);
	}

	//IID_IControlChangeNotify per devicetopology.h. This previously carried IConnector's IID, so a
	//QueryInterface for it would have handed back an IConnector and called it through this vtable.
	[Guid("A09513ED-C709-4D21-BD7B-5F34C47F3947"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	interface IControlChangeNotify
	{
		[PreserveSig]
		int OnNotify(
			[In] uint controlId,
			[In] nint context);
	}

	/// <summary>
	/// Windows CoreAudio IPart interface
	/// Defined in devicetopology.h
	/// </summary>
	[Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IPart
	{
		[PreserveSig]
		int GetName(
			[Out, MarshalAs(UnmanagedType.LPWStr)] out string name);

		[PreserveSig]
		int GetLocalId(
			[Out] out uint id);

		[PreserveSig]
		int GetGlobalId(
			[Out, MarshalAs(UnmanagedType.LPWStr)] out string id);

		[PreserveSig]
		int GetPartType(
			[Out] out PartTypeEnum partType);

		[PreserveSig]
		int GetSubType(
			out Guid subType);

		[PreserveSig]
		int GetControlInterfaceCount(
			[Out] out uint count);

		[PreserveSig]
		int GetControlInterface(
			[In] uint index,
			[Out, MarshalAs(UnmanagedType.Interface)] out IControlInterface controlInterface);

		[PreserveSig]
		int EnumPartsIncoming(
			[Out] out IPartsList parts);

		[PreserveSig]
		int EnumPartsOutgoing(
			[Out] out IPartsList parts);

		[PreserveSig]
		int GetTopologyObject(
			[Out, MarshalAs(UnmanagedType.IUnknown)] out object topologyObject);

		[PreserveSig]
		int Activate(
			[In] ClsCtx dwClsContext,
			[In] ref Guid refiid,
			[MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

		[PreserveSig]
		int RegisterControlChangeCallback(
			[In] ref Guid refiid,
			[In] IControlChangeNotify notify);

		[PreserveSig]
		int UnregisterControlChangeCallback(
			[In] IControlChangeNotify notify);
	}

	/// <summary>
	/// Windows CoreAudio IDeviceTopology interface
	/// Defined in devicetopology.h
	/// </summary>
	[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IDeviceTopology
	{
		[PreserveSig] int GetConnectorCount(out uint count);
		[PreserveSig] int GetConnector(uint index, out IConnector connector);
		[PreserveSig] int GetSubunitCount(out uint count);
		[PreserveSig] int GetSubunit(uint index, out ISubunit subunit);
		[PreserveSig] int GetPartById(uint id, out IPart part);
		[PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
		[PreserveSig] int GetSignalPath(IPart from, IPart to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IPartsList parts);
	}

	/// <summary>
	/// Windows CoreAudio IConnector interface
	/// Defined in devicetopology.h
	/// </summary>
	[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IConnector
	{
		[PreserveSig] int GetType(out ConnectorType type);
		[PreserveSig] int GetDataFlow(out DataFlow flow);
		[PreserveSig] int ConnectTo([In] IConnector connectTo);
		[PreserveSig] int Disconnect();
		[PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
		[PreserveSig] int GetConnectedTo(out IConnector conTo);
		[PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
		[PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
	}

	/// <summary>
	/// IMMNotificationClient
	/// </summary>
	[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMMNotificationClient
	{
		/// <summary>
		/// Device State Changed
		/// </summary>
		void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.I4)] DeviceState newState);

		/// <summary>
		/// Device Added
		/// </summary>
		void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

		/// <summary>
		/// Device Removed
		/// </summary>
		void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

		/// <summary>
		/// Default Device Changed
		/// </summary>
		void OnDefaultDeviceChanged(DataFlow flow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);

		/// <summary>
		/// Property Value Changed
		/// </summary>
		/// <param name="pwstrDeviceId"></param>
		/// <param name="key"></param>
		void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
	}

	/// <summary>
	/// is defined in propsys.h
	/// </summary>
	[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IPropertyStore
	{
		[PreserveSig] int GetCount(out int propCount);
		[PreserveSig] int GetAt(int property, out PropertyKey key);
		[PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
		[PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
		[PreserveSig] int Commit();
	}

	[Guid("DF45AEEA-B74A-4B6B-AFAD-2366B6AA012E"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioMute
	{
		//Declaration order is the vtable order: devicetopology.h puts SetMute before GetMute.
		//Swapping them made GetMute call native SetMute (so the out value was never written and
		//mute always read as 0) and SetMute call native GetMute, which wrote through the BOOL
		//value as if it were a pointer - hence E_POINTER / ERROR_NOACCESS from address 0 and 1.
		[PreserveSig]
		int SetMute(
			[In, MarshalAs(UnmanagedType.Bool)] bool mute,
			[In] ref Guid eventContext);

		[PreserveSig]
		int GetMute(
			[Out, MarshalAs(UnmanagedType.Bool)] out bool mute);
	}

	//IID_IPerChannelDbLevel per devicetopology.h. This previously carried IAudioVolumeLevel's IID
	//(the interface derived from it), so a direct QueryInterface for it asked for the wrong type.
	[Guid("C2F8E001-F205-4BC9-99BC-C13B1E048CCB"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IPerChannelDbLevel
	{
		[PreserveSig] int GetChannelCount(out uint channels);
		[PreserveSig] int GetLevelRange(uint channel, out float minLevelDb, out float maxLevelDb, out float stepping);
		[PreserveSig] int GetLevel(uint channel, out float levelDb);
		[PreserveSig] int SetLevel(uint channel, float levelDb, ref Guid eventGuidContext);
		[PreserveSig] int SetLevelUniform(float levelDb, ref Guid eventGuidContext);
		//LPArray is required: array parameters on a COM interface default to SafeArray, which the
		//native SetLevelAllChannels reads as a float* and interprets the SAFEARRAY header as levels.
		[PreserveSig] int SetLevelAllChannel([MarshalAs(UnmanagedType.LPArray)] float[] levelsDb, uint channels, ref Guid eventGuidContext);
	}

	[Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IAudioVolumeLevel : IPerChannelDbLevel
	{

	}

	/// <summary>
	/// implements IMMDeviceEnumerator
	/// </summary>
	[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	internal class MMDeviceEnumeratorComObject
	{
	}

	/// <summary>
	/// defined in MMDeviceAPI.h
	/// </summary>
	[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"),
	 InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
	 ComImport]
	internal interface IMMEndpoint
	{
		[PreserveSig] int GetDataFlow(out DataFlow dataFlow);
	}
}
#endif