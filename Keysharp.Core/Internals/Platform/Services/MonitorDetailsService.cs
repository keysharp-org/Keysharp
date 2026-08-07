#if WINDOWS
using Microsoft.Win32;
#endif

namespace Keysharp.Internals
{
	// Per-OS producers of DisplayDetails — the expensive per-display metadata behind IScreen.GetDisplayDetails.
	// Nothing here is reachable from GetDisplays(): topology enumeration stays exactly as cheap as it was.

#if WINDOWS
	/// <summary>
	/// Windows display metadata. Identity, model and physical size come from the monitor's EDID (read from the
	/// device's registry key, which is where Windows caches what the panel reported); the connection kind, exact
	/// rational refresh rate and rotation come from the DisplayConfig API; the adapter string from
	/// <c>EnumDisplayDevices</c>. Everything is best-effort: a display that answers nothing yields
	/// <see cref="DisplayDetails.Empty"/> rather than fabricated values.
	/// </summary>
	internal static class WindowsMonitorDetails
	{
		private const uint QdcOnlyActivePaths = 2;
		private const uint EddGetDeviceInterfaceName = 1;
		private const int DeviceInfoGetSourceName = 1;
		private const int DeviceInfoGetTargetName = 2;

		// DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values Keysharp maps to a connection name.
		private const int OutputTechnologyHd15 = 0;
		private const int OutputTechnologyDvi = 3;
		private const int OutputTechnologyHdmi = 4;
		private const int OutputTechnologyLvds = 5;
		private const int OutputTechnologyDisplayPortExternal = 10;
		private const int OutputTechnologyDisplayPortEmbedded = 11;
		private const int OutputTechnologyUdiExternal = 12;
		private const int OutputTechnologyUdiEmbedded = 13;
		private const int OutputTechnologyInternal = unchecked((int)0x80000000);

