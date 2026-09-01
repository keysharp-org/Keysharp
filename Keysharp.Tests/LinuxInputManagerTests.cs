using Assert = NUnit.Framework.Legacy.ClassicAssert;

#if LINUX
using Keysharp.Internals;
using Keysharp.Internals.Input.Linux;
using Keysharp.Internals.Input.Keyboard;
#endif

namespace Keysharp.Tests
{
	[Category("Internal"), Category("Curated")]
	public class LinuxInputManagerTests
	{
#if LINUX
		[Test, Category("Misc")]
		public void X11KeyStateFallbackIsLimitedToModifiers()
		{
			Assert.IsTrue(LinuxKeyboard.MayUseUnprivilegedKeyStateFallback(VirtualKeys.VK_LSHIFT));
			Assert.IsTrue(LinuxKeyboard.MayUseUnprivilegedKeyStateFallback(VirtualKeys.VK_RMENU));
			Assert.IsFalse(LinuxKeyboard.MayUseUnprivilegedKeyStateFallback(0x41));
			Assert.IsFalse(LinuxKeyboard.MayUseUnprivilegedKeyStateFallback(VirtualKeys.VK_CAPITAL));
		}

#endif
	}
}
