// The nested Ks.Font class shadows the ambient toolkit Font type everywhere under Ks, and the text
// rendering below wants the toolkit's.
#if WINDOWS
using System.Runtime.Intrinsics;
using NativeFont = System.Drawing.Font;
#else
using NativeFont = Eto.Drawing.Font;
#endif
using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A cross-platform image: capture it from the screen/a window/a file, queue transforms
		/// (scale, rotate, flip, crop) that are applied lazily, then output it (save to a file or hand
		/// out a native bitmap handle), search it for a sub-image, or read/write individual pixels.
		///
		/// Construct one through a <c>From*</c> factory, e.g. <c>img := Image.FromFile("logo.png")</c>
		/// or <c>Image.FromDesktop().Scale(0.5).Save("thumb.png")</c>. Transform methods accumulate and
		/// return the same instance, so they chain.
		///
		/// The C# type is named <c>KeysharpImage</c> to avoid colliding with the backend drawing
		/// <c>Image</c> type (System.Drawing on Windows, Eto.Drawing elsewhere); scripts see it as
		/// <c>Image</c> via <see cref="UserDeclaredNameAttribute"/>.
		///
		/// An image belongs to one thread, like a Gui. Nothing here is synchronised: the pending-op list, the
		/// materialised bitmap and the reused Graphics/brush/pen caches are all plain fields and plain
		/// dictionaries. Script pseudo-threads share an OS thread and are therefore safe by construction, but a
		/// <c>RealThread</c> is a real one — two of them drawing on the same image can corrupt a Dictionary into
		/// an endless loop, which presents as a hang rather than an error. Hand an image between real threads by
		/// finishing with it first, or give each its own.
		/// </summary>
		[UserDeclaredName("Image"), Experimental]
		public partial class KeysharpImage : KeysharpObject, IDisposable
		{
			// The current pixels. A lazy image folds pending work into this bitmap; a borrowed overlay canvas keeps
			// the platform bitmap identity fixed.
			private Bitmap baseBitmap;

			// Queued operations, applied in order on the next Materialize(). Each mutates the bitmap it is given and
			// returns it, or returns a replacement. Only a lazy image queues, and a lazy image owns its base.
			private readonly List<Func<Bitmap, Bitmap>> pending = new ();
			private readonly List<IDisposable> pendingResources = new ();

			// Image pixels per native screen unit at capture time (1.0 for files and one-pixel-per-unit captures).
			// All Image coordinates (Width/Height, Get/SetPixel, Search) are in the image's own pixels,
			// which on a Retina/HiDPI screen or window capture are physical pixels. Scripts that need to
			// map back to native screen coordinates do so explicitly via the ScaleX/ScaleY properties;
			// the class never silently rescales.
			private double scaleX = 1.0, scaleY = 1.0;

			// Screen-absolute position of this image's top-left at capture time (0,0 for file/bitmap images
			// that have no on-screen origin). Lets a consumer such as OCR map coordinates measured inside the
			// image back to screen coordinates without the caller having to pass the capture rectangle again.
			private int originX, originY;

			// Whether the origin/scale metadata is still meaningful. Rotate invalidates both and Flip the origin
			// (an arbitrary rotation/mirror has no well-defined screen mapping); the script-visible properties
			// then read "" so misuse fails visibly instead of silently landing highlights in the wrong place.
			// SetOrigin re-validates explicitly.
			private bool originValid = true, scaleValid = true;

			// Multiplier applied to every draw op's coordinates and font sizes (1.0 = draw in physical pixels).
			// A target-density Overlay sets this so the caller can draw in local screen units while the
			// canvas has an independently chosen pixel size. A matching transform keeps the result
			// crisp instead of upscaling a small bitmap. Never touched for a normal Image (stays 1.0).
			internal double drawScaleX = 1.0, drawScaleY = 1.0;

			private bool disposed;

			// Bytes of GC memory pressure currently registered for this image's bitmaps (see SyncGcPressure).
			private long gcPressure;

			// False when baseBitmap's pixels belong to someone else — an Overlay canvas, whose memory is the
			// platform backing's presentable buffer (a DIB section on Windows). Dispose then leaves the bitmap
			// alone: the OverlaySurface frees it, in an order the GDI object and its DIB both survive.
			internal bool ownsBitmap = true;

			// The canvas region changed since the last present, or null when nothing presents this image.
			// Owned by the OverlaySurface that lent us its pixels; a plain Image never has one.
			//
			// Presentation damage is independent of whether drawing is eager.
			private DamageList damage;
			// Content survives Present; damage only survives until the next successful Present.
			private DamageList drawn;
			private int? clearColor;

			// Keeps a borrowed canvas's owner reachable for as long as a script retains the canvas view.
			// Destroy still disposes the surface and invalidates the view immediately.
			private OverlaySurface borrowedSurface;

			public KeysharpImage(params object[] args) : base(args) { }

			/// <summary>
			/// The current pixels as a <c>Ks.Clr</c> object over the underlying bitmap, with pending work
			/// materialized first. The reference is to the live surface, so it goes stale once this image is
			/// next transformed or disposed; changes made through it bypass this class's queued-work model.
			/// </summary>
			public object ToClr()
			{
				ThrowIfDisposed();
				drawn = null; // External bitmap writes cannot be bounded by our drawing operations.
				return ManagedInvoke.WrapManaged(PrepareForRead());
			}

			#region Reusable draw state

			// Windows keeps a Graphics alive across draw ops on a stable surface. Eto drawing contexts cannot be
			// retained because reading a GTK bitmap can replace the Cairo surface beneath them.
			//
			// Per-operation GDI+ setup dominates simple draws, so stable bitmaps reuse their Graphics, brushes and
			// pens. Every lease resets the accumulated transform before applying drawScale.
			private Graphics liveGraphics;
			private Bitmap liveGraphicsFor;
#if WINDOWS
			private bool liveHighQuality;
#endif
			private Dictionary<int, SolidBrush> brushCache;
			private Dictionary<(int argb, float width), Pen> penCache;

			/// <summary>
			/// A Graphics for one draw operation. Cached contexts are borrowed; transient contexts are owned.
			/// </summary>
			private readonly struct GraphicsLease : IDisposable
			{
				private readonly Graphics graphics;
				private readonly bool owned;

				internal GraphicsLease(Graphics graphics, bool owned)
				{
					this.graphics = graphics;
					this.owned = owned;
				}

				internal Graphics Graphics => graphics;

				public void Dispose()
				{
					ImageHelper.PopDrawTransform(graphics);

					if (owned)
						graphics.Dispose();
				}
			}

			// Graphics for a user-facing draw op, honoring the canvas-to-draw-unit scales. Shapes/transforms that operate on whole
			// bitmaps (Create, Clear, Scale/Rotate/Flip) use ImageHelper.MakeGraphics directly and are unscaled.
			private GraphicsLease DrawG(Bitmap b, VectorDrawingState state, bool highQuality = true)
			{
#if WINDOWS
				// Every draw targets the base, so its Graphics is kept until the base is replaced.
				if (liveGraphics == null || !ReferenceEquals(liveGraphicsFor, b))
				{
					ReleaseLiveGraphics();
					liveGraphics = ImageHelper.MakeGraphics(b, highQuality);
					liveGraphicsFor = b;
					liveHighQuality = highQuality;
				}
				else if (liveHighQuality != highQuality)
				{
					ImageHelper.ConfigureGraphics(liveGraphics, highQuality);
					liveHighQuality = highQuality;
				}

				var g = liveGraphics;
				var owned = false;
#else
				var g = ImageHelper.MakeGraphics(b, highQuality);
				var owned = true;
#endif

				// Every operation reapplies its captured transform and clip state.
				ImageHelper.PushDrawTransform(g);
				var lease = new GraphicsLease(g, owned);

				try
				{
					ApplyDrawingState(g, state);
					return lease;
				}
				catch
				{
					lease.Dispose();

					if (!owned)
						ReleaseLiveGraphics();

					throw;
				}
			}

			/// <summary>A brush for <paramref name="argb"/>, created once per colour and reused. Never disposed by
			/// a caller — the image owns it.</summary>
			// Hand-authored graphics use a fixed palette — a whole HUD is a few dozen colours — so this cap is
			// never approached by the case it exists to serve. It is here for the other case: a script painting
			// colours it read from the screen (FindText's capture grid fills ~1800 cells from live pixels into
			// one long-lived image), where the key space is 2^24 and nothing would ever evict.
			//
			// Clearing wholesale rather than evicting one entry is deliberate. A brush costs ~0.5 us to rebuild,
			// so the amortised cost is nil, and it keeps the rule "a cached brush is only ever handed out inside
			// the draw op that asked for it" — nothing survives the call, so nothing can be disposed while in use.
			private const int MaxCachedDrawColors = 512;

			private SolidBrush Brush(int argb)
			{
				brushCache ??= [];

				if (!brushCache.TryGetValue(argb, out var brush))
				{
					if (brushCache.Count >= MaxCachedDrawColors)
					{
						foreach (var b in brushCache.Values)
						{
							try { b.Dispose(); }
							catch { }
						}

						brushCache.Clear();
					}

					brushCache[argb] = brush = new SolidBrush(ImageHelper.ArgbToColor(argb));
				}

				return brush;
			}

			/// <summary>A pen for one colour and width, created once and reused. Never disposed by a caller.</summary>
			private Pen GetPen(int argb, float width)
			{
				penCache ??= [];

				if (!penCache.TryGetValue((argb, width), out var pen))
				{
					if (penCache.Count >= MaxCachedDrawColors)
					{
						foreach (var p in penCache.Values)
						{
							try { p.Dispose(); }
							catch { }
						}

						penCache.Clear();
					}

					penCache[(argb, width)] = pen = new Pen(ImageHelper.ArgbToColor(argb), width);
				}

				return pen;
			}

			/// <summary>
			/// Materializes pending work, then releases the drawing context so the returned pixels can be read,
			/// copied or retained safely by another component.
			/// </summary>
			internal Bitmap PrepareForRead()
			{
				var bitmap = Materialize();
				ReleaseLiveGraphics();
				return bitmap;
			}

			// Materializes pending work for pixel access that ends before the next draw: a present, or a pixel read or
			// write inside this class. Flushing is enough, so the retained Graphics survives; Eto retains none.
			internal Bitmap PrepareForPixelAccess()
			{
				var bitmap = Materialize();
#if WINDOWS
				liveGraphics?.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
#endif
				return bitmap;
			}

			private void ReleaseLiveGraphics()
			{
				try { liveGraphics?.Dispose(); }
				catch { }

				liveGraphics = null;
				liveGraphicsFor = null;
			}

			// Brushes and pens are colour-keyed and outlive any one bitmap, so only the Graphics is tied to the
			// surface. Called whenever baseBitmap is replaced or the image is disposed.
			private void ReleaseDrawState()
			{
				ReleaseLiveGraphics();
				drawingClip = null;

				if (brushCache != null)
				{
					foreach (var b in brushCache.Values)
					{
						try { b.Dispose(); }
						catch { }
					}

					brushCache = null;
				}

				if (penCache != null)
				{
					foreach (var p in penCache.Values)
					{
						try { p.Dispose(); }
						catch { }
					}

					penCache = null;
				}
			}

			#endregion

			#region Damage tracking (Overlay canvases only)

			// Records the canvas pixels a draw op can touch. `rect` is in draw units, so it goes through
			// drawScale to reach canvas pixels; `pad` is extra canvas pixels for antialiasing and pen overhang.
			// Over-reporting only costs a slightly larger partial present; under-reporting leaves a stale pixel
			// on screen, so every pad here rounds up and every bound rounds outward.
			private void Damage(RectangleF rect, double pad = 1.0)
			{
				if (damage == null)
					return;

				var b = baseBitmap;

				if (b == null)
					return;

				double left = rect.Left * drawScaleX, top = rect.Top * drawScaleY;
				double right = rect.Right * drawScaleX, bottom = rect.Bottom * drawScaleY;

				// A script can hand in a coordinate that is NaN or infinite. Those describe no rectangle, and
				// rounding them lands on an empty one — which reads as "nothing changed" and would leave
				// whatever got drawn stranded on screen forever. Damage may only ever err towards repainting
				// too much, so an unusable bound means the whole surface.
				if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom))
				{
					DamageAll();
					return;
				}

				Damage(PixelRect.FromBounds(left, top, right, bottom, pad, new PixelSize(b.Width, b.Height)));
			}

			private void Damage(PixelRect rect)
			{
				damage?.Add(rect);
				drawn?.Add(rect);
			}

			internal void DamageAll()
			{
				if (damage == null)
					return;

				damage.AddAll();
				drawn?.AddAll();
			}

			#endregion

			/// <summary>
			/// <c>Image(source)</c> builds an image from a file path, another Image, or a native bitmap
			/// handle. <c>Image()</c> with no argument creates an empty image (used internally by the
			/// <c>From*</c> factories).
			/// </summary>
			//`new`, not `override`: construction dispatches by name, so the real signature can be declared here
			//and MinParams/MaxParams/named binding follow from it (see Buffer.__New and Any's constructor).
			public object __New(object source = null)
			{
				if (source != null)
				{
					// A script can call __New on the Overlay.Canvas it holds. Re-initialising that borrowed canvas would
					// point it at a bitmap the backing never presents and never frees, since ownsBitmap is false.
					if (!ownsBitmap)
						return Errors.ValueErrorOccurred(
							"An Overlay canvas cannot be re-initialised; use Overlay.SetImage to replace its content.");

					var (bmp, sx, sy) = LoadFromSource(source);

					if (bmp == null)
						return Errors.ValueErrorOccurred($"Could not create an image from {source}.");

					baseBitmap = bmp;
					scaleX = sx;
					scaleY = sy;
					SyncGcPressure();
				}

				return DefaultObject;
			}

			#region Capture / load factories

			/// <summary>Captures the whole virtual desktop (the union of all monitors).</summary>
			[Static] public static object FromDesktop(object @this)
			{
				var desktop = Platform.Screen.GetVirtualScreenBounds();
				return CaptureRect(desktop.X, desktop.Y, desktop.Width, desktop.Height, "Capturing the desktop failed.");
			}

			/// <summary>Captures a single monitor (the primary monitor if <paramref name="monitorNumber"/> is omitted).</summary>
			[Static] public static object FromMonitor(object @this, object monitorNumber = null)
			{
				var (left, top, width, height) = Monitor.GetMonitorBounds(monitorNumber);
				return CaptureRect((int)left, (int)top, (int)width, (int)height, "Capturing the monitor failed.");
			}

			/// <summary>
			/// Captures a rectangle of the screen. Coordinates are always absolute screen coordinates;
			/// unlike the screen-pixel functions (<c>PixelGetColor</c>, <c>ImageSearch</c>), this factory
			/// deliberately ignores the Pixel CoordMode so it matches its sibling capture factories
			/// (<c>FromDesktop</c>, <c>FromMonitor</c>, <c>FromWindow</c>), which are all absolute.
			/// </summary>
			[Static] public static object FromRect(object @this, object x, object y, object width, object height)
			{
				if (!x.CoerceInt(out var ix) || !y.CoerceInt(out var iy) || !width.CoerceInt(out var iw) || !height.CoerceInt(out var ih))
					return DefaultObject;

				return CaptureRect(ix, iy, iw, ih, "Capturing the screen rectangle failed.");
			}

			/// <summary>
			/// Captures the whole window (title bar and borders included) matched by the usual WinTitle
			/// criteria. Uses a true window-server capture where supported (Windows, macOS) so it works
			/// even when the window is occluded. On Linux, KWin/GNOME/Cinnamon use compositor window
			/// capture where available (occlusion-independent); Cinnamon falls back to grabbing the
			/// compositor-reported on-screen window rectangle when the extension can't capture (older
			/// installed extension, minimized window), in which case the window must be unobscured.
			///
			/// <para><paramref name="options"/> selects the Windows capture technique, either as a string or
			/// an object with a <c>Mode</c> property: <c>BitBlt</c>, <c>BitBltOpaque</c>, <c>PrintWindow</c>,
			/// <c>PrintWindowOpaque</c>, or <c>FullContent</c> (default). The Opaque modes turn off window
			/// transparency first; FullContent uses PrintWindow with PW_RENDERFULLCONTENT. Names are
			/// case-insensitive and unknown values raise ValueError. The technique is ignored on macOS/Linux.
			/// The <c>Decorations</c> property (default false) controls
			/// whether the title bar/borders are captured; it is honored only on KWin Wayland (false = client
			/// area only, true = full window) and ignored elsewhere, where each backend captures a fixed extent.</para>
			/// </summary>
			[Static] public static object FromWindow(object @this, object winTitle = null, object options = null, object winText = null, object excludeTitle = null, object excludeText = null)
			{
				if (!ParseCaptureMode(options, out var mode, out var modeName))
					return DefaultObject;

				if (mode < 0)
					return Errors.ValueErrorOccurred($"Unknown Mode \"{modeName}\". Expected BitBlt, BitBltOpaque, PrintWindow, PrintWindowOpaque or FullContent.", options);

				// Null only after SearchWindow's own error, or a parameter's, was continued.
				if (WindowSearch.SearchWindow(winTitle, winText, excludeTitle, excludeText, true) is not WindowInfoBase w)
					return DefaultObject;

#if LINUX
			if (w is Keysharp.Internals.Window.Linux.Wayland.WaylandWindowInfo wayland
				&& !wayland.HasKnownField(Keysharp.Internals.Window.Linux.Wayland.WaylandWindowFields.Frame)
				&& Keysharp.Internals.Window.Linux.Wayland.DesktopClient.TryProbeWindowSupport(out var capture, out _)
				&& !capture)
				return Errors.UnsupportedErrorOccurred("Image.FromWindow is unavailable: the desktop service provides neither window capture nor global window geometry.");
#endif

			// Whether to capture the title bar/borders. The default (false) captures only the client area
				// where the backend supports it (KWin); excluding decorations avoids the shadow-padded buffer whose
				// margin can't be mapped back to screen reliably. Honored only on KWin; on Windows/macOS/GNOME/X11
				// each backend captures a fixed extent and the flag is ignored.
				var includeDeco = ParseIncludeDecoration(options);

				// The window's screen-absolute frame rectangle. Its top-left is the on-screen origin recorded on
				// the Image so OCR (and any other consumer) can map image coordinates back to the screen.
				var bounds = w.Bounds;
				var (bmp, pixelScale) = GuiHelper.CaptureWindowContent(w.Handle, mode, includeDeco);

				if (bmp != null)
				{
					// Origin of the captured pixels in screen coordinates — where the image's top-left sits on screen.
					int ox = bounds.X, oy = bounds.Y;

					// How far the capture extends beyond the window frame per side, in physical pixels, assuming a
					// SYMMETRIC margin (KWin's include-decoration buffer and the GNOME window actor both pad the frame
					// symmetrically with the drop shadow). >0 means decorations/shadow were captured around the frame.
					double shadowXpx = (bmp.Width - bounds.Width * pixelScale.X) / 2.0;
					double shadowYpx = (bmp.Height - bounds.Height * pixelScale.Y) / 2.0;
					bool capturedBeyondFrame = shadowXpx > 0.5 || shadowYpx > 0.5;

					// Decorations are in the captured pixels when we asked for them (and the backend honored it) or
					// when the backend includes them regardless of the flag — GNOME always images the whole actor, so
					// capturedBeyondFrame catches that even though includeDeco is false (otherwise the client branch
					// below would wrongly map the origin to the client top-left).
					if (includeDeco || capturedBeyondFrame)
					{
						// Frame- or buffer-aligned capture. Windows' PrintWindow and macOS' capture return exactly the
						// frame (shadow margin ~0), so the frame origin is right (default above). KWin include-decoration
						// and the GNOME actor return the frame padded by the symmetric shadow margin; offset the origin to
						// the buffer's top-left derived from that margin. The shadow is transparent and harmless to OCR.
						// The captured image is the buffer, so its size versus the frame gives the margin directly.
						if (capturedBeyondFrame)
						{
							ox = bounds.X - (int)Math.Round(shadowXpx / pixelScale.X);
							oy = bounds.Y - (int)Math.Round(shadowYpx / pixelScale.Y);
						}
					}
#if LINUX
					else
					{
						// Client-area capture (X11 XGetImage, or KWin Wayland without decorations): the captured pixels are
						// already the client area natively (no crop), beginning at the CLIENT top-left and smaller than the
						// frame. Prefer the compositor's reported client position (derived from clientPos/clientSize
						// when KWin doesn't expose clientGeometry); if it's unreliable (equals the frame), derive the
						// insets from the capture: the client image is smaller than the frame by the borders, so
						// side = (frameW - capW)/2 and titleTop = (frameH - capH) - side.
						var clientPt = w.ClientToScreen();
						double capW = pixelScale.X != 0 ? bmp.Width / pixelScale.X : bmp.Width;
						double capH = pixelScale.Y != 0 ? bmp.Height / pixelScale.Y : bmp.Height;
						bool clientUnreliable = clientPt.X == bounds.X && clientPt.Y == bounds.Y && capH < bounds.Height - 1;

						if (clientUnreliable)
						{
							int side = (int)Math.Max(0, Math.Round((bounds.Width - capW) / 2));
							int titleTop = (int)Math.Max(0, Math.Round(bounds.Height - capH - side));
							(ox, oy) = (bounds.X + side, bounds.Y + titleTop);
						}
						else if (clientPt.X >= bounds.X && clientPt.Y >= bounds.Y && (clientPt.X != 0 || clientPt.Y != 0))
						{
							(ox, oy) = (clientPt.X, clientPt.Y);
						}
					}
#endif
					return Wrap(bmp, pixelScale.X, pixelScale.Y, originX: ox, originY: oy);
				}

				// No platform window capture: grab the window's on-screen rectangle. That grab is frame-aligned
				// (it includes the title bar), so the frame origin is correct for this path.
				bmp = GuiHelper.GetScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height);

				if (bmp == null)
					return Errors.ErrorOccurred("Capturing the window failed.");

				double sx = bounds.Width > 0 ? (double)bmp.Width / bounds.Width : 1.0;
				double sy = bounds.Height > 0 ? (double)bmp.Height / bounds.Height : 1.0;
				return Wrap(bmp, sx, sy, originX: bounds.X, originY: bounds.Y);
			}

			private static bool ParseCaptureMode(object options, out int mode, out string modeName)
			{
				var value = options is KeysharpObject ? Script.GetPropertyValueOrNull(options, "Mode") : options;

				if (!value.CoerceString(out modeName, "FullContent"))
				{
					mode = -1;
					return false;
				}

				mode = modeName.ToLowerInvariant() switch
				{
					"bitblt" => 0,
					"bitbltopaque" => 1,
					"printwindow" => 2,
					"printwindowopaque" => 3,
					"fullcontent" => 4,
					_ => -1
				};
				return true;
			}

			// Whether the capture should include the window's title bar/borders, read from an options object's
			// `Decorations` property (e.g. Image.FromWindow("A", {Decorations: true})). Defaults to false (client
			// area only): on KWin a decoration-inclusive capture returns the window's shadow-padded buffer, and the
			// shadow margin can't be mapped back to screen reliably. A bare string or no options keeps the default.
			private static bool ParseIncludeDecoration(object options)
			{
				if (options is not KeysharpObject)
					return false;

				var d = Script.GetPropertyValueOrNull(options, "Decorations");
				return d != null && d.Ab();
			}

			/// <summary>
			/// Loads an image from a file. <paramref name="width"/>/<paramref name="height"/> optionally scale it
			/// on load (a negative value keeps the aspect ratio); <paramref name="iconNumber"/> selects an
			/// icon group from multi-icon resources (EXE/DLL/ICO).
			/// </summary>
			[Static] public static object FromFile(object @this, object path, object width = null, object height = null, object iconNumber = null)
			{
				if (!path.CoerceString(out var f))
					return DefaultObject;

				if (f.Length == 0)
					return Errors.ValueErrorOccurred("A file name is required.");

				if (!width.CoerceInt(out var w, 0) || !height.CoerceInt(out var h, 0))
					return DefaultObject;

				var (bmp, source) = ImageHelper.LoadImage(f, w, h, iconNumber == null ? 0L : ImageHelper.PrepareIconNumber(iconNumber), exactPixels: true);
				(source as IDisposable)?.Dispose();
				return Wrap(bmp, failMsg: $"Loading the image from {f} failed.");
			}

			/// <summary>
			/// Wraps an existing image: another <c>Image</c>, or a native bitmap handle (HBITMAP on
			/// Windows, or a handle previously returned by <see cref="ToBitmap"/>).
			/// </summary>
			[Static] public static object FromBitmap(object @this, object handle)
			{
				if (handle == null)
					return Errors.ValueErrorOccurred("A bitmap source is required.");

				var (bmp, sx, sy) = LoadFromSource(handle);
				return Wrap(bmp, sx, sy, "Could not create an image from the given bitmap.");
			}

			/// <summary>
			/// Wraps the image currently on the clipboard, or "" when it holds none.
			/// <para>An alias of <c>Clipboard.Image</c>, which is where clipboard I/O lives (write it with
			/// <c>Clipboard.Image := img</c>). It is kept here because this factory family is a set a script scans as
			/// a whole, and dropping one member to send the reader to another class costs more than the alias does.</para>
			/// </summary>
			[Static] public static object FromClipboard(object @this) => KeysharpClipboard.staticget_Image(null);

			/// <summary>
			/// Creates a new ARGB canvas. Omit <paramref name="background"/> or pass "" for a fully
			/// transparent image; otherwise pass a color name, 0xRRGGBB, or 0xAARRGGBB value.
			///
			/// <para><paramref name="scale"/> (default 1) is an explicit rasterization convenience: the returned image is a
			/// PHYSICAL-resolution bitmap of <c>round(width*scale) x round(height*scale)</c> pixels, but every
			/// draw op and font is multiplied by <paramref name="scale"/>, so the caller authors shapes/text in
			/// LOGICAL units (the width/height passed here) and gets a crisp, physically-larger result rather than
			/// an upscaled small bitmap. It is not a display/backing scale: Overlay selects its own backing pixels, and
			/// callers should use Overlay.Redraw when target-aware vector/text rendering is required. MeasureText stays in logical units, so a
			/// layout measured at scale 1 composes unchanged with a scaled draw canvas.</para>
			/// </summary>
			[Static] public static object Create(object @this, object width, object height, object background = null, object scale = null)
			{
				if (!width.CoerceInt(out var w) || !height.CoerceInt(out var h) || !scale.CoerceDouble(out var s, 1.0))
					return DefaultObject;

				if (w <= 0 || h <= 0)
					return Errors.ValueErrorOccurred("Image.Create width and height must be positive.");

				if (!double.IsFinite(s) || s <= 0)
					return Errors.ValueErrorOccurred("Image.Create scale must be a finite positive number.");

				var pwValue = Math.Round(w * s);
				var phValue = Math.Round(h * s);

				if (!double.IsFinite(pwValue) || !double.IsFinite(phValue)
						|| pwValue < 1 || phValue < 1 || pwValue > int.MaxValue || phValue > int.MaxValue)
					return Errors.ValueErrorOccurred("Image.Create scaled dimensions exceed the supported bitmap range.");

				if (!TryParseColorArg(background, out var bg))
					return DefaultObject;

				var pw = (int)pwValue;
				var ph = (int)phValue;
				var bmp = ImageHelper.NewArgbCanvas(pw, ph);

				if (((uint)bg >> 24) != 0)
				{
					using var g = ImageHelper.MakeGraphics(bmp, highQuality: false);
					g.Clear(ImageHelper.ArgbToColor(bg));
				}

				var wrapped = Wrap(bmp);

				// A scaled canvas draws logical coordinates through a matching transform (see DrawG),
				// keeping text and shapes crisp at the physical resolution instead of upscaling a small bitmap.
				if (s != 1.0 && wrapped is KeysharpImage ki)
					ki.drawScaleX = ki.drawScaleY = s;

				return wrapped;
			}

			/// <summary>
			/// Builds a NEW ARGB image from raw pixel bytes — the inverse of <see cref="GetPixelData"/>.
			/// <paramref name="data"/> is a <see cref="Buffer"/> (or any object exposing script-visible <c>Ptr</c>
			/// and <c>Size</c> properties, the AHK duck-typing convention) whose bytes describe the image row by
			/// row, top-down, tightly packed (stride = <c>width * bytesPerPixel</c>).
			/// <paramref name="bytesPerPixel"/> (default 4) selects the layout: <c>1</c> = 8-bit grayscale, each
			/// byte becoming an opaque gray pixel (R=G=B=byte, A=255); <c>4</c> = R, G, B, A byte order. The
			/// data must hold at least <c>width * height * bytesPerPixel</c> bytes (a ValueError otherwise).
			/// </summary>
			[Static] public static object FromBuffer(object @this, object data, object width, object height, object bytesPerPixel = null)
			{
				// Accept a Buffer OR any object with Ptr/Size properties (duck-typed, like StrGet): the shared
				// Reflections helpers read a Buffer directly and fall back to a script-visible Ptr/Size on any
				// other object, so both work.
				if (!Reflections.TryGetPtrProperty(data, out long addr) || !Reflections.TryGetSizeProperty(data, out long have))
					return Errors.ValueErrorOccurred("FromBuffer requires a Buffer or an object with Ptr and Size properties.");

				if (!width.CoerceInt(out var w) || !height.CoerceInt(out var h) || !bytesPerPixel.CoerceInt(out var bpp, 4))
					return DefaultObject;

				if (bpp != 1 && bpp != 4)
					return Errors.ValueErrorOccurred("FromBuffer supports only 1 (grayscale) or 4 (RGBA) bytes per pixel.");

				if (w <= 0 || h <= 0)
					return Errors.ValueErrorOccurred("FromBuffer width and height must be positive.");

				nint ptr = new nint(addr);//TryGetPtrProperty already rejected a null (0) address.

				long need = (long)w * h * bpp;

				if (have < need)
					return Errors.ValueErrorOccurred($"FromBuffer needs at least {need} bytes for a {w}x{h} image but the buffer holds {have}.");

				var bmp = ImageHelper.NewArgbCanvas(w, h);

				unsafe
				{
					ImageHelper.WriteBufferToBitmap(bmp, (byte*)ptr, bpp);
				}

				return Wrap(bmp, failMsg: "Could not create an image from the given pixel data.");
			}

			#endregion

			#region Transforms (lazy, chainable)

			/// <summary>Queues a multiplicative resize. <c>Scale(2)</c> doubles; <c>Scale(2, 1)</c> stretches X only.</summary>
			public object Scale(object factor, object factorY = null)
			{
				ThrowIfDisposed();

				if (!factor.CoerceDouble(out var sx))
					return this;

				var sy = sx;

				if (factorY != null && !factorY.CoerceDouble(out sy))
					return this;

				if (sx <= 0 || sy <= 0)
					return Errors.ValueErrorOccurred("Scale factors must be positive.");

				if (!QueueTransform(b =>
				{
					var nw = Math.Max(1, (int)Math.Round(b.Width * sx));
					var nh = Math.Max(1, (int)Math.Round(b.Height * sy));
					return ImageHelper.ResizeBitmap(b, nw, nh, exactPixels: true);
				}))
					return this;

				// Scaling multiplies the pixels-per-screen-unit density: after Scale(2) there are twice as
				// many image pixels per native screen unit. Folding the factor into scaleX/scaleY keeps the
				// image-to-screen mapping (e.g. OCR dividing word coordinates by ScaleX) correct, so callers
				// can upscale for accuracy without their coordinates drifting.
				scaleX *= sx;
				scaleY *= sy;
				return this;
			}

			/// <summary>Queues a clockwise rotation by <paramref name="angle"/> degrees. The canvas grows to fit;
			/// <paramref name="background"/> fills the exposed corners. Omit it or pass "" for a transparent
			/// fill; pass a 0xRRGGBB color (including numeric 0 for opaque black) for a solid fill.
			///
			/// <para>Unlike <see cref="Scale"/> and <see cref="Crop"/>, Rotate does not maintain the image-to-screen
			/// coordinate mapping: <see cref="OriginX"/>/<see cref="OriginY"/> and
			/// <see cref="ScaleX"/>/<see cref="ScaleY"/> read "" afterwards (a correct origin/scale for an
			/// arbitrary rotation is ill-defined). <see cref="SetOrigin"/> can re-establish a mapping.</para></summary>
			public object Rotate(object angle, object background = null)
			{
				ThrowIfDisposed();

				if (!angle.CoerceDouble(out var deg))
					return this;

				if (!TryParseColorArg(background, out var bg))
					return this;

				if (!QueueTransform(b => ImageHelper.RotateBitmap(b, deg, bg)))
					return this;

				originValid = false;
				scaleValid = false;
				return this;
			}

			/// <summary>Queues a mirror: horizontal (left-right) by default, vertical when <paramref name="horizontal"/> is false.
			/// <para>Unlike <see cref="Scale"/> and <see cref="Crop"/>, Flip does not maintain the image-to-screen
			/// coordinate mapping: <see cref="OriginX"/>/<see cref="OriginY"/> read "" afterwards (after a mirror
			/// the pre-flip top-left is no longer the image's top-left). <see cref="ScaleX"/>/<see cref="ScaleY"/>
			/// are unchanged — a flip preserves pixel density. <see cref="SetOrigin"/> can re-anchor the image.</para></summary>
			public object Flip(object horizontal = null)
			{
				ThrowIfDisposed();
				var h = horizontal == null || horizontal.Ab();
				if (!QueueTransform(b => ImageHelper.FlipBitmap(b, h)))
					return this;

				originValid = false;
				return this;
			}

			/// <summary>Queues a crop to the (x, y, w, h) sub-region. The region is clamped to the image
			/// bounds at apply time; <paramref name="width"/> and <paramref name="height"/> must be positive.</summary>
			public object Crop(object x, object y, object width, object height)
			{
				ThrowIfDisposed();

				if (!x.CoerceInt(out var cx) || !y.CoerceInt(out var cy) || !width.CoerceInt(out var cw) || !height.CoerceInt(out var ch))
					return this;

				if (cw <= 0 || ch <= 0)
					return Errors.ValueErrorOccurred("Crop width and height must be positive.");

				if (!QueueTransform(b => ImageHelper.CropBitmap(b, cx, cy, cw, ch)))
					return this;

				// Cropping moves the image's top-left to (cx, cy) in image pixels, so its on-screen origin
				// shifts by that offset converted to native screen units. Keeping originX/originY in step lets a
				// consumer such as OCR map coordinates from the cropped image back to the original screen
				// position. (CropBitmap clamps a negative start to 0, so clamp here to match.)
				originX += (int)Math.Round(Math.Max(0, cx) / scaleX);
				originY += (int)Math.Round(Math.Max(0, cy) / scaleY);
				return this;
			}

			/// <summary>Queues an absolute resize to <paramref name="width"/> x <paramref name="height"/> pixels.
			/// A negative value keeps the aspect ratio from the other dimension (e.g. <c>Resize(-1, 30)</c> derives
			/// the width from the current 2:1 ratio); zero, or both dimensions negative, is a ValueError. Like
			/// <see cref="Scale"/> this folds into ScaleX/ScaleY so the image-to-screen mapping (OCR) stays correct.
			/// The current dimensions are resolved once at queue time, so the target is a fixed pixel size regardless
			/// of any later transforms in the chain.</summary>
			public object Resize(object width, object height)
			{
				ThrowIfDisposed();

				if (!width.CoerceInt(out var tw) || !height.CoerceInt(out var th))
					return this;

				if (tw == 0 || th == 0 || (tw < 0 && th < 0))
					return Errors.ValueErrorOccurred("Resize requires a positive width or height (a single negative value keeps the aspect ratio).");

				// Resolve the target against the CURRENT (materialized) dimensions so a negative value derives from
				// the live aspect ratio and the folded scale factor is exact; nw/nh are then constants baked into
				// the queued op, keeping the target an absolute pixel size.
				var src = Materialize();

				if (src == null)
					return Errors.ValueErrorOccurred("There is no image to resize.");

				int curW = src.Width, curH = src.Height;
				int nw = tw, nh = th;

				if (nw < 0) nw = Math.Max(1, (int)Math.Round(curW * (nh / (double)curH)));
				if (nh < 0) nh = Math.Max(1, (int)Math.Round(curH * (nw / (double)curW)));

				if (!QueueTransform(b => ImageHelper.ResizeBitmap(b, nw, nh, exactPixels: true)))
					return this;

				// Absolute resize changes the pixels-per-screen-unit density by nw/curW (nh/curH); fold it into
				// scaleX/scaleY exactly as Scale does so consumers (OCR) recover native units after the resize.
				scaleX *= (double)nw / curW;
				scaleY *= (double)nh / curH;
				return this;
			}

			/// <summary>Queues a full-canvas clear. Omit <paramref name="color"/> or pass "" for transparent.</summary>
			public object Clear(object color = null)
			{
				ThrowIfDisposed();

				if (!TryParseColorArg(color, out var argb))
					return this;

				// A live surface must retain bitmap identity. Clearing in place also avoids a canvas-sized
				// allocation on every animation frame.
				if (eagerDraw)
				{
					var partial = clearColor == argb && drawn != null && drawn.Kind != DamageKind.All;
					var region = partial ? drawn.Union() : new PixelRect(0, 0, baseBitmap.Width, baseBitmap.Height);

					if (!region.IsEmpty)
					{
						ReleaseLiveGraphics();
						QueueDraw(b =>
						{
							ImageHelper.ClearInPlace(b, argb, region);
							return b;
						});
						if (partial)
							damage?.Add(region);
						else
							damage?.AddAll();
					}

					clearColor = argb;
					drawn?.Reset();
					return this;
				}

				QueueDraw(b =>
				{
#if WINDOWS
					// Any other format could not hold the cleared colour's alpha exactly, so it gets a new canvas.
					if (b.PixelFormat == PixelFormat.Format32bppArgb)
					{
						ImageHelper.ClearInPlace(b, argb, new PixelRect(0, 0, b.Width, b.Height));
						return b;
					}

#endif
					var dst = ImageHelper.NewArgbCanvas(b.Width, b.Height);

					if (((uint)argb >> 24) != 0)
					{
						using var g = ImageHelper.MakeGraphics(dst, highQuality: false);
						g.Clear(ImageHelper.ArgbToColor(argb));
					}

					return dst;
				});
				return this;
			}

			/// <summary>
			/// Replaces this surface's entire contents with <paramref name="src"/>'s pixels, stretched to fit.
			/// For <c>Overlay.SetImage</c>, which needs "these pixels, exactly" rather than a composite.
			///
			/// Unscaled and, at matching sizes, unfiltered on purpose: these are canvas pixels, not draw units,
			/// so <see cref="DrawG"/>'s drawScale would be applied twice and its bicubic filter would soften a
			/// 1:1 copy.
			///
			/// Works on an already-painted surface, which is what makes reusing an Overlay's canvas across
			/// frames safe. That is not free on the Eto backends: they have no source-copy compositing mode, so
			/// the target has to be wiped first or a transparent pixel in <paramref name="src"/> would let the
			/// previous frame show through — permanently, and accumulating frame over frame.
			/// </summary>
			internal void BlitFrom(Bitmap src)
			{
				if (src == null || baseBitmap == null || ReferenceEquals(src, baseBitmap))
					return;

				ReleaseLiveGraphics();
				QueueDraw(b =>
				{
					var sameSize = b.Width == src.Width && b.Height == src.Height;
#if WINDOWS
					using var g = ImageHelper.MakeGraphics(b, highQuality: !sameSize);
					g.CompositingMode = CompositingMode.SourceCopy;
					g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height),
								new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
#else
					// Wipe, then draw over: source-over onto an empty surface is the same thing as a replace.
					using var g = ImageHelper.MakeGraphics(b, highQuality: !sameSize);
					g.Clear(ImageHelper.ArgbToColor(0));
					g.DrawImage(src, new RectangleF(0, 0, b.Width, b.Height));
#endif
					return b;
				});
				DamageAll();
			}

			/// <summary>Queues a line draw operation.</summary>
			public object DrawLine(object x1, object y1, object x2, object y2, object color = null, object thickness = null)
			{
				ThrowIfDisposed();

				if (!x1.CoerceDouble(out var px1) || !y1.CoerceDouble(out var py1) || !x2.CoerceDouble(out var px2) || !y2.CoerceDouble(out var py2))
					return this;

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (!thickness.CoerceDouble(out var tRaw, 1.0))
					return this;

				var t = Math.Max(0.0, tRaw);

				if (t == 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
					var pen = GetPen(argb, (float)t);
					g.DrawLine(pen, (float)px1, (float)py1, (float)px2, (float)py2);
					return b;
				});
				DamageVector(MakeRectF(Math.Min(px1, px2), Math.Min(py1, py2), Math.Abs(px2 - px1), Math.Abs(py2 - py1)), t / 2, state);
				return this;
			}

			/// <summary>Queues a rectangle outline draw operation.</summary>
			public object DrawRect(object x, object y, object width, object height, object color = null, object thickness = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (!thickness.CoerceDouble(out var tRaw, 1.0))
					return this;

				var t = Math.Max(0.0, tRaw);

				if (rect.Width <= 0 || rect.Height <= 0 || t == 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					// Axis-aligned rectangle strokes should land on exact pixels; antialiasing can slightly
					// dim corner pixels and make GetPixel() nondeterministic.
					using var gl = DrawG(b, state, highQuality: !state.Transform.IsAxisAligned);
					var g = gl.Graphics;
					var brush = Brush(argb);
					var stroke = (float)t;

					// The bars do not overlap, so a translucent colour paints every pixel once. A stroke that meets
					// itself fills the whole rectangle.
					if (2 * stroke >= rect.Width || 2 * stroke >= rect.Height)
						g.FillRectangle(brush, rect);
					else
					{
						var side = rect.Height - 2 * stroke;
						g.FillRectangle(brush, new RectangleF(rect.X, rect.Y, rect.Width, stroke));
						g.FillRectangle(brush, new RectangleF(rect.X, rect.Bottom - stroke, rect.Width, stroke));
						g.FillRectangle(brush, new RectangleF(rect.X, rect.Y + stroke, stroke, side));
						g.FillRectangle(brush, new RectangleF(rect.Right - stroke, rect.Y + stroke, stroke, side));
					}

					return b;
				});
				DamageVector(rect, 0, state);   // The stroke is drawn inside rect, so the rect itself bounds it.
				return this;
			}

			/// <summary>Queues a filled rectangle draw operation.</summary>
			public object FillRect(object x, object y, object width, object height, object color = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (rect.Width <= 0 || rect.Height <= 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					// Axis-aligned fill: antialiasing would only fuzz the edges, so draw it hard (highQuality:false).
					using var gl = DrawG(b, state, highQuality: !state.Transform.IsAxisAligned);
					var g = gl.Graphics;
					var brush = Brush(argb);
					g.FillRectangle(brush, rect);
					return b;
				});
				DamageVector(rect, 0, state);
				return this;
			}

			/// <summary>Queues an ellipse outline draw operation.</summary>
			public object DrawEllipse(object x, object y, object width, object height, object color = null, object thickness = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (!thickness.CoerceDouble(out var tRaw, 1.0))
					return this;

				var t = Math.Max(0.0, tRaw);

				if (rect.Width <= 0 || rect.Height <= 0 || t == 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
					var pen = GetPen(argb, (float)t);
					g.DrawEllipse(pen, rect);
					return b;
				});
				DamageVector(rect, t, state);
				return this;
			}

			/// <summary>Queues a filled ellipse draw operation.</summary>
			public object FillEllipse(object x, object y, object width, object height, object color = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (rect.Width <= 0 || rect.Height <= 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
					var brush = Brush(argb);
					g.FillEllipse(brush, rect);
					return b;
				});
				DamageVector(rect, 0, state);
				return this;
			}

			/// <summary>Queues a rounded-rectangle outline draw operation (the classic OSD pill).
			/// <paramref name="radius"/> is the corner radius in pixels, clamped to half the smaller side.</summary>
			public object DrawRoundRect(object x, object y, object width, object height, object radius, object color = null, object thickness = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh) || !radius.CoerceDouble(out var radiusRaw))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);
				var r = (float)Math.Max(0.0, radiusRaw);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (!thickness.CoerceDouble(out var tRaw, 1.0))
					return this;

				var t = Math.Max(0.0, tRaw);

				if (rect.Width <= 0 || rect.Height <= 0 || t == 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
					var pen = GetPen(argb, (float)t);
					using var path = MakeRoundRectPath(rect, r);
					g.DrawPath(pen, path);
					return b;
				});
				DamageVector(rect, t, state);
				return this;
			}

			/// <summary>Queues a filled rounded-rectangle draw operation.</summary>
			public object FillRoundRect(object x, object y, object width, object height, object radius, object color = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceDouble(out var rx) || !y.CoerceDouble(out var ry) || !width.CoerceDouble(out var rw) || !height.CoerceDouble(out var rh) || !radius.CoerceDouble(out var radiusRaw))
					return this;

				var rect = MakeRectF(rx, ry, rw, rh);
				var r = (float)Math.Max(0.0, radiusRaw);

				if (!TryParseColorArg(color, out var argb, unchecked((int)0xFF000000u), allowTransparentEmpty: false))
					return this;

				if (rect.Width <= 0 || rect.Height <= 0 || ((uint)argb >> 24) == 0)
					return this;

				var state = SnapshotDrawingState();
				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
					var brush = Brush(argb);
					using var path = MakeRoundRectPath(rect, r);
					g.FillPath(brush, path);
					return b;
				});
				DamageVector(rect, 0, state);
				return this;
			}

			// Four corner arcs + closed figure; a radius of 0 (or a degenerate rect) degrades to a plain
			// rectangle. GraphicsPath resolves to the System.Drawing or Eto type per backend — the members
			// used here are name-identical on both.
			private static GraphicsPath MakeRoundRectPath(RectangleF rect, float radius)
			{
				var path = new GraphicsPath();
				var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);

				if (r <= 0)
				{
					path.AddRectangle(rect);
					return path;
				}

				var d = r * 2f;
				path.AddArc(rect.X, rect.Y, d, d, 180, 90);
				path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
				path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
				path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
				path.CloseFigure();
				return path;
			}

			/// <summary>Queues text rendering. <paramref name="options"/> takes Gui.SetFont-style options
			/// ("s16 bold italic underline strike", case-insensitive); <paramref name="fontName"/> the font
			/// family name (a platform default when omitted) — the same two-argument font convention as
			/// <c>Gui.SetFont(Options, FontName)</c>.</summary>
			public object DrawText(object text, object x, object y, object color = null, object options = null, object fontName = null)
			{
				ThrowIfDisposed();

				if (!text.CoerceString(out var s) || string.IsNullOrEmpty(s))
					return this;

				if (!TryVectorPoint(x, y, out var textOrigin))
					return this;

				var px = textOrigin.X;
				var py = textOrigin.Y;

				if (!SplitFontArgs(options, fontName, out var fontOptions, out var fontFamily)
						|| !TryGetImageFont(fontOptions, fontFamily, out var font, out var quality))
					return this;

				//A Ks.Font in the options slot carries its own colour, which is the one thing the option string
				//cannot express here, so it seeds the colour argument when that was left out.
				var defaultArgb = options is Font sf && sf.fontOptions.color.HasValue
					? sf.fontOptions.color.Value.ToArgb() : unchecked((int)0xFF000000u);
				if (!TryVectorPaint(color, defaultArgb, out var paint))
					return this;

				if (paint.IsTransparent)
					return this;

				var state = SnapshotDrawingState();

				QueueDraw(b =>
				{
					using var gl = DrawG(b, state);
					var g = gl.Graphics;
#if WINDOWS
					ConfigureFontGraphics(g, quality);
#endif
					var sz = default(SizeF);
					var measured = !paint.IsSolid || damage != null;

					if (measured)
						sz = ImageHelper.MeasureText(g, font, s);

					if (paint.IsSolid)
					{
#if WINDOWS
						g.DrawString(s, font, Brush(paint.Solid), (float)px, (float)py);
#else
						g.DrawText(font, Brush(paint.Solid), (float)px, (float)py, s);
#endif
					}
					else
					{
						var coverage = ExpandBounds(new RectangleF((float)px, (float)py, sz.Width, sz.Height),
							Math.Max(2, sz.Height / 2));
						using var brush = CreateVectorBrush(paint.Brush, coverage);
#if WINDOWS
						g.DrawString(s, font, brush, (float)px, (float)py);
#else
						g.DrawText(font, brush, (float)px, (float)py, s);
#endif
					}

					// Measured on the Graphics that drew, the one place a Graphics is in hand, so only a presented
					// surface pays for it.
					if (damage != null)
					{
						// Measured extents are the logical box; italics, swashes and negative-left-bearing
						// glyphs paint outside it, and neither backend reports ink extents cheaply. Pad by half
						// the line height, which covers a full-height overhang on either side.
						DamageVector(new RectangleF((float)px, (float)py, sz.Width, sz.Height),
							Math.Max(2.0, sz.Height / 2), state);
					}

					return b;
				});

				return this;
			}

			/// <summary>Measures the size <paramref name="text"/> would occupy when drawn with the given font
			/// (same <paramref name="options"/>/<paramref name="fontName"/> convention as <see cref="DrawText"/>)
			/// and returns it as a <c>{Width, Height}</c> object. The size is in draw units, matching DrawText and
			/// the pixel-coordinate shapes — use it to centre or align text before drawing.</summary>
			public object MeasureText(object text, object options = null, object fontName = null)
			{
				ThrowIfDisposed();

				if (!SplitFontArgs(options, fontName, out var o, out var n) || !text.CoerceString(out var s)
						|| !TryGetImageFont(o, n, out var font, out var quality))
					return DefaultObject;

				var (w, h) = MeasureWithFont(s, font, quality);
				return MakeSize(w, h);
			}

			/// <summary>
			/// Normalizes a <see cref="Ks.Font"/> in the options position into options and family.
			/// An explicit fontName overrides the object's family.
			/// </summary>
			internal static bool SplitFontArgs(object options, object fontName, out string fontOptions, out string fontFamily)
			{
				fontOptions = "";

				if (fontName is Any)
				{
					fontFamily = "";
					_ = Errors.TypeErrorOccurred(fontName, typeof(string));
					return false;
				}

				if (!fontName.CoerceString(out fontFamily))
					return false;

				if (options is not Font f)
					return options.CoerceString(out fontOptions);

				fontOptions = f.fontOptions.OptionsNoColor;

				if (fontFamily.Length == 0)
					fontFamily = f.fontOptions.name ?? "";

				return true;
			}

			// Pixel size of text in the given font options, as DrawText lays it out at a draw scale of 1. (0,0) for
			// empty text or after a font-option error the script continued.
			internal static (double w, double h) MeasureTextCore(string text, string options, string fontName)
				=> TryGetImageFont(options, fontName, out var font, out var quality)
				   ? MeasureWithFont(text, font, quality) : (0.0, 0.0);

