using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Reflection;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Invoke;
using Keysharp.Internals.Threading;
using Keysharp.Internals.Window;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class RealThreadTests : TestRunner
	{
		private static int hookWinCriterionCalls;
		private static bool hookWinCriterionResult;

		// A named callable that runs an arbitrary delegate. Derives from KeysharpFunc (rather than implementing
		// an interface) and overrides every member used here, so none of the base implementation's reflection
		// state is needed. These must be overrides, not new members: callers hold it as a KeysharpFunc.
		private sealed class NamedCriterion(string name, Func<object> callback) : KeysharpFunc
		{
			public override bool IsBuiltIn => false;
			internal override bool IsValid => true;
			public override string Name => name;
			public override KeysharpFunc Bind(params object[] obj) => this;
			public override object Call(params object[] obj) => callback();
			public override object CallInst(object inst, params object[] obj) => callback();
			public override bool IsByRef(object obj = null) => false;
			public override bool IsOptional(object obj = null) => false;
		}

		private sealed class CallbackProbe
		{
			private readonly ConcurrentDictionary<string, ManualResetEventSlim> events = new();
			internal readonly ConcurrentDictionary<string, bool> HasSchedulerContext = new();
			internal readonly ConcurrentDictionary<string, int> ThreadIds = new();

			internal object Record(string name)
			{
				ThreadIds[name] = Environment.CurrentManagedThreadId;
				HasSchedulerContext[name] = SynchronizationContext.Current is ScriptEventSynchronizationContext;
				events.GetOrAdd(name, _ => new ManualResetEventSlim()).Set();
				return 0L;
			}

			internal bool WaitFor(string name, int timeout = 2000)
				=> WaitWithUiPump(() => events.GetOrAdd(name, _ => new ManualResetEventSlim()).IsSet, timeout);
		}

		private sealed class WorkerRegistrations
		{
			internal DelegateHolder CallbackHolder;
			internal KeysharpForm Form;
			internal Gui Gui;
			internal HotkeyDefinition Hotkey;
			internal HotkeyBinding HotkeyBinding;
			internal HotkeyVariant HotkeyVariant;
			internal HotstringDefinition Hotstring;
			internal int MessageId;
			internal int WorkerThreadId;
			internal bool WorkerHasSchedulerContext;
			internal KeysharpFunc TimerFunc;
			internal ScriptEventScheduler TimerScheduler;
			internal ScriptTimerState Timer;
		}

		private static Ks.RealThread StartWorker(Action body)
			=> (Ks.RealThread)Ks.RealThread.staticCall(null, new KeysharpFunc((Func<object>)(() =>
			{
				body();
				return 0L;
			})));

		private static void EnsureUiScheduler()
		{
			SkipIfUiInitializationBlocked("Test requires a live Eto Application (macOS testhost cannot drive AppKit).");
			_ = Script.TheScript.EventScheduler;
#if !WINDOWS
			_ = Eto.Forms.Application.Instance ?? new Eto.Forms.Application();
#endif
		}

		private void WithMatchingWorkerHotkey(HotkeyDefinition hotkey, KeysharpFunc workerCallback, Action<Ks.RealThread> assertions, string options = null)
		{
			var registered = new ManualResetEventSlim(false);
			Exception workerSetupException = null;
			Ks.RealThread worker = null;

			try
			{
				worker = StartWorker(() =>
				{
					try
					{
						_ = options == null ? Builtins.Keyboard.Hotkey(hotkey.Name, workerCallback) : Builtins.Keyboard.Hotkey(hotkey.Name, workerCallback, options);
					}
					catch (Exception ex)
					{
						workerSetupException = ex;
						throw;
					}
					finally
					{
						registered.Set();
					}
				});
				Assert.IsTrue(WaitWithUiPump(() => registered.IsSet), "Worker did not register the matching hotkey variant.");
				Assert.That(workerSetupException, Is.Null, workerSetupException?.ToString());
				assertions(worker);
			}
			finally
			{
				ShutdownWorker(s, worker);
			}
		}

		private static Message CreateMessage(int msgId)
		{
#if WINDOWS
			return Message.Create(IntPtr.Zero, msgId, IntPtr.Zero, IntPtr.Zero);
#else
			return new Message
			{
				HWnd = 0,
				Msg = msgId,
				WParam = 0,
				LParam = 0,
				Result = 0
			};
#endif
		}

		private static Error AssertScriptError(TestDelegate action) => Assert.Throws<KeysharpException>(action).UserError;

		private static bool WaitWithUiPump(Func<bool> predicate, int timeout = 2000)
		{
			var deadline = Environment.TickCount64 + timeout;
			var script = Script.TheScript;

			while (!predicate())
			{
				if (Environment.TickCount64 >= deadline)
					return false;

				try
				{
#if WINDOWS
					Application.DoEvents();
#else
					Eto.Forms.Application.Instance?.RunIteration();
#endif
					Keysharp.Internals.Flow.TryDoEvents(script.EventScheduler, propagateExit: true, yieldTick: false, pumpUi: false);
				}
				catch
				{
				}

				Thread.Sleep(1);
			}

			return true;
		}

		private static void AssertEventually(Func<bool> predicate, string message, int timeout = 2000)
			=> Assert.IsTrue(WaitWithUiPump(predicate, timeout), message);

		private static void ShutdownWorker(Script script, Ks.RealThread worker)
		{
			if (script != null)
			{
				script.hasExited = true;
				script.ScheduleAllEventSchedulers();
			}

			if (worker == null)
				return;

			_ = SpinWait.SpinUntil(() => !worker.IsActive, 2000);
		}

		[Test, Category("Threading"), Category("UI")]
		public void WorkerOwnership()
		{
			EnsureUiScheduler();

			var probe = new CallbackProbe();
			var registrations = new WorkerRegistrations();
			var registered = new ManualResetEventSlim(false);
			Exception workerSetupException = null;
			var form = (KeysharpForm)RuntimeHelpers.GetUninitializedObject(typeof(KeysharpForm));
			var gui = (Gui)RuntimeHelpers.GetUninitializedObject(typeof(Gui));
			Ks.RealThread worker = null;

			try
			{
				form.closedHandlers = new();
				gui.form = form;
				s.GuiData.allGuiHwnds[1] = gui;
				registrations.Form = form;
				registrations.Gui = gui;

				worker = StartWorker(() =>
				{
					try
					{
						registrations.WorkerThreadId = Environment.CurrentManagedThreadId;
						registrations.WorkerHasSchedulerContext = SynchronizationContext.Current is ScriptEventSynchronizationContext;

						registrations.TimerFunc = new KeysharpFunc((Func<object>)(() => probe.Record("timer")));
						_ = Keysharp.Builtins.Flow.SetTimer(registrations.TimerFunc, 250L);
						registrations.TimerScheduler = s.EventScheduler;
						registrations.Timer = s.FlowData.timers.Find(registrations.TimerFunc, registrations.TimerScheduler);

						registrations.Hotkey = new HotkeyDefinition(s, 1, new KeysharpFunc((Func<object, object>)(_ => probe.Record("hotkey"))), 0, "F24", 0);
						s.HotkeyData.shk = [..s.HotkeyData.shk, registrations.Hotkey];
						registrations.HotkeyVariant = registrations.Hotkey.firstVariant;
						registrations.HotkeyBinding = registrations.HotkeyVariant.FindBinding(s.EventScheduler);

						registrations.Hotstring = new HotstringDefinition(s, "::abc", "")
						{
							Name = "abc",
							funcObj = new KeysharpFunc((Func<object, object>)(_ => probe.Record("hotstring"))),
							maxThreads = 1,
							priority = 0
						};
						s.HotstringManager.shs.Add(registrations.Hotstring);

						registrations.MessageId = 0x8017;
						_ = Keysharp.Builtins.Flow.OnMessage(registrations.MessageId, new KeysharpFunc((Func<object, object, object, object, object>)((wParam, lParam, msg, hwnd) =>
						{
							_ = probe.Record("message");
							return 1L;
						})));

							form.closedHandlers ??= new();
						form.closedHandlers.ModifyEventHandlers(new KeysharpFunc((Func<object, object>)(_ => probe.Record("gui"))), 1);

						_ = Env.OnClipboardChange(new KeysharpFunc((Func<object, object>)(_ => probe.Record("clipboard"))));
						registrations.CallbackHolder = (DelegateHolder)Dll.CallbackCreate(new KeysharpFunc((Func<object>)(() => probe.Record("callbackcreate"))));
					}
					catch (Exception ex)
					{
						workerSetupException = ex;
						throw;
					}
					finally
					{
						registered.Set();
					}
				});

				Assert.IsTrue(WaitWithUiPump(() => registered.IsSet), "Worker did not finish registration setup.");
				if (workerSetupException != null)
					Assert.Fail(workerSetupException.ToString());
				Assert.IsTrue(worker.IsActive, "Worker should stay alive while it owns persistent registrations.");
				Assert.AreEqual(registrations.WorkerThreadId, worker.Id);
				Assert.IsTrue(registrations.WorkerHasSchedulerContext);

				Assert.IsTrue(registrations.Timer.OwnerScheduler.EnqueueTimer(registrations.Timer));
				Assert.IsTrue(probe.WaitFor("timer"));

				registrations.Hotkey.PerformInNewThreadMadeByCallerAsync(registrations.HotkeyVariant, 0, 0);
				Assert.IsTrue(probe.WaitFor("hotkey"));

				_ = registrations.Hotstring.PerformInNewThreadMadeByCaller(0, CaseConformModes.None, ' ', 0, false);
				Assert.IsTrue(probe.WaitFor("hotstring"));

				var filter = new MessageFilter(s);
				var msg = CreateMessage(registrations.MessageId);
				Assert.IsTrue(filter.CallEventHandlers(ref msg));
				Assert.IsTrue(probe.WaitFor("message"));

				_ = form.closedHandlers.InvokeEventHandlers("close");
				Assert.IsTrue(probe.WaitFor("gui"));

				_ = s.ClipFunctions.InvokeEventHandlers(1L);
				Assert.IsTrue(probe.WaitFor("clipboard"));

				Assert.AreEqual(0L, Dll.DllCall((long)registrations.CallbackHolder.Ptr));
				Assert.IsTrue(probe.WaitFor("callbackcreate"));

				_ = worker.Post(new KeysharpFunc((Func<object>)(() => probe.Record("post"))));
				Assert.IsTrue(probe.WaitFor("post"));

				foreach (var name in new[] { "timer", "hotkey", "hotstring", "message", "gui", "clipboard", "callbackcreate", "post" })
				{
					Assert.AreEqual(registrations.WorkerThreadId, probe.ThreadIds[name], $"{name} callback did not run on the owning worker.");
					Assert.IsTrue(probe.HasSchedulerContext[name], $"{name} callback did not observe the worker synchronization context.");
				}
			}
			finally
			{
				ShutdownWorker(s, worker);
			}

			AssertEventually(() => !worker.IsActive, "Worker should be fully stopped after shutdown.");
			AssertEventually(() => s.FlowData.timers.Find(registrations.TimerFunc, registrations.TimerScheduler) == null, "Worker-owned timer was not removed.");
			AssertEventually(() => registrations.HotkeyBinding.OwnerScheduler == null, "Worker-owned hotkey scheduler affinity was not cleared.");
			AssertEventually(() => !registrations.HotkeyBinding.IsActive, "Worker-owned hotkey binding was not disabled.");
			AssertEventually(() => registrations.Hotstring.ownerScheduler == null, "Worker-owned hotstring scheduler affinity was not cleared.");
			AssertEventually(() => (registrations.Hotstring.suspended & HotstringDefinition.HS_TURNED_OFF) != 0, "Worker-owned hotstring was not turned off.");
			AssertEventually(() => !s.GuiData.onMessageHandlers.ContainsKey(registrations.MessageId), "Worker-owned OnMessage registration was not removed.");
			AssertEventually(() => form.closedHandlers.Count == 0, "Worker-owned GUI handlers were not removed.");
			AssertEventually(() => s.ClipFunctions.Count == 0, "Worker-owned clipboard handlers were not removed.");
			AssertEventually(() => registrations.CallbackHolder.Ptr == 0L, "Worker-owned callback pointer was not freed.");
		}

		[Test, Category("Threading"), Category("UI")]
		public void SchedulerSend()
		{
			_ = s.EventScheduler;

			var ready = new ManualResetEventSlim(false);
			var uiRan = new ManualResetEventSlim(false);
			Ks.RealThread worker = null;

			try
			{
				worker = StartWorker(() =>
				{
					_ = Env.OnClipboardChange(new KeysharpFunc((Func<object, object>)(_ => 0L)));
					ready.Set();
				});

				Assert.IsTrue(WaitWithUiPump(() => ready.IsSet), "Worker did not become ready.");
				Assert.IsTrue(worker.IsActive, "Worker must still be alive before Send.");

				var result = worker.Send(new KeysharpFunc((Func<object>)(() =>
				{
					s.UIEventScheduler.EnqueueCallback(() => uiRan.Set(), ScriptEventQueue.Normal, false);
					Assert.IsTrue(uiRan.Wait(1000), "Worker never observed the callback queued back to the main scheduler.");
					return "ok";
				})));

				Assert.AreEqual("ok", result);
				Assert.IsTrue(uiRan.IsSet, "Main scheduler did not pump while waiting in Send.");
			}
			finally
			{
				ShutdownWorker(s, worker);
			}
		}

		// A KeysharpThread may be read from any real thread, but only its owner may terminate it: pseudo-thread stacks
		// are per real thread and are mutated without locking. This is the same restriction targeted Exit() always
		// had, now enforced by the object rather than by an error return.
		[Test, Category("Threading"), Category("UI")]
		public void ForeignThreadExit()
		{
			EnsureUiScheduler();
			var ready = new ManualResetEventSlim(false);
			KeysharpThread foreignThread = null;
			Ks.RealThread worker = null;

			try
			{
				worker = StartWorker(() =>
				{
					foreignThread = s.Threads.CurrentThreadObject;
					_ = Env.OnClipboardChange(new KeysharpFunc((Func<object, object>)(_ => 0L)));
					ready.Set();
				});

				Assert.IsTrue(WaitWithUiPump(() => ready.IsSet), "Worker did not expose its pseudo-thread.");
				_ = s.EventScheduler.TryExecuteThreadLaunch(0, false, false, threadVariables =>
				{
					//Reading it from here is fine; only the mutation is refused.
					Assert.IsTrue(foreignThread.IsActive);
					Assert.AreNotSame(foreignThread, s.Threads.CurrentThreadObject);
					var error = AssertScriptError(() => _ = foreignThread.Exit(1));
					Assert.That(error, Is.TypeOf<TargetError>());
				});
			}
			finally
			{
				ShutdownWorker(s, worker);
			}
		}

		[Test, Category("Threading"), Category("UI")]
		public void SendExitRequest()
		{
			EnsureUiScheduler();
			var ready = new ManualResetEventSlim(false);
			var exitRequested = new ManualResetEventSlim(false);
			Ks.RealThread worker = null;

			try
			{
				worker = StartWorker(() =>
				{
					_ = Env.OnClipboardChange(new KeysharpFunc((Func<object, object>)(_ => 0L)));
					ready.Set();
				});

				Assert.IsTrue(WaitWithUiPump(() => ready.IsSet), "Worker did not become ready.");
				Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() =>
					s.EventScheduler.TryExecuteThreadLaunch(0, false, false, tv =>
					{
						var targetThread = s.Threads.CurrentThreadObject;
						_ = worker.Send(new KeysharpFunc((Func<object>)(() =>
						{
							s.UIEventScheduler.EnqueueCallback(() =>
							{
								try { _ = targetThread.Exit(8); }
								finally { exitRequested.Set(); }
							}, ScriptEventQueue.Normal, false);
							Assert.IsTrue(exitRequested.Wait(1000), "Main scheduler did not process the exit request.");
							return 0L;
						})));
					}));

				Assert.AreEqual(8, Environment.ExitCode);
			}
			finally
			{
				ShutdownWorker(s, worker);
			}
		}

		[Test, Category("Threading"), Category("UI")]
		public void HotkeyDispatch()
		{
			var probe = new CallbackProbe();
			var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ => probe.Record("main"))), 0, "$a", 0);
			s.HotkeyData.shk = [..s.HotkeyData.shk, hk];

			WithMatchingWorkerHotkey(hk, new KeysharpFunc((Func<object, object>)(_ => probe.Record("worker"))), worker =>
			{
				Assert.AreEqual(1, s.HotkeyData.shk.Length, "Exact match should reuse the same hotkey definition.");
				Assert.AreEqual(2, hk.firstVariant.BindingCount, "Exact match should attach one callback per scheduler.");

				hk.PerformInNewThreadMadeByCallerAsync(hk.firstVariant, 0, 0);

				Assert.IsTrue(probe.WaitFor("main"));
				Assert.IsTrue(probe.WaitFor("worker"));
				Assert.AreEqual(worker.Id, probe.ThreadIds["worker"]);
				Assert.AreNotEqual(probe.ThreadIds["main"], probe.ThreadIds["worker"]);
			});
		}

		[Test, Category("Threading"), Category("Gui")]
		public void HookCriterionDispatch()
		{
			var criterionCalls = 0;
			var callbackRan = new ManualResetEventSlim(false);
			var previousCriterion = s.Threads.CurrentThread.hotCriterion;

			try
			{
				s.Threads.CurrentThread.hotCriterion = new KeysharpFunc((Func<object, object>)(_ =>
				{
					_ = Interlocked.Increment(ref criterionCalls);
					return 1L;
				}));

				var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ =>
				{
					callbackRan.Set();
					return 0L;
				})), 0, "$a", 0);
				s.HotkeyData.shk = [..s.HotkeyData.shk, hk];

				Assert.IsTrue(s.HookThread.PostMessage(new KeysharpMsg
				{
					message = (uint)UserMessages.AHK_HOOK_HOTKEY,
					wParam = new nint(hk.id),
					lParam = 0,
					obj = new HookHotkeyMsg
					{
						variant = hk.firstVariant,
						criterionFoundHwnd = 1
					}
				}));

				Assert.IsTrue(WaitWithUiPump(() => callbackRan.IsSet), "Prequalified hook hotkey did not dispatch.");
				Assert.AreEqual(0, Volatile.Read(ref criterionCalls), "Hook hotkey dispatch should not re-evaluate its criterion on receipt.");
			}
			finally
			{
				s.Threads.CurrentThread.hotCriterion = previousCriterion;
			}
		}

		[Test, Category("Threading"), Category("UI")]
		public void WindowDispatch()
		{
			var callbackRan = new ManualResetEventSlim(false);
			var previousCriterion = s.Threads.CurrentThread.hotCriterion;
			var previousCalls = Interlocked.Exchange(ref hookWinCriterionCalls, 0);
			hookWinCriterionResult = true;

			try
			{
				s.Threads.CurrentThread.hotCriterion = new NamedCriterion("HotIfWinActivePrivate", () =>
				{
					_ = Interlocked.Increment(ref hookWinCriterionCalls);
					return hookWinCriterionResult;
				});

				var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ =>
				{
					callbackRan.Set();
					return 0L;
				})), 0, "$c", 0);
				s.HotkeyData.shk = [..s.HotkeyData.shk, hk];

				var buildMethod = s.HookThread.GetType().GetMethod("TryBuildHookHotkeyMessage", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.IsNotNull(buildMethod, "Hook hotkey message builder should exist.");
				var args = new object[] { hk.id, 0UL, null, null, null };
				Assert.IsTrue((bool)buildMethod.Invoke(s.HookThread, args), "Hook hotkey should qualify successfully.");
				Assert.AreEqual(1, Volatile.Read(ref hookWinCriterionCalls), "Window-style criterion should be evaluated once on the hook side.");

				var hookMsg = (HookHotkeyMsg)args[4];
				Assert.IsNull(hookMsg.variant, "Window-style criteria should be re-evaluated on receipt instead of dispatching the prequalified variant directly.");

				Assert.IsTrue(s.HookThread.PostMessage(new KeysharpMsg
				{
					message = (uint)UserMessages.AHK_HOOK_HOTKEY,
					wParam = new nint(hk.id),
					lParam = 0,
					obj = hookMsg
				}));

				Assert.IsTrue(WaitWithUiPump(() => Volatile.Read(ref hookWinCriterionCalls) == 2), "Window-style criterion was not re-evaluated on receipt.");
				Assert.IsTrue(WaitWithUiPump(() => callbackRan.IsSet, 5000), "Re-evaluated hook hotkey did not dispatch.");
				Assert.AreEqual(2, Volatile.Read(ref hookWinCriterionCalls), "Window-style criterion should be evaluated again when the message is received.");
			}
			finally
			{
				s.Threads.CurrentThread.hotCriterion = previousCriterion;
				_ = Interlocked.Exchange(ref hookWinCriterionCalls, previousCalls);
			}
		}

		[Test, Category("Threading"), Category("UI")]
		public void HotIfRegistration()
		{
			var previousCriterion = s.Threads.CurrentThread.hotCriterion;

			try
			{
				var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ => 0L)), 0, "b", 0);
				s.HotkeyData.shk = [..s.HotkeyData.shk, hk];
				s.Threads.CurrentThread.hotCriterion = new KeysharpFunc((Func<object, object>)(_ => 1L));
				_ = hk.AddVariant(new KeysharpFunc((Func<object, object>)(_ => 0L)), 0);
				s.Threads.CurrentThread.hotCriterion = previousCriterion;

				_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(s);

