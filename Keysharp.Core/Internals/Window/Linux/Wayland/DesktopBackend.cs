#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Window, clipboard and pointer operations served by keysharp-desktop.</summary>
	internal class DesktopBackend : IWaylandBackend
	{
		internal const string X11BackendKey = "x11";
		internal static readonly DesktopBackend X11 = new(X11BackendKey,
			"X11 (keysharp-desktop)", nativeHandles: true);

		private readonly object windowListSync = new();
		private readonly SyntheticWindowHandleMap<string> handles = new();
		private readonly bool nativeHandles;

		internal DesktopBackend(string backendKey, string name, bool nativeHandles = false)
		{
			BackendKey = backendKey;
			Name = name;
			this.nativeHandles = nativeHandles;
		}

		public string BackendKey { get; }
		public string Name { get; }
		public virtual bool SupportsWindowEvents
			=> DesktopClient.ProviderSupportsWindowList();
		public virtual bool SupportsPushWindowEvents
			=> DesktopClient.ProviderSupportsWindowWatch();

		public virtual IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			=> SubscribeBrokerWindowEvents(sink, null);

		protected IDisposable SubscribeBrokerWindowEvents(Action<WaylandWindowEvent> sink,
			Func<Action, IDisposable> subscribeAvailability)
		{
			if (sink == null || !SupportsWindowEvents)
				return null;

			void OnEvent(WaylandWindowEventKind kind, byte[] json)
			{
				if (TryParseWindow(json, out var window))
				{
					var bounds = window.FrameGeometry.Width > 0 && window.FrameGeometry.Height > 0
						? window.FrameGeometry : (Rectangle?)null;
					sink(new WaylandWindowEvent(kind, window.Handle) { Bounds = bounds });
				}
			}

			return RecoveringSubscription.Create(
				onError => DesktopClient.WatchWindowEvents(OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				DesktopClient.ProbeProvider,
				subscribeAvailability);
		}

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			lock (windowListSync)
				return TryParseWindowList(DesktopClient.QueryWindowList(includeHidden),
					includeHidden, out windows);
		}

		internal bool TryParseWindowList(ReadOnlyMemory<byte> json,
			out IReadOnlyList<WaylandWindowInfo> windows)
		{
			lock (windowListSync)
				return TryParseWindowList(json, true, out windows);
		}

		private bool TryParseWindowList(ReadOnlyMemory<byte> json, bool complete,
			out IReadOnlyList<WaylandWindowInfo> windows)
		{
			var parsed = DesktopWindowParser.TryList(json, Resolve, out windows);

			if (parsed)
				RememberWindows(windows, complete);

			return parsed;
		}

		public virtual bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			if (TryGetServiceHandle(handle, out var id)
				&& TryParseWindow(DesktopClient.QueryWindow(id), out window)
				&& window.Handle == handle)
				return true;

			if (IsKnown(handle) && TryListWindows(true, out var windows))
			{
				window = windows.FirstOrDefault(candidate => candidate.Handle == handle);
				return window != null;
			}

			window = null;
			return false;
		}

		public bool TryGetActiveWindow(out WaylandWindowInfo window)
			=> TryParseWindow(DesktopClient.QueryActiveWindow(), out window);

		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			=> TryGetWindowAt(x, y, false, out window);

		internal bool TryGetWindowAt(int x, int y, bool deepest, out WaylandWindowInfo window)
		{
			if (TryParseWindow(DesktopClient.QueryWindowAt(x, y, deepest), out window))
				return true;

			if (TryListWindows(false, out var windows))
			{
				window = windows.LastOrDefault(candidate => candidate.Visible
					&& candidate.OnCurrentWorkspace
					&& candidate.HasKnownField(WaylandWindowFields.Frame)
					&& candidate.FrameGeometry.Contains(x, y));
				return window != null;
			}

			window = null;
			return false;
		}

		public bool IsKnown(nint handle)
			=> nativeHandles ? handle.ToInt64() is > 0 and <= uint.MaxValue : handles.Contains(handle);

		public virtual bool TryGetNativeWindowId(nint handle, out string id)
		{
			if (nativeHandles && IsKnown(handle))
			{
				id = ((ulong)handle).ToString(CultureInfo.InvariantCulture);
				return true;
			}

			return handles.TryGetValue(handle, out id);
		}

		internal bool TryChildren(nint handle, out IReadOnlyList<nint> children)
		{
			children = [];

			if (!TryGetServiceHandle(handle, out var id))
				return false;

			var json = DesktopClient.QueryChildren(id);

			if (json == null || json.Length == 0)
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);

				if (!DesktopWindowParser.Bool(document.RootElement, "ok")
					|| !document.RootElement.TryGetProperty("handles", out var items)
					|| items.ValueKind != JsonValueKind.Array)
					return false;

				lock (windowListSync)
					children = items.EnumerateArray().Select(Resolve)
						.Where(candidate => candidate != 0).ToArray();
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		public bool TryGetCursorPos(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(out x, out y);

		public bool TryGetWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea(out area);

		public virtual bool TryActivateWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.FocusWindow(id);

		public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
			=> DesktopClient.ReserveWindow(cookie, x, y, ttlMs);

		public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			compositorId = DesktopClient.GetReservedWindow(cookie);
			lock (windowListSync)
				handle = compositorId.Length > 0 ? Resolve(compositorId) : 0;
			return handle != 0;
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.MoveResizeWindow(id,
					setPosition ? bounds.X : int.MinValue,
					setPosition ? bounds.Y : int.MinValue,
					setSize && bounds.Width > 0 ? bounds.Width : 0,
					setSize && bounds.Height > 0 ? bounds.Height : 0);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SetWindowDecorated(id, !noBorder);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SetWindowState(id,
					WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SetWindowAbove(id, onTop);

		public bool TrySetSkipTaskbar(nint handle, bool skip)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SetWindowSkipTaskbar(id, skip);

		public virtual bool TrySetZOrder(nint handle, ZOrder z)
			=> TryGetServiceHandle(handle, out var id)
				&& (z == ZOrder.Top ? DesktopClient.RaiseWindow(id)
					: z == ZOrder.Bottom && DesktopClient.LowerWindow(id));

		public bool TrySetTransparency(nint handle, object alpha)
		{
			var opacity = alpha is string value && value.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255 : Math.Clamp((int)alpha.Al(), 0, 255);
			return TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SetWindowOpacity(id, opacity);
		}

		public bool SupportsTransparency
			=> DesktopClient.ProviderSupportsTransparency();

		public bool TryCloseWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.CloseWindow(id);

		public bool TryKillWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.KillWindow(id);

		internal bool TryRedrawWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.RedrawWindow(id);

		internal bool TryClickWindow(nint handle, Point at, uint button, int count)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.ClickWindow(id, at.X, at.Y, button, count);

		internal bool TrySendWindowButton(nint handle, Point at, uint button, bool down)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.SendWindowButton(id, at.X, at.Y, button, down);

		internal bool TryFocusChildWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id)
				&& DesktopClient.FocusChildWindow(id);

		internal bool TrySetWindowTitle(nint handle, string title)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.SetWindowTitle(id, title);

		internal bool TrySetWindowVisible(nint handle, bool visible)
			=> TryGetServiceHandle(handle, out var id) && DesktopClient.SetWindowVisible(id, visible);

		public bool SupportsMouse
			=> DesktopClient.ProviderSupportsAbsolutePointer();

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> DesktopClient.SendMouseMoveAbsolute(x, y);

		public bool TrySendMouseMoveRelative(int dx, int dy)
			=> DesktopClient.SendMouseMoveRelative(dx, dy);

		public bool TrySendMouseButton(uint button, bool pressed)
			=> DesktopClient.SendMouseButton(button, pressed);

		public bool TrySendMouseScroll(int delta, bool vertical)
			=> DesktopClient.SendMouseScroll(delta, vertical);

		public bool SupportsClipboard => DesktopClient.ProviderSupportsClipboard();

		public string[] GetClipboardMimetypes()
			=> DesktopClient.GetClipboardMimetypes();

		public byte[] GetClipboardContent(string mimetype)
			=> DesktopClient.GetClipboardContent(mimetype);

		public bool SetClipboardContent(string mimetype, byte[] bytes)
			=> DesktopClient.SetClipboardContent(mimetype, bytes);

		public string GetClipboardText()
			=> DesktopClient.GetClipboardText();

		public bool SetClipboardText(string text)
			=> DesktopClient.SetClipboardText(text);

		public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler,
			Action<Exception> onError = null)
			=> handler == null ? null : DesktopClient.WatchClipboardChanges(handler, onError);

		protected virtual void WindowsChanged(IReadOnlyList<WaylandWindowInfo> windows,
			IReadOnlyList<nint> removed) { }

		private void RememberWindows(IReadOnlyList<WaylandWindowInfo> windows, bool complete)
		{
			var removed = complete && !nativeHandles
				? handles.Retain(windows.Select(window => window.Handle)) : [];
			WindowsChanged(windows, removed);
		}

		private bool TryParseWindow(ReadOnlyMemory<byte> json, out WaylandWindowInfo window)
		{
			lock (windowListSync)
			{
				var parsed = DesktopWindowParser.TrySingle(json, Resolve, out window);

				if (parsed)
					RememberWindows([window], false);

				return parsed;
			}
		}

		protected nint Resolve(string id)
		{
			if (nativeHandles)
				return uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var native)
					? (nint)native : 0;

			return string.IsNullOrEmpty(id) ? 0 : handles.GetOrCreate(id);
		}

		private nint Resolve(JsonElement item)
		{
			if (item.ValueKind == JsonValueKind.String)
				return Resolve(item.GetString());

			if (item.ValueKind != JsonValueKind.Number || !item.TryGetUInt64(out var id))
				return 0;

			return nativeHandles
				? id is > 0 and <= uint.MaxValue ? (nint)id : 0
				: Resolve(id.ToString(CultureInfo.InvariantCulture));
		}

		protected bool TryGetServiceHandle(nint handle, out ulong id)
		{
			if (nativeHandles && IsKnown(handle))
			{
				id = (ulong)handle;
				return true;
			}

			id = 0;
			return handles.TryGetValue(handle, out var value)
				&& ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
		}
	}
}
#endif
