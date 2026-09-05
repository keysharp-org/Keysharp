#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// Broker calls used by the KWin Wayland backend. A KDE Wayland session is served by a KWin script the
	/// broker reaches over a socket the session daemon hands it at registration.
	///
	/// Two things about it are worth knowing before use. Captures do not go through the
	/// script -- they run in the broker's forked worker -- so they keep working when the
	/// script is wedged, busy or being restarted. And everything that does go through
	/// the script runs on the compositor's main thread, where concurrency is exactly one:
	/// the broker's two lanes buy ordering, so a cheap query is never behind a queue of
	/// enumerations, but never simultaneity.
	/// </summary>
	internal sealed class KWinBrokerBackend : IWaylandBackend
	{
		private const string BackendName = "kwin";
		private const long HandleTag = 0x4000_0000_0000_0000L;
		private readonly object handleLock = new();
		private readonly Dictionary<nint, ulong> serviceHandles = [];
		private readonly Dictionary<nint, string> captureIds = [];

		public string Name => "KWin (keysharp-desktop)";

		public bool SupportsWindowEvents => true;

		public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			=> sink != null ? new WaylandPollingEventSource(this, sink) : null;

		/// <summary>
		/// Whether the broker will serve this session. A probe rather than a look at
		/// the environment: the script may be absent, wedged or a version behind, and
		/// the service is the only thing that knows.
		/// </summary>
		internal static bool IsAvailable()
			=> DesktopClient.ProbeKWinProvider()
			   && DesktopClient.QueryWindowHandles(BackendName) != null;

		internal static bool QueryCursorPosition(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);

		internal static bool QueryWorkArea(out Rectangle area)
			=> DesktopClient.QueryWorkArea(BackendName, out area);

		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList(BackendName, includeHidden);

		internal static string QueryActiveWindow()
			=> DesktopClient.QueryActiveWindow(BackendName);

		internal static bool FocusWindow(ulong handle)
			=> DesktopClient.FocusWindow(BackendName, handle);

		internal static bool RaiseWindow(ulong handle)
			=> DesktopClient.RaiseWindow(BackendName, handle);

		/// <summary>
		/// Deliberately absent: KWin exposes raise and no lower, and the script reports
		/// the operation unsupported rather than approximating it by sending the window
		/// behind every other one, which is a different thing and a worse surprise. The
		/// broker does not advertise it either.
		/// </summary>
		internal static bool LowerWindow(ulong handle) => false;

		internal static bool CloseWindow(ulong handle)
			=> DesktopClient.CloseWindow(BackendName, handle);

		internal static bool MoveResizeWindow(ulong handle, int x, int y,
											 int width, int height)
			=> DesktopClient.MoveResizeWindow(BackendName, handle, x, y, width, height);

		/// <summary>0 restores, 1 minimizes, 2 maximizes.</summary>
		internal static bool SetWindowState(ulong handle, int state)
			=> DesktopClient.SetWindowState(BackendName, handle, state);

		/// <summary>
		/// 0 to 255. The script converts to the 0..1 KWin uses and rounds on the way
		/// back, so a fully opaque window reports 255 rather than 254.
		/// </summary>
		internal static bool SetWindowOpacity(ulong handle, int opacity)
			=> DesktopClient.SetWindowOpacity(BackendName, handle, opacity);

		internal static bool SetWindowAbove(ulong handle, bool above)
			=> DesktopClient.SetWindowAbove(BackendName, handle, above);

		internal static bool SetWindowDecorated(ulong handle, bool decorated)
			=> DesktopClient.SetWindowDecorated(BackendName, handle, decorated);

		internal static bool SetWindowSkipTaskbar(ulong handle, bool skip)
			=> DesktopClient.SetWindowSkipTaskbar(BackendName, handle, skip);

		public bool TryGetCursorPos(out int x, out int y)
			=> QueryCursorPosition(out x, out y);

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];
			var json = QueryWindowList(includeHidden);

			if (string.IsNullOrEmpty(json))
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;

				if (!JsonBool(root, "ok") || !root.TryGetProperty("windows", out var array)
					|| array.ValueKind != JsonValueKind.Array)
					return false;

				var parsed = new List<WaylandWindowInfo>();

				foreach (var item in array.EnumerateArray())
					if (TryParseWindow(item, out var window))
						parsed.Add(window);

				windows = parsed;
				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool TryGetActiveWindow(out WaylandWindowInfo window)
			=> TryParseSingleWindow(QueryActiveWindow(), out window);

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			window = null;

			if (!IsKnown(handle) || !TryListWindows(true, out var windows))
				return false;

			window = windows.FirstOrDefault(candidate => candidate.Handle == handle);
			return window != null;
		}

		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
		{
			window = null;

			if (!TryListWindows(false, out var windows))
				return false;

			for (var index = windows.Count - 1; index >= 0; index--)
			{
				var candidate = windows[index];
				var bounds = candidate.FrameGeometry;

				if (candidate.Visible && candidate.OnCurrentWorkspace
					&& x >= bounds.X && y >= bounds.Y
					&& x < bounds.Right && y < bounds.Bottom)
				{
					window = candidate;
					return true;
				}
			}

			return false;
		}

		public bool TryGetWorkArea(out Rectangle area)
			=> QueryWorkArea(out area);

		public bool TryActivateWindow(nint handle)
		{
			if (!TryGetServiceHandle(handle, out var serviceHandle))
				return false;

			var focused = FocusWindow(serviceHandle);
			_ = focused && RaiseWindow(serviceHandle);
			return focused;
		}

		public bool TryMoveResizeWindow(nint handle, Rectangle bounds, bool setPosition, bool setSize)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && MoveResizeWindow(serviceHandle,
				   setPosition ? bounds.X : int.MinValue,
				   setPosition ? bounds.Y : int.MinValue,
				   setSize ? bounds.Width : 0,
				   setSize ? bounds.Height : 0);

		public bool TrySetNoBorder(nint handle, bool noBorder)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && SetWindowDecorated(serviceHandle, !noBorder);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && SetWindowState(serviceHandle, WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TrySetAlwaysOnTop(nint handle, bool onTop)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && SetWindowAbove(serviceHandle, onTop);

		public bool TrySetSkipTaskbar(nint handle, bool skip)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && SetWindowSkipTaskbar(serviceHandle, skip);

		public bool TrySetZOrder(nint handle, ZOrder z)
			=> z == ZOrder.Top && TryGetServiceHandle(handle, out var serviceHandle)
			   && RaiseWindow(serviceHandle);

		public bool TrySetTransparency(nint handle, object alpha)
		{
			if (!TryGetServiceHandle(handle, out var serviceHandle))
				return false;

			var opacity = alpha is string value && value.Equals("off", StringComparison.OrdinalIgnoreCase)
				? 255 : Math.Clamp((int)alpha.Al(), 0, 255);
			return SetWindowOpacity(serviceHandle, opacity);
		}

		public bool SupportsTransparency => true;

		public bool TryCloseWindow(nint handle)
			=> TryGetServiceHandle(handle, out var serviceHandle)
			   && CloseWindow(serviceHandle);

		public bool IsKnown(nint handle)
		{
			lock (handleLock)
				return serviceHandles.ContainsKey(handle);
		}

		internal bool TryGetWindowUuid(nint handle, out string uuid)
		{
			lock (handleLock)
			{
				if (captureIds.TryGetValue(handle, out var nativeId)
					&& Guid.TryParse(nativeId, out var parsed))
				{
					uuid = parsed.ToString("B");
					return true;
				}
			}

			uuid = null;
			return false;
		}

		private bool TryParseSingleWindow(string json, out WaylandWindowInfo window)
		{
			window = null;

			if (string.IsNullOrEmpty(json))
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;

				return JsonBool(root, "ok")
					&& root.TryGetProperty("window", out var item)
					&& item.ValueKind == JsonValueKind.Object
					&& TryParseWindow(item, out window);
			}
			catch
			{
				return false;
			}
		}

		private bool TryParseWindow(JsonElement item, out WaylandWindowInfo info)
		{
			info = null;

			if (!TryJsonString(item, "id", out var id)
				|| !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var serviceHandle)
				|| serviceHandle == 0 || serviceHandle > uint.MaxValue)
				return false;

			var handle = GetOrCreateHandle(serviceHandle);
			if (TryJsonString(item, "captureId", out var nativeId) && !string.IsNullOrEmpty(nativeId))
			{
				lock (handleLock)
					captureIds[handle] = nativeId;
			}

			info = new WaylandWindowInfo(
				handle: handle,
				compositorId: id,
				title: JsonString(item, "title"),
				appId: JsonString(item, "appId"),
				pid: JsonLong(item, "pid"),
				frameGeometry: JsonRectangle(item, "frame"),
				clientGeometry: JsonRectangle(item, "client"),
				surfaceGeometry: JsonRectangle(item, "buffer"),
				active: JsonBool(item, "active"),
				minimized: JsonBool(item, "minimized"),
				maximized: JsonBool(item, "maximized"),
				visible: JsonBool(item, "visible"),
				alwaysOnTop: JsonBool(item, "alwaysOnTop"),
				decorated: !item.TryGetProperty("decorated", out _) || JsonBool(item, "decorated"),
				transparency: item.TryGetProperty("transparency", out _) ? JsonLong(item, "transparency") : -1L);
			return true;
		}

		private nint GetOrCreateHandle(ulong serviceHandle)
		{
			var handle = new nint(HandleTag | (long)serviceHandle);

			lock (handleLock)
				serviceHandles[handle] = serviceHandle;

			return handle;
		}

		private bool TryGetServiceHandle(nint handle, out ulong serviceHandle)
		{
			lock (handleLock)
				return serviceHandles.TryGetValue(handle, out serviceHandle);
		}

		private static bool JsonBool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value)
			   && (value.ValueKind == JsonValueKind.True
				   || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0);

		private static string JsonString(JsonElement element, string property)
			=> TryJsonString(element, property, out var value) ? value : string.Empty;

		private static bool TryJsonString(JsonElement element, string property, out string value)
		{
			value = string.Empty;
			if (!element.TryGetProperty(property, out var item))
				return false;
			value = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString();
			return true;
		}

		private static long JsonLong(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var value))
				return 0;
			return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
				? number
				: value.ValueKind == JsonValueKind.String
				  && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
					? number : 0;
		}

		private static Rectangle JsonRectangle(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var rectangle) || rectangle.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;
			return new Rectangle(JsonInt(rectangle, "x"), JsonInt(rectangle, "y"),
				JsonInt(rectangle, "width"), JsonInt(rectangle, "height"));
		}

		private static int JsonInt(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var value))
				return 0;
			return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
				? number
				: value.ValueKind == JsonValueKind.String
				  && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
					? number : 0;
		}
	}
}
#endif