#if WINDOWS
				Assert.AreEqual(HotkeyTypeEnum.Normal, hk.type, "A hotkey with an enabled global variant should be allowed to stay on the non-hook WM_HOTKEY path.");
#else
				Assert.AreEqual(HotkeyTypeEnum.KeyboardHook, hk.type, "Unix platforms currently route active hotkeys through the hook path instead of a registered WM_HOTKEY-style path.");
#endif
			}
			finally
			{
				s.Threads.CurrentThread.hotCriterion = previousCriterion;
			}
		}

		[Test, Category("Threading"), Category("UI")]
		public void WorkerHotkeyAfterExit()
		{
			var mainStarted = new ManualResetEventSlim(false);
			var releaseMain = new ManualResetEventSlim(false);
			var workerRan = new ManualResetEventSlim(false);
			var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ =>
			{
				mainStarted.Set();
				_ = releaseMain.Wait(2000);
				return 0L;
			})), 0, "$a", 0);
			s.HotkeyData.shk = [..s.HotkeyData.shk, hk];

			WithMatchingWorkerHotkey(hk, new KeysharpFunc((Func<object, object>)(_ =>
			{
				workerRan.Set();
				return 0L;
			})), worker =>
			{
				var releaser = Task.Run(() =>
				{
					_ = mainStarted.Wait(1000);
					Thread.Sleep(100);
					releaseMain.Set();
				});

				hk.PerformInNewThreadMadeByCallerAsync(hk.firstVariant, 0, 0);

				Assert.IsTrue(WaitWithUiPump(() => mainStarted.IsSet, 1000), "Main-thread hotkey callback never started.");
				Assert.IsTrue(WaitWithUiPump(() => workerRan.IsSet, 1000),
					"Worker-bound hotkey callback did not run after the main-thread callback completed.");
				releaser.Wait();
			});
		}

		[Test, Category("Threading"), Category("Gui")]
		public void BlockedMainBinding()
		{
			var mainStarted = new ManualResetEventSlim(false);
			var releaseMain = new ManualResetEventSlim(false);
			var workerProgressed = new ManualResetEventSlim(false);
			var workerCalls = 0;
			var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ =>
			{
				mainStarted.Set();
				_ = releaseMain.Wait(2000);
				return 0L;
			})), 0, "$a", 0);
			s.HotkeyData.shk = [..s.HotkeyData.shk, hk];

			WithMatchingWorkerHotkey(hk, new KeysharpFunc((Func<object, object>)(_ =>
			{
				if (Interlocked.Increment(ref workerCalls) >= 2)
					workerProgressed.Set();

				Thread.Sleep(50);
				return 0L;
			})), worker =>
			{
				var releaser = Task.Run(() =>
				{
					_ = mainStarted.Wait(1000);
					_ = workerProgressed.Wait(1000);
					releaseMain.Set();
				});

				hk.firstVariant.maxThreads = 2;
				hk.PerformInNewThreadMadeByCallerAsync(hk.firstVariant, 0, 0);
				Assert.IsTrue(WaitWithUiPump(() => mainStarted.IsSet, 1000), "Main-thread hotkey callback never started.");

				for (var i = 0; i < 3; i++)
					hk.PerformInNewThreadMadeByCallerAsync(hk.firstVariant, 0, 0);

				Assert.IsTrue(WaitWithUiPump(() => workerProgressed.IsSet, 1000),
					"Worker-bound hotkey callback did not make forward progress while the main binding was still blocked.");
				releaser.Wait();
			}, "T2");
		}

		[Test, Category("Threading"), Category("UI")]
		public void PostSendAfterExit()
		{
			_ = s.EventScheduler;
			var worker = StartWorker(() => { });

			AssertEventually(() => !worker.IsActive, "Worker with no persistent registrations should exit on its own.");

			var postError = AssertScriptError(() => worker.Post(new KeysharpFunc((Func<object>)(() => 0L))));
			var sendError = AssertScriptError(() => worker.Send(new KeysharpFunc((Func<object>)(() => 0L))));

			Assert.That(postError.Message, Does.Contain("Real thread is no longer alive."));
			Assert.That(sendError.Message, Does.Contain("Real thread is no longer alive."));
		}

		[Test, Category("Threading"), Category("UI")]
		public void StopUnhooksHotkeys()
		{
			var hk = new HotkeyDefinition(s, (uint)s.HotkeyData.shk.Length, new KeysharpFunc((Func<object, object>)(_ => 0L)), 0, "$a", 0);
			s.HotkeyData.shk = [..s.HotkeyData.shk, hk];
			_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(s);

			// Installing a real global hook needs devices to grab: a headless container (WSL/CI has no /dev/input
			// and no keysharp-inputd) cannot, so there is nothing to assert about unhooking there.
			if (!s.HookThread.HasKbdHook())
				Assert.Ignore("No global keyboard hook in this environment; hook installation needs real input devices.");

			s.Dispose();

			AssertEventually(() => !s.HookThread.HasKbdHook(), "Script.Dispose() did not uninstall the keyboard hook.");
		}
	}
}
