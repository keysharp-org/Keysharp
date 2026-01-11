using static Keysharp.Core.Common.Keyboard.KeyboardUtils;
using static Keysharp.Core.Common.Keyboard.VirtualKeys;

namespace Keysharp.Core.Common.Keyboard
{
	public class HotkeyDefinition
	{
		internal const uint AT_LEAST_ONE_COMBO_HAS_TILDE = 0x10;
		internal const uint AT_LEAST_ONE_VARIANT_HAS_TILDE = 0x02;
		internal const uint AT_LEAST_ONE_VARIANT_LACKS_TILDE = 0x04;
		internal const uint HOTKEY_ID_ALT_TAB = 0x7FFE;
		internal const uint HOTKEY_ID_ALT_TAB_AND_MENU = 0x7FFB;
		internal const uint HOTKEY_ID_ALT_TAB_MENU = 0x7FFC;
		internal const uint HOTKEY_ID_ALT_TAB_MENU_DISMISS = 0x7FFA;
		internal const uint HOTKEY_ID_ALT_TAB_SHIFT = 0x7FFD;
		internal const uint HOTKEY_ID_INVALID = HOTKEY_ID_MASK;
		internal const int HOTKEY_ID_MASK = 0x7FFF;
		internal const uint HOTKEY_ID_MAX = 0x7FF9;
		internal const uint HOTKEY_ID_OFF = 0x02;
		internal const uint HOTKEY_ID_ON = 0x01;
		internal const uint HOTKEY_ID_TOGGLE = 0x03;
		internal const uint HOTKEY_KEY_UP = 0x8000;
		internal const uint NO_SUPPRESS_NEXT_UP_EVENT = 0x08;
		internal const uint NO_SUPPRESS_PREFIX = 0x01;
		internal const uint NO_SUPPRESS_STATES = NO_SUPPRESS_NEXT_UP_EVENT;
		internal const uint NO_SUPPRESS_SUFFIX_VARIES = AT_LEAST_ONE_VARIANT_HAS_TILDE | AT_LEAST_ONE_VARIANT_LACKS_TILDE;
		internal static string COMPOSITE_DELIMITER = " & ";
		internal bool allowExtraModifiers = false;
		internal bool constructedOK;
		internal HotkeyVariant firstVariant, lastVariant;
		internal uint hookAction;
		internal uint id = HOTKEY_ID_INVALID;
		internal bool isRegistered;
		internal bool keybdHookMandatory;
		internal bool keyUp;
		internal uint modifiers = 0u;
		internal uint modifierSC;
		internal uint modifiersConsolidatedLR;
		internal uint modifiersLR;
		internal uint modifierVK;
		internal uint nextHotkey;
		internal uint noSuppress;
		internal bool parentEnabled = true;
		internal uint sc;
		internal HotkeyTypeEnum type = HotkeyTypeEnum.Normal;
		internal uint vk;
		internal bool vkWasSpecifiedByNumber;

		internal bool Enabled { get; set; }
		internal Options EnabledOptions { get; }
		internal Keys Extra { get; }
		internal Keys Keys { get; }
		internal string Name { get; set; }
		internal IFuncObj Precondition { get; set; }
		internal IFuncObj Proc { get; set; }
		internal string Typed { get; set; }

		internal HotkeyDefinition(Keys keys, Keys extra, Options options, IFuncObj proc)
		{
			Keys = keys;
			Extra = extra;
			EnabledOptions = options;
			Proc = proc;
			Enabled = true;
		}

		internal HotkeyDefinition(uint _id, IFuncObj callback, uint _hookAction, string _name, uint _noSuppress)
		{
			hookAction = _hookAction;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.

			if (TextInterpret(_name, this) == ResultType.Fail) // The called function will display the error.
				return;

			if (type == HotkeyTypeEnum.Joystick)
			{
				if (modifiers != 0 || modifierVK != 0 || modifierSC != 0)
				{
					_ = Errors.ValueErrorOccurred("Invalid hotkey.", _name);
					return;
				}
			}
			else // Perform modifier adjustment and other activities that don't apply to joysticks.
			{
				// Remove any modifiers that are obviously redundant from keys (even NORMAL/registered ones
				// due to cases where RegisterHotkey() fails and the key is then auto-enabled via the hook).
				// No attempt is currently made to correct a silly hotkey such as "lwin & lwin".  In addition,
				// weird hotkeys such as <^Control and ^LControl are not currently validated and might yield
				// unpredictable results.
				bool? is_neutral = false;
				uint modifiers_lr;

				if ((modifiers_lr = ht.KeyToModifiersLR(vk, sc, ref is_neutral)) != 0)
				{
					// This hotkey's action-key is itself a modifier, so ensure that it's not defined
					// to modify itself.  Other sections might rely on us doing this:
					if (is_neutral.Value)
						// Since the action-key is a neutral modifier (not left or right specific),
						// turn off any neutral modifiers that may be on:
						modifiers &= ~ConvertModifiersLR(modifiers_lr);
					else
						modifiersLR &= ~modifiers_lr;
				}

				if ((hookAction == HOTKEY_ID_ALT_TAB || hookAction == HOTKEY_ID_ALT_TAB_SHIFT)
						&& modifierVK == 0 && modifierSC == 0)
				{
					if (modifiers != 0)
					{
						// Neutral modifier has been specified.  Future enhancement: improve this
						// to try to guess which key, left or right, should be used based on the
						// location of the suffix key on the keyboard.  Lexikos: Better not do that
						// since a wrong guess will leave the user wondering why it doesn't work.
						_ = Errors.ValueErrorOccurred("This AltTab hotkey must specify which key (L or R).", _name);
						return;
					}

					if (modifiersLR != 0)
					{
						// If mModifiersLR contains only a single modifier key, that is valid
						// so we convert it here to its corresponding mModifierVK for use by
						// the hook.

						modifierVK = modifiersLR switch
					{
							MOD_LCONTROL => VK_LCONTROL,
							MOD_RCONTROL => VK_RCONTROL,
							MOD_LSHIFT => VK_LSHIFT,
							MOD_RSHIFT => VK_RSHIFT,
							MOD_LALT => VK_LMENU,
							MOD_RALT => VK_RMENU,
							MOD_LWIN => VK_LWIN,
							MOD_RWIN => VK_RWIN,
							_ => (uint)Errors.ValueErrorOccurred("This AltTab hotkey must have exactly one modifier/prefix.", _name, 0u)
						};

						// Since above didn't return:
						modifiersLR = 0;  // Since ModifierVK/SC is now its substitute.
					}

					// Update: This is no longer needed because the hook attempts to compensate.
					// However, leaving it enabled may improve performance and reliability.
					// Update#2: No, it needs to be disabled, otherwise alt-tab won't work right
					// in the rare case where an ALT key itself is defined as "AltTabMenu":
					//else
					// It has no ModifierVK/SC and no modifiers, so it's a hotkey that is defined
					// to fire only when the Alt-Tab menu is visible.  Since the ALT key must be
					// down for that menu to be visible (on all OSes?), add the ALT key to this
					// keys modifiers so that it will be detected as a hotkey whenever the
					// Alt-Tab menu is visible:
					//  modifiers |= MOD_ALT;
				}

				if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(type)) // Added in v1.0.39 to make a hotkey such as "LButton & LCtrl" install the mouse hook.
				{
					switch (vk)
					{
						case 0: // Scan codes having no available virtual key must always be handled by the hook.

						// In addition, to support preventing the toggleable keys from toggling, handle those
						// with the hook also.
						case VK_NUMLOCK:
						case VK_CAPITAL:
						case VK_SCROLL:

						// When the AppsKey used as a suffix, always use the hook to handle it because registering
						// such keys with RegisterHotkey() will fail to suppress(hide) the key-up events from the system,
						// and the key-up for Apps key, at least in apps like Explorer, is a special event that results in
						// the context menu appearing (though most other apps seem to use the key-down event rather than
						// the key-up, so they would probably work okay).  Note: Of possible future use is the fact that
						// if the Alt key is held down before pressing Appskey, it's native function does
						// not occur.  This may be similar to the fact that LWIN and RWIN don't cause the
						// start menu to appear if a shift key is held down.
						case VK_APPS:

						// Finally, the non-neutral (left-right) modifier keys (except LWin and RWin) must also
						// be done with the hook because even if RegisterHotkey() claims to succeed on them,
						// I'm 99% sure I tried it and the hotkeys don't actually work with that method:
						case VK_LCONTROL:
						case VK_RCONTROL:
						case VK_LSHIFT:
						case VK_RSHIFT:
						case VK_LMENU:
						case VK_RMENU:
							keybdHookMandatory = true;
							break;

						// To prevent the Start Menu from appearing for a naked LWIN or RWIN, must
						// handle this key with the hook (the presence of a normal modifier makes
						// this unnecessary, at least under WinXP, because the Start Menu is
						// never invoked when a modifier key is held down with lwin/rwin).
						case VK_LWIN:
						case VK_RWIN:

						// If this hotkey is an unmodified modifier (e.g. Control::) and there
						// are any other hotkeys that rely specifically on this modifier,
						// have the hook handle this hotkey so that it will only fire on key-up
						// rather than key-down.  Note: cases where this key's modifiersLR or
						// ModifierVK/SC are non-zero -- as well as hotkeys that use sc vs. vk
						// -- have already been set to use the keybd hook, so don't need to be
						// handled here.  UPDATE: All the following cases have been already set
						// to be HK_KEYBD_HOOK:
						// - left/right ctrl/alt/shift (since RegisterHotkey() doesn't support them).
						// - Any key with a ModifierVK/SC
						// - The naked lwin or rwin key (due to the check above)
						// Therefore, the only case left to be detected by this next line is the
						// one in which the user configures the naked neutral key VK_SHIFT,
						// VK_MENU, or VK_CONTROL.  As a safety precaution, always handle those
						// neutral keys with the hook so that their action will only fire
						// when the key is released (thus allowing each key to be used for its
						// normal modifying function):
						case VK_CONTROL:
						case VK_MENU:
						case VK_SHIFT:
							if (modifiers == 0 && modifiersLR == 0) // Modifier key as suffix and has no modifiers (or only a ModifierVK/SC).
								keybdHookMandatory = true;

							//else keys modified by CTRL/SHIFT/ALT/WIN can always be registered normally because these
							// modifiers are never used (are overridden) when that key is used as a ModifierVK
							// for another key.  Example: if key1 is a ModifierVK for another key, ^key1
							// (or any other modified versions of key1) can be registered as a hotkey because
							// that doesn't affect the hook's ability to use key1 as a prefix:
							break;
					}
				} // if HK_TYPE_CAN_BECOME_KEYBD_HOOK(mType)

				if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(type))
					if ((modifiersLR != 0 || hookAction != 0 || keyUp || modifierVK != 0 || modifierSC != 0) // mSC is handled higher above.
							|| (script.ForceKeybdHook || allowExtraModifiers // mNoSuppress&NO_SUPPRESS_PREFIX has already been handled elsewhere. Other bits in mNoSuppress must be checked later because they can change by any variants added after *this* one.
								|| (vk != 0 && !vkWasSpecifiedByNumber && ht.MapVkToSc(vk, true) != 0))) // Its mVK corresponds to two scan codes (such as "ENTER").
						keybdHookMandatory = true;

				// v1.0.38.02: The check of mVK_WasSpecifiedByNumber above was added so that an explicit VK hotkey such
				// as "VK24::" (which is VK_HOME) can be handled via RegisterHotkey() vs. the hook.  Someone asked for
				// this ability, but even if it weren't for that it seems more correct to recognize an explicitly-specified
				// VK as a "neutral VK" (i.e. one that fires for both scan codes if the VK has two scan codes). The user
				// can always specify "SCnnn::" as a hotkey to avoid this fire-on-both-scan-codes behavior.
				is_neutral = null;

				// Currently, these take precedence over each other in the following order, so don't
				// just bitwise-or them together in case there's any ineffectual stuff stored in
				// the fields that have no effect (e.g. modifiers have no effect if there's a mModifierVK):
				if (modifierVK != 0)
					modifiersConsolidatedLR = ht.KeyToModifiersLR(modifierVK, 0, ref is_neutral);
				else if (modifierSC != 0)
					modifiersConsolidatedLR = ht.KeyToModifiersLR(0, modifierSC, ref is_neutral);
				else
				{
					modifiersConsolidatedLR = modifiersLR;

					if (modifiers != 0)
						modifiersConsolidatedLR |= ConvertModifiers(modifiers);
				}
			} // if (mType != HK_JOYSTICK)

