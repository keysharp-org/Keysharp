#if WINDOWS
namespace Keysharp.Internals
{
	internal sealed class WindowsOverlay : OverlayBase
	{
		public override PixelSize GetCanvasSize(ScreenRect bounds)
			=> new(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
		protected override IImageOverlayBacking CreateBacking(uint id, Script owner) => new WindowsImageOverlay(owner);

		public override OverlaySurface CreateOverlaySurface(PixelSize pixels)
			=> pixels.HasArea ? DibOverlaySurface.TryCreate(pixels) : null;
	}

	/// <summary>
	/// Owns the three GDI objects behind a <see cref="DibOverlaySurface"/> — the DIB section, the memory DC, and
	/// the bitmap displaced when the DIB was selected in — and frees them in the one order that is valid.
	///
	/// A SafeHandle is critical-finalizable, so its release runs after ordinary finalizers. The GDI+ Bitmap
	/// wrapping these pixels therefore goes first and cannot be left pointing at a freed section.
	/// </summary>
	internal sealed class DibSectionHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		private nint hdcMem;
		private nint hOldBitmap;

		internal DibSectionHandle(nint hDib, nint hdcMem, nint hOldBitmap) : base(true)
		{
			SetHandle(hDib);
			this.hdcMem = hdcMem;
			this.hOldBitmap = hOldBitmap;
		}

		internal nint SourceDC => hdcMem;

		protected override bool ReleaseHandle()
		{
			// The DIB must leave the DC before it is deleted: a selected object cannot be freed.
			if (hdcMem != 0)
			{
				if (hOldBitmap != 0)
					_ = WindowsAPI.SelectObject(hdcMem, hOldBitmap);

				_ = WindowsAPI.DeleteDC(hdcMem);
				hdcMem = 0;
				hOldBitmap = 0;
			}

			if (handle != 0)
			{
				_ = WindowsAPI.DeleteObject(handle);
				handle = 0;
			}

			return true;
		}
	}

	/// <summary>
	/// A surface whose pixels live in a GDI DIB section: addressable as memory, so GDI+ can draw into it, and
	/// selectable into a DC, so <c>UpdateLayeredWindow</c> can read it. That double identity is what lets a
	/// frame reach the compositor without being copied. Premultiplied, because that is the form the layered
	/// blend consumes.
	/// </summary>
	internal sealed class DibOverlaySurface : OverlaySurface
	{
		private readonly DibSectionHandle gdi;

		/// <summary>The DC with the DIB selected into it — the source of an <c>UpdateLayeredWindow</c>. It stays
		/// selected for the canvas's whole life. GDI+ draws through <see cref="OverlaySurface.Bitmap"/>, the
		/// presentation boundary settles that drawing state, and GDI reads the same pixels.</summary>
		internal nint SourceDC => gdi.IsClosed ? 0 : gdi.SourceDC;

		private DibOverlaySurface(Bitmap bitmap, PixelSize size, DibSectionHandle gdi)
			: base(bitmap, size, premultiplied: true) => this.gdi = gdi;

