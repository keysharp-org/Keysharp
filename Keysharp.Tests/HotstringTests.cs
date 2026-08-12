using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using Keyboard = Keysharp.Builtins.Keyboard;

namespace Keysharp.Tests
{
	/// <summary>
	/// All hotstring tests must be run sequentially, hence the usage of lock (syncroot).
	/// </summary>
	public partial class HotstringTests : TestRunner
	{
		private static bool btwtyped = false;
		private static readonly ManualResetEventSlim btwTypedEvent = new(false);
		private QueuedSynchronizationContext mainContext;

		private void SimulateKeyPress(uint key)
		{
			s.HookThread.SimulateKeyPress(key);
			PumpSchedulers();
		}

		private void PumpSchedulers()
		{
			mainContext?.DrainAll();
			Keysharp.Internals.Flow.TryDoEvents(s.EventScheduler, propagateExit: true, yieldTick: false, pumpUi: false);
			mainContext?.DrainAll();
			Keysharp.Internals.Flow.TryDoEvents(s.UIEventScheduler, propagateExit: true, yieldTick: false, pumpUi: false);
			mainContext?.DrainAll();
		}

		// The hotstring callback runs on a pseudo-thread, whose admission (IsInterruptible/AnyThreadsAvailable)
		// can be momentarily blocked when the trigger key is pumped. When that happens the queued event is put
		// back on the scheduler and must be pumped again once it becomes admissible. A real script keeps pumping
		// via its message loop; this mirrors that (and the WaitWithUiPump pattern in RealThreadTests)
		// instead of blocking on the event, which would never reprocess a momentarily-blocked event.
		private bool WaitForCallback(ManualResetEventSlim signal, int timeoutMs = 2000)
		{
			var deadline = Environment.TickCount64 + timeoutMs;

			while (!signal.IsSet)
			{
				if (Environment.TickCount64 >= deadline)
					return false;

				PumpSchedulers();
				_ = signal.Wait(1);
			}

			return true;
		}

