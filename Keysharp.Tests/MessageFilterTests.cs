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
			// While no thread can start, the message is let through and its callbacks wait.
			Assert.IsTrue(s.Threads.TryBeginThread(out var critical));

			try
			{
				_ = Keysharp.Builtins.Flow.Critical();
				Assert.IsFalse(CallBuffered(filter, ref msg));
				context.DrainAll();
				Assert.AreEqual(0, calls);
			}
			finally
			{
				s.Threads.EndThread(critical);
			}

			s.EventScheduler.SchedulePump();
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
				return "";
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

			registrations[1].InstanceCount = registrations[1].MaxInstances;
			Assert.IsFalse(CallBuffered(filter, ref msg));
			context.DrainAll();
			Assert.That(order, Is.EqualTo(new[] { "B" }));

			registrations[0].InstanceCount = 0;
			registrations[1].InstanceCount = 0;
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

		[TestCase("zero", true, 0L)]
		[TestCase("integer", true, 7L)]
		[TestCase("string", true, 0L)]
		[TestCase("blank", false, 0L)]
		[TestCase("none", false, 0L)]
		[TestCase("error", false, 0L)]
		[TestCase("exit", false, 0L)]
		[Category("Threading")]
		public void OnMessageReturnControlsDispatch(string kind, bool claims, long reply)
		{
			_ = UseQueuedMainContext();
			const int msgId = 0x8008;
			var order = new List<string>();
			var monitor = new MsgMonitor();
			var filter = new MessageFilter(s);
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
			{
				order.Add("first");
				return kind switch
				{
					"zero" => 0L,
					"integer" => 7L,
					"string" => "abc",
					"blank" => "",
					"error" => throw new Keysharp.Builtins.Error("monitor failed"),
					"exit" => Keysharp.Builtins.Flow.Exit(),
					_ => null
				};
			})), 1, s.EventScheduler);
			monitor.ModifyRegistration(new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
			{
				order.Add("second");
				return "";
			})), 1, s.EventScheduler);
			s.GuiData.onMessageHandlers[msgId] = monitor;
			var msg = CreateMessage(msgId);

			Assert.AreEqual(claims, filter.CallEventHandlers(ref msg));
			Assert.AreEqual((nint)reply, GetResult(msg));
			Assert.That(order, Is.EqualTo(claims ? new[] { "first" } : new[] { "first", "second" }));
		}

		[Test, Category("Threading")]
		public void OnMessageUpdatesMaxThreadsInPlace()
		{
			_ = UseQueuedMainContext();
			const int msgId = 0x8007;
			var fn = new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) => 0L));

			_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn, 5L);
			_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn, 3L);
			var monitor = s.GuiData.onMessageHandlers[msgId];
			Assert.AreEqual(1, monitor.GetRegistrationsSnapshot().Length);
			Assert.AreEqual(3, monitor.GetRegistrationsSnapshot()[0].MaxInstances);

			_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn);
			Assert.AreEqual(3, monitor.GetRegistrationsSnapshot()[0].MaxInstances);
			_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn, 0L);
			Assert.IsFalse(s.GuiData.onMessageHandlers.ContainsKey(msgId));
			Assert.DoesNotThrow(() => Keysharp.Builtins.Flow.OnMessage(msgId, new KeysharpObject(), 0L));
		}

