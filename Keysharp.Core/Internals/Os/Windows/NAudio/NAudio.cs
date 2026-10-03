using Keysharp.Builtins;
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
namespace Keysharp.Internals.Os.Windows
{
	//You can never use the code rearranger on this file because many of the types
	//in it are for COM, and the order of declarations must match exactly.


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
		/// Helper method to gets blob data
		/// </summary>
		private byte[] GetBlob()
		{
			var blob = new byte[blobVal.Length];
			Marshal.Copy(blobVal.Data, blob, 0, blob.Length);
			return blob;
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
	}

	/// <summary>
	/// Property Keys
	/// </summary>
	internal static class PropertyKeys
	{
		internal static readonly PropertyKey PKEY_Device_DeviceDesc = new (new Guid(unchecked((int)0xa45c254e), unchecked((short)0xdf1c), 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0), 2);
		internal static readonly PropertyKey PKEY_Device_FriendlyName = new (new Guid(unchecked((int)0xa45c254e), unchecked((short)0xdf1c), 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0), 14);
		internal static readonly PropertyKey PKEY_DeviceInterface_FriendlyName = new (new Guid(0x026e516e, unchecked((short)0xb814), 0x414b, 0x83, 0xcd, 0x85, 0x6d, 0x6f, 0xef, 0x48, 0x22), 2);
	}

	/// <summary>One endpoint and the interfaces acquired from it.</summary>

	internal sealed class MMDevice : IDisposable
	{
		internal readonly IMMDevice deviceInterface;
		private PropertyStore propertyStore;
		private IDeviceTopology deviceTopology;
		private int disposed;

		internal static Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
		internal static Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
		internal static Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
		private static Guid IID_IDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");

		internal MMDevice(IMMDevice device) => deviceInterface = device;

		internal IDeviceTopology DeviceTopology
		{
			get
			{
				if (deviceTopology == null)
				{
					Marshal.ThrowExceptionForHR(deviceInterface.Activate(ref IID_IDeviceTopology, ClsCtx.ALL, 0, out var result));
					deviceTopology = (IDeviceTopology)result;
				}
				return deviceTopology;
			}
		}

		internal string FriendlyName
		{
			get
			{
				if (propertyStore == null)
				{
					Marshal.ThrowExceptionForHR(deviceInterface.OpenPropertyStore(StorageAccessMode.Read, out var store));
					propertyStore = new PropertyStore(store);
				}
				return propertyStore[PropertyKeys.PKEY_Device_FriendlyName] as string ?? "Unknown";
			}
		}

		internal string ID
		{
			get { Marshal.ThrowExceptionForHR(deviceInterface.GetId(out var id)); return id; }
		}

		internal DataFlow DataFlow
		{
			get { Marshal.ThrowExceptionForHR(((IMMEndpoint)deviceInterface).GetDataFlow(out var flow)); return flow; }
		}

		internal DeviceState State
		{
			get { Marshal.ThrowExceptionForHR(deviceInterface.GetState(out var state)); return state; }
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) != 0)
				return;

			try { propertyStore?.Dispose(); }
			finally
			{
				try { if (deviceTopology != null) Marshal.ReleaseComObject(deviceTopology); }
				finally { Marshal.ReleaseComObject(deviceInterface); }
			}
		}
	}

	/// <summary>
	/// Multimedia Device Collection
	/// </summary>
	internal class MMDeviceCollection : IEnumerable<MMDevice>, IDisposable
	{
		private IMMDeviceCollection mmDeviceCollection;

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
				//Now that Item carries [PreserveSig], a failure returns a status instead of throwing;
				//ignoring it would hand a null device to MMDevice and fail later and less clearly.
				Marshal.ThrowExceptionForHR(mmDeviceCollection.Item(index, out var result));
				return new MMDevice(result);
			}
		}

		internal MMDeviceCollection(IMMDeviceCollection parent)
		{
			mmDeviceCollection = parent;
		}

		public void Dispose()
		{
			var old = Interlocked.Exchange(ref mmDeviceCollection, null);
			if (old != null) Marshal.ReleaseComObject(old);
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

		internal bool TryGetDefaultAudioEndpoint(DataFlow flow, Role role, out MMDevice result)
		{
			result = null;
			var hr = realEnumerator.GetDefaultAudioEndpoint(flow, role, out var device);
			if (hr == unchecked((int)0x80070490)) return false;
			if (hr < 0) throw new COMException(null, hr);
			if (device == null) return false;
			result = new MMDevice(device);
			return true;
		}

		internal bool HasDefaultAudioEndpoint(DataFlow flow, Role role)
		{
			if (!TryGetDefaultAudioEndpoint(flow, role, out var device)) return false;
			device.Dispose();
			return true;
		}

		internal bool TryGetDevice(string id, out MMDevice result)
		{
			result = null;
			var hr = realEnumerator.GetDevice(id, out var device);
			if (hr == unchecked((int)0x80070490)) return false;
			if (hr < 0) throw new COMException(null, hr);
			if (device == null) return false;
			result = new MMDevice(device);
			return true;
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
	internal sealed class PropertyStore(IPropertyStore store) : IDisposable
	{
		private IPropertyStore storeInterface = store;

		internal object this[PropertyKey key]
		{
			get
			{
				var hr = storeInterface.GetValue(ref key, out var result);
				try { return hr < 0 || result.vt == 0 ? null : result.Value; }
				finally { PropVariantNative.PropVariantClear(ref result); }
			}
		}

		public void Dispose()
		{
			var old = Interlocked.Exchange(ref storeInterface, null);
			if (old != null) Marshal.ReleaseComObject(old);
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
}
#endif
