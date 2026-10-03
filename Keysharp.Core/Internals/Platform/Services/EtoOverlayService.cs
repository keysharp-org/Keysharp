namespace Keysharp.Internals
{
#if OSX
	internal sealed class MacOverlay : OverlayBase
	{
		public override PixelSize GetCanvasSize(ScreenRect bounds) => OverlayCanvasSizing.FromEtoScreen(bounds);
		protected override IImageOverlayBacking CreateBacking(uint id, Script owner) => new EtoImageOverlay(owner);
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
	// Shared Eto (GTK/Cocoa) overlay window -- the backing for an INTERACTIVE overlay on GNOME/Cinnamon (a shell
	// actor cannot receive input), the toolkit fallback elsewhere on Linux, and the only backing on macOS. It
	// borrows the canvas and keeps its own `displayed` bitmap, which a same-size move just repositions. On Linux
	// that bitmap is retained and updated from the canvas's damaged rows; macOS replaces it with a snapshot.
	internal sealed class EtoImageOverlay : IImageOverlayBacking
	{
		private readonly Script owner;
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
		private ScreenRect shownBounds;
		private bool shownClickThrough;
		private bool presented;
#if OSX
		private double shownBackingScale = 1;
#endif

		// Read per event by the handlers wired in EnsureForm, so a sink registered before or after the form
		// exists needs no rewiring.
		public Action<OverlayPointerEvent> PointerSink { get; set; }

		// Mouse events use toolkit units; X11 overlays expose root pixels.
		private double pointerScaleX = 1.0, pointerScaleY = 1.0;

		internal EtoImageOverlay(Script owner) => this.owner = owner;

		private OverlayPointerEvent MakePointerEvent(OverlayPointerKind kind, PointF location)
			=> new(kind, (int)Math.Round(location.X * pointerScaleX), (int)Math.Round(location.Y * pointerScaleY));

		public nint Handle => form is { IsDisposed: false, Loaded: true } ? form.Handle : 0;

		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
#if LINUX
			// GTK keeps the bitmap; Cocoa resizes each snapshot for the screen's current backing scale.
			if (form?.IsDisposed == false && form.Loaded && displayed != null
					&& shownOpacity == opacity && damage?.Kind == DamageKind.None)
				return Show(null, damage, bounds, clickThrough, opacity);
#endif
			return Show(canvas.PrepareForPresent(), damage, bounds, clickThrough, opacity);
		}

		// Internal only for ImageTests.GtkOverlaySnapshotPreservesArgbPixels.
		internal static Bitmap Snapshot(Bitmap image, byte opacity)
		{
			if (image == null)
				return null;

#if LINUX
			var snapshot = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgba);

			if (CopyRows(image, snapshot, 0, image.Height, opacity))
				return snapshot;

			using (var graphics = new Graphics(snapshot))
				graphics.DrawImage(image, 0, 0);

			return ImageHelper.ApplyOpacity(snapshot, opacity);
#else
			return ImageHelper.ApplyOpacity(new Bitmap(image), opacity);
#endif
		}

#if LINUX
		// Copies rows [top, bottom) between two same-size Cairo ARGB32 surfaces, scaling by the opacity on the way.
		// Both are premultiplied, so a constant alpha scales all four channels alike. False when either bitmap has
		// no such surface, and the caller takes the general path.
		private static unsafe bool CopyRows(Bitmap source, Bitmap target, int top, int bottom, byte opacity)
		{
			if (source.Handler is not Eto.GtkSharp.Drawing.BitmapHandler { Surface: { } sourceSurface }
					|| target.Handler is not Eto.GtkSharp.Drawing.BitmapHandler { Surface: { } targetSurface }
					|| sourceSurface.Format != Cairo.Format.Argb32 || targetSurface.Format != Cairo.Format.Argb32
					|| sourceSurface.DataPtr == 0 || targetSurface.DataPtr == 0
					|| source.Width != target.Width || source.Height != target.Height)
				return false;

			sourceSurface.Flush();
			targetSurface.Flush();
			var width = source.Width;
			var rowBytes = (long)width * 4;

			for (var y = Math.Max(0, top); y < Math.Min(bottom, source.Height); y++)
			{
				var src = (uint*)((byte*)sourceSurface.DataPtr + (long)y * sourceSurface.Stride);
				var dst = (uint*)((byte*)targetSurface.DataPtr + (long)y * targetSurface.Stride);

				if (opacity == 255)
					Buffer.MemoryCopy(src, dst, rowBytes, rowBytes);
				else
					for (var x = 0; x < width; x++)
						dst[x] = ImageHelper.ScalePremultiplied(src[x], opacity);
			}

			targetSurface.MarkDirty();
			return true;
		}

		// Brings `displayed` up to date with the canvas. It runs on the UI thread, where Paint reads the bitmap, so
		// one retained bitmap is enough: a same-size update copies only the damaged rows, and only a resize allocates.
		private void UpdateDisplayed(Bitmap image, DamageList damage, byte opacity)
		{
			if (image == null)
				return;

			if (displayed != null)
			{
				var rows = opacity == shownOpacity && damage?.Kind == DamageKind.Region
						   ? damage.Union() : new PixelRect(0, 0, image.Width, image.Height);

				if (CopyRows(image, displayed, rows.Y, rows.Bottom, opacity))
					return;
			}

			var old = displayed;
			displayed = Snapshot(image, opacity);
			old?.Dispose();
		}
#endif

		private bool Show(Bitmap image, DamageList damage, ScreenRect bounds, bool clickThrough, byte opacity)
		{
#if OSX
			Bitmap snapshot = null;
			var adopted = false;
#endif

			try
			{
#if OSX
				snapshot = Snapshot(image, opacity);
				var snap = snapshot;
#endif

				owner.InvokeOnUIThread(() =>
				{
					EnsureForm();
					var geometryChanged = !presented || bounds != shownBounds;
					var inputChanged = !presented || clickThrough != shownClickThrough;

					if (inputChanged)
						form.CanFocus = !clickThrough;

					var windowBounds = ToToolkitBounds(bounds);
					// Keep the pointer-coordinate mapping in step with this show's geometry (see the fields).
					pointerScaleX = windowBounds.Width > 0 ? (double)bounds.Width / windowBounds.Width : 1.0;
					pointerScaleY = windowBounds.Height > 0 ? (double)bounds.Height / windowBounds.Height : 1.0;
#if LINUX
					UpdateDisplayed(image, damage, opacity);
					PaintOwned(bounds, windowBounds);
#else
					// PaintOwned adopts the snapshot before any operation that can throw.
					adopted = snap != null;
					PaintOwned(snap, bounds, windowBounds);
#endif
					if (geometryChanged)
						form.Bounds = new Rectangle(windowBounds.X, windowBounds.Y,
							Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));

					var wasVisible = form.Visible;
					if (!form.Visible)
					{
#if LINUX
						_ = Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.ReserveWindow(form,
							windowBounds.X, windowBounds.Y);
#endif
						form.Show();
					}

					if (inputChanged)
						form.SetClickThrough(clickThrough);
#if LINUX
					// A Wayland client cannot place or stack its own toplevel, so the bounds set above and the
					// taskbar/topmost/border options from EnsureForm are silent no-ops; drive the compositor
					// instead, as Gui.Show does. An interactive overlay reaches this path on Mutter-family
					// compositors, where the shell-actor backing refuses it: an actor cannot receive input.
					// skipTaskbar is asked for regardless, but Muffin cannot honour it -- skip-taskbar is
					// read-only there -- so such an overlay does show up in Cinnamon's window list.
					if ((!wasVisible || geometryChanged)
							&& Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
						Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Position(form, form.Title,
							windowBounds.X, windowBounds.Y,
							Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height),
							removeBorder: true, keepAbove: true, skipTaskbar: true);
#endif
					shownOpacity = opacity;
					shownBounds = bounds;
					shownClickThrough = clickThrough;
					presented = true;
				});

				return true;   // borrow: `image` is neither retained nor disposed
			}
			catch
			{
#if OSX
				// The UI-thread invoke threw before PaintOwned took ownership of the snapshot, so it is still ours.
				// Once adopted, `displayed` owns it and Dispose frees it.
				if (!adopted)
					snapshot?.Dispose();
#endif

				return false;
			}
		}

		public bool Move(ScreenRect bounds)
		{
			// Same-size: reposition (the ImageView keeps its bitmap). Resize: re-render via Show.
			if (form is not { IsDisposed: false, Loaded: true } || bounds.Width != shownW || bounds.Height != shownH)
				return false;

			var moved = false;
			owner.InvokeOnUIThread(() =>
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
					shownBounds = bounds;
					moved = true;
				}
			});

			return moved;
		}

