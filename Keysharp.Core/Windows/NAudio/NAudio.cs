#if WINDOWS
/// <summary>
/// This code was taken from a project named NAudio which is located at https://github.com/naudio/NAudio
/// It consists of only the parts needed to make certain Keysharp sound functions work
/// The rest is omitted to keep the size of Keysharp as small as possible.
///
/// LICENSE
/// -------
/// Copyright(C) 2007 Ray Molenkamp
///
/// This source code is provided 'as-is', without any express or implied
/// warranty.In no event will the authors be held liable for any damages
/// arising from the use of this source code or the software it produces.
///
/// Permission is granted to anyone to use this source code for any purpose,
/// including commercial applications, and to alter it and redistribute it
/// freely, subject to the following restrictions:
///
/// 1. The origin of this source code must not be misrepresented; you must not
/// claim that you wrote the original source code.If you use this source code
///
///  in a product, an acknowledgment in the product documentation would be
///  appreciated but is not required.
/// 2. Altered source versions must be plainly marked as such, and must not be
///
///  misrepresented as being the original source code.
/// 3. This notice may not be removed or altered from any source distribution.
/// </summary>
namespace Keysharp.Core.Windows
{
	//You can never use the code rearranger on this file because many of the types
	//in it are for COM, and the order of declarations must match exactly.

	internal struct AudioVolumeNotificationDataStruct
	{
		public AudioVolumeNotificationDataStruct() { }
		internal Guid guidEventContext = default;//Need these to suppress warnings about initial values.
		internal bool bMuted = default;
		internal float fMasterVolume = default;
		internal uint nChannels = default;
		internal float ChannelVolume = default;
	}

	/// <summary>
	/// Representation of binary large object container.
	/// </summary>
	internal struct Blob
	{
		/// <summary>
		/// Length of binary object.
		/// </summary>
		internal int Length;
		/// <summary>
		/// Pointer to buffer storing data.
		/// </summary>
		internal nint Data;
	}

	/// <summary>
	/// PROPERTYKEY is defined in wtypes.h
	/// </summary>
	internal struct PropertyKey
	{
		/// <summary>
		/// Format ID
		/// </summary>
		internal Guid formatId;
		/// <summary>
		/// Property ID
		/// </summary>
		internal int propertyId;
		/// <summary>
		/// <param name="formatId"></param>
		/// <param name="propertyId"></param>
		/// </summary>
		internal PropertyKey(Guid formatId, int propertyId)
		{
			this.formatId = formatId;
			this.propertyId = propertyId;
		}
	}

	// <summary>
	/// from Propidl.h.
	/// http://msdn.microsoft.com/en-us/library/aa380072(VS.85).aspx
	/// contains a union so we have to do an explicit layout
	/// </summary>
	[StructLayout(LayoutKind.Explicit)]
	internal struct PropVariant
	{
		/// <summary>
		/// Value type tag.
		/// </summary>
		[FieldOffset(0)] internal short vt;

		/// <summary>
		/// Reserved1.
		/// </summary>
		[FieldOffset(2)] internal short wReserved1;

		/// <summary>
		/// Reserved2.
		/// </summary>
		[FieldOffset(4)] internal short wReserved2;

		/// <summary>
		/// Reserved3.
		/// </summary>
		[FieldOffset(6)] internal short wReserved3;

		/// <summary>
		/// cVal.
		/// </summary>
		[FieldOffset(8)] internal sbyte cVal;

		/// <summary>
		/// bVal.
		/// </summary>
		[FieldOffset(8)] internal byte bVal;

		/// <summary>
		/// iVal.
		/// </summary>
		[FieldOffset(8)] internal short iVal;

		/// <summary>
		/// uiVal.
		/// </summary>
		[FieldOffset(8)] internal ushort uiVal;

		/// <summary>
		/// lVal.
		/// </summary>
		[FieldOffset(8)] internal int lVal;

		/// <summary>
		/// ulVal.
		/// </summary>
		[FieldOffset(8)] internal uint ulVal;

		/// <summary>
		/// intVal.
		/// </summary>
		[FieldOffset(8)] internal int intVal;

		/// <summary>
		/// uintVal.
		/// </summary>
		[FieldOffset(8)] internal uint uintVal;

		/// <summary>
		/// hVal.
		/// </summary>
		[FieldOffset(8)] internal long hVal;

		/// <summary>
		/// uhVal.
		/// </summary>
		[FieldOffset(8)] internal long uhVal;

		/// <summary>
		/// fltVal.
		/// </summary>
		[FieldOffset(8)] internal float fltVal;

		/// <summary>
		/// dblVal.
		/// </summary>
		[FieldOffset(8)] internal double dblVal;

		//VARIANT_BOOL boolVal;
		/// <summary>
		/// boolVal.
		/// </summary>
		[FieldOffset(8)] internal short boolVal;

		/// <summary>
		/// scode.
		/// </summary>
		[FieldOffset(8)] internal int scode;

		//CY cyVal;
		//[FieldOffset(8)] private DateTime date; - can cause issues with invalid value
		/// <summary>
		/// Date time.
		/// </summary>
		[FieldOffset(8)] internal System.Runtime.InteropServices.ComTypes.FILETIME filetime;

		//CLSID* puuid;
		//CLIPDATA* pclipdata;
		//BSTR bstrVal;
		//BSTRBLOB bstrblobVal;
		/// <summary>
		/// Binary large object.
		/// </summary>
		[FieldOffset(8)] internal Blob blobVal;

		//LPSTR pszVal;
		/// <summary>
		/// Pointer value.
		/// </summary>
		[FieldOffset(8)] internal nint pointerValue; //LPWSTR

