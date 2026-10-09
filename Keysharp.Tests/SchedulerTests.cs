namespace Keysharp.Tests;

[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
public class SchedulerTests : TestRunner
{
	private sealed class DestructorProbe : Any, IDisposable
	{
		internal int Deletes;
		internal int Disposes;

		internal DestructorProbe() : base(null) { }

		public override object __Delete()
		{
			Deletes++;
			return 0L;
		}

		void IDisposable.Dispose() => Disposes++;
	}

	/// <summary>
	/// The one-engine-per-process contract, from the RETIRED script's side: what it registered must be
	/// revoked by its own Dispose, so nothing of it can still fire once a replacement is running. Asserting
	/// the replacement's own fields are empty would prove nothing — they are instance state on a new object.
	/// </summary>
	[Test, Category("Threading")]
	public void DisposedScriptRevokesItsOwnRegistrations()
	{
		var clipReg = new Keysharp.Internals.Scripting.CallbackRegistration(
			new KeysharpFunc((Func<object>)(() => 0L)), s.EventScheduler, true);
		Assert.IsTrue(s.ClipFunctions.Add(clipReg));
		Assert.IsTrue(clipReg.IsActive);

		var hs = (HotstringDefinition)s.HotstringManager.AddHotstring("::d1test", null, "", "d1test", "leak", false);
		Assert.That(hs.suspended, Is.EqualTo(0));

		_ = s.FlowData.timers.Upsert(new KeysharpFunc((Func<object>)(() => 0L)), s.EventScheduler, 1000L, false, 0L);
		Assert.That(s.FlowData.timers.IsEmpty, Is.False);

		s.Dispose();

		//Every kind of registration the retired script owned is now inert, and it stayed published so late
		//callers resolve a script whose guards answer honestly rather than a null.
		Assert.That(clipReg.IsActive, Is.False, "a clipboard registration must not survive its script");
		Assert.That(hs.suspended & HotstringDefinition.HS_TURNED_OFF, Is.Not.EqualTo(0), "hotstrings must be disabled on exit");
		Assert.IsTrue(s.FlowData.timers.IsEmpty, "timers must be removed on exit");
		Assert.IsTrue(s.IsDisposed);
		Assert.That(Script.TheScript, Is.SameAs(s));

		var replacement = new Script();
		s = replacement;//Hand ownership to TearDown.
		hsm = replacement.HotstringManager;
		Assert.That(Script.TheScript, Is.SameAs(replacement));
	}

	/// <summary>
	/// The hook mutex name (`#App { HookMutexName: ... }`, or the constructor argument used here) is per-Script;
	/// a replacement script must retain its own default.
	/// </summary>
	[Test, Category("Threading")]
	public void HookMutexIsolation()
	{
		var defaultKeyboardMutex = s.HookThread.KeybdMutexName;
		var defaultMouseMutex = s.HookThread.MouseMutexName;
		s.Dispose();

		using (var named = new Script(typeof(SchedulerTests), "CustomHookMutex"))
		{
			Assert.That(named.HookThread.KeybdMutexName, Is.EqualTo("CustomHookMutex Keybd"));
			Assert.That(named.HookThread.MouseMutexName, Is.EqualTo("CustomHookMutex Mouse"));
		}

		var replacement = new Script();
		s = replacement;//Hand ownership to TearDown.
		hsm = replacement.HotstringManager;
		Assert.That(replacement.HookThread.KeybdMutexName, Is.EqualTo(defaultKeyboardMutex));
		Assert.That(replacement.HookThread.MouseMutexName, Is.EqualTo(defaultMouseMutex));
	}

	/// <summary>
	/// Playback is one process-global resource (a single MCI alias / one child player), so it is owner-keyed
	/// rather than per-Script: a script's teardown must stop only what that script started. Driven through the
	/// private state because starting real playback in a test would need an audio device.
	/// </summary>
	[Test, Category("Threading")]
	public void SoundPlaybackStopsOnlyItsOwnOwner()
	{
		var type = typeof(Keysharp.Internals.Os.SoundPlayback);
		const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
		var ownerField = type.GetField("currentOwner", Flags);
#if WINDOWS
		//Windows keys off a separate "something is open" flag, which StopCurrent checks before the owner.
		var activeField = type.GetField("soundWasPlayed", Flags);
		activeField.SetValue(null, true);
#endif
		var other = (Script)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Script));

		try
		{
			ownerField.SetValue(null, s);

			Keysharp.Internals.Os.SoundPlayback.StopCurrent(other);
			Assert.That(ownerField.GetValue(null), Is.SameAs(s), "another script's teardown must not stop this one's playback");

			Keysharp.Internals.Os.SoundPlayback.StopCurrent(s);
			Assert.IsNull(ownerField.GetValue(null), "the owning script's teardown must stop its own playback");
		}
		finally
		{
			ownerField.SetValue(null, null);
#if WINDOWS
			activeField.SetValue(null, false);
#endif
			GC.SuppressFinalize(other);
		}
	}

	[Test, Category("Threading")]
	public void DisposingOlderScriptDoesNotClearNewPublication()
	{
		using var replacement = new Script();
		Assert.That(Script.TheScript, Is.SameAs(replacement));

		s.Dispose();

		Assert.That(Script.TheScript, Is.SameAs(replacement));
	}

	[Test, Category("Threading")]
	public void UiSchedulerRegistrationsAreRemovedOnDispose()
	{
		var callback = new KeysharpFunc((Func<object>)(() => 0L));
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(callback, 1L));
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(1));

		s.Dispose();

		Assert.That(s.ClipFunctions.Count, Is.EqualTo(0));
	}

	/// <summary>A callback its owner already registered is not added again and keeps its place, as AHK's OnScriptEvent
	/// finds it first. Any other object is found by reference, never by an Equals it overrides, and each closure is a
	/// callback of its own, as in AHK, even two made from one function body.</summary>
	[Test, Category("Threading")]
	public void RegisteringACallbackTwiceKeepsOneRegistration()
	{
		var callback = new KeysharpFunc((Func<object, object>)(_ => 0L));
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(callback, 1L));
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(callback, -1L));
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(1));

		var first = new EqualsEverything();
		var second = new EqualsEverything();
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(first, 1L));
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(second, 1L));
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(3), "Two objects are two registrations, whatever their Equals says.");

		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(first, -1L));
		var order = s.ClipFunctions.GetSnapshot();
		Assert.That(order.Length, Is.EqualTo(3));
		Assert.That(order[0].Callback, Is.SameAs(callback));
		Assert.That(order[1].Callback, Is.SameAs(first), "A callback added again keeps its place.");
		Assert.That(order[2].Callback, Is.SameAs(second));

		Func<object, object> body = _ => 0L;
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(Functions.Closure(body), 1L));
		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(Functions.Closure(body), 1L));
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(5), "Each closure is a registration of its own.");

		Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(first, 0L));
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(4), "Removal finds the object it was given.");
	}

	private sealed class EqualsEverything : KeysharpObject
	{
		public override bool Equals(object obj) => true;
		public override int GetHashCode() => 0;
	}

	[Test, Category("Threading")]
	public void SchedulerCleanupRejectsLateCallbackRegistration()
	{
		var callback = new KeysharpFunc((Func<object>)(() => 0L));
		s.EventScheduler.ShutdownForScriptDispose();

		Assert.That(s.ClipFunctions.ModifyEventHandlers(callback, 1L), Is.False);
		Assert.That(s.ClipFunctions.Count, Is.EqualTo(0));
	}

	[Test, Category("Threading")]
	public void DisposedSynchronizationContextRejectsCallbacks()
	{
		var context = s.EventScheduler.DispatchContext;
		var called = false;
		s.Dispose();

		context.Post(_ => called = true, null);
		_ = Assert.Throws<ObjectDisposedException>(() => context.Send(_ => called = true, null));
		Assert.That(called, Is.False);
	}

	[Test, Category("Threading")]
	public void QueuedDestructorDoesNotRunScriptCodeAfterDispose()
	{
		var context = UseQueuedMainContext();
		var probe = new DestructorProbe();
		s.DestructorPump.Enqueue(probe);
		Assert.That(context.PendingCount, Is.EqualTo(1));

		s.Dispose();
		context.DrainAll();

		Assert.That(probe.Deletes, Is.EqualTo(0));
		Assert.That(probe.Disposes, Is.EqualTo(1));
	}

	[Test, Category("Threading")]
	public void TaskContinuationKeepsScriptPersistent()
		=> Assert.IsTrue(TestScript("task-continuation-persistence", false));

	[Test, Category("Threading")]
	public void UnobservedFaultLookupRejectsRetiredScriptOwner()
	{
		var ownedTask = Task.FromResult(Guid.NewGuid());
		var hostTask = Task.FromResult(Guid.NewGuid());
		var wrapper = Ks.KeysharpTask.Wrap(ownedTask);
		var scheduler = s.EventScheduler;
		Assert.That(Ks.KeysharpTask.GetUnobservedScheduler(ownedTask), Is.SameAs(scheduler));
		Assert.IsNull(Ks.KeysharpTask.GetUnobservedScheduler(hostTask));
		var incomplete = (Script)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Script));
		Script.TheScript = incomplete;

		try
		{
			Assert.IsNull(Ks.KeysharpTask.GetUnobservedScheduler(ownedTask));
		}
		finally
		{
			Script.TheScript = s;
			GC.SuppressFinalize(incomplete);
			GC.KeepAlive(wrapper);
		}
	}

	[Test, Category("Threading")]
	public void ExplicitAndPostedPumpsUseSchedulerOwner()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var incomplete = (Script)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Script));
		var ran = false;
		Script.TheScript = incomplete;

		try
		{
			Assert.DoesNotThrow(() => Keysharp.Internals.Flow.TryDoEvents(scheduler, false, false, false));
			Assert.IsTrue(scheduler.EnqueueCallback(() => ran = true));
			Assert.That(ran, Is.False);
			Assert.That(context.PendingCount, Is.EqualTo(1));
			Assert.DoesNotThrow(context.DrainAll);
			Assert.IsTrue(ran);
		}
		finally
		{
			Script.TheScript = s;
			GC.SuppressFinalize(incomplete);
		}
	}

	[Test, Category("Threading")]
	public void DisposedOwnerDoesNotDrainPostedPump()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var ran = false;

		Assert.IsTrue(scheduler.EnqueueCallback(() => ran = true));
		Assert.That(context.PendingCount, Is.EqualTo(1));

		s.Dispose();
		Assert.DoesNotThrow(context.DrainAll);
		Assert.That(ran, Is.False);
	}

	[Test, Category("Threading")]
	public void PostedExitSuppression()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		s.hasExited = true;

		Assert.IsTrue(scheduler.EnqueueCallback(() => Assert.Fail("Exited script callback ran."), ScriptEventQueue.Normal, false));
		Assert.DoesNotThrow(context.DrainAll);
	}

	[Test, Category("Threading")]
	public void PostedExitPropagation()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		_ = Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() =>
			scheduler.TryExecuteThreadLaunch(0, false, false, threadVariables =>
			{
				Assert.IsTrue(scheduler.EnqueueCallback(() => _ = Keysharp.Builtins.Flow.Exit(7), ScriptEventQueue.Normal, false));
				Assert.DoesNotThrow(context.DrainAll);
				Keysharp.Internals.Flow.TryDoEvents(scheduler, propagateExit: true, yieldTick: false, pumpUi: false);
			}));
	}

	[Test, Category("Threading")]
	public void SequenceWrap()
	{
		s.pseudoThreadSequence = 0x0000FFFFFFFFFFFE;
		var first = 0L;
		var second = 0L;

		_ = s.EventScheduler.TryExecuteThreadLaunch(0, false, false, tv => first = tv.pseudoThreadId);
		_ = s.EventScheduler.TryExecuteThreadLaunch(0, false, false, tv => second = tv.pseudoThreadId);

		Assert.That(first, Is.EqualTo(unchecked((long)0xFFFFFFFFFFFF0000UL)));
		Assert.That(second, Is.EqualTo(0x0000000000010000L));
	}

	[Test, Category("Threading")]
	public void InteractiveNested()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();

		_ = scheduler.EnqueueCallback(() =>
		{
			order.Add("H1");
			_ = scheduler.EnqueueCallback(() => order.Add("H3"), ScriptEventQueue.Interactive, false);
			_ = scheduler.EnqueueCallback(() => order.Add("H4"), ScriptEventQueue.Interactive, false);
			_ = scheduler.EnqueueCallback(() => order.Add("N3"), ScriptEventQueue.Normal, false);
			_ = scheduler.EnqueueCallback(() => order.Add("N4"), ScriptEventQueue.Normal, false);
		}, ScriptEventQueue.Interactive, false);
		_ = scheduler.EnqueueCallback(() => order.Add("H2"), ScriptEventQueue.Interactive, false);
		_ = scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);
		_ = scheduler.EnqueueCallback(() => order.Add("N2"), ScriptEventQueue.Normal, false);

		Assert.That(context.PendingCount, Is.EqualTo(1));

		context.DrainAll();

		Assert.That(order, Is.EqualTo(
		[
			"H1", "H2", "H3", "H4",
			"N1", "N2", "N3", "N4"
		]));
	}

	[Test, Category("Threading")]
	public void BlockedInteractive()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();
		var interactiveBlocked = true;

		_ = scheduler.Enqueue(ScriptEventQueue.Interactive, 0, () =>
		{
			if (interactiveBlocked)
				return ScriptEventExecutionResult.GlobalBlocked;

			order.Add("H1");
			return ScriptEventExecutionResult.Executed;
		});
		_ = scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);

		context.DrainAll();

		// A refused launch parks and holds its own class, but dispatch work behind it still runs: the
		// conditions which refuse a launch do not gate message dispatch.
		Assert.That(order, Is.EqualTo(["N1"]));
		Assert.That(context.PendingCount, Is.EqualTo(0));

		interactiveBlocked = false;
		scheduler.SchedulePump();
		context.DrainAll();

		Assert.That(order, Is.EqualTo(["N1", "H1"]));
	}

	[TestCase(false), Category("Threading")]
	[TestCase(true)]
	public void LocalBlocksBothQueues(bool includeDispatch)
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();
		var blocked = true;
		var attempts = 0;

		ScriptEventExecutionResult TryEntry(string name)
		{
			if (blocked)
			{
				Assert.Less(++attempts, 16, "A pass must stop after both queues are locally blocked.");
				return ScriptEventExecutionResult.LocalBlocked;
			}

			order.Add(name);
			return ScriptEventExecutionResult.Executed;
		}

		_ = scheduler.Enqueue(ScriptEventQueue.Interactive, 0, () => TryEntry("hotkey"));
		_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => TryEntry("timer1"));
		_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () => TryEntry("timer2"));

		if (includeDispatch)
			_ = scheduler.EnqueueCallback(() => order.Add("dispatch"), ScriptEventQueue.Normal, false);

		// Pump directly so an assertion inside an entry escapes the UI exception boundary.
		scheduler.PumpThreadQueuedEventsCore();
		Assert.That(order, Is.EqualTo(includeDispatch ? ["dispatch"] : System.Array.Empty<string>()));
		Assert.IsTrue(scheduler.HasBlockedQueuedWork);

		blocked = false;
		scheduler.SchedulePump();
		context.DrainAll();

		Assert.That(order, Is.EqualTo(includeDispatch
			? new[] { "dispatch", "hotkey", "timer1", "timer2" }
			: ["hotkey", "timer1", "timer2"]));
		Assert.That(scheduler.HasBlockedQueuedWork, Is.False);
		Assert.That(context.PendingCount, Is.EqualTo(0));
	}

	[Test, Category("Threading")]
	public void BlockedLaunchDoesNotStarveDispatch()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();
		var launchBlocked = true;

		_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
		{
			if (launchBlocked)
				return ScriptEventExecutionResult.GlobalBlocked;

			order.Add("launch");
			return ScriptEventExecutionResult.Executed;
		});
		_ = scheduler.EnqueueCallback(() => order.Add("dispatch"), ScriptEventQueue.Normal, false);

		context.DrainAll();

		Assert.That(order, Is.EqualTo(["dispatch"]));
		Assert.IsTrue(scheduler.HasBlockedQueuedWork);

		launchBlocked = false;
		scheduler.SchedulePump();
		context.DrainAll();

		Assert.That(order, Is.EqualTo(["dispatch", "launch"]));
	}

	[Test, Category("Threading")]
	public void MislabeledDispatchBlockIsParked()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();
		var attempts = 0;

		// A producer bug: work labelled dispatch which reports a launch block. Parking it re-labelled is what
		// stops the skip-walk from refetching and re-running it for the rest of the pass.
		_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
		{
			attempts++;
			return ScriptEventExecutionResult.GlobalBlocked;
		}, launchesThread: false);
		_ = scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);

		context.DrainAll();

		Assert.That(attempts, Is.EqualTo(1));
		Assert.That(order, Is.EqualTo(["N1"]));
		Assert.IsTrue(scheduler.HasBlockedQueuedWork);
	}

	[Test, Category("Threading")]
	public void BlockedNormalRetry()
	{
		var context = UseQueuedMainContext();
		var scheduler = s.EventScheduler;
		var order = new List<string>();
		var normalBlocked = true;

		_ = scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
		{
			if (normalBlocked)
				return ScriptEventExecutionResult.GlobalBlocked;

			order.Add("N1");
			return ScriptEventExecutionResult.Executed;
		});

		context.DrainAll();

		Assert.IsEmpty(order);

		_ = scheduler.EnqueueCallback(() => order.Add("H1"), ScriptEventQueue.Interactive, false);
		context.DrainAll();

		Assert.That(order, Is.EqualTo(["H1"]));

		normalBlocked = false;
		scheduler.SchedulePump();
		context.DrainAll();

		Assert.That(order, Is.EqualTo(["H1", "N1"]));
	}
}
