namespace Keysharp.Internals
{
	/// <summary>
	/// The resolved-once bundle of platform capability services, one per process. OS selection happens at
	/// compile time; Linux services choose their concrete session backend once during host construction.
	/// </summary>
	internal abstract class PlatformHost : IDisposable
	{
		// Grouped by subject, and every service is named for the subject it serves — so the two event sources read
		// as the pair they are (WindowEvents / MonitorEvents) rather than one of them owning the generic name.
		// Windows
		internal abstract IWindow Window { get; }
		internal abstract IWindowEvents WindowEvents { get; }
		internal abstract ControlManagerBase Control { get; }

		// Displays
		internal abstract IScreen Screen { get; }
		internal abstract IMonitorControl MonitorControl { get; }
		internal abstract IMonitorEvents MonitorEvents { get; }
		internal abstract IOverlay Overlay { get; }

		// Input
		internal abstract IMouse Mouse { get; }
		internal abstract IKeyboard Keyboard { get; }
		internal abstract IInput Input { get; }
		internal abstract IHotkeys Hotkeys { get; }

		// Session
		internal abstract ISession Session { get; }
		internal abstract IPermissionManager Permissions { get; }

		/// <summary>The backend-specific clipboard. Reached only through <see cref="Clipboard"/>, never directly:
		/// it assumes it is called on the UI thread.</summary>
		internal abstract IClipboard ClipboardCore { get; }

		private IClipboard clipboardFacade;

		/// <summary>The process clipboard, with every operation marshalled to the UI thread. Wrapping the resolved
		/// backend ONCE, here, is what makes that correct by construction — the WinForms clipboard requires an STA
		/// thread and the GTK/Cocoa clipboards are UI-thread-only, so a call from a real thread (Ks.RealThread) is a
		/// failure at any seam that forgot to marshal. Marshalling at the call sites instead is what let
		/// ClipboardAll(), Image.FromClipboard and the old CopyImageToClipboard/IsClipboardEmpty each ship without
		/// it. <see cref="Script.InvokeOnUIThread{T}"/> short-circuits on the main thread, so this costs nothing in
		/// the common case.</summary>
		internal IClipboard Clipboard
		{
			get
			{
				var facade = clipboardFacade;

				if (facade == null)
				{
					facade = new UiThreadClipboard(ClipboardCore);
					facade = Interlocked.CompareExchange(ref clipboardFacade, facade, null) ?? facade;
				}

				return facade;
			}
		}

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
		private readonly IWindow window = new WindowsWindow();
		private readonly IWindowEvents windowEvents = new WindowsEvents();
		private readonly ControlManagerBase control = new Os.Windows.ControlManager();
		private readonly IScreen screen = new WindowsScreen();
		private readonly IMonitorControl monitorControl = new WindowsMonitorControl();
		private readonly IMonitorEvents monitorEvents = new WindowsMonitorEvents();
		private readonly IOverlay overlay = new WindowsOverlay();
		private readonly IMouse mouse = new WindowsMouse();
		private readonly IKeyboard keyboard = new WindowsKeyboard();
		private readonly IInput input = new WindowsInput();
		private readonly IHotkeys hotkeys = new WindowsHotkeys();
		private readonly ISession session = new WindowsSession();
		private readonly IPermissionManager permissions = new DefaultPermissionManager();
		private readonly IClipboard clipboard = new WindowsClipboard();

