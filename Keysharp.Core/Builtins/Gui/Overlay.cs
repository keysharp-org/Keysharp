using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A screen overlay backed by a raster canvas — click-through and always-on-top by default. Draw onto it with
		/// the same shape/text primitives as <see cref="KeysharpImage"/> (<c>DrawRect</c>, <c>FillRect</c>, <c>DrawLine</c>,
		/// <c>DrawEllipse</c>, <c>FillEllipse</c>, <c>DrawText</c>, <c>Clear</c>) or stamp an existing image with
		/// <see cref="DrawImage"/> / <see cref="Update"/>, then <see cref="Show"/> it on screen. Use <see cref="Update"/>
		/// to replace a live overlay's image, position, and native screen-space size in one backing operation. Drawing
		/// while the overlay is visible updates it live. The canvas is owned by the overlay; <see cref="Destroy"/> (or dropping
		/// all references) frees it. This is the single cross-platform overlay primitive that <c>Highlight</c> and,
		/// on Linux/macOS, <c>ToolTip</c> build on.
		/// <para>By default each draw op auto-repaints (one upload per op). Wrap a burst of primitives in
		/// <see cref="BeginDraw"/>/<see cref="EndDraw"/> — or <see cref="Redraw"/> — to composite a whole HUD frame and
		/// upload it exactly once. Set <see cref="ClickThrough"/> to <c>false</c> to make the overlay receive mouse
		/// input (an interactive HUD) instead of passing clicks through to the windows beneath it, and register
		/// mouse handlers with <see cref="OnEvent"/> (Click/DoubleClick/ContextMenu/MouseMove).</para>
		/// </summary>
		[UserDeclaredName("Overlay")]
		public class KeysharpOverlay : KeysharpObject
		{
			private const uint OverlayIdPrefix = 0x1000_0000u;
			private const uint IdMask = 0x0FFF_FFFFu;
			private static int nextOverlayId;

			// The drawable canvas (reuses Image's shape/text primitives so no drawing logic is duplicated here).
			private KeysharpImage canvas;
			private uint overlayId;
			private int x;
			private int y;
			private int w;   // explicit native width/height; 0 means "use the image pixel size"
			private int h;
			private long opacity = 255;   // whole-overlay alpha multiplier applied at upload time
			private bool requestedVisible;
			private bool isMapped;
			private bool clickThrough = true;   // default: transparent to mouse input (Highlight/ToolTip depend on this)
			private int suspendCount;           // > 0 while a BeginDraw/EndDraw (or Redraw) batch is deferring uploads
			private bool redrawing;

			private uint OverlayId => overlayId != 0
				? overlayId
				: overlayId = OverlayIdPrefix | ((uint)Interlocked.Increment(ref nextOverlayId) & IdMask);

			private (int ScreenW, int ScreenH) CurrentGeometry
				=> ResolveGeometry(w, h, (int)(canvas?.Width ?? 0), (int)(canvas?.Height ?? 0));

			// W/H are always native screen units. A raster image with no explicit W/H uses one native unit per pixel;
			// generated canvases ask the platform renderer for their actual pixel dimensions.
			private static (int ScreenW, int ScreenH) ResolveGeometry(
				int authoredW, int authoredH, int imageW, int imageH)
				=> (authoredW > 0 ? authoredW : Math.Max(0, imageW),
					authoredH > 0 ? authoredH : Math.Max(0, imageH));

			public KeysharpOverlay(params object[] args) : base(args) { }

			/// <summary>Overlay(x?, y?, w?, h?) stores the geometry; the canvas is created on the first
			/// draw (or Update), and nothing is shown until Show. X/Y/W/H are native screen coordinates: PMv2/X11
			/// desktop pixels, Cocoa points, or Wayland logical units. The renderer chooses the pixel size of generated
			/// canvases; supplied images already carry their raster dimensions.</summary>
			// `new`, not `override`: construction dispatches by name, so the real signature is declared here and
			// arity/defaults/named binding follow from it (see Buffer.__New and Any's constructor). A fifth
			// argument is now simply "Too many arguments" from the arity check, replacing the hand-written guard.
			public object __New(object X = null, object Y = null, object Width = null, object Height = null)
			{
				if (X != null) x = X.Ai();
				if (Y != null) y = Y.Ai();
				if (Width != null) w = Width.Ai();
				if (Height != null) h = Height.Ai();

				return DefaultObject;
			}

			#region Properties

			public object X { get => (long)x; set { if (RejectRedrawMutation()) return; x = value.Ai(); MoveLive(); } }
			public object Y { get => (long)y; set { if (RejectRedrawMutation()) return; y = value.Ai(); MoveLive(); } }

			/// <summary>Overlay width in native screen/draw units. Changing
			/// it resizes the live
			/// surface; the existing canvas is KEPT and the backing STRETCHES it to the new size (a display-time scale,
			/// not a bitmap rebuild), so a solid-fill or tile overlay can grow every frame cheaply without discarding
			/// its content. Draw ops keep targeting the canvas at its authored resolution — to draw crisply at a larger
			/// size, redraw the content or recreate the overlay.</summary>
			public object Width
			{
				get
				{
					if (w > 0)
						return (long)w;

					return canvas != null ? (long)CurrentGeometry.ScreenW : 0L;
				}
				set { if (RejectRedrawMutation()) return; w = value.Ai(); MoveLive(); }
			}

			/// <summary>Overlay height in native screen/draw units. Changing
			/// it stretches the live
			/// surface to the new size (the canvas is kept, not rebuilt) — see <see cref="Width"/>.</summary>
			public object Height
			{
				get
				{
					if (h > 0)
						return (long)h;

					return canvas != null ? (long)CurrentGeometry.ScreenH : 0L;
				}
				set { if (RejectRedrawMutation()) return; h = value.Ai(); MoveLive(); }
			}

			/// <summary>Whole-overlay opacity, 0 (invisible) to 255 (opaque, default). Multiplies the
			/// per-pixel alpha at upload time; setting it on a visible overlay re-uploads with the new
			/// alpha, so an OSD can be faded in/out without redrawing its content.</summary>
			public object Opacity
			{
				get => opacity;
				set
				{
					if (RejectRedrawMutation()) return;
					var v = Math.Clamp(value.Al(), 0L, 255L);

					if (v == opacity)
						return;

					opacity = v;
					MaybeRefresh();
				}
			}

			/// <summary>Whether the overlay is transparent to mouse input (default true). Leave it true for a passive
			/// HUD/highlight so clicks reach the windows beneath; set it false to make the overlay RECEIVE mouse input
			/// (an interactive HUD). Changing it on a visible overlay re-applies the input mode immediately.</summary>
			public object ClickThrough
			{
				get => clickThrough;
				set
				{
					if (RejectRedrawMutation()) return;
					var v = value.Ab();

					if (v == clickThrough)
						return;

					clickThrough = v;
					MaybeRefresh();   // re-push so the backing toggles the live surface's input mode
				}
			}

			public object Visible => isMapped;

			// Return 0 without allocating an overlay id when nothing has been shown yet: a backing only exists once
			// an id has been allocated (on the first Show), so overlayId == 0 means there is no window/handle. Reading
			// the OverlayId property here instead would burn an id (Interlocked.Increment) for a handle that is 0.
			public object Hwnd => overlayId == 0 ? 0L : Platform.Overlay.GetImageOverlayHandle(overlayId).ToInt64();

			#endregion

			#region Draw batching

			/// <summary>Begins a draw batch: subsequent draw ops and property changes update the canvas but DEFER the
			/// on-screen upload until the matching <see cref="EndDraw"/> (or the end of a <see cref="Redraw"/>). The
			/// default is auto-repaint-per-op (one upload per primitive); batching composites a whole HUD frame and
			/// uploads it exactly once. Calls nest — each BeginDraw needs an EndDraw, and the upload happens when the
			/// outermost EndDraw runs. Returns this for chaining.</summary>
			public object BeginDraw()
			{
				if (RejectRedrawMutation()) return this;
				suspendCount++;
				return this;
			}

			/// <summary>Ends a draw batch started with <see cref="BeginDraw"/>. When the outermost batch closes, the
			/// accumulated frame is uploaded once (if the overlay is visible). Returns this for chaining.</summary>
			public object EndDraw()
			{
				if (RejectRedrawMutation()) return this;
				if (suspendCount > 0)
					suspendCount--;

				if (suspendCount == 0 && requestedVisible)
					Refresh();

				return this;
			}

			/// <summary>Builds a complete replacement canvas off-screen, passing this overlay to
			/// <paramref name="callback"/>, then commits its pixels and optional geometry in one platform update.
			/// Drawing uses local native screen units while backing-pixel density is selected automatically for the target.
			/// A drawing exception or failed upload preserves the previous frame and overlay state.</summary>
			public object Redraw(object callback, object newX = null, object newY = null, object newWidth = null, object newHeight = null)
			{
				if (RejectRedrawMutation()) return this;
				if (callback is not KeysharpFunc f)
					return Errors.ValueErrorOccurred("Overlay.Redraw requires a callable object.");

				var nextX = newX != null ? newX.Ai() : x;
				var nextY = newY != null ? newY.Ai() : y;
				var nextW = newWidth != null ? newWidth.Ai() : w;
				var nextH = newHeight != null ? newHeight.Ai() : h;
				var oldGeometry = CurrentGeometry;
				var screenW = nextW > 0 ? nextW : oldGeometry.ScreenW;
				var screenH = nextH > 0 ? nextH : oldGeometry.ScreenH;

				if (!TryCreateCanvas(new ScreenRect(nextX, nextY, screenW, screenH), out var replacement))
					return Errors.ValueErrorOccurred("Overlay.Redraw requires a positive final width and height.");

				var previousCanvas = canvas;
				var previousX = x;
				var previousY = y;
				var previousW = w;
				var previousH = h;
				var previousOpacity = opacity;
				var previousClickThrough = clickThrough;
				var previousSuspend = suspendCount;
				var committed = false;

				// Draw into a private target-sized canvas. The live backing and previous model are untouched until the
				// final upload succeeds, so a resize never publishes an empty/intermediate surface.
				canvas = replacement;
				x = nextX;
				y = nextY;
				w = screenW;
				h = screenH;
				suspendCount = previousSuspend + 1;
				redrawing = true;

				try
				{
					_ = f.Call(this);
					replacement.Bake();

					if (x != nextX || y != nextY || w != screenW || h != screenH)
						return Errors.ValueErrorOccurred("Overlay.Redraw geometry must be supplied as arguments, not changed inside the callback.");

					var finalBounds = new ScreenRect(x, y, screenW, screenH);

					if (requestedVisible && previousSuspend == 0 && !TryUpload(replacement, finalBounds))
						return this;

					committed = true;
					previousCanvas?.Dispose();

					if (requestedVisible && previousSuspend == 0)
						isMapped = true;
				}
				finally
				{
					redrawing = false;
					suspendCount = previousSuspend;

					if (!committed)
					{
						canvas = previousCanvas;
						x = previousX;
						y = previousY;
						w = previousW;
						h = previousH;
						opacity = previousOpacity;
						clickThrough = previousClickThrough;
						replacement.Dispose();
					}
				}

				return this;
			}

			#endregion

			#region Drawing (delegates to the Image canvas, then repaints if shown)

			/// <summary>Fills the whole canvas. Omit <paramref name="color"/> or pass "" for transparent.</summary>
			public object Clear(object color = null) => Draw(() => canvas.Clear(color));

			public object DrawLine(object x1, object y1, object x2, object y2, object color = null, object thickness = null)
				=> Draw(() => canvas.DrawLine(x1, y1, x2, y2, color, thickness));

			public object DrawRect(object rx, object ry, object rw, object rh, object color = null, object thickness = null)
				=> Draw(() => canvas.DrawRect(rx, ry, rw, rh, color, thickness));

			public object FillRect(object rx, object ry, object rw, object rh, object color = null)
				=> Draw(() => canvas.FillRect(rx, ry, rw, rh, color));

			public object DrawEllipse(object rx, object ry, object rw, object rh, object color = null, object thickness = null)
				=> Draw(() => canvas.DrawEllipse(rx, ry, rw, rh, color, thickness));

			public object FillEllipse(object rx, object ry, object rw, object rh, object color = null)
				=> Draw(() => canvas.FillEllipse(rx, ry, rw, rh, color));

			public object DrawRoundRect(object rx, object ry, object rw, object rh, object radius, object color = null, object thickness = null)
				=> Draw(() => canvas.DrawRoundRect(rx, ry, rw, rh, radius, color, thickness));

			public object FillRoundRect(object rx, object ry, object rw, object rh, object radius, object color = null)
				=> Draw(() => canvas.FillRoundRect(rx, ry, rw, rh, radius, color));

			/// <summary>Queues text rendering; the font is given as Gui.SetFont-style
			/// <paramref name="options"/> ("s16 bold italic underline strike") plus a <paramref name="fontName"/>,
			/// exactly as in <see cref="KeysharpImage.DrawText"/>.</summary>
			public object DrawText(object text, object tx, object ty, object color = null, object options = null, object fontName = null)
				=> Draw(() => canvas.DrawText(text, tx, ty, color, options, fontName));

			/// <summary>Measures the size <paramref name="text"/> would occupy in the given font (same
			/// <paramref name="options"/>/<paramref name="fontName"/> convention as <see cref="DrawText"/>) and
			/// returns it as a <c>{w, h}</c> object, in the overlay's local draw units (so it composes with the
			/// coordinates passed to DrawText/DrawRect). Use it to centre or align text.</summary>
			public object MeasureText(object text, object options = null, object fontName = null)
			{
				var (mw, mh) = KeysharpImage.MeasureTextCore(text.As(), options.As(), fontName.As());
				return KeysharpImage.MakeSize(mw, mh);
			}

			/// <summary>Stamps another image (an Image, a file path, or a bitmap handle) onto the canvas.</summary>
			public object DrawImage(object image, object ix = null, object iy = null, object iw = null, object ih = null)
				=> Draw(() => canvas.DrawImage(image, ix, iy, iw, ih));

			/// <summary>Atomically replaces the canvas image and any supplied geometry (omit the geometry to just
			/// swap the image in place). The complete replacement is prepared off-screen and, when visible, handed
			/// to the platform in one upload; no blank canvas, intermediate move, or intermediate resize is
			/// published. A failed upload preserves both the previous on-screen frame and this overlay's previous
			/// state. The source (an Image, a file path, or a bitmap handle) is copied and remains owned by the
			/// caller. The image dimensions are its backing pixels; W/H are its native on-screen size. Update does
			/// not change visibility: call <see cref="Show"/> when staging into a hidden overlay.</summary>
			public object Update(object source, object newX = null, object newY = null, object newWidth = null,
						 object newHeight = null)
			{
				if (RejectRedrawMutation()) return this;
				var nextX = newX != null ? newX.Ai() : x;
				var nextY = newY != null ? newY.Ai() : y;
				var nextW = newWidth != null ? newWidth.Ai() : w;
				var nextH = newHeight != null ? newHeight.Ai() : h;
				// Do every fallible image operation before touching the live model. The old canvas remains owned by
				// this overlay and displayed by the backing until the final upload succeeds.
				if (!TryCopyImage(source, nameof(Update), out var replacement))
					return this;

				var nextGeometry = ResolveGeometry(nextW, nextH, (int)replacement.Width, (int)replacement.Height);
				SetDrawScale(replacement, (int)replacement.Width, (int)replacement.Height,
					nextGeometry.ScreenW, nextGeometry.ScreenH);

				var uploadNow = requestedVisible && suspendCount == 0;

				if (uploadNow && !TryUpload(
						replacement, new ScreenRect(nextX, nextY, nextGeometry.ScreenW, nextGeometry.ScreenH)))
				{
					replacement.Dispose();
					return this;
				}

				var previous = canvas;
				canvas = replacement;
				x = nextX;
				y = nextY;
				w = nextW;
				h = nextH;
				previous?.Dispose();

				if (uploadNow)
					isMapped = true;

				return this;
			}

			#endregion

			#region Events

			// Registered pointer handlers by canonical event name ("click", "doubleclick", "contextmenu",
			// "mousemove"). Each entry keeps the ORIGINAL script callback object for OnEvent(.., .., 0) removal
			// (converting again yields a different wrapper, so identity must be tested against what was passed)
			// and a CallbackRegistration whose active state holds script persistence, like other event hooks.
			// handlerGate guards the map: OnEvent mutates on a script thread while HandlePointerEvent snapshots
			// on the UI thread.
			private readonly object handlerGate = new ();
			private Dictionary<string, List<(object original, CallbackRegistration reg)>> eventHandlers;
			private bool sinkArmed;

			private static readonly string[] supportedEvents = ["click", "doubleclick", "contextmenu", "mousemove"];

			/// <summary>Registers <paramref name="callback"/> for a mouse event on this overlay, in the style of
			/// <c>Gui.OnEvent</c>. Events: <c>Click</c> (left button), <c>DoubleClick</c>, <c>ContextMenu</c>
			/// (right button) and <c>MouseMove</c>. The callback receives <c>(overlay, x, y)</c> with x/y in the
			/// overlay's local native units — the same units the draw ops use, so a hit-test against drawn
			/// shapes needs no conversion. <paramref name="addRemove"/>: 1 (default) = call after previously
			/// registered handlers, -1 = call before them, 0 = unregister the callback.
			/// <para>The overlay must not be click-through to receive mouse input: set
			/// <see cref="ClickThrough"/> := false, or the events never fire (input passes through to the
			/// windows beneath). Events require a backing with a client-side window (<see cref="Hwnd"/> != 0);
			/// a compositor-drawn overlay cannot receive input. Registered handlers keep the script persistent;
			/// <see cref="Destroy"/> removes them all.</para></summary>
			public object OnEvent(object eventName, object callback, object addRemove = null)
			{
				if (RejectRedrawMutation()) return this;
				var rawName = eventName.As();
				var name = rawName.ToLowerInvariant();

				if (System.Array.IndexOf(supportedEvents, name) < 0)
					return Errors.ValueErrorOccurred($"Overlay.OnEvent: unknown event \"{rawName}\". Supported: Click, DoubleClick, ContextMenu, MouseMove.");

				var mode = addRemove == null ? 1L : addRemove.Al();

				if (mode is not (1L or -1L or 0L))
					return Errors.ValueErrorOccurred("Overlay.OnEvent: AddRemove must be 1, -1 or 0.");

				var fo = Functions.GetKeysharpFunc(callback, null, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				var anyLeft = true;

				lock (handlerGate)
				{
					eventHandlers ??= new Dictionary<string, List<(object, CallbackRegistration)>>();

					if (!eventHandlers.TryGetValue(name, out var list))
						eventHandlers[name] = list = [];

					if (mode == 0L)
					{
						for (var i = list.Count - 1; i >= 0; i--)
						{
							if (ReferenceEquals(list[i].original, callback) || Equals(list[i].original, callback))
							{
								list[i].reg.Clear();   // releases the persistence hold
								list.RemoveAt(i);
							}
						}
					}
					else
					{
						var entry = (callback, new CallbackRegistration(fo, Script.TheScript?.EventScheduler, true));

						if (mode == -1L)
							list.Insert(0, entry);
						else
							list.Add(entry);
					}

					anyLeft = eventHandlers.Any(kv => kv.Value.Count > 0);
				}

				// Arm (or disarm) the platform sink outside the handler lock: the service applies it under its
				// own slot gate, and it survives backing recreation because it is stored by overlay id.
				if (anyLeft)
					EnsureSinkArmed();
				else
					DisarmSink();

				return this;
			}

			private void EnsureSinkArmed()
			{
				if (sinkArmed)
					return;

				// The service's id-keyed sink map (and the live backing) hold this delegate for as long as the
				// registration stands. Capture only a WEAK reference to the overlay: a strong capture would pin a
				// dropped overlay forever — __Delete could then never run, so an event-overlay abandoned without
				// Destroy would leak its canvas and keep holding script persistence, and a shown one would never
				// auto-hide on collection. With the weak target the normal drop-all-references lifecycle keeps
				// working; an event arriving after collection is a no-op until the destructor's Destroy clears
				// the registration.
				var weakSelf = new WeakReference<KeysharpOverlay>(this);
				Platform.Overlay.SetImageOverlayPointerSink(OverlayId, ev =>
				{
					if (weakSelf.TryGetTarget(out var overlay))
						overlay.HandlePointerEvent(ev);
				});
				sinkArmed = true;
			}

			private void DisarmSink()
			{
				if (!sinkArmed || overlayId == 0)
					return;

				Platform.Overlay.SetImageOverlayPointerSink(overlayId, null);
				sinkArmed = false;
			}

			// Removes every handler (releasing their persistence holds) and the platform sink — Destroy's path.
			private void ClearEventHandlers()
			{
				lock (handlerGate)
				{
					if (eventHandlers != null)
					{
						foreach (var list in eventHandlers.Values)
							foreach (var (_, reg) in list)
								reg.Clear();

						eventHandlers = null;
					}
				}

				DisarmSink();
			}

			// UI-thread entry: fans one backing pointer event out to that event's registered handlers, each on
			// its owning scheduler as a queued pseudo-thread (the same dispatch shape as WinEvent/Gui events).
			private void HandlePointerEvent(OverlayPointerEvent ev)
			{
				var name = ev.Kind switch
				{
					OverlayPointerKind.Click => "click",
					OverlayPointerKind.DoubleClick => "doubleclick",
					OverlayPointerKind.ContextMenu => "contextmenu",
					_ => "mousemove",
				};

				(object original, CallbackRegistration reg)[] snapshot;

				lock (handlerGate)
				{
					if (eventHandlers == null || !eventHandlers.TryGetValue(name, out var list) || list.Count == 0)
						return;

					snapshot = [.. list];
				}

				foreach (var (_, reg) in snapshot)
				{
					var scheduler = reg.OwnerScheduler ?? Script.TheScript?.EventScheduler;

					if (scheduler == null || scheduler.IsDisposed)
						continue;

					var r = reg;
					// One args array PER handler: a callback declaring a ByRef parameter writes into its argument
					// slots, which must not leak into the next handler's arguments.
					object[] args = [this, (long)ev.X, (long)ev.Y];
					_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => RunPointerHandler(scheduler, r, args));
				}
			}

			private static ScriptEventExecutionResult RunPointerHandler(ScriptEventScheduler scheduler, CallbackRegistration reg, object[] args)
			{
				using var thread = scheduler.StartPseudoThreadScope(0, false, false, false, ThreadKind.Event);

				if (!thread.Started)
					return thread.Result;

				try
				{
					_ = reg.Callback.Call(args);
				}
				catch (Exception ex)
				{
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
				}
				finally
				{
					Script.TheScript?.ExitIfNotPersistent();
				}

				return ScriptEventExecutionResult.Executed;
			}

			#endregion

			#region Show / Move / Hide / Destroy

			public object Show(object newX = null, object newY = null, object newWidth = null, object newHeight = null)
			{
				if (RejectRedrawMutation()) return this;
				if (newX != null) x = newX.Ai();
				if (newY != null) y = newY.Ai();
				if (newWidth != null) w = newWidth.Ai();
				if (newHeight != null) h = newHeight.Ai();

				if (!EnsureCanvas())
					return this;   // sizeless overlay: EnsureCanvas raised the error (throws in throw-mode); keep chaining otherwise

				// A resize just changes the displayed size; the backing STRETCHES the existing canvas to the new W/H
				// (see the W property), so growing a tile/fill overlay keeps its content instead of blanking it.
				requestedVisible = true;
				MaybeRefresh();
				return this;
			}

			public object Move(object newX = null, object newY = null, object newWidth = null, object newHeight = null)
			{
				if (RejectRedrawMutation()) return this;
				if (newX != null) x = newX.Ai();
				if (newY != null) y = newY.Ai();
				if (newWidth != null) w = newWidth.Ai();
				if (newHeight != null) h = newHeight.Ai();

				// The backing STRETCHES the existing canvas to the new W/H (see the W property) — a resize is a display
				// scale, not a bitmap rebuild — so a tile/fill overlay resized every frame keeps its content.
				MoveLive();
				return this;
			}

			public object Hide()
			{
				if (RejectRedrawMutation()) return this;
				requestedVisible = false;

				// Nothing was ever shown (no id/backing was allocated), so there is nothing to withdraw — and reading
				// the OverlayId property here would needlessly burn an id.
				if (overlayId == 0)
				{
					isMapped = false;
					return this;
				}

				// Only mark ourselves hidden once the platform CONFIRMS the surface is gone. If the withdraw
				// couldn't be confirmed (e.g. a dropped compositor hide), keep isMapped true so Visible stays
				// truthful and a later Hide re-attempts, instead of leaving a painted-but-"hidden" orphan.
				if (Platform.Overlay.TryHideImageOverlay(overlayId))
					isMapped = false;

				return this;
			}

			public object Destroy()
			{
				if (RejectRedrawMutation()) return DefaultObject;
				ClearEventHandlers();

				if (overlayId != 0)
				{
					// Try a graceful, confirm-gated withdraw first...
					_ = Hide();
					// ...then FORCE-reap the backing unconditionally. If Hide couldn't confirm the withdraw (a dropped
					// Wayland hide), the backing would otherwise stay mapped in OverlayService with no owner left to
					// retry — this disposes and removes it for good, distinct from the retryable Hide.
					Platform.Overlay.DisposeImageOverlay(overlayId);
				}

				// The surface and its canvas are being torn down regardless of any backing confirmation above, so
				// Visible must read false.
				requestedVisible = false;
				isMapped = false;
				canvas?.Dispose();
				canvas = null;
				return DefaultObject;
			}

			public override object __Delete() => Destroy();

			#endregion

			// Runs one canvas draw op, bakes it in (so the op chain never grows across repaints), repaints if visible
			// and not mid-batch, and returns this overlay for chaining. On a real failure the canvas op raises the
			// error (throws in the normal throwing mode); we return this either way so a fluent chain never receives
			// an error object to dereference.
			private object Draw(Func<object> op)
			{
				if (!EnsureCanvas())
					return this;

				_ = op();
				canvas.Bake();
				MaybeRefresh();
				return this;
			}

			private bool EnsureCanvas()
			{
				if (canvas != null)
					return true;

				if (w <= 0 || h <= 0)
				{
					_ = Errors.ValueErrorOccurred("Overlay has no size: construct it as Overlay(x, y, w, h) or call Update/DrawImage first.");
					return false;
				}

				if (TryCreateCanvas(new ScreenRect(x, y, w, h), out var created))
				{
					canvas = created;
					return true;
				}

				_ = Errors.ValueErrorOccurred("Could not create the overlay canvas.");
				return false;
			}

			private static bool TryCreateCanvas(ScreenRect bounds, out KeysharpImage created)
			{
				created = null;

				if (!bounds.HasArea)
					return false;

				// Ask the renderer for the target's actual pixel canvas. Drawing coordinates remain native local units.
				var pixels = Platform.Overlay.GetCanvasSize(bounds);

				if (!pixels.HasArea || KeysharpImage.Create(null,
						(long)pixels.Width, (long)pixels.Height) is not KeysharpImage image)
					return false;

				SetDrawScale(image, pixels.Width, pixels.Height, bounds.Width, bounds.Height);
				image.mutable = true;
				created = image;
				return true;
			}

			// Loads (where needed) and copies a caller-owned image without changing live overlay state.
			private bool TryCopyImage(object source, string operation, out KeysharpImage copy)
			{
				copy = null;
				var loaded = source as KeysharpImage;
				var ownsLoaded = false;

				if (loaded == null)
				{
					var result = KeysharpImage.FromBitmap(null, source);

					if (result is not KeysharpImage li)
					{
						_ = Errors.ValueErrorOccurred($"Overlay.{operation} could not load the source image.");
						return false;
					}

					loaded = li;
					ownsLoaded = true;
				}

				copy = loaded.Copy() as KeysharpImage;

				if (ownsLoaded)
					_ = loaded.Dispose();

				if (copy == null)
				{
					_ = Errors.ValueErrorOccurred($"Overlay.{operation} requires a valid Image.");
					return false;
				}

				copy.mutable = true;
				return true;
			}

			private static void SetDrawScale(KeysharpImage image, int pixelW, int pixelH, int screenW, int screenH)
			{
				image.drawScaleX = ScaleFactor.Normalize(screenW > 0 ? (double)pixelW / screenW : 1.0);
				image.drawScaleY = ScaleFactor.Normalize(screenH > 0 ? (double)pixelH / screenH : 1.0);
			}

			private bool RejectRedrawMutation()
			{
				if (!redrawing)
					return false;

				_ = Errors.ValueErrorOccurred("Overlay.Redraw callbacks may draw only; overlay state and lifecycle cannot be changed inside the callback.");
				return true;
			}

			// Repaints the live surface after a mutation, but ONLY when actually visible and not inside a
			// BeginDraw/EndDraw batch — the batch coalesces many mutations into the single upload EndDraw performs.
			private void MaybeRefresh()
			{
				if (requestedVisible && suspendCount == 0)
					Refresh();
			}

			private void MoveLive()
			{
				if (!isMapped || suspendCount > 0)
					return;

				var geometry = CurrentGeometry;
				var bounds = new ScreenRect(x, y, geometry.ScreenW, geometry.ScreenH);

				if (!Platform.Overlay.TryMoveImageOverlay(OverlayId, bounds))
					Refresh();
			}

			private void Refresh()
			{
				if (!requestedVisible || canvas == null)
					return;

				var geometry = CurrentGeometry;

				if (TryUpload(canvas, new ScreenRect(x, y, geometry.ScreenW, geometry.ScreenH)))
					isMapped = true;
			}

			// Uploads one already-prepared canvas at one final geometry. This is the only platform call made by
			// Update; the backing copies synchronously and never retains or disposes the canvas bitmap.
			private bool TryUpload(KeysharpImage source, ScreenRect bounds)
			{
				// Hand the canvas's own bitmap to the backing WITHOUT copying it — the backing borrows it and
				// performs its platform-specific display conversion synchronously (it never keeps or disposes what it is
				// handed). Some backings require more than one native transfer. Only an
				// opacity pass needs a temporary, which we own and dispose here.
				var bmp = source.PeekBitmap();

				if (bmp == null)
					return false;

				// ApplyOpacity mutates in place, so to preserve the live canvas we fade a throwaway clone; at full
				// opacity we borrow the canvas bitmap directly (zero-copy). toShow is disposed below iff it's the clone.
				var toShow = opacity != 255 ? ImageHelper.ApplyOpacity(new Bitmap(bmp), (byte)opacity) : bmp;

				try
				{
					return Platform.Overlay.TryShowImageOverlay(OverlayId, bounds, toShow, clickThrough);
				}
				finally
				{
					if (!ReferenceEquals(toShow, bmp))
						toShow.Dispose();   // dispose only the opacity temp, never the canvas's own bitmap
				}
			}
		}
	}
}