		public static object Label_9F201721(params object[] args)
		{
			btwtyped = true;
			btwTypedEvent.Set();
			return string.Empty;
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void AutoCorrect()
		{
			string val;
			HotstringDefinition hs1, hs2;
			var filename = string.Format("..{0}..{0}..{0}Keysharp.Tests{0}HotstringTests.txt", Path.DirectorySeparatorChar);
			var hotstrings = File.ReadLines(filename);
			var delimiters = new char[] { ',' };
			hsm.ClearHotstrings();
			hsm.RestoreDefaults(true);
			_ = Keyboard.Hotstring("Reset");

			foreach (var hotstring in hotstrings)
			{
				var splits = hotstring.Split(delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				var split0 = splits[0][(splits[0].IndexOf('(') + 1)..].Trim('"');
				var split3 = splits[3].Trim('"');
				hs1 = (HotstringDefinition)Keysharp.Runtime.Keyboard.HotstringManager.AddHotstring(split0, null, splits[2].Trim('"'), split3, splits[4].Trim('"'), false);
				//System.Diagnostics.Debug.WriteLine(split0);

				if (!split0.Contains('*'))
					val = split3 + " ";
				else
					val = split3;

				hsm.AddChars(val);
				hs2 = hsm.MatchHotstring();//Test as is.
				Assert.AreEqual(hs1, hs2);
				//
				_ = Keyboard.Hotstring("Reset");
				hsm.AddChars(Guid.NewGuid() + " " + val);//Test with text before it.
				hs2 = hsm.MatchHotstring();
				Assert.AreEqual(hs1, hs2);
				_ = Keyboard.Hotstring("Reset");
				hs2 = hsm.MatchHotstring();
				Assert.AreEqual(null, hs2);
				//Need to ensure the other tests with ? and * work.
				var opts = split0[1..split0.IndexOf(':', 1)];
				var newOptsName = ":*B0OSZRK123P10:" + split3;//Change options except for ? and C.

				//Still need to do the rest of the autocorrect file here.//TODO
				if (opts.Contains('?'))
					_ = Keyboard.Hotstring("?");
				else
					_ = Keyboard.Hotstring("?0");

				if (opts.Contains('C'))
					_ = Keyboard.Hotstring("C");
				else
					_ = Keyboard.Hotstring("C0");

				var found = Keyboard.Hotstring(newOptsName) as HotstringDefinition;
				Assert.IsNotNull(found);
				Assert.AreEqual(found.EndCharRequired, false);
				Assert.AreEqual(found.DoBackspace, false);
				Assert.AreEqual(found.OmitEndChar, true);
				Assert.AreEqual(found.SuspendExempt, true);
				Assert.AreEqual(found.DoReset, true);
				Assert.AreEqual(found.SendRaw, SendRawModes.Raw);
				Assert.AreEqual(found.KeyDelay, 123L);
				Assert.AreEqual(found.Priority, 10L);
				_ = Keyboard.Hotstring("?0");
				_ = Keyboard.Hotstring("C0");
			}
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void ChangeDefaultOptions()
		{
			hsm.RestoreDefaults(true);
			//End char required.
			var newVal = false;
			var origVal = A_DefaultHotstringEndCharRequired;
			Assert.AreEqual(origVal, !newVal);
			var oldVal = Keyboard.Hotstring("*:");
			Assert.AreNotEqual(origVal, A_DefaultHotstringEndCharRequired);
			Assert.AreEqual(A_DefaultHotstringEndCharRequired, newVal);
			Assert.AreEqual("", oldVal);
			//Case sensitivity.
			newVal = true;
			origVal = A_DefaultHotstringCaseSensitive;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("C");
			Assert.AreNotEqual(origVal, A_DefaultHotstringCaseSensitive);
			Assert.AreEqual(A_DefaultHotstringCaseSensitive, newVal);
			Assert.AreEqual("", oldVal);
			//Case sensitivity restore to default.
			newVal = false;
			origVal = A_DefaultHotstringCaseSensitive;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("C0");
			Assert.AreNotEqual(origVal, A_DefaultHotstringCaseSensitive);
			Assert.AreEqual(A_DefaultHotstringCaseSensitive, newVal);
			Assert.AreEqual("", oldVal);
			//Inside word.
			newVal = true;
			origVal = A_DefaultHotstringDetectWhenInsideWord;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("?");
			Assert.AreNotEqual(origVal, A_DefaultHotstringDetectWhenInsideWord);
			Assert.AreEqual(A_DefaultHotstringDetectWhenInsideWord, newVal);
			Assert.AreEqual("", oldVal);
			//Automatic backspacing off.
			newVal = false;
			origVal = A_DefaultHotstringDoBackspace;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("B0");
			Assert.AreNotEqual(origVal, A_DefaultHotstringDoBackspace);
			Assert.AreEqual(A_DefaultHotstringDoBackspace, newVal);
			Assert.AreEqual("", oldVal);
			//Automatic backspacing back on.
			newVal = true;
			origVal = A_DefaultHotstringDoBackspace;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("B");
			Assert.AreNotEqual(origVal, A_DefaultHotstringDoBackspace);
			Assert.AreEqual(A_DefaultHotstringDoBackspace, newVal);
			Assert.AreEqual("", oldVal);
			//Do not conform to typed case.
			newVal = false;
			origVal = A_DefaultHotstringConformToCase;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("C1");
			Assert.AreNotEqual(origVal, A_DefaultHotstringConformToCase);
			Assert.AreEqual(A_DefaultHotstringConformToCase, newVal);
			Assert.AreEqual("", oldVal);
			//Omit ending character.
			newVal = true;
			origVal = A_DefaultHotstringOmitEndChar;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("O");
			Assert.AreNotEqual(origVal, A_DefaultHotstringOmitEndChar);
			Assert.AreEqual(A_DefaultHotstringOmitEndChar, newVal);
			Assert.AreEqual("", oldVal);
			//Restore ending character.
			newVal = false;
			origVal = A_DefaultHotstringOmitEndChar;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("O0");
			Assert.AreNotEqual(origVal, A_DefaultHotstringOmitEndChar);
			Assert.AreEqual(A_DefaultHotstringOmitEndChar, newVal);
			Assert.AreEqual("", oldVal);
			//Exempt from suspend.
			newVal = true;
			origVal = A_SuspendExempt.Ab();
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("S");
			Assert.AreNotEqual(origVal, A_SuspendExempt.Ab());
			Assert.AreEqual(A_SuspendExempt.Ab(), newVal);
			Assert.AreEqual("", oldVal);
			//Remove suspend exempt.
			newVal = false;
			origVal = A_SuspendExempt.Ab();
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("S0");
			Assert.AreNotEqual(origVal, A_SuspendExempt.Ab());
			Assert.AreEqual(A_SuspendExempt.Ab(), newVal);
			Assert.AreEqual("", oldVal);
			//Reset on trigger.
			newVal = true;
			origVal = A_DefaultHotstringDoReset;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("Z");
			Assert.AreNotEqual(origVal, A_DefaultHotstringDoReset);
			Assert.AreEqual(A_DefaultHotstringDoReset, newVal);
			Assert.AreEqual("", oldVal);
			//Restore reset on trigger.
			newVal = false;
			origVal = A_DefaultHotstringDoReset;
			Assert.AreEqual(origVal, !newVal);
			oldVal = Keyboard.Hotstring("Z0");
			Assert.AreNotEqual(origVal, A_DefaultHotstringDoReset);
			Assert.AreEqual(A_DefaultHotstringDoReset, newVal);
			Assert.AreEqual("", oldVal);
			//Send replacement text raw.
			var newMode = SendRawModes.Raw.ToString();
			var origMode = A_DefaultHotstringSendRaw;
			Assert.AreEqual(origMode, SendRawModes.NotRaw.ToString());
			oldVal = Keyboard.Hotstring("R");
			Assert.AreNotEqual(origMode, A_DefaultHotstringSendRaw);
			Assert.AreEqual(A_DefaultHotstringSendRaw, newMode);
			Assert.AreEqual("", oldVal);
			//Restore replacement text mode.
			newMode = SendRawModes.NotRaw.ToString();
			origMode = A_DefaultHotstringSendRaw;
			Assert.AreEqual(origMode, SendRawModes.Raw.ToString());
			oldVal = Keyboard.Hotstring("R0");
			Assert.AreNotEqual(origMode, A_DefaultHotstringSendRaw);
			Assert.AreEqual(A_DefaultHotstringSendRaw, newMode);
			Assert.AreEqual("", oldVal);
			//Send replacement text mode.
			newMode = SendRawModes.RawText.ToString();
			origMode = A_DefaultHotstringSendRaw;
			Assert.AreEqual(origMode, SendRawModes.NotRaw.ToString());
			oldVal = Keyboard.Hotstring("T");
			Assert.AreNotEqual(origMode, A_DefaultHotstringSendRaw);
			Assert.AreEqual(A_DefaultHotstringSendRaw, newMode);
			Assert.AreEqual("", oldVal);
			//Restore replacement text mode.
			newMode = SendRawModes.NotRaw.ToString();
			origMode = A_DefaultHotstringSendRaw;
			Assert.AreEqual(origMode, SendRawModes.RawText.ToString());
			oldVal = Keyboard.Hotstring("T0");
			Assert.AreNotEqual(origMode, A_DefaultHotstringSendRaw);
			Assert.AreEqual(A_DefaultHotstringSendRaw, newMode);
			Assert.AreEqual("", oldVal);
			//Key delay.
			var newInt = 42;
			var origInt = A_DefaultHotstringKeyDelay;
			Assert.AreEqual(origInt, 0);
			oldVal = Keyboard.Hotstring($"K{newInt}");
			Assert.AreNotEqual(origInt, A_DefaultHotstringKeyDelay);
			Assert.AreEqual(A_DefaultHotstringKeyDelay, newInt);
			Assert.AreEqual("", oldVal);
			//Priority.
			newInt = 42;
			origInt = A_DefaultHotstringPriority;
			Assert.AreEqual(origInt, 0);
			oldVal = Keyboard.Hotstring($"P{newInt}");
			Assert.AreNotEqual(origInt, A_DefaultHotstringPriority);
			Assert.AreEqual(A_DefaultHotstringPriority, newInt);
			Assert.AreEqual("", oldVal);
			//Send mode Event.
			var newSendMode = SendModes.Event.ToString();
			var origSendMode = A_DefaultHotstringSendMode;
			Assert.AreEqual(origSendMode, SendModes.Input.ToString());
			oldVal = Keyboard.Hotstring("SE");
			Assert.AreNotEqual(origSendMode, A_DefaultHotstringSendMode);
			Assert.AreEqual(A_DefaultHotstringSendMode, newSendMode);
			Assert.AreEqual("", oldVal);
			//Send mode Play.
			newSendMode = SendModes.Play.ToString();
			origSendMode = A_DefaultHotstringSendMode;
			Assert.AreEqual(origSendMode, SendModes.Event.ToString());
			oldVal = Keyboard.Hotstring("SP");
			Assert.AreNotEqual(origSendMode, A_DefaultHotstringSendMode);
			Assert.AreEqual(A_DefaultHotstringSendMode, newSendMode);
			Assert.AreEqual("", oldVal);
			//Send mode Input.
			newSendMode = SendModes.InputThenPlay.ToString();
			origSendMode = A_DefaultHotstringSendMode;
			Assert.AreEqual(origSendMode, SendModes.Play.ToString());
			oldVal = Keyboard.Hotstring("SI");
			Assert.AreNotEqual(origSendMode, A_DefaultHotstringSendMode);
			Assert.AreEqual(A_DefaultHotstringSendMode, newSendMode);//InputThenPlay gets used when Input is specified. See HotstringDefinition.ParseOptions().
			Assert.AreEqual("", oldVal);
			//Try changing multiple options at once.
			//First reset everything back to the default state.
			_ = Keyboard.Hotstring("*0");
			origVal = A_DefaultHotstringEndCharRequired;
			Assert.AreEqual(origVal, true);
			_ = Keyboard.Hotstring("C0");
			origVal = A_DefaultHotstringCaseSensitive;
			Assert.AreEqual(origVal, false);
			_ = Keyboard.Hotstring("?0");
			origVal = A_DefaultHotstringDetectWhenInsideWord;
			Assert.AreEqual(origVal, false);
			_ = Keyboard.Hotstring("B");
			origVal = A_DefaultHotstringDoBackspace;
			Assert.AreEqual(origVal, true);
			_ = Keyboard.Hotstring("O0");
			origVal = A_DefaultHotstringOmitEndChar;
			Assert.AreEqual(origVal, false);
			_ = Keyboard.Hotstring("S0");
			origVal = A_SuspendExempt.Ab();
			Assert.AreEqual(origVal, false);
			_ = Keyboard.Hotstring("Z0");
			origVal = A_DefaultHotstringDoReset;
			Assert.AreEqual(origVal, false);
			_ = Keyboard.Hotstring("R0");
			Assert.AreEqual(A_DefaultHotstringSendRaw, SendRawModes.NotRaw.ToString());
			_ = Keyboard.Hotstring("T0");
			Assert.AreEqual(A_DefaultHotstringSendRaw, SendRawModes.NotRaw.ToString());
			_ = Keyboard.Hotstring("K-1");
			Assert.AreEqual(A_DefaultHotstringKeyDelay, -1L);
			_ = Keyboard.Hotstring("P-1");
			Assert.AreEqual(A_DefaultHotstringPriority, -1L);
			_ = Keyboard.Hotstring("SI");
			Assert.AreEqual(A_DefaultHotstringSendMode, SendModes.InputThenPlay.ToString());
			//Now test a multi-option string.
			_ = Keyboard.Hotstring("*?CB0OSZRK123P10");
			Assert.AreEqual(A_DefaultHotstringEndCharRequired, false);
			Assert.AreEqual(A_DefaultHotstringDetectWhenInsideWord, true);
			Assert.AreEqual(A_DefaultHotstringCaseSensitive, true);
			Assert.AreEqual(A_DefaultHotstringDoBackspace, false);
			Assert.AreEqual(A_DefaultHotstringOmitEndChar, true);
			Assert.AreEqual(A_SuspendExempt, true);
			Assert.AreEqual(A_DefaultHotstringDoReset, true);
			Assert.AreEqual(A_DefaultHotstringSendRaw, SendRawModes.Raw.ToString());
			Assert.AreEqual(A_DefaultHotstringKeyDelay, 123L);
			Assert.AreEqual(A_DefaultHotstringPriority, 10L);
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void ChangeEndChars()
		{
			hsm.RestoreDefaults(true);
			var newVal = "newendchars";
			var origVal = A_DefaultHotstringEndChars;
			Assert.AreEqual(origVal, "-()[]{}:;'\"/\\,.?!\r\n \t");
			var oldVal = Keyboard.Hotstring("EndChars", newVal);
			Assert.AreNotEqual(origVal, A_DefaultHotstringEndChars);
			Assert.AreEqual(A_DefaultHotstringEndChars, newVal);
			Assert.AreEqual(origVal, oldVal);
		}

		// RequiresHook: exercises real hotstring firing, which needs the global keyboard/mouse hook to install
		// (keysharp-inputd on Linux). Excluded from the non-interactive curated CI set; the full interactive
		// run still executes it and may prompt for input-access permission.
		[Test, Category("Hotstring"), Category("RequiresHook"), NonParallelizable]
		public void CreateHotstring()
		{
			//Can't seem to simulate uppercase here, so we can't test case sensitive hotstrings.
			btwtyped = false;
			btwTypedEvent.Reset();
			hsm.ClearHotstrings();
			hsm.RestoreDefaults(true);
			_ = Keyboard.Hotstring("Reset");
			_ = Keysharp.Runtime.Keyboard.HotstringManager.AddHotstring("::btw", Functions.Func(Label_9F201721, null), ":btw", "btw", "", false);
			_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks();
			Assert.IsTrue(A_KeybdHookInstalled == 1L);//Will fail if system has another hook, so exit your scripts before running this.
			Assert.IsTrue(A_MouseHookInstalled == 1L);//Because there is a hotstring and mouse reset is true by default, the mouse hook gets installed.
			SimulateKeyPress((uint)Keysharp.Builtins.Keyboard.GetKeyVK("b"));
			SimulateKeyPress((uint)Keysharp.Builtins.Keyboard.GetKeyVK("t"));
			SimulateKeyPress((uint)Keysharp.Builtins.Keyboard.GetKeyVK("w"));
			SimulateKeyPress((uint)Keysharp.Builtins.Keyboard.GetKeyVK("Enter"));
			Assert.IsTrue(WaitForCallback(btwTypedEvent), "Timed out waiting for hotstring callback.");
			Assert.AreEqual(btwtyped, true);
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void GetKey()
		{
			var sc = Keysharp.Builtins.Keyboard.GetKeySC("Esc");
			var vk = Keysharp.Builtins.Keyboard.GetKeyVK("Esc");
			Assert.IsTrue(sc > 0);
			Assert.AreEqual(27L, vk);
			var fromSc = $"sc{sc:x}";
			Assert.AreEqual(vk, Keysharp.Builtins.Keyboard.GetKeyVK(fromSc));
		}

#if LINUX || OSX
		[Test, Category("Hotstring"), Category("Internal")]
		public void UnixNumpadScanCodes()
		{
			var expected = new (string Name, long Vk, long Sc)[]
			{
#if LINUX
				("NumpadIns", 0x2D, 82),
				("NumpadEnd", 0x23, 79),
				("NumpadDown", 0x28, 80),
				("NumpadPgDn", 0x22, 81),
				("NumpadLeft", 0x25, 75),
				("NumpadClear", 0x0C, 76),
				("NumpadRight", 0x27, 77),
				("NumpadHome", 0x24, 71),
				("NumpadUp", 0x26, 72),
				("NumpadPgUp", 0x21, 73),
				("NumpadDel", 0x2E, 83),
#else
				("NumpadIns", 0x2D, 0x52),
				("NumpadEnd", 0x23, 0x53),
				("NumpadDown", 0x28, 0x54),
				("NumpadPgDn", 0x22, 0x55),
				("NumpadLeft", 0x25, 0x56),
				("NumpadClear", 0x0C, 0x47),
				("NumpadRight", 0x27, 0x58),
				("NumpadHome", 0x24, 0x59),
				("NumpadUp", 0x26, 0x5B),
				("NumpadPgUp", 0x21, 0x5C),
				("NumpadDel", 0x2E, 0x41),
#endif
			};

			foreach (var (name, vk, sc) in expected)
			{
				Assert.AreEqual(vk, Keyboard.GetKeyVK(name), name);
				Assert.AreEqual(sc, Keyboard.GetKeySC(name), name);
			}

#if LINUX
			Assert.AreEqual(72u, Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(0x26));
			Assert.AreEqual(103u, Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(0x26, true));
			Assert.AreEqual(103L, Keyboard.GetKeySC("Up"));
#else
			Assert.AreEqual(0x5Bu, Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(0x26));
			Assert.AreEqual(0x7Eu, Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(0x26, true));
			Assert.AreEqual(0x7EL, Keyboard.GetKeySC("Up"));
#endif
		}
#endif

#if LINUX
		[Test, Category("Hotstring"), Category("Internal")]
		public void LinuxEvdevMappings()
		{
			static uint VkToEvdev(uint vk) => Keysharp.Internals.Input.Keyboard.KeyCodes.VkToEvdev(vk);
			static uint EvdevToVk(uint evdev) => Keysharp.Internals.Input.Keyboard.KeyCodes.EvdevToVk(evdev);

			Assert.AreEqual(86u, VkToEvdev(0xE2)); // VK_OEM_102 -> KEY_102ND
			Assert.AreEqual(0xE2u, EvdevToVk(86));
			Assert.AreEqual(0xE2u, EvdevToVk(89)); // KEY_RO
			Assert.AreEqual(93u, VkToEvdev(0x15)); // VK_KANA
			foreach (var evdev in new uint[] { 90, 91, 93, 122 })
				Assert.AreEqual(0x15u, EvdevToVk(evdev));
			Assert.AreEqual(123u, VkToEvdev(0x19)); // VK_HANJA
			Assert.AreEqual(0x19u, EvdevToVk(123));
			Assert.AreEqual(0x5Du, EvdevToVk(139)); // KEY_MENU -> VK_APPS
			Assert.AreEqual(0x6Cu, EvdevToVk(95)); // KEY_KPJPCOMMA -> VK_SEPARATOR
			Assert.AreEqual(0xBBu, EvdevToVk(117)); // KEY_KPEQUAL -> VK_OEM_PLUS
			Assert.AreEqual(0xDCu, EvdevToVk(124)); // KEY_YEN -> VK_OEM_5
			Assert.AreEqual(0xACu, EvdevToVk(150)); // KEY_WWW -> VK_BROWSER_HOME
			Assert.AreEqual(155u, VkToEvdev(0xB4)); // VK_LAUNCH_MAIL -> KEY_MAIL
			Assert.AreEqual(0xB4u, EvdevToVk(215)); // KEY_EMAIL
			Assert.AreEqual(0xB3u, EvdevToVk(207)); // KEY_PLAY -> VK_MEDIA_PLAY_PAUSE
			Assert.AreEqual(223u, VkToEvdev(0x03)); // VK_CANCEL -> KEY_CANCEL
			Assert.AreEqual(0x26u, Keysharp.Internals.Input.Keyboard.KeyCodes.ApplyNumpadState(
				EvdevToVk(72), numLockOn: false, shiftDown: false)); // KEY_KP8 -> VK_UP
			Assert.AreEqual(0x68u, Keysharp.Internals.Input.Keyboard.KeyCodes.ApplyNumpadState(
				EvdevToVk(72), numLockOn: true, shiftDown: false)); // KEY_KP8 -> VK_NUMPAD8
			Assert.AreEqual(0x68u, Keysharp.Internals.Input.Keyboard.KeyCodes.ApplyNumpadState(
				EvdevToVk(72), numLockOn: false, shiftDown: true));
			Assert.AreEqual(0x26u, Keysharp.Internals.Input.Keyboard.KeyCodes.ApplyNumpadState(
				EvdevToVk(72), numLockOn: true, shiftDown: true));
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void LinuxScanCodes()
		{
			static uint[] CodesFor(uint vk) => Keysharp.Internals.Input.Keyboard.KeyCodes.ScanCodesForVk(vk).ToArray();
			static uint TwinOf(uint vk, bool numLockOn, bool shiftDown)
				=> Keysharp.Internals.Input.Keyboard.KeyCodes.NumpadTwinVk(vk, numLockOn, shiftDown);

			// The whole point of the reverse map: a key-state query must see every code that reports as the VK,
			// not just the one MapVkToSc names.
			CollectionAssert.AreEqual(new uint[] { 127, 139 }, CodesFor(0x5D)); // VK_APPS: KEY_COMPOSE, KEY_MENU
			CollectionAssert.AreEqual(new uint[] { 155, 215 }, CodesFor(0xB4)); // VK_LAUNCH_MAIL: KEY_MAIL, KEY_EMAIL
			CollectionAssert.AreEqual(new uint[] { 164, 200, 201, 207 }, CodesFor(0xB3)); // VK_MEDIA_PLAY_PAUSE
			CollectionAssert.AreEqual(new uint[] { 28, 96 }, CodesFor(0x0D)); // VK_RETURN: KEY_ENTER, KEY_KPENTER
			CollectionAssert.AreEqual(new uint[] { 103 }, CodesFor(0x26)); // VK_UP
			CollectionAssert.AreEqual(new uint[] { 72 }, CodesFor(0x68)); // VK_NUMPAD8
			CollectionAssert.IsEmpty(CodesFor(0)); // no such key

			// NumLock and Shift cancelling out is what folds KEY_KP8 into VK_UP, so that is exactly when a query
			// about VK_UP must also consider Numpad8's code (and never the other way round).
			Assert.AreEqual(0x68u, TwinOf(0x26, numLockOn: false, shiftDown: false));
			Assert.AreEqual(0x68u, TwinOf(0x26, numLockOn: true, shiftDown: true));
			Assert.AreEqual(0u, TwinOf(0x26, numLockOn: true, shiftDown: false));
			Assert.AreEqual(0u, TwinOf(0x68, numLockOn: false, shiftDown: false));
			Assert.AreEqual(0x65u, TwinOf(0x0C, numLockOn: false, shiftDown: false)); // VK_CLEAR -> VK_NUMPAD5
			Assert.AreEqual(0u, TwinOf((uint)'A', numLockOn: false, shiftDown: false));
		}
#endif

#if OSX
		[Test, Category("Hotstring"), Category("Internal")]
		public void MacKeyCodeMappings()
		{
			Assert.AreEqual(0x2Fu, Keysharp.Internals.Input.Keyboard.KeyCodes.MapScToVk(0x72)); // kVK_Help
			Assert.AreEqual(0x5Du, Keysharp.Internals.Input.Keyboard.KeyCodes.MapScToVk(0x6E)); // kVK_ContextualMenu
			Assert.AreEqual(0xE2u, Keysharp.Internals.Input.Keyboard.KeyCodes.MapScToVk(0x5E)); // kVK_JIS_Underscore
			Assert.AreEqual(0x6Cu, Keysharp.Internals.Input.Keyboard.KeyCodes.MapScToVk(0x5F)); // kVK_JIS_KeypadComma
			Assert.AreEqual(0x15u, Keysharp.Internals.Input.Keyboard.KeyCodes.MapScToVk(0x68)); // kVK_JIS_Kana
		}
#endif

		[Test, Category("Hotstring"), NonParallelizable]
		public void HotstringDirectives()
		{
			Assert.IsTrue(TestScript("hotstring-directives", false));
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void HotstringParsing()
		{
			var trigger = "^;";
			var hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual("^;", hk);
			//
			trigger = "`;";
			hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual(";", hk);
			//
			trigger = ":";
			hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual(":", hk);
			//
			trigger = "`";
			hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual("`", hk);
			//
			trigger = "``";
			hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual("`", hk);
			//
			trigger = "+`";
			hk = EscapeHotkeyTrigger(trigger);
			Assert.AreEqual("+`", hk);
			//
			Assert.IsTrue(TestScript("hotkey-hotstring-parsing", false));

			static string EscapeHotkeyTrigger(ReadOnlySpan<char> s)
			{
				var escaped = false;
				var sb = new StringBuilder(s.Length);
				char ch = (char)0;

				for (var i = 0; i < s.Length; ++i)
				{
					ch = s[i];
					escaped = i == 0 && ch == '`';

					if (!escaped)
						sb.Append(ch);
				}

				if (escaped)
					sb.Append(ch);

				return sb.ToString();
			}
		}

		/// <summary>
		/// The down half of a remap is the one piece of generated code which differs by platform: macOS wraps it
		/// so that only a physical auto-repeat is marked as one, which splits the literal immediately before its
		/// closing brace (see RemapDownSend in the lowerer). Assertions about a down-send therefore have to
		/// expect the text the running platform actually emits. The wrapping itself, including the brace this
		/// drops, is covered by MacRemapPropagatesNativeAutoRepeatMetadata. A wheel remap is never wrapped
		/// (it has no up event) and so is compared verbatim.
		/// </summary>
		private static string RemapDown(string downSend) =>
#if OSX
			downSend[..^1];
#else
			downSend;
#endif

		[Test, Category("Hotstring"), Category("Internal")]
		public void CopilotHotkeyAlias()
		{
			var script = """
				Copilot::MsgBox "direct"
				Copilot Up::MsgBox "up"
				Copilot & x::MsgBox "prefix"
				a & Copilot::MsgBox "suffix"
				MyCopilot::MsgBox "boundary"
				""";
			var (prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(script);
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("\"<#<+F23\""), generated);
			Assert.IsTrue(generated.Contains("\"<#<+F23 Up\""), generated);
			Assert.IsTrue(generated.Contains("\"<#<+F23 & x\""), generated);
			Assert.IsTrue(generated.Contains("\"a & <#<+F23\""), generated);
			Assert.IsTrue(generated.Contains("\"MyCopilot\""), generated);
			Assert.IsFalse(generated.Contains("\"Copilot\""), generated);
			Assert.AreEqual(0L, Keyboard.GetKeyVK("Copilot"));
			Assert.AreEqual(0L, Keyboard.GetKeySC("Copilot"));
			Assert.AreEqual(0L, Keyboard.GetKeyVK("Office"));
			Assert.AreEqual(0L, Keyboard.GetKeySC("Office"));

			// Office deliberately has no corresponding declaration/remap alias. Since it is not a real key name,
			// an identifier in the target position remains a one-line hotkey body rather than becoming a remap.
			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("a::Office");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsFalse(generated.Contains("__Remap_"), generated);
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void CopilotRemapAlias()
		{
			var (prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("Copilot::RCtrl");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("\"*<#<+F23\""), generated);
			Assert.IsTrue(generated.Contains(RemapDown("{Blind}{LShift up}{LWin up}{RCtrl DownR}")), generated);
			Assert.IsTrue(generated.Contains("{Blind}{RCtrl Up}"), generated);
			Assert.IsFalse(generated.Contains("GetKeyState(\"LShift\",\"P\")"), generated);
			Assert.IsFalse(generated.Contains("GetKeyState(\"LWin\",\"P\")"), generated);

			// The literal chord retains ordinary generic-chord behavior, including restoring physically-held
			// source modifiers after the remapped modifier is released.
			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("<#<+F23::RCtrl");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("GetKeyState(\"LShift\",\"P\")"), generated);
			Assert.IsTrue(generated.Contains("GetKeyState(\"LWin\",\"P\")"), generated);

			// Sided targets are translated into the explicit events Send requires, in both directions.
			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("Copilot::>^a");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind<+<#}{RCtrl down}{a DownR}")), generated);
			Assert.IsTrue(generated.Contains("{Blind}{RCtrl up}"), generated);

			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("a::Copilot");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind}{LShift down}{LWin down}{F23 DownR}")), generated);
			Assert.IsTrue(generated.Contains("{Blind}{LWin up}{LShift up}"), generated);
			Assert.IsTrue(generated.Contains("{Blind}{F23 Up}"), generated);
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void RemapCallbacks()
		{
			var (prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(">!.::RCtrl");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));

			var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
			var unit = lowerer.Build(prog, "Test");
			var generated = unit.ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind}{RAlt up}{RCtrl DownR}")), generated);
			Assert.IsFalse(generated.Contains(RemapDown("{Blind>!}{RCtrl DownR}")), generated);
			Assert.IsTrue(generated.Contains("{Blind}{RCtrl Up}"), generated);
			Assert.IsTrue(generated.Contains("GetKeyState(\"RAlt\",\"P\")"), generated);
			Assert.IsTrue(generated.Contains("{RAlt DownR}"), generated);
			Assert.IsTrue(generated.Contains("System.String.Concat("), generated);

			var upCallback = unit.DescendantNodes()
				.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
				.Single(method => method.Identifier.ValueText == "__Remap_2");
			var upSendCount = upCallback.DescendantNodes()
				.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
				.Count(invocation => invocation.Expression.ToString() == "Keysharp.Builtins.Keyboard.Send");
			Assert.AreEqual(1, upSendCount, generated);

			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics(">!.::b");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind>!}{b DownR}")), generated);

			// A wheel has no up event, so its up hotkey never fires; holding the destination with DownR would
			// leave the modifier stuck down. Such a remap must keep the plain press-and-release form.
			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("^WheelUp::LShift");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("{Blind<^>^}{LShift}"), generated);
			Assert.IsFalse(generated.Contains("{LShift DownR}"), generated);
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void SidedRemapDestination()
		{
			// A remap target is hotkey syntax and may use < and >, but Send has no such prefix and would type
			// them as literal characters (which is how "a::<#<+F23" came to type ">>"). Such a destination is
			// emitted as explicit modifier events instead, released immediately after the destination key so
			// that they are scoped to the one keystroke, exactly as Send scopes the neutral ^ ! + # prefixes.
			var (prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("a::<#<+F23");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			var generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind}{LShift down}{LWin down}{F23 DownR}")), generated);
			Assert.IsTrue(generated.Contains("{Blind}{LWin up}{LShift up}"), generated);
			Assert.IsTrue(generated.Contains("{Blind}{F23 Up}"), generated);
			Assert.IsFalse(generated.Contains("<#<+"), generated);

			// A neutral destination keeps the prefix form, which Send resolves to the left-hand key.
			(prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("a::#+F23");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));
			generated = new Keysharp.Compilation.Syntax.Lowerer().Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains(RemapDown("{Blind}#+{F23 DownR}")), generated);
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void CompositeModifiers()
		{
			// The modifiers belong to the prefix, not the suffix: the prefix key is still just F23.
			var combo = new HotkeyDefinition(1100, null, (uint)HotkeyTypeEnum.Normal, "<#<+F23 & x", 0);
			Assert.IsTrue(combo.constructedOK);
			Assert.AreEqual(Keysharp.Internals.Input.Keyboard.VirtualKeys.VK_F23, combo.modifierVK);
			Assert.AreEqual(
				Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LWIN | Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LSHIFT,
				combo.prefixModifiersLR);

			// A plain combo on the same prefix key is unconstrained, and is a different hotkey.
			var plain = new HotkeyDefinition(1101, null, (uint)HotkeyTypeEnum.Normal, "F23 & x", 0);
			Assert.IsTrue(plain.constructedOK);
			Assert.AreEqual(combo.modifierVK, plain.modifierVK);
			Assert.AreEqual(0u, plain.prefixModifiersLR);

			// The suffix of a composite may carry modifiers too. AutoHotkey rejects these outright, so a
			// combination which used to be spelled with a chord key name has an equivalent again.
			var suffix = new HotkeyDefinition(1103, null, (uint)HotkeyTypeEnum.Normal, "a & <#<+F23", 0);
			Assert.IsTrue(suffix.constructedOK);
			Assert.AreEqual(Keysharp.Internals.Input.Keyboard.VirtualKeys.VK_F23, suffix.vk);
			Assert.AreEqual(
				Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LWIN | Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LSHIFT,
				suffix.suffixModifiersLR);
			Assert.AreEqual(0u, suffix.prefixModifiersLR); // The modifiers belong to the suffix, not the prefix.

			// A composite with no modifiers on either side keeps ignoring the modifier state.
			var bare = new HotkeyDefinition(1104, null, (uint)HotkeyTypeEnum.Normal, "a & F23", 0);
			Assert.IsTrue(bare.constructedOK);
			Assert.AreEqual(0u, bare.suffixModifiersLR);
			Assert.IsTrue(ModifiersSatisfied(0u, bare.suffixModifiers, bare.suffixModifiersLR));

			// A suffix's modifiers are held when the hotkey fires, which is what modifiersConsolidatedLR
			// describes, so they belong there. A prefix's are not: they describe the earlier moment the prefix
			// was pressed and may already have been released, so they must stay out of it.
			Assert.AreEqual(
				Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LWIN | Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LSHIFT,
				suffix.modifiersConsolidatedLR & (Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LWIN | Keysharp.Internals.Input.Keyboard.KeyboardUtils.MOD_LSHIFT));
			Assert.AreEqual(0u, combo.modifiersConsolidatedLR);

			// Specificity orders the chain, so a bare combination cannot eclipse a modified one on the same
			// keys regardless of which was declared first.
			Assert.Greater(combo.CompositeSpecificity(), plain.CompositeSpecificity());
			Assert.Greater(suffix.CompositeSpecificity(), bare.CompositeSpecificity());
			Assert.AreEqual(0, bare.CompositeSpecificity());

			// A neutral modifier means either side satisfies it; a sided one means that side only.
			var neutral = new HotkeyDefinition(1102, null, (uint)HotkeyTypeEnum.Normal, "^F23 & x", 0);
			Assert.IsTrue(neutral.constructedOK);
			Assert.IsTrue(ModifiersSatisfied(MOD_LCONTROL, neutral.prefixModifiers, neutral.prefixModifiersLR));
			Assert.IsTrue(ModifiersSatisfied(MOD_RCONTROL, neutral.prefixModifiers, neutral.prefixModifiersLR));
			Assert.IsFalse(ModifiersSatisfied(MOD_LSHIFT, neutral.prefixModifiers, neutral.prefixModifiersLR));
			Assert.IsTrue(ModifiersSatisfied(MOD_LWIN | MOD_LSHIFT, combo.prefixModifiers, combo.prefixModifiersLR));
			Assert.IsFalse(ModifiersSatisfied(MOD_RWIN | MOD_RSHIFT, combo.prefixModifiers, combo.prefixModifiersLR));
			Assert.IsTrue(ModifiersSatisfied(0u, plain.prefixModifiers, plain.prefixModifiersLR));
		}

		[Test, Category("Hotstring"), Category("Internal")]
		public void CompositePrefixLatch()
		{
			var key = new Keysharp.Internals.Input.Keyboard.KeyType(Keysharp.Internals.Input.Keyboard.VirtualKeys.VK_F23);
			var chord = MOD_LWIN | MOD_LSHIFT;

			// A key no hotkey uses as a modified prefix records nothing, so ordinary keys pay for none of this.
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, true, chord);
			Assert.IsTrue(key.isDown);
			Assert.IsNull(key.downModifiersLR);

			key.samplesPrefixModifiers = true;

			// Pressed with nothing held: the requirement is not met, and zero is a real answer rather than
			// "not yet sampled".
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, false, 0u);
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, true, 0u);
			Assert.AreEqual(0u, key.downModifiersLR);
			Assert.IsFalse(ModifiersSatisfied(key.downModifiersLR ?? 0u, 0u, chord));

