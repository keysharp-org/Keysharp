using Keysharp.Builtins;
#if OSX
namespace Keysharp.Internals.Window.MacOS
{
	/// <summary>
	/// The neutral, READ-ONLY window for a macOS window: a <see cref="WindowInfoBase"/> whose state is its
	/// <see cref="MacNativeWindow"/> descriptor — seeded from the kCGWindow batch on enumerate, or lazily fetched
	/// once for a by-handle lookup and held for the instance's life. Symmetric with the Wayland
	/// <c>WaylandWindowInfo</c> and the Eto <c>ControlInfo</c>: a per-backend subtype that reads scalars from its
	/// own source. The scalar getters and the few read-shaped non-scalars (text, client-origin, hit-test) are
	/// native here; the tree links come from the shared <see cref="WindowInfoBase"/> (macOS has no window tree, so
	/// <see cref="MacWindow"/> answers them empty/self — no recursion). ACTIONS are NOT here: they go by handle
	/// through <see cref="MacWindow"/> (Platform.Window.Try*), which fetches a fresh descriptor and drives
	/// MacAccessibility/MacNativeWindows directly. Windows/X11 use the lazy <see cref="WindowInfo"/>.
	/// </summary>
	internal sealed class MacWindowInfo : WindowInfoBase
	{
		// Subset of Win32 styles with a clear NSWindow style-mask equivalent (read in Style; written in MacWindow).
		private const long WS_CAPTION = 0x00C00000;
		private const long WS_SYSMENU = 0x00080000;
		private const long WS_THICKFRAME = 0x00040000;
		private const long WS_MINIMIZEBOX = 0x00020000;

		private MacNativeWindow nativeInfoCache;
		private bool hasNativeInfoCache;
		private bool nativeInfoCacheIncludesText;

		internal MacWindowInfo(nint handle) : base(handle) { }

		internal MacWindowInfo(MacNativeWindow nativeInfo, bool includesTextMetadata) : base((nint)nativeInfo.WindowNumber)
			=> CacheNativeInfo(nativeInfo, includesTextMetadata);

		private void CacheNativeInfo(MacNativeWindow info, bool includesTextMetadata)
		{
			nativeInfoCache = info;
			hasNativeInfoCache = true;
			nativeInfoCacheIncludesText = includesTextMetadata;
		}

		// Fetch-once / no-TTL: seeded items are frozen for the instance's life; a by-handle item fetches the
		// descriptor once on first read. A read-only snapshot, so nothing ever invalidates the cache.
		private bool TryGetNativeInfo(out MacNativeWindow info, bool includeTextMetadata = false)
		{
			if (hasNativeInfoCache && (!includeTextMetadata || nativeInfoCacheIncludesText))
			{
				info = nativeInfoCache;
				return true;
			}

			if (MacNativeWindows.TryGetWindowInfo(Handle, out var latest, includeTextMetadata))
			{
				CacheNativeInfo(latest, includeTextMetadata);
				info = latest;
				return true;
			}

			info = default;
			return false;
		}

		internal override bool Active => MacAccessibility.TryGetFocusedWindowHandle(out var focused) && focused == Handle;

		internal override bool AlwaysOnTop
		{
			get
			{
				if (!TryGetNativeInfo(out var native) || native.OwnerPid != Environment.ProcessId)
					return false;

				return MacNativeWindows.TryGetOwnWindowAlwaysOnTop(native.WindowNumber, out var onTop) && onTop;
			}
		}

		internal override string ClassName
			=> className ??= TryGetNativeInfo(out var native, includeTextMetadata: true)
				? native.OwnerName.IsNullOrEmpty() ? "NSWindow" : native.OwnerName
				: DefaultErrorString;

		internal override Rectangle ClientBounds => Bounds;

		internal override bool Enabled => Exists;

		internal override bool Exists => TryGetNativeInfo(out _);

		internal override long ExStyle => 0;

		internal override bool IsHung => false;

		internal override Rectangle Bounds => TryGetNativeInfo(out var native) ? native.Bounds : Rectangle.Empty;

		internal override long PID => TryGetNativeInfo(out var native) ? native.OwnerPid : 0;

