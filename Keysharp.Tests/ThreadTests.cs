using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Threading;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class ThreadTests : TestRunner
	{
		[Test, Category("Threading")]
		public void NoTimersLocal()
		{
			Assert.AreEqual(true, Ks.A_AllowTimers);
			Assert.IsTrue(s.AccessorData.threadConfigDataPrototype.allowTimers);

			_ = Keysharp.Builtins.KeysharpThread.staticCall(null, "NoTimers", true);

			Assert.AreEqual(false, Ks.A_AllowTimers);
			Assert.IsTrue(s.AccessorData.threadConfigDataPrototype.allowTimers);
		}

		[Test, Category("Threading")]
		public void NoTimersPrototype()
		{
			s.AccessorData.threadConfigDataPrototype.allowTimers = false;
			Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

			try
			{
				Assert.IsFalse(btv.configData.allowTimers);
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
			Assert.AreEqual(42, s.uninterruptibleTime);

			Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

			try
			{
				Assert.AreEqual(42, btv.UninterruptibleDuration);
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
				Assert.IsFalse(btv.allowThreadToBeInterrupted);
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

			s.EventScheduler.EnqueueThreadLaunch(0, false, false, () => calls++, false);
			context.DrainAll();

			Assert.AreEqual(0, calls);

			s.Threads.CurrentThread.priority = 0;
			s.EventScheduler.SchedulePump();
			context.DrainAll();

			Assert.AreEqual(0, calls);
		}

		[Test, Category("Threading")]
		public void CriticalDialog()
		{
			Assert.IsTrue(s.Threads.TryBeginThread(out var btv));

			try
			{
				_ = Keysharp.Builtins.Flow.Critical();
				Assert.IsFalse(s.Threads.IsInterruptible());

				using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
					Assert.IsTrue(s.Threads.IsInterruptible());

				Assert.IsFalse(s.Threads.IsInterruptible());
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
				s.FlowData.allowInterruption = false;

				try
				{
					using (Keysharp.Internals.Flow.BeginDialogInterruptibilityScope())
						Assert.IsFalse(s.Threads.IsInterruptible());
				}
				finally
				{
					s.FlowData.allowInterruption = true;
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
				Assert.AreEqual(50L, Ks.A_PeekFrequency);
				Ks.A_PeekFrequency = 40;
				Assert.AreEqual(40L, Ks.A_PeekFrequency);
				Ks.A_PeekFrequency = 50;
				s.RecordMessageCheck();

				Assert.IsFalse(s.IsCurrentThreadPreemptiveCheckDue());
				Assert.IsTrue(Keysharp.Runtime.Flow.IsTrueAndRunning(true));
				Assert.IsFalse(s.IsCurrentThreadPreemptiveCheckDue());

				s.Threads.CurrentThread.lastPeekTick = unchecked(Environment.TickCount - 60);
				Assert.IsTrue(s.IsCurrentThreadPreemptiveCheckDue());

				Assert.IsTrue(Keysharp.Runtime.Flow.IsTrueAndRunning(true));
				Assert.IsFalse(s.IsCurrentThreadPreemptiveCheckDue());
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
				Assert.IsFalse(s.IsCurrentThreadPreemptiveCheckDue());
				Assert.AreEqual(-1, s.GetPeekFrequency());
			}
			finally
			{
				s.Threads.EndThread(btv);
			}
		}
	}
}