		[StructLayout(LayoutKind.Sequential)]
		private struct Luid
		{
			internal uint LowPart;
			internal int HighPart;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PathSourceInfo
		{
			internal Luid AdapterId;
			internal uint Id;
			internal uint ModeInfoIdx;
			internal uint StatusFlags;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Rational
		{
			internal uint Numerator;
			internal uint Denominator;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PathTargetInfo
		{
			internal Luid AdapterId;
			internal uint Id;
			internal uint ModeInfoIdx;
			internal int OutputTechnology;
			internal int Rotation;
			internal int Scaling;
			internal Rational RefreshRate;
			internal int ScanLineOrdering;
			internal int TargetAvailable;
			internal uint StatusFlags;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PathInfo
		{
			internal PathSourceInfo SourceInfo;
			internal PathTargetInfo TargetInfo;
			internal uint Flags;
		}

		// Only the size matters here: the mode array is required by QueryDisplayConfig but Keysharp reads
		// nothing out of it, so the payload is left as an opaque 48-byte union body.
		[StructLayout(LayoutKind.Sequential, Size = 64)]
		private struct ModeInfo
		{
			internal int InfoType;
			internal uint Id;
			internal Luid AdapterId;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DeviceInfoHeader
		{
			internal int Type;
			internal uint Size;
			internal Luid AdapterId;
			internal uint Id;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct SourceDeviceName
		{
			internal DeviceInfoHeader Header;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string ViewGdiDeviceName;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct TargetDeviceName
		{
			internal DeviceInfoHeader Header;
			internal uint Flags;
			internal int OutputTechnology;
			internal ushort EdidManufactureId;
			internal ushort EdidProductCodeId;
			internal uint ConnectorInstance;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string MonitorFriendlyDeviceName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string MonitorDevicePath;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct DisplayDevice
		{
			internal uint Cb;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceString;
			internal uint StateFlags;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceID;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceKey;
		}

		[DllImport("user32.dll")]
		private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

		[DllImport("user32.dll")]
		private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
			ref uint modeCount, [Out] ModeInfo[] modes, nint currentTopologyId);

		[DllImport("user32.dll")]
		private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

		[DllImport("user32.dll")]
		private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EnumDisplayDevicesW(string device, uint devNum, ref DisplayDevice info, uint flags);

		/// <summary>One DisplayConfig path resolved down to the facts Keysharp exposes.</summary>
		private readonly record struct PathFacts(string FriendlyName, string DevicePath, string Connection,
			bool IsInternal, double RefreshRate, int Orientation)
		{
			/// <summary>The "DisplayConfig told us nothing" value. Deliberately not <c>default</c>: a default record
			/// struct leaves every string null, and the callers treat these as ordinary empty strings.</summary>
			internal static readonly PathFacts None = new("", "", "", false, 0.0, 0);
		}

		internal static DisplayDetails Get(DisplayInfo display)
		{
			// DisplayInfo.Name on Windows is Screen.DeviceName (\\.\DISPLAY1), which is exactly the GDI source
			// name DisplayConfig keys its source lookup by.
			var deviceName = display.Name ?? "";

			if (deviceName.Length == 0)
				return DisplayDetails.Empty;

			var facts = QueryPath(deviceName);
			var adapter = QueryAdapterName(deviceName);
			var edidBytes = ReadEdidFromRegistry(facts.DevicePath);
			string model = facts.FriendlyName, manufacturer = "", serial = "", stableId = "";
			int widthMm = 0, heightMm = 0;

			if (edidBytes != null && Edid.TryParse(edidBytes, out var edid))
			{
				if (edid.ModelName.Length > 0)
					model = edid.ModelName;

				manufacturer = edid.Manufacturer;
				serial = !string.IsNullOrEmpty(edid.SerialText) ? edid.SerialText
					: edid.SerialNumber != 0 ? edid.SerialNumber.ToString() : "";
				widthMm = edid.WidthMm;
				heightMm = edid.HeightMm;
				// Panels that report no serial need a per-port disambiguator, otherwise two identical monitors
				// collapse onto one id. The device path already encodes the adapter+connector instance.
				stableId = edid.KeyIsUnique ? edid.Key
					: edid.Key.Length > 0 ? $"{edid.Key}@{ConnectorInstanceOf(facts.DevicePath)}" : "";
			}

			// No EDID (virtual/remote displays, locked-down registry): the device path is still stable for as long
			// as the monitor stays on the same port, which beats reporting nothing.
			if (stableId.Length == 0 && facts.DevicePath.Length > 0)
				stableId = facts.DevicePath;

			return new DisplayDetails
			{
				Model = model ?? "",
				Manufacturer = manufacturer,
				Serial = serial,
				Adapter = adapter,
				Connection = facts.Connection,
				StableId = stableId,
				RefreshRate = facts.RefreshRate,
				PhysicalWidthMm = widthMm,
				PhysicalHeightMm = heightMm,
				Orientation = facts.Orientation,
				IsInternal = facts.IsInternal,
			};
		}

		/// <summary>Finds the active DisplayConfig path whose SOURCE is the given GDI device name, then reads its
		/// target's friendly name, device path, output technology, refresh rate and rotation.</summary>
		private static PathFacts QueryPath(string deviceName)
		{
			try
			{
				if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0
					|| pathCount == 0)
					return PathFacts.None;

				var paths = new PathInfo[pathCount];
				var modes = new ModeInfo[modeCount];

				if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0)
					return PathFacts.None;

				for (var i = 0; i < pathCount; i++)
				{
					var source = new SourceDeviceName
					{
						Header = new DeviceInfoHeader
						{
							Type = DeviceInfoGetSourceName,
							Size = (uint)Marshal.SizeOf<SourceDeviceName>(),
							AdapterId = paths[i].SourceInfo.AdapterId,
							Id = paths[i].SourceInfo.Id,
						}
					};

					if (DisplayConfigGetDeviceInfo(ref source) != 0
						|| !string.Equals(source.ViewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
						continue;

					var target = new TargetDeviceName
					{
						Header = new DeviceInfoHeader
						{
							Type = DeviceInfoGetTargetName,
							Size = (uint)Marshal.SizeOf<TargetDeviceName>(),
							AdapterId = paths[i].TargetInfo.AdapterId,
							Id = paths[i].TargetInfo.Id,
						}
					};

					var technology = paths[i].TargetInfo.OutputTechnology;
					var friendly = "";
					var devicePath = "";

					if (DisplayConfigGetDeviceInfo(ref target) == 0)
					{
						friendly = target.MonitorFriendlyDeviceName ?? "";
						devicePath = target.MonitorDevicePath ?? "";
						technology = target.OutputTechnology;
					}

					var refresh = paths[i].TargetInfo.RefreshRate.Denominator != 0
						? paths[i].TargetInfo.RefreshRate.Numerator
							/ (double)paths[i].TargetInfo.RefreshRate.Denominator
						: 0.0;
					return new PathFacts(friendly, devicePath, ConnectionOf(technology), IsInternalTechnology(technology),
						refresh, RotationDegrees(paths[i].TargetInfo.Rotation));
				}
			}
			catch (DllNotFoundException) { }
			catch (EntryPointNotFoundException) { }

			return PathFacts.None;
		}

		/// <summary>The adapter's marketing name, from the display-adapter level of EnumDisplayDevices.</summary>
		private static string QueryAdapterName(string deviceName)
		{
			try
			{
				var device = new DisplayDevice { Cb = (uint)Marshal.SizeOf<DisplayDevice>() };

				for (uint i = 0; EnumDisplayDevicesW(null, i, ref device, 0); i++)
				{
					if (string.Equals(device.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
						return device.DeviceString ?? "";

					device = new DisplayDevice { Cb = (uint)Marshal.SizeOf<DisplayDevice>() };
				}
			}
			catch (DllNotFoundException) { }
			catch (EntryPointNotFoundException) { }

			return "";
		}

		/// <summary>
		/// Reads the cached EDID blob Windows stores under the monitor's PnP enumerator key. The DisplayConfig
		/// device path (<c>\\?\DISPLAY#DEL41C1#5&amp;1a2b3c&amp;0&amp;UID4353#{GUID}</c>) maps to the registry path
		/// <c>SYSTEM\CurrentControlSet\Enum\DISPLAY\DEL41C1\5&amp;1a2b3c&amp;0&amp;UID4353</c> by dropping the
		/// <c>\\?\</c> prefix and the interface GUID, then turning <c>#</c> into <c>\</c>.
		/// </summary>
		private static byte[] ReadEdidFromRegistry(string devicePath)
		{
			if (string.IsNullOrEmpty(devicePath) || !devicePath.StartsWith(@"\\?\", StringComparison.Ordinal))
				return null;

			var trimmed = devicePath[4..];
			var guid = trimmed.IndexOf('{');

			if (guid > 0)
				trimmed = trimmed[..guid].TrimEnd('#');

			var instance = trimmed.Replace('#', '\\');

			try
			{
				using var key = Registry.LocalMachine.OpenSubKey(
					$@"SYSTEM\CurrentControlSet\Enum\{instance}\Device Parameters");
				return key?.GetValue("EDID") as byte[];
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException
				or IOException)
			{
				return null;
			}
		}

		/// <summary>The connector instance out of a device path, used to separate two identical panels that report
		/// the same EDID serial.</summary>
		private static string ConnectorInstanceOf(string devicePath)
		{
			if (string.IsNullOrEmpty(devicePath))
				return "";

			var parts = devicePath.Split('#');
			return parts.Length >= 3 ? parts[2] : devicePath;
		}

		private static string ConnectionOf(int technology) => technology switch
		{
			OutputTechnologyHd15 => "VGA",
			OutputTechnologyDvi => "DVI",
			OutputTechnologyHdmi => "HDMI",
			OutputTechnologyLvds => "Internal",
			OutputTechnologyDisplayPortExternal or OutputTechnologyUdiExternal => "DisplayPort",
			OutputTechnologyDisplayPortEmbedded or OutputTechnologyUdiEmbedded => "eDP",
			OutputTechnologyInternal => "Internal",
			_ => "",
		};

		private static bool IsInternalTechnology(int technology) => technology
			is OutputTechnologyInternal or OutputTechnologyLvds
			or OutputTechnologyDisplayPortEmbedded or OutputTechnologyUdiEmbedded;

		// DISPLAYCONFIG_ROTATION: 1 identity, 2 rotate90, 3 rotate180, 4 rotate270.
		private static int RotationDegrees(int rotation) => rotation switch
		{
			2 => 90,
			3 => 180,
			4 => 270,
			_ => 0,
		};
	}
#elif LINUX
	/// <summary>
	/// Linux display metadata. Identity, model and physical size come from the DRM connector's EDID under
	/// <c>/sys/class/drm/*/edid</c> — world-readable, present on every KMS driver, and identical under X11 and
	/// Wayland, so one implementation serves every session type. Refresh rate comes from the session-specific
	/// topology source (XRandR or wl_output), which the caller supplies because only it knows the session shape.
	/// </summary>
	internal static class LinuxMonitorDetails
	{
		private const string DrmRoot = "/sys/class/drm";

		/// <summary>
		/// Builds the details for one display. <paramref name="refreshRate"/> and <paramref name="orientation"/>
		/// come from the caller's session-specific topology (XRandR / wl_output); everything else is read here.
		/// </summary>
		internal static DisplayDetails Get(DisplayInfo display, double refreshRate, int orientation)
		{
			var connector = FindConnector(display.Name);
			var connection = Edid.ConnectionFromConnectorName(connector.Length > 0 ? connector : display.Name);
			string model = "", manufacturer = "", serial = "", stableId = "";
			int widthMm = 0, heightMm = 0;
			var edidBytes = ReadEdid(connector);

			if (edidBytes != null && Edid.TryParse(edidBytes, out var edid))
			{
				model = edid.ModelName;
				manufacturer = edid.Manufacturer;
				serial = !string.IsNullOrEmpty(edid.SerialText) ? edid.SerialText
					: edid.SerialNumber != 0 ? edid.SerialNumber.ToString() : "";
				widthMm = edid.WidthMm;
				heightMm = edid.HeightMm;
				// Identical panels with no serial are separated by connector, making the id stable per port.
				stableId = edid.KeyIsUnique ? edid.Key
					: edid.Key.Length > 0 ? $"{edid.Key}@{connector}" : "";
			}

			if (stableId.Length == 0 && connector.Length > 0)
				stableId = connector;

			return new DisplayDetails
			{
				Model = model,
				Manufacturer = manufacturer,
				Serial = serial,
				Adapter = ReadAdapterName(connector),
				Connection = connection,
				StableId = stableId,
				RefreshRate = refreshRate,
				PhysicalWidthMm = widthMm,
				PhysicalHeightMm = heightMm,
				Orientation = orientation,
				IsInternal = Edid.IsInternalConnection(connection),
			};
		}

		/// <summary>
		/// Maps an output name as the session reports it ("DP-1", "HDMI-A-2") to the DRM connector directory
		/// ("card0-DP-1"). Only connected connectors are considered, so a disconnected port with a matching
		/// suffix cannot shadow the live one.
		/// </summary>
		internal static string FindConnector(string outputName)
		{
			if (string.IsNullOrWhiteSpace(outputName))
				return "";

			try
			{
				if (!Directory.Exists(DrmRoot))
					return "";

				foreach (var dir in Directory.EnumerateDirectories(DrmRoot))
				{
					var name = Path.GetFileName(dir);
					var dash = name.IndexOf('-');

					if (dash <= 0 || !name.StartsWith("card", StringComparison.Ordinal))
						continue;

					if (!string.Equals(name[(dash + 1)..], outputName, StringComparison.OrdinalIgnoreCase))
						continue;

					if (ReadTrimmed(Path.Combine(dir, "status")) is "connected")
						return name;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return "";
		}

		private static byte[] ReadEdid(string connector)
		{
			if (connector.Length == 0)
				return null;

			try
			{
				var path = Path.Combine(DrmRoot, connector, "edid");
				// A disconnected or still-probing connector exposes a zero-length edid file.
				return File.Exists(path) && new FileInfo(path).Length >= Edid.BlockSize ? File.ReadAllBytes(path) : null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}
		}

		/// <summary>The DRM driver behind a connector ("amdgpu", "i915", "nvidia") — the closest portable analogue
		/// of the Windows adapter string, since Linux exposes no marketing name for the GPU.</summary>
		private static string ReadAdapterName(string connector)
		{
			if (connector.Length == 0)
				return "";

			try
			{
				// /sys/class/drm/card0-DP-1/device is the PCI device; its driver entry is a symlink whose TARGET
				// directory is named after the module. Only the resolved link says anything: the literal path ends
				// in "driver", so falling back to it would report the adapter as "driver".
				var driver = Path.Combine(DrmRoot, connector, "device", "driver");
				var target = Directory.ResolveLinkTarget(driver, true);
				return target != null ? Path.GetFileName(target.FullName.TrimEnd(Path.DirectorySeparatorChar)) : "";
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return "";
			}
		}

		private static string ReadTrimmed(string path)
		{
			try
			{
				return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return "";
			}
		}
	}
#elif OSX
	/// <summary>
	/// The display name macOS itself shows. <c>NSScreen.localizedName</c> is a 10.15 API that MonoMac's bindings
	/// predate, so it is sent as a raw selector through libobjc and read back through CoreFoundation (NSString and
	/// CFString are toll-free bridged). Going through the C ABI rather than MonoMac's managed messaging helpers
	/// keeps this path off binding surface that varies between MonoMac/Xamarin.Mac versions — the same reasoning
	/// <c>MacNativeWindows</c> applies to <c>ActivateIgnoringOtherApps</c>. Anything unexpected yields "" and the
	/// caller falls back.
	/// </summary>
	internal static class MacScreenNames
	{
		private const string ObjC = "/usr/lib/libobjc.A.dylib";
		private const string CoreFoundation =
			"/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		[DllImport(ObjC, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
		private static extern nint SelRegisterName(string name);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern nint MsgSend(nint receiver, nint selector);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool MsgSendRespondsTo(nint receiver, nint selector, nint argument);

		[DllImport(CoreFoundation)]
		private static extern nint CFStringGetLength(nint theString);

		[DllImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool CFStringGetCString(nint theString, byte[] buffer, nint bufferSize, uint encoding);

		private const uint EncodingUtf8 = 0x08000100;

		private static readonly nint localizedName = SelRegisterName("localizedName");
		private static readonly nint respondsToSelector = SelRegisterName("respondsToSelector:");

		internal static string LocalizedName(Forms.Screen screen)
		{
			try
			{
				// Eto's Cocoa screen handler wraps an NSScreen; Handle is the ObjC object the selector goes to.
				if (screen?.ControlObject is not MonoMac.Foundation.NSObject native || native.Handle == 0)
					return "";

				if (!MsgSendRespondsTo(native.Handle, respondsToSelector, localizedName))
					return "";

				var value = MsgSend(native.Handle, localizedName);

				if (value == 0)
					return "";

				// CFStringGetLength is in UTF-16 code units; UTF-8 can need up to 3 bytes each, plus the NUL.
				var buffer = new byte[(int)CFStringGetLength(value) * 3 + 1];
				return CFStringGetCString(value, buffer, buffer.Length, EncodingUtf8)
					? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0')
					: "";
			}
			catch
			{
				return "";
			}
		}
	}

	/// <summary>
	/// macOS display metadata. CoreGraphics answers identity, physical size, rotation and refresh rate directly,
	/// so no EDID parsing is involved. Every call here is CoreGraphics (thread-safe), not AppKit, so it is usable
	/// off the main thread like the rest of <c>MacNativeWindows</c>' display queries.
	/// </summary>
	internal static class MacMonitorDetails
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct CGSize
		{
			internal double Width;
			internal double Height;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CGRect
		{
			internal double X, Y, Width, Height;
		}

		private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

		[DllImport(CoreGraphics)]
		private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

		[DllImport(CoreGraphics)]
		private static extern CGRect CGDisplayBounds(uint display);

		[DllImport(CoreGraphics)]
		private static extern CGSize CGDisplayScreenSize(uint display);

		[DllImport(CoreGraphics)]
		private static extern uint CGDisplayVendorNumber(uint display);

		[DllImport(CoreGraphics)]
		private static extern uint CGDisplayModelNumber(uint display);

		[DllImport(CoreGraphics)]
		private static extern uint CGDisplaySerialNumber(uint display);

		[DllImport(CoreGraphics)]
		private static extern double CGDisplayRotation(uint display);

		[DllImport(CoreGraphics)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool CGDisplayIsBuiltin(uint display);

		[DllImport(CoreGraphics)]
		private static extern nint CGDisplayCopyDisplayMode(uint display);

		[DllImport(CoreGraphics)]
		private static extern double CGDisplayModeGetRefreshRate(nint mode);

		[DllImport(CoreGraphics)]
		private static extern void CGDisplayModeRelease(nint mode);

		internal static DisplayDetails Get(DisplayInfo display)
		{
			if (!TryResolveDisplayId(display, out var id))
				return DisplayDetails.Empty;

			try
			{
				var size = CGDisplayScreenSize(id);
				var vendor = CGDisplayVendorNumber(id);
				var model = CGDisplayModelNumber(id);
				var serial = CGDisplaySerialNumber(id);
				var builtin = CGDisplayIsBuiltin(id);
				var refresh = 0.0;
				var mode = CGDisplayCopyDisplayMode(id);

				if (mode != 0)
				{
					refresh = CGDisplayModeGetRefreshRate(mode);
					CGDisplayModeRelease(mode);
				}

				// CoreGraphics reports the EDID vendor id as a packed PNP code, so decoding it with the EDID
				// parser's own routine is what makes the same panel report the same manufacturer on every platform.
				var manufacturer = Edid.DecodeManufacturer((ushort)vendor);
				var key = manufacturer.Length > 0 ? $"{manufacturer}{model:X4}" : $"CG{vendor:X8}{model:X4}";

				return new DisplayDetails
				{
					// CoreGraphics has no product-name API; the script-facing Name carries NSScreen's localized
					// name and Model stays empty rather than repeating a number as if it were a model string.
					Model = "",
					Manufacturer = manufacturer,
					Serial = serial != 0 ? serial.ToString() : "",
					Adapter = "",
					Connection = builtin ? "Internal" : "",
					// With no serial there is nothing per-unit to key on: CoreGraphics exposes no port identity,
					// and the display id is a per-SESSION handle that changes across reboots. Degrade to the
					// per-model key — which stays stable, and only collides between two identical panels — rather
					// than to an id that would silently stop matching after every restart.
					StableId = serial != 0 ? $"{key}-{serial:X8}" : key,
					RefreshRate = refresh,
					PhysicalWidthMm = (int)Math.Round(size.Width),
					PhysicalHeightMm = (int)Math.Round(size.Height),
					Orientation = ((int)Math.Round(CGDisplayRotation(id)) % 360 + 360) % 360,
					IsInternal = builtin,
				};
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return DisplayDetails.Empty;
			}
		}

		/// <summary>
		/// Finds the CGDirectDisplayID whose bounds match this display. Matching on geometry rather than asking
		/// AppKit for NSScreenNumber keeps the lookup on the thread-safe CoreGraphics path.
		/// </summary>
		internal static bool TryResolveDisplayId(DisplayInfo display, out uint id)
		{
			id = 0;

			try
			{
				var ids = new uint[16];

				if (CGGetActiveDisplayList((uint)ids.Length, ids, out var count) != 0 || count == 0)
					return false;

				var best = -1;
				var bestError = double.MaxValue;

				for (var i = 0; i < count && i < ids.Length; i++)
				{
					var bounds = CGDisplayBounds(ids[i]);
					var error = Math.Abs(bounds.X - display.Bounds.X) + Math.Abs(bounds.Y - display.Bounds.Y)
						+ Math.Abs(bounds.Width - display.Bounds.Width) + Math.Abs(bounds.Height - display.Bounds.Height);

					if (error < bestError)
					{
						best = i;
						bestError = error;
					}
				}

				// A few points of slack absorbs point-vs-pixel rounding; anything further apart is a different
				// display and must not be silently substituted.
				if (best < 0 || bestError > 4.0)
					return false;

				id = ids[best];
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}
	}
#endif
}
