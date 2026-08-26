#if LINUX
using Wl = Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Internals
{
	/// <summary>Linux overlay backing. Image overlays prefer wlr-layer-shell, then a compositor extension
	/// (GNOME/Cinnamon), then the Eto click-through fallback -- chosen per overlay on its first Show.</summary>
	internal sealed class LinuxOverlay : OverlayBase
	{
		public override PixelSize GetCanvasSize(ScreenRect bounds)
		{
			// X11's public coordinates are already root-window pixels, which are also the backing pixels.
			if (!IsWaylandSession)
				return OverlayCanvasSizing.FromScale(bounds, 1.0);

			var client = Wl.WaylandLayerShellClient.Current;
			var segments = client?.GetOutputSegments(bounds);

			if (segments is { Count: > 0 })
			{
				var scale = segments.Max(segment => ScaleFactor.Normalize(segment.Output.BufferScale));
				return OverlayCanvasSizing.FromScale(bounds, scale);
			}

			return OverlayCanvasSizing.FromEtoScreen(bounds);
		}

		protected override IImageOverlayBacking CreateBacking(uint id) => new LinuxImageOverlayBacking(id);
	}

	// Picks wlr-layer-shell / compositor-extension / Eto on the first Show (falling back to Eto if the preferred
	// backing fails), then reuses that concrete backing for every later Show/Move.
	internal sealed class LinuxImageOverlayBacking : IImageOverlayBacking
	{
		private readonly uint id;
		private IImageOverlayBacking inner;
		private Action<OverlayPointerEvent> pointerSink;

		internal LinuxImageOverlayBacking(uint id) => this.id = id;

		// Forwarded to whichever concrete backing selection picked. Eto and layer-shell surfaces can raise it;
		// compositor actors cannot receive client input.
		public Action<OverlayPointerEvent> PointerSink
		{
			get => pointerSink;
			set
			{
				pointerSink = value;

				if (inner != null)
					inner.PointerSink = value;
			}
		}

		public nint Handle => inner?.Handle ?? 0;

		// Each concrete Linux backing implements opacity, so lazy selection cannot invalidate this answer.
		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
			if (inner is CompositorImageBacking && !clickThrough && !RetireActor())
				return false;

			if (inner != null)
			{
				if (inner.Present(canvas, bounds, opacity, clickThrough, damage))
					return true;

				// A compositor restart invalidates the layer-shell backing; its replacement needs a complete frame.
				if (inner is LayerImageBacking layer && !layer.IsAvailable)
				{
					inner.Dispose();
					inner = null;
					return Present(canvas, bounds, opacity, clickThrough, AllDamage());
				}

				if (inner is CompositorImageBacking && RetireActor())
					return Present(canvas, bounds, opacity, clickThrough, AllDamage());

				return false;
			}

			var preferred = CreatePreferred(clickThrough);
			preferred.PointerSink = pointerSink;

			// The compositor backing treats an ambiguous timeout as success, preventing a duplicate Eto window.
			if (preferred.Present(canvas, bounds, opacity, clickThrough, AllDamage()))
			{
				inner = preferred;
				return true;
			}

			preferred.Dispose();

			if (preferred is EtoImageOverlay)
				return false;

			var fallback = new EtoImageOverlay { PointerSink = pointerSink };

			if (fallback.Present(canvas, bounds, opacity, clickThrough, AllDamage()))
			{
				inner = fallback;
				return true;
			}

			fallback.Dispose();
			return false;
		}

		private static DamageList AllDamage()
		{
			var damage = new DamageList();
			damage.AddAll();
			return damage;
		}

		private bool RetireActor()
		{
			if (inner is not CompositorImageBacking actor || !actor.TryHide())
				return false;

			actor.Dispose();
			inner = null;
			return true;
		}

		public bool Move(ScreenRect bounds) => inner?.Move(bounds) ?? false;

		public bool TryHide()
		{
			var backing = inner;

			if (backing == null)
				return true;

			// Keep `inner` on an unconfirmed withdraw so a later retry re-attempts it; only forget it once it is gone.
			if (!backing.TryHide())
				return false;

			inner = null;
			return true;
		}

		public void Dispose()
		{
			inner?.Dispose();
			inner = null;
		}

		private IImageOverlayBacking CreatePreferred(bool clickThrough)
		{
			var client = Wl.WaylandLayerShellClient.Current;

			if (client != null && client.IsAvailable)
				return new LayerImageBacking();

			if (clickThrough && IsWaylandSession && ShouldAttemptCompositor(Wl.WaylandBackend.Current))
				return new CompositorImageBacking(id);

			return new EtoImageOverlay();
		}

		// A shell service's real Show response is authoritative. Its separate NameHasOwner probe is only a cached
		// availability snapshot and can time out while other calls on the same healthy connection keep succeeding.
		internal static bool ShouldAttemptCompositor(Wl.IWaylandBackend backend)
			=> backend?.CanAttemptImageOverlay == true;
	}

	// wlr-layer-shell backing (KWin/wlroots). WaylandImageOverlay copies the pixels into its own SHM buffer on
	// Show, so nothing of the borrowed `image` is retained; a same-size move just repositions that surface.
	internal sealed class LayerImageBacking : IImageOverlayBacking
	{
		private sealed class Fragment
		{
			internal readonly Wl.WaylandImageOverlay Overlay;
			internal Wl.WaylandLayerShellClient.OutputSegment Segment;

			internal Fragment(Wl.WaylandImageOverlay overlay,
				Wl.WaylandLayerShellClient.OutputSegment segment)
			{
				Overlay = overlay;
				Segment = segment;
			}
		}

		private readonly Dictionary<uint, Fragment> fragments = [];
		private int shownW, shownH;
		private byte shownOpacity;
		private bool shownClickThrough;
		private Action<OverlayPointerEvent> pointerSink;

		// Each fragment adds its segment offset to the layer surface's local pointer coordinates.
		public Action<OverlayPointerEvent> PointerSink
		{
			get => pointerSink;
			set
			{
				pointerSink = value;
				ApplyPointerSinks();
			}
		}

		// (Re)wires every fragment's surface-local sink with its current segment offset. Called whenever the
		// sink or the fragment set/geometry changes; both happen under the owning slot gate, while the
		// dispatcher thread only reads the per-fragment delegate.
		private void ApplyPointerSinks()
		{
			foreach (var fragment in fragments.Values)
			{
				var segment = fragment.Segment;
				fragment.Overlay.PointerSink = pointerSink == null ? null : (kind, sx, sy) =>
				{
					var sink = pointerSink;

					if (sink != null)
						sink(new OverlayPointerEvent(kind,
							segment.SourceOffsetX + (int)Math.Round(sx),
							segment.SourceOffsetY + (int)Math.Round(sy)));
				};
			}
		}

		private bool Matches(IReadOnlyList<Wl.WaylandLayerShellClient.OutputSegment> segments)
		{
			if (!CanReuseFragmentCount(segments.Count, fragments.Count))
				return false;

			foreach (var segment in segments)
				if (!fragments.TryGetValue(segment.Output.RegistryName, out var fragment)
						|| fragment.Segment != segment)
					return false;

			return true;
		}

		internal static bool CanReuseFragmentCount(int segmentCount, int fragmentCount)
			=> segmentCount > 0 && segmentCount == fragmentCount;

		public nint Handle
		{
			get
			{
				foreach (var fragment in fragments.Values)
					return fragment.Overlay.Handle;

				return 0;
			}
		}

		internal bool IsAvailable
		{
			get
			{
				if (fragments.Count == 0)
					return Wl.WaylandLayerShellClient.Current?.IsAvailable == true;

				foreach (var fragment in fragments.Values)
					if (!fragment.Overlay.IsAvailable)
						return false;

				return true;
			}
		}

		// A constant alpha is folded into the pixel copy that has to happen anyway, so fading costs nothing
		// beyond the multiply — no cloning the canvas.
		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
			=> Show(canvas, bounds, clickThrough, opacity,
				damage?.Kind ?? DamageKind.All, damage?.Union() ?? default);

		private bool Show(OverlaySurface canvas, ScreenRect bounds, bool clickThrough,
			byte opacity, DamageKind canvasDamageKind, PixelRect canvasDamage)
		{
			var client = Wl.WaylandLayerShellClient.Current;

			if (client == null || !client.IsAvailable)
				return false;   // borrow: never dispose `image`

			try
			{
				var segments = client.GetOutputSegments(bounds);

				if (segments.Count == 0)
					return false;

				if (canvasDamageKind == DamageKind.None && shownOpacity == opacity
						&& shownClickThrough == clickThrough && Matches(segments))
					return true;

				var image = canvas.PrepareForPresent();

				// Prepare every existing fragment before committing any of them. A virtual-desktop animation therefore
				// reuses its layer surfaces and bounded buffer pools just like a single-output animation; only an actual
				// topology or segment-geometry change needs the replacement path below.
				if (Matches(segments))
				{
					foreach (var segment in segments)
					{
						var fragment = fragments[segment.Output.RegistryName];

						if (!fragment.Overlay.Prepare(image, SourcePixels(image, bounds, segment), segment.Bounds,
								segment.Output, clickThrough, opacity, canvasDamageKind, canvasDamage))
							return false;
					}

					foreach (var segment in segments)
						if (!fragments[segment.Output.RegistryName].Overlay.CommitPrepared())
							return false;

					foreach (var segment in segments)
						fragments[segment.Output.RegistryName].Segment = segment;

					shownW = bounds.Width;
					shownH = bounds.Height;
					shownOpacity = opacity;
					shownClickThrough = clickThrough;
					ApplyPointerSinks();
					return true;
				}

				var staged = new Dictionary<uint, Fragment>();
				var stagedCommitted = false;

				try
				{
					foreach (var segment in segments)
					{
						var overlay = new Wl.WaylandImageOverlay(client);

						// A brand-new fragment surface has no previous content, so it must be written whole
						// regardless of what changed on the canvas.
						if (!overlay.Prepare(image, SourcePixels(image, bounds, segment), segment.Bounds,
								segment.Output, clickThrough, opacity))
						{
							overlay.Dispose();
							return false;
						}

						staged.Add(segment.Output.RegistryName, new Fragment(overlay, segment));
					}

					// Every new surface is configured and has a complete SHM frame before any content is attached. The
					// final commits are necessarily per-output (Wayland has no cross-surface transaction), but a prepare
					// failure can no longer expose a partial replacement.
					foreach (var fragment in staged.Values)
						if (!fragment.Overlay.CommitPrepared())
							return false;

					stagedCommitted = true;
				}
				finally
				{
					if (!stagedCommitted)
						foreach (var fragment in staged.Values)
							fragment.Overlay.Dispose();
				}

				var previous = fragments.Values.ToArray();
				fragments.Clear();

				foreach (var pair in staged)
					fragments.Add(pair.Key, pair.Value);

				shownW = bounds.Width;
				shownH = bounds.Height;
				shownOpacity = opacity;
				shownClickThrough = clickThrough;
				ApplyPointerSinks();   // fresh fragments need the sink with their segment offsets

				// Replacements are already configured and mapped, so retiring old surfaces cannot flash a blank frame.
				foreach (var fragment in previous)
					fragment.Overlay.Dispose();

				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool Move(ScreenRect bounds)
		{
			if (fragments.Count != 1)
				return false;

			Fragment fragment = null;

			foreach (var candidate in fragments.Values)
			{
				fragment = candidate;
				break;
			}

			if (fragment == null)
				return false;

			if (!TryResolveSameOutputMove(fragment.Segment, shownW, shownH, bounds, out var segment))
				return false;

			try
			{
				if (!fragment.Overlay.Reposition(segment.Bounds, segment.Output))
					return false;
				fragment.Segment = segment;
				ApplyPointerSinks();   // keep the captured segment offset in step with the move
				return true;
			}
			catch { return false; }
		}

		internal static bool TryResolveSameOutputMove(Wl.WaylandLayerShellClient.OutputSegment current,
			int shownWidth, int shownHeight, ScreenRect bounds,
			out Wl.WaylandLayerShellClient.OutputSegment moved)
		{
			moved = default;
			var output = current.Output.Bounds;
			var wasWhole = current.SourceOffsetX == 0 && current.SourceOffsetY == 0
				&& current.Bounds.Width == shownWidth && current.Bounds.Height == shownHeight;
			var remainsWhole = bounds.Width == shownWidth && bounds.Height == shownHeight
				&& bounds.X >= output.X && bounds.Y >= output.Y
				&& bounds.Right <= output.Right && bounds.Bottom <= output.Bottom;

			if (!wasWhole || !remainsWhole)
				return false;

			moved = new Wl.WaylandLayerShellClient.OutputSegment(current.Output, bounds, 0, 0);
			return true;
		}

		public bool TryHide()
		{
			if (!TryRetire(fragments, fragment => fragment.Overlay.Dispose()))
				return false;

			shownW = shownH = 0;
			shownOpacity = 0;
			shownClickThrough = false;
			return true;
		}

		internal static bool TryRetire<T>(Dictionary<uint, T> owned, Action<T> retire)
		{
			var success = true;

			foreach (var pair in owned.ToArray())
				try
				{
					retire(pair.Value);
					owned.Remove(pair.Key);
				}
				catch { success = false; }

			return success;
		}

		public void Dispose() => _ = TryHide();

		private static Rectangle SourcePixels(Bitmap image, ScreenRect whole,
			Wl.WaylandLayerShellClient.OutputSegment segment)
		{
			var source = whole.ScreenToPixelBounds(segment.Bounds, new PixelSize(image.Width, image.Height));

			// An authored image can be smaller than the number of output fragments it spans. Such a fragment still
			// needs one source pixel; duplicating the boundary sample is the only representable nearest-neighbour result.
			if (source.Width == 0)
				source = new Rectangle(Math.Min(source.X, image.Width - 1), source.Y, 1, source.Height);

			if (source.Height == 0)
				source = new Rectangle(source.X, Math.Min(source.Y, image.Height - 1), source.Width, 1);

			return source;
		}
	}

	// Compositor-extension backing (GNOME/Cinnamon): hands the pixels to the shell as a PNG. A move asks the shell
	// to reposition the already-uploaded actor (no re-encode); only when that fast path is unavailable - an older
	// extension, or the actor was dropped - does it fall back to re-encoding and re-sending the current image.
	internal sealed class CompositorImageBacking : IImageOverlayBacking
	{
		private readonly uint id;
		private bool shown;
		private bool hidden;
		private ScreenRect shownBounds;
		private byte shownOpacity;

		internal CompositorImageBacking(uint id) => this.id = id;

		// Stored but never raised: a compositor-drawn actor has no client-side input window.
		public Action<OverlayPointerEvent> PointerSink { get; set; }

		public nint Handle => 0;

		// Actor opacity is folded into the encoded snapshot.
		public bool Present(OverlaySurface canvas, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
			// Compositor-extension actors are passive. Returning false selects the interactive Eto backing instead of
			// falsely claiming that this surface can receive input.
			if (!clickThrough)
				return false;

			if (shown && damage?.Kind == DamageKind.None && opacity == shownOpacity)
			{
				if (bounds == shownBounds)
					return true;

				if (CanMoveWithoutUpload(shownBounds, bounds)
						&& Wl.WaylandBackend.Current?.TryMoveImageOverlay(id, bounds.X, bounds.Y,
						bounds.Width, bounds.Height) == true)
				{
					shownBounds = bounds;
					return true;
				}
			}

			try
			{
				using var snapshot = EtoImageOverlay.Snapshot(canvas.PrepareForPresent());
				ImageHelper.ApplyOpacity(snapshot, opacity);
				var bytes = ImageHelper.ToPngBytes(snapshot);

				if (bytes.Length == 0)
					return false;

				var result = Wl.WaylandBackend.Current?.TryShowImageOverlay(id, bounds.X, bounds.Y, bounds.Width, bounds.Height, bytes)
							 ?? Wl.OverlayShowResult.Failed;

				// A timeout is ambiguous and may mean the actor was created; only a definitive rejection falls back.
				if (result == Wl.OverlayShowResult.Failed)
					return false;

				shown = true;
				hidden = false;
				shownBounds = bounds;
				shownOpacity = opacity;
				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool Move(ScreenRect bounds)
		{
			if (!shown || !CanMoveWithoutUpload(shownBounds, bounds))
				return false;

			if (bounds == shownBounds)
				return true;

			if (Wl.WaylandBackend.Current?.TryMoveImageOverlay(id, bounds.X, bounds.Y,
					bounds.Width, bounds.Height) != true)
				return false;

			shownBounds = bounds;
			return true;
		}

		internal static bool CanMoveWithoutUpload(ScreenRect current, ScreenRect next)
			=> current.Width == next.Width && current.Height == next.Height;

		public bool TryHide()
		{
			// Always ask the shell to drop the actor, even when our Show timed out (shown == false): the shell may
			// still have created it, and HideImageOverlay is a no-op for an unknown id, so an unconditional hide
			// reaps a possibly-orphaned actor without risk (the alternative left it on screen until process-death).
			if (hidden)
				return true;

			try
			{
				var backend = Wl.WaylandBackend.Current;

				// Disabling the extension reaps its actors, so owner absence confirms the hide.
				if (backend == null || !backend.SupportsImageOverlay)
				{
					hidden = true;
					shown = false;
					return true;
				}

				// A definitive ack (actor removed, or unknown id) confirms the withdraw. A dropped / timed-out call
				// returns false so the caller keeps us mapped and retries -- that is what stops a lost hide from
				// orphaning the actor for good.
				if (backend.TryHideImageOverlay(id))
				{
					hidden = true;
					shown = false;
					return true;
				}

				return false;
			}
			catch { return false; }
		}

		public void Dispose() => _ = TryHide();
	}
}
#endif
