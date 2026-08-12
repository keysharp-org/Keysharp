#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class WaylandTests
	{
		[Test]
		public void OverlayMove()
		{
			var output = new WaylandLayerShellClient.OutputTarget(4, new nint(9),
				new ScreenRect(-100, 0, 200, 100), 1.25, 1);
			var current = new WaylandLayerShellClient.OutputSegment(output,
				new ScreenRect(-80, 10, 20, 20), 0, 0);

			Assert.That(LayerImageBacking.TryResolveSameOutputMove(current, 20, 20,
				new ScreenRect(60, 30, 20, 20), out var moved), Is.True);
			Assert.That(moved.Output, Is.EqualTo(output));
			Assert.That(moved.Bounds, Is.EqualTo(new ScreenRect(60, 30, 20, 20)));
			Assert.That(LayerImageBacking.TryResolveSameOutputMove(current, 20, 20,
				new ScreenRect(90, 30, 20, 20), out _), Is.False,
				"crossing an output edge must take the topology/repaint path");
		}

		[Test]
		public void ShellOverlayProbe()
		{
			IWaylandBackend backend = new TransientProbeOverlayBackend();
			Assert.That(backend.SupportsImageOverlay, Is.False,
				"the cached availability probe is allowed to miss");
			Assert.That(LinuxImageOverlayBacking.ShouldAttemptCompositor(backend), Is.True,
				"a shell backend must still issue the authoritative Show call");
		}

		[Test]
		public void BridgeDiagnostics()
		{
			var throttle = new WaylandDiagnosticThrottle(5000);
			Assert.That(throttle.TryAcquire("GNOME:NameHasOwner", 1000, out var suppressed), Is.True);
			Assert.That(suppressed, Is.Zero);
			Assert.That(throttle.TryAcquire("GNOME:NameHasOwner", 2000, out _), Is.False);
			Assert.That(throttle.TryAcquire("GNOME:ShowImageOverlay", 2000, out _), Is.True,
				"different operations must not suppress each other");
			Assert.That(throttle.TryAcquire("GNOME:NameHasOwner", 6000, out suppressed), Is.True);
			Assert.That(suppressed, Is.EqualTo(1));
		}

		[Test]
		public void ShmBufferLimit()
		{
			WaylandBufferState[] buffers =
			[
				new(100, 100, false),
				new(100, 100, true),
				new(200, 100, true)
			];

			Assert.That(WaylandBufferPoolPolicy.FindReusable(buffers, 100, 100), Is.EqualTo(1));
			Assert.That(WaylandBufferPoolPolicy.FindReusable(buffers, 300, 100), Is.EqualTo(-1));
			Assert.That(WaylandBufferPoolPolicy.CanAllocate(2), Is.True);
			Assert.That(WaylandBufferPoolPolicy.CanAllocate(WaylandBufferPoolPolicy.Capacity), Is.False,
				"three in-flight, wrong-sized buffers must drop the new frame instead of growing the pool");
		}

		[Test]
		public void AbsoluteMotion()
		{
			// Primary-only desktop, origin at (0,0): identity mapping, extent == width/height.
			Assert.That(WaylandVirtualPointerCoordinates.ToMotionAbsolute(100, 50, 0, 0, 1920, 1080),
				Is.EqualTo((100u, 50u, 1920u, 1080u)));

			// Secondary monitor left-of-primary gives a negative-origin virtual desktop; the pixel must be
			// translated into the layout's own non-negative coordinate space before being sent as x/y.
			Assert.That(WaylandVirtualPointerCoordinates.ToMotionAbsolute(-500, 200, -1920, 0, 3840, 1080),
				Is.EqualTo((1420u, 200u, 3840u, 1080u)));

			// Out-of-bounds targets clamp into [0, extent] rather than wrapping/underflowing to a huge uint.
			Assert.That(WaylandVirtualPointerCoordinates.ToMotionAbsolute(-10, -10, 0, 0, 1920, 1080),
				Is.EqualTo((0u, 0u, 1920u, 1080u)));

			// A zero-area virtual desktop (degenerate/pre-enumeration state) must not produce a zero extent --
			// wlroots silently drops motion_absolute when x_extent or y_extent is 0.
			Assert.That(WaylandVirtualPointerCoordinates.ToMotionAbsolute(0, 0, 0, 0, 0, 0),
				Is.EqualTo((0u, 0u, 1u, 1u)));
		}

		[Test]
		public void ScreencopyRetirement()
		{
			FakeSession current = new();
			var first = current;
			Assert.That(WaylandScreenCapture.RunWithReusableSession(ref current, _ => (new object(), true)), Is.Not.Null);
			Assert.That(current, Is.SameAs(first));

			Assert.That(WaylandScreenCapture.RunWithReusableSession<FakeSession, object>(ref current,
				_ => (null, true)), Is.Null);
			Assert.That(first.Disposed, Is.False);
			Assert.That(current, Is.SameAs(first));

			Assert.That(WaylandScreenCapture.RunWithReusableSession<FakeSession, object>(ref current,
				_ => (null, false)), Is.Null);
			Assert.That(first.Disposed, Is.True);
			Assert.That(current, Is.Null);
		}

		[Test]
		public void WindowEventRecovery()
		{
			var available = false;
			var preferredAttempts = 0;
			var fallbackStarts = 0;
			Action availabilityChanged = null;
			Action<Exception> streamError = null;
			var fallbacks = new List<TrackingDisposable>();
			var preferred = new TrackingDisposable();

			using var source = new RecoveringSubscription(
				onError =>
				{
					preferredAttempts++;
					streamError = onError;
					return preferred;
				},
				() =>
				{
					fallbackStarts++;
					var fallback = new TrackingDisposable();
					fallbacks.Add(fallback);
					return fallback;
				},
				() => available,
				handler =>
				{
					availabilityChanged = handler;
					return new TrackingDisposable();
				},
				retryIntervalMs: 60_000);
			source.Start();

			Assert.That(source.IsPreferred, Is.False);
			Assert.That(preferredAttempts, Is.Zero, "known owner absence must not consume retry attempts");
			Assert.That(fallbackStarts, Is.EqualTo(1));

			available = true;
			availabilityChanged();
			Assert.That(source.IsPreferred, Is.True);
			Assert.That(preferredAttempts, Is.EqualTo(1));
			Assert.That(fallbacks[0].Disposed, Is.True);

			streamError(new IOException("signal stream failed"));
			Assert.That(source.IsPreferred, Is.False);
			Assert.That(preferred.Disposed, Is.True);
			Assert.That(fallbackStarts, Is.EqualTo(2));
		}

		private sealed class FakeSession : IDisposable
		{
			internal bool Disposed;
			public void Dispose() => Disposed = true;
		}

		private sealed class TrackingDisposable : IDisposable
		{
			internal bool Disposed { get; private set; }
			public void Dispose() => Disposed = true;
		}

		private sealed class TransientProbeOverlayBackend : IWaylandBackend
		{
			public string Name => "shell-test";
			public bool CanAttemptImageOverlay => true;

			public bool TryGetCursorPos(out int x, out int y)
			{
				x = y = 0;
				return false;
			}
		}
	}
}
#endif
