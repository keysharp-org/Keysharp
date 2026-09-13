using Keysharp.Builtins;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;

namespace Keysharp.Internals.Input
{
	internal class CollectInputState
	{
		internal nint activeWindow;
		internal char[] ch;
		internal int charCount;
		internal bool earlyCollected, used_dead_key_non_destructively;
		internal nint keyboardLayout;
	};

	internal class InputData : IDisposable
	{
		internal UITimer inputTimer;
		// Script threads start inputs while the scheduled end unlinks them and the hook thread ends them, so edits of the
		// input chain and an input's end take this; the hook thread's walks only read the chain.
		internal readonly Lock chainGate = new();

		public void Dispose()
		{
			var timer = inputTimer;
			inputTimer = null;
			timer?.Stop();
			timer?.Dispose();
		}
	}

	internal class InputType//This is also Windows specific, and needs to eventually be made into a common base with derived OS specific classes.//TODO
	{
		private readonly Script owner;
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
		internal InputHook scriptObject;
		// Volatile: the hook thread ends an input and queued callbacks re-read this when they would run.
		internal volatile InputStatusType status = InputStatusType.Off;
		internal int timeout;
		internal DateTime timeoutAt;
		internal bool transcribeModifiedKeys;
		internal bool visibleText, visibleNonText = true;
		internal bool visibleMouseMove = true;

		internal bool BeforeHotkeys => beforeHotkeys;

		// True when this input needs the low-level mouse hook installed: a mouse callback is set,
		// movement is being suppressed, or any mouse/wheel VK has been given Input key options
		// (end-key, suppress, etc.) via KeyOpt/end keys.
		internal bool MouseIsNeeded
		{
			get
			{
				if (!visibleMouseMove)
					return true;

				if (scriptObject != null
						&& (scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_MOUSEDOWN)?.Callback != null
							|| scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_MOUSEUP)?.Callback != null
							|| scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_MOUSEMOVE)?.Callback != null))
					return true;

				for (var v = 0u; v < keyVK.Length; ++v)
					if ((MouseUtils.IsMouseVK(v) || MouseUtils.IsWheelVK(v))
							&& (keyVK[v] & (HookThread.END_KEY_ENABLED | HookThread.INPUT_KEY_OPTION_MASK)) != 0)
						return true;

