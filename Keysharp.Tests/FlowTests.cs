using static Keysharp.Core.Loops;
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
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				ExitApp(2)
				FileAppend('fail', '*')
			", "2", true, false, 2)));
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				Exit(0)
				FileAppend('fail', '*')
			", "3", true, false, 0)));
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				FileAppend('pass', '*')
				Exit(2)
				FileAppend('fail', '*')
			", "4", true, false, 2)));
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				SetTimer((*) => FileAppend('pass', '*'), -1)
				Exit(1)
			", "5", true, false, 1)));
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				SetTimer((*) => (FileAppend('pass', '*'), Exit(3)), -1)
				Exit(2)
				FileAppend('fail', '*')
			", "6", true, false, 3)));
			//Flow.ResetState();
			Assert.IsTrue(HasPassed(RunScript(@"
				SetTimer((*) => (FileAppend('pass', '*'), ExitApp(0)), -1)
				SomeLabel:
				Sleep(1)
				goto SomeLabel
			", "7", true, false, 0)));
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
			Assert.AreEqual(new Keysharp.Core.Array("stringone", "stringtwo", "stringthree"), val);
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
			Assert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, ((Core.Buffer)val).ToByteArray());
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
					Assert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, ((Core.Buffer)val).ToByteArray());
					Assert.AreEqual("REG_BINARY", Accessors.A_LoopRegType);
					Assert.AreEqual("bin1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub2", Accessors.A_LoopRegKey);
				}
				else if (i == 4)
				{
					Assert.AreEqual(new Keysharp.Core.Array("stringone", "stringtwo", "stringthree"), val);
					Assert.AreEqual("KEY", Accessors.A_LoopRegType);
					Assert.AreEqual("ks_sub1", Accessors.A_LoopRegName);
					Assert.AreEqual("HKEY_CURRENT_USER\\SOFTWARE\\KeysharpTest\\ks_sub1", Accessors.A_LoopRegKey);
				}
				else if (i == 5)
				{
					Assert.AreEqual(new Keysharp.Core.Array("stringone", "stringtwo", "stringthree"), val);
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
        public void FlowOnError() => Assert.IsTrue(TestScript("flow-onerror", false));

        [Test, Category("Flow"), NonParallelizable]
        public void FlowRealThreads() => Assert.IsTrue(TestScript("flow-realthreads", false));

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