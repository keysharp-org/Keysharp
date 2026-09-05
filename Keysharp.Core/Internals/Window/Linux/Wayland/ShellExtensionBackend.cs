#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>keysharp-desktop backend augmented by a compositor shell extension.</summary>
	internal class ShellExtensionBackend : DesktopBackend, IDisposable
	{
		private readonly ShellExtensionBridge bridge;

		protected ShellExtensionBackend(string backendKey, string name, string serviceName,
			string objectPath, string diagnosticLabel) : base(backendKey, name)
			=> bridge = new ShellExtensionBridge(serviceName, objectPath, diagnosticLabel);

		public override IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			=> SubscribeBrokerWindowEvents(sink, bridge.SubscribeAvailability);

		public bool SupportsImageOverlay => bridge.HasOwner;
		public bool CanAttemptImageOverlay => true;

		public OverlayShowResult TryShowImageOverlay(uint id, int x, int y, int width,
			int height, byte[] pngBytes)
			=> bridge.ShowImageOverlay(id, x, y, width, height, pngBytes);

		public bool TryMoveImageOverlay(uint id, int x, int y, int width, int height)
			=> bridge.MoveImageOverlay(id, x, y, width, height);

		public bool TryHideImageOverlay(uint id) => bridge.HideImageOverlay(id);

		public IDisposable SubscribeClipboardAvailability(Action handler)
			=> bridge.SubscribeAvailability(handler);

		public void Dispose() => bridge.Dispose();
	}

	internal sealed class GnomeBackend : ShellExtensionBackend
	{
		internal GnomeBackend() : base("gnome", "GNOME",
			"io.github.keysharp.GnomeShell", "/io/github/keysharp/GnomeShell", "GNOME Shell") { }
	}

	internal sealed class CinnamonBackend : ShellExtensionBackend
	{
		internal CinnamonBackend() : base("cinnamon", "Cinnamon",
			"io.github.keysharp.CinnamonShell", "/io/github/keysharp/CinnamonShell", "Cinnamon Shell") { }
	}
}
#endif