		//IUnknown* punkVal;
		/*  IDispatch* ptrVal;
		    IStream* pStream;
		    IStorage* pStorage;
		    LPVERSIONEDSTREAM pVersionedStream;
		    LPSAFEARRAY parray;
		    CAC cac;
		    CAUB caub;
		    CAI cai;
		    CAUI caui;
		    CAL cal;
		    CAUL caul;
		    CAH cah;
		    CAUH cauh;
		    CAFLT caflt;
		    CADBL cadbl;
		    CABOOL cabool;
		    CASCODE cascode;
		    CACY cacy;
		    CADATE cadate;
		    CAFILETIME cafiletime;
		    CACLSID cauuid;
		    CACLIPDATA caclipdata;
		    CABSTR cabstr;
		    CABSTRBLOB cabstrblob;
		    CALPSTR calpstr;
		    CALPWSTR calpwstr;
		    CAPROPVARIANT capropvar;
		    CHAR* pcVal;
		    UCHAR* pbVal;
		    SHORT* piVal;
		    USHORT* puiVal;
		    LONG* plVal;
		    ULONG* pulVal;
		    INT* pintVal;
		    UINT* puintVal;
		    FLOAT* pfltVal;
		    DOUBLE* pdblVal;
		    VARIANT_BOOL* pboolVal;
		    DECIMAL* pdecVal;
		    SCODE* pscode;
		    CY* pcyVal;
		    DATE* pdate;
		    BSTR* pbstrVal;
		    IUnknown** ppunkVal;
		    IDispatch** pptrVal;
		    LPSAFEARRAY* ptrVal;
		    PROPVARIANT* ptrVal;
		*/

		/// <summary>
		/// Creates a new PropVariant containing a long value
		/// </summary>
		internal static PropVariant FromLong(long value) => new () { vt = (short)VarEnum.VT_I8, hVal = value };

		/// <summary>
		/// Helper method to gets blob data
		/// </summary>
		private byte[] GetBlob()
		{
			var blob = new byte[blobVal.Length];
			Marshal.Copy(blobVal.Data, blob, 0, blob.Length);
			return blob;
		}

		/// <summary>
		/// Interprets a blob as an array of structs
		/// </summary>
		internal T[] GetBlobAsArrayOf<T>()
		{
			var blobByteLength = blobVal.Length;
			var singleInstance = (T)Activator.CreateInstance(typeof(T));
			var structSize = Marshal.SizeOf(singleInstance);

			if (blobByteLength % structSize != 0)
			{
				_ = Errors.ErrorOccurred($"Blob size {blobByteLength} not a multiple of struct size {structSize}.");
				return default;
			}

			var items = blobByteLength / structSize;
			var array = new T[items];

			for (var n = 0; n < items; n++)
			{
				array[n] = (T)Activator.CreateInstance(typeof(T));
				Marshal.PtrToStructure(new nint((long)blobVal.Data + n * structSize), array[n]);
			}

			return array;
		}

		/// <summary>
		/// Gets the type of data in this PropVariant
		/// </summary>
		internal VarEnum DataType => (VarEnum)vt;

		/// <summary>
		/// Property value
		/// </summary>
		internal object Value
		{
			get
			{
				var ve = DataType;

				return ve switch
			{
					VarEnum.VT_I1 => bVal,
					VarEnum.VT_I2 => iVal,
					VarEnum.VT_I4 => lVal,
					VarEnum.VT_I8 => hVal,
					VarEnum.VT_INT => iVal,
					VarEnum.VT_UI4 => ulVal,
					VarEnum.VT_UI8 => uhVal,
					VarEnum.VT_LPWSTR => Marshal.PtrToStringUni(pointerValue),
						VarEnum.VT_BLOB or VarEnum.VT_VECTOR | VarEnum.VT_UI1 => GetBlob(),
						VarEnum.VT_CLSID => Marshal.PtrToStructure<Guid>(pointerValue),

						VarEnum.VT_BOOL => boolVal switch
					{
							-1 => true,
							0 => false,
							_ => _ = Errors.ErrorOccurred("PropVariant VT_BOOL must be either -1 or 0"),
						},
						VarEnum.VT_FILETIME => DateTime.FromFileTime((((long)filetime.dwHighDateTime) << 32) + filetime.dwLowDateTime),
						_ => _ = Errors.ErrorOccurred("PropVariant " + ve),
				};
			}
		}

		/// <summary>
		/// allows freeing up memory, might turn this into a Dispose method?
		/// </summary>
		[Obsolete("Call with pointer instead")]
		internal void Clear() => PropVariantNative.PropVariantClear(ref this);

		/// <summary>
		/// Clears with a known pointer
		/// </summary>
		internal static void Clear(nint ptr) => PropVariantNative.PropVariantClear(ptr);
	}

	/// <summary>
	/// Property Keys
	/// </summary>
	internal static class PropertyKeys
	{
		/// <summary>
		/// PKEY_AudioEndpoint_Association
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_Association = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 2);

