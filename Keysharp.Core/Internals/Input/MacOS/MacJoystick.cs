#if OSX
using Keysharp.Internals.AppleEvents;

namespace Keysharp.Internals.Input.MacOS
{
	/// <summary>
	/// Joysticks and gamepads on macOS, read through IOKit's HID manager as winmm reads them on Windows: every
	/// device whose HID descriptor calls it a joystick, gamepad or multi-axis controller, with the axes and
	/// button numbers that descriptor declares. Such devices are outside Input Monitoring, so reading them
	/// needs no permission.
	///
	/// A joystick's number is its position among those devices ordered by location, which names the port a
	/// wired controller is plugged into, so that a script's joystick number keeps meaning the same device.
	/// </summary>
	internal static unsafe partial class MacJoystick
	{
		private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
		private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		// HID usage tables.
		private const uint PageGenericDesktop = 0x01, PageSimulation = 0x02, PageButton = 0x09;
		private const uint UsageJoystick = 0x04, UsageGamePad = 0x05, UsageMultiAxis = 0x08;
		private const uint UsageX = 0x30, UsageWheel = 0x38, UsageHatSwitch = 0x39;
		private const uint UsageRudder = 0xBA, UsageThrottle = 0xBB, UsageAccelerator = 0xC4, UsageBrake = 0xC5;

		// IOHIDElementType's input kinds; the rest are outputs, features and collections.
		private const uint ElementInputMisc = 1, ElementInputAxis = 3;
		private const nint NumberSInt32Type = 3;
		private const long ListingLifetimeMs = 1000;
		private const uint NoAxis = uint.MaxValue;

		private sealed class Device
		{
			internal nint Handle;   // IOHIDDeviceRef, retained
			internal nint Elements; // The CFArray which keeps the element references below alive.
			internal long Location;
			internal JoyCaps Caps;
			internal JoystickLayout Layout;
			internal nint[] Axes;   // One element per axis of Layout.
			internal nint[] Buttons = new nint[JoystickData.MaxJoyButtons];
			internal nint Hat;
			internal long HatMinimum, HatDirections;

			internal void Release()
			{
				CF.CFRelease(Elements);
				CF.CFRelease(Handle);
			}
		}

		private static readonly Lock gate = new();
		private static nint manager;
		private static bool unavailable;
		private static Device[] devices = [];
		private static long listedAt;

		internal static bool TryGetCaps(uint index, out JoyCaps caps)
		{
			lock (gate)
			{
				var found = TryGetDevice(index, out var device);
				caps = found ? device.Caps : default;
				return found;
			}
		}

		internal static bool TryGetReading(uint index, out JoyReading reading)
		{
			reading = new JoyReading { Pov = -1 };

			lock (gate)
			{
				if (!TryGetDevice(index, out var device))
					return false;

				var answered = false;
				Span<int> values = stackalloc int[device.Axes.Length];

				for (var i = 0; i < values.Length; i++)
					answered |= TryGetValue(device, device.Axes[i], out values[i]);

				for (var i = 0; i < device.Buttons.Length; i++)
					if (device.Buttons[i] != 0 && TryGetValue(device, device.Buttons[i], out var pressed))
					{
						answered = true;

						if (pressed != 0)
							reading.Buttons |= 1u << i;
					}

				// A hat rests on a value outside its range.
				if (device.Hat != 0 && TryGetValue(device, device.Hat, out var hat))
				{
					answered = true;
					var direction = hat - device.HatMinimum;

					if (direction >= 0 && direction < device.HatDirections)
						reading.Pov = direction * 36000 / device.HatDirections;
				}

				// An unplugged device answers nothing, so the next read lists the devices again.
				if (!answered)
				{
					listedAt = 0;
					return false;
				}

				device.Layout.Read(values, ref reading);
				return true;
			}
		}

		private static bool TryGetValue(Device device, nint element, out int value)
		{
			value = 0;

			if (IOHIDDeviceGetValue(device.Handle, element, out var reference) != 0 || reference == 0)
				return false;

			value = (int)Math.Clamp(IOHIDValueGetIntegerValue(reference), int.MinValue, int.MaxValue);
			return true;
		}

		private static bool TryGetDevice(uint index, out Device device)
		{
			device = null;

			if (!EnsureManager())
				return false;

			var now = Environment.TickCount64;

			if (listedAt == 0 || now - listedAt > ListingLifetimeMs)
			{
				ListDevices();
				listedAt = now == 0 ? 1 : now;
			}

			if (index >= devices.Length)
				return false;

			device = devices[index];
			return true;
		}

