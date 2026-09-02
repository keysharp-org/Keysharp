using Keysharp.Internals.Events;
using Keysharp.Internals.Window;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	/// <summary>
	/// The surface every event-subscription hook shares — Status, IsActive, Count, Paused, Pause, Stop and
	/// __Delete — run through one assertion body for Ks.WinEvent, Ks.MonitorHook and Ks.ClipboardHook, so the
	/// three cannot drift apart. Members are reached by name, so a rename or a dropped member fails here rather
	/// than silently in a script.
	/// <para>The registrations are built directly rather than through the public factories: that installs no
	/// native backend, so the fixture is headless and hooks nothing real.</para>
	/// </summary>
	[TestFixture, Category("Internal"), Category("Curated")]
	public class EventHookTests : TestRunner
	{
		/// <summary>Every hook class, behind the same factory shape, so each test covers all three by construction.</summary>
		private static (string Name, Func<long, object> Create)[] Factories =>
		[
			("Ks.WinEvent", CreateWinEvent),
			("Ks.MonitorHook", CreateMonitorHook),
			("Ks.ClipboardHook", CreateClipboardHook),
		];

		private static KeysharpFunc Callback() => new((Func<object, object, object>)((hook, arg) => ""));

		private static object CreateWinEvent(long count)
		{
			var script = Script.TheScript;
			var reg = new WinEventRegistration(WindowEventType.Active, null, Callback(), count, script.EventScheduler, script.WinEventManager);
			var hook = new Ks.WinEvent { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateMonitorHook(long count)
		{
			var script = Script.TheScript;
			var reg = new MonitorEventRegistration(Callback(), count, script.EventScheduler, script.MonitorEventManager);
			var hook = new Ks.MonitorHook { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object CreateClipboardHook(long count)
		{
			var script = Script.TheScript;
			var reg = new ClipboardEventRegistration(Callback(), count, script.EventScheduler, script.ClipboardEventManager);
			var hook = new Ks.ClipboardHook { sub = reg };
			reg.scriptObject = hook;
			return hook;
		}

		private static object Get(object hook, string name)
		{
			var prop = hook.GetType().GetProperty(name);
			Assert.IsNotNull(prop, $"{hook.GetType().Name} is missing the {name} member.");
			return prop.GetValue(hook);
		}

		private static void Set(object hook, string name, object value)
		{
			var prop = hook.GetType().GetProperty(name);
			Assert.IsNotNull(prop, $"{hook.GetType().Name} is missing the {name} member.");
			prop.SetValue(hook, value);
		}

		private static object Invoke(object hook, string name, params object[] args)
		{
			var types = new Type[args.Length];
			System.Array.Fill(types, typeof(object));
			var method = hook.GetType().GetMethod(name, types);
			Assert.IsNotNull(method, $"{hook.GetType().Name} is missing the {name}({types.Length} args) member.");
			return method.Invoke(hook, args);
		}

		/// <summary>
		/// The whole lifecycle of one hook: live, paused, live again, then stopped. Written once and applied to
		/// every hook class — any divergence between them shows up as a failure naming the class.
		/// </summary>
		private static void AssertHookSurface(string name, object hook, long count)
		{
			Assert.AreEqual("Active", Get(hook, "Status"), $"{name}: a fresh hook is active.");
			Assert.AreEqual(true, Get(hook, "IsActive"), name);
			Assert.AreEqual(count, Get(hook, "Count"), $"{name}: Count starts at the requested budget.");
			Assert.AreEqual(false, Get(hook, "Paused"), name);

			// A null argument is the script-visible default, and it pauses.
			Assert.AreEqual(true, Invoke(hook, "Pause", [null]), $"{name}: Pause() defaults to pausing.");
			Assert.AreEqual("Paused", Get(hook, "Status"), name);
			Assert.AreEqual(false, Get(hook, "IsActive"), $"{name}: a paused hook is not active.");
			Assert.AreEqual(true, Get(hook, "Paused"), name);
			Assert.AreEqual(count, Get(hook, "Count"), $"{name}: pausing does not consume the budget.");

			Assert.AreEqual(false, Invoke(hook, "Pause", 0L), $"{name}: Pause(0) resumes.");
			Assert.AreEqual("Active", Get(hook, "Status"), name);
			Assert.AreEqual(true, Invoke(hook, "Pause", -1L), $"{name}: Pause(-1) toggles.");
			Assert.AreEqual(false, Invoke(hook, "Pause", -1L), $"{name}: Pause(-1) toggles back.");

			Set(hook, "Paused", true);
			Assert.AreEqual(true, Get(hook, "Paused"), $"{name}: Paused is writable.");
			Set(hook, "Paused", false);
			Assert.AreEqual(true, Get(hook, "IsActive"), name);

			Invoke(hook, "Stop");
			Assert.AreEqual("Stopped", Get(hook, "Status"), name);
			Assert.AreEqual(false, Get(hook, "IsActive"), name);
			// docs/api-review-naming-consistency.md: a stopped hook reports Count 0 — it will fire no more times.
			Assert.AreEqual(0L, Get(hook, "Count"), $"{name}: a stopped hook reports Count 0.");
			Assert.AreEqual(false, Get(hook, "Paused"), $"{name}: a stopped hook reports Paused false.");

			Set(hook, "Paused", true);
			Assert.AreEqual(false, Get(hook, "Paused"), $"{name}: a stopped hook ignores writes to Paused.");
			Assert.AreEqual(false, Invoke(hook, "Pause", [null]), $"{name}: Pause() on a stopped hook does nothing.");

			Invoke(hook, "Stop");
			Assert.AreEqual("Stopped", Get(hook, "Status"), $"{name}: Stop is idempotent.");
		}

		[Test, Category("Internal"), NonParallelizable]
		public void HookSurfaceIsIdenticalAcrossSources()
		{
			foreach (var (name, create) in Factories)
				AssertHookSurface(name, create(-1L), -1L);
		}

		[Test, Category("Internal"), NonParallelizable]
		public void HookSurfaceIsIdenticalWithAFiniteCount()
		{
			foreach (var (name, create) in Factories)
				AssertHookSurface(name, create(3L), 3L);
		}

		/// <summary>Dropping a hook stops it, and does so without needing Stop() to have been called first.</summary>
		[Test, Category("Internal"), NonParallelizable]
		public void HookDeleteStopsTheSubscription()
		{
			foreach (var (name, create) in Factories)
			{
				var hook = create(-1L);
				_ = ((KeysharpObject)hook).__Delete();
				Assert.AreEqual("Stopped", Get(hook, "Status"), $"{name}: __Delete stops the hook.");
				Assert.AreEqual(0L, Get(hook, "Count"), name);
			}
		}

		/// <summary>
		/// A full round trip through the manager — register, stop, sweep by owner — so the registration
		/// bookkeeping and the intake's published view are actually built and torn down. Constructing a
		/// registration alone never touches either.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void RegisterAndStopRoundTripThroughTheManager()
		{
			var script = Script.TheScript;
			var manager = script.WinEventManager;
			var scheduler = script.EventScheduler;

			Ks.WinEvent Subscribe(WindowEventType type)
			{
				var reg = new WinEventRegistration(type, null, Callback(), -1L, scheduler, manager);
				var hook = new Ks.WinEvent { sub = reg };
				reg.scriptObject = hook;
				manager.Register(reg);
				return hook;
			}

			var moved = Subscribe(WindowEventType.Move);
			var exists = Subscribe(WindowEventType.Exist);
			Assert.AreEqual("Active", moved.Status);
			Assert.AreEqual("Active", exists.Status);

			_ = moved.Stop();
			Assert.AreEqual("Stopped", moved.Status);
			Assert.AreEqual("Active", exists.Status, "Stopping one subscription leaves the others registered.");

			Assert.IsTrue(manager.RemoveOwned(scheduler), "The surviving subscription is swept by its owner.");
			Assert.AreEqual("Stopped", exists.Status);
			Assert.IsFalse(manager.RemoveOwned(scheduler), "A second sweep finds nothing.");
		}

		/// <summary>
		/// A count no subscription can honor is rejected by the factory, before anything is constructed — so it
		/// cannot leave a half-built registration behind, nor a permanently rooted hook that can never fire.
		/// </summary>
		[Test, Category("Internal"), NonParallelizable]
		public void InvalidCountIsRejectedByTheFactory()
		{
			static void AssertRejected(string what, TestDelegate subscribe)
			{
				var raised = Assert.Throws<KeysharpException>(subscribe);
				Assert.IsInstanceOf<ValueError>(raised.UserError, $"{what} must raise a ValueError.");
			}

			foreach (var bad in new object[] { 0L, -2L })
			{
				AssertRejected($"Monitor.OnChange(, {bad})", () => Ks.KeysharpMonitor.OnChange(null, Callback(), bad));
				AssertRejected($"WinEvent.Active(,, {bad})", () => Ks.WinEvent.Active(null, Callback(), null, bad));
				AssertRejected($"Clipboard.OnChange(, {bad})", () => Ks.KeysharpClipboard.OnChange(null, Callback(), bad));
			}
		}

		/// <summary>WinEvent alone names the event it listens for.</summary>
		[Test, Category("Internal")]
		public void WinEventReportsItsEventType()
		{
			var hook = (Ks.WinEvent)CreateWinEvent(-1L);
			Assert.AreEqual("Active", hook.EventType);
			_ = hook.Stop();
			Assert.AreEqual("Active", hook.EventType, "EventType survives Stop — it describes the subscription, not its state.");
		}
	}
}