		/// <summary>Null (not an exception) if any GDI step fails.</summary>
		internal static DibOverlaySurface TryCreate(PixelSize pixels)
		{
			var w = Math.Max(1, pixels.Width);
			var h = Math.Max(1, pixels.Height);
			// Negative height = top-down rows, matching GDI+'s own layout so both APIs read the buffer the same way.
			var header = new BITMAPINFOHEADER
			{
				biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = w,
				biHeight = -h,
				biPlanes = 1,
				biBitCount = 32,
				biCompression = 0,   // BI_RGB
			};
			nint dib = 0, dc = 0, old = 0, screenDc = 0;
			Bitmap bmp = null;
			DibSectionHandle gdi = null;

			try
			{
				screenDc = WindowsAPI.GetDC(0);

				if (screenDc == 0)
					return null;

				dib = WindowsAPI.CreateDIBSection(screenDc, ref header, 0 /* DIB_RGB_COLORS */, out var bits, 0, 0);

				if (dib == 0 || bits == 0)
					return null;

				// A memory DC created from null belongs to its creator thread and dies with that thread. Using a
				// real reference DC gives the surface the same lifetime as its owning safe handle.
				dc = WindowsAPI.CreateCompatibleDC(screenDc);

				if (dc == 0)
					return null;

				old = WindowsAPI.SelectObject(dc, dib);

				if (old == 0 || old == new nint(-1))
					return null;

				bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
				// Same reason as ImageHelper.NewArgbCanvas: a point-size font is scaled by the Graphics' DPI, and
				// a canvas that inherited the screen's 192 would render text at twice the size of everything else.
				bmp.SetResolution(96f, 96f);
				gdi = new DibSectionHandle(dib, dc, old);
				dib = dc = old = 0;
				var canvas = new DibOverlaySurface(bmp, new PixelSize(w, h), gdi);
				bmp = null;
				gdi = null;
				return canvas;
			}
			catch
			{
				return null;
			}
			finally
			{
				bmp?.Dispose();
				gdi?.Dispose();

				if (dc != 0)
				{
					if (old != 0 && old != new nint(-1))
						_ = WindowsAPI.SelectObject(dc, old);

					_ = WindowsAPI.DeleteDC(dc);
				}

				if (dib != 0)
					_ = WindowsAPI.DeleteObject(dib);

				if (screenDc != 0)
					_ = WindowsAPI.ReleaseDC(0, screenDc);
			}
		}

		internal bool TryAcquireSourceDC(out nint source)
		{
			source = 0;
			var acquired = false;

			try
			{
				gdi.DangerousAddRef(ref acquired);
				source = gdi.SourceDC;
				return acquired;
			}
			catch (ObjectDisposedException)
			{
				return false;
			}
		}

		internal void ReleaseSourceDC() => gdi.DangerousRelease();

		// Only the explicit path needs this. If the surface is never disposed the SafeHandle's own critical
		// finalizer frees the same objects, after the GDI+ bitmap over them has already gone.
		protected override void ReleasePlatform() => gdi.Dispose();
	}

	// A per-pixel-alpha layered top-level window (UpdateLayeredWindow) that is click-through and never activates.
	internal sealed class WindowsImageOverlay : IImageOverlayBacking
	{
		private readonly Script owner;
		private LayeredOverlayForm form;
		private int shownW, shownH, shownX, shownY;
		private byte shownOpacity = 255;
		private bool shownClickThrough = true;
		private bool mapped;
		// Only allocated when the canvas size and the window size disagree (a stretched tile, a live resize);
		// dropped again the moment they line up, and on hide, so a full-screen overlay does not keep a second
		// screen-sized buffer alive for a size it no longer has.
		private DibOverlaySurface presentation;
		// The form reads this through the GetPointerSink provider delegate wired at creation, so setting it
		// before or after the form exists (and across TryHide's form teardown/recreation) needs no rewiring.
		private Action<OverlayPointerEvent> pointerSink;

		internal WindowsImageOverlay(Script owner) => this.owner = owner;

		public Action<OverlayPointerEvent> PointerSink { get => pointerSink; set => pointerSink = value; }

		public nint Handle => form?.IsHandleCreated == true ? form.Handle : 0;

