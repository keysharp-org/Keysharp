namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class ThreadTests : TestRunner
{
	[Test, Category("Threading")]
	public void NoTimersLocal()
	{
		Assert.That(((KeysharpThread)Ks.A_Thread).AllowTimers, Is.True);
		Assert.IsTrue(s.AccessorData.threadConfigDataPrototype.allowTimers);

		_ = Keysharp.Builtins.KeysharpThread.staticCall(null, "NoTimers", true);

		Assert.That(((KeysharpThread)Ks.A_Thread).AllowTimers, Is.False);
		Assert.IsTrue(s.AccessorData.threadConfigDataPrototype.allowTimers);
	}

	[Test, Category("Threading")]
	public void NoTimersPrototype()
	{
		s.AccessorData.threadConfigDataPrototype.allowTimers = false;
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			Assert.That(btv.configData.allowTimers, Is.False);
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void InterruptDuration()
	{
		_ = Keysharp.Builtins.KeysharpThread.staticCall(null, "Interrupt", 42, 1);
		Assert.That(s.uninterruptibleTime, Is.EqualTo(42));

		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			Assert.That(btv.UninterruptibleDuration, Is.EqualTo(42));
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void CriticalDefault()
	{
		s.AccessorData.threadConfigDataPrototype.defaultIsCritical = true;
		s.AccessorData.threadConfigDataPrototype.peekFrequency = ThreadVariables.DefaultUninterruptiblePeekFrequency;
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			Assert.IsTrue(btv.isCritical);
			Assert.That(btv.allowThreadToBeInterrupted, Is.False);
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void PriorityDrop()
	{
		var context = UseQueuedMainContext();
		var calls = 0;
		s.Threads.CurrentThread.priority = 1;

		_ = s.EventScheduler.EnqueueThreadLaunch(0, false, false, () => calls++, false);
		context.DrainAll();

		Assert.That(calls, Is.Zero);

		s.Threads.CurrentThread.priority = 0;
		s.EventScheduler.SchedulePump();
		context.DrainAll();

		Assert.That(calls, Is.Zero);
	}

	[Test, Category("Threading")]
	public void CriticalDialog()
	{
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			_ = Keysharp.Builtins.Flow.Critical();
			Assert.That(s.Threads.IsInterruptible(), Is.False);

			using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
				Assert.IsTrue(s.Threads.IsInterruptible());

			Assert.That(s.Threads.IsInterruptible(), Is.False);
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void DialogInterruption()
	{
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			_ = Keysharp.Builtins.Flow.Critical();
			s.Threads.allowInterruption = false;

			try
			{
				using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
					Assert.That(s.Threads.IsInterruptible(), Is.False);
			}
			finally
			{
				s.Threads.allowInterruption = true;
			}
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void PeekFrequency()
	{
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			_ = Keysharp.Builtins.Flow.Critical(50);
			Assert.That(Ks.A_PeekFrequency, Is.EqualTo(50L));
			Ks.A_PeekFrequency = 40;
			Assert.That(Ks.A_PeekFrequency, Is.EqualTo(40L));
			Ks.A_PeekFrequency = 50;
			s.RecordMessageCheck();

			Assert.That(s.IsCurrentThreadPreemptiveCheckDue(), Is.False);
			Assert.IsTrue(Keysharp.Runtime.Flow.IsTrueAndRunning(true));
			Assert.That(s.IsCurrentThreadPreemptiveCheckDue(), Is.False);

			s.Threads.CurrentThread.lastPeekTick = unchecked(Environment.TickCount - 60);
			Assert.IsTrue(s.IsCurrentThreadPreemptiveCheckDue());

			Assert.IsTrue(Keysharp.Runtime.Flow.IsTrueAndRunning(true));
			Assert.That(s.IsCurrentThreadPreemptiveCheckDue(), Is.False);
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}

	[Test, Category("Threading")]
	public void CriticalMinusOne()
	{
		Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

		try
		{
			_ = Keysharp.Builtins.Flow.Critical(-1);
			s.Threads.CurrentThread.lastPeekTick = 0;

			Assert.IsTrue(Keysharp.Runtime.Flow.IsTrueAndRunning(true));
			Assert.That(s.IsCurrentThreadPreemptiveCheckDue(), Is.False);
			Assert.That(s.GetPeekFrequency(), Is.EqualTo(-1));
		}
		finally
		{
			s.Threads.EndThread(btv);
		}
	}
}
