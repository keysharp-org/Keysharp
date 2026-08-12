using Keysharp.Internals;

namespace Keysharp.Tests
{
	[Category("Internal"), Category("Curated")]
	public class RetryGateTests
	{
		[Test]
		public void RetryLimit()
		{
			var time = new ManualTimeProvider();
			var gate = new RetryGate(time, 3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

			for (var i = 0; i < 3; i++)
			{
				using (var attempt = gate.TryBegin())
					Assert.That(attempt, Is.Not.Null);

				time.Advance(TimeSpan.FromSeconds(1));
			}

			Assert.That(gate.TryBegin(), Is.Null);
			gate.Rearm();
			Assert.That(gate.TryBegin(), Is.Not.Null);
		}

		[Test]
		public void SuccessResetsFailures()
		{
			var time = new ManualTimeProvider();
			var gate = new RetryGate(time, 1, TimeSpan.Zero, TimeSpan.Zero);

			using (gate.TryBegin()) { }
			Assert.That(gate.TryBegin(), Is.Null);
			gate.Rearm();

			using (var attempt = gate.TryBegin())
				attempt.Succeed();

			Assert.That(gate.FailureCount, Is.Zero);
			Assert.That(gate.TryBegin(), Is.Not.Null);
		}

		[Test]
		public void StaleAttempt()
		{
			var gate = new RetryGate(maximumAttempts: 1, initialRetryDelay: TimeSpan.Zero,
				maximumRetryDelay: TimeSpan.Zero);
			var stale = gate.TryBegin();
			gate.Rearm();
			stale.Dispose();

			Assert.That(gate.TryBegin(), Is.Not.Null);
		}

		[Test]
		public void Suspension()
		{
			var gate = new RetryGate();
			gate.Suspend();
			Assert.That(gate.TryBegin(), Is.Null);
			gate.Rearm();
			Assert.That(gate.TryBegin(), Is.Not.Null);
		}

		private sealed class ManualTimeProvider : TimeProvider
		{
			private long timestamp = 1;
			public override long TimestampFrequency => TimeSpan.TicksPerSecond;
			public override long GetTimestamp() => timestamp;
			internal void Advance(TimeSpan duration) => timestamp += duration.Ticks;
		}
	}
}
