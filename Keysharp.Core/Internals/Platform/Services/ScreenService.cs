#if LINUX
using Wl = Keysharp.Internals.Window.Linux.Wayland;
#endif

namespace Keysharp.Internals
{
#if !WINDOWS
	/// <summary>Captures each intersected Eto display independently, then composites the backing-pixel images
	/// into one bitmap. Screen positions and requested extents remain in the platform's native screen units.</summary>
	internal static class EtoScreenCapture
	{
		internal static bool TryCapture(int x, int y, int width, int height, out Bitmap bmp)
		{
			bmp = null;

			if (width <= 0 || height <= 0)
				return false;

			var captures = new List<(ScreenRect Bounds, Bitmap Pixels)>();

			try
			{
				foreach (var screen in Forms.Screen.Screens ?? [])
				{
					if (screen == null)
						continue;

					var bounds = ScreenRect.FromRectangle(screen.Bounds);
					var left = Math.Max((long)x, bounds.X);
					var top = Math.Max((long)y, bounds.Y);
					var right = Math.Min((long)x + width, (long)bounds.X + bounds.Width);
					var bottom = Math.Min((long)y + height, (long)bounds.Y + bounds.Height);

					if (right <= left || bottom <= top)
						continue;

					var segmentWidth = (int)(right - left);
					var segmentHeight = (int)(bottom - top);
#if OSX
					// Cocoa's capture handler accepts the same global point space exposed by Screen.Bounds.
					var captureRect = new RectangleF(left, top, segmentWidth, segmentHeight);
#else
					// GDK's per-monitor handler adds its monitor origin before reading the root window.
					var captureRect = new RectangleF(left - bounds.X, top - bounds.Y, segmentWidth, segmentHeight);
#endif
					if (screen.GetImage(captureRect) is not Bitmap image || image.Width <= 0 || image.Height <= 0)
						return false;

					captures.Add((new ScreenRect((int)left, (int)top, segmentWidth, segmentHeight), image));
				}

				if (captures.Count == 0)
					return false;

				bmp = ScreenCaptureComposer.Compose(new ScreenRect(x, y, width, height), captures);
				return bmp != null;
			}
			finally
			{
				foreach (var capture in captures)
					capture.Pixels.Dispose();
			}
		}

	}
#endif

#if LINUX
	/// <summary>
	/// Resolves the one Linux <see cref="IScreen"/> for this session — the only place the compositor flavor is
	/// inspected. After this, capture/work-area/authorization dispatch is plain virtual calls: the right
	/// implementation was already chosen, so there are no <c>IsWaylandSession</c> / <c>is …Backend</c> checks on
	/// the hot path.
	/// </summary>
	internal static class LinuxScreens
	{
		internal static IScreen Resolve()
		{
			if (!IsWaylandSession)
				return new X11Screen();

			return Wl.WaylandBackend.Current switch
			{
				Wl.KWinBrokerBackend kwin => new KWinScreen(kwin),
				Wl.WaylandBackend.GnomeBackend gnome => new GnomeScreen(gnome),
				Wl.CinnamonBackend cinnamon => new CinnamonScreen(cinnamon),
				Wl.WaylandBackend.CosmicBackend cosmic => new CosmicScreen(cosmic),
				// Wayland without a recognized compositor helper: no compositor-specific capture/overlay, but
				// wl_output/xdg-output (core protocols every compositor advertises) still give real monitor
				// topology and metadata through WaylandLayerShellClient, so this is not plain EtoScreen.
				null => new GenericWaylandScreen(),
				var other => new WlrootsScreen(other),        // sway/Hyprland/wlroots: tries zwlr, else Eto
			};
		}
	}

	/// <summary>Base Linux screen: Eto root-window grab for regions, no true window capture (caller
	/// rectangle-grabs), no work-area override (caller uses Eto's per-screen WorkingArea), no authorization.</summary>
	internal class EtoScreen : IScreen
	{
		public virtual IReadOnlyList<DisplayInfo> GetDisplays()
		{
			var screens = Forms.Screen.Screens?.ToArray() ?? [];
			var result = new List<DisplayInfo>(screens.Length);
			var anyPrimary = screens.Any(s => s?.IsPrimary == true);

			for (var i = 0; i < screens.Length; i++)
			{
				var screen = screens[i];

				if (screen == null)
					continue;

				var bounds = ScreenRect.FromRectangle(screen.Bounds);
				ScreenRect workArea;

				try { workArea = ScreenRect.FromRectangle(screen.WorkingArea); }
				catch { workArea = bounds; }

				var primary = screen.IsPrimary || !anyPrimary && bounds.X == 0 && bounds.Y == 0;
				// Wayland screen coordinates already include the compositor's UI scaling. Under X11 the fallback
				// toolkit topology may still expose a separate monitor scale; the X11-native topology normally overrides it.
				var scale = IsWaylandSession ? 1.0 : ScaleFactor.Normalize(screen.DPI / 96.0);
				result.Add(new DisplayInfo(screen.ID ?? $"display-{i + 1}", bounds, workArea, scale, primary));
			}

			return result;
		}

