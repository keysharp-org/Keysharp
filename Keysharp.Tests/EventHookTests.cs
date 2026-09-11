using Keysharp.Internals.Audio;
using Keysharp.Internals.Events;
using Keysharp.Internals.Window;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The surface every factory-created event hook shares — InProgress, EndReason, Start, Stop and Pause — run
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

		private static object CreateWinEvent()
		{
			var script = Script.TheScript;
			var reg = new WinEventRegistration(WindowEventType.Active, null, Callback(), script.EventScheduler, script.WinEventManager);
			var hook = new Ks.WinEvent { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateMonitorHook()
		{
			var script = Script.TheScript;
			var reg = new MonitorEventRegistration(Callback(), script.EventScheduler, script.MonitorEventManager);
			var hook = new Ks.MonitorHook { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateClipboardHook()
		{
			var script = Script.TheScript;
			var reg = new ClipboardEventRegistration(Callback(), script.EventScheduler, script.ClipboardEventManager);
			var hook = new Ks.ClipboardHook { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateDeviceHook()
		{
			var script = Script.TheScript;
			var reg = new AudioEventRegistration(Callback(), script.EventScheduler, script.AudioEventManager, "All");
			var hook = new Ks.Audio.DeviceHook { sub = reg };
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
		/// The whole state machine of one hook — Active, Idle, Active, Ended — written once and applied to every
		/// hook class, so any divergence between them fails naming the class.
		/// </summary>
		private static void AssertHookSurface(string name, object hook)
		{
			AssertState(name, hook, true, "", "on a hook a factory returned");

			Call(hook, "Pause");
			AssertState(name, hook, false, "", "after Pause: Idle, not ended");
			Call(hook, "Pause");
			AssertState(name, hook, false, "", "after a second Pause, which does nothing");

			Call(hook, "Start");
			AssertState(name, hook, true, "", "after Start resumes it");
			Call(hook, "Start");
			AssertState(name, hook, true, "", "after Start on a running hook, which does nothing");

			Call(hook, "Stop");
			AssertState(name, hook, false, "Stopped", "after Stop");

			Call(hook, "Start");
			AssertState(name, hook, false, "Stopped", "after Start: a factory hook cannot begin again");
			Call(hook, "Pause");
			AssertState(name, hook, false, "Stopped", "after Pause on an ended hook");
			Call(hook, "Stop");
			AssertState(name, hook, false, "Stopped", "after a second Stop, which is idempotent");
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

				Assert.IsNull(type.GetMethod("Pause", [typeof(object)]), $"{name} still exposes Pause(newState).");
				Assert.AreEqual(typeof(Any), type.GetMethod("__Delete").DeclaringType,
					$"{name} overrides __Delete: garbage collection must never cancel a subscription.");
			}

			Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticPause"), "WinEvent.Pause is gone; loop over WinEvent.Hooks.");
			Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticget_Paused"), "WinEvent.Paused is gone.");
			Assert.IsNull(typeof(Ks.WinEvent).GetProperty("EventType"), "EventType became EventName.");
			Assert.IsEmpty(typeof(Ks.EventHook).GetConstructors(), "Hooks come only from factories.");
		}

		[Test, Category("Internal"), NonParallelizable]
		public void InvalidEventTypeErrorRecovery()
		{
			var script = Script.TheScript;
			script.ErrorStdOut = true;
			// Scripts cannot create a registration whose native event has no public name.
			var reg = new WinEventRegistration(unchecked((WindowEventType)(-1)), null, Callback(),
				script.EventScheduler, script.WinEventManager);
			var hook = new Ks.WinEvent { sub = reg };
			reg.scriptObject = hook;
			var handled = new List<(object Error, object Mode)>();
			var handler = new KeysharpFunc((Func<object, object, object>)((error, mode) =>
			{
				handled.Add((error, mode));
				return -1L;
			}));
			_ = Errors.OnError(handler);

			try
			{
				Assert.AreEqual("", hook.EventName);
				Assert.AreEqual(1, handled.Count);
				Assert.AreEqual(typeof(Error), handled[0].Error.GetType());
				Assert.AreEqual("Return", handled[0].Mode);
				var words = ((Error)handled[0].Error).Message.Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries);

				foreach (var name in new[] { "Active", "Exist", "NotExist", "Move", "Minimize", "Restore", "TitleChange", "CaretMove" })
					Assert.IsTrue(words.Contains(name, StringComparer.Ordinal), $"The diagnostic must list the public event type {name}.");

				foreach (var name in new[] { "Create", "Close", "Show" })
					Assert.IsFalse(words.Contains(name, StringComparer.Ordinal), $"The diagnostic must not suggest the internal event type {name}.");

				using (new Loops.TryScope(typeof(Error)))
					Assert.AreEqual(typeof(Error), Assert.Throws<KeysharpException>(() => _ = hook.EventName).UserError.GetType());

				Assert.AreEqual(1, handled.Count, "a caught error does not reach OnError");
			}
			finally
			{
				_ = Errors.OnError(handler, 0L);
				_ = hook.Stop();
			}
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
				var reg = new WinEventRegistration(type, null, Callback(), scheduler, manager);
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
			Assert.AreSame(moved, listed[1], "Hooks hands back the very objects the factories returned, oldest first.");
			Assert.AreSame(exists, listed[2]);

			moved.Pause();
			Assert.AreEqual(2L, ((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length,
				"A paused hook stays listed, or a loop could never resume it.");

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
		/// Stop() and Pause() take effect at the call: a callback queued before either is discarded when it would
		/// run, rather than delivered late. A resume before the queue drains does deliver it — the accepted cost
		/// of guarding only at the gate.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void QueuedCallbacksAreDiscardedAfterStopOrPause()
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
				_ = hook.Pause();
				Drain();
				Assert.AreEqual(0, calls, "An event queued before Pause is discarded while the hook is paused.");

				script.ClipboardEventManager.Dispatch(1L);
				_ = hook.Start();
				Drain();
				Assert.AreEqual(1, calls, "An event queued while paused runs if the hook resumed before it drained.");

				script.ClipboardEventManager.Dispatch(1L);
				_ = hook.Stop();
				Drain();
				Assert.AreEqual(1, calls, "An event queued before Stop is discarded.");
			}
			finally
			{
				_ = hook.Stop();
			}
		}

		/// <summary>WinEvent names the event it listens for; the other families subscribe to one event each.</summary>
		[Test, Category("Internal")]
		public void WinEventReportsItsEventName()
		{
			var hook = (Ks.WinEvent)CreateWinEvent();
			Assert.AreEqual("Active", hook.EventName);
			_ = hook.Stop();
			Assert.AreEqual("Active", hook.EventName, "EventName survives Stop — it describes the subscription, not its state.");
		}
	}
}
