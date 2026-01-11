using static Keysharp.Core.Common.Keyboard.KeyboardUtils;

namespace Keysharp.Core.Common.Input
{
	internal class CollectInputState
	{
		internal nint activeWindow;
		internal char[] ch;
		internal int charCount;
		internal bool earlyCollected, used_dead_key_non_destructively;
		internal nint keyboardLayout;
	};

	internal class InputData
	{
		internal UITimer inputTimer;
	}

	internal class InputType//This is also Windows specific, and needs to eventually be made into a common base with derived OS specific classes.//TODO
	{
		internal bool backspaceIsUndo = true;
		internal bool beforeHotkeys;
		internal string buffer = "";
		internal int bufferLengthMax = 1023;
		internal bool caseSensitive;
		internal bool endCharMode;
		internal string endChars = "";
		internal uint endCharsMax;
		internal bool endingBySC;
		internal char endingChar;
		internal int endingMatchIndex;
		internal uint endingMods;
		internal bool endingRequiredShift;
		internal uint endingSC;
		internal uint endingVK;
		internal bool findAnywhere;
		internal uint[] keySC = new uint[HookThread.SC_ARRAY_COUNT];
		internal uint[] keyVK = new uint[HookThread.VK_ARRAY_COUNT];
		internal List<string> match = [];
		internal uint minSendLevel;
		internal bool notifyNonText;
		internal InputType prev;
		internal InputObject scriptObject;
		internal InputStatusType status = InputStatusType.Off;
		internal int timeout;
		internal DateTime timeoutAt;
		internal bool transcribeModifiedKeys;
		internal bool visibleText, visibleNonText = true;

		internal bool BeforeHotkeys => beforeHotkeys;

		internal InputType(InputObject io, string options, string endKeys, string matchList)
		{
			scriptObject = io;
			ParseOptions(options);
			SetKeyFlags(endKeys);
			var splits = matchList.Split(Comma);

			for (var i = 0; i < splits.Length; i++)
			{
				var split = splits[i];

				if (split.Length > 0)
				{
					match.Add(split);
				}
				else if (match.Count > 1)//Empty entry, which means the preceding entry ended with a literal comma.
				{
					var s = match[match.Count - 1];
					match[match.Count - 1] = s + ',';
				}
				else
					match.Add(",");
			}
		}

		internal void CollectChar(string ch, int charCount)
		{
			var end = Math.Min(ch.Length, charCount);

			for (var i = 0; i < end; ++i)
			{
				if (endChars.Contains(ch[i], caseSensitive ? StringComparison.CurrentCulture : StringComparison.OrdinalIgnoreCase))
				{
					EndByChar(ch[i]);
					return;
				}

				if (buffer.Length == bufferLengthMax)
				{
					if (buffer.Length == 0) // For L0, collect nothing but allow OnChar, etc.
						return;

					break;
				}

				buffer += ch[i];
			}

			// Check if the buffer now matches any of the key phrases, if there are any:
			if (findAnywhere)
			{
				if (caseSensitive)
				{
					for (var i = 0; i < match.Count; ++i)
					{
						if (buffer.Contains(match[i], StringComparison.CurrentCulture))
						{
							EndByMatch(i);
							return;
						}
					}
				}
				else // Not case sensitive.
				{
					for (var i = 0; i < match.Count; ++i)
					{
						if (buffer.Contains(match[i], StringComparison.OrdinalIgnoreCase))
						{
							EndByMatch(i);
							return;
						}
					}
				}
			}
			else // Exact match is required
			{
				if (caseSensitive)
				{
					for (var i = 0; i < match.Count; ++i)
					{
						if (string.Compare(buffer, match[i], StringComparison.CurrentCulture) == 0)
						{
							EndByMatch(i);
							return;
						}
					}
				}
				else // Not case sensitive.
				{
					for (var i = 0; i < match.Count; ++i)
					{
						// v1.0.43.03: Changed to locale-insensitive search.  See similar v1.0.43.03 comment above for more details.
						if (string.Compare(buffer, match[i], StringComparison.InvariantCultureIgnoreCase) == 0)
						{
							EndByMatch(i);
							return;
						}
					}
				}
			}

			// Otherwise, no match found.
			if (buffer.Length >= bufferLengthMax)
				EndByLimit();
		}

		internal void EndByChar(char ch)
		{
			endingChar = ch;
			// The other EndKey related fields are ignored when Char is non-zero.
			EndByReason(InputStatusType.TerminatedByEndKey);
		}

