using Keysharp.Builtins;
#if !WINDOWS
using System;
using System.Collections.Generic;
using Keysharp.Internals.Input.Keyboard;
#if LINUX
using Keysharp.Internals.Input.Linux;
#endif
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;
using static Keysharp.Internals.Input.Keyboard.KeyboardMouseSender;
using Keysharp.Internals.Input.Hooks;

namespace Keysharp.Internals.Input.Hooks.Unix
{
	/// <summary>
	/// Shared Unix hook thread implementation.
	/// Platform-specific grabbing/query behavior is delegated to overrides.
	/// </summary>
	internal class UnixHookThread : Keysharp.Internals.Input.Hooks.HookThread
	{
		private static readonly bool HookDisabled = ShouldDisableHook();
		protected string lastHookActivationFailure;

		protected readonly Lock hookStateLock = new();
		protected long hookStateGeneration;

		// Cached on/off
		protected volatile bool keyboardEnabled;
		protected volatile bool mouseEnabled;

		private int sendDepth;
		internal bool SendInProgress => sendDepth > 0;
		internal readonly struct SendScope : IDisposable
		{
			private readonly UnixHookThread owner;
			public SendScope(UnixHookThread owner)
			{
				this.owner = owner;
				owner.sendDepth++;
			}

			public void Dispose()
			{
				owner.sendDepth = Math.Max(0, owner.sendDepth - 1);
				if (owner.sendDepth == 0)
					owner.SyncModifiersAfterSend();
			}
		}
		internal UnixHookThread(Script script, string mutexName) : base(script, mutexName)
		{
			ConfigureScanCodeNames();
#if LINUX
			_ = DesktopKeyboardState.Current.Get();
#endif
		}

		internal SendScope EnterSendScope() => new(this);

		private void SyncModifiersAfterSend()
		{
			// Ensure modifiers don't stay logically down after Send manipulates them.
			// Platform senders may reconcile state more precisely, but this keeps the
			// shared logical snapshot from drifting after a send scope closes.
			_ = kbdMsSender?.GetModifierLRState(true);
		}

		protected void ResetTrackedInputState()
		{
			System.Array.Clear(physicalKeyState, 0, physicalKeyState.Length);

			kbdMsSender.modifiersLRLogical = 0;
			kbdMsSender.modifiersLRLogicalNonIgnored = 0;
			kbdMsSender.modifiersLRPhysical = 0;
		}

		// KeyCodes.MapVkToSc dispatches to the active platform scan-code backend, so the
		// names stay consistent with the actual SCs delivered by the hook.
		internal void ConfigureScanCodeNames()
		{
			AddScKeyName("NumpadEnter", KeyCodes.MapVkToSc(VK_RETURN, true));
			AddScKeyName("Delete", KeyCodes.MapVkToSc(VK_DELETE, true));
			AddScKeyName("Del", KeyCodes.MapVkToSc(VK_DELETE, true));
			AddScKeyName("Insert", KeyCodes.MapVkToSc(VK_INSERT, true));
			AddScKeyName("Ins", KeyCodes.MapVkToSc(VK_INSERT, true));
			AddScKeyName("Up", KeyCodes.MapVkToSc(VK_UP, true));
			AddScKeyName("Down", KeyCodes.MapVkToSc(VK_DOWN, true));
			AddScKeyName("Left", KeyCodes.MapVkToSc(VK_LEFT, true));
			AddScKeyName("Right", KeyCodes.MapVkToSc(VK_RIGHT, true));
			AddScKeyName("Home", KeyCodes.MapVkToSc(VK_HOME, true));
			AddScKeyName("End", KeyCodes.MapVkToSc(VK_END, true));
			AddScKeyName("PgUp", KeyCodes.MapVkToSc(VK_PRIOR, true));
			AddScKeyName("PageUp", KeyCodes.MapVkToSc(VK_PRIOR, true));
			AddScKeyName("PgDn", KeyCodes.MapVkToSc(VK_NEXT, true));
			AddScKeyName("PageDown", KeyCodes.MapVkToSc(VK_NEXT, true));
		}