			// If mKeybdHookMandatory==true, ManifestAllHotkeysHotstringsHooks() will set mType to HK_KEYBD_HOOK for us.
			Name = _name;
			_ = AddVariant(callback, _noSuppress);
			// Above has ensured that both mFirstVariant and mLastVariant are non-NULL, so callers can rely on that.
			// Always assign the ID last, right before a successful return, so that the caller is notified
			// that the constructor succeeded:
			constructedOK = true;
			id = _id;
			// Don't do this because the caller still needs the old/unincremented value:
			//++sHotkeyCount;  // Hmm, seems best to do this here, but revisit this sometime.
		}

		~HotkeyDefinition()
		{
			_ = Unregister();
		}

		public static HotkeyDefinition AddHotkey(IFuncObj _callback, uint _hookAction, string _name)
		{
			var b = 0u;
			return AddHotkey(_callback, _hookAction, _name, ref b);
		}

		/// <summary>
		/// This function examines all hotkeys and hotstrings to determine:
		/// - Which hotkeys to register/unregister, or activate/deactivate in the hook.
		/// - Which hotkeys to be changed from HK_NORMAL to HK_KEYBD_HOOK (or vice versa).
		/// - In pursuit of the above, also assess the interdependencies between hotkeys: the presence or
		///   absence of a given hotkey can sometimes impact whether other hotkeys need to be converted from
		///   HK_NORMAL to HK_KEYBD_HOOK.  For example, a newly added/enabled global hotkey variant can
		///   cause a HK_KEYBD_HOOK hotkey to become HK_NORMAL, and the converse is also true.
		/// - Based on the above, decide whether the keyboard and/or mouse hooks need to be (de)activated.
		/// Needs to be public so tests can use it.
		/// </summary>
		public static object ManifestAllHotkeysHotstringsHooks()
		{
			// v1.0.37.05: A prefix key such as "a" in "a & b" should cause any use of "a" as a suffix
			// (such as ^!a) also to be a hook hotkey.  Otherwise, the ^!a hotkey won't fire because the
			// hook prevents the OS's hotkey monitor from seeing that the hotkey was pressed.  NOTE:
			// This is done only for virtual keys because prefix keys that are done by scan code (mModifierSC)
			// should already be hook hotkeys when used as suffix keys (there may be a few unusual exceptions,
			// but they seem too rare to justify the extra code size).
			// Update for v1.0.40: This first pass through the hotkeys now also checks things for hotkeys
			// that can affect other hotkeys. If this weren't done in the first pass, it might be possible
			// for a hotkey to make some other hotkey into a hook hotkey, but then the hook might not be
			// installed if that hotkey had already been processed earlier in the second pass.  Similarly,
			// a hotkey processed earlier in the second pass might have been registered when in fact it
			// should have become a hook hotkey due to something learned only later in the second pass.
			// Doing these types of things in the first pass resolves such situations.
			// Update for v1.1.27: Doing the above in the first pass doesn't work correctly, as mType is
			// reset to default during the first pass (even if a previous iteration might has set it to
			// HK_KEYBD_HOOK, such as when it is eclipsed by a wildcard hotkey).  One workaround would
			// be to set mKeybdHookMandatory = true, but that would prevent the hotkey from reverting to
			// HK_NORMAL when it no longer needs the hook.  Instead, there are now three passes.
			var vkIsPrefix = new bool[HookThread.VK_ARRAY_COUNT];
			var script = Script.TheScript;
			var hkd = script.HotkeyData;
			var kbd = script.KeyboardData;
			var shk = hkd.shk;
			var hkIsInactive = new bool[shk.Count];// No init needed.  Currently limited to around 16k (HOTKEY_ID_MAX).
			HotkeyVariant vp;
			int i, j;
			var ht = script.HookThread;

			// FIRST PASS THROUGH THE HOTKEYS:
			for (i = 0; i < shk.Count; ++i)
			{
				var hot = shk[i];

				if (hkIsInactive[i] = (A_IsSuspended && !hot.IsExemptFromSuspend())
									  || hot.IsCompletelyDisabled()) // Listed last for short-circuit performance.
				{
					// In the cases above, nothing later below can change the fact that this hotkey should
					// now be in an unregistered state.
					if (hot.isRegistered)
					{
						_ = hot.Unregister();

						// In case the hotkey's thread is already running, it seems best to cancel any repeat-run
						// that has already been scheduled.  Older comment: CT_SUSPEND, at least, relies on us to do this.
						for (vp = hot.firstVariant; vp != null; vp = vp.nextVariant)
							vp.runAgainAfterFinished = false; // Applies to all hotkey types, not just registered ones.
					}

					continue;
				}

				// Otherwise, this hotkey will be in effect, so check its attributes.

				if (hot.keybdHookMandatory)
				{
					// v1.0.44: The following is relied upon by some things like the Hotkey constructor and the tilde prefix
					// (the latter can set mKeybdHookMandatory for a hotkey sometime after the first variant is added [such
					// as for a subsequent variant]).  This practice also improves maintainability.
					if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(hot.type)) // To ensure it hasn't since become a joystick/mouse/mouse-and-keyboard hotkey.
						hot.type = HotkeyTypeEnum.KeyboardHook;
				}
				else // Hook isn't mandatory, so set any non-mouse/joystick/both hotkey to normal for possibly overriding later below.
				{
					// v1.0.42: The following is done to support situations in which a hotkey can be a hook hotkey sometimes,
					// but after a (de)suspend or after a change to other hotkeys via the Hotkey command, might no longer
					// require the hook.  Examples include:
					// 1) A hotkey can't be registered because some other app is using it, but later
					//    that condition changes.
					// 2) Suspend or the Hotkey command changes wildcard hotkeys so that non-wildcard
					//    hotkeys that have the same suffix are no longer eclipsed, and thus don't need
					//    the hook.  The same type of thing can happen if a key-up hotkey is disabled,
					//    which would allow it's key-down hotkey to become non-hook.  Similarly, if a
					//    all of a prefix key's hotkeys become disabled, and that prefix is also a suffix,
					//    those suffixes no longer need to be hook hotkeys.
					// 3) There may be other ways, especially in the future involving #HotIf WinActive/Exist
					//    keys whose criteria change.
					if (hot.type == HotkeyTypeEnum.KeyboardHook)
						hot.type = HotkeyTypeEnum.Normal; // To possibly be overridden back to HK_KEYBD_HOOK later below; but if not, it will be registered later below.
				}

				if (hot.modifierVK != 0)
					vkIsPrefix[hot.modifierVK] = true;
			} // End of first pass loop.

			// SECOND PASS THROUGH THE HOTKEYS:
			// Check for hotkeys that can affect other hotkeys, such as wildcard or key-up hotkeys.
			// This is separate to the other passes for reasons described at the top of the function.
			for (i = 0; i < shk.Count; ++i)
			{
				if (hkIsInactive[i])
					continue;

				var hot = shk[i];

				if (hot.keyUp && hot.vk != 0) // No need to do the below for mSC hotkeys since their down hotkeys would already be handled by the hook.
				{
					// For each key-up hotkey, search for any its counterpart that's a down-hotkey (if any).
					// Such a hotkey should also be handled by the hook because if registered, such as
					// "#5" (reg) and "#5 up" (hook), the hook would suppress the down event because it
					// is unaware that down-hotkey exists (it's suppressed to prevent the key from being
					// stuck in a logically down state).
					for (j = 0; j < shk.Count; ++j)
					{
						// No need to check the following because they are already hook hotkeys:
						// mModifierVK/SC
						// mAllowExtraModifiers
						// mNoSuppress
						// In addition, there is no need to check shk[j].mKeyUp because that can't be
						// true if it's mType is HK_NORMAL:
						// Also, g_IsSuspended and IsCompletelyDisabled() aren't checked
						// because it's harmless to operate on disabled hotkeys in this way.
						if (shk[j].vk == hot.vk && HK_TYPE_CAN_BECOME_KEYBD_HOOK(shk[j].type) // Ordered for short-circuit performance.
								&& shk[j].modifiersConsolidatedLR == hot.modifiersConsolidatedLR)
						{
							shk[j].type = HotkeyTypeEnum.KeyboardHook;
							// And if it's currently registered, it will be unregistered later below.
						}
					}
				}

				// v1.0.40: If this is a wildcard hotkey, any hotkeys it eclipses (i.e. includes as subsets)
				// should be made into hook hotkeys also, because otherwise they would be overridden by hook.
				// The following criteria are checked:
				// 1) Exclude those that have a ModifierSC/VK because in those cases, mAllowExtraModifiers is
				//    ignored.
				// 2) Exclude those that lack an mVK because those with mSC can't eclipse registered hotkeys
				//   (since any would-be eclipsed mSC hotkey is already a hook hotkey due to is SC nature).
				// 3) It must not have any mModifiersLR because such hotkeys can't completely eclipse
				//    registered hotkeys since they always have neutral vs. left/right-specific modifiers.
				//    For example, if *<^a is a hotkey, ^a can still be a registered hotkey because it could
				//    still be activated by pressing RControl+a.
				// 4) For maintainability, it doesn't check mNoSuppress because the hook is needed anyway,
				//    so might as well handle eclipsed hotkeys with it too.
				if (hot.allowExtraModifiers && hot.vk != 0 && hot.modifiersLR == 0 && !(hot.modifierSC != 0 || hot.modifierVK != 0))
				{
					for (j = 0; j < shk.Count; ++j)
					{
						// If it's not of type HK_NORMAL, there's no need to change its type regardless
						// of the values of its other members.  Also, if the wildcard hotkey (hot) has
						// any neutral modifiers, this hotkey must have at least those neutral modifiers
						// too or else it's not eclipsed (and thus registering it is okay).  In other words,
						// the wildcard hotkey's neutral modifiers must be a perfect subset of this hotkey's
						// modifiers for this one to be eclipsed by it. Note: Neither mModifiersLR nor
						// mModifiersConsolidated is checked for simplicity and also because it seems to add
						// flexibility.  For example, *<^>^a would require both left AND right ctrl to be down,
						// not EITHER. In other words, mModifiersLR can never in effect contain a neutral modifier.
						if (shk[j].vk == hot.vk && HK_TYPE_CAN_BECOME_KEYBD_HOOK(shk[j].type) // Ordered for short-circuit performance.
								&& (hot.modifiers & shk[j].modifiers) == hot.modifiers)
						{
							// Note: No need to check mModifiersLR because it would already be a hook hotkey in that case;
							// that is, the check of shk[j].mType precludes it.  It also precludes the possibility
							// of shk[j] being a key-up hotkey, wildcard hotkey, etc.
							shk[j].type = HotkeyTypeEnum.KeyboardHook;
							// And if it's currently registered, it will be unregistered later below.
						}
					}
				}
			} // End of second pass loop.

#if !WINDOWS
			// On Linux we always rely on the hooks (XGrabKey lacks callbacks), so force hook handling
			// for all active hotkeys rather than attempting RegisterHotkey-style optimization.
			hkd.whichHookNeeded = 0;
			for (i = 0; i < shk.Count; ++i)
			{
				if (hkIsInactive[i])
					continue;

				var hot = shk[i];
				if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(hot.type))
					hot.type = HotkeyTypeEnum.KeyboardHook;

				switch (hot.type)
				{
					case HotkeyTypeEnum.KeyboardHook: hkd.whichHookNeeded |= HookType.Keyboard; break;
					case HotkeyTypeEnum.MouseHook: hkd.whichHookNeeded |= HookType.Mouse; break;
					case HotkeyTypeEnum.BothHook: hkd.whichHookNeeded |= HookType.Keyboard | HookType.Mouse; break;
				}
			}

#else

			// THIRD PASS THROUGH THE HOTKEYS:
			// v1.0.42: Reset sWhichHookNeeded because it's now possible that the hook was on before but no longer
			// needed due to changing of a hotkey from hook to registered (for various reasons described above):
			hkd.whichHookNeeded = 0;

			for (i = 0; i < shk.Count; ++i)
			{
				if (hkIsInactive[i])
					continue; // v1.0.40: Treat disabled hotkeys as though they're not even present.

				var hot = shk[i];

				// HK_MOUSE_HOOK hotkeys, and most HK_KEYBD_HOOK hotkeys, are handled by the hotkey constructor.
				// What we do here upgrade any NORMAL/registered hotkey to HK_KEYBD_HOOK if there are other
				// hotkeys that interact or overlap with it in such a way that the hook is preferred.
				// This evaluation is done here because only now that hotkeys are about to be activated do
				// we know which ones are disabled or suspended, and thus don't need to be taken into account.
				if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(hot.type))
				{
					if (vkIsPrefix[hot.vk])
						// If it's a suffix that is also used as a prefix, use hook (this allows ^!a to work without $ when "a & b" is a hotkey).
						// v1.0.42: This was fixed so that mVK_WasSpecifiedByNumber dosn't affect it.  That is, a suffix that's
						// also used as a prefix should become a hook hotkey even if the suffix is specified as "vkNNN::".
						hot.type = HotkeyTypeEnum.KeyboardHook;
					// And if it's currently registered, it will be unregistered later below.
					else
					{
						// v1.0.42: Any #HotIf keyboard hotkey must use the hook if it lacks an enabled,
						// non-suspended, global variant.  Under those conditions, the hotkey is either:
						// 1) Single-variant hotkey that has criteria (non-global).
						// 2) Multi-variant hotkey but all variants have criteria (non-global).
						// 3) A hotkey with a non-suppressed (~) variant (always, for code simplicity): already handled by AddVariant().
						// In both cases above, the hook must handle the hotkey because there can be
						// situations in which the hook should let the hotkey's keystroke pass through
						// to the active window (i.e. the hook is needed to dynamically disable the hotkey).
						// mHookAction isn't checked here since those hotkeys shouldn't reach this stage (since they're always hook hotkeys).
						for (hot.type = HotkeyTypeEnum.KeyboardHook, vp = hot.firstVariant; vp != null; vp = vp.nextVariant)
						{
							if (vp.hotCriterion == null && vp.enabled // It's a global variant (no criteria) and it's enabled...
									&& (!A_IsSuspended || vp.suspendExempt))
								// ... and this variant isn't suspended (we already know IsCompletelyDisabled()==false from an earlier check).
							{
								hot.type = HotkeyTypeEnum.Normal; // Reset back to how it was before this loop started.  Hook not needed.
								break;
							}
						}

						// If the above promoted it from NORMAL to HOOK but the hotkey is currently registered,
						// it will be unregistered later below.
					}
				}

				// Check if this mouse hotkey also requires the keyboard hook (e.g. #LButton).
				// Some mouse hotkeys, such as those with normal modifiers, don't require it
				// since the mouse hook has logic to handle that situation.  But those that
				// are composite hotkeys such as "RButton & Space" or "Space & RButton" need
				// the keyboard hook:
				if (hot.type == HotkeyTypeEnum.MouseHook && (
							hot.modifierSC != 0 || hot.sc != 0 // i.e. since it's an SC, the modifying key isn't a mouse button.
							|| hot.hookAction != 0 // v1.0.25.05: At least some alt-tab actions require the keyboard hook. For example, a script consisting only of "MButton::AltTabAndMenu" would not work properly otherwise.
							// v1.0.25.05: The line below was added to prevent the Start Menu from appearing, which
							// requires the keyboard hook. ALT hotkeys don't need it because the mouse hook sends
							// a CTRL keystroke to disguise them, a trick that is unfortunately not reliable for
							// when it happens while the while key is down (though it does disguise a Win-up).
							|| ((hot.modifiersConsolidatedLR & (MOD_LWIN | MOD_RWIN)) != 0 && (hot.modifiersConsolidatedLR & (MOD_LALT | MOD_RALT)) == 0)
							// For v1.0.30, above has been expanded to include Win+Shift and Win+Control modifiers.
							|| (hot.vk != 0 && !MouseUtils.IsMouseVK(hot.vk)) // e.g. "RButton & Space"
							|| (hot.modifierVK != 0 && !MouseUtils.IsMouseVK(hot.modifierVK)))) // e.g. "Space & RButton"
					hot.type = HotkeyTypeEnum.BothHook;  // Needed by ChangeHookState().

				// For the above, the following types of mouse hotkeys do not need the keyboard hook:
				// 1) mAllowExtraModifiers: Already handled since the mouse hook fetches the modifier state
				//    manually when the keyboard hook isn't installed.
				// 2) mModifiersConsolidatedLR (i.e. the mouse button is modified by a normal modifier
				//    such as CTRL): Same reason as #1.
				// 3) As a subset of #2, mouse hotkeys that use WIN as a modifier will not have the
				//    Start Menu suppressed unless the keyboard hook is installed.  It's debatable,
				//    but that seems a small price to pay (esp. given how rare it is just to have
				//    the mouse hook with no keyboard hook) to avoid the overhead of the keyboard hook.
				// If the hotkey is normal, try to register it.  If the register fails, use the hook to try
				// to override any other script or program that might have it registered (as documented):
				if (hot.type == HotkeyTypeEnum.Normal)
				{
					if (hot.Register() == ResultType.Fail) // Can't register it, usually due to some other application or the OS using it.
						hot.type = HotkeyTypeEnum.KeyboardHook;
				}
				else // mType isn't NORMAL (possibly due to something above changing it), so ensure it isn't registered.
				{
					if (hot.isRegistered) // Improves typical performance since this hotkey could be mouse, joystick, etc.
						// Although the hook effectively overrides registered hotkeys, they should be unregistered anyway
						// to prevent the Send command from triggering the hotkey, and perhaps other side-effects.
						_ = hot.Unregister();
				}

				switch (hot.type)
				{
					case HotkeyTypeEnum.KeyboardHook: hkd.whichHookNeeded |= HookType.Keyboard; break;

					case HotkeyTypeEnum.MouseHook: hkd.whichHookNeeded |= HookType.Mouse; break;

					case HotkeyTypeEnum.BothHook: hkd.whichHookNeeded |= HookType.Keyboard | HookType.Mouse; break;
				}
			} // for()

