namespace Keysharp.Tests;

/// <summary>
/// The WinEvent lifecycle and its native event-state contracts.
/// <para>Most registrations are built directly rather than through the public factories: that installs no
/// native backend, so the fixture is headless and hooks nothing real.</para>
/// </summary>
[TestFixture, Category("Internal"), Category("Curated")]
public class EventHookTests : TestRunner
{
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
			if (hwnd.TryCoerceLong(out var h) && watched.Contains(h))
				log.Add($"{slot} {hwnd}");

			return "";
		}));

		var callbacks = new object[WinEventManager.typeCount];
		callbacks[(int)WindowEventType.Active] = Record("Active");
		callbacks[(int)WindowEventType.NotActive] = Record("NotActive");
		var reg = new WinEventRegistration(null, WindowSearchSettings.Current with { DetectHiddenWindows = true }, callbacks, script.EventScheduler, manager);
		var hook = new Ks.WinEvent { sub = reg };
		reg.scriptObject = hook;
		manager.Register(reg);

		void Drain() => Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: false, pumpUi: false);
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

	/// <summary>A source that throws while installing ends the new run Failed and unlists it, so it keeps nothing alive,
	/// and the source is synced again to the runs that remain, undoing whatever part of the install took.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void AnInstallThatThrowsEndsTheRunFailed()
	{
		var script = Script.TheScript;
		using var manager = new InstallFailingManager(script);
		var run = new InstallFailingManager.Run(script.EventScheduler, manager);
		run.Register();

		Assert.That(run.EndReason, Is.EqualTo("Failed"));
		Assert.That(run.IsActive, Is.False, "The failed run holds no root.");
		Assert.That(manager.HasSubscriptions, Is.False, "The failed run is not left listed.");
		Assert.That(manager.IsKeepingScriptRunning, Is.False, "The failed run does not keep the script running.");
		Assert.That(manager.syncs, Is.EqualTo(2), "The source is synced again once the run is removed.");
	}

	[TestCase(false), TestCase(true)]
	public void RefreshRequestsAreCoalesced(bool failFirst)
	{
		using var release = new ManualResetEventSlim();
		var entered = new TaskCompletionSource();
		var completed = new TaskCompletionSource();
		var calls = 0;
		using var manager = new InstallFailingManager(Script.TheScript, () =>
		{
			if (Interlocked.Increment(ref calls) == 1)
			{
				entered.SetResult();
				if (!release.Wait(5000)) throw new TimeoutException();
				if (failFirst) throw new InvalidOperationException("Refresh failed.");
			}
			else completed.TrySetResult();
		});
		try
		{
			manager.RequestRefresh();
			Assert.That(entered.Task.Wait(5000), Is.True);
			for (var i = 0; i < 32; i++) manager.RequestRefresh();
			Assert.That(Volatile.Read(ref calls), Is.EqualTo(1), "Refreshes must not overlap.");
			release.Set();
			Assert.That(completed.Task.Wait(5000), Is.True, "Pending work survives a failed refresh.");
			Assert.That(Volatile.Read(ref calls), Is.EqualTo(2), "Requests during a refresh merge into one follow-up.");
		}
		finally { release.Set(); }
	}

	/// <summary>A deactivation clears the tracked foreground only when it names that window, so focus moving to no
	/// window does not leave a stale foreground behind.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void DeactivateClearsOnlyTheTrackedForeground()
	{
		using var manager = new WinEventManager(Script.TheScript, () => 0, () => new ForegroundBackend());

		nint Send(WindowEventType type, nint hwnd)
		{
			manager.OnNativeEvent(new WindowEventRaw(type, hwnd, 0));
			return manager.ForegroundWindowHandle;
		}

		manager.SetForegroundTracking(true);

		try
		{
			Assert.That(Send(WindowEventType.Active, 0x1110), Is.EqualTo((nint)0x1110));
			Assert.That(Send(WindowEventType.Deactivate, 0x2220), Is.EqualTo((nint)0x1110), "Another window's deactivation is ignored.");
			Assert.That(Send(WindowEventType.Deactivate, 0x1110), Is.EqualTo(IntPtr.Zero), "The foreground window's deactivation clears it.");
		}
		finally
		{
			manager.SetForegroundTracking(false);
		}
	}

	/// <summary>Dropping a handle never stops a hook: the manager roots it, and __Delete is not overridden.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void DeleteDoesNotStopTheSubscription()
	{
		var hook = CreateWinEvent();
		_ = hook.__Delete();
		AssertState(hook, true, "");
		_ = hook.Stop();
	}

	[TestCase(0), TestCase(0x1110)]
	public void ForegroundCacheInitialization(int initial)
	{
		var backend = new ForegroundBackend();
		nint foreground = initial;
		nint duringStartup = -1;
		var queries = 0;
		using var manager = new WinEventManager(Script.TheScript,
			() => { queries++; return foreground; }, () => backend);
		backend.Starting = () => duringStartup = manager.ForegroundWindowHandle;

		manager.SetForegroundTracking(true);
		Assert.That(duringStartup, Is.EqualTo((nint)initial), "Reads during backend startup use the live foreground.");
		Assert.That(queries, Is.EqualTo(2), "The startup read and initial snapshot both query the foreground.");
		foreground = 0x2220;
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo((nint)initial), "A seeded zero is also a valid cached foreground.");
		Assert.That(queries, Is.EqualTo(2), "A seeded cache avoids live queries.");

		manager.SetForegroundTracking(false);
		backend.Starting = () =>
		{
			duringStartup = manager.ForegroundWindowHandle;
			backend.Sink(new WindowEventRaw(WindowEventType.Active, 0, 0));
		};
		manager.SetForegroundTracking(true);
		Assert.That(duringStartup, Is.EqualTo((nint)0x2220), "Restarting tracking does not expose the previous cache.");
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo(IntPtr.Zero), "A native event wins over the startup snapshot.");
		Assert.That(queries, Is.EqualTo(4), "An authoritative native zero does not cause another live query.");
	}

	[TestCase((int)WindowEventType.Close), TestCase((int)WindowEventType.Deactivate)]
	public void ForegroundRemovalRejectsPendingSeed(int type)
	{
		var backend = new ForegroundBackend();
		var queries = 0;
		using var manager = new WinEventManager(Script.TheScript, () =>
		{
			if (++queries == 1)
			{
				backend.Sink(new WindowEventRaw((WindowEventType)type, 0x1110, 0));
				return 0x1110;
			}

			return 0;
		}, () => backend);

		manager.SetForegroundTracking(true);
		Assert.That(queries, Is.EqualTo(1));
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo(IntPtr.Zero), "The removed window cannot become the cached foreground.");
		Assert.That(queries, Is.EqualTo(2), "An invalidated seed falls back to a fresh foreground query.");
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo(IntPtr.Zero));
		Assert.That(queries, Is.EqualTo(3), "A removal does not establish an authoritative zero foreground.");

		backend.Sink(new WindowEventRaw(WindowEventType.Active, 0, 0));
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo(IntPtr.Zero));
		Assert.That(queries, Is.EqualTo(3), "An authoritative native zero finishes cache initialization.");
	}

	[Test]
	public void ForegroundStartupFailureCanRetry()
	{
		var backend = new ForegroundBackend();
		var starts = 0;
		var stopped = WindowEventMask.None;
		backend.Starting = () =>
		{
			if (++starts == 1)
				throw new InvalidOperationException("Native install failed.");
		};
		backend.Stopping = mask => stopped |= mask;
		using var manager = new WinEventManager(Script.TheScript, () => 0x1110, () => backend);

		manager.SetForegroundTracking(true);
		Assert.That(stopped, Is.EqualTo(WindowEventMask.Active | WindowEventMask.Close),
			"A failed startup rolls back its partially installed hooks.");
		manager.SetForegroundTracking(true);
		Assert.That(starts, Is.EqualTo(2), "A later attempt must install the native source again.");
		backend.Sink(new WindowEventRaw(WindowEventType.Active, 0x2220, 0));
		Assert.That(manager.ForegroundWindowHandle, Is.EqualTo((nint)0x2220));
	}

	[Test, Category("Internal"), NonParallelizable]
	public void HookSurfaceIsIdenticalAcrossSources()
	{
		var hook = CreateWinEvent();
		AssertState(hook, true, "");
		_ = hook.Start();
		AssertState(hook, true, "");
		var callback = ((WinEventRegistration)hook.sub).callbacks[(int)WindowEventType.Active];
		_ = hook.Stop();
		AssertState(hook, false, "Stopped");

		_ = hook.Start();
		Assert.That(((WinEventRegistration)hook.sub).callbacks[(int)WindowEventType.Active], Is.SameAs(callback),
			"A restart preserves the configured callback.");
		var restarted = hook.EndReason;
		Assert.IsTrue(restarted is "" or "Failed");
		AssertState(hook, restarted == "", restarted);
		_ = hook.Stop();
		AssertState(hook, false, restarted == "" ? "Stopped" : "Failed");
		_ = hook.Stop();
		AssertState(hook, false, restarted == "" ? "Stopped" : "Failed");
	}

	/// <summary>A running hook keeps the script alive exactly when its AHK counterpart would: a window or clipboard
	/// watch does, while a display or device change, which AHK scripts watch through OnMessage, does not.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void PersistenceFollowsTheFamily()
	{
		var script = Script.TheScript;
		Assert.IsTrue(script.WinEventManager.KeepsScriptRunning);
		Assert.That(script.MonitorEventManager.KeepsScriptRunning, Is.False);
		Assert.That(script.AudioEventManager.KeepsScriptRunning, Is.False);
		Assert.That(script.InputEventManager.KeepsScriptRunning, Is.False);
		var idle = !script.AnyPersistent(includeWindows: false);
		var callback = new KeysharpFunc((Func<object, object>)(_ => ""));
		_ = Ks.KeysharpClipboard.OnChange(null, callback);

		try
		{
			Assert.That(script.ClipFunctions.Count, Is.EqualTo(1));
			Assert.IsTrue(script.AnyPersistent(includeWindows: false));
			_ = Env.OnClipboardChange(callback, 0L);
			Assert.That(script.ClipFunctions.Count, Is.Zero);

			if (idle)
				Assert.That(script.AnyPersistent(includeWindows: false), Is.False);
		}
		finally
		{
			_ = Ks.KeysharpClipboard.OnChange(null, callback, 0L);
		}
	}

	/// <summary>A running WinEvent keeps the real thread that started it running and a display-change hook does not,
	/// as their AHK counterparts keep a script running or not.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void RealThreadRootsFollowTheFamily() => Assert.IsTrue(TestScript("events-realthread-roots", false));

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
		Assert.That(listed.Length, Is.EqualTo(2L), "Hooks lists every live subscription.");
		Assert.That(listed[1], Is.SameAs(moved), "Hooks hands back the very objects the factories returned, in start order.");
		Assert.That(listed[2], Is.SameAs(exists));

		_ = moved.Stop();
		Assert.That(moved.EndReason, Is.EqualTo("Stopped"));
		Assert.IsTrue(exists.InProgress, "Stopping one subscription leaves the others registered.");
		Assert.That(((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length, Is.EqualTo(1L),
			"A stopped hook leaves the list, while the array already handed out is unaffected.");
		Assert.That(listed.Length, Is.EqualTo(2L));

		Assert.IsTrue(manager.RemoveOwned(scheduler), "The surviving subscription is swept by its owner.");
		Assert.That(exists.EndReason, Is.EqualTo("Exit"), "A teardown ends a hook with Exit, not Stopped.");
		Assert.That(moved.EndReason, Is.EqualTo("Stopped"), "A later teardown does not overwrite the first reason.");
		Assert.That(manager.RemoveOwned(scheduler), Is.False, "A second sweep finds nothing.");
	}

	/// <summary>The surface the state machine replaced is gone, not merely unused: nothing the old
	/// four-state vocabulary spelled survives beside the new one.</summary>
	[Test, Category("Internal")]
	public void RetiredMembersAreGone()
	{
		var type = typeof(Ks.WinEvent);
		foreach (var gone in new[] { "Status", "IsActive", "Count", "Paused" })
			Assert.IsNull(type.GetProperty(gone), $"WinEvent still exposes {gone}.");

		Assert.IsNull(type.GetMethod("Pause"), "WinEvent still exposes Pause, which is cut.");
		Assert.That(type.GetMethod("__Delete").DeclaringType, Is.EqualTo(typeof(Any)),
			"WinEvent overrides __Delete: garbage collection must never cancel a subscription.");

		Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticPause"), "WinEvent.Pause is gone; loop over WinEvent.Hooks.");
		Assert.IsNull(typeof(Ks.WinEvent).GetMethod("staticget_Paused"), "WinEvent.Paused is gone.");
		Assert.IsNull(typeof(Ks.WinEvent).GetProperty("EventType"), "EventType is gone.");
		Assert.IsNull(typeof(Ks.WinEvent).GetProperty("EventName"), "EventName is gone: one object carries several slots.");

		foreach (var factory in new[] { "Active", "Exist", "NotExist", "Move", "Minimize", "Restore", "TitleChange", "CaretMove" })
			Assert.IsNull(typeof(Ks.WinEvent).GetMethod(factory), $"WinEvent.{factory} is gone: construct a WinEvent and assign On{factory}.");
		Assert.IsEmpty(typeof(Ks.EventHook).GetConstructors(), "EventHook itself is never constructed; its subclasses are.");
	}

	/// <summary>A run whose end is recorded is over at once, even before the <c>Stop()</c> that ended it has
	/// unregistered it, so a <c>Start()</c> right after begins a new run instead of finding the old one running.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void StartAfterAnEndStillUnregisteringBeginsANewRun()
	{
		var hook = CreateWinEvent();
		var old = hook.sub;
		old.Register();

		try
		{
			Assert.IsTrue(old.End(EventSubscriptionBase.EndReasonStopped), "A Stop elsewhere records the end first.");
			Assert.That(hook.InProgress, Is.False, "The ended run reads as over while it is still listed.");
			_ = hook.Start();
			Assert.IsTrue(hook.InProgress, "Start begins a new run.");
			Assert.That(hook.sub, Is.Not.SameAs(old));
		}
		finally
		{
			old.Unregister();
			_ = hook.Stop();
		}
	}

	/// <summary>Slot assignments restarting a WinEvent on one thread while another stops it leave it stopped with
	/// nothing listed: whichever ends the old run first decides, and no restart outlives the Stop.</summary>
	[Test, Category("Internal"), NonParallelizable]
	public void StopRacingSlotRestartsLeavesTheHookStopped()
	{
		var we = new Ks.WinEvent("ahk_class NoSuchWindowClassForThisTest")
		{
			OnExist = SlotCallback()
		};
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
			_ = SpinWait.SpinUntil(() => Volatile.Read(ref rounds) >= 10, TimeSpan.FromSeconds(10));
			_ = we.Stop();
			Assert.IsTrue(restarter.Join(TimeSpan.FromSeconds(30)), "The restarts finish.");

			Assert.That(we.InProgress, Is.False, "The hook stays stopped.");
			Assert.That(we.EndReason, Is.EqualTo("Stopped"));
			Assert.That(((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length, Is.Zero, "No run is left listed.");
			Assert.That(Script.TheScript.WinEventManager.IsKeepingScriptRunning, Is.False);
		}
		finally
		{
			_ = restarter.Join(TimeSpan.FromSeconds(30));
			_ = we.Stop();
		}
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
		Assert.That(we.InProgress, Is.False);
		Assert.That(we.EndReason, Is.EqualTo("Stopped"), "An idle hook reads Stopped, as an unstarted AHK InputHook does.");
		Assert.That(we.WinTitle, Is.EqualTo("ahk_class NoSuchWindowClassForThisTest"));

		var cb = new KeysharpFunc((Func<object, object, object, object>)((hook, hwnd, time) => ""));
		_ = Assert.Throws<KeysharpException>(() => we.OnMove = "not a function");
		_ = Assert.Throws<KeysharpException>(() => we.OnMove = new KeysharpFunc((Func<object, object, object, object, object>)((a, b, c, d) => "")));
		Assert.IsNotInstanceOf<KeysharpFunc>(we.OnMove, "A refused callback leaves the slot empty.");

		_ = we.Start();
		Assert.That(we.InProgress, Is.False, "Start() with no slot set does nothing, as such a run could never report.");

		we.OnExist = cb;
		Assert.That(we.OnExist, Is.SameAs(cb));
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
			Assert.That(we.sub, Is.SameAs(run), "A new callback for a set slot keeps the run.");
			Assert.That(((WinEventRegistration)we.sub).callbacks[(int)WindowEventType.Exist], Is.SameAs(other), "The running run calls the new callback.");

			we.OnNotExist = cb;
			Assert.IsTrue(we.InProgress, "Setting an empty slot while running restarts the hook.");
			Assert.That(we.sub, Is.Not.SameAs(run), "The restart is a fresh run.");
			Assert.That(we.sub.OwnerScheduler, Is.SameAs(owner), "The restart stays on the thread that started the hook.");
			Assert.That(run.IsActive, Is.False, "The old run ended, so its queued callbacks are discarded.");
			Assert.That(((Keysharp.Builtins.Array)Ks.WinEvent.staticget_Hooks(null)).Length, Is.EqualTo(1L), "One object is listed once.");

			we.OnExist = "";
			Assert.IsNotInstanceOf<KeysharpFunc>(we.OnExist, "An empty string clears a slot.");
			Assert.IsTrue(we.InProgress, "A slot is still set, so the hook runs on.");

			we.OnNotExist = "";
			Assert.That(we.InProgress, Is.False, "Clearing the last slot stops the hook.");
			Assert.That(we.EndReason, Is.EqualTo("Stopped"));
		}
		finally
		{
			_ = we.Stop();
		}

		Assert.That(we.EndReason, Is.EqualTo("Stopped"));
		Assert.That(Script.TheScript.WinEventManager.IsKeepingScriptRunning, Is.False);
	}

	private static void AssertState(Ks.WinEvent hook, bool inProgress, string endReason)
	{
		Assert.That(hook.InProgress, Is.EqualTo(inProgress));
		Assert.That(hook.EndReason, Is.EqualTo(endReason));
	}

	// The idle object is given the run's slot first, so a restart rebuilds the same description from it.
	private static Ks.WinEvent CreateWinEvent()
	{
		var reg = WinEventRun(WindowEventType.Active);
		var hook = new Ks.WinEvent
		{
			OnActive = reg.callbacks[(int)WindowEventType.Active],
			sub = reg
		};
		reg.scriptObject = hook;
		return hook;
	}

	private static KeysharpFunc SlotCallback() => new((Func<object, object, object, object>)((hook, hwnd, time) => ""));

	// A one-slot run built directly, so no native window hook is installed until a test registers it.
	private static WinEventRegistration WinEventRun(WindowEventType slot)
	{
		var script = Script.TheScript;
		var callbacks = new object[WinEventManager.typeCount];
		callbacks[(int)slot] = SlotCallback();
		return new WinEventRegistration(null, WindowSearchSettings.Current, callbacks,
			script.EventScheduler, script.WinEventManager);
	}

	private sealed class ForegroundBackend : IWindowEventBackend
	{
		internal Action Starting;
		internal Action<WindowEventMask> Stopping;

		public Action<WindowEventRaw> Sink { get; set; }

		public void Dispose()
		{ }

		public void Start(WindowEventMask mask) => Starting?.Invoke();

		public void Stop(WindowEventMask mask) => Stopping?.Invoke(mask);
	}

	// A family whose native install throws while any run is listed; its sync afterwards, with none, does not.
	private sealed class InstallFailingManager(Script script, Action refresh = null) : EventManagerBase<InstallFailingManager.Run, IDisposable, ValueTuple>(script, true)
	{
		internal int syncs;
		internal void RequestRefresh() => QueueRefresh();
		protected override void ProcessRefresh() => refresh?.Invoke();

		protected override ThreadKind CallbackThreadKind => ThreadKind.Event;
		protected override bool IsRunning(Run run) => run.IsRunning;
		protected override void EndRegistration(Run run, string reason)
		{
			_ = run.End(reason);
			run.Clear();
		}

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
			internal override void Register() => manager.Register(this);

			internal override void Unregister() => manager.Unregister(this);
		}
	}
}