		// -------------------- lifecycle --------------------
		protected internal override void DeregisterHooks()
		{
			lock (hookStateLock)
			{
				hookStateGeneration++;
				keyboardEnabled = false;
				mouseEnabled = false;
				StopPlatformHookCore(dispose: true);
				kbdHook = 0;
				mouseHook = 0;
				OnPlatformHookStateCommitted(HookType.None);
			}

			// Release the named hook mutexes now that no hooks are held (mirrors AddRemoveHooks; this path
			// doesn't route through it). Done outside the lock since closing a Mutex never touches hookStateLock.
			SyncHookMutexes(changeIsTemporary: false);
		}

		public override void SimulateKeyPress(uint key)
		{
			// SimulateKeyPress expects a WinForms virtual-key value.
			var vk = (uint)((Keys)key & Keys.KeyCode);
			if (vk == 0)
				return;

			var sc = KeyCodes.MapVkToSc(vk);

			if (!keyboardEnabled)
				return;

			var e = new KeyboardHookEventArgs(EventType.KeyPressed, vk, sc);
			_ = LowLevelCommon(e, vk, sc, sc, keyUp: false, extraInfo: 0, eventFlags: 0);
		}

		// -------------------- enable/disable --------------------

		internal override void AddRemoveHooks(HookType hooksToBeActive, bool changeIsTemporary = false)
		{
			long requestGeneration;

			lock (hookStateLock)
				requestGeneration = ++hookStateGeneration;

			ChangePlatformHookState(hooksToBeActive & (HookType.Keyboard | HookType.Mouse), changeIsTemporary, requestGeneration);

			// Advertise the now-current hook state to other Keysharp scripts via the shared named mutexes
			// (HasKbdHook()/HasMouseHook() read the sentinels ChangePlatformHookState just updated). This is what
			// lets SendInput fall back correctly and A_KeybdHookInstalled/A_MouseHookInstalled report bit 2.
			SyncHookMutexes(changeIsTemporary);
		}

		protected virtual void ChangePlatformHookState(HookType req, bool changeIsTemporary, long expectedGeneration)
		{
			if (HookDisabled)
			{
				lock (hookStateLock)
				{
					if (hookStateGeneration != expectedGeneration)
						return;

					keyboardEnabled = false;
					mouseEnabled = false;
					kbdHook = 0;
					mouseHook = 0;
					StopPlatformHookCore(dispose: true);
					OnPlatformHookStateCommitted(HookType.None);
				}

				lastHookActivationFailure = PlatformHookDisabledMessage;
				Diagnostics.Debug.WriteLine(lastHookActivationFailure);
				return;
			}

			var wantKeyboard = (req & HookType.Keyboard) != 0;
			var wantMouse = (req & HookType.Mouse) != 0;

			lock (hookStateLock)
			{
				if (hookStateGeneration != expectedGeneration)
					return;

				var hadKeyboard = keyboardEnabled;
				var hadMouse = mouseEnabled;

				if (!wantKeyboard && !wantMouse)
				{
					keyboardEnabled = false;
					mouseEnabled = false;
					kbdHook = 0;
					mouseHook = 0;
					lastHookActivationFailure = null;

					if (!changeIsTemporary)
						StopPlatformHookCore(dispose: true);

					OnPlatformHookStateCommitted(HookType.None);

					return;
				}

				if (!StartPlatformHookCore(wantKeyboard, wantMouse, out var startFailure))
				{
					keyboardEnabled = false;
					mouseEnabled = false;
					kbdHook = 0;
					mouseHook = 0;
					lastHookActivationFailure = startFailure;

					if (!string.IsNullOrEmpty(lastHookActivationFailure))
						Diagnostics.Debug.WriteLine(lastHookActivationFailure);

					OnPlatformHookStartFailed(startFailure);
					OnPlatformHookStateCommitted(HookType.None);
					return;
				}

				if (!changeIsTemporary)
				{
					if (wantKeyboard && !hadKeyboard)
					{
						keyboardEnabled = false;
						kbdHook = 0;
						ResetHook(false, HookType.Keyboard, true);
					}

					if (wantMouse && !hadMouse)
					{
						mouseEnabled = false;
						mouseHook = 0;
						ResetHook(false, HookType.Mouse, true);
					}
				}

				keyboardEnabled = wantKeyboard;
				mouseEnabled = wantMouse;
				kbdHook = wantKeyboard ? 1 : 0;
				mouseHook = wantMouse ? 1 : 0;
				lastHookActivationFailure = null;
				OnPlatformHookStateCommitted(req);
			}
		}

