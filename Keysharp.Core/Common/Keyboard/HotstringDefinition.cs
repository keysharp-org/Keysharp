namespace Keysharp.Core.Common.Keyboard
{
	[PublicHiddenFromUser]
	public class HotstringDefinition
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
		internal IFuncObj funcObj;
		internal IFuncObj hotCriterion;
		internal long inputLevel;
		internal long priority, keyDelay;
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

		public HotstringDefinition(string sequence, string _replacement)
		{
			Sequence = sequence;
			replacement = _replacement;
			//EndChars = defEndChars;
		}

		internal HotstringDefinition(string _name, IFuncObj _funcObj, ReadOnlySpan<char> _options, string _hotstring, string _replacement
									 , bool _hasContinuationSection, int _suspend)

		{
			var script = Script.TheScript;
			var hm = script.HotstringManager;
			funcObj = _funcObj;
			hotCriterion = script.Threads.CurrentThread.hotCriterion;
			suspended = _suspend;
			maxThreads = A_MaxThreadsPerHotkey.Aui();  // The value of g_MaxThreadsPerHotkey can vary during load-time.
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
			inputLevel = (long)A_InputLevel;
			suspendExempt = A_SuspendExempt.Ab();
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

		internal bool CompareHotstring(ReadOnlySpan<char> _hotstring, bool _caseSensitive, bool _detectWhenInsideWord, IFuncObj _hotCriterion)
		{
			// hs.mEndCharRequired is not checked because although it affects the conditions for activating
			// the hotstring, ::abbrev:: and :*:abbrev:: cannot co-exist (the latter would always take over).
			return hotCriterion == _hotCriterion // Same #HotIf criterion.
				   && caseSensitive == _caseSensitive // ::BTW:: and :C:BTW:: can co-exist.
				   && detectWhenInsideWord == _detectWhenInsideWord // :?:ion:: and ::ion:: can co-exist.
				   && (_caseSensitive ? _hotstring.SequenceEqual(str.AsSpan()) : str.AsSpan().Equals(_hotstring, StringComparison.OrdinalIgnoreCase));// :C:BTW:: and :C:btw:: can co-exist, but not ::BTW:: and ::btw::.
		}

		internal void DoReplace(CaseConformModes caseMode, char endChar)
		{
			var sb = new StringBuilder();//This might be able to be done more efficiently, but use sb unless performance issues show up.
			var startOfReplacement = 0;
			string sendBuf;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;

			if (doBackspace)
			{
				var backspaceCount = str.Length;

				for (var i = 0; i < str.Length; i++)
					if (char.IsSurrogatePair(str, i))
					{
						i++;
						backspaceCount--;
					}

#if WINDOWS
				// Subtract 1 from backspaces because the final key pressed by the user to make a
				// match was already suppressed by the hook (it wasn't sent through to the active
				// window).  So what we do is backspace over all the other keys prior to that one,
				// put in the replacement text (if applicable), then send the EndChar through
				// (if applicable) to complete the sequence.

				if (!endCharRequired)
					--backspaceCount;
#endif

				for (var i = 0; i < backspaceCount; ++i)
				{
					_ = sb.Append('\b');  // Use raw backspaces, not {BS n}, in case the send will be raw.
					startOfReplacement++;
				}
			}

			if (!string.IsNullOrEmpty(replacement))
			{
				_ = sb.Append(replacement);

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

			ht.PrepareToSendHotstringReplacement(endChar);

			kbdMouseSender.SendKeys(sendBuf, sendRaw, sendMode, 0); // Send the backspaces and/or replacement.
			// Restore original values.
			tv.keyDelay = oldDelay;
			tv.keyDuration = oldPressDuration;
			tv.keyDelayPlay = oldDelayPlay;
			tv.keyDurationPlay = oldPressDurationPlay;
			tv.sendLevel = oldSendLevel;
		}

		internal bool HotIfRequiresEval() => hotCriterion is IFuncObj;

		internal void ParseOptions(ReadOnlySpan<char> aOptions)
		{
			var unused_X_option = false;
			ParseOptions(aOptions, ref priority, ref keyDelay, ref sendMode, ref caseSensitive, ref conformToCase, ref doBackspace
						 , ref omitEndChar, ref sendRaw, ref endCharRequired, ref detectWhenInsideWord, ref doReset, ref unused_X_option, ref suspendExempt);
		}

		/// <summary>
		/// Returns OK or FAIL.  Caller has already ensured that the backspacing (if specified by mDoBackspace)
		/// has been done.  Caller must have already created a new thread for us, and must close the thread when
		/// we return.
		/// </summary>
		internal ResultType PerformInNewThreadMadeByCaller(nint hwndCritFound, string endChar)
		{
			var script = Script.TheScript;

			if (!script.Threads.AnyThreadsAvailable())//First test global thread count.
				return ResultType.Fail;

			if (!AnyThreadsAvailable())//Then test local thread count.
				return ResultType.Fail;

			var tv = script.Threads.CurrentThread;

			if (priority < tv.priority)//Finally, test priority.
				return ResultType.Fail;

			VariadicFunction vf = (o) =>
			{
				object ret = null;
				var ok = Flow.TryCatch(() =>
				{
					ret = null;
					var tv = script.Threads.CurrentThread;
					tv.configData.sendLevel = inputLevel;
					tv.hwndLastUsed = hwndCritFound;
					tv.hotCriterion = hotCriterion;// v2: Let the Hotkey command use the criterion of this hotstring by default.
					ret = funcObj.Call(o);
				}, false, (false, null));
				_ = Interlocked.Decrement(ref existingThreads);
				return ret;
			};

			try
			{
				//var tsk = await Threads.LaunchInThread(priority, false, false, vf, new object[] { Name });
				// See Hotkey::Perform() for details about this.  For hot strings -- which also use the
				// g_script.mThisHotkeyStartTime value to determine whether g_script.mThisHotkeyModifiersLR
				// is still timely/accurate -- it seems best to set to "no modifiers":
				script.HookThread.kbdMsSender.thisHotkeyModifiersLR = 0;
				A_EndChar = endCharRequired ? endChar : ""; // v1.0.48.04: Explicitly set 0 when hs->mEndCharRequired==false because LOWORD is used for something else in that case.
				script.SetHotNamesAndTimes(Name);
				_ = Interlocked.Increment(ref existingThreads);//This is the thread count for this particular hotstring only.
				script.Threads.LaunchInThread(priority, false, false, vf, [Name], false);
			}
			catch (Error ex)
			{
				_ = Dialogs.MsgBox($"Exception thrown during hotstring handler.\n\n{ex}", null, "iconx");
			}

			return ResultType.Ok;
		}

		[Flags]
		public enum Options
		{ None = 0, AutoTrigger = 1, Nested = 2, Backspace = 4, CaseSensitive = 8, OmitEnding = 16, Raw = 32, Reset = 64 }
	}

	internal enum CaseConformModes
	{ None, AllCaps, FirstCap };
}