		internal void EndByKey(uint vk, uint sc, bool bySC, bool requiredShift)
		{
			endingVK = vk;
			endingSC = sc;
			endingBySC = bySC;
			endingRequiredShift = requiredShift;
			endingChar = (char)0; // Must be zero if the above are to be used.
			EndByReason(InputStatusType.TerminatedByEndKey);
		}

		internal void EndByLimit() => EndByReason(InputStatusType.LimitReached);

		internal void EndByMatch(int matchIndex)
		{
			endingMatchIndex = matchIndex;
			EndByReason(InputStatusType.TerminatedByMatch);
		}

		internal void EndByTimeout() => EndByReason(InputStatusType.TimedOut);

		internal string GetEndReason(ref string keyBuf)
		{
			var script = Script.TheScript;

			if (script.HookThread is HookThread hook && hook.kbdMsSender != null)
			{
				switch (status)
				{
					case InputStatusType.TimedOut:
						return "Timeout";

					case InputStatusType.TerminatedByMatch:
						return "Match";

					case InputStatusType.TerminatedByEndKey:
					{
						var keyName = keyBuf;

						if (keyName == null)
							return "EndKey";

						if (endingChar != '\0')
						{
							keyName = endingChar.ToString();
						}
						else if (endingRequiredShift)
						{
							// Since the only way a shift key can be required in our case is if it's a key whose name
							// is a single char (such as a shifted punctuation mark), use a diff. method to look up the
							// key name based on fact that the shift key was down to terminate the input.  We also know
							// that the key is an EndingVK because there's no way for the shift key to have been
							// required by a scan code based on the logic (above) that builds the end_key arrays.
							// MSDN: "Typically, ToAscii performs the translation based on the virtual-key code.
							// In some cases, however, bit 15 of the uScanCode parameter may be used to distinguish
							// between a key press and a key release. The scan code is used for translating ALT+
							// number key combinations.
							var state = new byte[256];
							var ch = new char[2];
							state[(int)Keys.ShiftKey] |= 0x80; // Indicate that the neutral shift key is down for conversion purposes.
							var active_window_keybd_layout = hook.kbdMsSender.GetFocusedKeybdLayout(0);
							var count = ToUnicode(endingVK, hook.MapVkToSc(endingVK), state // Nothing is done about ToAsciiEx's dead key side-effects here because it seems to rare to be worth it (assuming its even a problem).
										, ch, script.menuIsVisible != MenuType.None ? 1u : 0u, active_window_keybd_layout); // v1.0.44.03: Changed to call ToAsciiEx() so that active window's layout can be specified (see hook.cpp for details).
							keyName = keyName.Substring(0, count);
						}
						else
						{
							keyName = "";

							if (endingBySC)
								keyName = hook.SCtoKeyName(endingSC, false);

							if (keyName?.Length == 0)
								keyName = hook.VKtoKeyName(endingVK, !endingBySC);

							if (keyName?.Length == 0)
								keyName = "sc" + endingSC.ToString("X3");
						}

						keyBuf = keyName;
						return "EndKey";
					}

					case InputStatusType.LimitReached:
						return "Max";

					case InputStatusType.Off:
						return "Stopped";
				}
			}

			return DefaultObject;
		}

		internal bool InProgress() => status == InputStatusType.InProgress;

		internal InputType InputFindLink(InputType input)
		{
			var script = Script.TheScript;

			if (script.input == input)
				return script.input;
			else
				for (var i = script.input; input != null; i = i.prev)
					if (i.prev == input)
						return i.prev;

			return null;//input is not valid (faked AHK_INPUT_END message?) or not active.
		}

		internal InputType InputRelease()
		{
			var script = Script.TheScript;
			var ht = script.HookThread;

			// Input should already have ended prior to this function being called.
			// Otherwise, removal of aInput from the chain will end input collection.
			if (script.input == this)
			{
				script.input = prev;
			}
			else
			{
				for (var input = script.input; ; input = input.prev)
				{
					if (input == null)
						return null; // aInput is not valid (faked AHK_INPUT_END message?) or not active.

					if (input.prev == this)
					{
						input.prev = prev;
						break;
					}
				}
			}

			// Ensure any pending use of aInput by the hook is finished.
			ht.WaitHookIdle();
			prev = null;

			if (scriptObject != null)
			{
				HotkeyDefinition.MaybeUninstallHook();

				if (scriptObject.OnEnd != null)
					return this; // Return for caller to call OnEnd and Release.

				//Original called Release() on ScriptObject, and the comments specifically differentiate between that and setting to null.
				//So just don't do anything here.
				// The following is not done because this Release() is only to counteract an AddRef() in
				// InputStart().  ScriptObject != NULL indicates this input_type is actually embedded in
				// the InputObject and as such the link should never be broken until both are deleted.
				//aInput->ScriptObject = NULL;
				//Seems extreme to do this, and the script should exit on its own if its not persistent.
				//script.ExitIfNotPersistent(Flow.ExitReasons.Exit); // In case this InputHook was the only thing keeping the script running.
			}

			return null;
		}

