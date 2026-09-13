using Keysharp.Internals.Audio;
using Keysharp.Internals.Events;
using Keysharp.Internals.Window;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The surface every factory-created event hook shares — InProgress, EndReason, Start and Stop — run
	/// through one assertion body for Ks.WinEvent, Ks.MonitorHook, Ks.ClipboardHook and Ks.Audio.DeviceHook, so
	/// they cannot drift apart. Members are reached by name, so a rename or a dropped member fails here rather
	/// than silently in a script.
	/// <para>Most registrations are built directly rather than through the public factories: that installs no
	/// native backend, so the fixture is headless and hooks nothing real.</para>
	/// </summary>
	[TestFixture, Category("Internal"), Category("Curated")]
	public class EventHookTests : TestRunner
	{
		/// <summary>Every factory-created hook class, so each test covers all of them by construction.</summary>
		private static (string Name, Func<object> Create)[] Factories =>
		[
			("Ks.WinEvent", CreateWinEvent),
			("Ks.MonitorHook", CreateMonitorHook),
			("Ks.ClipboardHook", CreateClipboardHook),
			("Ks.Audio.DeviceHook", CreateDeviceHook),
		];

		private static KeysharpFunc Callback() => new((Func<object, object, object>)((hook, arg) => ""));

		private static KeysharpFunc SlotCallback() => new((Func<object, object, object, object>)((hook, hwnd, time) => ""));

		// A one-slot run built directly, so no native window hook is installed until a test registers it.
		private static WinEventRegistration WinEventRun(WindowEventType slot)
		{
			var script = Script.TheScript;
			var callbacks = new object[WinEventManager.typeCount];
			callbacks[(int)slot] = SlotCallback();
			return new WinEventRegistration(null, WinEventRegistration.CaptureSearchOptions(script), callbacks,
				script.EventScheduler, script.WinEventManager);
		}

		// The idle object is given the run's slot first, so a restart rebuilds the same description from it.
		private static object CreateWinEvent()
		{
			var reg = WinEventRun(WindowEventType.Active);
			var hook = new Ks.WinEvent { OnActive = reg.callbacks[(int)WindowEventType.Active] };
			hook.sub = reg;
			reg.scriptObject = hook;
			return hook;
		}

		// What a restart must carry over: the callback and, for a device hook, its kind.
		private static object Description(object hook) => ((Ks.EventHook)hook).sub switch
		{
			WinEventRegistration run => run.callbacks[(int)WindowEventType.Active],
			AudioEventRegistration run => (run.Callback, run.kind),
			var run => run.Callback
		};

		private static object CreateMonitorHook()
		{
			var script = Script.TheScript;
			var callback = Callback();
			var reg = new MonitorEventRegistration(callback, script.EventScheduler, script.MonitorEventManager);
			var hook = new Ks.MonitorHook(callback) { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateClipboardHook()
		{
			var script = Script.TheScript;
			var callback = Callback();
			var reg = new ClipboardEventRegistration(callback, script.EventScheduler, script.ClipboardEventManager);
			var hook = new Ks.ClipboardHook(callback) { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateDeviceHook()
		{
			var script = Script.TheScript;
			var callback = Callback();
			var reg = new AudioEventRegistration(callback, script.EventScheduler, script.AudioEventManager, "All");
			var hook = new Ks.Audio.DeviceHook(callback, "All") { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object Get(object hook, string name)
		{
			var prop = hook.GetType().GetProperty(name);
			Assert.IsNotNull(prop, $"{hook.GetType().Name} is missing the {name} member.");
			return prop.GetValue(hook);
		}

		private static void Call(object hook, string name)
		{
			var method = hook.GetType().GetMethod(name, Type.EmptyTypes);
			Assert.IsNotNull(method, $"{hook.GetType().Name} is missing the {name}() member.");
			_ = method.Invoke(hook, null);
		}

		private static void AssertState(string name, object hook, bool inProgress, string endReason, string what)
		{
			Assert.AreEqual(inProgress, Get(hook, "InProgress"), $"{name}: InProgress {what}.");
			Assert.AreEqual(endReason, Get(hook, "EndReason"), $"{name}: EndReason {what}.");
		}

		/// <summary>
		/// The whole state machine of one hook — running, stopped, running again — written once and applied to every
		/// hook class, so any divergence between them fails naming the class.
		/// </summary>
		private static void AssertHookSurface(string name, object hook)
		{
			AssertState(name, hook, true, "", "on a hook a factory returned");

			Call(hook, "Start");
			AssertState(name, hook, true, "", "after Start on a running hook, which does nothing");

			var description = Description(hook);
			Call(hook, "Stop");
			AssertState(name, hook, false, "Stopped", "after Stop");

			// A restart registers for real, so a host without this event source ends the new run Failed.
			Call(hook, "Start");
			Assert.AreEqual(description, Description(hook), $"{name}: a restart keeps the hook's description.");
			var restarted = (string)Get(hook, "EndReason");
			Assert.IsTrue(restarted is "" or "Failed", $"{name}: EndReason after Start is {restarted}.");
			AssertState(name, hook, restarted == "", restarted, "after Start: a stopped hook begins a fresh run");
			Call(hook, "Stop");
			AssertState(name, hook, false, restarted == "" ? "Stopped" : "Failed", "after stopping the fresh run");
			Call(hook, "Stop");
			AssertState(name, hook, false, restarted == "" ? "Stopped" : "Failed", "after a second Stop, which is idempotent");
		}

		[Test, Category("Internal"), NonParallelizable]
		public void HookSurfaceIsIdenticalAcrossSources()
		{
			foreach (var (name, create) in Factories)
				AssertHookSurface(name, create());
		}

		/// <summary>The surface the state machine replaced is gone, not merely unused: nothing the old
		/// four-state vocabulary spelled survives beside the new one.</summary>
		[Test, Category("Internal")]
		public void RetiredMembersAreGone()
		{
			foreach (var (name, create) in Factories)
			{
				var type = create().GetType();

				foreach (var gone in new[] { "Status", "IsActive", "Count", "Paused" })
					Assert.IsNull(type.GetProperty(gone), $"{name} still exposes {gone}.");

				Assert.IsNull(type.GetMethod("Pause"), $"{name} still exposes Pause, which is cut.");
				Assert.AreEqual(typeof(Any), type.GetMethod("__Delete").DeclaringType,
					$"{name} overrides __Delete: garbage collection must never cancel a subscription.");
			}

			Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticPause"), "WinEvent.Pause is gone; loop over WinEvent.Hooks.");
			Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticget_Paused"), "WinEvent.Paused is gone.");
			Assert.IsNull(typeof(Ks.WinEvent).GetProperty("EventType"), "EventType is gone.");
			Assert.IsNull(typeof(Ks.WinEvent).GetProperty("EventName"), "EventName is gone: one object carries several slots.");

			foreach (var factory in new[] { "Active", "Exist", "NotExist", "Move", "Minimize", "Restore", "TitleChange", "CaretMove" })
				Assert.IsNull(typeof(Ks.WinEvent).GetMethod(factory), $"WinEvent.{factory} is gone: construct a WinEvent and assign On{factory}.");
			Assert.IsEmpty(typeof(Ks.EventHook).GetConstructors(), "EventHook itself is never constructed; its subclasses are.");
		}

		/// <summary>Dropping a handle never stops a hook: the manager roots it, and __Delete is not overridden.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void DeleteDoesNotStopTheSubscription()
		{
			foreach (var (name, create) in Factories)
			{
				var hook = create();
				_ = ((KeysharpObject)hook).__Delete();
				AssertState(name, hook, true, "", "after __Delete: garbage collection never cancels a subscription");
				Call(hook, "Stop");
			}
		}

		/// <summary>
		/// A full round trip through the manager — register, enumerate, stop, sweep by owner — so the registration
		/// bookkeeping, the Hooks snapshot and the teardown reason are actually exercised. Constructing a
		/// registration alone never lists it, so an enumeration test built that way would pass on an empty array.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void RegisterEnumerateAndStopThroughTheManager()
		{
			var script = Script.TheScript;
			var manager = script.WinEventManager;
			var scheduler = script.EventScheduler;

			Ks.WinEvent Subscribe(WindowEventType type)
			{
				var reg = WinEventRun(type);
				var hook = new Ks.WinEvent { sub = reg };
				reg.scriptObject = hook;
				manager.Register(reg);
				return hook;
			}

			var moved = Subscribe(WindowEventType.Move);
			var exists = Subscribe(WindowEventType.Exist);

			if (moved.EndReason == "Failed")
				Assert.Ignore("This environment has no window-event source, so nothing can be registered.");

			Assert.IsTrue(moved.InProgress && exists.InProgress);

			var listed = (Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null);
			Assert.AreEqual(2L, listed.Length, "Hooks lists every live subscription.");
			Assert.AreSame(moved, listed[1], "Hooks hands back the very objects the factories returned, in start order.");
			Assert.AreSame(exists, listed[2]);

			_ = moved.Stop();
			Assert.AreEqual("Stopped", moved.EndReason);
			Assert.IsTrue(exists.InProgress, "Stopping one subscription leaves the others registered.");
			Assert.AreEqual(1L, ((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length,
				"A stopped hook leaves the list, while the array already handed out is unaffected.");
			Assert.AreEqual(2L, listed.Length);

			Assert.IsTrue(manager.RemoveOwned(scheduler), "The surviving subscription is swept by its owner.");
			Assert.AreEqual("Exit", exists.EndReason, "A teardown ends a hook with Exit, not Stopped.");
			Assert.AreEqual("Stopped", moved.EndReason, "A later teardown does not overwrite the first reason.");
			Assert.IsFalse(manager.RemoveOwned(scheduler), "A second sweep finds nothing.");
		}

		/// <summary>
		/// Stop() takes effect at the call: a callback queued before it is discarded when it would run, rather than
		/// delivered late.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void QueuedCallbacksAreDiscardedAfterStop()
		{
			var script = Script.TheScript;
			var calls = 0;
			var cb = new KeysharpFunc((Func<object, object, object>)((hook, type) =>
			{
				calls++;
				return "";
			}));

			if (Ks.KeysharpClipboard.OnChange(null, cb) is not Ks.ClipboardHook hook)
			{
				Assert.Fail("OnChange did not return a hook.");
				return;
			}

			void Drain() => Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);

			try
			{
				script.ClipboardEventManager.Dispatch(1L);
				Drain();
				Assert.AreEqual(1, calls, "An event on a running hook is delivered.");

				script.ClipboardEventManager.Dispatch(1L);
				_ = hook.Stop();
				Drain();
				Assert.AreEqual(1, calls, "An event queued before Stop is discarded.");

				// The ended run's queued callbacks hold its registration, so a restart does not revive them.
				script.ClipboardEventManager.Dispatch(1L);
				_ = hook.Stop();
				_ = hook.Start();
				Drain();
				Assert.AreEqual(1, calls, "An event queued before Stop is discarded even after a restart.");

				script.ClipboardEventManager.Dispatch(1L);
				Drain();
				Assert.AreEqual(2, calls, "The restarted hook receives new events.");
			}
			finally
			{
				_ = hook.Stop();
			}
		}

		/// <summary>A running hook keeps the script alive exactly when its AHK counterpart would: a window or clipboard
		/// watch does, while a display or device change, which AHK scripts watch through OnMessage, does not.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void PersistenceFollowsTheFamily()
		{
			var script = Script.TheScript;
			Assert.IsTrue(script.WinEventManager.KeepsScriptRunning);
			Assert.IsTrue(script.ClipboardEventManager.KeepsScriptRunning);
			Assert.IsFalse(script.MonitorEventManager.KeepsScriptRunning);
			Assert.IsFalse(script.AudioEventManager.KeepsScriptRunning);

			// The script's own answer is checked too, where nothing else in this host already keeps it running: that is
			// what the exit check reads, through the family's manager.
			var idle = !script.AnyPersistent(includeWindows: false);

			if (Ks.KeysharpClipboard.OnChange(null, Callback()) is not Ks.ClipboardHook hook)
			{
				Assert.Fail("OnChange did not return a hook.");
				return;
			}

			try
			{
				if (hook.EndReason == "Failed")
					Assert.Ignore("This host has no clipboard monitor, so no hook can run.");

				Assert.IsTrue(script.ClipboardEventManager.IsKeepingScriptRunning, "A running clipboard hook keeps the script alive.");

				if (idle)
					Assert.IsTrue(script.AnyPersistent(includeWindows: false), "The exit check sees the running clipboard hook.");

				_ = hook.Stop();
				Assert.IsFalse(script.ClipboardEventManager.IsKeepingScriptRunning, "A stopped one does not.");

				if (idle)
					Assert.IsFalse(script.AnyPersistent(includeWindows: false), "Nor does the exit check once it stops.");

				_ = hook.Start();
				Assert.IsTrue(script.ClipboardEventManager.IsKeepingScriptRunning, "A restarted one does again.");
			}
			finally
			{
				_ = hook.Stop();
			}

			var monitor = CreateMonitorHook();
			((Ks.MonitorHook)monitor).sub.Register();

			try
			{
				Assert.IsFalse(script.MonitorEventManager.IsKeepingScriptRunning, "A running display-change hook does not.");

				if (idle)
					Assert.IsFalse(script.AnyPersistent(includeWindows: false), "Nor does the exit check.");
			}
			finally
			{
				_ = ((Ks.MonitorHook)monitor).Stop();
			}
		}

		/// <summary>A run whose end is recorded is over at once, even before the <c>Stop()</c> that ended it has
		/// unregistered it, so a <c>Start()</c> right after begins a new run instead of finding the old one running.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void StartAfterAnEndStillUnregisteringBeginsANewRun()
		{
			var hook = (Ks.MonitorHook)CreateMonitorHook();
			var old = hook.sub;
			old.Register();

			try
			{
				Assert.IsTrue(old.End(EventSubscriptionBase.EndReasonStopped), "A Stop elsewhere records the end first.");
				Assert.IsFalse(hook.InProgress, "The ended run reads as over while it is still listed.");
				_ = hook.Start();
				Assert.IsTrue(hook.InProgress, "Start begins a new run.");
				Assert.AreNotSame(old, hook.sub);
			}
			finally
			{
				old.Unregister();
				_ = hook.Stop();
			}
		}

		/// <summary>A source that throws while installing ends the new run Failed and unlists it, so it keeps nothing alive,
		/// and the source is synced again to the runs that remain, undoing whatever part of the install took.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void AnInstallThatThrowsEndsTheRunFailed()
		{
			var script = Script.TheScript;
			using var manager = new InstallFailingManager(script);
			var run = new InstallFailingManager.Run(script.EventScheduler, manager);
			run.Register();

			Assert.AreEqual("Failed", run.EndReason);
			Assert.IsFalse(run.IsActive, "The failed run holds no root.");
			Assert.IsFalse(manager.HasSubscriptions, "The failed run is not left listed.");
			Assert.IsFalse(manager.IsKeepingScriptRunning, "The failed run does not keep the script running.");
			Assert.AreEqual(2, manager.syncs, "The source is synced again once the run is removed.");
		}

		/// <summary>Slot assignments restarting a WinEvent on one thread while another stops it leave it stopped with
		/// nothing listed: whichever ends the old run first decides, and no restart outlives the Stop.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void StopRacingSlotRestartsLeavesTheHookStopped()
		{
			var we = new Ks.WinEvent("ahk_class NoSuchWindowClassForThisTest");
			we.OnExist = SlotCallback();
			_ = we.Start();

			if (we.EndReason == "Failed")
				Assert.Ignore("This environment has no window-event source.");

			var rounds = 0;
			var restarter = new Thread(() =>
			{
				for (var i = 0; i < 100; i++)
				{
					we.OnNotExist = SlotCallback();          // an empty slot set: a restart
					we.OnNotExist = "";                      // and cleared again: another
					_ = Interlocked.Increment(ref rounds);
				}
			});

			try
			{
				restarter.Start();
				SpinWait.SpinUntil(() => Volatile.Read(ref rounds) >= 10, TimeSpan.FromSeconds(10));
				_ = we.Stop();
				Assert.IsTrue(restarter.Join(TimeSpan.FromSeconds(30)), "The restarts finish.");

				Assert.IsFalse(we.InProgress, "The hook stays stopped.");
				Assert.AreEqual("Stopped", we.EndReason);
				Assert.AreEqual(0L, ((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length, "No run is left listed.");
				Assert.IsFalse(Script.TheScript.WinEventManager.IsKeepingScriptRunning);
			}
			finally
			{
				_ = restarter.Join(TimeSpan.FromSeconds(30));
				_ = we.Stop();
			}
		}

		// A family whose native install throws while any run is listed; its sync afterwards, with none, does not.
		private sealed class InstallFailingManager(Script script) : EventManagerBase<InstallFailingManager.Run, IDisposable, ValueTuple>(script, true)
		{
			internal int syncs;

			protected override ThreadKind CallbackThreadKind => ThreadKind.Event;

			protected override IDisposable CreateBackend() => null;

			protected override void SyncNativeLocked()
			{
				syncs++;

				if (registrations.Count > 0)
					throw new InvalidOperationException("The source could not be installed.");
			}

			internal sealed class Run(ScriptEventScheduler owner, InstallFailingManager manager)
				: EventSubscriptionBase(null, owner, manager.KeepsScriptRunning)
			{
				internal override void Unregister() => manager.Unregister(this);

				internal override void Register() => manager.Register(this);
			}
		}

		/// <summary>A callback that could never run is refused where it is given, not at every event: one that is not a
		/// function, one requiring more arguments than the hook passes, and one accepting fewer, as AHK's ValidateFunctor
		/// refuses them.</summary>
		[Test, Category("Internal")]
		public void UnusableCallbacksAreRefusedByTheFactory()
		{
			Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => Ks.KeysharpClipboard.OnChange(null, "not a function")).UserError);
			var needsThree = new KeysharpFunc((Func<object, object, object, object>)((a, b, c) => ""));
			Assert.IsInstanceOf<ValueError>(Assert.Throws<KeysharpException>(() => Ks.KeysharpClipboard.OnChange(null, needsThree)).UserError);
			var takesOne = new KeysharpFunc((Func<object, object>)(a => ""));
			Assert.IsInstanceOf<ValueError>(Assert.Throws<KeysharpException>(() => Ks.KeysharpClipboard.OnChange(null, takesOne)).UserError);
			// Variadic, but it still requires three parameters where the hook passes two.
			var needsThreeThenAny = new KeysharpFunc((Func<object, object, object, object[], object>)((a, b, c, rest) => ""));
			Assert.IsTrue(needsThreeThenAny.IsVariadic);
			Assert.IsInstanceOf<ValueError>(Assert.Throws<KeysharpException>(() => Ks.KeysharpClipboard.OnChange(null, needsThreeThenAny)).UserError);
			Assert.AreEqual(0L, ((Keysharp.Builtins.Array)Ks.KeysharpClipboard.staticget_Hooks(null)).Length,
				"A refused callback leaves no hook behind.");
		}

		/// <summary>
		/// The WinEvent object form: constructed idle with its criteria, callbacks assigned to slots, then started.
		/// A new callback for a set slot is swapped into the running run, setting or clearing a slot restarts it,
		/// clearing the last one stops it, and a callback that could never run is refused at the assignment.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void WinEventIsConstructedIdleAndStarted()
		{
			var we = new Ks.WinEvent("ahk_class NoSuchWindowClassForThisTest");
			Assert.IsFalse(we.InProgress);
			Assert.AreEqual("Stopped", we.EndReason, "An idle hook reads Stopped, as an unstarted AHK InputHook does.");
			Assert.AreEqual("ahk_class NoSuchWindowClassForThisTest", we.WinTitle);

			var cb = new KeysharpFunc((Func<object, object, object, object>)((hook, hwnd, time) => ""));
			_ = Assert.Throws<KeysharpException>(() => we.OnMove = "not a function");
			_ = Assert.Throws<KeysharpException>(() => we.OnMove = new KeysharpFunc((Func<object, object, object, object, object>)((a, b, c, d) => "")));
			Assert.IsNotInstanceOf<KeysharpFunc>(we.OnMove, "A refused callback leaves the slot empty.");

			_ = we.Start();
			Assert.IsFalse(we.InProgress, "Start() with no slot set does nothing, as such a run could never report.");

			we.OnExist = cb;
			Assert.AreSame(cb, we.OnExist);
			_ = we.Start();

			try
			{
				if (we.EndReason == "Failed")
					Assert.Ignore("This environment has no window-event source.");

				Assert.IsTrue(we.InProgress);
				Assert.IsTrue(Script.TheScript.WinEventManager.IsKeepingScriptRunning, "A running WinEvent keeps the script alive.");
				var run = we.sub;
				var owner = run.OwnerScheduler;

				var other = new KeysharpFunc((Func<object, object, object, object>)((hook, hwnd, time) => ""));
				we.OnExist = other;
				Assert.AreSame(run, we.sub, "A new callback for a set slot keeps the run.");
				Assert.AreSame(other, ((WinEventRegistration)we.sub).callbacks[(int)WindowEventType.Exist], "The running run calls the new callback.");

				we.OnNotExist = cb;
				Assert.IsTrue(we.InProgress, "Setting an empty slot while running restarts the hook.");
				Assert.AreNotSame(run, we.sub, "The restart is a fresh run.");
				Assert.AreSame(owner, we.sub.OwnerScheduler, "The restart stays on the thread that started the hook.");
				Assert.IsFalse(run.IsActive, "The old run ended, so its queued callbacks are discarded.");
				Assert.AreEqual(1L, ((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length, "One object is listed once.");

				we.OnExist = "";
				Assert.IsNotInstanceOf<KeysharpFunc>(we.OnExist, "An empty string clears a slot.");
				Assert.IsTrue(we.InProgress, "A slot is still set, so the hook runs on.");

				we.OnNotExist = "";
				Assert.IsFalse(we.InProgress, "Clearing the last slot stops the hook.");
				Assert.AreEqual("Stopped", we.EndReason);
			}
			finally
			{
				_ = we.Stop();
			}

			Assert.AreEqual("Stopped", we.EndReason);
			Assert.IsFalse(Script.TheScript.WinEventManager.IsKeepingScriptRunning);
		}

		/// <summary>
		/// OnActive reports every matching activation, and OnNotActive the foreground leaving the matching windows, with
		/// the window last seen active; the window already active at Start() is recorded, not reported. Fed synthetic
		/// events to a run with no criteria and hidden windows included, so every handle but 0 matches.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void ActiveAndNotActiveFollowTheForeground()
		{
			var script = Script.TheScript;
			var manager = script.WinEventManager;
			var log = new List<string>();
			var foreground = (long)WindowQuery.GetForegroundWindowHandle();

			// The run also hears real foreground changes while this runs; only the synthetic windows and the seeded one count.
			var watched = new HashSet<long> { 0x1110, 0x2220, 0x3330, foreground };

			KeysharpFunc Record(string slot) => new((Func<object, object, object, object>)((hook, hwnd, time) =>
			{
				if (watched.Contains(hwnd.Al()))
					log.Add($"{slot} {hwnd}");

				return "";
			}));

			var callbacks = new object[WinEventManager.typeCount];
			callbacks[(int)WindowEventType.Active] = Record("Active");
			callbacks[(int)WindowEventType.NotActive] = Record("NotActive");
			var options = WinEventRegistration.CaptureSearchOptions(script);
			options.DetectHiddenWindows = true;
			var reg = new WinEventRegistration(null, options, callbacks, script.EventScheduler, manager);
			var hook = new Ks.WinEvent { sub = reg };
			reg.scriptObject = hook;
			manager.Register(reg);

			void Drain() => Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, yieldTick: false, pumpUi: false);
			void Activate(nint hwnd) => manager.OnNativeEvent(new WindowEventRaw(WindowEventType.Active, hwnd, 0));

			try
			{
				if (hook.EndReason == "Failed")
					Assert.Ignore("This environment has no window-event source.");

				Drain();
				Assert.IsEmpty(log, "The window active at Start() is recorded, not reported.");

				Activate(0);                                  // the seeded window leaves: NotActive for it
				Activate(0x1110);
				Activate(0x2220);                             // matching to matching: Active only
				Activate(0);                                  // matching to none: NotActive for the window last active
				Activate(0);                                  // none to none: nothing
				Activate(0x3330);
				Drain();

				var expected = new List<string> { "Active 4368", "Active 8736", "NotActive 8736", "Active 13104" };

				if (foreground != 0)
					expected.Insert(0, $"NotActive {foreground}");

				Assert.That(log, Is.EqualTo(expected));
			}
			finally
			{
				_ = hook.Stop();
			}
		}

		/// <summary>A running WinEvent keeps the real thread that started it running and a display-change hook does not,
		/// as their AHK counterparts keep a script running or not.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void RealThreadRootsFollowTheFamily() => Assert.IsTrue(TestScript("events-realthread-roots", false));
	}
}
