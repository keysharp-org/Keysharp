using static Keysharp.Core.External;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	public partial class ExternalTests : TestRunner
	{
#if WINDOWS
		[Test, Category("External")]
		public void CallbackCreate() => Assert.IsTrue(TestScript("external-callbackcreate", false));

		[Test, Category("External")]
		public void DllCall_()
		{
			var desktop = WindowsAPI.GetDesktopWindow();
			var rect = new RECT();
			var buf = new Keysharp.Core.Buffer(16, 0);
			_ = Dll.DllCall("user32.dll\\GetWindowRect", "ptr", desktop, "ptr", buf);
			_ = WindowsAPI.GetWindowRect((nint)desktop, out rect);
			var l = (long)NumGet(buf, 0, "UInt");
			var t = (long)NumGet(buf, 4, "UInt");
			var r = (long)NumGet(buf, 8, "UInt");
			var b = (long)NumGet(buf, 12, "UInt");
			Assert.IsTrue(r > 0 && b > 0);
			Assert.AreEqual(rect.Left, l);
			Assert.AreEqual(rect.Right, r);
			Assert.AreEqual(rect.Top, t);
			Assert.AreEqual(rect.Bottom, b);
			var str = "lower";
			var len = str.Length;
			var strbuf = new StringBuffer(str);
			_ = Dll.DllCall("user32.dll\\CharUpperBuff", "ptr", strbuf.Ptr, "UInt", len);
			Assert.AreEqual(strbuf.ToString(), str.ToUpper());
			Assert.IsTrue(TestScript("external-dllcall", false));
		}

		[Test, Category("External")]
		public void NumPutNumGet() => Assert.IsTrue(TestScript("external-numput-numget", true));

		[Test, Category("External")]
		public void COM() => Assert.IsTrue(TestScript("external-com", false));

		[Test, Category("External")]
		public void Clr() => Assert.IsTrue(TestScript("external-clr", false));

		[Test, Category("External")]
		public void OnMessage() => Assert.IsTrue(TestScript("external-onmessage", false));
#endif
	}
}