#if WINDOWS
		/// <summary>
		/// While the script is interruptible, a posted message above 0x0311 runs its callbacks before it is dispatched,
		/// as in AHK, so a claim keeps it from the window and from the Gui's own OnMessage.
		/// </summary>
		[TestCase(true), TestCase(false)]
		[Category("Threading"), Category("Gui"), Apartment(ApartmentState.STA)]
		public void PostedMessageClaimSkipsDispatch(bool claims)
		{
			const int msgId = 0x8009;
			var order = new List<string>();
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();
			var probe = new DispatchProbe(gui.form.Handle, msgId);

			try
			{
				_ = Keysharp.Builtins.Flow.OnMessage(msgId, new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
				{
					order.Add("global");
					return claims ? 0L : "";
				})));
				_ = gui.OnMessage(msgId, new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
				{
					order.Add("window");
					return "";
				})));

				Assert.IsTrue(WindowsAPI.PostMessage(gui.form.Handle, msgId, 0, 0));
				Application.DoEvents();

				Assert.That(order, Is.EqualTo(claims ? new[] { "global" } : new[] { "global", "window" }));
				Assert.AreEqual(claims ? 0 : 1, probe.Count);
			}
			finally
			{
				probe.ReleaseHandle();
				_ = gui.Destroy();
			}
		}

		/// <summary>
		/// While the script is uninterruptible, a posted message above 0x0311 reaches its window first and its callbacks
		/// run once a thread can start.
		/// </summary>
		[Test, Category("Threading"), Category("Gui"), Apartment(ApartmentState.STA)]
		public void PostedMessageWhileUninterruptible()
		{
			const int msgId = 0x800A;
			var calls = 0;
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();
			var probe = new DispatchProbe(gui.form.Handle, msgId);

			try
			{
				_ = Keysharp.Builtins.Flow.OnMessage(msgId, new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
				{
					calls++;
					return 0L;
				})));
				Assert.IsTrue(s.Threads.TryBeginThread(out var critical));

				try
				{
					_ = Keysharp.Builtins.Flow.Critical();
					Assert.IsTrue(WindowsAPI.PostMessage(gui.form.Handle, msgId, 0, 0));
					Application.DoEvents();
					Assert.AreEqual(1, probe.Count, "the window must get the message while no thread can start");
					Assert.AreEqual(0, calls);
				}
				finally
				{
					s.Threads.EndThread(critical);
				}

				Keysharp.Internals.Flow.TryDoEvents(s.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
				Assert.AreEqual(1, calls, "the callback must run once a thread can start");
				Assert.AreEqual(1, probe.Count);
			}
			finally
			{
				probe.ReleaseHandle();
				_ = gui.Destroy();
			}
		}

		/// <summary>
		/// A callback which pumps messages, as Sleep does, is called once for its message, not again when the message
		/// is dispatched.
		/// </summary>
		[Test, Category("Threading"), Category("Gui"), Apartment(ApartmentState.STA)]
		public void PumpingCallbackRunsOnce()
		{
			const int msgId = 0x800C;
			var calls = 0;
			var gui = new Gui(System.Array.Empty<object>());
			_ = gui.__New();
			var handle = gui.form.Handle;

			try
			{
				_ = Keysharp.Builtins.Flow.OnMessage(msgId, new KeysharpFunc((Func<object, object, object, object, object>)((_, _, _, _) =>
				{
					if (++calls == 1)
					{
						_ = WindowsAPI.PostMessage(handle, msgId + 1, 0, 0);
						Application.DoEvents();
					}

					return "";
				})));

				Assert.IsTrue(WindowsAPI.PostMessage(handle, msgId, 0, 0));
				Application.DoEvents();
				Assert.AreEqual(1, calls);
			}
			finally
			{
				_ = gui.Destroy();
			}
		}

		[TestCase(0x0201), TestCase(0x800B)]
		[Category("Threading")]
		public void ClaimedMessageLeavesNoStash(int msgId)
		{
			_ = UseQueuedMainContext();
			var claims = true;
			s.GuiData.onMessageHandlers[msgId] = CreateMonitor((_, _, _, _) => claims ? 0L : "");
			var filter = new MessageFilter(s);
			var msg = CreateMessage(msgId);

			// A claimed message is never dispatched, so WndProc would never clear a stash of it.
			Assert.IsTrue(filter.PreFilterMessage(ref msg));
			Assert.IsNull(filter.handledMsg);

			claims = false;
			Assert.IsFalse(filter.PreFilterMessage(ref msg));
			Assert.IsTrue(filter.handledMsg == msg);
		}

		/// <summary>Counts one message reaching a window's procedure, whatever the script's thread state.</summary>
		private sealed class DispatchProbe : NativeWindow
		{
			private readonly int msg;
			internal int Count;

			internal DispatchProbe(nint handle, int msg)
			{
				this.msg = msg;
				AssignHandle(handle);
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == msg)
					Count++;

				base.WndProc(ref m);
			}
		}

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
