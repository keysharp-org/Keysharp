namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class MessageFilterTests : TestRunner
{
	private MsgMonitor CreateMonitor(Func<object, object, object, object, object> callback, int maxInstances = 1)
	{
		var monitor = new MsgMonitor();
		monitor.ModifyRegistration(new KeysharpFunc(callback), maxInstances, s.EventScheduler);
		return monitor;
	}

#if WINDOWS
	// An invisible WinForms icon exercises notification routing without publishing a shell icon.
	[TestCase(1L, WindowsAPI.WM_LBUTTONDOWN, false), TestCase(1L, WindowsAPI.WM_LBUTTONDOWN, true)]
	[TestCase(1L, WindowsAPI.WM_LBUTTONDBLCLK, false), TestCase(1L, WindowsAPI.WM_LBUTTONDBLCLK, true)]
	[TestCase(2L, WindowsAPI.WM_LBUTTONDBLCLK, false), TestCase(2L, WindowsAPI.WM_LBUTTONDBLCLK, true)]
	[Category("Threading"), Apartment(ApartmentState.STA)]
	public void TrayNotifications(long clickCount, int notification, bool winFormsCallback)
	{
		var context = UseQueuedMainContext();
		var hwnd = s.MainWindowHandle;
		var calls = new List<object[]>();
		var claimed = true;
		var selected = 0;
		var menu = new Keysharp.Builtins.Menu();
		_ = menu.Add("Choose", new KeysharpFunc((Func<object, object, object, object>)((_, _, _) =>
		{
			selected++;
			return "";
		})));
		menu.Default = "Choose";
		menu.ClickCount = clickCount;
		var icon = new NotifyIcon { Tag = menu, ContextMenuStrip = menu.MenuItem };
		s.Tray = icon;
		s.trayMessageWindow = new TrayMessageWindow(s, icon);
		icon.MouseDown += s.TrayIcon_MouseDown;
		const int messageId = (int)UserMessages.AHK_NOTIFYICON;
		var nativeHandle = winFormsCallback ? s.trayMessageWindow.Handle : hwnd;
		var nativeMessage = winFormsCallback ? 0x800U : (uint)messageId;
		_ = Keysharp.Builtins.Flow.OnMessage(messageId, new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, handle) =>
		{
			calls.Add([wParam, lParam, msg, handle]);
			return claimed ? 0L : "";
		})));

		try
		{
			Assert.That(s.mainWindow.Visible, Is.False);
			Assert.That(icon.Visible, Is.False);
			_ = WindowsAPI.SendMessage(nativeHandle, nativeMessage, messageId, notification);
			context.DrainAll();
			CheckNotification(1, notification);
			Assert.That(selected, Is.EqualTo(0), "a zero return must suppress the default tray callback");

			claimed = false;
			_ = WindowsAPI.SendMessage(nativeHandle, nativeMessage, messageId, notification);
			context.DrainAll();
			CheckNotification(2, notification);
			Assert.That(selected, Is.EqualTo(1), "a blank return must allow the default callback once");

			claimed = true;
			Assert.IsTrue(WindowsAPI.PostMessage(nativeHandle, nativeMessage, messageId, notification));
			Application.DoEvents();
			context.DrainAll();
			CheckNotification(3, notification);
			Assert.That(selected, Is.EqualTo(1), "a posted notification must also respect suppression");

			claimed = false;
			Assert.IsTrue(WindowsAPI.PostMessage(nativeHandle, nativeMessage, messageId, notification));
			Application.DoEvents();
			context.DrainAll();
			CheckNotification(4, notification);
			Assert.That(selected, Is.EqualTo(2), "a posted notification must invoke the default callback once");

			claimed = true;
			_ = WindowsAPI.SendMessage(nativeHandle, nativeMessage, messageId, WindowsAPI.WM_RBUTTONUP);
			context.DrainAll();
			CheckNotification(5, WindowsAPI.WM_RBUTTONUP);
			Assert.That(menu.MenuItem.Visible, Is.False, "a claimed right-click must not open the tray menu");

			claimed = false;
			var count = 5;
			foreach (var inert in new[] { 0x0200, 0x0202, 0x0402, 0x0403, 0x0404, 0x0405,
				clickCount == 1 ? WindowsAPI.WM_LBUTTONUP : WindowsAPI.WM_LBUTTONDOWN })
			{
				_ = WindowsAPI.SendMessage(nativeHandle, nativeMessage, messageId, inert);
				context.DrainAll();
				CheckNotification(++count, inert);
				Assert.That(selected, Is.EqualTo(2), "other mouse and balloon notifications must not choose the default item");
			}

			Assert.That(s.mainWindow.Visible, Is.False);
		}
		finally
		{
			s.trayMessageWindow.ReleaseHandle();
			s.trayMessageWindow = null;
			icon.Dispose();
			s.Tray = null;
			menu.MenuItem.Dispose();
		}

		void CheckNotification(int count, int expectedNotification)
		{
			Assert.That(calls.Count, Is.EqualTo(count));
			Assert.That(calls[^1], Is.EqualTo(new object[] { (long)messageId, (long)expectedNotification, (long)messageId, hwnd.ToInt64() }));
		}
	}

