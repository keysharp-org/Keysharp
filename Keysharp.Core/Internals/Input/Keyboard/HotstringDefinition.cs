using Keysharp.Builtins;
using Keysharp.Runtime;

namespace Keysharp.Internals.Input.Keyboard
{
	[PublicHiddenFromUser]
	internal class HotstringDefinition
	{
		internal const int HOTSTRING_BLOCK_SIZE = 1024;
		internal const int HS_BUF_DELETE_COUNT = HS_BUF_SIZE / 2;
		internal const int HS_BUF_SIZE = (MAX_HOTSTRING_LENGTH * 2) + 10;
		internal const int HS_MAX_END_CHARS = 100;
		internal const int HS_SUSPENDED = 0x01;
		internal const int HS_TEMPORARILY_DISABLED = 0x04;
		internal const int HS_TURNED_OFF = 0x02;
		internal const int MAX_HOTSTRING_LENGTH = 40;
		internal const string MAX_HOTSTRING_LENGTH_STR = "40";      // Hard to imagine a need for more than this, and most are only a few chars long.

		internal bool caseSensitive, conformToCase, doBackspace, omitEndChar, endCharRequired
		, detectWhenInsideWord, doReset, suspendExempt, constructedOK;

		internal uint existingThreads, maxThreads;
		internal KeysharpFunc hotCriterion;
		internal long inputLevel;
		internal long priority, keyDelay;
		private readonly CallbackRegistration callbackRegistration = new();
		internal SendModes sendMode;
		internal SendRawModes sendRaw;
		internal string str, replacement;
		internal int suspended;

		public bool CaseSensitive => caseSensitive;

		public bool DoBackspace => doBackspace;

		public bool DoReset => doReset;

		public bool Enabled { get; set; }

		public Options EnabledOptions { get; set; }

		public bool EndCharRequired => endCharRequired;

		public long KeyDelay => keyDelay;

		public string Name { get; set; }

		public bool OmitEndChar => omitEndChar;

		public long Priority => priority;

		public string Replacement => replacement;

		public SendModes SendMode => sendMode;

		public SendRawModes SendRaw => sendRaw;

		public string Sequence { get; }

		public bool SuspendExempt => suspendExempt;
		internal KeysharpFunc funcObj
		{
			get => callbackRegistration.Callback;
			set => callbackRegistration.Set(value, callbackRegistration.OwnerScheduler, suspended == 0 && value != null);
		}
		internal ScriptEventScheduler ownerScheduler
		{
			get => callbackRegistration.OwnerScheduler;
			set => callbackRegistration.Set(funcObj, value, suspended == 0 && funcObj != null);
		}

		internal void SetSuspended(int newSuspended)
		{
			if (suspended == newSuspended)
				return;

			var wasEnabled = suspended == 0;
			suspended = newSuspended;
			var isEnabled = suspended == 0;

			if (wasEnabled == isEnabled)
				return;

			callbackRegistration.SetActive(isEnabled && funcObj != null);
		}

		internal HotstringDefinition(Script script, string sequence, string _replacement)
		{
			Sequence = sequence;
			replacement = _replacement;
			ownerScheduler = script.EventScheduler;
			//EndChars = defEndChars;
		}

		internal HotstringDefinition(Script script, string _name, KeysharpFunc _funcObj, ReadOnlySpan<char> _options, string _hotstring, string _replacement
									 , bool _hasContinuationSection, int _suspend)

