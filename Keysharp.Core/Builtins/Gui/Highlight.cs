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
		/// always-on-top overlay primitive — whose canvas is painted with a d-thick frame (a transparent centre so
		/// it frames, rather than covers, the target). The overlay is created lazily on the first <c>Show</c> and
		/// then REUSED: moves reposition it in place; only a size/thickness/colour change repaints the frame.</para>
		///
		/// <para>Coordinates are absolute screen pixels and the border is drawn just OUTSIDE the rectangle, matching
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

			// The requested target rectangle (screen pixels) and border style. `color` is stored normalized to a
			// 6-hex-digit string (like Gui.BackColor): a name/number set on it reads back as e.g. "FF0000".
			private int rx, ry, rw, rh, thickness = 2;
			private string color = "FF0000";

			// Signature of the frame currently painted onto the overlay, so Refresh can tell a pure move (just
			// reposition) from a resize/recolor (repaint the frame first) without rebuilding when nothing changed.
			private int builtW = int.MinValue, builtH = int.MinValue, builtD = int.MinValue;
			private int builtPixelW = int.MinValue, builtPixelH = int.MinValue;
			private string builtColor = "";

			// visible = caller intent (Show issued, no intervening Hide/Destroy); shown = overlay actually mapped.
			private bool visible, shown;

			public KeysharpHighlight(params object[] args) : base(args) { }

			/// <summary>Highlight(X?, Y?, Width?, Height?, Color := "Red", Thickness := 2) — stores the rectangle/style; no
			/// overlay is created until the first Show.</summary>
			// `new`, not `override`: construction dispatches by name, so the real signature is declared here and
			// arity/defaults/named binding follow from it (see Buffer.__New and Any's constructor). The parameters
			// are PascalCase on purpose: these names ARE script-facing API (`Highlight(Color: "Blue")`).
			public object __New(object X = null, object Y = null, object Width = null, object Height = null,
									object Color = null, object Thickness = null)
			{
				if (X != null) rx = X.Ai();
				if (Y != null) ry = Y.Ai();
				if (Width != null) rw = Width.Ai();
				if (Height != null) rh = Height.Ai();
				if (Color != null) color = NormalizeColor(Color);
				if (Thickness != null) thickness = Math.Max(0, Thickness.Ai());

				return DefaultObject;
			}

			#region Properties

			/// <summary>Left edge of the outlined rectangle, in screen pixels. Updates live while shown.</summary>
			public object X { get => (long)rx; set { rx = value.Ai(); Refresh(); } }

			/// <summary>Top edge of the outlined rectangle, in screen pixels. Updates live while shown.</summary>
			public object Y { get => (long)ry; set { ry = value.Ai(); Refresh(); } }

			/// <summary>Width of the outlined rectangle, in pixels. Updates live while shown.</summary>
			public object Width { get => (long)rw; set { rw = value.Ai(); Refresh(); } }

			/// <summary>Height of the outlined rectangle, in pixels. Updates live while shown.</summary>
			public object Height { get => (long)rh; set { rh = value.Ai(); Refresh(); } }

			/// <summary>Border color: set with a color name ("Red"), a 0xRRGGBB integer, or a hex string; it is
			/// normalized to and read back as a 6-hex-digit string (e.g. "FF0000"). Updates live while shown.</summary>
			public object Color { get => color; set { color = NormalizeColor(value); Refresh(); } }

			/// <summary>Border thickness in pixels (0 hides the border). Updates live while shown.</summary>
			public object Thickness { get => (long)thickness; set { thickness = Math.Max(0, value.Ai()); Refresh(); } }

			/// <summary>Whether the overlay is currently on screen.</summary>
			public object Visible => shown;

			/// <summary>Native handle of the overlay window where the backing has one (Eto/WinForms/layer surface),
			/// otherwise 0 (a compositor-drawn overlay has no client-side window).</summary>
			public object Hwnd => overlay?.Hwnd ?? (object)0L;

			#endregion

			#region Methods

			/// <summary>Shows the overlay (creating it on first use), returning immediately. Optional X/Y/Width/Height set the
			/// rectangle first, so Show doubles as move/resize.</summary>
			public object Show(object x = null, object y = null, object width = null, object height = null)
			{
				if (x != null) rx = x.Ai();
				if (y != null) ry = y.Ai();
				if (width != null) rw = width.Ai();
				if (height != null) rh = height.Ai();
				visible = true;
				Refresh();
				return this;
			}

			/// <summary>Repositions/resizes the overlay in place — and only that. Unlike <c>Show</c>, <c>Move</c>
			/// never shows or hides it and never changes visibility: on a hidden overlay it just records the new
			/// geometry for the next <c>Show</c>. All args optional, so <c>Move(, , Width, Height)</c> resizes only.</summary>
			public object Move(object x = null, object y = null, object width = null, object height = null)
			{
				if (x != null) rx = x.Ai();
				if (y != null) ry = y.Ai();
				if (width != null) rw = width.Ai();
				if (height != null) rh = height.Ai();

				if (shown)
					Refresh();

				return this;
			}

			/// <summary>Hides the overlay but keeps it alive for the next Show.</summary>
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
				builtPixelW = builtPixelH = int.MinValue;
				builtColor = "";
				return DefaultObject;
			}

			// __Delete is invoked by the destructor pump on the main thread when this object is collected, so a
			// caller that just drops the overlay still gets it freed (no explicit Destroy required).
			public override object __Delete() => Destroy();

			#endregion

			// Applies the current rectangle/style. Repaints the frame only on a size/thickness/colour change;
			// otherwise a pure move just repositions the overlay. A no-op when hidden; hides when the rect is empty.
			private void Refresh()
			{
				if (!visible)
					return;

				if (rw < 1 || rh < 1)
				{
					shown = false;
					_ = overlay?.Hide();
					return;
				}

				var target = new ScreenRect(rx, ry, rw, rh);
				_ = DisplayTopology.TryFind(Platform.Screen.GetDisplays(), target, out var display);
				var scale = ScaleFactor.Normalize(display.SizeScale);
				var d = Math.Max(1, (int)Math.Round(thickness * scale));
				int bw = rw + 2 * d, bh = rh + 2 * d, bx = rx - d, by = ry - d;
				var pixels = Platform.Overlay.GetCanvasSize(new ScreenRect(bx, by, bw, bh));

				overlay ??= new KeysharpOverlay();

				if (bw != builtW || bh != builtH || d != builtD
						|| pixels.Width != builtPixelW || pixels.Height != builtPixelH || color != builtColor)
				{
					using var frame = BuildFrame(bw, bh, d, color, pixels);

					if (frame == null)
						return;

					_ = overlay.SetImage(frame, bx, by, bw, bh);

					builtW = bw;
					builtH = bh;
					builtD = d;
					builtPixelW = pixels.Width;
					builtPixelH = pixels.Height;
					builtColor = color;

					if (overlay.Visible is not true)
						_ = overlay.Show();
				}
				else if (!shown)
					_ = overlay.Show(bx, by, bw, bh);
				else
					_ = overlay.Move(bx, by, bw, bh);   // pure move: reposition without repainting

				shown = true;
			}

			// Paints a d-thick frame of `colorHex` around a (bw x bh) transparent canvas as four filled edges,
			// leaving the centre transparent so the highlight frames the target instead of covering it.
			private static KeysharpImage BuildFrame(int bw, int bh, int d, string colorHex, PixelSize pixels)
			{
				if (!pixels.HasArea || KeysharpImage.Create(null, (long)pixels.Width, (long)pixels.Height) is not KeysharpImage img)
					return null;

				var sx = bw > 0 ? (double)pixels.Width / bw : 1.0;
				var sy = bh > 0 ? (double)pixels.Height / bh : 1.0;
				img.drawScaleX = ScaleFactor.Normalize(sx);
				img.drawScaleY = ScaleFactor.Normalize(sy);

				if (d > 0)
				{
					long c;

					try { c = Convert.ToInt64(colorHex, 16); }
					catch { c = 0xFF0000; }

					var inner = Math.Max(0, bh - 2 * d);
					_ = img.FillRect(0L, 0L, (long)bw, (long)d, c);
					_ = img.FillRect(0L, (long)(bh - d), (long)bw, (long)d, c);
					_ = img.FillRect(0L, (long)d, (long)d, (long)inner, c);
					_ = img.FillRect((long)(bw - d), (long)d, (long)d, (long)inner, c);
				}

				return img;
			}

			// Normalizes a color (a name like "Red", a 0xRRGGBB integer, or a "#RRGGBB"/"0xRRGGBB"/bare-hex string)
			// to the canonical 6-hex-digit string, matching how Gui.BackColor reports colors. Unparseable -> red.
			private static string NormalizeColor(object color)
			{
				if (color is string s && Conversions.TryParseColor(s, out var c))
					return (c.ToArgb() & 0xFFFFFF).ToString("X6");

				if (color is long || color is int || color is double)
					return (color.Al() & 0xFFFFFF).ToString("X6");

				return "FF0000";
			}
		}
	}
}
