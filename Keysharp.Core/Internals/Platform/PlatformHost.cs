namespace Keysharp.Internals
{
	/// <summary>
	/// The resolved-once bundle of platform capability services, one per process. OS selection happens at
	/// compile time; Linux services choose their concrete session backend once during host construction.
	/// </summary>
	internal abstract class PlatformHost : IDisposable
	{
		internal abstract IWindow Window { get; }
		internal abstract IMouse Mouse { get; }
		internal abstract IScreen Screen { get; }
		internal abstract IMonitorControl MonitorControl { get; }
		internal abstract IOverlay Overlay { get; }
		internal abstract IClipboard Clipboard { get; }
		internal abstract IWindowEvents Events { get; }
		internal abstract IMonitorEvents MonitorEvents { get; }
		internal abstract ISession Session { get; }
		internal abstract IHotkeys Hotkeys { get; }
		internal abstract IInput Input { get; }
		internal abstract IKeyboard Keyboard { get; }
		internal abstract IPermissionManager Permissions { get; }
		internal abstract ControlManagerBase Control { get; }

		public virtual void Dispose() { }

		/// <summary>Compile-time OS selection. Windows/macOS are single-backend; Linux resolves X11/Wayland
		/// services inside its host.</summary>
		internal static PlatformHost Resolve() =>
#if WINDOWS
			new WindowsPlatformHost();
#elif LINUX
			new LinuxPlatformHost();
#elif OSX
			new MacPlatformHost();
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
	}

	// Per-OS hosts. Only the current OS's host is compiled (the others are #if-guarded), matching how the
	// rest of the per-OS code is selected.
#if WINDOWS
	internal sealed class WindowsPlatformHost : PlatformHost
	{
		private readonly IMouse mouse = new WindowsMouse();
		private readonly IInput input = new WindowsInput();
		private readonly IKeyboard keyboard = new WindowsKeyboard();
		private readonly IOverlay overlay = new WindowsOverlay();
		private readonly IScreen screen = new WindowsScreen();
		private readonly IMonitorControl monitorControl = new WindowsMonitorControl();
		private readonly IClipboard clipboard = new WindowsClipboard();
		private readonly IWindowEvents events = new WindowsEvents();
		private readonly IMonitorEvents monitorEvents = new WindowsMonitorEvents();
		private readonly ISession session = new WindowsSession();
		private readonly IHotkeys hotkeys = new WindowsHotkeys();
		private readonly IPermissionManager permissions = new DefaultPermissionManager();
		private readonly IWindow window = new WindowsWindow();
		private readonly ControlManagerBase control = new Os.Windows.ControlManager();
		internal override IMouse Mouse => mouse;
		internal override IInput Input => input;
		internal override IKeyboard Keyboard => keyboard;
		internal override IOverlay Overlay => overlay;
		internal override IScreen Screen => screen;
		internal override IMonitorControl MonitorControl => monitorControl;
		internal override IClipboard Clipboard => clipboard;
		internal override IWindowEvents Events => events;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override ISession Session => session;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override IPermissionManager Permissions => permissions;
		internal override IWindow Window => window;
		internal override ControlManagerBase Control => control;
	}
#elif LINUX
	internal sealed class LinuxPlatformHost : PlatformHost
	{
		private readonly IMouse mouse = LinuxMice.Resolve();
		private readonly IInput input = new LinuxInput();
		private readonly IKeyboard keyboard = LinuxKeyboards.Resolve();
		private readonly IOverlay overlay = new LinuxOverlay();
		// Lazy: choosing the per-compositor IScreen needs the resolved Wayland backend, which must not be probed
		// at host construction. The compositor flavor is inspected once, on first Screen use.
		private readonly Lazy<IScreen> screen = new (LinuxScreens.Resolve);
		// Lazy for the same reason as screen, plus the choice inspects Eto's resolved clipboard handler, which is
		// only meaningful once the toolkit is up.
		private readonly Lazy<IClipboard> clipboard = new (LinuxClipboards.Resolve);
		// Not lazy and not per-compositor: DDC/CI over i2c and logind's backlight interface are kernel/session
		// facilities, identical under X11 and every Wayland compositor.
		private readonly IMonitorControl monitorControl = new LinuxMonitorControl();
		private readonly IWindowEvents events = new LinuxEvents();
		private readonly IMonitorEvents monitorEvents = new LinuxMonitorEvents();
		private readonly ISession session = new LinuxSession();
		private readonly IHotkeys hotkeys = new LinuxHotkeys();
		private readonly IWindow window = LinuxWindows.Resolve();
		private readonly IPermissionManager permissions = new LinuxPermissionManager();
		private readonly ControlManagerBase control = new Os.Unix.ControlManager();
		internal override IMouse Mouse => mouse;
		internal override IInput Input => input;
		internal override IKeyboard Keyboard => keyboard;
		internal override IOverlay Overlay => overlay;
		internal override IScreen Screen => screen.Value;
		internal override IMonitorControl MonitorControl => monitorControl;
		internal override IClipboard Clipboard => clipboard.Value;
		internal override IWindowEvents Events => events;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override ISession Session => session;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override IWindow Window => window;
		internal override IPermissionManager Permissions => permissions;
		internal override ControlManagerBase Control => control;

		public override void Dispose()
		{
			Keysharp.Internals.Window.Linux.Wayland.GnomeShellBridge.Reset();
			Keysharp.Internals.Window.Linux.Wayland.CinnamonShellBridge.Reset();
			Keysharp.Internals.Window.Linux.Wayland.KWinDBusBridge.Reset();
			Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Reset();
		}
	}
#elif OSX
	internal sealed class MacPlatformHost : PlatformHost
	{
		private readonly IMouse mouse = new MacMouse();
		private readonly IInput input = new MacInput();
		private readonly IKeyboard keyboard = new MacKeyboard();
		private readonly IOverlay overlay = new MacOverlay();
		private readonly IScreen screen = new MacScreen();
		private readonly IMonitorControl monitorControl = new MacMonitorControl();
		// macOS uses the shared Eto (Cocoa) clipboard — no focus gating, no data-control question, so no override.
		private readonly IClipboard clipboard = new EtoClipboard();
		private readonly IWindowEvents events = new MacEvents();
		private readonly IMonitorEvents monitorEvents = new MacMonitorEvents();
		private readonly ISession session = new MacSession();
		private readonly IHotkeys hotkeys = new MacHotkeys();
		private readonly IPermissionManager permissions = new MacPermissionManager();
		private readonly IWindow window = new MacWindow();
		private readonly ControlManagerBase control = new Os.Unix.ControlManager();
		internal override IMouse Mouse => mouse;
		internal override IInput Input => input;
		internal override IKeyboard Keyboard => keyboard;
		internal override IOverlay Overlay => overlay;
		internal override IScreen Screen => screen;
		internal override IClipboard Clipboard => clipboard;
		internal override IWindowEvents Events => events;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override ISession Session => session;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override IPermissionManager Permissions => permissions;
		internal override IWindow Window => window;
		internal override ControlManagerBase Control => control;
	}
#endif
}
