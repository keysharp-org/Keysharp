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
		public void WindowParserTracksExplicitAndLegacyFieldPresence()
		{
			const string explicitFields =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"Editor\",\"active\":true,\"validFields\":[\"title\"]}}";
			const string legacyFields =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"Editor\",\"active\":true}}";
			const string malformedFields =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"placeholder\",\"active\":true,\"validFields\":null}}";
			static nint Resolve(string id) => new(long.Parse(id, CultureInfo.InvariantCulture));

			Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(explicitFields), Resolve,
				out var explicitWindow), Is.True);
			Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(legacyFields), Resolve,
				out var legacyWindow), Is.True);
			Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(malformedFields), Resolve,
				out var malformedWindow), Is.True);
			Assert.Multiple(() =>
			{
				Assert.That(explicitWindow.HasKnownField(WaylandWindowFields.Title), Is.True);
				Assert.That(explicitWindow.HasKnownField(WaylandWindowFields.Active), Is.False);
				Assert.That(legacyWindow.HasKnownField(WaylandWindowFields.Title), Is.True);
				Assert.That(legacyWindow.HasKnownField(WaylandWindowFields.Active), Is.True);
				Assert.That(malformedWindow.HasKnownField(WaylandWindowFields.Title), Is.False);
				Assert.That(malformedWindow.HasKnownField(WaylandWindowFields.Active), Is.False);
			});
		}

		[Test]
		public void PollingIgnoresUnknownFieldsWithoutLosingItsBaseline()
		{
			const string initial =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"Editor\",\"minimized\":false,\"active\":true,\"frame\":{\"x\":10,\"y\":20,\"width\":300,\"height\":200},\"validFields\":[\"title\",\"minimized\",\"active\",\"frame\"]}}";
			const string partial =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"placeholder\",\"minimized\":true,\"active\":false,\"frame\":{\"x\":99,\"y\":99,\"width\":1,\"height\":1},\"validFields\":[]}}";
			const string changed =
				"{\"ok\":true,\"window\":{\"id\":\"1\",\"title\":\"Renamed\",\"minimized\":true,\"active\":true,\"frame\":{\"x\":15,\"y\":25,\"width\":300,\"height\":200},\"validFields\":[\"title\",\"minimized\",\"active\",\"frame\"]}}";
			static nint Resolve(string id) => new(long.Parse(id, CultureInfo.InvariantCulture));
			static WaylandWindowInfo Parse(string json)
			{
				Assert.That(DesktopWindowParser.TrySingle(Encoding.UTF8.GetBytes(json), Resolve,
					out var window), Is.True);
				return window;
			}

			var original = Parse(initial);
			var unknown = Parse(partial);
			var tracker = new WaylandWindowSnapshotTracker();
			var events = new List<WaylandWindowEvent>();
			tracker.Update([original], events.Add);
			tracker.Update([unknown], events.Add);
			tracker.Update([Parse(initial)], events.Add);

			Assert.That(events, Is.Empty,
				"an unknown interval must neither emit placeholder changes nor erase the prior baseline");

			tracker.Update([Parse(changed)], events.Add);
			Assert.Multiple(() =>
			{
				Assert.That(events.Select(windowEvent => windowEvent.Kind), Is.EqualTo(new[]
				{
					WaylandWindowEventKind.TitleChanged,
					WaylandWindowEventKind.Minimized,
					WaylandWindowEventKind.MoveResized
				}));
				Assert.That(events[^1].Bounds, Is.EqualTo(new Rectangle(15, 25, 300, 200)));
				Assert.That(events.Any(windowEvent => windowEvent.Kind == WaylandWindowEventKind.Activated), Is.False,
					"an unknown active field must not clear the last trustworthy active handle");
			});
		}

		[Test]
		public void GenericWindowListUsesStableOpaqueHandles()
		{
			var backend = new DesktopBackend("generic", "generic Wayland");
			const string first = "{\"ok\":true,\"windows\":[{\"id\":\"opaque:7\",\"compositorId\":\"river:editor\",\"title\":\"Editor\",\"appId\":\"org.example.Editor\",\"frame\":{\"x\":-50,\"y\":20,\"width\":640,\"height\":480},\"client\":{\"x\":-48,\"y\":42,\"width\":636,\"height\":456},\"active\":true,\"visible\":false,\"decorated\":true,\"validFields\":[\"id\",\"compositorId\",\"title\",\"appId\",\"frame\",\"client\",\"active\",\"visible\",\"decorated\"]}]}";
			const string second = "{\"ok\":true,\"windows\":[{\"id\":\"opaque:7\",\"compositorId\":\"river:editor\",\"title\":\"Renamed\",\"appId\":\"org.example.Editor\",\"validFields\":[\"id\",\"compositorId\",\"title\",\"appId\"]}]}";

			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(first), out var initial), Is.True);
			Assert.That(backend.TryParseWindowList(Encoding.UTF8.GetBytes(second), out var refreshed), Is.True);
			Assert.That(initial, Has.Count.EqualTo(1));
			Assert.That(refreshed, Has.Count.EqualTo(1));
			Assert.That(initial[0].Handle.ToInt64(), Is.LessThan(0),
				"synthetic Wayland handles must not overlap the non-negative X11 XID space");
			Assert.That(refreshed[0].Handle, Is.EqualTo(initial[0].Handle));
			Assert.That(refreshed[0].CompositorId, Is.EqualTo("river:editor"));
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
		public void SyntheticWindowHandleSurvivesDisappearAndReappear()
		{
			var handles = new SyntheticWindowHandleMap<string>();
			var original = handles.GetOrCreate("opaque:7");

			Assert.That(handles.Retain([]), Is.EqualTo(new[] { original }));
			Assert.That(handles.Contains(original), Is.False);

			var reappeared = handles.GetOrCreate("opaque:7");
			Assert.That(reappeared, Is.EqualTo(original));
			Assert.That(handles.Contains(original), Is.True);

			_ = handles.Retain([]);
			_ = handles.Retain([]);
			Assert.That(handles.GetOrCreate("opaque:7"), Is.Not.EqualTo(original));
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

		[Test]
		public void SubscriptionFailureDuringSetup()
		{
			var fail = true;
			var rejected = new TrackingDisposable();
			var replacement = new TrackingDisposable();
			using var source = new RecoveringSubscription(
				onError =>
				{
					if (!fail) return replacement;
					onError(new IOException("stream failed before setup returned"));
					return rejected;
				},
				() => new TrackingDisposable(), () => true, null, retryIntervalMs: 60_000);

			Assert.That(source.TryAttachPreferred(), Is.False);
			Assert.That(rejected.Disposed, Is.True);
			fail = false;
			Assert.That(source.TryAttachPreferred(), Is.True);
			Assert.That(replacement.Disposed, Is.False);
		}

		[Test]
		public void FallbackFactoryCanDisposeOwnerWithoutPublishingItsLateResult()
		{
			RecoveringSubscription source = null;
			var fallback = new TrackingDisposable();
			var ownerDisposeCompleted = false;
			Exception ownerDisposeError = null;
			source = new RecoveringSubscription(
				_ => null,
				() =>
				{
					ownerDisposeCompleted = RunOnWorker(source.Dispose, out ownerDisposeError);
					return fallback;
				},
				() => false,
				null,
				retryIntervalMs: 60_000);

			source.Start();

			Assert.Multiple(() =>
			{
				Assert.That(ownerDisposeCompleted, Is.True,
					"the fallback factory must not run while the owner state lock is held");
				Assert.That(ownerDisposeError, Is.Null);
				Assert.That(fallback.Disposed, Is.True,
					"a fallback returned after owner disposal must be retired instead of published");
				Assert.That(source.IsPreferred, Is.False);
			});
		}

		[Test]
		public void StalePreferredDisposerCanReenterOwner()
		{
			RecoveringSubscription source = null;
			Action availabilityChanged = null;
			var available = true;
			var reentryCompleted = false;
			Exception reentryError = null;
			var preferred = new CallbackDisposable(() =>
				reentryCompleted = RunOnWorker(source.Dispose, out reentryError));
			source = new RecoveringSubscription(
				_ =>
				{
					available = false;
					availabilityChanged();
					return preferred;
				},
				() => null,
				() => available,
				handler =>
				{
					availabilityChanged = handler;
					return null;
				},
				retryIntervalMs: 60_000);

			source.Start();

			Assert.Multiple(() =>
			{
				Assert.That(preferred.Disposed, Is.True);
				Assert.That(reentryCompleted, Is.True,
					"stale preferred cleanup must not hold the owner state lock");
				Assert.That(reentryError, Is.Null);
				Assert.That(source.IsPreferred, Is.False);
			});
		}

		private static bool RunOnWorker(Action action, out Exception error)
		{
			Exception workerError = null;
			var worker = new Thread(() =>
			{
				try { action(); }
				catch (Exception ex) { workerError = ex; }
			}) { IsBackground = true };
			worker.Start();
			var completed = worker.Join(2000);
			error = workerError;
			return completed;
		}

		private sealed class TrackingDisposable : IDisposable
		{
			internal bool Disposed { get; private set; }
			public void Dispose() => Disposed = true;
		}

		private sealed class CallbackDisposable(Action onDispose) : IDisposable
		{
			internal bool Disposed { get; private set; }
			public void Dispose()
			{
				Disposed = true;
				onDispose();
			}
		}

		private sealed class TransientProbeOverlayBackend : IWaylandBackend
		{
			public string BackendKey => "shell-test";
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
