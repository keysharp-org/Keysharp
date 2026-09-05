using Keysharp.Internals.Input.Keyboard;
using static Keysharp.Internals.Input.Keyboard.KeyboardUtils;
using static Keysharp.Internals.Input.Keyboard.VirtualKeys;

namespace Keysharp.Internals
{
	internal sealed class DefaultKeyboard : IKeyboard
	{
		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
		{
			mods = 0u;
			return false;
		}

		public bool TryGetModifierLRStatePhysical(out uint mods)
		{
			mods = 0u;
			return false;
		}

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
		{
			isDown = false;
			return false;
		}

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
		{
			isDown = false;
			return false;
		}

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			capsOn = numOn = scrollOn = false;
			return false;
		}
	}

#if WINDOWS
	internal sealed class WindowsKeyboard : IKeyboard
	{
		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
		{
			mods = 0u;

			if (TryGetKeyStateLogical(VK_LSHIFT, out var down) && down) mods |= MOD_LSHIFT;
			if (TryGetKeyStateLogical(VK_RSHIFT, out down) && down) mods |= MOD_RSHIFT;
			if (TryGetKeyStateLogical(VK_LCONTROL, out down) && down) mods |= MOD_LCONTROL;
			if (TryGetKeyStateLogical(VK_RCONTROL, out down) && down) mods |= MOD_RCONTROL;
			if (TryGetKeyStateLogical(VK_LMENU, out down) && down) mods |= MOD_LALT;
			if (TryGetKeyStateLogical(VK_RMENU, out down) && down) mods |= MOD_RALT;
			if (TryGetKeyStateLogical(VK_LWIN, out down) && down) mods |= MOD_LWIN;
			if (TryGetKeyStateLogical(VK_RWIN, out down) && down) mods |= MOD_RWIN;
			return true;
		}

		public bool TryGetModifierLRStatePhysical(out uint mods)
		{
			mods = 0u;
			return false;
		}

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
		{
			isDown = false;

			if (vk == 0 || vk > int.MaxValue)
				return false;

			isDown = (Keysharp.Internals.Os.Windows.WindowsAPI.GetAsyncKeyState((int)vk) & 0x8000) != 0;
			return true;
		}

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
		{
			isDown = false;
			return false;
		}

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			capsOn = (Keysharp.Internals.Os.Windows.WindowsAPI.GetKeyState((int)VK_CAPITAL) & 0x01) != 0;
			numOn = (Keysharp.Internals.Os.Windows.WindowsAPI.GetKeyState((int)VK_NUMLOCK) & 0x01) != 0;
			scrollOn = (Keysharp.Internals.Os.Windows.WindowsAPI.GetKeyState((int)VK_SCROLL) & 0x01) != 0;
			return true;
		}
	}
#endif