#else
	[TestCase(1L), TestCase(2L)]
	[Category("Threading")]
	public void TrayActivations(long clickCount)
	{
		SkipIfUiInitializationBlocked("Tray activation needs a usable UI toolkit.");
		s.mainWindow = new Keysharp.Internals.UI.Unix.MainWindow(s);
		s.mainWindow.InitializeHidden();
		var hwnd = s.MainWindowHandle;
		var context = UseQueuedMainContext();
		var calls = new List<object[]>();
		var claimed = true;
		var selected = 0;
		var menu = new Keysharp.Builtins.Menu();
		_ = menu.Add("Choose", new KeysharpFunc((Func<object, object, object, object>)((_, _, _) =>
		{
			selected++;
			return "";
		})));
		menu.Default = "Choose";
		menu.ClickCount = clickCount;
		var icon = new NotifyIcon { Tag = menu, ContextMenuStrip = menu.MenuItem };
		s.Tray = icon;
		icon.MouseClick += s.TrayIcon_MouseClick;
		icon.MouseDoubleClick += s.TrayIcon_MouseDoubleClick;
		const int messageId = (int)UserMessages.AHK_NOTIFYICON;
		_ = Keysharp.Builtins.Flow.OnMessage(messageId, new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, handle) =>
		{
			calls.Add([wParam, lParam, msg, handle]);
			return claimed ? 0L : "";
		})));
		// Eto's backend callback needs no shell icon; the wrapper does not expose its indicator.
		var indicator = (Eto.Forms.TrayIndicator)typeof(NotifyIcon).GetField("indicator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(icon);
		var activationTime = typeof(NotifyIcon).GetField("lastActivationTime", BindingFlags.Instance | BindingFlags.NonPublic);
		var callback = (Eto.Forms.TrayIndicator.ICallback)((Eto.ICallbackSource)indicator).Callback;

		try
		{
			Assert.IsFalse(icon.Visible);
			Assert.IsFalse(s.mainWindow.Visible);
			Activate(false);
			Activate(true);
			context.DrainAll();
			CheckNotifications(2);
			Assert.AreEqual(0, selected, "a zero return must suppress both activation callbacks");

			claimed = false;
			Activate(false);
			Activate(true);
			context.DrainAll();
			CheckNotifications(4);
			Assert.AreEqual(clickCount == 1 ? 2 : 1, selected, "each activation must choose the default at most once");
			Assert.IsFalse(icon.Visible);
			Assert.IsFalse(s.mainWindow.Visible);
		}
		finally
		{
			icon.Dispose();
			s.Tray = null;
			menu.MenuItem.Dispose();
		}

		void Activate(bool doubleClick)
		{
			activationTime.SetValue(icon, doubleClick ? Environment.TickCount64 : 0L);
			callback.OnActivated(indicator, EventArgs.Empty);
		}

		void CheckNotifications(int count)
		{
			Assert.AreEqual(count, calls.Count);
			Assert.That(calls[^2], Is.EqualTo(new object[] { (long)messageId, 0x202L, (long)messageId, hwnd.ToInt64() }));
			Assert.That(calls[^1], Is.EqualTo(new object[] { (long)messageId, 0x203L, (long)messageId, hwnd.ToInt64() }));
		}
	}
#endif

#if WINDOWS
	// Only the Windows pre-filter buffers: a monitor off Windows runs inline, so it can claim the message.
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
			Assert.That(CallBuffered(filter, ref msg), Is.False);
			context.DrainAll();
			Assert.That(calls, Is.EqualTo(0));
		}
		finally
		{
			s.Threads.EndThread(critical);
		}

		s.EventScheduler.SchedulePump();
		context.DrainAll();

		Assert.That(calls, Is.EqualTo(1));
	}
