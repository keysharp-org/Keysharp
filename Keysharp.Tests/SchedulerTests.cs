using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
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
			Assert.AreEqual(0, hs.suspended);

			_ = s.FlowData.timers.Upsert(new KeysharpFunc((Func<object>)(() => 0L)), s.EventScheduler, 1000L, false, 0L);
			Assert.IsFalse(s.FlowData.timers.IsEmpty);

			s.Dispose();

			//Every kind of registration the retired script owned is now inert, and it stayed published so late
			//callers resolve a script whose guards answer honestly rather than a null.
			Assert.IsFalse(clipReg.IsActive, "a clipboard registration must not survive its script");
			Assert.AreNotEqual(0, hs.suspended & HotstringDefinition.HS_TURNED_OFF, "hotstrings must be disabled on exit");
			Assert.IsTrue(s.FlowData.timers.IsEmpty, "timers must be removed on exit");
			Assert.IsTrue(s.IsDisposed);
			Assert.AreSame(s, Script.TheScript);

			var replacement = new Script();
			s = replacement;//Hand ownership to TearDown.
			hsm = replacement.HotstringManager;
			Assert.AreSame(replacement, Script.TheScript);
		}

		/// <summary>
		/// The hook mutex name (`#App { HookMutexName: ... }`, or the constructor argument used here) is per-Script;
		/// a replacement script must retain its own default.
		/// </summary>
		[Test, Category("Threading")]
		public void HookMutexIsolation()
		{
			s.Dispose();//Otherwise it stays in KeysharpInputdManager.owners and blocks every later DisconnectClients.

			using (var named = new Script(typeof(SchedulerTests), "CustomHookMutex"))
			{
				Assert.AreEqual("CustomHookMutex Keybd", named.HookThread.KeybdMutexName);
				Assert.AreEqual("CustomHookMutex Mouse", named.HookThread.MouseMutexName);
			}

			var replacement = new Script();
			s = replacement;//Hand ownership to TearDown.
			hsm = replacement.HotstringManager;
			Assert.AreEqual("Keysharp Keybd", replacement.HookThread.KeybdMutexName);
			Assert.AreEqual("Keysharp Mouse", replacement.HookThread.MouseMutexName);
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
				Assert.AreSame(s, ownerField.GetValue(null), "another script's teardown must not stop this one's playback");

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
			Assert.AreSame(replacement, Script.TheScript);

			s.Dispose();

			Assert.AreSame(replacement, Script.TheScript);
		}

		[Test, Category("Threading")]
		public void UiSchedulerRegistrationsAreRemovedOnDispose()
		{
			var callback = new KeysharpFunc((Func<object>)(() => 0L));
			Assert.IsTrue(s.ClipFunctions.ModifyEventHandlers(callback, 1L));
			Assert.AreEqual(1, s.ClipFunctions.Count);

			s.Dispose();

			Assert.AreEqual(0, s.ClipFunctions.Count);
		}

		[Test, Category("Threading")]
		public void SchedulerCleanupRejectsLateCallbackRegistration()
		{
			var callback = new KeysharpFunc((Func<object>)(() => 0L));
			s.EventScheduler.ShutdownForScriptDispose();

			Assert.IsFalse(s.ClipFunctions.ModifyEventHandlers(callback, 1L));
			Assert.AreEqual(0, s.ClipFunctions.Count);
		}

		[Test, Category("Threading")]
		public void DisposedSynchronizationContextRejectsCallbacks()
		{
			var context = s.EventScheduler.DispatchContext;
			var called = false;
			s.Dispose();

			context.Post(_ => called = true, null);
			Assert.Throws<ObjectDisposedException>(() => context.Send(_ => called = true, null));
			Assert.IsFalse(called);
		}

		[Test, Category("Threading")]
		public void QueuedDestructorDoesNotRunScriptCodeAfterDispose()
		{
			var context = UseQueuedMainContext();
			var probe = new DestructorProbe();
			s.DestructorPump.Enqueue(probe);
			Assert.AreEqual(1, context.PendingCount);

			s.Dispose();
			context.DrainAll();

			Assert.AreEqual(0, probe.Deletes);
			Assert.AreEqual(1, probe.Disposes);
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
			Assert.AreSame(scheduler, Ks.KeysharpTask.GetUnobservedScheduler(ownedTask));
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
				Assert.IsFalse(ran);
				Assert.AreEqual(1, context.PendingCount);
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
			Assert.AreEqual(1, context.PendingCount);

			s.Dispose();
			Assert.DoesNotThrow(context.DrainAll);
			Assert.IsFalse(ran);
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

			Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() =>
				scheduler.TryExecuteThreadLaunch(0, false, false, threadVariables =>
				{
					Assert.IsTrue(scheduler.EnqueueCallback(() => _ = Keysharp.Builtins.Flow.Exit(7), ScriptEventQueue.Normal, false));
					Assert.DoesNotThrow(context.DrainAll);
					Keysharp.Internals.Flow.TryDoEvents(scheduler, propagateExit: true, yieldTick: false, pumpUi: false);
				}));

			Assert.AreEqual(7, Environment.ExitCode);
		}

		[Test, Category("Threading")]
		public void SequenceWrap()
		{
			s.pseudoThreadSequence = 0x0000FFFFFFFFFFFE;
			long first = 0L;
			long second = 0L;

			_ = s.EventScheduler.TryExecuteThreadLaunch(0, false, false, tv => first = tv.pseudoThreadId);
			_ = s.EventScheduler.TryExecuteThreadLaunch(0, false, false, tv => second = tv.pseudoThreadId);

			Assert.AreEqual(unchecked((long)0xFFFFFFFFFFFF0000UL), first);
			Assert.AreEqual(0x0000000000010000L, second);
		}

		[Test, Category("Threading")]
		public void InteractiveNested()
		{
			var context = UseQueuedMainContext();
			var scheduler = s.EventScheduler;
			var order = new List<string>();

			scheduler.EnqueueCallback(() =>
			{
				order.Add("H1");
				scheduler.EnqueueCallback(() => order.Add("H3"), ScriptEventQueue.Interactive, false);
				scheduler.EnqueueCallback(() => order.Add("H4"), ScriptEventQueue.Interactive, false);
				scheduler.EnqueueCallback(() => order.Add("N3"), ScriptEventQueue.Normal, false);
				scheduler.EnqueueCallback(() => order.Add("N4"), ScriptEventQueue.Normal, false);
			}, ScriptEventQueue.Interactive, false);
			scheduler.EnqueueCallback(() => order.Add("H2"), ScriptEventQueue.Interactive, false);
			scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);
			scheduler.EnqueueCallback(() => order.Add("N2"), ScriptEventQueue.Normal, false);

			Assert.AreEqual(1, context.PendingCount);

			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[]
			{
				"H1", "H2", "H3", "H4",
				"N1", "N2", "N3", "N4"
			}));
		}

		[Test, Category("Threading")]
		public void BlockedInteractive()
		{
			var context = UseQueuedMainContext();
			var scheduler = s.EventScheduler;
			var order = new List<string>();
			var interactiveBlocked = true;

			scheduler.Enqueue(ScriptEventQueue.Interactive, 0, () =>
			{
				if (interactiveBlocked)
					return ScriptEventExecutionResult.GlobalBlocked;

				order.Add("H1");
				return ScriptEventExecutionResult.Executed;
			});
			scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);

			context.DrainAll();

			// A refused launch parks and holds its own class, but dispatch work behind it still runs: the
			// conditions which refuse a launch do not gate message dispatch.
			Assert.That(order, Is.EqualTo(new[] { "N1" }));
			Assert.AreEqual(0, context.PendingCount);

			interactiveBlocked = false;
			scheduler.SchedulePump();
			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "N1", "H1" }));
		}

		[Test, Category("Threading")]
		public void BlockedLaunchDoesNotStarveDispatch()
		{
			var context = UseQueuedMainContext();
			var scheduler = s.EventScheduler;
			var order = new List<string>();
			var launchBlocked = true;

			scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
			{
				if (launchBlocked)
					return ScriptEventExecutionResult.GlobalBlocked;

				order.Add("launch");
				return ScriptEventExecutionResult.Executed;
			});
			scheduler.EnqueueCallback(() => order.Add("dispatch"), ScriptEventQueue.Normal, false);

			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "dispatch" }));
			Assert.IsTrue(scheduler.HasBlockedQueuedWork);

			launchBlocked = false;
			scheduler.SchedulePump();
			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "dispatch", "launch" }));
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
			scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
			{
				attempts++;
				return ScriptEventExecutionResult.GlobalBlocked;
			}, launchesThread: false);
			scheduler.EnqueueCallback(() => order.Add("N1"), ScriptEventQueue.Normal, false);

			context.DrainAll();

			Assert.AreEqual(1, attempts);
			Assert.That(order, Is.EqualTo(new[] { "N1" }));
			Assert.IsTrue(scheduler.HasBlockedQueuedWork);
		}

		[Test, Category("Threading")]
		public void BlockedNormalRetry()
		{
			var context = UseQueuedMainContext();
			var scheduler = s.EventScheduler;
			var order = new List<string>();
			var normalBlocked = true;

			scheduler.Enqueue(ScriptEventQueue.Normal, 0, () =>
			{
				if (normalBlocked)
					return ScriptEventExecutionResult.GlobalBlocked;

				order.Add("N1");
				return ScriptEventExecutionResult.Executed;
			});

			context.DrainAll();

			Assert.IsEmpty(order);

			scheduler.EnqueueCallback(() => order.Add("H1"), ScriptEventQueue.Interactive, false);
			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "H1" }));

			normalBlocked = false;
			scheduler.SchedulePump();
			context.DrainAll();

			Assert.That(order, Is.EqualTo(new[] { "H1", "N1" }));
		}
	}
}