		internal override string Path
		{
			get
			{
				if (!processPath.IsNullOrEmpty())
					return processPath;

				var pid = PID;

				if (pid <= 0)
					return DefaultErrorString;

				// Cheap AppKit lookup that also resolves hardened/system apps where the neutral
				// Process.MainModule path throws access-denied.
				var app = MonoMac.AppKit.NSRunningApplication.GetRunningApplication((int)pid);
				var url = app?.ExecutableUrl;

				if (url?.Path is string path && !path.IsNullOrEmpty())
					return processPath = path;

				return base.Path;
			}
		}

		internal override string ProcessName
		{
			get
			{
				if (!processName.IsNullOrEmpty())
					return processName;

				var path = Path;

				if (!path.IsNullOrEmpty() && path != DefaultErrorString)
					return processName = System.IO.Path.GetFileName(path);

				return base.ProcessName;
			}
		}

		internal override long Style
		{
			get
			{
				// Frame furniture is only readable for our own windows — AX exposes no equivalent for another
				// application's window, and there is nothing honest to synthesize for one.
				if (!TryGetNativeInfo(out var native)
						|| native.OwnerPid != Environment.ProcessId
						|| !MacNativeWindows.TryGetOwnWindowFrameStyle(native.WindowNumber, out var titled, out var closable, out var resizable, out var miniaturizable))
					return 0;

				var style = titled ? WS_CAPTION : EtoWindowStyles.WS_POPUP;

				if (closable) style |= WS_SYSMENU;
				if (resizable) style |= WS_THICKFRAME;
				if (miniaturizable) style |= WS_MINIMIZEBOX;

				// Match the shared Eto projection (EtoWindowStyles.ForWindow) so a window reports the same
				// state bits whether it was reached by handle or seeded from an enumeration batch.
				if (Visible) style |= EtoWindowStyles.WS_VISIBLE;

				style |= WindowState switch
				{
					FormWindowState.Minimized => EtoWindowStyles.WS_MINIMIZE,
					FormWindowState.Maximized => EtoWindowStyles.WS_MAXIMIZE,
					_ => 0L
				};

				return style;
			}
		}

		internal override string Title
		{
			get
			{
				if (title != null)
					return title;

				if (!TryGetNativeInfo(out var native, includeTextMetadata: true))
					return title = string.Empty;

				return title = native.Title.IsNullOrEmpty() ? string.Empty : native.Title;
			}
		}

		internal override object Transparency
		{
			get
			{
				if (!TryGetNativeInfo(out var native))
					return -1L;

				// A fully-opaque window (kCGWindowAlpha 1.0 -> 255) reports the "no transparency set" sentinel -1L
				// (WinGetTransparent -> ""), matching Windows/X11 (AHK returns "" when the window has no transparency
				// level); only a translucent 0-254 alpha is a number. macOS exposes no "was it explicitly set" flag,
				// so an alpha of exactly 1.0 is indistinguishable from never-set and treated as no transparency.
				var alpha = Math.Clamp(Convert.ToInt32(native.Alpha * 255.0), 0, 255);
				return alpha < 255 ? (long)alpha : -1L;
			}
		}

		internal override object TransparentColor
		{
			get
			{
				Diagnostics.Debug.WriteLine("Transparency key/color is not supported on macOS.");
				return 0L;
			}
		}

		internal override bool Visible => TryGetNativeInfo(out var native) && native.Visible;

		internal override FormWindowState WindowState
		{
			get
			{
				if (!TryGetNativeInfo(out var native))
					return FormWindowState.Normal;

				return MacAccessibility.TryGetWindowState(native, out var state)
					? state
					: native.VisibleOnScreen ? FormWindowState.Normal : FormWindowState.Minimized;
			}
		}

		// Read-shaped non-scalars: native here (so a held item never routes them back through Platform.Window).
		internal override List<string> GetText(WindowSearchOptions options)
		{
			var titleText = Title;
			return titleText.IsNullOrEmpty() ? [] : [titleText];
		}

		internal override POINT ClientToScreen()
			=> TryGetNativeInfo(out var native) ? new POINT(native.Bounds.X, native.Bounds.Y) : new POINT();

		// Hit-test for ControlFromPoint; macOS has no child controls, so the window matches its own bounds.
		internal void ChildFindPoint(PointAndHwnd pah)
		{
			if (TryGetNativeInfo(out var native) && native.Bounds.Contains(pah.pt.X, pah.pt.Y))
			{
				pah.hwndFound = Handle;
				pah.rectFound = native.Bounds;
			}
		}
	}
}
#endif
