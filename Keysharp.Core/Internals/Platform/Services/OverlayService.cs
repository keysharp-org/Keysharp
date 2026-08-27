namespace Keysharp.Internals
{
	/// <summary>The pointer-event kinds an interactive (non-click-through) overlay backing can raise.</summary>
	internal enum OverlayPointerKind
	{
		MouseMove,
		Click,          // left/primary button
		DoubleClick,    // left/primary button
		ContextMenu,    // right/secondary button
	}

	/// <summary>One pointer event on an overlay surface. X/Y are surface-local coordinates in the backing's
	/// native window units — the same units the overlay's draw ops address, so no conversion is needed to
	/// hit-test drawn content.</summary>
	internal readonly record struct OverlayPointerEvent(OverlayPointerKind Kind, int X, int Y);

	/// <summary>
	/// One click-through image overlay's platform backing.
	///
	/// The platform supplies the pixels through <see cref="OverlayBase.CreateOverlaySurface"/>; the Overlay owns
	/// that surface, draws into it, and asks <see cref="Present"/> to put the changed part on screen. A canvas
	/// handed to Present stays valid and
	/// mutable afterwards — it is the live surface, not a snapshot — so a backing that cannot present from it
	/// directly copies what it needs synchronously and never retains or disposes it.
	///
	/// <see cref="Move"/> repositions the last presented content as cheaply as the backing can; if it cannot
	/// satisfy the request without new pixels (a resize), it returns false and the caller re-presents.
	/// </summary>
	internal interface IImageOverlayBacking : IDisposable
	{
		/// <summary>
		/// Puts <paramref name="surface"/> on screen at <paramref name="bounds"/>. Synchronous.
		///
		/// The backing reconciles size itself: when the surface's pixel size is what this platform would choose
		/// for these bounds it can present it as-is, otherwise it scales — always from the surface, never from a
		/// previous scaled result, so repeated resizes cannot compound resampling error.
		///
		/// <paramref name="damage"/> is the region that changed since the last successful present, in surface
		/// pixels. It is a hint a backing may ignore (presenting everything is always correct);
		/// <see cref="DamageKind.None"/> means no pixels changed, but the present must still carry any change of
		/// geometry, opacity or input mode. It is passed separately rather than read from
		/// <c>surface.Damage</c> because the service substitutes a whole-surface value when this backing has not
		/// presented the surface before.
		/// </summary>
		/// <param name="clickThrough">True (the default for a passive HUD/highlight) makes the surface
		/// transparent to mouse input; false makes it receive mouse input, where the backing supports an
		/// interactive mode (Windows layered form, Eto window, or a layer surface with its default input
		/// region). A backing which cannot be interactive must return false so selection can fall back to one
		/// which can.</param>
		bool Present(OverlaySurface surface, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage);

		bool Move(ScreenRect bounds);

		/// <summary>Receiver for pointer events on an interactive (non-click-through) surface, or null when the
		/// overlay has no registered handlers. Raised on the UI thread. Backings without a client-side input
		/// window (a compositor-owned actor) stores the value but does not raise it.</summary>
		Action<OverlayPointerEvent> PointerSink { get; set; }

		/// <summary>
		/// Withdraw the on-screen surface. Returns true iff it is confirmed gone (so the caller may forget the id);
		/// false means the withdraw could not be confirmed -- e.g. a dropped / timed-out compositor call -- and the
		/// caller must keep the backing mapped so a later Hide can re-attempt rather than orphaning the surface.
		/// Must be idempotent: a call after a successful withdraw returns true without doing more work.
		/// </summary>
		bool TryHide();

		nint Handle { get; }
	}

	/// <summary>
	/// Platform-neutral overlay service: owns the id-to-backing map, its lock, and the show/move/hide/hide-all
	/// orchestration, all in terms of one abstract per-platform <see cref="IImageOverlayBacking"/>. Highlight,
	/// ToolTip (Linux/macOS) and the user-facing Overlay builtin all render through this single image primitive,
	/// so there is no separate highlight/tooltip surface here. The map lock protects membership; each slot has its
	/// own gate so native calls for one id are ordered without blocking unrelated overlays.
	/// </summary>
	internal abstract class OverlayBase : IOverlay
	{
		private sealed class OverlaySlot
		{
			internal readonly object Gate = new();
			internal readonly IImageOverlayBacking Backing;
			internal readonly Script Owner;
			internal bool Retired;

			// The surface this slot last presented, to catch a present from a different one (see
			// TryPresentImageOverlay). Compared by reference and never dereferenced. It is a strong reference,
			// which is what makes the comparison sound: a disposed surface cannot be collected and have a new
			// one land on its address, so identity here always means identity.
			internal OverlaySurface LastPresented;

			internal OverlaySlot(Script owner, IImageOverlayBacking backing)
			{
				Owner = owner;
				Backing = backing;
			}
		}

		private readonly record struct PointerSinkRegistration(Script Owner, Action<OverlayPointerEvent> Sink);

		private readonly object sync = new ();
		private readonly Dictionary<uint, OverlaySlot> overlays = new ();

		// Pointer sinks by overlay id, kept OUTSIDE the slot so a sink registered before the first Show — or
		// after a Hide disposed the backing — is (re)applied to whatever backing the id gets next.
		private readonly Dictionary<uint, PointerSinkRegistration> pointerSinks = new ();

		public abstract PixelSize GetCanvasSize(ScreenRect bounds);

		/// <summary>Create the backing for a new overlay id (called under the map lock; must not do UI/IO work).</summary>
		protected abstract IImageOverlayBacking CreateBacking(uint id, Script owner);

		/// <summary>
		/// Allocates a drawing surface of the kind this platform can present most cheaply. Deliberately not tied
		/// to an overlay id or a live backing: a canvas is created before the first present (there is no window
		/// yet) and survives the Hide that disposes the backing instance, so it belongs to the platform, not to
		/// either of those. The default is a plain bitmap, which every backing can present by copying.
		/// </summary>
		public virtual OverlaySurface CreateOverlaySurface(PixelSize pixels)
			=> pixels.HasArea ? OverlaySurface.Plain(pixels) : null;

		public void SetImageOverlayPointerSink(uint id, Action<OverlayPointerEvent> sink)
		{
			if (id == 0)
				return;

			OverlaySlot slot;

			lock (sync)
			{
				if (sink == null)
					_ = pointerSinks.Remove(id);
				else
					pointerSinks[id] = new(Script.TheScript, sink);

				_ = overlays.TryGetValue(id, out slot);
			}

			if (slot != null)
			{
				lock (slot.Gate)
				{
					if (IsCurrent(id, slot))
						slot.Backing.PointerSink = sink;
				}
			}
		}

		public bool TryPresentImageOverlay(uint id, OverlaySurface surface, ScreenRect bounds, byte opacity,
										   bool clickThrough)
		{
			if (id == 0 || surface?.Bitmap == null)
				return false;

			if (bounds.Width <= 0) bounds = bounds with { Width = surface.Size.Width };
			if (bounds.Height <= 0) bounds = bounds with { Height = surface.Size.Height };

			if (!bounds.HasArea)
				return TryHideImageOverlay(id);   // nothing to show; the caller still owns `surface`

			OverlaySlot slot;
			var created = false;

			lock (sync)
			{
				if (!overlays.TryGetValue(id, out slot))
				{
					var owner = Script.TheScript;
					overlays[id] = slot = new OverlaySlot(owner, CreateBacking(id, owner));
					created = true;

					// A fresh backing must inherit the id's registered sink (a plain property store; no UI work).
					if (pointerSinks.TryGetValue(id, out var sink))
						slot.Backing.PointerSink = sink.Sink;
				}
			}

			// Outside the lock (may hit the UI thread / D-Bus). The surface stays the caller's: a backing either
			// presents from it in place or copies what it needs synchronously, and never retains or disposes it.
			lock (slot.Gate)
			{
				if (!IsCurrent(id, slot))
					return false;

				// A backing's dirty-rect path is only sound while it keeps receiving the same surface: its
				// window still holds what the last present put there, and a partial transfer only tops that up.
				// Presenting a different surface breaks that assumption — Overlay.Redraw builds a whole new one,
				// draws part of it and presents, so an unguarded partial transfer would leave the rest of the
				// window showing the previous surface's pixels. The surface itself starts out fully damaged, so
				// this is belt-and-braces; it lives here because "every caller remembers to damage-all a new
				// surface" is a convention, and this is a check.
				var damage = ReferenceEquals(slot.LastPresented, surface) ? surface.Damage : AllDamage;

				if (slot.Backing.Present(surface, bounds, opacity, clickThrough, damage))
				{
					slot.LastPresented = surface;
					return IsCurrent(id, slot);
				}

				if (created)
				{
					try { slot.Backing.Dispose(); } catch { }
					Retire(id, slot);
				}

				return false;
			}
		}

		// Shared immutable damage for a surface this backing has not presented before.
		private static readonly DamageList AllDamage = CreateAllDamage();

		private static DamageList CreateAllDamage()
		{
			var d = new DamageList();
			d.AddAll();
			return d;
		}

		public bool TryMoveImageOverlay(uint id, ScreenRect bounds)
		{
			OverlaySlot slot;

			lock (sync)
			{
				if (!overlays.TryGetValue(id, out slot))
					return false;   // no surface exists to move; the caller must recreate it with Show
			}

			lock (slot.Gate)
			{
				if (!IsCurrent(id, slot) || !slot.Backing.Move(bounds))
					return false;

				return IsCurrent(id, slot);
			}
		}

		public bool TryHideImageOverlay(uint id)
		{
			OverlaySlot slot;

			lock (sync)
			{
				if (!overlays.TryGetValue(id, out slot))
					return true;   // already absent is a confirmed, idempotent hide
			}

			// Verify the withdraw BEFORE forgetting the id. If the surface can't be confirmed gone (a dropped or
			// timed-out compositor hide), keep the backing mapped so a later Hide re-attempts -- dropping the id here
			// while the surface is still painted is exactly what turned a transient hiccup into a permanent orphan.
			lock (slot.Gate)
			{
				if (!IsCurrent(id, slot))
					return true;

				if (!slot.Backing.TryHide())
					return false;

				Retire(id, slot);
				return true;
			}
		}

		public void DisposeImageOverlay(uint id)
		{
			OverlaySlot slot;

			// Unconditional force-reap (no confirm-gating): remove the backing from the map, then dispose it outside
			// the lock. This is Destroy's escape hatch for a backing whose confirm-gated TryHide never succeeded -- it
			// must not be left mapped forever with no owner to retry the withdraw.
			lock (sync)
			{
				if (!overlays.TryGetValue(id, out slot))
					return;
			}

			lock (slot.Gate)
			{
				if (!IsCurrent(id, slot))
					return;

				try { slot.Backing.Dispose(); } catch { }
				Retire(id, slot);
			}
		}

		public bool TryHideAllImageOverlays(Script owner = null)
		{
			KeyValuePair<uint, OverlaySlot>[] all;
			uint[] sinks;

			// Removing the matching slots is HideAll's linearization point. Show and Move recheck membership after
			// their native call, so an operation already holding a slot gate cannot report success after this point.
			lock (sync)
			{
				all = overlays.Where(kv => owner == null || ReferenceEquals(kv.Value.Owner, owner)).ToArray();
				sinks = pointerSinks.Where(kv => owner == null || ReferenceEquals(kv.Value.Owner, owner))
					.Select(kv => kv.Key).ToArray();

				if (all.Length == 0 && sinks.Length == 0)
					return false;

				foreach (var (id, slot) in all)
				{
					slot.Retired = true;
					_ = overlays.Remove(id);
				}

				foreach (var id in sinks)
					_ = pointerSinks.Remove(id);
			}

			foreach (var (_, slot) in all)
			{
				lock (slot.Gate)
					try { slot.Backing.Dispose(); } catch { }
			}

			return true;
		}

		public nint GetImageOverlayHandle(uint id)
		{
			OverlaySlot slot;

			lock (sync)
				if (!overlays.TryGetValue(id, out slot))
					return 0;

			lock (slot.Gate)
				return IsCurrent(id, slot) ? slot.Backing.Handle : 0;
		}

		private bool IsCurrent(uint id, OverlaySlot slot)
		{
			lock (sync)
				return !slot.Retired && overlays.TryGetValue(id, out var current)
					&& ReferenceEquals(current, slot);
		}

		private void Retire(uint id, OverlaySlot slot)
		{
			lock (sync)
			{
				slot.Retired = true;

				if (overlays.TryGetValue(id, out var current) && ReferenceEquals(current, slot))
					_ = overlays.Remove(id);
			}
		}
	}
}