		// The DRM connector (EDID, model, serial, physical size, connection kind) is readable from sysfs under
		// every Linux session type, so the shared base answers it. Refresh rate and rotation are session-specific
		// and stay 0/unknown here; X11Screen and WaylandScreen override to supply them.
		public virtual DisplayDetails GetDisplayDetails(DisplayInfo display)
			=> LinuxMonitorDetails.Get(display, 0.0, 0);

		public virtual bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
			=> EtoScreenCapture.TryCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height, out bmp);

		public virtual bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			bmp = null;
			pixelScale = PixelScale.One;
			return false;   // no occlusion-independent window capture here → caller falls back to a rectangle grab
		}

		public virtual bool RequiresAuthorization => false;

		public virtual Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> new (Os.PermissionStatus.NotApplicable);
	}

	/// <summary>X11 (including KDE/GNOME/Cinnamon under X11): public coordinates are native root-window pixels.
	/// Capture runs through keysharp-desktop without rescaling; its XComposite path also supports occluded windows.</summary>
	internal sealed class X11Screen : EtoScreen
	{
		public override IReadOnlyList<DisplayInfo> GetDisplays()
		{
			var displays = X11DisplayTopology.GetDisplays();
			return displays.Count > 0 ? displays : base.GetDisplays();
		}

		// NativeId carries the RandR output XID for this display, which owns the current mode and rotation.
		public override DisplayDetails GetDisplayDetails(DisplayInfo display)
		{
			var (refresh, orientation) = X11DisplayTopology.GetOutputMode((nuint)display.NativeId);
			return LinuxMonitorDetails.Get(display, refresh, orientation);
		}

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = Wl.DesktopClient.CaptureX11(bounds.X, bounds.Y, bounds.Width, bounds.Height);
			return bmp != null;
		}

		public override bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			bmp = h.ToInt64() is > 0 and <= uint.MaxValue
				? Wl.DesktopClient.CaptureX11Window((ulong)h, includeDecoration) : null;
			pixelScale = PixelScale.One;
			return bmp != null;
		}

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.AuthorizeX11(operation, prompt);
	}

	/// <summary>Shared Wayland-compositor base: work area comes from the compositor (a client can't compute it),
	/// and region capture falls back to the Eto grab when the compositor path returns nothing.</summary>
	internal abstract class WaylandScreen : EtoScreen
	{
		protected readonly Wl.IWaylandBackend backend;

		protected WaylandScreen(Wl.IWaylandBackend backend) => this.backend = backend;

		public override IReadOnlyList<DisplayInfo> GetDisplays()
		{
			var native = Wl.WaylandLayerShellClient.Current?.GetDisplays();
			var toolkit = base.GetDisplays().ToArray();
			var displays = native is { Count: > 0 } ? native.ToArray() : toolkit;

			// xdg-output supplies exact global geometry but not reserved panels/docks. Merge every GDK monitor's
			// per-output working area into the native snapshot before applying a compositor-specific override. This
			// preserves multi-monitor work areas instead of updating only whichever monitor owned one global rectangle.
			if (native is { Count: > 0 })
				for (var i = 0; i < displays.Length; i++)
					if (DisplayTopology.TryFind(toolkit, displays[i].Bounds, out var match))
					{
						var overlap = displays[i].Bounds.Intersect(match.Bounds);

						if (overlap.HasArea)
							displays[i] = displays[i] with { WorkArea = match.WorkArea };
					}

			if (backend != null && backend.TryGetWorkArea(out var wa) && wa.Width > 0 && wa.Height > 0)
			{
				var workArea = ScreenRect.FromRectangle(wa);

				if (DisplayTopology.TryFind(displays, workArea, out var owner))
					for (var i = 0; i < displays.Length; i++)
						if (displays[i].Equals(owner))
						{
							displays[i] = displays[i] with { WorkArea = workArea };
							break;
						}
			}

			return displays;
		}

		/// <summary>
		/// Wayland already delivered this output's physical size, make/model, mode refresh and transform in its
		/// geometry/mode events, so those need no extra query. The panel's EDID identity still comes from the DRM
		/// connector, which is readable regardless of compositor.
		/// </summary>
		public override DisplayDetails GetDisplayDetails(DisplayInfo display)
		{
			var client = Wl.WaylandLayerShellClient.Current;

			if (client == null || !client.TryGetOutputMetrics((uint)display.NativeId, out var refresh,
					out var orientation, out var mmWidth, out var mmHeight, out var make, out var model))
				return base.GetDisplayDetails(display);

			var details = LinuxMonitorDetails.Get(display, refresh, orientation);

			// wl_output's make/model fill in for a connector whose EDID could not be read (a compositor running
			// on a driver that exposes no sysfs edid), but never override the EDID values when those exist.
			return details with
			{
				Model = details.Model.Length > 0 ? details.Model : model,
				Manufacturer = details.Manufacturer.Length > 0 ? details.Manufacturer : make,
				PhysicalWidthMm = details.PhysicalWidthMm > 0 ? details.PhysicalWidthMm : mmWidth,
				PhysicalHeightMm = details.PhysicalHeightMm > 0 ? details.PhysicalHeightMm : mmHeight,
			};
		}
	}

	/// <summary>KWin Wayland via keysharp-desktop: region grabs through the ScreenShot2 interface,
	/// window grabs keyed by the window's internalId UUID (occlusion-independent).</summary>
	internal sealed class KWinScreen : WaylandScreen
	{
		private readonly Wl.KWinBrokerBackend kwin;

		internal KWinScreen(Wl.KWinBrokerBackend backend) : base(backend) => kwin = backend;

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = Wl.DesktopClient.Capture(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (bmp != null) return true;
			if (!Wl.DesktopClient.EnsureCaptureConsent()) return false;

			return base.TryCaptureRegion(bounds, out bmp);
		}

		public override bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			pixelScale = PixelScale.One;

			// org.kde.KWin.ScreenShot2.CaptureWindow re-renders off-screen → occlusion-independent. Windows
			// without a real internalId (the "windowId:" fallback) miss here → caller rectangle-grabs.
			if (kwin.TryGetWindowUuid(h, out var uuid))
			{
				bmp = Wl.DesktopClient.CaptureKWinWindow(uuid, includeDecoration);
				return bmp != null;
			}

			bmp = null;
			return false;
		}

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.Authorize(operation, prompt);
	}

	/// <summary>GNOME Wayland via keysharp-desktop + Shell extension: region grabs through the
	/// extension, window grabs image the window actor's own buffer (occlusion-independent; includes decoration).</summary>
	internal sealed class GnomeScreen : WaylandScreen
	{
		private readonly Wl.WaylandBackend.GnomeBackend gnome;

		internal GnomeScreen(Wl.WaylandBackend.GnomeBackend backend) : base(backend) => gnome = backend;

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = Wl.DesktopClient.CaptureGnome(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (bmp != null) return true;
			if (!Wl.DesktopClient.EnsureCaptureConsent()) return false;

			return base.TryCaptureRegion(bounds, out bmp);
		}

		public override bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			pixelScale = PixelScale.One;

			// The extension matches by raw stable_sequence, so strip the marker first (TryGetWindowSeq does that);
			// the actor image is clipped to the frame rect (includes decorations), so includeDecoration is ignored.
			if (gnome.TryGetWindowSeq(h, out var seq))
			{
				bmp = Wl.DesktopClient.CaptureGnomeWindow(seq);

				if (bmp == null)
					return false;

				// The actor buffer is DEVICE pixels while the frame bounds are logical, and the extension
				// clips the image to the frame, so each bitmap-to-frame ratio is its axis scale.
				var bounds = Platform.Window.GetBounds(h);

				pixelScale = PixelScale.From(bmp, bounds);

				return true;
			}

			bmp = null;
			return false;
		}

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.AuthorizeGnome(operation, prompt);
	}

	/// <summary>Cinnamon Wayland via keysharp-desktop + Shell extension: region grabs go through
	/// the broker (which enforces the user's capture consent) to the extension, which
	/// captures via Cinnamon.Screenshot. Window capture images the window actor's own buffer through the
	/// extension's CaptureWindow (occlusion-independent, like KWin/GNOME), falling back to a rectangle grab
	/// of the on-screen frame when the extension can't capture (older extension, minimized window).</summary>
	internal sealed class CinnamonScreen : WaylandScreen
	{
		private readonly Wl.CinnamonBackend cinnamon;

		internal CinnamonScreen(Wl.CinnamonBackend backend) : base(backend) => cinnamon = backend;

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = Wl.DesktopClient.CaptureCinnamon(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (bmp != null) return true;
			if (!Wl.DesktopClient.EnsureCaptureConsent()) return false;

			return base.TryCaptureRegion(bounds, out bmp);
		}

		public override bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale)
		{
			bmp = null;
			pixelScale = PixelScale.One;

			// Preferred: the extension images the window actor's own buffer, clipped to the frame rect —
			// occluded windows capture correctly, and the size ratio to the logical frame IS the device
			// scale. includeDecoration is moot (Cinnamon reports client == frame).
			var bounds = Platform.Window.GetBounds(h);

			if (cinnamon.TryGetWindowSeq(h, out var seq)
					&& Wl.DesktopClient.CaptureCinnamonWindow(seq) is Bitmap actorBmp)
			{
				bmp = actorBmp;

				pixelScale = PixelScale.From(bmp, bounds);

				return true;
			}

			// Fallback (older installed extension, minimized window): rectangle-grab the on-screen frame —
			// occlusion-dependent, but still routed through the consent-enforcing broker.
			if (bounds.Width <= 0 || bounds.Height <= 0)
				return false;

			bmp = Wl.DesktopClient.CaptureCinnamon(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (bmp == null)
				return false;

			// CaptureArea returns DEVICE pixels: on a HiDPI monitor the bitmap is larger than the logical bounds.
			// Report both capture-axis scales so fractional rounding cannot skew mapped OCR coordinates.
			pixelScale = PixelScale.From(bmp, bounds);
			return true;
		}

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.AuthorizeCinnamon(operation, prompt);
	}

	/// <summary>wlroots compositors (sway/Hyprland/Wayfire/…): region grabs through keysharp-desktop; no foreign
	/// per-window protocol, so window capture falls back to a rectangle grab.</summary>
	internal sealed class WlrootsScreen : WaylandScreen
	{
		internal WlrootsScreen(Wl.IWaylandBackend backend) : base(backend) { }

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = Wl.DesktopClient.CaptureGeneric(bounds.X, bounds.Y, bounds.Width, bounds.Height);

			if (bmp != null) return true;
			if (!Wl.DesktopClient.EnsureCaptureConsent()) return false;

			return base.TryCaptureRegion(bounds, out bmp);
		}

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.AuthorizeGeneric(operation, prompt);
	}

	/// <summary>COSMIC capture through keysharp-desktop. A failed region capture retries as one complete-desktop
	/// image so the broker can use its bounded portal fallback without changing CaptureArea semantics.</summary>
	internal sealed class CosmicScreen : WaylandScreen
	{
		internal CosmicScreen(Wl.WaylandBackend.CosmicBackend backend) : base(backend) { }

		public override bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			var direct = Wl.DesktopClient.CaptureGenericWithStatus(
				bounds.X, bounds.Y, bounds.Width, bounds.Height, out bmp);

			if (direct == Wl.DesktopCaptureStatus.Captured)
				return true;

			// A stopped session can represent an explicit user stop, so it is as authoritative as helper denial.
			if (!ShouldTryDesktopFallback(direct))
				return false;

			var status = Wl.BrokerDesktopCapture.Capture(bounds, GetDisplays(), out bmp);
			return status == Wl.DesktopCaptureStatus.Captured;
		}

		internal static bool ShouldTryDesktopFallback(Wl.DesktopCaptureStatus status)
			=> status is Wl.DesktopCaptureStatus.Unavailable or Wl.DesktopCaptureStatus.Failed;

		public override bool RequiresAuthorization => true;

		public override Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt)
			=> Wl.DesktopClient.AuthorizeGeneric(operation, prompt);
	}

	/// <summary>Unrecognized Wayland compositor: standard output topology with the existing toolkit capture fallback.</summary>
	internal sealed class GenericWaylandScreen : WaylandScreen
	{
		internal GenericWaylandScreen() : base(null) { }
	}