			// Auto-repeat must not revise it, even from that zero.
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, true, chord);
			Assert.AreEqual(0u, key.downModifiersLR, "the sample was revised while the key was still held");

			// Pressed with the modifiers held: armed.
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, false, 0u);
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, true, chord);
			Assert.IsTrue(ModifiersSatisfied(key.downModifiersLR ?? 0u, 0u, chord));

			// Latched: modifiers going away while the key is still held must not disarm it. This is what lets a
			// key whose firmware drops the modifiers immediately still act as a prefix.
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, true, 0u);
			Assert.IsTrue(ModifiersSatisfied(key.downModifiersLR ?? 0u, 0u, chord), "the sample was revised while the key was still held");

			// Released: the press is over, so both halves of it end together.
			Keysharp.Internals.Input.Hooks.HookThread.RecordKeyDownState(key, false, chord);
			Assert.IsFalse(key.isDown);
			Assert.IsNull(key.downModifiersLR);
		}


#if OSX
		[Test, Category("Hotstring"), Category("Internal")]
		public void MacRemapRepeat()
		{
			var eventInfo = new Keysharp.Internals.Input.Hooks.HookEventInfo(1, false, true, 0, null, null).BuildEventInfo();
			Assert.AreEqual(true, Script.GetPropertyValue(eventInfo, "IsAutoRepeat"));

			var (prog, diags) = Keysharp.Parsing.Syntax.Parser.ParseWithDiagnostics("a::b");
			Assert.IsEmpty(diags, "unexpected parse diagnostics: " + string.Join("; ", diags));

			var lowerer = new Keysharp.Compilation.Syntax.Lowerer();
			var generated = lowerer.Build(prog, "Test").ToFullString();
			Assert.IsTrue(generated.Contains("A_EventInfo"), generated);
			Assert.IsTrue(generated.Contains("IsAutoRepeat"), generated);
			Assert.IsTrue(generated.Contains(" AutoRepeat}"), generated);
			Assert.IsTrue(generated.Contains("{Blind}{b DownR"), generated);
		}
