using Keysharp.Builtins;
using Keysharp.Internals.Input;
using Keysharp.Internals.Input.Hooks;
using Keysharp.Internals.Threading;
using Keysharp.Internals.Window;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keyboard = Keysharp.Builtins.Keyboard;

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

		[Test, Category("InputHook")]
		public void KeyOptParsesSingleCharacterBracedKeys()
		{
			foreach (var (keys, expected) in new[]
			{
				("{1}", "1"),
				("{a}", "a"),
				("{1}{2}{3}", "123"),
				("{a}b{c}", "abc"),
				("4{5}6", "456"),
				("d{e}{f}", "def"),
				("ghi", "ghi")
			})
			{
				var io = (InputHook)new InputHook("V");
				io.KeyOpt(keys, "S");

				foreach (var key in expected)
				{
					var vk = (int)Keyboard.GetKeyVK(key.ToString());
					Assert.AreNotEqual(0, vk, $"{keys}: {key} has a virtual key");
					Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS, io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, $"{keys}: {key} is suppressed");
				}
			}

			var named = (InputHook)new InputHook("V");
			named.KeyOpt("{Delete}{Insert}{End}", "S");

			foreach (var key in new[] { "Delete", "Insert", "End" })
			{
				var vk = (int)Keyboard.GetKeyVK(key);
				var sc = (int)Keyboard.GetKeySC(key);
				Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS,
					(named.input.keyVK[vk] | named.input.keySC[sc]) & HookThread.INPUT_KEY_SUPPRESS, $"{key} is suppressed");
			}
		}

		[Test, Category("InputHook")]
		public void KeyOptGroupSelectors()
		{
			var keyboardVk = (int)Keyboard.GetKeyVK("a");
			var mouseVks = new[] { VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1, VK_XBUTTON2,
				VK_WHEEL_LEFT, VK_WHEEL_RIGHT, VK_WHEEL_DOWN, VK_WHEEL_UP };
			foreach (var (keys, keyboard, mouse) in new[]
			{
				("{All}", true, false), ("{Keyboard}", true, false),
				("{Mouse}", false, true), ("{keyboard}{MOUSE}", true, true),
				("{{Keyboard}}", false, false), ("{{Mouse}}", false, false)
			})
			{
				var io = (InputHook)new InputHook("V");
				io.KeyOpt(keys, "S");
				var keyboardFlags = keyboard ? HookThread.INPUT_KEY_SUPPRESS : 0u;
				var mouseFlags = mouse ? HookThread.INPUT_KEY_SUPPRESS : 0u;
				Assert.AreEqual(keyboardFlags, io.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS, keys);
				Assert.AreEqual(keyboardFlags, io.input.keyVK[VK_CANCEL] & HookThread.INPUT_KEY_SUPPRESS, keys);
				Assert.AreEqual(keyboardFlags, io.input.keySC[0] & HookThread.INPUT_KEY_SUPPRESS, keys);
				Assert.AreEqual(mouse, io.input.MouseIsNeeded, keys);

				foreach (var vk in mouseVks)
					Assert.AreEqual(mouseFlags, io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, $"{keys}: {vk:X2}");
			}

			var mixed = (InputHook)new InputHook("V");
			mixed.KeyOpt("a{Mouse}", "S");
			Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS, mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_SUPPRESS);
			Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS, mixed.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS);
			mixed.KeyOpt("{All}", "Z");
			Assert.AreEqual(0u, mixed.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS);
			Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS, mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_SUPPRESS);
			mixed.KeyOpt("{Mouse}", "E+V");
			Assert.AreEqual(HookThread.END_KEY_ENABLED, mixed.input.keyVK[VK_LBUTTON] & HookThread.END_KEY_ENABLED);
			Assert.AreEqual(HookThread.INPUT_KEY_VISIBLE, mixed.input.keyVK[VK_WHEEL_UP] & HookThread.INPUT_KEY_VISIBILITY_MASK);
			mixed.KeyOpt("{Mouse}", "Z");
			Assert.AreEqual(0u, mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_OPTION_MASK);

			foreach (var option in new[] { "I", "-N" })
			{
				var rejected = (InputHook)new InputHook("V");
				var error = Assert.Throws<KeysharpException>(() => rejected.KeyOpt("a{Keyboard}{Mouse}", $"S{option}"));
				Assert.IsInstanceOf<ValueError>(error.UserError);
				Assert.AreEqual(0u, rejected.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_OPTION_MASK, option);
				Assert.AreEqual(0u, rejected.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_OPTION_MASK, option);
			}
		}

		[Test, Category("InputHook")]
		public void MouseVisibilityDoesNotInheritVisibleNonText()
		{
			foreach (var (option, visible) in new[] { ("", true), ("S", false) })
			{
				var io = (InputHook)new InputHook("V");
				io.VisibleNonText = false;

				if (option.Length != 0)
					io.KeyOpt("{Mouse}", option);

				var previous = s.input;
				io.input.Start();
				io.input.prev = previous;
				s.input = io.input;

				try
				{
					Assert.AreEqual(visible, s.HookThread.CollectMouseInput(0, VK_LBUTTON, false, 0, 0, null, false), $"Mouse down: {option}");
					Assert.AreEqual(visible, s.HookThread.CollectMouseInput(0, VK_LBUTTON, true, 0, 0, null, false), $"Mouse up: {option}");
				}
				finally
				{
					s.input = previous;
					io.input.prev = null;
				}
			}
		}

		[Test, Category("InputHook")]
		public void LiteralBraceKeyNames()
		{
			foreach (var (keys, expected) in new[]
			{
				("{{}", "{"),
				("{}}", "}"),
				("{{}{}}", "{}"),
				("{1}{{}{}}{a}", "1{}a"),
				("{{}}", "{"),
				("{}", "")
			})
			{
				var endKeys = (InputHook)new InputHook("E", keys);
				Assert.AreEqual(expected, endKeys.input.endChars, $"{keys}: end characters");

				var io = (InputHook)new InputHook("V");
				io.KeyOpt(keys, "S");

				foreach (var key in expected)
				{
					var vk = (int)Keyboard.GetKeyVK(key.ToString());

					if (vk == 0 && (key == '{' || key == '}'))
						continue;

					Assert.AreNotEqual(0, vk, $"{keys}: {key} has a virtual key");
					Assert.AreEqual(HookThread.INPUT_KEY_SUPPRESS, io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, $"{keys}: {key} is suppressed");
				}
			}
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

		/// <summary>A fresh InputHook is idle and reads EndReason "Stopped", as in AHK, where one Off status covers both
		/// a hook that never ran and one that was stopped.</summary>
		[Test, Category("InputHook")]
		public void FreshHookReadsStopped()
		{
			var io = (InputHook)new InputHook("");
			Assert.IsFalse(io.InProgress);
			Assert.AreEqual("Stopped", io.EndReason, "A never-started input reads Stopped, as in AHK.");
			Assert.IsInstanceOf<Ks.EventHook>(io, "InputHook shares the hook vocabulary with every event family.");

			_ = io.Stop();
			Assert.AreEqual("Stopped", io.EndReason, "Stop on a never-started hook does nothing.");
			Assert.IsFalse(io.InProgress);
		}

		/// <summary>A running input has no end reason yet, and says so with "" rather than unset, so
		/// <c>!ih.EndReason</c> is safe to write while it runs. It used to read null and raise UnsetError.</summary>
		[Test, Category("InputHook")]
		public void RunningHookReportsNoEndReason()
		{
			var io = (InputHook)new InputHook("");

			io.input.Start();
			Assert.IsTrue(io.InProgress);
			Assert.AreEqual("", io.EndReason);
		}

		/// <summary>A callback property takes a callback the input can call with its arguments, as AHK's ValidateFunctor
		/// checks, and "" or unset clears it; a refused value leaves the property as it was.</summary>
		[Test, Category("InputHook")]
		public void CallbackPropertiesValidateAndClear()
		{
			var io = (InputHook)new InputHook("");
			var onChar = new KeysharpFunc((Func<object, object, object>)((_, ch) => 0L));
			io.OnChar = onChar;
			Assert.AreSame(onChar, io.OnChar);

			Assert.IsInstanceOf<ValueError>(Assert.Throws<KeysharpException>(() => io.OnEnd = onChar).UserError, "OnEnd is called with one argument.");
			Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => io.OnKeyDown = "not a function").UserError);
			Assert.IsNotInstanceOf<KeysharpFunc>(io.OnEnd);
			Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => io.OnChar = "not a function").UserError);
			Assert.IsInstanceOf<MethodError>(Assert.Throws<KeysharpException>(() => io.OnChar = new KeysharpObject()).UserError);
			Assert.AreSame(onChar, io.OnChar, "A refused value leaves the callback in place.");

			io.OnChar = null;
			Assert.IsNotInstanceOf<KeysharpFunc>(io.OnChar, "Unset clears a callback, as in AHK.");
		}

		/// <summary>The documented argument-less <c>ih.Wait()</c> used to raise a missing-argument error.</summary>
		[Test, Category("InputHook")]
		public void WaitTakesNoArgument()
		{
			var io = (InputHook)new InputHook("");
			Assert.AreEqual("Stopped", io.Wait(), "Waiting on a hook that is not running returns at once with its reason.");
		}

		/// <summary>
		/// Stopping takes effect at the call: a notification queued before it is discarded when it would run, unless the
		/// input has been started again by then. An ended input stays on the input stack until its end runs, which is why
		/// the test is the status, not membership.
		/// </summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void QueuedNotificationsAreDiscardedAfterStop()
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
				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 11, deviceId: 1, isAbsolute: false));
				context.DrainAll();
				Assert.AreEqual(1, calls);

				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 12, deviceId: 1, isAbsolute: false));
				io.input.status = InputStatusType.Off;
				context.DrainAll();
				Assert.AreEqual(1, calls, "A notification queued before the input ended is discarded.");

				io.input.Start();
				Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 13, deviceId: 1, isAbsolute: false));
				io.input.status = InputStatusType.Off;
				io.input.Start();
				context.DrainAll();
				Assert.AreEqual(2, calls, "One queued before an end is delivered if the input is started again first.");
			}
			finally
			{
				s.input = previous;
				io.input.prev = null;
			}
		}

		/// <summary>
		/// An input restarted while its end was still queued stays linked when that end runs, as AHK's
		/// InputUnlinkIfStopped keeps it: InputStart has already moved it to the top. Unlinking it there left a restarted
		/// input reading InProgress while it collected nothing. An input that did end is unlinked, and one that is not in
		/// the chain is a stale end.
		/// </summary>
		[Test, Category("InputHook")]
		public void RestartBeforeTheEndRunsStaysLinked()
		{
			var io = (InputHook)new InputHook("");
			var previous = s.input;
			io.input.Start();
			io.input.prev = previous;
			s.input = io.input;

			try
			{
				Assert.AreSame(io.input, io.input.InputUnlinkIfStopped(io.input), "A running input is found.");
				Assert.AreSame(io.input, s.input, "A running input stays linked.");

				io.input.status = InputStatusType.Off;
				Assert.AreSame(io.input, io.input.InputUnlinkIfStopped(io.input), "An ended input is found.");
				Assert.AreSame(previous, s.input, "An ended input is unlinked.");

				Assert.IsNull(io.input.InputUnlinkIfStopped(io.input), "An input no longer in the chain is a stale end.");
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

		/// <summary>An input started again before its end runs keeps the roots and the link its new run needs: the
		/// end releases only an input that is not in progress, as AHK's InputUnlinkIfStopped keeps a restarted one.</summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void RestartBeforeTheEndKeepsTheRoots()
		{
			if (s.HookThread is not HookThread { kbdMsSender: not null })
				Assert.Ignore("No input sender in this host, so an input cannot be ended through the hook.");

			var context = UseQueuedMainContext();
			var io = (InputHook)new InputHook("");
			io.OnChar = new KeysharpFunc((Func<object, object, object>)((_, ch) => 0L));
			var slot = io.GetCallbackSlot(UserMessages.AHK_INPUT_CHAR);

			var previous = s.input;
			Assert.IsTrue(io.input.LinkForStart(), "The chain half of Start() links the input.");

			try
			{
				io.input.Stop();
				Assert.IsTrue(io.input.LinkForStart(), "An ended input starts again.");
				context.DrainAll();

				Assert.IsTrue(slot.IsActive, "The restarted input keeps its persistence roots.");
				Assert.AreSame(io.input, s.input, "The restarted input stays linked.");
			}
			finally
			{
				if (ReferenceEquals(s.input, io.input))
					s.input = previous;

				io.input.prev = null;
				io.DeactivateCallbackPersistence();
			}
		}

		/// <summary>A running input holds its place in the before-hotkeys count by its BeforeHotkeys setting, so the
		/// setting is fixed until the input ends.</summary>
		[Test, Category("InputHook")]
		public void BeforeHotkeysIsFixedWhileRunning()
		{
			var io = (InputHook)new InputHook("");
			io.BeforeHotkeys = true;
			io.input.Start();

			try
			{
				Assert.IsInstanceOf<ValueError>(Assert.Throws<KeysharpException>(() => io.BeforeHotkeys = false).UserError);
				Assert.IsTrue(io.input.beforeHotkeys, "A refused change leaves the setting as it was.");
			}
			finally
			{
				io.input.status = InputStatusType.Off;
			}

			io.BeforeHotkeys = false;
			Assert.IsFalse(io.input.beforeHotkeys, "An ended input takes the change.");
		}

		/// <summary>The first end wins, whichever thread gets there first: a later reason is ignored, one end is queued,
		/// and the before-hotkeys count gives back exactly the place the start took.</summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void FirstEndWinsAndGivesBackTheBeforeHotkeysPlace()
		{
			if (s.HookThread is not HookThread { kbdMsSender: not null })
				Assert.Ignore("No input sender in this host, so an input cannot be ended through the hook.");

			var context = UseQueuedMainContext();
			var ends = 0;
			var io = (InputHook)new InputHook("");
			io.BeforeHotkeys = true;
			io.OnEnd = new KeysharpFunc((Func<object, object>)(_ =>
			{
				ends++;
				return 0L;
			}));

			var previous = s.input;
			var places = s.inputBeforeHotkeysCount;
			Assert.IsTrue(io.input.LinkForStart(), "The chain half of Start() links the input.");

			try
			{
				Assert.AreEqual(places + 1, s.inputBeforeHotkeysCount, "A running before-hotkeys input takes a place.");
				io.input.EndByTimeout();
				io.input.Stop();
				Assert.AreEqual("Timeout", io.EndReason, "The first reason stands.");
				Assert.AreEqual(places, s.inputBeforeHotkeysCount, "The place is given back once.");

				// Started again before the end runs, so every queued end finds the input linked and runs OnEnd: one end
				// runs it once, where a second would run it again.
				Assert.IsTrue(io.input.LinkForStart(), "The ended input starts again.");
				context.DrainAll();
				Assert.AreEqual(1, ends, "One end is queued, so OnEnd runs once.");
			}
			finally
			{
				if (io.input.InProgress())
				{
					io.input.Stop();
					context.DrainAll();
				}

				if (ReferenceEquals(s.input, io.input))
					s.input = previous;

				io.input.prev = null;
				io.DeactivateCallbackPersistence();
			}
		}

		/// <summary>OnEnd runs for an input started again before its end is processed, as AHK's InputRelease finds the
		/// restarted input and runs it. Restarting relinks the input at the top without running script code, so the
		/// queued end still finds it in the chain.</summary>
		[Test, Category("InputHook"), Category("Misc")]
		public void OnEndRunsForAnInputRestartedBeforeItsEnd()
		{
			if (s.HookThread is not HookThread { kbdMsSender: not null })
				Assert.Ignore("No input sender in this host, so an input cannot be ended through the hook.");

			var context = UseQueuedMainContext();
			var ends = 0;
			var io = (InputHook)new InputHook("");
			io.OnEnd = new KeysharpFunc((Func<object, object>)(_ =>
			{
				ends++;
				return 0L;
			}));

			var previous = s.input;
			Assert.IsTrue(io.input.LinkForStart(), "The chain half of Start() links the input.");

			try
			{
				io.input.Stop();
				Assert.IsTrue(io.input.LinkForStart(), "An ended input starts again.");
				Assert.IsFalse(io.input.LinkForStart(), "A running input is not linked twice.");

				context.DrainAll();
				Assert.AreEqual(1, ends, "The restarted input's queued end runs OnEnd.");
				Assert.AreSame(io.input, s.input, "The restarted input stays linked.");
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
