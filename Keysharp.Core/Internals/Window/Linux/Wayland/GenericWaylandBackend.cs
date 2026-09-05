#if LINUX
using System.Text.Json;

namespace Keysharp.Internals.Window.Linux.Wayland
{
	/// <summary>
	/// The keysharp-desktop backend for Wayland compositors with no extension of their
	/// own: sway, Hyprland, COSMIC, niri, river and the rest.
	///
	/// This is a different position from every other backend here, and the difference
	/// is worth understanding before using it. GNOME and Cinnamon run code INSIDE the
	/// compositor and sit on the privileged side of every Wayland restriction. This one
	/// is an ordinary client on the outside, so it can do only what a compositor has
	/// chosen to expose to one: capture outputs, drive an advertised virtual
	/// pointer, read the clipboard, and inspect or control advertised toplevels.
	///
	/// Other operations remain impossible. No Wayland protocol lets
	/// one client restack another client's window, set its geometry or opacity, keep it
	/// above, change its decoration, learn a pid to signal it, or correlate a toplevel
	/// to the process about to create it.
	/// </summary>
	internal sealed class GenericWaylandBackend : IWaylandBackend
	{
		private const string BackendName = "generic";
		private const long HandleTag = 0x0800_0000_0000_0000L;
		private readonly object handleLock = new();
		private readonly Dictionary<string, nint> handlesById = new(StringComparer.Ordinal);
		private readonly Dictionary<nint, string> idsByHandle = [];
		private long nextHandle;

		public string Name => "generic Wayland (keysharp-desktop)";

		public bool SupportsWindowEvents => true;

		public IDisposable SubscribeWindowEvents(Action<WaylandWindowEvent> sink)
			=> sink != null ? new WaylandPollingEventSource(this, sink) : null;

		/// <summary>
		/// Whether the broker will serve this session, asked rather than inferred. What
		/// a Wayland compositor implements is not knowable from its name -- two of the
		/// same kind differ -- so the daemon probes what is actually advertised and
		/// registers that, and asking is the only answer that cannot be wrong.
		/// </summary>
		internal static bool IsAvailable()
			=> DesktopClient.ProbeProvider(BackendName);

		/// <summary>
		/// The window list carries the facts exposed by the protocol selected by the
		/// broker. ext-foreign-toplevel-list supplies only title, app id and an opaque
		/// identifier; wlroots and COSMIC can additionally supply state or geometry.
		/// Missing facts remain absent in the JSON rather than being reported as zeros.
		///
		/// Null when the broker cannot serve it, as with every other backend.
		/// </summary>
		internal static string QueryWindowList(bool includeHidden)
			=> DesktopClient.QueryWindowList(BackendName, includeHidden);

		public bool TryListWindows(bool includeHidden, out IReadOnlyList<WaylandWindowInfo> windows)
			=> TryParseWindowList(QueryWindowList(includeHidden), out windows);

		internal bool TryParseWindowList(string json, out IReadOnlyList<WaylandWindowInfo> windows)
		{
			windows = [];

			if (string.IsNullOrEmpty(json))
				return false;

			try
			{
				using var document = JsonDocument.Parse(json);
				var root = document.RootElement;

				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
					|| !root.TryGetProperty("windows", out var array)
					|| array.ValueKind != JsonValueKind.Array)
					return false;

				var parsed = new List<WaylandWindowInfo>();

				foreach (var item in array.EnumerateArray())
				{
					if (item.ValueKind != JsonValueKind.Object || !TryString(item, "id", out var id) || string.IsNullOrEmpty(id))
						continue;

					parsed.Add(DesktopWindowParser.Parse(item, GetOrCreateHandle(id)));
				}

				windows = parsed;
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		public bool TryGetWindow(nint handle, out WaylandWindowInfo window)
		{
			window = null;
			return TryGetNumericId(handle, out var id)
				&& DesktopWindowParser.TrySingle(DesktopClient.QueryWindow(BackendName, id),
					GetOrCreateHandle, out window) && window.Handle == handle;
		}

		public bool TryGetActiveWindow(out WaylandWindowInfo window)
		{
			window = null;

			if (!TryListWindows(true, out var windows))
				return false;

			window = windows.FirstOrDefault(candidate => candidate.Active);
			return window != null;
		}

		public bool TryActivateWindow(nint handle)
			=> TryGetNumericId(handle, out var id)
			   && DesktopClient.FocusWindow(BackendName, id);

		public bool TrySetWindowState(nint handle, FormWindowState state)
			=> TryGetNumericId(handle, out var id)
			   && DesktopClient.SetWindowState(BackendName, id,
				   WaylandWindowStateProtocol.ToShellExtensionState(state));

		public bool TryCloseWindow(nint handle)
			=> TryGetNumericId(handle, out var id)
			   && DesktopClient.CloseWindow(BackendName, id);

		public bool IsKnown(nint handle)
		{
			lock (handleLock)
				return idsByHandle.ContainsKey(handle);
		}

		public bool TryGetCursorPos(out int x, out int y)
			=> DesktopClient.QueryCursorPosition(BackendName, out x, out y);

		public bool SupportsMouse
			=> DesktopClient.ProviderSupportsAbsolutePointer(BackendName);

		public bool TrySendMouseMoveAbsolute(int x, int y)
			=> DesktopClient.SendMouseMoveAbsolute(BackendName, x, y);

		private nint GetOrCreateHandle(string id)
		{
			lock (handleLock)
			{
				if (handlesById.TryGetValue(id, out var handle))
					return handle;

				handle = new nint(HandleTag | ++nextHandle);
				handlesById[id] = handle;
				idsByHandle[handle] = id;
				return handle;
			}
		}

		private static string String(JsonElement element, string property)
			=> TryString(element, property, out var value) ? value : string.Empty;

		private static bool TryString(JsonElement element, string property, out string value)
		{
			value = string.Empty;

			if (!element.TryGetProperty(property, out var item))
				return false;

			value = item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString();
			return true;
		}

		private bool TryGetNumericId(nint handle, out ulong id)
		{
			id = 0;
			lock (handleLock)
				return idsByHandle.TryGetValue(handle, out var value)
					&& ulong.TryParse(value, out id);
		}

		private static bool Bool(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value)
			   && (value.ValueKind == JsonValueKind.True
				   || value.ValueKind == JsonValueKind.Number
				   && value.TryGetInt32(out var number) && number != 0);

		private static Rectangle JsonRectangle(JsonElement element, string property)
		{
			if (!element.TryGetProperty(property, out var value)
					|| value.ValueKind != JsonValueKind.Object)
				return Rectangle.Empty;

			return new Rectangle(Int(value, "x"), Int(value, "y"),
				Int(value, "width"), Int(value, "height"));
		}

		private static int Int(JsonElement element, string property)
			=> element.TryGetProperty(property, out var value)
				&& value.ValueKind == JsonValueKind.Number
				&& value.TryGetInt32(out var number) ? number : 0;

		/// <summary>
		/// Reads only, and not through a portal: ext-data-control asks for no consent
		/// dialog and shows none, because a compositor that offers the protocol has
		/// already decided a client which can bind it may read the selection.
		///
		/// Clipboard writes use the broker's persistent selection owner when supported.
		/// </summary>
		internal static string[] ClipboardMimetypes()
			=> DesktopClient.GetClipboardMimetypes(BackendName);

		internal static byte[] ClipboardContent(string mimetype)
			=> DesktopClient.GetClipboardContent(BackendName, mimetype);

		internal static string ClipboardText()
			=> DesktopClient.GetClipboardText(BackendName);
	}
}
#endif
