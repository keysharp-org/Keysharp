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

		// Foreign-toplevel handles are the ONLY negative window ids (allocated via nextHandle-- from -1); X11 XIDs
		// and KWin/GNOME synthetic ids are positive. So a non-negative handle can never be a foreign toplevel —
		// skip the wl_display Refresh roundtrip that Get/IsWindow would otherwise do on EVERY property read of an
		// X11/XWayland or compositor-backed window held under a Wayland session.
		private static WaylandToplevel Foreign(nint h)
			=> h < 0 ? WaylandForeignToplevels.Current?.Get(h) : null;

		// "Is this a Wayland window (compositor-backed or foreign-toplevel), i.e. NOT an X11 window?" Used by the
		// getters whose Wayland answer is a constant, so it must not pay a roundtrip: Known(h) is a cheap bit/dict
		// test, and a negative id is by construction a foreign toplevel.
		private bool IsWayland(nint h)
			=> Known(h) || h < 0;

		public override string GetTitle(nint h)
		{
			if (Backend(h, out var info)) return info.Title ?? DefaultObject;
			if (Foreign(h) is { } tl) return tl.Title ?? DefaultObject;
			if (TryOwnControl(h, out _)) return base.GetTitle(h);
			return DefaultObject;
		}

		public override string GetClassName(nint h)
		{
			if (Backend(h, out var info)) return info.ClassName;
			if (Foreign(h) is { } tl) return tl.AppId ?? DefaultObject;
			if (TryOwnControl(h, out _)) return base.GetClassName(h);
			return DefaultErrorString;
		}

		public override long GetPid(nint h)
		{
			if (Backend(h, out var info)) return info.PID;
			if (Foreign(h) != null) return 0L;
			if (TryOwnControl(h, out _)) return base.GetPid(h);
			return 0L;
		}

		public override Rectangle GetBounds(nint h)
		{
			if (Backend(h, out var info)) return info.Bounds;
			if (Foreign(h) != null) return Rectangle.Empty;
			if (OwnBackend(h, out var own)) return own.Bounds;   // real compositor position, not Eto's no-op location
			if (TryOwnControl(h, out _)) return base.GetBounds(h);
			return Rectangle.Empty;
		}

		public override Rectangle GetClientBounds(nint h)
		{
			if (Backend(h, out var info)) return info.ClientBounds;
			if (Foreign(h) != null) return Rectangle.Empty;
			if (OwnBackend(h, out var own)) return OwnClientBounds(h, own);
			if (TryOwnControl(h, out _)) return base.GetClientBounds(h);
			return Rectangle.Empty;
		}

		public override long GetStyle(nint h)
		{
			// Wayland has no Win32 styles; the only one with a real equivalent is WS_CAPTION <-> decoration.
			if (Backend(h, out var info)) return info.Style;
			if (Foreign(h) != null) return 0L;
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
			if (Foreign(h) is { } tl) return tl.Activated;
			if (TryOwnControl(h, out _)) return base.GetActive(h);
			return false;
		}

		public override bool GetVisible(nint h)
		{
			if (Backend(h, out var info)) return info.Visible;
			if (Foreign(h) != null) return true;   // a known foreign-toplevel is, by definition, mapped
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
			if (Backend(h, out _) || Foreign(h) != null) return true;
			if (TryOwnControl(h, out _)) return base.GetExists(h);
			return false;
		}

		public override FormWindowState GetWindowState(nint h)
		{
			if (Backend(h, out var info)) return info.WindowState;

			if (Foreign(h) is { } tl)
				return tl.Minimized ? FormWindowState.Minimized : tl.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;

			if (TryOwnControl(h, out _)) return base.GetWindowState(h);
			return FormWindowState.Normal;
		}

		public override bool GetAlwaysOnTop(nint h)
		{
			if (Backend(h, out var info)) return info.AlwaysOnTop;
			if (Foreign(h) != null) return false;   // no foreign-client way to read keep-above
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
			if (Foreign(h) != null) return new POINT(0, 0);
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
			if (Foreign(h) != null) { top = default; return false; }
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
				return active.Handle;

			return WaylandForeignToplevels.Current?.Active is WaylandToplevel fa ? fa.Handle : 0;
		}

		public override bool IsWindow(nint h)
		{
			if (wayland?.TryGetWindow(h, out _) == true)
				return true;

			if (WaylandForeignToplevels.Current?.IsWindow(h) == true)
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

			// Foreign-toplevel compositors (sway/Hyprland/wlroots/COSMIC) expose no keep-above protocol.
			if (WaylandForeignToplevels.Current?.IsWindow(h) == true)
				return false;

			if (TryOwnControl(h, out _))
				return base.TrySetAlwaysOnTop(h, onTop);
			return false;
		}

		public override bool TryClose(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop): never fall a backend id through to X11.
				return wayland.TryCloseWindow(h);

			var toplevels = WaylandForeignToplevels.Current;
			if (toplevels?.Get(h) is WaylandToplevel tl)
				return toplevels.Close(tl);

			if (TryOwnControl(h, out _))
				return base.TryClose(h);
			return false;
		}

		public override bool TryKill(nint h)
		{
			if (Known(h))
				return wayland.TryKillWindow(h);

			if (Foreign(h) != null)
				return TryClose(h);

			if (TryOwnControl(h, out _))
				return base.TryKill(h);
			return false;
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (Known(h))
				return wayland.TrySetZOrder(h, z);

			// Foreign-toplevel exposes no stacking-order protocol.
			if (WaylandForeignToplevels.Current?.IsWindow(h) == true)
				return false;

			if (TryOwnControl(h, out _))
				return base.TrySetZOrder(h, z);
			return false;
		}

		public override bool TryHide(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop).
				return wayland.TrySetWindowState(h, FormWindowState.Minimized);

			if (WaylandForeignToplevels.Current is { } tlm && tlm.Get(h) is WaylandToplevel tl)
				return tlm.SetState(tl, FormWindowState.Minimized);

			if (TryOwnControl(h, out _))
				return base.TryHide(h);
			return false;
		}

		public override bool TryShow(nint h)
		{
			if (Known(h))   // membership guard (see TrySetAlwaysOnTop).
				return wayland.TrySetWindowState(h, FormWindowState.Normal);

			if (WaylandForeignToplevels.Current is { } tlm && tlm.Get(h) is WaylandToplevel tl)
				return tlm.SetState(tl, FormWindowState.Normal);

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

			if (WaylandForeignToplevels.Current is { } tlm && tlm.Get(h) is WaylandToplevel tl)
				return tlm.SetState(tl, state);

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

			if (Foreign(h) != null)
				return false;   // wlr-foreign-toplevel exposes no geometry; the caller raises OSError

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

			if (WaylandForeignToplevels.Current is { } tlm && tlm.Get(h) is WaylandToplevel tl)
				return tlm.Activate(tl);

			if (TryOwnControl(h, out _))
				return base.TryActivate(h);
			return false;
		}

		public override bool TrySetStyle(nint h, long style)
		{
			// Only WS_CAPTION maps to a Wayland concept (the compositor's decoration state).
			if (Known(h))
				return wayland.TrySetNoBorder(h, (style & WsCaption) != WsCaption);

			if (Foreign(h) != null)
				return false;

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

			if (Foreign(h) != null)
				return false;

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

			if (Foreign(h) != null)
				return false;

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
			if (Foreign(id) != null) return new WindowInfo(id);
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
			return WaylandForeignToplevels.Current?.Active is WaylandToplevel fa ? new WindowInfo(fa.Handle) : new WindowInfo(0);
		}

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
		{
			var list = new List<WindowInfoBase>();

			if (wayland?.TryListWindows(includeHidden, out var backendWindows) == true)
			{
				foreach (var w in backendWindows)
					list.Add(AsOwn(w));
			}

			if (WaylandForeignToplevels.Current is { } tlm)
				foreach (var tl in tlm.Enumerate())
					list.Add(new WindowInfo(tl.Handle));

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

	internal sealed class X11Window : LinuxWindow
	{
		private static XDisplay Display => XDisplay.Default;

		public override string GetTitle(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetTitle(h);
			return X11Title(h);
		}

		public override string GetClassName(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetClassName(h);
			return X11ClassName(h);
		}

		public override long GetPid(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetPid(h);
			return X11Pid(h);
		}

		public override Rectangle GetBounds(nint h)
		{
			if (TryOwnControl(h, out var control))
			{
				var bounds = base.GetBounds(h);
				return control is Form
					? X11DisplayTopology.FromToolkitBounds(ScreenRect.FromRectangle(bounds)).ToRectangle()
					: bounds;
			}
			return X11Bounds(h);
		}

		public override Rectangle GetClientBounds(nint h)
		{
			if (TryOwnControl(h, out _))
			{
				var bounds = base.GetClientBounds(h);
				// GetClientScreenRect is absolute for both forms and child controls, so every result crosses the
				// toolkit/root boundary. (GetBounds for a child is parent-local and deliberately remains a GUI unit.)
				return X11DisplayTopology.FromToolkitBounds(ScreenRect.FromRectangle(bounds)).ToRectangle();
			}
			return X11ClientBounds(h);
		}

		public override long GetStyle(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetStyle(h);
			return X11Style(h);
		}

		public override long GetExStyle(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetExStyle(h);
			return X11ExStyle(h);
		}

		public override bool GetActive(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetActive(h);
			return X11Active(h);
		}

		public override bool GetVisible(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetVisible(h);
			return X11Visible(h);
		}

		public override bool GetEnabled(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetEnabled(h);
			return X11Enabled(h);
		}

		public override bool GetHung(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetHung(h);
			return X11Hung(h);
		}

		public override bool GetExists(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetExists(h);
			return X11Exists(h);
		}

		public override FormWindowState GetWindowState(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetWindowState(h);
			return X11WindowState(h);
		}

		public override bool GetAlwaysOnTop(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetAlwaysOnTop(h);
			return X11AlwaysOnTop(h);
		}

		public override object GetTransparency(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetTransparency(h);
			return X11Transparency(h);
		}

		public override object GetTransparentColor(nint h)
		{
			if (TryOwnControl(h, out _)) return base.GetTransparentColor(h);
			return X11TransparentColor(h);
		}

		public override POINT ClientToScreen(nint h)
		{
			if (TryOwnControl(h, out _)) return base.ClientToScreen(h);
			return X11ClientToScreen(h);
		}

		public override bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
			if (base.TryGetText(h, detectHidden, out text))
				return true;

			text = X11Text(h, detectHidden);
			return true;
		}

		public override void ChildFindPoint(nint h, PointAndHwnd pah)
		{
			if (TryOwnControl(h, out _))
			{
				base.ChildFindPoint(h, pah);
				return;
			}

			X11ChildFindPoint(h, pah);
		}

		public override bool TryClientToScreen(nint h, ref Point pt)
		{
			if (base.TryClientToScreen(h, ref pt))
				return true;

			var origin = X11ClientToScreen(h);
			pt = new Point(pt.X + origin.X, pt.Y + origin.Y);
			return true;
		}

		public override bool TryGetParent(nint h, out nint parent)
		{
			if (base.TryGetParent(h, out parent))
				return true;

			parent = X11ParentHandle(h);
			return parent != 0;
		}

		public override bool TryGetTopLevel(nint h, out nint top)
		{
			if (base.TryGetTopLevel(h, out top))
				return true;

			top = X11NonChildParentHandle(h);
			return top != 0;
		}

		public override bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
			if (base.TryEnumerateChildren(h, out children))
				return true;

			children = X11ChildWindows(h).ToList();
			return true;
		}

		public override nint GetForegroundHandle()
			=> new nint(Display.XGetInputFocusHandle());

		public override bool IsWindow(nint h)
		{
			if (base.IsWindow(h))
				return true;

			if (h == 0)
				return false;

			var attr = new XWindowAttributes();

			lock (X11Server.xLibLock)
			{
				// Shared, permanently-rooted error handler (see X11Server.BeginErrorTrap): a per-call
				// delegate here races with GTK's Xlib usage on another thread and can SIGSEGV.
				var oldHandler = X11Server.BeginErrorTrap();

				try
				{
					var result = Xlib.XGetWindowAttributes(Display.Handle, h.ToInt64(), ref attr) != 0;
					_ = Xlib.XSync(Display.Handle, false);
					return !X11Server.ErrorTrapped && result;
				}
				finally
				{
					X11Server.EndErrorTrap(oldHandler);
				}
			}
		}

		public override bool TrySetAlwaysOnTop(nint h, bool onTop)
		{
			if (base.TrySetAlwaysOnTop(h, onTop))
				return true;

			if (h == 0)
				return false;

			var disp = Display;

			if (Control.FromHandle(h) is Form form)
				form.TopMost = onTop;
			else
				X11Server.SendNetWMMessage(h, disp._NET_WM_STATE, onTop ? 1 : 0, disp._NET_WM_STATE_ABOVE, 0, 0);

			_ = Xlib.XFlush(disp.Handle);
			return true;
		}

		public override bool TryClose(nint h)
		{
			if (base.TryClose(h))
				return true;

			if (h == 0)
				return false;

			var disp = Display;
			var ev = new XEvent();
			ev.ClientMessageEvent.type = XEventName.ClientMessage;
			ev.ClientMessageEvent.window = h;
			ev.ClientMessageEvent.message_type = disp.WM_PROTOCOLS;
			ev.ClientMessageEvent.format = 32;
			ev.ClientMessageEvent.ptr1 = disp.WM_DELETE_WINDOW;
			return Xlib.XSendEvent(disp.Handle, h, false, EventMasks.NoEvent, ref ev) != 0;
		}

		public override bool TryKill(nint h)
		{
			if (base.TryKill(h))
				return true;

			return X11Kill(h);
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (base.TrySetZOrder(h, z))
				return true;

			if (h == 0)
				return false;

			var disp = Display;

			if (z == ZOrder.Bottom)
				_ = Xlib.XLowerWindow(disp.Handle, h);
			else
				_ = Xlib.XRaiseWindow(disp.Handle, h);

			_ = Xlib.XFlush(disp.Handle);
			return true;
		}

		public override bool TryHide(nint h)
		{
			if (base.TryHide(h))
				return true;

			return h != 0 && Xlib.XUnmapWindow(Display.Handle, h) != 0;
		}

		public override bool TryShow(nint h)
		{
			if (base.TryShow(h))
				return true;

			return h != 0 && Xlib.XMapWindow(Display.Handle, h) != 0;
		}

		public override bool TryRedraw(nint h)
		{
			if (base.TryRedraw(h))
				return true;

			return h != 0 && Xlib.XClearWindow(Display.Handle, h) != 0;
		}

		public override bool TrySetState(nint h, FormWindowState state)
		{
			if (base.TrySetState(h, state))
				return true;

			if (h == 0)
				return false;

			var disp = Display;
			var current = GetWindowState(h);

			if (current == state)
				return true;

			switch (state)
			{
				case FormWindowState.Normal:
					lock (X11Server.xLibLock)
					{
						if (current == FormWindowState.Minimized)
						{
							_ = Xlib.XMapWindow(disp.Handle, h);
							X11Server.SendNetWMMessage(h, disp._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
						}
						else if (current == FormWindowState.Maximized)
						{
							X11Server.SendNetWMMessage(h, disp._NET_WM_STATE, 2, disp._NET_WM_STATE_MAXIMIZED_HORZ, disp._NET_WM_STATE_MAXIMIZED_VERT, 0);
							X11Server.SendNetWMMessage(h, disp._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
						}
					}

					break;

				case FormWindowState.Minimized:
					lock (X11Server.xLibLock)
					{
						if (current == FormWindowState.Maximized)
							X11Server.SendNetWMMessage(h, disp._NET_WM_STATE, 2, disp._NET_WM_STATE_MAXIMIZED_HORZ, disp._NET_WM_STATE_MAXIMIZED_VERT, 0);

						_ = Xlib.XIconifyWindow(disp.Handle, h.ToInt64(), disp.ScreenNumber);
					}

					break;

				case FormWindowState.Maximized:
					lock (X11Server.xLibLock)
					{
						if (current == FormWindowState.Minimized)
							_ = Xlib.XMapWindow(disp.Handle, h);

						X11Server.SendNetWMMessage(h, disp._NET_WM_STATE, 1, disp._NET_WM_STATE_MAXIMIZED_HORZ, disp._NET_WM_STATE_MAXIMIZED_VERT, 0);
					}

					X11Server.SendNetWMMessage(h, disp._NET_ACTIVE_WINDOW, (nint)1, 0, 0, 0);
					break;
			}

			_ = Xlib.XFlush(disp.Handle);
			return true;
		}

		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			if (TryOwnControl(h, out var control))
			{
				if (control is not Form)
					return base.TryMoveResize(h, bounds, setPos, setSize);

				var current = GetBounds(h);
				var requested = new ScreenRect(
					setPos && bounds.X != WindowInfoBase.Unchanged ? bounds.X : current.X,
					setPos && bounds.Y != WindowInfoBase.Unchanged ? bounds.Y : current.Y,
					setSize && bounds.Width != WindowInfoBase.Unchanged ? bounds.Width : current.Width,
					setSize && bounds.Height != WindowInfoBase.Unchanged ? bounds.Height : current.Height);
				var toolkit = X11DisplayTopology.ToToolkitBounds(requested).ToRectangle();
				return base.TryMoveResize(h, toolkit, setPos, setSize);
			}

			// Prefer a compositor-side move: it runs inside the WM and can place a window off screen, which the
			// WM refuses for an external XMoveWindow ConfigureRequest (it clamps such requests fully on screen).
			// Fall back to raw Xlib when no compositor answers (unknown DE, extension not loaded, window not found).
			if (TryCompositorMoveResize(h, bounds))
				return true;

			X11SetBounds(h, bounds);
			return true;
		}

		// Route an X11 move through the running compositor (Cinnamon/GNOME extension or KWin script), keyed by the
		// window's X11 id. Unlike an external XMoveWindow, the in-compositor move is not clamped on screen (Mutter
		// applies its looser user-action constraint; KWin sets frameGeometry directly), so this is what lets a
		// managed foreign window be positioned partially/fully off screen. Position axes use the int.MinValue
		// "unchanged" sentinel and size axes <= 0, matching the compositor move handlers. Returns false when no
		// compositor path is available so the caller falls back to Xlib.
		private static bool TryCompositorMoveResize(nint h, Rectangle bounds)
		{
			var xid = (ulong)(h.ToInt64() & 0xFFFF_FFFFL);

			if (xid == 0)
				return false;

			if (Platform.Desktop.IsCinnamon)
				return CinnamonShellBridge.SendMoveResizeByXid(xid, bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (Platform.Desktop.IsGnome)
				return GnomeShellBridge.SendMoveResizeByXid(xid, bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (Platform.Desktop.IsKde)
				return KWinDBusBridge.SendMoveResizeByXid(xid, bounds.X, bounds.Y, bounds.Width, bounds.Height);

			return false;
		}

		public override bool TryActivate(nint h)
		{
			if (base.TryActivate(h))
				return true;

			X11Activate(h);
			return true;
		}

		public override bool TrySetStyle(nint h, long style)
		{
			if (base.TrySetStyle(h, style))
				return true;

			X11SetStyle(h, style);
			return false;
		}

		public override bool TrySetExStyle(nint h, long exStyle)
		{
			if (base.TrySetExStyle(h, exStyle))
				return true;

			X11SetExStyle(h, exStyle);
			return false;
		}

		public override bool TrySetTransparency(nint h, object alpha)
		{
			if (base.TrySetTransparency(h, alpha))
				return true;

			X11SetTransparency(h, alpha);
			return true;
		}

		public override bool TryClick(nint h, Point at, uint button, int count)
		{
			if (base.TryClick(h, at, button, count))
				return true;

			X11Click(h, at);
			return true;
		}

		public override bool TrySetTitle(nint h, string title)
		{
			if (base.TrySetTitle(h, title))
				return true;

			X11SetTitle(h, title);
			return true;
		}

		public override bool TrySetVisible(nint h, bool visible)
		{
			if (base.TrySetVisible(h, visible))
				return true;

			X11SetVisible(h, visible);
			return true;
		}

		public override bool TrySetEnabled(nint h, bool enabled)
		{
			if (base.TrySetEnabled(h, enabled))
				return true;

			X11SetEnabled(h, enabled);
			return false;
		}

		public override bool TrySetTransparentColor(nint h, object color)
		{
			if (base.TrySetTransparentColor(h, color))
				return true;

			X11SetTransparentColor(h, color);
			return false;
		}

		private const int PositionTolerance = 4;

		private static XWindow X11(nint h) => new (Display, h.ToInt64());
		private static bool X11Specified(nint h) => h != 0;

		private static bool X11Active(nint h)
			=> X11Specified(h) && WindowQuery.ActiveWindow is WindowInfoBase item && item.Handle.ToInt64() == h.ToInt64();

		private static bool X11AlwaysOnTop(nint h)
		{
			if (!X11Specified(h))
				return false;

			if (Control.FromHandle(h) is Form form)
				return form.TopMost;

			var display = Display;
			var onTop = false;
			_ = X11ReadProps(h, display._NET_WM_STATE, (nint)XAtom.XA_ATOM, atom =>
			{
				if (atom == display._NET_WM_STATE_ABOVE)
				{
					onTop = true;
					return false;
				}

				return true;
			});
			return onTop;
		}

		private static IEnumerable<nint> X11ChildWindows(nint h)
		{
			if (!X11Specified(h))
				return [];

			var xwindow = X11(h);
			var attr = new XWindowAttributes();
			var filter = (long id) => Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) != 0;
			return xwindow.XDisplay.XQueryTreeRecursive(xwindow, filter).Select(w => new nint(w.ID)).ToList();
		}

		private static string X11ClassName(nint h)
		{
			if (!X11Specified(h))
				return DefaultErrorString;

			var xwindow = X11(h);

			static string PickClassName(string resClass, string resName)
			{
				if (!string.IsNullOrEmpty(resClass))
					return resClass;

				if (!string.IsNullOrEmpty(resName))
					return resName;

				return null;
			}

			bool TryGetWmClass(long windowId, out string resClass, out string resName)
			{
				resClass = null;
				resName = null;

				var wmClassAtom = Xlib.XInternAtom(xwindow.XDisplay.Handle, "WM_CLASS", true);
				if (wmClassAtom == 0 || windowId == 0)
					return false;

				nint prop = 0;
				var result = X11Server.TryGetWindowProperty(xwindow.XDisplay.Handle,
					windowId,
					wmClassAtom,
					0,
					new nint(256),
					false,
					(nint)XAtom.AnyPropertyType,
					out _,
					out _,
					out var nitems,
					out _,
					out prop);

				try
				{
					if (!result || prop == 0 || nitems.ToInt64() == 0)
						return false;

					var bytes = new byte[nitems.ToInt64()];
					Marshal.Copy(prop, bytes, 0, bytes.Length);
					var firstNull = System.Array.IndexOf(bytes, (byte)0);
					if (firstNull < 0)
						firstNull = bytes.Length;

					resName = firstNull > 0 ? Encoding.ASCII.GetString(bytes, 0, firstNull) : string.Empty;

					var secondStart = Math.Min(firstNull + 1, bytes.Length);
					var secondNull = System.Array.IndexOf(bytes, (byte)0, secondStart);
					if (secondNull < 0)
						secondNull = bytes.Length;

					if (secondStart < bytes.Length && secondNull > secondStart)
						resClass = Encoding.ASCII.GetString(bytes, secondStart, secondNull - secondStart);

					return true;
				}
				finally
				{
					if (prop != 0)
						_ = Xlib.XFree(prop);
				}
			}

			if (Xlib.TryGetClassHint(xwindow.XDisplay.Handle, xwindow.ID, out var resName, out var resClass))
			{
				var cn = PickClassName(resClass, resName);
				if (!string.IsNullOrEmpty(cn))
					return cn;
			}

			if (TryGetWmClass(xwindow.ID, out var wmClass, out var wmName))
			{
				var cn = PickClassName(wmClass, wmName);
				if (!string.IsNullOrEmpty(cn))
					return cn;
			}

			if (Control.FromHandle(h) is Control ctrl)
				return ctrl.GetType().Name;

			var tempParent = X11ParentHandle(h);
			var depth = 0;
			while (tempParent != 0 && depth++ < 16 && tempParent.ToInt64() != xwindow.XDisplay.Root.ID)
			{
				if (TryGetWmClass(tempParent.ToInt64(), out wmClass, out wmName))
				{
					var cn = PickClassName(wmClass, wmName);
					if (!string.IsNullOrEmpty(cn))
						return cn;
				}

				tempParent = X11ParentHandle(tempParent);
			}

			return DefaultObject;
		}

		private static Rectangle X11ClientBounds(nint h)
		{
			if (!X11Specified(h))
				return new Rectangle();

			var xwindow = X11(h);
			var attr = xwindow.Attributes;
			_ = Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID,
				0, 0, out var x, out var y, out _);
			return new Rectangle(x, y, attr.width, attr.height);
		}

		private static bool X11Enabled(nint h)
		{
			if (Control.FromHandle(h) is Control ctrl)
				return ctrl.Enabled;

			return X11Specified(h);
		}

		private static void X11SetEnabled(nint h, bool enabled)
		{
			if (Control.FromHandle(h) is Control ctrl)
				ctrl.Enabled = enabled;
		}

		private static bool X11Exists(nint h)
			=> X11Specified(h) && (Control.FromHandle(h) is Control || X11(h).TryGetAttributes(out _));

		private static long X11ExStyle(nint h) => 0L;
		private static bool X11Hung(nint h) => false;

		private static Rectangle X11Bounds(nint h)
		{
			var xwindow = X11(h);
			var attr = xwindow.Attributes;

			Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID, 0, 0, out var x, out var y, out _);
			var frame = X11FrameExtents(h);
			x -= frame.Left;
			y -= frame.Top;
			var outerW = attr.width + attr.border_width + frame.Left + frame.Width;
			var outerH = attr.height + attr.border_width + frame.Top + frame.Height;
			return new Rectangle(x, y, outerW, outerH);
		}

		private static void X11SetBounds(nint h, Rectangle value)
		{
			if (!X11Specified(h))
				return;

			var xwindow = X11(h);
			var setPos  = value.X != WindowInfoBase.Unchanged || value.Y != WindowInfoBase.Unchanged;
			var setSize = value.Width != WindowInfoBase.Unchanged || value.Height != WindowInfoBase.Unchanged;

			if (!setPos && !setSize)
				return;

			int x = value.X, y = value.Y, w = value.Width, height = value.Height;
			var needPos  = setPos && (x == WindowInfoBase.Unchanged || y == WindowInfoBase.Unchanged);
			var needSize = setSize && (w == WindowInfoBase.Unchanged || height == WindowInfoBase.Unchanged);

			if (needPos || needSize)
			{
				var cur = X11Bounds(h);
				if (x == WindowInfoBase.Unchanged) x = cur.X;
				if (y == WindowInfoBase.Unchanged) y = cur.Y;
				if (w == WindowInfoBase.Unchanged) w = cur.Width;
				if (height == WindowInfoBase.Unchanged) height = cur.Height;
			}

			// Both the public X11 contract and Xlib use root-window pixels. Resolve a complete rectangle only so a
			// size-only change preserves the current native position and a position-only change preserves its size.
			var current = X11Bounds(h);
			var native = new ScreenRect(
				x == WindowInfoBase.Unchanged ? current.X : x,
				y == WindowInfoBase.Unchanged ? current.Y : y,
				w == WindowInfoBase.Unchanged ? current.Width : w,
				height == WindowInfoBase.Unchanged ? current.Height : height);
			var sx = native.X;
			var sy = native.Y;
			var sw = native.Width;
			var sh = native.Height;

			var frame = X11FrameExtents(h);
			int clientW = sw - frame.Left - frame.Width;
			int clientH = sh - frame.Top - frame.Height;

			if (setPos && setSize)
			{
				_ = Xlib.XMoveResizeWindow(xwindow.XDisplay.Handle, xwindow.ID, sx, sy, clientW, clientH);
				_ = Xlib.XSync(xwindow.XDisplay.Handle, false);
				var after = X11Bounds(h);

				if (Math.Abs(after.X - x) > PositionTolerance || Math.Abs(after.Y - y) > PositionTolerance)
				{
					_ = Xlib.XResizeWindow(xwindow.XDisplay.Handle, xwindow.ID, clientW, clientH);
					_ = Xlib.XMoveWindow(xwindow.XDisplay.Handle, xwindow.ID, sx, sy);
					_ = Xlib.XFlush(xwindow.XDisplay.Handle);
				}
			}
			else if (setPos)
			{
				_ = Xlib.XMoveWindow(xwindow.XDisplay.Handle, xwindow.ID, sx, sy);
				_ = Xlib.XFlush(xwindow.XDisplay.Handle);
			}
			else
			{
				_ = Xlib.XResizeWindow(xwindow.XDisplay.Handle, xwindow.ID, clientW, clientH);
				_ = Xlib.XFlush(xwindow.XDisplay.Handle);
			}
		}

		private static nint X11NonChildParentHandle(nint h)
		{
			if (!X11Specified(h))
				return 0;

			var display = Display;
			var parent = h;
			nint wmStateCandidate = 0;
			var tempParent = h;
			var wmStateAtom = Xlib.XInternAtom(display.Handle, "WM_STATE", true);

			bool HasWmState(nint handle)
			{
				if (wmStateAtom == 0 || handle == 0)
					return false;

				nint prop = 0;
				var result = X11Server.TryGetWindowProperty(display.Handle,
					handle.ToInt64(),
					wmStateAtom,
					0,
					new nint(2),
					false,
					(nint)XAtom.AnyPropertyType,
					out _,
					out _,
					out var nitems,
					out _,
					out prop);

				if (prop != 0)
					_ = Xlib.XFree(prop);

				return result && nitems.ToInt64() > 0;
			}

			if (HasWmState(tempParent))
				wmStateCandidate = tempParent;

			tempParent = X11ParentHandle(h);
			while (tempParent != 0 && tempParent.ToInt64() != display.Root.ID)
			{
				parent = tempParent;

				if (wmStateCandidate == 0 && HasWmState(tempParent))
					wmStateCandidate = tempParent;

				tempParent = X11ParentHandle(parent);
			}

			if (wmStateCandidate != 0)
				return wmStateCandidate;

			// Nothing in the ancestor chain carries WM_STATE: the point landed on a reparenting WM's frame (a
			// server-side titlebar/border), whose managed client is a DESCENDANT, not an ancestor. Search the
			// frame's subtree for the WM_STATE window so a titlebar grab resolves to the movable client rather than
			// the frame itself (which the WM refuses to move). The client is normally a direct child, so the depth
			// bound stays small; this only runs when the up-walk already failed, i.e. not for ordinary client hits.
			nint FindWmStateDescendant(nint win, int depth)
			{
				if (depth < 0 || win == 0)
					return 0;

				nint childrenPtr = 0;

				try
				{
					if (Xlib.XQueryTree(display.Handle, win, out _, out _, out childrenPtr, out var nChildren) == 0 || childrenPtr == 0)
						return 0;

					for (var i = 0; i < nChildren; i++)
					{
						var c = Marshal.ReadIntPtr(childrenPtr, i * IntPtr.Size);

						if (c != 0 && HasWmState(c))
							return c;
					}

					for (var i = 0; i < nChildren; i++)
					{
						var c = Marshal.ReadIntPtr(childrenPtr, i * IntPtr.Size);

						if (c == 0)
							continue;

						var found = FindWmStateDescendant(c, depth - 1);

						if (found != 0)
							return found;
					}
				}
				finally
				{
					if (childrenPtr != 0)
						_ = Xlib.XFree(childrenPtr);
				}

				return 0;
			}

			var client = FindWmStateDescendant(parent, 3);
			return client != 0 ? client : parent;
		}

		private static nint X11ParentHandle(nint h)
		{
			if (!X11Specified(h) || Display.Handle == 0)
				return 0;

			var xwindow = X11(h);
			var parentReturn = 0L;
			var childrenReturn = nint.Zero;

			try
			{
				_ = Xlib.XQueryTree(xwindow.XDisplay.Handle, xwindow.ID, out _, out parentReturn, out childrenReturn, out _);
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"XQueryTree() failed: {ex.Message}");
			}
			finally
			{
				if (childrenReturn != 0)
					_ = Xlib.XFree(childrenReturn);
			}

			return new nint(parentReturn);
		}

		private static long X11Pid(nint h)
		{
			var pid = 0L;

			if (X11Specified(h))
				_ = X11ReadProps(h, Display._NET_WM_PID, (nint)XAtom.AnyPropertyType, atom => { pid = atom; return false; });

			return pid;
		}

		private static long X11Style(nint h)
		{
			if (!X11Specified(h))
				return 0L;

			if (Control.FromHandle(h) is Eto.Forms.Control control)
				return EtoWindowStyles.For(control);

			// A foreign X11 window: EWMH exposes no style word, and _MOTIF_WM_HINTS is both optional and
			// only about decorations, so there is nothing honest to synthesize here.
			Diagnostics.Debug.WriteLine($"Window with handle {h} is not owned by this process, so no window style is available. Returning 0.");
			return 0;
		}

		private static void X11SetStyle(nint h, long style)
			=> Diagnostics.Debug.WriteLine($"Styles cannot be set on linux.");

		private static void X11SetExStyle(nint h, long exStyle)
			=> Diagnostics.Debug.WriteLine($"ExStyles cannot be set on linux.");

		private static List<string> X11Text(nint h, bool detectHiddenText)
		{
			if (!X11Specified(h))
				return [];

			var xwindow = X11(h);
			var prop = new XTextProperty();
			var attr = new XWindowAttributes();
			var filter = (long id) =>
			{
				if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) != 0)
					if (detectHiddenText || attr.map_state == MapState.IsViewable)
						return true;

				return false;
			};
			return xwindow.XDisplay.XQueryTreeRecursive(xwindow, filter).Select(x =>
			{
				if (Xlib.XGetTextProperty(xwindow.XDisplay.Handle, x.ID, ref prop, XAtom.XA_WM_NAME) != 0)
				{
					var text = prop.GetText();
					prop.Free();
					return text;
				}

				return DefaultObject;
			}).ToList();
		}

		private static string X11Title(nint h)
		{
			if (!X11Specified(h))
				return DefaultObject;

			if (Control.FromHandle(h) is Control ctrl)
				return ctrl.Text;

			var xwindow = X11(h);

			try
			{
				var wmName = Xlib.GetWMName(xwindow.XDisplay.Handle, xwindow.ID);
				if (!string.IsNullOrEmpty(wmName))
					return wmName;

				var prop = new XTextProperty();
				if (Xlib.XGetTextProperty(xwindow.XDisplay.Handle, xwindow.ID, ref prop, (XAtom)xwindow.XDisplay._NET_WM_NAME) != 0)
				{
					if (prop.value != 0 && prop.format == 8 && prop.nitems > 0)
					{
						var text = prop.encoding == xwindow.XDisplay.UTF8_STRING
							? Marshal.PtrToStringUTF8(prop.value)
							: Marshal.PtrToStringAuto(prop.value);
						prop.Free();
						return text;
					}

					prop.Free();
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Debug.WriteLine($"XGetWMName() failed: {ex.Message}");
			}

			return DefaultObject;
		}

		private static void X11SetTitle(nint h, string title)
		{
			if (!X11Specified(h))
				return;

			if (Control.FromHandle(h) is Control ctrl)
			{
				ctrl.Text = title;
			}
			else
			{
				var xwindow = X11(h);
				var prop = new XTextProperty();

				try
				{
					_ = prop.SetText(title);
					Xlib.XSetTextProperty(xwindow.XDisplay.Handle, xwindow.ID, ref prop, XAtom.XA_WM_NAME);
				}
				catch (Exception ex)
				{
					Diagnostics.Debug.WriteLine($"XSetTextProperty() failed: {ex.Message}");
				}
				finally
				{
					prop.Free();
				}
			}

			_ = Xlib.XFlush(Display.Handle);
		}

		private static object X11Transparency(nint h)
		{
			if (!X11Specified(h))
				return -1L; // No transparency; matches Windows (WinGetTransparent -> "").

			long opacity = -1;
			_ = X11ReadProps(h, Display._NET_WM_WINDOW_OPACITY, (nint)XAtom.XA_CARDINAL, atom => { opacity = atom & 0xFFFFFFFFL; return false; });

			if (opacity < 0)
				return -1L; // _NET_WM_WINDOW_OPACITY unset => fully opaque / no transparency level.

			// _NET_WM_WINDOW_OPACITY is a 32-bit CARDINAL (0 = fully transparent, 0xFFFFFFFF = fully opaque).
			// Scale it back to AutoHotkey's 0..255 alpha (0xFFFFFFFF -> 255).
			return opacity * 255 / 0xFFFFFFFFL;
		}

		private static void X11SetTransparency(nint h, object value)
		{
			if (!X11Specified(h))
				return;

			var display = Display;

			if (value is string s)
			{
				if (s.ToLower() == "off")
					_ = Xlib.XDeleteProperty(display.Handle, h.ToInt64(), display._NET_WM_WINDOW_OPACITY);
			}
			else
			{
				var a255 = Math.Clamp((int)value.Al(), 0, 255);
				// _NET_WM_WINDOW_OPACITY is a 32-bit CARDINAL (0 = fully transparent, 0xFFFFFFFF = fully opaque),
				// not a 0..255 byte. Scale AutoHotkey's 0..255 alpha up to that range (255 -> 0xFFFFFFFF exactly,
				// since 0xFFFFFFFF / 255 = 16843009). Without this every value below 255 collapses to ~0% opaque.
				var opacity = (nint)(uint)((long)a255 * 16843009L);
				_ = Xlib.XChangeProperty(display.Handle, h.ToInt64(), display._NET_WM_WINDOW_OPACITY, (nint)XAtom.XA_CARDINAL, 32, PropertyMode.Replace, ref opacity, 1);
			}

			_ = Xlib.XFlush(display.Handle);
		}

		private static object X11TransparentColor(nint h)
		{
			Diagnostics.Debug.WriteLine($"Transparency key/color not supported on linux, returning 0.");
			return 0L;
		}

		private static void X11SetTransparentColor(nint h, object color)
			=> Diagnostics.Debug.WriteLine($"Transparency key/color not supported on linux.");

		private static bool X11Visible(nint h)
		{
			if (!X11Specified(h))
				return false;

			if (Control.FromHandle(h) is Control ctrl)
				return ctrl.Visible;

			return X11(h).Attributes.map_state == MapState.IsViewable;
		}

		private static void X11SetVisible(nint h, bool visible)
		{
			if (!X11Specified(h))
				return;

			if (Control.FromHandle(h) is Control ctrl)
			{
				ctrl.Visible = visible;
				return;
			}

			_ = visible ? Xlib.XMapWindow(Display.Handle, h) : Xlib.XUnmapWindow(Display.Handle, h);
			_ = Xlib.XFlush(Display.Handle);
		}

		private static FormWindowState X11WindowState(nint h)
		{
			if (!X11Specified(h))
				return FormWindowState.Normal;

			if (Control.FromHandle(h) is Form form)
				return form.WindowState;

			var display = Display;
			var maximized = 0;
			var minimized = false;
			_ = X11ReadProps(h, display._NET_WM_STATE, (nint)XAtom.XA_ATOM, atom =>
			{
				if ((atom == display._NET_WM_STATE_MAXIMIZED_HORZ) || (atom == display._NET_WM_STATE_MAXIMIZED_VERT))
					maximized++;
				else if (atom == display._NET_WM_STATE_HIDDEN)
					minimized = true;

				return true;
			});

			return minimized ? FormWindowState.Minimized : maximized == 2 ? FormWindowState.Maximized : FormWindowState.Normal;
		}

		private static void X11Activate(nint h)
		{
			if (!X11Specified(h))
				return;

			if (WindowQuery.ActiveWindow.Handle.ToInt64() == h.ToInt64())
				return;

			if (X11WindowState(h) == FormWindowState.Minimized)
				_ = Platform.Window.TrySetState(h, FormWindowState.Normal);
			else
			{
				lock (X11Server.xLibLock)
				{
					X11Server.SendNetWMMessage(h, Display._NET_ACTIVE_WINDOW, 1, 0, 0, 0);
					_ = Xlib.XFlush(Display.Handle);
				}
			}
		}

		private static void X11ChildFindPoint(nint h, PointAndHwnd pah)
		{
			if (!X11Specified(h))
				return;

			var xwindow = X11(h);
			var root = xwindow.XDisplay.Root.ID;

			foreach (var child in xwindow.XDisplay.XQueryTreeRecursive(xwindow, id =>
			{
				var attr = new XWindowAttributes();
				if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, id, ref attr) == 0)
					return false;

				if (attr.map_state != MapState.IsViewable)
					return false;

				if (pah.ignoreDisabled && Control.FromHandle(new nint(id)) is Control ctrl && !ctrl.Enabled)
					return false;

				return true;
			}))
			{
				var attr = new XWindowAttributes();
				if (Xlib.XGetWindowAttributes(xwindow.XDisplay.Handle, child.ID, ref attr) == 0)
					continue;

				if (!Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, child.ID, root, 0, 0, out var absX, out var absY, out _))
					continue;

				var rect = new Rectangle(absX, absY, attr.width, attr.height);
				if (pah.pt.X < rect.Left || pah.pt.X >= rect.Right || pah.pt.Y < rect.Top || pah.pt.Y >= rect.Bottom)
					continue;

				var centerx = rect.Left + ((double)rect.Width / 2);
				var centery = rect.Top + ((double)rect.Height / 2);
				var distance = Math.Sqrt(Math.Pow(pah.pt.X - centerx, 2.0) + Math.Pow(pah.pt.Y - centery, 2.0));
				var updateIt = pah.hwndFound == 0;

				if (!updateIt)
				{
					if (rect.Left >= pah.rectFound.Left && rect.Right <= pah.rectFound.Right
						&& rect.Top >= pah.rectFound.Top && rect.Bottom <= pah.rectFound.Bottom)
						updateIt = true;
					else if (distance < pah.distanceFound &&
							 (pah.rectFound.Left < rect.Left || pah.rectFound.Right > rect.Right
							  || pah.rectFound.Top < rect.Top || pah.rectFound.Bottom > rect.Bottom))
						updateIt = true;
				}

				if (updateIt)
				{
					pah.hwndFound = new nint(child.ID);
					pah.rectFound = rect;
					pah.distanceFound = distance;
				}
			}
		}

		private static void X11Click(nint h, Point? location = null)
		{
			X11SendMouseEvent(h, XEventName.ButtonPress, EventMasks.ButtonPress, Buttons.Left, location);
			_ = Xlib.XFlush(Display.Handle);
			X11SendMouseEvent(h, XEventName.ButtonRelease, EventMasks.ButtonRelease, Buttons.Left, location);
		}

		private static POINT X11ClientToScreen(nint h)
		{
			if (!X11Specified(h))
				return new POINT(0, 0);

			var xwindow = X11(h);
			_ = Xlib.XTranslateCoordinates(xwindow.XDisplay.Handle, xwindow.ID, xwindow.XDisplay.Root.ID, 0, 0, out var x, out var y, out _);
			return new POINT(x, y);
		}

		private static bool X11Kill(nint h)
		{
			if (!X11Specified(h))
				return false;

			_ = Platform.Window.TryClose(h);
			var i = 0;

			while (X11Exists(h) && i++ < 5)
			{
				if ((i & 1) == 1)
					Thread.Sleep(0);
				else
					Thread.Sleep(10);
			}

			if (!X11Exists(h))
				return true;

			_ = Xlib.XKillClient(Display.Handle, h.ToInt64());
			return !X11Exists(h);
		}

		private static bool X11ReadProps(nint h, nint state, nint type, Func<long, bool> func)
		{
			nint prop = 0;

			if (X11Server.TryGetWindowProperty(Display.Handle,
					h.ToInt64(),
					state,
					0,
					new nint(256),
					false,
					type,
					out _,
					out _,
					out var nitems,
					out _,
					out prop))
			{
				if (nitems.ToInt64() > 0 && prop != 0)
				{
					for (int i = 0; i < nitems; i++)
					{
						var atom = (nint)Marshal.ReadInt64(prop, i * 8);

						if (!func(atom))
							break;
					}
				}

				// XGetWindowProperty allocates a buffer whenever it succeeds (prop != NULL) even for a present-but-empty
				// property (nitems == 0, common for _NET_WM_STATE on ordinary windows), so free unconditionally to avoid
				// leaking. Matches the sibling readers (ActiveWindow, X11FrameExtents, HasWmState).
				if (prop != 0)
					_ = Xlib.XFree(prop);

				return true;
			}

			Diagnostics.Debug.WriteLine($"ReadStateProps() XGetWindowProperty failed.");
			return false;
		}

		private static void X11SendMouseEvent(nint h, XEventName evName, EventMasks evMask, Buttons button, Point? location = null)
		{
			var attr = X11(h).Attributes;
			Point click;

			if (location is Point logicalClick)
			{
				var client = X11ClientBounds(h);
				var scaleX = client.Width > 0 ? (double)attr.width / client.Width : 1.0;
				var scaleY = client.Height > 0 ? (double)attr.height / client.Height : 1.0;
				click = new Point((int)Math.Round(logicalClick.X * scaleX), (int)Math.Round(logicalClick.Y * scaleY));
			}
			else
				click = new Point(attr.width / 2, attr.height / 2);

			var ev = new XEvent();
			ev.ButtonEvent = new XButtonEvent();
			ev.ButtonEvent.type = evName;
			ev.ButtonEvent.send_event = true;
			ev.ButtonEvent.display = Display.Handle;
			ev.ButtonEvent.window = h;
			ev.ButtonEvent.subwindow = h;
			ev.ButtonEvent.x = click.X;
			ev.ButtonEvent.y = click.Y;
			ev.ButtonEvent.root = new nint(Display.Root.ID);
			ev.ButtonEvent.same_screen = true;
			ev.ButtonEvent.button = button;
			_ = Xlib.XSendEvent(Display.Handle, h.ToInt64(), true, evMask, ref ev);
		}

		private static Rectangle X11FrameExtents(nint h)
		{
			var prop = nint.Zero;
			var rect = Rectangle.Empty;
			_ = X11Server.TryGetWindowProperty(Display.Handle, h.ToInt64(), Display._NET_FRAME_EXTENTS, 0, new nint(40), false, (nint)XAtom.XA_CARDINAL, out _, out _, out var nitems, out _, out prop);

			if (prop != 0)
			{
				try
				{
					if (nitems.ToInt32() == 4)
					{
						rect = new Rectangle(
							Marshal.ReadInt32(prop, 0),
							Marshal.ReadInt32(prop, 2 * nint.Size),
							Marshal.ReadInt32(prop, nint.Size),
							Marshal.ReadInt32(prop, 3 * nint.Size));
					}
				}
				finally
				{
					_ = Xlib.XFree(prop);
				}
			}

			return rect;
		}

		// === batched query (moved from the per-OS Linux WindowManager; the neutral WindowSearch builds the one
		// WindowInfo from these results — cached for a full snapshot, live for a handle-only one). ===

		public override WindowInfoBase CreateWindow(nint id)
			=> TryOwnControl(id, out _) ? base.CreateWindow(id) : new WindowInfo(id);

		// X11 has no portable "focused control's thread" concept; the focused-layout lookup that uses this is a
		// Windows-only refinement, so report 0 (the legacy Linux behavior).
		public override uint GetFocusedControlThread(nint window = 0)
		{
			return 0;
		}

		public override WindowInfoBase ActiveWindow()
		{
			var activeId = 0L;
			nint prop = 0;

			if (X11Server.TryGetWindowProperty(Display.Handle, Display.Root.ID, Display._NET_ACTIVE_WINDOW,
					0, new nint(1), false, (nint)XAtom.AnyPropertyType, out _, out _, out var nitems, out _, out prop))
				if (nitems.ToInt64() > 0 && prop != 0)
					activeId = Marshal.ReadInt64(prop);

			if (prop != 0)
				_ = Xlib.XFree(prop);

			if (activeId != 0) return new WindowInfo((nint)activeId);

			var focused = Display.XGetInputFocusWindow();

			if (focused.ID == 0 || focused.ID == 1) return new WindowInfo(0);

			var top = X11NonChildParentHandle((nint)focused.ID);
			return new WindowInfo(top != 0 ? top : (nint)focused.ID);
		}

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
		{
			var list = new List<WindowInfoBase>();
			var attr = new XWindowAttributes();
			var filter = (long id) =>
			{
				if (Xlib.XGetWindowAttributes(Display.Handle, id, ref attr) != 0)
					if (includeHidden || attr.map_state == MapState.IsViewable)
						return true;

				return false;
			};
			var seen = new HashSet<long>();

			foreach (var w in Display.XQueryTreeRecursive(filter))
			{
				var topHandle = X11NonChildParentHandle((nint)w.ID);

				// A viewable X window implies viewable ancestors, so the filtered walk may seed visible=true.
				if (topHandle != 0 && seen.Add(topHandle.ToInt64()))
					list.Add(includeHidden ? new WindowInfo(topHandle) : new WindowInfo(topHandle, visible: true));
			}

			list.Reverse();
			return list;
		}

		public override bool TryGetAt(int x, int y, out nint child)
		{
			var found = FindWindowAtPointRecursive((nint)Display.Root.ID, x, y);
			child = found;
			return found != 0;
		}

		private static nint FindWindowAtPointRecursive(nint window, int rootX, int rootY)
		{
			var root = (nint)Display.Root.ID;
			var attr = new XWindowAttributes();

			if (Xlib.XGetWindowAttributes(Display.Handle, window, ref attr) == 0)
				return 0;

			if (!Xlib.XTranslateCoordinates(Display.Handle, root, window, rootX, rootY, out var winX, out var winY, out _))
				return 0;

			if (winX < 0 || winY < 0 || winX >= attr.width + attr.border_width || winY >= attr.height + attr.border_width)
				return 0;

			// Honor click-through (X SHAPE input region): a viewable window whose input shape excludes this
			// point would not receive a click here — the X server routes the event to the window below — so
			// skip it and its whole subtree, matching real pointer routing. This is what lets window-from-point
			// see through a Keysharp highlight/overlay (which is drawn click-through via an empty input region)
			// to the target window behind it. The root is never click-through, so skip the query for it.
			if (window != root && attr.map_state == MapState.IsViewable && !WindowInputContainsPoint(window, winX, winY))
				return 0;

			nint childrenReturn = 0;

			try
			{
				if (Xlib.XQueryTree(Display.Handle, window, out _, out _, out childrenReturn, out var nChildren) != 0 && childrenReturn != 0)
				{
					for (var i = nChildren - 1; i >= 0; i--)
					{
						var c = Marshal.ReadIntPtr(childrenReturn, i * IntPtr.Size);

						if (c == 0)
							continue;

						var found = FindWindowAtPointRecursive(c, rootX, rootY);

						if (found != 0)
							return found;
					}
				}
			}
			finally
			{
				if (childrenReturn != 0)
					_ = Xlib.XFree(childrenReturn);
			}

			return attr.map_state == MapState.IsViewable ? window : 0;
		}

		// 0 = not yet probed, 1 = SHAPE available, 2 = unavailable. Process-wide (extension presence is a
		// property of the X server, not the per-thread display); a benign race just re-probes.
		private static int shapeExtensionState;

		private static bool ShapeExtensionAvailable()
		{
			var state = shapeExtensionState;

			if (state != 0)
				return state == 1;

			bool available;

			try { available = Xlib.XShapeQueryExtension(Display.Handle, out _, out _); }
			catch (DllNotFoundException) { available = false; }        // libXext.so.6 not installed
			catch (EntryPointNotFoundException) { available = false; }

			shapeExtensionState = available ? 1 : 2;
			return available;
		}

		// Whether the window's X11 input shape contains the window-relative point. A window with no explicit
		// input shape reports its full rectangle (always contains → opaque); a click-through window (empty
		// input region) reports no rectangles → returns false so window-from-point skips it, mirroring how the
		// X server routes a real click past it. Fails OPEN (returns true) when SHAPE is unavailable or the query
		// fails, so a normal window is never wrongly skipped.
		private static bool WindowInputContainsPoint(nint window, int winX, int winY)
		{
			if (!ShapeExtensionAvailable())
				return true;

			var rects = Xlib.XShapeGetRectangles(Display.Handle, window, Xlib.ShapeInput, out var count, out _);

			if (rects == 0)
				return count > 0;   // empty region (count ≤ 0) → click-through; defensive otherwise

			try
			{
				if (count <= 0)
					return false;   // explicit empty input region → click-through

				var size = Marshal.SizeOf<XRectangle>();

				for (var i = 0; i < count; i++)
				{
					var r = Marshal.PtrToStructure<XRectangle>(rects + i * size);

					if (winX >= r.x && winX < r.x + r.width && winY >= r.y && winY < r.y + r.height)
						return true;
				}

				return false;   // point falls outside every input rectangle → click-through here
			}
			finally
			{
				_ = Xlib.XFree(rects);
			}
		}
	}
#endif
}
