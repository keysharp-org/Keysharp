using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ProcessTests : TestRunner
	{
		[Test, Category("Process")]
		public void ProcessRunWaitClose() => Assert.IsTrue(TestScript("process-run-wait-close", false));

		[Test, Category("Process")]
		public void ProcessGetParent() => Assert.IsTrue(TestScript("process-get-parent", false));

		[Test, Category("Process")]
		public void ProcessRunScript()
		{
			Assert.IsTrue(TestScript("process-runscript", false));
		}
	}
}
