#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>keysharp-desktop backend with KWin focus and capture-id behavior.</summary>
	internal sealed class KWinBrokerBackend : DesktopBackend
	{
		private readonly object captureLock = new();
		private readonly Dictionary<nint, string> captureIds = [];

		internal KWinBrokerBackend() : base("kwin", "KWin (keysharp-desktop)") { }

		public override bool TryActivateWindow(nint handle)
		{
			if (!TryGetServiceHandle(handle, out var id))
				return false;

			var focused = DesktopClient.FocusWindow(id);
			_ = focused && DesktopClient.RaiseWindow(id);
			return focused;
		}

		// KWin advertises raise but not lower; sending a window behind every other window
		// would not be an equivalent fallback.
		public override bool TrySetZOrder(nint handle, ZOrder z)
			=> z == ZOrder.Top && TryGetServiceHandle(handle, out var id)
				&& DesktopClient.RaiseWindow(id);

		public override bool TryGetNativeWindowId(nint handle, out string id)
		{
			lock (captureLock)
				if (captureIds.TryGetValue(handle, out var value) && Guid.TryParse(value, out var uuid))
				{
					id = uuid.ToString("B");
					return true;
				}

			if (TryGetWindow(handle, out _))
				lock (captureLock)
					if (captureIds.TryGetValue(handle, out var value) && Guid.TryParse(value, out var uuid))
					{
						id = uuid.ToString("B");
						return true;
					}

			id = null;
			return false;
		}

		protected override void WindowsChanged(IReadOnlyList<WaylandWindowInfo> windows,
			IReadOnlyList<nint> removed)
		{
			lock (captureLock)
			{
				foreach (var handle in removed)
					_ = captureIds.Remove(handle);

				foreach (var window in windows)
					if (string.IsNullOrEmpty(window.CaptureId))
						_ = captureIds.Remove(window.Handle);
					else
						captureIds[window.Handle] = window.CaptureId;
			}
		}
	}
}
#endif
