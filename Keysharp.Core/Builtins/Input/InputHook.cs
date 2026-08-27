using Keysharp.Runtime;

namespace Keysharp.Builtins
{
	/// <summary>
	/// Collects or intercepts keyboard input. Scripts construct one by calling the class, as AutoHotkey does:
	/// <c>ih := InputHook("V")</c>.
	/// </summary>
	public class InputHook : KeysharpObject
	{
		private const int CharCallbackIndex = 0;
		private const int EndCallbackIndex = 1;
		private const int KeyDownCallbackIndex = 2;
		private const int KeyUpCallbackIndex = 3;
		private const int MouseDownCallbackIndex = 4;
		private const int MouseUpCallbackIndex = 5;
		private const int MouseMoveCallbackIndex = 6;
		internal InputType input;
		private bool callbackPersistenceActive;
		private readonly CallbackRegistration[] callbackSlots = [new(), new(), new(), new(), new(), new(), new()];

		public object BackspaceIsUndo
		{
			get => input.backspaceIsUndo;
			set => input.backspaceIsUndo = value.Ab();
		}

		public object BeforeHotkeys
		{
			get => input.beforeHotkeys;
			set => input.beforeHotkeys = value.Ab();
		}

		public object BufferLengthMax
		{
			get => input.bufferLengthMax;
			set => input.bufferLengthMax = value.Ai();
		}

		public object CaseSensitive
		{
			get => input.caseSensitive;
			set => input.caseSensitive = value.Ab();
		}

		public object EndCharMode
		{
			get => input.endCharMode;
			set => input.endCharMode = value.Ab();
		}

		public string EndKey
		{
			get
			{
				if (input.status == InputStatusType.TerminatedByEndKey)
				{
					var str = "";
					_ = input.GetEndReason(ref str);
					return str;
				}

				return DefaultObject;
			}
		}

		public string EndMods
		{
			get
			{
				var sb = new StringBuilder(8);

				for (var i = 0; i < 8; ++i)
					if ((input.endingMods & (1 << i)) != 0)
					{
						_ = sb.Append(KeyboardMouseSender.ModLRString[i * 2]);
						_ = sb.Append(KeyboardMouseSender.ModLRString[(i * 2) + 1]);
					}

				return sb.ToString();
			}
		}

		public string EndReason
		{
			get
			{
				string str = null;
				return input.GetEndReason(ref str);
			}
		}

		public object FindAnywhere
		{
			get => input.findAnywhere;
			set => input.findAnywhere = value.Ab();
		}

		public bool InProgress => input.InProgress();

		public string Input => input.buffer;

		public string Match => input.status == InputStatusType.TerminatedByMatch && input.endingMatchIndex < input.match.Count
		? input.match[input.endingMatchIndex]
		: "";

		public object MinSendLevel
		{
			get => (long)input.minSendLevel;

			set
			{
				var val = value.Al();

				if (val < 0 || val > 101)
				{
					_ = Errors.ValueErrorOccurred($"Cannot set InputHook.MinSendLevel to a value outside of the range 0 - 101 ({value}).");
					return;
				}

				input.minSendLevel = (uint)val;
			}
		}

		public object NotifyNonText
		{
			get => input.notifyNonText;
			set => input.notifyNonText = value.Ab();
		}

		public object OnChar
		{
			get => GetCallback(CharCallbackIndex);
			set => SetKeyboardCallback(CharCallbackIndex, value);
		}

		public object OnEnd
		{
			get => GetCallback(EndCallbackIndex);
			set => SetCallback(EndCallbackIndex, value);
		}

		public object OnKeyDown
		{
			get => GetCallback(KeyDownCallbackIndex);
			set => SetKeyboardCallback(KeyDownCallbackIndex, value);
		}

		public object OnKeyUp
		{
			get => GetCallback(KeyUpCallbackIndex);
			set => SetKeyboardCallback(KeyUpCallbackIndex, value);
		}

		public object OnMouseDown
		{
			get => GetCallback(MouseDownCallbackIndex);
			set => SetMouseCallback(MouseDownCallbackIndex, value);
		}

		public object OnMouseMove
		{
			get => GetCallback(MouseMoveCallbackIndex);
			set => SetMouseCallback(MouseMoveCallbackIndex, value);
		}

		public object OnMouseUp
		{
			get => GetCallback(MouseUpCallbackIndex);
			set => SetMouseCallback(MouseUpCallbackIndex, value);
		}

		public object Timeout
		{
			get => input.timeout / 1000.0;

			set
			{
				input.timeout = (int)(value.ParseDouble() * 1000);

				if (input.InProgress() && input.timeout > 0)
					input.SetTimeoutTimer();
			}
		}

		public object TranscribeModifiedKeys
		{
			get => input.transcribeModifiedKeys;
			set => input.transcribeModifiedKeys = value.Ab();
		}