		{
			var hm = script.HotstringManager;
			funcObj = _funcObj;
			hotCriterion = script.Threads.CurrentThread.hotCriterion;
			suspended = _suspend;
			ownerScheduler = script.EventScheduler;
			maxThreads = script.AccessorData.maxThreadsPerHotkey;  // The value of g_MaxThreadsPerHotkey can vary during load-time.
			priority = hm.hsPriority;
			keyDelay = hm.hsKeyDelay;
			sendMode = hm.hsSendMode;  // And all these can vary too.
			caseSensitive = hm.hsCaseSensitive;
			conformToCase = hm.hsConformToCase;
			doBackspace = hm.hsDoBackspace;
			omitEndChar = hm.hsOmitEndChar;
			sendRaw = _hasContinuationSection ? SendRawModes.RawText : hm.hsSendRaw;
			endCharRequired = hm.hsEndCharRequired;
			detectWhenInsideWord = hm.hsDetectWhenInsideWord;
			doReset = hm.hsDoReset;
			inputLevel = script.AccessorData.inputLevel;
			suspendExempt = hm.hsSuspendExempt;
			constructedOK = false;
			var unusedX = false; // do not assign  mReplacement if execute_action is true.
			ParseOptions(_options, ref priority, ref keyDelay, ref sendMode, ref caseSensitive, ref conformToCase, ref doBackspace
						 , ref omitEndChar, ref sendRaw, ref endCharRequired, ref detectWhenInsideWord, ref doReset, ref unusedX, ref suspendExempt);
			str = _hotstring;
			Name = _name;

			if (!string.IsNullOrEmpty(_replacement))
				replacement = _replacement;
			else // Leave mReplacement NULL, but make this false so that the hook doesn't do extra work.
				conformToCase = false;

			constructedOK = true; // Done at the very end.
		}

		public override string ToString() => Name;

		internal static void ParseOptions(ReadOnlySpan<char> _options, ref long _priority, ref long _keyDelay, ref SendModes _sendMode
										  , ref bool _caseSensitive, ref bool _conformToCase, ref bool _doBackspace, ref bool _omitEndChar, ref SendRawModes _sendRaw
										  , ref bool _endCharRequired, ref bool _detectWhenInsideWord, ref bool _doReset, ref bool _executeAction, ref bool _suspendExempt)
		{
			// In this case, colon rather than zero marks the end of the string.  However, the string
			// might be empty so check for that too.  In addition, this is now called from
			// IsDirective(), so that's another reason to check for normal string termination.
			var colon = _options.IndexOf(':');
			var opts = _options.Slice(0, colon == -1 ? _options.Length : colon);

			for (var i = 0; i < opts.Length; i++)
			{
				var ch = char.ToUpper(opts[i]);
				var next = i < opts.Length - 1 ? opts.Slice(i + 1) : "";

				switch (ch)
				{
					case '*':
						_endCharRequired = next.Length > 0 && next[0] == '0';
						break;

					case '?':
						_detectWhenInsideWord = next.Length == 0 || next[0] != '0';
						break;

					case 'B':
						_doBackspace = next.Length == 0 || next[0] != '0';
						break;

					case 'C':
						if (next.Length == 0)// treat as plain "C"
						{
							_conformToCase = false;  // No point in conforming if its case sensitive.
							_caseSensitive = true;
						}
						else if (next[0] == '0') // restore both settings to default.
						{
							_conformToCase = true;
							_caseSensitive = false;
							i++;
						}
						else if (next[0] == '1')
						{
							_conformToCase = false;
							_caseSensitive = false;
							i++;
						}
						else//It was just a 'C' followed by another option.
						{
							_conformToCase = false;  // No point in conforming if its case sensitive.
							_caseSensitive = true;
						}

						break;

					case 'O':
						_omitEndChar = next.Length == 0 || next[0] != '0';
						break;

					// For options such as K & P: Use atoi() vs. ATOI() to avoid interpreting something like 0x01C
					// as hex when in fact the C was meant to be an option letter:
					case 'K':
					case 'P':
					{
						var j = 0;

						while (j < next.Length && (next[j] == '-' || char.IsNumber(next[j])))
							j++;

						if (long.TryParse(next.Slice(0, j), out var val))
						{
							if (ch == 'K')
								_keyDelay = val;
							else
								_priority = val;
						}

						i += j;
					}
					break;

					case 'R':
						_sendRaw = (next.Length == 0 || next[0] != '0') ? SendRawModes.Raw : SendRawModes.NotRaw;
						break;

					case 'T':
						_sendRaw = (next.Length == 0 || next[0] != '0') ? SendRawModes.RawText : SendRawModes.NotRaw;
						break;

					case 'S':
					{
						var tempch = (char)0;

						if (next.Length > 0)
						{
							tempch = char.ToUpper(next[0]);

							// Skip over S's sub-letter (if any) to exclude it from  further consideration.
							switch (tempch)
							{
								// There is no means to choose SM_INPUT because it seems too rarely desired (since auto-replace
								// hotstrings would then become interruptible, allowing the keystrokes of fast typists to get
								// interspersed with the replacement text).
								case 'I':
									i++;
									_sendMode = SendModes.InputThenPlay;
									break;

								case 'E':
									i++;
									_sendMode = SendModes.Event;
									break;

								case 'P':
									i++;
									_sendMode = SendModes.Play;
									break;

								default:
									if (tempch == '0')
									{
										i++;
										_suspendExempt = false;
									}
									else
										_suspendExempt = true;

									break;
							}
						}
						else
							_suspendExempt = true;
					}
					break;

					case 'Z':
						_doReset = next.Length == 0 || next[0] != '0';
						break;

					case 'X':
						_executeAction = next.Length == 0 || next[0] != '0';
						break;
						// Otherwise: Ignore other characters, such as the digits that comprise the number after the P option.
				}
			}
		}