#endif
			// Check if anything else requires the hook.
			// But do this part outside of the above block because these values may have changed since
			// this function was first called.  By design, the Num/Scroll/CapsLock AlwaysOn/Off setting
			// stays in effect even when Suspend in ON.
			var ts = script.KeyboardData.toggleStates;
			var hm = script.HotstringManager;

			if (hm.enabledCount != 0
					|| script.input != null // v1.0.91: Hook is needed for collecting input.
					|| !(ts.forceNumLock == ToggleValueType.Neutral && ts.forceCapsLock == ToggleValueType.Neutral && ts.forceScrollLock == ToggleValueType.Neutral))
				hkd.whichHookNeeded |= HookType.Keyboard;

			if (kbd.blockMouseMove || (hm.hsResetUponMouseClick && hm.enabledCount != 0))
				hkd.whichHookNeeded |= HookType.Mouse;

			//Regardless of the type of hook needed, including none, we always need to start the reader queue.
			//The presence of any hotkey/string should start the reader channel thread because even hotkeys
			//which are received in MainWindow.WndProc() need to be forwarded to it.
			if (shk.Count != 0 || script.HotstringManager.shs.Count != 0)
			{
				ht.Start();
			}

			// Install or deinstall either or both hooks, if necessary, based on these param values.
			ht.ChangeHookState(shk, hkd.whichHookNeeded, hkd.whichHookAlways);

			// Fix for v1.0.34: If the auto-execute section uses the Hotkey command but returns before doing
			// something that calls MsgSleep, the main timer won't have been turned on.  For example:
			// Hotkey, Joy1, MySubroutine
			// ;Sleep 1  ; This was a workaround before this fix.
			// return
			// By putting the following check here rather than in AutoHotkey.cpp, that problem is resolved.
			// In addition...
			if (hkd.joyHotkeyCount != 0)  // Joystick hotkeys require the timer to be always on. (This is most likely unneeded).
				Flow.SetMainTimer();

			return DefaultObject;
		}

		public override string ToString() => Name;

		/// <summary>
		/// aCallback can be NULL if the caller is creating a dynamic hotkey that has an aHookAction.
		/// aName must not be NULL.
		/// Returns the address of the new hotkey on success, or NULL otherwise.
		/// The caller is responsible for calling ManifestAllHotkeysHotstringsHooks(), if appropriate.
		/// </summary>
		internal static HotkeyDefinition AddHotkey(IFuncObj _callback, uint _hookAction, string _name, ref uint _noSuppress)
		{
			HotkeyDefinition hk;
			var hookIsMandatory = false;
			var script = Script.TheScript;

			//We must first check if the hotkey exists before creating a new one because this might just be a variant.
			//The code to check for a variant was in the parsing section of AHK, but we move it here because Keysharp adds them at runtime.
			if ((hk = FindHotkeyByTrueNature(_name, ref _noSuppress, ref hookIsMandatory)) != null) // Parent hotkey found.  Add a child/variant hotkey for it.
			{
				if (_hookAction != 0) // suffix_has_tilde has always been ignored for these types (alt-tab hotkeys).
				{
					// HotkeyDefinition::Dynamic() contains logic and comments similar to this, so maintain them together.
					// An attempt to add an alt-tab variant to an existing hotkey.  This might have
					// merit if the intention is to make it alt-tab now but to later disable that alt-tab
					// aspect via the Hotkey cmd to let the context-sensitive variants shine through
					// (take effect).
					hk.hookAction = _hookAction;
				}
				else
				{
					// Detect duplicate hotkey variants to help spot bugs in scripts.
					if (hk.FindVariant() != null) // See if there's already a variant matching the current criteria (suffix_has_tilde does not make variants distinct form each other because it would require firing two hotkey IDs in response to pressing one hotkey, which currently isn't in the design).
					{
						_ = Errors.ErrorOccurred($"Duplicate hotkey: {_name}");
						return default;
					}

					if (hk.AddVariant(_callback, _noSuppress) == null)
						return null;// ScriptError(ERR_OUTOFMEM, buf);

					if (hookIsMandatory || script.ForceKeybdHook)
					{
						// Require the hook for all variants of this hotkey if any variant requires it.
						// This seems more intuitive than the old behavior, which required $ or #UseHook
						// to be used on the *first* variant, even though it affected all variants.
						hk.keybdHookMandatory = true;
					}
				}

				return hk;
			}
			else
			{
				var shk = script.HotkeyData.shk;
				hk = new HotkeyDefinition((uint)shk.Count, _callback, _hookAction, _name, _noSuppress);

				if (hk.constructedOK)
				{
					shk.Add(hk);
					script.HookThread.hotkeyUp.Add(0);
					return hk;
				}
				else//This was originally in the parsing code, but fits better here.
				{
					var valid = TextInterpret(_name, null); // Passing NULL calls it in validate-only mode._name

					if (valid != ResultType.ConditionTrue)
						return null;// ResultType.Fail; // It already displayed the error.

					// This hotkey uses a single-character key name, which could be valid on some other
					// keyboard layout.  Allow the script to start, but warn the user about the problem.
					// Note that this hotkey's label is still valid even though the hotkey wasn't created.

					if (!script.ValidateThenExit) // Current keyboard layout is not relevant in /validate mode.
						_ = Dialogs.MsgBox($"Note: The hotkey {_name} will not be active because it does not exist in the current keyboard layout.");
				}
			}

			return null;
		}

		internal static void AddHotkeyCriterion(IFuncObj fo) => Script.TheScript.hotCriterions.Add(fo);

		internal static void AddHotkeyIfExpr(IFuncObj fo) => Script.TheScript.hotExprs.Add(fo);

		internal static void AllDestruct()
		{
			var script = Script.TheScript;
			script.HookThread.Unhook();
			var shk = script.HotkeyData.shk;

			foreach (var hk in shk)
				_ = hk.Unregister(); //Hotkeys will unregister as they go out of scope, but force them to do it now.

			shk.Clear();
		}

		internal static uint ConvertAltTab(string aBuf, bool aAllowOnOff)
		{
			if (string.IsNullOrEmpty(aBuf)) return 0;

			if (string.Compare(aBuf, "AltTab", true) == 0) return HOTKEY_ID_ALT_TAB;

			if (string.Compare(aBuf, "ShiftAltTab", true) == 0) return HOTKEY_ID_ALT_TAB_SHIFT;

			if (string.Compare(aBuf, "AltTabMenu", true) == 0) return HOTKEY_ID_ALT_TAB_MENU;

			if (string.Compare(aBuf, "AltTabAndMenu", true) == 0) return HOTKEY_ID_ALT_TAB_AND_MENU;

			if (string.Compare(aBuf, "AltTabMenuDismiss", true) == 0) return HOTKEY_ID_ALT_TAB_MENU_DISMISS;

			if (aAllowOnOff)
			{
				if (string.Compare(aBuf, "On", true) == 0) return HOTKEY_ID_ON;

				if (string.Compare(aBuf, "Off", true) == 0) return HOTKEY_ID_OFF;

				if (string.Compare(aBuf, "Toggle", true) == 0) return HOTKEY_ID_TOGGLE;
			}

			return 0;
		}

		/// <summary>
		/// v1.0.44: Caller has ensured that aFireWithNoSuppress is true if has already been decided and false if undecided.
		/// Upon return, caller can assume that the value in it is now decided rather than undecided.
		/// v1.0.42: Caller must not call this for AltTab hotkeys IDs, but this will always return NULL in such cases.
		/// singleChar is sometimes modified for the caller here (if singleChar isn't NULL).
		/// Caller has ensured that aHotkeyIDwithFlags contains a valid/existing hotkey ID.
		/// Technically, aHotkeyIDwithMask can be with or without the flags in the high bits.
		/// If present, they're removed.
		/// </summary>
		internal static HotkeyVariant CriterionFiringIsCertain(ref uint hotkeyIDwithFlags, bool _keyUp, ulong extraInfo
				, ref bool fireWithNoSuppress, ref char? singleChar)
		{
			// aHookAction isn't checked because this should never be called for alt-tab hotkeys (see other comments above).
			var hotkeyId = hotkeyIDwithFlags & HOTKEY_ID_MASK;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;
			var shk = script.HotkeyData.shk;
			HotkeyVariant vp;

			// The following check is for maintainability, since caller should have already checked and
			// handled HOTKEY_ID_ALT_TAB and similar.  Less-than-zero check not necessary because it's unsigned.
			if (hotkeyId >= shk.Count)
				return null; // Special alt-tab hotkey quasi-ID used by the hook.

			var hk = shk[(int)hotkeyId]; // For convenience and performance.

			if (fireWithNoSuppress // Caller has already determined its value with certainty...
					|| (hk.noSuppress & NO_SUPPRESS_SUFFIX_VARIES) != NO_SUPPRESS_SUFFIX_VARIES) // ...or its value is easy to determine, so do it now (compare to itself since it's a bitwise union).
			{
				// Since aFireWithNoSuppress can now be easily determined for the caller (or was already determined by the caller
				// itself), it's possible to take advantage of the following optimization, which is especially important in cases
				// where TitleMatchMode is "slow":
				// For performance, the following returns without having called WinExist/Active if it sees that one of this
				// hotkey's variant's will certainly fire due to the fact that it has a non-suspended global variant.
				// This reduces the number of situations in which double the number of WinExist/Active() calls are made
				// (once in the hook to determine whether the hotkey keystroke should be passed through to the active window,
				// and again upon receipt of the message for reasons explained there).
				for (vp = hk.firstVariant; vp != null; vp = vp.nextVariant)
					if (vp.hotCriterion == null && vp.enabled && (!A_IsSuspended || vp.suspendExempt)
							&& KeyboardMouseSender.HotInputLevelAllowsFiring(vp.inputLevel, extraInfo, ref singleChar))
					{
						// Fix for v1.0.47.02: The following section (above "return") was moved into this block
						// from above the for() because only when this for() returns is it certain that this
						// hk/hotkey_id is actually the right one, and thus its attributes can be used to determine
						// aFireWithNoSuppress for the caller.
						// Since this hotkey has variants only of one type (tilde or non-tilde), this variant must be of that type.
						if (!fireWithNoSuppress) // Caller hasn't yet determined its value with certainty (currently, this statement might always be true).
							fireWithNoSuppress = (hk.noSuppress & AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0; // Due to other checks, this means all variants are tilde.

						return vp; // Caller knows this isn't necessarily the variant that will fire since !vp.mHotCriterion.
					}
			}

			// Since above didn't return, a slower method is needed to find out which variant of this hotkey (if any)
			// should fire.
			var zero = 0L;

			if ((vp = hk.CriterionAllowsFiring(ref zero, extraInfo, ref singleChar)) != null)
			{
				if (!fireWithNoSuppress) // Caller hasn't yet determined its value with certainty (currently, this statement might always be true).
					fireWithNoSuppress = (vp.noSuppress & AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0;

				return vp; // It found an eligible variant to fire.
			}

			// Since above didn't find any variant of the hotkey than can fire, check for other eligible hotkeys.
			if (hk.hookAction == 0) // Rule out those that aren't susceptible to the bug.
			{
				// Custom combos are no longer ruled out by the above since they allow extra modifiers and
				// are capable of obscuring non-custom combos; e.g. LCtrl & a:: obscures <^a::, ^+a:: and so on.
				// Fix for v1.0.46.13: Although the section higher above found no variant to fire for the
				// caller-specified hotkey ID, it's possible that some other hotkey (one with a wildcard) is
				// eligible to fire due to the eclipsing behavior of wildcard hotkeys.  For example:
				//    #HotIf Not WinActive("Untitled")
				//    q::tooltip ThisHotkey . " Non-notepad"
				//    #HotIf WinActive("Untitled")
				//    *q::tooltip ThisHotkey . " Notepad"
				// However, the logic here might not be a perfect solution because it fires the first available
				// hotkey that has a variant whose criteria are met (which might not be exactly the desired rules
				// of precedence).  However, I think it's extremely rare that there would be more than one hotkey
				// that matches the original hotkey (VK, SC, has-wildcard) etc.  Even in the rare cases that there
				// is more than one, the rarity is compounded by the rarity of the bug even occurring, which probably
				// makes the odds vanishingly small.  That's why the following simple, high-performance loop is used
				// rather than more a more complex one that "locates the smallest (most specific) eclipsed wildcard
				// hotkey", or "the uppermost variant among all eclipsed wildcards that is eligible to fire".
				// UPDATE: This now uses a linked list of hotkeys which share the same suffix key, in the order of
				// sort_most_general_before_least, which might solve the concern about precedence.
				var modifiers = ConvertModifiersLR(kbdMouseSender.modifiersLRLogicalNonIgnored); // Neutral modifiers.

				for (var candidateId = hk.nextHotkey; candidateId != HOTKEY_ID_INVALID;)
				{
					var hk2 = shk[(int)candidateId];
					candidateId = hk2.nextHotkey;

					// Non-wildcard hotkeys are eligible for the workaround in cases like ^+a vs <^+a vs ^<+a, where
					// the neutral modifier acts as a sort of wildcard (it permits left, right or both).  This also
					// increases support for varying names, such as Esc vs. Escape vs. vk1B (which already partially
					// worked if wildcards were used).
					// However, must ensure only the allowed modifiers are down when !mAllowExtraModifiers.
					// mVK and mSC aren't checked since the linked list only includes hotkeys for this same suffix key.
					// This also allows the workaround to be partially applied to LCtrl vs. Ctrl and similar (as suffixes).
					if ((hk2.allowExtraModifiers || (~hk2.modifiersConsolidatedLR & kbdMouseSender.modifiersLRLogicalNonIgnored) == 0)
							&& hk2.keyUp == hk.keyUp // Seems necessary that up/down nature is the same in both.
							&& hk2.modifierVK == 0 // Avoid accidental matching of normal hotkeys with custom-combo "&"
							&& hk2.modifierSC == 0// hotkeys that happen to have the same mVK/SC.
							&& hk2.hookAction == 0// Might be unnecessary to check this; but just in case.
							&& hk2.id != hotkeyId // Don't consider the original hotkey because it was already found ineligible.
							&& (hk2.modifiers & ~modifiers) == 0 // All neutral modifiers required by the candidate are pressed.
							&& (hk2.modifiersLR & ~kbdMouseSender.modifiersLRLogicalNonIgnored) == 0 // All left-right specific modifiers required by the candidate are pressed.
							//&& hk2.mType != HK_JOYSTICK // Seems unnecessary since joystick hotkeys don't call us and even if they did, probably shouldn't be included.
							//&& hk2.mParentEnabled   ) // CriterionAllowsFiring() will check this for us.
					   )
					{
						// The following section is similar to one higher above, so maintain them together:
						if ((vp = hk2.CriterionAllowsFiring(ref zero, extraInfo, ref singleChar)) != null)
						{
							if (!fireWithNoSuppress) // Caller hasn't yet determined its value with certainty (currently, this statement might always be true).
								fireWithNoSuppress = (vp.noSuppress & AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0;

							hotkeyIDwithFlags = hk2.id; // Caller currently doesn't need the flags put onto it, so they're omitted.
							return vp; // It found an eligible variant to fire.
						}
					}
				}
			}

			// Otherwise, this hotkey has no variants that can fire.  Caller wants a few things updated in that case.
			if (!fireWithNoSuppress) // Caller hasn't yet determined its value with certainty.
				fireWithNoSuppress = true; // Fix for v1.0.47.04: Added this line and the one above to fix the fact that a context-sensitive hotkey like "a UP::" would block the down-event of that key even when the right window/criteria aren't met.

			if (singleChar.HasValue && singleChar != 'i') // 'i' takes precedence because it's used to detect when #InputLevel prevented the hotkey from firing, to prevent it from being suppressed.
				singleChar = '#'; // '#' in KeyHistory to indicate this hotkey is disabled due to #HotIf WinActive/Exist() criterion.

			return null;
		}

		internal static ref uint CustomComboLast(ref uint first)
		{
			for (; first != HOTKEY_ID_INVALID; first = ref Script.TheScript.HotkeyData.shk[(int)first].nextHotkey)
			{
			}

			return ref first;
		}

		/// <summary>
		/// Creates, updates, enables, or disables a hotkey dynamically (while the script is running).
		/// </summary>
		/// <param name="hotkeyName"></param>
		/// <param name="options"></param>
		/// <param name="callback"></param>
		/// <param name="hookAction"></param>
		/// <returns></returns>
		/// <exception cref="ValueError"></exception>
		internal static ResultType Dynamic(string hotkeyName, string options, IFuncObj callback, uint hookAction)
		{
			// Caller has ensured that aCallback and _hookAction can't both be non-zero. Furthermore,
			// both can be zero/NULL only when the caller is updating an existing hotkey to have new options
			// (i.e. it's retaining its current callback).
			if (callback != null)
			{
                if (callback is IFuncObj fc && !fc.IsValid)
                    return ResultType.Fail;
			}

			uint noSuppress = 0;
			bool hook_is_mandatory = false;
			var hk = FindHotkeyByTrueNature(hotkeyName, ref noSuppress, ref hook_is_mandatory); // NULL if not found.
			var variant = hk?.FindVariant();
			var updateAllHotkeys = false;  // This method avoids multiple calls to ManifestAllHotkeysHotstringsHooks() (which is high-overhead).
			var variantWasJustCreated = false;
			var script = Script.TheScript;

			switch (hookAction)
			{
				case HOTKEY_ID_ON:
				case HOTKEY_ID_OFF:
				case HOTKEY_ID_TOGGLE:
					if (hk == null)
						return (ResultType)Errors.ValueErrorOccurred("Nonexistent hotkey.", hotkeyName, ResultType.Fail);

					if (!(variant != null || hk.hookAction != 0)) // hookAction (alt-tab) hotkeys don't need a variant that matches the current criteria.
						// To avoid ambiguity and also allow the script to use error handling to detect whether a variant
						// already exists, it seems best to strictly require a matching variant rather than falling back
						// onto some "default variant" such as the global variant (if any).
						return (ResultType)Errors.ValueErrorOccurred("Nonexistent hotkey variant(IfWin).", hotkeyName, ResultType.Fail);

					if (hookAction == HOTKEY_ID_TOGGLE)
						hookAction = hk.hookAction != 0
									 ? hk.parentEnabled ? HOTKEY_ID_OFF : HOTKEY_ID_ON // Enable/disable parent hotkey (due to alt-tab being a global hotkey).
									 : variant.enabled ? HOTKEY_ID_OFF : HOTKEY_ID_ON; // Enable/disable individual variant.

					if (hookAction == HOTKEY_ID_ON)
					{
						if (hk.hookAction != 0 ? hk.EnableParent() : hk.Enable(variant))
							updateAllHotkeys = true; // Do it this way so that any previous "true" value isn't lost.
					}
					else if (hk.hookAction != 0 ? hk.DisableParent() : hk.Disable(variant))
						updateAllHotkeys = true; // Do it this way so that any previous "true" value isn't lost.

					break;

				default: // _hookAction is 0 or an AltTab action.  COMMAND: Hotkey, Name, Callback|AltTabAction
					if (hk == null) // No existing hotkey of this name, so create a new hotkey.
					{
						if (hookAction != 0) // Create hotkey: Hotkey Name, AltTabAction
							hk = AddHotkey(null, hookAction, hotkeyName, ref noSuppress);
						else // Create hotkey: Hotkey Name, Callback [, Options]
						{
							if (callback == null) // Caller is trying to set new aOptions for a nonexistent hotkey.
								return (ResultType)Errors.ValueErrorOccurred("Nonexistent hotkey.", hotkeyName, ResultType.Fail);

							hk = AddHotkey(callback, 0, hotkeyName, ref noSuppress);
						}

						if (hk == null)
							return ResultType.Fail; // AddHotkey() already displayed the error.

						variant = hk.lastVariant; // Update for use with the options-parsing section further below.
						updateAllHotkeys = true;
						variantWasJustCreated = true;
					}
					else // Hotkey already exists (though possibly not the required variant).  Update the hotkey if appropriate.
					{
						if (hk.hookAction != hookAction) // COMMAND: Change to/from alt-tab hotkey.
						{
							// LoadIncludedFile() contains logic and comments similar to this, so maintain them together.
							// If _hookAction isn't zero, the caller is converting this hotkey into a global alt-tab
							// hotkey (alt-tab hotkeys are never subject to #HotIf, as documented).  Thus, variant can
							// be NULL because making a hotkey become alt-tab doesn't require the creation or existence
							// of a variant matching the current #HotIf criteria.  However, continue on to process the
							// Options parameter in case it contains "On" or some other keyword applicable to alt-tab.
							hk.hookAction = hookAction;

							if (hookAction == 0)
								// Since this hotkey is going from alt-tab to non-alt-tab, make sure it's not disabled
								// because currently, mParentEnabled is only actually used by alt-tab hotkeys (though it
								// may have other uses in the future, which is why it's implemented and named the way it is).
								hk.parentEnabled = true;
							else // This hotkey is becoming alt-tab.
							{
								// Make the hook mandatory for this hotkey. Known limitation: For maintainability and code size,
								// this is never undone (even if the hotkey is changed back to non-alt-tab) because there are
								// many other reasons a hotkey's mKeybdHookMandatory could be true, so can't change it back to
								// false without checking all those other things.
								if (HK_TYPE_CAN_BECOME_KEYBD_HOOK(hk.type))
									hk.keybdHookMandatory = true; // Causes mType to be set to HK_KEYBD_HOOK by ManifestAllHotkeysHotstringsHooks().
							}

							// Even if it's still an alt-tab action (just a different one), hook's data structures still
							// need to be updated.  Otherwise, this is a change from an alt-tab type to a non-alt-tab type,
							// or vice versa: Due to the complexity of registered vs. hook hotkeys, for now just start from
							// scratch so that there is high confidence that the hook and all registered hotkeys, including
							// their interdependencies, will be re-initialized correctly.
							updateAllHotkeys = true;
						}

						// If the above changed the action from an Alt-tab type to non-alt-tab, there may be a callback
						// to be applied to the existing variant (or created as a new variant).
						if (callback != null) // Update hotkey: Hotkey Name, Callback [, Options]
						{
							// If there's a matching variant, update its callback. Otherwise, create a new variant.
							if (variant != null) // There's an existing variant...
							{
								if (callback != variant.callback) // ...and its callback is being changed.
									variant.callback = callback;
							}
							else // No existing variant matching current criteria, so create a new variant.
							{
								variant = hk.AddVariant(callback, noSuppress);
								variantWasJustCreated = true;
								updateAllHotkeys = true;

								// It seems undesirable for #UseHook to be applied to a hotkey just because its options
								// were updated with the Hotkey command; therefore, #UseHook is only applied for newly
								// created variants such as this one.  For others, the $ prefix can be applied.
								if (script.ForceKeybdHook)
									hook_is_mandatory = true;
							}
						}
						else// NULL callback, so either it just became an alt-tab hotkey above, or it's "Hotkey Name,, Options".
							if (variant == null) // Below relies on this check.
								break; // Let the error-catch below report it as an error.

						// v1.1.15: Allow the ~tilde prefix to be added/removed from an existing hotkey variant.
						// v1.1.19: Apply this change even if _callback is omitted.  This is redundant if
						// variant_was_just_created, but checking that condition seems counter-productive.
						variant.noSuppress = noSuppress;
						// hk.noSuppress might be inaccurate if a no-suppress flag was just removed from this variant,
						// but that just means a slight reduction in efficiency if tilde is removed from all variants.
						hk.noSuppress |= noSuppress; // Apply both AT_LEAST_ONE_VARIANT_HAS_TILDE and NO_SUPPRESS_PREFIX, if present.

						if ((noSuppress & AT_LEAST_ONE_VARIANT_HAS_TILDE) == 0)
							hk.noSuppress |= AT_LEAST_ONE_VARIANT_LACKS_TILDE;

						// v1.1.19: Allow the $UseHook prefix to be added to an existing hotkey.
						if (!hk.keybdHookMandatory && (hook_is_mandatory || (noSuppress != 0)))
						{
							// Require the hook for all variants of this hotkey if any variant requires it.
							// This seems more intuitive than the old behavior, which required $ or #UseHook
							// to be used on the *first* variant, even though it affected all variants.
							updateAllHotkeys = true; // Since it may be switching from reg to k-hook.
							hk.keybdHookMandatory = true;
						}
					} // Hotkey already existed.

					break;
			} // switch(aHookAction)

			// Above has ensured that hk is not NULL.
			// The following check catches:
			// Hotkey, Name,, Options  ; Where name exists as a hotkey, but the right variant doesn't yet exist.
			// If it catches anything else, that could be a bug, so this error message will help spot it.
			if (!(variant != null || hk.hookAction != 0)) // mHookAction (alt-tab) hotkeys don't need a variant that matches the current criteria.
				return (ResultType)Errors.ValueErrorOccurred("Nonexistent hotkey variant (IfWin).", hotkeyName, ResultType.Fail);

			// Below relies on the fact that either variant or hk.mHookAction (or both) is now non-zero.
			// Specifically, when an existing hotkey was changed to become an alt-tab hotkey, above, there will sometimes
			// be a NULL variant (depending on whether there happens to be a variant in the hotkey that matches the current criteria).

			// If aOptions is blank, any new hotkey or variant created above will have used the current values of
			// g_MaxThreadsBuffer, etc.
			if (!string.IsNullOrEmpty(options))
			{
				for (var i = 0; i < options.Length; ++i)
				{
					switch (char.ToUpper(options[i]))
					{
						case 'O': // v1.0.38.02.
						{
							var ch2 = char.ToUpper(options[i + 1]);

							if (ch2 == 'N') // Full validation for maintainability.
							{
								++i; // Omit the 'N' from further consideration in case it ever becomes a valid option letter.

								if (hk.hookAction != 0 ? hk.EnableParent() : hk.Enable(variant)) // Under these conditions, earlier logic has ensured variant is non-NULL.
									updateAllHotkeys = true; // Do it this way so that any previous "true" value isn't lost.
							}
							else if (options.AsSpan(i).StartsWith("Off", StringComparison.OrdinalIgnoreCase))
							{
								i += 2; // Omit the letters of the word from further consideration in case "f" ever becomes a valid option letter.

								if (hk.hookAction != 0 ? hk.DisableParent() : hk.Disable(variant)) // Under these conditions, earlier logic has ensured variant is non-NULL.
									updateAllHotkeys = true; // Do it this way so that any previous "true" value isn't lost.

								if (variantWasJustCreated) // This variant (and possibly its parent hotkey) was just created above.
									updateAllHotkeys = false; // Override the "true" that was set (either right above *or* anywhere earlier) because this new hotkey/variant won't affect other hotkeys.
							}
						}
						break;

						case 'B':
						{
							if (variant != null)
							{
								var ch2 = options[i + 1];
								variant.maxThreadsBuffer = ch2 != '0';  // i.e. if the char is NULL or something other than '0'.
							}
						}
						break;

						// For options such as P & T: Use atoi() vs. ATOI() to avoid interpreting something like 0x01B
						// as hex when in fact the B was meant to be an option letter:
						case 'P':
						{
							if (variant != null && int.TryParse(options.AsSpan(i + 1), out var val))
								variant.priority = val;
						}
						break;

						case 'S':
						{
							if (variant != null)
							{
								var ch2 = options[i + 1];
								variant.suspendExempt = ch2 != '0';
							}
						}
						break;

						case 'T':
							if (variant != null)
							{
								if (uint.TryParse(options.AsSpan(i + 1), out var val))
									variant.maxThreads = val;

								if (variant.maxThreads > script.MaxThreadsTotal) // To avoid array overflow, this limit must by obeyed except where otherwise documented.
									// Older comment: Keep this limited to prevent stack overflow due to too many pseudo-
									variant.maxThreads = script.MaxThreadsTotal;
								else if (variant.maxThreads < 1)
									variant.maxThreads = 1;
							}

							break;

						case 'I':
							if (variant != null && long.TryParse(options.AsSpan(i + 1), out var newInputLevel))
							{
								variant.maxThreads = newInputLevel;

								if (KeyboardMouseSender.SendLevelIsValid(newInputLevel))
								{
									if (newInputLevel != 0 && !hk.keybdHookMandatory)
									{
										// For simplicity, a hotkey requires the hook if any of its variants have a non-zero
										// input level, even if those variants are disabled.  The same is done for the tilde
										// prefix above and in AddVariant(); see there for more comments.
										hk.keybdHookMandatory = true;
										updateAllHotkeys = true;
									}

									variant.inputLevel = newInputLevel;
								}
							}

							break;
							// Otherwise: Ignore other characters, such as the digits that comprise the number after the T option.
					}

					while (i < options.Length && Types.IsSpace(options[i]) == 0)
						i++;
				}
			}

			if (updateAllHotkeys)
				_ = ManifestAllHotkeysHotstringsHooks(); // See its comments for why it's done in so many of the above situations.

			return ResultType.Ok;
		}

		/// <summary>
		/// Returns the address of the hotkey if found, NULL otherwise.
		/// In v1.0.42, it tries harder to find a match so that the order of modifier symbols doesn't affect the true nature of a hotkey.
		/// For example, ^!c should be the same as !^c, primarily because RegisterHotkey() and the hook would consider them the same.
		/// Primary benefits to the above:
		/// 1) Catches script bugs, such as unintended duplicates.
		/// 2) Allows a script to use the Hotkey command more precisely and with greater functionality.
		/// 3) Allows hotkey variants to work correctly even when the order of modifiers varies.  For example, if ^!c is a hotkey that fires
		///    only when window type 1 is active and !^c (reversed modifiers) is a hotkey that fires only when window type 2 is active,
		///    one of them would never fire because the hook isn't capable or storing two hotkey IDs for the same combination of
		///    modifiers+VK/SC.
		/// </summary>
		internal static HotkeyDefinition FindHotkeyByTrueNature(string _name, ref uint _noSuppress, ref bool _hookIsMandatory)
		{
			var shk = Script.TheScript.HotkeyData.shk;
			HotkeyProperties propCandidate = new (), propExisting = new ();
			_ = TextToModifiers(_name, null, propCandidate);
			_noSuppress = (propCandidate.prefixHasTilde ? NO_SUPPRESS_PREFIX : 0)//Set for caller.
						  | (propCandidate.suffixHasTilde ? AT_LEAST_ONE_VARIANT_HAS_TILDE : 0);
			_hookIsMandatory = propCandidate.hookIsMandatory; // Set for caller.
			// Both suffix_has_tilde and a hypothetical prefix_has_tilde are ignored during dupe-checking below.
			// See comments inside the loop for details.

			for (var i = 0; i < shk.Count; ++i)
			{
				_ = TextToModifiers(shk[i].Name, null, propExisting);

				if (propExisting.modifiers == propCandidate.modifiers
						&& propExisting.modifiersLR == propCandidate.modifiersLR
						&& propExisting.isKeyUp == propCandidate.isKeyUp
						// Treat wildcard (*) as an entirely separate hotkey from one without a wildcard.  This is because
						// the hook has special handling for wildcards that allow non-wildcard hotkeys that overlap them to
						// take precedence, sort of like "clip children".  The logic that builds the eclipsing array would
						// need to be redesigned, which might not even be possible given the complexity of interactions
						// between variant-precedence and hotkey/wildcard-precedence.
						// By contrast, in v1.0.44 pass-through (~) is considered an attribute of each variant of a
						// particular hotkey, not something that makes an entirely new hotkey.
						// This was done because the old method of having them distinct appears to have only one advantage:
						// the ability to dynamically enable/disable ~x separately from x (since if both were in effect
						// simultaneously, one would override the other due to two different hotkey IDs competing for the same
						// ID slot within the VK/SC hook arrays).  The advantages of allowing tilde to be a per-variant attribute
						// seem substantial, namely to have some variant/siblings pass-through while others do not.
						&& propExisting.hasAsterisk == propCandidate.hasAsterisk
						// v1.0.43.05: Use stricmp not lstrcmpi because an uppercase high ANSI letter isn't necessarily
						// produced by holding down the shift key and pressing the lowercase letter.  In addition, it
						// preserves backward compatibility and may improve flexibility.
						&& string.Compare(propExisting.prefixText, propCandidate.prefixText, true) == 0
						&& string.Compare(propExisting.suffixText, propCandidate.suffixText, true) == 0)
					return shk[i]; // Match found.
			}

			return null;  // No match found.
		}

		/// <summary>
		/// Returns the address of the hotkey if found, NULL otherwise.
		/// Find the first hotkey whose modifiersLR contains *any* of the modifiers shows in the parameter value.
		/// Obsolete: The caller tells us the ID of the hotkey to omit from the search because that one
		/// would always be found (since something like "lcontrol=calc.exe" in the script
		/// would really be defines as  "<^control=calc.exe".
		/// Note: By intent, this function does not find hotkeys whose normal/neutral modifiers
		/// contain <modifiersLR>.
		/// </summary>
		/// <param name="modifiersLR"></param>
		/// <returns></returns>
		internal static HotkeyDefinition FindHotkeyContainingModLR(uint modifiersLR)
		{
			if (modifiersLR == 0)
				return null;

			foreach (var v in Script.TheScript.HotkeyData.shk)
				if ((v.modifiersLR & modifiersLR) != 0)// Bitwise set-intersection: indicates if anything in common:
					return v;

			return null;  // No match found.
		}

		internal static IFuncObj FindHotkeyCriterion(IFuncObj fo) => FindHotkeyIf(fo, Script.TheScript.hotCriterions);

		internal static IFuncObj FindHotkeyIfExpr(IFuncObj fo) => FindHotkeyIf(fo, Script.TheScript.hotExprs);

		internal static uint FindPairedHotkey(uint firstID, uint modsLR, bool keyUp)
		{
			var modifiers = ConvertModifiersLR(modsLR); // Neutral modifiers.

			for (var candidateId = firstID; candidateId != HOTKEY_ID_INVALID;)
			{
				var hk2 = Script.TheScript.HotkeyData.shk[(int)candidateId];
				candidateId = hk2.nextHotkey;

				if ((hk2.allowExtraModifiers || ((~hk2.modifiersConsolidatedLR & modsLR) == 0))
						&& hk2.keyUp == keyUp
						&& hk2.modifierVK == 0 // Avoid accidental matching of normal hotkeys with custom-combo "&"
						&& hk2.modifierSC == 0 // hotkeys that happen to have the same mVK/SC.
						&& hk2.hookAction == 0 // Might be unnecessary to check this; but just in case.
						&& (hk2.modifiers & ~modifiers) == 0 // All neutral modifiers required by the candidate are pressed.
						&& (hk2.modifiersLR & ~modsLR) == 0 // All left-right specific modifiers required by the candidate are pressed.
						//&& hk2.mParentEnabled // CriterionAllowsFiring() will check this for us.
				   )
					return keyUp ? (hk2.id | HOTKEY_KEY_UP) : hk2.id;
			}

			return HOTKEY_ID_INVALID;
		}

		/// <summary>
		/// Get the text description of all hotkeys in this Script.TheScript.
		/// </summary>
		internal static string GetHotkeyDescriptions()
		{
			var shk = Script.TheScript.HotkeyData.shk;
			var sb = new StringBuilder(4096);
			_ = sb.Append("Type\tOff?\tLevel\tRunning\tName\r\n-------------------------------------------------------------------\r\n");

			//Start at the oldest and continue up through the newest.
			for (var i = 0; i < shk.Count; ++i)
				_ = sb.Append(shk[i].ToText(true));

			return sb.ToString();
		}

		internal static bool HK_TYPE_CAN_BECOME_KEYBD_HOOK(HotkeyTypeEnum type) => type == HotkeyTypeEnum.Normal;

		internal static bool HK_TYPE_IS_HOOK(HotkeyTypeEnum type) => type > HotkeyTypeEnum.Normal&& type < HotkeyTypeEnum.Joystick;

		/// <summary>
		/// This is a global function because it's used by both hotkeys and hotstrings.
		/// In addition to being called by the hook thread, this can now be called by the main thread.
		/// That happens when a WM_HOTKEY message arrives (for non-hook hotkeys, i.e. RegisterHotkey).
		/// Returns a non-NULL HWND if firing is allowed.  However, if it's a global criterion or
		/// a "not-criterion" such as #HotIf Not WinActive(), (HWND)1 is returned rather than a genuine HWND.
		/// </summary>
		/// <param name="criterion"></param>
		/// <param name="hotkeyName"></param>
		/// <returns></returns>
		internal static long HotCriterionAllowsFiring(IFuncObj criterion, string hotkeyName)
		{
			if (criterion == null)
				return 1L;

			object val = null;
			var task = Task.Run(() => val = criterion.Call(hotkeyName));//Unsure if this will cause problems with GUI threading.

			if (task.Wait(TimeSpan.FromMilliseconds(A_HotIfTimeout.Ad())))
			{
				if (val is long l)
					return l;
				else
					return Script.ForceBool(val) ? 1L : 0L;
			}
			else
				return 0L;
		}

		internal static uint HotkeyRequiresModLR(uint hotkeyID, uint modLR) => hotkeyID < Script.TheScript.HotkeyData.shk.Count ? Script.TheScript.HotkeyData.shk[(int)hotkeyID].modifiersConsolidatedLR& modLR : 0u;

		internal static void InstallKeybdHook()
		{
			var script = Script.TheScript;
			script.HotkeyData.whichHookNeeded |= HookType.Keyboard;
			var ht = script.HookThread;

			if (!ht.HasKbdHook())
				ht.ChangeHookState(script.HotkeyData.shk, script.HotkeyData.whichHookNeeded, script.HotkeyData.whichHookAlways);
		}

		internal static void InstallMouseHook()
		{
			var script = Script.TheScript;
			script.HotkeyData.whichHookNeeded |= HookType.Mouse;
			var ht = script.HookThread;

			if (!ht.HasMouseHook())
				ht.ChangeHookState(script.HotkeyData.shk, script.HotkeyData.whichHookNeeded, script.HotkeyData.whichHookAlways);
		}

		internal static bool IsAltTab(uint id) => id > HOTKEY_ID_MAX&& id < HOTKEY_ID_INVALID;

		/// <summary>
		/// Caller knows that one of the users of the keyboard hook no longer requires it,
		/// and wants it uninstalled if it is no longer needed by anything else.
		/// </summary>
		internal static void MaybeUninstallHook()
		{
			var script = Script.TheScript;

			// Do some quick checks to avoid scanning all hotkeys unnecessarily:
			if (script.input != null || script.HotstringManager.enabledCount != 0 || ((int)script.HotkeyData.whichHookAlways & KeyboardMouseSender.HookKeyboard) != 0)
				return;

			// Do more thorough checking to determine whether the hook is still needed:
			_ = ManifestAllHotkeysHotstringsHooks();
		}

		/// <summary>
		/// aVKorSC contains the virtual key or scan code of the specified prefix key (it's a scan code if aIsSC is true).
		/// Returns true if this prefix key has no suffixes that can possibly.  Each such suffix is prevented from
		/// firing by one or more of the following:
		/// 1) Hotkey is completely disabled via IsCompletelyDisabled().
		/// 2) Hotkey has criterion and those criterion do not allow the hotkey to fire.
		/// </summary>
		/// <param name="VKorSC"></param>
		/// <param name="isSC"></param>
		/// <param name="suppress">Caller is expected to set to a default value of false.</param>
		/// <returns></returns>
		internal static bool PrefixHasNoEnabledSuffixes(uint VKorSC, bool isSC, ref bool suppress)
		{
			var script = Script.TheScript;
			var ht = script.HookThread;
			var shk = script.HotkeyData.shk;
			// v1.0.44: Added aAsModifier so that a pair of hotkeys such as:
			//   LControl::tooltip LControl
			//   <^c::tooltip ^c
			// ...works as it did in versions prior to 1.0.41, namely that LControl fires on key-up rather than
			// down because it is considered a prefix key for the <^c hotkey .
			bool? b = null;
			var asModifier = ht.KeyToModifiersLR(isSC ? 0u : VKorSC, isSC ? VKorSC : 0u, ref b);
			bool hasEnabledSuffix = false;

			for (var i = 0; i < shk.Count; ++i)
			{
				var hk = shk[i];

				if (VKorSC != (isSC ? hk.modifierSC : hk.modifierVK) && (asModifier & hk.modifiersLR) == 0
						|| hk.IsCompletelyDisabled())
					continue; // This hotkey isn't enabled or it doesn't use the specified key as a prefix.  No further checking for it.

				if (hk.hookAction != 0)
				{
					if (A_IsSuspended)
						// An alt-tab hotkey (non-NULL mHookAction) is always suspended when g_IsSuspended==true because
						// alt-tab hotkeys have no subroutine capable of making them exempt.  So g_IsSuspended is checked
						// for alt-tab hotkeys here; and for other types of hotkeys, it's checked further below.
						continue;

					//else // This alt-tab hotkey is currently active.

					if ((hk.noSuppress & NO_SUPPRESS_PREFIX) != 0 || suppress)
						return false; // Since any stored mHotCriterion are ignored for alt-tab hotkeys, no further checking is needed.

					hasEnabledSuffix = true;
					continue; // Still need to check other hotkeys for NO_SUPPRESS_PREFIX.
				}

				if (hasEnabledSuffix && ((hk.noSuppress & NO_SUPPRESS_PREFIX) == 0))
					continue; // No need to evaluate this hotkey's variants.

				// Otherwise, find out if any of its variants is eligible to fire.  If so, immediately return
				// false because even one eligible hotkey means this prefix is enabled.
				for (var vp = hk.firstVariant; vp != null; vp = vp.nextVariant)
				{
					// v1.0.42: Fixed to take into account whether the hotkey is suspended (previously it only checked
					// whether the hotkey was enabled (above), which isn't enough):
					if (vp.enabled // This particular variant within its parent hotkey is enabled.
							&& (!A_IsSuspended || vp.suspendExempt) // This variant isn't suspended...
							&& (vp.hotCriterion == null || (HotCriterionAllowsFiring(vp.hotCriterion, hk.Name) != 0L))) // ... and its criteria allow it to fire.
					{
						if ((vp.noSuppress & NO_SUPPRESS_PREFIX) != 0 || suppress)
							return false; // At least one of this prefix's suffixes is eligible for firing.

						hasEnabledSuffix = true;

						if ((hk.noSuppress & NO_SUPPRESS_PREFIX) == 0)
							break; // None of this hotkey's variants have NO_SUPPRESS_PREFIX.

						// Keep checking to ensure no other enabled variants have NO_SUPPRESS_PREFIX.
					}

					//return false; // At least one of this prefix's suffixes is eligible for firing.
				}
			}

			// Since above didn't return, either no hotkeys were found for this prefix that are capable of firing,
			// or no variants were found with the NO_SUPPRESS_PREFIX flag.
			suppress = hasEnabledSuffix;
			return !hasEnabledSuffix;
		}

		internal static void ResetRunAgainAfterFinished()  // For all hotkeys and all variants of each.
		{
			var shk = Script.TheScript.HotkeyData.shk;

			for (var i = 0; i < shk.Count; ++i)
				for (var vp = shk[i].firstVariant; vp != null; vp = vp.nextVariant)
					vp.runAgainAfterFinished = false;
		}

		/// <summary>
		/// Returns OK or FAIL.  This function is static and aThisHotkey is passed in as a parameter
		/// so that aThisHotkey can be NULL. NULL signals that aName should be checked as a valid
		/// hotkey only rather than populating the members of the new hotkey aThisHotkey. This function
		/// and those it calls should avoid showing any error dialogs in validation mode.  Instead,
		/// it should simply return OK if aName is a valid hotkey and FAIL otherwise.
		internal static ResultType TextInterpret(string name, HotkeyDefinition thisHotkey, bool syntaxCheckOnly = false)
		{
			var splits = name.Split(COMPOSITE_DELIMITER);

			if (splits.Length == 1)
			{
				var ttm = TextToModifiers(splits[0], thisHotkey);
				return TextToKey(ref ttm, false, thisHotkey, syntaxCheckOnly);
			}

			var term1 = splits[0];

			if (term1[0] == '~')
				term1 = term1.Substring(1);

			term1 = term1.TrimEnd(SpaceTab);
			var result = TextToKey(ref term1, true, thisHotkey, syntaxCheckOnly);

			if (result == ResultType.Fail || result == ResultType.ConditionFalse)
				return result;

			var term2 = splits[1].TrimStart(SpaceTab);

			// Even though modifiers on keys already modified by a mModifierVK are not supported, call
			// TextToModifiers() anyway to use its output (for consistency).  The modifiers it sets
			// are currently ignored because the mModifierVK takes precedence.
			// UPDATE: Treat any modifier other than '~' as an error, since otherwise users expect
			// hotkeys like "' & +e::Send È" to work.
			if (term2[0] == '~')
				term2 = term2.Substring(1); // Some other stage handles this modifier, so just ignore it here.

			return TextToKey(ref term2, false, thisHotkey, syntaxCheckOnly);
		}

		/// <summary>
		/// This function and those it calls should avoid showing any error dialogs when caller passes NULL for
		/// aThisHotkey (however, there is at least one exception explained in comments below where it occurs).
		/// Caller must ensure that aText is a modifiable string.
		/// Takes input param aText to support receiving only a subset of mName.
		/// In private members, sets the values of vk/sc or ModifierVK/ModifierSC depending on aIsModifier.
		/// It may also merge new modifiers into the existing value of modifiers, so the caller
		/// should never reset modifiers after calling this.
		/// Returns OK or FAIL.
		internal static ResultType TextToKey(ref string text, bool isModifier, HotkeyDefinition thisHotkey, bool syntaxCheckOnly)
		{
			uint tempVk; // No need to initialize this one.
			var tempSc = 0u;
			uint? modifiersLR = 0u;
			var isMouse = false;
			uint? joystickId = 0u;
			var script = Script.TheScript;
			var ht = script.HookThread;
			var kbdMouseSender = ht.kbdMsSender;//This should always be non-null if any hotkeys/strings are present.
			// Previous steps should make it unnecessary to call omit_leading_whitespace(aText).
			var keynameEndIndex = text.FindHotkeyIdentifierEnd();

			if (keynameEndIndex == 0 && text.Length > 0) // Any single character except '\0' can be a key name.
				keynameEndIndex = 1;

			var keynameEnd = text.AsSpan(keynameEndIndex);

			if (!isModifier && keynameEnd.IndexOfAny(SpaceTabSv) == 0 && keynameEnd.TrimStart(SpaceTab).StartsWith("Up", StringComparison.OrdinalIgnoreCase))
			{
				if (syntaxCheckOnly)
					return ResultType.Ok; // It's a word or single character followed by " up" -- looks valid.

				// This is a key-up hotkey, such as "Ctrl Up::".
				if (thisHotkey != null)
					thisHotkey.keyUp = true;

				text = text.Substring(0, keynameEndIndex);
			}
			else
			{
				// If there's something after the first word/character, it's not a hotkey.
				if (syntaxCheckOnly)
					return keynameEnd.Length > 0 ? ResultType.Fail : ResultType.Ok;

				if (keynameEnd.Length > 0)
					return ResultType.ConditionFalse;
			}

			var hotkeyTypeTemp = HotkeyTypeEnum.Normal;
			ref var hotkeyType = ref (thisHotkey != null ? ref thisHotkey.type : ref hotkeyTypeTemp);//Simplifies and reduces code size below.

			if ((tempVk = ht.TextToVK(text, ref modifiersLR, true, true, 0)) != 0) // Assign.
			{
				if (isModifier)
				{
					if (MouseUtils.IsWheelVK(tempVk))
						return (ResultType)Errors.ValueErrorOccurred("Unsupported prefix key.", text, ResultType.Fail);
				}
				else

					// This is done here rather than at some later stage because we have access to the raw
					// name of the suffix key (with any leading modifiers such as ^ omitted from the beginning):
					if (thisHotkey != null)
						thisHotkey.vkWasSpecifiedByNumber = text.StartsWith("VK", StringComparison.OrdinalIgnoreCase);

				isMouse = MouseUtils.IsMouseVK(tempVk);

				if ((modifiersLR.Value & (MOD_LSHIFT | MOD_RSHIFT)) != 0)
					if (tempVk >= 'A' && tempVk <= 'Z')  // VK of an alpha char is the same as the ASCII code of its uppercase version.
						modifiersLR &= ~(MOD_LSHIFT | MOD_RSHIFT);

				// Above: Making alpha chars case insensitive seems much more friendly.  In other words,
				// if the user defines ^Z as a hotkey, it will really be ^z, not ^+z.  By removing SHIFT
				// from the modifiers here, we're only removing it from our modifiers, not the global
				// modifiers that have already been set elsewhere for this key (e.g. +Z will still be +z).
			}
			else // No virtual key was found.  Is there a scan code?
			{
				bool? dummy = null;

				if ((tempSc = ht.TextToSC(text, ref dummy)) == 0)
				{
					if ((tempSc = (uint)Joystick.Joystick.ConvertJoy(text, ref joystickId, true)) == 0)  // Is there a joystick control/button?
					{
						if (text.Length == 1 && !script.IsReadyToExecute)
						{
							// At load time, single-character key names are always considered valid but show a
							// warning if they can't be registered as hotkeys on the current keyboard layout.
							if (thisHotkey == null) // First stage: caller wants to differentiate this case from others.
								return ResultType.ConditionTrue;

							return ResultType.Fail; // Second stage: return FAIL to avoid creating an invalid hotkey.
						}

						// It's more appropriate to say "key name" than "hotkey" in this message because it's only
						// showing the one bad key name when it's a composite hotkey such as "Capslock & y".
						return (ResultType)Errors.ValueErrorOccurred("Invalid key name.", text, ResultType.Fail);
					}
					else
					{
						// Block joystick buttons as prefix keys at this stage in case hotkey_type would be overridden
						// by the suffix key.  For example, the hotkey `Joy1 & LButton::` would reinterpret Joy1 as sc0C.
						if (isModifier)
							return (ResultType)Errors.ValueErrorOccurred("Unsupported prefix key.", text, ResultType.Fail);

						++script.HotkeyData.joyHotkeyCount;
						hotkeyType = HotkeyTypeEnum.Joystick;
						tempVk = joystickId.Value;  // 0 is the 1st joystick, 1 the 2nd, etc.
						script.HotkeyData.joystickHasHotkeys[joystickId.Value] = true;
					}
				}
			}

			//    If ever do this, be sure to avoid doing it for keys that must be tracked by scan code (e.g. those in the
			//    scan code array).
			//    if (!temp_vk && !is_mouse)  // sc must be non-zero or else it would have already returned above.
			//        if (temp_vk = sc_to_vk(temp_sc))
			//        {
			//            snprintf(error_text, sizeof(error_text), "DEBUG: \"%s\" (scan code %X) was successfully mapped to virtual key %X", text, temp_sc, temp_vk);
			//            MsgBox(error_text);
			//            temp_sc = 0; // Maybe set this just for safety, even though a non-zero vk should always take precedence over it.
			//        }

			if (isMouse)
				hotkeyType = HotkeyTypeEnum.MouseHook;

			if (isModifier)
			{
				if (thisHotkey != null)
				{
					thisHotkey.modifierVK = tempVk;
					thisHotkey.modifierSC = tempSc;
				}
			}
			else
			{
				if (thisHotkey != null)
				{
					thisHotkey.vk = tempVk;
					thisHotkey.sc = tempSc;
					// Turn on any additional modifiers.  e.g. SHIFT to realize '#'.
					// Fix for v1.0.37.03: To avoid using the keyboard hook for something like "+::", which in
					// turn would allow the hotkey fire only for LShift+Equals rather than RShift+Equals, convert
					// modifiers from left-right to neutral.  But exclude right-side modifiers (except RWin) so that
					// things like AltGr are more precisely handled (the implications of this policy could use
					// further review).  Currently, right-Alt (via AltGr) is the only possible right-side key.
					thisHotkey.modifiers |= ConvertModifiersLR(modifiersLR.Value & (MOD_RWIN | MOD_LWIN | MOD_LCONTROL | MOD_LALT | MOD_LSHIFT));
					thisHotkey.modifiersLR |= modifiersLR.Value & (MOD_RSHIFT | MOD_RALT | MOD_RCONTROL); // Not MOD_RWIN since it belongs above.
				}
			}

			return ResultType.Ok;
		}

		/// <summary>
		/// This function and those it calls should avoid showing any error dialogs when caller passes NULL for aThisHotkey.
		/// Takes input param <text> to support receiving only a subset of object.text.
		/// Returns the location in <text> of the first non-modifier key.
		/// Checks only the first char(s) for modifiers in case these characters appear elsewhere (e.g. +{+}).
		/// But come to think of it, +{+} isn't valid because + itself is already shift-equals.  So += would be
		/// used instead, e.g. +==action.  Similarly, all the others, except =, would be invalid as hotkeys also.
		/// UPDATE: On some keyboard layouts, the + key and various others don't require the shift key to be
		/// manifest.  Thus, on these systems a hotkey such as ^+:: is now supported as meaning Ctrl-Plus.
		internal static string TextToModifiers(string text, HotkeyDefinition thisHotkey, HotkeyProperties properties = null)
		{
			// Init output parameter for caller if it gave one:
			properties?.Reset();

			if (string.IsNullOrEmpty(text))
				return text; // Below relies on this having ensured that aText isn't blank.

			// Explicitly avoids initializing modifiers to 0 because the caller may have already included
			// some set some modifiers in there.
			int marker;
			bool keyLeft, keyRight;
			//Simplifies and reduces code size below:
			uint tempModifiers = 0u, tempModifiersLR = 0u;
			ref var modifiers = ref tempModifiers;
			ref var modifiersLR = ref tempModifiersLR;

			if (properties != null)
			{
				modifiers = ref properties.modifiers;
				modifiersLR = ref properties.modifiersLR;
			}
			else if (thisHotkey != null)
			{
				modifiers = ref thisHotkey.modifiers;
				modifiersLR = ref thisHotkey.modifiersLR;
			}

			// Improved for v1.0.37.03: The loop's condition is now marker[1] vs. marker[0] so that
			// the last character is never considered a modifier.  This allows a modifier symbol
			// to double as the name of a suffix key.  It also fixes issues on layouts where the
			// symbols +^#! do not require the shift key to be held down, such as the German layout.
			//
			// Improved for v1.0.40.01: The loop's condition now stops when it reaches a single space followed
			// by the word "Up" so that hotkeys like "< up" and "+ up" are supported by seeing their '<' or '+' as
			// a key name rather than a modifier symbol.
			// Fix for v1.1.27.05: Stop at any space, not just " up", so that " & " is also covered.
			// This fixes "> & v" to not interpret ">" as a modifier.  This also causes "+ ::" to be
			// seen as invalid, where previously TextToModifiers() saw it as Shift+Space but a later
			// stage trimmed the space and registered "+::".  This is best since trailing spaces are
			// not allowed in any other hotkeys, and even "+  ::" (two spaces) was not allowed.
			for (marker = 0, keyLeft = false, keyRight = false; marker + 1 < text.Length && text[marker + 1] != ' '; ++marker)
			{
				switch (text[marker])
				{
					case '>':
						keyRight = true;
						break;

					case '<':
						keyLeft = true;
						break;

					case '*':
						if (thisHotkey != null)
							thisHotkey.allowExtraModifiers = true;

						if (properties != null)
							properties.hasAsterisk = true;

						break;

					case '~':
						if (properties != null)
							properties.suffixHasTilde = true; // If this is the prefix's tilde rather than the suffix, it will be overridden later below.

						break;

					case '$':
						if (thisHotkey != null)
							thisHotkey.keybdHookMandatory = true; // This flag will be ignored if TextToKey() decides this is a JOYSTICK or MOUSE hotkey.

						if (properties != null)
							properties.hookIsMandatory = true;

						break;

					case '!':
						if (!keyRight && !keyLeft)
						{
							modifiers |= MOD_ALT;
							break;
						}

						// Both left and right may be specified, e.g. ><+a means both shift keys must be held down:
						if (keyLeft)
						{
							modifiersLR |= MOD_LALT;
							keyLeft = false;
						}

						if (keyRight)
						{
							modifiersLR |= MOD_RALT;
							keyRight = false;
						}

						break;

					case '^':
						if (!keyRight && !keyLeft)
						{
							modifiers |= MOD_CONTROL;
							break;
						}

						if (keyLeft)
						{
							modifiersLR |= MOD_LCONTROL;
							keyLeft = false;
						}

						if (keyRight)
						{
							modifiersLR |= MOD_RCONTROL;
							keyRight = false;
						}

						break;

					case '+':
						if (!keyRight && !keyLeft)
						{
							modifiers |= MOD_SHIFT;
							break;
						}

						if (keyLeft)
						{
							modifiersLR |= MOD_LSHIFT;
							keyLeft = false;
						}

						if (keyRight)
						{
							modifiersLR |= MOD_RSHIFT;
							keyRight = false;
						}

						break;

					case '#':
						if (!keyRight && !keyLeft)
						{
							modifiers |= MOD_WIN;
							break;
						}

						if (keyLeft)
						{
							modifiersLR |= MOD_LWIN;
							keyLeft = false;
						}

						if (keyRight)
						{
							modifiersLR |= MOD_RWIN;
							keyRight = false;
						}

						break;

					default:
						goto breakloop; // Stop immediately whenever a non-modifying char is found.
				} // switch (*marker)
			} // for()

			breakloop:
			// Now *marker is the start of the key's name.  In addition, one of the following is now true:
			// 1) marker[0] is a non-modifier symbol; that is, the loop stopped because it found the first non-modifier symbol.
			// 2) marker[1] is '\0'; that is, the loop stopped because it reached the next-to-last char (the last char itself is never a modifier; e.g. ^+ is Ctrl+Plus on some keyboard layouts).
			// 3) marker[1] is the start of the string " Up", in which case marker[0] is considered the suffix key even if it happens to be a modifier symbol (see comments at for-loop's control stmt).
			var sub = text.Substring(marker);

			if (properties != null)
			{
				// When caller passes non-NULL aProperties, it didn't omit the prefix portion of a composite hotkey
				// (e.g. the "a & " part of "a & b" is present).  So parse these and all other types of hotkeys when in this mode.
				var splits = sub.Split(COMPOSITE_DELIMITER);

				if (splits.Length == 2)
				{
					properties.prefixText = splits[0].TrimEnd(SpaceTab);
					properties.suffixText = splits[1].TrimStart(SpaceTab);
					properties.prefixHasTilde = properties.suffixHasTilde;

					if (properties.suffixHasTilde = properties.suffixText.StartsWith('~')) // Override any value of noSuppress set higher above.
						properties.suffixText = properties.suffixText.Substring(1); // For simplicity, no skipping of leading whitespace between tilde and the suffix key name.
				}
				else // A normal (non-composite) hotkey, so noSuppress was already set properly (higher above).
					properties.suffixText = sub.TrimStart(SpaceTab);

				var tempIndex = properties.suffixText.IndexOf(" Up", StringComparison.OrdinalIgnoreCase);

				if (tempIndex != -1) // Should be reliable detection method because leading spaces have been omitted and it's unlikely a legitimate key name will ever contain a space followed by "Up".
				{
					properties.suffixText = properties.suffixText.OmitTrailingWhitespace(tempIndex);//Omit " Up" from suffix_text since caller wants that.
					properties.isKeyUp = true; // Override the default set earlier.
				}
			}

			return sub;
		}

		internal static void TriggerJoyHotkeys(int joystickID, uint buttonsNewlyDown)
		{
			var script = Script.TheScript;
			var shk = script.HotkeyData.shk;

			for (var i = 0; i < shk.Count; ++i)
			{
				var hk = shk[i];

				// Fix for v1.0.34: If hotkey isn't enabled, or hotkeys are suspended and this one isn't
				// exempt, don't fire it.  These checks are necessary only for joystick hotkeys because
				// normal hotkeys are completely deactivated when turned off or suspended, but the joystick
				// is still polled even when some joystick hotkeys are disabled.  UPDATE: In v1.0.42, Suspend
				// is checked upon receipt of the message, not here upon sending.
				if (hk.type == HotkeyTypeEnum.Joystick && hk.vk == joystickID
						&& (buttonsNewlyDown & (0x01 << (int)(hk.sc - (uint)JoyControls.Button1))) != 0) // This hotkey's button is among those newly pressed.
				{
					// Criteria are checked, and variant determined, upon arrival of message rather than when sending
					// ("suspend" is also checked then).  This is because joystick button presses are never hidden
					// from the active window (the concept really doesn't apply), so not checking here avoids the
					// performance loss of a second check (the loss can be significant in the case of
					// "SetTitleMatchMode Slow").
					//
					// Post it to the thread because the message pump itself (not the WindowProc) will handle it.
					// UPDATE: Posting to NULL would have a risk of discarding the message if a MsgBox pump or
					// pump other than MsgSleep() were running.  The only reason it doesn't is that this function
					// is only ever called from MsgSleep(), which is careful to process all messages (at least
					// those that aren't kept queued due to the message filter) prior to returning to its caller.
					// But for maintainability, it seems best to change this to g_hWnd vs. NULL to make joystick
					// hotkeys behave more like standard hotkeys.
					_ = PostHotkeyMessage(script.MainWindowHandle, (uint)i, 0u);
				}

				//else continue the loop in case the user has newly pressed more than one joystick button.
			}
		}

		/// <summary>
		/// Returns null upon out-of-memory; otherwise, the address of the new variant.
		/// The caller is responsible for calling ManifestAllHotkeysHotstringsHooks(), if appropriate.
		/// </summary>
		/// <param name="_callback"></param>
		/// <param name="_suffixHasTilde"></param>
		/// <returns></returns>
		internal HotkeyVariant AddVariant(IFuncObj _callback, uint _noSuppress)
		{
			var vp = new HotkeyVariant
			{
				// The following members are left at 0/NULL by the above:
				// mNextVariant
				// mExistingThreads
				// mRunAgainAfterFinished
				// mRunAgainTime
				// mPriority (default priority is always 0)
				callback = _callback,
				originalCallback = Proc,
				maxThreads = A_MaxThreadsPerHotkey.Aui(),    // The values of these can vary during load-time.
				maxThreadsBuffer = A_MaxThreadsBuffer.Ab(),
				inputLevel = (long)A_InputLevel,
				hotCriterion = Script.TheScript.Threads.CurrentThread.hotCriterion, // If this hotkey is an alt-tab one (mHookAction), this is stored but ignored until/unless the Hotkey command converts it into a non-alt-tab hotkey.
				suspendExempt = A_SuspendExempt.Ab(),
				noSuppress = _noSuppress,
				enabled = true
			};

			if (vp.inputLevel > 0)
			{
				keybdHookMandatory = true;// A non-zero InputLevel only works when using the hook
			}

			noSuppress |= _noSuppress;

			if ((_noSuppress & AT_LEAST_ONE_VARIANT_HAS_TILDE) != 0)
			{
				// For simplicity, make the hook mandatory for any hotkey that has at least one non-suppressed variant.
				// Otherwise, ManifestAllHotkeysHotstringsHooks() would have to do a loop to check if any
				// non-suppressed variants are actually enabled & non-suspended to decide if the hook is actually needed
				// for a hotkey that has a global variant.  Due to rarity and code size, it doesn't seem worth it.
				keybdHookMandatory = true;
			}
			else
				noSuppress |= AT_LEAST_ONE_VARIANT_LACKS_TILDE;

			// Update the linked list:
			if (firstVariant == null)
			{
				vp.index = 1; // Start at 1 since 0 means "undetermined variant".
				firstVariant = lastVariant = vp;
			}
			else
			{
				vp.index = lastVariant.index + 1;
				lastVariant.nextVariant = vp;
				lastVariant = vp;// This must be done after the above:
			}

			return vp;  // Indicate success by returning the new object.
		}

		//internal bool Condition()
		//{
		//  if (Precondition == null)
		//      return true;

		//  var result = Precondition.Call(new object[] { });
		//  return result is bool b ? b : result != null;
		//}

		/// <summary>
		/// Caller must not call this for AltTab hotkeys IDs because this will always return NULL in such cases.
		/// Returns the address of the first matching non-global hotkey variant that is allowed to fire.
		/// If there is no non-global one eligible, the global one is returned (or NULL if none).
		/// If non-NULL, aFoundHWND is an output variable for the caller, but it is only set if a
		/// non-global/criterion variant is found; that is, it isn't changed when no match is found or
		/// when the match is a global variant.  Even when set, aFoundHWND will be (HWND)1 for
		/// "not-criteria" such as #HotIf Not WinActive().
		/// </summary>
		internal HotkeyVariant CriterionAllowsFiring(ref long foundHwnd, ulong extraInfo, ref char? singleChar)
		{
			// Check mParentEnabled in case the hotkey became disabled between the time the message was posted
			// and the time it arrived.  A similar check is done for "suspend" later below (since "suspend"
			// is a per-variant attribute).
			if (!parentEnabled) // IsCompletelyDisabled() isn't called because the loop below checks all the mEnabled flags, no need to do it twice.
				return null;

			HotkeyVariant vp, vpToFire;

			// aHookAction isn't checked because this should never be called for alt-tab hotkeys (see other comments above).
			for (vpToFire = null, vp = firstVariant; vp != null; vp = vp.nextVariant)
			{
				// Technically, g_IsSuspended needs to be checked only if our caller is TriggerJoyHotkeys()
				// because other callers would never have received the hotkey message in the first place.
				// However, since it's possible for a hotkey to become suspended between the time its hotkey
				// message is posted and the time it is fetched and processed, aborting the firing seems
				// like the best choice for the widest variety of circumstances (even though it's a departure
				// from the behavior in previous versions).  Another reason to check g_IsSuspended unconditionally
				// is for maintainability and code size reduction.  Finally, it's unlikely to significantly
				// impact performance since the vast majority of hotkeys have either one or just a few variants.
				if (vp.enabled // This particular variant within its parent hotkey is enabled.
						&& (!A_IsSuspended || vp.suspendExempt) // This variant isn't suspended...
						&& KeyboardMouseSender.HotInputLevelAllowsFiring(vp.inputLevel, extraInfo, ref singleChar) // ... its #InputLevel allows it to fire...
						&& (vp.hotCriterion == null || ((foundHwnd = HotCriterionAllowsFiring(vp.hotCriterion, Name)) != 0L))) // ... and its criteria allow it to fire.
				{
					if (vp.hotCriterion != null) // Since this is the first criteria hotkey, it takes precedence.
						return vp;

					//else since this is variant has no criteria, let the first criteria variant in the list
					// take precedence over it (if there is one).  If none is found, the vp_to_fire will stay
					// set as the non-criterion variant.
					vpToFire = vp;
				}
			}

			return vpToFire; // Either NULL or the variant found by the loop.
		}

		/// <summary>
		/// Returns the address of the variant in this hotkey whose criterion matches the current #HotIf criterion.
		/// If no match, it returns NULL.
		/// </summary>
		/// <returns></returns>
		internal HotkeyVariant FindVariant()
		{
			var tv = Script.TheScript.Threads.CurrentThread;

			for (var vp = firstVariant; vp != null; vp = vp.nextVariant)
				if (vp.hotCriterion == tv.hotCriterion)
					return vp;

			return null;
		}

		internal bool IsCompletelyDisabled()
		{
			if (hookAction != 0) // Alt tab hotkeys are disabled completely if and only if the parent is disabled.
				return !parentEnabled;

			for (var vp = firstVariant; vp != null; vp = vp.nextVariant)
				if (vp.enabled)
					return false;

			return true;
		}

		internal bool IsExemptFromSuspend() // A hotkey is considered exempt if even one of its variants is exempt.
		{
			// It's the caller's responsibility to check vp.mEnabled; that isn't done here.
			if (hookAction != 0) // An alt-tab hotkey (which overrides all its variants) is never exempt.
				return false;

			for (var vp = firstVariant; vp != null; vp = vp.nextVariant)
				if (vp.suspendExempt)
					return true; // Even a single exempt variant makes the entire hotkey exempt.

			// Otherwise, since the above didn't find any exempt variants, the entire hotkey is non-exempt:
			return false;
		}

		/// <summary>
		/// Caller is responsible for having called PerformIsAllowed() before calling us.
		/// Caller must have already created a new thread for us, and must close the thread when we return.
		/// </summary>
		/// <param name="variant"></param>
		/// <param name="critFoundHwnd"></param>
		/// <param name="lParamVal"></param>
		internal void PerformInNewThreadMadeByCallerAsync(HotkeyVariant variant, long critFoundHwnd, int lParamVal)
		{
			var script = Script.TheScript;
			var hkd = script.HotkeyData;

			if (script.HotkeyData.dialogIsDisplayed) // Another recursion layer is already displaying the warning dialog below.
				return;

			TimeSpan timeUntilNow;
			bool displayWarning;
			var ht = script.HookThread;

			if (hkd.timePrev == DateTime.MinValue)
				hkd.timePrev = DateTime.UtcNow;

			++hkd.throttledKeyCount;
			hkd.timeNow = DateTime.UtcNow;
			// Calculate the amount of time since the last reset of the sliding interval.
			// Note: A tickcount in the past can be subtracted from one in the future to find
			// the true difference between them, even if the system's uptime is greater than
			// 49 days and the future one has wrapped but the past one hasn't.  This is
			// due to the nature of DWORD subtraction.  The only time this calculation will be
			// unreliable is when the true difference between the past and future
			// tickcounts itself is greater than about 49 days:
			timeUntilNow = hkd.timeNow - hkd.timePrev;

			if (displayWarning = (hkd.throttledKeyCount > script.AccessorData.maxHotkeysPerInterval
								  && timeUntilNow.TotalMilliseconds < script.AccessorData.hotkeyThrottleInterval))
			{
				// The moment any dialog is displayed, hotkey processing is halted since this
				// app currently has only one thread.
				var error_text = $"{hkd.throttledKeyCount} hotkeys have been received in the last {timeUntilNow.TotalMilliseconds}ms.\n\nDo you want to continue?\n(see A_MaxHotkeysPerInterval in the help file)";  // In case it's stuck in a loop.
				// Turn off any RunAgain flags that may be on, which in essence is the same as de-buffering
				// any pending hotkey keystrokes that haven't yet been fired:
				ResetRunAgainAfterFinished();
				// This is now needed since hotkeys can still fire while a messagebox is displayed.
				// Seems safest to do this even if it isn't always necessary:
				hkd.dialogIsDisplayed = true;
				script.FlowData.allowInterruption = false;

				if (Dialogs.MsgBox(error_text, null, "YesNo") == DialogResult.No.ToString())
					_ = Flow.ExitAppInternal(Flow.ExitReasons.Close, null, false);// Might not actually Exit if there's an OnExit function.

				script.FlowData.allowInterruption = true;
				hkd.dialogIsDisplayed = false;
			}

			// The display_warning var is needed due to the fact that there's an OR in this condition:
			if (displayWarning || timeUntilNow.TotalMilliseconds > script.AccessorData.hotkeyThrottleInterval)
			{
				// Reset the sliding interval whenever it expires.  Doing it this way makes the
				// sliding interval more sensitive than alternate methods might be.
				// Also reset it if a warning was displayed, since in that case it didn't expire.
				hkd.throttledKeyCount = 0;
				hkd.timePrev = hkd.timeNow;
			}

			// At this point, even though the user chose to continue, it seems safest
			// to ignore this particular hotkey event since it might be WinClose or some
			// other command that would have unpredictable results due to the displaying
			// of the dialog itself.
			if (displayWarning)
				return;

			VariadicFunction vf = (o) =>
			{
				if (MouseUtils.IsWheelVK(vk)) // If this is true then also: msg.message==AHK_HOOK_HOTKEY
					A_EventInfo = (long)Conversions.LowWord(lParamVal); // v1.0.43.03: Override the thread default of 0 with the number of notches by which the wheel was turned.

				A_SendLevel = variant.inputLevel;
				var script = Script.TheScript;
				var tv = script.Threads.CurrentThread;
				tv.hwndLastUsed = new nint(critFoundHwnd);
				tv.hotCriterion = variant.hotCriterion;
				object ret = null;
				var ok = Flow.TryCatch(() => ret = variant.callback.Call(o), false, (false, null));

				if (!ok)
					variant.runAgainAfterFinished = false;  // Ensure this is reset due to the error.

				_ = Interlocked.Decrement(ref variant.existingThreads);

				if (variant.runAgainAfterFinished)
				{
					// But MsgSleep() can change it back to true again, when called by the above call
					// to ExecUntil(), to keep it auto-repeating:
					variant.runAgainAfterFinished = false;  // i.e. this "run again" ticket has now been used up.

					if ((DateTime.UtcNow - variant.runAgainTime).TotalMilliseconds <= 1000)
					{
						// v1.0.44.14: Post a message rather than directly running the above ExecUntil again.
						// This fixes unreported bugs in previous versions where the thread isn't reinitialized before
						// the launch of one of these buffered hotkeys, which caused settings such as SetKeyDelay
						// not to start off at their defaults.  Also, there are quite a few other things that the main
						// event loop does to prep for the launch of a hotkey.  Rather than copying them here or
						// trying to put them into a shared function (which would be difficult due to their nature),
						// it's much more maintainable to post a message, and in most cases, it shouldn't measurably
						// affect response time (this feature is rarely used anyway).
						//Some hotkeys will be using the hook and others will be using the built in Windows hotkey handler.
						//Sending a message will work for both cases.
						_ = PostHotkeyMessage(script.MainWindowHandle, id, 0);
					}

					//else it was posted too long ago, so don't do it.  This is because most users wouldn't
					// want a buffered hotkey to stay pending for a long time after it was pressed, because
					// that might lead to unexpected behavior.
				}

				return ret;
			};

			try
			{
				// This is stored as an attribute of the script (semi-globally) rather than passed
				// as a parameter to ExecUntil (and from their on to any calls to SendKeys() that it
				// makes) because it's possible for SendKeys to be called asynchronously, namely
				// by a timed subroutine, while A_HotkeyModifierTimeout is still in effect,
				// in which case we would want SendKeys() to take note of these modifiers even
				// if it was called from an ExecUntil() other than ours here:
				ht.kbdMsSender.thisHotkeyModifiersLR = modifiersConsolidatedLR;
				script.SetHotNamesAndTimes(Name);
				//This is the thread count for this particular hotkey only and must come before the thread is actually launched.
				//It will be decremented within the VariadicFunction above after the callback is called.
				_ = Interlocked.Increment(ref variant.existingThreads);
				script.Threads.LaunchInThread(variant.priority, false, false, vf, [Name], false);
			}
			catch (Error ex)
			{
				_ = Dialogs.MsgBox($"Exception thrown during hotkey handler.\n\n{ex}", null, "iconx");
			}
		}

		internal ResultType Register()
		{
			if (isRegistered)
				return ResultType.Ok;  // It's normal for a disabled hotkey to return OK.

			if (type != HotkeyTypeEnum.Normal) // Caller normally checks this for performance, but it's checked again for maintainability.
				return ResultType.Fail; // Don't attempt to register joystick or hook hotkeys, since their VK/modifiers aren't intended for that.

			// Indicate that the key modifies itself because RegisterHotkey() requires that +SHIFT,
			// for example, be used to register the naked SHIFT key.  So what we do here saves the
			// user from having to specify +SHIFT in the script:
			var key = (Keys)vk;
			var modifiersToRegister = modifiers;
			var script = Script.TheScript;

			switch (key)
			{
				case Keys.LWin:
				case Keys.RWin: modifiersToRegister |= MOD_WIN; break;

				case Keys.ControlKey: modifiersToRegister |= MOD_CONTROL; break;

				case Keys.ShiftKey: modifiersToRegister |= MOD_SHIFT; break;

				case Keys.Menu: modifiersToRegister |= MOD_ALT; break;
			}

			// Must register them to our main window (i.e. don't use NULL to indicate our thread),
			// otherwise any modal dialogs, such as MessageBox(), that call DispatchMessage()
			// internally wouldn't be able to find anyone to send hotkey messages to, so they
			// would probably be lost:
			return (isRegistered = RegisterHotKey(script.MainWindowHandle, id, (KeyModifiers)modifiersToRegister, vk))
				   ? ResultType.Ok
				   : ResultType.Fail;
			// Above: On failure, reset the modifiers in case this function changed them.  This is
			// done in case this hotkey will now be handled by the hook, which doesn't want any
			// extra modifiers that were added above.
			// UPDATE: In v1.0.42, the mModifiers value is never changed here because a hotkey that
			// gets registered here one time might fail to be registered some other time (perhaps due
			// to Suspend, followed by some other app taking ownership that hotkey, followed by
			// de-suspend, which would then have a hotkey with the wrong modifiers in it for the hook.
		}

		/// <summary>
		/// Return the text description of this hotkey.
		/// </summary>
		internal string ToText(bool appendNewline)
		{
			HotkeyVariant vp;
			int existingThreads;

			for (existingThreads = 0, vp = firstVariant; vp != null; vp = vp.nextVariant)
				existingThreads += vp.existingThreads;

			var existingThreadsStr = existingThreads != 0 ? existingThreads.ToString() : "";

			string htype = type switch
		{
				HotkeyTypeEnum.Normal => "reg",
				HotkeyTypeEnum.KeyboardHook => "k-hook",
				HotkeyTypeEnum.MouseHook => "m-hook",
				HotkeyTypeEnum.BothHook => "2-hooks",
				HotkeyTypeEnum.Joystick => "joypoll",
				_ => "",
		};

		string enabledStr;

		if (IsCompletelyDisabled()) // Takes into account alt-tab vs. non-alt-tab, etc.
				enabledStr = "OFF";
			else if (hookAction != 0 && parentEnabled) // It's completely "on" in this case.
				enabledStr = "";
			else // It's on or if all individual variants are on, otherwise it's partial.
			{
				// Set default: Empty string means "ON" because it reduces clutter in the displayed list.
				for (enabledStr = "", vp = firstVariant; vp != null; vp = vp.nextVariant)
					if (!vp.enabled)
					{
						enabledStr = "PART";
						break;
					}
			}

			string levelStr;
			long minLevel = 100L, maxLevel = 0L;

			for (vp = firstVariant; vp != null; vp = vp.nextVariant)
			{
				if (minLevel > vp.inputLevel)
					minLevel = vp.inputLevel;

				if (maxLevel < vp.inputLevel)
					maxLevel = vp.inputLevel;
			}

			if (minLevel != maxLevel)
				levelStr = $"{minLevel}-{maxLevel}";
			else if (minLevel != 0)
				levelStr = minLevel.ToString();
			else // Show nothing for level 0.
				levelStr = "";

			var buf = $"{htype}{((type == HotkeyTypeEnum.Normal && !isRegistered) ? "(no)" : "")}\t{enabledStr}\t{levelStr}\t{existingThreadsStr}\t{Name}";

			if (appendNewline)
				buf += "\r\n";

			return buf;
		}

		internal ResultType Unregister()
		{
			if (!isRegistered)
				return ResultType.Ok;

			var script = Script.TheScript;
			var mw = script.mainWindow;

			// Don't report any errors in here, at least not when we were called in conjunction
			// with cleanup and exit.  Such reporting might cause an infinite loop, leading to
			// a stack overflow if the reporting itself encounters an error and tries to exit,
			// which in turn would call us again:
			if (mw != null && mw.IsDisposed)
			{
				isRegistered = false;
				return ResultType.Ok;
			}

			var handle = script.MainWindowHandle;
			mw?.Invoke(() =>
			{
				_ = UnregisterHotKey(handle, id);
			});
			return isRegistered ? ResultType.Ok : ResultType.Fail;//I've see it fail in one rare case.
		}

		private static IFuncObj FindHotkeyIf(IFuncObj fo, List<IFuncObj> list)
		{
			if (fo == null)
				return null;

			foreach (var cp in list)
			{
				if (cp != null)
				{
					if (cp == fo)
						return cp;

					var bf1 = cp as BoundFunc;
					var bf2 = fo as BoundFunc;

					if (bf1 != null ^ bf2 != null)
						continue;

					if (bf1 != null && bf2 != null)
					{
						if (bf1.boundargs != null ^ bf2.boundargs != null)
							goto keeptrying;

						if (bf1.boundargs != null && bf2.boundargs != null)
						{
							if (bf1.boundargs.Length != bf2.boundargs.Length)
								goto keeptrying;

							for (var i = 0; i < bf1.boundargs.Length; i++)
								if (!bf1.boundargs[i].Equals(bf2.boundargs[i]))
									goto keeptrying;
						}
					}

					if (cp.Name == fo.Name && ReferenceEquals(cp.Inst, fo.Inst))
						return cp;
				}

				keeptrying:
				;
			}

			return null;
		}

		private static bool HotIfWinActivePrivate(object title, object text, object hotkey) => WindowSearch.SearchWindow(title, text, null, null, false) is WindowItemBase win && win.Active;

		private static bool HotIfWinExistPrivate(object title, object text, object hotkey) => WindowSearch.SearchWindow(title, text, null, null, false) is WindowItemBase win && win.Exists;

		private static bool HotIfWinNotActivePrivate(object title, object text, object hotkey) => !HotIfWinActivePrivate(title, text, hotkey);

		private static bool HotIfWinNotExistPrivate(object title, object text, object hotkey) => !HotIfWinExistPrivate(title, text, hotkey);

		internal static object SetupHotIfWin(string funcname, object obj0 = null, object obj1 = null)
		{
			var script = Script.TheScript;

			if (obj0 != null || obj1 != null)
			{
				var mi = typeof(HotkeyDefinition).GetMethod(funcname, BindingFlags.NonPublic | BindingFlags.Static);
				var fo = new FuncObj(mi);
				var bf = fo.Bind(obj0 ?? "", obj1 ?? "");//Must not pass null so that the logic in BoundFunc.Call() works.

				if (FindHotkeyCriterion(bf) == null)
					AddHotkeyCriterion(bf);

				script.Threads.CurrentThread.hotCriterion = bf;
			}
			else
				script.Threads.CurrentThread.hotCriterion = null;

			return null;
		}

		private bool Disable(HotkeyVariant aVariant) // Returns true if the variant needed to be disabled, in which case caller should generally call ManifestAllHotkeysHotstringsHooks().
		{
			if (!aVariant.enabled) // Added for v1.0.23 to greatly improve performance when hotkey is already in the right state.
				return false; // Indicate that it's already disabled.

			aVariant.enabled = false;
			aVariant.runAgainAfterFinished = false; // ManifestAllHotkeysHotstringsHooks() won't do this unless the entire hotkey is disabled/unregistered.
			return true;
		}

		private bool DisableParent() // Returns true if the hotkey needed to be disabled, in which case caller should generally call ManifestAllHotkeysHotstringsHooks().
		{
			if (!parentEnabled)
				return false; // Indicate that it's already disabled.

			parentEnabled = false;
			return true;
		}

		private bool Enable(HotkeyVariant aVariant) // Returns true if the variant needed to be disabled, in which case caller should generally call ManifestAllHotkeysHotstringsHooks().
		{
			if (aVariant.enabled) // Added for v1.0.23 to greatly improve performance when hotkey is already in the right state.
				return false; // Indicate that it's already enabled.

			aVariant.enabled = true;
			return true;
		}

		private bool EnableParent() // Returns true if the hotkey needed to be disabled, in which case caller should generally call ManifestAllHotkeysHotstringsHooks().
		{
			if (parentEnabled)
				return false; // Indicate that it's already enabled.

			parentEnabled = true;
			return true;
		}

		[Flags]
		internal enum Options
		{ None = 0, IgnoreModifiers = 1, PassThrough = 2, Up = 4 }
	}

	internal class HkSortedType
	{
		internal bool allowExtraModifiers = false;
		internal uint idWithFlags = 0u;
		internal uint modifiers = 0u;
		internal uint modifiersLR = 0u;
		internal uint sc = 0u;
		internal uint vk = 0u;

		/// <summary>
		/// The only items whose order are important are those with the same suffix.  For a given suffix,
		/// we want the most general modifiers (e.g. CTRL) to appear closer to the top of the list than
		/// those with more specific modifiers (e.g. CTRL-ALT).  To make qsort() perform properly, it seems
		/// best to sort by vk/sc then by generality.
		/// </summary>
		internal static int SortMostGeneralBeforeLeast(HkSortedType b1, HkSortedType b2)

		{
			if (b1.vk != b2.vk)
				return (int)b1.vk - (int)b2.vk;

			if (b1.sc != b2.sc)
				return (int)b1.sc - (int)b2.sc;

			// If the above didn't return, we now know that a1 and a2 have the same vk's or sc's.  So
			// we use a tie-breaker to cause the most general keys to appear closer to the top of the
			// list than less general ones.  This should result in a given suffix being grouped together
			// after the sort.  Within each suffix group, the most general modifiers should appear first.

			// This part is basically saying that keys that don't allow extra modifiers can always be processed
			// after all other keys:
			if (b1.allowExtraModifiers && !b2.allowExtraModifiers)
				return -1;  // Indicate that a1 is smaller, so that it will go to the top.

			if (!b1.allowExtraModifiers && b2.allowExtraModifiers)
				return 1;

			// However the order of suffixes that don't allow extra modifiers, among themselves, may be important.
			// Thus we don't return a zero if both have AllowExtraModifiers = 0.
			// Example: User defines ^a, but also defines >^a.  What should probably happen is that >^a forces ^a
			// to fire only when <^a occurs.
			// v1.1.33.03: Compare number of modifiers.  This supersedes some previous checks for when
			// a1's modifiers are a subset of a2's or vice versa (since the subset would always have
			// fewer bits).  This new method helps prioritize combinations which overlap but have a
			// different number of modifiers, such as "*<^a" vs. "*<^>^a".
			var nmodLR_a1 = System.Numerics.BitOperations.PopCount(b1.modifiersLR);
			var nmodLR_a2 = System.Numerics.BitOperations.PopCount(b2.modifiersLR);
			var nmod_a1 = System.Numerics.BitOperations.PopCount(b1.modifiers) + nmodLR_a1;
			var nmod_a2 = System.Numerics.BitOperations.PopCount(b2.modifiers) + nmodLR_a2;

			if (nmod_a1 != nmod_a2)
				return nmod_a1 - nmod_a2;

			if (nmodLR_a1 != nmodLR_a2)
				return nmodLR_a1 - nmodLR_a2;

			// v1.1.33.03: Sort by modifier value so that key-up hotkeys end up immediately after their
			// counterparts, otherwise we get odd results like Alt+Shift+A firing "*!a" and "*+a up"
			// instead of "*!a" and "*!a up" or "*+a" and "*+a up".
			if (b1.modifiers != b2.modifiers)
				return (int)b1.modifiers - (int)b2.modifiers; // !^+#

			if (b1.modifiersLR != b2.modifiersLR)
				return (int)b1.modifiersLR - (int)b2.modifiersLR; // <^>^<!>!<+>+<#>#

			// v1.0.38.03: The following check is added to handle a script containing hotkeys
			// such as the following (in this order):
			// *MButton::
			// *Mbutton Up::
			// MButton::
			// MButton Up::
			// What would happen before is that the qsort() would sometimes cause "MButton Up" from the
			// list above to be processed prior to "MButton", which would set hotkey_up[*MButton's ID]
			// to be MButton Up's ID.  Then when "MButton" was processed, it would set its_table_entry
			// to MButton's ID, but hotkey_up[MButton's ID] would be wrongly left INVALID when it should
			// have received a copy of the asterisk hotkey ID's counterpart key-up ID.  However, even
			// giving it a copy would not be quite correct because then *MButton's ID would wrongly
			// be left associated with MButton's Up's ID rather than *MButton Up's.  By solving the
			// problem here in the sort rather than copying the ID, both bugs are resolved.
			// v1.1.33.03: The scenario above would now also be prevented by checks in ChangeHookState
			// which avoid pairing a key-up hotkey with a more permissive key-down hotkey, but keeping
			// this might help ensure key-up hotkeys are matched optimally when there is overlap.
			//if ((b1.id_with_flags & HOTKEY_KEY_UP) != (b2.id_with_flags & HOTKEY_KEY_UP))
			//  return (b1.id_with_flags & HOTKEY_KEY_UP) ? 1 : -1; // Put key-up hotkeys higher in the list than their down counterparts (see comment above).
			// v1.1.33.03: Getting to this point should mean that a1 and a2 have the same modifiers,
			// vk and sc, but they might have different up/down status and key name (Esc/Escape/vk1B).
			// Ensure predictability by putting them in an order based on id_with_flags.
			return (int)b1.idWithFlags - (int)b2.idWithFlags;
		}
	}

	internal class HotkeyData
	{
		internal HookType whichHookAlways = HookType.None;
		internal bool dialogIsDisplayed;
		internal uint joyHotkeyCount;
		internal bool[] joystickHasHotkeys = new bool[JoystickData.MaxJoysticks];
		internal List<HotkeyDefinition> shk = new (256);
		internal uint throttledKeyCount;
		internal DateTime timeNow;
		internal DateTime timePrev = DateTime.MinValue;
		internal HookType whichHookNeeded = HookType.None;
	}

	/// <summary>
	/// Used by TextToModifiers() and its callers.
	/// </summary>
	internal class HotkeyProperties
	{
		internal bool hasAsterisk;
		internal bool hookIsMandatory;
		internal bool isKeyUp;
		internal uint modifiers;
		internal uint modifiersLR;
		internal bool prefixHasTilde;
		internal string prefixText = "";
		internal bool suffixHasTilde;//As opposed to "prefix has tilde".
		internal string suffixText = "";

		internal void Reset()
		{
			modifiers = 0u;
			modifiersLR = 0u;
			prefixHasTilde = false;
			suffixHasTilde = false;
			hasAsterisk = false;
			isKeyUp = false;
			hookIsMandatory = false;
			prefixText = "";
			suffixText = "";
		}
	}

	internal class HotkeyVariant
	{
		internal IFuncObj callback;
		internal bool enabled;
		internal int existingThreads;
		internal IFuncObj hotCriterion;
		internal int index;
		internal long inputLevel;
		internal long maxThreads = long.MaxValue;//Don't really care about max threads in Keysharp, so just make it a huge number.
		internal bool maxThreadsBuffer;
		internal HotkeyVariant nextVariant;
		internal uint noSuppress;
		internal IFuncObj originalCallback;   // This is the callback set at load time.
		internal int priority;
		internal bool runAgainAfterFinished;
		internal DateTime runAgainTime;
		internal bool suspendExempt;

		/// <summary>
		/// For now, attempts to launch another simultaneous instance of this subroutine
		/// are ignored if MaxThreadsPerHotkey (for this particular hotkey) has been reached.
		/// In the future, it might be better to have this user-configurable, i.e. to devise
		/// some way for the hotkeys to be kept queued up so that they take effect only when
		/// the number of currently active threads drops below the max.  But doing such
		/// might make "infinite key loops" harder to catch because the rate of incoming hotkeys
		/// would be slowed down to prevent the subroutines from running concurrently:
		/// </summary>
		/// <param name="variant"></param>
		/// <returns></returns>
		internal bool AnyThreadsAvailable() => existingThreads < maxThreads;

		internal void RunAgainAfterFinished()
		{
			if (maxThreadsBuffer)
				runAgainAfterFinished = true;

			runAgainTime = DateTime.UtcNow;
		}
	}

	internal enum HotCriterionEnum
	{
		NoCriterion,//Must be first
		IfActive,
		IfNotActive,
		IfExist,
		IfNotExist,
		IfCallback
	}

	internal enum HotkeyTypeEnum
	{ Normal, KeyboardHook, MouseHook, BothHook, Joystick }
}
