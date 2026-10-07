#if LINUX
using Keysharp.Internals.Linux;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>Window, clipboard and pointer operations served by keysharp-desktop.</summary>
	internal class DesktopBackend : IWaylandBackend
	{
		internal const string X11BackendKey = "x11";
		internal static DesktopBackend X11 => Script.TheScript.LinuxServices.X11;

		private readonly DesktopClient desktop;
		private DesktopClient Desktop => desktop ?? DesktopClient.Current;

		private readonly object windowListSync = new();
		private readonly SyntheticWindowHandleMap<string> handles = new();
		private readonly Dictionary<nint, string> captureIds = [];
		private readonly Dictionary<ulong, WindowReservation> reservations = [];
		private readonly bool nativeHandles;
		private readonly bool usePushWindowEvents;

		private sealed class WindowReservation(long expires)
		{
			internal readonly long Expires = expires;
			internal nint Handle;
			internal bool Published;
		}

		internal DesktopBackend(string backendKey, string name, bool nativeHandles = false,
			bool usePushWindowEvents = true, DesktopClient desktop = null)
		{
			this.desktop = desktop;
			BackendKey = backendKey;
			Name = name;
			this.nativeHandles = nativeHandles;
			this.usePushWindowEvents = usePushWindowEvents;
		}

		public string BackendKey { get; }
		public string Name { get; }
		public virtual bool SupportsWindowEvents
			=> Desktop.ProviderSupportsWindowList();
		public virtual bool SupportsPushWindowEvents
			=> usePushWindowEvents && Desktop.ProviderSupportsWindowWatch();

		public virtual IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
		{
			if (sink == null || !SupportsWindowEvents)
				return null;

			return usePushWindowEvents
				? SubscribeBrokerWindowEvents(sink, null)
				: new WaylandPollingEventSource(this, sink);
		}

		protected IDisposable SubscribeBrokerWindowEvents(Action<WaylandWindowEvent> sink,
			Func<Action, IDisposable> subscribeAvailability)
		{
			if (sink == null || !SupportsWindowEvents)
				return null;

			void OnEvent(WaylandWindowEventKind kind, WaylandWindowInfo serviceWindow)
				=> sink(ResolveWindowEvent(kind, serviceWindow));

			return RecoveringSubscription.Create(
				onError => Desktop.WatchWindowEvents(OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				Desktop.ProbeProvider,
				subscribeAvailability);
		}

		internal WaylandWindowEvent ResolveWindowEvent(WaylandWindowEventKind kind, WaylandWindowInfo serviceWindow)
		{
			lock (windowListSync)
			{
				var window = ResolveWindow(serviceWindow);
				RememberWindows([window], false);
				var bounds = window.FrameGeometry.Width > 0 && window.FrameGeometry.Height > 0
					? window.FrameGeometry : (Rectangle?)null;
				return new WaylandWindowEvent(kind, WaylandOwnToplevels.ResolveEventHandle(window)) { Bounds = bounds };
			}
		}

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			if (Desktop.TryReadWindows(out var state))
			{
				lock (windowListSync)
				{
					if (!Desktop.TryReadCachedWindows(out state)) { windows = []; return false; }
					var all = state.Select(ResolveWindow).ToArray();
					RememberWindows(all, true);
					windows = includeHidden ? all : all.Where(window => window.Visible).ToArray();
					return true;
				}
			}
			if (SupportsPushWindowEvents) { windows = []; return false; }
			// Providers without a watch cannot maintain an authoritative mirror.
			lock (windowListSync)
				return TryParseWindowList(Desktop.QueryWindowList(includeHidden),
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

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			=> TryGetWindow(handle, out window, out _);

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window, out bool notFound)
		{
			window = null;
			notFound = false;

			if (!TryGetServiceHandle(handle, out var id))
				return false;

			// An explicit query needs current geometry, including a window published ahead of the lease's mirror.
			var json = Desktop.QueryWindow(id, out var status);
			if (TryReadWindow(handle, json, status, out window, out notFound) || notFound)
				return window != null;

			if (status == NativeClientStatus.Unsupported
				? Desktop.TryReadWindows(out var state)
				: Desktop.TryReadCachedWindows(out state))
			{
				lock (windowListSync)
				{
					var serviceWindow = state.FirstOrDefault(candidate => candidate.ServiceHandle == id);
					if (serviceWindow != null)
					{
						window = ResolveWindow(serviceWindow);
						RememberWindows([window], false);
						return true;
					}
				}
				// Only a mirror that has observed this identity can establish its removal.
				notFound = status == NativeClientStatus.Unsupported && Desktop.IsMirroredWindow(id);
				return false;
			}

			if (status != NativeClientStatus.Unsupported || !TryListWindows(true, out var windows))
				return false;

			window = windows.FirstOrDefault(candidate => candidate.Handle == handle);
			notFound = window == null;
			return window != null;
		}

		/// <summary>What a window query's reply says about <paramref name="handle"/>.</summary>
		internal bool TryReadWindow(nint handle, ReadOnlyMemory<byte> json, NativeClientStatus status,
			out WaylandWindowInfo window, out bool notFound)
		{
			notFound = status == NativeClientStatus.NotFound;

			if (status == NativeClientStatus.Ok && TryParseWindow(json, out window) && window.Handle == handle)
				return true;

			window = null;
			return false;
		}

		public bool TryGetActiveWindow(out WaylandWindowInfo window)
		{
			if (TryListWindows(true, out var windows))
			{
				window = windows.LastOrDefault(candidate => candidate.Active);
				return window != null;
			}
			window = null;
			return false;
		}

		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			=> TryGetWindowAt(x, y, false, out window);

		internal bool TryGetWindowAt(int x, int y, bool deepest, out WaylandWindowInfo window)
		{
			if (TryParseWindow(Desktop.QueryWindowAt(x, y, deepest), out window))
				return true;

			window = TryListWindows(false, out var windows) ? FindWindowAt(windows, x, y) : null;
			return window != null;
		}

		internal WaylandWindowInfo FindWindowAt(IReadOnlyList<WaylandWindowInfo> windows, int x, int y)
		{
			bool Contains(WaylandWindowInfo candidate) => candidate.Visible && !candidate.Minimized
				&& candidate.OnCurrentWorkspace
				&& candidate.HasKnownField(WaylandWindowFields.Frame)
				&& candidate.FrameGeometry.Contains(x, y);
			// Generic toplevel lists have no stacking order; prefer the focused window when it contains the point.
			var focused = BackendKey == "generic" ? windows.LastOrDefault(candidate => candidate.Active && Contains(candidate)) : null;
			return focused ?? windows.LastOrDefault(Contains);
		}

		public bool IsKnown(nint handle)
			=> nativeHandles ? handle.ToInt64() is > 0 and <= uint.MaxValue : handles.Contains(handle);

		public virtual bool TryGetNativeWindowId(nint handle, out string id)
		{
			lock (windowListSync)
			{
				if (captureIds.TryGetValue(handle, out id))
					return true;

				if (nativeHandles && IsKnown(handle))
				{
					id = ((ulong)handle).ToString(CultureInfo.InvariantCulture);
					return true;
				}

				return handles.TryGetValue(handle, out id);
			}
		}

		internal bool TryChildren(nint handle, out IReadOnlyList<nint> children)
		{
			children = [];

			if (!TryGetServiceHandle(handle, out var id))
				return false;

			var json = Desktop.QueryChildren(id);

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
			=> Desktop.QueryCursorPosition(out x, out y);

		public bool TryGetWorkArea(out Rectangle area)
			=> Desktop.QueryWorkArea(out area);

		public virtual bool TryActivateWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && Desktop.FocusWindow(id);

		public bool TryReserveWindow(ulong cookie, int x, int y, int ttlMs)
		{
			if (!Desktop.ReserveWindow(cookie, x, y, ttlMs))
				return false;
			RememberReservation(cookie, ttlMs);
			return true;
		}

		internal void RememberReservation(ulong cookie, int ttlMs)
		{
			lock (windowListSync)
				if (!nativeHandles)
				{
					RemoveExpiredReservations();
					reservations[cookie] = new(Environment.TickCount64 + ttlMs);
				}
		}

		public bool TryGetReservedWindow(ulong cookie, out nint handle, out string compositorId)
		{
			compositorId = Desktop.GetReservedWindow(cookie);
			return TryReadReservedWindow(cookie, compositorId, out handle);
		}

		internal bool TryReadReservedWindow(ulong cookie, string compositorId, out nint handle)
		{
			lock (windowListSync)
			{
				handle = !string.IsNullOrEmpty(compositorId) ? Resolve(compositorId) : 0;
				if (handle != 0 && reservations.TryGetValue(cookie, out var reservation))
					reservation.Handle = handle;
				return handle != 0;
			}
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.MoveResizeWindow(id,
					setPosition ? bounds.X : int.MinValue,
					setPosition ? bounds.Y : int.MinValue,
					setSize && bounds.Width > 0 ? bounds.Width : 0,
					setSize && bounds.Height > 0 ? bounds.Height : 0);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowDecorated(id, !noBorder);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowState(id,
					WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TryUnminimizeWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowState(id, WaylandWindowStateProtocol.Unminimized);

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowAbove(id, onTop);

		public bool TrySetSkipTaskbar(nint handle, bool skip)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowSkipTaskbar(id, skip);

		public virtual bool TrySetZOrder(nint handle, ZOrder z)
			=> TryGetServiceHandle(handle, out var id)
				&& (z == ZOrder.Top ? Desktop.RaiseWindow(id)
					: z == ZOrder.Bottom && Desktop.LowerWindow(id));

		public bool TrySetTransparency(nint handle, object alpha)
		{
			int opacity;

			if (alpha is string value && value.Equals("off", StringComparison.OrdinalIgnoreCase))
				opacity = 255;
			else
			{
				_ = alpha.TryCoerceInt(out var a);
				opacity = Math.Clamp(a, 0, 255);
			}
			return TryGetServiceHandle(handle, out var id)
				&& Desktop.SetWindowOpacity(id, opacity);
		}

		public bool SupportsTransparency
			=> Desktop.ProviderSupportsTransparency();

		public bool SupportsWindowMove
			=> Desktop.ProviderSupportsWindowMove();

		public bool TryCloseWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && Desktop.CloseWindow(id);

		public bool TryKillWindow(nint handle)
		{
			if (!TryGetServiceHandle(handle, out var id))
				return false;

			// A force-kill is compositor-specific on Wayland. Where it is absent, retain WinKill's
			// documented graceful-close fallback instead of sending an operation the provider rejected.
			if (!Desktop.ProviderSupportsWindowKill())
				return Desktop.CloseWindow(id);

			// As AHK does, ask first and force only a window still there after about 500 ms, so an app that
			// would have closed cleanly keeps its shutdown path. The pump-aware sleep lets one of our own
			// windows dispatch the close it was sent.
			_ = Desktop.CloseWindow(id);

			for (var waited = 0; waited < 500 && TryGetWindow(handle, out _); waited += 25)
				Keysharp.Internals.Flow.SleepWithoutInterruption(25);

			if (!TryGetWindow(handle, out var remaining))
				return true;

			// The broker would signal the owning process, which must never be this one.
			return remaining?.PID != Environment.ProcessId && Desktop.KillWindow(id);
		}

		internal bool TryRedrawWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id) && Desktop.RedrawWindow(id);

		internal bool TryClickWindow(nint handle, Point at, uint button, int count)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.ClickWindow(id, at.X, at.Y, button, count);

		internal bool TrySendWindowButton(nint handle, Point at, uint button, bool down)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.SendWindowButton(id, at.X, at.Y, button, down);

		internal bool TryFocusChildWindow(nint handle)
			=> TryGetServiceHandle(handle, out var id)
				&& Desktop.FocusChildWindow(id);

		internal bool TrySetWindowTitle(nint handle, string title)
			=> TryGetServiceHandle(handle, out var id) && Desktop.SetWindowTitle(id, title);

		internal bool TrySetWindowVisible(nint handle, bool visible)
			=> TryGetServiceHandle(handle, out var id) && Desktop.SetWindowVisible(id, visible);

		public bool SupportsMouse
			=> Desktop.ProviderSupportsAbsolutePointer();

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> Desktop.SendMouseMoveAbsolute(x, y);

		public bool TrySendMouseMoveRelative(int dx, int dy)
			=> Desktop.SendMouseMoveRelative(dx, dy);

		public bool TrySendMouseButton(uint button, bool pressed)
			=> Desktop.SendMouseButton(button, pressed);

		public bool TrySendMouseScroll(int delta, bool vertical)
			=> Desktop.SendMouseScroll(delta, vertical);

		public bool SupportsClipboard => Desktop.ProviderSupportsClipboard();

		public string[] GetClipboardMimetypes()
			=> Desktop.GetClipboardMimetypes();

		public byte[] GetClipboardContent(string mimetype)
			=> Desktop.GetClipboardContent(mimetype);

		public bool SetClipboardContent(string mimetype, byte[] bytes)
			=> Desktop.SetClipboardContent(mimetype, bytes);

		public string GetClipboardText()
			=> Desktop.GetClipboardText();

		public bool SetClipboardText(string text)
			=> Desktop.SetClipboardText(text);

		public IDisposable SubscribeClipboardChanges(Action<string, string[]> handler,
			Action<Exception> onError = null)
			=> handler == null ? null : Desktop.WatchClipboardChanges(handler, onError);

		private void RememberWindows(IReadOnlyList<WaylandWindowInfo> windows, bool complete)
		{
			if (complete && !nativeHandles)
			{
				RemoveExpiredReservations();
				foreach (var reservation in reservations.Values)
					if (windows.Any(window => window.Handle == reservation.Handle))
						reservation.Published = true;
			}

			// A consumed reservation can name a window before the leased snapshot publishes it.
			var removed = complete && !nativeHandles
				? handles.Retain(windows.Select(window => window.Handle).Concat(
					reservations.Values.Where(reservation => !reservation.Published && reservation.Handle != 0)
					.Select(reservation => reservation.Handle))) : [];
			foreach (var handle in removed)
				_ = captureIds.Remove(handle);

			foreach (var window in windows)
				if (string.IsNullOrEmpty(window.CaptureId))
					_ = captureIds.Remove(window.Handle);
				else
					captureIds[window.Handle] = window.CaptureId;
		}

		private void RemoveExpiredReservations()
		{
			var now = Environment.TickCount64;
			foreach (var cookie in reservations.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
				_ = reservations.Remove(cookie);
		}

		private WaylandWindowInfo ResolveWindow(WaylandWindowInfo value)
		{
			var handle = Resolve(value.ServiceHandle.ToString(CultureInfo.InvariantCulture));
			return new(handle, value.CompositorId,
				value.Title, value.AppId, value.PID, value.FrameGeometry, value.ClientGeometry, value.SurfaceGeometry,
				value.Active, value.Minimized, value.Maximized, value.Visible, value.AlwaysOnTop, value.Decorated,
				value.Transparency, value.OnCurrentWorkspace,
				value.ServiceParentHandle == 0 ? value.ParentHandle : Resolve(value.ServiceParentHandle.ToString(CultureInfo.InvariantCulture)),
				value.ServiceParentHandle == 0 ? handle : value.TopLevelHandle,
				value.CaptureId, value.KnownFields, value.ServiceHandle, value.ServiceParentHandle, value.StackingOrder);
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