		internal void InputStart()
		{
			var script = Script.TheScript;

			// Set or update the timeout timer if needed.  The timer proc takes care to end
			// only those inputs which are due, and will reset or kill the timer as needed.
			if (timeout > 0)
				SetTimeoutTimer();

			// It is possible for &input to already be in the list if AHK_INPUT_END is still
			// in the message queue, in which case it must be removed from its current position
			// to prevent the list from looping back on itself.
			_ = InputUnlinkIfStopped(this);
			prev = script.input;
			Start();
			script.input = this; // Signal the hook to start the input.

			if (beforeHotkeys)
				++script.inputBeforeHotkeysCount;

			HotkeyDefinition.InstallKeybdHook(); // Install the hook (if needed).
		}

		internal InputType InputUnlinkIfStopped(InputType input)
		{
			InputType temp = null;
			var script = Script.TheScript;

			if (input == null)
				return null;

			if (script.input == input)
			{
				temp = script.input;
				script.input = temp.prev;
			}
			else
			{
				for (var i = script.input; i != null; i = i.prev)
				{
					if (i.prev == input)
					{
						if (!input.InProgress())
						{
							temp = i.prev;
							script.HookThread.WaitHookIdle();
							i.prev = input.prev;
						}
					}
				}
			}

			if (temp != null)
				temp.prev = null;//Prev has been detached, so the caller cannot use this to iterate.

			return temp;
		}

		internal bool IsInteresting(ulong dwExtraInfo)
		{
			char? ch = null;
			return minSendLevel == 0 ? true : KeyboardMouseSender.HotInputLevelAllowsFiring(minSendLevel - 1, dwExtraInfo, ref ch);
		}

		internal void ParseOptions(string options)
		{
			for (var i = 0; i < options.Length; i++)
			{
				switch (char.ToUpper(options[i]))
				{
					case 'B':
						backspaceIsUndo = false;
						break;

					case 'C':
						caseSensitive = true;
						break;

					case 'H':
						beforeHotkeys = true;
						break;

					case 'I':
						minSendLevel = (options[i + 1] <= '9' && options[i + 1] >= '0') ? uint.Parse(options.AsSpan(i + 1).BeginNums()) : 1u;
						break;

					case 'M':
						transcribeModifiedKeys = true;
						break;

					case 'L':
						// Use atoi() vs. ATOI() to avoid interpreting something like 0x01C as hex
						// when in fact the C was meant to be an option letter:
						bufferLengthMax = int.Parse(options.AsSpan(i + 1).BeginNums());

						if (bufferLengthMax < 0)
							bufferLengthMax = 0;

						break;

					case 'T':
						var sub = options.AsSpan(i + 1);
						timeout = (int)(double.Parse(sub.BeginNums(true)) * 1000);
						break;

					case 'V':
						visibleText = true;
						visibleNonText = true;
						break;

					case '*':
						findAnywhere = true;
						break;

					case 'E':
						// Interpret single-character keys as characters rather than converting them to VK codes.
						// This tends to work better when using multiple keyboard layouts, but changes behavior:
						// for instance, an end char of "." cannot be triggered while holding Alt.
						endCharMode = true;
						break;
				}
			}
		}

