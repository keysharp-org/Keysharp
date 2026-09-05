#if LINUX
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Internals
{
	internal sealed class X11Window : LinuxWindow
	{
		private static DesktopBackend Broker => DesktopBackend.X11;
		private static WaylandWindowInfo Query(nint handle)
			=> Broker.TryGetWindow(handle, out var window) ? window : null;
		private static bool Valid(nint handle) => Broker.IsKnown(handle);

		public override string GetTitle(nint h)
			=> TryOwnControl(h, out _) ? base.GetTitle(h) : Query(h)?.Title ?? "";
		public override string GetClassName(nint h)
			=> TryOwnControl(h, out _) ? base.GetClassName(h) : Query(h)?.ClassName ?? "";
		public override long GetPid(nint h)
			=> TryOwnControl(h, out _) ? base.GetPid(h) : Query(h)?.PID ?? 0;
		public override Rectangle GetBounds(nint h)
		{
			if (!TryOwnControl(h, out var control)) return Query(h)?.Bounds ?? Rectangle.Empty;
			var bounds = base.GetBounds(h);
			return control is Form ? X11DisplayTopology.FromToolkitBounds(ScreenRect.FromRectangle(bounds)).ToRectangle() : bounds;
		}
		public override Rectangle GetClientBounds(nint h)
			=> TryOwnControl(h, out _) ? X11DisplayTopology.FromToolkitBounds(
				ScreenRect.FromRectangle(base.GetClientBounds(h))).ToRectangle() : Query(h)?.ClientBounds ?? Rectangle.Empty;
		public override long GetStyle(nint h)
			=> TryOwnControl(h, out _) ? base.GetStyle(h) : Query(h)?.Style ?? 0;
		public override long GetExStyle(nint h)
			=> TryOwnControl(h, out _) ? base.GetExStyle(h) : 0;
		public override bool GetActive(nint h)
			=> TryOwnControl(h, out _) ? base.GetActive(h) : Query(h)?.Active == true;
		public override bool GetVisible(nint h)
			=> TryOwnControl(h, out _) ? base.GetVisible(h) : Query(h)?.Visible == true;
		public override bool GetEnabled(nint h)
			=> TryOwnControl(h, out _) ? base.GetEnabled(h) : Query(h)?.Enabled == true;
		public override bool GetHung(nint h)
			=> TryOwnControl(h, out _) && base.GetHung(h);
		public override bool GetExists(nint h)
			=> TryOwnControl(h, out _) ? base.GetExists(h) : Query(h) != null;
		public override bool IsWindow(nint h) => GetExists(h);
		public override FormWindowState GetWindowState(nint h)
			=> TryOwnControl(h, out _) ? base.GetWindowState(h) : Query(h)?.WindowState ?? FormWindowState.Normal;
		public override bool GetAlwaysOnTop(nint h)
			=> TryOwnControl(h, out _) ? base.GetAlwaysOnTop(h) : Query(h)?.AlwaysOnTop == true;
		public override object GetTransparency(nint h)
			=> TryOwnControl(h, out _) ? base.GetTransparency(h) : Query(h)?.Transparency ?? -1L;
		public override object GetTransparentColor(nint h)
			=> TryOwnControl(h, out _) ? base.GetTransparentColor(h) : 0L;
		public override POINT ClientToScreen(nint h)
			=> TryOwnControl(h, out _) ? base.ClientToScreen(h) : Query(h)?.ClientToScreen() ?? default;
		public override bool TryClientToScreen(nint h, ref Point pt)
		{
			if (TryOwnControl(h, out _)) return base.TryClientToScreen(h, ref pt);
			if (Query(h) is not { } window) return false;
			pt = new Point(pt.X + window.ClientBounds.X, pt.Y + window.ClientBounds.Y);
			return true;
		}
		public override bool TryGetParent(nint h, out nint parent)
		{
			if (TryOwnControl(h, out _)) return base.TryGetParent(h, out parent);
			parent = Query(h)?.ParentHandle ?? 0;
			return parent != 0;
		}
		public override bool TryGetTopLevel(nint h, out nint top)
		{
			if (TryOwnControl(h, out _)) return base.TryGetTopLevel(h, out top);
			top = Query(h)?.TopLevelHandle ?? 0;
			return top != 0;
		}
		public override bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
			if (TryOwnControl(h, out _)) return base.TryEnumerateChildren(h, out children);
			return Broker.TryChildren(h, out children);
		}
		public override bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
			if (TryOwnControl(h, out _)) return base.TryGetText(h, detectHidden, out text);
			text = [];
			if (!Broker.TryChildren(h, out var children)) return false;
			var pending = new Queue<nint>(children);
			var seen = new HashSet<nint>();
			while (pending.TryDequeue(out var child) && seen.Count < 4096)
			{
				if (!seen.Add(child) || Query(child) is not { } window) continue;
				if ((detectHidden || window.Visible) && !string.IsNullOrEmpty(window.Title)) text.Add(window.Title);
				if (Broker.TryChildren(child, out var descendants))
					foreach (var descendant in descendants) pending.Enqueue(descendant);
			}
			return true;
		}
		public override void ChildFindPoint(nint h, PointAndHwnd point)
		{
			if (TryOwnControl(h, out _)) { base.ChildFindPoint(h, point); return; }
			if (!Broker.TryGetWindowAt(point.pt.X, point.pt.Y, true, out var child)
				|| child.Handle == h || child.TopLevelHandle != h) return;
			point.hwndFound = child.Handle;
			point.rectFound = child.Bounds;
		}
		public override nint GetForegroundHandle()
			=> Broker.TryGetActiveWindow(out var window) ? window.Handle : 0;
		public override WindowInfoBase CreateWindow(nint id)
			=> TryOwnControl(id, out _) ? base.CreateWindow(id) : Query(id) is { } window ? window : new WindowInfo(id);
		public override WindowInfoBase ActiveWindow()
			=> Broker.TryGetActiveWindow(out var window) ? window : new WindowInfo(0);
		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
			=> Broker.TryListWindows(includeHidden, out var windows) ? windows.Reverse().Cast<WindowInfoBase>().ToArray() : [];
		public override bool TryGetAt(int x, int y, out nint child)
		{
			var found = Broker.TryGetWindowAt(x, y, true, out var window);
			child = found ? window.Handle : 0;
			return found;
		}
		public override WindowInfoBase WindowAt(int x, int y)
			=> Broker.TryGetWindowAt(x, y, out var window) ? window : null;
		public override uint GetFocusedControlThread(nint window = 0) => 0;

		public override bool TrySetAlwaysOnTop(nint h, bool value)
			=> TryOwnControl(h, out _) ? base.TrySetAlwaysOnTop(h, value)
				: Broker.TrySetAlwaysOnTop(h, value);
		public override bool TryClose(nint h)
			=> TryOwnControl(h, out _) ? base.TryClose(h) : Broker.TryCloseWindow(h);
		public override bool TryKill(nint h)
			=> TryOwnControl(h, out _) ? base.TryKill(h) : Broker.TryKillWindow(h);
		public override bool TrySetZOrder(nint h, ZOrder value)
			=> TryOwnControl(h, out _) ? base.TrySetZOrder(h, value)
				: Broker.TrySetZOrder(h, value);
		public override bool TryHide(nint h) => TrySetVisible(h, false);
		public override bool TryShow(nint h) => TrySetVisible(h, true);
		public override bool TryRedraw(nint h)
			=> TryOwnControl(h, out _) ? base.TryRedraw(h) : Broker.TryRedrawWindow(h);
		public override bool TrySetState(nint h, FormWindowState state)
			=> TryOwnControl(h, out _) ? base.TrySetState(h, state) : Broker.TrySetWindowState(h, state);
		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			if (TryOwnControl(h, out var control))
			{
				if (control is Form) bounds = X11DisplayTopology.ToToolkitBounds(ScreenRect.FromRectangle(bounds)).ToRectangle();
				return base.TryMoveResize(h, bounds, setPos, setSize);
			}
			if (!Valid(h)) return false;
			if (!setPos || !setSize)
			{
				if (Query(h) is not { } current) return false;
				bounds = new Rectangle(setPos ? bounds.X : current.Bounds.X, setPos ? bounds.Y : current.Bounds.Y,
					setSize ? bounds.Width : current.Bounds.Width, setSize ? bounds.Height : current.Bounds.Height);
			}
			return Broker.TryMoveResizeWindow(h, bounds, true, true);
		}
		public override bool TryActivate(nint h)
			=> TryOwnControl(h, out _) ? base.TryActivate(h) : Broker.TryActivateWindow(h);
		public override bool TrySetStyle(nint h, long style)
			=> TryOwnControl(h, out _) ? base.TrySetStyle(h, style)
				: Broker.TrySetNoBorder(h, (style & 0x00C00000L) == 0);
		public override bool TrySetExStyle(nint h, long style)
			=> TryOwnControl(h, out _) && base.TrySetExStyle(h, style);
		public override bool TrySetTransparency(nint h, object alpha)
			=> TryOwnControl(h, out _) ? base.TrySetTransparency(h, alpha) : Broker.TrySetTransparency(h, alpha);
		public override bool TryClick(nint h, Point at, uint button, int count)
			=> TryOwnControl(h, out _) ? base.TryClick(h, at, button, count)
				: Broker.TryClickWindow(h, at, button, count);
		public override bool TrySetTitle(nint h, string title)
			=> TryOwnControl(h, out _) ? base.TrySetTitle(h, title) : Broker.TrySetWindowTitle(h, title);
		public override bool TrySetVisible(nint h, bool visible)
			=> TryOwnControl(h, out _) ? base.TrySetVisible(h, visible) : Broker.TrySetWindowVisible(h, visible);
		public override bool TrySetEnabled(nint h, bool enabled)
			=> TryOwnControl(h, out _) && base.TrySetEnabled(h, enabled);
		public override bool TrySetTransparentColor(nint h, object color)
			=> TryOwnControl(h, out _) && base.TrySetTransparentColor(h, color);
	}
}
#endif
