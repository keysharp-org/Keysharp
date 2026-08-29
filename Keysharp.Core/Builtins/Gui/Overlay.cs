using Keysharp.Internals;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// A click-through, always-on-top screen overlay. The overlay owns window state; its pixels are the
		/// borrowed <see cref="KeysharpImage"/> exposed by <see cref="Canvas"/>. Drawing changes only that canvas;
		/// <see cref="Present"/> publishes a completed frame. <see cref="SetImage"/> copies a source into the canvas,
		/// and <see cref="Redraw"/> builds and commits a replacement canvas off-screen.
		/// <para><c>Highlight</c> and, on Linux/macOS, <c>ToolTip</c> use this platform primitive.</para>
		/// <para>Set <see cref="ClickThrough"/> to <c>false</c> to make the overlay receive mouse
		/// input (an interactive HUD) instead of passing clicks through to the windows beneath it, and register
		/// mouse handlers with <see cref="OnEvent"/> (Click/DoubleClick/ContextMenu/MouseMove).</para>
		/// </summary>
		[UserDeclaredName("Overlay")]
		public class KeysharpOverlay : KeysharpObject
		{
			private const uint OverlayIdPrefix = 0x1000_0000u;
			private const uint IdMask = 0x0FFF_FFFFu;
			private static int nextOverlayId;

			// The surface owns the platform pixels, their Image view and accumulated damage as one lifetime.
			private OverlaySurface surface;

			// The drawing view of `surface`. Not a field: two names for one thing is how they drift apart.
			private KeysharpImage canvas => surface?.Image;

			// For tests that need to prove which surface an operation left in place (SetImage reuse, Redraw
			// replacement) and whether a present is still owed. Internal, so neither is part of the script surface.
			internal OverlaySurface SurfaceForTests => surface;
			internal bool HasUnpresentedDamageForTests => surface?.Damage.Kind != DamageKind.None;
			private uint overlayId;
			private int x;
			private int y;
			private int w;   // authored native width/height; 0 keeps that dimension automatic
			private int h;
			private int autoW;   // resolved automatic geometry can differ from the backing-pixel size
			private int autoH;
			private long opacity = 255;   // whole-overlay alpha multiplier applied at upload time
			private bool requestedVisible;
			private bool isMapped;
			private bool clickThrough = true;   // default: transparent to mouse input (Highlight/ToolTip depend on this)
			private bool redrawing;

			private uint OverlayId => overlayId != 0
				? overlayId
				: overlayId = OverlayIdPrefix | ((uint)Interlocked.Increment(ref nextOverlayId) & IdMask);

			private (int ScreenW, int ScreenH) CurrentGeometry
				=> ResolveGeometry(w, h,
					autoW > 0 ? autoW : (int)(canvas?.Width ?? 0),
					autoH > 0 ? autoH : (int)(canvas?.Height ?? 0));

			// Width and height are native screen units. An image without either uses one unit per pixel;
			// generated canvases ask the platform renderer for their actual pixel dimensions.
			private static (int ScreenW, int ScreenH) ResolveGeometry(
				int authoredW, int authoredH, int imageW, int imageH)
				=> (authoredW > 0 ? authoredW : Math.Max(0, imageW),
					authoredH > 0 ? authoredH : Math.Max(0, imageH));

			public KeysharpOverlay(params object[] args) : base(args) { }

			/// <summary>
			/// <c>Overlay()</c> creates an unconfigured overlay. <c>Overlay(X, Y, Width, Height)</c> stores a native
			/// screen rectangle and creates its canvas on first access. Use <see cref="FromImage"/> when an image
			/// supplies the initial pixels.
			/// </summary>
			// `new`, not `override`: lifecycle dispatch resolves the most-derived declaration by name.
			public object __New(object X = null, object Y = null, object Width = null, object Height = null)
			{
				if (X == null && Y == null && Width == null && Height == null)
					return DefaultObject;

				if (X == null || Y == null || Width == null || Height == null)
					return Errors.ValueErrorOccurred("Overlay requires either no arguments or X, Y, Width and Height.");

				var nextX = X.Ai();
				var nextY = Y.Ai();
				var nextW = Width.Ai();
				var nextH = Height.Ai();

				if (nextW <= 0 || nextH <= 0)
					return Errors.ValueErrorOccurred("Overlay Width and Height must be positive.");

				x = nextX;
				y = nextY;
				w = nextW;
				h = nextH;

				return DefaultObject;
			}

			/// <summary>Creates a hidden overlay whose canvas is copied from <paramref name="Source"/>.</summary>
			[Static]
			public static object FromImage(object @this, object Source, object X = null, object Y = null,
				object Width = null, object Height = null)
			{
				var overlay = new KeysharpOverlay();
				_ = overlay.SetImage(Source, X, Y, Width, Height);
				return overlay.surface != null ? overlay : DefaultObject;
			}

			#region Properties

			public object X { get => (long)x; set { if (RejectRedrawMutation()) return; x = value.Ai(); MoveLive(false); } }
			public object Y { get => (long)y; set { if (RejectRedrawMutation()) return; y = value.Ai(); MoveLive(false); } }

			/// <summary>Width in native screen units. Resizing keeps and scales the canvas; use
			/// <see cref="Redraw"/> when its raster size should change.</summary>
			public object Width
			{
				get
				{
					if (w > 0)
						return (long)w;

					return canvas != null ? (long)CurrentGeometry.ScreenW : 0L;
				}
				set
				{
					if (RejectRedrawMutation()) return;
					var next = value.Ai();

					if (next < 0)
					{
						_ = Errors.ValueErrorOccurred("Overlay Width cannot be negative.");
						return;
					}

					var previous = CurrentGeometry.ScreenW;
					w = next;

					if (next == 0)
						autoW = (int)(canvas?.Width ?? 0);

					MoveLive(CurrentGeometry.ScreenW != previous);
				}
			}

			/// <summary>Height in native screen units. See <see cref="Width"/>.</summary>
			public object Height
			{
				get
				{
					if (h > 0)
						return (long)h;

					return canvas != null ? (long)CurrentGeometry.ScreenH : 0L;
				}
				set
				{
					if (RejectRedrawMutation()) return;
					var next = value.Ai();

					if (next < 0)
					{
						_ = Errors.ValueErrorOccurred("Overlay Height cannot be negative.");
						return;
					}

					var previous = CurrentGeometry.ScreenH;
					h = next;

					if (next == 0)
						autoH = (int)(canvas?.Height ?? 0);

					MoveLive(CurrentGeometry.ScreenH != previous);
				}
			}

			/// <summary>Whole-overlay opacity from 0 to 255. Changing it republishes a visible overlay.</summary>
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
			/// HUD/highlight so clicks reach the windows beneath; set it false to make the overlay receive mouse input
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

			#region Frame building

			/// <summary>Builds a complete replacement canvas off-screen, passing it to
			/// <paramref name="Callback"/>, then presents its pixels and optional geometry as a completed frame.
			/// Drawing uses local native screen units while backing-pixel density is selected automatically for the target.
			/// A drawing exception leaves the current frame unchanged. A failed presentation does not commit the
			/// replacement canvas or geometry.</summary>
			public object Redraw(object Callback, object X = null, object Y = null, object Width = null, object Height = null)
			{
				if (RejectRedrawMutation()) return this;
				if (Callback is not KeysharpFunc f)
					return Errors.ValueErrorOccurred("Overlay.Redraw requires a callable object.");

				var nextX = X != null ? X.Ai() : x;
				var nextY = Y != null ? Y.Ai() : y;
				var nextW = Width != null ? Width.Ai() : w;
				var nextH = Height != null ? Height.Ai() : h;
				var oldGeometry = CurrentGeometry;

				if (nextW < 0 || nextH < 0)
					return Errors.ValueErrorOccurred("Overlay Redraw Width and Height cannot be negative.");

				var screenW = nextW > 0 ? nextW : oldGeometry.ScreenW;
				var screenH = nextH > 0 ? nextH : oldGeometry.ScreenH;

				if (!TryCreateSurface(new ScreenRect(nextX, nextY, screenW, screenH), out var replacement))
					return Errors.ValueErrorOccurred("Overlay.Redraw requires a positive final width and height.");

				var previousSurface = surface;
				var previousX = x;
				var previousY = y;
				var previousW = w;
				var previousH = h;
				var previousAutoW = autoW;
				var previousAutoH = autoH;
				var committed = false;

				// Draw into a private target-sized canvas. The live backing and previous model are untouched until the
				// final upload succeeds, so a resize never publishes an empty/intermediate surface.
				surface = replacement;
				x = nextX;
				y = nextY;
				w = nextW;
				h = nextH;

				if (nextW == 0)
					autoW = screenW;

				if (nextH == 0)
					autoH = screenH;

				redrawing = true;

				try
				{
					_ = f.Call(canvas);
					var finalBounds = new ScreenRect(x, y, screenW, screenH);

					if (requestedVisible && !TryPresent(replacement, finalBounds))
						return this;

					committed = true;
					previousSurface?.Dispose();

					if (requestedVisible)
						isMapped = true;
				}
				finally
				{
					redrawing = false;

					if (!committed)
					{
						surface = previousSurface;
						x = previousX;
						y = previousY;
						w = previousW;
						h = previousH;
						autoW = previousAutoW;
						autoH = previousAutoH;
						replacement.Dispose();
					}
				}

				return this;
			}

			#endregion

			#region Drawing

			/// <summary>
			/// The overlay's borrowed drawing surface. Drawing and read operations are available, but ownership and
			/// whole-image replacement operations are not. Call <see cref="Present"/> when a frame is complete.
			/// <para>The overlay owns this image and it cannot be disposed, re-initialised, scaled, rotated,
			/// cropped, filtered, or have its pixel data replaced, because its pixels are the platform's own
			/// presentable buffer. <c>Canvas.Copy()</c> gives an independent image
			/// you can transform freely.</para>
			/// <para>The reference is not stable for the overlay's whole life: <see cref="Destroy"/> frees it,
			/// and a resizing <see cref="SetImage"/> or any <see cref="Redraw"/> builds a new canvas, so a
			/// reference held across either is dead. Read it again rather than caching it, unless the overlay is
			/// known never to resize.</para>
			/// <para>Reading this creates the canvas, so the overlay needs a size first — from the constructor,
			/// or from <see cref="SetImage"/>.</para>
			/// </summary>
			public KeysharpImage Canvas => EnsureCanvas() ? canvas : null;

			/// <summary>Publishes the current canvas without changing visibility.</summary>
			public object Present()
			{
				if (RejectRedrawMutation()) return this;
				if (EnsureCanvas()) Refresh();
				return this;
			}

			/// <summary>Copies <paramref name="Source"/> into the canvas and optionally changes geometry. A failed
			/// presentation does not commit requested geometry or a replacement canvas; a reused same-sized canvas
			/// retains the copied pixels for the next presentation. This operation does not change visibility.</summary>
			public object SetImage(object Source, object X = null, object Y = null, object Width = null,
				object Height = null)
			{
				if (RejectRedrawMutation()) return this;
				var nextX = X != null ? X.Ai() : x;
				var nextY = Y != null ? Y.Ai() : y;
				var nextW = Width != null ? Width.Ai() : w;
				var nextH = Height != null ? Height.Ai() : h;

				if (nextW < 0 || nextH < 0)
					return Errors.ValueErrorOccurred("Overlay SetImage Width and Height cannot be negative.");

				if (!TryResolveSource(Source, nameof(SetImage), out var loaded, out var ownsLoaded))
					return this;

				OverlaySurface replacement = null;

				try
				{
					if (ReferenceEquals(loaded, canvas))
					{
						_ = Errors.ValueErrorOccurred("Overlay.SetImage cannot use its own Canvas; call Present instead.");
						return this;
					}

					var srcBitmap = loaded.PrepareForRead();

					if (srcBitmap == null)
					{
						_ = Errors.ValueErrorOccurred($"Overlay.{nameof(SetImage)} requires a valid Image.");
						return this;
					}

					var srcSize = new PixelSize(srcBitmap.Width, srcBitmap.Height);
					var reuse = surface != null && surface.Size == srcSize;
					var target = reuse ? surface : (replacement = CreateSurface(srcSize));

					if (target == null)
					{
						_ = Errors.ValueErrorOccurred($"Overlay.{nameof(SetImage)} requires a valid Image.");
						return this;
					}

					target.Image.BlitFrom(srcBitmap);
					var nextGeometry = ResolveGeometry(nextW, nextH, target.Size.Width, target.Size.Height);
					var uploadNow = requestedVisible && !redrawing;

					if (uploadNow && !TryPresent(target,
							new ScreenRect(nextX, nextY, nextGeometry.ScreenW, nextGeometry.ScreenH)))
						return this;

					if (!reuse)
					{
						var previous = surface;
						surface = target;
						replacement = null;
						previous?.Dispose();
					}

					x = nextX;
					y = nextY;
					w = nextW;
					h = nextH;

					if (nextW == 0)
						autoW = target.Size.Width;

					if (nextH == 0)
						autoH = target.Size.Height;

					SetDrawScale(target.Image, target.Size.Width, target.Size.Height,
						nextGeometry.ScreenW, nextGeometry.ScreenH);

					if (uploadNow)
						isMapped = true;

					return this;
				}
				finally
				{
					replacement?.Dispose();

					if (ownsLoaded)
						_ = loaded.Dispose();
				}
			}

			#endregion

			#region Events

			// Registered pointer handlers by canonical event name ("click", "doubleclick", "contextmenu",
			// "mousemove"). Each entry keeps the original script callback object for OnEvent(.., .., 0) removal
			// (converting again yields a different wrapper, so identity must be tested against what was passed)
			// and a CallbackRegistration whose active state holds script persistence, like other event hooks.
			// handlerGate guards the map: OnEvent mutates on a script thread while HandlePointerEvent snapshots
			// on the UI thread.
			private readonly object handlerGate = new ();
			private Dictionary<string, List<(object original, CallbackRegistration reg)>> eventHandlers;
			private bool sinkArmed;

			private static readonly string[] supportedEvents = ["click", "doubleclick", "contextmenu", "mousemove"];

			/// <summary>Registers <paramref name="Callback"/> for a mouse event on this overlay, in the style of
			/// <c>Gui.OnEvent</c>. Events: <c>Click</c> (left button), <c>DoubleClick</c>, <c>ContextMenu</c>
			/// (right button) and <c>MouseMove</c>. The callback receives <c>(overlay, x, y)</c> with x/y in the
			/// overlay's local native units — the same units the draw ops use, so a hit-test against drawn
			/// shapes needs no conversion. <paramref name="AddRemove"/>: 1 (default) = call after previously
			/// registered handlers, -1 = call before them, 0 = unregister the callback.
			/// <para>The overlay must not be click-through to receive mouse input: set
			/// <see cref="ClickThrough"/> := false, or the events never fire (input passes through to the
			/// windows beneath). Events require a backing with a client-side window (<see cref="Hwnd"/> != 0);
			/// a compositor-drawn overlay cannot receive input. Registered handlers keep the script persistent;
			/// <see cref="Destroy"/> removes them all.</para></summary>
			public object OnEvent(object EventName, object Callback, object AddRemove = null)
			{
				if (RejectRedrawMutation()) return this;
				var rawName = EventName.As();
				var name = rawName.ToLowerInvariant();

				if (System.Array.IndexOf(supportedEvents, name) < 0)
					return Errors.ValueErrorOccurred($"Overlay.OnEvent: unknown event \"{rawName}\". Supported: Click, DoubleClick, ContextMenu, MouseMove.");

				var mode = AddRemove == null ? 1L : AddRemove.Al();

				if (mode is not (1L or -1L or 0L))
					return Errors.ValueErrorOccurred("Overlay.OnEvent: AddRemove must be 1, -1 or 0.");

				var fo = Functions.GetKeysharpFunc(Callback, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(Callback, typeof(KeysharpFunc));

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
							if (ReferenceEquals(list[i].original, Callback) || Equals(list[i].original, Callback))
							{
								list[i].reg.Clear();   // releases the persistence hold
								list.RemoveAt(i);
							}
						}
					}
					else
					{
						var entry = (Callback, new CallbackRegistration(fo, Script.TheScript?.EventScheduler, true));

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
				// registration stands. Capture only a weak reference to the overlay: a strong capture would pin a
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
					var scheduler = reg.OwnerScheduler;

					if (scheduler == null || scheduler.IsDisposed)
						continue;

					var r = reg;
					// One args array per handler: a callback declaring a ByRef parameter writes into its argument
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
					scheduler.Owner.ExitIfNotPersistent();
				}

				return ScriptEventExecutionResult.Executed;
			}

			#endregion

			#region Show / Move / Hide / Destroy

			public object Show(object X = null, object Y = null, object Width = null, object Height = null)
			{
				if (RejectRedrawMutation()) return this;
				var nextX = X != null ? X.Ai() : x;
				var nextY = Y != null ? Y.Ai() : y;
				var nextW = Width != null ? Width.Ai() : w;
				var nextH = Height != null ? Height.Ai() : h;

				if (nextW < 0 || nextH < 0)
					return Errors.ValueErrorOccurred("Overlay Show Width and Height cannot be negative.");

				x = nextX;
				y = nextY;
				w = nextW;
				h = nextH;

				if (Width != null && nextW == 0)
					autoW = (int)(canvas?.Width ?? 0);

				if (Height != null && nextH == 0)
					autoH = (int)(canvas?.Height ?? 0);

				if (!EnsureCanvas())
					return this;   // sizeless overlay: EnsureCanvas raised the error (throws in throw-mode); keep chaining otherwise

				requestedVisible = true;
				MaybeRefresh();
				return this;
			}

			public object Move(object X = null, object Y = null, object Width = null, object Height = null)
			{
				if (RejectRedrawMutation()) return this;
				var nextX = X != null ? X.Ai() : x;
				var nextY = Y != null ? Y.Ai() : y;
				var nextW = Width != null ? Width.Ai() : w;
				var nextH = Height != null ? Height.Ai() : h;

				if (nextW < 0 || nextH < 0)
					return Errors.ValueErrorOccurred("Overlay Move Width and Height cannot be negative.");

				var previous = CurrentGeometry;
				x = nextX;
				y = nextY;
				w = nextW;
				h = nextH;

				if (Width != null && nextW == 0)
					autoW = (int)(canvas?.Width ?? 0);

				if (Height != null && nextH == 0)
					autoH = (int)(canvas?.Height ?? 0);

				var current = CurrentGeometry;
				MoveLive(current.ScreenW != previous.ScreenW || current.ScreenH != previous.ScreenH);
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

				// Keep the mapped state when withdrawal is not confirmed so a later Hide can retry.
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
					_ = Hide();
					// Destroy cannot leave an unconfirmed backing without an owner to retry it.
					Platform.Overlay.DisposeImageOverlay(overlayId);
				}

				// The surface and its canvas are being torn down regardless of any backing confirmation above, so
				// Visible must read false.
				requestedVisible = false;
				isMapped = false;
				// One Dispose: the surface frees the image view, the bitmap and the platform memory beneath it,
				// in the one order in which the GDI object and its DIB both survive to be freed.
				surface?.Dispose();
				surface = null;
				autoW = 0;
				autoH = 0;
				return DefaultObject;
			}

			public override object __Delete() => Destroy();

			#endregion

			private bool EnsureCanvas()
			{
				if (canvas != null)
					return true;

				if (w <= 0 || h <= 0)
				{
					_ = Errors.ValueErrorOccurred("Overlay has no size: use Overlay(X, Y, Width, Height) or Overlay.FromImage(Source).");
					return false;
				}

				if (TryCreateSurface(new ScreenRect(x, y, w, h), out var created))
				{
					surface = created;
					return true;
				}

				_ = Errors.ValueErrorOccurred("Could not create the overlay canvas.");
				return false;
			}

			// Asks the renderer for the pixel surface it prefers for these bounds. Drawing coordinates stay native
			// local units; drawScale bridges the two.
			private bool TryCreateSurface(ScreenRect bounds, out OverlaySurface created)
			{
				created = bounds.HasArea ? CreateSurface(Platform.Overlay.GetCanvasSize(bounds)) : null;

				if (created == null)
					return false;

				SetDrawScale(created.Image, created.Size.Width, created.Size.Height, bounds.Width, bounds.Height);
				return true;
			}

			// The surface comes from the platform, not from KeysharpImage.Create: its pixels may be memory the
			// compositor reads directly, which is what lets a present skip copying them.
			private OverlaySurface CreateSurface(PixelSize pixels)
			{
				if (!pixels.HasArea)
					return null;

				var created = Platform.Overlay.CreateOverlaySurface(pixels);

				if (created?.Bitmap != null && created.Image != null)
					return created;

				created?.Dispose();
				return null;
			}

			// Resolves a SetImage source to a KeysharpImage without copying it. A path or bitmap handle
			// has to be loaded (and is then ours to free); a script-supplied Image is borrowed.
			private bool TryResolveSource(object source, string operation, out KeysharpImage loaded, out bool ownsLoaded)
			{
				loaded = source as KeysharpImage;
				ownsLoaded = false;

				if (loaded != null)
					return true;

				if (KeysharpImage.FromBitmap(null, source) is not KeysharpImage li)
				{
					_ = Errors.ValueErrorOccurred($"Overlay.{operation} could not load the source image.");
					return false;
				}

				loaded = li;
				ownsLoaded = true;
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

			// Repaints after a visible state mutation. Redraw publishes its completed replacement itself.
			private void MaybeRefresh()
			{
				if (requestedVisible && !redrawing)
					Refresh();
			}

			private void MoveLive(bool resized)
			{
				if (!isMapped || redrawing)
					return;

				if (resized)
				{
					Refresh();
					return;
				}

				var geometry = CurrentGeometry;
				var bounds = new ScreenRect(x, y, geometry.ScreenW, geometry.ScreenH);

				if (!Platform.Overlay.TryMoveImageOverlay(OverlayId, bounds))
					Refresh();
			}

			private void Refresh()
			{
				if (!requestedVisible || surface == null)
					return;

				var geometry = CurrentGeometry;

				if (TryPresent(surface, new ScreenRect(x, y, geometry.ScreenW, geometry.ScreenH)))
					isMapped = true;
			}

			// SetImage and Redraw can present a candidate before it becomes the live surface.
			private bool TryPresent(OverlaySurface target, ScreenRect bounds)
			{
				if (target?.Bitmap == null)
					return false;

				if (!Platform.Overlay.TryPresentImageOverlay(OverlayId, target, bounds, (byte)opacity, clickThrough))
					return false;

				// Failed presents retain damage for the next attempt.
				target.Damage.Reset();
				return true;
			}
		}
	}
}