		internal override string GetHookActivationFailureReason() => lastHookActivationFailure ?? "";

		protected virtual string PlatformHookDisabledMessage => "Unix hook disabled via KEYSHARP_DISABLE_HOOK=1.";

		protected virtual bool StartPlatformHookCore(bool wantKeyboard, bool wantMouse, out string message)
		{
			message = wantKeyboard || wantMouse
				? "Unix global hooks are not available on this platform."
				: null;
			return false;
		}

		protected virtual void OnPlatformHookStartFailed(string message)
		{
		}

		/// <summary>Called after reset and publication of a successfully requested platform-hook state.</summary>
		protected virtual void OnPlatformHookStateCommitted(HookType activeHooks)
		{
		}

		protected virtual void StopPlatformHookCore(bool dispose)
		{
			if (!dispose)
				return;

			ResetTrackedInputState();
			SetMoveSuppression(false);
		}

		internal override void Unhook() => DeregisterHooks();
		internal override void Unhook(nint hook) => DeregisterHooks();

		private static bool ShouldDisableHook()
		{
			var env = Environment.GetEnvironmentVariable("KEYSHARP_DISABLE_HOOK");
			return !string.IsNullOrEmpty(env) &&
				   (env.Equals("1") || env.Equals("true", StringComparison.OrdinalIgnoreCase) || env.Equals("yes", StringComparison.OrdinalIgnoreCase));
		}

		private bool moveSuppressionActive;
		protected readonly object moveSuppressionLock = new();

		protected virtual bool OnMoveSuppressionChanged(bool active) => true;

		protected void SetMoveSuppression(bool active)
		{
			lock (moveSuppressionLock)
			{
				if (active == moveSuppressionActive)
					return;

				if (OnMoveSuppressionChanged(active))
					moveSuppressionActive = active;
			}
		}

		protected static bool HasKeysharpInjectedExtraInfo(ulong extraInfo)
		{
			var rawExtraInfo = unchecked((long)extraInfo);
			return rawExtraInfo == KeyIgnore
				   || rawExtraInfo == KeyBlockThis
				   || (rawExtraInfo >= KeyIgnoreMin() && rawExtraInfo <= KeyIgnoreLevel(0));
		}

#if OSX
		private static bool IsModifierKey(uint vk) => vk is
			VK_SHIFT or VK_LSHIFT or VK_RSHIFT or
			VK_CONTROL or VK_LCONTROL or VK_RCONTROL or
			VK_MENU or VK_LMENU or VK_RMENU or
			VK_LWIN or VK_RWIN;

		// Records a non-modifier key's physical state from an event that was not injected. LowLevelCommon records it
		// too for the events that reach it, so only a key suppressed before that, as BlockInput does, needs this.
		protected void UpdateObservedPhysicalKeyState(uint vk, bool keyUp, bool isInjected)
		{
			if (isInjected || IsModifierKey(vk) || vk == 0 || vk >= physicalKeyState.Length)
				return;

			physicalKeyState[vk] = (byte)(keyUp ? 0 : StateDown);
		}
#endif

		// Called when a physical (non-injected) key-down is suppressed by the hook.
		// Lets platforms undo side effects the OS applies below the event tap
		// (e.g. the macOS HID driver toggles CapsLock before suppression takes effect).
		protected virtual void OnPhysicalKeyDownSuppressed(uint vk)
		{
		}

		// -------------------- utilities & abstract impls --------------------

		internal override bool IsKeyToggledOn(uint vk)
		{
			if (Platform.Keyboard.TryGetIndicatorStatesLogical(out var capsOn, out var numOn, out var scrollOn))
			{
				return vk switch
				{
					VK_CAPITAL => capsOn,
					VK_NUMLOCK => numOn,
					VK_SCROLL => scrollOn,
					_ => false
				};
			}

			return false;
		}

