#if LINUX
using System.Text.Json;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Internals.Input.Linux
{
	internal sealed class DesktopKeyboardSnapshot
	{
		internal uint Group { get; init; }
		internal bool GroupKnown { get; init; }
		internal uint Modifiers { get; init; }
		internal bool ModifiersKnown { get; init; }
		internal bool IndicatorsKnown { get; init; }
		internal bool CapsLock { get; init; }
		internal bool NumLock { get; init; }
		internal bool ScrollLock { get; init; }
		internal string Keymap { get; init; }
		internal string MapRevision { get; init; }
		internal int[] PointerMapping { get; init; } = [];
	}

	internal sealed class DesktopKeyboardState
	{
		internal static readonly DesktopKeyboardState X11 = new(() => DesktopClient.QueryKeyboardState("x11"));
		internal static readonly DesktopKeyboardState Wayland = new(() => DesktopClient.QueryKeyboardState("auto"));
		internal static DesktopKeyboardState Current => Platform.Desktop.IsWaylandSession ? Wayland : X11;
		private readonly object gate = new();
		private readonly Func<string> query;
		private readonly Func<long> clock;
		private DesktopKeyboardSnapshot snapshot;
		private DesktopKeyboardSnapshot previousKeymap;
		private long refreshAt;

		internal DesktopKeyboardState(Func<string> query, Func<long> clock = null)
		{
			this.query = query;
			this.clock = clock ?? (() => Environment.TickCount64);
		}

		internal DesktopKeyboardSnapshot Get()
		{
			lock (gate)
			{
				var now = clock();
				if (now < refreshAt) return snapshot;
				snapshot = Parse(query(), previousKeymap);
				if (snapshot?.Keymap != null) previousKeymap = snapshot;
				// Share one state query across translations and modifier/indicator lookups.
				refreshAt = now + (snapshot == null ? 250 : 16);
				return snapshot;
			}
		}

		internal static DesktopKeyboardSnapshot Parse(string json, DesktopKeyboardSnapshot previous = null)
		{
			if (string.IsNullOrEmpty(json)) return null;
			try
			{
				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;
				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok)
					|| ok.ValueKind != JsonValueKind.True) return null;
				var revision = root.TryGetProperty("mapRevision", out var rev) && rev.ValueKind == JsonValueKind.String ? rev.GetString() : null;
				var keymap = root.TryGetProperty("keymap", out var map) && map.ValueKind == JsonValueKind.String ? map.GetString() : null;
				if (keymap == null && !string.IsNullOrEmpty(revision) && previous?.MapRevision == revision) keymap = previous.Keymap;
				return new DesktopKeyboardSnapshot
				{
					Group = Number(root, "group"),
					GroupKnown = HasNumber(root, "group"),
					Modifiers = Number(root, "modifiers"),
					ModifiersKnown = HasNumber(root, "modifiers"),
					IndicatorsKnown = HasFlag(root, "capsLock") && HasFlag(root, "numLock") && HasFlag(root, "scrollLock"),
					CapsLock = Flag(root, "capsLock"),
					NumLock = Flag(root, "numLock"),
					ScrollLock = Flag(root, "scrollLock"),
					Keymap = keymap,
					MapRevision = revision,
					PointerMapping = PointerMapping(root)
				};
			}
			catch (JsonException) { return null; }
			catch (InvalidOperationException) { return null; }
		}

		private static uint Number(JsonElement root, string name)
			=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number) ? number : 0;

		private static bool Flag(JsonElement root, string name)
			=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

		private static bool HasNumber(JsonElement root, string name)
			=> HasField(root, name) && root.GetProperty(name) is var value && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out _);

		private static bool HasFlag(JsonElement root, string name)
			=> HasField(root, name) && root.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False;

		private static int[] PointerMapping(JsonElement root)
		{
			if (!HasField(root, "pointerMapping")) return [];
			var mapping = root.GetProperty("pointerMapping");
			if (mapping.ValueKind != JsonValueKind.Array || mapping.GetArrayLength() > 256) return [];
			var buttons = new int[mapping.GetArrayLength()];
			for (var i = 0; i < buttons.Length; i++)
				if (mapping[i].ValueKind != JsonValueKind.Number || !mapping[i].TryGetInt32(out buttons[i]) || buttons[i] is < 0 or > 255) return [];
			return buttons;
		}

		private static bool HasField(JsonElement root, string name)
			=> root.TryGetProperty(name, out _) && (!root.TryGetProperty("validFields", out var fields)
				|| fields.ValueKind == JsonValueKind.Array && fields.EnumerateArray().Any(field => field.ValueKind == JsonValueKind.String && field.GetString() == name));
	}
}
#endif