		internal bool AnyThreadsAvailable() => existingThreads < maxThreads;

		internal bool CompareHotstring(ReadOnlySpan<char> _hotstring, bool _caseSensitive, bool _detectWhenInsideWord, KeysharpFunc _hotCriterion)
		{
			// hs.mEndCharRequired is not checked because although it affects the conditions for activating
			// the hotstring, ::abbrev:: and :*:abbrev:: cannot co-exist (the latter would always take over).
			return hotCriterion == _hotCriterion // Same #HotIf criterion.
				   && caseSensitive == _caseSensitive // ::BTW:: and :C:BTW:: can co-exist.
				   && detectWhenInsideWord == _detectWhenInsideWord // :?:ion:: and ::ion:: can co-exist.
				   && (_caseSensitive ? _hotstring.SequenceEqual(str.AsSpan()) : str.AsSpan().Equals(_hotstring, StringComparison.OrdinalIgnoreCase));// :C:BTW:: and :C:btw:: can co-exist, but not ::BTW:: and ::btw::.
		}

		internal int ComputeReplacementSkipChars(ReadOnlySpan<char> hsBufSpan, bool finalCharSuppressed, ref CaseConformModes caseMode)
		{
			if (!doBackspace || string.IsNullOrEmpty(replacement) || string.IsNullOrEmpty(str))
				return 0;

			var triggerLength = str.Length;
			var typedStart = hsBufSpan.Length - triggerLength - (endCharRequired ? 1 : 0);

			if (typedStart < 0)
				return 0;

			var finalTriggerCharSuppressed = finalCharSuppressed && !endCharRequired;
			var maxSkip = Math.Max(0, triggerLength - (finalTriggerCharSuppressed ? 1 : 0));
			var firstCharWithCase = -1;

			if (caseMode == CaseConformModes.FirstCap)
			{
				var typedEnd = typedStart + triggerLength;

				for (var i = typedStart; i < typedEnd; i++)
				{
					var c = hsBufSpan[i];

					if (char.IsLower(c) || char.IsUpper(c))
					{
						firstCharWithCase = i;
						break;
					}
				}
			}

			var skipChars = 0;

			while (skipChars < maxSkip && skipChars < replacement.Length)
			{
				var replacementChar = replacement[skipChars];

				if (sendRaw == SendRawModes.NotRaw && "^+!#{}".Contains(replacementChar))
					break;

				var typedChar = hsBufSpan[typedStart + skipChars];
				var isPair = char.IsHighSurrogate(replacementChar)
							 && skipChars + 1 < replacement.Length
							 && skipChars + 1 < maxSkip
							 && typedStart + skipChars + 1 < hsBufSpan.Length
							 && char.IsLowSurrogate(replacement[skipChars + 1])
							 && char.IsHighSurrogate(typedChar)
							 && char.IsLowSurrogate(hsBufSpan[typedStart + skipChars + 1]);

				if (isPair)
				{
					// Match surrogate pairs atomically; case folding on astral characters
					// isn't attempted here (rare for hotstring replacements).
					if (typedChar != replacementChar || hsBufSpan[typedStart + skipChars + 1] != replacement[skipChars + 1])
						break;

					skipChars += 2;
					continue;
				}

				// Don't let a standalone char match half of a surrogate pair on either side.
				if (char.IsHighSurrogate(replacementChar) || char.IsLowSurrogate(replacementChar)
						|| char.IsHighSurrogate(typedChar) || char.IsLowSurrogate(typedChar))
					break;

				if (typedChar != replacementChar)
				{
					if (typedChar != char.ToUpper(replacementChar))
						break;

					if (caseMode != CaseConformModes.AllCaps
							&& !(caseMode == CaseConformModes.FirstCap && typedStart + skipChars == firstCharWithCase))
						break;
				}

				skipChars++;
			}

			if (caseMode == CaseConformModes.FirstCap && firstCharWithCase >= 0 && typedStart + skipChars > firstCharWithCase)
				caseMode = CaseConformModes.None;

			return skipChars;
		}

