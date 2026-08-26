using Keysharp.Builtins;

#if LINUX
namespace Keysharp.Internals.Window.Linux.Wayland
{
	using System.Runtime.InteropServices;

	internal sealed class WaylandToplevel
	{
		internal string AppId { get; set; } = string.Empty;
		internal bool Closed { get; set; }
		internal nint CosmicProxy { get; set; }
		internal bool CosmicReady { get; set; }
		internal Dictionary<nint, Rectangle> GeometryByOutput { get; } = [];
		internal nint Handle { get; set; }
		internal string Identifier { get; set; } = string.Empty;
		internal GCHandle ListenerHandle { get; set; }
		internal string PendingAppId { get; set; }
		internal Dictionary<nint, Rectangle> PendingGeometryByOutput { get; set; }
		internal string PendingIdentifier { get; set; }
		internal uint? PendingState { get; set; }
		internal string PendingTitle { get; set; }
		internal nint Proxy { get; set; }
		internal WaylandForeignToplevelProtocol Protocol { get; set; }
		internal string Title { get; set; } = string.Empty;
		internal uint State { get; set; }

		// zwlr_foreign_toplevel_handle_v1 state enum: maximized=0, minimized=1, activated=2, fullscreen=3.
		// Each entry value n is encoded as bit n of State so membership tests are a single mask check.
		internal bool Activated => (State & (1u << 2)) != 0;
		internal bool Fullscreen => (State & (1u << 3)) != 0;
		internal bool Maximized => (State & 1u) != 0;
		internal bool Minimized => (State & (1u << 1)) != 0;
	}

	internal enum WaylandForeignToplevelProtocol
	{
		Ext,
		Wlr
	}
}
#endif