#elif WINDOWS
	internal sealed class WindowsScreen : IScreen
	{
		public IReadOnlyList<DisplayInfo> GetDisplays()
		{
			var screens = Forms.Screen.AllScreens;
			var result = new DisplayInfo[screens.Length];

			for (var i = 0; i < screens.Length; i++)
			{
				var screen = screens[i];
				var bounds = ScreenRect.FromRectangle(screen.Bounds);
				result[i] = new DisplayInfo(screen.DeviceName ?? $"display-{i + 1}", bounds,
					ScreenRect.FromRectangle(screen.WorkingArea), GetScale(bounds), screen.Primary);
			}

			return result;
		}

		public DisplayDetails GetDisplayDetails(DisplayInfo display) => WindowsMonitorDetails.Get(display);

		private static double GetScale(ScreenRect bounds)
		{
			var rect = new RECT
			{
				Left = bounds.X,
				Top = bounds.Y,
				Right = (int)Math.Clamp(bounds.Right, int.MinValue, int.MaxValue),
				Bottom = (int)Math.Clamp(bounds.Bottom, int.MinValue, int.MaxValue)
			};
			var monitor = WindowsAPI.MonitorFromRect(ref rect, 2); // MONITOR_DEFAULTTONEAREST

			if (monitor != 0 && WindowsAPI.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
				return dpiX / 96.0;

			return 1.0;
		}

		public bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
		{
			bmp = null;
			// A PMv2-aware process sees the whole Win32 virtual desktop in physical pixels. Monitor UI scaling
			// therefore never changes the 1:1 relationship between this rectangle and CopyFromScreen's bitmap.

			if (!bounds.HasArea)
				return false;

			var format = Forms.Screen.PrimaryScreen.BitsPerPixel switch
			{
				8 or 16 => PixelFormat.Format16bppRgb565,
				24 => PixelFormat.Format24bppRgb,
				32 => PixelFormat.Format32bppArgb,
				_ => PixelFormat.Format32bppArgb,
			};

			var result = new Bitmap(bounds.Width, bounds.Height, format);

			try
			{
				using var graphics = Graphics.FromImage(result);
				graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
				bmp = result;
				return true;
			}
			catch
			{
				result.Dispose();
				return false;
			}
		}
		public bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale) { bmp = null; pixelScale = PixelScale.One; return false; }
		public bool RequiresAuthorization => false;
		public Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt) => new (Os.PermissionStatus.NotApplicable);
	}