#if WINDOWS
			// Measuring needs a Graphics but no surface of its own, so each thread keeps one, on a 1x1 bitmap the
			// Graphics keeps alive.
			[ThreadStatic] private static Graphics measureGraphics;
#endif

			private static (double w, double h) MeasureWithFont(string text, NativeFont font, int? quality)
			{
				if (string.IsNullOrEmpty(text))
					return (0.0, 0.0);

#if WINDOWS
				var g = measureGraphics ??= ImageHelper.MakeGraphics(ImageHelper.NewArgbCanvas(1, 1));
				ConfigureFontGraphics(g, quality);
#else
				// An Eto Graphics on a bitmap holds backend locks, so it is not kept between calls.
				using var bmp = ImageHelper.NewArgbCanvas(1, 1);
				using var g = ImageHelper.MakeGraphics(bmp);
#endif
				var sz = ImageHelper.MeasureText(g, font, text);
				return (sz.Width, sz.Height);
			}

			// MeasureText's return shape: {Width, Height}.
			internal static KeysharpObject MakeSize(double w, double h)
			{
				var o = new KeysharpObject();
				o.DefinePropInternal("Width", new OwnPropsDesc(w));
				o.DefinePropInternal("Height", new OwnPropsDesc(h));
				return o;
			}

			/// <summary>Queues drawing another image onto this canvas.</summary>
			public object DrawImage(object image, object x = null, object y = null, object width = null, object height = null)
			{
				ThrowIfDisposed();
				// Immediate draws can borrow a different image. Queued draws and self-draws need a snapshot.
				var borrowed = eagerDraw && image is KeysharpImage && !ReferenceEquals(image, this);
				var source = borrowed ? ((KeysharpImage)image).PrepareForRead() : LoadFromSource(image).bmp;

				if (source == null)
					return Errors.ValueErrorOccurred("DrawImage source must be an Image, file path, or bitmap handle.");

				var ownedSource = borrowed ? null : source;

				try
				{
					if (!x.CoerceDouble(out var px, 0.0) || !y.CoerceDouble(out var py, 0.0))
						return this;

					double requestedW, requestedH;

					if (width == null)
						requestedW = source.Width;
					else if (!width.CoerceDouble(out requestedW))
						return this;

					if (height == null)
						requestedH = source.Height;
					else if (!height.CoerceDouble(out requestedH))
						return this;

					if (requestedW <= 0 || requestedH <= 0)
						return this;

					var state = SnapshotDrawingState();
					QueueDraw(b =>
					{
						using var gl = DrawG(b, state);
						var g = gl.Graphics;
#if WINDOWS
						g.DrawImage(source, new RectangleF((float)px, (float)py, (float)requestedW, (float)requestedH),
							new RectangleF(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
#else
						g.DrawImage(source, new RectangleF((float)px, (float)py, (float)requestedW, (float)requestedH));
#endif
						return b;
					});
					DamageVector(new RectangleF((float)px, (float)py, (float)requestedW, (float)requestedH), 1, state);

					if (!eagerDraw)
					{
						pendingResources.Add(source);
						ownedSource = null;
					}
				}
				finally
				{
					ownedSource?.Dispose();
					GC.KeepAlive(image);
				}

				return this;
			}

			/// <summary>Queues a desaturation to grayscale: each pixel's R, G and B become the luminance
			/// <c>0.299 R + 0.587 G + 0.114 B</c>, rounded half up as AutoHotkey's Round does; alpha is preserved.
			/// Lazy and chainable.</summary>
			public object Grayscale()
			{
				ThrowIfDisposed();

				_ = QueueTransform(b => ImageHelper.MapPixelsArgb(b, new GrayscaleMap()));
				return this;
			}

			private readonly struct GrayscaleMap : ImageHelper.IPixelMap
			{
				public uint Map(uint p)
				{
					uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
					var gray = ImageHelper.Grayscale(r, g, b);
					return (p & 0xFF000000u) | (gray << 16) | (gray << 8) | gray;
				}
			}

			// Maps each RGB channel through a 256-entry table and keeps alpha.
			private readonly struct ChannelTableMap(byte[] table) : ImageHelper.IPixelMap
			{
				public uint Map(uint p) => (p & 0xFF000000u) | ((uint)table[(p >> 16) & 0xFF] << 16)
					| ((uint)table[(p >> 8) & 0xFF] << 8) | table[p & 0xFF];
			}

			// Maps alpha through a 256-entry table and keeps RGB.
			private readonly struct AlphaTableMap(byte[] table) : ImageHelper.IPixelMap
			{
				public uint Map(uint p) => ((uint)table[p >> 24] << 24) | (p & 0x00FFFFFFu);
			}

			private static byte[] ChannelTable(Func<int, int> channel)
			{
				var table = new byte[256];

				for (var c = 0; c < table.Length; c++)
					table[c] = (byte)Math.Clamp(channel(c), 0, 255);

				return table;
			}

			/// <summary>Queues an alpha multiply: every pixel's alpha becomes <c>round(A * factor)</c> with
			/// <paramref name="factor"/> clamped to [0, 1]; RGB is preserved. Lazy and chainable. Grouped with
			/// <see cref="Brightness"/>/<see cref="Contrast"/> (normalized-float channel transforms) — distinct
			/// from <c>Overlay.Opacity</c>, which is whole-surface transparency on the AHK 0-255 scale.</summary>
			public object Alpha(object factor)
			{
				ThrowIfDisposed();

				if (!factor.CoerceDouble(out var fRaw))
					return this;

				var f = Math.Clamp(fRaw, 0.0, 1.0);
				var map = new AlphaTableMap(ChannelTable(a => (int)Math.Round(a * f)));

				_ = QueueTransform(b => ImageHelper.MapPixelsArgb(b, map));
				return this;
			}

			/// <summary>Queues a brightness shift: every RGB channel is offset by <c>round(amount * 255)</c> and
			/// clamped to [0, 255], with <paramref name="amount"/> clamped to [-1, 1] (1 = white, -1 = black);
			/// alpha is preserved. Lazy and chainable.</summary>
			public object Brightness(object amount)
			{
				ThrowIfDisposed();

				if (!amount.CoerceDouble(out var amtRaw))
					return this;

				var amt = Math.Clamp(amtRaw, -1.0, 1.0);
				var delta = (int)Math.Round(amt * 255);
				var map = new ChannelTableMap(ChannelTable(c => c + delta));

				_ = QueueTransform(b => ImageHelper.MapPixelsArgb(b, map));
				return this;
			}

			/// <summary>Queues a contrast adjust: each RGB channel becomes
			/// <c>clamp(round((c - 128) * (1 + amount) + 128), 0, 255)</c>, with <paramref name="amount"/> clamped
			/// to [-1, 1] (-1 flattens every channel toward 128, +1 doubles the spread); alpha is preserved.
			/// Lazy and chainable.</summary>
			public object Contrast(object amount)
			{
				ThrowIfDisposed();

				if (!amount.CoerceDouble(out var amtRaw))
					return this;

				var amt = Math.Clamp(amtRaw, -1.0, 1.0);
				var factor = 1.0 + amt;
				var map = new ChannelTableMap(ChannelTable(c => (int)Math.Round((c - 128) * factor + 128)));

				_ = QueueTransform(b => ImageHelper.MapPixelsArgb(b, map));
				return this;
			}

			/// <summary>
			/// Returns an independent copy of this image with its queued transforms already applied, carrying
			/// over the scale and screen-origin metadata. Edits or further transforms on the copy do not affect
			/// the original — used when a consumer (e.g. OCR) needs to transform an image the caller still owns.
			/// </summary>
			public object Copy()
			{
				ThrowIfDisposed();

				if (disposed || baseBitmap == null)
					return Errors.ValueErrorOccurred("There is no image to copy.");

				// Materialize() applies any queued transforms; with none it returns the base bitmap directly (no
				// clone), so Copy() makes exactly one independent copy of the result either way.
				var src = PrepareForRead();

				if (src == null)
					return Errors.ValueErrorOccurred("There is no image to copy.");

				// The draw-unit scales are carried over so a copy of a scaled Create() canvas keeps drawing logical
				// coordinates at the right physical scale. `eagerDraw` is deliberately not copied: a copy is an
				// independent lazy image, not another live drawing surface aliasing the same pixels.
				var copy = new KeysharpImage { baseBitmap = new Bitmap(src), scaleX = scaleX, scaleY = scaleY,
					originX = originX, originY = originY, originValid = originValid, scaleValid = scaleValid,
					drawScaleX = drawScaleX, drawScaleY = drawScaleY };
				copy.SyncGcPressure();
				return copy;
			}

			#endregion

			#region Output

			/// <summary>Applies any queued transforms and saves the result to <paramref name="filename"/>.
			/// The encoder is chosen from the extension (.png/.jpg/.bmp/.gif/.tif), defaulting to PNG.
			/// Note that which formats can actually be written depends on the platform's image backend
			/// (e.g. GIF output is unavailable on some Linux gdk-pixbuf builds); a failure is reported as
			/// an error. Returns this image so calls can chain.</summary>
			public object Save(object filename)
			{
				ThrowIfDisposed();
				var bmp = PrepareForRead();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to save.");

				if (!filename.CoerceString(out var f))
					return DefaultObject;

				try
				{
					ImageHelper.SaveBitmap(bmp, f);
				}
				catch (Exception ex)
				{
					return Errors.ValueErrorOccurred($"Saving the image to {f} failed: {ex.Message}");
				}

				return this;
			}

			/// <summary>Applies any queued transforms and returns a native bitmap handle (HBITMAP on
			/// Windows, a Pixbuf/NSImage handle elsewhere) that the "HBITMAP:" consumers (ImageSearch,
			/// Gui Picture, LoadPicture) accept, as the legacy <c>ImageCapture</c> did. The handle is
			/// managed independently of this image. On Windows it is a 32bpp DIB section with premultiplied
			/// alpha, as LoadPicture returns, which the script owns and frees with DeleteObject.</summary>
			public object ToBitmap()
			{
				ThrowIfDisposed();
				var bmp = PrepareForRead();

				if (bmp == null)
					return 0L;

				// Hand a *copy* to the manager (not `bmp` itself) for two reasons: this image keeps
				// ownership of its own bitmap, and the copy ctor forces a surface-backed bitmap
				// (after a transform) to materialize its real backing store (e.g. a Gdk.Pixbuf on Linux)
				// so the handle the manager extracts is valid. Do not "optimize" the copy away.
				if (ImageHandleManager.TryAddBitmap(new Bitmap(bmp), ImageHandleKind.Bitmap, out var handle))
					return handle.ToInt64();

				return 0L;
			}

			/// <summary>Creates a simple GUI window containing this image (after applying any queued
			/// transforms) and shows it (named to match <c>Gui.Show</c>). Returns the <c>Gui</c> so the
			/// script can move, retitle, or close it. The picture is shown at the image's full pixel size
			/// (the size reported in the caption); the window opens that big, or screen-sized if the image
			/// is larger than the monitor (Windows will not make a window bigger than the screen). It is
			/// resizable between a small lower bound and the image size, never past it into dead space, and
			/// scrollbars appear whenever the window is smaller than the image so all of it stays reachable.
			///
			/// <para>When <paramref name="wait"/> is true the call blocks until the user closes the window
			/// (a Keysharp convenience beyond AHK's Image): handy because OCR results normally flash by, so you
			/// can eyeball the captured image first. The wait pumps the message loop, so timers, hotkeys and the
			/// window itself stay responsive; the preview window is destroyed once dismissed.</para></summary>
			public object Show(object title = null, object wait = null)
			{
				ThrowIfDisposed();
				var bmp = PrepareForRead();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to display.");

				// Snapshot the dimensions from this one materialized bitmap so the caption and the Picture size are
				// provably the same source as the displayed pixels: nothing is queued before ToBitmap() below, so it
				// returns the same bitmap and imgW/imgH match the shown handle.
				int imgW = bmp.Width, imgH = bmp.Height;

				// Converted before the handle is made, which returning early would otherwise leave behind.
				if (!title.CoerceString(out var baseTitle, "Image"))
					return DefaultObject;

				if (ToBitmap() is not long handle || handle == 0)
					return Errors.ValueErrorOccurred("Could not prepare the image for display.");

				// Caption shows the image's true pixel size and capture scale as a percentage
				// (e.g. "Image  3840 x 2160 @ 200%").
				var caption = $"{baseTitle}  {imgW} x {imgH} @ {FormatScalePercent(scaleX, scaleY)}";

				// Construct the Gui through the runtime's Class.Call (the path scripts use); calling the
				// C# constructor directly binds to Gui's internal form-wrapping ctor and crashes. "+Resize"
				// makes the window stretchable; "+AutoScroll" lets the whole image be reached by scrolling
				// when the window is smaller than it. Windows refuses to make a window larger than the
				// monitor (it clamps to the max tracking size), so a picture bigger than the screen cannot
				// be shown by a window that fits it — scrolling bridges that gap.
				if (Script.TheScript.Vars.Statics[typeof(Gui)] is not Class guiClass
						|| guiClass.Call("+Resize +AutoScroll", caption) is not Gui gui)
					return Errors.ErrorOccurred("Could not create a window to display the image.");

				// Size the picture to the image's full pixel dimensions (the caption size). A Picture from
				// an "HBITMAP:" source is set to the loaded bitmap's pixel size directly, bypassing the GUI's
				// DPI scaling, so passing the *pixel* width/height (not a DPI-logical size) is what makes the
				// window open at the size the caption advertises. The control is fixed at this size and
				// pinned top-left; scrolling reaches any part the window is too small to show.
				_ = gui.Add("Picture", $"w{imgW} h{imgH}", "HBITMAP:" + handle);

				// Cap the window at the size it first opens (the image, or the screen if the image is larger):
				// "+MaxSize" with no dimensions defers the limit until first show, where the Gui pins it to
				// that size, so the window can't be stretched past the image into dead space. "+MinSize"
				// gives a small, DPI-scaled lower bound so it can't be shrunk away to nothing.
				_ = gui.Opt("+MinSize120x90 +MaxSize");

				if (wait != null && wait.Ab())
				{
					// Block until the user dismisses the window. Hook the form's close directly rather than
					// WinWaitClose(gui.Hwnd): an AHK GUI HIDES rather than destroys on close, so WinWaitClose
					// would hinge on window-search-by-handle plus the script's DetectHiddenWindows state to
					// decide "gone" — both fragile here (and window-id matching is unreliable on Wayland). The
					// Closing event is a direct, per-window signal with none of those dependencies. A plain
					// captured bool is enough: the event and the pump both run on the UI thread, and
					// WaitUntil re-invokes the predicate each iteration so the read is never hoisted.
					// Subscribe before Show so a near-instant close can't be missed.
					var closing = false;
#if WINDOWS
					gui.form.FormClosing += (s, e) => closing = true;
#else
					gui.form.Closing += (s, e) => closing = true;
#endif
					_ = gui.Show();
					Keysharp.Internals.Flow.WaitUntil(() => closing);
					_ = gui.Destroy();
					return gui;
				}

				_ = gui.Show();
				return gui;
			}

			#endregion

			#region Pixels / search

			/// <summary>The current width in pixels (after queued transforms).</summary>
			public long Width { get { ThrowIfDisposed(); return Materialize()?.Width ?? 0L; } }

			/// <summary>The current height in pixels (after queued transforms).</summary>
			public long Height { get { ThrowIfDisposed(); return Materialize()?.Height ?? 0L; } }

			/// <summary>Image pixels per native screen unit along X at capture time (1.0 for files and
			/// one-pixel-per-unit captures; ~2.0 for a Retina point-space capture). Multiply a native screen
			/// width by this to get image pixels, or divide an image X coordinate by it to get native units.
			/// Reads "" after <see cref="Rotate"/> (the mapping is no longer defined) until
			/// <see cref="SetOrigin"/> re-establishes it.</summary>
			public object ScaleX { get { ThrowIfDisposed(); return scaleValid ? scaleX : ""; } }

			/// <summary>Image pixels per native screen unit along Y. See <see cref="ScaleX"/>.</summary>
			public object ScaleY { get { ThrowIfDisposed(); return scaleValid ? scaleY : ""; } }

			/// <summary>Screen-absolute X coordinate of this image's top-left at capture time (0 for file/bitmap
			/// images, which have no on-screen origin). OCR uses it as the default x offset so highlights and
			/// clicks land on the screen position the words actually occupy. Reads "" after
			/// <see cref="Rotate"/>/<see cref="Flip"/> (the origin is no longer defined) until
			/// <see cref="SetOrigin"/> re-establishes it.</summary>
			public object OriginX { get { ThrowIfDisposed(); return originValid ? (object)(long)originX : ""; } }

			/// <summary>Screen-absolute Y coordinate of this image's top-left at capture time. See <see cref="OriginX"/>.</summary>
			public object OriginY { get { ThrowIfDisposed(); return originValid ? (object)(long)originY : ""; } }

			/// <summary>Re-anchors this image's screen mapping: sets the screen-absolute origin of its top-left
			/// and, optionally, the pixels-per-screen-unit scale (<paramref name="scaleY"/> defaults to
			/// <paramref name="scaleX"/> when only one is given). Use it to give a <see cref="FromBuffer"/>/file
			/// image a screen position, or to restore the mapping after <see cref="Rotate"/>/<see cref="Flip"/>
			/// invalidated it. Returns this image.</summary>
			public object SetOrigin(object x, object y, object scaleX = null, object scaleY = null)
			{
				ThrowIfDisposed();

				if (!x.CoerceInt(out var ox) || !y.CoerceInt(out var oy))
					return this;

				if (scaleX != null || scaleY != null)
				{
					double sx, sy;

					if (scaleX != null)
					{
						if (!scaleX.CoerceDouble(out sx))
							return this;
					}
					else
						sx = this.scaleX;

					if (scaleY != null)
					{
						if (!scaleY.CoerceDouble(out sy))
							return this;
					}
					else
						sy = sx;

					if (!double.IsFinite(sx) || sx <= 0 || !double.IsFinite(sy) || sy <= 0)
						return Errors.ValueErrorOccurred("SetOrigin scale factors must be finite positive numbers.");

					this.scaleX = sx;
					this.scaleY = sy;
					scaleValid = true;
				}

				originX = ox;
				originY = oy;
				originValid = true;

				return this;
			}

			/// <summary>Returns the full 32-bit ARGB color of the pixel at (x, y) as an unsigned value in
			/// 0xAARRGGBB order: an opaque red reads as 0xFFFF0000, a 50%-alpha pixel as 0x80RRGGBB. Mask
			/// with 0xFFFFFF if only the RGB component is wanted.</summary>
			public object GetPixel(object x, object y)
			{
				ThrowIfDisposed();
				var bmp = PrepareForPixelAccess();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to read.");

				if (!x.CoerceInt(out var px) || !y.CoerceInt(out var py))
					return DefaultObject;

				if (px < 0 || py < 0 || px >= bmp.Width || py >= bmp.Height)
					return Errors.ValueErrorOccurred($"Pixel ({px}, {py}) is out of range.");

				return (long)(uint)bmp.GetPixel(px, py).ToArgb();
			}

			/// <summary>Sets the pixel at (x, y) to <paramref name="color"/>, given as 0xRRGGBB (fully
			/// opaque) or 0xAARRGGBB (explicit alpha). Returns this image.</summary>
			public object SetPixel(object x, object y, object color)
			{
				ThrowIfDisposed();
				var bmp = PrepareForPixelAccess();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to write.");

				if (!x.CoerceInt(out var px) || !y.CoerceInt(out var py))
					return DefaultObject;

				if (px < 0 || py < 0 || px >= bmp.Width || py >= bmp.Height)
					return Errors.ValueErrorOccurred($"Pixel ({px}, {py}) is out of range.");

				if (!TryParseColorArg(color, out var argb))
					return this;

				bmp.SetPixel(px, py, ImageHelper.ArgbToColor(argb));
				// In canvas pixels already, so it bypasses Damage()'s draw-unit scaling. The one pixel is
				// reported as a 1x1 rect; without this it is the only mutating op that changes a presented
				// surface without saying so.
				Damage(PixelRect.FromEdges(px, py, px + 1, py + 1));

				return this;
			}

			/// <summary>
			/// Searches this image for <paramref name="needle"/> (an Image, a file path, or a bitmap handle)
			/// and returns the first match as an object whose <c>X</c>/<c>Y</c> are the match's top-left as
			/// 0-based, absolute image pixels (see <see cref="ScaleX"/>), or "" (falsy) when there is no
			/// match — so <c>if m := img.Search(needle)</c> is the idiomatic use.
			///
			/// <para><paramref name="x"/>/<paramref name="y"/>/<paramref name="width"/>/<paramref name="height"/>
			/// restrict the search to a region, clamped to the image. Each is independently optional: omitted
			/// values default to 0 / the far edge, so <c>Search(n, 100, 100)</c> searches from (100, 100) to
			/// the bottom-right corner. Returned coordinates stay absolute image pixels, never region-relative.</para>
			///
			/// <para>Matching is RGB-only (alpha is ignored). <paramref name="variation"/> is the 0-255
			/// per-channel tolerance; <paramref name="trans"/> a needle color that matches anything
			/// (ImageSearch's *TransN); <paramref name="direction"/> selects the scan order:
			/// TopLeft (default), TopRight, BottomLeft, BottomRight scan rows; LeftTop, LeftBottom,
			/// RightTop, RightBottom scan columns; Center starts nearest the center. Names are
			/// case-insensitive and unknown values raise ValueError.</para>
			/// </summary>
			public object Search(object needle, object x = null, object y = null, object width = null, object height = null,
				object variation = null, object trans = null, object direction = null)
			{
				ThrowIfDisposed();

				if (needle == null)
					return Errors.ValueErrorOccurred("Search requires a needle image.");

				if (!ParseSearchDirection(direction, out var dir))
					return DefaultObject;

				if (dir < 1)
					return Errors.ValueErrorOccurred($"Unknown Direction \"{Errors.Describe(direction)}\". Expected {ImageFinder.DirectionNames}.", direction);

				var haystack = PrepareForPixelAccess();

				if (haystack == null)
					return Errors.ValueErrorOccurred("There is no image to search.");

				var (needleBmp, ownsNeedle) = NeedleBitmap(needle);

				if (needleBmp == null)
					return Errors.ValueErrorOccurred("Could not load the search image.");

				try
				{
					if (!CoerceOptionalRegionArg(x, out var xi) || !CoerceOptionalRegionArg(y, out var yi) ||
							!CoerceOptionalRegionArg(width, out var wi) || !CoerceOptionalRegionArg(height, out var hi))
						return DefaultObject;

					var area = ResolveRegion(haystack, xi, yi, wi, hi);

					if (area.Width <= 0 || area.Height <= 0)
						return "";

					var transColor = -1L;

					if (trans != null && trans is not string { Length: 0 })
					{
						if (!TryParseColorArg(trans, out var transArgb))
							return DefaultObject;

						transColor = transArgb & 0xFFFFFF;
					}

					if (!variation.CoerceLong(out var variationL, 0))
						return DefaultObject;

					using var finder = new ImageFinder(haystack, region: area) { Variation = (byte)Math.Clamp(variationL, 0, 255) };
					var loc = finder.Find(needleBmp, transColor, dir);
					return loc.HasValue ? MakePoint(loc.Value.X + area.X, loc.Value.Y + area.Y) : "";
				}
				finally
				{
					if (ownsNeedle)
						needleBmp.Dispose();
				}
			}

			/// <summary>
			/// Searches this image for every occurrence of <paramref name="needle"/> and returns them as an
			/// array of <c>{X, Y}</c> match objects (absolute image pixels) — empty when there are none, so
			/// check <c>matches.Length</c>. The region and matching arguments work exactly as in
			/// <see cref="Search"/>. Matches are ordered by <paramref name="direction"/>;
			/// overlapping matches are all returned. When every needle pixel is the <paramref name="trans"/>
			/// wildcard color, a single match at the region origin is returned. Matching is RGB-only.
			/// </summary>
			public object SearchAll(object needle, object x = null, object y = null, object width = null, object height = null,
				object variation = null, object trans = null, object direction = null)
			{
				ThrowIfDisposed();

				if (needle == null)
					return Errors.ValueErrorOccurred("SearchAll requires a needle image.");

				if (!ParseSearchDirection(direction, out var dir))
					return DefaultObject;

				if (dir < 1)
					return Errors.ValueErrorOccurred($"Unknown Direction \"{Errors.Describe(direction)}\". Expected {ImageFinder.DirectionNames}.", direction);

				var haystack = PrepareForPixelAccess();

				if (haystack == null)
					return Errors.ValueErrorOccurred("There is no image to search.");

				var (needleBmp, ownsNeedle) = NeedleBitmap(needle);

				if (needleBmp == null)
					return Errors.ValueErrorOccurred("Could not load the search image.");

				var results = new Array();

				try
				{
					if (!CoerceOptionalRegionArg(x, out var xi) || !CoerceOptionalRegionArg(y, out var yi) ||
							!CoerceOptionalRegionArg(width, out var wi) || !CoerceOptionalRegionArg(height, out var hi))
						return DefaultObject;

					var area = ResolveRegion(haystack, xi, yi, wi, hi);

					if (area.Width <= 0 || area.Height <= 0)
						return results;

					var transColor = -1L;

					if (trans != null && trans is not string { Length: 0 })
					{
						if (!TryParseColorArg(trans, out var transArgb))
							return DefaultObject;

						transColor = transArgb & 0xFFFFFF;
					}

					if (!variation.CoerceLong(out var variationL, 0))
						return DefaultObject;

					using var finder = new ImageFinder(haystack, region: area) { Variation = (byte)Math.Clamp(variationL, 0, 255) };
					var found = finder.FindAll(needleBmp, transColor, dir);

					foreach (var p in found)
						_ = results.Push(MakePoint(p.X + area.X, p.Y + area.Y));

					return results;
				}
				finally
				{
					if (ownsNeedle)
						needleBmp.Dispose();
				}
			}

			/// <summary>
			/// Searches this image for the first pixel matching a color (a color name, 0xRRGGBB, or 0xAARRGGBB —
			/// PixelSearch over a captured/loaded image instead of the live screen) and returns it as an object
			/// <c>{X, Y, Color}</c> — <c>X</c>/<c>Y</c> are absolute image pixels and <c>Color</c> is the matched
			/// pixel's full 0xAARRGGBB (the same value <see cref="GetPixel"/> returns, alpha included) — or ""
			/// (falsy) on a miss, so <c>if p := img.SearchPixel("Red")</c> is the idiomatic use. Matching is
			/// RGB-only (alpha is ignored).
			///
			/// <para>The optional region works exactly as in <see cref="Search"/>.
			/// <paramref name="variation"/> (0-255) allows per-channel tolerance. <paramref name="direction"/>
			/// selects the scan's starting corner: TopLeft (default), TopRight, BottomLeft or BottomRight.
			/// Names are case-insensitive and unknown values raise ValueError.</para>
			/// </summary>
			public object SearchPixel(object color, object x = null, object y = null, object width = null, object height = null,
				object variation = null, object direction = null)
			{
				ThrowIfDisposed();

				if (color == null)
					return Errors.ValueErrorOccurred("SearchPixel requires a color.");

				if (!ParseSearchDirection(direction, out var dir))
					return DefaultObject;

				if (dir < 1 || dir > 4)
					return Errors.ValueErrorOccurred($"Unknown Direction \"{Errors.Describe(direction)}\". Expected TopLeft, TopRight, BottomLeft or BottomRight.", direction);

				var haystack = PrepareForPixelAccess();

				if (haystack == null)
					return Errors.ValueErrorOccurred("There is no image to search.");

				if (!CoerceOptionalRegionArg(x, out var xi) || !CoerceOptionalRegionArg(y, out var yi) ||
						!CoerceOptionalRegionArg(width, out var wi) || !CoerceOptionalRegionArg(height, out var hi))
					return DefaultObject;

				var area = ResolveRegion(haystack, xi, yi, wi, hi);

				if (area.Width <= 0 || area.Height <= 0)
					return "";

				if (!TryParseColorArg(color, out var target, unchecked((int)0xFF000000), allowTransparentEmpty: false))
					return DefaultObject;

				if (!variation.CoerceLong(out var variationL, 0))
					return DefaultObject;

				Point? loc;

				// The finder locks the haystack, so the matched pixel is read after it is released.
				using (var finder = new ImageFinder(haystack, region: area) { Variation = (byte)Math.Clamp(variationL, 0, 255) })
					loc = finder.Find(ImageHelper.ArgbToColor(target), ltr: dir is 1 or 3, ttb: dir is 1 or 2);

				if (!loc.HasValue)
					return "";

				int ax = loc.Value.X + area.X, ay = loc.Value.Y + area.Y;
				// Report the actual pixel's full ARGB (what GetPixel would return), read from the haystack.
				return MakePixel(ax, ay, (long)(uint)haystack.GetPixel(ax, ay).ToArgb());
			}

			private static bool ParseSearchDirection(object direction, out int dir)
			{
				if (!direction.CoerceString(out var name, "TopLeft"))
				{
					dir = 0;
					return false;
				}

				dir = ImageFinder.ParseDirection(name);
				return true;
			}

			// Coerces an optional region argument (Search/SearchAll/SearchPixel's x/y/width/height) to an int,
			// leaving null when the script omitted it so ResolveRegion applies its own default. False means the
			// value raised a TypeError the script continued, and the caller must return at once.
			private static bool CoerceOptionalRegionArg(object obj, out int? value)
			{
				if (obj == null)
				{
					value = null;
					return true;
				}

				if (!obj.CoerceInt(out var v))
				{
					value = null;
					return false;
				}

				value = v;
				return true;
			}

			// Resolves the optional (x, y, width, height) region arguments into the rectangle of the haystack the
			// finder scans, clamped to it. Each defaults independently: the origin to 0, the size to the far edge.
			// A non-positive size, or an origin at or past an edge, leaves an empty rectangle, which holds no pixels
			// and so no match.
			private static Rectangle ResolveRegion(Bitmap haystack, int? x, int? y, int? width, int? height)
			{
				int rx = Math.Clamp(x ?? 0, 0, haystack.Width);
				int ry = Math.Clamp(y ?? 0, 0, haystack.Height);
				int rw = Math.Clamp(width ?? haystack.Width - rx, 0, haystack.Width - rx);
				int rh = Math.Clamp(height ?? haystack.Height - ry, 0, haystack.Height - ry);
				return new Rectangle(rx, ry, rw, rh);
			}

			// The needle's bitmap, and whether it is a copy the caller disposes. Another Image is read where it lies;
			// this one is copied, because the finder holds its bitmap locked.
			private (Bitmap bmp, bool owned) NeedleBitmap(object needle)
				=> needle is KeysharpImage image && !ReferenceEquals(image, this)
				   ? (image.PrepareForPixelAccess(), false)
				   : (LoadFromSource(needle).bmp, true);

			// A {X, Y} match object with own properties.
			private static KeysharpObject MakePoint(long x, long y)
			{
				var o = new KeysharpObject();
				o.DefinePropInternal("X", new OwnPropsDesc(x));
				o.DefinePropInternal("Y", new OwnPropsDesc(y));
				return o;
			}

			// A {X, Y, Color} pixel-match object; Color is the pixel's full 0xAARRGGBB.
			private static KeysharpObject MakePixel(long x, long y, long color)
			{
				var o = new KeysharpObject();
				o.DefinePropInternal("X", new OwnPropsDesc(x));
				o.DefinePropInternal("Y", new OwnPropsDesc(y));
				o.DefinePropInternal("Color", new OwnPropsDesc(color));
				return o;
			}

			/// <summary>
			/// Materializes the image and copies its pixels into a freshly allocated <see cref="Buffer"/>,
			/// tightly packed and top-down (row stride = <c>Width * bytesPerPixel</c>), ready to hand to a
			/// native imaging/OCR library through DllCall (e.g. Tesseract's <c>SetImage</c>).
			/// <paramref name="bytesPerPixel"/> selects the layout:
			/// <list type="bullet">
			///   <item><c>4</c> (default): 32-bit color in R, G, B, A byte order (the layout Leptonica/Tesseract use
			///   for <c>bytes_per_pixel = 4</c>), preserving color and alpha. It is the default so that
			///   <c>img.SetPixelData(img.GetPixelData())</c> round-trips: <see cref="SetPixelData"/> and
			///   <see cref="FromBuffer"/> default to the same layout.</item>
			///   <item><c>1</c>: 8-bit grayscale, one luminance byte per pixel
			///   (<c>0.299 R + 0.587 G + 0.114 B</c>, rounded half up as in <see cref="Grayscale"/>).
			///   Unambiguous across byte orders — this is what
			///   Tesseract expects for <c>bytes_per_pixel = 1</c>, and what OCR engines threshold anyway.</item>
			/// </list>
			/// The returned Buffer owns its memory; keep a reference to it for as long as the native side
			/// reads from <c>buf.Ptr</c>. The pixel dimensions are this image's <see cref="Width"/>/<see cref="Height"/>.
			///
			/// <para>Pass <paramref name="buffer"/> to write into storage you already own instead of allocating
			/// a new Buffer, and that same object is returned. This is for the capture-in-a-loop case, where a
			/// fresh multi-megabyte Buffer per frame is the dominant cost: allocate once and pass it back each
			/// time (<c>data := img.GetPixelData(4, data)</c>). It may be a <see cref="Buffer"/> or any object
			/// exposing script-visible <c>Ptr</c> and <c>Size</c> properties, the same duck typing
			/// <see cref="SetPixelData"/> accepts. It must hold at LEAST <c>Width * Height * bytesPerPixel</c>
			/// bytes (a ValueError otherwise); exactly that many are written, from the start, and anything
			/// beyond is left alone — so one buffer sized for the largest capture can serve smaller ones too.</para>
			/// </summary>
			public object GetPixelData(object bytesPerPixel = null, object buffer = null)
			{
				ThrowIfDisposed();

				if (!bytesPerPixel.CoerceInt(out var bpp, 4))
					return DefaultObject;

				if (bpp != 1 && bpp != 4)
					return Errors.ValueErrorOccurred("GetPixelData supports only 1 (grayscale) or 4 (RGBA) bytes per pixel.");

				var bmp = PrepareForPixelAccess();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to read.");

				int w = bmp.Width, h = bmp.Height;
				long need = (long)w * h * bpp;

				if (buffer == null)
				{
					var buf = new Buffer(need);

					unsafe
					{
						WritePixelData(bmp, (byte*)buf.Ptr, bpp);
					}

					return buf;
				}

				if (!Reflections.TryGetPtrProperty(buffer, out long addr) || !Reflections.TryGetSizeProperty(buffer, out long have))
					return Errors.ValueErrorOccurred("GetPixelData requires a Buffer or an object with Ptr and Size properties.");

				// At least, not exactly: the caller supplies scratch space, not a description of the image
				// (which is what makes SetPixelData's exact-size check the right one there). The valid extent
				// is always Width * Height * bytesPerPixel, which the caller can compute from this image.
				if (have < need)
					return Errors.ValueErrorOccurred($"GetPixelData needs at least {need} bytes for the current {w}x{h} image at {bpp} bytes per pixel but the buffer holds {have}.");

				unsafe
				{
					WritePixelData(bmp, (byte*)new nint(addr), bpp);
				}

				return buffer;
			}

			/// <summary>
			/// Overwrites this image's pixels from raw pixel bytes — the inverse of <see cref="GetPixelData"/>.
			/// <paramref name="data"/> is a <see cref="Buffer"/> (or any object exposing script-visible <c>Ptr</c>
			/// and <c>Size</c> properties, the AHK duck-typing convention). It must describe exactly
			/// <c>Width * Height * bytesPerPixel</c> bytes for the image's current (materialized) dimensions (a
			/// ValueError otherwise); it is not resized. <paramref name="bytesPerPixel"/> (default 4) selects the source layout, matching
			/// GetPixelData: <c>1</c> = 8-bit grayscale (each byte becomes an opaque gray R=G=B=byte, A=255),
			/// <c>4</c> = R, G, B, A byte order. The change is applied immediately (like <see cref="SetPixel"/>).
			/// Returns this image.
			///
			/// <para>On Windows the pixels are written in place through a 32bpp lock. On the Eto backends
			/// (Linux/macOS) the current bitmap may be a 3-byte-per-pixel Pixbuf, so the result is written into a
			/// fresh 32bpp canvas that replaces the current image rather than writing 4-byte pixels into 3bpp
			/// storage; the observable result (dimensions and pixels) is identical.</para>
			/// </summary>
			public object SetPixelData(object data, object bytesPerPixel = null)
			{
				ThrowIfDisposed();

				// A borrowed base is an Overlay canvas: the backing's presentable memory, which this must neither
				// replace (the Eto path swaps the bitmap) nor silently rewrite (the Windows path writes in place and
				// would leave the presented pixels changed with no damage recorded). Refused up front so both
				// branches are covered and nothing is allocated first.
				if (borrowedSurface != null)
					return Errors.ValueErrorOccurred(
						"An Overlay canvas cannot have its pixel data replaced; draw onto it instead.");

				// Accept a Buffer OR any object with Ptr/Size properties (duck-typed, like StrGet): the shared
				// Reflections helpers read a Buffer directly and fall back to a script-visible Ptr/Size otherwise.
				if (!Reflections.TryGetPtrProperty(data, out long addr) || !Reflections.TryGetSizeProperty(data, out long have))
					return Errors.ValueErrorOccurred("SetPixelData requires a Buffer or an object with Ptr and Size properties.");

				if (!bytesPerPixel.CoerceInt(out var bpp, 4))
					return DefaultObject;

				if (bpp != 1 && bpp != 4)
					return Errors.ValueErrorOccurred("SetPixelData supports only 1 (grayscale) or 4 (RGBA) bytes per pixel.");

				nint ptr = new nint(addr);//TryGetPtrProperty already rejected a null (0) address.

				var bmp = PrepareForPixelAccess();

				if (bmp == null)
					return Errors.ValueErrorOccurred("There is no image to write.");

				int w = bmp.Width, h = bmp.Height;
				long need = (long)w * h * bpp;

				if (have != need)
					return Errors.ValueErrorOccurred($"SetPixelData needs exactly {need} bytes for the current {w}x{h} image but the buffer holds {have}.");

#if WINDOWS
				// LockBits(Format32bppArgb) exposes a 32bpp view of any source format, so writing in place is safe.
				unsafe
				{
					ImageHelper.WriteBufferToBitmap(bmp, (byte*)ptr, bpp);
				}
#else
				// Eto's Lock() exposes the bitmap's native storage, which for a loaded 24-bit image is a 3bpp
				// Pixbuf; writing 4-byte pixels into that would corrupt/overrun it. Since the buffer describes
				// every pixel anyway, write into a fresh guaranteed-32bpp canvas and swap it in as the new base
				// rather than writing in place. Eto retains no Graphics, and the queue is already empty.
				var canvas = ImageHelper.NewArgbCanvas(w, h);

				unsafe
				{
					ImageHelper.WriteBufferToBitmap(canvas, (byte*)ptr, bpp);
				}

				baseBitmap.Dispose();
				baseBitmap = canvas;
				SyncGcPressure();
#endif
				return this;
			}

			#endregion

			#region Internals

			// Reads a bitmap's pixels straight into dst in the requested layout (bpp 1 = 8-bit grayscale,
			// 4 = 32-bit RGBA), converting each pixel in a single pass with no intermediate ARGB array.
			// dst must point at w*h*bpp writable bytes. The cross-platform lock/translate scaffolding
			// mirrors ImageFinder so the two stay consistent across backends.
			private static unsafe void WritePixelData(Bitmap bmp, byte* dst, int bpp)
			{
				int w = bmp.Width, h = bmp.Height;
#if WINDOWS
				var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

				try
				{
					// Format32bppArgb stores BGRA in memory, so a little-endian uint read yields 0xAARRGGBB.
					var basePtr = (byte*)data.Scan0;

					for (var y = 0; y < h; y++)
					{
						var row = (uint*)(basePtr + (nint)y * data.Stride);
						var dstRow = y * w;

						if (bpp == 4)
						{
							SwapRedBlue(row, (uint*)dst + dstRow, w);
							continue;
						}

						for (var x = 0; x < w; x++)
							EmitPixel(dst, dstRow + x, row[x], bpp);
					}
				}
				finally
				{
					bmp.UnlockBits(data);
				}

#else
				// Force 32bpp first so the 4-byte reads are valid (Pixbuf is 3bpp for 24-bit images). A 3bpp
				// source becomes opaque RGBA; an already-4bpp bitmap (possibly premultiplied, with real alpha)
				// passes through unchanged, so the per-pixel translate below still handles its premultiplication.
				var bmp32 = ImageHelper.EnsureOpaque32Bpp(bmp);

				try
				{
					using var data = bmp32.Lock();
					var basePtr = (byte*)data.Data;
					var stride = data.ScanWidth;
					var srcBpp = data.BytesPerPixel;

					// TranslateDataToArgb is a per-pixel virtual call reordering the backend's channel layout
					// (Gtk RGBA vs Cocoa BGRA) and un-premultiplying alpha. Skip it only when it is a genuine
					// no-op. Probe once with a partially-transparent marker whose four bytes are all distinct: any
					// channel swap changes it, and any un-premultiplication changes the RGB of a non-opaque pixel,
					// so an unchanged result proves the stored layout already is straight 0xAARRGGBB and the
					// per-pixel call is redundant. An A=255 marker would hide premultiplication — and a 4bpp bitmap
					// with real alpha reaches here un-forced, since EnsureOpaque32Bpp only converts 3bpp storage.
					const int marker = unchecked((int)0x80112233);
					var identity = srcBpp == 4 && data.TranslateDataToArgb(marker) == marker;

					for (var y = 0; y < h; y++)
					{
						var row = basePtr + (long)y * stride;
						var dstRow = y * w;

						for (var x = 0; x < w; x++)
						{
							var raw = *(int*)(row + x * srcBpp);
							EmitPixel(dst, dstRow + x, (uint)(identity ? raw : data.TranslateDataToArgb(raw)), bpp);
						}
					}
				}
				finally
				{
					if (!ReferenceEquals(bmp32, bmp))
						bmp32.Dispose();
				}

#endif
			}

#if WINDOWS
			// Format32bppArgb rows hold B, G, R, A bytes; RGBA output swaps the first and third byte of each pixel.
			private static unsafe void SwapRedBlue(uint* src, uint* dst, int width)
			{
				var x = 0;

				// The shuffle indices are written inline: the JIT emits a single byte shuffle only for constant indices.
				if (Vector128.IsHardwareAccelerated)
					for (; x <= width - 4; x += 4)
						Vector128.Shuffle(Vector128.Load((byte*)(src + x)),
							Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15)).Store((byte*)(dst + x));

				for (; x < width; x++)
				{
					var p = src[x];
					dst[x] = (p & 0xFF00FF00u) | ((p >> 16) & 0xFFu) | ((p & 0xFFu) << 16);
				}
			}

#endif
			// Writes one 0xAARRGGBB pixel into dst at pixel index idx in the bpp layout: grayscale uses the
			// rounded luminance shared with Grayscale; RGBA writes R,G,B,A in that byte order. Aggressively inlined
			// to remove the call overhead; the bpp test then runs inline per pixel but is perfectly predicted
			// (constant for the whole call), so it costs effectively nothing.
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private static unsafe void EmitPixel(byte* dst, int idx, uint p, int bpp)
			{
				if (bpp == 1)
				{
					uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
					dst[idx] = (byte)ImageHelper.Grayscale(r, g, b);
				}
				else
				{
					var o = idx * 4;
					dst[o]     = (byte)((p >> 16) & 0xFF); // R
					dst[o + 1] = (byte)((p >> 8) & 0xFF);  // G
					dst[o + 2] = (byte)(p & 0xFF);         // B
					dst[o + 3] = (byte)((p >> 24) & 0xFF); // A
				}
			}

			// Captures a screen rectangle and wraps it, recording the HiDPI capture scale. Coordinates
			// are always absolute screen coordinates; the Pixel CoordMode is deliberately not applied
			// (see FromRect) so every capture factory shares one coordinate convention.
			private static object CaptureRect(int x, int y, int w, int h, string failMsg)
			{
				var bmp = GuiHelper.GetScreen(x, y, w, h);

				if (bmp == null)
					return Errors.ErrorOccurred(failMsg);

				double sx = w > 0 ? (double)bmp.Width / w : 1.0;
				double sy = h > 0 ? (double)bmp.Height / h : 1.0;
				return Wrap(bmp, sx, sy, originX: x, originY: y);
			}

			private static object Wrap(Bitmap bmp, double sx = 1.0, double sy = 1.0, string failMsg = null, int originX = 0, int originY = 0)
			{
				if (bmp == null)
					return Errors.ValueErrorOccurred(failMsg ?? "Failed to create the image.");

				var img = new KeysharpImage { baseBitmap = bmp, scaleX = sx, scaleY = sy, originX = originX, originY = originY };
				img.SyncGcPressure();
				return img;
			}

			/// <summary>Wraps a bitmap this class will own as an Image. Internal so the Clipboard class can build the
			/// Image its getter returns without a second copy of the wrapping.</summary>
			internal static object WrapBitmap(Bitmap bmp) => Wrap(bmp);

			// Resolves an arbitrary script source to a freshly-owned bitmap plus its capture scale.
			// Accepts another Image, a file path, or a native bitmap handle.
			internal static (Bitmap bmp, double sx, double sy) LoadFromSource(object source)
			{
				if (source is KeysharpImage img)
				{
					var b = img.PrepareForRead();
					return (b == null ? null : new Bitmap(b), img.scaleX, img.scaleY);
				}

				if (source is string s)
				{
					var (loaded, icon) = ImageHelper.LoadImage(s, 0, 0, 0L, exactPixels: true);
					(icon as IDisposable)?.Dispose();
					return (loaded, 1.0, 1.0);
				}

				// A backend bitmap, which is what Clr interop (and Keysharp's own internals) hand out. Without this
				// it would fall through to the handle branch below and be read as a nonsense pointer.
				if (source is Bitmap bitmap)
					return (new Bitmap(bitmap), 1.0, 1.0);

				// Treat anything else as a native bitmap handle. Lenient: a non-numeric source just yields a null
				// handle, which the caller reports through its own "could not create an image" ValueError.
				_ = source.TryCoerceLong(out var handleValue);
				var handle = (nint)handleValue;

				if (handle != 0)
				{
					try { return (ImageHelper.GetBitmapFromHBitmap(handle), 1.0, 1.0); }
					catch { return (null, 1.0, 1.0); }
				}

				return (null, 1.0, 1.0);
			}

			// Applies queued work in order, folding each result into the base as soon as it is produced, so the draws
			// after a transform keep reusing the base's Graphics. Only a lazy image queues, and it owns its base. If an
			// op throws, what already ran stays; draw arguments are validated before queueing, so that is a backend
			// failure.
			private Bitmap Materialize()
			{
				if (disposed || baseBitmap == null)
					return null;

				if (pending.Count == 0)
					return baseBitmap;

				try
				{
					foreach (var op in pending)
					{
						var next = op(baseBitmap);

						if (next != null && !ReferenceEquals(next, baseBitmap))
						{
							ReleaseLiveGraphics();
							baseBitmap.Dispose();
							baseBitmap = next;
						}
					}
				}
				finally
				{
					pending.Clear();
					DisposePendingResources();
					SyncGcPressure();
				}

				return baseBitmap;
			}

			/// <summary>
			/// The drawing view of an <see cref="OverlaySurface"/>: a live surface over pixels this image does not
			/// own, because they belong to the platform backing that will present them. Every draw goes straight
			/// into those pixels — no lazy queue, no defensive copy, and no bitmap swap (the surface has to stay
			/// the same object, because the compositor may be reading it) — and records itself in the surface's
			/// damage list, so a present can transfer only what changed.
			/// </summary>
			internal static KeysharpImage FromExistingSurface(OverlaySurface surface)
			{
				if (surface?.Bitmap == null)
					return null;

				return new KeysharpImage
				{
					baseBitmap = surface.Bitmap,
					eagerDraw = true,
					ownsBitmap = false,
					borrowedSurface = surface,
					// Shared with the surface, not owned: the backing reads it to decide what to transfer, and
					// clears it only once a present has actually reached the screen.
					damage = surface.Damage,
					drawn = new DamageList(),
				};
			}

			// An eager image is a live drawing surface (used by an Overlay canvas): every draw op is applied
			// straight to baseBitmap with no lazy queue. Set once after bitmap creation; an eager image never
			// queues, and it refuses transforms.
			//
			// Strictly "when does a draw happen", nothing more. Whether the pixels are ours is `ownsBitmap`,
			// and whether anything presents them is `damage`; an Overlay canvas has all three, and they are
			// independently answerable.
			internal bool eagerDraw;

			// Enqueues a draw op, which mutates the bitmap it is given and returns it, or returns a replacement (a
			// lazy Clear of a base it cannot clear in place). On an eager surface the op is applied to baseBitmap now.
			private void QueueDraw(Func<Bitmap, Bitmap> op)
			{
				if (eagerDraw)
				{
					if (baseBitmap == null)
						return;

					Bitmap result;

					try { result = op(baseBitmap); }   // in-place ops mutate and return baseBitmap
					catch
					{
						DamageAll();
						throw;
					}

					if (!ReferenceEquals(result, baseBitmap))
					{
						// A borrowed surface is the backing's presentable memory; on Windows it is the DIB section.
						// Swapping it would leave the backing presenting stale pixels. Every eager operation must
						// draw in place, so reaching here is a Keysharp bug, not something a script can provoke —
						// raised as a script error all the same, because this is a script-visible class and a bare
						// CLR exception out of one escapes every handler a script is able to write.
						if (!ownsBitmap)
						{
							result.Dispose();
							_ = Errors.ValueErrorOccurred(
								"An Overlay canvas draw op replaced its bitmap; canvas ops must mutate in place.");
							return;
						}

						// The live Graphics is bound to the bitmap being replaced.
						ReleaseLiveGraphics();
						baseBitmap.Dispose();
						baseBitmap = result;
						SyncGcPressure();
					}

					return;
				}

				pending.Add(op);
			}

			/// <summary>
			/// Queues a whole-bitmap transform (Scale/Rotate/Flip/Crop/Resize) or filter (Grayscale/Alpha/
			/// Brightness/Contrast) — an op that returns a new bitmap rather than mutating the one it is given.
			///
			/// These deliberately do not go through <see cref="QueueDraw"/>, whose live-surface branch applies
			/// its op immediately. Mixing the two would invert their order: a draw queued after a transform
			/// would be painted first, onto the untransformed pixels, and then the transform would be replayed
			/// over the result. A borrowed base cannot be replaced because it is compositor-owned memory.
			/// A live surface therefore refuses transforms.
			/// </summary>
			/// <returns>False when the transform was refused, so a caller leaves its origin/scale metadata alone:
			/// in non-throwing error mode the refusal returns instead of unwinding.</returns>
			private bool QueueTransform(Func<Bitmap, Bitmap> op)
			{
				if (eagerDraw)
				{
					_ = Errors.ValueErrorOccurred(
						"A live drawing surface (an Overlay canvas) cannot be scaled, rotated, cropped or filtered.");
					return false;
				}

				pending.Add(op);
				return true;
			}

			// A System.Drawing.Bitmap's real weight is unmanaged GDI memory; the managed KeysharpImage/Bitmap
			// wrappers are a few dozen bytes, so the GC sees almost nothing, feels no pressure, and doesn't run —
			// the finalizer that disposes these bitmaps (via DestructorPump) then never fires and undisposed
			// captures pile into a rising high-water mark. Registering the bitmaps' byte size as GC memory
			// pressure lets the GC schedule collection on cue, so the automatic cleanup reclaims them and scripts
			// need no manual Dispose. Self-balancing: it reconciles the amount already registered against the live
			// bitmaps on every state change, and Dispose (disposed => 0 bytes) drives it back to zero.
			private void SyncGcPressure()
			{
				long bytes = 0;

				if (!disposed)
				{
					// A borrowed canvas is deliberately excluded: collecting this wrapper would not free those
					// bytes (its owner holds them for the overlay's lifetime), so charging for them would only
					// nag the GC into collections that can reclaim nothing.
					if (baseBitmap != null && ownsBitmap)
						bytes += BitmapByteEstimate(baseBitmap);
				}

				if (bytes == gcPressure)
					return;

				if (bytes > gcPressure)
					GC.AddMemoryPressure(bytes - gcPressure);
				else
					GC.RemoveMemoryPressure(gcPressure - bytes);

				gcPressure = bytes;
			}

			// Approximate unmanaged footprint of a bitmap (32bpp: width * height * 4). Only a GC hint, so the
			// exact stride/pixel format is irrelevant.
			private static long BitmapByteEstimate(Bitmap bmp) => (long)bmp.Width * bmp.Height * 4;

			private static RectangleF MakeRectF(double x, double y, double w, double h)
				=> new ((float)x, (float)y, (float)w, (float)h);

			// Rendering quality is not part of a font: DrawText applies it to the Graphics.
			private readonly record struct ImageFontKey(string Name, double? Size, int? Weight, bool? Italic, bool? Underline,
				bool? Strike);

			// Keyed on parsed options, so only valid ones reach it. A queued draw or an Eto toolkit handler can still
			// hold a cached font, so the cache is bounded by dropping its entries to their finalizers, never by disposing.
			private static readonly System.Collections.Concurrent.ConcurrentDictionary<ImageFontKey, NativeFont> fontCache = new ();
			private const int MaxCachedFonts = 256;

			// Parses and validates DrawText/MeasureText font options and returns the font for them. False after an
			// error the script continued, and the caller then returns at once.
			private static bool TryGetImageFont(string options, string name, out NativeFont font, out int? quality)
			{
				font = null;
				quality = null;

				if (Conversions.ParseFontOptions(options, name, strict: true) is not { } parsed)
					return false;

				if (parsed.color.HasValue)
				{
					_ = Errors.ValueErrorOccurred("Font colour belongs in DrawText's color argument, not in the font options.");
					return false;
				}

				quality = parsed.quality;
				var key = new ImageFontKey(parsed.name, parsed.size, parsed.weight, parsed.italic, parsed.underline, parsed.strike);
#if WINDOWS
				var cacheable = true;
#else
				// Eto rejects a rendering quality when it creates the font, so options with one never take a cached font.
				var cacheable = parsed.quality is not > 0;
#endif

				if (cacheable && fontCache.TryGetValue(key, out font))
					return true;

#if WINDOWS
				using var standard = new NativeFont(System.Drawing.FontFamily.GenericSansSerif, 10);
#else
				var standard = SystemFonts.Default(10);
#endif
				var created = Conversions.ApplyFont(standard, parsed, forImage: true);

				// The standard font comes back only after an error the script continued.
				if (ReferenceEquals(created, standard))
					return false;

				if (fontCache.Count >= MaxCachedFonts)
					fontCache.Clear();

				font = fontCache.GetOrAdd(key, created);

				if (!ReferenceEquals(font, created))
					created.Dispose();

				return true;
			}

#if WINDOWS
			private static void ConfigureFontGraphics(Graphics graphics, int? quality)
			{
				graphics.TextRenderingHint = quality switch
				{
					0 => System.Drawing.Text.TextRenderingHint.SystemDefault,
					1 => System.Drawing.Text.TextRenderingHint.SingleBitPerPixel,
					2 or 3 => System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit,
					5 => System.Drawing.Text.TextRenderingHint.ClearTypeGridFit,
					_ => System.Drawing.Text.TextRenderingHint.AntiAliasGridFit
				};
			}
#endif

			// Formats the capture scale for the window title as a percentage: a single value when X and Y
			// match (the usual case), otherwise "sx%/sy%". 1.0 -> "100%", 2.0 -> "200%", 1.5 -> "150%".
			private static string FormatScalePercent(double sx, double sy)
			{
				static string P(double v) => $"{(long)Math.Round(v * 100)}%";
				return sx == sy ? P(sx) : $"{P(sx)}/{P(sy)}";
			}

			// Parses a color argument into packed 0xAARRGGBB. "" / null is fully transparent (alpha 0). A value
			// whose high byte is set (> 0xFFFFFF) is taken as an explicit 0xAARRGGBB; otherwise it is a 0xRRGGBB
			// and made fully opaque. NOTE: for a NUMERIC argument, 0xFF0000 and 0x00FF0000 are the same integer,
			// so a transparent color (alpha 0) cannot be expressed numerically — it would read as opaque RRGGBB.
			// Use "", the color name "Transparent" (KnownColor.Transparent parses to alpha 0 via TryParseColor),
			// or an 8-hex-digit STRING (e.g. "0x80FF0000") when you need a non-opaque alpha.
			// Any other string, and a NaN or infinite number, raises a ValueError, as AutoHotkey's colour options do;
			// `name` names the parameter in it.
			// False when the argument raised an error the script continued, and the caller then returns at once.
			private static bool TryParseColorArg(object o, out int argb, int defaultArgb = 0, bool allowTransparentEmpty = true,
				string name = null)
			{
				argb = 0;

				if (o == null)
				{
					argb = allowTransparentEmpty ? 0 : defaultArgb;
					return true;
				}

				if (o is long or int || o is double number && double.IsFinite(number))
				{
					_ = o.TryCoerceLong(out var rawLong);
					var raw = (uint)rawLong;
					argb = unchecked((int)(raw > 0xFFFFFFu ? raw : 0xFF000000u | (raw & 0xFFFFFFu)));
					return true;
				}

				if (o is not double)
				{
					if (!o.CoerceString(out var s))
						return false;

					if (s.Length == 0)
					{
						argb = allowTransparentEmpty ? 0 : defaultArgb;
						return true;
					}

					if (Conversions.TryParseColor(s, out var c))
					{
						argb = c.ToArgb();
						return true;
					}

					if (s.ParseLong() is long v)
					{
						var parsed = (uint)v;
						argb = unchecked((int)(parsed > 0xFFFFFFu ? parsed : 0xFF000000u | (parsed & 0xFFFFFFu)));
						return true;
					}
				}

				_ = Errors.ValueErrorOccurred(name == null ? "Invalid color." : $"{name} must be a valid color.", o);
				return false;
			}

			private void DisposePendingResources()
			{
				foreach (var resource in pendingResources)
				{
					try { resource?.Dispose(); } catch { }
				}

				pendingResources.Clear();
			}

			// Number of still-parked draw-source resources, exposed for tests to prove the eager DrawImage path
			// disposes eagerly (never parks) rather than leaking a bitmap per call.
			internal int PendingResourcesCount => pendingResources.Count;

			// Raises a ValueError from every public method/property once this image has been disposed, so a
			// use-after-Dispose fails loudly and consistently instead of silently returning 0/"". Errors.*Occurred
			// throws when the error is not suppressed by an OnError handler (the normal case), matching the rest
			// of the class's error contract.
			private void ThrowIfDisposed()
			{
				if (disposed)
					_ = Errors.ValueErrorOccurred("This Image has been disposed.");
			}

			public object Dispose()
			{
				// An Overlay canvas belongs to its Overlay. Disposing it from a script would not free the pixels
				// (the surface owns those) but would leave the overlay holding a disposed image. Destroy is the
				// operation that owns canvas teardown.
				if (borrowedSurface != null)
					return Errors.ValueErrorOccurred(
						"An Overlay canvas is owned by its Overlay; call Overlay.Destroy instead.");

				((IDisposable)this).Dispose();
				return DefaultObject;
			}

			void IDisposable.Dispose()
			{
				if (disposed)
					return;

				disposed = true;
				// Before the bitmap: the Graphics is attached to it.
				ReleaseDrawState();

				// A borrowed canvas bitmap belongs to the OverlaySurface that created it, which frees the GDI+
				// wrapper and the memory underneath it in the one order that is safe.
				if (ownsBitmap)
					baseBitmap?.Dispose();

				baseBitmap = null;
				borrowedSurface = null;
				pending.Clear();
				DisposePendingResources();
				SyncGcPressure();   // disposed => reconciles to 0, releasing all registered pressure
			}

			#endregion
		}
	}
}
