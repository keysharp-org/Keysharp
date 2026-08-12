using Keysharp.Builtins;
using Keysharp.Internals.Input;
using Keysharp.Internals.Input.Hooks;
using Keysharp.Internals.Threading;
using Keysharp.Internals.Window;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	[TestFixture, NonParallelizable, Category("Internal"), Category("Curated")]
	public class InputHookTests : TestRunner
	{
		// Joined with '|' so the exact phrase list (including order and embedded commas) is compared.
		private static string MatchListOf(string matchList)
		{
			var io = (InputHook)new InputHook("", "", matchList);
			return string.Join("|", io.input.match);
		}

		// Bug fix: the match list is parsed faithfully like AHK's input_type::SetMatchList.
		// Two consecutive commas are a single literal comma; empty phrases are omitted; and an
		// empty match list yields no phrases (previously it produced a spurious "," phrase that
		// silently terminated every InputHook on a typed comma).
		[Test, Category("InputHook")]
		public void MatchListParsing()
		{
			Assert.AreEqual("", MatchListOf(""));                       // no phrases (was a spurious ",")
			Assert.AreEqual("abc", MatchListOf("abc"));
			Assert.AreEqual("abc|def", MatchListOf("abc,def"));
			Assert.AreEqual("abc", MatchListOf("abc,"));               // trailing comma omitted
			Assert.AreEqual("ab,cd", MatchListOf("ab,,cd"));           // double comma -> literal comma
			Assert.AreEqual("single,item", MatchListOf("single,,item"));     // doc example
			Assert.AreEqual("string1,|string2", MatchListOf("string1,,,string2")); // doc example
			Assert.AreEqual("btw|otoh|fl", MatchListOf("btw,otoh,fl"));
		}

		// Bug fix: a trailing I/L/T option (the last char of the options string) must not throw;
		// AHK reads the C-string null terminator safely and defaults the value.
		[Test, Category("InputHook")]
		public void TrailingNumericOption()
		{
			Assert.DoesNotThrow(() => new InputHook("I"));
			Assert.DoesNotThrow(() => new InputHook("L"));
			Assert.DoesNotThrow(() => new InputHook("T"));

			var i = (InputHook)new InputHook("I");
			Assert.AreEqual(1L, i.MinSendLevel); // 'I' with no number -> level 1

			var l = (InputHook)new InputHook("L");
			Assert.AreEqual(0, l.input.bufferLengthMax); // 'L' with no number -> 0
		}

		// Mouse-event support: VisibleMouseMove defaults to true (movement passes through), and
		// InputType.MouseIsNeeded becomes true once movement is being suppressed or a mouse button
		// carries Input key options (e.g. an end key). MouseIsNeeded is what makes Start() install
		// the low-level mouse hook in addition to the keyboard hook.
		[Test, Category("InputHook")]
		public void MouseHookNeed()
		{
			var io = (InputHook)new InputHook("");
			Assert.AreEqual(true, io.VisibleMouseMove); // default: movement passes through
			Assert.IsFalse(io.input.MouseIsNeeded);     // keyboard-only hook needs no mouse hook

			io.VisibleMouseMove = false;                // suppressing movement requires the mouse hook
			Assert.AreEqual(false, io.VisibleMouseMove);
			Assert.IsTrue(io.input.MouseIsNeeded);

			var io2 = (InputHook)new InputHook("");
			Assert.IsFalse(io2.input.MouseIsNeeded);
			io2.KeyOpt("{LButton}", "+E");              // LButton as an end key also needs the mouse hook
			Assert.IsTrue(io2.input.MouseIsNeeded);
		}

		// InputType.KeyboardIsNeeded gates installing the keyboard hook, mirroring MouseIsNeeded for the
		// mouse hook. A default (text-suppressing) input needs it; a purely-mouse visible observer does not.
		[Test, Category("InputHook")]
		public void KeyboardHookNeed()
		{
			var def = (InputHook)new InputHook("");  // default options suppress typed text
			Assert.IsTrue(def.input.KeyboardIsNeeded);   // ...so the keyboard hook is required
			Assert.IsFalse(def.input.MouseIsNeeded);

			var vis = (InputHook)new InputHook("V"); // visible: no text suppression
			Assert.IsTrue(vis.input.KeyboardIsNeeded);   // still a keyboard collector (not mouse-only)

			vis.VisibleMouseMove = false;                // now a pure mouse observer
			Assert.IsTrue(vis.input.MouseIsNeeded);
			Assert.IsFalse(vis.input.KeyboardIsNeeded);  // ...so the keyboard hook is no longer needed

			var ek = (InputHook)new InputHook("V", "{Enter}"); // visible + a keyboard end key
			Assert.IsTrue(ek.input.KeyboardIsNeeded);    // keyboard end key keeps the keyboard hook
		}

		[Test, Category("InputHook")]
		public void MouseEventPosition()
		{
			var mouse = new HookEventInfo(123, false, false, 0, null, 7, new POINT(321, 654));
			s.Threads.CurrentThread.eventInfo = (Func<object>)mouse.BuildEventInfo;

			var visible = ThreadAccessors.A_EventInfo;
			Assert.AreEqual(321L, Script.GetPropertyValue(visible, "X"));
			Assert.AreEqual(654L, Script.GetPropertyValue(visible, "Y"));
			Assert.IsFalse(Script.GetPropertyValue(visible, "IsAutoRepeat").Ab());

			var keyboard = new HookEventInfo(456, false, false, 0, null, 8);
			s.Threads.CurrentThread.eventInfo = (Func<object>)keyboard.BuildEventInfo;
			visible = ThreadAccessors.A_EventInfo;
			Assert.AreEqual(0L, KeysharpObject.HasOwnProp(visible, "X"));
			Assert.AreEqual(0L, KeysharpObject.HasOwnProp(visible, "Y"));
		}

		[Test, Category("InputHook"), Category("Misc")]
		public void MouseMoveQueue()
		{
			var context = UseQueuedMainContext();
			var calls = new List<(long dx, long dy, object info)>();
			var io = (InputHook)new InputHook("");
			io.OnMouseMove = new KeysharpFunc((Func<object, object, object, object>)((_, dx, dy) =>
			{
				calls.Add((dx.Al(), dy.Al(), ThreadAccessors.A_EventInfo));
				return 0L;
			}));

			var previous = s.input;
			io.input.Start();
			io.input.prev = previous;
			s.input = io.input;

			try
			{
				Assert.IsTrue(s.HookThread.CollectMouseMove(0, 0, 0, false, 10, new POINT(20, 30)));
				Assert.IsTrue(s.HookThread.CollectMouseMove(4, -2, 0, true, 11, deviceId: 2, isAbsolute: false));
				Assert.IsTrue(s.HookThread.CollectMouseMove(0, 0, 0, true, 12, deviceId: 0, isAbsolute: true));
				Assert.IsEmpty(calls);
				context.DrainAll();

				Assert.That(calls.Select(c => (c.dx, c.dy)), Is.EqualTo(new[] { (0L, 0L), (4L, -2L), (0L, 0L) }));
				Assert.AreEqual(20L, Script.GetPropertyValue(calls[0].info, "X"));
				Assert.AreEqual(30L, Script.GetPropertyValue(calls[0].info, "Y"));
				Assert.AreEqual(0L, KeysharpObject.HasOwnProp(calls[0].info, "DeviceId"));
				Assert.AreEqual(0L, KeysharpObject.HasOwnProp(calls[0].info, "IsAbsolute"));
				Assert.AreEqual(0L, KeysharpObject.HasOwnProp(calls[1].info, "X"));
				Assert.AreEqual(2L, Script.GetPropertyValue(calls[1].info, "DeviceId"));
				Assert.IsFalse(Script.GetPropertyValue(calls[1].info, "IsAbsolute").Ab());
				Assert.AreEqual(0L, Script.GetPropertyValue(calls[2].info, "DeviceId"));
				Assert.IsTrue(Script.GetPropertyValue(calls[2].info, "IsAbsolute").Ab());
				Assert.AreEqual(12L, Script.GetPropertyValue(calls[2].info, "Timestamp"));
				Assert.IsTrue(Script.GetPropertyValue(calls[2].info, "IsInjected").Ab());
			}
			finally
			{
				s.input = previous;
				io.input.prev = null;
			}
		}
	}
}
