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
			WithPhysicalMonitor(display, handle =>
			{
				// Read the feature first, on the handle that is already open: DDC/CI writes are unacknowledged, so
				// a monitor that does not implement this code would otherwise report a silent success. The read is
				// the only evidence the code exists.
				if (!GetVCPFeatureAndVCPFeatureReply(handle, code, 0, out _, out _))
					return;

				applied = SetVCPFeature(handle, code, (uint)Math.Clamp(value, 0, ushort.MaxValue));
			});
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

	/// <summary>logind's manager interface, used only to find the caller's own session by enumeration — see
	/// <see cref="LinuxMonitorControl.ResolveOwnSessionPath"/> for why <c>session/self</c> alone is not enough.</summary>
	[DBusInterface("org.freedesktop.login1.Manager")]
	public interface ILogindManager : IDBusObject
	{
		Task<(string Id, uint Uid, string UserName, string Seat, ObjectPath Path)[]> ListSessionsAsync();
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

			// Deliberately does NOT suggest the 'i2c' group: that grants every i2c bus, including the
			// motherboard SMBus ones (DIMM SPD, PMICs). The uaccess rule a root install adds
			// (70-keysharp-i2c-uaccess.rules, or ddcutil's equivalent 60-ddcutil-i2c.rules) is scoped to
			// display-controller buses and needs no group membership or re-login.
			return $"{bus} could not be opened - the DDC/CI uaccess udev rule is not installed or has not been "
				+ "applied yet; install Keysharp as root (or install ddcutil), then run "
				+ "'sudo udevadm trigger --subsystem-match=i2c-dev'";
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
				// logind lives on the SYSTEM bus (the rest of Keysharp's D-Bus use is session-bus).
				connection = new Connection(Tmds.DBus.Address.System);
				connection.ConnectAsync().GetAwaiter().GetResult();
				var path = ResolveOwnSessionPath(connection) ?? new ObjectPath("/org/freedesktop/login1/session/self");
				var session = connection.CreateProxy<ILogindSession>("org.freedesktop.login1", path);
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

		/// <summary>
		/// The current user's logind session, found by asking logind directly rather than trusting
		/// <c>session/self</c> to resolve. <c>self</c> only works when the CALLING PROCESS is itself tracked in the
		/// session's cgroup (<c>sd_pid_get_session</c> on the connecting PID) — true for a process descended
		/// straight from <c>gnome-session</c>, false for a great many perfectly normal ways of running a script:
		/// a detached/backgrounded process, a terminal or IDE that reparents its children outside the session
		/// scope, a remote-dev or containerized shell. In every one of those cases the desktop session itself is
		/// alive and its <c>SetBrightness</c> works fine — logind just cannot find it FROM the calling process, so
		/// this looks it up by username instead. A session with a seat (a real, locally-attached graphical/console
		/// login) is preferred over a seat-less manager/lingering session; failing that, any session for this user
		/// beats none.
		/// </summary>
		private static ObjectPath? ResolveOwnSessionPath(Connection connection)
		{
			try
			{
				var manager = connection.CreateProxy<ILogindManager>("org.freedesktop.login1",
					new ObjectPath("/org/freedesktop/login1"));
				var sessions = manager.ListSessionsAsync().GetAwaiter().GetResult();
				var user = Environment.UserName;
				ObjectPath? anyForUser = null;

				foreach (var s in sessions)
				{
					if (!string.Equals(s.UserName, user, StringComparison.Ordinal))
						continue;

					if (s.Seat.Length > 0)
						return s.Path;

					anyForUser ??= s.Path;
				}

				return anyForUser;
			}
			catch
			{
				return null;
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
		/// <c>i2c-N</c> entry under the DRM connector directory (newer kernels nest it under <c>ddc/i2c-dev</c>) —
		/// for a connector whose DDC goes through the classic GMBUS mux (HDMI, VGA, and DP on older/simpler
		/// GPU generations). A connector driven over the DisplayPort AUX channel instead (the common case for a
		/// native DP or USB-C/Thunderbolt external monitor on a modern Intel/AMD GPU) has neither: only a
		/// <c>drm_dp_auxN</c> companion device, with no symlink back to its i2c adapter at all.
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

				// DisplayPort AUX-channel DDC: the driver still registers the AUX transport as an ordinary i2c
				// adapter (DDC-over-AUX-CH, as the DP spec requires) — it is just not nested under the connector
				// directory. It is found instead by matching the connector's drm_dp_auxN device against the
				// system's i2c adapters on the one thing they share: the driver names both identically
				// (e.g. drm_dp_aux3's "name" is "AUX C/DDI C/PHY C" or "DPMST", exactly matching the "name" of
				// the i2c-dev adapter the same driver registered for that same AUX transport).
				if (Directory.Exists(connectorDir))
					foreach (var auxDir in Directory.EnumerateDirectories(connectorDir))
					{
						if (!Path.GetFileName(auxDir).StartsWith("drm_dp_aux", StringComparison.Ordinal))
							continue;

						var auxName = ReadTrimmed(Path.Combine(auxDir, "name"));

						if (auxName.Length == 0 || !Directory.Exists(I2cDevRoot))
							continue;

						foreach (var i2cDir in Directory.EnumerateDirectories(I2cDevRoot))
							if (ReadTrimmed(Path.Combine(i2cDir, "name")) == auxName)
								return $"/dev/{Path.GetFileName(i2cDir)}";
					}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			return "";
		}

		private const string I2cDevRoot = "/sys/class/i2c-dev";

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

		internal static bool TryGetVcp(string outputName, byte code, out int current, out int max)
		{
			int gotCurrent = 0, gotMax = 0;
			var ok = WithBus(outputName, fd => ReadFeature(fd, code, out gotCurrent, out gotMax));
			current = gotCurrent;
			max = gotMax;
			return ok;
		}

		/// <summary>
		/// Writes one feature, after reading it back on the same open bus to confirm the monitor implements it.
		/// A DDC/CI write is unacknowledged, so without the preceding read a code the display does not support
		/// would report success and do nothing.
		/// </summary>
		internal static bool TrySetVcp(string outputName, byte code, int value)
			=> WithBus(outputName, fd =>
			{
				if (!ReadFeature(fd, code, out _, out _))
					return false;

				Thread.Sleep(SettleMs);
				return WriteFeature(fd, code, value);
			});

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
	/// macOS brightness and VCP control. The built-in panel — and Apple's own external displays, which is why this
	/// is tried first for every display rather than only for internal ones — goes through the private
	/// DisplayServices framework, the only thing that drives an Apple backlight. Every other external display goes
	/// through DDC/CI, via <see cref="MacDdc"/>.
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

			if (TryGetDisplayServicesBrightness(display, out var value))
			{
				percent = value;
				return true;
			}

			// The built-in panel has no DDC channel, so there is nothing to fall back to; going further would only
			// mean an I2C round-trip that cannot succeed.
			if (details.IsInternal)
				return false;

			if (MacDdc.TryGetVcp(display, MacDdc.VcpBrightness, out var current, out var max) && max > 0)
			{
				percent = (int)Math.Round(current * 100.0 / max);
				return true;
			}

			return false;
		}

		public bool TrySetBrightness(DisplayInfo display, DisplayDetails details, int percent)
		{
			percent = Math.Clamp(percent, 0, 100);

			// DisplayServicesSetBrightness returns success for a display it cannot actually drive — it silently
			// does nothing on a third-party external monitor — so its return value cannot decide the transport.
			// The GETTER does fail honestly on those displays, so it is used as the capability probe instead;
			// without this the setter would report success and change nothing.
			if (TryGetDisplayServicesBrightness(display, out _)
				&& MacMonitorDetails.TryResolveDisplayId(display, out var id))
				try
				{
					if (DisplayServicesSetBrightness(id, percent / 100f) == 0)
						return true;
				}
				catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
				{
				}

			if (details.IsInternal)
				return false;

			// The percentage has to be scaled by the monitor's own maximum, so a set costs a read first; MacDdc
			// sequences both on one open transport with the settle delay the standard requires between them.
			return MacDdc.TrySetVcpScaled(display, MacDdc.VcpBrightness, percent / 100.0);
		}

		public bool TryGetVcp(DisplayInfo display, DisplayDetails details, byte code, out int current, out int max)
		{
			current = max = 0;
			// DisplayServices is a brightness-only interface with no notion of VCP, so unlike brightness there is
			// no non-DDC path here — not even for the built-in panel.
			return !details.IsInternal && MacDdc.TryGetVcp(display, code, out current, out max);
		}

		public bool TrySetVcp(DisplayInfo display, DisplayDetails details, byte code, int value)
			=> !details.IsInternal && MacDdc.TrySetVcp(display, code, value);

		public string UnsupportedReason(DisplayInfo display, DisplayDetails details)
		{
			if (details.IsInternal)
				return "this is the built-in panel - DisplayServices did not accept the request, and a built-in "
					+ "panel has no DDC/CI connection to fall back to";

			return MacDdc.TransportUnavailableReason(display)
				?? "the monitor did not respond to DDC/CI - check that it is enabled in the monitor's own "
					+ "on-screen menu (often called DDC/CI or MCCS)";
		}

		private static bool TryGetDisplayServicesBrightness(DisplayInfo display, out int percent)
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
	}

	/// <summary>
	/// DDC/CI for macOS. The packet layer is the same VESA framing <see cref="LinuxDdc"/> speaks — the monitor is a
	/// slave at 0x37, each transaction is length-prefixed and checksummed, and the standard mandates a settle delay
	/// — but macOS has no i2c-dev character device, so the bytes have to reach the wire through one of two very
	/// different transports, picked per display:
	/// <list type="bullet">
	/// <item><description><b>Apple Silicon</b> — <c>IOAVService</c>. Private and undocumented, and the only option:
	/// under the DCP display architecture <c>IOFramebuffer</c> does not exist at all, so the public I2C API below
	/// has nothing to attach to. This is the same transport m1ddc, MonitorControl and BetterDisplay use.</description></item>
	/// <item><description><b>Intel</b> — <c>IOFBCopyI2CInterfaceForBus</c> + <c>IOI2CSendRequest</c>, which are
	/// public, documented IOKit (<c>IOKit/i2c/IOI2CInterface.h</c>).</description></item>
	/// </list>
	/// <para>Both are attempted regardless of the process architecture rather than switched on
	/// <c>ProcessArchitecture</c>: that would answer the wrong question under Rosetta, where an x64 process is
	/// running on a machine that only has the Apple Silicon transport.</para>
	/// <para>No transport handle is cached, for the reason given at the top of this file: a DDC transaction costs
	/// far more than opening the handle around it, and a cached handle would need a display-hotplug invalidation
	/// signal to stay correct.</para>
	/// </summary>
	internal static class MacDdc
	{
		internal const byte VcpBrightness = 0x10;

		private const string IOKitFramework = "/System/Library/Frameworks/IOKit.framework/IOKit";
		private const string CoreFoundation =
			"/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

		private const byte SlaveAddress = 0x37;    // DDC/CI, 7-bit
		private const byte SourceAddress = 0x51;   // host, as it appears on the wire
		private const byte HostAddress = 0x6E;     // 8-bit write address; also the checksum seed
		private const int SettleMs = 40;           // DDC/CI requires >= 40 ms between transactions
		private const int ReplyLength = 12;        // an 11-byte VCP feature reply, plus slack

		// ---- public entry points -------------------------------------------------------------------------

		internal static bool TryGetVcp(DisplayInfo display, byte code, out int current, out int max)
		{
			int gotCurrent = 0, gotMax = 0;
			var ok = WithTransport(display, t => ReadFeature(t, code, out gotCurrent, out gotMax));
			current = gotCurrent;
			max = gotMax;
			return ok;
		}

		/// <summary>Writes one feature, after reading it back on the same open transport to confirm the monitor
		/// implements it. A DDC/CI write is unacknowledged, so without the preceding read a code the display does
		/// not support would report success and do nothing.</summary>
		internal static bool TrySetVcp(DisplayInfo display, byte code, int value)
			=> WithTransport(display, t =>
			{
				if (!ReadFeature(t, code, out _, out _))
					return false;

				Thread.Sleep(SettleMs);
				return WriteFeature(t, code, value);
			});

		/// <summary>Sets a feature to a fraction (0..1) of the monitor's own reported maximum. Scaling needs that
		/// maximum, so this is a read followed by a write, sequenced on ONE open transport so the settle delay
		/// between them is actually observed.</summary>
		internal static bool TrySetVcpScaled(DisplayInfo display, byte code, double fraction)
			=> WithTransport(display, t =>
			{
				if (!ReadFeature(t, code, out _, out var max) || max <= 0)
					return false;

				Thread.Sleep(SettleMs);
				return WriteFeature(t, code, (int)Math.Round(Math.Clamp(fraction, 0.0, 1.0) * max));
			});

		/// <summary>
		/// A reason no DDC transport could be opened for this display, or null when one opened fine (in which case
		/// the failure was the monitor's own answer, not the transport). Used to make the OSError say what to
		/// change instead of just "not supported".
		/// </summary>
		internal static string TransportUnavailableReason(DisplayInfo display)
		{
			using var transport = OpenTransport(display);

			if (transport != null)
				return null;

			return HasFramebuffers()
				? "no I2C bus was found for this display's connector - the graphics device does not route DDC/CI "
					+ "for it"
				: "no DDC/CI transport could be opened for this display - on Apple Silicon the display must be "
					+ "attached to a port that exposes an AV service, which some USB-C hubs and docks do not";
		}

		// ---- DDC/CI packet layer -------------------------------------------------------------------------

		private static bool ReadFeature(IDdcTransport transport, byte code, out int current, out int max)
		{
			current = max = 0;
			var reply = new byte[ReplyLength];

			// Get VCP feature: opcode 0x01 followed by the VCP code.
			if (!transport.Transact([0x01, code], reply))
				return false;

			// Reply: [dest, 0x88, 0x02, result, code, type, maxHi, maxLo, curHi, curLo, checksum]. The two
			// transports hand back the frame at slightly different offsets, so the marker is located rather than
			// assumed.
			for (var i = 0; i + 8 < reply.Length; i++)
			{
				if (reply[i] != 0x88 || reply[i + 1] != 0x02)
					continue;

				// A non-zero result byte means the monitor understood the request but does not support the code.
				if (reply[i + 2] != 0x00 || reply[i + 3] != code)
					return false;

				max = (reply[i + 5] << 8) | reply[i + 6];
				current = (reply[i + 7] << 8) | reply[i + 8];
				return max > 0;
			}

			return false;
		}

		private static bool WriteFeature(IDdcTransport transport, byte code, int value)
		{
			var clamped = Math.Clamp(value, 0, ushort.MaxValue);
			// Set VCP feature: opcode 0x03, VCP code, then the value big-endian.
			var ok = transport.Transact([0x03, code, (byte)(clamped >> 8), (byte)(clamped & 0xFF)], null);
			Thread.Sleep(SettleMs);
			return ok;
		}

		private static bool WithTransport(DisplayInfo display, Func<IDdcTransport, bool> transaction)
		{
			using var transport = OpenTransport(display);

			try
			{
				return transport != null && transaction(transport);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		/// <summary>Apple Silicon's AV service first, then Intel's framebuffer I2C bus; null when neither answers
		/// for this display.</summary>
		private static IDdcTransport OpenTransport(DisplayInfo display)
		{
			if (!MacMonitorDetails.TryGetIdentity(display, out var vendor, out var model, out var serial))
				return null;

			try
			{
				return AvServiceTransport.TryOpen(vendor, model, serial)
					?? (IDdcTransport)FramebufferTransport.TryOpen(vendor, model, serial);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return null;
			}
		}

		/// <summary>One open DDC channel to a monitor. <c>Transact</c> frames <paramref name="payload"/> for its own
		/// transport, so callers deal only in VESA payloads; a null or empty reply buffer means write-only.</summary>
		private interface IDdcTransport : IDisposable
		{
			bool Transact(byte[] payload, byte[] reply);
		}

		/// <summary>Checksum over a framed packet, XOR-seeded as the standard specifies.</summary>
		private static byte Checksum(byte seed, ReadOnlySpan<byte> bytes)
		{
			var checksum = seed;

			foreach (var b in bytes)
				checksum ^= b;

			return checksum;
		}

		// ---- Apple Silicon: IOAVService ------------------------------------------------------------------

		private sealed class AvServiceTransport : IDdcTransport
		{
			private nint service;

			private AvServiceTransport(nint service) => this.service = service;

			/// <summary>Finds the external display's AV service and wraps it. Each candidate is paired with a
			/// display through the IORegistry's ProductAttributes rather than by enumeration order, which would
			/// pick the wrong monitor as soon as two are attached.</summary>
			internal static AvServiceTransport TryOpen(uint vendor, uint model, uint serial)
			{
				if (IOServiceGetMatchingServices(0, IOServiceMatching("DCPAVServiceProxy"), out var iterator) != 0)
					return null;

				var singleExternal = 0u;
				var externals = 0;
				var matched = 0u;

				try
				{
					for (var entry = IOIteratorNext(iterator); entry != 0; entry = IOIteratorNext(iterator))
					{
						if (!IsExternal(entry))
						{
							_ = IOObjectRelease(entry);
							continue;
						}

						externals++;

						if (Matches(entry, vendor, model, serial))
						{
							matched = entry;
							break;
						}

						// Keep the last external as a fallback for a monitor that publishes no ProductAttributes;
						// it is only usable if it turns out to be the ONLY external, checked below.
						if (singleExternal != 0)
							_ = IOObjectRelease(singleExternal);

						singleExternal = entry;
					}
				}
				finally
				{
					_ = IOObjectRelease(iterator);
				}

				var chosen = matched != 0 ? matched : externals == 1 ? singleExternal : 0;

				if (matched != 0 && singleExternal != 0)
					_ = IOObjectRelease(singleExternal);

				if (chosen == 0)
				{
					if (singleExternal != 0)
						_ = IOObjectRelease(singleExternal);

					return null;
				}

				var service = IOAVServiceCreateWithService(0, chosen);
				_ = IOObjectRelease(chosen);
				return service != 0 ? new AvServiceTransport(service) : null;
			}

			public bool Transact(byte[] payload, byte[] reply)
			{
				// The source address travels as the transaction's data address here, not as a byte in the buffer,
				// so it is XORed into the checksum seed instead.
				var packet = new byte[payload.Length + 2];
				packet[0] = (byte)(0x80 | payload.Length);
				payload.CopyTo(packet, 1);
				packet[^1] = Checksum((byte)(HostAddress ^ SourceAddress), packet.AsSpan(0, packet.Length - 1));

				if (IOAVServiceWriteI2C(service, SlaveAddress, SourceAddress, packet, (uint)packet.Length) != 0)
					return false;

				if (reply == null || reply.Length == 0)
					return true;

				Thread.Sleep(SettleMs);
				return IOAVServiceReadI2C(service, SlaveAddress, SourceAddress, reply, (uint)reply.Length) == 0;
			}

			public void Dispose()
			{
				if (service != 0)
				{
					CFRelease(service);
					service = 0;
				}
			}

			private static bool IsExternal(uint entry)
			{
				var location = IORegistryEntryCreateCFProperty(entry, keyLocation, 0, 0);

				if (location == 0)
					return false;

				try
				{
					return CFStringCompare(location, valueExternal, 0) == 0;
				}
				finally
				{
					CFRelease(location);
				}
			}

			/// <summary>
			/// Compares the display identity CoreGraphics reports against the IORegistry's ProductAttributes, which
			/// live on an ancestor of the AV service (the framebuffer node), hence the parent-walking search.
			/// </summary>
			private static bool Matches(uint entry, uint vendor, uint model, uint serial)
			{
				var attributes = IORegistryEntrySearchCFProperty(entry, "IOService", keyDisplayAttributes, 0,
					KIORegistryIterateRecursively | KIORegistryIterateParents);

				if (attributes == 0)
					return false;

				try
				{
					var product = CFDictionaryGetValue(attributes, keyProductAttributes);

					if (product == 0)
						return false;

					// A panel that reports no serial (identical units from one batch often do not) still matches on
					// vendor + product; requiring an exact serial match would reject it outright.
					var registrySerial = ReadNumber(product, keySerialNumber);
					return ReadNumber(product, keyLegacyManufacturerId) == vendor
						&& ReadNumber(product, keyProductId) == model
						&& (serial == 0 || registrySerial <= 0 || registrySerial == serial);
				}
				finally
				{
					CFRelease(attributes);
				}
			}
		}

		// ---- Intel: the public IOFramebuffer I2C API -----------------------------------------------------

		/// <summary>
		/// The documented IOKit I2C path. It is dead code on Apple Silicon — <c>IOFramebuffer</c> instances do not
		/// exist there, so <see cref="TryOpen"/> simply finds nothing — and is reached only on Intel Macs.
		/// </summary>
		private sealed class FramebufferTransport : IDdcTransport
		{
			private const uint SimpleTransaction = 1;
			private const uint DdcCiReplyTransaction = 2;
			private const uint ReplyAddress = HostAddress | 1;

			private nint connect;

			private FramebufferTransport(nint connect) => this.connect = connect;

			internal static FramebufferTransport TryOpen(uint vendor, uint model, uint serial)
			{
				if (IOServiceGetMatchingServices(0, IOServiceMatching("IODisplayConnect"), out var iterator) != 0)
					return null;

				try
				{
					for (var entry = IOIteratorNext(iterator); entry != 0; entry = IOIteratorNext(iterator))
					{
						try
						{
							if (!Matches(entry, vendor, model, serial)
								|| IORegistryEntryGetParentEntry(entry, "IOService", out var framebuffer) != 0)
								continue;

							try
							{
								if (TryOpenBus(framebuffer) is FramebufferTransport transport)
									return transport;
							}
							finally
							{
								_ = IOObjectRelease(framebuffer);
							}
						}
						finally
						{
							_ = IOObjectRelease(entry);
						}
					}
				}
				finally
				{
					_ = IOObjectRelease(iterator);
				}

				return null;
			}

			public bool Transact(byte[] payload, byte[] reply)
			{
				// Here the source address IS the first byte on the wire, so the checksum seed is the bare host
				// address and the packet carries one extra leading byte compared with the AV service framing.
				var packet = new byte[payload.Length + 3];
				packet[0] = SourceAddress;
				packet[1] = (byte)(0x80 | payload.Length);
				payload.CopyTo(packet, 2);
				packet[^1] = Checksum(HostAddress, packet.AsSpan(0, packet.Length - 1));

				var wants = reply != null && reply.Length > 0;
				var sendHandle = GCHandle.Alloc(packet, GCHandleType.Pinned);
				var replyHandle = wants ? GCHandle.Alloc(reply, GCHandleType.Pinned) : default;

				try
				{
					var request = new IOI2CRequest
					{
						SendTransactionType = SimpleTransaction,
						SendAddress = HostAddress,
						SendBuffer = sendHandle.AddrOfPinnedObject(),
						SendBytes = (uint)packet.Length,
						MinReplyDelay = (ulong)SettleMs * 1_000_000,   // nanoseconds
					};

					if (wants)
					{
						request.ReplyTransactionType = DdcCiReplyTransaction;
						request.ReplyAddress = ReplyAddress;
						request.ReplyBuffer = replyHandle.AddrOfPinnedObject();
						request.ReplyBytes = (uint)reply.Length;
					}

					// The outer result only reports whether the transaction started; the bus result is in the
					// struct the driver wrote back.
					return IOI2CSendRequest(connect, 0, ref request) == 0 && request.Result == 0;
				}
				finally
				{
					sendHandle.Free();

					if (wants)
						replyHandle.Free();
				}
			}

			public void Dispose()
			{
				if (connect != 0)
				{
					_ = IOI2CInterfaceClose(connect, 0);
					connect = 0;
				}
			}

			/// <summary>The first bus on this framebuffer that opens. A connector's DDC channel is normally bus 0,
			/// but a device that routes several connectors through one framebuffer exposes more.</summary>
			private static FramebufferTransport TryOpenBus(uint framebuffer)
			{
				if (IOFBGetI2CInterfaceCount(framebuffer, out var count) != 0)
					return null;

				for (var bus = 0u; bus < count; bus++)
				{
					if (IOFBCopyI2CInterfaceForBus(framebuffer, bus, out var iface) != 0)
						continue;

					try
					{
						if (IOI2CInterfaceOpen(iface, 0, out var connect) == 0)
							return new FramebufferTransport(connect);
					}
					finally
					{
						_ = IOObjectRelease(iface);
					}
				}

				return null;
			}

			private static bool Matches(uint entry, uint vendor, uint model, uint serial)
			{
				var info = IODisplayCreateInfoDictionary(entry, 0);

				if (info == 0)
					return false;

				try
				{
					var registrySerial = ReadNumber(info, keyDisplaySerialNumber);
					return ReadNumber(info, keyDisplayVendorId) == vendor
						&& ReadNumber(info, keyDisplayProductId) == model
						&& (serial == 0 || registrySerial <= 0 || registrySerial == serial);
				}
				finally
				{
					CFRelease(info);
				}
			}
		}

		/// <summary>Whether this machine has any IOFramebuffer at all — false on Apple Silicon, which is what makes
		/// the two "no transport" explanations distinguishable.</summary>
		private static bool HasFramebuffers()
		{
			try
			{
				if (IOServiceGetMatchingServices(0, IOServiceMatching("IOFramebuffer"), out var iterator) != 0)
					return false;

				var entry = IOIteratorNext(iterator);
				var any = entry != 0;

				if (any)
					_ = IOObjectRelease(entry);

				_ = IOObjectRelease(iterator);
				return any;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				return false;
			}
		}

		// ---- interop -------------------------------------------------------------------------------------

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		private struct IOI2CRequest
		{
			internal uint SendTransactionType;
			internal uint ReplyTransactionType;
			internal uint SendAddress;
			internal uint ReplyAddress;
			internal byte SendSubAddress;
			internal byte ReplySubAddress;
			private readonly ushort reservedA;
			internal ulong MinReplyDelay;
			internal int Result;
			internal uint CommFlags;
			private readonly uint padA;
			internal uint SendBytes;
			private readonly ulong reservedB;
			private readonly uint padB;
			internal uint ReplyBytes;
			internal nint Completion;
			internal nint SendBuffer;
			internal nint ReplyBuffer;
			private readonly uint reservedC0, reservedC1, reservedC2, reservedC3, reservedC4;
			private readonly uint reservedC5, reservedC6, reservedC7, reservedC8, reservedC9;
		}

		private const uint KIORegistryIterateRecursively = 1;
		private const uint KIORegistryIterateParents = 2;
		private const nint KCFNumberSInt64Type = 4;
		private const uint EncodingUtf8 = 0x08000100;

		[DllImport(IOKitFramework)]
		private static extern nint IOServiceMatching([MarshalAs(UnmanagedType.LPStr)] string name);

		[DllImport(IOKitFramework)]
		private static extern int IOServiceGetMatchingServices(uint mainPort, nint matching, out uint iterator);

		[DllImport(IOKitFramework)]
		private static extern uint IOIteratorNext(uint iterator);

		[DllImport(IOKitFramework)]
		private static extern int IOObjectRelease(uint obj);

		[DllImport(IOKitFramework)]
		private static extern nint IORegistryEntryCreateCFProperty(uint entry, nint key, nint allocator,
			uint options);

		[DllImport(IOKitFramework)]
		private static extern nint IORegistryEntrySearchCFProperty(uint entry,
			[MarshalAs(UnmanagedType.LPStr)] string plane, nint key, nint allocator, uint options);

		[DllImport(IOKitFramework)]
		private static extern int IORegistryEntryGetParentEntry(uint entry,
			[MarshalAs(UnmanagedType.LPStr)] string plane, out uint parent);

		[DllImport(IOKitFramework)]
		private static extern nint IODisplayCreateInfoDictionary(uint framebuffer, uint options);

		// Private, Apple Silicon only. Declared here rather than resolved through dlsym because a missing entry
		// point raises EntryPointNotFoundException, which every call path above already treats as "no transport".
		[DllImport(IOKitFramework)]
		private static extern nint IOAVServiceCreateWithService(nint allocator, uint service);

		[DllImport(IOKitFramework)]
		private static extern int IOAVServiceWriteI2C(nint service, uint chipAddress, uint dataAddress,
			byte[] buffer, uint size);

		[DllImport(IOKitFramework)]
		private static extern int IOAVServiceReadI2C(nint service, uint chipAddress, uint offset,
			byte[] buffer, uint size);

		[DllImport(IOKitFramework)]
		private static extern int IOFBGetI2CInterfaceCount(uint framebuffer, out uint count);

		[DllImport(IOKitFramework)]
		private static extern int IOFBCopyI2CInterfaceForBus(uint framebuffer, uint bus, out uint iface);

		[DllImport(IOKitFramework)]
		private static extern int IOI2CInterfaceOpen(uint iface, uint options, out nint connect);

		[DllImport(IOKitFramework)]
		private static extern int IOI2CInterfaceClose(nint connect, uint options);

		[DllImport(IOKitFramework)]
		private static extern int IOI2CSendRequest(nint connect, uint options, ref IOI2CRequest request);

		[DllImport(CoreFoundation)]
		private static extern void CFRelease(nint cf);

		[DllImport(CoreFoundation)]
		private static extern nint CFStringCreateWithCString(nint allocator,
			[MarshalAs(UnmanagedType.LPStr)] string cStr, uint encoding);

		[DllImport(CoreFoundation)]
		private static extern int CFStringCompare(nint a, nint b, uint options);

		[DllImport(CoreFoundation)]
		private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

		[DllImport(CoreFoundation)]
		private static extern nuint CFGetTypeID(nint cf);

		[DllImport(CoreFoundation)]
		private static extern nuint CFNumberGetTypeID();

		[DllImport(CoreFoundation)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool CFNumberGetValue(nint number, nint type, out long value);

		// Interned for the process lifetime: these are looked up per DDC call, and each one is a fixed key name.
		private static readonly nint keyLocation = CFStr("Location");
		private static readonly nint valueExternal = CFStr("External");
		private static readonly nint keyDisplayAttributes = CFStr("DisplayAttributes");
		private static readonly nint keyProductAttributes = CFStr("ProductAttributes");
		private static readonly nint keyLegacyManufacturerId = CFStr("LegacyManufacturerID");
		private static readonly nint keyProductId = CFStr("ProductID");
		private static readonly nint keySerialNumber = CFStr("SerialNumber");
		private static readonly nint keyDisplayVendorId = CFStr("DisplayVendorID");
		private static readonly nint keyDisplayProductId = CFStr("DisplayProductID");
		private static readonly nint keyDisplaySerialNumber = CFStr("DisplaySerialNumber");

		private static nint CFStr(string value) => CFStringCreateWithCString(0, value, EncodingUtf8);

		/// <summary>A numeric dictionary entry, or -1 when it is absent or not a number. The type check matters:
		/// CFNumberGetValue on a non-number is undefined behaviour, and these dictionaries are driver-supplied.</summary>
		private static long ReadNumber(nint dictionary, nint key)
		{
			var value = CFDictionaryGetValue(dictionary, key);
			return value != 0 && CFGetTypeID(value) == CFNumberGetTypeID()
				&& CFNumberGetValue(value, KCFNumberSInt64Type, out var number) ? number : -1;
		}
	}
#endif
}
