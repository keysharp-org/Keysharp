#if OSX
namespace Keysharp.Internals.Os.MacOS
{
	/// <summary>
	/// The Core Audio HAL property accessors, shared by everything that reads or writes a device property. The
	/// HAL exposes one generic get/set pair over an opaque address, so every caller needs the same handful of
	/// typed wrappers around it; keeping one copy is what stops the Sound functions and the Audio class drifting
	/// apart on details like who owns a returned CFString.
	/// <para>
	/// Every framework entry point is reached by absolute path, and every wrapper returns the raw OSStatus so a
	/// caller can classify a failure rather than have it thrown at them.
	/// </para>
	/// </summary>
	internal static class CoreAudioHal
	{
		internal const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
		internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		internal const uint kAudioObjectSystemObject = 1u;
		internal const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62u;   // 'glob'
		internal const uint kAudioObjectPropertyScopeOutput = 0x6F757470u;   // 'outp'
		internal const uint kAudioObjectPropertyScopeInput = 0x696E7074u;    // 'inpt'
		internal const uint kAudioObjectPropertyElementMain = 0u;
		internal const uint kAudioObjectPropertyName = 0x6C6E616Du;          // 'lnam'
		internal const uint kAudioDevicePropertyStreamConfiguration = 0x736C6179u;   // 'slay'
		internal const uint kAudioHardwareServiceDevicePropertyVirtualMasterVolume = 0x766D7663u; // 'vmvc'
		internal const uint kAudioDevicePropertyVolumeScalar = 0x766F6C75u; // 'volu'
		internal const uint kAudioDevicePropertyMute = 0x6D757465u; // 'mute'
		private const uint kAudioHardwarePropertyTranslateUIDToDevice = 0x75696464u; // 'uidd'

		[StructLayout(LayoutKind.Sequential)]
		internal struct AudioObjectPropertyAddress
		{
			public uint mSelector;
			public uint mScope;
			public uint mElement;
		}

		[StructLayout(LayoutKind.Sequential)]
		internal struct CFRange
		{
			public long location;
			public long length;
		}

		[DllImport(CoreAudio)]
		internal static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, ref uint dataSize, nint outData);

		[DllImport(CoreAudio)]
		internal static extern int AudioObjectSetPropertyData(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, uint dataSize, nint inData);

		[DllImport(CoreAudio)]
		internal static extern int AudioObjectGetPropertyDataSize(uint objectId, ref AudioObjectPropertyAddress addr, uint qualifierSize, nint qualifierData, out uint outDataSize);

		[DllImport(CoreFoundation)]
		internal static extern long CFStringGetLength(nint cfStr);

		[DllImport(CoreFoundation)]
		internal static extern void CFStringGetCharacters(nint cfStr, CFRange range, nint buffer);

		[DllImport(CoreFoundation)]
		internal static extern nint CFStringCreateWithCharacters(nint allocator, nint chars, long numChars);

		[DllImport(CoreFoundation)]
		internal static extern void CFRelease(nint cfTypeRef);

		internal static AudioObjectPropertyAddress Address(uint selector, uint scope, uint element = kAudioObjectPropertyElementMain) => new ()
		{
			mSelector = selector,
			mScope = scope,
			mElement = element,
		};

		internal static unsafe uint GetDeviceId(ReadOnlySpan<char> uid)
		{
			if (uid.IsEmpty)
				return 0;

			nint cfUid;

			fixed (char* chars = uid)
				cfUid = CFStringCreateWithCharacters(0, (nint)chars, uid.Length);

			if (cfUid == 0)
				return 0;

			try
			{
				var addr = Address(kAudioHardwarePropertyTranslateUIDToDevice, kAudioObjectPropertyScopeGlobal);
				uint deviceId = 0;
				uint size = sizeof(uint);
				return AudioObjectGetPropertyData(kAudioObjectSystemObject, ref addr, (uint)sizeof(nint), (nint)(&cfUid), ref size, (nint)(&deviceId)) == 0
					   ? deviceId : 0;
			}
			finally
			{
				CFRelease(cfUid);
			}
		}

		/// <summary>Reads a direction's virtual master, scalar master, then first channel control.</summary>
		internal static int GetDeviceVolume(uint deviceId, uint scope, out float volume)
		{
			var addr = Address(kAudioHardwareServiceDevicePropertyVirtualMasterVolume, scope);
			var result = GetPropertyFloat(deviceId, addr, out volume);

			if (result != 0)
			{
				addr.mSelector = kAudioDevicePropertyVolumeScalar;
				result = GetPropertyFloat(deviceId, addr, out volume);

				if (result != 0)
				{
					addr.mElement = 1;
					result = GetPropertyFloat(deviceId, addr, out volume);
				}
			}

			return result;
		}

		internal static int SetDeviceVolume(uint deviceId, uint scope, float volume)
		{
			var addr = Address(kAudioHardwareServiceDevicePropertyVirtualMasterVolume, scope);
			var result = SetPropertyFloat(deviceId, addr, volume);

			if (result != 0)
			{
				addr.mSelector = kAudioDevicePropertyVolumeScalar;
				result = SetPropertyFloat(deviceId, addr, volume);

				if (result != 0)
				{
					var channels = GetChannelCount(deviceId, scope);

					for (var channel = 1; channel <= channels; channel++)
					{
						addr.mElement = (uint)channel;
						result = SetPropertyFloat(deviceId, addr, volume);

						if (result != 0)
							return result;
					}
				}
			}

			return result;
		}