		/// <summary>
		/// PKEY_AudioEndpoint_ControlPanelPageProvider
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_ControlPanelPageProvider = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 1);

		/// <summary>
		/// PKEY_AudioEndpoint_Disable_SysFx
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_Disable_SysFx = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 5);

		/// <summary>
		/// PKEY_AudioEndpoint_FormFactor
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_FormFactor = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 0);

		/// <summary>
		/// PKEY_AudioEndpoint_FullRangeSpeakers
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_FullRangeSpeakers = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 6);

		/// <summary>
		/// PKEY_AudioEndpoint_GUID
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_GUID = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 4);

		/// <summary>
		/// PKEY_AudioEndpoint_JackSubType
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_JackSubType = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 8);

		/// <summary>
		/// PKEY_AudioEndpoint_PhysicalSpeakers
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_PhysicalSpeakers = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 3);

		/// <summary>
		/// PKEY_AudioEndpoint_Supports_EventDriven_Mode
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEndpoint_Supports_EventDriven_Mode = new (new Guid(0x1da5d803, unchecked((short)0xd492), 0x4edd, 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e), 7);

		/// <summary>
		/// PKEY_AudioEngine_DeviceFormat
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEngine_DeviceFormat = new (new Guid(unchecked((int)0xf19f064d), 0x82c, 0x4e27, 0xbc, 0x73, 0x68, 0x82, 0xa1, 0xbb, 0x8e, 0x4c), 0);

		/// <summary>
		/// PKEY_AudioEngine_OEMFormat
		/// </summary>
		internal static readonly PropertyKey PKEY_AudioEngine_OEMFormat = new (new Guid(unchecked((int)0xe4870e26), 0x3cc5, 0x4cd2, 0xba, 0x46, 0xca, 0xa, 0x9a, 0x70, 0xed, 0x4), 3);

		/// <summary>
		/// Id of controller device for endpoint device property.
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_ControllerDeviceId = new (new Guid(unchecked((int)0xb3f8fa53), unchecked(0x0004), 0x438e, 0x90, 0x03, 0x51, 0xa4, 0x6e, 0x13, 0x9b, 0xfc), 2);

		/// <summary>
		/// Device description property.
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_DeviceDesc = new (new Guid(unchecked((int)0xa45c254e), unchecked((short)0xdf1c), 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0), 2);

		/// <summary>
		/// PKEY _Devie_FriendlyName
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_FriendlyName = new (new Guid(unchecked((int)0xa45c254e), unchecked((short)0xdf1c), 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0), 14);

		/// <summary>
		/// PKEY _Device_IconPath
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_IconPath = new (new Guid(unchecked(0x259abffc), unchecked(0x50a7), 0x47ce, 0xaf, 0x8, 0x68, 0xc9, 0xa7, 0xd7, 0x33, 0x66), 12);

		/// <summary>
		/// System-supplied device instance identification string, assigned by PnP manager, persistent across system restarts.
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_InstanceId = new (new Guid(0x78c34fc8, 0x104a, 0x4aca, 0x9e, 0xa4, 0x52, 0x4d, 0x52, 0x99, 0x6e, 0x57), 256);

		/// <summary>
		/// Device interface key property.
		/// </summary>
		internal static readonly PropertyKey PKEY_Device_InterfaceKey = new (new Guid(unchecked(0x233164c8), unchecked(0x1b2c), 0x4c7d, 0xbc, 0x68, 0xb6, 0x71, 0x68, 0x7a, 0x25, 0x67), 1);

		/// <summary>
		/// PKEY_DeviceInterface_FriendlyName
		/// </summary>
		internal static readonly PropertyKey PKEY_DeviceInterface_FriendlyName = new (new Guid(0x026e516e, unchecked((short)0xb814), 0x414b, 0x83, 0xcd, 0x85, 0x6d, 0x6f, 0xef, 0x48, 0x22), 2);
	}

	/// <summary>
	/// Audio Endpoint Volume
	/// </summary>
	internal class AudioEndpointVolume : IDisposable
	{
		private readonly IAudioEndpointVolume audioEndPointVolume;
		private AudioEndpointVolumeCallback callBack;

		private Guid notificationGuid = Guid.Empty;

		/// <summary>
		/// Channels
		/// </summary>
		internal AudioEndpointVolumeChannels Channels { get; }

		/// <summary>
		/// Hardware Support
		/// </summary>
		internal EEndpointHardwareSupport HardwareSupport { get; }

		/// <summary>
		/// Master Volume Level
		/// </summary>
		internal float MasterVolumeLevel
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndPointVolume.GetMasterVolumeLevel(out var result));
				return result;
			}

			set => Marshal.ThrowExceptionForHR(audioEndPointVolume.SetMasterVolumeLevel(value, ref notificationGuid));
		}

		/// <summary>
		/// Master Volume Level Scalar
		/// </summary>
		internal float MasterVolumeLevelScalar
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndPointVolume.GetMasterVolumeLevelScalar(out var result));
				return result;
			}

			set => Marshal.ThrowExceptionForHR(audioEndPointVolume.SetMasterVolumeLevelScalar(value, ref notificationGuid));
		}

		/// <summary>
		/// Mute
		/// </summary>
		internal bool Mute
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndPointVolume.GetMute(out var result));
				return result;
			}

			set => Marshal.ThrowExceptionForHR(audioEndPointVolume.SetMute(value, ref notificationGuid));
		}

		/// <summary>
		/// GUID to pass to AudioEndpointVolumeCallback
		/// </summary>
		internal Guid NotificationGuid
		{
			get => notificationGuid;
			set => notificationGuid = value;
		}

		/// <summary>
		/// Step Information
		/// </summary>
		internal AudioEndpointVolumeStepInformation StepInformation { get; }

		/// <summary>
		/// Volume Range
		/// </summary>
		internal AudioEndpointVolumeVolumeRange VolumeRange { get; }

		/// <summary>
		/// Creates a new Audio endpoint volume
		/// </summary>
		/// <param name="realEndpointVolume">IAudioEndpointVolume COM interface</param>
		internal AudioEndpointVolume(IAudioEndpointVolume realEndpointVolume)
		{
			audioEndPointVolume = realEndpointVolume;
			Channels = new AudioEndpointVolumeChannels(audioEndPointVolume);
			StepInformation = new AudioEndpointVolumeStepInformation(audioEndPointVolume);
			Marshal.ThrowExceptionForHR(audioEndPointVolume.QueryHardwareSupport(out var hardwareSupp));
			HardwareSupport = (EEndpointHardwareSupport)hardwareSupp;
			VolumeRange = new AudioEndpointVolumeVolumeRange(audioEndPointVolume);
			callBack = new AudioEndpointVolumeCallback(this);
			Marshal.ThrowExceptionForHR(audioEndPointVolume.RegisterControlChangeNotify(callBack));
		}

		/// <summary>
		/// Finalizer
		/// </summary>
		~AudioEndpointVolume()
		{
			Dispose();
		}

		/// <summary>
		/// Dispose
		/// </summary>
		public void Dispose()
		{
			if (callBack != null)
			{
				Marshal.ThrowExceptionForHR(audioEndPointVolume.UnregisterControlChangeNotify(callBack));
				callBack = null;
			}

			_ = Marshal.ReleaseComObject(audioEndPointVolume);
			GC.SuppressFinalize(this);
		}

		internal void FireNotification(AudioVolumeNotificationData notificationData) => OnVolumeNotification?.Invoke(notificationData);

		/// <summary>
		/// Volume Step Down
		/// </summary>
		internal void VolumeStepDown() => Marshal.ThrowExceptionForHR(audioEndPointVolume.VolumeStepDown(ref notificationGuid));

		/// <summary>
		/// Volume Step Up
		/// </summary>
		internal void VolumeStepUp() => Marshal.ThrowExceptionForHR(audioEndPointVolume.VolumeStepUp(ref notificationGuid));

		/// <summary>
		/// On Volume Notification
		/// </summary>
		internal event AudioEndpointVolumeNotificationDelegate OnVolumeNotification;
	}

	// This class implements the IAudioEndpointVolumeCallback interface,
	// it is implemented in this class because implementing it on AudioEndpointVolume
	// (where the functionality is really wanted, would cause the OnNotify function
	// to show up in the internal API.
	internal class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
	{
		private readonly AudioEndpointVolume parent;

		internal AudioEndpointVolumeCallback(AudioEndpointVolume parent) => this.parent = parent;

		public void OnNotify(nint notifyData)
		{
			//Since AUDIO_VOLUME_NOTIFICATION_DATA is dynamic in length based on the
			//number of audio channels available we cannot just call PtrToStructure
			//to get all data, thats why it is split up into two steps, first the static
			//data is marshalled into the data structure, then with some nint math the
			//remaining floats are read from memory.
			//
			var data = Marshal.PtrToStructure<AudioVolumeNotificationDataStruct>(notifyData);
			//Determine offset in structure of the first float
			var offset = Marshal.OffsetOf<AudioVolumeNotificationDataStruct>("ChannelVolume");
			//Determine offset in memory of the first float
			var firstFloatPtr = (nint)(notifyData + (long)offset);
			var voldata = new float[data.nChannels];

			//Read all floats from memory.
			for (var i = 0; i < data.nChannels; i++)
			{
				voldata[i] = Marshal.PtrToStructure<float>(firstFloatPtr);
			}

			//Create combined structure and Fire Event in parent class.
			var notificationData = new AudioVolumeNotificationData(data.guidEventContext, data.bMuted, data.fMasterVolume, voldata, data.guidEventContext);
			parent.FireNotification(notificationData);
		}
	}

	/// <summary>
	/// Audio Endpoint Volume Channel
	/// </summary>
	internal class AudioEndpointVolumeChannel
	{
		private readonly uint channel;
		private readonly IAudioEndpointVolume audioEndpointVolume;

		private Guid notificationGuid = Guid.Empty;

		/// <summary>
		/// GUID to pass to AudioEndpointVolumeCallback
		/// </summary>
		internal Guid NotificationGuid
		{
			get => notificationGuid;
			set => notificationGuid = value;
		}

		internal AudioEndpointVolumeChannel(IAudioEndpointVolume parent, int channel)
		{
			this.channel = (uint)channel;
			audioEndpointVolume = parent;
		}

		/// <summary>
		/// Volume Level
		/// </summary>
		internal float VolumeLevel
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndpointVolume.GetChannelVolumeLevel(channel, out var result));
				return result;
			}
			set
			{
				Marshal.ThrowExceptionForHR(audioEndpointVolume.SetChannelVolumeLevel(channel, value, ref notificationGuid));
			}
		}

		/// <summary>
		/// Volume Level Scalar
		/// </summary>
		internal float VolumeLevelScalar
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndpointVolume.GetChannelVolumeLevelScalar(channel, out var result));
				return result;
			}
			set
			{
				Marshal.ThrowExceptionForHR(audioEndpointVolume.SetChannelVolumeLevelScalar(channel, value, ref notificationGuid));
			}
		}

	}

	/// <summary>
	/// Audio Endpoint Volume Channels
	/// </summary>
	internal class AudioEndpointVolumeChannels
	{
		readonly IAudioEndpointVolume audioEndPointVolume;
		readonly AudioEndpointVolumeChannel[] channels;

		/// <summary>
		/// Channel Count
		/// </summary>
		internal int Count
		{
			get
			{
				Marshal.ThrowExceptionForHR(audioEndPointVolume.GetChannelCount(out var result));
				return result;
			}
		}

		/// <summary>
		/// Indexer - get a specific channel
		/// </summary>
		internal AudioEndpointVolumeChannel this[int index] => channels[index];

		internal AudioEndpointVolumeChannels(IAudioEndpointVolume parent)
		{
			audioEndPointVolume = parent;
			var channelCount = Count;
			channels = new AudioEndpointVolumeChannel[channelCount];

			for (int i = 0; i < channelCount; i++)
			{
				channels[i] = new AudioEndpointVolumeChannel(audioEndPointVolume, i);
			}
		}
	}

	/// <summary>
	/// Audio Endpoint Volume Step Information
	/// </summary>
	internal class AudioEndpointVolumeStepInformation
	{
		private readonly uint step;
		private readonly uint stepCount;

		internal AudioEndpointVolumeStepInformation(IAudioEndpointVolume parent)
		{
			Marshal.ThrowExceptionForHR(parent.GetVolumeStepInfo(out step, out stepCount));
		}

		/// <summary>
		/// Step
		/// </summary>
		internal uint Step => step;

		/// <summary>
		/// StepCount
		/// </summary>
		internal uint StepCount => stepCount;
	}

	/// <summary>
	/// Audio Endpoint Volume Volume Range
	/// </summary>
	internal class AudioEndpointVolumeVolumeRange
	{
		readonly float volumeMinDecibels;
		readonly float volumeMaxDecibels;
		readonly float volumeIncrementDecibels;

		internal AudioEndpointVolumeVolumeRange(IAudioEndpointVolume parent)
		{
			Marshal.ThrowExceptionForHR(parent.GetVolumeRange(out volumeMinDecibels, out volumeMaxDecibels, out volumeIncrementDecibels));
		}

		/// <summary>
		/// Minimum Decibels
		/// </summary>
		internal float MinDecibels => volumeMinDecibels;

		/// <summary>
		/// Maximum Decibels
		/// </summary>
		internal float MaxDecibels => volumeMaxDecibels;

		/// <summary>
		/// Increment Decibels
		/// </summary>
		internal float IncrementDecibels => volumeIncrementDecibels;
	}

	/// <summary>
	/// Audio Volume Notification Data
	/// </summary>
	internal class AudioVolumeNotificationData
	{
		/// <summary>
		/// Event Context
		/// </summary>
		internal Guid EventContext { get; }

		/// <summary>
		/// Muted
		/// </summary>
		internal bool Muted { get; }

		/// <summary>
		/// Guid that raised the event
		/// </summary>
		internal Guid Guid { get; }

		/// <summary>
		/// Master Volume
		/// </summary>
		internal float MasterVolume { get; }

		/// <summary>
		/// Channels
		/// </summary>
		internal int Channels { get; }

		/// <summary>
		/// Channel Volume
		/// </summary>
		internal float[] ChannelVolume { get; }

		/// <summary>
		/// Audio Volume Notification Data
		/// </summary>
		/// <param name="eventContext"></param>
		/// <param name="muted"></param>
		/// <param name="masterVolume"></param>
		/// <param name="channelVolume"></param>
		/// <param name="guid"></param>
		internal AudioVolumeNotificationData(Guid eventContext, bool muted, float masterVolume, float[] channelVolume, Guid guid)
		{
			EventContext = eventContext;
			Muted = muted;
			MasterVolume = masterVolume;
			Channels = channelVolume.Length;
			ChannelVolume = channelVolume;
			Guid = guid;
		}
	}

	/// <summary>
	/// MM Device
	/// </summary>
	internal class MMDevice : IDisposable
	{
		#region Variables
		internal readonly IMMDevice deviceInterface;
		private PropertyStore propertyStore;
		private IAudioMeterInformation audioMeterInformation;
		private AudioEndpointVolume audioEndpointVolume;
		private IAudioSessionManager audioSessionManager;
		private IDeviceTopology deviceTopology;
		#endregion

		#region Guids
		internal static Guid IID_IAudioMeterInformation = new ("C02216F6-8C67-4B5B-9D00-D008E73E0064");
		internal static Guid IID_IAudioEndpointVolume = new ("5CDF2C82-841E-4546-9722-0CF74078229A");
		internal static Guid IID_IAudioClient = new ("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
		internal static Guid IDD_IAudioSessionManager = new ("BFA971F1-4D5E-40BB-935E-967039BFBEE4");
		internal static Guid IDD_IDeviceTopology = new ("2A07407E-6497-4A18-9787-32F79BD0D98F");

		#endregion

		#region Init
		/// <summary>
		/// Initializes the device's property store.
		/// </summary>
		/// <param name="stgmAccess">The storage-access mode to open store for.</param>
		/// <remarks>Administrative client is required for Write and ReadWrite modes.</remarks>
		internal void GetPropertyInformation(StorageAccessMode stgmAccess = StorageAccessMode.Read)
		{
			Marshal.ThrowExceptionForHR(deviceInterface.OpenPropertyStore(stgmAccess, out var propstore));
			propertyStore = new PropertyStore(propstore);
		}

		//internal IAudioClient GetAudioClient()
		//{
		//  Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IID_IAudioClient, ClsCtx.ALL, 0, out var result));
		//  //return new AudioClient(result as IAudioClient);
		//  return result as IAudioClient;
		//}

		private void GetAudioMeterInformation()
		{
			Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IID_IAudioMeterInformation, ClsCtx.ALL, 0, out var result));
			//audioMeterInformation = new AudioMeterInformation(result as IAudioMeterInformation);
			audioMeterInformation = result as IAudioMeterInformation;
		}

		private void GetAudioEndpointVolume()
		{
			Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IID_IAudioEndpointVolume, ClsCtx.ALL, 0, out var result));
			audioEndpointVolume = new AudioEndpointVolume(result as IAudioEndpointVolume);
		}

		private void GetAudioSessionManager()
		{
			Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IDD_IAudioSessionManager, ClsCtx.ALL, 0, out var result));
			audioSessionManager = result as IAudioSessionManager;
		}

		private void GetDeviceTopology()
		{
			Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IDD_IDeviceTopology, ClsCtx.ALL, 0, out var result));
			//deviceTopology = new DeviceTopology(result as IDeviceTopology);
			deviceTopology = result as IDeviceTopology;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Audio Client
		/// Makes a new one each call to allow caller to manage when to dispose
		/// n.b. should probably not be a property anymore
		/// </summary>
		//internal AudioClient AudioClient => GetAudioClient();

		/// <summary>
		/// Audio Meter Information
		/// </summary>
		internal IAudioMeterInformation AudioMeterInformation
		{
			get
			{
				if (audioMeterInformation == null)
					GetAudioMeterInformation();

				return audioMeterInformation;
			}
		}

		/// <summary>
		/// Audio Endpoint Volume
		/// </summary>
		internal AudioEndpointVolume AudioEndpointVolume
		{
			get
			{
				if (audioEndpointVolume == null)
					GetAudioEndpointVolume();

				return audioEndpointVolume;
			}
		}

		/// <summary>
		/// AudioSessionManager instance
		/// </summary>
		internal IAudioSessionManager AudioSessionManager
		{
			get
			{
				if (audioSessionManager == null)
					GetAudioSessionManager();

				return audioSessionManager;
			}
		}

		/// <summary>
		/// DeviceTopology instance
		/// </summary>
		internal IDeviceTopology DeviceTopology
		{
			get
			{
				if (deviceTopology == null)
					GetDeviceTopology();

				return deviceTopology;
			}
		}

		/// <summary>
		/// Properties
		/// </summary>
		internal PropertyStore Properties
		{
			get
			{
				if (propertyStore == null)
					GetPropertyInformation();

				return propertyStore;
			}
		}

		/// <summary>
		/// Friendly name for the endpoint
		/// </summary>
		internal string FriendlyName
		{
			get
			{
				if (propertyStore == null)
					GetPropertyInformation();

				if (propertyStore.Contains(PropertyKeys.PKEY_Device_FriendlyName))
					return (string)propertyStore[PropertyKeys.PKEY_Device_FriendlyName].Value;
				else
					return "Unknown";
			}
		}

		/// <summary>
		/// Friendly name of device
		/// </summary>
		internal string DeviceFriendlyName
		{
			get
			{
				if (propertyStore == null)
					GetPropertyInformation();

				if (propertyStore.Contains(PropertyKeys.PKEY_DeviceInterface_FriendlyName))
					return (string)propertyStore[PropertyKeys.PKEY_DeviceInterface_FriendlyName].Value;
				else
					return "Unknown";
			}
		}

		/// <summary>
		/// Icon path of device
		/// </summary>
		internal string IconPath
		{
			get
			{
				if (propertyStore == null)
					GetPropertyInformation();

				if (propertyStore.Contains(PropertyKeys.PKEY_Device_IconPath))
					return (string)propertyStore[PropertyKeys.PKEY_Device_IconPath].Value;

				return "Unknown";
			}
		}

		/// <summary>
		/// Device Instance Id of Device
		/// </summary>
		internal string InstanceId
		{
			get
			{
				if (propertyStore == null)
					GetPropertyInformation();

				if (propertyStore.Contains(PropertyKeys.PKEY_Device_InstanceId))
					return (string)propertyStore[PropertyKeys.PKEY_Device_InstanceId].Value;

				return "Unknown";
			}
		}

		/// <summary>
		/// Device ID
		/// </summary>
		internal string ID
		{
			get
			{
				Marshal.ThrowExceptionForHR(deviceInterface.GetId(out var result));
				return result;
			}
		}

		/// <summary>
		/// Data Flow
		/// </summary>
		internal DataFlow DataFlow
		{
			get
			{
				var ep = deviceInterface as IMMEndpoint;
				_ = ep.GetDataFlow(out var result);
				return result;
			}
		}

		/// <summary>
		/// Device State
		/// </summary>
		internal DeviceState State
		{
			get
			{
				Marshal.ThrowExceptionForHR(deviceInterface.GetState(out var result));
				return result;
			}
		}

		#endregion

		#region Constructor
		internal MMDevice(IMMDevice realDevice)
		{
			deviceInterface = realDevice;
		}
		#endregion

		/// <summary>
		/// To string
		/// </summary>
		public override string ToString()
		{
			return FriendlyName;
		}

		/// <summary>
		/// Dispose
		/// </summary>
		public void Dispose()
		{
			this.audioEndpointVolume?.Dispose();
			//this.audioSessionManager?.Dispose();
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Finalizer
		/// </summary>
		~MMDevice()
		{
			Dispose();
		}
	}

	/// <summary>
	/// Multimedia Device Collection
	/// </summary>
	internal class MMDeviceCollection : IEnumerable<MMDevice>
	{
		private readonly IMMDeviceCollection mmDeviceCollection;

		/// <summary>
		/// Device count
		/// </summary>
		internal int Count
		{
			get
			{
				Marshal.ThrowExceptionForHR(mmDeviceCollection.GetCount(out var result));
				return result;
			}
		}

		/// <summary>
		/// Get device by index
		/// </summary>
		/// <param name="index">Device index</param>
		/// <returns>Device at the specified index</returns>
		internal MMDevice this[int index]
		{
			get
			{
				_ = mmDeviceCollection.Item(index, out var result);
				return new MMDevice(result);
			}
		}

		internal MMDeviceCollection(IMMDeviceCollection parent)
		{
			mmDeviceCollection = parent;
		}

		#region IEnumerable<MMDevice> Members

		/// <summary>
		/// Get Enumerator
		/// </summary>
		/// <returns>Device enumerator</returns>
		public IEnumerator<MMDevice> GetEnumerator()
		{
			for (int index = 0; index < Count; index++)
			{
				yield return this[index];
			}
		}

		#endregion

		#region IEnumerable Members

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion
	}

	/// <summary>
	/// MM Device Enumerator
	/// </summary>
	internal class MMDeviceEnumerator : IDisposable
	{
		private IMMDeviceEnumerator realEnumerator;

		/// <summary>
		/// Creates a new MM Device Enumerator
		/// </summary>
		internal MMDeviceEnumerator()
		{
			if (Environment.OSVersion.Version.Major < 6)
			{
				_ = Errors.ErrorOccurred("This functionality is only supported on Windows Vista or newer.");
				return;
			}

			realEnumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
		}

		/// <summary>
		/// Enumerate Audio Endpoints
		/// </summary>
		/// <param name="dataFlow">Desired DataFlow</param>
		/// <param name="dwStateMask">State Mask</param>
		/// <returns>Device Collection</returns>
		internal MMDeviceCollection EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState dwStateMask)
		{
			Marshal.ThrowExceptionForHR(realEnumerator.EnumAudioEndpoints(dataFlow, dwStateMask, out var result));
			return new MMDeviceCollection(result);
		}

		/// <summary>
		/// Get Default Endpoint
		/// </summary>
		/// <param name="dataFlow">Data Flow</param>
		/// <param name="role">Role</param>
		/// <returns>Device</returns>
		internal MMDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role)
		{
			Marshal.ThrowExceptionForHR(realEnumerator.GetDefaultAudioEndpoint(dataFlow, role, out var device));
			return new MMDevice(device);
		}

		/// <summary>
		/// Check to see if a default audio end point exists without needing an exception.
		/// </summary>
		/// <param name="dataFlow">Data Flow</param>
		/// <param name="role">Role</param>
		/// <returns>True if one exists, and false if one does not exist.</returns>
		internal bool HasDefaultAudioEndpoint(DataFlow dataFlow, Role role)
		{
			const int E_NOTFOUND = unchecked((int)0x80070490);
			int hresult = realEnumerator.GetDefaultAudioEndpoint(dataFlow, role, out var device);

			if (hresult == 0x0)
			{
				_ = Marshal.ReleaseComObject(device);
				return true;
			}

			if (hresult == E_NOTFOUND)
			{
				return false;
			}

			Marshal.ThrowExceptionForHR(hresult);
			return false;
		}

		/// <summary>
		/// Get device by ID
		/// </summary>
		/// <param name="id">Device ID</param>
		/// <returns>Device</returns>
		internal MMDevice GetDevice(string id)
		{
			Marshal.ThrowExceptionForHR(realEnumerator.GetDevice(id, out var device));
			return new MMDevice(device);
		}

		/// <summary>
		/// Registers a call back for Device Events
		/// </summary>
		/// <param name="client">Object implementing IMMNotificationClient type casted as IMMNotificationClient interface</param>
		/// <returns></returns>
		internal int RegisterEndpointNotificationCallback([In][MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client)
		{
			return realEnumerator.RegisterEndpointNotificationCallback(client);
		}

		/// <summary>
		/// Unregisters a call back for Device Events
		/// </summary>
		/// <param name="client">Object implementing IMMNotificationClient type casted as IMMNotificationClient interface </param>
		/// <returns></returns>
		internal int UnregisterEndpointNotificationCallback([In][MarshalAs(UnmanagedType.Interface)] IMMNotificationClient client)
		{
			return realEnumerator.UnregisterEndpointNotificationCallback(client);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Called to dispose/finalize contained objects.
		/// </summary>
		/// <param name="disposing">True if disposing, false if called from a finalizer.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (realEnumerator != null)
				{
					// although GC would do this for us, we want it done now
					_ = Marshal.ReleaseComObject(realEnumerator);
					realEnumerator = null;
				}
			}
		}
	}

	/// <summary>
	/// Property Store class, only supports reading properties at the moment.
	/// </summary>
	internal class PropertyStore
	{
		private readonly IPropertyStore storeInterface;

		/// <summary>
		/// Property Count
		/// </summary>
		internal int Count
		{
			get
			{
				Marshal.ThrowExceptionForHR(storeInterface.GetCount(out var result));
				return result;
			}
		}

		/// <summary>
		/// Creates a new property store
		/// </summary>
		/// <param name="store">IPropertyStore COM interface</param>
		internal PropertyStore(IPropertyStore store) => storeInterface = store;

		/// <summary>
		/// Saves a property change.
		/// </summary>
		internal void Commit() => Marshal.ThrowExceptionForHR(storeInterface.Commit());

		/// <summary>
		/// Contains property guid
		/// </summary>
		/// <param name="key">Looks for a specific key</param>
		/// <returns>True if found</returns>
		internal bool Contains(PropertyKey key)
		{
			for (var i = 0; i < Count; i++)
			{
				var ikey = Get(i);

				if ((ikey.formatId == key.formatId) && (ikey.propertyId == key.propertyId))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Gets property key at sepecified index
		/// </summary>
		/// <param name="index">Index</param>
		/// <returns>Property key</returns>
		internal PropertyKey Get(int index)
		{
			Marshal.ThrowExceptionForHR(storeInterface.GetAt(index, out var key));
			return key;
		}

		/// <summary>
		/// Gets property value at specified index
		/// </summary>
		/// <param name="index">Index</param>
		/// <returns>Property value</returns>
		internal PropVariant GetValue(int index)
		{
			var key = Get(index);
			Marshal.ThrowExceptionForHR(storeInterface.GetValue(ref key, out var result));
			return result;
		}

		/// <summary>
		/// Sets property value at specified key.
		/// </summary>
		/// <param name="key">Key of property to set.</param>
		/// <param name="value">Value to write.</param>
		internal void SetValue(PropertyKey key, PropVariant value) => Marshal.ThrowExceptionForHR(storeInterface.SetValue(ref key, ref value));

		/// <summary>
		/// Gets property by index
		/// </summary>
		/// <param name="index">Property index</param>
		/// <returns>The property</returns>
		internal PropertyStoreProperty this[int index]
		{
			get
			{
				var key = Get(index);
				Marshal.ThrowExceptionForHR(storeInterface.GetValue(ref key, out var result));
				return new PropertyStoreProperty(key, result);
			}
		}

		/// <summary>
		/// Indexer by guid
		/// </summary>
		/// <param name="key">Property Key</param>
		/// <returns>Property or null if not found</returns>
		internal PropertyStoreProperty this[PropertyKey key]
		{
			get
			{
				for (var i = 0; i < Count; i++)
				{
					var ikey = Get(i);

					if ((ikey.formatId == key.formatId) && (ikey.propertyId == key.propertyId))
					{
						Marshal.ThrowExceptionForHR(storeInterface.GetValue(ref ikey, out var result));
						return new PropertyStoreProperty(ikey, result);
					}
				}

				return null;
			}
		}
	}

	/// <summary>
	/// is defined in propsys.h
	/// </summary>
	/// <summary>
	/// Property Store Property
	/// </summary>
	internal class PropertyStoreProperty
	{
		private PropVariant propertyValue;

		/// <summary>
		/// Property Key
		/// </summary>
		internal PropertyKey Key { get; }

		/// <summary>
		/// Property Value
		/// </summary>
		internal object Value => propertyValue.Value;

		internal PropertyStoreProperty(PropertyKey key, PropVariant value)
		{
			Key = key;
			propertyValue = value;
		}
	}

	internal partial class PropVariantNative
	{
		[DllImport(WindowsAPI.ole32)]
		internal static extern int PropVariantClear(ref PropVariant pvar);

		[LibraryImport(WindowsAPI.ole32, EntryPoint = "PropVariantClear")]
		internal static partial int PropVariantClear(nint pvar);
	}

	/// <summary>
	/// is defined in WTypes.h
	/// </summary>
	[Flags]
	internal enum ClsCtx
	{
		INPROC_SERVER = 0x1,
		INPROC_HANDLER = 0x2,
		LOCAL_SERVER = 0x4,
		INPROC_SERVER16 = 0x8,
		REMOTE_SERVER = 0x10,
		INPROC_HANDLER16 = 0x20,

		//RESERVED1 = 0x40,
		//RESERVED2 = 0x80,
		//RESERVED3 = 0x100,
		//RESERVED4 = 0x200,
		NO_CODE_DOWNLOAD = 0x400,

		//RESERVED5 = 0x800,
		NO_CUSTOM_MARSHAL = 0x1000,

		ENABLE_CODE_DOWNLOAD = 0x2000,
		NO_FAILURE_LOG = 0x4000,
		DISABLE_AAA = 0x8000,
		ENABLE_AAA = 0x10000,
		FROM_DEFAULT_CONTEXT = 0x20000,
		ACTIVATE_32_BIT_SERVER = 0x40000,
		ACTIVATE_64_BIT_SERVER = 0x80000,
		ENABLE_CLOAKING = 0x100000,
		PS_DLL = unchecked((int)0x80000000),
		INPROC = INPROC_SERVER | INPROC_HANDLER,
		SERVER = INPROC_SERVER | LOCAL_SERVER | REMOTE_SERVER,
		ALL = SERVER | INPROC_HANDLER
	}

	/// <summary>
	/// The EDataFlow enumeration defines constants that indicate the direction
	/// in which audio data flows between an audio endpoint device and an application
	/// </summary>
	internal enum DataFlow
	{
		/// <summary>
		/// Audio rendering stream.
		/// Audio data flows from the application to the audio endpoint device, which renders the stream.
		/// </summary>
		Render,

		/// <summary>
		/// Audio capture stream. Audio data flows from the audio endpoint device that captures the stream,
		/// to the application
		/// </summary>
		Capture,

		/// <summary>
		/// Audio rendering or capture stream. Audio data can flow either from the application to the audio
		/// endpoint device, or from the audio endpoint device to the application.
		/// </summary>
		All
	}

	/// <summary>
	/// Device State
	/// </summary>
	[Flags]
	internal enum DeviceState
	{
		/// <summary>
		/// DEVICE_STATE_ACTIVE
		/// </summary>
		Active = 0x00000001,

		/// <summary>
		/// DEVICE_STATE_DISABLED
		/// </summary>
		Disabled = 0x00000002,

		/// <summary>
		/// DEVICE_STATE_NOTPRESENT
		/// </summary>
		NotPresent = 0x00000004,

		/// <summary>
		/// DEVICE_STATE_UNPLUGGED
		/// </summary>
		Unplugged = 0x00000008,

		/// <summary>
		/// DEVICE_STATEMASK_ALL
		/// </summary>
		All = 0x0000000F
	}

	/// <summary>
	/// Endpoint Hardware Support
	/// </summary>
	[Flags]
	internal enum EEndpointHardwareSupport
	{
		/// <summary>
		/// Volume
		/// </summary>
		Volume = 0x00000001,

		/// <summary>
		/// Mute
		/// </summary>
		Mute = 0x00000002,

		/// <summary>
		/// Meter
		/// </summary>
		Meter = 0x00000004
	}

	/// <summary>
	/// The ERole enumeration defines constants that indicate the role
	/// that the system has assigned to an audio endpoint device
	/// </summary>
	internal enum Role
	{
		/// <summary>
		/// Games, system notification sounds, and voice commands.
		/// </summary>
		Console,

		/// <summary>
		/// Music, movies, narration, and live music recording
		/// </summary>
		Multimedia,

		/// <summary>
		/// Voice communications (talking to another person).
		/// </summary>
		Communications,
	}

	/// <summary>
	/// MMDevice STGM enumeration
	/// </summary>
	internal enum StorageAccessMode
	{
		/// <summary>
		/// Read-only access mode.
		/// </summary>
		Read,

		/// <summary>
		/// Write-only access mode.
		/// </summary>
		Write,

		/// <summary>
		/// Read-write access mode.
		/// </summary>
		ReadWrite
	}

	/// <summary>
	/// Defines constants that indicate the current state of an audio session.
	/// </summary>
	/// <remarks>
	/// MSDN Reference: http://msdn.microsoft.com/en-us/library/dd370792.aspx
	/// </remarks>
	internal enum AudioSessionState
	{
		/// <summary>
		/// The audio session is inactive.
		/// </summary>
		AudioSessionStateInactive = 0,

		/// <summary>
		/// The audio session is active.
		/// </summary>
		AudioSessionStateActive = 1,

		/// <summary>
		/// The audio session has expired.
		/// </summary>
		AudioSessionStateExpired = 2
	}

	/// <summary>
	/// Defines constants that indicate a reason for an audio session being disconnected.
	/// </summary>
	/// <remarks>
	/// MSDN Reference: Unknown
	/// </remarks>
	internal enum AudioSessionDisconnectReason
	{
		/// <summary>
		/// The user removed the audio endpoint device.
		/// </summary>
		DisconnectReasonDeviceRemoval = 0,

		/// <summary>
		/// The Windows audio service has stopped.
		/// </summary>
		DisconnectReasonServerShutdown = 1,

		/// <summary>
		/// The stream format changed for the device that the audio session is connected to.
		/// </summary>
		DisconnectReasonFormatChanged = 2,

		/// <summary>
		/// The user logged off the WTS session that the audio session was running in.
		/// </summary>
		DisconnectReasonSessionLogoff = 3,

		/// <summary>
		/// The WTS session that the audio session was running in was disconnected.
		/// </summary>
		DisconnectReasonSessionDisconnected = 4,

		/// <summary>
		/// The (shared-mode) audio session was disconnected to make the audio endpoint device available for an exclusive-mode connection.
		/// </summary>
		DisconnectReasonExclusiveModeOverride = 5
	}

	/// <summary>
	/// Connector type
	/// </summary>
	internal enum ConnectorType
	{
		/// <summary>
		/// The connector is part of a connection of unknown type.
		/// </summary>
		UnknownConnector,
		/// <summary>
		/// The connector is part of a physical connection to an auxiliary device that is installed inside the system chassis
		/// </summary>
		PhysicalInternal,
		/// <summary>
		/// The connector is part of a physical connection to an external device.
		/// </summary>
		PhysicalExternal,
		/// <summary>
		/// The connector is part of a software-configured I/O connection (typically a DMA channel) between system memory and an audio hardware device on an audio adapter.
		/// </summary>
		SoftwareIo,
		/// <summary>
		/// The connector is part of a permanent connection that is fixed and cannot be configured under software control.
		/// </summary>
		SoftwareFixed,
		/// <summary>
		/// The connector is part of a connection to a network.
		/// </summary>
		Network,
	}

	internal enum PartTypeEnum
	{
		Connector = 0,
		Subunit = 1,
		HardwarePeriphery = 2,
		SoftwareDriver = 3,
		Splitter = 4,
		Category = 5,
		Other = 6
	}

	/// <summary>
	/// Audio Endpoint Volume Notifiaction Delegate
	/// </summary>
	/// <param name="data">Audio Volume Notification Data</param>
	internal delegate void AudioEndpointVolumeNotificationDelegate(AudioVolumeNotificationData data);
}
#endif