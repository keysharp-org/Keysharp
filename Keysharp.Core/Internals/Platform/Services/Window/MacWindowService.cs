using Keysharp.Builtins;

namespace Keysharp.Internals
{

#if OSX
	internal sealed class MacWindow : WindowBase
	{
		private const int Unchanged = WindowInfoBase.Unchanged;
		private const long WS_CAPTION = 0x00C00000;
		private const long WS_SYSMENU = 0x00080000;
		private const long WS_THICKFRAME = 0x00040000;
		private const long WS_MINIMIZEBOX = 0x00020000;

		// A by-handle MacWindowInfo for the live read path (Get*/GetText/hit-test) — its scalar reads land on the
		// neutral subtype directly. Actions go through the Try* methods below, which fetch a fresh descriptor.
		private static MacWindowInfo Item(nint h) => new (h);

		private static bool TryItem(nint h, out MacWindowInfo item, bool includeTextMetadata = false)
		{
			if (!TryOwnControl(h, out _) && MacNativeWindows.TryGetWindowInfo(h, out var native, includeTextMetadata))
			{
				item = new MacWindowInfo(native, includeTextMetadata);
				return true;
			}

			item = null;
			return false;
		}

		// macOS off-process titles come from the kCGWindow batch (unavailable when re-queried by handle), so a
		// by-handle MacWindowInfo lazily fetches its descriptor once; enumerate seeds each from the batch.
		public override WindowInfoBase CreateWindow(nint id)
		{
			if (TryItem(id, out var item, includeTextMetadata: true))
				return item;

			return TryOwnControl(id, out _) ? base.CreateWindow(id) : new MacWindowInfo(id);
		}

		public override WindowInfoBase ActiveWindow()
		{
			var fh = GetForegroundHandle();
			return fh == 0 ? new WindowInfo(0) : CreateWindow(fh);
		}

		// --- granular live reads (rare: WindowInfo reads from its held source; these serve by-handle lookups) ---
		public override string GetTitle(nint h)
			=> TryItem(h, out var item, includeTextMetadata: true) ? item.Title : TryOwnControl(h, out _) ? base.GetTitle(h) : Item(h).Title;
		public override string GetClassName(nint h)
			=> TryItem(h, out var item, includeTextMetadata: true) ? item.ClassName : TryOwnControl(h, out _) ? base.GetClassName(h) : Item(h).ClassName;
		public override long GetPid(nint h)
			=> TryItem(h, out var item) ? item.PID : TryOwnControl(h, out _) ? base.GetPid(h) : Item(h).PID;
		public override Rectangle GetBounds(nint h)
			=> TryItem(h, out var item) ? item.Bounds : TryOwnControl(h, out _) ? base.GetBounds(h) : Item(h).Bounds;
		public override Rectangle GetClientBounds(nint h)
			=> TryItem(h, out var item) ? item.ClientBounds : TryOwnControl(h, out _) ? base.GetClientBounds(h) : Item(h).ClientBounds;
		public override long GetStyle(nint h)
			=> TryItem(h, out var item) ? item.Style : TryOwnControl(h, out _) ? base.GetStyle(h) : Item(h).Style;
		public override long GetExStyle(nint h)
			=> TryItem(h, out var item) ? item.ExStyle : TryOwnControl(h, out _) ? base.GetExStyle(h) : Item(h).ExStyle;
		public override bool GetActive(nint h)
			=> h > 0 && (ulong)h <= uint.MaxValue ? GetForegroundHandle() == h : TryOwnControl(h, out _) && base.GetActive(h);
		public override bool GetVisible(nint h)
			=> TryItem(h, out var item) ? item.Visible : TryOwnControl(h, out _) ? base.GetVisible(h) : Item(h).Visible;
		public override bool GetEnabled(nint h)
			=> TryItem(h, out var item) ? item.Enabled : TryOwnControl(h, out _) ? base.GetEnabled(h) : Item(h).Enabled;
		public override bool GetHung(nint h)
			=> TryItem(h, out var item) ? item.IsHung : TryOwnControl(h, out _) ? base.GetHung(h) : Item(h).IsHung;
		public override bool GetExists(nint h)
			=> TryItem(h, out var item) ? item.Exists : TryOwnControl(h, out _) ? base.GetExists(h) : Item(h).Exists;
		public override FormWindowState GetWindowState(nint h)
			=> TryItem(h, out var item) ? item.WindowState : TryOwnControl(h, out _) ? base.GetWindowState(h) : Item(h).WindowState;
		public override bool GetAlwaysOnTop(nint h)
			=> TryItem(h, out var item) ? item.AlwaysOnTop : TryOwnControl(h, out _) ? base.GetAlwaysOnTop(h) : Item(h).AlwaysOnTop;
		public override object GetTransparency(nint h)
			=> Item(h).Transparency;
		public override object GetTransparentColor(nint h)
			=> TryItem(h, out var item) ? item.TransparentColor : TryOwnControl(h, out _) ? base.GetTransparentColor(h) : Item(h).TransparentColor;
		public override POINT ClientToScreen(nint h)
			=> TryItem(h, out var item) ? item.ClientToScreen() : TryOwnControl(h, out _) ? base.ClientToScreen(h) : Item(h).ClientToScreen();
		public override bool IsWindow(nint h)
			=> TryItem(h, out var item) ? item.Exists : base.IsWindow(h);

