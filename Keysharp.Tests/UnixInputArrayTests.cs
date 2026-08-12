#if !WINDOWS
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Input.Hooks.Unix;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.Unix;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class UnixInputArrayTests : TestRunner
	{
		private sealed class RecordingSender : UnixKeyboardMouseSender
		{
			internal bool ThrowDuringDispatch { get; set; }
			internal int DispatchCount { get; private set; }
			internal SendModes CurrentMode => sendMode;
			internal void SetMode(SendModes mode) => sendMode = mode;

			protected override void DispatchEventArray(UnixHookThread lht, InputArrayState state, long extraInfo)
			{
				DispatchCount++;

				if (ThrowDuringDispatch)
					throw new InvalidOperationException("deterministic dispatch failure");
			}
		}

		[Test, Category("Input")]
		public void ArrayFrameCleanup()
		{
			var sender = new RecordingSender();
			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, MOD_LSHIFT);
			sender.PutKeybdEventIntoArray(0, 0x41, 0, 0, 0);
			sender.sendInputCursorPos.X = 101;
			sender.sendInputCursorPos.Y = 202;

			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, MOD_RCONTROL);
			sender.PutKeybdEventIntoArray(0, 0x42, 0, 0, 0);
			var finalDelay = -1L;
			sender.SendEventArray(ref finalDelay, 0);
			sender.CleanupEventArray(finalDelay);

			Assert.AreEqual(1, sender.DispatchCount);
			Assert.AreEqual(1, sender.SiEventCount(), "cleanup consumed the outer array frame");
			Assert.AreEqual(MOD_LSHIFT, sender.eventModifiersLR);
			Assert.AreEqual(SendModes.Input, sender.CurrentMode);
			Assert.AreEqual(101, sender.sendInputCursorPos.X);
			Assert.AreEqual(202, sender.sendInputCursorPos.Y);

			sender.CleanupEventArray(-1);
			Assert.AreEqual(0, sender.SiEventCount());
			Assert.AreEqual(0u, sender.eventModifiersLR);
		}

		[Test, Category("Input")]
		public void ArrayFrameFailure()
		{
			var sender = new RecordingSender();
			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, MOD_LALT);
			sender.PutKeybdEventIntoArray(0, 0x41, 0, 0, 0);

			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, MOD_RSHIFT);
			sender.PutKeybdEventIntoArray(0, 0x42, 0, 0, 0);
			sender.ThrowDuringDispatch = true;
			var finalDelay = -1L;

			Assert.Throws<InvalidOperationException>(() => sender.SendEventArray(ref finalDelay, 0));
			sender.AbortEventArray();

			Assert.AreEqual(1, sender.SiEventCount(), "abort consumed the outer array frame");
			Assert.AreEqual(MOD_LALT, sender.eventModifiersLR);
			Assert.AreEqual(SendModes.Input, sender.CurrentMode);

			sender.AbortEventArray();
			Assert.AreEqual(0, sender.SiEventCount());
			Assert.AreEqual(0u, sender.eventModifiersLR);
		}
	}
}
#endif
