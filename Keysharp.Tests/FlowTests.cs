using static Keysharp.Runtime.Loops;
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
			//Keysharp.Builtins.Flow.ResetState();
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

        [Test, Category("Flow")]
        public void FlowForIn() => Assert.IsTrue(TestScript("flow-for-in", false));

        [Test, Category("Flow")]
        public void FlowIf() => Assert.IsTrue(TestScript("flow-if", true));

		[Test, Category("Flow"), NonParallelizable]
		public void FlowLoop()
		{
			const long n = 10L;
			var x = 0L;
			Assert.AreEqual(0L, Accessors.A_Index);
			_ = Push();

            foreach (long i in Loop(n))
            {
                Assert.AreEqual(++x, i);
                Assert.AreEqual(i, Accessors.A_Index);
            }

            _ = Pop();//Caller is always required to do this.
            Assert.AreEqual(x, n);
            Assert.AreEqual(0L, Accessors.A_Index);
            x = 0;
            _ = Push();

            foreach (long i in Loop(n))
            {
                x++;

                if (i > 5)
                    break;
            }

            _ = Pop();//Caller is always required to do this.
            Assert.AreEqual(x, 6L);
            Assert.AreEqual(0L, Accessors.A_Index);
            Assert.IsTrue(TestScript("flow-loop", true));
        }

        [Test, Category("Flow")]
        public void FlowLoopParse() => Assert.IsTrue(TestScript("flow-loop-parse", true));

        [Test, Category("Flow")]
        public void FlowLoopRead() => Assert.IsTrue(TestScript("flow-loop-read", true));

#if WINDOWS
		[Test, Category("Flow")]
		public void FlowLoopReg()
		{
			try
			{
				_ = Registrys.RegDeleteKey(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest");
			}
			catch
			{
			}

			//
			_ = Registrys.RegWrite("ksdefval", "REG_SZ", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "");
			var val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "");
			Assert.AreEqual("ksdefval", val);
			//
			_ = Registrys.RegWrite("ksval", "REG_SZ", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval");
			val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval");
			Assert.AreEqual("ksval", val);
			//
			_ = Registrys.RegWrite("stringone\nstringtwo\nstringthree", "REG_MULTI_SZ", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "");
			val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "");
			Assert.AreEqual(new Keysharp.Builtins.Array("stringone", "stringtwo", "stringthree"), val);
			//
			_ = Registrys.RegWrite(1, "REG_DWORD", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1");
			val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1");
			Assert.AreEqual(1, val);
			//
			_ = Registrys.RegWrite(2, "REG_QWORD", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1");
			val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1");
			Assert.AreEqual(2, val);
			//
			_ = Registrys.RegWrite("AABBCCDD", "REG_BINARY", @"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1");
			val = Registrys.RegRead(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1");
			Assert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, ((Builtins.Buffer)val).ToByteArray());
			//
			var i = 0;
			_ = Push(LoopType.Registry);

            foreach (var reg in LoopRegistry(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "kvr"))
            {
                val = Registrys.RegRead(null, null, "testdefault");

				if (i == 0)
				{
					Assert.AreEqual("ksval", val);
					Assert.AreEqual("REG_SZ", Accessors.A_LoopRegType);
					Assert.AreEqual("testval", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest", Accessors.A_LoopRegKey);
				}
				else if (i == 1)
				{
					Assert.AreEqual("ksdefval", val);
					Assert.AreEqual("", Accessors.A_LoopRegName);
					Assert.AreEqual("REG_SZ", Accessors.A_LoopRegType);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest", Accessors.A_LoopRegKey);
				}
				else if (i == 2)
				{
					Assert.AreEqual("testdefault", val.ToString());
					Assert.AreEqual("KEY", Accessors.A_LoopRegType);
					Assert.AreEqual("ks_sub2", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub2", Accessors.A_LoopRegKey);
				}
				else if (i == 3)
				{
					Assert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, ((Builtins.Buffer)val).ToByteArray());
					Assert.AreEqual("REG_BINARY", Accessors.A_LoopRegType);
					Assert.AreEqual("bin1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub2", Accessors.A_LoopRegKey);
				}
				else if (i == 4)
				{
					Assert.AreEqual(new Keysharp.Builtins.Array("stringone", "stringtwo", "stringthree"), val);
					Assert.AreEqual("KEY", Accessors.A_LoopRegType);
					Assert.AreEqual("ks_sub1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1", Accessors.A_LoopRegKey);
				}
				else if (i == 5)
				{
					Assert.AreEqual(new Keysharp.Builtins.Array("stringone", "stringtwo", "stringthree"), val);
					Assert.AreEqual("REG_MULTI_SZ", Accessors.A_LoopRegType);
					Assert.AreEqual("", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1", Accessors.A_LoopRegKey);
				}
				else if (i == 6)
				{
					Assert.AreEqual("testdefault", val);
					Assert.AreEqual("KEY", Accessors.A_LoopRegType);
					Assert.AreEqual("ks_sub1_sub1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1\\ks_sub1_sub1", Accessors.A_LoopRegKey);
				}
				else if (i == 7)
				{
					Assert.AreEqual(2, val);
					Assert.AreEqual("REG_QWORD", Accessors.A_LoopRegType);
					Assert.AreEqual("qword1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1\\ks_sub1_sub1", Accessors.A_LoopRegKey);
				}
				else if (i == 8)
				{
					Assert.AreEqual(1, val);
					Assert.AreEqual("REG_DWORD", Accessors.A_LoopRegType);
					Assert.AreEqual("dword1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1\\ks_sub1_sub1", Accessors.A_LoopRegKey);
				}

                i++;
            }

			_ = Pop();
			_ = Registrys.RegDelete(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest", "testval");
			_ = Registrys.RegDelete(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1", "");
			_ = Registrys.RegDelete(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "dword1");
			_ = Registrys.RegDelete(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub1\ks_sub1_sub1", "qword1");
			_ = Registrys.RegDelete(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2", "bin1");
			_ = Registrys.RegDeleteKey(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest\ks_sub2");
			_ = Registrys.RegDeleteKey(@"HKEY_CURRENT_USER\SOFTWARE\KeysharpTest");
			Assert.IsTrue(TestScript("flow-loop-reg", true));
		}
#endif

        [Test, Category("Flow")]
        public void FlowLoopReturn() => Assert.IsTrue(TestScript("flow-loop-return", false));

        [Test, Category("Flow")]
        public void FlowLoopSwitchBreakGoto() => Assert.IsTrue(TestScript("flow-loop-switch-break-goto", true));

        [Test, Category("Flow")]
        public void FlowLoopThrow() => Assert.IsTrue(TestScript("flow-loop-throw", false));

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
		public void FlowWhile()
		{
			const long n = 10L;
			var x = 0L;
			Assert.AreEqual(0L, Accessors.A_Index);
			_ = Push();

            foreach (long i in Loop(n))
            {
                Assert.AreEqual(++x, i);
                Assert.AreEqual(i, Accessors.A_Index);
            }

            _ = Pop();//Caller is always required to do this.
            Assert.AreEqual(x, n);
            Assert.AreEqual(0L, Accessors.A_Index);
            Assert.IsTrue(TestScript("flow-while", true));
        }
    }
}