		public override bool TryGetText(nint h, bool detectHidden, bool fast, out List<string> text)
		{
			if (TryItem(h, out var item, includeTextMetadata: true))
			{
				text = item.GetText(detectHidden, fast);
				return true;
			}

			return base.TryGetText(h, detectHidden, fast, out text);
		}

		public override void ChildFindPoint(nint h, PointAndHwnd pah)
		{
			if (TryItem(h, out var item))
				item.ChildFindPoint(pah);
			else
				base.ChildFindPoint(h, pah);
		}

		public override bool TryClientToScreen(nint h, ref Point pt)
		{
			if (TryItem(h, out var item))
			{
				var o = item.ClientToScreen();
				pt = new Point(pt.X + o.X, pt.Y + o.Y);
				return true;
			}

			return base.TryClientToScreen(h, ref pt);
		}

		// macOS exposes no window tree to us: every window is its own top-level with no children.
		public override bool TryGetParent(nint h, out nint parent)
		{
			if (TryOwnControl(h, out _))
				return base.TryGetParent(h, out parent);

			parent = 0;
			return false;
		}
		public override bool TryGetTopLevel(nint h, out nint top)
		{
			// No native fetch: every macOS window is its own top-level (see comment above), so
			// answering membership with a CGWindowList snapshot would be pure waste on the
			// MouseGetPos/window-from-point hot path.
			if (TryOwnControl(h, out _))
				return base.TryGetTopLevel(h, out top);

			top = h;
			return h != 0;
		}
		public override bool TryEnumerateChildren(nint h, out IReadOnlyList<nint> children)
		{
			if (TryOwnControl(h, out _))
				return base.TryEnumerateChildren(h, out children);

			children = [];
			return true;
		}

		// --- control: fetch the descriptor by handle and drive MacAccessibility/MacNativeWindows directly ---
		public override bool TrySetAlwaysOnTop(nint h, bool onTop)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetAlwaysOnTop(h, onTop);

			if (native.OwnerPid != Environment.ProcessId)
			{
				// macOS has no API to change another process's window level (raising via Accessibility would steal
				// focus). Only our own windows support AlwaysOnTop.
				Diagnostics.Debug.WriteLine("AlwaysOnTop is only supported for windows owned by this process on macOS.");
				return false;
			}

			var succeeded = MacNativeWindows.TrySetOwnWindowAlwaysOnTop(native.WindowNumber, onTop);
			if (!succeeded)
				Diagnostics.Debug.WriteLine("AlwaysOnTop failed for this macOS window.");

			return succeeded;
		}

		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryMoveResize(h, bounds, setPos, setSize);

			var rect = native.Bounds;

			if (bounds.X != Unchanged) rect.X = bounds.X;
			if (bounds.Y != Unchanged) rect.Y = bounds.Y;
			if (bounds.Width != Unchanged) rect.Width = bounds.Width;
			if (bounds.Height != Unchanged) rect.Height = bounds.Height;

			using var window = MacAccessibility.ResolveWindowElement(native, "move/resize window");
			if (!MacAccessibility.TryMoveResizeWindow(window, rect, setPos, setSize))
				_ = Errors.OSErrorOccurred("Move/resize for macOS window failed.");