		// UpdateLayeredWindow blends by a constant alpha itself, so a faded overlay costs nothing extra here.
		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
			try
			{
				var width = Math.Max(1, bounds.Width <= 0 ? canvas.Size.Width : bounds.Width);
				var height = Math.Max(1, bounds.Height <= 0 ? canvas.Size.Height : bounds.Height);
				// Damage is only meaningful against an unchanged window rectangle: a move or a resize re-lays the
				// whole surface, and the pixels outside a dirty rect would then be whatever the old geometry left
				// there. So geometry changes force a full update, and only a steady rectangle takes the fast path.
				//
				// A changed constant alpha counts as one of those. Whether UpdateLayeredWindowIndirect keeps
				// SourceConstantAlpha as a window property or bakes it into the region it copies is not
				// documented; if it bakes it in, a partial transfer would leave the dirty rect at the new alpha
				// and the rest of the window at the old one. Transferring everything is correct under either
				// reading, and an opacity change is rare enough that the full copy costs nothing worth having.
				var geometryChanged = width != shownW || height != shownH || bounds.X != shownX || bounds.Y != shownY
									  || !mapped || opacity != shownOpacity;

				if (!geometryChanged && damage?.Kind == DamageKind.None && clickThrough == shownClickThrough)
					return true;

				var direct = canvas is DibOverlaySurface
					&& canvas.Size.Width == width && canvas.Size.Height == height;
				var bitmap = direct ? canvas.PrepareForPresent() : canvas.PrepareForRead();

				if (bitmap == null)
					return false;

				var sourceCanvas = Reconcile(canvas, bitmap, width, height, ref damage);

				if (sourceCanvas == null)
					return false;

				var dirty = geometryChanged ? default : DirtyRect(damage, canvas.Size, width, height);

				if (!sourceCanvas.TryAcquireSourceDC(out var source))
					return false;

				var updated = false;

				try
				{
					owner.InvokeOnUIThread(() =>
					{
						EnsureForm();
						// Apply the input mode before showing so the exstyle is right from the first CreateParams
						// evaluation (a live toggle later goes through SetWindowLong instead).
						form.SetClickThrough(clickThrough);
						updated = form.ShowLayered(source, bounds.X, bounds.Y, width, height, opacity, dirty, !mapped);
					});
				}
				finally
				{
					sourceCanvas.ReleaseSourceDC();
				}

				if (!updated)
					return false;

				shownW = width;
				shownH = height;
				shownX = bounds.X;
				shownY = bounds.Y;
				shownOpacity = opacity;
				shownClickThrough = clickThrough;
				mapped = true;
				return true;
			}
			catch
			{
				return false;
			}
		}

		// Produces the DC UpdateLayeredWindow reads from. When the canvas is already the right size for these
		// bounds — the ordinary case — that is the canvas's own DIB and nothing is copied at all. Otherwise the
		// canvas is scaled into a presentation DIB kept for as long as the bounds hold still. The scale always
		// runs from the canvas, never from the previous scaled result, so dragging a resize cannot accumulate
		// resampling error the way stretching a stretched copy would.
		private DibOverlaySurface Reconcile(OverlaySurface canvas, Bitmap bitmap, int width, int height,
										ref DamageList damage)
		{
			if (canvas is DibOverlaySurface dib && canvas.Size.Width == width && canvas.Size.Height == height)
			{
				DropPresentation();
				return dib;
			}

			if (presentation == null || presentation.Size.Width != width || presentation.Size.Height != height)
			{
				DropPresentation();
				presentation = DibOverlaySurface.TryCreate(new PixelSize(width, height));

				if (presentation == null)
					return null;
			}

			using (var g = Graphics.FromImage(presentation.Bitmap))
			{
				g.CompositingMode = CompositingMode.SourceCopy;
				// A same-size copy needs no filter; only a genuine rescale interpolates. Both surfaces are
				// premultiplied, which is the correct space to interpolate alpha in.
				g.InterpolationMode = width == canvas.Size.Width && height == canvas.Size.Height
									  ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.Clear(Color.Transparent);
				g.DrawImage(bitmap, new Rectangle(0, 0, width, height),
							0, 0, canvas.Size.Width, canvas.Size.Height, GraphicsUnit.Pixel);
			}

			damage = null;   // every pixel of the presentation surface is new
			return presentation;
		}

		private void DropPresentation()
		{
			presentation?.Dispose();
			presentation = null;
		}

		// The one rectangle UpdateLayeredWindowIndirect can be limited to. Empty means "transfer everything".
		private static PixelRect DirtyRect(DamageList damage, PixelSize canvasSize, int width, int height)
		{
			if (damage == null || damage.Kind == DamageKind.All)
				return default;

			var union = damage.Union();
			// Damage is in canvas pixels; the surface being transferred is width x height. They are equal on the
			// direct path (the only one that gets here with damage), but clamp rather than trust that.
			return canvasSize.Width == width && canvasSize.Height == height
				   ? union.Intersect(new PixelRect(0, 0, width, height)) : default;
		}

