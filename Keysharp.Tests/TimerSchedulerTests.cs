namespace Keysharp.Tests;

[TestFixture, NonParallelizable]
public class TimerSchedulerTests : TestRunner
{
	[Test, Category("Threading")]
	public void BlockedTimer()
	{
		var output = RunScript(string.Concat(path, "timer-blocked-fires-once.ahk"), "timer-blocked-fires-once", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
	}

	[Test, Category("Threading")]
	public void ShortTimerPeriod() => Assert.IsTrue(TestScript("timer-short-callback-keeps-period", false));

	[Test, Category("Threading")]
	public void LongTimerCoalescing() => Assert.IsTrue(TestScript("timer-long-callback-coalesces", false));

	[Test, Category("Threading")]
	public void CallbackSleep() => Assert.IsTrue(TestScript("timer-during-callback-sleep", false));

	[Test, Category("Threading")]
	public void BlockedAdmission() => Assert.IsTrue(TestScript("timer-blocked-admission", false));

	[Test, Category("Threading"), Category("Internal")]
	public void NotificationDuringPump()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		_ = scheduler.EnqueueCallback(() =>
		{
			scheduler.WakeForTimerCheck();
			scheduler.WakeForTimerCheck();
		});

		scheduler.PumpThreadQueuedEventsCore();
		// The original post is still in the test transport; the active pass must add one follow-up.
		Assert.That(context.PendingCount, Is.EqualTo(2));
		context.DrainAll();
		Assert.That(context.PendingCount, Is.Zero);
	}
}
