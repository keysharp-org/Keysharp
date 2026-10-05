#if LINUX
using Keysharp.Internals.Linux;
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>keysharp-desktop backend with KWin focus and capture-id behavior.</summary>
	internal sealed class KWinBrokerBackend : DesktopBackend
	{
		internal KWinBrokerBackend() : base("kwin", "KWin (keysharp-desktop)") { }

		public override bool TryActivateWindow(nint handle)
		{
			if (!TryGetServiceHandle(handle, out var id))
				return false;

			var focused = DesktopClient.Current.FocusWindow(id);
			_ = focused && DesktopClient.Current.RaiseWindow(id);
			return focused;
		}

		public override bool TryGetNativeWindowId(nint handle, out string id)
		{
			if (!base.TryGetNativeWindowId(handle, out id) || !Guid.TryParse(id, out _))
			{
				_ = TryGetWindow(handle, out _);
				_ = base.TryGetNativeWindowId(handle, out id);
			}

			if (Guid.TryParse(id, out var uuid))
			{
				id = uuid.ToString("B");
				return true;
			}

			id = null;
			return false;
		}
	}
}
#endif
