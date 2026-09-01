#if !WINDOWS
namespace Keysharp.Internals.Input.Keyboard
{
	/// <summary>
	/// VK/SC dispatch for the non-Windows backends. On Linux SC is an input service evdev
	/// code; on macOS SC is the kVK code.
	/// </summary>
	internal static partial class KeyCodes
	{
		// The dual-state numpad keys: each keypad key and the navigation key it produces while NumLock and Shift
		// cancel out. VK_CLEAR/Numpad5 is Linux-only — macOS has a dedicated keypad Clear and its keypad always
		// reports digits, so there is no pair to fold there.
		private static readonly (uint NavigationVk, uint KeypadVk)[] numpadNavigationKeys =
		[
			(VirtualKeys.VK_INSERT, VirtualKeys.VK_NUMPAD0),
			(VirtualKeys.VK_END, VirtualKeys.VK_NUMPAD1),
			(VirtualKeys.VK_DOWN, VirtualKeys.VK_NUMPAD2),
			(VirtualKeys.VK_NEXT, VirtualKeys.VK_NUMPAD3),
			(VirtualKeys.VK_LEFT, VirtualKeys.VK_NUMPAD4),
#if LINUX
			(VirtualKeys.VK_CLEAR, VirtualKeys.VK_NUMPAD5),
#endif
			(VirtualKeys.VK_RIGHT, VirtualKeys.VK_NUMPAD6),
			(VirtualKeys.VK_HOME, VirtualKeys.VK_NUMPAD7),
			(VirtualKeys.VK_UP, VirtualKeys.VK_NUMPAD8),
			(VirtualKeys.VK_PRIOR, VirtualKeys.VK_NUMPAD9),
			(VirtualKeys.VK_DELETE, VirtualKeys.VK_DECIMAL),
		];

		private static readonly Lock scanCodesLock = new ();
		private static Dictionary<uint, uint[]> scanCodesByVk;

		private static bool TryGetNumpadKeyPair(uint vk, out uint navigationVk, out uint keypadVk)
		{
			foreach (var key in numpadNavigationKeys)
			{
				if (key.NavigationVk != vk && key.KeypadVk != vk)
					continue;

				navigationVk = key.NavigationVk;
				keypadVk = key.KeypadVk;
				return true;
			}

			navigationVk = keypadVk = 0;
			return false;
		}

		/// <summary>The VK a hook reports for <paramref name="vk"/> in the given NumLock/Shift state: a keypad key
		/// produces its navigation twin whenever the two cancel out, exactly as the Windows hook does.</summary>
		internal static uint ApplyNumpadState(uint vk, bool numLockOn, bool shiftDown)
		{
			if (numLockOn == shiftDown
				&& TryGetNumpadKeyPair(vk, out var navigationVk, out var keypadVk)
				&& vk == keypadVk)
				return navigationVk;

			return vk;
		}

		/// <summary>The inverse of <see cref="ApplyNumpadState"/>: the keypad VK currently reported under
		/// <paramref name="vk"/>'s name, or 0 when nothing folds into it in this state. A query about a navigation
		/// key has to answer for its keypad twin as well, since the hook no longer distinguishes them.</summary>
		internal static uint NumpadTwinVk(uint vk, bool numLockOn, bool shiftDown)
			=> numLockOn == shiftDown
			   && TryGetNumpadKeyPair(vk, out var navigationVk, out var keypadVk)
			   && navigationVk == vk
			? keypadVk
			: 0u;

