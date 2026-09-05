#if LINUX
using System.Text.Json;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	internal static class DesktopWindowParser
	{
		internal static string Text(JsonElement item, string name)
			=> item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
				? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";

		internal static long Number(JsonElement item, string name)
			=> long.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

		internal static bool Bool(JsonElement item, string name, bool fallback = false)
			=> item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.True
				|| value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n != 0 : fallback;

		internal static Rectangle Bounds(JsonElement item, string name)
		{
			if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)
				|| value.ValueKind != JsonValueKind.Object) return Rectangle.Empty;
			var x = Number(value, "x");
			var y = Number(value, "y");
			var width = Number(value, "width");
			var height = Number(value, "height");
			return x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
				&& width is >= 0 and <= int.MaxValue && height is >= 0 and <= int.MaxValue
				? new Rectangle((int)x, (int)y, (int)width, (int)height) : Rectangle.Empty;
		}

		internal static WaylandWindowInfo Parse(JsonElement item, nint handle)
			=> new(handle, Text(item, "id"), Text(item, "title"),
				item.TryGetProperty("appId", out _) ? Text(item, "appId") : Text(item, "class"),
				Number(item, "pid"), Bounds(item, "frame"), Bounds(item, "client"), Bounds(item, "buffer"),
				Bool(item, "active"), Bool(item, "minimized"), Bool(item, "maximized"),
				Bool(item, "visible", true), Bool(item, "alwaysOnTop"), Bool(item, "decorated"),
				item.TryGetProperty("transparency", out _) ? Number(item, "transparency") : -1L,
				Bool(item, "onCurrentWorkspace", true),
				parentHandle: (nint)Number(item, "parent"), topLevelHandle: (nint)Number(item, "topLevel"),
				validFields: item.TryGetProperty("validFields", out var fields) && fields.ValueKind == JsonValueKind.Array
					? fields.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.String)
						.Select(f => f.GetString()).ToHashSet(StringComparer.Ordinal) : null);

		internal static bool TrySingle(string json, Func<string, nint> resolve, out WaylandWindowInfo window)
		{
			window = null;
			if (string.IsNullOrEmpty(json)) return false;
			try
			{
				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;
				if (!Bool(root, "ok") || !root.TryGetProperty("window", out var item)
					|| item.ValueKind != JsonValueKind.Object) return false;
				var id = Text(item, "id");
				if (string.IsNullOrEmpty(id)) return false;
				var handle = resolve(id);
				if (handle == 0) return false;
				window = Parse(item, handle);
				return true;
			}
			catch (JsonException) { return false; }
		}
	}
}
#endif
