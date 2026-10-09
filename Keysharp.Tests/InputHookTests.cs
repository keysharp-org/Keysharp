namespace Keysharp.Tests;

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
		Assert.That(MatchListOf(""), Is.Empty);                       // no phrases (was a spurious ",")
		Assert.That(MatchListOf("abc"), Is.EqualTo("abc"));
		Assert.That(MatchListOf("abc,def"), Is.EqualTo("abc|def"));
		Assert.That(MatchListOf("abc,"), Is.EqualTo("abc"));               // trailing comma omitted
		Assert.That(MatchListOf("ab,,cd"), Is.EqualTo("ab,cd"));           // double comma -> literal comma
		Assert.That(MatchListOf("single,,item"), Is.EqualTo("single,item"));     // doc example
		Assert.That(MatchListOf("string1,,,string2"), Is.EqualTo("string1,|string2")); // doc example
		Assert.That(MatchListOf("btw,otoh,fl"), Is.EqualTo("btw|otoh|fl"));
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
			_ = io.KeyOpt(keys, "S");

			foreach (var key in expected)
			{
				var vk = (int)Keyboard.GetKeyVK(key.ToString());
				Assert.That(vk, Is.Not.EqualTo(0), $"{keys}: {key} has a virtual key");
				Assert.That(io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS), $"{keys}: {key} is suppressed");
			}
		}

		var named = (InputHook)new InputHook("V");
		_ = named.KeyOpt("{Delete}{Home}{End}", "S");

		foreach (var key in new[] { "Delete", "Home", "End" })
		{
			var vk = (int)Keyboard.GetKeyVK(key);
			var sc = (int)Keyboard.GetKeySC(key);
			Assert.That((named.input.keyVK[vk] | named.input.keySC[sc]) & HookThread.INPUT_KEY_SUPPRESS,
				Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS), $"{key} is suppressed");
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
			_ = io.KeyOpt(keys, "S");
			var keyboardFlags = keyboard ? HookThread.INPUT_KEY_SUPPRESS : 0u;
			var mouseFlags = mouse ? HookThread.INPUT_KEY_SUPPRESS : 0u;
			Assert.That(io.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(keyboardFlags), keys);
			Assert.That(io.input.keyVK[VK_CANCEL] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(keyboardFlags), keys);
			Assert.That(io.input.keySC[0] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(keyboardFlags), keys);
			Assert.That(io.input.MouseIsNeeded, Is.EqualTo(mouse), keys);

			foreach (var vk in mouseVks)
				Assert.That(io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(mouseFlags), $"{keys}: {vk:X2}");
		}

		var mixed = (InputHook)new InputHook("V");
		_ = mixed.KeyOpt("a{Mouse}", "S");
		Assert.That(mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS));
		Assert.That(mixed.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS));
		_ = mixed.KeyOpt("{All}", "Z");
		Assert.That(mixed.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(0u));
		Assert.That(mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS));
		_ = mixed.KeyOpt("{Mouse}", "E+V");
		Assert.That(mixed.input.keyVK[VK_LBUTTON] & HookThread.END_KEY_ENABLED, Is.EqualTo(HookThread.END_KEY_ENABLED));
		Assert.That(mixed.input.keyVK[VK_WHEEL_UP] & HookThread.INPUT_KEY_VISIBILITY_MASK, Is.EqualTo(HookThread.INPUT_KEY_VISIBLE));
		_ = mixed.KeyOpt("{Mouse}", "Z");
		Assert.That(mixed.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_OPTION_MASK, Is.EqualTo(0u));

		foreach (var option in new[] { "I", "-N" })
		{
			var rejected = (InputHook)new InputHook("V");
			var error = Assert.Throws<KeysharpException>(() => rejected.KeyOpt("a{Keyboard}{Mouse}", $"S{option}"));
			Assert.IsInstanceOf<ValueError>(error.UserError);
			Assert.That(rejected.input.keyVK[keyboardVk] & HookThread.INPUT_KEY_OPTION_MASK, Is.EqualTo(0u), option);
			Assert.That(rejected.input.keyVK[VK_LBUTTON] & HookThread.INPUT_KEY_OPTION_MASK, Is.EqualTo(0u), option);
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
				_ = io.KeyOpt("{Mouse}", option);

			var previous = s.input;
			io.input.Start();
			io.input.prev = previous;
			s.input = io.input;

			try
			{
				Assert.That(s.HookThread.CollectMouseInput(0, VK_LBUTTON, false, 0, 0, null, false), Is.EqualTo(visible), $"Mouse down: {option}");
				Assert.That(s.HookThread.CollectMouseInput(0, VK_LBUTTON, true, 0, 0, null, false), Is.EqualTo(visible), $"Mouse up: {option}");
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
			Assert.That(endKeys.input.endChars, Is.EqualTo(expected), $"{keys}: end characters");

			var io = (InputHook)new InputHook("V");
			_ = io.KeyOpt(keys, "S");

			foreach (var key in expected)
			{
				var vk = (int)Keyboard.GetKeyVK(key.ToString());

				if (vk == 0 && (key == '{' || key == '}'))
					continue;

				Assert.That(vk, Is.Not.EqualTo(0), $"{keys}: {key} has a virtual key");
				Assert.That(io.input.keyVK[vk] & HookThread.INPUT_KEY_SUPPRESS, Is.EqualTo(HookThread.INPUT_KEY_SUPPRESS), $"{keys}: {key} is suppressed");
			}
		}
	}

	// Mouse-event support: VisibleMouseMove defaults to true (movement passes through), and
	// InputType.MouseIsNeeded becomes true once movement is being suppressed or a mouse button
	// carries Input key options (e.g. an end key). MouseIsNeeded is what makes Start() install
	// the low-level mouse hook in addition to the keyboard hook.
	[Test, Category("InputHook")]
	public void MouseHookNeed()
	{
		var io = (InputHook)new InputHook("");
		Assert.That(io.VisibleMouseMove, Is.EqualTo(true)); // default: movement passes through
		Assert.That(io.input.MouseIsNeeded, Is.False);     // keyboard-only hook needs no mouse hook

		io.VisibleMouseMove = false;                // suppressing movement requires the mouse hook
		Assert.That(io.VisibleMouseMove, Is.EqualTo(false));
		Assert.IsTrue(io.input.MouseIsNeeded);

		var io2 = (InputHook)new InputHook("");
		Assert.That(io2.input.MouseIsNeeded, Is.False);
		_ = io2.KeyOpt("{LButton}", "+E");              // LButton as an end key also needs the mouse hook
		Assert.IsTrue(io2.input.MouseIsNeeded);
	}

	// InputType.KeyboardIsNeeded gates installing the keyboard hook, mirroring MouseIsNeeded for the
	// mouse hook. A default (text-suppressing) input needs it; a purely-mouse visible observer does not.
	[Test, Category("InputHook")]
	public void KeyboardHookNeed()
	{
		var def = (InputHook)new InputHook("");  // default options suppress typed text
		Assert.IsTrue(def.input.KeyboardIsNeeded);   // ...so the keyboard hook is required
		Assert.That(def.input.MouseIsNeeded, Is.False);

		var vis = (InputHook)new InputHook("V"); // visible: no text suppression
		Assert.IsTrue(vis.input.KeyboardIsNeeded);   // still a keyboard collector (not mouse-only)

		vis.VisibleMouseMove = false;                // now a pure mouse observer
		Assert.IsTrue(vis.input.MouseIsNeeded);
		Assert.That(vis.input.KeyboardIsNeeded, Is.False);  // ...so the keyboard hook is no longer needed

		var ek = (InputHook)new InputHook("V", "{Enter}"); // visible + a keyboard end key
		Assert.IsTrue(ek.input.KeyboardIsNeeded);    // keyboard end key keeps the keyboard hook
	}

	[Test, Category("InputHook")]
	public void MouseEventPosition()
	{
		var mouse = new HookEventInfo(123, false, false, 0, null, 7, new POINT(321, 654));
		s.Threads.CurrentThread.eventInfo = (Func<object>)mouse.BuildEventInfo;

		var visible = ThreadAccessors.A_EventInfo;
		Assert.That(Script.GetPropertyValue(visible, "X"), Is.EqualTo(321L));
		Assert.That(Script.GetPropertyValue(visible, "Y"), Is.EqualTo(654L));
		Assert.That(Script.GetPropertyValue(visible, "IsAutoRepeat").Ab(), Is.False);

		var keyboard = new HookEventInfo(456, false, false, 0, null, 8);
		s.Threads.CurrentThread.eventInfo = (Func<object>)keyboard.BuildEventInfo;
		visible = ThreadAccessors.A_EventInfo;
		Assert.That(KeysharpObject.HasOwnProp(visible, "X"), Is.EqualTo(0L));
		Assert.That(KeysharpObject.HasOwnProp(visible, "Y"), Is.EqualTo(0L));
	}

	[Test, Category("InputHook"), Category("Misc")]
	public void MouseMoveQueue()
	{
		var context = UseQueuedMainContext();
		var calls = new List<(long dx, long dy, object info)>();
		var io = (InputHook)new InputHook("");
		io.OnMouseMove = new KeysharpFunc((Func<object, object, object, object>)((_, dx, dy) =>
		{
			_ = dx.TryCoerceLong(out var x);
			_ = dy.TryCoerceLong(out var y);
			calls.Add((x, y, ThreadAccessors.A_EventInfo));
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
			Assert.That(Script.GetPropertyValue(calls[0].info, "X"), Is.EqualTo(20L));
			Assert.That(Script.GetPropertyValue(calls[0].info, "Y"), Is.EqualTo(30L));
			Assert.That(KeysharpObject.HasOwnProp(calls[0].info, "DeviceId"), Is.EqualTo(0L));
			Assert.That(KeysharpObject.HasOwnProp(calls[0].info, "IsAbsolute"), Is.EqualTo(0L));
			Assert.That(KeysharpObject.HasOwnProp(calls[1].info, "X"), Is.EqualTo(0L));
			Assert.That(Script.GetPropertyValue(calls[1].info, "DeviceId"), Is.EqualTo(2L));
			Assert.That(Script.GetPropertyValue(calls[1].info, "IsAbsolute").Ab(), Is.False);
			Assert.That(Script.GetPropertyValue(calls[2].info, "DeviceId"), Is.EqualTo(0L));
			Assert.IsTrue(Script.GetPropertyValue(calls[2].info, "IsAbsolute").Ab());
			Assert.That(Script.GetPropertyValue(calls[2].info, "Timestamp"), Is.EqualTo(12L));
			Assert.IsTrue(Script.GetPropertyValue(calls[2].info, "IsInjected").Ab());
		}
		finally
		{
			s.input = previous;
			io.input.prev = null;
		}
	}

	/// <summary>A running input has no end reason yet, and says so with "" rather than unset, so
	/// <c>!ih.EndReason</c> is safe to write while it runs. It used to read null and raise UnsetError.</summary>
	[Test, Category("InputHook")]
	public void RunningHookReportsNoEndReason()
	{
		var io = (InputHook)new InputHook("");

		io.input.Start();
		Assert.IsTrue(io.InProgress);
		Assert.That(io.EndReason, Is.Empty);
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
			Assert.That(calls, Is.EqualTo(1));

			Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 12, deviceId: 1, isAbsolute: false));
			io.input.status = InputStatusType.Off;
			context.DrainAll();
			Assert.That(calls, Is.EqualTo(1), "A notification queued before the input ended is discarded.");

			io.input.Start();
			Assert.IsTrue(s.HookThread.CollectMouseMove(1, 1, 0, true, 13, deviceId: 1, isAbsolute: false));
			io.input.status = InputStatusType.Off;
			io.input.Start();
			context.DrainAll();
			Assert.That(calls, Is.EqualTo(2), "One queued before an end is delivered if the input is started again first.");
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
			Assert.That(io.input.InputUnlinkIfStopped(io.input), Is.SameAs(io.input), "A running input is found.");
			Assert.That(s.input, Is.SameAs(io.input), "A running input stays linked.");

			io.input.status = InputStatusType.Off;
			Assert.That(io.input.InputUnlinkIfStopped(io.input), Is.SameAs(io.input), "An ended input is found.");
			Assert.That(s.input, Is.SameAs(previous), "An ended input is unlinked.");

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
			Assert.That(io.EndReason, Is.EqualTo("Stopped"));
			context.DrainAll();

			Assert.That(slot.IsActive, Is.False, "The persistence root is released with no OnEnd to run.");
			Assert.That(s.input, Is.Not.SameAs(io.input), "The input is unlinked.");
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
			Assert.That(s.input, Is.SameAs(io.input), "The restarted input stays linked.");
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
		Assert.That(io.input.beforeHotkeys, Is.False, "An ended input takes the change.");
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
			Assert.That(s.inputBeforeHotkeysCount, Is.EqualTo(places + 1), "A running before-hotkeys input takes a place.");
			io.input.EndByTimeout();
			io.input.Stop();
			Assert.That(io.EndReason, Is.EqualTo("Timeout"), "The first reason stands.");
			Assert.That(s.inputBeforeHotkeysCount, Is.EqualTo(places), "The place is given back once.");

			// Started again before the end runs, so every queued end finds the input linked and runs OnEnd: one end
			// runs it once, where a second would run it again.
			Assert.IsTrue(io.input.LinkForStart(), "The ended input starts again.");
			context.DrainAll();
			Assert.That(ends, Is.EqualTo(1), "One end is queued, so OnEnd runs once.");
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
			Assert.That(io.input.LinkForStart(), Is.False, "A running input is not linked twice.");

			context.DrainAll();
			Assert.That(ends, Is.EqualTo(1), "The restarted input's queued end runs OnEnd.");
			Assert.That(s.input, Is.SameAs(io.input), "The restarted input stays linked.");
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
