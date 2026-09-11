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

		/// <summary>
		/// A fresh InputHook is idle, not ended: EndReason is "" until something ends it. AutoHotkey reports
		/// "Stopped" here because its single Off status covers both cases; a hook that has never run has not ended.
		/// </summary>
		[Test, Category("InputHook")]
		public void FreshHookIsIdleNotEnded()
		{
			var io = (InputHook)new InputHook("");
			Assert.IsFalse(io.InProgress);
			Assert.AreEqual("", io.EndReason);
			Assert.IsInstanceOf<Ks.EventHook>(io, "InputHook shares the hook vocabulary with every event family.");

			_ = io.Stop();
			Assert.AreEqual("Stopped", io.EndReason, "Stop on a never-started hook ends it.");
			Assert.IsFalse(io.InProgress);
		}

		/// <summary>A running input has no end reason yet, and says so with "" rather than unset, so
		/// <c>!ih.EndReason</c> is safe to write while it runs. It used to read null and raise UnsetError.</summary>
		[Test, Category("InputHook")]
		public void RunningAndPausedHooksReportNoEndReason()
		{
			var io = (InputHook)new InputHook("");

			io.input.Start();
			Assert.IsTrue(io.InProgress);
			Assert.AreEqual("", io.EndReason);

			io.input.status = InputStatusType.Paused;
			Assert.IsFalse(io.InProgress);
			Assert.AreEqual("", io.EndReason, "A paused input is idle, not ended.");
		}

		/// <summary>The documented argument-less <c>ih.Wait()</c> used to raise a missing-argument error.</summary>
		[Test, Category("InputHook")]
		public void WaitTakesNoArgument()
		{
			var io = (InputHook)new InputHook("");
			Assert.AreEqual("", io.Wait(), "Waiting on a hook that is not running returns at once with its reason.");
		}

		/// <summary>
		/// Pausing and stopping take effect at the call: a notification queued before either is discarded when it
		/// would run. A paused input stays on the input stack, which is why the test is the status, not membership.
		/// </summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void QueuedNotificationsAreDiscardedAfterPauseOrStop()
		{
			var context = UseQueuedMainContext();
			var calls = 0;
			var io = (InputHook)new InputHook("");
			io.OnMouseMove = new KeysharpFunc((Func<object, object, object, object>)((_, dx, dy) =>
			{
				calls++;
				return 0L;
			}));

			var previous = s.input;
			io.input.Start();
			io.input.prev = previous;
			s.input = io.input;

			try
			{
				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 10, deviceId: 1, isAbsolute: false));
				_ = io.Pause();
				context.DrainAll();
				Assert.AreEqual(0, calls, "A notification queued before Pause is discarded while paused.");
				Assert.AreSame(io.input, s.input, "Pausing leaves the input linked where it stood.");

				io.input.status = InputStatusType.InProgress;
				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 11, deviceId: 1, isAbsolute: false));
				context.DrainAll();
				Assert.AreEqual(1, calls);

				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 12, deviceId: 1, isAbsolute: false));
				io.input.status = InputStatusType.Off;
				context.DrainAll();
				Assert.AreEqual(1, calls, "A notification queued before the input ended is discarded.");
			}
			finally
			{
				s.input = previous;
				io.input.prev = null;
			}
		}

		/// <summary>
		/// Ending an input unlinks it and releases the persistence roots its start took, whether or not it has an
		/// OnEnd. Both used to be skipped for an input with OnChar but no OnEnd, which kept a script that was
		/// otherwise done running forever.
		/// </summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void EndingWithoutOnEndStillReleases()
		{
			if (s.HookThread is not HookThread { kbdMsSender: not null })
				Assert.Ignore("No input sender in this host, so an input cannot be ended through the hook.");

			var context = UseQueuedMainContext();
			// Releasing the last input rightly checks whether the script is done, and this fixture has nothing else
			// keeping it alive, so without this the check would tear it down under the assertions.
			s.FlowData.persistentValueSetByUser = true;
			var io = (InputHook)new InputHook("");
			io.OnChar = new KeysharpFunc((Func<object, object, object>)((_, ch) => 0L));
			var slot = io.GetCallbackSlot(UserMessages.AHK_INPUT_CHAR);

			var previous = s.input;
			io.input.Start();
			io.input.prev = previous;
			s.input = io.input;
			io.ActivateCallbackPersistence();

			try
			{
				Assert.IsTrue(slot.IsActive, "Starting roots the input through its callbacks.");
				io.input.Stop();
				Assert.AreEqual("Stopped", io.EndReason);
				context.DrainAll();

				Assert.IsFalse(slot.IsActive, "The persistence root is released with no OnEnd to run.");
				Assert.AreNotSame(io.input, s.input, "The input is unlinked.");
			}
			finally
			{
				if (ReferenceEquals(s.input, io.input))
					s.input = previous;

				io.input.prev = null;
				io.DeactivateCallbackPersistence();
			}
		}
	}
}
