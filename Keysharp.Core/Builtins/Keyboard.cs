namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for keyboard-related functions.
	/// </summary>
	public static class Keyboard
	{
		/// <summary>
		/// Disables or enables the user's ability to interact with the computer via keyboard and mouse.
		/// </summary>
		/// <param name="onOff">Different operations will be taken depending on the specified value:<br/>
		/// OnOff: This mode blocks all user inputs unconditionally. Specify one of the following values:<br/>
		///     On or 1 (true): The user is prevented from interacting with the computer(mouse and keyboard input has no effect).<br/>
		///     Off or 0 (false): Input is re-enabled.<br/>
		/// <br/>
		/// SendMouse: This mode only blocks user inputs while specific send and/or mouse functions are in progress. Specify one of the following words:
		///     Send: The user's keyboard and mouse input is ignored while a SendEvent is in progress (including Send and SendText if SendMode "Event" has been used). This prevents the user's keystrokes from disrupting the flow of simulated keystrokes. When the Send finishes, input is re-enabled (unless still blocked by a previous use of BlockInput "On").
		///     Mouse: The user's keyboard and mouse input is ignored while a Click, MouseMove, MouseClick, or MouseClickDrag is in progress (the traditional SendEvent mode only). This prevents the user's mouse movements and clicks from disrupting the simulated mouse events.When the mouse action finishes, input is re-enabled(unless still blocked by a previous use of BlockInput "On").
		///     SendAndMouse: A combination of the above two modes.
		///     Default: Turns off both the Send and the Mouse modes, but does not change the current state of input blocking.For example, if BlockInput "On" is currently in effect, using BlockInput "Default" will not turn it off.
		/// <br/>
		/// MouseMove: This mode only blocks the mouse cursor movement. Specify one of the following words:<br/>
		///     MouseMove: The mouse cursor will not move in response to the user's physical movement of the mouse (DirectInput applications are a possible exception).<br/>
		///         When a script first uses this function, the mouse hook is installed (if it is not already).<br/>
		///         The mouse hook will stay installed until the next use of the Suspend or Hotkey function, at which time it is removed if not required by any hotkeys or hotstrings (see #Hotstring NoMouse).<br/>
		///     MouseMoveOff: Allows the user to move the mouse cursor.<br/>
		/// </param>
		public static object BlockInput(object onOff)
		{
			var mode = onOff.As();
			var toggle = ConvertBlockInput(mode);
			_ = ScriptBlockInput(toggle);
			return DefaultObject;
		}

		/// <summary>
		/// Retrieves the current position of the caret (text insertion point).
		/// </summary>
		/// <param name="outputVarX">If omitted, the corresponding value will not be stored.<br/>
		/// Otherwise, specify references to the output variables in which to store the X and Y coordinates.<br/>
		/// The retrieved coordinates are relative to the active window's client area unless overridden by<br/>
		/// using <see cref="CoordMode"/> or <see cref="A_CoordModeCaret"/>.
		/// </param>
		/// <param name="outputVarY">See <paramref name="outputVarX"/>.</param>
		/// <returns>If there is no active window or the caret position cannot be determined, returns 0 (false)<br/>
		/// and the output variables are made blank. It returns 1 (true) if the system returned a caret position,<br/>
		/// but this does not necessarily mean a caret is visible.
		/// </returns>
		public static bool CaretGetPos([ByRef] object outputVarX = null,
									   [ByRef] object outputVarY = null)
		{
			outputVarX ??= VarRef.Empty; outputVarY ??= VarRef.Empty;
#if WINDOWS
            // I believe only the foreground window can have a caret position due to relationship with focused control.
            var targetWindow = WindowsAPI.GetForegroundWindow(); // Variable must be named targetwindow for ATTACH_THREAD_INPUT.

			if (targetWindow == 0) // No window is in the foreground, report blank coordinate.
			{
				if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", "");
				if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", "");
				return false;
			}

			var h = WindowsAPI.GetWindowThreadProcessId(targetWindow, out var _);
			var info = GUITHREADINFO.Default;//Must be initialized this way because the size field must be populated.
			var result = WindowsAPI.GetGUIThreadInfo(h, ref info) && info.hwndCaret != 0;

			if (!result)
			{
				if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", "");
				if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", "");
                return false;
			}

			var pt = new POINT
			{
				X = info.rcCaret.Left,
				Y = info.rcCaret.Top
			};
			var script = Script.TheScript;
			var caretWnd = WindowQuery.CreateWindow(info.hwndCaret);
			caretWnd.ClientToScreen(ref pt);// Unconditionally convert to screen coordinates, for simplicity.
			int x = 0, y = 0;
			CoordToScreen(ref x, ref y, CoordMode.Caret);// Now convert back to whatever is expected for the current mode.
			pt.X -= x;
			pt.Y -= y;
			if (outputVarX != null) Script.SetPropertyValue(outputVarX, "__Value", (long)pt.X);
			if (outputVarY != null) Script.SetPropertyValue(outputVarY, "__Value", (long)pt.Y);
			return true;
#else
#if LINUX
			// AT-SPI first (it covers every application, including this one when accessibility is on), then the
			// script's own GTK controls, which need the UI thread and are invisible to AT-SPI when it is off.
			var caret = (Found: LinuxAccessibility.TryGetCaretScreenPosition(out var x, out var y), X: x, Y: y);

			if (!caret.Found)
				caret = Script.TheScript.InvokeOnUIThread(static () =>
					(Found: LinuxAccessibility.TryGetOwnedCaretScreenPosition(out var ownedX, out var ownedY),
					 X: ownedX, Y: ownedY));

#elif OSX
			var caret = (Found: MacAccessibility.TryGetCaretScreenPosition(out var x, out var y), X: x, Y: y);
#endif

			if (!caret.Found)
			{
				Script.SetPropertyValue(outputVarX, "__Value", "");
				Script.SetPropertyValue(outputVarY, "__Value", "");
				return false;
			}

			//Both sources report screen coordinates; convert back to whatever the current mode expects.
			var originX = 0;
			var originY = 0;
			CoordToScreen(ref originX, ref originY, CoordMode.Caret);
			Script.SetPropertyValue(outputVarX, "__Value", (long)(caret.X - originX));
			Script.SetPropertyValue(outputVarY, "__Value", (long)(caret.Y - originY));
			return true;
#endif
		}

		/// <summary>
		/// Retrieves the name/text of a key.
		/// </summary>
		/// <param name="keyName">This can be just about any single character from the keyboard or one<br/>
		/// of the key names from the key list. Examples: B, 5, LWin, RControl, Alt, Enter, Escape.<br/>
		/// Alternatively, this can be an explicit virtual key code such as vkFF, an explicit scan code<br/>
		/// such as sc01D, or a combination of VK and SC (in that order) such as vk1Bsc001.<br/>
		/// Note that these codes must be in hexadecimal.
		/// </param>
		/// <returns>The name of the specified key, or blank if the key is invalid or unnamed.</returns>
		public static string GetKeyName(object keyName) => GetKeyNamePrivate(keyName.As(), 0) as string;

		/// <summary>
		/// Retrieves the scan code of a key.
		/// </summary>
		/// <param name="keyName">Any single character or one of the key names from the key list.<br/>
		/// Examples: B, 5, LWin, RControl, Alt, Enter, Escape.<br/>
		/// Alternatively, this can be an explicit virtual key code such as vkFF, an explicit scan code such as sc01D,<br/>
		/// or a combination of VK and SC (in that order) such as vk1Bsc001.Note that these codes must be in hexadecimal.
		/// </param>
		/// <returns>Returns the scan code of the specified key, or 0 if the key is invalid or has no scan code.</returns>
		public static long GetKeySC(object keyName) => Convert.ToInt64(GetKeyNamePrivate(keyName.As(), 1));

		/// <summary>
		/// Returns 1 (true) or 0 (false) depending on whether the specified keyboard key or mouse/controller<br/>
		/// button is down or up. Also retrieves controller status.
		/// </summary>
		/// <param name="keyName">This can be just about any single character from the keyboard or one of
		/// the key names from the key list, such as a mouse/controller button.<br/>
		/// Examples: B, 5, LWin, RControl, Alt, Enter, Escape, LButton, MButton, Joy1.<br/>
		/// Alternatively, an explicit virtual key code such as vkFF may be specified.<br/>
		/// This is useful in the rare case where a key has no name. The code of such a key can be<br/>
		/// determined by following the steps at the bottom of the key list page.<br/>
		/// Note that this code must be in hexadecimal.<br/>
		/// Known limitation: This function cannot differentiate between two keys which share the same<br/>
		/// virtual key code, such as Left and NumpadLeft.
		/// </param>
		/// <param name="mode"></param>
		public static object GetKeyState(object keyName, object mode = null)
		{
			var keyname = keyName.As();
			var modeVal = mode.As();
			var script = Script.TheScript;
			var ht = script.HookThread;
			JoyControls joy;
			uint? joystickid = 0u;
			uint? dummy = null;
			KeyStateTypes keystatetype;

			if (string.Compare(modeVal, "T", true) == 0)
				keystatetype = KeyStateTypes.Toggle;//Whether a toggleable key such as CapsLock is currently turned on.
			else if (string.Compare(modeVal, "P", true) == 0)
				keystatetype = KeyStateTypes.Physical;//Physical state of key.
			else
				keystatetype = KeyStateTypes.Logical;

			var vk = ht.TextToVK(keyname, ref dummy, layout: null); // Returns 0 if keyname is not a valid key name or virtual key code.

			if (vk == 0)
			{
				if ((joy = Joystick.ConvertJoy(keyname, ref joystickid)) == 0)
					return Errors.ValueErrorOccurred("Invalid value.");//It is neither a key name nor a joystick button/axis.

				return Joystick.ScriptGetJoyState(joy, joystickid.Value);
			}

			// Since above didn't return: There is a virtual key (not a joystick control).
			return ScriptGetKeyState(vk, keystatetype); // 1 for down and 0 for up.
		}

		/// <summary>
		/// Retrieves the virtual key code of a key.
		/// </summary>
		/// <param name="keyName">Any single character or one of the key names from the key list.<br/>
		/// Examples: B, 5, LWin, RControl, Alt, Enter, Escape.<br/>
		/// Alternatively, this can be an explicit virtual key code such as vkFF, an explicit scan code<br/>
		/// such as sc01D, or a combination of VK and SC (in that order) such as vk1Bsc001.<br/>
		/// Note that these codes must be in hexadecimal.
		/// </param>
		/// <returns>The virtual key code of the specified key, or 0 if the key is invalid or has no virtual key code.</returns>
		public static long GetKeyVK(object keyName) => Convert.ToInt64(GetKeyNamePrivate(keyName.As(), 2));

		/// <summary>
		/// Creates, modifies, enables, or disables a hotkey while the script is running.
		/// </summary>
		/// <param name="keyName">Name of the hotkey's activation key, including any modifier symbols. For example, specify #c for the Win+C hotkey.</param>
		/// <param name="action">If omitted and KeyName already exists as a hotkey, its action will not be changed.<br/>
		/// This is useful to change only the hotkey's Options. Otherwise, specify a callback, a hotkey name without<br/>
		/// trailing colons, or one of the special values listed below.
		///     On: The hotkey becomes enabled. No action is taken if the hotkey is already On.
		///     Off: The hotkey becomes disabled.No action is taken if the hotkey is already Off.
		///     Toggle: The hotkey is set to the opposite state (enabled or disabled).
		///     AltTab(and others) : These are special Alt-Tab hotkey actions that are described here.
		/// </param>
		/// <param name="options">A string of zero or more of the following options with optional spaces in between. For example: "On B0".
		///     On: Enables the hotkey if it is currently disabled.
		///     Off: Disables the hotkey if it is currently enabled. This is typically used to create a hotkey in an initially-disabled state.
		///     B or B0: Specify the letter B to buffer the hotkey as described in #MaxThreadsBuffer. Specify B0 (B with the number 0) to disable this type of buffering.
		///     Pn: Specify the letter P followed by the hotkey's thread priority. If the P option is omitted when creating a hotkey, 0 will be used.
		///     S or S0: Specify the letter S to make the hotkey exempt from Suspend, which allows the hotkey to be used to turn Suspend off. Specify S0 (S with the number 0) to remove the exemption, allowing the hotkey to be suspended.
		///     Tn: Specify the letter T followed by a the number of threads to allow for this hotkey as described in #MaxThreadsPerHotkey. For example: T5.
		///     In (InputLevel): Specify the letter I (or i) followed by the hotkey's input level. For example: I1.
		/// </param>
		/// <exception cref="Error">Throws an <see cref="Error"/> exception if an invalid function object or name is specified.</exception>
		public static object Hotkey(object keyName, object action = null, object options = null)
		{
			var script = Script.TheScript;
			var keyname = keyName.As();
			var label = action.As();
			var opt = options.As();
			KeysharpFunc fo = null;
			var hook_action = 0u;

			if (action != null)
			{
				fo = Functions.GetKeysharpFunc(action, null);//Don't throw on failure because returning null is a valid action.
				var tv = script.Threads.CurrentThread;

				//Hotkey callbacks are invoked with one argument (the hotkey name). A function requiring more can
				//never run, so reject it at registration: the invocation path swallows callback exceptions, which
				//used to turn this into a hotkey that silently did nothing. Declaring FEWER stays legal -- the
				//name may be ignored, as in AutoHotkey.
				if (fo != null && !fo.CanAcceptArgCount(1))
					return Errors.ValueErrorOccurred(
						$"Hotkey callback requires at least {fo.MinParams} parameter(s), but hotkey callbacks are called with one (the hotkey name).",
						fo.Mph.QualifiedName);   //Not fo.Name: a bound function's Name is empty, so it would name nothing

				if (fo == null && !string.IsNullOrEmpty(label) && ((hook_action = HotkeyDefinition.ConvertAltTab(label, true)) == 0))
				{
					var shk = script.HotkeyData.shk;

					for (var i = 0; i < shk.Length; ++i)
					{
						if (shk[i].Name == label)
						{
							for (var v = shk[i].firstVariant; v != null; v = v.nextVariant)
							{
								if (v.hotCriterion == tv.hotCriterion)
								{
									fo = v.FindBinding(script.EventScheduler)?.OriginalCallback;
									goto break_twice;
								}
							}
						}
					}

break_twice:;

					if (fo == null)
						return Errors.ErrorOccurred($"Unable to find existing hotkey handler: {label}");
				}

				if (fo == null)
					hook_action = HotkeyDefinition.ConvertAltTab(label, true);
			}

			_ = HotkeyDefinition.Dynamic(script, keyname, opt, fo, hook_action);
			return DefaultObject;
		}

		/// <summary>
		/// Creates, modifies, enables, or disables a hotstring while the script is running.
		/// </summary>
		/// <param name="string">This can be a hotstring trigger string, or new options or a sub function.
		/// Hotstring: The hotstring's trigger string, preceded by the usual colons and option characters. For example, "::btw" or ":*:]d".
		/// NewOptions: To set new default options for subsequently created hotstrings, pass the options to the<br/>
		///     Hotstring function without any leading or trailing colon. For example: Hotstring "T".<br/>
		/// SubFunction:
		///     EndChars: Retrieves or modifies the set of characters used as ending characters by the hotstring recognizer.<br/>
		///     MouseReset: Retrieves or modifies the global setting which controls whether mouse clicks reset the hotstring recognizer.<br/>
		///     Reset: Immediately resets the hotstring recognizer.
		/// </param>
		/// <param name="replacement">If omitted and string already exists as a hotstring, its replacement will<br/>
		/// not be changed. This is useful to change only the hotstring's options, or to turn it on or off.<br/>
		/// Otherwise, specify the replacement string or a callback.
		/// If replacement is a function, it is called(as a new thread) when the hotstring triggers.
		/// The callback accepts one parameter which is the hotstring name that triggered it.
		/// </param>
		/// <param name="onOffToggle">OnOffToggle or the value to pass to the sub function.</param>
		/// <returns>If changing a value, returns the previous value, else returns the HotstringDefinition for the specified hostring.</returns>
		/// <exception cref="ValueError">A <see cref="ValueError"/> exception is thrown if the hotstring name is invalid.</exception>
		/// <exception cref="TargetError">A <see cref="TargetError"/> exception is thrown if the hotstring cannot be found.</exception>
		public static object Hotstring(object @string, object replacement = null, object onOffToggle = null)
		{
			var name = @string.As();
			var replacementVal = replacement;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;
			var xOption = false;
			var action = replacementVal as string;
			var hm = script.HotstringManager;

			if (string.Compare(name, "EndChars", true) == 0) // Equivalent to #Hotstring EndChars <action>
			{
				var old = hm.defEndChars;

				if (!string.IsNullOrEmpty(action))
					hm.defEndChars = action;

				return old;//Return the old value.
			}
			else if (string.Compare(name, "MouseReset", true) == 0) // "MouseReset, true" seems more intuitive than "NoMouse, false"
			{
				var previousValue = hm.hsResetUponMouseClick;

				if (replacementVal != null)
				{
					var val = Options.OnOff(replacementVal);

					if (val != null)
					{
						hm.hsResetUponMouseClick = val.Value;

						if (hm.hsResetUponMouseClick != previousValue && hm.enabledCount != 0) // No need if there aren't any hotstrings.
							_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(script); // Install the hook if needed, or uninstall if no longer needed.
					}
				}

				return previousValue;
			}
			else if (string.Compare(name, "Reset", true) == 0)
			{
				var str = hm.CurrentInputBuffer;
				hm.ClearBuf();
				return str;
			}
			else if (replacementVal == null && onOffToggle == null && name.Length > 0 && name[0] != ':') //Check if only one param was passed. Equivalent to #Hotstring <name>.
			{
				HotstringDefinition.ParseOptions(name, ref hm.hsPriority, ref hm.hsKeyDelay, ref hm.hsSendMode, ref hm.hsCaseSensitive
												 , ref hm.hsConformToCase, ref hm.hsDoBackspace, ref hm.hsOmitEndChar, ref hm.hsSendRaw, ref hm.hsEndCharRequired
												 , ref hm.hsDetectWhenInsideWord, ref hm.hsDoReset, ref xOption, ref hm.hsSuspendExempt);
				return DefaultObject;
			}

			// Parse the hotstring name.
			var hotstringStart = "";
			ReadOnlySpan<char> hotstringOptions = ""; // Set default as "no options were specified for this hotstring".

			if (name.Length > 1 && name[0] == ':')
			{
				if (name[1] != ':')
				{
					hotstringOptions = name.AsSpan(1); // Point it to the hotstring's option letters.
					// The following relies on the fact that options should never contain a literal colon.
					var tempindex = hotstringOptions.IndexOf(':');

					if (tempindex != -1)
						hotstringStart = hotstringOptions.Slice(tempindex + 1).ToString(); // Points to the hotstring itself.
				}
				else // Double-colon, so it's a hotstring if there's more after this (but this means no options are present).
					if (name.Length > 2)
						hotstringStart = name.Substring(2);

				//else it's just a naked "::", which is invalid.
			}

			if (hotstringStart.Length == 0)
				return Errors.ValueErrorOccurred("Hotstring definition did not contain a hotstring.");

			// Determine options which affect hotstring identity/uniqueness.
			var caseSensitive = hm.hsCaseSensitive;
			var detectInsideWord = hm.hsDetectWhenInsideWord;
			var un = false; var lun = 0L; var sm = SendModes.Event; var sr = SendRawModes.NotRaw; // Unused.
			var executeAction = false;

			if (hotstringOptions.Length > 0)
				HotstringDefinition.ParseOptions(hotstringOptions, ref lun, ref lun, ref sm, ref caseSensitive, ref un, ref un, ref un, ref sr, ref un, ref detectInsideWord, ref un, ref executeAction, ref un);

			KeysharpFunc ifunc = null;

			if (replacementVal != null)
			{
				if ((ifunc = Functions.GetKeysharpFunc(replacementVal, null, false)) is not null)
				{
				}
				else if (executeAction)
					return Errors.ValueErrorOccurred("The 'X' option must be used together with a function object.");
			}

			var toggle = ToggleValueType.Neutral;

			if (onOffToggle != null && (toggle = Conversions.ConvertOnOffToggle(onOffToggle)) == ToggleValueType.Invalid)
				return Errors.ValueErrorOccurred($"Invalid value of {onOffToggle} for parameter 3.");

			bool wasAlreadyEnabled;
			var tv = script.Threads.CurrentThread;
			var existing = hm.FindHotstring(hotstringStart, caseSensitive, detectInsideWord, tv.hotCriterion);

			if (existing != null)
			{
				wasAlreadyEnabled = existing.suspended == 0;
				var mutatingAction = ifunc != null || !string.IsNullOrEmpty(action);
				var originalSuspended = existing.suspended;

				// Update the replacement string or function, if specified.
				if (mutatingAction)
				{
					string newReplacement = null; // Set default: not auto-replace.

					if (ifunc == null && replacementVal is string rep) // Caller specified a replacement string ('E' option was handled above).
						newReplacement = rep;

					// Temporarily disable hook access without changing worker persistence mid-mutation.
					existing.suspended |= HotstringDefinition.HS_TEMPORARILY_DISABLED;
					ht.WaitHookIdle();
					// At this point it is certain the hook thread is not in the middle of reading this
					// hotstring's other properties, such as mReplacement.
					existing.replacement = newReplacement;
					existing.funcObj = ifunc;
				}

				// Update the hotstring's options.  Note that mCaseSensitive and mDetectWhenInsideWord
				// can't be changed this way since FindHotstring() would not have found it if they differed.
				// This is done after the above to avoid *partial* updates in the event of a failure.
				existing.ParseOptions(hotstringOptions);

				var newSuspended = existing.suspended;

				switch (toggle)
				{
					case ToggleValueType.Toggle:
						newSuspended ^= HotstringDefinition.HS_TURNED_OFF;
						break;

					case ToggleValueType.On:
						newSuspended &= ~HotstringDefinition.HS_TURNED_OFF;
						break;

					case ToggleValueType.Off:
						newSuspended |= HotstringDefinition.HS_TURNED_OFF;
						break;
				}

				newSuspended &= ~HotstringDefinition.HS_TEMPORARILY_DISABLED;

				if (mutatingAction)
				{
					existing.suspended = newSuspended;
					existing.ownerScheduler = script.EventScheduler;
				}
				else
					existing.SetSuspended(newSuspended);
			}
			else // No matching hotstring yet.
			{
				if (ifunc == null && string.IsNullOrEmpty(action))
					return Errors.TargetErrorOccurred($"Nonexistent hotstring: {name}");

				var initialSuspendState = (toggle == ToggleValueType.Off) ? HotstringDefinition.HS_TURNED_OFF : 0;

				if (A_IsSuspended)
					initialSuspendState |= HotstringDefinition.HS_SUSPENDED;

				var addResult = hm.AddHotstring(name, ifunc, hotstringOptions, hotstringStart, action, false, initialSuspendState);
				if (addResult is not HotstringDefinition)
					return addResult;

				existing = hm.shs[hm.shs.Count - 1];
				wasAlreadyEnabled = false; // Because it didn't exist.
			}

			// Note that suspended must be 0 to count as enabled, meaning the hotstring was neither
			// turned off by us nor suspended by SuspendAll().  If it was suspended, there's no change
			// in status.
			var isenabled = existing.suspended == 0; // Important to avoid direct comparison with mSuspended becauses it isn't pure bool.

			if (isenabled != wasAlreadyEnabled)
			{
				// One of the following just happened:
				//  - a hotstring was created and enabled
				//  - an existing disabled hotstring was just enabled
				//  - an existing enabled hotstring was just disabled
				var previouslyEnabled = hm.enabledCount;
				hm.enabledCount = isenabled ? hm.enabledCount + 1 : hm.enabledCount - 1;

				if ((hm.enabledCount > 0) != (previouslyEnabled > 0)) // Change in status of whether the hotstring recognizer is needed.
				{
					if (isenabled)
						hm.ClearBuf();

					if (!isenabled || ht.kbdHook == 0) // Hook may not be needed anymore || hook is needed but not present.
						_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(script);
				}
			}

			return existing;
		}

		public static object InstallKeybdHook(object install = null, object force = null) =>
		InstallHook(HookType.Keyboard, install, force);

		public static object InstallMouseHook(object install = null, object force = null) =>
		InstallHook(HookType.Mouse, install, force);

		/// <summary>
		/// Displays script info and a history of the most recent keystrokes and mouse clicks.
		/// </summary>
		/// <param name="maxEvents">If omitted, the script's main window will be shown, equivalent to selecting<br/>
		/// the "View->Key history" menu item. Otherwise, specify the maximum number of keyboard and mouse events<br/>
		/// that can be recorded for display in the window (limit 500).<br/>
		/// The key history is also reset, but the main window is not shown or refreshed.
		/// Specify 0 to disable key history entirely.</param>
		public static object KeyHistory(object maxEvents = null)
		{
			var script = Script.TheScript;

			if (maxEvents != null)
			{
				var max = Math.Clamp(maxEvents.Al(), 0, 500);

				if (script.HookThread is HookThread ht)
				{
					if (ht.HasEitherHook())
						ht.SetKeyHistory((int)max);
					else
						ht.keyHistory = max > 0 ? new KeyHistory((int)max) : null;
				}
			}
			else if (script.mainWindow != null)
			{
				script.PostToUIThread(() => script.mainWindow.ShowHistory());
			}

			return DefaultObject;
		}

		/// <summary>
		/// Waits for a key or mouse/controller button to be released or pressed down.
		/// </summary>
		/// <param name="keyName">
		/// This can be just about any single character from the keyboard or one of the key names from the key list,<br/>
		/// such as a mouse/controller button. Controller attributes other than buttons are not supported.<br/>
		/// An explicit virtual key code such as vkFF may also be specified.<br/>
		/// This is useful in the rare case where a key has no name and produces no visible character when pressed.<br/>
		/// Its virtual key code can be determined by following the steps at the bottom of the key list page.
		/// </param>
		/// <param name="options">
		/// If blank or omitted, the function will wait indefinitely for the specified key or mouse/controller<br/>
		/// button to be physically released by the user.<br/>
		/// However, if the keyboard hook is not installed and keyName is a keyboard key released artificially<br/>
		/// by means such as the <see cref="Send"/> function, the key will be seen as having been physically released.<br/>
		/// The same is true for mouse buttons when the mouse hook is not installed.<br/>
		/// Otherwise, specify a string of one or more of the following options(in any order, with optional spaces in between):<br/>
		///     D: Wait for the key to be pushed down.<br/>
		///     L: Check the logical state of the key, which is the state that the OS and the active window believe<br/>
		///         the key to be in (not necessarily the same as the physical state).<br/>
		///         This option is ignored for controller buttons.<br/>
		///     T: Timeout (e.g.T3). The number of seconds to wait before timing out and returning 0.<br/>
		///         If the key or button achieves the specified state, the function will not wait for the timeout to expire.<br/>
		///         Instead, it will immediately return 1.
		/// </param>
		/// <returns>0 (false) if the function timed out or 1 (true) otherwise.</returns>
		/// <exception cref="ValueError">Throws a <see cref="ValueError"/> exception if an invalid joystick button is specified.</exception>
		public static object KeyWait(object keyName, object options = null)
		{
			var keyname = keyName.As();
			var opts = options.As();
			bool waitIndefinitely;
			int sleepDuration;
			DateTime startTime;
			var vk = 0u; // For GetKeyState; stays 0 if this is a joystick control.
			bool waitForKeyDown;
			KeyStateTypes keyStateType;
			var joy = JoyControls.Invalid;
			uint? joystickId = 0;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;
			uint? modLR = null;
			if ((vk = ht.TextToVK(keyname, ref modLR, layout: null)) == 0)
			{
				joy = Joystick.ConvertJoy(keyname, ref joystickId);

				if (!Joystick.IsJoystickButton(joy))//Currently, only buttons are supported.
					return Errors.ValueErrorOccurred($"Invalid keyname parameter: {keyname}");// It's either an invalid key name or an unsupported Joy-something.
			}

			// Set defaults:
			waitForKeyDown = false;  // The default is to wait for the key to be released.
			keyStateType = KeyStateTypes.Physical;  // Since physical is more often used.
			waitIndefinitely = true;
			sleepDuration = 0;

			for (var i = 0; i < opts.Length; ++i)
			{
				switch (char.ToUpper(opts[i]))
				{
					case 'D':
						waitForKeyDown = true;
						break;

					case 'L':
						keyStateType = KeyStateTypes.Logical;
						break;

					case 'T':
						// Although ATOF() supports hex, it's been documented in the help file that hex should
						// not be used (see comment above) so if someone does it anyway, some option letters
						// might be misinterpreted:
						waitIndefinitely = false;
						var numstr = "";
						var cc = CultureInfo.CurrentCulture;

						for (var numi = i + 1; numi < opts.Length; numi++)
						{
							if (char.IsDigit(opts[numi]) || opts[numi] == cc.NumberFormat.NumberDecimalSeparator[0])
								numstr += opts[numi];
							else
								break;
						}

						sleepDuration = (int)(numstr.ParseDouble() * 1000);
						break;
				}
			}

			for (startTime = DateTime.UtcNow; ;) // start_time is initialized unconditionally for use with v1.0.30.02's new logging feature further below.
			{
				// Always do the first iteration so that at least one check is done.
				if (vk != 0) // Waiting for key or mouse button, not joystick.
				{
					if (ScriptGetKeyState(vk, keyStateType) == waitForKeyDown)
						return true;
				}
				else // Waiting for joystick button
				{
					if (Joystick.ScriptGetJoyState(joy, joystickId.Value) is bool b && b == waitForKeyDown)
						return true;
				}

				// Must cast to int or any negative result will be lost due to DWORD type:
				if (waitIndefinitely || (int)(sleepDuration - (DateTime.UtcNow - startTime).TotalMilliseconds) > Script.SLEEP_INTERVAL_HALF)
				{
					_ = Flow.Sleep(Script.SLEEP_INTERVAL);
				}
				else // Done waiting.
					return false; // Since it timed out, we override the default with this.
			}
		}

		/// <summary>
		/// Sends simulated keystrokes and mouse clicks to the active window.
		/// By default, Send is synonymous with <see cref="SendInput"/>; but it can be made a synonym for <see cref="SendEvent"/> or <see cref="SendPlay"/> via <see cref="SendMode"/>.
		/// </summary>
		/// <param name="keys">The sequence of keys to send.</param>
		public static object Send(object keys)
		{
			var script = Script.TheScript;
			script.HookThread.kbdMsSender.SendKeys(keys.As(), SendRawModes.NotRaw, ThreadAccessors.A_SendMode, 0);
			return DefaultObject;
		}

		//We initially had these using BeginInvoke(), but that is wrong, because these will often be launched from threads in responde to a hotkey/string.
		//The state of those threads needs to be preserved, but invoking will overwrite that state by putting the call on the main GUI thread.
		//This is unlikely to be true anymore since we implemented the pseudo-thread functionality of AHK.
		//So put them back to just straight calls, revisit if cross threading bugs occur.
		//public static void SendEvent(object obj) => Keysharp.Runtime.Script.mainWindow.CheckedBeginInvoke(() => Keysharp.Runtime.Script.HookThread.kbdMsSender.SendKeys(obj.As(), SendRawModes.NotRaw, SendModes.Event, 0), true, true);
		//public static void Send(object obj) => Keysharp.Runtime.Script.mainWindow.CheckedBeginInvoke(() => Keysharp.Runtime.Script.HookThread.kbdMsSender.SendKeys(obj.As(), SendRawModes.NotRaw, Accessors.SendMode, 0), true, true);
		//public static void SendInput(object obj) => Keysharp.Runtime.Script.mainWindow.CheckedBeginInvoke(() => Keysharp.Runtime.Script.HookThread.kbdMsSender.SendKeys(obj.As(), SendRawModes.NotRaw, Accessors.SendMode == SendModes.InputThenPlay ? SendModes.InputThenPlay : SendModes.Input, 0), true, true);
		//public static void SendPlay(object obj) => Keysharp.Runtime.Script.mainWindow.CheckedBeginInvoke(() => Keysharp.Runtime.Script.HookThread.kbdMsSender.SendKeys(obj.As(), SendRawModes.NotRaw, SendModes.Play, 0), true, true);
		//public static void SendText(object obj) => Keysharp.Runtime.Script.mainWindow.CheckedBeginInvoke(() => Keysharp.Runtime.Script.HookThread.kbdMsSender.SendKeys(obj.As(), SendRawModes.RawText, Accessors.SendMode, 0), true, true);

		/// <summary>
		/// SendEvent sends keystrokes using the Windows keybd_event function (search Microsoft Docs for details).<br/>
		/// The rate at which keystrokes are sent is determined by <see cref="SetKeyDelay"/>.<br/>
		/// <see cref="SendMode"/> can be used to make Send synonymous with <see cref="SendEvent"/> or <see cref="SendPlay"/>.
		/// </summary>
		public static object SendEvent(object keys)
		{
			var script = Script.TheScript;
			script.HookThread.kbdMsSender.SendKeys(keys.As(), SendRawModes.NotRaw, SendModes.Event, 0);
			return DefaultObject;
		}

		/// <summary>
		/// <see cref="SendInput"/> and <see cref="SendPlay"/> use the same syntax as <see cref="SendEvent"/> but are generally faster and more reliable.<br/>
		/// In addition, they buffer any physical keyboard or mouse activity during the send, which prevents the<br/>
		/// user's keystrokes from being interspersed with those being sent.<br/>
		/// <see cref="SendMode"/> can be used to make <see cref="Send"/> synonymous with <see cref="SendInput"/> or <see cref="SendPlay"/>.
		/// </summary>
		public static object SendInput(object keys)
		{
			var script = Script.TheScript;
			script.HookThread.kbdMsSender.SendKeys(keys.As(), SendRawModes.NotRaw, ThreadAccessors.A_SendMode == SendModes.InputThenPlay ? SendModes.InputThenPlay : SendModes.Input, 0);
			return DefaultObject;
		}

		/// <summary>
		/// Controls which artificial keyboard and mouse events are ignored by hotkeys and hotstrings.
		/// </summary>
		/// <param name="level">An integer between 0 and 100.</param>
		/// <returns>The previous setting; an integer between 0 and 100.</returns>
		public static object SendLevel(object level)
		{
			var old = A_SendLevel;
			A_SendLevel = level.Al();
			return old;
		}

		/// <summary>
		/// Makes Send synonymous with <see cref="SendEvent"/> or <see cref="SendPlay"/> rather than the default (<see cref="SendInput"/>).<br/>
		/// Also makes <see cref="Click"/> and <see cref="MouseMove"/>/<see cref="Click"/>/<see cref="Drag"/> use the specified method.
		/// </summary>
		/// <param name="mode">Event, Input, InputThenPlay or Play.</param>
		/// <returns>The previous setting; either Event, Input, InputThenPlay or Play.</returns>
		public static object SendMode(object mode)
		{
			var old = A_SendMode;
			A_SendMode = mode;
			return old;
		}

		/// <summary>
		/// <see cref="SendInput"/> and <see cref="SendPlay"/> use the same syntax as <see cref="SendEvent"/> but are generally faster and more reliable.<br/>
		/// In addition, they buffer any physical keyboard or mouse activity during the send, which prevents the<br/>
		/// user's keystrokes from being interspersed with those being sent.<br/>
		/// <see cref="SendMode"/> can be used to make <see cref="Send"/> synonymous with <see cref="SendInput"/> or <see cref="SendPlay"/>.
		/// </summary>
		public static object SendPlay(object keys)
		{
			var script = Script.TheScript;
			script.HookThread.kbdMsSender.SendKeys(keys.As(), SendRawModes.NotRaw, SendModes.Play, 0);
			return DefaultObject;
		}

		/// <summary>
		/// Similar to <see cref="Send"/>, except that all characters in Keys are interpreted and sent literally. See Text mode for details.
		/// </summary>
		public static object SendText(object keys)
		{
			var script = Script.TheScript;
			script.HookThread.kbdMsSender.SendKeys(keys.As(), SendRawModes.RawText, ThreadAccessors.A_SendMode, 0);
			return DefaultObject;
		}

		/// <summary>
		/// Sets the state of CapsLock. Can also force the key to stay on or off.
		/// </summary>
		/// <param name="state">
		/// If blank or omitted, the AlwaysOn/Off attribute of the key is removed (if present).<br/>
		/// Otherwise, specify one of the following values:<br/>
		///     On or 1 (true): Turns on the key and removes the AlwaysOn/Off attribute of the key (if present).<br/>
		///     Off or 0 (false): Turns off the key and removes the AlwaysOn/Off attribute of the key (if present).<br/>
		///     AlwaysOn: Forces the key to stay on permanently.<br/>
		///     AlwaysOff: Forces the key to stay off permanently.
		/// </param>
		public static object SetCapsLockState(object state = null)
		{
			// Keys is a cross-platform alias (see GlobalUsings); this is a standard VK code handled by the platform-specific ToggleKeyState.
			SetToggleState((uint)Keys.Capital, ref Script.TheScript.KeyboardData.toggleStates.forceCapsLock, state.As());
			return DefaultObject;
		}

		/// <summary>
		/// Sets the delay that will occur after each keystroke sent by <see cref="Send"/> or <see cref="ControlSend"/>.
		/// </summary>
		/// <param name="delay">If omitted, the current delay is retained. Otherwise, specify the time in milliseconds.<br/>
		/// Specify -1 for no delay at all or 0 for the smallest possible delay<br/>
		/// (however, if the Play parameter is present, both 0 and -1 produce no delay).
		/// </param>
		/// <param name="pressDuration">Certain games and other specialized applications may require a delay<br/>
		/// inside each keystroke; that is, after the press of the key but before its release.<br/>
		/// If omitted, the current press duration is retained.Otherwise, specify the time in milliseconds.<br/>
		/// Specify -1 for no delay at all or 0 for the smallest possible delay<br/>
		/// (however, if the Play parameter is present, both 0 and -1 produce no delay).<br/>
		/// Note: PressDuration also produces a delay after any change to the modifier key<br/>
		/// state (Ctrl, Alt, Shift, and Win) needed to support the keys being sent.
		/// </param>
		/// <param name="play">If blank or omitted, the delay and press duration are applied to the traditional <see cref="SendEvent"/><br/>
		/// mode. Otherwise, specify the word Play to apply both to the <see cref="SendPlay"/> mode.<br/>
		/// If a script never uses this parameter, both are always -1 for <see cref="SendPlay"/>.
		/// </param>
		public static object SetKeyDelay(object delay = null, object pressDuration = null, object play = null)
		{
			var isPlay = play.As().Equals("play", StringComparison.OrdinalIgnoreCase);
			var del = isPlay ? A_KeyDelayPlay : A_KeyDelay;
			var dur = isPlay ? A_KeyDurationPlay : A_KeyDuration;

			if (delay != null)
				del = delay.Al();

			if (pressDuration != null)
				dur = pressDuration.Al();

			if (isPlay)
			{
				A_KeyDelayPlay = del;
				A_KeyDurationPlay = dur;
			}
			else
			{
				A_KeyDelay = del;
				A_KeyDuration = dur;
			}

			return DefaultObject;
		}

		/// <summary>
		/// See <see cref="SetCapsLockState"/>, but for NumLock.
		/// </summary>
		public static object SetNumLockState(object state = null)
		{
			// Keys is a cross-platform alias (see GlobalUsings); this is a standard VK code handled by the platform-specific ToggleKeyState.
			SetToggleState((uint)Keys.NumLock, ref Script.TheScript.KeyboardData.toggleStates.forceNumLock, state.As());
			return DefaultObject;
		}

		/// <summary>
		/// See <see cref="SetCapsLockState"/>, but for ScrollLock.
		/// </summary>
		public static object SetScrollLockState(object state = null)
		{
			// Keys is a cross-platform alias (see GlobalUsings); this is a standard VK code handled by the platform-specific ToggleKeyState.
			SetToggleState((uint)Keys.Scroll, ref Script.TheScript.KeyboardData.toggleStates.forceScrollLock, state.As());
			return DefaultObject;
		}

		/// <summary>
		/// Whether to restore the state of CapsLock after a <see cref="Send"/>.
		/// </summary>
		/// <param name="setting">If true, CapsLock will be restored to its former value if Send needed to change<br/>
		/// it temporarily for its operation.<br/>
		/// If false, the state of CapsLock is not changed at all.<br/>
		/// As a result, <see cref="Send"/> will invert the case of the characters if CapsLock happens to be ON during the operation.
		/// </param>
		public static object SetStoreCapsLockMode(object setting)
		{
			var old = A_StoreCapsLockMode;
			A_StoreCapsLockMode = setting;
			return old;
		}

		public static object HotIf(object callback = null)
		{
			var script = Script.TheScript;

			if (callback != null)
			{
				var funcobj = Functions.GetKeysharpFunc(callback, null, true);
				var cp = HotkeyDefinition.FindHotkeyIf(funcobj, script.hotExprs);

				if (cp == null && funcobj != null)
					script.hotExprs.Add(cp = funcobj);

				script.Threads.CurrentThread.hotCriterion = cp;
			}
			else
				script.Threads.CurrentThread.hotCriterion = null;

			return DefaultObject;
		}

		public static object HotIfWinActive(object winTitle = null, object winText = null) => HotkeyDefinition.SetupHotIfWin("HotIfWinActivePrivate", winTitle, winText);

		public static object HotIfWinExist(object winTitle = null, object winText = null) => HotkeyDefinition.SetupHotIfWin("HotIfWinExistPrivate", winTitle, winText);

		public static object HotIfWinNotActive(object winTitle = null, object winText = null) => HotkeyDefinition.SetupHotIfWin("HotIfWinNotActivePrivate", winTitle, winText);

		public static object HotIfWinNotExist(object winTitle = null, object winText = null) => HotkeyDefinition.SetupHotIfWin("HotIfWinNotExistPrivate", winTitle, winText);

		/// <summary>
		/// Get the hotkey descriptions and put them in the Vars tab of the main window.
		/// </summary>
		public static object ListHotkeys()
		{
			Script.TheScript.mainWindow?.ListHotkeys();
			return DefaultObject;
		}

		/// <summary>
		/// Internal helper to convert an input mode from a string to a <see cref="ToggleValueType"/>.
		/// </summary>
		/// <param name="mode">The string name of the input mode.</param>
		/// <returns>The <see cref="ToggleValueType"/> equivalent of the mode string.</returns>
		internal static ToggleValueType ConvertBlockInput(string mode)
		{
			var toggle = Conversions.ConvertOnOff(mode);

			if (toggle != ToggleValueType.Invalid)
				return toggle;

			if (string.Compare(mode, "Send", true) == 0) return ToggleValueType.Send;

			if (string.Compare(mode, "Mouse", true) == 0) return ToggleValueType.Mouse;

			if (string.Compare(mode, "SendAndMouse", true) == 0) return ToggleValueType.SendAndMouse;

			if (string.Compare(mode, "Default", true) == 0) return ToggleValueType.Default;

			if (string.Compare(mode, "MouseMove", true) == 0) return ToggleValueType.MouseMove;

			if (string.Compare(mode, "MouseMoveOff", true) == 0) return ToggleValueType.MouseMoveOff;

			return ToggleValueType.Invalid;
		}

		/// <summary>
		/// Internal helper to get the name of a key.
		/// </summary>
		/// <param name="vk">The virtual key code.</param>
		/// <param name="sc">The scan code.</param>
		/// <param name="def">The default value to use if the conversion failed.</param>
		/// <returns>The name of the key as specified by vk and sc. Else def if the conversion failed.</returns>
		internal static string GetKeyNameHelper(uint vk, uint sc, string def = "not found")
		{
			var ht = Script.TheScript.HookThread;
			var buf = ""; // Set default.

			if (vk == 0 && sc == 0)
				return buf;

			if (vk == 0)
				vk = KeyCodes.MapScToVk(sc);
			else if (sc == 0 && (vk == (uint)Keys.Return || (sc = KeyCodes.MapVkToSc(vk, true)) == 0)) // Prefer the non-Numpad name.
				sc = KeyCodes.MapVkToSc(vk);

			// Check SC first to properly differentiate between Home/NumpadHome, End/NumpadEnd, etc.
			// v1.0.43: WheelDown/Up store the notch/turn count in SC, so don't consider that to be a valid SC.
			if (sc != 0 && !MouseUtils.IsWheelVK(vk))
			{
				buf = ht.SCtoKeyName(sc, false);

				if (buf != "")
					return buf;
				// Otherwise this key is probably one we can handle by VK.
			}

			if ((buf = ht.VKtoKeyName(vk, false)) != "")
				return buf;

			return def;// Since this key is unrecognized, return the caller-supplied default value.
		}

		/// <summary>
		/// Internal helper to blocks keyboard and mouse input from reaching the script.
		/// </summary>
		/// <param name="toggle">True to block, else false to unblock.</param>
		/// <returns>Always returns OK for caller convenience.</returns>
		internal static ResultType ScriptBlockInput(ToggleValueType toggle)
		{
			var script = Script.TheScript;
		#if LINUX
			var kud = script.KeyboardUtilsData;
			var stateChanged = false;

			switch (toggle)
			{
				case ToggleValueType.On:
				case ToggleValueType.Off:
					script.KeyboardData.blockInput = toggle == ToggleValueType.On;
					stateChanged = true;
					break;

				case ToggleValueType.Send:
				case ToggleValueType.Mouse:
				case ToggleValueType.SendAndMouse:
				case ToggleValueType.Default:
					// These modes only store the policy; they do not block anything immediately.
					// The send and mouse paths call ScriptBlockInput(On) at the start of each
					// operation and ScriptBlockInput(Off) at the end, so the actual native
					// block is applied through the On/Off branch, not here.
					script.KeyboardData.blockInputMode = toggle;
					break;

				case ToggleValueType.MouseMove:
					script.KeyboardData.blockMouseMove = true;
					stateChanged = true;
					break;

				case ToggleValueType.MouseMoveOff:
					script.KeyboardData.blockMouseMove = false;
					stateChanged = true;
					break;
				// default (NEUTRAL or TOGGLE_INVALID): do nothing.
			}

			if (!stateChanged)
				return ResultType.Ok;

			// Movement-only blocking is a mouse-hook concern. LinuxHookThread already
			// suppresses physical move events while passing buttons, wheels and injected moves.
			if (script.KeyboardData.blockMouseMove)
				HotkeyDefinition.InstallMouseHook(script);

			var inputdMask = Keysharp.Internals.Input.Linux.KeysharpInputdClient.BlockInputMask.None;

			if (script.KeyboardData.blockInput)
				inputdMask = Keysharp.Internals.Input.Linux.KeysharpInputdClient.BlockInputMask.Keyboard
					| Keysharp.Internals.Input.Linux.KeysharpInputdClient.BlockInputMask.Mouse;

			var directBlockApplied = Keysharp.Internals.Input.Linux.KeysharpInputdManager.TrySetBlockInput(inputdMask, out _);
			var mouseMoveApplied = !script.KeyboardData.blockMouseMove || script.HookThread.HasMouseHook();
			var platformApplied = directBlockApplied && (script.KeyboardData.blockInput || mouseMoveApplied);

			if (platformApplied)
			{
				// The hook/direct-block path may become available after the X11 fallback disabled
				// devices. Release only devices which this script disabled through that fallback.
				foreach (var id in script.KeyboardData.xinputBlockedDevices.ToArray())
				{
					var result = RunCommand("xinput", "--enable", id.ToString(CultureInfo.InvariantCulture));

					if (result.Succeeded)
						_ = script.KeyboardData.xinputBlockedDevices.Remove(id);
					else
						Diagnostics.Debug.WriteLine($"BlockInput: xinput command failed for device {id}: {result.ErrorMessage}");
				}
			}
			else
			{
				_ = kud.GetKeybdMouseDevices();

				foreach (var id in kud.kbMouseList.Concat(script.KeyboardData.xinputBlockedDevices).Distinct().ToArray())
				{
					var disable = script.KeyboardData.blockInput
						|| (script.KeyboardData.blockMouseMove && kud.mouseList.Contains(id));
					var result = RunCommand("xinput", disable ? "--disable" : "--enable",
						id.ToString(CultureInfo.InvariantCulture));

					if (result.Succeeded)
					{
						if (disable)
							_ = script.KeyboardData.xinputBlockedDevices.Add(id);
						else
							_ = script.KeyboardData.xinputBlockedDevices.Remove(id);
					}
					else
						Diagnostics.Debug.WriteLine($"BlockInput: xinput command failed for device {id}: {result.ErrorMessage}");
				}
			}
		#elif WINDOWS
			switch (toggle)
			{
				// Always turn input ON/OFF even if g_BlockInput says its already in the right state.  This is because
				// BlockInput can be externally and undetectably disabled, e.g. if the user presses Ctrl-Alt-Del:
				case ToggleValueType.On:
				case ToggleValueType.Off:
					var state = toggle == ToggleValueType.On;
					_ = WindowsAPI.BlockInput(state);
					script.KeyboardData.blockInput = state;
					break;

				case ToggleValueType.Send:
				case ToggleValueType.Mouse:
				case ToggleValueType.SendAndMouse:
				case ToggleValueType.Default:
					script.KeyboardData.blockInputMode = toggle;
					break;

				case ToggleValueType.MouseMove:
					script.KeyboardData.blockMouseMove = true;
					HotkeyDefinition.InstallMouseHook(script);
					break;

				case ToggleValueType.MouseMoveOff:
					script.KeyboardData.blockMouseMove = false;
					// But the mouse hook is left installed because it might be needed by other things. This approach is similar to that used by the Input command.
					break;
					// default (NEUTRAL or TOGGLE_INVALID): do nothing.
			}
	#else
			switch (toggle)
			{
				case ToggleValueType.On:
				case ToggleValueType.Off:
					script.KeyboardData.blockInput = toggle == ToggleValueType.On;

					// There is no OS-level "block all input" API on macOS (unlike Windows' BlockInput()),
					// so blocking is implemented by suppressing physical events in the keyboard/mouse hook.
					// Make sure that hook is actually installed and running.
					if (toggle == ToggleValueType.On)
					{
						HotkeyDefinition.InstallKeybdHook(script);
						HotkeyDefinition.InstallMouseHook(script);
					}

					break;

				case ToggleValueType.Send:
				case ToggleValueType.Mouse:
				case ToggleValueType.SendAndMouse:
				case ToggleValueType.Default:
					script.KeyboardData.blockInputMode = toggle;
					break;

				case ToggleValueType.MouseMove:
					script.KeyboardData.blockMouseMove = true;
					HotkeyDefinition.InstallMouseHook(script);
					break;

				case ToggleValueType.MouseMoveOff:
					script.KeyboardData.blockMouseMove = false;
					break;
			}

			script.HookThread.RefreshPlatformKeyGrabs();
	#endif

			return ResultType.Ok;//By design, it never returns FAIL.
		}

		/// <summary>
		/// Internal helper to get the state of a key.
		/// </summary>
		/// <param name="vk">The key to examine.</param>
		/// <param name="keyStateType">The type of state to examine: toggle or physical.</param>
		/// <returns>true if down, else false</returns>
#if LINUX
		/// <summary>
		/// Acquires the inputd capability that reading global key state needs, at the one boundary where the
		/// script asked for it. Reading which keys the user is holding is monitoring data, and the daemon
		/// enforces that: it answers GetKeyState/GetPointerButtons only for a client holding the matching hook
		/// capability (403 otherwise), so polling can never become a back door around the hook permission.
		/// <para>
		/// This deliberately lives here rather than inside the query, because Send also reads the current
		/// modifier state to decide what to press and release. Asking down there would turn every Send into a
		/// keyboard-monitoring prompt — escalating "may type" into "may read everything you type", which is the
		/// exact bundling the permission model exists to prevent. GetKeyState and KeyWait are the only callers
		/// of this method, and both are things the script explicitly asked for.
		/// </para>
		/// <para>
		/// The already-granted and already-declined steady states both resolve on a bitmask test without a lock,
		/// an allocation or a round trip, so a KeyWait polling loop costs nothing after the first call.
		/// </para>
		/// </summary>
		private static void EnsureKeyStateQueryPermission(uint vk)
		{
			var required = MouseUtils.IsMouseVK(vk)
				? Keysharp.Internals.Input.Linux.KeysharpInputdClient.Capabilities.HookMouse
				: Keysharp.Internals.Input.Linux.KeysharpInputdClient.Capabilities.HookKeyboard;

			if (!Keysharp.Internals.Input.Linux.KeysharpInputdManager.HasInputCapability(required))
				_ = Keysharp.Internals.Input.Linux.KeysharpInputdManager.EnsureCapabilities(required, "read input state");
		}
#endif

		internal static bool ScriptGetKeyState(uint vk, KeyStateTypes keyStateType)
		{
			var ht = Script.TheScript.HookThread;
			var kbdMouseSender = ht.kbdMsSender;

			if (vk == 0) // Assume "up" if indeterminate.
				return false;

#if LINUX
			EnsureKeyStateQueryPermission(vk);
#endif

			switch (keyStateType)
			{
				case KeyStateTypes.Toggle: // Whether a toggleable key such as CapsLock is currently turned on.
					return ht.IsKeyToggledOn(vk); // This also works for non-"lock" keys, but in that case the toggle state can be out of sync with other processes/

				case KeyStateTypes.Physical: // Physical state of key.
				{
					// "P" must NEVER install a hook. If the relevant hook is already present, use its tracked
					// physical state. Otherwise, use an authoritative platform query when available, then
					// fall back to the logical/live query below. The fallback matches AutoHotkey, where "P"
					// degrades to an OS query when the hook is absent.
					var isMouse = MouseUtils.IsMouseVK(vk);

					if (isMouse ? ht.HasMouseHook() : ht.HasKbdHook())
					{
						if (!isMouse)
						{
							bool? dummy = null;

							// The hook is installed, so use its tracked physical state rather than an OS query.
							// But first, correct the hook modifier state if it needs it (see GetModifierLRState()).
							if (ht.KeyToModifiersLR(vk, 0, ref dummy) != 0)    // It's a modifier.
								_ = kbdMouseSender.GetModifierLRState(true);

							if (vk < ht.physicalKeyState.Length)
								return (ht.physicalKeyState[vk] & KeyboardMouseSender.StateDown) != 0;

							return false;
						}

						return (ht.physicalKeyState[vk] & KeyboardMouseSender.StateDown) != 0;
					}

					// Some platforms expose authoritative physical device state without requiring this
					// process to install a hook (inputd and macOS HID state). If they do not, explicitly
					// fall through to the logical/live query below, as AutoHotkey does without a hook.
					if (!isMouse
						&& Keysharp.Internals.Platform.Keyboard.TryGetKeyStatePhysical(vk, out var physicalDown))
						return physicalDown;

					break;
				}
			} // switch()

			// Otherwise, use the default state-type: KEYSTATE_LOGICAL
			// On XP/2K at least, a key can be physically down even if it isn't logically down,
			// which is why this historically used the GetKeyState-based path rather than some more
			// comprehensive method such as consulting the physical key state as tracked by the hook:
			// v1.0.42.01 kept this from being changed to the current-state query for backward compatibility.
			// One example is the journal playback hook: when a window owned by the script receives
			// such a keystroke, only GetKeyState() can detect the changed state of the key, not GetAsyncKeyState().
			// Keysharp: Windows 11 has removed journal playback hook, so this now uses IsKeyDownLogical's
			// current-state semantics instead.
			return ht.IsKeyDownLogical(vk);
		}

		/// <summary>
		/// Internal helper to get the name of a key in different ways depending on the calling context.
		/// </summary>
		/// <param name="keyname">The name of the key to examine.</param>
		/// <param name="callid">The calling context. 0, 1 or 2.</param>
		/// <returns>A string or integer representation of the key.</returns>
		private static object GetKeyNamePrivate(string keyname, int callid)
		{
			var ht = Script.TheScript.HookThread;
			var vk = 0u;
			var sc = 0u;
			var source = KeySource.None;
			uint? modLR = null;
			_ = ht.TextToVKandSC(keyname, ref vk, ref sc, ref source, ref modLR);

			return callid switch
		{
				0 => GetKeyNameHelper(vk, sc, ""),
					1 => sc != 0 ? sc : KeyCodes.MapVkToSc(vk),
					2 => vk != 0 ? vk : KeyCodes.MapScToVk(sc),
					_ => "",
			};
		}

		private static object InstallHook(HookType whichHook, object install = null, object force = null)
		{
			var i = install.Ab(true);
			var f = force.Ab();
			var script = Script.TheScript;
			var ht = script.HookThread;

			if (i)
			{
				var op = whichHook == HookType.Keyboard ? "InstallKeybdHook" : "InstallMouseHook";
				_ = script.Permissions.EnsureInputMonitoring(operation: op);
			}

			//When the second parameter is true, unconditionally remove the hook. If the first parameter is
			//also true, the hook will be reinstalled fresh. Otherwise the hook will be left uninstalled,
			//until something happens to reinstall it, such as ManifestAllHotkeysHotstringsHooks().
			if (f)
				ht.AddRemoveHooks(ht.GetActiveHooks() & ~whichHook);

			RequireHook(script, whichHook, i);

			if (!f || i)
				_ = HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(script);

			return DefaultObject;
		}

		private static void RequireHook(Script script, HookType whichHook, bool require = true)
		{
			var hkd = script.HotkeyData;
			_ = require ? hkd.whichHookAlways |= whichHook : hkd.whichHookAlways &= ~whichHook;
		}

		/// <summary>
		/// Internal helper to toggle a key state.
		/// </summary>
		/// <param name="vk">The virtual key code of the key to toggle.</param>
		/// <param name="forceLock">Whether to lock the the key as always down or always off.</param>
		/// <param name="toggleText">The type of toggling to do: On, Off, AlwaysOn, AlwaysOff.</param>
		private static void SetToggleState(uint vk, ref ToggleValueType forceLock, string toggleText)
		{
			var toggle = Conversions.ConvertOnOffAlways(toggleText, ToggleValueType.Neutral);
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;

			switch (toggle)
			{
				case ToggleValueType.On:
				case ToggleValueType.Off:
					// Turning it on or off overrides any prior AlwaysOn or AlwaysOff setting.
					// Probably need to change the setting BEFORE attempting to toggle the
					// key state, otherwise the hook may prevent the state from being changed
					// if it was set to be AlwaysOn or AlwaysOff:
					forceLock = ToggleValueType.Neutral;
					_ = kbdMouseSender.ToggleKeyState(vk, toggle);
					break;

				case ToggleValueType.AlwaysOn:
				case ToggleValueType.AlwaysOff:
					EnsureForcedLockStatePermissions(vk);
					forceLock = (toggle == ToggleValueType.AlwaysOn) ? ToggleValueType.On : ToggleValueType.Off; // Must do this first.
					_ = kbdMouseSender.ToggleKeyState(vk, forceLock);
					// The hook is currently needed to support keeping these keys AlwaysOn or AlwaysOff, though
					// there may be better ways to do it (such as registering them as a hotkey, but
					// that may introduce quite a bit of complexity):
					HotkeyDefinition.InstallKeybdHook(script);
					break;

				case ToggleValueType.Neutral:
					// Note: No attempt is made to detect whether the keybd hook should be deinstalled
					// because it's no longer needed due to this change.  That would require some
					// careful thought about the impact on the status variables in the Hotkey class, etc.,
					// so it can be left for a future enhancement:
					forceLock = ToggleValueType.Neutral;
					break;
			}
		}

		private static void EnsureForcedLockStatePermissions(uint vk)
		{
			var operation = vk switch
			{
				(uint)Keys.Capital => "SetCapsLockState AlwaysOn/AlwaysOff",
				(uint)Keys.NumLock => "SetNumLockState AlwaysOn/AlwaysOff",
				(uint)Keys.Scroll => "SetScrollLockState AlwaysOn/AlwaysOff",
				_ => "SetLockState AlwaysOn/AlwaysOff"
			};
			var result = Script.TheScript.Permissions.RequestInputCapabilities(
				monitoring: true,
				injection: true,
				blockInput: false,
				operation: operation);

			if (!result.IsGranted)
				throw new InvalidOperationException(result.Message.IsNullOrEmpty()
					? $"Permission is required for '{operation}'."
					: result.Message);
		}
	}

	public partial class Ks
	{
		/// <summary>
		/// Retrieves Keysharp's platform-specific identifier for the current keyboard layout.
		/// </summary>
		/// <returns>A stable, readable platform-native layout string.</returns>
		public static string GetKeyboardLayout()
		{
			var layout = Platform.Keys.GetKeyboardLayoutName();
			return layout == "" ? "unknown" : layout;
		}

		/// <summary>
		/// Retrieves layout-aware key information for a key name or single character.
		/// </summary>
		/// <param name="keyName">A key name or single character to resolve.</param>
		/// <param name="layout">Optional layout string from <see cref="GetKeyboardLayout"/>.</param>
		/// <returns>An object with VK, SC, Name, Modifiers and Prefix, or 0 if unresolved.</returns>
		public static object GetKeyInfo(object keyName, object layout = null)
		{
			var script = Script.TheScript;
			var keyname = keyName.As();
			var keybdLayout = layout.IsNullOrEmpty() ? 0 : Platform.Keys.ResolveKeyboardLayout(layout.As());
			var ht = script.HookThread;
			var vk = 0u;
			var sc = 0u;
			var source = KeySource.None;
			uint? modifiersLR = 0u;

			if (!ht.TextToVKandSC(keyname, ref vk, ref sc, ref source, ref modifiersLR, KeybdLayoutRef.FromHandle(keybdLayout)))
				return 0L;

			if (vk == 0)
				vk = KeyCodes.MapScToVk(sc);

			if (sc == 0)
				sc = KeyCodes.MapVkToSc(vk);

			if (vk == 0 && sc == 0)
				return 0L;

			var name = Keyboard.GetKeyNameHelper(vk, sc, "");

			if (name == "")
				return 0L;

			return MakeKeyInfo(vk, sc, name, modifiersLR ?? 0u);
		}

		private static KeysharpObject MakeKeyInfo(uint vk, uint sc, string name, uint modifiersLR)
		{
			var obj = new KeysharpObject();
			obj.DefinePropInternal("VK", new OwnPropsDesc(obj, (long)vk));
			obj.DefinePropInternal("SC", new OwnPropsDesc(obj, (long)sc));
			obj.DefinePropInternal("Name", new OwnPropsDesc(obj, name));
			obj.DefinePropInternal("Modifiers", new OwnPropsDesc(obj, (long)KeyboardUtils.ConvertModifiersLR(modifiersLR)));
			obj.DefinePropInternal("Prefix", new OwnPropsDesc(obj, ModifiersLRToPrefix(modifiersLR)));
			return obj;
		}

		private static string ModifiersLRToPrefix(uint modifiersLR)
		{
			var sb = new StringBuilder(8);
			AppendModifierPrefix(sb, modifiersLR, KeyboardUtils.MOD_LCONTROL, KeyboardUtils.MOD_RCONTROL, '^');
			AppendModifierPrefix(sb, modifiersLR, KeyboardUtils.MOD_LSHIFT, KeyboardUtils.MOD_RSHIFT, '+');
			AppendModifierPrefix(sb, modifiersLR, KeyboardUtils.MOD_LALT, KeyboardUtils.MOD_RALT, '!');
			AppendModifierPrefix(sb, modifiersLR, KeyboardUtils.MOD_LWIN, KeyboardUtils.MOD_RWIN, '#');
			return sb.ToString();
		}

		private static void AppendModifierPrefix(StringBuilder sb, uint modifiersLR, uint left, uint right, char prefix)
		{
			var hasLeft = (modifiersLR & left) != 0;
			var hasRight = (modifiersLR & right) != 0;

			if (!hasLeft && !hasRight)
				return;

			if (hasRight && !hasLeft)
				_ = sb.Append('>');

			_ = sb.Append(prefix);
		}
	}

	internal class KeyboardData
	{
		internal readonly ToggleStates toggleStates = new ();
		internal bool blockInput;
		internal ToggleValueType blockInputMode = ToggleValueType.Default;
		internal bool blockMouseMove;
#if LINUX
		internal readonly HashSet<int> xinputBlockedDevices = [];
#endif
		internal Dictionary<string, HotkeyDefinition> hotkeys = [];
		internal Dictionary<string, HotstringDefinition> hotstrings = [];
	}

	/// <summary>
	/// Internal static holder to keep track of the toggle state of caps/num/scroll lock.
	/// </summary>
	internal class ToggleStates
	{
		internal ToggleValueType forceCapsLock = ToggleValueType.Neutral;
		internal ToggleValueType forceNumLock = ToggleValueType.Neutral;
		internal ToggleValueType forceScrollLock = ToggleValueType.Neutral;
	}
}