		internal override uint SC_LCONTROL => KeyCodes.MapVkToSc(VK_LCONTROL);
		internal override uint SC_RCONTROL => KeyCodes.MapVkToSc(VK_RCONTROL);
		internal override uint SC_LALT => KeyCodes.MapVkToSc(VK_LMENU);
		internal override uint SC_RALT => KeyCodes.MapVkToSc(VK_RMENU);
		internal override uint SC_LSHIFT => KeyCodes.MapVkToSc(VK_LSHIFT);
		internal override uint SC_RSHIFT => KeyCodes.MapVkToSc(VK_RSHIFT);
		internal override uint SC_LWIN => KeyCodes.MapVkToSc(VK_LWIN);
		internal override uint SC_RWIN => KeyCodes.MapVkToSc(VK_RWIN);

		internal override uint KeyToModifiersLR(uint vk, uint sc, ref bool? isNeutral)
		{
			if (vk == 0 && sc == 0)
				return 0;

			if (vk != 0)
			{
				switch (vk)
				{
					case VK_SHIFT:
					{
						if (sc != 0)
						{
							var mapped = KeyCodes.MapScToVk(sc);
							if (mapped == VK_RSHIFT) return MOD_RSHIFT;
							if (mapped == VK_LSHIFT)
							{
								if (isNeutral != null) isNeutral = true;
								return MOD_LSHIFT;
							}
						}

						if (isNeutral != null) isNeutral = true;
						return MOD_LSHIFT;
					}
					case VK_CONTROL:
					{
						if (sc != 0)
						{
							var mapped = KeyCodes.MapScToVk(sc);
							if (mapped == VK_RCONTROL) return MOD_RCONTROL;
							if (mapped == VK_LCONTROL)
							{
								if (isNeutral != null) isNeutral = true;
								return MOD_LCONTROL;
							}
						}

						if (isNeutral != null) isNeutral = true;
						return MOD_LCONTROL;
					}
					case VK_MENU:
					{
						if (sc != 0)
						{
							var mapped = KeyCodes.MapScToVk(sc);
							if (mapped == VK_RMENU) return MOD_RALT;
							if (mapped == VK_LMENU)
							{
								if (isNeutral != null) isNeutral = true;
								return MOD_LALT;
							}
						}

						if (isNeutral != null) isNeutral = true;
						return MOD_LALT;
					}
					default:
						return ModifierLRMaskFromVK(vk);
				}
			}

			return ModifierLRMaskFromVK(KeyCodes.MapScToVk(sc));
		}