			return true;
		}

		public override bool TryUnminimize(nint h)
		{
			if (TryOwnControl(h, out _))
				return base.TryUnminimize(h);

			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return false;

			using var window = MacAccessibility.ResolveWindowElement(native, "unminimize window", waitForWindow: true);
			if (!MacAccessibility.TryGetWindowState(window, out var current)
				|| current == FormWindowState.Minimized && !MacAccessibility.TrySetWindowState(window, FormWindowState.Normal))
				_ = Errors.OSErrorOccurred("Unminimizing the macOS window failed.");

			return true;
		}

		public override bool TrySetState(nint h, FormWindowState state)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetState(h, state);

			using var window = MacAccessibility.ResolveWindowElement(native, "set window state", waitForWindow: true);
			return MacAccessibility.TrySetWindowState(window, state);
		}

		public override bool TrySetStyle(nint h, long style)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetStyle(h, style);

			if (native.OwnerPid != Environment.ProcessId)
			{
				Diagnostics.Debug.WriteLine("Window styles are only supported for windows owned by this process on macOS.");
				return false;
			}

			var succeeded = MacNativeWindows.TrySetOwnWindowFrameStyle(native.WindowNumber,
					(style & WS_CAPTION) == WS_CAPTION,
					(style & WS_SYSMENU) != 0,
					(style & WS_THICKFRAME) != 0,
					(style & WS_MINIMIZEBOX) != 0);
			if (!succeeded)
				Diagnostics.Debug.WriteLine("Setting the window style failed for this macOS window.");

			return succeeded;
		}

		public override bool TrySetExStyle(nint h, long exStyle)
		{
			Diagnostics.Debug.WriteLine("ExStyles are not supported on macOS.");
			return false;
		}

		public override bool TrySetTransparency(nint h, object alpha)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetTransparency(h, alpha);

			if (native.OwnerPid != Environment.ProcessId)
			{
				Diagnostics.Debug.WriteLine("Opacity control is only supported for windows owned by this process on macOS.");
				return false;
			}

			double a;

			// Off restores the opaque window; converting it as a number would make the window invisible instead.
			if (alpha is string text && text.Equals("off", StringComparison.OrdinalIgnoreCase))
				a = 1.0;
			else
			{
				_ = alpha.TryCoerceLong(out var raw);
				a = Math.Clamp(raw, 0, 255) / 255.0;
			}

			var succeeded = MacNativeWindows.TrySetOwnWindowAlpha(native.WindowNumber, a);
			if (!succeeded)
				Diagnostics.Debug.WriteLine("Opacity control failed for this macOS window.");

			return succeeded;
		}

		public override bool TryActivate(nint h)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryActivate(h);

			using var window = MacAccessibility.ResolveWindowElement(native, "activate window", waitForWindow: true);
			return window != null ? MacAccessibility.TryActivateWindow(window)
				: MacNativeWindows.ActivateAppByPid(native.OwnerPid);
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetZOrder(h, z);

			if (z != ZOrder.Bottom)   // raise to top
			{
				using var window = MacAccessibility.ResolveWindowElement(native, "raise window");
				return MacAccessibility.TryRaiseWindow(window);
			}

			if (native.OwnerPid == Environment.ProcessId && MacNativeWindows.TrySendOwnWindowToBack(native.WindowNumber))
				return true;

			Diagnostics.Debug.WriteLine("Sending a window to the bottom of the Z order is only supported for windows owned by this process on macOS.");
			return false;
		}

		public override bool TryClose(nint h)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryClose(h);

			using var window = MacAccessibility.ResolveWindowElement(native, "close window");
			return MacAccessibility.TryCloseWindow(window);
		}

		public override bool TryKill(nint h)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out _, includeTextMetadata: false))
				return base.TryKill(h);

			_ = TryClose(h);

			// Give a responsive app time to process the close and shut down gracefully (saving prompts, etc.)
			// before escalating to a hard Process.Kill. AHK waits ~500ms; poll with a real delay so we don't
			// force-kill a healthy window that simply hasn't handled the close request yet.
			for (var waited = 0; MacNativeWindows.TryGetWindowInfo(h, out _, includeTextMetadata: false) && waited < 500; waited += 10)
				Flow.SleepWithoutInterruption(10);

			if (!MacNativeWindows.TryGetWindowInfo(h, out var native, includeTextMetadata: false))
				return true;

			try
			{
				using var proc = Process.GetProcessById(native.OwnerPid);
				proc.Kill();
			}
			catch
			{
			}

			return !MacNativeWindows.TryGetWindowInfo(h, out _, includeTextMetadata: false);
		}

		public override bool TryHide(nint h)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryHide(h);

			return native.OwnerPid == Environment.ProcessId
				? MacNativeWindows.TryHideOwnWindow(native.WindowNumber, native)
				: MacNativeWindows.HideApplication(native.OwnerPid);
		}

		public override bool TryShow(nint h)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryShow(h);

			var restored = native.OwnerPid == Environment.ProcessId
							? MacNativeWindows.TryShowOwnWindow(native.WindowNumber)
							: MacNativeWindows.UnhideApplication(native.OwnerPid);
			if (!restored && native.OwnerPid != Environment.ProcessId)
				return false;

			using var window = MacAccessibility.ResolveWindowElement(native, "show window");
			var activated = window != null ? MacAccessibility.TryActivateWindow(window)
				: MacNativeWindows.ActivateAppByPid(native.OwnerPid);
			return restored || activated;
		}

		// Public AppKit invalidation requires a window owned by this process.
		public override bool TryRedraw(nint h)
			=> base.TryRedraw(h) || (MacNativeWindows.TryGetWindowInfo(h, out var native) && native.OwnerPid == Environment.ProcessId
				&& MacNativeWindows.TryRedrawOwnWindow(native.WindowNumber));

		public override bool TryClick(nint h, Point at, uint button, int count)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryClick(h, at, button, count);

			using var window = MacAccessibility.ResolveWindowElement(native, "post mouse click");
			var clicked = MacAccessibility.TryClickWindow(window, at, button, count);
			if (!clicked)
				Diagnostics.Debug.WriteLine("Native click failed on macOS window.");

			return clicked;
		}

		public override bool TrySetTitle(nint h, string title)
		{
			if (TryOwnControl(h, out _) || !MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetTitle(h, title);

			bool ok;
			if (native.OwnerPid == Environment.ProcessId)
				ok = MacNativeWindows.TrySetOwnWindowTitle(native.WindowNumber, title);
			else
			{
				using var window = MacAccessibility.ResolveWindowElement(native, "set window title");
				ok = MacAccessibility.TrySetWindowTitle(window, title);
			}

			if (!ok)
				Diagnostics.Debug.WriteLine("Setting the window title failed on macOS.");

			return ok;
		}

		public override bool TrySetVisible(nint h, bool visible) => visible ? TryShow(h) : TryHide(h);

		public override bool TrySetEnabled(nint h, bool enabled)
		{
			if (base.TrySetEnabled(h, enabled))
				return true;

			Diagnostics.Debug.WriteLine("Enabled state is not implemented for macOS windows.");
			return false;
		}

		public override bool TrySetTransparentColor(nint h, object color)
		{
			if (TryOwnControl(h, out _))
				return base.TrySetTransparentColor(h, color);

			Diagnostics.Debug.WriteLine("Transparency key/color is not supported on macOS.");
			return false;
		}

		public override nint GetForegroundHandle()
			=> MacAccessibility.TryGetFocusedWindowHandle(out var h) && h != 0
				? h
				: 0;

		public override IEnumerable<WindowInfoBase> Enumerate(bool includeHidden)
		{
			// Snapshot() requests kCGWindowName, omitted for other processes' windows unless Screen Recording
			// access is granted; without it title-based matching silently finds nothing.
			_ = MacAccessibility.EnsureScreenCaptureAccess("enumerate window titles", prompt: true);

			var list = new List<WindowInfoBase>();
			var wins = MacNativeWindows.Snapshot();

			for (int i = 0; i < wins.Count; i++)
			{
				var info = wins[i];

				var window = TryOwnControl((nint)info.WindowNumber, out _)
					? base.CreateWindow((nint)info.WindowNumber) : new MacWindowInfo(info, includesTextMetadata: true);
				if (includeHidden || window.Visible)
					list.Add(window);
			}

			return list;
		}

		public override bool TryGetAt(int x, int y, out nint child)
		{
			if (MacNativeWindows.TryGetWindowAtPoint(new POINT(x, y), out var native))
			{
				child = (nint)native.WindowNumber;
				return true;
			}

			child = default;
			return false;
		}

		public override uint GetFocusedControlThread(nint window = 0)
		{
			var hwnd = window != 0 ? window : GetForegroundHandle();

			if (hwnd == 0)
				return 0;

			if (MacNativeWindows.TryGetWindowInfo(hwnd, out var native, includeTextMetadata: false))
				return (uint)Math.Max(0, native.OwnerPid);

			return 0;
		}
	}
#endif
}
