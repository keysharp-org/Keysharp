namespace Keysharp.Tests;

public partial class ScreenTests
{
	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void FractionalSeam()
	{
		var captures = new List<(ScreenRect Bounds, Bitmap Pixels)>
		{
			(new ScreenRect(0, 0, 3, 1), SolidBitmap(3, 1, unchecked((int)0xFFFF0000))),
			(new ScreenRect(3, 0, 2, 1), SolidBitmap(3, 1, unchecked((int)0xFF0000FF)))
		};

		try
		{
			using var result = ScreenCaptureComposer.Compose(new ScreenRect(0, 0, 5, 1), captures);
			Assert.NotNull(result);
			Assert.That(result.Width, Is.EqualTo(8));

			for (var x = 0; x < 5; x++)
				Assert.That(result.GetPixel(x, 0).ToArgb(), Is.EqualTo(unchecked((int)0xFFFF0000)));

			for (var x = 5; x < 8; x++)
				Assert.That(result.GetPixel(x, 0).ToArgb(), Is.EqualTo(unchecked((int)0xFF0000FF)));
		}
		finally
		{
			foreach (var (_, Pixels) in captures)
				Pixels.Dispose();
		}
	}

	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void BitmapTransfer()
	{
		var source = SolidBitmap(2, 1, unchecked((int)0xFF102030));
		var captures = new List<(ScreenRect Bounds, Bitmap Pixels)>
		{
			(new ScreenRect(-2, 4, 2, 1), source)
		};

		using var result = ScreenCaptureComposer.Compose(new ScreenRect(-2, 4, 2, 1), captures);
		Assert.That(result, Is.SameAs(source));
		Assert.That(captures.Count, Is.Zero, "ownership transfer must remove the returned bitmap from disposal");
	}

	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void OverlaySerialization()
	{
		using var canvas = TestSurface(1, 1);
		var backing = new BlockingOverlayBacking();
		var service = new TestOverlayService(() => backing);
		var bounds = new ScreenRect(0, 0, 1, 1);
		var first = Task.Run(() => service.TryPresentImageOverlay(1, canvas, bounds, 255, true));

		Assert.IsTrue(backing.FirstShowEntered.Wait(TimeSpan.FromSeconds(2)));
		using var secondStarted = new ManualResetEventSlim();
		var second = Task.Run(() =>
		{
			secondStarted.Set();
			return service.TryPresentImageOverlay(1, canvas, bounds, 255, true);
		});
		Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(2)));

		backing.ReleaseShows.Set();
		Assert.IsTrue(first.GetAwaiter().GetResult());
		Assert.IsTrue(second.GetAwaiter().GetResult());
		Assert.That(backing.MaxConcurrentCalls, Is.EqualTo(1));
	}

	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void OverlayFailure()
	{
		using var canvas = TestSurface(1, 1);
		var backing = new BlockingOverlayBacking { ShowResult = false };
		backing.ReleaseShows.Set();
		var service = new TestOverlayService(() => backing);

		Assert.That(service.TryPresentImageOverlay(7, canvas, new ScreenRect(0, 0, 1, 1), 255, true), Is.False);
		Assert.IsTrue(backing.Disposed);
		Assert.That(service.GetImageOverlayHandle(7), Is.EqualTo(nint.Zero));
	}

	// Hide keeps the backing, so the next present reuses the same window and handle; an unconfirmed hide
	// reports failure so the caller retries, and only Dispose tears the backing down.
	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void OverlayHideKeepsBacking()
	{
		using var canvas = TestSurface(1, 1);
		var created = 0;
		var backing = new RecordingOverlayBacking();
		var service = new TestOverlayService(() =>
		{
			created++;
			return backing;
		});
		var bounds = new ScreenRect(0, 0, 1, 1);

		Assert.IsTrue(service.TryPresentImageOverlay(4, canvas, bounds, 255, true));
		backing.HideResult = false;
		Assert.That(service.TryHideImageOverlay(4), Is.False);
		backing.HideResult = true;
		Assert.IsTrue(service.TryHideImageOverlay(4));
		Assert.That(backing.Disposed, Is.False);
		Assert.That(service.GetImageOverlayHandle(4), Is.EqualTo((nint)123));

		Assert.IsTrue(service.TryPresentImageOverlay(4, canvas, bounds, 255, true));
		Assert.That(created, Is.EqualTo(1), "a Show after Hide must reuse the backing");

		Assert.IsTrue(service.TryHideImageOverlay(4));
		service.DisposeImageOverlay(4);
		Assert.IsTrue(backing.Disposed);
		Assert.That(service.GetImageOverlayHandle(4), Is.EqualTo(nint.Zero));
	}

	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void OverlayOwnerTeardownIsIsolated()
	{
		using var firstCanvas = TestSurface(1, 1);
		using var secondCanvas = TestSurface(1, 1);
		var firstBacking = new RecordingOverlayBacking();
		var secondBacking = new RecordingOverlayBacking();
		var thirdBacking = new RecordingOverlayBacking();
		var backings = new Queue<IImageOverlayBacking>([firstBacking, secondBacking, thirdBacking]);
		var service = new TestOverlayService(backings.Dequeue);
		var other = (Script)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Script));

		try
		{
			//Overlay slots are stamped with the current script; publish `other` while creating the ones that
			//must survive this script's HideAll (the way a leftover overlay from a previous script would).
			Assert.IsTrue(service.TryPresentImageOverlay(1, firstCanvas, new ScreenRect(0, 0, 1, 1), 255, true));

			Script.TheScript = other;
			Assert.IsTrue(service.TryPresentImageOverlay(2, secondCanvas, new ScreenRect(0, 0, 1, 1), 255, true));
			Script.TheScript = s;

			service.SetImageOverlayPointerSink(3, _ => { });

			Assert.IsTrue(service.DisposeAllImageOverlays(s));
			Assert.IsTrue(firstBacking.Disposed);
			Assert.That(secondBacking.Disposed, Is.False);
			Assert.That(service.GetImageOverlayHandle(1), Is.EqualTo(nint.Zero));
			Assert.That(service.GetImageOverlayHandle(2), Is.Not.EqualTo(nint.Zero));

			Script.TheScript = other;
			Assert.IsTrue(service.TryPresentImageOverlay(3, secondCanvas, new ScreenRect(0, 0, 1, 1), 255, true));

			Assert.IsNull(thirdBacking.PointerSink);
		}
		finally
		{
			Script.TheScript = s;
			_ = service.DisposeAllImageOverlays();
			GC.SuppressFinalize(other);
		}
	}

	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void ConcurrentHide()
	{
		using var canvas = TestSurface(1, 1);
		var backing = new BlockingOverlayBacking();
		var service = new TestOverlayService(() => backing);
		var showing = Task.Run(() => service.TryPresentImageOverlay(3, canvas,
			new ScreenRect(0, 0, 1, 1), 255, true));

		Assert.IsTrue(backing.FirstShowEntered.Wait(TimeSpan.FromSeconds(2)));
		using var hideStarted = new ManualResetEventSlim();
		var hiding = Task.Run(() =>
		{
			hideStarted.Set();
			return service.DisposeAllImageOverlays();
		});
		Assert.IsTrue(hideStarted.Wait(TimeSpan.FromSeconds(2)));
		Assert.IsTrue(SpinWait.SpinUntil(() => !IsOverlayRegistered(service, 3), TimeSpan.FromSeconds(2)),
			"HideAll must clear registration before waiting for an in-progress Show");
		backing.ReleaseShows.Set();

		Assert.That(showing.GetAwaiter().GetResult(), Is.False);
		Assert.IsTrue(hiding.GetAwaiter().GetResult());
		Assert.IsTrue(backing.Disposed);
		Assert.That(service.GetImageOverlayHandle(3), Is.EqualTo(nint.Zero));
	}

	// Observe HideAll's documented linearization point without adding a test-only production API.
	private static bool IsOverlayRegistered(OverlayBase service, uint id)
	{
		var type = typeof(OverlayBase);
		var sync = type.GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service);

		if (sync == null || type.GetField("overlays", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service) is not IDictionary overlays)
			throw new InvalidOperationException("OverlayBase registration fields were not found.");

		lock (sync)
			return overlays.Contains(id);
	}

	private static Bitmap SolidBitmap(int width, int height, int argb)
	{
		var bitmap = ImageHelper.NewArgbCanvas(width, height);
		using var graphics = ImageHelper.MakeGraphics(bitmap, highQuality: false);
		graphics.Clear(Color.FromArgb(argb));
		return bitmap;
	}

	private static OverlaySurface TestSurface(int w, int h)
		=> new(SolidBitmap(w, h, unchecked((int)0xFFFFFFFF)), new PixelSize(w, h), false);

	// A backing's dirty-rect path is only sound while it keeps receiving the same surface: its window
	// still holds what the last present put there. Presenting a different surface — which Overlay.Redraw
	// does every time — must therefore arrive as whole-surface damage, or the parts the new surface's own
	// damage does not name would keep showing the previous surface's pixels.
	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void UnseenSurfaceDamage()
	{
		var backing = new RecordingOverlayBacking();
		var service = new TestOverlayService(() => backing);
		var bounds = new ScreenRect(0, 0, 4, 4);

		using var first = TestSurface(4, 4);
		using var second = TestSurface(4, 4);

		Assert.IsTrue(service.TryPresentImageOverlay(1, first, bounds, 255, true));
		Assert.That(backing.LastDamage?.Kind, Is.EqualTo(DamageKind.All), "a surface never presented before is all-damaged");

		// Same surface again, this time carrying only a small region: the backing may top up.
		first.Damage.Reset();
		first.Damage.Add(new PixelRect(1, 1, 2, 2));
		Assert.IsTrue(service.TryPresentImageOverlay(1, first, bounds, 255, true));
		Assert.That(backing.LastDamage?.Kind, Is.EqualTo(DamageKind.Region), "the same surface may report a partial region");

		// A different surface with the same partial damage cannot be trusted.
		second.Damage.Reset();
		second.Damage.Add(new PixelRect(1, 1, 2, 2));
		Assert.IsTrue(service.TryPresentImageOverlay(1, second, bounds, 255, true));
		Assert.That(backing.LastDamage?.Kind, Is.EqualTo(DamageKind.All),
						"a surface this backing has not presented before must be transferred whole");
	}

	// A present that did not reach the screen must leave the damage standing, so the next one repaints those
	// pixels instead of losing them. This is the half of the contract that only shows up when something
	// fails, which is exactly when it matters.
	[Test, Category("Screen"), Category("Internal"), Category("Curated")]
	public void FailedPresentDamage()
	{
		var backing = new RecordingOverlayBacking { Result = false };
		var service = new TestOverlayService(() => backing);
		var bounds = new ScreenRect(0, 0, 4, 4);
		using var surface = TestSurface(4, 4);

		surface.Damage.Reset();
		surface.Damage.Add(new PixelRect(1, 1, 2, 2));
		Assert.That(service.TryPresentImageOverlay(1, surface, bounds, 255, true), Is.False);
		Assert.That(surface.Damage.Kind, Is.EqualTo(DamageKind.Region),
						"a present that failed must not clear the damage it did not paint");
	}

	private sealed class RecordingOverlayBacking : IImageOverlayBacking
	{
		internal bool Disposed;
		internal bool Result = true;
		internal bool HideResult = true;
		internal DamageList LastDamage;
		internal OverlaySurface LastSurface;

		public Action<OverlayPointerEvent> PointerSink { get; set; }
		public nint Handle => 123;
		public bool Present(OverlaySurface surface, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
			LastSurface = surface;
			LastDamage = damage;
			return Result;
		}

		public bool Move(ScreenRect bounds) => true;
		public bool Hide() => HideResult;
		public void Dispose() => Disposed = true;
	}

	private sealed class TestOverlayService : OverlayBase
	{
		private readonly Func<IImageOverlayBacking> create;

		internal TestOverlayService(Func<IImageOverlayBacking> create) => this.create = create;
		public override PixelSize GetCanvasSize(ScreenRect bounds) => new(bounds.Width, bounds.Height);
		protected override IImageOverlayBacking CreateBacking(uint id, Script owner) => create();
	}

	private sealed class BlockingOverlayBacking : IImageOverlayBacking
	{
		private int activeCalls;
		private int maxConcurrentCalls;

		internal readonly ManualResetEventSlim FirstShowEntered = new(false);
		internal readonly ManualResetEventSlim ReleaseShows = new(false);
		internal bool ShowResult = true;
		internal bool Disposed;
		internal int MaxConcurrentCalls => Volatile.Read(ref maxConcurrentCalls);

		public Action<OverlayPointerEvent> PointerSink { get; set; }

		public nint Handle => Disposed ? 0 : 123;

		public bool Present(OverlaySurface surface, ScreenRect bounds, byte opacity, bool clickThrough, DamageList damage)
		{
			var active = Interlocked.Increment(ref activeCalls);
			InterlockedExtensions.Max(ref maxConcurrentCalls, active);
			FirstShowEntered.Set();
			_ = ReleaseShows.Wait(TimeSpan.FromSeconds(2));
			_ = Interlocked.Decrement(ref activeCalls);
			return ShowResult;
		}

		public bool Move(ScreenRect bounds) => true;
		public bool Hide() => true;
		public void Dispose() => Disposed = true;
	}

	private static class InterlockedExtensions
	{
		internal static void Max(ref int target, int value)
		{
			int current;
			do
			{
				current = Volatile.Read(ref target);
				if (current >= value)
					return;
			}
			while (Interlocked.CompareExchange(ref target, value, current) != current);
		}
	}
}
