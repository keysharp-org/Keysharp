using System.Collections;
using System.Reflection;
using Keysharp.Internals;
using Keysharp.Internals.Images;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
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
				Assert.AreEqual(8, result.Width);

				for (var x = 0; x < 5; x++)
					Assert.AreEqual(unchecked((int)0xFFFF0000), result.GetPixel(x, 0).ToArgb());

				for (var x = 5; x < 8; x++)
					Assert.AreEqual(unchecked((int)0xFF0000FF), result.GetPixel(x, 0).ToArgb());
			}
			finally
			{
				foreach (var capture in captures)
					capture.Pixels.Dispose();
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
			Assert.AreSame(source, result);
			Assert.AreEqual(0, captures.Count, "ownership transfer must remove the returned bitmap from disposal");
		}

		[Test, Category("Screen"), Category("Internal"), Category("Curated")]
		public void OverlaySerialization()
		{
			using var image = SolidBitmap(1, 1, unchecked((int)0xFFFFFFFF));
			var backing = new BlockingOverlayBacking();
			var service = new TestOverlayService(() => backing);
			var bounds = new ScreenRect(0, 0, 1, 1);
			var first = Task.Run(() => service.TryShowImageOverlay(1, bounds, image, true));

			Assert.IsTrue(backing.FirstShowEntered.Wait(TimeSpan.FromSeconds(2)));
			using var secondStarted = new ManualResetEventSlim();
			var second = Task.Run(() =>
			{
				secondStarted.Set();
				return service.TryShowImageOverlay(1, bounds, image, true);
			});
			Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(2)));

			backing.ReleaseShows.Set();
			Assert.IsTrue(first.GetAwaiter().GetResult());
			Assert.IsTrue(second.GetAwaiter().GetResult());
			Assert.AreEqual(1, backing.MaxConcurrentCalls);
		}

		[Test, Category("Screen"), Category("Internal"), Category("Curated")]
		public void OverlayFailure()
		{
			using var image = SolidBitmap(1, 1, unchecked((int)0xFFFFFFFF));
			var backing = new BlockingOverlayBacking { ShowResult = false };
			backing.ReleaseShows.Set();
			var service = new TestOverlayService(() => backing);

			Assert.IsFalse(service.TryShowImageOverlay(7, new ScreenRect(0, 0, 1, 1), image, true));
			Assert.IsTrue(backing.Disposed);
			Assert.AreEqual(nint.Zero, service.GetImageOverlayHandle(7));
		}

		[Test, Category("Screen"), Category("Internal"), Category("Curated")]
		public void ConcurrentHide()
		{
			using var image = SolidBitmap(1, 1, unchecked((int)0xFFFFFFFF));
			var backing = new BlockingOverlayBacking();
			var service = new TestOverlayService(() => backing);
			var showing = Task.Run(() => service.TryShowImageOverlay(3,
				new ScreenRect(0, 0, 1, 1), image, true));

			Assert.IsTrue(backing.FirstShowEntered.Wait(TimeSpan.FromSeconds(2)));
			using var hideStarted = new ManualResetEventSlim();
			var hiding = Task.Run(() =>
			{
				hideStarted.Set();
				return service.TryHideAllImageOverlays();
			});
			Assert.IsTrue(hideStarted.Wait(TimeSpan.FromSeconds(2)));
			Assert.IsTrue(SpinWait.SpinUntil(() => !IsOverlayRegistered(service, 3), TimeSpan.FromSeconds(2)),
				"HideAll must clear registration before waiting for an in-progress Show");
			backing.ReleaseShows.Set();

			Assert.IsFalse(showing.GetAwaiter().GetResult());
			Assert.IsTrue(hiding.GetAwaiter().GetResult());
			Assert.IsTrue(backing.Disposed);
			Assert.AreEqual(nint.Zero, service.GetImageOverlayHandle(3));
		}

		// Observe HideAll's documented linearization point without adding a test-only production API.
		private static bool IsOverlayRegistered(OverlayBase service, uint id)
		{
			var type = typeof(OverlayBase);
			var sync = type.GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service);
			var overlays = type.GetField("overlays", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(service)
				as IDictionary;

			if (sync == null || overlays == null)
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

		private sealed class TestOverlayService : OverlayBase
		{
			private readonly Func<IImageOverlayBacking> create;

			internal TestOverlayService(Func<IImageOverlayBacking> create) => this.create = create;
			public override PixelSize GetCanvasSize(ScreenRect bounds) => new(bounds.Width, bounds.Height);
			protected override IImageOverlayBacking CreateBacking(uint id) => create();
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

			public bool Show(Bitmap image, ScreenRect bounds, bool clickThrough)
			{
				var active = Interlocked.Increment(ref activeCalls);
				InterlockedExtensions.Max(ref maxConcurrentCalls, active);
				FirstShowEntered.Set();
				ReleaseShows.Wait(TimeSpan.FromSeconds(2));
				_ = Interlocked.Decrement(ref activeCalls);
				return ShowResult;
			}

			public bool Move(ScreenRect bounds) => true;
			public bool TryHide() => true;
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
}