		public bool Move(ScreenRect bounds)
		{
			if (form == null)
				return false;

			// A layered window retains its last UpdateLayeredWindow content across a move, so a same-size move
			// is a pure reposition (no pixels needed; matters for mouse-following highlights). A resize needs
			// new pixels, so return false and let the overlay re-render via Show.
			if (bounds.Width == shownW && bounds.Height == shownH)
			{
				var moved = false;
				owner.InvokeOnUIThread(() =>
					moved = WindowsAPI.SetWindowPos(form.Handle, new nint(WindowsAPI.HWND_TOPMOST), bounds.X, bounds.Y, 0, 0,
											WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_NOSIZE));

				// Record where it actually went. Leaving this stale would make the next present believe the
				// geometry changed and repaint the whole surface for a move that already happened.
				if (moved)
				{
					shownX = bounds.X;
					shownY = bounds.Y;
				}

				return moved;
			}

			return false;
		}

		private void EnsureForm() => form ??= new LayeredOverlayForm { GetPointerSink = () => pointerSink };

		public bool TryHide()
		{
			DropPresentation();
			mapped = false;
			var closed = true;

			// InvokeOnUIThread is synchronous, so `closed`/`form` reflect the outcome once it returns.
			owner.InvokeOnUIThread(() =>
			{
				try
				{
					form?.Close();
					form?.Dispose();
					form = null;   // only reached when Close/Dispose didn't throw
				}
				catch { closed = false; }   // leave `form` set so a later retry can re-close it
			});

			return closed && form == null;
		}