		internal override bool EarlyCollectInput(ulong extraInfo, uint rawSC, uint vk, uint sc, bool keyUp, bool isIgnored
										, CollectInputState state, KeyHistoryItem keyHistoryCurr, object eventInfo)
		// Returns true if the caller should treat the key as visible (non-suppressed).
		// Always use the parameter aVK rather than event.vkCode because the caller or caller's caller
		// might have adjusted aVK, such as to make it a left/right specific modifier key rather than a
		// neutral one. On the other hand, event.scanCode is the one we need for ToUnicodeEx() calls.
		{
			state.earlyCollected = true;
			state.used_dead_key_non_destructively = false;
			state.charCount = 0;

			if (keyUp && !CollectKeyUp(extraInfo, vk, sc, true, eventInfo))
				return false;

			// The checks above suppress key-up if key-down was suppressed and the Input is still active.
			// Otherwise, avoid suppressing key-up since it may result in the key getting stuck down.
			// At the very least, this is needed for cases where a user presses a #z hotkey, for example,
			// to initiate an Input.  When the user releases the LWIN/RWIN key during the input, that
			// up-event should not be suppressed otherwise the modifier key would get "stuck down".
			if (keyUp)
				return true;

			var transcribeKey = true;

			// Don't unconditionally transcribe modified keys such as Ctrl-C because calling ToAsciiEx() on
			// some such keys (e.g. Ctrl-LeftArrow or RightArrow if I recall correctly), disrupts the native
			// function of those keys.  That is the reason for the existence of the
			// g_input.TranscribeModifiedKeys option.
			// Fix for v1.0.38: Below now uses kbdMsSender.modifiersLR_logical vs. g_modifiersLR_physical because
			// it's the logical state that determines what will actually be produced on the screen and
			// by ToAsciiEx() below.  This fixes the Input command to properly capture simulated
			// keystrokes even when they were sent via hotkey such #c or a hotstring for which the user
			// might still be holding down a modifier, such as :*:<t>::Test (if '>' requires shift key).
			// It might also fix other issues.
			if ((kbdMsSender.modifiersLRLogical & ~(MOD_LSHIFT | MOD_RSHIFT)) != 0 // At least one non-Shift modifier is down (Shift may also be down).
					&& !((kbdMsSender.modifiersLRLogical & (MOD_LALT | MOD_RALT)) != 0 && (kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) != 0))
			{
				// Since in some keybd layouts, AltGr (Ctrl+Alt) will produce valid characters (such as the @ symbol,
				// which is Ctrl+Alt+Q in the German/IBM layout and Ctrl+Alt+2 in the Spanish layout), an attempt
				// will now be made to transcribe all of the following modifier combinations:
				// - Anything with no modifiers at all.
				// - Anything that uses ONLY the shift key.
				// - Anything with Ctrl+Alt together in it, including Ctrl+Alt+Shift, etc. -- but don't do
				//   "anything containing the Alt key" because that causes weird side-effects with
				//   Alt+LeftArrow/RightArrow and maybe other keys too).
				// Older comment: If any modifiers except SHIFT are physically down, don't transcribe the key since
				// most users wouldn't want that.  An additional benefit of this policy is that registered hotkeys will
				// normally be excluded from the input (except those rare ones that have only SHIFT as a modifier).
				// Note that ToAsciiEx() will translate ^i to a tab character, !i to plain i, and many other modified
				// letters as just the plain letter key, which we don't want.
				for (var input = script.input; ; input = input.prev)
				{
					if (input == null) // No inputs left, and none were found that meet the conditions below.
					{
						transcribeKey = false;
						break;
					}

					// Transcription is done only once for all layers, so do this if any layer requests it:
					if (input.transcribeModifiedKeys && input.InProgress() && input.IsInteresting(extraInfo))
						break;
				}
			}

			// v1.1.28.00: active_window is set to the focused control, if any, so that the hotstring buffer is reset
			// when the focus changes between controls, not just between windows.
			// v1.1.28.01: active_window is left as the active window; the above is not done because it disrupts
			// hotstrings when the first keypress causes a change in focus, such as to enter editing mode in Excel.
			// See Get_active_window_keybd_layout macro definition for related comments.
#if LINUX
			var activeWindow = script.WinEventManager.ForegroundWindowHandle;
#elif OSX
			// On macOS, resolving the focused window per keystroke is expensive (Accessibility
			// round-trips, sometimes a full CGWindowList snapshot) and would touch AppKit from this
			// background hook thread. For hotstring buffer reset we only need an identity that changes
			// when the typing context changes, so use the frontmost application instead - its PID is
			// tracked event-driven and cached, making this read effectively free. Tradeoff: switching
			// between two windows of the same app won't reset the buffer.
			var activeWindow = Keysharp.Internals.Window.MacOS.MacNativeWindows.ForegroundAppHandle;
#endif
			var activeWindowKeybdLayout = GetKeyboardLayout();
			state.activeWindow = activeWindow;
			state.keyboardLayout = activeWindowKeybdLayout;

			// SUMMARY OF DEAD KEY ISSUE:
			// Calling ToUnicodeEx() with conventional parameters disrupts the entry of dead keys in two different ways:
			//  1) Passing a dead key buffers it within the keyboard layout's internal state.
			//  2) Passing a live key removes any pending dead key from the keyboard layout's internal state.
			// In either case, the state is then incorrect for the active window's own call to ToUnicodeEx(), so it ends
			// up with something like "e" or "''e" instead of "e acute".  Originally this was solved by reinserting the pending
			// dead key (first by re-sending the dead key, then later by calling ToUnicodeEx()), but now we use a special
			// combination of parameters to avoid changing the state where possible.

			int charCount;
			Span<char> ch = state.ch = new char[8];

			if (vk == VK_PACKET)
			{
				// VK_PACKET corresponds to a SendInput event with the KEYEVENTF_UNICODE flag.
				charCount = 1;// SendInput only supports a single 16-bit character code.
				ch[0] = (char)rawSC; // No translation needed.
			}
			else if (transcribeKey && vk != VK_MENU)
			{
				// Unlike Windows, the Unix/macOS hooks read raw key events and translate them with our
				// own keyboard-layout mapping, so there is no shared OS keyboard buffer to avoid disturbing.
				// Dead-key composition state is therefore owned entirely by the layout provider (see
				// ToUnicodeWithDeadKeys), making the Windows replay/flush/no-modify dance unnecessary: the
				// provider buffers a dead key and composes it with the next key regardless of whether the
				// dead key was suppressed from the active window.
				var keyState = translationKeyState;
				System.Array.Clear(keyState);
				// Provide the correct logical modifier and CapsLock state for the translation below.
				AdjustKeyState(keyState, kbdMsSender.modifiersLRLogical);
				keyState[VK_CAPITAL] = (byte)(IsKeyToggledOn(VK_CAPITAL) ? 1 : 0);
				ch.Clear();
				charCount = ToUnicodeWithDeadKeys(vk, rawSC, keyState, ch, 0, activeWindowKeybdLayout);

				if (charCount == 0 && (kbdMsSender.modifiersLRLogical & (MOD_LALT | MOD_RALT)) != 0 && (kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) == 0u)
				{
					// Let the Alt state be ignored for Alt / Alt+Shift combinations (matches the M option
					// behavior: e.g. Win+E or Alt+letter still transcribes to the base letter).
					keyState[VK_MENU] = 0;
					keyState[VK_LMENU] = 0;
					keyState[VK_RMENU] = 0;
					ch.Clear();
					charCount = ToUnicodeWithDeadKeys(vk, rawSC, keyState, ch, 0, activeWindowKeybdLayout);
				}

				if ((kbdMsSender.modifiersLRLogical & (MOD_LCONTROL | MOD_RCONTROL)) == 0) // i.e. must not replace '\r' with '\n' if it is the result of Ctrl+M.
				{
					if (ch.Length > 0)
						if (ch[0] == '\r')  // Translate \r to \n since \n is more typical and useful in Windows.
							ch[0] = '\n';

					if (ch.Length > 1)
						if (ch[1] == '\r')  // But it's never referred to if byte_count < 2
							ch[1] = '\n';
				}
			}
			else
			{
				charCount = 0;
			}

			// Simulated Enter may not always produce text via ToUnicode, but hotstring
			// matching expects an end-character for Enter.
			if (charCount == 0 && vk == VK_RETURN)
			{
				charCount = 1;
				ch[0] = '\n';
			}

			// If Backspace is pressed after a dead key, ch[0] is the "dead" char and ch[1] is '\b'.
			if (vk == VK_BACK && charCount > 0)
				charCount--;// Remove '\b' to simplify the backspacing and collection stages.

			state.charCount = charCount;

			if (!CollectInputHook(extraInfo, vk, sc, ch, charCount, true, eventInfo))
				return false; // Suppress.

			return true;//Visible.
		}