#if LINUX
		// Sizes the drawable to the window and repaints it from `displayed`. UI thread. GTK/Cairo owns the mapping
		// from widget units to its backing surface, so Paint draws the renderer-selected raster into the widget's
		// native rectangle; resizing the bitmap instead would throw away HiDPI pixels on Wayland and would wrongly
		// apply GTK's scale to X11 root-pixel coordinates.
		private void PaintOwned(ScreenRect bounds, ScreenRect windowBounds)
		{
			var size = new Size(Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));
			// Invalidate explicitly because same-size content changes do not raise SizeChanged.
			paintW = size.Width;
			paintH = size.Height;
			imageSurface.Size = size;
			imageSurface.Invalidate();
			shownW = bounds.Width;
			shownH = bounds.Height;
		}
#else
		// Adopts `snapshot` (an owned, private copy) as the displayed bitmap, resized to the device pixels of the
		// screen the overlay sits on. UI thread. windowBounds is in Cocoa's logical points.
		private void PaintOwned(Bitmap snapshot, ScreenRect bounds, ScreenRect windowBounds)
		{
			var size = new Size(Math.Max(1, windowBounds.Width), Math.Max(1, windowBounds.Height));
			var old = displayed;
			var next = snapshot;

			try
			{
				// Match Cocoa's point-sized window to the selected screen's device-pixel backing store.
				var screen = Forms.Screen.FromRectangle(new RectangleF(bounds.X, bounds.Y, size.Width, size.Height)) ?? Forms.Screen.PrimaryScreen;
				var backing = ScaleFactor.Normalize(screen?.LogicalPixelSize ?? 1f);
				shownBackingScale = backing;
				var devW = Math.Max(1, (int)Math.Round(size.Width * backing));
				var devH = Math.Max(1, (int)Math.Round(size.Height * backing));

				if (next != null && (next.Width != devW || next.Height != devH))
				{
					var resized = ImageHelper.ResizeBitmap(next, devW, devH, exactPixels: true);

					if (!ReferenceEquals(resized, next))
					{
						var unscaled = next;
						next = resized;
						try { unscaled.Dispose(); } catch { }
					}
				}

				if (next != null)
				{
					displayed = next;
					imageView.Image = next;
				}

				imageView.Size = size;

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
					try { imageView.Image = old; } catch { }
					try { next.Dispose(); } catch { }
				}

				throw;
			}
		}