		internal void DoReplace(Script script, CaseConformModes caseMode, char endChar, uint triggerVk = 0, int skipChars = 0)
		{
			var sb = new StringBuilder();//This might be able to be done more efficiently, but use sb unless performance issues show up.
			var startOfReplacement = 0;
			string sendBuf;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;
			var triggerLength = str?.Length ?? 0;
			skipChars = doBackspace ? Math.Clamp(skipChars, 0, triggerLength) : 0;

			if (doBackspace)
			{
				var backspaceCount = ComputeReplacementBackspaceCount(skipChars);

				for (var i = 0; i < backspaceCount; ++i)
				{
					_ = sb.Append('\b');  // Use raw backspaces, not {BS n}, in case the send will be raw.
					startOfReplacement++;
				}
			}

			if (!string.IsNullOrEmpty(replacement))
			{
				if (skipChars < replacement.Length)
					_ = sb.Append(replacement, skipChars, replacement.Length - skipChars);

				if (caseMode == CaseConformModes.AllCaps)
				{
					sendBuf = sb.ToString().ToUpper();
					_ = sb.Clear();
					_ = sb.Append(sendBuf);
				}
				else if (caseMode == CaseConformModes.FirstCap)
				{
					var b = false;
					sendBuf = sb.ToString();
					_ = sb.Clear();

					for (var i = 0; i < sendBuf.Length; i++)
					{
						if (i < startOfReplacement)
							_ = sb.Append(sendBuf[i]);
						else if (b)
							_ = sb.Append(sendBuf[i]);
						else if (!b)
						{
							_ = sb.Append(char.ToUpper(sendBuf[i]));
							b = true;
						}
					}
				}

				if (!omitEndChar) // The ending character (if present) needs to be sent too.
				{
					// Send the final character in raw mode so that chars such as !{} are sent properly.
					// v1.0.43: Avoid two separate calls to SendKeys because:
					// 1) It defeats the uninterruptibility of the hotstring's replacement by allowing the user's
					//    buffered keystrokes to take effect in between the two calls to SendKeys.
					// 2) Performance: Avoids having to install the playback hook twice, etc.
					if (endCharRequired && endChar != 0) // Must now check mEndCharRequired because LOWORD has been overloaded with context-sensitive meanings.
					{
						// v1.0.43.02: Don't send "{Raw}" if already in raw mode!
						// v1.1.27: Avoid adding {Raw} if it gets switched on within the replacement text.
						if (sendRaw != 0 || replacement.Contains("{Raw}", StringComparison.OrdinalIgnoreCase) || replacement.Contains("{Text}", StringComparison.OrdinalIgnoreCase))
							_ = sb.Append(endChar);
						else
							_ = sb.Append(string.Format("{0}{1}", "{Raw}", endChar));
					}
				}
			}

			sendBuf = sb.ToString();

			if (sendBuf.Length == 0) // No keys to send.
				return;

			// For the following, mSendMode isn't checked because the backup/restore is needed to varying extents
			// by every mode.
			var tv = script.Threads.CurrentThread.configData;
			var oldDelay = tv.keyDelay;
			var oldPressDuration = tv.keyDuration;
			var oldDelayPlay = tv.keyDelayPlay;
			var oldPressDurationPlay = tv.keyDurationPlay;
			var oldSendLevel = tv.sendLevel;
			tv.keyDelay = keyDelay; // This is relatively safe since SendKeys() normally can't be interrupted by a new thread.
			tv.keyDuration = -1L;   // Always -1, since Send command can be used in body of hotstring to have a custom press duration.
			tv.keyDelayPlay = -1L;
			tv.keyDurationPlay = keyDelay; // Seems likely to be more useful (such as in games) to apply mKeyDelay to press duration rather than above.
			// Setting the SendLevel to 0 rather than this->mInputLevel since auto-replace hotstrings are used for text replacement rather than
			// key remapping, which means the user almost always won't want the generated input to trigger other hotkeys or hotstrings.
			// Action hotstrings (not using auto-replace) do get their thread's SendLevel initialized to the hotstring's InputLevel.
			tv.sendLevel = 0L;

			// v1.0.43: The following section gives time for the hook to pass the final keystroke of the hotstring to the
			// system.  This is necessary only for modes other than the original/SendEvent mode because that one takes
			// advantage of the serialized nature of the keyboard hook to ensure the user's final character always appears
			// on screen before the replacement text can appear.
			// By contrast, when the mode is SendPlay (and less frequently, SendInput), the system and/or hook needs
			// another timeslice to ensure that AllowKeyToGoToSystem() actually takes effect on screen (SuppressThisKey()
			// doesn't seem to have this problem).
			if (!(doBackspace || omitEndChar) && sendMode != SendModes.Event) // The final character of the abbreviation (or its EndChar) was not suppressed by the hook.
				Thread.Sleep(0);

			kbdMouseSender.SendKeys(sendBuf, sendRaw, sendMode, 0); // Send the backspaces and/or replacement.
			// Restore original values.
			tv.keyDelay = oldDelay;
			tv.keyDuration = oldPressDuration;
			tv.keyDelayPlay = oldDelayPlay;
			tv.keyDurationPlay = oldPressDurationPlay;
			tv.sendLevel = oldSendLevel;
		}