		public object VisibleMouseMove
		{
			get => input.visibleMouseMove;
			set
			{
				input.visibleMouseMove = value.Ab();

				// Suppressing movement requires the mouse hook; install it if collection is already running.
				if (input.InProgress())
				{
					if (input.MouseIsNeeded)
						HotkeyDefinition.InstallMouseHook(Script.TheScript);

					Script.TheScript.HookThread.RefreshPlatformKeyGrabs();
				}
			}
		}

		public object VisibleNonText
		{
			get => input.visibleNonText;
			set
			{
				input.visibleNonText = value.Ab();

				if (input.InProgress())
				{
					// Turning suppression on requires the keyboard hook if it wasn't already installed.
					if (input.KeyboardIsNeeded)
						HotkeyDefinition.InstallKeybdHook(Script.TheScript);

					Script.TheScript.HookThread.RefreshPlatformKeyGrabs();
				}
			}
		}

		public object VisibleText
		{
			get => input.visibleText;
			set
			{
				input.visibleText = value.Ab();

				if (input.InProgress())
				{
					if (input.KeyboardIsNeeded)
						HotkeyDefinition.InstallKeybdHook(Script.TheScript);

					Script.TheScript.HookThread.RefreshPlatformKeyGrabs();
				}
			}
		}

		public InputHook(params object[] args) : base(args) { }

		/// <summary>
		/// Creates an object which can be used to collect or intercept keyboard input.
		/// </summary>
		/// <param name="args">
		/// Up to three values, each optional:
		/// <list type="number">
		/// <item>options: a string of zero or more of the following (in any order, with optional spaces between):
		/// B: Sets BackspaceIsUndo to 0 (false), which causes Backspace to be ignored.
		/// C: Sets CaseSensitive to 1 (true), making MatchList case-sensitive.
		/// I: Sets MinSendLevel to 1 or a given value, causing any input with send level below this value to be ignored.
		/// For example, I2 would ignore any input with a level of 0 (the default) or 1, but would capture input at level 2.
		/// L: Length limit (e.g. L5). The maximum allowed length of the input.
		/// When the text reaches this length, the Input is terminated and EndReason is set to the word Max (unless the text matches one of the MatchList phrases, in which case EndReason is set to the word Match).
		/// If unspecified, the length limit is 1023.
		/// M: Allows a greater range of modified keypresses to produce text.
		/// Normally, a key is treated as non-text if it is modified by any combination other than Shift, Ctrl+Alt (i.e. AltGr) or Ctrl+Alt+Shift (i.e. AltGr+Shift).
		/// This option causes translation to be attempted for other combinations of modifiers.
		/// T: Sets Timeout (e.g. T3 or T2.5).
		/// V: Sets VisibleText and VisibleNonText to 1 (true).
		/// Normally, the user's input is blocked (hidden from the system).
		/// Use this option to have the user's keystrokes sent to the active window.
		/// *: Wildcard. Sets FindAnywhere to 1 (true), allowing matches to be found anywhere within what the user types.
		/// E: Handle single-character end keys by character code instead of by keycode.
		/// This provides more consistent results if the active window's keyboard layout is different to the script's keyboard layout.
		/// It also prevents key combinations which don't actually produce the given end characters from ending input; for example, if @ is an end key, on the US layout Shift+2 will trigger it but Ctrl+Shift+2 will not (if the E option is used).
		/// If the C option is also used, the end character is case-sensitive.</item>
		/// <item>endKeys: a list of zero or more keys, any one of which terminates the Input when pressed (the end key itself is not written to the Input buffer). When an Input is terminated this way, EndReason is set to the word EndKey and EndKey is set to the name of the key.</item>
		/// <item>matchList: a comma-separated list of key phrases, any of which will cause the Input to be terminated (in which case EndReason will be set to the word Match).</item>
		/// </list>
		/// </param>
		/// <returns>An empty value; the constructed object is the instance being initialized.</returns>
		public object __New(object Options = null, object EndKeys = null, object MatchList = null)
		{
			input = new InputType(this, Options.As(), EndKeys.As(), MatchList.As());
			return DefaultObject;
		}

		public object KeyOpt(object keys, object keyOptions)
		{
			var keysVal = keys.As();
			var options = keyOptions.As();
			var adding = true;
			uint flag = 0U, addFlags = 0u, removeFlags = 0u;

			for (var i = 0; i < options.Length; ++i)
			{
				switch (char.ToUpper(options[i]))
				{
					case '+': adding = true; continue;

					case '-': adding = false; continue;

					case ' ': case '\t': continue;

					case 'E': flag = HookThread.END_KEY_ENABLED; break;

					case 'I': flag = HookThread.INPUT_KEY_IGNORE_TEXT; break;

					case 'N': flag = HookThread.INPUT_KEY_NOTIFY; break;

					case 'S':
						flag = HookThread.INPUT_KEY_SUPPRESS;

						if (adding)
							removeFlags |= HookThread.INPUT_KEY_VISIBLE;

						break;

					case 'V':
						flag = HookThread.INPUT_KEY_VISIBLE;

						if (adding)
							removeFlags |= HookThread.INPUT_KEY_SUPPRESS;

						break;

					case 'Z': // Zero (reset)
						addFlags = 0;
						removeFlags = HookThread.INPUT_KEY_OPTION_MASK;
						continue;

					default:
						return Errors.ValueErrorOccurred($"Invalid option.", options);
				}

				if (adding)
					addFlags |= flag; // Add takes precedence over remove, so remove_flags isn't changed.
				else
				{
					removeFlags |= flag;
					addFlags &= ~flag; // Override any previous add.
				}
			}