#endif

		[Test, Category("Hotstring"), NonParallelizable]
		public void HotstringParsing2()
		{
			var filename = "hotstring-parsing2";
			_ = TestScript(filename, false);
			//After the script exits, the hotstrings are still kept in memory in the global list.
			//So query them below to ensure they were properly parsed.
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("bitw ");
			var hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::bitw");
			Assert.AreEqual(hs.Replacement, "biggest in the world");
			_ = Keyboard.Hotstring("Reset");
			//
			hsm.AddChars("1 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::1");
			Assert.AreEqual(hs.Replacement, ":2");
			_ = Keyboard.Hotstring("Reset");
			//
			hsm.AddChars("3 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::3");
			Assert.AreEqual(hs.Replacement, "::4");
			_ = Keyboard.Hotstring("Reset");
			//
			hsm.AddChars("5: ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::5:");
			Assert.AreEqual(hs.Replacement, "6");
			_ = Keyboard.Hotstring("Reset");
			//
			hsm.AddChars("7: ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::7:");
			Assert.AreEqual(hs.Replacement, ":8");
			_ = Keyboard.Hotstring("Reset");
			//
			var val = "Any text between the top and bottom parentheses is treated literally.\nBy default" +
					  ", the hard carriage return (Enter) between the previous line and this one is als" +
					  "o preserved.\n    By default, the indentation (tab) to the left of this line is " +
					  "preserved.";
			hsm.AddChars("text1 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::text1");
			Assert.AreEqual(hs.Replacement, val);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf1 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, ":X:mf1");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf2 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, ":X:mf2");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf3 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, ":X:mf3");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf4 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::mf4");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf5 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::mf5");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf6 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::mf6");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf7 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, ":X:mf7");
			Assert.AreEqual(hs.Replacement, null);
			//
			_ = Keyboard.Hotstring("Reset");
			hsm.AddChars("mf8 ");
			hs = hsm.MatchHotstring();
			Assert.AreEqual(hs.Name, "::mf8");
			Assert.AreEqual(hs.Replacement, null);
			//
			hsm.ClearHotstrings();
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void InputHookOptions()
		{
			var ih = new InputHook("B C H I10 M L1 T2 V * E");
			Assert.AreEqual(ih.BackspaceIsUndo, false);
			Assert.AreEqual(ih.CaseSensitive, true);
			Assert.AreEqual(ih.BeforeHotkeys, true);
			Assert.AreEqual(ih.MinSendLevel, 10u);
			Assert.AreEqual(ih.TranscribeModifiedKeys, true);
			Assert.AreEqual(ih.BufferLengthMax, 1);
			Assert.AreEqual(ih.Timeout, 2);
			Assert.AreEqual(ih.VisibleText, true);
			Assert.AreEqual(ih.VisibleNonText, true);
			Assert.AreEqual(ih.FindAnywhere, true);
			Assert.AreEqual(ih.EndCharMode, true);
			//
			ih = new InputHook("BCHI10ML123T2V*E");
			Assert.AreEqual(ih.BackspaceIsUndo, false);
			Assert.AreEqual(ih.CaseSensitive, true);
			Assert.AreEqual(ih.BeforeHotkeys, true);
			Assert.AreEqual(ih.MinSendLevel, 10u);
			Assert.AreEqual(ih.TranscribeModifiedKeys, true);
			Assert.AreEqual(ih.BufferLengthMax, 123);
			Assert.AreEqual(ih.Timeout, 2);
			Assert.AreEqual(ih.VisibleText, true);
			Assert.AreEqual(ih.VisibleNonText, true);
			Assert.AreEqual(ih.FindAnywhere, true);
			Assert.AreEqual(ih.EndCharMode, true);
		}

		[Test, Category("Hotstring"), Category("Internal"), NonParallelizable]
		public void InputHookScanCode()
		{
			const uint VkReturn = 0x0D;
			var ih = new InputHook("V");
			ih.KeyOpt("{Enter}", "E");

			// VK_RETURN is the one VK backed by two scan codes, so MapVkToSc must report a non-zero *secondary*
			// (the NumpadEnter code); that is exactly what lets KeyOpt tell Enter apart from NumpadEnter.
			var secondary = Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(VkReturn, true);
			Assert.AreNotEqual(0u, secondary);

			// {Enter} names the MAIN Enter, so its end-key is registered at the primary scan code, not at
			// NumpadEnter's. On Windows the primary is the secondary with its extended bit cleared; evdev/Mac
			// have no such bit relation, so query the primary directly.
#if WINDOWS
			var sc = secondary ^ 0x100u;
#else
			var sc = Keysharp.Internals.Input.Keyboard.KeyCodes.MapVkToSc(VkReturn);
#endif

			Assert.AreEqual(Keysharp.Internals.Input.Hooks.HookThread.END_KEY_ENABLED, ih.input.keySC[sc] & Keysharp.Internals.Input.Hooks.HookThread.END_KEY_ENABLED);
			Assert.AreEqual(0u, ih.input.keySC[secondary] & Keysharp.Internals.Input.Hooks.HookThread.END_KEY_ENABLED); // NumpadEnter is a distinct key, not this end-key.

#if LINUX
			const uint EvdevEnter = 28u;
			Assert.AreEqual(EvdevEnter, sc);
#endif
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void ResetInputBuffer()
		{
			hsm.AddChars("asdf");
			var origVal = hsm.CurrentInputBuffer;
			Assert.AreEqual(origVal, "asdf");
			origVal = Keyboard.Hotstring("Reset") as string;
			Assert.AreEqual(origVal, "asdf");
			var newVal = hsm.CurrentInputBuffer;
			Assert.AreNotEqual(origVal, newVal);
			Assert.AreEqual(newVal, "");
		}

		[Test, Category("Hotstring"), NonParallelizable]
		public void ResetOnMouseClick()
		{
			hsm.RestoreDefaults(true);
			var newVal = false;
			var origVal = A_DefaultHotstringNoMouse;
			Assert.AreEqual(origVal, false);
			var oldVal = Keyboard.Hotstring("MouseReset", newVal);
			Assert.AreNotEqual(origVal, A_DefaultHotstringNoMouse);
			Assert.AreEqual(A_DefaultHotstringNoMouse, !newVal);
			Assert.AreEqual(origVal.Ab(), !oldVal.Ab());
			//Reset to what it was for the sake of other tests in this class.
			_ = Keyboard.Hotstring("MouseReset", true);
		}

		[Test, Category("Hotstring"), Category("Internal"), NonParallelizable]
		public void EndCharMatching()
		{
			ResetHotstringMatchState();
			var immediate = AddHotstringForMatchTest("*:", "kssuite");
			Assert.AreEqual(immediate, MatchHotstring("kssuite"));

			ResetHotstringMatchState();
			var endChar = AddHotstringForMatchTest("", "ksend");
			Assert.IsNull(MatchHotstring("ksend"));
			_ = Keyboard.Hotstring("Reset");
			Assert.AreEqual(endChar, MatchHotstring("ksend "));
		}

		[Test, Category("Hotstring"), Category("Internal"), NonParallelizable]
		public void EndingCharacters()
		{
			ResetHotstringMatchState();
			var periodEndChar = AddHotstringForMatchTest("", "ksdot");
			Assert.AreEqual(periodEndChar, MatchHotstring("ksdot."));

			ResetHotstringMatchState();
			var tabEndChar = AddHotstringForMatchTest("", "kstab");
			Assert.AreEqual(tabEndChar, MatchHotstring("kstab\t"));
		}

		[Test, Category("Hotstring"), Category("Internal"), NonParallelizable]
		public void InsideWordMatching()
		{
			ResetHotstringMatchState();
			AddHotstringForMatchTest("", "ksword");
			Assert.IsNull(MatchHotstring("prefixksword "));

			ResetHotstringMatchState();
			var insideWord = AddHotstringForMatchTest("?:", "ksword");
			Assert.AreEqual(insideWord, MatchHotstring("prefixksword "));
		}

		[Test, Category("Hotstring"), Category("Internal"), NonParallelizable]
		public void CaseSensitiveMatching()
		{
			ResetHotstringMatchState();
			var caseSensitive = AddHotstringForMatchTest("C:", "AbC");
			Assert.AreEqual(caseSensitive, MatchHotstring("AbC "));

			_ = Keyboard.Hotstring("Reset");
			Assert.IsNull(MatchHotstring("abc "));
		}

		[SetUp, Category("Hotstring")]
		public void Setup()
		{
			mainContext = UseQueuedMainContext();
			_ = Keyboard.Hotstring("*0");
			_ = Keyboard.Hotstring("C0");
			_ = Keyboard.Hotstring("?0");
			_ = Keyboard.Hotstring("B");
			_ = Keyboard.Hotstring("O0");
			_ = Keyboard.Hotstring("R0");
			_ = Keyboard.Hotstring("T0");
			_ = Keyboard.Hotstring("S0");
			//_ = Keyboard.Hotstring("SI");
			_ = Keyboard.Hotstring("Z0");
			_ = Keyboard.Hotstring("K0");
			_ = Keyboard.Hotstring("P0");
			_ = Keyboard.Hotstring("EndChars", "-()[]{}:;'\"/\\,.?!\r\n \t");
			hsm.RestoreDefaults(true);
			hsm.ClearHotstrings();
		}

		private void ResetHotstringMatchState()
		{
			_ = Keyboard.Hotstring("Reset");
			hsm.ClearHotstrings();
			hsm.RestoreDefaults(true);
		}

		private HotstringDefinition AddHotstringForMatchTest(string optionPrefix, string trigger)
		{
			var normalizedPrefix = string.IsNullOrEmpty(optionPrefix) ? ":" : optionPrefix;
			var name = $":{normalizedPrefix}{trigger}";
			var options = $"{normalizedPrefix}{trigger}";
			return (HotstringDefinition)Keysharp.Runtime.Keyboard.HotstringManager.AddHotstring(name, null, options, trigger, trigger.ToUpperInvariant(), false);
		}

		private HotstringDefinition MatchHotstring(string typed)
		{
			hsm.AddChars(typed);
			return hsm.MatchHotstring();
		}
	}
}
