using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A lightweight, caller-owned screen-border overlay for debugging and visualization: outline any screen
		/// rectangle with a colored, click-through border. Construct one, then drive it with
		/// <c>Show</c>/<c>Move</c>/<c>Hide</c>/<c>Destroy</c>. The caller owns the object (like a ToolTip slot —
		/// there is no global registry); calling <c>Destroy</c>, or dropping all references (the overlay is then
		/// torn down on garbage collection via <c>__Delete</c>), frees it.
		///
		/// <para>Internally it is a single <see cref="KeysharpOverlay"/> — the one cross-platform, click-through,
		/// always-on-top overlay primitive — whose canvas holds a d-thick frame (a transparent centre so it frames,
		/// rather than covers, the target). The overlay is created on the first <c>Show</c> and then reused: a move
		/// repositions it, a recolour redraws the frame in place, and only a change of size, thickness or display
		/// density builds a new canvas.</para>
		///
		/// <para>Coordinates are absolute screen pixels and the border is drawn just outside the rectangle, matching
		/// the screen-pixel coordinates that callers such as OCR/Ax/AtSpi produce.</para>
		///
		/// <code>
		/// hl := Highlight(100, 100, 200, 50)   ; build (not shown yet)
		/// hl.Show()                            ; outline (100,100,200,50), non-blocking
		/// hl.Move(140, 160)                    ; reposition without repainting the frame
		/// hl.Show(300, 300, 120, 120)          ; resize in place
		/// hl.Color := "Lime"                   ; recolor in place
		/// hl.Hide()                            ; keep the overlay for the next Show
		/// hl.Destroy()                         ; free it
		/// </code>
		/// </summary>
		[UserDeclaredName("Highlight")]
		public class KeysharpHighlight : KeysharpObject
		{
			// The single reusable overlay (null until the first Show, and after Destroy).
			private KeysharpOverlay overlay;

			// The requested target rectangle (screen pixels) and border style; `color` is 0xRRGGBB.
			private int rx, ry, rw, rh, thickness = 2;
			private int color = 0xFF0000;

			// The frame on the overlay's canvas, so Refresh can tell a pure move (reposition), a recolour (redraw
			// the edges in place) and a change of size, thickness or canvas density (a new canvas) apart.
			private int builtW = int.MinValue, builtH = int.MinValue, builtD = int.MinValue, builtColor = -1;
			private PixelSize builtPixels;

			// visible = caller intent (Show issued, no intervening Hide/Destroy); shown = overlay actually mapped.
			private bool visible, shown;

			public KeysharpHighlight(params object[] args) : base(args) { }

			/// <summary>Highlight(X?, Y?, Width?, Height?, Color := "Red", Thickness := 2) — stores the rectangle/style; no
			/// overlay is created until the first Show.</summary>
			// `new`, not `override`: construction dispatches by name, so the real signature is declared here and
			// arity/defaults/named binding follow from it (see Buffer.__New and Any's constructor). The parameters
			// are PascalCase on purpose: these names are script-facing API (`Highlight(Color: "Blue")`).
			public object __New(object x = null, object y = null, object width = null, object height = null,
									object color = null, object thickness = null)
			{
				if (!SetRect(x, y, width, height) || color != null && !TrySetColor(color))
					return DefaultObject;

				if (thickness.CoerceInt(out var t, this.thickness)) this.thickness = Math.Max(0, t);

				return DefaultObject;
			}

			#region Properties

			/// <summary>Left edge of the outlined rectangle, in screen pixels. Updates live while shown.</summary>
			public object X { get => (long)rx; set { if (value.CoerceInt(out rx, rx)) Refresh(); } }

			/// <summary>Top edge of the outlined rectangle, in screen pixels. Updates live while shown.</summary>
			public object Y { get => (long)ry; set { if (value.CoerceInt(out ry, ry)) Refresh(); } }

			/// <summary>Width of the outlined rectangle, in pixels. Updates live while shown.</summary>
			public object Width { get => (long)rw; set { if (value.CoerceInt(out rw, rw)) Refresh(); } }

			/// <summary>Height of the outlined rectangle, in pixels. Updates live while shown.</summary>
			public object Height { get => (long)rh; set { if (value.CoerceInt(out rh, rh)) Refresh(); } }

			/// <summary>Border color: set with a color name ("Red"), a 0xRRGGBB integer, or a hex string; it reads back
			/// as a 6-hex-digit string (e.g. "FF0000"). An invalid color raises a ValueError and keeps the previous
			/// one. Updates live while shown.</summary>
			public object Color { get => color.ToString("X6"); set { if (TrySetColor(value)) Refresh(); } }

			/// <summary>Border thickness in pixels (0 hides the border). Updates live while shown.</summary>
			public object Thickness { get => (long)thickness; set { if (value.CoerceInt(out var t, thickness)) { thickness = Math.Max(0, t); Refresh(); } } }

			/// <summary>Whether the overlay is currently on screen.</summary>
			public object IsVisible => shown;

			/// <summary>Native handle of the overlay window where the backing has one (Eto/WinForms/layer surface),
			/// otherwise 0 (a compositor-drawn overlay has no client-side window). The handle survives Hide and Show
			/// except on Wayland layer-shell, which rebuilds its surface.</summary>
			public object Hwnd => overlay?.Hwnd ?? (object)0L;

			#endregion

			#region Methods

			/// <summary>Shows the overlay (creating it on first use), returning immediately. Optional X/Y/Width/Height set the
			/// rectangle first, so Show doubles as move/resize.</summary>
			public object Show(object x = null, object y = null, object width = null, object height = null)
			{
				if (!SetRect(x, y, width, height))
					return DefaultObject;

				visible = true;
				Refresh();
				return this;
			}

			/// <summary>Repositions/resizes the overlay in place — and only that. Unlike <c>Show</c>, <c>Move</c>
			/// never shows or hides it and never changes visibility: on a hidden overlay it just records the new
			/// geometry for the next <c>Show</c>. All args optional, so <c>Move(, , Width, Height)</c> resizes only.</summary>
			public object Move(object x = null, object y = null, object width = null, object height = null)
			{
				if (!SetRect(x, y, width, height))
					return DefaultObject;

				if (shown)
					Refresh();

				return this;
			}

			/// <summary>Takes the border off the screen and keeps its window and frame for the next Show.</summary>
			public object Hide()
			{
				visible = false;
				shown = false;
				_ = overlay?.Hide();
				return this;
			}

			/// <summary>Destroys the overlay and frees its resources. Idempotent; the object can be reused
			/// (a later Show rebuilds it).</summary>
			public object Destroy()
			{
				visible = false;
				shown = false;
				_ = overlay?.Destroy();
				overlay = null;
				builtW = builtH = builtD = int.MinValue;
				builtPixels = default;
				builtColor = -1;
				return DefaultObject;
			}

			// __Delete is invoked by the destructor pump on the main thread when this object is collected, so a
			// caller that just drops the overlay still gets it freed (no explicit Destroy required).
			public override object __Delete() => Destroy();

			#endregion

			// Each field is its own default, so an omitted value, or one whose error the script continues, leaves the
			// field as it was.
			private bool SetRect(object x, object y, object width, object height) =>
				x.CoerceInt(out rx, rx) && y.CoerceInt(out ry, ry) && width.CoerceInt(out rw, rw) && height.CoerceInt(out rh, rh);

			// A color name, a hex string or a 0xRRGGBB number. Anything else is an error, and the color stays.
			private bool TrySetColor(object value)
			{
				if (value is long or int or double)
				{
					_ = value.TryCoerceLong(out var rgb);
					color = (int)(rgb & 0xFFFFFF);
					return true;
				}

				if (!value.CoerceString(out var s))
					return false;

				if (!Conversions.TryParseColor(s, out var parsed))
				{
					_ = Errors.ValueErrorOccurred("Invalid value.", s);
					return false;
				}

				color = parsed.ToArgb() & 0xFFFFFF;
				return true;
			}

			// Applies the current rectangle and style. A no-op when hidden; hides when there is nothing to draw.
			private void Refresh()
			{
				if (!visible)
					return;

				if (rw < 1 || rh < 1 || thickness == 0)
				{
					shown = false;
					_ = overlay?.Hide();
					return;
				}

				var target = new ScreenRect(rx, ry, rw, rh);
				_ = DisplayTopology.TryFind(Platform.Screen.GetDisplays(), target, out var display);
				var d = Math.Max(1, (int)Math.Round(thickness * ScaleFactor.Normalize(display.SizeScale)));
				int bw = rw + 2 * d, bh = rh + 2 * d, bx = rx - d, by = ry - d;
				// A same-size frame moved to a display of another density needs a raster of its own too.
				var pixels = Platform.Overlay.GetCanvasSize(new ScreenRect(bx, by, bw, bh));

				overlay ??= new KeysharpOverlay();

				if (bw != builtW || bh != builtH || d != builtD || pixels != builtPixels)
				{
					// A fresh canvas is already transparent, so the frame is just its four edges.
					if (!overlay.Redraw(canvas => DrawFrame(canvas, bw, bh, d), bx, by, bw, bh))
						return;

					builtW = bw;
					builtH = bh;
					builtD = d;
					builtPixels = pixels;
					builtColor = color;

					if (overlay.IsVisible is not true)
						_ = overlay.Show();
				}
				else if (color != builtColor)
				{
					var canvas = overlay.Canvas;
					_ = canvas.Clear();
					DrawFrame(canvas, bw, bh, d);
					builtColor = color;
					_ = overlay.Show(bx, by, bw, bh);
				}
				else if (overlay.IsVisible is not true)
					_ = overlay.Show(bx, by, bw, bh);
				else
					_ = overlay.Move(bx, by, bw, bh);

				shown = overlay.IsVisible is true;
			}

			// Paints a d-thick frame around a (bw x bh) canvas as four filled edges, leaving the centre transparent so
			// the highlight frames the target instead of covering it.
			private void DrawFrame(KeysharpImage canvas, int bw, int bh, int d)
			{
				long c = color;
				var inner = Math.Max(0, bh - 2 * d);
				_ = canvas.FillRect(0L, 0L, (long)bw, (long)d, c);
				_ = canvas.FillRect(0L, (long)(bh - d), (long)bw, (long)d, c);
				_ = canvas.FillRect(0L, (long)d, (long)d, (long)inner, c);
				_ = canvas.FillRect((long)(bw - d), (long)d, (long)d, (long)inner, c);
			}
		}
	}
}