			if (string.Compare(keysVal, "{All}", true) == 0)
			{
				// Could optimize by using memset() when remove_flags == 0xFF, but that doesn't seem
				// worthwhile since this mode is already faster than SetKeyFlags() with a single key.
				for (var i = 0; i < input.keyVK.Length; ++i)
					input.keyVK[i] = (input.keyVK[i] & ~removeFlags) | addFlags;

				for (var i = 0; i < input.keySC.Length; ++i)
					input.keySC[i] = (input.keySC[i] & ~removeFlags) | addFlags;

				// AHK returns here; falling through to SetKeyFlags would re-parse the literal "{All}".
			}
			else
				input.SetKeyFlags(keysVal, false, removeFlags, addFlags);

			if (input.InProgress())
			{
				Script.TheScript.HookThread.RefreshPlatformKeyGrabs();

				// Flagging a key after Start() may newly require a hook: a keyboard key/end key needs the
				// keyboard hook, a mouse/wheel button (end key or +S) needs the mouse hook.
				if (input.KeyboardIsNeeded)
					HotkeyDefinition.InstallKeybdHook(Script.TheScript);

				if (input.MouseIsNeeded)
					HotkeyDefinition.InstallMouseHook(Script.TheScript);
			}

			return DefaultObject;
		}

		public object Start()
		{
			if (!input.InProgress())
			{
				input.buffer = "";
				input.InputStart();
			}
			return DefaultObject;
		}

		public object Stop()
		{
			if (input.InProgress())
				input.Stop();
			return DefaultObject;
		}

		public object Wait(object maxTime)
		{
			var ms = maxTime.Ad(double.MaxValue) * 1000.0;
			var tickStart = DateTime.UtcNow;

			while (input.InProgress() && (DateTime.UtcNow - tickStart).TotalMilliseconds < ms)
				_ = Flow.Sleep(20);

			// AHK's InputHook.Wait returns the EndReason (the documented return value).
			string str = null;
			return input.GetEndReason(ref str);
		}

		internal void ActivateCallbackPersistence() => SetCallbackPersistenceActive(true);

		internal void DeactivateCallbackPersistence() => SetCallbackPersistenceActive(false);

		internal CallbackRegistration GetCallbackSlot(UserMessages message)
		{
			var index = message switch
			{
				UserMessages.AHK_INPUT_CHAR => CharCallbackIndex,
				UserMessages.AHK_INPUT_END => EndCallbackIndex,
				UserMessages.AHK_INPUT_KEYDOWN => KeyDownCallbackIndex,
				UserMessages.AHK_INPUT_KEYUP => KeyUpCallbackIndex,
				UserMessages.AHK_INPUT_MOUSEDOWN => MouseDownCallbackIndex,
				UserMessages.AHK_INPUT_MOUSEUP => MouseUpCallbackIndex,
				UserMessages.AHK_INPUT_MOUSEMOVE => MouseMoveCallbackIndex,
				_ => -1
			};

			return index >= 0 ? callbackSlots[index] : null;
		}

		private void SetCallbackPersistence(bool persistenceActive)
		{
			foreach (var callbackSlot in callbackSlots)
				callbackSlot.SetActive(persistenceActive && callbackSlot.Callback != null);
		}

		private object GetCallback(int index) => callbackSlots[index].Callback ?? (object)DefaultObject;

		private void SetCallback(int index, object value)
		{
			var callback = Functions.GetKeysharpFunc(value, null, true);
			callbackSlots[index].Set(callback, callback != null ? Script.TheScript?.EventScheduler : null, callbackPersistenceActive && callback != null);
		}

		// Same as SetCallback, but ensures the low-level mouse hook is running if the callback is
		// assigned while collection is already in progress (Start() installs it up front otherwise).
		private void SetMouseCallback(int index, object value)
		{
			SetCallback(index, value);

			if (input.InProgress() && input.MouseIsNeeded)
				HotkeyDefinition.InstallMouseHook(Script.TheScript);
		}

		// As SetCallback, but ensures the keyboard hook is running if an OnChar/OnKeyDown/OnKeyUp callback
		// is assigned after Start() (the keyboard hook is otherwise only conditionally installed at Start).
		private void SetKeyboardCallback(int index, object value)
		{
			SetCallback(index, value);

			if (input.InProgress() && input.KeyboardIsNeeded)
				HotkeyDefinition.InstallKeybdHook(Script.TheScript);
		}

		private void SetCallbackPersistenceActive(bool active)
		{
			if (callbackPersistenceActive == active)
				return;

			callbackPersistenceActive = active;
			SetCallbackPersistence(active);
		}
	}
}