				return false;
			}
		}

		// True when this input needs the low-level keyboard hook: a keyboard callback, a match list,
		// keyboard text/non-text suppression, or any keyboard (non-mouse) end key / KeyOpt all require it.
		// A bare input with none of these still needs it UNLESS it is purely a mouse observer
		// (see MouseIsNeeded), because the default options suppress typed text, which needs the hook.
		internal bool KeyboardIsNeeded
		{
			get
			{
				if (scriptObject != null
						&& (scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_CHAR)?.Callback != null
							|| scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_KEYDOWN)?.Callback != null
							|| scriptObject.GetCallbackSlot(UserMessages.AHK_INPUT_KEYUP)?.Callback != null))
					return true;

				if (match.Count > 0 || !visibleText || !visibleNonText)
					return true;

				for (var s = 0u; s < keySC.Length; ++s)
					if ((keySC[s] & (HookThread.END_KEY_ENABLED | HookThread.INPUT_KEY_OPTION_MASK)) != 0)
						return true;

				for (var v = 0u; v < keyVK.Length; ++v)
					if (!MouseUtils.IsMouseVK(v) && !MouseUtils.IsWheelVK(v)
							&& (keyVK[v] & (HookThread.END_KEY_ENABLED | HookThread.INPUT_KEY_OPTION_MASK)) != 0)
						return true;

				return !MouseIsNeeded;
			}
		}

		internal InputType(InputHook io, string options, string endKeys, string matchList)
		{
			owner = Script.TheScript;
			scriptObject = io;
			ParseOptions(options);
			SetKeyFlags(endKeys);
			SetMatchList(matchList);
		}

		// Faithful port of AHK input_type::SetMatchList: comma-separated phrases, where two
		// consecutive commas are a single literal comma within a phrase, and empty phrases
		// (e.g. from a leading/trailing/lone comma) are omitted.  The previous Split(',')
		// approach broke both rules and, worse, produced a spurious "," phrase for every
		// InputHook created without a match list.
		internal void SetMatchList(string matchList)
		{
			match.Clear();

			if (string.IsNullOrEmpty(matchList))
				return;

			var sb = new StringBuilder();

			for (var i = 0; i < matchList.Length; i++)
			{
				var ch = matchList[i];

				if (ch != ',') // Not a comma, so just copy it over.
				{
					_ = sb.Append(ch);
					continue;
				}

				if (i + 1 < matchList.Length && matchList[i + 1] == ',') // Double comma becomes a single literal comma.
				{
					_ = sb.Append(',');
					++i; // Skip the second comma of the pair.
					continue;
				}

				// Otherwise this is a delimiting comma; omit empty phrases.
				if (sb.Length > 0)
				{
					match.Add(sb.ToString());
					_ = sb.Clear();
				}
			}

			if (sb.Length > 0) // Terminate the last item.
				match.Add(sb.ToString());
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
			var script = owner;

			// A running input has no reason, as in AHK; a null here would make `!ih.EndReason` raise UnsetError. An
			// input that was never started reads "Stopped", also as in AHK.
			if (status == InputStatusType.InProgress)
				return "";

			if (status == InputStatusType.Off)
				return EventSubscriptionBase.EndReasonStopped;

			if (status == InputStatusType.Failed)
				return "Failed";

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
							var active_window_keybd_layout = Platform.Keys.GetKeyboardLayout();
							var count = ToUnicode(endingVK, KeyCodes.MapVkToSc(endingVK), state // Nothing is done about ToAsciiEx's dead key side-effects here because it seems to rare to be worth it (assuming its even a problem).
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
				}
			}

			return "";
		}

		internal bool InProgress() => status == InputStatusType.InProgress;

		internal Lock ChainGate => owner.InputData.chainGate;

		/// <summary>Releases the persistence roots <paramref name="hook"/>'s start took, unless the input is running
		/// again: one step with InputStart taking them, so a start from another thread cannot slip in between.</summary>
		internal void ReleaseRootsIfEnded(InputHook hook)
		{
			lock (ChainGate)
				if (!InProgress())
					hook?.DeactivateCallbackPersistence();
		}

		internal InputType InputRelease()
		{
			var script = owner;

			// Not in the chain: a stale end, which releases nothing.
			if (InputUnlinkIfStopped(this) == null)
				return null;

			script.HookThread.RefreshPlatformKeyGrabs();

			if (scriptObject == null)
				return null;

			HotkeyDefinition.MaybeUninstallHook(script);

			// Returned whether or not there is an OnEnd: the caller releases the persistence roots InputStart took,
			// which AHK does unconditionally, then runs OnEnd if one is set.
			return this;
		}

		/// <summary>Starts the input with an empty buffer, whether it never ran or has ended; does nothing to one
		/// already running, which two threads starting it at once would otherwise link twice.</summary>
		internal void InputStart()
		{
			if (!LinkForStart())
				return;

			var script = owner;

			if (KeyboardIsNeeded)
				HotkeyDefinition.InstallKeybdHook(script); // Keyboard hook only when collecting/suppressing keyboard.

			if (MouseIsNeeded)
				HotkeyDefinition.InstallMouseHook(script); // Also install the mouse hook when collecting mouse events.

			script.HookThread.RefreshPlatformKeyGrabs();
		}

		/// <summary>The chain half of <see cref="InputStart"/>: links the input at the top with an empty buffer and takes
		/// its place in the counts. False when it was already running, or cannot run here.</summary>
		internal bool LinkForStart()
		{
			var script = owner;

			lock (ChainGate)
			{
				if (InProgress())
					return false;

#if LINUX
				// A headless Linux host can never install the input hooks, so an input started there would report itself
				// running while collecting nothing.
				if (Script.IsHeadless)
				{
					status = InputStatusType.Failed;
					return false;
				}
#endif

				buffer = "";

				// Set or update the timeout timer if needed.  The timer proc takes care to end
				// only those inputs which are due, and will reset or kill the timer as needed.
				if (timeout > 0)
					SetTimeoutTimer();

				// It is possible for &input to already be in the list if AHK_INPUT_END is still
				// in the message queue, in which case it must be removed from its current position
				// to prevent the list from looping back on itself.
				_ = UnlinkIfStoppedLocked(this);
				prev = script.input;
				Start();
				scriptObject?.ActivateCallbackPersistence();
				script.input = this; // Signal the hook to start the input.

				if (beforeHotkeys)
					_ = Interlocked.Increment(ref script.inputBeforeHotkeysCount);

				return true;
			}
		}

		/// <summary>
		/// Finds <paramref name="input"/> in the chain and unlinks it unless it is running again, as AHK's
		/// InputUnlinkIfStopped does: an input restarted while its end was still queued has already been moved to the top
		/// by InputStart and must stay linked. Returns the input when it was in the chain, null when it was not.
		/// </summary>
		internal InputType InputUnlinkIfStopped(InputType input)
		{
			lock (ChainGate)
				return UnlinkIfStoppedLocked(input);
		}

		private InputType UnlinkIfStoppedLocked(InputType input)
		{
			if (input == null)
				return null;

			var script = owner;

			for (InputType i = script.input, above = null; i != null; above = i, i = i.prev)
			{
				if (i != input)
					continue;

				// The input keeps its prev, so a hook walk standing on it still reaches the rest of the chain, and
				// InputStart sets prev before relinking it. Nothing here waits, so no script code runs mid-edit; the cost is
				// that a walk standing on it while it is relinked at the top passes the inputs above it once more.
				if (!input.InProgress())
				{
					if (above == null)
						script.input = input.prev;
					else
						above.prev = input.prev;
				}

				return input;
			}

			return null;
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
						// Guard against 'I' being the last character (AHK reads the C-string null terminator safely here).
						minSendLevel = (i + 1 < options.Length && options[i + 1] >= '0' && options[i + 1] <= '9')
									   ? uint.Parse(options.AsSpan(i + 1).BeginNums()) : 1u;
						break;

					case 'M':
						transcribeModifiedKeys = true;
						break;

					case 'L':
						// Use atoi() vs. ATOI() to avoid interpreting something like 0x01C as hex
						// when in fact the C was meant to be an option letter.  An empty numeric span
						// (e.g. trailing 'L') maps to 0, mirroring AHK's _ttoi behavior instead of throwing.
						var lnum = options.AsSpan(i + 1).BeginNums();
						bufferLengthMax = lnum.Length > 0 ? int.Parse(lnum) : 0;

						if (bufferLengthMax < 0)
							bufferLengthMax = 0;

						break;

					case 'T':
						var sub = options.AsSpan(i + 1).BeginNums(true);
						timeout = sub.Length > 0 ? (int)(double.Parse(sub) * 1000) : 0;
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
			var script = owner;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.
			var keybdLayout = new KeybdLayoutRef(); // Resolved lazily (once, foreground focused-control layout) only if an end key needs char mapping.

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
						var keySource = KeySource.None;
						_ = ht.TextToVKandSC(nextkey, ref vk, ref sc, ref keySource, ref modifiersLR, keybdLayout, allowVkScPair: false);

						if (vk != 0)
						{
							vkByNumber = (keySource & KeySource.Vk) != 0;

							if (!vkByNumber && (sc = KeyCodes.MapVkToSc(vk, true)) != 0) // This VK maps to two scan codes (e.g. Enter/NumpadEnter); sc = the secondary.
							{
#if WINDOWS
								sc ^= 0x100; // Convert sc to the primary scan code, which is the one named by end_key.
#else
								sc = KeyCodes.MapVkToSc(vk); // evdev has no extended-bit relation; use the primary (named) code directly.
#endif
								vk = 0; // Handle it only by SC.
							}
						}
						else
						{
							scByNumber = (keySource & KeySource.Sc) != 0;
						}

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
						sc = 0;
						var charKeySource = KeySource.None;
						_ = ht.TextToVKandSC(singleCharString, ref vk, ref sc, ref charKeySource, ref modifiersLR, keybdLayout);
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

						if (flagsRemove != 0 && !vkByNumber && (temp_sc = KeyCodes.MapVkToSc(vk)) != 0)
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
					if (flagsRemove != 0 && !scByNumber.IsTrue() && (vk = KeyCodes.MapScToVk(sc)) != 0)
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
			var script = owner;
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
			var script = owner;

			if (script.HookThread is HookThread hook && hook.kbdMsSender != null)
			{
				bool early;

				// The hook thread and the timeout can both end an input: the first end wins, as a hook's first reason does.
				// The before-hotkeys setting is read with it, so the count gives back exactly what the start took.
				lock (ChainGate)
				{
					if (status != InputStatusType.InProgress)
						return;

					endingMods = hook.kbdMsSender.modifiersLRLogical; // Not relevant to all end reasons, but might be useful anyway.
					status = aReason;
					early = beforeHotkeys;
				}

				if (early)
					_ = Interlocked.Decrement(ref script.inputBeforeHotkeysCount);

				hook.RefreshPlatformKeyGrabs();

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
			var script = owner;
			var inputTimer = script.InputData.inputTimer;

			//Null once InputData is disposed. Dispose and this tick both run on the UI thread, but a tick already
			//queued when the script tore down still arrives after it.
			if (inputTimer == null)
				return;

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
		Failed,
		TimedOut,
		TerminatedByMatch,
		TerminatedByEndKey,
		LimitReached
	}
}
