#if OSX
namespace Keysharp.Internals.AppleEvents
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct CFRange
	{
		internal nint Location;
		internal nint Length;
	}

	/// <summary>
	/// The slice of Core Foundation the Apple Events layer needs: strings and URLs to reach the scripting
	/// definition, and the distributed notification centre that stands in for connection point events.
	/// </summary>
	internal static partial class CF
	{
		private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		internal const uint EncodingUTF8 = 0x08000100;
		internal const int NumberSInt64Type = 4;
		internal const int NumberDoubleType = 13;
		internal const nint NotificationSuspensionBehaviorDeliverImmediately = 4;

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRelease(nint cf);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFRetain(nint cf);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFEqual(nint first, nint second);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFRunLoopGetMain();

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFRunLoopRemoveSource(nint runLoop, nint source, nint mode);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFArrayCreate(nint allocator, nint[] values, nint count, nint callbacks);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFArrayGetCount(nint array);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFDictionaryCreate(nint allocator, nint[] keys, nint[] values, nint count, nint keyCallbacks, nint valueCallbacks);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFDictionaryGetValueIfPresent(nint dictionary, nint key, out nint value);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFNumberGetValue(nint number, int type, out int value);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFNumberGetValue(nint number, int type, out double value);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFGetTypeID(nint cf);

		[LibraryImport(CoreFoundation)]
		private static partial nint CFCopyDescription(nint cf);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFStringGetTypeID();

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFNumberGetTypeID();

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFBooleanGetTypeID();

		[LibraryImport(CoreFoundation)]
		private static partial nint CFStringCreateWithBytes(nint alloc, nint bytes, nint numBytes, uint encoding, byte isExternalRepresentation);

		[LibraryImport(CoreFoundation)]
		private static partial nint CFStringGetLength(nint theString);

		[LibraryImport(CoreFoundation)]
		private static partial void CFStringGetCharacters(nint theString, CFRange range, nint buffer);

		[LibraryImport(CoreFoundation)]
		private static partial nint CFURLCreateWithFileSystemPath(nint allocator, nint filePath, nint pathStyle, byte isDirectory);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFDataGetLength(nint theData);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFDataGetBytePtr(nint theData);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFDictionaryGetCount(nint theDict);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFDictionaryGetKeysAndValues(nint theDict, nint keys, nint values);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFBooleanGetValue(nint boolean);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFNumberGetValue(nint number, int theType, nint valuePtr);

		[LibraryImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.U1)]
		internal static partial bool CFNumberIsFloatType(nint number);

		[LibraryImport(CoreFoundation)]
		internal static partial nint CFNotificationCenterGetDistributedCenter();

		[LibraryImport(CoreFoundation)]
		internal static partial void CFNotificationCenterAddObserver(nint center, nint observer, nint callBack,
				nint name, nint @object, nint suspensionBehavior);

		[LibraryImport(CoreFoundation)]
		internal static partial void CFNotificationCenterRemoveEveryObserver(nint center, nint observer);

		[LibraryImport(CoreFoundation)]
		internal static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);

		/// <summary>Reads a borrowed CF object reference from an exported constant.</summary>
		internal static nint ResolvePointerConstant(string symbolName)
		{
			if (!NativeLibrary.TryLoad(CoreFoundation, out var coreFoundation))
				return 0;

			try
			{
				return NativeLibrary.TryGetExport(coreFoundation, symbolName, out var symbol) && symbol != 0
					? Marshal.ReadIntPtr(symbol)
					: 0;
			}
			finally
			{
				NativeLibrary.Free(coreFoundation);
			}
		}

		/// <summary>Creates a CFString the caller must release.</summary>
		internal static nint CreateString(string value)
		{
			var bytes = Encoding.UTF8.GetBytes(value ?? "");

			unsafe
			{
				fixed (byte* p = bytes)
					return CFStringCreateWithBytes(0, (nint)p, bytes.Length, EncodingUTF8, 0);
			}
		}

		internal static string ReadString(nint theString)
		{
			if (theString == 0)
				return "";

			var length = CFStringGetLength(theString);

			if (length <= 0)
				return "";

			return string.Create(checked((int)length), theString, static (buffer, value) =>
			{
				unsafe
				{
					fixed (char* p = buffer)
						CFStringGetCharacters(value, new CFRange { Location = 0, Length = buffer.Length }, (nint)p);
				}
			});
		}

		/// <summary>Creates a file URL the caller must release. Path style 0 is the POSIX one.</summary>
		internal static nint CreateFileUrl(string path, bool isDirectory)
		{
			var cfPath = CreateString(path);

			if (cfPath == 0)
				return 0;

			try
			{
				return CFURLCreateWithFileSystemPath(0, cfPath, 0, isDirectory ? (byte)1 : (byte)0);
			}
			finally
			{
				CFRelease(cfPath);
			}
		}

		internal static byte[] ReadData(nint data)
		{
			if (data == 0)
				return [];

			var length = (int)CFDataGetLength(data);

			if (length <= 0)
				return [];

			var bytes = new byte[length];
			var ptr = CFDataGetBytePtr(data);

			if (ptr == 0)
				return [];

			Marshal.Copy(ptr, bytes, 0, length);
			return bytes;
		}

		/// <summary>Converts one value out of a notification's user info into the nearest script value.</summary>
		internal static object ReadValue(nint value)
		{
			if (value == 0)
				return "";

			var typeId = CFGetTypeID(value);

			if (typeId == CFStringGetTypeID())
				return ReadString(value);

			if (typeId == CFBooleanGetTypeID())
				return CFBooleanGetValue(value);

			if (typeId == CFNumberGetTypeID())
			{
				if (CFNumberIsFloatType(value))
				{
					var d = 0d;

					unsafe
					{
						_ = CFNumberGetValue(value, NumberDoubleType, (nint)(&d));
					}

					return d;
				}

				var l = 0L;

				unsafe
				{
					_ = CFNumberGetValue(value, NumberSInt64Type, (nint)(&l));
				}

				return l;
			}

			// Anything else (arrays, nested dictionaries, dates) is reported by its description rather than dropped,
			// so a payload a script did not expect is still visible instead of arriving as an empty string.
			var description = CFCopyDescription(value);

			if (description == 0)
				return "";

			try
			{
				return ReadString(description);
			}
			finally
			{
				CFRelease(description);
			}
		}
	}
}
#endif