		private static bool EnsureManager()
		{
			if (manager != 0)
				return true;

			if (unavailable)
				return false;

			var matching = CreateMatching();
			var created = matching != 0 ? IOHIDManagerCreate(0, 0) : 0;

			if (created == 0)
			{
				if (matching != 0)
					CF.CFRelease(matching);

				unavailable = true;
				Diagnostics.Debug.WriteLine("Joystick support is unavailable: the HID manager could not be created.");
				return false;
			}

			IOHIDManagerSetDeviceMatchingMultiple(created, matching);
			CF.CFRelease(matching);
			// The manager's device set follows plugging and unplugging only from a running loop.
			var mode = CF.ResolvePointerConstant("kCFRunLoopCommonModes");

			if (mode != 0)
				IOHIDManagerScheduleWithRunLoop(created, CF.CFRunLoopGetMain(), mode);

			// A device which refuses to open fails this and then its own reads, without affecting the others.
			_ = IOHIDManagerOpen(created, 0);
			manager = created;
			return true;
		}

		// Matching by usage keeps keyboards and pointers out of the set, which opening would otherwise put
		// behind Input Monitoring.
		private static nint CreateMatching()
		{
			var keyCallbacks = Export("kCFTypeDictionaryKeyCallBacks");
			var valueCallbacks = Export("kCFTypeDictionaryValueCallBacks");
			var arrayCallbacks = Export("kCFTypeArrayCallBacks");

			if (keyCallbacks == 0 || valueCallbacks == 0 || arrayCallbacks == 0)
				return 0;

			var pageKey = CF.CreateString("DeviceUsagePage");
			var usageKey = CF.CreateString("DeviceUsage");
			var page = CreateNumber((int)PageGenericDesktop);
			ReadOnlySpan<uint> usages = [UsageJoystick, UsageGamePad, UsageMultiAxis];
			var dictionaries = new nint[usages.Length];

			for (var i = 0; i < usages.Length; i++)
			{
				var usage = CreateNumber((int)usages[i]);
				dictionaries[i] = CF.CFDictionaryCreate(0, [pageKey, usageKey], [page, usage], 2, keyCallbacks, valueCallbacks);
				CF.CFRelease(usage);
			}

			var matching = CF.CFArrayCreate(0, dictionaries, dictionaries.Length, arrayCallbacks);

			foreach (var dictionary in dictionaries)
				CF.CFRelease(dictionary);

			CF.CFRelease(page);
			CF.CFRelease(usageKey);
			CF.CFRelease(pageKey);
			return matching;
		}

		private static void ListDevices()
		{
			var set = IOHIDManagerCopyDevices(manager);
			var handles = new nint[set != 0 ? (int)CFSetGetCount(set) : 0];

			if (handles.Length != 0)
				fixed (nint* values = handles)
					CFSetGetValues(set, values);

			var listed = new List<Device>(handles.Length);

			foreach (var handle in handles)
			{
				// A device already described is still retained, so its address cannot have passed to another.
				var device = Array.Find(devices, known => known.Handle == handle) ?? Describe(handle);

				if (device != null)
					listed.Add(device);
			}

			foreach (var known in devices)
				if (!listed.Contains(known))
					known.Release();

			if (set != 0)
				CF.CFRelease(set);

			listed.Sort(static (first, second) => first.Location != second.Location
				? first.Location.CompareTo(second.Location)
				: first.Handle.CompareTo(second.Handle));
			devices = listed.ToArray();
		}

