namespace Keysharp.Internals
{
#if OSX
	internal sealed class MacOverlay : OverlayBase
	{
		public override PixelSize GetCanvasSize(ScreenRect bounds) => OverlayCanvasSizing.FromEtoScreen(bounds);
		protected override IImageOverlayBacking CreateBacking(uint id) => new EtoImageOverlay();
	}
#endif

#if LINUX || OSX
	/// <summary>Render-target sizing for the Eto fallback. This stays with the overlay service rather than display
	/// topology because LogicalPixelSize describes the target's backing canvas, not screen coordinates.</summary>
	internal static class OverlayCanvasSizing
	{
		internal static PixelSize FromEtoScreen(ScreenRect bounds)
		{
			var screen = Forms.Screen.FromRectangle(new RectangleF(bounds.X, bounds.Y,
				Math.Max(1, bounds.Width), Math.Max(1, bounds.Height))) ?? Forms.Screen.PrimaryScreen;
			return FromScale(bounds, ScaleFactor.Normalize(screen?.LogicalPixelSize ?? 1f));
		}

		internal static PixelSize FromScale(ScreenRect bounds, double scale)
			=> new(ToPixels(bounds.Width, scale), ToPixels(bounds.Height, scale));

		private static int ToPixels(int length, double scale)
		{
			var value = Math.Round(Math.Max(1, length) * ScaleFactor.Normalize(scale));
			return value >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)value);
		}
	}
#endif

#if LINUX || OSX
	// Shared Eto (GTK/Cocoa) click-through overlay window -- the toolkit fallback on Linux and the only backing on
	// macOS. It borrows `image`: Show snapshots it on the calling thread for isolation and
	// keeps only that private `displayed` bitmap, which a same-size move just repositions.
	internal sealed class EtoImageOverlay : IImageOverlayBacking
	{
		private Keysharp.Builtins.KeysharpForm form;
#if LINUX
		// ImageView scales against GTK's asynchronously updated allocation. A 1:1 Drawable instead limits resize
		// lag to brief clipping or a transparent far edge.
		private Eto.Forms.Drawable imageSurface;
		private int paintW, paintH;
#else
		private ImageView imageView;
#endif
		private Bitmap displayed;
		private int shownW, shownH;
		private byte shownOpacity;
#if OSX
		private double shownBackingScale = 1;
#endif

		// Read per event by the handlers wired in EnsureForm, so a sink registered before/after the form
		// exists (or across TryHide's teardown/recreation) needs no rewiring.
		public Action<OverlayPointerEvent> PointerSink { get; set; }

		// Mouse events use toolkit units; X11 overlays expose root pixels.
		private double pointerScaleX = 1.0, pointerScaleY = 1.0;

		private OverlayPointerEvent MakePointerEvent(OverlayPointerKind kind, PointF location)
			=> new(kind, (int)Math.Round(location.X * pointerScaleX), (int)Math.Round(location.Y * pointerScaleY));

		public nint Handle => form?.Handle ?? 0;

		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
#if LINUX
			var reusePixels = displayed != null && shownOpacity == opacity && damage?.Kind == DamageKind.None;
#else
			const bool reusePixels = false;
#endif
			return Show(reusePixels ? null : canvas.PrepareForPresent(), bounds, clickThrough, opacity);
		}

		internal static Bitmap Snapshot(Bitmap image)
		{
			if (image == null)
				return null;

#if LINUX
			if (image.Handler is not Eto.GtkSharp.Drawing.BitmapHandler { Surface: not null })
				return new Bitmap(image);

			var snapshot = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgba);
			using var graphics = new Graphics(snapshot);
			graphics.DrawImage(image, 0, 0);
			return snapshot;
#else
			return new Bitmap(image);
