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
		public void CosmicGeometryAndProtocolHelpers()
		{
			Assert.That(WaylandForeignToplevels.TryResolveGeometry(new Rectangle(120, 40, 800, 600),
				new ScreenRect(-1920, 0, 1920, 1080), out var resolved), Is.True);
			Assert.That(resolved, Is.EqualTo(new Rectangle(-1800, 40, 800, 600)));
			Assert.That(WaylandForeignToplevels.TryResolveGeometry(new Rectangle(0, 0, 20, 20),
				new ScreenRect(int.MaxValue - 10, 0, 10, 10), out _), Is.False);
			Assert.That(WaylandForeignToplevels.TryResolveGeometry(new Rectangle(0, 0, 20, 20),
				new ScreenRect(0, int.MaxValue - 10, 10, 10), out _), Is.False);
			var first = WaylandForeignToplevels.NewHandle();
			var second = WaylandForeignToplevels.NewHandle();
			Assert.That(first.ToInt64(), Is.LessThan(0L));
			Assert.That(second.ToInt64(), Is.LessThan(first.ToInt64()));
			Assert.That(WaylandForeignToplevels.FoldBitSet([0u, 2u, 2u, 31u, 32u, uint.MaxValue], 32),
				Is.EqualTo((1UL << 0) | (1UL << 2) | (1UL << 31)));
			Assert.That(WaylandForeignToplevels.FoldBitSet([1u, 5u, 63u, 64u], 64),
				Is.EqualTo((1UL << 1) | (1UL << 5) | (1UL << 63)));
		}

		[Test]
		public void CosmicStateCommitsAtomically()
		{
			var output = new nint(7);
			var state = new WaylandToplevel { State = 1, PendingState = 4 };
			state.GeometryByOutput[output] = new Rectangle(1, 2, 3, 4);
			state.PendingGeometryByOutput = new() { [output] = new Rectangle(10, 20, 30, 40) };
			Assert.That(state.State, Is.EqualTo(1));
			Assert.That(state.GeometryByOutput[output], Is.EqualTo(new Rectangle(1, 2, 3, 4)));
			WaylandForeignToplevels.CommitCosmicUpdate(state);
			Assert.That(state.State, Is.EqualTo(4));
			Assert.That(state.GeometryByOutput[output], Is.EqualTo(new Rectangle(10, 20, 30, 40)));
			Assert.That(state.CosmicReady, Is.True);
			Assert.That(state.PendingState, Is.Null);
			Assert.That(state.PendingGeometryByOutput, Is.Null);
		}

		[Test]
		public void WaylandOutputChangesCommitAtomically()
		{
			var output = new WaylandOutput { Version = 4 };
			output.OutputPending.GeometryX = -1920;
			output.OutputPending.ModeWidth = 1920;
			output.OutputPending.ModeHeight = 1080;
			output.OutputPending.Name = "wl-output";
			output.XdgPending.LogicalX = 40;
			output.XdgPending.HasLogicalPosition = true;
			output.XdgPending.Name = "stale-xdg-name";
			output.CommitOutput(false);
			Assert.That(output.Bounds, Is.EqualTo(new ScreenRect(-1920, 0, 1920, 1080)));
			Assert.That(output.LogicalX, Is.Zero);
			output.CommitXdg();
			Assert.That(output.Bounds.X, Is.EqualTo(40));
			Assert.That(output.Name, Is.EqualTo("wl-output"));
			output.OutputPending.GeometryX = 0;
			Assert.That(output.GeometryX, Is.EqualTo(-1920));
			output.CommitOutput(false);
			Assert.That(output.GeometryX, Is.Zero);
			Assert.That(output.Bounds.X, Is.EqualTo(40));

			var legacy = new WaylandOutput { Version = 1 };
			legacy.OutputPending.ModeWidth = 640;
			legacy.OutputPending.ModeHeight = 480;
			legacy.CommitLegacyOutput();
			Assert.That(legacy.Bounds, Is.EqualTo(new ScreenRect(0, 0, 640, 480)));
		}

		[Test]
		public void DisplayPumpHonorsItsDeadline()
		{
			long tick = 100;
			var dispatches = 0;
			var completed = WaylandDisplayPump.WaitUntil(() => false, 25,
				remaining => { dispatches++; tick += remaining; return true; }, () => tick);
			Assert.That(completed, Is.False);
			Assert.That(dispatches, Is.EqualTo(1));
		}

		[Test]
		public void PollingDerivesActivationFromTheListSnapshot() =>
			Assert.That(WaylandPollingEventSource.ActiveHandle(
			[
				new(new nint(10), active: false),
				new(new nint(20), active: true),
				new(new nint(30), active: false)
			]), Is.EqualTo(new nint(20)));

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
