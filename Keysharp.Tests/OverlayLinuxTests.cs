#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Window.Linux.Wayland;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public class OverlayLinuxTests
	{
		[Test]
		public void DamageHistoryTracksChangesRatherThanColdCopies()
		{
			var history = new WaylandDamageHistory(4);
			var first = history.Commit(WaylandFrameDamage.All);
			var secondDamage = WaylandFrameDamage.Region(new PixelRect(1, 1, 2, 2));

			Assert.That(history.Resolve(-1, secondDamage).Kind, Is.EqualTo(DamageKind.All));
			history.Commit(secondDamage);

			var thirdDamage = WaylandFrameDamage.Region(new PixelRect(4, 4, 2, 2));
			Assert.That(history.Resolve(-1, thirdDamage).Kind, Is.EqualTo(DamageKind.All));
			history.Commit(thirdDamage);

			var replay = history.Resolve(first,
				WaylandFrameDamage.Region(new PixelRect(7, 7, 1, 1)));
			Assert.That(replay.Kind, Is.EqualTo(DamageKind.Region));
			Assert.That(replay.Bounds, Is.EqualTo(new PixelRect(1, 1, 7, 7)));
		}

		[Test]
		public void DamageHistoryPreservesNoChangeFrames()
		{
			var history = new WaylandDamageHistory(4);
			var first = history.Commit(WaylandFrameDamage.All);

			Assert.That(history.Resolve(-1, WaylandFrameDamage.None).Kind, Is.EqualTo(DamageKind.All));
			history.Commit(WaylandFrameDamage.None);

			Assert.That(history.Resolve(first, WaylandFrameDamage.None).Kind, Is.EqualTo(DamageKind.None));
		}

		[Test]
		public void BufferInterpretationIncludesSourceAndOpacity()
		{
			var current = new WaylandBufferInterpretation(100, 50, new Rectangle(0, 0, 100, 50), 255);

			Assert.That(current, Is.Not.EqualTo(
				new WaylandBufferInterpretation(100, 50, new Rectangle(1, 0, 100, 50), 255)));
			Assert.That(current, Is.Not.EqualTo(
				new WaylandBufferInterpretation(100, 50, new Rectangle(0, 0, 100, 50), 128)));
		}

		[Test]
		public void ActorMoveCannotResizeUnpublishedPixels()
		{
			var current = new ScreenRect(10, 20, 100, 50);

			Assert.That(CompositorImageBacking.CanMoveWithoutUpload(current,
				new ScreenRect(30, 40, 100, 50)), Is.True);
			Assert.That(CompositorImageBacking.CanMoveWithoutUpload(current,
				new ScreenRect(30, 40, 101, 50)), Is.False);
		}

		[Test]
		public void SharedFrameBufferIsWritableAndPrivateAndCleansUp()
		{
			var buffer = OverlayShmBuffer.Create(id: 7, width: 4, height: 3);

			Assert.That(buffer, Is.Not.Null);

			try
			{
				Assert.That(buffer.Stride, Is.EqualTo(16));
				Assert.That(new FileInfo(buffer.Path).Length, Is.EqualTo(48));
				if (OperatingSystem.IsLinux())   // the guard is the platform analyzer's, not ours
					Assert.That(File.GetUnixFileMode(buffer.Path),
						Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
				// The shell recognises its own clients' buffers by this name, and maps nothing else.
				Assert.That(Path.GetFileName(buffer.Path), Does.StartWith("keysharp-overlay-"));

				// What the client writes through the mapping is what a reader of the file sees: that is the
				// whole point of the path.
				unsafe { ((uint*)buffer.Data)[0] = 0xDEADBEEF; }

				var bytes = File.ReadAllBytes(buffer.Path);
				Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(0xDEADBEEFu));
			}
			finally
			{
				buffer.Dispose();
			}

			Assert.That(File.Exists(buffer.Path), Is.False);
		}

		[Test]
		public void SharedFrameBuffersNeverReuseAName()
		{
			var first = OverlayShmBuffer.Create(id: 8, width: 2, height: 2);
			var second = OverlayShmBuffer.Create(id: 8, width: 4, height: 4);

			try
			{
				// The shell keys its mapping on the path, so a resized overlay that reused the name would
				// leave it uploading from the old, shorter mapping.
				Assert.That(second.Path, Is.Not.EqualTo(first.Path));
			}
			finally
			{
				first?.Dispose();
				second?.Dispose();
			}
		}

		[TestCase(1, 1, true)]
		[TestCase(2, 2, true)]
		[TestCase(3, 3, true)]
		[TestCase(0, 0, false)]
		[TestCase(2, 1, false)]
		public void StableOutputCountCanReuseEveryFragment(int segmentCount, int fragmentCount, bool expected)
			=> Assert.That(LayerImageBacking.CanReuseFragmentCount(segmentCount, fragmentCount), Is.EqualTo(expected));

		[Test]
		public void LayerTeardownRetriesOnlyFailedFragments()
		{
			var failed = new RetryableDisposable(1);
			var retired = new RetryableDisposable(0);
			var fragments = new Dictionary<uint, RetryableDisposable>
			{
				[1] = failed,
				[2] = retired
			};

			Assert.That(LayerImageBacking.TryRetire(fragments, fragment => fragment.Dispose()), Is.False);
			Assert.That(fragments.Keys, Is.EqualTo(new[] { 1u }));
			Assert.That(LayerImageBacking.TryRetire(fragments, fragment => fragment.Dispose()), Is.True);
			Assert.That(fragments, Is.Empty);
			Assert.That(failed.Attempts, Is.EqualTo(2));
			Assert.That(retired.Attempts, Is.EqualTo(1));
		}

		private sealed class RetryableDisposable(int failures)
		{
			internal int Attempts { get; private set; }

			internal void Dispose()
			{
				if (Attempts++ < failures)
					throw new IOException("teardown failed");
			}
		}
	}
}
#endif