#if LINUX
	internal static class LinuxKeyboards
	{
		internal static IKeyboard Resolve()
			=> new LinuxKeyboard(!Platform.Desktop.IsWaylandSession && Platform.Desktop.IsX11Available
				? new X11Keyboard()
				: null);
	}

	/// <summary>
	/// Uses keysharp-input as the compositor-independent source of logical and physical keyboard state.
	/// Desktop module queries are an unprivileged fallback only for the fixed modifier and lock-toggle snapshots.
	/// </summary>
	internal sealed class LinuxKeyboard(IKeyboard fallback) : IKeyboard
	{
		private readonly NativeInputKeyboard nativeInput = new();

		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
		{
			if (nativeInput.TryGetModifierLRStateLogical(out mods, keymapBuffer))
				return true;

			if (fallback != null)
				return fallback.TryGetModifierLRStateLogical(out mods, keymapBuffer);

			mods = 0u;
			return false;
		}

		public bool TryGetModifierLRStatePhysical(out uint mods)
		{
			if (nativeInput.TryGetModifierLRStatePhysical(out mods))
				return true;

			if (fallback != null)
				return fallback.TryGetModifierLRStatePhysical(out mods);

			mods = 0u;
			return false;
		}

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
		{
			if (nativeInput.TryGetKeyStateLogical(vk, out isDown))
				return true;

			if (fallback != null && MayUseUnprivilegedKeyStateFallback(vk))
				return fallback.TryGetKeyStateLogical(vk, out isDown);

			isDown = false;
			return false;
		}

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
		{
			if (nativeInput.TryGetKeyStatePhysical(vk, out isDown))
				return true;

			if (fallback != null && MayUseUnprivilegedKeyStateFallback(vk))
				return fallback.TryGetKeyStatePhysical(vk, out isDown);

			isDown = false;
			return false;
		}

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			if (nativeInput.TryGetIndicatorStatesLogical(out capsOn, out numOn, out scrollOn))
				return true;

			if (fallback != null)
				return fallback.TryGetIndicatorStatesLogical(out capsOn, out numOn, out scrollOn);

			capsOn = numOn = scrollOn = false;
			return false;
		}

		internal static bool MayUseUnprivilegedKeyStateFallback(uint vk)
			=> IsModifierVk(vk);
	}

	internal sealed class NativeInputKeyboard : IKeyboard
	{
		internal const uint LLKHF_CAPS_LOCK_ON = 0x04u;
		internal const uint LLKHF_NUM_LOCK_ON = 0x08u;
		internal const uint LLKHF_SCROLL_LOCK_ON = 0x40u;
		private static volatile bool indicatorSnapshotValid;
		private static bool indicatorSnapshotCaps;
		private static bool indicatorSnapshotNum;
		private static bool indicatorSnapshotScroll;

		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
			=> Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetModifierState(
				out mods, out _, out _, out _, out _);

		public bool TryGetModifierLRStatePhysical(out uint mods)
			=> Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetModifierState(
				out _, out mods, out _, out _, out _);

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
		{
			isDown = false;

			if (vk == 0)
				return false;

			var modifierMask = ModifierLRMaskFromVK(vk);

			if (modifierMask != 0)
			{
				if (!Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetModifierState(
						out var modifierState, out _, out _, out _, out _))
					return false;

				isDown = (modifierState & modifierMask) != 0;
				return true;
			}

			if (!Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetKeyState(
				out var mods, out _, out var numLock, out _, out var logicalKeys, out _))
				return false;

			var shiftDown = (mods & (MOD_LSHIFT | MOD_RSHIFT)) != 0;
			return TryGetVkFromEvdevBitmap(vk, logicalKeys, numLock, shiftDown, out isDown);
		}

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
		{
			isDown = false;

			if (vk == 0)
				return false;

			var modifierMask = ModifierLRMaskFromVK(vk);

			if (modifierMask != 0)
			{
				if (!Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetModifierState(
						out _, out var modifierState, out _, out _, out _))
					return false;

				isDown = (modifierState & modifierMask) != 0;
				return true;
			}

			if (!Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetKeyState(
				out var mods, out _, out var numLock, out _, out _, out var physicalKeys))
				return false;

			var shiftDown = (mods & (MOD_LSHIFT | MOD_RSHIFT)) != 0;
			return TryGetVkFromEvdevBitmap(vk, physicalKeys, numLock, shiftDown, out isDown);
		}

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			if (Keysharp.Internals.Input.Hooks.Linux.LinuxHookThread.IsInHookCallback && TryGetIndicatorSnapshot(out capsOn, out numOn, out scrollOn))
				return true;

			return Keysharp.Internals.Input.Linux.KeysharpInputManager.TryGetModifierState(
				out _, out _, out capsOn, out numOn, out scrollOn);
		}

		internal static bool HookFlagsNumLockOn(uint flags) => (flags & LLKHF_NUM_LOCK_ON) != 0;

		internal static void UpdateIndicatorSnapshotFromHookFlags(uint flags)
		{
			indicatorSnapshotCaps = (flags & LLKHF_CAPS_LOCK_ON) != 0;
			indicatorSnapshotNum = (flags & LLKHF_NUM_LOCK_ON) != 0;
			indicatorSnapshotScroll = (flags & LLKHF_SCROLL_LOCK_ON) != 0;
			indicatorSnapshotValid = true;
		}

		private static bool TryGetIndicatorSnapshot(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			if (indicatorSnapshotValid)
			{
				capsOn = indicatorSnapshotCaps;
				numOn = indicatorSnapshotNum;
				scrollOn = indicatorSnapshotScroll;
				return true;
			}

			capsOn = false;
			numOn = false;
			scrollOn = false;
			return false;
		}

		private static bool TryGetEvdevBit(byte[] keys, uint evdev, out bool isDown)
		{
			isDown = false;

			if (keys == null || evdev >= keys.Length * 8u)
				return false;

			isDown = (keys[evdev >> 3] & (1 << ((int)evdev & 7))) != 0;
			return true;
		}

		private static bool TryGetVkFromEvdevBitmap(uint vk, byte[] keys, out bool isDown)
			=> TryGetVkFromEvdevBitmap(vk, keys, numLockOn: false, shiftDown: false, out isDown);

		private static bool TryGetVkFromEvdevBitmap(
			uint vk,
			byte[] keys,
			bool numLockOn,
			bool shiftDown,
			out bool isDown)
		{
			isDown = false;

			switch (vk)
			{
				case VK_SHIFT:
					if (!TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_LSHIFT), out var lShift)
						|| !TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_RSHIFT), out var rShift))
						return false;
					isDown = lShift || rShift;
					return true;
				case VK_CONTROL:
					if (!TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_LCONTROL), out var lCtrl)
						|| !TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_RCONTROL), out var rCtrl))
						return false;
					isDown = lCtrl || rCtrl;
					return true;
				case VK_MENU:
					if (!TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_LMENU), out var lAlt)
						|| !TryGetEvdevBit(keys, KeyCodes.VkToEvdev(VK_RMENU), out var rAlt))
						return false;
					isDown = lAlt || rAlt;
					return true;
			}

			// A missing bitmap is not a valid all-keys-up snapshot.
			if (keys == null || keys.Length == 0)
				return false;

			// A VK can be produced by several evdev codes (KEY_COMPOSE/KEY_MENU both mean VK_APPS), so every code
			// that maps back to it is checked rather than just the canonical one.
			var codes = KeyCodes.ScanCodesForVk(vk);

			if (codes.Length == 0)
				return false;

			// The hook reports the NumLock/Shift-adjusted VK, so a keypad key whose name is currently folded into
			// its navigation twin is never down under its own name, and the twin answers for both.
			if (KeyCodes.ApplyNumpadState(vk, numLockOn, shiftDown) != vk)
				return true;

			var twin = KeyCodes.NumpadTwinVk(vk, numLockOn, shiftDown);
			isDown = AnyEvdevBitSet(keys, codes)
					 || (twin != 0 && AnyEvdevBitSet(keys, KeyCodes.ScanCodesForVk(twin)));
			return true;
		}

		private static bool AnyEvdevBitSet(byte[] keys, ReadOnlySpan<uint> evdevCodes)
		{
			foreach (var evdev in evdevCodes)
				if (TryGetEvdevBit(keys, evdev, out var down) && down)
					return true;

			return false;
		}
	}

	internal sealed class X11Keyboard : IKeyboard
	{
		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
		{
			var snapshot = Keysharp.Internals.Input.Linux.DesktopKeyboardState.X11.Get();
			mods = snapshot?.Modifiers ?? 0;
			return snapshot?.ModifiersKnown ?? false;
		}

		public bool TryGetModifierLRStatePhysical(out uint mods)
		{
			mods = 0;
			return false;
		}

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
		{
			isDown = false;
			var mask = vk switch
			{
				VK_SHIFT => MOD_LSHIFT | MOD_RSHIFT,
				VK_CONTROL => MOD_LCONTROL | MOD_RCONTROL,
				VK_MENU => MOD_LALT | MOD_RALT,
				VK_LSHIFT => MOD_LSHIFT,
				VK_RSHIFT => MOD_RSHIFT,
				VK_LCONTROL => MOD_LCONTROL,
				VK_RCONTROL => MOD_RCONTROL,
				VK_LMENU => MOD_LALT,
				VK_RMENU => MOD_RALT,
				VK_LWIN => MOD_LWIN,
				VK_RWIN => MOD_RWIN,
				_ => 0u
			};
			if (mask == 0 || !TryGetModifierLRStateLogical(out var mods)) return false;
			isDown = (mods & mask) != 0;
			return true;
		}

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
		{
			isDown = false;
			return false;
		}

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			var snapshot = Keysharp.Internals.Input.Linux.DesktopKeyboardState.X11.Get();
			capsOn = snapshot?.CapsLock ?? false;
			numOn = snapshot?.NumLock ?? false;
			scrollOn = snapshot?.ScrollLock ?? false;
			return snapshot?.IndicatorsKnown ?? false;
		}
	}