		internal int ComputeReplacementBackspaceCount(int skipChars)
		{
			var trigger = str ?? "";
			skipChars = Math.Clamp(skipChars, 0, trigger.Length);
			var backspaceCount = trigger.Length - skipChars;

			for (var i = skipChars; i < trigger.Length; i++)
				if (char.IsSurrogatePair(trigger, i))
				{
					i++;
					backspaceCount--;
				}

			// Subtract 1 from backspaces because the final key pressed by the user to make a
			// match was already suppressed by the hook. If a retained prefix is followed by
			// an astral final trigger character, the suppressed low surrogate can leave the
			// high surrogate between that prefix and the replacement; keep one backspace for it.
			if (!endCharRequired
					&& !(skipChars > 0
						 && trigger.Length >= 2
						 && skipChars <= trigger.Length - 2
						 && char.IsHighSurrogate(trigger[trigger.Length - 2])
						 && char.IsLowSurrogate(trigger[trigger.Length - 1])))
				--backspaceCount;

			return Math.Max(backspaceCount, 0);
		}

		internal void ParseOptions(ReadOnlySpan<char> aOptions)
		{
			var unused_X_option = false;
			ParseOptions(aOptions, ref priority, ref keyDelay, ref sendMode, ref caseSensitive, ref conformToCase, ref doBackspace
						 , ref omitEndChar, ref sendRaw, ref endCharRequired, ref detectWhenInsideWord, ref doReset, ref unused_X_option, ref suspendExempt);
		}