		internal override IWindow Window => window;
		internal override IWindowEvents WindowEvents => windowEvents;
		internal override ControlManagerBase Control => control;
		internal override IScreen Screen => screen;
		internal override IMonitorControl MonitorControl => monitorControl;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override IOverlay Overlay => overlay;
		internal override IMouse Mouse => mouse;
		internal override IKeyboard Keyboard => keyboard;
		internal override IInput Input => input;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override ISession Session => session;
		internal override IPermissionManager Permissions => permissions;
		internal override IClipboard ClipboardCore => clipboard;
	}
#elif LINUX
	internal sealed class LinuxPlatformHost : PlatformHost
	{
		private readonly IWindow window = LinuxWindows.Resolve();
		private readonly IWindowEvents windowEvents = new LinuxEvents();
		private readonly ControlManagerBase control = new Os.Unix.ControlManager();
		// Lazy: choosing the per-compositor IScreen needs the resolved Wayland backend, which must not be probed
		// at host construction. The compositor flavor is inspected once, on first Screen use.
		private readonly Lazy<IScreen> screen = new (LinuxScreens.Resolve);
		// Not lazy and not per-compositor: DDC/CI over i2c and logind's backlight interface are kernel/session
		// facilities, identical under X11 and every Wayland compositor.
		private readonly IMonitorControl monitorControl = new LinuxMonitorControl();
		private readonly IMonitorEvents monitorEvents = new LinuxMonitorEvents();
		private readonly IOverlay overlay = new LinuxOverlay();
		private readonly IMouse mouse = LinuxMice.Resolve();
		private readonly IKeyboard keyboard = LinuxKeyboards.Resolve();
		private readonly IInput input = new LinuxInput();
		private readonly IHotkeys hotkeys = new LinuxHotkeys();
		private readonly ISession session = new LinuxSession();
		private readonly IPermissionManager permissions = new LinuxPermissionManager();
		// Lazy for the same reason as screen, plus the choice inspects Eto's resolved clipboard handler, which is
		// only meaningful once the toolkit is up.
		private readonly Lazy<IClipboard> clipboard = new (LinuxClipboards.Resolve);

		internal override IWindow Window => window;
		internal override IWindowEvents WindowEvents => windowEvents;
		internal override ControlManagerBase Control => control;
		internal override IScreen Screen => screen.Value;
		internal override IMonitorControl MonitorControl => monitorControl;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override IOverlay Overlay => overlay;
		internal override IMouse Mouse => mouse;
		internal override IKeyboard Keyboard => keyboard;
		internal override IInput Input => input;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override ISession Session => session;
		internal override IPermissionManager Permissions => permissions;
		internal override IClipboard ClipboardCore => clipboard.Value;

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
		private readonly IWindow window = new MacWindow();
		private readonly IWindowEvents windowEvents = new MacEvents();
		private readonly ControlManagerBase control = new Os.Unix.ControlManager();
		private readonly IScreen screen = new MacScreen();
		private readonly IMonitorControl monitorControl = new MacMonitorControl();
		private readonly IMonitorEvents monitorEvents = new MacMonitorEvents();
		private readonly IOverlay overlay = new MacOverlay();
		private readonly IMouse mouse = new MacMouse();
		private readonly IKeyboard keyboard = new MacKeyboard();
		private readonly IInput input = new MacInput();
		private readonly IHotkeys hotkeys = new MacHotkeys();
		private readonly ISession session = new MacSession();
		private readonly IPermissionManager permissions = new MacPermissionManager();
		// macOS uses the shared Eto (Cocoa) clipboard — no focus gating, no data-control question, so no override.
		private readonly IClipboard clipboard = new EtoClipboard();

		internal override IWindow Window => window;
		internal override IWindowEvents WindowEvents => windowEvents;
		internal override ControlManagerBase Control => control;
		internal override IScreen Screen => screen;
		internal override IMonitorControl MonitorControl => monitorControl;
		internal override IMonitorEvents MonitorEvents => monitorEvents;
		internal override IOverlay Overlay => overlay;
		internal override IMouse Mouse => mouse;
		internal override IKeyboard Keyboard => keyboard;
		internal override IInput Input => input;
		internal override IHotkeys Hotkeys => hotkeys;
		internal override ISession Session => session;
		internal override IPermissionManager Permissions => permissions;
		internal override IClipboard ClipboardCore => clipboard;
	}
#endif
}
