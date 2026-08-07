#if LINUX
using Tmds.DBus;
#endif

namespace Keysharp.Internals
{
	// Monitor DEVICE control: brightness and raw DDC/CI VCP features, one implementation per OS.
	//
	// None of these implementations cache a native monitor handle. That is deliberate rather than lazy: a DDC/CI
	// transaction costs 40-200 ms all by itself, so opening the handle around it is noise, while a cached handle
	// would need an invalidation signal on display hotplug that Keysharp does not have yet. Per-call open/close is
	// the correct trade until a display-change event exists to invalidate against.

#if WINDOWS
	/// <summary>
	/// Windows brightness and VCP control. External displays go through DDC/CI (dxva2), the built-in laptop panel
	/// through WMI — which is a per-PANEL interface, not a per-monitor one, so it is used only for the display
	/// DisplayConfig reports as internal instead of being silently retargeted at whatever monitor was asked for.
	/// </summary>
	internal sealed class WindowsMonitorControl : IMonitorControl
	{
		private const uint MonitorDefaultToNearest = 2;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct PhysicalMonitor
		{
			internal nint Handle;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
		}

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count,
			[Out] PhysicalMonitor[] monitors);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetMonitorBrightness(nint physicalMonitor, out uint minimum, out uint current,
			out uint maximum);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetMonitorBrightness(nint physicalMonitor, uint brightness);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetVCPFeatureAndVCPFeatureReply(nint physicalMonitor, byte code,
			nint codeType, out uint current, out uint maximum);

		[DllImport("dxva2.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetVCPFeature(nint physicalMonitor, byte code, uint value);

		public bool TryGetBrightness(DisplayInfo display, DisplayDetails details, out int percent)
		{
			percent = 0;

			if (details.IsInternal && WindowsPanelBrightness.TryGet(out var wmi))
			{
				percent = wmi;
				return true;
			}

			var found = false;
			var value = 0;
			WithPhysicalMonitor(display, handle =>
			{
				if (!GetMonitorBrightness(handle, out var min, out var current, out var max) || max <= min)
					return;

				value = ToPercent(current, min, max);
				found = true;
			});
			percent = value;
			return found;
		}

		public bool TrySetBrightness(DisplayInfo display, DisplayDetails details, int percent)
		{
			percent = Math.Clamp(percent, 0, 100);

			if (details.IsInternal && WindowsPanelBrightness.TrySet(percent))
				return true;

			var applied = false;
			WithPhysicalMonitor(display, handle =>
			{
				if (!GetMonitorBrightness(handle, out var min, out var _, out var max) || max <= min)
					return;

				applied = SetMonitorBrightness(handle, FromPercent(percent, min, max));
			});
			return applied;
		}

		public bool TryGetVcp(DisplayInfo display, DisplayDetails details, byte code, out int current, out int max)
		{
			var gotCurrent = 0;
			var gotMax = 0;
			var found = false;
			WithPhysicalMonitor(display, handle =>
			{
				// A null code-type pointer is allowed and means "don't tell me whether it's a set or momentary code".
				if (!GetVCPFeatureAndVCPFeatureReply(handle, code, 0, out var value, out var maximum))
					return;

				gotCurrent = (int)value;
				gotMax = (int)maximum;
				found = true;
			});
			current = gotCurrent;
			max = gotMax;
			return found;
		}

		public bool TrySetVcp(DisplayInfo display, DisplayDetails details, byte code, int value)
		{
			var applied = false;
			WithPhysicalMonitor(display, handle => applied = SetVCPFeature(handle, code, (uint)Math.Max(0, value)));
			return applied;
		}

		// Phrased around the TRANSPORT rather than the operation, so it reads correctly appended to both
		// "could not read the brightness of ..." and "could not read VCP feature 0x.. from ...".
		public string UnsupportedReason(DisplayInfo display, DisplayDetails details)
			=> details.IsInternal
				? "this is a built-in panel - it has no DDC/CI connection, and the WMI backlight interface did not answer"
				: "the monitor did not respond to DDC/CI - check that it is enabled in the monitor's own on-screen menu (often called DDC/CI or MCCS)";

		/// <summary>
		/// Resolves the HMONITOR for a display, opens its physical monitors, runs <paramref name="action"/> against
		/// the first one, then destroys the handles. Everything is best-effort: an unreachable monitor leaves the
		/// action simply not invoked, which the callers report as "not supported for this display".
		/// </summary>
		private static void WithPhysicalMonitor(DisplayInfo display, Action<nint> action)
		{
			var rect = new RECT
			{
				Left = display.Bounds.X,
				Top = display.Bounds.Y,
				Right = (int)Math.Clamp(display.Bounds.Right, int.MinValue, int.MaxValue),
				Bottom = (int)Math.Clamp(display.Bounds.Bottom, int.MinValue, int.MaxValue),
			};
			var monitor = WindowsAPI.MonitorFromRect(ref rect, MonitorDefaultToNearest);

			if (monitor == 0)
				return;

			PhysicalMonitor[] monitors = null;

			try
			{
				if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
					return;

				monitors = new PhysicalMonitor[count];

				if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors))
					return;

				// One HMONITOR maps to several physical monitors only in a cloned/daisy-chained configuration; the
				// first is the one the desktop rectangle actually belongs to.
				if (monitors[0].Handle != 0)
					action(monitors[0].Handle);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
			}
			finally
			{
				if (monitors != null)
					try { _ = DestroyPhysicalMonitors((uint)monitors.Length, monitors); } catch { }
			}
		}