#elif OSX
	internal sealed class MacScreen : IScreen
	{
		public IReadOnlyList<DisplayInfo> GetDisplays()
		{
			var screens = Forms.Screen.Screens?.ToArray() ?? [];
			var result = new List<DisplayInfo>(screens.Length);

			for (var i = 0; i < screens.Length; i++)
			{
				var screen = screens[i];

				if (screen == null)
					continue;

				var bounds = ScreenRect.FromRectangle(screen.Bounds);
				ScreenRect workArea;
				try { workArea = ScreenRect.FromRectangle(screen.WorkingArea); }
				catch { workArea = bounds; }
				// NSScreen's localizedName ("Built-in Retina Display", "DELL U2720Q") is the name macOS itself
				// shows for a display. Eto's Screen.ID is kept only as the fallback: no Cocoa screen handler sets
				// it, so before this it was always the synthetic display-N that reached scripts.
				var name = MacScreenNames.LocalizedName(screen);
				result.Add(new DisplayInfo(
					name.Length > 0 ? name : screen.ID is { Length: > 0 } id ? id : $"display-{i + 1}",
					bounds, workArea, 1.0, screen.IsPrimary));
			}

			return result;
		}

		public DisplayDetails GetDisplayDetails(DisplayInfo display) => MacMonitorDetails.Get(display);

		public bool TryCaptureRegion(ScreenRect bounds, out Bitmap bmp)
			=> EtoScreenCapture.TryCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height, out bmp);
		public bool TryCaptureWindow(nint h, bool includeDecoration, out Bitmap bmp, out PixelScale pixelScale) { bmp = null; pixelScale = PixelScale.One; return false; }
		public bool RequiresAuthorization => true;   // macOS Screen Recording permission (handled in MacPermissionManager)
		public Os.PermissionResult RequestCaptureAuthorization(string operation, bool prompt) => new (Os.PermissionStatus.NotApplicable);
	}
#endif
}
