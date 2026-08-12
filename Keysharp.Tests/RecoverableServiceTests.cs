using Keysharp.Internals;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Misc"), Category("Internal"), Category("Curated")]
	public class RecoverableServiceTests
	{
		[Test]
		public void LeaseDisposal()
		{
			var created = new TrackedService();
			using var service = new RecoverableService<TrackedService>(() => created);
			var lease = service.TryAcquire();

			Assert.That(lease, Is.Not.Null);
			service.Invalidate(created);
			Assert.That(created.Disposed, Is.False);

			lease.Dispose();
			Assert.That(created.Disposed, Is.True);
		}

		[Test]
		public void StaleInvalidation()
		{
			var first = new TrackedService();
			var second = new TrackedService();
			var queue = new Queue<TrackedService>([first, second]);
			using var service = new RecoverableService<TrackedService>(() => queue.Dequeue(),
				initialRetryDelay: TimeSpan.Zero);

			using var firstLease = service.TryAcquire();
			service.Invalidate(first);
			using var secondLease = service.TryAcquire();
			service.Invalidate(first);

			Assert.That(secondLease, Is.Not.Null);
			Assert.That(second.Disposed, Is.False);
		}

		[Test]
		public void FailureBudget()
		{
			var attempts = 0;
			var clock = new ManualTimeProvider();
			using var service = new RecoverableService<TrackedService>(
				() => { attempts++; return null; },
				timeProvider: clock,
				maximumAttempts: 3,
				initialRetryDelay: TimeSpan.FromMilliseconds(100),
				maximumRetryDelay: TimeSpan.FromMilliseconds(100));

			for (var i = 0; i < 3; i++)
			{
				Assert.That(service.TryAcquire(), Is.Null);
				clock.Advance(TimeSpan.FromMilliseconds(101));
			}

			Assert.That(service.TryAcquire(), Is.Null);
			Assert.That(attempts, Is.EqualTo(3));

			service.Rearm();
			Assert.That(service.TryAcquire(), Is.Null);
			Assert.That(attempts, Is.EqualTo(4));
		}

		[Test]
		public void StaleFactory()
		{
			var entered = new ManualResetEventSlim();
			var release = new ManualResetEventSlim();
			var old = new TrackedService();
			using var service = new RecoverableService<TrackedService>(() =>
			{
				entered.Set();
				release.Wait();
				return old;
			});

			var task = Task.Run(() => service.TryAcquire());
			Assert.That(entered.Wait(1000), Is.True);
			service.Rearm();
			release.Set();
			Assert.That(task.GetAwaiter().GetResult(), Is.Null);
			Assert.That(old.Disposed, Is.True);
		}

		private sealed class TrackedService : IDisposable
		{
			internal bool Disposed { get; private set; }
			public void Dispose() => Disposed = true;
		}

		private sealed class ManualTimeProvider : TimeProvider
		{
			private long timestamp = 1;
			public override long GetTimestamp() => timestamp;
			public override long TimestampFrequency => 1000;
			internal void Advance(TimeSpan elapsed) => timestamp += (long)elapsed.TotalMilliseconds;
		}
	}
}
