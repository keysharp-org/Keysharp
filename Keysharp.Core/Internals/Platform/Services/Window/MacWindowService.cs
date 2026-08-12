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
			if (MacNativeWindows.TryGetWindowInfo(h, out var native, includeTextMetadata))
			{
				item = new MacWindowInfo(native, includeTextMetadata);
				return true;
			}

			item = null;
			return false;
		}

		// The window-server descriptor for a handle, accepting either a CGWindowID or one of our own Eto
		// window handles (an NSWindow pointer). Used by the action verbs whose native implementation is
		// own-process only, so that a script-created GUI reaches it instead of falling through to the
		// toolkit no-op in WindowBase.
		private static bool TryNative(nint h, out MacNativeWindow native)
			=> MacNativeWindows.TryGetWindowInfo(h, out native)
			   || MacNativeWindows.TryGetOwnWindowInfo(h, out native);

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
			=> TryItem(h, out var item) ? item.Active : TryOwnControl(h, out _) ? base.GetActive(h) : Item(h).Active;
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
			=> TryItem(h, out var item) ? item.Transparency : TryOwnControl(h, out _) ? base.GetTransparency(h) : Item(h).Transparency;
		public override object GetTransparentColor(nint h)
			=> TryItem(h, out var item) ? item.TransparentColor : TryOwnControl(h, out _) ? base.GetTransparentColor(h) : Item(h).TransparentColor;
		public override POINT ClientToScreen(nint h)
			=> TryItem(h, out var item) ? item.ClientToScreen() : TryOwnControl(h, out _) ? base.ClientToScreen(h) : Item(h).ClientToScreen();
		public override bool IsWindow(nint h)
			=> TryItem(h, out var item) ? item.Exists : base.IsWindow(h);

		public override bool TryGetText(nint h, bool detectHidden, out List<string> text)
		{
			if (TryItem(h, out var item, includeTextMetadata: true))
			{
				text = item.GetText(new WindowSearchOptions { DetectHiddenText = detectHidden });
				return true;
			}

			return base.TryGetText(h, detectHidden, out text);
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
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetAlwaysOnTop(h, onTop);

			if (native.OwnerPid != Environment.ProcessId)
			{
				// macOS has no API to change another process's window level (raising via Accessibility would steal
				// focus). Only our own windows support AlwaysOnTop.
				Diagnostics.Debug.WriteLine("AlwaysOnTop is only supported for windows owned by this process on macOS.");
				return false;
			}

			if (!MacNativeWindows.TrySetOwnWindowAlwaysOnTop(native.WindowNumber, onTop))
				Diagnostics.Debug.WriteLine("AlwaysOnTop failed for this macOS window.");

			return true;
		}

		public override bool TryMoveResize(nint h, Rectangle bounds, bool setPos, bool setSize)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryMoveResize(h, bounds, setPos, setSize);

			var rect = native.Bounds;

			if (bounds.X != Unchanged) rect.X = bounds.X;
			if (bounds.Y != Unchanged) rect.Y = bounds.Y;
			if (bounds.Width != Unchanged) rect.Width = bounds.Width;
			if (bounds.Height != Unchanged) rect.Height = bounds.Height;

			if (!MacAccessibility.TryMoveResizeWindow(native, rect, setPos, setSize))
				_ = Errors.OSErrorOccurred("Move/resize for macOS window failed.");

			return true;
		}

		public override bool TrySetState(nint h, FormWindowState state)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetState(h, state);

			switch (state)
			{
				// macOS has no "maximized" window state; the nearest equivalent to a Windows/Linux maximize
				// is native full screen (what the green button does by default), so map WinMaximize onto it.
				case FormWindowState.Maximized:
					if (!MacAccessibility.TrySetFullScreen(native, true))
						Diagnostics.Debug.WriteLine("Full screen (maximize) for macOS window failed.");

					return true;

				// WinRestore: leave full screen first (AppKit restores the prior frame), then un-minimize + raise.
				case FormWindowState.Normal:
					_ = MacAccessibility.TrySetFullScreen(native, false);

					if (!MacAccessibility.TrySetWindowState(native, state))
						Diagnostics.Debug.WriteLine("WindowState for macOS window failed.");

					return true;

				default: // Minimized
					if (!MacAccessibility.TrySetWindowState(native, state))
						Diagnostics.Debug.WriteLine("WindowState for macOS window failed.");

					return true;
			}
		}

		public override bool TrySetStyle(nint h, long style)
		{
			if (!TryNative(h, out var native))
				return base.TrySetStyle(h, style);

			if (native.OwnerPid != Environment.ProcessId)
			{
				Diagnostics.Debug.WriteLine("Window styles are only supported for windows owned by this process on macOS.");
				return false;
			}

			if (!MacNativeWindows.TrySetOwnWindowFrameStyle(native.WindowNumber,
					(style & WS_CAPTION) == WS_CAPTION,
					(style & WS_SYSMENU) != 0,
					(style & WS_THICKFRAME) != 0,
					(style & WS_MINIMIZEBOX) != 0))
				Diagnostics.Debug.WriteLine("Setting the window style failed for this macOS window.");

			return true;
		}

		public override bool TrySetExStyle(nint h, long exStyle)
		{
			Diagnostics.Debug.WriteLine("ExStyles are not supported on macOS.");
			return false;
		}

		public override bool TrySetTransparency(nint h, object alpha)
		{
			if (!TryNative(h, out var native))
				return base.TrySetTransparency(h, alpha);

			if (native.OwnerPid != Environment.ProcessId)
			{
				Diagnostics.Debug.WriteLine("Opacity control is only supported for windows owned by this process on macOS.");
				return false;
			}

			var a = Math.Clamp(alpha.Al(), 0, 255) / 255.0;

			if (!MacNativeWindows.TrySetOwnWindowAlpha(native.WindowNumber, a))
				Diagnostics.Debug.WriteLine("Opacity control failed for this macOS window.");

			return true;
		}

		public override bool TryActivate(nint h)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryActivate(h);

			// Restore before activating — a minimized window is un-minimized even if already foreground.
			if (MacAccessibility.TryGetWindowState(native, out var st) && st == FormWindowState.Minimized)
				_ = MacAccessibility.TrySetWindowState(native, FormWindowState.Normal);

			_ = MacAccessibility.TryActivateWindow(native);
			return true;
		}

		public override bool TrySetZOrder(nint h, ZOrder z)
		{
			if (!TryNative(h, out var native))
				return base.TrySetZOrder(h, z);

			if (z != ZOrder.Bottom)   // raise to top
			{
				if (!MacAccessibility.TryRaiseWindow(native))
					Diagnostics.Debug.WriteLine("Raising macOS window to top failed.");

				return true;
			}

			if (native.OwnerPid == Environment.ProcessId && MacNativeWindows.TrySendOwnWindowToBack(native.WindowNumber))
				return true;

			Diagnostics.Debug.WriteLine("Sending a window to the bottom of the Z order is only supported for windows owned by this process on macOS.");
			return false;
		}

		public override bool TryClose(nint h)
			=> MacNativeWindows.TryGetWindowInfo(h, out var native)
				? MacAccessibility.TryCloseWindow(native)
				: base.TryClose(h);

		public override bool TryKill(nint h)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out _))
				return base.TryKill(h);

			_ = TryClose(h);

			// Give a responsive app time to process the close and shut down gracefully (saving prompts, etc.)
			// before escalating to a hard Process.Kill. AHK waits ~500ms; poll with a real delay so we don't
			// force-kill a healthy window that simply hasn't handled the close request yet.
			for (var waited = 0; MacNativeWindows.TryGetWindowInfo(h, out _) && waited < 500; waited += 10)
				Flow.SleepWithoutInterruption(10);

			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return true;

			try
			{
				using var proc = Process.GetProcessById(native.OwnerPid);
				proc.Kill();
			}
			catch
			{
			}

			return !MacNativeWindows.TryGetWindowInfo(h, out _);
		}

		public override bool TryHide(nint h)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryHide(h);

			if (native.OwnerPid == Environment.ProcessId)
				// One of our own windows: order it out of the window server so only this window disappears.
				return MacNativeWindows.TryHideOwnWindow(native.WindowNumber, native)
					   || MacAccessibility.TrySetWindowState(native, FormWindowState.Minimized);

			// Another app's window: macOS gives no way to hide just one, so hide the whole app (closest to WinHide).
			return MacNativeWindows.HideApplication(native.OwnerPid)
				   || MacAccessibility.TrySetWindowState(native, FormWindowState.Minimized);
		}

		public override bool TryShow(nint h)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryShow(h);

			var restored = native.OwnerPid == Environment.ProcessId
							? MacNativeWindows.TryShowOwnWindow(native.WindowNumber)
							: MacNativeWindows.UnhideApplication(native.OwnerPid);

			_ = MacAccessibility.TrySetWindowState(native, FormWindowState.Normal);
			var activated = MacAccessibility.TryActivateWindow(native);
			return restored || activated;
		}

		// Own controls/windows are invalidated through the toolkit (base) — try that FIRST. AppKit repaints
		// foreign windows on its own schedule and exposes no "invalidate that window" call, so for those the
		// verb only reports that the window exists rather than doing anything.
		public override bool TryRedraw(nint h)
			=> base.TryRedraw(h) || MacNativeWindows.TryGetWindowInfo(h, out _);

		public override bool TryClick(nint h, Point at, uint button, int count)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TryClick(h, at, button, count);

			for (var i = 0; i < count; i++)
				if (!MacAccessibility.TryClickWindow(native, at, rightButton: button == 2))
					Diagnostics.Debug.WriteLine("Native click failed on macOS window.");

			return true;
		}

		public override bool TrySetTitle(nint h, string title)
		{
			if (!MacNativeWindows.TryGetWindowInfo(h, out var native))
				return base.TrySetTitle(h, title);

			var ok = native.OwnerPid == Environment.ProcessId
				? MacNativeWindows.TrySetOwnWindowTitle(native.WindowNumber, title)
				: MacAccessibility.TrySetWindowTitle(native, title);

			if (!ok)
				Diagnostics.Debug.WriteLine("Setting the window title failed on macOS.");

			return true;
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

		public override IReadOnlyList<WindowInfoBase> Enumerate(bool includeHidden)
		{
			// Snapshot() requests kCGWindowName, omitted for other processes' windows unless Screen Recording
			// access is granted; without it title-based matching silently finds nothing.
			_ = MacAccessibility.EnsureScreenCaptureAccess("enumerate window titles", prompt: true);

			var list = new List<WindowInfoBase>();
			var wins = MacNativeWindows.Snapshot();

			for (int i = 0; i < wins.Count; i++)
			{
				var info = wins[i];

				// Off-screen entries some apps register for menu-bar/status items aren't real windows and can't be
				// queried individually; only exclude them when hidden windows aren't being searched.
				if (!includeHidden && !info.IsOnScreen && !MacNativeWindows.TryGetWindowInfo((nint)info.WindowNumber, out _))
					continue;

				if (includeHidden || info.Visible)
					list.Add(new MacWindowInfo(info, includesTextMetadata: true));   // seeded from the batch
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