		/// <summary>
		/// Every platform scan code that resolves back to <paramref name="vk"/>, in ascending order, or an empty
		/// span when no key maps to it. This is deliberately not <see cref="MapVkToSc"/>: that answers "which code
		/// names this key" (primary plus at most one secondary), while a key-state query has to consider every
		/// synonymous physical code the platform defines — evdev has several (KEY_MAIL/KEY_EMAIL, KEY_COMPOSE/
		/// KEY_MENU, KEY_PLAYPAUSE and its three aliases), and the Mac keycode space has Return/keypad Enter and
		/// ANSI/keypad equals. Built once by walking the code space through the same table the hook translates
		/// incoming events with, so a state query can never see a key the hook would report differently.
		/// </summary>
		internal static ReadOnlySpan<uint> ScanCodesForVk(uint vk)
		{
			var map = Volatile.Read(ref scanCodesByVk);

			if (map == null)
			{
				lock (scanCodesLock)
				{
					map = scanCodesByVk;

					if (map == null)
						Volatile.Write(ref scanCodesByVk, map = BuildScanCodesByVk());
				}
			}

			return map.TryGetValue(vk, out var codes) ? codes : default;
		}

		private static Dictionary<uint, uint[]> BuildScanCodesByVk()
		{
			var pending = new Dictionary<uint, List<uint>>();

			for (var sc = 0u; sc <= MaxScanCode; sc++)
			{
				if (!TryMapScanCodeToVk(sc, out var vk) || vk == 0)
					continue;

				if (!pending.TryGetValue(vk, out var codes))
					pending[vk] = codes = [];

				codes.Add(sc);
			}

			var map = new Dictionary<uint, uint[]>(pending.Count);

			foreach (var pair in pending)
				map[pair.Key] = [.. pair.Value];

			return map;
		}

#if LINUX
		// KEY_MAX from linux/input-event-codes.h. The highest code the table names (KEY_MODE, 0x175) is well
		// inside it, but scanning the whole range keeps the reverse map correct if one is ever added above it.
		private const uint MaxScanCode = 0x2FFu;

		private static bool TryMapScanCodeToVk(uint sc, out uint vk) => (vk = EvdevToVk(sc)) != 0;

		public static uint MapScToVk(uint sc)
		{
			return sc == 0 ? 0 : EvdevToVk(sc);
		}

		public static uint MapVkToSc(uint vk, bool returnSecondary = false)
		{
			if (vk == 0)
				return 0;

			// Match Windows' VK-to-SC contract for dual-state numpad keys: the non-extended
			// keypad key is primary and the dedicated navigation key is secondary. This lets
			// the shared AHK key-name logic distinguish NumpadUp from Up without Unix aliases.
			if (TryGetNumpadKeyPair(vk, out var navigationVk, out var keypadVk) && vk == navigationVk)
				return VkToEvdev(returnSecondary ? navigationVk : keypadVk);

			return VkToEvdev(vk, returnSecondary);
		}
#endif

#if OSX
		// kVK_UpArrow, the highest code the mapper knows; the same bound the provider's reverse map uses.
		private const uint MaxScanCode = 0x7Eu;

		private static bool TryMapScanCodeToVk(uint sc, out uint vk) => TryMapMacCodeToVk(sc, out vk);

		public static uint MapScToVk(uint sc)
		{
			if (sc == 0)
				return 0;

			return TryMapMacCodeToVk(sc, out var vk) ? vk : 0;
		}

		public static uint MapVkToSc(uint vk, bool returnSecondary = false)
		{
			if (vk == 0)
				return 0;

			// Match Windows' VK-to-SC contract for dual-state numpad keys: the keypad code is
			// primary and the dedicated navigation code is secondary.
			if (TryGetNumpadKeyPair(vk, out var navigationVk, out var keypadVk) && vk == navigationVk)
			{
				var scanVk = returnSecondary ? navigationVk : keypadVk;
				return TryMapVkToMacCode(scanVk, out var numpadSc) ? numpadSc : 0;
			}

			// Return and NumpadEnter also share a VK, with Return primary. A secondary request
			// for any other VK returns zero so callers can use it as the duplicate-SC test.
			if (returnSecondary)
				return vk == VirtualKeys.VK_RETURN ? 0x4Cu : 0u;

			return TryMapVkToMacCode(vk, out var sc) ? sc : 0;
		}
#endif
	}
}
#endif
