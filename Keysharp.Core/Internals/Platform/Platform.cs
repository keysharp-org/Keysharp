namespace Keysharp.Internals
{
	/// <summary>
	/// The single entry point to everything platform-specific. The host is resolved once, lazily and
	/// thread-safely, and forced on the startup thread so no thread-affine probe is frozen by an arbitrary
	/// first-toucher. Runtime-selectable concerns (X11 vs Wayland vs compositor flavor) are reached as
	/// resolved interface objects (<see cref="Window"/>, <see cref="Mouse"/>, ...); concerns with no runtime
	/// variation are exposed as nested static groups.
	///
	/// Lifetime: the host is PROCESS-global. Every resolved service caches only process-level facts — the
	/// X11/Wayland session shape, the active compositor backend, OS capability flags — none of which vary
	/// between the Script instances a single process may host. Per-Script mutable state lives on <c>Script</c>
	/// (e.g. <c>Script.WindowGroups</c>), NOT here, so a Script swap needs no host reset. <see cref="Reset"/>
	/// exists only to force re-resolution (e.g. a test that mutates the environment); it is not called on
	/// Script construction.
	/// </summary>
	internal static partial class Platform
	{
		private static Lazy<PlatformHost> host = NewHost();

		private static Lazy<PlatformHost> NewHost()
			=> new (PlatformHost.Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

		/// <summary>The resolved host. Forcing this on the startup thread is the deterministic resolution point.</summary>
		internal static PlatformHost Instance => host.Value;

		/// <summary>Dispose and re-resolve the service graph. This is a test-only, no-concurrent-use operation; the
		/// static desktop-environment snapshot is intentionally not refreshed.</summary>
		internal static void Reset()
		{
			var previous = Interlocked.Exchange(ref host, NewHost());

			if (previous.IsValueCreated)
				previous.Value.Dispose();
		}

		// Convenience accessors — a field read through Instance, then the service. Grouped and named to match
		// PlatformHost; see the grouping note there.
		internal static IWindow Window => Instance.Window;
		internal static IWindowEvents WindowEvents => Instance.WindowEvents;
		internal static ControlManagerBase Control => Instance.Control;
		internal static IScreen Screen => Instance.Screen;
		internal static IMonitorControl MonitorControl => Instance.MonitorControl;
		internal static IMonitorEvents MonitorEvents => Instance.MonitorEvents;
		internal static IOverlay Overlay => Instance.Overlay;
		internal static IMouse Mouse => Instance.Mouse;
		internal static IKeyboard Keyboard => Instance.Keyboard;
		internal static IInput Input => Instance.Input;
		internal static IHotkeys Hotkeys => Instance.Hotkeys;
		internal static ISession Session => Instance.Session;
		internal static IPermissionManager Permissions => Instance.Permissions;
		// The UI-thread-marshalling facade, not the raw backend (PlatformHost.ClipboardCore).
		internal static IClipboard Clipboard => Instance.Clipboard;
	}
}
