using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable]
	public class TimerSchedulerTests : TestRunner
	{
		[Test, Category("Threading")]
		public void BlockedTimer()
		{
			var output = RunScript(string.Concat(path, "timer-blocked-fires-once.ahk"), "timer-blocked-fires-once", true, false);
			Assert.AreEqual("pass", output.Trim(), output);
		}

		[Test, Category("Threading")]
		public void ShortTimerPeriod() => Assert.IsTrue(TestScript("timer-short-callback-keeps-period", false));

		[Test, Category("Threading")]
		public void LongTimerCoalescing() => Assert.IsTrue(TestScript("timer-long-callback-coalesces", false));

		[Test, Category("Threading")]
		public void CallbackSleep() => Assert.IsTrue(TestScript("timer-during-callback-sleep", false));
	}
}