		internal static int GetDeviceMute(uint deviceId, uint scope, out bool muted)
		{
			var addr = Address(kAudioDevicePropertyMute, scope);
			var result = GetPropertyUInt(deviceId, addr, out var value);

			if (result != 0)
			{
				addr.mElement = 1;
				result = GetPropertyUInt(deviceId, addr, out value);
			}

			muted = value != 0;
			return result;
		}

		internal static int SetDeviceMute(uint deviceId, uint scope, bool muted)
		{
			var addr = Address(kAudioDevicePropertyMute, scope);
			var value = muted ? 1u : 0u;
			var result = SetPropertyUInt(deviceId, addr, value);

			if (result != 0)
			{
				var channels = GetChannelCount(deviceId, scope);

				for (var channel = 1; channel <= channels; channel++)
				{
					addr.mElement = (uint)channel;
					result = SetPropertyUInt(deviceId, addr, value);

					if (result != 0)
						return result;
				}
			}

			return result;
		}

		internal static unsafe int GetPropertyFloat(uint objectId, AudioObjectPropertyAddress addr, out float value)
		{
			float buffer = 0;
			uint size = sizeof(float);
			var result = AudioObjectGetPropertyData(objectId, ref addr, 0, 0, ref size, (nint)(&buffer));
			value = result == 0 ? buffer : 0f;
			return result;
		}

		internal static unsafe int SetPropertyFloat(uint objectId, AudioObjectPropertyAddress addr, float value)
		{
			return AudioObjectSetPropertyData(objectId, ref addr, 0, 0, sizeof(float), (nint)(&value));
		}

		internal static unsafe int GetPropertyUInt(uint objectId, AudioObjectPropertyAddress addr, out uint value)
		{
			uint buffer = 0;
			uint size = sizeof(uint);
			var result = AudioObjectGetPropertyData(objectId, ref addr, 0, 0, ref size, (nint)(&buffer));
			value = result == 0 ? buffer : 0;
			return result;
		}

		internal static unsafe int SetPropertyUInt(uint objectId, AudioObjectPropertyAddress addr, uint value)
		{
			return AudioObjectSetPropertyData(objectId, ref addr, 0, 0, sizeof(uint), (nint)(&value));
		}

		internal static uint[] GetPropertyUInts(uint objectId, AudioObjectPropertyAddress addr)
		{
			if (AudioObjectGetPropertyDataSize(objectId, ref addr, 0, nint.Zero, out var dataSize) != 0 || dataSize == 0)
				return [];

			var ptr = Marshal.AllocHGlobal((int)dataSize);

			try
			{
				if (AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref dataSize, ptr) != 0)
					return [];

				var count = (int)(dataSize / sizeof(uint));
				var result = new uint[count];

				for (var i = 0; i < count; i++)
					result[i] = (uint)Marshal.ReadInt32(ptr, i * sizeof(uint));

				return result;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		internal static string GetPropertyString(uint objectId, AudioObjectPropertyAddress addr)
		{
			var ptrSize = nint.Size;
			var cfStrHolder = Marshal.AllocHGlobal(ptrSize);

			try
			{
				var size = (uint)ptrSize;

				if (AudioObjectGetPropertyData(objectId, ref addr, 0, nint.Zero, ref size, cfStrHolder) != 0)
					return "";

				var cfStr = Marshal.ReadIntPtr(cfStrHolder);

				if (cfStr == nint.Zero)
					return "";

				try
				{
					var len = CFStringGetLength(cfStr);

					if (len <= 0)
						return "";

					var charBuf = Marshal.AllocHGlobal((int)(len * 2));

					try
					{
						CFStringGetCharacters(cfStr, new CFRange { location = 0, length = len }, charBuf);
						return Marshal.PtrToStringUni(charBuf, (int)len) ?? "";
					}
					finally
					{
						Marshal.FreeHGlobal(charBuf);
					}
				}
				finally
				{
					// A CFString from a Get property arrives with a reference this caller owns.
					CFRelease(cfStr);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(cfStrHolder);
			}
		}

		/// <summary>
		/// Total channels the device carries in one direction, which is also how its kind is decided. The
		/// property is an AudioBufferList: a UInt32 buffer count, then 16-byte AudioBuffer entries starting at
		/// offset 8, each opening with its own channel count.
		/// </summary>
		internal static int GetChannelCount(uint deviceId, uint scope)
		{
			var addr = Address(kAudioDevicePropertyStreamConfiguration, scope);

			if (AudioObjectGetPropertyDataSize(deviceId, ref addr, 0, nint.Zero, out var dataSize) != 0 || dataSize < 4)
				return 0;

			var ptr = Marshal.AllocHGlobal((int)dataSize);

			try
			{
				if (AudioObjectGetPropertyData(deviceId, ref addr, 0, nint.Zero, ref dataSize, ptr) != 0)
					return 0;

				var buffers = (uint)Marshal.ReadInt32(ptr);
				var total = 0;

				for (var i = 0; i < buffers; i++)
				{
					var entry = 8 + (i * 16);

					if (entry + 4 > dataSize)
						break;

					total += Marshal.ReadInt32(ptr, entry);
				}

				return total;
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}
	}
}
#endif
