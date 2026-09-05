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
		public void GenericWindowListUsesStableOpaqueHandles()
		{
			var backend = new GenericWaylandBackend();
			const string first = "{\"ok\":true,\"windows\":[{\"id\":\"opaque:7\",\"title\":\"Editor\",\"class\":\"org.example.Editor\",\"frame\":{\"x\":-50,\"y\":20,\"width\":640,\"height\":480},\"client\":{\"x\":-48,\"y\":42,\"width\":636,\"height\":456},\"active\":true,\"visible\":false,\"decorated\":true}]}";
			const string second = "{\"ok\":true,\"windows\":[{\"id\":\"opaque:7\",\"title\":\"Renamed\",\"class\":\"org.example.Editor\"}]}";

			Assert.That(backend.TryParseWindowList(first, out var initial), Is.True);
			Assert.That(backend.TryParseWindowList(second, out var refreshed), Is.True);
			Assert.That(initial, Has.Count.EqualTo(1));
			Assert.That(refreshed, Has.Count.EqualTo(1));
			Assert.That(refreshed[0].Handle, Is.EqualTo(initial[0].Handle));
			Assert.That(refreshed[0].Title, Is.EqualTo("Renamed"));
			Assert.That(refreshed[0].ClassName, Is.EqualTo("org.example.Editor"));
			Assert.That(initial[0].FrameGeometry, Is.EqualTo(new Rectangle(-50, 20, 640, 480)));
			Assert.That(initial[0].ClientGeometry, Is.EqualTo(new Rectangle(-48, 42, 636, 456)));
			Assert.That(initial[0].Active, Is.True);
			Assert.That(initial[0].Visible, Is.False);
			Assert.That(initial[0].Decorated, Is.True);
			Assert.That(refreshed[0].Visible, Is.True,
				"missing portable fields retain their conservative defaults");
			Assert.That(backend.IsKnown(initial[0].Handle), Is.True);
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