		private static Device Describe(nint handle)
		{
			var elements = IOHIDDeviceCopyMatchingElements(handle, 0, 0);

			if (elements == 0)
				return null;

			var device = new Device { Handle = handle, Elements = elements };
			var ranges = new List<JoyAxisRange>();
			var axes = new List<nint>();
			var buttons = 0;
			var count = CF.CFArrayGetCount(elements);

			for (nint i = 0; i < count; i++)
			{
				var element = CF.CFArrayGetValueAtIndex(elements, i);
				var type = IOHIDElementGetType(element);

				if (type < ElementInputMisc || type > ElementInputAxis)
					continue;

				var page = IOHIDElementGetUsagePage(element);
				var usage = IOHIDElementGetUsage(element);
				var minimum = IOHIDElementGetLogicalMin(element);
				var maximum = IOHIDElementGetLogicalMax(element);

				if (page == PageButton)
				{
					// A button's number is its usage, as winmm numbers them.
					if (usage >= 1 && usage <= device.Buttons.Length && device.Buttons[usage - 1] == 0)
					{
						device.Buttons[usage - 1] = element;
						buttons = Math.Max(buttons, (int)usage);
					}
				}
				else if (page == PageGenericDesktop && usage == UsageHatSwitch)
				{
					if (device.Hat == 0 && maximum > minimum)
					{
						device.Hat = element;
						device.HatMinimum = minimum;
						device.HatDirections = maximum - minimum + 1;
					}
				}
				else if (AxisCode(page, usage) is var code && code != NoAxis && maximum > minimum
						&& !ranges.Exists(range => range.Code == code))
				{
					ranges.Add(new(code, (int)Math.Clamp(minimum, int.MinValue, int.MaxValue), (int)Math.Clamp(maximum, int.MinValue, int.MaxValue)));
					axes.Add(element);
				}
			}

			if (axes.Count == 0 && buttons == 0)
			{
				CF.CFRelease(elements);
				return null;
			}

			_ = CF.CFRetain(handle);
			device.Axes = axes.ToArray();
			device.Layout = new JoystickLayout(ranges.ToArray(), gamepad: false);
			device.Caps = new(CF.ReadString(IOHIDDeviceGetProperty(handle, ProductKey)), buttons, device.Layout.Axes, device.Hat != 0);
			var location = IOHIDDeviceGetProperty(handle, LocationKey);
			long locationId = 0;

			if (location != 0 && CF.CFNumberGetValue(location, CF.NumberSInt64Type, (nint)(&locationId)))
				device.Location = locationId;

			return device;
		}

		/// <summary>The evdev code of the axis a HID usage declares, as the Linux kernel maps one to the other.</summary>
		private static uint AxisCode(uint page, uint usage) => page switch
		{
			// X, Y, Z, Rx, Ry, Rz, Slider, Dial and Wheel are consecutive in both tables.
			PageGenericDesktop when usage >= UsageX && usage <= UsageWheel => usage - UsageX,
			PageSimulation => usage switch
			{
				UsageRudder => JoystickLayout.AbsRudder,
				UsageThrottle => JoystickLayout.AbsThrottle,
				UsageAccelerator => JoystickLayout.AbsGas,
				UsageBrake => JoystickLayout.AbsBrake,
				_ => NoAxis
			},
			_ => NoAxis
		};

		private static readonly nint ProductKey = CF.CreateString("Product");
		private static readonly nint LocationKey = CF.CreateString("LocationID");

		private static nint CreateNumber(int value) => CFNumberCreate(0, NumberSInt32Type, &value);

		/// <summary>The address of a structure Core Foundation exports, such as a set of collection callbacks.</summary>
		private static nint Export(string name)
		{
			if (!NativeLibrary.TryLoad(CoreFoundation, out var library))
				return 0;

			try
			{
				return NativeLibrary.TryGetExport(library, name, out var address) ? address : 0;
			}
			finally
			{
				NativeLibrary.Free(library);
			}
		}

		[LibraryImport(CoreFoundation)]
		private static partial nint CFNumberCreate(nint allocator, nint type, int* value);

		[LibraryImport(CoreFoundation)]
		private static partial nint CFSetGetCount(nint set);

		[LibraryImport(CoreFoundation)]
		private static partial void CFSetGetValues(nint set, nint* values);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDManagerCreate(nint allocator, uint options);

		[LibraryImport(IOKit)]
		private static partial void IOHIDManagerSetDeviceMatchingMultiple(nint manager, nint multiple);

		[LibraryImport(IOKit)]
		private static partial void IOHIDManagerScheduleWithRunLoop(nint manager, nint runLoop, nint runLoopMode);

		[LibraryImport(IOKit)]
		private static partial int IOHIDManagerOpen(nint manager, uint options);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDManagerCopyDevices(nint manager);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDDeviceGetProperty(nint device, nint key);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDDeviceCopyMatchingElements(nint device, nint matching, uint options);

		[LibraryImport(IOKit)]
		private static partial int IOHIDDeviceGetValue(nint device, nint element, out nint value);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDValueGetIntegerValue(nint value);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetType(nint element);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetUsagePage(nint element);

		[LibraryImport(IOKit)]
		private static partial uint IOHIDElementGetUsage(nint element);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDElementGetLogicalMin(nint element);

		[LibraryImport(IOKit)]
		private static partial nint IOHIDElementGetLogicalMax(nint element);
	}
}
#endif