#endif

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
		Assert.That(calls, Is.EqualTo(1));
		Assert.That(GetResult(msg), Is.EqualTo((nint)42));
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
			Assert.That(calls, Is.EqualTo(1));
			Assert.That(maxThreadCount, Is.EqualTo(2));
			Assert.That(s.totalExistingThreads, Is.EqualTo(1));
			Assert.That(GetResult(msg), Is.EqualTo((nint)73));
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
			Assert.That(calls, Is.EqualTo(Script.maxEmergencyThreads));
			Assert.That(maxThreadCount, Is.EqualTo((int)s.MaxThreadsTotal + Script.maxEmergencyThreads));
			Assert.That(s.totalExistingThreads, Is.EqualTo(1));
			Assert.That(GetResult(msg), Is.EqualTo((nint)1));
		}
		finally
		{
			s.Threads.EndThread(occupied);
		}
	}

#if WINDOWS
	/// <summary>
	/// A callback at its MaxThreads skips the message, and with every callback there the message is left unmonitored
	/// rather than replayed, as AHK's MsgMonitor does.
	/// </summary>
	[Test, Category("Threading")]
	public void OnMessageInstanceLimit()
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

		Assert.That(CallBuffered(filter, ref msg), Is.False);

		context.DrainAll();
		Assert.That(order, Is.EqualTo(["B"]));

		registrations[1].InstanceCount = registrations[1].MaxInstances;
		Assert.That(CallBuffered(filter, ref msg), Is.False);
		context.DrainAll();
		Assert.That(order, Is.EqualTo(["B"]));

		registrations[0].InstanceCount = 0;
		registrations[1].InstanceCount = 0;
		s.EventScheduler.SchedulePump();
		context.DrainAll();

		Assert.That(order, Is.EqualTo(["B"]));
	}

	/// <summary>
	/// At #MaxThreads an interruptible script leaves the message unmonitored rather than replaying it later.
	/// </summary>
	[Test, Category("Threading")]
	public void OnMessageAtMaxThreads()
	{
		var context = UseQueuedMainContext();

		var calls = 0;
		const int msgId = 0x800D;
		s.GuiData.onMessageHandlers[msgId] = CreateMonitor((wParam, lParam, msg, hwnd) =>
		{
			calls++;
			return 0L;
		});

		var filter = new MessageFilter(s);
		var msg = CreateMessage(msgId);
		s.MaxThreadsTotal = 1;
		// No startup window, so the thread holding the only slot is interruptible.
		s.uninterruptibleTime = 0;
		Assert.IsTrue(s.Threads.TryBeginThread(out var occupied));

		try
		{
			Assert.IsTrue(s.Threads.IsInterruptible());
			Assert.That(CallBuffered(filter, ref msg), Is.False);
			context.DrainAll();
		}
		finally
		{
			s.Threads.EndThread(occupied);
		}

		s.EventScheduler.SchedulePump();
		context.DrainAll();

		Assert.That(calls, Is.EqualTo(0));
	}
#endif

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
		Assert.That(order, Is.EqualTo(["B"]));
		Assert.That(GetResult(msg), Is.EqualTo((nint)7));
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

		Assert.That(filter.CallEventHandlers(ref msg), Is.EqualTo(claims));
		Assert.That(GetResult(msg), Is.EqualTo((nint)reply));
		Assert.That(order, Is.EqualTo(claims ? new[] { "first" } : ["first", "second"]));
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
		Assert.That(monitor.GetRegistrationsSnapshot().Length, Is.EqualTo(1));
		Assert.That(monitor.GetRegistrationsSnapshot()[0].MaxInstances, Is.EqualTo(3));

		_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn);
		Assert.That(monitor.GetRegistrationsSnapshot()[0].MaxInstances, Is.EqualTo(3));
		_ = Keysharp.Builtins.Flow.OnMessage(msgId, fn, 0L);
		Assert.That(s.GuiData.onMessageHandlers.ContainsKey(msgId), Is.False);
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

			Assert.That(order, Is.EqualTo(claims ? new[] { "global" } : ["global", "window"]));
			Assert.That(probe.Count, Is.EqualTo(claims ? 0 : 1));
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
				Assert.That(probe.Count, Is.EqualTo(1), "the window must get the message while no thread can start");
				Assert.That(calls, Is.EqualTo(0));
			}
			finally
			{
				s.Threads.EndThread(critical);
			}

			Keysharp.Internals.Flow.TryDoEvents(s.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
			Assert.That(calls, Is.EqualTo(1), "the callback must run once a thread can start");
			Assert.That(probe.Count, Is.EqualTo(1));
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
			Assert.That(calls, Is.EqualTo(1));
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
		Assert.That(filter.PreFilterMessage(ref msg), Is.False);
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

	private static nint GetResult(Message message) => message.Result;
#endif
}
