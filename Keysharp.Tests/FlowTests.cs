using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
    public partial class FlowTests : TestRunner
    {
        [Test, Category("Flow")]
        public void FlowEnum() => Assert.IsTrue(TestScript("flow-enum", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowExit()
        {
            Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				ExitApp(0)
				FileAppend('fail', '*')
			", "1", true, false, 0)));
			//Keysharp.Builtins.Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				ExitApp(2)
				FileAppend('fail', '*')
			", "2", true, false, 2)));
			//Keysharp.Builtins.Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				Exit(0)
				FileAppend('fail', '*')
			", "3", true, false, 0)));
			//Keysharp.Builtins.Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				Exit(2)
				FileAppend('fail', '*')
			", "4", true, false, 2)));
			//As in AHK, the auto-execute section's Exit code stays pending while a timer keeps the script running, and the
			//timer's thread ending with nothing left running exits with it.
			Assert.IsTrue(HasPassed(RunScript(@"
				SetTimer((*) => FileAppend('pass', '*'), -1)
				Exit(1)
			", "5", true, false, 1)));
			//Keysharp.Builtins.Flow.ResetState();
			if (!Script.IsUiInitializationBlocked)
				Assert.IsTrue(HasPassed(RunScript(@"
					SetTimer((*) => (FileAppend('pass', '*'), Exit(3)), -1)
					Exit(2) ; Exits the auto-exec section, then the UI loop should process the timer
					FileAppend('fail', '*')
				", "6", true, false, 3)));
			//Keysharp.Builtins.Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				SetTimer((*) => (FileAppend('pass', '*'), ExitApp(0)), -1)
				SomeLabel:
				Sleep(1)
				goto SomeLabel
			", "7", true, false, 0)));
			//Targeting an underlying pseudo-thread: the timer marks the auto-execute thread, the later request
			//replaces the pending exit code, and Exit returns the target's ID both times.
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_Thread }
				autoThread := A_Thread
				SetTimer((*) => (autoThread.Exit(6), FileAppend(autoThread.Exit(7) == autoThread.Id ? 'pass' : 'fail', '*')), -1)
				Loop
					Sleep(1)
			", "8", true, false, 7)));
			//The auto-execute thread is the oldest one on its real thread, so it is both Index 1 and Threads[1].
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_Thread, A_RealThread }
				if A_Thread.Index != 1
					ExitApp(1)
				autoId := A_Thread.Id
				SetTimer((*) => FileAppend(A_RealThread.Threads[1].Exit(7) == autoId ? 'pass' : 'fail', '*'), -1)
				Loop
					Sleep(1)
			", "9", true, false, 7)));
			//A pseudo-thread stays reachable through Underlying while it is the one being interrupted.
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_Thread }
				autoId := A_Thread.Id
				SetTimer((*) => FileAppend(A_Thread.Underlying.Id == autoId ? 'pass' : 'fail', '*'), -1)
				Sleep(200)
				ExitApp(0)
			", "10", true, false, 0)));
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_Thread }
				FileAppend('pass', '*')
				A_Thread.Exit(4)
				FileAppend('fail', '*')
			", "11", true, false, 4)));
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_RealThread }
				FileAppend('pass', '*')
				A_RealThread.Threads[1].Exit(5)
				FileAppend('fail', '*')
			", "12", true, false, 5)));
			//A Thread object held past its pseudo-thread's lifetime reports itself inactive and refuses to be
			//exited, rather than silently targeting whichever pseudo-thread reused the pooled slot.
			Assert.IsTrue(HasPassed(RunScript(@"
				#import KS { A_Thread, A_RealThread }
				staleThread := 0
				SetTimer(CaptureThread, -1)
				while !staleThread
					Sleep(1)
				SetTimer(CheckStaleThread, -1)
				Loop
					Sleep(1)

				CaptureThread() {
					global staleThread := A_Thread
				}

				CheckStaleThread() {
					global staleThread
					if staleThread.IsActive
						FileAppend('fail', '*')
					try
						staleThread.Exit(9)
					catch TargetError
						FileAppend('pass', '*')
					A_RealThread.Threads[1].Exit(0)
				}
			", "13", true, false, 0)));
        }

        // Regression for the double-teardown bug (ExitApp/Reload inside an OnExit handler). A nested ExitAppInternal
        // ran the ENTIRE termination sequence (including the __Delete loop over static/global objects) and set
        // hasExited, then the outer call resumed and ran it a SECOND time. A global object's __Delete fires exactly
        // once per teardown, so with the bug it printed twice; the hasExited guard added to ExitAppInternal restores
        // it to exactly once. Headless: no GUI or input required.
        [Test, Category("Flow"), NonParallelizable]
        public void FlowOnExitNestedExitApp()
        {
            var output = RunScript(@"
				class Cleaner {
					__Delete() {
						FileAppend('D', '*')
					}
				}
				g := Cleaner()
				OnExit((*) => ExitApp())
				ExitApp()
			", "flow-onexit-nested-exitapp", true, false, 0);
            var deletes = output.Count(c => c == 'D');
            Assert.AreEqual(1, deletes, $"__Delete ran {deletes} time(s) during teardown; expected exactly 1. Raw output: [{output}]");
        }

		// A release made inside a thread takes effect when the last thread ends, as in AHK. Only a script whose
		// auto-execute section has run exits by itself; the C# fixture never runs one.
		[Test, Category("Flow"), NonParallelizable]
		public void FlowExitRequests()
		{
			// No check runs once an exit is committed: a __Delete in the exit sweep that releases and pumps must not
			// re-enter it, which would sweep again.
			Passes(RunScript(@"
				class Cleaner {
					__Delete() {
						SetTimer(Nothing, 0)
						Sleep(20)
						FileAppend(exits = 1 ? 'pass' : 'fail exits ' exits, '*')
					}
				}
				Nothing() {
				}
				Exiting(*) {
					global exits += 1
				}
				exits := 0
				swept := Cleaner()
				OnExit(Exiting)
			", "exit-request-delete", true, false, 0));

			// The rest end the script after auto-execute, which needs the message loop.
			if (Script.IsUiInitializationBlocked)
				return;

			// Persistent(false) inside a thread takes effect when that thread ends, not before.
			Passes(RunScript(@"
				Persistent()
				OnExit(Exiting)
				SetTimer(Release, -1)
				Release() {
					Persistent(false)
					Sleep(50)
					FileAppend('pass', '*')
				}
				Exiting(reason, *) {
					if reason != 'Exit'
						FileAppend(' fail reason ' reason, '*')
				}
			", "exit-request-persistent", true, false, 0));
			// SetTimer(f, 0): a timer that stops itself ends the script when its thread ends.
			Passes(RunScript(@"
				SetTimer(Tick, 10)
				Tick() {
					static n := 0
					if ++n < 3
						return
					SetTimer(Tick, 0)
					Sleep(50)
					FileAppend('pass', '*')
				}
			", "exit-request-settimer", true, false, 0));
			// A release made while another thread runs takes effect when the last thread ends.
			Passes(RunScript(@"
				Persistent()
				released := false
				SetTimer(Outer, -1)
				Outer() {
					SetTimer(Inner, -1)
					Sleep(100)
					FileAppend(released ? 'pass' : 'fail: Inner did not run', '*')
				}
				Inner() {
					global released := true
					Persistent(false)
				}
			", "exit-request-nested", true, false, 0));
			// The check at a thread end exits with the code that thread's Exit(n) set.
			Passes(RunScript(@"
				Persistent()
				SetTimer(Release, -1)
				Release() {
					Persistent(false)
					FileAppend('pass', '*')
					Exit(3)
				}
			", "exit-request-code", true, false, 3));
			// An Exit(n) in a thread that interrupted another is not left for the script's exit, as in AHK, where only
			// the only running thread's is: the auto-execute section ends later, and the script exits with 0.
			Passes(RunScript(@"
				SetTimer(() => Exit(5), -1)
				Sleep(100)
				FileAppend('pass', '*')
			", "exit-request-interrupted-code", true, false, 0));
			// SetTimer with no function refers to the timer that launched the current thread, even after another
			// timer's thread interrupted it and ended.
			Passes(RunScript(@"
				outerRuns := 0, innerRuns := 0
				SetTimer(Outer, -1)
				SetTimer(Finish, -300)
				Outer() {
					global outerRuns += 1
					if outerRuns = 1 {
						SetTimer(Inner, -1)
						Sleep(50)
						SetTimer(, -1)
					}
				}
				Inner() {
					global innerRuns += 1
				}
				Finish() {
					FileAppend(outerRuns = 2 && innerRuns = 1 ? 'pass' : 'fail ' outerRuns ' ' innerRuns, '*')
				}
			", "exit-request-own-timer", true, false, 0));
			// A failed auto-execute section ends a script nothing keeps running with the reason Error, as in AHK.
			var failed = RunScript(@"
				OnExit((reason, code) => FileAppend('reason ' reason ' ' code ';', '*'))
				throw Error('expected auto-execute failure')
			", "exit-request-autoexec-error", true, false, 1);
			Assert.That(failed, Does.Contain("reason Error 1;"));
			Assert.That(failed, Does.Not.Contain("fail exit"));
#if WINDOWS
			// That exit spends the check the section's end posted: a veto is not followed by a second, stale ask
			// (reason Exit, code 0) before the message the vetoing handler posted exits with 5.
			var vetoed = RunScript(@"
				calls := 0
				OnMessage(0x5555, (*) => ExitApp(5))
				OnExit(Exiting)
				Exiting(reason, code) {
					global calls += 1
					FileAppend('reason ' reason ' ' code ';', '*')
					if calls = 1 {
						DetectHiddenWindows(true)
						PostMessage(0x5555, 0, 0, , A_ScriptHwnd)
						return 1
					}
				}
				throw Error('expected auto-execute failure')
			", "exit-request-autoexec-veto", true, false, 5);
			Assert.That(vetoed, Does.Contain("reason Error 1;reason Exit 5;"));
			Assert.That(vetoed, Does.Not.Contain("fail exit"));
			// OnClipboardChange(f, 0) emptying the chain releases what kept the script running.
			Passes(RunScript(@"
				OnClipboardChange(Changed)
				SetTimer(Remove, -1)
				Changed(*) {
				}
				Remove() {
					OnClipboardChange(Changed, 0)
					Sleep(50)
					FileAppend('pass', '*')
				}
			", "exit-request-clipboard", true, false, 0));
			// A vetoing OnExit is asked once per last-thread end: checks made before the posted one runs join it, and the
			// next check waits for the message's thread to end.
			Passes(RunScript(@"
				calls := 0
				messaged := false
				OnMessage(0x5555, Received)
				OnExit(Exiting)
				SetTimer(Release, -1)
				Release() => Persistent(false)
				Received(*) {
					global messaged := true
				}
				Exiting(reason, *) {
					global calls += 1
					if calls = 1 {
						DetectHiddenWindows(true)
						PostMessage(0x5555, 0, 0, , A_ScriptHwnd)
						return 1
					}
					FileAppend(calls = 2 && messaged && reason = 'Exit' ? 'pass' : 'fail ' calls ' ' messaged ' ' reason, '*')
				}
			", "exit-request-veto", true, false, 0));
#endif

			static void Passes(string output) => Assert.IsTrue(HasPassed(output), $"[{output}]");
		}

        [Test, Category("Flow")]
        public void FlowForIn() => Assert.IsTrue(TestScript("flow-for-in", false));

        [Test, Category("Flow")]
        public void FlowIf() => Assert.IsTrue(TestScript("flow-if", true));

		[Test, Category("Flow"), NonParallelizable]
		public void FlowLoop() => Assert.IsTrue(TestScript("flow-loop", true));

        [Test, Category("Flow")]
        public void FlowLoopParse() => Assert.IsTrue(TestScript("flow-loop-parse", true));

        [Test, Category("Flow")]
        public void FlowLoopRead() => Assert.IsTrue(TestScript("flow-loop-read", true));

#if WINDOWS
		[Test, Category("Flow")]
		public void FlowLoopReg() => Assert.IsTrue(TestScript("flow-loop-reg", true));
#endif

        [Test, Category("Flow")]
        public void FlowLoopReturn() => Assert.IsTrue(TestScript("flow-loop-return", false));

        [Test, Category("Flow")]
        public void FlowLoopSwitchBreakGoto() => Assert.IsTrue(TestScript("flow-loop-switch-break-goto", true));

        [Test, Category("Flow")]
        public void FlowLoopThrow() => Assert.IsTrue(TestScript("flow-loop-throw", false));

        [Test, Category("Flow"), Category("Curated"), NonParallelizable]
        public void FlowLoopInterrupt() => Assert.IsTrue(TestScript("flow-loop-interrupt", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowMultiStatement() => Assert.IsTrue(TestScript("flow-multi-statement", false));

        [Test, Category("Flow")]
        public void FlowOnError()
		{
			SkipIfUiInitializationBlocked("Error dispatch path differs when UI initialization is blocked.");
			Assert.IsTrue(TestScript("flow-onerror", false));
		}

        [Test, Category("Flow"), NonParallelizable]
        public void FlowRealThreads() => Assert.IsTrue(TestScript("flow-realthreads", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowThreadPause() => Assert.IsTrue(TestScript("flow-thread-pause", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowWorkerBlockedQueue() => Assert.IsTrue(TestScript("flow-worker-blocked-queue", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowWorkerCriticalDispatch() => Assert.IsTrue(TestScript("flow-worker-critical-dispatch", false));

        // Forces the headless branch: with a display present RunMainWindow takes the GUI path, where the ambient
        // context comes from the UI framework instead of the scheduler.
        [Test, Category("Flow"), NonParallelizable]
        public void FlowHeadlessAmbientContext()
        {
            var previous = Environment.GetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS");
            Environment.SetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS", "1");

            try
            {
                Assert.IsTrue(TestScript("flow-headless-ambient-context", false));
            }
            finally
            {
                Environment.SetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS", previous);
            }
        }

        // Forces the headless branch, where there is no UI framework to marshal through. Without it a display
        // is present and RunMainWindow takes the GUI path, so this scenario never reaches the code it covers.
        [Test, Category("Flow"), NonParallelizable]
        public void FlowRealThreadHeadlessMarshal()
        {
            var previous = Environment.GetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS");
            Environment.SetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS", "1");

            try
            {
                Assert.IsTrue(TestScript("flow-realthread-headless-marshal", false));
            }
            finally
            {
                Environment.SetEnvironmentVariable("KEYSHARP_FORCE_HEADLESS", previous);
            }
        }

        [Test, Category("Flow")]
        public void FlowSwitch() => Assert.IsTrue(TestScript("flow-switch", false));

        [Test, Category("Flow")]
        public void FlowTryCatch() => Assert.IsTrue(TestScript("flow-trycatch", false));

        //Collections tests already test foreach in C#, so just test the script here.
        [Test, Category("Flow")]
        public void FlowUntil() => Assert.IsTrue(TestScript("flow-until", true));

		[Test, Category("Flow"), NonParallelizable]
		public void FlowWhile() => Assert.IsTrue(TestScript("flow-while", true));
    }
}
