#if LINUX
using System.Text.Json;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static class DesktopWindowParser
	{
		internal static bool TryText(JsonElement item, string name, out string value)
		{
			value = "";

			if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var property)
				|| property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
				return false;

			value = property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
			return true;
		}

		internal static string Text(JsonElement item, string name)
			=> TryText(item, name, out var value) ? value : "";

		internal static long Number(JsonElement item, string name)
		{
			if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
				return 0;

			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
				return number;

			return value.ValueKind == JsonValueKind.String
				&& long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
				? number : 0;
		}

		internal static bool Bool(JsonElement item, string name, bool fallback = false)
		{
			if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
				return fallback;

			if (value.ValueKind == JsonValueKind.True)
				return true;

			if (value.ValueKind == JsonValueKind.False)
				return false;

			if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
				return number != 0;

			if (value.ValueKind == JsonValueKind.String)
			{
				var text = value.GetString();

				if (bool.TryParse(text, out var boolean))
					return boolean;

				if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
					return number != 0;
			}

			return fallback;
		}

		private static WaylandWindowInfo Parse(JsonElement item, nint handle, string id)
		{
			if (item.ValueKind != JsonValueKind.Object)
				return new WaylandWindowInfo(handle, knownFields: WaylandWindowFields.None);

			var fields = ReadKnownFields(item);
			var compositorId = Has(fields, WaylandWindowFields.CompositorId)
				? Text(item, "compositorId") : id;
			var appId = Has(fields, WaylandWindowFields.AppId) ? Text(item, "appId") : "";

			return new WaylandWindowInfo(
				handle: handle,
				compositorId: compositorId,
				title: Has(fields, WaylandWindowFields.Title) ? Text(item, "title") : "",
				appId: appId,
				pid: Has(fields, WaylandWindowFields.Pid) ? Number(item, "pid") : 0,
				frameGeometry: Has(fields, WaylandWindowFields.Frame) ? ReadBounds(item, "frame") : Rectangle.Empty,
				clientGeometry: Has(fields, WaylandWindowFields.Client) ? ReadBounds(item, "client") : Rectangle.Empty,
				surfaceGeometry: Has(fields, WaylandWindowFields.Buffer) ? ReadBounds(item, "buffer") : Rectangle.Empty,
				active: Has(fields, WaylandWindowFields.Active) && Bool(item, "active"),
				minimized: Has(fields, WaylandWindowFields.Minimized) && Bool(item, "minimized"),
				maximized: Has(fields, WaylandWindowFields.Maximized) && Bool(item, "maximized"),
				visible: !Has(fields, WaylandWindowFields.Visible) || Bool(item, "visible", true),
				alwaysOnTop: Has(fields, WaylandWindowFields.AlwaysOnTop) && Bool(item, "alwaysOnTop"),
				decorated: !Has(fields, WaylandWindowFields.Decorated) || Bool(item, "decorated", true),
				transparency: Has(fields, WaylandWindowFields.Transparency) ? Number(item, "transparency") : -1L,
				onCurrentWorkspace: !Has(fields, WaylandWindowFields.OnCurrentWorkspace)
					|| Bool(item, "onCurrentWorkspace", true),
				parentHandle: (nint)Number(item, "parent"),
				topLevelHandle: (nint)Number(item, "topLevel"),
				captureId: Has(fields, WaylandWindowFields.CaptureId) ? Text(item, "captureId") : "",
				knownFields: fields);
		}

		internal static bool TryParse(JsonElement item, Func<string, nint> resolve, out WaylandWindowInfo window)
		{
			window = null;

			if (resolve == null || !TryText(item, "id", out var id) || string.IsNullOrEmpty(id))
				return false;

			var handle = resolve(id);

			if (handle == 0)
				return false;

			window = Parse(item, handle, id);
			return true;
		}

		internal static bool TryList(ReadOnlyMemory<byte> json, Func<string, nint> resolve,
			out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];

			if (json.IsEmpty || resolve == null)
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				return TryList(document.RootElement, resolve, out windows);
			}
			catch (JsonException)
			{
				return false;
			}
		}

		internal static bool TrySingle(ReadOnlyMemory<byte> json, Func<string, nint> resolve,
			out WaylandWindowInfo window)
		{
			window = null;

			if (json.IsEmpty || resolve == null)
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				return TrySingle(document.RootElement, resolve, out window);
			}
			catch (JsonException)
			{
				return false;
			}
		}

		internal static bool TryWindowEvent(ReadOnlyMemory<byte> json, Func<string, nint> resolve,
			out WaylandWindowInfo window)
		{
			window = null;

			if (json.IsEmpty || resolve == null)
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				return TryParse(document.RootElement, resolve, out window);
			}
			catch (JsonException)
			{
				return false;
			}
		}

		private static bool TryList(JsonElement root, Func<string, nint> resolve,
			out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];

			if (!Bool(root, "ok") || !root.TryGetProperty("windows", out var items)
				|| items.ValueKind != JsonValueKind.Array)
				return false;

			var parsed = new List<WaylandWindowInfo>(items.GetArrayLength());

			foreach (var item in items.EnumerateArray())
				if (TryParse(item, resolve, out var window))
					parsed.Add(window);

			windows = parsed;
			return true;
		}

		private static bool TrySingle(JsonElement root, Func<string, nint> resolve,
			out WaylandWindowInfo window)
		{
			window = null;
			return Bool(root, "ok") && root.TryGetProperty("window", out var item)
				&& TryParse(item, resolve, out window);
		}

		private static Rectangle ReadBounds(JsonElement item, string name)
		{
			if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;

			var x = Number(value, "x");
			var y = Number(value, "y");
			var width = Number(value, "width");
			var height = Number(value, "height");

			return x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
				&& width is >= 0 and <= int.MaxValue && height is >= 0 and <= int.MaxValue
				? new Rectangle((int)x, (int)y, (int)width, (int)height) : Rectangle.Empty;
		}

		private static readonly (string Name, WaylandWindowFields Field)[] fields =
		[
			("compositorId", WaylandWindowFields.CompositorId),
			("title", WaylandWindowFields.Title),
			("appId", WaylandWindowFields.AppId),
			("pid", WaylandWindowFields.Pid),
			("frame", WaylandWindowFields.Frame),
			("client", WaylandWindowFields.Client),
			("buffer", WaylandWindowFields.Buffer),
			("active", WaylandWindowFields.Active),
			("minimized", WaylandWindowFields.Minimized),
			("maximized", WaylandWindowFields.Maximized),
			("visible", WaylandWindowFields.Visible),
			("alwaysOnTop", WaylandWindowFields.AlwaysOnTop),
			("decorated", WaylandWindowFields.Decorated),
			("transparency", WaylandWindowFields.Transparency),
			("onCurrentWorkspace", WaylandWindowFields.OnCurrentWorkspace),
			("captureId", WaylandWindowFields.CaptureId),
		];

		private static WaylandWindowFields ReadKnownFields(JsonElement item)
		{
			if (item.ValueKind != JsonValueKind.Object)
				return WaylandWindowFields.None;

			var allowed = WaylandWindowFields.All;
			if (item.TryGetProperty("validFields", out var values))
			{
				allowed = WaylandWindowFields.None;
				if (values.ValueKind == JsonValueKind.Array)
					foreach (var value in values.EnumerateArray())
						if (value.ValueKind == JsonValueKind.String)
							foreach (var (name, field) in fields)
								if (value.ValueEquals(name)) { allowed |= field; break; }
			}

			var present = WaylandWindowFields.None;
			foreach (var (name, field) in fields)
				if (item.TryGetProperty(name, out _))
					present |= field;
			return present & allowed;
		}

		private static bool Has(WaylandWindowFields fields, WaylandWindowFields field)
			=> (fields & field) != 0;
	}
}
#endif
