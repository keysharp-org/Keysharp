using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Invoke;
using Keysharp.Internals.Window;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class MessageFilterTests : TestRunner
	{
		private MsgMonitor CreateMonitor(Func<object, object, object, object, object> callback, int maxInstances = 1)
		{
			var monitor = new MsgMonitor();
			monitor.ModifyRegistration(new KeysharpFunc(callback), maxInstances, s.EventScheduler);
			return monitor;
		}

		[Test, Category("Threading")]
		public void OnMessageBuffered()
		{
			var context = UseQueuedMainContext();

			var calls = 0;
			const int msgId = 0x8001;
			s.GuiData.onMessageHandlers[msgId] = CreateMonitor((wParam, lParam, msg, hwnd) =>
			{
				calls++;
				return 0L;
			});

			var filter = new MessageFilter(s);
			var msg = CreateMessage(msgId);

			filter.handledMsg = msg;
			var handled = CallBuffered(filter, ref msg);

			Assert.IsFalse(handled);
			Assert.AreEqual(0, calls);

			context.DrainAll();

			Assert.AreEqual(1, calls);
		}

		[Test, Category("Threading")]
		public void OnMessageEmergency()
		{
			_ = UseQueuedMainContext();

			var calls = 0;
			const int msgId = 0x8002;
			s.GuiData.onMessageHandlers[msgId] = CreateMonitor((wParam, lParam, msg, hwnd) =>
			{
				calls++;
				return 42L;
			});

			var filter = new MessageFilter(s);
			var msg = CreateMessage(msgId);
			var handled = filter.CallEventHandlers(ref msg);

			Assert.IsTrue(handled);
			Assert.AreEqual(1, calls);
			Assert.AreEqual((nint)42, GetResult(msg));
		}

		[Test, Category("Threading")]
		public void EmergencyReserve()
		{
			s.MaxThreadsTotal = 1;
			Assert.IsTrue(s.Threads.TryBeginThread(out var occupied));

			try
			{
				var calls = 0;
				var maxThreadCount = 0;
				const int msgId = 0x8003;
				s.GuiData.onMessageHandlers[msgId] = CreateMonitor((wParam, lParam, msg, hwnd) =>
				{
					calls++;
					maxThreadCount = Math.Max(maxThreadCount, s.totalExistingThreads);
					return 73L;
				});

				var filter = new MessageFilter(s);
				var msg = CreateMessage(msgId);
				var handled = filter.CallEventHandlers(ref msg);

				Assert.IsTrue(handled);
				Assert.AreEqual(1, calls);
				Assert.AreEqual(2, maxThreadCount);
				Assert.AreEqual(1, s.totalExistingThreads);
				Assert.AreEqual((nint)73, GetResult(msg));
			}
			finally
			{
				s.Threads.EndThread(occupied);
			}
		}

		[Test, Category("Threading")]
		public void EmergencyLimit()
		{
			s.MaxThreadsTotal = 1;
			Assert.IsTrue(s.Threads.TryBeginThread(out var occupied));

			try
			{
				var calls = 0;
				var maxThreadCount = 0;
				const int msgId = 0x8004;
				var filter = new MessageFilter(s);
				s.GuiData.onMessageHandlers[msgId] = CreateMonitor((wParam, lParam, msg, hwnd) =>
				{
					calls++;
					maxThreadCount = Math.Max(maxThreadCount, s.totalExistingThreads);

					if (calls < Script.maxEmergencyThreads + 2)
					{
						var nested = CreateMessage(msgId);
						_ = filter.CallEventHandlers(ref nested);
					}

					return 1L;
				}, Script.maxEmergencyThreads + 2);

				var msg = CreateMessage(msgId);
				var handled = filter.CallEventHandlers(ref msg);

				Assert.IsTrue(handled);
				Assert.AreEqual(Script.maxEmergencyThreads, calls);
				Assert.AreEqual((int)s.MaxThreadsTotal + Script.maxEmergencyThreads, maxThreadCount);
				Assert.AreEqual(1, s.totalExistingThreads);
				Assert.AreEqual((nint)1, GetResult(msg));
			}
			finally
			{
				s.Threads.EndThread(occupied);
			}
		}

		[Test, Category("Threading")]
		public void OnMessageLocalBlock()
		{
			var context = UseQueuedMainContext();
			// A parked launch is not a persistence root, and OnMessage deliberately is not one either, so draining
			// the first handler's completion would otherwise exit this script before the second is unblocked.
			s.FlowData.persistentValueSetByUser = true;

			var order = new List<string>();
			const int msgId = 0x8005;
			var monitor = new MsgMonitor();
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) =>
			{
				order.Add("A");
				return 0L;
			})), 1, s.EventScheduler);
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) =>
			{
				order.Add("B");
				return 0L;
			})), 1, s.EventScheduler);
			s.GuiData.onMessageHandlers[msgId] = monitor;

			var registrations = monitor.GetRegistrationsSnapshot();
			registrations[0].InstanceCount = registrations[0].MaxInstances;

			var filter = new MessageFilter(s);
			var msg = CreateMessage(msgId);
			filter.handledMsg = msg;

			Assert.IsFalse(CallBuffered(filter, ref msg));

			context.DrainAll();
			Assert.That(order, Is.EqualTo(new[] { "B" }));

			registrations[0].InstanceCount = 0;
			s.EventScheduler.SchedulePump();
			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "B", "A" }));
		}

		[Test, Category("Threading")]
		public void RegistrationLimit()
		{
			_ = UseQueuedMainContext();

			var order = new List<string>();
			const int msgId = 0x8006;
			var monitor = new MsgMonitor();
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) =>
			{
				order.Add("A");
				return 0L;
			})), 1, s.EventScheduler);
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) =>
			{
				order.Add("B");
				return 7L;
			})), 2, s.EventScheduler);
			s.GuiData.onMessageHandlers[msgId] = monitor;

			var registrations = monitor.GetRegistrationsSnapshot();
			registrations[0].InstanceCount = registrations[0].MaxInstances;

			var filter = new MessageFilter(s);
			var msg = CreateMessage(msgId);
			var handled = filter.CallEventHandlers(ref msg);

			Assert.IsTrue(handled);
			Assert.That(order, Is.EqualTo(new[] { "B" }));
			Assert.AreEqual((nint)7, GetResult(msg));
		}

#if WINDOWS
		private static Message CreateMessage(int msgId) => Message.Create(IntPtr.Zero, msgId, IntPtr.Zero, IntPtr.Zero);

		private static bool CallBuffered(MessageFilter filter, ref Message message) => filter.CallEventHandlers(ref message, true);

		private static nint GetResult(Message message) => message.Result;
#else
		private static Message CreateMessage(int msgId) => new()
		{
			HWnd = 0,
			Msg = msgId,
			WParam = 0,
			LParam = 0,
			Result = 0
		};

		private static bool CallBuffered(MessageFilter filter, ref Message message) => filter.CallEventHandlers(ref message, true);

		private static nint GetResult(Message message) => message.Result;
#endif
	}
}
