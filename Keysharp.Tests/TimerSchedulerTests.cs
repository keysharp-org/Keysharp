namespace Keysharp.Tests;

[TestFixture, NonParallelizable]
public class TimerSchedulerTests : TestRunner
{
	[Test, Category("Threading")]
	public void BlockedAdmission() => Assert.IsTrue(TestScript("timer-blocked-admission", false));

	[Test, Category("Threading")]
	public void BlockedTimer()
	{
		var output = RunScript(string.Concat(path, "timer-blocked-fires-once.ahk"), "timer-blocked-fires-once", true, false);
		Assert.That(output.Trim(), Is.EqualTo("pass"), output);
	}

	[Test, Category("Threading")]
	public void CallbackSleep() => Assert.IsTrue(TestScript("timer-during-callback-sleep", false));

	[TestCase(false), TestCase(true), Category("Threading"), Category("Internal"), Category("Curated")]
	public void FailedLaunchPreservesTimer(bool runOnce)
	{
		_ = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var timers = s.FlowData.timers;
		var calls = 0;
		var timer = timers.Upsert(new KeysharpFunc((Func<object>)(() =>
		{
			calls++;
			return 0L;
		})), scheduler, runOnce ? -60_000L : 60_000L, 0L);
		var info = timer.Info;
		var maxThreads = s.MaxThreadsTotal;

		try
		{
			timer.NextDueTick = 0;
			s.MaxThreadsTotal = 0;
			scheduler.PumpThreadQueuedEventsCore();
			Assert.That(calls, Is.Zero);
			Assert.That(timer.Queued, Is.True);
			Assert.That(timer.RunningCount, Is.Zero);
			Assert.That(timer.NextDueTick, Is.Zero);
			Assert.That(info.IsActive, Is.True);
			Assert.That(info.EndReason, Is.Empty);
			Assert.That(info.LastStartTick, Is.EqualTo(-1));

			s.MaxThreadsTotal = maxThreads;
			scheduler.PumpThreadQueuedEventsCore();
			Assert.That(calls, Is.EqualTo(1));
			Assert.That(timer.Queued, Is.False);
			Assert.That(timer.RunningCount, Is.Zero);
			Assert.That(info.LastStartTick, Is.GreaterThanOrEqualTo(0));
			Assert.That(info.EndReason, Is.EqualTo(runOnce ? "Completed" : ""));
			scheduler.PumpThreadQueuedEventsCore();
			Assert.That(calls, Is.EqualTo(1));
		}
		finally
		{
			s.MaxThreadsTotal = maxThreads;
			timers.DisableOrDelete(timer);
		}
	}

	[Test, Category("Threading")]
	public void LongTimerCoalescing() => Assert.IsTrue(TestScript("timer-long-callback-coalesces", false));

	[TestCase(false), TestCase(true), Category("Threading"), Category("Internal"), Category("Curated")]
	public void MenuVisible(bool runOnce)
	{
		_ = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var timers = s.FlowData.timers;
		var calls = 0;
		var callbackThreadId = 0;
		var timer = timers.Upsert(new KeysharpFunc((Func<object>)(() =>
		{
			calls++;
			callbackThreadId = Environment.CurrentManagedThreadId;
			return 0L;
		})), scheduler, runOnce ? -60_000L : 60_000L, 0L);
		s.SetMenuVisible(1, true);

		try
		{
			timer.NextDueTick = 0;
			scheduler.PumpThreadQueuedEventsCore();
			Assert.That(calls, Is.EqualTo(1));
			Assert.That(callbackThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));

			if (!runOnce)
				timer.NextDueTick = 0;

			scheduler.PumpThreadQueuedEventsCore();
			Assert.That(calls, Is.EqualTo(runOnce ? 1 : 2));
			Assert.That(s.IsMenuVisible, Is.True);
		}
		finally
		{
			s.SetMenuVisible(1, false);

			if (timer.IsActive)
				timers.DisableOrDelete(timer);
		}
	}

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

	[Test, Category("Threading"), Category("Internal"), Category("Curated")]
	public void RetiredTimerSlot()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var timers = s.FlowData.timers;
		var calls = 0;
		var timer = timers.Upsert(new KeysharpFunc((Func<object>)(() =>
		{
			calls++;
			return 0L;
		})), scheduler, 60_000L, 2L);
		var info = timer.Info;
		timer.NextDueTick = 0;
		scheduler.EnqueueDueTimers();
		Assert.That(timer.Queued, Is.True);

		// Keep the queued entry to model a dispatch already captured when owner cleanup removes its timer.
		Assert.That(timers.RemoveOwned(scheduler), Is.True);
		timers.ResetTimer(timer);
		timers.UpdatePriority(timer, 99L);
		timers.DisableOrDelete(timer);
		Assert.That(timers.MarkCallbackStarted(timer), Is.Null);
		timers.MarkCallbackFinished(timer);
		timers.ReleaseQueuedTimer(timer);
		Assert.That(scheduler.EnqueueTimer(timer), Is.False);
		scheduler.PumpThreadQueuedEventsCore();
		context.DrainAll();

		Assert.That(calls, Is.Zero);
		Assert.That(timers.Count, Is.Zero);
		Assert.That(timers.AnyEnabled, Is.False);
		Assert.That(timer.IsActive, Is.False);
		Assert.That(info.IsActive, Is.False);
		Assert.That(info.Priority, Is.EqualTo(2L));
		Assert.That(info.EndReason, Is.EqualTo("Stopped"));
	}

	[Test, Category("Threading")]
	public void ShortTimerPeriod() => Assert.IsTrue(TestScript("timer-short-callback-keeps-period", false));

	[Test, Category("Threading"), Category("Curated")]
	public void TimerHandles() => Assert.IsTrue(TestScript("timer-handles", false));

	[Test, Category("Threading"), Category("Curated")]
	public void TimerEventInfo() => Assert.IsTrue(TestScript("timer-eventinfo", false));

	[Test, Category("Threading"), Category("Curated")]
	public void TimerEventInfoExit() => Assert.IsTrue(TestScript("timer-eventinfo-exit", false));
}