#endif

#if OSX
	internal sealed class MacKeyboard : IKeyboard
	{
		private const ulong AlphaShiftKeyMask = 1UL << 16;
		private static volatile bool indicatorSnapshotValid;
		private static bool indicatorSnapshotNum;
		private static bool indicatorSnapshotScroll;

		public bool TryGetModifierLRStateLogical(out uint mods, byte[] keymapBuffer = null)
			=> TryQueryModifierLRStateForSource(Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateCombinedSessionState, out mods);

		public bool TryGetModifierLRStatePhysical(out uint mods)
			=> TryQueryModifierLRStateForSource(Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateHIDSystemState, out mods);

		public bool TryGetKeyStateLogical(uint vk, out bool isDown)
			=> TryQueryMacKeyState(vk, Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateCombinedSessionState, useIndicators: true, out isDown);

		public bool TryGetKeyStatePhysical(uint vk, out bool isDown)
			=> TryQueryMacKeyState(vk, Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateHIDSystemState, useIndicators: false, out isDown);

		public bool TryGetIndicatorStatesLogical(out bool capsOn, out bool numOn, out bool scrollOn)
		{
			numOn = false;
			scrollOn = false;

			if (indicatorSnapshotValid)
			{
				numOn = indicatorSnapshotNum;
				scrollOn = indicatorSnapshotScroll;
			}

			if (Keysharp.Internals.Input.MacOS.MacCapsLockState.TryGet(out capsOn))
				return true;

			if (TryGetCurrentModifierFlags(Keysharp.Internals.Input.MacOS.MacNativeInput.kCGEventSourceStateCombinedSessionState, out var flags))
			{
				capsOn = (flags & AlphaShiftKeyMask) != 0;
				return true;
			}

			capsOn = false;
			return false;
		}

		internal static void UpdateIndicatorSnapshotFromMask(Keysharp.Internals.Input.Hooks.EventMask mask)
		{
			indicatorSnapshotNum = (mask & Keysharp.Internals.Input.Hooks.EventMask.NumLock) != 0;
			indicatorSnapshotScroll = (mask & Keysharp.Internals.Input.Hooks.EventMask.ScrollLock) != 0;
			indicatorSnapshotValid = true;
		}

		private bool TryQueryMacKeyState(uint vk, uint sourceState, bool useIndicators, out bool isDown)
		{
			isDown = false;

			if (vk == 0)
				return false;

			if (vk is VK_SHIFT or VK_CONTROL or VK_MENU)
			{
				var (left, right) = vk switch
				{
					VK_SHIFT => (VK_LSHIFT, VK_RSHIFT),
					VK_CONTROL => (VK_LCONTROL, VK_RCONTROL),
					_ => (VK_LMENU, VK_RMENU)
				};

				var leftOk = Keysharp.Internals.Input.MacOS.MacKeyboardState.TryQuery(left, sourceState, out var leftDown);
				var rightOk = Keysharp.Internals.Input.MacOS.MacKeyboardState.TryQuery(right, sourceState, out var rightDown);
				isDown = leftDown || rightDown;
				return leftOk && rightOk;
			}

			if (useIndicators && (vk == VK_CAPITAL || vk == VK_NUMLOCK || vk == VK_SCROLL))
			{
				if (TryGetIndicatorStatesLogical(out var capsOn, out var numOn, out var scrollOn))
				{
					isDown = vk switch
					{
						VK_CAPITAL => capsOn,
						VK_NUMLOCK => numOn,
						VK_SCROLL => scrollOn,
						_ => false
					};
					return true;
				}
			}

			try
			{
				// A portable VK can have more than one physical kVK code (Return/keypad Enter and
				// ANSI/keypad equals are common examples). Query every code that maps back to it so
				// GetKeyState reflects the complete key, while GetKeySC still reports the primary one.
				var codes = KeyCodes.ScanCodesForVk(vk);

				foreach (var macCode in codes)
				{
					if (Keysharp.Internals.Input.MacOS.MacNativeInput.CGEventSourceKeyState(sourceState, (ushort)macCode))
					{
						isDown = true;
						break;
					}
				}

				return codes.Length != 0;
			}
			catch
			{
				isDown = false;
				return false;
			}
		}

		private static bool TryQueryModifierLRStateForSource(uint sourceState, out uint mods)
		{
			mods = 0u;
			var success = true;

			foreach (var vk in ModifierLRVks)
			{
				if (!Keysharp.Internals.Input.MacOS.MacKeyboardState.TryQuery(vk, sourceState, out var down))
					success = false;
				else if (down)
					mods |= ModifierLRMaskFromVK(vk);
			}

			return success;
		}

		private static bool TryGetCurrentModifierFlags(uint sourceState, out ulong flags)
		{
			try
			{
				flags = Keysharp.Internals.Input.MacOS.MacNativeInput.CGEventSourceFlagsState(sourceState);
				return true;
			}
			catch
			{
				flags = 0;
				return false;
			}
		}
	}
#endif
}