		internal void SetKeyFlags(string keys, bool endKeyMode = true, uint flagsRemove = 0u, uint flagsAdd = HookThread.END_KEY_ENABLED)
		{
			//While this may have been easier and more concise to do C# style, there are extremely hard to decipher details in the original which make exact duplication
			//of the behavior very unlikely. So it's copied verbatim and ported to ensure consistent functionality.
			uint? modifiersLR = 0u;
			int keyTextLength;
			var singleCharCount = 0u;
			//TCHAR* end_pos, single_char_string[2];
			var endPos = 0;
			var singleCharString = "";
			var endcharMode = endKeyMode && endCharMode;
			var vk = 0u;
			var sc = 0u;
			var vkByNumber = false;
			bool? scByNumber = false;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.

			for (var i = 0; i < keys.Length; ++i) // This a modified version of the processing loop used in SendKeys().
			{
				vk = 0; // Set default.  Not strictly necessary but more maintainable.
				singleCharString = "";  // Set default as "this key name is not a single-char string".
				var ch = keys[i];
				//var sub = keys.Substring(i + 1);
				var sub = keys.AsSpan().Slice(i + 1);

				switch (ch)
				{
					case '}': continue;  // Important that these be ignored.

					case '{':
					{
						endPos = sub.IndexOf('}');

						if (endPos == -1)
							continue;  // Do nothing, just ignore the unclosed '{' and continue.

						if ((keyTextLength = endPos - i - 1) == 0)
						{
							if (sub.Length > 1 && sub[1] == '}') // The string "{}}" has been encountered, which is interpreted as a single "}".
							{
								++endPos;
								keyTextLength = 1;
							}
							else // Empty braces {} were encountered.
								continue;  // do nothing: let it proceed to the }, which will then be ignored.
						}

						if (keyTextLength == 1) // A single-char key name, such as {.} or {{}.
						{
							if (endcharMode) // Handle this single-char key name by char code, not by VK.
							{
								// Although it might be sometimes useful to treat "x" as a character and "{x}" as a key,
								// "{{}" and "{}}" can't be included without the extra braces.  {vkNN} can still be used
								// to handle the key by VK instead of by character.
								singleCharCount++;
								continue; // It will be processed by another section.
							}

							singleCharString = keys[i + 1].ToString(); // Only used when vk != 0.
						}

						//end_pos = '\0';  // temporarily terminate the string here.
						scByNumber = false; // Set default.
						modifiersLR = 0;  // Init prior to below.
						// Handle the key by VK if it was given by number, such as {vk26}.
						// Otherwise, for any key name which has a VK shared by two possible SCs
						// (such as Up and NumpadUp), handle it by SC so it's identified correctly.
						var nextkey = sub.Slice(0, endPos).ToString();
						vk = ht.TextToVK(nextkey, ref modifiersLR, true, true, GetKeyboardLayout(0));

						if (vk != 0)
						{
							vkByNumber = nextkey.StartsWith("vk", StringComparison.OrdinalIgnoreCase);

							if (!vkByNumber && (sc = ht.MapVkToSc(vk, true)) != 0)
							{
								sc ^= 0x100; // Convert sc to the primary scan code, which is the one named by end_key.
								vk = 0; // Handle it only by SC.
							}
						}
						else
							// No virtual key, so try to find a scan code.
							sc = ht.TextToSC(nextkey, ref scByNumber);

						i += endPos;  // In prep for ++i at the top of the loop.
						break; // Break out of the switch() and do the vk handling beneath it (if there is a vk).
					}

					default:
						if (endcharMode)
						{
							singleCharCount++;
							continue; // It will be processed by another section.
						}

						singleCharString = ch.ToString();
						modifiersLR = 0u;  // Init prior to below.
						vk = ht.TextToVK(singleCharString, ref modifiersLR, true, true, GetKeyboardLayout(0));
						vkByNumber = false;
						scByNumber = false;
						break;
				} // switch()

				if (vk != 0) // A valid virtual key code was discovered above.
				{
					// Insist the shift key be down to form genuinely different symbols --
					// namely punctuation marks -- but not for alphabetic chars.
					if (singleCharString.Length == 1 && endKeyMode && !char.IsLetter(singleCharString[0])) // v1.0.46.05: Added check for "*single_char_string" so that non-single-char strings like {F9} work as end keys even when the Shift key is being held down (this fixes the behavior to be like it was in pre-v1.0.45).
					{
						// Now we know it's not alphabetic, and it's not a key whose name
						// is longer than one char such as a function key or numpad number.
						// That leaves mostly just the number keys (top row) and all
						// punctuation chars, which are the ones that we want to be
						// distinguished between shifted and unshifted:
						if ((modifiersLR & (MOD_LSHIFT | MOD_RSHIFT)) != 0)
							keyVK[vk] |= HookThread.END_KEY_WITH_SHIFT;
						else
							keyVK[vk] |= HookThread.END_KEY_WITHOUT_SHIFT;
					}
					else
					{
						keyVK[vk] = (keyVK[vk] & ~flagsRemove) | flagsAdd;
						// Apply flag removal to this key's SC as well.  This is primarily
						// to support combinations like {All} +E, {LCtrl}{RCtrl} -E.
						uint temp_sc;

						if (flagsRemove != 0 && !vkByNumber && (temp_sc = ht.MapVkToSc(vk)) != 0)
						{
							keySC[temp_sc] &= ~flagsRemove; // But apply aFlagsAdd only by VK.
							// Since aFlagsRemove implies ScriptObject != NULL and !vk_by_number
							// was also checked, that implies vk_to_sc(vk, true) was already called
							// and did not find a secondary SC.
						}
					}
				}

				if (sc != 0 || scByNumber.IsTrue()) // Fixed for v1.1.33.02: Allow sc000 for setting/unsetting flags for any events that lack a scan code.
				{
					keySC[sc] = (keySC[sc] & ~flagsRemove) | flagsAdd;

					// If specified by name, apply flag removal to this key's VK as well.
					if (flagsRemove != 0 && !scByNumber.IsTrue() && (vk = ht.MapScToVk(sc)) != 0)
						keyVK[vk] &= ~flagsRemove;
				}
			} // for()

			if (singleCharCount != 0)  // See single_char_count++ above for comments.
			{
				if (singleCharCount > endCharsMax)
					endCharsMax = singleCharCount;

				for (var i = 0; i < keys.Length; ++i)
				{
					var ch = keys[i];

					if (ch == '{' && i < keys.Length - 1)
					{
						endPos = keys.IndexOf('}', i + 1);

						if (endPos != -1)
						{
							if (endPos == i + 1 && endPos < keys.Length - 1 && keys[endPos + 1] == '}') // {}}
								endPos++;

							if (endPos == i + 2)
								endChars += keys[i + 1]; // Copy the single character from between the braces.

							i = endPos; // Skip '{key'.  Loop does ++src to skip the '}'.
						}
					}
					else if (ch == '}')// Otherwise, just ignore the '{'.
						continue;

					endChars += keys[i];
				}
			}
			else if (endKeyMode && endCharsMax == 0) // single_char_count is false
			{
				endChars = "";
			}
		}