#endif
		}

		private bool Show(Bitmap image, ScreenRect bounds, bool clickThrough, byte opacity)
		{
			Bitmap snapshot = null;
			var adopted = false;

			try
			{
				if (image != null)
				{
					snapshot = Snapshot(image);
					ImageHelper.ApplyOpacity(snapshot, opacity);
				}

				var snap = snapshot;

				Script.InvokeOnUIThread(() =>
				{
					EnsureForm();
					form.CanFocus = !clickThrough;
					var windowBounds = ToToolkitBounds(bounds);
					// Keep the pointer-coordinate mapping in step with this show's geometry (see the fields).
					pointerScaleX = windowBounds.Width > 0 ? (double)bounds.Width / windowBounds.Width : 1.0;
					pointerScaleY = windowBounds.Height > 0 ? (double)bounds.Height / windowBounds.Height : 1.0;
					// PaintOwned adopts the snapshot before any operation that can throw.
					adopted = snap != null;
					PaintOwned(snap, bounds, windowBounds);
					// Bounds maps to one native move-resize request.
					form.Bounds = new Rectangle(windowBounds.X, windowBounds.Y,
						Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));

					if (!form.Visible)
						form.Show();

					form.SetClickThrough(clickThrough);
#if LINUX
					// A Wayland client cannot place or stack its own toplevel, so the bounds set above and the
					// taskbar/topmost/border options from EnsureForm are silent no-ops; drive the compositor
					// instead, as Gui.Show does. An interactive overlay reaches this path on Mutter-family
					// compositors, where the shell-actor backing refuses it: an actor cannot receive input.
					if (Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
						Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Position(form, form.Title,
							windowBounds.X, windowBounds.Y,
							Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height),
							removeBorder: true, keepAbove: true, skipTaskbar: true);
#endif
					shownOpacity = opacity;
				});

				return true;   // borrow: `image` is neither retained nor disposed
			}
			catch
			{
				// The UI-thread invoke threw before PaintOwned took ownership of the snapshot -- dispose it here so it
				// does not leak. If ownership had transferred, `displayed` owns it now and TryHide will free it.
				if (!adopted)
					snapshot?.Dispose();

				return false;
			}
		}

		public bool Move(ScreenRect bounds)
		{
			// Same-size: reposition (the ImageView keeps its bitmap). Resize: re-render via Show.
			if (form == null || bounds.Width != shownW || bounds.Height != shownH)
				return false;

			var moved = false;
			Script.InvokeOnUIThread(() =>
			{
				if (form != null)
				{
					var windowBounds = ToToolkitBounds(bounds);
#if OSX
					var screen = Forms.Screen.FromRectangle(new RectangleF(bounds.X, bounds.Y,
						Math.Max(1, bounds.Width), Math.Max(1, bounds.Height))) ?? Forms.Screen.PrimaryScreen;

					if (Math.Abs(ScaleFactor.Normalize(screen?.LogicalPixelSize ?? 1f) - shownBackingScale) > 0.0001)
						return;
#endif
					form.Location = new Point(windowBounds.X, windowBounds.Y);
#if LINUX
					// The setter above is a no-op on Wayland (see Show), so re-assert through the compositor. Moves
					// coalesce per form, so a drag collapses to the latest position rather than one trip per frame.
					if (Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
						Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Position(form, form.Title,
							windowBounds.X, windowBounds.Y,
							Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));
#endif
					moved = true;
				}
			});

			return moved;
		}

		// Adopts `snapshot` (an owned, private copy) as the displayed bitmap, resizing it if needed. UI thread.
		// x/y are the overlay's on-screen position and width/height its on-screen size, in the toolkit's window
		// coordinate units (physical px on GTK, logical points on Cocoa). x/y are used only on macOS to pick the
		// screen the overlay actually sits on (for the right backing scale); GTK ignores them here.
		private void PaintOwned(Bitmap snapshot, ScreenRect bounds, ScreenRect windowBounds)
		{
			var size = new Size(Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));
			var old = displayed;
			var next = snapshot;

			try
			{
#if OSX
				// Match Cocoa's point-sized window to the selected screen's device-pixel backing store.
				var screen = Forms.Screen.FromRectangle(new RectangleF(bounds.X, bounds.Y, size.Width, size.Height)) ?? Forms.Screen.PrimaryScreen;
				var backing = ScaleFactor.Normalize(screen?.LogicalPixelSize ?? 1f);
				shownBackingScale = backing;
				var devW = Math.Max(1, (int)Math.Round(size.Width * backing));
				var devH = Math.Max(1, (int)Math.Round(size.Height * backing));

				if (next.Width != devW || next.Height != devH)
				{
					var resized = ImageHelper.ResizeBitmap(next, devW, devH, exactPixels: true);

					if (!ReferenceEquals(resized, next))
					{
						var unscaled = next;
						next = resized;
						try { unscaled.Dispose(); } catch { }
					}
				}

				displayed = next;
#else
				// GTK/Cairo owns the mapping from widget units to its backing surface. Keep the renderer-selected raster
				// intact and draw it into the widget's native rectangle in the Paint handler; resizing it here would throw
				// away HiDPI pixels on Wayland and would incorrectly apply GTK's scale to X11 root-pixel coordinates.
				if (next != null)
					displayed = next;
#endif

#if LINUX
				// Invalidate explicitly because same-size content changes do not raise SizeChanged.
				paintW = size.Width;
				paintH = size.Height;
				imageSurface.Size = size;
				imageSurface.Invalidate();
#else
				imageView.Image = next;
				imageView.Size = size;
#endif
				// The view must stop referencing the replaced frame before it is disposed.
				if (next != null)
					try { old?.Dispose(); } catch { }

				shownW = bounds.Width;
				shownH = bounds.Height;
			}
			catch
			{
				if (next != null)
				{
					displayed = old;
#if OSX
					try { imageView.Image = old; } catch { }
#endif
					try { next.Dispose(); } catch { }
				}

				throw;
			}
		}

		private static ScreenRect ToToolkitBounds(ScreenRect bounds)
		{
#if LINUX
			if (!IsWaylandSession)
				return Keysharp.Internals.Window.Linux.X11.X11DisplayTopology.ToToolkitBounds(bounds);
#endif
			return bounds;
		}

		private void EnsureForm()
		{
			if (form != null)
				return;

			form = new Keysharp.Builtins.KeysharpForm
			{
				FormBorderStyle = Keysharp.Builtins.FormBorderStyle.None,
				ShowInTaskbar = false,
				ShowActivated = false,
				CanFocus = false,
				TopMost = true,
				// GTK ignores shrinking a non-resizable window; the borderless overlay still has no user resize affordance.
				Resizable = true,
				BackgroundColor = Colors.Transparent
			};
#if LINUX
			imageSurface = new Eto.Forms.Drawable { BackgroundColor = Colors.Transparent };
			// Make the underlying GTK EventBox windowless so the transparent, click-through form shows through the
			// drawable instead of it painting its own opaque window (same recipe as KeysharpLinkLabel).
			try
			{
				if (imageSurface.ToNative() is Gtk.EventBox eventBox)
					eventBox.VisibleWindow = false;
			}
			catch { }
			imageSurface.Paint += (s, e) =>
			{
				// Clear to transparent first so a lagging-large allocation leaves no ghost of the previous frame in
				// the margin, then blit the bitmap 1:1 at the top-left (which the window's Location already tracks).
				e.Graphics.Clear();
				var d = displayed;

				if (d != null)
					e.Graphics.DrawImage(d, 0, 0, Math.Max(1, paintW), Math.Max(1, paintH));
			};
			// Repaint whenever the widget's allocation actually changes. A GTK window adopts its new size only after
			// the WM's async ConfigureNotify, so a frame painted mid-resize is clipped to a stale allocation; without
			// this, a resize whose final allocation lands after the last Invalidate stays cropped until the next size
			// change. Re-blitting on each real allocation guarantees the settled frame shows the whole bitmap.
			imageSurface.SizeChanged += (s, e) => imageSurface?.Invalidate();
			form.Content = imageSurface;
#else
			imageView = new ImageView { BackgroundColor = Colors.Transparent };
			form.Content = imageView;
#endif
			// Pointer events for OnEvent: only a non-click-through window receives them from the toolkit, so no
			// extra gating is needed here beyond a registered sink. Coordinates are the toolkit's window-local
			// units, which are the overlay's native draw units.
			form.MouseUp += (s, e) =>
			{
				var sink = PointerSink;

				if (sink == null)
					return;

				if (e.Buttons == Forms.MouseButtons.Primary)
					sink(MakePointerEvent(OverlayPointerKind.Click, e.Location));
				else if (e.Buttons == Forms.MouseButtons.Alternate)
					sink(MakePointerEvent(OverlayPointerKind.ContextMenu, e.Location));
			};
			form.MouseDoubleClick += (s, e) =>
			{
				if (e.Buttons == Forms.MouseButtons.Primary)
					PointerSink?.Invoke(MakePointerEvent(OverlayPointerKind.DoubleClick, e.Location));
			};
			form.MouseMove += (s, e) =>
				PointerSink?.Invoke(MakePointerEvent(OverlayPointerKind.MouseMove, e.Location));
#if OSX
			// MouseMove on a never-key window (ShowActivated=false) needs acceptsMouseMovedEvents on the
			// NSWindow — Cocoa otherwise routes motion only to the key window. First-click delivery is a
			// view-level acceptsFirstMouse question that cannot be set from here; it is flagged in
			// docs/design-wayland-overlay-input.md as the one remaining macOS unknown for OnEvent.
			try
			{
				if (form.ControlObject is MonoMac.AppKit.NSWindow nsw)
					nsw.AcceptsMouseMovedEvents = true;
			}
			catch { }
#endif
			form.SetClickThrough(true);
			// Override-redirect preserves exact placement and stacking for X11 overlays.
#if LINUX
			Eto.Forms.EtoExtensions.SetFormOverlayTopmost(form);
#endif
		}

		public bool TryHide()
		{
			var closed = true;

			// InvokeOnUIThread is synchronous, so `closed`/`form` reflect the outcome once it returns.
			Script.InvokeOnUIThread(() =>
			{
				try
				{
#if LINUX
					// Before the handle dies, since the correlation is keyed by it and holds a claimed compositor
					// id: leaving it would keep that id claimed, so a later overlay - a reshown card gets a new
					// form - could never claim its own window.
					if (form != null && Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
						Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Forget(form.Handle);

#endif
					form?.Close();
					form?.Dispose();
					form = null;   // only reached when Close/Dispose didn't throw
#if LINUX
					imageSurface = null;
					paintW = paintH = 0;
#else
					imageView = null;
#endif
					displayed?.Dispose();
					displayed = null;
					shownOpacity = 0;
				}
				catch { closed = false; }   // leave `form` set so a later retry can re-close it
			});

			return closed && form == null;
		}

		public void Dispose() => _ = TryHide();
	}
#endif
}
