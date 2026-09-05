namespace Keysharp.Internals
{
	internal static partial class Platform
	{
		/// <summary>Per-platform <see cref="Mapper.DriveBase"/> factory (compile-time OS selection).</summary>
		internal static class Drive
		{
			internal static Mapper.DriveBase CreateDrive(DriveInfo drive)
			{
#if WINDOWS
				return new Keysharp.Internals.Mapper.Windows.Drive(drive);
#elif LINUX
				return new Keysharp.Internals.Mapper.Linux.Drive(drive);
#elif OSX
				return new Keysharp.Internals.Mapper.MacOS.Drive(drive);
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}

		/// <summary>Per-platform <see cref="Window.StatusBarBase"/> factory (compile-time OS selection).</summary>
		internal static class StatusBar
		{
			internal static Window.StatusBarBase CreateStatusBar(nint hwnd)
			{
#if WINDOWS
				return new Keysharp.Internals.Window.Windows.StatusBar(hwnd);
#elif LINUX
				return new Keysharp.Internals.Window.Unix.StatusBar(hwnd);
#elif OSX
				return new Keysharp.Internals.Window.Unix.StatusBar(hwnd);
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}

		/// <summary>Per-platform <see cref="Window.IWindowEventBackend"/> factory (compile-time OS selection).
		/// Each <c>WinEventManager</c> (one per <see cref="Script"/>) owns and disposes the backend it gets,
		/// so this hands out a fresh one per call.</summary>
		internal static class WindowEvents
		{
			internal static Window.IWindowEventBackend CreateBackend(Script owner)
			{
#if WINDOWS
				return new Keysharp.Internals.Window.Windows.WindowEventBackend(owner);
#elif LINUX
				if (Desktop.IsWaylandSession)
				{
					if (Keysharp.Internals.Window.Linux.Wayland.WaylandBackend.Current is { SupportsWindowEvents: true } wayland)
						return new Keysharp.Internals.Window.Linux.Wayland.WaylandWindowEventBackend(owner, wayland);

					// GDK's display is a native Wayland display here, so it cannot be passed to Xlib even when
					// XWayland is running. An unsupported compositor has no safe global event source.
					return null;
				}

				return new Keysharp.Internals.Window.Linux.Wayland.WaylandWindowEventBackend(owner,
					Keysharp.Internals.Window.Linux.Wayland.DesktopBackend.X11);
#elif OSX
				return new Keysharp.Internals.Window.MacOS.WindowEventBackend(owner);
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}

		/// <summary>Per-platform <see cref="Window.IMonitorEventBackend"/> factory (compile-time OS selection).
		/// Unlike window events this needs no per-session selection: the Linux backend reads GDK's monitor
		/// signals, which GDK maintains under X11 and every Wayland compositor alike.</summary>
		internal static class MonitorEvents
		{
			internal static Window.IMonitorEventBackend CreateBackend(Script owner)
			{
#if WINDOWS
				return new Keysharp.Internals.Window.Windows.MonitorEventBackend();//Owner unused: this backend hangs off the static SystemEvents, and Script.Dispose detaches it.
#elif LINUX
				return new Keysharp.Internals.Window.Linux.MonitorEventBackend(owner);
#elif OSX
				return new Keysharp.Internals.Window.MacOS.MonitorEventBackend(owner);
#else
#error Unsupported platform. Only WINDOWS, LINUX, and OSX are supported.
#endif
			}
		}
	}
}
