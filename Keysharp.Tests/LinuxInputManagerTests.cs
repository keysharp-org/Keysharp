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

		[TestCase(VirtualKeys.VK_SHIFT, 42u)] // KEY_LEFTSHIFT
		[TestCase(VirtualKeys.VK_SHIFT, 54u)] // KEY_RIGHTSHIFT
		[TestCase(VirtualKeys.VK_CONTROL, 97u)] // KEY_RIGHTCTRL
		[TestCase(VirtualKeys.VK_LBUTTON, KeyCodes.EvdevButtonLeft)]
		[TestCase(VirtualKeys.VK_RBUTTON, KeyCodes.EvdevButtonRight)]
		[TestCase(VirtualKeys.VK_MBUTTON, KeyCodes.EvdevButtonMiddle)]
		[TestCase(VirtualKeys.VK_XBUTTON1, KeyCodes.EvdevButtonSide)]
		[TestCase(VirtualKeys.VK_XBUTTON1, KeyCodes.EvdevButtonBack)]
		[TestCase(VirtualKeys.VK_XBUTTON2, KeyCodes.EvdevButtonExtra)]
		[TestCase(VirtualKeys.VK_XBUTTON2, KeyCodes.EvdevButtonForward)]
		public void DeviceBitmapAliases(uint vk, uint code)
		{
			var held = new byte[KeysharpInputClient.KeyStateBitmapBytes];
			held[code / 8] |= (byte)(1 << (int)(code % 8u));
			Assert.IsTrue(NativeInputKeyboard.TryGetVkFromEvdevBitmap(vk, held, false, false, out var down));
			Assert.IsTrue(down);
			Assert.IsTrue(NativeInputKeyboard.TryGetVkFromEvdevBitmap(vk,
				new byte[KeysharpInputClient.KeyStateBitmapBytes], false, false, out down));
			Assert.IsFalse(down);
			Assert.IsFalse(NativeInputKeyboard.TryGetVkFromEvdevBitmap(vk, [], false, false, out _));
		}

#endif
	}
}
