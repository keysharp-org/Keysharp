using Keysharp.Builtins;

namespace Keysharp.Internals
{

#if LINUX
	internal static class LinuxWindows
	{
		internal static IWindow Resolve()
		{
			if (IsWaylandSession)
				return new WaylandWindow(WaylandBackend.Current);

			if (IsX11Available)
				return new X11Window();

			return new LinuxWindow();
		}
	}

	internal class LinuxWindow : WindowBase
	{
		public override WindowInfoBase CreateWindow(nint id)
			=> TryOwnControl(id, out _) ? base.CreateWindow(id) : new WindowInfo(id);

		public override WindowInfoBase ActiveWindow() => new WindowInfo(0);

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden) => [];

		public override bool TryGetAt(int x, int y, out nint child)
		{
			child = default;
			return false;
		}

		public override nint GetForegroundHandle() => 0;

		public override uint GetFocusedControlThread(nint window = 0)
		{
			return 0;
		}
	}

	internal sealed class WaylandWindow : LinuxWindow
	{
		private const long WsCaption = 0x00C00000L;
		private readonly IWaylandBackend wayland;

		internal WaylandWindow(IWaylandBackend wayland) => this.wayland = wayland;

		private bool Backend(nint h, out WaylandWindowInfo info)
		{
			info = null;
			return wayland != null && wayland.TryGetWindow(h, out info);
		}

		// Membership only, zero IPC (bit-tag / id-map check). Guards routing decisions; Backend(h, out info)
		// is reserved for the sites that consume the fetched info.
		private bool Known(nint h)
			=> wayland != null && wayland.IsKnown(h);

		// One of OUR OWN top-level windows lives in Eto/GTK land under a native handle the compositor backend
		// doesn't key on, so Backend(h) misses and Eto's self position/query (which is a Wayland no-op) is used.
		// Correlate such a handle to its compositor window so self-window verbs (move, bounds) take the same
		// compositor path foreign windows do. Returns false for child controls and non-own handles.
		private bool OwnBackend(nint h, out WaylandWindowInfo info)
		{
			info = null;

			if (wayland == null || !TryOwnControl(h, out var ctrl) || ctrl is not Form form)
				return false;

			var size = form.GetSize();
			return WaylandOwnToplevels.TryGetCompositorHandle(form, form.Title, size.Width, size.Height, out var compHandle)
				   && wayland.TryGetWindow(compHandle, out info);
		}

		// Used by getters whose Wayland answer is a constant. Membership is a cheap tagged-handle/map check and
		// does not make a broker request.
		private bool IsWayland(nint h)
			=> Known(h);

		public override string GetTitle(nint h)
		{
			if (Backend(h, out var info)) return info.Title ?? DefaultObject;
			if (TryOwnControl(h, out _)) return base.GetTitle(h);
			return DefaultObject;
		}

		public override string GetClassName(nint h)
		{
			if (Backend(h, out var info)) return info.ClassName;
			if (TryOwnControl(h, out _)) return base.GetClassName(h);
			return DefaultErrorString;
		}

		public override long GetPid(nint h)
		{
			if (Backend(h, out var info)) return info.PID;
			if (TryOwnControl(h, out _)) return base.GetPid(h);
			return 0L;
		}

		public override Rectangle GetBounds(nint h)
		{
			if (Backend(h, out var info)) return info.Bounds;
			if (OwnBackend(h, out var own)) return own.Bounds;   // real compositor position, not Eto's no-op location
			if (TryOwnControl(h, out _)) return base.GetBounds(h);
			return Rectangle.Empty;
		}

		public override Rectangle GetClientBounds(nint h)
		{
			if (Backend(h, out var info)) return info.ClientBounds;
			if (OwnBackend(h, out var own)) return OwnClientBounds(h, own);
			if (TryOwnControl(h, out _)) return base.GetClientBounds(h);
			return Rectangle.Empty;
		}

		public override long GetStyle(nint h)
		{
			// Wayland has no Win32 styles; the only one with a real equivalent is WS_CAPTION <-> decoration.
			if (Backend(h, out var info)) return info.Style;
			if (TryOwnControl(h, out _)) return base.GetStyle(h);
			return 0L;
		}

		public override long GetExStyle(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetExStyle(h);
			if (IsWayland(h)) return 0L;
			return 0L;
		}

		public override bool GetActive(nint h)
		{
			if (Backend(h, out var info)) return info.Active;
			if (TryOwnControl(h, out _)) return base.GetActive(h);
			return false;
		}

		public override bool GetVisible(nint h)
		{
			if (Backend(h, out var info)) return info.Visible;
			if (TryOwnControl(h, out _)) return base.GetVisible(h);
			return false;
		}

		public override bool GetEnabled(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetEnabled(h);
			if (IsWayland(h)) return true;
			return false;
		}

		public override bool GetHung(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetHung(h);
			if (IsWayland(h)) return false;
			return false;
		}

		public override bool GetExists(nint h)
		{
			if (Backend(h, out _)) return true;
			if (TryOwnControl(h, out _)) return base.GetExists(h);
			return false;
		}

		public override FormWindowState GetWindowState(nint h)
		{
			if (Backend(h, out var info)) return info.WindowState;

			if (TryOwnControl(h, out _)) return base.GetWindowState(h);
			return FormWindowState.Normal;
		}

		public override bool GetAlwaysOnTop(nint h)
		{
			if (Backend(h, out var info)) return info.AlwaysOnTop;
			if (TryOwnControl(h, out _)) return base.GetAlwaysOnTop(h);
			return false;
		}

		public override object GetTransparency(nint h)
		{
			if (Backend(h, out var info)) return info.Transparency;
			if (OwnBackend(h, out var own)) return own.Transparency;   // the compositor holds our own opacity too
			if (TryOwnControl(h, out _)) return base.GetTransparency(h);
			// -1L is the "no explicit transparency set" sentinel (WinGetTransparent -> ""), matching Windows/X11.
			if (IsWayland(h)) return -1L;
			return -1L;
		}

		public override object GetTransparentColor(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetTransparentColor(h);
			if (IsWayland(h)) return 0L;
			return 0L;
		}

		public override POINT ClientToScreen(nint h)
		{
			if (Backend(h, out var info)) { var r = info.ClientGeometry; return new POINT(r.X, r.Y); }
			if (OwnBackend(h, out var own)) { var r = OwnClientBounds(h, own); return new POINT(r.X, r.Y); }
			if (TryOwnControl(h, out _)) return base.ClientToScreen(h);
			return new POINT(0, 0);
		}

		public override bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
			if (TryOwnControl(h, out _)) return base.TryGetText(h, detectHidden, out text);
			if (IsWayland(h)) { text = []; return true; }
			text = [];
			return false;
		}

		public override void ChildFindPoint(nint h, PointAndHwnd pah)
		{
			if (TryOwnControl(h, out _)) { base.ChildFindPoint(h, pah); return; }
			if (IsWayland(h)) return;
		}

		public override bool TryClientToScreen(nint h, ref Point pt)
		{
			var origin = ClientToScreen(h);
			pt = new Point(pt.X + origin.X, pt.Y + origin.Y);
			return true;
		}

		/// <summary>
		/// Where our own window's client area sits, composed from the one thing each side knows: the compositor
		/// says where the surface is, the toolkit says where the content sits inside it.
		/// </summary>
		private Rectangle OwnClientBounds(nint h, WaylandWindowInfo own)
		{
			var frame = own.FrameGeometry;

			if (TryGetSurfaceOrigin(own, out var surface)
					&& TryOwnControl(h, out var ctrl) && ctrl is Form form && form.Content is Control content)
			{
				var offset = content.PointToScreen(Point.Empty);   // surface-relative, which is what we correct
				var size = form.ClientSize;

				if (size.Width > 0 && size.Height > 0)
					return new Rectangle(surface.X + (int)Math.Round(offset.X), surface.Y + (int)Math.Round(offset.Y),
										 size.Width, size.Height);
			}

			var local = TryOwnControl(h, out _) ? base.GetClientBounds(h) : Rectangle.Empty;

			if (local.Width > 0 && local.Height > 0 && frame.Width > 0 && frame.Height > 0)
			{
				// Without a surface rect the decoration can only be guessed at: assume it is even down the sides
				// and put the remainder on top, which is where a titlebar is.
				var width = Math.Min(local.Width, frame.Width);
				var height = Math.Min(local.Height, frame.Height);
				var side = Math.Max(0, (int)Math.Round((frame.Width - width) / 2.0));
				var top = Math.Max(0, frame.Height - height - side);
				return new Rectangle(frame.X + side, frame.Y + top, width, height);
			}

			return own.ClientGeometry.Width > 0 && own.ClientGeometry.Height > 0 ? own.ClientGeometry : frame;
		}

		/// <summary>The origin a Wayland client's own coordinates are relative to, when the compositor reports it.</summary>
		private static bool TryGetSurfaceOrigin(WaylandWindowInfo own, out Point origin)
		{
			var surface = own.SurfaceGeometry;
			origin = new Point(surface.X, surface.Y);
			return surface.Width > 0 && surface.Height > 0;
		}


		public override bool TryGetParent(nint h, out nint parent)
		{
			if (TryOwnControl(h, out _)) return base.TryGetParent(h, out parent);
			if (IsWayland(h)) { parent = default; return false; }
			parent = default;
			return false;
		}

		public override bool TryGetTopLevel(nint h, out nint top)
		{
			if (Known(h)) { top = h; return true; }   // a backend toplevel is its own top
			if (TryOwnControl(h, out _)) return base.TryGetTopLevel(h, out top);
			top = default;
			return false;
		}

		public override bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
			if (TryOwnControl(h, out _)) return base.TryEnumerateChildren(h, out children);
			if (IsWayland(h)) { children = []; return true; }
			children = [];
			return false;
		}

		public override nint GetForegroundHandle()
		{
			if (wayland?.TryGetActiveWindow(out var active) == true)
				return AsOwn(active).Handle;

			return 0;
		}

		public override bool IsWindow(nint h)
		{
			if (wayland?.TryGetWindow(h, out _) == true)
				return true;

			if (base.IsWindow(h))
				return true;

			return false;
		}

		public override bool TrySetAlwaysOnTop(nint h, bool onTop)
		{
			// Guard on MEMBERSHIP (zero IPC), not on IPC success: a compositor-IPC backend (KWin/GNOME/Cinnamon)
			// can return false on a transient bridge timeout for a window it genuinely manages. Returning that
			// false (so the neutral WindowInfo raises OSError) is correct; falling through to X11 would feed a
			// synthetic compositor id to Xlib as if it were an XID.
			if (Known(h))
				return wayland.TrySetAlwaysOnTop(h, onTop);

			if (TryOwnControl(h, out _))
				return base.TrySetAlwaysOnTop(h, onTop);
			return false;
		}

		public override bool TryClose(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop): never fall a backend id through to X11.
				return wayland.TryCloseWindow(h);

			if (TryOwnControl(h, out _))
				return base.TryClose(h);
			return false;
		}

		public override bool TryKill(nint h)
		{
			if (Known(h))
				return wayland.TryKillWindow(h);

			if (TryOwnControl(h, out _))
				return base.TryKill(h);
			return false;
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (Known(h))
				return wayland.TrySetZOrder(h, z);

			if (TryOwnControl(h, out _))
				return base.TrySetZOrder(h, z);
			return false;
		}

		public override bool TryHide(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop).
				return wayland.TrySetWindowState(h, FormWindowState.Minimized);

			if (TryOwnControl(h, out _))
				return base.TryHide(h);
			return false;
		}

		public override bool TryShow(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop).
				return wayland.TrySetWindowState(h, FormWindowState.Normal);

			if (TryOwnControl(h, out _))
				return base.TryShow(h);
			return false;
		}

		public override bool TryRedraw(nint h)
		{
			if (Known(h))   // WaylandWindowItem.Redraw => false
				return false;

			if (TryOwnControl(h, out _))
				return base.TryRedraw(h);
			return false;
		}

		public override bool TrySetState(nint h, FormWindowState state)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop).
				return wayland.TrySetWindowState(h, state);

			if (TryOwnControl(h, out _))
				return base.TrySetState(h, state);
			return false;
		}

		// The remaining control verbs reuse the proven X11 read/write logic via the directly-constructed X11
		// helper (NOT WindowQuery.CreateWindow → no recursion), with the Wayland branches folded in front.

		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			if (Backend(h, out var info))
			{
				var rect = info.FrameGeometry;

				if (bounds.X != WindowInfoBase.Unchanged) rect.X = bounds.X;
				if (bounds.Y != WindowInfoBase.Unchanged) rect.Y = bounds.Y;
				if (bounds.Width != WindowInfoBase.Unchanged) rect.Width = bounds.Width;
				if (bounds.Height != WindowInfoBase.Unchanged) rect.Height = bounds.Height;

				if (!wayland.TryMoveResizeWindow(h, rect, setPos, setSize))
					return false;

				if (setPos)
					// If WaylandOwnToplevels is still placing one of our own windows, let it converge here.
					WaylandOwnToplevels.NotifyExternalMove(h, bounds.X, bounds.Y);

				return true;
			}

			if (TryOwnControl(h, out _))
			{
				// A Wayland client may resize itself (Eto), but it cannot set its own xdg-toplevel position
				// (control.SetLocation is a silent no-op), so route the move through the compositor backend the
				// same way Gui.Move() does. Resize still goes via Eto.
				if (setSize)
					_ = base.TryMoveResize(h, bounds, false, true);

				if (setPos)
				{
					if (!OwnBackend(h, out var own))
						return false;

					var rect = own.FrameGeometry;
					if (bounds.X != WindowInfoBase.Unchanged) rect.X = bounds.X;
					if (bounds.Y != WindowInfoBase.Unchanged) rect.Y = bounds.Y;

					if (!wayland.TryMoveResizeWindow(own.Handle, rect, true, false))
						return false;

					WaylandOwnToplevels.NotifyExternalMove(own.Handle, bounds.X, bounds.Y);
				}

				return true;
			}

			return false;
		}

		public override bool TryActivate(nint h)
		{
			if (Known(h))
				return wayland.TryActivateWindow(h);

			if (TryOwnControl(h, out _))
				return base.TryActivate(h);
			return false;
		}

		public override bool TrySetStyle(nint h, long style)
		{
			// Only WS_CAPTION maps to a Wayland concept (the compositor's decoration state).
			if (Known(h))
				return wayland.TrySetNoBorder(h, (style & WsCaption) != WsCaption);

			if (TryOwnControl(h, out _))
				return base.TrySetStyle(h, style);
			return false;
		}

		public override bool TrySetExStyle(nint h, long exStyle)
		{
			if (TryOwnControl(h, out _))
				return base.TrySetExStyle(h, exStyle);

			return false;
		}

		public override bool TrySetTransparency(nint h, object alpha)
		{
			if (Known(h))
				return wayland.TrySetTransparency(h, alpha);

			// A Wayland client cannot make its own surface translucent (GTK's window opacity is an X11-only path),
			// so route our own windows through the compositor backend the way WinMove does. Via the positioner
			// rather than OwnBackend directly, because it also holds the request for a window that has not been
			// mapped yet and reasserts it after a Hide/Show, which unmaps the window and drops the compositor's
			// opacity with it.
			if (TryOwnControl(h, out var ctrl) && ctrl is Form ownForm)
			{
				var size = ownForm.GetSize();
				return WaylandOwnToplevels.SetTransparency(ownForm, ownForm.Title, size.Width, size.Height, alpha);
			}

			return false;
		}

		public override bool TryClick(nint h, Point at, uint button, int count)
		{
			if (TryOwnControl(h, out _))
				return base.TryClick(h, at, button, count);
			return false;
		}

		public override bool TrySetTitle(nint h, string title)
		{
			if (TryOwnControl(h, out _))
				return base.TrySetTitle(h, title);
			return false;
		}

		public override bool TrySetVisible(nint h, bool visible)
		{
			if (Known(h))
				return wayland.TrySetWindowState(h, visible ? FormWindowState.Normal : FormWindowState.Minimized);

			if (TryOwnControl(h, out _))
				return base.TrySetVisible(h, visible);
			return false;
		}

		public override bool TrySetEnabled(nint h, bool enabled)
		{
			if (TryOwnControl(h, out _))
				return base.TrySetEnabled(h, enabled);
			return false;
		}

		public override bool TrySetTransparentColor(nint h, object color)
		{
			if (TryOwnControl(h, out _))
				return base.TrySetTransparentColor(h, color);
			return false;
		}

		public override WindowInfoBase CreateWindow(nint id)
		{
			if (Backend(id, out var info)) return info;
			if (TryOwnControl(id, out _)) return base.CreateWindow(id);
			return new WindowInfo(id);
		}

		/// <summary>
		/// The window as a script must see it. The compositor answers every query about one of OUR OWN windows
		/// under its own handle, but a script only ever holds the Eto one (that is what <c>Gui.Hwnd</c> returns),
		/// so anything a script can compare against <c>Gui.Hwnd</c> - or hand to <c>Control.FromHandle</c>, which
		/// is how MouseGetPos finds the control under the cursor - has to be re-homed onto that handle first.
		/// Leaves a foreign window, and one of ours that has not been correlated yet, exactly as it came.
		/// </summary>
		private static WindowInfoBase AsOwn(WindowInfoBase window)
			=> window != null && WaylandOwnToplevels.TryGetFormHandle(window.Handle, out var own)
			   ? new WindowInfo(own)
			   : window;

		public override WindowInfoBase ActiveWindow()
		{
			if (wayland?.TryGetActiveWindow(out var active) == true) return AsOwn(active);
			return new WindowInfo(0);
		}

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
		{
			var list = new List<WindowInfoBase>();
			if (wayland?.TryListWindows(includeHidden, out var backendWindows) == true)
			{
				foreach (var w in backendWindows)
					list.Add(AsOwn(w));
			}

			list.Reverse();
			return list;
		}

		public override bool TryGetAt(int x, int y, out nint child)
		{
			if (wayland?.TryGetWindowAt(x, y, out var info) == true)
			{
				child = AsOwn(info).Handle;
				return true;
			}

			child = default;
			return false;
		}

		public override WindowInfoBase WindowAt(int x, int y)
			=> wayland?.TryGetWindowAt(x, y, out var info) == true ? AsOwn(info) : null;

		public override uint GetFocusedControlThread(nint window = 0)
		{
			return 0;
		}
	}

#endif
}
