using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Invoke;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class InputSchedulerTests : TestRunner
	{
		private static HotkeyDefinition CreateHotkey(string name = "F1")
			=> new(Script.TheScript, 1, new KeysharpFunc((Func<object, object>)(_ => 0L)), 0, name, 0);

		private static HotkeyVariant CreateHotkeyVariant(Action action, int maxThreads = 1, int existingThreads = 0, bool maxThreadsBuffer = false)
		{
			var variant = new HotkeyVariant
			{
				maxThreads = maxThreads,
				maxThreadsBuffer = maxThreadsBuffer,
				priority = 0
			};
			var binding = variant.SetBinding(Script.TheScript.EventScheduler, new KeysharpFunc((Func<object, object>)(_ =>
			{
				action();
				return 0L;
			})), null);
			binding.ExistingThreads = existingThreads;
			return variant;
		}

		[Test, Category("Threading")]
		public void HotkeyGlobalCapacity()
		{
			var context = UseQueuedMainContext();
			s.MaxThreadsTotal = 1;
			Assert.IsTrue(s.Threads.TryBeginThread(out var occupied));

			try
			{
				var calls = 0;
				var hk = CreateHotkey();
				var variant = CreateHotkeyVariant(() => calls++);

				hk.PerformInNewThreadMadeByCallerAsync(variant, 0, 0);
				context.DrainAll();

				Assert.AreEqual(0, calls);

				s.Threads.EndThread(occupied);
				context.DrainAll();

				Assert.AreEqual(1, calls);
			}
			catch
			{
				s.Threads.EndThread(occupied);
				throw;
			}
		}

		[Test, Category("Threading")]
		public void HotstringLocalBlock()
		{
			var context = UseQueuedMainContext();

			var hotstringCalls = 0;
			var normalCalls = 0;
			var hs = new HotstringDefinition(s, "::abc", "")
			{
				Name = "abc"
			};
			hs.funcObj = new KeysharpFunc((Func<object, object>)(name =>
			{
				hotstringCalls++;
				return 0L;
			}));
			hs.maxThreads = 1;
			hs.existingThreads = 1;
			hs.priority = 0;

			_ = hs.PerformInNewThreadMadeByCaller(0, CaseConformModes.None, ' ', 0, false);
			s.EventScheduler.EnqueueCallback(() => normalCalls++, ScriptEventQueue.Normal, false);

			context.DrainAll();

			Assert.AreEqual(0, hotstringCalls);
			Assert.AreEqual(1, normalCalls);
		}

		[Test, Category("Threading")]
		public void HotkeyBufferedRetry()
		{
			var context = UseQueuedMainContext();

			var hotkeyCalls = 0;
			var normalCalls = 0;
			var hk = CreateHotkey();
			var variant = CreateHotkeyVariant(() => hotkeyCalls++, existingThreads: 1, maxThreadsBuffer: true);

			hk.PerformInNewThreadMadeByCallerAsync(variant, 0, 0);
			s.EventScheduler.EnqueueCallback(() => normalCalls++, ScriptEventQueue.Normal, false);

			context.DrainAll();

			Assert.AreEqual(0, hotkeyCalls);
			Assert.AreEqual(1, normalCalls);

			variant.FindBinding(Script.TheScript.EventScheduler).ExistingThreads = 0;
			s.EventScheduler.SchedulePump();
			context.DrainAll();

			Assert.AreEqual(1, hotkeyCalls);
		}

		[Test, Category("Threading")]
		public void HotkeyUnbufferedDrop()
		{
			var context = UseQueuedMainContext();

			var hotkeyCalls = 0;
			var hk = CreateHotkey();
			var variant = CreateHotkeyVariant(() => hotkeyCalls++, existingThreads: 1, maxThreadsBuffer: false);

			hk.PerformInNewThreadMadeByCallerAsync(variant, 0, 0);
			hk.PerformInNewThreadMadeByCallerAsync(variant, 0, 0);

			context.DrainAll();
			Assert.AreEqual(0, hotkeyCalls);

			variant.FindBinding(Script.TheScript.EventScheduler).ExistingThreads = 0;
			s.EventScheduler.SchedulePump();
			context.DrainAll();

			Assert.AreEqual(0, hotkeyCalls, "Unbuffered hotkey events should be dropped before entering the scheduler queue.");
		}

		[Test, Category("Threading")]
		public void InteractiveInputQueue()
		{
			var context = UseQueuedMainContext();
			var order = new List<string>();

			var hk = CreateHotkey();
			var variant = CreateHotkeyVariant(() => order.Add("hotkey"));
			var hs = new HotstringDefinition(s, "::abc", "")
			{
				Name = "abc",
				funcObj = new KeysharpFunc((Func<object, object>)(_ =>
				{
					order.Add("hotstring");
					return 0L;
				})),
				maxThreads = 1,
				priority = 0
			};

			s.EventScheduler.EnqueueCallback(() => order.Add("normal-1"), ScriptEventQueue.Normal, false);
			hk.PerformInNewThreadMadeByCallerAsync(variant, 0, 0);
			_ = hs.PerformInNewThreadMadeByCaller(0, CaseConformModes.None, ' ', 0, false);
			s.EventScheduler.EnqueueCallback(() => order.Add("normal-2"), ScriptEventQueue.Normal, false);

			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "hotkey", "hotstring", "normal-1", "normal-2" }));
		}
	}
}