		internal ResultType PerformInNewThreadMadeByCaller(long criterionFoundHwnd, CaseConformModes caseMode, char endChar, uint triggerVk, bool recheckCriterionOnReceipt, int skipChars = 0)
		{
			if (ownerScheduler is not { IsDisposed: false } targetScheduler)
				return ResultType.Fail;

			var queuedEvent = new HotstringQueuedEvent(this, targetScheduler, criterionFoundHwnd, recheckCriterionOnReceipt, caseMode, endChar, triggerVk, skipChars);
			_ = targetScheduler.Enqueue(ScriptEventQueue.Interactive, priority, queuedEvent.Execute);
			return ResultType.Ok;
		}

		private sealed class HotstringQueuedEvent(HotstringDefinition definition, ScriptEventScheduler scheduler, long criterionFoundHwnd, bool recheckCriterionOnReceipt, CaseConformModes caseMode, char endChar, uint triggerVk, int skipChars)
		{
			internal ScriptEventExecutionResult Execute()
			{
				if (!definition.AnyThreadsAvailable())
					return ScriptEventExecutionResult.Dropped;

				using var thread = scheduler.StartPseudoThreadScope(definition.priority, false, false, false, ThreadKind.Hotstring);
				if (!thread.Started)
					return thread.Result;

				var script = scheduler.Owner;
				var callbackExecuted = false;

				try
				{
					var hwndCritFound = criterionFoundHwnd;

					if (recheckCriterionOnReceipt && definition.hotCriterion != null)
					{
						hwndCritFound = HotkeyDefinition.HotCriterionAllowsFiring(script, definition.hotCriterion, definition.Name);

						if (hwndCritFound == 0)
							return ScriptEventExecutionResult.Dropped;
					}

					hwndCritFound = HotkeyDefinition.NormalizeCriterionFoundHwnd(definition.hotCriterion, hwndCritFound);

					script.HookThread.kbdMsSender.thisHotkeyModifiersLR = 0;
					A_EndChar = definition.endCharRequired ? endChar.ToString() : "";
					script.SetHotNamesAndTimes(definition.Name);
					_ = Interlocked.Increment(ref definition.existingThreads);

					try
					{
						var btv = thread.ThreadVariables;
						btv.configData.sendLevel = definition.inputLevel;
						btv.hwndLastUsed = new nint(hwndCritFound);
						btv.hotCriterion = definition.hotCriterion;// v2: Let the Hotkey command use the criterion of this hotstring by default.

						if (string.IsNullOrEmpty(definition.replacement))
						{
							// Action (callback) hotstring: the backspacing in DoReplace is cosmetic and can fail
							// independently of the user's callback (e.g. input injection is unavailable or denied,
							// such as on a headless host). A failed send must not suppress the callback, which is
							// the actual hotstring action.
							try
							{
								definition.DoReplace(script, caseMode, endChar, triggerVk, skipChars);
							}
							catch (Exception ex)
							{
								_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
							}

							_ = definition.funcObj.Call([definition.Name]);
						}
						else
						{
							// Auto-replace hotstring: the send IS the action, so let failures propagate to the
							// outer handler.
							definition.DoReplace(script, caseMode, endChar, triggerVk, skipChars);
						}

						callbackExecuted = true;
					}
					finally
					{
						_ = Interlocked.Decrement(ref definition.existingThreads);
					}
				}
				catch (Exception ex)
				{
					_ = Keysharp.Internals.Flow.HandleCaughtException(ex);
				}

				return callbackExecuted
					? ScriptEventExecutionResult.Executed
					: ScriptEventExecutionResult.Dropped;
			}
		}

		[Flags]
		public enum Options
		{ None = 0, AutoTrigger = 1, Nested = 2, Backspace = 4, CaseSensitive = 8, OmitEnding = 16, Raw = 32, Reset = 64 }
	}

	internal enum CaseConformModes
	{ None, AllCaps, FirstCap };
}
