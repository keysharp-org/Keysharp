namespace Keysharp.Internals
{

	// Backend is a factory, not a cached service: each WinEventManager (one per Script) takes
	// ownership of the returned backend and disposes it, so handing out a memoized instance
	// would give every Script after the first a disposed backend.
#if WINDOWS
	internal sealed class WindowsEvents : IWindowEvents
	{
		public IWindowEventBackend CreateBackend(Script owner) => new Keysharp.Internals.Window.Windows.WindowEventBackend(owner);
	}

	internal sealed class WindowsMonitorEvents : IMonitorEvents
	{
		public IMonitorEventBackend CreateBackend(Script owner) => new Keysharp.Internals.Window.Windows.MonitorEventBackend();//Owner unused: this backend hangs off the static SystemEvents, and Script.Dispose detaches it.
	}
#elif LINUX
	/// <summary>
	/// Linux window-event backend selection. On a Wayland session the compositor-native backend is preferred
	/// (it sees both native Wayland and XWayland windows) when the active compositor can actually push events;
	/// otherwise the X11 backend is used, which still observes XWayland windows via the always-present X server.
	/// </summary>
	internal sealed class LinuxEvents : IWindowEvents
	{
		public IWindowEventBackend CreateBackend(Script owner) => Resolve(owner);

		private static IWindowEventBackend Resolve(Script owner)
		{
			if (IsWaylandSession)
			{
				var wayland = WaylandBackend.Current;

				if (wayland != null && wayland.SupportsWindowEvents)
					return new WaylandWindowEventBackend(owner, wayland);
			}

			return new WindowEventBackend(owner);
		}
	}

	/// <summary>
	/// Unlike window events, monitor events need no per-session selection: the backend reads GDK's monitor signals,
	/// which GDK maintains under X11 and every Wayland compositor alike.
	/// </summary>
	internal sealed class LinuxMonitorEvents : IMonitorEvents
	{
		public IMonitorEventBackend CreateBackend(Script owner) => new MonitorEventBackend(owner);
	}
#elif OSX
	internal sealed class MacEvents : IWindowEvents
	{
		public IWindowEventBackend CreateBackend(Script owner) => new WindowEventBackend(owner);
	}

	internal sealed class MacMonitorEvents : IMonitorEvents
	{
		public IMonitorEventBackend CreateBackend(Script owner) => new MonitorEventBackend(owner);
	}
#endif
}