#endif

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
			{
				if (!form.IsDisposed && (!presented || form.Loaded))
					return;

				Dispose();
			}

			form = new Keysharp.Builtins.KeysharpForm(owner)
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

		// The form, its handle and the displayed bitmap stay for the next Show, which only maps the window again.
		public bool Hide()
		{
			var hidden = true;

			owner.InvokeOnUIThread(() =>
			{
				if (form == null || form.IsDisposed || !form.Loaded)
					return;

				form.Visible = false;
				hidden = !form.Visible;
			});

			return hidden;
		}

		public void Dispose()
		{
			owner.InvokeOnUIThread(() =>
			{
#if LINUX
				// Before the handle dies, since the correlation is keyed by it and holds a claimed compositor id:
				// leaving it would keep that id claimed, so a later overlay - a reshown card gets a new form -
				// could never claim its own window.
				if (form != null && Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.IsSupported)
					Keysharp.Internals.Window.Linux.Wayland.WaylandOwnToplevels.Forget(form);

#endif
				var closing = form;
				form = null;
#if LINUX
				imageSurface = null;
				paintW = paintH = 0;
#else
				imageView = null;
#endif
				shownOpacity = 0;
				shownBounds = default;
				shownClickThrough = false;
				presented = false;

				try
				{
					if (closing?.IsDisposed == false)
					{
						if (closing.Loaded)
							closing.Close();

						closing.Dispose();
					}
				}
				finally
				{
					displayed?.Dispose();
					displayed = null;
				}
			});
		}
	}
#endif
}
