#if OSX
using Keysharp.Builtins;
using Keysharp.Internals.AppleEvents;

namespace Keysharp.Internals.Input.MacOS
{
	internal static unsafe partial class MacSystemSettings
	{
		private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
		private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
		private const uint PageGenericDesktop = 1, UsagePointer = 1, UsageMouse = 2, UsageWheel = 0x38;
		private const uint ElementInputMisc = 1, ElementInputAxis = 3;
		private static readonly nint ParametersKey = CF.CreateString("HIDParameters");
		private static readonly nint ButtonModeKey = CF.CreateString("HIDPointerButtonMode");

		internal static bool MouseButtonsSwapped()
		{
			var service = IOServiceGetMatchingService(0, IOServiceMatching("IOHIDSystem"));

			if (service == 0)
				return QueryFailed("The macOS HID system is unavailable.");

			try
			{
				var parameters = IORegistryEntryCreateCFProperty(service, ParametersKey, 0, 0);

				if (parameters == 0)
					return QueryFailed("The macOS pointer button settings are unavailable.");

				try
				{
					if (!CF.CFDictionaryGetValueIfPresent(parameters, ButtonModeKey, out var value)
							|| value == 0 || CF.CFGetTypeID(value) != CF.CFNumberGetTypeID()
							|| !CF.CFNumberGetValue(value, 3, out int mode))
						return QueryFailed("The macOS pointer button mode is unavailable.");

					// IOHIDButtonModes: 0 maps both buttons to primary, 1 reverses them, 2 enables secondary.
					return mode == 1;
				}
				finally { CF.CFRelease(parameters); }
			}
			finally { _ = IOObjectRelease(service); }
		}

		internal static bool MouseWheelPresent()
		{
			var manager = IOHIDManagerCreate(0, 0);

			if (manager == 0)
				return QueryFailed("The macOS HID device list is unavailable.");

			try
			{
				// A fresh descriptor-only enumeration sees hot-plugging without opening input devices or a run loop.
				IOHIDManagerSetDeviceMatching(manager, 0);
				var devices = IOHIDManagerCopyDevices(manager);

				if (devices == 0)
					return false;

				try
				{
					var handles = new nint[checked((int)CFSetGetCount(devices))];

					if (handles.Length != 0)
						fixed (nint* values = handles)
							CFSetGetValues(devices, values);

					foreach (var device in handles)
					{
						if (!IOHIDDeviceConformsTo(device, PageGenericDesktop, UsageMouse)
								&& !IOHIDDeviceConformsTo(device, PageGenericDesktop, UsagePointer))
							continue;

						var elements = IOHIDDeviceCopyMatchingElements(device, 0, 0);

						if (elements == 0)
							continue;

						try
						{
							for (nint i = 0, count = CF.CFArrayGetCount(elements); i < count; i++)
							{
								var element = CF.CFArrayGetValueAtIndex(elements, i);

								if (IOHIDElementGetType(element) is ElementInputMisc or ElementInputAxis
										&& IOHIDElementGetUsagePage(element) == PageGenericDesktop
										&& IOHIDElementGetUsage(element) == UsageWheel)
									return true;
							}
						}
						finally { CF.CFRelease(elements); }
					}

					return false;
				}
				finally { CF.CFRelease(devices); }
			}
			finally { CF.CFRelease(manager); }
		}

		private static bool QueryFailed(string message)
		{
			_ = Errors.OSErrorOccurredWithMessage(message);
			return false;
		}

		[LibraryImport(IOKit, StringMarshalling = StringMarshalling.Utf8)]
		private static partial nint IOServiceMatching(string name);

		[LibraryImport(IOKit)]
		private static partial uint IOServiceGetMatchingService(uint mainPort, nint matching);

		[LibraryImport(IOKit)]
		private static partial nint IORegistryEntryCreateCFProperty(uint entry, nint key, nint allocator, uint options);

		[LibraryImport(IOKit)]
		private static partial int IOObjectRelease(uint entry);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDManagerCreate(nint allocator, uint options);

		[LibraryImport(IOKit)]
		private static partial void IOHIDManagerSetDeviceMatching(nint manager, nint matching);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDManagerCopyDevices(nint manager);

		[LibraryImport(IOKit)]
		[return: MarshalAs(UnmanagedType.U1)]
		private static partial bool IOHIDDeviceConformsTo(nint device, uint usagePage, uint usage);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDDeviceCopyMatchingElements(nint device, nint matching, uint options);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetType(nint element);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetUsagePage(nint element);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetUsage(nint element);

		[LibraryImport(CoreFoundation)]
		private static partial nint CFSetGetCount(nint set);

		[LibraryImport(CoreFoundation)]
		private static partial void CFSetGetValues(nint set, nint* values);
	}
}
#endif