		private static int ToPercent(uint current, uint min, uint max)
			=> (int)Math.Round((current - (double)min) * 100.0 / (max - min));

		private static uint FromPercent(int percent, uint min, uint max)
			=> (uint)Math.Round(min + percent / 100.0 * (max - min));
	}

	/// <summary>
	/// The built-in laptop panel's backlight via WMI (<c>root\wmi</c>). This interface has no notion of WHICH
	/// monitor — there is one integrated panel per machine — so callers must only reach it for the display
	/// DisplayConfig identifies as internal.
	/// </summary>
	internal static class WindowsPanelBrightness
	{
		internal static bool TryGet(out int percent)
		{
			percent = 0;

			try
			{
				using var searcher = new ManagementObjectSearcher(@"root\wmi",
					"SELECT CurrentBrightness FROM WmiMonitorBrightness");

				foreach (var instance in searcher.Get())
					using (instance)
					{
						percent = Convert.ToInt32(instance["CurrentBrightness"]);
						return true;
					}
			}
			catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
				or System.Runtime.InteropServices.COMException)
			{
			}

			return false;
		}

		internal static bool TrySet(int percent)
		{
			try
			{
				using var searcher = new ManagementObjectSearcher(@"root\wmi",
					"SELECT * FROM WmiMonitorBrightnessMethods");

				foreach (var instance in searcher.Get())
					using (var method = (ManagementObject)instance)
					{
						// WmiSetBrightness(Timeout, Brightness); a zero timeout means "apply and do not revert".
						_ = method.InvokeMethod("WmiSetBrightness", [(uint)0, (byte)Math.Clamp(percent, 0, 100)]);
						return true;
					}
			}
			catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException
				or System.Runtime.InteropServices.COMException)
			{
			}

			return false;
		}
	}
#elif LINUX
#pragma warning disable IDE1006 // D-Bus member names are case-sensitive.
	/// <summary>
	/// logind's session interface. <c>SetBrightness</c> is the supported unprivileged way to change a backlight:
	/// logind performs the write as root on behalf of the session owner, so no udev rule or group membership is
	/// needed. There is no matching getter — the current value is read straight from sysfs, which is world-readable.
	/// </summary>
	[DBusInterface("org.freedesktop.login1.Session")]
	public interface ILogindSession : IDBusObject
	{
		Task SetBrightnessAsync(string subsystem, string name, uint brightness);
	}