		internal override uint CharToVKAndModifiers(char ch, ref uint? modifiersLr, KeybdLayoutRef layout, bool enableAZFallback = true)
		{
			// A linefeed is a plain Enter, as a carriage return is. Mapping it through the layout instead
			// would either fail or land on Ctrl+Enter, since no key types U+000A on its own.
			if (ch == '\n')
				return VK_RETURN;

			// Delegate to the Unix char mapper used by the sender; add Shift/AltGr if needed. The layout
			// group is snapshotted once per send in the carrier, so every char reuses it (no per-char query).
			if (Rune.TryCreate(ch, out var rune)
				&& KeyCodes.TryMapRuneToKeystroke(rune, layout?.Value, out var vk, out var needShift, out var needAltGr))
			{
				uint mods = modifiersLr ?? 0;
				if (needShift) mods |= MOD_LSHIFT;
				if (needAltGr) mods |= MOD_RALT;
				modifiersLr = mods;
				return vk;
			}
			// The caller enables this only for shortcut notation when sending ordinary text.
			if (enableAZFallback && (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
			{
				if (ch is >= 'A' and <= 'Z')
					modifiersLr = (modifiersLr ?? 0) | MOD_LSHIFT;
				return (uint)char.ToUpperInvariant(ch);
			}
			return 0;
		}

	}
}
#endif
