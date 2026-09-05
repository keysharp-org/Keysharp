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
		internal static readonly DesktopKeyboardState X11 = new(
			revision => DesktopClient.QueryKeyboardState(revision));
		internal static readonly DesktopKeyboardState Wayland = new(
			revision => DesktopClient.QueryKeyboardState(revision));
		internal static DesktopKeyboardState Current => Platform.Desktop.IsWaylandSession ? Wayland : X11;
		private readonly object gate = new();
		private readonly Func<string, byte[]> query;
		private readonly Func<long> clock;
		private readonly Action<Action> scheduleRefresh;
		private DesktopKeyboardSnapshot snapshot;
		private DesktopKeyboardSnapshot previousKeymap;
		private long refreshAt;
		private bool refreshRunning;

		internal DesktopKeyboardState(Func<string, byte[]> query, Func<long> clock = null,
			Action<Action> scheduleRefresh = null)
		{
			this.query = query;
			this.clock = clock ?? (() => Environment.TickCount64);
			this.scheduleRefresh = scheduleRefresh ?? (action => Task.Run(action));
		}

		internal DesktopKeyboardSnapshot Get()
		{
			lock (gate)
			{
				if (!refreshRunning && clock() >= refreshAt)
				{
					refreshRunning = true;
					scheduleRefresh(Refresh);
				}
				return snapshot;
			}
		}

		private void Refresh()
		{
			var previous = previousKeymap;

			DesktopKeyboardSnapshot refreshed = null;

			try
			{
				refreshed = Parse(query(previous?.MapRevision), previous);
			}
			catch
			{
			}

			lock (gate)
			{
				if (refreshed != null)
				{
					snapshot = refreshed;

					if (refreshed.Keymap != null)
						previousKeymap = refreshed;
				}

				refreshAt = clock() + (refreshed == null ? 250 : 16);
				refreshRunning = false;
			}
		}

		internal static DesktopKeyboardSnapshot Parse(string json, DesktopKeyboardSnapshot previous = null)
			=> Parse(System.Text.Encoding.UTF8.GetBytes(json ?? ""), previous);

		internal static DesktopKeyboardSnapshot Parse(ReadOnlyMemory<byte> json,
			DesktopKeyboardSnapshot previous = null)
		{
			if (json.IsEmpty) return null;
			try
			{
				using var document = JsonDocument.Parse(json);
				return Parse(document.RootElement, previous);
			}
			catch (JsonException) { return null; }
			catch (InvalidOperationException) { return null; }
		}

		private static DesktopKeyboardSnapshot Parse(JsonElement root,
			DesktopKeyboardSnapshot previous)
		{
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