#pragma warning restore IDE1006

	/// <summary>
	/// Linux brightness and VCP control. The built-in panel goes through the kernel backlight class (a direct
	/// sysfs write where permissions allow, else logind's SetBrightness); external monitors go through DDC/CI on
	/// the connector's i2c bus. Both mechanisms are kernel/session facilities, identical under X11 and every
	/// Wayland compositor, which is why this service has no per-compositor variation.
	/// </summary>
	internal sealed class LinuxMonitorControl : IMonitorControl
	{
		private const string BacklightRoot = "/sys/class/backlight";

		public bool TryGetBrightness(DisplayInfo display, DisplayDetails details, out int percent)
		{
			percent = 0;

			// Only the display the connector identifies as built-in may use the backlight class: like WMI on
			// Windows it is a per-PANEL interface with no notion of WHICH monitor, so reaching it for anything
			// else would silently read (and, on the setter, change) the laptop screen instead of the monitor
			// that was asked for.
			if (details.IsInternal)
			{
				var device = FindBacklightDevice();

				if (device.Length > 0 && TryReadBacklight(device, out var current, out var max) && max > 0)
				{
					percent = (int)Math.Round(current * 100.0 / max);
					return true;
				}
			}

			if (LinuxDdc.TryGetVcp(display.Name, LinuxDdc.VcpBrightness, out var value, out var maximum)
				&& maximum > 0)
			{
				percent = (int)Math.Round(value * 100.0 / maximum);
				return true;
			}

			return false;
		}

		public bool TrySetBrightness(DisplayInfo display, DisplayDetails details, int percent)
		{
			percent = Math.Clamp(percent, 0, 100);

			// Per-panel interface; see the note in TryGetBrightness.
			if (details.IsInternal)
			{
				var device = FindBacklightDevice();

				if (device.Length > 0 && TryReadBacklight(device, out _, out var max) && max > 0)
				{
					var raw = (int)Math.Round(percent / 100.0 * max);

					if (TryWriteBacklight(device, raw))
						return true;
				}
			}

			// The percentage has to be scaled by the monitor's own maximum, so a set costs a read first. Both
			// halves reuse one open bus and one settle delay rather than paying for two full transactions.
			return LinuxDdc.TrySetVcpScaled(display.Name, LinuxDdc.VcpBrightness, percent / 100.0);
		}

		public bool TryGetVcp(DisplayInfo display, DisplayDetails details, byte code, out int current, out int max)
			=> LinuxDdc.TryGetVcp(display.Name, code, out current, out max);

		public bool TrySetVcp(DisplayInfo display, DisplayDetails details, byte code, int value)
			=> LinuxDdc.TrySetVcp(display.Name, code, value);

		public string UnsupportedReason(DisplayInfo display, DisplayDetails details)
		{
			if (details.IsInternal)
				return $"no writable backlight device was found under {BacklightRoot} and logind refused the write";

			var bus = LinuxDdc.FindBus(display.Name);

			if (bus.Length == 0)
				return "no DDC/CI i2c bus was found for this connector - load the i2c-dev module (modprobe i2c-dev)";

			return $"{bus} could not be opened - add your user to the 'i2c' group, or install a udev rule granting "
				+ "access to the DDC/CI bus, then log out and back in";
		}

		/// <summary>The first backlight device the kernel exposes. Laptops have exactly one; the "raw" ACPI/GPU
		/// devices and the firmware ones all live here and any of them drives the same panel.</summary>
		private static string FindBacklightDevice()
		{
			try
			{
				if (!Directory.Exists(BacklightRoot))
					return "";

				foreach (var dir in Directory.EnumerateDirectories(BacklightRoot).OrderBy(d => d, StringComparer.Ordinal))
					if (File.Exists(Path.Combine(dir, "max_brightness")))
						return Path.GetFileName(dir);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return "";
		}

		private static bool TryReadBacklight(string device, out int current, out int max)
		{
			current = max = 0;

			try
			{
				var dir = Path.Combine(BacklightRoot, device);
				return int.TryParse(File.ReadAllText(Path.Combine(dir, "brightness")).Trim(), out current)
					&& int.TryParse(File.ReadAllText(Path.Combine(dir, "max_brightness")).Trim(), out max);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
			{
				return false;
			}
		}

		/// <summary>Direct sysfs write where the distro's udev rules allow it (the common case on desktops that
		/// put the user in the <c>video</c> group), otherwise logind performs the write for us.</summary>
		private static bool TryWriteBacklight(string device, int raw)
		{
			try
			{
				File.WriteAllText(Path.Combine(BacklightRoot, device, "brightness"),
					raw.ToString(CultureInfo.InvariantCulture));
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return TrySetBrightnessViaLogind("backlight", device, raw);
		}

		private static bool TrySetBrightnessViaLogind(string subsystem, string device, int raw)
		{
			Connection connection = null;

			try
			{
				// logind lives on the SYSTEM bus (the rest of Keysharp's D-Bus use is session-bus). "session/self"
				// resolves to the caller's own session, so no session id lookup is needed.
				connection = new Connection(Tmds.DBus.Address.System);
				connection.ConnectAsync().GetAwaiter().GetResult();
				var session = connection.CreateProxy<ILogindSession>("org.freedesktop.login1",
					new ObjectPath("/org/freedesktop/login1/session/self"));
				session.SetBrightnessAsync(subsystem, device, (uint)Math.Max(0, raw)).GetAwaiter().GetResult();
				return true;
			}
			catch
			{
				return false;
			}
			finally
			{
				connection?.Dispose();
			}
		}
	}

	/// <summary>
	/// DDC/CI over the connector's i2c bus — the same mechanism ddcutil uses, without requiring it to be
	/// installed. The monitor is a slave at address 0x37; each transaction is a length-prefixed, checksummed
	/// packet, and the standard mandates a settle delay between them.
	/// </summary>
	internal static class LinuxDdc
	{
		internal const byte VcpBrightness = 0x10;

		private const int I2cSlave = 0x0703;      // ioctl: set slave address
		private const byte SlaveAddress = 0x37;   // DDC/CI
		private const byte SourceAddress = 0x51;
		private const int OpenReadWrite = 2;
		private const int SettleMs = 50;          // DDC/CI requires >=40 ms between transactions

		[DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
		private static extern int Open(string path, int flags);

		[DllImport("libc", EntryPoint = "close", SetLastError = true)]
		private static extern int Close(int fd);

		[DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
		private static extern int Ioctl(int fd, nuint request, nuint value);

		[DllImport("libc", EntryPoint = "write", SetLastError = true)]
		private static extern nint Write(int fd, byte[] buffer, nuint count);

		[DllImport("libc", EntryPoint = "read", SetLastError = true)]
		private static extern nint Read(int fd, byte[] buffer, nuint count);

		/// <summary>
		/// The i2c bus node that carries this connector's DDC channel. The kernel exposes it as an
		/// <c>i2c-N</c> entry under the DRM connector directory (newer kernels nest it under <c>ddc/i2c-dev</c>).
		/// </summary>
		internal static string FindBus(string outputName)
		{
			var connector = LinuxMonitorDetails.FindConnector(outputName);

			if (connector.Length == 0)
				return "";

			try
			{
				var connectorDir = Path.Combine("/sys/class/drm", connector);
				// Newer kernels: /sys/class/drm/card0-DP-1/ddc/i2c-dev/i2c-5
				var ddcDir = Path.Combine(connectorDir, "ddc", "i2c-dev");

				if (Directory.Exists(ddcDir))
					foreach (var dir in Directory.EnumerateDirectories(ddcDir))
						if (Path.GetFileName(dir).StartsWith("i2c-", StringComparison.Ordinal))
							return $"/dev/{Path.GetFileName(dir)}";

				// Older kernels: /sys/class/drm/card0-DP-1/i2c-5
				if (Directory.Exists(connectorDir))
					foreach (var dir in Directory.EnumerateDirectories(connectorDir))
						if (Path.GetFileName(dir).StartsWith("i2c-", StringComparison.Ordinal))
							return $"/dev/{Path.GetFileName(dir)}";
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return "";
		}

		internal static bool TryGetVcp(string outputName, byte code, out int current, out int max)
		{
			int gotCurrent = 0, gotMax = 0;
			var ok = WithBus(outputName, fd => ReadFeature(fd, code, out gotCurrent, out gotMax));
			current = gotCurrent;
			max = gotMax;
			return ok;
		}

		internal static bool TrySetVcp(string outputName, byte code, int value)
			=> WithBus(outputName, fd => WriteFeature(fd, code, value));

		/// <summary>
		/// Sets a feature to a fraction (0..1) of the value the monitor reports as its maximum. Scaling needs the
		/// maximum, so this is a read followed by a write, sequenced here on ONE open bus. Note this is not a
		/// speed-up over issuing the two separately — it adds the settle delay the standard requires BETWEEN
		/// transactions, which a read and a write issued independently would skip.
		/// </summary>
		internal static bool TrySetVcpScaled(string outputName, byte code, double fraction)
			=> WithBus(outputName, fd =>
			{
				if (!ReadFeature(fd, code, out _, out var max) || max <= 0)
					return false;

				Thread.Sleep(SettleMs);
				return WriteFeature(fd, code, (int)Math.Round(Math.Clamp(fraction, 0.0, 1.0) * max));
			});

		/// <summary>Opens the connector's i2c bus, runs one or more DDC transactions on it, and closes it.</summary>
		private static bool WithBus(string outputName, Func<int, bool> transaction)
		{
			var bus = FindBus(outputName);

			if (bus.Length == 0)
				return false;

			var fd = -1;

			try
			{
				fd = OpenBus(bus);
				return fd >= 0 && transaction(fd);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
			finally
			{
				if (fd >= 0)
					_ = Close(fd);
			}
		}

		private static bool ReadFeature(int fd, byte code, out int current, out int max)
		{
			current = max = 0;

			// Get VCP feature: opcode 0x01 followed by the VCP code.
			if (!SendPacket(fd, [0x01, code]))
				return false;

			Thread.Sleep(SettleMs);
			var reply = new byte[12];

			if (Read(fd, reply, (nuint)reply.Length) < 11)
				return false;

			// Reply: [dest, 0x88, 0x02, result, code, type, maxHi, maxLo, curHi, curLo, checksum].
			// A non-zero result byte means the monitor understood the request but does not support the code.
			if (reply[1] != 0x88 || reply[2] != 0x02 || reply[3] != 0x00 || reply[4] != code)
				return false;

			max = (reply[6] << 8) | reply[7];
			current = (reply[8] << 8) | reply[9];
			return true;
		}

		private static bool WriteFeature(int fd, byte code, int value)
		{
			var clamped = Math.Clamp(value, 0, ushort.MaxValue);
			// Set VCP feature: opcode 0x03, VCP code, then the value big-endian.
			var ok = SendPacket(fd, [0x03, code, (byte)(clamped >> 8), (byte)(clamped & 0xFF)]);
			Thread.Sleep(SettleMs);
			return ok;
		}

		private static int OpenBus(string bus)
		{
			var fd = Open(bus, OpenReadWrite);

			if (fd < 0)
				return -1;

			if (Ioctl(fd, I2cSlave, SlaveAddress) < 0)
			{
				_ = Close(fd);
				return -1;
			}

			return fd;
		}

		/// <summary>
		/// Frames and writes one DDC/CI packet: source address, length byte (0x80 | payload length), the payload,
		/// then an XOR checksum seeded with the destination address as the standard specifies.
		/// </summary>
		private static bool SendPacket(int fd, ReadOnlySpan<byte> payload)
		{
			var packet = new byte[payload.Length + 3];
			packet[0] = SourceAddress;
			packet[1] = (byte)(0x80 | payload.Length);

			for (var i = 0; i < payload.Length; i++)
				packet[i + 2] = payload[i];

			// The checksum is computed as if the destination address (0x6E) had been the first byte on the wire,
			// even though the i2c layer supplies it from the slave address.
			byte checksum = 0x6E;

			for (var i = 0; i < packet.Length - 1; i++)
				checksum ^= packet[i];

			packet[^1] = checksum;
			return Write(fd, packet, (nuint)packet.Length) == packet.Length;
		}
	}
#elif OSX
	/// <summary>
	/// macOS brightness. The built-in panel (and Apple's own displays) go through the private DisplayServices
	/// framework, which is what every macOS brightness utility ends up using because there is no public API.
	/// External displays are NOT supported: DDC/CI on macOS needs IOAVService, which is private, undocumented and
	/// different on Apple Silicon versus Intel — so this reports an honest failure instead of pretending.
	/// </summary>
	internal sealed class MacMonitorControl : IMonitorControl
	{
		private const string DisplayServices =
			"/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices";

		[DllImport(DisplayServices)]
		private static extern int DisplayServicesGetBrightness(uint display, out float brightness);

		[DllImport(DisplayServices)]
		private static extern int DisplayServicesSetBrightness(uint display, float brightness);

		public bool TryGetBrightness(DisplayInfo display, DisplayDetails details, out int percent)
		{
			percent = 0;

			if (!MacMonitorDetails.TryResolveDisplayId(display, out var id))
				return false;

			try
			{
				if (DisplayServicesGetBrightness(id, out var brightness) != 0)
					return false;

				percent = (int)Math.Round(Math.Clamp(brightness, 0f, 1f) * 100);
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		public bool TrySetBrightness(DisplayInfo display, DisplayDetails details, int percent)
		{
			if (!MacMonitorDetails.TryResolveDisplayId(display, out var id))
				return false;

			try
			{
				return DisplayServicesSetBrightness(id, Math.Clamp(percent, 0, 100) / 100f) == 0;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		public bool TryGetVcp(DisplayInfo display, DisplayDetails details, byte code, out int current, out int max)
		{
			current = max = 0;
			return false;
		}

		public bool TrySetVcp(DisplayInfo display, DisplayDetails details, byte code, int value) => false;

		public string UnsupportedReason(DisplayInfo display, DisplayDetails details)
			=> details.IsInternal
				? "the DisplayServices framework did not accept the request for the built-in display"
				: "macOS exposes no supported interface for controlling an external display's brightness or DDC/CI features";
	}
#endif
}