		internal void SetTimeoutTimer()
		{
			var script = Script.TheScript;
			var now = DateTime.UtcNow;
			timeoutAt = now.AddMilliseconds(timeout);

			if (!script.inputTimerExists || timeout < (script.inputTimeoutAt - now).TotalMilliseconds)
			{
				var inputTimer = script.InputData.inputTimer;
				script.inputTimeoutAt = timeoutAt;

				if (inputTimer == null)
				{
					inputTimer = new UITimer();
#if WINDOWS
					inputTimer.Tick += InputTimer_Tick;
#else
					inputTimer.Elapsed += InputTimer_Tick;
#endif
					script.InputData.inputTimer = inputTimer;
				}

				inputTimer.Interval = timeout;
				inputTimer.Start();
				script.inputTimerExists = true;
			}
		}

		internal void Start() => status = InputStatusType.InProgress;

		internal void Stop() => EndByReason(InputStatusType.Off);

		private void EndByReason(InputStatusType aReason)
		{
			var script = Script.TheScript;

			if (script.HookThread is HookThread hook && hook.kbdMsSender != null)
			{
				endingMods = hook.kbdMsSender.modifiersLRLogical; // Not relevant to all end reasons, but might be useful anyway.
				status = aReason;

				if (beforeHotkeys)
					--script.inputBeforeHotkeysCount;

				// It's done this way rather than calling InputRelease() directly...
				// ...so that we can rely on MsgSleep() to create a new thread for the OnEnd event.
				// ...because InputRelease() can't be called by the hook thread.
				// ...because some callers rely on the list not being broken by this call.
				_ = hook.PostMessage(
						new KeysharpMsg()
				{
					message = (uint)UserMessages.AHK_INPUT_END,
					obj = this
				});
			}
		}

		private void InputTimer_Tick(object sender, EventArgs e)
		{
			var script = Script.TheScript;
			var inputTimer = script.InputData.inputTimer;
			inputTimer.Stop();
			var newTimerPeriod = 0;

			for (var input = script.input; input != null; input = input.prev)
			{
				if (input.timeout != 0 && input.InProgress())
				{
					var timeLeft = (int)(input.timeoutAt - DateTime.UtcNow).TotalMilliseconds;

					if (timeLeft <= 0)
						input.EndByTimeout();
					else if (timeLeft < newTimerPeriod || newTimerPeriod == 0)
						newTimerPeriod = timeLeft;
				}
			}

			if (newTimerPeriod != 0)
			{
				inputTimer.Interval = newTimerPeriod;
				script.inputTimeoutAt = DateTime.UtcNow.AddMilliseconds(newTimerPeriod);
				inputTimer.Start();
			}
			else
			{
				if (script.inputTimerExists)
				{
					inputTimer.Stop();
					script.inputTimerExists = false;
				}
			}
		}
	}

	internal enum InputStatusType
	{
		Off,
		InProgress,
		TimedOut,
		TerminatedByMatch,
		TerminatedByEndKey,
		LimitReached
	}
}