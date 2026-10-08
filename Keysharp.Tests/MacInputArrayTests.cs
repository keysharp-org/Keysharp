#if OSX
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Input.Hooks.Unix;
using Keysharp.Internals.Input.Keyboard;
using Keysharp.Internals.Input.MacOS;
using Keysharp.Internals.Window;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class MacInputArrayTests : TestRunner
	{
		private sealed class RecordingSender(Script owner, MacKeyboardState keyboardState = null, MacMouseEventStream mouseStream = null)
			: MacKeyboardMouseSender(owner, keyboardState ?? new(), mouseStream ?? new())
		{
			internal bool ThrowDuringDispatch { get; set; }
			internal int DispatchCount { get; private set; }
			internal string DispatchedText { get; private set; }
			internal bool FailNextModifierQuery { get; set; }
			internal (int X, int Y)[] DispatchedMousePositions { get; private set; }
			internal SendModes CurrentMode => sendMode;
			internal void SetMode(SendModes mode) => sendMode = mode;
			internal void StartSend() => OnSendKeysStarting();
			internal void FinishSend() => OnSendKeysFinished();

			internal override uint GetModifierLRState(bool explicitlyGet = false)
			{
				if (FailNextModifierQuery)
				{
					FailNextModifierQuery = false;
					throw new InvalidOperationException("deterministic modifier-query failure");
				}
				return 0;
			}

			protected override void DispatchEventArray(UnixHookThread lht, InputArrayState state, long extraInfo)
			{
				DispatchCount++;
				DispatchedText = new string(state.Events.Where(ev => ev.Type == ArrayEventType.Text).Select(ev => ev.Text).ToArray());
				DispatchedMousePositions = state.Events.Where(ev => ev.Type is ArrayEventType.MouseMoveAbs
					or ArrayEventType.MousePress or ArrayEventType.MouseRelease).Select(ev => (ev.X, ev.Y)).ToArray();

				if (ThrowDuringDispatch)
					throw new InvalidOperationException("deterministic dispatch failure");
			}
		}

		private sealed class DisplayMouseSink : MacMouseEventStream.Sink
		{
			internal override Rectangle[] GetDisplayBounds() => [new Rectangle(0, 0, 100, 100)];
		}

		[TestCase(false)]
		[TestCase(true)]
		[Category("Input")]
		public void QueuedMouseDisplayClamp(bool drag)
		{
			var sender = new RecordingSender(s, mouseStream: new(new DisplayMouseSink()));
			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, 0);
			sender.sendInputCursorPos = new POINT(90, 50);
			try
			{
				if (drag)
					sender.MouseClickDrag(VK_LBUTTON, 20, 0, -10, 0, 0, true);
				else
				{
					uint flags = 0;
					int x = 20, y = 0;
					sender.MouseMove(ref x, ref y, ref flags, 0, true);
					x = -10;
					y = 0;
					sender.MouseMove(ref x, ref y, ref flags, 0, true);
				}
				var finalDelay = -1L;
				sender.SendEventArray(ref finalDelay, 0);
				var expected = drag ? new[] { (99, 50), (99, 50), (89, 50), (89, 50) } : new[] { (99, 50), (89, 50) };
				CollectionAssert.AreEqual(expected, sender.DispatchedMousePositions);
			}
			finally { sender.AbortEventArray(); }
		}

		[Test, Category("Input")]
		public void NestedSendStartFailure()
		{
			var state = new MacKeyboardState();
			var sender = new RecordingSender(s, state);
			var hook = (UnixHookThread)s.HookThread;
			sender.StartSend();
			try
			{
				_ = state.ApplyFlagsChanged(VK_LCONTROL, MacNativeInput.kCGEventFlagMaskControl,
					MacKeyboardState.Origin.PhysicalHid, true, MacNativeInput.InjectedEventKind.None);
				sender.FailNextModifierQuery = true;
				Assert.Throws<InvalidOperationException>(sender.StartSend);
				sender.FinishSend();
				Assert.IsTrue(hook.SendInProgress, "failed initialization closed the outer send scope");
				Assert.AreEqual(0u, state.GetModifiers(() => 0), "failed initialization ended the outer keyboard transaction");
			}
			finally { sender.FinishSend(); }
			Assert.IsFalse(hook.SendInProgress);
			Assert.AreEqual(MOD_LCONTROL, state.GetModifiers(() => 0));
		}

		[Test, Category("Input")]
		public void NestedTextFrames()
		{
			var sender = new RecordingSender(s);
			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, 0);
			sender.SendUnicodeChar('\uD83D', 0);
			sender.SendUnicodeChar('\uDE00', 0);
			sender.PutKeybdEventIntoArray(0, 0, 'é', (uint)KEYEVENTF_UNICODE, 0);
			sender.PutKeybdEventIntoArray(0, 0, 'é', (uint)(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP), 0);

			sender.InitEventArray(4, 0);
			sender.SendUnicodeChar('x', 0);
			var finalDelay = -1L;
			sender.SendEventArray(ref finalDelay, 0);
			Assert.AreEqual("x", sender.DispatchedText);
			sender.AbortEventArray();
			sender.SendEventArray(ref finalDelay, 0);
			Assert.AreEqual("😀é", sender.DispatchedText);
			sender.CleanupEventArray(-1);

			sender.SetMode(SendModes.Input);
			sender.InitEventArray(4, 0);
			sender.SendUnicodeChar('y', 0);
			sender.SendEventArray(ref finalDelay, 0);
			Assert.AreEqual("y", sender.DispatchedText, "a reused frame retained text from an earlier send");
			sender.AbortEventArray();
		}

		[Test, Category("Input")]
		public void ArrayFrameCleanup()
		{
			var sender = new RecordingSender(s);
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
			var sender = new RecordingSender(s);
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