		public void Dispose() => _ = TryHide();
	}

	internal sealed class LayeredOverlayForm : Form
	{
		// Default true: passive HUDs/highlights pass mouse input through. Set false to make the layered window
		// interactive (it then receives clicks). Drives both the WS_EX_TRANSPARENT exstyle and the WM_NCHITTEST
		// handler below, and can be toggled at runtime via SetClickThrough on an already-shown overlay.
		private bool clickThrough = true;

		// Provider for the owning backing's pointer sink (read per event so a sink registered after the form
		// exists is seen without rewiring). Events only arrive while !clickThrough, but the guard below keeps
		// a mid-toggle message from leaking through.
		internal Func<Action<OverlayPointerEvent>> GetPointerSink;

		private void RaisePointer(OverlayPointerKind kind, int x, int y)
		{
			if (clickThrough)
				return;

			GetPointerSink?.Invoke()?.Invoke(new OverlayPointerEvent(kind, x, y));
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);

			if (e.Button == MouseButtons.Left)
				RaisePointer(OverlayPointerKind.Click, e.X, e.Y);
			else if (e.Button == MouseButtons.Right)
				RaisePointer(OverlayPointerKind.ContextMenu, e.X, e.Y);
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);

			if (e.Button == MouseButtons.Left)
				RaisePointer(OverlayPointerKind.DoubleClick, e.X, e.Y);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			RaisePointer(OverlayPointerKind.MouseMove, e.X, e.Y);
		}

		internal LayeredOverlayForm()
		{
			FormBorderStyle = FormBorderStyle.None;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.Manual;
			TopMost = true;
		}

		protected override bool ShowWithoutActivation => true;

		protected override CreateParams CreateParams
		{
			get
			{
				var cp = base.CreateParams;
				cp.Style |= unchecked((int)WindowsAPI.WS_POPUP);
				cp.Style &= ~(WindowsAPI.WS_CAPTION | WindowsAPI.WS_THICKFRAME | WindowsAPI.WS_SYSMENU);
				cp.ExStyle |= WindowsAPI.WS_EX_LAYERED
							  | WindowsAPI.WS_EX_TOPMOST
							  | WindowsAPI.WS_EX_TOOLWINDOW
							  | WindowsAPI.WS_EX_NOACTIVATE;

				// Only add WS_EX_TRANSPARENT for a click-through overlay; an interactive one must be able to receive
				// the mouse. WS_EX_LAYERED stays either way (it is what makes UpdateLayeredWindow's per-pixel alpha work).
				if (clickThrough)
					cp.ExStyle |= WindowsAPI.WS_EX_TRANSPARENT;

				return cp;
			}
		}

		// Toggle the input mode. Before the handle exists the flag is picked up by CreateParams; on a live window
		// WS_EX_TRANSPARENT is flipped in place via SetWindowLong (no re-create needed for click-through).
		internal void SetClickThrough(bool enable)
		{
			clickThrough = enable;

			if (!IsHandleCreated)
				return;

			var ex = WindowsAPI.GetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE).ToInt64();
			var updated = enable ? ex | WindowsAPI.WS_EX_TRANSPARENT : ex & ~(long)WindowsAPI.WS_EX_TRANSPARENT;

			if (updated != ex)
				_ = WindowsAPI.SetWindowLongPtr(Handle, WindowsAPI.GWL_EXSTYLE, new nint(updated));
		}

		/// <summary>
		/// Puts <paramref name="sourceDc"/>'s pixels on the layered surface. <paramref name="sourceDc"/> already
		/// has a premultiplied 32bpp DIB selected into it, so there is nothing to convert or copy here.
		/// </summary>
		/// <param name="dirty">When non-empty, the only part of the surface transferred — the rest of the layered
		/// window keeps what it already has. The caller passes empty whenever the geometry changed, because the
		/// pixels outside a dirty rect would then be whatever the old layout left there.</param>
		/// <remarks>
		/// Position and size are supplied rather than null to mean "unchanged". With a source DC but a null size,
		/// the call succeeds without painting. Moving or resizing separately is also wrong: one
		/// UpdateLayeredWindow does geometry and pixels atomically, and splitting them shows the window at its
		/// new position with the previous surface for a frame.
		/// </remarks>
		internal unsafe bool ShowLayered(nint sourceDc, int x, int y, int width, int height, byte opacity,
										 PixelRect dirty, bool mapping)
		{
			if (sourceDc == 0)
				return false;

			if (!IsHandleCreated)
				_ = Handle;

			var topPos = new POINT(x, y);
			var size = new SIZE(width, height);
			var src = new POINT(0, 0);
			var blend = new BLENDFUNCTION
			{
				BlendOp = WindowsAPI.AC_SRC_OVER,
				BlendFlags = 0,
				SourceConstantAlpha = opacity,
				AlphaFormat = WindowsAPI.AC_SRC_ALPHA
			};
			var dirtyRect = new RECT { Left = dirty.X, Top = dirty.Y, Right = dirty.Right, Bottom = dirty.Bottom };
			var info = new UPDATELAYEREDWINDOWINFO
			{
				cbSize = (uint)sizeof(UPDATELAYEREDWINDOWINFO),
				hdcDst = 0,
				pptDst = &topPos,
				psize = &size,
				hdcSrc = sourceDc,
				pptSrc = &src,
				crKey = 0,
				pblend = &blend,
				dwFlags = WindowsAPI.ULW_ALPHA,
				prcDirty = dirty.IsEmpty ? null : &dirtyRect,
			};

			if (!WindowsAPI.UpdateLayeredWindowIndirect(Handle, &info))
				return false;

			// Z-order upkeep only on the frame that maps the window. Doing it every frame costs two window
			// messages per frame for a topmost state that has not changed, and SWP_NOMOVE|SWP_NOSIZE keeps it
			// from reintroducing the half-step described above.
			if (mapping)
			{
				_ = WindowsAPI.SetWindowPos(Handle, new nint(WindowsAPI.HWND_TOPMOST), 0, 0, 0, 0,
											WindowsAPI.SWP_NOACTIVATE | WindowsAPI.SWP_NOMOVE | WindowsAPI.SWP_NOSIZE);
				_ = WindowsAPI.ShowWindow(Handle, WindowsAPI.SW_SHOWNOACTIVATE);
			}

			return true;
		}

		protected override void WndProc(ref Message m)
		{
			// Report every point as transparent so the mouse falls through to the window beneath, but only while
			// click-through. An interactive overlay defers to the base hit-test so it can receive the mouse.
			if (clickThrough && m.Msg == WindowsAPI.WM_NCHITTEST)
			{
				m.Result = new nint(WindowsAPI.HTTRANSPARENT);
				return;
			}

			base.WndProc(ref m);
		}

	}
}
#endif
