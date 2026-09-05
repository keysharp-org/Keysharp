#if LINUX
using System.Text.Json;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal sealed class X11BrokerBackend : IWaylandBackend
	{
		internal static readonly X11BrokerBackend Instance = new();
		internal const string BackendName = "x11";
		public string Name => "X11 (keysharp-desktop)";
		public bool SupportsWindowEvents => true;
		public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
		{
			if (sink == null) return null;
			void OnEvent(string type, string json)
			{
				WaylandWindowEventKind? kind = type switch
				{
					"create" => WaylandWindowEventKind.Created,
					"close" => WaylandWindowEventKind.Closed,
					"active" => WaylandWindowEventKind.Activated,
					"title" => WaylandWindowEventKind.TitleChanged,
					"minimize" => WaylandWindowEventKind.Minimized,
					"restore" => WaylandWindowEventKind.Restored,
					"move" => WaylandWindowEventKind.MoveResized,
					_ => null
				};
				if (kind == null || string.IsNullOrEmpty(json)) return;
				try
				{
					using var document = JsonDocument.Parse(json);
					var item = document.RootElement;
					if (item.ValueKind != JsonValueKind.Object) return;
					if (item.TryGetProperty("window", out var wrapped)) item = wrapped;
					var handle = Resolve(DesktopWindowParser.Text(item, "id"));
					if (handle == 0) return;
					var bounds = DesktopWindowParser.Bounds(item, "frame");
					sink(new WaylandWindowEvent(kind.Value, handle)
						{ Bounds = bounds.Width > 0 && bounds.Height > 0 ? bounds : null });
				}
				catch (JsonException) { }
			}
			return RecoveringSubscription.Create(
				onError => DesktopClient.WatchWindowEvents(BackendName, OnEvent, onError),
				() => new WaylandPollingEventSource(this, sink),
				() => DesktopClient.ProbeProvider(BackendName), null);
		}
		public bool IsKnown(nint handle) => handle.ToInt64() is > 0 and <= uint.MaxValue;
		public bool TryGetCursorPos(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);
		public bool SupportsMouse => DesktopClient.ProviderSupportsAbsolutePointer(BackendName);
		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> DesktopClient.SendMouseMoveAbsolute(BackendName, x, y);
		private static nint Resolve(string value)
			=> uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? (nint)id : 0;
		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
			=> DesktopWindowParser.TrySingle(IsKnown(handle) ? DesktopClient.QueryWindow(BackendName, (ulong)handle) : null,
				Resolve, out window);
		public bool TryGetActiveWindow(out WaylandWindowInfo window)
			=> DesktopWindowParser.TrySingle(DesktopClient.QueryActiveWindow(BackendName), Resolve, out window);
		public bool TryGetWindowAt(int x, int y, out WaylandWindowInfo window)
			=> TryGetWindowAt(x, y, false, out window);
		internal bool TryGetWindowAt(int x, int y, bool deepest, out WaylandWindowInfo window)
			=> DesktopWindowParser.TrySingle(DesktopClient.QueryWindowAt(BackendName, x, y, deepest), Resolve, out window);
		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			=> TryParseWindowList(DesktopClient.QueryWindowList(BackendName, includeHidden), out windows);

		internal static bool TryParseWindowList(string json, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];
			if (string.IsNullOrEmpty(json)) return false;
			try
			{
				using var document = JsonDocument.Parse(json);
				if (!DesktopWindowParser.Bool(document.RootElement, "ok")
					|| !document.RootElement.TryGetProperty("windows", out var items)
					|| items.ValueKind != JsonValueKind.Array) return false;
				var parsed = new List<WaylandWindowInfo>();
				foreach (var item in items.EnumerateArray())
				{
					var handle = Resolve(DesktopWindowParser.Text(item, "id"));
					if (handle != 0) parsed.Add(DesktopWindowParser.Parse(item, handle));
				}
				windows = parsed;
				return true;
			}
			catch (JsonException) { return false; }
		}

		internal bool TryChildren(nint handle, out IReadOnlyList<nint> children)
		{
			children = [];
			var json = IsKnown(handle) ? DesktopClient.QueryChildren(BackendName, (ulong)handle) : null;
			if (string.IsNullOrEmpty(json)) return false;
			try
			{
				using var document = JsonDocument.Parse(json);
				if (!DesktopWindowParser.Bool(document.RootElement, "ok")
					|| !document.RootElement.TryGetProperty("handles", out var items)
					|| items.ValueKind != JsonValueKind.Array) return false;
				children = items.EnumerateArray().Select(item => Resolve(item.ToString()))
					.Where(id => id != 0).ToArray();
				return true;
			}
			catch (JsonException) { return false; }
		}
	}
}
#endif
